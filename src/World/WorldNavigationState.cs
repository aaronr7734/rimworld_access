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
        private static Caravan selectedCaravan = null;

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

            // Announce menu and initial position
            string initialInfo = WorldInfoHelper.GetTileSummary(currentSelectedTile);
            TolkHelper.Speak($"World map. {initialInfo}");
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
            selectedCaravan = null;
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
        /// Opens the quest locations browser (Q key).
        /// </summary>
        public static void OpenQuestLocationsBrowser()
        {
            if (!isInitialized)
                return;

            QuestLocationsBrowserState.Open(currentSelectedTile);
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

        /// <summary>
        /// Forms a caravan at the currently selected settlement (C key).
        /// </summary>
        public static void FormCaravanAtSelectedSettlement()
        {
            if (!isInitialized || !currentSelectedTile.Valid)
            {
                TolkHelper.Speak("No tile selected", SpeechPriority.Normal);
                return;
            }

            Settlement settlement = Find.WorldObjects?.SettlementAt(currentSelectedTile);

            if (settlement == null)
            {
                TolkHelper.Speak("No settlement at current tile", SpeechPriority.Normal);
                return;
            }

            if (settlement.Faction != Faction.OfPlayer)
            {
                TolkHelper.Speak("Can only form caravans from player settlements", SpeechPriority.Normal);
                return;
            }

            if (!settlement.HasMap)
            {
                TolkHelper.Speak("Settlement has no map", SpeechPriority.Normal);
                return;
            }

            // Open caravan formation dialog
            Dialog_FormCaravan dialog = new Dialog_FormCaravan(settlement.Map);
            Find.WindowStack.Add(dialog);

            TolkHelper.Speak("Opening caravan formation dialog");
        }

        /// <summary>
        /// Shows detailed stats for the currently selected caravan (I key when caravan selected).
        /// </summary>
        public static void ShowCaravanStats()
        {
            Caravan caravan = GetSelectedCaravan();
            if (caravan == null)
            {
                TolkHelper.Speak("No caravan selected", SpeechPriority.Normal);
                return;
            }

            CaravanStatsState.Open(caravan);
        }

        /// <summary>
        /// Opens the order menu for the currently selected caravan (] key).
        /// Uses the cursor tile as the target location for orders.
        /// </summary>
        public static void GiveCaravanOrders()
        {
            if (!isInitialized || !currentSelectedTile.Valid)
            {
                TolkHelper.Speak("No tile selected", SpeechPriority.Normal);
                return;
            }

            Caravan caravan = GetSelectedCaravan();
            if (caravan == null)
            {
                TolkHelper.Speak("No caravan selected", SpeechPriority.Normal);
                return;
            }

            List<FloatMenuOption> orders = new List<FloatMenuOption>();

            // Add basic "Travel here" option if not at current location
            if (currentSelectedTile != caravan.Tile)
            {
                FloatMenuOption travelOption = new FloatMenuOption(
                    $"Travel to this tile",
                    delegate
                    {
                        if (caravan.pather != null)
                        {
                            caravan.pather.StartPath(currentSelectedTile, null, repathImmediately: false, resetPauseStatus: true);
                            TolkHelper.Speak($"{caravan.Label} traveling to destination");
                        }
                    },
                    MenuOptionPriority.Default,
                    null,
                    null,
                    0f,
                    null,
                    null
                );
                orders.Add(travelOption);
            }

            // Get available orders from world objects at this tile
            List<FloatMenuOption> worldObjectOrders = FloatMenuMakerWorld.ChoicesAtFor(currentSelectedTile, caravan);
            if (worldObjectOrders != null && worldObjectOrders.Count > 0)
            {
                orders.AddRange(worldObjectOrders);
            }

            if (orders.Count == 0)
            {
                TolkHelper.Speak("No orders available - already at this location", SpeechPriority.Normal);
                return;
            }

            // Open windowless float menu with caravan orders (includes disabled options)
            WindowlessFloatMenuState.Open(orders, colonistOrders: false);
            TolkHelper.Speak($"{caravan.Label} orders: {orders.Count} options available");
        }

        /// <summary>
        /// Cycles to the next player caravan (for order-giving).
        /// Does not move the map cursor.
        /// </summary>
        public static void CycleToNextCaravan()
        {
            if (!isInitialized)
                return;

            List<Caravan> playerCaravans = Find.WorldObjects?.Caravans?
                .Where(c => c.Faction == Faction.OfPlayer)
                .OrderBy(c => c.Label)
                .ToList();

            if (playerCaravans == null || playerCaravans.Count == 0)
            {
                TolkHelper.Speak("No player caravans found", SpeechPriority.Normal);
                selectedCaravan = null;
                return;
            }

            // Find current index
            int currentIndex = -1;
            if (selectedCaravan != null)
            {
                currentIndex = playerCaravans.IndexOf(selectedCaravan);
            }

            // Move to next caravan
            int nextIndex = (currentIndex + 1) % playerCaravans.Count;
            selectedCaravan = playerCaravans[nextIndex];

            // Sync with game's selection system
            if (Find.WorldSelector != null)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.Select(selectedCaravan);
            }

            // Announce caravan
            string announcement = $"{selectedCaravan.Label}, {nextIndex + 1} of {playerCaravans.Count}";
            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Cycles to the previous player caravan (for order-giving).
        /// Does not move the map cursor.
        /// </summary>
        public static void CycleToPreviousCaravan()
        {
            if (!isInitialized)
                return;

            List<Caravan> playerCaravans = Find.WorldObjects?.Caravans?
                .Where(c => c.Faction == Faction.OfPlayer)
                .OrderBy(c => c.Label)
                .ToList();

            if (playerCaravans == null || playerCaravans.Count == 0)
            {
                TolkHelper.Speak("No player caravans found", SpeechPriority.Normal);
                selectedCaravan = null;
                return;
            }

            // Find current index
            int currentIndex = -1;
            if (selectedCaravan != null)
            {
                currentIndex = playerCaravans.IndexOf(selectedCaravan);
            }

            // Move to previous caravan
            int prevIndex = currentIndex - 1;
            if (prevIndex < 0)
                prevIndex = playerCaravans.Count - 1;

            selectedCaravan = playerCaravans[prevIndex];

            // Sync with game's selection system
            if (Find.WorldSelector != null)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.Select(selectedCaravan);
            }

            // Announce caravan
            string announcement = $"{selectedCaravan.Label}, {prevIndex + 1} of {playerCaravans.Count}";
            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Gets the currently selected caravan (if any).
        /// </summary>
        public static Caravan GetSelectedCaravan()
        {
            if (!isInitialized)
                return null;

            // Return the explicitly selected caravan if set
            if (selectedCaravan != null)
                return selectedCaravan;

            // Otherwise, check if there's a caravan at the current tile
            if (!currentSelectedTile.Valid)
                return null;

            var worldObjects = Find.WorldObjects?.ObjectsAt(currentSelectedTile);
            if (worldObjects == null)
                return null;

            // Find a player-controlled caravan
            foreach (WorldObject obj in worldObjects)
            {
                if (obj is Caravan caravan && caravan.Faction == Faction.OfPlayer)
                {
                    return caravan;
                }
            }

            return null;
        }
    }
}
