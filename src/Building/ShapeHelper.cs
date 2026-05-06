using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Enum representing available shape types for building placement.
    /// Maps to RimWorld's DrawStyle system for cell calculation.
    /// </summary>
    public enum ShapeType
    {
        /// <summary>Single-cell placement (no shape, place one tile at a time)</summary>
        Manual,
        /// <summary>Orthogonal line - horizontal or vertical only</summary>
        Line,
        /// <summary>Diagonal line using Bresenham algorithm</summary>
        AngledLine,
        /// <summary>Filled rectangle - all cells within the rectangle</summary>
        FilledRectangle,
        /// <summary>Empty rectangle - border cells only (for walls)</summary>
        EmptyRectangle,
        /// <summary>Filled oval/ellipse - all cells within the ellipse</summary>
        FilledOval,
        /// <summary>Empty oval/ellipse - border cells only</summary>
        EmptyOval
    }

    /// <summary>
    /// Helper class for shape-based building placement.
    /// Wraps RimWorld's native DrawStyle classes and provides utilities for
    /// calculating cells, detecting obstacles, and managing shape selection.
    /// </summary>
    public static class ShapeHelper
    {
        // Cached DrawStyle instances for each shape type
        private static readonly DrawStyle_Line lineStyle = new DrawStyle_Line();
        private static readonly DrawStyle_AngledLine angledLineStyle = new DrawStyle_AngledLine();
        private static readonly DrawStyle_FilledRectangle filledRectangleStyle = new DrawStyle_FilledRectangle();
        private static readonly DrawStyle_EmptyRectangle emptyRectangleStyle = new DrawStyle_EmptyRectangle();
        private static readonly DrawStyle_FilledOval filledOvalStyle = new DrawStyle_FilledOval();
        private static readonly DrawStyle_EmptyOval emptyOvalStyle = new DrawStyle_EmptyOval();

        // Reusable buffer to avoid allocation during cell calculation
        private static readonly List<IntVec3> cellBuffer = new List<IntVec3>();

        /// <summary>
        /// Mapping from DrawStyle type names to ShapeType enum values.
        /// Used when reading from designator.DrawStyleCategory.styles.
        /// </summary>
        private static readonly Dictionary<Type, ShapeType> drawStyleTypeToShapeType = new Dictionary<Type, ShapeType>
        {
            { typeof(DrawStyle_Line), ShapeType.Line },
            { typeof(DrawStyle_AngledLine), ShapeType.AngledLine },
            { typeof(DrawStyle_FilledRectangle), ShapeType.FilledRectangle },
            { typeof(DrawStyle_EmptyRectangle), ShapeType.EmptyRectangle },
            { typeof(DrawStyle_FilledOval), ShapeType.FilledOval },
            { typeof(DrawStyle_EmptyOval), ShapeType.EmptyOval }
        };

        /// <summary>
        /// Calculates the cells for a shape between two points.
        /// Delegates to RimWorld's native DrawStyle classes.
        /// </summary>
        /// <param name="shape">The shape type to calculate</param>
        /// <param name="origin">The starting corner/point</param>
        /// <param name="target">The ending corner/point</param>
        /// <returns>List of cells that make up the shape</returns>
        public static List<IntVec3> CalculateCells(ShapeType shape, IntVec3 origin, IntVec3 target)
        {
            cellBuffer.Clear();

            switch (shape)
            {
                case ShapeType.Manual:
                    // Manual mode returns just the target cell
                    cellBuffer.Add(target);
                    break;

                case ShapeType.Line:
                    lineStyle.Update(origin, target, cellBuffer);
                    break;

                case ShapeType.AngledLine:
                    angledLineStyle.Update(origin, target, cellBuffer);
                    break;

                case ShapeType.FilledRectangle:
                    filledRectangleStyle.Update(origin, target, cellBuffer);
                    break;

                case ShapeType.EmptyRectangle:
                    emptyRectangleStyle.Update(origin, target, cellBuffer);
                    break;

                case ShapeType.FilledOval:
                    filledOvalStyle.Update(origin, target, cellBuffer);
                    break;

                case ShapeType.EmptyOval:
                    emptyOvalStyle.Update(origin, target, cellBuffer);
                    break;

                default:
                    cellBuffer.Add(target);
                    break;
            }

            // Return a copy to prevent external modification of the buffer
            return new List<IntVec3>(cellBuffer);
        }

        /// <summary>
        /// Gets the available shapes for a designator by reading from its DrawStyleCategory.
        /// Works with any designator that has DrawStyleCategory defined - buildings, orders, zones, etc.
        /// Always includes Manual as the first option, then adds game-defined shapes.
        /// </summary>
        /// <param name="designator">The designator to get shapes for</param>
        /// <returns>List of available shape types - game shapes first, Manual last</returns>
        public static List<ShapeType> GetAvailableShapes(Designator designator)
        {
            var shapes = new List<ShapeType>();

            if (designator != null)
            {
                // Get the DrawStyleCategory from the designator
                // This works for all designator types: Designator_Build, Designator_Hunt, Designator_Mine, Designator_Zone, etc.
                DrawStyleCategoryDef category = designator.DrawStyleCategory;
                if (category != null && category.styles != null && category.styles.Count > 0)
                {
                    // Map each DrawStyleDef to our ShapeType enum
                    foreach (DrawStyleDef styleDef in category.styles)
                    {
                        if (styleDef?.drawStyleType == null)
                            continue;

                        if (drawStyleTypeToShapeType.TryGetValue(styleDef.drawStyleType, out ShapeType shapeType))
                        {
                            if (!shapes.Contains(shapeType))
                            {
                                shapes.Add(shapeType);
                            }
                        }
                    }
                }
            }

            // Always include Manual as the last option - single-cell placement is always valid
            shapes.Add(ShapeType.Manual);

            return shapes;
        }

        /// <summary>
        /// Checks if a designator supports shape-based multi-cell selection.
        /// Returns true for designators that have DrawStyleCategory defined and support DesignateMultiCell or DesignateSingleCell.
        /// </summary>
        /// <param name="designator">The designator to check</param>
        /// <returns>True if the designator supports shapes</returns>
        public static bool SupportsShapes(Designator designator)
        {
            if (designator == null)
                return false;

            var shapes = GetAvailableShapes(designator);
            return shapes.Count > 1; // More than just Manual
        }

        /// <summary>
        /// Checks if a designator is a Build designator (places blueprints).
        /// </summary>
        public static bool IsBuildDesignator(Designator designator)
        {
            return designator is Designator_Build;
        }

        /// <summary>
        /// Checks if a designator is a Place designator (Build or Install).
        /// </summary>
        public static bool IsPlaceDesignator(Designator designator)
        {
            return designator is Designator_Place;
        }

        /// <summary>
        /// Checks if a designator inherits from a type with the given name.
        /// Walks the type hierarchy checking type names.
        /// </summary>
        /// <param name="designator">The designator to check</param>
        /// <param name="typeName">The type name to search for in the hierarchy</param>
        /// <returns>True if the designator's type hierarchy includes the specified type name</returns>
        private static bool InheritsFromTypeName(Designator designator, string typeName)
        {
            if (designator == null)
                return false;

            Type type = designator.GetType();
            while (type != null)
            {
                if (type.Name == typeName)
                    return true;
                type = type.BaseType;
            }
            return false;
        }

        /// <summary>
        /// Checks if a designator is a Zone designator.
        /// </summary>
        public static bool IsZoneDesignator(Designator designator)
        {
            return InheritsFromTypeName(designator, "Designator_Zone");
        }

        /// <summary>
        /// Checks if a designator is a delete/shrink designator (removes cells rather than adding them).
        /// Examples: Designator_ZoneDelete, Designator_ZoneDelete_Shrink
        /// These designators should skip obstacle detection since removing cells can't have obstacles.
        /// </summary>
        public static bool IsDeleteDesignator(Designator designator)
        {
            return InheritsFromTypeName(designator, "Designator_ZoneDelete");
        }

        /// <summary>
        /// Checks if a designator is an Area designator (expand or clear allowed areas).
        /// These require an area to be selected before placement can begin.
        /// Examples: Designator_AreaAllowedExpand, Designator_AreaAllowedClear
        /// </summary>
        public static bool IsAreaDesignator(Designator designator)
        {
            return InheritsFromTypeName(designator, "Designator_AreaAllowed");
        }

        /// <summary>
        /// Checks if a designator is a built-in area designator (Snow/Sand, Roof, Home).
        /// These operate on fixed Area objects from the map's AreaManager.
        /// Examples: Designator_AreaSnowClear, Designator_AreaBuildRoof, Designator_AreaHome
        /// </summary>
        public static bool IsBuiltInAreaDesignator(Designator designator)
        {
            if (designator == null)
                return false;

            // Check for each built-in area type
            return InheritsFromTypeName(designator, "Designator_AreaSnowClear") ||
                   InheritsFromTypeName(designator, "Designator_AreaHome") ||
                   InheritsFromTypeName(designator, "Designator_AreaBuildRoof") ||
                   InheritsFromTypeName(designator, "Designator_AreaNoRoof") ||
                   InheritsFromTypeName(designator, "Designator_AreaIgnoreRoof") ||
                   InheritsFromTypeName(designator, "Designator_AreaPollutionClear");
        }

        /// <summary>
        /// Checks if a designator is any type of area designator (Allowed OR Built-in).
        /// </summary>
        public static bool IsAnyAreaDesignator(Designator designator)
        {
            return IsAreaDesignator(designator) || IsBuiltInAreaDesignator(designator);
        }

        /// <summary>
        /// Checks if a built-in area designator is in "expand" mode (adds cells) vs "clear" mode (removes cells).
        /// </summary>
        public static bool IsBuiltInAreaExpanding(Designator designator)
        {
            if (designator == null)
                return true;

            string typeName = designator.GetType().Name;
            // "Clear" designators remove cells, others add cells
            // Special case: Designator_AreaIgnoreRoof is a "clear" operation (removes from both BuildRoof and NoRoof)
            return !typeName.Contains("Clear") && !typeName.Contains("IgnoreRoof");
        }

        /// <summary>
        /// Gets the Area object for a built-in area designator from the map's AreaManager.
        /// Returns null if not a built-in area designator or no map available.
        /// </summary>
        public static Area GetBuiltInAreaForDesignator(Designator designator, Map map)
        {
            if (designator == null || map?.areaManager == null)
                return null;

            string typeName = designator.GetType().Name;

            if (typeName.Contains("SnowClear") || typeName.Contains("SandClear"))
                return map.areaManager.SnowOrSandClear;
            if (typeName.Contains("AreaHome"))
                return map.areaManager.Home;
            if (typeName.Contains("BuildRoof"))
                return map.areaManager.BuildRoof;
            if (typeName.Contains("NoRoof") || typeName.Contains("IgnoreRoof"))
                return map.areaManager.NoRoof;
            if (typeName.Contains("PollutionClear"))
                return map.areaManager.PollutionClear; // Returns null if Biotech DLC not active

            return null;
        }

        /// <summary>
        /// Checks if a designator is a Cells designator (multi-cell selection like Mine).
        /// </summary>
        public static bool IsCellsDesignator(Designator designator)
        {
            return InheritsFromTypeName(designator, "Designator_Cells");
        }

        /// <summary>
        /// Checks if a designator is an Order designator (Hunt, Haul, etc. - thing-based).
        /// These designate things at cells rather than cells themselves.
        /// </summary>
        public static bool IsOrderDesignator(Designator designator)
        {
            if (designator == null)
                return false;

            // Orders are designators that have DrawStyleCategory but are not Build, Place, Zone, Cells, or Areas
            // They designate Things at cells (like Hunt, Haul, Tame, etc.)
            if (designator is Designator_Place)
                return false;
            if (IsCellsDesignator(designator))
                return false;
            if (IsZoneDesignator(designator))
                return false;
            if (IsAreaDesignator(designator))
                return false;
            if (IsBuiltInAreaDesignator(designator))
                return false;

            // If it has a DrawStyleCategory, it's an order-type designator
            return designator.DrawStyleCategory != null;
        }

        /// <summary>
        /// Gets the default shape for a designator based on its DrawStyleCategory.
        /// Returns Manual if no DrawStyleCategory is defined.
        /// </summary>
        /// <param name="designator">The designator to get the default shape for</param>
        /// <returns>The default shape type for this designator</returns>
        public static ShapeType GetDefaultShape(Designator designator)
        {
            if (designator == null)
                return ShapeType.Manual;

            DrawStyleCategoryDef category = designator.DrawStyleCategory;
            if (category == null || category.styles == null || category.styles.Count == 0)
                return ShapeType.Manual;

            // Get the first style from the category as the default
            DrawStyleDef firstStyle = category.styles[0];
            if (firstStyle?.drawStyleType == null)
                return ShapeType.Manual;

            if (drawStyleTypeToShapeType.TryGetValue(firstStyle.drawStyleType, out ShapeType shapeType))
            {
                return shapeType;
            }

            return ShapeType.Manual;
        }

        /// <summary>
        /// Gets the DrawStyleDef that corresponds to a ShapeType for a given designator.
        /// Used to set the game's SelectedStyle when the user picks a shape.
        /// </summary>
        /// <param name="designator">The designator to search</param>
        /// <param name="shape">The shape type to find</param>
        /// <returns>The corresponding DrawStyleDef, or null if not found</returns>
        public static DrawStyleDef GetDrawStyleDef(Designator designator, ShapeType shape)
        {
            if (designator == null || shape == ShapeType.Manual)
                return null;

            DrawStyleCategoryDef category = designator.DrawStyleCategory;
            if (category == null || category.styles == null)
                return null;

            // Find the type that corresponds to the shape
            Type targetType = null;
            foreach (var kvp in drawStyleTypeToShapeType)
            {
                if (kvp.Value == shape)
                {
                    targetType = kvp.Key;
                    break;
                }
            }

            if (targetType == null)
                return null;

            // Find the DrawStyleDef with matching type
            return category.styles.FirstOrDefault(s => s?.drawStyleType == targetType);
        }

        /// <summary>
        /// Gets a human-readable name for a shape type for screen reader announcements.
        /// </summary>
        /// <param name="shape">The shape type</param>
        /// <returns>Human-readable name</returns>
        public static string GetShapeName(ShapeType shape)
        {
            switch (shape)
            {
                case ShapeType.Manual:          return "RimWorldAccess.Building.Shape.Name.Manual".Translate();
                case ShapeType.Line:            return "RimWorldAccess.Building.Shape.Name.Line".Translate();
                case ShapeType.AngledLine:      return "RimWorldAccess.Building.Shape.Name.AngledLine".Translate();
                case ShapeType.FilledRectangle: return "RimWorldAccess.Building.Shape.Name.FilledRectangle".Translate();
                case ShapeType.EmptyRectangle:  return "RimWorldAccess.Building.Shape.Name.EmptyRectangle".Translate();
                case ShapeType.FilledOval:      return "RimWorldAccess.Building.Shape.Name.FilledOval".Translate();
                case ShapeType.EmptyOval:       return "RimWorldAccess.Building.Shape.Name.EmptyOval".Translate();
                default:                        return shape.ToString();
            }
        }

        /// <summary>
        /// Gets a description explaining how a shape works for screen reader announcements.
        /// </summary>
        /// <param name="shape">The shape type</param>
        /// <returns>Description of how the shape works</returns>
        public static string GetShapeDescription(ShapeType shape)
        {
            switch (shape)
            {
                case ShapeType.Manual:          return "RimWorldAccess.Building.Shape.Desc.Manual".Translate();
                case ShapeType.Line:            return "RimWorldAccess.Building.Shape.Desc.Line".Translate();
                case ShapeType.AngledLine:      return "RimWorldAccess.Building.Shape.Desc.AngledLine".Translate();
                case ShapeType.FilledRectangle: return "RimWorldAccess.Building.Shape.Desc.FilledRectangle".Translate();
                case ShapeType.EmptyRectangle:  return "RimWorldAccess.Building.Shape.Desc.EmptyRectangle".Translate();
                case ShapeType.FilledOval:      return "RimWorldAccess.Building.Shape.Desc.FilledOval".Translate();
                case ShapeType.EmptyOval:       return "RimWorldAccess.Building.Shape.Desc.EmptyOval".Translate();
                default:                        return string.Empty;
            }
        }

        /// <summary>
        /// Gets the shape name from a DrawStyleDef's label if available,
        /// otherwise falls back to the shape type name.
        /// </summary>
        /// <param name="styleDef">The DrawStyleDef to get the name from</param>
        /// <returns>Human-readable name</returns>
        public static string GetShapeName(DrawStyleDef styleDef)
        {
            if (styleDef == null)
                return "RimWorldAccess.Building.Shape.Name.Manual".Translate();

            // Use the game's localized label if available
            if (!string.IsNullOrEmpty(styleDef.label))
                return styleDef.LabelCap;

            // Fall back to our shape type name
            if (styleDef.drawStyleType != null &&
                drawStyleTypeToShapeType.TryGetValue(styleDef.drawStyleType, out ShapeType shapeType))
            {
                return GetShapeName(shapeType);
            }

            return styleDef.defName ?? (string)"RimWorldAccess.Building.Shape.Name.Unknown".Translate();
        }

        /// <summary>
        /// Converts a DrawStyleDef to a ShapeType.
        /// </summary>
        /// <param name="styleDef">The DrawStyleDef to convert</param>
        /// <returns>The corresponding ShapeType, or Manual if not found</returns>
        public static ShapeType DrawStyleDefToShapeType(DrawStyleDef styleDef)
        {
            if (styleDef?.drawStyleType == null)
                return ShapeType.Manual;

            if (drawStyleTypeToShapeType.TryGetValue(styleDef.drawStyleType, out ShapeType shapeType))
            {
                return shapeType;
            }

            return ShapeType.Manual;
        }

        /// <summary>
        /// Finds the next obstacle (wall, blueprint, or impassable terrain) in a given direction.
        /// Returns the cell one tile BEFORE the obstacle so the user lands on an interior tile.
        /// </summary>
        /// <param name="start">The starting position</param>
        /// <param name="direction">The direction to search (North, East, South, West)</param>
        /// <param name="map">The map to search on</param>
        /// <returns>The cell before the obstacle, or null if no obstacle found before map edge</returns>
        public static IntVec3? FindNextObstacle(IntVec3 start, Rot4 direction, Map map)
        {
            if (map == null)
                return null;

            // Get the direction offset
            IntVec3 offset = direction.FacingCell;

            IntVec3 current = start;
            IntVec3? lastValidCell = null;

            // Maximum distance to search (map diagonal)
            int maxDistance = Mathf.Max(map.Size.x, map.Size.z);

            for (int i = 1; i <= maxDistance; i++)
            {
                IntVec3 nextCell = start + (offset * i);

                // Check if we've reached the map edge
                if (!nextCell.InBounds(map))
                {
                    // Return the last valid cell (one before map edge)
                    return lastValidCell;
                }

                // Check for obstacles
                if (IsObstacle(nextCell, map))
                {
                    // Return the cell before the obstacle
                    // If we're at distance 1 and it's an obstacle, return null (can't move)
                    if (i == 1)
                        return null;

                    return start + (offset * (i - 1));
                }

                lastValidCell = nextCell;
            }

            // No obstacle found, return the last valid cell
            return lastValidCell;
        }

        /// <summary>
        /// Checks if a cell contains an obstacle (wall, blueprint, or impassable terrain/thing).
        /// </summary>
        /// <param name="cell">The cell to check</param>
        /// <param name="map">The map the cell is on</param>
        /// <returns>True if the cell contains an obstacle</returns>
        public static bool IsObstacle(IntVec3 cell, Map map)
        {
            if (map == null || !cell.InBounds(map))
                return true;

            // Check for impassable things (walls, buildings, etc.)
            if (cell.Impassable(map))
                return true;

            // Check for blueprints
            List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing == null)
                    continue;

                // Check for blueprints
                if (thing.def.IsBlueprint)
                    return true;

                // Check for frames (construction in progress)
                if (thing.def.IsFrame)
                    return true;
            }

            // Check for impassable terrain
            TerrainDef terrain = cell.GetTerrain(map);
            if (terrain != null && terrain.passability == Traversability.Impassable)
                return true;

            return false;
        }

        /// <summary>
        /// Gets the dimensions of a shape between two points.
        /// </summary>
        /// <param name="origin">The first corner</param>
        /// <param name="target">The second corner</param>
        /// <returns>Tuple of (width, height)</returns>
        public static (int width, int height) GetDimensions(IntVec3 origin, IntVec3 target)
        {
            CellRect rect = CellRect.FromLimits(origin, target);
            return (rect.Width, rect.Height);
        }

        /// <summary>
        /// Formats the dimensions as a string for announcement.
        /// Always returns dimensions in "W by H" format.
        /// </summary>
        /// <param name="origin">The first corner</param>
        /// <param name="target">The second corner</param>
        /// <returns>Formatted string like "8 by 8"</returns>
        public static string FormatDimensions(IntVec3 origin, IntVec3 target)
        {
            var (width, height) = GetDimensions(origin, target);
            return $"{width} by {height}";
        }

        /// <summary>
        /// Checks if the cells form a regular rectangle (no holes or gaps).
        /// A regular rectangle has cell count equal to bounding box area.
        /// </summary>
        public static bool IsRegularRectangle(IEnumerable<IntVec3> cells)
        {
            if (cells == null || !cells.Any())
                return false;

            int minX = cells.Min(c => c.x);
            int maxX = cells.Max(c => c.x);
            int minZ = cells.Min(c => c.z);
            int maxZ = cells.Max(c => c.z);

            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;
            int expectedCount = width * height;
            int actualCount = cells.Count();

            return actualCount == expectedCount;
        }

        /// <summary>
        /// Formats shape size for announcements.
        /// Uses "W by H" for regular rectangles, "N cells" for irregular shapes.
        /// </summary>
        public static string FormatShapeSize(IEnumerable<IntVec3> cells)
        {
            if (cells == null || !cells.Any())
                return "RimWorldAccess.Building.Shape.SizeZero".Translate();

            int count = cells.Count();

            if (count == 1)
                return "RimWorldAccess.Building.Shape.SizeOne".Translate();

            if (IsRegularRectangle(cells))
            {
                int minX = cells.Min(c => c.x);
                int maxX = cells.Max(c => c.x);
                int minZ = cells.Min(c => c.z);
                int maxZ = cells.Max(c => c.z);

                int width = maxX - minX + 1;
                int height = maxZ - minZ + 1;

                return "RimWorldAccess.Building.Shape.SizeWxH".Translate(width, height);
            }

            return "RimWorldAccess.Building.Shape.SizeMany".Translate(count);
        }

        /// <summary>
        /// Formats shape size from two corner points.
        /// Always uses dimensions since we don't know actual cells yet during drag.
        /// </summary>
        public static string FormatShapeSizeFromCorners(IntVec3 corner1, IntVec3 corner2)
        {
            var (width, height) = GetDimensions(corner1, corner2);
            return "RimWorldAccess.Building.Shape.SizeWxH".Translate(width, height);
        }

        /// <summary>
        /// Checks if a shape type requires two points to define (vs. single point placement).
        /// </summary>
        /// <param name="shape">The shape type to check</param>
        /// <returns>True if the shape needs two points (origin and target)</returns>
        public static bool RequiresTwoPoints(ShapeType shape)
        {
            return shape != ShapeType.Manual;
        }

        /// <summary>
        /// Checks if a shape is a border-only shape (empty rectangle, empty oval).
        /// These shapes place elements only on the perimeter.
        /// </summary>
        /// <param name="shape">The shape type to check</param>
        /// <returns>True if the shape places only border cells</returns>
        public static bool IsBorderShape(ShapeType shape)
        {
            return shape == ShapeType.EmptyRectangle || shape == ShapeType.EmptyOval;
        }

        /// <summary>
        /// Checks if a shape is a filled shape (filled rectangle, filled oval).
        /// These shapes place elements on all interior cells.
        /// </summary>
        /// <param name="shape">The shape type to check</param>
        /// <returns>True if the shape fills all interior cells</returns>
        public static bool IsFilledShape(ShapeType shape)
        {
            return shape == ShapeType.FilledRectangle || shape == ShapeType.FilledOval;
        }

        /// <summary>
        /// Checks if a shape is a line shape (line, angled line).
        /// </summary>
        /// <param name="shape">The shape type to check</param>
        /// <returns>True if the shape is a line type</returns>
        public static bool IsLineShape(ShapeType shape)
        {
            return shape == ShapeType.Line || shape == ShapeType.AngledLine;
        }
    }
}
