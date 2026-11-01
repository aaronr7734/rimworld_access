using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// A <see cref="ScreenScope"/> table region over any
    /// <see cref="MainTabWindow_PawnTable"/> — a mod's added pawn table, or a
    /// vanilla one no bespoke scope claims — with zero per-screen code. The live
    /// <see cref="PawnTable"/> on the window is the single source of truth, so a
    /// sort cleared here restores vanilla's own default order rather than a
    /// snapshot. Per-column reading and activation dispatch through
    /// <see cref="PawnColumnHandlerRegistry"/> by worker type, so a mod column
    /// subclassing a vanilla worker inherits full support and an unknown worker
    /// degrades to a navigable, sortable, honestly-unreadable cell.
    /// Attached in ShellBootstrap as a
    /// <see cref="ScopeForWindow.RegisterHierarchy"/> factory; exact-type
    /// registrations win over the hierarchy arm, and the factory declines the
    /// intercept-and-replace vanilla tabs itself.
    /// <see cref="TryCreateForTable"/> additionally serves any
    /// <see cref="MainTabWindow"/> that can supply its own table without being a
    /// <see cref="MainTabWindow_PawnTable"/>, which a compat shim opts into by
    /// registering against its own window type.
    /// </summary>
    public sealed class GenericPawnTableScope : ScreenScope, IPawnTableFocusSource
    {
        private const int TableRegion = 0;

        private static readonly AccessTools.FieldRef<MainTabWindow_PawnTable, PawnTable> tableField =
            AccessTools.FieldRefAccess<MainTabWindow_PawnTable, PawnTable>("table");

        private static readonly AccessTools.FieldRef<PawnTable, PawnTableDef> tableDefField =
            AccessTools.FieldRefAccess<PawnTable, PawnTableDef>("def");

        /// <summary>
        /// The vanilla tabs already driven by a windowless accessible screen.
        /// Their windows stay open and rendered, so attaching the generic tier too
        /// would put two scopes on one surface claiming the same keys. Exact types,
        /// matching the opener patches' own guards, so a mod subclass overriding
        /// DoWindowContents escapes both.
        /// </summary>
        private static readonly HashSet<Type> interceptedTabs = new HashSet<Type>
        {
            typeof(MainTabWindow_Work),
            typeof(MainTabWindow_Animals),
            typeof(MainTabWindow_Wildlife),
            typeof(MainTabWindow_Assign),
            typeof(MainTabWindow_Mechs),
        };

        private readonly MainTabWindow window;
        private readonly Func<PawnTable> tableSupplier;
        private readonly List<Pawn> rows = new List<Pawn>();
        private readonly List<PawnColumnDef> columns = new List<PawnColumnDef>();
        private int labelColumn = -1;
        private bool announcedOpen;
        private bool focused;

        // Keyed by def, not instance: a customizing mod rebuilds the PawnTable on every column edit.
        private PawnTableDef snapshotTableDef;
        private readonly List<PawnColumnDef> snapshotColumns = new List<PawnColumnDef>();
        private string pendingColumnAdded;

        /// <summary>The ScopeForWindow hierarchy factory. Null = no scope (vanilla flow untouched).</summary>
        internal static FocusScope TryCreate(Window window)
        {
            var tab = window as MainTabWindow_PawnTable;
            if (tab == null || interceptedTabs.Contains(window.GetType()))
            {
                return null;
            }
            return new GenericPawnTableScope(tab, () => tableField(tab));
        }

        /// <summary>
        /// Attaches the tier to a <see cref="MainTabWindow"/> that supplies its own
        /// live <see cref="PawnTable"/> from wherever it keeps one. No
        /// intercepted-tabs check: registering directly against a window type is
        /// already a deliberate opt-in.
        /// </summary>
        internal static FocusScope TryCreateForTable(MainTabWindow host, Func<PawnTable> tableSupplier)
        {
            if (host == null || tableSupplier == null)
            {
                return null;
            }
            return new GenericPawnTableScope(host, tableSupplier);
        }

        private GenericPawnTableScope(MainTabWindow window, Func<PawnTable> tableSupplier)
        {
            this.window = window;
            this.tableSupplier = tableSupplier;
            Claim("pawnTable.cyclePriorityDown", e => AdjustCurrentCell(-1), when: () => CurrentCellAdjustable());
            Claim("pawnTable.cyclePriorityUp", e => AdjustCurrentCell(1), when: () => CurrentCellAdjustable());
            Claim(SharedMenuGrammar.ReorderLeft, e => MoveCurrentColumn(-1), when: ColumnsEditable);
            Claim(SharedMenuGrammar.ReorderRight, e => MoveCurrentColumn(1), when: ColumnsEditable);
            Claim("pawnTable.removeColumn", e => RemoveCurrentColumn(), when: ColumnsEditable);
        }

        public override string Name
        {
            get { return "pawn-table"; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>
        /// A pawn table's only ButtonText draws are per-header and per-cell widgets,
        /// exactly the over-collection trap CaptureWindowButtons' remarks warn
        /// about; their functions are served in-cell instead.
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
        /// The flat "Additional controls" region catches widgets the geometry router
        /// cannot place, plus the table's header buttons and counters, which stay
        /// flat deliberately: they draw once per table, not per row.
        /// </summary>
        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        /// <summary>
        /// Drops from the flat extras region every captured widget sitting in a body
        /// cell whose column handler opted out via
        /// <see cref="IPawnColumnHandler.SuppressCapturedExtras"/>, so a handled
        /// column's own option strip does not flood the table. A widget outside any
        /// suppressed cell still surfaces through the base class's diff.
        /// </summary>
        protected override IEnumerable<string> AdditionalPresentedTexts
        {
            get { return SuppressedExtraLabels(); }
        }

        /// <summary>
        /// Geometric twin of <see cref="SuppressedExtraLabels"/> for captionless
        /// in-cell widgets, which have no text for the presented-set diff to match
        /// and would otherwise reach "Additional controls" as nameless rows. Vetoes
        /// before folding, so the whole cluster drops together.
        /// </summary>
        protected override bool ExcludeFromCapturedExtras(CapturedWidget widget)
        {
            if (columns.Count == 0 || rows.Count == 0)
            {
                return false;
            }
            int colIndex = PawnTableExtrasRouter.FindColumnAt(columns, widget.ScreenRect.center.x);
            if (colIndex < 0)
            {
                return false;
            }
            int rowIndex = PawnTableExtrasRouter.FindRowAt(rows, widget.ScreenRect.center.y);
            if (rowIndex < 0)
            {
                return false;
            }
            return PawnColumnHandlerRegistry.Resolve(columns[colIndex]).SuppressCapturedExtras(columns[colIndex]);
        }

        private IEnumerable<string> SuppressedExtraLabels()
        {
            if (columns.Count == 0 || rows.Count == 0)
            {
                yield break;
            }
            IReadOnlyList<CapturedWidget> items = WidgetCapture.Items;
            var seen = new HashSet<string>();
            for (int i = 0; i < items.Count; i++)
            {
                CapturedWidget widget = items[i];
                if (string.IsNullOrEmpty(widget.Label) || !seen.Add(widget.Label))
                {
                    continue;
                }
                int colIndex = PawnTableExtrasRouter.FindColumnAt(columns, widget.ScreenRect.center.x);
                if (colIndex < 0)
                {
                    continue;
                }
                int rowIndex = PawnTableExtrasRouter.FindRowAt(rows, widget.ScreenRect.center.y);
                if (rowIndex < 0)
                {
                    continue;
                }
                if (PawnColumnHandlerRegistry.Resolve(columns[colIndex]).SuppressCapturedExtras(columns[colIndex]))
                {
                    yield return widget.Label;
                }
            }
        }

        private protected override void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            PawnTableExtrasRouter.AddRouteCandidates(
                0, rows, columns.Count, c => columns[c], candidates, targets);
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>The tab's own button label, the same word the sighted player reads on the bottom bar.</summary>
        protected override string ContentRegionName(int region)
        {
            if (window.def != null && !window.def.label.NullOrEmpty())
            {
                return window.def.LabelCap.ToString();
            }
            return "RimWorldAccess.Shell.Generic.PawnTableRegion".Loc().ToString();
        }

        protected override void RefreshContent()
        {
            PawnTable table = tableSupplier();
            rows.Clear();
            columns.Clear();
            labelColumn = -1;
            if (table == null)
            {
                PawnTableExtrasRouter.SetActiveTable(null);
                return;
            }
            PawnTableExtrasRouter.SetActiveTable(table);
            List<PawnColumnDef> visible = table.Columns;
            for (int i = 0; i < visible.Count; i++)
            {
                PawnColumnDef def = visible[i];
                if (PawnColumnHandlerRegistry.Resolve(def).SkipColumn(def))
                {
                    continue;
                }
                if (labelColumn < 0 && def.Worker is PawnColumnWorker_Label)
                {
                    labelColumn = columns.Count;
                }
                columns.Add(def);
            }
            // A third-party table's pawn list can carry nulls (RimWorld of Magic's
            // MainTabWindow_Golems adds every workstation's GolemPawn unchecked).
            // Vanilla renders those rows blank; presenting one here would be a
            // phantom row that cannot be acted on.
            foreach (Pawn pawn in table.PawnsListForReading)
            {
                if (pawn != null)
                {
                    rows.Add(pawn);
                }
            }
            NoteAddedColumn(tableDefField(table));
        }

        private void NoteAddedColumn(PawnTableDef tableDef)
        {
            int added = tableDef != null && ReferenceEquals(tableDef, snapshotTableDef)
                ? InsertedColumnIndex()
                : -1;
            snapshotTableDef = tableDef;
            snapshotColumns.Clear();
            snapshotColumns.AddRange(columns);
            if (added < 0)
            {
                return;
            }
            string label = ColumnHeaderLabel(columns[added]);
            string text = added == 0
                ? "RimWorldAccess.Shell.Generic.ColumnAddedFirst".Loc(label).ToString()
                : "RimWorldAccess.Shell.Generic.ColumnAddedRightOf".Loc(label, ColumnHeaderLabel(columns[added - 1])).ToString();
            if (focused)
            {
                TolkHelper.SpeakData(text);
            }
            else
            {
                pendingColumnAdded = text;
            }
        }

        /// <summary>Positional, so a duplicate of an existing column still counts as an insertion.</summary>
        private int InsertedColumnIndex()
        {
            if (snapshotColumns.Count == 0 || columns.Count != snapshotColumns.Count + 1)
            {
                return -1;
            }
            int index = 0;
            while (index < snapshotColumns.Count && ReferenceEquals(columns[index], snapshotColumns[index]))
            {
                index++;
            }
            for (int i = index; i < snapshotColumns.Count; i++)
            {
                if (!ReferenceEquals(columns[i + 1], snapshotColumns[i]))
                {
                    return -1;
                }
            }
            return index;
        }

        protected override int ContentItemCount(int region)
        {
            return rows.Count;
        }

        protected override int ContentColumnCount(int region)
        {
            return columns.Count;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            if (column < 0 || column >= columns.Count)
            {
                return new TableColumnInfo("", null, sortable: false);
            }
            PawnColumnDef def = columns[column];
            string label = ColumnHeaderLabel(def);
            // Suppress a tip the label already is, once an icon-headed column's
            // headerTip has been promoted to its label.
            string tip = def.headerTip.NullOrEmpty() || def.headerTip == label ? null : def.headerTip;
            IPawnTableColumnEditor editor = PawnTableColumnEditorRegistry.Resolve(tableSupplier());
            if (tip != null && editor != null)
            {
                tip = editor.FilterHeaderTip(def, tip);
            }
            return new TableColumnInfo(label, tip, def.sortable);
        }

        /// <summary>
        /// Icon-headed columns carry no label text, so the name falls back to the
        /// header tip's first line only — headerTip is a tooltip field and may run
        /// to several sentences — then the handler's own name, then the bare
        /// defName, which is correct for a def that genuinely has no name.
        /// </summary>
        private static string ColumnHeaderLabel(PawnColumnDef def)
        {
            if (!def.label.NullOrEmpty())
            {
                return def.LabelCap.ToString();
            }
            string tipName = HeaderTipFirstLine(def.headerTip);
            if (tipName != null)
            {
                return tipName;
            }
            string handlerLabel = PawnColumnHandlerRegistry.Resolve(def).HeaderLabel(def);
            return !string.IsNullOrEmpty(handlerLabel) ? handlerLabel : def.defName;
        }

        private static string HeaderTipFirstLine(string headerTip)
        {
            if (headerTip.NullOrEmpty())
            {
                return null;
            }
            int newline = headerTip.IndexOf('\n');
            string line = (newline >= 0 ? headerTip.Substring(0, newline) : headerTip).Trim();
            return line.Length > 0 ? line : null;
        }

        /// <summary>Row identity comes from the table's own name column when present, so named forms match what vanilla draws.</summary>
        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index >= 0 && index < rows.Count)
            {
                d.Label = RowLabel(rows[index]);
            }
            return d;
        }

        private string RowLabel(Pawn pawn)
        {
            if (labelColumn >= 0)
            {
                string label = PawnColumnCellReader.CellText(columns[labelColumn], pawn);
                if (!string.IsNullOrEmpty(label))
                {
                    return label;
                }
            }
            return pawn.LabelShortCap.StripTags();
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            if (row < 0 || row >= rows.Count || column < 0 || column >= columns.Count)
            {
                return "";
            }
            string text = PawnColumnCellReader.CellText(columns[column], rows[row]);
            CapturedWidget extra = FindCellExtra(row, column);
            if (extra == null)
            {
                return text;
            }
            // A lettered extra is a real named control joining the cell's text, so
            // speak it. A captionless glyph names nothing useful: in an unreadable
            // cell it IS the cell and speaks the role word, beside real text it
            // stays silent. Enter fires it either way via the NotHandled path.
            if (!string.IsNullOrEmpty(extra.Label) && HasLetterOrDigit(extra.Label))
            {
                string extraLabel = extra.Label.StripTags();
                return string.IsNullOrEmpty(text) ? extraLabel : text + ", " + extraLabel;
            }
            if (PawnColumnHandlerRegistry.Resolve(columns[column]) is FallbackColumnHandler)
            {
                return "RimWorldAccess.Shell.Role.Button".Loc().ToString();
            }
            return text;
        }

        protected override string ContentCellTip(int region, int row, int column)
        {
            if (row < 0 || row >= rows.Count || column < 0 || column >= columns.Count)
            {
                return null;
            }
            PawnColumnDef def = columns[column];
            try
            {
                return PawnColumnHandlerRegistry.Resolve(def).CellTip(def, rows[row]);
            }
            catch (Exception ex)
            {
                PawnColumnCellReader.LogFailure(def, ex);
                return null;
            }
        }

        protected override string ContentCellHint(int region, int row, int column)
        {
            if (row < 0 || row >= rows.Count || column < 0 || column >= columns.Count)
            {
                return null;
            }
            return PawnColumnCellReader.CellHint(columns[column], rows[row]);
        }

        protected override bool TryGetLiveSort(int region, out int column, out bool descending)
        {
            PawnTable table = tableSupplier();
            column = table != null && table.SortingBy != null ? columns.IndexOf(table.SortingBy) : -1;
            descending = table != null && table.SortingDescending;
            return table != null;
        }

        protected override bool ActivateContentCell(int region, int row, int column)
        {
            if (row < 0 || row >= rows.Count || column < 0 || column >= columns.Count)
            {
                return false;
            }
            PawnColumnDef def = columns[column];
            Pawn pawn = rows[row];
            PawnColumnActivation result;
            try
            {
                result = PawnColumnHandlerRegistry.Resolve(def).ActivateCell(def, pawn, tableSupplier());
            }
            catch (Exception ex)
            {
                PawnColumnCellReader.LogFailure(def, ex);
                return false;
            }
            switch (result)
            {
                case PawnColumnActivation.StateChanged:
                    // Toggle doctrine: the cursor didn't move — speak only the new state.
                    AnnounceCurrentCellStateChange();
                    return true;
                case PawnColumnActivation.OpenedUI:
                    // The float menu / dialog announces itself.
                    return true;
                default:
                    // No handler action, so a mod's in-cell button on this cell
                    // still gets its refusal.
                    return ActivateCellExtra(row, column);
            }
        }

        /// <summary>Whether the focused cell's column handler accepts a Left/Right step.</summary>
        private bool CurrentCellAdjustable()
        {
            RefreshModel();
            TableModel model = Model.CurrentTable;
            if (model == null)
            {
                return false;
            }
            int row = model.Rows.Index - 1;
            int column = model.ColumnIndex;
            if (row < 0 || row >= rows.Count || column < 0 || column >= columns.Count)
            {
                return false;
            }
            PawnColumnDef def = columns[column];
            try
            {
                return PawnColumnHandlerRegistry.Resolve(def).CanAdjustCell(def);
            }
            catch (Exception ex)
            {
                PawnColumnCellReader.LogFailure(def, ex);
                return false;
            }
        }

        /// <summary>Steps the focused cell's value in <paramref name="direction"/>, e.g. a work priority.</summary>
        private void AdjustCurrentCell(int direction)
        {
            RefreshModel();
            TableModel model = Model.CurrentTable;
            if (model == null)
            {
                return;
            }
            int row = model.Rows.Index - 1;
            int column = model.ColumnIndex;
            if (row < 0 || row >= rows.Count || column < 0 || column >= columns.Count)
            {
                return;
            }
            PawnColumnDef def = columns[column];
            Pawn pawn = rows[row];
            PawnColumnActivation result;
            try
            {
                result = PawnColumnHandlerRegistry.Resolve(def).AdjustCell(def, pawn, direction, tableSupplier());
            }
            catch (Exception ex)
            {
                PawnColumnCellReader.LogFailure(def, ex);
                return;
            }
            if (result == PawnColumnActivation.StateChanged)
            {
                AnnounceCurrentCellStateChange();
            }
        }

        private bool ColumnsEditable()
        {
            if (PawnTableColumnEditorRegistry.Resolve(tableSupplier()) == null)
            {
                return false;
            }
            RefreshModel();
            TableModel model = Model.CurrentTable;
            return model != null && model.ColumnIndex >= 0 && model.ColumnIndex < columns.Count;
        }

        private bool OnHeaderRow(string refusal)
        {
            TableModel model = Model.CurrentTable;
            if (model != null && model.Rows.Index == 0)
            {
                return true;
            }
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.SpeakData(refusal);
            return false;
        }

        private void RemoveCurrentColumn()
        {
            if (!OnHeaderRow("RimWorldAccess.Shell.Generic.RemoveColumnHeaderOnly".Loc().ToString()))
            {
                return;
            }
            PawnTable table = tableSupplier();
            IPawnTableColumnEditor editor = PawnTableColumnEditorRegistry.Resolve(table);
            TableModel model = Model.CurrentTable;
            if (editor == null || model == null || model.ColumnIndex < 0 || model.ColumnIndex >= columns.Count)
            {
                return;
            }
            PawnColumnDef def = columns[model.ColumnIndex];
            string label = ColumnHeaderLabel(def);
            if (columns.Count <= 1)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData("RimWorldAccess.Shell.Generic.CannotRemoveLastColumn".Loc(label).ToString());
                return;
            }
            try
            {
                editor.RemoveColumn(window, table, def);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("GenericPawnTableScope.RemoveColumn", ex);
                return;
            }
            RefreshModel();
            TolkHelper.SpeakData("RimWorldAccess.Shell.Generic.ColumnRemoved".Loc(label).ToString());
        }

        private void MoveCurrentColumn(int direction)
        {
            if (!OnHeaderRow("RimWorldAccess.Shell.Generic.MoveColumnHeaderOnly".Loc().ToString()))
            {
                return;
            }
            PawnTable table = tableSupplier();
            IPawnTableColumnEditor editor = PawnTableColumnEditorRegistry.Resolve(table);
            TableModel model = Model.CurrentTable;
            if (editor == null || model == null || model.ColumnIndex < 0 || model.ColumnIndex >= columns.Count)
            {
                return;
            }
            int target = model.ColumnIndex + direction;
            PawnColumnDef moved = columns[model.ColumnIndex];
            if (target < 0 || target >= columns.Count)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData("RimWorldAccess.Shell.Screen.CannotReorder".Translate().ToString());
                return;
            }
            try
            {
                editor.MoveColumn(window, table, moved, columns[target], direction);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("GenericPawnTableScope.MoveColumn", ex);
                return;
            }
            RefreshModel();
            int landed = columns.IndexOf(moved);
            TableModel refreshed = Model.CurrentTable;
            if (landed < 0 || refreshed == null)
            {
                return;
            }
            if (refreshed.ColumnIndex != landed)
            {
                refreshed.MoveToColumn(landed);
            }
            TolkHelper.SpeakData("RimWorldAccess.Shell.Generic.ColumnMoved".Loc(ColumnHeaderLabel(moved), landed + 1).ToString());
        }

        /// <summary>
        /// Fires the captured button this cell owns through <see cref="WidgetCapture.RequestActivate"/>,
        /// matched by kind, label and ordinal on the next pass so the mod's own handler runs. Silent:
        /// the click's own side effect announces itself.
        /// </summary>
        private bool ActivateCellExtra(int row, int column)
        {
            CapturedWidget extra = FindCellExtra(row, column);
            if (extra == null)
            {
                return false;
            }
            IReadOnlyList<CapturedWidget> items = WidgetCapture.Items;
            int index = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i], extra))
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                return false;
            }
            int ordinal = OrdinalOf(items, index);
            WidgetCapture.RequestActivate(extra.Kind, extra.Label ?? "", ordinal);
            return true;
        }

        /// <summary>
        /// The captured button whose screen rect lands inside (row, column)'s body
        /// cell: a mod's extra in-cell control the column handler knows nothing
        /// about. Columns that opted out via
        /// <see cref="IPawnColumnHandler.SuppressCapturedExtras"/> are excluded so a
        /// handler already covering its cell never double-attaches. Read fresh off
        /// the live capture pass every time.
        /// </summary>
        private CapturedWidget FindCellExtra(int row, int column)
        {
            if (row < 0 || row >= rows.Count || column < 0 || column >= columns.Count)
            {
                return null;
            }
            PawnColumnDef def = columns[column];
            if (PawnColumnHandlerRegistry.Resolve(def).SuppressCapturedExtras(def))
            {
                return null;
            }
            IReadOnlyList<CapturedWidget> items = WidgetCapture.Items;
            for (int i = 0; i < items.Count; i++)
            {
                CapturedWidget widget = items[i];
                if (widget.Kind != WidgetKind.Button && widget.Kind != WidgetKind.InvisibleButton)
                {
                    continue;
                }
                Vector2 center = widget.ScreenRect.center;
                if (PawnTableExtrasRouter.FindColumnAt(columns, center.x) != column)
                {
                    continue;
                }
                if (PawnTableExtrasRouter.FindRowAt(rows, center.y) != row)
                {
                    continue;
                }
                return widget;
            }
            return null;
        }


        private static bool HasLetterOrDigit(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsLetterOrDigit(text[i]))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>How many earlier widgets in the same pass share this widget's kind and label. Duplicates <see cref="RimWorldAccess.CaptureDescriptor.OrdinalOf"/>, which takes a <see cref="List{T}"/> while <see cref="WidgetCapture.Items"/> is read-only.</summary>
        private static int OrdinalOf(IReadOnlyList<CapturedWidget> items, int index)
        {
            CapturedWidget target = items[index];
            string label = target.Label ?? "";
            int ordinal = 0;
            for (int i = 0; i < index; i++)
            {
                if (items[i].Kind == target.Kind && (items[i].Label ?? "") == label)
                {
                    ordinal++;
                }
            }
            return ordinal;
        }

        /// <summary>Enter on a cell with no action says it is read only.</summary>
        protected override void ActivateContentItem(int region, int index)
        {
            if (index < 0 || index >= rows.Count)
            {
                return;
            }
            AnnounceCurrentCellReadOnly();
        }

        /// <summary>
        /// Sorting runs through the table's own SortBy/RecachePawns, so the
        /// comparer is the worker's language-independent Compare and a cleared
        /// sort is vanilla's true default order, not a snapshot.
        /// </summary>
        protected override int ApplyContentSort(int region, int column, SortCycleResult cycle, int currentRow)
        {
            PawnTable table = tableSupplier();
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
                table.SortBy(columns[column], cycle == SortCycleResult.SortedDescending);
            }
            rows.Clear();
            rows.AddRange(table.PawnsListForReading);
            if (tracked == null)
            {
                return 0;
            }
            int index = rows.IndexOf(tracked);
            return index >= 0 ? index : 0;
        }

        public override void OnPush()
        {
            base.OnPush();
            rows.Clear();
            columns.Clear();
            labelColumn = -1;
            announcedOpen = false;
            snapshotTableDef = null;
            snapshotColumns.Clear();
            pendingColumnAdded = null;
            TypeaheadReset();
        }

        public override void OnPop()
        {
            focused = false;
            PawnTableExtrasRouter.SetActiveTable(null);
            base.OnPop();
        }

        public override void OnUnfocus()
        {
            focused = false;
            base.OnUnfocus();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            focused = true;
            if (pendingColumnAdded != null)
            {
                TolkHelper.SpeakData(pendingColumnAdded);
                pendingColumnAdded = null;
            }
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;
            RefreshModel();
            if (rows.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.UI.GenericWindow.NoElements".Loc());
                return;
            }
            TolkHelper.SpeakData(ContentRegionName(TableRegion));
            AnnounceCurrentItem();
        }

        /// <summary>
        /// The runtime type of the window this instance drives, since the tier serves
        /// whatever subclass a mod adds. A host reached through
        /// <see cref="TryCreateForTable"/> is not a pawn-table tab, and the driver's
        /// own cast declines it, so no ring is drawn there.
        /// </summary>
        Type IPawnTableFocusSource.TabWindowType
        {
            get { return window.GetType(); }
        }

        Pawn IPawnTableFocusSource.FocusedTablePawn
        {
            get
            {
                TableModel table = Model.CurrentTable;
                int row = table != null ? table.Rows.Index - 1 : -1;
                return row >= 0 && row < rows.Count ? rows[row] : null;
            }
        }

        PawnColumnDef IPawnTableFocusSource.FocusedTableColumn
        {
            get
            {
                TableModel table = Model.CurrentTable;
                int column = table != null ? table.ColumnIndex : -1;
                return column >= 0 && column < columns.Count ? columns[column] : null;
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
}
