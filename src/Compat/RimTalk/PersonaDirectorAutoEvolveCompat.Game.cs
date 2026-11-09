using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Reflection facade for PersonaDirector's <c>Window_AutoEvolveConfig</c>, read by
    /// <see cref="PersonaDirectorAutoEvolveScope"/>. A miss on this surface leaves the window on
    /// the generic reader without touching the batch director.
    ///
    /// The window's private setters (<c>SetNotify</c>, <c>SetSpeed</c>, <c>SetMode</c>,
    /// <c>SetAdvancedPreset</c>, <c>ApplySelectedEnabled</c>, <c>ApplySelectedInterval</c>) are
    /// the vehicles wherever one exists; only the inline checkbox bodies are hand-copied.
    /// </summary>
    internal static class PersonaDirectorAutoEvolveCompat
    {
        private const string Context = "PersonaDirector compat (auto evolve)";

        internal static readonly Type WindowType;

        private static readonly FieldInfo rosterField;
        private static readonly FieldInfo batchIntervalBufferField;
        private static readonly FieldInfo historyLimitBufferField;
        private static readonly FieldInfo changedField;
        private static readonly MethodInfo setModeMethod;
        private static readonly MethodInfo setNotifyMethod;
        private static readonly MethodInfo setSpeedMethod;
        private static readonly MethodInfo setAdvancedPresetMethod;
        private static readonly MethodInfo applySelectedEnabledMethod;
        private static readonly MethodInfo applySelectedIntervalMethod;
        private static readonly MethodInfo getSpeedLabelMethod;

        private static readonly FieldInfo globalEnabledField;
        private static readonly FieldInfo defaultEnabledField;
        private static readonly FieldInfo autoNotifyField;
        private static readonly FieldInfo speedProtectionField;
        private static readonly FieldInfo autoModeField;
        private static readonly FieldInfo autoEvolveNotesField;
        private static readonly FieldInfo evolvePresetField;
        private static readonly FieldInfo autoGenCategoriesField;
        private static readonly MethodInfo ensureAutoGenCategoriesMethod;
        private static readonly FieldInfo orderedCategoryIdsField;
        private static readonly FieldInfo onlyOnRoleChangeField;

        private static readonly FieldInfo configEnabledField;
        private static readonly FieldInfo configNextUpdateTickField;
        private static readonly FieldInfo configDaysBufferField;
        private static readonly PropertyInfo configTimedUpdatesProp;

        private static readonly Type worldComponentType;
        private static readonly MethodInfo getAutoConfigMethod;
        private static readonly Type managerType;
        private static readonly MethodInfo isPendingMethod;
        private static readonly MethodInfo resetTransientWorkMethod;
        private static readonly PropertyInfo selectedVisiblePawnsProp;
        private static readonly MethodInfo clearSelectionMethod;
        private static readonly ConstructorInfo historyWindowCtor;
        private static readonly MethodInfo getAllPresetsMethod;
        private static readonly FieldInfo promptPresetNameField;

        /// <summary>DrawTriggerSettings' event row, in draw order: (label key, DirectorSettings bool field).</summary>
        internal static readonly List<KeyValuePair<string, FieldInfo>> EventTriggers = new List<KeyValuePair<string, FieldInfo>>();

        private static readonly bool ready;

        static PersonaDirectorAutoEvolveCompat()
        {
            var surface = new ReflectionSurface(Context);

            WindowType = surface.Type("RimPersonaDirector.Window_AutoEvolveConfig");
            Type settingsType = surface.Type("RimPersonaDirector.DirectorSettings");
            Type rosterType = surface.Type("RimPersonaDirector.PawnRosterList");
            Type configType = surface.Type("RimPersonaDirector.AutoEvolveConfig");
            Type categoryType = surface.Type("RimPersonaDirector.AutoGenCategory");
            Type catalogType = surface.Type("RimPersonaDirector.AutoGenCategoryCatalog");
            Type gateType = surface.Type("RimPersonaDirector.DirectorFeatureGate");
            Type historyType = surface.Type("RimPersonaDirector.Window_PersonaHistory");
            Type promptApiType = surface.Type("RimTalk.API.RimTalkPromptAPI");
            Type promptPresetType = surface.Type("RimTalk.Prompt.PromptPreset");
            worldComponentType = surface.Type("RimPersonaDirector.DirectorWorldComponent");
            managerType = surface.Type("RimPersonaDirector.DirectorAutoEvolveManager");

            rosterField = surface.Field(WindowType, "_roster");
            batchIntervalBufferField = surface.Field(WindowType, "_batchIntervalBuffer");
            historyLimitBufferField = surface.Field(WindowType, "_historyLimitBuffer");
            changedField = surface.Field(WindowType, "_changed");
            setModeMethod = surface.Method(WindowType, "SetMode");
            setNotifyMethod = surface.Method(WindowType, "SetNotify");
            setSpeedMethod = surface.Method(WindowType, "SetSpeed");
            setAdvancedPresetMethod = surface.Method(WindowType, "SetAdvancedPreset");
            applySelectedEnabledMethod = surface.Method(WindowType, "ApplySelectedEnabled");
            applySelectedIntervalMethod = surface.Method(WindowType, "ApplySelectedInterval");
            getSpeedLabelMethod = surface.Method(WindowType, "GetSpeedLabel");

            globalEnabledField = surface.Field(settingsType, "globalAutoEvolveEnabled");
            defaultEnabledField = surface.Field(settingsType, "autoEvolveDefaultEnabled");
            autoNotifyField = surface.Field(settingsType, "autoNotify");
            speedProtectionField = surface.Field(settingsType, "speedProtection");
            autoModeField = surface.Field(settingsType, "autoMode");
            autoEvolveNotesField = surface.Field(settingsType, "autoEvolveNotes");
            evolvePresetField = surface.Field(settingsType, "rimTalkPreset_Evolve");
            autoGenCategoriesField = surface.Field(settingsType, "autoGenCategories");
            ensureAutoGenCategoriesMethod = surface.Method(settingsType, "EnsureAutoGenCategories", Type.EmptyTypes);
            orderedCategoryIdsField = surface.Field(catalogType, "OrderedIds");
            onlyOnRoleChangeField = surface.Field(categoryType, "onlyOnRoleChange");

            EventTriggers.Add(new KeyValuePair<string, FieldInfo>("RPD_AutoEvolve_EventMarriage", surface.Field(settingsType, "autoEvolveOnMarriage")));
            EventTriggers.Add(new KeyValuePair<string, FieldInfo>("RPD_AutoEvolve_EventBreakup", surface.Field(settingsType, "autoEvolveOnBreakup")));
            EventTriggers.Add(new KeyValuePair<string, FieldInfo>("RPD_AutoEvolve_EventBirth", surface.Field(settingsType, "autoEvolveOnBirth")));
            EventTriggers.Add(new KeyValuePair<string, FieldInfo>("RPD_AutoEvolve_EventDirectFamilyDeath", surface.Field(settingsType, "autoEvolveOnDirectFamilyDeath")));
            EventTriggers.Add(new KeyValuePair<string, FieldInfo>("RPD_AutoEvolve_EventTraitAdded", surface.Field(settingsType, "autoEvolveOnTraitAdded")));

            configEnabledField = surface.Field(configType, "enabled");
            configNextUpdateTickField = surface.Field(configType, "nextUpdateTick");
            configDaysBufferField = surface.Field(configType, "daysBuffer");
            configTimedUpdatesProp = surface.Property(configType, "TimedUpdatesEnabled");

            getAutoConfigMethod = surface.Method(worldComponentType, "GetAutoConfig", new[] { typeof(Pawn) });
            isPendingMethod = surface.Method(managerType, "IsPending", new[] { typeof(Pawn) });
            resetTransientWorkMethod = surface.Method(gateType, "ResetTransientWork", Type.EmptyTypes);
            selectedVisiblePawnsProp = surface.Property(rosterType, "SelectedVisiblePawns");
            clearSelectionMethod = surface.Method(rosterType, "ClearSelection", Type.EmptyTypes);
            historyWindowCtor = surface.Constructor(historyType, new[] { typeof(Pawn) });
            getAllPresetsMethod = surface.Method(promptApiType, "GetAllPresets", Type.EmptyTypes);
            promptPresetNameField = surface.Field(promptPresetType, "Name");

            ready = surface.Ready;
        }

        internal static bool Ready
        {
            get { return ready; }
        }

        internal static object Roster(Window window)
        {
            return rosterField.GetValue(window);
        }

        // Window-level controls.

        internal static string BatchIntervalBuffer(Window window)
        {
            return batchIntervalBufferField.GetValue(window) as string ?? "";
        }

        /// <summary>MUTATION-C: mirrors DrawBatchToolbar's TextField return-assign into the private buffer; Apply parses it.</summary>
        internal static void SetBatchIntervalBuffer(Window window, string value)
        {
            batchIntervalBufferField.SetValue(window, value ?? "");  // MUTATION-C: see summary.
        }

        internal static string HistoryLimitBuffer(Window window)
        {
            return historyLimitBufferField.GetValue(window) as string ?? "";
        }

        /// <summary>MUTATION-C: mirrors DrawBatchToolbar's TextField return-assign; the window's own PostClose parses, validates and saves it.</summary>
        internal static void SetHistoryLimitBuffer(Window window, string value)
        {
            historyLimitBufferField.SetValue(window, value ?? "");  // MUTATION-C: see summary.
        }

        internal static List<Pawn> SelectedVisiblePawns(object roster)
        {
            return roster == null ? new List<Pawn>() : selectedVisiblePawnsProp.GetValue(roster) as List<Pawn> ?? new List<Pawn>();
        }

        /// <summary>Vehicle A: the Clear selection button's own click body.</summary>
        internal static void ClearSelection(object roster)
        {
            clearSelectionMethod.Invoke(roster, null);
        }

        internal static object WorldComponent()
        {
            return Find.World?.GetComponent(worldComponentType);
        }

        /// <summary>Vehicle A: the window's own Enable/Disable button body, which is also the row checkbox's body for one pawn.</summary>
        internal static void ApplyEnabled(Window window, IEnumerable<Pawn> pawns, bool enabled)
        {
            applySelectedEnabledMethod.Invoke(window, new object[] { pawns, WorldComponent(), enabled });
        }

        /// <summary>Vehicle A: the window's own Apply button body.</summary>
        internal static void ApplyInterval(Window window, IEnumerable<Pawn> pawns, int days)
        {
            applySelectedIntervalMethod.Invoke(window, new object[] { pawns, WorldComponent(), days });
        }

        internal static void OpenHistory(Pawn pawn)
        {
            Find.WindowStack.Add((Window)historyWindowCtor.Invoke(new object[] { pawn }));
        }

        // DirectorSettings, read live; the setters below mirror the window's inline bodies.

        internal static bool GlobalEnabled()
        {
            return (bool)globalEnabledField.GetValue(PersonaDirectorCompat.Settings());
        }

        internal static bool DefaultEnabled()
        {
            return (bool)defaultEnabledField.GetValue(PersonaDirectorCompat.Settings());
        }

        /// <summary>MUTATION-C: mirrors DrawGlobalSettings' Enable Auto-Evolve checkbox body (field, `_changed`, ResetTransientWork); an inline CheckboxLabeled return-assign with no setter.</summary>
        internal static void SetGlobalEnabled(Window window, bool value)
        {
            globalEnabledField.SetValue(PersonaDirectorCompat.Settings(), value);  // MUTATION-C: see summary.
            MarkChanged(window, true);
        }

        /// <summary>MUTATION-C: mirrors DrawGlobalSettings' default-enabled checkbox body (field, `_changed`, no reset); an inline CheckboxLabeled return-assign with no setter.</summary>
        internal static void SetDefaultEnabled(Window window, bool value)
        {
            defaultEnabledField.SetValue(PersonaDirectorCompat.Settings(), value);  // MUTATION-C: see summary.
            MarkChanged(window, false);
        }

        /// <summary>MUTATION-C: the `_changed = true` (and ResetTransientWork where the window calls it) every inline settings checkbox ends with.</summary>
        private static void MarkChanged(Window window, bool resetTransientWork)
        {
            changedField.SetValue(window, true);  // MUTATION-C: see summary.
            if (resetTransientWork)
            {
                resetTransientWorkMethod.Invoke(null, null);
            }
        }

        internal static int NotifyOrdinal()
        {
            return Convert.ToInt32(autoNotifyField.GetValue(PersonaDirectorCompat.Settings()));
        }

        internal static int SpeedOrdinal()
        {
            return Convert.ToInt32(speedProtectionField.GetValue(PersonaDirectorCompat.Settings()));
        }

        internal static int ModeOrdinal()
        {
            return Convert.ToInt32(autoModeField.GetValue(PersonaDirectorCompat.Settings()));
        }

        /// <summary>The speed button's own label.</summary>
        internal static string SpeedLabel(int ordinal)
        {
            return getSpeedLabelMethod.Invoke(null, new[] { Enum.ToObject(speedProtectionField.FieldType, ordinal) }) as string ?? "";
        }

        /// <summary>Vehicle A: the window's own SetNotify, which the Notify menu's options call.</summary>
        internal static void SetNotify(Window window, int ordinal)
        {
            setNotifyMethod.Invoke(window, new[] { Enum.ToObject(autoNotifyField.FieldType, ordinal) });
        }

        /// <summary>Vehicle A: the window's own SetSpeed, which the speed menu's options call.</summary>
        internal static void SetSpeed(Window window, int ordinal)
        {
            setSpeedMethod.Invoke(window, new[] { Enum.ToObject(speedProtectionField.FieldType, ordinal) });
        }

        /// <summary>Vehicle A: the window's own SetMode, which the Mode menu's options call.</summary>
        internal static void SetMode(Window window, int ordinal)
        {
            setModeMethod.Invoke(window, new[] { Enum.ToObject(autoModeField.FieldType, ordinal) });
        }

        internal static string EvolvePreset()
        {
            return evolvePresetField.GetValue(PersonaDirectorCompat.Settings()) as string ?? "";
        }

        /// <summary>Vehicle A: the window's own SetAdvancedPreset, which the preset menu's options call.</summary>
        internal static void SetAdvancedPreset(Window window, string presetName)
        {
            setAdvancedPresetMethod.Invoke(window, new object[] { presetName });
        }

        /// <summary>RimTalk's preset names in its own order, skipping unnamed ones as the preset menu does.</summary>
        internal static List<string> RimTalkPresetNames()
        {
            var names = new List<string>();
            try
            {
                if (getAllPresetsMethod.Invoke(null, null) is IEnumerable presets)
                {
                    foreach (object preset in presets)
                    {
                        string name = preset == null ? null : promptPresetNameField.GetValue(preset) as string;
                        if (!string.IsNullOrEmpty(name))
                        {
                            names.Add(name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError(Context, ex);
            }
            return names;
        }

        internal static string Notes()
        {
            return autoEvolveNotesField.GetValue(PersonaDirectorCompat.Settings()) as string ?? "";
        }

        /// <summary>MUTATION-C: mirrors DrawNotes' TextArea return-assign; PostClose diffs it against the opening text to decide on a save.</summary>
        internal static void SetNotes(string value)
        {
            autoEvolveNotesField.SetValue(PersonaDirectorCompat.Settings(), value ?? "");  // MUTATION-C: see summary.
        }

        // Triggers.

        /// <summary>The role-trigger category ids in draw order, after the window's own EnsureAutoGenCategories.</summary>
        internal static string[] RoleCategoryIds()
        {
            ensureAutoGenCategoriesMethod.Invoke(PersonaDirectorCompat.Settings(), null);
            return orderedCategoryIdsField.GetValue(null) as string[] ?? new string[0];
        }

        private static object Category(string id)
        {
            IDictionary categories = autoGenCategoriesField.GetValue(PersonaDirectorCompat.Settings()) as IDictionary;
            return categories != null && categories.Contains(id) ? categories[id] : null;
        }

        internal static bool RoleTrigger(string id)
        {
            object category = Category(id);
            return category != null && (bool)onlyOnRoleChangeField.GetValue(category);
        }

        /// <summary>MUTATION-C: mirrors DrawTriggerSettings' role checkbox body (onlyOnRoleChange, `_changed`, ResetTransientWork); a bare checkbox beside a ButtonInvisible label.</summary>
        internal static void SetRoleTrigger(Window window, string id, bool value)
        {
            object category = Category(id);
            if (category == null) return;
            onlyOnRoleChangeField.SetValue(category, value);  // MUTATION-C: see summary.
            MarkChanged(window, true);
        }

        internal static bool EventTrigger(FieldInfo field)
        {
            return (bool)field.GetValue(PersonaDirectorCompat.Settings());
        }

        /// <summary>MUTATION-C: mirrors DrawTriggerCheckbox's body (field, `_changed`, ResetTransientWork); a bare checkbox beside a ButtonInvisible label.</summary>
        internal static void SetEventTrigger(Window window, FieldInfo field, bool value)
        {
            field.SetValue(PersonaDirectorCompat.Settings(), value);  // MUTATION-C: see summary.
            MarkChanged(window, true);
        }

        // Per-pawn rows. GetAutoConfig creates a missing entry, exactly as every row draw does.

        private static object AutoConfig(Pawn pawn)
        {
            object world = WorldComponent();
            return world == null ? null : getAutoConfigMethod.Invoke(world, new object[] { pawn });
        }

        internal static bool AutoEnabled(Pawn pawn)
        {
            object config = AutoConfig(pawn);
            return config != null && (bool)configEnabledField.GetValue(config);
        }

        internal static string DaysBuffer(Pawn pawn)
        {
            object config = AutoConfig(pawn);
            return config == null ? "" : configDaysBufferField.GetValue(config) as string ?? "0";
        }

        /// <summary>MUTATION-C: mirrors DrawPawnRow's cycle TextField return-assign into `daysBuffer`; the row's own draw then parses, normalizes and schedules it, and DrawList draws every row whether or not it is scrolled into view.</summary>
        internal static void SetDaysBuffer(Pawn pawn, string value)
        {
            object config = AutoConfig(pawn);
            if (config == null) return;
            configDaysBufferField.SetValue(config, value ?? "");  // MUTATION-C: see summary.
        }

        /// <summary>DrawPawnRow's Next cell text, the same chain of states it draws.</summary>
        internal static string NextText(Pawn pawn)
        {
            object config = AutoConfig(pawn);
            if (config == null || !(bool)configEnabledField.GetValue(config))
            {
                return "--";
            }
            object manager = Find.World?.GetComponent(managerType);
            if (manager != null && (bool)isPendingMethod.Invoke(manager, new object[] { pawn }))
            {
                return Translator.Translate("RPD_AutoEvolve_Pending");
            }
            if (!(bool)configTimedUpdatesProp.GetValue(config))
            {
                return Translator.Translate("RPD_AutoEvolve_ScheduleDisabled");
            }
            int next = (int)configNextUpdateTickField.GetValue(config);
            if (next <= 0)
            {
                return Translator.Translate("RPD_AutoEvolve_Pending");
            }
            int days = UnityEngine.Mathf.FloorToInt(Math.Max(0f, (next - GenTicks.TicksGame) / 60000f));
            return TranslatorFormattedStringExtensions.Translate("RPD_AutoEvolve_NextIn", days);
        }
    }
}
