using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard focus scope for the caravan inspection tree (I or Enter on a selected caravan on
    /// the world map): a category tree of caravan status, pawns, gear and items. The mod owns no
    /// window here — the surface is the windowless <see cref="CaravanInspectState"/> data facade —
    /// so the scope rides the focus stack through <see cref="CaravanInspectScopeMirror"/>. It is
    /// modal, so the shell backstop swallows any key it does not claim.
    ///
    /// <b>Instance reach.</b> <see cref="Live"/> is set in the constructor and never cleared. That
    /// is what lets <see cref="CaravanInspectState.Open"/> hand a freshly built tree over
    /// synchronously — the mirror does not push until the reconcile pass after Open returns, so
    /// the caravan name and the first row would otherwise be spoken a frame apart from the tree
    /// they describe — and what keeps <see cref="CaravanInspectState.RefreshTree"/> reaching the
    /// model while an overlay sits above this tree. The tree survives a pop/re-push; only Close
    /// drops it.
    ///
    /// <b>Refresh before every key.</b> A change detected mid-flow (an abandon confirmed inside a
    /// dialog) must be picked up before the next key is processed, so
    /// <see cref="RefreshContent"/> carries the check and the claims that do not otherwise touch
    /// the model (the five pawn readouts, Alt+I, Delete, Escape) call <c>RefreshModel()</c>
    /// themselves; no key is exempt.
    ///
    /// Navigation, Enter, Backspace and typeahead are the shared tree grammar. Page Up/Down are
    /// claimed here (the base claims neither id) and always reject: this tree flags no section
    /// boundary. Bespoke: Alt+M/N/H/G/K (mood/needs/health/gear/top-skills for the focused pawn),
    /// Alt+I, Delete (abandon), Escape (close).
    ///
    /// Alt+I is screen-specific: it opens <see cref="StatBreakdownState"/> for a stat row's
    /// tooltip, a real info card for a pawn/thing row, or the item's own action. Delete is
    /// load-bearing — it abandons the focused pawn or item.
    ///
    /// StatBreakdownState and GearEquipMenuState need no coexistence term in this mirror's gate:
    /// each has a live modal scope reconciled AFTER it, so their later Push re-floats them above.
    ///
    /// No close hook exists for an inspected caravan destroyed, merged or disbanded while this
    /// screen is open — the rebuild's same-index fallback is the only defense against a stale tree.
    /// </summary>
    public sealed class CaravanInspectScope : TreeRegionScope
    {
        private bool refreshingContent;

        public CaravanInspectScope()
        {
            // One instance for the session, reachable whether or not this scope currently sits on
            // the focus stack — see the class remarks.
            Live = this;

            // End-key/collapse "return to last visited child" memory.
            Tree.TrackLastChild = true;

            Claim("caravanInspect.showMood", e => PerformPawnReadout(CaravanInspectState.ShowPawnMood));
            Claim("caravanInspect.showNeeds", e => PerformPawnReadout(CaravanInspectState.ShowPawnNeeds));
            Claim("caravanInspect.showHealth", e => PerformPawnReadout(CaravanInspectState.ShowPawnHealth));
            Claim("caravanInspect.showGear", e => PerformPawnReadout(CaravanInspectState.ShowPawnGear));
            Claim("caravanInspect.showSkills", e => PerformPawnReadout(CaravanInspectState.ShowPawnSkills));

            // The base constructor deliberately claims neither id, so this screen claims them.
            // The tree never sets IsSectionBoundary, so both always play the reject click.
            Claim("tree.jumpToPreviousSection", e => PerformJumpToAdjacentSection(false));
            Claim("tree.jumpToNextSection", e => PerformJumpToAdjacentSection(true));

            Claim(SharedMenuGrammar.Info, e => PerformInfo());
            Claim("caravanInspect.abandonItem", e => PerformDelete());

            // The base's typeahead-gated Cancel claim, registered first, clears an active search;
            // this one fires only once no search is active. Close() speaks its own announcement.
            Claim(SharedMenuGrammar.Cancel, e => PerformClose(), when: () => !TypeaheadHasActiveSearch);

            twinMount = new TreeRegionTwinMount(this, () => CaravanInspectState.CurrentCaravan?.Name ?? TreeRegionLabel);
        }

        /// <summary>The visible body: the tree twin draws this screen's rows while it is the top scope.</summary>
        private readonly ITreeTwinMount twinMount;

        /// <summary>The session's single scope instance — see the class remarks on instance reach.</summary>
        internal static CaravanInspectScope Live { get; private set; }

        public override string Name
        {
            get { return "caravan-inspect"; }
        }

        /// <summary>Windowless: no vanilla window will ever close this tree, so Escape is this scope's.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>The tree draws no window of its own, so there are no buttons to capture.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        protected override string TreeRegionLabel
        {
            get { return "RimWorldAccess.Caravan.Inspect.RegionName".Translate(); }
        }

        // ShouldAutoExpandForSearch stays false: typeahead here matches only already-visible rows.
        // IsSectionBoundary stays false — see the Page Up/Down claims.

        public override void OnPush()
        {
            base.OnPush();
            // Safety net: if the state built a tree while no scope existed to receive it, adopt
            // it now, silently.
            InspectionTreeItem pending = CaravanInspectState.TakePendingRoot();
            if (pending != null)
            {
                SetTreeRoot(pending);
                RefreshModel();
                SyncRegionFromCurrentTree();
            }
            TreeTwinWindow.Mount(twinMount);
        }

        public override void OnPop()
        {
            base.OnPop();
            TreeTwinWindow.Unmount(twinMount);
        }

        /// <summary>Presents a freshly opened caravan's tree and speaks its focused row.</summary>
        internal void OpenTree(InspectionTreeItem root)
        {
            SetTreeRoot(root);
            RefreshModel();
            SyncRegionFromCurrentTree();
            if (Tree.Count > 0)
            {
                AnnounceCurrentItem();
            }
        }

        /// <summary>Drops the tree when the screen closes.</summary>
        internal void ClearTree()
        {
            ResetTree();
        }

        /// <summary>
        /// Rebuilds the tree after the caravan's contents changed, keeping the focused row:
        /// matched by data object, then by label within the same parent, then by index. Expansion
        /// state carries over by node path, since the rebuild replaces every node object. Silent.
        /// </summary>
        internal void RebuildTree()
        {
            // Plain Up/Down move the region cursor only, so the tree cursor has to be
            // re-anchored before it is read as the row to restore.
            SyncTreeFromRegion();

            InspectionTreeItem oldItem = Tree.SelectedItem;
            object oldData = oldItem != null ? oldItem.Data : null;
            string oldLabel = oldItem != null ? oldItem.Label : null;
            string oldParentLabel = oldItem != null && oldItem.Parent != null ? oldItem.Parent.Label : null;
            int oldIndex = Tree.SelectedIndex;

            // Only VISIBLE expandable nodes are snapshotted, so in submenu mode an expanded
            // parent — hidden by definition there — comes back collapsed.
            var expansionStates = new Dictionary<string, bool>();
            for (int i = 0; i < Tree.Count; i++)
            {
                InspectionTreeItem item = Tree.Visible[i];
                if (item.IsExpandable)
                {
                    expansionStates[NodePath(item)] = item.IsExpanded;
                }
            }

            SetTreeRoot(CaravanInspectState.BuildTreeRoot());

            foreach (InspectionTreeItem item in AllNodes(Tree.Root))
            {
                bool wasExpanded;
                if (item.IsExpandable && expansionStates.TryGetValue(NodePath(item), out wasExpanded))
                {
                    item.IsExpanded = wasExpanded;
                }
            }
            Tree.Reflatten();

            RestoreCursor(oldData, oldLabel, oldParentLabel, oldIndex);
            ReanchorRegionToTree();
        }

        private void RestoreCursor(object oldData, string oldLabel, string oldParentLabel, int oldIndex)
        {
            if (oldData != null)
            {
                for (int i = 0; i < Tree.Count; i++)
                {
                    if (Tree.Visible[i].Data == oldData)
                    {
                        Tree.SetSelectedIndex(i);
                        return;
                    }
                }
            }

            if (!string.IsNullOrEmpty(oldLabel) && !string.IsNullOrEmpty(oldParentLabel))
            {
                for (int i = 0; i < Tree.Count; i++)
                {
                    InspectionTreeItem item = Tree.Visible[i];
                    if (item.Label == oldLabel && item.Parent != null && item.Parent.Label == oldParentLabel)
                    {
                        Tree.SetSelectedIndex(i);
                        return;
                    }
                }
            }

            // The focused item is gone: stay at the same position, or on the new last row.
            Tree.SetSelectedIndex(Math.Min(oldIndex, Tree.Count - 1));
        }

        /// <summary>
        /// Puts the region cursor back on the tree cursor after a structural rebuild. The row
        /// count is pushed into the region first because a rebuild can run from inside
        /// <see cref="RefreshContent"/>, i.e. before RefreshModel re-declares the regions —
        /// without it the cursor could not reach a row past the old end of the list.
        /// </summary>
        private void ReanchorRegionToTree()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null)
            {
                return;
            }
            region.SetCount(PrefixRowCount + Tree.Count);
            SyncRegionFromCurrentTree();
        }

        /// <summary>
        /// Pulls the tree's own cursor up to the region cursor. Plain Up/Down move the region
        /// model only, so anything reading <c>Tree.SelectedItem</c> from outside the
        /// expand/collapse and Home/End paths has to re-anchor first.
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

        /// <summary>Unique path string for a node, so expansion state survives a rebuild.</summary>
        private static string NodePath(InspectionTreeItem node)
        {
            var parts = new List<string>();
            for (InspectionTreeItem current = node; current != null && current.IndentLevel >= 0; current = current.Parent)
            {
                parts.Insert(0, current.Label ?? "?");
            }
            return string.Join("/", parts.ToArray());
        }

        private static IEnumerable<InspectionTreeItem> AllNodes(InspectionTreeItem node)
        {
            if (node == null) yield break;

            yield return node;

            for (int i = 0; i < node.Children.Count; i++)
            {
                foreach (InspectionTreeItem descendant in AllNodes(node.Children[i]))
                {
                    yield return descendant;
                }
            }
        }

        /// <summary>
        /// The refresh-before-every-key check. Guarded: a detected change rebuilds the tree from
        /// in here, and that rebuild must not re-enter the refresh it was started from.
        /// </summary>
        protected override void RefreshContent()
        {
            base.RefreshContent();
            if (refreshingContent)
            {
                return;
            }
            refreshingContent = true;
            try
            {
                CaravanInspectState.CheckForChangesAndRefresh();
            }
            finally
            {
                refreshingContent = false;
            }
        }

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            var d = new ElementDescription();
            d.Label = item.Label.StripTags();
            if (item.IsExpandable)
            {
                d.Role = ElementRole.TreeItem;
                d.Expanded = item.IsExpanded;
            }
            // Stat rows carry their value inside the label already (AddStatNode), and their
            // breakdown tooltip is Alt+I's business, not the focus announcement's.
            return d;
        }

        /// <summary>
        /// Enter on a tree row: a stat row re-speaks itself, a node with an action runs it, an
        /// expandable node toggles, and a leaf with no action rejects.
        /// </summary>
        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            // Stat rows are never expandable, so the expand branches below cannot shadow this.
            if (!item.IsExpandable && item.Data is CaravanInspectState.StatNodeData)
            {
                TolkHelper.SpeakData(item.Label);
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
            TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.NoActionAvailable".Loc());
        }

        private void PerformPawnReadout(Action<InspectionTreeItem> readout)
        {
            RefreshModel();
            readout(CurrentTreeItem());
        }

        private void PerformInfo()
        {
            RefreshModel();
            InspectionTreeItem item = CurrentTreeItem();
            if (item == null)
            {
                return;
            }
            CaravanInspectState.ShowInfoFor(item);
        }

        private void PerformDelete()
        {
            RefreshModel();
            InspectionTreeItem item = CurrentTreeItem();
            if (item == null)
            {
                return;
            }
            if (item.OnDelete != null)
            {
                item.OnDelete();
                return;
            }
            TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.CannotAbandon".Loc());
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        private void PerformClose()
        {
            RefreshModel();
            CaravanInspectState.Close();
        }
    }

    /// <summary>
    /// Keeps <see cref="CaravanInspectScope"/> in lockstep with
    /// <see cref="CaravanInspectState.IsActive"/>, standing down while WindowlessInspectionState
    /// overlays it — that state's own scope must win the keyboard over this tree.
    ///
    /// Reconcile order is load-bearing: InspectionScopeMirror runs BEFORE this one, which is what
    /// the gate below relies on, and the caravan overlay family (StatBreakdown, QuantityMenu,
    /// SliderDialog) must reconcile AFTER it so their own Push re-floats them above. The scope
    /// keeps its tree across a pop/re-push here.
    /// </summary>
    internal static class CaravanInspectScopeMirror
    {
        private static readonly CaravanInspectScope scope = new CaravanInspectScope();

        public static void Reconcile()
        {
            // A per-frame push would re-float this scope above the abandon confirmation.
            if (CaravanInspectState.IsActive
                && !WindowlessInspectionState.IsActive
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
