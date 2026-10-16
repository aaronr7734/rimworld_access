using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Announcement text for ViewingModeState. Pure formatting, no side effects.</summary>
    public static class ViewingModeAnnouncer
    {
        // Marks a terrain-based obstacle so it formats as "X tiles" rather than pluralizing.
        private const string TerrainPrefix = "TERRAIN:";

        #region Main Entry Points

        /// <summary>
        /// The announcement for entering viewing mode: designated targets for orders, blueprint
        /// count and obstacles for builds.
        /// </summary>
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
            string shapeInfo = FormatShapeTypeCounts(segmentShapeTypes);
            string segmentInfo = !string.IsNullOrEmpty(shapeInfo)
                ? (string)"RimWorldAccess.Building.Announcer.SegmentSuffix".Translate(shapeInfo)
                : "";

            if (isOrderDesignator)
            {
                string targetSummary = BuildOrderTargetList(designator, orderTargets, orderTargetCells);
                int targetCount = orderTargets.Count + orderTargetCells.Count;
                string designatorLabel = designator?.Label ?? (string)"RimWorldAccess.Building.Announcer.OrderFallback".Translate();

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

                string mainPart;
                if (!string.IsNullOrEmpty(shapeSize) && !string.IsNullOrEmpty(targetSummary))
                {
                    mainPart = "RimWorldAccess.Building.Announcer.OrderOnTargeting".Translate(designatorLabel, shapeSize, targetSummary);
                }
                else if (!string.IsNullOrEmpty(targetSummary))
                {
                    mainPart = "RimWorldAccess.Building.Announcer.OrderTargeting".Translate(designatorLabel, targetSummary);
                }
                else if (!string.IsNullOrEmpty(shapeSize))
                {
                    mainPart = "RimWorldAccess.Building.Announcer.OrderOn".Translate(designatorLabel, shapeSize);
                }
                else
                {
                    mainPart = "RimWorldAccess.Building.Announcer.OrderDesignations".Translate(totalPlaced, designatorLabel);
                }

                return BuildViewingModeAnnouncement(new List<string> { mainPart + segmentInfo, hintsStr });
            }
            else if (isAreaDesignator || isBuiltInAreaDesignator)
            {
                return BuildAreaDesignatorAnnouncement(
                    designator, totalPlaced, segmentInfo, placedCells, lastSegmentCells, targetArea, isBuiltInAreaDesignator);
            }
            else if (isZoneDesignator)
            {
                return BuildZoneDesignatorAnnouncement(
                    designator, totalPlaced, segmentInfo, isDeleteDesignator,
                    obstacleCells, detectedRegionCount, placedCells, lastSegmentCells,
                    wasZoneExpansion, targetZone, createdZones);
            }
            else
            {
                return BuildBuildDesignatorAnnouncement(
                    designator, totalPlaced, segmentInfo, isBuildDesignator,
                    obstacleCells, detectedEnclosures, placedBlueprints, placedCells,
                    protectedCount, protectedByLabels);
            }
        }

        /// <summary>
        /// Order targets grouped by type, without the designator prefix. Groups below 1% of the
        /// total collapse into "and N more items in M smaller groups".
        /// </summary>
        public static string BuildOrderTargetList(Designator designator, List<Thing> orderTargets, List<IntVec3> orderTargetCells)
        {
            Map map = Find.CurrentMap;

            var thingCounts = new Dictionary<string, int>();

            foreach (Thing thing in orderTargets)
            {
                // LabelShort drops condition percentages, which would split the groups.
                string label = thing.LabelShort ?? thing.def?.label ?? (string)"RimWorldAccess.Building.Announcer.UnknownTarget".Translate();
                if (thingCounts.ContainsKey(label))
                    thingCounts[label]++;
                else
                    thingCounts[label] = 1;
            }

            if (map != null)
            {
                foreach (IntVec3 cell in orderTargetCells)
                {
                    Thing edifice = cell.GetEdifice(map);
                    string label;

                    if (edifice != null)
                    {
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

            string result = JoinWithAnd(significantParts);

            if (smallGroupCount > 0)
            {
                if (significantParts.Count > 0)
                {
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
        /// The zone-designator announcement: creation or expansion, obstacles, and the split
        /// warning, sized by dimensions rather than cell counts, plus the keyboard hints.
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
            List<string> parts = BuildZoneFactParts(designator, totalPlaced, segmentInfo,
                isDeleteDesignator, obstacleCells, placedCells, lastSegmentCells, wasZoneExpansion,
                targetZone, createdZones);

            if (isDeleteDesignator)
            {
                parts.Add(DefaultEditHints());
            }
            else
            {
                var hints = new List<string>();
                if (obstacleCells.Count > 0)
                {
                    hints.Add("RimWorldAccess.Building.Announcer.HintNavigateObstacles".Translate());
                }
                hints.Add("RimWorldAccess.Building.Announcer.HintAddShape".Translate());
                hints.Add("RimWorldAccess.Building.Announcer.HintUndoLast".Translate());
                hints.Add("RimWorldAccess.Building.Announcer.HintConfirm".Translate());
                parts.Add(JoinHints(hints));
            }

            return BuildViewingModeAnnouncement(parts);
        }

        /// <summary>
        /// The measured facts of a zone gesture on their own — created, expanded or shrunk, at what
        /// size, and what blocked it — with no viewing-mode prefix and no keyboard hints, so a zone
        /// the mouse dragged out speaks the identical sentences.
        /// </summary>
        public static List<string> BuildZoneFactParts(
            Designator designator,
            int totalPlaced,
            string segmentInfo,
            bool isDeleteDesignator,
            List<IntVec3> obstacleCells,
            List<IntVec3> placedCells,
            List<IntVec3> lastSegmentCells,
            bool wasZoneExpansion,
            Zone targetZone,
            HashSet<Zone> createdZones)
        {
            var parts = new List<string>();

            if (isDeleteDesignator)
            {
                if (totalPlaced > 0)
                {
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

                return parts;
            }

            string zoneName = GetZoneTypeName(designator, targetZone, createdZones);

            string blockingObstacleSummary = GetZoneBlockingObstacleSummary(obstacleCells);

            // createdZones is authoritative here; detectedRegionCount is only a fallback.
            int actualZoneCount = createdZones?.Count ?? 0;
            if (actualZoneCount == 0 && totalPlaced > 0)
            {
                actualZoneCount = 1;
            }

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

            if (totalPlaced > 0)
            {
                if (actualZoneCount > 1)
                {
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
                    // Only the newly added shape, not every segment's cells combined.
                    var cellsForSize = lastSegmentCells != null && lastSegmentCells.Count > 0
                        ? lastSegmentCells
                        : placedCells;
                    string sizeString = ShapeHelper.FormatShapeSize(cellsForSize);
                    parts.Add((string)"RimWorldAccess.Building.Announcer.ZoneExpandedBy".Translate(zoneName, sizeString)
                        + blockedPart + segmentInfo);
                }
                else
                {
                    string sizeString = ShapeHelper.FormatShapeSize(placedCells);
                    parts.Add((string)"RimWorldAccess.Building.Announcer.ZoneCreated".Translate(sizeString, zoneName)
                        + blockedPart + segmentInfo);
                }
            }
            else
            {
                parts.Add((string)"RimWorldAccess.Building.Announcer.NoZoneCellsPlaced".Translate() + segmentInfo);

                if (obstacleCells.Count > 0 && !string.IsNullOrEmpty(blockingObstacleSummary))
                {
                    parts.Add((obstacleCells.Count == 1
                        ? "RimWorldAccess.Building.Announcer.AllCellsBlockedByOne"
                        : "RimWorldAccess.Building.Announcer.AllCellsBlockedByMany").Translate(obstacleCells.Count, blockingObstacleSummary));
                }
            }

            return parts;
        }

        /// <summary>
        /// The zone-type noun, classified by runtime type identity and never by parsing
        /// translated labels. Vanilla represents regular and dumping stockpiles alike as
        /// Zone_Stockpile, so that distinction survives only through the creating designator's
        /// type, which exists during creation but not on expand or shrink.
        /// </summary>
        public static string GetZoneTypeName(Designator designator, Zone targetZone, HashSet<Zone> createdZones)
        {
            // Prefer the zone: on expand/shrink the designator itself is generic.
            Zone zone = targetZone ?? createdZones?.FirstOrDefault();
            if (zone != null)
            {
                string typeMatch = ZoneTypeKeyFromZone(zone);
                if (typeMatch != null)
                    return typeMatch;

                // A modded zone type has no type signal; present its own label as-is.
                if (!string.IsNullOrEmpty(zone.label))
                    return zone.label;
            }

            // No zone yet (every cell blocked): the designator's type is the only place
            // dumping vs. regular stockpile is knowable.
            if (designator is Designator_ZoneAddStockpile_Dumping)
                return "RimWorldAccess.Building.Announcer.ZoneType.Dumping".Translate();
            if (designator is Designator_ZoneAddStockpile)
                return "RimWorldAccess.Building.Announcer.ZoneType.Stockpile".Translate();
            if (designator is Designator_ZoneAdd_Growing)
                return "RimWorldAccess.Building.Announcer.ZoneType.Growing".Translate();
            if (designator is Designator_ZoneAdd_Fishing)
                return "RimWorldAccess.Building.Announcer.ZoneType.Fishing".Translate();

            if (!string.IsNullOrEmpty(designator?.Label))
                return designator.Label;

            return "RimWorldAccess.Building.Announcer.ZoneType.Generic".Translate();
        }

        /// <summary>The localized zone-type noun for a zone's runtime type, or null for an unknown kind.</summary>
        private static string ZoneTypeKeyFromZone(Zone zone)
        {
            if (zone is Zone_Stockpile)
                return "RimWorldAccess.Building.Announcer.ZoneType.Stockpile".Translate();
            if (zone is Zone_Growing)
                return "RimWorldAccess.Building.Announcer.ZoneType.Growing".Translate();
            if (zone is Zone_Fishing)
                return "RimWorldAccess.Building.Announcer.ZoneType.Fishing".Translate();
            return null;
        }

        /// <summary>
        /// Zone cells as a size string with "zone" already in the wording, from whole-phrase
        /// keys — splicing the word into ShapeHelper.FormatShapeSize output breaks every
        /// non-English language.
        /// </summary>
        public static string FormatZoneCellsSize(List<IntVec3> cells)
        {
            if (cells == null || cells.Count == 0)
                return "RimWorldAccess.Building.View.RemovedZoneSizeZero".Translate();

            if (cells.Count == 1)
                return "RimWorldAccess.Building.View.RemovedZoneSizeOne".Translate();

            if (ShapeHelper.IsRegularRectangle(cells))
            {
                int minX = cells.Min(c => c.x);
                int maxX = cells.Max(c => c.x);
                int minZ = cells.Min(c => c.z);
                int maxZ = cells.Max(c => c.z);
                int width = maxX - minX + 1;
                int height = maxZ - minZ + 1;
                return "RimWorldAccess.Building.View.RemovedZoneSizeWxH".Translate(width, height);
            }

            return "RimWorldAccess.Building.View.RemovedZoneSizeMany".Translate(cells.Count);
        }

        /// <summary>Obstacles blocking zone placement, grouped by type into a comma list.</summary>
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

        /// <summary>The label for whatever blocks a zone at this cell, per CanOverlapZones.</summary>
        public static string GetZoneObstacleLabel(IntVec3 cell, Map map)
        {
            List<Thing> things = cell.GetThingList(map);
            foreach (Thing thing in things)
            {
                if (thing.def != null && !thing.def.CanOverlapZones)
                {
                    // LabelNoParenthesis drops condition percentages, which would split groups.
                    string label = thing.LabelNoParenthesis;
                    if (string.IsNullOrEmpty(label))
                    {
                        label = thing.def?.label;
                    }
                    if (string.IsNullOrEmpty(label))
                    {
                        if (thing.def?.entityDefToBuild is ThingDef builtDef)
                        {
                            label = "RimWorldAccess.Building.Announcer.UnbuiltSuffix".Translate(builtDef.label);
                        }
                    }
                    if (string.IsNullOrEmpty(label))
                    {
                        // Last resort: the category name is a game enum, not localizable here.
                        label = thing.def?.category.ToString().ToLower()
                            ?? (string)"RimWorldAccess.Building.Announcer.StructureFallback".Translate();
                    }
                    return label;
                }
            }

            Zone existingZone = map.zoneManager.ZoneAt(cell);
            if (existingZone != null)
            {
                return "RimWorldAccess.Building.Announcer.ExistingZone".Translate(existingZone.label);
            }

            if (cell.InNoZoneEdgeArea(map))
            {
                return TerrainPrefix + (string)"RimWorldAccess.Building.Announcer.EdgeArea".Translate();
            }

            if (cell.Fogged(map))
            {
                return "RimWorldAccess.Building.Announcer.FogOfWar".Translate();
            }

            TerrainDef terrain = cell.GetTerrain(map);
            if (terrain != null && terrain.passability == Traversability.Impassable)
            {
                return TerrainPrefix + (terrain.label ?? (string)"RimWorldAccess.Building.Announcer.TerrainFallback".Translate());
            }

            // Unidentified: report the terrain, which catches low-fertility growing blocks.
            if (terrain != null)
            {
                return TerrainPrefix + (terrain.label ?? (string)"RimWorldAccess.Building.Announcer.UnknownTerrain".Translate());
            }

            return "RimWorldAccess.Building.Announcer.BlockedCell".Translate();
        }

        #endregion

        #region Area-Specific Announcements

        /// <summary>
        /// The area-designator announcement for allowed and built-in areas, plus keyboard hints.
        /// </summary>
        public static string BuildAreaDesignatorAnnouncement(
            Designator designator,
            int totalPlaced,
            string segmentInfo,
            List<IntVec3> placedCells,
            List<IntVec3> lastSegmentCells,
            Area targetArea,
            bool isBuiltInAreaDesignator = false)
        {
            List<string> parts = BuildAreaFactParts(designator, totalPlaced, segmentInfo, placedCells,
                lastSegmentCells, targetArea, isBuiltInAreaDesignator);

            parts.Add(DefaultEditHints());

            return BuildViewingModeAnnouncement(parts);
        }

        /// <summary>
        /// The measured facts of an area gesture on their own — marked or cleared, and at what size —
        /// with no viewing-mode prefix and no keyboard hints, so an area the mouse painted speaks the
        /// identical sentences.
        /// </summary>
        public static List<string> BuildAreaFactParts(
            Designator designator,
            int totalPlaced,
            string segmentInfo,
            List<IntVec3> placedCells,
            List<IntVec3> lastSegmentCells,
            Area targetArea,
            bool isBuiltInAreaDesignator = false)
        {
            var parts = new List<string>();

            string areaName;
            if (isBuiltInAreaDesignator)
            {
                areaName = targetArea?.Label ?? GetBuiltInAreaName(designator);
            }
            else
            {
                areaName = targetArea?.Label ?? (string)"RimWorldAccess.Building.Announcer.AreaFallback".Translate();
            }

            bool isExpanding = isBuiltInAreaDesignator
                ? ShapeHelper.IsBuiltInAreaExpanding(designator)
                : designator is Designator_AreaAllowedExpand;

            if (totalPlaced > 0)
            {
                var cellsForSize = lastSegmentCells != null && lastSegmentCells.Count > 0
                    ? lastSegmentCells
                    : placedCells;
                string sizeString = ShapeHelper.FormatShapeSize(cellsForSize);

                if (isBuiltInAreaDesignator)
                {
                    parts.Add((isExpanding
                        ? "RimWorldAccess.Building.Announcer.BuiltInAreaMarked"
                        : "RimWorldAccess.Building.Announcer.BuiltInAreaUnmarked").Translate(sizeString, areaName)
                        + segmentInfo);
                }
                else
                {
                    parts.Add((isExpanding
                        ? "RimWorldAccess.Building.Announcer.AllowedAreaExpanded"
                        : "RimWorldAccess.Building.Announcer.AllowedAreaCleared").Translate(areaName, sizeString)
                        + segmentInfo);
                }
            }
            else
            {
                // Whole-phrase per action: lowercasing a translated verb is not safe.
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

            return parts;
        }

        /// <summary>A readable name for a built-in area designator.</summary>
        private static string GetBuiltInAreaName(Designator designator)
        {
            if (designator == null)
                return "RimWorldAccess.Building.Announcer.AreaFallback".Translate();

            // Classify by type name; parsing the translated label breaks other languages.
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

            // A modded designator gets its own label as-is rather than a guess.
            if (!string.IsNullOrEmpty(designator.Label))
                return designator.Label;

            return "RimWorldAccess.Building.Announcer.AreaFallback".Translate();
        }

        #endregion

        #region Build-Specific Announcements

        /// <summary>
        /// The build-designator announcement — placement, obstacles, enclosure — plus the
        /// keyboard hints.
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
            List<string> parts = BuildPlacementFactParts(designator, totalPlaced, segmentInfo,
                isBuildDesignator, obstacleCells, detectedEnclosures, placedBlueprints, placedCells,
                protectedCount, protectedByLabels);

            // Add control hints
            var hints = new List<string>();

            bool hasAnyObstacles = obstacleCells.Count > 0 || (detectedEnclosures != null && detectedEnclosures.Any(e => e.ObstacleCount > 0));
            if (hasAnyObstacles)
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
        /// The measured facts of a placement on their own: what was placed and at what cost, what
        /// blocked it, the enclosure it formed, and anything meditation protection spared. No
        /// viewing-mode prefix and no keyboard hints, so a placement the mouse pointer completed
        /// speaks the identical sentences without inheriting the keyboard flow's affordances.
        /// </summary>
        public static List<string> BuildPlacementFactParts(
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

            // The sanitized label drops a trailing "...", which would pluralize as "wall...s".
            string designatorLabel = ArchitectHelper.GetSanitizedLabel(designator,
                (string)"RimWorldAccess.Building.Announcer.BlueprintsFallback".Translate());

            int totalIntended = totalPlaced + obstacleCells.Count;

            string blockingObstacleSummary = GetBlockingObstacleSummary(obstacleCells);

            string placedSizeStr = placedCells != null && placedCells.Count > 0
                ? ShapeHelper.FormatShapeSize(placedCells)
                : totalPlaced.ToString();

            if (obstacleCells.Count > 0 && totalPlaced > 0)
            {
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
                string pluralLabel = totalPlaced > 1
                    ? ArchitectHelper.PluralizePreservingParentheses(designatorLabel, totalPlaced)
                    : designatorLabel;

                string costInfo = GetCostInfo(placedBlueprints);
                parts.Add((string)"RimWorldAccess.Building.Place.PlacedBuild".Translate(placedSizeStr, pluralLabel, costInfo)
                    + segmentInfo);
            }
            else
            {
                parts.Add((isBuildDesignator
                    ? "RimWorldAccess.Building.Place.NoBlueprintsPlaced"
                    : "RimWorldAccess.Building.Place.NoDesignationsPlaced").Translate()
                    + segmentInfo);

                if (obstacleCells.Count > 0 && !string.IsNullOrEmpty(blockingObstacleSummary))
                {
                    parts.Add("RimWorldAccess.Building.Announcer.AllBlockedBy".Translate(blockingObstacleSummary));
                }
            }

            if (detectedEnclosures != null && detectedEnclosures.Count > 0)
            {
                if (detectedEnclosures.Count == 1)
                {
                    var enc = detectedEnclosures[0];
                    string enclosureSize = ShapeHelper.FormatShapeSize(enc.InteriorCells);
                    string enclosurePart;

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

                    var enclosureDescriptions = new List<string>();
                    foreach (var enc in detectedEnclosures)
                    {
                        string size = ShapeHelper.FormatShapeSize(enc.InteriorCells);
                        string description;

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

                    if (totalGaps > 0)
                    {
                        enclosurePart += (totalGaps == 1
                            ? "RimWorldAccess.Building.Announcer.EnclosureTotalGapSuffixOne"
                            : "RimWorldAccess.Building.Announcer.EnclosureTotalGapSuffixMany").Translate(totalGaps);
                    }

                    parts.Add(enclosurePart);
                }
            }

            if (protectedCount > 0 && protectedByLabels != null)
            {
                string protectionSummary = MeditationProtectionHelper.FormatShapeSummary(
                    protectedCount, protectedByLabels);
                if (!string.IsNullOrEmpty(protectionSummary))
                {
                    parts.Add(protectionSummary);
                    parts.Add(MeditationProtectionHelper.DisableHint());
                }
            }

            return parts;
        }

        /// <summary>Obstacles that prevented placement, grouped by type into a comma list.</summary>
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

        /// <summary>The obstacle label at a cell; terrain obstacles carry the TERRAIN: prefix.</summary>
        public static string GetObstacleLabel(IntVec3 cell, Map map)
        {
            List<Thing> things = cell.GetThingList(map);
            foreach (Thing thing in things)
            {
                // LabelShort drops condition percentages, which would split the groups.
                if (thing is Building || thing is Pawn)
                {
                    return thing.LabelShort ?? thing.def?.label ?? (string)"RimWorldAccess.Building.Announcer.ObstacleFallback".Translate();
                }

                if (thing.def.IsBlueprint || thing.def.IsFrame)
                {
                    return thing.LabelShort ?? (string)"RimWorldAccess.Building.Announcer.BlueprintObstacle".Translate();
                }

                if (thing.def.category == ThingCategory.Item)
                {
                    return thing.LabelShort ?? (string)"RimWorldAccess.Building.Announcer.ItemObstacle".Translate();
                }
            }

            TerrainDef terrain = cell.GetTerrain(map);
            if (terrain != null && (terrain.passability == Traversability.Impassable || !terrain.affordances?.Contains(TerrainAffordanceDefOf.Light) == true))
            {
                return TerrainPrefix + (terrain.label ?? (string)"RimWorldAccess.Building.Announcer.TerrainFallback".Translate());
            }

            return "RimWorldAccess.Building.Announcer.ObstacleFallback".Translate();
        }

        /// <summary>
        /// The placement's cost suffix, e.g. "(140 wood)" or "(75 steel, 2 components)". Reads
        /// the game's own CostListAdjusted so stuff cost and extra resource costs both appear,
        /// matching what vanilla charges.
        /// </summary>
        public static string GetCostInfo(List<Thing> placedBlueprints)
        {
            if (placedBlueprints == null || placedBlueprints.Count == 0)
                return string.Empty;

            if (!(placedBlueprints[0]?.def?.entityDefToBuild is ThingDef thingDef))
                return string.Empty;

            int placedCount = placedBlueprints.Count;
            // The blueprint's own stuff first: a mouse placement has no architect selection.
            ThingDef stuffDef = (placedBlueprints[0] as IConstructible)?.EntityToBuildStuff()
                ?? (thingDef.MadeFromStuff ? ArchitectState.SelectedMaterial : null);
            List<ThingDefCountClass> costList = thingDef.CostListAdjusted(stuffDef, errorOnNullStuff: false);
            if (costList == null)
                return string.Empty;

            var resourceCosts = new List<(int count, string name)>();
            foreach (ThingDefCountClass cost in costList)
            {
                if (cost?.thingDef == null || cost.count <= 0)
                    continue;
                resourceCosts.Add((cost.count * placedCount, cost.thingDef.label));
            }

            if (resourceCosts.Count == 0)
                return string.Empty;

            if (resourceCosts.Count == 1)
            {
                var (resourceCount, resourceName) = resourceCosts[0];
                return "RimWorldAccess.Building.Place.PlacedCostSuffix".Translate(resourceCount, resourceName);
            }

            var items = new List<string>();
            foreach (var (resourceCount, resourceName) in resourceCosts)
            {
                items.Add("RimWorldAccess.Building.Place.PlacedCostItem".Translate(resourceCount, resourceName));
            }

            return "RimWorldAccess.Building.Place.PlacedCostSuffixMulti".Translate(items.ToCommaList());
        }

        #endregion

        #region Composition Helpers

        /// <summary>Prefixes the parts with "Viewing mode" and joins them; AnnouncementBuilder owns the punctuation.</summary>
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

        /// <summary>Joins control hints into one comma-separated phrase.</summary>
        private static string JoinHints(List<string> hints)
        {
            return string.Join(", ", hints);
        }

        /// <summary>The standard add-shape / undo / confirm hint trio.</summary>
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
        /// Joins phrase fragments through the localized list-conjunction keys (one, two, or
        /// three-or-more), never by splicing a conjunction in.
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

        /// <summary>Counts cells by the label <paramref name="labelGetter"/> returns for each.</summary>
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

        /// <summary>Formats ScannerItem obstacles as a comma list, without truncation.</summary>
        public static string FormatObstacleList(List<ScannerItem> obstacles)
        {
            if (obstacles == null || obstacles.Count == 0)
                return string.Empty;

            var obstacleCounts = new Dictionary<string, int>();

            foreach (var obstacle in obstacles)
            {
                // LabelNoParenthesis drops condition percentages, which would split groups.
                string label;
                if (obstacle.Thing != null)
                {
                    label = obstacle.Thing.LabelNoParenthesis ?? obstacle.Thing.def?.label ?? (string)"RimWorldAccess.Building.Announcer.ObstacleFallback".Translate();
                }
                else
                {
                    label = obstacle.Label;
                }

                if (obstacleCounts.ContainsKey(label))
                    obstacleCounts[label]++;
                else
                    obstacleCounts[label] = 1;
            }

            return FormatCountedList(obstacleCounts, truncate: true);
        }

        /// <summary>
        /// Formats label counts as a comma list, pluralizing above one. TERRAIN:-prefixed labels
        /// become "X terrain tiles" instead. With <paramref name="truncate"/>, groups under 1%
        /// of the total collapse into "and X more in Y smaller groups".
        /// </summary>
        public static string FormatCountedList(Dictionary<string, int> counts, bool truncate = false)
        {
            if (counts == null || counts.Count == 0)
                return string.Empty;

            var formattedParts = new List<string>();
            int smallGroupCount = 0;
            int smallGroupItems = 0;

            int totalCount = counts.Values.Sum();
            int threshold = truncate ? totalCount / 100 : 0;
            if (truncate && threshold < 1)
                threshold = 1; // minimum 1

            foreach (var kvp in counts.OrderByDescending(x => x.Value))
            {
                if (truncate && kvp.Value < threshold)
                {
                    smallGroupCount++;
                    smallGroupItems += kvp.Value;
                    continue;
                }

                string key = kvp.Key;
                string label;

                if (key.StartsWith(TerrainPrefix))
                {
                    string terrainName = key.Substring(TerrainPrefix.Length);
                    formattedParts.Add((kvp.Value == 1
                        ? "RimWorldAccess.Building.Announcer.TerrainTilesOne"
                        : "RimWorldAccess.Building.Announcer.TerrainTilesMany").Translate(kvp.Value, terrainName));
                }
                else
                {
                    label = kvp.Value > 1
                        ? ArchitectHelper.PluralizePreservingParentheses(key, kvp.Value)
                        : key;
                    formattedParts.Add("RimWorldAccess.Building.Announcer.CountLabel".Translate(kvp.Value, label));
                }
            }

            if (smallGroupCount > 0)
            {
                formattedParts.Add((smallGroupCount == 1
                    ? "RimWorldAccess.Building.Announcer.MoreInGroupsOne"
                    : "RimWorldAccess.Building.Announcer.MoreInGroupsMany").Translate(smallGroupItems, smallGroupCount));
            }

            return JoinWithAnd(formattedParts);
        }

        /// <summary>Shape-type counts for multi-segment gestures; empty for a single segment.</summary>
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

        /// <summary>The display name for a shape type.</summary>
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
