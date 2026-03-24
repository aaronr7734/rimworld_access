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
            ThingDef          // Individual thing/item checkbox
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
        private static ThingFilter parentFilter = null;
        private static TreeNode_ThingCategory rootNode = null;
        private static List<NavigationNode> flattenedNodes = new List<NavigationNode>();
        private static int selectedIndex = 0;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        // Track expanded categories by defName (default is collapsed, matching vanilla)
        private static HashSet<string> expandedCategories = new HashSet<string>();

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
        /// Gets the current selected index in the flattened navigation list.
        /// Used by ReadingPolicyEditorState to save position when switching panels.
        /// </summary>
        public static int GetCurrentIndex() => selectedIndex;

        /// <summary>
        /// Sets the current selected index with bounds checking.
        /// Used by ReadingPolicyEditorState to restore position when switching panels.
        /// </summary>
        public static void SetCurrentIndex(int index)
        {
            if (flattenedNodes.Count > 0 && index >= 0 && index < flattenedNodes.Count)
            {
                selectedIndex = index;
            }
        }

        /// <summary>
        /// Activates filter navigation for a given ThingFilter.
        /// </summary>
        /// <param name="initialIndex">Optional starting index (used by ReadingPolicyEditorState to restore position when switching panels).</param>
        public static void Activate(ThingFilter filter, ThingFilter parentFilter, TreeNode_ThingCategory root, bool showQuality, bool showHitPoints, int initialIndex = 0)
        {
            isActive = true;
            currentFilter = filter;
            ThingFilterNavigationState.parentFilter = parentFilter;
            rootNode = root;
            hasQualitySlider = showQuality;
            hasHitPointsSlider = showHitPoints;
            currentSliderMode = SliderMode.None;
            currentSliderPart = SliderPart.Min;
            isEditingSlider = false;
            typeahead.ClearSearch();
            expandedCategories.Clear();
            MenuHelper.ResetLevel("ThingFilter");

            RebuildNavigationList();
            selectedIndex = (initialIndex >= 0 && initialIndex < flattenedNodes.Count) ? initialIndex : 0;
            AnnounceCurrentNode();
        }

        /// <summary>
        /// Deactivates filter navigation.
        /// </summary>
        public static void Deactivate()
        {
            isActive = false;
            currentFilter = null;
            parentFilter = null;
            rootNode = null;
            flattenedNodes.Clear();
            selectedIndex = 0;
            typeahead.ClearSearch();
            expandedCategories.Clear();
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
                    Label = "HitPointsBasic".Translate().CapitalizeFirst(),
                    Description = "",
                    Data = "HitPoints"
                });
            }

            if (hasQualitySlider)
            {
                flattenedNodes.Add(new NavigationNode
                {
                    Type = NodeType.Slider,
                    IndentLevel = 0,
                    Label = "Quality".Translate(),
                    Description = "",
                    Data = "Quality"
                });
            }

            // Build tree
            if (rootNode != null)
            {
                AddCategoryChildren(rootNode, 0, isRoot: true);
            }

        }

        /// <summary>
        /// Recursively adds category children to the flattened list.
        /// </summary>
        private static void AddCategoryChildren(TreeNode_ThingCategory node, int indentLevel, bool isRoot = false)
        {
            // Add parent special filters at root level (e.g., AllowRotten, AllowFresh from Root category)
            if (isRoot)
            {
                foreach (var specialFilter in node.catDef.ParentsSpecialThingFilterDefs)
                {
                    if (specialFilter.configurable && ThingFilterHelper.IsVisibleSpecialFilter(specialFilter, parentFilter))
                    {
                        flattenedNodes.Add(new NavigationNode
                        {
                            Type = NodeType.SpecialFilter,
                            IndentLevel = indentLevel,
                            Label = specialFilter.LabelCap,
                            Description = specialFilter.description,
                            IsChecked = currentFilter.Allows(specialFilter),
                            Data = specialFilter
                        });
                    }
                }
            }

            // Add special filters
            foreach (var specialFilter in node.catDef.childSpecialFilters)
            {
                if (specialFilter.configurable && ThingFilterHelper.IsVisibleSpecialFilter(specialFilter, parentFilter))
                {
                    flattenedNodes.Add(new NavigationNode
                    {
                        Type = NodeType.SpecialFilter,
                        IndentLevel = indentLevel,
                        Label = specialFilter.LabelCap,
                        Description = specialFilter.description,
                        IsChecked = currentFilter.Allows(specialFilter),
                        Data = specialFilter
                    });
                }
            }

            // Add child categories
            foreach (var childCategory in node.ChildCategoryNodes)
            {
                if (!ThingFilterHelper.IsVisibleCategory(childCategory, parentFilter))
                    continue;

                // Tri-state: check if any visible descendants are allowed
                var allowanceState = ThingFilterHelper.GetAllowanceState(
                    childCategory.catDef, currentFilter, td => ThingFilterHelper.IsVisible(td, parentFilter));
                bool isChecked = (allowanceState != ThingFilterHelper.CategoryAllowanceState.NoneAllowed);

                string categoryKey = childCategory.catDef.defName;
                bool isExpanded = expandedCategories.Contains(categoryKey);

                flattenedNodes.Add(new NavigationNode
                {
                    Type = NodeType.Category,
                    IndentLevel = indentLevel,
                    Label = childCategory.LabelCap,
                    Description = childCategory.catDef.description,
                    IsExpanded = isExpanded,
                    IsChecked = isChecked,
                    Data = childCategory
                });

                // Recursively add children if expanded
                if (isExpanded)
                {
                    AddCategoryChildren(childCategory, indentLevel + 1);
                }
            }

            // Add thing defs (with full vanilla visibility check)
            foreach (var thingDef in node.catDef.childThingDefs.OrderBy(t => t.label))
            {
                if (ThingFilterHelper.IsVisible(thingDef, parentFilter))
                {
                    flattenedNodes.Add(new NavigationNode
                    {
                        Type = NodeType.ThingDef,
                        IndentLevel = indentLevel,
                        Label = thingDef.LabelCap,
                        Description = thingDef.DescriptionDetailed,
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
                currentSliderPart = SliderPart.Min;
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
                AnnounceSliderPartValue(includePartName: true);
            }
        }

        /// <summary>
        /// Announces the current slider editing state.
        /// </summary>
        private static void AnnounceSliderEditMode()
        {
            string sliderName = currentSliderMode == SliderMode.Quality
                ? "Quality".Translate()
                : "HitPointsBasic".Translate().CapitalizeFirst();
            string partName = currentSliderPart == SliderPart.Min ? "Minimum" : "Maximum";

            if (currentSliderMode == SliderMode.Quality)
            {
                var range = currentFilter.AllowedQualityLevels;
                string value = currentSliderPart == SliderPart.Min ? range.min.GetLabel() : range.max.GetLabel();
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
        /// Announces just the current slider value, without help text instructions.
        /// Used during value adjustment (Left/Right) and part switching (Up/Down).
        /// </summary>
        private static void AnnounceSliderPartValue(bool includePartName)
        {
            string partName = currentSliderPart == SliderPart.Min ? "Minimum" : "Maximum";

            if (currentSliderMode == SliderMode.Quality)
            {
                var range = currentFilter.AllowedQualityLevels;
                string value = currentSliderPart == SliderPart.Min ? range.min.GetLabel() : range.max.GetLabel();
                TolkHelper.Speak(includePartName ? $"{partName}: {value}" : value);
            }
            else if (currentSliderMode == SliderMode.HitPoints)
            {
                var range = currentFilter.AllowedHitPointsPercents;
                string value = currentSliderPart == SliderPart.Min ? $"{range.min:P0}" : $"{range.max:P0}";
                TolkHelper.Speak(includePartName ? $"{partName}: {value}" : value);
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

            string cleanLabel = node.Label;

            switch (node.Type)
            {
                case NodeType.SpecialFilter:
                    var specialFilter = node.Data as SpecialThingFilterDef;
                    if (specialFilter != null)
                    {
                        bool desired = !currentFilter.Allows(specialFilter);
                        currentFilter.SetAllow(specialFilter, desired);
                        // Re-read from game to verify actual state
                        node.IsChecked = currentFilter.Allows(specialFilter);
                        TolkHelper.Speak($"{cleanLabel}: {(node.IsChecked ? "Allowed" : "Disallowed")}");
                    }
                    break;

                case NodeType.Category:
                    var category = node.Data as TreeNode_ThingCategory;
                    if (category != null)
                    {
                        // Tri-state toggle matching vanilla: Off→On, Partial→On, On→Off
                        var state = ThingFilterHelper.GetAllowanceState(
                            category.catDef, currentFilter, td => ThingFilterHelper.IsVisible(td, parentFilter));
                        bool desired = (state != ThingFilterHelper.CategoryAllowanceState.AllAllowed);
                        currentFilter.SetAllow(category.catDef, desired);
                        // Rebuild re-reads all states from the game
                        RebuildNavigationList();
                        // Restore selection after rebuild using stable data reference
                        int restored = FindNodeByData(NodeType.Category, category);
                        if (restored >= 0) selectedIndex = restored;
                        // Re-read actual state for announcement
                        var actualState = ThingFilterHelper.GetAllowanceState(
                            category.catDef, currentFilter, td => ThingFilterHelper.IsVisible(td, parentFilter));
                        string catResult = actualState == ThingFilterHelper.CategoryAllowanceState.NoneAllowed ? "Disallowed" : "Allowed";
                        TolkHelper.Speak($"{cleanLabel}: {catResult}");
                    }
                    break;

                case NodeType.ThingDef:
                    var thingDef = node.Data as ThingDef;
                    if (thingDef != null)
                    {
                        bool desired = !currentFilter.Allows(thingDef);
                        currentFilter.SetAllow(thingDef, desired);
                        // Re-read from game to verify actual state
                        node.IsChecked = currentFilter.Allows(thingDef);
                        TolkHelper.Speak($"{cleanLabel}: {(node.IsChecked ? "Allowed" : "Disallowed")}");
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

            // Clear search when expanding to avoid stale search state
            typeahead.ClearSearch();

            var node = flattenedNodes[selectedIndex];

            // Case 1: Collapsed category - expand it, focus stays
            if (node.Type == NodeType.Category && !node.IsExpanded)
            {
                // Add to expanded set
                if (node.Data is TreeNode_ThingCategory catNode)
                {
                    expandedCategories.Add(catNode.catDef.defName);
                }
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

            // Case 3: End node (ThingDef, Slider, SpecialFilter) - reject
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

            // Clear search when collapsing to avoid stale search state
            typeahead.ClearSearch();

            var node = flattenedNodes[selectedIndex];

            // Case 1: Expanded category - collapse it, focus stays
            if (node.Type == NodeType.Category && node.IsExpanded)
            {
                // Remove from expanded set so it stays collapsed after rebuild
                if (node.Data is TreeNode_ThingCategory catNode)
                {
                    expandedCategories.Remove(catNode.catDef.defName);
                }
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
        /// Finds the index of a node in the flattened list by type and data identity.
        /// Data objects (TreeNode_ThingCategory, ThingDef, SpecialThingFilterDef) are
        /// game singletons that survive list rebuilds.
        /// </summary>
        private static int FindNodeIndex(NavigationNode node)
        {
            return FindNodeByData(node.Type, node.Data);
        }

        /// <summary>
        /// Finds a node index by type and data reference.
        /// </summary>
        private static int FindNodeByData(NodeType type, object data)
        {
            for (int i = 0; i < flattenedNodes.Count; i++)
            {
                if (flattenedNodes[i].Type == type && Equals(flattenedNodes[i].Data, data))
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
                    if (node.Data is TreeNode_ThingCategory expandCat)
                        expandedCategories.Add(expandCat.catDef.defName);
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
        /// Jumps to the first sibling at the same indent level (Home key) or absolute first (Ctrl+Home).
        /// </summary>
        public static void JumpToFirst(bool ctrlPressed = false)
        {
            if (flattenedNodes == null || flattenedNodes.Count == 0)
                return;

            MenuHelper.HandleTreeHomeKey(flattenedNodes, ref selectedIndex, node => node.IndentLevel, ctrlPressed, ClearAndAnnounce);
        }

        /// <summary>
        /// Jumps to the last item in scope (End key) or absolute last (Ctrl+End).
        /// For expanded nodes: jumps to last visible descendant.
        /// For collapsed/leaf nodes: jumps to last sibling.
        /// </summary>
        public static void JumpToLast(bool ctrlPressed = false)
        {
            if (flattenedNodes == null || flattenedNodes.Count == 0)
                return;

            MenuHelper.HandleTreeEndKey(
                flattenedNodes,
                ref selectedIndex,
                node => node.IndentLevel,
                node => node.Type == NodeType.Category && node.IsExpanded,
                node => HasVisibleChildren(node),
                ctrlPressed,
                ClearAndAnnounce);
        }

        /// <summary>
        /// Checks if a node has visible children (next item has higher indent level).
        /// </summary>
        private static bool HasVisibleChildren(NavigationNode node)
        {
            if (node.Type != NodeType.Category || !node.IsExpanded)
                return false;

            int nodeIndex = flattenedNodes.IndexOf(node);
            if (nodeIndex < 0 || nodeIndex >= flattenedNodes.Count - 1)
                return false;

            return flattenedNodes[nodeIndex + 1].IndentLevel > node.IndentLevel;
        }

        /// <summary>
        /// Clears typeahead search and announces current node.
        /// </summary>
        private static void ClearAndAnnounce()
        {
            typeahead.ClearSearch();
            AnnounceCurrentNode();
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
                AnnounceSliderPartValue(includePartName: false);
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
                AnnounceSliderPartValue(includePartName: false);
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
                TolkHelper.Speak($"{"Quality".Translate()}: {range.min.GetLabel()} - {range.max.GetLabel()}");
            }
            else if (sliderType == "HitPoints")
            {
                var range = currentFilter.AllowedHitPointsPercents;
                TolkHelper.Speak($"{"HitPointsBasic".Translate().CapitalizeFirst()}: {range.min:P0} - {range.max:P0}");
            }
        }

        /// <summary>
        /// Allows all items (convenience function).
        /// </summary>
        public static void AllowAll()
        {
            if (currentFilter != null)
            {
                currentFilter.SetAllowAll(parentFilter);
                RebuildNavigationList();
                TolkHelper.Speak("AllowAll".Translate());
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
                TolkHelper.Speak("ClearAll".Translate());
            }
        }

        /// <summary>
        /// Announces the current node.
        /// Format: "{name}. {state}. {X of Y}. level N"
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
            string suffix = MenuHelper.GetLevelSuffix("ThingFilter", node.IndentLevel);
            string posStr = MenuHelper.FormatPosition(position - 1, total);

            string announcement;

            string cleanLabel = node.Label;

            switch (node.Type)
            {
                case NodeType.Slider:
                    string sliderValue = GetSliderValueString(node);
                    announcement = $"{cleanLabel} {sliderValue}. {posStr}{suffix}";
                    break;

                case NodeType.SpecialFilter:
                    string specialState = node.IsChecked ? "allowed" : "disallowed";
                    string specialDesc = string.IsNullOrEmpty(node.Description) ? "" : $" {node.Description}";
                    announcement = $"{cleanLabel}. {specialState}.{specialDesc} {posStr}{suffix}";
                    break;

                case NodeType.Category:
                    var catNodeAnnounce = node.Data as TreeNode_ThingCategory;
                    string categorySummary = "disallowed";
                    if (catNodeAnnounce != null)
                    {
                        var summary = ThingFilterHelper.GetCategorySummary(
                            catNodeAnnounce.catDef, currentFilter, td => ThingFilterHelper.IsVisible(td, parentFilter));
                        categorySummary = ThingFilterHelper.FormatCategorySummary(summary);
                    }
                    string categoryExpanded = node.IsExpanded ? "expanded" : "collapsed";
                    string categoryDesc = string.IsNullOrEmpty(node.Description) ? "" : $" {node.Description}";
                    announcement = $"{cleanLabel}. {categorySummary}, {categoryExpanded}.{categoryDesc} {posStr}{suffix}";
                    break;

                case NodeType.ThingDef:
                    string thingState = node.IsChecked ? "allowed" : "disallowed";
                    string thingDesc = string.IsNullOrEmpty(node.Description) ? "" : $" {node.Description}";
                    announcement = $"{cleanLabel}. {thingState}.{thingDesc} {posStr}{suffix}";
                    break;

                default:
                    announcement = $"{cleanLabel}. {posStr}{suffix}";
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
                return $"{range.min.GetLabel()} - {range.max.GetLabel()}";
            }
            else if (sliderType == "HitPoints")
            {
                var range = currentFilter.AllowedHitPointsPercents;
                return $"{range.min:P0} - {range.max:P0}";
            }

            return "";
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
                labels.Add(node.Label);
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
            string cleanLabel = node.Label;

            // Build state string based on node type
            string stateStr = "";
            switch (node.Type)
            {
                case NodeType.SpecialFilter:
                case NodeType.ThingDef:
                    stateStr = node.IsChecked ? " allowed" : " disallowed";
                    break;
                case NodeType.Category:
                    var searchCatNode = node.Data as TreeNode_ThingCategory;
                    string searchCatState = "disallowed";
                    if (searchCatNode != null)
                    {
                        var searchSummary = ThingFilterHelper.GetCategorySummary(
                            searchCatNode.catDef, currentFilter, td => ThingFilterHelper.IsVisible(td, parentFilter));
                        searchCatState = ThingFilterHelper.FormatCategorySummary(searchSummary);
                    }
                    string searchCatExpand = node.IsExpanded ? "expanded" : "collapsed";
                    stateStr = $" {searchCatState}, {searchCatExpand}";
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
