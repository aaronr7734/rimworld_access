using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Extracts information about world tiles and settlements for the world-map announcements.</summary>
    public static class WorldInfoHelper
    {
        // Compass tokens stay English inside this class so the dictionary lookups (opposite
        // direction, priority ordering) keep working; localization happens at announcement time.
        // Internal so RoutePlannerState can localize the same tokens in its own announcements.
        internal static string LocalizeCompass(string englishCompass)
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
        /// A brief tile summary for navigation announcements. <paramref name="includeRouteInfo"/>
        /// adds the waypoint prefix and route-planner info; <paramref name="minimal"/> keeps only
        /// biome, hilliness, temperature and world objects; <paramref name="fuelCostInfo"/> is
        /// inserted right after the biome name.
        /// </summary>
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

        /// <summary>Detailed tile information, for the Info key.</summary>
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

            // Mirrors the "Debug world tile ID" line both WITab_Terrain and WITab_Orbit append
            // under Prefs.DevMode; this builder covers the surface and space tabs alike.
            if (Prefs.DevMode)
            {
                builder.Add("RimWorldAccess.Dev.Info.WorldTileId".Translate(planetTile.ToString()));
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

        /// <summary>Describes a settlement: name, faction, relationship, goodwill, visitable/attackable status.</summary>
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

        /// <summary>All settlements, nearest first from <paramref name="fromTile"/>.</summary>
        public static List<Settlement> GetSettlementsByDistance(PlanetTile fromTile)
        {
            if (!fromTile.Valid || Find.WorldObjects?.Settlements == null || Find.WorldGrid == null)
                return new List<Settlement>();

            return Find.WorldObjects.Settlements
                .OrderBy(s => Find.WorldGrid.ApproxDistanceInTiles(fromTile, s.Tile))
                .ToList();
        }

        /// <summary>The first player settlement, or null.</summary>
        public static Settlement GetPlayerHomeSettlement()
        {
            if (Find.WorldObjects?.Settlements == null)
                return null;

            return Find.WorldObjects.Settlements
                .FirstOrDefault(s => s.Faction == Faction.OfPlayer);
        }

        public static List<Caravan> GetPlayerCaravans()
        {
            if (Find.WorldObjects?.Caravans == null)
                return new List<Caravan>();

            return Find.WorldObjects.Caravans
                .Where(c => c.Faction == Faction.OfPlayer)
                .ToList();
        }

        /// <summary>A caravan's status for cycle announcements: current activity first, then destination.</summary>
        public static string GetCaravanStatus(Caravan caravan)
        {
            if (caravan == null)
                return "RimWorldAccess.World.Caravan.Status.Unknown".Translate();

            if (caravan.AllOwnersDowned)
                return "RimWorldAccess.World.Caravan.Status.AllDowned".Translate();

            if (caravan.AllOwnersHaveMentalBreak)
                return "RimWorldAccess.World.Caravan.Status.MentalBreak".Translate();

            if (caravan.ImmobilizedByMass)
                return "RimWorldAccess.World.Caravan.Status.Overloaded".Translate();

            // Arrival outranks movement state.
            Settlement visitedSettlement = CaravanVisitUtility.SettlementVisitedNow(caravan);
            if (visitedSettlement != null)
                return "RimWorldAccess.World.Caravan.Status.Visiting".Translate(visitedSettlement.Label);

            if (caravan.pather.Moving)
            {
                if (caravan.pather.Paused)
                    return "RimWorldAccess.World.Caravan.Status.Paused".Translate() + GetDestinationSuffix(caravan, active: false);

                if (caravan.NightResting)
                {
                    return BuildRestingStatus(caravan) + GetDestinationSuffix(caravan, active: false);
                }

                return "RimWorldAccess.World.Caravan.Status.Traveling".Translate() + GetDestinationSuffix(caravan, active: true);
            }

            // Not moving: the Destination field may hold stale data, so it is not read here.
            if (caravan.NightResting)
            {
                return BuildRestingStatus(caravan);
            }

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
        /// Where the caravan is headed. An arrival action's own ReportString is appended unmodified;
        /// otherwise the destination comes from the destination tile's world object, phrased
        /// "to X" while <paramref name="active"/> and ". To X" while paused or resting.
        /// </summary>
        private static string GetDestinationSuffix(Caravan caravan, bool active)
        {
            if (caravan?.pather == null)
                return "";

            // The game's ReportString is already fully localized; never hand-roll -ing/infinitive
            // morphology on top of it.
            if (caravan.pather.ArrivalAction != null)
            {
                string report = caravan.pather.ArrivalAction.ReportString;
                if (!string.IsNullOrEmpty(report))
                {
                    string trimmedReport = report.TrimEnd('.');
                    return "RimWorldAccess.World.Caravan.SuffixReport".Translate(trimmedReport);
                }
            }

            // No usable report: derive the destination from the game's own destination-tile object
            // rather than string-matching a localized report.
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

        /// <summary>Quests targeting this tile, with difficulty and a brief description, or null.</summary>
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

                    // First two sentences, or 250 chars. The no-defer overload reads a marker while
                    // the description is being rewritten: this summary is rebuilt every time the
                    // cursor revisits the tile, so waiting for it to settle would buy nothing.
                    string questDesc = RimTalkQuestsCompat.GetDescriptionOrMarker(quest).StripTags();
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

        /// <summary>Quests targeting this tile, in full, for the Info key's detailed view.</summary>
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
                // Marker-only, no-defer read: this view is rebuilt fresh on every press.
                string questDesc = RimTalkQuestsCompat.GetDescriptionOrMarker(quest).StripTags();

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

        /// <summary>Key 1: growing period, forageability, grazing, rainfall, stone types.</summary>
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
        /// Key 2: movement difficulty, winter penalty, roads, rivers, elevation, coastal. On a
        /// planned route, path direction and travel times come FIRST, for quick path tracing.
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

        /// <summary>Key 3: disease frequency, pollution, noxious haze risk.</summary>
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

        /// <summary>Key 4: coordinates and time zone.</summary>
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

            // The raw tile index is dev-only data a sighted player never sees, so it is not spoken.

            return builder.Build();
        }

        /// <summary>Key 5: mutators, landmarks, region feature, caves.</summary>
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
        /// Which directions the roads on this tile run, in 8-way compass terms for accuracy on the
        /// hex grid: "runs north to south", "curves from north to east", "ends here", or a junction.
        /// </summary>
        private static string GetRoadDirectionDescription(PlanetTile fromTile, List<SurfaceTile.RoadLink> roads)
        {
            return BuildRoadDirection(fromTile, roads, inline: false);
        }

        // Lower-case mid-sentence form, so composing "Highway1 runs north to south" never depends
        // on ToLower() over a capitalized phrase.
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
        /// Which directions the rivers on this tile flow, in 8-way compass terms. Rivers use "flows"
        /// language because, unlike roads, they have a direction.
        /// </summary>
        private static string GetRiverDirectionDescription(PlanetTile fromTile, List<SurfaceTile.RiverLink> rivers)
        {
            return BuildRiverDirection(fromTile, rivers, inline: false);
        }

        // Lower-case mid-sentence form.
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
        /// Which arrow key moves from one tile to a neighbor, matching
        /// WorldNavigationState.MoveInDirection's own selection logic: a cardinal name when one key
        /// reaches it, otherwise an 8-way compass name, signalling that two presses are needed.
        /// </summary>
        internal static string GetArrowKeyDirection(PlanetTile fromTile, PlanetTile toTile)
        {
            if (!fromTile.Valid || !toTile.Valid || Find.WorldGrid == null)
                return null;

            Vector3 currentPos = Find.WorldGrid.GetTileCenter(fromTile);
            Vector3 targetPos = Find.WorldGrid.GetTileCenter(toTile);
            ScannerDirectionHelper.TangentBasis(currentPos, out Vector3 north, out Vector3 east);

            // Only the four cardinals the arrow keys support.
            if (WorldNavigationState.BestNeighborToward(fromTile, north).tileId == toTile.tileId)
                return "north";
            if (WorldNavigationState.BestNeighborToward(fromTile, east).tileId == toTile.tileId)
                return "east";
            if (WorldNavigationState.BestNeighborToward(fromTile, -north).tileId == toTile.tileId)
                return "south";
            if (WorldNavigationState.BestNeighborToward(fromTile, -east).tileId == toTile.tileId)
                return "west";

            // Not reachable in one press: the 8-way name tells the player two are needed.
            return GetCompassDirection(currentPos, targetPos, north, east);
        }

        /// <summary>
        /// The 8-way compass direction between two positions. For directions that must match arrow
        /// key navigation, use <see cref="GetArrowKeyDirection"/> instead.
        /// </summary>
        internal static string GetCompassDirection(Vector3 fromPos, Vector3 toPos, Vector3 north, Vector3 east)
        {
            Vector3 up = fromPos.normalized;
            Vector3 direction = (toPos - fromPos).normalized;

            float northComponent = Vector3.Dot(direction, north);
            float eastComponent = Vector3.Dot(direction, east);

            // Cardinal vs diagonal cutoff, about 22.5 degrees.
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

        /// <summary>Orders a direction pair consistently: north before south, east before west, diagonals by primary component.</summary>
        private static (string, string) OrderDirectionPair8Way(string dir1, string dir2)
        {
            var priority = new Dictionary<string, int>
            {
                {"north", 0}, {"northeast", 1}, {"east", 2}, {"southeast", 3},
                {"south", 4}, {"southwest", 5}, {"west", 6}, {"northwest", 7}
            };

            if (priority.TryGetValue(dir1, out int p1) && priority.TryGetValue(dir2, out int p2))
            {
                if (p1 > p2)
                    return (dir2, dir1);
            }
            return (dir1, dir2);
        }

        #endregion

        #region Caravan Path Helpers

        /// <summary>
        /// Caravan paths passing through this tile ("Sam's caravan will head north"), or null.
        /// Considers the multi-selected caravans if any, otherwise the focused one;
        /// <paramref name="includeCaravansOnTile"/> widens it to every moving caravan on the tile.
        /// </summary>
        internal static string GetCaravanPathAnnouncement(PlanetTile tile, bool includeCaravansOnTile = false)
        {
            if (!tile.Valid || Find.WorldGrid == null)
                return null;

            List<string> pathAnnouncements = new List<string>();
            List<string> stoppingCaravans = new List<string>();
            HashSet<int> processedCaravanIds = new HashSet<int>();

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

            var caravansToCheck = GetSelectedCaravansForPathCheck();
            if (caravansToCheck != null && caravansToCheck.Count > 0)
            {
                foreach (var caravan in caravansToCheck)
                {
                    if (caravan == null || caravan.Destroyed)
                        continue;

                    if (processedCaravanIds.Contains(caravan.ID))
                        continue;

                    // A paused caravan still has a path and a destination.
                    if (caravan.pather == null || caravan.pather.curPath == null)
                        continue;

                    WorldPath path = caravan.pather.curPath;
                    if (!path.Found || path.NodesLeftCount <= 0)
                        continue;

                    string caravanName = caravan.Label ?? (string)"RimWorldAccess.World.Caravan.DefaultLabel".Translate();

                    if (IsCaravanDestination(tile, path))
                    {
                        stoppingCaravans.Add(caravanName);
                    }
                    // A caravan ON this tile is covered above, or by its status line.
                    else if (caravan.Tile.tileId != tile.tileId)
                    {
                        string direction = GetCaravanPathDirection(tile, path);

                        if (!string.IsNullOrEmpty(direction))
                        {
                            pathAnnouncements.Add("RimWorldAccess.World.Caravan.PathWillHead".Translate(caravanName, LocalizeCompass(direction)));
                        }
                    }
                }
            }

            List<string> allAnnouncements = new List<string>();

            if (stoppingCaravans.Count > 0)
            {
                string stoppingText = "RimWorldAccess.World.Caravan.PathWillStopHere".Translate(FormatCaravanList(stoppingCaravans));
                allAnnouncements.Add(stoppingText);
            }

            allAnnouncements.AddRange(pathAnnouncements);

            if (allAnnouncements.Count == 0)
                return null;

            return string.Join(" ", allAnnouncements);
        }

        private static bool IsCaravanDestination(PlanetTile tile, WorldPath path)
        {
            if (!tile.Valid || path == null || !path.Found || path.NodesLeftCount <= 0)
                return false;

            // The destination is the last remaining node.
            PlanetTile destTile = path.Peek(path.NodesLeftCount - 1);
            return destTile.tileId == tile.tileId;
        }

        /// <summary>Formats caravan names as "A", "A and B", or "A, B, and C" through the game's own localized conjunction.</summary>
        private static string FormatCaravanList(List<string> names)
        {
            if (names.Count == 0)
                return "";
            return names.ToCommaList(useAnd: true);
        }

        // Caravan label plus a parenthetical: the faction name for foreign caravans, a short
        // status for the player's own.
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

        /// <summary>The caravans to check for path announcements: explicit multi-selections first, else the focused caravan.</summary>
        private static List<Caravan> GetSelectedCaravansForPathCheck()
        {
            var multiSelected = WorldNavigationState.GetMultiSelectedCaravans();
            if (multiSelected != null && multiSelected.Count > 0)
            {
                return multiSelected.ToList();
            }

            var focusedCaravan = WorldNavigationState.GetSelectedCaravan();
            if (focusedCaravan != null)
            {
                return new List<Caravan> { focusedCaravan };
            }

            return null;
        }

        /// <summary>The direction the caravan will travel from this tile, or null when the tile is not on its remaining path.</summary>
        private static string GetCaravanPathDirection(PlanetTile tile, WorldPath path)
        {
            int tileId = tile.tileId;

            // Peek indexes the remaining path with the HIGHEST index closest to the destination.
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

            if (tileIndex >= path.NodesLeftCount - 1)
                return null;

            PlanetTile nextTile = path.Peek(tileIndex + 1);
            if (!nextTile.Valid)
                return null;

            return GetArrowKeyDirection(tile, nextTile);
        }

        /// <summary>A short caravan status: "heading north toward X" while traveling, otherwise resting, paused, stopped or overloaded.</summary>
        internal static string GetCaravanShortStatus(Caravan caravan)
        {
            if (caravan == null)
                return null;

            if (caravan.AllOwnersDowned)
                return "RimWorldAccess.World.Caravan.Status.AllDowned".Translate();

            if (caravan.AllOwnersHaveMentalBreak)
                return "RimWorldAccess.World.Caravan.Status.MentalBreak".Translate();

            if (caravan.ImmobilizedByMass)
                return "RimWorldAccess.World.Caravan.Status.Overloaded".Translate();

            Settlement visitedSettlement = CaravanVisitUtility.SettlementVisitedNow(caravan);
            if (visitedSettlement != null)
                return "RimWorldAccess.World.Caravan.Status.Visiting".Translate(visitedSettlement.Label);

            if (caravan.pather != null && caravan.pather.Moving)
            {
                if (caravan.pather.Paused)
                    return "RimWorldAccess.World.Caravan.Status.Paused".Translate();

                if (caravan.NightResting)
                    return "RimWorldAccess.World.Caravan.Status.Resting".Translate();

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

            if (caravan.NightResting)
                return "RimWorldAccess.World.Caravan.Status.Resting".Translate();

            return "RimWorldAccess.World.Caravan.Status.Stopped".Translate();
        }

        /// <summary>The direction the caravan is heading from its current tile.</summary>
        private static string GetCaravanTravelDirection(Caravan caravan)
        {
            if (caravan?.pather?.curPath == null || !caravan.pather.curPath.Found)
                return null;

            WorldPath path = caravan.pather.curPath;
            if (path.NodesLeftCount < 1)
                return null;

            PlanetTile currentTile = caravan.Tile;
            PlanetTile nextTile = PlanetTile.Invalid;

            PlanetTile immediateTile = path.Peek(0);
            if (immediateTile.tileId != currentTile.tileId)
            {
                nextTile = immediateTile;
            }
            else if (path.NodesLeftCount >= 2)
            {
                nextTile = path.Peek(1);
            }
            else
            {
                // Only the destination remains, and the caravan is already on it.
                return null;
            }

            if (!nextTile.Valid || Find.WorldGrid == null)
                return null;

            return GetArrowKeyDirection(currentTile, nextTile);
        }

        /// <summary>The destination name for a traveling caravan.</summary>
        private static string GetCaravanDestinationName(Caravan caravan)
        {
            if (caravan?.pather == null || !caravan.pather.Destination.Valid)
                return null;

            // From the destination-tile object itself: ArrivalAction.ReportString is a full
            // localized clause, never a bare place name, so parsing it here would misuse it.
            var destObjects = Find.WorldObjects?.ObjectsAt(caravan.pather.Destination);
            var destObject = destObjects?.FirstOrDefault();
            return destObject?.Label;
        }

        #endregion
    }
}
