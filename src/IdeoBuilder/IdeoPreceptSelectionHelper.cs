using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Helpers for the base-precept (issue-based) editor: enumerating configurable issues,
    /// building the issue tree, building the per-issue value picker, and applying a chosen
    /// precept. Everything is sourced from the game's own defs and validated through the
    /// game's CanAddPreceptAllFactions / CanListPrecept so meme/precept restrictions are
    /// always respected.
    /// </summary>
    public static class IdeoPreceptSelectionHelper
    {
        // Section titles, also stored as each section node's Data so navigation can identify which
        // section ("active" vs "not set") an issue belongs to across a rebuild.
        public const string ActiveSectionTitle = "Active precepts";
        public const string NotSetSectionTitle = "Not set";

        /// <summary>Base (issue-based) precept defs the player can choose between.</summary>
        public static List<PreceptDef> BasePreceptDefs()
        {
            return DefDatabase<PreceptDef>.AllDefs
                .Where(d => d.preceptClass == typeof(Precept) && d.issue != null)
                .ToList();
        }

        /// <summary>
        /// The issues that should appear in the tree, in display order. An issue appears if it
        /// has at least one visible candidate precept, or the ideo already has a precept for it.
        /// </summary>
        public static List<IssueDef> ConfigurableIssues(Ideo ideo)
        {
            var baseDefs = BasePreceptDefs();
            var issues = baseDefs
                .Where(d => d.visible)
                .Select(d => d.issue)
                .Distinct()
                .ToList();

            // Include issues the ideo already has a precept for (defensive).
            foreach (var p in ideo.PreceptsListForReading)
            {
                if (p.def.preceptClass == typeof(Precept) && p.def.issue != null && !issues.Contains(p.def.issue))
                    issues.Add(p.def.issue);
            }

            return issues.OrderBy(i => i.LabelCap.RawText).ToList();
        }

        public static List<PreceptDef> CandidatesForIssue(IssueDef issue)
        {
            return BasePreceptDefs()
                .Where(d => d.issue == issue)
                .OrderBy(d => (int)d.impact)
                .ThenBy(d => d.LabelCap.RawText)
                .ToList();
        }

        public static List<Precept> CurrentPreceptsForIssue(Ideo ideo, IssueDef issue)
        {
            return ideo.PreceptsListForReading.Where(p => p.def.issue == issue).ToList();
        }

        #region Tree building

        public static InspectionTreeItem BuildTree(Ideo ideo)
        {
            var root = new InspectionTreeItem
            {
                Label = "Root",
                IndentLevel = -1,
                IsExpandable = true,
                IsExpanded = true,
                Type = InspectionTreeItem.ItemType.Category,
            };

            // Group issues into two sections: those the ideo has set a precept for ("active"),
            // and those still unset ("not set"). Active comes first so the player hears their
            // current choices before the long list of open issues.
            var issues = ConfigurableIssues(ideo);
            var active = issues.Where(i => CurrentPreceptsForIssue(ideo, i).Count > 0).ToList();
            var inactive = issues.Where(i => CurrentPreceptsForIssue(ideo, i).Count == 0).ToList();

            AddIssueSection(root, ideo, ActiveSectionTitle, active, expanded: true);
            AddIssueSection(root, ideo, NotSetSectionTitle, inactive, expanded: false);

            return root;
        }

        private static void AddIssueSection(InspectionTreeItem root, Ideo ideo, string title, List<IssueDef> issues, bool expanded)
        {
            if (issues.Count == 0) return;

            var section = new InspectionTreeItem
            {
                Label = title + ", " + issues.Count,
                IndentLevel = 0,
                IsExpandable = true,
                IsExpanded = expanded,
                Type = InspectionTreeItem.ItemType.Category,
                Data = title, // lets navigation identify the section across rebuilds
                Parent = root,
            };

            foreach (var issue in issues)
            {
                var current = CurrentPreceptsForIssue(ideo, issue);
                string shortLabel = BuildIssueLabel(issue, current);
                var detailLines = BuildIssueDetailLines(current);

                var issueNode = new InspectionTreeItem
                {
                    ExpandedLabel = shortLabel,
                    Label = detailLines.Count > 0 ? shortLabel + ". " + string.Join(". ", detailLines) : shortLabel,
                    IndentLevel = 1,
                    IsExpandable = true,
                    IsExpanded = false,
                    Type = InspectionTreeItem.ItemType.Category,
                    Data = issue,
                    Parent = section,
                };
                foreach (var line in detailLines)
                {
                    issueNode.Children.Add(new InspectionTreeItem
                    {
                        Label = line,
                        IndentLevel = 2,
                        IsExpandable = false,
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Parent = issueNode,
                    });
                }
                section.Children.Add(issueNode);
            }

            root.Children.Add(section);
        }

        public static string BuildIssueLabel(IssueDef issue, List<Precept> current)
        {
            string issueLabel = issue.LabelCap.ToString();
            if (current.Count == 0)
                return issueLabel + ": " + "None".Translate();

            // Use the precept DEF's label — that's the chosen value ("disgusting", "ugly", "don't
            // mind", "acceptable"). Precept.LabelCap resolves to the generated, issue-derived name
            // instead, which is why this previously read "Corpses: Corpses" and hid the real value.
            string value = string.Join(", ", current.Select(p => (string)p.def.LabelCap));
            // Defensive: if a def's label is the issue name itself, collapse "Issue: Issue" to just
            // "Issue"; otherwise read "Issue: value" (e.g. "Cannibalism: Acceptable").
            if (string.Equals(value.Trim(), issueLabel.Trim(), System.StringComparison.OrdinalIgnoreCase))
                return issueLabel;
            return issueLabel + ": " + value;
        }

        /// <summary>
        /// Detail lines for an issue node: each current precept's impact tier plus its full
        /// vanilla tip (description, prohibitions, mood/opinion effects, mental breaks, need
        /// changes, stat offsets) — cleaned of markup and grammar tokens, one line apiece.
        /// </summary>
        private static List<string> BuildIssueDetailLines(List<Precept> current)
        {
            var lines = new List<string>();
            foreach (var precept in current)
            {
                lines.Add("IdeoImpact".Translate() + ": " + ImpactLabel(precept.def.impact));
                string tip = precept.GetTip();
                if (string.IsNullOrEmpty(tip)) continue;
                foreach (var raw in tip.Split('\n'))
                {
                    string line = IdeoBuilderHelper.CleanGameText(raw);
                    if (!string.IsNullOrEmpty(line))
                        lines.Add(line);
                }
            }
            return lines;
        }

        public static string ImpactLabel(PreceptImpact impact)
        {
            // PreceptImpact maps to the same impact label keys (1=Low,2=Medium,3=High).
            return IdeoImpactUtility.MemeImpactLabel((int)impact + 1).ToString().CapitalizeFirst();
        }

        #endregion

        #region Value picker

        /// <summary>
        /// Builds the float-menu options for choosing a precept for an issue. Available precepts
        /// get a working action; unavailable ones are listed with their disable reason and no
        /// action (so the player knows they exist but can't be set). The current precept is
        /// marked. For multi-precept issues the action toggles; otherwise it replaces.
        /// </summary>
        public static List<FloatMenuOption> BuildValuePickerOptions(Ideo ideo, IssueDef issue, System.Action onChanged)
        {
            var options = new List<FloatMenuOption>();
            var current = CurrentPreceptsForIssue(ideo, issue);
            bool allowMultiple = issue.allowMultiplePrecepts;

            foreach (var def in CandidatesForIssue(issue))
            {
                var listReport = IdeoUIUtility.CanListPrecept(ideo, def, IdeoEditMode.GameStart);
                // If not listable and there's no reason text, vanilla hides it entirely.
                if (!listReport.Accepted && string.IsNullOrWhiteSpace(listReport.Reason))
                    continue;

                bool isCurrent = current.Any(p => p.def == def);
                var addReport = ideo.CanAddPreceptAllFactions(def);
                bool available = addReport.Accepted;

                // Lead with the name and status, then impact, then the requirement/lock reason
                // (e.g. "requires meme individualist"), and finally the description — so the most
                // actionable information comes before the long blurb.
                var sb = new StringBuilder();
                sb.Append((string)def.LabelCap);
                if (isCurrent)
                    sb.Append(", current");
                else if (!available)
                    sb.Append(", ").Append("Unavailable");
                sb.Append(". ").Append("IdeoImpact".Translate()).Append(": ").Append(ImpactLabel(def.impact));
                if (!isCurrent && !available && !string.IsNullOrWhiteSpace(addReport.Reason))
                    sb.Append(". ").Append(IdeoBuilderHelper.CleanGameText(addReport.Reason));
                if (!string.IsNullOrEmpty(def.description))
                    sb.Append(". ").Append(IdeoBuilderHelper.CleanGameText(def.description));

                var captured = def;
                if (isCurrent && allowMultiple)
                {
                    // Toggle off
                    options.Add(new FloatMenuOption(sb.ToString(), () =>
                    {
                        RemovePreceptsOfDef(ideo, issue, captured);
                        onChanged?.Invoke();
                    }));
                }
                else if (isCurrent && !allowMultiple)
                {
                    // Already the single selection; selecting again is a no-op but allowed.
                    options.Add(new FloatMenuOption(sb.ToString(), () => onChanged?.Invoke()));
                }
                else if (!available)
                {
                    // Unavailable — listed with its reason but no action.
                    options.Add(new FloatMenuOption(sb.ToString(), null));
                }
                else
                {
                    options.Add(new FloatMenuOption(sb.ToString(), () =>
                    {
                        SetPrecept(ideo, issue, captured, allowMultiple);
                        onChanged?.Invoke();
                    }));
                }
            }

            if (options.Count == 0)
                options.Add(new FloatMenuOption("NoChoicesAvailable".Translate(), null));

            return options;
        }

        public static void SetPrecept(Ideo ideo, IssueDef issue, PreceptDef def, bool allowMultiple)
        {
            if (!allowMultiple)
            {
                // Replace any existing precepts for this issue.
                foreach (var p in CurrentPreceptsForIssue(ideo, issue).ToList())
                    ideo.RemovePrecept(p, replacing: true);
            }
            var precept = PreceptMaker.MakePrecept(def);
            ideo.AddPrecept(precept, init: true);
            ideo.anyPreceptEdited = true;
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        public static void RemovePreceptsOfDef(Ideo ideo, IssueDef issue, PreceptDef def)
        {
            foreach (var p in CurrentPreceptsForIssue(ideo, issue).Where(p => p.def == def).ToList())
                ideo.RemovePrecept(p);
            ideo.anyPreceptEdited = true;
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        #endregion
    }
}
