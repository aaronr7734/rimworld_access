using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// The data facade behind the windowless colony-wide inventory menu: the aggregation of every
    /// stored and pawn-carried item on the map, the category/group/stack node tree built from it,
    /// the row labels, the per-stack context menus, and the item actions (jump, drop, install,
    /// details). It owns the tree's content and lifecycle and none of its keyboard behaviour — the
    /// cursor, typeahead, expansion and row announcements all live in <see cref="InventoryScope"/>.
    /// </summary>
    public static class WindowlessInventoryState
    {
        /// <summary>Node kind, stored in InspectionTreeItem.Data via InventoryNodeData.</summary>
        public enum NodeType
        {
            Category,       // A ThingCategoryDef
            DefGroup,       // All items of the same ThingDef (2+ variants) or same meal tier
            MaterialGroup,  // All items of the same ThingDef + Stuff (within a DefGroup)
            ItemGroup,      // An aggregated item type (single variant: def+stuff+quality)
            Stack           // An individual physical stack at a specific location
        }

        /// <summary>Per-node payload stored in InspectionTreeItem.Data.</summary>
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

        private static bool isActive = false;

        public static bool IsActive => isActive;

        /// <summary>The tree the scope is presenting, kept here so a re-pushed scope can re-seed.</summary>
        private static InspectionTreeItem treeRoot;

        /// <summary>Set when a tree was built with no scope in existence yet to receive it (see NotifyScopeAttached).</summary>
        private static bool pendingOpen;

        /// <summary>
        /// Hands a freshly built tree to the scope, which presents it and speaks the focused row.
        /// The pending flag covers the one case where no scope exists yet, so the tree still
        /// reaches the screen on the scope's first push.
        /// </summary>
        private static void PresentTree(InspectionTreeItem root, bool announceRow)
        {
            treeRoot = root;
            InventoryScope live = InventoryScope.Live;
            if (live == null)
            {
                pendingOpen = announceRow;
                return;
            }
            pendingOpen = false;
            live.OpenTree(root, announceRow);
        }

        /// <summary>Called from the scope's OnPush: adopt the current tree if it has not already.</summary>
        internal static void NotifyScopeAttached(InventoryScope scope)
        {
            if (!isActive || treeRoot == null)
            {
                return;
            }
            if (pendingOpen)
            {
                pendingOpen = false;
                scope.OpenTree(treeRoot, true);
                return;
            }
            scope.EnsureTree(treeRoot);
        }

        private static void ClearTree()
        {
            treeRoot = null;
            pendingOpen = false;
            InventoryScope live = InventoryScope.Live;
            if (live != null)
            {
                live.ClearTree();
            }
        }

        public static void Open()
        {
            if (Find.CurrentMap == null)
            {
                Log.Warning("WindowlessInventoryState: Cannot open - no current map");
                return;
            }

            isActive = true;

            List<Thing> allStoredItems = InventoryHelper.GetAllStoredItems();
            Dictionary<Thing, Pawn> pawnCarriedThings = InventoryHelper.GetAllPawnCarriedItems();

            if (allStoredItems.Count == 0 && pawnCarriedThings.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.OpenedEmpty".Loc());
                var emptyRoot = new InspectionTreeItem
                {
                    Label = "Root",
                    IndentLevel = -1,
                    IsExpanded = true,
                    IsExpandable = false
                };
                PresentTree(emptyRoot, announceRow: false);
                SoundDefOf.TabOpen.PlayOneShotOnCamera();
                return;
            }

            // Unified aggregation merges stored and carried by ThingDef, Stuff and Quality.
            List<InventoryHelper.InventoryItem> allItems = InventoryHelper.AggregateAllItems(allStoredItems, pawnCarriedThings);

            List<InventoryHelper.CategoryNode> categoryTree = InventoryHelper.BuildCategoryTree(allItems);

            var root = BuildTree(categoryTree);

            // Distinct items are DefGroups and single-variant ItemGroups, not every variant.
            int distinctItemCount = CountDistinctItems(root);

            string summaryKey = distinctItemCount == 1
                ? "RimWorldAccess.Inspection.Inventory.OpenedSummaryOne"
                : "RimWorldAccess.Inspection.Inventory.OpenedSummaryMany";
            TolkHelper.Speak(summaryKey.Loc(distinctItemCount, root.Children.Count));
            SoundDefOf.TabOpen.PlayOneShotOnCamera();

            // The scope announces the focused row on top of the summary.
            PresentTree(root, announceRow: true);
        }

        /// <summary>
        /// Clears session state with no announcement, sound, or selection mutation. Used by
        /// StateResetRegistry at a session boundary, where speaking a close announcement would be
        /// wrong; Close() delegates its field clearing here so the two cannot drift.
        /// </summary>
        public static void Reset()
        {
            isActive = false;
            ClearTree();
        }

        public static void Close()
        {
            Reset();

            TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.Closed".Loc());
            SoundDefOf.TabClose.PlayOneShotOnCamera();
        }

        #region Tree Building

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
        /// Converts an InventoryHelper.CategoryNode tree to InspectionTreeItems: DefGroup and
        /// MaterialGroup levels for multi-variant items, a flat ItemGroup for single-variant ones.
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

                if (categoryNode.SubCategories.Count > 0)
                {
                    BuildCategoryChildren(categoryNode.SubCategories, catItem, depth + 1);
                }

                BuildItemChildren(categoryNode.Items, catItem, depth);

                parent.Children.Add(catItem);
            }
        }

        /// <summary>Builds a category node's item children.</summary>
        private static void BuildItemChildren(List<InventoryHelper.InventoryItem> items, InspectionTreeItem catItem, int depth)
        {
            if (items.Count == 0) return;

            // Meal-tier items are grouped by preferability rather than by ThingDef.
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

            var remainingItems = items.Where(i => !mealGroupedDefs.Contains(i.Def)).ToList();
            var defGroups = remainingItems
                .GroupBy(i => i.Def)
                .ToList();

            // Children go into one list so the whole level can be sorted together.
            var childNodes = new List<(InspectionTreeItem node, int totalQty)>();

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

                // Meal DefGroup children are ItemGroups: each is a different ThingDef.
                foreach (var item in mealItems)
                {
                    var itemGroupItem = BuildItemGroupNode(item, defGroupItem, depth + 2);
                    defGroupItem.Children.Add(itemGroupItem);
                }

                childNodes.Add((defGroupItem, totalQty));
            }

            foreach (var defGroup in defGroups)
            {
                var groupItems = defGroup.ToList();

                if (groupItems.Count == 1)
                {
                    var itemGroupItem = BuildItemGroupNode(groupItems[0], catItem, depth + 1);
                    childNodes.Add((itemGroupItem, groupItems[0].TotalQuantity));
                }
                else
                {
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
                        // Quality-only variation puts Stacks directly under the DefGroup.
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

            foreach (var (node, _) in childNodes.OrderByDescending(c => c.totalQty))
            {
                catItem.Children.Add(node);
            }
        }

        /// <summary>
        /// Builds an ItemGroup node with Stack children, for single-variant items and for meal
        /// variants inside a meal DefGroup.
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

        private static bool IsMealTier(FoodPreferability pref)
        {
            return pref == FoodPreferability.MealAwful
                || pref == FoodPreferability.MealTerrible
                || pref == FoodPreferability.MealSimple
                || pref == FoodPreferability.MealFine
                || pref == FoodPreferability.MealLavish;
        }

        private static bool HasMealTierPreferability(ThingDef def)
        {
            return def.ingestible != null && IsMealTier(def.ingestible.preferability);
        }

        /// <summary>Display label for a material/quality DefGroup node, with a material summary.</summary>
        private static string BuildDefGroupLabel(ThingDef def, List<InventoryHelper.InventoryItem> items)
        {
            int totalCount = items.Sum(i => i.TotalQuantity);
            int carriedCount = items.Sum(i => i.CarriedCount);

            string label = BuildGroupHead(def.LabelCap, totalCount, carriedCount);

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
                        shownMaterials.Add("RimWorldAccess.Inspection.Inventory.Label.MaterialEntry".Translate(
                            mat.Count, mat.Stuff.LabelAsStuff));
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
                        label += "RimWorldAccess.Inspection.Inventory.Label.MaterialAndOne".Translate(
                            truncatedMaterials[0].count, truncatedMaterials[0].name);
                    }
                    else if (truncatedMaterials.Count > 1)
                    {
                        label += "RimWorldAccess.Inspection.Inventory.Label.MaterialAndOther".Translate(
                            truncatedMaterials.Count);
                    }
                }
            }

            return label;
        }

        /// <summary>
        /// The label head shared by the three group label builders, extended with the carried-by-
        /// colonists parenthetical when at least one stack sits in a pawn's inventory.
        /// </summary>
        private static string BuildGroupHead(string label, int totalCount, int carriedCount)
        {
            if (carriedCount > 0)
                return "RimWorldAccess.Inspection.Inventory.Label.GroupHeadCarried".Translate(
                    label, totalCount, carriedCount);
            return "RimWorldAccess.Inspection.Inventory.Label.GroupHead".Translate(label, totalCount);
        }

        /// <summary>Display label for a meal-tier DefGroup, named after its shortest member label.</summary>
        private static string BuildMealGroupLabel(List<InventoryHelper.InventoryItem> items)
        {
            int totalCount = items.Sum(i => i.TotalQuantity);
            int carriedCount = items.Sum(i => i.CarriedCount);

            string baseName = items
                .OrderBy(i => i.Def.label.Length)
                .First().Def.LabelCap;

            return BuildGroupHead(baseName, totalCount, carriedCount);
        }

        /// <summary>Display label for a MaterialGroup node.</summary>
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
            return BuildGroupHead(name, totalQty, carriedCount);
        }

        /// <summary>Display label for an individual stack node, with its location and flags.</summary>
        private static string BuildStackLabel(InventoryHelper.InventoryItem item, InventoryHelper.InventoryStack stack)
        {
            string name = item.GetItemName(stack.Thing);
            string suffix = "";
            if (stack.IsTainted)
                suffix += "RimWorldAccess.Inspection.Inventory.Label.StackTaintedSuffix".Translate();
            if (stack.IsForbidden)
                suffix += "RimWorldAccess.Inspection.Inventory.Label.StackForbiddenSuffix".Translate();
            return "RimWorldAccess.Inspection.Inventory.Label.StackLine".Translate(
                name, stack.Quantity, stack.LocationLabel, suffix);
        }

        #endregion

        #region Activation and Domain Actions

        /// <summary>
        /// Delete on a tree row: drop a carried stack, or explain which row the player has to be
        /// on instead. A null row is a silent no-op.
        /// </summary>
        internal static void DeleteNode(InspectionTreeItem item)
        {
            if (item == null)
            {
                return;
            }

            var data = item.Data as InventoryNodeData;
            if (data == null)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.Drop.SelectCarriedItem".Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            if (data.Type == NodeType.Stack && data.StackData != null)
            {
                if (data.StackData.IsCarried)
                {
                    DropStack(data.StackData);
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.Drop.NotCarriedStack".Loc());
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }
                return;
            }

            if (data.Type == NodeType.ItemGroup && data.ItemData != null)
            {
                string key = data.ItemData.HasCarriedStacks
                    ? "RimWorldAccess.Inspection.Inventory.Drop.ExpandToSelectStack"
                    : "RimWorldAccess.Inspection.Inventory.Drop.NotCarriedStack";
                TolkHelper.Speak(key.Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            if (data.Type == NodeType.DefGroup || data.Type == NodeType.MaterialGroup)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.Drop.ExpandGroupToSelectStack".Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.Drop.SelectCarriedItem".Loc());
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Alt+I's first rung: the info card for the row's own aggregated item, when it has
        /// one. False leaves <see cref="InventoryScope"/> to walk the ancestors' linked defs.
        /// </summary>
        internal static bool TryOpenInfoCardFor(InspectionTreeItem item)
        {
            var invItem = GetInventoryItemFromNode(item);
            if (invItem != null && invItem.Def != null)
            {
                InfoCardState.OpenInfoCardForDef(invItem.Def);
                return true;
            }

            return false;
        }

        /// <summary>The InventoryItem behind a row (ItemGroup, Stack, or their parent), else null.</summary>
        private static InventoryHelper.InventoryItem GetInventoryItemFromNode(InspectionTreeItem item)
        {
            var data = item.Data as InventoryNodeData;
            if (data == null) return null;

            if ((data.Type == NodeType.ItemGroup || data.Type == NodeType.Stack) && data.ItemData != null)
                return data.ItemData;

            return null;
        }

        /// <summary>The InventoryStack behind a Stack row, else null.</summary>
        private static InventoryHelper.InventoryStack GetStackFromNode(InspectionTreeItem item)
        {
            var data = item.Data as InventoryNodeData;
            if (data == null) return null;

            if (data.Type == NodeType.Stack && data.StackData != null)
                return data.StackData;

            return null;
        }

        /// <summary>
        /// Opens the context menu for a stack or item-group row via WindowlessFloatMenuState.
        /// </summary>
        internal static void OpenContextMenuFor(InspectionTreeItem item)
        {
            if (item == null)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.Context.NoMenuAvailable".Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            var data = item.Data as InventoryNodeData;
            if (data == null)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.Context.NoMenuAvailable".Loc());
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

            TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.Context.NoMenuAvailable".Loc());
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        private static void OpenStackContextMenu(InventoryHelper.InventoryStack stack, InventoryHelper.InventoryItem item)
        {
            var options = new List<FloatMenuOption>();

            if (stack.IsCarried)
            {
                options.Add(new FloatMenuOption(
                    "RimWorldAccess.Inspection.Inventory.Action.JumpToPawn".Translate(),
                    () => JumpToStack(stack)));
                options.Add(new FloatMenuOption(
                    "RimWorldAccess.Inspection.Inventory.Action.Drop".Translate(),
                    () => DropStack(stack)));
            }
            else
            {
                if (stack.IsMinifiedThing)
                {
                    options.Add(new FloatMenuOption(
                        "RimWorldAccess.Inspection.Inventory.Action.Install".Translate(),
                        () => InstallStack(stack)));
                }
                options.Add(new FloatMenuOption(
                    "RimWorldAccess.Inspection.Inventory.Action.JumpToLocation".Translate(),
                    () => JumpToStack(stack)));
            }

            if (item?.Def != null)
            {
                options.Add(new FloatMenuOption(
                    "RimWorldAccess.Inspection.Inventory.Action.ShowInfo".Translate(),
                    () => InfoCardState.OpenInfoCardForDef(item.Def)));
            }

            if (options.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.Context.NoActionsAvailable".Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        private static void OpenItemGroupContextMenu(InventoryHelper.InventoryItem item)
        {
            var options = new List<FloatMenuOption>();

            var firstStack = item.Stacks.FirstOrDefault();
            if (firstStack != null)
            {
                options.Add(new FloatMenuOption(
                    "RimWorldAccess.Inspection.Inventory.Action.JumpToFirstLocation".Translate(),
                    () => JumpToStack(firstStack)));
            }

            if (item.Def != null)
            {
                options.Add(new FloatMenuOption(
                    "RimWorldAccess.Inspection.Inventory.Action.ShowInfo".Translate(),
                    () => InfoCardState.OpenInfoCardForDef(item.Def)));
            }

            options.Add(new FloatMenuOption(
                "RimWorldAccess.Inspection.Inventory.Action.ViewDetails".Translate(),
                () => ViewItemDetails(item)));

            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        /// <summary>
        /// Jumps to the row's stack location, or to the first stack of an item group.
        /// </summary>
        internal static void JumpToNode(InspectionTreeItem item)
        {
            if (item == null)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.Jump.SelectItem".Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            var stack = GetStackFromNode(item);
            if (stack != null)
            {
                JumpToStack(stack);
                return;
            }

            var data = item.Data as InventoryNodeData;

            if (data != null && data.Type == NodeType.ItemGroup && data.ItemData != null)
            {
                var firstStack = data.ItemData.Stacks.FirstOrDefault();
                if (firstStack != null)
                {
                    JumpToStack(firstStack);
                    return;
                }
            }

            TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.Jump.SelectItem".Loc());
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        #endregion

        #region Item Actions

        private static void JumpToStack(InventoryHelper.InventoryStack stack)
        {
            if (stack == null)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.Jump.NoLocation".Loc());
                return;
            }

            IntVec3 location;
            string subject;

            if (stack.IsCarried)
            {
                Pawn carrier = stack.CarrierPawn;
                if (carrier == null || carrier.Destroyed || carrier.Dead)
                {
                    TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.Drop.CarrierUnavailable".Loc());
                    return;
                }
                location = carrier.Position;
                subject = carrier.LabelShort;
            }
            else
            {
                location = stack.Thing.Position;
                subject = stack.LocationLabel;
            }
            string announcement = "RimWorldAccess.Inspection.Inventory.Jump.JumpedToAt".Translate(subject, location);

            if (Find.CameraDriver != null)
            {
                Find.CameraDriver.JumpToCurrentMapLoc(location);
            }

            if (MapNavigationState.IsInitialized)
            {
                MapNavigationState.CurrentCursorPosition = location;
            }

            TolkHelper.SpeakData(announcement);
            SoundDefOf.Click.PlayOneShotOnCamera();
            Close();
        }

        /// <summary>Drops a carried stack from its carrier pawn and refreshes the menu.</summary>
        private static void DropStack(InventoryHelper.InventoryStack stack)
        {
            if (stack == null || stack.Thing == null)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.Drop.NoItem".Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            Pawn carrier = stack.CarrierPawn;
            if (carrier == null || carrier.Destroyed || carrier.Dead)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.Drop.CarrierUnavailable".Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            if (carrier.inventory?.innerContainer == null)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.Drop.CannotAccessInventory".Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            if (carrier.inventory.innerContainer.TryDrop(stack.Thing, carrier.Position, carrier.Map,
                ThingPlaceMode.Near, out Thing droppedThing))
            {
                string carrierName = carrier.LabelShort;
                int quantity = stack.Quantity;

                RefreshInventory();

                TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.Drop.Dropped".Loc(quantity, carrierName));
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.Drop.Failed".Loc(stack.Thing.LabelCap));
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        private static void InstallStack(InventoryHelper.InventoryStack stack)
        {
            if (!stack.IsMinifiedThing || stack.Thing == null || stack.Thing.Destroyed)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.Install.NoItem".Loc());
                return;
            }

            Close();

            Find.Selector.ClearSelection();
            Find.Selector.Select(stack.Thing, playSound: false, forceDesignatorDeselect: false);

            Designator_Install installDesignator = new Designator_Install();
            ArchitectState.EnterPlacementMode(installDesignator);
        }

        private static void ViewItemDetails(InventoryHelper.InventoryItem item)
        {
            ThingDef def = item.Def;

            List<string> details = new List<string>();
            details.Add("RimWorldAccess.Inspection.Inventory.Detail.Item".Translate(def.LabelCap));
            details.Add("RimWorldAccess.Inspection.Inventory.Detail.TotalQuantity".Translate(item.TotalQuantity));
            details.Add("RimWorldAccess.Inspection.Inventory.Detail.Description".Translate(def.description));

            if (def.stackLimit > 1)
            {
                details.Add("RimWorldAccess.Inspection.Inventory.Detail.StackLimit".Translate(def.stackLimit));
            }

            if (def.BaseMarketValue > 0)
            {
                details.Add("RimWorldAccess.Inspection.Inventory.Detail.MarketValue".Translate(
                    def.BaseMarketValue.ToString("F2"),
                    (def.BaseMarketValue * item.TotalQuantity).ToString("F2")));
            }

            if (def.statBases != null && def.statBases.Count > 0)
            {
                details.Add("RimWorldAccess.Inspection.Inventory.Detail.Mass".Translate(
                    def.statBases.GetStatValueFromList(StatDefOf.Mass, 0).ToString("F2")));
            }

            details.Add("RimWorldAccess.Inspection.Inventory.Detail.Stacks".Translate(item.Stacks.Count));
            if (item.HasCarriedStacks)
            {
                details.Add("RimWorldAccess.Inspection.Inventory.Detail.CarriedByColonists".Translate(item.CarriedCount));
            }

            string announcement = string.Join(". ", details);
            TolkHelper.SpeakData(announcement);
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        #endregion

        #region Refresh and State Preservation

        /// <summary>Counts distinct items: DefGroups plus single-variant ItemGroups.</summary>
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
                foreach (var child in node.Children)
                {
                    count += CountDistinctItems(child);
                }
            }

            return count;
        }

        /// <summary>A stable key identifying a node across refreshes, for expansion state.</summary>
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

        /// <summary>Rebuilds the menu in place, preserving expansion state and selection.</summary>
        private static void RefreshInventory()
        {
            var expansionState = new Dictionary<string, bool>();
            if (treeRoot != null)
            {
                SaveExpansionState(treeRoot, expansionState);
            }

            List<Thing> allStoredItems = InventoryHelper.GetAllStoredItems();
            Dictionary<Thing, Pawn> pawnCarriedThings = InventoryHelper.GetAllPawnCarriedItems();
            List<InventoryHelper.InventoryItem> allItems = InventoryHelper.AggregateAllItems(allStoredItems, pawnCarriedThings);
            List<InventoryHelper.CategoryNode> categoryTree = InventoryHelper.BuildCategoryTree(allItems);
            var root = BuildTree(categoryTree);

            // Restore expansion state before handing over (so the visible list reflects it)
            RestoreExpansionState(root, expansionState);

            treeRoot = root;

            // The scope restores the cursor onto the same row label, else clamps the old index.
            InventoryScope live = InventoryScope.Live;
            if (live != null)
            {
                live.ReplaceTree(root);
            }
        }

        #endregion
    }
}
