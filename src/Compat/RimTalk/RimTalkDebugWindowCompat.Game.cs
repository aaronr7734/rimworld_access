using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Reflection facade for <c>RimTalk.UI.DebugWindow</c> and its data types (<c>ApiLog</c>,
    /// <c>TalkRequest</c>, <c>PawnState</c>, <c>PromptMessageSegment</c>). Enum-valued members
    /// (ApiLog.State via GetState(), Channel, RequestStatus, Role, the DebugViewMode nested enum)
    /// are read back as plain <c>.ToString()</c> names throughout: this scope never WRITES one
    /// (every value-changing button lives behind the mod's own FloatMenu, reached only through the
    /// captured-extras region's real-widget click), so no enum <see cref="Type"/> ever needs
    /// resolving.
    ///
    /// List-typed fields whose element type is a RimTalk type (<c>List&lt;ApiLog&gt;</c>,
    /// <c>List&lt;PawnState&gt;</c>, <c>List&lt;TalkRequest&gt;</c>,
    /// <c>List&lt;PromptMessageSegment&gt;</c>, <c>Dictionary&lt;string,List&lt;ApiLog&gt;&gt;</c>)
    /// are read back through the non-generic <see cref="IList"/>/<see cref="IDictionary"/>
    /// interfaces every <c>List&lt;T&gt;</c>/<c>Dictionary&lt;TKey,TValue&gt;</c> already implements
    /// regardless of <c>T</c> -- no RimTalk assembly reference needed. <c>List&lt;string&gt;</c>
    /// (<c>_expandedPawns</c>) and <c>HashSet&lt;int&gt;</c> (<c>_expandedPromptSegmentIndices</c>)
    /// are pure BCL generic instantiations and are cast to their real concrete types directly.
    /// </summary>
    internal static class RimTalkDebugWindowCompat
    {
        private const string RimTalkPackageId = "cj.rimtalk";

        private static readonly Type dialogType;
        private static readonly FieldInfo viewModeField;
        private static readonly FieldInfo pawnFilterField;
        private static readonly FieldInfo textSearchField;
        private static readonly FieldInfo selectedLogField;
        private static readonly FieldInfo stickToBottomField;
        private static readonly FieldInfo requestsField;
        private static readonly FieldInfo pawnStatesField;
        private static readonly FieldInfo talkLogsByPawnField;
        private static readonly FieldInfo cachedActiveViewListField;
        private static readonly FieldInfo expandedPawnsField;
        private static readonly FieldInfo sortColumnField;
        private static readonly FieldInfo sortAscendingField;
        private static readonly FieldInfo tempPromptSegmentsField;
        private static readonly FieldInfo expandedPromptSegmentIndicesField;
        private static readonly FieldInfo aiStatusField;
        private static readonly FieldInfo totalCallsField;
        private static readonly FieldInfo totalTokensField;
        private static readonly FieldInfo avgCallsPerMinField;
        private static readonly FieldInfo avgTokensPerMinField;
        private static readonly FieldInfo avgTokensPerCallField;
        private static readonly MethodInfo getSortedPawnStatesMethod;
        private static readonly MethodInfo getLastResponseForPawnMethod;
        private static readonly MethodInfo resetMethod;
        private static readonly MethodInfo getRoleLabelMethod;
        private static readonly bool ready;

        // ApiLog
        private static readonly Type apiLogType;
        private static readonly PropertyInfo apiLogTimestampProp;
        private static readonly PropertyInfo apiLogNameProp;
        private static readonly PropertyInfo apiLogResponseProp;
        private static readonly FieldInfo apiLogInteractionTypeField;
        private static readonly FieldInfo apiLogElapsedMsField;
        private static readonly FieldInfo apiLogIsFirstDialogueField;
        private static readonly PropertyInfo apiLogPayloadProp;
        private static readonly MethodInfo apiLogGetStateMethod;
        private static readonly Type payloadType;
        private static readonly PropertyInfo payloadTokenCountProp;

        // TalkRequest
        private static readonly Type talkRequestType;
        private static readonly PropertyInfo trInitiatorProp;
        private static readonly PropertyInfo trRecipientProp;
        private static readonly PropertyInfo trPromptProp;
        private static readonly PropertyInfo trCreatedTimeProp;
        private static readonly PropertyInfo trCreatedTickProp;
        private static readonly PropertyInfo trFinishedTickProp;
        private static readonly PropertyInfo trStatusProp;
        private static readonly PropertyInfo trTalkTypeProp;

        // PawnState
        private static readonly Type pawnStateType;
        private static readonly FieldInfo psPawnField;
        private static readonly PropertyInfo psLastTalkTickProp;
        private static readonly PropertyInfo psTalkInitiationWeightProp;
        private static readonly MethodInfo psCanGenerateTalkMethod;

        // PromptMessageSegment
        private static readonly Type segmentType;
        private static readonly PropertyInfo segEntryNameProp;
        private static readonly PropertyInfo segRoleProp;
        private static readonly PropertyInfo segContentProp;

        // Settings/UIUtil
        private static readonly Type rimTalkSettingsType;
        private static readonly FieldInfo settingsIsEnabledField;
        private static readonly Type uiUtilType;

        public static bool Ready => ready;

        static RimTalkDebugWindowCompat()
        {
            var surface = new ReflectionSurface("RimTalk debug window compat");

            dialogType = surface.Type("RimTalk.UI.DebugWindow");
            apiLogType = surface.Type("RimTalk.Data.ApiLog");
            talkRequestType = surface.Type("RimTalk.Data.TalkRequest");
            pawnStateType = surface.Type("RimTalk.Data.PawnState");
            segmentType = surface.Type("RimTalk.Data.PromptMessageSegment");
            rimTalkSettingsType = surface.Type("RimTalk.RimTalkSettings");

            viewModeField = surface.Field(dialogType, "_viewMode");
            pawnFilterField = surface.Field(dialogType, "_pawnFilter");
            textSearchField = surface.Field(dialogType, "_textSearch");
            selectedLogField = surface.Field(dialogType, "_selectedLog");
            requestsField = surface.Field(dialogType, "_requests");
            pawnStatesField = surface.Field(dialogType, "_pawnStates");
            talkLogsByPawnField = surface.Field(dialogType, "_talkLogsByPawn");
            cachedActiveViewListField = surface.Field(dialogType, "_cachedActiveViewList");
            expandedPawnsField = surface.Field(dialogType, "_expandedPawns");
            sortColumnField = surface.Field(dialogType, "_sortColumn");
            sortAscendingField = surface.Field(dialogType, "_sortAscending");
            tempPromptSegmentsField = surface.Field(dialogType, "_tempPromptSegments");
            expandedPromptSegmentIndicesField = surface.Field(dialogType, "_expandedPromptSegmentIndices");
            getSortedPawnStatesMethod = surface.Method(dialogType, "GetSortedPawnStates");
            getLastResponseForPawnMethod = surface.Method(dialogType, "GetLastResponseForPawn", new[] { typeof(string) });
            resetMethod = surface.Method(dialogType, "Reset");

            apiLogTimestampProp = surface.Property(apiLogType, "Timestamp");
            apiLogNameProp = surface.Property(apiLogType, "Name");
            apiLogResponseProp = surface.Property(apiLogType, "Response");
            apiLogGetStateMethod = surface.Method(apiLogType, "GetState");

            trInitiatorProp = surface.Property(talkRequestType, "Initiator");
            trStatusProp = surface.Property(talkRequestType, "Status");

            psPawnField = surface.Field(pawnStateType, "Pawn");
            segContentProp = surface.Property(segmentType, "Content");

            settingsIsEnabledField = surface.Field(rimTalkSettingsType, "IsEnabled");

            ready = surface.Ready && RimTalkSettingsFacade.Ready;

            // OPTIONAL from here down: every read below is null-conditional, so a drift in any of
            // these degrades one column or one stats row rather than declining the whole window.
            payloadType = AccessTools.TypeByName("RimTalk.Client.Payload");
            uiUtilType = AccessTools.TypeByName("RimTalk.UI.UIUtil");
            payloadTokenCountProp = payloadType != null ? AccessTools.Property(payloadType, "TokenCount") : null;

            if (dialogType != null)
            {
                stickToBottomField = AccessTools.Field(dialogType, "_stickToBottom");
                aiStatusField = AccessTools.Field(dialogType, "_aiStatus");
                totalCallsField = AccessTools.Field(dialogType, "_totalCalls");
                totalTokensField = AccessTools.Field(dialogType, "_totalTokens");
                avgCallsPerMinField = AccessTools.Field(dialogType, "_avgCallsPerMin");
                avgTokensPerMinField = AccessTools.Field(dialogType, "_avgTokensPerMin");
                avgTokensPerCallField = AccessTools.Field(dialogType, "_avgTokensPerCall");
                getRoleLabelMethod = AccessTools.Method(dialogType, "GetRoleLabel");
            }
            if (apiLogType != null)
            {
                apiLogInteractionTypeField = AccessTools.Field(apiLogType, "InteractionType");
                apiLogElapsedMsField = AccessTools.Field(apiLogType, "ElapsedMs");
                apiLogIsFirstDialogueField = AccessTools.Field(apiLogType, "IsFirstDialogue");
                apiLogPayloadProp = AccessTools.Property(apiLogType, "Payload");
            }
            if (talkRequestType != null)
            {
                trRecipientProp = AccessTools.Property(talkRequestType, "Recipient");
                trPromptProp = AccessTools.Property(talkRequestType, "Prompt");
                trCreatedTimeProp = AccessTools.Property(talkRequestType, "CreatedTime");
                trCreatedTickProp = AccessTools.Property(talkRequestType, "CreatedTick");
                trFinishedTickProp = AccessTools.Property(talkRequestType, "FinishedTick");
                trTalkTypeProp = AccessTools.Property(talkRequestType, "TalkType");
            }
            if (pawnStateType != null)
            {
                psLastTalkTickProp = AccessTools.Property(pawnStateType, "LastTalkTick");
                psTalkInitiationWeightProp = AccessTools.Property(pawnStateType, "TalkInitiationWeight");
                psCanGenerateTalkMethod = AccessTools.Method(pawnStateType, "CanGenerateTalk");
            }
            if (segmentType != null)
            {
                segEntryNameProp = AccessTools.Property(segmentType, "EntryName");
                segRoleProp = AccessTools.Property(segmentType, "Role");
            }
        }

        public static void Register()
        {
            if (!ready || !ModsConfig.IsActive(RimTalkPackageId))
                return;

            ScopeForWindow.Register(dialogType, delegate (Window w)
            {
                return new RimTalkDebugScope(w);
            });

            // The CTRL variant of RimTalk's own PlaySettings toggle icon (TogglePatch.Postfix's
            // ctrl branch opens a bare `new DebugWindow()`), registered generically so
            // DialogueLogScope's button row can offer it.
            ToolbarModifierRegistry.Register("rimtalk.toggle",
                "RimWorldAccess.Narrative.DialogueLog.OpenDebugWindow",
                ActivateOpenDebugWindow);

        }

        /// <summary>
        /// Vehicle A: the exact construction TogglePatch's ctrl-click branch uses
        /// (`new DebugWindow()`), reproduced by reflection since the type is never referenced at
        /// compile time. Mirrors that branch's own not-already-open guard
        /// (`!Find.WindowStack.IsOpen&lt;DebugWindow&gt;()`) via the non-generic overload.
        /// </summary>
        internal static bool TryOpenDebugWindow()
        {
            if (!ready)
            {
                return false;
            }
            try
            {
                if (Find.WindowStack.IsOpen(dialogType))
                {
                    return true;
                }
                object instance = Activator.CreateInstance(dialogType);
                Find.WindowStack.Add((Window)instance);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk debug window compat", ex);
                return false;
            }
        }

        /// <summary>ToolbarModifierRegistry entry point for the "rimtalk.toggle" ctrl variant -- same failure announcement DialogueLogScope's other toolbar actions use.</summary>
        private static void ActivateOpenDebugWindow()
        {
            if (!TryOpenDebugWindow())
            {
                TolkHelper.SpeakData((string)"RimWorldAccess.Narrative.DialogueLog.ActionFailed".Translate());
            }
        }

        // The MemoryLog view, on its own gate so a RimTalk build without it keeps the other three.

        private static readonly LazyReflectionGate memoryLogGate =
            new LazyReflectionGate("RimTalk debug window compat (memory log)", ResolveMemoryLog);

        private static FieldInfo cachedMemoryLogsField;
        private static FieldInfo selectedMemoryLogField;
        private static PropertyInfo mlTimestampProp;
        private static PropertyInfo mlTickProp;
        private static PropertyInfo mlSourceNameProp;
        private static PropertyInfo mlSourceIdProp;
        private static PropertyInfo mlTargetNameProp;
        private static PropertyInfo mlTargetIdProp;
        private static PropertyInfo mlChangeTypeProp;
        private static PropertyInfo mlEventKeyProp;
        private static PropertyInfo mlNoteProp;
        private static PropertyInfo mlOldWeightProp;
        private static PropertyInfo mlNewWeightProp;
        private static PropertyInfo mlIsDirectiveProp;
        private static PropertyInfo mlIsCoreTraumaProp;
        private static PropertyInfo mlDetailsProp;
        private static MethodInfo stripBoilerplateMethod;
        private static MethodInfo resolvePawnMethod;

        private static bool ResolveMemoryLog(ReflectionSurface surface)
        {
            Type window = surface.Supplied("RimTalk.UI.DebugWindow", dialogType);
            Type entry = surface.Type("RimTalk.PawnMemory.MemoryLogEntry");
            Type tracker = surface.Type("RimTalk.PawnMemory.PawnMemoryTracker");
            FieldInfo cached = surface.Field(window, "_cachedMemoryLogs");
            FieldInfo selected = surface.Field(window, "_selectedMemoryLog");
            PropertyInfo timestamp = surface.Property(entry, "Timestamp");
            PropertyInfo tick = surface.Property(entry, "Tick");
            PropertyInfo sourceName = surface.Property(entry, "SourcePawnName");
            PropertyInfo sourceId = surface.Property(entry, "SourcePawnId");
            PropertyInfo targetName = surface.Property(entry, "TargetPawnName");
            PropertyInfo targetId = surface.Property(entry, "TargetPawnId");
            PropertyInfo changeType = surface.Property(entry, "ChangeType");
            PropertyInfo eventKey = surface.Property(entry, "EventKey");
            PropertyInfo note = surface.Property(entry, "Note");
            PropertyInfo oldWeight = surface.Property(entry, "OldWeight");
            PropertyInfo newWeight = surface.Property(entry, "NewWeight");
            PropertyInfo isDirective = surface.Property(entry, "IsDirective");
            PropertyInfo isCoreTrauma = surface.Property(entry, "IsCoreTrauma");
            PropertyInfo details = surface.Property(entry, "Details");
            MethodInfo strip = surface.Method(tracker, "StripBoilerplate", new[] { typeof(string) });
            MethodInfo resolvePawn = surface.Method(window, "ResolvePawn", new[] { typeof(string), typeof(int) });
            if (!surface.Ready)
            {
                return false;
            }
            cachedMemoryLogsField = cached;
            selectedMemoryLogField = selected;
            mlTimestampProp = timestamp;
            mlTickProp = tick;
            mlSourceNameProp = sourceName;
            mlSourceIdProp = sourceId;
            mlTargetNameProp = targetName;
            mlTargetIdProp = targetId;
            mlChangeTypeProp = changeType;
            mlEventKeyProp = eventKey;
            mlNoteProp = note;
            mlOldWeightProp = oldWeight;
            mlNewWeightProp = newWeight;
            mlIsDirectiveProp = isDirective;
            mlIsCoreTraumaProp = isCoreTrauma;
            mlDetailsProp = details;
            stripBoilerplateMethod = strip;
            resolvePawnMethod = resolvePawn;
            return true;
        }

        internal static bool MemoryLogReady => ready && memoryLogGate.Ensure();

        internal static IList GetMemoryLogs(Window w) => MemoryLogReady ? cachedMemoryLogsField.GetValue(w) as IList : null;
        internal static object GetSelectedMemoryLog(Window w) => MemoryLogReady ? selectedMemoryLogField.GetValue(w) : null;

        /// <summary>MUTATION-C: mirrors DrawMemoryLogRow's row-click assignment (`_selectedMemoryLog = entry`), a bare private field behind a ButtonInvisible the virtualized table draws only for rows in view.</summary>
        internal static void SelectMemoryLog(Window w, object entry)
        {
            selectedMemoryLogField.SetValue(w, entry);  // MUTATION-C: see summary.
        }

        internal static DateTime MemoryLogTimestamp(object e) => (DateTime)mlTimestampProp.GetValue(e);
        internal static int MemoryLogTick(object e) => (int)mlTickProp.GetValue(e);
        internal static string MemoryLogEventKey(object e) => mlEventKeyProp.GetValue(e) as string ?? "";
        internal static bool MemoryLogIsDirective(object e) => (bool)mlIsDirectiveProp.GetValue(e);
        internal static bool MemoryLogIsCoreTrauma(object e) => (bool)mlIsCoreTraumaProp.GetValue(e);
        internal static string MemoryLogDetails(object e) => mlDetailsProp.GetValue(e) as string ?? "";

        internal static string MemoryLogPawnName(object e, bool target)
        {
            string name = (target ? mlTargetNameProp : mlSourceNameProp).GetValue(e) as string;
            return string.IsNullOrEmpty(name) ? "-" : name;
        }

        internal static Pawn MemoryLogPawn(object e, bool target)
        {
            int id = (int)(target ? mlTargetIdProp : mlSourceIdProp).GetValue(e);
            return resolvePawnMethod.Invoke(null, new object[] { MemoryLogPawnName(e, target), id }) as Pawn;
        }

        internal static string MemoryLogChangeTypeLabel(object e)
        {
            return Translator.Translate("RimTalk.DebugWindow.MemoryType" + mlChangeTypeProp.GetValue(e)).Resolve();
        }

        internal static string MemoryLogWeight(object e, string format)
        {
            string newWeight = ((float)mlNewWeightProp.GetValue(e)).ToString(format);
            return mlChangeTypeProp.GetValue(e).ToString() == "Updated"
                ? "RimWorldAccess.Compat.RimTalk.Debug.MemoryWeightChange".Translate(((float)mlOldWeightProp.GetValue(e)).ToString(format), newWeight).Resolve()
                : newWeight;
        }

        internal static string MemoryLogNote(object e)
        {
            return stripBoilerplateMethod.Invoke(null, new[] { mlNoteProp.GetValue(e) as string }) as string ?? "";
        }

        /// <summary>DrawMemoryLogRow's note cell, built as it builds it.</summary>
        internal static string MemoryLogSummary(object e)
        {
            string note = MemoryLogNote(e);
            string key = MemoryLogEventKey(e);
            if (!string.IsNullOrEmpty(note))
            {
                return "[" + key + "] " + note;
            }
            return string.IsNullOrEmpty(key) ? MemoryLogDetails(e) : key;
        }

        // DebugWindow field access.

        internal static string GetViewMode(Window w) => viewModeField?.GetValue(w)?.ToString();
        internal static string GetPawnFilter(Window w) => pawnFilterField?.GetValue(w) as string ?? "";
        // MUTATION-C: mirrors DrawSearchField's own return-assign (`_pawnFilter = DrawSearchField(...)`); a bare private string field with no gated setter.
        internal static void SetPawnFilter(Window w, string value) => pawnFilterField?.SetValue(w, value);
        internal static string GetTextSearch(Window w) => textSearchField?.GetValue(w) as string ?? "";
        // MUTATION-C: mirrors DrawSearchField's own return-assign for _textSearch, same as SetPawnFilter above.
        internal static void SetTextSearch(Window w, string value) => textSearchField?.SetValue(w, value);
        internal static object GetSelectedLog(Window w) => selectedLogField?.GetValue(w);

        /// <summary>MUTATION-C: mirrors DrawRequestRow's own row-click field assignment exactly (see RimTalkDebugScope.SelectLog).</summary>
        internal static void SelectLog(Window w, object apiLog)
        {
            selectedLogField?.SetValue(w, apiLog); // MUTATION-C: see method summary above.
            stickToBottomField?.SetValue(w, false); // MUTATION-C: see method summary above.
        }

        internal static IList GetRequests(Window w) => requestsField?.GetValue(w) as IList;
        internal static IList GetActiveRequestsView(Window w) => cachedActiveViewListField?.GetValue(w) as IList;

        /// <summary>
        /// GetSortedPawnStates' own switch returns <c>_pawnStates</c> (a <c>List&lt;PawnState&gt;</c>,
        /// IList-castable) ONLY in its default/unsorted branch -- every sorted branch returns a LINQ
        /// <c>IOrderedEnumerable&lt;PawnState&gt;</c>, which does NOT implement <see cref="IList"/>,
        /// so the boxing fallback below is the common case whenever a sort is active, not a rare one.
        /// </summary>
        internal static IList GetSortedPawnStates(Window w)
        {
            try
            {
                object result = getSortedPawnStatesMethod?.Invoke(w, null);
                return result as IList ?? (result as IEnumerable)?.ToBoxedList();
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk debug window compat", ex);
                return null;
            }
        }

        internal static string GetLastResponseForPawn(Window w, string pawnLabel)
        {
            try
            {
                return getLastResponseForPawnMethod?.Invoke(w, new object[] { pawnLabel }) as string;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk debug window compat", ex);
                return null;
            }
        }

        internal static IList GetTalkLogsForPawn(Window w, string pawnLabel)
        {
            var dict = talkLogsByPawnField?.GetValue(w) as IDictionary;
            if (dict == null || !dict.Contains(pawnLabel))
                return null;
            return dict[pawnLabel] as IList;
        }

        internal static int PawnRequestCount(Window w, string pawnLabel)
        {
            IList logs = GetTalkLogsForPawn(w, pawnLabel);
            if (logs == null)
                return 0;
            int count = 0;
            for (int i = 0; i < logs.Count; i++)
            {
                if ((bool)(apiLogIsFirstDialogueField?.GetValue(logs[i]) ?? false))
                    count++;
            }
            return count;
        }

        internal static IList GetExpandedPawns(Window w) => expandedPawnsField?.GetValue(w) as List<string>;

        internal static void ToggleExpandedPawn(Window w, string pawnLabel)
        {
            List<string> expanded = expandedPawnsField?.GetValue(w) as List<string>;
            if (expanded == null)
                return;
            if (expanded.Contains(pawnLabel))
                expanded.Remove(pawnLabel);
            else
                expanded.Add(pawnLabel);
        }

        // MUTATION-C: mirrors DrawSortableHeader's own field assignment (_sortColumn/_sortAscending, both bare private fields with no gated setter) -- see RimTalkDebugScope.ApplyContentSort for the full sort-cycle reconciliation this drives.
        internal static void SetSortColumn(Window w, string value) => sortColumnField?.SetValue(w, value);
        internal static void SetSortAscending(Window w, bool value) => sortAscendingField?.SetValue(w, value);

        internal static IList GetPromptSegments(Window w, object apiLog)
        {
            // _tempPromptSegments only reflects the CURRENTLY selected log (DrawDetailsPanel's own
            // ResolvePromptSegments cache) -- reading it directly is correct since this facade is
            // only ever asked for the selected log's own segments.
            return tempPromptSegmentsField?.GetValue(w) as IList;
        }

        internal static object GetSegmentAt(Window w, object apiLog, int index)
        {
            IList segments = GetPromptSegments(w, apiLog);
            return segments != null && index >= 0 && index < segments.Count ? segments[index] : null;
        }

        internal static ICollection<int> GetExpandedSegmentIndices(Window w) => expandedPromptSegmentIndicesField?.GetValue(w) as HashSet<int>;

        internal static void ToggleExpandedSegment(Window w, int index)
        {
            HashSet<int> expanded = expandedPromptSegmentIndicesField?.GetValue(w) as HashSet<int>;
            if (expanded == null)
                return;
            if (expanded.Contains(index))
                expanded.Remove(index);
            else
                expanded.Add(index);
        }

        internal static string SegmentContent(Window w, object apiLog, int index)
        {
            object segment = GetSegmentAt(w, apiLog, index);
            return segment != null ? segContentProp?.GetValue(segment) as string : null;
        }

        internal static string SegmentEntryName(object segment)
        {
            string name = segEntryNameProp?.GetValue(segment) as string;
            return string.IsNullOrWhiteSpace(name) ? "Entry" : name;
        }

        internal static string SegmentPreview(object segment)
        {
            string content = segContentProp?.GetValue(segment) as string;
            if (string.IsNullOrWhiteSpace(content))
                return "(empty)";
            string firstLine = content.Replace("\r", "").Split('\n')[0].Trim();
            return firstLine.Length > 80 ? firstLine.Substring(0, 77) + "..." : firstLine;
        }

        /// <summary>Vehicle A: the window's own private static GetRoleLabel (AI -> "Assistant", everything else -> its plain enum name).</summary>
        internal static string SegmentRoleLabel(Window w, object segment)
        {
            object role = segRoleProp?.GetValue(segment);
            if (role == null)
                return "";
            try
            {
                return getRoleLabelMethod?.Invoke(null, new[] { role }) as string ?? role.ToString();
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk debug window compat", ex);
                return role.ToString();
            }
        }

        internal static string GetAiStatus(Window w) => aiStatusField?.GetValue(w) as string ?? "";
        internal static long GetTotalCalls(Window w) => Convert.ToInt64(totalCallsField?.GetValue(w) ?? 0L);
        internal static long GetTotalTokens(Window w) => Convert.ToInt64(totalTokensField?.GetValue(w) ?? 0L);
        internal static double GetAvgCallsPerMin(Window w) => Convert.ToDouble(avgCallsPerMinField?.GetValue(w) ?? 0.0);
        internal static double GetAvgTokensPerMin(Window w) => Convert.ToDouble(avgTokensPerMinField?.GetValue(w) ?? 0.0);
        internal static double GetAvgTokensPerCall(Window w) => Convert.ToDouble(avgTokensPerCallField?.GetValue(w) ?? 0.0);

        internal static void ResetLogs(Window w)
        {
            try
            {
                resetMethod?.Invoke(w, null);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk debug window compat", ex);
            }
        }

        internal static void ExportLogs(Window w)
        {
            try
            {
                MethodInfo export = uiUtilType != null ? AccessTools.Method(uiUtilType, "ExportLogs") : null;
                object requests = requestsField?.GetValue(w);
                export?.Invoke(null, new[] { requests });
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk debug window compat", ex);
            }
        }

        internal static bool GetIsEnabled()
        {
            try
            {
                object settings = RimTalkSettingsFacade.Get();
                return settings != null && settingsIsEnabledField != null && (bool)settingsIsEnabledField.GetValue(settings);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk debug window compat", ex);
                return false;
            }
        }

        /// <summary>MUTATION-C: mirrors DrawBottomActions' own CheckboxLabeled return-assign verbatim, including its omission of ModSettings.Write() (see RimTalkDebugScope.ToggleEnabled).</summary>
        internal static void SetIsEnabled(bool value)
        {
            try
            {
                object settings = RimTalkSettingsFacade.Get();
                if (settings != null && settingsIsEnabledField != null)
                {
                    // MUTATION-C: see method summary above -- DrawBottomActions' own CheckboxLabeled return-assign.
                    settingsIsEnabledField.SetValue(settings, value);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk debug window compat", ex);
            }
        }

        // ------------------------------------------------------------------
        // ApiLog access.
        // ------------------------------------------------------------------

        internal static DateTime ApiLogTimestamp(object apiLog) => apiLog != null ? (DateTime)(apiLogTimestampProp?.GetValue(apiLog) ?? DateTime.MinValue) : DateTime.MinValue;
        internal static string ApiLogName(object apiLog) => apiLog != null ? apiLogNameProp?.GetValue(apiLog) as string : null;
        internal static string ApiLogResponse(object apiLog) => apiLog != null ? apiLogResponseProp?.GetValue(apiLog) as string : null;
        internal static string ApiLogInteractionType(object apiLog) => apiLog != null ? apiLogInteractionTypeField?.GetValue(apiLog) as string : null;

        internal static string ApiLogResponseOrGenerating(object apiLog)
        {
            string response = ApiLogResponse(apiLog);
            return response ?? Translator.Translate("RimTalk.DebugWindow.Generating").Resolve();
        }

        internal static string ApiLogElapsedMsText(object apiLog)
        {
            if (apiLog == null) return "";
            string response = ApiLogResponse(apiLog);
            if (response == null) return "";
            int elapsed = Convert.ToInt32(apiLogElapsedMsField?.GetValue(apiLog) ?? 0);
            return elapsed == 0 ? "-" : elapsed.ToString();
        }

        internal static string ApiLogTokensText(object apiLog)
        {
            if (apiLog == null) return "";
            object payload = apiLogPayloadProp?.GetValue(apiLog);
            int tokenCount = payload != null ? Convert.ToInt32(payloadTokenCountProp?.GetValue(payload) ?? 0) : 0;
            if (tokenCount != 0)
                return tokenCount.ToString();
            bool isFirst = apiLog != null && (bool)(apiLogIsFirstDialogueField?.GetValue(apiLog) ?? false);
            return isFirst ? "-" : "";
        }

        internal static string ApiLogState(object apiLog)
        {
            if (apiLog == null || apiLogGetStateMethod == null)
                return "None";
            try
            {
                return apiLogGetStateMethod.Invoke(apiLog, null)?.ToString() ?? "None";
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk debug window compat", ex);
                return "None";
            }
        }

        /// <summary>The pawn this ApiLog is attributed to, resolved by matching Name against the live pawn-states list (mirrors DrawRequestRow's own lookup).</summary>
        internal static Pawn ApiLogPawn(Window w, object apiLog)
        {
            string name = ApiLogName(apiLog);
            if (string.IsNullOrEmpty(name))
                return null;
            IList states = pawnStatesField?.GetValue(w) as IList;
            if (states == null)
                return null;
            for (int i = 0; i < states.Count; i++)
            {
                object state = states[i];
                if (PawnStateLabel(state) == name)
                    return PawnStateOwner(state);
            }
            return null;
        }

        // ------------------------------------------------------------------
        // TalkRequest access.
        // ------------------------------------------------------------------

        internal static Pawn TalkRequestInitiator(object tr) => tr != null ? trInitiatorProp?.GetValue(tr) as Pawn : null;
        internal static Pawn TalkRequestRecipient(object tr) => tr != null ? trRecipientProp?.GetValue(tr) as Pawn : null;
        internal static string TalkRequestPrompt(object tr) => tr != null ? trPromptProp?.GetValue(tr) as string : null;
        internal static DateTime TalkRequestCreatedTime(object tr) => tr != null ? (DateTime)(trCreatedTimeProp?.GetValue(tr) ?? DateTime.MinValue) : DateTime.MinValue;
        internal static string TalkRequestTalkType(object tr) => tr != null ? trTalkTypeProp?.GetValue(tr)?.ToString() ?? "" : "";
        internal static string TalkRequestStatus(object tr) => tr != null ? trStatusProp?.GetValue(tr)?.ToString() ?? "Pending" : "Pending";

        internal static int TalkRequestElapsedSeconds(object tr)
        {
            if (tr == null) return 0;
            string status = TalkRequestStatus(tr);
            int finishedTick = Convert.ToInt32(trFinishedTickProp?.GetValue(tr) ?? -1);
            int createdTick = Convert.ToInt32(trCreatedTickProp?.GetValue(tr) ?? 0);
            int endTick = (status == "Pending" || finishedTick == -1) ? GenTicks.TicksGame : finishedTick;
            return Math.Max(0, endTick - createdTick) / 60;
        }

        internal static string PawnLabelOrDash(Pawn pawn) => pawn != null ? "[" + pawn.LabelShort + "]" : "-";

        // ------------------------------------------------------------------
        // PawnState access.
        // ------------------------------------------------------------------

        internal static Pawn PawnStateOwner(object pawnState) => pawnState != null ? psPawnField?.GetValue(pawnState) as Pawn : null;
        internal static string PawnStateLabel(object pawnState) => PawnStateOwner(pawnState)?.LabelShort ?? "";
        internal static int PawnLastTalkTick(object pawnState) => pawnState != null ? Convert.ToInt32(psLastTalkTickProp?.GetValue(pawnState) ?? 0) : 0;
        internal static double PawnChattiness(object pawnState) => pawnState != null ? Convert.ToDouble(psTalkInitiationWeightProp?.GetValue(pawnState) ?? 0.0) : 0.0;

        internal static bool PawnCanGenerateTalk(object pawnState)
        {
            if (pawnState == null || psCanGenerateTalkMethod == null)
                return false;
            try
            {
                return (bool)(psCanGenerateTalkMethod.Invoke(pawnState, null) ?? false);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk debug window compat", ex);
                return false;
            }
        }

    }
}
