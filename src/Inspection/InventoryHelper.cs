using System;
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
        /// Represents a single physical stack of items at a specific location.
        /// </summary>
        public class InventoryStack
        {
            public Thing Thing { get; set; }
            public int Quantity { get; set; }
            public Pawn CarrierPawn { get; set; }
            public bool IsForbidden { get; set; }
            public bool IsTainted { get; set; }
            public string LocationLabel { get; set; }
            public bool IsMinifiedThing { get; set; }

            public bool IsCarried => CarrierPawn != null;

            /// <summary>
            /// Position for jump-to functionality. Carrier position for carried items, thing position for stored items.
            /// </summary>
            public IntVec3 Position => IsCarried && CarrierPawn != null && !CarrierPawn.Destroyed
                ? CarrierPawn.Position
                : Thing.Position;
        }

        /// <summary>
        /// Represents an aggregated inventory item with its total quantity and individual stacks.
        /// Items are grouped by ThingDef + Stuff (material) + Quality to avoid incorrectly
        /// combining items like "steel knife (excellent)" with "plasteel knife (poor)".
        /// </summary>
        public class InventoryItem
        {
            public ThingDef Def { get; set; }
            public ThingDef Stuff { get; set; }
            public QualityCategory? Quality { get; set; }
            public List<InventoryStack> Stacks { get; set; }
            public bool IsMinifiedThing { get; set; }

            public int TotalQuantity => Stacks.Sum(s => s.Quantity);
            public int CarriedCount => Stacks.Where(s => s.IsCarried).Sum(s => s.Quantity);
            public bool HasCarriedStacks => Stacks.Any(s => s.IsCarried);

            public InventoryItem(ThingDef def, ThingDef stuff = null, QualityCategory? quality = null)
            {
                Def = def;
                Stuff = stuff;
                Quality = quality;
                Stacks = new List<InventoryStack>();
                IsMinifiedThing = false;
            }

            /// <summary>
            /// Gets the base item name with material prefix and quality suffix, without quantity.
            /// Used by both GetDisplayLabel and external code for stack labels.
            /// </summary>
            public string GetItemName()
            {
                string itemName;
                if (Stuff != null)
                {
                    itemName = $"{Stuff.LabelAsStuff} {Def.label}";
                }
                else
                {
                    itemName = Def.label;
                }

                if (!string.IsNullOrEmpty(itemName))
                {
                    itemName = char.ToUpper(itemName[0]) + itemName.Substring(1);
                }

                if (Quality.HasValue)
                {
                    itemName += $" ({Quality.Value})";
                }

                return itemName;
            }

            public string GetDisplayLabel()
            {
                string name = GetItemName();
                int carried = CarriedCount;
                if (carried > 0)
                {
                    return $"{name} x{TotalQuantity} ({carried} carried by colonists)";
                }
                return $"{name} x{TotalQuantity}";
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
                if (CategoryDef == null)
                    return "RimWorldAccess.Inspection.Inventory.UncategorizedFallback".Translate();
                if (Items.Count > 0)
                {
                    return "RimWorldAccess.Inspection.Inventory.CategoryWithCount".Translate(
                        CategoryDef.LabelCap, Items.Count);
                }
                return CategoryDef.LabelCap;
            }
        }

        /// <summary>
        /// Determines a human-readable location label for a stored thing.
        /// </summary>
        public static string GetLocationLabel(Thing thing, Map map)
        {
            if (thing == null || map == null || !thing.Spawned) return "";

            SlotGroup slotGroup = map.haulDestinationManager.SlotGroupAt(thing.Position);
            if (slotGroup == null) return $"at ({thing.Position.x}, {thing.Position.z})";

            if (slotGroup.parent is Zone_Stockpile stockpile)
            {
                return $"in {stockpile.label}";
            }

            if (slotGroup.parent is Building_Storage building)
            {
                if (building is IStorageGroupMember member && member.Group != null)
                {
                    return $"at {member.Group.RenamableLabel}";
                }
                return $"on {building.Label}";
            }

            return $"at ({thing.Position.x}, {thing.Position.z})";
        }

        /// <summary>
        /// Checks if a thing is forbidden via its CompForbiddable.
        /// </summary>
        public static bool GetIsForbidden(Thing thing)
        {
            if (thing is ThingWithComps twc && twc.compForbiddable != null)
            {
                return twc.compForbiddable.Forbidden;
            }
            return false;
        }

        /// <summary>
        /// Checks if apparel is tainted (worn by a corpse).
        /// </summary>
        public static bool GetIsTainted(Thing thing)
        {
            if (thing is Apparel apparel)
            {
                return apparel.WornByCorpse;
            }
            return false;
        }

        /// <summary>
        /// Aggregates all items (stored + carried) by ThingDef + Stuff + Quality into unified InventoryItems.
        /// Each physical stack becomes an InventoryStack with location info.
        /// </summary>
        public static List<InventoryItem> AggregateAllItems(List<Thing> storedItems, Dictionary<Thing, Pawn> carriedItems)
        {
            var aggregated = new Dictionary<(ThingDef, ThingDef, QualityCategory?), InventoryItem>();
            Map map = Find.CurrentMap;

            foreach (Thing item in storedItems)
            {
                if (item?.def == null) continue;

                Thing thingToCheck = item;
                bool isMinified = item is MinifiedThing;
                if (isMinified)
                {
                    Thing innerThing = item.GetInnerIfMinified();
                    if (innerThing != null) thingToCheck = innerThing;
                }

                ThingDef defToUse = thingToCheck.def;
                ThingDef stuffToUse = thingToCheck.Stuff;
                QualityCategory? qualityToUse = null;
                var qualityComp = thingToCheck.TryGetComp<CompQuality>();
                if (qualityComp != null) qualityToUse = qualityComp.Quality;

                var key = (defToUse, stuffToUse, qualityToUse);
                if (!aggregated.ContainsKey(key))
                {
                    aggregated[key] = new InventoryItem(defToUse, stuffToUse, qualityToUse)
                    {
                        IsMinifiedThing = isMinified
                    };
                }

                aggregated[key].Stacks.Add(new InventoryStack
                {
                    Thing = item,
                    Quantity = item.stackCount,
                    CarrierPawn = null,
                    IsForbidden = GetIsForbidden(item),
                    IsTainted = GetIsTainted(item),
                    LocationLabel = GetLocationLabel(item, map),
                    IsMinifiedThing = isMinified
                });
            }

            foreach (var kvp in carriedItems)
            {
                Thing item = kvp.Key;
                Pawn carrier = kvp.Value;
                if (item?.def == null || carrier == null) continue;

                Thing thingToCheck = item;
                bool isMinified = item is MinifiedThing;
                if (isMinified)
                {
                    Thing innerThing = item.GetInnerIfMinified();
                    if (innerThing != null) thingToCheck = innerThing;
                }

                ThingDef defToUse = thingToCheck.def;
                ThingDef stuffToUse = thingToCheck.Stuff;
                QualityCategory? qualityToUse = null;
                var qualityComp = thingToCheck.TryGetComp<CompQuality>();
                if (qualityComp != null) qualityToUse = qualityComp.Quality;

                var key = (defToUse, stuffToUse, qualityToUse);
                if (!aggregated.ContainsKey(key))
                {
                    aggregated[key] = new InventoryItem(defToUse, stuffToUse, qualityToUse)
                    {
                        IsMinifiedThing = isMinified
                    };
                }

                aggregated[key].Stacks.Add(new InventoryStack
                {
                    Thing = item,
                    Quantity = item.stackCount,
                    CarrierPawn = carrier,
                    IsForbidden = false,
                    IsTainted = GetIsTainted(item),
                    LocationLabel = $"carried by {carrier.LabelShort}",
                    IsMinifiedThing = isMinified
                });
            }

            return aggregated.Values.ToList();
        }

        /// <summary>
        /// Collects all items from stockpiles and storage buildings across the colony.
        /// Uses a HashSet to prevent duplicate counting.
        /// </summary>
        public static List<Thing> GetAllStoredItems()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Log.Warning("InventoryHelper: Cannot get stored items - no current map");
                return new List<Thing>();
            }

            // Use HashSet to prevent duplicates (items on shelves in stockpiles could be counted twice)
            HashSet<Thing> uniqueItems = new HashSet<Thing>();

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
                                // Skip minified things that already have an install blueprint
                                if (item is MinifiedThing && InstallBlueprintUtility.ExistingBlueprintFor(item) != null)
                                    continue;
                                uniqueItems.Add(item);
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
                            // Skip minified things that already have an install blueprint
                            if (item is MinifiedThing && InstallBlueprintUtility.ExistingBlueprintFor(item) != null)
                                continue;
                            uniqueItems.Add(item);
                        }
                    }
                }
            }

            return uniqueItems.ToList();
        }

        /// <summary>
        /// Collects all items carried by owned pawns (colonists and animals) on the current map.
        /// Returns a dictionary mapping each Thing to its carrier Pawn.
        /// </summary>
        public static Dictionary<Thing, Pawn> GetAllPawnCarriedItems()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Log.Warning("InventoryHelper: Cannot get pawn-carried items - no current map");
                return new Dictionary<Thing, Pawn>();
            }

            Dictionary<Thing, Pawn> carriedItems = new Dictionary<Thing, Pawn>();

            // Get items from player faction pawns (colonists and animals)
            foreach (Pawn pawn in map.mapPawns.PawnsInFaction(Faction.OfPlayer))
            {
                if (pawn.inventory?.innerContainer == null) continue;

                foreach (Thing item in pawn.inventory.innerContainer)
                {
                    if (item != null)
                    {
                        // Skip minified things that already have an install blueprint
                        if (item is MinifiedThing && InstallBlueprintUtility.ExistingBlueprintFor(item) != null)
                            continue;
                        carriedItems[item] = pawn;
                    }
                }
            }

            return carriedItems;
        }

        /// <summary>
        /// Aggregates items by ThingDef + Stuff (material) + Quality, populating Stacks.
        /// Used by CaravanInspectState for caravan inventory display.
        /// For colony inventory, use AggregateAllItems instead.
        /// </summary>
        public static List<InventoryItem> AggregateStacks(List<Thing> items)
        {
            var aggregated = new Dictionary<(ThingDef, ThingDef, QualityCategory?), InventoryItem>();
            Map map = Find.CurrentMap;

            foreach (Thing item in items)
            {
                if (item?.def == null) continue;

                Thing thingToCheck = item;
                bool isMinified = item is MinifiedThing;
                if (isMinified)
                {
                    Thing innerThing = item.GetInnerIfMinified();
                    if (innerThing != null) thingToCheck = innerThing;
                }

                ThingDef defToUse = thingToCheck.def;
                ThingDef stuffToUse = thingToCheck.Stuff;
                QualityCategory? qualityToUse = null;
                var qualityComp = thingToCheck.TryGetComp<CompQuality>();
                if (qualityComp != null) qualityToUse = qualityComp.Quality;

                var key = (defToUse, stuffToUse, qualityToUse);
                if (!aggregated.ContainsKey(key))
                {
                    aggregated[key] = new InventoryItem(defToUse, stuffToUse, qualityToUse)
                    {
                        IsMinifiedThing = isMinified
                    };
                }

                aggregated[key].Stacks.Add(new InventoryStack
                {
                    Thing = item,
                    Quantity = item.stackCount,
                    CarrierPawn = null,
                    IsForbidden = GetIsForbidden(item),
                    IsTainted = GetIsTainted(item),
                    LocationLabel = map != null ? GetLocationLabel(item, map) : "",
                    IsMinifiedThing = isMinified
                });
            }

            return aggregated.Values.ToList();
        }

        /// <summary>
        /// Groups inventory items by their categories, building a hierarchical tree
        /// </summary>
        public static List<CategoryNode> BuildCategoryTree(List<InventoryItem> items)
        {
            Dictionary<ThingCategoryDef, CategoryNode> categoryNodes = new Dictionary<ThingCategoryDef, CategoryNode>();

            void AddItemToCategories(InventoryItem item)
            {
                ThingDef thingDef = item.Def;
                if (thingDef.thingCategories == null || thingDef.thingCategories.Count == 0)
                {
                    return;
                }

                foreach (ThingCategoryDef category in thingDef.thingCategories)
                {
                    if (!categoryNodes.ContainsKey(category))
                    {
                        categoryNodes[category] = new CategoryNode(category);
                    }

                    categoryNodes[category].Items.Add(item);
                    categoryNodes[category].TotalItemCount++;

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

            foreach (InventoryItem item in items)
            {
                AddItemToCategories(item);
            }

            List<InventoryItem> uncategorizedItems = new List<InventoryItem>();
            foreach (InventoryItem item in items)
            {
                if (item.Def.thingCategories == null || item.Def.thingCategories.Count == 0)
                {
                    uncategorizedItems.Add(item);
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

            // Add uncategorized node if there are any uncategorized items
            if (uncategorizedItems.Count > 0)
            {
                var uncategorizedNode = new CategoryNode(null); // null signals uncategorized
                foreach (var item in uncategorizedItems)
                {
                    uncategorizedNode.Items.Add(item);
                }
                uncategorizedNode.TotalItemCount = uncategorizedItems.Count;
                uncategorizedNode.Items.Sort((a, b) => b.TotalQuantity.CompareTo(a.TotalQuantity));
                rootCategories.Add(uncategorizedNode);
            }

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

                // Sort items by quantity descending (largest stacks first)
                if (node.Items.Count > 0)
                {
                    node.Items.Sort((a, b) => b.TotalQuantity.CompareTo(a.TotalQuantity));
                }
            }
        }

    }
}
