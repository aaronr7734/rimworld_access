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
                TolkHelper.Speak("RimWorldAccess.StartingSite.NoTileSelected".Loc());
                return;
            }

            if (!isMenuOpen)
            {
                isMenuOpen = true;
                selectedMenuIndex = 0;
                PopulateMenuItems();

                if (availableMenuItems.Count > 0)
                {
                    string menuTitle = "RimWorldAccess.StartingSite.MenuTitle".Translate(availableMenuItems.Count);
                    string firstItem = GetMenuItemName(availableMenuItems[0]);
                    TolkHelper.Speak("RimWorldAccess.StartingSite.MenuOpened".Loc(menuTitle, firstItem));
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.StartingSite.NoAdditionalInfo".Loc());
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
            TolkHelper.Speak("RimWorldAccess.StartingSite.MenuItemPosition".Translate(itemName, selectedMenuIndex + 1, availableMenuItems.Count));
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
                TolkHelper.Speak("RimWorldAccess.StartingSite.MenuClosed".Loc());
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
                TolkHelper.Speak("RimWorldAccess.StartingSite.RandomSiteFailed".Loc(), SpeechPriority.High);
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

            TolkHelper.Speak("RimWorldAccess.StartingSite.RandomSiteSelected".Loc(tileInfo));
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
                TolkHelper.Speak("RimWorldAccess.StartingSite.CannotDetermineBiome".Loc());
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

            TolkHelper.Speak("RimWorldAccess.StartingSite.NoDifferentBiomeFound".Loc(iterations));
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
                string key = proximityOffsets.Count == 1
                    ? "RimWorldAccess.StartingSite.SettlingAffectsFactionsOne"
                    : "RimWorldAccess.StartingSite.SettlingAffectsFactionsMany";
                return key.Translate("Warning".Translate(), proximityOffsets.Count);
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
                    return "RimWorldAccess.StartingSite.Category.RoadsAndRivers".Translate();
                case AdditionalInfoCategory.StoneTypes:
                    return "RimWorldAccess.StartingSite.Category.StoneTypes".Translate();
                case AdditionalInfoCategory.GrowingInfo:
                    return "RimWorldAccess.StartingSite.Category.GrowingInfo".Translate();
                case AdditionalInfoCategory.HealthInfo:
                    return "RimWorldAccess.StartingSite.Category.HealthInfo".Translate();
                case AdditionalInfoCategory.MovementAndLocation:
                    return "RimWorldAccess.StartingSite.Category.MovementAndLocation".Translate();
                case AdditionalInfoCategory.DLCFeatures:
                    return "RimWorldAccess.StartingSite.Category.DLCFeatures".Translate();
                case AdditionalInfoCategory.Coordinates:
                    return "RimWorldAccess.StartingSite.Category.Coordinates".Translate();
                default:
                    return "RimWorldAccess.StartingSite.Category.Unknown".Translate();
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
                    return "RimWorldAccess.StartingSite.NoInfoAvailable".Translate();
            }
        }

        // =============================================
        // Private helpers - Info categories
        // =============================================

        private static string GetRoadsAndRiversInfo(PlanetTile tile)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("RimWorldAccess.StartingSite.RoadsRivers.Header".Translate());

            Tile tileData = tile.Tile;
            if (tileData is SurfaceTile surfaceTile)
            {
                if (surfaceTile.Roads != null && surfaceTile.Roads.Count > 0)
                {
                    string roads = string.Join(", ", surfaceTile.Roads.Select(r => r.road.label).Distinct());
                    sb.AppendLine("RimWorldAccess.StartingSite.RoadsRivers.RoadsList".Translate(roads));
                }
                else
                {
                    sb.AppendLine("RimWorldAccess.StartingSite.RoadsRivers.RoadsNone".Translate());
                }

                if (surfaceTile.Rivers != null && surfaceTile.Rivers.Count > 0)
                {
                    var largestRiver = surfaceTile.Rivers.MaxBy(r => r.river.degradeThreshold);
                    sb.AppendLine("RimWorldAccess.StartingSite.RoadsRivers.River".Translate(largestRiver.river.LabelCap));
                }
                else
                {
                    sb.AppendLine("RimWorldAccess.StartingSite.RoadsRivers.RiverNone".Translate());
                }
            }

            Rot4 coastDirection = Find.World.CoastDirectionAt(tile);
            if (coastDirection != Rot4.Invalid)
            {
                sb.AppendLine("RimWorldAccess.StartingSite.RoadsRivers.CoastalYes".Translate(coastDirection.ToString()));
            }
            else
            {
                sb.AppendLine("RimWorldAccess.StartingSite.RoadsRivers.CoastalNo".Translate());
            }

            return sb.ToString().TrimEnd();
        }

        private static string GetStoneTypesInfo(PlanetTile tile)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("RimWorldAccess.StartingSite.Stone.Header".Translate());

            var stoneTypes = Find.World.NaturalRockTypesIn(tile);
            if (stoneTypes != null && stoneTypes.Any())
            {
                string stones = string.Join(", ", stoneTypes.Select(s => s.label));
                sb.AppendLine(stones);
            }
            else
            {
                sb.AppendLine("RimWorldAccess.StartingSite.NoStoneInfo".Translate());
            }

            return sb.ToString().TrimEnd();
        }

        private static string GetDLCFeaturesInfo(PlanetTile tile)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("RimWorldAccess.StartingSite.Dlc.Header".Translate());

            Tile tileData = tile.Tile;
            bool hasAnyInfo = false;

            if (ModsConfig.BiotechActive)
            {
                float pollution = tileData.pollution;
                sb.AppendLine("RimWorldAccess.StartingSite.Dlc.Pollution".Translate(pollution.ToStringPercent()));

                float nearbyPollution = WorldPollutionUtility.CalculateNearbyPollutionScore(tile.tileId);
                if (nearbyPollution >= GameConditionDefOf.NoxiousHaze.minNearbyPollution)
                {
                    float hazeInterval = GameConditionDefOf.NoxiousHaze.mtbOverNearbyPollutionCurve.Evaluate(nearbyPollution);
                    sb.AppendLine("RimWorldAccess.StartingSite.Dlc.NoxiousHazeEvery".Translate(hazeInterval.ToString("F1")));
                }
                else
                {
                    sb.AppendLine("RimWorldAccess.StartingSite.Dlc.NoxiousHazeNone".Translate());
                }

                hasAnyInfo = true;
            }

            if (tileData.Landmark != null)
            {
                sb.AppendLine("RimWorldAccess.StartingSite.Dlc.Landmark".Translate(tileData.Landmark.name));
                hasAnyInfo = true;
            }

            if (tileData.Mutators != null && tileData.Mutators.Count > 0)
            {
                sb.AppendLine("RimWorldAccess.StartingSite.Dlc.TileMutators".Translate(tileData.Mutators.Count));
                foreach (var mutator in tileData.Mutators)
                {
                    sb.AppendLine("RimWorldAccess.StartingSite.Dlc.MutatorBullet".Translate(mutator.label));
                }
                hasAnyInfo = true;
            }

            if (!hasAnyInfo)
            {
                sb.AppendLine("RimWorldAccess.StartingSite.NoDlcFeatures".Translate());
            }

            return sb.ToString().TrimEnd();
        }
    }
}
