using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Sound;
using RimWorld;
using UnityEngine;

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
    /// Maintains state for zone creation mode.
    /// Tracks which cells have been selected and what type of zone to create.
    /// Uses RimWorld's native APIs for rectangle selection feedback.
    /// </summary>
    public static class ZoneCreationState
    {
        private static bool isInCreationMode = false;
        private static ZoneType selectedZoneType = ZoneType.Stockpile;
        private static List<IntVec3> selectedCells = new List<IntVec3>();
        private static Zone expandingZone = null; // Track zone being expanded
        private static bool isShrinking = false; // true = shrink mode (selected cells will be removed)
        private static string pendingAllowedAreaName = null; // Store name for allowed area creation

        // Rectangle selection state
        private static IntVec3? rectangleStart = null;
        private static IntVec3? rectangleEnd = null;
        private static List<IntVec3> previewCells = new List<IntVec3>();

        // For native sound feedback (matches DesignationDragger behavior)
        private static int lastCellCount = 0;
        private static float lastDragRealTime = 0f;

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
        /// Whether a rectangle start corner has been set.
        /// </summary>
        public static bool HasRectangleStart => rectangleStart.HasValue;

        /// <summary>
        /// Whether we are actively previewing a rectangle (start and end set).
        /// </summary>
        public static bool IsInPreviewMode => rectangleStart.HasValue && rectangleEnd.HasValue;

        /// <summary>
        /// The start corner of the rectangle being selected.
        /// </summary>
        public static IntVec3? RectangleStart => rectangleStart;

        /// <summary>
        /// The end corner of the rectangle being selected.
        /// </summary>
        public static IntVec3? RectangleEnd => rectangleEnd;

        /// <summary>
        /// Cells in the current rectangle preview.
        /// </summary>
        public static IReadOnlyList<IntVec3> PreviewCells => previewCells;

        /// <summary>
        /// List of cells that have been selected for the zone.
        /// </summary>
        public static List<IntVec3> SelectedCells => selectedCells;

        /// <summary>
        /// Whether we're in shrink mode (selected cells will be removed from zone).
        /// </summary>
        public static bool IsShrinking => isShrinking;

        /// <summary>
        /// Sets the pending name for an allowed area that will be created.
        /// </summary>
        public static void SetPendingAllowedAreaName(string name)
        {
            pendingAllowedAreaName = name;
            Log.Message($"Set pending allowed area name: {name}");
        }

        /// <summary>
        /// Enters zone creation mode with the specified zone type.
        /// Uses rectangle selection: Space sets corners, arrows preview, Space confirms rectangle.
        /// </summary>
        public static void EnterCreationMode(ZoneType zoneType)
        {
            isInCreationMode = true;
            selectedZoneType = zoneType;
            selectedCells.Clear();
            rectangleStart = null;
            rectangleEnd = null;
            previewCells.Clear();
            lastCellCount = 0;
            lastDragRealTime = Time.realtimeSinceStartup;

            string zoneName = GetZoneTypeName(zoneType);
            string instructions = "Press Space to set start corner, navigate to opposite corner, Space again to confirm rectangle. Enter to create zone, Escape to cancel.";

            TolkHelper.Speak($"Creating {zoneName}. {instructions}");
            Log.Message($"Entered zone creation mode: {zoneName}");
        }

        /// <summary>
        /// Adds a cell to the selection if not already selected.
        /// Used for individual cell selection (toggle mode during expansion).
        /// </summary>
        public static void AddCell(IntVec3 cell)
        {
            if (selectedCells.Contains(cell))
            {
                TolkHelper.Speak($"Already selected, {cell.x}, {cell.z}");
                return;
            }

            selectedCells.Add(cell);
            TolkHelper.Speak($"Selected, {cell.x}, {cell.z}");
        }

        /// <summary>
        /// Sets the start corner for rectangle selection.
        /// </summary>
        public static void SetRectangleStart(IntVec3 cell)
        {
            rectangleStart = cell;
            rectangleEnd = null;
            previewCells.Clear();
            lastCellCount = 0;
            lastDragRealTime = Time.realtimeSinceStartup;
            TolkHelper.Speak($"Start at {cell.x}, {cell.z}");
        }

        /// <summary>
        /// Updates the rectangle preview as the cursor moves.
        /// Plays native sound feedback when cell count changes.
        /// </summary>
        public static void UpdatePreview(IntVec3 endCell)
        {
            if (!rectangleStart.HasValue) return;

            rectangleEnd = endCell;

            // Use native CellRect API for rectangle calculation
            CellRect rect = CellRect.FromLimits(rectangleStart.Value, endCell);
            previewCells = rect.Cells.ToList();

            int width = rect.Width;
            int height = rect.Height;
            int cellCount = previewCells.Count;

            // Play native sound when cell count changes (like DesignationDragger line 119-124)
            if (cellCount != lastCellCount)
            {
                SoundInfo info = SoundInfo.OnCamera();
                info.SetParameter("TimeSinceDrag", Time.realtimeSinceStartup - lastDragRealTime);
                SoundDefOf.Designate_DragStandard_Changed.PlayOneShot(info);
                lastDragRealTime = Time.realtimeSinceStartup;
                lastCellCount = cellCount;

                // Announce dimensions like native display (only when >= 5 cells, line 165-171)
                if (width >= 5 || height >= 5)
                {
                    TolkHelper.Speak($"{width} by {height}", SpeechPriority.Low);
                }
                // Or announce cell count for smaller selections
                else if (cellCount >= 4)
                {
                    TolkHelper.Speak($"{cellCount}", SpeechPriority.Low);
                }
            }
        }

        /// <summary>
        /// Confirms the current rectangle preview, adding all cells to selection.
        /// Allows starting a new rectangle immediately.
        /// </summary>
        public static void ConfirmRectangle()
        {
            if (!IsInPreviewMode)
            {
                TolkHelper.Speak("No rectangle to confirm");
                return;
            }

            // Add preview cells to selection (avoiding duplicates)
            int addedCount = 0;
            foreach (var cell in previewCells)
            {
                if (!selectedCells.Contains(cell))
                {
                    selectedCells.Add(cell);
                    addedCount++;
                }
            }

            CellRect rect = CellRect.FromLimits(rectangleStart.Value, rectangleEnd.Value);
            TolkHelper.Speak($"{rect.Width} by {rect.Height}, {addedCount} cells added. Total: {selectedCells.Count}");

            // Clear rectangle state for next selection
            rectangleStart = null;
            rectangleEnd = null;
            previewCells.Clear();
            lastCellCount = 0;
        }

        /// <summary>
        /// Cancels the current rectangle selection without adding cells.
        /// </summary>
        public static void CancelRectangle()
        {
            if (!HasRectangleStart)
            {
                return;
            }

            rectangleStart = null;
            rectangleEnd = null;
            previewCells.Clear();
            lastCellCount = 0;
            TolkHelper.Speak("Rectangle cancelled");
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
            isShrinking = false;
            selectedCells.Clear();
            rectangleStart = null;
            rectangleEnd = null;
            previewCells.Clear();
            lastCellCount = 0;
            lastDragRealTime = Time.realtimeSinceStartup;

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

            string instructions = "Press Space to set corners, Enter to confirm, Escape to cancel.";
            TolkHelper.Speak($"Expanding {zone.label}. {selectedCells.Count} tiles currently selected. {instructions}");
            Log.Message($"Entered expansion mode for zone: {zone.label}. Pre-selected {selectedCells.Count} existing tiles");
        }

        /// <summary>
        /// Enters shrink mode for an existing zone.
        /// Selected cells will be removed from the zone on confirm.
        /// </summary>
        public static void EnterShrinkMode(Zone zone)
        {
            if (zone == null)
            {
                TolkHelper.Speak("Cannot shrink: no zone provided", SpeechPriority.High);
                Log.Error("EnterShrinkMode called with null zone");
                return;
            }

            isInCreationMode = true;
            expandingZone = zone;
            isShrinking = true;
            selectedCells.Clear(); // Start with empty selection - selected cells will be removed
            rectangleStart = null;
            rectangleEnd = null;
            previewCells.Clear();
            lastCellCount = 0;
            lastDragRealTime = Time.realtimeSinceStartup;

            string instructions = "Press Space to set corners, Enter to confirm, Escape to cancel.";
            TolkHelper.Speak($"Shrinking {zone.label}. Select cells to remove. {instructions}");
            Log.Message($"Entered shrink mode for zone: {zone.label}");
        }

        /// <summary>
        /// Creates the zone with all selected cells and exits creation mode.
        /// If in expansion mode, adds cells to existing zone instead.
        /// If in shrink mode, removes selected cells from zone.
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
        /// Updates the expanding zone based on selected cells.
        /// In expand mode: adds new cells, removes deselected cells.
        /// In shrink mode: removes selected cells.
        /// </summary>
        private static void ExpandZone(Map map)
        {
            if (expandingZone == null)
            {
                TolkHelper.Speak("Error: No zone to modify", SpeechPriority.High);
                Log.Error("ExpandZone called but expandingZone is null");
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

                // Check for disconnected fragments AFTER all modifications (matches standard RimWorld behavior)
                expandingZone.CheckContiguous();

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
        /// Removes selected cells from the zone (shrink mode).
        /// </summary>
        private static void ShrinkZone(Map map)
        {
            if (selectedCells.Count == 0)
            {
                TolkHelper.Speak("No cells selected. Zone unchanged.");
                Reset();
                return;
            }

            try
            {
                int removedCount = 0;

                // Remove selected cells from the zone
                foreach (IntVec3 cell in selectedCells)
                {
                    if (expandingZone.ContainsCell(cell))
                    {
                        expandingZone.RemoveCell(cell);
                        removedCount++;
                    }
                }

                // Check if zone is now empty
                if (expandingZone.Cells.Count() == 0)
                {
                    TolkHelper.Speak($"All cells removed. {expandingZone.label} deleted.");
                    expandingZone.Delete();
                }
                else
                {
                    // Check for disconnected fragments
                    expandingZone.CheckContiguous();
                    TolkHelper.Speak($"Removed {removedCount} cells from {expandingZone.label}. {expandingZone.Cells.Count()} cells remaining.");
                }

                Log.Message($"Shrunk zone {expandingZone?.label}: removed {removedCount} cells");
            }
            catch (System.Exception ex)
            {
                TolkHelper.Speak($"Error shrinking zone: {ex.Message}", SpeechPriority.High);
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
            TolkHelper.Speak("Zone creation cancelled");
            Log.Message("Zone creation cancelled");
            Reset();
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
            isShrinking = false;
            rectangleStart = null;
            rectangleEnd = null;
            previewCells.Clear();
            lastCellCount = 0;
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

    }
}
