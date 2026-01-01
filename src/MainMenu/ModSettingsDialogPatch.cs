using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Dialog_ModSettings to announce when mod settings dialogs open and close.
    /// Note: Individual mod settings UIs are mod-specific and cannot be generically navigated.
    /// This patch provides basic dialog-level accessibility.
    /// </summary>
    [HarmonyPatch]
    public static class ModSettingsDialogPatch
    {
        // Cache reflection for the mod field
        private static FieldInfo modField;

        // Track which dialogs have been announced to avoid repeating
        private static HashSet<int> announcedDialogs = new HashSet<int>();

        static ModSettingsDialogPatch()
        {
            modField = typeof(Dialog_ModSettings).GetField("mod", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        /// <summary>
        /// Patch DoWindowContents to announce on first render and track dialog lifecycle.
        /// Dialog_ModSettings doesn't override PostOpen/PostClose.
        /// </summary>
        [HarmonyPatch(typeof(Dialog_ModSettings), "DoWindowContents")]
        [HarmonyPostfix]
        public static void DoWindowContents_Postfix(Dialog_ModSettings __instance, Rect inRect)
        {
            int instanceId = __instance.GetHashCode();

            // Announce only on first render
            if (!announcedDialogs.Contains(instanceId))
            {
                announcedDialogs.Add(instanceId);

                var mod = modField?.GetValue(__instance) as Mod;
                if (mod != null)
                {
                    string modName = mod.SettingsCategory();
                    if (modName.NullOrEmpty())
                    {
                        modName = mod.Content?.Name ?? "Unknown mod";
                    }
                    TolkHelper.Speak($"Mod settings for {modName}. Press Escape to close.");
                }
                else
                {
                    TolkHelper.Speak("Mod settings dialog opened. Press Escape to close.");
                }
            }
        }

        /// <summary>
        /// Patch the base Window.PostClose to detect when Dialog_ModSettings closes.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostClose")]
        [HarmonyPostfix]
        public static void Window_PostClose_Postfix(Window __instance)
        {
            if (__instance is Dialog_ModSettings)
            {
                int instanceId = __instance.GetHashCode();
                announcedDialogs.Remove(instanceId);
                TolkHelper.Speak("Mod settings closed");
            }
        }
    }
}
