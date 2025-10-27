using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch to handle input during architect placement mode.
    /// Handles Space (select cell), Enter (confirm), and Escape (cancel).
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
            // Only active when in architect placement mode
            if (!ArchitectState.IsInPlacementMode)
                return;

            // Only process keyboard events
            if (Event.current.type != EventType.KeyDown)
                return;

            // Check we have a valid map
            if (Find.CurrentMap == null)
            {
                ArchitectState.Cancel();
                return;
            }

            KeyCode key = Event.current.keyCode;
            bool handled = false;

            // R key - rotate building
            if (key == KeyCode.R)
            {
                ArchitectState.RotateBuilding();
                handled = true;
            }
            // Space key - toggle selection of current cell
            else if (key == KeyCode.Space)
            {
                // Cooldown to prevent rapid toggling
                if (Time.time - lastSpaceTime < SpaceCooldown)
                    return;

                lastSpaceTime = Time.time;

                IntVec3 currentPosition = MapNavigationState.CurrentCursorPosition;

                // For build designators, we typically place immediately rather than selecting multiple cells
                if (ArchitectState.SelectedDesignator is Designator_Build)
                {
                    // Single placement - check if valid and place immediately
                    AcceptanceReport report = ArchitectState.SelectedDesignator.CanDesignateCell(currentPosition);

                    if (report.Accepted)
                    {
                        try
                        {
                            ArchitectState.SelectedDesignator.DesignateSingleCell(currentPosition);
                            ArchitectState.SelectedDesignator.Finalize(true);

                            string label = ArchitectState.SelectedDesignator.Label;
                            ClipboardHelper.CopyToClipboard($"{label} placed at {currentPosition.x}, {currentPosition.z}");

                            // Stay in placement mode for buildings - user can place more or press Escape
                            // Clear selected cells for next placement
                            ArchitectState.SelectedCells.Clear();
                        }
                        catch (System.Exception ex)
                        {
                            ClipboardHelper.CopyToClipboard($"Error placing: {ex.Message}");
                            MelonLoader.MelonLogger.Error($"Error in single cell designation: {ex}");
                        }
                    }
                    else
                    {
                        string reason = report.Reason ?? "Cannot place here";
                        ClipboardHelper.CopyToClipboard($"Invalid: {reason}");
                    }
                }
                else
                {
                    // Multi-cell selection (for mining, plant cutting, etc.)
                    ArchitectState.ToggleCell(currentPosition);
                }

                handled = true;
            }
            // Enter key - confirm and execute designation (for multi-cell designators)
            else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                // For build designators, Enter exits placement mode
                if (ArchitectState.SelectedDesignator is Designator_Build)
                {
                    ClipboardHelper.CopyToClipboard("Placement completed");
                    ArchitectState.Reset();
                }
                else
                {
                    // For multi-cell designators, execute the placement
                    ArchitectState.ExecutePlacement(Find.CurrentMap);
                }

                handled = true;
            }
            // Escape key - cancel placement
            else if (key == KeyCode.Escape)
            {
                ArchitectState.Cancel();
                handled = true;
            }

            if (handled)
            {
                Event.current.Use();
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
                            ClipboardHelper.CopyToClipboard(modifiedInfo);
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
                        ClipboardHelper.CopyToClipboard(modifiedInfo);
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
                MelonLoader.MelonLogger.Msg("Space key intercepted during architect placement mode");

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
