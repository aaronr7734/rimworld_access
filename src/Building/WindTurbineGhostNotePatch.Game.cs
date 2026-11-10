using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The wind turbine ghost's obstructions, through <see cref="WindPathGhostNote"/>, and the same
    /// reading as the turbine's placement-spot rating.
    /// </summary>
    [HarmonyPatch(typeof(PlaceWorker_WindTurbine), nameof(PlaceWorker_WindTurbine.DrawGhost))]
    internal static class WindTurbineGhostNotePatch
    {
        private static readonly FieldInfo ReductionPerObstacle =
            AccessTools.Field(typeof(CompPowerPlantWind), "PowerReductionPercentPerObstacle");

        // CompPowerPlantWind.RecalculateBlockages zeroes output on a vacuum map before it looks at the path.
        private const bool StopsInVacuum = true;

        private static bool ReductionReadable => ReductionPerObstacle != null && ReductionPerObstacle.IsLiteral;

        [HarmonyPostfix]
        internal static void Postfix(ThingDef def, IntVec3 center, Rot4 rot)
        {
            try
            {
                if (!PlacementHelpSpeech.CollectingOutlines || !ReductionReadable)
                    return;
                WindPathGhostNote.Collect(WindTurbineUtility.CalculateWindCells(center, rot, def.size),
                    (float)ReductionPerObstacle.GetRawConstantValue(), StopsInVacuum);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Wind turbine ghost note error", ex);
            }
        }

        public static bool AppliesTo(ThingDef def)
        {
            return PlacementUsefulnessRegistry.HasPlaceWorker(def, typeof(PlaceWorker_WindTurbine));
        }

        public static Func<IntVec3, Rot4, PlacementQuality> MakeRating(ThingDef def, Map map)
        {
            if (!ReductionReadable)
                return null;
            return WindPathGhostNote.RatingFactory(WindTurbineUtility.CalculateWindCells,
                (float)ReductionPerObstacle.GetRawConstantValue(), StopsInVacuum)(def, map);
        }
    }
}
