using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Shared base of the one-way transfer screens (<see cref="CaravanFormationScope"/>,
    /// <see cref="SplitCaravanScope"/>, <see cref="TransportPodLoadingScope"/>), each of which
    /// shows its dialog in one of two views picked by <see cref="TransferView"/>. The subclass
    /// keeps every mutation and dialog action; this class owns only what differs between views.
    ///
    /// Content regions are laid out as the tab lists first (indices 0 to
    /// <see cref="ListRegionCount"/> - 1, in the dialog's own tab order), then an optional
    /// Summary. The TABLE view shows each list as a sortable table. The CLASSIC view (the
    /// default) shows every region as a flat list: Left/Right switch tabs, Tab cycles the tab
    /// lists, Summary and the Buttons region, and each row speaks its name and count with the
    /// columns, icons and hover text a sighted player sees riding the Extras channel. Classic
    /// rows are read off each widget's drawn order every refresh, so the focused row is always
    /// the row on screen under vanilla's search box and sort dropdowns.
    ///
    /// Both views scroll the focused row into view (<see cref="TransferableWidgetLayout"/>), and
    /// Shift+Enter on a tab list belongs to the subclass's take-maximum claim rather than the
    /// screen's proceed chord. Ctrl+Tab swaps the view on the open dialog through
    /// <see cref="ScopeForWindow.Swap"/> and remembers it.
    /// </summary>
    public abstract class TransferScreenScope : ScreenScope
    {
        protected readonly bool Classic;
        protected readonly bool SwappedIn;

        private readonly SectionPrefixTracker sectionPrefix = new SectionPrefixTracker(trackSilentRows: false, speakFirstLanding: true);
        private readonly Dictionary<TransferableOneWay, string> sectionTitles = new Dictionary<TransferableOneWay, string>();
        private int sectionPrefixRegion = -1;

        /// <summary>The tab list last visited, the one Tab returns to.</summary>
        private int listRegion;

        protected TransferScreenScope(bool classic, bool swappedIn)
        {
            Classic = classic;
            SwappedIn = swappedIn;

            Claim("transfer.swapView", delegate { SwapView(); });
            if (!classic)
            {
                return;
            }
            // Reached because the base's Left/Right claims stand down on a flat row.
            Claim(SharedMenuGrammar.NextHorizontal, delegate { SwitchTab(1); }, when: InListRegion);
            Claim(SharedMenuGrammar.PreviousHorizontal, delegate { SwitchTab(-1); }, when: InListRegion);
            Claim("transfer.quantity.increaseTen", delegate { StepQuantity(10); }, when: CanStepQuantity);
            Claim("transfer.quantity.decreaseTen", delegate { StepQuantity(-10); }, when: CanStepQuantity);
            Claim("transfer.quantity.increaseHundred", delegate { StepQuantity(100); }, when: CanStepQuantity);
            Claim("transfer.quantity.decreaseHundred", delegate { StepQuantity(-100); }, when: CanStepQuantity);
            Claim("transfer.quantity.max", delegate { SetQuantityExtreme(max: true); }, when: CanStepQuantity);
            Claim("transfer.quantity.min", delegate { SetQuantityExtreme(max: false); }, when: CanStepQuantity);
        }

        // ------------------------------------------------------------------
        // The subclass contract.
        // ------------------------------------------------------------------

        /// <summary>How many tab lists lead the content regions.</summary>
        protected abstract int ListRegionCount { get; }

        /// <summary>Whether a Summary region follows the tab lists.</summary>
        protected abstract bool HasSummaryRegion { get; }

        /// <summary>The transferable a tab-list row shows, or null for a section header, a row out of range, or a row with no vanilla widget behind it.</summary>
        protected abstract TransferableOneWay ListRowAt(int region, int row);

        /// <summary>The dialog's live widget drawing a tab list, or null when none draws it.</summary>
        protected abstract TransferableOneWayWidget ListWidget(int region);

        /// <summary>The column set of a tab list, or null when it has no vanilla columns.</summary>
        protected abstract TransferableTableColumns.WidgetView ListView(int region);

        /// <summary>The table view's column count; <see cref="ContentColumnCount"/> is 0 throughout the classic view.</summary>
        protected abstract int TableColumnCount(int region);

        /// <summary>This screen in the other view, on the same dialog.</summary>
        protected abstract TransferScreenScope CreateOtherView();

        /// <summary>Whether the quantity steps apply to the current row; the subclass's own +/- gate.</summary>
        protected abstract bool CanStepQuantity();

        /// <summary>Steps the current row's count, through the subclass's own gates.</summary>
        protected abstract void StepQuantity(int delta);

        /// <summary>Sets the current row's count to its maximum or zero, through the subclass's own gates.</summary>
        protected abstract void SetQuantityExtreme(bool max);

        /// <summary>Whether Shift+Enter on this tab list takes the maximum; false where the list has no such claim.</summary>
        protected virtual bool ListRegionTakesMaximum(int region)
        {
            return true;
        }

        // ------------------------------------------------------------------
        // Shared helpers for the subclasses.
        // ------------------------------------------------------------------

        protected int SummaryRegionIndex
        {
            get { return HasSummaryRegion ? ListRegionCount : -1; }
        }

        protected bool InSummaryRegion
        {
            get { return HasSummaryRegion && Model.RegionIndex == ListRegionCount; }
        }

        /// <summary>
        /// The data row under the cursor in the current content region: a table's row less its
        /// header row, a flat list's index, and -1 on an empty region or outside the content.
        /// </summary>
        protected int CurrentContentRow()
        {
            if (Model.RegionIndex >= ContentRegionCount)
            {
                return -1;
            }
            TableModel table = Model.CurrentTable;
            if (table != null)
            {
                return table.Rows.Index - 1;
            }
            ListModel list = Model.CurrentRegion;
            return list == null || list.IsEmpty ? -1 : list.Index;
        }

        /// <summary>
        /// A classic tab list in the widget's drawn order, falling back to <paramref name="fallback"/>
        /// when the widget cannot be read. Section titles are kept for the crossing announcement.
        /// </summary>
        protected List<TransferableOneWay> ClassicListRows(TransferableOneWayWidget widget, IEnumerable<CaravanUIHelper.PawnSectionRow> fallback)
        {
            var rows = new List<TransferableOneWay>();
            List<TransferableWidgetLayout.DrawnRow> drawn = TransferableWidgetLayout.Rows(widget);
            if (drawn != null)
            {
                for (int i = 0; i < drawn.Count; i++)
                {
                    rows.Add(drawn[i].Transferable);
                    sectionTitles[drawn[i].Transferable] = drawn[i].SectionTitle;
                }
                return rows;
            }
            string title = null;
            foreach (CaravanUIHelper.PawnSectionRow row in fallback)
            {
                if (row.IsHeader)
                {
                    title = row.Header;
                    continue;
                }
                rows.Add(row.Transferable);
                sectionTitles[row.Transferable] = title;
            }
            return rows;
        }

        /// <summary>The Buttons-region entry that swaps views, for the end of a subclass's declared actions.</summary>
        protected void AddSwapAction(List<ScreenAction> actions)
        {
            actions.Add(new ScreenAction(
                (Classic ? "RimWorldAccess.Trade.View.SwitchToTable" : "RimWorldAccess.Trade.View.SwitchToClassic").Translate().ToString(),
                SwapView, "transfer.swapView"));
        }

        /// <summary>What a swapped-in view says in place of the dialog's opening line.</summary>
        protected string SwappedInAnnouncement()
        {
            return (Classic ? "RimWorldAccess.Trade.View.Classic" : "RimWorldAccess.Trade.View.Table").Translate().ToString();
        }

        // ------------------------------------------------------------------
        // Views.
        // ------------------------------------------------------------------

        internal static bool OpensClassic
        {
            get
            {
                RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
                return settings == null || settings.DefaultTransferView == TransferView.Classic;
            }
        }

        private void SwapView()
        {
            Window window = OwnedWindow;
            if (window == null)
            {
                return;
            }
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings != null)
            {
                settings.DefaultTransferView = Classic ? TransferView.Table : TransferView.Classic;
                LoadedModManager.GetMod<RimWorldAccessMod_Settings>()?.WriteSettings();
            }
            ScopeForWindow.Swap(window, w => CreateOtherView());
        }

        // ------------------------------------------------------------------
        // Content contract pieces that differ by view.
        // ------------------------------------------------------------------

        protected sealed override int ContentColumnCount(int region)
        {
            return Classic ? 0 : TableColumnCount(region);
        }

        protected sealed override ElementDescription DescribeContentItem(int region, int index)
        {
            ElementDescription d = DescribeTransferItem(region, index);
            if (!Classic)
            {
                return d;
            }
            // Enter toggles, opens the chooser or the breakdown on every row, so no row arms the proceed confirm.
            d.KeepsAccept = true;
            if (region < ListRegionCount && string.IsNullOrEmpty(d.Extras))
            {
                TransferableOneWay t = ListRowAt(region, index);
                if (t != null)
                {
                    d.Extras = TransferRowDetail.Compose(t, ListView(region), ListWidget(region));
                }
            }
            return d;
        }

        /// <summary>The row as the subclass describes it; the classic view adds Extras afterwards.</summary>
        protected abstract ElementDescription DescribeTransferItem(int region, int index);

        /// <summary>Classic typeahead stays on the tab being read, as the tab lists always did.</summary>
        protected override bool ContentRegionSearchable(int region)
        {
            if (region >= ListRegionCount)
            {
                return false;
            }
            return !Classic || region == Model.RegionIndex;
        }

        /// <summary>The row's name alone, so a count or a stat never matches a search.</summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            TransferableOneWay t = region < ListRegionCount ? ListRowAt(region, row) : null;
            if (t == null)
            {
                return base.ContentRowSearchText(region, row);
            }
            return t.AnyThing is Pawn pawn && t.MaxCount == 1 ? pawn.LabelShortCap.StripTags() : t.LabelCap.StripTags();
        }

        protected override bool ContentOwnsActivateDefault
        {
            get
            {
                int region = Model.RegionIndex;
                return region < ListRegionCount && ListRegionTakesMaximum(region)
                    && ListRowAt(region, CurrentContentRow()) != null;
            }
        }

        public override bool RememberTabPositions
        {
            get { return Classic || base.RememberTabPositions; }
        }

        /// <summary>Tab and Left/Right each reach a different set of regions, so no single "section x of y" fits.</summary>
        protected override bool RegionNamesCarryPosition
        {
            get { return Classic || base.RegionNamesCarryPosition; }
        }

        /// <summary>Crossing into a new pawn section speaks its title, the header a sighted player sees above the rows.</summary>
        protected override string AnnouncePrefix(int region, int index)
        {
            if (!Classic || region >= ListRegionCount)
            {
                return null;
            }
            if (region != sectionPrefixRegion)
            {
                sectionPrefix.Reset();
                sectionPrefixRegion = region;
            }
            TransferableOneWay t = ListRowAt(region, index);
            if (t == null || !sectionTitles.TryGetValue(t, out string title))
            {
                title = null;
            }
            return sectionPrefix.Cross(title);
        }

        /// <summary>A region landing speaks no section title, so the landed row's section counts as heard.</summary>
        private void PrimeSectionPrefix(int region)
        {
            sectionPrefix.Reset();
            sectionPrefixRegion = region;
            TransferableOneWay t = region < ListRegionCount ? ListRowAt(region, CurrentContentRow()) : null;
            if (t != null && sectionTitles.TryGetValue(t, out string title))
            {
                sectionPrefix.Prime(title);
            }
        }

        protected override void OnCursorSettled(int region, int index)
        {
            if (region >= ListRegionCount)
            {
                return;
            }
            listRegion = region;
            // A failed scroll is cosmetic; it must never swallow the move that triggered it.
            try
            {
                TransferableWidgetLayout.ScrollIntoView(ListWidget(region), ListRowAt(region, CurrentContentRow()));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Transfer row scroll failed", ex);
            }
        }

        // ------------------------------------------------------------------
        // Classic navigation.
        // ------------------------------------------------------------------

        private bool InListRegion()
        {
            RefreshModel();
            return Model.RegionIndex < ListRegionCount;
        }

        /// <summary>Left/Right: the neighbouring tab, wrapping.</summary>
        private void SwitchTab(int direction)
        {
            RefreshModel();
            if (RelocatedThisRefresh)
            {
                return;
            }
            if (ListRegionCount < 2)
            {
                MenuHelper.PlayEdgeTone();
                return;
            }
            int current = Model.RegionIndex;
            for (int step = 1; step < ListRegionCount; step++)
            {
                int target = ((current + direction * step) % ListRegionCount + ListRegionCount) % ListRegionCount;
                if (Model.IsRegionNavigable(target))
                {
                    LandIn(target);
                    return;
                }
            }
            MenuHelper.PlayEdgeTone();
        }

        /// <summary>Classic Tab: the tab lists (back to the tab left), Summary, then the Buttons region, wrapping.</summary>
        protected override void MoveRegion(bool forward)
        {
            if (!Classic)
            {
                base.MoveRegion(forward);
                return;
            }
            RefreshModel();
            if (RelocatedThisRefresh)
            {
                return;
            }
            if (Model.RegionIndex < ListRegionCount)
            {
                listRegion = Model.RegionIndex;
            }
            var stops = new List<int>();
            if (Model.IsRegionNavigable(listRegion))
            {
                stops.Add(listRegion);
            }
            if (HasSummaryRegion && Model.IsRegionNavigable(SummaryRegionIndex))
            {
                stops.Add(SummaryRegionIndex);
            }
            if (HasActionsRegion())
            {
                stops.Add(ActionsRegionIndex());
            }
            int at = stops.IndexOf(Model.RegionIndex);
            if (stops.Count == 0 || (stops.Count == 1 && at == 0))
            {
                MenuHelper.PlayEdgeTone();
                return;
            }
            int next = at < 0 ? 0 : ((at + (forward ? 1 : -1)) % stops.Count + stops.Count) % stops.Count;
            LandIn(stops[next]);
        }

        /// <summary>The landing every classic region move shares: the model jump, then the same sound and utterance a Tab press makes.</summary>
        private void LandIn(int target)
        {
            MoveResult moved = Model.MoveToRegion(target);
            if (!MenuHelper.SoundMove(moved))
            {
                return;
            }
            TypeaheadReset();
            OnRegionChanged(moved);
            PrimeSectionPrefix(target);
            TabSwitchSound?.PlayOneShotOnCamera();
            NotifyCursorSettled();
            AnnounceRegion();
        }
    }

    /// <summary>
    /// What a classic transfer row carries beyond its name and count, in the order vanilla draws
    /// it right to left: the data columns, the pawn's ideoligion, xenotype and slave marks, the
    /// animal and mech icons, then the hover description.
    /// </summary>
    internal static class TransferRowDetail
    {
        internal static string Compose(TransferableOneWay t, TransferableTableColumns.WidgetView view, TransferableOneWayWidget widget)
        {
            var parts = new List<string>();
            if (view != null)
            {
                bool isPawn = t.AnyThing is Pawn;
                int count = TransferableTableColumns.ColumnCount(view.Profile);
                for (int i = 0; i < count; i++)
                {
                    TransferableTableColumns.ColumnKind kind = TransferableTableColumns.KindAt(view.Profile, i);
                    string text = TransferableTableColumns.CellText(kind, t, view);
                    if (!string.IsNullOrEmpty(text))
                    {
                        parts.Add(Labelled(TransferableTableColumns.ColumnInfo(kind, isPawn).Label, text));
                    }
                }
            }
            if (t.AnyThing is Pawn pawn)
            {
                AddPawnMarks(parts, pawn, widget);
            }
            string description = t.HasAnyThing ? GizmoTextUtility.FlattenNewlines(t.TipDescription.StripTags()) : "";
            if (!string.IsNullOrEmpty(description))
            {
                parts.Add(description.Trim().TrimEnd('.'));
            }
            return string.Join(". ", parts);
        }

        private static string Labelled(string label, string value)
        {
            return "RimWorldAccess.Caravan.Classic.ColumnValue".Translate(label, value).ToString();
        }

        /// <summary>TransferableOneWayWidget.DoRow's icons and TransferableUIUtility.DoExtraIcons, each read as its own tooltip.</summary>
        private static void AddPawnMarks(List<string> parts, Pawn pawn, TransferableOneWayWidget widget)
        {
            if (widget != null && widget.drawIdeo && pawn.Ideo != null)
            {
                parts.Add(Labelled("Ideo".Translate().CapitalizeFirst(), pawn.Ideo.name));
            }
            if (widget != null && widget.drawXenotype && pawn.genes?.Xenotype != null)
            {
                parts.Add(Labelled("Xenotype".Translate().CapitalizeFirst(), pawn.genes.XenotypeLabelCap));
            }
            if (pawn.IsSlave && pawn.guest != null)
            {
                parts.Add(pawn.guest.GetLabel().CapitalizeFirst());
            }
            if (pawn.RaceProps.Animal)
            {
                if (pawn.IsCaravanRideable())
                {
                    AddTip(parts, CaravanRideableUtility.GetIconTooltipText(pawn));
                }
                if (pawn.relations?.GetFirstDirectRelationPawn(PawnRelationDefOf.Bond) != null)
                {
                    AddTip(parts, TrainableUtility.GetIconTooltipText(pawn));
                }
                if (pawn.health.hediffSet.HasHediff(HediffDefOf.Pregnant, mustBeVisible: true))
                {
                    AddTip(parts, PawnColumnWorker_Pregnant.GetTooltipText(pawn));
                }
                if (pawn.health.hediffSet.AnyHediffMakesSickThought)
                {
                    var sick = new List<string>();
                    foreach (Hediff h in pawn.health.hediffSet.hediffs)
                    {
                        if (h.def.makesSickThought)
                        {
                            sick.Add(h.LabelCap);
                        }
                    }
                    parts.Add(Labelled("CaravanAnimalSick".Translate(), string.Join(", ", sick)));
                }
            }
            else if (ModsConfig.BiotechActive && pawn.IsColonyMech)
            {
                Pawn overseer = pawn.GetOverseer();
                if (overseer != null)
                {
                    AddTip(parts, "MechOverseer".Translate(overseer));
                }
            }
            else if (CaravanBonusUtility.HasCaravanBonus(pawn))
            {
                AddTip(parts, CaravanBonusUtility.GetIconTooltipText(pawn));
            }
        }

        private static void AddTip(List<string> parts, string tip)
        {
            if (string.IsNullOrEmpty(tip))
            {
                return;
            }
            string flat = GizmoTextUtility.FlattenNewlines(tip.StripTags()).Trim().TrimEnd('.');
            if (!string.IsNullOrEmpty(flat))
            {
                parts.Add(flat);
            }
        }
    }
}
