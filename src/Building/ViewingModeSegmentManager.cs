using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Handles segment stack management and undo functionality for ViewingModeState.
    /// Contains methods for removing segments, tracking items, and managing undo operations
    /// for all designator types (Build, Zone, Orders, Cells).
    /// </summary>
    public static class ViewingModeSegmentManager
    {
        #region Segment Removal

        /// <summary>
        /// Removes items from a single segment (blueprints, zone cells, area cells, or designations).
        /// This is the core removal logic for all segment types.
        /// </summary>
        /// <param name="segmentIndex">Index of the segment to remove, or -1 to process all segments</param>
        /// <param name="segments">List of blueprint segments (for Build designators)</param>
        /// <param name="cellSegments">List of cell segments (for non-Build designators)</param>
        /// <param name="isBuildDesignator">Whether this is a Build designator</param>
        /// <param name="isZoneDesignator">Whether this is a Zone designator</param>
        /// <param name="isAreaDesignator">Whether this is an Area designator (Allowed Areas)</param>
        /// <param name="isBuiltInAreaDesignator">Whether this is a built-in Area designator (Snow/Sand, Roof, Home)</param>
        /// <param name="activeDesignator">The active designator (for designation removal)</param>
        /// <param name="targetZone">The target zone (for zone undo cell count calculation)</param>
        /// <returns>The count of items that were removed</returns>
        public static int RemoveSegmentItems(
            int segmentIndex,
            List<List<Thing>> segments,
            List<List<IntVec3>> cellSegments,
            bool isBuildDesignator,
            bool isZoneDesignator,
            bool isAreaDesignator,
            bool isBuiltInAreaDesignator,
            Designator activeDesignator,
            Zone targetZone)
        {
            int removedCount = 0;
            Map map = Find.CurrentMap;

            // Check for area designator first (areas use AreaUndoTracker, not segments)
            // This includes both Allowed Areas and Built-in Areas (Snow/Sand, Roof, Home)
            if (isAreaDesignator || isBuiltInAreaDesignator)
            {
                removedCount = RemoveAreaSegmentItems(map);
                return removedCount;
            }

            if (isBuildDesignator)
            {
                removedCount = RemoveBuildSegmentItems(segmentIndex, segments);
            }
            else if (isZoneDesignator)
            {
                removedCount = RemoveZoneSegmentItems(segmentIndex, cellSegments, map, targetZone);
            }
            else if (activeDesignator is Designator_Plan_Add)
            {
                // Plan cells live in the PlanManager, not the DesignationManager, so undo removes
                // the placed cells from whatever plan now occupies them (Plan.RemoveCell auto-
                // deregisters a plan once its last cell is gone).
                removedCount = RemovePlanSegmentItems(segmentIndex, cellSegments, map);
            }
            else
            {
                removedCount = RemoveDesignationSegmentItems(segmentIndex, cellSegments, map, activeDesignator);
            }

            return removedCount;
        }

        /// <summary>
        /// Removes blueprint items from Build designator segments.
        /// </summary>
        private static int RemoveBuildSegmentItems(int segmentIndex, List<List<Thing>> segments)
        {
            int removedCount = 0;

            if (segmentIndex >= 0 && segmentIndex < segments.Count)
            {
                // Remove single segment
                var segment = segments[segmentIndex];
                foreach (Thing blueprint in segment)
                {
                    if (blueprint != null && !blueprint.Destroyed)
                    {
                        blueprint.Destroy(DestroyMode.Cancel);
                        removedCount++;
                    }
                }
            }
            else if (segmentIndex == -1)
            {
                // Remove all segments
                foreach (var segment in segments)
                {
                    foreach (Thing blueprint in segment)
                    {
                        if (blueprint != null && !blueprint.Destroyed)
                        {
                            blueprint.Destroy(DestroyMode.Cancel);
                            removedCount++;
                        }
                    }
                }
            }

            return removedCount;
        }

        /// <summary>
        /// Removes zone cells using ZoneUndoTracker to restore previous state.
        /// </summary>
        private static int RemoveZoneSegmentItems(
            int segmentIndex,
            List<List<IntVec3>> cellSegments,
            Map map,
            Zone targetZone)
        {
            int removedCount = 0;

            if (map != null)
            {
                if (segmentIndex >= 0 && segmentIndex < cellSegments.Count)
                {
                    // Get the count from the cell segment - this is what was actually placed
                    // This handles both new zone creation (where LastSegmentTargetZone is null)
                    // and zone expansion (where we want the actual cells placed, not zone diff)
                    removedCount = cellSegments[segmentIndex].Count;

                    // Perform the undo
                    ZoneUndoTracker.UndoLastSegment(map);
                }
                else if (segmentIndex == -1)
                {
                    // For undo all, sum up all cell segments
                    foreach (var segment in cellSegments)
                    {
                        removedCount += segment.Count;
                    }

                    // Perform the undo
                    ZoneUndoTracker.UndoAll(map);
                }
            }

            return removedCount;
        }

        /// <summary>
        /// Removes plan cells placed in one segment (or all segments when segmentIndex is -1) by
        /// taking them back out of the PlanManager. Mirrors how the plan-remove tool works, but
        /// scoped to exactly the cells this placement added.
        /// </summary>
        private static int RemovePlanSegmentItems(
            int segmentIndex,
            List<List<IntVec3>> cellSegments,
            Map map)
        {
            if (map?.planManager == null)
                return 0;

            int removedCount = 0;
            if (segmentIndex >= 0 && segmentIndex < cellSegments.Count)
            {
                removedCount = RemovePlanCells(cellSegments[segmentIndex], map);
            }
            else if (segmentIndex == -1)
            {
                foreach (var segment in cellSegments)
                    removedCount += RemovePlanCells(segment, map);
            }
            return removedCount;
        }

        private static int RemovePlanCells(List<IntVec3> cells, Map map)
        {
            int removedCount = 0;
            foreach (var cell in cells)
            {
                var plan = map.planManager.PlanAt(cell);
                if (plan != null)
                {
                    plan.RemoveCell(cell);
                    removedCount++;
                }
            }
            return removedCount;
        }

        /// <summary>
        /// Removes the last segment of area cell changes using AreaUndoTracker.
        /// </summary>
        private static int RemoveAreaSegmentItems(Map map)
        {
            if (!AreaUndoTracker.HasUndoData)
                return 0;

            return AreaUndoTracker.Undo();
        }

        /// <summary>
        /// Removes designations from DesignationManager for Orders/Cells designators.
        /// Uses OrderUndoTracker to remove actual Designation objects, which correctly
        /// handles both thing-based (Hunt, Haul, Tame) and cell-based (Mine) designators.
        /// </summary>
        private static int RemoveDesignationSegmentItems(
            int segmentIndex,
            List<List<IntVec3>> cellSegments,
            Map map,
            Designator activeDesignator)
        {
            if (map == null)
                return 0;

            if (segmentIndex >= 0)
            {
                // Remove single segment (last one)
                return OrderUndoTracker.UndoLastSegment(map);
            }
            else if (segmentIndex == -1)
            {
                // Remove all segments
                return OrderUndoTracker.UndoAll(map);
            }

            return 0;
        }

        /// <summary>
        /// Removes a designation at a specific cell.
        /// Used for undoing Orders/Cells designations.
        /// </summary>
        /// <param name="cell">The cell to remove designation from</param>
        /// <param name="map">The map</param>
        /// <param name="activeDesignator">The active designator (to get the designation type)</param>
        /// <returns>True if a designation was removed</returns>
        public static bool RemoveDesignationAtCell(IntVec3 cell, Map map, Designator activeDesignator)
        {
            if (map?.designationManager == null)
                return false;

            // Try to get the designation def from the active designator
            DesignationDef designationDef = null;

            // Use reflection to get the protected Designation property from most designator types
            var designationProperty = activeDesignator?.GetType().GetProperty("Designation",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (designationProperty != null)
            {
                designationDef = designationProperty.GetValue(activeDesignator) as DesignationDef;
            }

            if (designationDef != null)
            {
                // Remove designation of the specific type
                Designation existing = map.designationManager.DesignationAt(cell, designationDef);
                if (existing != null)
                {
                    map.designationManager.RemoveDesignation(existing);
                    return true;
                }
            }
            else
            {
                // Fallback: try to remove any designation at the cell
                // This is less precise but handles edge cases
                List<Designation> designations = new List<Designation>(map.designationManager.AllDesignationsAt(cell));
                foreach (var designation in designations)
                {
                    map.designationManager.RemoveDesignation(designation);
                }
                return designations.Count > 0;
            }

            return false;
        }

        #endregion

        #region Item Type Helpers

        /// <summary>
        /// Gets the item type string with proper pluralization based on count.
        /// Returns singular form for count of 1, plural form otherwise.
        /// </summary>
        /// <param name="count">The item count</param>
        /// <param name="isBuildDesignator">Whether this is a Build designator</param>
        /// <param name="isZoneDesignator">Whether this is a Zone designator</param>
        /// <returns>The item type string (e.g., "blueprint", "blueprints", "zone cell", "zone cells")</returns>
        public static string GetItemTypeForCount(int count, bool isBuildDesignator, bool isZoneDesignator)
        {
            if (isBuildDesignator)
                return (count == 1 ? "RimWorldAccess.Building.View.ItemTypeBlueprintOne" : "RimWorldAccess.Building.View.ItemTypeBlueprintMany").Translate();
            else if (isZoneDesignator)
                return (count == 1 ? "RimWorldAccess.Building.View.ItemTypeZoneCellOne" : "RimWorldAccess.Building.View.ItemTypeZoneCellMany").Translate();
            else
                return (count == 1 ? "RimWorldAccess.Building.View.ItemTypeDesignationOne" : "RimWorldAccess.Building.View.ItemTypeDesignationMany").Translate();
        }

        #endregion

        #region Segment Stack Queries

        /// <summary>
        /// Gets the total count of placed items across all segments.
        /// </summary>
        /// <param name="segments">List of blueprint segments (for Build designators)</param>
        /// <param name="cellSegments">List of cell segments (for non-Build designators)</param>
        /// <param name="isBuildDesignator">Whether this is a Build designator</param>
        /// <returns>The total count of placed items</returns>
        public static int GetTotalPlacedCount(
            List<List<Thing>> segments,
            List<List<IntVec3>> cellSegments,
            bool isBuildDesignator)
        {
            if (isBuildDesignator)
            {
                int count = 0;
                foreach (var segment in segments)
                {
                    count += segment.Count;
                }
                return count;
            }
            else
            {
                int count = 0;
                foreach (var segment in cellSegments)
                {
                    count += segment.Count;
                }
                return count;
            }
        }

        /// <summary>
        /// Gets all placed blueprints across all segments.
        /// Only meaningful for Build designators.
        /// </summary>
        /// <param name="segments">List of blueprint segments</param>
        /// <returns>A list of all placed blueprints</returns>
        public static List<Thing> GetAllPlacedBlueprints(List<List<Thing>> segments)
        {
            var all = new List<Thing>();
            foreach (var segment in segments)
            {
                all.AddRange(segment);
            }
            return all;
        }

        /// <summary>
        /// Gets all placed cells across all segments.
        /// For non-Build designators (Orders, Zones, Cells).
        /// </summary>
        /// <param name="cellSegments">List of cell segments</param>
        /// <returns>A list of all placed cells</returns>
        public static List<IntVec3> GetAllPlacedCells(List<List<IntVec3>> cellSegments)
        {
            var all = new List<IntVec3>();
            foreach (var segment in cellSegments)
            {
                all.AddRange(segment);
            }
            return all;
        }

        /// <summary>
        /// Gets the number of segments in the stack.
        /// </summary>
        /// <param name="segments">List of blueprint segments (for Build designators)</param>
        /// <param name="cellSegments">List of cell segments (for non-Build designators)</param>
        /// <param name="isBuildDesignator">Whether this is a Build designator</param>
        /// <returns>The segment count</returns>
        public static int GetSegmentCount(
            List<List<Thing>> segments,
            List<List<IntVec3>> cellSegments,
            bool isBuildDesignator)
        {
            return isBuildDesignator ? segments.Count : cellSegments.Count;
        }

        #endregion
    }
}
