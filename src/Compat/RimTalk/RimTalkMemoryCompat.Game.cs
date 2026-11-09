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
    /// Reflection facade for the ExpandMemory extension (cj.rimtalk.expandmemory). The
    /// RimTalkMemoryPatch assembly is never linked, so every member is resolved by name, on three
    /// independent surfaces so a drift in one never takes the others down:
    ///
    /// <b>Memory data</b> (<c>FourLayerMemoryComp</c>, <c>MemoryEntry</c>, the mod's own
    /// <c>TextUtil.Translate</c> labels): the "Memories" branch of the pawn inspection tree.
    ///
    /// <b>Memory tab</b> (<c>MemoryTabWindowBridge</c> and the <c>MemoryTabWindow.Context</c> its
    /// elements share): <see cref="RimTalkMemoryMainTabScope"/>. Every element reads that one
    /// context, so the scope reads and drives it there rather than through the draw.
    ///
    /// <b>Common Knowledge</b> (<c>Dialog_CommonKnowledge</c> and its library):
    /// <see cref="RimTalkCommonKnowledgeScope"/>.
    ///
    /// Everything else in the mod draws plain captured Widgets inside absorbing windows the
    /// generic reader already attaches to (the pawn selector and personalize side windows, the
    /// create-memory, prompt, tag-test and injection-preview dialogs); the settings live in the
    /// shared <c>Dialog_ModSettings</c>. <c>ITab_Memory</c> is still never registered by the mod.
    /// </summary>
    internal static class RimTalkMemoryCompat
    {
        private const string PackageId = "cj.rimtalk.expandmemory";

        // Memory data

        private static readonly Type memoryCompType;
        private static readonly PropertyInfo activeMemoriesProp;
        private static readonly PropertyInfo situationalMemoriesProp;
        private static readonly PropertyInfo eventLogMemoriesProp;
        private static readonly PropertyInfo archiveMemoriesProp;
        private static readonly PropertyInfo interactorProp;
        private static readonly PropertyInfo summarizerProp;
        private static readonly MethodInfo pinMemoryMethod;
        private static readonly MethodInfo checkSummarizingMethod;
        private static readonly MethodInfo checkSummarizedMethod;

        private static readonly FieldInfo memContentField;
        private static readonly FieldInfo memTypeField;
        private static readonly FieldInfo memLayerField;
        private static readonly FieldInfo memGameTickField;
        private static readonly PropertyInfo memEndGameTickProp;
        private static readonly PropertyInfo memImportanceProp;
        private static readonly PropertyInfo memActivityProp;
        private static readonly FieldInfo memRelatedPawnNameField;
        private static readonly FieldInfo memIsPinnedField;
        private static readonly FieldInfo memTagsField;
        private static readonly FieldInfo memNoteField;
        private static readonly PropertyInfo memAgeStringProp;

        private static readonly MethodInfo translateLayerMethod;
        private static readonly MethodInfo translateTypeMethod;

        private static readonly bool dataReady;

        // Memory tab

        private static readonly Type bridgeType;
        private static readonly FieldInfo bridgeTabWindowField;
        private static readonly FieldInfo tabWindowContextField;
        private static readonly FieldInfo tabWindowDetailsField;
        private static readonly FieldInfo contextMemoryCompField;
        private static readonly FieldInfo contextSelectionField;
        private static readonly FieldInfo contextTreatedTimelineField;
        private static readonly PropertyInfo contextFocuseProp;
        private static readonly MethodInfo contextSetFocuseMethod;
        private static readonly PropertyInfo contextCursorTickProp;
        private static readonly MethodInfo contextRaiseRePositionTimelineMethod;
        private static readonly MethodInfo contextRaiseRePositionCursorMethod;
        private static readonly FieldInfo detailsEditingMemoryField;
        private static readonly MethodInfo selectionContainsMethod;
        private static readonly MethodInfo selectionAddMethod;
        private static readonly MethodInfo selectionClearMethod;

        private static readonly bool tabReady;

        // Common Knowledge

        private static readonly Type dialogCommonKnowledgeType;
        private static readonly PropertyInfo entriesProp; // List<CommonKnowledgeEntry>
        private static readonly FieldInfo dckSearchFilterField;
        private static readonly FieldInfo dckSelectedEntriesField;
        private static readonly FieldInfo dckLastSelectedEntryField;
        private static readonly FieldInfo dckCurrentCategoryField;
        private static readonly FieldInfo dckEditModeField;
        private static readonly FieldInfo dckLibraryField;
        private static readonly MethodInfo dckGetCategoryCountMethod;
        private static readonly Type knowledgeCategoryType;

        /// <summary>The mod's own enum names (RimTalk.Memory.UI.KnowledgeCategory), read once so a mod update can never leave this list stale against the Enum.Parse calls below.</summary>
        private static readonly string[] KnowledgeCategoryNames;

        private static readonly Type entryType;
        private static readonly FieldInfo entryTagField;
        private static readonly FieldInfo entryContentField;
        private static readonly FieldInfo entryImportanceField;
        private static readonly FieldInfo entryIsEnabledField;

        private static readonly MethodInfo canBeExtractedMethod;
        private static readonly MethodInfo setCanBeExtractedMethod;
        private static readonly MethodInfo canBeMatchedMethod;
        private static readonly MethodInfo setCanBeMatchedMethod;
        private static readonly MethodInfo getEntryCategoryMethod;
        private static readonly MethodInfo getCategoryLabelMethod;
        private static readonly MethodInfo getVisibilityTextMethod;

        private static readonly bool commonKnowledgeReady;

        public static bool CommonKnowledgeReady { get { return commonKnowledgeReady; } }

        static RimTalkMemoryCompat()
        {
            var data = new ReflectionSurface("RimTalk memory compat (memory data)");

            memoryCompType = data.Type("RimTalk.Memory.FourLayerMemoryComp");
            Type memoryEntryType = data.Type("RimTalk.Memory.MemoryEntry");
            Type memoryLayerType = data.Type("RimTalk.Memory.MemoryLayer");
            Type memoryTypeType = data.Type("RimTalk.Memory.MemoryType");
            Type interactorType = data.Type("RimTalk.Memory.Maintenance.MemoryInteractor");
            Type summarizerType = data.Type("RimTalk.Memory.Maintenance.MemorySummarizer");
            Type textUtilType = data.Type("RimTalk.Memory.Utils.TextUtil");

            activeMemoriesProp = data.Property(memoryCompType, "ActiveMemories");
            situationalMemoriesProp = data.Property(memoryCompType, "SituationalMemories");
            eventLogMemoriesProp = data.Property(memoryCompType, "EventLogMemories");
            archiveMemoriesProp = data.Property(memoryCompType, "ArchiveMemories");
            interactorProp = data.Property(memoryCompType, "Interactor");
            summarizerProp = data.Property(memoryCompType, "Summarizer");

            memContentField = data.Field(memoryEntryType, "Content");
            memTypeField = data.Field(memoryEntryType, "Type");
            memLayerField = data.Field(memoryEntryType, "Layer");
            memGameTickField = data.Field(memoryEntryType, "GameTick");
            memEndGameTickProp = data.Property(memoryEntryType, "EndGameTick");
            memImportanceProp = data.Property(memoryEntryType, "Importance");
            memActivityProp = data.Property(memoryEntryType, "Activity");
            memRelatedPawnNameField = data.Field(memoryEntryType, "relatedPawnName");
            memIsPinnedField = data.Field(memoryEntryType, "IsPinned");
            memTagsField = data.Field(memoryEntryType, "Tags");
            memNoteField = data.Field(memoryEntryType, "Note");
            memAgeStringProp = data.Property(memoryEntryType, "AgeString");

            if (memoryEntryType != null)
            {
                pinMemoryMethod = data.Method(interactorType, "PinMemory", new[] { memoryEntryType, typeof(bool) });
                checkSummarizingMethod = data.Method(summarizerType, "CheckSummarizing", new[] { memoryEntryType });
                checkSummarizedMethod = data.Method(summarizerType, "CheckSummarized", new[] { memoryEntryType });
            }
            if (memoryLayerType != null && memoryTypeType != null)
            {
                translateLayerMethod = data.Method(textUtilType, "Translate", new[] { memoryLayerType });
                translateTypeMethod = data.Method(textUtilType, "Translate", new[] { memoryTypeType });
            }

            dataReady = data.Ready;

            var tab = new ReflectionSurface("RimTalk memory compat (memory tab)");

            bridgeType = tab.Type("RimTalk.Memory.UI.TabWindow.MemoryTabWindowBridge");
            Type tabWindowType = tab.Type("RimTalk.Memory.UI.TabWindow.MemoryTabWindow");
            Type contextType = tab.Supplied("RimTalk.Memory.UI.TabWindow.MemoryTabWindow.Context",
                tabWindowType != null ? AccessTools.Inner(tabWindowType, "Context") : null);
            Type detailsType = tab.Type("RimTalk.Memory.UI.TabWindow.MemoryDetails");

            bridgeTabWindowField = tab.Field(bridgeType, "_memoryTabWindow");
            tabWindowContextField = tab.Field(tabWindowType, "_context");
            tabWindowDetailsField = tab.Field(tabWindowType, "_details");
            contextMemoryCompField = tab.Field(contextType, "MemoryComp");
            contextSelectionField = tab.Field(contextType, "Selection");
            contextTreatedTimelineField = tab.Field(contextType, "TreatedTimelineMemories");
            contextFocuseProp = tab.Property(contextType, "Focuse");
            contextCursorTickProp = tab.Property(contextType, "CursorTick");
            contextRaiseRePositionTimelineMethod = tab.Method(contextType, "RaiseRePositionTimeline", Type.EmptyTypes);
            contextRaiseRePositionCursorMethod = tab.Method(contextType, "RaiseRePositionCursor", Type.EmptyTypes);
            detailsEditingMemoryField = tab.Field(detailsType, "_editingMemory");

            Type selectionType = contextSelectionField?.FieldType;
            if (memoryEntryType != null)
            {
                contextSetFocuseMethod = tab.Method(contextType, "SetFocuse", new[] { memoryEntryType });
                selectionContainsMethod = tab.Method(selectionType, "Contains", new[] { memoryEntryType });
                selectionAddMethod = tab.Method(selectionType, "Add", new[] { memoryEntryType });
                selectionClearMethod = tab.Method(selectionType, "Clear", Type.EmptyTypes);
            }

            tabReady = dataReady && tab.Ready && contextCursorTickProp?.GetSetMethod() != null;

            var ck = new ReflectionSurface("RimTalk memory compat (Common Knowledge)");

            dialogCommonKnowledgeType = ck.Type("RimTalk.Memory.UI.Dialog_CommonKnowledge");
            Type libraryType = ck.Type("RimTalk.Memory.CommonKnowledgeLibrary");
            entryType = ck.Type("RimTalk.Memory.CommonKnowledgeEntry");
            knowledgeCategoryType = ck.Type("RimTalk.Memory.UI.KnowledgeCategory");
            Type extendedType = ck.Type("RimTalk.Memory.ExtendedKnowledgeEntry");
            Type uiHelpersType = ck.Type("RimTalk.Memory.UI.CommonKnowledgeUIHelpers");

            entriesProp = ck.Property(libraryType, "Entries");

            dckSearchFilterField = ck.Field(dialogCommonKnowledgeType, "searchFilter");
            dckSelectedEntriesField = ck.Field(dialogCommonKnowledgeType, "selectedEntries");
            dckLastSelectedEntryField = ck.Field(dialogCommonKnowledgeType, "lastSelectedEntry");
            dckCurrentCategoryField = ck.Field(dialogCommonKnowledgeType, "currentCategory");
            dckEditModeField = ck.Field(dialogCommonKnowledgeType, "editMode");
            dckLibraryField = ck.Field(dialogCommonKnowledgeType, "library");
            dckGetCategoryCountMethod = knowledgeCategoryType != null
                ? ck.Method(dialogCommonKnowledgeType, "GetCategoryCount", new[] { knowledgeCategoryType })
                : null;

            entryTagField = ck.Field(entryType, "tag");
            entryContentField = ck.Field(entryType, "content");
            entryImportanceField = ck.Field(entryType, "importance");
            entryIsEnabledField = ck.Field(entryType, "isEnabled");

            if (entryType != null)
            {
                canBeExtractedMethod = ck.Method(extendedType, "CanBeExtracted", new[] { entryType });
                setCanBeExtractedMethod = ck.Method(extendedType, "SetCanBeExtracted", new[] { entryType, typeof(bool) });
                canBeMatchedMethod = ck.Method(extendedType, "CanBeMatched", new[] { entryType });
                setCanBeMatchedMethod = ck.Method(extendedType, "SetCanBeMatched", new[] { entryType, typeof(bool) });
                getEntryCategoryMethod = ck.Method(uiHelpersType, "GetEntryCategory", new[] { entryType });
                getVisibilityTextMethod = ck.Method(uiHelpersType, "GetVisibilityText", new[] { entryType });
            }
            getCategoryLabelMethod = knowledgeCategoryType != null
                ? ck.Method(uiHelpersType, "GetCategoryLabel", new[] { knowledgeCategoryType })
                : null;

            KnowledgeCategoryNames = knowledgeCategoryType != null
                ? Enum.GetNames(knowledgeCategoryType)
                : new string[0];

            commonKnowledgeReady = ck.Ready;
        }

        public static void Register()
        {
            if (!ModsConfig.IsActive(PackageId))
            {
                return;
            }

            if (tabReady)
            {
                ScopeForWindow.Register(bridgeType, delegate (Window w)
                {
                    return new RimTalkMemoryMainTabScope(w);
                });
            }
            if (commonKnowledgeReady)
            {
                ScopeForWindow.Register(dialogCommonKnowledgeType, delegate (Window w)
                {
                    return new RimTalkCommonKnowledgeScope(w);
                });
            }
            if (dataReady)
            {
                InspectNodeRegistry.RegisterCategoryExtender("Character", AddMemoriesNode);
            }
        }

        // Memory tab. Every element of the tab shares one MemoryTabWindow.Context, owned by the
        // bridge through its private MemoryTabWindow.

        internal static object TabContext(Window bridge)
        {
            object tabWindow = bridgeTabWindowField.GetValue(bridge);
            return tabWindow == null ? null : tabWindowContextField.GetValue(tabWindow);
        }

        internal static object TabDetails(Window bridge)
        {
            object tabWindow = bridgeTabWindowField.GetValue(bridge);
            return tabWindow == null ? null : tabWindowDetailsField.GetValue(tabWindow);
        }

        internal static object ContextMemoryComp(object context)
        {
            return context == null ? null : contextMemoryCompField.GetValue(context);
        }

        internal static Pawn ContextPawn(object context)
        {
            return (ContextMemoryComp(context) as ThingComp)?.parent as Pawn;
        }

        internal static IList ContextTimelineMemories(object context)
        {
            return context == null ? null : contextTreatedTimelineField.GetValue(context) as IList;
        }

        internal static object ContextFocuse(object context)
        {
            return context == null ? null : contextFocuseProp.GetValue(context);
        }

        internal static bool IsSelected(object context, object memory)
        {
            object selection = context == null ? null : contextSelectionField.GetValue(context);
            return selection != null && (bool)selectionContainsMethod.Invoke(selection, new[] { memory });
        }

        internal static int SelectionCount(object context)
        {
            return context == null ? 0 : CountOf(contextSelectionField.GetValue(context));
        }

        /// <summary>
        /// MUTATION-C: mirrors MemoryCard.HandleSelect's click body (Selection.Clear unless Ctrl
        /// is held, then Selection.Add and SetFocuse). The card's only vehicle is a
        /// Widgets.ButtonInvisible that the virtualized timeline draws only while the card is
        /// scrolled into view, and its body reads the live Ctrl state, so the two gestures go
        /// through the context's own public members instead.
        /// </summary>
        internal static void SelectMemory(object context, object memory, bool addToSelection)
        {
            object selection = contextSelectionField.GetValue(context);
            if (!addToSelection)
            {
                selectionClearMethod.Invoke(selection, null);
            }
            selectionAddMethod.Invoke(selection, new[] { memory });
            contextSetFocuseMethod.Invoke(context, new[] { memory });
        }

        /// <summary>
        /// Scrolls the timeline and the chronicle cursor to <paramref name="memory"/> through the
        /// same cursor tick and reposition events the chronicle axis raises on a click. View state
        /// only.
        /// </summary>
        internal static void RevealMemory(object context, object memory)
        {
            contextCursorTickProp.GetSetMethod().Invoke(context, new object[] { MemGameTick(memory) });
            contextRaiseRePositionTimelineMethod.Invoke(context, null);
            contextRaiseRePositionCursorMethod.Invoke(context, null);
        }

        internal static bool DetailsEditing(object details)
        {
            return details != null && detailsEditingMemoryField.GetValue(details) != null;
        }

        // FourLayerMemoryComp

        internal static IList ActiveMemories(object comp) { return comp == null ? null : activeMemoriesProp.GetValue(comp) as IList; }
        internal static IList SituationalMemories(object comp) { return comp == null ? null : situationalMemoriesProp.GetValue(comp) as IList; }
        internal static IList EventLogMemories(object comp) { return comp == null ? null : eventLogMemoriesProp.GetValue(comp) as IList; }
        internal static IList ArchiveMemories(object comp) { return comp == null ? null : archiveMemoriesProp.GetValue(comp) as IList; }

        /// <summary>Vehicle A: the pin button's own click body, <c>MemoryComp.Interactor.PinMemory(memory, !memory.IsPinned)</c>.</summary>
        internal static void TogglePin(object comp, object memory)
        {
            object interactor = comp == null ? null : interactorProp.GetValue(comp);
            if (interactor != null)
            {
                pinMemoryMethod.Invoke(interactor, new object[] { memory, !MemIsPinned(memory) });
            }
        }

        internal static bool IsSummarizing(object comp, object memory)
        {
            object summarizer = comp == null ? null : summarizerProp.GetValue(comp);
            return summarizer != null && (bool)checkSummarizingMethod.Invoke(summarizer, new[] { memory });
        }

        internal static bool IsSummarized(object comp, object memory)
        {
            object summarizer = comp == null ? null : summarizerProp.GetValue(comp);
            return summarizer != null && (bool)checkSummarizedMethod.Invoke(summarizer, new[] { memory });
        }

        // MemoryEntry

        internal static string MemContent(object entry) { return memContentField.GetValue(entry) as string ?? ""; }
        internal static int MemGameTick(object entry) { return (int)memGameTickField.GetValue(entry); }
        internal static int MemEndGameTick(object entry) { return (int)memEndGameTickProp.GetValue(entry); }
        internal static float MemImportance(object entry) { return (float)memImportanceProp.GetValue(entry); }
        internal static float MemActivity(object entry) { return (float)memActivityProp.GetValue(entry); }
        internal static string MemRelatedPawnName(object entry) { return memRelatedPawnNameField.GetValue(entry) as string; }
        internal static bool MemIsPinned(object entry) { return (bool)memIsPinnedField.GetValue(entry); }
        internal static IList<string> MemTags(object entry) { return memTagsField.GetValue(entry) as IList<string>; }
        internal static string MemNote(object entry) { return memNoteField.GetValue(entry) as string; }
        internal static string MemAgeString(object entry) { return memAgeStringProp.GetValue(entry) as string ?? ""; }

        /// <summary>The layer name the mod's own UI shows (TextUtil.Translate).</summary>
        internal static string MemLayerLabel(object entry)
        {
            return translateLayerMethod.Invoke(null, new[] { memLayerField.GetValue(entry) }) as string ?? "";
        }

        /// <summary>The type name the mod's own UI shows (TextUtil.Translate).</summary>
        internal static string MemTypeLabel(object entry)
        {
            return translateTypeMethod.Invoke(null, new[] { memTypeField.GetValue(entry) }) as string ?? "";
        }

        // CommonKnowledgeLibrary / CommonKnowledgeEntry

        internal static IList GetEntries(object library) { return entriesProp.GetValue(library) as IList; }

        internal static string EntryTag(object entry) { return entryTagField.GetValue(entry) as string; }
        internal static string EntryContent(object entry) { return entryContentField.GetValue(entry) as string; }
        internal static float EntryImportance(object entry) { return (float)entryImportanceField.GetValue(entry); }
        internal static bool EntryIsEnabled(object entry) { return (bool)entryIsEnabledField.GetValue(entry); }

        /// <summary>MUTATION-C: mirrors DrawEntryRow's own return-assign (`Widgets.Checkbox(..., ref entry.isEnabled, ...)`) -- a bare public field with no gated setter.</summary>
        internal static void SetEntryIsEnabled(object entry, bool value) { entryIsEnabledField.SetValue(entry, value); }

        internal static bool EntryCanBeExtracted(object entry) { return (bool)canBeExtractedMethod.Invoke(null, new[] { entry }); }
        internal static void SetEntryCanBeExtracted(object entry, bool value) { setCanBeExtractedMethod.Invoke(null, new object[] { entry, value }); }
        internal static bool EntryCanBeMatched(object entry) { return (bool)canBeMatchedMethod.Invoke(null, new[] { entry }); }
        internal static void SetEntryCanBeMatched(object entry, bool value) { setCanBeMatchedMethod.Invoke(null, new object[] { entry, value }); }

        internal static string EntryCategoryLabel(object entry)
        {
            object category = getEntryCategoryMethod.Invoke(null, new[] { entry });
            return getCategoryLabelMethod.Invoke(null, new[] { category }) as string;
        }

        internal static string EntryVisibilityText(object entry)
        {
            return getVisibilityTextMethod.Invoke(null, new[] { entry }) as string;
        }

        // Dialog_CommonKnowledge instance state

        internal static string GetSearchFilter(Window w) { return dckSearchFilterField.GetValue(w) as string ?? ""; }

        /// <summary>MUTATION-C: mirrors DrawToolbar's own return-assign (`searchFilter = Widgets.TextField(...)`) -- a bare private field with no gated setter.</summary>
        internal static void SetSearchFilter(Window w, string value) { dckSearchFilterField.SetValue(w, value ?? ""); }

        internal static object GetCurrentCategory(Window w) { return dckCurrentCategoryField.GetValue(w); }

        /// <summary>MUTATION-C: mirrors DrawSidebar's own category-button click body (currentCategory assign + selectedEntries.Clear()).</summary>
        internal static void SetCurrentCategory(Window w, string categoryName)
        {
            dckCurrentCategoryField.SetValue(w, Enum.Parse(knowledgeCategoryType, categoryName));
            InvokeSetMethod(dckSelectedEntriesField.GetValue(w), "Clear", null, entryType);
        }

        internal static IEnumerable<string> KnowledgeCategories()
        {
            return KnowledgeCategoryNames;
        }

        internal static string CategoryLabel(string categoryName)
        {
            object category = Enum.Parse(knowledgeCategoryType, categoryName);
            return getCategoryLabelMethod.Invoke(null, new[] { category }) as string;
        }

        /// <summary>Vehicle A: the dialog's own private GetCategoryCount(KnowledgeCategory) verbatim.</summary>
        internal static int GetCategoryCount(Window w, string categoryName)
        {
            object category = Enum.Parse(knowledgeCategoryType, categoryName);
            object result = dckGetCategoryCountMethod.Invoke(w, new[] { category });
            return result is int i ? i : 0;
        }

        internal static object GetLibrary(Window w) { return dckLibraryField.GetValue(w); }

        internal static bool IsEditMode(Window w) { return (bool)dckEditModeField.GetValue(w); }

        internal static bool IsEntrySelected(Window w, object entry)
        {
            return (bool)InvokeSetMethod(dckSelectedEntriesField.GetValue(w), "Contains", entry, entryType);
        }

        internal static int SelectedEntryCount(Window w)
        {
            return CountOf(dckSelectedEntriesField.GetValue(w));
        }

        /// <summary>A HashSet's element count; HashSet&lt;T&gt; implements only the generic ICollection.</summary>
        private static int CountOf(object set)
        {
            int count = 0;
            if (set is IEnumerable enumerable)
            {
                foreach (object unused in enumerable)
                {
                    count++;
                }
            }
            return count;
        }

        internal static IEnumerable<object> SelectedEntries(Window w)
        {
            if (dckSelectedEntriesField.GetValue(w) is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    yield return item;
                }
            }
        }

        /// <summary>MUTATION-C: mirrors HandleEntryClick's plain-click branch (Clear, Add, set lastSelectedEntry, clear editMode -- all bare fields with no gated setter).</summary>
        internal static void ReplaceEntrySelectionWith(Window w, object entry)
        {
            // MUTATION-C: mirrors HandleEntryClick's plain-click branch verbatim (Clear, Add, set lastSelectedEntry, clear editMode -- all bare fields with no gated setter).
            object set = dckSelectedEntriesField.GetValue(w);
            InvokeSetMethod(set, "Clear", null, entryType);
            InvokeSetMethod(set, "Add", entry, entryType);
            dckLastSelectedEntryField.SetValue(w, entry);
            // MUTATION-C: same plain-click branch as above -- clears edit mode too.
            dckEditModeField.SetValue(w, false);
        }

        /// <summary>MUTATION-C: mirrors HandleEntryClick's Ctrl-click branch (Contains/Remove/Add toggle, plus lastSelectedEntry -- editMode is untouched on this branch, decompiled-verified).</summary>
        internal static void ToggleEntrySelection(Window w, object entry)
        {
            // MUTATION-C: see summary above -- Ctrl-click toggle branch.
            object set = dckSelectedEntriesField.GetValue(w);
            bool contains = (bool)InvokeSetMethod(set, "Contains", entry, entryType);
            InvokeSetMethod(set, contains ? "Remove" : "Add", entry, entryType);
            dckLastSelectedEntryField.SetValue(w, entry);
        }

        private static object InvokeSetMethod(object set, string name, object arg, Type elementType)
        {
            if (set == null)
            {
                return name == "Contains" ? (object)false : null;
            }
            MethodInfo m = arg == null
                ? set.GetType().GetMethod(name, Type.EmptyTypes)
                : set.GetType().GetMethod(name, new[] { elementType });
            return m?.Invoke(set, arg == null ? null : new[] { arg });
        }

        /// <summary>MUTATION-C: reorders the SAME live backing list (`List&lt;CommonKnowledgeEntry&gt; Entries`) a single position -- the keyboard equivalent of the mouse's drag-to-reorder, one row at a time rather than reproducing its multi-select block-move math. Returns false when already at that end.</summary>
        internal static bool ReorderEntry(object library, object entry, int direction)
        {
            IList entries = GetEntries(library);
            if (entries == null)
            {
                return false;
            }
            int index = entries.IndexOf(entry);
            int target = index + direction;
            if (index < 0 || target < 0 || target >= entries.Count)
            {
                return false;
            }
            object swap = entries[target];
            entries[target] = entry;
            entries[index] = swap;
            return true;
        }

        // Read-only Memories reviewer (pawn inspection tree, "Character" category)

        private static void AddMemoriesNode(InspectionTreeItem categoryItem, object obj)
        {
            if (!(obj is Pawn pawn))
            {
                return;
            }
            object comp = FindMemoryComp(pawn);
            if (comp == null)
            {
                return;
            }
            IList active = ActiveMemories(comp);
            IList situational = SituationalMemories(comp);
            IList eventLog = EventLogMemories(comp);
            IList archive = ArchiveMemories(comp);
            int total = (active?.Count ?? 0) + (situational?.Count ?? 0) + (eventLog?.Count ?? 0) + (archive?.Count ?? 0);
            if (total == 0)
            {
                return;
            }

            string label = "RimWorldAccess.Compat.RimTalk.Memory.MemoriesNode".Translate(total);
            InspectNodeFactory.Section(categoryItem, label, pawn, delegate (InspectionTreeItem section)
            {
                AddLayerSubsection(section, active);
                AddLayerSubsection(section, situational);
                AddLayerSubsection(section, eventLog);
                AddLayerSubsection(section, archive);
            });
        }

        /// <summary>Each comp list holds one layer, so its first entry names the layer.</summary>
        private static void AddLayerSubsection(InspectionTreeItem parent, IList memories)
        {
            if (memories == null || memories.Count == 0 || memories[0] == null)
            {
                return;
            }
            string label = "RimWorldAccess.Compat.RimTalk.Memory.LayerNode".Translate(MemLayerLabel(memories[0]), memories.Count);
            InspectNodeFactory.Section(parent, label, memories, delegate (InspectionTreeItem layerSection)
            {
                foreach (object entry in memories)
                {
                    if (entry != null)
                    {
                        InspectNodeFactory.DetailLine(layerSection, MemoryLine(entry));
                    }
                }
            });
        }

        private static string MemoryLine(object entry)
        {
            string relatedPawn = MemRelatedPawnName(entry);
            string line = string.IsNullOrEmpty(relatedPawn)
                ? "RimWorldAccess.Compat.RimTalk.Memory.MemoryLine".Translate(
                    MemTypeLabel(entry), MemContent(entry), MemAgeString(entry),
                    MemImportance(entry).ToString("F2"), MemActivity(entry).ToString("F2"))
                : "RimWorldAccess.Compat.RimTalk.Memory.MemoryLineWithPawn".Translate(
                    MemTypeLabel(entry), MemContent(entry), MemAgeString(entry),
                    MemImportance(entry).ToString("F2"), MemActivity(entry).ToString("F2"), relatedPawn);
            return MemIsPinned(entry)
                ? (string)"RimWorldAccess.Compat.RimTalk.Memory.MemoryLinePinned".Translate(line)
                : line;
        }

        /// <summary>AllComps type match: <c>ThingCompUtility.TryGetComp&lt;T&gt;</c> needs a compile-time T this assembly never has.</summary>
        private static object FindMemoryComp(Pawn pawn)
        {
            if (pawn?.AllComps == null)
            {
                return null;
            }
            foreach (ThingComp comp in pawn.AllComps)
            {
                if (comp != null && memoryCompType.IsInstanceOfType(comp))
                {
                    return comp;
                }
            }
            return null;
        }
    }
}
