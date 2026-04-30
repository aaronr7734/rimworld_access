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
    /// Uses TreeNavigationHelper for all standard navigation logic.
    /// </summary>
    public static class CaravanInspectState
    {
        private static TreeNavigationHelper treeNav = new TreeNavigationHelper("CaravanInspect");
        private static bool isActive = false;
        private static Caravan currentCaravan = null;

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
        public static bool HasActiveTypeahead => treeNav.HasActiveSearch;

        static CaravanInspectState()
        {
            treeNav.FormatItemAnnouncement = FormatItemAnnouncement;
            treeNav.FormatSearchAnnouncement = FormatSearchAnnouncement;
            treeNav.OnActivate = HandleActivate;
            treeNav.OnDelete = HandleDelete;
            treeNav.OnInfo = HandleInfo;
            treeNav.TrackLastChild = true;
        }

        /// <summary>
        /// Builds caravan category nodes (Caravan Status, Pawns, Gear, Items) for a given parent.
        /// Used by WorldObjectSelectionState to embed caravan inspection tree without opening CaravanInspectState.
        /// </summary>
        /// <param name="parent">The parent node to attach categories to</param>
        /// <param name="caravan">The caravan to build categories for</param>
        public static void BuildCaravanCategoriesFor(InspectionTreeItem parent, Caravan caravan)
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
                TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.NoCaravanSpecified".Translate(), SpeechPriority.High);
                return;
            }

            isActive = true;
            currentCaravan = caravan;

            // Build the tree and record counts for change detection
            var root = BuildTree();
            treeNav.Initialize(root);
            UpdateTrackedCounts();

            // Simple announcement - just the caravan name
            TolkHelper.Speak(caravan.Name);

            if (treeNav.Count > 0)
            {
                treeNav.ReannounceCurrentItem();
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
            treeNav.Reset();
            TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.Closed".Translate());
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
            var oldItem = treeNav.SelectedItem;
            object oldData = oldItem?.Data;
            string oldLabel = oldItem?.Label;
            int oldIndex = treeNav.SelectedIndex;
            var oldParent = oldItem?.Parent;
            string oldParentLabel = oldParent?.Label;

            // Remember expansion states for all nodes by their labels (to restore after rebuild)
            var expansionStates = new Dictionary<string, bool>();
            foreach (var item in treeNav.VisibleItems)
            {
                if (item.IsExpandable)
                {
                    string key = GetNodePath(item);
                    expansionStates[key] = item.IsExpanded;
                }
            }

            // Rebuild tree
            var root = BuildTree();
            treeNav.Initialize(root);

            // Restore expansion states
            foreach (var item in GetAllNodes(treeNav.RootItem))
            {
                if (item.IsExpandable)
                {
                    string key = GetNodePath(item);
                    if (expansionStates.TryGetValue(key, out bool wasExpanded))
                    {
                        item.IsExpanded = wasExpanded;
                    }
                }
            }

            // Rebuild visible list with restored expansion
            treeNav.RebuildVisibleList();

            // Try to find the same item by Data reference first
            if (oldData != null)
            {
                int foundIndex = -1;
                for (int i = 0; i < treeNav.VisibleItems.Count; i++)
                {
                    if (treeNav.VisibleItems[i].Data == oldData)
                    {
                        foundIndex = i;
                        break;
                    }
                }
                if (foundIndex >= 0)
                {
                    treeNav.SetSelectedIndex(foundIndex);
                    return;
                }
            }

            // Try to find by label within the same parent
            if (!string.IsNullOrEmpty(oldLabel) && !string.IsNullOrEmpty(oldParentLabel))
            {
                for (int i = 0; i < treeNav.VisibleItems.Count; i++)
                {
                    var item = treeNav.VisibleItems[i];
                    if (item.Label == oldLabel && item.Parent?.Label == oldParentLabel)
                    {
                        treeNav.SetSelectedIndex(i);
                        return;
                    }
                }
            }

            // If item was deleted, stay at the same index position (or move up if at end)
            int newIndex = Math.Min(oldIndex, treeNav.Count - 1);
            if (newIndex < 0) newIndex = 0;
            treeNav.SetSelectedIndex(newIndex);
        }

        /// <summary>
        /// Gets all nodes in the tree (for iteration).
        /// </summary>
        private static IEnumerable<InspectionTreeItem> GetAllNodes(InspectionTreeItem node)
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
        private static string GetNodePath(InspectionTreeItem node)
        {
            var parts = new List<string>();
            var current = node;
            while (current != null && current.IndentLevel >= 0)
            {
                parts.Insert(0, current.Label ?? "?");
                current = current.Parent;
            }
            return string.Join("/", parts);
        }

        /// <summary>
        /// Builds the tree structure for the caravan.
        /// </summary>
        private static InspectionTreeItem BuildTree()
        {
            var root = new InspectionTreeItem
            {
                Label = "Root",
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false
            };

            // Add main categories
            AddCaravanStatusNode(root);
            AddPawnsNode(root);
            AddGearNode(root);
            AddItemsNode(root);

            return root;
        }

        /// <summary>
        /// Adds the Caravan Status node with stats.
        /// </summary>
        private static void AddCaravanStatusNode(InspectionTreeItem parent)
        {
            var statusNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = "Caravan Status",
                IndentLevel = parent.IndentLevel + 1,
                IsExpandable = true,
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

        private static void AddStatNode(InspectionTreeItem parent, string label, string value, string tooltip = null)
        {
            var node = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = $"{label}: {value}",
                Tooltip = tooltip,  // Store tooltip for StatBreakdownState (Alt+I)
                IndentLevel = parent.IndentLevel + 1,
                Parent = parent,
                IsExpandable = false,
                // Store label separately in Data for stat breakdown identification
                Data = new StatNodeData { StatLabel = label, StatTooltip = tooltip }
            };

            parent.Children.Add(node);
        }

        /// <summary>
        /// Data class to store stat-specific info for Alt+I inspection.
        /// </summary>
        internal class StatNodeData
        {
            public string StatLabel { get; set; }
            public string StatTooltip { get; set; }
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
        private static void AddPawnsNode(InspectionTreeItem parent)
        {
            var pawns = currentCaravan.PawnsListForReading;
            var colonists = pawns.Where(p => p.IsColonist && !p.IsPrisoner).OrderBy(p => p.LabelShortCap).ToList();
            var prisoners = pawns.Where(p => p.IsPrisoner).OrderBy(p => p.LabelShortCap).ToList();
            var animals = pawns.Where(p => p.RaceProps.Animal).OrderBy(p => p.LabelShortCap).ToList();

            int totalPawns = colonists.Count + prisoners.Count + animals.Count;

            var pawnsNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = $"Pawns ({totalPawns})",
                IndentLevel = parent.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false,
                Parent = parent
            };

            // Find negotiator
            Pawn negotiator = BestCaravanPawnUtility.FindBestNegotiator(currentCaravan);

            // Add Colonists sub-category
            if (colonists.Count > 0)
            {
                var colonistsNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = $"Colonists ({colonists.Count})",
                    IndentLevel = pawnsNode.IndentLevel + 1,
                    IsExpandable = true,
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

                    var pawnNode = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = label,
                        IndentLevel = colonistsNode.IndentLevel + 1,
                        Parent = colonistsNode,
                        Data = pawn,
                        OnDelete = () => AbandonItem(pawn),
                        OnActivate = () => InspectPawn(pawn)
                    };
                    colonistsNode.Children.Add(pawnNode);
                }

                pawnsNode.Children.Add(colonistsNode);
            }

            // Add Prisoners sub-category
            if (prisoners.Count > 0)
            {
                var prisonersNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = $"Prisoners ({prisoners.Count})",
                    IndentLevel = pawnsNode.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = pawnsNode
                };

                foreach (var pawn in prisoners)
                {
                    var pawnNode = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = pawn.LabelShortCap,
                        IndentLevel = prisonersNode.IndentLevel + 1,
                        Parent = prisonersNode,
                        Data = pawn,
                        OnDelete = () => AbandonItem(pawn),
                        OnActivate = () => InspectPawn(pawn)
                    };
                    prisonersNode.Children.Add(pawnNode);
                }

                pawnsNode.Children.Add(prisonersNode);
            }

            // Add Animals sub-category
            if (animals.Count > 0)
            {
                var animalsNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = $"Animals ({animals.Count})",
                    IndentLevel = pawnsNode.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = pawnsNode
                };

                foreach (var animal in animals)
                {
                    var animalNode = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = animal.LabelShortCap,
                        IndentLevel = animalsNode.IndentLevel + 1,
                        Parent = animalsNode,
                        Data = animal,
                        OnDelete = () => AbandonItem(animal),
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
        private static void AddGearNode(InspectionTreeItem parent)
        {
            var humanlikePawns = currentCaravan.PawnsListForReading
                .Where(p => p.RaceProps.Humanlike && !p.Dead)
                .OrderBy(p => p.LabelShortCap)
                .ToList();

            int totalGear = humanlikePawns.Sum(p =>
                (p.equipment?.Primary != null ? 1 : 0) +
                (p.apparel?.WornApparel?.Count ?? 0));

            var gearNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = $"Gear ({totalGear} items)",
                IndentLevel = parent.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false,
                Parent = parent
            };

            foreach (var pawn in humanlikePawns)
            {
                int pawnGearCount = (pawn.equipment?.Primary != null ? 1 : 0) +
                                   (pawn.apparel?.WornApparel?.Count ?? 0);

                if (pawnGearCount == 0)
                    continue;

                var pawnGearNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = $"{pawn.LabelShortCap} ({pawnGearCount})",
                    IndentLevel = gearNode.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = gearNode,
                    Data = pawn
                };

                // Add weapon
                if (pawn.equipment?.Primary != null)
                {
                    var weapon = pawn.equipment.Primary;
                    pawnGearNode.Children.Add(new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = $"{pawn.LabelShortCap}'s {weapon.LabelCap}",
                        IndentLevel = pawnGearNode.IndentLevel + 1,
                        Parent = pawnGearNode,
                        Data = weapon,
                        OnDelete = () => AbandonItem(weapon),
                        OnActivate = () => OpenGearMenu(weapon, pawn)
                    });
                }

                // Add apparel
                if (pawn.apparel?.WornApparel != null)
                {
                    foreach (var apparel in pawn.apparel.WornApparel.OrderByDescending(a => a.def.apparel.bodyPartGroups.Count))
                    {
                        pawnGearNode.Children.Add(new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.Item,
                            Label = $"{pawn.LabelShortCap}'s {apparel.LabelCap}",
                            IndentLevel = pawnGearNode.IndentLevel + 1,
                            Parent = pawnGearNode,
                            Data = apparel,
                            OnDelete = () => AbandonItem(apparel),
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
        private static void AddItemsNode(InspectionTreeItem parent)
        {
            var inventoryItems = CaravanInventoryUtility.AllInventoryItems(currentCaravan)?.ToList();

            if (inventoryItems == null || inventoryItems.Count == 0)
            {
                var emptyNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Category,
                    Label = "Items (empty)",
                    IndentLevel = parent.IndentLevel + 1,
                    IsExpandable = false,
                    Parent = parent
                };
                parent.Children.Add(emptyNode);
                return;
            }

            int totalCount = inventoryItems.Sum(t => t.stackCount);

            // Use InventoryHelper for consistent categorization (same tree as colony inventory)
            var aggregatedItems = InventoryHelper.AggregateStacks(inventoryItems);
            var categoryTree = InventoryHelper.BuildCategoryTree(aggregatedItems);

            var itemsNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = $"Items ({totalCount})",
                IndentLevel = parent.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false,
                Parent = parent
            };

            // Convert InventoryHelper.CategoryNode tree to InspectionTreeItem tree
            AddInventoryCategoryNodes(itemsNode, categoryTree, inventoryItems);

            parent.Children.Add(itemsNode);
        }

        /// <summary>
        /// Recursively adds inventory category nodes from InventoryHelper tree.
        /// </summary>
        private static void AddInventoryCategoryNodes(InspectionTreeItem parent, List<InventoryHelper.CategoryNode> categoryNodes, List<Thing> allItems)
        {
            foreach (var categoryNode in categoryNodes)
            {
                var catNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = categoryNode.GetDisplayLabel(),
                    IndentLevel = parent.IndentLevel + 1,
                    IsExpandable = categoryNode.SubCategories.Count > 0 || categoryNode.Items.Count > 0,
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

                    var itemNode = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = invItem.GetDisplayLabel(),
                        IndentLevel = catNode.IndentLevel + 1,
                        Parent = catNode,
                        Data = representativeThing,  // Store actual Thing for abandon/inspect
                        OnDelete = representativeThing != null ? (Action)(() => AbandonItem(representativeThing)) : null,
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
        /// Abandons an item (pawn or thing) from the caravan.
        /// </summary>
        private static void AbandonItem(object itemData)
        {
            if (itemData is Pawn pawn)
            {
                CaravanAbandonOrBanishUtility.TryAbandonOrBanishViaInterface(pawn, currentCaravan);
            }
            else if (itemData is Thing thing)
            {
                CaravanAbandonOrBanishUtility.TryAbandonOrBanishViaInterface(thing, currentCaravan);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.CannotAbandon".Translate());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        /// <summary>
        /// Shows mood info for the selected pawn (Alt+M).
        /// </summary>
        private static void ShowPawnMood()
        {
            var item = treeNav.SelectedItem;
            if (item?.Data is Pawn pawn && pawn.needs?.mood != null)
            {
                string moodInfo = PawnInfoHelper.GetMoodInfo(pawn);
                TolkHelper.Speak(moodInfo);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.NoMoodForItem".Translate());
            }
        }

        /// <summary>
        /// Shows needs info for the selected pawn (Alt+N).
        /// </summary>
        private static void ShowPawnNeeds()
        {
            var item = treeNav.SelectedItem;
            if (item?.Data is Pawn pawn && pawn.needs != null)
            {
                string needsInfo = PawnInfoHelper.GetNeedsInfo(pawn);
                TolkHelper.Speak(needsInfo);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.NoNeedsForItem".Translate());
            }
        }

        /// <summary>
        /// Shows health info for the selected pawn (Alt+H).
        /// </summary>
        private static void ShowPawnHealth()
        {
            var item = treeNav.SelectedItem;
            if (item?.Data is Pawn pawn && pawn.health != null)
            {
                string healthInfo = PawnInfoHelper.GetHealthInfo(pawn);
                TolkHelper.Speak(healthInfo);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.NoHealthForItem".Translate());
            }
        }

        /// <summary>
        /// Shows gear info for the selected pawn (Alt+G).
        /// </summary>
        private static void ShowPawnGear()
        {
            var item = treeNav.SelectedItem;
            if (item?.Data is Pawn pawn)
            {
                string gearInfo = PawnInfoHelper.GetGearInfo(pawn);
                TolkHelper.Speak(gearInfo);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.NoGearForItem".Translate());
            }
        }

        /// <summary>
        /// Shows top skills for the selected pawn (Alt+K).
        /// </summary>
        private static void ShowPawnSkills()
        {
            var item = treeNav.SelectedItem;
            if (item?.Data is Pawn pawn)
            {
                string skillsInfo = PawnInfoHelper.GetTopSkillsInfo(pawn);
                TolkHelper.Speak(skillsInfo);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.NoSkillsForItem".Translate());
            }
        }

        #endregion

        #region Announcement Formatters

        private static string FormatItemAnnouncement(InspectionTreeItem item)
        {
            string label = item.Label.StripTags();

            // State indicator for expandable items
            string stateIndicator = TreeNavigationHelper.FormatExpansionSpaceSuffix(item);

            // Position among siblings
            var (position, total) = treeNav.GetSiblingPosition(item);
            string positionPart = MenuHelper.FormatPosition(position - 1, total);

            // Level suffix
            string levelSuffix = MenuHelper.GetLevelSuffix("CaravanInspect", item.IndentLevel);

            // Only add period separator if label doesn't already end with punctuation
            string separator = label.EndsWith(".") || label.EndsWith("!") || label.EndsWith("?") ? " " : ". ";
            string announcement = $"{label}{stateIndicator}{separator}{positionPart}{levelSuffix}";
            return announcement;
        }

        private static string FormatSearchAnnouncement(InspectionTreeItem item, TypeaheadSearchHelper typeahead)
        {
            string label = item.Label.StripTags();

            if (typeahead.HasActiveSearch)
            {
                string stateIndicator = TreeNavigationHelper.FormatExpansionSpaceSuffix(item);
                return typeahead.BuildItemAnnouncement($"{label}{stateIndicator}");
            }

            return FormatItemAnnouncement(item);
        }

        #endregion

        #region Custom Action Handlers

        private static bool HandleActivate(InspectionTreeItem item)
        {
            // Categories toggle expand/collapse via default behavior
            if (item.IsExpandable && !item.IsExpanded)
                return false;

            // Item's own OnActivate is handled by TreeNavigationHelper
            // For stat items with no OnActivate, just re-read the label
            if (item.Data is StatNodeData)
            {
                TolkHelper.Speak(item.Label);
                return true;
            }

            // If expandable and expanded, let default behavior handle (drill down)
            if (item.IsExpandable)
                return false;

            // For items without OnActivate
            if (item.OnActivate == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.NoActionAvailable".Translate());
                return true;
            }

            return false; // Let TreeNavigationHelper call item.OnActivate
        }

        private static bool HandleDelete(InspectionTreeItem item)
        {
            // Item's own OnDelete is handled by TreeNavigationHelper
            if (item.OnDelete != null)
                return false; // Let TreeNavigationHelper call it

            TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.CannotAbandon".Translate());
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            return true;
        }

        private static bool HandleInfo(InspectionTreeItem item)
        {
            // Stat with tooltip - open StatBreakdownState for navigable breakdown
            if (item.Data is StatNodeData statData && !string.IsNullOrEmpty(statData.StatTooltip))
            {
                StatBreakdownState.Open(statData.StatLabel, statData.StatTooltip);
                return true;
            }

            if (item.Data is Pawn pawn)
            {
                InspectPawn(pawn);
                return true;
            }

            if (item.Data is Thing thing)
            {
                InspectThing(thing);
                return true;
            }

            if (item.OnActivate != null)
            {
                // Has some action - execute it
                item.OnActivate();
                return true;
            }

            TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.NoBreakdown".Translate());
            return true;
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

            // Handle Alt shortcuts before delegating to TreeNavigationHelper
            // These are caravan-specific and not part of standard tree navigation

            // Alt+M: Mood
            if (key == KeyCode.M && alt && !shift && !ctrl)
            {
                ShowPawnMood();
                Event.current.Use();
                return true;
            }

            // Alt+N: Needs
            if (key == KeyCode.N && alt && !shift && !ctrl)
            {
                ShowPawnNeeds();
                Event.current.Use();
                return true;
            }

            // Alt+H: Health
            if (key == KeyCode.H && alt && !shift && !ctrl)
            {
                ShowPawnHealth();
                Event.current.Use();
                return true;
            }

            // Alt+G: Gear
            if (key == KeyCode.G && alt && !shift && !ctrl)
            {
                ShowPawnGear();
                Event.current.Use();
                return true;
            }

            // Alt+K: Skills
            if (key == KeyCode.K && alt && !shift && !ctrl)
            {
                ShowPawnSkills();
                Event.current.Use();
                return true;
            }

            // Delegate to TreeNavigationHelper for standard tree navigation
            Event ev = Event.current;
            if (ev.type == EventType.KeyDown)
            {
                bool handled = treeNav.HandleInput(ev);
                if (handled)
                {
                    ev.Use();
                    return true;
                }

                // TreeNavigationHelper returns false for Escape with no active search
                if (key == KeyCode.Escape)
                {
                    Close();
                    ev.Use();
                    return true;
                }
            }

            // Block ALL unhandled keys to prevent game's native handlers from processing them
            // This makes the overlay screen modal - it captures all keyboard input while active
            return true;
        }

        #endregion
    }
}
