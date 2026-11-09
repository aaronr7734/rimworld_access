using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// What a wind-driven building's ghost outlines: which of its wind cells are obstructed, by
    /// what, and the output share those obstructions cost. The ghost draws only the outline; a
    /// sighted player reads the trees, walls and roofs inside it. One calculation serves both the
    /// placement cursor's note and the placement-spot rating, so the two always agree. Shared by
    /// the vanilla wind turbine and any modded building built on the same wind rule.
    /// </summary>
    internal static class WindPathGhostNote
    {
        /// <param name="windCells">The cells the building's own ghost outlines.</param>
        /// <param name="reductionPerObstacle">The owning comp's output cut per obstructed cell.</param>
        /// <param name="stopsInVacuum">Whether the owning comp produces nothing on a vacuum map.</param>
        public static void Collect(IEnumerable<IntVec3> windCells, float reductionPerObstacle, bool stopsInVacuum)
        {
            if (!PlacementHelpSpeech.CollectingOutlines)
                return;
            Map map = Find.CurrentMap;
            if (map == null)
                return;
            PlacementHelpSpeech.CollectOutline(Rate(map, windCells, reductionPerObstacle, stopsInVacuum).Phrase);
        }

        /// <summary>A placement-spot rating factory for a building whose wind cells come from <paramref name="windCells"/>.</summary>
        public static Func<ThingDef, Map, Func<IntVec3, Rot4, PlacementQuality>> RatingFactory(
            Func<IntVec3, Rot4, IntVec2, IEnumerable<IntVec3>> windCells, float reductionPerObstacle, bool stopsInVacuum)
        {
            return (def, map) => (cell, rotation) =>
                Rate(map, windCells(cell, rotation, def.size), reductionPerObstacle, stopsInVacuum);
        }

        /// <summary>The share of full output left after obstructions, and the phrase naming them.</summary>
        public static PlacementQuality Rate(Map map, IEnumerable<IntVec3> windCells, float reductionPerObstacle, bool stopsInVacuum)
        {
            if (stopsInVacuum && map.Biome.inVacuum)
                return new PlacementQuality(0f, "RimWorldAccess.Building.Place.WindNoneInVacuum".Translate());

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
                return new PlacementQuality(PlacementSpotRanking.FullQuality, "RimWorldAccess.Building.Place.WindPathClear".Translate());

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
                ? new PlacementQuality(0f, "RimWorldAccess.Building.Place.WindPathBlockedNoOutput".Translate(blockerList))
                : new PlacementQuality(1f - reduction,
                    "RimWorldAccess.Building.Place.WindPathBlocked".Translate(blockerList, reduction.ToStringPercent()));
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
                    return GenLabel.ThingLabel(things[i].def, things[i].Stuff);
            }
            return null;
        }
    }
}
