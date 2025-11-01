using System.Collections.Generic;
using System.Linq;
using System.Text;
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
                return "Out of bounds";

            var sb = new StringBuilder();

            // Get all things at this position
            List<Thing> things = position.GetThingList(map);

            // Categorize things
            var pawns = new List<Pawn>();
            var buildings = new List<Building>();
            var items = new List<Thing>();
            var plants = new List<Plant>();

            foreach (var thing in things)
            {
                if (thing is Pawn pawn)
                    pawns.Add(pawn);
                else if (thing is Building building)
                    buildings.Add(building);
                else if (thing is Plant plant)
                    plants.Add(plant);
                else
                    items.Add(thing);
            }

            bool addedSomething = false;

            // Add individual pawns (most important)
            foreach (var pawn in pawns.Take(3))
            {
                if (addedSomething) sb.Append(", ");
                sb.Append(pawn.LabelShort);
                addedSomething = true;
            }
            if (pawns.Count > 3)
            {
                if (addedSomething) sb.Append(", ");
                sb.Append($"and {pawns.Count - 3} more pawns");
                addedSomething = true;
            }

            // Add buildings with power and temperature info
            foreach (var building in buildings.Take(2))
            {
                if (addedSomething) sb.Append(", ");

                // Check if this is a smoothed stone wall and add "wall" suffix
                string buildingLabel = building.LabelShort;
                if (building.def.defName.StartsWith("Smoothed") && building.def.building != null && !building.def.building.isNaturalRock)
                {
                    buildingLabel += " wall";
                }
                sb.Append(buildingLabel);

                // Add temperature control information if building is a cooler/heater
                string tempControlInfo = GetTemperatureControlInfo(building);
                if (!string.IsNullOrEmpty(tempControlInfo))
                {
                    sb.Append(", ");
                    sb.Append(tempControlInfo);
                }

                // Add power information if building has power components
                string powerInfo = PowerInfoHelper.GetPowerInfo(building);
                if (!string.IsNullOrEmpty(powerInfo))
                {
                    sb.Append(", ");
                    sb.Append(powerInfo);
                }

                addedSomething = true;
            }
            if (buildings.Count > 2)
            {
                if (addedSomething) sb.Append(", ");
                sb.Append($"and {buildings.Count - 2} more buildings");
                addedSomething = true;
            }

            // Add items
            if (items.Count > 0)
            {
                if (addedSomething) sb.Append(", ");
                if (items.Count == 1)
                {
                    string itemLabel = items[0].LabelShort;
                    CompForbiddable forbiddable = items[0].TryGetComp<CompForbiddable>();
                    if (forbiddable != null && forbiddable.Forbidden)
                    {
                        itemLabel = "Forbidden " + itemLabel;
                    }
                    sb.Append(itemLabel);
                }
                else
                {
                    sb.Append($"{items.Count} items");
                }
                addedSomething = true;
            }

            // Add plants if present and nothing else important
            if (plants.Count > 0 && !addedSomething)
            {
                sb.Append(plants[0].LabelShort);
                addedSomething = true;
            }

            // Check if terrain has no audio match - if so, announce terrain name
            TerrainDef terrain = position.GetTerrain(map);
            if (terrain != null && !TerrainAudioHelper.HasAudioMatch(terrain))
            {
                if (addedSomething) sb.Append(", ");

                // Check if this is a smooth stone floor and add "floor" suffix
                string terrainLabel = terrain.LabelCap;
                if (terrain.defName.EndsWith("_Smooth"))
                {
                    terrainLabel += " floor";
                }
                sb.Append(terrainLabel);
                addedSomething = true;
            }

            // Add zone information if present
            Zone zone = position.GetZone(map);
            if (zone != null)
            {
                if (addedSomething) sb.Append(", ");
                sb.Append($"in {zone.label}");
            }

            // Add roofed/unroofed status
            RoofDef roof = position.GetRoof(map);
            string roofStatus = (roof != null) ? "roofed" : "unroofed";
            if (addedSomething)
                sb.Append($", {roofStatus}");
            else
                sb.Append(roofStatus);

            // Add light level
            PsychGlow lightLevel = map.glowGrid.PsychGlowAt(position);
            string lightDescription = lightLevel.GetLabel();
            sb.Append($", {lightDescription}");

            // Add coordinates
            sb.Append($", at {position.x}, {position.z}");

            return sb.ToString();
        }

        /// <summary>
        /// Gets detailed information about a tile for verbose mode.
        /// Includes all items, terrain, temperature, and other properties.
        /// </summary>
        public static string GetDetailedTileInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return "Position out of bounds";

            var sb = new StringBuilder();
            sb.AppendLine($"=== Tile {position.x}, {position.z} ===");

            // Terrain
            TerrainDef terrain = position.GetTerrain(map);
            if (terrain != null)
            {
                sb.AppendLine($"Terrain: {terrain.LabelCap}");
            }

            // Get all things
            List<Thing> things = position.GetThingList(map);

            if (things.Count == 0)
            {
                sb.AppendLine("No objects on this tile");
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
                    sb.AppendLine($"\nPawns ({pawns.Count}):");
                    foreach (var pawn in pawns)
                    {
                        sb.AppendLine($"  - {pawn.LabelShortCap}");
                    }
                }

                if (buildings.Count > 0)
                {
                    sb.AppendLine($"\nBuildings ({buildings.Count}):");
                    foreach (var building in buildings)
                    {
                        sb.Append($"  - {building.LabelShortCap}");

                        // Add temperature control information if building is a cooler/heater
                        string tempControlInfo = GetTemperatureControlInfo(building);
                        if (!string.IsNullOrEmpty(tempControlInfo))
                        {
                            sb.Append($" ({tempControlInfo})");
                        }

                        // Add power information if building has power components
                        string powerInfo = PowerInfoHelper.GetPowerInfo(building);
                        if (!string.IsNullOrEmpty(powerInfo))
                        {
                            if (!string.IsNullOrEmpty(tempControlInfo))
                                sb.Append($", {powerInfo}");
                            else
                                sb.Append($" ({powerInfo})");
                        }

                        sb.AppendLine();
                    }
                }

                if (items.Count > 0)
                {
                    sb.AppendLine($"\nItems ({items.Count}):");
                    foreach (var item in items.Take(20)) // Limit to 20 items
                    {
                        string label = item.LabelShortCap;
                        if (item.stackCount > 1)
                            label += $" x{item.stackCount}";

                        // Check if item is forbidden
                        CompForbiddable forbiddable = item.TryGetComp<CompForbiddable>();
                        if (forbiddable != null && forbiddable.Forbidden)
                        {
                            label = "Forbidden " + label;
                        }

                        sb.AppendLine($"  - {label}");
                    }
                    if (items.Count > 20)
                        sb.AppendLine($"  ... and {items.Count - 20} more items");
                }

                if (plants.Count > 0)
                {
                    sb.AppendLine($"\nPlants ({plants.Count}):");
                    foreach (var plant in plants)
                    {
                        sb.AppendLine($"  - {plant.LabelShortCap}");
                    }
                }
            }

            // Additional info
            sb.AppendLine("\n--- Environmental Info ---");

            // Temperature
            float temperature = position.GetTemperature(map);
            sb.AppendLine($"Temperature: {temperature:F1}°C");

            // Roof
            RoofDef roof = position.GetRoof(map);
            if (roof != null)
            {
                sb.AppendLine($"Roof: {roof.LabelCap}");
            }
            else
            {
                sb.AppendLine("Roof: None (outdoors)");
            }

            // Fog of war
            if (position.Fogged(map))
            {
                sb.AppendLine("Status: Fogged (not visible)");
            }

            // Zone
            Zone zone = position.GetZone(map);
            if (zone != null)
            {
                sb.AppendLine($"Zone: {zone.label}");
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
                return "Out of bounds";

            var sb = new StringBuilder();
            List<Thing> things = position.GetThingList(map);

            // Separate items and pawns
            var pawns = things.OfType<Pawn>().ToList();
            var items = things.Where(t => !(t is Pawn) && !(t is Building) && !(t is Plant)).ToList();

            if (pawns.Count == 0 && items.Count == 0)
            {
                return "no items or pawns";
            }

            // List all pawns
            if (pawns.Count > 0)
            {
                sb.Append($"{pawns.Count} pawn");
                if (pawns.Count > 1) sb.Append("s");
                sb.Append(": ");

                for (int i = 0; i < pawns.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(pawns[i].LabelShortCap);
                }
            }

            // List all items
            if (items.Count > 0)
            {
                if (pawns.Count > 0) sb.Append(". ");
                sb.Append($"{items.Count} item");
                if (items.Count > 1) sb.Append("s");
                sb.Append(": ");

                int displayLimit = 10;
                for (int i = 0; i < items.Count && i < displayLimit; i++)
                {
                    if (i > 0) sb.Append(", ");

                    string label = items[i].LabelShortCap;
                    if (items[i].stackCount > 1)
                        label += $" x{items[i].stackCount}";

                    // Check if forbidden
                    CompForbiddable forbiddable = items[i].TryGetComp<CompForbiddable>();
                    if (forbiddable != null && forbiddable.Forbidden)
                        label = "Forbidden " + label;

                    sb.Append(label);
                }

                if (items.Count > displayLimit)
                    sb.Append($", and {items.Count - displayLimit} more");
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
                return "Out of bounds";

            var sb = new StringBuilder();
            TerrainDef terrain = position.GetTerrain(map);

            if (terrain == null)
                return "no terrain information";

            sb.Append(terrain.LabelCap);

            // Add smoothness information
            if (terrain.defName.EndsWith("_Smooth"))
                sb.Append(", smooth");
            else if (terrain.defName.EndsWith("_Rough"))
                sb.Append(", rough");

            // Add beauty if non-zero
            StatDef beautyStat = StatDefOf.Beauty;
            float beauty = terrain.GetStatValueAbstract(beautyStat);
            if (beauty != 0)
                sb.Append($", beauty {beauty:F0}");

            // Add cleanliness if non-zero
            if (terrain.GetStatValueAbstract(StatDefOf.Cleanliness) != 0)
            {
                float cleanliness = terrain.GetStatValueAbstract(StatDefOf.Cleanliness);
                sb.Append($", cleanliness {cleanliness:F1}");
            }

            // Add movement speed modifier
            if (terrain.pathCost > 0)
                sb.Append($", path cost {terrain.pathCost}");

            return sb.ToString();
        }

        /// <summary>
        /// Gets information about plants at a tile (key 3).
        /// Shows plant species, growth percentage, and harvestable status.
        /// </summary>
        public static string GetPlantsInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return "Out of bounds";

            List<Thing> things = position.GetThingList(map);
            var plants = things.OfType<Plant>().ToList();

            if (plants.Count == 0)
                return "no plants";

            var sb = new StringBuilder();

            for (int i = 0; i < plants.Count; i++)
            {
                if (i > 0) sb.Append(", ");

                Plant plant = plants[i];
                sb.Append(plant.LabelShortCap);

                // Add growth percentage
                float growthPercent = plant.Growth * 100f;
                sb.Append($" ({growthPercent:F0}% grown)");

                // Check if harvestable
                if (plant.HarvestableNow)
                    sb.Append(", harvestable");
                else
                    sb.Append(", not harvestable");

                // Check if dying
                if (plant.Dying)
                    sb.Append(", dying");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets information about lighting at a tile (key 4).
        /// Shows light level and natural/artificial status.
        /// </summary>
        public static string GetLightInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return "Out of bounds";

            var sb = new StringBuilder();

            // Get light level
            PsychGlow lightLevel = map.glowGrid.PsychGlowAt(position);
            string lightDescription = lightLevel.GetLabel();
            sb.Append(lightDescription);

            // Get actual glow value
            float glow = map.glowGrid.GroundGlowAt(position, false);
            sb.Append($" ({glow:F1} glow)");

            // Check if natural light
            float naturalLight = map.skyManager.CurSkyGlow;
            if (naturalLight > 0)
                sb.Append($", natural light {naturalLight:F1}");

            return sb.ToString();
        }

        /// <summary>
        /// Gets information about temperature at a tile (key 5).
        /// Shows current temperature and indoor/outdoor status.
        /// </summary>
        public static string GetTemperatureInfo(IntVec3 position, Map map)
        {
            if (map == null || !position.InBounds(map))
                return "Out of bounds";

            var sb = new StringBuilder();

            // Get temperature
            float temperature = position.GetTemperature(map);
            string tempCelsius = $"{temperature:F1}°C";

            sb.Append(tempCelsius);

            // Check if indoors/outdoors
            RoofDef roof = position.GetRoof(map);
            if (roof != null)
                sb.Append(" indoors");
            else
                sb.Append(" outdoors");

            // Check for temperature control buildings nearby
            List<Thing> things = position.GetThingList(map);
            var buildings = things.OfType<Building>().ToList();

            foreach (var building in buildings)
            {
                string tempControlInfo = GetTemperatureControlInfo(building);
                if (!string.IsNullOrEmpty(tempControlInfo))
                {
                    sb.Append($". {building.LabelShortCap}: {tempControlInfo}");
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

                directionInfo = $"cooling {coolingDir}, heating {heatingDir}";
            }
            else
            {
                // For other temperature control devices (heaters, vents, etc.)
                directionInfo = "temperature control";
            }

            // Add target temperature
            float targetTemp = tempControl.TargetTemperature;
            string tempString = targetTemp.ToStringTemperature("F0");

            return $"{directionInfo}, target {tempString}";
        }

        /// <summary>
        /// Converts an IntVec3 direction to a cardinal direction string.
        /// </summary>
        private static string GetCardinalDirection(IntVec3 direction)
        {
            if (direction == IntVec3.North) return "north";
            if (direction == IntVec3.South) return "south";
            if (direction == IntVec3.East) return "east";
            if (direction == IntVec3.West) return "west";
            return "unknown";
        }
    }
}
