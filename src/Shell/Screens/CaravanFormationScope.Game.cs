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
    /// Keyboard focus scope for <see cref="Dialog_FormCaravan"/>, registered through
    /// <see cref="ScopeForWindow"/>. Regions: Pawns, Items, Travel supplies, Summary, plus the
    /// automatic Buttons region holding the dialog's real bottom buttons. The classic view shows
    /// them as flat lists and the table view as tables (see <see cref="TransferScreenScope"/>).
    /// In the table view Pawns/Items/Travel supplies are TABLE regions: an identity/count column built by
    /// <see cref="CaravanAnnouncementHelper.BuildItemAnnouncement"/> plus the shared
    /// <see cref="TransferableTableColumns"/> data columns, which match
    /// <c>CaravanUIUtility.CreateCaravanTransferableWidgets</c>'s own draw* flags. Summary is a
    /// 2-column stat table built from <see cref="CaravanStatFormatter"/>; Enter on a stat row opens
    /// the breakdown. <c>EnableSortChord</c> is false because Alt+S is this screen's Send chord and
    /// the shared sort chord would shadow it; Enter on the header row still sorts.
    ///
    /// Vehicle Framework compat: when <see cref="CaravanVehicleTab.Active"/> holds at construction a
    /// Vehicles region is inserted FIRST (matching VF's own tab order) and every other region index
    /// shifts up by one — which is why <see cref="pawnsRegion"/> and its siblings are instance
    /// fields computed in the constructor, never consts. All VF-specific behavior lives behind
    /// <see cref="CaravanVehicleTab.Provider"/>; this scope only asks for row data/columns/text and
    /// forwards Enter to <c>ToggleVehicle</c>, which opens the seat-assignment dialog on select and
    /// mirrors the card's seat-clearing mutation on deselect. Pawns locked to a vehicle seat are
    /// read-only in VF's own widget, and <see cref="PawnLockedAnnounce"/> reproduces that gate on
    /// every pawn-row mutation path. VF's feature-flagged special-properties icon overlay and its
    /// per-comp <c>CompStatCard</c> lines are knowingly not reproduced: both are IMGUI-only with no
    /// backing data to enumerate reflectively, and seat detail is covered by the seat dialog.
    ///
    /// The Buttons region is DECLARED, not captured (<see cref="CaptureWindowButtons"/> = false):
    /// the transferable tables draw their own Widgets.ButtonText column sort headers, so blanket
    /// capture over-collects. The declared actions run the game's own logic with the game's own
    /// translation keys, in DoBottomButtons's own draw order. ChangeRouteButton invokes the dialog's
    /// own button body (<c>Find.WorldRoutePlanner.Start(dialog)</c>), handing off to the mod's
    /// existing route-planner flow; with Vehicle Framework present and vehicles selected it hands
    /// off to VF's <c>WorldRoutePannerReroute</c> instead (see <see cref="VfCaravanRouteCompat"/>).
    /// The route planner is this screen's only destination-choosing path.
    ///
    /// All tab/cursor/search/announce/auto-provision state is instance state — popping the scope IS
    /// the reset. <see cref="CaravanFormationState"/> survives as a bridge for what Harmony patches
    /// and other files still read: the colony-originated destination sub-flow runs while the dialog
    /// is off the WindowStack, with no scope in existence, so that state cannot live on an instance.
    ///
    /// <see cref="OwnsCancel"/>/<see cref="OwnsAccept"/> stay at ScreenScope's defaults. Escape stays
    /// vanilla's own in the common case (the dialog sets closeOnCancel = !reform);
    /// CaravanFormationPatch's Window_OnCancelKeyPressed_Prefix is the sole mechanism blocking it
    /// during an active search or a reform dialog, where <see cref="ShouldClaimEscape"/> claims
    /// Escape and closes manually. Enter differs: Dialog_FormCaravan overrides OnAcceptKeyPressed
    /// directly (the Harmony declaring-type trap), so the general router can never see it, and
    /// CaravanFormationPatch's own prefix blocks it while this scope is live EXCEPT while
    /// <see cref="Send"/> is calling it synchronously
    /// (<see cref="CaravanFormationState.SendingFromOurCode"/> bypasses the block).
    /// </summary>
    public sealed class CaravanFormationScope : TransferScreenScope
    {
        // Instance fields, not consts: an active ICaravanVehicleTabProvider makes Vehicles region 0
        // and shifts every other region up by one. See the class remarks.
        private readonly bool hasVehiclesRegion;
        private readonly int vehiclesRegion;
        private readonly int pawnsRegion;
        private readonly int itemsRegion;
        private readonly int suppliesRegion;
        private readonly int summaryRegion;

        private readonly Dialog_FormCaravan dialog;

        /// <summary>
        /// Pawns-region rows, including vanilla's own read-only section-header rows (see
        /// <see cref="CaravanUIHelper.GetPawnSectionRows"/>). Items/Supplies have no vanilla section
        /// headers, so they stay plain <see cref="TransferableOneWay"/> lists.
        /// </summary>
        private readonly List<CaravanUIHelper.PawnSectionRow> pawnRows = new List<CaravanUIHelper.PawnSectionRow>();
        private readonly List<TransferableOneWay> itemRows = new List<TransferableOneWay>();
        private readonly List<TransferableOneWay> supplyRows = new List<TransferableOneWay>();
        // Refreshed every RefreshContent pass from the provider's live widget order; unlike the
        // three lists above, never cached.
        private readonly List<TransferableOneWay> vehicleRows = new List<TransferableOneWay>();
        private List<CaravanUIHelper.PawnSectionRow> pawnDefaultOrder;
        private List<TransferableOneWay> itemDefaultOrder;
        private List<TransferableOneWay> supplyDefaultOrder;
        private readonly List<string> summaryItems = new List<string>();
        // Language-independent kind tag per summaryItems entry, kept in lockstep: identifies the
        // stat for breakdowns and the Name column without matching a localized string.
        private readonly List<string> summaryKinds = new List<string>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private readonly Dictionary<TransferableOneWay, int> savedSupplyAmounts = new Dictionary<TransferableOneWay, int>();
        private bool autoProvisionEnabled;
        private bool announcedOpen;

        public CaravanFormationScope(Dialog_FormCaravan dialog)
            : this(dialog, OpensClassic, swappedIn: false, previous: null)
        {
        }

        /// <summary>A swap carries the auto-provision state, which lives on this scope rather than the dialog.</summary>
        private CaravanFormationScope(Dialog_FormCaravan dialog, bool classic, bool swappedIn, CaravanFormationScope previous)
            : base(classic, swappedIn)
        {
            this.dialog = dialog;
            if (previous != null)
            {
                autoProvisionEnabled = previous.autoProvisionEnabled;
                foreach (var kvp in previous.savedSupplyAmounts)
                {
                    savedSupplyAmounts[kvp.Key] = kvp.Value;
                }
            }

            hasVehiclesRegion = CaravanVehicleTab.Active;
            vehiclesRegion = hasVehiclesRegion ? 0 : -1;
            pawnsRegion = hasVehiclesRegion ? 1 : 0;
            itemsRegion = pawnsRegion + 1;
            suppliesRegion = itemsRegion + 1;
            summaryRegion = suppliesRegion + 1;

            Claim(SharedMenuGrammar.Cancel, delegate { HandleCancel(); }, when: ShouldClaimEscape);

            Claim("caravanFormation.addMaxOrSelectAll", delegate { HandleShiftEnter(); }, when: NotSummaryRegion);
            Claim("caravanFormation.removeItem", delegate { HandleDeleteItem(); }, when: NotSummaryRegion);
            Claim("caravanFormation.inspect", delegate { InspectOrBreakdown(); });
            Claim("caravanFormation.send", delegate { Send(); });
            Claim("caravanFormation.reset", delegate { Reset(); });
            Claim("caravanFormation.toggleAutoProvision", delegate { ToggleAutoProvision(); });
            Claim("caravanFormation.showHealth", delegate { ShowHealthInfo(); });
            Claim("caravanFormation.showMood", delegate { ShowMoodInfo(); });
            Claim("caravanFormation.showNeeds", delegate { ShowNeedsInfo(); });
            Claim("caravanFormation.showGear", delegate { ShowGearInfo(); });
            Claim("caravanFormation.showSkills", delegate { ShowSkillsInfo(); });

            Claim("caravanFormation.increment", delegate { AdjustQuantityBy(1); }, when: AllowQuantityShortcuts);
            Claim("caravanFormation.decrement", delegate { AdjustQuantityBy(-1); }, when: AllowQuantityShortcuts);
        }

        public override string Name
        {
            get { return "caravan-formation"; }
        }

        /// <summary>Summary's stat rows stay out of the typeahead search space.</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        internal bool HasActiveTypeahead
        {
            get { return TypeaheadHasActiveSearch; }
        }

        /// <summary>Alt+S is this screen's Send chord — the shared sort chord must not shadow it. Enter on the header row still sorts.</summary>
        protected override bool EnableSortChord
        {
            get { return false; }
        }

        // ------------------------------------------------------------------
        // ScreenScope content contract.
        // ------------------------------------------------------------------

        protected override int ContentRegionCount
        {
            get { return hasVehiclesRegion ? 5 : 4; }
        }

        protected override string ContentRegionName(int region)
        {
            if (hasVehiclesRegion && region == vehiclesRegion)
                return CaravanVehicleTab.Provider.RegionName;
            if (region == pawnsRegion)
                return "PawnsTab".Translate().ToString();
            if (region == itemsRegion)
                return "ItemsTab".Translate().ToString();
            if (region == suppliesRegion)
                return "TravelSupplies".Translate().ToString();
            return "RimWorldAccess.Caravan.Form.SummaryRegionName".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            if (hasVehiclesRegion && region == vehiclesRegion)
                return vehicleRows.Count;
            if (region == pawnsRegion)
                return pawnRows.Count;
            if (region == itemsRegion)
                return itemRows.Count;
            if (region == suppliesRegion)
                return supplyRows.Count;
            return summaryItems.Count;
        }

        /// <summary>Table region column counts: identity/count column (1) plus the shared data columns; Summary is a fixed 2-column (name, value) stat table.</summary>
        protected override int TableColumnCount(int region)
        {
            if (dialog == null)
                return 0;
            if (hasVehiclesRegion && region == vehiclesRegion)
                return 1 + CaravanVehicleTab.Provider.ColumnCount;
            if (region == pawnsRegion || region == itemsRegion || region == suppliesRegion)
                return 1 + TransferableTableColumns.ColumnCount(WidgetViewFor(region).Profile);
            if (region == summaryRegion)
                return 2;
            return 0;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            if (region == summaryRegion)
            {
                return column == 0
                    ? new TableColumnInfo("RimWorldAccess.Common.NameColumn".Translate().ToString(), null, false)
                    : new TableColumnInfo("Value".Translate().ToString(), null, false);
            }
            if (hasVehiclesRegion && region == vehiclesRegion)
            {
                if (column == 0)
                {
                    // The widget owns the sort order, so Sortable is forced off: the default true
                    // would let Enter-on-header claim a sort that never happens.
                    TableColumnInfo identity = TransferableTableColumns.IdentityColumnInfo(null);
                    identity.Sortable = false;
                    return identity;
                }
                return CaravanVehicleTab.Provider.ColumnInfo(column - 1);
            }
            if (column == 0)
                return TransferableTableColumns.IdentityColumnInfo("FormCaravanColonyThingCountTip".Translate().ToString());
            TransferableTableColumns.WidgetView view = WidgetViewFor(region);
            return TransferableTableColumns.ColumnInfo(TransferableTableColumns.KindAt(view.Profile, column - 1), isPawnColumn: region == pawnsRegion);
        }

        /// <summary>
        /// Populates the Pawns/Items/Supplies row sets once, so an active sort survives repeated
        /// refreshes instead of being re-filtered back to filter order; <see cref="Reset"/> clears
        /// the caches after recalculating the dialog's transferables. Summary's stat lines are
        /// rebuilt every refresh because their values change with every quantity edit.
        /// </summary>
        protected override void RefreshContent()
        {
            if (dialog == null)
                return;

            if (Classic)
            {
                RefreshClassicRows();
            }
            else if (pawnRows.Count == 0 && itemRows.Count == 0 && supplyRows.Count == 0)
            {
                List<TransferableOneWay> all = dialog.transferables ?? new List<TransferableOneWay>();
                if (hasVehiclesRegion)
                {
                    // VehiclePawn extends Pawn: keep vehicles out of the pawns filter, on both the
                    // initial population and the saved default order.
                    all = all.Where(t => !CaravanVehicleTab.Provider.IsVehicle(t)).ToList();
                }
                // The live pawnsTransfer widget was populated before the pre-filter above ran, so
                // its sections may still carry a vehicle; reading it would reintroduce vehicles into
                // the Pawns table. Skip it — a null widget falls back to the pre-filtered `all`.
                TransferableOneWayWidget pawnsWidget = hasVehiclesRegion
                    ? null
                    : pawnsTransferField?.GetValue(dialog) as TransferableOneWayWidget;
                pawnRows.AddRange(CaravanUIHelper.GetPawnSectionRows(all, pawnsWidget));
                itemRows.AddRange(CaravanUIHelper.FilterByCategory(all, CaravanUIHelper.TransferableCategory.Items));
                supplyRows.AddRange(CaravanUIHelper.FilterByCategory(all, CaravanUIHelper.TransferableCategory.FoodAndMedicine));
                pawnDefaultOrder = new List<CaravanUIHelper.PawnSectionRow>(pawnRows);
                itemDefaultOrder = new List<TransferableOneWay>(itemRows);
                supplyDefaultOrder = new List<TransferableOneWay>(supplyRows);
            }

            if (hasVehiclesRegion)
            {
                // Selection toggles re-sort the widget's own list: read it fresh every pass.
                vehicleRows.Clear();
                vehicleRows.AddRange(CaravanVehicleTab.Provider.VehicleRows());
            }

            BuildSummaryItems();
        }

        /// <summary>Rebuilt every pass from the drawn order; vehicles stay out of Pawns (VehiclePawn extends Pawn).</summary>
        private void RefreshClassicRows()
        {
            List<TransferableOneWay> all = dialog.transferables ?? new List<TransferableOneWay>();
            if (hasVehiclesRegion)
            {
                all = all.Where(t => !CaravanVehicleTab.Provider.IsVehicle(t)).ToList();
            }
            pawnRows.Clear();
            foreach (TransferableOneWay t in ClassicListRows(ListWidget(pawnsRegion), CaravanUIHelper.GetPawnSectionRows(all, null)))
            {
                if (!hasVehiclesRegion || !CaravanVehicleTab.Provider.IsVehicle(t))
                    pawnRows.Add(new CaravanUIHelper.PawnSectionRow(t));
            }
            itemRows.Clear();
            itemRows.AddRange(ClassicListRows(ListWidget(itemsRegion),
                CaravanUIHelper.FilterByCategory(all, CaravanUIHelper.TransferableCategory.Items).Select(t => new CaravanUIHelper.PawnSectionRow(t))));
            supplyRows.Clear();
            supplyRows.AddRange(ClassicListRows(ListWidget(suppliesRegion),
                CaravanUIHelper.FilterByCategory(all, CaravanUIHelper.TransferableCategory.FoodAndMedicine).Select(t => new CaravanUIHelper.PawnSectionRow(t))));
        }

        protected override ElementDescription DescribeTransferItem(int region, int index)
        {
            ElementDescription d = new ElementDescription();
            if (region == summaryRegion)
            {
                if (Classic)
                {
                    d.Label = index >= 0 && index < summaryItems.Count ? summaryItems[index] : "";
                    return d;
                }
                d.Label = index >= 0 && index < summaryKinds.Count ? StatShortName(summaryKinds[index]) : "";
                return d;
            }
            if (hasVehiclesRegion && region == vehiclesRegion)
            {
                if (index >= 0 && index < vehicleRows.Count)
                {
                    d.Label = CaravanVehicleTab.Provider.DescribeRow(vehicleRows[index]);
                    if (Classic)
                        d.Extras = TransferRowDetail.VehicleDetail(vehicleRows[index]);
                }
                return d;
            }
            if (region == pawnsRegion)
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
                // Vehicle Framework draws a seated pawn's checkbox read-only; the composer speaks
                // that state only for a role, and these rows have none.
                if (hasVehiclesRegion && row.Transferable.AnyThing is Pawn seated
                    && CaravanVehicleTab.Provider.IsPawnSeatLocked(seated, out _))
                {
                    d.Label += ", " + TranslatedShellVocabulary.Instance.Word(ElementStateWord.ReadOnly);
                }
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
            if (region == summaryRegion)
            {
                if (row < 0 || row >= summaryItems.Count)
                    return "";
                return column == 0 ? StatShortName(row < summaryKinds.Count ? summaryKinds[row] : null) : summaryItems[row];
            }
            if (hasVehiclesRegion && region == vehiclesRegion)
            {
                if (row < 0 || row >= vehicleRows.Count)
                    return "";
                TransferableOneWay vehicle = vehicleRows[row];
                return column == 0
                    ? CaravanVehicleTab.Provider.DescribeRow(vehicle)
                    : CaravanVehicleTab.Provider.CellText(vehicle, column - 1);
            }
            if (region == pawnsRegion)
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
            if (region == summaryRegion || column == 0)
                return null;
            if (hasVehiclesRegion && region == vehiclesRegion)
            {
                if (row < 0 || row >= vehicleRows.Count)
                    return null;
                return CaravanVehicleTab.Provider.CellTip(vehicleRows[row], column - 1);
            }
            if (region == pawnsRegion)
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

            if (region == summaryRegion)
            {
                OpenStatBreakdown();
                return;
            }

            if (hasVehiclesRegion && region == vehiclesRegion)
            {
                if (index < 0 || index >= vehicleRows.Count)
                    return;
                bool openedSeatDialog = CaravanVehicleTab.Provider.ToggleVehicle(vehicleRows[index]);
                RefreshModel();
                // Deselect changes the row's spoken state, so re-announce; select opens the seat
                // dialog, whose own scope announces, and speaking here would talk over it.
                if (!openedSeatDialog)
                {
                    AnnounceCurrentItem();
                }
                return;
            }

            if (region == pawnsRegion)
            {
                if (index < 0 || index >= pawnRows.Count)
                    return;
                CaravanUIHelper.PawnSectionRow row = pawnRows[index];
                if (row.IsHeader)
                {
                    // Read-only section header: re-read it.
                    AnnounceCurrentItem();
                    return;
                }
                TogglePawnSelection(row.Transferable);
                return;
            }

            List<TransferableOneWay> rows = RowsFor(region);
            if (index < 0 || index >= rows.Count)
                return;
            TransferableOneWay transferable = rows[index];

            if (region == suppliesRegion && autoProvisionEnabled)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.SuppliesTabLocked".Loc());
            }
            else
            {
                OpenQuantityMenu(transferable);
            }
        }

        /// <summary>
        /// Re-orders a Pawns/Items/Supplies row set for a new sort state via the shared
        /// TransferableTableColumns comparers; Summary is not sortable.
        /// </summary>
        protected override int ApplyContentSort(int region, int column, SortCycleResult cycle, int currentRow)
        {
            if (region == summaryRegion)
                return -1;
            if (hasVehiclesRegion && region == vehiclesRegion)
                return -1; // The widget owns the sort order; column 0 is also marked unsortable, so this is defensive only.
            if (region == pawnsRegion)
                return ApplyPawnSort(column, cycle, currentRow);

            List<TransferableOneWay> rows = RowsFor(region);
            List<TransferableOneWay> defaultOrder = region == itemsRegion ? itemDefaultOrder : supplyDefaultOrder;
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
        /// Sorts the Pawns table one section at a time, mirroring vanilla's per-section cache sort in
        /// TransferableOneWayWidget.CacheTransferables: header rows stay fixed as section boundaries
        /// and only the pawns within each section are reordered.
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
                TransferableTableColumns.WidgetView view = WidgetViewFor(pawnsRegion);
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
            if (dialog == null)
                return;
            int region = Model.RegionIndex;
            if (hasVehiclesRegion && region == vehiclesRegion)
            {
                CaravanVehicleTab.Provider.SyncTab(true, 0);
                return;
            }
            // Summary and the Buttons region have no visual tab: leave the dialog on whichever tab
            // was last synced.
            if (region >= summaryRegion)
                return;
            // Region indices are offset by one whenever the Vehicles region exists (see the class
            // remarks), so re-derive the 0/1/2 vanilla tab index.
            int vanillaTab = region - pawnsRegion;
            SyncGameTab(vanillaTab);
            if (hasVehiclesRegion)
            {
                CaravanVehicleTab.Provider.SyncTab(false, vanillaTab);
            }
        }

        /// <summary>False: the tables' own column-sort buttons would pollute the captured region.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        /// <summary>Shift+Enter presses Send from anywhere on this screen: the one-chord proceed.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "caravanFormation.send"; }
        }

        /// <summary>
        /// The dialog's real bottom buttons in DoBottomButtons's own draw order, then the two
        /// Prefs.DevMode-gated dev buttons. Labels come from the game's own translation keys; the dev
        /// labels are vanilla dev-tool literals, presented verbatim.
        /// </summary>
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("Send".Translate().ToString(), Send, "caravanFormation.send"));
                if (ShowCancelButton())
                {
                    actions.Add(new ScreenAction("CancelButton".Translate().ToString(), CloseDialog, SharedMenuGrammar.Cancel));
                }
                actions.Add(new ScreenAction("ResetButton".Translate().ToString(), Reset, "caravanFormation.reset"));
                if (CanChooseRoute())
                {
                    actions.Add(new ScreenAction("ChangeRouteButton".Translate().ToString(), ChangeRoute));
                }
                if (Prefs.DevMode)
                {
                    actions.Add(new ScreenAction("DEV: Send instantly", DevSendInstantly));
                    actions.Add(new ScreenAction("DEV: Select everything", DevSelectEverything));
                }
                AddSwapAction(actions);
                return actions;
            }
        }

        /// <summary>
        /// The dialog's own "DEV: Send instantly" button body: DebugTryFormCaravanInstantly, then
        /// tick and close on success. That method shows its own rejection message and returns false
        /// when there is no valid colonist owner.
        /// </summary>
        private void DevSendInstantly()
        {
            if (dialog == null)
                return;
            var method = AccessTools.Method(typeof(Dialog_FormCaravan), "DebugTryFormCaravanInstantly");
            if (method == null)
                return;
            // A formed caravan closes the dialog, and PostClose must not call that a cancellation.
            CaravanFormationState.NoteSendAttempted();
            if ((bool)method.Invoke(dialog, null))
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                dialog.Close(doCloseSound: false);
                return;
            }
            CaravanFormationState.ResetSendAttempted();
        }

        /// <summary>
        /// The dialog's own "DEV: Select everything" button body: SetToSendEverything, which maxes
        /// every transferable and recaches. Rebuilds the row model and announces.
        /// </summary>
        private void DevSelectEverything()
        {
            if (dialog == null)
                return;
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            AccessTools.Method(typeof(Dialog_FormCaravan), "SetToSendEverything")?.Invoke(dialog, null);
            RefreshModel();
            TolkHelper.Speak("RimWorldAccess.Dev.EverythingSelected".Loc());
        }

        private bool ShowCancelButton()
        {
            var prop = AccessTools.Property(typeof(Dialog_FormCaravan), "ShowCancelButton");
            return prop == null || (bool)prop.GetValue(dialog);
        }

        private bool CanChooseRoute()
        {
            var field = AccessTools.Field(typeof(Dialog_FormCaravan), "canChooseRoute");
            return field != null && (bool)field.GetValue(dialog);
        }

        /// <summary>
        /// True when the dialog's own MustChooseRoute gate would refuse Send for want of a
        /// destination. Normal at open with Vehicle Framework installed: its PostOpen transpiler
        /// removes vanilla's route-first handoff, leaving the player the ChangeRoute button.
        /// </summary>
        private bool NeedsRouteChoice()
        {
            if (dialog == null)
                return false;
            var mustProp = AccessTools.Property(typeof(Dialog_FormCaravan), "MustChooseRoute");
            if (mustProp == null || !(bool)mustProp.GetValue(dialog))
                return false;
            var destField = VanillaAccess.GetField(typeof(Dialog_FormCaravan), "destinationTile");
            if (destField == null)
                return false;
            return !((PlanetTile)destField.GetValue(dialog)).Valid;
        }

        /// <summary>The Cancel button: closes as vanilla's own Cancel does, so PostClose announces it.</summary>
        private void CloseDialog()
        {
            OwnedWindow?.Close();
        }

        /// <summary>
        /// The ChangeRoute button: vanilla's own button body, handing off to the mod's route-planner
        /// flow — or, with Vehicle Framework present and vehicles selected, to VF's own
        /// WorldRoutePannerReroute (see <see cref="VfCaravanRouteCompat"/>).
        /// </summary>
        private void ChangeRoute()
        {
            if (dialog == null)
                return;
            dialog.soundClose.PlayOneShotOnCamera();
            // VF transpiles the vanilla route button to reroute vehicle caravans through its own
            // planner; this mod-owned button bypasses that transpile, so ask VF for the same
            // decision when it is present.
            if (!VfCaravanRouteCompat.StartRoutePlanning(dialog))
            {
                Find.WorldRoutePlanner.Start(dialog);
            }
            // VF's own vehicle planner announces through its scope; the vanilla planner has no voice of its own.
            if (Find.WorldRoutePlanner.Active)
            {
                RoutePlannerState.AnnounceCaravanRouteStart();
            }
        }

        // ------------------------------------------------------------------
        // Lifecycle.
        // ------------------------------------------------------------------

        public override void OnPush()
        {
            base.OnPush();
            if (!SwappedIn)
            {
                DisableAutoSelectTravelSupplies();
            }
            CaravanFormationState.NotifyScopeAttached(this);
            TransferableRingRequest.CurrentProvider = () => CurrentTransferableForQuantity();
        }

        public override void OnPop()
        {
            TransferableRingRequest.CurrentProvider = null;
            CaravanFormationState.NotifyScopeDetached(this);
            base.OnPop();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen || dialog == null)
                return;
            announcedOpen = true;
            string opening;
            if (SwappedIn)
            {
                opening = SwappedInAnnouncement();
            }
            else
            {
                opening = "RimWorldAccess.Caravan.Form.OpenInstructions".Translate().ToString();
                string tabCount = Classic ? null : TabCountFragment();
                if (!string.IsNullOrEmpty(tabCount))
                {
                    opening = opening + ". " + tabCount;
                }
            }
            if (!SwappedIn && NeedsRouteChoice())
            {
                opening = opening + ". " + "RimWorldAccess.Caravan.Form.RouteStillNeeded"
                    .Translate("ChangeRouteButton".Translate());
            }
            // One utterance, so SpeechSanitizer cleans the seams.
            TolkHelper.SpeakData(opening);

            if (hasVehiclesRegion)
            {
                RefreshModel();
                if (vehicleRows.Count == 0)
                {
                    // Mirrors VF's own SetInitialTab: with no vehicles yet, land on Pawns rather than
                    // the resting Vehicles region.
                    MoveResult result = Model.MoveToRegion(pawnsRegion);
                    if (result.Changed)
                    {
                        OnRegionChanged(result);
                    }
                }
                else
                {
                    // Vehicles is already the resting region; sync the sighted tab so it matches the
                    // cursor from the first frame.
                    CaravanVehicleTab.Provider.SyncTab(true, 0);
                }
            }

            AnnounceCurrentItem();
        }

        /// <summary>
        /// Guards the Shift+Enter/Delete claims: not on Summary (nothing to add-max/remove), and not
        /// on Vehicles, whose rows may only mutate through
        /// <see cref="CaravanVehicleTab.Provider"/>'s ToggleVehicle — the item helpers would bypass
        /// the seat-assignment flow.
        /// </summary>
        private bool NotSummaryRegion()
        {
            int region = Model.RegionIndex;
            return region != summaryRegion && !(hasVehiclesRegion && region == vehiclesRegion);
        }

        protected override int ListRegionCount
        {
            get { return summaryRegion; }
        }

        protected override bool HasSummaryRegion
        {
            get { return true; }
        }

        protected override TransferableOneWay ListRowAt(int region, int row)
        {
            if (region == pawnsRegion)
                return CurrentPawnRowTransferable(row);
            if (region == itemsRegion || region == suppliesRegion)
                return IndexedTransferable(RowsFor(region), row);
            return null;
        }

        protected override TransferableOneWayWidget ListWidget(int region)
        {
            if (dialog == null)
                return null;
            System.Reflection.FieldInfo field = region == pawnsRegion ? pawnsTransferField
                : region == itemsRegion ? itemsTransferField
                : region == suppliesRegion ? travelSuppliesTransferField
                : null;
            return field?.GetValue(dialog) as TransferableOneWayWidget;
        }

        protected override TransferableTableColumns.WidgetView ListView(int region)
        {
            return region == pawnsRegion || region == itemsRegion || region == suppliesRegion ? WidgetViewFor(region) : null;
        }

        protected override TransferScreenScope CreateOtherView()
        {
            return new CaravanFormationScope(dialog, !Classic, swappedIn: true, previous: this);
        }

        /// <summary>Vehicles mutate only through the seat-assignment flow.</summary>
        protected override bool ListRegionTakesMaximum(int region)
        {
            return !(hasVehiclesRegion && region == vehiclesRegion);
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
            if (Model.RegionIndex == pawnsRegion && PawnLockedAnnounce(CurrentTransferableForQuantity()))
                return;
            if (max)
                TransferableQuantityHelper.SetToMax(CurrentTransferableForQuantity, NotifyTransferablesChanged);
            else
                TransferableQuantityHelper.SetToZero(CurrentTransferableForQuantity, NotifyTransferablesChanged);
        }

        // ------------------------------------------------------------------
        // Item actions.
        // ------------------------------------------------------------------

        /// <summary>The Vehicles list when it applies, else items or supplies; any other region falls back to supplies, since callers gate Summary and Pawns (which has its own header-aware row type) out first.</summary>
        private List<TransferableOneWay> RowsFor(int region)
        {
            if (hasVehiclesRegion && region == vehiclesRegion)
                return vehicleRows;
            if (region == itemsRegion)
                return itemRows;
            return supplyRows;
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

        /// <summary>True, and speaks the reason, when this pawn is locked to a vehicle seat (parity with VF's own readOnly gate); false when the caller's mutation may proceed.</summary>
        private bool PawnLockedAnnounce(TransferableOneWay t)
        {
            if (!hasVehiclesRegion || t == null)
                return false;
            if (!(t.AnyThing is Pawn pawn))
                return false;
            if (CaravanVehicleTab.Provider.IsPawnSeatLocked(pawn, out string note))
            {
                // Already-resolved text from the provider, not a translation key: SpeakData.
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

            if (region == pawnsRegion)
            {
                TransferableOneWay pawnTransferable = CurrentPawnRowTransferable(index);
                if (pawnTransferable == null)
                    return;
                if (PawnLockedAnnounce(pawnTransferable))
                    return;
                CaravanQuantityHelper.SelectAllPawns(pawnTransferable, NotifyTransferablesChanged, AnnounceCurrentItem);
                return;
            }

            List<TransferableOneWay> rows = RowsFor(region);
            if (rows.Count == 0 || index < 0 || index >= rows.Count)
                return;
            TransferableOneWay transferable = rows[index];

            if (region == suppliesRegion && autoProvisionEnabled)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.SuppliesTabLocked".Loc());
                return;
            }

            float remainingCapacity = dialog.MassCapacity - dialog.MassUsage;
            var result = CaravanQuantityHelper.CalculateMaxToAdd(transferable, remainingCapacity);
            CaravanQuantityHelper.ApplyMaxAdd(transferable, result, NotifyTransferablesChanged);
        }

        private void HandleDeleteItem()
        {
            TypeaheadReset();
            int region = Model.RegionIndex;
            int index = CurrentContentRow();
            bool isPawnTab = region == pawnsRegion;

            TransferableOneWay transferable = isPawnTab
                ? CurrentPawnRowTransferable(index)
                : IndexedTransferable(RowsFor(region), index);
            if (transferable == null)
                return;

            if (isPawnTab && PawnLockedAnnounce(transferable))
                return;
            bool isSuppliesLocked = region == suppliesRegion && autoProvisionEnabled;

            CaravanInputHelper.HandleDeleteKey(transferable, isPawnTab, isSuppliesLocked, NotifyTransferablesChanged);
        }

        private void InspectOrBreakdown()
        {
            int region = Model.RegionIndex;
            if (region == summaryRegion)
            {
                OpenStatBreakdown();
                return;
            }

            int index = CurrentContentRow();
            Thing thing = region == pawnsRegion
                ? CurrentPawnRowTransferable(index)?.AnyThing
                : IndexedTransferable(RowsFor(region), index)?.AnyThing;
            if (thing != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(thing));
            }
        }

        private void OpenStatBreakdown()
        {
            var statInfo = GetCurrentStatExplanation();
            if (statInfo.HasValue)
            {
                StatBreakdownState.Open(statInfo.Value.name, statInfo.Value.explanation);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.NoBreakdownForItem".Loc());
            }
        }

        private void ShowHealthInfo() => CaravanInputHelper.HandlePawnInfoShortcuts(KeyCode.H, SelectedPawn(), true, false, false);
        private void ShowMoodInfo() => CaravanInputHelper.HandlePawnInfoShortcuts(KeyCode.M, SelectedPawn(), true, false, false);
        private void ShowNeedsInfo() => CaravanInputHelper.HandlePawnInfoShortcuts(KeyCode.N, SelectedPawn(), true, false, false);
        private void ShowGearInfo() => CaravanInputHelper.HandlePawnInfoShortcuts(KeyCode.G, SelectedPawn(), true, false, false);
        private void ShowSkillsInfo() => CaravanInputHelper.HandlePawnInfoShortcuts(KeyCode.K, SelectedPawn(), true, false, false);

        /// <summary>
        /// Null on Summary, which has no "current pawn" concept, and null on Vehicles: VehiclePawn
        /// extends Pawn, so without that guard the pawn-info shortcuts could target a vehicle as if
        /// it were a colonist.
        /// </summary>
        private Pawn SelectedPawn()
        {
            int region = Model.RegionIndex;
            if (region == summaryRegion || (hasVehiclesRegion && region == vehiclesRegion))
                return null;
            int index = CurrentContentRow();
            if (region == pawnsRegion)
                return CurrentPawnRowTransferable(index)?.AnyThing as Pawn;
            return CaravanUIHelper.GetSelectedPawn(RowsFor(region), index);
        }

        // ------------------------------------------------------------------
        // Dialog actions.
        // ------------------------------------------------------------------

        private void Send()
        {
            if (dialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.NoDialog".Loc(), SpeechPriority.High);
                return;
            }

            try
            {
                CaravanFormationState.NoteSendAttempted();

                // Stop the route planner BEFORE OnAcceptKeyPressed: for reform caravans
                // TryReformCaravan removes the map before PostClose fires, so this is the last
                // chance to close the planner ahead of the map switch.
                try
                {
                    var routePlanner = Find.WorldRoutePlanner;
                    if (routePlanner != null)
                    {
                        bool plannerActive = false;
                        try { plannerActive = routePlanner.Active; } catch { }
                        if (plannerActive)
                        {
                            routePlanner.Stop();
                        }
                    }
                }
                catch (Exception routeEx)
                {
                    Log.Warning($"RimWorld Access: Failed to stop route planner: {routeEx.Message}");
                }

                // Capture the tile BEFORE sending: reform caravans remove the temporary map before
                // WorldNavigationState.Open runs, and the world cursor must start at the reform site.
                Map currentMap = Find.CurrentMap;
                if (currentMap != null && currentMap.Tile.Valid)
                {
                    WorldNavigationState.PendingStartTile = currentMap.Tile;
                }

                // CaravanFormationPatch blocks the dialog's own OnAcceptKeyPressed while this scope
                // is live; bypass that block for this call.
                CaravanFormationState.SendingFromOurCode = true;
                try
                {
                    dialog.OnAcceptKeyPressed();
                }
                finally
                {
                    CaravanFormationState.SendingFromOurCode = false;
                }
            }
            catch (Exception ex)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.FailedSendCaravan".Loc(ex.Message), SpeechPriority.High);
                Log.Error($"RimWorld Access: Failed to send caravan: {ex.Message}\n{ex.StackTrace}");
                CaravanFormationState.ResetSendAttempted();
            }
        }

        private void Reset()
        {
            if (dialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.NoDialog".Loc(), SpeechPriority.High);
                return;
            }

            try
            {
                var method = AccessTools.Method(typeof(Dialog_FormCaravan), "CalculateAndRecacheTransferables");
                if (method != null)
                {
                    method.Invoke(dialog, null);
                    // The dialog recomputed its transferables: drop the cached row lists and default
                    // orders so the next RefreshContent repopulates from the fresh set.
                    pawnRows.Clear();
                    itemRows.Clear();
                    supplyRows.Clear();
                    pawnDefaultOrder = null;
                    itemDefaultOrder = null;
                    supplyDefaultOrder = null;
                    TolkHelper.Speak("RimWorldAccess.Caravan.Form.SelectionsReset".Loc());
                    RefreshModel();
                    Model.CurrentRegion?.MoveFirst();
                    AnnounceCurrentItem();
                }
            }
            catch (Exception ex)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.FailedReset".Loc(ex.Message), SpeechPriority.High);
                Log.Error($"RimWorld Access: Failed to reset caravan formation: {ex.Message}");
            }
        }

        private void ToggleAutoProvision()
        {
            if (dialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.NoDialog".Loc(), SpeechPriority.High);
                return;
            }

            try
            {
                if (autoProvisionEnabled)
                {
                    autoProvisionEnabled = false;

                    var autoField = AccessTools.Field(typeof(Dialog_FormCaravan), "autoSelectTravelSupplies");
                    autoField?.SetValue(dialog, false);

                    foreach (var kvp in savedSupplyAmounts)
                    {
                        kvp.Key.AdjustTo(kvp.Value);
                    }
                    savedSupplyAmounts.Clear();

                    NotifyTransferablesChanged();
                    TolkHelper.Speak("RimWorldAccess.Caravan.Form.AutoProvisionOff".Loc());
                }
                else
                {
                    savedSupplyAmounts.Clear();

                    List<TransferableOneWay> allTransferables = dialog.transferables ?? new List<TransferableOneWay>();
                    foreach (var t in allTransferables)
                    {
                        if (t.ThingDef.category != ThingCategory.Pawn &&
                            ((!t.ThingDef.thingCategories.NullOrEmpty() && t.ThingDef.thingCategories.Contains(ThingCategoryDefOf.Medicine)) ||
                             (t.ThingDef.IsIngestible && !t.ThingDef.IsDrug && !t.ThingDef.IsCorpse && (t.ThingDef.plant == null || !t.ThingDef.plant.IsTree)) ||
                             (t.AnyThing.GetInnerIfMinified().def.IsBed && t.AnyThing.GetInnerIfMinified().def.building != null && t.AnyThing.GetInnerIfMinified().def.building.bed_caravansCanUse)))
                        {
                            savedSupplyAmounts[t] = t.CountToTransfer;
                        }
                    }

                    var autoField = AccessTools.Field(typeof(Dialog_FormCaravan), "autoSelectTravelSupplies");
                    autoField?.SetValue(dialog, true);

                    var method = AccessTools.Method(typeof(Dialog_FormCaravan), "SelectApproximateBestTravelSupplies");
                    method?.Invoke(dialog, null);

                    autoProvisionEnabled = true;
                    NotifyTransferablesChanged();
                    TolkHelper.Speak("RimWorldAccess.Caravan.Form.AutoProvisionOn".Loc());
                }
            }
            catch (Exception ex)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.FailedToggleAutoProvision".Loc(ex.Message), SpeechPriority.High);
                Log.Error($"RimWorld Access: Failed to toggle auto-provision: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------
        // Escape (menus.cancel): claimed only when ShouldClaimEscape is true.
        // ------------------------------------------------------------------

        /// <summary>
        /// True on a reform dialog, where closeOnCancel=false makes vanilla's Escape handling a no-op
        /// and this scope must close manually. Otherwise the claim never fires and vanilla's own
        /// OnCancelKeyPressed runs uninterrupted.
        /// </summary>
        private bool ShouldClaimEscape()
        {
            return dialog != null && !dialog.closeOnCancel;
        }

        private void HandleCancel()
        {
            Map mapToReturnTo = (Map)VanillaAccess.GetField(typeof(Dialog_FormCaravan), "map").GetValue(dialog);

            if (Find.WorldRoutePlanner != null && Find.WorldRoutePlanner.Active)
            {
                Find.WorldRoutePlanner.Stop();
            }

            Find.WindowStack.TryRemove(dialog);

            if (mapToReturnTo != null)
            {
                CameraJumper.TryHideWorld();
                Current.Game.CurrentMap = mapToReturnTo;
            }

            TolkHelper.Speak("RimWorldAccess.Caravan.Form.ReformationCancelled".Loc());
        }

        // ------------------------------------------------------------------
        // Quantity shortcuts.
        // ------------------------------------------------------------------

        /// <summary>
        /// True when the inline quantity shortcuts apply: a transferable region, not an
        /// auto-provisioned Supplies region, and on Pawns only for a stackable transferable.
        /// </summary>
        private bool AllowQuantityShortcuts()
        {
            RefreshModel();
            int region = Model.RegionIndex;
            if (region != pawnsRegion && region != itemsRegion && region != suppliesRegion)
                return false;
            if (region == suppliesRegion && autoProvisionEnabled)
                return false;
            if (region != pawnsRegion)
                return true;

            TransferableOneWay transferable = CurrentTransferableForQuantity();
            return transferable != null && transferable.MaxCount > 1;
        }

        private void AdjustQuantityBy(int delta)
        {
            if (Model.RegionIndex == pawnsRegion && PawnLockedAnnounce(CurrentTransferableForQuantity()))
                return;
            TransferableQuantityHelper.AdjustQuantity(CurrentTransferableForQuantity, delta, NotifyTransferablesChanged);
        }

        // ------------------------------------------------------------------
        // Data access.
        // ------------------------------------------------------------------

        /// <summary>The transferable under the row cursor, or null when the cursor sits on the table's own column-header row, a Pawns section-header row, or outside a transferable region.</summary>
        private TransferableOneWay CurrentTransferableForQuantity()
        {
            int currentRegion = Model.RegionIndex;
            if (currentRegion != pawnsRegion && currentRegion != itemsRegion && currentRegion != suppliesRegion)
                return null;
            int index = CurrentContentRow();
            if (currentRegion == pawnsRegion)
                return CurrentPawnRowTransferable(index);
            return IndexedTransferable(RowsFor(currentRegion), index);
        }

        /// <summary>The current map's tile, for days-until-rot estimates; null (blank rot cells) when no map is current.</summary>
        private PlanetTile? CurrentTile()
        {
            Map map = Find.CurrentMap;
            return map != null ? (PlanetTile?)map.Tile : null;
        }

        // The column set is read off the dialog's own live TransferableOneWayWidget per tab, so a
        // mod that changes the widget's draw* flags changes this table too. The ColumnProfile
        // presets are only the reflection fallback.
        private static readonly System.Reflection.FieldInfo pawnsTransferField =
            HarmonyLib.AccessTools.Field(typeof(Dialog_FormCaravan), "pawnsTransfer");
        private static readonly System.Reflection.FieldInfo itemsTransferField =
            HarmonyLib.AccessTools.Field(typeof(Dialog_FormCaravan), "itemsTransfer");
        private static readonly System.Reflection.FieldInfo travelSuppliesTransferField =
            HarmonyLib.AccessTools.Field(typeof(Dialog_FormCaravan), "travelSuppliesTransfer");

        private TransferableTableColumns.WidgetView WidgetViewFor(int region)
        {
            System.Reflection.FieldInfo field = region == pawnsRegion ? pawnsTransferField
                : region == itemsRegion ? itemsTransferField
                : travelSuppliesTransferField;
            TransferableTableColumns.ColumnProfile fallback = region == pawnsRegion
                ? TransferableTableColumns.ColumnProfile.CaravanPawns
                : TransferableTableColumns.ColumnProfile.CaravanItems;
            return TransferableTableColumns.ViewFor(
                dialog != null ? field?.GetValue(dialog) as TransferableOneWayWidget : null,
                fallback, CurrentTile());
        }

        private void NotifyTransferablesChanged()
        {
            if (dialog == null)
                return;
            try
            {
                var method = AccessTools.Method(typeof(Dialog_FormCaravan), "Notify_TransferablesChanged");
                method?.Invoke(dialog, null);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to call Notify_TransferablesChanged: {ex.Message}");
            }
        }

        private void DisableAutoSelectTravelSupplies()
        {
            if (dialog == null)
                return;
            try
            {
                var field = AccessTools.Field(typeof(Dialog_FormCaravan), "autoSelectTravelSupplies");
                field?.SetValue(dialog, false);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to disable auto-select travel supplies: {ex.Message}");
            }
        }

        private void SyncGameTab(int region)
        {
            try
            {
                var tabField = AccessTools.Field(typeof(Dialog_FormCaravan), "tab");
                tabField?.SetValue(dialog, region);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to sync game tab: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds the Summary rows: exactly what CaravanUIUtility.DrawCaravanInfo displays (mass,
        /// speed, food, foraging, visibility), plus a destination line.
        /// </summary>
        private void BuildSummaryItems()
        {
            summaryItems.Clear();
            summaryKinds.Clear();

            if (dialog == null)
            {
                return;
            }

            try
            {
                float massUsage = dialog.MassUsage;
                float massCapacity = dialog.MassCapacity;
                bool isOverloaded = massUsage > massCapacity;
                summaryItems.Add(CaravanStatFormatter.FormatMass(massUsage, massCapacity));
                summaryKinds.Add("Mass");

                var tilesInfo = AccessTools.Property(typeof(Dialog_FormCaravan), "TilesPerDay");
                if (tilesInfo != null)
                {
                    float tilesPerDay = (float)tilesInfo.GetValue(dialog);
                    summaryItems.Add(CaravanStatFormatter.FormatSpeed(tilesPerDay, isOverloaded));
                    summaryKinds.Add("Speed");
                }

                var foodInfo = AccessTools.Property(typeof(Dialog_FormCaravan), "DaysWorthOfFood");
                if (foodInfo != null)
                {
                    var food = (ValueTuple<float, float>)foodInfo.GetValue(dialog);
                    summaryItems.Add(CaravanStatFormatter.FormatFood(food.Item1, food.Item2));
                    summaryKinds.Add("Food");
                }

                var forageInfo = AccessTools.Property(typeof(Dialog_FormCaravan), "ForagedFoodPerDay");
                if (forageInfo != null)
                {
                    var forage = (ValueTuple<ThingDef, float>)forageInfo.GetValue(dialog);
                    summaryItems.Add(CaravanStatFormatter.FormatForaging(forage.Item1, forage.Item2));
                    summaryKinds.Add("Foraging");
                }

                var visInfo = AccessTools.Property(typeof(Dialog_FormCaravan), "Visibility");
                if (visInfo != null)
                {
                    float visibility = (float)visInfo.GetValue(dialog);
                    summaryItems.Add(CaravanStatFormatter.FormatVisibility(visibility));
                    summaryKinds.Add("Visibility");
                }

                var destTileField = VanillaAccess.GetField(typeof(Dialog_FormCaravan), "destinationTile");
                if (destTileField != null)
                {
                    PlanetTile destTile = (PlanetTile)destTileField.GetValue(dialog);
                    if (destTile.Valid && Find.WorldGrid != null)
                    {
                        string tileName = WorldInfoHelper.GetTileSummary(destTile);
                        string destItem = "RimWorldAccess.Caravan.Form.SummaryDestination".Translate(tileName);

                        var ticksToArriveProp = AccessTools.Property(typeof(Dialog_FormCaravan), "TicksToArrive");
                        if (ticksToArriveProp != null)
                        {
                            try
                            {
                                int ticksToArrive = (int)ticksToArriveProp.GetValue(dialog);
                                if (ticksToArrive > 0)
                                {
                                    float daysToArrive = ticksToArrive / 60000f;
                                    destItem = "RimWorldAccess.Caravan.Form.SummaryDestinationWithEta".Translate(tileName, daysToArrive.ToString("F1"));
                                }
                            }
                            catch { }
                        }
                        summaryItems.Add(destItem);
                        summaryKinds.Add("Destination");
                    }
                    else
                    {
                        summaryItems.Add("RimWorldAccess.Caravan.Form.SummaryDestinationNotSet".Translate());
                        summaryKinds.Add("Destination");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to get caravan stats: {ex.Message}");
                summaryItems.Add("RimWorldAccess.Caravan.Form.SummaryStatsUnavailable".Translate());
                summaryKinds.Add(null);
            }
        }

        /// <summary>The Summary stat table's Name-column text for a language-independent kind tag, reusing the same short labels the Alt+I breakdown already speaks.</summary>
        private static string StatShortName(string kind)
        {
            switch (kind)
            {
                case "Mass": return "RimWorldAccess.Caravan.Inspect.StatMass".Translate();
                case "Speed": return "RimWorldAccess.Caravan.Inspect.StatSpeed".Translate();
                case "Food": return "RimWorldAccess.Caravan.Inspect.StatFood".Translate();
                case "Foraging": return "RimWorldAccess.Caravan.Inspect.StatForaging".Translate();
                case "Visibility": return "RimWorldAccess.Caravan.Inspect.StatVisibility".Translate();
                case "Destination": return "RimWorldAccess.Caravan.Inspect.StatDestination".Translate();
                default: return "";
            }
        }

        /// <summary>
        /// The explanation text for the selected summary stat, keyed off the language-independent
        /// summaryKinds tag rather than the localized display string.
        /// </summary>
        private (string name, string explanation)? GetCurrentStatExplanation()
        {
            if (dialog == null || summaryItems.Count == 0)
                return null;

            int index = CurrentContentRow();
            if (index < 0 || index >= summaryItems.Count)
                return null;

            try
            {
                string kind = index < summaryKinds.Count ? summaryKinds[index] : null;
                string fieldName = null;
                string propertyName = null;
                string statName = null;

                switch (kind)
                {
                    case "Mass":
                        fieldName = "cachedMassCapacityExplanation";
                        propertyName = "MassCapacity";
                        statName = "RimWorldAccess.Caravan.Inspect.StatMass".Translate();
                        break;
                    case "Speed":
                        fieldName = "cachedTilesPerDayExplanation";
                        propertyName = "TilesPerDay";
                        statName = "RimWorldAccess.Caravan.Inspect.StatSpeed".Translate();
                        break;
                    case "Foraging":
                        fieldName = "cachedForagedFoodPerDayExplanation";
                        propertyName = "ForagedFoodPerDay";
                        statName = "RimWorldAccess.Caravan.Inspect.StatForaging".Translate();
                        break;
                    case "Visibility":
                        fieldName = "cachedVisibilityExplanation";
                        propertyName = "Visibility";
                        statName = "RimWorldAccess.Caravan.Inspect.StatVisibility".Translate();
                        break;
                    default:
                        // Food has no in-game breakdown; nor do Destination and the rest.
                        return null;
                }

                if (fieldName == null)
                    return null;

                // Reading the property recalculates the cached explanation.
                if (propertyName != null)
                {
                    var prop = AccessTools.Property(typeof(Dialog_FormCaravan), propertyName);
                    prop?.GetValue(dialog);
                }

                var field = AccessTools.Field(typeof(Dialog_FormCaravan), fieldName);
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
