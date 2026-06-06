using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Handles scanner category management for ViewingModeState.
    /// Creates and updates temporary scanner categories for obstacles and order targets.
    /// </summary>
    public static class ViewingModeScannerHelper
    {
        #region Targets Category (for Order Designators)

        /// <summary>
        /// Creates or updates the temporary scanner category with current order targets.
        /// Similar to UpdateObstacleCategory but for order targets.
        /// </summary>
        /// <param name="orderTargets">List of things designated by order operations</param>
        /// <param name="orderTargetCells">List of cells designated by cell-based orders</param>
        /// <param name="activeDesignator">The active designator (for category naming)</param>
        public static void UpdateTargetsCategory(
            List<Thing> orderTargets,
            List<IntVec3> orderTargetCells,
            Designator activeDesignator)
        {
            int targetCount = orderTargets.Count + orderTargetCells.Count;
            if (targetCount == 0)
            {
                ScannerState.RemoveTemporaryCategory();
                return;
            }

            // Get category name based on designator
            string categoryName = GetTargetsCategoryName(activeDesignator);

            // Create scanner items for each target
            var cursorPos = MapNavigationState.CurrentCursorPosition;
            var targetItems = new List<ScannerItem>();
            Map map = Find.CurrentMap;

            // Add thing-based targets
            foreach (Thing thing in orderTargets)
            {
                string label = thing.LabelShort ?? thing.def?.label ?? "Target";
                var item = new ScannerItem(thing, cursorPos);
                targetItems.Add(item);
            }

            // Add cell-based targets
            if (map != null)
            {
                foreach (IntVec3 cell in orderTargetCells)
                {
                    Thing edifice = cell.GetEdifice(map);
                    string label;

                    if (edifice != null)
                    {
                        // Use the edifice as a scanner item
                        var item = new ScannerItem(edifice, cursorPos);
                        targetItems.Add(item);
                    }
                    else
                    {
                        TerrainDef terrain = cell.GetTerrain(map);
                        label = terrain?.label ?? (string)"RimWorldAccess.Building.Scanner.FallbackTarget".Translate();
                        var item = new ScannerItem(cell, label, cursorPos);
                        targetItems.Add(item);
                    }
                }
            }

            // Sort by distance
            targetItems.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            // Create temporary category in ScannerState
            ScannerState.CreateTemporaryCategory(categoryName, targetItems);
        }

        /// <summary>
        /// Gets the category name for the targets scanner based on the active designator.
        /// </summary>
        /// <param name="activeDesignator">The active designator</param>
        /// <returns>A descriptive category name for the targets</returns>
        public static string GetTargetsCategoryName(Designator activeDesignator)
        {
            if (activeDesignator == null)
                return (string)"RimWorldAccess.Building.Scanner.CategoryTargets".Translate();

            string label = activeDesignator.Label ?? (string)"RimWorldAccess.Building.Scanner.FallbackOrder".Translate();

            // Common designator types get specific names
            string defName = activeDesignator.GetType().Name;

            if (defName.Contains("Hunt"))
                return (string)"RimWorldAccess.Building.Scanner.HuntTargets".Translate();
            if (defName.Contains("Mine"))
                return (string)"RimWorldAccess.Building.Scanner.MineTargets".Translate();
            if (defName.Contains("Cancel"))
                return (string)"RimWorldAccess.Building.Scanner.CanceledOrders".Translate();
            if (defName.Contains("Haul"))
                return (string)"RimWorldAccess.Building.Scanner.HaulTargets".Translate();
            if (defName.Contains("Cut") || defName.Contains("Chop"))
                return (string)"RimWorldAccess.Building.Scanner.CutTargets".Translate();
            if (defName.Contains("Harvest"))
                return (string)"RimWorldAccess.Building.Scanner.HarvestTargets".Translate();
            if (defName.Contains("Tame"))
                return (string)"RimWorldAccess.Building.Scanner.TameTargets".Translate();
            if (defName.Contains("Slaughter"))
                return (string)"RimWorldAccess.Building.Scanner.SlaughterTargets".Translate();
            if (defName.Contains("Deconstruct"))
                return (string)"RimWorldAccess.Building.Scanner.DeconstructTargets".Translate();

            return (string)"RimWorldAccess.Building.Scanner.GenericTargets".Translate(label);
        }

        #endregion

        #region Obstacles Category (for Build and Zone Designators)

        /// <summary>
        /// Creates or updates the temporary scanner category with current obstacles.
        /// Includes both placement obstacles and interior obstacles from enclosures.
        /// </summary>
        /// <param name="obstacleCells">List of cells where placement failed</param>
        /// <param name="detectedEnclosures">List of detected enclosures (for interior obstacles)</param>
        /// <param name="isZoneDesignator">Whether this is a zone designator</param>
        public static void UpdateObstacleCategory(
            List<IntVec3> obstacleCells,
            List<Enclosure> detectedEnclosures,
            bool isZoneDesignator)
        {
            var cursorPos = MapNavigationState.CurrentCursorPosition;
            var allObstacles = new List<ScannerItem>();

            // Add placement obstacles (cells where placement failed)
            foreach (var cell in obstacleCells)
            {
                string obstacleDesc = GetObstacleDescription(cell, isZoneDesignator);
                var item = new ScannerItem(cell, (string)"RimWorldAccess.Building.Scanner.ObstacleWithDesc".Translate(obstacleDesc), cursorPos);
                allObstacles.Add(item);
            }

            // Add interior obstacles from detected enclosures with enclosure context
            // First, count enclosures by size to handle duplicates
            var sizeCounts = new Dictionary<string, int>();
            var sizeCurrentIndex = new Dictionary<string, int>();
            foreach (var enclosure in detectedEnclosures)
            {
                string size = ShapeHelper.FormatShapeSize(enclosure.InteriorCells);
                if (!sizeCounts.ContainsKey(size))
                {
                    sizeCounts[size] = 0;
                    sizeCurrentIndex[size] = 0;
                }
                sizeCounts[size]++;
            }

            // Now add obstacles with numbered enclosure labels when there are duplicates
            foreach (var enclosure in detectedEnclosures)
            {
                if (enclosure.Obstacles != null && enclosure.Obstacles.Count > 0)
                {
                    string enclosureSize = ShapeHelper.FormatShapeSize(enclosure.InteriorCells);
                    sizeCurrentIndex[enclosureSize]++;

                    // Only number if there are multiple enclosures of the same size
                    string enclosureLabel = sizeCounts[enclosureSize] > 1
                        ? (string)"RimWorldAccess.Building.Scanner.EnclosureNumbered".Translate(enclosureSize, sizeCurrentIndex[enclosureSize])
                        : (string)"RimWorldAccess.Building.Scanner.Enclosure".Translate(enclosureSize);

                    foreach (var obstacle in enclosure.Obstacles)
                    {
                        // Create a new item with enclosure context
                        var item = new ScannerItem(obstacle.Thing, cursorPos);
                        item.Label = (string)"RimWorldAccess.Building.Scanner.ObstacleInEnclosure".Translate(obstacle.Label, enclosureLabel);
                        allObstacles.Add(item);
                    }
                }
            }

            if (allObstacles.Count == 0)
            {
                ScannerState.RemoveTemporaryCategory();
                return;
            }

            // Sort by distance
            allObstacles.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            // Create temporary category in ScannerState
            ScannerState.CreateTemporaryCategory((string)"RimWorldAccess.Building.Scanner.CategoryObstacles".Translate(), allObstacles);
        }

        /// <summary>
        /// Gets a description of what is blocking placement at a cell.
        /// For zone designators, uses zone-specific obstacle detection.
        /// </summary>
        /// <param name="cell">The cell to check</param>
        /// <param name="isZoneDesignator">Whether this is a zone designator</param>
        /// <returns>A description of the obstacle</returns>
        public static string GetObstacleDescription(IntVec3 cell, bool isZoneDesignator)
        {
            Map map = Find.CurrentMap;
            if (map == null)
                return (string)"RimWorldAccess.Map.Label.Unknown".Translate();

            // For zone designators, use zone-specific detection
            if (isZoneDesignator)
            {
                return GetZoneObstacleDescriptionForScanner(cell, map);
            }

            // Check for things at the cell
            List<Thing> things = cell.GetThingList(map);
            foreach (Thing thing in things)
            {
                // Skip plants, filth, and other minor things
                if (thing is Building || thing is Pawn)
                {
                    return thing.LabelShort ?? thing.def?.label ?? (string)"RimWorldAccess.Building.Scanner.ObstacleSomething".Translate();
                }

                // Check for blueprints or frames
                if (thing.def.IsBlueprint || thing.def.IsFrame)
                {
                    return thing.LabelShort ?? (string)"RimWorldAccess.Building.Scanner.ObstacleBlueprint".Translate();
                }

                // Check for items
                if (thing.def.category == ThingCategory.Item)
                {
                    return thing.LabelShort ?? (string)"RimWorldAccess.Building.Scanner.ObstacleItem".Translate();
                }
            }

            // Check terrain
            TerrainDef terrain = cell.GetTerrain(map);
            if (terrain != null && (terrain.passability == Traversability.Impassable || !terrain.affordances?.Contains(TerrainAffordanceDefOf.Light) == true))
            {
                return terrain.label ?? (string)"RimWorldAccess.Building.Scanner.ObstacleTerrain".Translate();
            }

            // Check if out of bounds
            if (!cell.InBounds(map))
            {
                return (string)"RimWorldAccess.Building.Scanner.ObstacleOutOfBounds".Translate();
            }

            // Check for roofing issues
            if (map.roofGrid?.RoofAt(cell) != null)
            {
                // Some buildings can't be placed under certain roofs
                return (string)"RimWorldAccess.Building.Scanner.ObstacleRoofedArea".Translate();
            }

            return (string)"RimWorldAccess.Building.Scanner.ObstacleBlocked".Translate();
        }

        /// <summary>
        /// Gets a description for zone obstacles for scanner navigation.
        /// Returns a short, friendly description for screen reader announcement.
        /// </summary>
        /// <param name="cell">The cell to check</param>
        /// <param name="map">The map</param>
        /// <returns>A description of the zone obstacle</returns>
        public static string GetZoneObstacleDescriptionForScanner(IntVec3 cell, Map map)
        {
            // Check for things that can't overlap zones
            List<Thing> things = cell.GetThingList(map);
            foreach (Thing thing in things)
            {
                if (thing.def != null && !thing.def.CanOverlapZones)
                {
                    return thing.LabelShort ?? thing.def?.label ?? (string)"RimWorldAccess.Building.Scanner.ZoneObstacle".Translate();
                }
            }

            // Check for existing zone of different type
            Zone existingZone = map.zoneManager.ZoneAt(cell);
            if (existingZone != null)
            {
                return (string)"RimWorldAccess.Building.Scanner.ZoneExisting".Translate(existingZone.label);
            }

            // Check if too close to map edge
            if (cell.InNoZoneEdgeArea(map))
            {
                return (string)"RimWorldAccess.Building.Scanner.ZoneEdgeArea".Translate();
            }

            // Check if fogged
            if (cell.Fogged(map))
            {
                return (string)"RimWorldAccess.Building.Scanner.ZoneFogOfWar".Translate();
            }

            // Check terrain
            TerrainDef terrain = cell.GetTerrain(map);
            if (terrain != null && terrain.passability == Traversability.Impassable)
            {
                return terrain.label ?? (string)"RimWorldAccess.Building.Scanner.ObstacleTerrain".Translate();
            }

            return (string)"RimWorldAccess.Building.Scanner.ObstacleBlocked".Translate();
        }

        #endregion
    }
}
