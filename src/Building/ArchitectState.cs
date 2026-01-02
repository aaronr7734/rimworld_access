using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Defines the modes of the architect system.
    /// </summary>
    public enum ArchitectMode
    {
        Inactive,           // Not in architect mode
        CategorySelection,  // Selecting a category (Orders, Structure, etc.)
        ToolSelection,      // Selecting a tool within a category
        MaterialSelection,  // Selecting material for construction
        PlacementMode       // Placing designations on the map
    }

    /// <summary>
    /// Maintains state for the accessible architect system.
    /// Tracks current mode, selected category, designator, and placement state.
    /// </summary>
    public static class ArchitectState
    {
        private static ArchitectMode currentMode = ArchitectMode.Inactive;
        private static DesignationCategoryDef selectedCategory = null;
        private static Designator selectedDesignator = null;
        private static BuildableDef selectedBuildable = null;
        private static ThingDef selectedMaterial = null;
        private static List<IntVec3> selectedCells = new List<IntVec3>();
        private static Rot4 currentRotation = Rot4.North;

        // Rectangle selection state for zone designators
        private static IntVec3? rectangleStart = null;
        private static IntVec3? rectangleEnd = null;
        private static List<IntVec3> previewCells = new List<IntVec3>();
        private static int lastCellCount = 0;
        private static float lastDragRealTime = 0f;

        // Reflection field info for accessing protected placingRot field
        private static FieldInfo placingRotField = AccessTools.Field(typeof(Designator_Place), "placingRot");

        /// <summary>
        /// Gets the current architect mode.
        /// </summary>
        public static ArchitectMode CurrentMode => currentMode;

        /// <summary>
        /// Gets the currently selected category.
        /// </summary>
        public static DesignationCategoryDef SelectedCategory => selectedCategory;

        /// <summary>
        /// Gets the currently selected designator.
        /// </summary>
        public static Designator SelectedDesignator => selectedDesignator;

        /// <summary>
        /// Gets the currently selected buildable (for construction).
        /// </summary>
        public static BuildableDef SelectedBuildable => selectedBuildable;

        /// <summary>
        /// Gets the currently selected material (for construction).
        /// </summary>
        public static ThingDef SelectedMaterial => selectedMaterial;

        /// <summary>
        /// Gets the list of selected cells for placement.
        /// </summary>
        public static List<IntVec3> SelectedCells => selectedCells;

        /// <summary>
        /// Gets or sets the current rotation for building placement.
        /// </summary>
        public static Rot4 CurrentRotation
        {
            get => currentRotation;
            set => currentRotation = value;
        }

        /// <summary>
        /// Whether architect mode is currently active (any mode except Inactive).
        /// </summary>
        public static bool IsActive => currentMode != ArchitectMode.Inactive;

        /// <summary>
        /// Whether we're currently in placement mode on the map.
        /// </summary>
        public static bool IsInPlacementMode => currentMode == ArchitectMode.PlacementMode;

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
        /// Enters category selection mode.
        /// </summary>
        public static void EnterCategorySelection()
        {
            currentMode = ArchitectMode.CategorySelection;
            selectedCategory = null;
            selectedDesignator = null;
            selectedBuildable = null;
            selectedMaterial = null;
            selectedCells.Clear();

            Log.Message("Entered architect category selection");
        }

        /// <summary>
        /// Enters tool selection mode for a specific category.
        /// </summary>
        public static void EnterToolSelection(DesignationCategoryDef category)
        {
            currentMode = ArchitectMode.ToolSelection;
            selectedCategory = category;
            selectedDesignator = null;
            selectedBuildable = null;
            selectedMaterial = null;
            selectedCells.Clear();

            TolkHelper.Speak($"{category.LabelCap} category selected. Choose a tool");
            Log.Message($"Entered tool selection for category: {category.defName}");
        }

        /// <summary>
        /// Enters material selection mode for a buildable that requires stuff.
        /// </summary>
        public static void EnterMaterialSelection(BuildableDef buildable, Designator designator)
        {
            currentMode = ArchitectMode.MaterialSelection;
            selectedBuildable = buildable;
            selectedDesignator = designator;
            selectedMaterial = null;
            selectedCells.Clear();

            TolkHelper.Speak($"Select material for {buildable.label}");
            Log.Message($"Entered material selection for: {buildable.defName}");
        }

        /// <summary>
        /// Enters placement mode with the selected designator.
        /// </summary>
        public static void EnterPlacementMode(Designator designator, ThingDef material = null)
        {
            currentMode = ArchitectMode.PlacementMode;
            selectedDesignator = designator;
            selectedMaterial = material;
            selectedCells.Clear();

            // Reset rotation to North when entering placement mode
            currentRotation = Rot4.North;

            // Set the designator as selected in the game's DesignatorManager
            if (Find.DesignatorManager != null)
            {
                Find.DesignatorManager.Select(designator);
            }

            // If this is a build designator, set its rotation via reflection
            if (designator is Designator_Build buildDesignator)
            {
                if (placingRotField != null)
                {
                    placingRotField.SetValue(buildDesignator, currentRotation);
                }
            }

            string toolName = designator.Label;
            string announcement = GetPlacementAnnouncement(designator);
            TolkHelper.Speak(announcement);
            Log.Message($"Entered placement mode with designator: {toolName}");
        }

        /// <summary>
        /// Rotates the current building clockwise.
        /// </summary>
        public static void RotateBuilding()
        {
            if (!IsInPlacementMode || !(selectedDesignator is Designator_Build buildDesignator))
                return;

            // Rotate clockwise
            currentRotation.Rotate(RotationDirection.Clockwise);

            // Set rotation on the designator via reflection
            if (placingRotField != null)
            {
                placingRotField.SetValue(buildDesignator, currentRotation);
            }

            // Announce new rotation and spatial info
            string announcement = GetRotationAnnouncement(buildDesignator);
            TolkHelper.Speak(announcement);
            Log.Message($"Rotated building to: {currentRotation}");
        }

        /// <summary>
        /// Gets the initial placement announcement including size and rotation info.
        /// </summary>
        private static string GetPlacementAnnouncement(Designator designator)
        {
            if (!(designator is Designator_Build buildDesignator))
            {
                return $"{designator.Label} selected. Press Space to designate tiles, Enter to confirm, Escape to cancel";
            }

            BuildableDef entDef = buildDesignator.PlacingDef;
            IntVec2 size = entDef.Size;

            string sizeInfo = GetSizeDescription(size, currentRotation);
            string specialRequirements = GetSpecialSpatialRequirements(entDef, currentRotation);
            string controlInfo = "Press Space to place, R to rotate, Escape to cancel";

            if (!string.IsNullOrEmpty(specialRequirements))
            {
                return $"{designator.Label} selected. {sizeInfo}. {specialRequirements}. {controlInfo}";
            }

            return $"{designator.Label} selected. {sizeInfo}. {controlInfo}";
        }

        /// <summary>
        /// Gets rotation announcement including size and spatial requirements.
        /// </summary>
        private static string GetRotationAnnouncement(Designator_Build buildDesignator)
        {
            BuildableDef entDef = buildDesignator.PlacingDef;
            IntVec2 size = entDef.Size;

            string sizeInfo = GetSizeDescription(size, currentRotation);
            string rotationName = GetRotationName(currentRotation);
            string specialRequirements = GetSpecialSpatialRequirements(entDef, currentRotation);

            if (!string.IsNullOrEmpty(specialRequirements))
            {
                return $"Facing {rotationName}. {sizeInfo}. {specialRequirements}";
            }

            return $"Facing {rotationName}. {sizeInfo}";
        }

        /// <summary>
        /// Gets special spatial requirements for buildings like wind turbines and coolers.
        /// </summary>
        private static string GetSpecialSpatialRequirements(BuildableDef def, Rot4 rotation)
        {
            if (def == null || !(def is ThingDef thingDef))
                return null;

            string defName = thingDef.defName.ToLower();

            // Check for wind turbine
            if (defName.Contains("windturbine"))
            {
                return GetWindTurbineRequirements(rotation);
            }

            // Check for cooler
            if (defName.Contains("cooler"))
            {
                return GetCoolerRequirements(rotation);
            }

            return null;
        }

        /// <summary>
        /// Gets spatial requirements for wind turbines.
        /// </summary>
        private static string GetWindTurbineRequirements(Rot4 rotation)
        {
            // Wind turbines need clear space in front and behind
            // The exact distances vary based on rotation
            if (rotation == Rot4.North)
            {
                return "Requires clear space: 9 tiles north, 5 tiles south";
            }
            else if (rotation == Rot4.East)
            {
                return "Requires clear space: 9 tiles east, 5 tiles west";
            }
            else if (rotation == Rot4.South)
            {
                return "Requires clear space: 5 tiles north, 9 tiles south";
            }
            else // West
            {
                return "Requires clear space: 5 tiles east, 9 tiles west";
            }
        }

        /// <summary>
        /// Gets spatial requirements for coolers.
        /// </summary>
        private static string GetCoolerRequirements(Rot4 rotation)
        {
            // Coolers have a hot side (front) and cold side (back)
            // Hot side is North relative to rotation, cold side is South
            IntVec3 hotSide = IntVec3.North.RotatedBy(rotation);
            IntVec3 coldSide = IntVec3.South.RotatedBy(rotation);

            string hotDir = GetDirectionName(hotSide);
            string coldDir = GetDirectionName(coldSide);

            return $"Hot exhaust to {hotDir}, cold air to {coldDir}";
        }

        /// <summary>
        /// Gets a direction name from an IntVec3 offset.
        /// </summary>
        private static string GetDirectionName(IntVec3 offset)
        {
            if (offset == IntVec3.North) return "north";
            if (offset == IntVec3.South) return "south";
            if (offset == IntVec3.East) return "east";
            if (offset == IntVec3.West) return "west";
            return "unknown";
        }

        /// <summary>
        /// Gets a human-readable description of the building size and occupied tiles.
        /// </summary>
        private static string GetSizeDescription(IntVec2 size, Rot4 rotation)
        {
            // Adjust size for rotation (horizontal rotations swap x and z)
            int width = size.x;
            int depth = size.z;

            if (rotation.IsHorizontal)
            {
                int temp = width;
                width = depth;
                depth = temp;
            }

            if (width == 1 && depth == 1)
            {
                return "Size: 1 tile";
            }

            // Build relative description
            List<string> parts = new List<string>();
            parts.Add($"Size: {width} by {depth}");

            // Describe occupied tiles relative to cursor
            if (width > 1 || depth > 1)
            {
                List<string> directions = new List<string>();

                // Calculate how many tiles extend in each direction from center
                int eastTiles = (width - 1) / 2;
                int westTiles = width - 1 - eastTiles;
                int northTiles = (depth - 1) / 2;
                int southTiles = depth - 1 - northTiles;

                if (northTiles > 0)
                    directions.Add($"{northTiles} north");
                if (southTiles > 0)
                    directions.Add($"{southTiles} south");
                if (eastTiles > 0)
                    directions.Add($"{eastTiles} east");
                if (westTiles > 0)
                    directions.Add($"{westTiles} west");

                if (directions.Count > 0)
                    parts.Add("Extends " + string.Join(", ", directions));
            }

            return string.Join(". ", parts);
        }

        /// <summary>
        /// Gets a human-readable rotation name.
        /// </summary>
        private static string GetRotationName(Rot4 rotation)
        {
            if (rotation == Rot4.North) return "North";
            if (rotation == Rot4.East) return "East";
            if (rotation == Rot4.South) return "South";
            if (rotation == Rot4.West) return "West";
            return rotation.ToString();
        }

        /// <summary>
        /// Adds a cell to the selection if valid for the current designator.
        /// </summary>
        public static void ToggleCell(IntVec3 cell)
        {
            if (selectedDesignator == null)
                return;

            // Check if this designator can designate this cell
            AcceptanceReport report = selectedDesignator.CanDesignateCell(cell);

            if (selectedCells.Contains(cell))
            {
                // Remove cell
                selectedCells.Remove(cell);
                TolkHelper.Speak($"Deselected, {cell.x}, {cell.z}");
            }
            else if (report.Accepted)
            {
                // Add cell
                selectedCells.Add(cell);
                TolkHelper.Speak($"Selected, {cell.x}, {cell.z}");
            }
            else
            {
                // Cannot designate this cell
                string reason = report.Reason ?? "Cannot designate here";
                TolkHelper.Speak($"Invalid: {reason}");
            }
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

            // Play native sound when cell count changes (like DesignationDragger)
            if (cellCount != lastCellCount)
            {
                SoundInfo info = SoundInfo.OnCamera();
                info.SetParameter("TimeSinceDrag", Time.realtimeSinceStartup - lastDragRealTime);
                SoundDefOf.Designate_DragStandard_Changed.PlayOneShot(info);
                lastDragRealTime = Time.realtimeSinceStartup;
                lastCellCount = cellCount;

                // Announce dimensions like native display (only when >= 5 cells)
                if (width >= 5 || height >= 5)
                {
                    TolkHelper.Speak($"{width} by {height}", SpeechPriority.Low);
                }
                else if (cellCount >= 4)
                {
                    TolkHelper.Speak($"{cellCount}", SpeechPriority.Low);
                }
            }
        }

        /// <summary>
        /// Confirms the current rectangle preview, adding all cells to selection.
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
        /// Executes the placement (designates all selected cells).
        /// </summary>
        public static void ExecutePlacement(Map map)
        {
            if (selectedDesignator == null || selectedCells.Count == 0)
            {
                TolkHelper.Speak("No tiles selected");
                Cancel();
                return;
            }

            try
            {
                // Use the designator's DesignateMultiCell method
                selectedDesignator.DesignateMultiCell(selectedCells);

                string toolName = selectedDesignator.Label;
                TolkHelper.Speak($"{toolName} placed on {selectedCells.Count} tiles");
                Log.Message($"Executed placement: {toolName} on {selectedCells.Count} tiles");
            }
            catch (System.Exception ex)
            {
                TolkHelper.Speak($"Error placing designation: {ex.Message}", SpeechPriority.High);
                Log.Error($"Error in ExecutePlacement: {ex}");
            }
            finally
            {
                Reset();
            }
        }

        /// <summary>
        /// Cancels the current operation and fully exits architect mode.
        /// </summary>
        public static void Cancel()
        {
            TolkHelper.Speak("Architect menu closed");

            // Always fully close architect mode
            Reset();
        }

        /// <summary>
        /// Checks if the current designator is a zone/area/cell-based designator.
        /// This includes zones (stockpiles, growing zones), areas (home, roof), and other multi-cell designators.
        /// </summary>
        public static bool IsZoneDesignator()
        {
            if (selectedDesignator == null)
                return false;

            // Check if this designator's type hierarchy includes "Designator_Cells"
            // This covers all multi-cell designators: zones, areas, roofs, etc.
            System.Type type = selectedDesignator.GetType();
            while (type != null)
            {
                if (type.Name == "Designator_Cells")
                    return true;
                type = type.BaseType;
            }

            return false;
        }

        /// <summary>
        /// Resets the architect state completely.
        /// </summary>
        public static void Reset()
        {
            currentMode = ArchitectMode.Inactive;
            selectedCategory = null;
            selectedDesignator = null;
            selectedBuildable = null;
            selectedMaterial = null;
            selectedCells.Clear();
            currentRotation = Rot4.North;

            // Clear rectangle selection state
            rectangleStart = null;
            rectangleEnd = null;
            previewCells.Clear();
            lastCellCount = 0;

            // Deselect any active designator in the game
            if (Find.DesignatorManager != null)
            {
                Find.DesignatorManager.Deselect();
            }

            Log.Message("Architect state reset");
        }
    }
}
