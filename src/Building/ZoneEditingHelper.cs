using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Handles zone manipulation and region detection for ViewingModeState.
    /// Contains methods for toggling zone cells, checking connectivity,
    /// detecting disconnected regions, and managing zone references.
    /// </summary>
    public static class ZoneEditingHelper
    {
        #region Zone Cell Operations

        /// <summary>
        /// Toggles a zone cell at the current cursor position.
        /// If the cell IS part of the zone, removes it (with connectivity check to prevent splits).
        /// If the cell is NOT part of the zone, adds it to the targetZone.
        /// Uses targetZone to prevent creating new zones - cells can only be added adjacent to the target.
        /// </summary>
        /// <param name="targetZone">The target zone being edited</param>
        /// <param name="activeDesignator">The active zone designator</param>
        /// <param name="createdZones">Set of zones that have been created/modified</param>
        /// <param name="originalZoneCells">Original zone cells (for shrink mode re-adding)</param>
        /// <param name="isDeleteDesignator">Whether this is a delete/shrink operation</param>
        /// <returns>A result containing the outcome and any zone reference changes</returns>
        public static ZoneEditResult ToggleZoneCellAtCursor(
            ref Zone targetZone,
            Designator activeDesignator,
            HashSet<Zone> createdZones,
            HashSet<IntVec3> originalZoneCells,
            bool isDeleteDesignator)
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return new ZoneEditResult(false, "RimWorldAccess.Guard.NoMapLoaded".Translate(), SpeechPriority.High);
            }

            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;

            // Check if there's a zone at the cursor position
            Zone zoneAtCursor = map.zoneManager.ZoneAt(cursorPos);

            if (zoneAtCursor != null)
            {
                // Cell IS part of a zone - try to remove it
                return TryRemoveZoneCell(zoneAtCursor, cursorPos, map, ref targetZone, createdZones, originalZoneCells, isDeleteDesignator);
            }
            else
            {
                // Cell is NOT part of a zone - try to add it to targetZone
                return TryAddZoneCell(cursorPos, map, targetZone, activeDesignator, createdZones, originalZoneCells, isDeleteDesignator);
            }
        }

        /// <summary>
        /// Attempts to remove a cell from a zone, checking for connectivity first.
        /// In expand mode, prevents removing cells that existed before expansion.
        /// </summary>
        private static ZoneEditResult TryRemoveZoneCell(
            Zone zoneAtCursor,
            IntVec3 cursorPos,
            Map map,
            ref Zone targetZone,
            HashSet<Zone> createdZones,
            HashSet<IntVec3> originalZoneCells,
            bool isDeleteDesignator)
        {
            // In expand mode (not delete designator), prevent removing cells that existed before expansion
            // Only cells added during this session can be removed
            if (!isDeleteDesignator && originalZoneCells != null && originalZoneCells.Contains(cursorPos))
            {
                return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.CannotRemoveOriginalCell".Translate(), SpeechPriority.Normal);
            }

            // Check if we can safely remove it
            if (WouldDisconnectZone(zoneAtCursor, cursorPos, map))
            {
                return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.WouldDisconnect".Translate(), SpeechPriority.Normal);
            }

            // Safe to remove
            try
            {
                string zoneName = zoneAtCursor.label ?? (string)"RimWorldAccess.Building.Zone.FallbackName".Translate();
                zoneAtCursor.RemoveCell(cursorPos);

                // Check if the zone still exists after removal (RimWorld deletes zones with no cells)
                bool zoneStillExists = map.zoneManager.AllZones.Contains(zoneAtCursor);

                if (zoneStillExists)
                {
                    // Zone still exists - track that we modified it
                    if (!createdZones.Contains(zoneAtCursor))
                    {
                        createdZones.Add(zoneAtCursor);
                    }
                    return new ZoneEditResult(true, "RimWorldAccess.Building.Zone.CellRemoved".Translate(zoneName), SpeechPriority.Normal);
                }
                else
                {
                    // Zone was deleted (had no remaining cells) - remove stale reference
                    createdZones.Remove(zoneAtCursor);

                    // If the deleted zone was our targetZone, pick another from createdZones if available
                    if (targetZone == zoneAtCursor)
                    {
                        targetZone = createdZones.FirstOrDefault();
                    }

                    return new ZoneEditResult(true, "RimWorldAccess.Building.Zone.ZoneDeleted".Translate(zoneName), SpeechPriority.Normal, zoneDeleted: true);
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[ZoneEditingHelper] Error removing zone cell: {ex.Message}");
                return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.FailedToRemoveCell".Translate(), SpeechPriority.Normal);
            }
        }

        /// <summary>
        /// Attempts to add a cell to the target zone.
        /// </summary>
        private static ZoneEditResult TryAddZoneCell(
            IntVec3 cursorPos,
            Map map,
            Zone targetZone,
            Designator activeDesignator,
            HashSet<Zone> createdZones,
            HashSet<IntVec3> originalZoneCells,
            bool isDeleteDesignator)
        {
            // Validate we have a target zone to add to
            if (targetZone == null)
            {
                return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.NoZoneSelectedForEditing".Translate(), SpeechPriority.Normal);
            }

            // Check if cell is already in a different zone
            Zone existingZone = map.zoneManager.ZoneAt(cursorPos);
            if (existingZone != null)
            {
                if (existingZone == targetZone)
                    return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.CellAlreadyInThisZone".Translate(), SpeechPriority.Normal);
                else
                    return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.CellInOtherZone".Translate(existingZone.label), SpeechPriority.Normal);
            }

            // For shrink mode, only allow re-adding cells that were originally in the zone
            // In shrink mode, activeDesignator is Designator_ZoneDelete, so we can't use
            // Designator_ZoneAdd validation - just add the cell directly
            if (isDeleteDesignator)
            {
                if (!originalZoneCells.Contains(cursorPos))
                {
                    return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.CellNotPartOfOriginal".Translate(), SpeechPriority.Normal);
                }

                // Check that the cell is adjacent to the current zone
                // This prevents re-adding disconnected cells that were originally part of the zone
                bool isAdjacentForShrink = false;
                for (int i = 0; i < 4; i++)
                {
                    IntVec3 neighbor = cursorPos + GenAdj.CardinalDirections[i];
                    if (neighbor.InBounds(map) && map.zoneManager.ZoneAt(neighbor) == targetZone)
                    {
                        isAdjacentForShrink = true;
                        break;
                    }
                }
                if (!isAdjacentForShrink)
                {
                    return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.CellMustBeAdjacent".Translate(), SpeechPriority.Normal);
                }

                // All shrink mode checks passed - add the cell directly to targetZone
                try
                {
                    targetZone.AddCell(cursorPos);

                    // Track that we modified this zone
                    if (!createdZones.Contains(targetZone))
                    {
                        createdZones.Add(targetZone);
                    }

                    string zoneName = targetZone.label ?? (string)"RimWorldAccess.Building.Zone.FallbackName".Translate();
                    return new ZoneEditResult(true, "RimWorldAccess.Building.Zone.CellAdded".Translate(zoneName), SpeechPriority.Normal);
                }
                catch (System.Exception ex)
                {
                    Log.Error($"[ZoneEditingHelper] Error adding zone cell in shrink mode: {ex.Message}");
                    return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.FailedToAddCell".Translate(), SpeechPriority.Normal);
                }
            }

            // Non-shrink mode: Validate we have a ZoneAdd designator for expanding
            var zoneDesignator = activeDesignator as Designator_ZoneAdd;
            if (zoneDesignator == null)
            {
                return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.NoZoneAtLocation".Translate(), SpeechPriority.Normal);
            }

            // Check zone-type requirements (soil fertility for growing zones, etc.)
            AcceptanceReport report = zoneDesignator.CanDesignateCell(cursorPos);
            if (!report.Accepted)
            {
                string reason = !string.IsNullOrEmpty(report.Reason) ? report.Reason : (string)"RimWorldAccess.Building.Zone.CannotAddCellHere".Translate();
                return new ZoneEditResult(false, reason, SpeechPriority.Normal);
            }

            // Check contiguity - cell must be cardinal adjacent to targetZone
            bool isAdjacent = false;
            for (int i = 0; i < 4; i++)
            {
                IntVec3 neighbor = cursorPos + GenAdj.CardinalDirections[i];
                if (neighbor.InBounds(map) && map.zoneManager.ZoneAt(neighbor) == targetZone)
                {
                    isAdjacent = true;
                    break;
                }
            }

            if (!isAdjacent)
            {
                return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.CellMustBeAdjacent".Translate(), SpeechPriority.Normal);
            }

            try
            {
                // All checks passed - add the cell directly to targetZone
                targetZone.AddCell(cursorPos);

                // Track that we modified this zone
                if (!createdZones.Contains(targetZone))
                {
                    createdZones.Add(targetZone);
                }

                string zoneName = targetZone.label ?? (string)"RimWorldAccess.Building.Zone.FallbackName".Translate();
                return new ZoneEditResult(true, "RimWorldAccess.Building.Zone.CellAdded".Translate(zoneName), SpeechPriority.Normal);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[ZoneEditingHelper] Error adding zone cell: {ex.Message}");
                return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.FailedToAddCell".Translate(), SpeechPriority.Normal);
            }
        }

        #endregion

        #region Connectivity Detection

        /// <summary>
        /// Checks if removing a cell would disconnect the zone into separate parts.
        /// Uses flood-fill to verify all remaining cells are still reachable from each other.
        /// </summary>
        /// <param name="zone">The zone to check</param>
        /// <param name="cellToRemove">The cell that would be removed</param>
        /// <param name="map">The current map</param>
        /// <returns>True if removing the cell would disconnect the zone, false if safe to remove</returns>
        public static bool WouldDisconnectZone(Zone zone, IntVec3 cellToRemove, Map map)
        {
            // Get all cells except the one we're removing
            var remainingCells = new HashSet<IntVec3>(zone.Cells);
            remainingCells.Remove(cellToRemove);

            if (remainingCells.Count == 0)
                return false; // Removing last cell is fine (zone will be deleted)

            // Flood-fill from first remaining cell to find all connected cells
            var visited = new HashSet<IntVec3>();
            var queue = new Queue<IntVec3>();
            var startCell = remainingCells.First();

            queue.Enqueue(startCell);
            visited.Add(startCell);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                // Check cardinal neighbors (N, E, S, W)
                foreach (var dir in GenAdj.CardinalDirections)
                {
                    var neighbor = current + dir;
                    if (remainingCells.Contains(neighbor) && !visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            // If we couldn't visit all remaining cells, removing this cell would disconnect the zone
            return visited.Count < remainingCells.Count;
        }

        /// <summary>
        /// Counts the number of disconnected regions among a set of cells using flood fill.
        /// This helps determine if zone placement will result in multiple separate zones.
        /// </summary>
        /// <param name="cells">The cells to analyze for contiguity</param>
        /// <returns>The number of disconnected regions (1 = fully contiguous, 2+ = will be split)</returns>
        public static int CountDisconnectedRegions(List<IntVec3> cells)
        {
            if (cells == null || cells.Count == 0)
                return 0;

            // Build a set for efficient lookup
            var remainingCells = new HashSet<IntVec3>(cells);
            int regionCount = 0;

            // Keep flood filling until all cells are processed
            while (remainingCells.Count > 0)
            {
                regionCount++;

                // Start a new region from any remaining cell
                IntVec3 startCell = default;
                foreach (var cell in remainingCells)
                {
                    startCell = cell;
                    break;
                }

                // Flood fill to find all connected cells in this region
                var queue = new Queue<IntVec3>();
                queue.Enqueue(startCell);
                remainingCells.Remove(startCell);

                while (queue.Count > 0)
                {
                    IntVec3 current = queue.Dequeue();

                    // Check all 4 cardinal neighbors
                    foreach (IntVec3 offset in GenAdj.CardinalDirections)
                    {
                        IntVec3 neighbor = current + offset;

                        // If this neighbor is in our remaining cells, it's connected
                        if (remainingCells.Contains(neighbor))
                        {
                            remainingCells.Remove(neighbor);
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }

            return regionCount;
        }

        #endregion

        #region Zone Collection and Cleanup

        /// <summary>
        /// Collects the unique zones that were created/expanded by zone placement.
        /// Call this after DesignateMultiCell to find what zones contain our cells.
        /// Also sets targetZone to the first zone found for editing operations.
        /// </summary>
        /// <param name="placedCells">The cells that were designated for zoning</param>
        /// <param name="createdZones">Set to add found zones to</param>
        /// <param name="targetZone">Reference to set to the first zone found</param>
        public static void CollectCreatedZones(List<IntVec3> placedCells, HashSet<Zone> createdZones, ref Zone targetZone)
        {
            Map map = Find.CurrentMap;
            if (map?.zoneManager == null || placedCells == null)
                return;

            foreach (IntVec3 cell in placedCells)
            {
                Zone zone = map.zoneManager.ZoneAt(cell);
                if (zone != null)
                {
                    createdZones.Add(zone);
                    // Set targetZone to the first zone found (for editing operations)
                    if (targetZone == null)
                    {
                        targetZone = zone;
                    }
                }
            }
        }

        /// <summary>
        /// Removes stale zone references from a zone collection.
        /// RimWorld deletes zones when their last cell is removed, so we need to
        /// clean up our tracking to avoid referencing deleted zones.
        /// </summary>
        /// <param name="createdZones">The set of zones to clean up</param>
        public static void CleanupStaleZoneReferences(HashSet<Zone> createdZones)
        {
            Map map = Find.CurrentMap;
            if (map?.zoneManager == null)
                return;

            // Remove zones that no longer exist in the game
            createdZones.RemoveWhere(zone => !map.zoneManager.AllZones.Contains(zone));
        }

        /// <summary>
        /// Gets the zone at the specified cell position.
        /// </summary>
        /// <param name="cell">The cell position to check</param>
        /// <returns>The zone at the cell, or null if no zone exists there</returns>
        public static Zone GetZoneAtCell(IntVec3 cell)
        {
            Map map = Find.CurrentMap;
            return map?.zoneManager?.ZoneAt(cell);
        }

        /// <summary>
        /// Gets the cells belonging to a zone.
        /// </summary>
        /// <param name="zone">The zone to get cells from</param>
        /// <returns>A list of cells in the zone</returns>
        public static List<IntVec3> GetZoneCells(Zone zone)
        {
            if (zone == null)
                return new List<IntVec3>();

            return zone.Cells.ToList();
        }

        #endregion
    }

    /// <summary>
    /// Result of a zone edit operation (add/remove cell).
    /// </summary>
    public class ZoneEditResult
    {
        /// <summary>Whether the operation was successful.</summary>
        public bool Success { get; }

        /// <summary>Message to announce to the user.</summary>
        public string Message { get; }

        /// <summary>Priority for the announcement.</summary>
        public SpeechPriority Priority { get; }

        /// <summary>Whether a zone was deleted as a result of this operation.</summary>
        public bool ZoneDeleted { get; }

        public ZoneEditResult(bool success, string message, SpeechPriority priority, bool zoneDeleted = false)
        {
            Success = success;
            Message = message;
            Priority = priority;
            ZoneDeleted = zoneDeleted;
        }
    }
}
