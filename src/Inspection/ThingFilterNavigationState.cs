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
            // Auto-expand category nodes during typeahead so matches inside
            // collapsed branches become reachable; original expansion state
            // is restored when the search ends.
            treeNav.ShouldExpandForSearch = item =>
                (item.Data as FilterNodeData)?.Type == NodeType.Category;
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
                    if (specialFilter.configurable && ThingFilterHelper.IsVisibleSpecialFilter(specialFilter, node, currentFilter, parentFilter))
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
                if (specialFilter.configurable && ThingFilterHelper.IsVisibleSpecialFilter(specialFilter, node, currentFilter, parentFilter))
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

                var catNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Category,
                    Label = childCategory.LabelCap,
                    Description = childCategory.catDef.description,
                    IndentLevel = indentLevel,
                    IsExpandable = true,
                    // Default collapsed (matches vanilla). Full subtree is built up
                    // front so TreeNavigationHelper drives expand/collapse natively.
                    IsExpanded = false,
                    Parent = parent,
                    Data = new FilterNodeData
                    {
                        Type = NodeType.Category,
                        IsChecked = isChecked,
                        Reference = childCategory
                    }
                };
                parent.Children.Add(catNode);

                AddCategoryChildren(childCategory, catNode, indentLevel + 1);
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
        /// Walks the existing tree and refreshes IsChecked on every leaf and
        /// category node from the underlying filter. Used after any filter
        /// change so the cursor stays put — no structural rebuild.
        /// </summary>
        private static void RefreshAllowanceStates()
        {
            if (treeNav.RootItem == null) return;
            RefreshAllowanceStatesRecursive(treeNav.RootItem);
        }

        private static void RefreshAllowanceStatesRecursive(InspectionTreeItem node)
        {
            var data = node.Data as FilterNodeData;
            if (data != null)
            {
                switch (data.Type)
                {
                    case NodeType.SpecialFilter:
                        if (data.Reference is SpecialThingFilterDef sf)
                            data.IsChecked = currentFilter.Allows(sf);
                        break;
                    case NodeType.ThingDef:
                        if (data.Reference is ThingDef td)
                            data.IsChecked = currentFilter.Allows(td);
                        break;
                    case NodeType.Category:
                        if (data.Reference is TreeNode_ThingCategory cat)
                        {
                            var state = ThingFilterHelper.GetAllowanceState(
                                cat.catDef, currentFilter,
                                t => ThingFilterHelper.IsVisible(t, parentFilter));
                            data.IsChecked = state != ThingFilterHelper.CategoryAllowanceState.NoneAllowed;
                        }
                        break;
                }
            }

            foreach (var child in node.Children)
                RefreshAllowanceStatesRecursive(child);
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
            string partName = currentSliderPart == SliderPart.Min
                ? (string)"RimWorldAccess.Inspection.ThingFilter.SliderPart.Min".Translate()
                : (string)"RimWorldAccess.Inspection.ThingFilter.SliderPart.Max".Translate();

            if (currentSliderMode == SliderMode.Quality)
            {
                var range = currentFilter.AllowedQualityLevels;
                string value = currentSliderPart == SliderPart.Min ? range.min.GetLabel() : range.max.GetLabel();
                TolkHelper.Speak("RimWorldAccess.Inspection.ThingFilter.SliderEdit.Announcement".Loc(sliderName, partName, value));
            }
            else if (currentSliderMode == SliderMode.HitPoints)
            {
                var range = currentFilter.AllowedHitPointsPercents;
                string value = currentSliderPart == SliderPart.Min ? $"{range.min:P0}" : $"{range.max:P0}";
                TolkHelper.Speak("RimWorldAccess.Inspection.ThingFilter.SliderEdit.Announcement".Loc(sliderName, partName, value));
            }
        }

        /// <summary>
        /// Announces just the current slider value, without help text instructions.
        /// Used during value adjustment (Left/Right) and part switching (Up/Down).
        /// </summary>
        private static void AnnounceSliderPartValue(bool includePartName)
        {
            string partName = currentSliderPart == SliderPart.Min
                ? (string)"RimWorldAccess.Inspection.ThingFilter.SliderPart.Min".Translate()
                : (string)"RimWorldAccess.Inspection.ThingFilter.SliderPart.Max".Translate();

            if (currentSliderMode == SliderMode.Quality)
            {
                var range = currentFilter.AllowedQualityLevels;
                string value = currentSliderPart == SliderPart.Min ? range.min.GetLabel() : range.max.GetLabel();
                TolkHelper.SpeakData(includePartName
                    ? (string)"RimWorldAccess.Inspection.ThingFilter.SliderEdit.PartWithValue".Translate(partName, value)
                    : value);
            }
            else if (currentSliderMode == SliderMode.HitPoints)
            {
                var range = currentFilter.AllowedHitPointsPercents;
                string value = currentSliderPart == SliderPart.Min ? $"{range.min:P0}" : $"{range.max:P0}";
                TolkHelper.SpeakData(includePartName
                    ? (string)"RimWorldAccess.Inspection.ThingFilter.SliderEdit.PartWithValue".Translate(partName, value)
                    : value);
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

            // Commit on activation: clear any active typeahead and restore the
            // pre-search expansion. Matches expand/collapse behavior so a
            // subsequent Down arrow walks the full list rather than only matches.
            treeNav.CommitAndClearSearch();

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
                        TolkHelper.SpeakData((string)(data.IsChecked
                            ? "RimWorldAccess.Inspection.Storage.ToggleResultAllowed"
                            : "RimWorldAccess.Inspection.Storage.ToggleResultDisallowed").Translate(cleanLabel));
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
                        // Re-read all states from the game without disturbing the tree structure.
                        RefreshAllowanceStates();
                        var actualState = ThingFilterHelper.GetAllowanceState(
                            category.catDef, currentFilter, td => ThingFilterHelper.IsVisible(td, parentFilter));
                        bool catAllowed = actualState != ThingFilterHelper.CategoryAllowanceState.NoneAllowed;
                        TolkHelper.SpeakData((string)(catAllowed
                            ? "RimWorldAccess.Inspection.Storage.ToggleResultAllowed"
                            : "RimWorldAccess.Inspection.Storage.ToggleResultDisallowed").Translate(cleanLabel));
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
                        TolkHelper.SpeakData((string)(data.IsChecked
                            ? "RimWorldAccess.Inspection.Storage.ToggleResultAllowed"
                            : "RimWorldAccess.Inspection.Storage.ToggleResultDisallowed").Translate(cleanLabel));
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
        /// Delegates to TreeNavigationHelper so the standard expand sound plays
        /// and submenu mode lands the cursor on the first child correctly.
        /// </summary>
        public static void Expand()
        {
            if (treeNav.SelectedItem == null) return;

            var data = treeNav.SelectedItem.Data as FilterNodeData;
            if (data == null || data.Type != NodeType.Category)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                // Sliders open a range editor on Enter; teach that instead of a bare rejection.
                TolkHelper.Speak(data != null && data.Type == NodeType.Slider
                    ? "RimWorldAccess.Inspection.Storage.PressEnterEditRange".Loc()
                    : "RimWorldAccess.Inspection.ThingFilter.CannotExpand".Loc());
                return;
            }

            treeNav.ExpandOrDrillDown();
        }

        /// <summary>
        /// Collapses the current category node or moves to parent (Left arrow - WCAG tree navigation).
        /// </summary>
        public static void Collapse()
        {
            if (treeNav.SelectedItem == null) return;
            treeNav.CollapseOrDrillUp();
        }

        /// <summary>
        /// Expands all sibling categories at the same level as the current item.
        /// </summary>
        public static void ExpandAllSiblings()
        {
            treeNav.ExpandAllSiblings();
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
                TolkHelper.SpeakData($"{"Quality".Translate()}: {range.min.GetLabel()} - {range.max.GetLabel()}");
            }
            else if (sliderType == "HitPoints")
            {
                var range = currentFilter.AllowedHitPointsPercents;
                TolkHelper.SpeakData($"{"HitPointsBasic".Translate().CapitalizeFirst()}: {range.min:P0} - {range.max:P0}");
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
                RefreshAllowanceStates();
                TolkHelper.Speak("AllowAll".Loc());
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
                RefreshAllowanceStates();
                TolkHelper.Speak("ClearAll".Loc());
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
                    string specialState = (string)(data.IsChecked
                        ? "RimWorldAccess.Inspection.Storage.ItemAllowed"
                        : "RimWorldAccess.Inspection.Storage.ItemDisallowed").Translate(cleanLabel);
                    string specialDesc = string.IsNullOrEmpty(item.Description) ? "" : $" {item.Description}";
                    return $"{specialState}.{specialDesc} {posStr}{suffix}";

                case NodeType.Category:
                    var catNode = data.Reference as TreeNode_ThingCategory;
                    string categoryExpanded = (string)(item.IsExpanded
                        ? "RimWorldAccess.Tree.StateExpanded"
                        : "RimWorldAccess.Tree.StateCollapsed").Translate();
                    string categoryDesc = string.IsNullOrEmpty(item.Description) ? "" : $" {item.Description}";
                    if (catNode != null)
                    {
                        var summary = ThingFilterHelper.GetCategorySummary(
                            catNode.catDef, currentFilter, td => ThingFilterHelper.IsVisible(td, parentFilter));
                        string categorySummary = ThingFilterHelper.FormatCategorySummary(summary);
                        string categoryBody = (string)"RimWorldAccess.Inspection.Storage.CategoryWithSummary".Translate(
                            cleanLabel, categorySummary, categoryExpanded);
                        return $"{categoryBody}.{categoryDesc} {posStr}{suffix}";
                    }
                    string categoryLabelState = (string)"RimWorldAccess.Inspection.Storage.LabelWithExpandState".Translate(
                        cleanLabel, categoryExpanded);
                    return $"{categoryLabelState}.{categoryDesc} {posStr}{suffix}";

                case NodeType.ThingDef:
                    string thingState = (string)(data.IsChecked
                        ? "RimWorldAccess.Inspection.Storage.ItemAllowed"
                        : "RimWorldAccess.Inspection.Storage.ItemDisallowed").Translate(cleanLabel);
                    string thingDesc = string.IsNullOrEmpty(item.Description) ? "" : $" {item.Description}";
                    return $"{thingState}.{thingDesc} {posStr}{suffix}";

                default:
                    return $"{cleanLabel}. {posStr}{suffix}";
            }
        }

        /// <summary>
        /// Custom search announcement formatter. Reuses the regular announcement
        /// (full label, state, description, level, position) and appends the match
        /// position — so the user still hears item context while searching.
        /// </summary>
        private static string FormatSearchAnnouncement(InspectionTreeItem item, TypeaheadSearchHelper typeahead)
        {
            string baseAnnouncement = FormatItemAnnouncement(item);
            if (!typeahead.HasActiveSearch)
                return baseAnnouncement;
            return baseAnnouncement + typeahead.BuildSearchContextSuffix();
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
            if (!isActive || treeNav.Count == 0) return;
            treeNav.HandleTypeaheadCharacter(c);
        }

        /// <summary>
        /// Processes backspace for typeahead search.
        /// </summary>
        public static void ProcessBackspace()
        {
            if (!isActive || treeNav.Count == 0) return;
            treeNav.HandleTypeaheadBackspace();
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
                TolkHelper.SpeakData(FormatSearchAnnouncement(item, treeNav.Typeahead));
            }
            else
            {
                AnnounceCurrentNode();
            }
        }

        #endregion
    }
}
