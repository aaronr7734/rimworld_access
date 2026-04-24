using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Context in which world navigation is active.
    /// Controls which features are available.
    /// </summary>
    public enum WorldNavContext
    {
        None,     // Not active
        InGame,   // F8 world map during gameplay
        WorldGen  // Starting site selection during game setup
    }

    /// <summary>
    /// Maintains the state of world map navigation for accessibility features.
    /// Tracks the current selected tile as the user navigates the world map with arrow keys.
    /// Used by both in-game world map (F8) and world generation starting site screen.
    /// </summary>
    public static class WorldNavigationState
    {
        private static PlanetTile currentSelectedTile = PlanetTile.Invalid;
        private static bool isActive = false;
        private static bool isInitialized = false;
        private static Caravan selectedCaravan = null;
        private static HashSet<Caravan> multiSelectedCaravans = new HashSet<Caravan>();
        private static bool isInPoleTerritory = false;
        private static bool lastWasInPoleTerritory = false;
        private static WorldNavContext context = WorldNavContext.None;

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
        /// Gets the current navigation context (InGame, WorldGen, or None).
        /// </summary>
        public static WorldNavContext Context => context;

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
            set
            {
                currentSelectedTile = value;
                // External origin writes end the world scanner's navigation session so the next
                // Page Up/Down re-sorts from the new origin. Scanner-driven jumps (JumpToCurrent)
                // guard with a flag so they do not self-invalidate.
                WorldScannerState.NotifyOriginWritten();
            }
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
        /// Opens world navigation mode in the InGame context.
        /// Called when entering world view (F8) during gameplay.
        /// </summary>
        public static void Open()
        {
            Open(WorldNavContext.InGame);
        }

        /// <summary>
        /// Opens world navigation mode in the specified context.
        /// WorldGen context: uses the game's WorldInterface selection or a provided start tile.
        /// InGame context: uses priority chain (pending tile, current map, game selection, caravan, home).
        /// </summary>
        public static void Open(WorldNavContext navContext, PlanetTile? startTile = null)
        {
            if (Find.World == null)
            {
                TolkHelper.Speak("World not available", SpeechPriority.High);
                return;
            }

            context = navContext;
            isActive = true;

            if (navContext == WorldNavContext.WorldGen)
            {
                // World gen: use provided tile, game's already-chosen tile, WorldInterface, or random
                if (startTile.HasValue && startTile.Value.Valid)
                {
                    currentSelectedTile = startTile.Value;
                }
                else if (Find.GameInitData?.startingTile.Valid == true)
                {
                    // Game's PostOpen() already called ChooseRandomStartingTile() which picks a valid land tile
                    currentSelectedTile = Find.GameInitData.startingTile;
                }
                else if (Find.WorldInterface?.SelectedTile.Valid == true)
                {
                    currentSelectedTile = Find.WorldInterface.SelectedTile;
                }
                else
                {
                    currentSelectedTile = TileFinder.RandomStartingTile();
                }
            }
            else
            {
                // === In-game priority chain (existing logic) ===
                bool foundCaravan = false;
                bool foundStartingTile = false;

                // Priority 0: Check for pending start tile (set by caravan reform before map was removed)
                if (pendingStartTile.Valid)
                {
                    currentSelectedTile = pendingStartTile;
                    foundStartingTile = true;
                    pendingStartTile = PlanetTile.Invalid; // Consume the pending tile

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
                if (!foundStartingTile)
                {
                    Map currentMap = Find.CurrentMap;
                    if (currentMap != null && currentMap.Tile.Valid)
                    {
                        currentSelectedTile = currentMap.Tile;
                        foundStartingTile = true;

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
                        currentSelectedTile = new PlanetTile(0, -1);
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
            }

            isInitialized = true;

            // Sync with game's selection system
            SyncSelectionWithGame();

            // Build announcement with biome description for starting tile
            string initialInfo = WorldInfoHelper.GetTileSummary(currentSelectedTile, includeRouteInfo: navContext == WorldNavContext.InGame);

            // Get biome description for the starting tile
            string biomeDesc = BiomeDescriptionTracker.GetBiomeDescriptionIfNew(currentSelectedTile);
            if (!string.IsNullOrEmpty(biomeDesc))
            {
                initialInfo = AppendSentence(initialInfo, biomeDesc);
            }

            // Check if route planner is active (in-game only) - announce it so user knows
            if (navContext == WorldNavContext.InGame && RoutePlannerState.IsActive)
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


            // Jump camera to selected tile
            if (Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.JumpTo(currentSelectedTile);
            }

            // Orient camera so north is up (arrow keys match compass directions)
            OrientCameraNorthUp();

            // Check if we're in pole territory
            UpdatePoleStatus();
        }

        /// <summary>
        /// Appends a sentence to existing text, ensuring proper period separation.
        /// Avoids double periods when either the existing text ends with a period
        /// or the new sentence starts after one.
        /// </summary>
        private static string AppendSentence(string existing, string sentence)
        {
            if (string.IsNullOrEmpty(sentence)) return existing;
            if (string.IsNullOrEmpty(existing)) return sentence;

            string trimmedExisting = existing.TrimEnd();
            bool existingEndsPeriod = trimmedExisting.EndsWith(".");

            if (existingEndsPeriod)
                return trimmedExisting + " " + sentence;
            else
                return trimmedExisting + ". " + sentence;
        }

        /// <summary>
        /// Syncs the current tile with the game's selection system.
        /// Handles both in-game (WorldSelector) and world gen (WorldInterface + GameInitData).
        /// </summary>
        public static void SyncSelectionWithGame()
        {
            if (Find.WorldSelector != null)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.SelectedTile = currentSelectedTile;
            }

            if (context == WorldNavContext.WorldGen)
            {
                if (Find.WorldInterface != null)
                    Find.WorldInterface.SelectedTile = currentSelectedTile;
                if (Find.GameInitData != null)
                    Find.GameInitData.startingTile = currentSelectedTile;
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
            context = WorldNavContext.None;
            currentSelectedTile = PlanetTile.Invalid;
            selectedCaravan = null;
            multiSelectedCaravans.Clear();
            isInPoleTerritory = false;
            lastWasInPoleTerritory = false;
            BiomeDescriptionTracker.Reset();
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

            // Sync with game's selection system (handles both InGame and WorldGen)
            SyncSelectionWithGame();

            // Jump camera to new tile
            if (Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.JumpTo(currentSelectedTile);
            }

            // Check if we've entered/left pole territory
            UpdatePoleStatus();

            // Check if we've moved off a planned route (in-game only)
            if (context == WorldNavContext.InGame)
            {
                RoutePlannerState.CheckOffRoute(currentSelectedTile);
            }

            // Notify world-gen context of tile change (closes I-menu, etc.)
            if (context == WorldNavContext.WorldGen)
            {
                StartingSiteContext.OnTileChanged();
            }

            // Announce new tile
            AnnounceTile();

            return true;
        }

        /// <summary>
        /// Cycles to the next available planet layer (e.g., Surface ↔ Orbit).
        /// Used for gravship navigation between orbital and surface views.
        /// </summary>
        public static void CyclePlanetLayer()
        {
            var worldGrid = Find.WorldGrid;
            if (worldGrid == null) return;

            var currentLayer = PlanetLayer.Selected;
            if (currentLayer == null) return;

            // Find next valid layer that has a connection from current
            PlanetLayer nextLayer = null;
            foreach (var kvp in worldGrid.PlanetLayers)
            {
                var layer = kvp.Value;
                if (layer == currentLayer) continue;
                if (!currentLayer.HasConnectionFromTo(layer)) continue;

                AcceptanceReport report = layer.CanSelectLayer();
                if (!report.Accepted)
                {
                    TolkHelper.Speak($"Cannot switch to {layer.Def.LabelCap}: {report.Reason}");
                    return;
                }
                nextLayer = layer;
                break;
            }

            if (nextLayer == null)
            {
                TolkHelper.Speak("No other layers available");
                return;
            }

            PlanetLayer.Selected = nextLayer;

            // Find the closest tile on the new layer to maintain position
            var closestTile = nextLayer.GetClosestTile_NewTemp(currentSelectedTile);
            if (closestTile.Valid)
            {
                currentSelectedTile = closestTile;
                SyncSelectionWithGame();
            }
            else
            {
                currentSelectedTile = PlanetTile.Invalid;
            }

            string layerName = nextLayer.Def.LabelCap;
            bool isSpace = nextLayer.Def.isSpace;
            TolkHelper.Speak($"Switched to {layerName} layer.{(isSpace ? " Space layer." : "")}");
            AnnounceTile();
        }

        /// <summary>
        /// Announces the current tile information.
        /// Includes biome descriptions (both contexts) and faction/settle warnings (WorldGen only).
        /// </summary>
        public static void AnnounceTile()
        {
            if (!currentSelectedTile.Valid)
                return;

            // Get fuel cost if transport pod or gravship launch targeting is active (in-game only)
            string fuelCostInfo = null;
            if (context == WorldNavContext.InGame && TransportPodLaunchState.IsActive)
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
            else if (context == WorldNavContext.InGame && GravshipDestinationState.ShouldAnnounceFuelCosts())
            {
                fuelCostInfo = GravshipDestinationState.GetFuelCostAnnouncement(currentSelectedTile);
            }

            // Get ability destination info if world ability targeting is active (in-game only)
            string abilityDestInfo = null;
            if (context == WorldNavContext.InGame && WorldAbilityTargetingState.IsActive)
            {
                abilityDestInfo = WorldAbilityTargetingState.GetDestinationInfo(currentSelectedTile);
            }

            // Pass fuel cost and route info based on context
            string tileInfo = WorldInfoHelper.GetTileSummary(
                currentSelectedTile,
                includeRouteInfo: context == WorldNavContext.InGame,
                minimal: false,
                fuelCostInfo: fuelCostInfo);

            // Append ability destination info if available (in-game only)
            if (!string.IsNullOrEmpty(abilityDestInfo))
            {
                tileInfo += $". {abilityDestInfo}";
            }

            // WorldGen: append faction proximity warning (change-only) before biome description
            if (context == WorldNavContext.WorldGen)
            {
                string factionWarning = StartingSiteContext.GetFactionProximityWarning(currentSelectedTile);
                if (!string.IsNullOrEmpty(factionWarning))
                {
                    tileInfo = AppendSentence(tileInfo, factionWarning);
                }

                // WorldGen: append biome settle warning if present
                BiomeDef biome = currentSelectedTile.Tile?.PrimaryBiome;
                if (biome != null && !string.IsNullOrEmpty(biome.settleWarning))
                {
                    tileInfo = AppendSentence(tileInfo, "Warning".Translate() + ": " + biome.settleWarning);
                }
            }

            // Both contexts: append biome description if entering a new biome
            string biomeDesc = BiomeDescriptionTracker.GetBiomeDescriptionIfNew(currentSelectedTile);
            if (!string.IsNullOrEmpty(biomeDesc))
            {
                tileInfo = AppendSentence(tileInfo, biomeDesc);
            }

            TolkHelper.Speak(tileInfo);

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
        /// Jumps to the player's home settlement. In-game only.
        /// </summary>
        public static void JumpToHome()
        {
            if (context != WorldNavContext.InGame) return;
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
        /// Jumps to the nearest player caravan. In-game only.
        /// </summary>
        public static void JumpToNearestCaravan()
        {
            if (context != WorldNavContext.InGame) return;
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
        /// Opens the settlement browser (S key). In-game only.
        /// </summary>
        public static void OpenSettlementBrowser()
        {
            if (context != WorldNavContext.InGame) return;
            if (!isInitialized)
                return;

            SettlementBrowserState.Open(currentSelectedTile);
        }

        /// <summary>
        /// Opens the quest locations browser (Q key). In-game only.
        /// </summary>
        public static void OpenQuestLocationsBrowser()
        {
            if (context != WorldNavContext.InGame) return;
            if (!isInitialized)
                return;

            QuestLocationsBrowserState.Open(currentSelectedTile);
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
        /// Forms a caravan at the currently selected settlement (C key). In-game only.
        /// Opens the route planner first to set destination, then opens the caravan formation dialog.
        /// </summary>
        public static void FormCaravanAtSelectedSettlement()
        {
            if (context != WorldNavContext.InGame) return;
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
        /// Shows the caravan inspect screen (I key when caravan selected). In-game only.
        /// </summary>
        public static void ShowCaravanInspect()
        {
            if (context != WorldNavContext.InGame) return;
            Caravan caravan = GetSelectedCaravan();
            if (caravan == null)
            {
                TolkHelper.Speak("No caravan selected", SpeechPriority.Normal);
                return;
            }

            CaravanInspectState.Open(caravan);
        }

        /// <summary>
        /// Opens the order menu for the currently selected caravan (] key). In-game only.
        /// Uses the cursor tile as the target location for orders.
        /// </summary>
        public static void GiveCaravanOrders()
        {
            if (context != WorldNavContext.InGame) return;
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
        /// Cycles to the next player caravan (for order-giving). In-game only.
        /// Does not move the map cursor.
        /// </summary>
        public static void CycleToNextCaravan()
        {
            if (context != WorldNavContext.InGame) return;
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
        /// Cycles to the previous player caravan (for order-giving). In-game only.
        /// Does not move the map cursor.
        /// </summary>
        public static void CycleToPreviousCaravan()
        {
            if (context != WorldNavContext.InGame) return;
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
        /// Toggles multi-selection of the currently focused caravan (via Ctrl+Space). In-game only.
        /// </summary>
        public static void ToggleCaravanSelection()
        {
            if (context != WorldNavContext.InGame) return;
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
        /// Jumps the cursor to the selected caravan(s) location (Alt+C). In-game only.
        /// If multiple caravans are selected, they must all be on the same tile.
        /// </summary>
        public static void JumpToSelectedCaravans()
        {
            if (context != WorldNavContext.InGame) return;
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
                SyncSelectionWithGame();

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
                SyncSelectionWithGame();
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
