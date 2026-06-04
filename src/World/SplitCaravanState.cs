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
    /// State management for keyboard navigation in Dialog_SplitCaravan.
    /// Provides three-tab interface for selecting pawns, items, and travel supplies.
    /// Summary view shows stats for both original and new caravan in two rows.
    /// </summary>
    public static class SplitCaravanState
    {
        private enum Tab
        {
            Pawns,
            Items,
            FoodAndMedicine
        }

        private enum SummaryRow
        {
            NewCaravan,      // First (row 1) - the caravan being created/filled
            OriginalCaravan  // Second (row 2) - the source caravan
        }

        private enum SummaryStat
        {
            Mass,
            Speed,
            Food,
            Foraging,
            Visibility
        }

        private const int TabCount = 3;
        private const int SummaryStatCount = 5;

        private static bool isActive = false;
        private static Dialog_SplitCaravan currentDialog = null;
        private static Caravan sourceCaravan = null;
        private static Tab currentTab = Tab.Pawns;
        private static int selectedIndex = 0;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        // Summary toggle state (Tab key to quickly view stats)
        private static bool showingSummary = false;
        private static Tab savedTab = Tab.Pawns;
        private static int savedIndex = 0;
        private static SummaryRow currentSummaryRow = SummaryRow.OriginalCaravan;
        private static SummaryStat currentSummaryStat = SummaryStat.Mass;

        // Position memory per tab - preserves selected index when switching tabs
        private static Dictionary<Tab, int> tabPositions = new Dictionary<Tab, int>();

        // Flag to track if split was attempted (to avoid announcing "cancelled" on successful split)
        private static bool splitAttempted = false;

        // Flag to track if summary instructions have been shown this session (not reset on Close)
        private static bool summaryInstructionsShown = false;

        /// <summary>
        /// Gets whether split caravan keyboard navigation is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets whether a split was attempted (used by PostClose to decide announcement).
        /// </summary>
        public static bool SplitAttempted => splitAttempted;

        /// <summary>
        /// Gets whether typeahead search is currently active.
        /// Used by Window.OnCancelKeyPressed patch to block dialog close.
        /// </summary>
        public static bool HasActiveTypeahead => typeahead.HasActiveSearch;

        /// <summary>
        /// Opens keyboard navigation for the specified Dialog_SplitCaravan.
        /// </summary>
        public static void Open(Dialog_SplitCaravan dialog)
        {
            if (dialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Split.NoDialog".Loc(), SpeechPriority.High);
                return;
            }

            isActive = true;
            currentDialog = dialog;
            currentTab = Tab.Pawns;
            selectedIndex = 0;
            showingSummary = false;
            currentSummaryRow = SummaryRow.NewCaravan;
            typeahead.ClearSearch();

            // Get the source caravan from the dialog
            try
            {
                FieldInfo caravanField = AccessTools.Field(typeof(Dialog_SplitCaravan), "caravan");
                if (caravanField != null)
                {
                    sourceCaravan = caravanField.GetValue(dialog) as Caravan;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to get caravan from dialog: {ex.Message}");
            }

            TolkHelper.Speak("RimWorldAccess.Caravan.Split.OpenInstructions".Loc());
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
            sourceCaravan = null;
            currentTab = Tab.Pawns;
            selectedIndex = 0;
            showingSummary = false;
            currentSummaryRow = SummaryRow.NewCaravan;
            currentSummaryStat = SummaryStat.Mass;
            tabPositions.Clear();
            typeahead.ClearSearch();
            splitAttempted = false;
        }

        /// <summary>
        /// Gets the transferables list from the current dialog using reflection.
        /// </summary>
        private static List<TransferableOneWay> GetTransferables()
        {
            if (currentDialog == null)
                return new List<TransferableOneWay>();

            try
            {
                FieldInfo field = AccessTools.Field(typeof(Dialog_SplitCaravan), "transferables");
                if (field != null)
                {
                    var result = field.GetValue(currentDialog);
                    if (result is List<TransferableOneWay> transferables)
                    {
                        return transferables;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"RimWorld Access: Failed to get transferables from Dialog_SplitCaravan: {ex.Message}");
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
                case Tab.FoodAndMedicine: return CaravanUIHelper.TransferableCategory.FoodAndMedicine;
                case Tab.Items: return CaravanUIHelper.TransferableCategory.Items;
                default: return CaravanUIHelper.TransferableCategory.Pawns;
            }
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
        /// Announces the current tab.
        /// </summary>
        private static void AnnounceCurrentTab()
        {
            string tabName = GetTabName(currentTab);
            List<TransferableOneWay> tabTransferables = GetCurrentTabTransferables();
            TolkHelper.Speak("RimWorldAccess.Caravan.Split.TabHeader".Loc(tabName, tabTransferables.Count));
        }

        /// <summary>
        /// Announces the currently selected item.
        /// </summary>
        private static void AnnounceCurrentItem()
        {
            if (showingSummary)
            {
                AnnounceSummaryStat();
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
        /// In summary mode, navigates to next stat (down in the stat list).
        /// </summary>
        public static void SelectNext()
        {
            if (showingSummary)
            {
                // Navigate to next stat (down) - don't wrap
                int nextStat = (int)currentSummaryStat + 1;
                if (nextStat >= SummaryStatCount)
                {
                    // At bottom, announce current position
                    TolkHelper.Speak("RimWorldAccess.Caravan.Split.BottomEdge".Translate(GetCurrentStatValue(), (int)currentSummaryStat + 1, SummaryStatCount));
                    return;
                }
                currentSummaryStat = (SummaryStat)nextStat;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceSummaryStat();
                return;
            }

            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Split.NoItemsInTab".Loc());
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
        /// In summary mode, navigates to previous stat (up in the stat list).
        /// </summary>
        public static void SelectPrevious()
        {
            if (showingSummary)
            {
                // Navigate to previous stat (up) - don't wrap
                int prevStat = (int)currentSummaryStat - 1;
                if (prevStat < 0)
                {
                    // At top, announce current position
                    TolkHelper.Speak("RimWorldAccess.Caravan.Split.TopEdge".Translate(GetCurrentStatValue(), (int)currentSummaryStat + 1, SummaryStatCount));
                    return;
                }
                currentSummaryStat = (SummaryStat)prevStat;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceSummaryStat();
                return;
            }

            List<TransferableOneWay> transferables = GetCurrentTabTransferables();

            if (transferables.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Split.NoItemsInTab".Loc());
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
                // Exit summary mode first
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
        /// The game's Dialog_SplitCaravan.tab field is private, so we use reflection.
        /// Game tab values: Pawns=0, Items=1, FoodAndMedicine=2
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
                    case Tab.FoodAndMedicine:
                        gameTabValue = 2;
                        break;
                    default:
                        gameTabValue = 0;
                        break;
                }

                FieldInfo tabField = AccessTools.Field(typeof(Dialog_SplitCaravan), "tab");
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
                TolkHelper.Speak("RimWorldAccess.Caravan.Split.ReturnedToTab".Loc(tabName));
                AnnounceCurrentItem();
            }
            else
            {
                savedTab = currentTab;
                savedIndex = selectedIndex;
                showingSummary = true;
                // Don't reset currentSummaryRow - preserve position across summary views
                typeahead.ClearSearch();
                SoundDefOf.Click.PlayOneShotOnCamera();

                // Only announce full instructions the first time per session
                if (!summaryInstructionsShown)
                {
                    TolkHelper.Speak("RimWorldAccess.Caravan.Split.SummaryViewWithHint".Loc());
                    summaryInstructionsShown = true;
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Caravan.Split.SummaryView".Loc());
                }
                AnnounceSummaryWithCaravan();
            }
        }

        /// <summary>
        /// Announces just the current stat value and position (for Up/Down navigation).
        /// Does not include caravan name since we're staying on the same caravan.
        /// </summary>
        private static void AnnounceSummaryStat()
        {
            if (currentDialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Split.NoCaravanData".Loc());
                return;
            }

            string statValue = GetCurrentStatValue();
            int statPosition = (int)currentSummaryStat + 1;

            TolkHelper.Speak("RimWorldAccess.Caravan.Split.StatPosition".Loc(statValue, statPosition, SummaryStatCount));
        }

        /// <summary>
        /// Announces stat value with caravan name (for Left/Right navigation when switching caravans).
        /// </summary>
        private static void AnnounceSummaryWithCaravan()
        {
            if (currentDialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Split.NoCaravanData".Loc());
                return;
            }

            string caravanName = currentSummaryRow == SummaryRow.NewCaravan
                ? "RimWorldAccess.Caravan.Split.NewCaravanLabel".Translate()
                : "RimWorldAccess.Caravan.Split.OriginalCaravanLabel".Translate();
            string statValue = GetCurrentStatValue();
            int statPosition = (int)currentSummaryStat + 1;

            TolkHelper.Speak("RimWorldAccess.Caravan.Split.StatCaravanPosition".Loc(statValue, caravanName, statPosition, SummaryStatCount));
        }

        /// <summary>
        /// Gets the value string for the currently selected stat.
        /// </summary>
        private static string GetCurrentStatValue()
        {
            if (currentDialog == null)
                return "RimWorldAccess.Caravan.Split.UnknownStatValue".Translate();

            bool isSource = currentSummaryRow == SummaryRow.OriginalCaravan;

            try
            {
                switch (currentSummaryStat)
                {
                    case SummaryStat.Mass:
                        return GetMassStatValue(isSource);
                    case SummaryStat.Speed:
                        return GetSpeedStatValue(isSource);
                    case SummaryStat.Food:
                        return GetFoodStatValue(isSource);
                    case SummaryStat.Foraging:
                        return GetForagingStatValue(isSource);
                    case SummaryStat.Visibility:
                        return GetVisibilityStatValue(isSource);
                    default:
                        return "Unknown";
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to get stat value: {ex.Message}");
                return "Unavailable";
            }
        }

        private static string GetMassStatValue(bool isSource)
        {
            string massUsageProp = isSource ? "SourceMassUsage" : "DestMassUsage";
            string massCapacityProp = isSource ? "SourceMassCapacity" : "DestMassCapacity";

            PropertyInfo massUsageInfo = AccessTools.Property(typeof(Dialog_SplitCaravan), massUsageProp);
            PropertyInfo massCapacityInfo = AccessTools.Property(typeof(Dialog_SplitCaravan), massCapacityProp);

            if (massUsageInfo != null && massCapacityInfo != null)
            {
                float massUsage = (float)massUsageInfo.GetValue(currentDialog);
                float massCapacity = (float)massCapacityInfo.GetValue(currentDialog);
                return CaravanStatFormatter.FormatMass(massUsage, massCapacity);
            }
            return "Mass: Unknown";
        }

        /// <summary>
        /// Gets the remaining mass capacity for the destination (new) caravan.
        /// Used by Shift+Enter to add max items that fit.
        /// </summary>
        private static float GetDestRemainingCapacity()
        {
            if (currentDialog == null)
                return 0f;

            PropertyInfo massUsageInfo = AccessTools.Property(typeof(Dialog_SplitCaravan), "DestMassUsage");
            PropertyInfo massCapacityInfo = AccessTools.Property(typeof(Dialog_SplitCaravan), "DestMassCapacity");

            if (massUsageInfo != null && massCapacityInfo != null)
            {
                float massUsage = (float)massUsageInfo.GetValue(currentDialog);
                float massCapacity = (float)massCapacityInfo.GetValue(currentDialog);
                return massCapacity - massUsage;
            }
            return 0f;
        }

        private static string GetSpeedStatValue(bool isSource)
        {
            string tilesPerDayProp = isSource ? "SourceTilesPerDay" : "DestTilesPerDay";
            string massUsageProp = isSource ? "SourceMassUsage" : "DestMassUsage";
            string massCapacityProp = isSource ? "SourceMassCapacity" : "DestMassCapacity";

            PropertyInfo tilesInfo = AccessTools.Property(typeof(Dialog_SplitCaravan), tilesPerDayProp);
            PropertyInfo massUsageInfo = AccessTools.Property(typeof(Dialog_SplitCaravan), massUsageProp);
            PropertyInfo massCapacityInfo = AccessTools.Property(typeof(Dialog_SplitCaravan), massCapacityProp);

            if (tilesInfo != null)
            {
                float tilesPerDay = (float)tilesInfo.GetValue(currentDialog);

                // Check for overload - game shows "Immobile" when massUsage > massCapacity
                bool isOverloaded = false;
                if (massUsageInfo != null && massCapacityInfo != null)
                {
                    float massUsage = (float)massUsageInfo.GetValue(currentDialog);
                    float massCapacity = (float)massCapacityInfo.GetValue(currentDialog);
                    isOverloaded = massUsage > massCapacity;
                }

                return CaravanStatFormatter.FormatSpeed(tilesPerDay, isOverloaded);
            }
            return "Speed: Unknown";
        }

        private static string GetFoodStatValue(bool isSource)
        {
            string daysWorthProp = isSource ? "SourceDaysWorthOfFood" : "DestDaysWorthOfFood";
            PropertyInfo foodInfo = AccessTools.Property(typeof(Dialog_SplitCaravan), daysWorthProp);

            if (foodInfo != null)
            {
                var foodObj = foodInfo.GetValue(currentDialog);
                var food = (ValueTuple<float, float>)foodObj;
                return CaravanStatFormatter.FormatFood(food.Item1, food.Item2);
            }
            return "Food: Unknown";
        }

        private static string GetForagingStatValue(bool isSource)
        {
            string forageProp = isSource ? "SourceForagedFoodPerDay" : "DestForagedFoodPerDay";
            PropertyInfo forageInfo = AccessTools.Property(typeof(Dialog_SplitCaravan), forageProp);

            if (forageInfo != null)
            {
                var forageObj = forageInfo.GetValue(currentDialog);
                var forage = (ValueTuple<ThingDef, float>)forageObj;
                return CaravanStatFormatter.FormatForaging(forage.Item1, forage.Item2);
            }
            return "Foraging: Unknown";
        }

        private static string GetVisibilityStatValue(bool isSource)
        {
            string visibilityProp = isSource ? "SourceVisibility" : "DestVisibility";
            PropertyInfo visInfo = AccessTools.Property(typeof(Dialog_SplitCaravan), visibilityProp);

            if (visInfo != null)
            {
                float visibility = (float)visInfo.GetValue(currentDialog);
                return CaravanStatFormatter.FormatVisibility(visibility);
            }
            return "Visibility: Unknown";
        }

        /// <summary>
        /// Gets the explanation text for the currently selected summary stat.
        /// Uses reflection to access the cached explanation fields from the dialog.
        /// </summary>
        /// <returns>A tuple of (stat name, explanation text) or null if no explanation available.</returns>
        private static (string name, string explanation)? GetCurrentStatExplanation()
        {
            if (currentDialog == null)
                return null;

            try
            {
                bool isSource = currentSummaryRow == SummaryRow.OriginalCaravan;
                string prefix = isSource ? "cachedSource" : "cachedDest";
                string propPrefix = isSource ? "Source" : "Dest";
                string fieldName = null;
                string propertyName = null;
                string statName = null;

                // IMPORTANT: We must access the property first to trigger recalculation of the cached explanation.
                // The game only updates cachedXxxExplanation when the corresponding property getter is called.
                switch (currentSummaryStat)
                {
                    case SummaryStat.Mass:
                        fieldName = prefix + "MassCapacityExplanation";
                        propertyName = propPrefix + "MassCapacity";
                        statName = (isSource ? "Original" : "New") + " Caravan Mass Capacity";
                        break;
                    case SummaryStat.Speed:
                        fieldName = prefix + "TilesPerDayExplanation";
                        propertyName = propPrefix + "TilesPerDay";
                        statName = (isSource ? "Original" : "New") + " Caravan Speed";
                        break;
                    case SummaryStat.Food:
                        // Food doesn't have a breakdown explanation in the game
                        return null;
                    case SummaryStat.Foraging:
                        fieldName = prefix + "ForagedFoodPerDayExplanation";
                        propertyName = propPrefix + "ForagedFoodPerDay";
                        statName = (isSource ? "Original" : "New") + " Caravan Foraging";
                        break;
                    case SummaryStat.Visibility:
                        fieldName = prefix + "VisibilityExplanation";
                        propertyName = propPrefix + "Visibility";
                        statName = (isSource ? "Original" : "New") + " Caravan Visibility";
                        break;
                    default:
                        return null;
                }

                if (fieldName == null)
                    return null;

                // Access the property first to trigger recalculation of the cached explanation
                if (propertyName != null)
                {
                    PropertyInfo prop = AccessTools.Property(typeof(Dialog_SplitCaravan), propertyName);
                    if (prop != null)
                    {
                        // Just access the property getter - we don't need the value,
                        // this triggers the game to recalculate the cached explanation
                        prop.GetValue(currentDialog);
                    }
                }

                FieldInfo field = AccessTools.Field(typeof(Dialog_SplitCaravan), fieldName);
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
                MethodInfo method = AccessTools.Method(typeof(Dialog_SplitCaravan), "CountToTransferChanged");
                if (method != null)
                {
                    method.Invoke(currentDialog, null);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to call CountToTransferChanged: {ex.Message}");
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
        /// Attempts to split the caravan.
        /// </summary>
        public static void Split()
        {
            if (currentDialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Split.NoDialogAvailable".Loc(), SpeechPriority.High);
                return;
            }

            try
            {
                // Call TrySplitCaravan via reflection
                MethodInfo method = AccessTools.Method(typeof(Dialog_SplitCaravan), "TrySplitCaravan");
                if (method != null)
                {
                    splitAttempted = true;
                    bool success = (bool)method.Invoke(currentDialog, null);
                    if (success)
                    {
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                        currentDialog.Close(doCloseSound: false);
                        TolkHelper.Speak("RimWorldAccess.Caravan.Split.SplitSuccessfully".Loc());
                    }
                    else
                    {
                        // Split failed validation - reset flag
                        splitAttempted = false;
                    }
                }
            }
            catch (Exception ex)
            {
                splitAttempted = false;
                TolkHelper.Speak("RimWorldAccess.Caravan.Split.FailedToSplit".Loc(ex.Message), SpeechPriority.High);
                Log.Error($"RimWorld Access: Failed to split caravan: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets all selections.
        /// </summary>
        public static void Reset()
        {
            if (currentDialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Split.NoDialogAvailable".Loc(), SpeechPriority.High);
                return;
            }

            try
            {
                MethodInfo method = AccessTools.Method(typeof(Dialog_SplitCaravan), "CalculateAndRecacheTransferables");
                if (method != null)
                {
                    method.Invoke(currentDialog, null);
                    selectedIndex = 0;
                    TolkHelper.Speak("RimWorldAccess.Caravan.Split.SelectionsReset".Loc());
                    AnnounceCurrentItem();
                }
            }
            catch (Exception ex)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Split.FailedReset".Loc(ex.Message), SpeechPriority.High);
                Log.Error($"RimWorld Access: Failed to reset split caravan: {ex.Message}");
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
                case Tab.FoodAndMedicine: return "TravelSupplies".Translate();
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
        /// Handles keyboard input for split caravan.
        /// Returns true if the input was handled.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive)
                return false;

            // Handle Escape - only intercept to clear search, let game close dialog
            if (key == KeyCode.Escape)
            {
                // If a confirmation dialog is on top, close both the confirmation and the split dialog
                // PostClose patch will announce cancellation
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
                // Let the game handle escape - Window_PostClose_Postfix will announce cancellation
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
                else if (currentTab == Tab.Items || currentTab == Tab.FoodAndMedicine)
                {
                    OpenQuantityMenu(transferable);
                }

                Event.current.Use();
                return true;
            }

            // Handle inline quantity adjustment (Items, FoodAndMedicine, and grouped Pawns)
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
                    if (TransferableQuantityHelper.HandleQuantityInput(key, shift, ctrl, alt,
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
                    if (!shift && !ctrl && !alt)
                    {
                        if (showingSummary)
                        {
                            // Switch to other caravan (left = previous)
                            currentSummaryRow = currentSummaryRow == SummaryRow.NewCaravan
                                ? SummaryRow.OriginalCaravan
                                : SummaryRow.NewCaravan;
                            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                            AnnounceSummaryWithCaravan();
                        }
                        else
                        {
                            PreviousTab();
                        }
                        return true;
                    }
                    break;

                case KeyCode.RightArrow:
                    if (!shift && !ctrl && !alt)
                    {
                        if (showingSummary)
                        {
                            // Switch to other caravan (right = next)
                            currentSummaryRow = currentSummaryRow == SummaryRow.NewCaravan
                                ? SummaryRow.OriginalCaravan
                                : SummaryRow.NewCaravan;
                            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                            AnnounceSummaryWithCaravan();
                            return true;
                        }
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

                        // Items/FoodAndMedicine: Shift+Enter adds max that fits in remaining capacity
                        if (currentTab == Tab.Items || currentTab == Tab.FoodAndMedicine)
                        {
                            float remainingCapacity = GetDestRemainingCapacity();
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

                        // Items/FoodAndMedicine tab: Enter opens quantity menu
                        if (currentTab == Tab.Items || currentTab == Tab.FoodAndMedicine)
                        {
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

                        CaravanInputHelper.HandleDeleteKey(
                            transferable,
                            isPawnTab,
                            false, // SplitCaravan has no auto-provision
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
                                TolkHelper.Speak("RimWorldAccess.Caravan.Split.NoBreakdownForStat".Loc());
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
                        Split();
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

                case KeyCode.Home:
                    if (showingSummary)
                    {
                        // Go to first stat (Mass)
                        currentSummaryStat = SummaryStat.Mass;
                        AnnounceSummaryStat();
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
                        // Go to last stat (Visibility)
                        currentSummaryStat = SummaryStat.Visibility;
                        AnnounceSummaryStat();
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
