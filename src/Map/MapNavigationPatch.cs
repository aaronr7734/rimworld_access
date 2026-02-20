using System.Linq;
using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch for CameraDriver.Update() to add accessible map navigation.
    /// Intercepts arrow key input to move a cursor tile-by-tile instead of panning the camera.
    /// The camera follows the cursor, keeping it centered on screen.
    /// </summary>
    [HarmonyPatch(typeof(CameraDriver))]
    [HarmonyPatch("Update")]
    public static class MapNavigationPatch
    {
        private static bool hasAnnouncedThisFrame = false;
        private static int lastProcessedFrame = -1;

        /// <summary>
        /// Updates the map navigation suppression flag based on active menus.
        /// </summary>
        private static void UpdateSuppressionFlag()
        {
            // Don't suppress if placement mode is active - it needs arrow key navigation
            // even if Schedule/Animals menu is technically still "active" in the background
            if (ShapePlacementState.IsActive || ViewingModeState.IsActive)
            {
                MapNavigationState.SuppressMapNavigation = false;
                return;
            }

            // Suppress map navigation if ANY menu that uses arrow keys is active
            // Note: Scanner is NOT included here because it doesn't suppress map navigation
            MapNavigationState.SuppressMapNavigation =
                WorldNavigationState.IsActive ||
                WindowlessDialogState.IsActive ||
                WindowlessFloatMenuState.IsActive ||
                ShapeSelectionMenuState.IsActive ||
                // Note: ViewingModeState is NOT included - it allows arrow navigation for moving around
                ArchitectTreeState.IsActive ||
                CaravanFormationState.IsActive ||
                WindowlessPauseMenuState.IsActive ||
                NotificationMenuState.IsActive ||
                QuestMenuState.IsActive ||
                WindowlessSaveMenuState.IsActive ||
                WindowlessConfirmationState.IsActive ||
                WindowlessDeleteConfirmationState.IsActive ||
                WindowlessOptionsMenuState.IsActive ||
                ZoneRenameState.IsActive ||
                PlaySettingsMenuState.IsActive ||
                StorageSettingsMenuState.IsActive ||
                PlantSelectionMenuState.IsActive ||
                MechControlGroupState.IsActive ||
                RangeEditMenuState.IsActive ||
                WorkMenuState.IsActive ||
                AssignMenuState.IsActive ||
                WindowlessOutfitPolicyState.IsActive ||
                WindowlessFoodPolicyState.IsActive ||
                WindowlessDrugPolicyState.IsActive ||
                WindowlessAreaState.IsActive ||
                WindowlessScheduleState.IsActive ||
                BillsMenuState.IsActive ||
                PrisonerTabState.IsActive ||
                BillConfigState.IsActive ||
                ThingFilterMenuState.IsActive ||
                TempControlMenuState.IsActive ||
                BedAssignmentState.IsActive ||
                BuildingOwnerAssignmentState.IsActive ||
                WindowlessResearchMenuState.IsActive ||
                WindowlessResearchDetailState.IsActive ||
                WindowlessInspectionState.IsActive ||
                WindowlessInventoryState.IsActive ||
                HealthTabState.IsActive ||
                FlickableComponentState.IsActive ||
                RefuelableComponentState.IsActive ||
                BreakdownableComponentState.IsActive ||
                DoorControlState.IsActive ||
                ForbidControlState.IsActive ||
                AnimalsMenuState.IsActive ||
                WildlifeMenuState.IsActive ||
                TransportPodLoadingState.IsActive ||
                // History tab states
                HistoryState.IsActive ||
                HistoryStatisticsState.IsActive ||
                HistoryMessagesState.IsActive;
                // Note: TransportPodSelectionState is NOT included - it uses map navigation for cursor movement
        }

        /// <summary>
        /// Prefix patch that intercepts arrow key input before the camera's normal panning behavior.
        /// Returns false to skip original CameraDriver.Update() when menus are active (prevents camera panning in menus).
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix(CameraDriver __instance)
        {
            // Reset per-frame flag
            hasAnnouncedThisFrame = false;

            // Update suppression flag based on active menus
            UpdateSuppressionFlag();

            // Only process input during normal gameplay with a valid map
            if (Find.CurrentMap == null)
            {
                MapNavigationState.Reset();
                return true; // Let original run
            }

            // Don't process arrow keys if any dialog or window that prevents camera motion is open
            if (Find.WindowStack != null && Find.WindowStack.WindowsPreventCameraMotion)
            {
                return true; // Let original run (it will also respect this flag)
            }

            // Prevent processing input multiple times in the same frame
            // (Update() can be called multiple times per frame)
            int currentFrame = Time.frameCount;
            if (lastProcessedFrame == currentFrame)
            {
                return true;
            }
            lastProcessedFrame = currentFrame;

            // Check for map additions/removals and announce to user
            MapNavigationState.CheckForMapChanges();

            // Initialize cursor position if needed - MUST happen before suppression check
            // so that new maps get initialized even if a menu is temporarily active
            if (!MapNavigationState.IsInitialized)
            {
                MapNavigationState.Initialize(Find.CurrentMap);

                // Announce starting position
                string initialInfo = TileInfoHelper.GetTileSummary(MapNavigationState.CurrentCursorPosition, Find.CurrentMap);
                TolkHelper.Speak(initialInfo);
                MapNavigationState.LastAnnouncedInfo = initialInfo;
                hasAnnouncedThisFrame = true;
                return true;
            }

            // When menus are open, skip the original CameraDriver.Update() entirely
            // This prevents arrow keys from panning the camera while in menus
            if (MapNavigationState.SuppressMapNavigation)
            {
                return false; // SKIP original - don't let camera pan in menus
            }

            // Check for map switching (Shift+comma/period)
            // Regular comma/period pawn cycling is handled by ThingSelectionUtilityPatch
            // NOTE: We use Input.GetKey/GetKeyDown here because CameraDriver.Update() is a
            // Unity Update() method, not an OnGUI callback. IMGUI events (Event.current) are
            // only valid during OnGUI calls and will be null/invalid in Update().
            bool shiftHeldForMapSwitch = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (shiftHeldForMapSwitch && Input.GetKeyDown(KeyCode.Period))
            {
                HandleMapSwitching(forward: true);
                return true;
            }
            else if (shiftHeldForMapSwitch && Input.GetKeyDown(KeyCode.Comma))
            {
                HandleMapSwitching(forward: false);
                return true;
            }
            // Note: Regular comma/period without shift passes through to game's ShortcutKeys
            // which calls ThingSelectionUtility.SelectNext/PreviousColonist()
            // Our ThingSelectionUtilityPatch intercepts those to filter by current map

            // Arrow key navigation is now handled by MapArrowKeyHandler in OnGUI context
            // (via UnifiedKeyboardPatch at Priority 10.5) for OS key repeat support.
            // This CameraDriver.Update() Prefix still handles:
            // - Frame flag reset and suppression updates
            // - Map null check and reset
            // - WindowsPreventCameraMotion check
            // - Frame deduplication
            // - Map changes check and initialization
            // - Map switching with Shift+comma/period

            // Let original CameraDriver.Update() run for non-arrow-key functionality
            // (zoom, following, etc.)
            return true;
        }

        /// <summary>
        /// Handles switching between maps when Shift+comma or Shift+period is pressed.
        /// Restores cursor to last known position on the target map.
        /// </summary>
        /// <param name="forward">True for Shift+period (next map), false for Shift+comma (previous map)</param>
        private static void HandleMapSwitching(bool forward)
        {
            int mapCount = PawnSelectionState.GetMapCount();

            if (mapCount <= 1)
            {
                TolkHelper.Speak("Only one map available");
                hasAnnouncedThisFrame = true;
                return;
            }

            // Switch to the next/previous map
            Pawn focusPawn = forward
                ? PawnSelectionState.SwitchToNextMap(out string mapName, out int pawnCount)
                : PawnSelectionState.SwitchToPreviousMap(out mapName, out pawnCount);

            // Check if map switch actually happened (mapName will be set if successful)
            if (string.IsNullOrEmpty(mapName))
            {
                TolkHelper.Speak("Could not switch maps");
                hasAnnouncedThisFrame = true;
                return;
            }

            // Restore cursor to last known position for this map
            MapNavigationState.RestoreCursorForCurrentMap();

            // Invalidate scanner cache so it refreshes for the new map
            ScannerState.Invalidate();

            // Clear any selection when switching maps
            if (Find.Selector != null)
            {
                Find.Selector.ClearSelection();
            }

            // Build announcement: "Now at [MapName] ([X] colonists)"
            string colonistWord = pawnCount == 1 ? "colonist" : "colonists";
            string fullAnnouncement;
            if (pawnCount == 0)
            {
                fullAnnouncement = $"Now at {mapName}. No colonists here.";
            }
            else
            {
                fullAnnouncement = $"Now at {mapName} ({pawnCount} {colonistWord})";
            }
            TolkHelper.Speak(fullAnnouncement);
            MapNavigationState.LastAnnouncedInfo = fullAnnouncement;
            hasAnnouncedThisFrame = true;
        }

        /// <summary>
        /// Postfix patch to prevent camera drift and default camera dolly movement.
        /// In Cursor mode: always reset velocity to prevent drift from edge scrolling.
        /// In Pawn mode: only reset velocity when arrow keys were pressed this frame.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(CameraDriver __instance)
        {
            // In Cursor mode, always reset velocity to prevent drift
            // This blocks edge scrolling and any other accumulated velocity
            if (MapNavigationState.CurrentCameraMode == CameraFollowMode.Cursor)
            {
                Traverse.Create(__instance).Field("velocity").SetValue(Vector3.zero);
                Traverse.Create(__instance).Field("desiredDollyRaw").SetValue(Vector2.zero);
            }
            else if (hasAnnouncedThisFrame)
            {
                // In Pawn mode with arrow key usage, also reset for that frame
                Traverse.Create(__instance).Field("velocity").SetValue(Vector3.zero);
                Traverse.Create(__instance).Field("desiredDollyRaw").SetValue(Vector2.zero);
            }
        }
    }

    /// <summary>
    /// Harmony patches for ThingSelectionUtility to override the game's colonist cycling.
    /// By default, the game cycles through ALL colonists across all maps.
    /// We override this to only cycle through colonists on the CURRENT map.
    /// Shift+comma/period for map switching is handled separately in MapNavigationPatch.
    /// </summary>
    [HarmonyPatch(typeof(ThingSelectionUtility))]
    public static class ThingSelectionUtilityPatch
    {
        /// <summary>
        /// Prefix patch for SelectNextColonist to filter by current map.
        /// </summary>
        [HarmonyPatch("SelectNextColonist")]
        [HarmonyPrefix]
        public static bool SelectNextColonist_Prefix()
        {
            // If world view is selected, let the original method handle it (caravan cycling)
            if (WorldRendererUtility.WorldRendered)
                return true;

            // Check if shift is held - if so, this is a map switch request
            // Let our HandleMapSwitching in MapNavigationPatch handle it (it already ran)
            // Just block the original to prevent double-handling
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shiftHeld)
                return false; // Block original - our map switching already handled it

            // When on mech section, cycle mechs instead of colonists
            Pawn selectedPawn;
            if (ColonistBarState.IsOnMechSection)
            {
                selectedPawn = ColonistBarState.SelectNextMech();
                if (selectedPawn == null)
                {
                    TolkHelper.Speak("No mechs on this map");
                    return false;
                }
            }
            else
            {
                selectedPawn = PawnSelectionState.SelectNextColonist();
                if (selectedPawn == null)
                {
                    TolkHelper.Speak("No colonists on this map");
                    return false;
                }
            }

            // Select the pawn and jump camera to follow
            if (Find.Selector != null)
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(selectedPawn);
            }

            // Jump camera to pawn and enable Pawn Following mode
            // NOTE: Cursor stays where it was - user can press Alt+C to move cursor to pawn
            if (Find.CameraDriver != null)
            {
                Find.CameraDriver.JumpToCurrentMapLoc(selectedPawn.Position);
            }
            MapNavigationState.CurrentCameraMode = CameraFollowMode.Pawn;

            // Set flag so G key shows this pawn's gizmos (until arrow keys move cursor)
            GizmoNavigationState.PawnJustSelected = true;

            // Keep colonist bar position in sync
            ColonistBarState.SyncBarPosition(selectedPawn);

            // Announce selection
            string currentTask = selectedPawn.GetJobReport();
            if (string.IsNullOrEmpty(currentTask))
                currentTask = "Idle";

            TolkHelper.Speak($"{selectedPawn.LabelShort} selected - {currentTask}");

            return false; // Block original method
        }

        /// <summary>
        /// Prefix patch for SelectPreviousColonist to filter by current map.
        /// </summary>
        [HarmonyPatch("SelectPreviousColonist")]
        [HarmonyPrefix]
        public static bool SelectPreviousColonist_Prefix()
        {
            // If world view is selected, let the original method handle it (caravan cycling)
            if (WorldRendererUtility.WorldRendered)
                return true;

            // Check if shift is held - if so, this is a map switch request
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shiftHeld)
                return false; // Block original - our map switching already handled it

            // When on mech section, cycle mechs instead of colonists
            Pawn selectedPawn;
            if (ColonistBarState.IsOnMechSection)
            {
                selectedPawn = ColonistBarState.SelectPreviousMech();
                if (selectedPawn == null)
                {
                    TolkHelper.Speak("No mechs on this map");
                    return false;
                }
            }
            else
            {
                selectedPawn = PawnSelectionState.SelectPreviousColonist();
                if (selectedPawn == null)
                {
                    TolkHelper.Speak("No colonists on this map");
                    return false;
                }
            }

            // Select the pawn and jump camera to follow
            if (Find.Selector != null)
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(selectedPawn);
            }

            // Jump camera to pawn and enable Pawn Following mode
            // NOTE: Cursor stays where it was - user can press Alt+C to move cursor to pawn
            if (Find.CameraDriver != null)
            {
                Find.CameraDriver.JumpToCurrentMapLoc(selectedPawn.Position);
            }
            MapNavigationState.CurrentCameraMode = CameraFollowMode.Pawn;

            // Set flag so G key shows this pawn's gizmos (until arrow keys move cursor)
            GizmoNavigationState.PawnJustSelected = true;

            // Keep colonist bar position in sync
            ColonistBarState.SyncBarPosition(selectedPawn);

            // Announce selection
            string currentTask = selectedPawn.GetJobReport();
            if (string.IsNullOrEmpty(currentTask))
                currentTask = "Idle";

            TolkHelper.Speak($"{selectedPawn.LabelShort} selected - {currentTask}");

            return false; // Block original method
        }
    }

    /// <summary>
    /// Blocks RimWorld's automatic pawn following when in Cursor mode.
    /// </summary>
    [HarmonyPatch(typeof(CameraMapConfig))]
    [HarmonyPatch("ConfigFixedUpdate_60")]
    public static class CameraMapConfigPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (Find.CurrentMap == null)
                return true;

            // Block pawn following in Cursor mode
            if (MapNavigationState.CurrentCameraMode == CameraFollowMode.Cursor)
                return false;

            return true;
        }
    }
}
