using System.Collections.Generic;
using System.Linq;
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

            // Check if local map targeting is active for transport pod landing specifically
            // We need to differentiate between transport pod landing and weapon/ability targeting
            bool inTransportPodTargeting = false;
            if (Find.Targeter != null && Find.Targeter.IsTargeting)
            {
                // Check if the mouseAttachment matches the transport pod cursor
                // This ensures we only handle transport pod landing, not weapon targeting
                var mouseAttachmentField = AccessTools.Field(typeof(Targeter), "mouseAttachment");
                if (mouseAttachmentField != null)
                {
                    Texture2D mouseAttachment = mouseAttachmentField.GetValue(Find.Targeter) as Texture2D;
                    if (mouseAttachment != null &&
                        CompLaunchable.TargeterMouseAttachment != null &&
                        mouseAttachment == CompLaunchable.TargeterMouseAttachment)
                    {
                        inTransportPodTargeting = true;
                    }
                }
            }

            // Only active when in architect placement mode OR when a designator is selected (e.g., from gizmos)
            // OR when transport pod landing targeting is active
            if (!inArchitectMode && !hasActiveDesignator && !inTransportPodTargeting)
                return;

            // Only process keyboard events
            if (Event.current.type != EventType.KeyDown)
                return;

            // Don't process when shape selection menu is active
            // Don't process when viewing mode is active UNLESS ShapePlacementState is also active
            // (user is placing a door from viewing mode gizmo - ViewingModeState yields Space to us)
            if (ShapeSelectionMenuState.IsActive)
                return;

            // Don't process when area selection menu is active
            if (AreaSelectionMenuState.IsActive)
                return;

            // Don't let placement mode steal keys from overlay screens
            if (WindowlessInventoryState.IsActive
                || GizmoNavigationState.IsActive
                || WindowlessInspectionState.IsActive
                || WindowlessFloatMenuState.IsActive)
                return;

            if (ViewingModeState.IsActive && !ShapePlacementState.IsActive)
                return;

            // Don't let placement mode steal Enter from gizmo navigation
            // Gizmo navigation needs Enter key to execute selected gizmo
            if (GizmoNavigationState.IsActive)
            {
                if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                {
                    // Don't consume - let UnifiedKeyboardPatch route to GizmoNavigationState
                    return;
                }
                // For other keys, let placement mode handle them normally
            }

            // Don't let placement mode steal keys from active scanner search
            // Scanner search needs Enter, Escape, Backspace, and letter keys
            if (ScannerSearchState.IsActive)
            {
                KeyCode k = Event.current.keyCode;
                if (k == KeyCode.Return || k == KeyCode.KeypadEnter || k == KeyCode.Escape ||
                    k == KeyCode.Backspace || (k >= KeyCode.A && k <= KeyCode.Z) ||
                    (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9))
                {
                    // Don't consume - let UnifiedKeyboardPatch route to search
                    return;
                }
            }

            // Don't let placement mode steal keys from active Go To coordinate input
            // Go To needs Enter, Escape, Backspace, numbers, +/-, comma, space
            if (GoToState.IsActive)
            {
                KeyCode k = Event.current.keyCode;
                if (k == KeyCode.Return || k == KeyCode.KeypadEnter || k == KeyCode.Escape ||
                    k == KeyCode.Backspace || k == KeyCode.Space || k == KeyCode.Comma ||
                    k == KeyCode.Equals || k == KeyCode.Minus ||
                    k == KeyCode.KeypadPlus || k == KeyCode.KeypadMinus ||
                    (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) ||
                    (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9))
                {
                    // Don't consume - let UnifiedKeyboardPatch route to goto
                    return;
                }
            }

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

            // Handle local map targeting mode (transport pod landing) first
            if (inTransportPodTargeting)
            {
                handled = HandleTargetingModeInput(key, shiftHeld);
                if (handled)
                {
                    Event.current.Use();
                }
                return;
            }

            // Get the active designator (from either source)
            Designator activeDesignator = inArchitectMode ?
                ArchitectState.SelectedDesignator :
                Find.DesignatorManager.SelectedDesignator;

            if (activeDesignator == null)
                return;

            // Check designator type - use ShapeHelper methods for consistent classification
            bool isZoneDesignator = ShapeHelper.IsZoneDesignator(activeDesignator);
            bool isBuildDesignator = ShapeHelper.IsBuildDesignator(activeDesignator);
            bool isPlaceDesignator = ShapeHelper.IsPlaceDesignator(activeDesignator);
            bool isCellsDesignator = ShapeHelper.IsCellsDesignator(activeDesignator);
            bool isOrderDesignator = ShapeHelper.IsOrderDesignator(activeDesignator);

            // Get available shapes for ANY designator that has DrawStyleCategory
            // This includes buildings, orders (Hunt, Haul), zones, and other multi-cell designators
            var availableShapes = ShapeHelper.GetAvailableShapes(activeDesignator);
            bool supportsShapes = availableShapes.Count >= 1;

            // Tab key - open shape selection menu or switch to manual mode
            if (key == KeyCode.Tab)
            {
                handled = HandleTabKey(activeDesignator, shiftHeld, supportsShapes, inArchitectMode, availableShapes);
            }
            // Shift+Space - Remove shape points OR cancel blueprint at cursor position
            else if (shiftHeld && key == KeyCode.Space)
            {
                handled = HandleShiftSpaceKey();
            }
            // Shift+R - rotate building counter-clockwise
            else if (shiftHeld && key == KeyCode.R)
            {
                handled = HandleRotateKey(activeDesignator, inArchitectMode, RotationDirection.Counterclockwise);
            }
            // R key - rotate building clockwise
            else if (key == KeyCode.R)
            {
                handled = HandleRotateKey(activeDesignator, inArchitectMode, RotationDirection.Clockwise);
            }
            // Space key - unified handling for all designator types
            else if (key == KeyCode.Space)
            {
                // Cooldown to prevent rapid toggling
                if (Time.time - lastSpaceTime < SpaceCooldown)
                {
                    Event.current.Use();
                    return;
                }

                lastSpaceTime = Time.time;
                IntVec3 currentPosition = MapNavigationState.CurrentCursorPosition;
                handled = HandleSpaceKey(activeDesignator, currentPosition, inArchitectMode, isPlaceDesignator, isZoneDesignator, isOrderDesignator, isCellsDesignator);
            }
            // Enter key - confirm and execute designation
            else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                handled = HandleEnterKey(activeDesignator, inArchitectMode, isPlaceDesignator, isZoneDesignator, isOrderDesignator, isCellsDesignator);
            }
            // Escape key - cancel shape placement, rectangle, or cancel placement
            else if (key == KeyCode.Escape)
            {
                handled = HandleEscapeKey(inArchitectMode);
            }

            if (handled)
            {
                Event.current.Use();
            }
        }

        /// <summary>
        /// Handles Tab key input for shape selection.
        /// Tab opens shape selection menu, Shift+Tab switches to manual mode.
        /// Works for designators selected via architect menu OR via gizmos (e.g., zone expand/shrink).
        /// </summary>
        /// <returns>True if the key was handled, false otherwise.</returns>
        private static bool HandleTabKey(Designator activeDesignator, bool shiftHeld, bool supportsShapes, bool inArchitectMode, List<ShapeType> availableShapes)
        {
            // Allow shape selection when we have an active designator that supports shapes,
            // whether it was selected via architect menu (inArchitectMode) or via gizmo (activeDesignator != null)
            bool canUseShapes = supportsShapes && activeDesignator != null;

            // Tab key - open shape selection menu (for any designator that supports shapes)
            if (!shiftHeld)
            {
                if (canUseShapes)
                {
                    // Block Tab if placement is in progress with points set
                    // User must press Escape to clear selection first
                    if (ShapePlacementState.IsPlacementInProgress)
                    {
                        TolkHelper.Speak("Cannot change shape while placing. Press Escape to clear selection first.");
                        return true;
                    }

                    // Check if only Manual shape is available - nothing to cycle through
                    if (availableShapes.Count == 1 && availableShapes[0] == ShapeType.Manual)
                    {
                        TolkHelper.Speak("No shapes available.");
                        return true;
                    }

                    // Cancel any active shape placement before opening menu
                    if (ShapePlacementState.IsActive)
                    {
                        ShapePlacementState.Reset();
                    }

                    if (availableShapes.Count > 1)
                    {
                        // Multiple shapes available - open shape selection menu
                        ShapeSelectionMenuState.Open(activeDesignator);
                    }
                    else
                    {
                        // Only one non-Manual shape - activate it directly
                        ShapePlacementState.Enter(activeDesignator, availableShapes[0]);
                    }
                    return true;
                }
            }
            // Shift+Tab - quick switch to Manual mode (for any designator with shapes)
            else if (canUseShapes)
            {
                // Block Shift+Tab if placement is in progress with points set
                // User must press Escape to clear selection first
                if (ShapePlacementState.IsPlacementInProgress)
                {
                    TolkHelper.Speak("Cannot change shape while placing. Press Escape to clear selection first.");
                    return true;
                }

                // Check if already in manual mode
                if (ShapePlacementState.IsActive && ShapePlacementState.CurrentShape == ShapeType.Manual)
                {
                    TolkHelper.Speak("Already in manual mode.");
                    return true;
                }

                // Reset to manual mode and cancel any shape in progress
                if (ShapePlacementState.IsActive)
                {
                    ShapePlacementState.Reset(); // Use Reset instead of Cancel to avoid announcement
                }
                ShapePlacementState.Enter(activeDesignator, ShapeType.Manual);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Handles R key input for rotating buildings.
        /// If the building is not rotatable, announces that it can't be rotated.
        /// </summary>
        /// <returns>True if the key was handled, false otherwise.</returns>
        private static bool HandleRotateKey(Designator activeDesignator, bool inArchitectMode, RotationDirection direction)
        {
            // Check if the building can be rotated before attempting rotation
            // Some buildings like doors auto-detect their orientation and cannot be manually rotated
            bool canRotate = true;
            string buildingLabel = null;

            if (activeDesignator is Designator_Build buildDesignator)
            {
                if (buildDesignator.PlacingDef is ThingDef thingDef)
                {
                    canRotate = thingDef.rotatable;
                    buildingLabel = thingDef.label;
                }
            }
            else if (activeDesignator is Designator_Place designatorPlace)
            {
                if (designatorPlace.PlacingDef is ThingDef thingDef)
                {
                    canRotate = thingDef.rotatable;
                    buildingLabel = thingDef.label;
                }
            }

            // If not rotatable, announce and return
            if (!canRotate)
            {
                string name = buildingLabel ?? activeDesignator.Label ?? "This";
                // Capitalize first letter for better announcement
                if (!string.IsNullOrEmpty(name))
                {
                    name = char.ToUpper(name[0]) + name.Substring(1);
                }
                TolkHelper.Speak($"{name} can't be rotated.");
                return true;
            }

            // Building is rotatable - proceed with rotation
            if (inArchitectMode)
            {
                ArchitectState.RotateBuilding(direction);
            }
            else if (activeDesignator.GetType().Name == "Designator_MoveGravship")
            {
                // Gravship landing designator — rotate via marker.GravshipRotation
                var markerField = AccessTools.Field(activeDesignator.GetType(), "marker");
                if (markerField != null)
                {
                    var marker = markerField.GetValue(activeDesignator);
                    if (marker != null)
                    {
                        var rotProp = AccessTools.Property(marker.GetType(), "GravshipRotation");
                        if (rotProp != null)
                        {
                            Rot4 currentRot = (Rot4)rotProp.GetValue(marker);
                            currentRot.Rotate(direction);
                            rotProp.SetValue(marker, currentRot);

                            string dirName = currentRot == Rot4.North ? "North" :
                                             currentRot == Rot4.East ? "East" :
                                             currentRot == Rot4.South ? "South" : "West";
                            TolkHelper.Speak($"Gravship facing {dirName}.");
                        }
                    }
                }
            }
            else if (activeDesignator is Designator_Place designatorPlace)
            {
                // Use reflection to access private placingRot field
                var rotField = AccessTools.Field(typeof(Designator_Place), "placingRot");
                if (rotField != null)
                {
                    Rot4 currentRot = (Rot4)rotField.GetValue(designatorPlace);
                    currentRot.Rotate(direction);
                    rotField.SetValue(designatorPlace, currentRot);

                    // Build a proper announcement with direction and special info
                    string announcement = GetDesignatorRotationAnnouncement(designatorPlace, currentRot);
                    TolkHelper.Speak(announcement);
                }
            }
            return true;
        }

        /// <summary>
        /// Handles Shift+Space input for removing shape points or cancelling blueprints.
        /// </summary>
        /// <returns>True if the key was handled, false otherwise.</returns>
        private static bool HandleShiftSpaceKey()
        {
            // If in shape placement mode with points set, remove points step-by-step
            if (ShapePlacementState.IsActive &&
                ShapePlacementState.CurrentShape != ShapeType.Manual &&
                ShapePlacementState.HasFirstPoint)
            {
                ShapePlacementState.RemoveLastPoint();
                return true;
            }
            // If in shape placement mode but no points set, announce that
            else if (ShapePlacementState.IsActive &&
                     ShapePlacementState.CurrentShape != ShapeType.Manual &&
                     !ShapePlacementState.HasFirstPoint)
            {
                TolkHelper.Speak("No points to remove");
                return true;
            }
            // Otherwise, cancel blueprint at cursor position (existing behavior)
            else
            {
                IntVec3 currentPosition = MapNavigationState.CurrentCursorPosition;
                CancelBlueprintAtPosition(currentPosition);
                return true;
            }
        }

        /// <summary>
        /// Handles Space key input for placing cells, setting shape points, or toggling selections.
        /// </summary>
        /// <returns>True if the key was handled, false otherwise.</returns>
        private static bool HandleSpaceKey(Designator activeDesignator, IntVec3 currentPosition, bool inArchitectMode, bool isPlaceDesignator, bool isZoneDesignator, bool isOrderDesignator, bool isCellsDesignator)
        {
            // Check if we're in shape placement mode with a non-Manual shape
            // This applies to ALL designator types (build, orders, zones)
            if (ShapePlacementState.IsActive && ShapePlacementState.CurrentShape != ShapeType.Manual)
            {
                // Two-point placement workflow - same for all designator types
                if (ShapePlacementState.CurrentPhase == PlacementPhase.SettingFirstCorner)
                {
                    ShapePlacementState.SetFirstPoint(currentPosition);
                }
                else if (ShapePlacementState.CurrentPhase == PlacementPhase.SettingSecondCorner)
                {
                    ShapePlacementState.SetSecondPoint(currentPosition);
                }
            }
            // Manual mode or ShapePlacementState inactive - single cell designation
            else
            {
                // For build/place designators (buildings, install)
                if (isPlaceDesignator)
                {
                    AcceptanceReport report = activeDesignator.CanDesignateCell(currentPosition);

                    if (report.Accepted)
                    {
                        // Check meditation focus / tree protection before placing
                        var (placingDef, placingRot) =
                            MeditationProtectionHelper.GetPlacementInfo(activeDesignator);

                        if (placingDef != null &&
                            MeditationProtectionHelper.IsArtificialBuilding(
                                placingDef, Faction.OfPlayer))
                        {
                            var protection = MeditationProtectionHelper.CheckProtection(
                                Find.CurrentMap, placingDef, Faction.OfPlayer,
                                currentPosition, placingRot);

                            if (protection.IsProtected)
                            {
                                string message =
                                    MeditationProtectionHelper.FormatManualBlockMessage(protection);
                                TolkHelper.Speak(message);
                                return true;
                            }
                        }

                        try
                        {
                            activeDesignator.DesignateSingleCell(currentPosition);
                            activeDesignator.Finalize(true);

                            string label = activeDesignator.Label;
                            TolkHelper.Speak($"{label} placed at {currentPosition.x}, {currentPosition.z}");

                            // Clear selected cells for next placement (both architect and gizmo modes)
                            // User presses Enter to confirm and exit placement mode
                            if (inArchitectMode)
                            {
                                ArchitectState.ClearSelectedCells();
                            }
                            // For gizmo placement, stay in placement mode like architect mode
                            // User presses Enter to confirm and exit
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
                // For zone designators
                else if (isZoneDesignator)
                {
                    if (inArchitectMode)
                    {
                        // Toggle cell in the selection list (architect menu mode)
                        ArchitectState.ToggleCell(currentPosition);
                    }
                    else
                    {
                        // Gizmo mode (e.g., expand/shrink from zone gizmo)
                        // Use GizmoZoneEditState for proper zone cell toggling with
                        // connectivity checks, adjacency enforcement, and state tracking
                        // (same behavior as viewing mode's Space key)
                        GizmoZoneEditState.EnsureInitialized(activeDesignator);
                        GizmoZoneEditState.ToggleZoneCellAtCursor();
                    }
                }
                // For orders (Hunt, Haul, etc.) and cells designators (Mine)
                else if ((isOrderDesignator || isCellsDesignator) && inArchitectMode)
                {
                    // Toggle cell in the selection list
                    ArchitectState.ToggleCell(currentPosition);
                }
                // For gravship landing placement designator
                else if (activeDesignator.GetType().Name == "Designator_MoveGravship")
                {
                    // Compensate for vanilla rounding bug in even-sized gravships.
                    // GetSizeRotAdjustedCell uses size/2 but PrefabUtility.GetRoot uses (size-1)/2,
                    // causing a +1 offset per axis when that dimension is even.
                    IntVec3 placementPos = currentPosition;
                    var markerField = AccessTools.Field(activeDesignator.GetType(), "marker");
                    var marker = markerField?.GetValue(activeDesignator) as GravshipLandingMarker;
                    if (marker != null)
                    {
                        IntVec2 size = marker.gravship.Bounds.Size;
                        Rot4 rot = marker.GravshipRotation;

                        // Replicate GetSizeRotAdjustedCell math
                        IntVec3 halfX = new IntVec3(size.x / 2, 0, 0);
                        IntVec3 halfZ = new IntVec3(0, 0, size.z / 2);
                        IntVec3 adjusted = currentPosition;
                        if (rot == Rot4.North) adjusted += halfX + halfZ;
                        else if (rot == Rot4.East) adjusted += halfX - halfZ;
                        else if (rot == Rot4.South) adjusted -= halfX + halfZ;
                        else if (rot == Rot4.West) adjusted += -halfX + halfZ;

                        // See where the marker would actually end up
                        IntVec3 wouldPlace = PrefabUtility.GetRoot(adjusted, size, rot);
                        IntVec3 offset = wouldPlace - currentPosition;
                        if (offset != IntVec3.Zero)
                        {
                            placementPos = currentPosition - offset;
                        }
                    }

                    AcceptanceReport report = activeDesignator.CanDesignateCell(placementPos);
                    if (report.Accepted)
                    {
                        activeDesignator.DesignateSingleCell(placementPos);
                        TolkHelper.Speak($"Gravship positioned at {currentPosition.x}, {currentPosition.z}. Select landing marker and use gizmos to confirm landing.");
                        // DesignateSingleCell auto-deselects the designator
                    }
                    else
                    {
                        string reason = report.Reason ?? "Cannot place here";
                        TolkHelper.Speak($"Invalid: {reason}");
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Handles Enter key input for confirming and executing designations.
        /// </summary>
        /// <returns>True if the key was handled, false otherwise.</returns>
        private static bool HandleEnterKey(Designator activeDesignator, bool inArchitectMode, bool isPlaceDesignator, bool isZoneDesignator, bool isOrderDesignator, bool isCellsDesignator)
        {
            // If in shape placement previewing phase, execute the designation and enter viewing mode
            // This works for ALL designator types (build, orders, zones)
            if (ShapePlacementState.IsActive && ShapePlacementState.CurrentPhase == PlacementPhase.Previewing)
            {
                // Save the current shape before placing (for restore after undo)
                ShapeType currentShape = ShapePlacementState.CurrentShape;

                try
                {
                    // Pass silent: true because ViewingModeState.Enter will announce the placement
                    var result = ShapePlacementState.PlaceDesignations(silent: true);

                    // Check if this is a zone deletion that needs confirmation
                    if (result.NeedsFullDeletionConfirmation)
                    {
                        // Show confirmation dialog for zone deletion
                        ShowZoneDeletionConfirmation(result, activeDesignator, currentShape);
                        return true;
                    }
                    else if (result.PlacedCount > 0)
                    {
                        ViewingModeState.Enter(result, activeDesignator, currentShape);
                        // Reset shape placement state after placing
                        ShapePlacementState.Reset();
                    }
                    else
                    {
                        // No placements - give appropriate feedback based on designator type
                        // Stay in shape placement mode so user can try again (like Escape when corners are set)
                        bool isDeleteDesignator = ShapeHelper.IsDeleteDesignator(activeDesignator);
                        if (isDeleteDesignator)
                        {
                            TolkHelper.Speak("No zone cells in selection. Try again.");
                        }
                        else
                        {
                            TolkHelper.Speak("No valid cells in selection. Try again.");
                        }
                        // Clear selection but stay in placement mode (don't exit entirely)
                        ShapePlacementState.ClearSelectionAndStay(silent: true);
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Error($"[ArchitectPlacementPatch] Exception during placement: {ex}");
                    TolkHelper.Speak("Placement failed. Clearing selection.");
                    ShapePlacementState.Reset();
                }
                return true;
            }
            // If in shape placement mode but corners not yet set, give guidance
            else if (ShapePlacementState.IsActive && ShapePlacementState.CurrentShape != ShapeType.Manual)
            {
                if (ShapePlacementState.CurrentPhase == PlacementPhase.SettingFirstCorner)
                {
                    TolkHelper.Speak("Place first point with Space");
                }
                else if (ShapePlacementState.CurrentPhase == PlacementPhase.SettingSecondCorner)
                {
                    TolkHelper.Speak("Place second point with Space");
                }
                return true;
            }
            // For place designators (build, reinstall) not in shape mode
            else if (isPlaceDesignator)
            {
                // Normal exit - placement completed
                TolkHelper.Speak("Placement completed");
                if (ShapePlacementState.IsActive)
                {
                    ShapePlacementState.Reset();
                }
                if (inArchitectMode)
                    ArchitectState.Reset();
                else
                    Find.DesignatorManager.Deselect();
                return true;
            }
            // For zone designators (not in shape mode) - execute from selected cells
            else if (isZoneDesignator && inArchitectMode)
            {
                Map map = Find.CurrentMap;
                ExecuteZonePlacement(activeDesignator, map);
                // Reset ShapePlacementState if it was active (Manual mode)
                if (ShapePlacementState.IsActive)
                {
                    ShapePlacementState.Reset();
                }
                return true;
            }
            // For zone designators in gizmo mode (not architect mode) - confirm and exit
            else if (isZoneDesignator && !inArchitectMode)
            {
                // In gizmo mode, changes are applied immediately via GizmoZoneEditState
                // Enter just confirms and exits editing mode
                TolkHelper.Speak("Zone editing completed");

                // Reset states
                if (ShapePlacementState.IsActive)
                {
                    ShapePlacementState.Reset();
                }
                if (GizmoZoneEditState.IsActive)
                {
                    GizmoZoneEditState.Reset();
                }
                Find.DesignatorManager.Deselect();
                return true;
            }
            // For orders and cells designators in architect mode (not in shape mode)
            else if (inArchitectMode && (isOrderDesignator || isCellsDesignator))
            {
                // Execute the placement from selected cells
                ArchitectState.ExecutePlacement(Find.CurrentMap);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Handles Escape key input for cancelling shape placement or exiting placement mode.
        /// </summary>
        /// <returns>True if the key was handled, false otherwise.</returns>
        private static bool HandleEscapeKey(bool inArchitectMode)
        {
            // If shape placement is active, check what to do based on points and stack
            if (ShapePlacementState.IsActive)
            {
                // Case 1: Points are set - clear them and stay in placement mode
                if (ShapePlacementState.HasFirstPoint)
                {
                    // Clear points via previewHelper, stay in placement mode
                    // This resets to SettingFirstCorner phase
                    // Use silent=true so we control the announcement here
                    ShapePlacementState.ClearSelectionAndStay(silent: true);
                    TolkHelper.Speak("Selection cleared");
                }
                // Case 2: No points set, but we came from viewing mode - return to it
                else if (ShapePlacementState.HasViewingModeOnStack)
                {
                    ShapePlacementState.Reset();
                    // Re-activate viewing mode with existing segments intact
                    ViewingModeState.Reactivate();
                }
                // Case 3: No points set, no viewing mode on stack - exit architect entirely
                else
                {
                    TolkHelper.Speak("Placement cancelled");
                    ShapePlacementState.Reset();

                    // Check if we need to return to a parent menu (Schedule/Animals → Manage Areas)
                    if (WindowlessAreaState.HasPendingReturn)
                    {
                        WindowlessAreaState.CompletePendingReturn();
                    }
                    else if (inArchitectMode)
                    {
                        ArchitectState.Reset();
                    }
                    else
                    {
                        Find.DesignatorManager.Deselect();
                    }
                }
            }
            else
            {
                TolkHelper.Speak("Placement cancelled");

                // Check if we need to return to a parent menu (Schedule/Animals → Manage Areas)
                if (WindowlessAreaState.HasPendingReturn)
                {
                    WindowlessAreaState.CompletePendingReturn();
                }
                else if (inArchitectMode)
                {
                    ArchitectState.Cancel();
                }
                else
                {
                    Find.DesignatorManager.Deselect();
                }
            }
            return true;
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
                    string thingLabel = thing.LabelShort;
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
        /// Handles keyboard input during local map targeting mode (e.g., transport pod landing).
        /// Returns true if input was handled.
        /// </summary>
        private static bool HandleTargetingModeInput(KeyCode key, bool shiftHeld)
        {
            // Space or Enter - confirm target at cursor position
            if (key == KeyCode.Space || key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                if (shiftHeld)
                    return false;

                IntVec3 targetCell = MapNavigationState.CurrentCursorPosition;
                Map map = Find.CurrentMap;

                if (map == null || !targetCell.InBounds(map))
                {
                    TolkHelper.Speak("Invalid target position", SpeechPriority.High);
                    return true;
                }

                // Validate landing spot using the game's validation
                if (!DropCellFinder.IsGoodDropSpot(targetCell, map, allowFogged: false, canRoofPunch: true))
                {
                    string reason = GetLandingInvalidReason(targetCell, map);
                    TolkHelper.Speak($"Cannot land here: {reason}", SpeechPriority.High);
                    return true;
                }

                // Create target info and let the targeter process it
                LocalTargetInfo target = new LocalTargetInfo(targetCell);

                // Get the action BEFORE stopping targeting (StopTargeting clears the action)
                var actionField = HarmonyLib.AccessTools.Field(typeof(Targeter), "action");
                System.Action<LocalTargetInfo> action = null;
                if (actionField != null)
                {
                    action = actionField.GetValue(Find.Targeter) as System.Action<LocalTargetInfo>;
                }

                // Check if we have an action to invoke
                if (action != null)
                {
                    // Stop targeting and invoke the action
                    Find.Targeter.StopTargeting();
                    action.Invoke(target);
                    TolkHelper.Speak($"Landing confirmed at {targetCell.x}, {targetCell.z}", SpeechPriority.Normal);
                }
                else
                {
                    // No action available - stop targeting but report the error
                    Find.Targeter.StopTargeting();
                    TolkHelper.Speak("Error: Could not confirm target. Please try using the mouse.", SpeechPriority.High);
                }

                return true;
            }

            // Escape - cancel targeting
            if (key == KeyCode.Escape)
            {
                Find.Targeter.StopTargeting();
                TolkHelper.Speak("Targeting cancelled", SpeechPriority.Normal);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets a human-readable reason why a landing spot is invalid.
        /// Note: Thin roofs are VALID - pods punch through them.
        /// Only thick roofs (overhead mountain) block landing.
        /// </summary>
        private static string GetLandingInvalidReason(IntVec3 cell, Map map)
        {
            if (map == null || !cell.InBounds(map))
                return "Out of bounds";

            // Check terrain
            TerrainDef terrain = cell.GetTerrain(map);
            if (terrain != null)
            {
                if (terrain.IsWater)
                    return "Water";
            }

            // Check for thick roof (can't punch through mountain)
            // Note: Thin roofs are OK - pods punch through
            RoofDef roof = cell.GetRoof(map);
            if (roof != null && roof.isThickRoof)
                return "Overhead mountain";

            // Check if walkable (basic passability)
            if (!cell.Walkable(map))
                return "Impassable terrain";

            // Check for buildings/edifices
            Building building = cell.GetEdifice(map);
            if (building != null)
            {
                // IsClearableFreeBuilding buildings (like conduits) are OK
                if (!building.IsClearableFreeBuilding)
                    return building.LabelCap;
            }

            // Check for fog
            if (cell.Fogged(map))
                return "Fogged area";

            // Check for existing skyfallers or transporters
            List<Thing> things = cell.GetThingList(map);
            foreach (Thing thing in things)
            {
                if (thing is IActiveTransporter)
                    return "Another transport pod";
                if (thing is Skyfaller)
                    return "Incoming skyfaller";
            }

            return "Invalid spot";
        }

        // Note: IsZoneDesignator moved to ShapeHelper.IsZoneDesignator()

        /// <summary>
        /// Shows a confirmation dialog when a zone shrink operation would delete the entire zone.
        /// This action cannot be undone with Escape in viewing mode.
        /// </summary>
        private static void ShowZoneDeletionConfirmation(PlacementResult pendingResult, Designator designator, ShapeType currentShape)
        {
            string zoneName = pendingResult.ZonePendingDeletion?.label ?? "this zone";
            string message = $"This will delete the entire zone ({zoneName}). " +
                           "This action cannot be undone with Escape in viewing mode.\n\n" +
                           "To delete zones, you can also use the delete option in the gizmo menu (G).";

            // Announce the dialog for screen readers
            TolkHelper.Speak($"Delete Zone confirmation. {message}");

            Dialog_MessageBox dialog = new Dialog_MessageBox(
                message,
                "Delete Zone",
                () =>
                {
                    // User confirmed deletion
                    var result = ShapePlacementState.ExecuteConfirmedZoneDeletion(pendingResult, silent: true);
                    TolkHelper.Speak($"Zone {zoneName} deleted.");

                    // Exit the entire build/zone interface - deletion cannot be undone
                    ShapePlacementState.Reset();
                    ArchitectState.Reset();
                    Find.DesignatorManager.Deselect();
                },
                "Cancel",
                () =>
                {
                    // User cancelled - stay in shape placement mode
                    TolkHelper.Speak("Deletion cancelled. Still in shape placement mode.");
                },
                null,  // title
                true   // buttonADestructive
            );

            Find.WindowStack.Add(dialog);
        }

        /// <summary>
        /// Executes zone placement with all selected cells.
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
                // Use cursor-based zone selection to determine expand vs create
                IntVec3 referenceCell = ArchitectState.SelectedCells[0];
                ZoneSelectionResult selectionResult = ZoneSelectionHelper.SelectZoneAtCell(designator, referenceCell);

                // Use the designator's standard DesignateMultiCell method
                designator.DesignateMultiCell(ArchitectState.SelectedCells);

                string label = designator.Label ?? "Zone";
                string action = selectionResult.IsExpansion ? "expanded" : "created";
                string zoneName = selectionResult.IsExpansion && selectionResult.TargetZone != null
                    ? selectionResult.TargetZone.label
                    : label;
                TolkHelper.Speak($"{zoneName} {action} with {ArchitectState.SelectedCells.Count} cells");
                Log.Message($"Zone placement executed: {zoneName} {action} with {ArchitectState.SelectedCells.Count} cells");
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
        /// Gets a rotation announcement for a Designator_Place (used by reinstall gizmo, etc.)
        /// Delegates to shared ArchitectState method to avoid duplication.
        /// </summary>
        private static string GetDesignatorRotationAnnouncement(Designator_Place designatorPlace, Rot4 rotation)
        {
            return ArchitectState.GetRotationAnnouncementForDef(designatorPlace.PlacingDef, rotation);
        }
    }

    // NOTE: ArchitectPlacementAnnouncementPatch was removed.
    // Arrow key handling and preview updates are now handled atomically by
    // MapArrowKeyHandler in OnGUI context (via UnifiedKeyboardPatch at Priority 10.5).
    // This fixes the key repeat desync issue where Input.GetKeyDown() in the Prefix
    // wouldn't fire on repeat frames but Input.GetKey() in this Postfix would.

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
