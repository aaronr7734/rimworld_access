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
    /// Manages the windowless inspection panel state.
    /// Uses a tree structure with inline expansion/collapse.
    /// </summary>
    public static class WindowlessInspectionState
    {
        public static bool IsActive { get; private set; } = false;

        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
        public static TypeaheadSearchHelper Typeahead => typeahead;

        private static InspectionTreeItem rootItem = null;
        private static List<InspectionTreeItem> visibleItems = null;
        private static int selectedIndex = 0;
        private static IntVec3 inspectionPosition;
        private static object parentObject = null; // Track parent object for navigation back
        private static Dictionary<InspectionTreeItem, InspectionTreeItem> lastChildPerParent = new Dictionary<InspectionTreeItem, InspectionTreeItem>();

        /// <summary>
        /// Opens the inspection menu for the specified position.
        /// </summary>
        public static void Open(IntVec3 position)
        {
            try
            {
                inspectionPosition = position;

                // Build the object list
                var objects = BuildObjectList();

                if (objects.Count == 0)
                {
                    TolkHelper.Speak("No items here to inspect.");
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return;
                }

                // Build the tree
                rootItem = InspectionTreeBuilder.BuildTree(objects);
                RebuildVisibleList();
                selectedIndex = 0;
                lastChildPerParent.Clear();
                MenuHelper.ResetLevel("Inspection");

                IsActive = true;
                SoundDefOf.TabOpen.PlayOneShotOnCamera();
                typeahead.ClearSearch();

                // Special handling for single object: auto-expand and position on first child
                if (objects.Count == 1 && visibleItems.Count > 0)
                {
                    var singleItem = visibleItems[0];
                    if (singleItem.IsExpandable)
                    {
                        // Announce just the item name (no expand/collapse status)
                        TolkHelper.Speak(singleItem.Label.StripTags());

                        // Trigger lazy loading if needed
                        if (singleItem.OnActivate != null && singleItem.Children.Count == 0)
                        {
                            singleItem.OnActivate();
                        }

                        if (singleItem.Children.Count > 0)
                        {
                            singleItem.IsExpanded = true;
                            RebuildVisibleList();
                            selectedIndex = 1; // First child
                            AnnounceCurrentSelection();
                        }
                        return; // Early return - skip normal announcement
                    }
                }

                // Normal case: multiple objects or non-expandable single object
                AnnounceCurrentSelection();
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error opening inspection menu: {ex}");
                Close();
            }
        }

        /// <summary>
        /// Opens the inspection menu for a specific object (Thing, Pawn, Building, etc.).
        /// </summary>
        /// <param name="obj">The object to inspect</param>
        /// <param name="parent">Optional parent object to return to when pressing Escape</param>
        public static void OpenForObject(object obj, object parent = null)
        {
            try
            {
                if (obj == null)
                {
                    TolkHelper.Speak("No object to inspect.");
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return;
                }

                // Set position if it's a Thing
                if (obj is Thing thing)
                {
                    inspectionPosition = thing.Position;
                }
                else
                {
                    inspectionPosition = IntVec3.Invalid;
                }

                // Store parent for navigation back
                parentObject = parent;

                // Build tree with just this object
                var objects = new List<object> { obj };
                rootItem = InspectionTreeBuilder.BuildTree(objects);
                RebuildVisibleList();
                selectedIndex = 0;
                lastChildPerParent.Clear();
                MenuHelper.ResetLevel("Inspection");

                IsActive = true;
                SoundDefOf.TabOpen.PlayOneShotOnCamera();
                typeahead.ClearSearch();
                AnnounceCurrentSelection();
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error opening inspection menu for object: {ex}");
                Close();
            }
        }

        /// <summary>
        /// Closes the inspection menu.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
            rootItem = null;
            visibleItems = null;
            lastChildPerParent.Clear();
            MenuHelper.ResetLevel("Inspection");
            selectedIndex = 0;
            parentObject = null;
            typeahead.ClearSearch();
        }

        /// <summary>
        /// Rebuilds after an action that modifies the tree structure (e.g., deleting a job).
        /// </summary>
        public static void RebuildAfterAction()
        {
            if (!IsActive)
                return;

            RebuildVisibleList();

            // Try to keep selection valid
            if (selectedIndex >= visibleItems.Count)
                selectedIndex = Math.Max(0, visibleItems.Count - 1);

            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Rebuilds the tree (used after actions that modify state).
        /// </summary>
        public static void RebuildTree()
        {
            if (!IsActive)
                return;

            var objects = BuildObjectList();
            rootItem = InspectionTreeBuilder.BuildTree(objects);
            RebuildVisibleList();
            lastChildPerParent.Clear(); // Clear saved positions since tree nodes are recreated

            // Try to keep selection valid
            if (selectedIndex >= visibleItems.Count)
                selectedIndex = Math.Max(0, visibleItems.Count - 1);

            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Builds the list of inspectable objects at the cursor position.
        /// </summary>
        private static List<object> BuildObjectList()
        {
            var objects = new List<object>();

            if (Find.CurrentMap == null)
                return objects;

            var objectsAtPosition = Selector.SelectableObjectsAt(inspectionPosition, Find.CurrentMap);

            foreach (var obj in objectsAtPosition)
            {
                if (obj is Pawn || obj is Building || obj is Plant || obj is Thing)
                {
                    objects.Add(obj);
                }
            }

            return objects;
        }

        /// <summary>
        /// Rebuilds the visible items list based on expansion state.
        /// </summary>
        private static void RebuildVisibleList()
        {
            visibleItems = new List<InspectionTreeItem>();

            if (rootItem == null)
                return;

            // Get all visible items from the tree
            foreach (var child in rootItem.Children)
            {
                visibleItems.AddRange(child.GetVisibleItems());
            }
        }

        /// <summary>
        /// Selects the next item.
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
        /// Selects the previous item.
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
        /// Expands the selected item (Right arrow).
        /// WCAG behavior:
        /// - On closed node: Open node, focus stays on current item
        /// - On open node: Move to first child
        /// - On end node: Reject sound + feedback
        /// </summary>
        public static void Expand()
        {
            if (!IsActive || visibleItems == null || selectedIndex >= visibleItems.Count)
                return;

            // Clear search when expanding to avoid "no more search results" confusion
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

            // Collapsed node - expand it (focus stays on current item)
            // Trigger lazy loading if needed
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

            // Focus stays on current item - just announce the state change
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

            // Clear search when expanding
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
            bool hasExpandableItems = false;
            foreach (var sibling in siblings)
            {
                if (sibling.IsExpandable)
                {
                    hasExpandableItems = true;
                    break;
                }
            }

            if (!hasExpandableItems)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No categories to expand at this level.");
                return;
            }

            if (collapsedSiblings.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("All categories already expanded at this level.");
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

                // Only count as expanded if there are children to show
                if (sibling.Children.Count > 0)
                {
                    sibling.IsExpanded = true;
                    expandedCount++;
                }
            }

            // Rebuild the visible items list
            RebuildVisibleList();

            // Announce result
            if (expandedCount == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No categories to expand at this level.");
            }
            else
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                string categoryWord = expandedCount == 1 ? "category" : "categories";
                TolkHelper.Speak($"Expanded {expandedCount} {categoryWord}.");
            }
        }

        /// <summary>
        /// Collapses the selected item (Left arrow).
        /// WCAG behavior:
        /// - On open node: Close node, focus stays on current item
        /// - On closed node: Move to parent (WITHOUT collapsing the parent)
        /// - On end node: Move to parent (WITHOUT collapsing the parent)
        /// </summary>
        public static void Collapse()
        {
            if (!IsActive || visibleItems == null || selectedIndex >= visibleItems.Count)
                return;

            // Clear search when collapsing to avoid "no more search results" confusion
            typeahead.ClearSearch();

            var item = visibleItems[selectedIndex];

            // Case 1: Item is expandable and expanded - collapse it (focus stays)
            if (item.IsExpandable && item.IsExpanded)
            {
                item.IsExpanded = false;
                RebuildVisibleList();

                // Adjust selection if it's now out of range
                if (selectedIndex >= visibleItems.Count)
                    selectedIndex = Math.Max(0, visibleItems.Count - 1);

                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentSelection();
                return;
            }

            // Case 2: Item is collapsed or end node - move to parent WITHOUT collapsing
            var parent = item.Parent;

            // Skip non-expandable parents (like root) to find an expandable ancestor
            while (parent != null && !parent.IsExpandable)
            {
                parent = parent.Parent;
            }

            if (parent == null)
            {
                // No expandable parent - we're at the top level
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Already at top level.", SpeechPriority.High);
                return;
            }

            // Save current child position for this parent (for later re-expansion)
            lastChildPerParent[parent] = item;

            // Move selection to the parent WITHOUT collapsing it
            int parentIndex = visibleItems.IndexOf(parent);
            if (parentIndex >= 0)
            {
                selectedIndex = parentIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceCurrentSelection();
            }
            else
            {
                // Parent not visible (shouldn't happen, but handle it)
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Cannot navigate to parent.", SpeechPriority.High);
            }
        }

        /// <summary>
        /// Activates the selected item (Enter key).
        /// </summary>
        public static void ActivateAction()
        {
            if (!IsActive || visibleItems == null || selectedIndex >= visibleItems.Count)
                return;

            var item = visibleItems[selectedIndex];

            // For expandable items, Enter acts like Right arrow
            if (item.IsExpandable && !item.IsExpanded)
            {
                Expand();
                return;
            }

            // For items with actions, execute the action
            if (item.OnActivate != null)
            {
                item.OnActivate();
                SoundDefOf.Click.PlayOneShotOnCamera();
                return;
            }

            // Otherwise, nothing to do
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("No action available for this item.");
        }

        /// <summary>
        /// Deletes the currently selected item (Delete key).
        /// Used for canceling queued jobs.
        /// </summary>
        public static void DeleteItem()
        {
            if (!IsActive || visibleItems == null || selectedIndex >= visibleItems.Count)
                return;

            var item = visibleItems[selectedIndex];

            // Check if item has a delete action
            if (item.OnDelete != null)
            {
                item.OnDelete();
                SoundDefOf.Click.PlayOneShotOnCamera();
                return;
            }

            // No delete action available
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("Cannot delete this item.");
        }

        /// <summary>
        /// Closes the entire panel (Escape key).
        /// If there's a parent object, returns to that parent's inspection instead of fully closing.
        /// </summary>
        public static void ClosePanel()
        {
            if (!IsActive)
                return;

            // Check if we have a parent to return to
            if (parentObject != null)
            {
                var parent = parentObject; // Save reference before Close() clears it
                Close();
                SoundDefOf.Click.PlayOneShotOnCamera();
                OpenForObject(parent); // Open parent without a parent (we don't go deeper than 2 levels)
            }
            else
            {
                Close();
                SoundDefOf.Click.PlayOneShotOnCamera();
                TolkHelper.Speak("Inspection panel closed.");
            }
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
        /// Checks if there's only one root item (single object being inspected).
        /// </summary>
        private static bool HasSingleRoot()
        {
            return rootItem != null && rootItem.Children.Count == 1;
        }

        /// <summary>
        /// Moves selection to the first child of the current item.
        /// </summary>
        private static void MoveToFirstChild()
        {
            var item = visibleItems[selectedIndex];
            if (item.Children.Count > 0)
            {
                int firstChildIndex = visibleItems.IndexOf(item) + 1;
                if (firstChildIndex < visibleItems.Count)
                {
                    selectedIndex = firstChildIndex;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                }
            }
        }

        /// <summary>
        /// Gets the list of labels for all visible items.
        /// </summary>
        private static List<string> GetItemLabels()
        {
            var labels = new List<string>();
            if (visibleItems != null)
            {
                foreach (var item in visibleItems)
                {
                    labels.Add(item.Label.StripTags());
                }
            }
            return labels;
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

            if (typeahead.HasActiveSearch)
            {
                // Build state indicator (only for expandable items)
                string stateIndicator = "";
                if (item.IsExpandable)
                {
                    stateIndicator = item.IsExpanded ? " expanded" : " collapsed";
                }

                string announcement = $"{label}{stateIndicator}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'";
                TolkHelper.Speak(announcement);
            }
            else
            {
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Announces the current selection to the screen reader.
        /// Format: "level N. {name} {state}. {X} of {Y}." or "{name} {state}. {X} of {Y}."
        /// Level is only announced when it changes.
        /// </summary>
        private static void AnnounceCurrentSelection()
        {
            try
            {
                if (visibleItems == null || visibleItems.Count == 0)
                {
                    TolkHelper.Speak("No items to inspect.");
                    return;
                }

                if (selectedIndex < 0 || selectedIndex >= visibleItems.Count)
                    return;

                var item = visibleItems[selectedIndex];

                // Strip XML tags from label and trailing punctuation to avoid double periods
                string label = item.Label.StripTags().TrimEnd('.', '!', '?');

                // Build state indicator (only for expandable items)
                string stateIndicator = "";
                if (item.IsExpandable)
                {
                    stateIndicator = item.IsExpanded ? " expanded" : " collapsed";
                }

                // Get sibling position
                var (position, total) = GetSiblingPosition(item);

                // Get level prefix (only announced when level changes)
                // If single root, subtract 1 so children start at level 1
                int adjustedLevel = item.IndentLevel;
                if (HasSingleRoot())
                {
                    adjustedLevel = Math.Max(0, adjustedLevel - 1);
                }
                // Build full announcement: "{name} {state}. {X} of {Y}. level N"
                string levelSuffix = MenuHelper.GetLevelSuffix("Inspection", adjustedLevel);
                string announcement = $"{label}{stateIndicator}. {MenuHelper.FormatPosition(position - 1, total)}.{levelSuffix}";

                TolkHelper.Speak(announcement);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error in AnnounceCurrentSelection: {ex}");
            }
        }

        /// <summary>
        /// Handles keyboard input for the inspection menu.
        /// Returns true if the input was handled.
        /// </summary>
        public static bool HandleInput(Event ev)
        {
            if (!IsActive)
                return false;

            if (ev.type != EventType.KeyDown)
                return false;

            try
            {
                // Check if any tab state is active and delegate input to it
                if (HealthTabState.IsActive)
                {
                    return HealthTabState.HandleInput(ev);
                }

                KeyCode key = ev.keyCode;

                // Handle Escape - clear search FIRST, then close
                if (key == KeyCode.Escape)
                {
                    if (typeahead.HasActiveSearch)
                    {
                        typeahead.ClearSearchAndAnnounce();
                        ev.Use();
                        return true;
                    }
                    ClosePanel();
                    ev.Use();
                    return true;
                }

                // Handle Backspace for search
                if (key == KeyCode.Backspace && typeahead.HasActiveSearch)
                {
                    var labels = GetItemLabels();
                    if (typeahead.ProcessBackspace(labels, out int newIndex))
                    {
                        if (newIndex >= 0)
                            selectedIndex = newIndex;
                        AnnounceWithSearch();
                    }
                    ev.Use();
                    return true;
                }

                // Handle Up arrow - navigate with search awareness
                if (key == KeyCode.UpArrow)
                {
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        // Navigate through matches only when there ARE matches
                        int prevIndex = typeahead.GetPreviousMatch(selectedIndex);
                        if (prevIndex >= 0)
                        {
                            selectedIndex = prevIndex;
                            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                            AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        // Navigate normally (either no search active, OR search with no matches)
                        SelectPrevious();
                    }
                    ev.Use();
                    return true;
                }

                // Handle Down arrow - navigate with search awareness
                if (key == KeyCode.DownArrow)
                {
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        // Navigate through matches only when there ARE matches
                        int nextIndex = typeahead.GetNextMatch(selectedIndex);
                        if (nextIndex >= 0)
                        {
                            selectedIndex = nextIndex;
                            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                            AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        // Navigate normally (either no search active, OR search with no matches)
                        SelectNext();
                    }
                    ev.Use();
                    return true;
                }

                // Handle Right arrow - expand
                if (key == KeyCode.RightArrow)
                {
                    Expand();
                    ev.Use();
                    return true;
                }

                // Handle Left arrow - collapse
                if (key == KeyCode.LeftArrow)
                {
                    Collapse();
                    ev.Use();
                    return true;
                }

                // Handle Enter - execute
                if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    ActivateAction();
                    ev.Use();
                    return true;
                }

                // Handle Delete - delete item
                if (key == KeyCode.Delete)
                {
                    DeleteItem();
                    ev.Use();
                    return true;
                }

                // Handle * key - expand all sibling categories (WCAG tree view pattern)
                bool isStar = key == KeyCode.KeypadMultiply || (ev.shift && key == KeyCode.Alpha8);
                if (isStar)
                {
                    ExpandAllSiblings();
                    ev.Use();
                    return true;
                }

                // Handle typeahead characters
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                if (isLetter || isNumber)
                {
                    char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                    var labels = GetItemLabels();
                    if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
                    {
                        if (newIndex >= 0)
                        {
                            selectedIndex = newIndex;
                            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                            AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
                    }
                    ev.Use();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error handling input in inspection menu: {ex}");
            }

            return false;
        }
    }
}
