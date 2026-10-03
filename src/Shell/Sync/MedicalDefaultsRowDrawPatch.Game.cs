using System;
using HarmonyLib;
using RimWorld;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Records each group row <see cref="Dialog_MedicalDefaults"/> draws for
    /// <see cref="MedicalDefaultsScope"/>. Every group goes through the private DoRow (decompiled
    /// RimWorld/Dialog_MedicalDefaults.cs:63), which carries its label key, tooltip key and
    /// current setting.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_MedicalDefaults), "DoRow")]
    internal static class MedicalDefaultsRowDrawPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Dialog_MedicalDefaults __instance, ref MedicalCareCategory category, string labelKey, string tipKey)
        {
            MedicalDefaultsScope scope = MedicalDefaultsScope.Current;
            if (scope == null)
            {
                return;
            }
            try
            {
                scope.RecordRow(__instance, labelKey, tipKey, category);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Medical defaults row capture error", ex);
            }
        }
    }
}
