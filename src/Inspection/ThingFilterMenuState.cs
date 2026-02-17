using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Sound;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Reusable windowless menu for thing filtering.
    /// Used by both BillConfigState (ingredient filters) and StorageSettingsMenuState.
    /// </summary>
    public static class ThingFilterMenuState
    {
        private static List<MenuItem> menuItems = null;
        private static int selectedIndex = 0;
        private static bool isActive = false;
        private static ThingFilter currentFilter = null;
        private static ThingFilter parentFilter = null;  // Defines what's possible (tree structure)
        private static HashSet<string> expandedCategories = new HashSet<string>();
        private static string menuTitle = "";
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        private enum MenuItemType
        {
            ClearAll,
            AllowAll,
            Category,
            ThingDef,
            SpecialFilter,
            HitPointsRange,
            QualityRange
        }

        private class MenuItem
        {
            public MenuItemType type;
            public string label;
            public object data; // Can be TreeNode_ThingCategory, ThingDef, SpecialThingFilterDef
            public int indentLevel;
            public bool isExpanded;
            public bool isAllowed; // Current state
            public MenuItem parent;

            public MenuItem(MenuItemType type, string label, object data, int indent = 0)
            {
                this.type = type;
                this.label = label;
                this.data = data;
                this.indentLevel = indent;
                this.isExpanded = false;
                this.isAllowed = false;
                this.parent = null;
            }
        }

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the thing filter menu.
        /// </summary>
        /// <param name="filter">The filter being edited (what's currently selected)</param>
        /// <param name="fixedFilter">Optional parent filter that defines what's possible (tree structure)</param>
        /// <param name="title">Menu title for announcements</param>
        public static void Open(ThingFilter filter, ThingFilter fixedFilter = null, string title = "Thing Filter")
        {
            if (filter == null)
            {
                Log.Error("Cannot open thing filter menu: filter is null");
                return;
            }

            currentFilter = filter;
            parentFilter = fixedFilter;
            menuTitle = title;
            menuItems = new List<MenuItem>();
            selectedIndex = 0;
            isActive = true;
            MenuHelper.ResetLevel("ThingFilterMenu");

            BuildMenuItems();
            AnnounceCurrentSelection();

            Log.Message($"Opened thing filter menu: {title}");
        }

        /// <summary>
        /// Closes the thing filter menu.
        /// </summary>
        public static void Close()
        {
            menuItems = null;
            selectedIndex = 0;
            isActive = false;
            currentFilter = null;
            parentFilter = null;
            expandedCategories.Clear();
            MenuHelper.ResetLevel("ThingFilterMenu");
            typeahead.ClearSearch();
        }

        private static void BuildMenuItems()
        {
            menuItems.Clear();

            // Quick actions
            menuItems.Add(new MenuItem(MenuItemType.ClearAll, "Clear All", null));
            menuItems.Add(new MenuItem(MenuItemType.AllowAll, "Allow All", null));

            // Hit points range (use parent filter's configurability if available)
            bool hpConfigurable = parentFilter?.allowedHitPointsConfigurable ?? currentFilter.allowedHitPointsConfigurable;
            if (hpConfigurable)
            {
                FloatRange hpRange = currentFilter.AllowedHitPointsPercents;
                string hpLabel = $"Hit Points: {hpRange.min:P0} - {hpRange.max:P0}";
                menuItems.Add(new MenuItem(MenuItemType.HitPointsRange, hpLabel, hpRange));
            }

            // Quality range (use parent filter's configurability if available)
            bool qualityConfigurable = parentFilter?.allowedQualitiesConfigurable ?? currentFilter.allowedQualitiesConfigurable;
            if (qualityConfigurable)
            {
                QualityRange qualityRange = currentFilter.AllowedQualityLevels;
                string qualityLabel = $"Quality: {qualityRange.min} - {qualityRange.max}";
                menuItems.Add(new MenuItem(MenuItemType.QualityRange, qualityLabel, qualityRange));
            }

            // Thing filter tree - use parent filter's tree structure if available
            TreeNode_ThingCategory rootNode = parentFilter?.DisplayRootCategory ?? currentFilter.DisplayRootCategory;
            BuildCategoryItems(rootNode, 0, null, isRoot: true);
        }

        private static void BuildCategoryItems(TreeNode_ThingCategory node, int indent, MenuItem parentItem, bool isRoot = false)
        {
            if (node == null)
                return;

            // Add special filters for this category
            foreach (SpecialThingFilterDef specialFilter in node.catDef.childSpecialFilters)
            {
                if (specialFilter.configurable && IsVisibleSpecialFilter(specialFilter))
                {
                    MenuItem item = new MenuItem(MenuItemType.SpecialFilter, "*" + specialFilter.LabelCap, specialFilter, indent);
                    item.isAllowed = currentFilter.Allows(specialFilter);
                    item.parent = parentItem;
                    menuItems.Add(item);
                }
            }

            // Add child categories
            foreach (TreeNode_ThingCategory childNode in node.ChildCategoryNodes)
            {
                if (!IsVisibleCategory(childNode))
                    continue;

                MenuItem catItem = new MenuItem(MenuItemType.Category, childNode.LabelCap, childNode, indent);
                catItem.isAllowed = IsCategoryAllowed(childNode);
                catItem.parent = parentItem;

                // Check if this category should be expanded
                string categoryKey = childNode.catDef.defName;
                catItem.isExpanded = expandedCategories.Contains(categoryKey);

                menuItems.Add(catItem);

                // If expanded, add children
                if (catItem.isExpanded)
                {
                    BuildCategoryItems(childNode, indent + 1, catItem, isRoot: false);
                }
            }

            // Add thing defs in this category
            foreach (ThingDef thingDef in node.catDef.childThingDefs)
            {
                if (IsVisible(thingDef) && !Find.HiddenItemsManager.Hidden(thingDef))
                {
                    MenuItem item = new MenuItem(MenuItemType.ThingDef, thingDef.LabelCap, thingDef, indent);
                    item.isAllowed = currentFilter.Allows(thingDef);
                    item.parent = parentItem;
                    menuItems.Add(item);
                }
            }
        }

        /// <summary>
        /// Checks if a ThingDef should be visible in the filter tree.
        /// Mirrors Listing_TreeThingFilter.Visible(ThingDef).
        /// </summary>
        private static bool IsVisible(ThingDef td)
        {
            if (!td.PlayerAcquirable)
                return false;
            if (td.virtualDefParent != null)
                return false;
            if (parentFilter != null)
            {
                if (!parentFilter.Allows(td))
                    return false;
                if (parentFilter.IsAlwaysDisallowedDueToSpecialFilters(td))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Checks if a category node has any visible descendant ThingDefs.
        /// Mirrors Listing_TreeThingFilter.Visible(TreeNode_ThingCategory).
        /// </summary>
        private static bool IsVisibleCategory(TreeNode_ThingCategory node)
        {
            return node.catDef.DescendantThingDefs.Any(td => IsVisible(td));
        }

        /// <summary>
        /// Checks if a special filter should be visible.
        /// Mirrors Listing_TreeThingFilter.Visible(SpecialThingFilterDef).
        /// </summary>
        private static bool IsVisibleSpecialFilter(SpecialThingFilterDef f)
        {
            if (parentFilter != null && !parentFilter.Allows(f))
                return false;
            return true;
        }

        private static bool IsCategoryAllowed(TreeNode_ThingCategory node)
        {
            foreach (ThingDef thingDef in node.catDef.DescendantThingDefs)
            {
                if (IsVisible(thingDef) && currentFilter.Allows(thingDef))
                {
                    return true;
                }
            }
            return false;
        }

        public static void SelectNext()
        {
            if (menuItems == null || menuItems.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, menuItems.Count);
            AnnounceCurrentSelection();
        }

        public static void SelectPrevious()
        {
            if (menuItems == null || menuItems.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, menuItems.Count);
            AnnounceCurrentSelection();
        }

        public static void ExpandOrToggleOn()
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            switch (item.type)
            {
                case MenuItemType.Category:
                    // WCAG Right arrow: collapsed -> expand (focus stays); expanded -> move to first child
                    if (!item.isExpanded)
                    {
                        // Collapsed: expand it, focus stays on current item
                        TreeNode_ThingCategory node = item.data as TreeNode_ThingCategory;
                        if (node != null)
                        {
                            expandedCategories.Add(node.catDef.defName);
                            RebuildMenu();
                            SoundDefOf.TabOpen.PlayOneShotOnCamera();
                            AnnounceCurrentSelection();
                        }
                    }
                    else
                    {
                        // Already expanded: move to first child if it has children
                        if (!MoveToFirstChild())
                        {
                            // No children, play reject sound
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        }
                    }
                    break;

                case MenuItemType.ThingDef:
                case MenuItemType.SpecialFilter:
                    // End node: play reject sound (no children to expand into)
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    break;

                case MenuItemType.HitPointsRange:
                case MenuItemType.QualityRange:
                    TolkHelper.Speak("Press Enter to edit range");
                    break;

                case MenuItemType.ClearAll:
                    ClearAllItems();
                    break;

                case MenuItemType.AllowAll:
                    AllowAllItems();
                    break;
            }
        }

        public static void CollapseOrToggleOff()
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            switch (item.type)
            {
                case MenuItemType.Category:
                    // WCAG Left arrow: expanded -> collapse (focus stays); collapsed -> move to parent
                    if (item.isExpanded)
                    {
                        // Expanded: collapse it, focus stays on current item
                        TreeNode_ThingCategory node = item.data as TreeNode_ThingCategory;
                        if (node != null)
                        {
                            expandedCategories.Remove(node.catDef.defName);
                            RebuildMenu();
                            SoundDefOf.TabClose.PlayOneShotOnCamera();
                            AnnounceCurrentSelection();
                        }
                    }
                    else
                    {
                        // Collapsed: move to parent without collapsing
                        MoveToParent();
                    }
                    break;

                case MenuItemType.ThingDef:
                case MenuItemType.SpecialFilter:
                    // End node: move to parent
                    MoveToParent();
                    break;

                case MenuItemType.HitPointsRange:
                case MenuItemType.QualityRange:
                    TolkHelper.Speak("Press Enter to edit range");
                    break;
            }
        }

        public static void ToggleCurrent()
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            switch (item.type)
            {
                case MenuItemType.Category:
                    ToggleCategory(item, !item.isAllowed);
                    break;

                case MenuItemType.ThingDef:
                case MenuItemType.SpecialFilter:
                    ToggleItem(item, !item.isAllowed);
                    break;

                case MenuItemType.HitPointsRange:
                    // Open range edit submenu
                    RangeEditMenuState.OpenHitPointsRange(currentFilter.AllowedHitPointsPercents);
                    break;

                case MenuItemType.QualityRange:
                    // Open range edit submenu
                    RangeEditMenuState.OpenQualityRange(currentFilter.AllowedQualityLevels);
                    break;

                case MenuItemType.ClearAll:
                    ClearAllItems();
                    break;

                case MenuItemType.AllowAll:
                    AllowAllItems();
                    break;
            }
        }

        /// <summary>
        /// Applies range changes from the range edit submenu.
        /// </summary>
        public static void ApplyRangeChanges(FloatRange hitPoints, QualityRange quality)
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            if (item.type == MenuItemType.HitPointsRange)
            {
                currentFilter.AllowedHitPointsPercents = hitPoints;
                item.label = $"Hit Points: {hitPoints.min:P0} - {hitPoints.max:P0}";
                item.data = hitPoints;
                TolkHelper.Speak(item.label);
            }
            else if (item.type == MenuItemType.QualityRange)
            {
                currentFilter.AllowedQualityLevels = quality;
                item.label = $"Quality: {quality.min} - {quality.max}";
                item.data = quality;
                TolkHelper.Speak(item.label);
            }
        }

        private static void ToggleCategory(MenuItem item, bool allow)
        {
            TreeNode_ThingCategory node = item.data as TreeNode_ThingCategory;
            if (node == null) return;

            if (allow)
            {
                currentFilter.SetAllow(node.catDef, true);
            }
            else
            {
                currentFilter.SetAllow(node.catDef, false);
            }

            item.isAllowed = allow;
            string state = allow ? "Allowed" : "Disallowed";
            TolkHelper.Speak($"{state}: {item.label}");

            // Update child items if expanded
            if (item.isExpanded)
            {
                RebuildMenu();
            }
        }

        private static void ToggleItem(MenuItem item, bool allow)
        {
            if (item.type == MenuItemType.ThingDef)
            {
                ThingDef thingDef = item.data as ThingDef;
                if (thingDef != null)
                {
                    currentFilter.SetAllow(thingDef, allow);
                    item.isAllowed = allow;
                }
            }
            else if (item.type == MenuItemType.SpecialFilter)
            {
                SpecialThingFilterDef specialFilter = item.data as SpecialThingFilterDef;
                if (specialFilter != null)
                {
                    currentFilter.SetAllow(specialFilter, allow);
                    item.isAllowed = allow;
                }
            }

            string state = allow ? "Allowed" : "Disallowed";
            TolkHelper.Speak($"{state}: {item.label}");
        }

        private static void ClearAllItems()
        {
            currentFilter.SetDisallowAll();
            RebuildMenu();
            TolkHelper.Speak("Cleared all items");
        }

        private static void AllowAllItems()
        {
            currentFilter.SetAllowAll(parentFilter);
            RebuildMenu();
            TolkHelper.Speak("Allowed all items");
        }

        private static void RebuildMenu()
        {
            // Save current selection
            MenuItem currentItem = (selectedIndex >= 0 && selectedIndex < menuItems.Count) ? menuItems[selectedIndex] : null;

            // Rebuild
            BuildMenuItems();

            // Try to restore selection
            if (currentItem != null)
            {
                for (int i = 0; i < menuItems.Count; i++)
                {
                    if (menuItems[i].label == currentItem.label && menuItems[i].type == currentItem.type)
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }

            selectedIndex = Mathf.Clamp(selectedIndex, 0, menuItems.Count - 1);
        }

        private static void AnnounceCurrentSelection()
        {
            if (selectedIndex >= 0 && selectedIndex < menuItems.Count)
            {
                MenuItem item = menuItems[selectedIndex];

                // WCAG format: "{name} {state}. {X of Y}. {allowed}. level N"
                string announcement = item.label;

                // Add expanded/collapsed state for categories
                if (item.type == MenuItemType.Category)
                {
                    string expandState = item.isExpanded ? "expanded" : "collapsed";
                    announcement += $" {expandState}";
                }

                // Add sibling position (X of Y)
                var (position, total) = GetSiblingPosition(item);
                announcement += $". {MenuHelper.FormatPosition(position - 1, total)}";

                // Add allowed/disallowed state for toggleable items
                if (item.type == MenuItemType.Category || item.type == MenuItemType.ThingDef || item.type == MenuItemType.SpecialFilter)
                {
                    string allowState = item.isAllowed ? "allowed" : "disallowed";
                    announcement += $". {allowState}";
                }

                // Add level suffix at the end (only announced when level changes)
                announcement += MenuHelper.GetLevelSuffix("ThingFilterMenu", item.indentLevel);

                TolkHelper.Speak(announcement);
            }
        }

        /// <summary>
        /// Gets the position of an item among its siblings at the same indent level.
        /// </summary>
        private static (int position, int total) GetSiblingPosition(MenuItem item)
        {
            // Find siblings: items with the same parent reference
            var siblings = new List<MenuItem>();
            foreach (var m in menuItems)
            {
                if (m.parent == item.parent && m.indentLevel == item.indentLevel)
                {
                    siblings.Add(m);
                }
            }

            int position = siblings.IndexOf(item) + 1;
            return (position, siblings.Count);
        }

        /// <summary>
        /// Moves focus to the first child of the current category.
        /// Returns true if successful, false if no children exist.
        /// </summary>
        private static bool MoveToFirstChild()
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return false;

            MenuItem item = menuItems[selectedIndex];
            int currentIndent = item.indentLevel;

            // Find first child in the flat list (next item with higher indent level)
            for (int i = selectedIndex + 1; i < menuItems.Count; i++)
            {
                if (menuItems[i].indentLevel > currentIndent)
                {
                    // Found a child
                    selectedIndex = i;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    return true;
                }
                if (menuItems[i].indentLevel <= currentIndent)
                {
                    // Hit a sibling or ancestor, no children
                    break;
                }
            }

            return false;
        }

        /// <summary>
        /// Moves focus to the parent of the current item.
        /// </summary>
        private static void MoveToParent()
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            // If item has a parent reference, find it in the list
            if (item.parent != null)
            {
                for (int i = 0; i < menuItems.Count; i++)
                {
                    if (menuItems[i] == item.parent)
                    {
                        selectedIndex = i;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceCurrentSelection();
                        return;
                    }
                }
            }

            // No parent found (top-level item), play reject sound
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Jumps to the first sibling at the same level (Home key) or absolute first (Ctrl+Home).
        /// </summary>
        public static void JumpToFirst(bool ctrlPressed = false)
        {
            if (menuItems == null || menuItems.Count == 0) return;
            MenuHelper.HandleTreeHomeKey(menuItems, ref selectedIndex, m => m.indentLevel, ctrlPressed, ClearAndAnnounce);
        }

        /// <summary>
        /// Jumps to the last item in scope (End key) or absolute last (Ctrl+End).
        /// For expanded nodes: jumps to last visible descendant.
        /// For collapsed/leaf nodes: jumps to last sibling.
        /// </summary>
        public static void JumpToLast(bool ctrlPressed = false)
        {
            if (menuItems == null || menuItems.Count == 0) return;
            MenuHelper.HandleTreeEndKey(
                menuItems,
                ref selectedIndex,
                m => m.indentLevel,
                m => m.type == MenuItemType.Category && m.isExpanded,
                m => HasVisibleChildren(m),
                ctrlPressed,
                ClearAndAnnounce);
        }

        /// <summary>
        /// Checks if a menu item has visible children (next item has higher indent level).
        /// </summary>
        private static bool HasVisibleChildren(MenuItem item)
        {
            if (item.type != MenuItemType.Category || !item.isExpanded)
                return false;

            int itemIndex = menuItems.IndexOf(item);
            if (itemIndex < 0 || itemIndex >= menuItems.Count - 1)
                return false;

            return menuItems[itemIndex + 1].indentLevel > item.indentLevel;
        }

        /// <summary>
        /// Clears typeahead search and announces current selection.
        /// </summary>
        private static void ClearAndAnnounce()
        {
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Checks if typeahead search has an active search buffer.
        /// </summary>
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        /// <summary>
        /// Clears the current typeahead search.
        /// </summary>
        public static void ClearTypeaheadSearch()
        {
            typeahead.ClearSearchAndAnnounce();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Processes a backspace key for typeahead search.
        /// </summary>
        public static bool ProcessBackspace()
        {
            if (!typeahead.HasActiveSearch) return false;

            var labels = GetVisibleItemLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0) selectedIndex = newIndex;
                AnnounceWithSearch();
            }
            return true;
        }

        /// <summary>
        /// Processes a character input for typeahead search.
        /// </summary>
        public static bool ProcessTypeaheadCharacter(char c)
        {
            var labels = GetVisibleItemLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0) { selectedIndex = newIndex; AnnounceWithSearch(); }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
            }
            return true;
        }

        /// <summary>
        /// Navigates to the next matching item when search is active.
        /// </summary>
        public static bool SelectNextMatch()
        {
            if (!typeahead.HasActiveSearch) return false;

            int next = typeahead.GetNextMatch(selectedIndex);
            if (next >= 0)
            {
                selectedIndex = next;
                AnnounceWithSearch();
            }
            return true;
        }

        /// <summary>
        /// Navigates to the previous matching item when search is active.
        /// </summary>
        public static bool SelectPreviousMatch()
        {
            if (!typeahead.HasActiveSearch) return false;

            int prev = typeahead.GetPreviousMatch(selectedIndex);
            if (prev >= 0)
            {
                selectedIndex = prev;
                AnnounceWithSearch();
            }
            return true;
        }

        private static List<string> GetVisibleItemLabels()
        {
            var labels = new List<string>();
            if (menuItems != null)
            {
                foreach (var item in menuItems)
                {
                    labels.Add(item.label);
                }
            }
            return labels;
        }

        private static void AnnounceWithSearch()
        {
            if (menuItems == null || selectedIndex < 0 || selectedIndex >= menuItems.Count) return;

            string label = menuItems[selectedIndex].label;

            if (typeahead.HasActiveSearch)
            {
                TolkHelper.Speak($"{label}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'");
            }
            else
            {
                AnnounceCurrentSelection();
            }
        }
    }
}
