using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Defines the different types of objects that can be jumped to using Ctrl+Arrow keys.
    /// </summary>
    public enum JumpMode
    {
        Terrain,    // Jump by terrain type (original behavior)
        Buildings,  // Jump to buildings (walls, doors, etc.)
        Geysers,           // Jump to steam geysers
        HarvestableTrees,  // Jump to harvestable trees
        MinableTiles       // Jump to mineable resources (ore, stone chunks)
    }

    /// <summary>
    /// Maintains the state of map navigation for accessibility features.
    /// Tracks the current cursor position as the user navigates the map with arrow keys.
    /// </summary>
    public static class MapNavigationState
    {
        private static IntVec3 currentCursorPosition = IntVec3.Invalid;
        private static string lastAnnouncedInfo = "";
        private static bool isInitialized = false;
        private static bool suppressMapNavigation = false;
        private static JumpMode currentJumpMode = JumpMode.Terrain;

        /// <summary>
        /// Gets or sets the current cursor position on the map.
        /// </summary>
        public static IntVec3 CurrentCursorPosition
        {
            get => currentCursorPosition;
            set => currentCursorPosition = value;
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
        /// </summary>
        public static bool IsInitialized
        {
            get => isInitialized;
            set => isInitialized = value;
        }

        /// <summary>
        /// Gets or sets whether map navigation should be suppressed (e.g., when menus are open).
        /// When true, arrow keys will not move the map cursor.
        /// Automatically returns true if trade menu, trade confirmation, gizmo navigation, or windowless dialog is active.
        /// </summary>
        public static bool SuppressMapNavigation
        {
            get
            {
                // Suppress if trade menu is active
                if (TradeNavigationState.IsActive)
                    return true;

                // Suppress if trade confirmation dialog is active
                if (TradeConfirmationState.IsActive)
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
        /// Gets the current jump mode (terrain, buildings, geysers, harvestable trees, or mineable tiles).
        /// </summary>
        public static JumpMode CurrentJumpMode => currentJumpMode;

        /// <summary>
        /// Cycles to the next jump mode and announces it.
        /// </summary>
        public static void CycleJumpModeForward()
        {
            currentJumpMode = (JumpMode)(((int)currentJumpMode + 1) % 5);
            AnnounceJumpMode();
        }

        /// <summary>
        /// Cycles to the previous jump mode and announces it.
        /// </summary>
        public static void CycleJumpModeBackward()
        {
            currentJumpMode = (JumpMode)(((int)currentJumpMode + 4) % 5);
            AnnounceJumpMode();
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
            else
            {
                modeText = "Jump mode: Unknown";
            }
            TolkHelper.Speak(modeText);
        }

        /// <summary>
        /// Initializes the cursor position to the center of the current map view or camera position.
        /// </summary>
        public static void Initialize(Map map)
        {
            if (map == null)
            {
                currentCursorPosition = IntVec3.Invalid;
                isInitialized = false;
                return;
            }

            // Start at the camera's current position
            if (Find.CameraDriver != null)
            {
                currentCursorPosition = Find.CameraDriver.MapPosition;
            }
            else
            {
                // Fallback to map center if camera driver not available
                currentCursorPosition = map.Center;
            }

            lastAnnouncedInfo = "";
            isInitialized = true;
        }

        /// <summary>
        /// Moves the cursor position by the given offset, ensuring it stays within map bounds.
        /// Returns true if the position changed.
        /// </summary>
        public static bool MoveCursor(IntVec3 offset, Map map)
        {
            if (map == null || !isInitialized)
                return false;

            IntVec3 newPosition = currentCursorPosition + offset;

            // Clamp to map bounds
            newPosition.x = UnityEngine.Mathf.Clamp(newPosition.x, 0, map.Size.x - 1);
            newPosition.z = UnityEngine.Mathf.Clamp(newPosition.z, 0, map.Size.z - 1);

            if (newPosition != currentCursorPosition)
            {
                currentCursorPosition = newPosition;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Resets the navigation state (useful when changing maps or returning to main menu).
        /// </summary>
        public static void Reset()
        {
            currentCursorPosition = IntVec3.Invalid;
            lastAnnouncedInfo = "";
            isInitialized = false;
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
            TerrainDef currentTerrain = currentCursorPosition.GetTerrain(map);
            if (currentTerrain == null)
                return false;

            IntVec3 searchPosition = currentCursorPosition;

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
            if (searchPosition != currentCursorPosition)
            {
                currentCursorPosition = searchPosition;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Jumps to the next tile with a building (wall, door, etc.) in the specified direction.
        /// Returns true if the position changed.
        /// </summary>
        public static bool JumpToNextBuilding(IntVec3 direction, Map map)
        {
            if (map == null || !isInitialized)
                return false;

            IntVec3 searchPosition = currentCursorPosition;

            // Search in the specified direction until we find a building
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

                // Check if this tile has a building (wall, door, or other structure)
                if (HasRelevantBuilding(searchPosition, map))
                {
                    // Found a building, stop searching
                    break;
                }
            }

            // Update position if we moved
            if (searchPosition != currentCursorPosition)
            {
                currentCursorPosition = searchPosition;
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

            IntVec3 searchPosition = currentCursorPosition;

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
            if (searchPosition != currentCursorPosition)
            {
                currentCursorPosition = searchPosition;
                return true;
            }

            return false;
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

            IntVec3 searchPosition = currentCursorPosition;

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
            if (searchPosition != currentCursorPosition)
            {
                currentCursorPosition = searchPosition;
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

            IntVec3 searchPosition = currentCursorPosition;

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
            if (searchPosition != currentCursorPosition)
            {
                currentCursorPosition = searchPosition;
                return true;
            }

            return false;
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
    }
}
