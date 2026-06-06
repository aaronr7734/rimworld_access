using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// State for viewing mode after shape-based placement.
    /// Works for all designator types: Buildings (blueprints), Orders (Hunt, Haul), Zones, and Cells (Mine).
    /// Allows reviewing obstacles and making adjustments.
    /// Supports multi-segment placement with = (add more) and - (remove last).
    ///
    /// Obstacle navigation is delegated to ScannerState via a temporary "Obstacles" category.
    /// This category is a proper scanner category that:
    /// - Can be cycled to using Ctrl+PageUp/Down like other categories
    /// - Supports Page Up/Down to navigate between obstacles
    /// - Uses Home to jump to the current obstacle
    /// - Is automatically removed when exiting viewing mode
    /// </summary>
    public static class ViewingModeState
    {
        private static bool isActive = false;

        // Track segments separately so we can remove just the last one
        // For Build designators: List of Things (blueprints)
        // For Orders/Zones/Cells: List is empty, we use cellSegments instead
        private static List<List<Thing>> segments = new List<List<Thing>>();

        // Track cells per segment for non-Build designators (Orders, Zones, Cells)
        // These use DesignationManager to undo, not Thing.Destroy()
        private static List<List<IntVec3>> cellSegments = new List<List<IntVec3>>();

        // Track shape type per segment (for shape-aware announcements)
        private static List<ShapeType> segmentShapeTypes = new List<ShapeType>();

        // Track obstacle cells for segment logic (knowing which cells failed)
        // Navigation is handled by ScannerState's temporary category.
        // Paired HashSet provides O(1) membership checks during the accumulation loop;
        // without it, dedup is O(n^2) and chokes on full-map shape placements.
        private static List<IntVec3> obstacleCells = new List<IntVec3>();
        private static HashSet<IntVec3> obstacleCellsSet = new HashSet<IntVec3>();

        // Track meditation focus / tree protection across segments
        private static int protectedCount = 0;
        private static HashSet<string> protectedByLabels = new HashSet<string>();

        // Track detected enclosures formed by wall blueprints
        private static List<Enclosure> detectedEnclosures = new List<Enclosure>();

        // Track the number of disconnected regions for zone placements
        private static int detectedRegionCount = 0;

        // Track zones created by zone placement (for accurate confirmation message)
        private static HashSet<Zone> createdZones = new HashSet<Zone>();

        // The specific zone being edited in viewing mode - prevents creating new zones
        private static Zone targetZone = null;

        // Cells that were part of targetZone when entering viewing mode (for re-adding in shrink mode)
        private static HashSet<IntVec3> originalZoneCells = new HashSet<IntVec3>();

        // Track order targets (things that were designated) - for Hunt, Haul, Tame, etc.
        // These are Things (animals, items) that the order designator targeted
        private static List<Thing> orderTargets = new List<Thing>();

        // Track order target cells (cells that were designated) - for Mine, Cancel, etc.
        // These are cells where cell-based orders were placed
        private static List<IntVec3> orderTargetCells = new List<IntVec3>();

        // Store the designator used for placement (for adding new blueprints)
        private static Designator activeDesignator = null;

        // Store the shape type used for placement (for restoring after undo)
        private static ShapeType usedShapeType = ShapeType.Manual;

        // Track if we're temporarily out adding more shapes (don't clear segments on re-entry)
        private static bool isAddingMore = false;

        // Track whether this is a Build designator (for undo behavior)
        private static bool isBuildDesignator = false;

        // Track whether this is an Order designator (Hunt, Mine, Cancel, etc.)
        private static bool isOrderDesignator = false;

        // Track whether this is a Zone designator (Stockpile, Growing, etc.)
        private static bool isZoneDesignator = false;

        // Track whether this is a delete/shrink designator (removes cells, no obstacles possible)
        private static bool isDeleteDesignator = false;

        // Track whether this is an Area designator (Allowed Area expand/shrink)
        private static bool isAreaDesignator = false;

        // Track whether this is a built-in area designator (Snow/Sand, Roof, Home)
        private static bool isBuiltInAreaDesignator = false;

        // Store the target area for area designators
        private static Area targetArea = null;

        // Frame-based timing guard for confirmation - prevents G key from being blocked
        // when it's pressed in the same frame as Enter key completes Confirm()
        private static int confirmationFrame = -1;

        #region Properties

        /// <summary>
        /// Whether viewing mode is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// List of all blueprints placed across all segments.
        /// Only populated for Build designators.
        /// </summary>
        public static List<Thing> PlacedBlueprints
        {
            get
            {
                return ViewingModeSegmentManager.GetAllPlacedBlueprints(segments);
            }
        }

        /// <summary>
        /// List of all cells designated across all segments.
        /// For non-Build designators (Orders, Zones, Cells).
        /// </summary>
        public static List<IntVec3> PlacedCells
        {
            get
            {
                return ViewingModeSegmentManager.GetAllPlacedCells(cellSegments);
            }
        }

        /// <summary>
        /// Total count of placed items (blueprints for Build, cells for others).
        /// </summary>
        public static int PlacedCount
        {
            get
            {
                return ViewingModeSegmentManager.GetTotalPlacedCount(segments, cellSegments, isBuildDesignator);
            }
        }

        /// <summary>
        /// Number of segments placed.
        /// </summary>
        public static int SegmentCount => ViewingModeSegmentManager.GetSegmentCount(segments, cellSegments, isBuildDesignator);

        /// <summary>
        /// List of cells where placement failed due to obstacles.
        /// </summary>
        public static List<IntVec3> ObstacleCells => obstacleCells;

        /// <summary>
        /// List of Things that were designated by order operations (Hunt, Haul, etc.).
        /// </summary>
        public static List<Thing> OrderTargets => orderTargets;

        /// <summary>
        /// List of cells that were designated by cell-based order operations (Mine, Cancel, etc.).
        /// </summary>
        public static List<IntVec3> OrderTargetCells => orderTargetCells;

        /// <summary>
        /// Whether the current designator is an Order type (Hunt, Mine, Cancel, etc.).
        /// </summary>
        public static bool IsOrderDesignator => isOrderDesignator;

        /// <summary>
        /// Whether the current designator is a Zone type (Stockpile, Growing, etc.).
        /// </summary>
        public static bool IsZoneDesignator => isZoneDesignator;

        /// <summary>
        /// Whether the current designator is an Area type (Allowed Area expand/shrink).
        /// </summary>
        public static bool IsAreaDesignator => isAreaDesignator;

        /// <summary>
        /// Whether the current designator is a built-in area type (Snow/Sand, Roof, Home).
        /// </summary>
        public static bool IsBuiltInAreaDesignator => isBuiltInAreaDesignator;

        /// <summary>
        /// The target area for area designators.
        /// </summary>
        public static Area TargetArea => targetArea;

        /// <summary>
        /// Returns true if Confirm() was just called within the last frame.
        /// Used to prevent G key from being blocked when pressed immediately after Enter.
        /// </summary>
        public static bool JustConfirmed => Time.frameCount <= confirmationFrame + 1;

        #endregion

        #region State Management

        /// <summary>
        /// Enters viewing mode with the results from shape placement.
        /// Can be called multiple times to add more segments.
        /// Works for all designator types: Build (blueprints), Orders, Zones, and Cells.
        /// </summary>
        /// <param name="result">The placement result from ShapePlacementState</param>
        /// <param name="designator">The designator used for placement (for adding new items)</param>
        /// <param name="shapeType">The shape type used for placement (for restoring after undo)</param>
        public static void Enter(PlacementResult result, Designator designator = null, ShapeType shapeType = ShapeType.Manual)
        {
            if (result == null)
            {
                Log.Warning("[ViewingModeState] Enter called with null result");
                return;
            }

            // Always update designator type flags from the current designator
            // This ensures correct announcements even when adding more shapes
            isBuildDesignator = ShapeHelper.IsBuildDesignator(designator);
            isOrderDesignator = ShapeHelper.IsOrderDesignator(designator);
            isZoneDesignator = ShapeHelper.IsZoneDesignator(designator);
            isDeleteDesignator = ShapeHelper.IsDeleteDesignator(designator);
            isAreaDesignator = ShapeHelper.IsAreaDesignator(designator);
            isBuiltInAreaDesignator = ShapeHelper.IsBuiltInAreaDesignator(designator);
            if (isAreaDesignator)
            {
                targetArea = Designator_AreaAllowed.selectedArea;
            }
            else if (isBuiltInAreaDesignator)
            {
                // Built-in areas (Snow/Sand, Roof, Home) have fixed Area objects on the map
                targetArea = ShapeHelper.GetBuiltInAreaForDesignator(designator, Find.CurrentMap);
            }

            // First time entering - save cursor and initialize
            // But if we're adding more shapes, keep existing segments
            if (!isActive && !isAddingMore)
            {
                ScannerState.SaveFocus();
                segments.Clear();
                cellSegments.Clear();
                segmentShapeTypes.Clear();
                obstacleCells.Clear();
                obstacleCellsSet.Clear();
                protectedCount = 0;
                protectedByLabels.Clear();
                createdZones.Clear();
                orderTargets.Clear();
                orderTargetCells.Clear();
            }

            // For zone designators, reset zone-specific state when adding more segments
            // Clear targetZone so CollectCreatedZones can set it to the new segment's zone
            // Keep createdZones intact - it accumulates zones across all segments (HashSet handles duplicates)
            // At confirm time, CleanupStaleZoneReferences removes any zones that were undone
            if (isAddingMore && isZoneDesignator)
            {
                targetZone = null;
                originalZoneCells.Clear();
            }

            isAddingMore = false; // Reset the flag

            // Add this placement as a new segment
            // For Build designators, track Things (blueprints)
            // For others, track cells
            if (isBuildDesignator)
            {
                var newSegment = new List<Thing>(result.PlacedBlueprints ?? new List<Thing>());
                segments.Add(newSegment);
            }
            else
            {
                var newCellSegment = new List<IntVec3>(result.PlacedCells ?? new List<IntVec3>());
                cellSegments.Add(newCellSegment);

                // For order designators, collect the targets (things/cells that were designated)
                if (isOrderDesignator && result.PlacedCells != null)
                {
                    CollectOrderTargets(result.PlacedCells, designator);
                }

                // Store order undo segment for undo support
                if (isOrderDesignator && OrderUndoTracker.HasPendingRecord)
                {
                    OrderUndoTracker.AddSegment();
                }
            }

            // Track shape type for this segment
            segmentShapeTypes.Add(shapeType);

            // Add any new obstacles (skip for delete designators since removing cells can't have obstacles)
            if (!isDeleteDesignator && result.ObstacleCells != null)
            {
                foreach (var cell in result.ObstacleCells)
                {
                    if (obstacleCellsSet.Add(cell))
                        obstacleCells.Add(cell);
                }
            }

            // Add protected cells (meditation focus / tree protection) to obstacle list for scanner navigation
            if (!isDeleteDesignator && result.ProtectedCells != null)
            {
                foreach (var cell in result.ProtectedCells)
                {
                    if (obstacleCellsSet.Add(cell))
                        obstacleCells.Add(cell);
                }
                protectedCount += result.ProtectedCount;
                if (result.ProtectedByLabels != null)
                {
                    foreach (string label in result.ProtectedByLabels)
                    {
                        protectedByLabels.Add(label);
                    }
                }
            }

            activeDesignator = designator;
            usedShapeType = shapeType;
            isActive = true;

            // Detect wall enclosures for build designators (do this before UpdateObstacleCategory
            // so interior obstacles can be added to the scanner)
            // Pass obstacleCells so we can detect corner gaps in the perimeter
            detectedEnclosures.Clear();
            if (isBuildDesignator)
            {
                detectedEnclosures = EnclosureDetector.DetectEnclosures(PlacedBlueprints, Find.CurrentMap, obstacleCells);
            }

            // For zone designators, count disconnected regions and collect created zones
            detectedRegionCount = 0;
            if (isZoneDesignator && result.PlacedCells != null && result.PlacedCells.Count > 0)
            {
                // Count how many separate regions the valid cells form
                detectedRegionCount = CountDisconnectedRegions(result.PlacedCells);

                // Collect the actual zones created (for confirm message)
                CollectCreatedZones(result.PlacedCells);

                // Store the zone undo record as a segment (for undo support)
                ZoneUndoTracker.AddSegment();

                // For shrink operations, get targetZone and original cells from ZoneUndoTracker
                // CollectCreatedZones can't find the zone for shrink (cells were removed)
                // Use PreShrinkOriginalCells which persists independently of segments
                if (isDeleteDesignator)
                {
                    targetZone = ZoneUndoTracker.LastSegmentTargetZone;
                    var origCells = ZoneUndoTracker.PreShrinkOriginalCells;
                    if (origCells != null)
                    {
                        originalZoneCells = new HashSet<IntVec3>(origCells);
                    }
                }
                else
                {
                    // For expand operations, get original cells from ZoneUndoTracker
                    // This prevents removing cells that existed before expansion (only newly added cells can be removed)
                    var origCells = ZoneUndoTracker.PreExpandOriginalCells;
                    if (origCells != null)
                    {
                        originalZoneCells = new HashSet<IntVec3>(origCells);
                    }
                }
            }

            // Create temporary scanner category for obstacles or targets
            // Skip for delete designators since they can't have obstacles
            if (isOrderDesignator)
            {
                // For orders, create targets category instead of obstacles
                ViewingModeScannerHelper.UpdateTargetsCategory(orderTargets, orderTargetCells, activeDesignator);
            }
            else if (!isDeleteDesignator)
            {
                // For builds and zone-add, create obstacles category (includes interior obstacles from enclosures)
                ViewingModeScannerHelper.UpdateObstacleCategory(obstacleCells, detectedEnclosures, isZoneDesignator);
            }

            // Clean up any stale zone references before building announcement
            // This removes zones that were deleted via undo
            if (isZoneDesignator)
            {
                CleanupStaleZoneReferences();
            }

            // Get the last segment's cells for expansion announcements
            // For zone expansion, we want to announce only the newly added shape's dimensions
            List<IntVec3> lastSegmentCells = null;
            if (!isBuildDesignator && cellSegments.Count > 0)
            {
                lastSegmentCells = cellSegments[cellSegments.Count - 1];
            }

            // Build the announcement using the announcer
            string announcement = ViewingModeAnnouncer.BuildEntryAnnouncement(
                designator,
                PlacedCount,
                segmentShapeTypes,
                isOrderDesignator,
                isZoneDesignator,
                isBuildDesignator,
                isDeleteDesignator,
                obstacleCells,
                orderTargets,
                orderTargetCells,
                detectedRegionCount,
                detectedEnclosures,
                PlacedCells,
                lastSegmentCells,
                PlacedBlueprints,
                ZoneUndoTracker.WasZoneExpansion,
                targetZone,
                createdZones,
                isAreaDesignator,
                targetArea,
                isBuiltInAreaDesignator,
                protectedCount,
                protectedByLabels);
            TolkHelper.SpeakData(announcement, SpeechPriority.Normal);

            int totalPlaced = PlacedCount;
            int segCount = SegmentCount;
            string itemType = isBuildDesignator ? "blueprints" : "designations";
            Log.Message($"[ViewingModeState] Entered with {result.PlacedCount} new {itemType} (total: {totalPlaced}), {obstacleCells.Count} obstacles, {orderTargets.Count} order targets");
        }

        /// <summary>
        /// Collects the things/cells that were designated by an order designator.
        /// This queries the map to find what was actually designated at each cell.
        /// </summary>
        private static void CollectOrderTargets(List<IntVec3> placedCells, Designator designator)
        {
            Map map = Find.CurrentMap;
            if (map == null || designator == null)
                return;

            foreach (IntVec3 cell in placedCells)
            {
                // Check for thing-based designations (Hunt, Haul, Tame, etc.)
                List<Thing> things = cell.GetThingList(map);
                bool foundThingTarget = false;

                foreach (Thing thing in things)
                {
                    // Check if this thing has any designation on it
                    if (map.designationManager.DesignationOn(thing) != null)
                    {
                        if (!orderTargets.Contains(thing))
                        {
                            orderTargets.Add(thing);
                            foundThingTarget = true;
                        }
                    }
                }

                // If no thing target was found, this might be a cell-based designation (Mine, Cancel, etc.)
                if (!foundThingTarget)
                {
                    // Check if there's any designation at this cell
                    if (map.designationManager.AllDesignationsAt(cell).Any())
                    {
                        if (!orderTargetCells.Contains(cell))
                        {
                            orderTargetCells.Add(cell);
                        }
                    }
                }
            }
        }

        #region Zone Helper Methods

        /// <summary>
        /// Gets the cells belonging to a zone.
        /// Delegates to ZoneEditingHelper.
        /// </summary>
        /// <param name="zone">The zone to get cells from</param>
        /// <returns>A list of cells in the zone</returns>
        private static List<IntVec3> GetZoneCells(Zone zone)
        {
            return ZoneEditingHelper.GetZoneCells(zone);
        }

        #endregion

        /// <summary>
        /// Exits viewing mode and confirms all placements.
        /// Also exits architect/placement mode entirely.
        /// Note: The cursor stays where the user left it - we never move it against
        /// their will when exiting viewing mode.
        /// </summary>
        public static void Confirm()
        {
            if (!isActive)
                return;

            // Set confirmation frame BEFORE Reset() clears isActive
            // This allows JustConfirmed to return true even after IsActive becomes false
            confirmationFrame = Time.frameCount;

            int totalPlaced = PlacedCount;
            string announcement;

            if (isZoneDesignator)
            {
                // Handle delete/shrink designators differently
                if (isDeleteDesignator)
                {
                    announcement = (totalPlaced == 1
                        ? "RimWorldAccess.Building.View.ConfirmZoneCellsRemovedOne"
                        : "RimWorldAccess.Building.View.ConfirmZoneCellsRemovedMany").Translate(totalPlaced);
                }
                else
                {
                    // For zone creation/expansion, report with dimensions or cell count
                    // Clean up any stale zone references before counting
                    CleanupStaleZoneReferences();
                    int zoneCount = createdZones.Count;
                    string zoneName = ViewingModeAnnouncer.GetZoneTypeName(activeDesignator, targetZone, createdZones);

                    // Determine if this was an expansion of existing zone
                    bool wasExpansion = ZoneUndoTracker.WasZoneExpansion;

                    if (zoneCount == 1)
                    {
                        // Single zone - check if cells were manually modified
                        Zone theZone = createdZones.First();
                        int actualCellCount = theZone.Cells.Count();

                        // Get original cell count (0 for new zones, >0 for expansions)
                        int originalCellCount = ZoneUndoTracker.LastSegmentOriginalCells?.Count ?? 0;

                        // Cells that were added by this operation
                        int cellsAdded = actualCellCount - originalCellCount;

                        // Expected cells from shape placement
                        int expectedCellCount = PlacedCells.Count;

                        // Check if cells were manually modified (added/removed via Space key)
                        bool wasModified = cellsAdded != expectedCellCount;

                        if (wasModified)
                        {
                            // Use cell count instead of dimensions since shape was modified
                            if (wasExpansion)
                            {
                                announcement = (cellsAdded == 1
                                    ? "RimWorldAccess.Building.View.ConfirmCellsAddedToZoneOne"
                                    : "RimWorldAccess.Building.View.ConfirmCellsAddedToZoneMany").Translate(cellsAdded, zoneName);
                            }
                            else
                            {
                                announcement = (actualCellCount == 1
                                    ? "RimWorldAccess.Building.View.ConfirmZoneCreatedFromCellsOne"
                                    : "RimWorldAccess.Building.View.ConfirmZoneCreatedFromCellsMany").Translate(actualCellCount, zoneName);
                            }
                        }
                        else
                        {
                            // Use shape-aware size (dimensions for regular, cell count for irregular)
                            string sizeString = ShapeHelper.FormatShapeSize(PlacedCells);
                            announcement = (wasExpansion
                                ? "RimWorldAccess.Building.View.ConfirmZoneExpanded"
                                : "RimWorldAccess.Building.View.ConfirmZoneCreated").Translate(sizeString, zoneName);
                        }
                    }
                    else
                    {
                        // Multiple zones - list sizes for each, sorted by size
                        // Multiple zones are created by splits, so always "created"
                        // Truncate smaller zones (under 1% of total cells)
                        var zoneSizes = new List<(Zone zone, int cellCount, string sizeStr)>();
                        int totalCells = 0;

                        foreach (Zone zone in createdZones)
                        {
                            var zoneCells = GetZoneCells(zone);
                            int cellCount = zoneCells.Count;
                            totalCells += cellCount;
                            string sizeStr = ShapeHelper.FormatShapeSize(zoneCells);
                            zoneSizes.Add((zone, cellCount, sizeStr));
                        }

                        // Sort by cell count descending (largest first)
                        zoneSizes.Sort((a, b) => b.cellCount.CompareTo(a.cellCount));

                        // 1% threshold for truncation
                        int threshold = totalCells / 100;
                        if (threshold < 1)
                            threshold = 1;

                        var significantSizes = new List<string>();
                        int smallZoneCount = 0;

                        foreach (var (zone, cellCount, sizeStr) in zoneSizes)
                        {
                            if (cellCount >= threshold)
                            {
                                significantSizes.Add(sizeStr);
                            }
                            else
                            {
                                smallZoneCount++;
                            }
                        }

                        string sizesPart = string.Join(", ", significantSizes);
                        if (smallZoneCount > 0)
                        {
                            sizesPart += (smallZoneCount == 1
                                ? "RimWorldAccess.Building.View.SmallerZonesOne"
                                : "RimWorldAccess.Building.View.SmallerZonesMany").Translate(smallZoneCount);
                        }

                        announcement = (zoneCount == 1
                            ? "RimWorldAccess.Building.View.ConfirmMultipleZonesCreatedOne"
                            : "RimWorldAccess.Building.View.ConfirmMultipleZonesCreatedMany").Translate(zoneCount, zoneName, sizesPart);
                    }
                }
            }
            else if (isBuildDesignator)
            {
                announcement = "RimWorldAccess.Building.View.OrdersConfirmedBlueprints".Translate(totalPlaced);
            }
            else
            {
                announcement = "RimWorldAccess.Building.View.OrdersConfirmedDesignations".Translate(totalPlaced);
            }

            TolkHelper.SpeakData(announcement, SpeechPriority.Normal);

            // Clear zone undo tracker data since changes are confirmed
            if (isZoneDesignator)
            {
                ZoneUndoTracker.Clear();
            }

            Reset();

            // Check if we need to return to a parent menu (Schedule/Animals)
            if (WindowlessAreaState.HasPendingReturn)
            {
                WindowlessAreaState.CompletePendingReturn();
            }

            // Also exit architect/placement mode entirely
            ShapePlacementState.Reset();
            ArchitectState.Reset();

            Log.Message("[ViewingModeState] Confirmed and exited placement mode");
        }

        /// <summary>
        /// Removes the last segment only and stays in viewing mode.
        /// For Build designators: Destroys blueprints
        /// For Orders/Zones/Cells: Removes designations from DesignationManager
        /// If no segments left, exits viewing mode and returns to placement.
        /// </summary>
        public static void RemoveLastSegment()
        {
            if (!isActive)
                return;

            int currentSegCount = SegmentCount;
            if (currentSegCount == 0)
                return;

            // Capture the shape type before removing (for announcement)
            ShapeType removedShapeType = ShapeType.Manual;
            if (segmentShapeTypes.Count > 0)
            {
                removedShapeType = segmentShapeTypes[segmentShapeTypes.Count - 1];
                segmentShapeTypes.RemoveAt(segmentShapeTypes.Count - 1);
            }

            // Capture the segment cells before removing (for shape-aware size formatting)
            List<IntVec3> removedCells = null;
            if (!isBuildDesignator && cellSegments.Count > 0)
            {
                removedCells = new List<IntVec3>(cellSegments[cellSegments.Count - 1]);
            }

            // Remove the last segment using centralized helper
            int lastIndex = isBuildDesignator ? segments.Count - 1 : cellSegments.Count - 1;
            int removedCount = ViewingModeSegmentManager.RemoveSegmentItems(
                lastIndex, segments, cellSegments, isBuildDesignator, isZoneDesignator, isAreaDesignator, isBuiltInAreaDesignator, activeDesignator, targetZone);

            // Remove the segment from the list
            if (isBuildDesignator)
                segments.RemoveAt(segments.Count - 1);
            else
                cellSegments.RemoveAt(cellSegments.Count - 1);

            // Build the size string - use shape-aware formatting for zones
            string sizeString;
            if (isZoneDesignator && removedCells != null && removedCells.Count > 0)
            {
                string shapeSize = ShapeHelper.FormatShapeSize(removedCells);
                // Add "zone" suffix for clarity (e.g., "3 by 3 zone" or "9 cells zone")
                // But if FormatShapeSize already includes "cells", don't add redundant suffix
                if (shapeSize.Contains("cell"))
                {
                    sizeString = shapeSize.Replace("cells", "zone cells").Replace("cell", "zone cell");
                }
                else
                {
                    sizeString = $"{shapeSize} zone";
                }
            }
            else
            {
                string itemType = ViewingModeSegmentManager.GetItemTypeForCount(removedCount, isBuildDesignator, isZoneDesignator);
                sizeString = $"{removedCount} {itemType}";
            }

            // Get shape display name for the removed segment
            string removedShapeName = ViewingModeAnnouncer.GetShapeDisplayName(removedShapeType);

            // Announce what happened
            // For zone shrink (delete designator), undoing RE-ADDS cells, so say "Restored" not "Removed"
            string action = isDeleteDesignator
                ? (string)"RimWorldAccess.Building.View.ActionRestored".Translate()
                : (string)"RimWorldAccess.Building.View.ActionRemoved".Translate();
            int remainingSegments = SegmentCount;

            if (remainingSegments > 0)
            {
                // Use shape type counts for remaining segments
                string shapeInfo = ViewingModeAnnouncer.FormatShapeTypeCounts(segmentShapeTypes);
                string remainingInfo = !string.IsNullOrEmpty(shapeInfo)
                    ? (string)"RimWorldAccess.Building.View.RemainingInShapeSuffix".Translate(shapeInfo)
                    : (string)"RimWorldAccess.Building.View.RemainingInSegmentsSuffix".Translate(remainingSegments);

                // Format remaining size using shape-aware formatting (dimensions when possible)
                string remainingSizeString;
                if (isZoneDesignator)
                {
                    var remainingCells = PlacedCells;
                    remainingSizeString = ShapeHelper.FormatShapeSize(remainingCells);
                }
                else
                {
                    int remainingCount = PlacedCount;
                    string itemType = ViewingModeSegmentManager.GetItemTypeForCount(remainingCount, isBuildDesignator, isZoneDesignator);
                    remainingSizeString = $"{remainingCount} {itemType}";
                }

                TolkHelper.Speak("RimWorldAccess.Building.View.SegmentRemovedSomeRemain".Loc(action, sizeString, removedShapeName, remainingSizeString, remainingInfo), SpeechPriority.Normal);
            }
            else
            {
                // No segments left - stay in viewing mode, user presses = to add more
                TolkHelper.Speak("RimWorldAccess.Building.View.SegmentRemovedNoneRemain".Loc(action, sizeString, removedShapeName), SpeechPriority.Normal);
            }

            Log.Message($"[ViewingModeState] Removed last segment ({removedCount} items), {remainingSegments} segments remaining");
        }

        /// <summary>
        /// Reactivates viewing mode without resetting state.
        /// Used when returning from shape placement via Escape.
        /// Keeps all segments intact and simply re-enables viewing mode.
        /// </summary>
        public static void Reactivate()
        {
            int segCount = SegmentCount;
            if (segCount == 0)
            {
                Log.Warning("[ViewingModeState] Reactivate called but no segments exist");
                return;
            }

            // Re-activate viewing mode without resetting anything
            isActive = true;
            isAddingMore = false;

            TolkHelper.Speak("RimWorldAccess.Building.View.ReturnedToPreview".Loc(), SpeechPriority.Normal);

            Log.Message($"[ViewingModeState] Reactivated with {segCount} segments");
        }

        /// <summary>
        /// Goes back to shape placement mode to add another segment.
        /// Keeps existing blueprints in place.
        /// </summary>
        public static void AddAnotherShape()
        {
            if (!isActive)
                return;

            Designator savedDesignator = activeDesignator;
            ShapeType savedShape = usedShapeType;

            // Mark that we're adding more - don't clear segments on re-entry
            isAddingMore = true;

            // Exit viewing mode but keep segments intact
            isActive = false;

            TolkHelper.Speak("RimWorldAccess.Building.View.AddAnotherShape".Loc(), SpeechPriority.Normal);

            // Enter shape placement mode with viewing mode on stack (so Escape can return here)
            if (savedDesignator != null && savedShape != ShapeType.Manual)
            {
                ShapePlacementState.Enter(savedDesignator, savedShape, fromViewingMode: true);
            }

            Log.Message("[ViewingModeState] Exited to add another shape");
        }

        /// <summary>
        /// Removes last segment AND returns to placement mode.
        /// This is the Escape key behavior.
        /// Works for all designator types: Build (blueprints), Orders, Zones, and Cells.
        /// </summary>
        public static void UndoLastAndReturn()
        {
            if (!isActive)
                return;

            int removedCount = 0;
            int currentSegCount = SegmentCount;

            // Remove the last segment if there is one
            if (currentSegCount > 0)
            {
                // Remove the last segment using centralized helper
                int lastIndex = isBuildDesignator ? segments.Count - 1 : cellSegments.Count - 1;
                removedCount = ViewingModeSegmentManager.RemoveSegmentItems(
                    lastIndex, segments, cellSegments, isBuildDesignator, isZoneDesignator, isAreaDesignator, isBuiltInAreaDesignator, activeDesignator, targetZone);

                // Remove the segment from the list
                if (isBuildDesignator)
                    segments.RemoveAt(segments.Count - 1);
                else
                    cellSegments.RemoveAt(cellSegments.Count - 1);

                string itemType = ViewingModeSegmentManager.GetItemTypeForCount(removedCount, isBuildDesignator, isZoneDesignator);
                TolkHelper.Speak("RimWorldAccess.Building.View.RemovedItemsReturning".Loc(removedCount, itemType), SpeechPriority.Normal);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Building.View.ReturningToPlacement".Loc(), SpeechPriority.Normal);
            }

            // Save designator and shape before leaving
            Designator savedDesignator = activeDesignator;
            ShapeType savedShape = usedShapeType;

            // If there are still segments, just temporarily exit viewing mode
            // When they come back via Enter, we'll add to existing segments
            int remainingSegments = SegmentCount;
            if (remainingSegments > 0)
            {
                isAddingMore = true; // Keep segments on re-entry
                isActive = false;
            }
            else
            {
                // No segments left, fully reset
                // Note: We do NOT restore cursor position - keep it where the user left it
                Reset();
            }

            // Return to shape placement mode with viewing mode on stack if segments remain
            if (savedDesignator != null && savedShape != ShapeType.Manual)
            {
                bool hasRemainingSegments = SegmentCount > 0;
                ShapePlacementState.Enter(savedDesignator, savedShape, fromViewingMode: hasRemainingSegments);
            }

            Log.Message($"[ViewingModeState] Undid last segment and returned to placement, {SegmentCount} segments remaining");
        }

        /// <summary>
        /// Removes ALL placed items and exits completely.
        /// For Build designators: Destroys blueprints
        /// For Orders/Zones/Cells: Removes designations
        /// </summary>
        public static void UndoAll()
        {
            if (!isActive)
                return;

            // Remove all segments using centralized helper (-1 = all segments)
            int removedCount = ViewingModeSegmentManager.RemoveSegmentItems(
                -1, segments, cellSegments, isBuildDesignator, isZoneDesignator, isAreaDesignator, isBuiltInAreaDesignator, activeDesignator, targetZone);

            string itemType = ViewingModeSegmentManager.GetItemTypeForCount(removedCount, isBuildDesignator, isZoneDesignator);
            TolkHelper.Speak((removedCount == 1
                ? "RimWorldAccess.Building.View.RemovedItems"
                : "RimWorldAccess.Building.View.RemovedAllItems").Loc(removedCount, itemType), SpeechPriority.Normal);

            // Note: We do NOT restore cursor position - keep it where the user left it

            // Save the designator and shape before reset
            Designator savedDesignator = activeDesignator;
            ShapeType savedShape = usedShapeType;

            Reset();

            // Re-enter shape placement mode with the same shape
            if (savedDesignator != null && savedShape != ShapeType.Manual)
            {
                ShapePlacementState.Enter(savedDesignator, savedShape);
            }

            Log.Message($"[ViewingModeState] Undid all {removedCount} items and returned to {savedShape} placement");
        }

        /// <summary>
        /// Shows a confirmation dialog before exiting viewing mode.
        /// "Leave" removes all blueprints/designations/zone changes and exits to game map.
        /// "Stay" closes the dialog and stays in preview mode.
        /// </summary>
        private static void ShowExitConfirmation()
        {
            if (!isActive)
                return;

            // If no segments remain, just exit immediately without showing dialog
            if (SegmentCount == 0)
            {
                // Use appropriate message based on designator type
                if (isZoneDesignator)
                {
                    TolkHelper.Speak("RimWorldAccess.Building.View.ZoneEditingCancelled".Loc(), SpeechPriority.Normal);
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Building.View.PlacementCancelled".Loc(), SpeechPriority.Normal);
                }

                Reset();
                ShapePlacementState.Reset();
                GizmoZoneEditState.Reset();

                // Check if we need to return to a parent menu (Schedule/Animals)
                if (WindowlessAreaState.HasPendingReturn)
                {
                    WindowlessAreaState.CompletePendingReturn();
                }

                // ArchitectState.Reset() returns early if not in architect mode,
                // so explicitly deselect the designator for gizmo mode
                if (ArchitectState.CurrentMode == ArchitectMode.Inactive)
                {
                    Find.DesignatorManager?.Deselect();
                }
                else
                {
                    ArchitectState.Reset();
                }

                Log.Message("[ViewingModeState] Exited via Escape with no segments remaining");
                return;
            }

            // Determine dialog message based on designator type
            string dialogMessage;
            if (isBuildDesignator)
            {
                dialogMessage = "RimWorldAccess.Building.View.LeavePreviewBlueprints".Translate();
            }
            else if (isZoneDesignator)
            {
                dialogMessage = "RimWorldAccess.Building.View.LeavePreviewZones".Translate();
            }
            else
            {
                dialogMessage = "RimWorldAccess.Building.View.LeavePreviewDesignations".Translate();
            }

            Find.WindowStack.Add(new Dialog_MessageBox(
                dialogMessage,
                "RimWorldAccess.Building.View.DialogLeave".Translate(),
                () =>
                {
                    // Remove all segments using centralized helper (-1 = all segments)
                    int removedCount = ViewingModeSegmentManager.RemoveSegmentItems(
                        -1, segments, cellSegments, isBuildDesignator, isZoneDesignator, isAreaDesignator, isBuiltInAreaDesignator, activeDesignator, targetZone);

                    string itemType = ViewingModeSegmentManager.GetItemTypeForCount(removedCount, isBuildDesignator, isZoneDesignator);
                    // For zone shrink (delete designator), undoing RE-ADDS cells, so say "Restored" not "Removed"
                    string action = isDeleteDesignator
                        ? (string)"RimWorldAccess.Building.View.ActionRestored".Translate()
                        : (string)"RimWorldAccess.Building.View.ActionRemoved".Translate();

                    // Use appropriate exit message based on designator type
                    string exitMessage = isZoneDesignator
                        ? (string)"RimWorldAccess.Building.View.ZoneEditingCancelled".Translate()
                        : (string)"RimWorldAccess.Building.View.PlacementCancelled".Translate();
                    string exitKey = removedCount == 1
                        ? "RimWorldAccess.Building.View.ExitWithRemoval"
                        : "RimWorldAccess.Building.View.ExitWithRemovalAll";
                    TolkHelper.Speak(exitKey.Loc(action, removedCount, itemType, exitMessage), SpeechPriority.Normal);

                    // Exit completely (don't restore cursor - keep it where user left it)
                    Reset();

                    // Check if we need to return to a parent menu (Schedule/Animals)
                    if (WindowlessAreaState.HasPendingReturn)
                    {
                        WindowlessAreaState.CompletePendingReturn();
                    }

                    // Also exit architect/placement mode entirely
                    ShapePlacementState.Reset();
                    GizmoZoneEditState.Reset();

                    // ArchitectState.Reset() returns early if not in architect mode,
                    // so explicitly deselect the designator for gizmo mode
                    if (ArchitectState.CurrentMode == ArchitectMode.Inactive)
                    {
                        Find.DesignatorManager?.Deselect();
                    }
                    else
                    {
                        ArchitectState.Reset();
                    }

                    Log.Message($"[ViewingModeState] Exited via confirmation, removed {removedCount} items");
                },
                "RimWorldAccess.Building.View.DialogStay".Translate(),
                null,
                "RimWorldAccess.Building.View.DialogConfirmExit".Translate(),
                false
            ));
        }

        /// <summary>
        /// Resets all state to inactive.
        /// </summary>
        private static void Reset()
        {
            // Clear zone undo tracker data
            ZoneUndoTracker.Clear();

            // Clear order undo tracker data
            OrderUndoTracker.Clear();

            isActive = false;
            isAddingMore = false;
            isBuildDesignator = false;
            isOrderDesignator = false;
            isZoneDesignator = false;
            isDeleteDesignator = false;
            isAreaDesignator = false;
            isBuiltInAreaDesignator = false;
            targetArea = null;
            segments.Clear();
            cellSegments.Clear();
            segmentShapeTypes.Clear();
            obstacleCells.Clear();
            obstacleCellsSet.Clear();
            detectedEnclosures.Clear();
            detectedRegionCount = 0;
            createdZones.Clear();
            targetZone = null;
            originalZoneCells.Clear();
            orderTargets.Clear();
            orderTargetCells.Clear();
            activeDesignator = null;
            usedShapeType = ShapeType.Manual;

            // Clean up scanner temporary category
            ScannerState.RemoveTemporaryCategory();
            ScannerState.RestoreFocus();
        }

        #endregion

        #region Blueprint Management

        /// <summary>
        /// Adds a blueprint at the current map cursor position.
        /// </summary>
        public static void AddBlueprintAtCursor()
        {
            if (!isActive)
                return;

            if (activeDesignator == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.View.NoDesignatorAvailable".Loc(), SpeechPriority.High);
                return;
            }

            if (!GuardHelper.RequireMap(out Map map, SpeechPriority.High)) return;

            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;

            // Check if we can place here
            AcceptanceReport report = activeDesignator.CanDesignateCell(cursorPos);
            if (!report.Accepted)
            {
                string reason = !string.IsNullOrEmpty(report.Reason)
                    ? report.Reason
                    : (string)"RimWorldAccess.Building.View.CannotPlaceHere".Translate();
                TolkHelper.SpeakData(reason, SpeechPriority.Normal);
                return;
            }

            try
            {
                // Track things before placement
                List<Thing> thingsBefore = new List<Thing>(cursorPos.GetThingList(map));

                // Place the blueprint
                activeDesignator.DesignateSingleCell(cursorPos);

                // Call Finalize to play the placement sound (like manual placement does)
                activeDesignator.Finalize(true);

                // Find the newly placed blueprint
                List<Thing> thingsAfter = cursorPos.GetThingList(map);
                foreach (Thing thing in thingsAfter)
                {
                    if (!thingsBefore.Contains(thing) &&
                        (thing.def.IsBlueprint || thing.def.IsFrame))
                    {
                        // Add to the last segment (or create one if needed)
                        if (segments.Count == 0)
                        {
                            segments.Add(new List<Thing>());
                        }
                        segments[segments.Count - 1].Add(thing);
                        break;
                    }
                }

                // Remove from obstacle list if it was there and update scanner category
                if (obstacleCellsSet.Remove(cursorPos))
                {
                    obstacleCells.Remove(cursorPos);
                    ViewingModeScannerHelper.UpdateObstacleCategory(obstacleCells, detectedEnclosures, isZoneDesignator);
                }

                // Announce like manual placement: "{label} placed at x, z"
                string label = activeDesignator.Label ?? (string)"RimWorldAccess.Building.View.BlueprintFallback".Translate();
                TolkHelper.Speak("RimWorldAccess.Building.View.PlacedAt".Loc(label, cursorPos.x, cursorPos.z), SpeechPriority.Normal);
                Log.Message($"[ViewingModeState] Added blueprint at {cursorPos}");
            }
            catch (System.Exception ex)
            {
                Log.Error($"[ViewingModeState] Error adding blueprint: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Building.View.FailedToAddBlueprint".Loc(), SpeechPriority.High);
            }
        }

        /// <summary>
        /// Removes a blueprint at the current map cursor position.
        /// </summary>
        public static void RemoveBlueprintAtCursor()
        {
            if (!isActive)
                return;

            if (!GuardHelper.RequireMap(out Map map, SpeechPriority.High)) return;

            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;

            // Find blueprints at cursor position that we placed
            List<Thing> things = cursorPos.GetThingList(map);
            Thing blueprintToRemove = null;
            var allPlaced = PlacedBlueprints;

            foreach (Thing thing in things)
            {
                if ((thing.def.IsBlueprint || thing.def.IsFrame) && allPlaced.Contains(thing))
                {
                    blueprintToRemove = thing;
                    break;
                }
            }

            if (blueprintToRemove == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.View.NoBlueprintHere".Loc(), SpeechPriority.Normal);
                return;
            }

            // Get the label before destroying
            string thingLabel = blueprintToRemove.LabelShort;

            // Remove the blueprint from whichever segment contains it
            foreach (var segment in segments)
            {
                if (segment.Remove(blueprintToRemove))
                    break;
            }
            blueprintToRemove.Destroy(DestroyMode.Cancel);

            // Play cancel sound and announce like manual placement
            SoundDefOf.Designate_Cancel.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Building.View.CancelledBlueprint".Loc(thingLabel), SpeechPriority.Normal);
            Log.Message($"[ViewingModeState] Removed blueprint at {cursorPos}");
        }

        /// <summary>
        /// Toggles a zone cell at the current cursor position.
        /// Delegates to ZoneEditingHelper for the actual logic.
        /// </summary>
        public static void ToggleZoneCellAtCursor()
        {
            if (!isActive)
                return;

            var result = ZoneEditingHelper.ToggleZoneCellAtCursor(
                ref targetZone,
                activeDesignator,
                createdZones,
                originalZoneCells,
                isDeleteDesignator);

            TolkHelper.SpeakData(result.Message, result.Priority);

            if (result.Success)
            {
                IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
                if (result.ZoneDeleted)
                {
                    Log.Message($"[ViewingModeState] Zone cell operation at {cursorPos}: zone was deleted");
                }
                else
                {
                    Log.Message($"[ViewingModeState] Zone cell operation at {cursorPos}: {result.Message}");
                }
            }
        }

        #endregion

        #region Zone Region Detection

        /// <summary>
        /// Counts the number of disconnected regions among a set of cells using flood fill.
        /// Delegates to ZoneEditingHelper.
        /// </summary>
        /// <param name="cells">The cells to analyze for contiguity</param>
        /// <returns>The number of disconnected regions (1 = fully contiguous, 2+ = will be split)</returns>
        private static int CountDisconnectedRegions(List<IntVec3> cells)
        {
            return ZoneEditingHelper.CountDisconnectedRegions(cells);
        }

        /// <summary>
        /// Collects the unique zones that were created/expanded by zone placement.
        /// Delegates to ZoneEditingHelper.
        /// </summary>
        /// <param name="placedCells">The cells that were designated for zoning</param>
        private static void CollectCreatedZones(List<IntVec3> placedCells)
        {
            ZoneEditingHelper.CollectCreatedZones(placedCells, createdZones, ref targetZone);
        }

        /// <summary>
        /// Removes stale zone references from createdZones.
        /// Delegates to ZoneEditingHelper.
        /// </summary>
        private static void CleanupStaleZoneReferences()
        {
            ZoneEditingHelper.CleanupStaleZoneReferences(createdZones);
        }

        #endregion

        #region Input Handling

        /// <summary>
        /// Processes keyboard input for viewing mode.
        /// </summary>
        /// <param name="key">The key code pressed</param>
        /// <param name="shift">Whether shift is held</param>
        /// <returns>True if the input was handled</returns>
        public static bool HandleInput(KeyCode key, bool shift)
        {
            if (!isActive)
                return false;

            // Note: PageUp/PageDown/Home for obstacle navigation are handled by ScannerState
            // via the temporary "Obstacles" category

            switch (key)
            {
                case KeyCode.Space:
                    // Zone designators use true toggle based on actual zone membership
                    // Space toggles: if cell is in zone, remove it; if not, add it
                    if (isZoneDesignator)
                    {
                        ToggleZoneCellAtCursor();
                    }
                    else
                    {
                        // Build designators and other types use the standard blueprint methods
                        if (shift)
                        {
                            RemoveBlueprintAtCursor();
                        }
                        else
                        {
                            AddBlueprintAtCursor();
                        }
                    }
                    return true;

                case KeyCode.Equals:
                case KeyCode.Plus:
                case KeyCode.KeypadPlus:
                    // Don't handle if Go To is active - let Go To process + for coordinates
                    if (GoToState.IsActive)
                        return false;
                    // = key - add another shape, keep existing blueprints
                    AddAnotherShape();
                    return true;

                case KeyCode.Minus:
                case KeyCode.KeypadMinus:
                    // Don't handle if Go To is active - let Go To process - for coordinates
                    if (GoToState.IsActive)
                        return false;
                    // - key - remove last segment only, stay in viewing mode
                    RemoveLastSegment();
                    return true;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    // Don't intercept Enter if gizmo navigation or windowless float menu is active
                    // They need Enter to execute the selected gizmo or menu option
                    if (GizmoNavigationState.IsActive || WindowlessFloatMenuState.IsActive)
                    {
                        return false;  // Let the active menu handle it
                    }
                    // Enter - finalize all placements
                    Confirm();
                    return true;

                case KeyCode.Escape:
                    // Escape - show confirmation dialog before leaving preview
                    ShowExitConfirmation();
                    return true;

                case KeyCode.Tab:
                    // Block Tab in viewing mode - do nothing, just consume the event
                    // This prevents Tab from opening the architect menu
                    return true;
            }

            return false;
        }

        #endregion
    }
}
