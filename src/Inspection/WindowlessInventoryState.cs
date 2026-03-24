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
    /// Displays all stored items organized by category with hierarchical navigation
    /// </summary>
    public static class WindowlessInventoryState
    {
        private static bool isActive = false;
        private static List<TreeNode> rootNodes = new List<TreeNode>();
        private static List<TreeNode> flattenedVisibleNodes = new List<TreeNode>();
        private static int selectedIndex = 0;
        private static Dictionary<TreeNode, TreeNode> lastChildPerParent = new Dictionary<TreeNode, TreeNode>();
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        public static bool IsActive => isActive;
        public static TypeaheadSearchHelper Typeahead => typeahead;

        /// <summary>
        /// Represents a node in the inventory tree (category, item group, or stack)
        /// </summary>
        public class TreeNode
        {
            public enum NodeType
            {
                Category,       // A ThingCategoryDef
                DefGroup,       // All items of the same ThingDef (2+ variants) or same meal tier
                MaterialGroup,  // All items of the same ThingDef + Stuff (within a DefGroup)
                ItemGroup,      // An aggregated item type (single variant: def+stuff+quality)
                Stack           // An individual physical stack at a specific location
            }

            public NodeType Type { get; set; }
            public string Label { get; set; }
            public int Depth { get; set; }
            public bool IsExpanded { get; set; }
            public bool CanExpand { get; set; }

            // For Category nodes
            public InventoryHelper.CategoryNode CategoryData { get; set; }

            // For ItemGroup and Stack nodes
            public InventoryHelper.InventoryItem ItemData { get; set; }

            // For Stack nodes
            public InventoryHelper.InventoryStack StackData { get; set; }

            // For DefGroup and MaterialGroup nodes
            public ThingDef DefGroupDef { get; set; }
            public ThingDef DefGroupStuff { get; set; }
            public List<InventoryHelper.InventoryItem> DefGroupItems { get; set; }

            // Tree structure
            public TreeNode Parent { get; set; }
            public List<TreeNode> Children { get; set; }

            public TreeNode()
            {
                Children = new List<TreeNode>();
                IsExpanded = false;
                CanExpand = false;
            }

            public string GetDisplayString()
            {
                string indent = new string(' ', Depth * 2);
                string expandIndicator = "";

                if (CanExpand)
                {
                    expandIndicator = IsExpanded ? "▼ " : "► ";
                }

                return $"{indent}{expandIndicator}{Label}";
            }
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
            selectedIndex = 0;
            lastChildPerParent.Clear();
            MenuHelper.ResetLevel("Inventory");
            typeahead.ClearSearch();

            // Collect all items
            List<Thing> allStoredItems = InventoryHelper.GetAllStoredItems();
            Dictionary<Thing, Pawn> pawnCarriedThings = InventoryHelper.GetAllPawnCarriedItems();

            if (allStoredItems.Count == 0 && pawnCarriedThings.Count == 0)
            {
                TolkHelper.Speak("Inventory menu opened. No items in colony storage or carried by pawns.");
                rootNodes = new List<TreeNode>();
                flattenedVisibleNodes = new List<TreeNode>();
                SoundDefOf.TabOpen.PlayOneShotOnCamera();
                return;
            }

            // Unified aggregation (merges stored + carried by ThingDef + Stuff + Quality)
            List<InventoryHelper.InventoryItem> allItems = InventoryHelper.AggregateAllItems(allStoredItems, pawnCarriedThings);

            // Build category tree
            List<InventoryHelper.CategoryNode> categoryTree = InventoryHelper.BuildCategoryTree(allItems);

            // Convert to TreeNode structure
            rootNodes = BuildTreeNodes(categoryTree);

            // Flatten to get initially visible nodes
            RebuildFlattenedList();

            // Count distinct items (DefGroups and single-variant ItemGroups, not every variant)
            int distinctItemCount = CountDistinctItems(rootNodes);

            // Announcement
            string announcement = $"Colony inventory opened. {distinctItemCount} item types. {rootNodes.Count} categories.";
            TolkHelper.Speak(announcement);
            SoundDefOf.TabOpen.PlayOneShotOnCamera();

            // Announce first selection
            if (flattenedVisibleNodes.Count > 0)
            {
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Converts InventoryHelper.CategoryNode tree to TreeNode tree.
        /// Creates hierarchy with DefGroup/MaterialGroup for multi-variant items,
        /// and flat ItemGroup for single-variant items.
        /// </summary>
        private static List<TreeNode> BuildTreeNodes(List<InventoryHelper.CategoryNode> categoryNodes, TreeNode parent = null, int depth = 0)
        {
            List<TreeNode> nodes = new List<TreeNode>();

            foreach (InventoryHelper.CategoryNode categoryNode in categoryNodes)
            {
                TreeNode catNode = new TreeNode
                {
                    Type = TreeNode.NodeType.Category,
                    Label = categoryNode.GetDisplayLabel(),
                    Depth = depth,
                    CategoryData = categoryNode,
                    Parent = parent,
                    CanExpand = (categoryNode.SubCategories.Count > 0 || categoryNode.Items.Count > 0)
                };

                // Build children (subcategories)
                if (categoryNode.SubCategories.Count > 0)
                {
                    catNode.Children.AddRange(BuildTreeNodes(categoryNode.SubCategories, catNode, depth + 1));
                }

                // Build item children with DefGroup/MaterialGroup/ItemGroup hierarchy
                BuildItemChildren(categoryNode.Items, catNode, depth);

                nodes.Add(catNode);
            }

            return nodes;
        }

        /// <summary>
        /// Builds item children for a category node, creating DefGroups for multi-variant
        /// items and flat ItemGroups for single-variant items.
        /// </summary>
        private static void BuildItemChildren(List<InventoryHelper.InventoryItem> items, TreeNode catNode, int depth)
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
            var childNodes = new List<(TreeNode node, int totalQty)>();

            // Build meal DefGroup nodes
            foreach (var mealGroup in mealGroups)
            {
                var mealItems = mealGroup.OrderByDescending(i => i.TotalQuantity).ToList();
                int totalQty = mealItems.Sum(i => i.TotalQuantity);

                TreeNode defGroupNode = new TreeNode
                {
                    Type = TreeNode.NodeType.DefGroup,
                    Label = BuildMealGroupLabel(mealItems),
                    Depth = depth + 1,
                    Parent = catNode,
                    CanExpand = true,
                    DefGroupDef = mealItems.OrderBy(i => i.Def.label.Length).First().Def,
                    DefGroupItems = mealItems
                };

                // Meal DefGroup children are ItemGroups (each is a different ThingDef)
                foreach (var item in mealItems)
                {
                    TreeNode itemGroupNode = BuildItemGroupNode(item, defGroupNode, depth + 2);
                    defGroupNode.Children.Add(itemGroupNode);
                }

                childNodes.Add((defGroupNode, totalQty));
            }

            // Build material/quality DefGroups and single-variant ItemGroups
            foreach (var defGroup in defGroups)
            {
                var groupItems = defGroup.ToList();

                if (groupItems.Count == 1)
                {
                    // Single variant: regular ItemGroup
                    TreeNode itemGroupNode = BuildItemGroupNode(groupItems[0], catNode, depth + 1);
                    childNodes.Add((itemGroupNode, groupItems[0].TotalQuantity));
                }
                else
                {
                    // Multiple variants: create DefGroup
                    ThingDef def = defGroup.Key;
                    int totalQty = groupItems.Sum(i => i.TotalQuantity);
                    bool hasStuffVariations = groupItems.Any(i => i.Stuff != null);

                    TreeNode defGroupNode = new TreeNode
                    {
                        Type = TreeNode.NodeType.DefGroup,
                        Label = BuildDefGroupLabel(def, groupItems),
                        Depth = depth + 1,
                        Parent = catNode,
                        CanExpand = true,
                        DefGroupDef = def,
                        DefGroupItems = groupItems
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

                            TreeNode matGroupNode = new TreeNode
                            {
                                Type = TreeNode.NodeType.MaterialGroup,
                                Label = BuildMaterialGroupLabel(def, matGroup.Stuff, materialTotal, matGroup.Items),
                                Depth = depth + 2,
                                Parent = defGroupNode,
                                CanExpand = true,
                                DefGroupDef = def,
                                DefGroupStuff = matGroup.Stuff
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
                                TreeNode stackNode = new TreeNode
                                {
                                    Type = TreeNode.NodeType.Stack,
                                    Label = BuildStackLabel(pair.Item, pair.Stack),
                                    Depth = depth + 3,
                                    StackData = pair.Stack,
                                    ItemData = pair.Item,
                                    Parent = matGroupNode,
                                    CanExpand = false
                                };
                                matGroupNode.Children.Add(stackNode);
                            }

                            defGroupNode.Children.Add(matGroupNode);
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
                            TreeNode stackNode = new TreeNode
                            {
                                Type = TreeNode.NodeType.Stack,
                                Label = BuildStackLabel(pair.Item, pair.Stack),
                                Depth = depth + 2,
                                StackData = pair.Stack,
                                ItemData = pair.Item,
                                Parent = defGroupNode,
                                CanExpand = false
                            };
                            defGroupNode.Children.Add(stackNode);
                        }
                    }

                    childNodes.Add((defGroupNode, totalQty));
                }
            }

            // Sort all children by total quantity descending and add to category
            foreach (var (node, _) in childNodes.OrderByDescending(c => c.totalQty))
            {
                catNode.Children.Add(node);
            }
        }

        /// <summary>
        /// Counts distinct items across all categories (DefGroups + single-variant ItemGroups).
        /// </summary>
        private static int CountDistinctItems(List<TreeNode> nodes)
        {
            int count = 0;
            foreach (TreeNode node in nodes)
            {
                if (node.Type == TreeNode.NodeType.DefGroup || node.Type == TreeNode.NodeType.ItemGroup)
                {
                    count++;
                }
                else if (node.Type == TreeNode.NodeType.Category && node.Children.Count > 0)
                {
                    count += CountDistinctItems(node.Children);
                }
            }
            return count;
        }

        /// <summary>
        /// Builds a standard ItemGroup node with Stack children.
        /// Used for single-variant items and for meal variants within a meal DefGroup.
        /// </summary>
        private static TreeNode BuildItemGroupNode(InventoryHelper.InventoryItem item, TreeNode parent, int depth)
        {
            TreeNode itemGroupNode = new TreeNode
            {
                Type = TreeNode.NodeType.ItemGroup,
                Label = item.GetDisplayLabel(),
                Depth = depth,
                ItemData = item,
                Parent = parent,
                CanExpand = true
            };

            foreach (InventoryHelper.InventoryStack stack in item.Stacks)
            {
                TreeNode stackNode = new TreeNode
                {
                    Type = TreeNode.NodeType.Stack,
                    Label = BuildStackLabel(item, stack),
                    Depth = depth + 1,
                    StackData = stack,
                    ItemData = item,
                    Parent = itemGroupNode,
                    CanExpand = false
                };
                itemGroupNode.Children.Add(stackNode);
            }

            return itemGroupNode;
        }

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

        /// <summary>
        /// Rebuilds the flattened list of visible nodes based on expansion states
        /// </summary>
        private static void RebuildFlattenedList()
        {
            flattenedVisibleNodes.Clear();
            AddVisibleNodes(rootNodes);

            // Clamp selection index
            if (selectedIndex >= flattenedVisibleNodes.Count)
            {
                selectedIndex = Math.Max(0, flattenedVisibleNodes.Count - 1);
            }
        }

        /// <summary>
        /// Recursively adds visible nodes to the flattened list
        /// </summary>
        private static void AddVisibleNodes(List<TreeNode> nodes)
        {
            foreach (TreeNode node in nodes)
            {
                flattenedVisibleNodes.Add(node);

                if (node.IsExpanded && node.Children.Count > 0)
                {
                    AddVisibleNodes(node.Children);
                }
            }
        }

        /// <summary>
        /// Closes the inventory menu
        /// </summary>
        public static void Close()
        {
            isActive = false;
            rootNodes.Clear();
            flattenedVisibleNodes.Clear();
            selectedIndex = 0;
            lastChildPerParent.Clear();
            MenuHelper.ResetLevel("Inventory");
            typeahead.ClearSearch();

            TolkHelper.Speak("Inventory menu closed.");
            SoundDefOf.TabClose.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Handles keyboard input for the inventory menu
        /// Returns true if the input was handled
        /// </summary>
        public static bool HandleInput(Event ev)
        {
            if (!isActive) return false;

            KeyCode key = ev.keyCode;

            // Home - jump to first (Ctrl+Home for absolute first)
            if (key == KeyCode.Home)
            {
                ev.Use();
                if (ev.control)
                    JumpToAbsoluteFirst();
                else
                    JumpToFirst();
                return true;
            }

            // End - jump to last (Ctrl+End for absolute last)
            if (key == KeyCode.End)
            {
                ev.Use();
                if (ev.control)
                    JumpToAbsoluteLast();
                else
                    JumpToLast();
                return true;
            }

            // Handle Escape - clear search FIRST, then close
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    ev.Use();
                    return true;
                }
                ev.Use();
                Close();
                return true;
            }

            // Handle Backspace for search
            if (key == KeyCode.Backspace && typeahead.HasActiveSearch)
            {
                var labels = GetItemLabels();
                if (typeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0)
                        selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
                ev.Use();
                return true;
            }

            // Up arrow - navigate with search awareness
            if (key == KeyCode.UpArrow)
            {
                ev.Use();
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    int prevIndex = typeahead.GetPreviousMatch(selectedIndex);
                    if (prevIndex >= 0)
                    {
                        selectedIndex = prevIndex;
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    SelectPrevious();
                }
                return true;
            }

            // Down arrow - navigate with search awareness
            if (key == KeyCode.DownArrow)
            {
                ev.Use();
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    int nextIndex = typeahead.GetNextMatch(selectedIndex);
                    if (nextIndex >= 0)
                    {
                        selectedIndex = nextIndex;
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    SelectNext();
                }
                return true;
            }

            // Right arrow - expand current node
            if (key == KeyCode.RightArrow)
            {
                ev.Use();
                ExpandCurrent();
                return true;
            }

            // Left arrow - collapse current node
            if (key == KeyCode.LeftArrow)
            {
                ev.Use();
                CollapseCurrent();
                return true;
            }

            // Handle * key - expand all categories (not ItemGroups or Stacks)
            bool isStar = key == KeyCode.KeypadMultiply || (ev.shift && key == KeyCode.Alpha8);
            if (isStar)
            {
                ExpandAllCategories();
                ev.Use();
                return true;
            }

            // Right bracket - open context menu for current stack or item group
            if (key == KeyCode.RightBracket)
            {
                ev.Use();
                OpenContextMenu();
                return true;
            }

            // Delete key - drop carried item (works on Stack nodes)
            if (key == KeyCode.Delete)
            {
                ev.Use();
                HandleDeleteKey();
                return true;
            }

            // Alt+J - Jump to item/pawn based on context
            if (ev.alt && key == KeyCode.J)
            {
                ev.Use();
                HandleJumpKey();
                return true;
            }

            // Enter - activate current node
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                ev.Use();
                ActivateCurrent();
                return true;
            }

            // Alt+I - open info card for current item
            if (ev.alt && key == KeyCode.I)
            {
                ev.Use();
                var item = GetCurrentOrParentItem();
                if (item != null && item.Def != null)
                {
                    InfoCardState.OpenInfoCardForDef(item.Def);
                }
                else
                {
                    TolkHelper.Speak("No info card available");
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }
                return true;
            }

            // Handle typeahead characters
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            if ((isLetter || isNumber) && !ev.alt)
            {
                char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                var labels = GetItemLabels();
                if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
                {
                    if (newIndex >= 0)
                    {
                        selectedIndex = newIndex;
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
                }
                ev.Use();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets whether typeahead search is active.
        /// </summary>
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        /// <summary>
        /// Helper method that clears typeahead search, plays tick sound, and announces the current selection.
        /// Used as callback for MenuHelper tree navigation methods.
        /// </summary>
        private static void ClearAndAnnounce()
        {
            typeahead.ClearSearch();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Jumps to the first sibling at the same indent level (Home key).
        /// </summary>
        private static void JumpToFirst()
        {
            if (flattenedVisibleNodes == null || flattenedVisibleNodes.Count == 0) return;
            MenuHelper.HandleTreeHomeKey(flattenedVisibleNodes, ref selectedIndex, item => item.Depth, false, ClearAndAnnounce);
        }

        /// <summary>
        /// Jumps to the last item in the current scope (End key).
        /// </summary>
        private static void JumpToLast()
        {
            if (flattenedVisibleNodes == null || flattenedVisibleNodes.Count == 0) return;
            MenuHelper.HandleTreeEndKey(flattenedVisibleNodes, ref selectedIndex, item => item.Depth,
                item => item.IsExpanded, item => item.Children != null && item.Children.Count > 0, false, ClearAndAnnounce);
        }

        /// <summary>
        /// Jumps to the absolute first item in the entire tree (Ctrl+Home).
        /// </summary>
        private static void JumpToAbsoluteFirst()
        {
            if (flattenedVisibleNodes == null || flattenedVisibleNodes.Count == 0) return;
            MenuHelper.HandleTreeHomeKey(flattenedVisibleNodes, ref selectedIndex, item => item.Depth, true, ClearAndAnnounce);
        }

        /// <summary>
        /// Jumps to the absolute last item in the entire tree (Ctrl+End).
        /// </summary>
        private static void JumpToAbsoluteLast()
        {
            if (flattenedVisibleNodes == null || flattenedVisibleNodes.Count == 0) return;
            MenuHelper.HandleTreeEndKey(flattenedVisibleNodes, ref selectedIndex, item => item.Depth,
                item => item.IsExpanded, item => item.Children != null && item.Children.Count > 0, true, ClearAndAnnounce);
        }

        /// <summary>
        /// Gets the list of labels for all visible items.
        /// </summary>
        private static List<string> GetItemLabels()
        {
            var labels = new List<string>();
            foreach (var node in flattenedVisibleNodes)
            {
                labels.Add(node.Label ?? "");
            }
            return labels;
        }

        /// <summary>
        /// Gets the InventoryItem for the current selection (ItemGroup, Stack, or their parent).
        /// Returns null if the selection is not related to an inventory item.
        /// </summary>
        private static InventoryHelper.InventoryItem GetCurrentOrParentItem()
        {
            if (flattenedVisibleNodes.Count == 0 || selectedIndex >= flattenedVisibleNodes.Count)
                return null;

            TreeNode current = flattenedVisibleNodes[selectedIndex];

            if (current.Type == TreeNode.NodeType.ItemGroup && current.ItemData != null)
                return current.ItemData;

            if (current.Type == TreeNode.NodeType.Stack && current.ItemData != null)
                return current.ItemData;

            return null;
        }

        /// <summary>
        /// Gets the InventoryStack for the current selection if it's a Stack node.
        /// Returns null otherwise.
        /// </summary>
        private static InventoryHelper.InventoryStack GetCurrentStack()
        {
            if (flattenedVisibleNodes.Count == 0 || selectedIndex >= flattenedVisibleNodes.Count)
                return null;

            TreeNode current = flattenedVisibleNodes[selectedIndex];

            if (current.Type == TreeNode.NodeType.Stack && current.StackData != null)
                return current.StackData;

            return null;
        }

        /// <summary>
        /// Opens a context menu for the current stack or item group via WindowlessFloatMenuState.
        /// </summary>
        private static void OpenContextMenu()
        {
            if (flattenedVisibleNodes.Count == 0) return;

            TreeNode current = flattenedVisibleNodes[selectedIndex];

            if (current.Type == TreeNode.NodeType.Stack && current.StackData != null)
            {
                OpenStackContextMenu(current.StackData, current.ItemData);
                return;
            }

            if (current.Type == TreeNode.NodeType.ItemGroup && current.ItemData != null)
            {
                OpenItemGroupContextMenu(current.ItemData);
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
        /// Handles the Delete key - drops carried items from specific stacks.
        /// </summary>
        private static void HandleDeleteKey()
        {
            var stack = GetCurrentStack();
            if (stack != null)
            {
                if (stack.IsCarried)
                {
                    DropStack(stack);
                }
                else
                {
                    TolkHelper.Speak("This item is in storage, not carried by a pawn.");
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }
                return;
            }

            TreeNode current = flattenedVisibleNodes[selectedIndex];
            if (current.Type == TreeNode.NodeType.ItemGroup && current.ItemData != null)
            {
                if (current.ItemData.HasCarriedStacks)
                {
                    TolkHelper.Speak("Expand this item to select a specific stack to drop.");
                }
                else
                {
                    TolkHelper.Speak("This item is in storage, not carried by a pawn.");
                }
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            if (current.Type == TreeNode.NodeType.DefGroup || current.Type == TreeNode.NodeType.MaterialGroup)
            {
                TolkHelper.Speak("Expand this group and select a specific stack to drop.");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            TolkHelper.Speak("Select a carried item to drop.");
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Handles the Alt+J key - jumps to stack location or first stack of item group.
        /// </summary>
        private static void HandleJumpKey()
        {
            typeahead.ClearSearch();

            // Stack-level: jump to that specific stack's location
            var stack = GetCurrentStack();
            if (stack != null)
            {
                JumpToStack(stack);
                return;
            }

            TreeNode current = flattenedVisibleNodes[selectedIndex];

            // ItemGroup-level: jump to first available stack location
            if (current.Type == TreeNode.NodeType.ItemGroup && current.ItemData != null)
            {
                var firstStack = current.ItemData.Stacks.FirstOrDefault();
                if (firstStack != null)
                {
                    JumpToStack(firstStack);
                    return;
                }
            }

            TolkHelper.Speak("Select an item to jump to.");
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Announces the current selection with search context if applicable.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (flattenedVisibleNodes.Count == 0)
            {
                TolkHelper.Speak("Inventory is empty.");
                return;
            }

            TreeNode current = flattenedVisibleNodes[selectedIndex];

            // Get state info (only for expandable nodes)
            string stateInfo = "";
            if (current.CanExpand)
            {
                stateInfo = current.IsExpanded ? "expanded" : "collapsed";
            }

            // Get sibling position
            var (position, total) = GetSiblingPosition(current);
            string positionInfo = $"{position} of {total}";

            // Build announcement with search context
            string announcement;
            if (string.IsNullOrEmpty(stateInfo))
            {
                announcement = $"{current.Label}";
            }
            else
            {
                announcement = $"{current.Label} {stateInfo}";
            }

            if (typeahead.HasActiveSearch)
            {
                announcement += $", {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'";
            }
            else
            {
                announcement += $". {positionInfo}";
            }

            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            TolkHelper.Speak(announcement.Trim());
        }

        /// <summary>
        /// Selects the previous item in the list
        /// </summary>
        private static void SelectPrevious()
        {
            if (flattenedVisibleNodes.Count == 0) return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, flattenedVisibleNodes.Count);

            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Selects the next item in the list
        /// </summary>
        private static void SelectNext()
        {
            if (flattenedVisibleNodes.Count == 0) return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, flattenedVisibleNodes.Count);

            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Expands the currently selected node (WCAG-compliant)
        /// - If collapsed and expandable: expand, focus stays on current item
        /// - If already expanded with children: move to first child
        /// - If end node (not expandable): reject feedback
        /// </summary>
        private static void ExpandCurrent()
        {
            if (flattenedVisibleNodes.Count == 0) return;

            // Clear search when expanding to avoid "no more search results" confusion
            typeahead.ClearSearch();

            TreeNode current = flattenedVisibleNodes[selectedIndex];

            if (!current.CanExpand)
            {
                // End node - provide reject feedback
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Cannot expand this item.", SpeechPriority.High);
                return;
            }

            if (!current.IsExpanded)
            {
                // Collapsed node - expand it, focus stays on current item
                current.IsExpanded = true;
                RebuildFlattenedList();
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentSelection();
            }
            else
            {
                // Already expanded - move to first child
                MoveToFirstChild();
            }
        }

        /// <summary>
        /// Moves focus to the first child of the current expanded item
        /// </summary>
        private static void MoveToFirstChild()
        {
            TreeNode current = flattenedVisibleNodes[selectedIndex];

            if (current.Children.Count > 0)
            {
                int firstChildIndex = flattenedVisibleNodes.IndexOf(current) + 1;
                if (firstChildIndex < flattenedVisibleNodes.Count)
                {
                    selectedIndex = firstChildIndex;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                }
            }
        }

        /// <summary>
        /// Expands all category nodes in the entire tree recursively.
        /// Does NOT expand ItemGroup or Stack nodes.
        /// </summary>
        public static void ExpandAllCategories()
        {
            if (rootNodes.Count == 0)
            {
                TolkHelper.Speak("No categories to expand.");
                return;
            }

            int expandedCount = ExpandCategoriesRecursively(rootNodes);

            if (expandedCount > 0)
            {
                RebuildFlattenedList();
                typeahead.ClearSearch();
                SoundDefOf.Click.PlayOneShotOnCamera();
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
        /// Recursively expands all category nodes but not ItemGroup or Stack nodes.
        /// Only recurses into Category children since ItemGroups can't contain categories.
        /// </summary>
        private static int ExpandCategoriesRecursively(List<TreeNode> nodes)
        {
            int expandedCount = 0;

            foreach (TreeNode node in nodes)
            {
                if (node.Type == TreeNode.NodeType.Category && node.CanExpand && !node.IsExpanded)
                {
                    node.IsExpanded = true;
                    expandedCount++;
                }

                // Only recurse into Category children
                if (node.Type == TreeNode.NodeType.Category && node.Children.Count > 0)
                {
                    expandedCount += ExpandCategoriesRecursively(node.Children);
                }
            }

            return expandedCount;
        }

        /// <summary>
        /// Collapses the currently selected node (WCAG-compliant)
        /// - If expanded: collapse, focus stays on current item
        /// - If collapsed or end node: move to parent WITHOUT collapsing it
        /// </summary>
        private static void CollapseCurrent()
        {
            if (flattenedVisibleNodes.Count == 0) return;

            // Clear search when collapsing to avoid "no more search results" confusion
            typeahead.ClearSearch();

            TreeNode current = flattenedVisibleNodes[selectedIndex];

            if (current.CanExpand && current.IsExpanded)
            {
                // Currently expanded - collapse it, focus stays on current item
                current.IsExpanded = false;
                RebuildFlattenedList();
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentSelection();
            }
            else if (current.Parent != null)
            {
                // Collapsed or end node - move to parent WITHOUT collapsing it
                lastChildPerParent[current.Parent] = current;

                selectedIndex = flattenedVisibleNodes.IndexOf(current.Parent);
                if (selectedIndex < 0) selectedIndex = 0;

                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceCurrentSelection();
            }
            else
            {
                // Already at top level with no parent
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Already at top level.", SpeechPriority.High);
            }
        }

        /// <summary>
        /// Activates the currently selected node
        /// </summary>
        private static void ActivateCurrent()
        {
            if (flattenedVisibleNodes.Count == 0) return;

            TreeNode current = flattenedVisibleNodes[selectedIndex];

            // If it's a category or item group, toggle expansion
            if (current.CanExpand)
            {
                if (current.IsExpanded)
                {
                    CollapseCurrent();
                }
                else
                {
                    ExpandCurrent();
                }
            }
            else if (current.Type == TreeNode.NodeType.Stack)
            {
                // Stack nodes: open context menu on Enter
                OpenContextMenu();
            }
            else
            {
                TolkHelper.Speak("This item has no actions.");
            }
        }

        /// <summary>
        /// Gets the sibling position (1-based) and total siblings for a node
        /// </summary>
        private static (int position, int total) GetSiblingPosition(TreeNode item)
        {
            List<TreeNode> siblings = item.Parent != null ? item.Parent.Children : rootNodes;
            int position = siblings.IndexOf(item) + 1;
            return (position, siblings.Count);
        }

        /// <summary>
        /// Announces the currently selected item via screen reader
        /// </summary>
        private static void AnnounceCurrentSelection()
        {
            if (flattenedVisibleNodes.Count == 0)
            {
                TolkHelper.Speak("Inventory is empty.");
                return;
            }

            TreeNode current = flattenedVisibleNodes[selectedIndex];

            // Get state info (only for expandable nodes)
            string stateInfo = "";
            if (current.CanExpand)
            {
                stateInfo = current.IsExpanded ? " expanded" : " collapsed";
            }

            // Get sibling position
            var (position, total) = GetSiblingPosition(current);

            // Build announcement
            string levelSuffix = MenuHelper.GetLevelSuffix("Inventory", current.Depth);
            string positionPart = MenuHelper.FormatPosition(position - 1, total);
            string positionSection = string.IsNullOrEmpty(positionPart) ? "." : $". {positionPart}.";
            string announcement = $"{current.Label}{stateInfo}{positionSection}{levelSuffix}";

            TolkHelper.Speak(announcement.Trim());
        }

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
        /// Gets a unique key for a node to track expansion state across refreshes.
        /// </summary>
        private static string GetNodeKey(TreeNode node)
        {
            if (node.Type == TreeNode.NodeType.Category && node.CategoryData != null)
            {
                if (node.CategoryData.CategoryDef == null)
                    return "cat:uncategorized";
                return "cat:" + node.CategoryData.CategoryDef.defName;
            }

            if (node.Type == TreeNode.NodeType.DefGroup && node.DefGroupDef != null)
            {
                // Distinguish meal tier DefGroups from material/quality DefGroups
                if (HasMealTierPreferability(node.DefGroupDef) && node.DefGroupItems != null
                    && node.DefGroupItems.Any(i => i.Def != node.DefGroupDef))
                {
                    return "defgroup:meal:" + node.DefGroupDef.ingestible.preferability.ToString();
                }
                return "defgroup:" + node.DefGroupDef.defName;
            }

            if (node.Type == TreeNode.NodeType.MaterialGroup && node.DefGroupDef != null)
            {
                string key = "matgroup:" + node.DefGroupDef.defName;
                if (node.DefGroupStuff != null)
                    key += ":" + node.DefGroupStuff.defName;
                return key;
            }

            if (node.Type == TreeNode.NodeType.ItemGroup && node.ItemData != null)
            {
                string key = "itemgroup:" + node.ItemData.Def.defName;
                if (node.ItemData.Stuff != null)
                    key += ":" + node.ItemData.Stuff.defName;
                if (node.ItemData.Quality.HasValue)
                    key += ":" + node.ItemData.Quality.Value.ToString();
                return key;
            }

            if (node.Type == TreeNode.NodeType.Stack && node.StackData != null)
            {
                return "stack:" + node.StackData.Thing.ThingID;
            }

            return node.Label;
        }

        /// <summary>
        /// Saves the expansion state of all nodes in the tree.
        /// </summary>
        private static void SaveExpansionState(List<TreeNode> nodes, Dictionary<string, bool> state)
        {
            foreach (TreeNode node in nodes)
            {
                string key = GetNodeKey(node);
                if (!string.IsNullOrEmpty(key))
                {
                    state[key] = node.IsExpanded;
                }
                if (node.Children.Count > 0)
                {
                    SaveExpansionState(node.Children, state);
                }
            }
        }

        /// <summary>
        /// Restores the expansion state of nodes from a previously saved state.
        /// </summary>
        private static void RestoreExpansionState(List<TreeNode> nodes, Dictionary<string, bool> state)
        {
            foreach (TreeNode node in nodes)
            {
                string key = GetNodeKey(node);
                if (!string.IsNullOrEmpty(key) && state.TryGetValue(key, out bool wasExpanded))
                {
                    node.IsExpanded = wasExpanded;
                }
                if (node.Children.Count > 0)
                {
                    RestoreExpansionState(node.Children, state);
                }
            }
        }

        /// <summary>
        /// Tries to restore cursor position to the old label, or clamps to valid range.
        /// </summary>
        private static void RestoreCursorPosition(string oldLabel)
        {
            if (string.IsNullOrEmpty(oldLabel) || flattenedVisibleNodes.Count == 0)
            {
                selectedIndex = 0;
                return;
            }

            for (int i = 0; i < flattenedVisibleNodes.Count; i++)
            {
                if (flattenedVisibleNodes[i].Label == oldLabel)
                {
                    selectedIndex = i;
                    return;
                }
            }

            if (selectedIndex >= flattenedVisibleNodes.Count)
            {
                selectedIndex = Math.Max(0, flattenedVisibleNodes.Count - 1);
            }
        }

        /// <summary>
        /// Refreshes the inventory menu without closing it.
        /// Preserves expansion state and selection position.
        /// </summary>
        private static void RefreshInventory()
        {
            Dictionary<string, bool> expansionState = new Dictionary<string, bool>();
            SaveExpansionState(rootNodes, expansionState);

            string oldLabel = (selectedIndex < flattenedVisibleNodes.Count)
                ? flattenedVisibleNodes[selectedIndex].Label : null;

            // Recollect all items with unified aggregation
            List<Thing> allStoredItems = InventoryHelper.GetAllStoredItems();
            Dictionary<Thing, Pawn> pawnCarriedThings = InventoryHelper.GetAllPawnCarriedItems();
            List<InventoryHelper.InventoryItem> allItems = InventoryHelper.AggregateAllItems(allStoredItems, pawnCarriedThings);
            List<InventoryHelper.CategoryNode> categoryTree = InventoryHelper.BuildCategoryTree(allItems);
            rootNodes = BuildTreeNodes(categoryTree);

            RestoreExpansionState(rootNodes, expansionState);
            RebuildFlattenedList();
            typeahead.ClearSearch();
            RestoreCursorPosition(oldLabel);
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

        /// <summary>
        /// Draws visual highlights for the selected item (for sighted users)
        /// </summary>
        public static void DrawHighlight()
        {
            if (!isActive || flattenedVisibleNodes.Count == 0) return;

            TreeNode current = flattenedVisibleNodes[selectedIndex];

            // Calculate position for highlight
            float lineHeight = 24f;
            float yOffset = selectedIndex * lineHeight;
            float xOffset = 20f;
            float width = 600f;
            float height = lineHeight;

            Rect highlightRect = new Rect(xOffset, yOffset + 100f, width, height);

            // Draw semi-transparent highlight
            Color highlightColor = new Color(1f, 1f, 0f, 0.3f);
            Widgets.DrawBoxSolid(highlightRect, highlightColor);

            // Draw label
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(highlightRect, current.GetDisplayString());
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
