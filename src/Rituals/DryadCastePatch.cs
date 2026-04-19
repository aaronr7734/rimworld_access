using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Dialog_ChangeDryadCaste lifecycle.
    /// Keeps DryadCasteState synchronized with dialog open/close and prevents the
    /// game's default Cancel handling from closing the dialog out from under us.
    /// </summary>
    public static class DryadCastePatch
    {
        /// <summary>
        /// Dialog_ChangeDryadCaste doesn't override PostOpen, so we patch the base Window.PostOpen
        /// and filter. This runs after PreOpen's Ideology DLC guard has had a chance to call Close(),
        /// so we won't try to open state against a dialog that's already on its way out.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostOpen")]
        public static class Window_PostOpen_DryadCaste_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_ChangeDryadCaste dialog))
                    return;

                try
                {
                    DryadCasteState.Open(dialog);
                }
                catch (Exception ex)
                {
                    Log.Error($"[DryadCastePatch] Error in PostOpen: {ex.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_DryadCaste_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_ChangeDryadCaste))
                    return;

                try
                {
                    if (DryadCasteState.IsActive)
                    {
                        DryadCasteState.Close();
                        TolkHelper.Speak("Dryad caste dialog closed.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[DryadCastePatch] Error in PostClose: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Block game's default Cancel handling while our keyboard nav is active — our Escape
        /// handler already calls Close(), and RimWorld's KeyBindingDefOf.Cancel.KeyDownEvent
        /// fires independently of Event.current.Use(). Without this, the game can trigger a
        /// second close path (with a close sound) right after our own.
        /// </summary>
        [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
        public static class Window_OnCancelKeyPressed_DryadCaste_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Window __instance)
            {
                if (__instance is Dialog_ChangeDryadCaste && DryadCasteState.IsActive)
                    return false;
                return true;
            }
        }
    }
}
