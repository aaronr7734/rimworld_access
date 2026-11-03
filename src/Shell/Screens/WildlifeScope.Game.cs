using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The wildlife table: every wild animal on the map, in one TABLE REGION with the same columns
    /// vanilla's Wildlife tab shows. The mod owns no window here — the surface is the windowless
    /// <see cref="WildlifeMenuState"/> — so the scope rides the focus stack through
    /// <see cref="WildlifeScopeMirror"/>.
    ///
    /// Row/column navigation, Home/End, Enter-on-header sort and the Alt+S alias are base
    /// ScreenScope behavior. Painting (Shift+Up/Down single-cell, Shift+Home/End bulk-to-edge,
    /// Ctrl+Shift+Home/End entire column) has no base equivalent and stays a per-screen claim set.
    ///
    /// All row, cursor and search state is instance state, so popping the scope IS the reset;
    /// <see cref="WildlifeMenuState"/> survives only as the bridge <see cref="WildlifeMenuPatch"/>
    /// and the map-ambient guards read. Row identity is
    /// <see cref="WildlifeMenuHelper.GetAnimalName"/> (name without activity), while the Name
    /// COLUMN's cell value carries the animal's current activity as well.
    ///
    /// The real <see cref="MainTabWindow_Wildlife"/> stays open, and this scope supplies the focused
    /// cell to <see cref="PawnTableFocusDriver"/> via <see cref="IPawnTableFocusSource"/>.
    /// </summary>
    public sealed class WildlifeScope : ScreenScope, IPawnTableFocusSource
    {
        private const int WildlifeRegion = 0;

        private readonly List<Pawn> wildlifeList = new List<Pawn>();
        private List<Pawn> defaultOrder;
        private bool announcedOpen;

        public WildlifeScope()
        {
            Claim(SharedMenuGrammar.Cancel, delegate { Close(); });

            Claim("wildlife.infoCard", delegate { OpenInfoCard(); });

            // Painting, one action disambiguated by key: Shift+Up/Down single-cell, Shift+Home/End
            // bulk-to-edge, Ctrl+Shift+Home/End entire column. No base equivalent.
            Claim("wildlife.paintUp", delegate { PaintUp(); });
            Claim("wildlife.paintDown", delegate { PaintDown(); });
            Claim("wildlife.paintToFirst", delegate { PaintToFirst(); });
            Claim("wildlife.paintToLast", delegate { PaintToLast(); });
            Claim("wildlife.paintEntireColumn",
                delegate (KeyEventSnapshot e) { PaintEntireColumn(e.Key == KeyCode.Home); });
        }

        public override string Name
        {
            get { return "wildlife"; }
        }

        /// <summary>Shared cross-region typeahead.</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Windowless modal: this scope owns Escape itself (close the menu / clear a search).</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        // ScreenScope content contract.

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>Vanilla's own Wildlife tab label, looked up by name; no new translation key.</summary>
        protected override string ContentRegionName(int region)
        {
            MainButtonDef def = DefDatabase<MainButtonDef>.GetNamedSilentFail("Wildlife");
            return def != null ? def.LabelCap.Resolve() : "Wildlife";
        }

        protected override int ContentColumnCount(int region)
        {
            return WildlifeMenuHelper.GetTotalColumnCount();
        }

        protected override int ContentItemCount(int region)
        {
            return wildlifeList.Count;
        }

        /// <summary>
        /// One-time population: the roster is snapshotted and never silently re-fetched, since
        /// re-querying the map every refresh would discard an active sort or the cursor position.
        /// Rebuilds only when the cached list is empty.
        /// </summary>
        protected override void RefreshContent()
        {
            if (wildlifeList.Count > 0)
                return;
            Map map = Find.CurrentMap;
            if (map == null)
                return;

            List<Pawn> initial = map.mapPawns.AllPawns
                .Where(p => p.Spawned &&
                           (p.Faction == null || p.Faction == Faction.OfInsects) &&
                           p.AnimalOrWildMan() &&
                           !p.Position.Fogged(p.Map) &&
                           !p.IsPrisonerInPrisonCell())
                .ToList();
            if (initial.Count == 0)
                return;

            WildlifeMenuHelper.InitColumnDefs();

            // Vanilla's default sort is PawnTable_Wildlife.LabelSortFunction (body size descending,
            // then label), not a column-index sort.
            initial = initial
                .OrderByDescending(p => p.RaceProps?.baseBodySize ?? 0)
                .ThenBy(p => p.def.label)
                .ToList();

            wildlifeList.AddRange(initial);
            defaultOrder = new List<Pawn>(wildlifeList);
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index >= 0 && index < wildlifeList.Count)
            {
                d.Label = WildlifeMenuHelper.GetAnimalName(wildlifeList[index]);
            }
            return d;
        }

        /// <summary>Enter on a non-interactive column says it is read only.</summary>
        protected override void ActivateContentItem(int region, int index)
        {
            if (index < 0 || index >= wildlifeList.Count)
                return;
            AnnounceCurrentCellReadOnly();
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            return new TableColumnInfo(
                WildlifeMenuHelper.GetColumnName(column),
                WildlifeMenuHelper.GetColumnTooltip(null, column),
                WildlifeMenuHelper.IsColumnSortable(column));
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            if (row < 0 || row >= wildlifeList.Count)
                return "";
            return WildlifeMenuHelper.GetColumnValue(wildlifeList[row], column);
        }

        protected override bool TryGetPawnCell(int region, int row, int column, out PawnColumnDef def, out Pawn pawn)
        {
            bool inRange = row >= 0 && row < wildlifeList.Count;
            def = inRange ? WildlifeMenuHelper.GetDef(column) : null;
            pawn = inRange ? wildlifeList[row] : null;
            return def != null;
        }

        /// <summary>Registry cell tips for Unknown-classified (typically modded) columns only; the curated columns speak through their own value phrasing.</summary>
        protected override string ContentCellTip(int region, int row, int column)
        {
            if (row < 0 || row >= wildlifeList.Count)
                return null;
            return WildlifeMenuHelper.GetUnknownCellTip(wildlifeList[row], column);
        }

        /// <summary>Enter on an interactive column: Name jumps to the animal, Hunt/Tame toggle designations.</summary>
        protected override bool ActivateContentCell(int region, int row, int column)
        {
            if (row < 0 || row >= wildlifeList.Count)
                return false;
            if (WildlifeMenuHelper.GetColumnType(column) == WildlifeMenuHelper.ColumnType.Unknown)
                return ActivatePawnCellTableless(region, row, column);
            if (!WildlifeMenuHelper.IsColumnInteractive(column))
                return false;

            Pawn pawn = wildlifeList[row];
            WildlifeMenuHelper.ColumnType type = WildlifeMenuHelper.GetColumnType(column);
            switch (type)
            {
                case WildlifeMenuHelper.ColumnType.Name:
                    JumpToAnimalOnMap(pawn);
                    return true;
                case WildlifeMenuHelper.ColumnType.Hunt:
                    ToggleHunt(pawn);
                    return true;
                case WildlifeMenuHelper.ColumnType.Tame:
                    ToggleTame(pawn);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Re-orders for a new sort state through the game's own column comparers; a cleared sort
        /// restores the vanilla body-size/label default captured at open.
        /// </summary>
        protected override int ApplyContentSort(int region, int column, SortCycleResult cycle, int currentRow)
        {
            Pawn currentPawn = currentRow >= 0 && currentRow < wildlifeList.Count ? wildlifeList[currentRow] : null;
            List<Pawn> reordered = cycle == SortCycleResult.Cleared
                ? new List<Pawn>(defaultOrder ?? wildlifeList)
                : WildlifeMenuHelper.SortWildlifeByColumn(wildlifeList, column, cycle == SortCycleResult.SortedDescending);

            wildlifeList.Clear();
            wildlifeList.AddRange(reordered);

            if (currentPawn == null)
                return 0;
            int index = wildlifeList.IndexOf(currentPawn);
            return index >= 0 ? index : 0;
        }

        // Lifecycle.

        public override void OnPush()
        {
            base.OnPush();
            wildlifeList.Clear();
            defaultOrder = null;
            TypeaheadReset();
            announcedOpen = false;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;
            if (wildlifeList.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Animals.Wildlife.Menu.NoWildlife".Loc());
                return;
            }
            ScreenReturn.LandTableCursor(Name, Model.CurrentTable, wildlifeList);
            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            string announcement = "RimWorldAccess.Animals.Wildlife.Menu.OpeningTitle".Translate(wildlifeList.Count).ToString();
            TolkHelper.SpeakData(announcement);
            AnnounceCurrentItem();
        }

        // Typeahead is the base engine's; Escape with no search closes the menu via the plain
        // Cancel claim above, the base's search-clear claim winning first.

        // Cell actions.

        private void OpenInfoCard()
        {
            RefreshModel();
            TableModel table = Model.CurrentTable;
            int row = table == null ? -1 : table.Rows.Index - 1;
            Pawn animal = row >= 0 && row < wildlifeList.Count ? wildlifeList[row] : null;
            if (animal != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(animal));
            }
            else
            {
                InfoCardState.SpeakNoInfoCardAvailable();
            }
        }

        private void JumpToAnimalOnMap(Pawn pawn)
        {
            ScreenReturn.JumpToPawn(Name, MainTabWindowLink.Wildlife, Model.CurrentTable, Close,
                pawn, "RimWorldAccess.Animals.Wildlife.Menu.NotOnMap");
        }

        private void ToggleHunt(Pawn pawn)
        {
            bool isNowMarked = WildlifeMenuHelper.ToggleHuntDesignation(pawn);
            (isNowMarked ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            AfterCellEdit();
        }

        private void ToggleTame(Pawn pawn)
        {
            bool? result = WildlifeMenuHelper.ToggleTameDesignation(pawn);
            if (result == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Animals.Wildlife.Menu.CannotTame".Loc(), SpeechPriority.High);
                return;
            }
            (result.Value ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            AfterCellEdit();
        }

        /// <summary>
        /// Re-sorts in place when the table is sorted by the just-edited column, keeping the cursor
        /// at the same ROW POSITION rather than following the edited item, then speaks the resulting
        /// cell through the shared composer.
        /// </summary>
        private void AfterCellEdit()
        {
            TableModel table = Model.CurrentTable;
            if (table != null && table.HasActiveSort && table.SortColumnIndex == table.ColumnIndex)
            {
                List<Pawn> resorted = WildlifeMenuHelper.SortWildlifeByColumn(wildlifeList, table.ColumnIndex, table.SortDescending);
                wildlifeList.Clear();
                wildlifeList.AddRange(resorted);
                int currentRow = table.Rows.Index - 1;
                int clamped = System.Math.Min(currentRow, wildlifeList.Count - 1);
                if (clamped >= 0)
                {
                    table.Rows.MoveTo(clamped + 1);
                }
                // The resort changed which animal is under the cursor, not the column, so no tooltip
                // repeat.
                AnnounceCurrent(CellAxis.Row);
                return;
            }
            // Toggle doctrine: the cursor didn't move, so speak only the new state.
            AnnounceCurrentCellStateChange();
        }

        // Painting.

        /// <summary>Next data-row index for painting: search-match-aware like row navigation, else a plain wrap.</summary>
        private int NextPaintRow(int currentRow, int delta)
        {
            int count = wildlifeList.Count;
            if (TypeaheadHasActiveSearch)
            {
                int matchRow = TypeaheadMatchRowInRegion(WildlifeRegion, currentRow, delta);
                if (matchRow >= 0 && matchRow < count)
                    return matchRow;
            }
            return ((currentRow + delta) % count + count) % count;
        }

        private void PaintUp()
        {
            PaintSingle(-1);
        }

        private void PaintDown()
        {
            PaintSingle(1);
        }

        private void PaintSingle(int delta)
        {
            if (wildlifeList.Count <= 1)
                return;
            RefreshModel();
            TableModel table = Model.CurrentTable;
            if (table == null)
                return;
            int col = table.ColumnIndex;
            if (!WildlifeMenuHelper.CanPaintColumn(col))
            {
                MenuHelper.SpeakCannotPaintColumn();
                return;
            }

            int sourceRow = table.Rows.Index - 1;
            Pawn sourcePawn = wildlifeList[sourceRow];
            bool brushValue = WildlifeMenuHelper.GetPaintableValue(sourcePawn, col);

            int targetRow = NextPaintRow(sourceRow, delta);
            if (targetRow < 0 || targetRow >= wildlifeList.Count)
                return;
            table.Rows.MoveTo(targetRow + 1);
            Pawn targetPawn = wildlifeList[targetRow];

            string colName = WildlifeMenuHelper.GetColumnName(col);
            string valueLabel = WildlifeMenuHelper.GetPaintValueLabel(col, brushValue);
            string pos = MenuHelper.FormatPosition(targetRow, wildlifeList.Count);

            bool targetValue = WildlifeMenuHelper.GetPaintableValue(targetPawn, col);
            if (targetValue == brushValue)
            {
                TolkHelper.Speak("RimWorldAccess.Animals.Paint.Single.CellAlready".Loc(targetPawn.LabelShort, colName, valueLabel, pos));
                return;
            }

            bool applied = WildlifeMenuHelper.SetPaintableValue(targetPawn, col, brushValue);
            SoundDef sound = applied ? WildlifeMenuHelper.GetPaintSound(col, brushValue) : SoundDefOf.ClickReject;
            sound.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Animals.Paint.Single.CellApplied".Loc(targetPawn.LabelShort, colName, valueLabel, pos));
        }

        private void PaintToLast()
        {
            PaintBulk(towardFirst: false, entireColumn: false);
        }

        private void PaintToFirst()
        {
            PaintBulk(towardFirst: true, entireColumn: false);
        }

        private void PaintEntireColumn(bool towardFirst)
        {
            PaintBulk(towardFirst, entireColumn: true);
        }

        private void PaintBulk(bool towardFirst, bool entireColumn)
        {
            if (wildlifeList.Count == 0)
                return;
            RefreshModel();
            TableModel table = Model.CurrentTable;
            if (table == null)
                return;
            int col = table.ColumnIndex;
            if (!WildlifeMenuHelper.CanPaintColumn(col))
            {
                MenuHelper.SpeakCannotPaintColumn();
                return;
            }

            int currentRow = table.Rows.Index - 1;
            string colName = WildlifeMenuHelper.GetColumnName(col);

            int startRow, endRow;
            if (entireColumn)
            {
                startRow = 0;
                endRow = wildlifeList.Count - 1;
            }
            else if (towardFirst)
            {
                startRow = 0;
                endRow = currentRow;
            }
            else
            {
                startRow = currentRow;
                endRow = wildlifeList.Count - 1;
            }

            Pawn sourcePawn = wildlifeList[currentRow];
            bool brushValue = WildlifeMenuHelper.GetPaintableValue(sourcePawn, col);
            string valueLabel = WildlifeMenuHelper.GetPaintValueLabel(col, brushValue);
            SoundDef paintSound = WildlifeMenuHelper.GetPaintSound(col, brushValue);

            var changed = new List<string>();
            for (int i = startRow; i <= endRow; i++)
            {
                Pawn pawn = wildlifeList[i];
                bool currentValue = WildlifeMenuHelper.GetPaintableValue(pawn, col);
                if (currentValue != brushValue)
                {
                    if (WildlifeMenuHelper.SetPaintableValue(pawn, col, brushValue))
                        changed.Add(pawn.LabelShort);
                }
            }

            table.Rows.MoveTo((towardFirst ? startRow : endRow) + 1);

            if (changed.Count > 0)
            {
                BulkSoundQueue.Queue(changed.Count, paintSound);
                TolkHelper.Speak("RimWorldAccess.Animals.Paint.Bulk.CellApplied".Loc(colName, valueLabel, MenuHelper.FormatNameList(changed)));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Animals.Wildlife.Paint.Bulk.CellAlreadyAll".Loc(colName, valueLabel));
            }
        }

        // IPawnTableFocusSource.

        /// <summary>Alt+Shift+J routes on the real vanilla table drawing underneath, which this scope reads but was never attached to.</summary>
        protected override Window PointerSurface
        {
            get { return PawnTableFocusDriver.OpenTabWindowFor(this); }
        }

        private protected override void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            PawnTableExtrasRouter.AddRouteCandidates(
                0, wildlifeList, ContentColumnCount(0), WildlifeMenuHelper.GetDef, candidates, targets);
        }

        Type IPawnTableFocusSource.TabWindowType
        {
            get { return typeof(MainTabWindow_Wildlife); }
        }

        Pawn IPawnTableFocusSource.FocusedTablePawn
        {
            get
            {
                TableModel table = Model.CurrentTable;
                int row = table != null ? table.Rows.Index - 1 : -1;
                return row >= 0 && row < wildlifeList.Count ? wildlifeList[row] : null;
            }
        }

        PawnColumnDef IPawnTableFocusSource.FocusedTableColumn
        {
            get
            {
                TableModel table = Model.CurrentTable;
                return table != null ? WildlifeMenuHelper.GetDef(table.ColumnIndex) : null;
            }
        }

        bool IPawnTableFocusSource.FocusedOnHeaderRow
        {
            get
            {
                TableModel table = Model.CurrentTable;
                return table != null && table.Rows.Index == 0;
            }
        }

        // Close.

        private void Close()
        {
            WildlifeMenuState.Close();
        }
    }

    /// <summary>
    /// Keeps <see cref="WildlifeScope"/> in lockstep with
    /// <see cref="WildlifeMenuState.IsActive"/>, reconciled every OnGUI pass AFTER
    /// <see cref="GizmoScopeMirror"/> so the two coexist. Stands down while an info card is open,
    /// which <see cref="WildlifeScope.OpenInfoCard"/> can open. IsActive has exactly one setter
    /// path and nothing else spawns from inside this screen, so no further co-active gates apply.
    /// </summary>
    internal static class WildlifeScopeMirror
    {
        private static readonly WildlifeScope scope = new WildlifeScope();

        public static void Reconcile()
        {
            if (WildlifeMenuState.IsActive && !InfoCardState.IsActive)
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
