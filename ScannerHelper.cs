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
        public float Distance { get; set; }
        public string Label { get; set; }
        public IntVec3 Position { get; set; }
        public int BulkCount => BulkThings?.Count ?? 1;
        public bool IsBulkGroup => BulkThings != null && BulkThings.Count > 1;

        public ScannerItem(Thing thing, IntVec3 cursorPosition)
        {
            Thing = thing;
            Position = thing.Position;
            Distance = (thing.Position - cursorPosition).LengthHorizontal;

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

            // Initialize all categories
            var colonistsCategory = new ScannerCategory("Colonists");
            var playerPawnsSubcat = new ScannerSubcategory("Player-Controlled Pawns");
            var npcPawnsSubcat = new ScannerSubcategory("NPCs");
            colonistsCategory.Subcategories.Add(playerPawnsSubcat);
            colonistsCategory.Subcategories.Add(npcPawnsSubcat);

            var tameAnimalsCategory = new ScannerCategory("Tame Animals");
            var tameAnimalsSubcat = new ScannerSubcategory("All");
            tameAnimalsCategory.Subcategories.Add(tameAnimalsSubcat);

            var wildAnimalsCategory = new ScannerCategory("Wild Animals");
            var wildAnimalsSubcat = new ScannerSubcategory("All");
            wildAnimalsCategory.Subcategories.Add(wildAnimalsSubcat);

            var buildingsCategory = new ScannerCategory("Buildings");
            var wallsDoorsSubcat = new ScannerSubcategory("Walls & Doors");
            var otherBuildingsSubcat = new ScannerSubcategory("Other Buildings");
            buildingsCategory.Subcategories.Add(wallsDoorsSubcat);
            buildingsCategory.Subcategories.Add(otherBuildingsSubcat);

            var treesCategory = new ScannerCategory("Trees");
            var harvestableTreesSubcat = new ScannerSubcategory("Harvestable Trees");
            var nonHarvestableTreesSubcat = new ScannerSubcategory("Non-Harvestable Trees");
            treesCategory.Subcategories.Add(harvestableTreesSubcat);
            treesCategory.Subcategories.Add(nonHarvestableTreesSubcat);

            var plantsCategory = new ScannerCategory("Plants");
            var harvestablePlantsSubcat = new ScannerSubcategory("Harvestable Plants");
            var debrisSubcat = new ScannerSubcategory("Debris");
            plantsCategory.Subcategories.Add(harvestablePlantsSubcat);
            plantsCategory.Subcategories.Add(debrisSubcat);

            var itemsCategory = new ScannerCategory("Items");
            var itemsSubcat = new ScannerSubcategory("All Items");
            var forbiddenItemsSubcat = new ScannerSubcategory("Forbidden Items");
            itemsCategory.Subcategories.Add(itemsSubcat);
            itemsCategory.Subcategories.Add(forbiddenItemsSubcat);

            var mineableTilesCategory = new ScannerCategory("Mineable Tiles");
            var mineableTilesSubcat = new ScannerSubcategory("All");
            mineableTilesCategory.Subcategories.Add(mineableTilesSubcat);

            // Collect all things from the map
            var allThings = map.listerThings.AllThings;
            var playerFaction = Faction.OfPlayer;

            foreach (var thing in allThings)
            {
                if (!thing.Spawned || !thing.Position.IsValid)
                    continue;

                var item = new ScannerItem(thing, cursorPosition);

                if (thing is Pawn pawn)
                {
                    // Categorize pawns
                    if (pawn.RaceProps.Humanlike)
                    {
                        // Colonists category
                        if (pawn.Faction == playerFaction)
                        {
                            playerPawnsSubcat.Items.Add(item);
                        }
                        else
                        {
                            npcPawnsSubcat.Items.Add(item);
                        }
                    }
                    else if (pawn.RaceProps.Animal)
                    {
                        // Animals
                        if (pawn.Faction == playerFaction)
                        {
                            tameAnimalsSubcat.Items.Add(item);
                        }
                        else
                        {
                            wildAnimalsSubcat.Items.Add(item);
                        }
                    }
                }
                else if (thing is Building building)
                {
                    // Skip natural rock/ore
                    if (building.def.building != null && building.def.building.isNaturalRock)
                        continue;

                    // Categorize buildings
                    if (building is Building_Door ||
                        (building.def.graphicData != null && building.def.graphicData.linkType == LinkDrawerType.CornerFiller))
                    {
                        // Walls and doors
                        wallsDoorsSubcat.Items.Add(item);
                    }
                    else
                    {
                        // Other buildings
                        otherBuildingsSubcat.Items.Add(item);
                    }
                }
                else if (thing is Plant plant)
                {
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
                else if (!IsDebrisItem(thing))
                {
                    // Regular items (not debris)
                    if (thing.IsForbidden(Faction.OfPlayer))
                    {
                        forbiddenItemsSubcat.Items.Add(item);
                    }
                    else
                    {
                        itemsSubcat.Items.Add(item);
                    }
                }
            }

            // Collect mineable tiles
            var allCells = map.AllCells;
            foreach (var cell in allCells)
            {
                var terrain = map.terrainGrid.TerrainAt(cell);

                // Check if it's a mineable rock
                if (terrain != null && terrain.affordances != null &&
                    terrain.affordances.Contains(TerrainAffordanceDefOf.Heavy))
                {
                    // Check if there's a mineable thing at this location
                    var edifice = cell.GetEdifice(map);
                    if (edifice != null && edifice.def.building != null &&
                        edifice.def.building.isResourceRock && edifice.def.building.mineableYield > 0)
                    {
                        var item = new ScannerItem(edifice, cursorPosition);
                        mineableTilesSubcat.Items.Add(item);
                    }
                }
            }

            // Group identical items and sort all subcategories by distance
            foreach (var category in new[] { colonistsCategory, tameAnimalsCategory, wildAnimalsCategory,
                                             buildingsCategory, treesCategory, plantsCategory,
                                             itemsCategory, mineableTilesCategory })
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
            categories.Add(colonistsCategory);
            categories.Add(tameAnimalsCategory);
            categories.Add(wildAnimalsCategory);
            categories.Add(buildingsCategory);
            categories.Add(treesCategory);
            categories.Add(plantsCategory);
            categories.Add(itemsCategory);
            categories.Add(mineableTilesCategory);

            // Remove empty categories
            categories.RemoveAll(c => c.IsEmpty);

            return categories;
        }

        private static bool IsDebrisItem(Thing thing)
        {
            // Check for common debris types
            if (thing.def.category == ThingCategory.Filth)
                return true;

            if (thing.def.defName.Contains("Chunk"))
                return true;

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
        /// Pawns are never grouped - they're always unique individuals.
        /// </summary>
        private static List<ScannerItem> GroupIdenticalItems(List<ScannerItem> items, IntVec3 cursorPosition)
        {
            var grouped = new List<ScannerItem>();
            var processedThings = new HashSet<Thing>();

            foreach (var item in items)
            {
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
        /// Checks if two things are identical (same def, quality, stuff, etc.)
        /// </summary>
        private static bool AreThingsIdentical(Thing a, Thing b)
        {
            // Must be the same def
            if (a.def != b.def)
                return false;

            // Must have same stuff (material)
            if (a.Stuff != b.Stuff)
                return false;

            // Check quality if applicable
            var qualityA = a.TryGetComp<CompQuality>();
            var qualityB = b.TryGetComp<CompQuality>();

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

            // Check hit points percentage (for damaged items)
            float hpPercentA = (float)a.HitPoints / a.MaxHitPoints;
            float hpPercentB = (float)b.HitPoints / b.MaxHitPoints;

            // Consider items identical if HP difference is less than 10%
            if (Math.Abs(hpPercentA - hpPercentB) > 0.1f)
                return false;

            return true;
        }
    }
}
