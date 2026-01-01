using System.Collections.Generic;
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
        private static HashSet<string> expandedCategories = new HashSet<string>();
        private static string menuTitle = "";

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
        public static void Open(ThingFilter filter, string title = "Thing Filter")
        {
            if (filter == null)
            {
                Log.Error("Cannot open thing filter menu: filter is null");
                return;
            }

            currentFilter = filter;
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
            expandedCategories.Clear();
            MenuHelper.ResetLevel("ThingFilterMenu");
        }

        private static void BuildMenuItems()
        {
            menuItems.Clear();

            // Quick actions
            menuItems.Add(new MenuItem(MenuItemType.ClearAll, "Clear All", null));
            menuItems.Add(new MenuItem(MenuItemType.AllowAll, "Allow All", null));

            // Hit points range (if configurable)
            if (currentFilter.allowedHitPointsConfigurable)
            {
                FloatRange hpRange = currentFilter.AllowedHitPointsPercents;
                string hpLabel = $"Hit Points: {hpRange.min:P0} - {hpRange.max:P0}";
                menuItems.Add(new MenuItem(MenuItemType.HitPointsRange, hpLabel, hpRange));
            }

            // Quality range (if configurable)
            if (currentFilter.allowedQualitiesConfigurable)
            {
                QualityRange qualityRange = currentFilter.AllowedQualityLevels;
                string qualityLabel = $"Quality: {qualityRange.min} - {qualityRange.max}";
                menuItems.Add(new MenuItem(MenuItemType.QualityRange, qualityLabel, qualityRange));
            }

            // Thing filter tree
            TreeNode_ThingCategory rootNode = currentFilter.DisplayRootCategory;
            BuildCategoryItems(rootNode, 0, null, isRoot: true);
        }

        private static void BuildCategoryItems(TreeNode_ThingCategory node, int indent, MenuItem parentItem, bool isRoot = false)
        {
            if (node == null)
                return;

            // Add special filters for this category
            foreach (SpecialThingFilterDef specialFilter in node.catDef.childSpecialFilters)
            {
                if (specialFilter.configurable)
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
                if (!Find.HiddenItemsManager.Hidden(thingDef))
                {
                    MenuItem item = new MenuItem(MenuItemType.ThingDef, thingDef.LabelCap, thingDef, indent);
                    item.isAllowed = currentFilter.Allows(thingDef);
                    item.parent = parentItem;
                    menuItems.Add(item);
                }
            }
        }

        private static bool IsCategoryAllowed(TreeNode_ThingCategory node)
        {
            // Check if any descendant thing def is allowed
            foreach (ThingDef thingDef in node.catDef.DescendantThingDefs)
            {
                if (currentFilter.Allows(thingDef))
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
            currentFilter.SetAllowAll(null);
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

                // WCAG format: "[level N. ]{name} {state}. {X of Y}"
                string prefix = MenuHelper.GetLevelPrefix("ThingFilterMenu", item.indentLevel);
                string announcement = prefix + item.label;

                // Add expanded/collapsed state for categories
                if (item.type == MenuItemType.Category)
                {
                    string expandState = item.isExpanded ? "expanded" : "collapsed";
                    announcement += $" {expandState}";
                }

                // Add sibling position (X of Y)
                var (position, total) = GetSiblingPosition(item);
                announcement += $". {MenuHelper.FormatPosition(position - 1, total)}";

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
    }
}
