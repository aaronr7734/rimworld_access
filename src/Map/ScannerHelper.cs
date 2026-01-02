using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public class ScannerItem
    {
        public Thing Thing { get; set; }
        public List<Thing> BulkThings { get; set; } // For grouped items of the same type
        public List<IntVec3> BulkTerrainPositions { get; set; } // For grouped terrain tiles
        public Designation Designation { get; set; } // For designation items
        public List<Designation> BulkDesignations { get; set; } // For grouped designations of the same type
        public float Distance { get; set; }
        public string Label { get; set; }
        public IntVec3 Position { get; set; }
        public bool IsTerrain { get; set; } // True if this represents terrain instead of a Thing
        public bool IsDesignation => Designation != null; // True if this represents a designation
        public Zone Zone { get; set; } // For zone items
        public bool IsZone => Zone != null; // True if this represents a zone
        public Room Room { get; set; } // For room items
        public bool IsRoom => Room != null; // True if this represents a room
        public int BulkCount => BulkThings?.Count ?? (BulkTerrainPositions?.Count ?? (BulkDesignations?.Count ?? 1));
        public bool IsBulkGroup => (BulkThings != null && BulkThings.Count > 1) ||
                                   (BulkTerrainPositions != null && BulkTerrainPositions.Count > 1) ||
                                   (BulkDesignations != null && BulkDesignations.Count > 1);

        public ScannerItem(Thing thing, IntVec3 cursorPosition)
        {
            Thing = thing;
            Position = thing.Position;
            Distance = (thing.Position - cursorPosition).LengthHorizontal;
            IsTerrain = false;

            // Build label with additional context
            if (thing is Pawn pawn)
            {
                Label = pawn.LabelShort + TileInfoHelper.GetPawnSuffix(pawn);
            }
            else
            {
                Label = thing.LabelShort ?? thing.def.label ?? "Unknown";
            }
        }

        // Constructor for bulk groups
        public ScannerItem(List<Thing> things, IntVec3 cursorPosition)
        {
            if (things == null || things.Count == 0)
                throw new ArgumentException("Bulk group must contain at least one thing");

            BulkThings = things;
            Thing = things[0]; // Primary thing (closest)
            Position = Thing.Position;
            Distance = (Thing.Position - cursorPosition).LengthHorizontal;
            IsTerrain = false;

            // Build label from first item
            if (Thing is Pawn pawn)
            {
                Label = pawn.LabelShort + TileInfoHelper.GetPawnSuffix(pawn);
            }
            else
            {
                Label = Thing.LabelShort ?? Thing.def.label ?? "Unknown";
            }
        }

        // Constructor for terrain tiles (no actual Thing object)
        public ScannerItem(IntVec3 cell, string label, IntVec3 cursorPosition)
        {
            Thing = null;
            Position = cell;
            Distance = (cell - cursorPosition).LengthHorizontal;
            Label = label;
            IsTerrain = true;
        }

        // Constructor for grouped terrain tiles
        public ScannerItem(List<IntVec3> positions, string label, IntVec3 cursorPosition)
        {
            if (positions == null || positions.Count == 0)
                throw new ArgumentException("Terrain group must contain at least one position");

            Thing = null;
            BulkTerrainPositions = positions;
            Position = positions[0]; // Primary position (closest)
            Distance = (positions[0] - cursorPosition).LengthHorizontal;
            Label = label;
            IsTerrain = true;
        }

        // Constructor for designation items
        public ScannerItem(Designation designation, IntVec3 cursorPosition)
        {
            Designation = designation;
            Position = designation.target.Cell;
            Distance = (Position - cursorPosition).LengthHorizontal;
            IsTerrain = false;
            Thing = designation.target.HasThing ? designation.target.Thing : null;

            // Get localized label from the Designator
            string defLabel = ScannerHelper.GetLocalizedDesignationLabel(designation.def);

            if (designation.target.HasThing && designation.target.Thing != null)
            {
                Label = $"{designation.target.Thing.LabelShort} ({defLabel})";
            }
            else
            {
                // For cell-based designations (mine, smooth floor, etc.), get terrain or building label
                var map = Find.CurrentMap;
                if (map != null)
                {
                    var edifice = Position.GetEdifice(map);
                    if (edifice != null)
                    {
                        Label = $"{edifice.LabelShort} ({defLabel})";
                    }
                    else
                    {
                        var terrain = Position.GetTerrain(map);
                        Label = terrain != null
                            ? $"{terrain.LabelCap} ({defLabel})"
                            : defLabel;
                    }
                }
                else
                {
                    Label = defLabel;
                }
            }
        }

        // Constructor for grouped designations (same type)
        public ScannerItem(List<Designation> designations, IntVec3 cursorPosition)
        {
            if (designations == null || designations.Count == 0)
                throw new ArgumentException("Designation group must contain at least one designation");

            BulkDesignations = designations;
            Designation = designations[0]; // Primary designation (closest)
            Position = Designation.target.Cell;
            Distance = (Position - cursorPosition).LengthHorizontal;
            IsTerrain = false;
            Thing = Designation.target.HasThing ? Designation.target.Thing : null;

            // Get localized label from the Designator
            Label = ScannerHelper.GetLocalizedDesignationLabel(Designation.def);
        }

        // Constructor for zone items
        public ScannerItem(Zone zone, IntVec3 cursorPosition)
        {
            Zone = zone;
            IsTerrain = false;

            // Calculate center position of zone
            if (zone.cells != null && zone.cells.Count > 0)
            {
                int avgX = (int)zone.cells.Average(c => c.x);
                int avgZ = (int)zone.cells.Average(c => c.z);
                Position = new IntVec3(avgX, 0, avgZ);
            }
            else
            {
                Position = zone.Position; // Fallback to first cell
            }

            Distance = (Position - cursorPosition).LengthHorizontal;

            // Build label with zone info
            if (zone is Zone_Growing growZone && growZone.PlantDefToGrow != null)
            {
                Label = $"{zone.label} ({growZone.PlantDefToGrow.label})";
            }
            else
            {
                Label = zone.label;
            }
        }

        // Constructor for room items
        public ScannerItem(Room room, IntVec3 cursorPosition)
        {
            Room = room;
            IsTerrain = false;

            // Calculate center position of room
            var cells = room.Cells.ToList();
            if (cells.Count > 0)
            {
                int avgX = (int)cells.Average(c => c.x);
                int avgZ = (int)cells.Average(c => c.z);
                Position = new IntVec3(avgX, 0, avgZ);
            }
            else
            {
                Position = IntVec3.Zero;
            }

            Distance = (Position - cursorPosition).LengthHorizontal;

            // Build label with role
            Label = room.GetRoomRoleLabel();
        }

        public string GetDirectionFrom(IntVec3 fromPosition)
        {
            IntVec3 offset = Position - fromPosition;

            // Calculate angle in degrees (0 = north, 90 = east)
            double angle = Math.Atan2(offset.x, offset.z) * (180.0 / Math.PI);
            if (angle < 0) angle += 360;

            // Convert to 8-direction compass
            if (angle >= 337.5 || angle < 22.5) return "North";
            if (angle >= 22.5 && angle < 67.5) return "Northeast";
            if (angle >= 67.5 && angle < 112.5) return "East";
            if (angle >= 112.5 && angle < 157.5) return "Southeast";
            if (angle >= 157.5 && angle < 202.5) return "South";
            if (angle >= 202.5 && angle < 247.5) return "Southwest";
            if (angle >= 247.5 && angle < 292.5) return "West";
            return "Northwest";
        }
    }

    public class ScannerSubcategory
    {
        public string Name { get; set; }
        public List<ScannerItem> Items { get; set; }

        public ScannerSubcategory(string name)
        {
            Name = name;
            Items = new List<ScannerItem>();
        }

        public bool IsEmpty => Items == null || Items.Count == 0;
    }

    public class ScannerCategory
    {
        public string Name { get; set; }
        public List<ScannerSubcategory> Subcategories { get; set; }

        public ScannerCategory(string name)
        {
            Name = name;
            Subcategories = new List<ScannerSubcategory>();
        }

        public bool IsEmpty => Subcategories == null || Subcategories.All(sc => sc.IsEmpty);

        public int TotalItemCount => Subcategories.Sum(sc => sc.Items.Count);
    }

    public static class ScannerHelper
    {
        public static List<ScannerCategory> CollectMapItems(Map map, IntVec3 cursorPosition)
        {
            var categories = new List<ScannerCategory>();

            // Initialize all categories with dash-formatted names

            // Pawns category (renamed from Colonists)
            var pawnsCategory = new ScannerCategory("Pawns");
            var pawnsPlayerSubcat = new ScannerSubcategory("Pawns-Player");
            var pawnsNPCSubcat = new ScannerSubcategory("Pawns-NPC");
            var pawnsMechanoidsSubcat = new ScannerSubcategory("Pawns-Mechanoids");
            pawnsCategory.Subcategories.Add(pawnsPlayerSubcat);
            pawnsCategory.Subcategories.Add(pawnsNPCSubcat);
            pawnsCategory.Subcategories.Add(pawnsMechanoidsSubcat);

            // Tame Animals with Pen/Non-Pen split
            var tameAnimalsCategory = new ScannerCategory("Tame");
            var tamePenSubcat = new ScannerSubcategory("Tame-Pen");
            var tameNonPenSubcat = new ScannerSubcategory("Tame-NonPen");
            tameAnimalsCategory.Subcategories.Add(tamePenSubcat);
            tameAnimalsCategory.Subcategories.Add(tameNonPenSubcat);

            // Wild Animals with Hostile/Passive split
            var wildAnimalsCategory = new ScannerCategory("Wild");
            var wildHostileSubcat = new ScannerSubcategory("Wild-Hostile");
            var wildPassiveSubcat = new ScannerSubcategory("Wild-Passive");
            wildAnimalsCategory.Subcategories.Add(wildHostileSubcat);
            wildAnimalsCategory.Subcategories.Add(wildPassiveSubcat);

            // Hazards category
            var hazardsCategory = new ScannerCategory("Hazards");
            var fireSubcat = new ScannerSubcategory("Hazards-Fire");
            var blightSubcat = new ScannerSubcategory("Hazards-Blight");
            hazardsCategory.Subcategories.Add(fireSubcat);
            hazardsCategory.Subcategories.Add(blightSubcat);

            // Buildings category (architect tab structure)
            var buildingsCategory = new ScannerCategory("Buildings");
            var structureSubcat = new ScannerSubcategory("Buildings-Structure");
            var productionSubcat = new ScannerSubcategory("Buildings-Production");
            var furnitureSubcat = new ScannerSubcategory("Buildings-Furniture");
            var powerSubcat = new ScannerSubcategory("Buildings-Power");
            var securitySubcat = new ScannerSubcategory("Buildings-Security");
            var miscBuildingsSubcat = new ScannerSubcategory("Buildings-Misc");
            var recreationSubcat = new ScannerSubcategory("Buildings-Recreation");
            var shipSubcat = new ScannerSubcategory("Buildings-Ship");
            var temperatureSubcat = new ScannerSubcategory("Buildings-Temperature");
            buildingsCategory.Subcategories.Add(structureSubcat);
            buildingsCategory.Subcategories.Add(productionSubcat);
            buildingsCategory.Subcategories.Add(furnitureSubcat);
            buildingsCategory.Subcategories.Add(powerSubcat);
            buildingsCategory.Subcategories.Add(securitySubcat);
            buildingsCategory.Subcategories.Add(miscBuildingsSubcat);
            buildingsCategory.Subcategories.Add(recreationSubcat);
            buildingsCategory.Subcategories.Add(shipSubcat);
            buildingsCategory.Subcategories.Add(temperatureSubcat);

            // Trees category
            var treesCategory = new ScannerCategory("Trees");
            var harvestableTreesSubcat = new ScannerSubcategory("Trees-Harvestable");
            var nonHarvestableTreesSubcat = new ScannerSubcategory("Trees-NonHarvestable");
            treesCategory.Subcategories.Add(harvestableTreesSubcat);
            treesCategory.Subcategories.Add(nonHarvestableTreesSubcat);

            // Plants category
            var plantsCategory = new ScannerCategory("Plants");
            var harvestablePlantsSubcat = new ScannerSubcategory("Plants-Harvestable");
            var debrisSubcat = new ScannerSubcategory("Plants-Debris");
            plantsCategory.Subcategories.Add(harvestablePlantsSubcat);
            plantsCategory.Subcategories.Add(debrisSubcat);

            // Items category with Stored/Furniture/Scattered split
            var itemsCategory = new ScannerCategory("Items");
            var itemsStoredSubcat = new ScannerSubcategory("Items-Stored");
            var itemsFurnitureSubcat = new ScannerSubcategory("Items-Furniture");
            var itemsScatteredSubcat = new ScannerSubcategory("Items-Scattered");
            var itemsForbiddenSubcat = new ScannerSubcategory("Items-Forbidden");
            itemsCategory.Subcategories.Add(itemsStoredSubcat);
            itemsCategory.Subcategories.Add(itemsFurnitureSubcat);
            itemsCategory.Subcategories.Add(itemsScatteredSubcat);
            itemsCategory.Subcategories.Add(itemsForbiddenSubcat);

            // Terrain category
            var terrainCategory = new ScannerCategory("Terrain");
            var terrainNaturalSubcat = new ScannerSubcategory("Terrain-Natural");
            var terrainConstructedSubcat = new ScannerSubcategory("Terrain-Constructed");
            terrainCategory.Subcategories.Add(terrainNaturalSubcat);
            terrainCategory.Subcategories.Add(terrainConstructedSubcat);

            // Mineable category with Rare/Stone/Chunks subcategories
            var mineableCategory = new ScannerCategory("Mineable");
            var mineableRareSubcat = new ScannerSubcategory("Mineable-Rare");
            var mineableStoneSubcat = new ScannerSubcategory("Mineable-Stone");
            var mineableChunksSubcat = new ScannerSubcategory("Mineable-Chunks");
            mineableCategory.Subcategories.Add(mineableRareSubcat);
            mineableCategory.Subcategories.Add(mineableStoneSubcat);
            mineableCategory.Subcategories.Add(mineableChunksSubcat);

            // Orders category with subcategories for each designation type
            var ordersCategory = new ScannerCategory("Orders");
            var ordersConstructionSubcat = new ScannerSubcategory("Orders-Construction");
            var ordersHaulSubcat = new ScannerSubcategory("Orders-Haul");
            var ordersHuntSubcat = new ScannerSubcategory("Orders-Hunt");
            var ordersMineSubcat = new ScannerSubcategory("Orders-Mine");
            var ordersDeconstructSubcat = new ScannerSubcategory("Orders-Deconstruct");
            var ordersUninstallSubcat = new ScannerSubcategory("Orders-Uninstall");
            var ordersCutSubcat = new ScannerSubcategory("Orders-Cut");
            var ordersHarvestSubcat = new ScannerSubcategory("Orders-Harvest");
            var ordersSmoothSubcat = new ScannerSubcategory("Orders-Smooth");
            var ordersTameSubcat = new ScannerSubcategory("Orders-Tame");
            var ordersSlaughterSubcat = new ScannerSubcategory("Orders-Slaughter");
            var ordersOtherSubcat = new ScannerSubcategory("Orders-Other");
            ordersCategory.Subcategories.Add(ordersConstructionSubcat);
            ordersCategory.Subcategories.Add(ordersHaulSubcat);
            ordersCategory.Subcategories.Add(ordersHuntSubcat);
            ordersCategory.Subcategories.Add(ordersMineSubcat);
            ordersCategory.Subcategories.Add(ordersDeconstructSubcat);
            ordersCategory.Subcategories.Add(ordersUninstallSubcat);
            ordersCategory.Subcategories.Add(ordersCutSubcat);
            ordersCategory.Subcategories.Add(ordersHarvestSubcat);
            ordersCategory.Subcategories.Add(ordersSmoothSubcat);
            ordersCategory.Subcategories.Add(ordersTameSubcat);
            ordersCategory.Subcategories.Add(ordersSlaughterSubcat);
            ordersCategory.Subcategories.Add(ordersOtherSubcat);

            // Zones category
            var zonesCategory = new ScannerCategory("Zones");
            var zonesGrowingSubcat = new ScannerSubcategory("Zones-Growing");
            var zonesStockpileSubcat = new ScannerSubcategory("Zones-Stockpile");
            var zonesFishingSubcat = new ScannerSubcategory("Zones-Fishing");
            var zonesOtherSubcat = new ScannerSubcategory("Zones-Other");
            zonesCategory.Subcategories.Add(zonesGrowingSubcat);
            zonesCategory.Subcategories.Add(zonesStockpileSubcat);
            zonesCategory.Subcategories.Add(zonesFishingSubcat);
            zonesCategory.Subcategories.Add(zonesOtherSubcat);

            // Rooms category
            var roomsCategory = new ScannerCategory("Rooms");
            var roomsAllSubcat = new ScannerSubcategory("Rooms-All");
            roomsCategory.Subcategories.Add(roomsAllSubcat);

            // Collect all things from the map
            var allThings = map.listerThings.AllThings;
            var playerFaction = Faction.OfPlayer;
            var fogGrid = map.fogGrid;

            foreach (var thing in allThings)
            {
                if (!thing.Spawned || !thing.Position.IsValid)
                    continue;

                // Skip items in fog of war (unseen tiles)
                if (fogGrid.IsFogged(thing.Position))
                    continue;

                var item = new ScannerItem(thing, cursorPosition);

                if (thing is Pawn pawn)
                {
                    // Categorize pawns
                    if (pawn.RaceProps.IsMechanoid)
                    {
                        // Mechanoids subcategory (all mechanoids regardless of faction)
                        pawnsMechanoidsSubcat.Items.Add(item);
                    }
                    else if (pawn.RaceProps.Humanlike)
                    {
                        // Pawns category
                        if (pawn.Faction == playerFaction)
                        {
                            pawnsPlayerSubcat.Items.Add(item);
                        }
                        else
                        {
                            pawnsNPCSubcat.Items.Add(item);
                        }
                    }
                    else if (pawn.RaceProps.Animal)
                    {
                        // Animals
                        if (pawn.Faction == playerFaction)
                        {
                            // Tame animals - check if pen animal (roamer = needs to be managed by rope)
                            if (pawn.Roamer)
                            {
                                tamePenSubcat.Items.Add(item);
                            }
                            else
                            {
                                tameNonPenSubcat.Items.Add(item);
                            }
                        }
                        else
                        {
                            // Wild animals - check if hostile
                            if (pawn.HostileTo(playerFaction))
                            {
                                wildHostileSubcat.Items.Add(item);
                            }
                            else
                            {
                                wildPassiveSubcat.Items.Add(item);
                            }
                        }
                    }
                }
                else if (thing is Fire)
                {
                    // Fire hazard
                    fireSubcat.Items.Add(item);
                }
                else if (thing is Plant plant)
                {
                    // Check for blight
                    if (plant.Blighted)
                    {
                        blightSubcat.Items.Add(item);
                    }

                    if (plant.def.plant.IsTree)
                    {
                        // Trees
                        if (plant.def.plant.harvestYield > 0)
                        {
                            harvestableTreesSubcat.Items.Add(item);
                        }
                        else
                        {
                            nonHarvestableTreesSubcat.Items.Add(item);
                        }
                    }
                    else
                    {
                        // Non-tree plants
                        if (plant.HarvestableNow || plant.def.plant.harvestYield > 0)
                        {
                            harvestablePlantsSubcat.Items.Add(item);
                        }
                        else
                        {
                            // Debris (grass, etc.)
                            debrisSubcat.Items.Add(item);
                        }
                    }
                }
                else if (thing is Blueprint || thing is Frame)
                {
                    // Blueprints and frames (construction projects) go to Orders-Construction
                    ordersConstructionSubcat.Items.Add(item);
                }
                else if (thing is Building building)
                {
                    // Skip natural rock/ore (these are handled as mineable tiles below)
                    if (building.def.building != null && building.def.building.isNaturalRock)
                        continue;

                    // Categorize buildings by designation category
                    var designationCategory = building.def.designationCategory;
                    if (designationCategory != null)
                    {
                        switch (designationCategory.defName)
                        {
                            case "Structure":
                                structureSubcat.Items.Add(item);
                                break;
                            case "Production":
                                productionSubcat.Items.Add(item);
                                break;
                            case "Furniture":
                                furnitureSubcat.Items.Add(item);
                                break;
                            case "Power":
                                powerSubcat.Items.Add(item);
                                break;
                            case "Security":
                                securitySubcat.Items.Add(item);
                                break;
                            case "Misc":
                                miscBuildingsSubcat.Items.Add(item);
                                break;
                            case "Joy":
                                recreationSubcat.Items.Add(item);
                                break;
                            case "Ship":
                                shipSubcat.Items.Add(item);
                                break;
                            case "Temperature":
                                temperatureSubcat.Items.Add(item);
                                break;
                            default:
                                // If no specific category, put in structure
                                structureSubcat.Items.Add(item);
                                break;
                        }
                    }
                    else
                    {
                        // No designation category - default to structure
                        structureSubcat.Items.Add(item);
                    }
                }
                else if (IsStoneChunk(thing))
                {
                    // Stone chunks go to mineable chunks subcategory
                    mineableChunksSubcat.Items.Add(item);
                }
                else if (!IsDebrisItem(thing))
                {
                    // Regular items - categorize by storage state
                    if (thing.IsForbidden(Faction.OfPlayer))
                    {
                        itemsForbiddenSubcat.Items.Add(item);
                    }
                    else if (IsUninstalledFurniture(thing))
                    {
                        // Uninstalled furniture
                        itemsFurnitureSubcat.Items.Add(item);
                    }
                    else if (IsInStorage(thing, map))
                    {
                        // Items in stockpiles/shelves
                        itemsStoredSubcat.Items.Add(item);
                    }
                    else
                    {
                        // Scattered items not in storage
                        itemsScatteredSubcat.Items.Add(item);
                    }
                }
            }

            // Collect mineable tiles and terrain
            var allCells = map.AllCells;
            foreach (var cell in allCells)
            {
                // Skip fogged cells
                if (fogGrid.IsFogged(cell))
                    continue;

                var terrain = map.terrainGrid.TerrainAt(cell);

                // Check for mineable rocks (both ore and plain stone)
                var edifice = cell.GetEdifice(map);
                if (edifice != null && edifice.def.building != null && edifice.def.building.isNaturalRock)
                {
                    var item = new ScannerItem(edifice, cursorPosition);

                    // Separate rare minerals (ore) from plain stone
                    if (edifice.def.building.isResourceRock && edifice.def.building.mineableYield > 0)
                    {
                        // Rare minerals (steel, gold, plasteel, uranium, etc.)
                        mineableRareSubcat.Items.Add(item);
                    }
                    else
                    {
                        // Plain stone (granite, marble, slate, limestone, sandstone)
                        mineableStoneSubcat.Items.Add(item);
                    }
                }

                // Collect terrain tiles
                if (terrain != null)
                {
                    // Natural terrain (rich soil, etc.)
                    if (!terrain.layerable && terrain.natural)
                    {
                        // Only include interesting natural terrain (rich soil, water, marsh, etc.)
                        if (terrain.fertility >= 1.4f || // Rich soil
                            terrain.HasTag("Water") ||
                            terrain.defName.Contains("Marsh") ||
                            terrain.defName.Contains("Sand") ||
                            terrain.defName.Contains("Gravel") ||
                            terrain.defName.Contains("Ice"))
                        {
                            var terrainItem = new ScannerItem(cell, terrain.label, cursorPosition);
                            terrainNaturalSubcat.Items.Add(terrainItem);
                        }
                    }
                    // Constructed floors
                    else if (terrain.layerable || !terrain.natural)
                    {
                        // Only include actually constructed floors (not natural dirt/soil)
                        if (!terrain.natural)
                        {
                            var terrainItem = new ScannerItem(cell, terrain.label, cursorPosition);
                            terrainConstructedSubcat.Items.Add(terrainItem);
                        }
                    }
                }
            }

            // Collect all designations/orders
            var allDesignations = map.designationManager.AllDesignations;
            foreach (var designation in allDesignations)
            {
                // Skip designations without valid targets
                if (designation == null || designation.def == null)
                    continue;

                // Skip if target cell is invalid or fogged
                IntVec3 targetCell = designation.target.Cell;
                if (!targetCell.IsValid || fogGrid.IsFogged(targetCell))
                    continue;

                // Skip if thing target is not spawned
                if (designation.target.HasThing && !designation.target.Thing.Spawned)
                    continue;

                var item = new ScannerItem(designation, cursorPosition);

                // Categorize by designation type
                if (designation.def == DesignationDefOf.Haul)
                {
                    ordersHaulSubcat.Items.Add(item);
                }
                else if (designation.def == DesignationDefOf.Hunt)
                {
                    ordersHuntSubcat.Items.Add(item);
                }
                else if (designation.def == DesignationDefOf.Mine || designation.def == DesignationDefOf.MineVein)
                {
                    ordersMineSubcat.Items.Add(item);
                }
                else if (designation.def == DesignationDefOf.Deconstruct)
                {
                    ordersDeconstructSubcat.Items.Add(item);
                }
                else if (designation.def == DesignationDefOf.Uninstall)
                {
                    ordersUninstallSubcat.Items.Add(item);
                }
                else if (designation.def == DesignationDefOf.CutPlant || designation.def == DesignationDefOf.ExtractTree)
                {
                    ordersCutSubcat.Items.Add(item);
                }
                else if (designation.def == DesignationDefOf.HarvestPlant)
                {
                    ordersHarvestSubcat.Items.Add(item);
                }
                else if (designation.def == DesignationDefOf.SmoothFloor || designation.def == DesignationDefOf.SmoothWall)
                {
                    ordersSmoothSubcat.Items.Add(item);
                }
                else if (designation.def == DesignationDefOf.Tame)
                {
                    ordersTameSubcat.Items.Add(item);
                }
                else if (designation.def == DesignationDefOf.Slaughter)
                {
                    ordersSlaughterSubcat.Items.Add(item);
                }
                else
                {
                    // All other designations (Strip, Open, Flick, RemoveFloor, etc.)
                    ordersOtherSubcat.Items.Add(item);
                }
            }

            // Collect all zones
            var allZones = map.zoneManager.AllZones;
            foreach (var zone in allZones)
            {
                if (zone == null || zone.cells == null || zone.cells.Count == 0)
                    continue;

                var item = new ScannerItem(zone, cursorPosition);

                if (zone is Zone_Growing)
                {
                    zonesGrowingSubcat.Items.Add(item);
                }
                else if (zone is Zone_Stockpile)
                {
                    zonesStockpileSubcat.Items.Add(item);
                }
                else if (zone.GetType().Name == "Zone_Fishing")
                {
                    zonesFishingSubcat.Items.Add(item);
                }
                else
                {
                    zonesOtherSubcat.Items.Add(item);
                }
            }

            // Collect all rooms
            var allRooms = map.regionGrid.AllRooms;
            foreach (var room in allRooms)
            {
                // Skip outdoor areas and invalid rooms
                if (room.PsychologicallyOutdoors || !room.ProperRoom)
                    continue;

                // Skip rooms that are entirely in fog of war
                bool hasVisibleCell = false;
                foreach (var cell in room.Cells)
                {
                    if (!fogGrid.IsFogged(cell))
                    {
                        hasVisibleCell = true;
                        break;
                    }
                }
                if (!hasVisibleCell)
                    continue;

                var item = new ScannerItem(room, cursorPosition);
                roomsAllSubcat.Items.Add(item);
            }

            // Group identical items and sort all subcategories by distance
            foreach (var category in new[] { pawnsCategory, tameAnimalsCategory, wildAnimalsCategory,
                                             hazardsCategory, buildingsCategory, treesCategory, plantsCategory,
                                             itemsCategory, terrainCategory, mineableCategory, ordersCategory,
                                             zonesCategory, roomsCategory })
            {
                foreach (var subcat in category.Subcategories)
                {
                    // First sort by distance
                    subcat.Items = subcat.Items.OrderBy(i => i.Distance).ToList();

                    // Then group identical items (but not pawns - they're always unique)
                    subcat.Items = GroupIdenticalItems(subcat.Items, cursorPosition);
                }
            }

            // Add categories in order (only non-empty ones will be included later)
            categories.Add(pawnsCategory);
            categories.Add(tameAnimalsCategory);
            categories.Add(wildAnimalsCategory);
            categories.Add(hazardsCategory);
            categories.Add(buildingsCategory);
            categories.Add(treesCategory);
            categories.Add(plantsCategory);
            categories.Add(itemsCategory);
            categories.Add(terrainCategory);
            categories.Add(mineableCategory);
            categories.Add(ordersCategory);
            categories.Add(zonesCategory);
            categories.Add(roomsCategory);

            // Remove empty categories
            categories.RemoveAll(c => c.IsEmpty);

            return categories;
        }

        private static bool IsInStorage(Thing thing, Map map)
        {
            // Check if thing is in a stockpile zone
            var zone = map.zoneManager.ZoneAt(thing.Position);
            if (zone is Zone_Stockpile)
                return true;

            // Check if thing is on a storage building (shelf, rack, etc.)
            var storageBuilding = thing.Position.GetThingList(map)
                .OfType<Building_Storage>()
                .FirstOrDefault();

            return storageBuilding != null;
        }

        private static bool IsUninstalledFurniture(Thing thing)
        {
            // Check if it's a minified (uninstalled) building
            if (thing is MinifiedThing)
                return true;

            // Check if the thing def is a building that can be reinstalled
            if (thing.def.Minifiable)
                return true;

            return false;
        }

        private static bool IsStoneChunk(Thing thing)
        {
            // Check if this is a stone chunk (mineable resource lying on ground)
            if (thing.def.defName.Contains("Chunk"))
                return true;

            // Also check thingCategories for StoneChunks
            if (thing.def.thingCategories != null)
            {
                foreach (var cat in thing.def.thingCategories)
                {
                    if (cat.defName.Contains("Chunk"))
                        return true;
                }
            }

            return false;
        }

        private static bool IsDebrisItem(Thing thing)
        {
            // Check for common debris types
            if (thing.def.category == ThingCategory.Filth)
                return true;

            // Note: Chunks are now handled by IsStoneChunk, not filtered as debris

            if (thing.def.defName == "Slag")
                return true;

            // Check for rubble-like items
            var label = thing.def.label?.ToLower() ?? "";
            if (label.Contains("rubble") || label.Contains("slag"))
                return true;

            return false;
        }

        /// <summary>
        /// Groups identical items together (same def, quality, stuff).
        /// Pawns are never grouped - they're unique individuals.
        /// Terrain tiles are grouped by label (e.g., all "granite flagstone" tiles together).
        /// Designations are grouped by designation type.
        /// </summary>
        private static List<ScannerItem> GroupIdenticalItems(List<ScannerItem> items, IntVec3 cursorPosition)
        {
            var grouped = new List<ScannerItem>();
            var processedThings = new HashSet<Thing>();
            var processedPositions = new HashSet<IntVec3>(); // For terrain items
            var processedDesignations = new HashSet<Designation>(); // For designation items

            foreach (var item in items)
            {
                // Group terrain items by label
                if (item.IsTerrain)
                {
                    // Skip if we already processed this position
                    if (processedPositions.Contains(item.Position))
                        continue;

                    // Find all terrain tiles with the same label
                    var identicalPositions = new List<IntVec3> { item.Position };
                    processedPositions.Add(item.Position);

                    foreach (var otherItem in items)
                    {
                        if (!otherItem.IsTerrain || processedPositions.Contains(otherItem.Position))
                            continue;

                        if (otherItem.Label == item.Label)
                        {
                            identicalPositions.Add(otherItem.Position);
                            processedPositions.Add(otherItem.Position);
                        }
                    }

                    // Create grouped terrain item if multiple found, otherwise add single item
                    if (identicalPositions.Count > 1)
                    {
                        // Sort by distance for the bulk group
                        identicalPositions = identicalPositions.OrderBy(p => (p - cursorPosition).LengthHorizontal).ToList();
                        grouped.Add(new ScannerItem(identicalPositions, item.Label, cursorPosition));
                    }
                    else
                    {
                        grouped.Add(item);
                    }
                    continue;
                }

                // Group designation items by designation def (type)
                if (item.IsDesignation)
                {
                    // Skip if we already processed this designation
                    if (processedDesignations.Contains(item.Designation))
                        continue;

                    // Find all designations with the same def (type)
                    var identicalDesignations = new List<Designation> { item.Designation };
                    processedDesignations.Add(item.Designation);

                    foreach (var otherItem in items)
                    {
                        if (!otherItem.IsDesignation || processedDesignations.Contains(otherItem.Designation))
                            continue;

                        if (otherItem.Designation.def == item.Designation.def)
                        {
                            identicalDesignations.Add(otherItem.Designation);
                            processedDesignations.Add(otherItem.Designation);
                        }
                    }

                    // Create grouped designation item if multiple found, otherwise add single item
                    if (identicalDesignations.Count > 1)
                    {
                        // Sort by distance for the bulk group
                        identicalDesignations = identicalDesignations.OrderBy(d => (d.target.Cell - cursorPosition).LengthHorizontal).ToList();
                        grouped.Add(new ScannerItem(identicalDesignations, cursorPosition));
                    }
                    else
                    {
                        grouped.Add(item);
                    }
                    continue;
                }

                // Zones are never grouped - each zone is unique
                if (item.IsZone)
                {
                    grouped.Add(item);
                    continue;
                }

                // Rooms are never grouped - each room is unique
                if (item.IsRoom)
                {
                    grouped.Add(item);
                    continue;
                }

                // Skip if already processed
                if (processedThings.Contains(item.Thing))
                    continue;

                // Pawns are never grouped - they're unique individuals
                if (item.Thing is Pawn)
                {
                    grouped.Add(item);
                    processedThings.Add(item.Thing);
                    continue;
                }

                // Find all identical items
                var identicalThings = new List<Thing> { item.Thing };
                processedThings.Add(item.Thing);

                foreach (var otherItem in items)
                {
                    if (processedThings.Contains(otherItem.Thing))
                        continue;

                    if (AreThingsIdentical(item.Thing, otherItem.Thing))
                    {
                        identicalThings.Add(otherItem.Thing);
                        processedThings.Add(otherItem.Thing);
                    }
                }

                // Create grouped item if multiple found, otherwise add single item
                if (identicalThings.Count > 1)
                {
                    // Sort by distance for the bulk group
                    identicalThings = identicalThings.OrderBy(t => (t.Position - cursorPosition).LengthHorizontal).ToList();
                    grouped.Add(new ScannerItem(identicalThings, cursorPosition));
                }
                else
                {
                    grouped.Add(item);
                }
            }

            return grouped;
        }

        /// <summary>
        /// Unwraps a MinifiedThing to get the actual inner item, or returns the thing as-is.
        /// Handles MinifiedThing and MinifiedTree (which extends MinifiedThing).
        /// </summary>
        private static Thing GetActualThing(Thing thing)
        {
            if (thing is MinifiedThing minified && minified.InnerThing != null)
                return minified.InnerThing;
            return thing;
        }

        /// <summary>
        /// Checks if two things are identical (same def, quality, stuff, etc.)
        /// HP differences are ignored to prevent duplicate entries for damaged items.
        /// </summary>
        private static bool AreThingsIdentical(Thing a, Thing b)
        {
            // Unwrap minified things to compare actual items
            var actualA = GetActualThing(a);
            var actualB = GetActualThing(b);

            // Must be the same def
            if (actualA.def != actualB.def)
                return false;

            // Must have same stuff (material)
            if (actualA.Stuff != actualB.Stuff)
                return false;

            // Check quality if applicable
            var qualityA = actualA.TryGetComp<CompQuality>();
            var qualityB = actualB.TryGetComp<CompQuality>();

            if (qualityA != null && qualityB != null)
            {
                if (qualityA.Quality != qualityB.Quality)
                    return false;
            }
            else if (qualityA != null || qualityB != null)
            {
                // One has quality, the other doesn't
                return false;
            }

            // HP is now ignored - damaged trees, walls, etc. are grouped together
            return true;
        }

        /// <summary>
        /// Gets the localized label for a DesignationDef by finding its Designator.
        /// </summary>
        public static string GetLocalizedDesignationLabel(DesignationDef def)
        {
            if (def == null)
                return "Unknown";

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
    }
}
