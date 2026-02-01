using System.Collections.Generic;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Defines the phases of the shape placement workflow.
    /// </summary>
    public enum PlacementPhase
    {
        /// <summary>Shape placement is not active</summary>
        Inactive,
        /// <summary>User is positioning the first point of the shape</summary>
        SettingFirstCorner,
        /// <summary>User is positioning the second point (shape preview updates live)</summary>
        SettingSecondCorner,
        /// <summary>Shape is defined, user is reviewing before placing</summary>
        Previewing
    }

    /// <summary>
    /// Contains the results of a shape placement operation.
    /// Tracks placed blueprints, obstacles, and resource costs for viewing mode.
    /// </summary>
    public class PlacementResult
    {
        /// <summary>Number of blueprints successfully placed</summary>
        public int PlacedCount { get; set; }

        /// <summary>Number of cells that could not be designated due to obstacles</summary>
        public int ObstacleCount { get; set; }

        /// <summary>Cells where blueprints were successfully placed</summary>
        public List<IntVec3> PlacedCells { get; set; }

        /// <summary>Cells that could not be designated (blocked by existing things)</summary>
        public List<IntVec3> ObstacleCells { get; set; }

        /// <summary>Total resource cost for all placed blueprints</summary>
        public int TotalResourceCost { get; set; }

        /// <summary>Name of the primary resource (e.g., "wood", "steel")</summary>
        public string ResourceName { get; set; }

        /// <summary>List of placed blueprint Things for undo functionality</summary>
        public List<Thing> PlacedBlueprints { get; set; }

        /// <summary>
        /// True if this operation would delete an entire zone and needs confirmation.
        /// When true, the result contains no placements - caller should show warning dialog.
        /// </summary>
        public bool NeedsFullDeletionConfirmation { get; set; }

        /// <summary>
        /// The zone that would be deleted if NeedsFullDeletionConfirmation is true.
        /// </summary>
        public Zone ZonePendingDeletion { get; set; }

        /// <summary>
        /// The valid cells that would delete the zone if NeedsFullDeletionConfirmation is true.
        /// </summary>
        public List<IntVec3> PendingValidCells { get; set; }

        /// <summary>
        /// Creates a new empty PlacementResult.
        /// </summary>
        public PlacementResult()
        {
            PlacedCells = new List<IntVec3>();
            ObstacleCells = new List<IntVec3>();
            PlacedBlueprints = new List<Thing>();
            PendingValidCells = new List<IntVec3>();
            ResourceName = string.Empty;
        }
    }

    /// <summary>
    /// State machine for two-point shape-based building placement.
    /// Manages the workflow: Enter -> SetFirstPoint -> SetSecondPoint/UpdatePreview -> PlaceBlueprints.
    /// </summary>
    public static class ShapePlacementState
    {
        // Shared preview helper for shape calculations and sound feedback
        private static readonly ShapePreviewHelper previewHelper = new ShapePreviewHelper();

        // State tracking
        private static PlacementPhase currentPhase = PlacementPhase.Inactive;
        private static ShapeType currentShape = ShapeType.Manual;
        private static Designator activeDesignator = null;

        // Stack tracking - whether we can return to viewing mode on exit
        private static bool hasViewingModeOnStack = false;

        // Cursor position when entering shape mode - used for zone expand/create decision
        // This ensures the zone selection matches what was announced on entry
        private static IntVec3 entryCursorPosition = IntVec3.Invalid;

        // Mapping of designator name keywords to gerund action phrases
        private static readonly Dictionary<string, string> DesignatorActionMap = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "haul", "hauling" },
            { "hunt", "hunting" },
            { "mine", "mining" },
            { "deconstruct", "deconstruction" },
            { "cut", "cutting" },
            { "smooth", "smoothing" },
            { "tame", "taming" },
            { "cancel", "cancellation" }
        };

        #region Properties

        /// <summary>
        /// Whether shape placement is currently active.
        /// Defensive check: also verifies a designator is actually selected in the game.
        /// This prevents stale state if the designator was deselected externally.
        /// </summary>
        public static bool IsActive =>
            currentPhase != PlacementPhase.Inactive &&
            Find.DesignatorManager?.SelectedDesignator != null;

        /// <summary>
        /// The current phase of the placement workflow.
        /// </summary>
        public static PlacementPhase CurrentPhase => currentPhase;

        /// <summary>
        /// The currently selected shape type.
        /// </summary>
        public static ShapeType CurrentShape => currentShape;

        /// <summary>
        /// The first point of the shape (origin point).
        /// </summary>
        public static IntVec3? FirstPoint => previewHelper.FirstCorner;

        /// <summary>
        /// The second point of the shape (target point).
        /// </summary>
        public static IntVec3? SecondPoint => previewHelper.SecondCorner;

        /// <summary>
        /// The cells that make up the current shape preview.
        /// Updated as the cursor moves during SettingSecondCorner phase.
        /// </summary>
        public static IReadOnlyList<IntVec3> PreviewCells => previewHelper.PreviewCells;

        /// <summary>
        /// Whether the first point has been set.
        /// </summary>
        public static bool HasFirstPoint => previewHelper.HasFirstCorner;

        /// <summary>
        /// Whether placement is in progress (active with points set).
        /// Use this to guard against state corruption from external actions.
        /// </summary>
        public static bool IsPlacementInProgress => IsActive && HasFirstPoint;

        /// <summary>
        /// Whether we're in preview mode (both points set).
        /// </summary>
        public static bool IsInPreviewMode => previewHelper.IsInPreviewMode;

        /// <summary>
        /// The designator being used for placement.
        /// </summary>
        public static Designator ActiveDesignator => activeDesignator;

        /// <summary>
        /// Whether there's a viewing mode state on the stack to return to.
        /// </summary>
        public static bool HasViewingModeOnStack => hasViewingModeOnStack;

        #endregion

        #region State Management

        /// <summary>
        /// Enters shape placement mode with the specified designator and shape.
        /// </summary>
        /// <param name="designator">The designator to use for placement</param>
        /// <param name="shape">The shape type for the placement</param>
        /// <param name="fromViewingMode">Whether we're entering from viewing mode (to support returning on Escape)</param>
        public static void Enter(Designator designator, ShapeType shape, bool fromViewingMode = false)
        {
            activeDesignator = designator;
            currentShape = shape;
            currentPhase = PlacementPhase.SettingFirstCorner;
            previewHelper.Reset();
            previewHelper.SetCurrentShape(shape);
            hasViewingModeOnStack = fromViewingMode;

            // Sync shape selection to game's SelectedStyle for "Remember Draw Styles" setting
            SyncShapeToGameStyle(designator, shape);

            // Store cursor position for zone expand/create decision
            // This ensures the zone selection matches what's announced on entry
            entryCursorPosition = MapNavigationState.CurrentCursorPosition;

            // For zone designators, clear any existing zone selection.
            // The expand/create decision should be based purely on cursor position,
            // not on what zone happens to be selected from a previous operation.
            // This ensures the actual behavior matches the announcement made on entry.
            // Note: The gizmo-based expand flow uses GizmoZoneEditState, which preserves selection.
            if (ShapeHelper.IsZoneDesignator(designator))
            {
                Find.Selector.ClearSelection();
            }

            string shapeName = ShapeHelper.GetShapeName(shape);
            string designatorLabel = ArchitectHelper.GetSanitizedLabel(designator);

            // Build a comprehensive announcement with size, rotation, and key hints
            string announcement = BuildEnterAnnouncement(designator, designatorLabel, shape, shapeName);
            TolkHelper.Speak(announcement);

            Log.Message($"[ShapePlacementState] Entered with shape {shape} for designator {designatorLabel}, viewingModeOnStack={fromViewingMode}");
        }

        /// <summary>
        /// Builds the announcement for entering shape placement mode.
        /// Includes mode, shape selection, item name, size info, rotation/facing info, and key hints.
        /// Format: "Shape mode. {Shape} selected. {Item info}. {Hints}."
        /// </summary>
        private static string BuildEnterAnnouncement(Designator designator, string designatorLabel, ShapeType shape, string shapeName)
        {
            List<string> parts = new List<string>();

            // Mode and shape selection - clear separation for screen reader clarity
            if (shape == ShapeType.Manual)
            {
                // Manual mode announcement
                parts.Add("Manual mode");
                if (ShapeHelper.IsOrderDesignator(designator) || ShapeHelper.IsCellsDesignator(designator) || ShapeHelper.IsZoneDesignator(designator))
                {
                    parts.Add(designatorLabel);
                }
                else
                {
                    parts.Add($"Placing {designatorLabel}");
                }
            }
            else
            {
                // Shape mode announcement with clear separation
                parts.Add("Shape mode");
                parts.Add($"{shapeName} selected");
                parts.Add(designatorLabel);
            }

            // Add zone expand/create info for zone designators
            if (ShapeHelper.IsZoneDesignator(designator) && !ShapeHelper.IsDeleteDesignator(designator))
            {
                IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
                string zoneModeInfo = ZoneSelectionHelper.GetZoneModeAnnouncement(designator, cursorPos);
                parts.Add(zoneModeInfo);
            }

            // Add size and rotation info for build designators
            if (designator is Designator_Place placeDesignator && placeDesignator.PlacingDef != null)
            {
                BuildableDef def = placeDesignator.PlacingDef;
                IntVec2 size = def.Size;

                // Size info
                if (size.x == 1 && size.z == 1)
                {
                    parts.Add("Size: 1 tile");
                }
                else
                {
                    parts.Add($"Size: {size.x}x{size.z}");
                }

                // Rotation info - only include for rotatable buildings
                // Non-rotatable buildings (like doors) auto-detect orientation when placed
                bool isRotatable = def is ThingDef thingDef && thingDef.rotatable;
                if (isRotatable)
                {
                    Rot4 rotation = Rot4.North;
                    var rotField = HarmonyLib.AccessTools.Field(typeof(Designator_Place), "placingRot");
                    if (rotField != null)
                    {
                        rotation = (Rot4)rotField.GetValue(placeDesignator);
                    }
                    // Use shared method that includes building-specific info (bed head, cooler direction, etc.)
                    string rotationInfo = ArchitectState.GetRotationAnnouncementForDef(def, rotation);
                    parts.Add(rotationInfo);
                }
            }

            // Key hints
            if (shape == ShapeType.Manual)
            {
                if (ShapeHelper.IsOrderDesignator(designator) || ShapeHelper.IsCellsDesignator(designator))
                {
                    parts.Add("Move to targets, Space to select, Enter to confirm, Tab for shapes, Escape to cancel");
                }
                else
                {
                    // Check if this building can actually be rotated
                    // Some buildings like doors auto-detect their orientation and cannot be manually rotated
                    bool canRotate = false;
                    if (designator is Designator_Build buildDes)
                    {
                        if (buildDes.PlacingDef is ThingDef thingDef)
                        {
                            canRotate = thingDef.rotatable;
                        }
                    }

                    if (canRotate)
                    {
                        parts.Add("Move to position and press Space to place, R to rotate, Escape to cancel");
                    }
                    else
                    {
                        parts.Add("Move to position and press Space to place, Escape to cancel");
                    }
                }
            }
            else
            {
                parts.Add("Move to first point and press Space");
            }

            return string.Join(". ", parts) + ".";
        }

        /// <summary>
        /// Sets the first point of the shape at the specified cell.
        /// </summary>
        /// <param name="cell">The cell position for the first point</param>
        public static void SetFirstPoint(IntVec3 cell)
        {
            if (currentPhase != PlacementPhase.SettingFirstCorner)
            {
                Log.Warning($"[ShapePlacementState] SetFirstPoint called in wrong phase: {currentPhase}");
                return;
            }

            previewHelper.SetFirstCorner(cell, "[ShapePlacementState]");
            currentPhase = PlacementPhase.SettingSecondCorner;
        }

        /// <summary>
        /// Sets the second point of the shape and transitions to previewing phase.
        /// </summary>
        /// <param name="cell">The cell position for the second point</param>
        public static void SetSecondPoint(IntVec3 cell)
        {
            if (currentPhase != PlacementPhase.SettingSecondCorner)
            {
                Log.Warning($"[ShapePlacementState] SetSecondPoint called in wrong phase: {currentPhase}");
                return;
            }

            if (!previewHelper.HasFirstCorner)
            {
                Log.Error("[ShapePlacementState] SetSecondPoint called without first point set");
                return;
            }

            previewHelper.SetSecondCorner(cell, "[ShapePlacementState]");
            currentPhase = PlacementPhase.Previewing;
        }

        /// <summary>
        /// Updates the shape preview as the cursor moves during SettingSecondCorner phase.
        /// Plays sound feedback when the cell count changes.
        /// </summary>
        /// <param name="cursor">The current cursor position</param>
        public static void UpdatePreview(IntVec3 cursor)
        {
            if (currentPhase != PlacementPhase.SettingSecondCorner)
                return;

            if (!previewHelper.HasFirstCorner)
                return;

            previewHelper.UpdatePreview(cursor);
        }

        /// <summary>
        /// Places designations for all cells in the current preview.
        /// Works for all designator types: Build (blueprints), Orders (Hunt, Haul), Zones, and Cells (Mine).
        /// </summary>
        /// <param name="silent">If true, does not announce the placement (caller will announce, e.g., viewing mode)</param>
        /// <returns>A PlacementResult containing statistics and placed items</returns>
        public static PlacementResult PlaceDesignations(bool silent = false)
        {
            PlacementResult result = new PlacementResult();

            // Validate pre-conditions
            Map map = ValidatePrePlacement(result);
            if (map == null)
                return result;

            // Track items placed this operation for undo
            List<Thing> placedThisOperation = new List<Thing>();

            // Get designator info
            bool isZoneDesignator = ShapeHelper.IsZoneDesignator(activeDesignator);

            // For zones, use DesignateMultiCell with all valid cells at once
            if (isZoneDesignator)
            {
                PlacementResult zoneResult = PlaceZoneDesignations(result, map);
                if (zoneResult != null)
                    return zoneResult; // Early return for zone deletion confirmation
            }
            // For all other designators (Build, Orders, Cells), use DesignateSingleCell per cell
            else
            {
                PlaceNonZoneDesignations(result, map, placedThisOperation);
            }

            FinalizeAndAnnounce(result, placedThisOperation, silent);

            return result;
        }

        /// <summary>
        /// Validates pre-conditions for placement.
        /// </summary>
        /// <param name="result">The PlacementResult to populate with error info</param>
        /// <returns>The current map if valid, null if validation failed</returns>
        private static Map ValidatePrePlacement(PlacementResult result)
        {
            if (activeDesignator == null)
            {
                TolkHelper.Speak("No designator active", SpeechPriority.High);
                return null;
            }

            if (previewHelper.PreviewCells.Count == 0)
            {
                TolkHelper.Speak("No cells selected", SpeechPriority.High);
                return null;
            }

            Map map = Find.CurrentMap;
            if (map == null)
            {
                TolkHelper.Speak("No map available", SpeechPriority.High);
                return null;
            }

            return map;
        }

        /// <summary>
        /// Places zone designations using DesignateMultiCell.
        /// </summary>
        /// <param name="result">The PlacementResult to populate</param>
        /// <param name="map">The current map</param>
        /// <returns>A PlacementResult if early return needed (zone deletion confirmation), null otherwise</returns>
        private static PlacementResult PlaceZoneDesignations(PlacementResult result, Map map)
        {
            bool isDeleteDesignator = ShapeHelper.IsDeleteDesignator(activeDesignator);

            // Filter to valid cells first
            List<IntVec3> validCells = new List<IntVec3>();
            foreach (IntVec3 cell in previewHelper.PreviewCells)
            {
                AcceptanceReport report = activeDesignator.CanDesignateCell(cell);
                if (report.Accepted)
                {
                    validCells.Add(cell);
                }
                else
                {
                    result.ObstacleCells.Add(cell);
                    result.ObstacleCount++;
                }
            }

            if (validCells.Count > 0)
            {
                try
                {
                    // Use cursor position from when shape mode was entered to determine expand vs create
                    // This ensures the behavior matches what was announced on entry
                    IntVec3 referenceCell = entryCursorPosition.IsValid ? entryCursorPosition : validCells[0];
                    ZoneSelectionResult selectionResult = ZoneSelectionHelper.SelectZoneAtCell(activeDesignator, referenceCell);
                    Zone targetZone = selectionResult.TargetZone;

                    // For expand operations (not delete, and we have a target zone), filter cells to
                    // only those that are adjacent to the existing zone or to already-valid cells
                    // This prevents creating disconnected zones when using the expand gizmo
                    if (!isDeleteDesignator && targetZone != null && selectionResult.IsExpansion)
                    {
                        validCells = FilterCellsForExpansion(validCells, targetZone, map);
                        if (validCells.Count == 0)
                        {
                            Log.Message("[ShapePlacementState] No cells adjacent to zone for expansion");
                            return null;
                        }
                    }

                    // For shrink operations, check if this would delete the entire zone
                    if (isDeleteDesignator && targetZone != null)
                    {
                        if (ZoneUndoTracker.WouldDeleteEntireZone(targetZone, validCells))
                        {
                            // Return early with pending confirmation flag
                            result.NeedsFullDeletionConfirmation = true;
                            result.ZonePendingDeletion = targetZone;
                            result.PendingValidCells.AddRange(validCells);
                            result.ObstacleCells.Clear(); // Clear obstacles since we're not placing yet
                            result.ObstacleCount = 0;
                            Log.Message($"[ShapePlacementState] Shrink would delete entire zone {targetZone.label}, needs confirmation");
                            return result;
                        }
                    }

                    // Capture zone state BEFORE modification for undo support
                    ZoneUndoTracker.CaptureBeforeState(targetZone, map, isDeleteDesignator);

                    activeDesignator.DesignateMultiCell(validCells);

                    // Capture zone state AFTER modification (detects splits)
                    ZoneUndoTracker.CaptureAfterState(map);

                    result.PlacedCells.AddRange(validCells);
                    result.PlacedCount = validCells.Count;
                }
                catch (System.Exception ex)
                {
                    Log.Error($"[ShapePlacementState] Error placing zone: {ex.Message}");
                }
            }

            return null; // Continue with normal flow
        }

        /// <summary>
        /// Filters cells to only include those that form a contiguous expansion of the target zone.
        /// Uses flood-fill starting from cells adjacent to the existing zone.
        /// </summary>
        private static List<IntVec3> FilterCellsForExpansion(List<IntVec3> candidateCells, Zone targetZone, Map map)
        {
            if (candidateCells.Count == 0 || targetZone == null)
                return candidateCells;

            HashSet<IntVec3> zoneCells = new HashSet<IntVec3>(targetZone.Cells);
            HashSet<IntVec3> candidateSet = new HashSet<IntVec3>(candidateCells);
            HashSet<IntVec3> validExpansionCells = new HashSet<IntVec3>();

            // Find all candidate cells that are directly adjacent to the existing zone
            Queue<IntVec3> queue = new Queue<IntVec3>();
            foreach (IntVec3 cell in candidateCells)
            {
                foreach (IntVec3 dir in GenAdj.CardinalDirections)
                {
                    IntVec3 neighbor = cell + dir;
                    if (zoneCells.Contains(neighbor))
                    {
                        // This candidate cell is adjacent to the zone
                        if (validExpansionCells.Add(cell))
                        {
                            queue.Enqueue(cell);
                        }
                        break;
                    }
                }
            }

            // Flood-fill to include candidate cells that are adjacent to valid expansion cells
            while (queue.Count > 0)
            {
                IntVec3 current = queue.Dequeue();
                foreach (IntVec3 dir in GenAdj.CardinalDirections)
                {
                    IntVec3 neighbor = current + dir;
                    if (candidateSet.Contains(neighbor) && validExpansionCells.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            // Return filtered list preserving original order
            List<IntVec3> result = new List<IntVec3>();
            foreach (IntVec3 cell in candidateCells)
            {
                if (validExpansionCells.Contains(cell))
                {
                    result.Add(cell);
                }
            }

            if (result.Count < candidateCells.Count)
            {
                Log.Message($"[ShapePlacementState] Filtered expansion from {candidateCells.Count} to {result.Count} cells (must be adjacent to zone)");
            }

            return result;
        }

        /// <summary>
        /// Places non-zone designations (Build, Orders, Cells) using DesignateSingleCell per cell.
        /// </summary>
        /// <param name="result">The PlacementResult to populate</param>
        /// <param name="map">The current map</param>
        /// <param name="placedThisOperation">List to track placed things for undo</param>
        private static void PlaceNonZoneDesignations(PlacementResult result, Map map, List<Thing> placedThisOperation)
        {
            bool isBuildDesignator = ShapeHelper.IsBuildDesignator(activeDesignator);
            bool isAreaDesignator = ShapeHelper.IsAreaDesignator(activeDesignator);
            bool isBuiltInAreaDesignator = ShapeHelper.IsBuiltInAreaDesignator(activeDesignator);
            bool isOrderDesignator = ShapeHelper.IsOrderDesignator(activeDesignator);

            // Capture area state before painting for undo support
            if (isAreaDesignator && Designator_AreaAllowed.selectedArea != null)
            {
                bool isExpanding = activeDesignator is Designator_AreaAllowedExpand;
                AreaUndoTracker.CaptureBeforeState(Designator_AreaAllowed.selectedArea, isExpanding);
            }
            else if (isBuiltInAreaDesignator)
            {
                // Built-in areas (Snow/Sand, Roof, Home) have fixed Area objects on the map
                Area builtInArea = ShapeHelper.GetBuiltInAreaForDesignator(activeDesignator, map);
                if (builtInArea != null)
                {
                    bool isExpanding = ShapeHelper.IsBuiltInAreaExpanding(activeDesignator);
                    AreaUndoTracker.CaptureBeforeState(builtInArea, isExpanding);
                }
            }

            // Get building info for cost calculation (only applies to Build designators)
            BuildableDef buildableDef = isBuildDesignator ? GetBuildableDefFromDesignator(activeDesignator) : null;
            int costPerCell = GetCostPerCell(buildableDef);
            string resourceName = GetResourceName(buildableDef);

            // Capture designation state before placement for order designators
            // This allows us to diff and find exactly which designations were created
            if (isOrderDesignator)
            {
                OrderUndoTracker.CaptureBeforeState(map);
            }

            // Place designation for each cell
            foreach (IntVec3 cell in previewHelper.PreviewCells)
            {
                AcceptanceReport report = activeDesignator.CanDesignateCell(cell);

                if (report.Accepted)
                {
                    try
                    {
                        // For Build designators, track the blueprint for undo
                        if (isBuildDesignator)
                        {
                            List<Thing> thingsBefore = new List<Thing>(cell.GetThingList(map));
                            activeDesignator.DesignateSingleCell(cell);
                            List<Thing> thingsAfter = cell.GetThingList(map);
                            bool blueprintAdded = false;
                            foreach (Thing thing in thingsAfter)
                            {
                                if (!thingsBefore.Contains(thing) &&
                                    (thing.def.IsBlueprint || thing.def.IsFrame))
                                {
                                    placedThisOperation.Add(thing);
                                    blueprintAdded = true;
                                    break;
                                }
                            }

                            if (blueprintAdded)
                            {
                                result.PlacedCells.Add(cell);
                                result.PlacedCount++;
                            }
                        }
                        else
                        {
                            // For non-build designators, always count
                            activeDesignator.DesignateSingleCell(cell);
                            result.PlacedCells.Add(cell);
                            result.PlacedCount++;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error($"[ShapePlacementState] Error placing at {cell}: {ex.Message}");
                        result.ObstacleCells.Add(cell);
                        result.ObstacleCount++;
                    }
                }
                else
                {
                    result.ObstacleCells.Add(cell);
                    result.ObstacleCount++;
                }
            }

            // Capture designation state after placement for order designators
            if (isOrderDesignator)
            {
                OrderUndoTracker.CaptureAfterState(map);
            }

            // Calculate total resource cost (only for Build designators)
            if (isBuildDesignator)
            {
                result.TotalResourceCost = result.PlacedCount * costPerCell;
                result.ResourceName = resourceName;
            }

            // Capture area state after painting
            if (isAreaDesignator || isBuiltInAreaDesignator)
            {
                AreaUndoTracker.CaptureAfterState();
            }
        }

        /// <summary>
        /// Finalizes the designator and announces results.
        /// </summary>
        /// <param name="result">The PlacementResult to finalize</param>
        /// <param name="placedThisOperation">List of placed things for undo tracking</param>
        /// <param name="silent">If true, does not announce the placement</param>
        private static void FinalizeAndAnnounce(PlacementResult result, List<Thing> placedThisOperation, bool silent)
        {
            // Finalize the designator if any placements succeeded
            if (result.PlacedCount > 0)
            {
                try
                {
                    activeDesignator.Finalize(true);
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[ShapePlacementState] Error finalizing designator: {ex.Message}");
                }
            }

            result.PlacedBlueprints = placedThisOperation;

            // Announce results unless silent (caller will announce, e.g., viewing mode)
            if (!silent)
            {
                // Use sanitized label to strip "..." suffix (prevents "wall...s" bug)
                string designatorName = ArchitectHelper.GetSanitizedLabel(activeDesignator);
                string announcement = BuildPlacementAnnouncement(result, designatorName, activeDesignator);
                TolkHelper.Speak(announcement);
            }

            Log.Message($"[ShapePlacementState] Placed {result.PlacedCount} designations, {result.ObstacleCount} obstacles");
        }

        /// <summary>
        /// Executes the zone deletion after user confirms via dialog.
        /// Called when PlacementResult.NeedsFullDeletionConfirmation was true and user clicked "Delete Zone".
        /// </summary>
        /// <param name="pendingResult">The result that contains the pending deletion info</param>
        /// <param name="silent">If true, does not announce the deletion (caller will announce)</param>
        /// <returns>Updated PlacementResult with actual deletion results</returns>
        public static PlacementResult ExecuteConfirmedZoneDeletion(PlacementResult pendingResult, bool silent = false)
        {
            if (pendingResult == null || !pendingResult.NeedsFullDeletionConfirmation)
            {
                Log.Warning("[ShapePlacementState] ExecuteConfirmedZoneDeletion called without pending confirmation");
                return pendingResult;
            }

            Zone targetZone = pendingResult.ZonePendingDeletion;
            List<IntVec3> validCells = pendingResult.PendingValidCells;
            Map map = Find.CurrentMap;

            if (targetZone == null || map == null)
            {
                Log.Error("[ShapePlacementState] ExecuteConfirmedZoneDeletion: missing zone or map");
                return pendingResult;
            }

            try
            {
                // Delete the zone directly (no undo tracking since this is irreversible)
                string zoneName = targetZone.label;
                targetZone.Delete();

                // Update the result
                pendingResult.PlacedCells.AddRange(validCells);
                pendingResult.PlacedCount = validCells.Count;
                pendingResult.NeedsFullDeletionConfirmation = false;

                if (!silent)
                {
                    TolkHelper.Speak($"Zone {zoneName} deleted.", SpeechPriority.Normal);
                }

                Log.Message($"[ShapePlacementState] Confirmed deletion of zone {zoneName}");
            }
            catch (System.Exception ex)
            {
                Log.Error($"[ShapePlacementState] Error executing zone deletion: {ex.Message}");
            }

            return pendingResult;
        }

        /// <summary>
        /// Cancels the current shape placement operation completely and exits shape mode.
        /// </summary>
        public static void Cancel()
        {
            PlacementPhase previousPhase = currentPhase;

            // Reset all state
            Reset();

            // Announce based on what phase we were in
            switch (previousPhase)
            {
                case PlacementPhase.SettingFirstCorner:
                    TolkHelper.Speak("Shape placement cancelled");
                    break;
                case PlacementPhase.SettingSecondCorner:
                    TolkHelper.Speak("Shape cancelled, back to first point");
                    break;
                case PlacementPhase.Previewing:
                    TolkHelper.Speak("Preview cancelled");
                    break;
            }

            Log.Message($"[ShapePlacementState] Cancelled from phase {previousPhase}");
        }

        /// <summary>
        /// Clears the current selection but stays in shape placement mode with the same shape.
        /// Use this for Escape key behavior when user wants to restart selection, not exit.
        /// </summary>
        /// <param name="silent">If true, does not announce anything (caller will announce)</param>
        /// <returns>True if selection was cleared and we should stay in shape mode, false if nothing to clear</returns>
        public static bool ClearSelectionAndStay(bool silent = false)
        {
            PlacementPhase previousPhase = currentPhase;

            // If we're in SettingFirstCorner with no corner set, there's nothing to clear
            if (previousPhase == PlacementPhase.SettingFirstCorner && !previewHelper.HasFirstCorner)
            {
                return false;
            }

            // Save the shape for logging
            ShapeType savedShape = currentShape;

            // Announce if not silent
            if (!silent)
            {
                if (previousPhase == PlacementPhase.Previewing)
                {
                    // In Previewing phase, tell user how to proceed
                    TolkHelper.Speak("Selection cleared. Press Escape again to exit, or Enter then Equals to add another section.");
                }
                else if (previousPhase == PlacementPhase.SettingSecondCorner)
                {
                    TolkHelper.Speak("Selection cancelled, back to first point");
                }
            }

            // Reset preview helper but keep the shape
            previewHelper.Reset();
            currentPhase = PlacementPhase.SettingFirstCorner;

            Log.Message($"[ShapePlacementState] Cleared selection from phase {previousPhase}, staying in {savedShape} mode");
            return true;
        }

        /// <summary>
        /// Removes the most recently set point, stepping back through the placement phases.
        /// Used by Shift+Space to undo points one at a time.
        /// </summary>
        /// <returns>True if a point was removed, false if no points to remove</returns>
        public static bool RemoveLastPoint()
        {
            // If in Previewing phase (both points set), remove second point
            if (currentPhase == PlacementPhase.Previewing && previewHelper.IsInPreviewMode)
            {
                // Clear second point by resetting preview and keeping first point position
                IntVec3 firstPointPos = previewHelper.FirstCorner.Value;
                previewHelper.Reset();
                // Use silent=true to avoid redundant "First point" announcement
                previewHelper.SetFirstCorner(firstPointPos, "[ShapePlacementState]", silent: true);
                currentPhase = PlacementPhase.SettingSecondCorner;
                TolkHelper.Speak("Second point removed");
                Log.Message("[ShapePlacementState] Removed second point, back to SettingSecondCorner phase");
                return true;
            }

            // If in SettingSecondCorner phase (only first point set), remove first point
            if (currentPhase == PlacementPhase.SettingSecondCorner && previewHelper.HasFirstCorner)
            {
                previewHelper.Reset();
                currentPhase = PlacementPhase.SettingFirstCorner;
                TolkHelper.Speak("First point removed");
                Log.Message("[ShapePlacementState] Removed first point, back to SettingFirstCorner phase");
                return true;
            }

            // No points to remove (in SettingFirstCorner phase with no first point, or unexpected state)
            TolkHelper.Speak("No points to remove");
            return false;
        }

        /// <summary>
        /// Resets all state variables to their initial values.
        /// Idempotent: safe to call multiple times, early exits if already inactive.
        /// </summary>
        public static void Reset()
        {
            // Early exit if already inactive - prevents redundant work and logging
            if (currentPhase == PlacementPhase.Inactive)
            {
                return;
            }

            // Set phase to Inactive FIRST before any other cleanup
            // This prevents infinite loops with DesignatorManagerDeselectPatch
            currentPhase = PlacementPhase.Inactive;
            currentShape = ShapeType.Manual;
            previewHelper.FullReset();
            activeDesignator = null;
            hasViewingModeOnStack = false;
            entryCursorPosition = IntVec3.Invalid;

            Log.Message("[ShapePlacementState] State reset");
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Syncs the mod's shape selection to the game's SelectedStyle.
        /// This ensures RimWorld's "Remember Draw Styles" setting works correctly.
        /// Setting SelectedStyle automatically updates the game's previouslySelected dictionary.
        /// </summary>
        private static void SyncShapeToGameStyle(Designator designator, ShapeType shape)
        {
            if (designator == null)
                return;

            var designatorManager = Find.DesignatorManager;
            if (designatorManager == null)
                return;

            // Get the DrawStyleDef for this shape (null for Manual mode)
            DrawStyleDef styleDef = ShapeHelper.GetDrawStyleDef(designator, shape);

            // Setting SelectedStyle automatically updates the previouslySelected dictionary
            if (styleDef != null)
            {
                designatorManager.SelectedStyle = styleDef;
            }
        }

        /// <summary>
        /// Gets the currently selected zone from Find.Selector.
        /// Used for zone expand/shrink operations to identify the target zone.
        /// </summary>
        /// <returns>The selected zone, or null if no zone is selected</returns>
        private static Zone GetSelectedZone()
        {
            var selectedObjects = Find.Selector?.SelectedObjects;
            if (selectedObjects == null)
                return null;

            foreach (object obj in selectedObjects)
            {
                if (obj is Zone zone)
                    return zone;
            }

            return null;
        }

        /// <summary>
        /// Gets the BuildableDef from a designator for cost calculation.
        /// </summary>
        private static BuildableDef GetBuildableDefFromDesignator(Designator designator)
        {
            if (designator is Designator_Build buildDesignator)
            {
                return buildDesignator.PlacingDef;
            }

            if (designator is Designator_Place placeDesignator)
            {
                return placeDesignator.PlacingDef;
            }

            return null;
        }

        /// <summary>
        /// Gets the resource cost per cell for a buildable.
        /// </summary>
        private static int GetCostPerCell(BuildableDef buildable)
        {
            if (buildable == null)
                return 0;

            // Check for stuff cost (most common for walls, floors, etc.)
            if (buildable is ThingDef thingDef && thingDef.MadeFromStuff)
            {
                return buildable.CostStuffCount;
            }

            // Check for fixed costs
            if (buildable.CostList != null && buildable.CostList.Count > 0)
            {
                // Return the count of the first (primary) cost
                return buildable.CostList[0].count;
            }

            return 0;
        }

        /// <summary>
        /// Gets the name of the primary resource for a buildable.
        /// </summary>
        private static string GetResourceName(BuildableDef buildable)
        {
            if (buildable == null)
                return string.Empty;

            // For stuff-based buildings, return generic "material" (actual material depends on selection)
            if (buildable is ThingDef thingDef && thingDef.MadeFromStuff)
            {
                // Try to get the currently selected stuff from ArchitectState
                if (ArchitectState.SelectedMaterial != null)
                {
                    return ArchitectState.SelectedMaterial.label;
                }
                return "material";
            }

            // For fixed cost buildings, return the primary resource name
            if (buildable.CostList != null && buildable.CostList.Count > 0)
            {
                return buildable.CostList[0].thingDef.label;
            }

            return string.Empty;
        }

        /// <summary>
        /// Builds the announcement string for placement results.
        /// </summary>
        private static string BuildPlacementAnnouncement(PlacementResult result, string designatorName, Designator designator)
        {
            List<string> parts = new List<string>();
            bool isBuild = ShapeHelper.IsBuildDesignator(designator);
            bool isOrder = ShapeHelper.IsOrderDesignator(designator);

            // Main placement info
            if (result.PlacedCount > 0)
            {
                if (isBuild)
                {
                    // Pluralize the designator name if multiple items placed
                    string name = result.PlacedCount > 1
                        ? Find.ActiveLanguageWorker.Pluralize(designatorName, result.PlacedCount)
                        : designatorName;

                    string costInfo = string.Empty;
                    if (result.TotalResourceCost > 0 && !string.IsNullOrEmpty(result.ResourceName))
                    {
                        costInfo = $" ({result.TotalResourceCost} {result.ResourceName})";
                    }
                    parts.Add($"Placed {result.PlacedCount} {name}{costInfo}");
                }
                else
                {
                    // For orders, use "Designated X for [action]" matching RimWorld's terminology
                    string action = GetActionFromDesignatorName(designatorName);
                    parts.Add($"Designated {result.PlacedCount} for {action}");
                }
            }
            else
            {
                if (isBuild)
                {
                    parts.Add("No blueprints placed");
                }
                else
                {
                    parts.Add("No designations placed");
                }
            }

            // Obstacle info - only for build designators and zone-add, not for orders or delete/shrink
            bool isDelete = ShapeHelper.IsDeleteDesignator(designator);
            if (!isOrder && !isDelete && result.ObstacleCount > 0)
            {
                parts.Add($"{result.ObstacleCount} obstacles found");
            }

            return string.Join(". ", parts);
        }

        /// <summary>
        /// Converts a designator name to a gerund action phrase for announcements.
        /// </summary>
        /// <param name="designatorName">The designator label (e.g., "Haul things", "Hunt", "Mine")</param>
        /// <returns>A gerund action phrase (e.g., "hauling", "hunting", "mining")</returns>
        private static string GetActionFromDesignatorName(string designatorName)
        {
            string lowerName = designatorName.ToLower();

            // Check each keyword in the map
            foreach (var kvp in DesignatorActionMap)
            {
                if (lowerName.Contains(kvp.Key))
                    return kvp.Value;
            }

            // For unknown designators, just use the name lowercase
            return lowerName;
        }

        /// <summary>
        /// Gets whether the current phase allows cursor movement to update preview.
        /// </summary>
        public static bool ShouldUpdatePreviewOnMove()
        {
            return currentPhase == PlacementPhase.SettingSecondCorner && previewHelper.HasFirstCorner;
        }

        /// <summary>
        /// Gets the dimensions of the current shape preview.
        /// </summary>
        /// <returns>Tuple of (width, height) or (0, 0) if no preview</returns>
        public static (int width, int height) GetCurrentDimensions()
        {
            if (!previewHelper.HasFirstCorner || !MapNavigationState.IsInitialized)
                return (0, 0);

            IntVec3 target = previewHelper.SecondCorner ?? MapNavigationState.CurrentCursorPosition;
            return ShapeHelper.GetDimensions(previewHelper.FirstCorner.Value, target);
        }

        #endregion
    }
}
