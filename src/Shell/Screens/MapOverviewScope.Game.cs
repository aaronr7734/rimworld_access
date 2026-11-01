using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The shared window behind the map overviews (<see cref="GlobalHealthWindow"/>,
    /// <see cref="GlobalGearWindow"/>): the group filters beside the scope's own tree rows and its
    /// buttons, so the visible cursor and the spoken one cannot drift.
    /// </summary>
    public abstract class MapOverviewWindow : Window
    {
        private const float FilterColumnWidth = 220f;
        private const float FilterRowHeight = 28f;
        private const float RowHeight = TreeRowPainter.RowHeight;

        private Vector2 scrollPosition;
        private int scrolledTo = -1;

        protected MapOverviewWindow()
        {
            doCloseX = true;
            absorbInputAroundWindow = true;
            draggable = true;
            resizeable = true;
        }

        protected abstract MapOverviewScope Scope { get; }

        protected abstract string Title { get; }

        /// <summary>The thing whose icon leads a row, or null for none.</summary>
        protected virtual Thing RowIcon(InspectionTreeItem item)
        {
            return null;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(920f, 640f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            MapOverviewScope scope = Scope;
            scope?.SyncRows();

            Text.Font = GameFont.Medium;
            float titleHeight = Text.LineHeight;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, titleHeight), Title);
            Text.Font = GameFont.Small;

            const float buttonHeight = 35f;
            float y = inRect.y + titleHeight + 8f;
            float buttonRowY = inRect.yMax - buttonHeight;
            float bodyHeight = buttonRowY - 10f - y;
            if (scope != null)
            {
                DrawFilters(new Rect(inRect.x, y, FilterColumnWidth, bodyHeight), scope);
                DrawList(new Rect(inRect.x + FilterColumnWidth + 10f, y, inRect.width - FilterColumnWidth - 10f, bodyHeight), scope);
                DrawButtons(new Rect(inRect.x, buttonRowY, inRect.width, buttonHeight), scope);
            }
        }

        private static void DrawFilters(Rect rect, MapOverviewScope scope)
        {
            int focused = scope.FocusedFilterIndex;
            float y = rect.y;
            for (int i = 0; i < MapOverviewScope.FilterCount; i++)
            {
                Rect row = new Rect(rect.x, y, rect.width, FilterRowHeight);
                scope.FilterRects[i] = GuiSpace.VisibleScreenRect(row);
                bool value = scope.FilterValue(i);
                bool before = value;
                Widgets.CheckboxLabeled(row, MapOverviewScope.FilterLabel(i), ref value);
                if (value != before)
                {
                    scope.ClickFilter(i);
                }
                if (i == focused)
                {
                    FocusRing.Draw(row);
                }
                y += FilterRowHeight + 2f;
            }
        }

        private void DrawList(Rect outer, MapOverviewScope scope)
        {
            scope.FocusedRowRect = default(Rect);
            IReadOnlyList<InspectionTreeItem> rows = scope.VisibleRows;
            Rect viewRect = new Rect(0f, 0f, outer.width - 16f, rows.Count * RowHeight);

            int focused = scope.CurrentRowIndex;
            if (focused >= 0 && focused != scrolledTo)
            {
                TreeRowPainter.ClampScrollTo(ref scrollPosition, focused, outer.height, viewRect.height);
                scrolledTo = focused;
            }

            bool ringed = scope.TreeHasCursor;
            Widgets.BeginScrollView(outer, ref scrollPosition, viewRect);
            Text.Anchor = TextAnchor.MiddleLeft;
            int clickedIndex = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                Rect rowRect = new Rect(0f, i * RowHeight, viewRect.width, RowHeight);
                if (rowRect.yMax < scrollPosition.y || rowRect.y > scrollPosition.y + outer.height)
                {
                    continue;
                }
                InspectionTreeItem item = rows[i];
                var row = new TreeTwinRow
                {
                    Label = item.SearchText,
                    Level = Mathf.Max(0, item.IndentLevel),
                    IsExpandable = item.IsExpandable,
                    IsExpanded = item.IsExpanded,
                    IconThing = RowIcon(item),
                };
                TreeRowPainter.DrawRow(row, rowRect, i, i == focused, ringed && i == focused, mouseover: true);
                if (i == focused)
                {
                    scope.FocusedRowRect = GuiSpace.VisibleScreenRect(rowRect);
                }
                if (Widgets.ButtonInvisible(rowRect))
                {
                    clickedIndex = i;
                }
            }
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.EndScrollView();

            // After the scroll view: activating a row can close this window.
            if (clickedIndex >= 0)
            {
                scope.ClickRow(clickedIndex);
            }
        }

        private static void DrawButtons(Rect rect, MapOverviewScope scope)
        {
            IReadOnlyList<ScreenAction> actions = scope.CurrentActions();
            const float spacing = 10f;
            float width = Mathf.Min(200f, (rect.width - spacing * (actions.Count - 1)) / actions.Count);
            float x = rect.x;
            int focusedIndex = scope.CurrentActionIndex;
            for (int i = 0; i < actions.Count; i++)
            {
                ScreenAction action = actions[i];
                Rect r = new Rect(x, rect.y, width, rect.height);
                if (Widgets.ButtonText(r, action.Label))
                {
                    action.Activate();
                }
                if (i == focusedIndex)
                {
                    FocusRing.Draw(r);
                }
                x += width + spacing;
            }
        }
    }

    /// <summary>
    /// Keyboard scope shared by the map overviews. Regions: the tree, the group filters (one
    /// checkbox per <see cref="MapPawnGroupFilter"/> flag, persisted per screen), and Buttons.
    ///
    /// The tree rebuilds silently at most every <see cref="LiveRebuildSeconds"/>, and only when
    /// <see cref="ContentSignature"/> moved, never under a search or before its auto-expansion is
    /// restored. Rebuilds ride <see cref="TreeStatePreserve.CursorRule.FollowData"/>, so every
    /// node's Data must be unique among its siblings.
    /// </summary>
    public abstract class MapOverviewScope : TreeRegionScope
    {
        private const int FiltersRegion = 1;
        private const float LiveRebuildSeconds = 0.25f;

        private static readonly MapPawnGroupFilter[] Filters =
        {
            MapPawnGroupFilter.Colonists,
            MapPawnGroupFilter.ColonyAnimals,
            MapPawnGroupFilter.ColonyMechs,
            MapPawnGroupFilter.PrisonersAndSlaves,
            MapPawnGroupFilter.Visitors,
            MapPawnGroupFilter.Hostiles,
        };

        private static readonly string[] FilterLabelKeys =
        {
            "RimWorldAccess.PawnGroup.Filter.Colonists",
            "RimWorldAccess.PawnGroup.Filter.ColonyAnimals",
            "RimWorldAccess.PawnGroup.Filter.ColonyMechs",
            "RimWorldAccess.PawnGroup.Filter.PrisonersAndSlaves",
            "RimWorldAccess.PawnGroup.Filter.Visitors",
            "RimWorldAccess.PawnGroup.Filter.Hostiles",
        };

        internal static int FilterCount => Filters.Length;

        private readonly Window window;
        private readonly List<ScreenAction> actionsBuffer = new List<ScreenAction>();
        private int builtSignature;
        private float lastRebuildTime = -1f;

        internal readonly Rect[] FilterRects = new Rect[Filters.Length];
        internal Rect FocusedRowRect;

        protected MapOverviewScope(Window window)
        {
            this.window = window;
        }

        protected internal override Window OwnedWindow
        {
            get { return window; }
        }

        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        /// <summary>This screen's persisted filter flags.</summary>
        protected abstract MapPawnGroupFilter ShownGroups { get; set; }

        /// <summary>A cheap hash of everything the tree would speak for <paramref name="shown"/>; equal means no rebuild.</summary>
        protected abstract int ContentSignature(MapPawnGroupFilter shown);

        /// <summary>A fresh tree; every node's Data is unique among its siblings and stable across rebuilds.</summary>
        protected abstract InspectionTreeItem BuildRoot(MapPawnGroupFilter shown);

        /// <summary>The confirmation after a filter toggle; <paramref name="state"/> is the spoken checkbox state.</summary>
        protected abstract string FilterChangedAnnouncement(string state);

        /// <summary>Whether a leaf row does something on Enter; the rest read as read-only.</summary>
        protected abstract bool IsActionRow(InspectionTreeItem item);

        /// <summary>The role an action row speaks.</summary>
        protected virtual ElementRole ActionRowRole
        {
            get { return ElementRole.None; }
        }

        public override void OnPush()
        {
            // A re-push keeps the live tree, and with it the cursor and expansion.
            if (Tree.Root == null)
            {
                MapPawnGroupFilter shown = ShownGroups;
                builtSignature = ContentSignature(shown);
                lastRebuildTime = Time.realtimeSinceStartup;
                SetTreeRoot(BuildRoot(shown));
            }
            base.OnPush();
        }

        protected override bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            return item.IsExpandable;
        }

        /// <summary>The filters need no auto-expansion, and a search there must not expand the tree.</summary>
        protected override void OnTypeaheadWillSearch()
        {
            if (CursorInTreeRegion)
            {
                base.OnTypeaheadWillSearch();
            }
        }

        // Rows.

        internal IReadOnlyList<InspectionTreeItem> VisibleRows
        {
            get { return Tree.Visible; }
        }

        internal void SyncRows()
        {
            if (TypeaheadHasActiveSearch || CurrentPanel.SearchExpansionSnapshot != null
                || Time.realtimeSinceStartup - lastRebuildTime < LiveRebuildSeconds)
            {
                return;
            }
            lastRebuildTime = Time.realtimeSinceStartup;
            if (ContentSignature(ShownGroups) != builtSignature)
            {
                Rebuild();
            }
        }

        private void Rebuild()
        {
            MapPawnGroupFilter shown = ShownGroups;
            builtSignature = ContentSignature(shown);
            lastRebuildTime = Time.realtimeSinceStartup;

            int oldIndex = CurrentRowIndex;
            int index = SetTreeRootPreservingState(BuildRoot(shown), oldIndex, TreeStatePreserve.CursorRule.FollowData);
            if (index < 0)
            {
                index = Math.Min(Math.Max(oldIndex, 0), Tree.Count - 1);
            }
            Tree.SetSelectedIndex(index);
            RefreshModel();
            Model.Region(TreeRegionIndex)?.MoveTo(index);
            if (CurrentPanel.LastAnnouncedParent == null && index >= 0 && index < Tree.Count)
            {
                CurrentPanel.LastAnnouncedParent = Tree.Visible[index].Parent;
            }
        }

        /// <summary>The tree region's retained cursor, so its row stays highlighted while another region has focus.</summary>
        internal int CurrentRowIndex
        {
            get
            {
                if (Model.RegionCount <= TreeRegionIndex)
                {
                    return -1;
                }
                ListModel region = Model.Region(TreeRegionIndex);
                return region != null && !region.IsEmpty && region.Index < Tree.Count ? region.Index : -1;
            }
        }

        internal bool TreeHasCursor
        {
            get { return Model.RegionIndex == TreeRegionIndex; }
        }

        /// <summary>A mouse click on a row: moves the keyboard cursor there and activates it, exactly as Enter would.</summary>
        internal void ClickRow(int index)
        {
            if (index >= 0 && index < Tree.Count)
            {
                Tree.SetSelectedIndex(index);
                TwinActivateRow(index);
            }
        }

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            // Top-level rows keep their live summary even while open.
            string label = item.IndentLevel > 0 && item.IsExpanded && item.ExpandedLabel != null ? item.ExpandedLabel : item.Label;
            var d = new ElementDescription { Label = label.StripTags() };
            if (item.IsExpandable)
            {
                d.Role = ElementRole.TreeItem;
                d.Expanded = item.IsExpanded;
            }
            else if (IsActionRow(item))
            {
                d.Role = ActionRowRole;
            }
            else
            {
                d.ReadOnly = true;
            }
            return d;
        }

        // Filters.

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        protected override string ContentRegionName(int region)
        {
            return region == FiltersRegion
                ? (string)"RimWorldAccess.PawnGroup.FiltersRegion".Translate()
                : base.ContentRegionName(region);
        }

        protected override int ContentItemCount(int region)
        {
            return region == FiltersRegion ? Filters.Length : base.ContentItemCount(region);
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (region != FiltersRegion)
            {
                return base.DescribeContentItem(region, index);
            }
            if (index < 0 || index >= Filters.Length)
            {
                return new ElementDescription();
            }
            return new ElementDescription
            {
                Label = FilterLabel(index),
                Role = ElementRole.Checkbox,
                Check = FilterValue(index) ? CheckState.Checked : CheckState.Unchecked,
            };
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == FiltersRegion)
            {
                ToggleFilter(index);
                return;
            }
            base.ActivateContentItem(region, index);
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            return region != FiltersRegion && base.CanAdjustContentItem(region, index);
        }

        protected internal override Rect FocusedContentRect()
        {
            if (Model.RegionIndex == FiltersRegion)
            {
                int index = FocusedFilterIndex;
                return index >= 0 ? FilterRects[index] : default(Rect);
            }
            return CursorInTreeRegion ? FocusedRowRect : default(Rect);
        }

        internal int FocusedFilterIndex
        {
            get
            {
                if (Model.RegionIndex != FiltersRegion)
                {
                    return -1;
                }
                ListModel region = Model.CurrentRegion;
                return region != null && !region.IsEmpty && region.Index < Filters.Length ? region.Index : -1;
            }
        }

        internal static string FilterLabel(int index)
        {
            return FilterLabelKeys[index].Translate();
        }

        internal bool FilterValue(int index)
        {
            return (ShownGroups & Filters[index]) != 0;
        }

        /// <summary>A mouse click on a filter: moves the cursor there and toggles it, exactly as Enter would.</summary>
        internal void ClickFilter(int index)
        {
            LandCursorAt(FiltersRegion, index, announce: false);
            ToggleFilter(index);
        }

        private void ToggleFilter(int index)
        {
            if (index < 0 || index >= Filters.Length)
            {
                return;
            }
            bool next = !FilterValue(index);
            ShownGroups = next ? ShownGroups | Filters[index] : ShownGroups & ~Filters[index];
            LoadedModManager.GetMod<RimWorldAccessMod_Settings>()?.WriteSettings();
            Rebuild();
            string state = AnnouncementComposer.ComposeStateChange(
                new ElementDescription { Check = next ? CheckState.Checked : CheckState.Unchecked },
                TranslatedShellVocabulary.Instance);
            TolkHelper.SpeakData(FilterChangedAnnouncement(state));
        }

        // Buttons.

        internal IReadOnlyList<ScreenAction> CurrentActions()
        {
            return DeclaredActions;
        }

        /// <summary>The declared-action index under the cursor, or -1; Buttons follow the content regions.</summary>
        internal int CurrentActionIndex
        {
            get
            {
                if (Model.RegionIndex != ContentRegionCount)
                {
                    return -1;
                }
                ListModel region = Model.CurrentRegion;
                return region != null && !region.IsEmpty ? region.Index : -1;
            }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actionsBuffer.Clear();
                actionsBuffer.Add(new ScreenAction(
                    (string)"CloseButton".Translate(), delegate { window.Close(); }, SharedMenuGrammar.Cancel));
                return actionsBuffer;
            }
        }
    }
}
