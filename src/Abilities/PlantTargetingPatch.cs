using HarmonyLib;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Detects the start of CompPlantable seed planting so PlantTargetingState can provide
    /// per-tile plantability feedback (Gauranlen seeds, etc.).
    /// </summary>
    [HarmonyPatch]
    public static class PlantTargetingPatch
    {
        /// <summary>
        /// Prefix on CompPlantable.BeginTargeting (private). Runs before the method calls
        /// Find.Targeter.BeginTargeting, so PlantTargetingState.IsActive is already true when the
        /// generic Targeter.BeginTargeting postfix runs — that postfix checks it and skips opening
        /// the generic ItemTargetingState, avoiding a duplicate announcement.
        ///
        /// The same path fires again when the player confirms an invalid cell: CompPlantable's
        /// callback re-invokes BeginTargeting to keep placement mode open. Open() treats that as
        /// the same session and stays silent.
        /// </summary>
        [HarmonyPatch(typeof(CompPlantable), "BeginTargeting")]
        [HarmonyPrefix]
        public static void BeginTargeting_Prefix(CompPlantable __instance)
        {
            PlantTargetingState.Open(__instance);
        }
    }
}
