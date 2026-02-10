using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Dialog_FactionDuringLanding to enable keyboard accessibility.
    /// Uses Window.PostOpen/PostClose lifecycle (same pattern as InfoCardPatch).
    /// </summary>
    public static class FactionLandingPatch
    {
        /// <summary>
        /// Postfix patch for Window.PostOpen to activate keyboard navigation when
        /// Dialog_FactionDuringLanding opens. Multiple postfixes on Window.PostOpen
        /// are safe — InfoCardPatch also patches this method with a type check.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostOpen")]
        public static class Window_PostOpen_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (__instance is Dialog_FactionDuringLanding dialog)
                {
                    FactionLandingState.Open(dialog);
                }
            }
        }

        /// <summary>
        /// Postfix patch for Window.PostClose to clean up accessibility state when
        /// Dialog_FactionDuringLanding closes.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (__instance is Dialog_FactionDuringLanding && FactionLandingState.IsActive)
                {
                    FactionLandingState.Close();
                }
            }
        }

        /// <summary>
        /// Block the game's Escape handling on Page_SelectStartingSite in the same frame
        /// we closed the faction dialog. Our prefix on UIRootOnGUI handles Escape and removes
        /// the dialog, but the original UIRootOnGUI still runs (void prefix), so
        /// HandleEventsHighPriority fires Notify_PressedCancel which would call DoBack()
        /// on the now-exposed Page_SelectStartingSite. Uses frame counter because IsActive
        /// is already false by the time this fires. See CLAUDE.md "Keyboard Input Isolation".
        /// </summary>
        [HarmonyPatch(typeof(Page), "OnCancelKeyPressed")]
        public static class Page_OnCancelKeyPressed_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Page __instance)
            {
                if (__instance is Page_SelectStartingSite &&
                    (FactionLandingState.IsActive || FactionLandingState.escapeHandledOnFrame == Time.frameCount))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
