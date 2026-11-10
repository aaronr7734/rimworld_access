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
        // Null when the constant is gone or no longer a literal.
        private static readonly float? ReductionPerObstacle = ReadReduction();

        // CompPowerPlantWind.RecalculateBlockages zeroes output on a vacuum map before it looks at the path.
        private const bool StopsInVacuum = true;

        private static float? ReadReduction()
        {
            FieldInfo field = AccessTools.Field(typeof(CompPowerPlantWind), "PowerReductionPercentPerObstacle");
            return field != null && field.IsLiteral ? (float)field.GetRawConstantValue() : (float?)null;
        }

        [HarmonyPostfix]
        internal static void Postfix(ThingDef def, IntVec3 center, Rot4 rot)
        {
            try
            {
                if (!PlacementHelpSpeech.CollectingOutlines || !ReductionPerObstacle.HasValue)
                    return;
                WindPathGhostNote.Collect(WindTurbineUtility.CalculateWindCells(center, rot, def.size),
                    ReductionPerObstacle.Value, StopsInVacuum);
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

        public static PlacementRating MakeRating(ThingDef def, Map map)
        {
            if (!ReductionPerObstacle.HasValue)
                return null;
            return WindPathGhostNote.RatingFactory(WindTurbineUtility.CalculateWindCells,
                ReductionPerObstacle.Value, StopsInVacuum)(def, map);
        }
    }
}
