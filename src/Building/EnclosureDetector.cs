using System;
using System.Collections.Generic;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Represents an enclosure formed by wall blueprints.
    /// </summary>
    public class Enclosure
    {
        /// <summary>
        /// The interior cells of the enclosure.
        /// </summary>
        public List<IntVec3> InteriorCells { get; set; }

        /// <summary>
        /// Obstacles found inside the enclosure.
        /// </summary>
        public List<ScannerItem> Obstacles { get; set; }

        /// <summary>
        /// Cells where wall placement failed that would have been part of this enclosure's perimeter.
        /// These represent gaps in the wall that prevent the room from being sealed.
        /// </summary>
        public List<IntVec3> GapCells { get; set; }

        /// <summary>
        /// Number of cells in the enclosure.
        /// </summary>
        public int CellCount => InteriorCells?.Count ?? 0;

        /// <summary>
        /// Number of obstacles in the enclosure.
        /// </summary>
        public int ObstacleCount => Obstacles?.Count ?? 0;

        /// <summary>
        /// Number of gaps in the enclosure perimeter (failed wall placements that would connect walls).
        /// </summary>
        public int GapCount => GapCells?.Count ?? 0;

        /// <summary>
        /// Whether this enclosure has gaps that prevent it from being sealed.
        /// </summary>
        public bool HasGaps => GapCount > 0;
    }

    /// <summary>
    /// Detects when wall blueprints form enclosed areas.
    /// Uses flood fill to find interior cells bounded by walls.
    /// </summary>
    public static class EnclosureDetector
    {
        // Performance limit - skip detection for very large areas
        private const int MAX_ENCLOSURE_CELLS = 10000;

        /// <summary>
        /// Detects enclosures formed by wall blueprints combined with existing walls/mountains.
        /// Also identifies gaps in the perimeter caused by failed placements.
        /// </summary>
        /// <param name="blueprints">List of placed blueprints to analyze</param>
        /// <param name="map">The current map</param>
        /// <param name="failedCells">Optional list of cells where wall placement failed (obstacles)</param>
        /// <returns>List of detected enclosures with their interior cells, obstacles, and gaps</returns>
        public static List<Enclosure> DetectEnclosures(List<Thing> blueprints, Map map, List<IntVec3> failedCells = null)
        {
            if (map == null || blueprints == null || blueprints.Count == 0)
                return new List<Enclosure>();

            // Build set of wall blueprint positions
            var wallCells = new HashSet<IntVec3>();
            foreach (Thing blueprint in blueprints)
            {
                if (IsWallBlueprint(blueprint))
                    wallCells.Add(blueprint.Position);
            }

            if (wallCells.Count == 0)
                return new List<Enclosure>();

            // Build set of failed cells for efficient lookup
            var failedCellSet = new HashSet<IntVec3>();
            if (failedCells != null)
            {
                foreach (var cell in failedCells)
                    failedCellSet.Add(cell);
            }

            // Find candidate interior cells (neighbors of walls that aren't walls themselves)
            var candidates = FindCandidateInteriorCells(wallCells, map);

            // Flood fill from each candidate to find enclosures
            var enclosures = new List<Enclosure>();
            var processedCells = new HashSet<IntVec3>();
            var cursorPos = MapNavigationState.CurrentCursorPosition;

            foreach (IntVec3 candidate in candidates)
            {
                if (processedCells.Contains(candidate))
                    continue;

                var (isEnclosed, interiorCells) = TryFloodFill(candidate, wallCells, map);

                // Mark all found cells as processed (whether enclosed or not)
                foreach (var cell in interiorCells)
                    processedCells.Add(cell);

                if (isEnclosed && interiorCells.Count > 0)
                {
                    var obstacles = ObstacleDetector.FindObstacles(map, interiorCells, cursorPos);

                    // Find perimeter gaps (failed cells that would have been part of this enclosure's walls)
                    var gapCells = FindPerimeterGaps(interiorCells, wallCells, failedCellSet, map);

                    enclosures.Add(new Enclosure
                    {
                        InteriorCells = interiorCells,
                        Obstacles = obstacles,
                        GapCells = gapCells
                    });
                }
            }

            return enclosures;
        }

        /// <summary>
        /// Checks if a thing is a wall blueprint or frame.
        /// </summary>
        private static bool IsWallBlueprint(Thing thing)
        {
            if (thing?.def == null)
                return false;

            // Must be a blueprint or frame
            if (!thing.def.IsBlueprint && !thing.def.IsFrame)
                return false;

            // Check what it will become
            if (thing.def.entityDefToBuild is ThingDef thingDef)
            {
                // Explicit wall check
                if (thingDef.building?.isWall == true)
                    return true;

                // Impassable structures also form boundaries
                if (thingDef.passability == Traversability.Impassable)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Finds cells that are adjacent to wall blueprints but aren't walls themselves.
        /// These are candidates for being interior cells of an enclosure.
        /// </summary>
        private static HashSet<IntVec3> FindCandidateInteriorCells(HashSet<IntVec3> wallCells, Map map)
        {
            var candidates = new HashSet<IntVec3>();

            foreach (IntVec3 wallCell in wallCells)
            {
                // Check 4 cardinal neighbors
                foreach (IntVec3 offset in GenAdj.CardinalDirections)
                {
                    IntVec3 neighbor = wallCell + offset;

                    if (!neighbor.InBounds(map))
                        continue;

                    // Skip if also a wall blueprint
                    if (wallCells.Contains(neighbor))
                        continue;

                    // Skip if impassable (mountain, existing wall)
                    if (neighbor.Impassable(map))
                        continue;

                    candidates.Add(neighbor);
                }
            }

            return candidates;
        }

        /// <summary>
        /// Flood-fills from <paramref name="startCell"/> treating existing walls, impassable
        /// terrain, wall blueprints, and wall frames as boundaries. Used by Ctrl+A in shape
        /// placement to pick up rooms that are enclosed by blueprint walls (which RimWorld's
        /// Room system doesn't recognize until the walls are built).
        /// </summary>
        /// <returns>
        /// isEnclosed = true if the fill stayed bounded (did not reach the map edge and did not
        /// exceed the internal size cap). interiorCells contains the cells that were reached.
        /// </returns>
        public static (bool isEnclosed, List<IntVec3> interiorCells) TryFloodFillFromCell(IntVec3 startCell, Map map)
        {
            if (map == null)
                return (false, new List<IntVec3>());

            return TryFloodFill(startCell, new HashSet<IntVec3>(), map);
        }

        /// <summary>
        /// Attempts to flood fill from a start cell to determine if it's enclosed.
        /// Returns whether the area is enclosed and the list of interior cells.
        /// </summary>
        private static (bool isEnclosed, List<IntVec3> cells) TryFloodFill(
            IntVec3 startCell,
            HashSet<IntVec3> wallBlueprintCells,
            Map map)
        {
            var foundCells = new List<IntVec3>();
            bool reachedOpenArea = false;

            // Pass check: determines if a cell can be entered during flood fill
            Predicate<IntVec3> passCheck = (IntVec3 c) =>
            {
                if (!c.InBounds(map))
                    return false;

                // Wall blueprint = boundary (can't pass)
                if (wallBlueprintCells.Contains(c))
                    return false;

                // Impassable terrain (mountain, existing wall) = boundary
                if (c.Impassable(map))
                    return false;

                // Check for existing wall/door buildings or their blueprints/frames at the cell.
                // Doors are wall segments for room-detection purposes even though pawns pass through them,
                // so we treat them (and their blueprints/frames) as enclosure boundaries.
                foreach (Thing thing in c.GetThingList(map))
                {
                    // Existing completed wall or door
                    if (thing is Building && thing.def.building?.isWall == true)
                        return false;
                    if (thing.def.IsDoor)
                        return false;

                    // Wall/door blueprint or frame we might have missed
                    if ((thing.def.IsBlueprint || thing.def.IsFrame) &&
                        thing.def.entityDefToBuild is ThingDef td &&
                        (td.building?.isWall == true || td.IsDoor || td.passability == Traversability.Impassable))
                        return false;
                }

                return true;
            };

            // Cell processor: collects cells and checks for "escape" to open area
            Func<IntVec3, bool> processor = (IntVec3 c) =>
            {
                foundCells.Add(c);

                // If we've collected too many cells, this isn't a meaningful enclosure
                if (foundCells.Count >= MAX_ENCLOSURE_CELLS)
                {
                    reachedOpenArea = true;
                    return true; // Stop the flood fill
                }

                // If we reach the map edge, it's not enclosed
                if (c.x == 0 || c.z == 0 || c.x == map.Size.x - 1 || c.z == map.Size.z - 1)
                {
                    reachedOpenArea = true;
                    return true; // Stop the flood fill
                }

                return false; // Continue filling
            };

            // Run the flood fill
            map.floodFiller.FloodFill(startCell, passCheck, processor);

            bool isEnclosed = !reachedOpenArea && foundCells.Count > 0;
            return (isEnclosed, foundCells);
        }

        /// <summary>
        /// Finds failed cells that would have been part of the enclosure's perimeter.
        /// A failed cell is considered a perimeter gap if it has 2+ adjacent wall blueprint cells
        /// (in cardinal directions), meaning it was intended to connect walls at a corner or along the perimeter.
        /// </summary>
        /// <param name="interiorCells">The interior cells of the enclosure</param>
        /// <param name="wallCells">The set of wall blueprint positions</param>
        /// <param name="failedCells">The set of cells where placement failed</param>
        /// <param name="map">The current map</param>
        /// <returns>List of failed cells that represent gaps in the perimeter</returns>
        private static List<IntVec3> FindPerimeterGaps(
            List<IntVec3> interiorCells,
            HashSet<IntVec3> wallCells,
            HashSet<IntVec3> failedCells,
            Map map)
        {
            var gaps = new List<IntVec3>();

            if (failedCells == null || failedCells.Count == 0)
                return gaps;

            // Build a set of interior cells for efficient lookup
            var interiorSet = new HashSet<IntVec3>(interiorCells);

            // Check each failed cell to see if it would be part of this enclosure's perimeter
            foreach (IntVec3 failedCell in failedCells)
            {
                if (!failedCell.InBounds(map))
                    continue;

                // Count how many wall blueprint cells are adjacent in cardinal directions
                int adjacentWallCount = 0;
                bool isAdjacentToInterior = false;

                foreach (IntVec3 offset in GenAdj.CardinalDirections)
                {
                    IntVec3 neighbor = failedCell + offset;

                    if (wallCells.Contains(neighbor))
                        adjacentWallCount++;

                    if (interiorSet.Contains(neighbor))
                        isAdjacentToInterior = true;
                }

                // A failed cell is a perimeter gap if:
                // 1. It has 2+ adjacent wall blueprints (corner or along-wall position), OR
                // 2. It has 1 adjacent wall AND is adjacent to interior (mid-wall gap)
                // This catches both corner gaps and mid-wall gaps
                if (adjacentWallCount >= 2 || (adjacentWallCount >= 1 && isAdjacentToInterior))
                {
                    gaps.Add(failedCell);
                }
            }

            return gaps;
        }
    }
}
