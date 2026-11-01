using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The world-tile object selection tree, reached with Enter on a tile holding more than one
    /// inspectable object: a flat one-level tree of caravans, settlements and sites. Settlements and
    /// sites expand into their own inspection details; a player caravan's row instead opens
    /// <see cref="CaravanInspectScope"/>, the screen it gets alone on a tile. The mod owns no window
    /// here — the surface is the windowless <see cref="RimWorldAccess.WorldObjectSelectionState"/>
    /// data facade — so the scope rides the focus stack through
    /// <see cref="WorldObjectSelectionScopeMirror"/>, which keeps only the tree DATA: eligibility
    /// filter, sort order, lazy child population, activation closures.
    ///
    /// Every row is described and activated by the same generic, type-agnostic rules — dispatch on
    /// <c>Type</c>, <c>IsExpandable</c> and <c>OnActivate</c>, never on <c>item.Data</c>'s runtime
    /// type.
    /// </summary>
    public sealed class WorldObjectSelectionScope : TreeRegionScope
    {
        public WorldObjectSelectionScope()
        {
            // TrackLastChild: End-key and collapse "return to last visited child" memory.
            Tree.TrackLastChild = true;

            // TreeRegionScope's base constructor deliberately does not claim Page Up/Down, so this
            // screen registers and claims them itself. This tree never sets IsSectionBoundary, so
            // both always play the reject click.
            Claim("tree.jumpToPreviousSection", e => PerformJumpToAdjacentSection(false));
            Claim("tree.jumpToNextSection", e => PerformJumpToAdjacentSection(true));


            // No node this tree builds ever sets LinkedDef, so Alt+I always falls through to the
            // "no info card" fallback; the spoken announcement, not silence, is the right answer.
            Claim(SharedMenuGrammar.Info, e => PerformInfoCard());

            // The base's guarded claim, registered ahead of this one, clears an active search
            // first; this claim only fires once no search is active.
            Claim(SharedMenuGrammar.Cancel, e => PerformClose(), when: () => !TypeaheadHasActiveSearch);
        }

        public override string Name
        {
            get { return "world-object-selection"; }
        }

        protected override string TreeRegionLabel
        {
            get { return "RimWorldAccess.WorldObject.RegionName".Translate(); }
        }

        /// <summary>
        /// Builds the tree fresh on every genuine push, then plays the TabOpen sound and speaks the
        /// object count; the first row follows from <see cref="OnFocus"/>.
        /// </summary>
        public override void OnPush()
        {
            SetTreeRoot(WorldObjectSelectionState.BuildTreeRoot());
            base.OnPush();
            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            TolkHelper.SpeakData("RimWorldAccess.WorldObject.ObjectsAtTile".Translate(Tree.Root.Children.Count));
        }

        public override void OnFocus()
        {
            base.OnFocus();
            AnnounceCurrentItem();
        }

        protected override void OnBeforeExpandNode(InspectionTreeItem item)
        {
            WorldObjectSelectionState.BuildChildrenFor(item);
        }

        // ShouldAutoExpandForSearch and IsSectionBoundary both stay at the base's default false:
        // typeahead here matches only already-visible rows, and Page Up/Down keep the
        // claimed-but-always-reject shape.

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            var d = new ElementDescription();
            d.Label = item.Label.StripTags();

            if (item.Type == InspectionTreeItem.ItemType.DetailText)
            {
                // Read-only: Enter just re-speaks the same text (see ActivateTreeNode).
                d.ReadOnly = true;
            }
            else if (item.IsExpandable)
            {
                d.Role = ElementRole.TreeItem;
                d.Expanded = item.IsExpanded;
            }
            else if (item.OnActivate != null)
            {
                d.Role = ElementRole.Button;
            }
            else
            {
                // A leaf with no OnActivate always Enter-rejects, so read-only is the honest
                // description of its capability.
                d.ReadOnly = true;
            }

            return d;
        }

        /// <summary>
        /// Enter/Space on a tree row: DetailText rows re-speak, a node with its own
        /// <c>OnActivate</c> invokes it, and an expandable node without one toggles expand/collapse
        /// on Enter too — a deliberate divergence from <see cref="FilterTreeScopeBase"/>'s
        /// Left/Right-only convention. Anything else click-rejects.
        /// </summary>
        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            if (item.Type == InspectionTreeItem.ItemType.DetailText)
            {
                TolkHelper.SpeakData(item.Label.StripTags());
                return;
            }

            if (item.OnActivate != null)
            {
                item.OnActivate();
                return;
            }

            if (item.IsExpandable)
            {
                PerformActivateExpandToggle(item);
                return;
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.WorldObject.NoActionAvailable".Loc());
        }

        private void PerformInfoCard()
        {
            InspectionTreeItem current = CurrentTreeItem();
            if (current != null && current.LinkedDef != null)
            {
                InfoCardState.OpenInfoCardForDef(current.LinkedDef);
                return;
            }
            InfoCardState.SpeakNoInfoCardAvailable();
        }

        private void PerformClose()
        {
            WorldObjectSelectionState.Close();
            TolkHelper.Speak("RimWorldAccess.WorldObject.SelectionClosed".Loc());
        }
    }

    /// <summary>
    /// Keeps <see cref="WorldObjectSelectionScope"/> in lockstep with
    /// <see cref="RimWorldAccess.WorldObjectSelectionState.IsActive"/>. Chain-independent: the sole
    /// opener is WorldScope's own ambient claim, this screen never nests inside another migrated
    /// menu, and the single-object auto-open path never flips <c>IsActive</c> true at all.
    /// </summary>
    internal static class WorldObjectSelectionScopeMirror
    {
        private static readonly WorldObjectSelectionScope scope = new WorldObjectSelectionScope();

        public static void Reconcile()
        {
            // Real-dialog stand-down: Alt+I on a row opens the info card while the state stays
            // active, and an unconditional per-frame Push would re-float this scope above it.
            if (WorldObjectSelectionState.IsActive
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
