using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Generic tree-hosting layer. A content region presents a subclass's own prefix rows
    /// (<see cref="PrefixRowCount"/> — steppers, quick actions; NOT tree nodes) followed by the
    /// flattened visible list of an <see cref="RimWorldAccess.InspectionTreeItem"/> tree driven by
    /// <see cref="TreeModel{T}"/>. Carries no filter/checkbox logic — that is
    /// <see cref="FilterTreeScopeBase"/> one layer up.
    ///
    /// Left/Right expand/collapse; Enter/Space are the ACTIVATION path
    /// (<see cref="ActivateTreeNode"/>) and never expand on their own. Plain Home/End are
    /// sibling-scoped tree jumps, not the base's flat row jump; Ctrl+Home/Ctrl+End give the
    /// whole-list variant. Page Up/Down (<see cref="PerformJumpToAdjacentSection"/>) is NOT
    /// claimed here — several screen ids ride this base without those ids registered, so an
    /// unconditional claim would throw at dispatch; a subclass registers and claims them itself.
    ///
    /// Typeahead rides the shared ScreenScope engine. The search-time auto-expansion snapshot is
    /// restored from <see cref="RefreshContent"/> once a finished search is observed rather than
    /// synchronously at settle/clear time: neither the settle path nor the engine's Escape/
    /// backspace handlers are virtual, and every navigation action refreshes before announcing,
    /// so the restore is late rather than wrong.
    ///
    /// <see cref="ElementDescription.Level"/> is set unconditionally here. The "speak the level
    /// only when it changed" gate must NOT live at this seam: <see cref="DescribeContentItem"/>
    /// also runs once per visible row while building the typeahead haystack, which would corrupt
    /// any stateful gate before the real announcement consults it. The gate sits downstream on
    /// the single speaking path. Position is per-item and safe here, and is the node's SIBLING
    /// position rather than the flat row index.
    /// </summary>
    public abstract partial class TreeRegionScope : ScreenScope
    {
        /// <summary>
        /// One tree and the per-tree state that belongs with it. A single-tree screen uses the
        /// one panel this class creates for itself; a screen drawing two filter panels side by
        /// side calls <see cref="CreatePanel"/> per panel and maps them through
        /// <see cref="PanelFor"/>. The search snapshot and boundary pointer live here, not on the
        /// scope: two independently searched panels would otherwise restore each other's
        /// expansion.
        /// </summary>
        protected sealed class TreePanel
        {
            public readonly TreeModel<InspectionTreeItem> Tree =
                new TreeModel<InspectionTreeItem>(new InspectionTreeItemShape());

            /// <summary>Snapshot of pre-search expansion state, non-null only while an auto-expansion is outstanding.</summary>
            internal Dictionary<InspectionTreeItem, bool> SearchExpansionSnapshot;

            /// <summary>Frame the snapshot was taken — see RefreshContent's same-frame restore guard.</summary>
            internal int SearchExpansionFrame = -1;

            /// <summary>
            /// Parent of the most recently landed-on tree node, or null right after a rebuild —
            /// the boundary-crossing state <see cref="AnnouncePrefix"/> compares against. Reset
            /// explicitly at every rebuild site rather than relying on fresh nodes never matching.
            /// </summary>
            internal InspectionTreeItem LastAnnouncedParent;
        }

        private readonly List<TreePanel> panels = new List<TreePanel>();
        private readonly TreePanel defaultPanel;

        /// <summary>
        /// The tree under the cursor, falling back to the first panel when the cursor rests in a
        /// non-tree region. A multi-panel subclass addresses its panels explicitly instead.
        /// </summary>
        protected TreeModel<InspectionTreeItem> Tree
        {
            get { return CurrentPanel.Tree; }
        }

        protected TreePanel CurrentPanel
        {
            get { return PanelFor(Model.RegionIndex) ?? defaultPanel; }
        }

        /// <summary>
        /// The panel a content region presents, or null when the region is not a tree. The
        /// default maps the one panel onto <see cref="TreeRegionIndex"/>, which is every
        /// single-tree subclass's shape.
        /// </summary>
        protected virtual TreePanel PanelFor(int region)
        {
            return region == TreeRegionIndex ? defaultPanel : null;
        }

        /// <summary>Registers an additional panel — see <see cref="TreePanel"/>. Call from the subclass constructor.</summary>
        protected TreePanel CreatePanel()
        {
            var panel = new TreePanel();
            panel.Tree.OnBeforeExpand = OnBeforeExpandNode;
            panels.Add(panel);
            return panel;
        }

        protected TreeRegionScope()
        {
            defaultPanel = CreatePanel();

            Claim("tree.expandAllSiblings", e => PerformExpandAllSiblings());
            Claim("tree.jumpToFirstAbsolute", e => PerformTreeEdgeJump(true, true));
            Claim("tree.jumpToLastAbsolute", e => PerformTreeEdgeJump(false, true));

            // tree.jumpToPreviousSection/tree.jumpToNextSection are DELIBERATELY NOT claimed
            // here: several screen ids ride this base without those ids registered under their
            // own screen id, and an unconditional claim would throw at dispatch time for all of
            // them. A subclass wanting Page Up/Down claims them in its OWN constructor after
            // registering both ids under its own screen id.
        }

        // ------------------------------------------------------------------
        // The tree-hosting contract a subclass fills in.
        // ------------------------------------------------------------------

        /// <summary>
        /// Number of rows BEFORE the tree in the shared content region. These are NOT tree nodes;
        /// 0 (the default) means the region is pure tree.
        /// </summary>
        protected virtual int PrefixRowCount
        {
            get { return 0; }
        }

        protected virtual ElementDescription DescribePrefixRow(int index)
        {
            return new ElementDescription();
        }

        protected virtual void ActivatePrefixRow(int index)
        {
        }

        protected virtual bool CanAdjustPrefixRow(int index)
        {
            return false;
        }

        protected virtual void AdjustPrefixRow(int index, int direction)
        {
        }

        // Region-parameterized twins of the five hooks above; TreeRegionScope calls THESE. Each
        // defaults to the single-region form, so only a subclass whose regions differ from each
        // other overrides these instead of the originals.

        protected virtual int PrefixRowCountFor(int region)
        {
            return PrefixRowCount;
        }

        protected virtual ElementDescription DescribePrefixRow(int region, int index)
        {
            return DescribePrefixRow(index);
        }

        protected virtual void ActivatePrefixRow(int region, int index)
        {
            ActivatePrefixRow(index);
        }

        protected virtual bool CanAdjustPrefixRow(int region, int index)
        {
            return CanAdjustPrefixRow(index);
        }

        protected virtual void AdjustPrefixRow(int region, int index, int direction)
        {
            AdjustPrefixRow(index, direction);
        }

        /// <summary>Localized name of the shared content region.</summary>
        protected abstract string TreeRegionLabel { get; }

        /// <summary>
        /// The content region index the tree occupies. The cursor-reading helpers consult it so a
        /// cursor resting in a non-tree region is never mistaken for tree row
        /// (index - PrefixRowCount). Override only when the tree is not region 0.
        /// </summary>
        protected virtual int TreeRegionIndex { get { return 0; } }

        protected bool CursorInTreeRegion { get { return PanelFor(Model.RegionIndex) != null; } }

        /// <summary>Prefix-row count of the region the cursor is in — the offset every cursor-scoped helper below subtracts.</summary>
        private int CursorPrefixRowCount { get { return PrefixRowCountFor(Model.RegionIndex); } }

        /// <summary>
        /// Per-node description: label plus whatever structured state applies (<c>Check</c>,
        /// <c>Expanded</c>, <c>Extras</c>) — state words belong in those fields, never baked into
        /// the label. The base composes Level and sibling Position on top; set them here only to
        /// override that default.
        /// </summary>
        protected abstract ElementDescription DescribeTreeNode(InspectionTreeItem item);

        /// <summary>
        /// Enter/Space on a tree row. Activation never expands on its own — expand/collapse is
        /// Left/Right; a scope wanting Enter to toggle a branch routes it through
        /// <see cref="PerformActivateExpandToggle"/>.
        /// </summary>
        protected abstract void ActivateTreeNode(InspectionTreeItem item);

        /// <summary>Lazy child population hook, called before a node expands; no-op by default.</summary>
        protected virtual void OnBeforeExpandNode(InspectionTreeItem item)
        {
        }

        /// <summary>Which nodes typeahead auto-expands on the first search character.</summary>
        protected virtual bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            return false;
        }

        /// <summary>
        /// Opt-in Page Up/Down section-boundary predicate. False for every node (the default)
        /// leaves <see cref="PerformJumpToAdjacentSection"/> claimed but always rejecting, which
        /// is right for a flat tree with no detail sections.
        /// </summary>
        protected virtual bool IsSectionBoundary(InspectionTreeItem item)
        {
            return false;
        }

        // ------------------------------------------------------------------
        // Tree lifecycle (call from the concrete screen's Open()/Close()).
        // ------------------------------------------------------------------

        protected void SetTreeRoot(InspectionTreeItem root, int initialIndex = 0)
        {
            SetTreeRoot(defaultPanel, root, initialIndex);
        }

        protected void SetTreeRoot(TreePanel panel, InspectionTreeItem root, int initialIndex = 0)
        {
            // Clear BEFORE the root swap so SetRoot's flatten runs with the user's real
            // submenu preference back in force (not the search window's suppressed flatten)
            // and initialIndex lands against the final visible list.
            ClearSearchExpansionState(panel);
            SyncTreeSettings(panel);
            panel.Tree.SetRoot(root, initialIndex);
            panel.LastAnnouncedParent = null;
        }

        protected void ResetTree()
        {
            ResetTree(defaultPanel);
        }

        protected void ResetTree(TreePanel panel)
        {
            ClearSearchExpansionState(panel);
            panel.Tree.Reset();
            panel.LastAnnouncedParent = null;
        }

        /// <summary>
        /// Replaces the tree root while preserving expansion state and landing the cursor back on
        /// the same LOGICAL node rather than the same row index. Takes and returns CONTENT-REGION
        /// indices (including PrefixRowCount); returns -1 when the old cursor node is gone from
        /// the rebuilt tree, leaving the caller's numeric clamp in force. Does NOT move the region
        /// cursor — call <see cref="SyncRegionFromCurrentTree"/>, then announce once.
        /// </summary>
        protected int SetTreeRootPreservingState(InspectionTreeItem newRoot, int regionCursorIndex)
        {
            return SetTreeRootPreservingState(defaultPanel, newRoot, regionCursorIndex);
        }

        protected int SetTreeRootPreservingState(TreePanel panel, InspectionTreeItem newRoot, int regionCursorIndex)
        {
            int prefixRows = PrefixRowCountFor(RegionOf(panel));
            int treeCursor = regionCursorIndex - prefixRows;
            // Same clear-before-swap ordering as SetTreeRoot above, for the same reason.
            ClearSearchExpansionState(panel);
            int restored = TreeStatePreserve.SetRootPreservingState(panel.Tree, newRoot, r =>
            {
                SyncTreeSettings(panel);
                panel.Tree.SetRoot(r);
            }, treeCursor, OnBeforeExpandNode);
            panel.LastAnnouncedParent = null;
            return restored >= 0 ? restored + prefixRows : -1;
        }

        /// <summary>The content region a panel presents. Falls back to the tree region for the single-panel case.</summary>
        private int RegionOf(TreePanel panel)
        {
            for (int region = 0; region < ContentRegionCount; region++)
            {
                if (ReferenceEquals(PanelFor(region), panel))
                {
                    return region;
                }
            }
            return TreeRegionIndex;
        }

        private void SyncTreeSettings(TreePanel panel)
        {
            panel.Tree.Wrap = WrapItems;
            panel.Tree.SubmenuMode = RimWorldAccessMod_Settings.Settings != null
                && RimWorldAccessMod_Settings.Settings.SubmenuTreeNavigation;
        }

        /// <summary>
        /// Clears boundary tracking on every push, before a subclass's own OnPush runs: a
        /// subclass may rebuild the tree through some path other than the three rebuild helpers,
        /// or defer the rebuild to a later RefreshContent.
        /// </summary>
        public override void OnPush()
        {
            base.OnPush();
            for (int i = 0; i < panels.Count; i++)
            {
                panels[i].LastAnnouncedParent = null;
            }
        }

        /// <summary>
        /// Submenu-mode-only boundary announcement. Submenu mode hides expanded parent rows from
        /// <see cref="TreeModel{T}.Visible"/>, so Down from one parent's last child lands on the
        /// next parent's first child with no audible signal the group changed; this speaks the new
        /// parent's bare name (<c>ExpandedLabel</c> when the parent's own row is folded, else
        /// <c>Label</c>) — never the expansion suffix, which would double-speak the child count.
        /// Silent in standard mode (parent rows are visible), for the hidden root or the tree's
        /// own root, and on the first landing since a rebuild.
        /// </summary>
        protected override string AnnouncePrefix(int region, int index)
        {
            InspectionTreeItem item = CurrentTreeItem();
            if (item == null)
            {
                return null;
            }
            TreePanel panel = CurrentPanel;
            InspectionTreeItem previous = panel.LastAnnouncedParent;
            InspectionTreeItem parent = item.Parent;
            panel.LastAnnouncedParent = parent;
            if (previous == null)
            {
                return null;
            }
            if (!panel.Tree.SubmenuMode || ReferenceEquals(parent, previous))
            {
                return null;
            }
            if (parent == null || parent.IndentLevel < 0 || ReferenceEquals(parent, panel.Tree.Root))
            {
                return null;
            }
            return parent.ExpandedLabel ?? parent.Label;
        }

        /// <summary>
        /// Clears the boundary-crossing state after an in-place section rebuild. The tracked
        /// parent is a node reference, so a freshly built section node never matches the old one
        /// and the next landing would re-announce a prefix the player never left. Call once the
        /// rebuild's tree mutation is complete, typically right after Reflatten.
        /// </summary>
        protected void ResetBoundaryTracking()
        {
            CurrentPanel.LastAnnouncedParent = null;
        }

        /// <summary>
        /// Whether tree rows report their flat position within the content region instead of
        /// their position among siblings. True suits a region that mixes always-flat prefix rows
        /// with tree rows, where sibling counts change partway down and read as a mismatch. Level
        /// is announced either way, so depth survives the switch.
        /// </summary>
        protected virtual bool UseFlatRegionPositions
        {
            get { return false; }
        }

        // ------------------------------------------------------------------
        // ScreenScope content-region wiring (one shared region: prefix rows then the tree).
        // ------------------------------------------------------------------

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return TreeRegionLabel;
        }

        protected override int ContentItemCount(int region)
        {
            TreePanel panel = PanelFor(region);
            return PrefixRowCountFor(region) + (panel != null ? panel.Tree.Count : 0);
        }

        /// <summary>
        /// The region whose row is being described right now. <see cref="DescribeTreeNode"/> takes
        /// no region but IS called for regions other than the cursor's (the typeahead haystack
        /// sweeps every row of every region), so a multi-panel subclass must read this rather than
        /// the cursor's region.
        /// </summary>
        protected int DescribingRegion { get; private set; }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            DescribingRegion = region;
            int prefixRows = PrefixRowCountFor(region);
            if (index < prefixRows)
            {
                ElementDescription pd = DescribePrefixRow(region, index) ?? new ElementDescription();
                if (!pd.PositionIndex.HasValue && !UseFlatRegionPositions)
                {
                    // Own group: the flat count would swing with every expand/collapse.
                    pd.PositionIndex = index + 1;
                    pd.PositionCount = prefixRows;
                }
                return pd;
            }
            TreePanel panel = PanelFor(region);
            if (panel == null)
            {
                return new ElementDescription();
            }
            TreeModel<InspectionTreeItem> tree = panel.Tree;
            int treeIndex = index - prefixRows;
            if (treeIndex < 0 || treeIndex >= tree.Count)
            {
                return new ElementDescription();
            }
            InspectionTreeItem item = tree.Visible[treeIndex];
            ElementDescription d = DescribeTreeNode(item) ?? new ElementDescription();
            if (!d.PositionIndex.HasValue)
            {
                if (UseFlatRegionPositions)
                {
                    d.PositionIndex = index + 1;
                    d.PositionCount = ContentItemCount(region);
                }
                else
                {
                    var siblingPosition = tree.GetSiblingPosition(item);
                    d.PositionIndex = siblingPosition.position;
                    d.PositionCount = siblingPosition.total;
                }
            }
            if (!d.Level.HasValue)
            {
                // Unconditional — see the class header's Level-gating hazard note.
                d.Level = item.IndentLevel + 1;
            }
            if (!d.Selected.HasValue)
            {
                d.Selected = item.Selected;
            }
            // Ahead of whatever the subclass added (the inspection tree's "inspectable", say).
            if (!item.IsExpanded && !string.IsNullOrEmpty(item.Detail))
            {
                d.Extras = string.IsNullOrEmpty(d.Extras) ? item.Detail : item.Detail + ". " + d.Extras;
            }
            return d;
        }

        /// <summary>Matched against the detail too, so a row stays findable by everything it speaks.</summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            int prefixRows = PrefixRowCountFor(region);
            TreePanel panel = PanelFor(region);
            if (panel == null || row < prefixRows)
            {
                return base.ContentRowSearchText(region, row);
            }
            int treeIndex = row - prefixRows;
            return treeIndex < panel.Tree.Count
                ? panel.Tree.Visible[treeIndex].SearchText.StripTags()
                : "";
        }

        protected override string ContentRowSearchIdentity(int region, int row)
        {
            int prefixRows = PrefixRowCountFor(region);
            TreePanel panel = PanelFor(region);
            if (panel == null || row < prefixRows)
            {
                return null;
            }
            int treeIndex = row - prefixRows;
            return treeIndex < panel.Tree.Count ? TreeNodeSearchIdentity(panel.Tree.Visible[treeIndex]) : null;
        }

        /// <summary>
        /// One node's stable ranking identity (see <see cref="TypeaheadCandidate.Identity"/>) —
        /// the underlying def's own label where node labels are composed forms the game rewrites.
        /// </summary>
        protected virtual string TreeNodeSearchIdentity(InspectionTreeItem item)
        {
            return null;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            int prefixRows = PrefixRowCountFor(region);
            if (index < prefixRows)
            {
                ActivatePrefixRow(region, index);
                return;
            }
            TreePanel panel = PanelFor(region);
            if (panel == null)
            {
                return;
            }
            int treeIndex = index - prefixRows;
            if (treeIndex < 0 || treeIndex >= panel.Tree.Count)
            {
                return;
            }
            panel.Tree.SetSelectedIndex(treeIndex);
            ActivateTreeNode(panel.Tree.Visible[treeIndex]);
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            if (index < PrefixRowCountFor(region))
            {
                return CanAdjustPrefixRow(region, index);
            }
            // Every tree row participates in Left/Right, non-expandable rows included: they get
            // the reject sound rather than falling through to some other handler.
            return PanelFor(region) != null;
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            int prefixRows = PrefixRowCountFor(region);
            if (index < prefixRows)
            {
                AdjustPrefixRow(region, index, direction);
                return;
            }
            TreePanel panel = PanelFor(region);
            if (panel == null)
            {
                return;
            }
            TreeModel<InspectionTreeItem> tree = panel.Tree;
            int treeIndex = index - prefixRows;
            if (treeIndex < 0 || treeIndex >= tree.Count)
            {
                return;
            }
            // Expand/collapse clears an active search before acting.
            TypeaheadReset();
            tree.SetSelectedIndex(treeIndex);
            if (direction > 0)
            {
                PerformExpand(panel);
            }
            else
            {
                PerformCollapse(panel);
            }
        }

        /// <summary>
        /// Whether this scope's tree is the one <see cref="TreeTwinWindow"/> is currently drawing.
        /// A scope that owns a real window always answers false and routes through the base path.
        /// </summary>
        private bool TwinMounted
        {
            get { return TreeTwinWindow.IsActiveMountScope(this); }
        }

        /// <summary>
        /// The twin is an ImmediateWindow, which <see cref="ShellGuards.NonImmediateWindowUnderPointer"/>
        /// skips by design, so handing it to PointerRouting.PointerOwnedBy asks exactly the
        /// right question: the pointer is inside the twin and no REAL window sits over it.
        /// </summary>
        protected override Window PointerSurface
        {
            get { return base.PointerSurface ?? (TwinMounted ? TreeTwinWindow.LiveWindow : null); }
        }

        private protected override void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            base.CollectRouteCandidates(candidates, targets);
            if (!TwinMounted)
            {
                return;
            }
            IReadOnlyList<PointerHitCandidate> hits = TreeTwinWindow.RowHits;
            IReadOnlyList<int> rows = TreeTwinWindow.RowHitRows;
            // Twin row indices index the tree's visible rows, so the model index adds the prefix.
            int prefix = PrefixRowCountFor(TreeRegionIndex);
            for (int i = 0; i < hits.Count; i++)
            {
                candidates.Add(hits[i]);
                targets.Add(new RouteTarget { Region = TreeRegionIndex, Index = prefix + rows[i] });
            }
        }

        protected override void RefreshContent()
        {
            // A snapshot is never restored in the frame it was taken: HandleChar snapshots and
            // expands BEFORE its own RefreshModel, and the search only becomes active after that
            // refresh, so without the frame guard the first typed character collapses everything
            // back before the search space is built.
            TreePanel panel = CurrentPanel;
            if (!TypeaheadHasActiveSearch && panel.SearchExpansionSnapshot != null
                && UnityEngine.Time.frameCount != panel.SearchExpansionFrame)
            {
                RestoreSearchExpansion(panel);
            }
        }


        // ------------------------------------------------------------------
        // Plain Home/End: sibling-scoped tree jump, not the base's flat row jump.
        // ------------------------------------------------------------------

        protected override void MoveItemEdge(bool first)
        {
            PerformTreeEdgeJump(first, false);
        }

        private void PerformTreeEdgeJump(bool first, bool absolute)
        {
            // An active search owns Home/End for its first/last-match jump; the sibling-scoped
            // tree jump below is plain navigation only.
            if (TypeaheadHasActiveSearch)
            {
                base.MoveItemEdge(first);
                return;
            }
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return;
            }
            int treeIndex = region.Index - CursorPrefixRowCount;
            if (!CursorInTreeRegion || treeIndex < 0 || treeIndex >= Tree.Count)
            {
                // No tree context for a sibling-scoped jump: fall back to the flat region edge.
                if (!MenuHelper.SoundMove(first ? region.MoveFirst() : region.MoveLast()))
                {
                    return;
                }
                NotifyCursorSettled();
                AnnounceCurrent(CellAxis.Row);
                return;
            }
            Tree.SetSelectedIndex(treeIndex);
            MoveResult result = first ? Tree.HomeKey(absolute) : Tree.EndKey(absolute);
            if (result.Kind == MoveKind.Empty)
            {
                return;
            }
            SyncRegionFromTree(region);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            NotifyCursorSettled();
            AnnounceCurrent(CellAxis.Row);
        }

        /// <summary>
        /// Page Up/Down: jump to the previous/next node satisfying <see cref="IsSectionBoundary"/>,
        /// without wrapping. Rejects when a search is active, the cursor sits on a prefix row, or
        /// no boundary lies in the searched direction. Not claimed here — a subclass claims it
        /// after registering both ids under its own screen id (see the constructor).
        /// </summary>
        protected void PerformJumpToAdjacentSection(bool forward)
        {
            RefreshModel();
            if (TypeaheadHasActiveSearch)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return;
            }
            int treeIndex = region.Index - CursorPrefixRowCount;
            if (!CursorInTreeRegion || treeIndex < 0 || treeIndex >= Tree.Count)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            int step = forward ? 1 : -1;
            for (int i = treeIndex + step; i >= 0 && i < Tree.Count; i += step)
            {
                if (IsSectionBoundary(Tree.Visible[i]))
                {
                    Tree.SetSelectedIndex(i);
                    SyncRegionFromCurrentTree();
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    NotifyCursorSettled();
                    AnnounceCurrentItem();
                    return;
                }
            }
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        // ------------------------------------------------------------------
        // Left/Right: expand/collapse dispatch.
        // ------------------------------------------------------------------

        private void PerformExpand(TreePanel panel)
        {
            TreeActionResult<InspectionTreeItem> result = panel.Tree.ExpandOrDrillDown();
            switch (result.Kind)
            {
                case TreeActionKind.Rejected:
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    break;
                case TreeActionKind.Expanded:
                case TreeActionKind.ExpandedSubmenu:
                    SoundDefOf.FloatMenu_Open.PlayOneShotOnCamera();
                    SyncRegionFromCurrentTree();
                    MarkParentAnnounced(panel);
                    NotifyCursorSettled();
                    AnnounceCurrentItem();
                    break;
                case TreeActionKind.DrilledToChild:
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    SyncRegionFromCurrentTree();
                    MarkParentAnnounced(panel);
                    NotifyCursorSettled();
                    AnnounceCurrentItem();
                    break;
            }
        }

        /// <summary>A deliberate expand/drill stands the submenu boundary prefix down for its landing.</summary>
        private static void MarkParentAnnounced(TreePanel panel)
        {
            IReadOnlyList<InspectionTreeItem> visible = panel.Tree.Visible;
            int index = panel.Tree.SelectedIndex;
            if (index >= 0 && index < visible.Count)
            {
                panel.LastAnnouncedParent = visible[index].Parent;
            }
        }

        /// <summary>
        /// Enter on a branch node: expand-and-drill in submenu mode (the call Right arrow uses),
        /// or a plain expand/collapse toggle in standard mode — the one place Enter also
        /// collapses, since Right arrow only ever expands.
        /// </summary>
        protected void PerformActivateExpandToggle(InspectionTreeItem item)
        {
            if (Tree.SubmenuMode)
            {
                TreeActionResult<InspectionTreeItem> result = Tree.ExpandOrDrillDown();
                switch (result.Kind)
                {
                    case TreeActionKind.Expanded:
                    case TreeActionKind.ExpandedSubmenu:
                        SoundDefOf.FloatMenu_Open.PlayOneShotOnCamera();
                        RefreshModel();
                        SyncRegionFromCurrentTree();
                        MarkParentAnnounced(CurrentPanel);
                        AnnounceCurrentItem();
                        break;
                    case TreeActionKind.DrilledToChild:
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        RefreshModel();
                        SyncRegionFromCurrentTree();
                        MarkParentAnnounced(CurrentPanel);
                        AnnounceCurrentItem();
                        break;
                        // Rejected/None: silent.
                }
                return;
            }

            if (!item.IsExpanded)
            {
                OnBeforeExpandNode(item);
            }
            item.IsExpanded = !item.IsExpanded;
            Tree.Reflatten();
            if (Tree.SelectedIndex >= Tree.Count)
            {
                Tree.SetSelectedIndex(Tree.Count - 1);
            }
            (item.IsExpanded ? SoundDefOf.FloatMenu_Open : SoundDefOf.FloatMenu_Cancel).PlayOneShotOnCamera();
            RefreshModel();
            SyncRegionFromCurrentTree();
            AnnounceCurrentItem();
        }

        private void PerformCollapse(TreePanel panel)
        {
            TreeActionResult<InspectionTreeItem> result = panel.Tree.CollapseOrDrillUp();
            switch (result.Kind)
            {
                case TreeActionKind.Rejected:
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    break;
                case TreeActionKind.Collapsed:
                case TreeActionKind.CollapsedToParent:
                    SoundDefOf.FloatMenu_Cancel.PlayOneShotOnCamera();
                    SyncRegionFromCurrentTree();
                    NotifyCursorSettled();
                    AnnounceCurrentItem();
                    break;
                case TreeActionKind.DrilledToParent:
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    SyncRegionFromCurrentTree();
                    NotifyCursorSettled();
                    AnnounceCurrentItem();
                    break;
            }
        }

        private void PerformExpandAllSiblings()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return;
            }
            int treeIndex = region.Index - CursorPrefixRowCount;
            if (!CursorInTreeRegion || treeIndex < 0 || treeIndex >= Tree.Count)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            Tree.SetSelectedIndex(treeIndex);
            ExpandSiblingsResult result = Tree.ExpandAllSiblings();
            if (result.ExpandedCount > 0)
            {
                TypeaheadReset();
                ClearSearchExpansionState(CurrentPanel);
                EmbeddedAudioHelper.PlaySoundDefWithReverb(SoundDefOf.FloatMenu_Open);
                TolkHelper.Speak((result.ExpandedCount == 1
                    ? "RimWorldAccess.Tree.ExpandedCountOne"
                    : "RimWorldAccess.Tree.ExpandedCountMany").Loc(result.ExpandedCount));
                SyncRegionFromCurrentTree();
                if (Tree.SubmenuMode)
                {
                    AnnounceCurrentItem();
                }
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak((result.AnyExpandable
                    ? "RimWorldAccess.Tree.AllAlreadyExpanded"
                    : "RimWorldAccess.Tree.NoneToExpand").Loc());
            }
        }

        /// <summary>
        /// Repositions the content region's cursor onto <see cref="Tree"/>'s current
        /// SelectedIndex. Call after changing the tree's selection outside the expand/collapse and
        /// Home/End paths this class syncs itself, before announcing.
        /// </summary>
        protected void SyncRegionFromCurrentTree()
        {
            if (!CursorInTreeRegion)
            {
                return;
            }
            ListModel region = Model.CurrentRegion;
            if (region != null)
            {
                SyncRegionFromTree(region);
            }
        }

        private void SyncRegionFromTree(ListModel region)
        {
            int target = CursorPrefixRowCount + Tree.SelectedIndex;
            if (target >= 0 && target < region.Count)
            {
                region.MoveTo(target);
            }
        }

        /// <summary>
        /// Moves the tree cursor to <paramref name="target"/> after a structural change,
        /// re-expanding its ancestor chain so it is actually reachable. Unlike a plain
        /// <see cref="TreeModel{T}.IndexOf"/>, this works in submenu mode, where the visible
        /// list is only the current expansion frontier: an expanded target is itself invisible
        /// there (its children take its place), so the cursor lands on its first visible
        /// descendant instead. Returns false when the target is no longer attached to the tree
        /// — a rebuilt subtree orphans its old items, and callers walk up <c>item.Parent</c>
        /// retrying with the nearest surviving ancestor. Does NOT touch the region cursor;
        /// call <see cref="SyncRegionFromCurrentTree"/> after a successful reveal.
        /// </summary>
        protected bool TryRevealAndSelect(InspectionTreeItem target)
        {
            if (target == null || Tree.Root == null)
            {
                return false;
            }

            // Orphans keep stale Parent pointers after a Children.Clear(), so attachment must be
            // checked hop by hop up to the current root.
            InspectionTreeItem cur = target;
            while (cur.Parent != null)
            {
                if (!cur.Parent.Children.Contains(cur))
                {
                    return false;
                }
                cur = cur.Parent;
            }
            if (!ReferenceEquals(cur, Tree.Root))
            {
                return false;
            }

            for (InspectionTreeItem ancestor = target.Parent; ancestor != null; ancestor = ancestor.Parent)
            {
                ancestor.IsExpanded = true;
            }

            SyncTreeSettings(CurrentPanel);
            Tree.Reflatten();

            InspectionTreeItem landing = target;
            int index = Tree.IndexOf(landing);
            while (index < 0 && landing.IsExpanded && landing.Children.Count > 0)
            {
                landing = landing.Children[0];
                index = Tree.IndexOf(landing);
            }
            if (index < 0)
            {
                return false;
            }

            Tree.SetSelectedIndex(index);
            return true;
        }

        /// <summary>The tree item under the region cursor right now, or null while the cursor rests on a prefix row.</summary>
        protected InspectionTreeItem CurrentTreeItem()
        {
            if (!CursorInTreeRegion)
            {
                return null;
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return null;
            }
            int treeIndex = region.Index - CursorPrefixRowCount;
            if (treeIndex < 0 || treeIndex >= Tree.Count)
            {
                return null;
            }
            return Tree.Visible[treeIndex];
        }

        // ------------------------------------------------------------------
        // Typeahead search-time auto-expansion. The class header covers why the restore is timed
        // off RefreshContent rather than off settle/clear.
        //
        // Tree.SuppressSubmenuHiding stays true for the whole search window (set here, cleared in
        // RestoreSearchExpansion/ClearSearchExpansionState): submenu mode otherwise hides an
        // expanded header behind its children, leaving that header permanently unmatchable and
        // unreachable by typeahead.
        // ------------------------------------------------------------------

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        protected override void OnTypeaheadWillSearch()
        {
            EnsureSearchExpansion(CurrentPanel);
        }

        private void EnsureSearchExpansion(TreePanel panel)
        {
            if (panel.SearchExpansionSnapshot != null || panel.Tree.Root == null)
            {
                return;
            }
            var snapshot = new Dictionary<InspectionTreeItem, bool>();
            bool changed = SnapshotAndExpand(panel.Tree.Root, snapshot);
            panel.SearchExpansionFrame = UnityEngine.Time.frameCount;
            // Track the snapshot even when empty, and suspend submenu hiding either way: a header
            // the user expanded before the search is just as hidden as one auto-expanded here.
            panel.SearchExpansionSnapshot = snapshot;
            panel.Tree.SuppressSubmenuHiding = true;
            if (changed || panel.Tree.SubmenuMode)
            {
                panel.Tree.Reflatten();
            }
        }

        private bool SnapshotAndExpand(InspectionTreeItem node, Dictionary<InspectionTreeItem, bool> snapshot)
        {
            bool changed = false;
            for (int i = 0; i < node.Children.Count; i++)
            {
                InspectionTreeItem child = node.Children[i];
                if (child.IsExpandable)
                {
                    snapshot[child] = child.IsExpanded;
                    if (!child.IsExpanded && ShouldAutoExpandForSearch(child))
                    {
                        OnBeforeExpandNode(child);
                        child.IsExpanded = true;
                        changed = true;
                    }
                }
                if (SnapshotAndExpand(child, snapshot))
                {
                    changed = true;
                }
            }
            return changed;
        }

        private void RestoreSearchExpansion(TreePanel panel)
        {
            Dictionary<InspectionTreeItem, bool> snapshot = panel.SearchExpansionSnapshot;
            panel.SearchExpansionSnapshot = null;
            // The search window is over even when the snapshot was empty.
            panel.Tree.SuppressSubmenuHiding = false;
            if (snapshot == null)
            {
                return;
            }
            // Anchor on the REGION cursor, not Tree.SelectedItem: plain Up/Down and the typeahead
            // match walk move only the region cursor, so the tree's own selection is stale here
            // and anchoring on it restores the pre-search row's path, silently teleporting the
            // cursor. With the cursor outside the tree's region there is no anchor at all, and the
            // plain full restore runs instead.
            InspectionTreeItem current = CursorInTreeRegion ? CurrentTreeItem() : null;
            foreach (KeyValuePair<InspectionTreeItem, bool> kv in snapshot)
            {
                kv.Key.IsExpanded = kv.Value;
            }
            // Keep the settled row reachable: re-expand its ancestor chain.
            for (InspectionTreeItem ancestor = current != null ? current.Parent : null;
                 ancestor != null;
                 ancestor = ancestor.Parent)
            {
                ancestor.IsExpanded = true;
            }
            panel.Tree.Reflatten();
            int idx = current != null ? panel.Tree.IndexOf(current) : -1;
            if (idx >= 0)
            {
                panel.Tree.SetSelectedIndex(idx);
                ListModel region = Model.CurrentRegion;
                if (region != null)
                {
                    SyncRegionFromTree(region);
                }
            }
        }

        /// <summary>
        /// Ends the search-expansion window without restoring node states, for the call sites that
        /// commit rather than undo (a rebuilt tree root, an explicit expand-all). Clearing the
        /// snapshot always turns off <see cref="TreeModel{T}.SuppressSubmenuHiding"/> with it, so
        /// no path leaves submenu hiding suspended after its search is gone.
        /// </summary>
        private void ClearSearchExpansionState(TreePanel panel)
        {
            panel.SearchExpansionSnapshot = null;
            panel.Tree.SuppressSubmenuHiding = false;
        }
    }
}
