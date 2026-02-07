using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// State management for caravan inspection screen (I key or Enter on world map).
    /// Provides tree view interface for caravan information.
    /// </summary>
    public static class CaravanInspectState
    {
        /// <summary>
        /// Types of nodes in the caravan tree.
        /// </summary>
        public enum NodeType
        {
            Root,           // Invisible root
            Category,       // Main category (Caravan Status, Pawns, Gear, Items)
            SubCategory,    // Sub-category (Colonists, Animals, category names)
            Stat,           // A stat item (Mass: 150 kg)
            Pawn,           // A pawn
            GearItem,       // An equipped gear item
            InventoryItem,  // An inventory item
            WorldObject,    // A world object (caravan, settlement, site) - used by WorldObjectSelectionState
            Action,         // An actionable item - used by WorldObjectSelectionState
            DetailText      // Non-actionable detail text - used by WorldObjectSelectionState
        }

        /// <summary>
        /// Represents a node in the caravan tree.
        /// </summary>
        public class TreeNode
        {
            public NodeType Type { get; set; }
            public string Label { get; set; }
            public string Value { get; set; }           // For stats
            public string Tooltip { get; set; }         // For stat breakdowns (Alt+I opens StatBreakdownState)
            public int Depth { get; set; }
            public bool IsExpanded { get; set; }
            public bool CanExpand { get; set; }
            public object Data { get; set; }            // Pawn, Thing, etc.
            public Pawn OwnerPawn { get; set; }         // For gear items
            public TreeNode Parent { get; set; }
            public List<TreeNode> Children { get; set; }
            public Action OnActivate { get; set; }      // Action when Enter is pressed
            public bool CanAbandon { get; set; }

            public TreeNode()
            {
                Children = new List<TreeNode>();
                IsExpanded = false;
                CanExpand = false;
            }

            public string GetDisplayLabel()
            {
                if (Type == NodeType.Stat && !string.IsNullOrEmpty(Value))
                {
                    return $"{Label}: {Value}";
                }
                if (Type == NodeType.GearItem && OwnerPawn != null)
                {
                    return $"{OwnerPawn.LabelShortCap}'s {Label}";
                }
                return Label;
            }
        }

        private static bool isActive = false;
        private static Caravan currentCaravan = null;
        private static TreeNode rootNode = null;
        private static List<TreeNode> visibleNodes = new List<TreeNode>();
        private static int selectedIndex = 0;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
        private static Dictionary<TreeNode, TreeNode> lastChildPerParent = new Dictionary<TreeNode, TreeNode>();

        // Track caravan contents to detect changes (for auto-refresh after abandon)
        private static int lastKnownPawnCount = 0;
        private static int lastKnownItemCount = 0;

        /// <summary>
        /// Gets whether the caravan inspect screen is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets the current caravan being inspected.
        /// </summary>
        public static Caravan CurrentCaravan => currentCaravan;

        /// <summary>
        /// Gets whether typeahead search is currently active.
        /// </summary>
        public static bool HasActiveTypeahead => typeahead.HasActiveSearch;

        /// <summary>
        /// Builds caravan category nodes (Caravan Status, Pawns, Gear, Items) for a given parent.
        /// Used by WorldObjectSelectionState to embed caravan inspection tree without opening CaravanInspectState.
        /// </summary>
        /// <param name="parent">The parent node to attach categories to</param>
        /// <param name="caravan">The caravan to build categories for</param>
        public static void BuildCaravanCategoriesFor(TreeNode parent, Caravan caravan)
        {
            if (parent == null || caravan == null)
                return;

            // Temporarily set currentCaravan so the Add*Node methods work
            Caravan previousCaravan = currentCaravan;
            currentCaravan = caravan;

            try
            {
                AddCaravanStatusNode(parent);
                AddPawnsNode(parent);
                AddGearNode(parent);
                AddItemsNode(parent);
            }
            finally
            {
                // Restore previous caravan (important if CaravanInspectState is active)
                currentCaravan = previousCaravan;
            }
        }

        /// <summary>
        /// Opens the caravan inspect screen for the specified caravan.
        /// </summary>
        public static void Open(Caravan caravan)
        {
            if (caravan == null)
            {
                TolkHelper.Speak("No caravan specified", SpeechPriority.High);
                return;
            }

            isActive = true;
            currentCaravan = caravan;
            selectedIndex = 0;
            typeahead.ClearSearch();
            lastChildPerParent.Clear();
            MenuHelper.ResetLevel("CaravanInspect");

            // Build the tree and record counts for change detection
            BuildTree();
            UpdateTrackedCounts();

            // Simple announcement - just the caravan name
            TolkHelper.Speak(caravan.Name);

            if (visibleNodes.Count > 0)
            {
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Updates the tracked counts for change detection.
        /// </summary>
        private static void UpdateTrackedCounts()
        {
            if (currentCaravan == null)
            {
                lastKnownPawnCount = 0;
                lastKnownItemCount = 0;
                return;
            }

            lastKnownPawnCount = currentCaravan.PawnsListForReading?.Count ?? 0;
            var items = CaravanInventoryUtility.AllInventoryItems(currentCaravan);
            lastKnownItemCount = items?.Sum(t => t.stackCount) ?? 0;
        }

        /// <summary>
        /// Checks if caravan contents have changed and refreshes if needed.
        /// Called at the start of HandleInput to detect changes after abandon dialogs close.
        /// </summary>
        private static void CheckForChangesAndRefresh()
        {
            if (currentCaravan == null)
                return;

            int currentPawnCount = currentCaravan.PawnsListForReading?.Count ?? 0;
            var items = CaravanInventoryUtility.AllInventoryItems(currentCaravan);
            int currentItemCount = items?.Sum(t => t.stackCount) ?? 0;

            if (currentPawnCount != lastKnownPawnCount || currentItemCount != lastKnownItemCount)
            {
                RefreshTree();
                UpdateTrackedCounts();
            }
        }

        /// <summary>
        /// Closes the caravan inspect screen.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentCaravan = null;
            rootNode = null;
            visibleNodes.Clear();
            selectedIndex = 0;
            typeahead.ClearSearch();
            lastChildPerParent.Clear();
            TolkHelper.Speak("Caravan inspect closed");
        }

        /// <summary>
        /// Refreshes the tree structure (called after gear changes or item abandonment).
        /// Maintains cursor position by finding the same item or falling back to position.
        /// </summary>
        public static void RefreshTree()
        {
            if (!IsActive || currentCaravan == null)
                return;

            // Remember current selection details for restoration
            TreeNode oldNode = selectedIndex >= 0 && selectedIndex < visibleNodes.Count
                ? visibleNodes[selectedIndex]
                : null;

            object oldData = oldNode?.Data;
            string oldLabel = oldNode?.Label;
            int oldIndex = selectedIndex;
            TreeNode oldParent = oldNode?.Parent;
            string oldParentLabel = oldParent?.Label;

            // Remember expansion states for all nodes by their labels (to restore after rebuild)
            var expansionStates = new Dictionary<string, bool>();
            foreach (var node in visibleNodes)
            {
                if (node.CanExpand)
                {
                    string key = GetNodePath(node);
                    expansionStates[key] = node.IsExpanded;
                }
            }

            // Rebuild tree
            BuildTree();

            // Restore expansion states
            foreach (var node in GetAllNodes(rootNode))
            {
                if (node.CanExpand)
                {
                    string key = GetNodePath(node);
                    if (expansionStates.TryGetValue(key, out bool wasExpanded))
                    {
                        node.IsExpanded = wasExpanded;
                    }
                }
            }

            // Rebuild visible list with restored expansion
            RebuildVisibleList();

            // Try to find the same item by Data reference first
            if (oldData != null)
            {
                int foundIndex = visibleNodes.FindIndex(n => n.Data == oldData);
                if (foundIndex >= 0)
                {
                    selectedIndex = foundIndex;
                    return;
                }
            }

            // Try to find by label within the same parent
            if (!string.IsNullOrEmpty(oldLabel) && !string.IsNullOrEmpty(oldParentLabel))
            {
                int foundIndex = visibleNodes.FindIndex(n =>
                    n.Label == oldLabel &&
                    n.Parent?.Label == oldParentLabel);
                if (foundIndex >= 0)
                {
                    selectedIndex = foundIndex;
                    return;
                }
            }

            // If item was deleted, stay at the same index position (or move up if at end)
            selectedIndex = Math.Min(oldIndex, visibleNodes.Count - 1);
            if (selectedIndex < 0) selectedIndex = 0;
        }

        /// <summary>
        /// Gets all nodes in the tree (for iteration).
        /// </summary>
        private static IEnumerable<TreeNode> GetAllNodes(TreeNode node)
        {
            if (node == null) yield break;

            yield return node;

            foreach (var child in node.Children)
            {
                foreach (var descendant in GetAllNodes(child))
                {
                    yield return descendant;
                }
            }
        }

        /// <summary>
        /// Gets a unique path string for a node (for restoration after rebuild).
        /// </summary>
        private static string GetNodePath(TreeNode node)
        {
            var parts = new List<string>();
            var current = node;
            while (current != null && current.Type != NodeType.Root)
            {
                parts.Insert(0, current.Label ?? "?");
                current = current.Parent;
            }
            return string.Join("/", parts);
        }

        /// <summary>
        /// Builds the tree structure for the caravan.
        /// </summary>
        private static void BuildTree()
        {
            rootNode = new TreeNode
            {
                Type = NodeType.Root,
                Label = "Root",
                Depth = -1,
                IsExpanded = true,
                CanExpand = true
            };

            // Add main categories
            AddCaravanStatusNode(rootNode);
            AddPawnsNode(rootNode);
            AddGearNode(rootNode);
            AddItemsNode(rootNode);

            // Rebuild visible list
            RebuildVisibleList();
        }

        /// <summary>
        /// Adds the Caravan Status node with stats.
        /// </summary>
        private static void AddCaravanStatusNode(TreeNode parent)
        {
            var statusNode = new TreeNode
            {
                Type = NodeType.Category,
                Label = "Caravan Status",
                Depth = parent.Depth + 1,
                CanExpand = true,
                IsExpanded = false,
                Parent = parent
            };

            // Add stats as children with tooltips from the game's built-in explanation properties
            AddStatNode(statusNode, "Location", GetLocationString());

            // Mass with game's tooltip explanation
            string massTooltip = GetMassTooltip();
            AddStatNode(statusNode, "Mass", GetMassString(), massTooltip);

            // Status with detailed explanation
            string statusTooltip = GetStatusTooltip();
            AddStatNode(statusNode, "Status", GetMovementStatus(), statusTooltip);

            // Speed with game's tooltip (uses same method as Gizmo_CaravanInfo)
            // Game shows "Immobile" when overloaded, otherwise shows tiles/day
            // Game's description: "CaravanMovementSpeedTip".Translate()
            string speedDescription = "CaravanMovementSpeedTip".Translate();
            if (currentCaravan.MassUsage > currentCaravan.MassCapacity)
            {
                // Matches game's GetMovementSpeedLabel when immobile
                AddStatNode(statusNode, "Speed", $"Immobile. {speedDescription}");
            }
            else
            {
                var speedExplanation = new StringBuilder();
                float tilesPerDay = TilesPerDayCalculator.ApproxTilesPerDay(currentCaravan, speedExplanation);
                // Game format: {tilesPerDay:0.#} tiles/day + description
                AddStatNode(statusNode, "Speed", $"{tilesPerDay:0.#} tiles/day. {speedDescription}", speedExplanation.ToString());
            }

            // Food with tooltip - matches game's CaravanUIUtility.GetDaysWorthOfFoodLabel behavior
            // Game's description: "DaysWorthOfFoodTooltip".Translate()
            string foodDescription = "DaysWorthOfFoodTooltip".Translate();
            try
            {
                var foodInfo = currentCaravan.DaysWorthOfFood;
                string foodValue;

                if (foodInfo.days >= 600f)
                {
                    foodValue = "Infinite";
                }
                else
                {
                    // Game format: {days:0.#} (shows "3" not "3.0")
                    foodValue = $"{foodInfo.days:0.#} days";

                    // Show rot only if food is perishable AND will rot before running out
                    // This matches the game's exact logic in CaravanUIUtility.GetDaysWorthOfFoodLabel
                    if (foodInfo.tillRot < 600f && foodInfo.tillRot < foodInfo.days)
                    {
                        foodValue += $" ({foodInfo.tillRot:0.#} days until rot)";
                    }
                }

                // Check for food warnings
                if (currentCaravan.needs.AnyPawnOutOfFood(out string malnutritionInfo))
                {
                    foodValue += " - OUT OF FOOD";
                    if (!string.IsNullOrEmpty(malnutritionInfo))
                    {
                        foodValue += $" ({malnutritionInfo})";
                    }
                }

                // Add description after value
                foodValue += $". {foodDescription}";
                AddStatNode(statusNode, "Food", foodValue);
            }
            catch
            {
                AddStatNode(statusNode, "Food", "Unknown");
            }

            // Foraging info if applicable
            // Game format: {perDay:0.#} ({food.label})
            // Game's description: "ForagedFoodPerDayTip".Translate()
            try
            {
                var forageInfo = currentCaravan.forage.ForagedFoodPerDay;
                if (forageInfo.perDay > 0f)
                {
                    string forageTooltip = currentCaravan.forage.ForagedFoodPerDayExplanation;
                    string forageDescription = "ForagedFoodPerDayTip".Translate();
                    string foodLabel = forageInfo.food?.label ?? "food";
                    AddStatNode(statusNode, "Foraging", $"{forageInfo.perDay:0.#} ({foodLabel})/day. {forageDescription}", forageTooltip);
                }
            }
            catch { }

            // Destination and ETA
            if (currentCaravan.pather?.Moving == true && currentCaravan.pather.Destination.Valid)
            {
                AddStatNode(statusNode, "Destination", GetDestinationString());
                AddStatNode(statusNode, "ETA", GetETAString());
            }

            // Visibility with game's tooltip
            // Game's description: "CaravanVisibilityTip".Translate()
            string visDescription = "CaravanVisibilityTip".Translate();
            string visTooltip = currentCaravan.VisibilityExplanation;
            AddStatNode(statusNode, "Visibility", $"{currentCaravan.Visibility:P0}. {visDescription}", visTooltip);

            // Beds info when resting
            if (!currentCaravan.pather?.MovingNow == true && currentCaravan.beds != null)
            {
                int bedCount = currentCaravan.beds.GetUsedBedCount();
                string bedLabel = bedCount > 0 ? $"{bedCount} bedroll(s) in use" : "No bedrolls";
                AddStatNode(statusNode, "Beds", bedLabel);
            }

            parent.Children.Add(statusNode);
        }

        /// <summary>
        /// Gets tooltip explanation for mass using game's built-in explanation.
        /// </summary>
        private static string GetMassTooltip()
        {
            // Use the game's built-in mass capacity explanation
            string gameExplanation = currentCaravan.MassCapacityExplanation;

            var sb = new StringBuilder();
            sb.AppendLine($"Mass carried: {currentCaravan.MassUsage:F1} kg");
            sb.AppendLine($"Mass capacity: {currentCaravan.MassCapacity:F1} kg");

            if (currentCaravan.MassUsage > currentCaravan.MassCapacity)
            {
                sb.AppendLine("OVERLOADED - Caravan cannot move until mass is reduced.");
            }
            else
            {
                float remaining = currentCaravan.MassCapacity - currentCaravan.MassUsage;
                sb.AppendLine($"Remaining capacity: {remaining:F1} kg");
            }

            // Append the game's detailed breakdown
            if (!string.IsNullOrEmpty(gameExplanation))
            {
                sb.AppendLine();
                sb.AppendLine("Capacity breakdown:");
                sb.Append(gameExplanation);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets tooltip explanation for status.
        /// </summary>
        private static string GetStatusTooltip()
        {
            var sb = new StringBuilder();

            if (currentCaravan.CantMove)
            {
                sb.AppendLine("Caravan cannot move because:");
                if (currentCaravan.AllOwnersDowned)
                    sb.AppendLine("- All caravan members are downed");
                if (currentCaravan.AllOwnersHaveMentalBreak)
                    sb.AppendLine("- All caravan members are having mental breaks");
                if (currentCaravan.ImmobilizedByMass)
                    sb.AppendLine("- Caravan is overloaded beyond capacity");
            }
            else if (currentCaravan.NightResting)
            {
                sb.AppendLine("Caravan is resting during nighttime hours.");
                int bedCount = currentCaravan.beds?.GetUsedBedCount() ?? 0;
                if (bedCount > 0)
                    sb.AppendLine($"Using {bedCount} bedroll(s) for better rest.");
                else
                    sb.AppendLine("No bedrolls - pawns are sleeping on the ground.");
            }
            else if (currentCaravan.pather?.Moving == true)
            {
                if (currentCaravan.pather.Paused)
                    sb.AppendLine("Caravan movement is paused.");
                else
                    sb.AppendLine("Caravan is traveling toward destination.");
            }
            else
            {
                sb.AppendLine("Caravan is stopped and waiting for orders.");
            }

            return sb.ToString();
        }

        private static void AddStatNode(TreeNode parent, string label, string value, string tooltip = null)
        {
            var node = new TreeNode
            {
                Type = NodeType.Stat,
                Label = label,
                Value = value,
                Tooltip = tooltip,  // Store tooltip for StatBreakdownState (Alt+I)
                Depth = parent.Depth + 1,
                Parent = parent,
                CanExpand = false
            };

            parent.Children.Add(node);
        }

        private static string GetLocationString()
        {
            if (currentCaravan.Tile.Valid && Find.WorldGrid != null)
            {
                Vector2 coords = Find.WorldGrid.LongLatOf(currentCaravan.Tile);
                return $"Tile {currentCaravan.Tile}, {coords.y:F1}N {coords.x:F1}E";
            }
            return "Unknown";
        }

        private static string GetMassString()
        {
            // Matches game's CaravanUIUtility format: {massUsage:F0} / {massCapacity:F0} kg
            float massUsage = currentCaravan.MassUsage;
            float massCapacity = currentCaravan.MassCapacity;
            return $"{massUsage:F0} / {massCapacity:F0} kg";
        }

        private static string GetMovementStatus()
        {
            // Use WorldInfoHelper for consistent status display with comma/period cycling
            string status = WorldInfoHelper.GetCaravanStatus(currentCaravan);
            // Capitalize first letter for display
            if (!string.IsNullOrEmpty(status))
            {
                return char.ToUpper(status[0]) + status.Substring(1);
            }
            return status;
        }

        private static string GetDestinationString()
        {
            if (currentCaravan.pather?.Destination.Valid != true)
                return "None";

            PlanetTile destTile = currentCaravan.pather.Destination;
            Settlement destSettlement = Find.WorldObjects?.SettlementAt(destTile);
            if (destSettlement != null)
                return destSettlement.Label;
            return $"Tile {destTile}";
        }

        private static string GetETAString()
        {
            if (currentCaravan.pather?.Destination.Valid != true)
                return "N/A";

            float ticksToArrive = CaravanArrivalTimeEstimator.EstimatedTicksToArrive(
                currentCaravan.Tile, currentCaravan.pather.Destination, currentCaravan);
            if (ticksToArrive > 0)
            {
                float hoursToArrive = ticksToArrive / 2500f;
                float daysToArrive = hoursToArrive / 24f;
                return daysToArrive >= 1f ? $"{daysToArrive:F1} days" : $"{hoursToArrive:F1} hours";
            }
            return "Unknown";
        }

        /// <summary>
        /// Adds the Pawns node with Colonists and Animals sub-categories.
        /// </summary>
        private static void AddPawnsNode(TreeNode parent)
        {
            var pawns = currentCaravan.PawnsListForReading;
            var colonists = pawns.Where(p => p.IsColonist && !p.IsPrisoner).OrderBy(p => p.LabelShortCap).ToList();
            var prisoners = pawns.Where(p => p.IsPrisoner).OrderBy(p => p.LabelShortCap).ToList();
            var animals = pawns.Where(p => p.RaceProps.Animal).OrderBy(p => p.LabelShortCap).ToList();

            int totalPawns = colonists.Count + prisoners.Count + animals.Count;

            var pawnsNode = new TreeNode
            {
                Type = NodeType.Category,
                Label = $"Pawns ({totalPawns})",
                Depth = parent.Depth + 1,
                CanExpand = true,
                IsExpanded = false,
                Parent = parent
            };

            // Find negotiator
            Pawn negotiator = BestCaravanPawnUtility.FindBestNegotiator(currentCaravan);

            // Add Colonists sub-category
            if (colonists.Count > 0)
            {
                var colonistsNode = new TreeNode
                {
                    Type = NodeType.SubCategory,
                    Label = $"Colonists ({colonists.Count})",
                    Depth = pawnsNode.Depth + 1,
                    CanExpand = true,
                    IsExpanded = false,
                    Parent = pawnsNode
                };

                foreach (var pawn in colonists)
                {
                    string label = pawn.LabelShortCap;
                    if (pawn.story?.TitleCap != null && !pawn.story.TitleCap.NullOrEmpty())
                        label += $", {pawn.story.TitleCap}";
                    if (pawn == negotiator)
                        label += ", negotiator";

                    var pawnNode = new TreeNode
                    {
                        Type = NodeType.Pawn,
                        Label = label,
                        Depth = colonistsNode.Depth + 1,
                        Parent = colonistsNode,
                        Data = pawn,
                        CanAbandon = true,
                        OnActivate = () => InspectPawn(pawn)
                    };
                    colonistsNode.Children.Add(pawnNode);
                }

                pawnsNode.Children.Add(colonistsNode);
            }

            // Add Prisoners sub-category
            if (prisoners.Count > 0)
            {
                var prisonersNode = new TreeNode
                {
                    Type = NodeType.SubCategory,
                    Label = $"Prisoners ({prisoners.Count})",
                    Depth = pawnsNode.Depth + 1,
                    CanExpand = true,
                    IsExpanded = false,
                    Parent = pawnsNode
                };

                foreach (var pawn in prisoners)
                {
                    var pawnNode = new TreeNode
                    {
                        Type = NodeType.Pawn,
                        Label = pawn.LabelShortCap,
                        Depth = prisonersNode.Depth + 1,
                        Parent = prisonersNode,
                        Data = pawn,
                        CanAbandon = true,
                        OnActivate = () => InspectPawn(pawn)
                    };
                    prisonersNode.Children.Add(pawnNode);
                }

                pawnsNode.Children.Add(prisonersNode);
            }

            // Add Animals sub-category
            if (animals.Count > 0)
            {
                var animalsNode = new TreeNode
                {
                    Type = NodeType.SubCategory,
                    Label = $"Animals ({animals.Count})",
                    Depth = pawnsNode.Depth + 1,
                    CanExpand = true,
                    IsExpanded = false,
                    Parent = pawnsNode
                };

                foreach (var animal in animals)
                {
                    var animalNode = new TreeNode
                    {
                        Type = NodeType.Pawn,
                        Label = animal.LabelShortCap,
                        Depth = animalsNode.Depth + 1,
                        Parent = animalsNode,
                        Data = animal,
                        CanAbandon = true,
                        OnActivate = () => InspectPawn(animal)
                    };
                    animalsNode.Children.Add(animalNode);
                }

                pawnsNode.Children.Add(animalsNode);
            }

            parent.Children.Add(pawnsNode);
        }

        /// <summary>
        /// Adds the Gear node with per-pawn gear.
        /// </summary>
        private static void AddGearNode(TreeNode parent)
        {
            var humanlikePawns = currentCaravan.PawnsListForReading
                .Where(p => p.RaceProps.Humanlike && !p.Dead)
                .OrderBy(p => p.LabelShortCap)
                .ToList();

            int totalGear = humanlikePawns.Sum(p =>
                (p.equipment?.Primary != null ? 1 : 0) +
                (p.apparel?.WornApparel?.Count ?? 0));

            var gearNode = new TreeNode
            {
                Type = NodeType.Category,
                Label = $"Gear ({totalGear} items)",
                Depth = parent.Depth + 1,
                CanExpand = true,
                IsExpanded = false,
                Parent = parent
            };

            foreach (var pawn in humanlikePawns)
            {
                int pawnGearCount = (pawn.equipment?.Primary != null ? 1 : 0) +
                                   (pawn.apparel?.WornApparel?.Count ?? 0);

                if (pawnGearCount == 0)
                    continue;

                var pawnGearNode = new TreeNode
                {
                    Type = NodeType.SubCategory,
                    Label = $"{pawn.LabelShortCap} ({pawnGearCount})",
                    Depth = gearNode.Depth + 1,
                    CanExpand = true,
                    IsExpanded = false,
                    Parent = gearNode,
                    Data = pawn
                };

                // Add weapon
                if (pawn.equipment?.Primary != null)
                {
                    var weapon = pawn.equipment.Primary;
                    pawnGearNode.Children.Add(new TreeNode
                    {
                        Type = NodeType.GearItem,
                        Label = weapon.LabelCap,
                        Depth = pawnGearNode.Depth + 1,
                        Parent = pawnGearNode,
                        Data = weapon,
                        OwnerPawn = pawn,
                        CanAbandon = true,
                        OnActivate = () => OpenGearMenu(weapon, pawn)
                    });
                }

                // Add apparel
                if (pawn.apparel?.WornApparel != null)
                {
                    foreach (var apparel in pawn.apparel.WornApparel.OrderByDescending(a => a.def.apparel.bodyPartGroups.Count))
                    {
                        pawnGearNode.Children.Add(new TreeNode
                        {
                            Type = NodeType.GearItem,
                            Label = apparel.LabelCap,
                            Depth = pawnGearNode.Depth + 1,
                            Parent = pawnGearNode,
                            Data = apparel,
                            OwnerPawn = pawn,
                            CanAbandon = true,
                            OnActivate = () => OpenGearMenu(apparel, pawn)
                        });
                    }
                }

                gearNode.Children.Add(pawnGearNode);
            }

            parent.Children.Add(gearNode);
        }

        /// <summary>
        /// Adds the Items node using InventoryHelper for consistent category tree (same as colony inventory).
        /// </summary>
        private static void AddItemsNode(TreeNode parent)
        {
            var inventoryItems = CaravanInventoryUtility.AllInventoryItems(currentCaravan)?.ToList();

            if (inventoryItems == null || inventoryItems.Count == 0)
            {
                var emptyNode = new TreeNode
                {
                    Type = NodeType.Category,
                    Label = "Items (empty)",
                    Depth = parent.Depth + 1,
                    CanExpand = false,
                    Parent = parent
                };
                parent.Children.Add(emptyNode);
                return;
            }

            int totalCount = inventoryItems.Sum(t => t.stackCount);

            // Use InventoryHelper for consistent categorization (same tree as colony inventory)
            var aggregatedItems = InventoryHelper.AggregateStacks(inventoryItems);
            var categoryTree = InventoryHelper.BuildCategoryTree(aggregatedItems);

            var itemsNode = new TreeNode
            {
                Type = NodeType.Category,
                Label = $"Items ({totalCount})",
                Depth = parent.Depth + 1,
                CanExpand = true,
                IsExpanded = false,
                Parent = parent
            };

            // Convert InventoryHelper.CategoryNode tree to our TreeNode tree (read-only, no actions)
            AddInventoryCategoryNodes(itemsNode, categoryTree, inventoryItems);

            parent.Children.Add(itemsNode);
        }

        /// <summary>
        /// Recursively adds inventory category nodes from InventoryHelper tree.
        /// </summary>
        private static void AddInventoryCategoryNodes(TreeNode parent, List<InventoryHelper.CategoryNode> categoryNodes, List<Thing> allItems)
        {
            foreach (var categoryNode in categoryNodes)
            {
                var catNode = new TreeNode
                {
                    Type = NodeType.SubCategory,
                    Label = categoryNode.GetDisplayLabel(),
                    Depth = parent.Depth + 1,
                    CanExpand = categoryNode.SubCategories.Count > 0 || categoryNode.Items.Count > 0,
                    IsExpanded = false,
                    Parent = parent
                };

                // Recursively add subcategories
                if (categoryNode.SubCategories.Count > 0)
                {
                    AddInventoryCategoryNodes(catNode, categoryNode.SubCategories, allItems);
                }

                // Add items (read-only - no Jump/View actions like in colony inventory)
                foreach (var invItem in categoryNode.Items)
                {
                    // Find the actual Thing instance(s) for this def to enable abandon
                    var thingsOfType = allItems.Where(t => t.def == invItem.Def).ToList();
                    Thing representativeThing = thingsOfType.FirstOrDefault();

                    bool canEquip = invItem.Def.IsWeapon || invItem.Def.IsApparel;

                    var itemNode = new TreeNode
                    {
                        Type = NodeType.InventoryItem,
                        Label = invItem.GetDisplayLabel(),
                        Depth = catNode.Depth + 1,
                        Parent = catNode,
                        Data = representativeThing,  // Store actual Thing for abandon/inspect
                        CanAbandon = representativeThing != null,
                        // Read-only: Enter inspects item (or opens equip menu for gear)
                        OnActivate = representativeThing != null
                            ? (canEquip
                                ? (Action)(() => OpenGearMenu(representativeThing, null))
                                : (Action)(() => InspectThing(representativeThing)))
                            : null
                    };

                    catNode.Children.Add(itemNode);
                }

                parent.Children.Add(catNode);
            }
        }

        /// <summary>
        /// Rebuilds the flattened visible nodes list.
        /// </summary>
        private static void RebuildVisibleList()
        {
            visibleNodes.Clear();

            if (rootNode == null)
                return;

            // Add all visible children of root
            foreach (var child in rootNode.Children)
            {
                AddVisibleNodes(child);
            }
        }

        private static void AddVisibleNodes(TreeNode node)
        {
            visibleNodes.Add(node);

            if (node.IsExpanded && node.Children.Count > 0)
            {
                foreach (var child in node.Children)
                {
                    AddVisibleNodes(child);
                }
            }
        }

        #region Actions

        private static void InspectPawn(Pawn pawn)
        {
            if (pawn != null)
            {
                Dialog_InfoCard infoCard = new Dialog_InfoCard(pawn);
                Find.WindowStack.Add(infoCard);
            }
        }

        private static void InspectThing(Thing thing)
        {
            if (thing != null)
            {
                Dialog_InfoCard infoCard = new Dialog_InfoCard(thing);
                Find.WindowStack.Add(infoCard);
            }
        }

        private static void OpenGearMenu(Thing item, Pawn owner)
        {
            GearEquipMenuState.Open(currentCaravan, item, owner);
        }

        /// <summary>
        /// Abandons the selected item (Delete key).
        /// </summary>
        private static void AbandonSelected()
        {
            if (visibleNodes.Count == 0 || selectedIndex < 0 || selectedIndex >= visibleNodes.Count)
                return;

            var node = visibleNodes[selectedIndex];

            if (!node.CanAbandon)
            {
                TolkHelper.Speak("Cannot abandon this item");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            if (node.Data is Pawn pawn)
            {
                CaravanAbandonOrBanishUtility.TryAbandonOrBanishViaInterface(pawn, currentCaravan);
            }
            else if (node.Data is Thing thing)
            {
                CaravanAbandonOrBanishUtility.TryAbandonOrBanishViaInterface(thing, currentCaravan);
            }
            else
            {
                TolkHelper.Speak("Cannot abandon this item");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        /// <summary>
        /// Shows mood info for the selected pawn (Alt+M).
        /// </summary>
        private static void ShowPawnMood()
        {
            if (visibleNodes.Count == 0 || selectedIndex < 0 || selectedIndex >= visibleNodes.Count)
                return;

            var node = visibleNodes[selectedIndex];

            if (node.Data is Pawn pawn && pawn.needs?.mood != null)
            {
                string moodInfo = PawnInfoHelper.GetMoodInfo(pawn);
                TolkHelper.Speak(moodInfo);
            }
            else
            {
                TolkHelper.Speak("No mood info available for this item");
            }
        }

        /// <summary>
        /// Shows needs info for the selected pawn (Alt+N).
        /// </summary>
        private static void ShowPawnNeeds()
        {
            if (visibleNodes.Count == 0 || selectedIndex < 0 || selectedIndex >= visibleNodes.Count)
                return;

            var node = visibleNodes[selectedIndex];

            if (node.Data is Pawn pawn && pawn.needs != null)
            {
                string needsInfo = PawnInfoHelper.GetNeedsInfo(pawn);
                TolkHelper.Speak(needsInfo);
            }
            else
            {
                TolkHelper.Speak("No needs info available for this item");
            }
        }

        /// <summary>
        /// Shows health info for the selected pawn (Alt+H).
        /// </summary>
        private static void ShowPawnHealth()
        {
            if (visibleNodes.Count == 0 || selectedIndex < 0 || selectedIndex >= visibleNodes.Count)
                return;

            var node = visibleNodes[selectedIndex];

            if (node.Data is Pawn pawn && pawn.health != null)
            {
                string healthInfo = PawnInfoHelper.GetHealthInfo(pawn);
                TolkHelper.Speak(healthInfo);
            }
            else
            {
                TolkHelper.Speak("No health info available for this item");
            }
        }

        /// <summary>
        /// Shows gear info for the selected pawn (Alt+G).
        /// </summary>
        private static void ShowPawnGear()
        {
            if (visibleNodes.Count == 0 || selectedIndex < 0 || selectedIndex >= visibleNodes.Count)
                return;

            var node = visibleNodes[selectedIndex];

            if (node.Data is Pawn pawn)
            {
                string gearInfo = PawnInfoHelper.GetGearInfo(pawn);
                TolkHelper.Speak(gearInfo);
            }
            else
            {
                TolkHelper.Speak("No gear info available for this item");
            }
        }

        /// <summary>
        /// Shows top skills for the selected pawn (Alt+K).
        /// </summary>
        private static void ShowPawnSkills()
        {
            if (visibleNodes.Count == 0 || selectedIndex < 0 || selectedIndex >= visibleNodes.Count)
                return;

            var node = visibleNodes[selectedIndex];

            if (node.Data is Pawn pawn)
            {
                string skillsInfo = PawnInfoHelper.GetTopSkillsInfo(pawn);
                TolkHelper.Speak(skillsInfo);
            }
            else
            {
                TolkHelper.Speak("No skills info available for this item");
            }
        }

        #endregion

        #region Navigation

        /// <summary>
        /// Selects the next item.
        /// </summary>
        public static void SelectNext()
        {
            if (visibleNodes.Count == 0)
            {
                TolkHelper.Speak("No items");
                return;
            }

            selectedIndex = MenuHelper.SelectNext(selectedIndex, visibleNodes.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Selects the previous item.
        /// </summary>
        public static void SelectPrevious()
        {
            if (visibleNodes.Count == 0)
            {
                TolkHelper.Speak("No items");
                return;
            }

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, visibleNodes.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Expands the selected node (Right arrow).
        /// </summary>
        public static void Expand()
        {
            if (visibleNodes.Count == 0 || selectedIndex >= visibleNodes.Count)
                return;

            typeahead.ClearSearch();
            var node = visibleNodes[selectedIndex];

            if (!node.CanExpand)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Cannot expand");
                return;
            }

            if (node.IsExpanded)
            {
                // Already expanded - move to first child
                if (node.Children.Count > 0)
                {
                    int childIndex = visibleNodes.IndexOf(node.Children[0]);
                    if (childIndex >= 0)
                    {
                        selectedIndex = childIndex;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceCurrentSelection();
                    }
                }
                return;
            }

            // Expand
            node.IsExpanded = true;
            RebuildVisibleList();
            SoundDefOf.Click.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Collapses the selected node (Left arrow).
        /// </summary>
        public static void Collapse()
        {
            if (visibleNodes.Count == 0 || selectedIndex >= visibleNodes.Count)
                return;

            typeahead.ClearSearch();
            var node = visibleNodes[selectedIndex];

            // If expanded, collapse
            if (node.CanExpand && node.IsExpanded)
            {
                node.IsExpanded = false;
                RebuildVisibleList();

                if (selectedIndex >= visibleNodes.Count)
                    selectedIndex = Math.Max(0, visibleNodes.Count - 1);

                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentSelection();
                return;
            }

            // Move to parent
            var parent = node.Parent;
            while (parent != null && !parent.CanExpand)
            {
                parent = parent.Parent;
            }

            if (parent == null || parent == rootNode)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Already at top level");
                return;
            }

            lastChildPerParent[parent] = node;

            int parentIndex = visibleNodes.IndexOf(parent);
            if (parentIndex >= 0)
            {
                selectedIndex = parentIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Expands all sibling nodes at the same level as the current item.
        /// WCAG tree view pattern: * key expands all siblings.
        /// </summary>
        public static void ExpandAllSiblings()
        {
            if (visibleNodes.Count == 0 || selectedIndex >= visibleNodes.Count)
                return;

            TreeNode currentItem = visibleNodes[selectedIndex];
            TreeNode parent = currentItem.Parent; // null or root means top level

            // Get siblings list (either from parent's children or root's children)
            List<TreeNode> siblings;
            if (parent == null || parent.Type == NodeType.Root)
            {
                siblings = rootNode?.Children ?? new List<TreeNode>();
            }
            else
            {
                siblings = parent.Children;
            }

            // Find all collapsed sibling nodes that can be expanded
            int expandedCount = 0;
            foreach (TreeNode sibling in siblings)
            {
                // Must be expandable and currently collapsed
                if (sibling.CanExpand && !sibling.IsExpanded)
                {
                    sibling.IsExpanded = true;
                    expandedCount++;
                }
            }

            if (expandedCount > 0)
            {
                RebuildVisibleList();
                typeahead.ClearSearch(); // Clear search since visible items changed
                SoundDefOf.Click.PlayOneShotOnCamera();
                if (expandedCount == 1)
                    TolkHelper.Speak("Expanded 1 node");
                else
                    TolkHelper.Speak($"Expanded {expandedCount} nodes");
            }
            else
            {
                // Check if there are any expandable sibling nodes at all
                bool hasAnyExpandableSiblings = false;
                foreach (TreeNode sibling in siblings)
                {
                    if (sibling.CanExpand)
                    {
                        hasAnyExpandableSiblings = true;
                        break;
                    }
                }

                if (hasAnyExpandableSiblings)
                    TolkHelper.Speak("All nodes already expanded at this level");
                else
                    TolkHelper.Speak("No expandable nodes at this level");
            }
        }

        /// <summary>
        /// Activates the selected item (Enter key).
        /// </summary>
        public static void ActivateSelected()
        {
            if (visibleNodes.Count == 0 || selectedIndex >= visibleNodes.Count)
                return;

            var node = visibleNodes[selectedIndex];

            // If expandable and collapsed, expand
            if (node.CanExpand && !node.IsExpanded)
            {
                Expand();
                return;
            }

            // If has action, execute it
            if (node.OnActivate != null)
            {
                node.OnActivate();
                SoundDefOf.Click.PlayOneShotOnCamera();
                return;
            }

            // For stats, just read them again
            if (node.Type == NodeType.Stat)
            {
                TolkHelper.Speak(node.GetDisplayLabel());
                return;
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("No action available");
        }

        /// <summary>
        /// Helper method to clear typeahead and announce current selection.
        /// Used as callback for MenuHelper tree navigation methods.
        /// </summary>
        private static void ClearAndAnnounce()
        {
            typeahead.ClearSearch();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Jumps to the first item, or first sibling at current level.
        /// Ctrl+Home jumps to absolute first.
        /// </summary>
        private static void JumpToFirst(bool ctrlPressed = false)
        {
            if (visibleNodes == null || visibleNodes.Count == 0)
            {
                TolkHelper.Speak("No items");
                return;
            }

            MenuHelper.HandleTreeHomeKey(visibleNodes, ref selectedIndex, node => node.Depth, ctrlPressed, ClearAndAnnounce);
        }

        /// <summary>
        /// Jumps to the last item, last sibling, or last visible descendant.
        /// Ctrl+End jumps to absolute last.
        /// </summary>
        private static void JumpToLast(bool ctrlPressed = false)
        {
            if (visibleNodes == null || visibleNodes.Count == 0)
            {
                TolkHelper.Speak("No items");
                return;
            }

            MenuHelper.HandleTreeEndKey(
                visibleNodes,
                ref selectedIndex,
                node => node.Depth,
                node => node.IsExpanded,
                node => node.CanExpand && node.Children != null && node.Children.Count > 0,
                ctrlPressed,
                ClearAndAnnounce);
        }

        #endregion

        #region Announcements

        /// <summary>
        /// Gets sibling position for announcement.
        /// </summary>
        private static (int position, int total) GetSiblingPosition(TreeNode node)
        {
            if (node.Parent == null)
                return (1, 1);

            var siblings = node.Parent.Children;
            int position = siblings.IndexOf(node) + 1;
            return (position, siblings.Count);
        }

        /// <summary>
        /// Announces the current selection.
        /// </summary>
        private static void AnnounceCurrentSelection()
        {
            if (visibleNodes.Count == 0 || selectedIndex >= visibleNodes.Count)
                return;

            var node = visibleNodes[selectedIndex];
            string label = node.GetDisplayLabel().StripTags();

            // State indicator for expandable items
            string stateIndicator = "";
            if (node.CanExpand)
            {
                stateIndicator = node.IsExpanded ? " expanded" : " collapsed";
            }

            // Position among siblings
            var (position, total) = GetSiblingPosition(node);
            string positionPart = MenuHelper.FormatPosition(position - 1, total);

            // Level suffix
            string levelSuffix = MenuHelper.GetLevelSuffix("CaravanInspect", node.Depth);

            // Only add period separator if label doesn't already end with punctuation
            string separator = label.EndsWith(".") || label.EndsWith("!") || label.EndsWith("?") ? " " : ". ";
            string announcement = $"{label}{stateIndicator}{separator}{positionPart}{levelSuffix}";
            TolkHelper.Speak(announcement, SpeechPriority.Low);
        }

        /// <summary>
        /// Announces with typeahead search context.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (visibleNodes.Count == 0 || selectedIndex >= visibleNodes.Count)
                return;

            var node = visibleNodes[selectedIndex];
            string label = node.GetDisplayLabel().StripTags();

            if (typeahead.HasActiveSearch)
            {
                string stateIndicator = "";
                if (node.CanExpand)
                {
                    stateIndicator = node.IsExpanded ? " expanded" : " collapsed";
                }
                TolkHelper.Speak($"{label}{stateIndicator}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'");
            }
            else
            {
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Gets labels for typeahead search.
        /// </summary>
        private static List<string> GetNodeLabels()
        {
            return visibleNodes.Select(n => n.GetDisplayLabel()).ToList();
        }

        #endregion

        #region Input Handling

        /// <summary>
        /// Handles keyboard input for the caravan inspect screen.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive)
                return false;

            // Let StatBreakdownState handle input when it's active
            if (StatBreakdownState.IsActive)
                return false;

            // Check for changes (e.g., after abandon dialog closed) and refresh if needed
            CheckForChangesAndRefresh();

            // Handle Escape
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentSelection();
                    return true;
                }
                Close();
                return true;
            }

            // Handle Backspace for typeahead
            if (key == KeyCode.Backspace && typeahead.HasActiveSearch)
            {
                var labels = GetNodeLabels();
                if (typeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0) selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
                return true;
            }

            // Navigation
            if (key == KeyCode.UpArrow && !shift && !ctrl && !alt)
            {
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    int prevIndex = typeahead.GetPreviousMatch(selectedIndex);
                    if (prevIndex >= 0)
                    {
                        selectedIndex = prevIndex;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    SelectPrevious();
                }
                return true;
            }

            if (key == KeyCode.DownArrow && !shift && !ctrl && !alt)
            {
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    int nextIndex = typeahead.GetNextMatch(selectedIndex);
                    if (nextIndex >= 0)
                    {
                        selectedIndex = nextIndex;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    SelectNext();
                }
                return true;
            }

            if (key == KeyCode.LeftArrow && !shift && !ctrl && !alt)
            {
                Collapse();
                return true;
            }

            if (key == KeyCode.RightArrow && !shift && !ctrl && !alt)
            {
                Expand();
                return true;
            }

            if (key == KeyCode.Home && !shift && !alt)
            {
                JumpToFirst(Event.current.control);
                Event.current.Use();
                return true;
            }

            if (key == KeyCode.End && !shift && !alt)
            {
                JumpToLast(Event.current.control);
                Event.current.Use();
                return true;
            }

            // Handle * key - expand all sibling nodes (WCAG tree view pattern)
            bool isStar = key == KeyCode.KeypadMultiply || (shift && key == KeyCode.Alpha8);
            if (isStar && !ctrl && !alt)
            {
                ExpandAllSiblings();
                return true;
            }

            // Actions
            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !shift && !ctrl && !alt)
            {
                typeahead.ClearSearch();
                ActivateSelected();
                return true;
            }

            if (key == KeyCode.Delete && !shift && !ctrl && !alt)
            {
                AbandonSelected();
                return true;
            }

            // Alt+I: Inspect - for stats opens breakdown menu, for pawns/things opens inspection
            if (key == KeyCode.I && alt && !shift && !ctrl)
            {
                typeahead.ClearSearch();
                var node = visibleNodes.Count > 0 && selectedIndex < visibleNodes.Count
                    ? visibleNodes[selectedIndex] : null;

                if (node == null)
                {
                    TolkHelper.Speak("Nothing to inspect");
                }
                else if (node.Type == NodeType.Stat && !string.IsNullOrEmpty(node.Tooltip))
                {
                    // Stat with tooltip - open StatBreakdownState for navigable breakdown
                    StatBreakdownState.Open(node.Label, node.Tooltip);
                }
                else if (node.Data is Pawn pawn)
                {
                    InspectPawn(pawn);
                }
                else if (node.Data is Thing thing)
                {
                    InspectThing(thing);
                }
                else if (node.OnActivate != null)
                {
                    // Has some action - execute it
                    node.OnActivate();
                }
                else
                {
                    TolkHelper.Speak("No breakdown available");
                }
                return true;
            }

            // Alt+M: Mood
            if (key == KeyCode.M && alt && !shift && !ctrl)
            {
                ShowPawnMood();
                return true;
            }

            // Alt+N: Needs
            if (key == KeyCode.N && alt && !shift && !ctrl)
            {
                ShowPawnNeeds();
                return true;
            }

            // Alt+H: Health
            if (key == KeyCode.H && alt && !shift && !ctrl)
            {
                ShowPawnHealth();
                return true;
            }

            // Alt+G: Gear
            if (key == KeyCode.G && alt && !shift && !ctrl)
            {
                ShowPawnGear();
                return true;
            }

            // Alt+K: Skills
            if (key == KeyCode.K && alt && !shift && !ctrl)
            {
                ShowPawnSkills();
                return true;
            }

            // Typeahead search (A-Z, 0-9)
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            if ((isLetter || isNumber) && !ctrl && !alt)
            {
                char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                var labels = GetNodeLabels();
                if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
                {
                    if (newIndex >= 0)
                    {
                        selectedIndex = newIndex;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
                }
                return true;
            }

            // Block ALL unhandled keys to prevent game's native handlers from processing them
            // This makes the overlay screen modal - it captures all keyboard input while active
            return true;
        }

        #endregion
    }
}
