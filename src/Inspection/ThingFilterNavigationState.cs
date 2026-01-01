using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Sound;
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
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

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
        public static bool HasActiveSearch => typeahead.HasActiveSearch;
        public static bool HasNoMatches => typeahead.HasNoMatches;

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
            typeahead.ClearSearch();
            MenuHelper.ResetLevel("ThingFilter");

            RebuildNavigationList();
            AnnounceCurrentNode();
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
            typeahead.ClearSearch();
            MenuHelper.ResetLevel("ThingFilter");
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

            selectedIndex = MenuHelper.SelectNext(selectedIndex, flattenedNodes.Count);
            AnnounceCurrentNode();
        }

        /// <summary>
        /// Moves selection to the previous node.
        /// </summary>
        public static void SelectPrevious()
        {
            if (flattenedNodes.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, flattenedNodes.Count);
            AnnounceCurrentNode();
        }

        /// <summary>
        /// Activates the current selection:
        /// - Sliders: Enter editing mode
        /// - Checkboxes (SpecialFilter, Category, ThingDef): Toggle checked state
        /// - SaveAndReturn: Execute action
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
            else if (node.Type == NodeType.SpecialFilter || node.Type == NodeType.Category || node.Type == NodeType.ThingDef)
            {
                // Toggle checkbox (Enter key works same as Space for checkboxes)
                ToggleSelected();
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
                AnnounceCurrentNode();
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
                TolkHelper.Speak($"{sliderName} - {partName}: {value}. Use Left/Right to adjust, Up/Down to switch Min/Max, Enter to confirm.");
            }
            else if (currentSliderMode == SliderMode.HitPoints)
            {
                var range = currentFilter.AllowedHitPointsPercents;
                string value = currentSliderPart == SliderPart.Min ? $"{range.min:P0}" : $"{range.max:P0}";
                TolkHelper.Speak($"{sliderName} - {partName}: {value}. Use Left/Right to adjust, Up/Down to switch Min/Max, Enter to confirm.");
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

            // Strip asterisks from labels for announcements
            string cleanLabel = StripAsterisks(node.Label);

            switch (node.Type)
            {
                case NodeType.SpecialFilter:
                    var specialFilter = node.Data as SpecialThingFilterDef;
                    if (specialFilter != null)
                    {
                        bool newValue = !currentFilter.Allows(specialFilter);
                        currentFilter.SetAllow(specialFilter, newValue);
                        node.IsChecked = newValue;
                        TolkHelper.Speak($"{cleanLabel}: {(newValue ? "Allowed" : "Disallowed")}");
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
                        TolkHelper.Speak($"{cleanLabel}: {(newValue ? "Allowed" : "Disallowed")}");
                    }
                    break;

                case NodeType.ThingDef:
                    var thingDef = node.Data as ThingDef;
                    if (thingDef != null)
                    {
                        bool newValue = !currentFilter.Allows(thingDef);
                        currentFilter.SetAllow(thingDef, newValue);
                        node.IsChecked = newValue;
                        TolkHelper.Speak($"{cleanLabel}: {(newValue ? "Allowed" : "Disallowed")}");
                    }
                    break;

                case NodeType.Slider:
                    // For sliders, toggle just announces current value
                    AnnounceSliderValue(node);
                    break;
            }
        }

        /// <summary>
        /// Expands the current category node (Right arrow - WCAG tree navigation).
        /// If collapsed: expand and stay on current node.
        /// If already expanded: move to first child.
        /// If end node: reject with feedback.
        /// </summary>
        public static void Expand()
        {
            if (flattenedNodes.Count == 0 || selectedIndex < 0 || selectedIndex >= flattenedNodes.Count)
                return;

            var node = flattenedNodes[selectedIndex];

            // Case 1: Collapsed category - expand it, focus stays
            if (node.Type == NodeType.Category && !node.IsExpanded)
            {
                node.IsExpanded = true;
                int oldIndex = selectedIndex;
                RebuildNavigationList();
                // Find the same node after rebuild (it should be at or near the same position)
                selectedIndex = FindNodeIndex(node);
                if (selectedIndex < 0) selectedIndex = oldIndex;
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentNode();
                return;
            }

            // Case 2: Expanded category - move to first child
            if (node.Type == NodeType.Category && node.IsExpanded)
            {
                // First child is the next item with higher indent level
                if (selectedIndex + 1 < flattenedNodes.Count)
                {
                    var nextNode = flattenedNodes[selectedIndex + 1];
                    if (nextNode.IndentLevel > node.IndentLevel)
                    {
                        selectedIndex++;
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        AnnounceCurrentNode();
                        return;
                    }
                }
                // No children found (empty category)
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Cannot expand this item.");
                return;
            }

            // Case 3: End node (ThingDef, Slider, SpecialFilter, SaveAndReturn) - reject
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("Cannot expand this item.");
        }

        /// <summary>
        /// Collapses the current category node or moves to parent (Left arrow - WCAG tree navigation).
        /// If expanded category: collapse and stay on current node.
        /// If collapsed/end node: move to parent.
        /// If at root level with no parent: reject with feedback.
        /// </summary>
        public static void Collapse()
        {
            if (flattenedNodes.Count == 0 || selectedIndex < 0 || selectedIndex >= flattenedNodes.Count)
                return;

            var node = flattenedNodes[selectedIndex];

            // Case 1: Expanded category - collapse it, focus stays
            if (node.Type == NodeType.Category && node.IsExpanded)
            {
                node.IsExpanded = false;
                int oldIndex = selectedIndex;
                RebuildNavigationList();
                // Find the same node after rebuild
                selectedIndex = FindNodeIndex(node);
                if (selectedIndex < 0) selectedIndex = oldIndex;
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentNode();
                return;
            }

            // Case 2: Move to parent (don't collapse parent)
            int parentIndex = FindParentIndex(node);
            if (parentIndex >= 0)
            {
                selectedIndex = parentIndex;
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentNode();
                return;
            }

            // Case 3: At root level with no parent - reject
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("Already at top level");
        }

        /// <summary>
        /// Finds the index of a node in the flattened list by reference.
        /// </summary>
        private static int FindNodeIndex(NavigationNode node)
        {
            for (int i = 0; i < flattenedNodes.Count; i++)
            {
                if (flattenedNodes[i] == node)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Finds the parent index for a given node.
        /// Parent is the nearest preceding node with a lower indent level.
        /// </summary>
        private static int FindParentIndex(NavigationNode node)
        {
            if (node.IndentLevel <= 0)
                return -1;

            int targetIndent = node.IndentLevel - 1;
            int currentIdx = FindNodeIndex(node);

            // Search backwards for a node with lower indent level
            for (int i = currentIdx - 1; i >= 0; i--)
            {
                if (flattenedNodes[i].IndentLevel == targetIndent)
                    return i;
                // If we hit something with even lower indent, we've gone too far
                if (flattenedNodes[i].IndentLevel < targetIndent)
                    break;
            }
            return -1;
        }

        /// <summary>
        /// Expands all sibling categories at the same level as the current item.
        /// WCAG tree view pattern: * key expands all siblings.
        /// </summary>
        public static void ExpandAllSiblings()
        {
            if (flattenedNodes == null || selectedIndex < 0 || selectedIndex >= flattenedNodes.Count)
                return;

            NavigationNode currentNode = flattenedNodes[selectedIndex];
            int currentIndent = currentNode.IndentLevel;
            int parentIndex = FindParentIndex(currentNode);

            // Find the range of siblings (nodes at same indent level under same parent)
            int startIndex = 0;
            int endIndex = flattenedNodes.Count - 1;

            // If we have a parent, siblings are bounded by parent's scope
            if (parentIndex >= 0)
            {
                startIndex = parentIndex + 1;
                // Find end: scan forwards from parent until we hit a node at parent's level or lower
                for (int i = parentIndex + 1; i < flattenedNodes.Count; i++)
                {
                    if (flattenedNodes[i].IndentLevel <= flattenedNodes[parentIndex].IndentLevel)
                    {
                        endIndex = i - 1;
                        break;
                    }
                }
            }
            else
            {
                // At root level, siblings extend until we hit a different root-level parent section
                // For root level, we consider all root-level items as siblings
                startIndex = 0;
                endIndex = flattenedNodes.Count - 1;
            }

            // Find all collapsed sibling categories at the current indent level
            int expandedCount = 0;
            for (int i = startIndex; i <= endIndex; i++)
            {
                var node = flattenedNodes[i];
                // Must be at same indent level (sibling) and be a collapsed category
                if (node.IndentLevel == currentIndent && node.Type == NodeType.Category && !node.IsExpanded)
                {
                    node.IsExpanded = true;
                    expandedCount++;
                }
            }

            if (expandedCount > 0)
            {
                RebuildNavigationList();
                typeahead.ClearSearch(); // Clear search since visible items changed
                if (expandedCount == 1)
                    TolkHelper.Speak("Expanded 1 category");
                else
                    TolkHelper.Speak($"Expanded {expandedCount} categories");
            }
            else
            {
                // Check if there are any sibling categories at all
                bool hasAnySiblingCategories = false;
                for (int i = startIndex; i <= endIndex; i++)
                {
                    if (flattenedNodes[i].IndentLevel == currentIndent && flattenedNodes[i].Type == NodeType.Category)
                    {
                        hasAnySiblingCategories = true;
                        break;
                    }
                }

                if (hasAnySiblingCategories)
                    TolkHelper.Speak("All categories already expanded at this level");
                else
                    TolkHelper.Speak("No categories to expand at this level");
            }
        }

        /// <summary>
        /// Gets the position of the current node among its siblings (same indent level, same parent).
        /// Returns (position, total) where position is 1-based.
        /// </summary>
        private static (int position, int total) GetSiblingPosition(NavigationNode node)
        {
            int nodeIndex = FindNodeIndex(node);
            if (nodeIndex < 0)
                return (1, 1);

            int indentLevel = node.IndentLevel;

            // Find the range of siblings by looking for the parent boundary
            int startIndex = 0;
            int endIndex = flattenedNodes.Count - 1;

            // Find start: scan backwards until we hit a lower indent level or start
            for (int i = nodeIndex - 1; i >= 0; i--)
            {
                if (flattenedNodes[i].IndentLevel < indentLevel)
                {
                    startIndex = i + 1;
                    break;
                }
            }

            // Find end: scan forwards until we hit a lower indent level or end
            for (int i = nodeIndex + 1; i < flattenedNodes.Count; i++)
            {
                if (flattenedNodes[i].IndentLevel < indentLevel)
                {
                    endIndex = i - 1;
                    break;
                }
            }

            // Count siblings at the same indent level within this range
            int position = 0;
            int total = 0;
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (flattenedNodes[i].IndentLevel == indentLevel)
                {
                    total++;
                    if (i <= nodeIndex)
                        position = total;
                }
            }

            return (position, total);
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
                TolkHelper.Speak($"Quality: {range.min} to {range.max}");
            }
            else if (sliderType == "HitPoints")
            {
                var range = currentFilter.AllowedHitPointsPercents;
                TolkHelper.Speak($"Hit Points: {range.min:P0} to {range.max:P0}");
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
                TolkHelper.Speak("Allowed all items");
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
                TolkHelper.Speak("Disallowed all items");
            }
        }

        /// <summary>
        /// Announces the current node using WCAG-compliant format.
        /// Format: "[level N. ]{name} {state}. {X of Y}"
        /// </summary>
        private static void AnnounceCurrentNode()
        {
            if (flattenedNodes.Count == 0)
            {
                TolkHelper.Speak("No items in filter");
                return;
            }

            if (selectedIndex < 0 || selectedIndex >= flattenedNodes.Count)
                return;

            var node = flattenedNodes[selectedIndex];
            var (position, total) = GetSiblingPosition(node);
            string prefix = MenuHelper.GetLevelPrefix("ThingFilter", node.IndentLevel);

            string announcement;

            // Strip asterisks from labels (they're visual indicators for special filters)
            string cleanLabel = StripAsterisks(node.Label);

            switch (node.Type)
            {
                case NodeType.Slider:
                    // Sliders show their current value
                    string sliderValue = GetSliderValueString(node);
                    announcement = $"{prefix}{cleanLabel} {sliderValue}. {MenuHelper.FormatPosition(position - 1, total)}";
                    break;

                case NodeType.SpecialFilter:
                    // Special filters show checked state
                    string specialState = node.IsChecked ? "checked" : "not checked";
                    announcement = $"{prefix}{cleanLabel} {specialState}. {MenuHelper.FormatPosition(position - 1, total)}";
                    break;

                case NodeType.Category:
                    // Categories show expanded/collapsed state
                    string categoryState = node.IsExpanded ? "expanded" : "collapsed";
                    announcement = $"{prefix}{cleanLabel} {categoryState}. {MenuHelper.FormatPosition(position - 1, total)}";
                    break;

                case NodeType.ThingDef:
                    // ThingDefs show checked state
                    string thingState = node.IsChecked ? "checked" : "not checked";
                    announcement = $"{prefix}{cleanLabel} {thingState}. {MenuHelper.FormatPosition(position - 1, total)}";
                    break;

                case NodeType.SaveAndReturn:
                    announcement = $"{prefix}{cleanLabel}. {MenuHelper.FormatPosition(position - 1, total)}";
                    break;

                default:
                    announcement = $"{prefix}{cleanLabel}. {MenuHelper.FormatPosition(position - 1, total)}";
                    break;
            }

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Gets the current value string for a slider node.
        /// </summary>
        private static string GetSliderValueString(NavigationNode node)
        {
            string sliderType = node.Data as string;

            if (sliderType == "Quality")
            {
                var range = currentFilter.AllowedQualityLevels;
                return $"{range.min} to {range.max}";
            }
            else if (sliderType == "HitPoints")
            {
                var range = currentFilter.AllowedHitPointsPercents;
                return $"{range.min:P0} to {range.max:P0}";
            }

            return "";
        }

        /// <summary>
        /// Strips leading asterisks and whitespace from a label.
        /// Asterisks are visual indicators for special filters that shouldn't be read aloud.
        /// </summary>
        private static string StripAsterisks(string label)
        {
            if (string.IsNullOrEmpty(label))
                return label;

            return label.TrimStart('*', ' ');
        }

        #region Typeahead Support

        /// <summary>
        /// Gets the labels for typeahead searching.
        /// </summary>
        private static List<string> GetSearchLabels()
        {
            var labels = new List<string>();
            foreach (var node in flattenedNodes)
            {
                labels.Add(StripAsterisks(node.Label));
            }
            return labels;
        }

        /// <summary>
        /// Gets the last failed search string.
        /// </summary>
        public static string GetLastFailedSearch()
        {
            return typeahead.LastFailedSearch;
        }

        /// <summary>
        /// Processes a typeahead character for search.
        /// </summary>
        public static void ProcessTypeaheadCharacter(char c)
        {
            if (!isActive || flattenedNodes.Count == 0)
                return;

            var labels = GetSearchLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
            }
        }

        /// <summary>
        /// Processes backspace for typeahead search.
        /// </summary>
        public static void ProcessBackspace()
        {
            if (!isActive || flattenedNodes.Count == 0)
                return;

            if (!typeahead.HasActiveSearch)
                return;

            var labels = GetSearchLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                }
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Clears the typeahead search and announces.
        /// </summary>
        public static void ClearTypeaheadSearch()
        {
            typeahead.ClearSearchAndAnnounce();
            AnnounceCurrentNode();
        }

        /// <summary>
        /// Sets the selected index (for typeahead navigation).
        /// </summary>
        public static void SetSelectedIndex(int index)
        {
            if (index >= 0 && index < flattenedNodes.Count)
            {
                selectedIndex = index;
            }
        }

        /// <summary>
        /// Selects the next match in the filtered list.
        /// </summary>
        public static void SelectNextMatch()
        {
            if (flattenedNodes.Count == 0)
                return;

            int nextIndex = typeahead.GetNextMatch(selectedIndex);
            if (nextIndex >= 0)
            {
                selectedIndex = nextIndex;
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Selects the previous match in the filtered list.
        /// </summary>
        public static void SelectPreviousMatch()
        {
            if (flattenedNodes.Count == 0)
                return;

            int prevIndex = typeahead.GetPreviousMatch(selectedIndex);
            if (prevIndex >= 0)
            {
                selectedIndex = prevIndex;
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Announces the current selection with search context if applicable.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (flattenedNodes.Count == 0 || selectedIndex < 0 || selectedIndex >= flattenedNodes.Count)
                return;

            var node = flattenedNodes[selectedIndex];
            string cleanLabel = StripAsterisks(node.Label);

            // Build state string based on node type
            string stateStr = "";
            switch (node.Type)
            {
                case NodeType.SpecialFilter:
                case NodeType.ThingDef:
                    stateStr = node.IsChecked ? " checked" : " not checked";
                    break;
                case NodeType.Category:
                    stateStr = node.IsExpanded ? " expanded" : " collapsed";
                    break;
                case NodeType.Slider:
                    stateStr = " " + GetSliderValueString(node);
                    break;
            }

            if (typeahead.HasActiveSearch)
            {
                TolkHelper.Speak($"{cleanLabel}{stateStr}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'");
            }
            else
            {
                AnnounceCurrentNode();
            }
        }

        #endregion
    }
}
