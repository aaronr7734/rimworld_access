using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper class for extracting information about world tiles and settlements.
    /// Used by WorldNavigationState and SettlementBrowserState.
    /// </summary>
    public static class WorldInfoHelper
    {
        /// <summary>
        /// Gets a brief summary of a world tile (for navigation announcements).
        /// </summary>
        public static string GetTileSummary(PlanetTile planetTile)
        {
            if (!planetTile.Valid || Find.WorldGrid == null)
                return "Invalid tile";

            Tile tile = planetTile.Tile;
            if (tile == null)
                return "Unknown tile";

            StringBuilder summary = new StringBuilder();

            // Check for route planner waypoint at this tile FIRST (before everything else)
            if (Find.WorldRoutePlanner != null && Find.WorldRoutePlanner.Active)
            {
                for (int i = 0; i < Find.WorldRoutePlanner.waypoints.Count; i++)
                {
                    if (Find.WorldRoutePlanner.waypoints[i].Tile == planetTile)
                    {
                        summary.Append($"Waypoint {i + 1}. ");
                        break;
                    }
                }
            }

            // Add biome
            if (tile.PrimaryBiome != null)
            {
                summary.Append(tile.PrimaryBiome.LabelCap);
            }

            // Add hilliness
            if (tile.hilliness != Hilliness.Impassable && tile.hilliness != Hilliness.Undefined)
            {
                summary.Append($", {tile.hilliness.GetLabelCap()}");
            }

            // Add temperature (average)
            float temp = tile.temperature;
            summary.Append($", {temp:F0}°C");

            // Check for world objects at this tile
            if (Find.WorldObjects != null)
            {
                List<WorldObject> objectsAtTile = Find.WorldObjects.ObjectsAt(planetTile).ToList();

                if (objectsAtTile.Count > 0)
                {
                    // Prioritize settlements
                    Settlement settlement = objectsAtTile.OfType<Settlement>().FirstOrDefault();
                    if (settlement != null)
                    {
                        summary.Append($", {settlement.Label}");

                        // Add faction info - only what's visible on the world map tooltip
                        if (settlement.Faction != null)
                        {
                            if (settlement.Faction == Faction.OfPlayer)
                            {
                                summary.Append(" (Player colony)");
                            }
                            else
                            {
                                // Faction name
                                summary.Append($" ({settlement.Faction.Name}");

                                // Relationship with goodwill (visible on inspect pane)
                                string relationship = settlement.Faction.HostileTo(Faction.OfPlayer) ? "Hostile" :
                                                     settlement.Faction.PlayerRelationKind.GetLabelCap();
                                int goodwill = settlement.Faction.PlayerGoodwill;
                                string goodwillStr = goodwill >= 0 ? $"+{goodwill}" : goodwill.ToString();
                                summary.Append($", {relationship} {goodwillStr}");

                                summary.Append(")");

                                // Title required for trading (shown on inspect pane for Empire)
                                if (settlement.TraderKind != null)
                                {
                                    RoyalTitleDef titleRequired = settlement.TraderKind.TitleRequiredToTrade;
                                    if (titleRequired != null)
                                    {
                                        summary.Append($". Requires {titleRequired.GetLabelCapForBothGenders()} title to trade");
                                    }
                                }
                            }
                        }
                    }

                    // Check for caravans on this tile (regardless of settlement presence)
                    Caravan caravan = objectsAtTile.OfType<Caravan>().FirstOrDefault();
                    if (caravan != null)
                    {
                        summary.Append($", {caravan.Label}");

                        // Add faction info for caravan
                        if (caravan.Faction != null && caravan.Faction != Faction.OfPlayer)
                        {
                            summary.Append($" ({caravan.Faction.Name})");
                        }
                    }

                    // If no settlement or caravan, list other world objects (like sites)
                    if (settlement == null && caravan == null)
                    {
                        WorldObject firstObject = objectsAtTile.FirstOrDefault();
                        if (firstObject != null)
                        {
                            summary.Append($", {firstObject.Label}");
                        }
                    }
                }
            }

            // Add quest information for this tile (includes difficulty and description)
            string questInfo = GetQuestInfoForTile(planetTile);
            if (!string.IsNullOrEmpty(questInfo))
            {
                summary.Append($". {questInfo}");
            }

            return summary.ToString();
        }

        /// <summary>
        /// Gets detailed information about a world tile (for I key).
        /// </summary>
        public static string GetDetailedTileInfo(PlanetTile planetTile)
        {
            if (!planetTile.Valid || Find.WorldGrid == null)
                return "Invalid tile";

            Tile tile = planetTile.Tile;
            if (tile == null)
                return "Unknown tile";

            StringBuilder info = new StringBuilder();

            // Coordinates
            Vector2 longlat = Find.WorldGrid.LongLatOf(planetTile);
            info.AppendLine($"Coordinates: {longlat.y:F2}° N, {longlat.x:F2}° E");

            // Biome
            if (tile.PrimaryBiome != null)
            {
                info.AppendLine($"Biome: {tile.PrimaryBiome.LabelCap}");
            }

            // Hilliness
            if (tile.hilliness != Hilliness.Undefined)
            {
                info.AppendLine($"Hilliness: {tile.hilliness.GetLabelCap()}");
            }

            // Elevation
            info.AppendLine($"Elevation: {tile.elevation:F0}m");

            // Temperature
            info.AppendLine($"Temperature: Average {tile.temperature:F0}°C");

            // Pollution (if Biotech active)
            if (ModsConfig.BiotechActive && tile.pollution > 0)
            {
                info.AppendLine($"Pollution: {tile.pollution:F0}%");
            }

            // World objects at this tile
            if (Find.WorldObjects != null)
            {
                List<WorldObject> objectsAtTile = Find.WorldObjects.ObjectsAt(planetTile).ToList();

                if (objectsAtTile.Count > 0)
                {
                    info.AppendLine("\nWorld Objects:");

                    foreach (WorldObject obj in objectsAtTile)
                    {
                        info.Append($"  - {obj.Label}");

                        // Add type-specific information
                        if (obj is Settlement settlement)
                        {
                            if (settlement.Faction != null)
                            {
                                if (settlement.Faction == Faction.OfPlayer)
                                {
                                    info.Append(" (Player colony)");
                                }
                                else
                                {
                                    // Only show what's visible on the world map inspect pane
                                    string relationship = settlement.Faction.HostileTo(Faction.OfPlayer) ? "Hostile" :
                                                         settlement.Faction.PlayerRelationKind.GetLabelCap();
                                    int goodwill = settlement.Faction.PlayerGoodwill;
                                    string goodwillStr = goodwill >= 0 ? $"+{goodwill}" : goodwill.ToString();
                                    info.AppendLine();
                                    info.AppendLine($"    Faction: {settlement.Faction.Name}");
                                    info.AppendLine($"    Relationship: {relationship} ({goodwillStr})");

                                    // Title required for trading (shown on inspect pane for Empire)
                                    if (settlement.TraderKind != null)
                                    {
                                        RoyalTitleDef titleRequired = settlement.TraderKind.TitleRequiredToTrade;
                                        if (titleRequired != null)
                                        {
                                            info.Append($"    Requires {titleRequired.GetLabelCapForBothGenders()} title to trade");
                                        }
                                    }
                                }
                            }
                        }
                        else if (obj is Caravan caravan)
                        {
                            if (caravan.Faction != null)
                            {
                                info.Append($" (Caravan, {caravan.Faction.Name})");
                            }
                        }
                        else if (obj is Site site)
                        {
                            info.Append(" (Site)");
                        }

                        info.AppendLine();
                    }
                }
            }

            // Add quest information
            string questInfo = GetDetailedQuestInfoForTile(planetTile);
            if (!string.IsNullOrEmpty(questInfo))
            {
                info.AppendLine("\nQuest Targets:");
                info.Append(questInfo);
            }

            return info.ToString().TrimEnd();
        }

        /// <summary>
        /// Gets information about a specific settlement.
        /// </summary>
        public static string GetSettlementInfo(Settlement settlement)
        {
            if (settlement == null)
                return "No settlement";

            StringBuilder info = new StringBuilder();

            info.AppendLine($"Settlement: {settlement.Label}");

            if (settlement.Faction != null)
            {
                info.AppendLine($"Faction: {settlement.Faction.Name}");

                if (settlement.Faction == Faction.OfPlayer)
                {
                    info.AppendLine("Relationship: Player Colony");
                }
                else
                {
                    string relationship = settlement.Faction.HostileTo(Faction.OfPlayer) ? "Hostile" :
                                         settlement.Faction.PlayerRelationKind.GetLabel();
                    info.AppendLine($"Relationship: {relationship}");

                    // Add goodwill
                    int goodwill = settlement.Faction.PlayerGoodwill;
                    info.AppendLine($"Goodwill: {goodwill}");
                }
            }

            // Add visitable/attackable status
            if (settlement.Visitable)
            {
                info.AppendLine("Status: Visitable");
            }

            if (settlement.Attackable)
            {
                info.AppendLine("Status: Attackable");
            }

            return info.ToString().TrimEnd();
        }

        /// <summary>
        /// Gets a list of all settlements sorted by distance from a given tile.
        /// </summary>
        public static List<Settlement> GetSettlementsByDistance(PlanetTile fromTile)
        {
            if (!fromTile.Valid || Find.WorldObjects?.Settlements == null || Find.WorldGrid == null)
                return new List<Settlement>();

            return Find.WorldObjects.Settlements
                .OrderBy(s => Find.WorldGrid.ApproxDistanceInTiles(fromTile, s.Tile))
                .ToList();
        }

        /// <summary>
        /// Gets the player's home settlement (first player settlement found).
        /// </summary>
        public static Settlement GetPlayerHomeSettlement()
        {
            if (Find.WorldObjects?.Settlements == null)
                return null;

            return Find.WorldObjects.Settlements
                .FirstOrDefault(s => s.Faction == Faction.OfPlayer);
        }

        /// <summary>
        /// Gets all player caravans.
        /// </summary>
        public static List<Caravan> GetPlayerCaravans()
        {
            if (Find.WorldObjects?.Caravans == null)
                return new List<Caravan>();

            return Find.WorldObjects.Caravans
                .Where(c => c.Faction == Faction.OfPlayer)
                .ToList();
        }

        /// <summary>
        /// Gets quest information for a tile (if any quests target this tile).
        /// Includes difficulty and brief description for direct tile announcements.
        /// </summary>
        public static string GetQuestInfoForTile(PlanetTile planetTile)
        {
            if (!planetTile.Valid || Find.QuestManager == null)
                return null;

            List<string> questInfos = new List<string>();

            var activeQuests = Find.QuestManager.questsInDisplayOrder
                .Where(q => q.State == QuestState.Ongoing && !q.hidden && !q.hiddenInUI)
                .ToList();

            foreach (Quest quest in activeQuests)
            {
                bool isQuestTarget = false;

                foreach (GlobalTargetInfo target in quest.QuestLookTargets)
                {
                    if (!target.IsValid || !target.IsWorldTarget)
                        continue;

                    PlanetTile targetTile = PlanetTile.Invalid;

                    if (target.HasWorldObject && target.WorldObject != null)
                    {
                        targetTile = target.WorldObject.Tile;
                    }
                    else if (target.Tile.Valid)
                    {
                        targetTile = target.Tile;
                    }

                    if (targetTile.Valid && targetTile == planetTile)
                    {
                        isQuestTarget = true;
                        break;
                    }
                }

                if (isQuestTarget)
                {
                    string questName = quest.name.StripTags();
                    StringBuilder questEntry = new StringBuilder();
                    questEntry.Append($"Quest: {questName}");

                    // Add difficulty rating as text (avoid unicode symbols that screen readers may not handle)
                    if (quest.challengeRating > 0)
                    {
                        string difficulty = quest.challengeRating == 1 ? "1 star" : $"{quest.challengeRating} stars";
                        questEntry.Append($" ({difficulty})");
                    }

                    // Add description (first two sentences or up to 250 chars)
                    string questDesc = quest.description.ToString().StripTags();
                    if (!string.IsNullOrEmpty(questDesc))
                    {
                        // Find second sentence end or use first 250 characters
                        int firstPeriod = questDesc.IndexOf('.');
                        int secondPeriod = firstPeriod > 0 ? questDesc.IndexOf('.', firstPeriod + 1) : -1;

                        if (secondPeriod > 0 && secondPeriod < 250)
                        {
                            questDesc = questDesc.Substring(0, secondPeriod + 1);
                        }
                        else if (firstPeriod > 0 && firstPeriod < 250)
                        {
                            questDesc = questDesc.Substring(0, firstPeriod + 1);
                        }
                        else if (questDesc.Length > 250)
                        {
                            questDesc = questDesc.Substring(0, 247) + "...";
                        }
                        questEntry.Append($" - {questDesc}");
                    }

                    questInfos.Add(questEntry.ToString());
                }
            }

            if (questInfos.Count == 0)
                return null;

            return string.Join(" | ", questInfos);
        }

        /// <summary>
        /// Gets detailed quest information for a tile (for the I key detailed view).
        /// </summary>
        public static string GetDetailedQuestInfoForTile(PlanetTile planetTile)
        {
            if (!planetTile.Valid || Find.QuestManager == null)
                return null;

            StringBuilder info = new StringBuilder();

            var activeQuests = Find.QuestManager.questsInDisplayOrder
                .Where(q => q.State == QuestState.Ongoing && !q.hidden && !q.hiddenInUI)
                .ToList();

            foreach (Quest quest in activeQuests)
            {
                bool isQuestTarget = false;

                foreach (GlobalTargetInfo target in quest.QuestLookTargets)
                {
                    if (!target.IsValid || !target.IsWorldTarget)
                        continue;

                    PlanetTile targetTile = PlanetTile.Invalid;

                    if (target.HasWorldObject && target.WorldObject != null)
                    {
                        targetTile = target.WorldObject.Tile;
                    }
                    else if (target.Tile.Valid)
                    {
                        targetTile = target.Tile;
                    }

                    if (targetTile.Valid && targetTile == planetTile)
                    {
                        isQuestTarget = true;
                        break;
                    }
                }

                if (isQuestTarget)
                {
                    string questName = quest.name.StripTags();
                    string questDesc = quest.description.ToString().StripTags();

                    info.AppendLine($"Quest: {questName}");

                    // Add challenge rating if available (use text instead of unicode symbols)
                    if (quest.challengeRating > 0)
                    {
                        string difficulty = quest.challengeRating == 1 ? "1 star" : $"{quest.challengeRating} stars";
                        info.AppendLine($"  Difficulty: {difficulty}");
                    }

                    // Add description (truncate if too long)
                    if (!string.IsNullOrEmpty(questDesc))
                    {
                        if (questDesc.Length > 200)
                            questDesc = questDesc.Substring(0, 197) + "...";
                        info.AppendLine($"  Description: {questDesc}");
                    }

                    info.AppendLine();
                }
            }

            if (info.Length == 0)
                return null;

            return info.ToString().TrimEnd();
        }
        #region Number Key Tile Info (Keys 1-5)

        /// <summary>
        /// Key 1: Growing and Food information.
        /// Growing period, forageability, grazing, rainfall.
        /// </summary>
        public static string GetTileGrowingInfo(PlanetTile planetTile)
        {
            if (!planetTile.Valid || Find.WorldGrid == null)
                return "Invalid tile";

            Tile tile = planetTile.Tile;
            if (tile == null)
                return "Unknown tile";

            StringBuilder info = new StringBuilder();

            // Growing period - use game's calculation
            string growingPeriod = Zone_Growing.GrowingQuadrumsDescription(planetTile);
            info.Append($"Growing period: {growingPeriod}.");

            // Rainfall
            info.Append($" Rainfall: {tile.rainfall:F0} mm.");

            // Forageability
            if (tile.PrimaryBiome?.foragedFood != null && tile.PrimaryBiome.forageability > 0f)
            {
                info.Append($" Forageability: {tile.PrimaryBiome.forageability.ToStringPercent()} ({tile.PrimaryBiome.foragedFood.label}).");
            }
            else
            {
                info.Append(" Forageability: 0%.");
            }

            // Grazing (animals can graze now)
            bool canGraze = VirtualPlantsUtility.EnvironmentAllowsEatingVirtualPlantsNowAt(planetTile);
            info.Append($" Animals can graze: {(canGraze ? "yes" : "no")}.");

            return info.ToString();
        }

        /// <summary>
        /// Key 2: Movement and Terrain information.
        /// Movement difficulty, winter penalty, roads, rivers, stone types, elevation.
        /// </summary>
        public static string GetTileMovementInfo(PlanetTile planetTile)
        {
            if (!planetTile.Valid || Find.WorldGrid == null)
                return "Invalid tile";

            Tile tile = planetTile.Tile;
            if (tile == null)
                return "Unknown tile";

            StringBuilder info = new StringBuilder();

            // Movement difficulty
            if (Find.World.Impassable(planetTile))
            {
                info.Append("Movement: Impassable.");
            }
            else
            {
                float difficulty = WorldPathGrid.CalculatedMovementDifficultyAt(planetTile, false, null, null);
                float roadMultiplier = Find.WorldGrid.GetRoadMovementDifficultyMultiplier(planetTile, PlanetTile.Invalid, null);
                float totalDifficulty = difficulty * roadMultiplier;
                info.Append($"Movement difficulty: {totalDifficulty:F1}.");

                // Winter penalty
                if (WorldPathGrid.WillWinterEverAffectMovementDifficulty(planetTile))
                {
                    float currentWinterOffset = WorldPathGrid.GetCurrentWinterMovementDifficultyOffset(planetTile);
                    if (currentWinterOffset > 0)
                    {
                        info.Append($" Current winter penalty: +{currentWinterOffset:F1}.");
                    }
                    else
                    {
                        info.Append(" Winter penalty: +2.0 in winter.");
                    }
                }
            }

            // Terrain/Hilliness
            if (tile.HillinessLabel != Hilliness.Undefined)
            {
                info.Append($" Terrain: {tile.HillinessLabel.GetLabelCap()}.");
            }

            // Elevation
            info.Append($" Elevation: {tile.elevation:F0} m.");

            // Roads and Rivers (only for surface tiles)
            if (tile is SurfaceTile surfaceTile)
            {
                if (surfaceTile.Roads != null && surfaceTile.Roads.Count > 0)
                {
                    string roads = surfaceTile.Roads
                        .Select(r => r.road.label)
                        .Distinct()
                        .ToCommaList(useAnd: true);
                    info.Append($" Road: {roads.CapitalizeFirst()}.");
                }

                if (surfaceTile.Rivers != null && surfaceTile.Rivers.Count > 0)
                {
                    var largestRiver = surfaceTile.Rivers.MaxBy(r => r.river.degradeThreshold);
                    info.Append($" River: {largestRiver.river.LabelCap}.");
                }
            }

            // Stone types (if can build base here)
            if (tile.PrimaryBiome?.canBuildBase == true)
            {
                var stoneTypes = Find.World.NaturalRockTypesIn(planetTile)
                    .Select(rt => rt.label)
                    .ToList();
                if (stoneTypes.Count > 0)
                {
                    info.Append($" Stone types: {stoneTypes.ToCommaList(useAnd: true).CapitalizeFirst()}.");
                }
            }

            // Coastal
            if (tile.IsCoastal)
            {
                info.Append(" Coastal.");
            }

            return info.ToString();
        }

        /// <summary>
        /// Key 3: Health and Environment information.
        /// Disease frequency, pollution, noxious haze risk.
        /// </summary>
        public static string GetTileHealthInfo(PlanetTile planetTile)
        {
            if (!planetTile.Valid || Find.WorldGrid == null)
                return "Invalid tile";

            Tile tile = planetTile.Tile;
            if (tile == null)
                return "Unknown tile";

            StringBuilder info = new StringBuilder();

            // Disease frequency
            if (tile.PrimaryBiome?.diseaseMtbDays > 0)
            {
                float diseasesPerYear = 60f / tile.PrimaryBiome.diseaseMtbDays;
                info.Append($"Disease frequency: {diseasesPerYear:F1} per year.");
            }
            else
            {
                info.Append("Disease frequency: None.");
            }

            // Pollution (Biotech DLC)
            if (ModsConfig.BiotechActive)
            {
                info.Append($" Tile pollution: {tile.pollution.ToStringPercent()}.");

                // Nearby pollution score
                float nearbyPollution = WorldPollutionUtility.CalculateNearbyPollutionScore(planetTile);
                info.Append($" Nearby pollution: {nearbyPollution:F2}.");

                // Noxious haze risk
                if (nearbyPollution >= GameConditionDefOf.NoxiousHaze.minNearbyPollution)
                {
                    float hazeInterval = GameConditionDefOf.NoxiousHaze.mtbOverNearbyPollutionCurve.Evaluate(nearbyPollution);
                    info.Append($" Noxious haze interval: {hazeInterval:F0} days.");
                }
                else
                {
                    info.Append(" Noxious haze: Never.");
                }
            }

            return info.ToString();
        }

        /// <summary>
        /// Key 4: Location information.
        /// Coordinates, time zone, tile ID.
        /// </summary>
        public static string GetTileLocationInfo(PlanetTile planetTile)
        {
            if (!planetTile.Valid || Find.WorldGrid == null)
                return "Invalid tile";

            StringBuilder info = new StringBuilder();

            // Coordinates
            Vector2 longlat = Find.WorldGrid.LongLatOf(planetTile);
            string latDir = longlat.y >= 0 ? "N" : "S";
            string lonDir = longlat.x >= 0 ? "E" : "W";
            info.Append($"Coordinates: {Mathf.Abs(longlat.y):F1} degrees {latDir}, {Mathf.Abs(longlat.x):F1} degrees {lonDir}.");

            // Time zone
            int timeZone = GenDate.TimeZoneAt(longlat.x);
            string tzStr = timeZone >= 0 ? $"+{timeZone}" : timeZone.ToString();
            info.Append($" Time zone: UTC{tzStr}.");

            // Tile ID (useful for debugging/reporting)
            info.Append($" Tile ID: {planetTile}.");

            return info.ToString();
        }

        /// <summary>
        /// Key 5: Tile Features and DLC information.
        /// Mutators (Odyssey), landmarks (Odyssey), caves.
        /// </summary>
        public static string GetTileFeaturesInfo(PlanetTile planetTile)
        {
            if (!planetTile.Valid || Find.WorldGrid == null)
                return "Invalid tile";

            Tile tile = planetTile.Tile;
            if (tile == null)
                return "Unknown tile";

            StringBuilder info = new StringBuilder();
            bool hasContent = false;

            // Mutators (Odyssey DLC)
            if (tile.Mutators.Any())
            {
                var mutatorLabels = tile.Mutators
                    .OrderByDescending(m => m.displayPriority)
                    .Select(m => m.Label(planetTile))
                    .ToList();
                info.Append($"Tile features: {mutatorLabels.ToCommaList().CapitalizeFirst()}.");
                hasContent = true;
            }

            // Landmarks (Odyssey DLC)
            if (ModsConfig.OdysseyActive && tile.Landmark != null)
            {
                if (hasContent) info.Append(" ");
                info.Append($"Landmark: {tile.Landmark.name}.");
                hasContent = true;
            }

            // World feature (e.g., part of a named region)
            if (tile.feature != null)
            {
                if (hasContent) info.Append(" ");
                info.Append($"Region: {tile.feature.name}.");
                hasContent = true;
            }

            // Check for caves using game's method
            bool hasCaves = Find.World.HasCaves(planetTile);
            if (hasCaves)
            {
                if (hasContent) info.Append(" ");
                info.Append("May have caves.");
                hasContent = true;
            }

            if (!hasContent)
            {
                info.Append("No special features.");
            }

            return info.ToString();
        }

        #endregion
    }
}
