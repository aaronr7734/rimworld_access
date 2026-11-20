using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard scope for the fishing zone settings menu. The mod owns no window here — the surface
    /// is the windowless <see cref="FishingZoneMenuState"/> — so the scope rides the focus stack
    /// through <see cref="FishingZoneScopeMirror"/>.
    ///
    /// It sits on <see cref="TreeRegionScope"/> because the panel is one region of grouped rows
    /// ending in a genuinely expandable Fish species branch, which the flat content contract cannot
    /// express. Navigation, typeahead, the entry announcement and the expand/collapse grammar come
    /// from the chassis; the state keeps the zone data, the row tree and every mutation.
    ///
    /// Fish children load on first expand (<see cref="OnBeforeExpandNode"/>), so a menu the player
    /// never expands never sweeps the water body's fish lists.
    ///
    /// Left/Right ride the chassis; the By10/By100/By1000 steps, the two value jumps and Alt+I keep
    /// their own ids and chords.
    ///
    /// Enter on a count row opens a real <see cref="TextFieldEditSession"/> — digits only, the value
    /// clamped by the state's vanilla-harvested bounds on confirm. The dispatcher's modal
    /// text-session branch routes every key to that session, so no claim here needs a browse gate.
    /// </summary>
    public sealed class FishingZoneScope : TreeRegionScope
    {
        /// <summary>
        /// The one mirror-held instance, so <see cref="FishingZoneMenuState"/>'s statics can present
        /// a tree and re-announce a row. Set at construction and never cleared, because the openers
        /// run inside a Harmony patch on the inspect pane's pass, which can land either side of the
        /// mirror's reconcile.
        /// </summary>
        internal static FishingZoneScope Live { get; private set; }

        private readonly TextFieldEditSession numericEdit = new TextFieldEditSession();

        public FishingZoneScope()
        {
            Live = this;

            Claim("fishingZone.infoCard", e => FishingZoneMenuState.OpenFishInfoCard(CurrentTreeItem()));

            // Plain Left/Right (step 1) ride the base's own adjust path instead.
            Claim("fishingZone.increaseBy10", e => AdjustFocusedValue(1, 10));
            Claim("fishingZone.decreaseBy10", e => AdjustFocusedValue(-1, 10));
            Claim("fishingZone.increaseBy100", e => AdjustFocusedValue(1, 100));
            Claim("fishingZone.decreaseBy100", e => AdjustFocusedValue(-1, 100));
            Claim("fishingZone.increaseBy1000", e => AdjustFocusedValue(1, 1000));
            Claim("fishingZone.decreaseBy1000", e => AdjustFocusedValue(-1, 1000));

            // These jump the focused field's VALUE; plain and Ctrl Home/End jump the menu cursor.
            Claim("fishingZone.jumpToMin", e => JumpFocusedValue(true));
            Claim("fishingZone.jumpToMax", e => JumpFocusedValue(false));

            // The base's typeahead-gated Cancel claim, registered first, clears an active search;
            // reaching this one means no search is live, so it always closes.
            Claim(SharedMenuGrammar.Cancel, e => CloseMenu(), when: () => !TypeaheadHasActiveSearch);

            RegisterPopTeardown(numericEdit.CancelIfActive);
        }

        public override string Name
        {
            get { return "fishing-zone"; }
        }

        /// <summary>No mod-owned <c>Window</c> exists for this inspect-pane overlay — self-claim Escape.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>A windowless overlay has no captured buttons and no declared actions.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        /// <summary>Vanilla's own fishing tab label — no key of our own.</summary>
        protected override string TreeRegionLabel
        {
            get { return (string)"TabFishing".Translate(); }
        }

        /// <summary>
        /// The row under the cursor, for the focus ring. Runs inside vanilla's own tab draw, so it
        /// reads the model as-is and never refreshes it.
        /// </summary>
        internal InspectionTreeItem FocusedRow
        {
            get { return CurrentTreeItem(); }
        }

        /// <summary>
        /// Builds the tree if the state opened before this scope existed, the <see cref="Live"/>
        /// handoff's one gap. Guarded on an absent root, so the pop and re-push around an open info
        /// card leaves the player's row untouched.
        /// </summary>
        public override void OnPush()
        {
            base.OnPush();
            if (Tree.Root == null)
            {
                SetTreeRoot(FishingZoneMenuState.BuildTree());
            }
        }

        /// <summary>Presents a freshly built tree; the chassis speaks the entry announcement on focus.</summary>
        internal void OpenTree(InspectionTreeItem root)
        {
            TypeaheadReset();
            SetTreeRoot(root);
        }

        internal void ClearTree()
        {
            TypeaheadReset();
            ResetTree();
        }

        /// <summary>
        /// Swaps in a rebuilt tree after a change that adds or removes rows, keeping the cursor on
        /// the same row NUMBER and re-announcing whatever row that now is.
        /// </summary>
        internal void RebuildTreePreservingIndex(InspectionTreeItem root)
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            int treeIndex = region != null ? region.Index - PrefixRowCount : 0;
            SetTreeRoot(root, treeIndex > 0 ? treeIndex : 0);
            RefreshModel();
            SyncRegionFromCurrentTree();
            AnnounceCurrentItem();
        }

        /// <summary>Lazy child population: the Fish species branch builds its rows on first expand.</summary>
        protected override void OnBeforeExpandNode(InspectionTreeItem item)
        {
            if (item.IsExpandable && item.Children.Count == 0)
            {
                FishingZoneMenuState.LoadFishChildren(item);
            }
        }

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            return FishingZoneMenuState.DescribeItem(item);
        }

        /// <summary>
        /// A count row opens exact numeric entry, the Fish species branch toggles, and every other
        /// row runs its own action.
        /// </summary>
        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            if (FishingZoneMenuState.RowTakesTypedNumber(item))
            {
                BeginNumericEdit(item);
                return;
            }
            if (item.IsExpandable)
            {
                PerformActivateExpandToggle(item);
                return;
            }
            FishingZoneMenuState.ExecuteRow(item);
        }

        protected override bool TryGetSliderBox(int region, int index, out float value, out bool high)
        {
            high = false;
            value = 0f;
            return region == TreeRegionIndex && FishingZoneMenuState.TryGetSliderValue(TreeRowAt(index), out value);
        }

        /// <summary>A value row steps by one; every other row keeps the base's expand/collapse.</summary>
        protected override void AdjustContentItem(int region, int index, int direction)
        {
            InspectionTreeItem item = TreeRowAt(index);
            if (FishingZoneMenuState.RowAdjustsValue(item))
            {
                FishingZoneMenuState.AdjustValue(item, direction);
                return;
            }
            base.AdjustContentItem(region, index, direction);
        }

        private void AdjustFocusedValue(int direction, int multiplier)
        {
            RefreshModel();
            InspectionTreeItem item = CurrentTreeItem();
            if (!FishingZoneMenuState.RowAdjustsValue(item))
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Fishing.Action.NotAdjustable".Loc(), SpeechPriority.High);
                return;
            }
            FishingZoneMenuState.AdjustValue(item, direction, multiplier);
        }

        private void JumpFocusedValue(bool toMinimum)
        {
            RefreshModel();
            InspectionTreeItem item = CurrentTreeItem();
            if (item == null)
            {
                return;
            }
            if (toMinimum)
            {
                FishingZoneMenuState.JumpToMin(item);
            }
            else
            {
                FishingZoneMenuState.JumpToMax(item);
            }
        }

        /// <summary>
        /// Exact numeric entry for one count row: digits only, matching what
        /// Listing_Standard.IntEntry accepts, with the value bound left to the state's own
        /// vanilla-harvested clamp on confirm. Escape keeps the row untouched, and either exit
        /// re-announces the row, which is why nothing on the apply path speaks.
        /// </summary>
        private void BeginNumericEdit(InspectionTreeItem item)
        {
            ElementDescription row = FishingZoneMenuState.DescribeItem(item);
            numericEdit.EnterEdit(
                FishingZoneMenuState.TypedNumberSeed(item),
                TextFieldSpec.WholeNumber("RimWorldAccess.TextInput.LabelDefault"),
                row != null ? row.Label : "",
                value => ApplyTypedNumber(item, value),
                onExit: ReAnnounceRow);
        }

        private static void ApplyTypedNumber(InspectionTreeItem item, string value)
        {
            if (int.TryParse(value, out int typed))
            {
                FishingZoneMenuState.ApplyTypedNumber(item, typed);
            }
        }

        private void ReAnnounceRow()
        {
            RefreshModel();
            AnnounceCurrentItem();
        }

        /// <summary>The tree row at a content-region index, or null when the index is off the tree.</summary>
        private InspectionTreeItem TreeRowAt(int index)
        {
            int treeIndex = index - PrefixRowCount;
            return treeIndex >= 0 && treeIndex < Tree.Count ? Tree.Visible[treeIndex] : null;
        }

        private void CloseMenu()
        {
            FishingZoneMenuState.Close();
            InspectionReturnHelper.AnnounceParentOrFallback(
                "RimWorldAccess.Inspection.Patch.ClosedFishingZoneMenu".Translate());
        }
    }

    /// <summary>
    /// Keeps <see cref="FishingZoneScope"/> in lockstep with
    /// <see cref="FishingZoneMenuState.IsActive"/>, reconciled every OnGUI pass by the shell
    /// dispatcher. It additionally stands down while an info card is open over the menu, so the card
    /// owns the keyboard and the menu's AnyLiveModal contribution stops shadowing the info-card
    /// handler; the scope keeps its tree and cursor across that pop and re-push.
    /// </summary>
    internal static class FishingZoneScopeMirror
    {
        private static readonly FishingZoneScope scope = new FishingZoneScope();

        public static void Reconcile()
        {
            if (FishingZoneMenuState.IsActive && !InfoCardState.IsActive)
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
