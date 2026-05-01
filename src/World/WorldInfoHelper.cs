using System.Collections.Generic;
using System.Linq;
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
        // Maps internal English compass tokens (north/east/south/west and four
        // diagonals) to localized lower-case nouns from Map.Direction.Lower.*.
        // The tokens stay English inside this class so the dictionary lookups
        // (opposite-direction, priority ordering) keep working - localization
        // happens at announcement time only.
        private static string LocalizeCompass(string englishCompass)
        {
            switch (englishCompass)
            {
                case "north":     return "RimWorldAccess.Map.Direction.Lower.North".Translate();
                case "south":     return "RimWorldAccess.Map.Direction.Lower.South".Translate();
                case "east":      return "RimWorldAccess.Map.Direction.Lower.East".Translate();
                case "west":      return "RimWorldAccess.Map.Direction.Lower.West".Translate();
                case "northeast": return "RimWorldAccess.Map.Direction.Lower.Northeast".Translate();
                case "southeast": return "RimWorldAccess.Map.Direction.Lower.Southeast".Translate();
                case "southwest": return "RimWorldAccess.Map.Direction.Lower.Southwest".Translate();
                case "northwest": return "RimWorldAccess.Map.Direction.Lower.Northwest".Translate();
                default:          return englishCompass;
            }
        }

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
                return "RimWorldAccess.World.Tile.Invalid".Translate();

            Tile tile = planetTile.Tile;
            if (tile == null)
                return "RimWorldAccess.World.Tile.Unknown".Translate();

            var builder = new AnnouncementBuilder().DefaultSep(Separator.Comma);

            if (includeRouteInfo && Find.WorldRoutePlanner != null && Find.WorldRoutePlanner.Active)
            {
                for (int i = 0; i < Find.WorldRoutePlanner.waypoints.Count; i++)
                {
                    if (Find.WorldRoutePlanner.waypoints[i].Tile == planetTile)
                    {
                        builder.Add("RimWorldAccess.World.Tile.Summary.WaypointPrefix".Translate(i + 1));
                        break;
                    }
                }
            }

            if (tile.PrimaryBiome != null)
                builder.Add(tile.PrimaryBiome.LabelCap);

            if (ModsConfig.OdysseyActive && tile.Landmark != null)
                builder.Add(tile.Landmark.name);

            if (!string.IsNullOrEmpty(fuelCostInfo))
                builder.Add(fuelCostInfo);

            bool isSpaceLayer = planetTile.LayerDef?.isSpace == true;
            if (!isSpaceLayer)
            {
                if (tile.hilliness != Hilliness.Impassable && tile.hilliness != Hilliness.Undefined)
                    builder.Add(tile.hilliness.GetLabelCap());

                builder.Add(MenuHelper.FormatTemperature(tile.temperature, "F0"));
            }

            if (Find.WorldObjects != null)
            {
                List<WorldObject> objectsAtTile = Find.WorldObjects.ObjectsAt(planetTile)
                    .Where(obj => !(obj is RoutePlannerWaypoint))
                    .ToList();

                if (objectsAtTile.Count > 0)
                {
                    Settlement settlement = objectsAtTile.OfType<Settlement>().FirstOrDefault();
                    if (settlement != null)
                        AppendSettlementSummary(builder, settlement);

                    var caravans = objectsAtTile.OfType<Caravan>().ToList();
                    if (caravans.Count > 0)
                    {
                        string joined = caravans.Select(FormatCaravanWithDetail).ToList().ToCommaList(useAnd: true);
                        string key = caravans.Count == 1
                            ? "RimWorldAccess.World.Tile.Caravan.IsHere"
                            : "RimWorldAccess.World.Tile.Caravan.AreHere";
                        builder.Add(key.Translate(joined), Separator.Period);
                    }

                    if (settlement == null && caravans.Count == 0)
                    {
                        WorldObject firstObject = objectsAtTile.FirstOrDefault();
                        if (firstObject != null)
                            builder.Add(firstObject.Label);
                    }
                }
            }

            if (!minimal && tile is SurfaceTile surfaceTile)
            {
                if (surfaceTile.Roads != null && surfaceTile.Roads.Count > 0)
                {
                    string roadName = surfaceTile.Roads.First().road.label;
                    string roadInline = GetRoadDirectionInline(planetTile, surfaceTile.Roads);
                    if (!string.IsNullOrEmpty(roadInline))
                        builder.Add("RimWorldAccess.World.Tile.Summary.RoadInline".Translate(
                            roadName.CapitalizeFirst(), roadInline), Separator.Period);
                }

                if (surfaceTile.Rivers != null && surfaceTile.Rivers.Count > 0)
                {
                    string riverInline = GetRiverDirectionInline(planetTile, surfaceTile.Rivers);
                    if (!string.IsNullOrEmpty(riverInline))
                        builder.Add("RimWorldAccess.World.Tile.Summary.RiverInline".Translate(riverInline),
                            Separator.Period);
                }
            }

            if (!minimal)
            {
                string questInfo = GetQuestInfoForTile(planetTile);
                if (!string.IsNullOrEmpty(questInfo))
                    builder.Add(questInfo, Separator.Period);
            }

            if (includeRouteInfo)
            {
                string routeInfo = RoutePlannerState.GetRouteAnnouncement(planetTile);
                if (!string.IsNullOrEmpty(routeInfo))
                {
                    builder.Add(routeInfo, Separator.Period);
                }
                else
                {
                    string caravanPathInfo = GetCaravanPathAnnouncement(planetTile);
                    if (!string.IsNullOrEmpty(caravanPathInfo))
                        builder.Add(caravanPathInfo, Separator.Period);
                }
            }

            return builder.Build();
        }

        private static void AppendSettlementSummary(AnnouncementBuilder builder, Settlement settlement)
        {
            string label = settlement.Label;

            if (settlement.Faction == null)
            {
                builder.Add(label);
                return;
            }

            string factionDisplay;
            if (settlement.Faction == Faction.OfPlayer)
            {
                factionDisplay = "RimWorldAccess.World.Settlement.FactionInline".Translate(Faction.OfPlayer.Name);
            }
            else
            {
                string relationship = settlement.Faction.PlayerRelationKind.GetLabelCap();
                int goodwill = settlement.Faction.PlayerGoodwill;
                string goodwillStr = goodwill >= 0
                    ? (string)"RimWorldAccess.World.Settlement.GoodwillPositive".Translate(goodwill)
                    : goodwill.ToString();
                factionDisplay = "RimWorldAccess.World.Settlement.FactionWithRelationInline".Translate(
                    settlement.Faction.Name, relationship, goodwillStr);
            }

            builder.Add("RimWorldAccess.World.Settlement.LabelWithFaction".Translate(label, factionDisplay));

            if (settlement.Faction != Faction.OfPlayer && settlement.TraderKind != null)
            {
                RoyalTitleDef titleRequired = settlement.TraderKind.TitleRequiredToTrade;
                if (titleRequired != null)
                    builder.Add("RimWorldAccess.World.Settlement.RequiresTitleToTrade".Translate(
                        titleRequired.GetLabelCapForBothGenders()), Separator.Period);
            }
        }

        /// <summary>
        /// Gets detailed information about a world tile (for I key).
        /// </summary>
        public static string GetDetailedTileInfo(PlanetTile planetTile)
        {
            if (!planetTile.Valid || Find.WorldGrid == null)
                return "RimWorldAccess.World.Tile.Invalid".Translate();

            Tile tile = planetTile.Tile;
            if (tile == null)
                return "RimWorldAccess.World.Tile.Unknown".Translate();

            var builder = new AnnouncementBuilder().DefaultSep(Separator.Period);

            Vector2 longlat = Find.WorldGrid.LongLatOf(planetTile);
            builder.Add("RimWorldAccess.World.Tile.Detail.Coordinates".Translate(
                longlat.y.ToString("F2"), longlat.x.ToString("F2")));

            if (tile.PrimaryBiome != null)
                builder.Add("RimWorldAccess.World.Tile.Detail.Biome".Translate(tile.PrimaryBiome.LabelCap));

            bool isSpaceLayer = planetTile.LayerDef?.isSpace == true;
            if (!isSpaceLayer)
            {
                if (tile.hilliness != Hilliness.Undefined)
                    builder.Add("RimWorldAccess.World.Tile.Detail.Hilliness".Translate(tile.hilliness.GetLabelCap()));

                builder.Add("RimWorldAccess.World.Tile.Detail.Elevation".Translate(tile.elevation.ToString("F0")));
                builder.Add("RimWorldAccess.World.Tile.Detail.TemperatureAverage".Translate(
                    MenuHelper.FormatTemperature(tile.temperature, "F0")));

                if (ModsConfig.BiotechActive && tile.pollution > 0)
                    builder.Add("RimWorldAccess.World.Tile.Detail.Pollution".Translate(tile.pollution.ToString("F0")));
            }

            if (Find.WorldObjects != null)
            {
                List<WorldObject> objectsAtTile = Find.WorldObjects.ObjectsAt(planetTile).ToList();
                if (objectsAtTile.Count > 0)
                {
                    builder.Add("RimWorldAccess.World.Tile.Detail.WorldObjectsHeader".Translate());

                    foreach (WorldObject obj in objectsAtTile)
                        AppendDetailedWorldObject(builder, obj);
                }
            }

            string questInfo = GetDetailedQuestInfoForTile(planetTile);
            if (!string.IsNullOrEmpty(questInfo))
            {
                builder.Add("RimWorldAccess.World.Tile.Detail.QuestTargetsHeader".Translate());
                builder.Add(questInfo);
            }

            return builder.Build();
        }

        private static void AppendDetailedWorldObject(AnnouncementBuilder builder, WorldObject obj)
        {
            if (obj is Settlement settlement)
            {
                if (settlement.Faction == Faction.OfPlayer)
                {
                    builder.Add("RimWorldAccess.World.Tile.Detail.PlayerSettlement".Translate(
                        obj.Label, Faction.OfPlayer.Name));
                    return;
                }

                if (settlement.Faction != null)
                {
                    builder.Add(obj.Label);
                    builder.Add("RimWorldAccess.World.Tile.Detail.Faction".Translate(settlement.Faction.Name));

                    string relationship = settlement.Faction.PlayerRelationKind.GetLabelCap();
                    int goodwill = settlement.Faction.PlayerGoodwill;
                    string goodwillStr = goodwill >= 0
                        ? (string)"RimWorldAccess.World.Settlement.GoodwillPositive".Translate(goodwill)
                        : goodwill.ToString();
                    builder.Add("RimWorldAccess.World.Tile.Detail.RelationshipWithGoodwill".Translate(
                        relationship, goodwillStr));

                    RoyalTitleDef titleRequired = settlement.TraderKind?.TitleRequiredToTrade;
                    if (titleRequired != null)
                        builder.Add("RimWorldAccess.World.Tile.Detail.RequiresTitleToTrade".Translate(
                            titleRequired.GetLabelCapForBothGenders()));
                    return;
                }

                builder.Add(obj.Label);
                return;
            }

            if (obj is Caravan caravan)
            {
                if (caravan.Faction != null)
                    builder.Add("RimWorldAccess.World.Tile.Detail.CaravanWithFaction".Translate(
                        obj.Label, caravan.Faction.Name));
                else
                    builder.Add(obj.Label);
                return;
            }

            if (obj is Site)
            {
                builder.Add("RimWorldAccess.World.Tile.Detail.Site".Translate(obj.Label));
                return;
            }

            builder.Add(obj.Label);
        }

        /// <summary>
        /// Gets information about a specific settlement.
        /// </summary>
        public static string GetSettlementInfo(Settlement settlement)
        {
            if (settlement == null)
                return "RimWorldAccess.World.Settlement.None".Translate();

            var builder = new AnnouncementBuilder().DefaultSep(Separator.Period);

            builder.Add("RimWorldAccess.World.Settlement.LabelField".Translate(settlement.Label));

            if (settlement.Faction != null)
            {
                builder.Add("RimWorldAccess.World.Settlement.FactionField".Translate(settlement.Faction.Name));

                if (settlement.Faction == Faction.OfPlayer)
                {
                    builder.Add("RimWorldAccess.World.Settlement.RelationshipField".Translate(
                        "RimWorldAccess.World.Settlement.PlayerColonyRelationship".Translate()));
                }
                else
                {
                    string relationship = settlement.Faction.HostileTo(Faction.OfPlayer)
                        ? (string)"RimWorldAccess.World.Settlement.HostileRelationship".Translate()
                        : settlement.Faction.PlayerRelationKind.GetLabel();
                    builder.Add("RimWorldAccess.World.Settlement.RelationshipField".Translate(relationship));

                    int goodwill = settlement.Faction.PlayerGoodwill;
                    builder.Add("RimWorldAccess.World.Settlement.GoodwillField".Translate(goodwill));
                }
            }

            if (settlement.Visitable)
                builder.Add("RimWorldAccess.World.Settlement.StatusVisitable".Translate());

            if (settlement.Attackable)
                builder.Add("RimWorldAccess.World.Settlement.StatusAttackable".Translate());

            return builder.Build();
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
                return "RimWorldAccess.World.Caravan.Status.Unknown".Translate();

            // Check problem states first (these are most important)
            if (caravan.AllOwnersDowned)
                return "RimWorldAccess.World.Caravan.Status.AllDowned".Translate();

            if (caravan.AllOwnersHaveMentalBreak)
                return "RimWorldAccess.World.Caravan.Status.MentalBreak".Translate();

            if (caravan.ImmobilizedByMass)
                return "RimWorldAccess.World.Caravan.Status.Overloaded".Translate();

            // Check if visiting a settlement (this takes priority - they've arrived)
            Settlement visitedSettlement = CaravanVisitUtility.SettlementVisitedNow(caravan);
            if (visitedSettlement != null)
                return "RimWorldAccess.World.Caravan.Status.Visiting".Translate(visitedSettlement.Label);

            // Check movement status
            if (caravan.pather.Moving)
            {
                if (caravan.pather.Paused)
                    return "RimWorldAccess.World.Caravan.Status.Paused".Translate() + GetDestinationSuffix(caravan, active: false);

                // Check if actually moving right now or stopped for some reason
                if (caravan.NightResting)
                {
                    return BuildRestingStatus(caravan) + GetDestinationSuffix(caravan, active: false);
                }

                // Actively traveling - use "to [infinitive]" format
                return "RimWorldAccess.World.Caravan.Status.Traveling".Translate() + GetDestinationSuffix(caravan, active: true);
            }

            // Not moving - no destination is meaningful (Destination field may have stale data)
            if (caravan.NightResting)
            {
                return BuildRestingStatus(caravan);
            }

            // Caravan is stopped - they have no active destination
            return "RimWorldAccess.World.Caravan.Status.Waiting".Translate();
        }

        // "resting" with optional bedroll-count parenthetical (one/many split).
        private static string BuildRestingStatus(Caravan caravan)
        {
            string baseWord = "RimWorldAccess.World.Caravan.Status.Resting".Translate();
            int bedCount = caravan?.beds?.GetUsedBedCount() ?? 0;
            if (bedCount <= 0)
                return baseWord;
            string suffix = bedCount == 1
                ? "RimWorldAccess.World.Caravan.RestingBedsOneSuffix".Translate()
                : "RimWorldAccess.World.Caravan.RestingBedsManySuffix".Translate(bedCount);
            return baseWord + suffix;
        }

        /// <summary>
        /// Gets a destination suffix with proper grammar.
        /// active=true (caravan currently traveling): " to X" (clean) or
        /// ", to <infinitive>" (verb-extracted).
        /// active=false (paused/resting): ". To X" (clean) or
        /// ", will <infinitive>" (verb-extracted).
        /// </summary>
        private static string GetDestinationSuffix(Caravan caravan, bool active)
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

                    // If it starts with "Traveling to", extract the destination.
                    // This pattern only matches the game's English inspect string -
                    // localized game data falls through to the destination-tile fallback.
                    if (trimmedReport.StartsWith("Traveling to ", System.StringComparison.OrdinalIgnoreCase))
                    {
                        string destination = trimmedReport.Substring(13);
                        return active
                            ? "RimWorldAccess.World.Caravan.SuffixActiveTo".Translate(destination)
                            : "RimWorldAccess.World.Caravan.SuffixPausedSeparate".Translate(destination);
                    }

                    // Convert "-ing" verb to infinitive: "Entering X" -> "enter X"
                    string infinitive = ConvertToInfinitive(trimmedReport);
                    return active
                        ? "RimWorldAccess.World.Caravan.SuffixActiveVerb".Translate(infinitive)
                        : "RimWorldAccess.World.Caravan.SuffixPausedVerb".Translate(infinitive);
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
                        return active
                            ? "RimWorldAccess.World.Caravan.SuffixActiveTo".Translate(settlement.Label)
                            : "RimWorldAccess.World.Caravan.SuffixPausedSeparate".Translate(settlement.Label);
                    }

                    var site = destObjects.OfType<Site>().FirstOrDefault();
                    if (site != null)
                    {
                        return active
                            ? "RimWorldAccess.World.Caravan.SuffixActiveTo".Translate(site.Label)
                            : "RimWorldAccess.World.Caravan.SuffixPausedSeparate".Translate(site.Label);
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
                    string difficulty = null;
                    if (quest.challengeRating > 0)
                    {
                        difficulty = quest.challengeRating == 1
                            ? "RimWorldAccess.World.Quest.DifficultyOneStar".Translate()
                            : "RimWorldAccess.World.Quest.DifficultyManyStars".Translate(quest.challengeRating);
                    }

                    // Add description (first two sentences or up to 250 chars)
                    string questDesc = quest.description.ToString().StripTags();
                    if (!string.IsNullOrEmpty(questDesc))
                    {
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
                    }

                    string entry;
                    bool hasDifficulty = !string.IsNullOrEmpty(difficulty);
                    bool hasDesc = !string.IsNullOrEmpty(questDesc);
                    if (hasDifficulty && hasDesc)
                        entry = "RimWorldAccess.World.Quest.LabelWithDifficultyAndDescription".Translate(questName, difficulty, questDesc);
                    else if (hasDifficulty)
                        entry = "RimWorldAccess.World.Quest.LabelWithDifficulty".Translate(questName, difficulty);
                    else if (hasDesc)
                        entry = "RimWorldAccess.World.Quest.LabelWithDescription".Translate(questName, questDesc);
                    else
                        entry = "RimWorldAccess.World.Quest.Label".Translate(questName);

                    questInfos.Add(entry);
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

            var builder = new AnnouncementBuilder().DefaultSep(Separator.Period);

            var activeQuests = Find.QuestManager.questsInDisplayOrder
                .Where(q => q.State == QuestState.Ongoing && !q.hidden && !q.hiddenInUI);

            foreach (Quest quest in activeQuests)
            {
                if (!QuestTargetsTile(quest, planetTile))
                    continue;

                string questName = quest.name.StripTags();
                string questDesc = quest.description.ToString().StripTags();

                builder.Add("RimWorldAccess.World.Quest.Label".Translate(questName));

                if (quest.challengeRating > 0)
                {
                    string difficulty = quest.challengeRating == 1
                        ? (string)"RimWorldAccess.World.Quest.DifficultyOneStar".Translate()
                        : (string)"RimWorldAccess.World.Quest.DifficultyManyStars".Translate(quest.challengeRating);
                    builder.Add("RimWorldAccess.World.Quest.DetailDifficultyField".Translate(difficulty));
                }

                if (!string.IsNullOrEmpty(questDesc))
                {
                    if (questDesc.Length > 200)
                        questDesc = questDesc.Substring(0, 197) + "...";
                    builder.Add("RimWorldAccess.World.Quest.DetailDescriptionField".Translate(questDesc));
                }
            }

            string result = builder.Build();
            return string.IsNullOrEmpty(result) ? null : result;
        }

        private static bool QuestTargetsTile(Quest quest, PlanetTile planetTile)
        {
            foreach (GlobalTargetInfo target in quest.QuestLookTargets)
            {
                if (!target.IsValid || !target.IsWorldTarget)
                    continue;

                PlanetTile targetTile = PlanetTile.Invalid;
                if (target.HasWorldObject && target.WorldObject != null)
                    targetTile = target.WorldObject.Tile;
                else if (target.Tile.Valid)
                    targetTile = target.Tile;

                if (targetTile.Valid && targetTile == planetTile)
                    return true;
            }
            return false;
        }
        #region Number Key Tile Info (Keys 1-5)

        /// <summary>
        /// Key 1: Growing, Food, and Resources information.
        /// Growing period, forageability, grazing, rainfall, stone types.
        /// </summary>
        public static string GetTileGrowingInfo(PlanetTile planetTile)
        {
            if (!planetTile.Valid || Find.WorldGrid == null)
                return "RimWorldAccess.World.Tile.Invalid".Translate();

            if (planetTile.LayerDef?.isSpace == true)
                return "RimWorldAccess.World.Tile.Growing.NoSpaceInfo".Translate();

            Tile tile = planetTile.Tile;
            if (tile == null)
                return "RimWorldAccess.World.Tile.Unknown".Translate();

            var builder = new AnnouncementBuilder().DefaultSep(Separator.Period);

            string growingPeriod = Zone_Growing.GrowingQuadrumsDescription(planetTile);
            builder.Add("RimWorldAccess.World.Tile.Growing.Period".Translate(growingPeriod));
            builder.Add("RimWorldAccess.World.Tile.Growing.Rainfall".Translate(tile.rainfall.ToString("F0")));

            if (tile.PrimaryBiome?.foragedFood != null && tile.PrimaryBiome.forageability > 0f)
                builder.Add("RimWorldAccess.World.Tile.Growing.Forageability".Translate(
                    tile.PrimaryBiome.forageability.ToStringPercent(), tile.PrimaryBiome.foragedFood.label));
            else
                builder.Add("RimWorldAccess.World.Tile.Growing.NoForageability".Translate());

            bool canGraze = VirtualPlantsUtility.EnvironmentAllowsEatingVirtualPlantsNowAt(planetTile);
            builder.Add(canGraze
                ? "RimWorldAccess.World.Tile.Growing.GrazeYes".Translate()
                : "RimWorldAccess.World.Tile.Growing.GrazeNo".Translate());

            if (tile.PrimaryBiome?.canBuildBase == true)
            {
                var stoneTypes = Find.World.NaturalRockTypesIn(planetTile)
                    .Select(rt => rt.label)
                    .ToList();
                if (stoneTypes.Count > 0)
                    builder.Add("RimWorldAccess.World.Tile.Growing.StoneTypes".Translate(
                        stoneTypes.ToCommaList(useAnd: true).CapitalizeFirst()));
            }

            return builder.Build();
        }

        /// <summary>
        /// Key 2: Movement and Terrain information.
        /// Movement difficulty, winter penalty, roads, rivers, elevation, coastal.
        /// When on a planned route, adds path direction and travel times at the BEGINNING.
        /// </summary>
        public static string GetTileMovementInfo(PlanetTile planetTile)
        {
            if (!planetTile.Valid || Find.WorldGrid == null)
                return "RimWorldAccess.World.Tile.Invalid".Translate();

            if (planetTile.LayerDef?.isSpace == true)
                return "RimWorldAccess.World.Tile.Movement.NoSpaceInfo".Translate();

            Tile tile = planetTile.Tile;
            if (tile == null)
                return "RimWorldAccess.World.Tile.Unknown".Translate();

            var builder = new AnnouncementBuilder().DefaultSep(Separator.Period);

            // Route/path info at the BEGINNING (for quick path tracing with Key 2)
            string routeAnnouncement = RoutePlannerState.GetRouteAnnouncement(planetTile);
            if (!string.IsNullOrEmpty(routeAnnouncement))
            {
                builder.Add(routeAnnouncement);

                var timing = RoutePlannerState.GetCurrentSegmentTiming(planetTile);
                if (timing.HasValue)
                {
                    string timeFromStart = timing.Value.ticksFromStart.ToStringTicksToDays("0.#");
                    builder.Add("RimWorldAccess.World.Tile.Movement.RouteArrival".Translate(timeFromStart));
                }
            }
            else
            {
                string caravanPathInfo = GetCaravanPathAnnouncement(planetTile, includeCaravansOnTile: true);
                if (!string.IsNullOrEmpty(caravanPathInfo))
                    builder.Add(caravanPathInfo);
            }

            if (Find.World.Impassable(planetTile))
            {
                builder.Add("RimWorldAccess.World.Tile.Movement.Impassable".Translate());
            }
            else
            {
                float difficulty = WorldPathGrid.CalculatedMovementDifficultyAt(planetTile, false, null, null);
                float roadMultiplier = Find.WorldGrid.GetRoadMovementDifficultyMultiplier(planetTile, PlanetTile.Invalid, null);
                float totalDifficulty = difficulty * roadMultiplier;
                builder.Add("RimWorldAccess.World.Tile.Movement.Difficulty".Translate(totalDifficulty.ToString("F1")));

                if (WorldPathGrid.WillWinterEverAffectMovementDifficulty(planetTile))
                {
                    float currentWinterOffset = WorldPathGrid.GetCurrentWinterMovementDifficultyOffset(planetTile);
                    builder.Add(currentWinterOffset > 0
                        ? "RimWorldAccess.World.Tile.Movement.WinterPenaltyCurrent".Translate(currentWinterOffset.ToString("F1"))
                        : "RimWorldAccess.World.Tile.Movement.WinterPenaltyDefault".Translate());
                }
            }

            if (tile.HillinessLabel != Hilliness.Undefined)
                builder.Add("RimWorldAccess.World.Tile.Movement.Terrain".Translate(tile.HillinessLabel.GetLabelCap()));

            builder.Add("RimWorldAccess.World.Tile.Movement.Elevation".Translate(tile.elevation.ToString("F0")));

            if (tile is SurfaceTile surfaceTile)
            {
                if (surfaceTile.Roads != null && surfaceTile.Roads.Count > 0)
                {
                    string roads = surfaceTile.Roads
                        .Select(r => r.road.label)
                        .Distinct()
                        .ToCommaList(useAnd: true);
                    string roadEntry = "RimWorldAccess.World.Tile.Movement.Road".Translate(roads.CapitalizeFirst());

                    string roadDirection = GetRoadDirectionDescription(planetTile, surfaceTile.Roads);
                    if (!string.IsNullOrEmpty(roadDirection))
                        roadEntry += " " + roadDirection;

                    builder.Add(roadEntry);
                }

                if (surfaceTile.Rivers != null && surfaceTile.Rivers.Count > 0)
                {
                    var largestRiver = surfaceTile.Rivers.MaxBy(r => r.river.degradeThreshold);
                    string riverEntry = "RimWorldAccess.World.Tile.Movement.River".Translate(largestRiver.river.LabelCap);

                    string riverDirection = GetRiverDirectionDescription(planetTile, surfaceTile.Rivers);
                    if (!string.IsNullOrEmpty(riverDirection))
                        riverEntry += " " + riverDirection;

                    builder.Add(riverEntry);
                }
            }

            if (tile.IsCoastal)
                builder.Add("RimWorldAccess.World.Tile.Movement.Coastal".Translate());

            return builder.Build();
        }

        /// <summary>
        /// Key 3: Health and Environment information.
        /// Disease frequency, pollution, noxious haze risk.
        /// </summary>
        public static string GetTileHealthInfo(PlanetTile planetTile)
        {
            if (!planetTile.Valid || Find.WorldGrid == null)
                return "RimWorldAccess.World.Tile.Invalid".Translate();

            if (planetTile.LayerDef?.isSpace == true)
                return "RimWorldAccess.World.Tile.Health.NoSpaceInfo".Translate();

            Tile tile = planetTile.Tile;
            if (tile == null)
                return "RimWorldAccess.World.Tile.Unknown".Translate();

            var builder = new AnnouncementBuilder().DefaultSep(Separator.Period);

            if (tile.PrimaryBiome?.diseaseMtbDays > 0)
            {
                float diseasesPerYear = 60f / tile.PrimaryBiome.diseaseMtbDays;
                builder.Add("RimWorldAccess.World.Tile.Health.DiseaseFrequency".Translate(diseasesPerYear.ToString("F1")));
            }
            else
            {
                builder.Add("RimWorldAccess.World.Tile.Health.DiseaseNone".Translate());
            }

            if (ModsConfig.BiotechActive)
            {
                builder.Add("RimWorldAccess.World.Tile.Health.TilePollution".Translate(tile.pollution.ToStringPercent()));

                float nearbyPollution = WorldPollutionUtility.CalculateNearbyPollutionScore(planetTile);
                builder.Add("RimWorldAccess.World.Tile.Health.NearbyPollution".Translate(nearbyPollution.ToString("F2")));

                if (nearbyPollution >= GameConditionDefOf.NoxiousHaze.minNearbyPollution)
                {
                    float hazeInterval = GameConditionDefOf.NoxiousHaze.mtbOverNearbyPollutionCurve.Evaluate(nearbyPollution);
                    builder.Add("RimWorldAccess.World.Tile.Health.NoxiousHazeInterval".Translate(hazeInterval.ToString("F0")));
                }
                else
                {
                    builder.Add("RimWorldAccess.World.Tile.Health.NoxiousHazeNever".Translate());
                }
            }

            return builder.Build();
        }

        /// <summary>
        /// Key 4: Location information.
        /// Coordinates, time zone, tile ID.
        /// </summary>
        public static string GetTileLocationInfo(PlanetTile planetTile)
        {
            if (!planetTile.Valid || Find.WorldGrid == null)
                return "RimWorldAccess.World.Tile.Invalid".Translate();

            var builder = new AnnouncementBuilder().DefaultSep(Separator.Period);

            Vector2 longlat = Find.WorldGrid.LongLatOf(planetTile);
            string latDir = longlat.y >= 0
                ? (string)"RimWorldAccess.World.Tile.Location.LatNorth".Translate()
                : (string)"RimWorldAccess.World.Tile.Location.LatSouth".Translate();
            string lonDir = longlat.x >= 0
                ? (string)"RimWorldAccess.World.Tile.Location.LonEast".Translate()
                : (string)"RimWorldAccess.World.Tile.Location.LonWest".Translate();
            builder.Add("RimWorldAccess.World.Tile.Location.Coordinates".Translate(
                Mathf.Abs(longlat.y).ToString("F1"), latDir,
                Mathf.Abs(longlat.x).ToString("F1"), lonDir));

            int timeZone = GenDate.TimeZoneAt(longlat.x);
            string tzStr = timeZone >= 0
                ? (string)"RimWorldAccess.World.Tile.Location.TimeZonePositive".Translate(timeZone)
                : timeZone.ToString();
            builder.Add("RimWorldAccess.World.Tile.Location.TimeZone".Translate(tzStr));

            builder.Add("RimWorldAccess.World.Tile.Location.TileId".Translate(planetTile.ToString()));

            return builder.Build();
        }

        /// <summary>
        /// Key 5: Tile Features and DLC information.
        /// Mutators (Odyssey), landmarks (Odyssey), caves.
        /// </summary>
        public static string GetTileFeaturesInfo(PlanetTile planetTile)
        {
            if (!planetTile.Valid || Find.WorldGrid == null)
                return "RimWorldAccess.World.Tile.Invalid".Translate();

            Tile tile = planetTile.Tile;
            if (tile == null)
                return "RimWorldAccess.World.Tile.Unknown".Translate();

            var builder = new AnnouncementBuilder().DefaultSep(Separator.Period);

            if (tile.Mutators.Any())
            {
                var mutatorParts = tile.Mutators
                    .OrderByDescending(m => m.displayPriority)
                    .Select(m =>
                    {
                        string label = m.Label(planetTile);
                        string desc = m.Description(planetTile);
                        return !string.IsNullOrEmpty(desc) && desc != label
                            ? (string)"RimWorldAccess.World.Tile.Features.MutatorWithDescription".Translate(label, desc)
                            : label;
                    })
                    .ToList();
                builder.Add("RimWorldAccess.World.Tile.Features.Mutators".Translate(string.Join(". ", mutatorParts)));
            }

            if (ModsConfig.OdysseyActive && tile.Landmark != null)
                builder.Add("RimWorldAccess.World.Tile.Features.Landmark".Translate(
                    tile.Landmark.name, tile.Landmark.def.LabelCap));

            if (tile.feature != null)
                builder.Add("RimWorldAccess.World.Tile.Features.Region".Translate(tile.feature.name));

            if (Find.World.HasCaves(planetTile))
                builder.Add("RimWorldAccess.World.Tile.Features.MayHaveCaves".Translate());

            string result = builder.Build();
            if (string.IsNullOrEmpty(result))
                return "RimWorldAccess.World.Tile.Features.None".Translate();
            return result;
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
            return BuildRoadDirection(fromTile, roads, inline: false);
        }

        // Lower-case mid-sentence form used by GetTileSummary so the
        // composition "Highway1 runs north to south" doesn't depend on
        // ToLower() over the capitalized phrase.
        private static string GetRoadDirectionInline(PlanetTile fromTile, List<SurfaceTile.RoadLink> roads)
        {
            return BuildRoadDirection(fromTile, roads, inline: true);
        }

        private static string BuildRoadDirection(PlanetTile fromTile, List<SurfaceTile.RoadLink> roads, bool inline)
        {
            if (roads == null || roads.Count == 0 || Find.WorldGrid == null)
                return null;

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
                return inline
                    ? "RimWorldAccess.World.Road.EndsHere.Inline".Translate()
                    : "RimWorldAccess.World.Road.EndsHere".Translate();
            }

            if (directions.Count == 1)
            {
                string dir = LocalizeCompass(directions[0]);
                return inline
                    ? "RimWorldAccess.World.Road.EndsContinues.Inline".Translate(dir)
                    : "RimWorldAccess.World.Road.EndsContinues".Translate(dir);
            }

            if (directions.Count == 2)
            {
                if (AreOppositeDirections8Way(directions[0], directions[1]))
                {
                    var ordered = OrderDirectionPair8Way(directions[0], directions[1]);
                    string a = LocalizeCompass(ordered.Item1);
                    string b = LocalizeCompass(ordered.Item2);
                    return inline
                        ? "RimWorldAccess.World.Road.Runs.Inline".Translate(a, b)
                        : "RimWorldAccess.World.Road.Runs".Translate(a, b);
                }
                else
                {
                    string a = LocalizeCompass(directions[0]);
                    string b = LocalizeCompass(directions[1]);
                    return inline
                        ? "RimWorldAccess.World.Road.Curves.Inline".Translate(a, b)
                        : "RimWorldAccess.World.Road.Curves".Translate(a, b);
                }
            }

            string list = directions.Select(LocalizeCompass).ToList().ToCommaList(useAnd: true);
            return inline
                ? "RimWorldAccess.World.Road.Junction.Inline".Translate(list)
                : "RimWorldAccess.World.Road.Junction".Translate(list);
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
            return BuildRiverDirection(fromTile, rivers, inline: false);
        }

        // Lower-case mid-sentence form used by GetTileSummary.
        private static string GetRiverDirectionInline(PlanetTile fromTile, List<SurfaceTile.RiverLink> rivers)
        {
            return BuildRiverDirection(fromTile, rivers, inline: true);
        }

        private static string BuildRiverDirection(PlanetTile fromTile, List<SurfaceTile.RiverLink> rivers, bool inline)
        {
            if (rivers == null || rivers.Count == 0 || Find.WorldGrid == null)
                return null;

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
                string dir = LocalizeCompass(directions[0]);
                return inline
                    ? "RimWorldAccess.World.River.Flows.Inline".Translate(dir)
                    : "RimWorldAccess.World.River.Flows".Translate(dir);
            }

            if (directions.Count == 2)
            {
                if (AreOppositeDirections8Way(directions[0], directions[1]))
                {
                    var ordered = OrderDirectionPair8Way(directions[0], directions[1]);
                    string a = LocalizeCompass(ordered.Item1);
                    string b = LocalizeCompass(ordered.Item2);
                    return inline
                        ? "RimWorldAccess.World.River.FlowsBetween.Inline".Translate(a, b)
                        : "RimWorldAccess.World.River.FlowsBetween".Translate(a, b);
                }
                else
                {
                    string a = LocalizeCompass(directions[0]);
                    string b = LocalizeCompass(directions[1]);
                    return inline
                        ? "RimWorldAccess.World.River.Bends.Inline".Translate(a, b)
                        : "RimWorldAccess.World.River.Bends".Translate(a, b);
                }
            }

            string list = directions.Select(LocalizeCompass).ToList().ToCommaList(useAnd: true);
            return inline
                ? "RimWorldAccess.World.River.Confluence.Inline".Translate(list)
                : "RimWorldAccess.World.River.Confluence".Translate(list);
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

                    string caravanName = caravan.Label ?? (string)"RimWorldAccess.World.Caravan.DefaultLabel".Translate();

                    // Get direction to next tile on path
                    string direction = GetCaravanTravelDirection(caravan);
                    if (!string.IsNullOrEmpty(direction))
                    {
                        string destination = GetCaravanDestinationName(caravan);
                        string dirLocalized = LocalizeCompass(direction);
                        if (!string.IsNullOrEmpty(destination))
                            pathAnnouncements.Add("RimWorldAccess.World.Caravan.PathHeadingTowardOnTile".Translate(caravanName, dirLocalized, destination));
                        else
                            pathAnnouncements.Add("RimWorldAccess.World.Caravan.PathHeadingOnTile".Translate(caravanName, dirLocalized));
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

                    string caravanName = caravan.Label ?? (string)"RimWorldAccess.World.Caravan.DefaultLabel".Translate();

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
                            pathAnnouncements.Add("RimWorldAccess.World.Caravan.PathWillHead".Translate(caravanName, LocalizeCompass(direction)));
                        }
                    }
                }
            }

            // Build the combined announcement
            List<string> allAnnouncements = new List<string>();

            // Add stopping announcement if any caravans stop here
            if (stoppingCaravans.Count > 0)
            {
                string stoppingText = "RimWorldAccess.World.Caravan.PathWillStopHere".Translate(FormatCaravanList(stoppingCaravans));
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
        /// Delegates to the game's ToCommaList(useAnd: true) so the conjunction
        /// honours the active language (vanilla "AndListItem" translation key).
        /// </summary>
        private static string FormatCaravanList(List<string> names)
        {
            if (names.Count == 0)
                return "";
            return names.ToCommaList(useAnd: true);
        }

        // Formats a single caravan label with an optional parenthetical
        // detail (foreign-faction name for non-player caravans, or short
        // status for player caravans). Used by GetTileSummary's caravan
        // listing.
        private static string FormatCaravanWithDetail(Caravan caravan)
        {
            string label = caravan.Label;
            string detail;
            if (caravan.Faction != null && caravan.Faction != Faction.OfPlayer)
            {
                detail = caravan.Faction.Name;
            }
            else
            {
                detail = GetCaravanShortStatus(caravan);
            }
            if (string.IsNullOrEmpty(detail))
                return label;
            return "RimWorldAccess.World.Tile.Caravan.WithDetailParenthetical".Translate(label, detail);
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
                return "RimWorldAccess.World.Caravan.Status.AllDowned".Translate();

            if (caravan.AllOwnersHaveMentalBreak)
                return "RimWorldAccess.World.Caravan.Status.MentalBreak".Translate();

            if (caravan.ImmobilizedByMass)
                return "RimWorldAccess.World.Caravan.Status.Overloaded".Translate();

            // Check if visiting a settlement
            Settlement visitedSettlement = CaravanVisitUtility.SettlementVisitedNow(caravan);
            if (visitedSettlement != null)
                return "RimWorldAccess.World.Caravan.Status.Visiting".Translate(visitedSettlement.Label);

            // Check movement status
            if (caravan.pather != null && caravan.pather.Moving)
            {
                if (caravan.pather.Paused)
                    return "RimWorldAccess.World.Caravan.Status.Paused".Translate();

                if (caravan.NightResting)
                    return "RimWorldAccess.World.Caravan.Status.Resting".Translate();

                // Actively traveling - get direction and destination
                string direction = GetCaravanTravelDirection(caravan);
                string destination = GetCaravanDestinationName(caravan);

                if (!string.IsNullOrEmpty(direction) && !string.IsNullOrEmpty(destination))
                    return "RimWorldAccess.World.Caravan.Status.HeadingDirectionToward".Translate(LocalizeCompass(direction), destination);
                else if (!string.IsNullOrEmpty(destination))
                    return "RimWorldAccess.World.Caravan.Status.TravelingTo".Translate(destination);
                else if (!string.IsNullOrEmpty(direction))
                    return "RimWorldAccess.World.Caravan.Status.HeadingDirection".Translate(LocalizeCompass(direction));
                else
                    return "RimWorldAccess.World.Caravan.Status.Traveling".Translate();
            }

            // Not moving
            if (caravan.NightResting)
                return "RimWorldAccess.World.Caravan.Status.Resting".Translate();

            return "RimWorldAccess.World.Caravan.Status.Stopped".Translate();
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
