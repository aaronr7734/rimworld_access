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
        private static HashSet<Caravan> multiSelectedCaravans = new HashSet<Caravan>();
        private static bool isInPoleTerritory = false;
        private static bool lastWasInPoleTerritory = false;
        private static bool wasOnRoad = false;
        private static bool wasOnRiver = false;

        /// <summary>
        /// Pending start tile set by other systems (e.g., caravan reform) before world view opens.
        /// This is used when the map that should provide the start tile will be removed before Open() is called.
        /// </summary>
        private static PlanetTile pendingStartTile = PlanetTile.Invalid;

        /// <summary>
        /// Latitude threshold for pole territory (degrees from equator).
        /// Beyond this, compass directions become unreliable.
        /// </summary>
        private const float PoleLatitudeThreshold = 75f;

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
        /// Gets whether the cursor is currently in pole territory (|latitude| > 75°).
        /// When in pole territory, compass directions become unreliable due to
        /// the convergence of meridians at the poles.
        /// </summary>
        public static bool IsInPoleTerritory => isInPoleTerritory;

        /// <summary>
        /// Sets a pending start tile that Open() will use instead of trying to find one.
        /// Use this when the current map will be removed before world view opens (e.g., caravan reform).
        /// The pending tile is consumed (cleared) when Open() uses it.
        /// </summary>
        public static PlanetTile PendingStartTile
        {
            get => pendingStartTile;
            set => pendingStartTile = value;
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

            // Initialize to a sensible starting position
            // Priority: 0) Pending tile (set by caravan reform), 1) Current map's world tile, 2) Game's selection, 3) Player caravan, 4) Home settlement
            bool foundCaravan = false;
            bool foundStartingTile = false;

            // Priority 0: Check for pending start tile (set by caravan reform before map was removed)
            if (pendingStartTile.Valid)
            {
                currentSelectedTile = pendingStartTile;
                foundStartingTile = true;
                pendingStartTile = PlanetTile.Invalid; // Consume the pending tile

                // Check if there's a caravan at this tile (the newly reformed caravan)
                var caravanAtTile = Find.WorldObjects?.ObjectsAt(currentSelectedTile)
                    .OfType<RimWorld.Planet.Caravan>()
                    .FirstOrDefault(c => c.Faction == Faction.OfPlayer);
                if (caravanAtTile != null)
                {
                    selectedCaravan = caravanAtTile;
                    foundCaravan = true;
                }
            }

            // First priority: If we were on a map, start at that map's world tile
            // This handles pressing F8 from a temporary encounter map
            if (!foundStartingTile)
            {
                Map currentMap = Find.CurrentMap;
                if (currentMap != null && currentMap.Tile.Valid)
                {
                    currentSelectedTile = currentMap.Tile;
                    foundStartingTile = true;

                    // Check if there's a caravan at this tile
                    var caravanAtTile = Find.WorldObjects?.ObjectsAt(currentSelectedTile)
                        .OfType<RimWorld.Planet.Caravan>()
                        .FirstOrDefault(c => c.Faction == Faction.OfPlayer);
                    if (caravanAtTile != null)
                    {
                        selectedCaravan = caravanAtTile;
                        foundCaravan = true;
                    }
                }
            }

            // Second priority: Check game's current world selection
            if (!foundStartingTile && Find.WorldSelector != null && Find.WorldSelector.SelectedTile.Valid)
            {
                currentSelectedTile = Find.WorldSelector.SelectedTile;
                foundStartingTile = true;
            }

            // Third priority: Look for player caravans
            if (!foundStartingTile)
            {
                var playerCaravans = Find.WorldObjects?.Caravans?
                    .Where(c => c.Faction == Faction.OfPlayer)
                    .ToList();

                if (playerCaravans != null && playerCaravans.Count >= 1)
                {
                    // Use first caravan
                    currentSelectedTile = playerCaravans[0].Tile;
                    selectedCaravan = playerCaravans[0];
                    foundCaravan = true;
                    foundStartingTile = true;
                }
            }

            // Fourth priority: Default to player's home settlement
            if (!foundStartingTile)
            {
                Settlement homeSettlement = Find.WorldObjects?.Settlements?.FirstOrDefault(s => s.Faction == Faction.OfPlayer);
                if (homeSettlement != null)
                {
                    currentSelectedTile = homeSettlement.Tile;
                    foundStartingTile = true;
                }
                else
                {
                    // Fallback to tile 0 (should always exist)
                    currentSelectedTile = new PlanetTile(0);
                }
            }

            // If we found a tile but haven't checked for caravan yet, do so now
            if (!foundCaravan && currentSelectedTile.Valid)
            {
                var caravanAtTile = Find.WorldObjects?.ObjectsAt(currentSelectedTile)
                    .OfType<RimWorld.Planet.Caravan>()
                    .FirstOrDefault(c => c.Faction == Faction.OfPlayer);
                if (caravanAtTile != null)
                {
                    selectedCaravan = caravanAtTile;
                }
            }

            isInitialized = true;

            // Announce menu and initial position
            string initialInfo = WorldInfoHelper.GetTileSummary(currentSelectedTile);

            // Check if route planner is active - announce it so user knows
            if (RoutePlannerState.IsActive)
            {
                int waypointCount = RoutePlannerState.WaypointCount;
                if (waypointCount > 0)
                {
                    TolkHelper.Speak($"World map. Route planner active with {waypointCount} waypoints. {initialInfo}");
                }
                else
                {
                    TolkHelper.Speak($"World map. Route planner active. {initialInfo}");
                }
            }
            else
            {
                TolkHelper.Speak($"World map. {initialInfo}");
            }
            lastAnnouncedInfo = initialInfo;

            // Jump camera to selected tile
            if (Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.JumpTo(currentSelectedTile);
            }

            // Orient camera so north is up (arrow keys match compass directions)
            OrientCameraNorthUp();

            // Check if we're in pole territory
            UpdatePoleStatus();

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
            multiSelectedCaravans.Clear();
            isInPoleTerritory = false;
            lastWasInPoleTerritory = false;
            wasOnRoad = false;
            wasOnRiver = false;
        }

        /// <summary>
        /// Orients the camera so geographic north is screen-up.
        /// This ensures arrow keys match compass directions.
        /// </summary>
        private static void OrientCameraNorthUp()
        {
            if (Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.RotateSoNorthIsUp();
            }
        }

        /// <summary>
        /// Updates pole territory status based on current tile's latitude.
        /// Announces when entering or leaving pole territory.
        /// </summary>
        private static void UpdatePoleStatus()
        {
            if (!currentSelectedTile.Valid || Find.WorldGrid == null)
            {
                isInPoleTerritory = false;
                return;
            }

            // Get latitude (y component of LongLatOf)
            UnityEngine.Vector2 longlat = Find.WorldGrid.LongLatOf(currentSelectedTile);
            float latitude = longlat.y;

            // Check if we're in pole territory
            isInPoleTerritory = UnityEngine.Mathf.Abs(latitude) > PoleLatitudeThreshold;

            // Announce when entering pole territory (only on state change)
            if (isInPoleTerritory && !lastWasInPoleTerritory)
            {
                string pole = latitude > 0 ? "north" : "south";
                TolkHelper.Speak($"Near {pole} pole - using relative directions", SpeechPriority.Normal);
            }
            else if (!isInPoleTerritory && lastWasInPoleTerritory)
            {
                TolkHelper.Speak("Leaving pole territory - using compass directions", SpeechPriority.Normal);
            }

            lastWasInPoleTerritory = isInPoleTerritory;
        }

        /// <summary>
        /// Checks if we've left a road or river and announces it.
        /// </summary>
        private static void CheckOffRoadOrRiver(PlanetTile tile)
        {
            if (!tile.Valid)
            {
                wasOnRoad = false;
                wasOnRiver = false;
                return;
            }

            bool nowOnRoad = false;
            bool nowOnRiver = false;

            if (tile.Tile is SurfaceTile surfaceTile)
            {
                nowOnRoad = surfaceTile.Roads != null && surfaceTile.Roads.Count > 0;
                nowOnRiver = surfaceTile.Rivers != null && surfaceTile.Rivers.Count > 0;
            }

            // Announce leaving road/river (before the tile announcement)
            if (wasOnRoad && !nowOnRoad)
            {
                TolkHelper.Speak("Off road", SpeechPriority.Normal);
            }
            if (wasOnRiver && !nowOnRiver)
            {
                TolkHelper.Speak("Off river", SpeechPriority.Normal);
            }

            wasOnRoad = nowOnRoad;
            wasOnRiver = nowOnRiver;
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

            // Check if we've entered/left pole territory
            UpdatePoleStatus();

            // Check if we've moved off a planned route
            RoutePlannerState.CheckOffRoute(currentSelectedTile);

            // Road/river directions are now accurate, so "off road" announcement not needed
            // CheckOffRoadOrRiver(currentSelectedTile);

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

            // Get fuel cost if transport pod launch targeting is active
            string fuelCostInfo = null;
            if (TransportPodLaunchState.IsActive)
            {
                int originTile = TransportPodLaunchState.GetOriginTile();
                if (originTile >= 0 && Find.WorldGrid != null)
                {
                    float distance = Find.WorldGrid.ApproxDistanceInTiles(originTile, currentSelectedTile);
                    if (distance > 0.1f)
                    {
                        fuelCostInfo = TransportPodLaunchState.GetFuelCostAnnouncement(distance);
                    }
                }
            }

            // Get ability destination info if world ability targeting is active (e.g., Farskip)
            string abilityDestInfo = null;
            if (WorldAbilityTargetingState.IsActive)
            {
                abilityDestInfo = WorldAbilityTargetingState.GetDestinationInfo(currentSelectedTile);
            }

            // Pass fuel cost to GetTileSummary so it's inserted right after the biome
            string tileInfo = WorldInfoHelper.GetTileSummary(currentSelectedTile, includeRouteInfo: true, minimal: false, fuelCostInfo: fuelCostInfo);

            // Append ability destination info if available
            if (!string.IsNullOrEmpty(abilityDestInfo))
            {
                tileInfo += $". {abilityDestInfo}";
            }

            TolkHelper.Speak(tileInfo);
            lastAnnouncedInfo = tileInfo;
        }

        /// <summary>
        /// Handles arrow key navigation for world map.
        /// Maps arrow keys to geographic compass directions (north/south/east/west).
        /// Uses the same calculation as the scanner for consistency.
        /// </summary>
        public static void HandleArrowKey(UnityEngine.KeyCode key)
        {
            if (!isInitialized || !currentSelectedTile.Valid)
                return;

            if (Find.WorldGrid == null)
                return;

            // Calculate geographic north/east using the same method as the scanner
            // This ensures arrow keys match the directions shown in the scanner
            UnityEngine.Vector3 currentPos = Find.WorldGrid.GetTileCenter(currentSelectedTile);
            UnityEngine.Vector3 up = currentPos.normalized; // "Up" is away from planet center
            UnityEngine.Vector3 north = UnityEngine.Vector3.ProjectOnPlane(UnityEngine.Vector3.up, up).normalized;
            UnityEngine.Vector3 east = UnityEngine.Vector3.Cross(up, north).normalized;

            UnityEngine.Vector3 desiredDirection = UnityEngine.Vector3.zero;

            switch (key)
            {
                case UnityEngine.KeyCode.UpArrow:
                    desiredDirection = north;
                    break;
                case UnityEngine.KeyCode.DownArrow:
                    desiredDirection = -north; // South
                    break;
                case UnityEngine.KeyCode.RightArrow:
                    desiredDirection = east;
                    break;
                case UnityEngine.KeyCode.LeftArrow:
                    desiredDirection = -east; // West
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

            // Jump camera and orient north-up
            if (Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.JumpTo(currentSelectedTile);
            }
            OrientCameraNorthUp();

            // Check if we've entered/left pole territory
            UpdatePoleStatus();

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

            // Jump camera and orient north-up
            if (Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.JumpTo(currentSelectedTile);
            }
            OrientCameraNorthUp();

            // Check if we've entered/left pole territory
            UpdatePoleStatus();

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

            // Jump camera and orient north-up
            if (Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.JumpTo(currentSelectedTile);
            }
            OrientCameraNorthUp();

            // Check if we've entered/left pole territory
            UpdatePoleStatus();

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
        /// Announces categorized tile information based on number key pressed.
        /// Key 1: Growing and Food
        /// Key 2: Movement and Terrain
        /// Key 3: Health and Environment
        /// Key 4: Location
        /// Key 5: Tile Features/DLC
        /// </summary>
        public static void AnnounceTileInfoCategory(int category)
        {
            if (!isInitialized || !currentSelectedTile.Valid)
                return;

            string info;
            switch (category)
            {
                case 1:
                    info = WorldInfoHelper.GetTileGrowingInfo(currentSelectedTile);
                    break;
                case 2:
                    info = WorldInfoHelper.GetTileMovementInfo(currentSelectedTile);
                    break;
                case 3:
                    info = WorldInfoHelper.GetTileHealthInfo(currentSelectedTile);
                    break;
                case 4:
                    info = WorldInfoHelper.GetTileLocationInfo(currentSelectedTile);
                    break;
                case 5:
                    info = WorldInfoHelper.GetTileFeaturesInfo(currentSelectedTile);
                    break;
                default:
                    return;
            }

            TolkHelper.Speak(info);
        }

        /// <summary>
        /// Forms a caravan at the currently selected settlement (C key).
        /// Opens the route planner first to set destination, then opens the caravan formation dialog.
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

            // Create the dialog and add to window stack (so it initializes transferables)
            // Set pending flag FIRST so PostOpen doesn't activate CaravanFormationState
            Dialog_FormCaravan dialog = new Dialog_FormCaravan(settlement.Map);
            CaravanFormationState.PendingRoutePlannerOpen = true;
            Find.WindowStack.Add(dialog);

            // Open route planner in caravan formation mode
            // This sets waypoint 1 at the settlement automatically and hides the dialog
            // When user presses Enter, ConfirmRoute() will reopen the dialog with destination set
            RoutePlannerState.OpenForCaravan(dialog);
        }

        /// <summary>
        /// Shows the caravan inspect screen (I key when caravan selected).
        /// </summary>
        public static void ShowCaravanInspect()
        {
            Caravan caravan = GetSelectedCaravan();
            if (caravan == null)
            {
                TolkHelper.Speak("No caravan selected", SpeechPriority.Normal);
                return;
            }

            CaravanInspectState.Open(caravan);
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

            // Validate multi-selection to clean up any destroyed caravans
            ValidateAndCleanupSelection();

            // Sync with game's selection system (but preserve multi-selection)
            if (Find.WorldSelector != null && multiSelectedCaravans.Count == 0)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.Select(selectedCaravan);
            }

            // Announce caravan with status and selection status
            string caravanStatus = WorldInfoHelper.GetCaravanStatus(selectedCaravan);
            string selectionStatus = multiSelectedCaravans.Contains(selectedCaravan) ? ", selected" : "";
            string announcement = $"{selectedCaravan.Label}, {caravanStatus}{selectionStatus}, {nextIndex + 1} of {playerCaravans.Count}";
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

            // Validate multi-selection to clean up any destroyed caravans
            ValidateAndCleanupSelection();

            // Sync with game's selection system (but preserve multi-selection)
            if (Find.WorldSelector != null && multiSelectedCaravans.Count == 0)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.Select(selectedCaravan);
            }

            // Announce caravan with status and selection status
            string caravanStatus = WorldInfoHelper.GetCaravanStatus(selectedCaravan);
            string selectionStatus = multiSelectedCaravans.Contains(selectedCaravan) ? ", selected" : "";
            string announcement = $"{selectedCaravan.Label}, {caravanStatus}{selectionStatus}, {prevIndex + 1} of {playerCaravans.Count}";
            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Gets the currently selected caravan (if any).
        /// Validates that the caravan still exists (handles merge cleanup).
        /// </summary>
        public static Caravan GetSelectedCaravan()
        {
            if (!isInitialized)
                return null;

            // Validate the selected caravan still exists (might have been merged/destroyed)
            if (selectedCaravan != null && (selectedCaravan.Destroyed || !Find.WorldObjects.Caravans.Contains(selectedCaravan)))
            {
                selectedCaravan = null;
            }

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

        /// <summary>
        /// Toggles multi-selection of the currently focused caravan (via Ctrl+Space).
        /// </summary>
        public static void ToggleCaravanSelection()
        {
            if (selectedCaravan == null)
            {
                TolkHelper.Speak("No caravan focused. Use comma or period to cycle through caravans first.");
                return;
            }

            // Validate first to clean up any destroyed caravans (e.g., after a merge)
            ValidateAndCleanupSelection();

            if (multiSelectedCaravans.Contains(selectedCaravan))
            {
                multiSelectedCaravans.Remove(selectedCaravan);
                TolkHelper.Speak($"{selectedCaravan.Label} deselected. {multiSelectedCaravans.Count} caravans selected.");
            }
            else
            {
                multiSelectedCaravans.Add(selectedCaravan);
                TolkHelper.Speak($"{selectedCaravan.Label} selected. {multiSelectedCaravans.Count} caravans selected.");
            }

            // Sync multi-selection with game's WorldSelector
            SyncMultiSelectionWithGame();
        }

        /// <summary>
        /// Syncs our multi-selection with RimWorld's WorldSelector.
        /// </summary>
        private static void SyncMultiSelectionWithGame()
        {
            if (Find.WorldSelector == null)
                return;

            Find.WorldSelector.ClearSelection();
            foreach (var caravan in multiSelectedCaravans)
            {
                if (caravan != null && !caravan.Destroyed)
                {
                    Find.WorldSelector.Select(caravan, playSound: false);
                }
            }
        }

        /// <summary>
        /// Gets whether the specified caravan is multi-selected.
        /// </summary>
        public static bool IsCaravanMultiSelected(Caravan caravan)
        {
            return multiSelectedCaravans.Contains(caravan);
        }

        /// <summary>
        /// Gets all multi-selected caravans.
        /// Does NOT validate automatically - call ValidateAndCleanupSelection() explicitly when needed.
        /// </summary>
        public static IReadOnlyCollection<Caravan> GetMultiSelectedCaravans()
        {
            return multiSelectedCaravans;
        }

        /// <summary>
        /// Validates the multi-selection and removes any destroyed or invalid caravans.
        /// Call this after actions that might destroy caravans (like merge).
        /// </summary>
        public static void ValidateAndCleanupSelection()
        {
            if (multiSelectedCaravans.Count == 0)
                return;

            // Get current valid player caravans
            var validCaravans = Find.WorldObjects?.Caravans?
                .Where(c => c.Faction == Faction.OfPlayer && !c.Destroyed)
                .ToHashSet() ?? new HashSet<Caravan>();

            // Remove any caravans that no longer exist
            multiSelectedCaravans.RemoveWhere(c => c == null || c.Destroyed || !validCaravans.Contains(c));
        }

        /// <summary>
        /// Jumps the cursor to the selected caravan(s) location (Alt+C).
        /// If multiple caravans are selected, they must all be on the same tile.
        /// </summary>
        public static void JumpToSelectedCaravans()
        {
            // Check multi-selection first
            if (multiSelectedCaravans.Count > 0)
            {
                // Check if all selected caravans are on the same tile
                var tiles = multiSelectedCaravans.Select(c => c.Tile).Distinct().ToList();
                if (tiles.Count > 1)
                {
                    TolkHelper.Speak("You can't be in two places at once! Selected caravans are on different tiles.");
                    return;
                }

                // All on same tile - jump there
                PlanetTile targetTile = tiles[0];
                currentSelectedTile = targetTile;

                // Center camera on that tile
                Find.WorldCameraDriver?.JumpTo(Find.WorldGrid.GetTileCenter(targetTile));

                string tileInfo = WorldInfoHelper.GetTileSummary(targetTile);
                TolkHelper.Speak($"Jumped to {multiSelectedCaravans.Count} selected caravans. {tileInfo}");
                return;
            }

            // Fall back to single focused caravan
            if (selectedCaravan != null)
            {
                currentSelectedTile = selectedCaravan.Tile;
                Find.WorldCameraDriver?.JumpTo(Find.WorldGrid.GetTileCenter(selectedCaravan.Tile));

                string tileInfo = WorldInfoHelper.GetTileSummary(selectedCaravan.Tile);
                TolkHelper.Speak($"Jumped to {selectedCaravan.Label}. {tileInfo}");
                return;
            }

            TolkHelper.Speak("No caravan selected. Use comma or period to cycle through caravans first.");
        }

        /// <summary>
        /// Clears all multi-selected caravans.
        /// </summary>
        public static void ClearMultiSelection()
        {
            multiSelectedCaravans.Clear();
            if (Find.WorldSelector != null)
            {
                Find.WorldSelector.ClearSelection();
            }
        }
    }
}
