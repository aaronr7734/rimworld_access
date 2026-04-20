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
        /// <param name="planetTile">The tile to summarize.</param>
        /// <param name="includeRouteInfo">If true, includes waypoint prefix and route planner info.
        /// Set to false when called from scanner or other contexts that provide their own context.</param>
        /// <param name="minimal">If true, only includes biome, hilliness, temp, and world objects.
        /// Excludes roads, rivers, quests. Used by scanners that just need basic tile identification.</param>
        /// <param name="fuelCostInfo">Optional fuel cost info to insert right after the biome name.
        /// Used during transport pod targeting.</param>
        public static string GetTileSummary(PlanetTile planetTile, bool includeRouteInfo = true, bool minimal = false, string fuelCostInfo = null)
        {
            if (!planetTile.Valid || Find.WorldGrid == null)
                return "Invalid tile";

            Tile tile = planetTile.Tile;
            if (tile == null)
                return "Unknown tile";

            StringBuilder summary = new StringBuilder();

            // Check for route planner waypoint at this tile FIRST (before everything else)
            // Only include if caller wants route info (e.g., arrow key navigation)
            if (includeRouteInfo && Find.WorldRoutePlanner != null && Find.WorldRoutePlanner.Active)
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

            // Add landmark name (Odyssey DLC) - sighted players see this in inspect pane header
            if (ModsConfig.OdysseyActive && tile.Landmark != null)
            {
                summary.Append($", {tile.Landmark.name}");
            }

            // Add fuel cost right after biome (for transport pod targeting)
            if (!string.IsNullOrEmpty(fuelCostInfo))
            {
                summary.Append($", {fuelCostInfo}");
            }

            // Add hilliness and temperature (surface layers only - orbit tiles have default/meaningless values)
            bool isSpaceLayer = planetTile.LayerDef?.isSpace == true;
            if (!isSpaceLayer)
            {
                if (tile.hilliness != Hilliness.Impassable && tile.hilliness != Hilliness.Undefined)
                {
                    summary.Append($", {tile.hilliness.GetLabelCap()}");
                }

                // Add temperature (average, respects user's temperature mode preference)
                summary.Append($", {MenuHelper.FormatTemperature(tile.temperature, "F0")}");
            }

            // Check for world objects at this tile (excluding route planner waypoints - we handle those separately above)
            if (Find.WorldObjects != null)
            {
                List<WorldObject> objectsAtTile = Find.WorldObjects.ObjectsAt(planetTile)
                    .Where(obj => !(obj is RoutePlannerWaypoint))
                    .ToList();

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
                                summary.Append($" ({Faction.OfPlayer.Name})");
                            }
                            else
                            {
                                // Faction name
                                summary.Append($" ({settlement.Faction.Name}");

                                // Relationship with goodwill (visible on inspect pane)
                                string relationship = settlement.Faction.PlayerRelationKind.GetLabelCap();
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
                    // List all caravans with natural language formatting and status
                    var caravans = objectsAtTile.OfType<Caravan>().ToList();
                    if (caravans.Count > 0)
                    {
                        summary.Append(". ");
                        if (caravans.Count == 1)
                        {
                            var caravan = caravans[0];
                            summary.Append($"{caravan.Label}");

                            // Add faction for non-player caravans, or status for player caravans
                            if (caravan.Faction != null && caravan.Faction != Faction.OfPlayer)
                            {
                                summary.Append($" ({caravan.Faction.Name})");
                            }
                            else
                            {
                                string shortStatus = GetCaravanShortStatus(caravan);
                                if (!string.IsNullOrEmpty(shortStatus))
                                {
                                    summary.Append($" ({shortStatus})");
                                }
                            }
                            summary.Append(" is here");
                        }
                        else
                        {
                            // Multiple caravans - format naturally: "A (status), B (status), and C (status) are here"
                            for (int i = 0; i < caravans.Count; i++)
                            {
                                var caravan = caravans[i];
                                summary.Append(caravan.Label);

                                // Add faction for non-player caravans, or status for player caravans
                                if (caravan.Faction != null && caravan.Faction != Faction.OfPlayer)
                                {
                                    summary.Append($" ({caravan.Faction.Name})");
                                }
                                else
                                {
                                    string shortStatus = GetCaravanShortStatus(caravan);
                                    if (!string.IsNullOrEmpty(shortStatus))
                                    {
                                        summary.Append($" ({shortStatus})");
                                    }
                                }

                                if (i < caravans.Count - 2)
                                {
                                    summary.Append(", ");
                                }
                                else if (i == caravans.Count - 2)
                                {
                                    summary.Append(" and ");
                                }
                            }
                            summary.Append(" are here");
                        }
                    }

                    // If no settlement or caravan, list other world objects (like sites)
                    if (settlement == null && caravans.Count == 0)
                    {
                        WorldObject firstObject = objectsAtTile.FirstOrDefault();
                        if (firstObject != null)
                        {
                            summary.Append($", {firstObject.Label}");
                        }
                    }
                }
            }

            // Add road/river direction info for navigation (skip in minimal mode)
            if (!minimal && tile is SurfaceTile surfaceTile)
            {
                // Road direction with name
                if (surfaceTile.Roads != null && surfaceTile.Roads.Count > 0)
                {
                    string roadName = surfaceTile.Roads.First().road.label;
                    string roadDir = GetRoadDirectionDescription(planetTile, surfaceTile.Roads);
                    if (!string.IsNullOrEmpty(roadDir))
                    {
                        summary.Append($". {roadName.CapitalizeFirst()} {roadDir.TrimEnd('.').ToLower()}");
                    }
                }

                // River direction
                if (surfaceTile.Rivers != null && surfaceTile.Rivers.Count > 0)
                {
                    string riverDir = GetRiverDirectionDescription(planetTile, surfaceTile.Rivers);
                    if (!string.IsNullOrEmpty(riverDir))
                    {
                        summary.Append($". River {riverDir.TrimEnd('.').ToLower()}");
                    }
                }
            }

            // Add quest information for this tile (skip in minimal mode)
            if (!minimal)
            {
                string questInfo = GetQuestInfoForTile(planetTile);
                if (!string.IsNullOrEmpty(questInfo))
                {
                    summary.Append($". {questInfo}");
                }
            }

            // Add route planner path info at the END (when on a planned route)
            // Only include if caller wants route info (e.g., arrow key navigation)
            if (includeRouteInfo)
            {
                string routeInfo = RoutePlannerState.GetRouteAnnouncement(planetTile);
                if (!string.IsNullOrEmpty(routeInfo))
                {
                    summary.Append($". {routeInfo}");
                }
                else
                {
                    // Only show caravan path info when route planner is NOT active
                    // (route planner has its own path display)
                    string caravanPathInfo = GetCaravanPathAnnouncement(planetTile);
                    if (!string.IsNullOrEmpty(caravanPathInfo))
                    {
                        summary.Append($". {caravanPathInfo}");
                    }
                }
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

            // Surface-only data (orbit tiles have default/meaningless values for these)
            bool isSpaceLayer = planetTile.LayerDef?.isSpace == true;
            if (!isSpaceLayer)
            {
                // Hilliness
                if (tile.hilliness != Hilliness.Undefined)
                {
                    info.AppendLine($"Hilliness: {tile.hilliness.GetLabelCap()}");
                }

                // Elevation
                info.AppendLine($"Elevation: {tile.elevation:F0}m");

                // Temperature (respects user's temperature mode preference)
                info.AppendLine($"Temperature: Average {MenuHelper.FormatTemperature(tile.temperature, "F0")}");

                // Pollution (if Biotech active)
                if (ModsConfig.BiotechActive && tile.pollution > 0)
                {
                    info.AppendLine($"Pollution: {tile.pollution:F0}%");
                }
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
                                    info.Append($" ({Faction.OfPlayer.Name})");
                                }
                                else
                                {
                                    // Only show what's visible on the world map inspect pane
                                    string relationship = settlement.Faction.PlayerRelationKind.GetLabelCap();
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
        /// Gets a concise status string for a caravan (for cycle announcements).
        /// Shows current activity first, then destination if applicable.
        /// Examples: "traveling to Hilltown", "resting (2 bedrolls), heading to Hilltown", "paused, heading to Hilltown"
        /// </summary>
        public static string GetCaravanStatus(Caravan caravan)
        {
            if (caravan == null)
                return "unknown";

            // Check problem states first (these are most important)
            if (caravan.AllOwnersDowned)
                return "all downed";

            if (caravan.AllOwnersHaveMentalBreak)
                return "mental break";

            if (caravan.ImmobilizedByMass)
                return "overloaded";

            // Check if visiting a settlement (this takes priority - they've arrived)
            Settlement visitedSettlement = CaravanVisitUtility.SettlementVisitedNow(caravan);
            if (visitedSettlement != null)
                return $"visiting {visitedSettlement.Label}";

            // Check movement status
            if (caravan.pather.Moving)
            {
                if (caravan.pather.Paused)
                    return $"paused{GetDestinationSuffix(caravan, "will")}";

                // Check if actually moving right now or stopped for some reason
                if (caravan.NightResting)
                {
                    int bedCount = caravan.beds.GetUsedBedCount();
                    string bedInfo = bedCount > 0 ? $" ({bedCount} {(bedCount == 1 ? "bedroll" : "bedrolls")})" : "";
                    return $"resting{bedInfo}{GetDestinationSuffix(caravan, "will")}";
                }

                // Actively traveling - use "to [infinitive]" format
                return $"traveling{GetDestinationSuffix(caravan, "to")}";
            }

            // Not moving - no destination is meaningful (Destination field may have stale data)
            if (caravan.NightResting)
            {
                int bedCount = caravan.beds.GetUsedBedCount();
                string bedInfo = bedCount > 0 ? $" ({bedCount} {(bedCount == 1 ? "bedroll" : "bedrolls")})" : "";
                return $"resting{bedInfo}";
            }

            // Caravan is stopped - they have no active destination
            return "waiting";
        }

        /// <summary>
        /// Gets a destination suffix with proper grammar.
        /// prefix="to" gives "to enter Mikaelville" (for traveling)
        /// prefix="will" gives "will enter Mikaelville" (for paused/resting)
        /// </summary>
        private static string GetDestinationSuffix(Caravan caravan, string prefix)
        {
            if (caravan?.pather == null)
                return "";

            // Check if there's an arrival action with a description
            if (caravan.pather.ArrivalAction != null)
            {
                string report = caravan.pather.ArrivalAction.ReportString;
                if (!string.IsNullOrEmpty(report))
                {
                    string trimmedReport = report.TrimEnd('.');

                    // If it starts with "Traveling to", extract the destination
                    if (trimmedReport.StartsWith("Traveling to ", System.StringComparison.OrdinalIgnoreCase))
                    {
                        string destination = trimmedReport.Substring(13);
                        // "to" prefix means actively traveling: "traveling to X"
                        // "will" prefix means paused/resting: ". To X" (separate sentences)
                        if (prefix == "to")
                            return $" to {destination}";
                        else
                            return $". To {destination}";
                    }

                    // Convert "-ing" verb to infinitive: "Entering X" -> "enter X"
                    string infinitive = ConvertToInfinitive(trimmedReport);
                    return $", {prefix} {infinitive}";
                }
            }

            // If we have a destination tile but no arrival action, try to describe it
            if (caravan.pather.Destination != PlanetTile.Invalid)
            {
                var destObjects = Find.WorldObjects?.ObjectsAt(caravan.pather.Destination);
                if (destObjects != null)
                {
                    var settlement = destObjects.OfType<Settlement>().FirstOrDefault();
                    if (settlement != null)
                    {
                        if (prefix == "to")
                            return $" to {settlement.Label}";
                        else
                            return $". To {settlement.Label}";
                    }

                    var site = destObjects.OfType<Site>().FirstOrDefault();
                    if (site != null)
                    {
                        if (prefix == "to")
                            return $" to {site.Label}";
                        else
                            return $". To {site.Label}";
                    }
                }
            }

            return "";
        }

        /// <summary>
        /// Converts an "-ing" verb phrase to infinitive form.
        /// "Entering Mikaelville" -> "enter Mikaelville"
        /// "Attacking bandit camp" -> "attack bandit camp"
        /// </summary>
        private static string ConvertToInfinitive(string phrase)
        {
            if (string.IsNullOrEmpty(phrase))
                return phrase;

            // Find the first word (the verb)
            int spaceIndex = phrase.IndexOf(' ');
            string verb = spaceIndex > 0 ? phrase.Substring(0, spaceIndex) : phrase;
            string rest = spaceIndex > 0 ? phrase.Substring(spaceIndex) : "";

            // Convert -ing to infinitive
            string lowerVerb = verb.ToLower();
            if (lowerVerb.EndsWith("ing") && lowerVerb.Length > 4)
            {
                // Handle common patterns:
                // "entering" -> "enter" (remove -ing, but "enter" not "ent")
                // "attacking" -> "attack" (remove -ing)
                // "visiting" -> "visit" (remove -ing)
                // "settling" -> "settle" (remove -ing, add -e)
                // "trading" -> "trade" (remove -ing, add -e)

                string stem = lowerVerb.Substring(0, lowerVerb.Length - 3);

                // Check if we need to add back an 'e' (words like trade, settle, etc.)
                // Common pattern: consonant + 'ing' where original had consonant + 'e'
                if (stem.Length >= 2)
                {
                    char lastChar = stem[stem.Length - 1];
                    char secondLast = stem[stem.Length - 2];

                    // If stem ends in consonant and second-to-last is also consonant, likely needs 'e'
                    // Examples: trad(e), settl(e), but not: attack, visit, enter
                    bool needsE = IsConsonant(lastChar) && IsConsonant(secondLast) &&
                                  !stem.EndsWith("ck") && !stem.EndsWith("tt") && !stem.EndsWith("ss");

                    // Special cases that don't need 'e'
                    if (stem == "enter" || stem == "attack" || stem == "visit" || stem == "rest")
                        needsE = false;

                    if (needsE)
                        stem += "e";
                }

                return stem + rest;
            }

            return phrase.ToLower();
        }

        private static bool IsConsonant(char c)
        {
            return char.IsLetter(c) && !"aeiouAEIOU".Contains(c);
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
        /// Key 1: Growing, Food, and Resources information.
        /// Growing period, forageability, grazing, rainfall, stone types.
        /// </summary>
        public static string GetTileGrowingInfo(PlanetTile planetTile)
        {
            if (!planetTile.Valid || Find.WorldGrid == null)
                return "Invalid tile";

            if (planetTile.LayerDef?.isSpace == true)
                return "No growing information for space tiles.";

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

            return info.ToString();
        }

        /// <summary>
        /// Key 2: Movement and Terrain information.
        /// Movement difficulty, winter penalty, roads, rivers, elevation, coastal.
        /// When on a planned route, adds path direction and travel times at the BEGINNING.
        /// </summary>
        public static string GetTileMovementInfo(PlanetTile planetTile)
        {
            if (!planetTile.Valid || Find.WorldGrid == null)
                return "Invalid tile";

            if (planetTile.LayerDef?.isSpace == true)
                return "No movement information for space tiles.";

            Tile tile = planetTile.Tile;
            if (tile == null)
                return "Unknown tile";

            StringBuilder info = new StringBuilder();

            // Route/path info at the BEGINNING (for quick path tracing with Key 2)
            // Priority: Route planner if active, otherwise caravan paths
            string routeAnnouncement = RoutePlannerState.GetRouteAnnouncement(planetTile);
            if (!string.IsNullOrEmpty(routeAnnouncement))
            {
                info.Append(routeAnnouncement);

                // Add context-aware timing based on current path segment
                // Shows cumulative time from start to reach this tile (matches game tooltip)
                var timing = RoutePlannerState.GetCurrentSegmentTiming(planetTile);
                if (timing.HasValue)
                {
                    string timeFromStart = timing.Value.ticksFromStart.ToStringTicksToDays("0.#");
                    info.Append($" Arrival: {timeFromStart}.");
                }

                info.Append(" ");
            }
            else
            {
                // Show caravan path info when route planner is not active
                // For the "2" key, include ALL moving caravans on this tile (not just selected)
                // since user is specifically asking for movement info
                string caravanPathInfo = GetCaravanPathAnnouncement(planetTile, includeCaravansOnTile: true);
                if (!string.IsNullOrEmpty(caravanPathInfo))
                {
                    info.Append(caravanPathInfo);
                    info.Append(" ");
                }
            }

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

                    // Add road direction description
                    string roadDirection = GetRoadDirectionDescription(planetTile, surfaceTile.Roads);
                    if (!string.IsNullOrEmpty(roadDirection))
                    {
                        info.Append($" {roadDirection}");
                    }
                }

                if (surfaceTile.Rivers != null && surfaceTile.Rivers.Count > 0)
                {
                    var largestRiver = surfaceTile.Rivers.MaxBy(r => r.river.degradeThreshold);
                    info.Append($" River: {largestRiver.river.LabelCap}.");

                    // Add river direction description
                    string riverDirection = GetRiverDirectionDescription(planetTile, surfaceTile.Rivers);
                    if (!string.IsNullOrEmpty(riverDirection))
                    {
                        info.Append($" {riverDirection}");
                    }
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

            if (planetTile.LayerDef?.isSpace == true)
                return "No health information for space tiles.";

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

            // Mutators (Odyssey DLC) - show labels and descriptions
            if (tile.Mutators.Any())
            {
                var mutators = tile.Mutators
                    .OrderByDescending(m => m.displayPriority)
                    .ToList();

                var mutatorParts = new List<string>();
                foreach (var m in mutators)
                {
                    string label = m.Label(planetTile);
                    string desc = m.Description(planetTile);
                    if (!string.IsNullOrEmpty(desc) && desc != label)
                        mutatorParts.Add($"{label}: {desc}");
                    else
                        mutatorParts.Add(label);
                }
                info.Append($"Tile features: {string.Join(". ", mutatorParts)}.");
                hasContent = true;
            }

            // Landmarks (Odyssey DLC)
            if (ModsConfig.OdysseyActive && tile.Landmark != null)
            {
                if (hasContent) info.Append(" ");
                info.Append($"Landmark: {tile.Landmark.name}, {tile.Landmark.def.LabelCap}.");
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

        #region Road Direction Helpers

        /// <summary>
        /// Gets a description of which directions roads go from this tile.
        /// Uses 8-way compass directions (N, NE, E, SE, S, SW, W, NW) for accuracy on hex grids.
        /// Examples:
        /// - "Runs north to south." (straight road)
        /// - "Runs northeast to southwest." (diagonal road)
        /// - "Curves from north to east." (turning road)
        /// - "Ends here, continues south." (dead end)
        /// - "Junction: north, east, and south." (3+ directions)
        /// </summary>
        private static string GetRoadDirectionDescription(PlanetTile fromTile, List<SurfaceTile.RoadLink> roads)
        {
            if (roads == null || roads.Count == 0 || Find.WorldGrid == null)
                return null;

            // Get arrow key direction for each road connection
            List<string> directions = new List<string>();
            foreach (var roadLink in roads)
            {
                string dir = GetArrowKeyDirection(fromTile, roadLink.neighbor);
                if (!string.IsNullOrEmpty(dir) && !directions.Contains(dir))
                {
                    directions.Add(dir);
                }
            }

            if (directions.Count == 0)
            {
                return "Ends here.";
            }

            if (directions.Count == 1)
            {
                return $"Ends here, continues {directions[0]}.";
            }

            if (directions.Count == 2)
            {
                if (AreOppositeDirections8Way(directions[0], directions[1]))
                {
                    var ordered = OrderDirectionPair8Way(directions[0], directions[1]);
                    return $"Runs {ordered.Item1} to {ordered.Item2}.";
                }
                else
                {
                    return $"Curves from {directions[0]} to {directions[1]}.";
                }
            }

            return $"Junction: {directions.ToCommaList(useAnd: true)}.";
        }

        /// <summary>
        /// Gets a description of which directions rivers flow from this tile.
        /// Uses 8-way compass directions (N, NE, E, SE, S, SW, W, NW) for accuracy on hex grids.
        /// Rivers use "flows" language since they have direction (unlike roads).
        /// Examples:
        /// - "Flows north to south." (river passing through)
        /// - "Flows northeast to southwest." (diagonal river)
        /// - "Bends from north to east." (river bending)
        /// - "Flows south." (river source/end)
        /// - "Confluence: north, east, and south." (river fork)
        /// </summary>
        private static string GetRiverDirectionDescription(PlanetTile fromTile, List<SurfaceTile.RiverLink> rivers)
        {
            if (rivers == null || rivers.Count == 0 || Find.WorldGrid == null)
                return null;

            // Get arrow key direction for each river connection
            List<string> directions = new List<string>();
            foreach (var riverLink in rivers)
            {
                string dir = GetArrowKeyDirection(fromTile, riverLink.neighbor);
                if (!string.IsNullOrEmpty(dir) && !directions.Contains(dir))
                {
                    directions.Add(dir);
                }
            }

            if (directions.Count == 0)
            {
                return null;
            }

            if (directions.Count == 1)
            {
                return $"Flows {directions[0]}.";
            }

            if (directions.Count == 2)
            {
                if (AreOppositeDirections8Way(directions[0], directions[1]))
                {
                    var ordered = OrderDirectionPair8Way(directions[0], directions[1]);
                    return $"Flows {ordered.Item1} to {ordered.Item2}.";
                }
                else
                {
                    return $"Bends from {directions[0]} to {directions[1]}.";
                }
            }

            return $"Confluence: {directions.ToCommaList(useAnd: true)}.";
        }

        /// <summary>
        /// Gets the arrow key direction needed to move from one tile to a neighbor tile.
        /// This determines which arrow key press would select the target tile, matching
        /// the logic used in WorldNavigationState.MoveInDirection.
        /// Returns: north, east, south, west if reachable by single arrow key.
        /// If the target tile isn't directly reachable by any single arrow key,
        /// returns 8-way compass direction (northeast, southeast, etc.) so user knows
        /// they need two key presses.
        /// </summary>
        internal static string GetArrowKeyDirection(PlanetTile fromTile, PlanetTile toTile)
        {
            if (!fromTile.Valid || !toTile.Valid || Find.WorldGrid == null)
                return null;

            Vector3 currentPos = Find.WorldGrid.GetTileCenter(fromTile);
            Vector3 targetPos = Find.WorldGrid.GetTileCenter(toTile);
            Vector3 up = currentPos.normalized;
            Vector3 north = Vector3.ProjectOnPlane(Vector3.up, up).normalized;
            Vector3 east = Vector3.Cross(up, north).normalized;

            // Get all neighbors
            List<PlanetTile> neighbors = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(fromTile, neighbors);

            // Only test the 4 cardinal directions that arrow keys actually support
            var cardinalDirections = new (string name, Vector3 dir)[]
            {
                ("north", north),
                ("east", east),
                ("south", -north),
                ("west", -east)
            };

            // First pass: check if any cardinal direction directly selects the target tile
            foreach (var (name, desiredDir) in cardinalDirections)
            {
                // Find which neighbor would be selected for this direction
                // (same logic as MoveInDirection)
                PlanetTile bestNeighbor = PlanetTile.Invalid;
                float bestDot = -2f;

                foreach (PlanetTile neighbor in neighbors)
                {
                    Vector3 neighborPos = Find.WorldGrid.GetTileCenter(neighbor);
                    Vector3 dirToNeighbor = (neighborPos - currentPos).normalized;
                    float dot = Vector3.Dot(dirToNeighbor, desiredDir);

                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        bestNeighbor = neighbor;
                    }
                }

                // If this direction would select our target tile, that's our answer
                if (bestNeighbor.Valid && bestNeighbor.tileId == toTile.tileId)
                {
                    return name;
                }
            }

            // Target tile isn't directly reachable by any single arrow key
            // Return 8-way compass direction so user knows they need two key presses
            return GetCompassDirection(currentPos, targetPos, north, east);
        }

        /// <summary>
        /// Gets the 8-way compass direction from one position to another.
        /// Returns: north, northeast, east, southeast, south, southwest, west, northwest
        /// These map to arrow key presses: north=up, south=down, east=right, west=left,
        /// and diagonals require two arrow keys (e.g., northeast=up+right).
        /// NOTE: For directions that should match arrow key navigation, use GetArrowKeyDirection instead.
        /// </summary>
        internal static string GetCompassDirection(Vector3 fromPos, Vector3 toPos, Vector3 north, Vector3 east)
        {
            Vector3 up = fromPos.normalized;
            Vector3 direction = (toPos - fromPos).normalized;

            // Project onto tangent plane
            float northComponent = Vector3.Dot(direction, north);
            float eastComponent = Vector3.Dot(direction, east);

            // Use threshold to determine cardinal vs diagonal (~22.5 degrees)
            const float threshold = 0.38f;

            if (Mathf.Abs(eastComponent) < threshold)
            {
                return northComponent >= 0 ? "north" : "south";
            }
            else if (Mathf.Abs(northComponent) < threshold)
            {
                return eastComponent >= 0 ? "east" : "west";
            }
            else
            {
                string ns = northComponent >= 0 ? "north" : "south";
                string ew = eastComponent >= 0 ? "east" : "west";
                return ns + ew;
            }
        }

        /// <summary>
        /// Checks if two 8-way directions are opposite.
        /// </summary>
        private static bool AreOppositeDirections8Way(string dir1, string dir2)
        {
            var opposites = new Dictionary<string, string>
            {
                {"north", "south"}, {"south", "north"},
                {"east", "west"}, {"west", "east"},
                {"northeast", "southwest"}, {"southwest", "northeast"},
                {"northwest", "southeast"}, {"southeast", "northwest"}
            };
            return opposites.TryGetValue(dir1, out string opposite) && opposite == dir2;
        }

        /// <summary>
        /// Orders a direction pair consistently for 8-way compass.
        /// </summary>
        private static (string, string) OrderDirectionPair8Way(string dir1, string dir2)
        {
            // Order: north before south, east before west, diagonals follow their primary component
            var priority = new Dictionary<string, int>
            {
                {"north", 0}, {"northeast", 1}, {"east", 2}, {"southeast", 3},
                {"south", 4}, {"southwest", 5}, {"west", 6}, {"northwest", 7}
            };

            if (priority.TryGetValue(dir1, out int p1) && priority.TryGetValue(dir2, out int p2))
            {
                // For opposites, prefer the "earlier" direction
                if (p1 > p2)
                    return (dir2, dir1);
            }
            return (dir1, dir2);
        }

        #endregion

        #region Caravan Path Helpers

        /// <summary>
        /// Gets an announcement for caravan paths passing through this tile.
        /// Returns null if no selected caravans have paths through this tile.
        /// Uses multiSelectedCaravans if any, otherwise uses the focused selectedCaravan.
        /// </summary>
        /// <param name="tile">The tile to check for caravan paths.</param>
        /// <param name="includeCaravansOnTile">If true, includes ALL moving caravans on this tile
        /// (not just selected ones). Used by "2" key for quick movement info.</param>
        /// <returns>Announcement like "Sam's caravan will head north." or "Sam's caravan will stop here." or null if not on any path.</returns>
        internal static string GetCaravanPathAnnouncement(PlanetTile tile, bool includeCaravansOnTile = false)
        {
            if (!tile.Valid || Find.WorldGrid == null)
                return null;

            List<string> pathAnnouncements = new List<string>();
            List<string> stoppingCaravans = new List<string>();
            HashSet<int> processedCaravanIds = new HashSet<int>();

            // If includeCaravansOnTile is true, first check ALL moving caravans on this tile
            if (includeCaravansOnTile && Find.WorldObjects != null)
            {
                var caravansOnTile = Find.WorldObjects.ObjectsAt(tile).OfType<Caravan>()
                    .Where(c => c.Faction == Faction.OfPlayer && c.pather != null && c.pather.Moving);

                foreach (var caravan in caravansOnTile)
                {
                    if (caravan == null || caravan.Destroyed)
                        continue;

                    processedCaravanIds.Add(caravan.ID);

                    WorldPath path = caravan.pather.curPath;
                    if (path == null || !path.Found || path.NodesLeftCount <= 0)
                        continue;

                    string caravanName = caravan.Label ?? "Caravan";

                    // Get direction to next tile on path
                    string direction = GetCaravanTravelDirection(caravan);
                    if (!string.IsNullOrEmpty(direction))
                    {
                        string destination = GetCaravanDestinationName(caravan);
                        if (!string.IsNullOrEmpty(destination))
                            pathAnnouncements.Add($"{caravanName} heading {direction} toward {destination}.");
                        else
                            pathAnnouncements.Add($"{caravanName} heading {direction}.");
                    }
                }
            }

            // Get relevant caravans based on selection state (for path-through tiles)
            var caravansToCheck = GetSelectedCaravansForPathCheck();
            if (caravansToCheck != null && caravansToCheck.Count > 0)
            {
                foreach (var caravan in caravansToCheck)
                {
                    if (caravan == null || caravan.Destroyed)
                        continue;

                    // Skip if already processed (caravan was on this tile)
                    if (processedCaravanIds.Contains(caravan.ID))
                        continue;

                    // Check if caravan has a valid path (may be paused but still has destination)
                    if (caravan.pather == null || caravan.pather.curPath == null)
                        continue;

                    WorldPath path = caravan.pather.curPath;
                    if (!path.Found || path.NodesLeftCount <= 0)
                        continue;

                    string caravanName = caravan.Label ?? "Caravan";

                    // Check if this tile is the caravan's destination
                    if (IsCaravanDestination(tile, path))
                    {
                        stoppingCaravans.Add(caravanName);
                    }
                    // Skip path announcements if the caravan is ON this tile
                    // (handled above if includeCaravansOnTile is true, otherwise status shows it)
                    else if (caravan.Tile.tileId != tile.tileId)
                    {
                        // Check if this tile is on the remaining path
                        string direction = GetCaravanPathDirection(tile, path);

                        if (!string.IsNullOrEmpty(direction))
                        {
                            pathAnnouncements.Add($"{caravanName} will head {direction}.");
                        }
                    }
                }
            }

            // Build the combined announcement
            List<string> allAnnouncements = new List<string>();

            // Add stopping announcement if any caravans stop here
            if (stoppingCaravans.Count > 0)
            {
                string stoppingText = FormatCaravanList(stoppingCaravans) + " will stop here.";
                allAnnouncements.Add(stoppingText);
            }

            // Add path direction announcements
            allAnnouncements.AddRange(pathAnnouncements);

            if (allAnnouncements.Count == 0)
                return null;

            return string.Join(" ", allAnnouncements);
        }

        /// <summary>
        /// Checks if the given tile is the destination of the caravan's current path.
        /// </summary>
        private static bool IsCaravanDestination(PlanetTile tile, WorldPath path)
        {
            if (!tile.Valid || path == null || !path.Found || path.NodesLeftCount <= 0)
                return false;

            // The destination is at Peek(NodesLeftCount - 1)
            PlanetTile destTile = path.Peek(path.NodesLeftCount - 1);
            return destTile.tileId == tile.tileId;
        }

        /// <summary>
        /// Formats a list of caravan names naturally: "A", "A and B", or "A, B, and C".
        /// </summary>
        private static string FormatCaravanList(List<string> names)
        {
            if (names.Count == 0)
                return "";
            if (names.Count == 1)
                return names[0];
            if (names.Count == 2)
                return $"{names[0]} and {names[1]}";

            // Three or more: "A, B, and C"
            return string.Join(", ", names.Take(names.Count - 1)) + ", and " + names[names.Count - 1];
        }

        /// <summary>
        /// Gets the list of caravans to check for path announcements.
        /// Priority: 1) Ctrl+Space multi-selected caravans, 2) Comma/period focused caravan
        /// </summary>
        private static List<Caravan> GetSelectedCaravansForPathCheck()
        {
            // First priority: Ctrl+Space explicit selections
            var multiSelected = WorldNavigationState.GetMultiSelectedCaravans();
            if (multiSelected != null && multiSelected.Count > 0)
            {
                return multiSelected.ToList();
            }

            // Second priority: Comma/period focused caravan
            var focusedCaravan = WorldNavigationState.GetSelectedCaravan();
            if (focusedCaravan != null)
            {
                return new List<Caravan> { focusedCaravan };
            }

            return null;
        }

        /// <summary>
        /// Gets the compass direction the caravan will travel from this tile.
        /// Returns null if the tile is not on the caravan's remaining path.
        /// </summary>
        private static string GetCaravanPathDirection(PlanetTile tile, WorldPath path)
        {
            int tileId = tile.tileId;

            // Find this tile in the remaining path
            // WorldPath stores nodes in reverse order (destination first, current last)
            // NodesReversed[0] = destination, NodesReversed[Count-1] = start
            // But we only care about nodes from 0 to NodesLeftCount-1 (the remaining path)
            int tileIndex = -1;
            for (int i = 0; i < path.NodesLeftCount; i++)
            {
                if (path.Peek(i).tileId == tileId)
                {
                    tileIndex = i;
                    break;
                }
            }

            if (tileIndex < 0)
                return null; // Tile not on remaining path

            // If we're at or past the last tile before destination, no next direction
            // (higher Peek index = closer to destination)
            if (tileIndex >= path.NodesLeftCount - 1)
                return null;

            // Get the next tile toward destination (higher index = closer to destination)
            PlanetTile nextTile = path.Peek(tileIndex + 1);
            if (!nextTile.Valid)
                return null;

            // Get arrow key direction (which key press would move from tile to nextTile)
            return GetArrowKeyDirection(tile, nextTile);
        }

        /// <summary>
        /// Gets a short status string for a caravan on the current tile.
        /// For traveling caravans: "heading [direction] toward [destination]"
        /// For other states: "resting", "paused", "stopped", "overloaded", etc.
        /// </summary>
        internal static string GetCaravanShortStatus(Caravan caravan)
        {
            if (caravan == null)
                return null;

            // Check problem states first
            if (caravan.AllOwnersDowned)
                return "all downed";

            if (caravan.AllOwnersHaveMentalBreak)
                return "mental break";

            if (caravan.ImmobilizedByMass)
                return "overloaded";

            // Check if visiting a settlement
            Settlement visitedSettlement = CaravanVisitUtility.SettlementVisitedNow(caravan);
            if (visitedSettlement != null)
                return $"visiting {visitedSettlement.Label}";

            // Check movement status
            if (caravan.pather != null && caravan.pather.Moving)
            {
                if (caravan.pather.Paused)
                    return "paused";

                if (caravan.NightResting)
                    return "resting";

                // Actively traveling - get direction and destination
                string direction = GetCaravanTravelDirection(caravan);
                string destination = GetCaravanDestinationName(caravan);

                if (!string.IsNullOrEmpty(direction) && !string.IsNullOrEmpty(destination))
                    return $"heading {direction} toward {destination}";
                else if (!string.IsNullOrEmpty(destination))
                    return $"traveling to {destination}";
                else if (!string.IsNullOrEmpty(direction))
                    return $"heading {direction}";
                else
                    return "traveling";
            }

            // Not moving
            if (caravan.NightResting)
                return "resting";

            return "stopped";
        }

        /// <summary>
        /// Gets the compass direction the caravan is currently heading from its current tile.
        /// </summary>
        private static string GetCaravanTravelDirection(Caravan caravan)
        {
            if (caravan?.pather?.curPath == null || !caravan.pather.curPath.Found)
                return null;

            WorldPath path = caravan.pather.curPath;
            if (path.NodesLeftCount < 1)
                return null;

            PlanetTile currentTile = caravan.Tile;
            PlanetTile nextTile = PlanetTile.Invalid;

            // Get direction from caravan's current tile to the next tile on path
            PlanetTile immediateTile = path.Peek(0);
            if (immediateTile.tileId != currentTile.tileId)
            {
                // Caravan heading to Peek(0) - this is the common case
                nextTile = immediateTile;
            }
            else if (path.NodesLeftCount >= 2)
            {
                // Caravan is at Peek(0), heading to Peek(1)
                nextTile = path.Peek(1);
            }
            else
            {
                // Only destination remains and we're already there
                return null;
            }

            if (!nextTile.Valid || Find.WorldGrid == null)
                return null;

            // Get arrow key direction (which key press would move to nextTile)
            return GetArrowKeyDirection(currentTile, nextTile);
        }

        /// <summary>
        /// Gets the destination name for a traveling caravan.
        /// </summary>
        private static string GetCaravanDestinationName(Caravan caravan)
        {
            if (caravan?.pather == null)
                return null;

            // Try to get destination from arrival action
            if (caravan.pather.ArrivalAction != null)
            {
                string report = caravan.pather.ArrivalAction.ReportString;
                if (!string.IsNullOrEmpty(report))
                {
                    // Extract destination from "Traveling to X" format
                    if (report.StartsWith("Traveling to ", System.StringComparison.OrdinalIgnoreCase))
                    {
                        return report.Substring(13).TrimEnd('.');
                    }
                    // Other action formats - just use the report
                    return report.TrimEnd('.');
                }
            }

            // Fall back to destination tile's world object name
            if (caravan.pather.Destination.Valid)
            {
                var destObjects = Find.WorldObjects?.ObjectsAt(caravan.pather.Destination);
                if (destObjects != null)
                {
                    var destObject = destObjects.FirstOrDefault();
                    if (destObject != null)
                        return destObject.Label;
                }
            }

            return null;
        }

        #endregion
    }
}
