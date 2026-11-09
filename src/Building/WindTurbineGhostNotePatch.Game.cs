using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>The wind turbine ghost's obstructions, through <see cref="WindPathGhostNote"/>.</summary>
    [HarmonyPatch(typeof(PlaceWorker_WindTurbine), nameof(PlaceWorker_WindTurbine.DrawGhost))]
    internal static class WindTurbineGhostNotePatch
    {
        private static readonly FieldInfo ReductionPerObstacle =
            AccessTools.Field(typeof(CompPowerPlantWind), "PowerReductionPercentPerObstacle");

        [HarmonyPostfix]
        internal static void Postfix(ThingDef def, IntVec3 center, Rot4 rot)
        {
            try
            {
                if (!PlacementHelpSpeech.CollectingOutlines || ReductionPerObstacle == null || !ReductionPerObstacle.IsLiteral)
                    return;
                WindPathGhostNote.Collect(WindTurbineUtility.CalculateWindCells(center, rot, def.size),
                    (float)ReductionPerObstacle.GetRawConstantValue());
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Wind turbine ghost note error", ex);
            }
        }
    }
}
