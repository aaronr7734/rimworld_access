using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages a windowless storage settings menu for stockpile zones.
    /// Provides keyboard navigation through priorities, categories, and individual items.
    /// </summary>
    public static class StorageSettingsMenuState
    {
        private static List<MenuItem> menuItems = null;
        private static int selectedIndex = 0;
        private static bool isActive = false;
        private static StorageSettings currentSettings = null;
        private static HashSet<string> expandedCategories = new HashSet<string>(); // Track which categories are expanded

        private enum MenuItemType
        {
            Priority,
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
            public object data; // Can be TreeNode_ThingCategory, ThingDef, SpecialThingFilterDef, or StoragePriority
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
        /// Opens the storage settings menu for the given storage settings.
        /// </summary>
        public static void Open(StorageSettings settings)
        {
            if (settings == null)
            {
                Log.Error("Cannot open storage settings menu: settings is null");
                return;
            }

            currentSettings = settings;
            menuItems = new List<MenuItem>();
            selectedIndex = 0;
            isActive = true;

            BuildMenuItems();
            AnnounceCurrentSelection();

            Log.Message($"Opened storage settings menu with {menuItems.Count} items");
        }

        /// <summary>
        /// Closes the storage settings menu.
        /// </summary>
        public static void Close()
        {
            menuItems = null;
            selectedIndex = 0;
            isActive = false;
            currentSettings = null;
            expandedCategories.Clear();
        }

        /// <summary>
        /// Builds the menu item list from the storage settings.
        /// </summary>
        private static void BuildMenuItems()
        {
            menuItems.Clear();

            // Priority selection
            menuItems.Add(new MenuItem(MenuItemType.Priority, GetPriorityLabel(), currentSettings.Priority));

            // Quick actions
            menuItems.Add(new MenuItem(MenuItemType.ClearAll, "Clear All", null));
            menuItems.Add(new MenuItem(MenuItemType.AllowAll, "Allow All", null));

            // Get the parent filter to determine what categories are configurable
            // This mirrors how ThingFilterUI.DoThingFilterConfigWindow works
            ThingFilter parentFilter = currentSettings.owner?.GetParentStoreSettings()?.filter;

            // Hit points range (use parent filter's configurability if available)
            bool hpConfigurable = parentFilter?.allowedHitPointsConfigurable ?? currentSettings.filter.allowedHitPointsConfigurable;
            if (hpConfigurable)
            {
                FloatRange hpRange = currentSettings.filter.AllowedHitPointsPercents;
                string hpLabel = $"Hit Points: {hpRange.min:P0} - {hpRange.max:P0}";
                menuItems.Add(new MenuItem(MenuItemType.HitPointsRange, hpLabel, hpRange));
            }

            // Quality range (use parent filter's configurability if available)
            bool qualityConfigurable = parentFilter?.allowedQualitiesConfigurable ?? currentSettings.filter.allowedQualitiesConfigurable;
            if (qualityConfigurable)
            {
                QualityRange qualityRange = currentSettings.filter.AllowedQualityLevels;
                string qualityLabel = $"Quality: {qualityRange.min} - {qualityRange.max}";
                menuItems.Add(new MenuItem(MenuItemType.QualityRange, qualityLabel, qualityRange));
            }

            // Thing filter tree
            // Use the parent filter's DisplayRootCategory if available (stable, doesn't change based on current filter)
            // Otherwise fall back to the filter's own RootNode
            TreeNode_ThingCategory rootNode;
            if (parentFilter != null)
            {
                rootNode = parentFilter.DisplayRootCategory;
            }
            else
            {
                // No parent filter - use the filter's RootNode (not DisplayRootCategory which changes based on allowed items)
                rootNode = currentSettings.filter.RootNode ?? ThingCategoryNodeDatabase.RootNode;
            }
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
                    item.isAllowed = currentSettings.filter.Allows(specialFilter);
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

                // Check if this category should be expanded (based on our tracking set)
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
                    item.isAllowed = currentSettings.filter.Allows(thingDef);
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
                if (currentSettings.filter.Allows(thingDef))
                {
                    return true;
                }
            }
            return false;
        }

        private static string GetPriorityLabel()
        {
            return $"Priority: {currentSettings.Priority.Label().CapitalizeFirst()}";
        }

        public static void SelectNext()
        {
            if (menuItems == null || menuItems.Count == 0)
                return;

            selectedIndex = (selectedIndex + 1) % menuItems.Count;
            AnnounceCurrentSelection();
        }

        public static void SelectPrevious()
        {
            if (menuItems == null || menuItems.Count == 0)
                return;

            selectedIndex = (selectedIndex - 1 + menuItems.Count) % menuItems.Count;
            AnnounceCurrentSelection();
        }

        public static void ExpandCurrent()
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            switch (item.type)
            {
                case MenuItemType.Priority:
                    // Cycle to next priority
                    CyclePriority(forward: true);
                    break;

                case MenuItemType.Category:
                    // Only expand if collapsed
                    if (!item.isExpanded)
                    {
                        TreeNode_ThingCategory node = item.data as TreeNode_ThingCategory;
                        if (node != null)
                        {
                            expandedCategories.Add(node.catDef.defName);
                            RebuildMenu();
                            TolkHelper.Speak($"Expanded: {item.label}");
                        }
                    }
                    else
                    {
                        TolkHelper.Speak($"{item.label} (already expanded)");
                    }
                    break;

                case MenuItemType.ThingDef:
                case MenuItemType.SpecialFilter:
                case MenuItemType.HitPointsRange:
                case MenuItemType.QualityRange:
                case MenuItemType.ClearAll:
                case MenuItemType.AllowAll:
                    // Right arrow doesn't apply to these types
                    TolkHelper.Speak("Press Enter to select");
                    break;
            }
        }

        public static void CollapseCurrent()
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            switch (item.type)
            {
                case MenuItemType.Priority:
                    // Cycle to previous priority
                    CyclePriority(forward: false);
                    break;

                case MenuItemType.Category:
                    // If expanded, collapse it
                    if (item.isExpanded)
                    {
                        TreeNode_ThingCategory node = item.data as TreeNode_ThingCategory;
                        if (node != null)
                        {
                            expandedCategories.Remove(node.catDef.defName);
                            RebuildMenu();
                            TolkHelper.Speak($"Collapsed: {item.label}");
                        }
                    }
                    else
                    {
                        // If not expanded, navigate to parent category
                        NavigateToParent();
                    }
                    break;

                case MenuItemType.ThingDef:
                case MenuItemType.SpecialFilter:
                    // Navigate to parent category
                    NavigateToParent();
                    break;

                case MenuItemType.HitPointsRange:
                case MenuItemType.QualityRange:
                case MenuItemType.ClearAll:
                case MenuItemType.AllowAll:
                    // Left arrow doesn't apply to these types
                    TolkHelper.Speak("Press Enter to select");
                    break;
            }
        }

        /// <summary>
        /// Navigates to the parent category of the current item.
        /// If the current item has a parent, collapses the parent and selects it.
        /// </summary>
        private static void NavigateToParent()
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            // Check if this item has a parent
            if (item.parent == null)
            {
                TolkHelper.Speak("At top level");
                return;
            }

            // Find the parent in the menu items list
            MenuItem parent = item.parent;

            // The parent should be a category - collapse it
            if (parent.type == MenuItemType.Category)
            {
                TreeNode_ThingCategory node = parent.data as TreeNode_ThingCategory;
                if (node != null)
                {
                    // Collapse the parent category
                    expandedCategories.Remove(node.catDef.defName);

                    // Remember the parent's label to find it after rebuild
                    string parentLabel = parent.label;
                    MenuItemType parentType = parent.type;

                    // Rebuild menu
                    RebuildMenu();

                    // Find and select the parent
                    for (int i = 0; i < menuItems.Count; i++)
                    {
                        if (menuItems[i].label == parentLabel && menuItems[i].type == parentType)
                        {
                            selectedIndex = i;
                            break;
                        }
                    }

                    AnnounceCurrentSelection();
                }
            }
            else
            {
                TolkHelper.Speak("At top level");
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
                    RangeEditMenuState.OpenHitPointsRange(currentSettings.filter.AllowedHitPointsPercents);
                    break;

                case MenuItemType.QualityRange:
                    // Open range edit submenu
                    RangeEditMenuState.OpenQualityRange(currentSettings.filter.AllowedQualityLevels);
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
                currentSettings.filter.AllowedHitPointsPercents = hitPoints;
                item.label = $"Hit Points: {hitPoints.min:P0} - {hitPoints.max:P0}";
                item.data = hitPoints;
                TolkHelper.Speak(item.label);
            }
            else if (item.type == MenuItemType.QualityRange)
            {
                currentSettings.filter.AllowedQualityLevels = quality;
                item.label = $"Quality: {quality.min} - {quality.max}";
                item.data = quality;
                TolkHelper.Speak(item.label);
            }
        }

        private static void CyclePriority(bool forward)
        {
            StoragePriority[] priorities = new[] {
                StoragePriority.Low,
                StoragePriority.Normal,
                StoragePriority.Preferred,
                StoragePriority.Important,
                StoragePriority.Critical
            };

            int currentIndex = Array.IndexOf(priorities, currentSettings.Priority);
            if (currentIndex == -1) currentIndex = 1; // Default to Normal

            if (forward)
            {
                currentIndex = (currentIndex + 1) % priorities.Length;
            }
            else
            {
                currentIndex = (currentIndex - 1 + priorities.Length) % priorities.Length;
            }

            currentSettings.Priority = priorities[currentIndex];

            // Update the menu item label
            menuItems[selectedIndex].label = GetPriorityLabel();
            menuItems[selectedIndex].data = currentSettings.Priority;

            TolkHelper.Speak(GetPriorityLabel());
        }

        private static void ToggleCategory(MenuItem item, bool allow)
        {
            TreeNode_ThingCategory node = item.data as TreeNode_ThingCategory;
            if (node == null) return;

            if (allow)
            {
                currentSettings.filter.SetAllow(node.catDef, true);
            }
            else
            {
                currentSettings.filter.SetAllow(node.catDef, false);
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
                    currentSettings.filter.SetAllow(thingDef, allow);
                    item.isAllowed = allow;
                }
            }
            else if (item.type == MenuItemType.SpecialFilter)
            {
                SpecialThingFilterDef specialFilter = item.data as SpecialThingFilterDef;
                if (specialFilter != null)
                {
                    currentSettings.filter.SetAllow(specialFilter, allow);
                    item.isAllowed = allow;
                }
            }

            string state = allow ? "Allowed" : "Disallowed";
            TolkHelper.Speak($"{state}: {item.label}");
        }

        private static void ClearAllItems()
        {
            currentSettings.filter.SetDisallowAll();
            RebuildMenu();
            TolkHelper.Speak("Cleared all items");
        }

        private static void AllowAllItems()
        {
            currentSettings.filter.SetAllowAll(null);
            RebuildMenu();
            TolkHelper.Speak("Allowed all items");
        }

        private static void AdjustHitPointsRange(bool increase)
        {
            FloatRange current = currentSettings.filter.AllowedHitPointsPercents;
            float step = 0.1f;

            if (increase)
            {
                // Right arrow: Expand range (increase max, decrease min)
                // Prioritize increasing max first
                if (current.max < 1f)
                {
                    current.max = Mathf.Min(1f, current.max + step);
                }
                else if (current.min > 0f)
                {
                    current.min = Mathf.Max(0f, current.min - step);
                }
            }
            else
            {
                // Left arrow: Narrow range (decrease max, increase min)
                // Prioritize decreasing max first
                if (current.max > current.min + step)
                {
                    current.max = Mathf.Max(current.min + step, current.max - step);
                }
                else if (current.min < 1f - step)
                {
                    current.min = Mathf.Min(1f - step, current.min + step);
                }
            }

            currentSettings.filter.AllowedHitPointsPercents = current;

            // Update menu item
            menuItems[selectedIndex].label = $"Hit Points: {current.min:P0} - {current.max:P0}";
            menuItems[selectedIndex].data = current;

            TolkHelper.Speak(menuItems[selectedIndex].label);
        }

        private static void AdjustQualityRange(bool increase)
        {
            QualityRange current = currentSettings.filter.AllowedQualityLevels;
            QualityCategory[] qualities = (QualityCategory[])Enum.GetValues(typeof(QualityCategory));

            int minIndex = Array.IndexOf(qualities, current.min);
            int maxIndex = Array.IndexOf(qualities, current.max);

            if (increase)
            {
                // Right arrow: Expand range (increase max, decrease min)
                // Prioritize increasing max first
                if (maxIndex < qualities.Length - 1)
                {
                    current.max = qualities[maxIndex + 1];
                }
                else if (minIndex > 0)
                {
                    current.min = qualities[minIndex - 1];
                }
            }
            else
            {
                // Left arrow: Narrow range (decrease max, increase min)
                // Prioritize decreasing max first
                if (maxIndex > minIndex)
                {
                    current.max = qualities[maxIndex - 1];
                }
                else if (minIndex < qualities.Length - 1)
                {
                    current.min = qualities[minIndex + 1];
                }
            }

            currentSettings.filter.AllowedQualityLevels = current;

            // Update menu item
            menuItems[selectedIndex].label = $"Quality: {current.min} - {current.max}";
            menuItems[selectedIndex].data = current;

            TolkHelper.Speak(menuItems[selectedIndex].label);
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
                string announcement = item.label;

                // Add state information
                if (item.type == MenuItemType.Category || item.type == MenuItemType.ThingDef || item.type == MenuItemType.SpecialFilter)
                {
                    string state = item.isAllowed ? "Allowed" : "Disallowed";
                    announcement += $" ({state})";
                }

                if (item.type == MenuItemType.Category)
                {
                    string expandState = item.isExpanded ? "Expanded" : "Collapsed";
                    announcement += $" [{expandState}]";
                }

                TolkHelper.Speak(announcement);
            }
        }
    }
}
