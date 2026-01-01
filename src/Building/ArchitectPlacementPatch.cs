using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch to handle input during architect placement mode.
    /// Handles Space (select/place cell), Shift+Space (cancel blueprint),
    /// Enter (confirm), and Escape (cancel).
    /// Also modifies arrow key announcements to include selected cell status.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class ArchitectPlacementInputPatch
    {
        private static float lastSpaceTime = 0f;
        private const float SpaceCooldown = 0.2f;

        /// <summary>
        /// Prefix patch to handle architect placement input at GUI event level.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Normal)]
        public static void Prefix()
        {
            // Only active during gameplay (not in main menu)
            if (Current.ProgramState != ProgramState.Playing)
                return;

            // Check if we're in placement mode (either via ArchitectState or directly via DesignatorManager)
            bool inArchitectMode = ArchitectState.IsInPlacementMode;
            bool hasActiveDesignator = Find.DesignatorManager != null &&
                                      Find.DesignatorManager.SelectedDesignator != null;

            // Only active when in architect placement mode OR when a designator is selected (e.g., from gizmos)
            if (!inArchitectMode && !hasActiveDesignator)
                return;

            // Only process keyboard events
            if (Event.current.type != EventType.KeyDown)
                return;

            // Check we have a valid map
            if (Find.CurrentMap == null)
            {
                if (inArchitectMode)
                    ArchitectState.Cancel();
                else if (hasActiveDesignator)
                    Find.DesignatorManager.Deselect();
                return;
            }

            KeyCode key = Event.current.keyCode;
            bool handled = false;
            bool shiftHeld = Event.current.shift;

            // Get the active designator (from either source)
            Designator activeDesignator = inArchitectMode ?
                ArchitectState.SelectedDesignator :
                Find.DesignatorManager.SelectedDesignator;

            if (activeDesignator == null)
                return;

            // Check if this is a zone designator
            bool isZoneDesignator = IsZoneDesignator(activeDesignator);

            // Shift+Space - Cancel blueprint at cursor position (check before regular Space)
            if (shiftHeld && key == KeyCode.Space)
            {
                IntVec3 currentPosition = MapNavigationState.CurrentCursorPosition;
                CancelBlueprintAtPosition(currentPosition);
                handled = true;
            }
            // R key - rotate building
            else if (key == KeyCode.R)
            {
                if (inArchitectMode)
                {
                    ArchitectState.RotateBuilding();
                }
                else if (activeDesignator is Designator_Place designatorPlace)
                {
                    // Use reflection to access private placingRot field
                    var rotField = AccessTools.Field(typeof(Designator_Place), "placingRot");
                    if (rotField != null)
                    {
                        Rot4 currentRot = (Rot4)rotField.GetValue(designatorPlace);
                        currentRot.Rotate(RotationDirection.Clockwise);
                        rotField.SetValue(designatorPlace, currentRot);
                        TolkHelper.Speak($"Rotated to {currentRot}");
                    }
                }
                handled = true;
            }
            // Shift+Arrow keys - auto-select to wall (only for zone designators in Manual mode)
            else if (shiftHeld && isZoneDesignator && ArchitectState.ZoneCreationMode == ZoneCreationMode.Manual)
            {
                IntVec3 currentPosition = MapNavigationState.CurrentCursorPosition;
                Map map = Find.CurrentMap;
                Rot4 direction = Rot4.Invalid;

                if (key == KeyCode.UpArrow)
                    direction = Rot4.North;
                else if (key == KeyCode.DownArrow)
                    direction = Rot4.South;
                else if (key == KeyCode.LeftArrow)
                    direction = Rot4.West;
                else if (key == KeyCode.RightArrow)
                    direction = Rot4.East;

                if (direction != Rot4.Invalid)
                {
                    AutoSelectToWall(currentPosition, direction, map, activeDesignator);
                    handled = true;
                }
            }
            // Space key - toggle selection of current cell
            else if (key == KeyCode.Space)
            {
                // Cooldown to prevent rapid toggling
                if (Time.time - lastSpaceTime < SpaceCooldown)
                    return;

                lastSpaceTime = Time.time;

                IntVec3 currentPosition = MapNavigationState.CurrentCursorPosition;

                // For zone designators, use multi-cell selection with mode support
                if (isZoneDesignator)
                {
                    if (inArchitectMode)
                    {
                        ArchitectState.ToggleCell(currentPosition);
                    }
                }
                // For build/place designators (including Designator_Install), place immediately
                else if (activeDesignator is Designator_Place)
                {
                    // Single placement - check if valid and place immediately
                    AcceptanceReport report = activeDesignator.CanDesignateCell(currentPosition);

                    if (report.Accepted)
                    {
                        try
                        {
                            activeDesignator.DesignateSingleCell(currentPosition);
                            activeDesignator.Finalize(true);

                            string label = activeDesignator.Label;
                            TolkHelper.Speak($"{label} placed at {currentPosition.x}, {currentPosition.z}");

                            // If in ArchitectState mode, clear selected cells for next placement
                            if (inArchitectMode)
                            {
                                ArchitectState.SelectedCells.Clear();
                            }
                            else
                            {
                                // For gizmo-activated placement (like Reinstall), exit after placement
                                Find.DesignatorManager.Deselect();
                            }
                        }
                        catch (System.Exception ex)
                        {
                            TolkHelper.Speak($"Error placing: {ex.Message}", SpeechPriority.High);
                            Log.Error($"Error in single cell designation: {ex}");
                        }
                    }
                    else
                    {
                        string reason = report.Reason ?? "Cannot place here";
                        TolkHelper.Speak($"Invalid: {reason}");
                    }
                }
                else
                {
                    // Multi-cell selection (for mining, plant cutting, etc.)
                    if (inArchitectMode)
                    {
                        ArchitectState.ToggleCell(currentPosition);
                    }
                }

                handled = true;
            }
            // Enter key - confirm and execute designation (for multi-cell designators)
            else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                // For zone designators, handle according to mode
                if (isZoneDesignator && inArchitectMode)
                {
                    ZoneCreationMode mode = ArchitectState.ZoneCreationMode;
                    Map map = Find.CurrentMap;

                    switch (mode)
                    {
                        case ZoneCreationMode.Manual:
                            // Manual mode: execute placement immediately
                            ExecuteZonePlacement(activeDesignator, map);
                            break;

                        case ZoneCreationMode.Borders:
                            // Borders mode: auto-fill interior
                            BordersModeAutoFill(activeDesignator, map);
                            break;

                        case ZoneCreationMode.Corners:
                            // Corners mode: fill rectangle
                            CornersModeAutoFill(activeDesignator, map);
                            break;
                    }

                    handled = true;
                }
                // For place designators (build, reinstall), Enter exits placement mode
                else if (activeDesignator is Designator_Place)
                {
                    TolkHelper.Speak("Placement completed");
                    if (inArchitectMode)
                        ArchitectState.Reset();
                    else
                        Find.DesignatorManager.Deselect();
                    handled = true;
                }
                else if (inArchitectMode)
                {
                    // For multi-cell designators in architect mode, execute the placement
                    ArchitectState.ExecutePlacement(Find.CurrentMap);
                    handled = true;
                }
            }
            // Escape key - cancel placement
            else if (key == KeyCode.Escape)
            {
                TolkHelper.Speak("Placement cancelled");
                if (inArchitectMode)
                    ArchitectState.Cancel();
                else
                    Find.DesignatorManager.Deselect();
                handled = true;
            }

            if (handled)
            {
                Event.current.Use();
            }
        }

        /// <summary>
        /// Cancels any blueprint or frame at the specified position.
        /// </summary>
        private static void CancelBlueprintAtPosition(IntVec3 position)
        {
            Map map = Find.CurrentMap;
            if (map == null)
                return;

            // Get all things at this position
            List<Thing> thingList = position.GetThingList(map);

            // Look for blueprints or frames
            bool foundAndCanceled = false;
            for (int i = thingList.Count - 1; i >= 0; i--)
            {
                Thing thing = thingList[i];

                // Check if it's a player-owned blueprint or frame
                if (thing.Faction == Faction.OfPlayer && (thing is Frame || thing is Blueprint))
                {
                    string thingLabel = thing.Label;
                    thing.Destroy(DestroyMode.Cancel);
                    TolkHelper.Speak($"Cancelled {thingLabel}");
                    SoundDefOf.Designate_Cancel.PlayOneShotOnCamera();
                    foundAndCanceled = true;
                    break; // Only cancel one blueprint per keypress
                }
            }

            if (!foundAndCanceled)
            {
                TolkHelper.Speak("No blueprint to cancel here");
            }
        }

        /// <summary>
        /// Checks if a designator is a zone/area/cell-based designator.
        /// This includes zones (stockpiles, growing zones), areas (home, roof), and other multi-cell designators.
        /// </summary>
        private static bool IsZoneDesignator(Designator designator)
        {
            if (designator == null)
                return false;

            // Check if this designator's type hierarchy includes "Designator_Cells"
            // This covers all multi-cell designators: zones, areas, roofs, etc.
            System.Type type = designator.GetType();
            while (type != null)
            {
                if (type.Name == "Designator_Cells")
                    return true;
                type = type.BaseType;
            }

            return false;
        }

        /// <summary>
        /// Auto-selects cells in a direction until hitting a wall or impassable terrain.
        /// </summary>
        private static void AutoSelectToWall(IntVec3 startPosition, Rot4 direction, Map map, Designator designator)
        {
            try
            {
                List<IntVec3> lineCells = new List<IntVec3>();
                IntVec3 currentCell = startPosition + direction.FacingCell;

                // Move in the direction until we hit a wall or go out of bounds
                while (currentCell.InBounds(map) && designator.CanDesignateCell(currentCell).Accepted)
                {
                    lineCells.Add(currentCell);
                    currentCell += direction.FacingCell;
                }

                // Add all cells to selection
                int addedCount = 0;
                foreach (IntVec3 cell in lineCells)
                {
                    if (!ArchitectState.SelectedCells.Contains(cell))
                    {
                        ArchitectState.SelectedCells.Add(cell);
                        addedCount++;
                    }
                }

                string directionName = direction.ToStringHuman();
                TolkHelper.Speak($"Selected {addedCount} cells to {directionName}. Total: {ArchitectState.SelectedCells.Count}");
                Log.Message($"Auto-select to wall: {addedCount} cells in direction {directionName}");
            }
            catch (System.Exception ex)
            {
                TolkHelper.Speak($"Error auto-selecting: {ex.Message}", SpeechPriority.High);
                Log.Error($"AutoSelectToWall error: {ex}");
            }
        }

        /// <summary>
        /// Executes zone placement in Manual mode.
        /// </summary>
        private static void ExecuteZonePlacement(Designator designator, Map map)
        {
            if (ArchitectState.SelectedCells.Count == 0)
            {
                TolkHelper.Speak("No cells selected");
                ArchitectState.Reset();
                return;
            }

            try
            {
                // Use the designator's DesignateMultiCell method
                designator.DesignateMultiCell(ArchitectState.SelectedCells);

                string label = designator.Label ?? "Zone";
                TolkHelper.Speak($"{label} created with {ArchitectState.SelectedCells.Count} cells");
                Log.Message($"Zone placement executed: {label} with {ArchitectState.SelectedCells.Count} cells");
            }
            catch (System.Exception ex)
            {
                TolkHelper.Speak($"Error creating zone: {ex.Message}", SpeechPriority.High);
                Log.Error($"ExecuteZonePlacement error: {ex}");
            }
            finally
            {
                ArchitectState.Reset();
            }
        }

        /// <summary>
        /// Auto-fills the interior of a zone from border cells using flood fill (Borders mode).
        /// </summary>
        private static void BordersModeAutoFill(Designator designator, Map map)
        {
            if (ArchitectState.SelectedCells.Count == 0)
            {
                TolkHelper.Speak("No border cells selected. Select border tiles first", SpeechPriority.High);
                return;
            }

            try
            {
                // Find the center point of the selected border cells
                int sumX = 0, sumZ = 0;
                foreach (IntVec3 cell in ArchitectState.SelectedCells)
                {
                    sumX += cell.x;
                    sumZ += cell.z;
                }
                IntVec3 centerPoint = new IntVec3(sumX / ArchitectState.SelectedCells.Count, 0, sumZ / ArchitectState.SelectedCells.Count);

                // Ensure center point is valid and not in the border
                if (!centerPoint.InBounds(map))
                {
                    TolkHelper.Speak("Invalid border selection. Cannot find interior point", SpeechPriority.High);
                    return;
                }

                // If center is in the border, try to find a nearby non-border cell
                if (ArchitectState.SelectedCells.Contains(centerPoint))
                {
                    // Try adjacent cells
                    bool foundStart = false;
                    foreach (IntVec3 adjacent in GenAdj.CardinalDirections)
                    {
                        IntVec3 testCell = centerPoint + adjacent;
                        if (testCell.InBounds(map) && !ArchitectState.SelectedCells.Contains(testCell) && designator.CanDesignateCell(testCell).Accepted)
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
                HashSet<IntVec3> borderSet = new HashSet<IntVec3>(ArchitectState.SelectedCells);

                map.floodFiller.FloodFill(centerPoint, (IntVec3 c) =>
                {
                    // Can traverse if: in bounds, not a border, and designator can place here
                    return c.InBounds(map) && !borderSet.Contains(c) && designator.CanDesignateCell(c).Accepted;
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
                    if (!ArchitectState.SelectedCells.Contains(cell))
                    {
                        ArchitectState.SelectedCells.Add(cell);
                        addedCount++;
                    }
                }

                TolkHelper.Speak($"Filled interior with {addedCount} cells. Total: {ArchitectState.SelectedCells.Count} cells. Creating zone");
                Log.Message($"Borders mode auto-fill: added {addedCount} interior cells, total {ArchitectState.SelectedCells.Count}");

                // Now execute the placement
                ExecuteZonePlacement(designator, map);
            }
            catch (System.Exception ex)
            {
                TolkHelper.Speak($"Error filling interior: {ex.Message}", SpeechPriority.High);
                Log.Error($"BordersModeAutoFill error: {ex}");
            }
        }

        /// <summary>
        /// Auto-fills a rectangular zone from 4 corner cells (Corners mode).
        /// </summary>
        private static void CornersModeAutoFill(Designator designator, Map map)
        {
            if (ArchitectState.SelectedCells.Count != 4)
            {
                TolkHelper.Speak($"Must select exactly 4 corners. Currently selected: {ArchitectState.SelectedCells.Count}", SpeechPriority.High);
                return;
            }

            try
            {
                // Find min and max X and Z coordinates
                int minX = int.MaxValue, maxX = int.MinValue;
                int minZ = int.MaxValue, maxZ = int.MinValue;

                foreach (IntVec3 corner in ArchitectState.SelectedCells)
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
                        if (cell.InBounds(map) && !ArchitectState.SelectedCells.Contains(cell))
                        {
                            rectangleCells.Add(cell);
                        }
                    }
                }

                // Add all rectangle cells to selection
                ArchitectState.SelectedCells.AddRange(rectangleCells);

                int width = maxX - minX + 1;
                int height = maxZ - minZ + 1;
                TolkHelper.Speak($"Filled {width} by {height} rectangle. Total: {ArchitectState.SelectedCells.Count} cells. Creating zone");
                Log.Message($"Corners mode auto-fill: {width}x{height} rectangle, total {ArchitectState.SelectedCells.Count} cells");

                // Now execute the placement
                ExecuteZonePlacement(designator, map);
            }
            catch (System.Exception ex)
            {
                TolkHelper.Speak($"Error filling rectangle: {ex.Message}", SpeechPriority.High);
                Log.Error($"CornersModeAutoFill error: {ex}");
            }
        }
    }

    /// <summary>
    /// Harmony patch to modify map navigation announcements during architect placement.
    /// Adds information about whether a cell can be designated.
    /// </summary>
    [HarmonyPatch(typeof(CameraDriver))]
    [HarmonyPatch("Update")]
    public static class ArchitectPlacementAnnouncementPatch
    {
        /// <summary>
        /// Postfix patch to modify tile announcements during architect placement.
        /// Adds "Selected" prefix for multi-cell designators, or validity info for build designators.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(CameraDriver __instance)
        {
            // Only active when in architect placement mode
            if (!ArchitectState.IsInPlacementMode)
                return;

            // Check if an arrow key was just pressed
            if (Find.CurrentMap == null || !MapNavigationState.IsInitialized)
                return;

            // Check if any arrow key was pressed this frame
            bool arrowKeyPressed = Input.GetKeyDown(KeyCode.UpArrow) ||
                                   Input.GetKeyDown(KeyCode.DownArrow) ||
                                   Input.GetKeyDown(KeyCode.LeftArrow) ||
                                   Input.GetKeyDown(KeyCode.RightArrow);

            if (arrowKeyPressed)
            {
                IntVec3 currentPosition = MapNavigationState.CurrentCursorPosition;
                Designator designator = ArchitectState.SelectedDesignator;

                if (designator == null)
                    return;

                // Get the last announced info
                string lastInfo = MapNavigationState.LastAnnouncedInfo;

                // For multi-cell designators, show if cell is already selected
                if (!(designator is Designator_Build))
                {
                    if (ArchitectState.SelectedCells.Contains(currentPosition))
                    {
                        if (!lastInfo.StartsWith("Selected"))
                        {
                            string modifiedInfo = "Selected, " + lastInfo;
                            TolkHelper.Speak(modifiedInfo);
                            MapNavigationState.LastAnnouncedInfo = modifiedInfo;
                        }
                    }
                }
                else
                {
                    // For build designators, announce if placement is valid
                    AcceptanceReport report = designator.CanDesignateCell(currentPosition);

                    if (!report.Accepted && !string.IsNullOrEmpty(report.Reason))
                    {
                        // Append the reason why we can't place here
                        string modifiedInfo = lastInfo + ", " + report.Reason;
                        TolkHelper.Speak(modifiedInfo);
                        MapNavigationState.LastAnnouncedInfo = modifiedInfo;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Harmony patch to intercept pause key (Space) during architect placement mode.
    /// Prevents Space from pausing the game when in placement mode.
    /// </summary>
    [HarmonyPatch(typeof(TimeControls))]
    [HarmonyPatch("DoTimeControlsGUI")]
    public static class ArchitectPlacementTimeControlsPatch
    {
        /// <summary>
        /// Prefix patch that intercepts the pause key event during architect placement.
        /// Returns false to skip TimeControls processing when Space is pressed in placement mode.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix()
        {
            // Only intercept when in architect placement mode
            if (!ArchitectState.IsInPlacementMode)
                return true; // Continue with normal processing

            // Check if this is a KeyDown event for the pause toggle key
            if (Event.current.type == EventType.KeyDown &&
                KeyBindingDefOf.TogglePause.KeyDownEvent)
            {
                // Consume the event so TimeControls doesn't process it
                Event.current.Use();

                // Log for debugging
                Log.Message("Space key intercepted during architect placement mode");

                // Don't let TimeControls process this event
                return false;
            }

            // Allow normal processing for other events
            return true;
        }
    }

    /// <summary>
    /// Harmony patch to render visual feedback during architect placement.
    /// Shows selected cells and current designation area.
    /// </summary>
    [HarmonyPatch(typeof(SelectionDrawer))]
    [HarmonyPatch("DrawSelectionOverlays")]
    public static class ArchitectPlacementVisualizationPatch
    {
        /// <summary>
        /// Postfix to draw visual indicators for selected cells during architect placement.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix()
        {
            // Only active when in architect placement mode
            if (!ArchitectState.IsInPlacementMode)
                return;

            Map map = Find.CurrentMap;
            if (map == null)
                return;

            // Draw highlights for selected cells (for multi-cell designators)
            foreach (IntVec3 cell in ArchitectState.SelectedCells)
            {
                if (cell.InBounds(map))
                {
                    // Draw a subtle highlight over selected cells
                    Graphics.DrawMesh(
                        MeshPool.plane10,
                        cell.ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays),
                        Quaternion.identity,
                        GenDraw.InteractionCellMaterial,
                        0
                    );
                }
            }

            // Draw highlight for current cursor position
            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
            if (cursorPos.InBounds(map))
            {
                // Use a different color for the current cursor
                Graphics.DrawMesh(
                    MeshPool.plane10,
                    cursorPos.ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays),
                    Quaternion.identity,
                    GenDraw.InteractionCellMaterial,
                    0
                );
            }
        }
    }
}
