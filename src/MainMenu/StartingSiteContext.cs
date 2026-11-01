using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Categories for the starting-site screen's "Tile info" region.</summary>
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
    /// World-gen starting-site features: the Tile info region's categories, random tile selection,
    /// change-only faction proximity warnings, and tile validation for settlement placement. <see cref="TileInfoRowCount"/>,
    /// <see cref="TileInfoRowName"/> and <see cref="TileInfoRowDetail"/> expose the categories as an
    /// ordinary read-only content region; <see cref="StartingSiteScreenScope"/> owns the row cursor
    /// and announcement grammar.
    /// </summary>
    public static class StartingSiteContext
    {
        private static List<AdditionalInfoCategory> availableMenuItems = new List<AdditionalInfoCategory>();

        public static void Open()
        {
            availableMenuItems.Clear();
        }

        public static void Close()
        {
            availableMenuItems.Clear();
        }

        // Tile info region.

        /// <summary>Row count for the current tile; call after <see cref="PopulateMenuItems"/> rebuilds the list.</summary>
        internal static int TileInfoRowCount => availableMenuItems.Count;

        internal static string TileInfoRowName(int index)
        {
            return GetMenuItemName(availableMenuItems[index]);
        }

        /// <summary>Resolves against the live selected tile, at most once per keystroke.</summary>
        internal static string TileInfoRowDetail(int index)
        {
            PlanetTile tile = WorldNavigationState.CurrentSelectedTile;
            if (!tile.Valid)
                return "RimWorldAccess.StartingSite.NoInfoAvailable".Translate();
            return GetDetailedInfoForCategory(tile, availableMenuItems[index]);
        }

        // Random tile selection.

        public static void SelectRandomTile()
        {
            // MUTATION-C: mirrors Page_SelectStartingSite's "SelectRandomSite" button
            // body verbatim (Page_SelectStartingSite.cs:245-256), INCLUDING the Odyssey
            // branch -- the button has a 50% chance to prefer a landmark settlement tile
            // over a plain random starting tile once Odyssey is active. Calling
            // TileFinder.RandomStartingTile() alone would silently drop that branch for
            // every Odyssey player using the R key or the Buttons-toolbar row.
            PlanetTile randomTile;
            if (ModsConfig.OdysseyActive && Rand.Bool)
            {
                randomTile = TileFinder.RandomSettlementTileFor(Find.WorldGrid.Surface, Faction.OfPlayer,
                    mustBeAutoChoosable: true, (PlanetTile x) => x.Tile.Landmark != null);
            }
            else
            {
                randomTile = TileFinder.RandomStartingTile();
            }
            if (!randomTile.Valid)
            {
                TolkHelper.Speak("RimWorldAccess.StartingSite.RandomSiteFailed".Loc(), SpeechPriority.High);
                return;
            }

            // Update shared navigation state
            // MUTATION-C: mirrors Page_SelectStartingSite's "SelectRandomSite" button
            // (Page_SelectStartingSite.cs:245-256) and DoWindowContents' own per-frame sync
            // (Page_SelectStartingSite.cs:149-152) -- the latter genuinely runs live here
            // too, since StartingSitePatch's Harmony patch on DoWindowContents is
            // Prefix/Postfix-only and lets vanilla's body execute every frame, so this
            // write is a same-frame duplicate, not a bypass.
            WorldNavigationState.CurrentSelectedTile = randomTile;
            Find.GameInitData.startingTile = randomTile;
            Find.WorldInterface.SelectedTile = randomTile;
            Find.WorldCameraDriver.JumpTo(Find.WorldGrid.GetTileCenter(randomTile));

            string tileInfo = WorldInfoHelper.GetTileSummary(randomTile, includeRouteInfo: false);

            string factionWarning = GetFactionProximityWarning(randomTile);
            if (!string.IsNullOrEmpty(factionWarning))
            {
                tileInfo += $". {factionWarning}";
            }

            string biomeDesc = BiomeDescriptionTracker.GetBiomeDescriptionIfNew(randomTile);
            if (!string.IsNullOrEmpty(biomeDesc))
            {
                tileInfo += $". {biomeDesc}";
            }

            TolkHelper.Speak("RimWorldAccess.StartingSite.RandomSiteSelected".Loc(tileInfo));
        }

        // Faction proximity warnings.

        /// <summary>The current faction proximity warning for a tile.</summary>
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

        // Private helpers: tile info.

        internal static void PopulateMenuItems()
        {
            availableMenuItems.Clear();

            availableMenuItems.Add(AdditionalInfoCategory.GrowingInfo);
            availableMenuItems.Add(AdditionalInfoCategory.HealthInfo);
            availableMenuItems.Add(AdditionalInfoCategory.MovementAndLocation);
            availableMenuItems.Add(AdditionalInfoCategory.Coordinates);

            PlanetTile tile = WorldNavigationState.CurrentSelectedTile;
            if (!tile.Valid)
                return;

            Tile tileData = tile.Tile;
            if (tileData == null) return;

            if (tileData is SurfaceTile surfaceTile)
            {
                if ((surfaceTile.Roads != null && surfaceTile.Roads.Count > 0) ||
                    (surfaceTile.Rivers != null && surfaceTile.Rivers.Count > 0) ||
                    Find.World.CoastDirectionAt(tile) != Rot4.Invalid)
                {
                    availableMenuItems.Insert(0, AdditionalInfoCategory.RoadsAndRivers);
                }
            }

            if (tileData.PrimaryBiome?.canBuildBase == true)
            {
                availableMenuItems.Insert(availableMenuItems.Count > 0 ? 1 : 0, AdditionalInfoCategory.StoneTypes);
            }

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

        // Private helpers: info categories.

        private static string GetRoadsAndRiversInfo(PlanetTile tile)
        {
            var builder = new AnnouncementBuilder().DefaultSep(Separator.Period);
            builder.Add("RimWorldAccess.StartingSite.RoadsRivers.Header".Translate());

            Tile tileData = tile.Tile;
            if (tileData is SurfaceTile surfaceTile)
            {
                if (surfaceTile.Roads != null && surfaceTile.Roads.Count > 0)
                {
                    string roads = string.Join(", ", surfaceTile.Roads.Select(r => r.road.label).Distinct());
                    builder.Add("RimWorldAccess.StartingSite.RoadsRivers.RoadsList".Translate(roads));
                }
                else
                {
                    builder.Add("RimWorldAccess.StartingSite.RoadsRivers.RoadsNone".Translate());
                }

                if (surfaceTile.Rivers != null && surfaceTile.Rivers.Count > 0)
                {
                    var largestRiver = surfaceTile.Rivers.MaxBy(r => r.river.degradeThreshold);
                    builder.Add("RimWorldAccess.StartingSite.RoadsRivers.River".Translate(largestRiver.river.LabelCap));
                }
                else
                {
                    builder.Add("RimWorldAccess.StartingSite.RoadsRivers.RiverNone".Translate());
                }
            }

            Rot4 coastDirection = Find.World.CoastDirectionAt(tile);
            if (coastDirection != Rot4.Invalid)
            {
                builder.Add("RimWorldAccess.StartingSite.RoadsRivers.CoastalYes".Translate(coastDirection.ToString()));
            }
            else
            {
                builder.Add("RimWorldAccess.StartingSite.RoadsRivers.CoastalNo".Translate());
            }

            return builder.Build();
        }

        private static string GetStoneTypesInfo(PlanetTile tile)
        {
            var builder = new AnnouncementBuilder().DefaultSep(Separator.Period);
            builder.Add("RimWorldAccess.StartingSite.Stone.Header".Translate());

            var stoneTypes = Find.World.NaturalRockTypesIn(tile);
            if (stoneTypes != null && stoneTypes.Any())
            {
                string stones = string.Join(", ", stoneTypes.Select(s => s.label));
                builder.Add(stones);
            }
            else
            {
                builder.Add("RimWorldAccess.StartingSite.NoStoneInfo".Translate());
            }

            return builder.Build();
        }

        private static string GetDLCFeaturesInfo(PlanetTile tile)
        {
            var builder = new AnnouncementBuilder().DefaultSep(Separator.Period);
            builder.Add("RimWorldAccess.StartingSite.Dlc.Header".Translate());

            Tile tileData = tile.Tile;
            bool hasAnyInfo = false;

            if (ModsConfig.BiotechActive)
            {
                float pollution = tileData.pollution;
                builder.Add("RimWorldAccess.StartingSite.Dlc.Pollution".Translate(pollution.ToStringPercent()));

                float nearbyPollution = WorldPollutionUtility.CalculateNearbyPollutionScore(tile.tileId);
                if (nearbyPollution >= GameConditionDefOf.NoxiousHaze.minNearbyPollution)
                {
                    float hazeInterval = GameConditionDefOf.NoxiousHaze.mtbOverNearbyPollutionCurve.Evaluate(nearbyPollution);
                    builder.Add("RimWorldAccess.StartingSite.Dlc.NoxiousHazeEvery".Translate(hazeInterval.ToString("F1")));
                }
                else
                {
                    builder.Add("RimWorldAccess.StartingSite.Dlc.NoxiousHazeNone".Translate());
                }

                hasAnyInfo = true;
            }

            if (tileData.Landmark != null)
            {
                builder.Add("RimWorldAccess.StartingSite.Dlc.Landmark".Translate(tileData.Landmark.name));
                hasAnyInfo = true;
            }

            if (tileData.Mutators != null && tileData.Mutators.Count > 0)
            {
                builder.Add("RimWorldAccess.StartingSite.Dlc.TileMutators".Translate(tileData.Mutators.Count));
                foreach (var mutator in tileData.Mutators)
                {
                    builder.Add("RimWorldAccess.StartingSite.Dlc.MutatorBullet".Translate(mutator.label));
                }
                hasAnyInfo = true;
            }

            if (!hasAnyInfo)
            {
                builder.Add("RimWorldAccess.StartingSite.NoDlcFeatures".Translate());
            }

            return builder.Build();
        }
    }
}
