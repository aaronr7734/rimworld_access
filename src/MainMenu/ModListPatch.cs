using HarmonyLib;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Page_ModsConfig lifecycle and input handling.
    /// Uses Pattern 1 (DoWindowContents_Prefix) for keyboard input.
    /// Patches OnCancelKeyPressed to prevent Escape from closing the page
    /// when we're in detail view (Window.InnerWindowOnGUI checks Cancel
    /// independently of DoWindowContents, so Event.current.Use() alone
    /// is not sufficient).
    /// </summary>
    [HarmonyPatch]
    public static class ModListPatch
    {
        /// <summary>
        /// Patch PreOpen to initialize our state when the mod list opens.
        /// </summary>
        [HarmonyPatch(typeof(Page_ModsConfig), "PreOpen")]
        [HarmonyPostfix]
        public static void PreOpen_Postfix(Page_ModsConfig __instance)
        {
            ModListState.Open(__instance);
        }

        /// <summary>
        /// Patch OnCloseRequest to clean up our state only when the page is actually closing.
        /// OnCloseRequest returns false when showing the unsaved changes dialog — in that case
        /// we must NOT close ModListState or it becomes unresponsive after "Go back".
        /// </summary>
        [HarmonyPatch(typeof(Page_ModsConfig), "OnCloseRequest")]
        [HarmonyPostfix]
        public static void OnCloseRequest_Postfix(bool __result)
        {
            if (__result)
            {
                ModListState.Close();
            }
        }

        /// <summary>
        /// Patch DoWindowContents to intercept keyboard input before the game processes it.
        /// Yields to WindowlessDialogState when a dialog is active (e.g., unsaved changes
        /// confirmation intercepted by DialogInterceptionPatch).
        /// </summary>
        [HarmonyPatch(typeof(Page_ModsConfig), "DoWindowContents")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        public static void DoWindowContents_Prefix(Page_ModsConfig __instance, Rect rect)
        {
            if (!ModListState.IsActive) return;
            if (Event.current.type != EventType.KeyDown) return;

            // When a dialog is active (e.g., unsaved changes Dialog_MessageBox intercepted
            // by DialogInterceptionPatch), forward input to WindowlessDialogState and consume
            // the event. We must consume it here because Page_ModsConfig.DoWindowContents has
            // its own Up/Down arrow handling (lines 255-266) that would swallow the keys before
            // WindowlessDialogState gets them via WindowlessDialogInputPatch.
            if (WindowlessDialogState.IsActive)
            {
                if (WindowlessDialogState.HandleInput(Event.current))
                {
                    Event.current.Use();
                }
                return;
            }

            KeyCode key = Event.current.keyCode;
            if (key == KeyCode.None) return;

            bool shift = Event.current.shift;
            bool ctrl = Event.current.control;
            bool alt = KeyboardHelper.IsAltHeld;

            if (ModListState.HandleInput(key, shift, ctrl, alt))
            {
                Event.current.Use();
            }
        }

        /// <summary>
        /// Block the game's Escape/Cancel handling when our detail view or typeahead
        /// search is active. Window.InnerWindowOnGUI checks KeyBindingDefOf.Cancel
        /// independently AFTER DoWindowContents, so Event.current.Use() alone cannot
        /// prevent OnCancelKeyPressed from firing.
        /// Pattern used by: WorldParamsPatch, ScenarioBuilderPatch, FactionLandingPatch.
        /// </summary>
        [HarmonyPatch(typeof(Page), "OnCancelKeyPressed")]
        [HarmonyPrefix]
        public static bool OnCancelKeyPressed_Prefix(Page __instance)
        {
            if (__instance is Page_ModsConfig && ModListState.IsActive)
            {
                // Block when in detail view — our HandleDetailInput handles Escape
                if (ModListState.IsInDetailView)
                    return false;

                // Block when WindowlessDialogState is active — dialog handles its own Escape
                if (WindowlessDialogState.IsActive)
                    return false;
            }
            return true;
        }
    }
}
