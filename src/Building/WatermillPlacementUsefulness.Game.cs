using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The water mill's spacing rule. <c>PlaceWorker_WatermillGenerator.AllowsPlacing</c> checks
    /// only terrain affordance and moving water; the overlap against other mills lives solely in its
    /// DrawGhost, so mirroring that test with the comp's own rect function is the only way a listed
    /// spot means a mill that would actually run.
    /// </summary>
    internal static class WatermillPlacementUsefulness
    {
        public static bool AppliesTo(ThingDef def)
        {
            return def.placeWorkers != null
                && def.placeWorkers.Contains(typeof(PlaceWorker_WatermillGenerator));
        }

        public static Func<IntVec3, Rot4, bool> MakeTest(ThingDef def, Map map)
        {
            List<Thing> mills = CollectWaterMills(def, map);
            return (cell, rotation) => !OverlapsExistingWaterUse(mills, cell, rotation);
        }

        private static bool OverlapsExistingWaterUse(List<Thing> mills, IntVec3 cell, Rot4 rotation)
        {
            CellRect rect = CompPowerPlantWater.WaterUseRect(cell, rotation);
            for (int i = 0; i < mills.Count; i++)
            {
                Thing mill = mills[i];
                if (rect.Overlaps(CompPowerPlantWater.WaterUseRect(mill.Position, mill.Rotation)))
                    return true;
            }
            return false;
        }

        // The three sources DrawGhost collects, generalized to whatever def is being placed.
        private static List<Thing> CollectWaterMills(ThingDef def, Map map)
        {
            var mills = new List<Thing>();
            mills.AddRange(map.listerBuildings.AllBuildingsColonistOfDef(def));

            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint))
            {
                if (thing.def.entityDefToBuild == def)
                    mills.Add(thing);
            }
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
            {
                if (thing.def.entityDefToBuild == def)
                    mills.Add(thing);
            }

            return mills;
        }
    }
}
