using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

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
        private const float RowHeight = TreeRowPainter.RowHeight;
        private const float MaxButtonWidth = 200f;

        private Vector2 scrollPosition;
        private int scrolledTo = -1;
        private readonly Dictionary<string, string> truncateCache = new Dictionary<string, string>();
        private float truncatedForWidth = -1f;
        private int truncatedForVersion = -1;
        private string title;
        private LoadedLanguage titleLanguage;

        internal MapOverviewScope Scope;

        protected MapOverviewWindow()
        {
            doCloseX = true;
            absorbInputAroundWindow = true;
            draggable = true;
            resizeable = true;
        }

        protected abstract string TitleKey { get; }

        internal string Title
        {
            get
            {
                if (title == null || titleLanguage != LanguageDatabase.activeLanguage)
                {
                    titleLanguage = LanguageDatabase.activeLanguage;
                    title = TitleKey.Translate();
                }
                return title;
            }
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
                ScreenControlPainter.DrawCheckboxColumn(new Rect(inRect.x, y, FilterColumnWidth, bodyHeight), scope.FilterRects,
                    MapOverviewScope.FilterCount, MapOverviewScope.FilterLabel, scope.FilterValue, null, scope.ClickFilter,
                    scope.FocusedFilterIndex);
                DrawList(new Rect(inRect.x + FilterColumnWidth + 10f, y, inRect.width - FilterColumnWidth - 10f, bodyHeight), scope);
                ScreenControlPainter.DrawActionRow(new Rect(inRect.x, buttonRowY, inRect.width, buttonHeight),
                    scope.CurrentActions(), scope.CurrentActionIndex, MaxButtonWidth);
            }
        }

        private void DrawList(Rect outer, MapOverviewScope scope)
        {
            scope.FocusedRowRect = default(Rect);
            ITreeTwinMount rows = scope.RowCache;
            int rowCount = rows.RowCount;
            Rect viewRect = new Rect(0f, 0f, outer.width - 16f, rowCount * RowHeight);
            if (viewRect.width != truncatedForWidth || scope.TwinRowVersion != truncatedForVersion)
            {
                truncateCache.Clear();
                truncatedForWidth = viewRect.width;
                truncatedForVersion = scope.TwinRowVersion;
            }

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
            for (int i = 0; i < rowCount; i++)
            {
                Rect rowRect = new Rect(0f, i * RowHeight, viewRect.width, RowHeight);
                if (rowRect.yMax < scrollPosition.y || rowRect.y > scrollPosition.y + outer.height)
                {
                    continue;
                }
                TreeRowPainter.DrawRow(rows.RowAt(i), rowRect, i, i == focused, ringed && i == focused, mouseover: true, truncateCache);
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

        private static readonly (MapPawnGroupFilter Flag, string LabelKey)[] Filters =
        {
            (MapPawnGroupFilter.Colonists, "RimWorldAccess.PawnGroup.Filter.Colonists"),
            (MapPawnGroupFilter.ColonyAnimals, "RimWorldAccess.PawnGroup.Filter.ColonyAnimals"),
            (MapPawnGroupFilter.ColonyMechs, "RimWorldAccess.PawnGroup.Filter.ColonyMechs"),
            (MapPawnGroupFilter.PrisonersAndSlaves, "RimWorldAccess.PawnGroup.Filter.PrisonersAndSlaves"),
            (MapPawnGroupFilter.Visitors, "RimWorldAccess.PawnGroup.Filter.Visitors"),
            (MapPawnGroupFilter.Hostiles, "RimWorldAccess.PawnGroup.Filter.Hostiles"),
        };

        private static readonly string[] filterLabels = new string[Filters.Length];
        private static LoadedLanguage filterLabelsLanguage;

        internal static int FilterCount => Filters.Length;

        private readonly MapOverviewWindow window;
        private readonly ScreenAction[] actions;
        private int builtSignature;
        private float lastRebuildTime = -1f;
        private int listedCount;

        internal readonly Rect[] FilterRects = new Rect[Filters.Length];
        internal Rect FocusedRowRect;

        internal readonly ITreeTwinMount RowCache;

        protected MapOverviewScope(MapOverviewWindow window)
        {
            this.window = window;
            actions = new[] { new ScreenAction((string)"CloseButton".Translate(), delegate { window.Close(); }, SharedMenuGrammar.Cancel) };
            RowCache = new RowMount(this);
            Claim(SharedMenuGrammar.Info, e => PerformInfoCard());
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

        /// <summary>A fresh tree, every node's Data unique among its siblings and stable across rebuilds, plus the pawn count the
        /// filter confirmation speaks. Always called right after <see cref="ContentSignature"/> for the same filter, so it may reuse that scan.</summary>
        protected abstract InspectionTreeItem BuildRoot(MapPawnGroupFilter shown, out int listed);

        protected abstract string FilterChangedOneKey { get; }

        protected abstract string FilterChangedKey { get; }

        /// <summary>Whether a leaf row does something on Enter; the rest read as read-only.</summary>
        protected abstract bool IsActionRow(InspectionTreeItem item);

        /// <summary>The role an action row speaks.</summary>
        protected virtual ElementRole ActionRowRole
        {
            get { return ElementRole.None; }
        }

        /// <summary>The thing whose icon leads a row, or null for none.</summary>
        protected virtual Thing RowIcon(InspectionTreeItem item)
        {
            return null;
        }

        public override void OnPush()
        {
            window.Scope = this;
            // A re-push keeps the live tree, and with it the cursor and expansion.
            if (Tree.Root == null)
            {
                if (ScreenReturn.TryTakeCursor(Name, out TreeReturnCursor saved))
                {
                    SetTreeRoot(saved.Root);
                    Rebuild(saved.Index);
                }
                else
                {
                    MapPawnGroupFilter shown = ShownGroups;
                    SetTreeRoot(BuildFresh(shown, ContentSignature(shown)));
                }
            }
            base.OnPush();
        }

        public override void OnPop()
        {
            if (ReferenceEquals(window.Scope, this))
            {
                window.Scope = null;
            }
            base.OnPop();
        }

        protected override string ComposeOpenAnnouncement()
        {
            return window.Title;
        }

        /// <summary>
        /// Closes the window, jumps to the pawn and opens <paramref name="tab"/> on <paramref name="landing"/>. The
        /// window closes first: the inspect-tab bridge stands down under a foreign dialog. The tab's own tree then
        /// speaks, so success says nothing here. Escape on the tab, or on the map when no tab opened, runs
        /// <paramref name="reopen"/> and lands back on this row.
        /// </summary>
        protected void HandOffToTab(Pawn pawn, Type tab, object landing, string unavailableKey, Action reopen)
        {
            if (pawn.Destroyed || !pawn.Spawned || !CameraJumper.CanJump(pawn))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData(unavailableKey.Translate());
                return;
            }
            window.Close();
            CameraJumper.TryJumpAndSelect(pawn);
            bool landed = WindowlessInspectionState.OpenTabLandingOn(tab, landing);
            if (!landed)
            {
                MapNavigationState.SpeakJumpedTo(pawn.LabelShort);
            }
            ScreenReturn.Arm(landed ? ScreenReturn.Binding.Inspection : ScreenReturn.Binding.Map,
                Name, new TreeReturnCursor(Tree.Root, CurrentRowIndex), reopen);
        }

        private void PerformInfoCard()
        {
            InspectionTreeItem item = CurrentTreeItem();
            if (item?.OnInfo != null)
            {
                item.OnInfo();
            }
            else if (item?.LinkedDef != null)
            {
                InfoCardState.OpenInfoCardForDef(item.LinkedDef);
            }
            else
            {
                InfoCardState.SpeakNoInfoCardAvailable();
            }
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

        private sealed class RowMount : TreeRegionTwinMount
        {
            private readonly MapOverviewScope owner;

            internal RowMount(MapOverviewScope owner)
                : base(owner, () => owner.window.Title)
            {
                this.owner = owner;
            }

            protected override void Decorate(InspectionTreeItem item, ref TreeTwinRow row)
            {
                row.Label = item.SearchText;
                row.IconThing = owner.RowIcon(item);
            }
        }

        internal void SyncRows()
        {
            if (TypeaheadHasActiveSearch || CurrentPanel.SearchExpansionSnapshot != null
                || Time.realtimeSinceStartup - lastRebuildTime < LiveRebuildSeconds)
            {
                return;
            }
            lastRebuildTime = Time.realtimeSinceStartup;
            MapPawnGroupFilter shown = ShownGroups;
            int signature = ContentSignature(shown);
            if (signature != builtSignature)
            {
                Rebuild(CurrentRowIndex, shown, signature);
            }
        }

        private InspectionTreeItem BuildFresh(MapPawnGroupFilter shown, int signature)
        {
            builtSignature = signature;
            lastRebuildTime = Time.realtimeSinceStartup;
            return BuildRoot(shown, out listedCount);
        }

        private void Rebuild(int oldIndex)
        {
            MapPawnGroupFilter shown = ShownGroups;
            Rebuild(oldIndex, shown, ContentSignature(shown));
        }

        private void Rebuild(int oldIndex, MapPawnGroupFilter shown, int signature)
        {
            int index = SetTreeRootPreservingState(BuildFresh(shown, signature), oldIndex, TreeStatePreserve.CursorRule.FollowData);
            if (index < 0)
            {
                index = Math.Min(Math.Max(oldIndex, 0), Tree.Count - 1);
                Tree.SetSelectedIndex(index);
            }
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
            if (filterLabelsLanguage != LanguageDatabase.activeLanguage || filterLabels[index] == null)
            {
                filterLabelsLanguage = LanguageDatabase.activeLanguage;
                for (int i = 0; i < Filters.Length; i++)
                {
                    filterLabels[i] = Filters[i].LabelKey.Translate();
                }
            }
            return filterLabels[index];
        }

        internal bool FilterValue(int index)
        {
            return (ShownGroups & Filters[index].Flag) != 0;
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
            MapPawnGroupFilter flag = Filters[index].Flag;
            ShownGroups = next ? ShownGroups | flag : ShownGroups & ~flag;
            LoadedModManager.GetMod<RimWorldAccessMod_Settings>()?.WriteSettings();
            Rebuild(CurrentRowIndex);
            string state = AnnouncementComposer.ComposeStateChange(
                new ElementDescription { Check = next ? CheckState.Checked : CheckState.Unchecked },
                TranslatedShellVocabulary.Instance);
            TolkHelper.SpeakData(listedCount == 1
                ? (string)FilterChangedOneKey.Translate(state)
                : (string)FilterChangedKey.Translate(state, listedCount));
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
            get { return actions; }
        }
    }
}
