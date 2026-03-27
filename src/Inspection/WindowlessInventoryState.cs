using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages the windowless colony-wide inventory menu (activated with 'I' key)
    /// Displays all stored items organized by category with hierarchical navigation.
    /// Uses TreeNavigationHelper for all standard navigation logic.
    /// </summary>
    public static class WindowlessInventoryState
    {
        /// <summary>
        /// Node type enum preserved from the original TreeNode implementation.
        /// Stored in InspectionTreeItem.Data via InventoryNodeData.
        /// </summary>
        public enum NodeType
        {
            Category,       // A ThingCategoryDef
            DefGroup,       // All items of the same ThingDef (2+ variants) or same meal tier
            MaterialGroup,  // All items of the same ThingDef + Stuff (within a DefGroup)
            ItemGroup,      // An aggregated item type (single variant: def+stuff+quality)
            Stack           // An individual physical stack at a specific location
        }

        /// <summary>
        /// Domain-specific data stored in InspectionTreeItem.Data for inventory nodes.
        /// </summary>
        public class InventoryNodeData
        {
            public NodeType Type { get; set; }
            public InventoryHelper.CategoryNode CategoryData { get; set; }
            public InventoryHelper.InventoryItem ItemData { get; set; }
            public InventoryHelper.InventoryStack StackData { get; set; }
            public ThingDef DefGroupDef { get; set; }
            public ThingDef DefGroupStuff { get; set; }
            public List<InventoryHelper.InventoryItem> DefGroupItems { get; set; }
        }

        private static TreeNavigationHelper treeNav = new TreeNavigationHelper("Inventory");
        private static bool isActive = false;

        public static bool IsActive => isActive;
        public static TypeaheadSearchHelper Typeahead => treeNav.Typeahead;

        static WindowlessInventoryState()
        {
            treeNav.FormatItemAnnouncement = FormatItemAnnouncement;
            treeNav.FormatSearchAnnouncement = FormatSearchAnnouncement;
            treeNav.OnActivate = HandleActivate;
            treeNav.OnDelete = HandleDeleteNode;
            treeNav.OnInfo = HandleInfoNode;
            treeNav.TrackLastChild = true;
        }

        /// <summary>
        /// Opens the inventory menu and collects all colony storage data
        /// </summary>
        public static void Open()
        {
            if (Find.CurrentMap == null)
            {
                Log.Warning("WindowlessInventoryState: Cannot open - no current map");
                return;
            }

            isActive = true;

            // Collect all items
            List<Thing> allStoredItems = InventoryHelper.GetAllStoredItems();
            Dictionary<Thing, Pawn> pawnCarriedThings = InventoryHelper.GetAllPawnCarriedItems();

            if (allStoredItems.Count == 0 && pawnCarriedThings.Count == 0)
            {
                TolkHelper.Speak("Inventory menu opened. No items in colony storage or carried by pawns.");
                var emptyRoot = new InspectionTreeItem
                {
                    Label = "Root",
                    IndentLevel = -1,
                    IsExpanded = true,
                    IsExpandable = false
                };
                treeNav.Initialize(emptyRoot);
                SoundDefOf.TabOpen.PlayOneShotOnCamera();
                return;
            }

            // Unified aggregation (merges stored + carried by ThingDef + Stuff + Quality)
            List<InventoryHelper.InventoryItem> allItems = InventoryHelper.AggregateAllItems(allStoredItems, pawnCarriedThings);

            // Build category tree
            List<InventoryHelper.CategoryNode> categoryTree = InventoryHelper.BuildCategoryTree(allItems);

            // Build InspectionTreeItem tree
            var root = BuildTree(categoryTree);
            treeNav.Initialize(root);

            // Count distinct items (DefGroups and single-variant ItemGroups, not every variant)
            int distinctItemCount = CountDistinctItems(root);

            // Announcement
            string announcement = $"Colony inventory opened. {distinctItemCount} item types. {root.Children.Count} categories.";
            TolkHelper.Speak(announcement);
            SoundDefOf.TabOpen.PlayOneShotOnCamera();

            // Announce first selection
            if (treeNav.Count > 0)
            {
                treeNav.ReannounceCurrentItem();
            }
        }

        /// <summary>
        /// Closes the inventory menu
        /// </summary>
        public static void Close()
        {
            isActive = false;
            treeNav.Reset();

            TolkHelper.Speak("Inventory menu closed.");
            SoundDefOf.TabClose.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Gets whether typeahead search is active.
        /// </summary>
        public static bool HasActiveSearch => treeNav.HasActiveSearch;

        /// <summary>
        /// Handles keyboard input for the inventory menu.
        /// Returns true if the input was handled.
        /// </summary>
        public static bool HandleInput(Event ev)
        {
            if (!isActive) return false;

            if (ev.type != EventType.KeyDown) return false;

            KeyCode key = ev.keyCode;

            // Right bracket - open context menu for current stack or item group
            if (key == KeyCode.RightBracket)
            {
                ev.Use();
                OpenContextMenu();
                return true;
            }

            // Alt+J - Jump to item/pawn based on context (use KeyboardHelper.IsAltHeld for AZERTY)
            if (KeyboardHelper.IsAltHeld && key == KeyCode.J)
            {
                ev.Use();
                HandleJumpKey();
                return true;
            }

            // * key — expand all categories recursively (inventory-specific: expands
            // every Category node in the entire tree, NOT just siblings at current level)
            bool isStar = key == KeyCode.KeypadMultiply || (ev.shift && key == KeyCode.Alpha8);
            if (isStar)
            {
                ExpandAllCategories();
                ev.Use();
                return true;
            }

            // Delegate to TreeNavigationHelper for standard tree navigation
            bool handled = treeNav.HandleInput(ev);
            if (handled)
            {
                ev.Use();
                return true;
            }

            // TreeNavigationHelper returns false for Escape with no active search
            if (key == KeyCode.Escape)
            {
                ev.Use();
                Close();
                return true;
            }

            // Block all other keys to prevent pass-through
            return false;
        }

        #region Tree Building

        /// <summary>
        /// Builds the root InspectionTreeItem from the category tree.
        /// </summary>
        private static InspectionTreeItem BuildTree(List<InventoryHelper.CategoryNode> categoryTree)
        {
            var root = new InspectionTreeItem
            {
                Label = "Root",
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false
            };

            BuildCategoryChildren(categoryTree, root, 0);
            return root;
        }

        /// <summary>
        /// Converts InventoryHelper.CategoryNode tree to InspectionTreeItem tree.
        /// Creates hierarchy with DefGroup/MaterialGroup for multi-variant items,
        /// and flat ItemGroup for single-variant items.
        /// </summary>
        private static void BuildCategoryChildren(List<InventoryHelper.CategoryNode> categoryNodes, InspectionTreeItem parent, int depth)
        {
            foreach (InventoryHelper.CategoryNode categoryNode in categoryNodes)
            {
                var catItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Category,
                    Label = categoryNode.GetDisplayLabel(),
                    IndentLevel = depth,
                    Parent = parent,
                    IsExpandable = (categoryNode.SubCategories.Count > 0 || categoryNode.Items.Count > 0),
                    Data = new InventoryNodeData
                    {
                        Type = NodeType.Category,
                        CategoryData = categoryNode
                    }
                };

                // Build children (subcategories)
                if (categoryNode.SubCategories.Count > 0)
                {
                    BuildCategoryChildren(categoryNode.SubCategories, catItem, depth + 1);
                }

                // Build item children with DefGroup/MaterialGroup/ItemGroup hierarchy
                BuildItemChildren(categoryNode.Items, catItem, depth);

                parent.Children.Add(catItem);
            }
        }

        /// <summary>
        /// Builds item children for a category node, creating DefGroups for multi-variant
        /// items and flat ItemGroups for single-variant items.
        /// </summary>
        private static void BuildItemChildren(List<InventoryHelper.InventoryItem> items, InspectionTreeItem catItem, int depth)
        {
            if (items.Count == 0) return;

            // Step 1: Identify meal-tier items and group by preferability
            var mealGroupedDefs = new HashSet<ThingDef>();
            var mealGroups = items
                .Where(i => HasMealTierPreferability(i.Def))
                .GroupBy(i => i.Def.ingestible.preferability)
                .Where(g => g.Count() >= 2)
                .ToList();

            foreach (var mg in mealGroups)
            {
                foreach (var item in mg)
                    mealGroupedDefs.Add(item.Def);
            }

            // Step 2: Group remaining items by ThingDef
            var remainingItems = items.Where(i => !mealGroupedDefs.Contains(i.Def)).ToList();
            var defGroups = remainingItems
                .GroupBy(i => i.Def)
                .ToList();

            // Step 3: Build all child nodes into a single list for sorting
            var childNodes = new List<(InspectionTreeItem node, int totalQty)>();

            // Build meal DefGroup nodes
            foreach (var mealGroup in mealGroups)
            {
                var mealItems = mealGroup.OrderByDescending(i => i.TotalQuantity).ToList();
                int totalQty = mealItems.Sum(i => i.TotalQuantity);

                var defGroupItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Category,
                    Label = BuildMealGroupLabel(mealItems),
                    IndentLevel = depth + 1,
                    Parent = catItem,
                    IsExpandable = true,
                    Data = new InventoryNodeData
                    {
                        Type = NodeType.DefGroup,
                        DefGroupDef = mealItems.OrderBy(i => i.Def.label.Length).First().Def,
                        DefGroupItems = mealItems
                    },
                    LinkedDef = mealItems.OrderBy(i => i.Def.label.Length).First().Def
                };

                // Meal DefGroup children are ItemGroups (each is a different ThingDef)
                foreach (var item in mealItems)
                {
                    var itemGroupItem = BuildItemGroupNode(item, defGroupItem, depth + 2);
                    defGroupItem.Children.Add(itemGroupItem);
                }

                childNodes.Add((defGroupItem, totalQty));
            }

            // Build material/quality DefGroups and single-variant ItemGroups
            foreach (var defGroup in defGroups)
            {
                var groupItems = defGroup.ToList();

                if (groupItems.Count == 1)
                {
                    // Single variant: regular ItemGroup
                    var itemGroupItem = BuildItemGroupNode(groupItems[0], catItem, depth + 1);
                    childNodes.Add((itemGroupItem, groupItems[0].TotalQuantity));
                }
                else
                {
                    // Multiple variants: create DefGroup
                    ThingDef def = defGroup.Key;
                    int totalQty = groupItems.Sum(i => i.TotalQuantity);
                    bool hasStuffVariations = groupItems.Any(i => i.Stuff != null);

                    var defGroupItem = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Category,
                        Label = BuildDefGroupLabel(def, groupItems),
                        IndentLevel = depth + 1,
                        Parent = catItem,
                        IsExpandable = true,
                        Data = new InventoryNodeData
                        {
                            Type = NodeType.DefGroup,
                            DefGroupDef = def,
                            DefGroupItems = groupItems
                        },
                        LinkedDef = def
                    };

                    if (hasStuffVariations)
                    {
                        // Sub-group by Stuff -> MaterialGroup -> Stacks
                        var materialGroups = groupItems
                            .GroupBy(i => i.Stuff)
                            .Select(g => new { Stuff = g.Key, Items = g.ToList() })
                            .OrderByDescending(g => g.Items.Sum(i => i.TotalQuantity))
                            .ToList();

                        foreach (var matGroup in materialGroups)
                        {
                            int materialTotal = matGroup.Items.Sum(i => i.TotalQuantity);

                            var matGroupItem = new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.SubCategory,
                                Label = BuildMaterialGroupLabel(def, matGroup.Stuff, materialTotal, matGroup.Items),
                                IndentLevel = depth + 2,
                                Parent = defGroupItem,
                                IsExpandable = true,
                                Data = new InventoryNodeData
                                {
                                    Type = NodeType.MaterialGroup,
                                    DefGroupDef = def,
                                    DefGroupStuff = matGroup.Stuff
                                },
                                LinkedDef = def
                            };

                            // Collect all stacks, sort by quality desc then quantity desc
                            var sortedStacks = matGroup.Items
                                .OrderByDescending(i => i.Quality.HasValue ? (int)i.Quality.Value : -1)
                                .SelectMany(i => i.Stacks.Select(s => new { Item = i, Stack = s }))
                                .OrderByDescending(x => x.Item.Quality.HasValue ? (int)x.Item.Quality.Value : -1)
                                .ThenByDescending(x => x.Stack.Quantity)
                                .ToList();

                            foreach (var pair in sortedStacks)
                            {
                                var stackItem = BuildStackNode(pair.Item, pair.Stack, matGroupItem, depth + 3);
                                matGroupItem.Children.Add(stackItem);
                            }

                            defGroupItem.Children.Add(matGroupItem);
                        }
                    }
                    else
                    {
                        // No stuff variations (quality-only): Stacks directly under DefGroup
                        var sortedStacks = groupItems
                            .OrderByDescending(i => i.Quality.HasValue ? (int)i.Quality.Value : -1)
                            .SelectMany(i => i.Stacks.Select(s => new { Item = i, Stack = s }))
                            .OrderByDescending(x => x.Item.Quality.HasValue ? (int)x.Item.Quality.Value : -1)
                            .ThenByDescending(x => x.Stack.Quantity)
                            .ToList();

                        foreach (var pair in sortedStacks)
                        {
                            var stackItem = BuildStackNode(pair.Item, pair.Stack, defGroupItem, depth + 2);
                            defGroupItem.Children.Add(stackItem);
                        }
                    }

                    childNodes.Add((defGroupItem, totalQty));
                }
            }

            // Sort all children by total quantity descending and add to category
            foreach (var (node, _) in childNodes.OrderByDescending(c => c.totalQty))
            {
                catItem.Children.Add(node);
            }
        }

        /// <summary>
        /// Builds a standard ItemGroup node with Stack children.
        /// Used for single-variant items and for meal variants within a meal DefGroup.
        /// </summary>
        private static InspectionTreeItem BuildItemGroupNode(InventoryHelper.InventoryItem item, InspectionTreeItem parent, int depth)
        {
            var itemGroupItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = item.GetDisplayLabel(),
                IndentLevel = depth,
                Parent = parent,
                IsExpandable = true,
                Data = new InventoryNodeData
                {
                    Type = NodeType.ItemGroup,
                    ItemData = item
                },
                LinkedDef = item.Def
            };

            foreach (InventoryHelper.InventoryStack stack in item.Stacks)
            {
                var stackItem = BuildStackNode(item, stack, itemGroupItem, depth + 1);
                itemGroupItem.Children.Add(stackItem);
            }

            return itemGroupItem;
        }

        /// <summary>
        /// Builds a Stack leaf node.
        /// </summary>
        private static InspectionTreeItem BuildStackNode(InventoryHelper.InventoryItem item, InventoryHelper.InventoryStack stack, InspectionTreeItem parent, int depth)
        {
            return new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = BuildStackLabel(item, stack),
                IndentLevel = depth,
                Parent = parent,
                IsExpandable = false,
                Data = new InventoryNodeData
                {
                    Type = NodeType.Stack,
                    StackData = stack,
                    ItemData = item
                },
                LinkedDef = item.Def
            };
        }

        #endregion

        #region Label Building

        /// <summary>
        /// Checks if a FoodPreferability value represents a meal tier.
        /// </summary>
        private static bool IsMealTier(FoodPreferability pref)
        {
            return pref == FoodPreferability.MealAwful
                || pref == FoodPreferability.MealTerrible
                || pref == FoodPreferability.MealSimple
                || pref == FoodPreferability.MealFine
                || pref == FoodPreferability.MealLavish;
        }

        /// <summary>
        /// Checks if a ThingDef has a meal-tier food preferability.
        /// </summary>
        private static bool HasMealTierPreferability(ThingDef def)
        {
            return def.ingestible != null && IsMealTier(def.ingestible.preferability);
        }

        /// <summary>
        /// Builds the display label for a DefGroup node (material/quality grouping).
        /// Format: "{def.LabelCap} x{total} ({N} carried), {material summary}"
        /// </summary>
        private static string BuildDefGroupLabel(ThingDef def, List<InventoryHelper.InventoryItem> items)
        {
            int totalCount = items.Sum(i => i.TotalQuantity);
            int carriedCount = items.Sum(i => i.CarriedCount);

            string label = $"{def.LabelCap} x{totalCount}";
            if (carriedCount > 0)
            {
                label += $" ({carriedCount} carried by colonists)";
            }

            // Add material summary if items have stuff variations
            var materialCounts = items
                .Where(i => i.Stuff != null)
                .GroupBy(i => i.Stuff)
                .Select(g => new { Stuff = g.Key, Count = g.Sum(i => i.TotalQuantity) })
                .OrderByDescending(m => m.Count)
                .ToList();

            if (materialCounts.Count > 0)
            {
                // Show top materials: at most 3, or until 95% coverage, whichever is fewer
                const int maxShown = 3;
                int threshold = (int)Math.Ceiling(totalCount * 0.95);
                int accumulated = 0;
                var shownMaterials = new List<string>();
                var truncatedMaterials = new List<(string name, int count)>();

                foreach (var mat in materialCounts)
                {
                    if (shownMaterials.Count < maxShown && accumulated < threshold)
                    {
                        shownMaterials.Add($"{mat.Count} {mat.Stuff.LabelAsStuff}");
                        accumulated += mat.Count;
                    }
                    else
                    {
                        truncatedMaterials.Add((mat.Stuff.LabelAsStuff, mat.Count));
                    }
                }

                if (shownMaterials.Count > 0)
                {
                    label += ", " + string.Join(", ", shownMaterials);
                    if (truncatedMaterials.Count == 1)
                    {
                        label += $", and {truncatedMaterials[0].count} {truncatedMaterials[0].name}";
                    }
                    else if (truncatedMaterials.Count > 1)
                    {
                        label += $", and {truncatedMaterials.Count} other materials";
                    }
                }
            }

            return label;
        }

        /// <summary>
        /// Builds the display label for a meal tier DefGroup node.
        /// Uses the shortest member label as the group name (the base variant).
        /// Format: "{baseMealLabel} x{total} ({N} carried by colonists)"
        /// </summary>
        private static string BuildMealGroupLabel(List<InventoryHelper.InventoryItem> items)
        {
            int totalCount = items.Sum(i => i.TotalQuantity);
            int carriedCount = items.Sum(i => i.CarriedCount);

            // Use the member with the shortest label as the base name
            string baseName = items
                .OrderBy(i => i.Def.label.Length)
                .First().Def.LabelCap;

            string label = $"{baseName} x{totalCount}";
            if (carriedCount > 0)
            {
                label += $" ({carriedCount} carried by colonists)";
            }

            return label;
        }

        /// <summary>
        /// Builds the display label for a MaterialGroup node.
        /// Format: "{stuff} {def} x{total} ({N} carried by colonists)"
        /// </summary>
        private static string BuildMaterialGroupLabel(ThingDef def, ThingDef stuff, int totalQty, List<InventoryHelper.InventoryItem> items)
        {
            string name;
            if (stuff != null)
            {
                name = $"{stuff.LabelAsStuff} {def.label}";
            }
            else
            {
                name = def.label;
            }

            if (!string.IsNullOrEmpty(name))
            {
                name = char.ToUpper(name[0]) + name.Substring(1);
            }

            int carriedCount = items.Sum(i => i.CarriedCount);
            if (carriedCount > 0)
            {
                return $"{name} x{totalQty} ({carriedCount} carried by colonists)";
            }
            return $"{name} x{totalQty}";
        }

        /// <summary>
        /// Builds the display label for an individual stack node.
        /// Format: "{name} x{count}, {location}" with optional flags.
        /// </summary>
        private static string BuildStackLabel(InventoryHelper.InventoryItem item, InventoryHelper.InventoryStack stack)
        {
            string name = item.GetItemName();
            string suffix = "";
            if (stack.IsTainted) suffix += " (tainted)";
            if (stack.IsForbidden) suffix += " (forbidden)";
            return $"{name} x{stack.Quantity}, {stack.LocationLabel}{suffix}";
        }

        #endregion

        #region Announcements

        /// <summary>
        /// Custom announcement formatter for navigation.
        /// Format: "Label expanded. 1 of 5. level 2"
        /// </summary>
        private static string FormatItemAnnouncement(InspectionTreeItem item)
        {
            // Get state info (only for expandable nodes)
            string stateInfo = "";
            if (item.IsExpandable)
            {
                stateInfo = item.IsExpanded ? " expanded" : " collapsed";
            }

            // Get sibling position
            var (position, total) = treeNav.GetSiblingPosition(item);
            string positionPart = MenuHelper.FormatPosition(position - 1, total);
            string positionSection = string.IsNullOrEmpty(positionPart) ? "." : $". {positionPart}.";

            // Level suffix
            string levelSuffix = MenuHelper.GetLevelSuffix("Inventory", item.IndentLevel);

            return $"{item.Label}{stateInfo}{positionSection}{levelSuffix}".Trim();
        }

        /// <summary>
        /// Custom announcement formatter for typeahead search.
        /// </summary>
        private static string FormatSearchAnnouncement(InspectionTreeItem item, TypeaheadSearchHelper typeahead)
        {
            // Get state info (only for expandable nodes)
            string stateInfo = "";
            if (item.IsExpandable)
            {
                stateInfo = item.IsExpanded ? " expanded" : " collapsed";
            }

            // Get sibling position
            var (position, total) = treeNav.GetSiblingPosition(item);
            string positionInfo = $"{position} of {total}";

            // Build announcement with search context
            string announcement;
            if (string.IsNullOrEmpty(stateInfo))
            {
                announcement = $"{item.Label}";
            }
            else
            {
                announcement = $"{item.Label}{stateInfo}";
            }

            if (typeahead.HasActiveSearch)
            {
                announcement += $", {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'";
            }
            else
            {
                announcement += $". {positionInfo}";
            }

            return announcement.Trim();
        }

        #endregion

        #region Recursive Category Expand

        /// <summary>
        /// Expands all category nodes in the entire tree recursively.
        /// Does NOT expand ItemGroup, DefGroup, MaterialGroup, or Stack nodes.
        /// This is inventory-specific: the standard TreeNavigationHelper * only expands siblings.
        /// </summary>
        private static void ExpandAllCategories()
        {
            if (treeNav.RootItem == null || treeNav.RootItem.Children.Count == 0)
            {
                TolkHelper.Speak("No categories to expand.");
                return;
            }

            int expandedCount = ExpandCategoriesRecursively(treeNav.RootItem.Children);

            if (expandedCount > 0)
            {
                treeNav.RebuildVisibleList();
                EmbeddedAudioHelper.PlaySoundDefWithReverb(SoundDefOf.FloatMenu_Open);
                if (expandedCount == 1)
                    TolkHelper.Speak("Expanded 1 category");
                else
                    TolkHelper.Speak($"Expanded {expandedCount} categories");
            }
            else
            {
                TolkHelper.Speak("All categories already expanded");
            }
        }

        /// <summary>
        /// Recursively expands all category nodes but not ItemGroup/DefGroup/Stack nodes.
        /// Only recurses into Category children since other node types can't contain categories.
        /// </summary>
        private static int ExpandCategoriesRecursively(IEnumerable<InspectionTreeItem> nodes)
        {
            int expandedCount = 0;

            foreach (var node in nodes)
            {
                var data = node.Data as InventoryNodeData;
                if (data != null && data.Type == NodeType.Category && node.IsExpandable && !node.IsExpanded)
                {
                    node.IsExpanded = true;
                    expandedCount++;
                }

                // Only recurse into Category children
                if (data != null && data.Type == NodeType.Category && node.Children.Count > 0)
                {
                    expandedCount += ExpandCategoriesRecursively(node.Children);
                }
            }

            return expandedCount;
        }

        #endregion

        #region Activation and Domain Actions

        /// <summary>
        /// OnActivate callback for TreeNavigationHelper.
        /// Returns true if handled (stack context menu), false to fall back to default toggle.
        /// </summary>
        private static bool HandleActivate(InspectionTreeItem item)
        {
            var data = item.Data as InventoryNodeData;
            if (data == null) return false;

            if (data.Type == NodeType.Stack)
            {
                // Stack nodes: open context menu on Enter
                OpenContextMenu();
                return true;
            }

            // For expandable nodes (Category, DefGroup, MaterialGroup, ItemGroup),
            // return false to let TreeNavigationHelper handle expand/collapse toggle
            return false;
        }

        /// <summary>
        /// OnDelete callback for TreeNavigationHelper.
        /// </summary>
        private static bool HandleDeleteNode(InspectionTreeItem item)
        {
            var data = item.Data as InventoryNodeData;
            if (data == null)
            {
                TolkHelper.Speak("Select a carried item to drop.");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return true;
            }

            if (data.Type == NodeType.Stack && data.StackData != null)
            {
                if (data.StackData.IsCarried)
                {
                    DropStack(data.StackData);
                }
                else
                {
                    TolkHelper.Speak("This item is in storage, not carried by a pawn.");
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }
                return true;
            }

            if (data.Type == NodeType.ItemGroup && data.ItemData != null)
            {
                if (data.ItemData.HasCarriedStacks)
                {
                    TolkHelper.Speak("Expand this item to select a specific stack to drop.");
                }
                else
                {
                    TolkHelper.Speak("This item is in storage, not carried by a pawn.");
                }
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return true;
            }

            if (data.Type == NodeType.DefGroup || data.Type == NodeType.MaterialGroup)
            {
                TolkHelper.Speak("Expand this group and select a specific stack to drop.");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return true;
            }

            TolkHelper.Speak("Select a carried item to drop.");
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            return true;
        }

        /// <summary>
        /// OnInfo callback for TreeNavigationHelper.
        /// </summary>
        private static bool HandleInfoNode(InspectionTreeItem item)
        {
            var invItem = GetInventoryItemFromNode(item);
            if (invItem != null && invItem.Def != null)
            {
                InfoCardState.OpenInfoCardForDef(invItem.Def);
                return true;
            }

            // Let TreeNavigationHelper try LinkedDef fallback
            return false;
        }

        /// <summary>
        /// Gets the InventoryItem for an InspectionTreeItem (ItemGroup, Stack, or their parent).
        /// Returns null if the item is not related to an inventory item.
        /// </summary>
        private static InventoryHelper.InventoryItem GetInventoryItemFromNode(InspectionTreeItem item)
        {
            var data = item.Data as InventoryNodeData;
            if (data == null) return null;

            if ((data.Type == NodeType.ItemGroup || data.Type == NodeType.Stack) && data.ItemData != null)
                return data.ItemData;

            return null;
        }

        /// <summary>
        /// Gets the InventoryStack for an InspectionTreeItem if it's a Stack node.
        /// Returns null otherwise.
        /// </summary>
        private static InventoryHelper.InventoryStack GetStackFromNode(InspectionTreeItem item)
        {
            var data = item.Data as InventoryNodeData;
            if (data == null) return null;

            if (data.Type == NodeType.Stack && data.StackData != null)
                return data.StackData;

            return null;
        }

        /// <summary>
        /// Opens a context menu for the current stack or item group via WindowlessFloatMenuState.
        /// </summary>
        private static void OpenContextMenu()
        {
            var item = treeNav.SelectedItem;
            if (item == null)
            {
                TolkHelper.Speak("No context menu available");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            var data = item.Data as InventoryNodeData;
            if (data == null)
            {
                TolkHelper.Speak("No context menu available");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            if (data.Type == NodeType.Stack && data.StackData != null)
            {
                OpenStackContextMenu(data.StackData, data.ItemData);
                return;
            }

            if (data.Type == NodeType.ItemGroup && data.ItemData != null)
            {
                OpenItemGroupContextMenu(data.ItemData);
                return;
            }

            TolkHelper.Speak("No context menu available");
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Opens context menu for a specific stack with contextual actions.
        /// </summary>
        private static void OpenStackContextMenu(InventoryHelper.InventoryStack stack, InventoryHelper.InventoryItem item)
        {
            var options = new List<FloatMenuOption>();

            if (stack.IsCarried)
            {
                options.Add(new FloatMenuOption("Jump to pawn (Alt+J)", () => JumpToStack(stack)));
                options.Add(new FloatMenuOption("Drop (Delete)", () => DropStack(stack)));
            }
            else
            {
                if (stack.IsMinifiedThing)
                {
                    options.Add(new FloatMenuOption("Install at", () => InstallStack(stack)));
                }
                options.Add(new FloatMenuOption("Jump to location (Alt+J)", () => JumpToStack(stack)));
            }

            if (item?.Def != null)
            {
                options.Add(new FloatMenuOption("Show info (Alt+I)", () => InfoCardState.OpenInfoCardForDef(item.Def)));
            }

            if (options.Count == 0)
            {
                TolkHelper.Speak("No actions available");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        /// <summary>
        /// Opens context menu for an item group (aggregate level).
        /// </summary>
        private static void OpenItemGroupContextMenu(InventoryHelper.InventoryItem item)
        {
            var options = new List<FloatMenuOption>();

            var firstStack = item.Stacks.FirstOrDefault();
            if (firstStack != null)
            {
                options.Add(new FloatMenuOption("Jump to first location (Alt+J)", () => JumpToStack(firstStack)));
            }

            if (item.Def != null)
            {
                options.Add(new FloatMenuOption("Show info (Alt+I)", () => InfoCardState.OpenInfoCardForDef(item.Def)));
            }

            options.Add(new FloatMenuOption("View details", () => ViewItemDetails(item)));

            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        /// <summary>
        /// Handles the Alt+J key - jumps to stack location or first stack of item group.
        /// </summary>
        private static void HandleJumpKey()
        {
            var item = treeNav.SelectedItem;
            if (item == null)
            {
                TolkHelper.Speak("Select an item to jump to.");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            // Stack-level: jump to that specific stack's location
            var stack = GetStackFromNode(item);
            if (stack != null)
            {
                JumpToStack(stack);
                return;
            }

            var data = item.Data as InventoryNodeData;

            // ItemGroup-level: jump to first available stack location
            if (data != null && data.Type == NodeType.ItemGroup && data.ItemData != null)
            {
                var firstStack = data.ItemData.Stacks.FirstOrDefault();
                if (firstStack != null)
                {
                    JumpToStack(firstStack);
                    return;
                }
            }

            TolkHelper.Speak("Select an item to jump to.");
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        #endregion

        #region Item Actions

        /// <summary>
        /// Jumps the camera and cursor to a specific stack's location
        /// </summary>
        private static void JumpToStack(InventoryHelper.InventoryStack stack)
        {
            if (stack == null)
            {
                TolkHelper.Speak("No location found.");
                return;
            }

            IntVec3 location;
            string announcement;

            if (stack.IsCarried)
            {
                Pawn carrier = stack.CarrierPawn;
                if (carrier == null || carrier.Destroyed || carrier.Dead)
                {
                    TolkHelper.Speak("Carrier pawn no longer available.");
                    return;
                }
                location = carrier.Position;
                announcement = $"Jumped to {carrier.LabelShort} at {location}.";
            }
            else
            {
                location = stack.Thing.Position;
                announcement = $"Jumped to {stack.LocationLabel} at {location}.";
            }

            if (Find.CameraDriver != null)
            {
                Find.CameraDriver.JumpToCurrentMapLoc(location);
            }

            if (MapNavigationState.IsInitialized)
            {
                MapNavigationState.CurrentCursorPosition = location;
            }

            TolkHelper.Speak(announcement);
            SoundDefOf.Click.PlayOneShotOnCamera();
            Close();
        }

        /// <summary>
        /// Drops a specific carried stack from its carrier pawn. Refreshes the menu.
        /// </summary>
        private static void DropStack(InventoryHelper.InventoryStack stack)
        {
            if (stack == null || stack.Thing == null)
            {
                TolkHelper.Speak("No item to drop.");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            Pawn carrier = stack.CarrierPawn;
            if (carrier == null || carrier.Destroyed || carrier.Dead)
            {
                TolkHelper.Speak("Carrier pawn no longer available.");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            if (carrier.inventory?.innerContainer == null)
            {
                TolkHelper.Speak("Cannot access carrier's inventory.");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            if (carrier.inventory.innerContainer.TryDrop(stack.Thing, carrier.Position, carrier.Map,
                ThingPlaceMode.Near, out Thing droppedThing))
            {
                string carrierName = carrier.LabelShort;
                int quantity = stack.Quantity;

                RefreshInventory();

                TolkHelper.Speak($"Dropped x{quantity} from {carrierName}.");
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            else
            {
                TolkHelper.Speak($"Failed to drop {stack.Thing.LabelCap}.");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        /// <summary>
        /// Initiates installation of a minified furniture stack.
        /// </summary>
        private static void InstallStack(InventoryHelper.InventoryStack stack)
        {
            if (!stack.IsMinifiedThing || stack.Thing == null || stack.Thing.Destroyed)
            {
                TolkHelper.Speak("No installable item found.");
                return;
            }

            Close();

            Find.Selector.ClearSelection();
            Find.Selector.Select(stack.Thing, playSound: false, forceDesignatorDeselect: false);

            Designator_Install installDesignator = new Designator_Install();
            ArchitectState.EnterPlacementMode(installDesignator);
        }

        /// <summary>
        /// Views detailed information about an item
        /// </summary>
        private static void ViewItemDetails(InventoryHelper.InventoryItem item)
        {
            ThingDef def = item.Def;

            List<string> details = new List<string>();
            details.Add($"Item: {def.LabelCap}");
            details.Add($"Total quantity: {item.TotalQuantity}");
            details.Add($"Description: {def.description}");

            if (def.stackLimit > 1)
            {
                details.Add($"Stack limit: {def.stackLimit}");
            }

            if (def.BaseMarketValue > 0)
            {
                details.Add($"Market value: ${def.BaseMarketValue:F2} each, ${def.BaseMarketValue * item.TotalQuantity:F2} total");
            }

            if (def.statBases != null && def.statBases.Count > 0)
            {
                details.Add($"Mass: {def.statBases.GetStatValueFromList(StatDefOf.Mass, 0):F2} kg");
            }

            details.Add($"Stacks: {item.Stacks.Count}");
            if (item.HasCarriedStacks)
            {
                details.Add($"Carried by colonists: {item.CarriedCount}");
            }

            string announcement = string.Join(". ", details);
            TolkHelper.Speak(announcement);
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        #endregion

        #region Refresh and State Preservation

        /// <summary>
        /// Counts distinct items across all categories (DefGroups + single-variant ItemGroups).
        /// </summary>
        private static int CountDistinctItems(InspectionTreeItem node)
        {
            int count = 0;
            var data = node.Data as InventoryNodeData;

            if (data != null && (data.Type == NodeType.DefGroup || data.Type == NodeType.ItemGroup))
            {
                count++;
            }
            else if (data != null && data.Type == NodeType.Category && node.Children.Count > 0)
            {
                foreach (var child in node.Children)
                {
                    count += CountDistinctItems(child);
                }
            }
            else if (data == null && node.Children.Count > 0)
            {
                // Root node
                foreach (var child in node.Children)
                {
                    count += CountDistinctItems(child);
                }
            }

            return count;
        }

        /// <summary>
        /// Gets a unique key for a node to track expansion state across refreshes.
        /// </summary>
        private static string GetNodeKey(InspectionTreeItem node)
        {
            var data = node.Data as InventoryNodeData;
            if (data == null) return node.Label;

            if (data.Type == NodeType.Category && data.CategoryData != null)
            {
                if (data.CategoryData.CategoryDef == null)
                    return "cat:uncategorized";
                return "cat:" + data.CategoryData.CategoryDef.defName;
            }

            if (data.Type == NodeType.DefGroup && data.DefGroupDef != null)
            {
                // Distinguish meal tier DefGroups from material/quality DefGroups
                if (HasMealTierPreferability(data.DefGroupDef) && data.DefGroupItems != null
                    && data.DefGroupItems.Any(i => i.Def != data.DefGroupDef))
                {
                    return "defgroup:meal:" + data.DefGroupDef.ingestible.preferability.ToString();
                }
                return "defgroup:" + data.DefGroupDef.defName;
            }

            if (data.Type == NodeType.MaterialGroup && data.DefGroupDef != null)
            {
                string key = "matgroup:" + data.DefGroupDef.defName;
                if (data.DefGroupStuff != null)
                    key += ":" + data.DefGroupStuff.defName;
                return key;
            }

            if (data.Type == NodeType.ItemGroup && data.ItemData != null)
            {
                string key = "itemgroup:" + data.ItemData.Def.defName;
                if (data.ItemData.Stuff != null)
                    key += ":" + data.ItemData.Stuff.defName;
                if (data.ItemData.Quality.HasValue)
                    key += ":" + data.ItemData.Quality.Value.ToString();
                return key;
            }

            if (data.Type == NodeType.Stack && data.StackData != null)
            {
                return "stack:" + data.StackData.Thing.ThingID;
            }

            return node.Label;
        }

        /// <summary>
        /// Saves the expansion state of all nodes in the tree.
        /// </summary>
        private static void SaveExpansionState(InspectionTreeItem node, Dictionary<string, bool> state)
        {
            if (node.IsExpandable)
            {
                string key = GetNodeKey(node);
                if (!string.IsNullOrEmpty(key))
                {
                    state[key] = node.IsExpanded;
                }
            }
            foreach (var child in node.Children)
            {
                SaveExpansionState(child, state);
            }
        }

        /// <summary>
        /// Restores the expansion state of nodes from a previously saved state.
        /// </summary>
        private static void RestoreExpansionState(InspectionTreeItem node, Dictionary<string, bool> state)
        {
            if (node.IsExpandable)
            {
                string key = GetNodeKey(node);
                if (!string.IsNullOrEmpty(key) && state.TryGetValue(key, out bool wasExpanded))
                {
                    node.IsExpanded = wasExpanded;
                }
            }
            foreach (var child in node.Children)
            {
                RestoreExpansionState(child, state);
            }
        }

        /// <summary>
        /// Refreshes the inventory menu without closing it.
        /// Preserves expansion state and selection position.
        /// </summary>
        private static void RefreshInventory()
        {
            // Save expansion state
            var expansionState = new Dictionary<string, bool>();
            if (treeNav.RootItem != null)
            {
                SaveExpansionState(treeNav.RootItem, expansionState);
            }

            string oldLabel = treeNav.SelectedItem?.Label;
            int oldIndex = treeNav.SelectedIndex;

            // Recollect all items with unified aggregation
            List<Thing> allStoredItems = InventoryHelper.GetAllStoredItems();
            Dictionary<Thing, Pawn> pawnCarriedThings = InventoryHelper.GetAllPawnCarriedItems();
            List<InventoryHelper.InventoryItem> allItems = InventoryHelper.AggregateAllItems(allStoredItems, pawnCarriedThings);
            List<InventoryHelper.CategoryNode> categoryTree = InventoryHelper.BuildCategoryTree(allItems);
            var root = BuildTree(categoryTree);

            // Restore expansion state before initializing (so visible list reflects old state)
            RestoreExpansionState(root, expansionState);

            treeNav.Initialize(root);

            // Try to restore cursor position by label
            if (!string.IsNullOrEmpty(oldLabel))
            {
                for (int i = 0; i < treeNav.VisibleItems.Count; i++)
                {
                    if (treeNav.VisibleItems[i].Label == oldLabel)
                    {
                        treeNav.SetSelectedIndex(i);
                        return;
                    }
                }
            }

            // Fall back to clamping old index
            if (oldIndex < treeNav.Count)
            {
                treeNav.SetSelectedIndex(oldIndex);
            }
            else
            {
                treeNav.SetSelectedIndex(Math.Max(0, treeNav.Count - 1));
            }
        }

        #endregion

        #region Visual Highlight

        /// <summary>
        /// Draws visual highlights for the selected item (for sighted users)
        /// </summary>
        public static void DrawHighlight()
        {
            if (!isActive || treeNav.Count == 0) return;

            var current = treeNav.SelectedItem;
            if (current == null) return;

            // Calculate position for highlight
            float lineHeight = 24f;
            float yOffset = treeNav.SelectedIndex * lineHeight;
            float xOffset = 20f;
            float width = 600f;
            float height = lineHeight;

            Rect highlightRect = new Rect(xOffset, yOffset + 100f, width, height);

            // Draw semi-transparent highlight
            Color highlightColor = new Color(1f, 1f, 0f, 0.3f);
            Widgets.DrawBoxSolid(highlightRect, highlightColor);

            // Draw label
            string indent = new string(' ', current.IndentLevel * 2);
            string expandIndicator = "";
            if (current.IsExpandable)
            {
                expandIndicator = current.IsExpanded ? "▼ " : "► ";
            }
            string displayString = $"{indent}{expandIndicator}{current.Label}";

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(highlightRect, displayString);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        #endregion
    }
}
