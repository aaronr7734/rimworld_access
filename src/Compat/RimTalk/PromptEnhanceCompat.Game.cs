using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Reflection facade + registration for PromptEnhance (ruaji.rimtalkpromptenhance,
    /// assembly RimTalkHealthEnhance.dll — the "Colony Status" system). No compile-time
    /// reference to the mod assembly anywhere in this file or its sibling scopes.
    ///
    /// SURFACES AND HOW THEY RIDE:
    /// <list type="bullet">
    /// <item><c>MainTabWindow_Announcement</c> — bespoke
    /// <see cref="PromptEnhanceAnnouncementScope"/>. The task card list, the History day's composed
    /// reading and the custom-area rows each need grouped composition a flat captured-extras read
    /// would scatter, so those three ride a hand-modeled table each; everything else — the text
    /// areas via <c>WidgetCapture.RequestTextOverride</c>, every button, and both TabRecord strips —
    /// rides captured-extras (<see cref="ScreenScope.CaptureWindowButtons"/> false,
    /// <see cref="ScreenScope.IncludeCapturedExtrasRegion"/> true). Region-to-tab mirroring uses
    /// <c>WidgetCapture.RequestActivate(WidgetKind.Tab, ...)</c>, which already handles a
    /// per-frame-recreated tab list, so no <c>currentTab</c> field-poke is needed.</item>
    /// <item><c>TaskEditorDialog</c>, <c>GroupDiscussionDialog</c>, <c>AreaEditorDialog</c>,
    /// <c>ColonyCenterAdjustDialog</c> and <c>PromptEditorDialog</c> take plain
    /// <see cref="ScopeForWindow.TryRegisterGenericReaderForWindow"/>: all five draw nothing but
    /// real captured <c>Widgets</c> calls, and GroupDiscussionDialog's two hand-rolled panes (bare
    /// RadioButton/Checkbox under a full-row ButtonInvisible) are exactly what the generic reader's
    /// check-texture and adjacency inference is for.</item>
    /// <item><c>TaskEditorDialog</c> also needs the accept guard: its <c>DoWindowContents</c>
    /// hand-polls Return at the top and calls <c>Event.current.Use(); SaveAndClose();</c>
    /// unconditionally, with no <c>OnAcceptKeyPressed</c> override, so masking the poll alone is
    /// enough. Masked unconditionally whenever <see cref="GenericWindowScope"/> owns the window: the
    /// scope's per-row Enter takes over, and the mod's real Save button stays the explicit commit.</item>
    /// <item><c>Dialog_ColorPicker</c> — bespoke <see cref="PromptEnhanceColorPickerScope"/>: its
    /// ten swatches are raw <c>Widgets.DrawBoxSolid</c> under <c>ButtonInvisible</c> with no check
    /// texture, which the generic engine's adjacency fusion cannot name, so they become ten
    /// <see cref="ScreenScope.DeclaredActions"/> named via <see cref="ColorNameHelper"/>, each
    /// invoking the mod's own <c>Action&lt;Color&gt;</c> callback and closing (vehicle A).</item>
    /// <item><c>AreaDrawingDesignator</c> is DESCOPED: it is a direct <c>Designator</c> subclass and
    /// <c>CustomNamedArea</c> implements <c>ICellBoolGiver</c> rather than <c>Verse.Area</c>, so
    /// neither wires into the vanilla-only ShapeHelper/ViewingModeState/AreaUndoTracker chain, whose
    /// every check is a hardcoded <c>is</c> test against concrete vanilla types. Its buttons still
    /// open the mod's own designator selection unchanged; a keyboard cell-paint path would need its
    /// own ambient claim over the designator's public DesignateSingleCell/CanDesignateCell.</item>
    /// <item>Settings need NO new code: the generic reader already survives tabbed custom settings
    /// pages, the double-draw measure pass, and the glyph-collapsible section shape. Of the three AI
    /// Historian fields only the API Key caption contains "API Key", which the existing
    /// <c>SensitiveFieldLabelSubstrings</c> entry already masks, while URL and Model Name correctly
    /// stay unmasked. Per-group bulk actions are absent in vanilla too, so their absence is parity.</item>
    /// </list>
    /// </summary>
    internal static class PromptEnhanceCompat
    {
        private const string PackageId = "ruaji.rimtalkpromptenhance";

        // MainTabWindow_Announcement and its private navigation fields.
        private static readonly Type mainTabWindowType;
        private static readonly FieldInfo selectedCategoryField; // AnnouncementCategory? on MainTabWindow_Announcement
        private static readonly FieldInfo currentSnapshotIndexField; // int
        private static readonly MethodInfo showPawnSelectorMenuMethod; // private void ShowPawnSelectorMenu(ColonyAnnouncement)

        // ColonyAnnouncementManager (GameComponent).
        private static readonly Type managerType;
        private static readonly PropertyInfo managerInstanceProp;
        private static readonly FieldInfo managerDataField; // public ColonyAnnouncementData Data
        private static readonly FieldInfo managerCustomAreasField; // public List<CustomNamedArea> CustomAreas
        private static readonly PropertyInfo managerDataVersionProp;
        private static readonly MethodInfo addAnnouncementMethod;
        private static readonly MethodInfo deleteAnnouncementMethod;
        private static readonly MethodInfo addCustomAreaMethod;
        private static readonly MethodInfo deleteCustomAreaMethod;
        private static readonly MethodInfo notifyDataChangedMethod;

        // ColonyAnnouncementData.
        private static readonly Type dataType;
        private static readonly FieldInfo colonyOverviewField;
        private static readonly FieldInfo announcementsField; // List<ColonyAnnouncement>
        private static readonly FieldInfo dailySnapshotsField; // List<DailySnapshot>
        private static readonly FieldInfo displayTickOffsetField; // long

        // ColonyAnnouncement.
        private static readonly Type announcementType;
        private static readonly FieldInfo annIdField;
        private static readonly FieldInfo annTitleField;
        private static readonly FieldInfo annDescriptionField;
        private static readonly FieldInfo annCategoryField;
        private static readonly FieldInfo annPriorityField;
        private static readonly FieldInfo annStatusField;
        private static readonly FieldInfo annProgressField;
        private static readonly FieldInfo annAssignedPawnNameField;
        private static readonly FieldInfo annIsGlobalField;
        private static readonly FieldInfo annBlueprintAreaIdField;
        private static readonly FieldInfo annCompletedTickField;

        private static readonly Type categoryEnumType;
        private static readonly Type priorityEnumType;
        private static readonly Type statusEnumType;
        private static object statusActive;
        private static object statusCompleted;
        private static object statusPaused;

        // DailySnapshot.
        private static readonly Type dailySnapshotType;
        private static readonly FieldInfo snapAbsTickField;
        private static readonly FieldInfo snapAISummaryField;
        private static readonly FieldInfo snapPlayerActionsField; // List<string>
        private static readonly FieldInfo snapEventsField; // List<string>
        private static readonly FieldInfo snapDiffReportField;
        private static readonly MethodInfo getDateStringWithOffsetMethod;

        // CustomNamedArea.
        private static readonly Type customAreaType;
        private static readonly FieldInfo areaIdField;
        private static readonly FieldInfo areaLabelField;
        private static readonly FieldInfo areaColorField;
        private static readonly FieldInfo areaIsActiveField;
        private static readonly PropertyInfo areaCellCountProp;

        // Dialog_ColorPicker.
        private static readonly Type colorPickerDialogType;
        private static readonly FieldInfo colorPickerCallbackField; // Action<Color>

        // TaskEditorDialog accept-guard.
        private static readonly Type taskEditorDialogType;

        private static readonly bool coreReady;

        public static bool CoreReady => coreReady;

        static PromptEnhanceCompat()
        {
            var surface = new ReflectionSurface("PromptEnhance compat");

            mainTabWindowType = surface.Type("RimTalkHealthEnhance.MainTabWindow_Announcement");
            managerType = surface.Type("RimTalkHealthEnhance.ColonyAnnouncementManager");
            dataType = surface.Type("RimTalkHealthEnhance.ColonyAnnouncementData");
            announcementType = surface.Type("RimTalkHealthEnhance.ColonyAnnouncement");
            categoryEnumType = surface.Type("RimTalkHealthEnhance.AnnouncementCategory");
            priorityEnumType = surface.Type("RimTalkHealthEnhance.AnnouncementPriority");
            statusEnumType = surface.Type("RimTalkHealthEnhance.AnnouncementStatus");
            dailySnapshotType = surface.Type("RimTalkHealthEnhance.DailySnapshot");
            customAreaType = surface.Type("RimTalkHealthEnhance.Models.CustomNamedArea");

            selectedCategoryField = surface.Field(mainTabWindowType, "selectedCategory");
            currentSnapshotIndexField = surface.Field(mainTabWindowType, "currentSnapshotIndex");

            managerInstanceProp = surface.Property(managerType, "Instance");
            managerDataField = surface.Field(managerType, "Data");
            managerCustomAreasField = surface.Field(managerType, "CustomAreas");
            managerDataVersionProp = surface.Property(managerType, "DataVersion");
            addAnnouncementMethod = surface.Method(managerType, "AddAnnouncement");
            deleteAnnouncementMethod = surface.Method(managerType, "DeleteAnnouncement", new[] { typeof(string) });
            addCustomAreaMethod = surface.Method(managerType, "AddCustomArea");
            deleteCustomAreaMethod = surface.Method(managerType, "DeleteCustomArea", new[] { typeof(string) });
            notifyDataChangedMethod = surface.Method(managerType, "NotifyDataChanged");

            colonyOverviewField = surface.Field(dataType, "ColonyOverview");
            announcementsField = surface.Field(dataType, "Announcements");
            dailySnapshotsField = surface.Field(dataType, "DailySnapshots");
            displayTickOffsetField = surface.Field(dataType, "DisplayTickOffset");

            annIdField = surface.Field(announcementType, "Id");
            annTitleField = surface.Field(announcementType, "Title");
            annDescriptionField = surface.Field(announcementType, "Description");
            annCategoryField = surface.Field(announcementType, "Category");
            annPriorityField = surface.Field(announcementType, "Priority");
            annStatusField = surface.Field(announcementType, "Status");
            annProgressField = surface.Field(announcementType, "Progress");
            annAssignedPawnNameField = surface.Field(announcementType, "AssignedPawnName");
            annIsGlobalField = surface.Field(announcementType, "IsGlobal");
            annBlueprintAreaIdField = surface.Field(announcementType, "BlueprintAreaId");
            annCompletedTickField = surface.Field(announcementType, "CompletedTick");

            snapAbsTickField = surface.Field(dailySnapshotType, "AbsTick");
            snapAISummaryField = surface.Field(dailySnapshotType, "AISummary");
            snapPlayerActionsField = surface.Field(dailySnapshotType, "PlayerActions");
            snapEventsField = surface.Field(dailySnapshotType, "Events");
            snapDiffReportField = surface.Field(dailySnapshotType, "DiffReport");
            getDateStringWithOffsetMethod = surface.Method(dailySnapshotType, "GetDateStringWithOffset", new[] { typeof(long), typeof(Vector2) });

            areaIdField = surface.Field(customAreaType, "Id");
            areaLabelField = surface.Field(customAreaType, "Label");
            areaColorField = surface.Field(customAreaType, "Color");
            areaIsActiveField = surface.Field(customAreaType, "IsActive");
            areaCellCountProp = surface.Property(customAreaType, "CellCount");

            if (statusEnumType != null)
            {
                statusActive = Enum.Parse(statusEnumType, "Active");
                statusCompleted = Enum.Parse(statusEnumType, "Completed");
                statusPaused = Enum.Parse(statusEnumType, "Paused");
            }

            coreReady = surface.Ready;

            // OPTIONAL: each drives one dialog or row action and is null-guarded at its call site,
            // so drift costs that one affordance rather than the whole main tab.
            showPawnSelectorMenuMethod = mainTabWindowType != null
                ? AccessTools.Method(mainTabWindowType, "ShowPawnSelectorMenu")
                : null;
            colorPickerDialogType = AccessTools.TypeByName("RimTalkHealthEnhance.UI.Dialog_ColorPicker");
            colorPickerCallbackField = colorPickerDialogType != null
                ? AccessTools.Field(colorPickerDialogType, "onColorSelected")
                : null;
            taskEditorDialogType = AccessTools.TypeByName("RimTalkHealthEnhance.TaskEditorDialog");
        }

        public static void Register()
        {
            if (!ModsConfig.IsActive(PackageId))
            {
                return;
            }

            if (coreReady)
            {
                ScopeForWindow.Register(mainTabWindowType, delegate (Window w) { return new PromptEnhanceAnnouncementScope(w); });
            }

            // Plain vanilla-idiom dialogs. Each call resolves its own type by name and no-ops when
            // absent, so these five lines carry no risk independent of coreReady.
            ScopeForWindow.TryRegisterGenericReaderForWindow("RimTalkHealthEnhance.TaskEditorDialog");
            ScopeForWindow.TryRegisterGenericReaderForWindow("RimTalkHealthEnhance.UI.GroupDiscussionDialog");
            ScopeForWindow.TryRegisterGenericReaderForWindow("RimTalkHealthEnhance.UI.AreaEditorDialog");
            ScopeForWindow.TryRegisterGenericReaderForWindow("RimTalkHealthEnhance.UI.ColonyCenterAdjustDialog");
            ScopeForWindow.TryRegisterGenericReaderForWindow("RimTalkHealthEnhance.PromptEditorDialog");

            if (colorPickerDialogType != null && colorPickerCallbackField != null)
            {
                ScopeForWindow.Register(colorPickerDialogType, delegate (Window w) { return new PromptEnhanceColorPickerScope(w, colorPickerCallbackField); });
            }

            if (taskEditorDialogType != null)
            {
                MethodInfo doWindowContents = AccessTools.Method(taskEditorDialogType, "DoWindowContents");
                if (doWindowContents != null)
                {
                    RimWorldAccessMod.HarmonyInstance.Patch(doWindowContents,
                        prefix: new HarmonyMethod(typeof(PromptEnhanceCompat), nameof(TaskEditorDrawPrefix)),
                        finalizer: new HarmonyMethod(typeof(PromptEnhanceCompat), nameof(TaskEditorDrawFinalizer)));
                }
                else
                {
                    ModLogger.Warning("PromptEnhance compat: TaskEditorDialog.DoWindowContents not found, its raw Return-key poll cannot be masked (Enter may double-fire a save-and-close alongside the generic reader's own row Enter).");
                }
            }
        }

        /// <summary>
        /// Masks TaskEditorDialog's raw Return poll (top of <c>DoWindowContents</c>, unconditional,
        /// no field or focus check) whenever <see cref="GenericWindowScope"/> owns the window —
        /// unconditional, because the scope claims Enter per row on every frame it is live. The
        /// dialog has no <c>OnAcceptKeyPressed</c> override, so no twin patch is needed, and its real
        /// Save button stays reachable as a captured row for the explicit commit.
        /// </summary>
        public static void TaskEditorDrawPrefix(object __instance, out bool __state)
        {
            Window window = __instance as Window;
            GenericWindowScope scope = FocusStack.Top as GenericWindowScope;
            __state = window != null && scope != null && scope.Owns(window);
            if (__state)
            {
                TextFieldRawPollGuard.MaskAcceptPoll(true);
            }
        }

        public static void TaskEditorDrawFinalizer(bool __state)
        {
            if (__state)
            {
                TextFieldRawPollGuard.RestoreAcceptPoll();
            }
        }

        // Manager and data accessors.

        public static object GetManagerInstance()
        {
            return coreReady ? managerInstanceProp.GetValue(null) : null;
        }

        public static object GetData(object manager)
        {
            return manager == null ? null : managerDataField.GetValue(manager);
        }

        public static IList GetCustomAreas(object manager)
        {
            return manager == null ? null : managerCustomAreasField.GetValue(manager) as IList;
        }

        public static int GetDataVersion(object manager)
        {
            return manager == null ? -1 : (int)managerDataVersionProp.GetValue(manager);
        }

        public static void NotifyDataChanged(object manager)
        {
            if (manager != null)
            {
                notifyDataChangedMethod.Invoke(manager, null);
            }
        }

        public static void DeleteAnnouncement(object manager, string id)
        {
            if (manager != null)
            {
                deleteAnnouncementMethod.Invoke(manager, new object[] { id });
            }
        }

        public static void DeleteCustomArea(object manager, string id)
        {
            if (manager != null)
            {
                deleteCustomAreaMethod.Invoke(manager, new object[] { id });
            }
        }

        public static string GetColonyOverview(object data)
        {
            return data == null ? "" : (colonyOverviewField.GetValue(data) as string ?? "");
        }

        public static IList GetAnnouncements(object data)
        {
            return data == null ? null : announcementsField.GetValue(data) as IList;
        }

        public static IList GetDailySnapshots(object data)
        {
            return data == null ? null : dailySnapshotsField.GetValue(data) as IList;
        }

        public static long GetDisplayTickOffset(object data)
        {
            return data == null ? 0L : (long)displayTickOffsetField.GetValue(data);
        }

        // MainTabWindow_Announcement navigation state.

        public static object GetSelectedCategory(Window dialog)
        {
            return selectedCategoryField.GetValue(dialog);
        }

        public static int GetCurrentSnapshotIndex(Window dialog)
        {
            return (int)currentSnapshotIndexField.GetValue(dialog);
        }

        public static string CategoryLabel(object category)
        {
            if (category == null)
            {
                return "RimWorldAccess.Compat.PromptEnhance.CategoryAll".Translate();
            }
            string key = "RTE_Announcement_Category_" + category;
            return Translator.Translate(key).Resolve();
        }

        public static void ShowPawnSelectorMenu(Window dialog, object announcement)
        {
            if (showPawnSelectorMenuMethod != null)
            {
                showPawnSelectorMenuMethod.Invoke(dialog, new object[] { announcement });
            }
        }

        // ColonyAnnouncement accessors.

        public static string AnnId(object a) => annIdField.GetValue(a) as string ?? "";
        public static string AnnTitle(object a) => annTitleField.GetValue(a) as string ?? "";
        public static string AnnDescription(object a) => annDescriptionField.GetValue(a) as string ?? "";
        public static object AnnCategory(object a) => annCategoryField.GetValue(a);
        public static object AnnPriority(object a) => annPriorityField.GetValue(a);
        public static object AnnStatus(object a) => annStatusField.GetValue(a);
        public static float AnnProgress(object a) => (float)annProgressField.GetValue(a);
        public static string AnnAssignedPawnName(object a) => annAssignedPawnNameField.GetValue(a) as string ?? "";
        public static bool AnnIsGlobal(object a) => (bool)annIsGlobalField.GetValue(a);
        public static string AnnBlueprintAreaId(object a) => annBlueprintAreaIdField.GetValue(a) as string;

        /// <summary>
        /// The mod has no translated Priority label anywhere — its card display and its picker both
        /// show the raw enum name — so this matches rather than inventing a key.
        /// </summary>
        public static string PriorityLabel(object priority)
        {
            return priority?.ToString() ?? "";
        }

        /// <summary>Mirrors DrawAnnouncementItem's own status-cycle button body verbatim — no standalone method exists on the mod side to call instead; the button's handler is inline.</summary>
        public static void CycleStatus(object a, object manager)
        {
            object status = AnnStatus(a);
            if (Equals(status, statusActive))
            {
                // MUTATION-C: mirrors the button's Active branch verbatim (Active -> Completed + CompletedTick stamp).
                annStatusField.SetValue(a, statusCompleted);
                annCompletedTickField.SetValue(a, Find.TickManager.TicksGame);
            }
            else if (Equals(status, statusPaused))
            {
                // MUTATION-C: mirrors the button's Paused branch verbatim (Paused -> Active).
                annStatusField.SetValue(a, statusActive);
            }
            else
            {
                // MUTATION-C: mirrors the button's Completed branch verbatim (Completed -> Active).
                annStatusField.SetValue(a, statusActive);
            }
            NotifyDataChanged(manager);
        }

        public static string StatusCycleLabel(object a)
        {
            object status = AnnStatus(a);
            if (Equals(status, statusActive))
            {
                return Translator.Translate("RTE_Announcement_Complete").Resolve();
            }
            if (Equals(status, statusPaused))
            {
                return Translator.Translate("RTE_Announcement_Resume").Resolve();
            }
            return Translator.Translate("RTE_Announcement_Reopen").Resolve();
        }

        public static string StatusLabel(object status)
        {
            return Translator.Translate("RTE_Announcement_Status_" + status).Resolve();
        }

        public static bool IsCompleted(object a)
        {
            return Equals(AnnStatus(a), statusCompleted);
        }

        public static IEnumerable<object> AnnouncementCategories()
        {
            foreach (object v in Enum.GetValues(categoryEnumType))
            {
                yield return v;
            }
        }

        public static void OpenTaskEditor(object announcementOrNull, object manager)
        {
            if (taskEditorDialogType == null)
            {
                return;
            }
            object instance = Activator.CreateInstance(taskEditorDialogType, announcementOrNull, manager);
            Find.WindowStack.Add((Window)instance);
        }

        // DailySnapshot accessors.

        public static long SnapAbsTick(object s) => (long)snapAbsTickField.GetValue(s);
        public static string SnapAISummary(object s) => snapAISummaryField.GetValue(s) as string ?? "";
        public static List<string> SnapPlayerActions(object s) => snapPlayerActionsField.GetValue(s) as List<string>;
        public static List<string> SnapEvents(object s) => snapEventsField.GetValue(s) as List<string>;
        public static string SnapDiffReport(object s) => snapDiffReportField.GetValue(s) as string ?? "";

        public static string SnapDateString(object s, long tickOffset)
        {
            Vector2 location = Vector2.zero;
            Map currentMap = Find.CurrentMap;
            if (currentMap != null)
            {
                location = Find.WorldGrid.LongLatOf(currentMap.Tile);
            }
            return (string)getDateStringWithOffsetMethod.Invoke(s, new object[] { tickOffset, location });
        }

        // CustomNamedArea accessors.

        public static string AreaId(object area) => areaIdField.GetValue(area) as string ?? "";
        public static string AreaLabel(object area) => areaLabelField.GetValue(area) as string ?? "";
        public static Color AreaColor(object area) => (Color)areaColorField.GetValue(area);
        public static bool AreaIsActive(object area) => (bool)areaIsActiveField.GetValue(area);
        public static int AreaCellCount(object area) => (int)areaCellCountProp.GetValue(area);
    }
}
