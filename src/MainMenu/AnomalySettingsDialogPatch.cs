using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Lifecycle patches for Dialog_AnomalySettings — wires up the keyboard navigation state
    /// when the dialog opens, tears it down on close, and blocks the game's default Cancel
    /// handling so our state can fully own the keyboard.
    /// </summary>
    public static class AnomalySettingsDialogPatch
    {
        [HarmonyPatch(typeof(Window), "PostOpen")]
        public static class Window_PostOpen_AnomalySettings_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_AnomalySettings dialog)) return;
                try
                {
                    AnomalySettingsDialogState.Open(dialog);
                }
                catch (Exception ex)
                {
                    Log.Error($"[AnomalySettingsDialogPatch] PostOpen failed: {ex.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_AnomalySettings_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_AnomalySettings)) return;
                try
                {
                    if (AnomalySettingsDialogState.IsActive)
                    {
                        // Capture the close mode BEFORE Close() resets it.
                        bool wasAccept = AnomalySettingsDialogState.wasAcceptClose;
                        AnomalySettingsDialogState.Close();

                        if (wasAccept)
                        {
                            // On Accept, route the user back to the Storyteller row so Enter
                            // advances the wizard naturally. ReturnToStorytellerMode speaks the
                            // storyteller info, so the user knows where focus landed.
                            TolkHelper.SpeakData("RimWorldAccess.AnomalySettings.Saved".Translate((string)"AnomalySettings".Translate()));
                            StorytellerSelectionPatch.ReturnToStorytellerMode();
                        }
                        else
                        {
                            // On Esc/X (discard), keep the cursor on the AnomalySettings row.
                            TolkHelper.SpeakData("RimWorldAccess.AnomalySettings.Closed".Translate((string)"AnomalySettings".Translate()));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[AnomalySettingsDialogPatch] PostClose failed: {ex.Message}");
                }
            }
        }

        // Same pattern as EntityCodexPatch / CaravanFormationPatch: block Window's own Cancel
        // handling so Escape only closes when our state has nothing else to do (e.g. clearing
        // typeahead). Without this, Escape would close the dialog AND propagate to whoever's
        // underneath, potentially closing the storyteller page.
        [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
        public static class Window_OnCancelKeyPressed_AnomalySettings_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Window __instance)
            {
                if (__instance is Dialog_AnomalySettings && AnomalySettingsDialogState.IsActive)
                    return false;
                return true;
            }
        }

        // Block the game's "press Enter = Accept" auto-handler — we want Enter to mean
        // "activate the focused row," not "commit and close."
        [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
        public static class Window_OnAcceptKeyPressed_AnomalySettings_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Window __instance)
            {
                if (__instance is Dialog_AnomalySettings && AnomalySettingsDialogState.IsActive)
                    return false;
                return true;
            }
        }

        // Mirror FactionLandingPatch.Page_OnCancelKeyPressed_Patch: when the dialog is open OR
        // we just closed it on this frame, prevent the underlying Page_SelectStoryteller from
        // also receiving the Cancel keystroke (which would call DoBack and bounce us out of
        // the storyteller page entirely). HandleEventsHighPriority fires Notify_PressedCancel
        // independently of Event.current.Use(), so a simple Use() doesn't block it.
        [HarmonyPatch(typeof(Page), "OnCancelKeyPressed")]
        public static class Page_OnCancelKeyPressed_AnomalySettings_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Page __instance)
            {
                if (__instance is Page_SelectStoryteller &&
                    (AnomalySettingsDialogState.IsActive ||
                     AnomalySettingsDialogState.escapeHandledOnFrame == Time.frameCount))
                {
                    return false;
                }
                return true;
            }
        }

        // Same defense for Accept: while the dialog is open, the underlying page must not
        // advance to the next page on Enter.
        [HarmonyPatch(typeof(Page), "OnAcceptKeyPressed")]
        public static class Page_OnAcceptKeyPressed_AnomalySettings_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Page __instance)
            {
                if (__instance is Page_SelectStoryteller && AnomalySettingsDialogState.IsActive)
                    return false;
                return true;
            }
        }
    }
}
