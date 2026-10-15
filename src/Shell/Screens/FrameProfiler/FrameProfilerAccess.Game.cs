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
        internal static Type ViewType { get; private set; }
        internal static Type CallTreeType { get; private set; }
        internal static Type SettingsType { get; private set; }
        internal static Type ThreadFilterType { get; private set; }
        internal static Type ProfilerGuiType { get; private set; }

        private static readonly LazyReflectionGate gate = new LazyReflectionGate("Frame profiler", Resolve);

        // Window_FrameProfiler.
        private static Func<object, object> windowSession, windowFlamechart, windowThreadFilter, windowSettings,
            windowActiveSection, windowCallTree, windowVramTable, windowHistogram;
        private static MethodInfo windowCopyView;

        // ProfilerSession.
        private static Func<object, bool> sessionHasData, sessionIsPaused, sessionIsSpikes, sessionFollowingNewest;
        private static Func<object, object> sessionSnapshot, sessionSource, sessionTree, sessionSearch;
        private static Func<object, string> searchQuery;
        private static MethodInfo sessionStep, sessionJumpToNewest, sessionSetPaused, sessionBeginSelection,
            sessionExtendSelection, sessionClearSelection, sessionSelectZoneTimeRange, sessionTryGetSnapshotAt, sessionTryGetSelectionBand, sessionInvalidate;
        private static Func<object, int> frameStoreCountOf;

        // Row sections and their list view.
        private static Func<object, object> sectionView;
        private static MethodInfo sectionCollapseSelected, sectionOpenContextMenu;
        private static Func<object, IList> viewRows;
        private static Func<object, int> viewSelectedIndex;
        private static Func<object, Array> viewColumns;
        private static Func<object, Func<string>> viewLabelHeader, viewRootCrumb;
        private static Func<object, Action> viewLabelSort;
        private static Func<object, List<ValueTuple<string, int>>> viewCrumbs;
        private static MethodInfo viewSelect, viewActivateSelected, viewCopySelectedRow, viewSelectNextMatch;
        private static Func<object, bool> callTreeFlamegraph;

        // ProfilerRowData.
        private static Func<object, string> rowLabel, rowDetail, rowPercentText, rowTotalText, rowSelfText,
            rowDeltaText, rowCallsText, rowAllocText;
        private static Func<object, int> rowDepth;
        private static Func<object, bool> rowHasChildren, rowExpanded, rowDimmed;
        private static Action<object> rowEnsureDisplayText;

        // ProfilerColumn.
        private static Func<object, object> columnKind;
        private static Func<object, Func<string>> columnHeader;
        private static Func<object, Action> columnSort;

        // Profiler tree model (sorting).
        private static Func<object, object> treeActiveSortColumn;
        private static Func<object, bool> treeSortAscending;

        // Settings drawer and thread filter.
        private static Func<object, bool> settingsIsOpen, threadOpen;
        private static MethodInfo settingsToggle;
        private static Func<object, IList> threadItems;
        private static FieldInfo threadOpenField;
        private static Func<object, UnityEngine.Rect> threadPopupRect;
        private static MethodInfo threadToggleSelectAll;
        private static Func<object, int> threadItemId;
        private static Func<object, string> threadItemLabel, threadItemBusy, threadItemUtil;
        private static Func<int, bool> registryIsSelected;
        private static Action<int, bool> registrySetSelected;
        private static Action<int> registrySelectOnly;
        private static Func<bool> registryAllSelected;

        // Histogram.
        private static Func<object, object> histogramPauseMarks;
        private static Func<object, int> barRingCount;
        private static PropertyInfo barRingItem;

        // Flamechart.
        private static FieldInfo flameZoneSelected;
        private static MethodInfo flameHighlightZone;
        private static Func<object, int, float> flameZoneSelfUs;
        private static ConstructorInfo zoneSelectionCtor;

        // Formatting helpers the window itself uses.
        private static Action<StringBuilder, float, string, string> appendMs;
        private static Action<StringBuilder, long, string> appendAllocBytes;
        private static readonly StringBuilder formatBuffer = new StringBuilder();

        // Waylith.Profiling.
        private static MethodInfo collectChunks;
        private static Type zoneChunkListType;
        private static Func<object, string> labelOf;
        private static Func<int, string> pathName;
        private static Func<object, int, string> describePause;
        private static Func<object, int> snapshotFrameIndex, snapshotOrdinal, snapshotSectionCount, snapshotGen0, snapshotGen1,
            snapshotGen2, snapshotPauseCount, snapshotAllocObjects;
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
        private static Func<object, Array> chunkNodes;
        private static Func<object, int> chunkCount;
        private static Func<object, int> nodePathId, nodeSubtreeEnd;
        private static Func<object, long> nodeStart, nodeEnd;
        private static Func<object, bool> nodeIsOpen;

        /// <summary>Resolves every member on first use; false on 1.6 or when 1.7 renamed something.</summary>
        internal static bool Ready => gate.Ensure();

        private static bool Resolve(ReflectionSurface s)
        {
            // Probed alone first so 1.6 pays for one failed type lookup, not thirty.
            Type windowType = AccessTools.TypeByName("Verse.ProfilerUI.Window_FrameProfiler");
            if (windowType == null)
            {
                return false;
            }
            WindowType = s.Supplied("Window_FrameProfiler", windowType);
            Type sessionType = s.Type("Verse.ProfilerUI.ProfilerSession");
            Type sectionType = s.Type("Verse.ProfilerUI.ProfilerRowSection");
            ViewType = s.Type("Verse.ProfilerUI.ProfilerRowListView");
            CallTreeType = s.Type("Verse.ProfilerUI.ProfilerCallTreeTable");
            SettingsType = s.Type("Verse.ProfilerUI.ProfilerSettingsDrawer");
            ThreadFilterType = s.Type("Verse.ProfilerUI.ProfilerThreadFilter");
            ProfilerGuiType = s.Type("Verse.ProfilerUI.ProfilerGui");
            Type rowType = s.Type("Verse.ProfilerUI.ProfilerRowData");
            Type columnType = s.Type("Verse.ProfilerUI.ProfilerColumn");
            Type profilerTreeType = s.Type("Verse.ProfilerUI.ProfilerTreeModel");
            Type searchType = s.Type("Verse.ProfilerUI.ProfilerSearchModel");
            Type frameStoreType = s.Type("Verse.ProfilerUI.ProfilerFrameStore");
            Type histogramType = s.Type("Verse.ProfilerUI.ProfilerHistogramWidget");
            Type barRingType = s.Type("Verse.ProfilerUI.ProfilerBarRing");
            Type flameType = s.Type("Verse.ProfilerUI.ProfilerFlamechartWidget");
            Type zoneSelectionType = s.Type("Verse.ProfilerUI.ProfilerZoneSelection");
            Type registryType = s.Type("Verse.ProfilerUI.ProfilerThreadRegistry");
            Type threadItemType = s.Type("Verse.ProfilerUI.ProfilerThreadFilter+Item");
            Type frameProfilerType = s.Type("Waylith.Profiling.FrameProfiler");
            Type snapshotType = s.Type("Waylith.Profiling.FrameProfiler+FrameSnapshot");
            Type threadSectionType = s.Type("Waylith.Profiling.FrameProfiler+ThreadSection");
            Type allocStatType = s.Type("Waylith.Profiling.FrameProfiler+NodeAllocStat");
            Type pathEntryType = s.Type("Waylith.Profiling.FrameProfiler+PathEntry");
            Type pauseType = s.Type("Waylith.Profiling.FrameProfiler+PauseSpan");
            Type pauseReasonType = s.Type("Waylith.Profiling.RuntimePauseReason");
            Type labelIdType = s.Type("Waylith.Profiling.ProfilerLabelId");
            Type zoneChunkType = s.Type("Waylith.Profiling.ZoneChunk");
            Type zoneNodeType = s.Type("Waylith.Profiling.ZoneNode");
            if (!s.Ready)
            {
                return false;
            }
            zoneChunkListType = typeof(List<>).MakeGenericType(zoneChunkType);

            windowSession = Get<object>(s, WindowType, "session");
            windowHistogram = Get<object>(s, WindowType, "histogram");
            windowCallTree = Get<object>(s, WindowType, "callTree");
            windowVramTable = Get<object>(s, WindowType, "vramTable");
            windowActiveSection = Get<object>(s, WindowType, "activeSection");
            windowFlamechart = Get<object>(s, WindowType, "flamechart");
            windowThreadFilter = Get<object>(s, WindowType, "threadFilter");
            windowSettings = Get<object>(s, WindowType, "settings");
            windowCopyView = s.Method(WindowType, "CopyView", Type.EmptyTypes);

            sessionHasData = Get<bool>(s, sessionType, "HasData");
            sessionIsPaused = Get<bool>(s, sessionType, "IsPaused");
            sessionIsSpikes = Get<bool>(s, sessionType, "IsSpikes");
            sessionFollowingNewest = Get<bool>(s, sessionType, "FollowingNewest");
            sessionSnapshot = Get<object>(s, sessionType, "Snapshot");
            sessionSource = Get<object>(s, sessionType, "Source");
            sessionTree = Get<object>(s, sessionType, "Tree");
            sessionSearch = Get<object>(s, sessionType, "Search");
            sessionBudgetMs = Get<float>(s, sessionType, "BudgetMs");
            searchQuery = Get<string>(s, searchType, "Query");
            sessionStep = s.Method(sessionType, "Step", new[] { typeof(int) });
            sessionJumpToNewest = s.Method(sessionType, "JumpToNewest", Type.EmptyTypes);
            sessionSetPaused = s.Method(sessionType, "SetPaused", new[] { typeof(bool) });
            sessionBeginSelection = s.Method(sessionType, "BeginSelection", new[] { typeof(int) });
            sessionExtendSelection = s.Method(sessionType, "ExtendSelection", new[] { typeof(int) });
            sessionClearSelection = s.Method(sessionType, "ClearSelection", Type.EmptyTypes);
            sessionSelectZoneTimeRange = s.Method(sessionType, "SelectZoneTimeRange", new[] { typeof(int), typeof(long), typeof(long) });
            sessionInvalidate = s.Method(sessionType, "Invalidate", Type.EmptyTypes);
            sessionTryGetSnapshotAt = s.Method(sessionType, "TryGetSnapshotAt");
            sessionTryGetSelectionBand = s.Method(sessionType, "TryGetSelectionBand");
            frameStoreCountOf = Static<int>(s.Method(frameStoreType, "CountOf"));

            sectionView = Get<object>(s, sectionType, "View");
            sectionCollapseSelected = s.Method(sectionType, "CollapseSelected", Type.EmptyTypes);
            sectionOpenContextMenu = s.Method(sectionType, "OpenContextMenu", Type.EmptyTypes);
            viewRows = Get<IList>(s, ViewType, "Rows");
            viewSelectedIndex = Get<int>(s, ViewType, "SelectedIndex");
            viewColumns = Get<Array>(s, ViewType, "Columns");
            viewLabelHeader = Get<Func<string>>(s, ViewType, "LabelHeader");
            viewLabelSort = Get<Action>(s, ViewType, "LabelSort");
            viewRootCrumb = Get<Func<string>>(s, ViewType, "RootCrumb");
            viewCrumbs = Get<List<ValueTuple<string, int>>>(s, ViewType, "crumbs");
            viewSelect = s.Method(ViewType, "Select", new[] { typeof(int) });
            viewActivateSelected = s.Method(ViewType, "ActivateSelected", new[] { typeof(bool) });
            viewCopySelectedRow = s.Method(ViewType, "CopySelectedRow", Type.EmptyTypes);
            viewSelectNextMatch = s.Method(ViewType, "SelectNextMatch", new[] { typeof(int) });
            callTreeFlamegraph = Get<bool>(s, CallTreeType, "FlamegraphMode");

            rowLabel = Get<string>(s, rowType, "Label");
            rowDetail = Get<string>(s, rowType, "Detail");
            rowPercentText = Get<string>(s, rowType, "PercentText");
            rowTotalText = Get<string>(s, rowType, "TotalText");
            rowSelfText = Get<string>(s, rowType, "SelfText");
            rowDeltaText = Get<string>(s, rowType, "DeltaText");
            rowCallsText = Get<string>(s, rowType, "CallsText");
            rowAllocText = Get<string>(s, rowType, "AllocText");
            rowDepth = Get<int>(s, rowType, "Depth");
            rowHasChildren = Get<bool>(s, rowType, "HasChildren");
            rowExpanded = Get<bool>(s, rowType, "Expanded");
            rowDimmed = Get<bool>(s, rowType, "Dimmed");
            rowEnsureDisplayText = Instance(rowType, s.Method(rowType, "EnsureDisplayText", Type.EmptyTypes));

            columnKind = Get<object>(s, columnType, "kind");
            columnHeader = Get<Func<string>>(s, columnType, "header");
            columnSort = Get<Action>(s, columnType, "sort");

            treeActiveSortColumn = Get<object>(s, profilerTreeType, "ActiveSortColumn");
            treeSortAscending = Get<bool>(s, profilerTreeType, "SortAscending");

            settingsIsOpen = Get<bool>(s, SettingsType, "IsOpen");
            settingsToggle = s.Method(SettingsType, "Toggle", Type.EmptyTypes);
            threadItems = Get<IList>(s, ThreadFilterType, "items");
            threadOpenField = s.Field(ThreadFilterType, "open");
            threadOpen = Get<bool>(s, ThreadFilterType, "open");
            threadPopupRect = Get<UnityEngine.Rect>(s, ThreadFilterType, "popupRect");
            threadToggleSelectAll = s.Method(ThreadFilterType, "ToggleSelectAll", Type.EmptyTypes);
            threadItemId = Get<int>(s, threadItemType, "threadId");
            threadItemLabel = Get<string>(s, threadItemType, "label");
            threadItemBusy = Get<string>(s, threadItemType, "busyText");
            threadItemUtil = Get<string>(s, threadItemType, "utilText");
            registryIsSelected = Typed<Func<int, bool>>(s.Method(registryType, "IsSelected", new[] { typeof(int) }));
            registrySetSelected = Typed<Action<int, bool>>(s.Method(registryType, "SetSelected", new[] { typeof(int), typeof(bool) }));
            registrySelectOnly = Typed<Action<int>>(s.Method(registryType, "SelectOnly", new[] { typeof(int) }));
            registryAllSelected = Typed<Func<bool>>(s.Property(registryType, "AllSelected")?.GetGetMethod(true));

            histogramPauseMarks = Get<object>(s, histogramType, "pauseMarks");
            barRingCount = Get<int>(s, barRingType, "Count");
            barRingItem = s.Property(barRingType, "Item");

            flameZoneSelected = s.Field(flameType, "ZoneSelected");
            flameHighlightZone = s.Method(flameType, "HighlightZone", new[] { typeof(int), typeof(int) });
            flameZoneSelfUs = Static<int, float>(s.Method(flameType, "ZoneSelfUs", new[] { zoneChunkType, typeof(int) }));
            zoneSelectionCtor = s.Constructor(zoneSelectionType, new[] { typeof(int), typeof(int), typeof(int) });

            appendMs = Typed<Action<StringBuilder, float, string, string>>(
                s.Method(profilerTreeType, "AppendMs", new[] { typeof(StringBuilder), typeof(float), typeof(string), typeof(string) }));
            appendAllocBytes = Typed<Action<StringBuilder, long, string>>(
                s.Method(profilerTreeType, "AppendAllocBytes", new[] { typeof(StringBuilder), typeof(long), typeof(string) }));

            collectChunks = s.Method(frameProfilerType, "CollectChunks",
                new[] { snapshotType, typeof(int), typeof(long), typeof(long), zoneChunkListType });
            MethodInfo getLabel = s.Method(frameProfilerType, "GetLabel", new[] { labelIdType });
            MethodInfo pathLabel = s.Method(frameProfilerType, "PathLabel", new[] { typeof(int) });
            labelOf = Static<string>(getLabel);
            if (getLabel != null && pathLabel != null)
            {
                ParameterExpression pathId = Expression.Parameter(typeof(int), "pathId");
                pathName = Expression.Lambda<Func<int, string>>(Expression.Call(getLabel, Expression.Call(pathLabel, pathId)), pathId).Compile();
            }
            describePause = Static<int, string>(s.Method(frameProfilerType, "DescribePause", new[] { pauseReasonType, typeof(int) }));

            snapshotFrameIndex = Get<int>(s, snapshotType, "frameIndex");
            snapshotOrdinal = Get<int>(s, snapshotType, "captureOrdinal");
            snapshotSectionCount = Get<int>(s, snapshotType, "sectionCount");
            snapshotGen0 = Get<int>(s, snapshotType, "gen0Collections");
            snapshotGen1 = Get<int>(s, snapshotType, "gen1Collections");
            snapshotGen2 = Get<int>(s, snapshotType, "gen2Collections");
            snapshotPauseCount = Get<int>(s, snapshotType, "pauseCount");
            snapshotAllocObjects = Get<int>(s, snapshotType, "allocatedObjectCount");
            snapshotTotalMs = Get<float>(s, snapshotType, "totalMs");
            snapshotPauseMs = Get<float>(s, snapshotType, "pauseMs");
            snapshotPromoted = Get<long>(s, snapshotType, "promotedBytes");
            snapshotAllocBytes = Get<long>(s, snapshotType, "DisplayAllocBytes");
            snapshotBegin = Get<long>(s, snapshotType, "beginTimeNanoseconds");
            snapshotEnd = Get<long>(s, snapshotType, "endTimeNanoseconds");
            snapshotIsSpike = Get<bool>(s, snapshotType, "isSpike");
            snapshotSections = Get<Array>(s, snapshotType, "sections");
            snapshotPathEntries = Get<Array>(s, snapshotType, "pathEntries");
            snapshotPauses = Get<Array>(s, snapshotType, "pauses");
            sectionThreadId = Get<int>(s, threadSectionType, "threadId");
            sectionNameLabel = Get<object>(s, threadSectionType, "nameLabelId");
            sectionStart = Get<long>(s, threadSectionType, "startNanoseconds");
            sectionEnd = Get<long>(s, threadSectionType, "endNanoseconds");
            sectionBusy = Get<long>(s, threadSectionType, "busyNanoseconds");
            sectionFirstPath = Get<int>(s, threadSectionType, "firstPathEntry");
            chunkBase = Get<long>(s, zoneChunkType, "baseNs");
            chunkAllocStats = Get<Array>(s, zoneChunkType, "allocStats");
            chunkNodes = Get<Array>(s, zoneChunkType, "nodes");
            chunkCount = Get<int>(s, zoneChunkType, "Count");
            allocTotal = Get<int>(s, allocStatType, "totalBytes");
            allocSelf = Get<int>(s, allocStatType, "selfBytes");
            allocCount = Get<int>(s, allocStatType, "count");
            pathSelfUs = Get<float>(s, pathEntryType, "selfMicroseconds");
            pathAllocBytes = Get<int>(s, pathEntryType, "allocBytes");
            pauseStart = Get<int>(s, pauseType, "startMicroseconds");
            pauseSuspended = Get<int>(s, pauseType, "suspendedMicroseconds");
            pauseEnd = Get<int>(s, pauseType, "endMicroseconds");
            pauseGeneration = Get<int>(s, pauseType, "condemnedGeneration");
            pauseThread = Get<int>(s, pauseType, "triggeringThreadId");
            pauseReason = Get<object>(s, pauseType, "reason");
            pausePromoted = Get<long>(s, pauseType, "promotedBytes");
            pauseCollected = Get<long>(s, pauseType, "collectedBytes");
            nodePathId = Get<int>(s, zoneNodeType, "pathId");
            nodeSubtreeEnd = Get<int>(s, zoneNodeType, "subtreeEnd");
            nodeStart = Get<long>(s, zoneNodeType, "StartNanoseconds");
            nodeEnd = Get<long>(s, zoneNodeType, "EndNanoseconds");
            nodeIsOpen = Get<bool>(s, zoneNodeType, "IsOpen");

            return s.Ready;
        }

        /// <summary>Compiled read of a field or property on a boxed instance, cast to <typeparamref name="T"/>.</summary>
        private static Func<object, T> Get<T>(ReflectionSurface s, Type type, string member)
        {
            MemberInfo info = s.FieldOrProperty(type, member);
            if (info == null)
            {
                return null;
            }
            ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
            Expression target = type.IsValueType ? (Expression)Expression.Unbox(instance, type) : Expression.Convert(instance, type);
            Expression read = Expression.MakeMemberAccess(target, info);
            return Expression.Lambda<Func<object, T>>(Expression.Convert(read, typeof(T)), instance).Compile();
        }

        private static T Typed<T>(MethodInfo method) where T : Delegate
        {
            return method == null ? null : AccessTools.MethodDelegate<T>(method);
        }

        private static Action<object> Instance(Type type, MethodInfo method)
        {
            if (method == null)
            {
                return null;
            }
            ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
            return Expression.Lambda<Action<object>>(Expression.Call(Expression.Convert(instance, type), method), instance).Compile();
        }

        /// <summary>A static method whose first parameter is a 1.7-only type, taken boxed.</summary>
        private static Func<object, TResult> Static<TResult>(MethodInfo method)
        {
            if (method == null)
            {
                return null;
            }
            ParameterExpression arg = Expression.Parameter(typeof(object), "arg");
            Expression call = Expression.Call(method, Expression.Convert(arg, method.GetParameters()[0].ParameterType));
            return Expression.Lambda<Func<object, TResult>>(call, arg).Compile();
        }

        private static Func<object, TArg, TResult> Static<TArg, TResult>(MethodInfo method)
        {
            if (method == null)
            {
                return null;
            }
            ParameterExpression arg = Expression.Parameter(typeof(object), "arg");
            ParameterExpression second = Expression.Parameter(typeof(TArg), "second");
            Expression call = Expression.Call(method, Expression.Convert(arg, method.GetParameters()[0].ParameterType), second);
            return Expression.Lambda<Func<object, TArg, TResult>>(call, arg, second).Compile();
        }

        private static object Call(MethodInfo method, object instance, params object[] args)
        {
            return method.Invoke(instance, args);
        }

        // ---- Window ----

        internal static object Session(object window) => windowSession(window);
        internal static object ActiveSection(object window) => windowActiveSection(window);
        internal static void CopyView(object window) => Call(windowCopyView, window);

        internal static bool ActiveIsCallTree(object window) => ReferenceEquals(windowActiveSection(window), windowCallTree(window));
        internal static bool ActiveIsVram(object window) => ReferenceEquals(windowActiveSection(window), windowVramTable(window));
        internal static bool FlamegraphMode(object window) => callTreeFlamegraph(windowCallTree(window));

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
        internal static bool SearchActive(object session) => !string.IsNullOrEmpty(searchQuery(sessionSearch(session)));
        internal static float BudgetMs(object session) => sessionBudgetMs(session);

        /// <summary>What a flamechart drag across one thread's lane does: that thread alone, over that span of the frame.</summary>
        internal static void SelectTimeRange(object session, int ordinal, int threadId, long startNs, long endNs)
        {
            registrySelectOnly(threadId);
            Call(sessionSelectZoneTimeRange, session, ordinal, startNs, endNs);
        }

        /// <summary>Recorded frames in the active source (ring buffer or spikes); offset 0 is the newest.</summary>
        internal static int FrameCount(object session) => frameStoreCountOf(sessionSource(session));

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
        internal static void ActivateSelected(object view, bool expandOnly) => Call(viewActivateSelected, view, expandOnly);
        internal static bool CopySelectedRow(object view) => (bool)Call(viewCopySelectedRow, view);
        internal static void SelectNextMatch(object view, int direction) => Call(viewSelectNextMatch, view, direction);
        internal static string LabelHeader(object view) => viewLabelHeader(view)();
        internal static Action LabelSort(object view) => viewLabelSort(view);

        /// <summary>The breadcrumb trail vanilla draws under the table: root crumb, then each ancestor of the selected row.</summary>
        internal static List<string> Crumbs(object view)
        {
            List<string> crumbs = new List<string> { viewRootCrumb(view)() };
            foreach (ValueTuple<string, int> entry in viewCrumbs(view))
            {
                crumbs.Add(entry.Item1);
            }
            return crumbs;
        }

        internal static int ColumnCount(object view) => viewColumns(view)?.Length ?? 0;
        internal static string ColumnKind(object view, int column) => columnKind(viewColumns(view).GetValue(column)).ToString();
        internal static string ColumnHeader(object view, int column) => columnHeader(viewColumns(view).GetValue(column))();
        internal static Action ColumnSort(object view, int column) => columnSort(viewColumns(view).GetValue(column));

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
            rowEnsureDisplayText(row);
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
        internal static bool ThreadPopupOpen(object window) => threadOpen(windowThreadFilter(window));

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
            UnityEngine.Rect popup = threadPopupRect(windowThreadFilter(window));
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
        internal static bool AllThreadsSelected() => registryAllSelected();
        internal static bool ThreadSelected(int threadId) => registryIsSelected(threadId);
        internal static void SetThreadSelected(int threadId, bool value) => registrySetSelected(threadId, value);

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
        internal static bool IsSpike(object snapshot) => snapshotIsSpike(snapshot);
        internal static long BeginNs(object snapshot) => snapshotBegin(snapshot);
        internal static long EndNs(object snapshot) => snapshotEnd(snapshot);

        internal static int SectionCount(object snapshot) => snapshotSectionCount(snapshot);
        internal static object SectionAt(object snapshot, int index) => snapshotSections(snapshot).GetValue(index);
        internal static int SectionThreadId(object section) => sectionThreadId(section);
        internal static string SectionName(object section) => labelOf(sectionNameLabel(section));
        internal static long SectionStartNs(object section) => sectionStart(section);
        internal static long SectionEndNs(object section) => sectionEnd(section);
        internal static long SectionBusyNs(object section) => sectionBusy(section);

        /// <summary>The zone chunks one thread recorded inside [startNs, endNs).</summary>
        internal static IList Chunks(object snapshot, int threadId, long startNs, long endNs)
        {
            IList chunks = (IList)Activator.CreateInstance(zoneChunkListType);
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
                    Name = describePause(pauseReason(span), pauseGeneration(span)),
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
        internal static string PathName(int pathId) => pathName(pathId);
        internal static float ZoneSelfUs(object chunk, int index) => flameZoneSelfUs(chunk, index);

        // ---- Formatting, through the profiler's own helpers ----

        internal static string FormatMs(float ms)
        {
            formatBuffer.Length = 0;
            appendMs(formatBuffer, ms, "0.000", " ");
            return formatBuffer.ToString();
        }

        internal static string FormatBytes(long bytes)
        {
            formatBuffer.Length = 0;
            appendAllocBytes(formatBuffer, bytes, " ");
            return formatBuffer.ToString();
        }
    }
}
