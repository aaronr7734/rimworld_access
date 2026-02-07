using System;
using System.Collections.Generic;
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
        /// Represents a node in the inventory tree (category, item, or action)
        /// </summary>
        public class TreeNode
        {
            public enum NodeType
            {
                Category,      // A ThingCategoryDef
                Item,          // An aggregated inventory item
                Action         // An action (Jump to location, View details)
            }

            public NodeType Type { get; set; }
            public string Label { get; set; }
            public int Depth { get; set; }
            public bool IsExpanded { get; set; }
            public bool CanExpand { get; set; }

            // For Category nodes
            public InventoryHelper.CategoryNode CategoryData { get; set; }

            // For Item nodes
            public InventoryHelper.InventoryItem ItemData { get; set; }

            // For Action nodes
            public Action OnActivate { get; set; }

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

            // Collect all stored items
            List<Thing> allStoredItems = InventoryHelper.GetAllStoredItems();

            // Collect pawn-carried items
            Dictionary<Thing, Pawn> pawnCarriedThings = InventoryHelper.GetAllPawnCarriedItems();
            List<InventoryHelper.InventoryItem> pawnCarriedItems = InventoryHelper.AggregatePawnCarriedItems(pawnCarriedThings);

            if (allStoredItems.Count == 0 && pawnCarriedItems.Count == 0)
            {
                TolkHelper.Speak("Inventory menu opened. No items in colony storage or carried by pawns.");
                rootNodes = new List<TreeNode>();
                flattenedVisibleNodes = new List<TreeNode>();
                SoundDefOf.TabOpen.PlayOneShotOnCamera();
                return;
            }

            // Aggregate storage items by type (now groups by ThingDef + Stuff + Quality)
            List<InventoryHelper.InventoryItem> aggregatedStorageItems = InventoryHelper.AggregateStacks(allStoredItems);

            // Build category tree (includes both storage and pawn-carried items)
            List<InventoryHelper.CategoryNode> categoryTree = InventoryHelper.BuildCategoryTree(aggregatedStorageItems, pawnCarriedItems);

            // Convert to TreeNode structure
            rootNodes = BuildTreeNodes(categoryTree);

            // Flatten to get initially visible nodes
            RebuildFlattenedList();

            // Announce opening with both storage and carried item counts
            int totalItemTypes = aggregatedStorageItems.Count + pawnCarriedItems.Count;
            string announcement;
            if (pawnCarriedItems.Count > 0)
            {
                announcement = $"Colony inventory opened. {aggregatedStorageItems.Count} item types in storage, {pawnCarriedItems.Count} carried by pawns. {rootNodes.Count} categories.";
            }
            else
            {
                announcement = $"Colony inventory opened. {aggregatedStorageItems.Count} item types in storage. {rootNodes.Count} categories.";
            }
            TolkHelper.Speak(announcement);
            SoundDefOf.TabOpen.PlayOneShotOnCamera();

            // Announce first selection
            if (flattenedVisibleNodes.Count > 0)
            {
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Converts InventoryHelper.CategoryNode tree to TreeNode tree
        /// </summary>
        private static List<TreeNode> BuildTreeNodes(List<InventoryHelper.CategoryNode> categoryNodes, TreeNode parent = null, int depth = 0)
        {
            List<TreeNode> nodes = new List<TreeNode>();

            foreach (InventoryHelper.CategoryNode categoryNode in categoryNodes)
            {
                // Create category node
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

                // Add items as children
                foreach (InventoryHelper.InventoryItem item in categoryNode.Items)
                {
                    TreeNode itemNode = new TreeNode
                    {
                        Type = TreeNode.NodeType.Item,
                        Label = item.GetDisplayLabel(),
                        Depth = depth + 1,
                        ItemData = item,
                        Parent = catNode,
                        CanExpand = true // Items can expand to show actions
                    };

                    // Build action children
                    itemNode.Children.AddRange(BuildItemActionNodes(item, itemNode, depth + 2));

                    catNode.Children.Add(itemNode);
                }

                nodes.Add(catNode);
            }

            return nodes;
        }

        /// <summary>
        /// Builds action nodes for an inventory item
        /// </summary>
        private static List<TreeNode> BuildItemActionNodes(InventoryHelper.InventoryItem item, TreeNode parent, int depth)
        {
            List<TreeNode> actions = new List<TreeNode>();

            if (item.IsCarried)
            {
                // Actions for pawn-carried items
                // NO "Install at" - must drop first to access gizmos (game parity)

                // Action: Jump to pawn
                TreeNode jumpToPawnAction = new TreeNode
                {
                    Type = TreeNode.NodeType.Action,
                    Label = "Jump to pawn (Alt+J)",
                    Depth = depth,
                    Parent = parent,
                    CanExpand = false,
                    OnActivate = () => JumpToCarrier(item)
                };
                actions.Add(jumpToPawnAction);

                // Action: Drop item
                if (item.Things.Count > 0)
                {
                    TreeNode dropAction = new TreeNode
                    {
                        Type = TreeNode.NodeType.Action,
                        Label = "Drop (Delete)",
                        Depth = depth,
                        Parent = parent,
                        CanExpand = false,
                        OnActivate = () => DropCarriedItem(item)
                    };
                    actions.Add(dropAction);
                }
            }
            else
            {
                // Actions for storage items

                // Action: Install at (FIRST for minified furniture)
                if (item.IsMinifiedThing && item.Things.Count > 0)
                {
                    TreeNode installAction = new TreeNode
                    {
                        Type = TreeNode.NodeType.Action,
                        Label = "Install at",
                        Depth = depth,
                        Parent = parent,
                        CanExpand = false,
                        OnActivate = () => InstallItem(item)
                    };
                    actions.Add(installAction);
                }

                // Action: Jump to location
                if (item.StorageLocations.Count > 0)
                {
                    TreeNode jumpAction = new TreeNode
                    {
                        Type = TreeNode.NodeType.Action,
                        Label = "Jump to location (Alt+J)",
                        Depth = depth,
                        Parent = parent,
                        CanExpand = false,
                        OnActivate = () => JumpToItem(item)
                    };
                    actions.Add(jumpAction);
                }
            }

            // Action: View details (available for both carried and storage items)
            TreeNode detailsAction = new TreeNode
            {
                Type = TreeNode.NodeType.Action,
                Label = "View details",
                Depth = depth,
                Parent = parent,
                CanExpand = false,
                OnActivate = () => ViewItemDetails(item)
            };
            actions.Add(detailsAction);

            return actions;
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
                    // Navigate through matches only when there ARE matches
                    int prevIndex = typeahead.GetPreviousMatch(selectedIndex);
                    if (prevIndex >= 0)
                    {
                        selectedIndex = prevIndex;
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    // Navigate normally (either no search active, OR search with no matches)
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
                    // Navigate through matches only when there ARE matches
                    int nextIndex = typeahead.GetNextMatch(selectedIndex);
                    if (nextIndex >= 0)
                    {
                        selectedIndex = nextIndex;
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    // Navigate normally (either no search active, OR search with no matches)
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

            // Handle * key - expand all categories (except items) for quick searching
            bool isStar = key == KeyCode.KeypadMultiply || (ev.shift && key == KeyCode.Alpha8);
            if (isStar)
            {
                ExpandAllCategories();
                ev.Use();
                return true;
            }

            // Delete key - drop carried item without closing menu (works on item node or action children)
            if (key == KeyCode.Delete)
            {
                ev.Use();
                InventoryHelper.InventoryItem item = GetCurrentOrParentItem();
                if (item != null && item.IsCarried)
                {
                    TryDropItem(item);
                }
                else if (item != null && !item.IsCarried)
                {
                    TolkHelper.Speak("This item is in storage, not carried by a pawn.");
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }
                else
                {
                    TolkHelper.Speak("Select a carried item to drop.");
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }
                return true;
            }

            // Alt+J - Jump to item/pawn based on context (works on item node or action children)
            if (ev.alt && key == KeyCode.J)
            {
                ev.Use();
                InventoryHelper.InventoryItem item = GetCurrentOrParentItem();
                if (item == null)
                {
                    TolkHelper.Speak("Select an item to jump to.");
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return true;
                }

                // Clear typeahead before jumping
                typeahead.ClearSearch();

                if (item.IsCarried)
                {
                    JumpToCarrier(item);  // Already closes inventory
                }
                else
                {
                    JumpToItem(item);     // Already closes inventory
                }
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
            // Use KeyCode instead of Event.current.character (which is empty in Unity IMGUI)
            // Skip if Alt is held - Alt+key combos are shortcuts, not search input
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
        /// If on an expanded node, jumps to its last visible descendant.
        /// Otherwise, jumps to last sibling at same level.
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
        /// Gets the InventoryItem for the current selection, whether it's an Item node or an Action child.
        /// Returns null if the selection is not related to an inventory item.
        /// </summary>
        private static InventoryHelper.InventoryItem GetCurrentOrParentItem()
        {
            if (flattenedVisibleNodes.Count == 0 || selectedIndex >= flattenedVisibleNodes.Count)
                return null;

            TreeNode current = flattenedVisibleNodes[selectedIndex];

            // If on an Item node, return its ItemData
            if (current.Type == TreeNode.NodeType.Item && current.ItemData != null)
                return current.ItemData;

            // If on an Action node, return parent's ItemData
            if (current.Type == TreeNode.NodeType.Action && current.Parent?.ItemData != null)
                return current.Parent.ItemData;

            return null;
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
                // Focus stays on current item, just announce the state change
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
        /// Does NOT expand item nodes (so their actions stay hidden).
        /// This allows quick searching through the entire inventory.
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
                typeahead.ClearSearch(); // Clear search since visible items changed
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
        /// Recursively expands all category nodes but not item nodes.
        /// Returns the count of newly expanded nodes.
        /// </summary>
        private static int ExpandCategoriesRecursively(List<TreeNode> nodes)
        {
            int expandedCount = 0;

            foreach (TreeNode node in nodes)
            {
                // Only expand Category nodes, not Item nodes
                // (Item nodes expand to show actions - we want those to stay collapsed)
                if (node.Type == TreeNode.NodeType.Category && node.CanExpand && !node.IsExpanded)
                {
                    node.IsExpanded = true;
                    expandedCount++;
                }

                // Recurse into children (whether currently visible or not)
                if (node.Children.Count > 0)
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
                // Save current child position for potential future restoration
                lastChildPerParent[current.Parent] = current;

                // Move focus to parent (do NOT collapse it)
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

            // If it's an action, execute it
            if (current.Type == TreeNode.NodeType.Action && current.OnActivate != null)
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                current.OnActivate();
                return;
            }

            // If it's a category or item, toggle expansion
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
        /// Format: "level N. {name} {state}. {X of Y}." or "{name} {state}. {X of Y}."
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

            // Build announcement: "{name} {state}. {X of Y}. level N"
            string levelSuffix = MenuHelper.GetLevelSuffix("Inventory", current.Depth);
            string positionPart = MenuHelper.FormatPosition(position - 1, total);
            string positionSection = string.IsNullOrEmpty(positionPart) ? "." : $". {positionPart}.";
            string announcement = $"{current.Label}{stateInfo}{positionSection}{levelSuffix}";

            TolkHelper.Speak(announcement.Trim());
        }

        /// <summary>
        /// Jumps the camera and cursor to the location of an item
        /// </summary>
        private static void JumpToItem(InventoryHelper.InventoryItem item)
        {
            if (item.StorageLocations.Count == 0)
            {
                TolkHelper.Speak("No storage location found for this item.");
                return;
            }

            IntVec3 location = item.StorageLocations[0];

            // Jump camera
            if (Find.CameraDriver != null)
            {
                Find.CameraDriver.JumpToCurrentMapLoc(location);
            }

            // Update map cursor if initialized
            if (MapNavigationState.IsInitialized)
            {
                MapNavigationState.CurrentCursorPosition = location;
            }

            TolkHelper.Speak($"Jumped to {item.Def.LabelCap} storage location at {location}.");
            SoundDefOf.Click.PlayOneShotOnCamera();

            // Close the inventory menu after jumping
            Close();
        }

        /// <summary>
        /// Jumps the camera and cursor to the carrier pawn
        /// </summary>
        private static void JumpToCarrier(InventoryHelper.InventoryItem item)
        {
            Pawn carrier = item.CarrierPawn;
            if (carrier == null || carrier.Destroyed || carrier.Dead)
            {
                TolkHelper.Speak("Carrier pawn no longer available.");
                return;
            }

            // Jump camera to carrier's position
            if (Find.CameraDriver != null)
            {
                Find.CameraDriver.JumpToCurrentMapLoc(carrier.Position);
            }

            // Update map cursor if initialized
            if (MapNavigationState.IsInitialized)
            {
                MapNavigationState.CurrentCursorPosition = carrier.Position;
            }

            TolkHelper.Speak($"Jumped to {carrier.LabelShort} at {carrier.Position}.");
            SoundDefOf.Click.PlayOneShotOnCamera();

            // Close the inventory menu after jumping
            Close();
        }

        /// <summary>
        /// Drops a carried item from its carrier pawn
        /// </summary>
        private static void DropCarriedItem(InventoryHelper.InventoryItem item)
        {
            if (item.Things.Count == 0)
            {
                TolkHelper.Speak("No item to drop.");
                return;
            }

            Thing thingToDrop = item.Things[0];
            Pawn carrier = item.CarrierPawn;

            if (carrier == null || carrier.Destroyed || carrier.Dead)
            {
                TolkHelper.Speak("Carrier pawn no longer available.");
                return;
            }

            if (carrier.inventory?.innerContainer == null)
            {
                TolkHelper.Speak("Cannot access carrier's inventory.");
                return;
            }

            // Drop the item near the pawn
            if (carrier.inventory.innerContainer.TryDrop(thingToDrop, carrier.Position, carrier.Map, ThingPlaceMode.Near, out Thing droppedThing))
            {
                string carrierName = carrier.LabelShort;
                int totalQuantity = item.TotalQuantity;

                // Refresh the inventory to show updated state (don't close)
                RefreshInventory();

                TolkHelper.Speak($"Dropped {item.Def.LabelCap} x{totalQuantity} from {carrierName}.");
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            else
            {
                TolkHelper.Speak($"Failed to drop {thingToDrop.LabelCap}.");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        /// <summary>
        /// Drops a carried item from its carrier pawn. Does not close the menu.
        /// Returns true if the drop succeeded.
        /// </summary>
        private static bool TryDropItem(InventoryHelper.InventoryItem item)
        {
            if (item == null || item.Things.Count == 0)
            {
                TolkHelper.Speak("No item to drop.");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }

            Thing thingToDrop = item.Things[0];
            Pawn carrier = item.CarrierPawn;

            if (carrier == null || carrier.Destroyed || carrier.Dead)
            {
                TolkHelper.Speak("Carrier pawn no longer available.");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }

            if (carrier.inventory?.innerContainer == null)
            {
                TolkHelper.Speak("Cannot access carrier's inventory.");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }

            // Drop the item near the pawn
            if (carrier.inventory.innerContainer.TryDrop(thingToDrop, carrier.Position, carrier.Map, ThingPlaceMode.Near, out Thing droppedThing))
            {
                string carrierName = carrier.LabelShort;
                int totalQuantity = item.TotalQuantity;

                // Refresh the inventory to show updated state
                RefreshInventory();

                TolkHelper.Speak($"Dropped {item.Def.LabelCap} x{totalQuantity} from {carrierName}.");
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            else
            {
                TolkHelper.Speak($"Failed to drop {thingToDrop.LabelCap}.");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        /// <summary>
        /// Gets a unique key for a node to track expansion state across refreshes.
        /// </summary>
        private static string GetNodeKey(TreeNode node)
        {
            // For categories, use the category def name (stable across refreshes)
            if (node.Type == TreeNode.NodeType.Category && node.CategoryData != null)
            {
                // When CategoryDef is null, return a fixed key for "Uncategorized" category
                if (node.CategoryData.CategoryDef == null)
                    return "cat:uncategorized";
                return "cat:" + node.CategoryData.CategoryDef.defName;
            }
            // For items, use the item def name, stuff, quality, and carrier info
            if (node.Type == TreeNode.NodeType.Item && node.ItemData != null)
            {
                string key = "item:" + node.ItemData.Def.defName;
                // Include stuff (material) in key
                if (node.ItemData.Stuff != null)
                {
                    key += ":" + node.ItemData.Stuff.defName;
                }
                // Include quality in key
                if (node.ItemData.Quality.HasValue)
                {
                    key += ":" + node.ItemData.Quality.Value.ToString();
                }
                // Include carrier for carried items
                if (node.ItemData.IsCarried && node.ItemData.CarrierPawn != null)
                {
                    key += ":carried:" + node.ItemData.CarrierPawn.ThingID;
                }
                return key;
            }
            // Fallback to label
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

            // Try to find exact match
            for (int i = 0; i < flattenedVisibleNodes.Count; i++)
            {
                if (flattenedVisibleNodes[i].Label == oldLabel)
                {
                    selectedIndex = i;
                    return;
                }
            }

            // If not found (item was dropped), stay at current index or clamp
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
            // Save current expansion state by category/item key
            Dictionary<string, bool> expansionState = new Dictionary<string, bool>();
            SaveExpansionState(rootNodes, expansionState);

            // Save current selection label for repositioning
            string oldLabel = (selectedIndex < flattenedVisibleNodes.Count)
                ? flattenedVisibleNodes[selectedIndex].Label : null;

            // Recollect all items
            List<Thing> allStoredItems = InventoryHelper.GetAllStoredItems();
            Dictionary<Thing, Pawn> pawnCarriedThings = InventoryHelper.GetAllPawnCarriedItems();
            List<InventoryHelper.InventoryItem> pawnCarriedItems = InventoryHelper.AggregatePawnCarriedItems(pawnCarriedThings);

            // Aggregate and rebuild tree (groups by ThingDef + Stuff + Quality)
            List<InventoryHelper.InventoryItem> aggregatedStorageItems = InventoryHelper.AggregateStacks(allStoredItems);
            List<InventoryHelper.CategoryNode> categoryTree = InventoryHelper.BuildCategoryTree(aggregatedStorageItems, pawnCarriedItems);
            rootNodes = BuildTreeNodes(categoryTree);

            // Restore expansion state before flattening
            RestoreExpansionState(rootNodes, expansionState);
            RebuildFlattenedList();

            // Clear typeahead since nodes changed (fixes lockup bug)
            typeahead.ClearSearch();

            // Restore cursor position to old selection or clamp
            RestoreCursorPosition(oldLabel);
        }

        /// <summary>
        /// Views detailed information about an item
        /// </summary>
        private static void ViewItemDetails(InventoryHelper.InventoryItem item)
        {
            ThingDef def = item.Def;

            // Build detailed description
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

            // Show carrier info or storage location info
            if (item.IsCarried && item.CarrierPawn != null)
            {
                details.Add($"Carried by: {item.CarrierPawn.LabelShort}");
            }
            else
            {
                details.Add($"Storage locations: {item.StorageLocations.Count}");
            }

            string announcement = string.Join(". ", details);
            TolkHelper.Speak(announcement);
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Initiates installation of a minified furniture item.
        /// Selects the thing and activates the Install designator.
        /// </summary>
        private static void InstallItem(InventoryHelper.InventoryItem item)
        {
            if (!item.IsMinifiedThing || item.Things.Count == 0)
            {
                TolkHelper.Speak("No installable item found.");
                return;
            }

            // Get the first minified thing
            Thing thingToInstall = item.Things[0];
            if (thingToInstall == null || thingToInstall.Destroyed)
            {
                TolkHelper.Speak("Item no longer available.");
                return;
            }

            // Close the inventory menu first
            Close();

            // Select the minified thing (required for Designator_Install to work)
            Find.Selector.ClearSelection();
            Find.Selector.Select(thingToInstall, playSound: false, forceDesignatorDeselect: false);

            // Get or create the install designator
            Designator_Install installDesignator = new Designator_Install();

            // Enter our placement mode to handle keyboard navigation
            // (this also selects the designator and announces placement info)
            ArchitectState.EnterPlacementMode(installDesignator);
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
            Color highlightColor = new Color(1f, 1f, 0f, 0.3f); // Yellow with alpha
            Widgets.DrawBoxSolid(highlightRect, highlightColor);

            // Draw label
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(highlightRect, current.GetDisplayString());
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
