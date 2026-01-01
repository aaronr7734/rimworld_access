using System.Collections.Generic;
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
    /// Defines the zone creation modes.
    /// </summary>
    public enum ZoneCreationMode
    {
        Manual,
        Borders,
        Corners
    }

    /// <summary>
    /// Maintains state for zone creation mode.
    /// Tracks which cells have been selected and what type of zone to create.
    /// </summary>
    public static class ZoneCreationState
    {
        private static bool isInCreationMode = false;
        private static ZoneType selectedZoneType = ZoneType.Stockpile;
        private static ZoneCreationMode currentMode = ZoneCreationMode.Manual;
        private static List<IntVec3> selectedCells = new List<IntVec3>();
        private static Zone expandingZone = null; // Track zone being expanded
        private static string pendingAllowedAreaName = null; // Store name for allowed area creation

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
        /// The current creation mode (Manual, Borders, or Corners).
        /// </summary>
        public static ZoneCreationMode CurrentMode
        {
            get => currentMode;
            private set => currentMode = value;
        }

        /// <summary>
        /// List of cells that have been selected for the zone.
        /// </summary>
        public static List<IntVec3> SelectedCells => selectedCells;

        /// <summary>
        /// Sets the pending name for an allowed area that will be created.
        /// </summary>
        public static void SetPendingAllowedAreaName(string name)
        {
            pendingAllowedAreaName = name;
            Log.Message($"Set pending allowed area name: {name}");
        }

        /// <summary>
        /// Enters zone creation mode with the specified zone type and mode.
        /// </summary>
        public static void EnterCreationMode(ZoneType zoneType, ZoneCreationMode mode)
        {
            isInCreationMode = true;
            selectedZoneType = zoneType;
            currentMode = mode;
            selectedCells.Clear();

            string zoneName = GetZoneTypeName(zoneType);
            string modeName = GetModeName(mode);

            string instructions = mode == ZoneCreationMode.Manual
                ? "Press Space to select tiles, Enter to confirm, Escape to cancel. Use Shift+arrows to auto-select to wall"
                : mode == ZoneCreationMode.Borders
                    ? "Select border tiles with Space, then press Enter to auto-fill interior"
                    : "Select 4 corner tiles with Space, then press Enter to fill rectangle";

            TolkHelper.Speak($"Creating {zoneName} in {modeName} mode. {instructions}");
            Log.Message($"Entered zone creation mode: {zoneName}, mode: {modeName}");
        }

        /// <summary>
        /// Adds a cell to the selection if not already selected.
        /// </summary>
        public static void AddCell(IntVec3 cell)
        {
            if (!selectedCells.Contains(cell))
            {
                selectedCells.Add(cell);
                TolkHelper.Speak($"Selected, {cell.x}, {cell.z}");
            }
            else
            {
                TolkHelper.Speak($"Already selected, {cell.x}, {cell.z}");
            }
        }

        /// <summary>
        /// Removes a cell from the selection.
        /// </summary>
        public static void RemoveCell(IntVec3 cell)
        {
            if (selectedCells.Remove(cell))
            {
                TolkHelper.Speak($"Deselected, {cell.x}, {cell.z}");
            }
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
        public static void EnterExpansionMode(Zone zone)
        {
            if (zone == null)
            {
                TolkHelper.Speak("Cannot expand: no zone provided", SpeechPriority.High);
                Log.Error("EnterExpansionMode called with null zone");
                return;
            }

            isInCreationMode = true;
            expandingZone = zone;
            currentMode = ZoneCreationMode.Manual;
            selectedCells.Clear();

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

            string instructions = "Press Space to toggle tiles, Enter to confirm, Escape to cancel. Use Shift+arrows to auto-select to wall";
            TolkHelper.Speak($"Expanding {zone.label}. {selectedCells.Count} tiles currently selected. {instructions}");
            Log.Message($"Entered expansion mode for zone: {zone.label}. Pre-selected {selectedCells.Count} existing tiles");
        }

        /// <summary>
        /// Creates the zone with all selected cells and exits creation mode.
        /// If in expansion mode, adds cells to existing zone instead.
        /// </summary>
        public static void CreateZone(Map map)
        {
            // Handle expansion mode
            if (expandingZone != null)
            {
                ExpandZone(map);
                return;
            }

            // Normal zone/area creation
            if (selectedCells.Count == 0)
            {
                TolkHelper.Speak("No cells selected. Zone not created.");
                Cancel();
                return;
            }

            string zoneName = "";

            try
            {
                switch (selectedZoneType)
                {
                    case ZoneType.Stockpile:
                        {
                            Zone_Stockpile stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
                            map.zoneManager.RegisterZone(stockpile);
                            foreach (IntVec3 cell in selectedCells)
                            {
                                if (cell.InBounds(map))
                                {
                                    stockpile.AddCell(cell);
                                }
                            }
                            zoneName = "Stockpile zone";
                        }
                        break;

                    case ZoneType.DumpingStockpile:
                        {
                            Zone_Stockpile dumpingStockpile = new Zone_Stockpile(StorageSettingsPreset.DumpingStockpile, map.zoneManager);
                            map.zoneManager.RegisterZone(dumpingStockpile);
                            foreach (IntVec3 cell in selectedCells)
                            {
                                if (cell.InBounds(map))
                                {
                                    dumpingStockpile.AddCell(cell);
                                }
                            }
                            zoneName = "Dumping stockpile zone";
                        }
                        break;

                    case ZoneType.GrowingZone:
                        {
                            Zone_Growing growingZone = new Zone_Growing(map.zoneManager);
                            map.zoneManager.RegisterZone(growingZone);
                            foreach (IntVec3 cell in selectedCells)
                            {
                                if (cell.InBounds(map))
                                {
                                    growingZone.AddCell(cell);
                                }
                            }
                            zoneName = "Growing zone";
                        }
                        break;

                    case ZoneType.AllowedArea:
                        {
                            // Create allowed area using the area manager
                            if (!map.areaManager.TryMakeNewAllowed(out Area_Allowed allowedArea))
                            {
                                TolkHelper.Speak("Cannot create more allowed areas. Maximum of 10 reached.", SpeechPriority.High);
                                Log.Warning("Failed to create allowed area: max limit reached");
                                Reset();
                                return;
                            }

                            // Set the custom name if provided
                            string areaName = pendingAllowedAreaName;
                            if (!string.IsNullOrWhiteSpace(areaName))
                            {
                                allowedArea.SetLabel(areaName);
                            }

                            // Add cells to the area
                            foreach (IntVec3 cell in selectedCells)
                            {
                                if (cell.InBounds(map))
                                {
                                    allowedArea[cell] = true; // Areas use indexer syntax
                                }
                            }

                            zoneName = $"Allowed area '{allowedArea.Label}'";
                            pendingAllowedAreaName = null; // Clear the pending name
                        }
                        break;

                    case ZoneType.HomeZone:
                        {
                            // Get the home area from the area manager
                            Area_Home homeArea = map.areaManager.Home;
                            if (homeArea == null)
                            {
                                TolkHelper.Speak("Error: Home area not found", SpeechPriority.High);
                                Log.Error("Home area not found in area manager");
                                Reset();
                                return;
                            }

                            // Add cells to the home area
                            foreach (IntVec3 cell in selectedCells)
                            {
                                if (cell.InBounds(map))
                                {
                                    homeArea[cell] = true; // Areas use indexer syntax
                                }
                            }

                            zoneName = "Home zone";
                        }
                        break;
                }

                TolkHelper.Speak($"{zoneName} created with {selectedCells.Count} cells");
                Log.Message($"Created {zoneName} with {selectedCells.Count} cells");
            }
            catch (System.Exception ex)
            {
                TolkHelper.Speak($"Error creating zone: {ex.Message}", SpeechPriority.High);
                Log.Error($"Error creating zone: {ex}");
            }
            finally
            {
                Reset();
            }
        }

        /// <summary>
        /// Updates the expanding zone based on selected cells (adds new cells, removes deselected cells).
        /// </summary>
        private static void ExpandZone(Map map)
        {
            if (expandingZone == null)
            {
                TolkHelper.Speak("Error: No zone to expand", SpeechPriority.High);
                Log.Error("ExpandZone called but expandingZone is null");
                Reset();
                return;
            }

            if (selectedCells.Count == 0)
            {
                TolkHelper.Speak("All cells removed. Zone deleted.");
                expandingZone.Delete();
                Reset();
                return;
            }

            try
            {
                int addedCount = 0;
                int removedCount = 0;

                // Build a set of selected cells for quick lookup
                HashSet<IntVec3> selectedSet = new HashSet<IntVec3>(selectedCells);

                // Remove cells that are in the zone but not in the selection
                List<IntVec3> cellsToRemove = new List<IntVec3>();
                foreach (IntVec3 cell in expandingZone.Cells)
                {
                    if (!selectedSet.Contains(cell))
                    {
                        cellsToRemove.Add(cell);
                    }
                }

                foreach (IntVec3 cell in cellsToRemove)
                {
                    expandingZone.RemoveCell(cell);
                    removedCount++;
                }

                // Add cells that are selected but not in the zone
                foreach (IntVec3 cell in selectedCells)
                {
                    if (cell.InBounds(map) && !expandingZone.ContainsCell(cell))
                    {
                        expandingZone.AddCell(cell);
                        addedCount++;
                    }
                }

                // Build feedback message
                string message = $"Updated {expandingZone.label}: ";
                if (addedCount > 0 && removedCount > 0)
                {
                    message += $"added {addedCount}, removed {removedCount} cells";
                }
                else if (addedCount > 0)
                {
                    message += $"added {addedCount} cells";
                }
                else if (removedCount > 0)
                {
                    message += $"removed {removedCount} cells";
                }
                else
                {
                    message += "no changes";
                }

                TolkHelper.Speak(message);
                Log.Message($"Expanded zone {expandingZone.label}: added {addedCount}, removed {removedCount} cells");
            }
            catch (System.Exception ex)
            {
                TolkHelper.Speak($"Error expanding zone: {ex.Message}", SpeechPriority.High);
                Log.Error($"Error expanding zone: {ex}");
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
            TolkHelper.Speak("Zone creation cancelled");
            Log.Message("Zone creation cancelled");
            Reset();
        }

        /// <summary>
        /// Auto-fills the interior of a zone from border cells using flood fill.
        /// Switches to Manual mode after completion.
        /// </summary>
        public static void BordersModeAutoFill(Map map)
        {
            if (selectedCells.Count == 0)
            {
                TolkHelper.Speak("No border cells selected. Select border tiles first", SpeechPriority.High);
                return;
            }

            try
            {
                // Find the center point of the selected border cells
                int sumX = 0, sumZ = 0;
                foreach (IntVec3 cell in selectedCells)
                {
                    sumX += cell.x;
                    sumZ += cell.z;
                }
                IntVec3 centerPoint = new IntVec3(sumX / selectedCells.Count, 0, sumZ / selectedCells.Count);

                // Ensure center point is valid and not in the border
                if (!centerPoint.InBounds(map))
                {
                    TolkHelper.Speak("Invalid border selection. Cannot find interior point", SpeechPriority.High);
                    return;
                }

                // If center is in the border, try to find a nearby non-border cell
                if (selectedCells.Contains(centerPoint))
                {
                    // Try adjacent cells
                    bool foundStart = false;
                    foreach (IntVec3 adjacent in GenAdj.CardinalDirections)
                    {
                        IntVec3 testCell = centerPoint + adjacent;
                        if (testCell.InBounds(map) && !selectedCells.Contains(testCell) && !testCell.Impassable(map))
                        {
                            centerPoint = testCell;
                            foundStart = true;
                            break;
                        }
                    }

                    if (!foundStart)
                    {
                        TolkHelper.Speak("Cannot find interior starting point. Border may be invalid", SpeechPriority.High);
                        return;
                    }
                }

                // Use flood fill to find all interior cells
                List<IntVec3> interiorCells = new List<IntVec3>();
                HashSet<IntVec3> borderSet = new HashSet<IntVec3>(selectedCells);

                map.floodFiller.FloodFill(centerPoint, (IntVec3 c) =>
                {
                    // Can traverse if: in bounds, not a border, not impassable
                    return c.InBounds(map) && !borderSet.Contains(c) && !c.Impassable(map);
                }, (IntVec3 c) =>
                {
                    // Add to interior cells
                    if (!borderSet.Contains(c))
                    {
                        interiorCells.Add(c);
                    }
                });

                // Add all interior cells to selection
                int addedCount = 0;
                foreach (IntVec3 cell in interiorCells)
                {
                    if (!selectedCells.Contains(cell))
                    {
                        selectedCells.Add(cell);
                        addedCount++;
                    }
                }

                // Switch to manual mode
                currentMode = ZoneCreationMode.Manual;

                TolkHelper.Speak($"Filled interior with {addedCount} cells. Total: {selectedCells.Count} cells. Now in manual mode");
                Log.Message($"Borders mode auto-fill: added {addedCount} interior cells, total {selectedCells.Count}");
            }
            catch (System.Exception ex)
            {
                TolkHelper.Speak($"Error filling interior: {ex.Message}", SpeechPriority.High);
                Log.Error($"BordersModeAutoFill error: {ex}");
            }
        }

        /// <summary>
        /// Auto-fills a rectangular zone from 4 corner cells.
        /// Switches to Manual mode after completion.
        /// </summary>
        public static void CornersModeAutoFill(Map map)
        {
            if (selectedCells.Count != 4)
            {
                TolkHelper.Speak($"Must select exactly 4 corners. Currently selected: {selectedCells.Count}", SpeechPriority.High);
                return;
            }

            try
            {
                // Find min and max X and Z coordinates
                int minX = int.MaxValue, maxX = int.MinValue;
                int minZ = int.MaxValue, maxZ = int.MinValue;

                foreach (IntVec3 corner in selectedCells)
                {
                    if (corner.x < minX) minX = corner.x;
                    if (corner.x > maxX) maxX = corner.x;
                    if (corner.z < minZ) minZ = corner.z;
                    if (corner.z > maxZ) maxZ = corner.z;
                }

                // Validate rectangle size
                if (minX >= maxX || minZ >= maxZ)
                {
                    TolkHelper.Speak("Invalid corner selection. Corners must form a rectangle", SpeechPriority.High);
                    return;
                }

                // Fill all cells in the bounding rectangle
                List<IntVec3> rectangleCells = new List<IntVec3>();
                for (int x = minX; x <= maxX; x++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        IntVec3 cell = new IntVec3(x, 0, z);
                        if (cell.InBounds(map) && !selectedCells.Contains(cell))
                        {
                            rectangleCells.Add(cell);
                        }
                    }
                }

                // Add all rectangle cells to selection
                selectedCells.AddRange(rectangleCells);

                // Switch to manual mode
                currentMode = ZoneCreationMode.Manual;

                int width = maxX - minX + 1;
                int height = maxZ - minZ + 1;
                TolkHelper.Speak($"Filled {width} by {height} rectangle. Total: {selectedCells.Count} cells. Now in manual mode");
                Log.Message($"Corners mode auto-fill: {width}x{height} rectangle, total {selectedCells.Count} cells");
            }
            catch (System.Exception ex)
            {
                TolkHelper.Speak($"Error filling rectangle: {ex.Message}", SpeechPriority.High);
                Log.Error($"CornersModeAutoFill error: {ex}");
            }
        }

        /// <summary>
        /// Auto-selects cells in a direction until hitting a wall or impassable terrain.
        /// Adds cells to the existing selection.
        /// </summary>
        public static void AutoSelectToWall(IntVec3 startPosition, Rot4 direction, Map map)
        {
            try
            {
                List<IntVec3> lineCells = new List<IntVec3>();
                IntVec3 currentCell = startPosition + direction.FacingCell;

                // Move in the direction until we hit a wall or go out of bounds
                while (currentCell.InBounds(map) && !currentCell.Impassable(map))
                {
                    lineCells.Add(currentCell);
                    currentCell += direction.FacingCell;
                }

                // Add all cells to selection
                int addedCount = 0;
                foreach (IntVec3 cell in lineCells)
                {
                    if (!selectedCells.Contains(cell))
                    {
                        selectedCells.Add(cell);
                        addedCount++;
                    }
                }

                string directionName = direction.ToStringHuman();
                TolkHelper.Speak($"Selected {addedCount} cells to {directionName}. Total: {selectedCells.Count}");
                Log.Message($"Auto-select to wall: {addedCount} cells in direction {directionName}");
            }
            catch (System.Exception ex)
            {
                TolkHelper.Speak($"Error auto-selecting: {ex.Message}", SpeechPriority.High);
                Log.Error($"AutoSelectToWall error: {ex}");
            }
        }

        /// <summary>
        /// Resets the state, exiting creation mode.
        /// </summary>
        public static void Reset()
        {
            isInCreationMode = false;
            selectedCells.Clear();
            expandingZone = null;
        }

        /// <summary>
        /// Gets a human-readable name for a zone type.
        /// </summary>
        private static string GetZoneTypeName(ZoneType type)
        {
            switch (type)
            {
                case ZoneType.Stockpile:
                    return "stockpile zone";
                case ZoneType.DumpingStockpile:
                    return "dumping stockpile zone";
                case ZoneType.GrowingZone:
                    return "growing zone";
                case ZoneType.AllowedArea:
                    return "allowed area";
                case ZoneType.HomeZone:
                    return "home zone";
                default:
                    return "zone";
            }
        }

        /// <summary>
        /// Gets a human-readable name for a zone creation mode.
        /// </summary>
        private static string GetModeName(ZoneCreationMode mode)
        {
            switch (mode)
            {
                case ZoneCreationMode.Manual:
                    return "manual";
                case ZoneCreationMode.Borders:
                    return "borders";
                case ZoneCreationMode.Corners:
                    return "corners";
                default:
                    return "unknown";
            }
        }
    }
}
