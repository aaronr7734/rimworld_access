using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Sound;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// State management for keyboard navigation in Dialog_FormCaravan.
    /// Provides three-tab interface for selecting pawns, items, and travel supplies.
    /// Tab key toggles summary view with caravan stats.
    /// </summary>
    public static class CaravanFormationState
    {
        private enum Tab
        {
            Pawns,
            Items,
            TravelSupplies
        }

        private const int TabCount = 3;

        private static bool isActive = false;
        private static Dialog_FormCaravan currentDialog = null;
        private static Tab currentTab = Tab.Pawns;
        private static int selectedIndex = 0;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        // Summary toggle state (Tab key to quickly view stats)
        private static bool showingSummary = false;
        private static Tab savedTab = Tab.Pawns;
        private static int savedIndex = 0;

        // Summary navigation (up/down arrows to navigate through stats)
        private static List<string> summaryItems = new List<string>();
        private static int summaryIndex = 0;

        // Flag to track if summary instructions have been shown this session (not reset on Close)
        private static bool summaryInstructionsShown = false;

        // Track if we're choosing destination (route planning mode)
        private static bool isChoosingDestination = false;

        // Flag to skip activation when route planner is being opened first
        private static bool pendingRoutePlannerOpen = false;

        // Auto-provision toggle state
        private static bool autoProvisionEnabled = false;
        private static Dictionary<TransferableOneWay, int> savedSupplyAmounts = new Dictionary<TransferableOneWay, int>();

        // Position memory per tab - preserves selected index when switching tabs
        private static Dictionary<Tab, int> tabPositions = new Dictionary<Tab, int>();

        // Flag to track if send was attempted (to avoid announcing "cancelled" on successful send)
        private static bool sendAttempted = false;

        /// <summary>
        /// Gets whether caravan formation keyboard navigation is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets whether a send was attempted (used by PostClose to decide announcement).
        /// </summary>
        public static bool SendAttempted => sendAttempted;

        /// <summary>
        /// Resets the send attempted flag. Called when user cancels a confirmation dialog
        /// (e.g., "Go back" on the low food warning) to restore normal cancel behavior.
        /// </summary>
        public static void ResetSendAttempted()
        {
            sendAttempted = false;
        }

        /// <summary>
        /// Gets whether typeahead search is currently active.
        /// Used by Window.OnCancelKeyPressed patch to block dialog close.
        /// </summary>
        public static bool HasActiveTypeahead => typeahead.HasActiveSearch;

        /// <summary>
        /// Gets whether we're currently in destination choosing mode.
        /// </summary>
        public static bool IsChoosingDestination => isChoosingDestination;

        /// <summary>
        /// Gets whether auto-provision is enabled (supplies tab is read-only).
        /// </summary>
        public static bool AutoProvisionEnabled => autoProvisionEnabled;

        /// <summary>
        /// Gets or sets whether route planner open is pending.
        /// Set to true before adding dialog to window stack when route planner should open first.
        /// </summary>
        public static bool PendingRoutePlannerOpen
        {
            get => pendingRoutePlannerOpen;
            set => pendingRoutePlannerOpen = value;
        }

        /// <summary>
        /// Triggers caravan reformation from the current map (for temporary encounter maps after ambushes).
        /// </summary>
        public static void TriggerReformation()
        {
            if (!GuardHelper.RequireMap(out Map currentMap, SpeechPriority.High)) return;

            if (currentMap.IsPlayerHome)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.CannotReformFromHome".Translate(), SpeechPriority.High);
                return;
            }

            MapParent mapParent = currentMap.Parent;
            if (mapParent == null)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.MapHasNoParent".Translate(), SpeechPriority.High);
                return;
            }

            FormCaravanComp formCaravanComp = mapParent.GetComponent<FormCaravanComp>();
            if (formCaravanComp == null)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.MapDoesNotSupport".Translate(), SpeechPriority.High);
                return;
            }

            if (!formCaravanComp.CanFormOrReformCaravanNow)
            {
                if (GenHostility.AnyHostileActiveThreatToPlayer(currentMap, countDormantPawnsAsHostile: false))
                {
                    TolkHelper.Speak("RimWorldAccess.Caravan.Form.CannotReformEnemiesPresent".Translate(), SpeechPriority.High);
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Caravan.Form.CannotReformAtThisTime".Translate(), SpeechPriority.High);
                }
                return;
            }

            Dialog_FormCaravan reformDialog = new Dialog_FormCaravan(currentMap, reform: true);
            Find.WindowStack.Add(reformDialog);

            TolkHelper.Speak("RimWorldAccess.Caravan.Form.OpeningReformation".Translate(), SpeechPriority.Normal);
        }

        /// <summary>
        /// Opens keyboard navigation for the specified Dialog_FormCaravan.
        /// </summary>
        public static void Open(Dialog_FormCaravan dialog)
        {
            if (dialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.NoFormationDialog".Translate(), SpeechPriority.High);
                return;
            }

            isActive = true;
            currentDialog = dialog;
            currentTab = Tab.Pawns;
            selectedIndex = 0;
            showingSummary = false;
            isChoosingDestination = false;
            typeahead.ClearSearch();

            // Disable auto-select travel supplies to prevent it from resetting our manual selections
            DisableAutoSelectTravelSupplies();

            TolkHelper.Speak("RimWorldAccess.Caravan.Form.OpenInstructions".Translate());
            AnnounceCurrentTab();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Closes keyboard navigation.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentDialog = null;
            currentTab = Tab.Pawns;
            selectedIndex = 0;
            showingSummary = false;
            isChoosingDestination = false;
            autoProvisionEnabled = false;
            savedSupplyAmounts.Clear();
            tabPositions.Clear();
            summaryItems.Clear();
            summaryIndex = 0;
            typeahead.ClearSearch();
            sendAttempted = false;
        }

        /// <summary>
        /// Gets the transferables list from the current dialog.
        /// </summary>
        private static List<TransferableOneWay> GetTransferables()
        {
            if (currentDialog == null)
                return new List<TransferableOneWay>();

            try
            {
                // transferables is public in Dialog_FormCaravan
                if (currentDialog.transferables != null)
                {
                    return currentDialog.transferables;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"RimWorld Access: Failed to get transferables from Dialog_FormCaravan: {ex.Message}");
            }

            return new List<TransferableOneWay>();
        }

        /// <summary>
        /// Gets transferables for the current tab.
        /// </summary>
        private static List<TransferableOneWay> GetCurrentTabTransferables()
        {
            List<TransferableOneWay> allTransferables = GetTransferables();
            return CaravanUIHelper.FilterByCategory(allTransferables, GetCategoryForTab(currentTab));
        }

        /// <summary>
        /// Maps the local Tab enum to CaravanUIHelper.TransferableCategory.
        /// </summary>
        private static CaravanUIHelper.TransferableCategory GetCategoryForTab(Tab tab)
        {
            switch (tab)
            {
                case Tab.Pawns: return CaravanUIHelper.TransferableCategory.Pawns;
                case Tab.TravelSupplies: return CaravanUIHelper.TransferableCategory.FoodAndMedicine;
                case Tab.Items: return CaravanUIHelper.TransferableCategory.Items;
                default: return CaravanUIHelper.TransferableCategory.Pawns;
            }
        }

        /// <summary>
        /// Announces the current tab.
        /// </summary>
        private static void AnnounceCurrentTab()
        {
            string tabName = GetTabName(currentTab);
            List<TransferableOneWay> tabTransferables = GetCurrentTabTransferables();
            TolkHelper.Speak("RimWorldAccess.Caravan.Form.TabHeader".Translate(tabName, tabTransferables.Count));
        }

        /// <summary>
        /// Announces the currently selected item.
        /// </summary>
        private static void AnnounceCurrentItem()
        {
            if (showingSummary)
            {
                AnnounceSummary();
                return;
            }

            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0)
            {
                CaravanAnnouncementHelper.AnnounceNoItems();
                return;
            }

            if (selectedIndex < 0 || selectedIndex >= transferables.Count)
            {
                selectedIndex = 0;
            }

            TransferableOneWay transferable = transferables[selectedIndex];
            string announcement = CaravanAnnouncementHelper.BuildItemAnnouncement(
                transferable, selectedIndex, transferables.Count);
            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Gets the currently selected pawn, if any.
        /// Works on Pawns tab or when a pawn-type transferable is selected on other tabs.
        /// </summary>
        private static Pawn GetSelectedPawn()
        {
            return CaravanUIHelper.GetSelectedPawn(GetCurrentTabTransferables(), selectedIndex);
        }

        /// <summary>
        /// Handles typeahead character input from the layout-aware dispatcher.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive) return;
            if (showingSummary) return;

            var labels = GetTransferableLabels();
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
                typeahead.SpeakNoMatches();
            }
        }

        /// <summary>
        /// Selects the next item in the current tab.
        /// </summary>
        public static void SelectNext()
        {
            if (showingSummary)
            {
                // In summary mode, navigate through summary items
                SelectNextSummaryItem();
                return;
            }

            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.NoItemsInTab".Translate());
                return;
            }

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int nextMatch = typeahead.GetNextMatch(selectedIndex);
                if (nextMatch >= 0)
                {
                    selectedIndex = nextMatch;
                    AnnounceWithSearch();
                }
                return;
            }

            selectedIndex = MenuHelper.SelectNext(selectedIndex, transferables.Count);
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Selects the previous item in the current tab.
        /// </summary>
        public static void SelectPrevious()
        {
            if (showingSummary)
            {
                // In summary mode, navigate through summary items
                SelectPreviousSummaryItem();
                return;
            }

            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.NoItemsInTab".Translate());
                return;
            }

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int prevMatch = typeahead.GetPreviousMatch(selectedIndex);
                if (prevMatch >= 0)
                {
                    selectedIndex = prevMatch;
                    AnnounceWithSearch();
                }
                return;
            }

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, transferables.Count);
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Switches to the next tab, preserving position in each tab.
        /// </summary>
        public static void NextTab()
        {
            if (showingSummary)
            {
                showingSummary = false;
            }

            // Save current position before switching
            tabPositions[currentTab] = selectedIndex;

            currentTab = (Tab)(((int)currentTab + 1) % TabCount);

            // Restore saved position for new tab (default to 0 if not visited yet)
            if (tabPositions.TryGetValue(currentTab, out int savedPos))
            {
                // Clamp to valid range in case list changed
                List<TransferableOneWay> transferables = GetCurrentTabTransferables();
                selectedIndex = System.Math.Min(savedPos, System.Math.Max(0, transferables.Count - 1));
            }
            else
            {
                selectedIndex = 0;
            }

            typeahead.ClearSearch();
            SyncGameTab();
            AnnounceCurrentTab();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Switches to the previous tab, preserving position in each tab.
        /// </summary>
        public static void PreviousTab()
        {
            if (showingSummary)
            {
                showingSummary = false;
            }

            // Save current position before switching
            tabPositions[currentTab] = selectedIndex;

            currentTab = (Tab)(((int)currentTab + TabCount - 1) % TabCount);

            // Restore saved position for new tab (default to 0 if not visited yet)
            if (tabPositions.TryGetValue(currentTab, out int savedPos))
            {
                // Clamp to valid range in case list changed
                List<TransferableOneWay> transferables = GetCurrentTabTransferables();
                selectedIndex = System.Math.Min(savedPos, System.Math.Max(0, transferables.Count - 1));
            }
            else
            {
                selectedIndex = 0;
            }

            typeahead.ClearSearch();
            SyncGameTab();
            AnnounceCurrentTab();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Syncs the game's visual tab with our internal tab state.
        /// The game's Dialog_FormCaravan.tab field is private, so we use reflection.
        /// Game tab values: Pawns=0, Items=1, TravelSupplies=2
        /// </summary>
        private static void SyncGameTab()
        {
            if (currentDialog == null)
                return;

            try
            {
                // Map our Tab enum to game's tab values
                int gameTabValue;
                switch (currentTab)
                {
                    case Tab.Pawns:
                        gameTabValue = 0;
                        break;
                    case Tab.Items:
                        gameTabValue = 1;
                        break;
                    case Tab.TravelSupplies:
                        gameTabValue = 2;
                        break;
                    default:
                        gameTabValue = 0;
                        break;
                }

                FieldInfo tabField = AccessTools.Field(typeof(Dialog_FormCaravan), "tab");
                if (tabField != null)
                {
                    tabField.SetValue(currentDialog, gameTabValue);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to sync game tab: {ex.Message}");
            }
        }

        /// <summary>
        /// Toggles between the current tab and the Summary view.
        /// </summary>
        private static void ToggleSummaryView()
        {
            if (showingSummary)
            {
                showingSummary = false;
                currentTab = savedTab;
                selectedIndex = savedIndex;
                typeahead.ClearSearch();
                SoundDefOf.Click.PlayOneShotOnCamera();

                string tabName = GetTabName(currentTab);
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.ReturnedToTab".Translate(tabName));
                AnnounceCurrentItem();
            }
            else
            {
                savedTab = currentTab;
                savedIndex = selectedIndex;
                showingSummary = true;
                typeahead.ClearSearch();
                SoundDefOf.Click.PlayOneShotOnCamera();

                // Only announce full instructions the first time per session
                if (!summaryInstructionsShown)
                {
                    TolkHelper.Speak("RimWorldAccess.Caravan.Form.SummaryViewWithHint".Translate());
                    summaryInstructionsShown = true;
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Caravan.Form.SummaryView".Translate());
                }
                AnnounceSummary();
            }
        }

        /// <summary>
        /// Builds the list of summary items for navigation.
        /// Preserves summaryIndex position if within valid range.
        /// Shows exactly what CaravanUIUtility.DrawCaravanInfo displays:
        /// Mass, Speed, Food, Foraging, Visibility.
        /// </summary>
        private static void BuildSummaryItems()
        {
            summaryItems.Clear();
            // Don't reset summaryIndex here - preserve position across summary views

            if (currentDialog == null)
            {
                summaryItems.Add("RimWorldAccess.Caravan.Form.SummaryNoCaravanData".Translate());
                return;
            }

            try
            {
                // 1. Mass (public properties)
                float massUsage = currentDialog.MassUsage;
                float massCapacity = currentDialog.MassCapacity;
                bool isOverloaded = massUsage > massCapacity;
                summaryItems.Add(CaravanStatFormatter.FormatMass(massUsage, massCapacity));

                // 2. Speed (TilesPerDay is private)
                PropertyInfo tilesInfo = AccessTools.Property(typeof(Dialog_FormCaravan), "TilesPerDay");
                if (tilesInfo != null)
                {
                    float tilesPerDay = (float)tilesInfo.GetValue(currentDialog);
                    summaryItems.Add(CaravanStatFormatter.FormatSpeed(tilesPerDay, isOverloaded));
                }

                // 3. Food (DaysWorthOfFood is private)
                PropertyInfo foodInfo = AccessTools.Property(typeof(Dialog_FormCaravan), "DaysWorthOfFood");
                if (foodInfo != null)
                {
                    var foodObj = foodInfo.GetValue(currentDialog);
                    var food = (ValueTuple<float, float>)foodObj;
                    summaryItems.Add(CaravanStatFormatter.FormatFood(food.Item1, food.Item2));
                }

                // 4. Foraging (ForagedFoodPerDay is private)
                PropertyInfo forageInfo = AccessTools.Property(typeof(Dialog_FormCaravan), "ForagedFoodPerDay");
                if (forageInfo != null)
                {
                    var forageObj = forageInfo.GetValue(currentDialog);
                    var forage = (ValueTuple<ThingDef, float>)forageObj;
                    summaryItems.Add(CaravanStatFormatter.FormatForaging(forage.Item1, forage.Item2));
                }

                // 5. Visibility (private)
                PropertyInfo visInfo = AccessTools.Property(typeof(Dialog_FormCaravan), "Visibility");
                if (visInfo != null)
                {
                    float visibility = (float)visInfo.GetValue(currentDialog);
                    summaryItems.Add(CaravanStatFormatter.FormatVisibility(visibility));
                }

                // Destination info
                FieldInfo destTileField = AccessTools.Field(typeof(Dialog_FormCaravan), "destinationTile");
                if (destTileField != null)
                {
                    PlanetTile destTile = (PlanetTile)destTileField.GetValue(currentDialog);
                    if (destTile.Valid && Find.WorldGrid != null)
                    {
                        string tileName = WorldInfoHelper.GetTileSummary(destTile);
                        string destItem = "RimWorldAccess.Caravan.Form.SummaryDestination".Translate(tileName);

                        PropertyInfo ticksToArriveProp = AccessTools.Property(typeof(Dialog_FormCaravan), "TicksToArrive");
                        if (ticksToArriveProp != null)
                        {
                            try
                            {
                                int ticksToArrive = (int)ticksToArriveProp.GetValue(currentDialog);
                                if (ticksToArrive > 0)
                                {
                                    float daysToArrive = ticksToArrive / 60000f;
                                    destItem = "RimWorldAccess.Caravan.Form.SummaryDestinationWithEta".Translate(tileName, daysToArrive.ToString("F1"));
                                }
                            }
                            catch { }
                        }
                        summaryItems.Add(destItem);
                    }
                    else
                    {
                        summaryItems.Add("RimWorldAccess.Caravan.Form.SummaryDestinationNotSet".Translate());
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to get caravan stats: {ex.Message}");
                summaryItems.Add("RimWorldAccess.Caravan.Form.SummaryStatsUnavailable".Translate());
            }
        }

        /// <summary>
        /// Announces the caravan stats summary.
        /// Builds the list and announces the first item.
        /// </summary>
        private static void AnnounceSummary()
        {
            BuildSummaryItems();
            AnnounceCurrentSummaryItem();
        }

        /// <summary>
        /// Announces the currently selected summary item.
        /// </summary>
        private static void AnnounceCurrentSummaryItem()
        {
            if (summaryItems.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.NoSummaryData".Translate());
                return;
            }

            if (summaryIndex < 0 || summaryIndex >= summaryItems.Count)
            {
                summaryIndex = 0;
            }

            string item = summaryItems[summaryIndex];
            string position = MenuHelper.FormatPosition(summaryIndex, summaryItems.Count);
            TolkHelper.Speak("RimWorldAccess.Caravan.Form.ItemPosition".Translate(item, position));
        }

        /// <summary>
        /// Moves to the next summary item.
        /// </summary>
        private static void SelectNextSummaryItem()
        {
            if (summaryItems.Count == 0)
                return;

            summaryIndex = MenuHelper.SelectNext(summaryIndex, summaryItems.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSummaryItem();
        }

        /// <summary>
        /// Moves to the previous summary item.
        /// </summary>
        private static void SelectPreviousSummaryItem()
        {
            if (summaryItems.Count == 0)
                return;

            summaryIndex = MenuHelper.SelectPrevious(summaryIndex, summaryItems.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSummaryItem();
        }

        /// <summary>
        /// Gets the explanation text for the currently selected summary stat.
        /// Uses reflection to access the cached explanation fields from the dialog.
        /// Detects stat type from the summary item text since order includes Foraging now.
        /// </summary>
        /// <returns>A tuple of (stat name, explanation text) or null if no explanation available.</returns>
        private static (string name, string explanation)? GetCurrentStatExplanation()
        {
            if (currentDialog == null || summaryItems.Count == 0)
                return null;

            if (summaryIndex < 0 || summaryIndex >= summaryItems.Count)
                return null;

            string currentItem = summaryItems[summaryIndex];

            try
            {
                // Detect stat type from the summary item text prefix
                // IMPORTANT: We must access the property first to trigger recalculation of the cached explanation.
                string fieldName = null;
                string propertyName = null;
                string statName = null;

                if (currentItem.StartsWith("Mass:"))
                {
                    fieldName = "cachedMassCapacityExplanation";
                    propertyName = "MassCapacity";
                    statName = "Mass Capacity";
                }
                else if (currentItem.StartsWith("Speed:"))
                {
                    fieldName = "cachedTilesPerDayExplanation";
                    propertyName = "TilesPerDay";
                    statName = "Speed";
                }
                else if (currentItem.StartsWith("Food:"))
                {
                    // Food doesn't have a breakdown explanation in the game
                    // The tooltip is just "DaysWorthOfFoodTooltip" which we already include
                    return null;
                }
                else if (currentItem.StartsWith("Foraging:"))
                {
                    fieldName = "cachedForagedFoodPerDayExplanation";
                    propertyName = "ForagedFoodPerDay";
                    statName = "Foraging";
                }
                else if (currentItem.StartsWith("Visibility:"))
                {
                    fieldName = "cachedVisibilityExplanation";
                    propertyName = "Visibility";
                    statName = "Visibility";
                }
                else
                {
                    // Destination or other items have no breakdown
                    return null;
                }

                if (fieldName == null)
                    return null;

                // Access the property first to trigger recalculation of the cached explanation
                if (propertyName != null)
                {
                    PropertyInfo prop = AccessTools.Property(typeof(Dialog_FormCaravan), propertyName);
                    if (prop != null)
                    {
                        // Just access the property getter - we don't need the value,
                        // this triggers the game to recalculate the cached explanation
                        prop.GetValue(currentDialog);
                    }
                }

                FieldInfo field = AccessTools.Field(typeof(Dialog_FormCaravan), fieldName);
                if (field == null)
                    return null;

                string explanation = field.GetValue(currentDialog) as string;
                if (string.IsNullOrEmpty(explanation))
                    return null;

                return (statName, explanation);
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to get stat explanation: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Toggles selection for a pawn (Space key in Pawns tab).
        /// For grouped pawns (multiple animals), opens quantity menu instead.
        /// </summary>
        private static void TogglePawnSelection()
        {
            if (currentTab != Tab.Pawns)
                return;

            List<TransferableOneWay> transferables = GetCurrentTabTransferables();
            if (transferables.Count == 0 || selectedIndex < 0 || selectedIndex >= transferables.Count)
                return;

            TransferableOneWay transferable = transferables[selectedIndex];

            // For grouped pawns (multiple animals), use quantity menu
            if (transferable.MaxCount > 1)
            {
                OpenQuantityMenu(transferable);
                return;
            }

            // For single pawns, use toggle
            CaravanUIHelper.TogglePawnSelection(transferable, NotifyTransferablesChanged);
        }

        /// <summary>
        /// Opens the quantity menu for a transferable with standard callbacks.
        /// </summary>
        private static void OpenQuantityMenu(TransferableOneWay transferable)
        {
            QuantityMenuState.Open(transferable, (newQty) =>
            {
                transferable.AdjustTo(newQty);
                NotifyTransferablesChanged();
                AnnounceCurrentItem();
            });
        }

        /// <summary>
        /// Notifies the dialog that transferables have changed.
        /// </summary>
        private static void NotifyTransferablesChanged()
        {
            if (currentDialog == null)
                return;

            try
            {
                MethodInfo method = AccessTools.Method(typeof(Dialog_FormCaravan), "Notify_TransferablesChanged");
                if (method != null)
                {
                    method.Invoke(currentDialog, null);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to call Notify_TransferablesChanged: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the currently selected transferable for quantity adjustment.
        /// Returns null if no valid selection (used by TransferableQuantityHelper).
        /// </summary>
        private static TransferableOneWay GetCurrentTransferableForQuantity()
        {
            List<TransferableOneWay> transferables = GetCurrentTabTransferables();
            if (transferables.Count == 0 || selectedIndex < 0 || selectedIndex >= transferables.Count)
                return null;

            return transferables[selectedIndex];
        }

        /// <summary>
        /// Disables auto-select travel supplies to prevent it from resetting manual selections.
        /// </summary>
        private static void DisableAutoSelectTravelSupplies()
        {
            if (currentDialog == null)
                return;

            try
            {
                FieldInfo field = AccessTools.Field(typeof(Dialog_FormCaravan), "autoSelectTravelSupplies");
                if (field != null)
                {
                    field.SetValue(currentDialog, false);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to disable auto-select travel supplies: {ex.Message}");
            }
        }

        /// <summary>
        /// Opens the route planner to choose a destination for the caravan.
        /// </summary>
        public static void ChooseRoute()
        {
            if (currentDialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.NoDialog".Translate(), SpeechPriority.High);
                return;
            }

            try
            {
                isChoosingDestination = true;

                if (Find.WindowStack != null)
                {
                    Find.WindowStack.TryRemove(currentDialog, doCloseSound: false);
                }

                CameraJumper.TryShowWorld();

                if (!WorldNavigationState.IsActive)
                {
                    WorldNavigationState.Open();
                }

                TolkHelper.Speak("RimWorldAccess.Caravan.Form.ChoosingDestination".Translate());
            }
            catch (Exception ex)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.FailedRoutePlanner".Translate(ex.Message), SpeechPriority.High);
                Log.Error($"RimWorld Access: Failed to start route planner: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the destination for the caravan and returns to the formation dialog.
        /// </summary>
        public static void SetDestination(PlanetTile destinationTile)
        {
            if (currentDialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.NoDialog".Translate(), SpeechPriority.High);
                isChoosingDestination = false;
                return;
            }

            if (!destinationTile.Valid)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.InvalidDestination".Translate(), SpeechPriority.High);
                isChoosingDestination = false;
                return;
            }

            try
            {
                MethodInfo notifyChoseRouteMethod = AccessTools.Method(typeof(Dialog_FormCaravan), "Notify_ChoseRoute");
                if (notifyChoseRouteMethod == null)
                {
                    TolkHelper.Speak("RimWorldAccess.Caravan.Form.FailedAccessNotifyChoseRoute".Translate(), SpeechPriority.High);
                    isChoosingDestination = false;
                    return;
                }

                CameraJumper.TryHideWorld();

                if (Find.WindowStack != null)
                {
                    Find.WindowStack.Add(currentDialog);
                }

                isChoosingDestination = false;

                notifyChoseRouteMethod.Invoke(currentDialog, new object[] { destinationTile });

                string tileInfo = WorldInfoHelper.GetTileSummary(destinationTile);
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.DestinationSet".Translate(tileInfo));
            }
            catch (Exception ex)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.FailedSetDestination".Translate(ex.Message), SpeechPriority.High);
                Log.Error($"RimWorld Access: Failed to set caravan destination: {ex}");
                isChoosingDestination = false;
            }
        }

        /// <summary>
        /// Cancels destination selection and returns to the formation dialog.
        /// </summary>
        public static void CancelDestinationSelection()
        {
            if (currentDialog == null)
            {
                isChoosingDestination = false;
                return;
            }

            try
            {
                CameraJumper.TryHideWorld();

                if (Find.WindowStack != null)
                {
                    Find.WindowStack.Add(currentDialog);
                }

                isChoosingDestination = false;

                TolkHelper.Speak("RimWorldAccess.Caravan.Form.DestinationCancelled".Translate());
            }
            catch (Exception ex)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.FailedCancelDestination".Translate(ex.Message), SpeechPriority.High);
                Log.Error($"RimWorld Access: Failed to cancel destination selection: {ex.Message}");
                isChoosingDestination = false;
            }
        }

        /// <summary>
        /// Attempts to send the caravan.
        /// </summary>
        public static void Send()
        {
            if (currentDialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.NoDialog".Translate(), SpeechPriority.High);
                return;
            }

            // Store reference locally in case it gets nulled during the operation
            Dialog_FormCaravan dialog = currentDialog;

            try
            {
                bool wasActive = isActive;
                isActive = false;
                sendAttempted = true;

                // IMPORTANT: Stop the route planner BEFORE calling OnAcceptKeyPressed
                // The game starts WorldRoutePlanner in PostOpen for ALL caravan dialogs (including reform).
                // For reform caravans, TryReformCaravan() removes the map, which switches to world view
                // BEFORE PostClose fires. This leaves the route planner active when entering world view.
                // By stopping it here, we ensure it's closed before the map switch happens.
                try
                {
                    var routePlanner = Find.WorldRoutePlanner;
                    if (routePlanner != null)
                    {
                        bool plannerActive = false;
                        try { plannerActive = routePlanner.Active; } catch { }
                        if (plannerActive)
                        {
                            routePlanner.Stop();
                        }
                    }
                }
                catch (Exception routeEx)
                {
                    Log.Warning($"RimWorld Access: Failed to stop route planner: {routeEx.Message}");
                    // Continue anyway - stopping route planner is not critical
                }

                // IMPORTANT: Store current map's tile BEFORE sending, because reform caravans
                // will remove the temporary map before WorldNavigationState.Open() is called.
                // This ensures the world cursor starts at the reform location, not the colony.
                Map currentMap = Find.CurrentMap;
                if (currentMap != null && currentMap.Tile.Valid)
                {
                    WorldNavigationState.PendingStartTile = currentMap.Tile;
                }

                // Use the local reference, not the static field which might get nulled
                dialog.OnAcceptKeyPressed();

                // Check if dialog is still open (validation failed)
                // Only restore isActive so user can keep interacting - do NOT reset sendAttempted
                // because the dialog close might be asynchronous (e.g., confirmation dialogs)
                // PostClose_Postfix will handle the sendAttempted flag correctly
                if (Find.WindowStack != null)
                {
                    bool stillOpen = false;
                    try { stillOpen = Find.WindowStack.IsOpen(dialog); } catch { }
                    if (stillOpen)
                    {
                        // Dialog still open means send failed validation - restore isActive
                        // but keep sendAttempted true so PostClose knows send was attempted
                        isActive = wasActive;
                    }
                }
            }
            catch (Exception ex)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.FailedSendCaravan".Translate(ex.Message), SpeechPriority.High);
                Log.Error($"RimWorld Access: Failed to send caravan: {ex.Message}\n{ex.StackTrace}");
                isActive = true;
                // Only reset sendAttempted on actual exception - not when dialog stays open
                sendAttempted = false;
            }
        }

        /// <summary>
        /// Resets all selections.
        /// </summary>
        public static void Reset()
        {
            if (currentDialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.NoDialog".Translate(), SpeechPriority.High);
                return;
            }

            try
            {
                MethodInfo method = AccessTools.Method(typeof(Dialog_FormCaravan), "CalculateAndRecacheTransferables");
                if (method != null)
                {
                    method.Invoke(currentDialog, null);
                    selectedIndex = 0;
                    TolkHelper.Speak("RimWorldAccess.Caravan.Form.SelectionsReset".Translate());
                    AnnounceCurrentItem();
                }
            }
            catch (Exception ex)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.FailedReset".Translate(ex.Message), SpeechPriority.High);
                Log.Error($"RimWorld Access: Failed to reset caravan formation: {ex.Message}");
            }
        }

        /// <summary>
        /// Toggles automatic travel supplies selection.
        /// When enabled: saves current amounts, auto-selects optimal supplies, locks supplies tab.
        /// When disabled: restores previous amounts, unlocks supplies tab.
        /// </summary>
        public static void ToggleAutoProvision()
        {
            if (currentDialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.NoDialog".Translate(), SpeechPriority.High);
                return;
            }

            try
            {
                if (autoProvisionEnabled)
                {
                    // Turning OFF - restore saved amounts
                    autoProvisionEnabled = false;

                    // Set game's flag to false
                    FieldInfo autoField = AccessTools.Field(typeof(Dialog_FormCaravan), "autoSelectTravelSupplies");
                    if (autoField != null)
                    {
                        autoField.SetValue(currentDialog, false);
                    }

                    // Restore saved amounts
                    foreach (var kvp in savedSupplyAmounts)
                    {
                        kvp.Key.AdjustTo(kvp.Value);
                    }
                    savedSupplyAmounts.Clear();

                    NotifyTransferablesChanged();
                    TolkHelper.Speak("RimWorldAccess.Caravan.Form.AutoProvisionOff".Translate());
                }
                else
                {
                    // Turning ON - save current amounts, then auto-select
                    savedSupplyAmounts.Clear();

                    // Save current travel supplies amounts
                    List<TransferableOneWay> allTransferables = GetTransferables();
                    foreach (var t in allTransferables)
                    {
                        if (t.ThingDef.category != ThingCategory.Pawn &&
                            ((!t.ThingDef.thingCategories.NullOrEmpty() && t.ThingDef.thingCategories.Contains(ThingCategoryDefOf.Medicine)) ||
                             (t.ThingDef.IsIngestible && !t.ThingDef.IsDrug && !t.ThingDef.IsCorpse && (t.ThingDef.plant == null || !t.ThingDef.plant.IsTree)) ||
                             (t.AnyThing.GetInnerIfMinified().def.IsBed && t.AnyThing.GetInnerIfMinified().def.building != null && t.AnyThing.GetInnerIfMinified().def.building.bed_caravansCanUse)))
                        {
                            savedSupplyAmounts[t] = t.CountToTransfer;
                        }
                    }

                    // Set game's flag to true
                    FieldInfo autoField = AccessTools.Field(typeof(Dialog_FormCaravan), "autoSelectTravelSupplies");
                    if (autoField != null)
                    {
                        autoField.SetValue(currentDialog, true);
                    }

                    // Call auto-select method
                    MethodInfo method = AccessTools.Method(typeof(Dialog_FormCaravan), "SelectApproximateBestTravelSupplies");
                    if (method != null)
                    {
                        method.Invoke(currentDialog, null);
                    }

                    autoProvisionEnabled = true;
                    NotifyTransferablesChanged();
                    TolkHelper.Speak("RimWorldAccess.Caravan.Form.AutoProvisionOn".Translate());
                }
            }
            catch (Exception ex)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.FailedToggleAutoProvision".Translate(ex.Message), SpeechPriority.High);
                Log.Error($"RimWorld Access: Failed to toggle auto-provision: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets a human-readable name for a tab.
        /// </summary>
        private static string GetTabName(Tab tab)
        {
            switch (tab)
            {
                case Tab.Pawns: return "PawnsTab".Translate();
                case Tab.Items: return "ItemsTab".Translate();
                case Tab.TravelSupplies: return "TravelSupplies".Translate();
                default: return tab.ToString();
            }
        }

        /// <summary>
        /// Gets the list of transferable labels for typeahead search.
        /// </summary>
        private static List<string> GetTransferableLabels()
        {
            return CaravanUIHelper.GetTransferableLabels(GetCurrentTabTransferables());
        }

        /// <summary>
        /// Announces the current item with search information.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0 || selectedIndex < 0 || selectedIndex >= transferables.Count)
            {
                CaravanAnnouncementHelper.AnnounceNoItems();
                return;
            }

            TransferableOneWay transferable = transferables[selectedIndex];
            string announcement = CaravanAnnouncementHelper.BuildSearchAnnouncement(
                transferable,
                typeahead.SearchBuffer,
                typeahead.CurrentMatchPosition,
                typeahead.MatchCount);
            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Handles keyboard input for caravan formation.
        /// Returns true if the input was handled.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive)
                return false;

            // If choosing destination, let world navigation handle the input
            if (isChoosingDestination)
                return false;

            // Handle Escape - clear search, or close dialog (especially for reform dialogs)
            if (key == KeyCode.Escape)
            {
                // If a confirmation dialog (e.g., "food will rot soon") is on top,
                // close both the confirmation and the caravan formation (cancel everything)
                // PostClose_Postfix will announce "Caravan formation cancelled"
                if (Find.WindowStack.IsOpen<Dialog_MessageBox>())
                {
                    Find.WindowStack.TryRemove(typeof(Dialog_MessageBox), doCloseSound: false);
                    Find.WindowStack.TryRemove(currentDialog, doCloseSound: false);
                    return true;
                }

                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentItem();
                    return true;
                }

                // For reform dialogs, game sets closeOnCancel=false so Escape doesn't work by default.
                // We need to handle closing ourselves.
                if (currentDialog != null && !currentDialog.closeOnCancel)
                {
                    // Store the map to return to before closing anything (map field is private)
                    Map mapToReturnTo = (Map)AccessTools.Field(typeof(Dialog_FormCaravan), "map").GetValue(currentDialog);

                    // Stop the route planner first (game starts it automatically for reform dialogs)
                    // Note: Route planner sets WorldRenderMode.Planet but Stop() doesn't revert it
                    if (Find.WorldRoutePlanner != null && Find.WorldRoutePlanner.Active)
                    {
                        Find.WorldRoutePlanner.Stop();
                    }

                    // Close the dialog
                    Find.WindowStack.TryRemove(currentDialog, doCloseSound: false);

                    // Switch back to map view (route planner switched to world view when it started)
                    if (mapToReturnTo != null)
                    {
                        CameraJumper.TryHideWorld();
                        Current.Game.CurrentMap = mapToReturnTo;
                    }

                    TolkHelper.Speak("RimWorldAccess.Caravan.Form.ReformationCancelled".Translate());
                    return true;
                }

                // For regular dialogs, let the game handle escape - PostClose_Postfix will announce
                return false;
            }

            // Handle Backspace for search
            if (key == KeyCode.Backspace && typeahead.HasActiveSearch)
            {
                var labels = GetTransferableLabels();
                if (typeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0) selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
                Event.current.Use();
                return true;
            }

            // Handle Tab - toggle Summary view
            if (key == KeyCode.Tab)
            {
                ToggleSummaryView();
                Event.current.Use();
                return true;
            }

            // Handle Space - toggle pawn or adjust quantity (same as Enter)
            if (key == KeyCode.Space && !shift && !ctrl && !alt && !showingSummary)
            {
                typeahead.ClearSearch(); // Clear search when activating an item

                List<TransferableOneWay> transferables = GetCurrentTabTransferables();
                if (transferables.Count == 0 || selectedIndex < 0 || selectedIndex >= transferables.Count)
                {
                    Event.current.Use();
                    return true;
                }

                TransferableOneWay transferable = transferables[selectedIndex];

                if (currentTab == Tab.Pawns)
                {
                    TogglePawnSelection();
                }
                else if (currentTab == Tab.Items)
                {
                    OpenQuantityMenu(transferable);
                }
                else if (currentTab == Tab.TravelSupplies)
                {
                    if (autoProvisionEnabled)
                    {
                        TolkHelper.Speak("RimWorldAccess.Caravan.Form.SuppliesTabLocked".Translate());
                    }
                    else
                    {
                        OpenQuantityMenu(transferable);
                    }
                }

                Event.current.Use();
                return true;
            }

            // Handle inline quantity adjustment (Items, TravelSupplies, and grouped Pawns)
            if (!showingSummary)
            {
                // Check if quantity shortcuts should be enabled for this tab/item
                bool allowQuantityShortcuts = currentTab != Tab.Pawns;

                // For Pawns tab, allow quantity shortcuts only for grouped animals (MaxCount > 1)
                if (!allowQuantityShortcuts)
                {
                    var transferable = GetCurrentTransferableForQuantity();
                    allowQuantityShortcuts = transferable != null && transferable.MaxCount > 1;
                }

                if (allowQuantityShortcuts)
                {
                    if (currentTab == Tab.TravelSupplies && autoProvisionEnabled)
                    {
                        // Don't handle quantity keys when supplies are locked
                        // (but don't consume the event either - let it fall through)
                    }
                    else if (TransferableQuantityHelper.HandleQuantityInput(key, shift, ctrl, alt,
                        GetCurrentTransferableForQuantity, NotifyTransferablesChanged))
                    {
                        Event.current.Use();
                        return true;
                    }
                }
            }

            // Handle typeahead characters (not in summary mode)
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            if ((isLetter || isNumber) && !alt && !showingSummary)
            {
                Event.current.Use();
                return true;
            }

            // Handle Alt+H/M/N pawn info shortcuts
            if (CaravanInputHelper.HandlePawnInfoShortcuts(key, GetSelectedPawn(), alt, shift, ctrl))
                return true;

            switch (key)
            {
                case KeyCode.UpArrow:
                    if (!shift && !ctrl && !alt)
                    {
                        SelectPrevious();
                        return true;
                    }
                    break;

                case KeyCode.DownArrow:
                    if (!shift && !ctrl && !alt)
                    {
                        SelectNext();
                        return true;
                    }
                    break;

                case KeyCode.LeftArrow:
                    if (!shift && !ctrl && !alt && !showingSummary)
                    {
                        PreviousTab();
                        return true;
                    }
                    break;

                case KeyCode.RightArrow:
                    if (!shift && !ctrl && !alt && !showingSummary)
                    {
                        NextTab();
                        return true;
                    }
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    // Shift+Enter: Add maximum that fits within remaining mass capacity
                    if (shift && !ctrl && !alt && !showingSummary)
                    {
                        typeahead.ClearSearch();

                        List<TransferableOneWay> transferables = GetCurrentTabTransferables();
                        if (transferables.Count == 0 || selectedIndex < 0 || selectedIndex >= transferables.Count)
                        {
                            return true;
                        }

                        TransferableOneWay transferable = transferables[selectedIndex];

                        // Pawns tab: Shift+Enter selects all pawns of this type
                        if (currentTab == Tab.Pawns)
                        {
                            CaravanQuantityHelper.SelectAllPawns(
                                transferable,
                                NotifyTransferablesChanged,
                                AnnounceCurrentItem);
                            return true;
                        }

                        // Items/TravelSupplies: Shift+Enter adds max that fits in remaining capacity
                        if (currentTab == Tab.Items || currentTab == Tab.TravelSupplies)
                        {
                            if (currentTab == Tab.TravelSupplies && autoProvisionEnabled)
                            {
                                TolkHelper.Speak("RimWorldAccess.Caravan.Form.SuppliesTabLocked".Translate());
                                return true;
                            }

                            float remainingCapacity = currentDialog.MassCapacity - currentDialog.MassUsage;
                            var result = CaravanQuantityHelper.CalculateMaxToAdd(transferable, remainingCapacity);
                            CaravanQuantityHelper.ApplyMaxAdd(
                                transferable,
                                result,
                                NotifyTransferablesChanged);
                            return true;
                        }

                        return true;
                    }
                    // Regular Enter: Toggle/open quantity menu
                    if (!shift && !ctrl && !alt && !showingSummary)
                    {
                        typeahead.ClearSearch(); // Clear search when activating an item

                        List<TransferableOneWay> transferables = GetCurrentTabTransferables();
                        if (transferables.Count == 0 || selectedIndex < 0 || selectedIndex >= transferables.Count)
                        {
                            return true;
                        }

                        TransferableOneWay transferable = transferables[selectedIndex];

                        // Pawns tab: Enter toggles pawn selection (same as Space)
                        if (currentTab == Tab.Pawns)
                        {
                            TogglePawnSelection();
                            return true;
                        }

                        // Items tab: Enter opens quantity menu
                        if (currentTab == Tab.Items)
                        {
                            OpenQuantityMenu(transferable);
                            return true;
                        }

                        // TravelSupplies tab: Enter opens quantity menu only if auto-provision is off
                        if (currentTab == Tab.TravelSupplies)
                        {
                            if (autoProvisionEnabled)
                            {
                                TolkHelper.Speak("RimWorldAccess.Caravan.Form.SuppliesTabLocked".Translate());
                                return true;
                            }
                            OpenQuantityMenu(transferable);
                            return true;
                        }

                        return true;
                    }
                    break;

                case KeyCode.Delete:
                    // Delete: Remove all of this item (set to 0)
                    if (!shift && !ctrl && !alt && !showingSummary)
                    {
                        typeahead.ClearSearch();

                        List<TransferableOneWay> transferables = GetCurrentTabTransferables();
                        if (transferables.Count == 0 || selectedIndex < 0 || selectedIndex >= transferables.Count)
                        {
                            return true;
                        }

                        TransferableOneWay transferable = transferables[selectedIndex];
                        bool isPawnTab = currentTab == Tab.Pawns;
                        bool isSuppliesLocked = currentTab == Tab.TravelSupplies && autoProvisionEnabled;

                        CaravanInputHelper.HandleDeleteKey(
                            transferable,
                            isPawnTab,
                            isSuppliesLocked,
                            NotifyTransferablesChanged);
                        return true;
                    }
                    break;

                case KeyCode.I:
                    // Alt+I: Inspect current item or stat breakdown
                    if (alt && !shift && !ctrl)
                    {
                        if (showingSummary)
                        {
                            // In summary mode: show stat breakdown
                            var statInfo = GetCurrentStatExplanation();
                            if (statInfo.HasValue)
                            {
                                StatBreakdownState.Open(statInfo.Value.name, statInfo.Value.explanation);
                            }
                            else
                            {
                                TolkHelper.Speak("RimWorldAccess.Caravan.Form.NoBreakdownForItem".Translate());
                            }
                        }
                        else
                        {
                            // In tab mode: inspect current item
                            List<TransferableOneWay> transferables = GetCurrentTabTransferables();
                            if (transferables.Count > 0 && selectedIndex >= 0 && selectedIndex < transferables.Count)
                            {
                                Thing thing = transferables[selectedIndex].AnyThing;
                                if (thing != null)
                                {
                                    Dialog_InfoCard infoCard = new Dialog_InfoCard(thing);
                                    Find.WindowStack.Add(infoCard);
                                }
                            }
                        }
                        return true;
                    }
                    break;

                case KeyCode.S:
                    if (!shift && !ctrl && alt)
                    {
                        Send();
                        return true;
                    }
                    break;

                case KeyCode.R:
                    if (!shift && !ctrl && alt)
                    {
                        Reset();
                        return true;
                    }
                    break;

                case KeyCode.A:
                    if (!shift && !ctrl && alt)
                    {
                        ToggleAutoProvision();
                        return true;
                    }
                    break;

                case KeyCode.Home:
                    if (showingSummary)
                    {
                        if (summaryItems.Count > 0)
                        {
                            summaryIndex = 0;
                            AnnounceCurrentSummaryItem();
                        }
                    }
                    else
                    {
                        List<TransferableOneWay> transferables = GetCurrentTabTransferables();
                        if (transferables.Count > 0)
                        {
                            selectedIndex = 0;
                            AnnounceCurrentItem();
                        }
                    }
                    return true;

                case KeyCode.End:
                    if (showingSummary)
                    {
                        if (summaryItems.Count > 0)
                        {
                            summaryIndex = summaryItems.Count - 1;
                            AnnounceCurrentSummaryItem();
                        }
                    }
                    else
                    {
                        List<TransferableOneWay> transferables = GetCurrentTabTransferables();
                        if (transferables.Count > 0)
                        {
                            selectedIndex = transferables.Count - 1;
                            AnnounceCurrentItem();
                        }
                    }
                    return true;
            }

            // Block ALL unhandled keys to prevent game's native handlers from processing them
            // This makes the overlay screen modal - it captures all keyboard input while active
            return true;
        }
    }
}
