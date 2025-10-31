using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation for ThingFilter tree structures.
    /// Handles hierarchical category trees with checkboxes, expand/collapse, and sliders.
    /// </summary>
    public static class ThingFilterNavigationState
    {
        // Navigation node types
        public enum NodeType
        {
            Slider,           // Quality or hit points slider (press Enter to edit)
            SpecialFilter,    // Special filter checkbox (marked with *)
            Category,         // Category with children (can expand/collapse)
            ThingDef,         // Individual thing/item checkbox
            SaveAndReturn     // Special action to save and return to assign menu
        }

        public class NavigationNode
        {
            public NodeType Type;
            public int IndentLevel;
            public string Label;
            public string Description;
            public bool IsExpanded;      // For categories
            public bool IsChecked;       // For checkboxes
            public object Data;          // ThingCategoryDef, ThingDef, SpecialThingFilterDef, or null for sliders
        }

        private static bool isActive = false;
        private static ThingFilter currentFilter = null;
        private static TreeNode_ThingCategory rootNode = null;
        private static List<NavigationNode> flattenedNodes = new List<NavigationNode>();
        private static int selectedIndex = 0;

        // Slider states
        private enum SliderMode { None, Quality, HitPoints }
        private enum SliderPart { Min, Max }
        private static bool hasQualitySlider = false;
        private static bool hasHitPointsSlider = false;
        private static SliderMode currentSliderMode = SliderMode.None;
        private static bool isEditingSlider = false;
        private static SliderPart currentSliderPart = SliderPart.Min;

        public static bool IsActive => isActive;
        public static bool IsEditingSlider => isEditingSlider;

        /// <summary>
        /// Activates filter navigation for a given ThingFilter.
        /// </summary>
        public static void Activate(ThingFilter filter, TreeNode_ThingCategory root, bool showQuality, bool showHitPoints)
        {
            isActive = true;
            currentFilter = filter;
            rootNode = root;
            hasQualitySlider = showQuality;
            hasHitPointsSlider = showHitPoints;
            selectedIndex = 0;
            currentSliderMode = SliderMode.None;

            RebuildNavigationList();
            UpdateClipboard();
        }

        /// <summary>
        /// Deactivates filter navigation.
        /// </summary>
        public static void Deactivate()
        {
            isActive = false;
            currentFilter = null;
            rootNode = null;
            flattenedNodes.Clear();
            selectedIndex = 0;
        }

        /// <summary>
        /// Rebuilds the flattened navigation list from the tree structure.
        /// </summary>
        private static void RebuildNavigationList()
        {
            flattenedNodes.Clear();

            // Add sliders at top
            if (hasHitPointsSlider)
            {
                flattenedNodes.Add(new NavigationNode
                {
                    Type = NodeType.Slider,
                    IndentLevel = 0,
                    Label = "Hit Points Range",
                    Description = "Allowed hit points percentage range",
                    Data = "HitPoints"
                });
            }

            if (hasQualitySlider)
            {
                flattenedNodes.Add(new NavigationNode
                {
                    Type = NodeType.Slider,
                    IndentLevel = 0,
                    Label = "Quality Range",
                    Description = "Allowed quality levels",
                    Data = "Quality"
                });
            }

            // Build tree
            if (rootNode != null)
            {
                AddCategoryChildren(rootNode, 0);
            }

            // Add "Save and Return" action at the bottom
            flattenedNodes.Add(new NavigationNode
            {
                Type = NodeType.SaveAndReturn,
                IndentLevel = 0,
                Label = "Save and Return to Assign Menu",
                Description = "Save filter changes and return to the assign menu"
            });
        }

        /// <summary>
        /// Recursively adds category children to the flattened list.
        /// </summary>
        private static void AddCategoryChildren(TreeNode_ThingCategory node, int indentLevel)
        {
            // Add special filters
            foreach (var specialFilter in node.catDef.childSpecialFilters)
            {
                if (specialFilter.configurable)
                {
                    flattenedNodes.Add(new NavigationNode
                    {
                        Type = NodeType.SpecialFilter,
                        IndentLevel = indentLevel,
                        Label = "*" + specialFilter.LabelCap,
                        Description = specialFilter.description,
                        IsChecked = currentFilter.Allows(specialFilter),
                        Data = specialFilter
                    });
                }
            }

            // Add child categories
            foreach (var childCategory in node.ChildCategoryNodes)
            {
                // Check if category has any allowed items to determine if it's "allowed"
                bool hasAllowedChildren = childCategory.catDef.DescendantThingDefs.Any(t => currentFilter.Allows(t));
                bool isExpanded = true; // Default to expanded

                flattenedNodes.Add(new NavigationNode
                {
                    Type = NodeType.Category,
                    IndentLevel = indentLevel,
                    Label = childCategory.LabelCap,
                    Description = $"Category: {childCategory.LabelCap}",
                    IsExpanded = isExpanded,
                    IsChecked = hasAllowedChildren,
                    Data = childCategory
                });

                // Recursively add children if expanded
                if (isExpanded)
                {
                    AddCategoryChildren(childCategory, indentLevel + 1);
                }
            }

            // Add thing defs
            foreach (var thingDef in node.catDef.childThingDefs.OrderBy(t => t.label))
            {
                if (!Find.HiddenItemsManager.Hidden(thingDef))
                {
                    flattenedNodes.Add(new NavigationNode
                    {
                        Type = NodeType.ThingDef,
                        IndentLevel = indentLevel,
                        Label = thingDef.LabelCap,
                        Description = thingDef.description ?? thingDef.LabelCap,
                        IsChecked = currentFilter.Allows(thingDef),
                        Data = thingDef
                    });
                }
            }
        }

        /// <summary>
        /// Moves selection to the next node.
        /// </summary>
        public static void SelectNext()
        {
            if (flattenedNodes.Count == 0)
                return;

            selectedIndex = (selectedIndex + 1) % flattenedNodes.Count;
            UpdateClipboard();
        }

        /// <summary>
        /// Moves selection to the previous node.
        /// </summary>
        public static void SelectPrevious()
        {
            if (flattenedNodes.Count == 0)
                return;

            selectedIndex--;
            if (selectedIndex < 0)
                selectedIndex = flattenedNodes.Count - 1;

            UpdateClipboard();
        }

        /// <summary>
        /// Enters slider editing mode (for sliders) or executes action (for SaveAndReturn).
        /// </summary>
        public static void ActivateSelected()
        {
            if (flattenedNodes.Count == 0 || selectedIndex < 0 || selectedIndex >= flattenedNodes.Count)
                return;

            var node = flattenedNodes[selectedIndex];

            if (node.Type == NodeType.Slider)
            {
                // Enter slider editing mode
                isEditingSlider = true;
                currentSliderPart = SliderPart.Min;
                string sliderType = node.Data as string;
                if (sliderType == "Quality")
                    currentSliderMode = SliderMode.Quality;
                else if (sliderType == "HitPoints")
                    currentSliderMode = SliderMode.HitPoints;

                AnnounceSliderEditMode();
            }
            else if (node.Type == NodeType.SaveAndReturn)
            {
                // Save and return to assign menu
                SaveAndReturnToAssign();
            }
        }

        /// <summary>
        /// Exits slider editing mode.
        /// </summary>
        public static void ExitSliderEdit()
        {
            if (isEditingSlider)
            {
                isEditingSlider = false;
                currentSliderMode = SliderMode.None;
                UpdateClipboard();
            }
        }

        /// <summary>
        /// Switches between Min and Max in slider editing mode.
        /// </summary>
        public static void ToggleSliderPart()
        {
            if (isEditingSlider)
            {
                currentSliderPart = (currentSliderPart == SliderPart.Min) ? SliderPart.Max : SliderPart.Min;
                AnnounceSliderEditMode();
            }
        }

        /// <summary>
        /// Announces the current slider editing state.
        /// </summary>
        private static void AnnounceSliderEditMode()
        {
            string sliderName = currentSliderMode == SliderMode.Quality ? "Quality" : "Hit Points";
            string partName = currentSliderPart == SliderPart.Min ? "Minimum" : "Maximum";

            if (currentSliderMode == SliderMode.Quality)
            {
                var range = currentFilter.AllowedQualityLevels;
                string value = currentSliderPart == SliderPart.Min ? range.min.ToString() : range.max.ToString();
                ClipboardHelper.CopyToClipboard($"{sliderName} - {partName}: {value}. Use Left/Right to adjust, Up/Down to switch Min/Max, Enter to confirm.");
            }
            else if (currentSliderMode == SliderMode.HitPoints)
            {
                var range = currentFilter.AllowedHitPointsPercents;
                string value = currentSliderPart == SliderPart.Min ? $"{range.min:P0}" : $"{range.max:P0}";
                ClipboardHelper.CopyToClipboard($"{sliderName} - {partName}: {value}. Use Left/Right to adjust, Up/Down to switch Min/Max, Enter to confirm.");
            }
        }

        /// <summary>
        /// Saves changes and returns to the assign menu.
        /// </summary>
        private static void SaveAndReturnToAssign()
        {
            // Deactivate filter navigation
            Deactivate();

            // Close whichever policy manager is active
            if (WindowlessOutfitPolicyState.IsActive)
            {
                WindowlessOutfitPolicyState.Close();
            }
            if (WindowlessFoodPolicyState.IsActive)
            {
                WindowlessFoodPolicyState.Close();
            }

            // Reopen assign menu
            if (Find.CurrentMap != null && Find.CurrentMap.mapPawns.FreeColonists.Any())
            {
                Pawn firstPawn = Find.CurrentMap.mapPawns.FreeColonists.First();
                AssignMenuState.Open(firstPawn);
            }
        }

        /// <summary>
        /// Toggles the checkbox for the current selection (if applicable).
        /// </summary>
        public static void ToggleSelected()
        {
            if (flattenedNodes.Count == 0 || selectedIndex < 0 || selectedIndex >= flattenedNodes.Count)
                return;

            var node = flattenedNodes[selectedIndex];

            switch (node.Type)
            {
                case NodeType.SpecialFilter:
                    var specialFilter = node.Data as SpecialThingFilterDef;
                    if (specialFilter != null)
                    {
                        bool newValue = !currentFilter.Allows(specialFilter);
                        currentFilter.SetAllow(specialFilter, newValue);
                        node.IsChecked = newValue;
                        ClipboardHelper.CopyToClipboard($"{node.Label}: {(newValue ? "Allowed" : "Disallowed")}");
                    }
                    break;

                case NodeType.Category:
                    var category = node.Data as TreeNode_ThingCategory;
                    if (category != null)
                    {
                        // Toggle all items in this category
                        bool hasAnyAllowed = category.catDef.DescendantThingDefs.Any(t => currentFilter.Allows(t));
                        bool newValue = !hasAnyAllowed;
                        currentFilter.SetAllow(category.catDef, newValue);
                        node.IsChecked = newValue;
                        RebuildNavigationList(); // Rebuild because children may change
                        ClipboardHelper.CopyToClipboard($"{node.Label}: {(newValue ? "Allowed" : "Disallowed")}");
                    }
                    break;

                case NodeType.ThingDef:
                    var thingDef = node.Data as ThingDef;
                    if (thingDef != null)
                    {
                        bool newValue = !currentFilter.Allows(thingDef);
                        currentFilter.SetAllow(thingDef, newValue);
                        node.IsChecked = newValue;
                        ClipboardHelper.CopyToClipboard($"{node.Label}: {(newValue ? "Allowed" : "Disallowed")}");
                    }
                    break;

                case NodeType.Slider:
                    // For sliders, toggle just announces current value
                    AnnounceSliderValue(node);
                    break;
            }
        }

        /// <summary>
        /// Expands or collapses a category node.
        /// </summary>
        public static void ToggleExpand()
        {
            if (flattenedNodes.Count == 0 || selectedIndex < 0 || selectedIndex >= flattenedNodes.Count)
                return;

            var node = flattenedNodes[selectedIndex];

            if (node.Type == NodeType.Category)
            {
                node.IsExpanded = !node.IsExpanded;
                int oldIndex = selectedIndex;
                RebuildNavigationList();
                selectedIndex = oldIndex; // Try to maintain position
                ClipboardHelper.CopyToClipboard($"{node.Label}: {(node.IsExpanded ? "Expanded" : "Collapsed")}");
            }
        }

        /// <summary>
        /// Adjusts the slider value (for quality or hit points sliders).
        /// In editing mode, adjusts the current part (min or max).
        /// Outside editing mode, just announces the current value.
        /// </summary>
        public static void AdjustSlider(int direction)
        {
            if (!isEditingSlider)
            {
                // Not in editing mode, just announce value
                if (flattenedNodes.Count == 0 || selectedIndex < 0 || selectedIndex >= flattenedNodes.Count)
                    return;

                var node = flattenedNodes[selectedIndex];
                if (node.Type == NodeType.Slider)
                {
                    AnnounceSliderValue(node);
                }
                return;
            }

            // In editing mode, adjust the current part
            if (currentSliderMode == SliderMode.Quality)
            {
                var range = currentFilter.AllowedQualityLevels;

                if (currentSliderPart == SliderPart.Min)
                {
                    int newMin = (int)range.min + direction;
                    newMin = Mathf.Clamp(newMin, (int)QualityCategory.Awful, (int)range.max);
                    range.min = (QualityCategory)newMin;
                }
                else // Max
                {
                    int newMax = (int)range.max + direction;
                    newMax = Mathf.Clamp(newMax, (int)range.min, (int)QualityCategory.Legendary);
                    range.max = (QualityCategory)newMax;
                }

                currentFilter.AllowedQualityLevels = range;
                AnnounceSliderEditMode();
            }
            else if (currentSliderMode == SliderMode.HitPoints)
            {
                var range = currentFilter.AllowedHitPointsPercents;
                float step = 0.05f; // 5% steps

                if (currentSliderPart == SliderPart.Min)
                {
                    float newMin = range.min + (direction * step);
                    newMin = Mathf.Clamp(newMin, 0f, range.max);
                    range.min = newMin;
                }
                else // Max
                {
                    float newMax = range.max + (direction * step);
                    newMax = Mathf.Clamp(newMax, range.min, 1f);
                    range.max = newMax;
                }

                currentFilter.AllowedHitPointsPercents = range;
                AnnounceSliderEditMode();
            }
        }

        /// <summary>
        /// Announces the current value of a slider.
        /// </summary>
        private static void AnnounceSliderValue(NavigationNode node)
        {
            string sliderType = node.Data as string;

            if (sliderType == "Quality")
            {
                var range = currentFilter.AllowedQualityLevels;
                ClipboardHelper.CopyToClipboard($"Quality: {range.min} to {range.max}");
            }
            else if (sliderType == "HitPoints")
            {
                var range = currentFilter.AllowedHitPointsPercents;
                ClipboardHelper.CopyToClipboard($"Hit Points: {range.min:P0} to {range.max:P0}");
            }
        }

        /// <summary>
        /// Allows all items (convenience function).
        /// </summary>
        public static void AllowAll()
        {
            if (currentFilter != null)
            {
                currentFilter.SetAllowAll(null);
                RebuildNavigationList();
                ClipboardHelper.CopyToClipboard("Allowed all items");
            }
        }

        /// <summary>
        /// Disallows all items (convenience function).
        /// </summary>
        public static void DisallowAll()
        {
            if (currentFilter != null)
            {
                currentFilter.SetDisallowAll();
                RebuildNavigationList();
                ClipboardHelper.CopyToClipboard("Disallowed all items");
            }
        }

        /// <summary>
        /// Updates the clipboard with the current selection.
        /// </summary>
        private static void UpdateClipboard()
        {
            if (flattenedNodes.Count == 0)
            {
                ClipboardHelper.CopyToClipboard("No items in filter");
                return;
            }

            if (selectedIndex < 0 || selectedIndex >= flattenedNodes.Count)
                return;

            var node = flattenedNodes[selectedIndex];
            string indent = new string(' ', node.IndentLevel * 2);
            string typeIcon = "";
            switch (node.Type)
            {
                case NodeType.Slider:
                    typeIcon = "[Slider]";
                    break;
                case NodeType.SpecialFilter:
                    typeIcon = "[*]";
                    break;
                case NodeType.Category:
                    typeIcon = node.IsExpanded ? "[-]" : "[+]";
                    break;
                case NodeType.ThingDef:
                    typeIcon = "[ ]";
                    break;
                case NodeType.SaveAndReturn:
                    typeIcon = "[Action]";
                    break;
            }

            string status = "";
            if (node.Type == NodeType.Category || node.Type == NodeType.ThingDef || node.Type == NodeType.SpecialFilter)
            {
                status = node.IsChecked ? " (Allowed)" : " (Disallowed)";
            }
            else if (node.Type == NodeType.Slider)
            {
                status = " - Press Enter to edit";
            }
            else if (node.Type == NodeType.SaveAndReturn)
            {
                status = " - Press Enter to execute";
            }

            ClipboardHelper.CopyToClipboard($"{indent}{typeIcon} {node.Label}{status} - {selectedIndex + 1}/{flattenedNodes.Count}");
        }
    }
}
