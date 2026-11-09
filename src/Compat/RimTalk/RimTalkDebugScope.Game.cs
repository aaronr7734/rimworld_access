using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard support for RimTalk's <c>RimTalk.UI.DebugWindow</c>: the 3-view log console, its
    /// filter bar, a details panel for the selected log, and the bottom stats/actions row. The
    /// content is entirely reflected/derived data with no literal widget rects to ring, so the
    /// table model's region/column/sort machinery is the vehicle rather than a WidgetCapture pass.
    ///
    /// Everything here goes through <see cref="RimTalkDebugWindowCompat"/>'s reflection cache; the
    /// RimTalk assembly is never referenced at compile time. Enum values are read back as
    /// <c>.ToString()</c> names rather than resolved as CLR <see cref="Type"/>s: this scope never
    /// writes one, so string comparison suffices.
    ///
    /// Regions, five hand-rolled plus two automatic:
    /// <list type="bullet">
    /// <item>Filters (0): the two free-text filters as a <see cref="TextFieldEditSession"/> pair
    /// over private string fields with no gated setter (MUTATION-C, mirroring DrawSearchField's own
    /// return-assign). They are the only <c>Widgets.TextField</c> calls in the window with no adjacent caption, so
    /// the captured-extras diff can never name them usefully — both fold to the identical
    /// "edit box, blank" — and <see cref="ExcludeFromCapturedExtras"/> drops every TextField-kind
    /// capture so Extras cannot double them.</item>
    /// <item>Table (1): the ACTIVE view's table. Columns and rows branch on the live
    /// <c>_viewMode</c> field, never a scope-local copy, so a mod-side view switch is picked up on
    /// the next refresh. Only GroupedByPawn is sortable — the other two draw plain
    /// <c>Widgets.Label</c> headers.</item>
    /// <item>PawnRequests (2): GroupedByPawn's per-pawn inline expansion as its own table, with
    /// pawn-divider header rows mixed among data rows. Zero rows outside GroupedByPawn or with
    /// nothing expanded, so the region is then invisible to navigation.</item>
    /// <item>Details (3): the selected log's content — header line, FailMsg (Failed only),
    /// Response, and the prompt-message segments as toggle-to-expand rows. DELIBERATELY READ-ONLY:
    /// an expanded segment's <c>GUI.TextArea</c> is editable and feeds Resend, but editing is
    /// deferred; every segment's full text is still reachable.</item>
    /// <item>Stats (4): the six values DoWindowContents caches onto private fields each frame,
    /// read-only. The TokensPerSecond sparkline is NOT reproduced: it has no cached field, and a
    /// 60-sample trend line has no non-visual equivalent worth inventing.</item>
    /// <item>Captured-extras: the ViewMode / filter / row-limit buttons, each opening a REAL
    /// <c>FloatMenu</c> from the mod's own delegates (vehicle A, by forcing the captured button's
    /// click), plus the Details panel's buttons while a log is selected. All carry their own label
    /// or tooltip, and Resend's <c>GUI.enabled</c> gate presents as a disabled row through
    /// <see cref="ExtrasRow.Disabled"/>, so no twin gate is reproduced.</item>
    /// <item>Buttons (declared): the bottom row in DrawBottomActions' draw order.
    /// <see cref="CaptureWindowButtons"/> is false because this window's CONTENT also draws
    /// buttons, and every label goes into <see cref="AdditionalPresentedTexts"/> so the extras diff
    /// cannot double-offer them.</item>
    /// </list>
    ///
    /// DebugWindow overrides neither <c>OnAcceptKeyPressed</c> nor <c>OnCancelKeyPressed</c>, so
    /// <see cref="ScreenScope"/>'s OwnsAccept/OwnsCancel defaults are already correct and no twin
    /// patch is needed. The draw-pass hook is only
    /// <see cref="ShellTextFocus.ReleaseNativeFocus"/> plus
    /// <see cref="TextFieldEditSession.MirrorLive"/> for the two filter fields, and the focus ring
    /// reaches only those two rows: every other region is reflected data with no rect behind it.
    /// </summary>
    public sealed class RimTalkDebugScope : ScreenScope
    {
        private const int FiltersRegion = 0;
        private const int TableRegion = 1;
        private const int PawnRequestsRegion = 2;
        private const int DetailsRegion = 3;
        private const int StatsRegion = 4;

        private enum FilterItem
        {
            PawnFilter,
            TextSearch,
        }

        private readonly Window dialog;
        private readonly TextFieldEditSession session = new TextFieldEditSession();

        // Rebuilt every RefreshContent -- see each region's own remarks.
        private readonly List<object> pawnTableRows = new List<object>(); // GroupedByPawn's pawn-level rows (boxed PawnState)
        private readonly List<PawnRequestRow> pawnRequestRows = new List<PawnRequestRow>();
        private readonly List<DetailRow> detailRows = new List<DetailRow>();
        private string lastViewMode;

        private struct PawnRequestRow
        {
            public bool IsHeader;
            public string HeaderPawnName;
            public object ApiLog;
        }

        private enum DetailRowKind
        {
            Header,
            FailMsg,
            Response,
            SegmentToggle,
            SegmentContent,
            MemoryHint,
            MemoryHeader,
            MemoryChangeType,
            MemoryWeight,
            MemoryEventKey,
            MemoryFlags,
            MemoryNote,
            MemoryReason,
        }

        private struct DetailRow
        {
            public DetailRowKind Kind;
            public int SegmentIndex;
        }

        public RimTalkDebugScope(Window dialog)
        {
            this.dialog = dialog;
            // No custom claims: every interaction rides the base ScreenScope's shared
            // Activate/Cell/Sort/Column claims through the overrides below.

            RegisterPopTeardown(session.CancelIfActive);
        }

        public override string Name
        {
            get { return "rimtalk-debug-window"; }
        }

        protected override int ContentRegionCount
        {
            get { return 5; }
        }

        /// <summary>The region name doubles as the live view-mode label, so it always says which of the three tables the player is in.</summary>
        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case FiltersRegion:
                    return "RimWorldAccess.Compat.RimTalk.Debug.FiltersRegionName".Translate();
                case TableRegion:
                    return ViewModeLabel();
                case PawnRequestsRegion:
                    return "RimWorldAccess.Compat.RimTalk.Debug.PawnRequestsRegionName".Translate();
                case DetailsRegion:
                    return "RimWorldAccess.Compat.RimTalk.Debug.DetailsRegionName".Translate();
                default:
                    return "RimWorldAccess.Compat.RimTalk.Debug.StatsRegionName".Translate();
            }
        }

        private string ViewModeLabel()
        {
            switch (CurrentViewMode())
            {
                case "GroupedByPawn":
                    return Translator.Translate("RimTalk.DebugWindow.ViewByPawn").Resolve();
                case "ActiveRequests":
                    return Translator.Translate("RimTalk.DebugWindow.ViewTalkRequests").Resolve();
                case "MemoryLog":
                    return Translator.Translate("RimTalk.DebugWindow.ViewMemoryLog").Resolve();
                default:
                    return Translator.Translate("RimTalk.DebugWindow.ViewByTime").Resolve();
            }
        }

        private string CurrentViewMode()
        {
            return RimTalkDebugWindowCompat.GetViewMode(dialog) ?? "MainTable";
        }

        protected override void RefreshContent()
        {
            string viewMode = CurrentViewMode();
            if (viewMode != lastViewMode)
            {
                lastViewMode = viewMode;
            }

            pawnTableRows.Clear();
            pawnRequestRows.Clear();
            if (viewMode == "GroupedByPawn")
            {
                RebuildPawnTableRows();
                RebuildPawnRequestRows();
            }

            detailRows.Clear();
            if (viewMode == "MemoryLog")
            {
                RebuildMemoryDetailRows();
                return;
            }
            object selected = RimTalkDebugWindowCompat.GetSelectedLog(dialog);
            if (selected != null)
            {
                RebuildDetailRows(selected);
            }
        }

        private void RebuildMemoryDetailRows()
        {
            if (RimTalkDebugWindowCompat.GetSelectedMemoryLog(dialog) == null)
            {
                detailRows.Add(new DetailRow { Kind = DetailRowKind.MemoryHint });
                return;
            }
            detailRows.Add(new DetailRow { Kind = DetailRowKind.MemoryHeader });
            detailRows.Add(new DetailRow { Kind = DetailRowKind.MemoryChangeType });
            detailRows.Add(new DetailRow { Kind = DetailRowKind.MemoryWeight });
            detailRows.Add(new DetailRow { Kind = DetailRowKind.MemoryEventKey });
            detailRows.Add(new DetailRow { Kind = DetailRowKind.MemoryFlags });
            detailRows.Add(new DetailRow { Kind = DetailRowKind.MemoryNote });
            detailRows.Add(new DetailRow { Kind = DetailRowKind.MemoryReason });
        }

        private void RebuildPawnTableRows()
        {
            IList sorted = RimTalkDebugWindowCompat.GetSortedPawnStates(dialog);
            if (sorted == null)
                return;
            string pawnFilter = RimTalkDebugWindowCompat.GetPawnFilter(dialog);
            string textSearch = RimTalkDebugWindowCompat.GetTextSearch(dialog);
            for (int i = 0; i < sorted.Count; i++)
            {
                object pawnState = sorted[i];
                if (pawnState == null)
                    continue;
                if (!string.IsNullOrWhiteSpace(pawnFilter))
                {
                    string label = RimTalkDebugWindowCompat.PawnStateLabel(pawnState);
                    if (label.IndexOf(pawnFilter.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }
                if (!string.IsNullOrWhiteSpace(textSearch))
                {
                    string lastResponse = RimTalkDebugWindowCompat.GetLastResponseForPawn(dialog, RimTalkDebugWindowCompat.PawnStateLabel(pawnState)) ?? "";
                    if (lastResponse.IndexOf(textSearch.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }
                pawnTableRows.Add(pawnState);
            }
        }

        /// <summary>Mirrors DrawGroupedPawnTable's inline expansion: per expanded pawn, a header divider row then that pawn's log rows in the 6-column, no-Pawn-column shape.</summary>
        private void RebuildPawnRequestRows()
        {
            IList expanded = RimTalkDebugWindowCompat.GetExpandedPawns(dialog);
            if (expanded == null || expanded.Count == 0)
                return;
            for (int i = 0; i < pawnTableRows.Count; i++)
            {
                string label = RimTalkDebugWindowCompat.PawnStateLabel(pawnTableRows[i]);
                if (!expanded.Contains(label))
                    continue;
                pawnRequestRows.Add(new PawnRequestRow { IsHeader = true, HeaderPawnName = label });
                IList logs = RimTalkDebugWindowCompat.GetTalkLogsForPawn(dialog, label);
                if (logs == null)
                    continue;
                for (int j = 0; j < logs.Count; j++)
                {
                    pawnRequestRows.Add(new PawnRequestRow { ApiLog = logs[j] });
                }
            }
        }

        /// <summary>DrawDetailsPanel's draw order: header, FailMsg (Failed only), Response, then each prompt segment plus its content row when expanded.</summary>
        private void RebuildDetailRows(object selected)
        {
            detailRows.Add(new DetailRow { Kind = DetailRowKind.Header });
            if (RimTalkDebugWindowCompat.ApiLogState(selected) == "Failed")
            {
                detailRows.Add(new DetailRow { Kind = DetailRowKind.FailMsg });
            }
            detailRows.Add(new DetailRow { Kind = DetailRowKind.Response });

            IList segments = RimTalkDebugWindowCompat.GetPromptSegments(dialog, selected);
            if (segments == null)
                return;
            ICollection<int> expandedIndices = RimTalkDebugWindowCompat.GetExpandedSegmentIndices(dialog);
            for (int i = 0; i < segments.Count; i++)
            {
                detailRows.Add(new DetailRow { Kind = DetailRowKind.SegmentToggle, SegmentIndex = i });
                if (expandedIndices != null && expandedIndices.Contains(i))
                {
                    detailRows.Add(new DetailRow { Kind = DetailRowKind.SegmentContent, SegmentIndex = i });
                }
            }
        }

        protected override int ContentItemCount(int region)
        {
            switch (region)
            {
                case FiltersRegion:
                    return 2;
                case TableRegion:
                    return TableRowCount();
                case PawnRequestsRegion:
                    return pawnRequestRows.Count;
                case DetailsRegion:
                    return detailRows.Count;
                default:
                    return 6;
            }
        }

        private int TableRowCount()
        {
            switch (CurrentViewMode())
            {
                case "GroupedByPawn":
                    return pawnTableRows.Count;
                case "ActiveRequests":
                    IList active = RimTalkDebugWindowCompat.GetActiveRequestsView(dialog);
                    return active?.Count ?? 0;
                case "MemoryLog":
                    return RimTalkDebugWindowCompat.GetMemoryLogs(dialog)?.Count ?? 0;
                default:
                    IList requests = RimTalkDebugWindowCompat.GetRequests(dialog);
                    return requests?.Count ?? 0;
            }
        }

        // Table contract: columns.

        protected override int ContentColumnCount(int region)
        {
            switch (region)
            {
                case TableRegion:
                    return CurrentViewMode() == "GroupedByPawn" ? 6 : 7;
                case PawnRequestsRegion:
                    return 6;
                default:
                    return 0;
            }
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            if (region == TableRegion)
            {
                switch (CurrentViewMode())
                {
                    case "GroupedByPawn":
                        return GroupedColumnInfo(column);
                    case "ActiveRequests":
                        return ActiveRequestsColumnInfo(column);
                    case "MemoryLog":
                        return MemoryLogColumnInfo(column);
                    default:
                        return MainTableColumnInfo(column, includePawn: true);
                }
            }
            if (region == PawnRequestsRegion)
                return MainTableColumnInfo(column, includePawn: false);
            return null;
        }

        private static TableColumnInfo GroupedColumnInfo(int column)
        {
            switch (column)
            {
                case 0: return new TableColumnInfo(H("HeaderPawn"), null, true);
                case 1: return new TableColumnInfo(H("HeaderResponse"), null, true);
                case 2: return new TableColumnInfo(H("HeaderStatus"), null, true);
                case 3: return new TableColumnInfo(H("HeaderLastTalk"), null, true);
                case 4: return new TableColumnInfo(H("HeaderRequests"), null, true);
                default: return new TableColumnInfo(H("HeaderChattiness"), null, true);
            }
        }

        private static TableColumnInfo ActiveRequestsColumnInfo(int column)
        {
            switch (column)
            {
                case 0: return new TableColumnInfo(H("HeaderTimestamp"));
                case 1: return new TableColumnInfo(H("HeaderInitiator"));
                case 2: return new TableColumnInfo(H("HeaderRecipient"));
                case 3: return new TableColumnInfo(H("HeaderPrompt"));
                case 4: return new TableColumnInfo(H("HeaderType"));
                case 5: return new TableColumnInfo(H("HeaderElapsed"));
                default: return new TableColumnInfo(H("HeaderStatus"));
            }
        }

        private static TableColumnInfo MemoryLogColumnInfo(int column)
        {
            switch (column)
            {
                case 0: return new TableColumnInfo(H("HeaderTimestamp"));
                case 1: return new TableColumnInfo(H("HeaderTick"));
                case 2: return new TableColumnInfo(H("HeaderPawn"));
                case 3: return new TableColumnInfo(H("HeaderTarget"));
                case 4: return new TableColumnInfo(H("HeaderChangeType"));
                case 5: return new TableColumnInfo(H("HeaderWeight"));
                default: return new TableColumnInfo(H("HeaderEventNote"));
            }
        }

        private static string MemoryLogCellText(object entry, int column)
        {
            switch (column)
            {
                case 0: return RimTalkDebugWindowCompat.MemoryLogTimestamp(entry).ToString("HH:mm:ss");
                case 1: return RimTalkDebugWindowCompat.MemoryLogTick(entry).ToString();
                case 2: return RimTalkDebugWindowCompat.MemoryLogPawnName(entry, target: false);
                case 3: return RimTalkDebugWindowCompat.MemoryLogPawnName(entry, target: true);
                case 4: return RimTalkDebugWindowCompat.MemoryLogChangeTypeLabel(entry);
                case 5: return RimTalkDebugWindowCompat.MemoryLogWeight(entry, "0");
                default: return RimTalkDebugWindowCompat.MemoryLogSummary(entry);
            }
        }

        /// <summary>Shared column shape: 7 columns for MainTable, 6 for the PawnRequests nested table, which has no Pawn column.</summary>
        private static TableColumnInfo MainTableColumnInfo(int column, bool includePawn)
        {
            int c = column;
            if (!includePawn && c >= 1)
                c++; // shift past the omitted Pawn column so the switch below matches MainTable's own indices
            switch (c)
            {
                case 0: return new TableColumnInfo(H("HeaderTimestamp"));
                case 1: return new TableColumnInfo(H("HeaderPawn"));
                case 2: return new TableColumnInfo(H("HeaderResponse"));
                case 3: return new TableColumnInfo(H("HeaderType"));
                case 4: return new TableColumnInfo(H("HeaderTimeMs"));
                case 5: return new TableColumnInfo(H("HeaderTokens"));
                default: return new TableColumnInfo(H("HeaderState"));
            }
        }

        /// <summary>Reuses the mod's own resolved column-header text, never a hand-transcribed label.</summary>
        private static string H(string key)
        {
            return Translator.Translate("RimTalk.DebugWindow." + key).Resolve();
        }

        // Table contract: cell text/tips.

        protected override string ContentCellText(int region, int row, int column)
        {
            if (region == TableRegion)
            {
                switch (CurrentViewMode())
                {
                    case "GroupedByPawn":
                        return row >= 0 && row < pawnTableRows.Count ? GroupedCellText(pawnTableRows[row], column) : "";
                    case "ActiveRequests":
                        IList active = RimTalkDebugWindowCompat.GetActiveRequestsView(dialog);
                        return active != null && row >= 0 && row < active.Count ? ActiveRequestCellText(active[row], column) : "";
                    case "MemoryLog":
                        IList memoryLogs = RimTalkDebugWindowCompat.GetMemoryLogs(dialog);
                        return memoryLogs != null && row >= 0 && row < memoryLogs.Count ? MemoryLogCellText(memoryLogs[row], column) : "";
                    default:
                        IList requests = RimTalkDebugWindowCompat.GetRequests(dialog);
                        return requests != null && row >= 0 && row < requests.Count ? ApiLogCellText(requests[row], column, includePawn: true) : "";
                }
            }
            if (region == PawnRequestsRegion)
            {
                if (row < 0 || row >= pawnRequestRows.Count)
                    return "";
                PawnRequestRow r = pawnRequestRows[row];
                if (r.IsHeader)
                    return column == 0 ? "RimWorldAccess.Compat.RimTalk.Debug.PawnRequestsHeader".Translate(r.HeaderPawnName).Resolve() : "";
                return ApiLogCellText(r.ApiLog, column, includePawn: false);
            }
            return "";
        }

        private string GroupedCellText(object pawnState, int column)
        {
            switch (column)
            {
                case 0: return RimTalkDebugWindowCompat.PawnStateLabel(pawnState);
                case 1: return RimTalkDebugWindowCompat.GetLastResponseForPawn(dialog, RimTalkDebugWindowCompat.PawnStateLabel(pawnState)) ?? "";
                case 2: return RimTalkDebugWindowCompat.PawnCanGenerateTalk(pawnState)
                    ? Translator.Translate("RimTalk.DebugWindow.StatusReady").Resolve()
                    : Translator.Translate("RimTalk.DebugWindow.StatusBusy").Resolve();
                case 3: return RimTalkDebugWindowCompat.PawnLastTalkTick(pawnState).ToString();
                case 4: return RimTalkDebugWindowCompat.PawnRequestCount(dialog, RimTalkDebugWindowCompat.PawnStateLabel(pawnState)).ToString();
                default: return RimTalkDebugWindowCompat.PawnChattiness(pawnState).ToString("F2");
            }
        }

        private static string ActiveRequestCellText(object talkRequest, int column)
        {
            switch (column)
            {
                case 0: return RimTalkDebugWindowCompat.TalkRequestCreatedTime(talkRequest).ToString("HH:mm:ss");
                case 1: return RimTalkDebugWindowCompat.PawnLabelOrDash(RimTalkDebugWindowCompat.TalkRequestInitiator(talkRequest));
                case 2:
                {
                    Pawn recipient = RimTalkDebugWindowCompat.TalkRequestRecipient(talkRequest);
                    Pawn initiator = RimTalkDebugWindowCompat.TalkRequestInitiator(talkRequest);
                    return recipient != null && recipient != initiator ? RimTalkDebugWindowCompat.PawnLabelOrDash(recipient) : "-";
                }
                case 3: return RimTalkDebugWindowCompat.TalkRequestPrompt(talkRequest) ?? "";
                case 4: return RimTalkDebugWindowCompat.TalkRequestTalkType(talkRequest);
                case 5: return RimTalkDebugWindowCompat.TalkRequestElapsedSeconds(talkRequest) + "s";
                default: return Translator.Translate("RimTalk.DebugWindow.State" + RimTalkDebugWindowCompat.TalkRequestStatus(talkRequest)).Resolve();
            }
        }

        private static string ApiLogCellText(object apiLog, int column, bool includePawn)
        {
            int c = column;
            if (!includePawn && c >= 1)
                c++;
            switch (c)
            {
                case 0: return RimTalkDebugWindowCompat.ApiLogTimestamp(apiLog).ToString("HH:mm:ss");
                case 1: return RimTalkDebugWindowCompat.ApiLogName(apiLog) ?? "-";
                case 2: return RimTalkDebugWindowCompat.ApiLogResponseOrGenerating(apiLog);
                case 3: return RimTalkDebugWindowCompat.ApiLogInteractionType(apiLog) ?? "-";
                case 4: return RimTalkDebugWindowCompat.ApiLogElapsedMsText(apiLog);
                case 5: return RimTalkDebugWindowCompat.ApiLogTokensText(apiLog);
                default: return Translator.Translate("RimTalk.DebugWindow.State" + RimTalkDebugWindowCompat.ApiLogState(apiLog)).Resolve();
            }
        }

        /// <summary>
        /// DrawRequestRow's per-row TipRegion, shared by MainTable and the PawnRequests nested
        /// table. Not GroupedByPawn's pawn rows, ActiveRequests' rows, or a header divider row —
        /// none of those draws a TipRegion.
        /// </summary>
        protected override string ContentCellTip(int region, int row, int column)
        {
            bool isApiLogRow = (region == TableRegion && CurrentViewMode() == "MainTable")
                || (region == PawnRequestsRegion && row >= 0 && row < pawnRequestRows.Count && !pawnRequestRows[row].IsHeader);
            if (!isApiLogRow)
                return null;
            return Translator.Translate("RimTalk.DebugWindow.TooltipSelectForDetails").Resolve();
        }

        // Table contract: sort. GroupedByPawn only; the other two report Sortable:false, so the
        // base class never calls ApplyContentSort for them.

        /// <summary>
        /// GetSortedPawnStates (the mod's private method, invoked reflectively — vehicle A) is
        /// driven by the same <c>_sortColumn</c>/<c>_sortAscending</c> fields DrawSortableHeader
        /// mutates, so this writes those fields and calls the mod's method rather than re-deriving
        /// its comparers.
        ///
        /// The mod's own sort is a permanent 2-state toggle, but the shared
        /// <see cref="TableModel"/> cycle is 3-state. Cleared maps to <c>_sortColumn = ""</c>, a
        /// state the mod starts in and displays correctly but can never reach by clicking:
        /// GetSortedPawnStates' switch falls through to unsorted, and the header's arrow test is
        /// false for every column. A keyboard-only affordance, but always a state the mod's own
        /// code supports and a sighted viewer reads correctly.
        /// </summary>
        protected override int ApplyContentSort(int region, int column, SortCycleResult cycle, int currentRow)
        {
            if (region != TableRegion || CurrentViewMode() != "GroupedByPawn")
                return -1;

            object current = currentRow >= 0 && currentRow < pawnTableRows.Count ? pawnTableRows[currentRow] : null;
            string key = GroupedSortKey(column);

            if (cycle == SortCycleResult.Cleared)
            {
                RimTalkDebugWindowCompat.SetSortColumn(dialog, "");
            }
            else
            {
                RimTalkDebugWindowCompat.SetSortColumn(dialog, key);
                RimTalkDebugWindowCompat.SetSortAscending(dialog, cycle == SortCycleResult.SortedAscending);
            }

            RebuildPawnTableRows();
            if (current == null)
                return 0;
            int idx = pawnTableRows.IndexOf(current);
            return idx >= 0 ? idx : 0;
        }

        /// <summary>The mod's own sort-key strings (GetSortedPawnStates' switch), keyed by our column order.</summary>
        private static string GroupedSortKey(int column)
        {
            switch (column)
            {
                case 0: return "RimTalk.DebugWindow.HeaderPawn";
                case 1: return "RimTalk.DebugWindow.HeaderResponse";
                case 2: return "RimTalk.DebugWindow.HeaderStatus";
                case 3: return "RimTalk.DebugWindow.HeaderLastTalk";
                case 4: return "RimTalk.DebugWindow.HeaderRequests";
                default: return "RimTalk.DebugWindow.HeaderChattiness";
            }
        }

        // Row descriptions.

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            switch (region)
            {
                case FiltersRegion:
                    DescribeFiltersRow(d, index);
                    break;
                case TableRegion:
                    // On plain landing, with no column focused, the row's identity is its
                    // column-0 cell text.
                    d.Label = ContentCellText(TableRegion, index, 0);
                    break;
                case PawnRequestsRegion:
                    DescribePawnRequestsRow(d, index);
                    break;
                case DetailsRegion:
                    DescribeDetailsRow(d, index);
                    break;
                case StatsRegion:
                    DescribeStatsRow(d, index);
                    break;
            }
            return d;
        }

        private void DescribeFiltersRow(ElementDescription d, int index)
        {
            d.Role = ElementRole.TextField;
            if (index == (int)FilterItem.PawnFilter)
            {
                d.Label = "RimWorldAccess.Compat.RimTalk.Debug.PawnFilterLabel".Translate();
                string value = RimTalkDebugWindowCompat.GetPawnFilter(dialog);
                if (string.IsNullOrEmpty(value)) d.ValueBlank = true; else d.Value = value;
            }
            else
            {
                d.Label = "RimWorldAccess.Compat.RimTalk.Debug.TextSearchLabel".Translate();
                string value = RimTalkDebugWindowCompat.GetTextSearch(dialog);
                if (string.IsNullOrEmpty(value)) d.ValueBlank = true; else d.Value = value;
            }
        }

        private void DescribePawnRequestsRow(ElementDescription d, int index)
        {
            if (index < 0 || index >= pawnRequestRows.Count)
                return;
            PawnRequestRow r = pawnRequestRows[index];
            if (r.IsHeader)
            {
                d.Label = "RimWorldAccess.Compat.RimTalk.Debug.PawnRequestsHeader".Translate(r.HeaderPawnName);
                d.Role = ElementRole.None;
                d.ReadOnly = true;
                return;
            }
            d.Label = ApiLogCellText(r.ApiLog, 0, includePawn: false);
        }

        private void DescribeDetailsRow(ElementDescription d, int index)
        {
            if (CurrentViewMode() == "MemoryLog")
            {
                DescribeMemoryDetailsRow(d, index);
                return;
            }
            object selected = RimTalkDebugWindowCompat.GetSelectedLog(dialog);
            if (selected == null || index < 0 || index >= detailRows.Count)
                return;
            DetailRow row = detailRows[index];
            d.Role = ElementRole.None;
            d.ReadOnly = true;
            switch (row.Kind)
            {
                case DetailRowKind.Header:
                {
                    string timestamp = RimTalkDebugWindowCompat.ApiLogTimestamp(selected).ToString("yyyy-MM-dd HH:mm:ss");
                    string name = (RimTalkDebugWindowCompat.ApiLogName(selected) ?? "-").Trim();
                    string interactionType = RimTalkDebugWindowCompat.ApiLogInteractionType(selected);
                    d.Label = string.IsNullOrEmpty(interactionType)
                        ? "RimWorldAccess.Compat.RimTalk.Debug.DetailHeaderShort".Translate(timestamp, name)
                        : "RimWorldAccess.Compat.RimTalk.Debug.DetailHeader".Translate(timestamp, name, interactionType);
                    break;
                }
                case DetailRowKind.FailMsg:
                    d.Label = Translator.Translate("RimTalk.DebugWindow.FailMsg").Resolve();
                    break;
                case DetailRowKind.Response:
                    d.Label = Translator.Translate("RimTalk.DebugWindow.Response").Resolve();
                    string response = RimTalkDebugWindowCompat.ApiLogResponse(selected);
                    if (string.IsNullOrEmpty(response)) d.ValueBlank = true; else d.Value = response;
                    break;
                case DetailRowKind.SegmentToggle:
                {
                    bool expanded = IsSegmentExpanded(row.SegmentIndex);
                    d.Role = ElementRole.Button;
                    d.ReadOnly = false;
                    d.Label = SegmentToggleLabel(selected, row.SegmentIndex, expanded);
                    break;
                }
                case DetailRowKind.SegmentContent:
                    d.Label = Translator.Translate("RimTalk.DebugWindow.PromptMessages").Resolve();
                    string content = RimTalkDebugWindowCompat.SegmentContent(dialog, selected, row.SegmentIndex);
                    if (string.IsNullOrEmpty(content)) d.ValueBlank = true; else d.Value = content;
                    break;
            }
        }

        private void DescribeMemoryDetailsRow(ElementDescription d, int index)
        {
            if (index < 0 || index >= detailRows.Count)
                return;
            d.Role = ElementRole.None;
            d.ReadOnly = true;
            object entry = RimTalkDebugWindowCompat.GetSelectedMemoryLog(dialog);
            if (detailRows[index].Kind == DetailRowKind.MemoryHint || entry == null)
            {
                d.Label = H("SelectRowHint");
                return;
            }
            switch (detailRows[index].Kind)
            {
                case DetailRowKind.MemoryHeader:
                {
                    string time = RimTalkDebugWindowCompat.MemoryLogTimestamp(entry).ToString("yyyy-MM-dd HH:mm:ss");
                    int tick = RimTalkDebugWindowCompat.MemoryLogTick(entry);
                    string source = RimTalkDebugWindowCompat.MemoryLogPawnName(entry, target: false);
                    string target = RimTalkDebugWindowCompat.MemoryLogPawnName(entry, target: true);
                    d.Label = target == "-"
                        ? "RimWorldAccess.Compat.RimTalk.Debug.MemoryDetailHeader".Translate(time, tick, source)
                        : "RimWorldAccess.Compat.RimTalk.Debug.MemoryDetailHeaderWithTarget".Translate(time, tick, source, target);
                    return;
                }
                case DetailRowKind.MemoryChangeType:
                    d.Label = H("HeaderChangeType");
                    d.Value = RimTalkDebugWindowCompat.MemoryLogChangeTypeLabel(entry);
                    return;
                case DetailRowKind.MemoryWeight:
                    d.Label = H("HeaderWeight");
                    d.Value = RimTalkDebugWindowCompat.MemoryLogWeight(entry, "F1");
                    return;
                case DetailRowKind.MemoryEventKey:
                    d.Label = H("HeaderEventKey");
                    d.Value = DashIfEmpty(RimTalkDebugWindowCompat.MemoryLogEventKey(entry));
                    return;
                case DetailRowKind.MemoryFlags:
                    d.Label = "RimWorldAccess.Compat.RimTalk.Debug.MemoryFlags".Translate(
                        (RimTalkDebugWindowCompat.MemoryLogIsDirective(entry) ? "Yes" : "No").Translate(),
                        (RimTalkDebugWindowCompat.MemoryLogIsCoreTrauma(entry) ? "Yes" : "No").Translate());
                    return;
                case DetailRowKind.MemoryNote:
                    d.Label = H("HeaderNoteContent");
                    d.Value = DashIfEmpty(RimTalkDebugWindowCompat.MemoryLogNote(entry));
                    return;
                default:
                    d.Label = H("HeaderReasonContext");
                    d.Value = DashIfEmpty(RimTalkDebugWindowCompat.MemoryLogDetails(entry));
                    return;
            }
        }

        private static string DashIfEmpty(string text)
        {
            return string.IsNullOrEmpty(text) ? "-" : text;
        }

        private bool IsSegmentExpanded(int segmentIndex)
        {
            ICollection<int> expanded = RimTalkDebugWindowCompat.GetExpandedSegmentIndices(dialog);
            return expanded != null && expanded.Contains(segmentIndex);
        }

        /// <summary>Mirrors GetPromptMessagePreview/GetRoleLabel's formatting plus the mod's "[+]"/"[-]" expand marker.</summary>
        private string SegmentToggleLabel(object selected, int segmentIndex, bool expanded)
        {
            object segment = RimTalkDebugWindowCompat.GetSegmentAt(dialog, selected, segmentIndex);
            if (segment == null)
                return "";
            string entryName = RimTalkDebugWindowCompat.SegmentEntryName(segment);
            string roleLabel = RimTalkDebugWindowCompat.SegmentRoleLabel(dialog, segment);
            string preview = RimTalkDebugWindowCompat.SegmentPreview(segment);
            string marker = expanded ? "[-]" : "[+]";
            return $"{marker} {segmentIndex + 1}. {entryName} ({roleLabel}): {preview}";
        }

        private void DescribeStatsRow(ElementDescription d, int index)
        {
            d.Role = ElementRole.None;
            d.ReadOnly = true;
            switch (index)
            {
                case 0:
                    d.Label = Translator.Translate("RimTalk.DebugWindow.AIStatus").Resolve();
                    d.Value = RimTalkDebugWindowCompat.GetAiStatus(dialog);
                    break;
                case 1:
                    d.Label = Translator.Translate("RimTalk.DebugWindow.TotalCalls").Resolve();
                    d.Value = RimTalkDebugWindowCompat.GetTotalCalls(dialog).ToString("N0");
                    break;
                case 2:
                    d.Label = Translator.Translate("RimTalk.DebugWindow.TotalTokens").Resolve();
                    d.Value = RimTalkDebugWindowCompat.GetTotalTokens(dialog).ToString("N0");
                    break;
                case 3:
                    d.Label = Translator.Translate("RimTalk.DebugWindow.AvgCallsPerMin").Resolve();
                    d.Value = RimTalkDebugWindowCompat.GetAvgCallsPerMin(dialog).ToString("F2");
                    break;
                case 4:
                    d.Label = Translator.Translate("RimTalk.DebugWindow.AvgTokensPerMin").Resolve();
                    d.Value = RimTalkDebugWindowCompat.GetAvgTokensPerMin(dialog).ToString("F2");
                    break;
                default:
                    d.Label = Translator.Translate("RimTalk.DebugWindow.AvgTokensPerCall").Resolve();
                    d.Value = RimTalkDebugWindowCompat.GetAvgTokensPerCall(dialog).ToString("F2");
                    break;
            }
        }

        // Activation.

        protected override void ActivateContentItem(int region, int index)
        {
            switch (region)
            {
                case FiltersRegion:
                    BeginEditFilter((FilterItem)index);
                    return;
                case TableRegion:
                    ActivateTableRow(index);
                    return;
                case PawnRequestsRegion:
                    ActivatePawnRequestsRow(index);
                    return;
                case DetailsRegion:
                    ActivateDetailsRow(index);
                    return;
                default:
                    AnnounceCurrentItem();
                    return;
            }
        }

        private void ActivateTableRow(int index)
        {
            switch (CurrentViewMode())
            {
                case "GroupedByPawn":
                    if (index < 0 || index >= pawnTableRows.Count)
                        return;
                    // Row default: toggle this pawn's inline expansion (MUTATION-C -- mirrors
                    // DrawGroupedPawnTable's own ButtonInvisible click, a bare List<string> with no
                    // gated Toggle method).
                    string label = RimTalkDebugWindowCompat.PawnStateLabel(pawnTableRows[index]);
                    RimTalkDebugWindowCompat.ToggleExpandedPawn(dialog, label);
                    RefreshModel();
                    AnnounceCurrentItem();
                    return;
                case "ActiveRequests":
                    // TalkRequest rows have no selection concept; only their pawn-name cells are
                    // clickable.
                    AnnounceCurrentItem();
                    return;
                case "MemoryLog":
                    IList memoryLogs = RimTalkDebugWindowCompat.GetMemoryLogs(dialog);
                    if (memoryLogs == null || index < 0 || index >= memoryLogs.Count)
                        return;
                    RimTalkDebugWindowCompat.SelectMemoryLog(dialog, memoryLogs[index]);
                    RefreshModel();
                    AnnounceCurrentItem();
                    return;
                default:
                    IList requests = RimTalkDebugWindowCompat.GetRequests(dialog);
                    if (requests == null || index < 0 || index >= requests.Count)
                        return;
                    SelectLog(requests[index]);
                    return;
            }
        }

        private void ActivatePawnRequestsRow(int index)
        {
            if (index < 0 || index >= pawnRequestRows.Count)
                return;
            PawnRequestRow r = pawnRequestRows[index];
            if (r.IsHeader)
            {
                AnnounceCurrentItem();
                return;
            }
            SelectLog(r.ApiLog);
        }

        /// <summary>MUTATION-C: mirrors DrawRequestRow's own row-click field assignment (_selectedLog + _stickToBottom, both bare private fields with no gated setter).</summary>
        private void SelectLog(object apiLog)
        {
            RimTalkDebugWindowCompat.SelectLog(dialog, apiLog);
            RefreshModel();
            AnnounceCurrentItem();
        }

        private void ActivateDetailsRow(int index)
        {
            if (index < 0 || index >= detailRows.Count)
                return;
            DetailRow row = detailRows[index];
            if (row.Kind != DetailRowKind.SegmentToggle || CurrentViewMode() == "MemoryLog")
            {
                AnnounceCurrentItem();
                return;
            }
            RimTalkDebugWindowCompat.ToggleExpandedSegment(dialog, row.SegmentIndex);
            RefreshModel();
            AnnounceCurrentItem();
        }

        /// <summary>Camera-jump cells: the Pawn column and ActiveRequests' Initiator/Recipient columns reproduce UIUtil.DrawClickablePawnName's click. Every other column falls through to the row default.</summary>
        protected override bool ActivateContentCell(int region, int row, int column)
        {
            if (region != TableRegion && region != PawnRequestsRegion)
                return false;

            if (region == TableRegion && CurrentViewMode() == "ActiveRequests")
            {
                if (column != 1 && column != 2)
                    return false;
                IList active = RimTalkDebugWindowCompat.GetActiveRequestsView(dialog);
                if (active == null || row < 0 || row >= active.Count)
                    return false;
                Pawn pawn = column == 1
                    ? RimTalkDebugWindowCompat.TalkRequestInitiator(active[row])
                    : RimTalkDebugWindowCompat.TalkRequestRecipient(active[row]);
                return JumpToPawn(pawn);
            }

            if (region == TableRegion && CurrentViewMode() == "MemoryLog")
            {
                IList memoryLogs = RimTalkDebugWindowCompat.GetMemoryLogs(dialog);
                if ((column != 2 && column != 3) || memoryLogs == null || row < 0 || row >= memoryLogs.Count)
                    return false;
                return JumpToPawn(RimTalkDebugWindowCompat.MemoryLogPawn(memoryLogs[row], target: column == 3));
            }

            if (region == TableRegion && CurrentViewMode() == "GroupedByPawn")
            {
                if (column != 0 || row < 0 || row >= pawnTableRows.Count)
                    return false;
                return JumpToPawn(RimTalkDebugWindowCompat.PawnStateOwner(pawnTableRows[row]));
            }

            // PawnRequestsRegion has no Pawn column — the header divider names the pawn — so no
            // cell there is camera-jumpable.
            if (region != TableRegion || column != 1)
                return false;
            IList requests = RimTalkDebugWindowCompat.GetRequests(dialog);
            object apiLog = requests != null && row >= 0 && row < requests.Count ? requests[row] : null;
            if (apiLog == null)
                return false;
            return JumpToPawn(RimTalkDebugWindowCompat.ApiLogPawn(dialog, apiLog));
        }

        /// <summary>Mirrors UIUtil.DrawClickablePawnName's click, corpse-or-live-pawn branch included. TryJump returns void, so the bool reports only whether a target existed.</summary>
        private static bool JumpToPawn(Pawn pawn)
        {
            if (pawn == null)
                return false;
            if (pawn.Dead && pawn.Corpse != null && pawn.Corpse.Spawned)
            {
                CameraJumper.TryJump(pawn.Corpse, CameraJumper.MovementMode.Cut);
                return true;
            }
            if (!pawn.Dead && pawn.Spawned)
            {
                CameraJumper.TryJump(pawn, CameraJumper.MovementMode.Cut);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Only the Filters rows have a literal widget behind them: DrawSearchField's two
        /// <c>Widgets.TextField</c> calls, in this region's order, drawn before the detail panes'
        /// raw <c>GUI.TextArea</c> boxes so their ordinals hold; every other region is reflected data.
        /// </summary>
        protected internal override UnityEngine.Rect FocusedContentRect()
        {
            ListModel region = Model.CurrentRegion;
            if (Model.RegionIndex != FiltersRegion || region == null || region.Index < 0 || region.Index > 1)
            {
                return default(UnityEngine.Rect);
            }
            int capture = WidgetCapture.IndexOfKind(WidgetKind.TextField, region.Index);
            return capture < 0 ? default(UnityEngine.Rect) : WidgetCapture.Items[capture].VisibleScreenRect;
        }

        // Filters region editing, reached only through ActivateContentItem above; the base
        // ScreenScope's shared Activate claim routes Enter there.

        private void BeginEditFilter(FilterItem item)
        {
            string current = item == FilterItem.PawnFilter
                ? RimTalkDebugWindowCompat.GetPawnFilter(dialog)
                : RimTalkDebugWindowCompat.GetTextSearch(dialog);
            string labelKey = item == FilterItem.PawnFilter
                ? "RimWorldAccess.Compat.RimTalk.Debug.PawnFilterLabel"
                : "RimWorldAccess.Compat.RimTalk.Debug.TextSearchLabel";

            // The mod's own Widgets.TextField call imposes no length cap.
            var spec = new TextFieldSpec(labelKey: "RimWorldAccess.TextInput.LabelDefault", maxLength: null, minLength: 0);
            session.EnterEdit(
                current,
                spec,
                labelKey.Translate(),
                value => ApplyFilterValue(item, value),
                ReAnnounceFiltersRow,
                announcePrompt: true);
        }

        private void ApplyFilterValue(FilterItem item, string value)
        {
            if (item == FilterItem.PawnFilter)
                RimTalkDebugWindowCompat.SetPawnFilter(dialog, value ?? "");
            else
                RimTalkDebugWindowCompat.SetTextSearch(dialog, value ?? "");
        }

        private void ReAnnounceFiltersRow()
        {
            AnnounceCurrentItem();
        }

        // Buttons region: declared, not captured, because this window's content also draws
        // buttons.

        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction(
                    ResolveOwnLabel("EnableRimTalk", "EnableFallback"),
                    ToggleEnabled,
                    "rimTalkDebug.toggleEnabled",
                    check: RimTalkDebugWindowCompat.GetIsEnabled() ? CheckState.Checked : CheckState.Unchecked));
                actions.Add(new ScreenAction(ResolveOwnLabel("ModSettings", "ModSettingsFallback"), OpenModSettings, "rimTalkDebug.modSettings"));
                actions.Add(new ScreenAction(ResolveOwnLabel("Export", "ExportFallback"), ExportLogs, "rimTalkDebug.export"));
                actions.Add(new ScreenAction(ResolveOwnLabel("ResetLogs", "ResetLogsFallback"), ResetLogs, "rimTalkDebug.resetLogs"));
                return actions;
            }
        }

        /// <summary>Reuses the mod's resolved button text, falling back to an RWA key only when it is missing.</summary>
        private static string ResolveOwnLabel(string modKey, string fallbackKey)
        {
            return CompatText.ResolveOrFallback("RimTalk.DebugWindow." + modKey, "RimWorldAccess.Compat.RimTalk.Debug." + fallbackKey);
        }

        /// <summary>These same four resolved labels must never ALSO surface through the captured-extras diff.</summary>
        protected override IEnumerable<string> AdditionalPresentedTexts
        {
            get
            {
                yield return ResolveOwnLabel("EnableRimTalk", "EnableFallback");
                yield return ResolveOwnLabel("ModSettings", "ModSettingsFallback");
                yield return ResolveOwnLabel("Export", "ExportFallback");
                yield return ResolveOwnLabel("ResetLogs", "ResetLogsFallback");
            }
        }

        /// <summary>MUTATION-C: mirrors DrawBottomActions' own CheckboxLabeled return-assign (RimTalkSettings.IsEnabled is a bare public field with no gated setter) -- including the mod's own omission of ModSettings.Write() afterward; this checkbox does not persist immediately in vanilla either.</summary>
        private void ToggleEnabled()
        {
            RimTalkDebugWindowCompat.SetIsEnabled(!RimTalkDebugWindowCompat.GetIsEnabled());
        }

        private void OpenModSettings()
        {
            RimTalkNarrativeCompat.TryOpenModSettings();
        }

        /// <summary>UIUtil.ExportLogs(_requests) verbatim, including its three hardcoded-English Messages.Message outcomes, which the message-to-speech bridge already speaks.</summary>
        private void ExportLogs()
        {
            RimTalkDebugWindowCompat.ExportLogs(dialog);
        }

        /// <summary>The window's own private Reset() verbatim.</summary>
        private void ResetLogs()
        {
            RimTalkDebugWindowCompat.ResetLogs(dialog);
            RefreshModel();
        }

        // Captured-extras region: the ViewMode / filter / row-limit buttons, plus the Details
        // panel's buttons while a log is selected.

        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        /// <summary>Drops every TextField-kind capture; the two hand-built Filters rows own those fields instead.</summary>
        protected override bool ExcludeFromCapturedExtras(CapturedWidget widget)
        {
            return widget.Kind == WidgetKind.TextField;
        }

        // Lifecycle.

        protected override string ComposeOpenAnnouncement()
        {
            return "RimWorldAccess.Compat.RimTalk.Debug.Opened".Translate();
        }
    }
}
