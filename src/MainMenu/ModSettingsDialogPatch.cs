using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Dialog_ModSettings. Announces dialog open/close, gates the
    /// widget-capture patches (ModSettingsWidgetPatches.cs) for exactly the duration of
    /// DoWindowContents, and routes keyboard input to ModSettingsMenuState - the
    /// windowless menu that makes each mod's settings widgets keyboard/screen-reader
    /// accessible by shadowing them as they're drawn.
    /// </summary>
    [HarmonyPatch]
    public static class ModSettingsDialogPatch
    {
        // Cache reflection for the mod field
        private static FieldInfo modField;

        static ModSettingsDialogPatch()
        {
            modField = typeof(Dialog_ModSettings).GetField("mod", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        /// <summary>
        /// Capture-gating Prefix. Runs first (Priority.First) so ModSettingsCaptureState.Capturing
        /// is already true before the original method - and therefore before the mod's own
        /// Mod.DoSettingsWindowContents call and every widget inside it - executes.
        /// </summary>
        [HarmonyPatch(typeof(Dialog_ModSettings), "DoWindowContents")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static void CaptureGate_Prefix(Dialog_ModSettings __instance)
        {
            ModSettingsCaptureState.BeginFrame(__instance);
        }

        /// <summary>
        /// Capture-gating Finalizer. Always runs, even if the mod's own settings UI threw,
        /// so Capturing never gets stuck true. Hands the frame's capture off to
        /// ModSettingsMenuState and ages out any pending activation that never found its target.
        /// </summary>
        [HarmonyPatch(typeof(Dialog_ModSettings), "DoWindowContents")]
        [HarmonyFinalizer]
        public static System.Exception CaptureGate_Finalizer(System.Exception __exception)
        {
            ModSettingsCaptureState.EndFrame();
            return __exception;
        }

        /// <summary>
        /// Intercepts keyboard input before the game/mod widgets process it - mirrors
        /// ModListPatch.DoWindowContents_Prefix. Only active once ModSettingsMenuState has
        /// opened (i.e. after the first render, see DoWindowContents_Postfix below).
        /// </summary>
        [HarmonyPatch(typeof(Dialog_ModSettings), "DoWindowContents")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        public static void Input_Prefix(Rect inRect)
        {
            // Defensive: our input handling must never throw into the game's window rendering
            // (RimWorld logs any DoWindowContents exception as "Exception filling window").
            try
            {
                if (!ModSettingsMenuState.IsActive) return;
                if (Event.current.type != EventType.KeyDown) return;

                KeyCode key = Event.current.keyCode;
                if (key == KeyCode.None) return;

                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = KeyboardHelper.IsAltHeld;

                if (ModSettingsMenuState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                }
            }
            catch (System.Exception e)
            {
                Log.Warning($"[RimWorld Access] ModSettings input handling error: {e}");
            }
        }

        /// <summary>
        /// Opens ModSettingsMenuState once, bound to the first Dialog_ModSettings instance that
        /// actually draws capturable widgets. This Postfix runs after the original
        /// DoWindowContents (and every widget in it) has executed, so the frame's capture
        /// snapshot is complete. Two guards keep it robust in heavily-modded setups:
        ///   - only open when no menu is already active (so a second on-screen Dialog_ModSettings
        ///     - e.g. a Mod Settings Framework wrapper - can't re-open/rebind), and
        ///   - only open when the frame captured at least one widget (so the wrapper dialog,
        ///     which draws nothing we recognise, is skipped rather than opening an empty menu).
        /// A mod whose settings are drawn entirely with controls we don't cover simply won't
        /// open a menu (nothing to navigate).
        /// </summary>
        [HarmonyPatch(typeof(Dialog_ModSettings), "DoWindowContents")]
        [HarmonyPostfix]
        public static void DoWindowContents_Postfix(Dialog_ModSettings __instance, Rect inRect)
        {
            try
            {
                if (ModSettingsMenuState.IsActive) return;

                var snapshot = ModSettingsCaptureState.CurrentFrameSnapshot;
                if (snapshot == null || snapshot.Count == 0) return;

                var mod = modField?.GetValue(__instance) as Mod;
                string modName;
                if (mod != null)
                {
                    modName = mod.SettingsCategory();
                    if (modName.NullOrEmpty())
                        modName = mod.Content?.Name ?? "RimWorldAccess.ModSettings.UnknownMod".Translate().ToString();
                }
                else
                {
                    modName = "RimWorldAccess.ModSettings.UnknownMod".Translate().ToString();
                }

                ModSettingsMenuState.Open(__instance, modName, snapshot);
            }
            catch (System.Exception e)
            {
                Log.Warning($"[RimWorld Access] ModSettings menu open error: {e}");
            }
        }

        /// <summary>
        /// Patch the base Window.PostClose to detect when our bound Dialog_ModSettings closes.
        /// Only reacts to the exact instance the menu bound to, so closing an unrelated
        /// Dialog_ModSettings (e.g. a framework wrapper) doesn't tear down our menu.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostClose")]
        [HarmonyPostfix]
        public static void Window_PostClose_Postfix(Window __instance)
        {
            if (__instance is Dialog_ModSettings && ReferenceEquals(__instance, ModSettingsMenuState.CurrentDialog))
            {
                ModSettingsMenuState.Close();
                ModSettingsCaptureState.Reset();
                TolkHelper.Speak("RimWorldAccess.ModSettings.Closed".Loc());
            }
        }

        /// <summary>
        /// Blocks the game's own Escape/Cancel handling while our menu is active - per the
        /// project's documented Window.OnCancelKeyPressed pattern (root CLAUDE.md), since
        /// Event.current.Use() alone cannot stop WindowStack from calling OnCancelKeyPressed
        /// independently of DoWindowContents. Our own Input_Prefix already handles Escape by
        /// closing the dialog itself (silently, via Find.WindowStack.TryRemove), so letting
        /// vanilla's handling through as well would double-close (and play the game's own
        /// close sound) an already-removed window.
        /// </summary>
        [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
        [HarmonyPrefix]
        public static bool Window_OnCancelKeyPressed_Prefix(Window __instance)
        {
            if (__instance is Dialog_ModSettings && ModSettingsMenuState.IsActive)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Twin of the OnCancelKeyPressed block, for Enter. WindowStack calls
        /// OnAcceptKeyPressed independently of DoWindowContents, and Dialog_ModSettings'
        /// default accept handling saves and closes the window - so without this, pressing
        /// Enter to activate a control in our menu would instead close the whole dialog
        /// (our Input_Prefix already consumed the KeyDown to run the activation, but that
        /// does not stop the WindowStack's separate Accept call). Block it while our menu
        /// is active; our menu never uses Enter to accept/close (Escape closes).
        /// </summary>
        [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
        [HarmonyPrefix]
        public static bool Window_OnAcceptKeyPressed_Prefix(Window __instance)
        {
            if (__instance is Dialog_ModSettings && ModSettingsMenuState.IsActive)
            {
                return false;
            }
            return true;
        }
    }
}
