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
        /// Gets a concise summary of what's on a tile, prioritizing pawns, buildings, and items.
        /// Format: "John, Mary, Stone wall, 5 items, at 23, 30"
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

            // Build the announcement prioritizing pawns first
            bool addedSomething = false;

            // Add individual pawns (most important)
            foreach (var pawn in pawns.Take(3)) // Limit to first 3 pawns
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

            // Add individual buildings (next priority)
            foreach (var building in buildings.Take(2)) // Limit to first 2 buildings
            {
                if (addedSomething) sb.Append(", ");
                sb.Append(building.LabelShort);

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

            // Summarize items by count
            if (items.Count > 0)
            {
                if (addedSomething) sb.Append(", ");
                if (items.Count == 1)
                {
                    string itemLabel = items[0].LabelShort;
                    // Check if item is forbidden
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

            // Add plant if present and nothing else important
            if (plants.Count > 0 && !addedSomething)
            {
                sb.Append(plants[0].LabelShort);
                addedSomething = true;
            }

            // If nothing on the tile, mention terrain
            if (!addedSomething)
            {
                TerrainDef terrain = position.GetTerrain(map);
                if (terrain != null)
                {
                    sb.Append(terrain.LabelCap);
                    addedSomething = true;
                }
            }

            // Add zone information if present
            Zone zone = position.GetZone(map);
            if (zone != null)
            {
                if (addedSomething) sb.Append(", ");
                sb.Append($"in {zone.label}");
                addedSomething = true;
            }

            // Add coordinates
            if (addedSomething)
                sb.Append($", at {position.x}, {position.z}");
            else
                sb.Append($"Empty, at {position.x}, {position.z}");

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
