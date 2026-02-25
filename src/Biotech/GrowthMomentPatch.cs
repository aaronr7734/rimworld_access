using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Dialog_GrowthMomentChoices to enable keyboard accessibility.
    /// Activates GrowthMomentState when the dialog opens and cleans up when it closes.
    /// Uses two initialization paths for robustness:
    /// 1. OpenLetter postfix (primary) - fires after ChoiceLetter_GrowthMoment.OpenLetter()
    /// 2. PostOpen postfix (backup) - fires when Dialog_GrowthMomentChoices.PostOpen() is called
    /// The backup ensures accessibility works even when the dialog is force-opened by the game
    /// (e.g., letter timeout via LetterStack.OpenAutomaticLetters).
    /// </summary>
    public static class GrowthMomentPatch
    {
        private static readonly FieldInfo letterField = typeof(Dialog_GrowthMomentChoices)
            .GetField("letter", BindingFlags.NonPublic | BindingFlags.Instance);

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
                if (GrowthMomentState.IsActive)
                    return;

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
        /// Backup initialization via Window.PostOpen().
        /// Patches the base Window.PostOpen method and checks for Dialog_GrowthMomentChoices,
        /// since Dialog_GrowthMomentChoices does not override PostOpen (Harmony requires the
        /// method to exist on the target type).
        /// Ensures GrowthMomentState is activated even if the OpenLetter postfix
        /// didn't fire (e.g., edge cases during force-open via LetterStack.OpenAutomaticLetters).
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostOpen")]
        public static class Window_PostOpen_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_GrowthMomentChoices dialog))
                    return;

                if (GrowthMomentState.IsActive)
                    return;

                try
                {
                    var letter = letterField?.GetValue(dialog) as ChoiceLetter_GrowthMoment;
                    if (letter != null)
                    {
                        GrowthMomentState.Open(letter, dialog);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimWorld Access] Error in GrowthMomentPatch.PostOpen backup: {ex.Message}");
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
