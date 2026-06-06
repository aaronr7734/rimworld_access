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
            treeNav.OnDelete = HandleDelete;
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

        /// <summary>
        /// Delete removes the focused issue's current precept, mirroring vanilla's per-precept
        /// "Remove" option in Precept.DrawPreceptBox: only precepts whose def has canRemoveInUI,
        /// whose issue has no default precept, and that aren't required by a meme can be removed.
        /// Keeps Delete consistent with the typed-precept lists (roles, rituals, etc.). After a
        /// successful removal the issue drops to the "Not set" section; OnPreceptChanged repositions
        /// the cursor and speaks the "{issue}: None, removed" confirmation.
        /// </summary>
        private static bool HandleDelete(InspectionTreeItem item)
        {
            var issue = ResolveIssue(item);
            if (issue == null) return false;

            var current = IdeoPreceptSelectionHelper.CurrentPreceptsForIssue(ideo, issue);
            if (current.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData(IdeoPreceptSelectionHelper.BuildIssueLabel(issue, current), SpeechPriority.High);
                return true;
            }

            var removable = current.Where(CanRemovePrecept).ToList();
            if (removable.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData(RemovalBlockedReason(current[0]), SpeechPriority.High);
                return true;
            }

            foreach (var precept in removable)
                ideo.RemovePrecept(precept);
            ideo.anyPreceptEdited = true;
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();

            // Reuse the post-change refresh/reposition/announce path used by the value picker.
            OnPreceptChanged(issue);
            return true;
        }

        /// <summary>Vanilla's removal guard (Precept.DrawPreceptBox): removable in the UI, the issue
        /// has no mandatory default precept, and no meme requires the precept.</summary>
        private static bool CanRemovePrecept(Precept precept)
        {
            return precept.def.canRemoveInUI
                && !precept.def.issue.HasDefaultPrecept
                && ideo.GetMemeThatRequiresPrecept(precept.def) == null;
        }

        private static string RemovalBlockedReason(Precept precept)
        {
            var requiringMeme = ideo.GetMemeThatRequiresPrecept(precept.def);
            if (requiringMeme != null)
                return "CannotRemove".Translate() + ": " + "RequiredByMeme".Translate(requiringMeme.label);
            return "CannotRemove".Translate() + ": " + IdeoBuilderHelper.PreceptLabel(precept);
        }

        #endregion

        #region Value picker

        private static void OpenValuePicker(IssueDef issue)
        {
            var options = IdeoPreceptSelectionHelper.BuildValuePickerOptions(ideo, issue, () => OnPreceptChanged(issue));
            TolkHelper.SpeakData(issue.LabelCap);
            // Suppress the float menu's generic "{full option label} selected" echo — the verbose
            // option text (value, impact, description) is too much on commit. OnPreceptChanged
            // speaks a concise "{issue}: {value}, selected" confirmation instead.
            WindowlessFloatMenuState.Open(options, colonistOrders: false, announceSelection: false);
        }

        private static void OnPreceptChanged(IssueDef issue)
        {
            // Capture where we were, in the OLD tree, before the rebuild.
            var oldRoot = treeNav.RootItem;
            var oldIssueNode = FindIssueNode(oldRoot, issue);
            var oldSection = SectionOf(oldIssueNode);
            string oldSectionId = oldSection?.Data as string;
            int posInOldSection = (oldSection != null && oldIssueNode != null)
                ? oldSection.Children.IndexOf(oldIssueNode) : -1;
            bool activeExpanded = FindSection(oldRoot, IdeoPreceptSelectionHelper.ActiveSectionTitle)?.IsExpanded ?? true;
            bool notSetExpanded = FindSection(oldRoot, IdeoPreceptSelectionHelper.NotSetSectionTitle)?.IsExpanded ?? false;

            ideo.RegenerateDescription();
            RebuildTree();

            // The rebuild resets section expansion to defaults (Not set collapsed); restore what the
            // player had open so their place in the list survives.
            ApplySectionExpansion(IdeoPreceptSelectionHelper.ActiveSectionTitle, activeExpanded);
            ApplySectionExpansion(IdeoPreceptSelectionHelper.NotSetSectionTitle, notSetExpanded);
            treeNav.RebuildVisibleList();

            var newIssueNode = FindIssueNode(treeNav.RootItem, issue);
            string newSectionId = SectionOf(newIssueNode)?.Data as string;

            InspectionTreeItem target;
            if (oldSectionId != null && newSectionId != null && oldSectionId != newSectionId)
            {
                // The issue jumped sections (e.g. a "Not set" issue that is now active). Stay in the
                // original section, on whatever now occupies the slot it vacated (i.e. the issue that
                // followed it) rather than chasing it into its new section.
                var origSection = FindSection(treeNav.RootItem, oldSectionId);
                if (origSection != null && origSection.Children.Count > 0)
                    target = origSection.Children[Mathf.Clamp(posInOldSection, 0, origSection.Children.Count - 1)];
                else
                    target = origSection ?? newIssueNode; // section emptied — its header, or fall back
            }
            else
            {
                // Same section (e.g. changed an already-set precept): stay on the changed issue.
                target = newIssueNode;
            }

            FocusNode(target);
            AnnouncePreceptSet(issue);
        }

        private static InspectionTreeItem FindIssueNode(InspectionTreeItem root, IssueDef issue)
        {
            if (root == null) return null;
            foreach (var child in root.Children)
            {
                if (child.Data is IssueDef d && d == issue) return child;
                var found = FindIssueNode(child, issue);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>The section node (direct child of root) an item lives under, or null.</summary>
        private static InspectionTreeItem SectionOf(InspectionTreeItem item)
        {
            var root = treeNav.RootItem;
            var cur = item;
            while (cur != null && cur.Parent != null && cur.Parent != root)
                cur = cur.Parent;
            return (cur != null && cur.Parent == root) ? cur : null;
        }

        private static InspectionTreeItem FindSection(InspectionTreeItem root, string id)
        {
            return root?.Children.FirstOrDefault(c => (c.Data as string) == id);
        }

        private static void ApplySectionExpansion(string id, bool expanded)
        {
            var s = FindSection(treeNav.RootItem, id);
            if (s != null) s.IsExpanded = expanded;
        }

        private static int IndexInVisible(InspectionTreeItem node)
        {
            var items = treeNav.VisibleItems;
            for (int i = 0; i < items.Count; i++)
                if (items[i] == node) return i;
            return -1;
        }

        private static void FocusNode(InspectionTreeItem target)
        {
            if (target == null) return;
            int idx = IndexInVisible(target);
            // If the target isn't directly visible, climb to its nearest visible ancestor.
            var t = target.Parent;
            while (idx < 0 && t != null) { idx = IndexInVisible(t); t = t.Parent; }
            if (idx < 0) idx = 0;
            treeNav.SetSelectedIndex(idx);
            // We never actually left the section, so don't let the next Up/Down re-announce it.
            treeNav.MarkCurrentParentAsAnnounced();
            // The cursor is positioned silently; OnPreceptChanged speaks the concise commit
            // confirmation (the precept that was just set), which is the meaningful feedback.
        }

        /// <summary>
        /// Commit feedback: "{issue}: {value}, selected" (or ", removed"), then — when the cursor
        /// moved on to a different issue — the item now under the cursor, as a single announcement
        /// so the confirmation is heard first and isn't cut off.
        /// </summary>
        private static void AnnouncePreceptSet(IssueDef issue)
        {
            var current = IdeoPreceptSelectionHelper.CurrentPreceptsForIssue(ideo, issue);
            var sb = new StringBuilder(IdeoPreceptSelectionHelper.BuildIssueLabel(issue, current));
            sb.Append(current.Count > 0 ? ", selected" : ", removed");

            var sel = treeNav.SelectedItem;
            if (sel != null && !ReferenceEquals(sel.Data, issue))
                sb.Append(". ").Append(FormatItem(sel));

            TolkHelper.SpeakData(sb.ToString(), SpeechPriority.High);
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
            TolkHelper.Speak("CustomizeIdeoligion".Loc());
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
                // In submenu mode the section header ("Active precepts") is hidden, so announce the
                // section the cursor starts in up front — before the first precept — instead of
                // having it pop up redundantly on the first Down arrow.
                var parent = first.Parent;
                if (parent != null && parent != treeNav.RootItem)
                {
                    string parentLabel = !string.IsNullOrEmpty(parent.ExpandedLabel) ? parent.ExpandedLabel : parent.Label;
                    sb.Append(". ").Append(parentLabel);
                }
                sb.Append(". ").Append(first.Label);
                if (first.IsExpandable)
                    sb.Append(first.IsExpanded ? ", expanded" : ", collapsed");
            }

            TolkHelper.SpeakData(sb.ToString(), SpeechPriority.High);
            // Mark the starting section as already announced so the first arrow doesn't repeat it.
            treeNav.MarkCurrentParentAsAnnounced();
        }

        #endregion
    }
}
