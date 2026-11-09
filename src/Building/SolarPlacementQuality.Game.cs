using System;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// A solar generator's share of full output under its footprint's roof, the factor
    /// <c>CompPowerPlantSolar.RoofedPowerOutputFactor</c> applies: unroofed cells over all cells.
    /// That property reads a spawned building's own rect, which a placement candidate has none of,
    /// so it is mirrored over the footprint the building would occupy. Sky glow scales every spot
    /// on the map alike and does not rank them.
    /// </summary>
    internal static class SolarPlacementQuality
    {
        // An exact match: a subclass is free to override DesiredPowerOutput without the roof factor.
        public static bool AppliesTo(ThingDef def)
        {
            if (def.comps == null)
                return false;
            for (int i = 0; i < def.comps.Count; i++)
            {
                if (def.comps[i].compClass == typeof(CompPowerPlantSolar))
                    return true;
            }
            return false;
        }

        public static Func<IntVec3, Rot4, PlacementQuality> MakeRating(ThingDef def, Map map)
        {
            return (cell, rotation) => Rate(def, map, cell, rotation);
        }

        private static PlacementQuality Rate(ThingDef def, Map map, IntVec3 cell, Rot4 rotation)
        {
            int total = 0;
            int roofed = 0;
            foreach (IntVec3 occupied in GenAdj.OccupiedRect(cell, rotation, def.size))
            {
                total++;
                if (map.roofGrid.Roofed(occupied))
                    roofed++;
            }

            if (roofed == 0)
                return PlacementQuality.Full;
            float output = (float)(total - roofed) / total;
            return new PlacementQuality(output,
                "RimWorldAccess.Building.Place.SolarPartlyRoofed".Translate(roofed, total, output.ToStringPercent()));
        }
    }
}
