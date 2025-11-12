using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Maintains the state of world map navigation for accessibility features.
    /// Tracks the current selected tile as the user navigates the world map with arrow keys.
    /// </summary>
    public static class WorldNavigationState
    {
        private static PlanetTile currentSelectedTile = PlanetTile.Invalid;
        private static bool isActive = false;
        private static bool isInitialized = false;
        private static string lastAnnouncedInfo = "";

        /// <summary>
        /// Gets whether world navigation is currently active.
        /// Used by other systems to suppress their input when in world view.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets whether the navigation state has been initialized.
        /// </summary>
        public static bool IsInitialized => isInitialized;

        /// <summary>
        /// Gets or sets the current selected tile on the world map.
        /// </summary>
        public static PlanetTile CurrentSelectedTile
        {
            get => currentSelectedTile;
            set => currentSelectedTile = value;
        }

        /// <summary>
        /// Opens world navigation mode and initializes the state.
        /// Called when entering world view (F8).
        /// </summary>
        public static void Open()
        {
            if (Find.World == null)
            {
                TolkHelper.Speak("World not available", SpeechPriority.High);
                return;
            }

            isActive = true;

            // Initialize to current selection or player's home base
            if (Find.WorldSelector != null && Find.WorldSelector.SelectedTile.Valid)
            {
                currentSelectedTile = Find.WorldSelector.SelectedTile;
            }
            else
            {
                // Default to player's home settlement if available
                Settlement homeSettlement = Find.WorldObjects?.Settlements?.FirstOrDefault(s => s.Faction == Faction.OfPlayer);
                if (homeSettlement != null)
                {
                    currentSelectedTile = homeSettlement.Tile;
                }
                else
                {
                    // Fallback to tile 0 (should always exist)
                    currentSelectedTile = new PlanetTile(0);
                }
            }

            isInitialized = true;

            // Announce initial position
            string initialInfo = WorldInfoHelper.GetTileSummary(currentSelectedTile);
            TolkHelper.Speak(initialInfo);
            lastAnnouncedInfo = initialInfo;

            // Jump camera to selected tile
            if (Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.JumpTo(currentSelectedTile);
            }

            // Sync with game's selection system
            if (Find.WorldSelector != null)
            {
                Find.WorldSelector.SelectedTile = currentSelectedTile;
            }
        }

        /// <summary>
        /// Closes world navigation mode.
        /// Called when returning to map view.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            isInitialized = false;
            currentSelectedTile = PlanetTile.Invalid;
            lastAnnouncedInfo = "";
        }

        /// <summary>
        /// Moves the selection to a neighboring tile in the specified direction.
        /// Uses camera's current orientation to determine which neighbor is "up/down/left/right".
        /// </summary>
        public static bool MoveInDirection(UnityEngine.Vector3 desiredDirection)
        {
            if (!isInitialized || !currentSelectedTile.Valid)
                return false;

            if (Find.WorldGrid == null)
                return false;

            // Get neighbors of current tile
            List<PlanetTile> neighbors = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(currentSelectedTile, neighbors);

            if (neighbors.Count == 0)
                return false;

            // Get current tile's 3D position
            UnityEngine.Vector3 currentPos = Find.WorldGrid.GetTileCenter(currentSelectedTile);

            // Find the neighbor that's closest to the desired direction
            PlanetTile bestNeighbor = PlanetTile.Invalid;
            float bestDot = -2f; // Start with impossibly low value

            foreach (PlanetTile neighbor in neighbors)
            {
                UnityEngine.Vector3 neighborPos = Find.WorldGrid.GetTileCenter(neighbor);
                UnityEngine.Vector3 directionToNeighbor = (neighborPos - currentPos).normalized;

                // Calculate how well this neighbor aligns with desired direction
                float dot = UnityEngine.Vector3.Dot(directionToNeighbor, desiredDirection);

                if (dot > bestDot)
                {
                    bestDot = dot;
                    bestNeighbor = neighbor;
                }
            }

            if (!bestNeighbor.Valid)
                return false;

            // Update selection
            currentSelectedTile = bestNeighbor;

            // Sync with game's selection system
            if (Find.WorldSelector != null)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.SelectedTile = currentSelectedTile;
            }

            // Jump camera to new tile
            if (Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.JumpTo(currentSelectedTile);
            }

            // Announce new tile
            AnnounceTile();

            return true;
        }

        /// <summary>
        /// Announces the current tile information.
        /// </summary>
        public static void AnnounceTile()
        {
            if (!currentSelectedTile.Valid)
                return;

            string tileInfo = WorldInfoHelper.GetTileSummary(currentSelectedTile);
            TolkHelper.Speak(tileInfo);
            lastAnnouncedInfo = tileInfo;
        }

        /// <summary>
        /// Handles arrow key navigation for world map.
        /// Maps arrow keys to camera-relative directions.
        /// </summary>
        public static void HandleArrowKey(UnityEngine.KeyCode key)
        {
            if (!isInitialized || !currentSelectedTile.Valid)
                return;

            if (Find.WorldCameraDriver == null)
                return;

            // Get camera's current rotation to determine "up/down/left/right" in world space
            UnityEngine.Quaternion cameraRotation = Find.WorldCameraDriver.sphereRotation;

            UnityEngine.Vector3 desiredDirection = UnityEngine.Vector3.zero;

            switch (key)
            {
                case UnityEngine.KeyCode.UpArrow:
                    // Move "up" relative to camera (north on screen)
                    desiredDirection = cameraRotation * UnityEngine.Vector3.forward;
                    break;
                case UnityEngine.KeyCode.DownArrow:
                    // Move "down" relative to camera (south on screen)
                    desiredDirection = cameraRotation * UnityEngine.Vector3.back;
                    break;
                case UnityEngine.KeyCode.RightArrow:
                    // Move "right" relative to camera (east on screen)
                    desiredDirection = cameraRotation * UnityEngine.Vector3.right;
                    break;
                case UnityEngine.KeyCode.LeftArrow:
                    // Move "left" relative to camera (west on screen)
                    desiredDirection = cameraRotation * UnityEngine.Vector3.left;
                    break;
            }

            if (desiredDirection != UnityEngine.Vector3.zero)
            {
                MoveInDirection(desiredDirection);
            }
        }

        /// <summary>
        /// Jumps to the player's home settlement.
        /// </summary>
        public static void JumpToHome()
        {
            if (!isInitialized)
                return;

            Settlement homeSettlement = Find.WorldObjects?.Settlements?.FirstOrDefault(s => s.Faction == Faction.OfPlayer);

            if (homeSettlement == null)
            {
                TolkHelper.Speak("No home settlement found", SpeechPriority.Normal);
                return;
            }

            currentSelectedTile = homeSettlement.Tile;

            // Sync with game's selection system
            if (Find.WorldSelector != null)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.Select(homeSettlement);
                Find.WorldSelector.SelectedTile = currentSelectedTile;
            }

            // Jump camera
            if (Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.JumpTo(currentSelectedTile);
            }

            // Announce tile info (includes settlement name)
            AnnounceTile();
        }

        /// <summary>
        /// Jumps to the nearest player caravan.
        /// </summary>
        public static void JumpToNearestCaravan()
        {
            if (!isInitialized || !currentSelectedTile.Valid)
                return;

            List<Caravan> playerCaravans = Find.WorldObjects?.Caravans?
                .Where(c => c.Faction == Faction.OfPlayer)
                .ToList();

            if (playerCaravans == null || playerCaravans.Count == 0)
            {
                TolkHelper.Speak("No player caravans found", SpeechPriority.Normal);
                return;
            }

            // Find nearest caravan
            Caravan nearestCaravan = null;
            float nearestDistance = float.MaxValue;

            foreach (Caravan caravan in playerCaravans)
            {
                if (!caravan.Tile.Valid)
                    continue;

                float distance = Find.WorldGrid.ApproxDistanceInTiles(currentSelectedTile, caravan.Tile);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestCaravan = caravan;
                }
            }

            if (nearestCaravan == null)
            {
                TolkHelper.Speak("No caravans found", SpeechPriority.Normal);
                return;
            }

            currentSelectedTile = nearestCaravan.Tile;

            // Sync with game's selection system
            if (Find.WorldSelector != null)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.Select(nearestCaravan);
                Find.WorldSelector.SelectedTile = currentSelectedTile;
            }

            // Jump camera
            if (Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.JumpTo(currentSelectedTile);
            }

            // Announce tile info
            AnnounceTile();
        }

        /// <summary>
        /// Opens the settlement browser (S key).
        /// </summary>
        public static void OpenSettlementBrowser()
        {
            if (!isInitialized)
                return;

            SettlementBrowserState.Open(currentSelectedTile);
        }

        /// <summary>
        /// Cycles to the next settlement (by distance from current position).
        /// </summary>
        public static void CycleToNextSettlement()
        {
            if (!isInitialized || !currentSelectedTile.Valid)
                return;

            var settlements = WorldInfoHelper.GetSettlementsByDistance(currentSelectedTile);
            if (settlements.Count == 0)
            {
                TolkHelper.Speak("No settlements found", SpeechPriority.Normal);
                return;
            }

            // Find current settlement if we're on one
            Settlement currentSettlement = Find.WorldObjects?.SettlementAt(currentSelectedTile);
            int currentIndex = -1;

            if (currentSettlement != null)
            {
                currentIndex = settlements.IndexOf(currentSettlement);
            }

            // Move to next settlement
            int nextIndex = (currentIndex + 1) % settlements.Count;
            Settlement nextSettlement = settlements[nextIndex];

            // Jump to it
            JumpToSettlement(nextSettlement);
        }

        /// <summary>
        /// Cycles to the previous settlement (by distance from current position).
        /// </summary>
        public static void CycleToPreviousSettlement()
        {
            if (!isInitialized || !currentSelectedTile.Valid)
                return;

            var settlements = WorldInfoHelper.GetSettlementsByDistance(currentSelectedTile);
            if (settlements.Count == 0)
            {
                TolkHelper.Speak("No settlements found", SpeechPriority.Normal);
                return;
            }

            // Find current settlement if we're on one
            Settlement currentSettlement = Find.WorldObjects?.SettlementAt(currentSelectedTile);
            int currentIndex = -1;

            if (currentSettlement != null)
            {
                currentIndex = settlements.IndexOf(currentSettlement);
            }

            // Move to previous settlement
            int prevIndex = currentIndex - 1;
            if (prevIndex < 0)
                prevIndex = settlements.Count - 1;

            Settlement prevSettlement = settlements[prevIndex];

            // Jump to it
            JumpToSettlement(prevSettlement);
        }

        /// <summary>
        /// Jumps to a specific settlement.
        /// </summary>
        private static void JumpToSettlement(Settlement settlement)
        {
            if (settlement == null)
                return;

            currentSelectedTile = settlement.Tile;

            // Sync with game's selection system
            if (Find.WorldSelector != null)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.Select(settlement);
                Find.WorldSelector.SelectedTile = currentSelectedTile;
            }

            // Jump camera
            if (Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.JumpTo(currentSelectedTile);
            }

            // Announce tile info
            AnnounceTile();
        }

        /// <summary>
        /// Reads detailed information about the current tile (I key).
        /// </summary>
        public static void ReadDetailedTileInfo()
        {
            if (!isInitialized || !currentSelectedTile.Valid)
                return;

            string detailedInfo = WorldInfoHelper.GetDetailedTileInfo(currentSelectedTile);
            TolkHelper.Speak(detailedInfo);
        }
    }
}
