using System;
using System.Linq;
using Verse;
using Verse.Sound;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages a windowless storage settings menu for stockpile zones / shelves.
    /// Provides keyboard navigation through priority, quick actions, sliders, and the
    /// thing filter category tree. Uses TreeNavigationHelper for all standard treeview
    /// keyboard handling (sounds, submenu mode, search auto-expansion, level suffix).
    /// </summary>
    public static class StorageSettingsMenuState
    {
        // Navigation node types stored in InspectionTreeItem.Data
        private enum NodeType
        {
            Priority,
            ClearAll,
            AllowAll,
            HitPointsRange,
            QualityRange,
            SpecialFilter,
            Category,
            ThingDef
        }

        private class StorageNodeData
        {
            public NodeType Type;
            public bool IsChecked;
            public object Reference;
        }

        private static bool isActive = false;
        private static StorageSettings currentSettings = null;
        private static ThingFilter parentFilter = null;
        private static TreeNode_ThingCategory rootNode = null;
        private static bool showPriority = true;
        private static TreeNavigationHelper treeNav = new TreeNavigationHelper("StorageSettings");

        public static bool IsActive => isActive;
        public static bool HasActiveSearch => treeNav.HasActiveSearch;
        public static bool HasNoMatches => treeNav.HasNoMatches;

        static StorageSettingsMenuState()
        {
            treeNav.FormatItemAnnouncement = FormatItemAnnouncement;
            treeNav.FormatSearchAnnouncement = FormatSearchAnnouncement;
            treeNav.OnActivate = HandleActivate;
            treeNav.AnnounceChildCounts = false;
            // Auto-expand category nodes during typeahead so matches inside
            // collapsed branches become reachable without manual expansion.
            treeNav.ShouldExpandForSearch = item =>
                (item.Data as StorageNodeData)?.Type == NodeType.Category;
        }

        public static void Open(StorageSettings settings, bool showPriority = true)
        {
            if (settings == null)
            {
                Log.Error("Cannot open storage settings menu: settings is null");
                return;
            }

            StorageSettingsMenuState.showPriority = showPriority;
            currentSettings = settings;
            parentFilter = settings.owner?.GetParentStoreSettings()?.filter;
            // Use the parent filter's stable display root when available so expansion
            // state isn't perturbed by toggling individual items.
            if (parentFilter != null)
            {
                rootNode = parentFilter.DisplayRootCategory;
            }
            else
            {
                rootNode = settings.filter.RootNode ?? ThingCategoryNodeDatabase.RootNode;
            }

            isActive = true;

            var treeRoot = BuildTree();
            treeNav.Initialize(treeRoot);
            treeNav.ReannounceCurrentItem();

            Log.Message($"Opened storage settings menu with {treeNav.Count} items");
        }

        public static void Close()
        {
            isActive = false;
            currentSettings = null;
            parentFilter = null;
            rootNode = null;
            showPriority = true;
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

            // Priority cycler at the top. Some storage parents hide the priority
            // setting in vanilla (e.g. the biosculpter pod's nutrition storage,
            // ITab_BiosculpterNutritionStorage.IsPrioritySettingVisible => false).
            if (showPriority)
            {
                root.Children.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "Priority".Translate(),
                    IndentLevel = 0,
                    IsExpandable = false,
                    Parent = root,
                    Data = new StorageNodeData { Type = NodeType.Priority }
                });
            }

            // Quick actions
            root.Children.Add(new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = "ClearAll".Translate(),
                IndentLevel = 0,
                IsExpandable = false,
                Parent = root,
                Data = new StorageNodeData { Type = NodeType.ClearAll }
            });
            root.Children.Add(new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = "AllowAll".Translate(),
                IndentLevel = 0,
                IsExpandable = false,
                Parent = root,
                Data = new StorageNodeData { Type = NodeType.AllowAll }
            });

            // Match vanilla ThingFilterUI: parent filter's flags govern visibility,
            // defaulting to true when no parent. Storage filters always show both
            // since storage isn't recipe-scoped. parentFilter.DisplayRootCategory was
            // accessed in Open() so RecalculateDisplayRootCategory() has already run.
            bool hpConfigurable = parentFilter?.allowedHitPointsConfigurable ?? true;
            bool qualityConfigurable = parentFilter?.allowedQualitiesConfigurable ?? true;

            if (hpConfigurable)
            {
                root.Children.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "HitPointsBasic".Translate().CapitalizeFirst(),
                    IndentLevel = 0,
                    IsExpandable = false,
                    Parent = root,
                    Data = new StorageNodeData { Type = NodeType.HitPointsRange }
                });
            }

            if (qualityConfigurable)
            {
                root.Children.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "Quality".Translate(),
                    IndentLevel = 0,
                    IsExpandable = false,
                    Parent = root,
                    Data = new StorageNodeData { Type = NodeType.QualityRange }
                });
            }

            // Filter tree
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
                        && ThingFilterHelper.IsVisibleSpecialFilter(specialFilter, node, currentSettings.filter, parentFilter))
                    {
                        parent.Children.Add(new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.Item,
                            Label = specialFilter.LabelCap,
                            Description = specialFilter.description,
                            IndentLevel = indentLevel,
                            IsExpandable = false,
                            Parent = parent,
                            Data = new StorageNodeData
                            {
                                Type = NodeType.SpecialFilter,
                                IsChecked = currentSettings.filter.Allows(specialFilter),
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
                    && ThingFilterHelper.IsVisibleSpecialFilter(specialFilter, node, currentSettings.filter, parentFilter))
                {
                    parent.Children.Add(new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = specialFilter.LabelCap,
                        Description = specialFilter.description,
                        IndentLevel = indentLevel,
                        IsExpandable = false,
                        Parent = parent,
                        Data = new StorageNodeData
                        {
                            Type = NodeType.SpecialFilter,
                            IsChecked = currentSettings.filter.Allows(specialFilter),
                            Reference = specialFilter
                        }
                    });
                }
            }

            // Child categories
            foreach (var childCategory in node.ChildCategoryNodes)
            {
                if (!ThingFilterHelper.IsVisibleCategory(childCategory, parentFilter))
                    continue;

                var allowanceState = ThingFilterHelper.GetAllowanceState(
                    childCategory.catDef, currentSettings.filter,
                    td => ThingFilterHelper.IsVisible(td, parentFilter));
                bool isChecked = allowanceState != ThingFilterHelper.CategoryAllowanceState.NoneAllowed;

                var catItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Category,
                    Label = childCategory.LabelCap,
                    Description = childCategory.catDef.description,
                    IndentLevel = indentLevel,
                    IsExpandable = true,
                    // Default collapsed (matches vanilla). The full subtree is built
                    // up front so TreeNavigationHelper can drive expand/collapse and
                    // submenu mode natively without a structural rebuild.
                    IsExpanded = false,
                    Parent = parent,
                    Data = new StorageNodeData
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
                        Data = new StorageNodeData
                        {
                            Type = NodeType.ThingDef,
                            IsChecked = currentSettings.filter.Allows(thingDef),
                            Reference = thingDef
                        }
                    });
                }
            }
        }

        /// <summary>
        /// Walks the existing tree and refreshes IsChecked on every leaf and
        /// category node. Used after any filter change (toggle, ClearAll,
        /// AllowAll, category tri-state toggle) so we don't lose cursor
        /// position the way a structural rebuild would.
        /// </summary>
        private static void RefreshAllowanceStates()
        {
            if (treeNav.RootItem == null) return;
            RefreshAllowanceStatesRecursive(treeNav.RootItem);
        }

        private static void RefreshAllowanceStatesRecursive(InspectionTreeItem node)
        {
            var data = node.Data as StorageNodeData;
            if (data != null)
            {
                switch (data.Type)
                {
                    case NodeType.SpecialFilter:
                        if (data.Reference is SpecialThingFilterDef sf)
                            data.IsChecked = currentSettings.filter.Allows(sf);
                        break;
                    case NodeType.ThingDef:
                        if (data.Reference is ThingDef td)
                            data.IsChecked = currentSettings.filter.Allows(td);
                        break;
                    case NodeType.Category:
                        if (data.Reference is TreeNode_ThingCategory cat)
                        {
                            var state = ThingFilterHelper.GetAllowanceState(
                                cat.catDef, currentSettings.filter,
                                t => ThingFilterHelper.IsVisible(t, parentFilter));
                            data.IsChecked = state != ThingFilterHelper.CategoryAllowanceState.NoneAllowed;
                        }
                        break;
                }
            }

            foreach (var child in node.Children)
                RefreshAllowanceStatesRecursive(child);
        }

        // ===== Navigation API (used by StorageSettingsMenuPatch) =====

        public static void SelectNext() => treeNav.SelectNext();
        public static void SelectPrevious() => treeNav.SelectPrevious();
        public static void JumpToFirst(bool ctrlPressed = false) => treeNav.JumpToFirst(ctrlPressed);
        public static void JumpToLast(bool ctrlPressed = false) => treeNav.JumpToLast(ctrlPressed);

        public static void ExpandCurrent()
        {
            if (treeNav.SelectedItem == null) return;

            var data = treeNav.SelectedItem.Data as StorageNodeData;
            if (data == null) return;

            // Right arrow on the priority shim cycles forward — it acts as the
            // "next priority" affordance for sighted users used to clicking +/−.
            if (data.Type == NodeType.Priority)
            {
                CyclePriority(forward: true);
                return;
            }

            // For everything else delegate to TreeNavigationHelper. It plays the
            // standard FloatMenu_Open / Tick_Tiny sounds, handles drill-down for
            // already-expanded items, and lands on the first remembered/child
            // when submenu mode hides the parent we just expanded.
            treeNav.ExpandOrDrillDown();
        }

        public static void CollapseCurrent()
        {
            if (treeNav.SelectedItem == null) return;

            var data = treeNav.SelectedItem.Data as StorageNodeData;
            if (data == null) return;

            if (data.Type == NodeType.Priority)
            {
                CyclePriority(forward: false);
                return;
            }

            treeNav.CollapseOrDrillUp();
        }

        public static void ExpandAllSiblings()
        {
            treeNav.ExpandAllSiblings();
        }

        public static void ToggleCurrent()
        {
            if (treeNav.SelectedItem == null) return;

            var item = treeNav.SelectedItem;
            var data = item.Data as StorageNodeData;
            if (data == null) return;

            // Commit on activation: clear any active typeahead and restore the
            // pre-search expansion. Matches expand/collapse behavior so a
            // subsequent Down arrow walks the full list rather than only matches.
            treeNav.CommitAndClearSearch();

            switch (data.Type)
            {
                case NodeType.Priority:
                    CyclePriority(forward: true);
                    break;

                case NodeType.ClearAll:
                    currentSettings.filter.SetDisallowAll();
                    RefreshAllowanceStates();
                    TolkHelper.Speak("ClearAll".Loc());
                    break;

                case NodeType.AllowAll:
                    currentSettings.filter.SetAllowAll(parentFilter);
                    RefreshAllowanceStates();
                    TolkHelper.Speak("AllowAll".Loc());
                    break;

                case NodeType.HitPointsRange:
                    RangeEditMenuState.OpenHitPointsRange(currentSettings.filter.AllowedHitPointsPercents);
                    break;

                case NodeType.QualityRange:
                    RangeEditMenuState.OpenQualityRange(currentSettings.filter.AllowedQualityLevels);
                    break;

                case NodeType.SpecialFilter:
                    if (data.Reference is SpecialThingFilterDef specialFilter)
                    {
                        bool desired = !currentSettings.filter.Allows(specialFilter);
                        currentSettings.filter.SetAllow(specialFilter, desired);
                        data.IsChecked = currentSettings.filter.Allows(specialFilter);
                        TolkHelper.SpeakData((string)(data.IsChecked
                            ? "RimWorldAccess.Inspection.Storage.ToggleResultAllowed"
                            : "RimWorldAccess.Inspection.Storage.ToggleResultDisallowed").Translate(item.Label));
                    }
                    break;

                case NodeType.Category:
                    if (data.Reference is TreeNode_ThingCategory catNode)
                    {
                        var state = ThingFilterHelper.GetAllowanceState(
                            catNode.catDef, currentSettings.filter,
                            td => ThingFilterHelper.IsVisible(td, parentFilter));
                        bool desiredCat = state != ThingFilterHelper.CategoryAllowanceState.AllAllowed;
                        currentSettings.filter.SetAllow(catNode.catDef, desiredCat);
                        RefreshAllowanceStates();
                        var actualState = ThingFilterHelper.GetAllowanceState(
                            catNode.catDef, currentSettings.filter,
                            td => ThingFilterHelper.IsVisible(td, parentFilter));
                        bool catAllowed = actualState != ThingFilterHelper.CategoryAllowanceState.NoneAllowed;
                        TolkHelper.SpeakData((string)(catAllowed
                            ? "RimWorldAccess.Inspection.Storage.ToggleResultAllowed"
                            : "RimWorldAccess.Inspection.Storage.ToggleResultDisallowed").Translate(item.Label));
                    }
                    break;

                case NodeType.ThingDef:
                    if (data.Reference is ThingDef thingDef)
                    {
                        bool desiredThing = !currentSettings.filter.Allows(thingDef);
                        currentSettings.filter.SetAllow(thingDef, desiredThing);
                        data.IsChecked = currentSettings.filter.Allows(thingDef);
                        TolkHelper.SpeakData((string)(data.IsChecked
                            ? "RimWorldAccess.Inspection.Storage.ToggleResultAllowed"
                            : "RimWorldAccess.Inspection.Storage.ToggleResultDisallowed").Translate(item.Label));
                    }
                    break;
            }
        }

        public static void OpenInfoCard()
        {
            var item = treeNav.SelectedItem;
            var data = item?.Data as StorageNodeData;
            if (data != null && data.Type == NodeType.ThingDef && data.Reference is ThingDef thingDef)
            {
                InfoCardState.OpenInfoCardForDef(thingDef);
                return;
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Inspection.Storage.NoInfoCard".Loc());
        }

        /// <summary>
        /// Applies the slider edits made in RangeEditMenuState back to the underlying filter
        /// and re-announces the current node so the new value is heard.
        /// </summary>
        public static void ApplyRangeChanges(FloatRange hitPoints, QualityRange quality)
        {
            if (treeNav.SelectedItem == null) return;
            var data = treeNav.SelectedItem.Data as StorageNodeData;
            if (data == null) return;

            if (data.Type == NodeType.HitPointsRange)
            {
                currentSettings.filter.AllowedHitPointsPercents = hitPoints;
                treeNav.ReannounceCurrentItem();
            }
            else if (data.Type == NodeType.QualityRange)
            {
                currentSettings.filter.AllowedQualityLevels = quality;
                treeNav.ReannounceCurrentItem();
            }
        }

        // ===== Priority cycling =====

        private static readonly StoragePriority[] PrioritiesAsc = new[]
        {
            StoragePriority.Low,
            StoragePriority.Normal,
            StoragePriority.Preferred,
            StoragePriority.Important,
            StoragePriority.Critical
        };

        private static void CyclePriority(bool forward)
        {
            int currentIndex = Array.IndexOf(PrioritiesAsc, currentSettings.Priority);
            if (currentIndex < 0) currentIndex = 1;

            int len = PrioritiesAsc.Length;
            int delta = forward ? 1 : -1;
            currentIndex = ((currentIndex + delta) % len + len) % len;

            currentSettings.Priority = PrioritiesAsc[currentIndex];
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            treeNav.ReannounceCurrentItem();
        }

        // ===== Typeahead routing =====

        public static void ProcessTypeaheadCharacter(char c)
        {
            if (!isActive || treeNav.Count == 0) return;
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
                TolkHelper.SpeakData(FormatSearchAnnouncement(treeNav.SelectedItem, treeNav.Typeahead));
            else
                treeNav.ReannounceCurrentItem();
        }

        // ===== Announcement formatting =====

        private static string FormatItemAnnouncement(InspectionTreeItem item)
        {
            var data = item.Data as StorageNodeData;
            if (data == null) return item.Label;

            var (position, total) = treeNav.GetSiblingPosition(item);
            string suffix = MenuHelper.GetLevelSuffix("StorageSettings", item.IndentLevel);
            string posStr = MenuHelper.FormatPosition(position - 1, total);
            string positionTail = string.IsNullOrEmpty(posStr) ? "" : $" {posStr}";

            switch (data.Type)
            {
                case NodeType.Priority:
                    string priorityLabel = currentSettings.Priority.Label().CapitalizeFirst();
                    return $"{item.Label}: {priorityLabel}.{positionTail}{suffix}";

                case NodeType.ClearAll:
                case NodeType.AllowAll:
                    return $"{item.Label}.{positionTail}{suffix}";

                case NodeType.HitPointsRange:
                {
                    var range = currentSettings.filter.AllowedHitPointsPercents;
                    return $"{item.Label}: {range.min:P0} - {range.max:P0}.{positionTail}{suffix}";
                }

                case NodeType.QualityRange:
                {
                    var range = currentSettings.filter.AllowedQualityLevels;
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
                            catNode.catDef, currentSettings.filter,
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

        // Custom activate routes through the same logic as Space/Enter so the standard
        // tree handler hands work off rather than toggling expand on category nodes.
        private static bool HandleActivate(InspectionTreeItem item)
        {
            ToggleCurrent();
            return true;
        }
    }
}
