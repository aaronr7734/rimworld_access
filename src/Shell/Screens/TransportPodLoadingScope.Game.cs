using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The transfer-loading screen for both <see cref="Dialog_LoadTransporters"/> and
    /// <see cref="Dialog_EnterPortal"/>, unified behind <see cref="ITransferLoadDialog"/>.
    ///
    /// Regions: Pawns, Items, Summary (only when the adapter reports one; map portals have none),
    /// plus the automatic Buttons region, shown as flat lists in the classic view and as tables in
    /// the table view (see <see cref="TransferScreenScope"/>). In the table view Pawns and Items are full TABLE regions — an identity/count
    /// column plus the shared <see cref="TransferableTableColumns"/> data columns read off the
    /// dialog's own live TransferableOneWayWidget — and Summary is a 2-column stat table whose Enter
    /// opens the stat breakdown. <see cref="EnableSortChord"/> is false because Alt+S is this
    /// screen's Accept chord and the shared sort chord would shadow it inside a table region; Enter
    /// on the header row still sorts.
    ///
    /// The Buttons region is DECLARED, not captured: the transferable table draws its own
    /// Widgets.ButtonText calls for column headers, so blanket capture collects about nineteen
    /// buttons instead of the three real actions. The declared actions run the game's own logic.
    ///
    /// All cursor, tab and search state is instance state, so popping the scope IS the reset;
    /// <see cref="TransportPodLoadingState"/> is only the static bridge the Harmony patches read.
    /// Both dialogs keep vanilla's own Escape close, with the per-dialog cancel blockers in
    /// TransportPodPatch/PortalPatch as the sole cancel-blocking mechanism and each dialog's own
    /// OnAcceptKeyPressed override carrying its accept blocker (the Harmony declaring-type trap).
    /// </summary>
    public sealed class TransportPodLoadingScope : TransferScreenScope
    {
        /// <summary>
        /// Which content region a region index maps to. An items-only dialog has no Pawns region at
        /// all, so <see cref="regionKinds"/> is built per-adapter and the region SET is the adapter's
        /// decision, never a hardcoded 0/1/2.
        /// </summary>
        private enum RegionKind
        {
            Pawns,
            Items,
            Summary,
        }

        private readonly ITransferLoadDialog adapter;
        private readonly Window dialog;
        private readonly List<RegionKind> regionKinds = new List<RegionKind>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        /// <summary>
        /// Pawns-region rows, including vanilla's read-only section-header rows. Items is built from
        /// a single untitled AddSection call, so it stays a plain list.
        /// </summary>
        private readonly List<CaravanUIHelper.PawnSectionRow> pawnRows = new List<CaravanUIHelper.PawnSectionRow>();
        private readonly List<TransferableOneWay> itemRows = new List<TransferableOneWay>();
        private List<CaravanUIHelper.PawnSectionRow> pawnDefaultOrder;
        private List<TransferableOneWay> itemDefaultOrder;
        private readonly List<string> summaryRows = new List<string>();
        private bool announcedOpen;

        public TransportPodLoadingScope(Window dialog) : this(dialog, ResolveAdapter(dialog))
        {
        }

        /// <summary>
        /// Takes an already-resolved adapter, the entry point for compat shims that build their own
        /// <see cref="ITransferLoadDialog"/> over a window type this scope never references.
        /// </summary>
        internal TransportPodLoadingScope(Window dialog, ITransferLoadDialog adapter)
            : this(dialog, adapter, OpensClassic, swappedIn: false)
        {
        }

        /// <summary>A swap reuses the adapter: compat adapters cannot be resolved again from the window.</summary>
        private TransportPodLoadingScope(Window dialog, ITransferLoadDialog adapter, bool classic, bool swappedIn)
            : base(classic, swappedIn)
        {
            this.dialog = dialog;
            this.adapter = adapter;
            if (this.adapter == null)
            {
                // Stay a shadow (IsLive false) so vanilla keeps the dialog rather than trapping the
                // user in a scope that cannot read it.
                TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.ReflectionFailed".Loc(), SpeechPriority.High);
            }
            else
            {
                if (this.adapter.HasPawnsTab)
                    regionKinds.Add(RegionKind.Pawns);
                regionKinds.Add(RegionKind.Items);
                if (this.adapter.HasSummary)
                    regionKinds.Add(RegionKind.Summary);
            }

            Claim("transportPodLoading.addMaximum", delegate { AddMaximum(); });
            Claim("transportPodLoading.removeItem", delegate { RemoveSelected(); });
            Claim("transportPodLoading.accept", delegate { Accept(); });
            Claim("transportPodLoading.reset", delegate { Reset(); });
            Claim("transportPodLoading.inspect", delegate { InspectOrBreakdown(); });
            Claim("transportPodLoading.showHealth", delegate { ShowPawnInfo(UnityEngine.KeyCode.H); });
            Claim("transportPodLoading.showMood", delegate { ShowPawnInfo(UnityEngine.KeyCode.M); });
            Claim("transportPodLoading.showNeeds", delegate { ShowPawnInfo(UnityEngine.KeyCode.N); });
            Claim("transportPodLoading.showGear", delegate { ShowPawnInfo(UnityEngine.KeyCode.G); });
            Claim("transportPodLoading.showSkills", delegate { ShowPawnInfo(UnityEngine.KeyCode.K); });

            Claim("transportPodLoading.increment", delegate { AdjustQuantityBy(1); }, when: AllowQuantityShortcuts);
            Claim("transportPodLoading.decrement", delegate { AdjustQuantityBy(-1); }, when: AllowQuantityShortcuts);
        }

        /// <summary>The two vanilla dialogs' own type switch, used by the single-Window constructor.</summary>
        private static ITransferLoadDialog ResolveAdapter(Window dialog)
        {
            if (dialog is Dialog_LoadTransporters loadDialog && LoadTransportersAdapter.ReflectionReady)
                return new LoadTransportersAdapter(loadDialog);
            if (dialog is Dialog_EnterPortal portalDialog && EnterPortalAdapter.ReflectionReady)
                return new EnterPortalAdapter(portalDialog);
            return null;
        }

        /// <summary>
        /// Null for any index outside the content regions: Model.RegionIndex reaches
        /// ContentRegionCount while the cursor sits on the Buttons region, where a raw list index
        /// would throw.
        /// </summary>
        private RegionKind? KindOf(int region)
        {
            return region >= 0 && region < regionKinds.Count ? regionKinds[region] : (RegionKind?)null;
        }

        public override string Name
        {
            get { return "transport-pod-loading"; }
        }

        /// <summary>
        /// Deliberately does NOT test WindowlessInspectionState: inspection stands down beneath a
        /// window-attached dialog while its state stays active, so such a term would deadlock this
        /// scope whenever the dialog is opened from the inspection tree.
        /// </summary>
        public override bool IsLive
        {
            get { return adapter != null; }
        }

        /// <summary>Cross-region typeahead; Summary's stat rows stay out of the search space.</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Alt+S is this screen's Accept chord — the shared sort chord must not shadow it. Enter on the header row still sorts.</summary>
        protected override bool EnableSortChord
        {
            get { return false; }
        }

        internal bool HasActiveTypeahead
        {
            get { return TypeaheadHasActiveSearch; }
        }

        internal string CancelAnnouncement
        {
            get { return adapter?.CancelAnnouncement; }
        }

        protected override int ContentRegionCount
        {
            get { return regionKinds.Count; }
        }

        protected override string ContentRegionName(int region)
        {
            switch (KindOf(region))
            {
                case RegionKind.Pawns: return "PawnsTab".Translate().ToString();
                case RegionKind.Items: return "ItemsTab".Translate().ToString();
                default: return "RimWorldAccess.TransportPods.Loading.SummaryTab".Translate().ToString();
            }
        }

        protected override int ContentItemCount(int region)
        {
            switch (KindOf(region))
            {
                case RegionKind.Pawns: return pawnRows.Count;
                case RegionKind.Items: return itemRows.Count;
                default: return summaryRows.Count;
            }
        }

        /// <summary>Identity column plus this dialog's own data columns; Summary is a fixed name/value pair.</summary>
        protected override int TableColumnCount(int region)
        {
            if (adapter == null)
                return 0;
            switch (KindOf(region))
            {
                case RegionKind.Pawns: return 1 + TransferableTableColumns.ColumnCount(adapter.PawnsView.Profile);
                case RegionKind.Items: return 1 + TransferableTableColumns.ColumnCount(adapter.ItemsView.Profile);
                case RegionKind.Summary: return 2;
                default: return 0;
            }
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            if (KindOf(region) == RegionKind.Summary)
            {
                return column == 0
                    ? new TableColumnInfo("RimWorldAccess.Common.NameColumn".Translate().ToString(), null, false)
                    : new TableColumnInfo("Value".Translate().ToString(), null, false);
            }
            if (adapter == null)
                return new TableColumnInfo();
            bool isPawnsRegion = KindOf(region) == RegionKind.Pawns;
            if (column == 0)
                return TransferableTableColumns.IdentityColumnInfo(adapter.CountColumnTooltip);
            TransferableTableColumns.WidgetView view = isPawnsRegion ? adapter.PawnsView : adapter.ItemsView;
            return TransferableTableColumns.ColumnInfo(TransferableTableColumns.KindAt(view.Profile, column - 1), isPawnColumn: isPawnsRegion);
        }

        /// <summary>
        /// Populates the Pawns/Items row sets ONCE, so an active sort survives refreshes instead of
        /// snapping back to filter order. Summary's lines rebuild every pass, since their values move
        /// with every quantity edit.
        /// </summary>
        protected override void RefreshContent()
        {
            if (adapter == null)
                return;

            if (Classic)
            {
                List<TransferableOneWay> all = adapter.GetAllTransferables() ?? new List<TransferableOneWay>();
                pawnRows.Clear();
                if (adapter.HasPawnsTab)
                {
                    foreach (TransferableOneWay t in ClassicListRows(PawnsWidget(), CaravanUIHelper.GetPawnSectionRows(all, null)))
                        pawnRows.Add(new CaravanUIHelper.PawnSectionRow(t));
                }
                itemRows.Clear();
                itemRows.AddRange(ClassicListRows(ItemsWidget(),
                    all.Where(t => t.ThingDef.category != ThingCategory.Pawn).Select(t => new CaravanUIHelper.PawnSectionRow(t))));
            }
            else if (pawnRows.Count == 0 && itemRows.Count == 0)
            {
                List<TransferableOneWay> all = adapter.GetAllTransferables() ?? new List<TransferableOneWay>();
                pawnRows.AddRange(CaravanUIHelper.GetPawnSectionRows(all, PawnsWidget()));
                // Everything that is not a pawn, matching the dialog's own itemsTransfer filter;
                // unlike caravans there is no supplies tab.
                itemRows.AddRange(all.Where(t => t.ThingDef.category != ThingCategory.Pawn));
                pawnDefaultOrder = new List<CaravanUIHelper.PawnSectionRow>(pawnRows);
                itemDefaultOrder = new List<TransferableOneWay>(itemRows);
            }

            summaryRows.Clear();
            if (adapter.HasSummary)
            {
                adapter.BuildSummaryItems(summaryRows, GetMassUsage());
            }
        }

        /// <summary>
        /// The dialog's live Pawns <see cref="TransferableOneWayWidget"/>, found by field name on
        /// whatever concrete type the dialog is. Null for a dialog with no such field, in which case
        /// <see cref="CaravanUIHelper.GetPawnSectionRows"/> falls back to its predicate builder.
        /// </summary>
        private TransferableOneWayWidget PawnsWidget()
        {
            if (dialog == null)
                return null;
            return AccessTools.Field(dialog.GetType(), "pawnsTransfer")?.GetValue(dialog) as TransferableOneWayWidget;
        }

        /// <summary>The dialog's live Items widget, by field name like <see cref="PawnsWidget"/>.</summary>
        private TransferableOneWayWidget ItemsWidget()
        {
            if (dialog == null)
                return null;
            return AccessTools.Field(dialog.GetType(), "itemsTransfer")?.GetValue(dialog) as TransferableOneWayWidget;
        }

        protected override ElementDescription DescribeTransferItem(int region, int index)
        {
            ElementDescription d = new ElementDescription();
            if (KindOf(region) == RegionKind.Summary)
            {
                if (index < 0 || index >= summaryRows.Count)
                    return d;
                d.Label = Classic ? summaryRows[index] : (adapter?.GetStatName(index) ?? "");
                return d;
            }
            if (KindOf(region) == RegionKind.Pawns)
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
                return d;
            }
            List<TransferableOneWay> rows = itemRows;
            if (index >= 0 && index < rows.Count)
            {
                d.Label = CaravanAnnouncementHelper.BuildItemAnnouncement(rows[index], index, rows.Count, includePosition: false);
            }
            return d;
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            if (KindOf(region) == RegionKind.Summary)
            {
                if (row < 0 || row >= summaryRows.Count)
                    return "";
                return column == 0 ? (adapter?.GetStatName(row) ?? "") : summaryRows[row];
            }
            if (adapter == null)
                return "";
            if (KindOf(region) == RegionKind.Pawns)
            {
                if (row < 0 || row >= pawnRows.Count)
                    return "";
                CaravanUIHelper.PawnSectionRow pawnRow = pawnRows[row];
                if (pawnRow.IsHeader)
                    return column == 0 ? pawnRow.Header : "";
                if (column == 0)
                    return CaravanAnnouncementHelper.BuildItemAnnouncement(pawnRow.Transferable, row, pawnRows.Count, includePosition: false);
                TransferableTableColumns.WidgetView pawnsView = adapter.PawnsView;
                return TransferableTableColumns.CellText(TransferableTableColumns.KindAt(pawnsView.Profile, column - 1), pawnRow.Transferable, pawnsView);
            }
            List<TransferableOneWay> rows = itemRows;
            if (row < 0 || row >= rows.Count)
                return "";
            TransferableOneWay transferable = rows[row];
            if (column == 0)
                return CaravanAnnouncementHelper.BuildItemAnnouncement(transferable, row, rows.Count, includePosition: false);
            TransferableTableColumns.WidgetView view = adapter.ItemsView;
            return TransferableTableColumns.CellText(TransferableTableColumns.KindAt(view.Profile, column - 1), transferable, view);
        }

        protected override string ContentCellTip(int region, int row, int column)
        {
            if (KindOf(region) == RegionKind.Summary || column == 0 || adapter == null)
                return null;
            if (KindOf(region) == RegionKind.Pawns)
            {
                if (row < 0 || row >= pawnRows.Count || pawnRows[row].IsHeader)
                    return null;
                TransferableTableColumns.WidgetView pawnsView = adapter.PawnsView;
                return TransferableTableColumns.CellTip(TransferableTableColumns.KindAt(pawnsView.Profile, column - 1), pawnRows[row].Transferable, pawnsView);
            }
            List<TransferableOneWay> rows = itemRows;
            if (row < 0 || row >= rows.Count)
                return null;
            TransferableTableColumns.WidgetView view = adapter.ItemsView;
            return TransferableTableColumns.CellTip(TransferableTableColumns.KindAt(view.Profile, column - 1), rows[row], view);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (KindOf(region) == RegionKind.Summary)
            {
                OpenStatBreakdown();
                return;
            }
            if (KindOf(region) == RegionKind.Pawns)
            {
                if (index < 0 || index >= pawnRows.Count)
                    return;
                CaravanUIHelper.PawnSectionRow row = pawnRows[index];
                if (row.IsHeader)
                {
                    AnnounceCurrentItem();
                    return;
                }
                if (row.Transferable.MaxCount == 1)
                {
                    TogglePawnSelection(row.Transferable);
                }
                else
                {
                    OpenQuantityMenu(row.Transferable);
                }
                return;
            }
            List<TransferableOneWay> rows = itemRows;
            if (index < 0 || index >= rows.Count)
                return;
            OpenQuantityMenu(rows[index]);
        }

        /// <summary>
        /// Re-orders a Pawns/Items row set with the shared TransferableTableColumns comparers, the
        /// identity column riding vanilla's own TransferableComparer_Name. Summary is not sortable.
        /// </summary>
        protected override int ApplyContentSort(int region, int column, SortCycleResult cycle, int currentRow)
        {
            if (KindOf(region) == RegionKind.Summary || adapter == null)
                return -1;
            if (KindOf(region) == RegionKind.Pawns)
                return ApplyPawnSort(column, cycle, currentRow);

            List<TransferableOneWay> rows = itemRows;
            List<TransferableOneWay> defaultOrder = itemDefaultOrder;
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
                    TransferableTableColumns.WidgetView view = adapter.ItemsView;
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
        /// Sorts the Pawns table one section at a time, mirroring
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
                TransferableTableColumns.WidgetView view = adapter.PawnsView;
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
            if (adapter == null)
                return;
            // Mirror into the dialog's own tab so the sighted view follows; Summary has no such tab.
            switch (KindOf(Model.RegionIndex))
            {
                case RegionKind.Pawns: adapter.GameTab = 0; break;
                case RegionKind.Items: adapter.GameTab = 1; break;
            }
        }

        /// <summary>False — the transferable table's own buttons would pollute the region (see the class remarks).</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        /// <summary>Shift+Enter presses Accept from anywhere on this screen: the one-chord proceed.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "transportPodLoading.accept"; }
        }

        /// <summary>
        /// The dialog's three real bottom buttons in vanilla's draw order, then
        /// Dialog_LoadTransporters' two dev-mode buttons, which only that dialog draws and which are
        /// therefore gated on the adapter type; "Load instantly" also honors vanilla's
        /// !LoadingInProgressOrReadyToLaunch gate. The dev labels are vanilla's own untranslated
        /// dev-tool literals, presented verbatim.
        /// </summary>
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction(
                    "ResetButton".Translate().ToString(),
                    Reset,
                    "transportPodLoading.reset"));
                actions.Add(new ScreenAction(
                    "CancelButton".Translate().ToString(),
                    CloseDialog,
                    SharedMenuGrammar.Cancel));
                actions.Add(new ScreenAction(
                    "AcceptButton".Translate().ToString(),
                    Accept,
                    "transportPodLoading.accept"));
                if (Prefs.DevMode && adapter is LoadTransportersAdapter loadAdapter)
                {
                    if (!loadAdapter.LoadingInProgressOrReadyToLaunch)
                    {
                        actions.Add(new ScreenAction("DEV: Load instantly", () => DevLoadInstantly(loadAdapter)));
                    }
                    actions.Add(new ScreenAction("DEV: Select everything", () => DevSelectEverything(loadAdapter)));
                }
                AddSwapAction(actions);
                return actions;
            }
        }

        /// <summary>
        /// The dialog's own "DEV: Load instantly" body: its DebugTryLoadInstantly, then tick and
        /// close, whose PostClose pops this scope.
        /// </summary>
        private void DevLoadInstantly(LoadTransportersAdapter loadAdapter)
        {
            if (loadAdapter.DebugTryLoadInstantly())
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                OwnedWindow?.Close(doCloseSound: false);
                TolkHelper.Speak("RimWorldAccess.Dev.TransportersLoadedInstantly".Loc());
            }
        }

        /// <summary>
        /// The dialog's own "DEV: Select everything" body: SetToLoadEverything maxes every
        /// transferable and recaches, then the row model is rebuilt.
        /// </summary>
        private void DevSelectEverything(LoadTransportersAdapter loadAdapter)
        {
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            loadAdapter.SetToLoadEverything();
            RefreshModel();
            TolkHelper.Speak("RimWorldAccess.Dev.EverythingSelected".Loc());
        }

        /// <summary>The Cancel button: close exactly as vanilla's own Cancel does, so PostClose announces the cancellation.</summary>
        private void CloseDialog()
        {
            OwnedWindow?.Close();
        }

        public override void OnPush()
        {
            base.OnPush();
            TransportPodLoadingState.ActiveScope = this;
            TransferableRingRequest.CurrentProvider = () => CurrentTransferable();
        }

        public override void OnPop()
        {
            TransferableRingRequest.CurrentProvider = null;
            TransportPodLoadingState.ActiveScope = null;
            base.OnPop();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen || adapter == null)
                return;
            announcedOpen = true;
            string tabCount = Classic || SwappedIn ? null : TabCountFragment();
            string opening = SwappedIn ? SwappedInAnnouncement() : adapter.OpenAnnouncement;
            // One utterance, so SpeechSanitizer cleans the seams; separate Speak calls each sanitize
            // in isolation and leave stray periods.
            TolkHelper.SpeakData(string.IsNullOrEmpty(tabCount) ? opening : opening + ". " + tabCount);
            AnnounceCurrentItem();
        }

        protected override int ListRegionCount
        {
            get { return HasSummaryRegion ? regionKinds.Count - 1 : regionKinds.Count; }
        }

        protected override bool HasSummaryRegion
        {
            get { return regionKinds.Contains(RegionKind.Summary); }
        }

        protected override TransferableOneWay ListRowAt(int region, int row)
        {
            switch (KindOf(region))
            {
                case RegionKind.Pawns: return CurrentPawnRowTransferable(row);
                case RegionKind.Items: return row >= 0 && row < itemRows.Count ? itemRows[row] : null;
                default: return null;
            }
        }

        protected override TransferableOneWayWidget ListWidget(int region)
        {
            switch (KindOf(region))
            {
                case RegionKind.Pawns: return PawnsWidget();
                case RegionKind.Items: return ItemsWidget();
                default: return null;
            }
        }

        protected override TransferableTableColumns.WidgetView ListView(int region)
        {
            if (adapter == null)
                return null;
            switch (KindOf(region))
            {
                case RegionKind.Pawns: return adapter.PawnsView;
                case RegionKind.Items: return adapter.ItemsView;
                default: return null;
            }
        }

        protected override TransferScreenScope CreateOtherView()
        {
            return new TransportPodLoadingScope(dialog, adapter, !Classic, swappedIn: true);
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
            if (max)
                TransferableQuantityHelper.SetToMax(CurrentTransferable, NotifyChanged);
            else
                TransferableQuantityHelper.SetToZero(CurrentTransferable, NotifyChanged);
        }

        private void TogglePawnSelection(TransferableOneWay transferable)
        {
            string label = TransferableLabel(transferable);
            if (transferable.CountToTransfer > 0)
            {
                transferable.AdjustTo(0);
                TolkHelper.SpeakData((string)"RimWorldAccess.TransportPods.Loading.ItemUnchecked".Translate(label));
            }
            else
            {
                transferable.AdjustTo(transferable.MaxCount);
                TolkHelper.SpeakData((string)"RimWorldAccess.TransportPods.Loading.ItemChecked".Translate(label));
            }
            NotifyChanged();
            // Fresh search per selection: type a name, Enter, type the next.
            TypeaheadReset();
        }

        private void OpenQuantityMenu(TransferableOneWay transferable)
        {
            QuantityMenuState.Open(transferable, delegate (int newQuantity)
            {
                if (!transferable.CanAdjustTo(newQuantity).Accepted)
                    return;
                transferable.AdjustTo(newQuantity);
                NotifyChanged();
            });
        }

        private void AddMaximum()
        {
            TransferableOneWay transferable = CurrentTransferable();
            if (transferable == null)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoItemSelected".Loc());
                return;
            }
            float remainingCapacity = adapter.MassCapacity - GetMassUsage();
            var result = CaravanQuantityHelper.CalculateMaxToAdd(transferable, remainingCapacity);
            if (result.ToAdd > 0)
            {
                transferable.AdjustTo(result.NewCount);
                NotifyChanged();
            }
            TolkHelper.SpeakData(result.Announcement);
        }

        private void RemoveSelected()
        {
            TransferableOneWay transferable = CurrentTransferable();
            if (transferable == null)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoItemSelected".Loc());
                return;
            }
            if (transferable.CountToTransfer == 0)
            {
                TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.AlreadyAtZero".Loc());
                return;
            }
            transferable.AdjustTo(0);
            NotifyChanged();
            TolkHelper.SpeakData((string)"RimWorldAccess.TransportPods.Loading.ItemRemoved".Translate(TransferableLabel(transferable)));
        }

        private void InspectOrBreakdown()
        {
            if (KindOf(Model.RegionIndex) == RegionKind.Summary)
            {
                OpenStatBreakdown();
                return;
            }
            TransferableOneWay transferable = CurrentTransferable();
            Thing thing = transferable?.AnyThing;
            if (thing != null)
            {
                // Dialog_InfoCard rather than WindowlessInspectionState: world pawns lack the
                // map-pawn tab set and fail tab discovery.
                Find.WindowStack.Add(new Dialog_InfoCard(thing));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.NoItemToInspect".Loc());
            }
        }

        private void OpenStatBreakdown()
        {
            RefreshModel();
            int row = CurrentContentRow();
            var statInfo = adapter != null && row >= 0
                ? adapter.GetStatExplanation(row)
                : null;
            if (statInfo.HasValue)
            {
                StatBreakdownState.Open(statInfo.Value.name, statInfo.Value.explanation);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.NoBreakdown".Loc());
            }
        }

        private void ShowPawnInfo(UnityEngine.KeyCode key)
        {
            CaravanInputHelper.HandlePawnInfoShortcuts(key, SelectedPawn(), true, false, false);
        }

        private void Accept()
        {
            if (adapter == null)
                return;
            TransportPodLoadingState.NoteAcceptAttempted();
            TransportPodLoadingState.AcceptingFromOurCode = true;
            try
            {
                adapter.TriggerAccept();
            }
            finally
            {
                TransportPodLoadingState.AcceptingFromOurCode = false;
            }
            // acceptAttempted intentionally persists while the dialog stays open behind a
            // confirmation box; the PostClose patch reads and resets it on the real close.
        }

        private void Reset()
        {
            if (adapter == null)
                return;
            foreach (var transferable in adapter.GetAllTransferables())
            {
                transferable.AdjustTo(0);
            }
            NotifyChanged();
            TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.ResetAll".Loc());
            RefreshModel();
            AnnounceCurrentItem();
        }

        private bool AllowQuantityShortcuts()
        {
            RefreshModel();
            RegionKind? kind = KindOf(Model.RegionIndex);
            if (kind == RegionKind.Items)
                return true;
            if (kind != RegionKind.Pawns)
                return false;
            TransferableOneWay transferable = CurrentTransferable();
            return transferable != null && transferable.MaxCount > 1;
        }

        private void AdjustQuantityBy(int delta)
        {
            TransferableQuantityHelper.AdjustQuantity(CurrentTransferable, delta, NotifyChanged);
        }

        /// <summary>The transferable at a Pawns-region row index, or null for a header or out-of-range row.</summary>
        private TransferableOneWay CurrentPawnRowTransferable(int index)
        {
            return index >= 0 && index < pawnRows.Count ? pawnRows[index].Transferable : null;
        }

        /// <summary>The transferable under the row cursor; null on a column-header row, a section header, or outside a transferable region.</summary>
        private TransferableOneWay CurrentTransferable()
        {
            RegionKind? kind = KindOf(Model.RegionIndex);
            if (kind != RegionKind.Pawns && kind != RegionKind.Items)
                return null;
            int row = CurrentContentRow();
            if (kind == RegionKind.Pawns)
                return CurrentPawnRowTransferable(row);
            return row >= 0 && row < itemRows.Count ? itemRows[row] : null;
        }

        private Pawn SelectedPawn()
        {
            int row = CurrentContentRow();
            if (KindOf(Model.RegionIndex) == RegionKind.Pawns)
                return CurrentPawnRowTransferable(row)?.AnyThing as Pawn;
            return CaravanUIHelper.GetSelectedPawn(itemRows, row);
        }

        private void NotifyChanged()
        {
            adapter?.NotifyTransferablesChanged();
        }

        private float GetMassUsage()
        {
            var transferables = adapter?.GetAllTransferables();
            if (transferables == null || transferables.Count == 0)
                return 0f;
            float total = 0f;
            foreach (var t in transferables)
            {
                if (t.CountToTransfer > 0 && t.AnyThing != null)
                {
                    total += t.AnyThing.GetStatValue(StatDefOf.Mass) * t.CountToTransfer;
                }
            }
            return total;
        }

        private static string TransferableLabel(TransferableOneWay transferable)
        {
            if (transferable == null)
                return "";
            if (transferable.AnyThing is Pawn pawn)
            {
                if (transferable.MaxCount > 1)
                {
                    return PawnLabelHelper.BuildGroupedPawnLabel(pawn, transferable.MaxCount);
                }
                return pawn.LabelShortCap.StripTags();
            }
            return transferable.LabelCap.StripTags();
        }
    }
}
