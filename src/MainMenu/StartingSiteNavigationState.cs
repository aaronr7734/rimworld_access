using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
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

    public static class StartingSiteNavigationState
    {
        private static bool initialized = false;
        private static bool hasReadCurrentTile = false;

        // Menu state
        private static bool isMenuOpen = false;
        private static int selectedMenuIndex = 0;
        private static List<AdditionalInfoCategory> availableMenuItems = new List<AdditionalInfoCategory>();

        // Faction proximity tracking
        private static string lastFactionWarning = null;

        public static void Initialize()
        {
            if (!initialized)
            {
                hasReadCurrentTile = false;
                isMenuOpen = false;
                selectedMenuIndex = 0;
                lastFactionWarning = null;
                initialized = true;
            }
        }

        public static void Reset()
        {
            initialized = false;
            hasReadCurrentTile = false;
            isMenuOpen = false;
            selectedMenuIndex = 0;
            availableMenuItems.Clear();
            lastFactionWarning = null;
        }

        public static void ReadCurrentTile()
        {
            PlanetTile tile = Find.WorldInterface.SelectedTile;

            if (!tile.Valid)
            {
                TolkHelper.Speak("No starting site selected. Press R to select random site, or use arrow keys to navigate the world map.");
                return;
            }

            string tileInfo = GetTileDescription(tile);

            // Add faction warning if present
            string factionWarning = GetFactionProximityWarning(tile);
            if (!string.IsNullOrEmpty(factionWarning))
            {
                tileInfo += " | " + factionWarning;
            }

            TolkHelper.Speak(tileInfo);
            hasReadCurrentTile = true;
        }

        public static void SelectRandomTile()
        {
            PlanetTile randomTile = TileFinder.RandomStartingTile();

            if (!randomTile.Valid)
            {
                TolkHelper.Speak("Failed to find a valid random starting site.", SpeechPriority.High);
                return;
            }

            Find.GameInitData.startingTile = randomTile;
            Find.WorldInterface.SelectedTile = randomTile;
            Find.WorldCameraDriver.JumpTo(Find.WorldGrid.GetTileCenter(randomTile));

            // Close menu if open
            isMenuOpen = false;

            // Read the newly selected tile
            string tileInfo = GetTileDescription(randomTile);

            // Add faction warning if present
            string factionWarning = GetFactionProximityWarning(randomTile);
            if (!string.IsNullOrEmpty(factionWarning))
            {
                tileInfo += " | " + factionWarning;
                lastFactionWarning = factionWarning;
            }

            TolkHelper.Speak($"Random site selected: {tileInfo}");
            hasReadCurrentTile = true;
        }

        public static void MoveInDirection(Direction8Way direction)
        {
            PlanetTile currentTile = Find.WorldInterface.SelectedTile;

            if (!currentTile.Valid)
            {
                // If no tile selected, start with a random valid tile
                SelectRandomTile();
                return;
            }

            // Get all neighbors
            List<PlanetTile> neighbors = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(currentTile, neighbors);

            if (neighbors.Count == 0)
            {
                TolkHelper.Speak("No neighboring tiles found.");
                return;
            }

            // Find the neighbor closest to the desired direction
            PlanetTile bestNeighbor = FindNeighborInDirection(currentTile, neighbors, direction);

            if (bestNeighbor.Valid)
            {
                // Close menu when moving to new tile
                isMenuOpen = false;

                // Update selection
                Find.WorldInterface.SelectedTile = bestNeighbor;
                Find.GameInitData.startingTile = bestNeighbor;
                Find.WorldCameraDriver.JumpTo(Find.WorldGrid.GetTileCenter(bestNeighbor));

                // Announce the new tile
                string tileInfo = GetTileDescription(bestNeighbor);
                Direction8Way actualDirection = Find.WorldGrid.GetDirection8WayFromTo(currentTile, bestNeighbor);

                // Check faction proximity warning changes
                string factionWarning = GetFactionProximityWarning(bestNeighbor);
                if (factionWarning != lastFactionWarning)
                {
                    if (!string.IsNullOrEmpty(factionWarning))
                    {
                        tileInfo += " | " + factionWarning;
                    }
                    lastFactionWarning = factionWarning;
                }

                TolkHelper.Speak($"Moved {actualDirection}: {tileInfo}");
            }
        }

        public static void JumpToNextBiomeInDirection(Direction8Way direction)
        {
            PlanetTile currentTile = Find.WorldInterface.SelectedTile;

            if (!currentTile.Valid)
            {
                // If no tile selected, start with a random valid tile
                SelectRandomTile();
                return;
            }

            // Get current biome
            BiomeDef currentBiome = currentTile.Tile.PrimaryBiome;

            // Close menu when jumping
            isMenuOpen = false;

            // Keep moving in the direction until we find a different biome
            PlanetTile nextTile = currentTile;
            PlanetTile candidateTile = PlanetTile.Invalid;
            int maxIterations = 1000; // Prevent infinite loops
            int iterations = 0;

            while (iterations < maxIterations)
            {
                // Get neighbors of current position
                List<PlanetTile> neighbors = new List<PlanetTile>();
                Find.WorldGrid.GetTileNeighbors(nextTile, neighbors);

                if (neighbors.Count == 0)
                {
                    break;
                }

                // Find the neighbor closest to the desired direction
                PlanetTile bestNeighbor = FindNeighborInDirection(nextTile, neighbors, direction);

                if (!bestNeighbor.Valid)
                {
                    break;
                }

                // Check if this neighbor has a different biome
                BiomeDef neighborBiome = bestNeighbor.Tile.PrimaryBiome;
                if (neighborBiome != currentBiome)
                {
                    candidateTile = bestNeighbor;
                    break;
                }

                // Move to this neighbor and continue searching
                nextTile = bestNeighbor;
                iterations++;
            }

            if (candidateTile.Valid)
            {
                // Update selection
                Find.WorldInterface.SelectedTile = candidateTile;
                Find.GameInitData.startingTile = candidateTile;
                Find.WorldCameraDriver.JumpTo(Find.WorldGrid.GetTileCenter(candidateTile));

                // Announce the new tile
                string tileInfo = GetTileDescription(candidateTile);
                Direction8Way actualDirection = Find.WorldGrid.GetDirection8WayFromTo(currentTile, candidateTile);

                // Check faction proximity warning changes
                string factionWarning = GetFactionProximityWarning(candidateTile);
                if (factionWarning != lastFactionWarning)
                {
                    if (!string.IsNullOrEmpty(factionWarning))
                    {
                        tileInfo += " | " + factionWarning;
                    }
                    lastFactionWarning = factionWarning;
                }

                TolkHelper.Speak($"Jumped {actualDirection} to new biome ({iterations} tiles): {tileInfo}");
            }
            else
            {
                // No different biome found in that direction
                TolkHelper.Speak($"No different biome found in {direction} direction (searched {iterations} tiles)");
            }
        }

        // Menu System
        public static void OpenAdditionalInfoMenu()
        {
            PlanetTile tile = Find.WorldInterface.SelectedTile;

            if (!tile.Valid)
            {
                TolkHelper.Speak("No tile selected. Use arrow keys to navigate to a tile first.");
                return;
            }

            if (!isMenuOpen)
            {
                // Open menu
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
                // Navigate down
                NavigateMenu(1);
            }
        }

        public static void NavigateMenu(int direction)
        {
            if (!isMenuOpen || availableMenuItems.Count == 0)
                return;

            selectedMenuIndex += direction;

            // Wrap around
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

            PlanetTile tile = Find.WorldInterface.SelectedTile;
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

        private static void PopulateMenuItems()
        {
            availableMenuItems.Clear();

            // Always available
            availableMenuItems.Add(AdditionalInfoCategory.GrowingInfo);
            availableMenuItems.Add(AdditionalInfoCategory.HealthInfo);
            availableMenuItems.Add(AdditionalInfoCategory.MovementAndLocation);
            availableMenuItems.Add(AdditionalInfoCategory.Coordinates);

            PlanetTile tile = Find.WorldInterface.SelectedTile;
            if (!tile.Valid)
                return;

            Tile tileData = tile.Tile;

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
            if (tileData.PrimaryBiome.canBuildBase)
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
            switch (category)
            {
                case AdditionalInfoCategory.RoadsAndRivers:
                    return GetRoadsAndRiversInfo(tile);
                case AdditionalInfoCategory.StoneTypes:
                    return GetStoneTypesInfo(tile);
                case AdditionalInfoCategory.GrowingInfo:
                    return GetGrowingInfo(tile);
                case AdditionalInfoCategory.HealthInfo:
                    return GetHealthInfo(tile);
                case AdditionalInfoCategory.MovementAndLocation:
                    return GetMovementInfo(tile);
                case AdditionalInfoCategory.DLCFeatures:
                    return GetDLCFeaturesInfo(tile);
                case AdditionalInfoCategory.Coordinates:
                    return GetCoordinatesInfo(tile);
                default:
                    return "No information available.";
            }
        }

        // Information Gathering Methods
        private static string GetRoadsAndRiversInfo(PlanetTile tile)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Roads, Rivers, and Coastline:");

            Tile tileData = tile.Tile;
            if (tileData is SurfaceTile surfaceTile)
            {
                // Roads
                if (surfaceTile.Roads != null && surfaceTile.Roads.Count > 0)
                {
                    string roads = string.Join(", ", surfaceTile.Roads.Select(r => r.road.label).Distinct());
                    sb.AppendLine($"Roads: {roads}");
                }
                else
                {
                    sb.AppendLine("Roads: None");
                }

                // Rivers
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

            // Coastal status
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

        private static string GetGrowingInfo(PlanetTile tile)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Growing and Foraging:");

            Tile tileData = tile.Tile;

            // Growing period
            string growingPeriod = Zone_Growing.GrowingQuadrumsDescription(tile);
            sb.AppendLine($"Growing Period: {growingPeriod}");

            // Forageability
            if (tileData.PrimaryBiome.foragedFood != null && tileData.PrimaryBiome.forageability > 0f)
            {
                sb.AppendLine($"Forageability: {tileData.PrimaryBiome.forageability.ToStringPercent()} ({tileData.PrimaryBiome.foragedFood.label})");
            }
            else
            {
                sb.AppendLine("Forageability: 0%");
            }

            // Animal grazing
            bool canGraze = VirtualPlantsUtility.EnvironmentAllowsEatingVirtualPlantsNowAt(tile);
            sb.AppendLine($"Animals Can Graze Now: {(canGraze ? "Yes" : "No")}");

            return sb.ToString().TrimEnd();
        }

        private static string GetHealthInfo(PlanetTile tile)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Disease Frequency:");

            Tile tileData = tile.Tile;
            float diseasesPerYear = 60f / tileData.PrimaryBiome.diseaseMtbDays;
            sb.AppendLine($"{diseasesPerYear:F1} per year");

            return sb.ToString().TrimEnd();
        }

        private static string GetMovementInfo(PlanetTile tile)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Movement and Location:");

            Tile tileData = tile.Tile;

            // Movement difficulty
            if (!Find.World.Impassable(tile))
            {
                StringBuilder explanation = new StringBuilder();
                float movementDifficulty = WorldPathGrid.CalculatedMovementDifficultyAt(tile, false, null, explanation);
                float roadMultiplier = Find.WorldGrid.GetRoadMovementDifficultyMultiplier(tile, PlanetTile.Invalid, explanation);
                float totalDifficulty = movementDifficulty * roadMultiplier;

                sb.AppendLine($"Movement Difficulty: {totalDifficulty:0.#}");

                if (WorldPathGrid.WillWinterEverAffectMovementDifficulty(tile))
                {
                    sb.AppendLine("Winter Movement Penalty: +2.0");
                }
            }
            else
            {
                sb.AppendLine("Movement Difficulty: Impassable");
            }

            // Time zone
            sb.AppendLine($"Time Zone: {Find.WorldGrid.LongLatOf(tile).y:F1}");

            return sb.ToString().TrimEnd();
        }

        private static string GetDLCFeaturesInfo(PlanetTile tile)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("DLC Features:");

            Tile tileData = tile.Tile;
            bool hasAnyInfo = false;

            // Pollution (Biotech)
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

            // Landmarks (Odyssey)
            if (tileData.Landmark != null)
            {
                sb.AppendLine($"Landmark: {tileData.Landmark.name}");
                hasAnyInfo = true;
            }

            // Tile mutators
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

        private static string GetCoordinatesInfo(PlanetTile tile)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Coordinates:");

            Vector2 longLat = Find.WorldGrid.LongLatOf(tile);
            sb.AppendLine($"Longitude: {longLat.x:F2}°");
            sb.AppendLine($"Latitude: {longLat.y:F2}°");
            sb.AppendLine($"Tile ID: {tile.tileId}");

            return sb.ToString().TrimEnd();
        }

        // Faction Proximity
        private static string GetFactionProximityWarning(PlanetTile tile)
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
                return $"Warning: Settling here affects {proximityOffsets.Count} faction{(proximityOffsets.Count > 1 ? "s" : "")}";
            }

            return null;
        }

        // Tile Validation
        public static void ValidateTileForSettlement()
        {
            PlanetTile tile = Find.WorldInterface.SelectedTile;

            if (!tile.Valid)
            {
                TolkHelper.Speak("No tile selected. Use arrow keys to navigate to a tile first.");
                return;
            }

            StringBuilder reason = new StringBuilder();
            bool isValid = TileFinder.IsValidTileForNewSettlement(tile, reason, forGravship: false);

            if (isValid)
            {
                // Show confirmation with faction proximity warnings if any
                List<Pair<Settlement, int>> proximityOffsets = new List<Pair<Settlement, int>>();
                SettlementProximityGoodwillUtility.AppendProximityGoodwillOffsets(
                    tile,
                    proximityOffsets,
                    ignoreIfAlreadyMinGoodwill: false,
                    ignorePermanentlyHostile: true);

                if (proximityOffsets.Count > 0)
                {
                    StringBuilder warning = new StringBuilder();
                    warning.AppendLine("Valid settlement location. Warning: Settling here will affect faction relations:");
                    foreach (var offset in proximityOffsets)
                    {
                        string change = offset.Second > 0 ? $"+{offset.Second}" : offset.Second.ToString();
                        warning.AppendLine($"  {offset.First.Faction.Name}: {change} goodwill per season");
                    }
                    TolkHelper.Speak(warning.ToString().TrimEnd());
                }
                else
                {
                    TolkHelper.Speak("Valid settlement location. No faction proximity warnings. Press Next to proceed.");
                }
            }
            else
            {
                string errorMessage = "Cannot settle here: " + reason.ToString();
                TolkHelper.Speak(errorMessage, SpeechPriority.High);
            }
        }

        private static PlanetTile FindNeighborInDirection(PlanetTile currentTile, List<PlanetTile> neighbors, Direction8Way targetDirection)
        {
            // Score each neighbor based on how close it is to the target direction
            PlanetTile bestNeighbor = PlanetTile.Invalid;
            float bestScore = float.MaxValue;

            foreach (PlanetTile neighbor in neighbors)
            {
                Direction8Way neighborDirection = Find.WorldGrid.GetDirection8WayFromTo(currentTile, neighbor);
                float directionDiff = GetDirectionDifference(targetDirection, neighborDirection);

                if (directionDiff < bestScore)
                {
                    bestScore = directionDiff;
                    bestNeighbor = neighbor;
                }
            }

            return bestNeighbor;
        }

        private static float GetDirectionDifference(Direction8Way target, Direction8Way actual)
        {
            // Convert to angles (0-7, where 0 is North)
            int targetAngle = (int)target;
            int actualAngle = (int)actual;

            // Calculate smallest angular difference
            int diff = System.Math.Abs(targetAngle - actualAngle);
            if (diff > 4)
            {
                diff = 8 - diff;
            }

            return diff;
        }

        private static string GetTileDescription(PlanetTile tile)
        {
            if (!tile.Valid)
            {
                return "Invalid tile";
            }

            Tile tileData = tile.Tile;
            StringBuilder sb = new StringBuilder();

            // Biome
            sb.Append($"Biome: {tileData.PrimaryBiome.LabelCap}");

            // Temperature
            sb.Append($" | Avg Temp: {tileData.temperature:F0}°C");

            // Rainfall
            sb.Append($" | Rainfall: {tileData.rainfall:F0}mm");

            // Hilliness
            sb.Append($" | Terrain: {tileData.hilliness.GetLabel()}");

            // Swampiness
            if (tileData.swampiness > 0.5f)
            {
                sb.Append(" | Swampy");
            }

            // Elevation
            sb.Append($" | Elevation: {tileData.elevation:F0}m");

            return sb.ToString();
        }

        public static bool HasReadCurrentTile => hasReadCurrentTile;
        public static bool IsMenuOpen => isMenuOpen;
        public static int SelectedMenuIndex => selectedMenuIndex;
        public static int MenuItemCount => availableMenuItems.Count;

        public static string GetCurrentMenuItemName()
        {
            if (!isMenuOpen || availableMenuItems.Count == 0)
                return "";
            return GetMenuItemName(availableMenuItems[selectedMenuIndex]);
        }
    }
}
