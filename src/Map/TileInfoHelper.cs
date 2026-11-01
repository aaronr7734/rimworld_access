using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Queries and formats spoken information about map tiles: the one-line summary plus the
    /// per-aspect readouts behind the number keys.
    /// </summary>
    public static class TileInfoHelper
    {
        // TerrainDef exposes smoothedTerrain (rough -> smoothed) but no reverse flag, so the
        // smoothed *results* are collected from DefDatabase once.
        private static HashSet<TerrainDef> _smoothedResultTerrainCache;

        private static bool IsSmoothedResultTerrain(TerrainDef terrain)
        {
            if (_smoothedResultTerrainCache == null)
            {
                _smoothedResultTerrainCache = new HashSet<TerrainDef>();
                foreach (TerrainDef def in DefDatabase<TerrainDef>.AllDefsListForReading)
                {
                    if (def.smoothedTerrain != null)
                        _smoothedResultTerrainCache.Add(def.smoothedTerrain);
                }
            }

            return _smoothedResultTerrainCache.Contains(terrain);
        }

        /// <summary>
        /// A concise summary of what is on a tile: contents, then terrain and roof, then
        /// coordinates.
        /// </summary>
        public static string GetTileSummary(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return "RimWorldAccess.Map.Tile.OutOfBounds".Translate();

            if (position.Fogged(map))
            {
                string fogDesignations = GetDesignationsInfo(position, map);
                if (!string.IsNullOrEmpty(fogDesignations))
                    return "RimWorldAccess.Map.Tile.UnseenWithDesignations".Translate(fogDesignations, position.x, position.z);
                return "RimWorldAccess.Map.Tile.Unseen".Translate(position.x, position.z);
            }

            bool notVisible = false;
            Pawn selectedPawn = Find.Selector?.FirstSelectedObject as Pawn;
            if (selectedPawn != null && selectedPawn.Drafted && selectedPawn.Spawned && selectedPawn.Map == map)
            {
                if (!GenSight.LineOfSight(selectedPawn.Position, position, map))
                    notVisible = true;
            }

            var thingDesignations = new Dictionary<Thing, List<Designation>>();
            var cellDesignations = new List<Designation>();
            foreach (var designation in map.designationManager.AllDesignationsAt(position))
            {
                if (designation.target.HasThing)
                {
                    if (!thingDesignations.TryGetValue(designation.target.Thing, out var dlist))
                    {
                        dlist = new List<Designation>();
                        thingDesignations[designation.target.Thing] = dlist;
                    }
                    dlist.Add(designation);
                }
                else
                {
                    cellDesignations.Add(designation);
                }
            }

            var sortedThings = position.GetThingList(map)
                .Where(t => !(t is Mote) && t.def.category != ThingCategory.Mote)
                .OrderByDescending(t => (int)t.def.altitudeLayer)
                .ToList();

            var pawns = new List<Pawn>();
            var nonPawnThings = new List<Thing>();
            bool hasBuildings = false;
            foreach (var thing in sortedThings)
            {
                if (thing is Pawn pawn)
                {
                    if (!HiddenPawns.IsHidden(pawn))
                        pawns.Add(pawn);
                }
                else
                {
                    nonPawnThings.Add(thing);
                    if (thing is Building)
                        hasBuildings = true;
                }
            }

            var builder = new AnnouncementBuilder().DefaultSep(Separator.Comma);

            foreach (var designation in cellDesignations)
                builder.Add(GetDesignationLabel(designation));

            if (pawns.Count > 0)
                builder.Add(FormatPawnsForTileSummary(pawns, thingDesignations));

            foreach (var thing in nonPawnThings)
                AppendThingSummary(builder, thing, position, map, thingDesignations);

            TerrainDef terrain = position.GetTerrain(map);
            if (terrain != null && RimWorldAccessMod_Settings.Settings.AnnounceTerrain)
            {
                bool isPolluted = position.IsPolluted(map);
                string terrainLabel = isPolluted
                    ? (string)"PollutedTerrain".Translate(terrain.label).CapitalizeFirst()
                    : (string)terrain.LabelCap;
                if (IsSmoothedResultTerrain(terrain))
                    terrainLabel = "RimWorldAccess.Map.Tile.FloorLabel".Translate(terrainLabel);
                ColorDef floorPaint = map.terrainGrid.ColorAt(position);
                if (floorPaint != null && !floorPaint.label.NullOrEmpty())
                    terrainLabel += "RimWorldAccess.Map.Tile.PaintSuffix".Translate(floorPaint.LabelCap);
                builder.Add(terrainLabel);
            }

            // The roof's own label distinguishes thin, thick, and mountain roofs.
            RoofDef roof = position.GetRoof(map);
            if (roof != null)
                builder.Add(roof.LabelCap);

            if (!hasBuildings)
            {
                string fuelingPortInfo = GetEmptyFuelingPortInfo(position, map);
                if (!string.IsNullOrEmpty(fuelingPortInfo))
                    builder.Add(fuelingPortInfo);
            }

            Zone zone = position.GetZone(map);
            if (zone != null)
                builder.Add(zone.label);

            // Plans are a sibling overlay of zones (map.planManager), neither terrain nor Thing.
            Plan plan = map.planManager.PlanAt(position);
            if (plan != null)
                builder.Add("RimWorldAccess.Map.Tile.Plan".Translate(
                    PlanColorHelper.ColorName(plan.Color), plan.RenamableLabel));

            builder.Add("RimWorldAccess.Map.Tile.Coords".Translate(position.x, position.z));

            if (IsDropPodLandingTargeting() &&
                !DropCellFinder.IsGoodDropSpot(position, map, allowFogged: false, canRoofPunch: true))
            {
                builder.Add("RimWorldAccess.Map.Tile.CantLand".Translate());
            }

            if (notVisible)
                builder.Add("RimWorldAccess.Map.Tile.NotVisible".Translate());

            return builder.Build();
        }

        private static void AppendThingSummary(AnnouncementBuilder builder, Thing thing, IntVec3 position, Map map,
            Dictionary<Thing, List<Designation>> thingDesignations)
        {
            if (thing is Frame frame)
            {
                string label = (string)frame.LabelEntityToBuild;
                string frameCellInfo = BuildingCellHelper.GetCellPrefix(frame, position);
                if (!string.IsNullOrEmpty(frameCellInfo))
                    label += "RimWorldAccess.Map.Tile.CellSuffix".Translate(frameCellInfo);
                label += ComposeThingDesignationSuffix(thing, thingDesignations);

                builder.Add(label);
                builder.Add("RimWorldAccess.Map.Tile.Frame.Building".Translate());
                builder.Add(frame.IsCompleted()
                    ? "RimWorldAccess.Map.Tile.Frame.WorkLeft".Translate(frame.WorkLeft.ToStringWorkAmount())
                    : "RimWorldAccess.Map.Tile.Frame.AwaitingSupplies".Translate());
            }
            else if (thing is Building building)
            {
                string label = building.LabelShort;
                if (building.def.IsSmoothed)
                    label = "RimWorldAccess.Map.Tile.WallLabel".Translate(label);
                if (building is Building_Door door)
                {
                    label = (door.Open
                        ? "RimWorldAccess.Map.Label.WithDoorOpen"
                        : "RimWorldAccess.Map.Label.WithDoorClosed").Translate(label);
                }

                string cellInfo = BuildingCellHelper.GetCellPrefix(building, position);
                if (!string.IsNullOrEmpty(cellInfo))
                    label += "RimWorldAccess.Map.Tile.CellSuffix".Translate(cellInfo);

                if (building.PaintColorDef != null && !building.PaintColorDef.label.NullOrEmpty())
                    label += "RimWorldAccess.Map.Tile.PaintSuffix".Translate(building.PaintColorDef.LabelCap);

                builder.Add(label + ComposeThingDesignationSuffix(thing, thingDesignations));

                string tempControlInfo = GetTemperatureControlInfo(building);
                if (!string.IsNullOrEmpty(tempControlInfo))
                    builder.Add(tempControlInfo);

                string transportPodInfo = GetTransportPodInfo(building, map);
                if (!string.IsNullOrEmpty(transportPodInfo))
                    builder.Add(transportPodInfo);

                string progressInfo = GetBuildingProgressInfo(building);
                if (!string.IsNullOrEmpty(progressInfo))
                    builder.Add(progressInfo);

                if (building is IStorageGroupMember storageMember && storageMember.Group != null)
                    builder.Add(storageMember.Group.RenamableLabel);
            }
            else if (thing is Blueprint blueprint)
            {
                string label = blueprint.LabelShort;
                string cellInfo = BuildingCellHelper.GetCellPrefix(blueprint, position);
                if (!string.IsNullOrEmpty(cellInfo))
                    label += "RimWorldAccess.Map.Tile.CellSuffix".Translate(cellInfo);

                builder.Add(label + ComposeThingDesignationSuffix(thing, thingDesignations));

                if (blueprint is Blueprint_Storage blueprintStorage
                    && ((IStorageGroupMember)blueprintStorage).Group is StorageGroup bpGroup)
                {
                    builder.Add(bpGroup.RenamableLabel);
                }
            }
            else if (thing is Plant plant)
            {
                builder.Add(plant.LabelCap + ComposeThingDesignationSuffix(thing, thingDesignations));
            }
            else if (thing is UnfinishedThing unfinished)
            {
                builder.Add(unfinished.LabelShort + ComposeThingDesignationSuffix(thing, thingDesignations));
                if (unfinished.Initialized)
                    builder.Add("RimWorldAccess.Map.Tile.WorkLeftAppend".Translate(unfinished.workLeft.ToStringWorkAmount()));
            }
            else
            {
                string itemLabel = thing.LabelMouseover;
                CompForbiddable forbiddable = thing.TryGetComp<CompForbiddable>();
                if (forbiddable != null && forbiddable.Forbidden)
                    itemLabel = "RimWorldAccess.Map.Tile.ForbiddenPrefix".Translate(itemLabel);
                builder.Add(itemLabel + ComposeThingDesignationSuffix(thing, thingDesignations));
            }
        }

        private static string ComposeThingDesignationSuffix(Thing thing, Dictionary<Thing, List<Designation>> thingDesignations)
        {
            if (!thingDesignations.TryGetValue(thing, out var thingDesigs))
                return string.Empty;

            return string.Concat(thingDesigs.Select(d =>
                (string)"RimWorldAccess.Map.Tile.CellSuffix".Translate(GetDesignationLabel(d))));
        }

        /// <summary>Items (with stack counts) and pawns at a tile, for key 1.</summary>
        public static string GetItemsAndPawnsInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return "RimWorldAccess.Map.Tile.OutOfBounds".Translate();

            List<Thing> things = position.GetThingList(map);
            // Vanilla's own cell readouts never name a pawn it hides from the player.
            var pawns = things.OfType<Pawn>().Where(p => !HiddenPawns.IsHidden(p)).ToList();
            var items = things.Where(t => !(t is Pawn) && !(t is Building) && !(t is Plant)
                && !(t is Mote) && t.def.category != ThingCategory.Mote).ToList();

            if (pawns.Count == 0 && items.Count == 0)
                return "RimWorldAccess.Map.Tile.Items.None".Translate();

            var builder = new AnnouncementBuilder().DefaultSep(Separator.Comma);

            foreach (var pawn in pawns)
                builder.Add(pawn.LabelShortCap + (GetPawnSuffix(pawn) ?? string.Empty));

            const int displayLimit = 10;
            for (int i = 0; i < items.Count && i < displayLimit; i++)
            {
                string label = items[i].LabelShortCap;
                if (items[i].stackCount > 1)
                    label += "RimWorldAccess.Map.Tile.Item.StackCount".Translate(items[i].stackCount);

                CompForbiddable forbiddable = items[i].TryGetComp<CompForbiddable>();
                if (forbiddable != null && forbiddable.Forbidden)
                    label = "RimWorldAccess.Map.Tile.ForbiddenPrefix".Translate(label);

                builder.Add(label);
            }

            if (items.Count > displayLimit)
                builder.Add("RimWorldAccess.Map.Tile.Items.MoreSuffix".Translate(items.Count - displayLimit));

            string result = builder.Build();

            // The hit-chance breakdown a sighted player gets on mouse-over. Each report names its
            // own target, and it goes last so it never runs into the item list.
            var reports = new List<string>();
            foreach (var pawn in pawns)
            {
                string shotReport = ShotReportHelper.GetShotReportFor(pawn);
                if (shotReport != null)
                    reports.Add(shotReport);
            }
            if (reports.Count > 0)
                result += ". " + string.Join(". ", reports);

            return result;
        }

        /// <summary>Terrain type, smoothness, beauty, and cleanliness at a tile, for key 2.</summary>
        public static string GetFlooringInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return "RimWorldAccess.Map.Tile.OutOfBounds".Translate();

            TerrainDef terrain = position.GetTerrain(map);
            if (terrain == null)
                return "RimWorldAccess.Map.Tile.Flooring.None".Translate();

            bool isPolluted = position.IsPolluted(map);
            string terrainLabel = isPolluted
                ? (string)"PollutedTerrain".Translate(terrain.label).CapitalizeFirst()
                : (string)terrain.LabelCap;

            var builder = new AnnouncementBuilder().DefaultSep(Separator.Comma);
            builder.Add(terrainLabel);

            ColorDef floorPaint = map.terrainGrid.ColorAt(position);
            if (floorPaint != null && !floorPaint.label.NullOrEmpty())
                builder.Add(floorPaint.LabelCap);

            float fertility = position.GetFertility(map);
            if (fertility > 0.0001f)
                builder.Add("RimWorldAccess.Map.Tile.Flooring.Fertility".Translate(fertility.ToStringPercent()));

            if (IsSmoothedResultTerrain(terrain))
                builder.Add("RimWorldAccess.Map.Tile.Flooring.Smooth".Translate());
            else if (terrain.smoothedTerrain != null)
                builder.Add("RimWorldAccess.Map.Tile.Flooring.Rough".Translate());

            float beauty = terrain.GetStatValueAbstract(StatDefOf.Beauty);
            if (beauty != 0)
                builder.Add("RimWorldAccess.Map.Tile.Flooring.Beauty".Translate(beauty.ToString("F0")));

            float cleanliness = terrain.GetStatValueAbstract(StatDefOf.Cleanliness);
            if (cleanliness != 0)
                builder.Add("RimWorldAccess.Map.Tile.Flooring.Cleanliness".Translate(cleanliness.ToString("F1")));

            if (terrain.pathCost > 0)
                builder.Add("RimWorldAccess.Map.Tile.Flooring.PathCost".Translate(terrain.pathCost));

            return builder.Build();
        }

        /// <summary>
        /// Resources at a tile, for key 3: plants (species, growth, harvestable), Odyssey fish
        /// (species and current/max population, as vanilla's mouseover shows), and deep mineral
        /// deposits. Deep ore appears only under an active powered scanner, and the empty message
        /// names only the resource types this tile's context makes relevant.
        /// </summary>
        public static string GetPlantsInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return "RimWorldAccess.Map.Tile.OutOfBounds".Translate();

            List<Thing> things = position.GetThingList(map);
            var plants = things.OfType<Plant>().ToList();
            bool hasPlants = plants.Count > 0;

            bool scannerActive = map.deepResourceGrid.AnyActiveDeepScannersOnMap();
            string deepOreInfo = scannerActive ? GetDeepOreInfo(position, map) : null;
            bool hasDeepOre = !string.IsNullOrEmpty(deepOreInfo);

            WaterBody waterBody = null;
            bool onWaterBody = ModsConfig.OdysseyActive
                && map.waterBodyTracker.TryGetWaterBodyAt(position, out waterBody)
                && waterBody != null;
            bool hasFish = onWaterBody && waterBody.HasFish;

            if (!hasPlants && !hasDeepOre && !hasFish)
            {
                if (onWaterBody && scannerActive)
                    return "RimWorldAccess.Map.Tile.Plants.NonePlusFishMinerals".Translate();
                if (onWaterBody)
                    return "RimWorldAccess.Map.Tile.Plants.NonePlusFish".Translate();
                if (scannerActive)
                    return "RimWorldAccess.Map.Tile.Plants.NoneNoMinerals".Translate();
                return "RimWorldAccess.Map.Tile.Plants.None".Translate();
            }

            var builder = new AnnouncementBuilder().DefaultSep(Separator.Comma);

            if (hasPlants)
            {
                foreach (var plant in plants)
                {
                    float growthPercent = plant.Growth * 100f;
                    builder.Add("RimWorldAccess.Map.Tile.Plants.LabelWithGrowth".Translate(
                        plant.LabelShortCap, growthPercent.ToString("F0")));

                    builder.Add(plant.HarvestableNow
                        ? "RimWorldAccess.Map.Tile.Plants.Harvestable".Translate()
                        : "RimWorldAccess.Map.Tile.Plants.NotHarvestable".Translate());

                    if (plant.Dying)
                        builder.Add("RimWorldAccess.Map.Tile.Plants.Dying".Translate());
                }
            }

            if (hasFish)
            {
                var allFish = waterBody.CommonFishIncludingExtras.Concat(waterBody.UncommonFish);
                string fishList = allFish.Select(f => f.label).ToCommaList().CapitalizeFirst();

                int population = Mathf.RoundToInt(waterBody.Population);
                int maxPopulation = Mathf.RoundToInt(waterBody.MaxPopulation);

                builder.Add("RimWorldAccess.Map.Tile.Plants.FishHeader".Translate(fishList, population, maxPopulation),
                    Separator.Period);

                var gillRot = map.gameConditionManager.GetActiveCondition<GameCondition_GillRot>();
                if (gillRot != null && !gillRot.HiddenByOtherCondition(map))
                    builder.Add(gillRot.LabelCap);
            }

            if (hasDeepOre)
                builder.Add("RimWorldAccess.Map.Tile.Plants.DeepHeader".Translate(deepOreInfo), Separator.Period);

            return builder.Build();
        }

        /// <summary>
        /// Light level, temperature, vacuum, and indoor/outdoor status at a tile, key 4.
        /// </summary>
        public static string GetLightInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return "RimWorldAccess.Map.Tile.OutOfBounds".Translate();

            var builder = new AnnouncementBuilder().DefaultSep(Separator.Comma);

            float glowValue = map.glowGrid.GroundGlowAt(position);
            PsychGlow lightLevel = map.glowGrid.PsychGlowAt(position);
            builder.Add("RimWorldAccess.Map.Tile.Light.LightLine".Translate(
                glowValue.ToStringPercent(), lightLevel.GetLabel()));

            float temperature = position.GetTemperature(map);
            builder.Add("RimWorldAccess.Map.Tile.Light.Temperature".Translate(
                MenuHelper.FormatTemperature(temperature, "F1")));

            float vacuum = position.GetVacuum(map);
            if (vacuum > 0f)
                builder.Add("RimWorldAccess.Map.Tile.Light.Vacuum".Translate(
                    vacuum.ToStringPercent("0")));

            RoofDef roof = position.GetRoof(map);
            builder.Add(roof != null
                ? "RimWorldAccess.Map.Tile.Light.Indoors".Translate()
                : "RimWorldAccess.Map.Tile.Light.Outdoors".Translate());

            List<Thing> things = position.GetThingList(map);
            foreach (var building in things.OfType<Building>())
            {
                string tempControlInfo = GetTemperatureControlInfo(building);
                if (!string.IsNullOrEmpty(tempControlInfo))
                    builder.Add("RimWorldAccess.Map.Tile.Light.TempControl".Translate(
                        building.LabelShortCap, tempControlInfo), Separator.Period);
            }

            return builder.Build();
        }

        /// <summary>
        /// Power status of any buildings at a tile connected to a power network, key 6.
        /// </summary>
        public static string GetPowerInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return "RimWorldAccess.Map.Tile.OutOfBounds".Translate();

            List<Thing> things = position.GetThingList(map);
            var buildings = things.OfType<Building>().ToList();

            if (buildings.Count == 0)
                return "RimWorldAccess.Map.Tile.Power.NoBuildings".Translate();

            var builder = new AnnouncementBuilder().DefaultSep(Separator.Period);
            int buildingsWithPower = 0;

            foreach (var building in buildings)
            {
                string powerInfo = PowerInfoHelper.GetPowerInfo(building);
                if (!string.IsNullOrEmpty(powerInfo))
                {
                    builder.Add("RimWorldAccess.Map.Tile.Power.Line".Translate(building.LabelShortCap, powerInfo));
                    buildingsWithPower++;
                }
            }

            if (buildingsWithPower == 0)
                return "RimWorldAccess.Map.Tile.Power.NoneConnected".Translate();

            return builder.Build();
        }

        /// <summary>
        /// Room name and stats with quality tiers for the room at a tile, key 5.
        /// </summary>
        public static string GetRoomStatsInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return "RimWorldAccess.Map.Tile.OutOfBounds".Translate();

            Room room = position.GetRoom(map);

            if (room == null)
                return "RimWorldAccess.Map.Tile.Room.None".Translate();

            RoofDef roof = position.GetRoof(map);
            if (roof == null)
                return "RimWorldAccess.Map.Tile.Room.Outdoors".Translate();

            if (!room.ProperRoom)
                return "RimWorldAccess.Map.Tile.Room.NotProper".Translate();

            return GetRoomStatsInfo(room);
        }

        /// <summary>
        /// Room name and non-hidden stats with quality tiers, shared by key 5 and gizmo
        /// navigation.
        /// </summary>
        public static string GetRoomStatsInfo(Room room)
        {
            if (room == null)
                return "RimWorldAccess.Map.Tile.Room.None".Translate();

            var builder = new AnnouncementBuilder().DefaultSep(Separator.Comma);

            string roomLabel = room.GetRoomRoleLabel();
            if (!string.IsNullOrEmpty(roomLabel))
                builder.Add(roomLabel.CapitalizeFirst());
            else if (room.Role != null)
                builder.Add(room.Role.LabelCap);
            else
                builder.Add("RimWorldAccess.Map.Tile.Room.Fallback".Translate());

            // Stats ordered by volatility: dynamic first, static last
            var statOrder = new[] { "Cleanliness", "Wealth", "Impressiveness", "Beauty", "Space" };
            // Vanilla's own gate: hidden stats surface only under the debug toggle.
            var visibleStats = DefDatabase<RoomStatDef>.AllDefsListForReading
                .Where(def => !def.isHidden || DebugViewSettings.showAllRoomStats).ToList();

            void AppendStat(RoomStatDef statDef)
            {
                float value = room.GetStat(statDef);
                RoomStatScoreStage stage = statDef.GetScoreStage(value);
                string stageLabel = stage?.label?.CapitalizeFirst() ?? "";
                // Vanilla marks role-relevant stats with a leading "*" and a footnote; the
                // same information reads better as a trailing phrase than as "star, star".
                bool isRelated = room.Role != null && room.Role.IsStatRelated(statDef);
                string statLine = string.IsNullOrEmpty(stageLabel)
                    ? "RimWorldAccess.Map.Tile.Room.Stat".Translate(string.Empty, statDef.LabelCap, statDef.ScoreToString(value))
                    : "RimWorldAccess.Map.Tile.Room.StatWithStage".Translate(string.Empty, statDef.LabelCap, stageLabel, statDef.ScoreToString(value));
                if (isRelated)
                    statLine += " (" + (string)"StatRelatesToCurrentRoom".Translate() + ")";
                builder.Add(statLine);
            }

            foreach (var statName in statOrder)
            {
                var statDef = visibleStats.FirstOrDefault(s => s.defName == statName);
                if (statDef != null) AppendStat(statDef);
            }

            foreach (RoomStatDef statDef in visibleStats)
            {
                if (!statOrder.Contains(statDef.defName))
                    AppendStat(statDef);
            }

            // Royalty: if this room serves a titled noble (a throne assigned to them, or a bed
            // they own), append the met/missing breakdown of their title's room requirements —
            // the throne checklist a sighted player sees but a screen reader otherwise can't.
            string titleRequirements = RoomRequirementsHelper.GetTitleRequirementsInfo(room);
            if (!string.IsNullOrEmpty(titleRequirements))
                builder.Add(titleRequirements, Separator.Period);

            return builder.Build();
        }

        /// <summary>
        /// Cooling/heating direction and target temperature for a temperature-control
        /// building, or null when it has none.
        /// </summary>
        private static string GetTemperatureControlInfo(Building building)
        {
            if (building == null)
                return null;

            CompTempControl tempControl = building.TryGetComp<CompTempControl>();
            if (tempControl == null)
                return null;

            Building_TempControl tempControlBuilding = building as Building_TempControl;
            if (tempControlBuilding == null)
                return null;

            string directionInfo = "";
            if (building is Building_Cooler)
            {
                // Coolers cool to their south (blue) side and heat to their north (red) one.
                Rot4 rotation = building.Rotation;

                IntVec3 coolingSide = IntVec3.South.RotatedBy(rotation);
                string coolingDir = GetCardinalDirection(coolingSide);

                IntVec3 heatingSide = IntVec3.North.RotatedBy(rotation);
                string heatingDir = GetCardinalDirection(heatingSide);

                directionInfo = "RimWorldAccess.Map.Tile.TempControl.Cooler".Translate(coolingDir, heatingDir);
            }
            else
            {
                directionInfo = "RimWorldAccess.Map.Tile.TempControl.Generic".Translate();
            }

            float targetTemp = tempControl.TargetTemperature;
            string tempString = MenuHelper.FormatTemperature(targetTemp, "F0");

            return "RimWorldAccess.Map.Tile.TempControl.WithTarget".Translate(directionInfo, tempString);
        }

        /// <summary>
        /// Cardinal direction name for an IntVec3 offset.
        /// </summary>
        private static string GetCardinalDirection(IntVec3 direction)
        {
            return BuildingCellHelper.GetCardinalDirection(direction) ?? "RimWorldAccess.Map.Tile.TempControl.UnknownDir".Translate().ToString();
        }

        /// <summary>
        /// Hostile- or trader-status suffix for a pawn, or null when neither applies;
        /// hostility wins.
        /// </summary>
        public static string GetPawnSuffix(Pawn pawn)
        {
            if (pawn.Faction != null && pawn.Faction.HostileTo(Faction.OfPlayer))
            {
                return "RimWorldAccess.Map.Tile.Pawn.HostileSuffix".Translate();
            }

            if (pawn.trader?.traderKind != null)
            {
                return "RimWorldAccess.Map.Tile.Pawn.TraderSuffix".Translate();
            }

            return null;
        }

        /// <summary>
        /// Formats a list of pawns for tile summary, optionally grouping by activity.
        /// </summary>
        private static string FormatPawnsForTileSummary(List<Pawn> pawns, Dictionary<Thing, List<Designation>> thingDesignations = null)
        {
            if (pawns == null || pawns.Count == 0)
                return null;

            bool showActivity = RimWorldAccessMod_Settings.Settings?.ShowPawnActivityOnMap ?? true;

            if (!showActivity)
            {
                return FormatPawnsSimple(pawns, thingDesignations);
            }

            return FormatPawnsWithActivityGrouping(pawns, thingDesignations);
        }

        /// <summary>
        /// Pawn list without activity grouping.
        /// </summary>
        private static string FormatPawnsSimple(List<Pawn> pawns, Dictionary<Thing, List<Designation>> thingDesignations = null)
        {
            bool showCover = RimWorldAccessMod_Settings.Settings?.ShowCoverInfo ?? true;
            return string.Join(", ", pawns.Select(p =>
            {
                string entry = p.LabelShort;

                string designationSuffix = GetThingDesignationSuffix(p, thingDesignations);
                if (!string.IsNullOrEmpty(designationSuffix))
                    entry += "RimWorldAccess.Map.Tile.Pawn.DesignationSuffix".Translate(designationSuffix);

                string suffix = GetPawnSuffix(p);
                if (!string.IsNullOrEmpty(suffix))
                    entry += suffix;

                if (showCover)
                {
                    string coverInfo = CoverHelper.GetCoverInfo(p);
                    if (!string.IsNullOrEmpty(coverInfo))
                        entry += "RimWorldAccess.Map.Tile.Pawn.CoverSuffix".Translate(coverInfo);
                }

                return entry;
            }));
        }

        /// <summary>
        /// Pawn list grouping pawns that share an activity, suffix, cover, and
        /// designations: "A and B (sleeping)".
        /// </summary>
        private static string FormatPawnsWithActivityGrouping(List<Pawn> pawns, Dictionary<Thing, List<Designation>> thingDesignations = null)
        {
            bool showCover = RimWorldAccessMod_Settings.Settings?.ShowCoverInfo ?? true;
            var groups = new List<(List<Pawn> pawns, string activity, string suffix, string coverInfo, string designationInfo)>();

            foreach (var pawn in pawns)
            {
                string activity = PawnHelper.GetPawnActivity(pawn);
                string suffix = GetPawnSuffix(pawn);
                string coverInfo = showCover ? CoverHelper.GetCoverInfo(pawn) : null;
                string designationInfo = GetThingDesignationSuffix(pawn, thingDesignations);

                var existingGroup = groups.FirstOrDefault(g => g.activity == activity && g.suffix == suffix && g.coverInfo == coverInfo && g.designationInfo == designationInfo);
                if (existingGroup.pawns != null)
                {
                    existingGroup.pawns.Add(pawn);
                }
                else
                {
                    groups.Add((new List<Pawn> { pawn }, activity, suffix, coverInfo, designationInfo));
                }
            }

            return string.Join(", ", groups.Select(group =>
            {
                string entry = FormatPawnNames(group.pawns);

                if (!string.IsNullOrEmpty(group.designationInfo))
                    entry += "RimWorldAccess.Map.Tile.Pawn.DesignationSuffix".Translate(group.designationInfo);

                if (!string.IsNullOrEmpty(group.suffix))
                    entry += group.suffix;

                if (!string.IsNullOrEmpty(group.coverInfo))
                    entry += "RimWorldAccess.Map.Tile.Pawn.CoverSuffix".Translate(group.coverInfo);

                if (!string.IsNullOrEmpty(group.activity))
                    entry += "RimWorldAccess.Map.Tile.Pawn.ActivitySuffix".Translate(group.activity);

                return entry;
            }));
        }

        /// <summary>
        /// Joins pawn names with list grammar: "A", "A and B", "A, B, and C".
        /// </summary>
        private static string FormatPawnNames(List<Pawn> pawns)
        {
            if (pawns.Count == 1)
                return pawns[0].LabelShort;

            if (pawns.Count == 2)
                return "RimWorldAccess.Map.Tile.Pawn.AndJoin".Translate(pawns[0].LabelShort, pawns[1].LabelShort);

            var names = pawns.Select(p => p.LabelShort).ToList();
            return "RimWorldAccess.Map.Tile.Pawn.OxfordJoin".Translate(string.Join(", ", names.Take(names.Count - 1)), names.Last());
        }

        /// <summary>
        /// Allowed and special areas containing a tile, behind key 7.
        /// </summary>
        public static string GetAreasInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return "RimWorldAccess.Map.Tile.OutOfBounds".Translate();

            var areaNames = map.areaManager.AllAreas
                .Where(a => a[position])
                .Select(a => a.Label)
                .ToList();

            if (areaNames.Count == 0)
                return "RimWorldAccess.Map.Tile.Areas.None".Translate();

            var builder = new AnnouncementBuilder().DefaultSep(Separator.Comma);
            foreach (var name in areaNames)
                builder.Add(name);

            return builder.Build();
        }

        /// <summary>
        /// Parenthesized location context for a position — "(in Stockpile zone 1)" — or
        /// null when neither a zone, a named storage group, nor a roled room applies.
        /// </summary>
        public static string GetLocationContext(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return null;

            // Zones and ISlotGroupParent things (shelves) cannot overlap, so a zone answer
            // is final.
            Zone zone = position.GetZone(map);
            if (zone != null)
            {
                return "RimWorldAccess.Map.Tile.Location.InZone".Translate(zone.label);
            }

            List<Thing> things = position.GetThingList(map);
            foreach (var thing in things)
            {
                if (thing is IStorageGroupMember storage && storage.Group != null)
                {
                    string groupName = storage.Group.RenamableLabel;
                    if (!string.IsNullOrEmpty(groupName))
                    {
                        return "RimWorldAccess.Map.Tile.Location.AtStorage".Translate(groupName);
                    }
                }
            }

            Room room = position.GetRoom(map);
            if (room != null && room.ProperRoom && !room.PsychologicallyOutdoors)
            {
                string roomLabel = room.GetRoomRoleLabel();
                if (!string.IsNullOrEmpty(roomLabel))
                {
                    return "RimWorldAccess.Map.Tile.Location.InRoom".Translate(roomLabel);
                }
            }

            return null;
        }

        /// <summary>
        /// Plain form of GetLocationContext (no surrounding parentheses) for use in
        /// direct pawn announcements like "Bob, in kitchen, cooking meals". Returns
        /// null if the pawn is outdoors or in a room with no meaningful role.
        /// </summary>
        public static string GetLocationContextPlain(IntVec3 position, Map map)
        {
            string ctx = GetLocationContext(position, map);
            if (string.IsNullOrEmpty(ctx))
                return null;
            if (ctx.Length >= 2 && ctx[0] == '(' && ctx[ctx.Length - 1] == ')')
                return ctx.Substring(1, ctx.Length - 2);
            return ctx;
        }

        /// <summary>
        /// Comma-separated designation labels targeting a thing ("chop", "hunt"), or null.
        /// </summary>
        private static string GetThingDesignationSuffix(Thing thing, Dictionary<Thing, List<Designation>> thingDesignations)
        {
            if (thingDesignations == null || !thingDesignations.TryGetValue(thing, out var designations) || designations.Count == 0)
                return null;

            return string.Join(", ", designations.Select(d => GetDesignationLabel(d)));
        }

        /// <summary>
        /// Comma-separated list of the designations active at a tile, or null.
        /// </summary>
        public static string GetDesignationsInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return null;

            var designations = map.designationManager.AllDesignationsAt(position);
            if (designations == null || designations.Count == 0)
                return null;

            return string.Join(", ", designations.Select(d => GetDesignationLabel(d)));
        }

        /// <summary>
        /// Player-facing label for a designation, from the game's own strings.
        /// </summary>
        private static string GetDesignationLabel(Designation designation)
        {
            if (designation == null || designation.def == null)
                return "RimWorldAccess.Map.Tile.UnknownOrder".Translate();

            string label = GetLocalizedDesignationLabel(designation.def);

            return label;
        }

        /// <summary>
        /// Gets the localized label for a DesignationDef by finding its Designator.
        /// </summary>
        private static string GetLocalizedDesignationLabel(DesignationDef def)
        {
            if (def == null)
                return "RimWorldAccess.Map.Label.Unknown".Translate();

            var designators = Find.ReverseDesignatorDatabase?.AllDesignators;
            if (designators != null)
            {
                foreach (var designator in designators)
                {
                    if (GetDesignationDef(designator) == def)
                        return designator.Label;
                }
            }

            string label = def.LabelCap;
            if (string.IsNullOrEmpty(label))
            {
                label = GenText.SplitCamelCase(def.defName);
            }
            return label;
        }

        /// <summary>
        /// Designator.Designation is protected; read it off the designator's own runtime
        /// type so mod subclasses resolve too.
        /// </summary>
        internal static DesignationDef GetDesignationDef(Designator designator)
        {
            var designationProp = designator.GetType().GetProperty("Designation",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            return designationProp == null ? null : designationProp.GetValue(designator) as DesignationDef;
        }

        /// <summary>
        /// Progress of a building's active process ("fermenting, 45%"), or null.
        /// </summary>
        private static string GetBuildingProgressInfo(Building building)
        {
            if (building is Building_FermentingBarrel barrel)
            {
                if (barrel.Fermented)
                    return "RimWorldAccess.Map.Tile.Progress.Fermented".Translate();
                if (barrel.Progress > 0f)
                    return "RimWorldAccess.Map.Tile.Progress.Fermenting".Translate(barrel.Progress.ToStringPercent());
            }

            if (building is Building_GeneAssembler assembler && assembler.Working)
            {
                return "RimWorldAccess.Map.Tile.Progress.Assembling".Translate(assembler.ProgressPercent.ToStringPercent());
            }

            return null;
        }

        /// <summary>
        /// For a transport pod, whether it is connected to fuel; for a pod launcher, its
        /// fuel level and fueling-port location. Null for anything else.
        /// </summary>
        private static string GetTransportPodInfo(Building building, Map map)
        {
            if (building == null || map == null)
                return null;

            CompTransporter transporter = building.TryGetComp<CompTransporter>();
            if (transporter != null)
            {
                CompLaunchable launchable = building.TryGetComp<CompLaunchable>();
                if (launchable != null)
                {
                    var connectedProp = HarmonyLib.AccessTools.Property(launchable.GetType(), "ConnectedToFuelingPort");
                    if (connectedProp != null)
                    {
                        try
                        {
                            bool connected = (bool)connectedProp.GetValue(launchable);
                            if (connected)
                            {
                                float fuel = TransportPodHelper.GetFuelLevel(launchable);
                                return "RimWorldAccess.Map.Tile.TransportPod.Fueled".Translate(fuel.ToString("F0"));
                            }
                            else
                            {
                                return "RimWorldAccess.Map.Tile.TransportPod.NotConnected".Translate();
                            }
                        }
                        catch { }
                    }
                }

                // Fall back to looking for an adjacent fueling port.
                bool hasAdjacentFuel = false;
                foreach (IntVec3 adjacent in GenAdj.CellsAdjacent8Way(building))
                {
                    if (adjacent.InBounds(map))
                    {
                        Building adjacentBuilding = adjacent.GetFirstBuilding(map);
                        if (adjacentBuilding != null)
                        {
                            CompRefuelable refuelable = adjacentBuilding.TryGetComp<CompRefuelable>();
                            if (refuelable != null && adjacentBuilding.def.building != null && adjacentBuilding.def.building.hasFuelingPort)
                            {
                                hasAdjacentFuel = true;
                                break;
                            }
                        }
                    }
                }

                return (hasAdjacentFuel
                    ? "RimWorldAccess.Map.Tile.TransportPod.AdjacentToLauncher"
                    : "RimWorldAccess.Map.Tile.TransportPod.NotConnected").Translate();
            }

            CompRefuelable refuelableComp = building.TryGetComp<CompRefuelable>();
            if (refuelableComp != null && building.def.building != null && building.def.building.hasFuelingPort)
            {
                IntVec3 fuelingPortCell = FuelingPortUtility.GetFuelingPortCell(building);
                if (fuelingPortCell.IsValid && fuelingPortCell.InBounds(map))
                {
                    float fuel = refuelableComp.Fuel;
                    return "RimWorldAccess.Map.Tile.TransportPod.LauncherWithFuelPort".Translate(fuel.ToString("F0"), fuelingPortCell.x, fuelingPortCell.z);
                }
            }

            return null;
        }

        /// <summary>
        /// Gets a relative direction description from one position to another.
        /// </summary>
        private static string GetRelativeDirection(IntVec3 from, IntVec3 to)
        {
            int dx = to.x - from.x;
            int dz = to.z - from.z;

            if (System.Math.Abs(dx) > System.Math.Abs(dz))
            {
                return (dx > 0
                    ? "RimWorldAccess.Map.Direction.Lower.East"
                    : "RimWorldAccess.Map.Direction.Lower.West").Translate();
            }
            else if (System.Math.Abs(dz) > System.Math.Abs(dx))
            {
                return (dz > 0
                    ? "RimWorldAccess.Map.Direction.Lower.North"
                    : "RimWorldAccess.Map.Direction.Lower.South").Translate();
            }
            else if (dx != 0 && dz != 0)
            {
                string ns = (dz > 0
                    ? "RimWorldAccess.Map.Direction.Lower.North"
                    : "RimWorldAccess.Map.Direction.Lower.South").Translate();
                string ew = (dx > 0
                    ? "RimWorldAccess.Map.Direction.Lower.East"
                    : "RimWorldAccess.Map.Direction.Lower.West").Translate();
                return "RimWorldAccess.Map.Tile.RelDir.Diagonal".Translate(ns, ew);
            }

            return "RimWorldAccess.Map.Tile.RelDir.Adjacent".Translate();
        }

        /// <summary>
        /// Announcement text when a position is some launcher's fueling-port cell — the
        /// empty cell pods belong on — otherwise null.
        /// </summary>
        private static string GetEmptyFuelingPortInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return null;

            Building fuelingPortGiver = FuelingPortUtility.FuelingPortGiverAtFuelingPortCell(position, map);
            if (fuelingPortGiver != null)
            {
                string launcherName = fuelingPortGiver.LabelShort ?? "RimWorldAccess.Map.Tile.TransportPod.LauncherFallback".Translate().ToString();

                CompRefuelable refuelable = fuelingPortGiver.TryGetComp<CompRefuelable>();
                if (refuelable != null)
                {
                    return "RimWorldAccess.Map.Tile.TransportPod.FuelingPortWithLevel".Translate(launcherName, refuelable.Fuel.ToString("F0"));
                }

                return "RimWorldAccess.Map.Tile.TransportPod.FuelingPort".Translate(launcherName);
            }

            return null;
        }

        /// <summary>
        /// Whether the game is in drop-pod landing targeting, identified by the targeter's
        /// mouse-attachment texture.
        /// </summary>
        private static bool IsDropPodLandingTargeting()
        {
            if (Find.Targeter == null || !Find.Targeter.IsTargeting)
                return false;

            var mouseAttachmentField = HarmonyLib.AccessTools.Field(typeof(Targeter), "mouseAttachment");
            if (mouseAttachmentField == null)
                return false;

            var mouseAttachment = mouseAttachmentField.GetValue(Find.Targeter) as UnityEngine.Texture2D;
            return mouseAttachment == CompLaunchable.TargeterMouseAttachment;
        }

        /// <summary>
        /// Deep ore at a tile ("gold, 300 remaining"), or null. Gated on a powered scanner
        /// existing, matching what a sighted player can see.
        /// </summary>
        public static string GetDeepOreInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return null;

            if (!map.deepResourceGrid.AnyActiveDeepScannersOnMap())
                return null;

            ThingDef oreDef = map.deepResourceGrid.ThingDefAt(position);
            if (oreDef == null)
                return null;

            int count = map.deepResourceGrid.CountAt(position);
            if (count <= 0)
                return null;

            return "RimWorldAccess.Map.Tile.Plants.DeepOreInfo".Translate(oreDef.label, count);
        }

        /// <summary>
        /// Whether the architect designator now in placement mode reveals deep resources,
        /// as a deep drill does.
        /// </summary>
        public static bool ShouldShowDeepOreForCurrentDesignator()
        {
            if (!ArchitectState.IsInPlacementMode)
                return false;

            Designator designator = ArchitectState.SelectedDesignator;
            if (designator == null)
                return false;

            if (!(designator is Designator_Build buildDesignator))
                return false;

            BuildableDef placingDef = buildDesignator.PlacingDef;
            if (placingDef == null)
                return false;

            // Mirrors DeepResourceGrid.DrawPlacingMouseAttachments.
            if (placingDef is ThingDef thingDef && thingDef.CompDefFor<CompDeepDrill>() != null)
            {
                return true;
            }

            return false;
        }
    }
}
