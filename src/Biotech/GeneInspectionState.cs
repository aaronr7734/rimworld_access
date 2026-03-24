using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation state for baby gene inspection (ITab_GenesPregnancy).
    /// Provides tree-based navigation through genes and their details.
    /// </summary>
    public static class GeneInspectionState
    {
        public static bool IsActive { get; private set; } = false;

        private static Pawn currentPawn = null;
        private static HediffWithParents currentPregnancy = null;
        private static GeneSetHolderBase currentHolder = null;
        private static InspectionTreeItem rootItem = null;
        private static List<InspectionTreeItem> visibleItems = null;
        private static int selectedIndex = 0;

        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
        public static TypeaheadSearchHelper Typeahead => typeahead;

        /// <summary>
        /// Opens the gene inspection accessibility state for a pregnant pawn.
        /// </summary>
        /// <param name="pawn">The pregnant pawn to inspect</param>
        public static void Open(Pawn pawn)
        {
            try
            {
                if (pawn == null)
                    return;

                // Find the pregnancy hediff
                var pregnancy = pawn.health?.hediffSet?.hediffs
                    .OfType<HediffWithParents>()
                    .FirstOrDefault();

                if (pregnancy == null || pregnancy.geneSet == null)
                {
                    TolkHelper.Speak("No pregnancy found.", SpeechPriority.High);
                    return;
                }

                currentPawn = pawn;
                currentPregnancy = pregnancy;
                IsActive = true;

                // Get parent names for context
                string motherName = pregnancy.Mother?.LabelShort;
                string fatherName = pregnancy.Father?.LabelShort;

                // Build the tree
                rootItem = GeneTreeBuilder.BuildTree(pregnancy.geneSet, motherName, fatherName);
                RebuildVisibleList();
                selectedIndex = 0;

                MenuHelper.ResetLevel("GeneInspection");
                typeahead.ClearSearch();

                SoundDefOf.TabOpen.PlayOneShotOnCamera();
                AnnounceOpening();
            }
            catch (Exception ex)
            {
                Log.Error($"[GeneInspectionState] Error opening: {ex.Message}");
                Close();
            }
        }

        /// <summary>
        /// Opens the gene inspection accessibility state for a GeneSetHolderBase item
        /// (embryo, genepack, or xenogerm).
        /// </summary>
        public static void OpenForGeneSetHolder(GeneSetHolderBase holder)
        {
            try
            {
                if (holder == null || holder.GeneSet == null)
                    return;

                currentPawn = null;
                currentPregnancy = null;
                currentHolder = holder;
                IsActive = true;

                // Get parent names for embryos
                string motherName = null;
                string fatherName = null;
                if (holder is HumanEmbryo embryo)
                {
                    try
                    {
                        motherName = embryo.Mother?.LabelShort;
                        fatherName = embryo.Father?.LabelShort;
                    }
                    catch { /* CompHasPawnSources may not be available */ }
                }

                // Build the tree
                rootItem = GeneTreeBuilder.BuildTree(holder.GeneSet, motherName, fatherName);

                // Override root label based on item type
                int geneCount = holder.GeneSet.GenesListForReading?.Count ?? 0;
                string countStr = $"({geneCount} {(geneCount == 1 ? "gene" : "genes")})";
                string xenotype = holder.GeneSet.Label;
                bool hasXenotype = !string.IsNullOrEmpty(xenotype) && xenotype != "ERR";

                if (holder is HumanEmbryo)
                    rootItem.Label = hasXenotype ? $"Embryo Genes: {xenotype} {countStr}" : $"Embryo Genes {countStr}";
                else if (holder is Xenogerm xg && !string.IsNullOrEmpty(xg.xenotypeName))
                    rootItem.Label = $"Xenogerm Genes: {xg.xenotypeName} {countStr}";
                else if (holder is Genepack)
                    rootItem.Label = hasXenotype ? $"Genepack: {xenotype} {countStr}" : $"Genepack Genes {countStr}";
                else
                    rootItem.Label = hasXenotype ? $"Genes: {xenotype} {countStr}" : $"Genes {countStr}";

                RebuildVisibleList();
                selectedIndex = 0;

                MenuHelper.ResetLevel("GeneInspection");
                typeahead.ClearSearch();

                SoundDefOf.TabOpen.PlayOneShotOnCamera();
                AnnounceOpening();
            }
            catch (Exception ex)
            {
                Log.Error($"[GeneInspectionState] Error opening for GeneSetHolder: {ex.Message}");
                Close();
            }
        }

        /// <summary>
        /// Closes the gene inspection accessibility state.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
            currentPawn = null;
            currentPregnancy = null;
            currentHolder = null;
            rootItem = null;
            visibleItems = null;
            selectedIndex = 0;
            typeahead.ClearSearch();
            MenuHelper.ResetLevel("GeneInspection");
        }

        /// <summary>
        /// Selects the next item (Down arrow).
        /// </summary>
        public static void SelectNext()
        {
            if (!IsActive || visibleItems == null || visibleItems.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, visibleItems.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Selects the previous item (Up arrow).
        /// </summary>
        public static void SelectPrevious()
        {
            if (!IsActive || visibleItems == null || visibleItems.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, visibleItems.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Jumps to the next gene header (Page Down).
        /// </summary>
        public static void JumpToNextGene()
        {
            if (!IsActive || visibleItems == null || visibleItems.Count == 0)
                return;

            typeahead.ClearSearch();

            // Search forward from current position for next gene (Item type with GeneDef data)
            for (int i = selectedIndex + 1; i < visibleItems.Count; i++)
            {
                var item = visibleItems[i];
                if (item.Type == InspectionTreeItem.ItemType.Item && item.Data is GeneDef)
                {
                    selectedIndex = i;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    return;
                }
                // Also stop at SubCategory (like "Biostats Summary")
                if (item.Type == InspectionTreeItem.ItemType.SubCategory)
                {
                    selectedIndex = i;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    return;
                }
            }

            // Wrap to beginning (if enabled)
            if (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true)
            {
                for (int i = 0; i <= selectedIndex; i++)
                {
                    var item = visibleItems[i];
                    if ((item.Type == InspectionTreeItem.ItemType.Item && item.Data is GeneDef) ||
                        item.Type == InspectionTreeItem.ItemType.SubCategory)
                    {
                        selectedIndex = i;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceCurrentSelection();
                        return;
                    }
                }
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Jumps to the previous gene header (Page Up).
        /// </summary>
        public static void JumpToPreviousGene()
        {
            if (!IsActive || visibleItems == null || visibleItems.Count == 0)
                return;

            typeahead.ClearSearch();

            // Search backward from current position
            for (int i = selectedIndex - 1; i >= 0; i--)
            {
                var item = visibleItems[i];
                if ((item.Type == InspectionTreeItem.ItemType.Item && item.Data is GeneDef) ||
                    item.Type == InspectionTreeItem.ItemType.SubCategory)
                {
                    selectedIndex = i;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    return;
                }
            }

            // Wrap to end (if enabled)
            if (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true)
            {
                for (int i = visibleItems.Count - 1; i >= selectedIndex; i--)
                {
                    var item = visibleItems[i];
                    if ((item.Type == InspectionTreeItem.ItemType.Item && item.Data is GeneDef) ||
                        item.Type == InspectionTreeItem.ItemType.SubCategory)
                    {
                        selectedIndex = i;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceCurrentSelection();
                        return;
                    }
                }
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Jumps to the first sibling at the same indent level (Home key).
        /// </summary>
        private static void JumpToFirst()
        {
            if (visibleItems == null || visibleItems.Count == 0) return;
            MenuHelper.HandleTreeHomeKey(visibleItems, ref selectedIndex, item => item.IndentLevel, false, ClearAndAnnounce);
        }

        /// <summary>
        /// Jumps to the last item in the current scope (End key).
        /// </summary>
        private static void JumpToLast()
        {
            if (visibleItems == null || visibleItems.Count == 0) return;
            MenuHelper.HandleTreeEndKey(visibleItems, ref selectedIndex, item => item.IndentLevel,
                item => item.IsExpanded, item => item.Children != null && item.Children.Count > 0, false, ClearAndAnnounce);
        }

        /// <summary>
        /// Jumps to the absolute first item in the entire tree (Ctrl+Home).
        /// </summary>
        private static void JumpToAbsoluteFirst()
        {
            if (visibleItems == null || visibleItems.Count == 0) return;
            MenuHelper.HandleTreeHomeKey(visibleItems, ref selectedIndex, item => item.IndentLevel, true, ClearAndAnnounce);
        }

        /// <summary>
        /// Jumps to the absolute last item in the entire tree (Ctrl+End).
        /// </summary>
        private static void JumpToAbsoluteLast()
        {
            if (visibleItems == null || visibleItems.Count == 0) return;
            MenuHelper.HandleTreeEndKey(visibleItems, ref selectedIndex, item => item.IndentLevel,
                item => item.IsExpanded, item => item.Children != null && item.Children.Count > 0, true, ClearAndAnnounce);
        }

        /// <summary>
        /// Clears typeahead search and announces the current selection.
        /// </summary>
        private static void ClearAndAnnounce()
        {
            typeahead?.ClearSearch();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Expands the selected item (Right arrow).
        /// WCAG behavior:
        /// - On closed node: Open node, focus stays on current item
        /// - On open node: Move to first child
        /// - On end node: Reject sound
        /// </summary>
        public static void Expand()
        {
            if (!IsActive || visibleItems == null || selectedIndex >= visibleItems.Count)
                return;

            typeahead.ClearSearch();
            var item = visibleItems[selectedIndex];

            // End node (not expandable) - reject
            if (!item.IsExpandable)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Cannot expand this item.", SpeechPriority.High);
                return;
            }

            // Already expanded - move to first child
            if (item.IsExpanded)
            {
                MoveToFirstChild();
                return;
            }

            // Collapsed node - expand it
            if (item.OnActivate != null && item.Children.Count == 0)
            {
                item.OnActivate();
            }

            if (item.Children.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No items to show.");
                return;
            }

            item.IsExpanded = true;
            RebuildVisibleList();
            SoundDefOf.Click.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Expands all sibling categories at the same level as the current item.
        /// WCAG tree view pattern: * key expands all siblings.
        /// </summary>
        public static void ExpandAllSiblings()
        {
            if (!IsActive || visibleItems == null || selectedIndex >= visibleItems.Count)
                return;

            typeahead.ClearSearch();

            var currentItem = visibleItems[selectedIndex];

            // Get siblings - items with the same parent
            List<InspectionTreeItem> siblings;
            if (currentItem.Parent == null || currentItem.Parent == rootItem)
            {
                siblings = rootItem.Children;
            }
            else
            {
                siblings = currentItem.Parent.Children;
            }

            // Find all collapsed sibling nodes that can be expanded
            var collapsedSiblings = new List<InspectionTreeItem>();
            foreach (var sibling in siblings)
            {
                if (sibling.IsExpandable && !sibling.IsExpanded)
                {
                    collapsedSiblings.Add(sibling);
                }
            }

            // Check if there are any expandable items at this level at all
            bool hasExpandableItems = siblings.Any(s => s.IsExpandable);

            if (!hasExpandableItems)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No items to expand at this level.");
                return;
            }

            if (collapsedSiblings.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("All items already expanded at this level.");
                return;
            }

            // Expand all collapsed siblings
            int expandedCount = 0;
            foreach (var sibling in collapsedSiblings)
            {
                // Trigger lazy loading if needed
                if (sibling.OnActivate != null && sibling.Children.Count == 0)
                {
                    sibling.OnActivate();
                }

                if (sibling.Children.Count > 0)
                {
                    sibling.IsExpanded = true;
                    expandedCount++;
                }
            }

            RebuildVisibleList();

            if (expandedCount == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No items to expand at this level.");
            }
            else
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                string itemWord = expandedCount == 1 ? "gene" : "genes";
                TolkHelper.Speak($"Expanded {expandedCount} {itemWord}.");
            }
        }

        /// <summary>
        /// Collapses the selected item (Left arrow).
        /// WCAG behavior:
        /// - On open node: Close node, focus stays on current item
        /// - On closed node: Move to parent
        /// - On end node: Move to parent
        /// </summary>
        public static void Collapse()
        {
            if (!IsActive || visibleItems == null || selectedIndex >= visibleItems.Count)
                return;

            typeahead.ClearSearch();
            var item = visibleItems[selectedIndex];

            // Case 1: Item is expandable and expanded - collapse it
            if (item.IsExpandable && item.IsExpanded)
            {
                item.IsExpanded = false;

                // Restore rich label for gene nodes (Description stores the rich collapsed label)
                if (item.Data is GeneDef && !string.IsNullOrEmpty(item.Description))
                {
                    item.Label = item.Description;
                }
                RebuildVisibleList();

                if (selectedIndex >= visibleItems.Count)
                    selectedIndex = Math.Max(0, visibleItems.Count - 1);

                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentSelection();
                return;
            }

            // Case 2: Move to parent
            MoveToParent();
        }

        /// <summary>
        /// Activates the selected item (Enter key).
        /// For expandable items: expands them.
        /// For gene items: could open InfoCard (future enhancement).
        /// </summary>
        public static void ActivateItem()
        {
            if (!IsActive || visibleItems == null || selectedIndex >= visibleItems.Count)
                return;

            var item = visibleItems[selectedIndex];

            // Handle expandable items
            if (item.IsExpandable)
            {
                if (!item.IsExpanded)
                {
                    Expand();
                }
                else
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak("Already expanded.");
                }
                return;
            }

            // For gene items, could open InfoCard
            if (item.Data is GeneDef gene)
            {
                // Open InfoCard for this gene
                Find.WindowStack.Add(new Dialog_InfoCard(gene));
                SoundDefOf.Click.PlayOneShotOnCamera();
                return;
            }

            // Otherwise, nothing to do
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("No action available for this item.");
        }

        /// <summary>
        /// Closes the gene inspection (Escape key).
        /// </summary>
        public static void CloseInspection()
        {
            if (!IsActive)
                return;

            // Also close the visual tab if open
            if (currentPawn != null)
            {
                var pane = Find.MainTabsRoot?.OpenTab?.TabWindow as MainTabWindow_Inspect;
                if (pane != null)
                {
                    // Close the genes tab
                    pane.CloseOpenTab();
                }
            }

            Close();
            SoundDefOf.Click.PlayOneShotOnCamera();
            TolkHelper.Speak("Gene inspection closed.");
        }

        /// <summary>
        /// Handles keyboard input for gene inspection.
        /// Returns true if input was handled.
        /// Called from UnifiedKeyboardPatch which handles Event.current.Use().
        /// </summary>
        public static bool HandleInput(Event ev)
        {
            if (!IsActive || ev.type != EventType.KeyDown)
                return false;

            KeyCode key = ev.keyCode;

            // Escape - close
            if (key == KeyCode.Escape)
            {
                CloseInspection();
                return true;
            }

            // Up arrow - previous item
            if (key == KeyCode.UpArrow)
            {
                SelectPrevious();
                return true;
            }

            // Down arrow - next item
            if (key == KeyCode.DownArrow)
            {
                SelectNext();
                return true;
            }

            // Right arrow - expand
            if (key == KeyCode.RightArrow)
            {
                Expand();
                return true;
            }

            // Left arrow - collapse
            if (key == KeyCode.LeftArrow)
            {
                Collapse();
                return true;
            }

            // Page Down - jump to next gene
            if (key == KeyCode.PageDown)
            {
                JumpToNextGene();
                return true;
            }

            // Page Up - jump to previous gene
            if (key == KeyCode.PageUp)
            {
                JumpToPreviousGene();
                return true;
            }

            // Home - jump to first (Ctrl+Home for absolute first)
            if (key == KeyCode.Home)
            {
                if (ev.control)
                    JumpToAbsoluteFirst();
                else
                    JumpToFirst();
                return true;
            }

            // End - jump to last (Ctrl+End for absolute last)
            if (key == KeyCode.End)
            {
                if (ev.control)
                    JumpToAbsoluteLast();
                else
                    JumpToLast();
                return true;
            }

            // Asterisk (*) - expand all sibling categories (WCAG tree view pattern)
            bool isStar = key == KeyCode.KeypadMultiply || (ev.shift && key == KeyCode.Alpha8);
            if (isStar)
            {
                ExpandAllSiblings();
                return true;
            }

            // Enter - activate
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                ActivateItem();
                return true;
            }

            // Typeahead search - alphanumeric keys
            if (ev.character != '\0' && char.IsLetterOrDigit(ev.character))
            {
                HandleTypeahead(ev.character);
                return true;
            }

            // Backspace - clear search
            if (key == KeyCode.Backspace && typeahead.HasActiveSearch)
            {
                typeahead.ClearSearch();
                SoundDefOf.Click.PlayOneShotOnCamera();
                TolkHelper.Speak("Search cleared.");
                return true;
            }

            return false;
        }

        #region Private Methods

        /// <summary>
        /// Rebuilds the visible items list from the tree.
        /// </summary>
        private static void RebuildVisibleList()
        {
            visibleItems = new List<InspectionTreeItem>();
            if (rootItem != null)
            {
                CollectVisibleItems(rootItem, visibleItems);
            }
        }

        /// <summary>
        /// Recursively collects visible items from the tree.
        /// </summary>
        private static void CollectVisibleItems(InspectionTreeItem item, List<InspectionTreeItem> list)
        {
            // Skip root item (it's just a container)
            if (item.IndentLevel >= 0)
            {
                list.Add(item);
            }

            // Add children if expanded (or if it's the root)
            if (item.IsExpanded || item.IndentLevel < 0)
            {
                foreach (var child in item.Children)
                {
                    CollectVisibleItems(child, list);
                }
            }
        }

        /// <summary>
        /// Moves selection to the first child of the current item.
        /// </summary>
        private static void MoveToFirstChild()
        {
            var item = visibleItems[selectedIndex];
            if (item.Children.Count > 0)
            {
                int itemIndex = visibleItems.IndexOf(item);
                if (itemIndex < 0)
                    return;

                int firstChildIndex = itemIndex + 1;
                if (firstChildIndex < visibleItems.Count)
                {
                    selectedIndex = firstChildIndex;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                }
            }
        }

        /// <summary>
        /// Moves selection to the parent of the current item.
        /// </summary>
        private static void MoveToParent()
        {
            var item = visibleItems[selectedIndex];
            var parent = item.Parent;

            // Don't move to root (hidden)
            if (parent == null || parent == rootItem)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("At top level.");
                return;
            }

            int parentIndex = visibleItems.IndexOf(parent);
            if (parentIndex >= 0)
            {
                selectedIndex = parentIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Handles typeahead search input.
        /// </summary>
        private static void HandleTypeahead(char c)
        {
            var labels = new List<string>();
            foreach (var item in visibleItems)
            {
                labels.Add(item.Label.StripTags());
            }

            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                selectedIndex = newIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceWithSearch();
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'.");
            }
        }

        /// <summary>
        /// Announces the current selection with search context if applicable.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (!IsActive || visibleItems == null || visibleItems.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= visibleItems.Count)
                return;

            var item = visibleItems[selectedIndex];
            string label = item.Label.StripTags();

            string stateIndicator = "";
            if (item.IsExpandable)
            {
                stateIndicator = item.IsExpanded ? " expanded" : " collapsed";
            }

            string announcement = $"{label}{stateIndicator}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'";
            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Gets the sibling position (X of Y) for the given item.
        /// </summary>
        private static (int position, int total) GetSiblingPosition(InspectionTreeItem item)
        {
            List<InspectionTreeItem> siblings;
            if (item.Parent == null || item.Parent == rootItem)
            {
                siblings = rootItem.Children;
            }
            else
            {
                siblings = item.Parent.Children;
            }
            int position = siblings.IndexOf(item) + 1;
            return (position, siblings.Count);
        }

        /// <summary>
        /// Announces the opening of the gene inspection.
        /// </summary>
        private static void AnnounceOpening()
        {
            if (rootItem == null)
                return;

            string rootLabel = rootItem.Label.StripTags();

            // Build opening announcement with first item
            var sb = new System.Text.StringBuilder();
            sb.Append(rootLabel);
            sb.Append(". ");

            // Announce the first item
            if (visibleItems != null && visibleItems.Count > 0)
            {
                var firstItem = visibleItems[0];
                string firstLabel = firstItem.Label.StripTags();
                string state = firstItem.IsExpandable ? (firstItem.IsExpanded ? "expanded" : "collapsed") : "";
                sb.Append($"First gene: {firstLabel}");
                if (!string.IsNullOrEmpty(state))
                    sb.Append($" {state}");
                sb.Append(". ");
            }

            sb.Append("Use Up and Down to navigate, Right to expand, Left to collapse. Page Up and Page Down jump between genes.");
            TolkHelper.Speak(sb.ToString());
        }

        /// <summary>
        /// Announces the current selection to the screen reader.
        /// </summary>
        private static void AnnounceCurrentSelection()
        {
            try
            {
                if (visibleItems == null || visibleItems.Count == 0)
                {
                    TolkHelper.Speak("No items available.");
                    return;
                }

                if (selectedIndex < 0 || selectedIndex >= visibleItems.Count)
                    return;

                var item = visibleItems[selectedIndex];

                // Strip XML tags from label
                string label = item.Label.StripTags().TrimEnd('.', '!', '?');

                // Build state indicator (only for expandable items)
                string stateIndicator = "";
                if (item.IsExpandable)
                {
                    stateIndicator = item.IsExpanded ? " expanded" : " collapsed";
                }

                // Get sibling position
                var (position, total) = GetSiblingPosition(item);

                // Build level suffix if level changed
                string levelSuffix = MenuHelper.GetLevelSuffix("GeneInspection", item.IndentLevel, skipLevelOne: false);

                // Build full announcement (respects AnnouncePosition setting)
                string positionPart = MenuHelper.FormatPosition(position - 1, total);
                string announcement = string.IsNullOrEmpty(positionPart)
                    ? $"{label}{stateIndicator}.{levelSuffix}"
                    : $"{label}{stateIndicator}.{levelSuffix} {positionPart}.";

                TolkHelper.Speak(announcement);
            }
            catch (Exception ex)
            {
                Log.Error($"[GeneInspectionState] Error announcing selection: {ex.Message}");
            }
        }

        #endregion
    }
}
