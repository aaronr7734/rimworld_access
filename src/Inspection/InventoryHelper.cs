using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper class for collecting and organizing colony-wide inventory data
    /// </summary>
    public static class InventoryHelper
    {
        /// <summary>
        /// Represents an aggregated inventory item with its total quantity and storage locations
        /// </summary>
        public class InventoryItem
        {
            public ThingDef Def { get; set; }
            public int TotalQuantity { get; set; }
            public List<IntVec3> StorageLocations { get; set; }

            public InventoryItem(ThingDef def)
            {
                Def = def;
                TotalQuantity = 0;
                StorageLocations = new List<IntVec3>();
            }

            public string GetDisplayLabel()
            {
                return $"{Def.LabelCap} x{TotalQuantity}";
            }
        }

        /// <summary>
        /// Represents a category with its items and subcategories
        /// </summary>
        public class CategoryNode
        {
            public ThingCategoryDef CategoryDef { get; set; }
            public List<InventoryItem> Items { get; set; }
            public List<CategoryNode> SubCategories { get; set; }
            public int TotalItemCount { get; set; }

            public CategoryNode(ThingCategoryDef categoryDef)
            {
                CategoryDef = categoryDef;
                Items = new List<InventoryItem>();
                SubCategories = new List<CategoryNode>();
                TotalItemCount = 0;
            }

            public string GetDisplayLabel()
            {
                if (Items.Count > 0)
                {
                    return $"{CategoryDef.LabelCap} ({Items.Count} types)";
                }
                return CategoryDef.LabelCap;
            }
        }

        /// <summary>
        /// Collects all items from stockpiles and storage buildings across the colony
        /// </summary>
        public static List<Thing> GetAllStoredItems()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Log.Warning("InventoryHelper: Cannot get stored items - no current map");
                return new List<Thing>();
            }

            List<Thing> allItems = new List<Thing>();

            // Get items from stockpiles
            if (map.zoneManager?.AllZones != null)
            {
                foreach (Zone zone in map.zoneManager.AllZones)
                {
                    if (zone is Zone_Stockpile stockpile)
                    {
                        SlotGroup slotGroup = stockpile.GetSlotGroup();
                        if (slotGroup?.HeldThings != null)
                        {
                            foreach (Thing item in slotGroup.HeldThings)
                            {
                                allItems.Add(item);
                            }
                        }
                    }
                }
            }

            // Get items from storage buildings
            if (map.listerBuildings != null)
            {
                foreach (Building_Storage storage in map.listerBuildings.AllBuildingsColonistOfClass<Building_Storage>())
                {
                    SlotGroup slotGroup = storage.GetSlotGroup();
                    if (slotGroup?.HeldThings != null)
                    {
                        foreach (Thing item in slotGroup.HeldThings)
                        {
                            allItems.Add(item);
                        }
                    }
                }
            }

            return allItems;
        }

        /// <summary>
        /// Aggregates items by ThingDef, summing quantities and tracking locations
        /// </summary>
        public static Dictionary<ThingDef, InventoryItem> AggregateStacks(List<Thing> items)
        {
            Dictionary<ThingDef, InventoryItem> aggregated = new Dictionary<ThingDef, InventoryItem>();

            foreach (Thing item in items)
            {
                if (item?.def == null) continue;

                if (!aggregated.ContainsKey(item.def))
                {
                    aggregated[item.def] = new InventoryItem(item.def);
                }

                InventoryItem invItem = aggregated[item.def];
                invItem.TotalQuantity += item.stackCount;

                // Store the location of this item (for jump-to functionality)
                // Only store if we don't have too many locations already (performance)
                if (invItem.StorageLocations.Count < 10)
                {
                    IntVec3 position = item.Position;
                    if (!invItem.StorageLocations.Contains(position))
                    {
                        invItem.StorageLocations.Add(position);
                    }
                }
            }

            return aggregated;
        }

        /// <summary>
        /// Groups inventory items by their categories, building a hierarchical tree
        /// </summary>
        public static List<CategoryNode> BuildCategoryTree(Dictionary<ThingDef, InventoryItem> aggregatedItems)
        {
            // Build a dictionary of all categories that have items
            Dictionary<ThingCategoryDef, CategoryNode> categoryNodes = new Dictionary<ThingCategoryDef, CategoryNode>();

            foreach (var kvp in aggregatedItems)
            {
                ThingDef thingDef = kvp.Key;
                InventoryItem item = kvp.Value;

                if (thingDef.thingCategories == null || thingDef.thingCategories.Count == 0)
                {
                    // Item has no category - skip it or add to "Uncategorized"
                    continue;
                }

                // Add item to all its categories
                foreach (ThingCategoryDef category in thingDef.thingCategories)
                {
                    // Ensure category node exists
                    if (!categoryNodes.ContainsKey(category))
                    {
                        categoryNodes[category] = new CategoryNode(category);
                    }

                    // Add item to this category
                    categoryNodes[category].Items.Add(item);
                    categoryNodes[category].TotalItemCount++;

                    // Ensure all parent categories exist
                    ThingCategoryDef parentCategory = category.parent;
                    while (parentCategory != null)
                    {
                        if (!categoryNodes.ContainsKey(parentCategory))
                        {
                            categoryNodes[parentCategory] = new CategoryNode(parentCategory);
                        }
                        categoryNodes[parentCategory].TotalItemCount++;
                        parentCategory = parentCategory.parent;
                    }
                }
            }

            // Build the tree structure by linking parents and children
            foreach (var kvp in categoryNodes)
            {
                ThingCategoryDef category = kvp.Key;
                CategoryNode node = kvp.Value;

                if (category.parent != null && categoryNodes.ContainsKey(category.parent))
                {
                    CategoryNode parentNode = categoryNodes[category.parent];
                    if (!parentNode.SubCategories.Contains(node))
                    {
                        parentNode.SubCategories.Add(node);
                    }
                }
            }

            // Find root categories (categories with no parent or whose parent isn't in our tree)
            // Skip the actual "Root" ThingCategoryDef and treat its children as top-level
            List<CategoryNode> rootCategories = new List<CategoryNode>();
            foreach (var kvp in categoryNodes)
            {
                ThingCategoryDef category = kvp.Key;
                CategoryNode node = kvp.Value;

                // Skip the actual "Root" category - we'll show its children instead
                if (category == ThingCategoryDefOf.Root)
                    continue;

                // This is a root if it has no parent, its parent isn't in our category set,
                // or its parent is the "Root" category (which we're skipping)
                if (category.parent == null ||
                    !categoryNodes.ContainsKey(category.parent) ||
                    category.parent == ThingCategoryDefOf.Root)
                {
                    rootCategories.Add(node);
                }
            }

            // Sort root categories by label
            rootCategories.Sort((a, b) => string.Compare(a.CategoryDef.label, b.CategoryDef.label));

            // Sort subcategories and items within each node
            SortCategoryNode(rootCategories);

            return rootCategories;
        }

        /// <summary>
        /// Recursively sorts subcategories and items within a category tree
        /// </summary>
        private static void SortCategoryNode(List<CategoryNode> nodes)
        {
            foreach (CategoryNode node in nodes)
            {
                // Sort subcategories alphabetically
                if (node.SubCategories.Count > 0)
                {
                    node.SubCategories.Sort((a, b) => string.Compare(a.CategoryDef.label, b.CategoryDef.label));
                    SortCategoryNode(node.SubCategories); // Recurse
                }

                // Sort items alphabetically
                if (node.Items.Count > 0)
                {
                    node.Items.Sort((a, b) => string.Compare(a.Def.label, b.Def.label));
                }
            }
        }

        /// <summary>
        /// Gets the first storage location for a given ThingDef
        /// </summary>
        public static IntVec3? FindFirstStorageLocation(ThingDef thingDef)
        {
            Map map = Find.CurrentMap;
            if (map == null) return null;

            // Check stockpiles
            if (map.zoneManager?.AllZones != null)
            {
                foreach (Zone zone in map.zoneManager.AllZones)
                {
                    if (zone is Zone_Stockpile stockpile)
                    {
                        SlotGroup slotGroup = stockpile.GetSlotGroup();
                        if (slotGroup?.HeldThings != null)
                        {
                            foreach (Thing item in slotGroup.HeldThings)
                            {
                                if (item.def == thingDef)
                                {
                                    return item.Position;
                                }
                            }
                        }
                    }
                }
            }

            // Check storage buildings
            if (map.listerBuildings != null)
            {
                foreach (Building_Storage storage in map.listerBuildings.AllBuildingsColonistOfClass<Building_Storage>())
                {
                    SlotGroup slotGroup = storage.GetSlotGroup();
                    if (slotGroup?.HeldThings != null)
                    {
                        foreach (Thing item in slotGroup.HeldThings)
                        {
                            if (item.def == thingDef)
                            {
                                return item.Position;
                            }
                        }
                    }
                }
            }

            return null;
        }
    }
}
