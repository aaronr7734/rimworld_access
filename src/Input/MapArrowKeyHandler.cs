using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Handles arrow key input for local colony map navigation in OnGUI context.
    /// This enables OS key repeat support, unlike the Update() context which only fires once.
    /// Called from UnifiedKeyboardPatch at Priority 10.5.
    /// </summary>
    public static class MapArrowKeyHandler
    {
        /// <summary>
        /// Handles an arrow key press for map navigation.
        /// Returns true if the key was handled and should be consumed.
        /// </summary>
        /// <param name="key">The arrow key pressed (Up, Down, Left, Right)</param>
        /// <param name="ctrlHeld">True if Ctrl modifier is held (from Event.current.control)</param>
        /// <param name="shiftHeld">True if Shift modifier is held (from Event.current.shift)</param>
        /// <returns>True if the key was handled, false otherwise</returns>
        public static bool HandleArrowKey(KeyCode key, bool ctrlHeld, bool shiftHeld)
        {
            // Handle Shift+Up/Down for jump mode cycling
            // NOTE: Jump mode cycling now works EVERYWHERE including zone creation mode
            // (previously blocked by incorrect blockJumpModeCycling check)
            if (shiftHeld)
            {
                if (key == KeyCode.UpArrow)
                {
                    MapNavigationState.CycleJumpModeForward();
                    return true;
                }
                else if (key == KeyCode.DownArrow)
                {
                    MapNavigationState.CycleJumpModeBackward();
                    return true;
                }
                // Shift+Left/Right adjusts preset distance (only in PresetDistance mode)
                // Ctrl+Shift+Left/Right adjusts by 10 tiles
                else if (MapNavigationState.CurrentJumpMode == JumpMode.PresetDistance)
                {
                    int step = ctrlHeld ? 10 : 1;
                    if (key == KeyCode.LeftArrow)
                    {
                        MapNavigationState.DecreasePresetDistance(step);
                        return true;
                    }
                    else if (key == KeyCode.RightArrow)
                    {
                        MapNavigationState.IncreasePresetDistance(step);
                        return true;
                    }
                }
            }

            // Determine movement direction
            IntVec3 moveOffset = IntVec3.Zero;
            bool keyPressed = false;

            switch (key)
            {
                case KeyCode.UpArrow:
                    moveOffset = IntVec3.North; // North is positive Z
                    keyPressed = true;
                    break;
                case KeyCode.DownArrow:
                    moveOffset = IntVec3.South; // South is negative Z
                    keyPressed = true;
                    break;
                case KeyCode.LeftArrow:
                    moveOffset = IntVec3.West; // West is negative X
                    keyPressed = true;
                    break;
                case KeyCode.RightArrow:
                    moveOffset = IntVec3.East; // East is positive X
                    keyPressed = true;
                    break;
            }

            if (!keyPressed)
                return false;

            // Move the cursor position - either jump or normal movement
            bool isJump = ctrlHeld;
            bool positionChanged;

            if (isJump)
            {
                // Use appropriate jump method based on current jump mode
                switch (MapNavigationState.CurrentJumpMode)
                {
                    case JumpMode.Terrain:
                        positionChanged = MapNavigationState.JumpToNextTerrainType(moveOffset, Find.CurrentMap);
                        break;
                    case JumpMode.Buildings:
                        positionChanged = MapNavigationState.JumpToNextBuilding(moveOffset, Find.CurrentMap);
                        break;
                    case JumpMode.Geysers:
                        positionChanged = MapNavigationState.JumpToNextGeyser(moveOffset, Find.CurrentMap);
                        break;
                    case JumpMode.HarvestableTrees:
                        positionChanged = MapNavigationState.JumpToNextHarvestableTrees(moveOffset, Find.CurrentMap);
                        break;
                    case JumpMode.MinableTiles:
                        positionChanged = MapNavigationState.JumpToNextMinableTiles(moveOffset, Find.CurrentMap);
                        break;
                    case JumpMode.PresetDistance:
                        positionChanged = MapNavigationState.JumpPresetDistance(moveOffset, Find.CurrentMap);
                        break;
                    case JumpMode.AdjacentToWall:
                        positionChanged = MapNavigationState.JumpToAdjacentToWall(moveOffset, Find.CurrentMap);
                        break;
                    default:
                        positionChanged = MapNavigationState.MoveCursor(moveOffset, Find.CurrentMap);
                        break;
                }
            }
            else
            {
                positionChanged = MapNavigationState.MoveCursor(moveOffset, Find.CurrentMap);
            }

            if (positionChanged)
            {
                HandlePositionChanged(isJump);
            }
            else
            {
                HandleBoundaryReached(isJump);
            }

            return true;
        }

        /// <summary>
        /// Handles post-movement updates when cursor position changed.
        /// Updates previews, camera, audio, and announcements.
        /// </summary>
        private static void HandlePositionChanged(bool isJump)
        {
            // Clear the "pawn just selected" flag since user is now navigating the map
            GizmoNavigationState.PawnJustSelected = false;

            // Get the new cursor position
            IntVec3 newPosition = MapNavigationState.CurrentCursorPosition;

            // Update rectangle preview if in zone creation mode with a start corner
            if (ZoneCreationState.IsInCreationMode && ZoneCreationState.HasRectangleStart)
            {
                ZoneCreationState.UpdatePreview(newPosition);
            }

            // Update rectangle preview if in area painting mode with a start corner
            if (AreaPaintingState.IsActive && AreaPaintingState.HasRectangleStart)
            {
                AreaPaintingState.UpdatePreview(newPosition);
            }

            // Move camera to center on new cursor position
            Find.CameraDriver.JumpToCurrentMapLoc(newPosition);

            // Switch to Cursor mode - camera follows cursor, blocks pawn following
            MapNavigationState.CurrentCameraMode = CameraFollowMode.Cursor;

            // Play terrain audio feedback
            TerrainDef terrain = newPosition.GetTerrain(Find.CurrentMap);
            TerrainAudioHelper.PlayTerrainAudio(terrain, 0.5f);

            // Announce the position with all contextual info
            AnnouncePosition(newPosition, Find.CurrentMap);
        }

        /// <summary>
        /// Announces the tile at the given position with all contextual prefixes.
        /// Used by both arrow key movement and Go To coordinate input.
        /// This includes deep ore info, "in area", shape dimensions, etc.
        /// </summary>
        public static void AnnouncePosition(IntVec3 position, Map map)
        {
            // Update shape preview if in shape placement mode
            if (ShapePlacementState.ShouldUpdatePreviewOnMove())
            {
                ShapePlacementState.UpdatePreview(position);
            }

            // Get base tile info
            string tileInfo = TileInfoHelper.GetTileSummary(position, map);

            // Add context prefixes based on current mode
            tileInfo = AddContextPrefix(tileInfo, position);

            // Only announce if different from last announcement (avoids spam)
            if (tileInfo != MapNavigationState.LastAnnouncedInfo)
            {
                TolkHelper.Speak(tileInfo);
                MapNavigationState.LastAnnouncedInfo = tileInfo;
            }
        }

        /// <summary>
        /// Handles when cursor is at map boundary and cannot move further.
        /// </summary>
        private static void HandleBoundaryReached(bool isJump)
        {
            // Skip for AdjacentToWall jump mode as it handles its own announcements
            if (isJump && MapNavigationState.CurrentJumpMode == JumpMode.AdjacentToWall)
                return;

            TolkHelper.Speak("Map boundary");
        }

        /// <summary>
        /// Adds appropriate context prefix to tile info based on current mode.
        /// </summary>
        private static string AddContextPrefix(string tileInfo, IntVec3 position)
        {
            // Jump targeting mode - announce per-tile jump validity
            if (JumpTargetingState.IsActive)
            {
                return JumpTargetingState.GetJumpValidityPrefix(position) + tileInfo;
            }

            // Zone creation mode - single tile selection
            if (ZoneCreationState.IsInCreationMode &&
                ZoneCreationState.SelectionMode == ZoneSelectionMode.SingleTile &&
                ZoneCreationState.IsCellSelected(position))
            {
                return "Selected, " + tileInfo;
            }

            // Area painting mode - preview or staged cells
            if (AreaPaintingState.IsActive)
            {
                if (AreaPaintingState.IsInPreviewMode && AreaPaintingState.PreviewCells.Contains(position))
                {
                    return "Preview, " + tileInfo;
                }
                else if (AreaPaintingState.StagedCells.Contains(position))
                {
                    return "Selected, " + tileInfo;
                }
            }

            // Architect placement mode - shape preview or selected cells
            if (ArchitectState.IsInPlacementMode)
            {
                // Check for deep ore info when placing a deep drill
                if (TileInfoHelper.ShouldShowDeepOreForCurrentDesignator())
                {
                    string deepOreInfo = TileInfoHelper.GetDeepOreInfo(position, Find.CurrentMap);
                    if (!string.IsNullOrEmpty(deepOreInfo))
                    {
                        tileInfo = deepOreInfo + ", " + tileInfo;
                    }
                }

                if (ShapePlacementState.IsActive && ShapePlacementState.PreviewCells.Contains(position))
                {
                    // Only label endpoints, not intermediate tiles
                    // For second point, only announce if it's been confirmed (Previewing phase),
                    // not while still being selected (SettingSecondCorner phase)
                    if (ShapePlacementState.FirstPoint.HasValue && position == ShapePlacementState.FirstPoint.Value)
                    {
                        return "First point, " + tileInfo;
                    }
                    else if (ShapePlacementState.CurrentPhase == PlacementPhase.Previewing &&
                             ShapePlacementState.SecondPoint.HasValue && position == ShapePlacementState.SecondPoint.Value)
                    {
                        return "Second point, " + tileInfo;
                    }
                    // No prefix for intermediate tiles in preview
                }
                else if (ArchitectState.SelectedCells.Contains(position))
                {
                    return "Selected, " + tileInfo;
                }
            }

            // Shelf linking mode - selected storage
            if (ShelfLinkingState.IsActive && ShelfLinkingState.IsStorageSelectedAt(position))
            {
                return "Selected, " + tileInfo;
            }

            // Area designator - show area membership when navigating
            // This helps users understand which cells are already in the area during expand/shrink
            // Works for both Allowed Areas and Built-in Areas (Snow/Sand, Roof, Home)
            if (ShapePlacementState.IsActive)
            {
                Designator activeDesignator = ShapePlacementState.ActiveDesignator;
                if (activeDesignator != null)
                {
                    Area targetArea = null;

                    if (ShapeHelper.IsAreaDesignator(activeDesignator))
                    {
                        // Allowed areas - get from static selectedArea
                        targetArea = Designator_AreaAllowed.selectedArea;
                    }
                    else if (ShapeHelper.IsBuiltInAreaDesignator(activeDesignator))
                    {
                        // Built-in areas - get from map's AreaManager
                        targetArea = ShapeHelper.GetBuiltInAreaForDesignator(activeDesignator, Find.CurrentMap);
                    }

                    if (targetArea != null && targetArea.Map != null &&
                        position.InBounds(targetArea.Map) && targetArea[position])
                    {
                        return "In area, " + tileInfo;
                    }
                }
            }
            else if (ViewingModeState.IsActive && (ViewingModeState.IsAreaDesignator || ViewingModeState.IsBuiltInAreaDesignator))
            {
                Area targetArea = ViewingModeState.TargetArea;
                if (targetArea != null && targetArea.Map != null &&
                    position.InBounds(targetArea.Map) && targetArea[position])
                {
                    return "In area, " + tileInfo;
                }
            }

            // Substructure overlay - check engine's overlay toggle and announce disconnected tiles
            SubstructureOverlayState.CheckOverlayState();
            if (SubstructureOverlayState.IsOverlayActive(Find.CurrentMap))
            {
                if (SubstructureOverlayState.IsDisconnectedAt(position, Find.CurrentMap))
                {
                    tileInfo = "Disconnected, " + tileInfo;
                }
            }

            return tileInfo;
        }
    }
}
