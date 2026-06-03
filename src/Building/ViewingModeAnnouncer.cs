using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Handles all announcement text generation for ViewingModeState.
    /// Pure formatting methods with no side effects.
    /// </summary>
    public static class ViewingModeAnnouncer
    {
        // Prefix used to mark terrain-based obstacles for special formatting
        private const string TerrainPrefix = "TERRAIN:";

        #region Main Entry Points

        /// <summary>
        /// Builds the announcement for entering viewing mode.
        /// For orders, includes a summary of what was designated.
        /// For builds, includes blueprint count and obstacles.
        /// </summary>
        /// <param name="designator">The designator used for placement</param>
        /// <param name="totalPlaced">Total count of placed items</param>
        /// <param name="segmentShapeTypes">List of shape types used for each segment</param>
        /// <param name="isOrderDesignator">Whether this is an order designator</param>
        /// <param name="isZoneDesignator">Whether this is a zone designator</param>
        /// <param name="isBuildDesignator">Whether this is a build designator</param>
        /// <param name="isDeleteDesignator">Whether this is a delete/shrink designator</param>
        /// <param name="obstacleCells">List of cells where placement failed</param>
        /// <param name="orderTargets">List of things designated by order operations</param>
        /// <param name="orderTargetCells">List of cells designated by cell-based orders</param>
        /// <param name="detectedRegionCount">Number of disconnected regions detected</param>
        /// <param name="detectedEnclosures">List of detected enclosures</param>
        /// <param name="placedCells">List of all placed cells (for zone size calculation)</param>
        /// <param name="lastSegmentCells">List of cells from the most recent segment only (for expansion announcements)</param>
        /// <param name="placedBlueprints">List of all placed blueprints</param>
        /// <param name="wasZoneExpansion">Whether this was an expansion of existing zone</param>
        /// <param name="targetZone">The target zone being edited (if any)</param>
        /// <param name="createdZones">Set of zones created/modified</param>
        /// <param name="isAreaDesignator">Whether this is an area designator (Allowed Areas)</param>
        /// <param name="targetArea">The target area for area designators</param>
        /// <param name="isBuiltInAreaDesignator">Whether this is a built-in area designator (Snow/Sand, Roof, Home)</param>
        /// <returns>The announcement string</returns>
        public static string BuildEntryAnnouncement(
            Designator designator,
            int totalPlaced,
            List<ShapeType> segmentShapeTypes,
            bool isOrderDesignator,
            bool isZoneDesignator,
            bool isBuildDesignator,
            bool isDeleteDesignator,
            List<IntVec3> obstacleCells,
            List<Thing> orderTargets,
            List<IntVec3> orderTargetCells,
            int detectedRegionCount,
            List<Enclosure> detectedEnclosures,
            List<IntVec3> placedCells,
            List<IntVec3> lastSegmentCells,
            List<Thing> placedBlueprints,
            bool wasZoneExpansion,
            Zone targetZone,
            HashSet<Zone> createdZones,
            bool isAreaDesignator = false,
            Area targetArea = null,
            bool isBuiltInAreaDesignator = false,
            int protectedCount = 0,
            HashSet<string> protectedByLabels = null)
        {
            // Use shape type counts for multi-segment, empty for single segment
            string shapeInfo = FormatShapeTypeCounts(segmentShapeTypes);
            string segmentInfo = !string.IsNullOrEmpty(shapeInfo)
                ? (string)"RimWorldAccess.Building.Announcer.SegmentSuffix".Translate(shapeInfo)
                : "";

            if (isOrderDesignator)
            {
                // Build order-specific announcement with clear shape and target info
                // Format: "Hunt order in 7 by 4 area, targeting 2 deer, 1 fox"
                string targetSummary = BuildOrderTargetList(designator, orderTargets, orderTargetCells);
                int targetCount = orderTargets.Count + orderTargetCells.Count;
                string designatorLabel = designator?.Label ?? (string)"RimWorldAccess.Building.Announcer.OrderFallback".Translate();

                // Use shape-aware formatting for the cells (dimensions when possible)
                string shapeSize = "";
                if (placedCells != null && placedCells.Count > 0)
                {
                    shapeSize = ShapeHelper.FormatShapeSize(placedCells);
                }

                var hints = new List<string>();
                if (targetCount > 0)
                {
                    hints.Add("RimWorldAccess.Building.Announcer.HintNavigateTargets".Translate());
                }
                hints.Add("RimWorldAccess.Building.Announcer.HintAddShape".Translate());
                hints.Add("RimWorldAccess.Building.Announcer.HintUndoLast".Translate());
                hints.Add("RimWorldAccess.Building.Announcer.HintConfirm".Translate());
                string hintsStr = JoinHints(hints);

                // Build the announcement with clear structure
                string mainPart;
                if (!string.IsNullOrEmpty(shapeSize) && !string.IsNullOrEmpty(targetSummary))
                {
                    // "Hunt order on 7 by 4, targeting 2 deer and 1 fox"
                    mainPart = "RimWorldAccess.Building.Announcer.OrderOnTargeting".Translate(designatorLabel, shapeSize, targetSummary);
                }
                else if (!string.IsNullOrEmpty(targetSummary))
                {
                    // No shape info, just targets: "Hunt order targeting 2 deer and 1 fox"
                    mainPart = "RimWorldAccess.Building.Announcer.OrderTargeting".Translate(designatorLabel, targetSummary);
                }
                else if (!string.IsNullOrEmpty(shapeSize))
                {
                    // Shape but no targets: "Hunt order on 7 by 4"
                    mainPart = "RimWorldAccess.Building.Announcer.OrderOn".Translate(designatorLabel, shapeSize);
                }
                else
                {
                    // Fallback
                    mainPart = "RimWorldAccess.Building.Announcer.OrderDesignations".Translate(totalPlaced, designatorLabel);
                }

                return BuildViewingModeAnnouncement(new List<string> { mainPart + segmentInfo, hintsStr });
            }
            else if (isAreaDesignator || isBuiltInAreaDesignator)
            {
                // Build area-specific announcement (Allowed Areas and Built-in Areas like Snow/Sand, Roof, Home)
                return BuildAreaDesignatorAnnouncement(
                    designator, totalPlaced, segmentInfo, placedCells, lastSegmentCells, targetArea, isBuiltInAreaDesignator);
            }
            else if (isZoneDesignator)
            {
                // Build zone-specific announcement with obstacle info and split warning
                return BuildZoneDesignatorAnnouncement(
                    designator, totalPlaced, segmentInfo, isDeleteDesignator,
                    obstacleCells, detectedRegionCount, placedCells, lastSegmentCells,
                    wasZoneExpansion, targetZone, createdZones);
            }
            else
            {
                // Build announcement for build designators with smooth flowing sentences
                return BuildBuildDesignatorAnnouncement(
                    designator, totalPlaced, segmentInfo, isBuildDesignator,
                    obstacleCells, detectedEnclosures, placedBlueprints, placedCells,
                    protectedCount, protectedByLabels);
            }
        }

        /// <summary>
        /// Builds a list of order targets grouped by type, without the designator prefix.
        /// Uses 1% threshold to group small categories under "and N others".
        /// Examples: "2 deer, 1 fox" or "12 compacted steel, 5 jade, and 3 more items in 2 smaller groups"
        /// </summary>
        /// <param name="designator">The order designator</param>
        /// <param name="orderTargets">List of things designated</param>
        /// <param name="orderTargetCells">List of cells designated</param>
        /// <returns>The target list string (without designator label)</returns>
        public static string BuildOrderTargetList(Designator designator, List<Thing> orderTargets, List<IntVec3> orderTargetCells)
        {
            Map map = Find.CurrentMap;

            // Count things by kind/def
            var thingCounts = new Dictionary<string, int>();

            foreach (Thing thing in orderTargets)
            {
                // Use LabelShort to exclude condition percentages for proper grouping
                string label = thing.LabelShort ?? thing.def?.label ?? (string)"RimWorldAccess.Building.Announcer.UnknownTarget".Translate();
                if (thingCounts.ContainsKey(label))
                    thingCounts[label]++;
                else
                    thingCounts[label] = 1;
            }

            // Count cell-based targets by what's there (mineable, etc.)
            if (map != null)
            {
                foreach (IntVec3 cell in orderTargetCells)
                {
                    // Get what's at this cell
                    Thing edifice = cell.GetEdifice(map);
                    string label;

                    if (edifice != null)
                    {
                        // Use LabelShort to exclude condition percentages for proper grouping
                        label = edifice.LabelShort ?? edifice.def?.label ?? (string)"RimWorldAccess.Building.Announcer.UnknownTarget".Translate();
                    }
                    else
                    {
                        TerrainDef terrain = cell.GetTerrain(map);
                        label = terrain?.label ?? (string)"RimWorldAccess.Building.Announcer.TileFallback".Translate();
                    }

                    if (thingCounts.ContainsKey(label))
                        thingCounts[label]++;
                    else
                        thingCounts[label] = 1;
                }
            }

            if (thingCounts.Count == 0)
                return string.Empty;

            int totalCount = thingCounts.Values.Sum();
            int threshold = totalCount / 100; // 1% threshold
            if (threshold < 1)
                threshold = 1; // minimum 1

            var significantParts = new List<string>();
            int smallGroupCount = 0;
            int smallGroupItems = 0;

            foreach (var kvp in thingCounts.OrderByDescending(x => x.Value))
            {
                if (kvp.Value >= threshold)
                {
                    string label = kvp.Value > 1
                        ? ArchitectHelper.PluralizePreservingParentheses(kvp.Key, kvp.Value)
                        : kvp.Key;
                    significantParts.Add("RimWorldAccess.Building.Announcer.CountLabel".Translate(kvp.Value, label));
                }
                else
                {
                    smallGroupCount++;
                    smallGroupItems += kvp.Value;
                }
            }

            // Format list with "and" before last item: "3 oak trees, 2 poplar trees, and 1 birch tree"
            string result = JoinWithAnd(significantParts);

            if (smallGroupCount > 0)
            {
                if (significantParts.Count > 0)
                {
                    // ", and N more items in M smaller groups" - One/Many on the group count
                    result += (smallGroupCount == 1
                        ? "RimWorldAccess.Building.Announcer.MoreItemsInGroupsSuffixOne"
                        : "RimWorldAccess.Building.Announcer.MoreItemsInGroupsSuffixMany").Translate(smallGroupItems, smallGroupCount);
                }
                else
                {
                    result = (smallGroupCount == 1
                        ? "RimWorldAccess.Building.Announcer.ItemsInGroupsOne"
                        : "RimWorldAccess.Building.Announcer.ItemsInGroupsMany").Translate(smallGroupItems, smallGroupCount);
                }
            }

            return result;
        }

        #endregion

        #region Zone-Specific Announcements

        /// <summary>
        /// Builds the announcement specifically for zone designators.
        /// Produces smooth flowing sentences combining zone creation/expansion, obstacles, and split warnings.
        /// Uses dimensions instead of cell counts for clarity.
        /// Examples:
        /// - "50 by 12 stockpile zone created." (new zone)
        /// - "4 by 3 stockpile zone expanded." (adding to existing zone - uses last segment size)
        /// - "50 by 12 stockpile zone created, 11 cells blocked by sandstones." (contiguous - no split warning)
        /// - "50 by 12 stockpile zone created, 11 cells blocked by 3 walls. Warning: This will create 3 separate zones."
        /// For delete/shrink: "Removed 50 cells from zone."
        /// </summary>
        public static string BuildZoneDesignatorAnnouncement(
            Designator designator,
            int totalPlaced,
            string segmentInfo,
            bool isDeleteDesignator,
            List<IntVec3> obstacleCells,
            int detectedRegionCount,
            List<IntVec3> placedCells,
            List<IntVec3> lastSegmentCells,
            bool wasZoneExpansion,
            Zone targetZone,
            HashSet<Zone> createdZones)
        {
            var parts = new List<string>();

            // Handle delete/shrink designators differently
            if (isDeleteDesignator)
            {
                // For shrink, we're removing cells, not creating zones
                // Use shape-aware formatting (dimensions when possible)
                if (totalPlaced > 0)
                {
                    // Use lastSegmentCells for the most recent removal, fall back to placedCells
                    var cellsForSize = lastSegmentCells != null && lastSegmentCells.Count > 0
                        ? lastSegmentCells
                        : placedCells;
                    string sizeString = ShapeHelper.FormatShapeSize(cellsForSize);
                    parts.Add("RimWorldAccess.Building.Announcer.RemovedFromZone".Translate(sizeString) + segmentInfo);
                }
                else
                {
                    parts.Add((string)"RimWorldAccess.Building.Announcer.NoZoneCellsRemoved".Translate() + segmentInfo);
                }

                parts.Add(DefaultEditHints());
                return BuildViewingModeAnnouncement(parts);
            }

            // Get zone type label from designator (already returns a lowercase-in-sentence noun)
            string zoneName = GetZoneTypeName(designator, targetZone, createdZones);

            // Get blocking obstacle summary using zone-specific detection
            string blockingObstacleSummary = GetZoneBlockingObstacleSummary(obstacleCells);

            // Get actual zone count from createdZones (more accurate than detectedRegionCount)
            int actualZoneCount = createdZones?.Count ?? 0;
            if (actualZoneCount == 0 && totalPlaced > 0)
            {
                // Fallback to 1 if zones haven't been collected yet
                actualZoneCount = 1;
            }

            // Build blocked part string if there are obstacles
            string blockedPart = "";
            if (obstacleCells.Count > 0)
            {
                if (!string.IsNullOrEmpty(blockingObstacleSummary))
                {
                    blockedPart = (obstacleCells.Count == 1
                        ? "RimWorldAccess.Building.Announcer.CellsBlockedByOne"
                        : "RimWorldAccess.Building.Announcer.CellsBlockedByMany").Translate(obstacleCells.Count, blockingObstacleSummary);
                }
                else
                {
                    blockedPart = (obstacleCells.Count == 1
                        ? "RimWorldAccess.Building.Announcer.CellsBlockedOne"
                        : "RimWorldAccess.Building.Announcer.CellsBlockedMany").Translate(obstacleCells.Count);
                }
            }

            // Build the main placement sentence
            if (totalPlaced > 0)
            {
                if (actualZoneCount > 1)
                {
                    // Multiple zones created - list each zone's size using shape-aware formatting
                    var zoneSizes = new List<string>();
                    foreach (Zone zone in createdZones.OrderByDescending(z => z.Cells.Count))
                    {
                        var zoneCells = ZoneEditingHelper.GetZoneCells(zone);
                        string sizeStr = ShapeHelper.FormatShapeSize(zoneCells);
                        zoneSizes.Add(sizeStr);
                    }
                    string sizesList = string.Join(", ", zoneSizes);
                    parts.Add((actualZoneCount == 1
                        ? "RimWorldAccess.Building.Announcer.ZonesCreatedListOne"
                        : "RimWorldAccess.Building.Announcer.ZonesCreatedListMany").Translate(actualZoneCount, zoneName, sizesList)
                        + blockedPart + segmentInfo);
                }
                else if (wasZoneExpansion)
                {
                    // Single zone expansion - use lastSegmentCells to announce only the newly added shape
                    // (not all cells from all segments combined)
                    var cellsForSize = lastSegmentCells != null && lastSegmentCells.Count > 0
                        ? lastSegmentCells
                        : placedCells;
                    string sizeString = ShapeHelper.FormatShapeSize(cellsForSize);
                    parts.Add((string)"RimWorldAccess.Building.Announcer.ZoneExpandedBy".Translate(zoneName, sizeString)
                        + blockedPart + segmentInfo);
                }
                else
                {
                    // Single zone creation
                    string sizeString = ShapeHelper.FormatShapeSize(placedCells);
                    parts.Add((string)"RimWorldAccess.Building.Announcer.ZoneCreated".Translate(sizeString, zoneName)
                        + blockedPart + segmentInfo);
                }
            }
            else
            {
                // No cells placed - all blocked
                parts.Add((string)"RimWorldAccess.Building.Announcer.NoZoneCellsPlaced".Translate() + segmentInfo);

                if (obstacleCells.Count > 0 && !string.IsNullOrEmpty(blockingObstacleSummary))
                {
                    parts.Add((obstacleCells.Count == 1
                        ? "RimWorldAccess.Building.Announcer.AllCellsBlockedByOne"
                        : "RimWorldAccess.Building.Announcer.AllCellsBlockedByMany").Translate(obstacleCells.Count, blockingObstacleSummary));
                }
            }

            // Add control hints
            var hints = new List<string>();
            if (obstacleCells.Count > 0)
            {
                hints.Add("RimWorldAccess.Building.Announcer.HintNavigateObstacles".Translate());
            }
            hints.Add("RimWorldAccess.Building.Announcer.HintAddShape".Translate());
            hints.Add("RimWorldAccess.Building.Announcer.HintUndoLast".Translate());
            hints.Add("RimWorldAccess.Building.Announcer.HintConfirm".Translate());
            parts.Add(JoinHints(hints));

            return BuildViewingModeAnnouncement(parts);
        }

        /// <summary>
        /// Gets the zone type name from a zone designator or the target zone.
        /// Prioritizes the actual zone's label (e.g., "Stockpile zone 1") over the designator label
        /// because expand/shrink designators have generic labels like "Expand zone".
        /// </summary>
        public static string GetZoneTypeName(Designator designator, Zone targetZone, HashSet<Zone> createdZones)
        {
            // First, try to get the zone type from the actual target zone
            // This is more reliable for expand/shrink operations where the designator
            // label is generic (e.g., "Expand zone") but the zone has a specific type.
            // NOTE: substring matches below run against English game labels (zone.label /
            // designator.Label). They only fire for English game data and fall through to
            // the generic key otherwise - matching the TODO-pattern used elsewhere in Building.
            // TODO: detect zone type by class/def identity instead of English label matching.
            if (targetZone != null && !string.IsNullOrEmpty(targetZone.label))
            {
                string match = ZoneTypeKeyFromLabel(targetZone.label);
                if (match != null)
                    return match;
            }

            // Also check createdZones if targetZone isn't set yet
            if (createdZones != null && createdZones.Count > 0)
            {
                Zone firstZone = createdZones.First();
                if (firstZone != null && !string.IsNullOrEmpty(firstZone.label))
                {
                    string match = ZoneTypeKeyFromLabel(firstZone.label);
                    if (match != null)
                        return match;
                }
            }

            // Fall back to designator label for zone creation (not expand/shrink)
            if (designator == null)
                return "RimWorldAccess.Building.Announcer.ZoneType.Generic".Translate();

            string label = designator.Label;
            if (!string.IsNullOrEmpty(label))
            {
                // Common patterns: "Create stockpile zone", "Create growing zone"
                string typeMatch = ZoneTypeKeyFromLabel(label);
                if (typeMatch != null)
                    return typeMatch;

                // For expand/shrink operations, the label is just "Expand zone" or "Shrink zone"
                // which doesn't have a zone type - so fall through to generic "Zone"
                string lowerLabel = label.ToLower();
                if (lowerLabel.Contains("expand") || lowerLabel.Contains("shrink"))
                    return "RimWorldAccess.Building.Announcer.ZoneType.Generic".Translate();

                // Fallback to the game's own label with some cleanup (for unknown/modded zones).
                // Surfacing the game label keeps mod compatibility; no localization possible here.
                label = label.Replace("Create ", "").Replace(" zone", "");
                if (!string.IsNullOrEmpty(label))
                    return char.ToUpper(label[0]) + label.Substring(1);
            }

            return "RimWorldAccess.Building.Announcer.ZoneType.Generic".Translate();
        }

        /// <summary>
        /// Maps an English game zone label to a localized lowercase-in-sentence zone-type noun,
        /// or null if the label doesn't match a known zone type.
        /// English keyword matching only fires for English game data and falls through gracefully.
        /// </summary>
        private static string ZoneTypeKeyFromLabel(string gameLabel)
        {
            string zoneLabel = gameLabel.ToLower();
            if (zoneLabel.Contains("stockpile"))
                return "RimWorldAccess.Building.Announcer.ZoneType.Stockpile".Translate();
            if (zoneLabel.Contains("growing"))
                return "RimWorldAccess.Building.Announcer.ZoneType.Growing".Translate();
            if (zoneLabel.Contains("dumping"))
                return "RimWorldAccess.Building.Announcer.ZoneType.Dumping".Translate();
            if (zoneLabel.Contains("fishing"))
                return "RimWorldAccess.Building.Announcer.ZoneType.Fishing".Translate();
            return null;
        }

        /// <summary>
        /// Gets a formatted summary of obstacles blocking zone placement.
        /// Specifically looks for things that can't overlap zones (buildings, walls, etc.).
        /// Groups obstacles by type and formats as "X thing1, Y thing2, and Z thing3".
        /// </summary>
        public static string GetZoneBlockingObstacleSummary(List<IntVec3> obstacleCells)
        {
            if (obstacleCells == null || obstacleCells.Count == 0)
                return string.Empty;

            Map map = Find.CurrentMap;
            if (map == null)
                return string.Empty;

            var obstacleCounts = CountItemsByLabel(obstacleCells, GetZoneObstacleLabel, map);
            return FormatCountedList(obstacleCounts, truncate: true);
        }

        /// <summary>
        /// Gets a simple label for the obstacle blocking zone placement at a cell.
        /// Checks for things that can't overlap zones according to RimWorld's CanOverlapZones property.
        /// </summary>
        public static string GetZoneObstacleLabel(IntVec3 cell, Map map)
        {
            // Check for things at the cell that can't overlap zones
            List<Thing> things = cell.GetThingList(map);
            foreach (Thing thing in things)
            {
                // Check if this thing prevents zone overlap
                if (thing.def != null && !thing.def.CanOverlapZones)
                {
                    // Try multiple label sources to get a meaningful name
                    // Use LabelNoParenthesis to exclude condition percentages (e.g., "(61%)") for proper grouping
                    string label = thing.LabelNoParenthesis;
                    if (string.IsNullOrEmpty(label))
                    {
                        label = thing.def?.label;
                    }
                    if (string.IsNullOrEmpty(label))
                    {
                        // For blueprints/frames, try to get the label of what's being built
                        if (thing.def?.entityDefToBuild is ThingDef builtDef)
                        {
                            label = "RimWorldAccess.Building.Announcer.UnbuiltSuffix".Translate(builtDef.label);
                        }
                    }
                    if (string.IsNullOrEmpty(label))
                    {
                        // Use the thing's category as a last resort (category name is a game enum, not localizable here)
                        label = thing.def?.category.ToString().ToLower()
                            ?? (string)"RimWorldAccess.Building.Announcer.StructureFallback".Translate();
                    }
                    return label;
                }
            }

            // Check for existing zone of different type
            Zone existingZone = map.zoneManager.ZoneAt(cell);
            if (existingZone != null)
            {
                return "RimWorldAccess.Building.Announcer.ExistingZone".Translate(existingZone.label);
            }

            // Check if too close to map edge
            if (cell.InNoZoneEdgeArea(map))
            {
                return TerrainPrefix + (string)"RimWorldAccess.Building.Announcer.EdgeArea".Translate();
            }

            // Check if fogged
            if (cell.Fogged(map))
            {
                return "RimWorldAccess.Building.Announcer.FogOfWar".Translate();
            }

            // Check terrain for impassable areas
            TerrainDef terrain = cell.GetTerrain(map);
            if (terrain != null && terrain.passability == Traversability.Impassable)
            {
                return TerrainPrefix + (terrain.label ?? (string)"RimWorldAccess.Building.Announcer.TerrainFallback".Translate());
            }

            // If we still can't identify the obstacle, report the terrain
            // This catches cases like low fertility terrain blocking growing zones
            if (terrain != null)
            {
                return TerrainPrefix + (terrain.label ?? (string)"RimWorldAccess.Building.Announcer.UnknownTerrain".Translate());
            }

            // True fallback - should be very rare
            return "RimWorldAccess.Building.Announcer.BlockedCell".Translate();
        }

        #endregion

        #region Area-Specific Announcements

        /// <summary>
        /// Builds the announcement specifically for area designators (Allowed Areas and Built-in Areas).
        /// Uses "Expanded" for expand designators and "Cleared" for clear designators.
        /// Examples:
        /// - "Expanded Kitchen by 7 by 4" (adding to allowed area)
        /// - "Cleared Kitchen by 12 cells" (removing from allowed area)
        /// - "Marked 4 by 5 for snow and sand removal" (built-in area)
        /// - "Unmarked 4 by 5 from snow and sand removal area" (built-in area clear)
        /// </summary>
        /// <param name="designator">The area designator</param>
        /// <param name="totalPlaced">Total count of cells affected</param>
        /// <param name="segmentInfo">Shape segment info string</param>
        /// <param name="placedCells">List of all placed cells</param>
        /// <param name="lastSegmentCells">List of cells from the most recent segment</param>
        /// <param name="targetArea">The target area being edited</param>
        /// <param name="isBuiltInAreaDesignator">Whether this is a built-in area (Snow/Sand, Roof, Home)</param>
        /// <returns>The announcement string</returns>
        public static string BuildAreaDesignatorAnnouncement(
            Designator designator,
            int totalPlaced,
            string segmentInfo,
            List<IntVec3> placedCells,
            List<IntVec3> lastSegmentCells,
            Area targetArea,
            bool isBuiltInAreaDesignator = false)
        {
            var parts = new List<string>();

            // Get the area name - for built-in areas, use the designator label or area label
            string areaName;
            if (isBuiltInAreaDesignator)
            {
                // Use the area's label (e.g., "Snow clear area", "Home area", "Build roof area")
                // or fall back to a name derived from the designator
                areaName = targetArea?.Label ?? GetBuiltInAreaName(designator);
            }
            else
            {
                areaName = targetArea?.Label ?? (string)"RimWorldAccess.Building.Announcer.AreaFallback".Translate();
            }

            // Determine the action (drives whole-phrase dispatch below)
            bool isExpanding = isBuiltInAreaDesignator
                ? ShapeHelper.IsBuiltInAreaExpanding(designator)
                : designator is Designator_AreaAllowedExpand;

            if (totalPlaced > 0)
            {
                // Use lastSegmentCells if available for the most recent shape size,
                // fall back to placedCells for all cells
                var cellsForSize = lastSegmentCells != null && lastSegmentCells.Count > 0
                    ? lastSegmentCells
                    : placedCells;
                string sizeString = ShapeHelper.FormatShapeSize(cellsForSize);

                if (isBuiltInAreaDesignator)
                {
                    // Built-in area: "Marked 4 by 5 for snow and sand removal" / "Unmarked 4 by 5 from home area"
                    parts.Add((isExpanding
                        ? "RimWorldAccess.Building.Announcer.BuiltInAreaMarked"
                        : "RimWorldAccess.Building.Announcer.BuiltInAreaUnmarked").Translate(sizeString, areaName)
                        + segmentInfo);
                }
                else
                {
                    // Allowed area: "Expanded Kitchen by 4 by 5" / "Cleared Kitchen by 4 by 5"
                    parts.Add((isExpanding
                        ? "RimWorldAccess.Building.Announcer.AllowedAreaExpanded"
                        : "RimWorldAccess.Building.Announcer.AllowedAreaCleared").Translate(areaName, sizeString)
                        + segmentInfo);
                }
            }
            else
            {
                // No cells affected. Whole-phrase per action to avoid lowercasing translated verbs.
                string noneKey;
                if (isBuiltInAreaDesignator)
                {
                    noneKey = isExpanding
                        ? "RimWorldAccess.Building.Announcer.NoAreaCellsMarked"
                        : "RimWorldAccess.Building.Announcer.NoAreaCellsUnmarked";
                }
                else
                {
                    noneKey = isExpanding
                        ? "RimWorldAccess.Building.Announcer.NoAreaCellsExpanded"
                        : "RimWorldAccess.Building.Announcer.NoAreaCellsCleared";
                }
                parts.Add((string)noneKey.Translate() + segmentInfo);
            }

            // Add control hints
            parts.Add(DefaultEditHints());

            return BuildViewingModeAnnouncement(parts);
        }

        /// <summary>
        /// Gets a readable name for a built-in area designator.
        /// </summary>
        private static string GetBuiltInAreaName(Designator designator)
        {
            if (designator == null)
                return "RimWorldAccess.Building.Announcer.AreaFallback".Translate();

            // Use the designator's label if available, which is usually descriptive
            // e.g., "Expand snow clear area", "Clear home area", "Build roof area".
            // Surfacing the game's own label keeps mod compatibility; the prefix strip
            // below only affects English game labels and falls through harmlessly otherwise.
            // TODO: stripping is English-keyword game-data handling, not user-facing copy.
            string label = designator.Label;
            if (!string.IsNullOrEmpty(label))
            {
                // Clean up the label - remove "Expand " or "Clear " prefix if present
                label = label.ToLower();
                if (label.StartsWith("expand "))
                    label = label.Substring(7);
                else if (label.StartsWith("clear "))
                    label = label.Substring(6);

                return label;
            }

            // Fallback based on type name (only hit when the game label is empty)
            string typeName = designator.GetType().Name;
            if (typeName.Contains("SnowClear") || typeName.Contains("SandClear"))
                return "RimWorldAccess.Building.Announcer.BuiltInArea.SnowSand".Translate();
            if (typeName.Contains("Home"))
                return "RimWorldAccess.Building.Announcer.BuiltInArea.Home".Translate();
            if (typeName.Contains("BuildRoof"))
                return "RimWorldAccess.Building.Announcer.BuiltInArea.BuildRoof".Translate();
            if (typeName.Contains("NoRoof"))
                return "RimWorldAccess.Building.Announcer.BuiltInArea.NoRoof".Translate();
            if (typeName.Contains("IgnoreRoof"))
                return "RimWorldAccess.Building.Announcer.BuiltInArea.IgnoreRoof".Translate();
            if (typeName.Contains("PollutionClear"))
                return "RimWorldAccess.Building.Announcer.BuiltInArea.PollutionClear".Translate();

            return "RimWorldAccess.Building.Announcer.AreaFallback".Translate();
        }

        #endregion

        #region Build-Specific Announcements

        /// <summary>
        /// Builds the announcement specifically for build designators.
        /// Produces smooth flowing sentences combining placement, obstacles, and enclosure info.
        /// Examples:
        /// - "Placed 7 by 4 wooden walls (140 wood). 7 by 9 enclosure formed containing 2 poplar trees, 1 cougar (dead), and 1 limestone chunk, but has 1 gap."
        /// - "Placed 6 by 4 of 28 wooden walls (120 wood). 4 walls blocked by 2 compacted steel and 2 granite boulders."
        /// </summary>
        public static string BuildBuildDesignatorAnnouncement(
            Designator designator,
            int totalPlaced,
            string segmentInfo,
            bool isBuildDesignator,
            List<IntVec3> obstacleCells,
            List<Enclosure> detectedEnclosures,
            List<Thing> placedBlueprints,
            List<IntVec3> placedCells,
            int protectedCount = 0,
            HashSet<string> protectedByLabels = null)
        {
            var parts = new List<string>();

            // Get designator label for the announcement
            // Use sanitized label to strip "..." suffix (prevents "wall...s" bug)
            string designatorLabel = ArchitectHelper.GetSanitizedLabel(designator,
                (string)"RimWorldAccess.Building.Announcer.BlueprintsFallback".Translate());

            // Calculate total intended placements (placed + blocked)
            int totalIntended = totalPlaced + obstacleCells.Count;

            // Get blocking obstacle summary
            string blockingObstacleSummary = GetBlockingObstacleSummary(obstacleCells);

            // Get shape-aware size string for placed cells (e.g., "7 by 4" instead of "28")
            string placedSizeStr = placedCells != null && placedCells.Count > 0
                ? ShapeHelper.FormatShapeSize(placedCells)
                : totalPlaced.ToString();

            // Build the main placement sentence
            if (obstacleCells.Count > 0 && totalPlaced > 0)
            {
                // "Placed 7 by 4 of 28 wooden walls (cost), Z blocked by [obstacle list]."
                // Uses shape dimensions for placed count, total count for intended
                string pluralLabel = totalPlaced > 1
                    ? ArchitectHelper.PluralizePreservingParentheses(designatorLabel, totalPlaced)
                    : designatorLabel;

                string costInfo = GetCostInfo(placedBlueprints);
                string blockedPart;
                if (!string.IsNullOrEmpty(blockingObstacleSummary))
                {
                    blockedPart = (obstacleCells.Count == 1
                        ? "RimWorldAccess.Building.Announcer.BlockedByOne"
                        : "RimWorldAccess.Building.Announcer.BlockedByMany").Translate(obstacleCells.Count, blockingObstacleSummary);
                }
                else
                {
                    blockedPart = (obstacleCells.Count == 1
                        ? "RimWorldAccess.Building.Announcer.BlockedOne"
                        : "RimWorldAccess.Building.Announcer.BlockedMany").Translate(obstacleCells.Count);
                }
                parts.Add((string)"RimWorldAccess.Building.Announcer.PlacedOfTotal".Translate(placedSizeStr, totalIntended, pluralLabel, costInfo)
                    + blockedPart + segmentInfo);
            }
            else if (totalPlaced > 0)
            {
                // "Placed 7 by 4 wooden walls (cost)." - uses shape dimensions
                string pluralLabel = totalPlaced > 1
                    ? ArchitectHelper.PluralizePreservingParentheses(designatorLabel, totalPlaced)
                    : designatorLabel;

                string costInfo = GetCostInfo(placedBlueprints);
                parts.Add((string)"RimWorldAccess.Building.Place.PlacedBuild".Translate(placedSizeStr, pluralLabel, costInfo)
                    + segmentInfo);
            }
            else
            {
                // No placements at all
                parts.Add((isBuildDesignator
                    ? "RimWorldAccess.Building.Place.NoBlueprintsPlaced"
                    : "RimWorldAccess.Building.Place.NoDesignationsPlaced").Translate()
                    + segmentInfo);

                if (obstacleCells.Count > 0 && !string.IsNullOrEmpty(blockingObstacleSummary))
                {
                    parts.Add("RimWorldAccess.Building.Announcer.AllBlockedBy".Translate(blockingObstacleSummary));
                }
            }

            // Add enclosure info if any enclosures detected
            if (detectedEnclosures != null && detectedEnclosures.Count > 0)
            {
                if (detectedEnclosures.Count == 1)
                {
                    var enc = detectedEnclosures[0];
                    string enclosureSize = ShapeHelper.FormatShapeSize(enc.InteriorCells);
                    string enclosurePart;

                    // Add "containing X, Y, Z" if there are interior obstacles
                    string interiorSummary = (enc.ObstacleCount > 0 && enc.Obstacles != null)
                        ? FormatObstacleList(enc.Obstacles)
                        : string.Empty;
                    if (!string.IsNullOrEmpty(interiorSummary))
                    {
                        enclosurePart = "RimWorldAccess.Building.Announcer.EnclosureFormedContaining".Translate(enclosureSize, interiorSummary);
                    }
                    else
                    {
                        enclosurePart = "RimWorldAccess.Building.Announcer.EnclosureFormed".Translate(enclosureSize);
                    }

                    // Add gap warning at the end
                    if (enc.HasGaps)
                    {
                        enclosurePart += (enc.GapCount == 1
                            ? "RimWorldAccess.Building.Announcer.EnclosureGapSuffixOne"
                            : "RimWorldAccess.Building.Announcer.EnclosureGapSuffixMany").Translate(enc.GapCount);
                    }

                    parts.Add(enclosurePart);
                }
                else
                {
                    int totalGaps = detectedEnclosures.Sum(e => e.GapCount);

                    // Build per-enclosure descriptions with individual contents
                    var enclosureDescriptions = new List<string>();
                    foreach (var enc in detectedEnclosures)
                    {
                        string size = ShapeHelper.FormatShapeSize(enc.InteriorCells);
                        string description;

                        // Add this enclosure's obstacles if any
                        string contents = (enc.Obstacles != null && enc.Obstacles.Count > 0)
                            ? FormatObstacleList(enc.Obstacles)
                            : string.Empty;
                        if (!string.IsNullOrEmpty(contents))
                        {
                            description = "RimWorldAccess.Building.Announcer.EnclosureSizeContaining".Translate(size, contents);
                        }
                        else
                        {
                            description = size;
                        }

                        enclosureDescriptions.Add(description);
                    }

                    string enclosurePart = "RimWorldAccess.Building.Announcer.EnclosuresFormedList".Translate(
                        detectedEnclosures.Count, string.Join(". ", enclosureDescriptions));

                    // Add gap warning at the end
                    if (totalGaps > 0)
                    {
                        enclosurePart += (totalGaps == 1
                            ? "RimWorldAccess.Building.Announcer.EnclosureTotalGapSuffixOne"
                            : "RimWorldAccess.Building.Announcer.EnclosureTotalGapSuffixMany").Translate(totalGaps);
                    }

                    parts.Add(enclosurePart);
                }
            }

            // Meditation protection info
            if (protectedCount > 0 && protectedByLabels != null)
            {
                string protectionSummary = MeditationProtectionHelper.FormatShapeSummary(
                    protectedCount, protectedByLabels);
                if (!string.IsNullOrEmpty(protectionSummary))
                {
                    parts.Add(protectionSummary);
                    parts.Add("RimWorldAccess.Building.Place.MeditationDisableHint".Translate());
                }
            }

            // Add control hints
            var hints = new List<string>();

            // Obstacle navigation hint
            bool hasAnyObstacles = obstacleCells.Count > 0 || (detectedEnclosures != null && detectedEnclosures.Any(e => e.ObstacleCount > 0));
            if (hasAnyObstacles)
            {
                hints.Add("RimWorldAccess.Building.Announcer.HintNavigateObstacles".Translate());
            }

            // Editing hints
            hints.Add("RimWorldAccess.Building.Announcer.HintAddShape".Translate());
            hints.Add("RimWorldAccess.Building.Announcer.HintUndoLast".Translate());
            hints.Add("RimWorldAccess.Building.Announcer.HintConfirm".Translate());

            parts.Add(JoinHints(hints));

            return BuildViewingModeAnnouncement(parts);
        }

        /// <summary>
        /// Gets a formatted summary of blocking obstacles (obstacles that prevented placement).
        /// Groups obstacles by type and formats as "X thing1, Y thing2, and Z thing3".
        /// </summary>
        public static string GetBlockingObstacleSummary(List<IntVec3> obstacleCells)
        {
            if (obstacleCells == null || obstacleCells.Count == 0)
                return string.Empty;

            Map map = Find.CurrentMap;
            if (map == null)
                return string.Empty;

            var obstacleCounts = CountItemsByLabel(obstacleCells, GetObstacleLabel, map);
            return FormatCountedList(obstacleCounts, truncate: true);
        }

        /// <summary>
        /// Gets a simple label for the obstacle at a cell (without "Obstacle:" prefix).
        /// Terrain-based obstacles are prefixed with "TERRAIN:" for special formatting.
        /// </summary>
        public static string GetObstacleLabel(IntVec3 cell, Map map)
        {
            // Check for things at the cell
            List<Thing> things = cell.GetThingList(map);
            foreach (Thing thing in things)
            {
                // Skip plants, filth, and other minor things
                // Use LabelShort to exclude condition percentages (e.g., "(61%)") for proper grouping
                if (thing is Building || thing is Pawn)
                {
                    return thing.LabelShort ?? thing.def?.label ?? (string)"RimWorldAccess.Building.Announcer.ObstacleFallback".Translate();
                }

                // Check for blueprints or frames
                if (thing.def.IsBlueprint || thing.def.IsFrame)
                {
                    return thing.LabelShort ?? (string)"RimWorldAccess.Building.Announcer.BlueprintObstacle".Translate();
                }

                // Check for items
                if (thing.def.category == ThingCategory.Item)
                {
                    return thing.LabelShort ?? (string)"RimWorldAccess.Building.Announcer.ItemObstacle".Translate();
                }
            }

            // Check terrain - prefix with TERRAIN: for special formatting as "X tiles"
            TerrainDef terrain = cell.GetTerrain(map);
            if (terrain != null && (terrain.passability == Traversability.Impassable || !terrain.affordances?.Contains(TerrainAffordanceDefOf.Light) == true))
            {
                return TerrainPrefix + (terrain.label ?? (string)"RimWorldAccess.Building.Announcer.TerrainFallback".Translate());
            }

            return "RimWorldAccess.Building.Announcer.ObstacleFallback".Translate();
        }

        /// <summary>
        /// Gets the cost info string for the current placement (e.g., "(140 wood)").
        /// </summary>
        public static string GetCostInfo(List<Thing> placedBlueprints)
        {
            // Try to calculate total cost from placed blueprints
            int totalCost = 0;
            string resourceName = string.Empty;

            if (placedBlueprints != null && placedBlueprints.Count > 0 && placedBlueprints[0]?.def?.entityDefToBuild is ThingDef thingDef)
            {
                // Get the stuff used (material)
                ThingDef stuffDef = ArchitectState.SelectedMaterial;

                if (thingDef.MadeFromStuff && stuffDef != null)
                {
                    totalCost = thingDef.CostStuffCount * placedBlueprints.Count;
                    resourceName = stuffDef.label;
                }
                else if (thingDef.CostList != null && thingDef.CostList.Count > 0)
                {
                    totalCost = thingDef.CostList[0].count * placedBlueprints.Count;
                    resourceName = thingDef.CostList[0].thingDef.label;
                }
            }

            if (totalCost > 0 && !string.IsNullOrEmpty(resourceName))
            {
                return "RimWorldAccess.Building.Place.PlacedCostSuffix".Translate(totalCost, resourceName);
            }

            return string.Empty;
        }

        #endregion

        #region Composition Helpers

        /// <summary>
        /// Wraps a list of body parts with the "Viewing mode" prefix and joins them with
        /// periods. The AnnouncementBuilder owns the separators/trailing punctuation.
        /// </summary>
        private static string BuildViewingModeAnnouncement(List<string> parts)
        {
            var builder = new AnnouncementBuilder();
            builder.Add("RimWorldAccess.Building.Announcer.ViewingMode".Translate());
            foreach (string part in parts)
            {
                builder.Add(part);
            }
            return builder.Build();
        }

        /// <summary>
        /// Joins control hints into a single comma-separated phrase.
        /// Hints are short imperative fragments and read naturally as a comma list.
        /// </summary>
        private static string JoinHints(List<string> hints)
        {
            return string.Join(", ", hints);
        }

        /// <summary>
        /// The standard "Equals to add another shape, Minus to undo last, Enter to confirm" hint trio.
        /// </summary>
        private static string DefaultEditHints()
        {
            return JoinHints(new List<string>
            {
                "RimWorldAccess.Building.Announcer.HintAddShape".Translate(),
                "RimWorldAccess.Building.Announcer.HintUndoLast".Translate(),
                "RimWorldAccess.Building.Announcer.HintConfirm".Translate(),
            });
        }

        /// <summary>
        /// Joins a list of phrase fragments using the localized list conjunctions:
        /// one item as-is, two items via the "A and B" key, and three or more via the
        /// "A, B, and C" key. Per the no-glue rule the conjunction lives in whole-phrase keys.
        /// </summary>
        private static string JoinWithAnd(List<string> items)
        {
            if (items == null || items.Count == 0)
                return string.Empty;
            if (items.Count == 1)
                return items[0];
            if (items.Count == 2)
                return "RimWorldAccess.Building.Announcer.ListTwo".Translate(items[0], items[1]);

            string allButLast = string.Join(", ", items.Take(items.Count - 1));
            return "RimWorldAccess.Building.Announcer.ListMany".Translate(allButLast, items.Last());
        }

        #endregion

        #region Shared Utilities

        /// <summary>
        /// Counts items by their label from a collection of cells.
        /// This is a shared helper for GetZoneBlockingObstacleSummary and GetBlockingObstacleSummary.
        /// </summary>
        /// <param name="cells">The cells to process</param>
        /// <param name="labelGetter">A function that gets the label for a cell</param>
        /// <param name="map">The map to use for label lookup</param>
        /// <returns>A dictionary of label to count</returns>
        public static Dictionary<string, int> CountItemsByLabel(IEnumerable<IntVec3> cells, Func<IntVec3, Map, string> labelGetter, Map map)
        {
            var counts = new Dictionary<string, int>();

            foreach (IntVec3 cell in cells)
            {
                string label = labelGetter(cell, map);
                if (counts.ContainsKey(label))
                    counts[label]++;
                else
                    counts[label] = 1;
            }

            return counts;
        }

        /// <summary>
        /// Formats a list of ScannerItem obstacles as "X thing1, Y thing2, and Z thing3".
        /// Lists all types without truncation.
        /// </summary>
        public static string FormatObstacleList(List<ScannerItem> obstacles)
        {
            if (obstacles == null || obstacles.Count == 0)
                return string.Empty;

            // Count obstacles by their label
            var obstacleCounts = new Dictionary<string, int>();

            foreach (var obstacle in obstacles)
            {
                // Use the thing's LabelNoParenthesis for proper grouping (excludes condition percentages)
                string label;
                if (obstacle.Thing != null)
                {
                    label = obstacle.Thing.LabelNoParenthesis ?? obstacle.Thing.def?.label ?? (string)"RimWorldAccess.Building.Announcer.ObstacleFallback".Translate();
                }
                else
                {
                    // For terrain-based obstacles, use the stored label (strip any prefix)
                    label = obstacle.Label;
                    if (label.StartsWith("Interior: "))
                        label = label.Substring(10);
                }

                if (obstacleCounts.ContainsKey(label))
                    obstacleCounts[label]++;
                else
                    obstacleCounts[label] = 1;
            }

            return FormatCountedList(obstacleCounts, truncate: true);
        }

        /// <summary>
        /// Formats a dictionary of label counts as "X thing1, Y thing2, and Z thing3".
        /// Pluralizes labels when count > 1.
        /// Terrain-based obstacles (prefixed with TERRAIN:) are formatted as "X terrain tiles" instead of pluralizing.
        /// When truncate is true, groups with less than 1% of the total are summarized as
        /// "and X more in Y smaller groups".
        /// </summary>
        /// <param name="counts">Dictionary of label to count</param>
        /// <param name="truncate">If true, truncate groups under 1% threshold</param>
        public static string FormatCountedList(Dictionary<string, int> counts, bool truncate = false)
        {
            if (counts == null || counts.Count == 0)
                return string.Empty;

            var formattedParts = new List<string>();
            int smallGroupCount = 0;
            int smallGroupItems = 0;

            // Calculate 1% threshold for truncation
            int totalCount = counts.Values.Sum();
            int threshold = truncate ? totalCount / 100 : 0;
            if (truncate && threshold < 1)
                threshold = 1; // minimum 1

            // Sort by count descending for consistency
            foreach (var kvp in counts.OrderByDescending(x => x.Value))
            {
                // If truncating and this group is below threshold, add to summary
                if (truncate && kvp.Value < threshold)
                {
                    smallGroupCount++;
                    smallGroupItems += kvp.Value;
                    continue;
                }

                string key = kvp.Key;
                string label;

                // Check for terrain prefix - terrain gets "X terrain tiles" format
                if (key.StartsWith(TerrainPrefix))
                {
                    string terrainName = key.Substring(TerrainPrefix.Length);
                    // Whole-phrase One/Many: "{0} {terrainName} tile" vs "{0} {terrainName} tiles"
                    formattedParts.Add((kvp.Value == 1
                        ? "RimWorldAccess.Building.Announcer.TerrainTilesOne"
                        : "RimWorldAccess.Building.Announcer.TerrainTilesMany").Translate(kvp.Value, terrainName));
                }
                else
                {
                    // Regular items get pluralized
                    label = kvp.Value > 1
                        ? ArchitectHelper.PluralizePreservingParentheses(key, kvp.Value)
                        : key;
                    formattedParts.Add("RimWorldAccess.Building.Announcer.CountLabel".Translate(kvp.Value, label));
                }
            }

            // Add truncated summary if any small groups were skipped
            if (smallGroupCount > 0)
            {
                formattedParts.Add((smallGroupCount == 1
                    ? "RimWorldAccess.Building.Announcer.MoreInGroupsOne"
                    : "RimWorldAccess.Building.Announcer.MoreInGroupsMany").Translate(smallGroupItems, smallGroupCount));
            }

            // Join with commas and "and" before the last item
            return JoinWithAnd(formattedParts);
        }

        /// <summary>
        /// Formats the shape type counts for announcements.
        /// Returns empty string for single segment (don't announce for single segment).
        /// Examples: "2 lines, 1 rectangle" or "1 line, 2 filled rectangles"
        /// </summary>
        public static string FormatShapeTypeCounts(List<ShapeType> segmentShapeTypes)
        {
            if (segmentShapeTypes == null || segmentShapeTypes.Count == 0) return "";
            if (segmentShapeTypes.Count == 1) return ""; // Don't announce for single segment

            var shapeCounts = segmentShapeTypes.GroupBy(s => s)
                .Select(g => new { Shape = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToList();

            var parts = new List<string>();
            foreach (var item in shapeCounts)
            {
                string shapeName = GetShapeDisplayName(item.Shape);
                if (item.Count == 1)
                    parts.Add("RimWorldAccess.Building.Announcer.CountLabel".Translate(1, shapeName));
                else
                    parts.Add("RimWorldAccess.Building.Announcer.CountLabel".Translate(
                        item.Count, Find.ActiveLanguageWorker.Pluralize(shapeName, item.Count)));
            }

            return string.Join(", ", parts);
        }

        /// <summary>
        /// Gets a display name for a shape type.
        /// </summary>
        public static string GetShapeDisplayName(ShapeType shape)
        {
            switch (shape)
            {
                case ShapeType.Manual: return "RimWorldAccess.Building.Announcer.ShapeName.Manual".Translate();
                case ShapeType.Line: return "RimWorldAccess.Building.Announcer.ShapeName.Line".Translate();
                case ShapeType.AngledLine: return "RimWorldAccess.Building.Announcer.ShapeName.AngledLine".Translate();
                case ShapeType.FilledRectangle: return "RimWorldAccess.Building.Announcer.ShapeName.FilledRectangle".Translate();
                case ShapeType.EmptyRectangle: return "RimWorldAccess.Building.Announcer.ShapeName.EmptyRectangle".Translate();
                case ShapeType.FilledOval: return "RimWorldAccess.Building.Announcer.ShapeName.FilledOval".Translate();
                case ShapeType.EmptyOval: return "RimWorldAccess.Building.Announcer.ShapeName.EmptyOval".Translate();
                default: return "RimWorldAccess.Building.Announcer.ShapeName.Generic".Translate();
            }
        }

        #endregion
    }
}
