using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;

namespace RimWorldAccess
{
    /// <summary>
    /// Patches CameraDriver.Update so arrow keys move a tile-by-tile cursor the camera follows,
    /// instead of panning the camera.
    /// </summary>
    [HarmonyPatch(typeof(CameraDriver))]
    [HarmonyPatch("Update")]
    public static class MapNavigationPatch
    {
        private static bool hasAnnouncedThisFrame = false;
        private static int lastProcessedFrame = -1;

        /// <summary>Updates the map navigation suppression flag based on active menus.</summary>
        private static void UpdateSuppressionFlag()
        {
            // KEPT deliberately — the PRIMARY suppression signal. Windowless modal mirror scopes
            // have no real window, so WindowsPreventCameraMotion never suppresses vanilla's
            // Input-polling camera pan for them; this short-circuit is what keeps polled arrow keys
            // from panning the camera under every migrated modal screen. Do not change the code.
            if (RimWorldAccess.Shell.FocusStack.AnyLiveModal)
            {
                MapNavigationState.SuppressMapNavigation = true;
                return;
            }

            // Placement mode needs arrow keys even while a Schedule/Animals menu is still active.
            if (ShapePlacementState.IsActive || ViewingModeState.IsActive)
            {
                MapNavigationState.SuppressMapNavigation = false;
                return;
            }

            // Any menu that uses arrow keys suppresses map navigation. The scanner does not.
            MapNavigationState.SuppressMapNavigation =
                WorldNavigationState.IsActive ||
                WindowlessFloatMenuState.IsActive ||
                // The gizmo menu owns ALL arrows while open: Left/Right adjust a focused slider
                // and must never leak to the map cursor or camera pan.
                GizmoNavigationState.IsActive ||
                ShapeSelectionMenuState.IsActive ||
                ArchitectTreeState.IsActive ||
                CaravanFormationState.IsActive ||
                NotificationMenuState.IsActive ||
                QuestMenuState.IsActive ||
                ZoneRenameState.IsActive ||
                StorageSettingsMenuState.IsActive ||
                PlantSelectionMenuState.IsActive ||
                MechControlGroupState.IsActive ||
                RangeEditMenuState.IsActive ||
                WorkMenuState.IsActive ||
                WorkTableState.IsActive ||
                AssignMenuState.IsActive ||
                BillsMenuState.IsActive ||
                PrisonerTabState.IsActive ||
                BillConfigState.IsActive ||
                ThingFilterMenuState.IsActive ||
                TempControlMenuState.IsActive ||
                WindowlessResearchMenuState.IsActive ||
                WindowlessResearchDetailState.IsActive ||
                WindowlessInspectionState.IsActive ||
                WindowlessInventoryState.IsActive ||
                HealthTabState.IsActive ||
                RefuelableComponentState.IsActive ||
                DoorControlState.IsActive ||
                ForbidControlState.IsActive ||
                AnimalsMenuState.IsActive ||
                WildlifeMenuState.IsActive ||
                PawnSkillsTableState.IsActive ||
                TransportPodLoadingState.IsActive ||
                // Windowless overlays drawn over the live map that arrow through their own lists,
                // not the map cursor; without them arrow keys pan the camera underneath.
                MechsMenuState.IsActive ||
                PawnAreaMenuState.IsActive ||
                LearningHelperState.IsActive ||
                StatBreakdownState.IsActive ||
                HistoryState.IsActive ||
                HistoryStatisticsState.IsActive ||
                HistoryMessagesState.IsActive;
                // TransportPodSelectionState, GizmoZoneEditState and ShelfLinkingState are absent
                // on purpose: they use the map cursor for cell selection.
        }

        /// <summary>
        /// Returns false to skip CameraDriver.Update entirely while menus are active, so arrow keys
        /// never pan the camera under them.
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix(CameraDriver __instance)
        {
            hasAnnouncedThisFrame = false;

            // Mirrors CameraDriver.Update's own early return: camera position is not yet restored.
            if (LongEventHandler.ShouldWaitForEvent)
                return true;

            UpdateSuppressionFlag();

            if (Find.CurrentMap == null)
            {
                MapNavigationState.Reset();
                return true; // Let original run
            }

            if (Find.WindowStack != null && Find.WindowStack.WindowsPreventCameraMotion)
            {
                return true; // Let original run (it will also respect this flag)
            }

            // Update() can run several times per frame; process input only once.
            int currentFrame = Time.frameCount;
            if (lastProcessedFrame == currentFrame)
            {
                return true;
            }
            lastProcessedFrame = currentFrame;

            MapNavigationState.CheckForMapChanges();

            // Must precede the suppression check so a new map still initializes under a live menu.
            if (!MapNavigationState.IsInitialized)
            {
                MapNavigationState.Initialize(Find.CurrentMap);

                string initialInfo = TileInfoHelper.GetTileSummary(MapNavigationState.CurrentCursorPosition, Find.CurrentMap);
                TolkHelper.SpeakData(initialInfo);
                MapNavigationState.LastAnnouncedInfo = initialInfo;
                hasAnnouncedThisFrame = true;
                return true;
            }

            // Owns its whole stand-down gate; see CursorIsSpokenFor.
            FollowedPawnAnnouncer.Poll();

            if (MapNavigationState.SuppressMapNavigation)
            {
                return false; // SKIP original - don't let camera pan in menus
            }

            // Input.GetKey rather than Event.current: this is a Unity Update, not an OnGUI
            // callback, so IMGUI events are invalid here.
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
            // Arrow-key navigation itself lives in MapArrowKeyHandler, in OnGUI context, for OS key
            // repeat; the original Update still runs for zoom, following and the rest.
            return true;
        }

        /// <summary>
        /// Switches maps on Shift+comma/period, restoring the cursor to its last position there.
        /// </summary>
        /// <param name="forward">True for Shift+period (next map), false for Shift+comma (previous map)</param>
        private static void HandleMapSwitching(bool forward)
        {
            int mapCount = PawnSelectionState.GetMapCount();

            if (mapCount <= 1)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Switch.OnlyOne".Loc());
                hasAnnouncedThisFrame = true;
                return;
            }

            Pawn focusPawn = forward
                ? PawnSelectionState.SwitchToNextMap(out string mapName, out string presenceInfo)
                : PawnSelectionState.SwitchToPreviousMap(out mapName, out presenceInfo);

            if (string.IsNullOrEmpty(mapName))
            {
                TolkHelper.Speak("RimWorldAccess.Map.Switch.Failed".Loc());
                hasAnnouncedThisFrame = true;
                return;
            }

            MapNavigationState.RestoreCursorForCurrentMap();

            ScannerState.Invalidate();

            if (Find.Selector != null)
            {
                Find.Selector.ClearSelection();
            }

            string fullAnnouncement;
            if (string.IsNullOrEmpty(presenceInfo))
            {
                fullAnnouncement = (string)"RimWorldAccess.Map.Switch.NoPawns".Translate(mapName);
            }
            else
            {
                fullAnnouncement = (string)"RimWorldAccess.Map.Switch.WithInfo".Translate(mapName, presenceInfo);
            }
            TolkHelper.SpeakData(fullAnnouncement);
            MapNavigationState.LastAnnouncedInfo = fullAnnouncement;
            hasAnnouncedThisFrame = true;
        }

        /// <summary>
        /// Kills camera drift: Cursor mode always resets velocity, Pawn mode only on a frame where
        /// arrow keys were pressed.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(CameraDriver __instance)
        {
            // Blocks edge scrolling and any other accumulated velocity.
            if (MapNavigationState.CurrentCameraMode == CameraFollowMode.Cursor)
            {
                Traverse.Create(__instance).Field("velocity").SetValue(Vector3.zero);
                Traverse.Create(__instance).Field("desiredDollyRaw").SetValue(Vector2.zero);
            }
            else if (hasAnnouncedThisFrame)
            {
                Traverse.Create(__instance).Field("velocity").SetValue(Vector3.zero);
                Traverse.Create(__instance).Field("desiredDollyRaw").SetValue(Vector2.zero);
            }
        }
    }

    /// <summary>
    /// Narrows the game's colonist cycling, which spans every map, to the current map only.
    /// </summary>
    [HarmonyPatch(typeof(ThingSelectionUtility))]
    public static class ThingSelectionUtilityPatch
    {
        /// <summary>Prefix patch for SelectNextColonist to filter by current map.</summary>
        [HarmonyPatch("SelectNextColonist")]
        [HarmonyPrefix]
        public static bool SelectNextColonist_Prefix()
        {
            if (!WorldRendererUtility.DrawingMap)
                return true;

            // Shift means a map switch, which HandleMapSwitching already ran; block the original.
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shiftHeld)
                return false; // Block original - our map switching already handled it

            if (MultiSelectState.IsMultiSelectMode)
            {
                MultiSelectState.NavigateFocusNext();
                return false;
            }

            // On a non-colonist section, cycle within that section instead of colonists.
            Pawn selectedPawn;
            if (ColonistBarState.IsOnNonColonistSection)
            {
                selectedPawn = ColonistBarState.SelectNextInSection();
                if (selectedPawn == null)
                {
                    TolkHelper.Speak(ColonistBarState.IsOnMechSection
                        ? "RimWorldAccess.Map.Pawn.NoMechs".Loc()
                        : ColonistBarState.CurrentSectionNoneHereKey.Loc());
                    return false;
                }
            }
            else
            {
                selectedPawn = PawnSelectionState.SelectNextColonist();
                if (selectedPawn == null)
                {
                    TolkHelper.Speak("RimWorldAccess.Map.Pawn.NoColonists".Loc());
                    return false;
                }
            }

            // Changing Selector ends an active targeting or placement session, so redirect to a
            // cursor jump and let Enter target or place there.
            if (PawnSelectionState.TryRedirectForTargetingOrPlacement(selectedPawn))
                return false;

            if (Find.Selector != null)
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(selectedPawn, playSound: true, forceDesignatorDeselect: !ShapePlacementState.IsActive);
            }

            MultiSelectState.NotifySingleSelect(selectedPawn);

            // The cursor deliberately stays put; Alt+C moves it to the pawn.
            if (Find.CameraDriver != null)
            {
                Find.CameraDriver.JumpToCurrentMapLoc(selectedPawn.Position);
            }
            MapNavigationState.CurrentCameraMode = CameraFollowMode.Pawn;

            GizmoNavigationState.PawnJustSelected = true;

            ColonistBarState.SyncBarPosition(selectedPawn);

            TolkHelper.SpeakData(MapSelectionAnnouncer.Describe(selectedPawn));

            return false; // Block original method
        }

        /// <summary>Prefix patch for SelectPreviousColonist to filter by current map.</summary>
        [HarmonyPatch("SelectPreviousColonist")]
        [HarmonyPrefix]
        public static bool SelectPreviousColonist_Prefix()
        {
            if (!WorldRendererUtility.DrawingMap)
                return true;

            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shiftHeld)
                return false; // Block original - our map switching already handled it

            if (MultiSelectState.IsMultiSelectMode)
            {
                MultiSelectState.NavigateFocusPrevious();
                return false;
            }

            // On a non-colonist section, cycle within that section instead of colonists.
            Pawn selectedPawn;
            if (ColonistBarState.IsOnNonColonistSection)
            {
                selectedPawn = ColonistBarState.SelectPreviousInSection();
                if (selectedPawn == null)
                {
                    TolkHelper.Speak(ColonistBarState.IsOnMechSection
                        ? "RimWorldAccess.Map.Pawn.NoMechs".Loc()
                        : ColonistBarState.CurrentSectionNoneHereKey.Loc());
                    return false;
                }
            }
            else
            {
                selectedPawn = PawnSelectionState.SelectPreviousColonist();
                if (selectedPawn == null)
                {
                    TolkHelper.Speak("RimWorldAccess.Map.Pawn.NoColonists".Loc());
                    return false;
                }
            }

            // See SelectNextColonist_Prefix for rationale.
            if (PawnSelectionState.TryRedirectForTargetingOrPlacement(selectedPawn))
                return false;

            if (Find.Selector != null)
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(selectedPawn, playSound: true, forceDesignatorDeselect: !ShapePlacementState.IsActive);
            }

            MultiSelectState.NotifySingleSelect(selectedPawn);

            // The cursor deliberately stays put; Alt+C moves it to the pawn.
            if (Find.CameraDriver != null)
            {
                Find.CameraDriver.JumpToCurrentMapLoc(selectedPawn.Position);
            }
            MapNavigationState.CurrentCameraMode = CameraFollowMode.Pawn;

            GizmoNavigationState.PawnJustSelected = true;

            ColonistBarState.SyncBarPosition(selectedPawn);

            TolkHelper.SpeakData(MapSelectionAnnouncer.Describe(selectedPawn));

            return false; // Block original method
        }
    }

    /// <summary>Blocks RimWorld's automatic pawn following when in Cursor mode.</summary>
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

    /// <summary>
    /// Blocks RimWorld's built-in arrow-key camera dolly while the mod owns arrow-key navigation:
    /// cursor movement does its own JumpToCurrentMapLoc and jump-mode adjustments must leave the
    /// camera alone, so the vanilla dolly would pan in parallel (2.4x faster with Shift held).
    /// </summary>
    [HarmonyPatch(typeof(CameraDriver))]
    [HarmonyPatch("CameraDriverOnGUI")]
    public static class CameraDriverOnGUIPatch
    {
        [HarmonyPostfix]
        public static void Postfix(CameraDriver __instance)
        {
            if (Find.CurrentMap == null)
                return;

            if (!WorldRendererUtility.DrawingMap)
                return;

            if (!MapNavigationState.IsInitialized)
                return;

            // Zero the keyboard dolly from vanilla's MapDolly_* bindings; the mouse-drag dolly is
            // preserved in non-Cursor modes.
            Traverse.Create(__instance).Field("desiredDolly").SetValue(Vector2.zero);

            MapHoverSpeech.Evaluate();
            MapDragSelectSpeech.Evaluate();
            PointerWarpState.Tick();
        }
    }
}
