using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper class to query and format information about tiles on the map.
    /// Provides both summarized and detailed information for screen reader accessibility.
    /// </summary>
    public static class TileInfoHelper
    {
        /// <summary>
        /// Gets a concise summary of what's on a tile.
        /// Format: "[item1, item2, ... last item], indoors/outdoors, {lighting level}, at X, Z"
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
                    if (!pawn.IsHiddenFromPlayer())
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
                if (terrain.defName.EndsWith("_Smooth"))
                    terrainLabel += "RimWorldAccess.Map.Tile.FloorSuffix".Translate();
                ColorDef floorPaint = map.terrainGrid.ColorAt(position);
                if (floorPaint != null && !floorPaint.label.NullOrEmpty())
                    terrainLabel += "RimWorldAccess.Map.Tile.PaintSuffix".Translate(floorPaint.LabelCap);
                builder.Add(terrainLabel);
            }

            // Announce the roof's own label (e.g. "Constructed roof", "Rock roof (thin)",
            // "Overhead mountain") so users can distinguish thin, thick, and mountain roofs
            // during navigation rather than a generic "roofed"/"underground".
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

            // Plan markers are a sibling overlay of zones (map.planManager), not a TerrainDef or a
            // Thing, so announce the plan's name and color when the cursor sits on one of its cells.
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
                if (building.def.defName.StartsWith("Smoothed") && building.def.building != null && !building.def.building.isNaturalRock)
                    label += "RimWorldAccess.Map.Tile.WallSuffix".Translate();
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

        /// <summary>
        /// Gets information about items and pawns at a tile (key 1).
        /// Lists all items with stack counts and all pawns with their labels.
        /// </summary>
        public static string GetItemsAndPawnsInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return "RimWorldAccess.Map.Tile.OutOfBounds".Translate();

            List<Thing> things = position.GetThingList(map);
            var pawns = things.OfType<Pawn>().ToList();
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

            // When a drafted shooter is selected, follow the tile contents with the
            // same ranged hit-chance breakdown a sighted player sees on mouse-over.
            // Each report names its own target, so it stays clear with several pawns
            // on the tile. Returns null unless the game's gating applies (drafted,
            // ranged weapon, target is not the shooter), leaving other readouts
            // untouched. Placed last so it never runs into the item list.
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

        /// <summary>
        /// Gets information about flooring at a tile (key 2).
        /// Shows terrain type, smoothness, beauty, and cleanliness.
        /// </summary>
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

            if (terrain.defName.EndsWith("_Smooth"))
                builder.Add("RimWorldAccess.Map.Tile.Flooring.Smooth".Translate());
            else if (terrain.defName.EndsWith("_Rough"))
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
        /// Gets information about resources at a tile (key 3): plants, fish, and deep
        /// mineral deposits. Plants show species, growth, and harvestable status. Fish
        /// (Odyssey) show species and current/max population, matching vanilla's mouseover.
        /// Deep ore is shown only when a powered ground-penetrating scanner is active.
        /// When nothing is present, the empty message lists only the resource types that
        /// are relevant to this tile's context (water adds fish; an active scanner adds
        /// mineral deposits).
        /// </summary>
        public static string GetPlantsInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return "RimWorldAccess.Map.Tile.OutOfBounds".Translate();

            // Plants present at this cell
            List<Thing> things = position.GetThingList(map);
            var plants = things.OfType<Plant>().ToList();
            bool hasPlants = plants.Count > 0;

            // Deep ore, only when a powered ground-penetrating scanner is active
            bool scannerActive = map.deepResourceGrid.AnyActiveDeepScannersOnMap();
            string deepOreInfo = scannerActive ? GetDeepOreInfo(position, map) : null;
            bool hasDeepOre = !string.IsNullOrEmpty(deepOreInfo);

            // Fish, only with Odyssey active and on a tracked water body. Reporting matches
            // vanilla's MouseoverReadout (species list + current/max population, plus GillRot).
            WaterBody waterBody = null;
            bool onWaterBody = ModsConfig.OdysseyActive
                && map.waterBodyTracker.TryGetWaterBodyAt(position, out waterBody)
                && waterBody != null;
            bool hasFish = onWaterBody && waterBody.HasFish;

            // Nothing present: announce only the resource types relevant to this tile.
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

            // Plants
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

            // Fish (species, current/max population, and GillRot if active)
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

            // Deep mineral deposits
            if (hasDeepOre)
                builder.Add("RimWorldAccess.Map.Tile.Plants.DeepHeader".Translate(deepOreInfo), Separator.Period);

            return builder.Build();
        }

        /// <summary>
        /// Gets information about brightness and temperature at a tile (key 4).
        /// Shows light level (simplified), temperature, and indoor/outdoor status.
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
        /// Gets power information for objects at a tile (key 6).
        /// Shows power status for any buildings connected to a power network.
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
        /// Gets information about room stats at a tile (key 5).
        /// Shows room name and all stats with quality tier descriptions.
        /// </summary>
        public static string GetRoomStatsInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return "RimWorldAccess.Map.Tile.OutOfBounds".Translate();

            string penInfo = PenInfoHelper.GetPenStatsInfo(position, map);
            if (!string.IsNullOrEmpty(penInfo))
                return penInfo;

            Room room = position.GetRoom(map);

            if (room == null)
                return "RimWorldAccess.Map.Tile.Room.None".Translate();

            // Check if outdoor (no roof) or not a proper room
            RoofDef roof = position.GetRoof(map);
            if (roof == null)
                return "RimWorldAccess.Map.Tile.Room.Outdoors".Translate();

            if (!room.ProperRoom)
                return "RimWorldAccess.Map.Tile.Room.NotProper".Translate();

            return GetRoomStatsInfo(room);
        }

        /// <summary>
        /// Gets information about room stats for a given room.
        /// Shows room name and all non-hidden stats with quality tier descriptions.
        /// Used by both the 5 key and the gizmo navigation.
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
            var visibleStats = DefDatabase<RoomStatDef>.AllDefsListForReading.Where(def => !def.isHidden).ToList();

            void AppendStat(RoomStatDef statDef)
            {
                float value = room.GetStat(statDef);
                RoomStatScoreStage stage = statDef.GetScoreStage(value);
                string stageLabel = stage?.label?.CapitalizeFirst() ?? "";
                // Vanilla draws a "*" before stats relevant to the room's role
                // (with a "* StatRelatesToCurrentRoom" footnote). For screen reader users
                // we surface the same information with the translated phrase as a suffix
                // on relevant stats — no leading asterisks for the screen reader to read
                // out as "star, star, star".
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

            return builder.Build();
        }

        /// <summary>
        /// Gets temperature control information for coolers and heaters.
        /// Returns direction (cooling/heating) and target temperature.
        /// </summary>
        private static string GetTemperatureControlInfo(Building building)
        {
            if (building == null)
                return null;

            // Check if this building has temperature control
            CompTempControl tempControl = building.TryGetComp<CompTempControl>();
            if (tempControl == null)
                return null;

            // Determine if this is a cooler or heater based on building type
            Building_TempControl tempControlBuilding = building as Building_TempControl;
            if (tempControlBuilding == null)
                return null;

            // For coolers specifically, we need to determine the cooling/heating direction
            string directionInfo = "";
            if (building.GetType().Name == "Building_Cooler")
            {
                // Coolers cool to the south (blue side) and heat to the north (red side)
                // IntVec3.South.RotatedBy(Rotation) gives the cooling direction
                // IntVec3.North.RotatedBy(Rotation) gives the heating direction
                Rot4 rotation = building.Rotation;

                // Get the actual cardinal direction for the blue (cooling) side
                IntVec3 coolingSide = IntVec3.South.RotatedBy(rotation);
                string coolingDir = GetCardinalDirection(coolingSide);

                // Get the actual cardinal direction for the red (heating) side
                IntVec3 heatingSide = IntVec3.North.RotatedBy(rotation);
                string heatingDir = GetCardinalDirection(heatingSide);

                directionInfo = "RimWorldAccess.Map.Tile.TempControl.Cooler".Translate(coolingDir, heatingDir);
            }
            else
            {
                // For other temperature control devices (heaters, vents, etc.)
                directionInfo = "RimWorldAccess.Map.Tile.TempControl.Generic".Translate();
            }

            // Add target temperature
            float targetTemp = tempControl.TargetTemperature;
            string tempString = MenuHelper.FormatTemperature(targetTemp, "F0");

            return "RimWorldAccess.Map.Tile.TempControl.WithTarget".Translate(directionInfo, tempString);
        }

        /// <summary>
        /// Converts an IntVec3 direction to a cardinal direction string.
        /// Delegates to BuildingCellHelper for shared implementation.
        /// </summary>
        private static string GetCardinalDirection(IntVec3 direction)
        {
            return BuildingCellHelper.GetCardinalDirection(direction) ?? "RimWorldAccess.Map.Tile.TempControl.UnknownDir".Translate().ToString();
        }

        /// <summary>
        /// Gets a suffix for a pawn based on their status (hostile or trader).
        /// Returns " (hostile)" if the pawn is hostile to the player,
        /// returns " (trader)" if the pawn is a trader,
        /// returns null if neither.
        /// </summary>
        public static string GetPawnSuffix(Pawn pawn)
        {
            // Check if pawn is hostile to player (takes priority over trader status)
            if (pawn.Faction != null && pawn.Faction.HostileTo(Faction.OfPlayer))
            {
                return "RimWorldAccess.Map.Tile.Pawn.HostileSuffix".Translate();
            }

            // Check if pawn is a trader
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
        /// Simple pawn formatting without activity.
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
        /// Formats pawns with activity grouping.
        /// Pawns doing the same activity are grouped: "A and B (sleeping)"
        /// </summary>
        private static string FormatPawnsWithActivityGrouping(List<Pawn> pawns, Dictionary<Thing, List<Designation>> thingDesignations = null)
        {
            // Group pawns by activity, suffix, cover info, and designations
            bool showCover = RimWorldAccessMod_Settings.Settings?.ShowCoverInfo ?? true;
            var groups = new List<(List<Pawn> pawns, string activity, string suffix, string coverInfo, string designationInfo)>();

            foreach (var pawn in pawns)
            {
                string activity = PawnHelper.GetPawnActivity(pawn);
                string suffix = GetPawnSuffix(pawn);
                string coverInfo = showCover ? CoverHelper.GetCoverInfo(pawn) : null;
                string designationInfo = GetThingDesignationSuffix(pawn, thingDesignations);

                // Find existing group with same activity, suffix, cover info, and designations
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
        /// Formats a list of pawn names with proper grammar.
        /// 1 pawn: "Name"
        /// 2 pawns: "Name1 and Name2"
        /// 3+ pawns: "Name1, Name2, and Name3"
        /// </summary>
        private static string FormatPawnNames(List<Pawn> pawns)
        {
            if (pawns.Count == 1)
                return pawns[0].LabelShort;

            if (pawns.Count == 2)
                return "RimWorldAccess.Map.Tile.Pawn.AndJoin".Translate(pawns[0].LabelShort, pawns[1].LabelShort);

            // 3+: "A, B, and C"
            var names = pawns.Select(p => p.LabelShort).ToList();
            return "RimWorldAccess.Map.Tile.Pawn.OxfordJoin".Translate(string.Join(", ", names.Take(names.Count - 1)), names.Last());
        }

        /// <summary>
        /// Gets information about areas at a tile (key 7).
        /// Shows which allowed areas and special areas (home area) the tile is part of.
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
        /// Gets location context for a position (zone/named storage, or room).
        /// Used by scanner announcements for mobile entities (pawns, animals).
        /// </summary>
        /// <param name="position">The position to check</param>
        /// <param name="map">The map to check on</param>
        /// <returns>Location context string like "(in Stockpile zone 1)" or null if no meaningful location</returns>
        public static string GetLocationContext(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return null;

            // Priority 1: Check for zone OR named storage (mutually exclusive - can't have both)
            // RimWorld enforces that ISlotGroupParent things (shelves) and zones cannot overlap
            Zone zone = position.GetZone(map);
            if (zone != null)
            {
                return "RimWorldAccess.Map.Tile.Location.InZone".Translate(zone.label);
            }

            // Check for named storage group (shelves, etc.) - only if no zone (mutually exclusive)
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

            // Priority 2: Check for indoor room with a meaningful role
            Room room = position.GetRoom(map);
            if (room != null && room.ProperRoom && !room.PsychologicallyOutdoors)
            {
                string roomLabel = room.GetRoomRoleLabel();
                if (!string.IsNullOrEmpty(roomLabel))
                {
                    return "RimWorldAccess.Map.Tile.Location.InRoom".Translate(roomLabel);
                }
            }

            // No meaningful location context
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
        /// Gets designation labels for a specific thing (e.g., "chop" for a tree, "hunt" for an animal).
        /// Returns comma-separated labels or null if no designations target this thing.
        /// </summary>
        private static string GetThingDesignationSuffix(Thing thing, Dictionary<Thing, List<Designation>> thingDesignations)
        {
            if (thingDesignations == null || !thingDesignations.TryGetValue(thing, out var designations) || designations.Count == 0)
                return null;

            return string.Join(", ", designations.Select(d => GetDesignationLabel(d)));
        }

        /// <summary>
        /// Gets information about designations/orders at a tile.
        /// Returns a comma-separated list of active designations.
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
        /// Gets a human-readable label for a designation using game strings.
        /// </summary>
        private static string GetDesignationLabel(Designation designation)
        {
            if (designation == null || designation.def == null)
                return "RimWorldAccess.Map.Tile.UnknownOrder".Translate();

            // Get localized label from the Designator that uses this DesignationDef
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

            // Try to find the Designator that uses this DesignationDef
            var designators = Find.ReverseDesignatorDatabase?.AllDesignators;
            if (designators != null)
            {
                foreach (var designator in designators)
                {
                    // Use reflection to get the protected Designation property
                    var designationProp = designator.GetType().GetProperty("Designation",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public);

                    if (designationProp != null)
                    {
                        var designatorDef = designationProp.GetValue(designator) as DesignationDef;
                        if (designatorDef == def)
                        {
                            return designator.Label;
                        }
                    }
                }
            }

            // Fallback: use LabelCap if available, otherwise format defName
            string label = def.LabelCap;
            if (string.IsNullOrEmpty(label))
            {
                label = GenText.SplitCamelCase(def.defName);
            }
            return label;
        }

        /// <summary>
        /// Gets work/process progress information for buildings with active processes.
        /// Returns a formatted string like "fermenting, 45%" or null if no progress to report.
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
        /// Gets transport pod related information for a building.
        /// For pod launchers: announces fuel port location
        /// For transport pods: announces if connected to fuel
        /// </summary>
        private static string GetTransportPodInfo(Building building, Map map)
        {
            if (building == null || map == null)
                return null;

            // Check if this is a transport pod (has CompTransporter)
            CompTransporter transporter = building.TryGetComp<CompTransporter>();
            if (transporter != null)
            {
                // Check if it's connected to a fueling port
                CompLaunchable launchable = building.TryGetComp<CompLaunchable>();
                if (launchable != null)
                {
                    // Use reflection to check ConnectedToFuelingPort if available
                    var connectedProp = HarmonyLib.AccessTools.Property(launchable.GetType(), "ConnectedToFuelingPort");
                    if (connectedProp != null)
                    {
                        try
                        {
                            bool connected = (bool)connectedProp.GetValue(launchable);
                            if (connected)
                            {
                                // Get fuel level if connected
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

                // Fallback: check if there's an adjacent fueling port
                bool hasAdjacentFuel = false;
                foreach (IntVec3 adjacent in GenAdj.CellsAdjacent8Way(building))
                {
                    if (adjacent.InBounds(map))
                    {
                        Building adjacentBuilding = adjacent.GetFirstBuilding(map);
                        if (adjacentBuilding != null)
                        {
                            // Check if it's a pod launcher/fueling port
                            CompRefuelable refuelable = adjacentBuilding.TryGetComp<CompRefuelable>();
                            if (refuelable != null && adjacentBuilding.def.defName.Contains("Launcher"))
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

            // Check if this is a pod launcher (has CompRefuelable and is a launcher type)
            CompRefuelable refuelableComp = building.TryGetComp<CompRefuelable>();
            if (refuelableComp != null && building.def.defName.Contains("Launcher"))
            {
                // Find the fueling port cell and announce its exact coordinates
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

            // Determine primary direction
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
                // Diagonal
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
        /// Checks if a position is a fueling port cell for a nearby launcher (empty cell where pods should be placed).
        /// Returns announcement text if this is a fueling port cell, null otherwise.
        /// </summary>
        private static string GetEmptyFuelingPortInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return null;

            // Use FuelingPortUtility to check if this cell is a fueling port for some launcher
            Building fuelingPortGiver = FuelingPortUtility.FuelingPortGiverAtFuelingPortCell(position, map);
            if (fuelingPortGiver != null)
            {
                // This is a fueling port cell - announce it
                string launcherName = fuelingPortGiver.LabelShort ?? "RimWorldAccess.Map.Tile.TransportPod.LauncherFallback".Translate().ToString();

                // Check current fuel level
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
        /// Checks if the game is currently in drop pod landing targeting mode.
        /// Detects this by checking for the specific mouse attachment texture used for drop pods.
        /// </summary>
        private static bool IsDropPodLandingTargeting()
        {
            if (Find.Targeter == null || !Find.Targeter.IsTargeting)
                return false;

            // Use reflection to check the mouseAttachment field
            var mouseAttachmentField = HarmonyLib.AccessTools.Field(typeof(Targeter), "mouseAttachment");
            if (mouseAttachmentField == null)
                return false;

            var mouseAttachment = mouseAttachmentField.GetValue(Find.Targeter) as UnityEngine.Texture2D;
            return mouseAttachment == CompLaunchable.TargeterMouseAttachment;
        }

        /// <summary>
        /// Gets deep ore deposit info for a tile if conditions are met.
        /// Returns info like "gold, 300 remaining" or null if no deep ore or conditions not met.
        /// Matches sighted player visibility - only shows when a powered scanner exists.
        /// </summary>
        /// <param name="position">The tile position to check</param>
        /// <param name="map">The map to check on</param>
        /// <returns>Deep ore info string or null</returns>
        public static string GetDeepOreInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return null;

            // Check if there's an active (powered) deep scanner on the map
            // This matches the visibility rules for sighted players
            if (!map.deepResourceGrid.AnyActiveDeepScannersOnMap())
                return null;

            // Get the deep ore at this position
            ThingDef oreDef = map.deepResourceGrid.ThingDefAt(position);
            if (oreDef == null)
                return null;

            int count = map.deepResourceGrid.CountAt(position);
            if (count <= 0)
                return null;

            return "RimWorldAccess.Map.Tile.Plants.DeepOreInfo".Translate(oreDef.label, count);
        }

        /// <summary>
        /// Checks if the current architect designator should show deep ore info.
        /// Returns true if placing a building with PlaceWorker_ShowDeepResources (like deep drill).
        /// </summary>
        public static bool ShouldShowDeepOreForCurrentDesignator()
        {
            if (!ArchitectState.IsInPlacementMode)
                return false;

            Designator designator = ArchitectState.SelectedDesignator;
            if (designator == null)
                return false;

            // Check if it's a build designator
            if (!(designator is Designator_Build buildDesignator))
                return false;

            // Get the BuildableDef being placed
            BuildableDef placingDef = buildDesignator.PlacingDef;
            if (placingDef == null)
                return false;

            // Check if it's a ThingDef with CompDeepDrill component
            // This matches RimWorld's DeepResourceGrid.DrawPlacingMouseAttachments() logic
            if (placingDef is ThingDef thingDef && thingDef.CompDefFor<CompDeepDrill>() != null)
            {
                return true;
            }

            return false;
        }
    }
}
