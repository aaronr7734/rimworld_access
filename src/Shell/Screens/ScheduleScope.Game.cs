using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Schedule tab (<see cref="MainTabWindow_Schedule"/>, which stays open and rendered) as
    /// two regions over one pawn list. Region 0 is the TABLE: rows are the live
    /// <see cref="PawnTable"/>'s own <c>PawnsListForReading</c>, columns are the table's own
    /// column list with the timetable column expanded into its 24 hour cells (the hour is the unit
    /// the player edits). Region 1, named by vanilla's own Allowed area column, is a LIST of the
    /// same pawns: each row speaks the pawn's area, Left/Right walk the area choices vanilla's own
    /// picker offers (Manage areas, Unrestricted, every assignable area), Enter applies the walked
    /// choice or opens the picker when none was walked. Tab moves between the regions and keeps
    /// the pawn.
    ///
    /// Every pawn is a row, including ones vanilla draws no hour cells for; those cells say why
    /// they cannot be painted, and Enter refuses with the same reason.
    ///
    /// The brush is <see cref="TimeAssignmentSelector.selectedAssignment"/> and row copy/paste uses
    /// <see cref="PawnColumnWorker_CopyPasteTimetable"/>'s private clipboard, so the on-screen
    /// palette and paste buttons stay in sync. Whether a paint chord means "paint hours" or "apply
    /// areas" follows the region the cursor is in.
    ///
    /// Two focus-ring sources, kept disjoint by the same condition on both sides (an hour column
    /// AND a row that draws hour cells): <see cref="ScheduleTimetableCellPatch"/> rings one hour
    /// slice, the shared <see cref="PawnTableFocusDriver"/> rings every other cell.
    /// </summary>
    public sealed class ScheduleScope : ScreenScope, IPawnTableFocusSource
    {
        private const int TableRegion = 0;
        private const int AreasRegion = 1;
        private const int HourCount = 24;

        // Registered in ShellActionInventory.Part3; this scope only claims them.
        private const string ActCopy = "schedule.copy";
        private const string ActPaste = "schedule.paste";
        private const string ActJumpFirstPawn = "schedule.jumpToFirstPawn";
        private const string ActJumpLastPawn = "schedule.jumpToLastPawn";
        private const string ActGridPaintUp = "schedule.grid.paintUp";
        private const string ActGridPaintDown = "schedule.grid.paintDown";
        private const string ActGridPaintLeft = "schedule.grid.paintLeft";
        private const string ActGridPaintRight = "schedule.grid.paintRight";
        private const string ActGridPaintToFirstHour = "schedule.grid.paintToFirstHour";
        private const string ActGridPaintToLastHour = "schedule.grid.paintToLastHour";
        private const string ActGridPaintToFirstPawn = "schedule.grid.paintToFirstPawn";
        private const string ActGridPaintToLastPawn = "schedule.grid.paintToLastPawn";
        private const string ActAreasApplyAbove = "schedule.areas.applyAbove";
        private const string ActAreasApplyBelow = "schedule.areas.applyBelow";
        private const string ActAreasContextMenu = "schedule.areas.contextMenu";
        private const string ActAreasPaintToFirstPawn = "schedule.areas.paintToFirstPawn";
        private const string ActAreasPaintToLastPawn = "schedule.areas.paintToLastPawn";
        private const string ActAreasPaintToAllTowardFirst = "schedule.areas.paintToAllTowardFirst";
        private const string ActAreasPaintToAllTowardLast = "schedule.areas.paintToAllTowardLast";

        private static readonly string[] BrushActions =
        {
            "schedule.selectBrush1", "schedule.selectBrush2", "schedule.selectBrush3",
            "schedule.selectBrush4", "schedule.selectBrush5", "schedule.selectBrush6",
            "schedule.selectBrush7", "schedule.selectBrush8", "schedule.selectBrush9",
            "schedule.selectBrush10",
        };

        private static readonly AccessTools.FieldRef<MainTabWindow_PawnTable, PawnTable> tableField =
            AccessTools.FieldRefAccess<MainTabWindow_PawnTable, PawnTable>("table");
        private static readonly AccessTools.FieldRef<PawnTable, Vector2> scrollPositionField =
            AccessTools.FieldRefAccess<PawnTable, Vector2>("scrollPosition");
        private static readonly AccessTools.FieldRef<PawnTable, List<float>> cachedRowHeightsField =
            AccessTools.FieldRefAccess<PawnTable, List<float>>("cachedRowHeights");
        // Vanilla's private static clipboard; sharing it keeps the on-screen paste buttons in sync.
        private static readonly FieldInfo clipboardField =
            AccessTools.Field(typeof(PawnColumnWorker_CopyPasteTimetable), "clipboard");

        private static readonly SoundDef BulkPaintSound = SoundDefOf.Designate_DragStandard_Changed_NoCam;

        /// <summary>One model column: a vanilla column def, plus which hour of the timetable strip it is (-1 when the whole cell is the column).</summary>
        private struct ColumnSlot
        {
            public PawnColumnDef Def;
            public int Hour;
        }

        private readonly MainTabWindow_Schedule window;
        private readonly List<Pawn> rows = new List<Pawn>();
        private readonly List<ColumnSlot> columns = new List<ColumnSlot>();

        // Model index of hour 0, or -1 when the table draws no timetable column.
        private int hourColumnStart = -1;
        // Vanilla's Allowed area column, or null when the table draws none (then there is no areas region).
        private PawnColumnDef areasDef;
        private int lastRegionIndex = -1;

        // The area choice walked with Left/Right in the areas region, valid only for areaCandidateRow.
        private static readonly object ManageAreasChoice = new object();
        private readonly List<object> areaChoices = new List<object>();
        private int areaCandidateRow = -1;
        private int areaCandidate = -1;

        private bool focusedRowDrawnThisPass;
        private bool initialCursorPlaced;

        public ScheduleScope(MainTabWindow_Schedule window)
        {
            this.window = window;

            Claim(ActCopy, e => CopyRow());
            Claim(ActPaste, e => PasteRow());
            Claim(ActJumpFirstPawn, e => MoveItemEdge(true));
            Claim(ActJumpLastPawn, e => MoveItemEdge(false));
            for (int i = 0; i < BrushActions.Length; i++)
            {
                int index = i;
                Claim(BrushActions[i], delegate { SelectBrush(index); });
            }

            // Hour columns: paint while moving, paint a range.
            Claim(ActGridPaintUp, e => PaintMoveRow(-1), when: OnHourColumn);
            Claim(ActGridPaintDown, e => PaintMoveRow(1), when: OnHourColumn);
            Claim(ActGridPaintLeft, e => PaintMoveHour(-1), when: OnHourColumn);
            Claim(ActGridPaintRight, e => PaintMoveHour(1), when: OnHourColumn);
            Claim(ActGridPaintToFirstHour, e => PaintHourRange(0), when: OnHourColumn);
            Claim(ActGridPaintToLastHour, e => PaintHourRange(HourCount - 1), when: OnHourColumn);
            Claim(ActGridPaintToFirstPawn, e => PaintHourAcrossRows(towardFirst: true), when: OnHourColumn);
            Claim(ActGridPaintToLastPawn, e => PaintHourAcrossRows(towardFirst: false), when: OnHourColumn);

            // Areas region: apply this pawn's area to a neighbour or to a run.
            Claim(ActAreasApplyAbove, e => ApplyAreaToNeighbor(-1), when: InAreasRegion);
            Claim(ActAreasApplyBelow, e => ApplyAreaToNeighbor(1), when: InAreasRegion);
            Claim(ActAreasPaintToFirstPawn, e => PaintAreaToEdge(towardFirst: true), when: InAreasRegion);
            Claim(ActAreasPaintToLastPawn, e => PaintAreaToEdge(towardFirst: false), when: InAreasRegion);
            Claim(ActAreasPaintToAllTowardFirst, e => PaintAreaAcrossAll(jumpToFirst: true), when: InAreasRegion);
            Claim(ActAreasPaintToAllTowardLast, e => PaintAreaAcrossAll(jumpToFirst: false), when: InAreasRegion);
            // Claimed on EVERY column: elsewhere the handler says where the context menu lives
            // instead of the chord dying silently against the scope's modality.
            Claim(ActAreasContextMenu, e => OpenAreaContextMenu());

            Claim(SharedMenuGrammar.Cancel, OnCancel);
        }

        public override string Name
        {
            get { return "schedule"; }
        }

        /// <summary>Shared cross-region typeahead over the pawn names.</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Digits 1-0 pick the timetable brush, so they must reach chord dispatch instead of the search buffer.</summary>
        protected override bool TypeaheadAcceptsDigits
        {
            get { return false; }
        }

        /// <summary>
        /// No Buttons region: the tab has no bottom buttons, and its <c>ButtonText</c> draws are
        /// per-cell widgets that would over-collect. The brush palette is served by digit chords.
        /// </summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        /// <summary>
        /// Inert during a drill-in this screen launches or can enter from: placement needs the MAP
        /// arrows, and <c>MapNavigationPatch.UpdateSuppressionFlag</c> suppresses them for ANY live
        /// modal scope before its placement exception runs. Going non-live stands this scope down
        /// from dispatch, the modal mask, and <c>MenuOwnsInput</c> in one stroke.
        /// </summary>
        public override bool IsLive
        {
            get { return base.IsLive && !DrillInActive(); }
        }

        /// <summary>
        /// The tab window inherits closeOnAccept/closeOnCancel and vanilla re-tests both bindings in
        /// its deferred GUI pass, where the dispatcher's Event.Use() is invisible. Enter is covered
        /// by the chassis; Escape is claimed here so one press closes the tab exactly once and says
        /// so, which needs cancel ownership even when no typeahead search is live.
        /// </summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        internal bool Owns(Window w)
        {
            return ReferenceEquals(w, window);
        }

        private PawnTable Table
        {
            // The table field is created in MainTabWindow_PawnTable.PostOpen.
            get { return window != null ? tableField(window) : null; }
        }

        private bool OwnsTable(PawnTable table)
        {
            return table != null && ReferenceEquals(table, Table);
        }

        /// <summary>
        /// The overlays this scope launches plus the map placement/viewing modes it can enter — see
        /// <see cref="IsLive"/>. The dispatcher already stands down for WindowlessFloatMenuState and
        /// TextInputManager, so only the remainder is named here.
        /// </summary>
        private static bool DrillInActive()
        {
            return ShapePlacementState.IsActive
                || ViewingModeState.IsActive;
        }

        // ScreenScope table contract.

        protected override int ContentRegionCount
        {
            get { return areasDef != null ? 2 : 1; }
        }

        /// <summary>The tab's own button label for the grid; vanilla's column label for the areas list.</summary>
        protected override string ContentRegionName(int region)
        {
            return region == AreasRegion ? (string)"AllowedArea".Translate() : ScheduleTabLabel;
        }

        /// <summary>
        /// Rows and columns come from the live table every pass: the row order is vanilla's, sorts
        /// included, and the column set is whatever the Restrict <see cref="PawnTableDef"/> draws,
        /// so a mod adding or removing one of the three edited columns is followed rather than
        /// assumed. Columns this screen does not EDIT are not modelled: copy/paste is a chord, and
        /// the icon columns are read by the generic pawn-table tier.
        /// </summary>
        protected override void RefreshContent()
        {
            rows.Clear();
            columns.Clear();
            hourColumnStart = -1;
            areasDef = null;
            PawnTable table = Table;
            if (table == null)
            {
                return;
            }
            List<PawnColumnDef> visible = table.Columns;
            for (int i = 0; i < visible.Count; i++)
            {
                PawnColumnDef def = visible[i];
                if (def.Worker is PawnColumnWorker_Label)
                {
                    columns.Add(new ColumnSlot { Def = def, Hour = -1 });
                }
                else if (def.Worker is PawnColumnWorker_Timetable)
                {
                    hourColumnStart = columns.Count;
                    for (int hour = 0; hour < HourCount; hour++)
                    {
                        columns.Add(new ColumnSlot { Def = def, Hour = hour });
                    }
                }
                else if (def.Worker is PawnColumnWorker_AllowedArea)
                {
                    areasDef = def;
                }
            }
            // A pawn list can carry nulls when a mod feeds the table; vanilla renders those rows
            // blank and they can never be acted on.
            foreach (Pawn pawn in table.PawnsListForReading)
            {
                if (pawn != null)
                {
                    rows.Add(pawn);
                }
            }
        }

        protected override int ContentItemCount(int region)
        {
            return rows.Count;
        }

        protected override int ContentColumnCount(int region)
        {
            return region == TableRegion ? columns.Count : 0;
        }

        /// <summary>Hour columns are named by the hour vanilla prints in its own header; the others by their def.</summary>
        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            if (column < 0 || column >= columns.Count)
            {
                return new TableColumnInfo("", null, sortable: false);
            }
            ColumnSlot slot = columns[column];
            if (slot.Hour >= 0)
            {
                return new TableColumnInfo(
                    "RimWorldAccess.Pawns.Schedule.HourColumn".Translate(slot.Hour).ToString(),
                    null, slot.Def.sortable);
            }
            string label = slot.Def.label.NullOrEmpty() ? slot.Def.defName : slot.Def.LabelCap.ToString();
            string tip = slot.Def.headerTip.NullOrEmpty() || slot.Def.headerTip == label ? null : slot.Def.headerTip;
            return new TableColumnInfo(label, tip, slot.Def.sortable);
        }

        /// <summary>An areas row carries the pawn's area as its value, read through the shared column handler.</summary>
        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index >= 0 && index < rows.Count)
            {
                d.Label = rows[index].LabelShort;
                if (region == AreasRegion && areasDef != null)
                {
                    d.Value = PawnColumnHandlerRegistry.Resolve(areasDef).CellText(areasDef, rows[index]);
                }
            }
            return d;
        }

        /// <summary>
        /// An hour cell is that pawn's assignment at that hour, or the reason it has none; the label
        /// and area cells read through the shared column handlers, so their text matches what every
        /// other pawn table speaks for the same vanilla column.
        /// </summary>
        protected override string ContentCellText(int region, int row, int column)
        {
            if (region != TableRegion || row < 0 || row >= rows.Count || column < 0 || column >= columns.Count)
            {
                return "";
            }
            Pawn pawn = rows[row];
            ColumnSlot slot = columns[column];
            if (slot.Hour < 0)
            {
                return PawnColumnHandlerRegistry.Resolve(slot.Def).CellText(slot.Def, pawn);
            }
            if (!CanEditTimetable(pawn))
            {
                return "RimWorldAccess.Pawns.Schedule.NoSchedule".Translate(pawn.LabelShort).ToString();
            }
            TimeAssignmentDef assignment = pawn.timetable.GetAssignment(slot.Hour);
            return assignment != null ? assignment.LabelCap.ToString() : "";
        }

        /// <summary>
        /// The label column carries the whole day as run-length ranges. It lives on ONE column:
        /// repeating it on all 24 hour cells would bury the hour the cursor is on.
        /// </summary>
        protected override string ContentCellTip(int region, int row, int column)
        {
            if (region != TableRegion || row < 0 || row >= rows.Count || column < 0 || column >= columns.Count)
            {
                return null;
            }
            ColumnSlot slot = columns[column];
            if (slot.Hour >= 0)
            {
                return null;
            }
            return slot.Def.Worker is PawnColumnWorker_Label
                ? TimetableReadout.Describe(rows[row])
                : PawnColumnHandlerRegistry.Resolve(slot.Def).CellTip(slot.Def, rows[row]);
        }

        /// <summary>Enter on an hour cell paints the brush; the label cell falls back to the row default.</summary>
        protected override bool ActivateContentCell(int region, int row, int column)
        {
            if (region != TableRegion || row < 0 || row >= rows.Count || column < 0 || column >= columns.Count)
            {
                return false;
            }
            ColumnSlot slot = columns[column];
            if (slot.Hour >= 0)
            {
                PaintCell(rows[row], slot.Hour);
                return true;
            }
            return false;
        }

        /// <summary>Areas row: apply the walked choice or open the picker. Grid row default: re-announce.</summary>
        protected override void ActivateContentItem(int region, int index)
        {
            if (index < 0 || index >= rows.Count)
            {
                return;
            }
            if (region == AreasRegion)
            {
                ActivateAreaRow(index);
                return;
            }
            SoundDefOf.Click.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Sorts through the table's own SortBy/RecachePawns, so the comparer is the column worker's
        /// Compare and a cleared sort is vanilla's true default order rather than a snapshot. Sorting
        /// from any hour cell sorts by the timetable column, the only header vanilla offers there.
        /// </summary>
        protected override int ApplyContentSort(int region, int column, SortCycleResult cycle, int currentRow)
        {
            PawnTable table = Table;
            if (table == null || column < 0 || column >= columns.Count)
            {
                return -1;
            }
            Pawn tracked = currentRow >= 0 && currentRow < rows.Count ? rows[currentRow] : null;
            if (cycle == SortCycleResult.Cleared)
            {
                table.SortBy(null, descending: false);
            }
            else
            {
                table.SortBy(columns[column].Def, cycle == SortCycleResult.SortedDescending);
            }
            RefreshContent();
            if (tracked == null)
            {
                return 0;
            }
            int index = rows.IndexOf(tracked);
            return index >= 0 ? index : 0;
        }

        /// <summary>
        /// Pointer routing over the label and area cells, whose geometry the shared router knows.
        /// The hour cells are 24 slices of one vanilla column and the router resolves a column by
        /// def, so a pointer inside the strip contributes nothing rather than routing to a guess.
        /// </summary>
        private protected override void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            PawnTableExtrasRouter.AddRouteCandidates(
                TableRegion, rows, columns.Count,
                c => columns[c].Hour >= 0 ? null : columns[c].Def,
                candidates, targets);
        }

        // Lifecycle.

        public override void OnPush()
        {
            base.OnPush();
            TooltipCapture.Arm();
            rows.Clear();
            columns.Clear();
            initialCursorPlaced = false;
        }

        public override void OnPop()
        {
            TooltipCapture.Disarm();
            base.OnPop();
        }

        /// <summary>
        /// Places the cursor on the current hour of day, on the pawn selected on the map. Once per
        /// open; a refocus leaves the cursor alone.
        /// </summary>
        public override void OnFocus()
        {
            base.OnFocus();
            if (initialCursorPlaced)
            {
                return;
            }
            initialCursorPlaced = true;
            TableModel table = Model.CurrentTable;
            if (table == null)
            {
                return;
            }
            Map map = Find.CurrentMap;
            if (map != null && hourColumnStart >= 0)
            {
                table.MoveToColumn(hourColumnStart + GenLocalDate.HourOfDay(map));
            }
            Pawn selected = Find.Selector != null ? Find.Selector.SingleSelectedThing as Pawn : null;
            if (selected != null)
            {
                int index = rows.IndexOf(selected);
                if (index >= 0)
                {
                    table.Rows.MoveTo(index + 1);
                }
            }
        }

        /// <summary>
        /// The screen's onboarding prose, spoken once per open before the first cell. Not an element
        /// description: it is about the screen, not a row.
        /// </summary>
        protected override string ComposeOpenAnnouncement()
        {
            var parts = new List<string> { ScheduleTabLabel + "." };
            TimeAssignmentDef brush = TimeAssignmentSelector.selectedAssignment;
            if (brush != null)
            {
                parts.Add("RimWorldAccess.Pawns.Schedule.Brush.Selected".Translate(brush.LabelCap));
            }
            List<TimeAssignmentDef> assignments = DefDatabase<TimeAssignmentDef>.AllDefsListForReading;
            if (assignments.Count > 0)
            {
                parts.Add("RimWorldAccess.Pawns.Schedule.Brush.HelpHeader".Translate());
                for (int i = 0; i < assignments.Count && i < BrushActions.Length; i++)
                {
                    int displayKey = (i + 1) % 10;
                    parts.Add("RimWorldAccess.Pawns.Schedule.Brush.NumberKeyHelp".Translate(displayKey, assignments[i].LabelCap));
                }
            }
            parts.Add("RimWorldAccess.Pawns.Schedule.Brush.ApplyHelp".Translate());
            parts.Add("RimWorldAccess.Pawns.Schedule.Brush.PaintHelp".Translate());
            parts.Add("RimWorldAccess.Pawns.Schedule.Brush.CopyPasteHelp".Translate("Copy".Translate(), "Paste".Translate()));
            return string.Join(" ", parts);
        }

        private static string ScheduleTabLabel
        {
            get
            {
                MainButtonDef def = DefDatabase<MainButtonDef>.GetNamed("Schedule", errorOnFail: false);
                return def != null ? (string)def.LabelCap : "Schedule";
            }
        }

        private void OnCancel(KeyEventSnapshot e)
        {
            // Block vanilla's same-frame Cancel re-test (see OwnsCancel).
            ShellFrameStamps.MarkCancelConsumed();
            string label = ScheduleTabLabel;
            Find.MainTabsRoot.EscapeCurrentTab(playSound: true);
            TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.Closed".Loc(label));
        }

        // Cursor accessors (the chassis cursor is the only cursor).

        /// <summary>The focused pawn row in either region, or -1 on the header row / an empty screen.</summary>
        private int CurrentRow()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null)
            {
                return -1;
            }
            int row = Model.RegionIndex == AreasRegion ? region.Index : region.Index - 1;
            return row >= 0 && row < rows.Count ? row : -1;
        }

        private Pawn CurrentPawn()
        {
            int row = CurrentRow();
            return row >= 0 ? rows[row] : null;
        }

        /// <summary>The focused hour, or -1 when the cursor is not on an hour cell.</summary>
        private int CurrentHour()
        {
            TableModel table = Model.CurrentTable;
            if (table == null || table.ColumnIndex >= columns.Count)
            {
                return -1;
            }
            return columns[table.ColumnIndex].Hour;
        }

        // Claim guards stay free of RefreshModel: a predicate runs speculatively during dispatch,
        // and the model the last pass built is the cursor the user is sitting on.
        private bool OnHourColumn()
        {
            return CurrentHour() >= 0;
        }

        private bool InAreasRegion()
        {
            return areasDef != null && Model.RegionIndex == AreasRegion;
        }

        /// <summary>Tab keeps the pawn: the region left hands its row to the region entered (the grid's row 0 is its header).</summary>
        protected override void OnRegionChanged(MoveResult result)
        {
            int from = lastRegionIndex;
            lastRegionIndex = Model.RegionIndex;
            areaCandidateRow = -1;
            if (from < 0 || from == Model.RegionIndex || from >= ContentRegionCount || Model.RegionIndex >= ContentRegionCount)
            {
                return;
            }
            int row = from == AreasRegion ? Model.Region(from).Index : Model.Region(from).Index - 1;
            ListModel target = Model.CurrentRegion;
            if (target == null || target.IsEmpty)
            {
                return;
            }
            int index = Model.RegionIndex == AreasRegion ? Math.Max(0, row) : row + 1;
            target.MoveTo(Math.Max(0, Math.Min(index, target.Count - 1)));
        }

        private static bool CanEditTimetable(Pawn pawn)
        {
            // PawnColumnWorker_Timetable.DoCell's own gate: vanilla draws no hour cells otherwise.
            return pawn != null && pawn.timetable != null && !pawn.IsSubhuman;
        }

        /// <summary>Moves the row cursor to a data row, wrapping within the data rows (the header row is never a paint target).</summary>
        private void MoveToDataRow(int row)
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || rows.Count == 0)
            {
                return;
            }
            int wrapped = ((row % rows.Count) + rows.Count) % rows.Count;
            region.MoveTo(Model.RegionIndex == AreasRegion ? wrapped : wrapped + 1);
        }

        // Brush selection and painting (hour columns).

        private void SelectBrush(int index)
        {
            List<TimeAssignmentDef> assignments = DefDatabase<TimeAssignmentDef>.AllDefsListForReading;
            if (index < 0 || index >= assignments.Count)
            {
                int displayKey = (index + 1) % 10;
                TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.Brush.NoBrushOnKey".Loc(displayKey));
                return;
            }
            TimeAssignmentSelector.selectedAssignment = assignments[index];
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            TolkHelper.SpeakData(assignments[index].LabelCap);
        }

        /// <summary>Paints the loaded brush into one hour cell, or refuses with the reason the cell already reads out.</summary>
        private void PaintCell(Pawn pawn, int hour)
        {
            if (!CanEditTimetable(pawn))
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.NoSchedule".Loc(pawn.LabelShort), SpeechPriority.High);
                return;
            }
            TimeAssignmentDef brush = TimeAssignmentSelector.selectedAssignment;
            if (brush == null)
            {
                return;
            }
            if (pawn.timetable.GetAssignment(hour) != brush)
            {
                pawn.timetable.SetAssignment(hour, brush);
                BulkPaintSound.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.PaintApplied".Loc(pawn.LabelShort, hour, brush.LabelCap));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.AlreadyAtHour".Loc(hour, brush.LabelCap));
            }
        }

        /// <summary>Shift+Up/Down: step to the next row and paint the same hour there.</summary>
        private void PaintMoveRow(int delta)
        {
            RefreshModel();
            int row = CurrentRow();
            int hour = CurrentHour();
            if (row < 0 || hour < 0 || TimeAssignmentSelector.selectedAssignment == null)
            {
                return;
            }
            MoveToDataRow(row + delta);
            PaintMoved();
        }

        /// <summary>Shift+Left/Right: step to the next hour of the same row and paint it.</summary>
        private void PaintMoveHour(int delta)
        {
            RefreshModel();
            TableModel table = Model.CurrentTable;
            int hour = CurrentHour();
            if (table == null || CurrentRow() < 0 || hour < 0 || TimeAssignmentSelector.selectedAssignment == null)
            {
                return;
            }
            table.MoveToColumn(hourColumnStart + (hour + delta + HourCount) % HourCount);
            PaintMoved();
        }

        /// <summary>The paint-chord landing announcement; names the pawn, since the cursor moved.</summary>
        private void PaintMoved()
        {
            Pawn pawn = CurrentPawn();
            int hour = CurrentHour();
            if (pawn == null || hour < 0)
            {
                return;
            }
            if (!CanEditTimetable(pawn))
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.NoSchedule".Loc(pawn.LabelShort), SpeechPriority.High);
                return;
            }
            TimeAssignmentDef brush = TimeAssignmentSelector.selectedAssignment;
            if (pawn.timetable.GetAssignment(hour) != brush)
            {
                pawn.timetable.SetAssignment(hour, brush);
                BulkPaintSound.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.PaintApplied".Loc(pawn.LabelShort, hour, brush.LabelCap));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.AlreadyAtPawnHour".Loc(pawn.LabelShort, hour, brush.LabelCap));
            }
        }

        /// <summary>Shift+Home/End: paint from the focused hour to hour 0 or 23 for this pawn.</summary>
        private void PaintHourRange(int boundaryHour)
        {
            RefreshModel();
            TableModel table = Model.CurrentTable;
            Pawn pawn = CurrentPawn();
            int startHour = CurrentHour();
            TimeAssignmentDef brush = TimeAssignmentSelector.selectedAssignment;
            if (table == null || startHour < 0 || pawn == null || brush == null)
            {
                return;
            }
            if (!CanEditTimetable(pawn))
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.NoSchedule".Loc(pawn.LabelShort), SpeechPriority.High);
                return;
            }
            int step = boundaryHour >= startHour ? 1 : -1;
            var changedHours = new List<int>();
            for (int h = startHour; h != boundaryHour + step; h += step)
            {
                if (pawn.timetable.GetAssignment(h) != brush)
                {
                    pawn.timetable.SetAssignment(h, brush);
                    changedHours.Add(h);
                }
            }
            table.MoveToColumn(hourColumnStart + boundaryHour);
            BulkSoundQueue.Queue(changedHours.Count, BulkPaintSound);

            if (changedHours.Count > 0)
            {
                changedHours.Sort();
                TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.BulkPaint.PaintedHours".Loc(brush.LabelCap, FormatChangedHourRanges(changedHours), pawn.LabelShort));
                return;
            }
            TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.BulkPaint.HoursAlready".Loc(FormatEdgeHourRange(startHour, boundaryHour), brush.LabelCap, pawn.LabelShort));
        }

        internal static string FormatEdgeHourRange(int startHour, int boundaryHour)
        {
            return boundaryHour == 0
                ? (startHour == 0
                    ? (string)"RimWorldAccess.Pawns.Schedule.BulkPaint.HourRangeFirstOnly".Translate()
                    : (string)"RimWorldAccess.Pawns.Schedule.BulkPaint.HourRangeFromZero".Translate(startHour))
                : (startHour == HourCount - 1
                    ? (string)"RimWorldAccess.Pawns.Schedule.BulkPaint.HourRangeLastOnly".Translate()
                    : (string)"RimWorldAccess.Pawns.Schedule.BulkPaint.HourRangeToLast".Translate(startHour));
        }

        /// <summary>Ctrl+Shift+Home/End: paint the focused hour for every row from here to the first or last one.</summary>
        private void PaintHourAcrossRows(bool towardFirst)
        {
            RefreshModel();
            int row = CurrentRow();
            int hour = CurrentHour();
            TimeAssignmentDef brush = TimeAssignmentSelector.selectedAssignment;
            if (row < 0 || hour < 0 || brush == null)
            {
                return;
            }
            int start = towardFirst ? 0 : row;
            int end = towardFirst ? row : rows.Count - 1;
            var changedNames = new List<string>();
            for (int i = start; i <= end; i++)
            {
                Pawn pawn = rows[i];
                if (!CanEditTimetable(pawn))
                {
                    continue;
                }
                if (pawn.timetable.GetAssignment(hour) != brush)
                {
                    pawn.timetable.SetAssignment(hour, brush);
                    changedNames.Add(pawn.LabelShort);
                }
            }
            MoveToDataRow(towardFirst ? 0 : rows.Count - 1);
            BulkSoundQueue.Queue(changedNames.Count, BulkPaintSound);

            if (changedNames.Count > 0)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.BulkPaint.PaintedHourForPawns".Loc(brush.LabelCap, hour, MenuHelper.FormatNameList(changedNames)));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.BulkPaint.HourAllPawnsAlready".Loc(hour, brush.LabelCap));
            }
        }

        /// <summary>Groups a sorted list of hour indices into consecutive ranges.</summary>
        internal static string FormatChangedHourRanges(List<int> hours)
        {
            if (hours.Count == 0)
            {
                return "";
            }
            var ranges = new List<string>();
            int rangeStart = hours[0];
            int rangeEnd = hours[0];
            for (int i = 1; i < hours.Count; i++)
            {
                if (hours[i] == rangeEnd + 1)
                {
                    rangeEnd = hours[i];
                }
                else
                {
                    ranges.Add(FormatHourRange(rangeStart, rangeEnd));
                    rangeStart = hours[i];
                    rangeEnd = hours[i];
                }
            }
            ranges.Add(FormatHourRange(rangeStart, rangeEnd));

            string label = hours.Count == 1
                ? (string)"RimWorldAccess.Pawns.Schedule.BulkPaint.HoursLabelOne".Translate()
                : (string)"RimWorldAccess.Pawns.Schedule.BulkPaint.HoursLabelMany".Translate();
            return "RimWorldAccess.Pawns.Schedule.BulkPaint.HoursWithList".Translate(label, MenuHelper.FormatNameList(ranges));
        }

        private static string FormatHourRange(int rangeStart, int rangeEnd)
        {
            return rangeStart == rangeEnd
                ? (string)"RimWorldAccess.Pawns.Schedule.BulkPaint.HourRangeOne".Translate(rangeStart)
                : (string)"RimWorldAccess.Pawns.Schedule.BulkPaint.HourRange".Translate(rangeStart, rangeEnd);
        }

        // Row copy/paste — vanilla's shared clipboard.

        private void CopyRow()
        {
            RefreshModel();
            Pawn pawn = CurrentPawn();
            if (!CanEditTimetable(pawn))
            {
                return;
            }
            clipboardField.SetValue(null, pawn.timetable.times.ToList());
            TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.CopyPasteAnnouncement".Loc("Copy".Translate(), pawn.LabelShort));
        }

        private void PasteRow()
        {
            var clipboard = clipboardField.GetValue(null) as List<TimeAssignmentDef>;
            if (clipboard == null)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.NoSchedCopied".Loc());
                return;
            }
            RefreshModel();
            Pawn pawn = CurrentPawn();
            if (!CanEditTimetable(pawn))
            {
                return;
            }
            for (int h = 0; h < HourCount && h < clipboard.Count; h++)
            {
                pawn.timetable.SetAssignment(h, clipboard[h]);
            }
            TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.CopyPasteAnnouncement".Loc("Paste".Translate(), pawn.LabelShort));
        }

        // Allowed-area column.

        /// <summary>
        /// The area picker. Vanilla's own generator builds the whole option list, manage entry
        /// included — that row raises the real <c>Dialog_ManageAreas</c>, driven by
        /// <see cref="ManageAreasScope"/>.
        /// </summary>
        private void OpenAreaPicker(Pawn pawn, int row)
        {
            if (pawn.playerSettings == null || pawn.MapHeld == null)
            {
                return;
            }
            if (!PawnColumnMutationHelper.CanEditAllowedArea(pawn))
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.AreaReadOnly".Loc(pawn.LabelShort), SpeechPriority.High);
                return;
            }
            Map map = pawn.MapHeld;
            int windowCountBefore = Find.WindowStack.Windows.Count;
            // MUTATION-C: the selAction below is the exact bare-field-write shape vanilla's
            // own callers pass to this generator (InspectPaneFiller.cs:160,
            // Designator_AreaAllowed.cs:45) — AreaRestrictionInPawnCurrentMap has no gated
            // setter. The CanEditAllowedArea gate above is vanilla's own compound cell gate.
            AreaUtility.MakeAllowedAreaListFloatMenu(
                selArea => pawn.playerSettings.AreaRestrictionInPawnCurrentMap = selArea,
                addNullAreaOption: true,
                addManageOption: true,
                map);
            if (Find.WindowStack.Windows.Count <= windowCountBefore
                || !(Find.WindowStack.Windows[Find.WindowStack.Windows.Count - 1] is FloatMenu spawnedMenu))
            {
                return;
            }
            List<FloatMenuOption> options = PawnColumnMutationHelper.ExtractFloatMenuOptions(spawnedMenu);
            Find.WindowStack.TryRemove(spawnedMenu, doCloseSound: false);
            if (options == null)
            {
                return;
            }
            // The accessible presenter, not the raw FloatMenu window: a real FloatMenu spawned far
            // from the mouse fades itself out within frames. playOpenSound is false because
            // FloatMenu's constructor already played it.
            WindowlessFloatMenuState.Open(options, spawnedMenu.givesColonistOrders, playOpenSound: false);
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            return region == AreasRegion && index >= 0 && index < rows.Count;
        }

        /// <summary>Left/Right: walk the choices vanilla's picker offers, starting from the pawn's own area.</summary>
        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (region != AreasRegion || index < 0 || index >= rows.Count)
            {
                return;
            }
            Pawn pawn = rows[index];
            LoadAreaChoices(pawn);
            if (areaChoices.Count == 0)
            {
                return;
            }
            if (areaCandidateRow != index || areaCandidate < 0 || areaCandidate >= areaChoices.Count)
            {
                areaCandidate = SyncedAreaChoice(pawn);
            }
            areaCandidateRow = index;
            areaCandidate = direction > 0
                ? MenuHelper.SelectNext(areaCandidate, areaChoices.Count)
                : MenuHelper.SelectPrevious(areaCandidate, areaChoices.Count);
            TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.AreaFocus".Loc(
                AreaChoiceName(areaChoices[areaCandidate]), MenuHelper.FormatPosition(areaCandidate, areaChoices.Count)));
        }

        /// <summary>Vanilla's picker list (AreaUtility.MakeAllowedAreaListFloatMenu): manage entry, Unrestricted, every assignable area.</summary>
        private void LoadAreaChoices(Pawn pawn)
        {
            areaChoices.Clear();
            Map map = pawn.MapHeld ?? Find.CurrentMap;
            if (map == null)
            {
                return;
            }
            areaChoices.Add(ManageAreasChoice);
            areaChoices.Add(null);
            areaChoices.AddRange(map.areaManager.AllAreas.Where(a => a.AssignableAsAllowed()));
        }

        private int SyncedAreaChoice(Pawn pawn)
        {
            Area current = pawn.playerSettings != null ? pawn.playerSettings.AreaRestrictionInPawnCurrentMap : null;
            int index = current != null ? areaChoices.IndexOf(current) : -1;
            return index >= 0 ? index : Math.Min(1, areaChoices.Count - 1);
        }

        private static string AreaChoiceName(object choice)
        {
            if (ReferenceEquals(choice, ManageAreasChoice))
            {
                return "ManageAreas".Translate();
            }
            return choice is Area area ? area.Label : (string)"NoAreaAllowed".Translate();
        }

        /// <summary>Enter on an areas row: the walked choice is applied (Manage areas opens the real dialog); with none walked, the picker opens.</summary>
        private void ActivateAreaRow(int row)
        {
            Pawn pawn = rows[row];
            LoadAreaChoices(pawn);
            if (areaCandidateRow != row || areaCandidate < 0 || areaCandidate >= areaChoices.Count)
            {
                OpenAreaPicker(pawn, row);
                return;
            }
            object choice = areaChoices[areaCandidate];
            if (ReferenceEquals(choice, ManageAreasChoice))
            {
                Map map = pawn.MapHeld ?? Find.CurrentMap;
                if (map != null)
                {
                    Find.WindowStack.Add(new Dialog_ManageAreas(map));
                }
                return;
            }
            if (!PawnColumnMutationHelper.CanEditAllowedArea(pawn))
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.AreaReadOnly".Loc(pawn.LabelShort), SpeechPriority.High);
                return;
            }
            // MUTATION-C: mirrors PawnColumnWorker_AllowedArea.DoCell's own full compound gate
            // (see ApplyAreaToNeighbor below); no gated setter exists for
            // AreaRestrictionInPawnCurrentMap itself. The guard above already enforced the gate.
            pawn.playerSettings.AreaRestrictionInPawnCurrentMap = choice as Area;
            string position = MenuHelper.FormatPosition(areaCandidate, areaChoices.Count);
            areaCandidateRow = -1;
            SoundDefOf.Click.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.AreaApplied".Loc(pawn.LabelShort, AreaChoiceName(choice), position));
        }

        /// <summary>Shift+Up/Down on an areas row: copy this pawn's area onto the neighbour and follow it.</summary>
        private void ApplyAreaToNeighbor(int delta)
        {
            RefreshModel();
            int row = CurrentRow();
            if (row < 0 || rows.Count <= 1)
            {
                return;
            }
            Area sourceArea = rows[row].playerSettings?.AreaRestrictionInPawnCurrentMap;
            MoveToDataRow(row + delta);
            Pawn target = CurrentPawn();
            if (target == null)
            {
                return;
            }
            string pawnPosition = MenuHelper.FormatPosition(CurrentRow(), rows.Count);
            if (!PawnColumnMutationHelper.CanEditAllowedArea(target))
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.AreaReadOnlyPos".Loc(target.LabelShort, pawnPosition), SpeechPriority.High);
                return;
            }

            // MUTATION-C: mirrors PawnColumnWorker_AllowedArea.DoCell's own full compound gate
            // (Faction/mutant/mech/SupportsAllowedAreas — vanilla draws no selector at all when
            // any clause fails, and the shift-click bulk handler checks the same members); no
            // gated setter exists for AreaRestrictionInPawnCurrentMap itself. The guard above
            // already enforced the gate for this write.
            target.playerSettings.AreaRestrictionInPawnCurrentMap = sourceArea;
            string areaName = sourceArea != null ? sourceArea.Label : (string)"NoAreaAllowed".Translate();
            TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.AreaAppliedPawnPos".Loc(target.LabelShort, areaName, pawnPosition));
        }

        private void PaintAreaToEdge(bool towardFirst)
        {
            RefreshModel();
            int row = CurrentRow();
            if (row < 0)
            {
                return;
            }
            PaintAreaAcrossRows(row, towardFirst ? 0 : row, towardFirst ? row : rows.Count - 1, towardFirst);
        }

        private void PaintAreaAcrossAll(bool jumpToFirst)
        {
            RefreshModel();
            int row = CurrentRow();
            if (row < 0)
            {
                return;
            }
            PaintAreaAcrossRows(row, 0, rows.Count - 1, jumpToFirst);
        }

        private void PaintAreaAcrossRows(int sourceRow, int start, int end, bool jumpToFirst)
        {
            if (start > end)
            {
                return;
            }
            Area sourceArea = rows[sourceRow].playerSettings?.AreaRestrictionInPawnCurrentMap;
            var changedNames = new List<string>();
            for (int i = start; i <= end && i < rows.Count; i++)
            {
                Pawn pawn = rows[i];
                // Pawns vanilla would draw no selector for are skipped, as in the bulk paths below.
                if (!PawnColumnMutationHelper.CanEditAllowedArea(pawn))
                {
                    continue;
                }
                if (pawn.playerSettings.AreaRestrictionInPawnCurrentMap != sourceArea)
                {
                    // MUTATION-C: mirrors PawnColumnWorker_AllowedArea's own full compound gate
                    // (see ApplyAreaToNeighbor above); no gated setter exists for
                    // AreaRestrictionInPawnCurrentMap itself. The guard above already enforced
                    // the gate for this write.
                    pawn.playerSettings.AreaRestrictionInPawnCurrentMap = sourceArea;
                    changedNames.Add(pawn.LabelShort);
                }
            }
            MoveToDataRow(jumpToFirst ? 0 : rows.Count - 1);
            BulkSoundQueue.Queue(changedNames.Count, BulkPaintSound);

            string areaName = sourceArea != null ? sourceArea.Label : (string)"NoAreaAllowed".Translate();
            if (changedNames.Count > 0)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.AreaPaintedFor".Loc(areaName, MenuHelper.FormatNameList(changedNames)));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.AreaAllAlready".Loc(areaName));
            }
        }

        private void OpenAreaContextMenu()
        {
            if (!InAreasRegion())
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.ContextOnlyAreas".Loc());
                return;
            }
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("RimWorldAccess.Pawns.Schedule.ContextMenu.SetAllHome".Translate(), SetAllToHomeArea),
                new FloatMenuOption("RimWorldAccess.Pawns.Schedule.ContextMenu.ClearAll".Translate(), ClearAllAreas),
            };
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        private void SetAllToHomeArea()
        {
            Area homeArea = Find.CurrentMap?.areaManager?.Home;
            if (homeArea == null)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.NoHomeArea".Loc());
                return;
            }
            TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.SetCountToHome".Loc(ApplyAreaToAll(homeArea)));
        }

        private void ClearAllAreas()
        {
            TolkHelper.Speak("RimWorldAccess.Pawns.Schedule.ClearedCount".Loc(ApplyAreaToAll(null)));
        }

        /// <summary>The context menu's bulk writes; returns how many pawns took the area.</summary>
        private int ApplyAreaToAll(Area area)
        {
            RefreshModel();
            int count = 0;
            foreach (Pawn pawn in rows)
            {
                // MUTATION-C: mirrors PawnColumnWorker_AllowedArea.HeaderClicked's bulk gate
                // (decompiled :88) — Faction.OfPlayer + SupportsAllowedAreas ONLY; vanilla's
                // shift-click bulk path checks fewer clauses than its per-cell DoCell gate, so
                // the full CanEditAllowedArea would be stricter than vanilla here.
                if (pawn.Faction == Faction.OfPlayer && pawn.playerSettings != null
                    && pawn.playerSettings.SupportsAllowedAreas)
                {
                    pawn.playerSettings.AreaRestrictionInPawnCurrentMap = area;
                    count++;
                }
            }
            return count;
        }

        // Per-GUI-pass work, driven by the table's own draw.

        /// <summary>Runs from the DoWindowContents prefix: reset the row-drawn tracker for this pass.</summary>
        internal void BeginDrawPass()
        {
            focusedRowDrawnThisPass = false;
        }

        /// <summary>
        /// Runs from the MainTabWindow_PawnTable.DoWindowContents postfix. PawnTable culls
        /// off-screen rows and has no scroll-to-selection, so a focused row that was not drawn this
        /// repaint nudges the private scroll position toward it.
        /// </summary>
        internal void OnGuiPass()
        {
            if (Event.current.type == EventType.Repaint && !focusedRowDrawnThisPass)
            {
                AutoScrollToFocused();
            }
        }

        internal void OnTimetableCellDrawn(Rect rect, Pawn pawn, PawnTable table)
        {
            if (pawn == null || !OwnsTable(table) || !ReferenceEquals(pawn, CurrentPawn()))
            {
                return;
            }
            // Unconditional: the row was drawn either way, and auto-scroll must not fight a row
            // whose strip is simply empty.
            focusedRowDrawnThisPass = true;
            int hour = CurrentHour();
            // CanEditTimetable is the condition DoCell drew the strip under; a postfix runs even
            // when it early-outed. This ring is the exact complement of what FocusedTablePawn hands
            // the shared driver, so an empty strip gets a whole-cell ring and never a slice on top.
            if (hour >= 0 && CanEditTimetable(pawn))
            {
                float cellWidth = rect.width / HourCount;
                FocusRing.Draw(new Rect(rect.x + hour * cellWidth, rect.y, cellWidth, rect.height).ContractedBy(1f));
            }
        }

        /// <summary>Tracks whether the focused row was drawn; every visible row has an area cell.</summary>
        internal void OnAllowedAreaCellDrawn(Pawn pawn, PawnTable table)
        {
            if (pawn != null && OwnsTable(table) && ReferenceEquals(pawn, CurrentPawn()))
            {
                focusedRowDrawnThisPass = true;
            }
        }

        private void AutoScrollToFocused()
        {
            PawnTable table = Table;
            Pawn focused = CurrentPawn();
            if (table == null || focused == null)
            {
                return;
            }
            int rowIndex = table.PawnsListForReading.IndexOf(focused);
            if (rowIndex < 0)
            {
                return;
            }
            List<float> heights = cachedRowHeightsField(table);
            if (heights == null || rowIndex >= heights.Count)
            {
                return;
            }
            float targetY = 0f;
            for (int i = 0; i < rowIndex; i++)
            {
                targetY += heights[i];
            }
            ref Vector2 scroll = ref scrollPositionField(table);
            scroll.y = targetY;
        }

        // IPawnTableFocusSource: the shared ring for every cell whose WHOLE cell is the cursor.

        Type IPawnTableFocusSource.TabWindowType
        {
            get { return window.GetType(); }
        }

        /// <summary>
        /// Null while the cursor is on an hour cell of a row that draws hour cells:
        /// <see cref="ScheduleTimetableCellPatch"/> rings that slice, and a second ring around the
        /// whole strip would point at every hour at once. A row with no hour cells has no slice to
        /// ring, so the strip itself is the cursor there.
        /// </summary>
        Pawn IPawnTableFocusSource.FocusedTablePawn
        {
            get
            {
                Pawn pawn = CurrentPawn();
                return CurrentHour() >= 0 && CanEditTimetable(pawn) ? null : pawn;
            }
        }

        PawnColumnDef IPawnTableFocusSource.FocusedTableColumn
        {
            get
            {
                if (InAreasRegion())
                {
                    return areasDef;
                }
                TableModel table = Model.CurrentTable;
                int column = table != null ? table.ColumnIndex : -1;
                return column >= 0 && column < columns.Count ? columns[column].Def : null;
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
    }

    /// <summary>
    /// Draws the focus ring over the focused hour cell and tracks whether the focused row was drawn
    /// this pass. Fires only for the timetable column; the scope re-checks the table identity so a
    /// stacked table cannot be painted.
    /// </summary>
    [HarmonyPatch(typeof(PawnColumnWorker_Timetable), "DoCell")]
    public static class ScheduleTimetableCellPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect rect, Pawn pawn, PawnTable table)
        {
            try
            {
                ScheduleScope scope = FocusStack.Top as ScheduleScope;
                if (scope != null)
                {
                    scope.OnTimetableCellDrawn(rect, pawn, table);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Schedule timetable cell draw error", ex);
            }
        }
    }

    /// <summary>
    /// Tracks whether the focused row was drawn — the area cell is drawn for every visible row, so
    /// it is the reliable tracker; the ring comes from the shared
    /// <see cref="PawnTableFocusDriver"/>. Other tabs share PawnColumnWorker_AllowedArea, so the
    /// scope re-checks the table identity.
    /// </summary>
    [HarmonyPatch(typeof(PawnColumnWorker_AllowedArea), "DoCell")]
    public static class ScheduleAllowedAreaCellPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, PawnTable table)
        {
            try
            {
                ScheduleScope scope = FocusStack.Top as ScheduleScope;
                if (scope != null)
                {
                    scope.OnAllowedAreaCellDrawn(pawn, table);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Schedule allowed-area cell draw error", ex);
            }
        }
    }

    /// <summary>
    /// Brackets the scope's per-pass work to the table's own draw. Patches the DECLARING
    /// MainTabWindow_PawnTable.DoWindowContents — MainTabWindow_Schedule overrides it but calls
    /// base — so this fires after the table has drawn its rows, for the top scope's window only.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_PawnTable), "DoWindowContents")]
    public static class SchedulePawnTableDrawPatch
    {
        [HarmonyPrefix]
        public static void Prefix(MainTabWindow_PawnTable __instance)
        {
            try
            {
                ScheduleScope scope = FocusStack.Top as ScheduleScope;
                if (scope != null && scope.Owns(__instance))
                {
                    scope.BeginDrawPass();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Schedule table draw pass error", ex);
            }
        }

        [HarmonyPostfix]
        public static void Postfix(MainTabWindow_PawnTable __instance)
        {
            try
            {
                ScheduleScope scope = FocusStack.Top as ScheduleScope;
                if (scope != null && scope.Owns(__instance))
                {
                    scope.OnGuiPass();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Schedule table draw pass error", ex);
            }
        }
    }
}
