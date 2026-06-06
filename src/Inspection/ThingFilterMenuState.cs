using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Reusable windowless menu for thing filtering. Used by BillConfigState
    /// (ingredient filters), pen markers (animal / auto-cut filters), and the
    /// shells tab. Built on TreeNavigationHelper so it shares sounds, search
    /// auto-expansion, submenu mode, and level announcements with every other
    /// modern treeview in the mod.
    /// </summary>
    public static class ThingFilterMenuState
    {
        private enum NodeType
        {
            ClearAll,
            AllowAll,
            HitPointsRange,
            QualityRange,
            SpecialFilter,
            Category,
            ThingDef
        }

        private class FilterNodeData
        {
            public NodeType Type;
            public bool IsChecked;
            public object Reference;
        }

        private static bool isActive = false;
        private static ThingFilter currentFilter = null;
        private static ThingFilter parentFilter = null;
        private static string menuTitle = "";
        private static bool forceHideHitPoints = false;
        private static bool forceHideQuality = false;

        private static TreeNavigationHelper treeNav = new TreeNavigationHelper("ThingFilterMenu");

        public static bool IsActive => isActive;
        public static bool HasActiveSearch => treeNav.HasActiveSearch;
        public static bool HasNoMatches => treeNav.HasNoMatches;

        static ThingFilterMenuState()
        {
            treeNav.FormatItemAnnouncement = FormatItemAnnouncement;
            treeNav.FormatSearchAnnouncement = FormatSearchAnnouncement;
            treeNav.OnActivate = HandleActivate;
            treeNav.AnnounceChildCounts = false;
            treeNav.ShouldExpandForSearch = item =>
                (item.Data as FilterNodeData)?.Type == NodeType.Category;
        }

        /// <summary>
        /// Opens the thing filter menu.
        /// </summary>
        /// <param name="filter">The filter being edited (what's currently selected)</param>
        /// <param name="fixedFilter">Optional parent filter that defines what's possible (tree structure)</param>
        /// <param name="title">Menu title for announcements</param>
        public static void Open(ThingFilter filter, ThingFilter fixedFilter = null, string title = "Thing Filter",
            bool forceHideHitPointsConfig = false, bool forceHideQualityConfig = false)
        {
            if (filter == null)
            {
                Log.Error("Cannot open thing filter menu: filter is null");
                return;
            }

            currentFilter = filter;
            parentFilter = fixedFilter;
            menuTitle = title;
            forceHideHitPoints = forceHideHitPointsConfig;
            forceHideQuality = forceHideQualityConfig;
            isActive = true;

            var treeRoot = BuildTree();
            treeNav.Initialize(treeRoot);
            treeNav.ReannounceCurrentItem();

            Log.Message($"Opened thing filter menu: {title}");
        }

        public static void Close()
        {
            isActive = false;
            currentFilter = null;
            parentFilter = null;
            treeNav.Reset();
        }

        // ===== Tree construction =====

        private static InspectionTreeItem BuildTree()
        {
            var root = new InspectionTreeItem
            {
                Label = "Root",
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false
            };

            // Quick actions
            root.Children.Add(new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = "ClearAll".Translate(),
                IndentLevel = 0,
                IsExpandable = false,
                Parent = root,
                Data = new FilterNodeData { Type = NodeType.ClearAll }
            });
            root.Children.Add(new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = "AllowAll".Translate(),
                IndentLevel = 0,
                IsExpandable = false,
                Parent = root,
                Data = new FilterNodeData { Type = NodeType.AllowAll }
            });

            // Match vanilla ThingFilterUI: touching DisplayRootCategory triggers
            // RecalculateDisplayRootCategory(), which is what populates
            // allowedHitPointsConfigurable / allowedQualitiesConfigurable on the
            // parent filter. Read the flags only after that has run.
            TreeNode_ThingCategory rootNode = parentFilter?.DisplayRootCategory
                ?? currentFilter.DisplayRootCategory;
            bool hpConfigurable = parentFilter?.allowedHitPointsConfigurable ?? true;
            bool qualityConfigurable = parentFilter?.allowedQualitiesConfigurable ?? true;

            if (hpConfigurable && !forceHideHitPoints)
            {
                root.Children.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "HitPointsBasic".Translate().CapitalizeFirst(),
                    IndentLevel = 0,
                    IsExpandable = false,
                    Parent = root,
                    Data = new FilterNodeData { Type = NodeType.HitPointsRange }
                });
            }

            if (qualityConfigurable && !forceHideQuality)
            {
                root.Children.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "Quality".Translate(),
                    IndentLevel = 0,
                    IsExpandable = false,
                    Parent = root,
                    Data = new FilterNodeData { Type = NodeType.QualityRange }
                });
            }
            if (rootNode != null)
            {
                AddCategoryChildren(rootNode, root, 0, isRoot: true);
            }

            return root;
        }

        private static void AddCategoryChildren(TreeNode_ThingCategory node, InspectionTreeItem parent,
            int indentLevel, bool isRoot = false)
        {
            // Parent special filters surface only at the root (e.g. AllowRotten / AllowFresh).
            if (isRoot)
            {
                foreach (var specialFilter in node.catDef.ParentsSpecialThingFilterDefs)
                {
                    if (specialFilter.configurable
                        && ThingFilterHelper.IsVisibleSpecialFilter(specialFilter, node, currentFilter, parentFilter))
                    {
                        parent.Children.Add(new InspectionTreeItem
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
                        });
                    }
                }
            }

            // Special filters scoped to this category
            foreach (var specialFilter in node.catDef.childSpecialFilters)
            {
                if (specialFilter.configurable
                    && ThingFilterHelper.IsVisibleSpecialFilter(specialFilter, node, currentFilter, parentFilter))
                {
                    parent.Children.Add(new InspectionTreeItem
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
                    });
                }
            }

            // Child categories — recurse fully so TreeNavigationHelper drives
            // expand/collapse natively. Default IsExpanded = false matches vanilla.
            foreach (var childCategory in node.ChildCategoryNodes)
            {
                if (!ThingFilterHelper.IsVisibleCategory(childCategory, parentFilter))
                    continue;

                var allowanceState = ThingFilterHelper.GetAllowanceState(
                    childCategory.catDef, currentFilter,
                    td => ThingFilterHelper.IsVisible(td, parentFilter));
                bool isChecked = allowanceState != ThingFilterHelper.CategoryAllowanceState.NoneAllowed;

                var catItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Category,
                    Label = childCategory.LabelCap,
                    Description = childCategory.catDef.description,
                    IndentLevel = indentLevel,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = parent,
                    Data = new FilterNodeData
                    {
                        Type = NodeType.Category,
                        IsChecked = isChecked,
                        Reference = childCategory
                    }
                };
                parent.Children.Add(catItem);

                AddCategoryChildren(childCategory, catItem, indentLevel + 1);
            }

            // Thing defs in this category
            foreach (var thingDef in node.catDef.childThingDefs.OrderBy(t => t.label))
            {
                if (ThingFilterHelper.IsVisible(thingDef, parentFilter))
                {
                    parent.Children.Add(new InspectionTreeItem
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
                    });
                }
            }
        }

        /// <summary>
        /// Walks the existing tree and refreshes IsChecked on every leaf and
        /// category node. Used after any filter change so the cursor stays put —
        /// no structural rebuild.
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

        // ===== Public navigation API =====

        public static void SelectNext() => treeNav.SelectNext();
        public static void SelectPrevious() => treeNav.SelectPrevious();
        public static void JumpToFirst(bool ctrlPressed = false) => treeNav.JumpToFirst(ctrlPressed);
        public static void JumpToLast(bool ctrlPressed = false) => treeNav.JumpToLast(ctrlPressed);
        public static void ExpandAllSiblings() => treeNav.ExpandAllSiblings();

        /// <summary>
        /// Right arrow handler. Expand category / drill into expanded category
        /// via TreeNavigationHelper — leaves are rejected with the standard sound.
        /// </summary>
        public static void ExpandOrToggleOn()
        {
            if (treeNav.SelectedItem == null) return;
            treeNav.ExpandOrDrillDown();
        }

        /// <summary>
        /// Left arrow handler. Collapse category if expanded; drill up to parent
        /// otherwise. TreeNavigationHelper handles the standard sound and submenu
        /// mode behavior.
        /// </summary>
        public static void CollapseOrToggleOff()
        {
            if (treeNav.SelectedItem == null) return;
            treeNav.CollapseOrDrillUp();
        }

        public static void ToggleCurrent()
        {
            if (treeNav.SelectedItem == null) return;
            var item = treeNav.SelectedItem;
            var data = item.Data as FilterNodeData;
            if (data == null) return;

            // Commit on activation: clear any active typeahead and restore the
            // pre-search expansion snapshot. Matches the expand/collapse behavior,
            // so a subsequent Down arrow walks the full list rather than only matches.
            treeNav.CommitAndClearSearch();

            switch (data.Type)
            {
                case NodeType.ClearAll:
                    currentFilter.SetDisallowAll();
                    RefreshAllowanceStates();
                    TolkHelper.Speak("ClearAll".Loc());
                    break;

                case NodeType.AllowAll:
                    currentFilter.SetAllowAll(parentFilter);
                    RefreshAllowanceStates();
                    TolkHelper.Speak("AllowAll".Loc());
                    break;

                case NodeType.HitPointsRange:
                    RangeEditMenuState.OpenHitPointsRange(currentFilter.AllowedHitPointsPercents);
                    break;

                case NodeType.QualityRange:
                    RangeEditMenuState.OpenQualityRange(currentFilter.AllowedQualityLevels);
                    break;

                case NodeType.SpecialFilter:
                    if (data.Reference is SpecialThingFilterDef sf)
                    {
                        bool desired = !currentFilter.Allows(sf);
                        currentFilter.SetAllow(sf, desired);
                        data.IsChecked = currentFilter.Allows(sf);
                        TolkHelper.Speak($"{item.Label}: {(data.IsChecked ? "Allowed" : "Disallowed")}");
                    }
                    break;

                case NodeType.Category:
                    if (data.Reference is TreeNode_ThingCategory catNode)
                    {
                        var state = ThingFilterHelper.GetAllowanceState(
                            catNode.catDef, currentFilter,
                            x => ThingFilterHelper.IsVisible(x, parentFilter));
                        bool desiredCat = state != ThingFilterHelper.CategoryAllowanceState.AllAllowed;
                        currentFilter.SetAllow(catNode.catDef, desiredCat);
                        RefreshAllowanceStates();
                        var actualState = ThingFilterHelper.GetAllowanceState(
                            catNode.catDef, currentFilter,
                            x => ThingFilterHelper.IsVisible(x, parentFilter));
                        string result = actualState == ThingFilterHelper.CategoryAllowanceState.NoneAllowed
                            ? "Disallowed"
                            : "Allowed";
                        TolkHelper.Speak($"{item.Label}: {result}");
                    }
                    break;

                case NodeType.ThingDef:
                    if (data.Reference is ThingDef td)
                    {
                        bool desiredThing = !currentFilter.Allows(td);
                        currentFilter.SetAllow(td, desiredThing);
                        data.IsChecked = currentFilter.Allows(td);
                        TolkHelper.Speak($"{item.Label}: {(data.IsChecked ? "Allowed" : "Disallowed")}");
                    }
                    break;
            }
        }

        public static void ApplyRangeChanges(FloatRange hitPoints, QualityRange quality)
        {
            if (treeNav.SelectedItem == null) return;
            var data = treeNav.SelectedItem.Data as FilterNodeData;
            if (data == null) return;

            if (data.Type == NodeType.HitPointsRange)
            {
                currentFilter.AllowedHitPointsPercents = hitPoints;
                treeNav.ReannounceCurrentItem();
            }
            else if (data.Type == NodeType.QualityRange)
            {
                currentFilter.AllowedQualityLevels = quality;
                treeNav.ReannounceCurrentItem();
            }
        }

        // ===== Typeahead routing =====

        public static void ProcessTypeaheadCharacter(char c)
        {
            if (!isActive || treeNav.Count == 0) return;
            // Delegate to the helper so search-time auto-expansion (ShouldExpandForSearch)
            // exposes matches inside collapsed categories.
            treeNav.HandleTypeaheadCharacter(c);
        }

        public static void ProcessBackspace()
        {
            if (!isActive || treeNav.Count == 0) return;
            treeNav.HandleTypeaheadBackspace();
        }

        public static void ClearTypeaheadSearch()
        {
            treeNav.Typeahead.ClearSearchAndAnnounce();
            treeNav.ReannounceCurrentItem();
        }

        public static void SelectNextMatch()
        {
            if (treeNav.Count == 0) return;
            int next = treeNav.Typeahead.GetNextMatch(treeNav.SelectedIndex);
            if (next >= 0)
            {
                treeNav.SetSelectedIndex(next);
                AnnounceWithSearch();
            }
        }

        public static void SelectPreviousMatch()
        {
            if (treeNav.Count == 0) return;
            int prev = treeNav.Typeahead.GetPreviousMatch(treeNav.SelectedIndex);
            if (prev >= 0)
            {
                treeNav.SetSelectedIndex(prev);
                AnnounceWithSearch();
            }
        }

        private static void AnnounceWithSearch()
        {
            if (treeNav.SelectedItem == null) return;
            if (treeNav.HasActiveSearch)
                TolkHelper.Speak(FormatSearchAnnouncement(treeNav.SelectedItem, treeNav.Typeahead));
            else
                treeNav.ReannounceCurrentItem();
        }

        // ===== Announcement formatting =====

        private static string FormatItemAnnouncement(InspectionTreeItem item)
        {
            var data = item.Data as FilterNodeData;
            if (data == null) return item.Label;

            var (position, total) = treeNav.GetSiblingPosition(item);
            string suffix = MenuHelper.GetLevelSuffix("ThingFilterMenu", item.IndentLevel);
            string posStr = MenuHelper.FormatPosition(position - 1, total);
            string positionTail = string.IsNullOrEmpty(posStr) ? "" : $" {posStr}";

            switch (data.Type)
            {
                case NodeType.ClearAll:
                case NodeType.AllowAll:
                    return $"{item.Label}.{positionTail}{suffix}";

                case NodeType.HitPointsRange:
                {
                    var range = currentFilter.AllowedHitPointsPercents;
                    return $"{item.Label}: {range.min:P0} - {range.max:P0}.{positionTail}{suffix}";
                }

                case NodeType.QualityRange:
                {
                    var range = currentFilter.AllowedQualityLevels;
                    return $"{item.Label}: {range.min.GetLabel()} - {range.max.GetLabel()}.{positionTail}{suffix}";
                }

                case NodeType.SpecialFilter:
                {
                    string state = data.IsChecked ? "allowed" : "disallowed";
                    string desc = string.IsNullOrEmpty(item.Description) ? "" : $" {item.Description}";
                    return $"{item.Label}. {state}.{desc}{positionTail}{suffix}";
                }

                case NodeType.Category:
                {
                    string summary = "disallowed";
                    if (data.Reference is TreeNode_ThingCategory catNode)
                    {
                        var s = ThingFilterHelper.GetCategorySummary(
                            catNode.catDef, currentFilter,
                            td => ThingFilterHelper.IsVisible(td, parentFilter));
                        summary = ThingFilterHelper.FormatCategorySummary(s);
                    }
                    string expanded = item.IsExpanded ? "expanded" : "collapsed";
                    string desc = string.IsNullOrEmpty(item.Description) ? "" : $" {item.Description}";
                    return $"{item.Label}. {summary}, {expanded}.{desc}{positionTail}{suffix}";
                }

                case NodeType.ThingDef:
                {
                    string state = data.IsChecked ? "allowed" : "disallowed";
                    string desc = string.IsNullOrEmpty(item.Description) ? "" : $" {item.Description}";
                    return $"{item.Label}. {state}.{desc}{positionTail}{suffix}";
                }

                default:
                    return $"{item.Label}.{positionTail}{suffix}";
            }
        }

        private static string FormatSearchAnnouncement(InspectionTreeItem item, TypeaheadSearchHelper typeahead)
        {
            // Reuse the regular announcement so the user still hears the full label,
            // state, and description while searching — then append match position.
            string baseAnnouncement = FormatItemAnnouncement(item);
            if (!typeahead.HasActiveSearch)
                return baseAnnouncement;
            return $"{baseAnnouncement} {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'";
        }

        private static bool HandleActivate(InspectionTreeItem item)
        {
            ToggleCurrent();
            return true;
        }
    }
}
