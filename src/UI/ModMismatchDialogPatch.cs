using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Announces Dialog_ModMismatch for screen reader users. The dialog appears when
    /// loading a save whose mod list or load order differs from the active set; it
    /// has four visual buttons (Load Anyway, Go Back, Save Mod List, Change Loaded
    /// Mods) but its keyboard behavior collapses to Enter = Load Anyway (via the
    /// class's OnAcceptKeyPressed override) and Escape = Go Back (via the default
    /// Window cancel handling). We announce the warning + mod lists on first frame
    /// and let vanilla's keyboard dispatch handle the rest.
    ///
    /// Dialog_ModMismatch inherits from Window (not Dialog_MessageBox), so the
    /// existing MessageBoxAccessibilityPatch does not cover it.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ModMismatch), "DoWindowContents")]
    public static class ModMismatchDialogPatch
    {
        private static ConditionalWeakTable<Dialog_ModMismatch, object> announcedDialogs = new ConditionalWeakTable<Dialog_ModMismatch, object>();

        [HarmonyPrefix]
        public static void Prefix(Dialog_ModMismatch __instance)
        {
            if (announcedDialogs.TryGetValue(__instance, out _))
                return;

            announcedDialogs.Add(__instance, null);

            try
            {
                var added = AccessTools.Field(typeof(Dialog_ModMismatch), "addedModsList").GetValue(__instance) as List<string>;
                var missing = AccessTools.Field(typeof(Dialog_ModMismatch), "missingModsList").GetValue(__instance) as List<string>;

                bool anyAdded = added != null && added.Count > 0;
                bool anyMissing = missing != null && missing.Count > 0;

                var parts = new List<string>
                {
                    "ModsMismatchWarningTitle".Translate().ToString().StripTags(),
                    "ModsMismatchWarningText".Translate().ToString().StripTags()
                };

                if (!anyAdded && !anyMissing)
                {
                    parts.Add("ModsMismatchOrderChanged".Translate().ToString().StripTags());
                }
                else
                {
                    if (anyAdded)
                    {
                        parts.Add($"{"AddedModsList".Translate()}: {string.Join(", ", added)}");
                    }
                    if (anyMissing)
                    {
                        parts.Add($"{"MissingModsList".Translate()}: {string.Join(", ", missing)}");
                    }
                }

                parts.Add($"Press Enter for {"LoadAnyway".Translate()}, Escape for {"GoBack".Translate()}.");

                TolkHelper.Speak(string.Join(". ", parts), SpeechPriority.High);
            }
            catch (System.Exception ex)
            {
                Log.Error($"RimWorld Access: Failed to announce Dialog_ModMismatch: {ex.Message}");
            }
        }
    }
}
