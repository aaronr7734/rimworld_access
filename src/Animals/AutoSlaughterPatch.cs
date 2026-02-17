using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Dialog_AutoSlaughter to enable keyboard accessibility.
    /// Uses Window.PostOpen/PostClose lifecycle for proper state management.
    /// </summary>
    public static class AutoSlaughterPatch
    {
        /// <summary>
        /// Postfix patch for Window.PostOpen to activate keyboard navigation when Dialog_AutoSlaughter opens.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostOpen")]
        public static class Window_PostOpen_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (__instance is Dialog_AutoSlaughter dialog)
                {
                    AutoSlaughterState.Open(dialog);
                }
            }
        }

        /// <summary>
        /// Postfix patch for Window.PostClose to clean up accessibility state when Dialog_AutoSlaughter closes.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (__instance is Dialog_AutoSlaughter)
                {
                    if (AutoSlaughterState.IsActive)
                    {
                        AutoSlaughterState.Close();
                    }
                }
            }
        }

        /// <summary>
        /// Patch for Window.OnAcceptKeyPressed to block the game's Enter key handling.
        /// Dialog_AutoSlaughter inherits Window's default OnAcceptKeyPressed which closes the dialog.
        /// We block this so our Enter key handling (numeric input mode, boolean toggle) works.
        /// </summary>
        [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
        public static class Window_OnAcceptKeyPressed_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Window __instance)
            {
                if (__instance is Dialog_AutoSlaughter && AutoSlaughterState.IsActive)
                    return false;
                return true;
            }
        }
    }
}
