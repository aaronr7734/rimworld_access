using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Windowless overlay for editing an ideoligion's base (issue-based) precepts. Opened from
    /// the builder hub. Presents a tree of issues; each issue node shows its current precept
    /// value, and expands to reveal the precept's description and impact.
    ///
    /// Keys:
    ///   Up/Down/Home/End/Left/Right — tree navigation (Right expands an issue to read details)
    ///   Enter — open the value picker (a windowless float menu) for the focused issue
    ///   Space — re-announce
    ///   A-Z / 0-9 — typeahead by issue label
    ///   Escape — close and return to the hub
    ///
    /// This is not tied to a game Window — it lives on top of Page_ConfigureIdeo and is routed
    /// through IdeoBuilderHubPatch's DoWindowContents prefix. The value picker uses
    /// WindowlessFloatMenuState, which is routed by UnifiedKeyboardPatch.
    /// </summary>
    public static class IdeoPreceptSelectionState
    {
        public static bool IsActive { get; private set; }

        private static Ideo ideo;
        private static TreeNavigationHelper treeNav = new TreeNavigationHelper("IdeoPreceptTree");
        private static bool configured;

        public static void Open(Ideo targetIdeo)
        {
            if (targetIdeo == null) return;
            ideo = targetIdeo;
            IsActive = true;
            EnsureConfigured();
            RebuildTree();
            AnnounceOpening();
        }

        public static void Close()
        {
            IsActive = false;
            ideo = null;
            treeNav.Reset();
        }

        private static void EnsureConfigured()
        {
            if (configured) return;
            configured = true;
            treeNav.AnnounceChildCounts = false;
            treeNav.FormatItemAnnouncement = FormatItem;
            treeNav.FormatStateChangeAnnouncement = FormatStateChange;
            treeNav.FormatSearchAnnouncement = FormatSearch;
            treeNav.OnActivate = HandleActivate;
        }

        public static void RebuildTree()
        {
            if (ideo == null) return;
            var root = IdeoPreceptSelectionHelper.BuildTree(ideo);
            treeNav.Initialize(root);
        }

        #region Formatters / activation

        private static string FormatItem(InspectionTreeItem item)
        {
            // Detail lines read as just their text.
            if (item.Type == InspectionTreeItem.ItemType.DetailText)
                return item.Label;

            var sb = new StringBuilder();
            // Smart label: an expanded issue reads its short "Issue: value" form (details are now
            // child lines); collapsed reads the full inline details.
            sb.Append(item.IsExpandable && item.IsExpanded && !string.IsNullOrEmpty(item.ExpandedLabel)
                ? item.ExpandedLabel : item.Label);

            if (item.IsExpandable)
                sb.Append(item.IsExpanded ? ", expanded" : ", collapsed");

            var (pos, total) = treeNav.GetSiblingPosition(item);
            string position = MenuHelper.FormatPosition(pos - 1, total);
            if (!string.IsNullOrEmpty(position))
                sb.Append(". ").Append(position);

            string levelSuffix = MenuHelper.GetLevelSuffix("IdeoPreceptTree", item.IndentLevel);
            if (!string.IsNullOrEmpty(levelSuffix))
                sb.Append(levelSuffix);

            return sb.ToString();
        }

        private static string FormatStateChange(InspectionTreeItem item)
        {
            string state = item.IsExpanded ? "Expanded" : "Collapsed";
            string label = !string.IsNullOrEmpty(item.ExpandedLabel) ? item.ExpandedLabel : item.Label;
            return state + ". " + label;
        }

        private static string FormatSearch(InspectionTreeItem item, TypeaheadSearchHelper t)
        {
            string label = !string.IsNullOrEmpty(item.ExpandedLabel) ? item.ExpandedLabel : item.Label;
            return $"{label}, {t.CurrentMatchPosition} of {t.MatchCount} matches for '{t.SearchBuffer}'";
        }

        private static bool HandleActivate(InspectionTreeItem item)
        {
            // Enter on an issue node opens the value picker. Enter on a detail line re-announces.
            var issue = ResolveIssue(item);
            if (issue != null)
            {
                OpenValuePicker(issue);
                return true;
            }
            return false;
        }

        private static IssueDef ResolveIssue(InspectionTreeItem item)
        {
            var cur = item;
            while (cur != null)
            {
                if (cur.Data is IssueDef issue)
                    return issue;
                cur = cur.Parent;
            }
            return null;
        }

        #endregion

        #region Value picker

        private static void OpenValuePicker(IssueDef issue)
        {
            var options = IdeoPreceptSelectionHelper.BuildValuePickerOptions(ideo, issue, () => OnPreceptChanged(issue));
            TolkHelper.Speak(issue.LabelCap);
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        private static void OnPreceptChanged(IssueDef issue)
        {
            ideo.RegenerateDescription();
            RebuildTree();
            // Refocus the changed issue.
            for (int i = 0; i < treeNav.VisibleItems.Count; i++)
            {
                if (treeNav.VisibleItems[i].Data == issue)
                {
                    treeNav.SetSelectedIndex(i);
                    TolkHelper.Speak(treeNav.SelectedItem.Label);
                    return;
                }
            }
        }

        #endregion

        #region Input

        public static bool HandleInput(Event ev)
        {
            if (ev.type != EventType.KeyDown) return false;

            KeyCode key = ev.keyCode;
            bool ctrl = ev.control;
            bool alt = KeyboardHelper.IsAltHeld;

            // Escape — clear search, else close
            if (key == KeyCode.Escape && !alt && !ctrl)
            {
                if (treeNav.HasActiveSearch)
                {
                    treeNav.Typeahead.ClearSearchAndAnnounce();
                    treeNav.ReannounceCurrentItem();
                    return true;
                }
                CloseAndAnnounce();
                return true;
            }

            return treeNav.HandleInput(ev);
        }

        private static void CloseAndAnnounce()
        {
            Close();
            SoundDefOf.TabClose.PlayOneShotOnCamera();
            TolkHelper.Speak("CustomizeIdeoligion".Translate());
        }

        #endregion

        #region Announcement

        private static void AnnounceOpening()
        {
            var sb = new StringBuilder();
            sb.Append("Precepts".Translate());
            // Clarify the count: how many issues are set vs still open (the hub's "precepts N"
            // counts the set ones, which previously read as a confusing bare number).
            var issues = IdeoPreceptSelectionHelper.ConfigurableIssues(ideo);
            int activeCount = issues.Count(i => IdeoPreceptSelectionHelper.CurrentPreceptsForIssue(ideo, i).Count > 0);
            sb.Append(". ").Append(activeCount).Append(" active, ").Append(issues.Count - activeCount).Append(" not set");

            if (treeNav.Count > 0)
            {
                var first = treeNav.VisibleItems[0];
                sb.Append(". ").Append(first.Label);
                if (first.IsExpandable)
                    sb.Append(first.IsExpanded ? ", expanded" : ", collapsed");
            }

            TolkHelper.Speak(sb.ToString(), SpeechPriority.High);
        }

        #endregion
    }
}
