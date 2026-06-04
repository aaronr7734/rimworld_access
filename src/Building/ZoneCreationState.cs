using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Defines the types of zones that can be created.
    /// </summary>
    public enum ZoneType
    {
        Stockpile,
        DumpingStockpile,
        GrowingZone,
        AllowedArea,
        HomeZone
    }

    /// <summary>
    /// Defines the selection mode for zone creation.
    /// </summary>
    public enum ZoneSelectionMode
    {
        BoxSelection,    // Space sets corners for shape selection
        SingleTile       // Space toggles individual tiles
    }

    /// <summary>
    /// Maintains state for zone creation mode.
    /// Tracks which cells have been selected and what type of zone to create.
    /// Supports all shapes via ShapeHelper (FilledRectangle, EmptyRectangle, FilledOval, etc.).
    /// </summary>
    public static class ZoneCreationState
    {
        private static bool isInCreationMode = false;
        private static ZoneType selectedZoneType = ZoneType.Stockpile;
        private static List<IntVec3> selectedCells = new List<IntVec3>();
        private static Zone targetZone = null; // Zone being modified (expand or shrink mode)
        private static bool isShrinking = false; // true = shrink mode (selected cells will be removed)
        private static ZoneSelectionMode selectionMode = ZoneSelectionMode.BoxSelection; // Default to box selection

        // Designator reference for shape validation
        private static Designator currentDesignator = null;

        // Shape-based selection via ShapePreviewHelper
        private static readonly ShapePreviewHelper previewHelper = new ShapePreviewHelper();
        private static ShapeType currentShape = ShapeType.FilledRectangle;

        /// <summary>
        /// Whether zone creation mode is currently active.
        /// </summary>
        public static bool IsInCreationMode
        {
            get => isInCreationMode;
            private set => isInCreationMode = value;
        }

        /// <summary>
        /// The type of zone being created.
        /// </summary>
        public static ZoneType SelectedZoneType
        {
            get => selectedZoneType;
            private set => selectedZoneType = value;
        }

        /// <summary>
        /// The current shape type being used for selection.
        /// </summary>
        public static ShapeType CurrentShape => currentShape;

        /// <summary>
        /// Whether a first corner has been set (shape origin point).
        /// </summary>
        public static bool HasFirstCorner => previewHelper.HasFirstCorner;

        /// <summary>
        /// Whether we are actively previewing a shape (first and second corner set).
        /// </summary>
        public static bool IsInPreviewMode => previewHelper.IsInPreviewMode;

        /// <summary>
        /// The first corner of the shape being selected (origin point).
        /// </summary>
        public static IntVec3? FirstCorner => previewHelper.FirstCorner;

        /// <summary>
        /// The second corner of the shape being selected (target point).
        /// </summary>
        public static IntVec3? SecondCorner => previewHelper.SecondCorner;

        /// <summary>
        /// Cells in the current shape preview.
        /// </summary>
        public static IReadOnlyList<IntVec3> PreviewCells => previewHelper.PreviewCells;

        // Legacy property aliases for backward compatibility.
        // These are used by ZoneCreationPatch.cs for announcements and by tests.
        // Kept to avoid breaking existing code that uses rectangle-specific naming.
        /// <summary>
        /// Whether a rectangle start corner has been set.
        /// Alias for HasFirstCorner for backward compatibility.
        /// Used by: ZoneCreationPatch (announcements), ZoneCreationState tests.
        /// </summary>
        public static bool HasRectangleStart => previewHelper.HasFirstCorner;

        /// <summary>
        /// The start corner of the rectangle being selected.
        /// Alias for FirstCorner for backward compatibility.
        /// Used by: ZoneCreationPatch (coordinate announcements).
        /// </summary>
        public static IntVec3? RectangleStart => previewHelper.FirstCorner;

        /// <summary>
        /// The end corner of the rectangle being selected.
        /// Alias for SecondCorner for backward compatibility.
        /// Used by: ZoneCreationPatch (coordinate announcements).
        /// </summary>
        public static IntVec3? RectangleEnd => previewHelper.SecondCorner;

        /// <summary>
        /// List of cells that have been selected for the zone.
        /// </summary>
        public static List<IntVec3> SelectedCells => selectedCells;

        /// <summary>
        /// Whether we're in shrink mode (selected cells will be removed from zone).
        /// </summary>
        public static bool IsShrinking => isShrinking;

        /// <summary>
        /// Gets the current selection mode (BoxSelection or SingleTile).
        /// </summary>
        public static ZoneSelectionMode SelectionMode => selectionMode;

        /// <summary>
        /// Toggles between box selection and single tile selection modes.
        /// </summary>
        public static void ToggleSelectionMode()
        {
            selectionMode = (selectionMode == ZoneSelectionMode.BoxSelection)
                ? ZoneSelectionMode.SingleTile
                : ZoneSelectionMode.BoxSelection;

            string modeName = (selectionMode == ZoneSelectionMode.BoxSelection)
                ? "RimWorldAccess.Building.Paint.ModeBox".Translate()
                : "RimWorldAccess.Building.Paint.ModeSingle".Translate();
            TolkHelper.Speak(modeName);
            Log.Message($"Zone creation: Switched to {modeName}");
        }

        /// <summary>
        /// Toggles selection of a single cell (adds if not selected, removes if selected).
        /// Used in single tile selection mode.
        /// Validates terrain requirements and adjacency before adding.
        /// </summary>
        public static void ToggleCell(IntVec3 cell)
        {
            Map map = Find.CurrentMap;
            if (map == null) return;

            if (selectedCells.Contains(cell))
            {
                // Removing - check if it would disconnect the selection
                if (selectedCells.Count > 1 && WouldDisconnectSelection(cell))
                {
                    TolkHelper.Speak("RimWorldAccess.Building.Create.CannotRemoveDisconnect".Loc());
                    return;
                }
                selectedCells.Remove(cell);
                TolkHelper.Speak("RimWorldAccess.Building.Create.Deselected".Loc(cell.x, cell.z, selectedCells.Count));
            }
            else
            {
                // Adding - validate the cell first
                string error = ValidateCellForCreation(cell, map);
                if (error != null)
                {
                    TolkHelper.Speak(error);
                    return;
                }

                selectedCells.Add(cell);
                TolkHelper.Speak("RimWorldAccess.Building.Create.SelectedWithTotal".Loc(MapNavigationState.FormatSelectedCell(cell), selectedCells.Count));
            }
        }

        /// <summary>
        /// Validates a cell can be added to the zone selection during creation.
        /// Reuses designator.CanDesignateCell() for terrain requirements.
        /// Checks adjacency against selectedCells (not an existing zone).
        /// </summary>
        /// <param name="cell">The cell to validate</param>
        /// <param name="map">The current map</param>
        /// <returns>Error message if invalid, null if valid</returns>
        private static string ValidateCellForCreation(IntVec3 cell, Map map)
        {
            // Check bounds
            if (!cell.InBounds(map))
                return "RimWorldAccess.Building.Create.CellOutOfBounds".Translate();

            // Check not already in a zone
            Zone existingZone = map.zoneManager.ZoneAt(cell);
            if (existingZone != null)
                return "RimWorldAccess.Building.Create.CellAlreadyInZone".Translate(existingZone.label);

            // Check terrain requirements via designator (soil fertility, etc.)
            if (currentDesignator != null)
            {
                var zoneDesignator = currentDesignator as Designator_ZoneAdd;
                if (zoneDesignator != null)
                {
                    AcceptanceReport report = zoneDesignator.CanDesignateCell(cell);
                    if (!report.Accepted)
                    {
                        return !string.IsNullOrEmpty(report.Reason)
                            ? report.Reason
                            : (string)"RimWorldAccess.Building.Create.CannotPlaceZoneHere".Translate();
                    }
                }
            }

            // Check adjacency to selectedCells (first cell is always allowed)
            if (selectedCells.Count > 0)
            {
                bool isAdjacent = false;
                for (int i = 0; i < 4; i++)
                {
                    IntVec3 neighbor = cell + GenAdj.CardinalDirections[i];
                    if (selectedCells.Contains(neighbor))
                    {
                        isAdjacent = true;
                        break;
                    }
                }
                if (!isAdjacent)
                    return "RimWorldAccess.Building.Create.MustBeAdjacentToSelection".Translate();
            }

            return null; // Valid
        }

        /// <summary>
        /// Checks if removing a cell would disconnect the remaining selection.
        /// Uses flood-fill (same algorithm as ZoneEditingHelper.WouldDisconnectZone).
        /// </summary>
        /// <param name="cellToRemove">The cell being considered for removal</param>
        /// <returns>True if removing the cell would disconnect the selection</returns>
        private static bool WouldDisconnectSelection(IntVec3 cellToRemove)
        {
            var remaining = new HashSet<IntVec3>(selectedCells);
            remaining.Remove(cellToRemove);

            if (remaining.Count == 0)
                return false; // Removing last cell is fine

            // Flood-fill from first remaining cell
            var visited = new HashSet<IntVec3>();
            var queue = new Queue<IntVec3>();
            var startCell = remaining.First();

            queue.Enqueue(startCell);
            visited.Add(startCell);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                for (int i = 0; i < 4; i++)
                {
                    var neighbor = current + GenAdj.CardinalDirections[i];
                    if (remaining.Contains(neighbor) && !visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            return visited.Count < remaining.Count;
        }

        /// <summary>
        /// Gets the available shapes for the current zone creation context.
        /// Returns shapes based on the designator's DrawStyleCategory if available.
        /// </summary>
        /// <returns>List of available shape types for the current context</returns>
        public static List<ShapeType> GetAvailableShapes()
        {
            if (currentDesignator != null)
                return ShapeHelper.GetAvailableShapes(currentDesignator);

            // Fallback for direct zone creation without designator
            return new List<ShapeType> { ShapeType.FilledRectangle };
        }

        /// <summary>
        /// Enters zone creation mode with the specified zone type.
        /// Uses shape selection by default: Space sets corners, arrows preview, Space confirms shape.
        /// Press Tab to switch to single tile selection mode.
        /// </summary>
        /// <param name="zoneType">The type of zone to create</param>
        /// <param name="designator">Optional designator to determine available shapes</param>
        public static void EnterCreationMode(ZoneType zoneType, Designator designator = null)
        {
            InitializeMode(designator);
            selectedZoneType = zoneType;
            selectionMode = ZoneSelectionMode.BoxSelection; // Default to box selection

            string zoneName = GetZoneTypeName(zoneType);
            string shapeName = ShapeHelper.GetShapeName(currentShape);
            string instructions = "RimWorldAccess.Building.Create.ShapeInstructions".Translate(shapeName);

            TolkHelper.Speak("RimWorldAccess.Building.Create.OpenCreating".Loc(zoneName, instructions));
            Log.Message($"Entered zone creation mode: {zoneName} with shape {currentShape}");
        }

        /// <summary>
        /// Sets the current shape type for zone selection.
        /// Called when user selects a shape from the shape selection menu.
        /// Validates that the shape is allowed for the current zone type.
        /// </summary>
        /// <param name="shape">The shape type to use</param>
        public static void SetCurrentShape(ShapeType shape)
        {
            var allowed = GetAvailableShapes();
            if (allowed.Contains(shape))
            {
                currentShape = shape;
                previewHelper.SetCurrentShape(shape);
                string shapeName = ShapeHelper.GetShapeName(shape);
                TolkHelper.Speak("RimWorldAccess.Building.Paint.ShapeChanged".Loc(shapeName));
                Log.Message($"ZoneCreationState: Set shape to {shape}");
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Building.Create.ShapeUnavailable".Loc());
                Log.Message($"ZoneCreationState: Shape {shape} not allowed. Available: {string.Join(", ", allowed)}");
            }
        }

        /// <summary>
        /// Sets the first corner (origin point) for shape selection.
        /// </summary>
        /// <param name="cell">The cell position for the first corner</param>
        public static void SetFirstCorner(IntVec3 cell)
        {
            previewHelper.SetFirstCorner(cell, "ZoneCreationState");
        }

        /// <summary>
        /// Sets the second corner (target point) for shape selection.
        /// </summary>
        /// <param name="cell">The cell position for the second corner</param>
        public static void SetSecondCorner(IntVec3 cell)
        {
            previewHelper.SetSecondCorner(cell, "ZoneCreationState");
        }

        /// <summary>
        /// Sets the start corner for rectangle selection.
        /// Alias for SetFirstCorner for backward compatibility.
        /// </summary>
        public static void SetRectangleStart(IntVec3 cell)
        {
            SetFirstCorner(cell);
        }

        /// <summary>
        /// Updates the shape preview as the cursor moves.
        /// Plays native sound feedback when cell count changes.
        /// Uses ShapeHelper.CalculateCells() for all shape types.
        /// </summary>
        /// <param name="cursor">The current cursor position</param>
        public static void UpdatePreview(IntVec3 cursor)
        {
            previewHelper.UpdatePreview(cursor);
        }

        /// <summary>
        /// Confirms the current shape preview, adding all cells to selection.
        /// Allows starting a new shape immediately.
        /// </summary>
        public static void ConfirmShape()
        {
            if (!IsInPreviewMode)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Paint.NoShapeToConfirm".Loc());
                return;
            }

            // Get confirmed cells from previewHelper (which also resets its state)
            var confirmedCells = previewHelper.ConfirmShape("ZoneCreationState");

            // Find cells that aren't already in the collection
            int addedCount = 0;
            foreach (var cell in confirmedCells)
            {
                if (!selectedCells.Contains(cell))
                {
                    selectedCells.Add(cell);
                    addedCount++;
                }
            }

            TolkHelper.Speak("RimWorldAccess.Building.Paint.ConfirmShapeIrregular".Loc(addedCount, selectedCells.Count));
        }

        /// <summary>
        /// Confirms the current rectangle preview, adding all cells to selection.
        /// Alias for ConfirmShape for backward compatibility.
        /// </summary>
        public static void ConfirmRectangle()
        {
            ConfirmShape();
        }

        /// <summary>
        /// Cancels the current shape selection without adding cells.
        /// </summary>
        public static void CancelShape()
        {
            previewHelper.Cancel();
        }

        /// <summary>
        /// Cancels the current rectangle selection without adding cells.
        /// Alias for CancelShape for backward compatibility.
        /// </summary>
        public static void CancelRectangle()
        {
            CancelShape();
        }

        /// <summary>
        /// Checks if a cell is currently selected.
        /// </summary>
        public static bool IsCellSelected(IntVec3 cell)
        {
            return selectedCells.Contains(cell);
        }

        /// <summary>
        /// Enters expansion mode for an existing zone.
        /// Pre-selects all existing zone tiles and allows adding/removing tiles using standard zone creation controls.
        /// </summary>
        /// <param name="zone">The zone to expand</param>
        /// <param name="designator">Optional designator to determine available shapes</param>
        public static void EnterExpansionMode(Zone zone, Designator designator = null)
        {
            if (zone == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Create.NoZoneProvidedExpand".Loc(), SpeechPriority.High);
                Log.Error("EnterExpansionMode called with null zone");
                return;
            }

            InitializeMode(designator);
            targetZone = zone;
            isShrinking = false;

            // Pre-select all existing zone tiles
            foreach (IntVec3 cell in zone.Cells)
            {
                selectedCells.Add(cell);
            }

            // Determine zone type for future reference (not used in expansion, but kept for consistency)
            if (zone is Zone_Stockpile stockpile)
            {
                // Check if it's a dumping stockpile by checking settings
                if (stockpile.settings.Priority == StoragePriority.Unstored)
                {
                    selectedZoneType = ZoneType.DumpingStockpile;
                }
                else
                {
                    selectedZoneType = ZoneType.Stockpile;
                }
            }
            else if (zone is Zone_Growing)
            {
                selectedZoneType = ZoneType.GrowingZone;
            }

            string shapeName = ShapeHelper.GetShapeName(currentShape);
            string instructions = "RimWorldAccess.Building.Create.ExpandShrinkInstructions".Translate(shapeName);
            TolkHelper.Speak("RimWorldAccess.Building.Create.OpenExpanding".Loc(zone.label, selectedCells.Count, instructions));
            Log.Message($"Entered expansion mode for zone: {zone.label}. Pre-selected {selectedCells.Count} existing cells with shape {currentShape}");
        }

        /// <summary>
        /// Enters shrink mode for an existing zone.
        /// Selected cells will be removed from the zone on confirm.
        /// </summary>
        /// <param name="zone">The zone to shrink</param>
        /// <param name="designator">Optional designator to determine available shapes</param>
        public static void EnterShrinkMode(Zone zone, Designator designator = null)
        {
            if (zone == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Create.NoZoneProvidedShrink".Loc(), SpeechPriority.High);
                Log.Error("EnterShrinkMode called with null zone");
                return;
            }

            InitializeMode(designator);
            targetZone = zone;
            isShrinking = true;

            string shapeName = ShapeHelper.GetShapeName(currentShape);
            string instructions = "RimWorldAccess.Building.Create.ExpandShrinkInstructions".Translate(shapeName);
            TolkHelper.Speak("RimWorldAccess.Building.Create.OpenShrinking".Loc(zone.label, instructions));
            Log.Message($"Entered shrink mode for zone: {zone.label} with shape {currentShape}");
        }

        /// <summary>
        /// Creates the zone with all selected cells and exits creation mode.
        /// If in expansion mode, adds cells to existing zone instead.
        /// If in shrink mode, removes selected cells from zone.
        /// </summary>
        public static void CreateZone(Map map)
        {
            // Handle expansion mode
            if (targetZone != null)
            {
                ExpandZone(map);
                return;
            }

            // Normal zone/area creation
            if (selectedCells.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Create.NoCellsZoneNotCreated".Loc());
                Cancel();
                return;
            }

            string zoneName = "";

            try
            {
                switch (selectedZoneType)
                {
                    case ZoneType.Stockpile:
                        CreateStockpileZone(map, selectedCells);
                        zoneName = "RimWorldAccess.Building.Create.ZoneCapped.Stockpile".Translate();
                        break;

                    case ZoneType.DumpingStockpile:
                        CreateDumpingStockpileZone(map, selectedCells);
                        zoneName = "RimWorldAccess.Building.Create.ZoneCapped.DumpingStockpile".Translate();
                        break;

                    case ZoneType.GrowingZone:
                        CreateGrowingZone(map, selectedCells);
                        zoneName = "RimWorldAccess.Building.Create.ZoneCapped.GrowingZone".Translate();
                        break;

                    case ZoneType.AllowedArea:
                        {
                            Area_Allowed allowedArea = CreateAllowedArea(map, selectedCells);
                            if (allowedArea == null)
                            {
                                TolkHelper.Speak("RimWorldAccess.Building.Create.MaxAllowedAreasReached".Loc(), SpeechPriority.High);
                                Log.Warning("Failed to create allowed area: max limit reached");
                                Reset();
                                return;
                            }
                            zoneName = "RimWorldAccess.Building.Create.AllowedAreaName".Translate(allowedArea.Label);
                        }
                        break;

                    case ZoneType.HomeZone:
                        if (!ExpandHomeZone(map, selectedCells))
                        {
                            TolkHelper.Speak("RimWorldAccess.Building.Create.HomeAreaNotFound".Loc(), SpeechPriority.High);
                            Log.Error("Home area not found in area manager");
                            Reset();
                            return;
                        }
                        zoneName = "RimWorldAccess.Building.Create.ZoneCapped.HomeZone".Translate();
                        break;
                }

                // Check for obstacles in the new zone
                var obstacles = ObstacleDetector.FindObstacles(
                    map,
                    selectedCells,
                    MapNavigationState.CurrentCursorPosition);

                string obstacleInfo = obstacles.Count > 0
                    ? (string)"RimWorldAccess.Building.Create.ObstaclesFoundSuffix".Translate(obstacles.Count)
                    : string.Empty;
                if (obstacles.Count > 0)
                {
                    ObstacleDetector.AddToScanner(obstacles, "Zone Obstacles");
                }

                TolkHelper.Speak("RimWorldAccess.Building.Create.ZoneCreated".Loc(zoneName, selectedCells.Count, obstacleInfo));
                Log.Message($"Created {zoneName} with {selectedCells.Count} cells, {obstacles.Count} obstacles");
            }
            catch (System.Exception ex)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Create.ErrorCreatingZone".Loc(ex.Message), SpeechPriority.High);
                Log.Error($"Error creating zone: {ex}");
            }
            finally
            {
                Reset();
            }
        }

        /// <summary>
        /// Updates the expanding zone based on selected cells.
        /// In expand mode: adds new cells, removes deselected cells.
        /// In shrink mode: removes selected cells.
        /// </summary>
        private static void ExpandZone(Map map)
        {
            if (targetZone == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Create.NoZoneToModify".Loc(), SpeechPriority.High);
                Log.Error("ExpandZone called but targetZone is null");
                Reset();
                return;
            }

            // Handle shrink mode - selected cells are removed from zone
            if (isShrinking)
            {
                ShrinkZone(map);
                return;
            }

            // Expand mode - zone is updated to match selection
            if (selectedCells.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Create.AllCellsRemovedZoneDeleted".Loc());
                targetZone.Delete();
                Reset();
                return;
            }

            try
            {
                int removedCount = 0;
                List<IntVec3> newlyAddedCells = new List<IntVec3>();

                // Build a set of selected cells for quick lookup
                HashSet<IntVec3> selectedSet = new HashSet<IntVec3>(selectedCells);

                // Remove cells that are in the zone but not in the selection
                List<IntVec3> cellsToRemove = new List<IntVec3>();
                foreach (IntVec3 cell in targetZone.Cells)
                {
                    if (!selectedSet.Contains(cell))
                    {
                        cellsToRemove.Add(cell);
                    }
                }

                foreach (IntVec3 cell in cellsToRemove)
                {
                    targetZone.RemoveCell(cell);
                    removedCount++;
                }

                // Add cells that are selected but not in the zone, tracking which are new
                foreach (IntVec3 cell in selectedCells)
                {
                    if (cell.InBounds(map) && !targetZone.ContainsCell(cell))
                    {
                        targetZone.AddCell(cell);
                        newlyAddedCells.Add(cell);
                    }
                }

                // Check for disconnected fragments AFTER all modifications (matches standard RimWorld behavior)
                targetZone.CheckContiguous();

                // Build feedback message
                int addedCount = newlyAddedCells.Count;
                string message = "RimWorldAccess.Building.Create.UpdatePrefix".Translate(targetZone.label);
                if (addedCount > 0 && removedCount > 0)
                {
                    message += "RimWorldAccess.Building.Create.UpdateAddedAndRemoved".Translate(addedCount, removedCount);
                }
                else if (addedCount > 0)
                {
                    message += "RimWorldAccess.Building.Create.UpdateAddedOnly".Translate(addedCount);
                }
                else if (removedCount > 0)
                {
                    message += "RimWorldAccess.Building.Create.UpdateRemovedOnly".Translate(removedCount);
                }
                else
                {
                    message += "RimWorldAccess.Building.Create.UpdateNoChanges".Translate();
                }

                // Check for obstacles in NEWLY ADDED cells only (not existing zone cells)
                if (newlyAddedCells.Count > 0)
                {
                    var obstacles = ObstacleDetector.FindObstacles(
                        map,
                        newlyAddedCells,
                        MapNavigationState.CurrentCursorPosition);

                    if (obstacles.Count > 0)
                    {
                        message += "RimWorldAccess.Building.Create.ObstaclesInNewAreaSuffix".Translate(obstacles.Count);
                        ObstacleDetector.AddToScanner(obstacles, "Zone Obstacles");
                    }
                }

                TolkHelper.Speak(message);
                Log.Message($"Expanded zone {targetZone.label}: added {addedCount}, removed {removedCount} cells");
            }
            catch (System.Exception ex)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Create.ErrorExpandingZone".Loc(ex.Message), SpeechPriority.High);
                Log.Error($"Error expanding zone: {ex}");
            }
            finally
            {
                Reset();
            }
        }

        /// <summary>
        /// Removes selected cells from the zone (shrink mode).
        /// </summary>
        private static void ShrinkZone(Map map)
        {
            if (selectedCells.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Create.NoCellsZoneUnchanged".Loc());
                Reset();
                return;
            }

            try
            {
                int removedCount = 0;

                // Remove selected cells from the zone
                foreach (IntVec3 cell in selectedCells)
                {
                    if (targetZone.ContainsCell(cell))
                    {
                        targetZone.RemoveCell(cell);
                        removedCount++;
                    }
                }

                // Check if zone is now empty
                if (targetZone.Cells.Count() == 0)
                {
                    TolkHelper.Speak("RimWorldAccess.Building.Create.AllCellsRemovedNamedDeleted".Loc(targetZone.label));
                    targetZone.Delete();
                }
                else
                {
                    // Check for disconnected fragments
                    targetZone.CheckContiguous();
                    TolkHelper.Speak("RimWorldAccess.Building.Create.RemovedCellsFromZone".Loc(removedCount, targetZone.label, targetZone.Cells.Count()));
                }

                Log.Message($"Shrunk zone {targetZone?.label}: removed {removedCount} cells");
            }
            catch (System.Exception ex)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Create.ErrorShrinkingZone".Loc(ex.Message), SpeechPriority.High);
                Log.Error($"Error shrinking zone: {ex}");
            }
            finally
            {
                Reset();
            }
        }

        /// <summary>
        /// Cancels zone creation and exits creation mode.
        /// </summary>
        public static void Cancel()
        {
            TolkHelper.Speak("RimWorldAccess.Building.Create.Cancelled".Loc());
            Log.Message("Zone creation cancelled");
            Reset();
        }

        /// <summary>
        /// Creates a stockpile zone with the selected cells.
        /// </summary>
        private static Zone CreateStockpileZone(Map map, List<IntVec3> cells)
        {
            Zone_Stockpile stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            map.zoneManager.RegisterZone(stockpile);
            foreach (IntVec3 cell in cells)
            {
                if (cell.InBounds(map))
                {
                    stockpile.AddCell(cell);
                }
            }
            return stockpile;
        }

        /// <summary>
        /// Creates a dumping stockpile zone with the selected cells.
        /// </summary>
        private static Zone CreateDumpingStockpileZone(Map map, List<IntVec3> cells)
        {
            Zone_Stockpile dumpingStockpile = new Zone_Stockpile(StorageSettingsPreset.DumpingStockpile, map.zoneManager);
            map.zoneManager.RegisterZone(dumpingStockpile);
            foreach (IntVec3 cell in cells)
            {
                if (cell.InBounds(map))
                {
                    dumpingStockpile.AddCell(cell);
                }
            }
            return dumpingStockpile;
        }

        /// <summary>
        /// Creates a growing zone with the selected cells.
        /// </summary>
        private static Zone CreateGrowingZone(Map map, List<IntVec3> cells)
        {
            Zone_Growing growingZone = new Zone_Growing(map.zoneManager);
            map.zoneManager.RegisterZone(growingZone);
            foreach (IntVec3 cell in cells)
            {
                if (cell.InBounds(map))
                {
                    growingZone.AddCell(cell);
                }
            }
            return growingZone;
        }

        /// <summary>
        /// Creates an allowed area with the selected cells.
        /// Returns null if the maximum number of allowed areas has been reached.
        /// </summary>
        private static Area_Allowed CreateAllowedArea(Map map, List<IntVec3> cells)
        {
            if (!map.areaManager.TryMakeNewAllowed(out Area_Allowed allowedArea))
            {
                return null;
            }

            // Add cells to the area
            foreach (IntVec3 cell in cells)
            {
                if (cell.InBounds(map))
                {
                    allowedArea[cell] = true;
                }
            }

            return allowedArea;
        }

        /// <summary>
        /// Expands the home zone with the selected cells.
        /// Returns false if the home area was not found.
        /// </summary>
        private static bool ExpandHomeZone(Map map, List<IntVec3> cells)
        {
            Area_Home homeArea = map.areaManager.Home;
            if (homeArea == null)
            {
                return false;
            }

            foreach (IntVec3 cell in cells)
            {
                if (cell.InBounds(map))
                {
                    homeArea[cell] = true;
                }
            }
            return true;
        }

        /// <summary>
        /// Initializes common mode state (creation, expansion, or shrink).
        /// Clears selection, resets preview, and sets shape based on designator.
        /// </summary>
        /// <param name="designator">Optional designator to determine available shapes</param>
        private static void InitializeMode(Designator designator)
        {
            isInCreationMode = true;
            currentDesignator = designator;
            selectedCells.Clear();
            previewHelper.Reset();
            currentShape = designator != null ? ShapeHelper.GetDefaultShape(designator) : ShapeType.FilledRectangle;
            previewHelper.SetCurrentShape(currentShape);
        }

        /// <summary>
        /// Resets the state, exiting creation mode.
        /// </summary>
        public static void Reset()
        {
            isInCreationMode = false;
            selectedCells.Clear();
            targetZone = null;
            isShrinking = false;
            currentDesignator = null;
            previewHelper.Reset();
            selectionMode = ZoneSelectionMode.BoxSelection; // Reset to default mode

            // Clear any obstacle scanner category from zone creation
            ObstacleDetector.ClearFromScanner();
        }

        /// <summary>
        /// Gets a human-readable name for a zone type.
        /// </summary>
        private static string GetZoneTypeName(ZoneType type)
        {
            switch (type)
            {
                case ZoneType.Stockpile:        return "RimWorldAccess.Building.Create.ZoneTypeName.Stockpile".Translate();
                case ZoneType.DumpingStockpile: return "RimWorldAccess.Building.Create.ZoneTypeName.DumpingStockpile".Translate();
                case ZoneType.GrowingZone:      return "RimWorldAccess.Building.Create.ZoneTypeName.GrowingZone".Translate();
                case ZoneType.AllowedArea:      return "RimWorldAccess.Building.Create.ZoneTypeName.AllowedArea".Translate();
                case ZoneType.HomeZone:         return "RimWorldAccess.Building.Create.ZoneTypeName.HomeZone".Translate();
                default:                        return "RimWorldAccess.Building.Create.ZoneTypeName.Default".Translate();
            }
        }

    }
}
