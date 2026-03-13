using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Categories for the additional info menu (I key) on the starting site screen.
    /// </summary>
    public enum AdditionalInfoCategory
    {
        RoadsAndRivers,
        StoneTypes,
        GrowingInfo,
        HealthInfo,
        MovementAndLocation,
        DLCFeatures,
        Coordinates
    }

    /// <summary>
    /// World generation starting site context.
    /// Holds features specific to the world gen screen:
    /// - I key additional info menu (7 categories)
    /// - R key random tile selection
    /// - Ctrl+arrows biome jump (using 3D vector math)
    /// - Faction proximity warnings (change-only announcements)
    /// - Tile validation for settlement placement
    /// </summary>
    public static class StartingSiteContext
    {
        // I-menu state
        private static bool isMenuOpen = false;
        private static int selectedMenuIndex = 0;
        private static List<AdditionalInfoCategory> availableMenuItems = new List<AdditionalInfoCategory>();

        public static bool IsMenuOpen => isMenuOpen;
        public static int SelectedMenuIndex => selectedMenuIndex;
        public static int MenuItemCount => availableMenuItems.Count;

        public static void Open()
        {
            isMenuOpen = false;
            selectedMenuIndex = 0;
            availableMenuItems.Clear();
        }

        public static void Close()
        {
            isMenuOpen = false;
            selectedMenuIndex = 0;
            availableMenuItems.Clear();
        }

        /// <summary>
        /// Closes the I-menu when navigating to a new tile.
        /// Called by WorldNavigationState.MoveInDirection() when in WorldGen context.
        /// </summary>
        public static void OnTileChanged()
        {
            isMenuOpen = false;
        }

        // =============================================
        // I-Menu
        // =============================================

        public static void OpenAdditionalInfoMenu()
        {
            PlanetTile tile = WorldNavigationState.CurrentSelectedTile;
            if (!tile.Valid)
            {
                TolkHelper.Speak("No tile selected. Use arrow keys to navigate to a tile first.");
                return;
            }

            if (!isMenuOpen)
            {
                isMenuOpen = true;
                selectedMenuIndex = 0;
                PopulateMenuItems();

                if (availableMenuItems.Count > 0)
                {
                    string menuTitle = "Additional Information Menu - " + availableMenuItems.Count + " categories available";
                    string firstItem = GetMenuItemName(availableMenuItems[0]);
                    TolkHelper.Speak($"{menuTitle}. Selected: {firstItem}. Use arrow keys to navigate, Enter to read details, Escape to close.");
                }
                else
                {
                    TolkHelper.Speak("No additional information available for this tile.");
                    isMenuOpen = false;
                }
            }
            else
            {
                NavigateMenu(1);
            }
        }

        public static void NavigateMenu(int direction)
        {
            if (!isMenuOpen || availableMenuItems.Count == 0)
                return;

            selectedMenuIndex += direction;

            if (selectedMenuIndex < 0)
                selectedMenuIndex = availableMenuItems.Count - 1;
            if (selectedMenuIndex >= availableMenuItems.Count)
                selectedMenuIndex = 0;

            string itemName = GetMenuItemName(availableMenuItems[selectedMenuIndex]);
            TolkHelper.Speak($"{itemName} ({selectedMenuIndex + 1} of {availableMenuItems.Count})");
        }

        public static void ReadSelectedMenuItem()
        {
            if (!isMenuOpen || availableMenuItems.Count == 0)
                return;

            PlanetTile tile = WorldNavigationState.CurrentSelectedTile;
            if (!tile.Valid)
                return;

            AdditionalInfoCategory category = availableMenuItems[selectedMenuIndex];
            string info = GetDetailedInfoForCategory(tile, category);
            TolkHelper.Speak(info);
        }

        public static void CloseMenu()
        {
            if (isMenuOpen)
            {
                isMenuOpen = false;
                TolkHelper.Speak("Menu closed.");
            }
        }

        public static string GetCurrentMenuItemName()
        {
            if (!isMenuOpen || availableMenuItems.Count == 0)
                return "";
            return GetMenuItemName(availableMenuItems[selectedMenuIndex]);
        }

        // =============================================
        // Random tile selection
        // =============================================

        public static void SelectRandomTile()
        {
            PlanetTile randomTile = TileFinder.RandomStartingTile();
            if (!randomTile.Valid)
            {
                TolkHelper.Speak("Failed to find a valid random starting site.", SpeechPriority.High);
                return;
            }

            // Update shared navigation state
            WorldNavigationState.CurrentSelectedTile = randomTile;
            Find.GameInitData.startingTile = randomTile;
            Find.WorldInterface.SelectedTile = randomTile;
            Find.WorldCameraDriver.JumpTo(Find.WorldGrid.GetTileCenter(randomTile));

            isMenuOpen = false;

            string tileInfo = WorldInfoHelper.GetTileSummary(randomTile, includeRouteInfo: false);

            string factionWarning = GetFactionProximityWarning(randomTile);
            if (!string.IsNullOrEmpty(factionWarning))
            {
                tileInfo += $". {factionWarning}";
            }

            // Include biome description for the new random location
            string biomeDesc = BiomeDescriptionTracker.GetBiomeDescriptionIfNew(randomTile);
            if (!string.IsNullOrEmpty(biomeDesc))
            {
                tileInfo += $". {biomeDesc}";
            }

            TolkHelper.Speak($"Random site selected: {tileInfo}");
        }

        // =============================================
        // Biome jump (Ctrl+arrows)
        // =============================================

        /// <summary>
        /// Jumps to the next biome boundary in the given arrow key direction.
        /// Uses 3D geographic compass math (same as WorldNavigationState.HandleArrowKey).
        /// </summary>
        public static void JumpToNextBiomeInDirection(KeyCode arrowKey)
        {
            PlanetTile currentTile = WorldNavigationState.CurrentSelectedTile;
            if (!currentTile.Valid)
            {
                SelectRandomTile();
                return;
            }

            BiomeDef currentBiome = currentTile.Tile?.PrimaryBiome;
            if (currentBiome == null)
            {
                TolkHelper.Speak("Cannot determine biome at current location");
                return;
            }
            isMenuOpen = false;

            // Calculate direction vector using 3D geographic compass math
            Vector3 currentPos = Find.WorldGrid.GetTileCenter(currentTile);
            Vector3 up = currentPos.normalized;
            Vector3 north = Vector3.ProjectOnPlane(Vector3.up, up).normalized;
            Vector3 east = Vector3.Cross(up, north).normalized;

            Vector3 desiredDirection;
            switch (arrowKey)
            {
                case KeyCode.UpArrow: desiredDirection = north; break;
                case KeyCode.DownArrow: desiredDirection = -north; break;
                case KeyCode.RightArrow: desiredDirection = east; break;
                case KeyCode.LeftArrow: desiredDirection = -east; break;
                default: return;
            }

            // Walk in direction until biome changes
            PlanetTile walker = currentTile;
            int iterations = 0;
            const int maxIterations = 1000;

            while (iterations < maxIterations)
            {
                List<PlanetTile> neighbors = new List<PlanetTile>();
                Find.WorldGrid.GetTileNeighbors(walker, neighbors);

                if (neighbors.Count == 0)
                    break;

                // Find best neighbor in direction using dot product
                PlanetTile bestNeighbor = PlanetTile.Invalid;
                float bestDot = -2f;
                Vector3 walkerPos = Find.WorldGrid.GetTileCenter(walker);

                foreach (var neighbor in neighbors)
                {
                    Vector3 neighborPos = Find.WorldGrid.GetTileCenter(neighbor);
                    Vector3 dir = (neighborPos - walkerPos).normalized;
                    float dot = Vector3.Dot(dir, desiredDirection);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        bestNeighbor = neighbor;
                    }
                }

                if (!bestNeighbor.Valid)
                    break;

                if (bestNeighbor.Tile?.PrimaryBiome != currentBiome)
                {
                    // Found new biome - move there via shared state
                    WorldNavigationState.CurrentSelectedTile = bestNeighbor;
                    Find.GameInitData.startingTile = bestNeighbor;
                    Find.WorldInterface.SelectedTile = bestNeighbor;
                    Find.WorldCameraDriver.JumpTo(Find.WorldGrid.GetTileCenter(bestNeighbor));

                    if (Find.WorldSelector != null)
                        Find.WorldSelector.SelectedTile = bestNeighbor;

                    // Announce via shared AnnounceTile (handles biome description + warnings)
                    WorldNavigationState.AnnounceTile();
                    return;
                }

                walker = bestNeighbor;
                iterations++;
            }

            TolkHelper.Speak($"No different biome found (searched {iterations} tiles)");
        }

        // =============================================
        // Faction proximity warnings
        // =============================================

        /// <summary>
        /// Gets the current faction proximity warning for a tile.
        /// </summary>
        public static string GetFactionProximityWarning(PlanetTile tile)
        {
            if (!tile.Valid)
                return null;

            List<Pair<Settlement, int>> proximityOffsets = new List<Pair<Settlement, int>>();
            SettlementProximityGoodwillUtility.AppendProximityGoodwillOffsets(
                tile,
                proximityOffsets,
                ignoreIfAlreadyMinGoodwill: false,
                ignorePermanentlyHostile: true);

            if (proximityOffsets.Count > 0)
            {
                return $"{"Warning".Translate()}: Settling here affects {proximityOffsets.Count} faction{(proximityOffsets.Count > 1 ? "s" : "")}";
            }

            return null;
        }

        // =============================================
        // Private helpers - Menu
        // =============================================

        private static void PopulateMenuItems()
        {
            availableMenuItems.Clear();

            // Always available
            availableMenuItems.Add(AdditionalInfoCategory.GrowingInfo);
            availableMenuItems.Add(AdditionalInfoCategory.HealthInfo);
            availableMenuItems.Add(AdditionalInfoCategory.MovementAndLocation);
            availableMenuItems.Add(AdditionalInfoCategory.Coordinates);

            PlanetTile tile = WorldNavigationState.CurrentSelectedTile;
            if (!tile.Valid)
                return;

            Tile tileData = tile.Tile;
            if (tileData == null) return;

            // Roads/Rivers if present
            if (tileData is SurfaceTile surfaceTile)
            {
                if ((surfaceTile.Roads != null && surfaceTile.Roads.Count > 0) ||
                    (surfaceTile.Rivers != null && surfaceTile.Rivers.Count > 0) ||
                    Find.World.CoastDirectionAt(tile) != Rot4.Invalid)
                {
                    availableMenuItems.Insert(0, AdditionalInfoCategory.RoadsAndRivers);
                }
            }

            // Stone types if base can be built
            if (tileData.PrimaryBiome?.canBuildBase == true)
            {
                availableMenuItems.Insert(availableMenuItems.Count > 0 ? 1 : 0, AdditionalInfoCategory.StoneTypes);
            }

            // DLC features if any present
            if (ModsConfig.BiotechActive || ModsConfig.AnomalyActive)
            {
                availableMenuItems.Add(AdditionalInfoCategory.DLCFeatures);
            }
        }

        private static string GetMenuItemName(AdditionalInfoCategory category)
        {
            switch (category)
            {
                case AdditionalInfoCategory.RoadsAndRivers:
                    return "Roads, Rivers, and Coastline";
                case AdditionalInfoCategory.StoneTypes:
                    return "Stone Types";
                case AdditionalInfoCategory.GrowingInfo:
                    return "Growing and Foraging";
                case AdditionalInfoCategory.HealthInfo:
                    return "Disease Frequency";
                case AdditionalInfoCategory.MovementAndLocation:
                    return "Movement and Time Zone";
                case AdditionalInfoCategory.DLCFeatures:
                    return "DLC Features";
                case AdditionalInfoCategory.Coordinates:
                    return "Coordinates";
                default:
                    return "Unknown";
            }
        }

        private static string GetDetailedInfoForCategory(PlanetTile tile, AdditionalInfoCategory category)
        {
            // Delegate to WorldInfoHelper for categories that overlap with number keys 1-5
            switch (category)
            {
                case AdditionalInfoCategory.GrowingInfo:
                    return WorldInfoHelper.GetTileGrowingInfo(tile);
                case AdditionalInfoCategory.HealthInfo:
                    return WorldInfoHelper.GetTileHealthInfo(tile);
                case AdditionalInfoCategory.MovementAndLocation:
                    return WorldInfoHelper.GetTileMovementInfo(tile);
                case AdditionalInfoCategory.Coordinates:
                    return WorldInfoHelper.GetTileLocationInfo(tile);
                // Keep world-gen-specific categories that have their own format
                case AdditionalInfoCategory.RoadsAndRivers:
                    return GetRoadsAndRiversInfo(tile);
                case AdditionalInfoCategory.StoneTypes:
                    return GetStoneTypesInfo(tile);
                case AdditionalInfoCategory.DLCFeatures:
                    return GetDLCFeaturesInfo(tile);
                default:
                    return "No information available.";
            }
        }

        // =============================================
        // Private helpers - Info categories
        // =============================================

        private static string GetRoadsAndRiversInfo(PlanetTile tile)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Roads, Rivers, and Coastline:");

            Tile tileData = tile.Tile;
            if (tileData is SurfaceTile surfaceTile)
            {
                if (surfaceTile.Roads != null && surfaceTile.Roads.Count > 0)
                {
                    string roads = string.Join(", ", surfaceTile.Roads.Select(r => r.road.label).Distinct());
                    sb.AppendLine($"Roads: {roads}");
                }
                else
                {
                    sb.AppendLine("Roads: None");
                }

                if (surfaceTile.Rivers != null && surfaceTile.Rivers.Count > 0)
                {
                    var largestRiver = surfaceTile.Rivers.MaxBy(r => r.river.degradeThreshold);
                    sb.AppendLine($"River: {largestRiver.river.LabelCap}");
                }
                else
                {
                    sb.AppendLine("River: None");
                }
            }

            Rot4 coastDirection = Find.World.CoastDirectionAt(tile);
            if (coastDirection != Rot4.Invalid)
            {
                sb.AppendLine($"Coastal: Yes (water to the {coastDirection})");
            }
            else
            {
                sb.AppendLine("Coastal: No");
            }

            return sb.ToString().TrimEnd();
        }

        private static string GetStoneTypesInfo(PlanetTile tile)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Stone Types:");

            var stoneTypes = Find.World.NaturalRockTypesIn(tile);
            if (stoneTypes != null && stoneTypes.Any())
            {
                string stones = string.Join(", ", stoneTypes.Select(s => s.label));
                sb.AppendLine(stones);
            }
            else
            {
                sb.AppendLine("No stone information available.");
            }

            return sb.ToString().TrimEnd();
        }

        private static string GetDLCFeaturesInfo(PlanetTile tile)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("DLC Features:");

            Tile tileData = tile.Tile;
            bool hasAnyInfo = false;

            if (ModsConfig.BiotechActive)
            {
                float pollution = tileData.pollution;
                sb.AppendLine($"Pollution: {pollution.ToStringPercent()}");

                float nearbyPollution = WorldPollutionUtility.CalculateNearbyPollutionScore(tile.tileId);
                if (nearbyPollution >= GameConditionDefOf.NoxiousHaze.minNearbyPollution)
                {
                    float hazeInterval = GameConditionDefOf.NoxiousHaze.mtbOverNearbyPollutionCurve.Evaluate(nearbyPollution);
                    sb.AppendLine($"Noxious Haze: Every {hazeInterval:F1} days on average");
                }
                else
                {
                    sb.AppendLine("Noxious Haze: Not occurring");
                }

                hasAnyInfo = true;
            }

            if (tileData.Landmark != null)
            {
                sb.AppendLine($"Landmark: {tileData.Landmark.name}");
                hasAnyInfo = true;
            }

            if (tileData.Mutators != null && tileData.Mutators.Count > 0)
            {
                sb.AppendLine($"Tile Mutators: {tileData.Mutators.Count}");
                foreach (var mutator in tileData.Mutators)
                {
                    sb.AppendLine($"  - {mutator.label}");
                }
                hasAnyInfo = true;
            }

            if (!hasAnyInfo)
            {
                sb.AppendLine("No DLC features present at this tile.");
            }

            return sb.ToString().TrimEnd();
        }
    }
}
