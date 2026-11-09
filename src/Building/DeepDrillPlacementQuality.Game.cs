using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// What a deep drill would bring up at a spot: the deep resources in the cells
    /// <c>DeepDrillUtility.GetNextResource</c> draws from around the drill, scored by how full of
    /// resource those cells are. A spot with none in reach is a last resort, since the drill then
    /// only grinds the base rock, and 0 where there is no bedrock either. Rated only while deep
    /// resources are visible
    /// (<see cref="DeepResourceReveal"/>); otherwise spots stay nearest first, so the list never
    /// reveals what the player cannot see.
    /// </summary>
    internal static class DeepDrillPlacementQuality
    {
        public static bool AppliesTo(ThingDef def)
        {
            return def.CompDefFor<CompDeepDrill>() != null;
        }

        public static Func<IntVec3, Rot4, PlacementQuality> MakeRating(ThingDef def, Map map)
        {
            if (!DeepResourceReveal.Revealed(map))
                return null;
            return (cell, rotation) => Rate(map, cell);
        }

        private static PlacementQuality Rate(Map map, IntVec3 position)
        {
            var countByResource = new Dictionary<ThingDef, int>();
            var drillOrder = new List<ThingDef>();
            float fill = 0f;
            for (int i = 0; i < DeepDrillUtility.NumCellsToScan; i++)
            {
                IntVec3 cell = position + GenRadial.RadialPattern[i];
                if (!cell.InBounds(map))
                    continue;
                ThingDef resource = map.deepResourceGrid.ThingDefAt(cell);
                int count = map.deepResourceGrid.CountAt(cell);
                if (resource == null || count <= 0)
                    continue;

                // A freshly found cell holds deepCountPerCell (CompDeepScanner), so this is the share of
                // the reach that is still full.
                fill += Mathf.Clamp01((float)count / resource.deepCountPerCell);
                if (countByResource.TryGetValue(resource, out int total))
                {
                    countByResource[resource] = total + count;
                }
                else
                {
                    countByResource[resource] = count;
                    drillOrder.Add(resource);
                }
            }

            if (drillOrder.Count == 0)
                return RockOnly(map, position);

            var amounts = new List<string>(drillOrder.Count);
            foreach (ThingDef resource in drillOrder)
            {
                amounts.Add("RimWorldAccess.Building.Place.DrillReachAmount".Translate(
                    resource.label, countByResource[resource]));
            }
            // Lifted above the fallback so any deposit outranks bare rock.
            float score = Mathf.Lerp(PlacementSpotRanking.FallbackQuality, PlacementSpotRanking.FullQuality,
                fill / DeepDrillUtility.NumCellsToScan);
            return new PlacementQuality(score,
                "RimWorldAccess.Building.Place.DrillReach".Translate(amounts.ToCommaList(useAnd: true)));
        }

        // CompDeepDrill.CanDrillNow falls back to the base rock; its own exhaustion message names it pluralized.
        private static PlacementQuality RockOnly(Map map, IntVec3 position)
        {
            ThingDef rock = DeepDrillUtility.GetBaseResource(map, position);
            if (rock == null)
                return new PlacementQuality(0f, null);
            return new PlacementQuality(PlacementSpotRanking.FallbackQuality,
                "RimWorldAccess.Building.Place.DrillRockOnly".Translate(Find.ActiveLanguageWorker.Pluralize(rock.label)));
        }
    }
}
