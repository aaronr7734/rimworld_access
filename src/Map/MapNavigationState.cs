using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Defines the different types of objects that can be jumped to using Ctrl+Arrow keys.
    /// </summary>
    public enum JumpMode
    {
        Terrain,           // Jump by terrain type (original behavior)
        Buildings,         // Jump to buildings (walls, doors, etc.)
        Geysers,           // Jump to steam geysers
        HarvestableTrees,  // Jump to harvestable trees
        MinableTiles,      // Jump to mineable resources (ore, stone chunks)
        PresetDistance,    // Jump a fixed number of tiles
        AdjacentToWall     // Jump to tile adjacent to (before) a wall
    }

    /// <summary>
    /// Defines whether the camera follows the cursor or a selected pawn.
    /// These modes are mutually exclusive.
    /// </summary>
    public enum CameraFollowMode
    {
        Cursor,  // Camera stays at cursor position, blocks RimWorld's pawn following
        Pawn     // Camera follows selected pawn (enabled by comma/period cycling)
    }

    /// <summary>
    /// Maintains the state of map navigation for accessibility features.
    /// Tracks the current cursor position as the user navigates the map with arrow keys.
    /// Stores per-map cursor positions so switching between maps preserves cursor location.
    /// </summary>
    public static class MapNavigationState
    {
        // Per-map cursor positions, keyed by map.uniqueID
        private static Dictionary<int, IntVec3> cursorPositionsByMap = new Dictionary<int, IntVec3>();

        private static string lastAnnouncedInfo = "";
        private static bool isInitialized = false;
        private static int initializedForMapId = -1; // Track which map we're initialized for
        private static bool suppressMapNavigation = false;
        private static JumpMode currentJumpMode = JumpMode.PresetDistance;
        private static int presetJumpDistance = 5;
        private static CameraFollowMode cameraFollowMode = CameraFollowMode.Cursor;

        // Pending restore position - used when returning from dialogs like trade
        // If set, Initialize() will use this position instead of the camera position
        private static IntVec3 pendingRestorePosition = IntVec3.Invalid;

        // Map tracking - detect when maps are added/removed
        private static HashSet<int> knownMapIds = new HashSet<int>();
        private static bool hasAnnouncedMultiMapHint = false;

        /// <summary>
        /// Gets or sets the current cursor position on the current map.
        /// Automatically stores/retrieves per-map positions using map.uniqueID.
        /// </summary>
        public static IntVec3 CurrentCursorPosition
        {
            get
            {
                if (Find.CurrentMap == null)
                    return IntVec3.Invalid;

                if (cursorPositionsByMap.TryGetValue(Find.CurrentMap.uniqueID, out IntVec3 pos))
                    return pos;

                return IntVec3.Invalid;
            }
            set
            {
                if (Find.CurrentMap != null)
                    cursorPositionsByMap[Find.CurrentMap.uniqueID] = value;
            }
        }

        /// <summary>
        /// Gets the stored cursor position for a specific map, or (0,0,0) if none stored.
        /// </summary>
        public static IntVec3 GetCursorPositionForMap(Map map)
        {
            if (map == null)
                return IntVec3.Invalid;

            if (cursorPositionsByMap.TryGetValue(map.uniqueID, out IntVec3 pos))
                return pos;

            // Default to (0,0,0) for maps with no stored position
            return new IntVec3(0, 0, 0);
        }

        /// <summary>
        /// Restores cursor to last known position for current map (or 0,0 if unknown).
        /// Also moves camera to that position.
        /// </summary>
        public static void RestoreCursorForCurrentMap()
        {
            if (Find.CurrentMap == null)
                return;

            IntVec3 restorePosition = GetCursorPositionForMap(Find.CurrentMap);

            // Validate position is in bounds
            if (!restorePosition.InBounds(Find.CurrentMap))
                restorePosition = new IntVec3(0, 0, 0);

            CurrentCursorPosition = restorePosition;
            Find.CameraDriver?.JumpToCurrentMapLoc(restorePosition);
            isInitialized = true;
        }

        /// <summary>
        /// Gets or sets the last announced tile information to avoid repetition.
        /// </summary>
        public static string LastAnnouncedInfo
        {
            get => lastAnnouncedInfo;
            set => lastAnnouncedInfo = value;
        }

        /// <summary>
        /// Indicates whether the navigation state has been initialized for the current map.
        /// Returns false if the current map is different from the map we initialized for.
        /// </summary>
        public static bool IsInitialized
        {
            get
            {
                // Not initialized at all
                if (!isInitialized)
                    return false;

                // Check if we're on the same map we initialized for
                if (Find.CurrentMap == null)
                    return false;

                // If the current map is different, we need to re-initialize
                if (Find.CurrentMap.uniqueID != initializedForMapId)
                    return false;

                return true;
            }
            set => isInitialized = value;
        }

        /// <summary>
        /// Gets or sets whether map navigation should be suppressed (e.g., when menus are open).
        /// When true, arrow keys will not move the map cursor.
        /// Automatically returns true if trade menu, gizmo navigation, or windowless dialog is active.
        /// </summary>
        public static bool SuppressMapNavigation
        {
            get
            {
                // Suppress if trade menu is active
                if (TradeNavigationState.IsActive)
                    return true;

                // Suppress if gizmo navigation is active
                if (GizmoNavigationState.IsActive)
                    return true;

                // Suppress if windowless dialog is active
                if (WindowlessDialogState.IsActive)
                    return true;

                return suppressMapNavigation;
            }
            set => suppressMapNavigation = value;
        }

        /// <summary>
        /// Gets the current jump mode (terrain, buildings, geysers, harvestable trees, mineable tiles, or preset distance).
        /// </summary>
        public static JumpMode CurrentJumpMode => currentJumpMode;

        /// <summary>
        /// Gets the current preset jump distance in tiles.
        /// </summary>
        public static int PresetJumpDistance => presetJumpDistance;

        /// <summary>
        /// Gets or sets the current camera follow mode.
        /// Cursor mode: camera stays at cursor, blocks pawn following.
        /// Pawn mode: camera follows selected pawn (after comma/period cycling).
        /// </summary>
        public static CameraFollowMode CurrentCameraMode
        {
            get => cameraFollowMode;
            set => cameraFollowMode = value;
        }

        /// <summary>
        /// Cycles to the next jump mode and announces it.
        /// </summary>
        public static void CycleJumpModeForward()
        {
            int modeCount = Enum.GetValues(typeof(JumpMode)).Length;
            currentJumpMode = (JumpMode)(((int)currentJumpMode + 1) % modeCount);
            AnnounceJumpMode();
        }

        /// <summary>
        /// Cycles to the previous jump mode and announces it.
        /// </summary>
        public static void CycleJumpModeBackward()
        {
            int modeCount = Enum.GetValues(typeof(JumpMode)).Length;
            currentJumpMode = (JumpMode)(((int)currentJumpMode + modeCount - 1) % modeCount);
            AnnounceJumpMode();
        }

        /// <summary>
        /// Increases the preset jump distance and announces the new value.
        /// </summary>
        public static void IncreasePresetDistance(int amount = 1)
        {
            presetJumpDistance += amount;
            TolkHelper.Speak($"Jump distance: {presetJumpDistance}");
        }

        /// <summary>
        /// Decreases the preset jump distance (minimum 1) and announces the new value.
        /// </summary>
        public static void DecreasePresetDistance(int amount = 1)
        {
            if (presetJumpDistance > 1)
            {
                presetJumpDistance = System.Math.Max(1, presetJumpDistance - amount);
                TolkHelper.Speak($"Jump distance: {presetJumpDistance}");
            }
            else
            {
                TolkHelper.Speak("Minimum distance");
            }
        }

        /// <summary>
        /// Announces the current jump mode to the user via clipboard.
        /// </summary>
        private static void AnnounceJumpMode()
        {
            string modeText;
            if (currentJumpMode == JumpMode.Terrain)
            {
                modeText = "Jump mode: Terrain";
            }
            else if (currentJumpMode == JumpMode.Buildings)
            {
                modeText = "Jump mode: Buildings";
            }
            else if (currentJumpMode == JumpMode.Geysers)
            {
                modeText = "Jump mode: Geysers";
            }
            else if (currentJumpMode == JumpMode.HarvestableTrees)
            {
                modeText = "Jump mode: Harvestable Trees";
            }
            else if (currentJumpMode == JumpMode.MinableTiles)
            {
                modeText = "Jump mode: Mineable Tiles";
            }
            else if (currentJumpMode == JumpMode.PresetDistance)
            {
                modeText = $"Jump mode: Preset distance, {presetJumpDistance} tiles";
            }
            else if (currentJumpMode == JumpMode.AdjacentToWall)
            {
                modeText = "Jump mode: Adjacent to Wall";
            }
            else
            {
                modeText = "Jump mode: Unknown";
            }
            TolkHelper.Speak(modeText);
        }

        /// <summary>
        /// Sets a position to restore when the map navigation next initializes.
        /// Used when returning from dialogs (like trade) to preserve cursor position.
        /// </summary>
        public static void SetPendingRestorePosition(IntVec3 position)
        {
            pendingRestorePosition = position;
        }

        /// <summary>
        /// Initializes the cursor position for a map.
        /// Priority: pending restore position > stored per-map position > camera position > (0,0,0)
        /// </summary>
        public static void Initialize(Map map)
        {
            if (map == null)
            {
                isInitialized = false;
                return;
            }

            IntVec3 newPosition;

            // Check for pending restore position first (from returning from trade, etc.)
            if (pendingRestorePosition.IsValid && pendingRestorePosition.InBounds(map))
            {
                newPosition = pendingRestorePosition;
                pendingRestorePosition = IntVec3.Invalid;
            }
            // Check if we have a stored position for this map
            else if (cursorPositionsByMap.TryGetValue(map.uniqueID, out IntVec3 storedPos) && storedPos.InBounds(map))
            {
                newPosition = storedPos;
            }
            // Start at the camera's current position
            else if (Find.CameraDriver != null)
            {
                newPosition = Find.CameraDriver.MapPosition;
            }
            else
            {
                // Fallback to (0,0,0) if nothing else available
                newPosition = new IntVec3(0, 0, 0);
            }

            // Store the position for this map
            cursorPositionsByMap[map.uniqueID] = newPosition;

            lastAnnouncedInfo = "";
            isInitialized = true;
            initializedForMapId = map.uniqueID;
        }

        /// <summary>
        /// Moves the cursor position by the given offset, ensuring it stays within map bounds.
        /// Returns true if the position changed.
        /// </summary>
        public static bool MoveCursor(IntVec3 offset, Map map)
        {
            if (map == null || !isInitialized)
                return false;

            IntVec3 newPosition = CurrentCursorPosition + offset;

            // Clamp to map bounds
            newPosition.x = UnityEngine.Mathf.Clamp(newPosition.x, 0, map.Size.x - 1);
            newPosition.z = UnityEngine.Mathf.Clamp(newPosition.z, 0, map.Size.z - 1);

            if (newPosition != CurrentCursorPosition)
            {
                CurrentCursorPosition = newPosition;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Resets all navigation state (used when returning to main menu).
        /// Clears all stored per-map cursor positions.
        /// </summary>
        public static void Reset()
        {
            cursorPositionsByMap.Clear();
            lastAnnouncedInfo = "";
            isInitialized = false;
            initializedForMapId = -1;
            knownMapIds.Clear();
            hasAnnouncedMultiMapHint = false;
            cameraFollowMode = CameraFollowMode.Cursor;

            // Invalidate scanner since map data is no longer valid
            ScannerState.Invalidate();
        }

        /// <summary>
        /// Checks for map additions/removals and announces changes to the user.
        /// Should be called periodically (e.g., from MapNavigationPatch).
        /// </summary>
        public static void CheckForMapChanges()
        {
            if (Find.Maps == null)
                return;

            // Build set of current map IDs
            var currentMapIds = new HashSet<int>();
            foreach (var map in Find.Maps)
            {
                currentMapIds.Add(map.uniqueID);
            }

            // Check for new maps
            foreach (int mapId in currentMapIds)
            {
                if (!knownMapIds.Contains(mapId))
                {
                    // New map detected
                    Map newMap = Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);
                    string mapName = GetMapDisplayName(newMap);

                    int totalMaps = Find.Maps.Count;
                    if (totalMaps == 2 && !hasAnnouncedMultiMapHint)
                    {
                        // First time having multiple maps - give the hint
                        TolkHelper.Speak($"New map: {mapName}. You now have {totalMaps} maps. Use Shift+Period and Shift+Comma to switch between them.");
                        hasAnnouncedMultiMapHint = true;
                    }
                    else if (totalMaps > 1)
                    {
                        TolkHelper.Speak($"New map: {mapName}. {totalMaps} maps total.");
                    }
                    // Don't announce when going from 0 to 1 map (game start)
                }
            }

            // Check for removed maps
            foreach (int mapId in knownMapIds)
            {
                if (!currentMapIds.Contains(mapId))
                {
                    // Map was removed - we don't have access to the map object anymore
                    // so we can't get its name, but we can announce the removal
                    int remainingMaps = Find.Maps.Count;
                    if (remainingMaps == 1)
                    {
                        TolkHelper.Speak("Map closed. One map remaining.");
                        hasAnnouncedMultiMapHint = false; // Reset hint for next time
                    }
                    else if (remainingMaps > 1)
                    {
                        TolkHelper.Speak($"Map closed. {remainingMaps} maps remaining.");
                    }

                    // Clean up cursor position for the removed map
                    cursorPositionsByMap.Remove(mapId);
                }
            }

            // Update known maps
            knownMapIds = currentMapIds;
        }

        /// <summary>
        /// Gets a display name for a map.
        /// </summary>
        private static string GetMapDisplayName(Map map)
        {
            if (map == null)
                return "Unknown";

            // Try to get a meaningful name from the map parent
            if (map.Parent != null)
            {
                // For settlements, use the label
                if (!string.IsNullOrEmpty(map.Parent.Label))
                    return map.Parent.Label;

                // For other map parents, use the def label
                if (map.Parent.def != null && !string.IsNullOrEmpty(map.Parent.def.label))
                    return map.Parent.def.label;
            }

            // Fallback: use "Map" with the unique ID
            return $"Map {map.uniqueID}";
        }

        /// <summary>
        /// Jumps to the next tile with a different terrain type in the specified direction.
        /// Returns true if the position changed.
        /// </summary>
        public static bool JumpToNextTerrainType(IntVec3 direction, Map map)
        {
            if (map == null || !isInitialized)
                return false;

            // Get the current terrain at the cursor position
            TerrainDef currentTerrain = CurrentCursorPosition.GetTerrain(map);
            if (currentTerrain == null)
                return false;

            IntVec3 searchPosition = CurrentCursorPosition;

            // Search in the specified direction until we find a different terrain type
            // Limit search to prevent infinite loops
            int maxSteps = UnityEngine.Mathf.Max(map.Size.x, map.Size.z);

            for (int step = 0; step < maxSteps; step++)
            {
                // Move one step in the direction
                searchPosition += direction;

                // Check if we're still within map bounds
                if (!searchPosition.InBounds(map))
                {
                    // Hit map boundary, clamp to edge and stop
                    searchPosition.x = UnityEngine.Mathf.Clamp(searchPosition.x, 0, map.Size.x - 1);
                    searchPosition.z = UnityEngine.Mathf.Clamp(searchPosition.z, 0, map.Size.z - 1);
                    break;
                }

                // Check if this tile has a different terrain type
                TerrainDef searchTerrain = searchPosition.GetTerrain(map);
                if (searchTerrain != null && searchTerrain != currentTerrain)
                {
                    // Found a different terrain type, stop searching
                    break;
                }
            }

            // Update position if we moved
            if (searchPosition != CurrentCursorPosition)
            {
                CurrentCursorPosition = searchPosition;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Jumps to first non-matching tile in the specified direction.
        /// Barriers = buildings + mineables. Skips matching tiles, stops on first different type.
        /// Returns true if the position changed.
        /// </summary>
        public static bool JumpToNextBuilding(IntVec3 direction, Map map)
        {
            if (map == null || !isInitialized)
                return false;

            bool startType = HasBarrier(CurrentCursorPosition, map);
            IntVec3 searchPosition = CurrentCursorPosition;

            int maxSteps = UnityEngine.Mathf.Max(map.Size.x, map.Size.z);

            for (int step = 0; step < maxSteps; step++)
            {
                searchPosition += direction;

                if (!searchPosition.InBounds(map))
                {
                    searchPosition.x = UnityEngine.Mathf.Clamp(searchPosition.x, 0, map.Size.x - 1);
                    searchPosition.z = UnityEngine.Mathf.Clamp(searchPosition.z, 0, map.Size.z - 1);
                    break;
                }

                if (HasBarrier(searchPosition, map) != startType)
                {
                    // Found different type, stop here
                    break;
                }
            }

            // Update position if we moved
            if (searchPosition != CurrentCursorPosition)
            {
                CurrentCursorPosition = searchPosition;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Jumps to the next steam geyser in the specified direction.
        /// Returns true if the position changed.
        /// </summary>
        public static bool JumpToNextGeyser(IntVec3 direction, Map map)
        {
            if (map == null || !isInitialized)
                return false;

            IntVec3 searchPosition = CurrentCursorPosition;

            // Search in the specified direction until we find a steam geyser
            // Limit search to prevent infinite loops
            int maxSteps = UnityEngine.Mathf.Max(map.Size.x, map.Size.z);

            for (int step = 0; step < maxSteps; step++)
            {
                // Move one step in the direction
                searchPosition += direction;

                // Check if we're still within map bounds
                if (!searchPosition.InBounds(map))
                {
                    // Hit map boundary, clamp to edge and stop
                    searchPosition.x = UnityEngine.Mathf.Clamp(searchPosition.x, 0, map.Size.x - 1);
                    searchPosition.z = UnityEngine.Mathf.Clamp(searchPosition.z, 0, map.Size.z - 1);
                    break;
                }

                // Check if this tile has a steam geyser
                if (HasSteamGeyser(searchPosition, map))
                {
                    // Found a geyser, stop searching
                    break;
                }
            }

            // Update position if we moved
            if (searchPosition != CurrentCursorPosition)
            {
                CurrentCursorPosition = searchPosition;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if a tile has a barrier (building or mineable).
        /// Used for barrier-skipping navigation.
        /// </summary>
        private static bool HasBarrier(IntVec3 position, Map map)
        {
            return HasRelevantBuilding(position, map) || HasMineableTiles(position, map);
        }

        /// <summary>
        /// Checks if a tile has a relevant building (walls, doors, or other edifices).
        /// </summary>
        private static bool HasRelevantBuilding(IntVec3 position, Map map)
        {
            var things = position.GetThingList(map);
            foreach (var thing in things)
            {
                // Check for buildings that are edifices (walls, doors, etc.)
                if (thing is Building building)
                {
                    // Include doors explicitly
                    if (building is Building_Door)
                        return true;

                    // Include walls and other structures that hold roofs or have high fill percent
                    if (building.def.building != null && building.def.building.isEdifice)
                    {
                        // Walls typically have holdsRoof or high fillPercent
                        if (building.def.holdsRoof || building.def.fillPercent >= 0.5f)
                            return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Checks if a tile has a steam geyser.
        /// </summary>
        private static bool HasSteamGeyser(IntVec3 position, Map map)
        {
            var things = position.GetThingList(map);
            foreach (var thing in things)
            {
                if (thing is Building_SteamGeyser)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Jumps to the next harvestable tree in the specified direction.
        /// Returns true if the position changed.
        /// </summary>
        public static bool JumpToNextHarvestableTrees(IntVec3 direction, Map map)
        {
            if (map == null || !isInitialized)
                return false;

            IntVec3 searchPosition = CurrentCursorPosition;

            // Search in the specified direction until we find a harvestable tree
            // Limit search to prevent infinite loops
            int maxSteps = UnityEngine.Mathf.Max(map.Size.x, map.Size.z);

            for (int step = 0; step < maxSteps; step++)
            {
                // Move one step in the direction
                searchPosition += direction;

                // Check if we're still within map bounds
                if (!searchPosition.InBounds(map))
                {
                    // Hit map boundary, clamp to edge and stop
                    searchPosition.x = UnityEngine.Mathf.Clamp(searchPosition.x, 0, map.Size.x - 1);
                    searchPosition.z = UnityEngine.Mathf.Clamp(searchPosition.z, 0, map.Size.z - 1);
                    break;
                }

                // Check if this tile has a harvestable tree
                if (HasHarvestableTrees(searchPosition, map))
                {
                    // Found a harvestable tree, stop searching
                    break;
                }
            }

            // Update position if we moved
            if (searchPosition != CurrentCursorPosition)
            {
                CurrentCursorPosition = searchPosition;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Jumps to the next mineable tile in the specified direction.
        /// Returns true if the position changed.
        /// </summary>
        public static bool JumpToNextMinableTiles(IntVec3 direction, Map map)
        {
            if (map == null || !isInitialized)
                return false;

            IntVec3 searchPosition = CurrentCursorPosition;

            // Search in the specified direction until we find a mineable tile
            // Limit search to prevent infinite loops
            int maxSteps = UnityEngine.Mathf.Max(map.Size.x, map.Size.z);

            for (int step = 0; step < maxSteps; step++)
            {
                // Move one step in the direction
                searchPosition += direction;

                // Check if we're still within map bounds
                if (!searchPosition.InBounds(map))
                {
                    // Hit map boundary, clamp to edge and stop
                    searchPosition.x = UnityEngine.Mathf.Clamp(searchPosition.x, 0, map.Size.x - 1);
                    searchPosition.z = UnityEngine.Mathf.Clamp(searchPosition.z, 0, map.Size.z - 1);
                    break;
                }

                // Check if this tile has mineable resources
                if (HasMineableTiles(searchPosition, map))
                {
                    // Found a mineable tile, stop searching
                    break;
                }
            }

            // Update position if we moved
            if (searchPosition != CurrentCursorPosition)
            {
                CurrentCursorPosition = searchPosition;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Jumps a preset number of tiles in the specified direction.
        /// Returns true if the position changed.
        /// </summary>
        public static bool JumpPresetDistance(IntVec3 direction, Map map)
        {
            if (map == null || !isInitialized)
                return false;

            IntVec3 newPosition = CurrentCursorPosition + (direction * presetJumpDistance);

            // Clamp to map bounds
            newPosition.x = UnityEngine.Mathf.Clamp(newPosition.x, 0, map.Size.x - 1);
            newPosition.z = UnityEngine.Mathf.Clamp(newPosition.z, 0, map.Size.z - 1);

            if (newPosition == CurrentCursorPosition)
            {
                TolkHelper.Speak("Map boundary");
                return false;
            }

            CurrentCursorPosition = newPosition;
            return true;
        }

        /// <summary>
        /// Checks if a tile has harvestable trees.
        /// A tree is considered harvestable if it's a plant, is a tree type,
        /// is harvestable now, and is not a stump.
        /// </summary>
        private static bool HasHarvestableTrees(IntVec3 position, Map map)
        {
            var things = position.GetThingList(map);
            foreach (var thing in things)
            {
                if (thing is Plant plant)
                {
                    // Check if it's a tree that's ready for harvest
                    if (plant.def.plant != null &&
                        plant.def.plant.IsTree &&
                        plant.HarvestableNow &&
                        !plant.def.plant.isStump)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Checks if a tile has mineable resources (ore, stone chunks, etc.).
        /// </summary>
        private static bool HasMineableTiles(IntVec3 position, Map map)
        {
            var things = position.GetThingList(map);
            foreach (var thing in things)
            {
                // Check if the thing is marked as mineable
                if (thing.def.mineable)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks if a tile has a blocking structure (wall, blueprint, frame, or mountain).
        /// Used for the AdjacentToWall jump mode.
        /// </summary>
        private static bool IsBlockingStructure(IntVec3 position, Map map)
        {
            var things = position.GetThingList(map);
            foreach (var thing in things)
            {
                // Check mineable rock (mountains)
                if (thing.def.mineable)
                    return true;

                // Check completed buildings
                if (thing is Building)
                {
                    if (thing.def.passability == Traversability.Impassable)
                        return true;
                    if (thing.def.Fillage == FillCategory.Full)
                        return true;
                    if (thing.def.building?.isWall == true)
                        return true;
                }

                // Check blueprints and frames
                if (thing.def.IsBlueprint || thing.def.IsFrame)
                {
                    if (thing.def.entityDefToBuild is ThingDef builtDef)
                    {
                        if (builtDef.passability == Traversability.Impassable)
                            return true;
                        if (builtDef.Fillage == FillCategory.Full)
                            return true;
                        if (builtDef.building?.isWall == true)
                            return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Jumps to the tile immediately before a wall or blocking structure in the specified direction.
        /// Returns true if the position changed.
        /// </summary>
        public static bool JumpToAdjacentToWall(IntVec3 direction, Map map)
        {
            if (map == null || !isInitialized)
                return false;

            // Check if already adjacent to a wall in that direction
            IntVec3 nextTile = CurrentCursorPosition + direction;
            if (nextTile.InBounds(map) && IsBlockingStructure(nextTile, map))
            {
                TolkHelper.Speak("Already adjacent to wall");
                return false;  // Stay in place
            }

            IntVec3 searchPosition = CurrentCursorPosition;
            IntVec3 lastValidPosition = CurrentCursorPosition;

            int maxSteps = UnityEngine.Mathf.Max(map.Size.x, map.Size.z);

            for (int step = 0; step < maxSteps; step++)
            {
                searchPosition += direction;

                if (!searchPosition.InBounds(map))
                {
                    // Hit map boundary - move to edge
                    if (lastValidPosition != CurrentCursorPosition)
                    {
                        CurrentCursorPosition = lastValidPosition;
                        return true;
                    }
                    TolkHelper.Speak("Map boundary");
                    return false;
                }

                if (IsBlockingStructure(searchPosition, map))
                {
                    // Found a wall - stop at the tile before it
                    if (lastValidPosition != CurrentCursorPosition)
                    {
                        CurrentCursorPosition = lastValidPosition;
                        return true;
                    }
                    // We were already at the last valid position
                    TolkHelper.Speak("Already adjacent to wall");
                    return false;
                }

                lastValidPosition = searchPosition;
            }

            // No wall found in this direction, stay in place
            TolkHelper.Speak("No wall found");
            return false;
        }

        // ===== JUMP / SELECTION ANNOUNCEMENTS =====

        /// <summary>
        /// Announces a jump to a named target (e.g., scanner item, menu row,
        /// quest location). Canonical phrasing: "Jumped to {target}".
        /// Pass null or empty targetLabel to announce "Jumped to target".
        /// </summary>
        public static void SpeakJumpedTo(string targetLabel, SpeechPriority priority = SpeechPriority.Normal)
        {
            string phrase = string.IsNullOrEmpty(targetLabel)
                ? "RimWorldAccess.Map.JumpedToTarget".Translate().ToString()
                : "RimWorldAccess.Map.JumpedTo".Translate(targetLabel).ToString();
            TolkHelper.Speak(phrase, priority);
        }

        /// <summary>
        /// Formats the "Selected, {x}, {z}" cell announcement used when the
        /// user picks a single map tile (architect placement, shape anchors,
        /// area paint start, etc.). Caller is responsible for speaking the
        /// returned string.
        /// </summary>
        public static string FormatSelectedCell(IntVec3 cell)
        {
            return "RimWorldAccess.Map.SelectedCell".Translate(cell.x, cell.z).ToString();
        }
    }
}
