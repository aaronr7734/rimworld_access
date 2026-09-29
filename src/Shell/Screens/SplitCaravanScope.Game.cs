using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard focus scope for <see cref="Dialog_SplitCaravan"/>, registered through
    /// <see cref="ScopeForWindow"/>. Simpler than <see cref="CaravanFormationScope"/>: no
    /// destination sub-mode, no auto-provision, and the dialog never leaves the WindowStack, so
    /// <see cref="SplitCaravanState"/> is a thin bridge.
    ///
    /// Regions: Pawns, Items, Travel supplies and Summary, plus the automatic Buttons region,
    /// shown as flat lists in the classic view and as tables in the table view (see
    /// <see cref="TransferScreenScope"/>). The classic Summary reads one caravan at a time, the
    /// new one first, and Left/Right flip it to the other. In the table view the first three are full TABLE regions — identity/count column plus the shared
    /// <see cref="TransferableTableColumns"/> data columns driven by the dialog's own widget
    /// draw flags. Summary is a 3-column stat table (name, new caravan, original caravan),
    /// mirroring the two stat panels vanilla itself draws side by side. Enter on a stat row
    /// explains the FOCUSED caravan's column; Enter on the identity column defaults to the new
    /// caravan's explanation.
    ///
    /// The Buttons region is DECLARED, not captured (<see cref="CaptureWindowButtons"/> false),
    /// in vanilla's own draw order (Accept, Reset, Cancel) using the game's translation keys.
    /// <c>EnableSortChord</c> is false because Alt+S is this screen's Split chord and the shared
    /// sort chord would shadow it inside a table region; Enter on the header row still sorts.
    ///
    /// <see cref="OwnsCancel"/>/<see cref="OwnsAccept"/> stay at ScreenScope's defaults. Escape
    /// stays vanilla's (the dialog never touches closeOnCancel); this scope claims it only to
    /// clear an active search. The dialog does not override OnAcceptKeyPressed, so
    /// CaravanFormationPatch's general Window prefix blocks Enter whenever this scope is live —
    /// no reentrancy bypass is needed, because <see cref="Split"/> reflects straight into
    /// TrySplitCaravan.
    ///
    /// Vehicle Framework compat: VF forces this dialog to its Vehicles tab even with zero
    /// vehicles in the split, so <see cref="SyncGameTab"/> and <see cref="OnFocus"/> pin the
    /// sighted tab back to whichever vanilla tab the cursor is on. There is no vehicles region
    /// here, but a pawn split out of a vehicle caravan can still be seat-assigned, so pawn-row
    /// mutations gate on <see cref="PawnLockedAnnounce"/>.
    /// </summary>
    public sealed class SplitCaravanScope : TransferScreenScope
    {
        private const int PawnsRegion = 0;
        private const int ItemsRegion = 1;
        private const int SuppliesRegion = 2;
        private const int SummaryRegion = 3;
        private const int SummaryStatCount = 5;

        private readonly Dialog_SplitCaravan dialog;

        /// <summary>
        /// Pawns-region rows, including read-only section-header rows (vanilla's own
        /// Colonists/Slaves/Prisoners/Capture/Animals/Mechs/Entities titles) -- see
        /// <see cref="CaravanUIHelper.GetPawnSectionRows"/>. Items/Supplies have no vanilla
        /// section headers (each is built from a single untitled AddSection call) so they stay
        /// plain <see cref="TransferableOneWay"/> lists.
        /// </summary>
        private readonly List<CaravanUIHelper.PawnSectionRow> pawnRows = new List<CaravanUIHelper.PawnSectionRow>();
        private readonly List<TransferableOneWay> itemRows = new List<TransferableOneWay>();
        private readonly List<TransferableOneWay> supplyRows = new List<TransferableOneWay>();
        private List<CaravanUIHelper.PawnSectionRow> pawnDefaultOrder;
        private List<TransferableOneWay> itemDefaultOrder;
        private List<TransferableOneWay> supplyDefaultOrder;
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private bool announcedOpen;

        /// <summary>Which caravan the classic Summary is reading; the new one until Left/Right flips it.</summary>
        private bool summaryShowsOriginal;
        private bool announceSummaryCaravan;

        public SplitCaravanScope(Dialog_SplitCaravan dialog)
            : this(dialog, OpensClassic, swappedIn: false)
        {
        }

        private SplitCaravanScope(Dialog_SplitCaravan dialog, bool classic, bool swappedIn)
            : base(classic, swappedIn)
        {
            this.dialog = dialog;

            if (classic)
            {
                Claim(SharedMenuGrammar.NextHorizontal, delegate { FlipSummaryCaravan(); }, when: () => InSummaryRegion);
                Claim(SharedMenuGrammar.PreviousHorizontal, delegate { FlipSummaryCaravan(); }, when: () => InSummaryRegion);
            }

            Claim("splitCaravan.addMaxOrSelectAll", delegate { HandleShiftEnter(); }, when: NotSummaryRegion);
            Claim("splitCaravan.removeItem", delegate { HandleDeleteItem(); }, when: NotSummaryRegion);
            Claim("splitCaravan.inspect", delegate { InspectOrBreakdown(); });
            Claim("splitCaravan.split", delegate { Split(); });
            Claim("splitCaravan.reset", delegate { Reset(); });
            Claim("splitCaravan.showHealth", delegate { ShowHealthInfo(); });
            Claim("splitCaravan.showMood", delegate { ShowMoodInfo(); });
            Claim("splitCaravan.showNeeds", delegate { ShowNeedsInfo(); });
            Claim("splitCaravan.showGear", delegate { ShowGearInfo(); });
            Claim("splitCaravan.showSkills", delegate { ShowSkillsInfo(); });

            Claim("splitCaravan.increment", delegate { AdjustQuantityBy(1); }, when: AllowQuantityShortcuts);
            Claim("splitCaravan.decrement", delegate { AdjustQuantityBy(-1); }, when: AllowQuantityShortcuts);
        }

        public override string Name
        {
            get { return "split-caravan"; }
        }

        /// <summary>
        /// Always live while attached. Gating on <c>!WindowlessInspectionState.IsActive</c>
        /// deadlocks the scope when the dialog opens while the inspection tree is up.
        /// </summary>
        public override bool IsLive
        {
            get { return true; }
        }

        /// <summary>Shared cross-region typeahead; Summary's stat rows stay out of the search space.</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        internal bool HasActiveTypeahead
        {
            get { return TypeaheadHasActiveSearch; }
        }

        /// <summary>Alt+S is this screen's Split chord — the shared sort chord must not shadow it. Enter on the header row still sorts.</summary>
        protected override bool EnableSortChord
        {
            get { return false; }
        }

        protected override int ContentRegionCount
        {
            get { return 4; }
        }

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case PawnsRegion: return "PawnsTab".Translate().ToString();
                case ItemsRegion: return "ItemsTab".Translate().ToString();
                case SuppliesRegion: return "TravelSupplies".Translate().ToString();
                default: return "RimWorldAccess.Caravan.Split.SummaryRegionName".Translate().ToString();
            }
        }

        protected override int ContentItemCount(int region)
        {
            switch (region)
            {
                case PawnsRegion: return pawnRows.Count;
                case ItemsRegion: return itemRows.Count;
                case SuppliesRegion: return supplyRows.Count;
                default: return SummaryStatCount;
            }
        }

        /// <summary>Table region column counts: identity/count column (1) plus the shared data columns; Summary is a fixed 3-column (name, new caravan, original caravan) stat table.</summary>
        protected override int TableColumnCount(int region)
        {
            if (dialog == null)
                return 0;
            switch (region)
            {
                case PawnsRegion:
                case ItemsRegion:
                case SuppliesRegion:
                    return 1 + TransferableTableColumns.ColumnCount(WidgetViewFor(region).Profile);
                case SummaryRegion:
                    return 3;
                default:
                    return 0;
            }
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            if (region == SummaryRegion)
            {
                switch (column)
                {
                    case 0: return new TableColumnInfo("RimWorldAccess.Common.NameColumn".Translate().ToString(), null, false);
                    case 1: return new TableColumnInfo("RimWorldAccess.Caravan.Split.NewCaravanLabel".Translate().ToString(), null, false);
                    default: return new TableColumnInfo("RimWorldAccess.Caravan.Split.OriginalCaravanLabel".Translate().ToString(), null, false);
                }
            }
            if (column == 0)
                return TransferableTableColumns.IdentityColumnInfo("SplitCaravanThingCountTip".Translate().ToString());
            TransferableTableColumns.WidgetView view = WidgetViewFor(region);
            return TransferableTableColumns.ColumnInfo(TransferableTableColumns.KindAt(view.Profile, column - 1), isPawnColumn: region == PawnsRegion);
        }

        /// <summary>
        /// Populates the row sets once so an active sort survives repeated refreshes instead of
        /// being re-filtered back to filter order every pass; <see cref="Reset"/> clears the
        /// caches after recalculating the dialog's own transferables.
        /// </summary>
        protected override void RefreshContent()
        {
            if (dialog == null)
                return;
            if (Classic)
            {
                List<TransferableOneWay> all = GetTransferables();
                pawnRows.Clear();
                foreach (TransferableOneWay t in ClassicListRows(ListWidget(PawnsRegion), CaravanUIHelper.GetPawnSectionRows(all, null)))
                    pawnRows.Add(new CaravanUIHelper.PawnSectionRow(t));
                itemRows.Clear();
                itemRows.AddRange(ClassicListRows(ListWidget(ItemsRegion),
                    CaravanUIHelper.FilterByCategory(all, CaravanUIHelper.TransferableCategory.Items).Select(t => new CaravanUIHelper.PawnSectionRow(t))));
                supplyRows.Clear();
                supplyRows.AddRange(ClassicListRows(ListWidget(SuppliesRegion),
                    CaravanUIHelper.FilterByCategory(all, CaravanUIHelper.TransferableCategory.FoodAndMedicine).Select(t => new CaravanUIHelper.PawnSectionRow(t))));
            }
            else if (pawnRows.Count == 0 && itemRows.Count == 0 && supplyRows.Count == 0)
            {
                List<TransferableOneWay> all = GetTransferables();
                TransferableOneWayWidget pawnsWidget = pawnsTransferField?.GetValue(dialog) as TransferableOneWayWidget;
                pawnRows.AddRange(CaravanUIHelper.GetPawnSectionRows(all, pawnsWidget));
                itemRows.AddRange(CaravanUIHelper.FilterByCategory(all, CaravanUIHelper.TransferableCategory.Items));
                supplyRows.AddRange(CaravanUIHelper.FilterByCategory(all, CaravanUIHelper.TransferableCategory.FoodAndMedicine));
                pawnDefaultOrder = new List<CaravanUIHelper.PawnSectionRow>(pawnRows);
                itemDefaultOrder = new List<TransferableOneWay>(itemRows);
                supplyDefaultOrder = new List<TransferableOneWay>(supplyRows);
            }
        }

        protected override ElementDescription DescribeTransferItem(int region, int index)
        {
            ElementDescription d = new ElementDescription();
            if (region == SummaryRegion)
            {
                d.Label = Classic ? GetStatValueText(index, summaryShowsOriginal) : StatShortName(index);
                return d;
            }
            if (region == PawnsRegion)
            {
                if (index < 0 || index >= pawnRows.Count)
                    return d;
                CaravanUIHelper.PawnSectionRow row = pawnRows[index];
                if (row.IsHeader)
                {
                    d.Label = row.Header;
                    d.Role = ElementRole.None;
                    d.ReadOnly = true;
                    return d;
                }
                d.Label = CaravanAnnouncementHelper.BuildItemAnnouncement(row.Transferable, index, pawnRows.Count, includePosition: false);
                // Vehicle Framework draws a seated pawn's checkbox read-only.
                d.ReadOnly = CaravanVehicleTab.Active && row.Transferable.AnyThing is Pawn seated
                    && CaravanVehicleTab.Provider.IsPawnSeatLocked(seated, out _);
                return d;
            }
            List<TransferableOneWay> rows = RowsFor(region);
            if (index >= 0 && index < rows.Count)
            {
                d.Label = CaravanAnnouncementHelper.BuildItemAnnouncement(rows[index], index, rows.Count, includePosition: false);
            }
            return d;
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            if (region == SummaryRegion)
            {
                if (column == 0)
                    return StatShortName(row);
                return GetStatValueText(row, isSource: column == 2);
            }
            if (region == PawnsRegion)
            {
                if (row < 0 || row >= pawnRows.Count)
                    return "";
                CaravanUIHelper.PawnSectionRow pawnRow = pawnRows[row];
                if (pawnRow.IsHeader)
                    return column == 0 ? pawnRow.Header : "";
                if (column == 0)
                    return CaravanAnnouncementHelper.BuildItemAnnouncement(pawnRow.Transferable, row, pawnRows.Count, includePosition: false);
                TransferableTableColumns.WidgetView pawnsView = WidgetViewFor(region);
                return TransferableTableColumns.CellText(TransferableTableColumns.KindAt(pawnsView.Profile, column - 1), pawnRow.Transferable, pawnsView);
            }
            List<TransferableOneWay> rows = RowsFor(region);
            if (row < 0 || row >= rows.Count)
                return "";
            TransferableOneWay transferable = rows[row];
            if (column == 0)
                return CaravanAnnouncementHelper.BuildItemAnnouncement(transferable, row, rows.Count, includePosition: false);
            TransferableTableColumns.WidgetView view = WidgetViewFor(region);
            return TransferableTableColumns.CellText(TransferableTableColumns.KindAt(view.Profile, column - 1), transferable, view);
        }

        protected override string ContentCellTip(int region, int row, int column)
        {
            if (region == SummaryRegion || column == 0)
                return null;
            if (region == PawnsRegion)
            {
                if (row < 0 || row >= pawnRows.Count || pawnRows[row].IsHeader)
                    return null;
                TransferableTableColumns.WidgetView pawnsView = WidgetViewFor(region);
                return TransferableTableColumns.CellTip(TransferableTableColumns.KindAt(pawnsView.Profile, column - 1), pawnRows[row].Transferable, pawnsView);
            }
            List<TransferableOneWay> rows = RowsFor(region);
            if (row < 0 || row >= rows.Count)
                return null;
            TransferableTableColumns.WidgetView view = WidgetViewFor(region);
            return TransferableTableColumns.CellTip(TransferableTableColumns.KindAt(view.Profile, column - 1), rows[row], view);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            TypeaheadReset();

            if (region == SummaryRegion)
            {
                // Row default (ActivateContentCell declined): the caravan the classic Summary reads, else the new one.
                OpenStatBreakdown(index, isSource: Classic && summaryShowsOriginal);
                return;
            }

            if (region == PawnsRegion)
            {
                if (index < 0 || index >= pawnRows.Count)
                    return;
                CaravanUIHelper.PawnSectionRow row = pawnRows[index];
                if (row.IsHeader)
                {
                    AnnounceCurrentItem();
                    return;
                }
                TogglePawnSelection(row.Transferable);
                return;
            }

            List<TransferableOneWay> rows = RowsFor(region);
            if (index < 0 || index >= rows.Count)
                return;
            OpenQuantityMenu(rows[index]);
        }

        /// <summary>Summary's New/Original caravan value columns are individually actionable: Enter explains the FOCUSED caravan's breakdown, not always the new one.</summary>
        protected override bool ActivateContentCell(int region, int row, int column)
        {
            if (region != SummaryRegion || column == 0)
                return false;
            TypeaheadReset();
            OpenStatBreakdown(row, isSource: column == 2);
            return true;
        }

        /// <summary>
        /// Re-orders a Pawns/Items/Supplies row set using the shared TransferableTableColumns
        /// comparers; the identity column sorts by vanilla's TransferableComparer_Name. Summary
        /// is not sortable.
        /// </summary>
        protected override int ApplyContentSort(int region, int column, SortCycleResult cycle, int currentRow)
        {
            if (region == SummaryRegion)
                return -1;

            if (region == PawnsRegion)
                return ApplyPawnSort(column, cycle, currentRow);

            List<TransferableOneWay> rows = RowsFor(region);
            List<TransferableOneWay> defaultOrder = region == ItemsRegion ? itemDefaultOrder : supplyDefaultOrder;
            TransferableOneWay current = currentRow >= 0 && currentRow < rows.Count ? rows[currentRow] : null;

            List<TransferableOneWay> reordered;
            if (cycle == SortCycleResult.Cleared)
            {
                reordered = new List<TransferableOneWay>(defaultOrder ?? rows);
            }
            else
            {
                bool descending = cycle == SortCycleResult.SortedDescending;
                if (column == 0)
                {
                    reordered = TransferableTableColumns.SortByIdentity(rows, descending);
                }
                else
                {
                    TransferableTableColumns.WidgetView view = WidgetViewFor(region);
                    reordered = TransferableTableColumns.SortByColumn(rows, TransferableTableColumns.KindAt(view.Profile, column - 1), descending, view);
                }
            }

            rows.Clear();
            rows.AddRange(reordered);
            if (current == null)
                return 0;
            int idx = rows.IndexOf(current);
            return idx >= 0 ? idx : 0;
        }

        /// <summary>
        /// Sorts the Pawns table one section at a time, mirroring vanilla's per-section cache
        /// sort: header rows stay fixed as boundaries and only the pawns within each section are
        /// reordered among themselves.
        /// </summary>
        private int ApplyPawnSort(int column, SortCycleResult cycle, int currentRow)
        {
            TransferableOneWay current = currentRow >= 0 && currentRow < pawnRows.Count
                ? pawnRows[currentRow].Transferable
                : null;

            List<CaravanUIHelper.PawnSectionRow> source = cycle == SortCycleResult.Cleared
                ? new List<CaravanUIHelper.PawnSectionRow>(pawnDefaultOrder ?? pawnRows)
                : new List<CaravanUIHelper.PawnSectionRow>(pawnRows);

            var result = new List<CaravanUIHelper.PawnSectionRow>();
            if (cycle == SortCycleResult.Cleared)
            {
                result.AddRange(source);
            }
            else
            {
                bool descending = cycle == SortCycleResult.SortedDescending;
                TransferableTableColumns.WidgetView view = WidgetViewFor(PawnsRegion);
                List<TransferableOneWay> bucket = new List<TransferableOneWay>();

                foreach (CaravanUIHelper.PawnSectionRow row in source)
                {
                    if (row.IsHeader)
                    {
                        FlushPawnSortBucket(bucket, result, column, descending, view);
                        result.Add(row);
                    }
                    else
                    {
                        bucket.Add(row.Transferable);
                    }
                }
                FlushPawnSortBucket(bucket, result, column, descending, view);
            }

            pawnRows.Clear();
            pawnRows.AddRange(result);
            if (current == null)
                return 0;
            int idx = pawnRows.FindIndex(r => !r.IsHeader && r.Transferable == current);
            return idx >= 0 ? idx : 0;
        }

        /// <summary>Sorts one section's accumulated pawns and appends them to <paramref name="result"/>, then clears the scratch bucket.</summary>
        private static void FlushPawnSortBucket(List<TransferableOneWay> bucket, List<CaravanUIHelper.PawnSectionRow> result, int column, bool descending, TransferableTableColumns.WidgetView view)
        {
            if (bucket.Count == 0)
                return;
            List<TransferableOneWay> sorted = column == 0
                ? TransferableTableColumns.SortByIdentity(bucket, descending)
                : TransferableTableColumns.SortByColumn(bucket, TransferableTableColumns.KindAt(view.Profile, column - 1), descending, view);
            foreach (TransferableOneWay t in sorted)
                result.Add(new CaravanUIHelper.PawnSectionRow(t));
            bucket.Clear();
        }

        protected override void OnRegionChanged(MoveResult result)
        {
            int region = Model.RegionIndex;
            // Region indexes 0/1/2 match the dialog's own tab values; Summary and Buttons have none.
            if (dialog != null && region < SummaryRegion)
            {
                SyncGameTab(region);
            }
        }

        /// <summary>False: the transferable tables' own column-sort buttons would pollute the region.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        /// <summary>Shift+Enter presses Accept from anywhere on this screen: the one-chord proceed.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "splitCaravan.split"; }
        }

        /// <summary>The dialog's three bottom buttons, in vanilla's own draw order.</summary>
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("AcceptButton".Translate().ToString(), Split, "splitCaravan.split"));
                actions.Add(new ScreenAction("ResetButton".Translate().ToString(), Reset, "splitCaravan.reset"));
                actions.Add(new ScreenAction("CancelButton".Translate().ToString(), CloseDialog, SharedMenuGrammar.Cancel));
                AddSwapAction(actions);
                return actions;
            }
        }

        /// <summary>The Cancel button: close exactly as vanilla's own Cancel does, so PostClose announces the cancellation.</summary>
        private void CloseDialog()
        {
            OwnedWindow?.Close();
        }

        public override void OnPush()
        {
            base.OnPush();
            SplitCaravanState.NotifyScopeAttached(this);
            TransferableRingRequest.CurrentProvider = () => CurrentTransferableForQuantity();
        }

        public override void OnPop()
        {
            TransferableRingRequest.CurrentProvider = null;
            SplitCaravanState.NotifyScopeDetached(this);
            base.OnPop();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen || dialog == null)
                return;
            announcedOpen = true;
            string tabCount = Classic || SwappedIn ? null : TabCountFragment();
            string opening = SwappedIn
                ? SwappedInAnnouncement()
                : "RimWorldAccess.Caravan.Split.OpenInstructions".Translate().ToString();
            TolkHelper.SpeakData(string.IsNullOrEmpty(tabCount) ? opening : opening + ". " + tabCount);

            if (CaravanVehicleTab.Active)
            {
                // VF forces selectedTab=10 (Vehicles) on open even with zero vehicles in the
                // split; sync the sighted tab back to Pawns, where the cursor actually starts.
                CaravanVehicleTab.Provider.SyncTab(false, 0);
            }

            // Lands on the table's header row, row 1 of every table.
            AnnounceCurrentItem();
        }

        private bool NotSummaryRegion()
        {
            return Model.RegionIndex != SummaryRegion;
        }

        protected override int ListRegionCount
        {
            get { return SummaryRegion; }
        }

        protected override bool HasSummaryRegion
        {
            get { return true; }
        }

        protected override TransferableOneWay ListRowAt(int region, int row)
        {
            if (region == PawnsRegion)
                return CurrentPawnRowTransferable(row);
            if (region == ItemsRegion || region == SuppliesRegion)
                return IndexedTransferable(RowsFor(region), row);
            return null;
        }

        protected override TransferableOneWayWidget ListWidget(int region)
        {
            if (dialog == null)
                return null;
            System.Reflection.FieldInfo field = region == PawnsRegion ? pawnsTransferField
                : region == ItemsRegion ? itemsTransferField
                : region == SuppliesRegion ? foodAndMedicineTransferField
                : null;
            return field?.GetValue(dialog) as TransferableOneWayWidget;
        }

        protected override TransferableTableColumns.WidgetView ListView(int region)
        {
            return region < SummaryRegion ? WidgetViewFor(region) : null;
        }

        protected override TransferScreenScope CreateOtherView()
        {
            return new SplitCaravanScope(dialog, !Classic, swappedIn: true);
        }

        protected override bool CanStepQuantity()
        {
            return AllowQuantityShortcuts();
        }

        protected override void StepQuantity(int delta)
        {
            AdjustQuantityBy(delta);
        }

        protected override void SetQuantityExtreme(bool max)
        {
            if (Model.RegionIndex == PawnsRegion && PawnLockedAnnounce(CurrentTransferableForQuantity()))
                return;
            if (max)
                TransferableQuantityHelper.SetToMax(CurrentTransferableForQuantity, NotifyTransferablesChanged);
            else
                TransferableQuantityHelper.SetToZero(CurrentTransferableForQuantity, NotifyTransferablesChanged);
        }

        /// <summary>Classic Summary Left/Right: the same stat for the other caravan, named once.</summary>
        private void FlipSummaryCaravan()
        {
            summaryShowsOriginal = !summaryShowsOriginal;
            announceSummaryCaravan = true;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }

        protected override string ContentRegionEntryDetail(int region)
        {
            return Classic && region == SummaryRegion ? CaravanLabel(summaryShowsOriginal) : null;
        }

        protected override string AnnouncePrefix(int region, int index)
        {
            if (region == SummaryRegion && announceSummaryCaravan)
            {
                announceSummaryCaravan = false;
                return CaravanLabel(summaryShowsOriginal);
            }
            return base.AnnouncePrefix(region, index);
        }

        /// <summary>Items/Supplies row lists only — Pawns has its own header-aware row type, read via <see cref="CurrentPawnRowTransferable"/>.</summary>
        private List<TransferableOneWay> RowsFor(int region)
        {
            return region == ItemsRegion ? itemRows : supplyRows;
        }

        /// <summary>The transferable at a Pawns-region row index, or null for a section-header row or an out-of-range index.</summary>
        private TransferableOneWay CurrentPawnRowTransferable(int index)
        {
            return index >= 0 && index < pawnRows.Count ? pawnRows[index].Transferable : null;
        }

        private static TransferableOneWay IndexedTransferable(List<TransferableOneWay> rows, int index)
        {
            return index >= 0 && index < rows.Count ? rows[index] : null;
        }

        /// <summary>True (and speaks the reason) when this pawn transferable is locked to a vehicle seat, matching VF's own readOnly gate; false when the caller's mutation may proceed.</summary>
        private bool PawnLockedAnnounce(TransferableOneWay t)
        {
            if (!CaravanVehicleTab.Active || t == null)
                return false;
            if (!(t.AnyThing is Pawn pawn))
                return false;
            if (CaravanVehicleTab.Provider.IsPawnSeatLocked(pawn, out string note))
            {
                // Already-resolved text from the provider, not a key — SpeakData, not Speak.
                TolkHelper.SpeakData(note, SpeechPriority.High);
                return true;
            }
            return false;
        }

        private void TogglePawnSelection(TransferableOneWay transferable)
        {
            if (PawnLockedAnnounce(transferable))
                return;
            if (transferable.MaxCount > 1)
            {
                OpenQuantityMenu(transferable);
                return;
            }
            CaravanUIHelper.TogglePawnSelection(transferable, NotifyTransferablesChanged);
        }

        private void OpenQuantityMenu(TransferableOneWay transferable)
        {
            QuantityMenuState.Open(transferable, delegate (int newQuantity)
            {
                if (!transferable.CanAdjustTo(newQuantity).Accepted)
                    return;
                transferable.AdjustTo(newQuantity);
                NotifyTransferablesChanged();
            });
        }

        private void HandleShiftEnter()
        {
            TypeaheadReset();
            int region = Model.RegionIndex;
            int index = CurrentContentRow();

            if (region == PawnsRegion)
            {
                TransferableOneWay transferable = CurrentPawnRowTransferable(index);
                if (transferable == null)
                    return;
                if (PawnLockedAnnounce(transferable))
                    return;
                CaravanQuantityHelper.SelectAllPawns(transferable, NotifyTransferablesChanged, AnnounceCurrentItem);
                return;
            }

            if (region == ItemsRegion || region == SuppliesRegion)
            {
                TransferableOneWay transferable = IndexedTransferable(RowsFor(region), index);
                if (transferable == null)
                    return;
                float remainingCapacity = GetDestRemainingCapacity();
                var result = CaravanQuantityHelper.CalculateMaxToAdd(transferable, remainingCapacity);
                CaravanQuantityHelper.ApplyMaxAdd(transferable, result, NotifyTransferablesChanged);
            }
        }

        private void HandleDeleteItem()
        {
            TypeaheadReset();
            int region = Model.RegionIndex;
            int index = CurrentContentRow();
            bool isPawnTab = region == PawnsRegion;

            TransferableOneWay transferable = isPawnTab
                ? CurrentPawnRowTransferable(index)
                : IndexedTransferable(RowsFor(region), index);
            if (transferable == null)
                return;

            if (isPawnTab && PawnLockedAnnounce(transferable))
                return;

            CaravanInputHelper.HandleDeleteKey(transferable, isPawnTab, isSuppliesTabLocked: false, NotifyTransferablesChanged);
        }

        private void InspectOrBreakdown()
        {
            if (Model.RegionIndex == SummaryRegion)
            {
                RefreshModel();
                TableModel table = Model.CurrentTable;
                bool isSource = table == null ? summaryShowsOriginal : table.ColumnIndex == 2;
                OpenStatBreakdown(CurrentContentRow(), isSource);
                return;
            }

            Thing thing = ListRowAt(Model.RegionIndex, CurrentContentRow())?.AnyThing;
            if (thing != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(thing));
            }
        }

        private void OpenStatBreakdown(int statIndex, bool isSource)
        {
            var statInfo = GetStatExplanation(statIndex, isSource);
            if (statInfo.HasValue)
            {
                StatBreakdownState.Open(statInfo.Value.name, statInfo.Value.explanation);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Split.NoBreakdownForStat".Loc());
            }
        }

        private void ShowHealthInfo() => CaravanInputHelper.HandlePawnInfoShortcuts(KeyCode.H, SelectedPawn(), true, false, false);
        private void ShowMoodInfo() => CaravanInputHelper.HandlePawnInfoShortcuts(KeyCode.M, SelectedPawn(), true, false, false);
        private void ShowNeedsInfo() => CaravanInputHelper.HandlePawnInfoShortcuts(KeyCode.N, SelectedPawn(), true, false, false);
        private void ShowGearInfo() => CaravanInputHelper.HandlePawnInfoShortcuts(KeyCode.G, SelectedPawn(), true, false, false);
        private void ShowSkillsInfo() => CaravanInputHelper.HandlePawnInfoShortcuts(KeyCode.K, SelectedPawn(), true, false, false);

        /// <summary>
        /// Null while viewing Summary: a stats view has no "current pawn".
        /// </summary>
        private Pawn SelectedPawn()
        {
            if (Model.RegionIndex == SummaryRegion)
                return null;
            int index = CurrentContentRow();
            if (Model.RegionIndex == PawnsRegion)
                return CurrentPawnRowTransferable(index)?.AnyThing as Pawn;
            return CaravanUIHelper.GetSelectedPawn(RowsFor(Model.RegionIndex), index);
        }

        private void Split()
        {
            if (dialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Split.NoDialogAvailable".Loc(), SpeechPriority.High);
                return;
            }

            try
            {
                var method = AccessTools.Method(typeof(Dialog_SplitCaravan), "TrySplitCaravan");
                if (method != null)
                {
                    SplitCaravanState.NoteSplitAttempted();
                    bool success = (bool)method.Invoke(dialog, null);
                    if (success)
                    {
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                        dialog.Close(doCloseSound: false);
                        TolkHelper.Speak("RimWorldAccess.Caravan.Split.SplitSuccessfully".Loc());
                    }
                    else
                    {
                        // Validation failed; reset so a later plain Escape still says "cancelled".
                        SplitCaravanState.ResetSplitAttempted();
                    }
                }
            }
            catch (Exception ex)
            {
                SplitCaravanState.ResetSplitAttempted();
                TolkHelper.Speak("RimWorldAccess.Caravan.Split.FailedToSplit".Loc(ex.Message), SpeechPriority.High);
                Log.Error($"RimWorld Access: Failed to split caravan: {ex.Message}");
            }
        }

        private void Reset()
        {
            if (dialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Split.NoDialogAvailable".Loc(), SpeechPriority.High);
                return;
            }

            try
            {
                var method = AccessTools.Method(typeof(Dialog_SplitCaravan), "CalculateAndRecacheTransferables");
                if (method != null)
                {
                    method.Invoke(dialog, null);
                    // The dialog recomputed its transferables, so the cached row lists are stale.
                    pawnRows.Clear();
                    itemRows.Clear();
                    supplyRows.Clear();
                    pawnDefaultOrder = null;
                    itemDefaultOrder = null;
                    supplyDefaultOrder = null;
                    TolkHelper.Speak("RimWorldAccess.Caravan.Split.SelectionsReset".Loc());
                    RefreshModel();
                    Model.CurrentRegion?.MoveFirst();
                    AnnounceCurrentItem();
                }
            }
            catch (Exception ex)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Split.FailedReset".Loc(ex.Message), SpeechPriority.High);
                Log.Error($"RimWorld Access: Failed to reset split caravan: {ex.Message}");
            }
        }

        // Escape: Dialog_SplitCaravan always has closeOnCancel=true, so vanilla closes it;
        // Escape-clears-search is the base ScreenScope typeahead claim.

        /// <summary>True when the inline quantity shortcuts should be claimed: a transferable region, and on Pawns only for a stackable row.</summary>
        private bool AllowQuantityShortcuts()
        {
            RefreshModel();
            int region = Model.RegionIndex;
            if (region != PawnsRegion && region != ItemsRegion && region != SuppliesRegion)
                return false;
            if (region != PawnsRegion)
                return true;

            TransferableOneWay transferable = CurrentTransferableForQuantity();
            return transferable != null && transferable.MaxCount > 1;
        }

        private void AdjustQuantityBy(int delta)
        {
            if (Model.RegionIndex == PawnsRegion && PawnLockedAnnounce(CurrentTransferableForQuantity()))
                return;
            TransferableQuantityHelper.AdjustQuantity(CurrentTransferableForQuantity, delta, NotifyTransferablesChanged);
        }

        /// <summary>The transferable under the row cursor, or null when the cursor sits on the table's own column-header row, a Pawns section-header row, or outside a transferable region.</summary>
        private TransferableOneWay CurrentTransferableForQuantity()
        {
            int currentRegion = Model.RegionIndex;
            if (currentRegion != PawnsRegion && currentRegion != ItemsRegion && currentRegion != SuppliesRegion)
                return null;
            int index = CurrentContentRow();
            if (currentRegion == PawnsRegion)
                return CurrentPawnRowTransferable(index);
            return IndexedTransferable(RowsFor(currentRegion), index);
        }

        private List<TransferableOneWay> GetTransferables()
        {
            try
            {
                var field = AccessTools.Field(typeof(Dialog_SplitCaravan), "transferables");
                if (field != null && field.GetValue(dialog) is List<TransferableOneWay> list)
                {
                    return list;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"RimWorld Access: Failed to get transferables from Dialog_SplitCaravan: {ex.Message}");
            }
            return new List<TransferableOneWay>();
        }

        private void NotifyTransferablesChanged()
        {
            if (dialog == null)
                return;
            try
            {
                var method = AccessTools.Method(typeof(Dialog_SplitCaravan), "CountToTransferChanged");
                method?.Invoke(dialog, null);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to call CountToTransferChanged: {ex.Message}");
            }
        }

        private void SyncGameTab(int region)
        {
            try
            {
                var tabField = AccessTools.Field(typeof(Dialog_SplitCaravan), "tab");
                tabField?.SetValue(dialog, region);
                if (CaravanVehicleTab.Active)
                {
                    // Split sessions never have a vehicles region of their own -- keep VF's
                    // own selectedTab pinned to this dialog's vanilla tab so it doesn't
                    // silently snap back to Vehicles on the next VF draw pass.
                    CaravanVehicleTab.Provider.SyncTab(false, region);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to sync game tab: {ex.Message}");
            }
        }

        /// <summary>Remaining mass capacity for the destination caravan; Shift+Enter adds the max that fits.</summary>
        private float GetDestRemainingCapacity()
        {
            if (dialog == null)
                return 0f;
            var massUsageInfo = AccessTools.Property(typeof(Dialog_SplitCaravan), "DestMassUsage");
            var massCapacityInfo = AccessTools.Property(typeof(Dialog_SplitCaravan), "DestMassCapacity");
            if (massUsageInfo != null && massCapacityInfo != null)
            {
                float massUsage = (float)massUsageInfo.GetValue(dialog);
                float massCapacity = (float)massCapacityInfo.GetValue(dialog);
                return massCapacity - massUsage;
            }
            return 0f;
        }

        /// <summary>The current map's tile, for days-until-rot estimates; null when no map is current. Reflection fallback only — the live widget carries the caravan's own tile.</summary>
        private PlanetTile? CurrentTile()
        {
            Map map = Find.CurrentMap;
            return map != null ? (PlanetTile?)map.Tile : null;
        }

        // The column set is read off the dialog's own live TransferableOneWayWidget per tab, so a
        // mod changing the widget's draw flags changes our table too. The ColumnProfile presets
        // are only the reflection fallback.
        private static readonly System.Reflection.FieldInfo pawnsTransferField =
            HarmonyLib.AccessTools.Field(typeof(Dialog_SplitCaravan), "pawnsTransfer");
        private static readonly System.Reflection.FieldInfo itemsTransferField =
            HarmonyLib.AccessTools.Field(typeof(Dialog_SplitCaravan), "itemsTransfer");
        private static readonly System.Reflection.FieldInfo foodAndMedicineTransferField =
            HarmonyLib.AccessTools.Field(typeof(Dialog_SplitCaravan), "foodAndMedicineTransfer");

        private TransferableTableColumns.WidgetView WidgetViewFor(int region)
        {
            System.Reflection.FieldInfo field = region == PawnsRegion ? pawnsTransferField
                : region == ItemsRegion ? itemsTransferField
                : foodAndMedicineTransferField;
            TransferableTableColumns.ColumnProfile fallback = region == PawnsRegion
                ? TransferableTableColumns.ColumnProfile.CaravanPawns
                : TransferableTableColumns.ColumnProfile.CaravanItems;
            return TransferableTableColumns.ViewFor(
                dialog != null ? field?.GetValue(dialog) as TransferableOneWayWidget : null,
                fallback, CurrentTile());
        }

        /// <summary>Summary stat table Name-column text (0=Mass, 1=Speed, 2=Food, 3=Foraging, 4=Visibility) — same for both caravan columns.</summary>
        private static string StatShortName(int statIndex)
        {
            switch (statIndex)
            {
                case 0: return "RimWorldAccess.Caravan.Inspect.StatMass".Translate();
                case 1: return "RimWorldAccess.Caravan.Inspect.StatSpeed".Translate();
                case 2: return "RimWorldAccess.Caravan.Inspect.StatFood".Translate();
                case 3: return "RimWorldAccess.Caravan.Inspect.StatForaging".Translate();
                case 4: return "RimWorldAccess.Caravan.Inspect.StatVisibility".Translate();
                default: return "";
            }
        }

        /// <summary>The value string for a summary stat (0=Mass, 1=Speed, 2=Food, 3=Foraging, 4=Visibility) for the given caravan (new/destination or original/source).</summary>
        private string GetStatValueText(int statIndex, bool isSource)
        {
            if (dialog == null)
                return (string)"RimWorldAccess.Caravan.Split.UnknownStatValue".Translate();

            try
            {
                switch (statIndex)
                {
                    case 0: return GetMassStatValue(isSource);
                    case 1: return GetSpeedStatValue(isSource);
                    case 2: return GetFoodStatValue(isSource);
                    case 3: return GetForagingStatValue(isSource);
                    case 4: return GetVisibilityStatValue(isSource);
                    default: return "Unknown";
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to get stat value: {ex.Message}");
                return "Unavailable";
            }
        }

        private string GetMassStatValue(bool isSource)
        {
            string massUsageProp = isSource ? "SourceMassUsage" : "DestMassUsage";
            string massCapacityProp = isSource ? "SourceMassCapacity" : "DestMassCapacity";
            var massUsageInfo = AccessTools.Property(typeof(Dialog_SplitCaravan), massUsageProp);
            var massCapacityInfo = AccessTools.Property(typeof(Dialog_SplitCaravan), massCapacityProp);
            if (massUsageInfo != null && massCapacityInfo != null)
            {
                float massUsage = (float)massUsageInfo.GetValue(dialog);
                float massCapacity = (float)massCapacityInfo.GetValue(dialog);
                return CaravanStatFormatter.FormatMass(massUsage, massCapacity);
            }
            return (string)"RimWorldAccess.Stat.MassUnknown".Translate();
        }

        private string GetSpeedStatValue(bool isSource)
        {
            string tilesPerDayProp = isSource ? "SourceTilesPerDay" : "DestTilesPerDay";
            string massUsageProp = isSource ? "SourceMassUsage" : "DestMassUsage";
            string massCapacityProp = isSource ? "SourceMassCapacity" : "DestMassCapacity";
            var tilesInfo = AccessTools.Property(typeof(Dialog_SplitCaravan), tilesPerDayProp);
            var massUsageInfo = AccessTools.Property(typeof(Dialog_SplitCaravan), massUsageProp);
            var massCapacityInfo = AccessTools.Property(typeof(Dialog_SplitCaravan), massCapacityProp);
            if (tilesInfo != null)
            {
                float tilesPerDay = (float)tilesInfo.GetValue(dialog);
                bool isOverloaded = false;
                if (massUsageInfo != null && massCapacityInfo != null)
                {
                    float massUsage = (float)massUsageInfo.GetValue(dialog);
                    float massCapacity = (float)massCapacityInfo.GetValue(dialog);
                    isOverloaded = massUsage > massCapacity;
                }
                return CaravanStatFormatter.FormatSpeed(tilesPerDay, isOverloaded);
            }
            return (string)"RimWorldAccess.Stat.SpeedUnknown".Translate();
        }

        private string GetFoodStatValue(bool isSource)
        {
            string daysWorthProp = isSource ? "SourceDaysWorthOfFood" : "DestDaysWorthOfFood";
            var foodInfo = AccessTools.Property(typeof(Dialog_SplitCaravan), daysWorthProp);
            if (foodInfo != null)
            {
                var food = (ValueTuple<float, float>)foodInfo.GetValue(dialog);
                return CaravanStatFormatter.FormatFood(food.Item1, food.Item2);
            }
            return (string)"RimWorldAccess.Stat.FoodUnknown".Translate();
        }

        private string GetForagingStatValue(bool isSource)
        {
            string forageProp = isSource ? "SourceForagedFoodPerDay" : "DestForagedFoodPerDay";
            var forageInfo = AccessTools.Property(typeof(Dialog_SplitCaravan), forageProp);
            if (forageInfo != null)
            {
                var forage = (ValueTuple<ThingDef, float>)forageInfo.GetValue(dialog);
                return CaravanStatFormatter.FormatForaging(forage.Item1, forage.Item2);
            }
            return (string)"RimWorldAccess.Stat.ForagingUnknown".Translate();
        }

        private string GetVisibilityStatValue(bool isSource)
        {
            string visibilityProp = isSource ? "SourceVisibility" : "DestVisibility";
            var visInfo = AccessTools.Property(typeof(Dialog_SplitCaravan), visibilityProp);
            if (visInfo != null)
            {
                float visibility = (float)visInfo.GetValue(dialog);
                return CaravanStatFormatter.FormatVisibility(visibility);
            }
            return (string)"RimWorldAccess.Stat.VisibilityUnknown".Translate();
        }

        /// <summary>The New/Original caravan label, shared with the Summary column headers so the breakdown title matches what the player already hears.</summary>
        private static string CaravanLabel(bool isSource)
        {
            return isSource
                ? "RimWorldAccess.Caravan.Split.OriginalCaravanLabel".Translate().ToString()
                : "RimWorldAccess.Caravan.Split.NewCaravanLabel".Translate().ToString();
        }

        /// <summary>
        /// The explanation text for a summary stat cell, named by a whole-phrase key
        /// parameterized with the caravan label so it matches the Summary column headers.
        /// </summary>
        private (string name, string explanation)? GetStatExplanation(int statIndex, bool isSource)
        {
            if (dialog == null || statIndex < 0 || statIndex >= SummaryStatCount)
                return null;

            try
            {
                string prefix = isSource ? "cachedSource" : "cachedDest";
                string propPrefix = isSource ? "Source" : "Dest";
                string fieldName = null;
                string propertyName = null;
                string statName = null;
                string caravanLabel = CaravanLabel(isSource);

                switch (statIndex)
                {
                    case 0:
                        fieldName = prefix + "MassCapacityExplanation";
                        propertyName = propPrefix + "MassCapacity";
                        statName = "RimWorldAccess.Caravan.Split.StatBreakdownName.MassCapacity".Translate(caravanLabel).ToString();
                        break;
                    case 1:
                        fieldName = prefix + "TilesPerDayExplanation";
                        propertyName = propPrefix + "TilesPerDay";
                        statName = "RimWorldAccess.Caravan.Split.StatBreakdownName.Speed".Translate(caravanLabel).ToString();
                        break;
                    case 2:
                        // The game exposes no breakdown explanation for food.
                        return null;
                    case 3:
                        fieldName = prefix + "ForagedFoodPerDayExplanation";
                        propertyName = propPrefix + "ForagedFoodPerDay";
                        statName = "RimWorldAccess.Caravan.Split.StatBreakdownName.Foraging".Translate(caravanLabel).ToString();
                        break;
                    case 4:
                        fieldName = prefix + "VisibilityExplanation";
                        propertyName = propPrefix + "Visibility";
                        statName = "RimWorldAccess.Caravan.Split.StatBreakdownName.Visibility".Translate(caravanLabel).ToString();
                        break;
                    default:
                        return null;
                }

                if (fieldName == null)
                    return null;

                if (propertyName != null)
                {
                    var prop = AccessTools.Property(typeof(Dialog_SplitCaravan), propertyName);
                    prop?.GetValue(dialog);
                }

                var field = AccessTools.Field(typeof(Dialog_SplitCaravan), fieldName);
                if (field == null)
                    return null;

                string explanation = field.GetValue(dialog) as string;
                if (string.IsNullOrEmpty(explanation))
                    return null;

                return (statName, explanation);
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to get stat explanation: {ex.Message}");
                return null;
            }
        }
    }
}
