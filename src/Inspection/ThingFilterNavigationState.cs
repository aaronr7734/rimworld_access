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
    /// Uses TreeNavigationHelper for all standard treeview keyboard handling.
    /// </summary>
    public static class ThingFilterNavigationState
    {
        // Navigation node types stored in InspectionTreeItem.Data
        public enum NodeType
        {
            Slider,           // Quality or hit points slider (press Enter to edit)
            SpecialFilter,    // Special filter checkbox (marked with *)
            Category,         // Category with children (can expand/collapse)
            ThingDef          // Individual thing/item checkbox
        }

        /// <summary>
        /// Data stored in InspectionTreeItem.Data for filter nodes.
        /// </summary>
        public class FilterNodeData
        {
            public NodeType Type;
            public bool IsChecked;       // For checkboxes
            public object Reference;     // ThingCategoryDef, ThingDef, SpecialThingFilterDef, or string for sliders
        }

        private static bool isActive = false;
        private static ThingFilter currentFilter = null;
        private static ThingFilter parentFilter = null;
        private static TreeNode_ThingCategory rootNode = null;
        private static TreeNavigationHelper treeNav = new TreeNavigationHelper("ThingFilter");

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
        public static bool HasActiveSearch => treeNav.HasActiveSearch;
        public static bool HasNoMatches => treeNav.HasNoMatches;

        static ThingFilterNavigationState()
        {
            treeNav.FormatItemAnnouncement = FormatItemAnnouncement;
            treeNav.FormatSearchAnnouncement = FormatSearchAnnouncement;
            treeNav.OnActivate = HandleActivate;
            treeNav.AnnounceChildCounts = false;
        }

        /// <summary>
        /// Gets the current selected index in the flattened navigation list.
        /// Used by ReadingPolicyEditorState to save position when switching panels.
        /// </summary>
        public static int GetCurrentIndex() => treeNav.SelectedIndex;

        /// <summary>
        /// Sets the current selected index with bounds checking.
        /// Used by ReadingPolicyEditorState to restore position when switching panels.
        /// </summary>
        public static void SetCurrentIndex(int index)
        {
            if (treeNav.Count > 0 && index >= 0 && index < treeNav.Count)
            {
                treeNav.SetSelectedIndex(index);
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
            expandedCategories.Clear();

            var treeRoot = BuildTree();
            treeNav.Initialize(treeRoot, initialIndex);
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
            expandedCategories.Clear();
            treeNav.Reset();
        }

        /// <summary>
        /// Builds the tree structure from the ThingFilter data.
        /// </summary>
        private static InspectionTreeItem BuildTree()
        {
            var root = new InspectionTreeItem
            {
                Label = "Root",
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false
            };

            // Add sliders at top
            if (hasHitPointsSlider)
            {
                var sliderNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "HitPointsBasic".Translate().CapitalizeFirst(),
                    IndentLevel = 0,
                    IsExpandable = false,
                    Parent = root,
                    Data = new FilterNodeData
                    {
                        Type = NodeType.Slider,
                        Reference = "HitPoints"
                    }
                };
                root.Children.Add(sliderNode);
            }

            if (hasQualitySlider)
            {
                var sliderNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "Quality".Translate(),
                    IndentLevel = 0,
                    IsExpandable = false,
                    Parent = root,
                    Data = new FilterNodeData
                    {
                        Type = NodeType.Slider,
                        Reference = "Quality"
                    }
                };
                root.Children.Add(sliderNode);
            }

            // Build tree from categories
            if (rootNode != null)
            {
                AddCategoryChildren(rootNode, root, 0, isRoot: true);
            }

            return root;
        }

        /// <summary>
        /// Recursively adds category children to the tree.
        /// </summary>
        private static void AddCategoryChildren(TreeNode_ThingCategory node, InspectionTreeItem parent, int indentLevel, bool isRoot = false)
        {
            // Add parent special filters at root level (e.g., AllowRotten, AllowFresh from Root category)
            if (isRoot)
            {
                foreach (var specialFilter in node.catDef.ParentsSpecialThingFilterDefs)
                {
                    if (specialFilter.configurable && ThingFilterHelper.IsVisibleSpecialFilter(specialFilter, parentFilter))
                    {
                        var sfNode = new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.Item,
                            Label = specialFilter.LabelCap,
                            Description = specialFilter.description,
                            IndentLevel = indentLevel,
                            IsExpandable = false,
                            Parent = parent,
                            Data = new FilterNodeData
                            {
                                Type = NodeType.SpecialFilter,
                                IsChecked = currentFilter.Allows(specialFilter),
                                Reference = specialFilter
                            }
                        };
                        parent.Children.Add(sfNode);
                    }
                }
            }

            // Add special filters
            foreach (var specialFilter in node.catDef.childSpecialFilters)
            {
                if (specialFilter.configurable && ThingFilterHelper.IsVisibleSpecialFilter(specialFilter, parentFilter))
                {
                    var sfNode = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = specialFilter.LabelCap,
                        Description = specialFilter.description,
                        IndentLevel = indentLevel,
                        IsExpandable = false,
                        Parent = parent,
                        Data = new FilterNodeData
                        {
                            Type = NodeType.SpecialFilter,
                            IsChecked = currentFilter.Allows(specialFilter),
                            Reference = specialFilter
                        }
                    };
                    parent.Children.Add(sfNode);
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

                var catNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Category,
                    Label = childCategory.LabelCap,
                    Description = childCategory.catDef.description,
                    IndentLevel = indentLevel,
                    IsExpandable = true,
                    IsExpanded = isExpanded,
                    Parent = parent,
                    Data = new FilterNodeData
                    {
                        Type = NodeType.Category,
                        IsChecked = isChecked,
                        Reference = childCategory
                    }
                };
                parent.Children.Add(catNode);

                // Recursively add children if expanded
                if (isExpanded)
                {
                    AddCategoryChildren(childCategory, catNode, indentLevel + 1);
                }
            }

            // Add thing defs (with full vanilla visibility check)
            foreach (var thingDef in node.catDef.childThingDefs.OrderBy(t => t.label))
            {
                if (ThingFilterHelper.IsVisible(thingDef, parentFilter))
                {
                    var tdNode = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = thingDef.LabelCap,
                        Description = thingDef.DescriptionDetailed,
                        IndentLevel = indentLevel,
                        IsExpandable = false,
                        Parent = parent,
                        LinkedDef = thingDef,
                        Data = new FilterNodeData
                        {
                            Type = NodeType.ThingDef,
                            IsChecked = currentFilter.Allows(thingDef),
                            Reference = thingDef
                        }
                    };
                    parent.Children.Add(tdNode);
                }
            }
        }

        /// <summary>
        /// Rebuilds the tree, preserving selection by data reference.
        /// </summary>
        private static void RebuildTree()
        {
            var oldItem = treeNav.SelectedItem;
            FilterNodeData oldData = oldItem?.Data as FilterNodeData;
            int oldIndex = treeNav.SelectedIndex;

            var treeRoot = BuildTree();
            treeNav.Initialize(treeRoot);

            // Try to restore selection by data reference
            if (oldData != null)
            {
                for (int i = 0; i < treeNav.Count; i++)
                {
                    var itemData = treeNav.VisibleItems[i].Data as FilterNodeData;
                    if (itemData != null && itemData.Type == oldData.Type && Equals(itemData.Reference, oldData.Reference))
                    {
                        treeNav.SetSelectedIndex(i);
                        return;
                    }
                }
            }

            // Fall back to old index
            if (oldIndex >= 0 && oldIndex < treeNav.Count)
                treeNav.SetSelectedIndex(oldIndex);
        }

        /// <summary>
        /// Moves selection to the next node.
        /// </summary>
        public static void SelectNext()
        {
            treeNav.SelectNext();
        }

        /// <summary>
        /// Moves selection to the previous node.
        /// </summary>
        public static void SelectPrevious()
        {
            treeNav.SelectPrevious();
        }

        /// <summary>
        /// Activates the current selection:
        /// - Sliders: Enter editing mode
        /// - Checkboxes (SpecialFilter, Category, ThingDef): Toggle checked state
        /// </summary>
        public static void ActivateSelected()
        {
            if (treeNav.SelectedItem == null)
                return;

            var item = treeNav.SelectedItem;
            var data = item.Data as FilterNodeData;
            if (data == null) return;

            if (data.Type == NodeType.Slider)
            {
                // Enter slider editing mode
                isEditingSlider = true;
                currentSliderPart = SliderPart.Min;
                string sliderType = data.Reference as string;
                if (sliderType == "Quality")
                    currentSliderMode = SliderMode.Quality;
                else if (sliderType == "HitPoints")
                    currentSliderMode = SliderMode.HitPoints;

                AnnounceSliderEditMode();
            }
            else if (data.Type == NodeType.SpecialFilter || data.Type == NodeType.Category || data.Type == NodeType.ThingDef)
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
            if (treeNav.SelectedItem == null)
                return;

            var item = treeNav.SelectedItem;
            var data = item.Data as FilterNodeData;
            if (data == null) return;

            string cleanLabel = item.Label;

            switch (data.Type)
            {
                case NodeType.SpecialFilter:
                    var specialFilter = data.Reference as SpecialThingFilterDef;
                    if (specialFilter != null)
                    {
                        bool desired = !currentFilter.Allows(specialFilter);
                        currentFilter.SetAllow(specialFilter, desired);
                        // Re-read from game to verify actual state
                        data.IsChecked = currentFilter.Allows(specialFilter);
                        TolkHelper.Speak($"{cleanLabel}: {(data.IsChecked ? "Allowed" : "Disallowed")}");
                    }
                    break;

                case NodeType.Category:
                    var category = data.Reference as TreeNode_ThingCategory;
                    if (category != null)
                    {
                        // Tri-state toggle matching vanilla: Off→On, Partial→On, On→Off
                        var state = ThingFilterHelper.GetAllowanceState(
                            category.catDef, currentFilter, td => ThingFilterHelper.IsVisible(td, parentFilter));
                        bool desiredCat = (state != ThingFilterHelper.CategoryAllowanceState.AllAllowed);
                        currentFilter.SetAllow(category.catDef, desiredCat);
                        // Rebuild re-reads all states from the game
                        RebuildTree();
                        // Re-read actual state for announcement
                        var actualState = ThingFilterHelper.GetAllowanceState(
                            category.catDef, currentFilter, td => ThingFilterHelper.IsVisible(td, parentFilter));
                        string catResult = actualState == ThingFilterHelper.CategoryAllowanceState.NoneAllowed ? "Disallowed" : "Allowed";
                        TolkHelper.Speak($"{cleanLabel}: {catResult}");
                    }
                    break;

                case NodeType.ThingDef:
                    var thingDef = data.Reference as ThingDef;
                    if (thingDef != null)
                    {
                        bool desiredThing = !currentFilter.Allows(thingDef);
                        currentFilter.SetAllow(thingDef, desiredThing);
                        // Re-read from game to verify actual state
                        data.IsChecked = currentFilter.Allows(thingDef);
                        TolkHelper.Speak($"{cleanLabel}: {(data.IsChecked ? "Allowed" : "Disallowed")}");
                    }
                    break;

                case NodeType.Slider:
                    // For sliders, toggle just announces current value
                    AnnounceSliderValue(item);
                    break;
            }
        }

        /// <summary>
        /// Expands the current category node (Right arrow - WCAG tree navigation).
        /// Delegates to TreeNavigationHelper with custom expand callback to sync expandedCategories.
        /// </summary>
        public static void Expand()
        {
            if (treeNav.SelectedItem == null)
                return;

            var item = treeNav.SelectedItem;
            var data = item.Data as FilterNodeData;

            // Non-expandable items - reject
            if (data == null || data.Type != NodeType.Category)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Cannot expand this item.");
                return;
            }

            if (!item.IsExpanded)
            {
                // Need to rebuild tree since children are lazy-loaded based on expandedCategories
                if (data.Reference is TreeNode_ThingCategory catNode)
                {
                    expandedCategories.Add(catNode.catDef.defName);
                }
                RebuildTree();
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentNode();
            }
            else if (item.Children.Count > 0)
            {
                // Already expanded - drill down to first child
                treeNav.ExpandOrDrillDown();
            }
            else
            {
                // Expanded but empty
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Cannot expand this item.");
            }
        }

        /// <summary>
        /// Collapses the current category node or moves to parent (Left arrow - WCAG tree navigation).
        /// </summary>
        public static void Collapse()
        {
            if (treeNav.SelectedItem == null)
                return;

            var item = treeNav.SelectedItem;
            var data = item.Data as FilterNodeData;

            // Case 1: Expanded category - collapse it
            if (data != null && data.Type == NodeType.Category && item.IsExpanded)
            {
                if (data.Reference is TreeNode_ThingCategory catNode)
                {
                    expandedCategories.Remove(catNode.catDef.defName);
                }
                RebuildTree();
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentNode();
                return;
            }

            // Case 2: Collapsed/end node - drill up to parent
            if (item.Parent != null && item.Parent != treeNav.RootItem)
            {
                treeNav.CollapseOrDrillUp();
                return;
            }

            // Case 3: At root level - reject
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("Already at top level");
        }

        /// <summary>
        /// Expands all sibling categories at the same level as the current item.
        /// WCAG tree view pattern: * key expands all siblings.
        /// </summary>
        public static void ExpandAllSiblings()
        {
            if (treeNav.SelectedItem == null)
                return;

            var currentItem = treeNav.SelectedItem;
            var siblings = (currentItem.Parent == null || currentItem.Parent == treeNav.RootItem)
                ? treeNav.RootItem.Children
                : currentItem.Parent.Children;

            int expandedCount = 0;
            foreach (var sibling in siblings)
            {
                var sibData = sibling.Data as FilterNodeData;
                if (sibData != null && sibData.Type == NodeType.Category && !sibling.IsExpanded)
                {
                    if (sibData.Reference is TreeNode_ThingCategory catNode)
                    {
                        expandedCategories.Add(catNode.catDef.defName);
                        expandedCount++;
                    }
                }
            }

            if (expandedCount > 0)
            {
                RebuildTree();
                EmbeddedAudioHelper.PlaySoundDefWithReverb(SoundDefOf.FloatMenu_Open);
                if (expandedCount == 1)
                    TolkHelper.Speak("Expanded 1 category");
                else
                    TolkHelper.Speak($"Expanded {expandedCount} categories");
            }
            else
            {
                // Check if there are any sibling categories at all
                bool hasAnySiblingCategories = siblings.Any(s =>
                {
                    var sd = s.Data as FilterNodeData;
                    return sd != null && sd.Type == NodeType.Category;
                });

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
            treeNav.JumpToFirst(ctrlPressed);
        }

        /// <summary>
        /// Jumps to the last item in scope (End key) or absolute last (Ctrl+End).
        /// For expanded nodes: jumps to last visible descendant.
        /// For collapsed/leaf nodes: jumps to last sibling.
        /// </summary>
        public static void JumpToLast(bool ctrlPressed = false)
        {
            treeNav.JumpToLast(ctrlPressed);
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
                if (treeNav.SelectedItem == null)
                    return;

                var data = treeNav.SelectedItem.Data as FilterNodeData;
                if (data != null && data.Type == NodeType.Slider)
                {
                    AnnounceSliderValue(treeNav.SelectedItem);
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
        private static void AnnounceSliderValue(InspectionTreeItem item)
        {
            var data = item.Data as FilterNodeData;
            if (data == null) return;

            string sliderType = data.Reference as string;

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
                RebuildTree();
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
                RebuildTree();
                TolkHelper.Speak("ClearAll".Translate());
            }
        }

        /// <summary>
        /// Gets the current value string for a slider node.
        /// </summary>
        private static string GetSliderValueString(InspectionTreeItem item)
        {
            var data = item.Data as FilterNodeData;
            if (data == null) return "";

            string sliderType = data.Reference as string;

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

        #region Announcement Formatters

        /// <summary>
        /// Announces the current node via TreeNavigationHelper.
        /// </summary>
        private static void AnnounceCurrentNode()
        {
            treeNav.ReannounceCurrentItem();
        }

        /// <summary>
        /// Custom item announcement formatter.
        /// Format: "{name}. {state}. {X of Y}. level N"
        /// </summary>
        private static string FormatItemAnnouncement(InspectionTreeItem item)
        {
            var data = item.Data as FilterNodeData;
            if (data == null)
                return item.Label;

            var (position, total) = treeNav.GetSiblingPosition(item);
            string suffix = MenuHelper.GetLevelSuffix("ThingFilter", item.IndentLevel);
            string posStr = MenuHelper.FormatPosition(position - 1, total);

            string cleanLabel = item.Label;

            switch (data.Type)
            {
                case NodeType.Slider:
                    string sliderValue = GetSliderValueString(item);
                    return $"{cleanLabel} {sliderValue}. {posStr}{suffix}";

                case NodeType.SpecialFilter:
                    string specialState = data.IsChecked ? "allowed" : "disallowed";
                    string specialDesc = string.IsNullOrEmpty(item.Description) ? "" : $" {item.Description}";
                    return $"{cleanLabel}. {specialState}.{specialDesc} {posStr}{suffix}";

                case NodeType.Category:
                    var catNode = data.Reference as TreeNode_ThingCategory;
                    string categorySummary = "disallowed";
                    if (catNode != null)
                    {
                        var summary = ThingFilterHelper.GetCategorySummary(
                            catNode.catDef, currentFilter, td => ThingFilterHelper.IsVisible(td, parentFilter));
                        categorySummary = ThingFilterHelper.FormatCategorySummary(summary);
                    }
                    string categoryExpanded = item.IsExpanded ? "expanded" : "collapsed";
                    string categoryDesc = string.IsNullOrEmpty(item.Description) ? "" : $" {item.Description}";
                    return $"{cleanLabel}. {categorySummary}, {categoryExpanded}.{categoryDesc} {posStr}{suffix}";

                case NodeType.ThingDef:
                    string thingState = data.IsChecked ? "allowed" : "disallowed";
                    string thingDesc = string.IsNullOrEmpty(item.Description) ? "" : $" {item.Description}";
                    return $"{cleanLabel}. {thingState}.{thingDesc} {posStr}{suffix}";

                default:
                    return $"{cleanLabel}. {posStr}{suffix}";
            }
        }

        /// <summary>
        /// Custom search announcement formatter.
        /// </summary>
        private static string FormatSearchAnnouncement(InspectionTreeItem item, TypeaheadSearchHelper typeahead)
        {
            var data = item.Data as FilterNodeData;
            if (data == null)
                return item.Label;

            string cleanLabel = item.Label;

            // Build state string based on node type
            string stateStr = "";
            switch (data.Type)
            {
                case NodeType.SpecialFilter:
                case NodeType.ThingDef:
                    stateStr = data.IsChecked ? " allowed" : " disallowed";
                    break;
                case NodeType.Category:
                    var searchCatNode = data.Reference as TreeNode_ThingCategory;
                    string searchCatState = "disallowed";
                    if (searchCatNode != null)
                    {
                        var searchSummary = ThingFilterHelper.GetCategorySummary(
                            searchCatNode.catDef, currentFilter, td => ThingFilterHelper.IsVisible(td, parentFilter));
                        searchCatState = ThingFilterHelper.FormatCategorySummary(searchSummary);
                    }
                    string searchCatExpand = item.IsExpanded ? "expanded" : "collapsed";
                    stateStr = $" {searchCatState}, {searchCatExpand}";
                    break;
                case NodeType.Slider:
                    stateStr = " " + GetSliderValueString(item);
                    break;
            }

            if (typeahead.HasActiveSearch)
            {
                return $"{cleanLabel}{stateStr}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'";
            }
            else
            {
                return FormatItemAnnouncement(item);
            }
        }

        /// <summary>
        /// Custom activate handler — returns true if handled (sliders, toggles).
        /// </summary>
        private static bool HandleActivate(InspectionTreeItem item)
        {
            var data = item.Data as FilterNodeData;
            if (data == null) return false;

            if (data.Type == NodeType.Slider)
            {
                ActivateSelected();
                return true;
            }

            if (data.Type == NodeType.SpecialFilter || data.Type == NodeType.ThingDef)
            {
                ToggleSelected();
                return true;
            }

            if (data.Type == NodeType.Category)
            {
                ToggleSelected();
                return true;
            }

            return false;
        }

        #endregion

        #region Typeahead Support

        /// <summary>
        /// Gets the last failed search string.
        /// </summary>
        public static string GetLastFailedSearch()
        {
            return treeNav.Typeahead.LastFailedSearch;
        }

        /// <summary>
        /// Processes a typeahead character for search.
        /// </summary>
        public static void ProcessTypeaheadCharacter(char c)
        {
            if (!isActive || treeNav.Count == 0)
                return;

            var labels = treeNav.VisibleItems.Select(item => item.Label).ToList();
            if (treeNav.Typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    treeNav.SetSelectedIndex(newIndex);
                    AnnounceWithSearch();
                }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{treeNav.Typeahead.LastFailedSearch}'");
            }
        }

        /// <summary>
        /// Processes backspace for typeahead search.
        /// </summary>
        public static void ProcessBackspace()
        {
            if (!isActive || treeNav.Count == 0)
                return;

            if (!treeNav.HasActiveSearch)
                return;

            var labels = treeNav.VisibleItems.Select(item => item.Label).ToList();
            if (treeNav.Typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    treeNav.SetSelectedIndex(newIndex);
                }
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Clears the typeahead search and announces.
        /// </summary>
        public static void ClearTypeaheadSearch()
        {
            treeNav.Typeahead.ClearSearchAndAnnounce();
            AnnounceCurrentNode();
        }

        /// <summary>
        /// Sets the selected index (for typeahead navigation).
        /// </summary>
        public static void SetSelectedIndex(int index)
        {
            if (index >= 0 && index < treeNav.Count)
            {
                treeNav.SetSelectedIndex(index);
            }
        }

        /// <summary>
        /// Selects the next match in the filtered list.
        /// </summary>
        public static void SelectNextMatch()
        {
            if (treeNav.Count == 0)
                return;

            int nextIndex = treeNav.Typeahead.GetNextMatch(treeNav.SelectedIndex);
            if (nextIndex >= 0)
            {
                treeNav.SetSelectedIndex(nextIndex);
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Selects the previous match in the filtered list.
        /// </summary>
        public static void SelectPreviousMatch()
        {
            if (treeNav.Count == 0)
                return;

            int prevIndex = treeNav.Typeahead.GetPreviousMatch(treeNav.SelectedIndex);
            if (prevIndex >= 0)
            {
                treeNav.SetSelectedIndex(prevIndex);
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Announces the current selection with search context if applicable.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (treeNav.SelectedItem == null)
                return;

            var item = treeNav.SelectedItem;

            if (treeNav.HasActiveSearch)
            {
                TolkHelper.Speak(FormatSearchAnnouncement(item, treeNav.Typeahead));
            }
            else
            {
                AnnounceCurrentNode();
            }
        }

        #endregion
    }
}
