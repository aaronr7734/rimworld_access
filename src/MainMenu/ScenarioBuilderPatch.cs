using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for the Scenario Builder (Page_ScenarioEditor).
    /// Initializes and closes the ScenarioBuilderState when the editor opens/closes.
    /// </summary>
    [HarmonyPatch(typeof(Page_ScenarioEditor))]
    public static class ScenarioBuilderPatch
    {
        /// <summary>
        /// Initialize state when the scenario editor opens.
        /// Page_ScenarioEditor overrides PreOpen, so we patch that.
        /// </summary>
        [HarmonyPatch("PreOpen")]
        [HarmonyPostfix]
        public static void PreOpen_Postfix(Page_ScenarioEditor __instance)
        {
            try
            {
                // Get the current scenario from the editor
                Scenario scenario = (Scenario)AccessTools.Field(typeof(Page_ScenarioEditor), "curScen").GetValue(__instance);

                // Initialize the scenario builder state
                ScenarioBuilderState.Open(scenario, __instance);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in ScenarioBuilderPatch PreOpen: {ex}");
            }
        }
    }

    /// <summary>
    /// Patch Page.DoNext to block page advancement when our scenario builder states are active.
    /// CRITICAL: This is the most direct way to block advancement because:
    /// - OnAcceptKeyPressed calls DoNext()
    /// - DoBottomButtons ALSO checks KeyBindingDefOf.Accept.KeyDownEvent and calls DoNext()
    /// - Patching DoNext() catches BOTH paths
    /// </summary>
    [HarmonyPatch(typeof(Page))]
    [HarmonyPatch("DoNext")]
    public static class ScenarioBuilderDoNextPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Page __instance)
        {
            // Only intercept for Page_ScenarioEditor
            if (__instance is Page_ScenarioEditor)
            {
                // Block page advancement when ANY of our scenario builder states are active
                if (ScenarioBuilderState.IsActive ||
                    ScenarioBuilderPartEditState.IsActive ||
                    ScenarioBuilderAddPartState.IsActive ||
                    WindowlessScenarioLoadState.IsActive ||
                    WindowlessScenarioSaveState.IsActive ||
                    WindowlessScenarioDeleteConfirmState.IsActive)
                {
                    return false; // Block DoNext() - stay on scenario builder
                }
            }

            // Handle Page_SelectScenario - block DoNext when windowless dialog is active or was just closed
            // This prevents Enter in delete confirmation from advancing to storyteller selection
            if (__instance is Page_SelectScenario)
            {
                if (WindowlessDialogState.IsActive || WindowlessDialogState.WasClosedThisFrame)
                {
                    return false; // Block DoNext()
                }
            }

            return true; // Let original method run (advances to next page normally)
        }
    }

    /// <summary>
    /// Patch Window.PostClose to detect when Page_ScenarioEditor closes.
    /// We patch Window because Page_ScenarioEditor doesn't override PostClose.
    /// </summary>
    [HarmonyPatch(typeof(Window))]
    [HarmonyPatch("PostClose")]
    public static class ScenarioBuilderClosePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Window __instance)
        {
            try
            {
                // Only handle Page_ScenarioEditor
                if (__instance is Page_ScenarioEditor && ScenarioBuilderState.IsActive)
                {
                    ScenarioBuilderState.Close();
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in ScenarioBuilderClosePatch: {ex}");
            }
        }
    }

    /// <summary>
    /// Patch Window.OnCancelKeyPressed to block Escape key from closing the editor.
    /// We want Escape to be handled by our state (to close overlays first).
    /// Also shows confirmation when there are unsaved changes.
    /// </summary>
    [HarmonyPatch(typeof(Window))]
    [HarmonyPatch("OnCancelKeyPressed")]
    public static class ScenarioBuilderCancelKeyPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Window __instance)
        {
            // Only intercept for Page_ScenarioEditor
            if (__instance is Page_ScenarioEditor)
            {
                // Block the game's Cancel handling when typeahead search is active
                // (Escape should clear search first, not close the editor)
                if (ScenarioBuilderState.IsActive && ScenarioBuilderState.PartsHasActiveSearch)
                {
                    return false; // Let UnifiedKeyboardPatch handle clearing the search
                }

                // Block the game's Cancel handling when our overlay states are active
                if (ScenarioBuilderPartEditState.IsActive ||
                    ScenarioBuilderAddPartState.IsActive ||
                    WindowlessScenarioLoadState.IsActive ||
                    WindowlessScenarioSaveState.IsActive ||
                    WindowlessScenarioDeleteConfirmState.IsActive)
                {
                    return false; // Skip original method - let our overlay handle the Escape
                }

                // NOTE: We don't show unsaved changes dialog here because OnCancelKeyPressed
                // never actually runs for Pages (closeOnCancel = false). The dialog is shown
                // in ScenarioBuilderDoBackPatch which handles DoBack() called from DoBottomButtons.
            }

            return true; // Let original method run
        }

    }

    /// <summary>
    /// Blocks the Page.CanDoBack() method when our overlay states are active.
    /// This prevents Page.DoBottomButtons() from calling DoBack() when Escape is pressed.
    /// </summary>
    [HarmonyPatch(typeof(Page))]
    [HarmonyPatch("CanDoBack")]
    public static class ScenarioBuilderCanDoBackPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Page __instance, ref bool __result)
        {
            if (__instance is Page_ScenarioEditor)
            {
                // Block going back when windowless dialog is active or just closed
                // This prevents DoBack() from being triggered by the same Escape that closed the dialog
                if (WindowlessDialogState.IsActive || WindowlessDialogState.WasClosedThisFrame)
                {
                    __result = false;
                    return false;
                }

                // Block going back when any overlay state is active
                if (ScenarioBuilderAddPartState.IsActive ||
                    ScenarioBuilderPartEditState.IsActive ||
                    WindowlessScenarioLoadState.IsActive ||
                    WindowlessScenarioSaveState.IsActive ||
                    WindowlessScenarioDeleteConfirmState.IsActive)
                {
                    __result = false;
                    return false;
                }

                // Block going back when ScenarioBuilderState has active typeahead
                if (ScenarioBuilderState.IsActive && ScenarioBuilderState.PartsHasActiveSearch)
                {
                    __result = false;
                    return false;
                }

                // NOTE: Do NOT block here when dirty - let DoBack() be called so it can show the dialog
            }

            // Handle Page_SelectScenario - block going back when windowless dialog is active or just closed
            // This prevents Escape in delete confirmation from returning to main menu
            if (__instance is Page_SelectScenario)
            {
                if (WindowlessDialogState.IsActive || WindowlessDialogState.WasClosedThisFrame)
                {
                    __result = false;
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Patches Page.DoBack() to show unsaved changes dialog for scenario builder.
    /// CRITICAL: This is the correct place to intercept Escape key for Page windows.
    /// OnCancelKeyPressed is NEVER called for Pages because closeOnCancel = false.
    /// DoBack() is called by DoBottomButtons() when Escape is pressed and CanDoBack() returns true.
    /// Since we block CanDoBack() when dirty, we also need to handle the dialog here for when
    /// users use the Back button directly.
    /// </summary>
    [HarmonyPatch(typeof(Page))]
    [HarmonyPatch("DoBack")]
    public static class ScenarioBuilderDoBackPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Page __instance)
        {
            // Only intercept for Page_ScenarioEditor
            if (__instance is Page_ScenarioEditor)
            {
                // Skip if a dialog was just closed this frame (prevent Escape from re-triggering)
                if (WindowlessDialogState.WasClosedThisFrame)
                {
                    return false; // Block - dialog was just dismissed, don't show again
                }

                // Check for unsaved changes
                if (ScenarioBuilderState.IsActive && ScenarioBuilderState.IsDirty())
                {
                    ShowUnsavedChangesDialog(__instance);
                    return false; // Block original method
                }
            }
            return true; // Let original method run
        }

        /// <summary>
        /// Shows a confirmation dialog for unsaved changes with Save, Discard, and Cancel buttons.
        /// </summary>
        private static void ShowUnsavedChangesDialog(Window editorWindow)
        {
            // Get the prev page so we can navigate back properly after discard
            var prevPage = AccessTools.Field(typeof(Page), "prev")?.GetValue(editorWindow) as Page;

            var dialog = new Dialog_MessageBox(
                "RimWorldAccess.ScenarioBuilder.UnsavedChangesPrompt".Translate(),
                "RimWorldAccess.ScenarioBuilder.UnsavedSave".Translate(),
                () =>
                {
                    // Save then close
                    WindowlessScenarioSaveState.Open(ScenarioBuilderState.CurrentScenario, () =>
                    {
                        ScenarioBuilderState.ResetDirty();
                        ScenarioBuilderState.Close();
                        editorWindow?.Close();
                    });
                },
                "RimWorldAccess.ScenarioBuilder.UnsavedDiscard".Translate(),
                () =>
                {
                    // Discard changes and navigate back to scenario list
                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.ChangesDiscarded".Loc());
                    ScenarioBuilderState.ResetDirty();
                    ScenarioBuilderState.Close();
                    // Navigate back to previous page (scenario list) instead of just closing
                    if (prevPage != null)
                    {
                        Find.WindowStack.Add(prevPage);
                    }
                    editorWindow?.Close();
                },
                "RimWorldAccess.ScenarioBuilder.UnsavedChangesTitle".Translate(),
                false
            );

            // Add a third button for Cancel
            dialog.buttonCText = "RimWorldAccess.ScenarioBuilder.UnsavedCancel".Translate();
            dialog.buttonCAction = () =>
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.ContinuingToEdit".Loc());
            };
            dialog.buttonCClose = true;

            Find.WindowStack.Add(dialog);
        }
    }
}
