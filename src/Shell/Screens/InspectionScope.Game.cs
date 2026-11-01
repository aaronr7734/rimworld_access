using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>How a freshly built inspection tree is presented, carried out by <see cref="InspectionScope.OpenTree"/>.</summary>
    internal enum InspectionTreeOpening
    {
        /// <summary>Announce the focused row (several objects at the cell, or a leaf single object).</summary>
        AnnounceRow,

        /// <summary>
        /// One object: speak its bare label, lazy-load its children, expand it and land on the
        /// first child. A load yielding nothing leaves the bare label as the whole announcement.
        /// </summary>
        SingleObject,

        /// <summary><see cref="SingleObject"/>, except that a lazy load yielding nothing still falls through to the row announcement.</summary>
        SingleObjectAnnounceIfEmpty,
    }

    /// <summary>
    /// Keyboard focus scope for the main inspection tree (the Enter/'I' windowless panel: a
    /// hierarchy of every selectable object at a map cell and their categories, stats and
    /// actions). The mod owns no window here — the surface is the windowless
    /// <see cref="RimWorldAccess.WindowlessInspectionState"/> — so the scope rides the focus
    /// stack through <see cref="InspectionScopeMirror"/>. It is modal, so the shell backstop
    /// swallows any key it does not claim; the Enter/KeypadEnter that OPENS the panel at the
    /// cursor stays an ambient <see cref="MapScope"/> claim (map.inspect.open).
    ///
    /// <b>Instance reach.</b> <see cref="Live"/> is set in the constructor and never cleared,
    /// deliberately rather than in an OnPush/OnPop pair: the mirror pops this scope whenever
    /// the info card (or any window-attached dialog) opens above the tree, and the card's close
    /// path re-announces the tree row BEFORE the next mirror pass re-pushes the scope, where a
    /// push-scoped Live would be null and the return would go silent. Tree data likewise
    /// survives a pop/re-push — <see cref="OnPop"/> does not reset it, only the state's
    /// Close/Reset do.
    ///
    /// <b>Bespoke navigation.</b> Right/Left are inspection-specific (lazy-load expand with a
    /// reject-if-empty message, and a collapse that skips non-expandable parents and un-hides a
    /// submenu-hidden one), so they ride <see cref="AdjustContentItem"/> overrides rather than
    /// the base's plain expand/collapse. Space activates Action items carrying a callback
    /// exactly like Enter and otherwise re-announces. Alt+I walks the whole ancestor chain
    /// through a type ladder (gear / hediff / gene / linked def) before giving up.
    ///
    /// Typeahead matches only high-level nodes, against the FULL label rather than the
    /// shortened expanded form — see <see cref="IsSearchableNode"/>.
    ///
    /// Several menus spawn from inside this tree without closing it (IsActive stays true
    /// throughout). Their scopes reconcile AFTER this one and land above it on the stack, so
    /// they own the keyboard without this mirror standing down; see the mirror's own remarks
    /// for what it does pop for.
    /// </summary>
    public sealed class InspectionScope : TreeRegionScope
    {
        public InspectionScope()
        {
            // One instance for the session, reachable even while the mirror has this scope
            // popped for a dialog above the tree — see the class remarks.
            Live = this;

            // End-key/collapse "return to last visited child" memory.
            Tree.TrackLastChild = true;

            // TreeRegionScope's constructor deliberately leaves Page Up/Down unclaimed, so this
            // screen claims them. The tree never sets IsSectionBoundary, so both always reject.
            Claim("tree.jumpToPreviousSection", e => PerformJumpToAdjacentSection(false));
            Claim("tree.jumpToNextSection", e => PerformJumpToAdjacentSection(true));

            Claim("inspection.toggleItem", e => PerformToggleItem());
            Claim("inspection.infoCard", e => PerformInfoCard());
            Claim("inspection.delete", e => PerformDelete());

            // The base ScreenScope's guarded claim, registered ahead of this one, clears an
            // active search first; this fires only once no search is active.
            Claim(SharedMenuGrammar.Cancel, e => WindowlessInspectionState.ClosePanel(),
                when: () => !TypeaheadHasActiveSearch);
        }

        /// <summary>The session's single scope instance — see the class remarks on instance reach.</summary>
        internal static InspectionScope Live { get; private set; }

        public override string Name
        {
            get { return "inspection"; }
        }

        /// <summary>Windowless: no vanilla window will ever close this panel, so Escape belongs to this scope's own claim.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>The panel draws no window of its own, so there are no buttons to capture.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        protected override string TreeRegionLabel
        {
            get { return "RimWorldAccess.Inspection.Panel.RegionName".Translate(); }
        }

        public override void OnPush()
        {
            base.OnPush();
            // Safety net: if the state built a tree while no scope existed to receive it, seed now.
            WindowlessInspectionState.NotifyScopeAttached(this);
        }

        /// <summary>Presents a freshly built tree, opening announcement included.</summary>
        internal void OpenTree(InspectionTreeItem root, InspectionTreeOpening opening)
        {
            // This runs from the opener before the mirror's push and speaks the opening itself,
            // so the base entry announcement must stand down or the row is spoken twice. When
            // the scope is already focused (a drill from within the panel) no focus event
            // follows and the next refocus should still announce — hence the not-top gate.
            if (!ReferenceEquals(FocusStack.Top, this))
            {
                SuppressNextEntryAnnouncement();
            }
            SetTreeRoot(root);
            RefreshModel();
            SyncRegionFromCurrentTree();
            if (opening != InspectionTreeOpening.AnnounceRow && TryOpenSingleObject(opening))
            {
                return;
            }
            AnnounceCurrentRow();
        }

        /// <summary>Adopts a tree the state already holds, silently (a re-push after the tree drifted).</summary>
        internal void EnsureTree(InspectionTreeItem root)
        {
            if (ReferenceEquals(Tree.Root, root))
            {
                return;
            }
            SetTreeRoot(root);
            RefreshModel();
            SyncRegionFromCurrentTree();
        }

        /// <summary>
        /// The preserving twin of <see cref="OpenTree"/>: adopts a freshly built root, keeping
        /// expansion state and landing on the same logical node (else its nearest surviving
        /// ancestor), then announces the landing row.
        /// </summary>
        internal void ReplaceTreePreservingCursor(InspectionTreeItem newRoot)
        {
            // CurrentRegion.Index is a tree index only while the cursor sits in the tree
            // region; a foreign-region caller degrades to a null path and the clamp below.
            ListModel region = Model.CurrentRegion;
            int regionIndex = region != null && !region.IsEmpty ? region.Index : PrefixRowCount;
            if (SetTreeRootPreservingState(newRoot, regionIndex) < 0
                && Tree.SelectedIndex >= Tree.Count)
            {
                Tree.SetSelectedIndex(Tree.Count - 1);
            }
            RefreshModel();
            SyncRegionFromCurrentTree();
            AnnounceCurrentRow();
        }

        /// <summary>Drops the tree when the panel closes.</summary>
        internal void ClearTree()
        {
            ResetTree();
        }

        /// <summary>Re-speak the focused row (returning from a child menu, an external refresh).</summary>
        internal void AnnounceCurrentRow()
        {
            AnnounceCurrentItem();
        }

        /// <summary>Re-flattens silently after children changed underneath, keeping the focused item.</summary>
        internal void RefreshTreeRows()
        {
            SyncTreeFromRegion();
            InspectionTreeItem current = Tree.SelectedItem;
            Tree.Reflatten();
            if (current != null)
            {
                int newIndex = Tree.IndexOf(current);
                if (newIndex >= 0)
                {
                    Tree.SetSelectedIndex(newIndex);
                }
                else if (Tree.SelectedIndex >= Tree.Count)
                {
                    Tree.SetSelectedIndex(Tree.Count - 1);
                }
            }
            RefreshModel();
            SyncRegionFromCurrentTree();
        }

        /// <summary>
        /// The row the cursor rests on, synced from the region first — for a caller that needs it
        /// BEFORE rebuilding a category's children, to hand to <see cref="TreeStatePreserve.CaptureBranch"/>.
        /// </summary>
        internal InspectionTreeItem CurrentItemForRebuild()
        {
            SyncTreeFromRegion();
            return Tree.SelectedItem;
        }

        /// <summary>
        /// Re-flattens and lands the cursor on <paramref name="landing"/> (a
        /// <see cref="TreeStatePreserve.RestoreBranch"/> result) via <see cref="TryRevealAndSelect"/>,
        /// which also handles submenu-mode visibility. Falls back to <see cref="RefreshTreeRows"/>
        /// when there is no landing or the reveal fails. Silent — the adapter announces its own outcome.
        /// </summary>
        internal void RefreshTreeRowsLandingOn(InspectionTreeItem landing)
        {
            if (landing != null && TryRevealAndSelect(landing))
            {
                // Remembered for ActivateTreeNode's post-activate restore: its nearest-ancestor
                // walk anchors on the now-orphaned pre-rebuild item and would degrade this
                // precise landing to the category's first child.
                rebuildLanding = landing;
                RefreshModel();
                SyncRegionFromCurrentTree();
                return;
            }
            RefreshTreeRows();
        }

        /// <summary>
        /// The node the most recent preserving category rebuild landed the cursor on, valid only
        /// between <see cref="RefreshTreeRowsLandingOn"/> and the activation that triggered it.
        /// </summary>
        private InspectionTreeItem rebuildLanding;

        /// <summary>
        /// Pulls the tree's own cursor up to the region cursor. Plain Up/Down move the region
        /// model only, so anything driven from OUTSIDE the key handlers must re-anchor before
        /// reading Tree.SelectedItem or it drags the cursor back to whichever row last went
        /// through an expand/collapse.
        /// </summary>
        private void SyncTreeFromRegion()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return;
            }
            int treeIndex = region.Index - PrefixRowCount;
            if (treeIndex >= 0 && treeIndex < Tree.Count)
            {
                Tree.SetSelectedIndex(treeIndex);
            }
        }

        /// <summary>
        /// The row the region cursor rests on, as a PURE READ — the visual driver runs from the
        /// mirror's reconcile pass and must never move the selection anchor the expand/collapse
        /// paths own.
        /// </summary>
        internal InspectionTreeItem FocusedRow()
        {
            ListModel current = Model.CurrentRegion;
            if (current != null && !current.IsEmpty)
            {
                int treeIndex = current.Index - PrefixRowCount;
                if (treeIndex >= 0 && treeIndex < Tree.Count)
                {
                    return Tree.Visible[treeIndex];
                }
            }
            return null;
        }

        /// <summary>
        /// The single-object opening: speak the object's bare name (no expand state), lazy-load
        /// its children, then expand it and land on the first child. Returns false when the
        /// object is a leaf, leaving the caller to announce the row normally.
        /// </summary>
        private bool TryOpenSingleObject(InspectionTreeOpening opening)
        {
            Type landingTab = WindowlessInspectionState.ConsumeLandingTabType();
            InspectionTreeItem single = Tree.Count > 0 ? Tree.Visible[0] : null;
            if (single == null || !single.IsExpandable)
            {
                return false;
            }

            TolkHelper.SpeakData(single.Label.StripTags());
            OnBeforeExpandNode(single);
            if (single.Children.Count == 0)
            {
                return opening != InspectionTreeOpening.SingleObjectAnnounceIfEmpty;
            }

            single.IsExpanded = true;
            Tree.Reflatten();
            // The categories exist only now, so a requested landing resolves here.
            InspectionTreeItem landing =
                WindowlessInspectionState.FindCategoryForTabType(single, landingTab);
            Thing landingThing = WindowlessInspectionState.ConsumeLandingThing();
            if (landing != null)
            {
                landing = FindRowPresenting(landing, landingThing) ?? landing;
            }
            if (landing == null || !TryRevealAndSelect(landing))
            {
                int firstChild = Tree.IndexOf(single.Children[0]);
                Tree.SetSelectedIndex(firstChild >= 0 ? firstChild : 0);
            }
            RefreshModel();
            SyncRegionFromCurrentTree();
            AnnounceCurrentRow();
            return true;
        }

        internal InspectionTreeItem FindRowPresenting(InspectionTreeItem category, Thing thing)
        {
            if (category == null || thing == null)
            {
                return null;
            }
            OnBeforeExpandNode(category);
            foreach (InspectionTreeItem child in category.Children)
            {
                if (Presents(child, thing))
                {
                    return child;
                }
                if (!child.IsExpandable)
                {
                    continue;
                }
                OnBeforeExpandNode(child);
                foreach (InspectionTreeItem grandchild in child.Children)
                {
                    if (Presents(grandchild, thing))
                    {
                        return grandchild;
                    }
                }
            }
            return null;
        }

        private static bool Presents(InspectionTreeItem item, Thing thing)
        {
            return item.Data is InteractiveGearHelper.GearItem gear && ReferenceEquals(gear.Thing, thing);
        }

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            // A row that is a real control (a gizmo command) supplies its own datum, re-read live
            // on every announcement. Everything else composes from the label.
            ElementDescription d = DescribeLiveElement(item);
            if (d == null)
            {
                d = new ElementDescription();

                // The short form once expanded: the collapsed label folds the content into itself.
                string rawLabel = item.IsExpandable && item.IsExpanded && !string.IsNullOrEmpty(item.ExpandedLabel)
                    ? item.ExpandedLabel
                    : item.Label;
                d.Label = rawLabel.StripTags().TrimEnd('.', '!', '?');
            }

            if (item.IsExpandable)
            {
                d.Role = ElementRole.TreeItem;
                d.Expanded = item.IsExpanded;
            }

            // The DIRECT test only — no ancestor walk, unlike Alt+I's own ladder.
            if (HasDirectInfoCard(item))
            {
                string inspectable = "RimWorldAccess.InfoCard.Inspectable".Translate();
                d.Extras = string.IsNullOrEmpty(d.Extras) ? inspectable : d.Extras + ". " + inspectable;
            }

            // A tree holding one object hides that object's own level, so its categories are
            // level 1 rather than 2: indent level plus one, minus one again for the single root.
            d.Level = HasSingleRoot() ? Math.Max(1, item.IndentLevel) : item.IndentLevel + 1;

            return d;
        }

        /// <summary>
        /// A control row's own live datum, or null for an ordinary label-derived row. A throwing
        /// delegate is logged and falls back to the label rather than silencing the row.
        /// </summary>
        private static ElementDescription DescribeLiveElement(InspectionTreeItem item)
        {
            if (item.DescribeElement == null)
            {
                return null;
            }
            try
            {
                return item.DescribeElement();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Inspection row element description failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>True while the tree holds exactly one inspected object.</summary>
        private bool HasSingleRoot()
        {
            return Tree.Root != null && Tree.Root.Children.Count == 1;
        }

        /// <summary>
        /// Search temporarily expands Category/Object nodes (never the items themselves, so
        /// their detail children stay hidden), plus any node explicitly opted in via
        /// AutoExpandForSearch — the gear sub-categories, so weapons and items are matchable by
        /// name. The match SET is defined independently by <see cref="IsSearchableNode"/>.
        /// </summary>
        protected override bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            return item.Type == InspectionTreeItem.ItemType.Category
                || item.Type == InspectionTreeItem.ItemType.Object
                || item.AutoExpandForSearch;
        }

        protected override bool ContentRowSearchable(int region, int row)
        {
            int treeIndex = row - PrefixRowCount;
            if (treeIndex < 0 || treeIndex >= Tree.Count)
            {
                return false;
            }
            return IsSearchableNode(Tree.Visible[treeIndex]);
        }

        /// <summary>
        /// Matched against the FULL label rather than the shortened ExpandedLabel spoken for an
        /// expanded node, so a node's folded content stays searchable.
        /// </summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            int treeIndex = row - PrefixRowCount;
            if (treeIndex < 0 || treeIndex >= Tree.Count)
            {
                return "";
            }
            return Tree.Visible[treeIndex].SearchText;
        }

        /// <summary>
        /// Which tree nodes typeahead can match. High-level only:
        /// - Interactable <see cref="InspectionTreeItem.ItemType.Action"/> items always match
        ///   (Operations, Health Settings, food toggles, …).
        /// - Read-only <see cref="InspectionTreeItem.ItemType.DetailText"/> never matches
        ///   (stat/status lines fold into their parent's announcement).
        /// - Categories and Objects always match.
        /// - Other navigable nodes (SubCategory/Item) match only when they are a direct child
        ///   of a Category or Object — i.e. a body part, capacity group, need, skill, or gear
        ///   sub-category. Grandchildren and deeper (the actual stat detail) do not.
        /// </summary>
        private static bool IsSearchableNode(InspectionTreeItem node)
        {
            if (node == null) return false;
            switch (node.Type)
            {
                case InspectionTreeItem.ItemType.Action:
                    return true;
                case InspectionTreeItem.ItemType.DetailText:
                    return false;
                case InspectionTreeItem.ItemType.Category:
                case InspectionTreeItem.ItemType.Object:
                    return true;
                default:
                    // Gear items are matchable by name despite nesting under a gear sub-category.
                    if (node.Data is InteractiveGearHelper.GearItem)
                        return true;
                    InspectionTreeItem p = node.Parent;
                    return p != null
                        && (p.Type == InspectionTreeItem.ItemType.Category
                            || p.Type == InspectionTreeItem.ItemType.Object);
            }
        }

        protected override void OnBeforeExpandNode(InspectionTreeItem item)
        {
            if (item.OnActivate != null && item.Children.Count == 0)
            {
                item.OnActivate();
            }
        }

        /// <summary>
        /// Enter on a tree row. A collapsed expandable node expands exactly as Right arrow
        /// does; a node with its own action runs it and then restores the cursor onto the
        /// acted-upon item (or the nearest ancestor that survived the rebuild); anything else
        /// rejects.
        /// </summary>
        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            if (item.IsExpandable && !item.IsExpanded)
            {
                PerformRightArrow();
                return;
            }

            if (item.OnActivate != null)
            {
                rebuildLanding = null;
                item.OnActivate();

                // The action either closed this state or opened an overlay menu that owns input
                // and its own announcement; re-announcing this row would double the speech.
                if (!WindowlessInspectionState.IsActive || item.OpensOverlayMenu)
                {
                    return;
                }

                SoundDefOf.Click.PlayOneShotOnCamera();

                // Restore the cursor to the acted-upon item or the nearest surviving ancestor. A
                // flat IndexOf is not enough: in submenu mode only the current expansion frontier
                // is visible, so a surviving ancestor still needs re-revealing.
                Tree.Reflatten();
                // A preserving category rebuild inside OnActivate already landed the cursor; honor
                // that, since the walk below anchors on the item such a rebuild orphaned.
                InspectionTreeItem landed = rebuildLanding;
                rebuildLanding = null;
                bool restored = landed != null && TryRevealAndSelect(landed);
                for (InspectionTreeItem candidate = item; candidate != null && !restored; candidate = candidate.Parent)
                {
                    restored = TryRevealAndSelect(candidate);
                }
                if (!restored && Tree.SelectedIndex >= Tree.Count)
                {
                    Tree.SetSelectedIndex(Tree.Count - 1);
                }

                RefreshModel();
                SyncRegionFromCurrentTree();
                AnnounceCurrentRow();
                return;
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Inspection.Panel.NoAction".Loc());
        }

        /// <summary>
        /// Space: Action items carrying a callback behave exactly like Enter; every other row
        /// re-announces itself.
        /// </summary>
        private void PerformToggleItem()
        {
            InspectionTreeItem item = CurrentTreeItem();
            if (item != null && item.Type == InspectionTreeItem.ItemType.Action && item.OnActivate != null)
            {
                ActivateCurrent();
                return;
            }
            AnnounceCurrentRow();
        }

        /// <summary>
        /// Right/Left carry this screen's own expand and collapse rather than the base's generic
        /// pair. A row that adjusts a value in place (a gizmo's target level) takes the chord
        /// first and speaks the refreshed value itself.
        /// </summary>
        protected override void AdjustContentItem(int region, int index, int direction)
        {
            int treeIndex = index - PrefixRowCount;
            if (treeIndex < 0 || treeIndex >= Tree.Count)
            {
                base.AdjustContentItem(region, index, direction);
                return;
            }
            Tree.SetSelectedIndex(treeIndex);
            InspectionTreeItem adjustable = Tree.Visible[treeIndex];
            if (adjustable.OnAdjust != null && adjustable.OnAdjust(direction))
            {
                return;
            }
            if (direction > 0)
            {
                PerformRightArrow();
            }
            else
            {
                PerformLeftArrow();
            }
        }

        /// <summary>
        /// Right arrow, with lazy loading and an empty-after-load rejection:
        /// - leaf node: reject with "cannot expand";
        /// - already expanded: drill down to the first child (the base's own expand path);
        /// - collapsed: build children on demand, reject with "no items to show" when the load
        ///   produces none, else expand.
        /// </summary>
        private void PerformRightArrow()
        {
            TypeaheadReset();

            InspectionTreeItem item = Tree.SelectedItem;
            if (item == null)
            {
                return;
            }

            if (!item.IsExpandable)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Inspection.Panel.CannotExpand".Loc(), SpeechPriority.High);
                return;
            }

            if (item.IsExpanded)
            {
                ExpandThroughBase();
                return;
            }

            OnBeforeExpandNode(item);
            if (item.Children.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Inspection.Panel.NoItemsToShow".Loc());
                return;
            }

            // Submenu mode drops the expanded parent from the visible list, so the unchanged
            // cursor index lands on its first child; standard mode keeps the cursor on the parent.
            item.IsExpanded = true;
            Tree.Reflatten();
            SoundDefOf.FloatMenu_Open.PlayOneShotOnCamera();
            SyncRegionFromCurrentTree();
            AnnounceCurrentRow();
        }

        /// <summary>The base's plain expand, for the already-expanded drill-into-first-child case.</summary>
        private void ExpandThroughBase()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return;
            }
            base.AdjustContentItem(Model.RegionIndex, region.Index, 1);
        }

        /// <summary>
        /// Left arrow, with this screen's parent-skipping collapse:
        /// - expanded node: collapse it, focus stays;
        /// - anything else: move to the nearest EXPANDABLE ancestor, skipping non-expandable
        ///   parents; when that ancestor is hidden (submenu mode hides expanded parents),
        ///   collapse it first so it becomes visible;
        /// - no such ancestor: reject at the top level.
        /// </summary>
        private void PerformLeftArrow()
        {
            TypeaheadReset();

            InspectionTreeItem item = Tree.SelectedItem;
            if (item == null)
            {
                return;
            }

            if (item.IsExpandable && item.IsExpanded)
            {
                item.IsExpanded = false;
                Tree.Reflatten();
                if (Tree.SelectedIndex >= Tree.Count)
                {
                    Tree.SetSelectedIndex(Tree.Count - 1);
                }
                SoundDefOf.FloatMenu_Cancel.PlayOneShotOnCamera();
                SyncRegionFromCurrentTree();
                AnnounceCurrentRow();
                return;
            }

            InspectionTreeItem parent = item.Parent;
            while (parent != null && !parent.IsExpandable)
            {
                parent = parent.Parent;
            }

            if (parent == null)
            {
                SpeakTopLevelEdge();
                return;
            }

            int parentIndex = Tree.IndexOf(parent);
            if (parentIndex >= 0)
            {
                Tree.SetSelectedIndex(parentIndex);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                SyncRegionFromCurrentTree();
                AnnounceCurrentRow();
                return;
            }

            parent.IsExpanded = false;
            Tree.Reflatten();
            parentIndex = Tree.IndexOf(parent);
            if (parentIndex < 0)
            {
                SpeakTopLevelEdge();
                return;
            }

            Tree.SetSelectedIndex(parentIndex);
            SoundDefOf.FloatMenu_Cancel.PlayOneShotOnCamera();
            SyncRegionFromCurrentTree();
            AnnounceCurrentRow();
        }

        private static void SpeakTopLevelEdge()
        {
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.TopLevel, SpeechPriority.High);
        }

        private void PerformDelete()
        {
            InspectionTreeItem item = CurrentTreeItem();
            if (item != null && item.OnDelete != null)
            {
                item.OnDelete();
            }
        }

        /// <summary>
        /// Alt+I: open the info card for the focused item, else for the nearest ancestor that
        /// has one, else say none is available.
        /// </summary>
        private void PerformInfoCard()
        {
            InspectionTreeItem item = CurrentTreeItem();
            if (item != null)
            {
                if (TryOpenInfoCardForItem(item))
                {
                    return;
                }
                for (InspectionTreeItem parent = item.Parent; parent != null; parent = parent.Parent)
                {
                    if (TryOpenInfoCardForItem(parent))
                    {
                        return;
                    }
                }
            }
            InfoCardState.SpeakNoInfoCardAvailable();
        }

        /// <summary>
        /// Whether the item has a DIRECT info card (custom info action, gear, hediff, gene, or
        /// linked def). Only matches types the game natively offers info card buttons for.
        /// </summary>
        private static bool HasDirectInfoCard(InspectionTreeItem item)
        {
            if (item.OnInfo != null) return true;
            if (item.Data is InteractiveGearHelper.GearItem gi && gi.Thing != null) return true;
            if (item.Data is Hediff) return true;
            if (item.Data is Gene || item.Data is GeneDef) return true;
            if (item.LinkedDef != null) return true;
            return false;
        }

        private static bool TryOpenInfoCardForItem(InspectionTreeItem item)
        {
            if (item.OnInfo != null)
            {
                item.OnInfo();
                return true;
            }

            if (item.Data is InteractiveGearHelper.GearItem gearItem && gearItem.Thing != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(gearItem.Thing));
                return true;
            }

            if (item.Data is Hediff hediff)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(hediff));
                return true;
            }

            if (item.Data is Gene gene)
            {
                InfoCardState.OpenInfoCardForDef(gene.def);
                return true;
            }
            if (item.Data is GeneDef geneDef)
            {
                InfoCardState.OpenInfoCardForDef(geneDef);
                return true;
            }

            if (item.LinkedDef != null)
            {
                InfoCardState.OpenInfoCardForDef(item.LinkedDef);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Keeps <see cref="InspectionScope"/> in lockstep with
    /// <see cref="WindowlessInspectionState.IsActive"/>, reconciled every OnGUI pass immediately
    /// before <see cref="GizmoScopeMirror"/> (see that mirror for the Ctrl+Alt+Enter coexistence
    /// this ordering exists for). The scope keeps its tree across a pop/re-push here.
    ///
    /// The stand-downs are the info card, any foreign input-owning window, and any main tab other
    /// than Inspect: a main tab window is neither absorbing nor close-on-outside, so the foreign
    /// test cannot see one, and this mirror would otherwise re-float above the scope such a tab
    /// attached at Add time. The tree keeps its rows and resumes when that tab closes. Every other
    /// screen that spawns from inside this tree while IsActive stays true — the inspect-component
    /// scopes, the health/prisoner/entity/fishing tabs, the bills/filter/storage family — has its
    /// own mirror reconciling AFTER this one, so stack order alone gives it the keyboard. Pure
    /// TextInputManager sessions (zone/pen rename) and float menus are covered by the
    /// dispatcher's blanket LegacyKeyboardOverlayActive stand-down.
    /// </summary>
    internal static class InspectionScopeMirror
    {
        private static readonly InspectionScope scope = new InspectionScope();

        public static void Reconcile()
        {
            // Real-dialog stand-down for anything opened from an inspection Action row; the
            // predicate lives in ShellGuards so every mirror with the same exposure shares it.
            if (WindowlessInspectionState.IsActive
                && !InfoCardState.IsActive
                && !ShellGuards.NonInspectMainTabOpen()
                && !ShellGuards.ForeignDialogWindowAbove())
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }
}
