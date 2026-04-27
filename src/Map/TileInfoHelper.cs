using System.Collections.Generic;
using System.Linq;
using System.Text;
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


            // Check fog of war
            if (position.Fogged(map))
            {
                // Still announce player-placed designations on fogged tiles
                string fogDesignations = GetDesignationsInfo(position, map);
                if (!string.IsNullOrEmpty(fogDesignations))
                    return "RimWorldAccess.Map.Tile.UnseenWithDesignations".Translate(fogDesignations, position.x, position.z);
                return "RimWorldAccess.Map.Tile.Unseen".Translate(position.x, position.z);
            }
            var sb = new StringBuilder();

            // Check visibility from drafted pawn (if one is selected)
            bool notVisible = false;
            Pawn selectedPawn = Find.Selector?.FirstSelectedObject as Pawn;
            if (selectedPawn != null && selectedPawn.Drafted && selectedPawn.Spawned && selectedPawn.Map == map)
            {
                // Check if pawn can see this position using line of sight
                if (!GenSight.LineOfSight(selectedPawn.Position, position, map))
                {
                    notVisible = true;
                }
            }

            // Collect designations and partition by target type
            var allDesignations = map.designationManager.AllDesignationsAt(position).ToList();
            var thingDesignations = new Dictionary<Thing, List<Designation>>();
            var cellDesignations = new List<Designation>();
            foreach (var designation in allDesignations)
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

            // Get all things sorted by AltitudeLayer descending (game's visual priority)
            var sortedThings = position.GetThingList(map)
                .Where(t => !(t is Mote) && t.def.category != ThingCategory.Mote)
                .OrderByDescending(t => (int)t.def.altitudeLayer)
                .ToList();

            // Separate pawns for grouped formatting; collect remaining things
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

            bool addedSomething = false;

            // 1. Cell-targeted designations (e.g., "mine", "smooth floor")
            foreach (var designation in cellDesignations)
            {
                if (addedSomething) sb.Append(", ");
                sb.Append(GetDesignationLabel(designation));
                addedSomething = true;
            }

            // 2. Pawns (grouped by activity, no truncation)
            if (pawns.Count > 0)
            {
                string pawnsText = FormatPawnsForTileSummary(pawns, thingDesignations);
                if (!string.IsNullOrEmpty(pawnsText))
                {
                    if (addedSomething) sb.Append(", ");
                    sb.Append(pawnsText);
                    addedSomething = true;
                }
            }

            // 3. Non-pawn things in AltitudeLayer order (no truncation)
            foreach (var thing in nonPawnThings)
            {
                if (addedSomething) sb.Append(", ");

                if (thing is Frame frame)
                {
                    sb.Append(frame.LabelEntityToBuild);
                    sb.Append("RimWorldAccess.Map.Tile.Frame.Building".Translate());
                    if (frame.IsCompleted())
                    {
                        sb.Append("RimWorldAccess.Map.Tile.Frame.WorkLeft".Translate(frame.WorkLeft.ToStringWorkAmount()));
                    }
                    else
                    {
                        sb.Append("RimWorldAccess.Map.Tile.Frame.AwaitingSupplies".Translate());
                    }
                    string frameCellInfo = BuildingCellHelper.GetCellPrefix(frame, position);
                    if (!string.IsNullOrEmpty(frameCellInfo))
                    {
                        sb.Append("RimWorldAccess.Map.Tile.CellSuffix".Translate(frameCellInfo));
                    }
                }
                else if (thing is Building building)
                {
                    string buildingLabel = building.LabelShort;
                    if (building.def.defName.StartsWith("Smoothed") && building.def.building != null && !building.def.building.isNaturalRock)
                    {
                        buildingLabel += "RimWorldAccess.Map.Tile.WallSuffix".Translate();
                    }
                    if (building is Building_Door door)
                    {
                        buildingLabel = (door.Open
                            ? "RimWorldAccess.Map.Label.WithDoorOpen"
                            : "RimWorldAccess.Map.Label.WithDoorClosed").Translate(buildingLabel);
                    }
                    sb.Append(buildingLabel);

                    string cellInfo = BuildingCellHelper.GetCellPrefix(building, position);
                    if (!string.IsNullOrEmpty(cellInfo))
                    {
                        sb.Append("RimWorldAccess.Map.Tile.CellSuffix".Translate(cellInfo));
                    }
                    string tempControlInfo = GetTemperatureControlInfo(building);
                    if (!string.IsNullOrEmpty(tempControlInfo))
                    {
                        sb.Append(", ");
                        sb.Append(tempControlInfo);
                    }
                    string transportPodInfo = GetTransportPodInfo(building, map);
                    if (!string.IsNullOrEmpty(transportPodInfo))
                    {
                        sb.Append(", ");
                        sb.Append(transportPodInfo);
                    }
                    string progressInfo = GetBuildingProgressInfo(building);
                    if (!string.IsNullOrEmpty(progressInfo))
                    {
                        sb.Append(", ");
                        sb.Append(progressInfo);
                    }
                    // Inline storage group with the building
                    if (building is IStorageGroupMember storageMember && storageMember.Group != null)
                    {
                        sb.Append(", ");
                        sb.Append(storageMember.Group.RenamableLabel);
                    }
                }
                else if (thing is Blueprint blueprint)
                {
                    sb.Append(blueprint.LabelShort);
                    string cellInfo = BuildingCellHelper.GetCellPrefix(blueprint, position);
                    if (!string.IsNullOrEmpty(cellInfo))
                    {
                        sb.Append("RimWorldAccess.Map.Tile.CellSuffix".Translate(cellInfo));
                    }
                    if (blueprint is RimWorld.Blueprint_Storage blueprintStorage)
                    {
                        var storageBpMember = (IStorageGroupMember)blueprintStorage;
                        if (storageBpMember.Group != null)
                        {
                            sb.Append(", ");
                            sb.Append(storageBpMember.Group.RenamableLabel);
                        }
                    }
                }
                else if (thing is Plant plant)
                {
                    sb.Append(plant.LabelCap);
                }
                else if (thing is UnfinishedThing unfinished)
                {
                    sb.Append(unfinished.LabelShort);
                    if (unfinished.Initialized)
                    {
                        sb.Append("RimWorldAccess.Map.Tile.WorkLeftAppend".Translate(unfinished.workLeft.ToStringWorkAmount()));
                    }
                }
                else
                {
                    // Regular items
                    string itemLabel = thing.LabelMouseover;
                    CompForbiddable forbiddable = thing.TryGetComp<CompForbiddable>();
                    if (forbiddable != null && forbiddable.Forbidden)
                    {
                        itemLabel = "RimWorldAccess.Map.Tile.ForbiddenPrefix".Translate(itemLabel);
                    }
                    sb.Append(itemLabel);
                }

                // Append thing-targeted designations in parentheses
                if (thingDesignations.TryGetValue(thing, out var thingDesigs))
                {
                    foreach (var desig in thingDesigs)
                    {
                        sb.Append("RimWorldAccess.Map.Tile.CellSuffix".Translate(GetDesignationLabel(desig)));
                    }
                }

                addedSomething = true;
            }

            // 4. Terrain (gated by AnnounceTerrain setting)
            TerrainDef terrain = position.GetTerrain(map);
            if (terrain != null && RimWorldAccessMod_Settings.Settings.AnnounceTerrain)
            {
                if (addedSomething) sb.Append(", ");
                bool isPolluted = position.IsPolluted(map);
                string terrainLabel = isPolluted
                    ? (string)"PollutedTerrain".Translate(terrain.label).CapitalizeFirst()
                    : (string)terrain.LabelCap;
                if (terrain.defName.EndsWith("_Smooth"))
                {
                    terrainLabel += "RimWorldAccess.Map.Tile.FloorSuffix".Translate();
                }
                sb.Append(terrainLabel);
                addedSomething = true;
            }

            // 5. Roof status (after terrain - both environmental info)
            RoofDef roof = position.GetRoof(map);
            if (roof != null)
            {
                string roofText = (roof.isNatural
                    ? "RimWorldAccess.Map.Tile.Roof.Underground"
                    : "RimWorldAccess.Map.Tile.Roof.Roofed").Translate();
                if (addedSomething)
                    sb.Append("RimWorldAccess.Map.Tile.Roof.Append".Translate(roofText));
                else
                    sb.Append(roofText);
                addedSomething = true;
            }

            // 6. Empty fueling port cell (only if no buildings on tile)
            if (!hasBuildings)
            {
                string fuelingPortInfo = GetEmptyFuelingPortInfo(position, map);
                if (!string.IsNullOrEmpty(fuelingPortInfo))
                {
                    if (addedSomething) sb.Append(", ");
                    sb.Append(fuelingPortInfo);
                    addedSomething = true;
                }
            }

            // 7. Zone information
            Zone zone = position.GetZone(map);
            if (zone != null)
            {
                if (addedSomething) sb.Append(", ");
                sb.Append(zone.label);
                addedSomething = true;
            }

            // Add coordinates
            if (addedSomething)
                sb.Append("RimWorldAccess.Map.Tile.CoordsSuffix".Translate(position.x, position.z));
            else
                sb.Append("RimWorldAccess.Map.Tile.CoordsBare".Translate(position.x, position.z));

            // Add landing validity when in drop pod landing targeting mode
            if (IsDropPodLandingTargeting())
            {
                if (!DropCellFinder.IsGoodDropSpot(position, map, allowFogged: false, canRoofPunch: true))
                {
                    sb.Append("RimWorldAccess.Map.Tile.CantLand".Translate());
                }
            }

            // Add visibility status after coordinates when drafted pawn cannot see this position
            if (notVisible)
            {
                sb.Append("RimWorldAccess.Map.Tile.NotVisible".Translate());
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets detailed information about a tile for verbose mode.
        /// Includes all items, terrain, temperature, and other properties.
        /// </summary>
        public static string GetDetailedTileInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return "RimWorldAccess.Map.Tile.PositionOutOfBounds".Translate();

            var sb = new StringBuilder();
            sb.AppendLine("RimWorldAccess.Map.Tile.Detail.Header".Translate(position.x, position.z));

            // Terrain
            TerrainDef terrain = position.GetTerrain(map);
            if (terrain != null)
            {
                bool isPolluted = position.IsPolluted(map);
                string terrainLabel = isPolluted
                    ? (string)"PollutedTerrain".Translate(terrain.label).CapitalizeFirst()
                    : (string)terrain.LabelCap;
                sb.AppendLine("RimWorldAccess.Map.Tile.Detail.TerrainLine".Translate(terrainLabel));
            }

            // Get all things
            List<Thing> things = position.GetThingList(map);

            if (things.Count == 0)
            {
                sb.AppendLine("RimWorldAccess.Map.Tile.Detail.NoObjects".Translate());
            }
            else
            {
                // Group by category
                var pawns = things.OfType<Pawn>().ToList();
                var buildings = things.OfType<Building>().ToList();
                var plants = things.OfType<Plant>().ToList();
                var items = things.Where(t => !(t is Pawn) && !(t is Building) && !(t is Plant)).ToList();

                if (pawns.Count > 0)
                {
                    sb.AppendLine("RimWorldAccess.Map.Tile.Detail.PawnsHeader".Translate(pawns.Count));
                    foreach (var pawn in pawns)
                    {
                        sb.AppendLine("RimWorldAccess.Map.Tile.Detail.BulletItem".Translate(pawn.LabelShortCap));
                    }
                }

                if (buildings.Count > 0)
                {
                    sb.AppendLine("RimWorldAccess.Map.Tile.Detail.BuildingsHeader".Translate(buildings.Count));
                    foreach (var building in buildings)
                    {
                        string detailLabel = building.LabelShortCap;
                        if (building is Building_Door door)
                        {
                            detailLabel = (door.Open
                                ? "RimWorldAccess.Map.Label.WithDoorOpen"
                                : "RimWorldAccess.Map.Label.WithDoorClosed").Translate(detailLabel);
                        }
                        sb.Append("RimWorldAccess.Map.Tile.Detail.BulletItem".Translate(detailLabel));

                        // Add temperature control information if building is a cooler/heater
                        string tempControlInfo = GetTemperatureControlInfo(building);
                        if (!string.IsNullOrEmpty(tempControlInfo))
                        {
                            sb.Append("RimWorldAccess.Map.Tile.Detail.ParenSuffix".Translate(tempControlInfo));
                        }

                        // Add power information if building has power components
                        string powerInfo = PowerInfoHelper.GetPowerInfo(building);
                        if (!string.IsNullOrEmpty(powerInfo))
                        {
                            if (!string.IsNullOrEmpty(tempControlInfo))
                                sb.Append("RimWorldAccess.Map.Tile.Detail.CommaSuffix".Translate(powerInfo));
                            else
                                sb.Append("RimWorldAccess.Map.Tile.Detail.ParenSuffix".Translate(powerInfo));
                        }

                        sb.AppendLine();
                    }
                }

                if (items.Count > 0)
                {
                    sb.AppendLine("RimWorldAccess.Map.Tile.Detail.ItemsHeader".Translate(items.Count));
                    foreach (var item in items.Take(20)) // Limit to 20 items
                    {
                        string label = item.LabelShortCap;
                        if (item.stackCount > 1)
                            label += "RimWorldAccess.Map.Tile.Detail.StackCount".Translate(item.stackCount);

                        // Check if item is forbidden
                        CompForbiddable forbiddable = item.TryGetComp<CompForbiddable>();
                        if (forbiddable != null && forbiddable.Forbidden)
                        {
                            label = "RimWorldAccess.Map.Tile.ForbiddenPrefix".Translate(label);
                        }

                        sb.AppendLine("RimWorldAccess.Map.Tile.Detail.BulletItem".Translate(label));
                    }
                    if (items.Count > 20)
                        sb.AppendLine("RimWorldAccess.Map.Tile.Detail.MoreItems".Translate(items.Count - 20));
                }

                if (plants.Count > 0)
                {
                    sb.AppendLine("RimWorldAccess.Map.Tile.Detail.PlantsHeader".Translate(plants.Count));
                    foreach (var plant in plants)
                    {
                        sb.AppendLine("RimWorldAccess.Map.Tile.Detail.BulletItem".Translate(plant.LabelShortCap));
                    }
                }
            }

            // Additional info
            sb.AppendLine("RimWorldAccess.Map.Tile.Detail.EnvironmentalHeader".Translate());

            // Temperature (respects user's temperature mode preference)
            float temperature = position.GetTemperature(map);
            sb.AppendLine("RimWorldAccess.Map.Tile.Detail.TemperatureLine".Translate(MenuHelper.FormatTemperature(temperature, "F1")));

            // Roof
            RoofDef roof = position.GetRoof(map);
            if (roof != null)
            {
                sb.AppendLine("RimWorldAccess.Map.Tile.Detail.RoofLine".Translate(roof.LabelCap));
            }
            else
            {
                sb.AppendLine("RimWorldAccess.Map.Tile.Detail.RoofNone".Translate());
            }

            // Fog of war
            if (position.Fogged(map))
            {
                sb.AppendLine("RimWorldAccess.Map.Tile.Detail.FoggedStatus".Translate());
            }

            // Zone
            Zone zone = position.GetZone(map);
            if (zone != null)
            {
                sb.AppendLine("RimWorldAccess.Map.Tile.Detail.ZoneLine".Translate(zone.label));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets information about items and pawns at a tile (key 1).
        /// Lists all items with stack counts and all pawns with their labels.
        /// </summary>
        public static string GetItemsAndPawnsInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return "RimWorldAccess.Map.Tile.OutOfBounds".Translate();

            var sb = new StringBuilder();
            List<Thing> things = position.GetThingList(map);

            // Separate items and pawns
            var pawns = things.OfType<Pawn>().ToList();
            var items = things.Where(t => !(t is Pawn) && !(t is Building) && !(t is Plant)).ToList();

            if (pawns.Count == 0 && items.Count == 0)
            {
                return "RimWorldAccess.Map.Tile.Items.None".Translate();
            }

            // List all pawns
            if (pawns.Count > 0)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    if (i > 0) sb.Append(", ");

                    sb.Append(pawns[i].LabelShortCap);

                    // Add suffix for hostile or trader pawns
                    string suffix = GetPawnSuffix(pawns[i]);
                    if (!string.IsNullOrEmpty(suffix))
                    {
                        sb.Append(suffix);
                    }
                }
            }

            // List all items
            if (items.Count > 0)
            {
                if (pawns.Count > 0) sb.Append(", ");

                int displayLimit = 10;
                for (int i = 0; i < items.Count && i < displayLimit; i++)
                {
                    if (i > 0) sb.Append(", ");

                    string label = items[i].LabelShortCap;
                    if (items[i].stackCount > 1)
                        label += "RimWorldAccess.Map.Tile.Detail.StackCount".Translate(items[i].stackCount);

                    // Check if forbidden
                    CompForbiddable forbiddable = items[i].TryGetComp<CompForbiddable>();
                    if (forbiddable != null && forbiddable.Forbidden)
                        label = "RimWorldAccess.Map.Tile.ForbiddenPrefix".Translate(label);

                    sb.Append(label);
                }

                if (items.Count > displayLimit)
                    sb.Append("RimWorldAccess.Map.Tile.Items.MoreSuffix".Translate(items.Count - displayLimit));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets information about flooring at a tile (key 2).
        /// Shows terrain type, smoothness, beauty, and cleanliness.
        /// </summary>
        public static string GetFlooringInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return "RimWorldAccess.Map.Tile.OutOfBounds".Translate();

            var sb = new StringBuilder();
            TerrainDef terrain = position.GetTerrain(map);

            if (terrain == null)
                return "RimWorldAccess.Map.Tile.Flooring.None".Translate();

            bool isPolluted = position.IsPolluted(map);
            string terrainLabel = isPolluted
                ? (string)"PollutedTerrain".Translate(terrain.label).CapitalizeFirst()
                : (string)terrain.LabelCap;
            sb.Append(terrainLabel);

            // Add fertility if the terrain has it (matches MouseoverReadout display)
            float fertility = position.GetFertility(map);
            if (fertility > 0.0001f)
                sb.Append("RimWorldAccess.Map.Tile.Flooring.Fertility".Translate(fertility.ToStringPercent()));

            // Add smoothness information
            if (terrain.defName.EndsWith("_Smooth"))
                sb.Append("RimWorldAccess.Map.Tile.Flooring.Smooth".Translate());
            else if (terrain.defName.EndsWith("_Rough"))
                sb.Append("RimWorldAccess.Map.Tile.Flooring.Rough".Translate());

            // Add beauty if non-zero
            StatDef beautyStat = StatDefOf.Beauty;
            float beauty = terrain.GetStatValueAbstract(beautyStat);
            if (beauty != 0)
                sb.Append("RimWorldAccess.Map.Tile.Flooring.Beauty".Translate(beauty.ToString("F0")));

            // Add cleanliness if non-zero
            if (terrain.GetStatValueAbstract(StatDefOf.Cleanliness) != 0)
            {
                float cleanliness = terrain.GetStatValueAbstract(StatDefOf.Cleanliness);
                sb.Append("RimWorldAccess.Map.Tile.Flooring.Cleanliness".Translate(cleanliness.ToString("F1")));
            }

            // Add movement speed modifier
            if (terrain.pathCost > 0)
                sb.Append("RimWorldAccess.Map.Tile.Flooring.PathCost".Translate(terrain.pathCost));

            // Add fishing info if Odyssey DLC is active (matches MouseoverReadout display)
            if (ModsConfig.OdysseyActive && map.waterBodyTracker.TryGetWaterBodyAt(position, out var waterBody) && waterBody.HasFish)
            {
                // Fish species list (common + uncommon)
                var allFish = waterBody.CommonFishIncludingExtras.Concat(waterBody.UncommonFish);
                string fishList = allFish.Select(f => f.label).ToCommaList().CapitalizeFirst();

                // Population numbers (current/max) - use rounding to match Zone_Fishing.GetInspectString()
                int population = Mathf.RoundToInt(waterBody.Population);
                int maxPopulation = Mathf.RoundToInt(waterBody.MaxPopulation);

                sb.Append("RimWorldAccess.Map.Tile.Flooring.Fish".Translate(fishList, population, maxPopulation));

                // GillRot condition if active
                var gillRot = map.gameConditionManager.GetActiveCondition<GameCondition_GillRot>();
                if (gillRot != null && !gillRot.HiddenByOtherCondition(map))
                {
                    sb.Append("RimWorldAccess.Map.Tile.Detail.ParenSuffix".Translate(gillRot.LabelCap));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets information about plants at a tile (key 3).
        /// Shows plant species, growth percentage, and harvestable status.
        /// When a ground-penetrating scanner is active, also shows deep ore deposit info.
        /// </summary>
        public static string GetPlantsInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return "RimWorldAccess.Map.Tile.OutOfBounds".Translate();

            // Get plant info
            List<Thing> things = position.GetThingList(map);
            var plants = things.OfType<Plant>().ToList();
            bool hasPlants = plants.Count > 0;

            // Get deep ore info if scanner is active
            bool scannerActive = map.deepResourceGrid.AnyActiveDeepScannersOnMap();
            string deepOreInfo = null;
            if (scannerActive)
            {
                deepOreInfo = GetDeepOreInfo(position, map);
            }
            bool hasDeepOre = !string.IsNullOrEmpty(deepOreInfo);

            // Build response based on what's present
            if (!hasPlants && !hasDeepOre)
            {
                if (scannerActive)
                    return "RimWorldAccess.Map.Tile.Plants.NoneNoMinerals".Translate();
                else
                    return "RimWorldAccess.Map.Tile.Plants.None".Translate();
            }

            var sb = new StringBuilder();

            // Add plant info first (if present)
            if (hasPlants)
            {
                for (int i = 0; i < plants.Count; i++)
                {
                    if (i > 0) sb.Append(", ");

                    Plant plant = plants[i];
                    sb.Append(plant.LabelShortCap);

                    // Add growth percentage
                    float growthPercent = plant.Growth * 100f;
                    sb.Append("RimWorldAccess.Map.Tile.Plants.Growth".Translate(growthPercent.ToString("F0")));

                    // Check if harvestable
                    if (plant.HarvestableNow)
                        sb.Append("RimWorldAccess.Map.Tile.Plants.Harvestable".Translate());
                    else
                        sb.Append("RimWorldAccess.Map.Tile.Plants.NotHarvestable".Translate());

                    // Check if dying
                    if (plant.Dying)
                        sb.Append("RimWorldAccess.Map.Tile.Plants.Dying".Translate());
                }
            }

            // Add deep ore info (if present)
            if (hasDeepOre)
            {
                if (hasPlants)
                    sb.Append("RimWorldAccess.Map.Tile.Plants.DeepHeaderJoiner".Translate());
                sb.Append("RimWorldAccess.Map.Tile.Plants.DeepHeader".Translate());
                sb.Append(deepOreInfo);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets information about brightness and temperature at a tile (key 4).
        /// Shows light level (simplified), temperature, and indoor/outdoor status.
        /// </summary>
        public static string GetLightInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return "RimWorldAccess.Map.Tile.OutOfBounds".Translate();

            var sb = new StringBuilder();

            // Get light level - percentage and label (matches what sighted players see)
            float glowValue = map.glowGrid.GroundGlowAt(position);
            PsychGlow lightLevel = map.glowGrid.PsychGlowAt(position);
            string lightLabel = lightLevel.GetLabel();
            sb.Append("RimWorldAccess.Map.Tile.Light.LightLine".Translate(glowValue.ToStringPercent(), lightLabel));

            // Get temperature (respects user's temperature mode preference)
            float temperature = position.GetTemperature(map);
            sb.Append("RimWorldAccess.Map.Tile.Light.TemperatureSuffix".Translate(MenuHelper.FormatTemperature(temperature, "F1")));

            // Check if indoors/outdoors
            RoofDef roof = position.GetRoof(map);
            if (roof != null)
                sb.Append("RimWorldAccess.Map.Tile.Light.Indoors".Translate());
            else
                sb.Append("RimWorldAccess.Map.Tile.Light.Outdoors".Translate());

            // Check for temperature control buildings
            List<Thing> things = position.GetThingList(map);
            var buildings = things.OfType<Building>().ToList();

            foreach (var building in buildings)
            {
                string tempControlInfo = GetTemperatureControlInfo(building);
                if (!string.IsNullOrEmpty(tempControlInfo))
                {
                    sb.Append("RimWorldAccess.Map.Tile.Light.TempControlSuffix".Translate(building.LabelShortCap, tempControlInfo));
                }
            }

            return sb.ToString();
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

            var sb = new StringBuilder();
            int buildingsWithPower = 0;

            foreach (var building in buildings)
            {
                string powerInfo = PowerInfoHelper.GetPowerInfo(building);
                if (!string.IsNullOrEmpty(powerInfo))
                {
                    if (buildingsWithPower > 0)
                        sb.Append("RimWorldAccess.Map.Tile.Power.Joiner".Translate());

                    sb.Append("RimWorldAccess.Map.Tile.Power.Line".Translate(building.LabelShortCap, powerInfo));
                    buildingsWithPower++;
                }
            }

            if (buildingsWithPower == 0)
                return "RimWorldAccess.Map.Tile.Power.NoneConnected".Translate();

            return sb.ToString();
        }

        /// <summary>
        /// Gets information about room stats at a tile (key 5).
        /// Shows room name and all stats with quality tier descriptions.
        /// </summary>
        public static string GetRoomStatsInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return "RimWorldAccess.Map.Tile.OutOfBounds".Translate();

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

            var sb = new StringBuilder();

            // 1. Room label (identifier) - what room is this
            string roomLabel = room.GetRoomRoleLabel();
            if (!string.IsNullOrEmpty(roomLabel))
            {
                sb.Append(roomLabel.CapitalizeFirst());
            }
            else if (room.Role != null)
            {
                sb.Append(room.Role.LabelCap);
            }
            else
            {
                sb.Append("RimWorldAccess.Map.Tile.Room.Fallback".Translate());
            }

            // Stats ordered by volatility: dynamic first, static last
            // Dynamic: Cleanliness (changes constantly), Wealth (changes often)
            // Static: Impressiveness (derived), Beauty, Space (rarely changes)
            var statOrder = new[] { "Cleanliness", "Wealth", "Impressiveness", "Beauty", "Space" };
            var visibleStats = DefDatabase<RoomStatDef>.AllDefsListForReading.Where(def => !def.isHidden);

            // 2. Output stats in defined order
            foreach (var statName in statOrder)
            {
                var statDef = visibleStats.FirstOrDefault(s => s.defName == statName);
                if (statDef == null) continue;

                float value = room.GetStat(statDef);
                RoomStatScoreStage stage = statDef.GetScoreStage(value);
                string stageLabel = stage?.label?.CapitalizeFirst() ?? "";
                string prefix = (room.Role != null && room.Role.IsStatRelated(statDef)) ? "*" : "";

                if (!string.IsNullOrEmpty(stageLabel))
                {
                    sb.Append("RimWorldAccess.Map.Tile.Room.StatWithStage".Translate(prefix, statDef.LabelCap, stageLabel, statDef.ScoreToString(value)));
                }
                else
                {
                    sb.Append("RimWorldAccess.Map.Tile.Room.Stat".Translate(prefix, statDef.LabelCap, statDef.ScoreToString(value)));
                }
            }

            // 3. Any remaining stats not in our predefined order
            foreach (RoomStatDef statDef in visibleStats)
            {
                if (statOrder.Contains(statDef.defName)) continue;

                float value = room.GetStat(statDef);
                RoomStatScoreStage stage = statDef.GetScoreStage(value);
                string stageLabel = stage?.label?.CapitalizeFirst() ?? "";
                string prefix = (room.Role != null && room.Role.IsStatRelated(statDef)) ? "*" : "";

                if (!string.IsNullOrEmpty(stageLabel))
                {
                    sb.Append("RimWorldAccess.Map.Tile.Room.StatWithStage".Translate(prefix, statDef.LabelCap, stageLabel, statDef.ScoreToString(value)));
                }
                else
                {
                    sb.Append("RimWorldAccess.Map.Tile.Room.Stat".Translate(prefix, statDef.LabelCap, statDef.ScoreToString(value)));
                }
            }

            return sb.ToString();
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
            return BuildingCellHelper.GetCardinalDirection(direction) ?? "RimWorldAccess.Map.Tile.TempControl.UnknownDir".Translate();
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
            var sb = new StringBuilder();
            bool showCover = RimWorldAccessMod_Settings.Settings?.ShowCoverInfo ?? true;

            for (int i = 0; i < pawns.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(pawns[i].LabelShort);

                // Append designations targeting this pawn (e.g., "hunt", "tame")
                string designationSuffix = GetThingDesignationSuffix(pawns[i], thingDesignations);
                if (!string.IsNullOrEmpty(designationSuffix))
                    sb.Append("RimWorldAccess.Map.Tile.Pawn.DesignationSuffix".Translate(designationSuffix));

                string suffix = GetPawnSuffix(pawns[i]);
                if (!string.IsNullOrEmpty(suffix))
                    sb.Append(suffix);

                if (showCover)
                {
                    string coverInfo = CoverHelper.GetCoverInfo(pawns[i]);
                    if (!string.IsNullOrEmpty(coverInfo))
                        sb.Append("RimWorldAccess.Map.Tile.Pawn.CoverSuffix".Translate(coverInfo));
                }
            }

            return sb.ToString();
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

            // Format each group
            var parts = new List<string>();
            foreach (var group in groups)
            {
                string names = FormatPawnNames(group.pawns);
                var groupText = new StringBuilder(names);

                // Add designation info if present (e.g., "(hunt)")
                if (!string.IsNullOrEmpty(group.designationInfo))
                    groupText.Append("RimWorldAccess.Map.Tile.Pawn.DesignationSuffix".Translate(group.designationInfo));

                // Add suffix (hostile/trader) if present
                if (!string.IsNullOrEmpty(group.suffix))
                    groupText.Append(group.suffix);

                // Add cover info if present (before activity)
                if (!string.IsNullOrEmpty(group.coverInfo))
                    groupText.Append("RimWorldAccess.Map.Tile.Pawn.CoverSuffix".Translate(group.coverInfo));

                // Add activity if present
                if (!string.IsNullOrEmpty(group.activity))
                    groupText.Append("RimWorldAccess.Map.Tile.Pawn.ActivitySuffix".Translate(group.activity));

                parts.Add(groupText.ToString());
            }

            return string.Join(", ", parts);
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

            var sb = new StringBuilder();
            var areaNames = new List<string>();

            // Check all areas
            foreach (Area area in map.areaManager.AllAreas)
            {
                // Check if this position is in the area
                if (area[position])
                {
                    areaNames.Add(area.Label);
                }
            }

            if (areaNames.Count == 0)
                return "RimWorldAccess.Map.Tile.Areas.None".Translate();

            // Build the result string
            for (int i = 0; i < areaNames.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(areaNames[i]);
            }

            return sb.ToString();
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

            var sb = new StringBuilder();
            for (int i = 0; i < designations.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(GetDesignationLabel(designations[i]));
            }

            return sb.ToString();
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
