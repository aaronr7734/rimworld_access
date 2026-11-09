using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Reflection facade for PersonaDirector (<c>RP.RimTalk.PersonaDirector</c>), a
    /// persona-authoring layer over RimTalk's persona editor. The assembly is never referenced at
    /// compile time; its <c>PersonalityData</c> tasks pass through as boxed objects.
    ///
    /// Three surfaces:
    /// 1. The F12 extras hub's three-way opener for <c>MainButtonWorker_Director</c>, mirroring
    ///    each Activate() branch as its own labeled entry, since the hub's generic per-def entry
    ///    can only replay the plain-click branch.
    /// 2. <see cref="PersonaDirectorBatchScope"/>, the bespoke table for <c>Window_BatchDirector</c>,
    ///    whose pawn list is a <c>PawnRosterList</c> with a raw-mouse selection column.
    /// 3. <see cref="PersonaDirectorAutoEvolveScope"/> for <c>Window_AutoEvolveConfig</c>, the
    ///    other roster window, on its own surface (<see cref="PersonaDirectorAutoEvolveCompat"/>).
    ///
    /// The buttons PersonaDirector adds to RimTalk's persona editor (Edit Notes, Evolve, Set Time)
    /// are plain Widgets drawn inside the editor's own draw, so the generic reader reaches them as
    /// real clicks. Every other window opts into the generic reader
    /// (<see cref="ScopeForWindow.TryRegisterGenericReaderForWindow"/>) or is already eligible by
    /// absorbing input.
    /// </summary>
    internal static class PersonaDirectorCompat
    {
        internal const string PackageId = "RP.RimTalk.PersonaDirector";

        private static readonly Type directorModType;
        private static readonly FieldInfo settingsField; // static, DirectorMod -> DirectorSettings
        private static readonly FieldInfo directorNotesField;

        private static readonly Type notesEditorType;
        private static readonly ConstructorInfo notesEditorCtor;
        private static readonly Type batchDirectorType;
        private static readonly ConstructorInfo batchDirectorCtor;
        private static readonly ConstructorInfo autoEvolveCtor;

        private static readonly bool ready;

        // Batch director

        private static readonly FieldInfo batchFiltersField; // DirectorSettings.BatchFilters
        private static readonly FieldInfo presetsField; // DirectorSettings.presets
        private static readonly FieldInfo selectedPresetIndexField; // DirectorSettings.selectedPresetIndex
        private static readonly FieldInfo promptPresetLabelField; // PromptPreset.label
        private static readonly MethodInfo buildCustomCharacterDataMethod; // (Pawn, bool, bool) -> string
        private static readonly MethodInfo buildCombinedCharacterDataMethod; // (List<Pawn>) -> string
        private static readonly MethodInfo generatePersonalityTaskMethod; // (string, string, Pawn) -> Task<PersonalityData>
        private static readonly MethodInfo generateBatchPersonaTaskMethod; // (string, Pawn) -> Task<PersonalityData>
        private static readonly MethodInfo openRimTalkDialogMethod; // (Pawn)
        private static readonly MethodInfo getCurrentPersonalityMethod; // (Pawn) -> string
        private static readonly MethodInfo openPersonaEditorPinMethod; // DirectorPersonaEditorPin.Open(Pawn)
        private static readonly PropertyInfo experimentalEnabledProp; // DirectorFeatureGate.ExperimentalEnabled

        private static readonly FieldInfo batchRosterField;
        private static readonly FieldInfo batchGenerationTasksField;
        private static readonly FieldInfo batchTaskField;
        private static readonly FieldInfo batchTaskPawnsField;
        private static readonly FieldInfo batchSendModeField;

        private static readonly PropertyInfo rosterVisiblePawnsProp;
        private static readonly PropertyInfo rosterSelectedPawnsProp;
        private static readonly FieldInfo rosterFilterKeysField;
        private static readonly FieldInfo rosterSearchTextField;
        private static readonly FieldInfo rosterSortByField;
        private static readonly FieldInfo rosterSortAscendingField;
        private static readonly MethodInfo rosterRefreshMethod;
        private static readonly MethodInfo rosterJumpToPawnMethod;
        private static readonly MethodInfo rosterGetPawnTagMethod;
        private static readonly MethodInfo rosterIsAvailableFilterMethod;
        private static readonly FieldInfo evolveTaskField;
        private static readonly FieldInfo evolvingWindowField;
        private static readonly Type importExportType;
        private static readonly MethodInfo exportToClipboardMethod;
        private static readonly MethodInfo exportToFileMethod;

        private static readonly bool batchReady;

        public static bool Ready
        {
            get { return ready && ModsConfig.IsActive(PackageId); }
        }

        static PersonaDirectorCompat()
        {
            var surface = new ReflectionSurface("PersonaDirector compat");

            directorModType = surface.Type("RimPersonaDirector.DirectorMod");
            Type settingsType = surface.Type("RimPersonaDirector.DirectorSettings");
            notesEditorType = surface.Type("RimPersonaDirector.Window_DirectorNotesEditor");

            settingsField = surface.Field(directorModType, "Settings");
            directorNotesField = surface.Field(settingsType, "directorNotes");
            notesEditorCtor = surface.Constructor(notesEditorType, Type.EmptyTypes);

            ready = surface.Ready;

            var batch = new ReflectionSurface("PersonaDirector compat (batch director)");

            batchDirectorType = batch.Type("RimPersonaDirector.Window_BatchDirector");
            Type rosterType = batch.Type("RimPersonaDirector.PawnRosterList");
            Type promptPresetType = batch.Type("RimPersonaDirector.PromptPreset");
            Type utilsType = batch.Type("RimPersonaDirector.DirectorUtils");
            Type pinType = batch.Type("RimPersonaDirector.DirectorPersonaEditorPin");
            Type featureGateType = batch.Type("RimPersonaDirector.DirectorFeatureGate");
            Type autoEvolveType = batch.Type("RimPersonaDirector.Window_AutoEvolveConfig");

            batchDirectorCtor = batch.Constructor(batchDirectorType, Type.EmptyTypes);
            autoEvolveCtor = batch.Constructor(autoEvolveType, Type.EmptyTypes);
            batchFiltersField = batch.Field(batch.Supplied("RimPersonaDirector.DirectorSettings", settingsType), "BatchFilters");
            presetsField = batch.Field(settingsType, "presets");
            selectedPresetIndexField = batch.Field(settingsType, "selectedPresetIndex");
            promptPresetLabelField = batch.Field(promptPresetType, "label");
            buildCustomCharacterDataMethod = batch.Method(utilsType, "BuildCustomCharacterData", new[] { typeof(Pawn), typeof(bool), typeof(bool) });
            buildCombinedCharacterDataMethod = batch.Method(utilsType, "BuildCombinedCharacterData", new[] { typeof(List<Pawn>) });
            generatePersonalityTaskMethod = batch.Method(utilsType, "GeneratePersonalityTask", new[] { typeof(string), typeof(string), typeof(Pawn) });
            generateBatchPersonaTaskMethod = batch.Method(utilsType, "GenerateBatchPersonaTask", new[] { typeof(string), typeof(Pawn) });
            openRimTalkDialogMethod = batch.Method(utilsType, "OpenRimTalkDialog", new[] { typeof(Pawn) });
            getCurrentPersonalityMethod = batch.Method(utilsType, "GetCurrentPersonality", new[] { typeof(Pawn) });
            openPersonaEditorPinMethod = batch.Method(pinType, "Open", new[] { typeof(Pawn) });
            experimentalEnabledProp = batch.Property(featureGateType, "ExperimentalEnabled");

            batchRosterField = batch.Field(batchDirectorType, "_roster");
            batchGenerationTasksField = batch.Field(batchDirectorType, "_generationTasks");
            batchTaskField = batch.Field(batchDirectorType, "_batchTask");
            batchTaskPawnsField = batch.Field(batchDirectorType, "_batchTaskPawns");
            batchSendModeField = batch.Field(batchDirectorType, "_batchSendMode");

            rosterVisiblePawnsProp = batch.Property(rosterType, "VisiblePawns");
            rosterSelectedPawnsProp = batch.Property(rosterType, "SelectedPawns");
            rosterFilterKeysField = batch.Field(rosterType, "_filterKeys");
            rosterSearchTextField = batch.Field(rosterType, "_searchText");
            rosterSortByField = batch.Field(rosterType, "_sortBy");
            rosterSortAscendingField = batch.Field(rosterType, "_sortAscending");
            rosterRefreshMethod = batch.Method(rosterType, "Refresh", Type.EmptyTypes);
            rosterJumpToPawnMethod = batch.Method(rosterType, "JumpToPawn", new[] { typeof(Pawn) });
            rosterGetPawnTagMethod = batch.Method(rosterType, "GetPawnTag", new[] { typeof(Pawn) });
            rosterIsAvailableFilterMethod = batch.Method(rosterType, "IsAvailableFilter", new[] { typeof(string) });
            importExportType = batch.Type("RimPersonaDirector.Window_ImportExport");
            Type featuresPatchType = batch.Type("RimPersonaDirector.Patch_PersonaEditorWindow_DirectorFeatures");
            evolveTaskField = batch.Field(featuresPatchType, "evolveTask");
            evolvingWindowField = batch.Field(featuresPatchType, "evolvingWindow");
            exportToClipboardMethod = batch.Method(importExportType, "ExportAndCopyToClipboard", Type.EmptyTypes);
            exportToFileMethod = batch.Method(importExportType, "ExportToFile", Type.EmptyTypes);

            batchReady = ready && batch.Ready;
        }

        internal static object Settings()
        {
            return settingsField.GetValue(null);
        }

        internal static string DirectorNotesText()
        {
            if (!Ready) return "";
            return (string)directorNotesField.GetValue(Settings()) ?? "";
        }

        /// <summary>MUTATION-C: directorNotes is a bare string field (DirectorSettings) with no gated setter, shared verbatim by Window_DirectorNotesEditor/Window_BatchDirector's own TextArea return-assigns.</summary>
        internal static void SetDirectorNotes(string value)
        {
            directorNotesField.SetValue(Settings(), value ?? "");  // MUTATION-C: bare string field (DirectorSettings), no gated setter.
        }

        // F12 extras hub: mirrors MainButtonWorker_Director.Activate's three mutually-exclusive
        // branches as three explicit entries, since the hub's generic per-def entry can only
        // replay plain-click.

        internal static string F12NotesLabel()
        {
            return CompatText.ResolveOrFallback("RPD_Notes_Title", "RimWorldAccess.Compat.RimTalk.PersonaDirector.F12OpenNotesFallback");
        }

        internal static string F12BatchLabel()
        {
            return CompatText.ResolveOrFallback("RPD_Batch_Title", "RimWorldAccess.Compat.RimTalk.PersonaDirector.F12OpenBatchFallback");
        }

        internal static string F12SettingsLabel()
        {
            return "RimWorldAccess.Compat.RimTalk.PersonaDirector.F12OpenSettingsFallback".Translate();
        }

        /// <summary>Mirrors MainButtonWorker_Director.Activate's else branch (toggle Window_DirectorNotesEditor).</summary>
        internal static void ToggleNotesWindow()
        {
            if (!Ready) return;
            ToggleWindow(notesEditorType, notesEditorCtor);
        }

        /// <summary>Mirrors MainButtonWorker_Director.Activate's right-click branch (toggle Window_BatchDirector).</summary>
        internal static void ToggleBatchWindow()
        {
            if (!batchReady) return;
            ToggleWindow(batchDirectorType, batchDirectorCtor);
        }

        private static void ToggleWindow(Type windowType, ConstructorInfo ctor)
        {
            try
            {
                Window existing = Find.WindowStack.Windows.FirstOrDefault(w => w.GetType() == windowType);
                if (existing != null)
                {
                    existing.Close(true);
                }
                else
                {
                    Find.WindowStack.Add((Window)ctor.Invoke(null));
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("PersonaDirector compat", ex);
            }
        }

        /// <summary>Mirrors MainButtonWorker_Director.Activate's shift-click branch (open Dialog_ModSettings).</summary>
        internal static void OpenSettings()
        {
            if (!Ready) return;
            try
            {
                Mod mod = LoadedModManager.GetMod(directorModType);
                if (mod != null)
                {
                    Find.WindowStack.Add(new Dialog_ModSettings(mod));
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("PersonaDirector compat", ex);
            }
        }

        public static void Register()
        {
            if (!Ready)
            {
                return;
            }

            ScopeForWindow.TryRegisterGenericReaderForWindow("RimPersonaDirector.Window_DirectorNotesEditor");
            ScopeForWindow.TryRegisterGenericReaderForWindow("RimPersonaDirector.Window_LibraryManager");
            ScopeForWindow.TryRegisterGenericReaderForWindow("RimPersonaDirector.Window_PersonaHistory");

            if (batchReady)
            {
                ScopeForWindow.Register(batchDirectorType, w => new PersonaDirectorBatchScope(w));
                ScopeForWindow.Register(importExportType, w => new PersonaDirectorImportExportScope(w));
            }
            else
            {
                ScopeForWindow.TryRegisterGenericReaderForWindow("RimPersonaDirector.Window_ImportExport");
            }

            if (batchReady && PersonaDirectorAutoEvolveCompat.Ready)
            {
                ScopeForWindow.Register(PersonaDirectorAutoEvolveCompat.WindowType, w => new PersonaDirectorAutoEvolveScope(w));
            }
            else
            {
                ScopeForWindow.TryRegisterGenericReaderForWindow("RimPersonaDirector.Window_AutoEvolveConfig");
            }
        }

        /// <summary>True while an Evolve started from <paramref name="editor"/> is running; the persona editor's Evolve button reads "Generating" for exactly this span.</summary>
        internal static bool IsEvolving(Window editor)
        {
            if (!batchReady || !ModsConfig.IsActive(PackageId))
            {
                return false;
            }
            return evolveTaskField.GetValue(null) != null && ReferenceEquals(evolvingWindowField.GetValue(null), editor);
        }

        /// <summary>Vehicle A: the Export button's left-click body.</summary>
        internal static void ExportToClipboard(Window window)
        {
            exportToClipboardMethod.Invoke(window, null);
        }

        /// <summary>Vehicle A: the Export button's right-click body.</summary>
        internal static void ExportToFile(Window window)
        {
            exportToFileMethod.Invoke(window, null);
        }

        // Batch director. The window keeps its pawn list, selection, sort and search in a
        // PawnRosterList; the generation tasks and send mode stay on the window.

        internal static object Roster(Window batchWindow)
        {
            return batchRosterField.GetValue(batchWindow);
        }

        internal static IEnumerable<Pawn> VisiblePawns(object roster)
        {
            return rosterVisiblePawnsProp.GetValue(roster) as IEnumerable<Pawn> ?? Enumerable.Empty<Pawn>();
        }

        internal static HashSet<Pawn> SelectedPawns(object roster)
        {
            return rosterSelectedPawnsProp.GetValue(roster) as HashSet<Pawn>;
        }

        /// <summary>Vehicle A: the roster's own Refresh, which every filter, search and sort click ends with.</summary>
        internal static void RefreshRoster(object roster)
        {
            rosterRefreshMethod.Invoke(roster, null);
        }

        /// <summary>Vehicle A: the name cell's own click handler.</summary>
        internal static void JumpToPawn(object roster, Pawn pawn)
        {
            rosterJumpToPawnMethod.Invoke(roster, new object[] { pawn });
        }

        internal static string PawnTag(Pawn pawn)
        {
            return rosterGetPawnTagMethod.Invoke(null, new object[] { pawn }) as string ?? "";
        }

        /// <summary>The roster's own filter keys, in its draw order, minus those its own IsAvailableFilter hides.</summary>
        internal static IEnumerable<string> AvailableFilterKeys(object roster)
        {
            if (!(rosterFilterKeysField.GetValue(roster) is IEnumerable<string> keys))
            {
                yield break;
            }
            foreach (string key in keys)
            {
                if ((bool)rosterIsAvailableFilterMethod.Invoke(roster, new object[] { key }))
                {
                    yield return key;
                }
            }
        }

        internal static string SearchText(object roster)
        {
            return rosterSearchTextField.GetValue(roster) as string ?? "";
        }

        /// <summary>MUTATION-C: mirrors DrawFilters' search-box return-assign (`_searchText = text; Refresh();`); a bare private field with no gated setter.</summary>
        internal static void SetSearchText(object roster, string value)
        {
            rosterSearchTextField.SetValue(roster, value ?? "");  // MUTATION-C: see summary.
            RefreshRoster(roster);
        }

        /// <summary>The roster's sort as (ordinal of its SortBy, ascending); SortBy runs Name, Race, Faction, Tag.</summary>
        internal static int SortOrdinal(object roster, out bool ascending)
        {
            ascending = (bool)rosterSortAscendingField.GetValue(roster);
            return Convert.ToInt32(rosterSortByField.GetValue(roster));
        }

        /// <summary>MUTATION-C: mirrors DrawHeaderCell's click body (`_sortBy`/`_sortAscending` writes, then Refresh); a header cell is a ButtonInvisible over a label, and both fields are bare and private.</summary>
        internal static void SetSort(object roster, int ordinal, bool ascending)
        {
            rosterSortByField.SetValue(roster, Enum.ToObject(rosterSortByField.FieldType, ordinal));  // MUTATION-C: see summary.
            rosterSortAscendingField.SetValue(roster, ascending);  // MUTATION-C: see summary.
            RefreshRoster(roster);
        }

        internal static bool BatchSendMode(Window batchWindow)
        {
            return (bool)batchSendModeField.GetValue(batchWindow);
        }

        /// <summary>MUTATION-C: mirrors the mode button's click body (`_batchSendMode = !_batchSendMode`); a bare private field with no gated setter.</summary>
        internal static void ToggleBatchSendMode(Window batchWindow)
        {
            batchSendModeField.SetValue(batchWindow, !BatchSendMode(batchWindow));  // MUTATION-C: see summary.
        }

        internal static IDictionary GenerationTasks(Window batchWindow)
        {
            return batchGenerationTasksField.GetValue(batchWindow) as IDictionary;
        }

        internal static System.Threading.Tasks.Task BatchTask(Window batchWindow)
        {
            return batchTaskField.GetValue(batchWindow) as System.Threading.Tasks.Task;
        }

        internal static List<Pawn> BatchTaskPawns(Window batchWindow)
        {
            return batchTaskPawnsField.GetValue(batchWindow) as List<Pawn>;
        }

        /// <summary>MUTATION-C: mirrors DrawGlobalActions' batch-mode branch, which assigns the bare private `_batchTaskPawns` and `_batchTask` the window's own UpdateAsyncTasks polls.</summary>
        internal static void StartBatchTask(Window batchWindow, List<Pawn> pawns)
        {
            batchTaskPawnsField.SetValue(batchWindow, pawns);  // MUTATION-C: see summary.
            object task = generateBatchPersonaTaskMethod.Invoke(null, new object[] { BuildCombinedCharacterData(pawns), pawns.FirstOrDefault() });
            batchTaskField.SetValue(batchWindow, task);  // MUTATION-C: see summary.
        }

        internal static string BuildCharacterData(Pawn pawn)
        {
            return (string)buildCustomCharacterDataMethod.Invoke(null, new object[] { pawn, false, false });
        }

        private static string BuildCombinedCharacterData(List<Pawn> pawns)
        {
            return (string)buildCombinedCharacterDataMethod.Invoke(null, new object[] { pawns });
        }

        internal static object GeneratePersonalityTask(string characterData, string labelShortCap, Pawn pawn)
        {
            return generatePersonalityTaskMethod.Invoke(null, new object[] { characterData, labelShortCap, pawn });
        }

        /// <summary>Vehicle A: the Talk button's own click body.</summary>
        internal static void OpenRimTalkDialog(Pawn target)
        {
            openRimTalkDialogMethod.Invoke(null, new object[] { target });
        }

        /// <summary>Vehicle A: the Deep Edit button's own click body.</summary>
        internal static void OpenPersonaEditorPin(Pawn pawn)
        {
            openPersonaEditorPinMethod.Invoke(null, new object[] { pawn });
        }

        /// <summary>The name cell's tooltip argument. The window computes it for every row on every draw, so reading it here adds nothing the mod does not already do.</summary>
        internal static string CurrentPersonality(Pawn pawn)
        {
            return getCurrentPersonalityMethod.Invoke(null, new object[] { pawn }) as string ?? "";
        }

        internal static bool ExperimentalEnabled()
        {
            return experimentalEnabledProp.GetValue(null) is bool enabled && enabled;
        }

        /// <summary>Vehicle A: the Auto Evolution button's own click body, closing the batch director for the config window.</summary>
        internal static void SwitchToAutoEvolve(Window batchWindow)
        {
            batchWindow.Close(true);
            Find.WindowStack.Add((Window)autoEvolveCtor.Invoke(null));
        }

        /// <summary>Vehicle A: the auto evolution window's Manual Generation button body, closing it for the batch director.</summary>
        internal static void SwitchToBatch(Window autoEvolveWindow)
        {
            autoEvolveWindow.Close(true);
            Find.WindowStack.Add((Window)batchDirectorCtor.Invoke(null));
        }

        internal static IDictionary BatchFilters()
        {
            return batchFiltersField.GetValue(Settings()) as IDictionary;
        }

        internal static IList Presets()
        {
            return presetsField.GetValue(Settings()) as IList;
        }

        /// <summary>MUTATION-C: selectedPresetIndex is a bare int field (DirectorSettings) with no gated setter; mirrors the prompt-slot FloatMenu's own return-assign exactly.</summary>
        internal static int SelectedPresetIndex
        {
            get { return (int)selectedPresetIndexField.GetValue(Settings()); }
            set { selectedPresetIndexField.SetValue(Settings(), value); }  // MUTATION-C: bare int field (DirectorSettings), no gated setter.
        }

        internal static string PromptPresetLabel(object preset)
        {
            return (string)promptPresetLabelField.GetValue(preset);
        }
    }
}
