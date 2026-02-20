using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Dialog_CreateXenogerm to enable keyboard accessibility.
    /// Activates XenogermState when the dialog opens and cleans up when it closes.
    /// </summary>
    public static class XenogermPatch
    {
        /// <summary>
        /// Postfix patch for Dialog_CreateXenogerm.PostOpen to activate keyboard navigation.
        /// </summary>
        [HarmonyPatch(typeof(Dialog_CreateXenogerm), "PostOpen")]
        public static class PostOpen_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Dialog_CreateXenogerm __instance)
            {
                try
                {
                    XenogermState.Open(__instance);
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimWorld Access] Error in XenogermPatch.PostOpen: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Postfix patch for Window.PostClose to clean up accessibility state when the dialog closes.
        /// Handles all close paths: our keyboard close, Accept, etc.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (__instance is Dialog_CreateXenogerm)
                {
                    if (XenogermState.IsActive)
                    {
                        XenogermState.Close();
                    }
                }
            }
        }

        /// <summary>
        /// Patch for Window.OnCancelKeyPressed to block RimWorld's Escape key handling
        /// when our XenogermState is active. Without this, pressing Escape would close
        /// the dialog via RimWorld's Window system independently of our keyboard handler.
        /// </summary>
        [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
        public static class Window_OnCancelKeyPressed_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Window __instance)
            {
                if (__instance is Dialog_CreateXenogerm || __instance is GeneCreationDialogBase)
                {
                    if (XenogermState.IsActive)
                    {
                        return false; // Block - our HandleInput handles Escape
                    }
                }
                return true;
            }
        }
    }
}
