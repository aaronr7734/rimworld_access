using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Reflection facade over RimWorld 1.7's frame profiler (<c>Verse.ProfilerUI</c> plus
    /// <c>Waylith.Profiling</c>). Neither namespace exists in the 1.6 assemblies this mod compiles
    /// against, so every member is resolved by name once and the whole surface declines together
    /// (<see cref="Ready"/> false) when any piece is missing. Hot reads compile to delegates;
    /// key-rate calls go through <see cref="MethodInfo.Invoke"/>.
    /// </summary>
    internal static class FrameProfilerAccess
    {
        internal static Type WindowType { get; private set; }
        internal static Type SessionType { get; private set; }
        internal static Type SectionType { get; private set; }
        internal static Type ViewType { get; private set; }
        internal static Type CallTreeType { get; private set; }
        internal static Type AllocType { get; private set; }
        internal static Type VramType { get; private set; }
        internal static Type SettingsType { get; private set; }
        internal static Type ThreadFilterType { get; private set; }
        internal static Type ProfilerGuiType { get; private set; }

        private static bool resolved;
        private static bool ready;

        // Window_FrameProfiler.
        private static Func<object, object> windowSession, windowFlamechart, windowThreadFilter, windowSettings,
            windowActiveSection, windowCallTree, windowAllocTable, windowVramTable, windowHistogram;
        private static Func<object, string> windowStatusText, windowFrameStepper, windowSearchQuery;
        private static MethodInfo windowCopyView;

        // ProfilerSession.
        private static Func<object, bool> sessionHasData, sessionIsPaused, sessionIsSpikes, sessionFollowingNewest;
        private static Func<object, object> sessionSnapshot, sessionSource, sessionTree, sessionSearch;
        private static MethodInfo sessionStep, sessionJumpToNewest, sessionSetPaused, sessionBeginSelection,
            sessionExtendSelection, sessionClearSelection, sessionSelectZoneTimeRange, sessionTryGetSnapshotAt, sessionTryGetSelectionBand, sessionInvalidate;
        private static MethodInfo frameStoreCountOf;

        // Row sections and their list view.
        private static Func<object, object> sectionView;
        private static MethodInfo sectionCollapseSelected, sectionOpenContextMenu;
        private static Func<object, IList> viewRows;
        private static Func<object, int> viewSelectedIndex;
        private static Func<object, Array> viewColumns;
        private static Func<object, Delegate> viewLabelHeader, viewLabelSort, viewRootCrumb;
        private static Func<object, string> viewDetailHeader, viewSearchStatus;
        private static Func<object, IList> viewCrumbs;
        private static MethodInfo viewSelect, viewMoveSelection, viewActivateSelected, viewCopySelectedRow, viewSelectNextMatch;
        private static Func<object, bool> callTreeFlamegraph;
        private static Func<object, string> allocFooter, vramFooter;

        // ProfilerRowData.
        private static Func<object, string> rowLabel, rowDetail, rowPercentText, rowTotalText, rowSelfText,
            rowDeltaText, rowCallsText, rowAllocText;
        private static Func<object, int> rowDepth;
        private static Func<object, bool> rowHasChildren, rowExpanded, rowDimmed;
        private static MethodInfo rowEnsureDisplayText;

        // ProfilerColumn.
        private static Func<object, object> columnKind;
        private static Func<object, Delegate> columnHeader, columnSort;

        // Profiler tree model (sorting).
        private static Func<object, object> treeActiveSortColumn;
        private static Func<object, bool> treeSortAscending;

        // Settings drawer and thread filter.
        private static Func<object, bool> settingsIsOpen;
        private static MethodInfo settingsToggle;
        private static Func<object, IList> threadItems;
        private static FieldInfo threadOpenField, threadPopupRect;
        private static MethodInfo threadToggleSelectAll;
        private static Func<object, int> threadItemId;
        private static Func<object, string> threadItemLabel, threadItemBusy, threadItemUtil;
        private static Func<object, string> threadButtonLabel, threadButtonBusy;
        private static MethodInfo registryIsSelected, registrySetSelected, registrySelectOnly;
        private static PropertyInfo registryAllSelected;

        // Histogram.
        private static Func<object, object> histogramPauseMarks;
        private static Func<object, int> barRingCount;
        private static PropertyInfo barRingItem;

        // Flamechart.
        private static FieldInfo flameZoneSelected;
        private static MethodInfo flameHighlightZone;
        private static MethodInfo flameZoneSelfUs;
        private static ConstructorInfo zoneSelectionCtor;

        // Formatting helpers the window itself uses.
        private static MethodInfo appendMs, appendAllocBytes, appendFrameMs;

        // Waylith.Profiling.
        private static MethodInfo collectChunks, getLabel, pathLabel;
        private static Type zoneChunkType;
        private static Func<object, int> snapshotFrameIndex, snapshotOrdinal, snapshotSectionCount, snapshotGen0, snapshotGen1,
            snapshotGen2, snapshotPauseCount, snapshotAllocObjects, snapshotTickDelta;
        private static Func<object, float> snapshotTotalMs, snapshotPauseMs;
        private static Func<object, long> snapshotPromoted, snapshotAllocBytes, snapshotBegin, snapshotEnd;
        private static Func<object, bool> snapshotIsSpike;
        private static Func<object, Array> snapshotSections;
        private static Func<object, int> sectionThreadId;
        private static Func<object, object> sectionNameLabel;
        private static Func<object, long> sectionStart, sectionEnd, sectionBusy;
        private static Func<object, long> chunkBase;
        private static Func<object, Array> chunkAllocStats, snapshotPathEntries;
        private static Func<object, int> allocTotal, allocSelf, allocCount, sectionFirstPath, pathAllocBytes;
        private static Func<object, float> pathSelfUs, sessionBudgetMs;
        private static Func<object, Array> snapshotPauses;
        private static Func<object, int> pauseStart, pauseSuspended, pauseEnd, pauseGeneration, pauseThread;
        private static Func<object, object> pauseReason;
        private static Func<object, long> pausePromoted, pauseCollected;
        private static MethodInfo describePause;
        private static Func<object, Array> chunkNodes;
        private static Func<object, int> chunkCount;
        private static Func<object, int> nodePathId, nodeSubtreeEnd;
        private static Func<object, long> nodeStart, nodeEnd;
        private static Func<object, bool> nodeIsOpen;

        /// <summary>Resolves every member on first use; false on 1.6 or when 1.7 renamed something.</summary>
        internal static bool Ready
        {
            get
            {
                if (!resolved)
                {
                    resolved = true;
                    try
                    {
                        ready = Resolve();
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Error("Frame profiler access failed to resolve: " + ex);
                        ready = false;
                    }
                }
                return ready;
            }
        }

        /// <summary>The window type, resolved without the rest of the facade, so registration costs nothing on 1.6.</summary>
        internal static Type ResolveWindowType()
        {
            return AccessTools.TypeByName("Verse.ProfilerUI.Window_FrameProfiler");
        }

        private static bool Resolve()
        {
            WindowType = ResolveWindowType();
            if (WindowType == null)
            {
                return false;
            }
            SessionType = Need("Verse.ProfilerUI.ProfilerSession");
            SectionType = Need("Verse.ProfilerUI.ProfilerRowSection");
            ViewType = Need("Verse.ProfilerUI.ProfilerRowListView");
            CallTreeType = Need("Verse.ProfilerUI.ProfilerCallTreeTable");
            AllocType = Need("Verse.ProfilerUI.ProfilerAllocTable");
            VramType = Need("Verse.ProfilerUI.ProfilerVramTable");
            SettingsType = Need("Verse.ProfilerUI.ProfilerSettingsDrawer");
            ThreadFilterType = Need("Verse.ProfilerUI.ProfilerThreadFilter");
            ProfilerGuiType = Need("Verse.ProfilerUI.ProfilerGui");
            Type rowType = Need("Verse.ProfilerUI.ProfilerRowData");
            Type columnType = Need("Verse.ProfilerUI.ProfilerColumn");
            Type profilerTreeType = Need("Verse.ProfilerUI.ProfilerTreeModel");
            Type frameStoreType = Need("Verse.ProfilerUI.ProfilerFrameStore");
            Type histogramType = Need("Verse.ProfilerUI.ProfilerHistogramWidget");
            Type flameType = Need("Verse.ProfilerUI.ProfilerFlamechartWidget");
            Type zoneSelectionType = Need("Verse.ProfilerUI.ProfilerZoneSelection");
            Type registryType = Need("Verse.ProfilerUI.ProfilerThreadRegistry");
            Type threadItemType = Need("Verse.ProfilerUI.ProfilerThreadFilter+Item");
            Type frameProfilerType = Need("Waylith.Profiling.FrameProfiler");
            Type snapshotType = Need("Waylith.Profiling.FrameProfiler+FrameSnapshot");
            Type threadSectionType = Need("Waylith.Profiling.FrameProfiler+ThreadSection");
            Type labelIdType = Need("Waylith.Profiling.ProfilerLabelId");
            zoneChunkType = Need("Waylith.Profiling.ZoneChunk");
            Type zoneNodeType = Need("Waylith.Profiling.ZoneNode");

            windowSession = Get<object>(WindowType, "session");
            windowHistogram = Get<object>(WindowType, "histogram");
            windowCallTree = Get<object>(WindowType, "callTree");
            windowAllocTable = Get<object>(WindowType, "allocTable");
            windowVramTable = Get<object>(WindowType, "vramTable");
            windowActiveSection = Get<object>(WindowType, "activeSection");
            windowFlamechart = Get<object>(WindowType, "flamechart");
            windowThreadFilter = Get<object>(WindowType, "threadFilter");
            windowSettings = Get<object>(WindowType, "settings");
            windowStatusText = Get<string>(WindowType, "statusText");
            windowFrameStepper = Get<string>(WindowType, "frameStepperText");
            windowSearchQuery = Get<string>(WindowType, "searchQuery");
            windowCopyView = NeedMethod(WindowType, "CopyView", Type.EmptyTypes);

            sessionHasData = Get<bool>(SessionType, "HasData");
            sessionIsPaused = Get<bool>(SessionType, "IsPaused");
            sessionIsSpikes = Get<bool>(SessionType, "IsSpikes");
            sessionFollowingNewest = Get<bool>(SessionType, "FollowingNewest");
            sessionSnapshot = Get<object>(SessionType, "Snapshot");
            sessionSource = Get<object>(SessionType, "Source");
            sessionTree = Get<object>(SessionType, "Tree");
            sessionSearch = Get<object>(SessionType, "Search");
            sessionStep = NeedMethod(SessionType, "Step", new[] { typeof(int) });
            sessionJumpToNewest = NeedMethod(SessionType, "JumpToNewest", Type.EmptyTypes);
            sessionSetPaused = NeedMethod(SessionType, "SetPaused", new[] { typeof(bool) });
            sessionBeginSelection = NeedMethod(SessionType, "BeginSelection", new[] { typeof(int) });
            sessionExtendSelection = NeedMethod(SessionType, "ExtendSelection", new[] { typeof(int) });
            sessionClearSelection = NeedMethod(SessionType, "ClearSelection", Type.EmptyTypes);
            sessionSelectZoneTimeRange = NeedMethod(SessionType, "SelectZoneTimeRange", new[] { typeof(int), typeof(long), typeof(long) });
            sessionInvalidate = NeedMethod(SessionType, "Invalidate", Type.EmptyTypes);
            sessionTryGetSnapshotAt = NeedMethod(SessionType, "TryGetSnapshotAt", null);
            sessionTryGetSelectionBand = NeedMethod(SessionType, "TryGetSelectionBand", null);
            frameStoreCountOf = NeedMethod(frameStoreType, "CountOf", null);

            sectionView = Get<object>(SectionType, "View");
            sectionCollapseSelected = NeedMethod(SectionType, "CollapseSelected", Type.EmptyTypes);
            sectionOpenContextMenu = NeedMethod(SectionType, "OpenContextMenu", Type.EmptyTypes);
            viewRows = Get<IList>(ViewType, "Rows");
            viewSelectedIndex = Get<int>(ViewType, "SelectedIndex");
            viewColumns = Get<Array>(ViewType, "Columns");
            viewLabelHeader = Get<Delegate>(ViewType, "LabelHeader");
            viewLabelSort = Get<Delegate>(ViewType, "LabelSort");
            viewRootCrumb = Get<Delegate>(ViewType, "RootCrumb");
            viewDetailHeader = Get<string>(ViewType, "DetailHeader");
            viewSearchStatus = Get<string>(ViewType, "SearchStatus");
            viewCrumbs = Get<IList>(ViewType, "crumbs");
            viewSelect = NeedMethod(ViewType, "Select", new[] { typeof(int) });
            viewMoveSelection = NeedMethod(ViewType, "MoveSelection", new[] { typeof(int) });
            viewActivateSelected = NeedMethod(ViewType, "ActivateSelected", new[] { typeof(bool) });
            viewCopySelectedRow = NeedMethod(ViewType, "CopySelectedRow", Type.EmptyTypes);
            viewSelectNextMatch = NeedMethod(ViewType, "SelectNextMatch", new[] { typeof(int) });
            callTreeFlamegraph = Get<bool>(CallTreeType, "FlamegraphMode");
            allocFooter = Get<string>(AllocType, "footer");
            vramFooter = Get<string>(VramType, "footer");

            rowLabel = Get<string>(rowType, "Label");
            rowDetail = Get<string>(rowType, "Detail");
            rowPercentText = Get<string>(rowType, "PercentText");
            rowTotalText = Get<string>(rowType, "TotalText");
            rowSelfText = Get<string>(rowType, "SelfText");
            rowDeltaText = Get<string>(rowType, "DeltaText");
            rowCallsText = Get<string>(rowType, "CallsText");
            rowAllocText = Get<string>(rowType, "AllocText");
            rowDepth = Get<int>(rowType, "Depth");
            rowHasChildren = Get<bool>(rowType, "HasChildren");
            rowExpanded = Get<bool>(rowType, "Expanded");
            rowDimmed = Get<bool>(rowType, "Dimmed");
            rowEnsureDisplayText = NeedMethod(rowType, "EnsureDisplayText", Type.EmptyTypes);

            columnKind = Get<object>(columnType, "kind");
            columnHeader = Get<Delegate>(columnType, "header");
            columnSort = Get<Delegate>(columnType, "sort");

            treeActiveSortColumn = Get<object>(profilerTreeType, "ActiveSortColumn");
            treeSortAscending = Get<bool>(profilerTreeType, "SortAscending");

            settingsIsOpen = Get<bool>(SettingsType, "IsOpen");
            settingsToggle = NeedMethod(SettingsType, "Toggle", Type.EmptyTypes);
            threadItems = Get<IList>(ThreadFilterType, "items");
            threadOpenField = AccessTools.Field(ThreadFilterType, "open");
            threadPopupRect = AccessTools.Field(ThreadFilterType, "popupRect");
            threadToggleSelectAll = NeedMethod(ThreadFilterType, "ToggleSelectAll", Type.EmptyTypes);
            threadButtonLabel = Get<string>(ThreadFilterType, "buttonLabel");
            threadButtonBusy = Get<string>(ThreadFilterType, "buttonBusyText");
            threadItemId = Get<int>(threadItemType, "threadId");
            threadItemLabel = Get<string>(threadItemType, "label");
            threadItemBusy = Get<string>(threadItemType, "busyText");
            threadItemUtil = Get<string>(threadItemType, "utilText");
            registryIsSelected = NeedMethod(registryType, "IsSelected", new[] { typeof(int) });
            registrySetSelected = NeedMethod(registryType, "SetSelected", new[] { typeof(int), typeof(bool) });
            registryAllSelected = AccessTools.Property(registryType, "AllSelected");
            registrySelectOnly = NeedMethod(registryType, "SelectOnly", new[] { typeof(int) });

            histogramPauseMarks = Get<object>(histogramType, "pauseMarks");
            Type barRingType = Need("Verse.ProfilerUI.ProfilerBarRing");
            barRingCount = Get<int>(barRingType, "Count");
            barRingItem = AccessTools.Property(barRingType, "Item");

            flameZoneSelected = AccessTools.Field(flameType, "ZoneSelected");
            flameHighlightZone = NeedMethod(flameType, "HighlightZone", new[] { typeof(int), typeof(int) });
            flameZoneSelfUs = NeedMethod(flameType, "ZoneSelfUs", new[] { zoneChunkType, typeof(int) });
            zoneSelectionCtor = AccessTools.Constructor(zoneSelectionType, new[] { typeof(int), typeof(int), typeof(int) });

            appendMs = NeedMethod(profilerTreeType, "AppendMs", new[] { typeof(StringBuilder), typeof(float), typeof(string), typeof(string) });
            appendAllocBytes = NeedMethod(profilerTreeType, "AppendAllocBytes", new[] { typeof(StringBuilder), typeof(long), typeof(string) });
            appendFrameMs = NeedMethod(profilerTreeType, "AppendFrameMs", new[] { typeof(StringBuilder), typeof(float), typeof(string) });

            collectChunks = NeedMethod(frameProfilerType, "CollectChunks",
                new[] { snapshotType, typeof(int), typeof(long), typeof(long), typeof(List<>).MakeGenericType(zoneChunkType) });
            getLabel = NeedMethod(frameProfilerType, "GetLabel", new[] { labelIdType });
            pathLabel = NeedMethod(frameProfilerType, "PathLabel", new[] { typeof(int) });

            snapshotFrameIndex = Get<int>(snapshotType, "frameIndex");
            snapshotOrdinal = Get<int>(snapshotType, "captureOrdinal");
            snapshotSectionCount = Get<int>(snapshotType, "sectionCount");
            snapshotGen0 = Get<int>(snapshotType, "gen0Collections");
            snapshotGen1 = Get<int>(snapshotType, "gen1Collections");
            snapshotGen2 = Get<int>(snapshotType, "gen2Collections");
            snapshotPauseCount = Get<int>(snapshotType, "pauseCount");
            snapshotAllocObjects = Get<int>(snapshotType, "allocatedObjectCount");
            snapshotTickDelta = Get<int>(snapshotType, "tickDelta");
            snapshotTotalMs = Get<float>(snapshotType, "totalMs");
            snapshotPauseMs = Get<float>(snapshotType, "pauseMs");
            snapshotPromoted = Get<long>(snapshotType, "promotedBytes");
            snapshotAllocBytes = Get<long>(snapshotType, "DisplayAllocBytes");
            snapshotBegin = Get<long>(snapshotType, "beginTimeNanoseconds");
            snapshotEnd = Get<long>(snapshotType, "endTimeNanoseconds");
            snapshotIsSpike = Get<bool>(snapshotType, "isSpike");
            snapshotSections = Get<Array>(snapshotType, "sections");
            sectionThreadId = Get<int>(threadSectionType, "threadId");
            sectionNameLabel = Get<object>(threadSectionType, "nameLabelId");
            sectionStart = Get<long>(threadSectionType, "startNanoseconds");
            sectionEnd = Get<long>(threadSectionType, "endNanoseconds");
            sectionBusy = Get<long>(threadSectionType, "busyNanoseconds");
            chunkBase = Get<long>(zoneChunkType, "baseNs");
            chunkAllocStats = Get<Array>(zoneChunkType, "allocStats");
            Type allocStatType = Need("Waylith.Profiling.FrameProfiler+NodeAllocStat");
            allocTotal = Get<int>(allocStatType, "totalBytes");
            allocSelf = Get<int>(allocStatType, "selfBytes");
            allocCount = Get<int>(allocStatType, "count");
            Type pathEntryType = Need("Waylith.Profiling.FrameProfiler+PathEntry");
            snapshotPathEntries = Get<Array>(snapshotType, "pathEntries");
            sectionFirstPath = Get<int>(threadSectionType, "firstPathEntry");
            pathSelfUs = Get<float>(pathEntryType, "selfMicroseconds");
            pathAllocBytes = Get<int>(pathEntryType, "allocBytes");
            sessionBudgetMs = Get<float>(SessionType, "BudgetMs");
            Type pauseType = Need("Waylith.Profiling.FrameProfiler+PauseSpan");
            snapshotPauses = Get<Array>(snapshotType, "pauses");
            pauseStart = Get<int>(pauseType, "startMicroseconds");
            pauseSuspended = Get<int>(pauseType, "suspendedMicroseconds");
            pauseEnd = Get<int>(pauseType, "endMicroseconds");
            pauseGeneration = Get<int>(pauseType, "condemnedGeneration");
            pauseThread = Get<int>(pauseType, "triggeringThreadId");
            pauseReason = Get<object>(pauseType, "reason");
            pausePromoted = Get<long>(pauseType, "promotedBytes");
            pauseCollected = Get<long>(pauseType, "collectedBytes");
            describePause = NeedMethod(frameProfilerType, "DescribePause", new[] { Need("Waylith.Profiling.RuntimePauseReason"), typeof(int) });
            chunkNodes = Get<Array>(zoneChunkType, "nodes");
            chunkCount = Get<int>(zoneChunkType, "Count");
            nodePathId = Get<int>(zoneNodeType, "pathId");
            nodeSubtreeEnd = Get<int>(zoneNodeType, "subtreeEnd");
            nodeStart = Get<long>(zoneNodeType, "StartNanoseconds");
            nodeEnd = Get<long>(zoneNodeType, "EndNanoseconds");
            nodeIsOpen = Get<bool>(zoneNodeType, "IsOpen");

            return barRingItem != null && threadOpenField != null && threadPopupRect != null && registryAllSelected != null && flameZoneSelected != null && zoneSelectionCtor != null;
        }

        private static Type Need(string name)
        {
            Type type = AccessTools.TypeByName(name);
            if (type == null)
            {
                throw new MissingMemberException(name);
            }
            return type;
        }

        private static MethodInfo NeedMethod(Type type, string name, Type[] parameters)
        {
            MethodInfo method = AccessTools.Method(type, name, parameters);
            if (method == null)
            {
                throw new MissingMemberException(type.FullName, name);
            }
            return method;
        }

        /// <summary>Compiled read of a field or property on a boxed instance, cast to <typeparamref name="T"/>.</summary>
        private static Func<object, T> Get<T>(Type type, string member)
        {
            ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
            Expression target = type.IsValueType ? (Expression)Expression.Unbox(instance, type) : Expression.Convert(instance, type);
            MemberInfo info = (MemberInfo)AccessTools.Field(type, member) ?? AccessTools.Property(type, member);
            if (info == null)
            {
                throw new MissingMemberException(type.FullName, member);
            }
            Expression read = Expression.MakeMemberAccess(target, info);
            return Expression.Lambda<Func<object, T>>(Expression.Convert(read, typeof(T)), instance).Compile();
        }

        private static object Call(MethodInfo method, object instance, params object[] args)
        {
            return method.Invoke(instance, args);
        }

        // ---- Window ----

        internal static object Session(object window) => windowSession(window);
        internal static object ActiveSection(object window) => windowActiveSection(window);
        internal static object Flamechart(object window) => windowFlamechart(window);
        internal static object ThreadFilter(object window) => windowThreadFilter(window);
        internal static object Settings(object window) => windowSettings(window);
        internal static object Histogram(object window) => windowHistogram(window);
        internal static string StatusText(object window) => windowStatusText(window) ?? "";
        internal static string FrameStepperText(object window) => windowFrameStepper(window) ?? "";
        internal static string SearchQuery(object window) => windowSearchQuery(window) ?? "";
        internal static void CopyView(object window) => Call(windowCopyView, window);

        internal static bool ActiveIsCallTree(object window) => ReferenceEquals(windowActiveSection(window), windowCallTree(window));
        internal static bool ActiveIsAlloc(object window) => ReferenceEquals(windowActiveSection(window), windowAllocTable(window));
        internal static bool ActiveIsVram(object window) => ReferenceEquals(windowActiveSection(window), windowVramTable(window));
        internal static bool FlamegraphMode(object window) => callTreeFlamegraph(windowCallTree(window));

        /// <summary>The alloc or VRAM table's footer line, or empty when the active section has none.</summary>
        internal static string ActiveFooter(object window)
        {
            if (ActiveIsAlloc(window))
            {
                return allocFooter(windowAllocTable(window)) ?? "";
            }
            if (ActiveIsVram(window))
            {
                return vramFooter(windowVramTable(window)) ?? "";
            }
            return "";
        }

        // ---- Session ----

        internal static bool HasData(object session) => sessionHasData(session);
        internal static bool IsPaused(object session) => sessionIsPaused(session);
        internal static bool IsSpikes(object session) => sessionIsSpikes(session);
        internal static bool FollowingNewest(object session) => sessionFollowingNewest(session);
        internal static object Snapshot(object session) => sessionHasData(session) ? sessionSnapshot(session) : null;
        internal static void Step(object session, int direction) => Call(sessionStep, session, direction);
        internal static void JumpToNewest(object session) => Call(sessionJumpToNewest, session);
        internal static void SetPaused(object session, bool paused) => Call(sessionSetPaused, session, paused);
        internal static void BeginSelection(object session, int newestOffset) => Call(sessionBeginSelection, session, newestOffset);
        internal static void ExtendSelection(object session, int newestOffset) => Call(sessionExtendSelection, session, newestOffset);
        internal static void ClearSelection(object session) => Call(sessionClearSelection, session);
        internal static void Invalidate(object session) => Call(sessionInvalidate, session);

        /// <summary>What a flamechart drag across one thread's lane does: that thread alone, over that span of the frame.</summary>
        internal static void SelectTimeRange(object session, int ordinal, int threadId, long startNs, long endNs)
        {
            registrySelectOnly.Invoke(null, new object[] { threadId });
            Call(sessionSelectZoneTimeRange, session, ordinal, startNs, endNs);
        }
        internal static bool SearchActive(object session) => SearchQueryOf(session).Length > 0;

        private static string SearchQueryOf(object session)
        {
            object search = sessionSearch(session);
            PropertyInfo query = AccessTools.Property(search.GetType(), "Query");
            return (query.GetValue(search) as string) ?? "";
        }

        /// <summary>Recorded frames in the active source (ring buffer or spikes); offset 0 is the newest.</summary>
        internal static int FrameCount(object session)
        {
            return (int)frameStoreCountOf.Invoke(null, new[] { sessionSource(session) });
        }

        internal static object SnapshotAt(object session, int newestOffset)
        {
            object[] args = { newestOffset, null };
            return (bool)sessionTryGetSnapshotAt.Invoke(session, args) ? args[1] : null;
        }

        /// <summary>The selected frame band as newest/oldest offsets; false while following the newest frame.</summary>
        internal static bool TryGetSelectionBand(object session, out int newestOffset, out int oldestOffset)
        {
            object[] args = { -1, -1 };
            bool ok = (bool)sessionTryGetSelectionBand.Invoke(session, args);
            newestOffset = (int)args[0];
            oldestOffset = (int)args[1];
            return ok;
        }

        // ---- Row sections ----

        internal static object View(object section) => sectionView(section);
        internal static void CollapseSelected(object section) => Call(sectionCollapseSelected, section);
        internal static void OpenContextMenu(object section) => Call(sectionOpenContextMenu, section);
        internal static IList Rows(object view) => viewRows(view);
        internal static int SelectedIndex(object view) => viewSelectedIndex(view);
        internal static void Select(object view, int index) => Call(viewSelect, view, index);
        internal static void MoveSelection(object view, int direction) => Call(viewMoveSelection, view, direction);
        internal static void ActivateSelected(object view, bool expandOnly) => Call(viewActivateSelected, view, expandOnly);
        internal static bool CopySelectedRow(object view) => (bool)Call(viewCopySelectedRow, view);
        internal static void SelectNextMatch(object view, int direction) => Call(viewSelectNextMatch, view, direction);
        internal static string SearchStatus(object view) => viewSearchStatus(view) ?? "";
        internal static string DetailHeader(object view) => viewDetailHeader(view);
        internal static string LabelHeader(object view) => (string)viewLabelHeader(view).DynamicInvoke();
        internal static Delegate LabelSort(object view) => viewLabelSort(view);

        /// <summary>The breadcrumb trail vanilla draws under the table: root crumb, then each ancestor of the selected row.</summary>
        internal static List<string> Crumbs(object view)
        {
            List<string> crumbs = new List<string> { (string)viewRootCrumb(view).DynamicInvoke() };
            IList entries = viewCrumbs(view);
            for (int i = 0; i < entries.Count; i++)
            {
                object tuple = entries[i];
                crumbs.Add((string)AccessTools.Field(tuple.GetType(), "Item1").GetValue(tuple));
            }
            return crumbs;
        }

        internal static int ColumnCount(object view) => viewColumns(view)?.Length ?? 0;
        internal static string ColumnKind(object view, int column) => columnKind(viewColumns(view).GetValue(column)).ToString();
        internal static string ColumnHeader(object view, int column) => (string)columnHeader(viewColumns(view).GetValue(column)).DynamicInvoke();
        internal static Delegate ColumnSort(object view, int column) => columnSort(viewColumns(view).GetValue(column));

        /// <summary>The sort the call tree applies: its active column name ("Total", "Label", ...) and direction.</summary>
        internal static string ActiveSortColumn(object session) => treeActiveSortColumn(sessionTree(session)).ToString();
        internal static bool SortAscending(object session) => treeSortAscending(sessionTree(session));

        // ---- Row data ----

        internal static string RowLabel(object row) => rowLabel(row) ?? "";
        internal static string RowDetail(object row) => rowDetail(row);
        internal static int RowDepth(object row) => rowDepth(row);
        internal static bool RowHasChildren(object row) => rowHasChildren(row);
        internal static bool RowExpanded(object row) => rowExpanded(row);
        internal static bool RowDimmed(object row) => rowDimmed(row);

        /// <summary>A cell's text exactly as the table draws it, by <c>ProfilerColumnKind</c> name.</summary>
        internal static string RowCell(object row, string kind)
        {
            rowEnsureDisplayText.Invoke(row, null);
            switch (kind)
            {
                case "Percent": return rowPercentText(row) ?? "";
                case "Total": return rowTotalText(row) ?? "";
                case "Self": return rowSelfText(row) ?? "";
                case "Delta": return rowDeltaText(row) ?? "";
                case "Calls": return rowCallsText(row) ?? "";
                default: return rowAllocText(row) ?? "";
            }
        }

        // ---- Settings and threads ----

        internal static bool SettingsOpen(object window) => settingsIsOpen(windowSettings(window));
        internal static void ToggleSettings(object window) => Call(settingsToggle, windowSettings(window));

        internal static IList ThreadItems(object window) => threadItems(windowThreadFilter(window));
        internal static int ThreadItemId(object item) => threadItemId(item);
        internal static string ThreadItemLabel(object item) => threadItemLabel(item) ?? "";
        internal static string ThreadItemBusy(object item) => threadItemBusy(item) ?? "";
        internal static string ThreadItemUtil(object item) => threadItemUtil(item) ?? "";
        internal static string ThreadButtonLabel(object window) => threadButtonLabel(windowThreadFilter(window)) ?? "";
        internal static string ThreadButtonBusy(object window) => threadButtonBusy(windowThreadFilter(window)) ?? "";
        internal static bool ThreadPopupOpen(object window) => (bool)threadOpenField.GetValue(windowThreadFilter(window));

        internal static void SetThreadPopupOpen(object window, bool open)
        {
            // MUTATION-C: mirrors ProfilerThreadFilter.DrawButton's `open = !open`; the button is a raw ProfilerGui.Pressed with no label to arm.
            threadOpenField.SetValue(windowThreadFilter(window), open);
        }

        /// <summary>
        /// A thread popup row in absolute UI points: row 0 is the select-all header, then one 24-point
        /// row per thread, laid out as DrawPopup lays them out inside the margin-less window.
        /// </summary>
        internal static UnityEngine.Rect ThreadRowScreenRect(Window window, int row)
        {
            UnityEngine.Rect popup = (UnityEngine.Rect)threadPopupRect.GetValue(windowThreadFilter(window));
            if (popup.width <= 0f)
            {
                return default(UnityEngine.Rect);
            }
            UnityEngine.Rect local = row == 0
                ? new UnityEngine.Rect(popup.x + 1f, popup.y + 1f, popup.width - 2f, 25f)
                : new UnityEngine.Rect(popup.x + 1f, popup.y + 26f + (row - 1) * 24f, popup.width - 2f, 24f);
            return new UnityEngine.Rect(window.windowRect.x + local.x, window.windowRect.y + local.y, local.width, local.height);
        }

        internal static void ToggleSelectAllThreads(object window) => Call(threadToggleSelectAll, windowThreadFilter(window));
        internal static bool AllThreadsSelected() => (bool)registryAllSelected.GetValue(null);
        internal static bool ThreadSelected(int threadId) => (bool)registryIsSelected.Invoke(null, new object[] { threadId });

        internal static void SetThreadSelected(int threadId, bool value)
        {
            registrySetSelected.Invoke(null, new object[] { threadId, value });
        }

        // ---- Histogram ----

        /// <summary>How long profiling was paused before the frame at <paramref name="newestOffset"/>, in ms; 0 when it was not.</summary>
        internal static float PauseBefore(object window, int newestOffset)
        {
            object marks = histogramPauseMarks(windowHistogram(window));
            return marks != null && newestOffset >= 0 && newestOffset < barRingCount(marks)
                ? (float)barRingItem.GetValue(marks, new object[] { newestOffset })
                : 0f;
        }

        // ---- Flamechart ----

        /// <summary>Raises the flamechart's own zone-click delegate, the path a mouse click on a zone takes.</summary>
        internal static void SelectZone(object window, int ordinal, int threadId, int pathId)
        {
            Delegate handler = flameZoneSelected.GetValue(windowFlamechart(window)) as Delegate;
            handler?.DynamicInvoke(zoneSelectionCtor.Invoke(new object[] { ordinal, threadId, pathId }));
        }

        internal static void HighlightZone(object window, int ordinal, int pathId)
        {
            Call(flameHighlightZone, windowFlamechart(window), ordinal, pathId);
        }

        // ---- Frame snapshots ----

        internal static int FrameIndex(object snapshot) => snapshotFrameIndex(snapshot);
        internal static int Ordinal(object snapshot) => snapshotOrdinal(snapshot);
        internal static float TotalMs(object snapshot) => snapshotTotalMs(snapshot);
        internal static float PauseMs(object snapshot) => snapshotPauseMs(snapshot);
        internal static int PauseCount(object snapshot) => snapshotPauseCount(snapshot);
        internal static int Gen0(object snapshot) => snapshotGen0(snapshot);
        internal static int Gen1(object snapshot) => snapshotGen1(snapshot);
        internal static int Gen2(object snapshot) => snapshotGen2(snapshot);
        internal static long PromotedBytes(object snapshot) => snapshotPromoted(snapshot);
        internal static long AllocBytes(object snapshot) => snapshotAllocBytes(snapshot);
        internal static int AllocObjects(object snapshot) => snapshotAllocObjects(snapshot);
        internal static int TickDelta(object snapshot) => snapshotTickDelta(snapshot);
        internal static bool IsSpike(object snapshot) => snapshotIsSpike(snapshot);
        internal static long BeginNs(object snapshot) => snapshotBegin(snapshot);
        internal static long EndNs(object snapshot) => snapshotEnd(snapshot);

        internal static int SectionCount(object snapshot) => snapshotSectionCount(snapshot);
        internal static object SectionAt(object snapshot, int index) => snapshotSections(snapshot).GetValue(index);
        internal static int SectionThreadId(object section) => sectionThreadId(section);
        internal static string SectionName(object section) => (string)getLabel.Invoke(null, new[] { sectionNameLabel(section) });
        internal static long SectionStartNs(object section) => sectionStart(section);
        internal static long SectionEndNs(object section) => sectionEnd(section);
        internal static long SectionBusyNs(object section) => sectionBusy(section);

        /// <summary>The zone chunks one thread recorded inside [startNs, endNs).</summary>
        internal static IList Chunks(object snapshot, int threadId, long startNs, long endNs)
        {
            IList chunks = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(zoneChunkType));
            collectChunks.Invoke(null, new object[] { snapshot, threadId, startNs, endNs, chunks });
            return chunks;
        }

        internal static long ChunkBaseNs(object chunk) => chunkBase(chunk);

        /// <summary>The zone's allocation stat, or false when the chunk tracked none for it.</summary>
        internal static bool TryZoneAlloc(object chunk, int index, out long bytes, out int count)
        {
            Array stats = chunkAllocStats(chunk);
            bytes = 0;
            count = 0;
            if (stats == null || index >= stats.Length)
            {
                return false;
            }
            object stat = stats.GetValue(index);
            bytes = Math.Max(allocTotal(stat), allocSelf(stat));
            count = allocCount(stat);
            return bytes > 0;
        }

        /// <summary>A thread section's own root path entry: its unprofiled self time (us) and allocations.</summary>
        internal static void SectionRootStats(object snapshot, object section, out float selfUs, out long allocBytes)
        {
            object entry = snapshotPathEntries(snapshot).GetValue(sectionFirstPath(section));
            selfUs = pathSelfUs(entry);
            allocBytes = pathAllocBytes(entry);
        }

        internal static float BudgetMs(object session) => sessionBudgetMs(session);

        /// <summary>One runtime pause (a garbage collection or other suspension) inside a frame.</summary>
        internal struct Pause
        {
            public string Name;
            public float StartUs;
            public float WaitingUs;
            public float StoppedUs;
            public long CollectedBytes;
            public long PromotedBytes;
            public int TriggeringThreadId;
        }

        internal static List<Pause> Pauses(object snapshot)
        {
            List<Pause> pauses = new List<Pause>();
            Array spans = snapshotPauses(snapshot);
            int count = Math.Min(PauseCount(snapshot), spans.Length);
            for (int i = 0; i < count; i++)
            {
                object span = spans.GetValue(i);
                int start = pauseStart(span), suspended = pauseSuspended(span), end = pauseEnd(span);
                pauses.Add(new Pause
                {
                    Name = (string)describePause.Invoke(null, new[] { pauseReason(span), (object)pauseGeneration(span) }),
                    StartUs = start,
                    WaitingUs = suspended - start,
                    StoppedUs = end - suspended,
                    CollectedBytes = pauseCollected(span),
                    PromotedBytes = pausePromoted(span),
                    TriggeringThreadId = pauseThread(span),
                });
            }
            return pauses;
        }
        internal static int ChunkCount(object chunk) => Math.Min(chunkCount(chunk), chunkNodes(chunk).Length);
        internal static object NodeAt(object chunk, int index) => chunkNodes(chunk).GetValue(index);
        internal static int NodePathId(object node) => nodePathId(node);
        internal static int NodeSubtreeEnd(object node) => nodeSubtreeEnd(node);
        internal static long NodeStartNs(object node) => nodeStart(node);
        internal static long NodeEndNs(object node) => nodeEnd(node);
        internal static bool NodeIsOpen(object node) => nodeIsOpen(node);
        internal static string PathName(int pathId) => (string)getLabel.Invoke(null, new[] { pathLabel.Invoke(null, new object[] { pathId }) });
        internal static float ZoneSelfUs(object chunk, int index) => (float)flameZoneSelfUs.Invoke(null, new[] { chunk, index });

        // ---- Formatting, through the profiler's own helpers ----

        internal static string FormatMs(float ms)
        {
            StringBuilder sb = new StringBuilder();
            appendMs.Invoke(null, new object[] { sb, ms, "0.000", " " });
            return sb.ToString();
        }

        internal static string FormatFrameMs(float ms)
        {
            StringBuilder sb = new StringBuilder();
            appendFrameMs.Invoke(null, new object[] { sb, ms, "0.0" });
            return sb.ToString();
        }

        internal static string FormatBytes(long bytes)
        {
            StringBuilder sb = new StringBuilder();
            appendAllocBytes.Invoke(null, new object[] { sb, bytes, " " });
            return sb.ToString();
        }
    }
}
