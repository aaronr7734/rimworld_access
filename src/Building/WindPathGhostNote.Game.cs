using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Speaks what a wind-driven building's ghost outlines at the placement cursor: which of its
    /// wind cells are obstructed, by what, and the output share those obstructions cost. The ghost
    /// draws only the outline; a sighted player reads the trees, walls and roofs inside it.
    /// Shared by the vanilla wind turbine and any modded building built on the same wind rule.
    /// </summary>
    internal static class WindPathGhostNote
    {
        /// <param name="windCells">The cells the building's own ghost outlines.</param>
        /// <param name="reductionPerObstacle">The owning comp's output cut per obstructed cell.</param>
        public static void Collect(IEnumerable<IntVec3> windCells, float reductionPerObstacle)
        {
            if (!PlacementHelpSpeech.CollectingOutlines)
                return;
            Map map = Find.CurrentMap;
            if (map == null)
                return;
            PlacementHelpSpeech.CollectOutline(Describe(map, windCells, reductionPerObstacle));
        }

        private static string Describe(Map map, IEnumerable<IntVec3> windCells, float reductionPerObstacle)
        {
            var countByBlocker = new Dictionary<string, int>();
            var blockerOrder = new List<string>();
            int blocked = 0;
            foreach (IntVec3 cell in windCells)
            {
                // Off-map cells are never drawn, and the roof grid has no bounds check of its own.
                if (!cell.InBounds(map))
                    continue;
                string blocker = BlockerAt(map, cell);
                if (blocker == null)
                    continue;
                blocked++;
                if (countByBlocker.TryGetValue(blocker, out int count))
                {
                    countByBlocker[blocker] = count + 1;
                }
                else
                {
                    countByBlocker[blocker] = 1;
                    blockerOrder.Add(blocker);
                }
            }

            if (blocked == 0)
                return "RimWorldAccess.Building.Place.WindPathClear".Translate();

            var blockers = new List<string>(blockerOrder.Count);
            foreach (string blocker in blockerOrder)
            {
                int count = countByBlocker[blocker];
                blockers.Add(count == 1
                    ? blocker
                    : (string)"RimWorldAccess.Building.Place.WindBlockerCount".Translate(blocker, count));
            }
            string blockerList = blockers.ToCommaList(useAnd: true);

            float reduction = Mathf.Min(1f, blocked * reductionPerObstacle);
            return reduction >= 1f
                ? "RimWorldAccess.Building.Place.WindPathBlockedNoOutput".Translate(blockerList)
                : "RimWorldAccess.Building.Place.WindPathBlocked".Translate(blockerList, reduction.ToStringPercent());
        }

        /// <summary>
        /// The roof or first wind-blocking thing on a cell. Mirrors
        /// <c>CompPowerPlantWind.RecalculateBlockages</c>: the rule lives only in that private method
        /// of a spawned comp, which a ghost has no instance of.
        /// </summary>
        private static string BlockerAt(Map map, IntVec3 cell)
        {
            RoofDef roof = map.roofGrid.RoofAt(cell);
            if (roof != null)
                return roof.label;
            List<Thing> things = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].def.blockWind)
                    return things[i].LabelNoCount;
            }
            return null;
        }
    }
}
