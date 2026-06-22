using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony lifecycle patches for vanilla's <c>Dialog_StylingStation</c>. Opens
    /// <see cref="StylingStationState"/> when the dialog opens, closes it when the dialog closes,
    /// and blocks the window's default Enter/Escape (Accept/Cancel) handling while our state owns
    /// the keyboard. Same defensive pattern as <see cref="EntityCodexPatch"/>.
    /// </summary>
    public static class StylingStationPatch
    {
        [HarmonyPatch(typeof(Window), "PostOpen")]
        public static class Window_PostOpen_Styling_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_StylingStation dialog))
                    return;
                try
                {
                    StylingStationState.Open(dialog);
                }
                catch (Exception ex)
                {
                    Log.Error($"[StylingStationPatch] Error in PostOpen: {ex.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_Styling_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_StylingStation))
                    return;
                try
                {
                    if (StylingStationState.IsActive)
                    {
                        StylingStationState.Close();
                        TolkHelper.Speak("RimWorldAccess.Styling.Closed".Loc());
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[StylingStationPatch] Error in PostClose: {ex.Message}");
                }
            }
        }

        // Block the window's Cancel (Escape) handling while we own input — our Escape handler
        // reverts changes and closes deliberately.
        [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
        public static class Window_OnCancelKeyPressed_Styling_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Window __instance)
            {
                if (__instance is Dialog_StylingStation && StylingStationState.IsActive)
                    return false;
                return true;
            }
        }

        // Block the window's Accept (Enter) handling so pressing Enter to apply a style item or a
        // color-picker option never triggers vanilla's accept-and-close.
        [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
        public static class Window_OnAcceptKeyPressed_Styling_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Window __instance)
            {
                if (__instance is Dialog_StylingStation && StylingStationState.IsActive)
                    return false;
                return true;
            }
        }
    }
}
