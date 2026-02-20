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
            string segmentInfo = !string.IsNullOrEmpty(shapeInfo) ? $" ({shapeInfo})" : "";

            if (isOrderDesignator)
            {
                // Build order-specific announcement with clear shape and target info
                // Format: "Hunt order in 7 by 4 area, targeting 2 deer, 1 fox"
                string targetSummary = BuildOrderTargetList(designator, orderTargets, orderTargetCells);
                int targetCount = orderTargets.Count + orderTargetCells.Count;
                string designatorLabel = designator?.Label ?? "Order";

                // Use shape-aware formatting for the cells (dimensions when possible)
                string shapeSize = "";
                if (placedCells != null && placedCells.Count > 0)
                {
                    shapeSize = ShapeHelper.FormatShapeSize(placedCells);
                }

                var hints = new List<string>();
                if (targetCount > 0)
                {
                    hints.Add("Page Up/Down to navigate targets");
                }
                hints.Add("Equals to add another shape");
                hints.Add("Minus to undo last");
                hints.Add("Enter to confirm");
                string hintsStr = string.Join(", ", hints);

                // Build the announcement with clear structure
                string mainPart;
                if (!string.IsNullOrEmpty(shapeSize) && !string.IsNullOrEmpty(targetSummary))
                {
                    // "Hunt order on 7 by 4, targeting 2 deer and 1 fox"
                    mainPart = $"{designatorLabel} order on {shapeSize}, targeting {targetSummary}";
                }
                else if (!string.IsNullOrEmpty(targetSummary))
                {
                    // No shape info, just targets: "Hunt order targeting 2 deer and 1 fox"
                    mainPart = $"{designatorLabel} order targeting {targetSummary}";
                }
                else if (!string.IsNullOrEmpty(shapeSize))
                {
                    // Shape but no targets: "Hunt order on 7 by 4"
                    mainPart = $"{designatorLabel} order on {shapeSize}";
                }
                else
                {
                    // Fallback
                    mainPart = $"{totalPlaced} {designatorLabel} designations";
                }

                return $"Viewing mode. {mainPart}{segmentInfo}. {hintsStr}.";
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
                string label = thing.LabelShort ?? thing.def?.label ?? "Unknown";
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
                        label = edifice.LabelShort ?? edifice.def?.label ?? "Unknown";
                    }
                    else
                    {
                        TerrainDef terrain = cell.GetTerrain(map);
                        label = terrain?.label ?? "tile";
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
                    significantParts.Add($"{kvp.Value} {label}");
                }
                else
                {
                    smallGroupCount++;
                    smallGroupItems += kvp.Value;
                }
            }

            // Format list with "and" before last item: "3 oak trees, 2 poplar trees, and 1 birch tree"
            string result;
            if (significantParts.Count == 0)
            {
                result = "";
            }
            else if (significantParts.Count == 1)
            {
                result = significantParts[0];
            }
            else if (significantParts.Count == 2)
            {
                result = $"{significantParts[0]} and {significantParts[1]}";
            }
            else
            {
                result = string.Join(", ", significantParts.Take(significantParts.Count - 1))
                    + ", and " + significantParts[significantParts.Count - 1];
            }

            if (smallGroupCount > 0)
            {
                string itemWord = smallGroupItems == 1 ? "item" : "items";
                string groupWord = smallGroupCount == 1 ? "group" : "groups";
                if (significantParts.Count > 0)
                {
                    result += $", and {smallGroupItems} more {itemWord} in {smallGroupCount} smaller {groupWord}";
                }
                else
                {
                    result = $"{smallGroupItems} {itemWord} in {smallGroupCount} {groupWord}";
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
                    parts.Add($"Removed {sizeString} from zone{segmentInfo}");
                }
                else
                {
                    parts.Add($"No zone cells removed{segmentInfo}");
                }

                parts.Add("Equals to add another shape, Minus to undo last, Enter to confirm");
                return $"Viewing mode. {string.Join(". ", parts)}.";
            }

            // Get zone type label from designator
            string zoneName = GetZoneTypeName(designator, targetZone, createdZones).ToLower();

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
                    blockedPart = $", {obstacleCells.Count} cells blocked by {blockingObstacleSummary}";
                }
                else
                {
                    blockedPart = $", {obstacleCells.Count} cells blocked";
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
                    string zoneWord = actualZoneCount == 1 ? "zone" : "zones";
                    parts.Add($"{actualZoneCount} {zoneName} {zoneWord} created: {string.Join(", ", zoneSizes)}{blockedPart}{segmentInfo}");
                }
                else if (wasZoneExpansion)
                {
                    // Single zone expansion - use lastSegmentCells to announce only the newly added shape
                    // (not all cells from all segments combined)
                    var cellsForSize = lastSegmentCells != null && lastSegmentCells.Count > 0
                        ? lastSegmentCells
                        : placedCells;
                    string sizeString = ShapeHelper.FormatShapeSize(cellsForSize);
                    parts.Add($"{zoneName} zone expanded by {sizeString}{blockedPart}{segmentInfo}");
                }
                else
                {
                    // Single zone creation
                    string sizeString = ShapeHelper.FormatShapeSize(placedCells);
                    parts.Add($"{sizeString} {zoneName} zone created{blockedPart}{segmentInfo}");
                }
            }
            else
            {
                // No cells placed - all blocked
                parts.Add($"No zone cells placed{segmentInfo}");

                if (obstacleCells.Count > 0 && !string.IsNullOrEmpty(blockingObstacleSummary))
                {
                    parts.Add($"All {obstacleCells.Count} cells blocked by {blockingObstacleSummary}");
                }
            }

            // Add control hints
            var hints = new List<string>();
            if (obstacleCells.Count > 0)
            {
                hints.Add("Page Up/Down to navigate obstacles");
            }
            hints.Add("Equals to add another shape");
            hints.Add("Minus to undo last");
            hints.Add("Enter to confirm");
            parts.Add(string.Join(", ", hints));

            return $"Viewing mode. {string.Join(". ", parts)}.";
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
            // label is generic (e.g., "Expand zone") but the zone has a specific type
            if (targetZone != null && !string.IsNullOrEmpty(targetZone.label))
            {
                string zoneLabel = targetZone.label.ToLower();
                if (zoneLabel.Contains("stockpile"))
                    return "Stockpile";
                if (zoneLabel.Contains("growing"))
                    return "Growing";
                if (zoneLabel.Contains("dumping"))
                    return "Dumping";
                if (zoneLabel.Contains("fishing"))
                    return "Fishing";
            }

            // Also check createdZones if targetZone isn't set yet
            if (createdZones != null && createdZones.Count > 0)
            {
                Zone firstZone = createdZones.First();
                if (firstZone != null && !string.IsNullOrEmpty(firstZone.label))
                {
                    string zoneLabel = firstZone.label.ToLower();
                    if (zoneLabel.Contains("stockpile"))
                        return "Stockpile";
                    if (zoneLabel.Contains("growing"))
                        return "Growing";
                    if (zoneLabel.Contains("dumping"))
                        return "Dumping";
                    if (zoneLabel.Contains("fishing"))
                        return "Fishing";
                }
            }

            // Fall back to designator label for zone creation (not expand/shrink)
            if (designator == null)
                return "Zone";

            string label = designator.Label;
            if (!string.IsNullOrEmpty(label))
            {
                // Common patterns: "Create stockpile zone", "Create growing zone"
                // Extract just the zone type
                string lowerLabel = label.ToLower();
                if (lowerLabel.Contains("stockpile"))
                    return "Stockpile";
                if (lowerLabel.Contains("growing"))
                    return "Growing";
                if (lowerLabel.Contains("dumping"))
                    return "Dumping";
                if (lowerLabel.Contains("fishing"))
                    return "Fishing";

                // For expand/shrink operations, the label is just "Expand zone" or "Shrink zone"
                // which doesn't have a zone type - so fall through to generic "Zone"
                if (lowerLabel.Contains("expand") || lowerLabel.Contains("shrink"))
                    return "Zone";

                // Fallback to the label with some cleanup (for zone creation)
                label = label.Replace("Create ", "").Replace(" zone", "");
                if (!string.IsNullOrEmpty(label))
                    return char.ToUpper(label[0]) + label.Substring(1);
            }

            return "Zone";
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
                            label = builtDef.label + " (unbuilt)";
                        }
                    }
                    if (string.IsNullOrEmpty(label))
                    {
                        // Use the thing's category as a last resort
                        label = thing.def?.category.ToString().ToLower() ?? "structure";
                    }
                    return label;
                }
            }

            // Check for existing zone of different type
            Zone existingZone = map.zoneManager.ZoneAt(cell);
            if (existingZone != null)
            {
                return $"existing {existingZone.label}";
            }

            // Check if too close to map edge
            if (cell.InNoZoneEdgeArea(map))
            {
                return TerrainPrefix + "edge area";
            }

            // Check if fogged
            if (cell.Fogged(map))
            {
                return "fog of war";
            }

            // Check terrain for impassable areas
            TerrainDef terrain = cell.GetTerrain(map);
            if (terrain != null && terrain.passability == Traversability.Impassable)
            {
                return TerrainPrefix + (terrain.label ?? "terrain");
            }

            // If we still can't identify the obstacle, report the terrain
            // This catches cases like low fertility terrain blocking growing zones
            if (terrain != null)
            {
                return TerrainPrefix + (terrain.label ?? "unknown terrain");
            }

            // True fallback - should be very rare
            return "blocked cell";
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
                areaName = targetArea?.Label ?? "area";
            }

            // Determine the action verb based on designator type
            string action;
            if (isBuiltInAreaDesignator)
            {
                // For built-in areas: "Marked" for expand, "Unmarked" for clear
                bool isExpanding = ShapeHelper.IsBuiltInAreaExpanding(designator);
                action = isExpanding ? "Marked" : "Unmarked";
            }
            else
            {
                // For allowed areas: "Expanded" or "Cleared"
                action = designator is Designator_AreaAllowedExpand ? "Expanded" : "Cleared";
            }

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
                    // Built-in area format: "Marked 4 by 5 for snow and sand removal" or "Unmarked 4 by 5 from home area"
                    bool isExpanding = ShapeHelper.IsBuiltInAreaExpanding(designator);
                    string preposition = isExpanding ? "for" : "from";
                    parts.Add($"{action} {sizeString} {preposition} {areaName}{segmentInfo}");
                }
                else
                {
                    // Allowed area format: "Expanded Kitchen by 4 by 5"
                    parts.Add($"{action} {areaName} by {sizeString}{segmentInfo}");
                }
            }
            else
            {
                parts.Add($"No area cells {action.ToLower()}{segmentInfo}");
            }

            // Add control hints
            var hints = new List<string>();
            hints.Add("Equals to add another shape");
            hints.Add("Minus to undo last");
            hints.Add("Enter to confirm");
            parts.Add(string.Join(", ", hints));

            return $"Viewing mode. {string.Join(". ", parts)}.";
        }

        /// <summary>
        /// Gets a readable name for a built-in area designator.
        /// </summary>
        private static string GetBuiltInAreaName(Designator designator)
        {
            if (designator == null)
                return "area";

            // Use the designator's label if available, which is usually descriptive
            // e.g., "Expand snow clear area", "Clear home area", "Build roof area"
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

            // Fallback based on type name
            string typeName = designator.GetType().Name;
            if (typeName.Contains("SnowClear") || typeName.Contains("SandClear"))
                return "snow and sand removal";
            if (typeName.Contains("Home"))
                return "home area";
            if (typeName.Contains("BuildRoof"))
                return "build roof area";
            if (typeName.Contains("NoRoof"))
                return "no roof area";
            if (typeName.Contains("IgnoreRoof"))
                return "roof areas";
            if (typeName.Contains("PollutionClear"))
                return "pollution clear area";

            return "area";
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
            string designatorLabel = ArchitectHelper.GetSanitizedLabel(designator, "blueprints");
            string itemType = isBuildDesignator ? "blueprints" : "designations";

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
                    blockedPart = $", {obstacleCells.Count} blocked by {blockingObstacleSummary}";
                }
                else
                {
                    blockedPart = $", {obstacleCells.Count} blocked";
                }
                parts.Add($"Placed {placedSizeStr} of {totalIntended} {pluralLabel}{costInfo}{blockedPart}{segmentInfo}");
            }
            else if (totalPlaced > 0)
            {
                // "Placed 7 by 4 wooden walls (cost)." - uses shape dimensions
                string pluralLabel = totalPlaced > 1
                    ? ArchitectHelper.PluralizePreservingParentheses(designatorLabel, totalPlaced)
                    : designatorLabel;

                string costInfo = GetCostInfo(placedBlueprints);
                parts.Add($"Placed {placedSizeStr} {pluralLabel}{costInfo}{segmentInfo}");
            }
            else
            {
                // No placements at all
                parts.Add($"No {itemType} placed{segmentInfo}");

                if (obstacleCells.Count > 0 && !string.IsNullOrEmpty(blockingObstacleSummary))
                {
                    parts.Add($"All blocked by {blockingObstacleSummary}");
                }
            }

            // Add enclosure info if any enclosures detected
            if (detectedEnclosures != null && detectedEnclosures.Count > 0)
            {
                if (detectedEnclosures.Count == 1)
                {
                    var enc = detectedEnclosures[0];
                    string enclosureSize = ShapeHelper.FormatShapeSize(enc.InteriorCells);
                    string enclosurePart = $"{enclosureSize} enclosure formed";

                    // Add "containing X, Y, Z" if there are interior obstacles
                    if (enc.ObstacleCount > 0 && enc.Obstacles != null)
                    {
                        string interiorSummary = FormatObstacleList(enc.Obstacles);
                        if (!string.IsNullOrEmpty(interiorSummary))
                        {
                            enclosurePart += $" containing {interiorSummary}";
                        }
                    }

                    // Add gap warning at the end
                    if (enc.HasGaps)
                    {
                        string gapWord = enc.GapCount == 1 ? "gap" : "gaps";
                        enclosurePart += $", but has {enc.GapCount} {gapWord}";
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
                        string description = size;

                        // Add this enclosure's obstacles if any
                        if (enc.Obstacles != null && enc.Obstacles.Count > 0)
                        {
                            string contents = FormatObstacleList(enc.Obstacles);
                            if (!string.IsNullOrEmpty(contents))
                            {
                                description += $" containing {contents}";
                            }
                        }

                        enclosureDescriptions.Add(description);
                    }

                    string enclosurePart = $"{detectedEnclosures.Count} enclosures formed: {string.Join(". ", enclosureDescriptions)}";

                    // Add gap warning at the end
                    if (totalGaps > 0)
                    {
                        string gapWord = totalGaps == 1 ? "gap" : "gaps";
                        enclosurePart += $", but {totalGaps} {gapWord} total";
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
                    parts.Add("Disable warning toggle on the tree to build here");
                }
            }

            // Add control hints
            var hints = new List<string>();

            // Obstacle navigation hint
            bool hasAnyObstacles = obstacleCells.Count > 0 || (detectedEnclosures != null && detectedEnclosures.Any(e => e.ObstacleCount > 0));
            if (hasAnyObstacles)
            {
                hints.Add("Page Up/Down to navigate obstacles");
            }

            // Editing hints
            hints.Add("Equals to add another shape");
            hints.Add("Minus to undo last");
            hints.Add("Enter to confirm");

            parts.Add(string.Join(", ", hints));

            return $"Viewing mode. {string.Join(". ", parts)}.";
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
                    return thing.LabelShort ?? thing.def?.label ?? "obstacle";
                }

                // Check for blueprints or frames
                if (thing.def.IsBlueprint || thing.def.IsFrame)
                {
                    return thing.LabelShort ?? "blueprint";
                }

                // Check for items
                if (thing.def.category == ThingCategory.Item)
                {
                    return thing.LabelShort ?? "item";
                }
            }

            // Check terrain - prefix with TERRAIN: for special formatting as "X tiles"
            TerrainDef terrain = cell.GetTerrain(map);
            if (terrain != null && (terrain.passability == Traversability.Impassable || !terrain.affordances?.Contains(TerrainAffordanceDefOf.Light) == true))
            {
                return TerrainPrefix + (terrain.label ?? "terrain");
            }

            return "obstacle";
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
                return $" ({totalCost} {resourceName})";
            }

            return string.Empty;
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
                    label = obstacle.Thing.LabelNoParenthesis ?? obstacle.Thing.def?.label ?? "obstacle";
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
                    label = kvp.Value == 1 ? $"{terrainName} tile" : $"{terrainName} tiles";
                }
                else
                {
                    // Regular items get pluralized
                    label = kvp.Value > 1
                        ? ArchitectHelper.PluralizePreservingParentheses(key, kvp.Value)
                        : key;
                }
                formattedParts.Add($"{kvp.Value} {label}");
            }

            // Add truncated summary if any small groups were skipped
            if (smallGroupCount > 0)
            {
                string groupWord = smallGroupCount == 1 ? "group" : "groups";
                formattedParts.Add($"{smallGroupItems} more in {smallGroupCount} smaller {groupWord}");
            }

            // Join with commas and "and" before the last item
            if (formattedParts.Count == 1)
            {
                return formattedParts[0];
            }
            else if (formattedParts.Count == 2)
            {
                return $"{formattedParts[0]} and {formattedParts[1]}";
            }
            else
            {
                // "X thing1, Y thing2, and Z thing3"
                string allButLast = string.Join(", ", formattedParts.Take(formattedParts.Count - 1));
                return $"{allButLast}, and {formattedParts.Last()}";
            }
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
                    parts.Add($"1 {shapeName}");
                else
                    parts.Add($"{item.Count} {Find.ActiveLanguageWorker.Pluralize(shapeName, item.Count)}");
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
                case ShapeType.Manual: return "manual placement";
                case ShapeType.Line: return "line";
                case ShapeType.AngledLine: return "angled line";
                case ShapeType.FilledRectangle: return "filled rectangle";
                case ShapeType.EmptyRectangle: return "empty rectangle";
                case ShapeType.FilledOval: return "filled oval";
                case ShapeType.EmptyOval: return "empty oval";
                default: return "shape";
            }
        }

        #endregion
    }
}
