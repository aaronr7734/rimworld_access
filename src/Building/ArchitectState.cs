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
    /// Defines the selection mode for architect placement.
    /// </summary>
    public enum ArchitectSelectionMode
    {
        BoxSelection,    // Space sets corners for rectangle selection
        SingleTile       // Space toggles individual tiles
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
        private static ArchitectSelectionMode selectionMode = ArchitectSelectionMode.BoxSelection; // Default to box selection

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
        /// Returns a read-only view to prevent external mutation.
        /// </summary>
        public static IReadOnlyList<IntVec3> SelectedCells => selectedCells.AsReadOnly();

        /// <summary>
        /// Clears the internal selected cells list.
        /// Used after placing a build designator at a single cell.
        /// </summary>
        public static void ClearSelectedCells()
        {
            selectedCells.Clear();
        }

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
        /// Defensive check: also verifies a designator is actually selected in the game.
        /// This prevents stale state if the designator was deselected externally.
        /// </summary>
        public static bool IsInPlacementMode =>
            currentMode == ArchitectMode.PlacementMode &&
            Find.DesignatorManager?.SelectedDesignator != null;

        /// <summary>
        /// Gets the current selection mode (BoxSelection or SingleTile).
        /// </summary>
        public static ArchitectSelectionMode SelectionMode => selectionMode;

        /// <summary>
        /// Toggles between box selection and single tile selection modes.
        /// Only applies to zone designators.
        /// </summary>
        public static void ToggleSelectionMode()
        {
            // Only allow toggling for zone designators
            if (!IsZoneDesignator())
            {
                TolkHelper.Speak("RimWorldAccess.Building.Architect.SelectionModeZoneOnly".Translate());
                return;
            }

            selectionMode = (selectionMode == ArchitectSelectionMode.BoxSelection)
                ? ArchitectSelectionMode.SingleTile
                : ArchitectSelectionMode.BoxSelection;

            string modeName = (selectionMode == ArchitectSelectionMode.BoxSelection)
                ? "RimWorldAccess.Building.Paint.ModeBox".Translate()
                : "RimWorldAccess.Building.Paint.ModeSingle".Translate();
            TolkHelper.Speak(modeName);
            Log.Message($"Architect placement: Switched to {modeName}");
        }

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

            TolkHelper.Speak("RimWorldAccess.Building.Architect.CategorySelected".Translate(category.LabelCap));
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

            TolkHelper.Speak("RimWorldAccess.Building.Architect.SelectMaterialFor".Translate(buildable.label));
            Log.Message($"Entered material selection for: {buildable.defName}");
        }

        /// <summary>
        /// Enters placement mode with the selected designator.
        /// This method sets up ArchitectState and calls DesignatorManager.Select().
        /// The DesignatorManagerPatch will handle entering ShapePlacementState or announcing manual mode.
        /// </summary>
        public static void EnterPlacementMode(Designator designator, ThingDef material = null)
        {
            // Close gizmo navigation when entering placement mode
            GizmoNavigationState.Close();

            currentMode = ArchitectMode.PlacementMode;
            selectedDesignator = designator;
            selectedMaterial = material;
            selectedCells.Clear();

            // Reset rotation to North when entering placement mode
            currentRotation = Rot4.North;

            // Reset selection mode to box selection
            selectionMode = ArchitectSelectionMode.BoxSelection;

            // If this is a place designator (build or install), set its rotation via reflection
            if (designator is Designator_Place placeDesignator)
            {
                if (placingRotField != null)
                {
                    placingRotField.SetValue(placeDesignator, currentRotation);
                }
            }

            string toolName = designator.Label;
            Log.Message($"Entered placement mode with designator: {toolName}");

            // Set the designator as selected in the game's DesignatorManager
            // The DesignatorManagerPatch.Postfix will handle entering accessible placement mode
            if (Find.DesignatorManager != null)
            {
                Find.DesignatorManager.Select(designator);

                // Sync our tracked rotation with the designator's actual rotation
                // (RimWorld's Selected() sets placingRot to PlacingDef.defaultPlacingRot)
                if (designator is Designator_Place placeDesignatorForSync && placingRotField != null)
                {
                    currentRotation = (Rot4)placingRotField.GetValue(placeDesignatorForSync);
                }
            }
        }

        /// <summary>
        /// Rotates the current building in the specified direction.
        /// Works with both Designator_Build (new construction) and Designator_Install (reinstall/install).
        /// </summary>
        public static void RotateBuilding(RotationDirection direction = RotationDirection.Clockwise)
        {
            if (!IsInPlacementMode || !(selectedDesignator is Designator_Place placeDesignator))
                return;

            currentRotation.Rotate(direction);

            // Set rotation on the designator via reflection
            if (placingRotField != null)
            {
                placingRotField.SetValue(placeDesignator, currentRotation);
            }

            // Announce new rotation and spatial info
            string announcement = GetRotationAnnouncementForDef(placeDesignator.PlacingDef, currentRotation);
            TolkHelper.Speak(announcement);
            Log.Message($"Rotated building to: {currentRotation}");
        }

        /// <summary>
        /// Gets rotation announcement for any BuildableDef at a given rotation.
        /// Shared by architect menu placement and gizmo-based placement (reinstall, etc.)
        /// </summary>
        internal static string GetRotationAnnouncementForDef(BuildableDef def, Rot4 rotation)
        {
            string facing = "RimWorldAccess.Building.Architect.Facing".Translate(GetRotationName(rotation));

            if (def == null)
                return facing;

            IntVec2 size = def.Size;
            string sizeInfo = GetSizeDescription(size, rotation);
            string specialRequirements = GetSpecialSpatialRequirements(def, rotation);

            return new AnnouncementBuilder()
                .Add(facing)
                .Add(sizeInfo)
                .Add(specialRequirements)
                .Build();
        }

        /// <summary>
        /// Gets special spatial requirements for buildings like wind turbines, coolers,
        /// beds (head position), fuel ports, TVs, and interaction cell buildings.
        /// </summary>
        private static string GetSpecialSpatialRequirements(BuildableDef def, Rot4 rotation)
        {
            if (def == null || !(def is ThingDef thingDef))
                return null;

            // Check for wind turbine (highest priority - needs clear space)
            // Uses PlaceWorker_WindTurbine check for proper game integration
            if (IsWindTurbine(thingDef))
            {
                return GetWindTurbineRequirements(rotation);
            }

            // Use BuildingCellHelper for all other special position info
            // (coolers, beds, fuel ports, TVs, vents, thrones, turrets, etc.)
            return BuildingCellHelper.GetPlacementPositionInfo(thingDef, rotation);
        }

        /// <summary>
        /// Checks if a ThingDef is a wind turbine by looking for PlaceWorker_WindTurbine.
        /// This is the proper game API for identifying wind turbines.
        /// </summary>
        private static bool IsWindTurbine(ThingDef def)
        {
            if (def?.placeWorkers == null)
                return false;

            foreach (var workerType in def.placeWorkers)
            {
                if (workerType == typeof(PlaceWorker_WindTurbine))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Wind turbine clear space requirements by rotation.
        /// Verified against WindTurbineUtility.CalculateWindCells() in game code:
        /// - North/East facing: 9 tiles front, 5 tiles back
        /// - South/West facing: 5 tiles front, 9 tiles back
        /// </summary>
        private static readonly Dictionary<Rot4, (int frontTiles, string frontDirKey, int backTiles, string backDirKey)> WindTurbineDirections = new Dictionary<Rot4, (int, string, int, string)>
        {
            { Rot4.North, (9, "RimWorldAccess.Map.Direction.Lower.North", 5, "RimWorldAccess.Map.Direction.Lower.South") },
            { Rot4.East, (9, "RimWorldAccess.Map.Direction.Lower.East", 5, "RimWorldAccess.Map.Direction.Lower.West") },
            { Rot4.South, (5, "RimWorldAccess.Map.Direction.Lower.North", 9, "RimWorldAccess.Map.Direction.Lower.South") },
            { Rot4.West, (5, "RimWorldAccess.Map.Direction.Lower.East", 9, "RimWorldAccess.Map.Direction.Lower.West") }
        };

        /// <summary>
        /// Gets spatial requirements for wind turbines.
        /// </summary>
        private static string GetWindTurbineRequirements(Rot4 rotation)
        {
            if (WindTurbineDirections.TryGetValue(rotation, out var directions))
            {
                string front = "RimWorldAccess.Building.Architect.TilesDirection".Translate(directions.frontTiles, directions.frontDirKey.Translate());
                string back = "RimWorldAccess.Building.Architect.TilesDirection".Translate(directions.backTiles, directions.backDirKey.Translate());
                return "RimWorldAccess.Building.Architect.WindTurbineClearSpace".Translate(front, back);
            }
            return "RimWorldAccess.Building.Architect.WindTurbineClearSpaceGeneric".Translate();
        }

        // NOTE: Cooler handling moved to BuildingCellHelper.GetCoolerDirectionInfo()
        // for consistent handling in both placement and cursor inspection.

        /// <summary>
        /// Gets a direction name from an IntVec3 offset.
        /// Delegates to BuildingCellHelper.GetCardinalDirection for consistency.
        /// </summary>
        private static string GetDirectionName(IntVec3 offset)
        {
            return BuildingCellHelper.GetCardinalDirection(offset) ?? "unknown";
        }

        /// <summary>
        /// Gets a human-readable description of the building size and occupied tiles.
        /// Uses RimWorld's GenAdj.OccupiedRect to accurately calculate where the building extends.
        /// </summary>
        internal static string GetSizeDescription(IntVec2 size, Rot4 rotation)
        {
            // Get cursor position for calculating actual occupied rect
            IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;

            // Use RimWorld's OccupiedRect to get the actual cells (accounts for rotation adjustments)
            CellRect occupiedRect = GenAdj.OccupiedRect(cursorPosition, rotation, size);

            int width = occupiedRect.Width;
            int depth = occupiedRect.Height;

            if (width == 1 && depth == 1)
            {
                return "RimWorldAccess.Building.Place.SizeOneTile".Translate();
            }

            // Build relative description
            var builder = new AnnouncementBuilder();
            builder.Add("RimWorldAccess.Building.Architect.SizeWxH".Translate(width, depth));

            // Calculate extends from cursor position to rect bounds
            if (width > 1 || depth > 1)
            {
                List<string> directions = new List<string>();

                // Calculate how many tiles extend in each direction from cursor
                int northTiles = occupiedRect.maxZ - cursorPosition.z;
                int southTiles = cursorPosition.z - occupiedRect.minZ;
                int eastTiles = occupiedRect.maxX - cursorPosition.x;
                int westTiles = cursorPosition.x - occupiedRect.minX;

                if (northTiles > 0)
                    directions.Add("RimWorldAccess.Building.Architect.ExtendDirection".Translate(northTiles, "RimWorldAccess.Map.Direction.Lower.North".Translate()));
                if (southTiles > 0)
                    directions.Add("RimWorldAccess.Building.Architect.ExtendDirection".Translate(southTiles, "RimWorldAccess.Map.Direction.Lower.South".Translate()));
                if (eastTiles > 0)
                    directions.Add("RimWorldAccess.Building.Architect.ExtendDirection".Translate(eastTiles, "RimWorldAccess.Map.Direction.Lower.East".Translate()));
                if (westTiles > 0)
                    directions.Add("RimWorldAccess.Building.Architect.ExtendDirection".Translate(westTiles, "RimWorldAccess.Map.Direction.Lower.West".Translate()));

                if (directions.Count > 0)
                    builder.Add("RimWorldAccess.Building.Architect.Extends".Translate(string.Join(", ", directions)));
            }

            return builder.Build();
        }

        /// <summary>
        /// Gets a human-readable rotation name.
        /// </summary>
        internal static string GetRotationName(Rot4 rotation)
        {
            if (rotation == Rot4.North) return "RimWorldAccess.Map.Direction.North".Translate();
            if (rotation == Rot4.East) return "RimWorldAccess.Map.Direction.East".Translate();
            if (rotation == Rot4.South) return "RimWorldAccess.Map.Direction.South".Translate();
            if (rotation == Rot4.West) return "RimWorldAccess.Map.Direction.West".Translate();
            return rotation.ToString();
        }

        /// <summary>
        /// Adds a cell to the selection if valid for the current designator.
        /// For zone designators, enforces adjacency requirements.
        /// </summary>
        public static void ToggleCell(IntVec3 cell)
        {
            if (selectedDesignator == null)
                return;

            // Check if this designator can designate this cell
            AcceptanceReport report = selectedDesignator.CanDesignateCell(cell);
            bool isZone = ShapeHelper.IsZoneDesignator(selectedDesignator);

            if (selectedCells.Contains(cell))
            {
                // Removing - check if it would disconnect the selection (for zones)
                if (isZone && selectedCells.Count > 1 && WouldDisconnectSelection(cell))
                {
                    TolkHelper.Speak("RimWorldAccess.Building.Create.CannotRemoveDisconnect".Translate());
                    return;
                }
                selectedCells.Remove(cell);
                TolkHelper.Speak("RimWorldAccess.Building.Paint.Deselected".Translate(cell.x, cell.z));
            }
            else if (report.Accepted)
            {
                // For zone designators, additional validation
                if (isZone)
                {
                    Map map = Find.CurrentMap;
                    if (map != null)
                    {
                        // Check if cell is already in ANY zone (RimWorld's CanDesignateCell allows same-type zones)
                        Zone existingZone = map.zoneManager.ZoneAt(cell);
                        if (existingZone != null)
                        {
                            TolkHelper.Speak("RimWorldAccess.Building.Create.CellAlreadyInZone".Translate(existingZone.label));
                            return;
                        }
                    }

                    // Enforce adjacency (except first cell)
                    if (selectedCells.Count > 0 && !IsAdjacentToSelection(cell))
                    {
                        TolkHelper.Speak("RimWorldAccess.Building.Create.MustBeAdjacentToSelection".Translate());
                        return;
                    }
                }
                selectedCells.Add(cell);
                TolkHelper.Speak(MapNavigationState.FormatSelectedCell(cell));
            }
            else
            {
                // Cannot designate this cell
                string reason = report.Reason ?? "RimWorldAccess.Building.Architect.CannotDesignateHere".Translate();
                TolkHelper.Speak("RimWorldAccess.Building.Architect.Invalid".Translate(reason));
            }
        }

        /// <summary>
        /// Checks if a cell is adjacent to any cell in the current selection.
        /// </summary>
        private static bool IsAdjacentToSelection(IntVec3 cell)
        {
            for (int i = 0; i < 4; i++)
            {
                IntVec3 neighbor = cell + GenAdj.CardinalDirections[i];
                if (selectedCells.Contains(neighbor))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if removing a cell would disconnect the remaining selection.
        /// Uses flood-fill algorithm.
        /// </summary>
        private static bool WouldDisconnectSelection(IntVec3 cellToRemove)
        {
            var remaining = new HashSet<IntVec3>(selectedCells);
            remaining.Remove(cellToRemove);

            if (remaining.Count == 0)
                return false;

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
        /// Executes the placement (designates all selected cells).
        /// </summary>
        public static void ExecutePlacement(Map map)
        {
            if (selectedDesignator == null || selectedCells.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Place.NoCellsSelected".Translate());
                Cancel();
                return;
            }

            try
            {
                // Use the designator's DesignateMultiCell method
                selectedDesignator.DesignateMultiCell(selectedCells);

                string toolName = selectedDesignator.Label;
                TolkHelper.Speak("RimWorldAccess.Building.Architect.PlacedOnCells".Translate(toolName, selectedCells.Count));
                Log.Message($"Executed placement: {toolName} on {selectedCells.Count} cells");
            }
            catch (System.Exception ex)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Architect.ErrorPlacing".Translate(ex.Message), SpeechPriority.High);
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
            TolkHelper.Speak("RimWorldAccess.Building.Architect.MenuClosed".Translate());

            // Always fully close architect mode
            Reset();
        }

        /// <summary>
        /// Checks if the current designator is a zone/area/cell-based designator.
        /// This includes zones (stockpiles, growing zones), areas (home, roof), and other multi-cell designators.
        /// Delegates to ShapeHelper for the type hierarchy check.
        /// </summary>
        public static bool IsZoneDesignator()
        {
            return ShapeHelper.IsCellsDesignator(selectedDesignator) || ShapeHelper.IsZoneDesignator(selectedDesignator);
        }

        /// <summary>
        /// Resets the architect state completely.
        /// Idempotent: safe to call multiple times, early exits if already inactive.
        /// </summary>
        public static void Reset()
        {
            // Early exit if already inactive - prevents redundant work and logging
            if (currentMode == ArchitectMode.Inactive)
            {
                return;
            }

            // Set mode to Inactive FIRST before calling Deselect()
            // This prevents infinite loops with DesignatorManagerDeselectPatch
            currentMode = ArchitectMode.Inactive;
            selectedCategory = null;
            selectedDesignator = null;
            selectedBuildable = null;
            selectedMaterial = null;
            selectedCells.Clear();
            currentRotation = Rot4.North;
            selectionMode = ArchitectSelectionMode.BoxSelection; // Reset to default mode

            // Deselect any active designator in the game
            if (Find.DesignatorManager != null)
            {
                Find.DesignatorManager.Deselect();
            }

            // Clear selection if it's a MinifiedThing (from inventory install)
            // This prevents stale MinifiedThing selection from affecting gizmo visibility
            if (Find.Selector?.SingleSelectedThing is MinifiedThing)
            {
                Find.Selector.ClearSelection();
            }

            Log.Message("Architect state reset");
        }
    }
}
