using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Dialog_GrowthMomentChoices to enable keyboard accessibility.
    /// Activates GrowthMomentState when the dialog opens and cleans up when it closes.
    /// </summary>
    public static class GrowthMomentPatch
    {
        /// <summary>
        /// Postfix patch for ChoiceLetter_GrowthMoment.OpenLetter() to activate keyboard navigation.
        /// At this point TrySetChoices() has populated passionChoices and traitChoices,
        /// and the dialog has been added to the WindowStack.
        /// </summary>
        [HarmonyPatch(typeof(ChoiceLetter_GrowthMoment), "OpenLetter")]
        public static class OpenLetter_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(ChoiceLetter_GrowthMoment __instance)
            {
                try
                {
                    // Find the dialog on the WindowStack
                    var dialog = Find.WindowStack.Windows
                        .OfType<Dialog_GrowthMomentChoices>()
                        .FirstOrDefault();

                    if (dialog != null)
                    {
                        GrowthMomentState.Open(__instance, dialog);
                    }
                    else
                    {
                        Log.Warning("[RimWorld Access] Growth moment dialog not found on WindowStack after OpenLetter");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimWorld Access] Error in GrowthMomentPatch.OpenLetter postfix: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Postfix patch for Window.PostClose to clean up accessibility state when the dialog closes.
        /// Handles all close paths: our keyboard close, timeout, pawn death, etc.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (__instance is Dialog_GrowthMomentChoices)
                {
                    if (GrowthMomentState.IsActive)
                    {
                        GrowthMomentState.Close();
                    }
                }
            }
        }
    }
}
