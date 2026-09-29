using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// The DATA facade behind the windowless caravan inspection screen (I, or Enter on a selected
    /// caravan on the world map): which caravan is inspected, the category tree built for it, the
    /// abandon/inspect/gear actions its rows carry, and the per-pawn readouts Alt+M/N/H/G/K speak.
    /// It owns the tree's CONTENT and lifecycle and none of its keyboard behaviour — cursor,
    /// typeahead, expansion state and every announcement live in <see cref="CaravanInspectScope"/>.
    /// </summary>
    public static class CaravanInspectState
    {
        private static bool isActive = false;
        private static Caravan currentCaravan = null;

        /// <summary>A built tree waiting for a scope to receive it (see <see cref="TakePendingRoot"/>).</summary>
        private static InspectionTreeItem pendingRoot;

        // Track caravan contents to detect changes (for auto-refresh after abandon)
        private static int lastKnownPawnCount = 0;
        private static int lastKnownItemCount = 0;

        public static bool IsActive => isActive;

        public static Caravan CurrentCaravan => currentCaravan;

        /// <summary>
        /// Builds caravan category nodes (Caravan Status, Pawns, Gear, Items) under a parent, so
        /// <see cref="WorldObjectSelectionState"/> can embed the tree without opening this screen.
        /// </summary>
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

        /// <summary>Opens the caravan inspect screen for the specified caravan.</summary>
        public static void Open(Caravan caravan)
        {
            if (caravan == null)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.NoCaravanSpecified".Loc(), SpeechPriority.High);
                return;
            }

            isActive = true;
            currentCaravan = caravan;

            var root = BuildTreeRoot();
            UpdateTrackedCounts();

            TolkHelper.SpeakData(caravan.Name);

            // The mirror does not push the scope until the reconcile pass after this returns, so the
            // tree is handed over directly here; the scope exists from the first OnGUI pass.
            CaravanInspectScope live = CaravanInspectScope.Live;
            if (live != null)
            {
                pendingRoot = null;
                live.OpenTree(root);
                return;
            }
            pendingRoot = root;
        }

        /// <summary>Drains a tree built before any scope existed to receive it; null otherwise.</summary>
        internal static InspectionTreeItem TakePendingRoot()
        {
            InspectionTreeItem root = pendingRoot;
            pendingRoot = null;
            return root;
        }

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
        /// Rebuilds the tree when the caravan's contents changed, so an abandon that happened inside a
        /// confirmation dialog is picked up before the next key is processed. Called from
        /// <see cref="CaravanInspectScope.RefreshContent"/>, which runs before every describe cycle.
        /// </summary>
        internal static void CheckForChangesAndRefresh()
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

        /// <summary>Closes the caravan inspect screen.</summary>
        public static void Close()
        {
            isActive = false;
            currentCaravan = null;
            pendingRoot = null;
            CaravanInspectScope live = CaravanInspectScope.Live;
            if (live != null)
            {
                live.ClearTree();
            }
            TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.Closed".Loc());
        }

        /// <summary>
        /// Refreshes the tree structure (after gear changes or item abandonment). The rebuild itself —
        /// expansion carried over by node path, cursor restored by data object then label then
        /// position — lives in <see cref="CaravanInspectScope.RebuildTree"/>.
        /// </summary>
        public static void RefreshTree()
        {
            if (!IsActive || currentCaravan == null)
                return;

            CaravanInspectScope live = CaravanInspectScope.Live;
            if (live != null)
            {
                live.RebuildTree();
            }
        }

        /// <summary>Builds the tree structure for the caravan; internal so the scope can rebuild it after the contents change.</summary>
        internal static InspectionTreeItem BuildTreeRoot()
        {
            var root = new InspectionTreeItem
            {
                Label = "Root",
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false
            };

            AddCaravanStatusNode(root);
            AddPawnsNode(root);
            AddGearNode(root);
            AddItemsNode(root);

            return root;
        }

        /// <summary>Adds the Caravan Status node with stats.</summary>
        private static void AddCaravanStatusNode(InspectionTreeItem parent)
        {
            var statusNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = "RimWorldAccess.Caravan.Inspect.CategoryCaravanStatus".Translate(),
                IndentLevel = parent.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false,
                Parent = parent
            };

            AddStatNode(statusNode, (string)"RimWorldAccess.Caravan.Inspect.StatLocation".Translate(), GetLocationString());

            string massTooltip = GetMassTooltip();
            AddStatNode(statusNode, (string)"RimWorldAccess.Caravan.Inspect.StatMass".Translate(), GetMassString(), massTooltip);

            string statusTooltip = GetStatusTooltip();
            AddStatNode(statusNode, (string)"RimWorldAccess.Caravan.Inspect.StatStatus".Translate(), GetMovementStatus(), statusTooltip);

            // Immobile when overloaded, else tiles/day, as Gizmo_CaravanInfo shows it.
            string speedDescription = "CaravanMovementSpeedTip".Translate();
            string speedLabel = "RimWorldAccess.Caravan.Inspect.StatSpeed".Translate();
            if (currentCaravan.MassUsage > currentCaravan.MassCapacity)
            {
                // Matches game's GetMovementSpeedLabel when immobile
                string immobile = "RimWorldAccess.Caravan.Inspect.Immobile".Translate();
                AddStatNode(statusNode, speedLabel, $"{immobile}. {speedDescription}");
            }
            else
            {
                var speedExplanation = new StringBuilder();
                float tilesPerDay = TilesPerDayCalculator.ApproxTilesPerDay(currentCaravan, speedExplanation);
                // Game format: {tilesPerDay:0.#} tiles/day + description (vanilla "TilesPerDay" unit)
                string tilesPerDayUnit = "TilesPerDay".Translate();
                AddStatNode(statusNode, speedLabel, $"{tilesPerDay:0.#} {tilesPerDayUnit}. {speedDescription}", speedExplanation.ToString());
            }

            // Matches CaravanUIUtility.GetDaysWorthOfFoodLabel.
            string foodDescription = "DaysWorthOfFoodTooltip".Translate();
            string foodStatLabel = "RimWorldAccess.Caravan.Inspect.StatFood".Translate();
            try
            {
                var foodInfo = currentCaravan.DaysWorthOfFood;
                string foodValue;

                if (foodInfo.days >= DaysWorthOfFoodCalculator.InfiniteDaysWorthOfFood)
                {
                    foodValue = "Infinite".Translate();
                }
                else
                {
                    // Game format: {days:0.#} (shows "3" not "3.0"); vanilla "PeriodDays" unit
                    foodValue = "PeriodDays".Translate(foodInfo.days.ToString("0.#"));

                    // Rot only when the food is perishable AND will rot before running out, as
                    // CaravanUIUtility.GetDaysWorthOfFoodLabel does.
                    if (foodInfo.tillRot < DaysWorthOfFoodCalculator.InfiniteDaysWorthOfFood && foodInfo.tillRot < foodInfo.days)
                    {
                        foodValue += " " + (string)"RimWorldAccess.Caravan.Inspect.DaysUntilRot".Translate(foodInfo.tillRot.ToString("0.#"));
                    }
                }

                if (currentCaravan.needs.AnyPawnOutOfFood(out string malnutritionInfo))
                {
                    foodValue += " - " + (string)"RimWorldAccess.Caravan.Inspect.OutOfFood".Translate();
                    if (!string.IsNullOrEmpty(malnutritionInfo))
                    {
                        foodValue += $" ({malnutritionInfo})";
                    }
                }

                foodValue += ". " + foodDescription;
                AddStatNode(statusNode, foodStatLabel, foodValue);
            }
            catch
            {
                AddStatNode(statusNode, foodStatLabel, (string)"RimWorldAccess.Caravan.Inspect.Unknown".Translate());
            }

            // Foraging, in the game's own "{perDay:0.#} ({food.label})" format.
            try
            {
                var forageInfo = currentCaravan.forage.ForagedFoodPerDay;
                if (forageInfo.perDay > 0f)
                {
                    string forageTooltip = currentCaravan.forage.ForagedFoodPerDayExplanation;
                    string forageDescription = "ForagedFoodPerDayTip".Translate();
                    string foragedFoodLabel = forageInfo.food?.label ?? (string)"RimWorldAccess.Caravan.Inspect.FoodFallback".Translate();
                    string foragingValue = (string)"RimWorldAccess.Caravan.Inspect.ForagingPerDay".Translate(forageInfo.perDay.ToString("0.#"), foragedFoodLabel);
                    AddStatNode(statusNode, (string)"RimWorldAccess.Caravan.Inspect.StatForaging".Translate(), foragingValue + ". " + forageDescription, forageTooltip);
                }
            }
            catch { }

            if (currentCaravan.pather?.Moving == true && currentCaravan.pather.Destination.Valid)
            {
                AddStatNode(statusNode, (string)"RimWorldAccess.Caravan.Inspect.StatDestination".Translate(), GetDestinationString());
                AddStatNode(statusNode, (string)"RimWorldAccess.Caravan.Inspect.StatETA".Translate(), GetETAString());
            }

            // Visibility, with the game's own tip.
            string visDescription = "CaravanVisibilityTip".Translate();
            string visTooltip = currentCaravan.VisibilityExplanation;
            AddStatNode(statusNode, (string)"RimWorldAccess.Caravan.Inspect.StatVisibility".Translate(), $"{currentCaravan.Visibility:P0}. {visDescription}", visTooltip);

            if (!currentCaravan.pather?.MovingNow == true && currentCaravan.beds != null)
            {
                int bedCount = currentCaravan.beds.GetUsedBedCount();
                string bedLabel = bedCount > 0
                    ? (string)"RimWorldAccess.Caravan.Inspect.BedrollsInUse".Translate(bedCount)
                    : (string)"RimWorldAccess.Caravan.Inspect.NoBedrolls".Translate();
                AddStatNode(statusNode, (string)"RimWorldAccess.Caravan.Inspect.StatBeds".Translate(), bedLabel);
            }

            parent.Children.Add(statusNode);
        }

        /// <summary>The mass tooltip, built around the game's own MassCapacityExplanation.</summary>
        private static string GetMassTooltip()
        {
            string gameExplanation = currentCaravan.MassCapacityExplanation;

            var sb = new StringBuilder();
            sb.AppendLine("RimWorldAccess.Caravan.Inspect.TooltipMassCarried".Translate(currentCaravan.MassUsage.ToString("F1")));
            sb.AppendLine("RimWorldAccess.Caravan.Inspect.TooltipMassCapacity".Translate(currentCaravan.MassCapacity.ToString("F1")));

            if (currentCaravan.MassUsage > currentCaravan.MassCapacity)
            {
                sb.AppendLine("RimWorldAccess.Caravan.Inspect.TooltipOverloaded".Translate());
            }
            else
            {
                float remaining = currentCaravan.MassCapacity - currentCaravan.MassUsage;
                sb.AppendLine("RimWorldAccess.Caravan.Inspect.TooltipRemainingCapacity".Translate(remaining.ToString("F1")));
            }

            if (!string.IsNullOrEmpty(gameExplanation))
            {
                sb.AppendLine();
                sb.AppendLine("RimWorldAccess.Caravan.Inspect.TooltipCapacityBreakdown".Translate());
                sb.Append(gameExplanation);
            }

            return sb.ToString();
        }

        /// <summary>The status tooltip.</summary>
        private static string GetStatusTooltip()
        {
            var sb = new StringBuilder();

            if (currentCaravan.CantMove)
            {
                sb.AppendLine("RimWorldAccess.Caravan.Inspect.StatusCannotMoveBecause".Translate());
                if (currentCaravan.AllOwnersDowned)
                    sb.AppendLine("- " + (string)"RimWorldAccess.Caravan.Inspect.StatusAllDowned".Translate());
                if (currentCaravan.AllOwnersHaveMentalBreak)
                    sb.AppendLine("- " + (string)"RimWorldAccess.Caravan.Inspect.StatusAllMentalBreak".Translate());
                if (currentCaravan.ImmobilizedByMass)
                    sb.AppendLine("- " + (string)"RimWorldAccess.Caravan.Inspect.StatusOverloaded".Translate());
            }
            else if (currentCaravan.NightResting)
            {
                sb.AppendLine("RimWorldAccess.Caravan.Inspect.StatusNightResting".Translate());
                int bedCount = currentCaravan.beds?.GetUsedBedCount() ?? 0;
                if (bedCount > 0)
                    sb.AppendLine("RimWorldAccess.Caravan.Inspect.StatusUsingBedrolls".Translate(bedCount));
                else
                    sb.AppendLine("RimWorldAccess.Caravan.Inspect.StatusNoBedrollsGround".Translate());
            }
            else if (currentCaravan.pather?.Moving == true)
            {
                if (currentCaravan.pather.Paused)
                    sb.AppendLine("RimWorldAccess.Caravan.Inspect.StatusPaused".Translate());
                else
                    sb.AppendLine("RimWorldAccess.Caravan.Inspect.StatusTraveling".Translate());
            }
            else
            {
                sb.AppendLine("RimWorldAccess.Caravan.Inspect.StatusStopped".Translate());
            }

            return sb.ToString();
        }

        private static void AddStatNode(InspectionTreeItem parent, string label, string value, string tooltip = null)
        {
            var node = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = (string)"RimWorldAccess.Caravan.Inspect.StatLabelValue".Translate(label, value),
                Tooltip = tooltip,  // Store tooltip for StatBreakdownState (Alt+I)
                IndentLevel = parent.IndentLevel + 1,
                Parent = parent,
                IsExpandable = false,
                // Store label separately in Data for stat breakdown identification
                Data = new StatNodeData { StatLabel = label, StatTooltip = tooltip }
            };

            parent.Children.Add(node);
        }

        /// <summary>Stat-specific info for the Alt+I breakdown.</summary>
        internal class StatNodeData
        {
            public string StatLabel { get; set; }
            public string StatTooltip { get; set; }
        }

        private static string GetLocationString()
        {
            if (currentCaravan.Tile.Valid && Find.WorldGrid != null)
                return DescribeTile(currentCaravan.Tile);
            return (string)"RimWorldAccess.Caravan.Inspect.Unknown".Translate();
        }

        private static string DescribeTile(PlanetTile tile)
        {
            Vector2 longLat = Find.WorldGrid.LongLatOf(tile);
            string coords = longLat.y.ToStringLatitude() + " " + longLat.x.ToStringLongitude();
            string biome = tile.Tile?.PrimaryBiome?.LabelCap;
            return string.IsNullOrEmpty(biome)
                ? coords
                : (string)"RimWorldAccess.Caravan.Inspect.TilePlace".Translate(biome, coords);
        }

        private static string GetMassString()
        {
            // Matches game's CaravanUIUtility format: {massUsage:F0} / {massCapacity:F0} kg
            float massUsage = currentCaravan.MassUsage;
            float massCapacity = currentCaravan.MassCapacity;
            return (string)"RimWorldAccess.Caravan.Inspect.MassUsageCapacity".Translate(
                massUsage.ToString("F0"), massCapacity.ToString("F0"));
        }

        private static string GetMovementStatus()
        {
            // Use WorldInfoHelper for consistent status display with comma/period cycling
            string status = WorldInfoHelper.GetCaravanStatus(currentCaravan);
            if (!string.IsNullOrEmpty(status))
            {
                return char.ToUpper(status[0]) + status.Substring(1);
            }
            return status;
        }

        private static string GetDestinationString()
        {
            if (currentCaravan.pather?.Destination.Valid != true)
                return (string)"None".Translate();

            PlanetTile destTile = currentCaravan.pather.Destination;
            Settlement destSettlement = Find.WorldObjects?.SettlementAt(destTile);
            if (destSettlement != null)
                return destSettlement.Label;
            return DescribeTile(destTile);
        }

        private static string GetETAString()
        {
            if (currentCaravan.pather?.Destination.Valid != true)
                return (string)"RimWorldAccess.Caravan.Inspect.NotAvailable".Translate();

            float ticksToArrive = CaravanArrivalTimeEstimator.EstimatedTicksToArrive(
                currentCaravan.Tile, currentCaravan.pather.Destination, currentCaravan);
            if (ticksToArrive > 0)
            {
                float hoursToArrive = ticksToArrive / 2500f;
                float daysToArrive = hoursToArrive / 24f;
                return daysToArrive >= 1f
                    ? (string)"PeriodDays".Translate(daysToArrive.ToString("F1"))
                    : (string)"PeriodHours".Translate(hoursToArrive.ToString("F1"));
            }
            return (string)"RimWorldAccess.Caravan.Inspect.Unknown".Translate();
        }

        /// <summary>Adds the Pawns node with one sub-category per vanilla pawn section.</summary>
        private static void AddPawnsNode(InspectionTreeItem parent)
        {
            var pawns = currentCaravan.PawnsListForReading;

            var pawnsNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = (string)"RimWorldAccess.Caravan.Inspect.CategoryPawns".Translate(pawns.Count),
                IndentLevel = parent.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false,
                Parent = parent
            };

            Pawn negotiator = BestCaravanPawnUtility.FindBestNegotiator(currentCaravan);

            // MUTATION-C: mirrors RimWorld.Planet.CaravanUIUtility.AddPawnsSections;
            // vanilla only feeds these predicates into a TransferableOneWayWidget we
            // do not host, so the section list, order, predicates and labels below
            // are hand-copied from the decompiled method rather than called directly.
            // A pawn matching multiple predicates (e.g. a downed slave who is also
            // capturable) appears in each matching section, same as vanilla's widget.
            AddPawnsSection(pawnsNode, "ColonistsSection".Translate(), pawns.Where(p => p.IsFreeNonSlaveColonist), negotiator, showTitleAndNegotiator: true);
            if (ModsConfig.IdeologyActive)
            {
                AddPawnsSection(pawnsNode, "SlavesSection".Translate(), pawns.Where(p => p.IsSlave), negotiator, showTitleAndNegotiator: true);
            }
            AddPawnsSection(pawnsNode, "PrisonersSection".Translate(), pawns.Where(p => p.IsPrisoner), negotiator, showTitleAndNegotiator: false);
            AddPawnsSection(pawnsNode, "CaptureSection".Translate(), pawns.Where(p => p.Downed && CaravanUtility.ShouldAutoCapture(p, Faction.OfPlayer)), negotiator, showTitleAndNegotiator: false);
            AddPawnsSection(pawnsNode, "AnimalsSection".Translate(), pawns.Where(p => p.IsAnimal), negotiator, showTitleAndNegotiator: false);
            if (ModsConfig.BiotechActive)
            {
                AddPawnsSection(pawnsNode, "MechsSection".Translate(), pawns.Where(p => p.IsColonyMech && p.OverseerSubject != null && p.OverseerSubject.State == OverseerSubjectState.Overseen), negotiator, showTitleAndNegotiator: false);
            }
            if (ModsConfig.AnomalyActive)
            {
                AddPawnsSection(pawnsNode, "EntitiesSection".Translate(), pawns.Where(p => p.IsColonySubhuman && p.mutant.Def.canTravelInCaravan), negotiator, showTitleAndNegotiator: false);
            }

            parent.Children.Add(pawnsNode);
        }

        /// <summary>
        /// Builds one pawn sub-category (Colonists, Slaves, Prisoners, ...) under the Pawns node,
        /// skipping an empty section. LabelShortCap ordering within a section is a mod-side choice for
        /// readable navigation; vanilla's own widget does not sort by it.
        /// </summary>
        private static void AddPawnsSection(InspectionTreeItem pawnsNode, string sectionLabel, IEnumerable<Pawn> sectionPawns, Pawn negotiator, bool showTitleAndNegotiator)
        {
            var pawnList = sectionPawns.OrderBy(p => p.LabelShortCap).ToList();
            if (pawnList.Count == 0)
                return;

            var sectionNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = (string)"RimWorldAccess.Caravan.Inspect.SectionWithCount".Translate(sectionLabel, pawnList.Count),
                IndentLevel = pawnsNode.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false,
                Parent = pawnsNode
            };

            foreach (var pawn in pawnList)
            {
                string label = pawn.LabelShortCap;
                if (showTitleAndNegotiator)
                {
                    if (pawn.story?.TitleCap != null && !pawn.story.TitleCap.NullOrEmpty())
                        label += $", {pawn.story.TitleCap}";
                    if (pawn == negotiator)
                        label += ", " + (string)"RimWorldAccess.Caravan.Inspect.Negotiator".Translate();
                }

                var pawnNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = label,
                    IndentLevel = sectionNode.IndentLevel + 1,
                    Parent = sectionNode,
                    Data = pawn,
                    OnDelete = () => AbandonItem(pawn),
                    OnActivate = () => InspectPawn(pawn)
                };
                sectionNode.Children.Add(pawnNode);
            }

            pawnsNode.Children.Add(sectionNode);
        }

        /// <summary>Adds the Gear node with per-pawn gear.</summary>
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
                Label = (string)"RimWorldAccess.Caravan.Inspect.CategoryGear".Translate(totalGear),
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
                    Label = (string)"RimWorldAccess.Caravan.Inspect.SectionWithCount".Translate((string)pawn.LabelShortCap, pawnGearCount),
                    IndentLevel = gearNode.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = gearNode,
                    Data = pawn
                };

                if (pawn.equipment?.Primary != null)
                {
                    var weapon = pawn.equipment.Primary;
                    pawnGearNode.Children.Add(new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = (string)"RimWorldAccess.Caravan.Inspect.PawnPossession".Translate((string)pawn.LabelShortCap, (string)weapon.LabelCap),
                        IndentLevel = pawnGearNode.IndentLevel + 1,
                        Parent = pawnGearNode,
                        Data = weapon,
                        OnDelete = () => AbandonItem(weapon),
                        OnActivate = () => OpenGearMenu(weapon, pawn)
                    });
                }

                if (pawn.apparel?.WornApparel != null)
                {
                    // MUTATION-C: mirrors RimWorld.ITab_Pawn_Gear.FillTab (decompiled
                    // ITab_Pawn_Gear.cs ~150-161): vanilla draws belts in its Equipment
                    // list ahead of the Apparel list, then orders the Apparel list by
                    // each item's primary body part group's list order, descending.
                    var orderedApparel = pawn.apparel.WornApparel
                        .OrderByDescending(a => a.def.apparel.layers.Contains(ApparelLayerDefOf.Belt))
                        .ThenByDescending(a => a.def.apparel.bodyPartGroups[0].listOrder);
                    foreach (var apparel in orderedApparel)
                    {
                        pawnGearNode.Children.Add(new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.Item,
                            Label = (string)"RimWorldAccess.Caravan.Inspect.PawnPossession".Translate((string)pawn.LabelShortCap, (string)apparel.LabelCap),
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

        /// <summary>Adds the Items node through InventoryHelper, so the category tree matches the colony inventory's.</summary>
        private static void AddItemsNode(InspectionTreeItem parent)
        {
            var inventoryItems = CaravanInventoryUtility.AllInventoryItems(currentCaravan)?.ToList();

            if (inventoryItems == null || inventoryItems.Count == 0)
            {
                var emptyNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Category,
                    Label = "RimWorldAccess.Caravan.Inspect.CategoryItemsEmpty".Translate(),
                    IndentLevel = parent.IndentLevel + 1,
                    IsExpandable = false,
                    Parent = parent
                };
                parent.Children.Add(emptyNode);
                return;
            }

            int totalCount = inventoryItems.Sum(t => t.stackCount);

            var aggregatedItems = InventoryHelper.AggregateStacks(inventoryItems);
            var categoryTree = InventoryHelper.BuildCategoryTree(aggregatedItems);

            var itemsNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = (string)"RimWorldAccess.Caravan.Inspect.CategoryItems".Translate(totalCount),
                IndentLevel = parent.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false,
                Parent = parent
            };

            AddInventoryCategoryNodes(itemsNode, categoryTree, inventoryItems);

            parent.Children.Add(itemsNode);
        }

        /// <summary>Recursively adds inventory category nodes from an InventoryHelper tree.</summary>
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

        /// <summary>Abandons an item (pawn or thing) from the caravan.</summary>
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
                TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.CannotAbandon".Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        /// <summary>The Alt+M readout for the focused row's pawn; says none is available for any other row.</summary>
        internal static void ShowPawnMood(InspectionTreeItem item)
        {
            if (item?.Data is Pawn pawn && pawn.needs?.mood != null)
            {
                string moodInfo = PawnInfoHelper.GetMoodInfo(pawn);
                TolkHelper.SpeakData(moodInfo);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.NoMoodForItem".Loc());
            }
        }

        /// <summary>The Alt+N readout for the focused row's pawn; says none is available for any other row.</summary>
        internal static void ShowPawnNeeds(InspectionTreeItem item)
        {
            if (item?.Data is Pawn pawn && pawn.needs != null)
            {
                string needsInfo = PawnInfoHelper.GetNeedsInfo(pawn);
                TolkHelper.SpeakData(needsInfo);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.NoNeedsForItem".Loc());
            }
        }

        /// <summary>The Alt+H readout for the focused row's pawn; says none is available for any other row.</summary>
        internal static void ShowPawnHealth(InspectionTreeItem item)
        {
            if (item?.Data is Pawn pawn && pawn.health != null)
            {
                string healthInfo = PawnInfoHelper.GetHealthInfo(pawn);
                TolkHelper.SpeakData(healthInfo);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.NoHealthForItem".Loc());
            }
        }

        /// <summary>The Alt+G readout for the focused row's pawn; says none is available for any other row.</summary>
        internal static void ShowPawnGear(InspectionTreeItem item)
        {
            if (item?.Data is Pawn pawn)
            {
                string gearInfo = PawnInfoHelper.GetGearInfo(pawn);
                TolkHelper.SpeakData(gearInfo);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.NoGearForItem".Loc());
            }
        }

        /// <summary>The Alt+K readout for the focused row's pawn; says none is available for any other row.</summary>
        internal static void ShowPawnSkills(InspectionTreeItem item)
        {
            if (item?.Data is Pawn pawn)
            {
                string skillsInfo = PawnInfoHelper.GetTopSkillsInfo(pawn);
                TolkHelper.SpeakData(skillsInfo);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.NoSkillsForItem".Loc());
            }
        }

        #endregion

        #region Alt+I

        /// <summary>
        /// Alt+I on a tree row: a navigable breakdown for a stat that carries one, a real info card for
        /// a pawn or thing row, the row's own action when it has one, else the no-breakdown
        /// announcement. The same nodes reached through <see cref="WorldObjectSelectionScope"/> get
        /// that scope's generic fallback instead.
        /// </summary>
        internal static void ShowInfoFor(InspectionTreeItem item)
        {
            if (item.Data is StatNodeData statData && !string.IsNullOrEmpty(statData.StatTooltip))
            {
                StatBreakdownState.Open(statData.StatLabel, statData.StatTooltip);
                return;
            }

            if (item.Data is Pawn pawn)
            {
                InspectPawn(pawn);
                return;
            }

            if (item.Data is Thing thing)
            {
                InspectThing(thing);
                return;
            }

            if (item.OnActivate != null)
            {
                item.OnActivate();
                return;
            }

            TolkHelper.Speak("RimWorldAccess.Caravan.Inspect.NoBreakdown".Loc());
        }

        #endregion
    }
}
