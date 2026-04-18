using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Sound;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages the windowless trade menu state for keyboard navigation.
    /// Provides comprehensive trading functionality with full feature parity to Dialog_Trade.
    /// </summary>
    public static class TradeNavigationState
    {
        private static bool isActive = false;
        private static TradeCategory currentCategory = TradeCategory.ColonyItems;
        private static int currentIndex = 0;
        private static bool isInQuantityMode = false;

        // Reference to the active trade dialog (kept open for game API compatibility)
        private static Dialog_Trade currentDialog = null;

        // References to active trade session (accessed via TradeSession for freshness)
        private static TradeDeal cachedDeal = null;
        private static ITrader cachedTrader = null;
        private static Pawn cachedNegotiator = null;

        // Cached tradeables organized by category
        private static List<Tradeable> colonyItemsList = new List<Tradeable>();
        private static List<Tradeable> traderItemsList = new List<Tradeable>();
        private static List<Tradeable> tradeSummaryList = new List<Tradeable>();

        // Tab position memory - remembers position when switching tabs
        private static Dictionary<TradeCategory, int> tabPositions = new Dictionary<TradeCategory, int>();
        private static TradeCategory previousCategory = TradeCategory.TraderItems;

        // Filter and sort state
        private static string filterText = "";
        private static TransferableSorterDef sorter1 = null;
        private static TransferableSorterDef sorter2 = null;

        // Typeahead search
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        // Numeric buffer for quantity input in quantity mode
        private static string numericBuffer = "";
        private static float lastNumericInputTime = 0f;
        private const float NUMERIC_INPUT_TIMEOUT = 10.0f;

        // Saved state to restore after trade closes
        private static IntVec3 savedCursorPosition = IntVec3.Invalid;
        private static bool savedWasOnWorldMap = false;
        private static int savedWorldTile = -1;
        private static bool viewStateSavedByPrefix = false;  // True if Prefix already saved view state

        /// <summary>
        /// Gets whether the trade menu is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets whether typeahead search is active.
        /// </summary>
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        /// <summary>
        /// Gets whether typeahead search has no matches.
        /// </summary>
        public static bool HasNoMatches => typeahead.HasNoMatches;

        /// <summary>
        /// Gets whether quantity adjustment mode is active.
        /// </summary>
        public static bool IsInQuantityMode => isInQuantityMode;

        /// <summary>
        /// Gets whether there is active numeric input in progress.
        /// </summary>
        public static bool HasActiveNumericInput => !string.IsNullOrEmpty(numericBuffer);

        /// <summary>
        /// Gets the current trade deal, preferring TradeSession.deal for freshness.
        /// Falls back to cached deal if TradeSession is not active.
        /// </summary>
        private static TradeDeal CurrentDeal => TradeSession.deal ?? cachedDeal;

        /// <summary>
        /// Trade categories for navigation.
        /// Three tabs: Their Items, Trade Summary (conditional), Your Items.
        /// Shared items appear in both inventory tabs with full buy/sell info.
        /// Trade Summary only appears when there are pending trades.
        /// </summary>
        private enum TradeCategory
        {
            ColonyItems,   // Your inventory (items you can sell)
            TraderItems,   // Their inventory (items you can buy)
            TradeSummary   // Pending trades (only visible when items are queued)
        }

        /// <summary>
        /// Saves the current view state before a trade dialog opens.
        /// Called by Harmony Prefix on CaravanArrivalAction_Trade.Arrived() BEFORE
        /// the game calls CameraJumper.TryJumpAndSelect() which would switch views.
        /// </summary>
        public static void SaveViewStateBeforeTrade()
        {
            // Check the current view state BEFORE RimWorld switches it
            savedWasOnWorldMap = Find.World?.renderer?.wantedMode == WorldRenderMode.Planet;

            if (savedWasOnWorldMap)
            {
                // On world map - save the selected world tile
                savedWorldTile = Find.WorldSelector?.SelectedTile ?? -1;
                savedCursorPosition = IntVec3.Invalid;
            }
            else
            {
                // On colony map - save the map cursor position
                savedWorldTile = -1;
                if (MapNavigationState.IsInitialized)
                {
                    savedCursorPosition = MapNavigationState.CurrentCursorPosition;
                }
                else
                {
                    savedCursorPosition = IntVec3.Invalid;
                }
            }

            viewStateSavedByPrefix = true;
        }

        /// <summary>
        /// Opens the trade menu with keyboard navigation for the given dialog.
        /// The dialog remains open for game API compatibility.
        /// </summary>
        /// <param name="dialog">The Dialog_Trade instance to provide navigation for</param>
        public static void Open(Dialog_Trade dialog)
        {
            if (dialog == null)
            {
                TolkHelper.Speak("No trade dialog");
                return;
            }

            if (!TradeSession.Active)
            {
                TolkHelper.Speak("No active trade session");
                return;
            }

            // Store dialog reference
            currentDialog = dialog;

            // Only save view state if it wasn't already saved by the Prefix patch.
            // The Prefix patch captures the view state BEFORE RimWorld's CameraJumper
            // switches the view, which gives us the correct original state.
            if (!viewStateSavedByPrefix)
            {
                // Fallback: save current view state (may not be accurate for caravan trades
                // since RimWorld switches to world map before we get here)
                savedWasOnWorldMap = Find.World?.renderer?.wantedMode == WorldRenderMode.Planet;

                if (savedWasOnWorldMap)
                {
                    savedWorldTile = Find.WorldSelector?.SelectedTile ?? -1;
                    savedCursorPosition = IntVec3.Invalid;
                }
                else
                {
                    savedWorldTile = -1;
                    if (MapNavigationState.IsInitialized)
                    {
                        savedCursorPosition = MapNavigationState.CurrentCursorPosition;
                    }
                    else
                    {
                        savedCursorPosition = IntVec3.Invalid;
                    }
                }
            }
            // Reset the flag so future trades can save state again
            viewStateSavedByPrefix = false;

            // Cache references from active session
            cachedDeal = TradeSession.deal;
            cachedTrader = TradeSession.trader;
            cachedNegotiator = TradeSession.playerNegotiator;

            if (cachedDeal == null || cachedTrader == null)
            {
                TolkHelper.Speak("Trade session not properly initialized");
                return;
            }

            // Initialize sort defaults (Category, then MarketValue)
            sorter1 = TransferableSorterDefOf.Category;
            sorter2 = TransferableSorterDefOf.MarketValue;

            // Pause the game when trade menu opens
            if (Current.ProgramState == ProgramState.Playing && Find.TickManager != null)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            }

            isActive = true;
            currentIndex = 0;
            isInQuantityMode = false;
            filterText = "";
            typeahead.ClearSearch();

            // Initialize tab position memory
            tabPositions.Clear();
            previousCategory = TradeCategory.TraderItems;

            // Build initial tradeable lists
            RefreshTradeables();

            // Start on Their Items tab (leftmost tab per user preference)
            currentCategory = TradeCategory.TraderItems;

            // Announce opening with essential controls only
            string traderName = cachedTrader.TraderName ?? "Unknown Trader";
            string traderKind = cachedTrader.TraderKind?.label ?? "trader";
            TolkHelper.Speak($"Trading with {traderName} ({traderKind}). Alt+B for balance, Alt+A to accept.");

            SoundDefOf.TabOpen.PlayOneShotOnCamera();


            // Announce first item without controls
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Closes the trade menu without executing the trade.
        /// Also closes the underlying Dialog_Trade.
        /// </summary>
        public static void Close()
        {
            // Store dialog reference before clearing state
            var dialogToClose = currentDialog;

            // Clear our state
            isActive = false;
            isInQuantityMode = false;
            currentIndex = 0;
            currentCategory = TradeCategory.TraderItems;

            colonyItemsList.Clear();
            traderItemsList.Clear();
            tradeSummaryList.Clear();
            tabPositions.Clear();

            currentDialog = null;
            cachedDeal = null;
            cachedTrader = null;
            cachedNegotiator = null;
            filterText = "";
            typeahead.ClearSearch();

            // Restore the view state to where the user was before trading
            RestoreViewState();

            // Close the trade dialog if it still exists
            if (dialogToClose != null && Find.WindowStack != null)
            {
                Find.WindowStack.TryRemove(dialogToClose, doCloseSound: false);
            }

            // Note: We intentionally do NOT call TradeSession.Close() here.
            // RimWorld's own Dialog_Trade never closes the trade session either.
            // Closing it while tooltips are still pending causes NullReferenceException
            // because tooltip lambdas try to access TradeSession.TradeCurrency.
            // The next trade will call SetupWith() which overwrites all fields anyway.
        }

        /// <summary>
        /// Closes the trade interface with a cancellation announcement.
        /// Used when user presses Escape to cancel.
        /// </summary>
        public static void CloseAndAnnounceCancel()
        {
            Close();
            TolkHelper.Speak("Trade cancelled");
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Called when the dialog is closing externally (not via our Close method).
        /// Cleans up our state without attempting to close the dialog again.
        /// </summary>
        public static void OnDialogClosing()
        {
            if (!isActive)
                return;

            isActive = false;
            isInQuantityMode = false;
            currentIndex = 0;
            currentCategory = TradeCategory.TraderItems;

            colonyItemsList.Clear();
            traderItemsList.Clear();
            tradeSummaryList.Clear();
            tabPositions.Clear();

            currentDialog = null;
            cachedDeal = null;
            cachedTrader = null;
            cachedNegotiator = null;
            filterText = "";
            typeahead.ClearSearch();

            // Restore the view state to where the user was before trading
            RestoreViewState();

            // Don't announce - the dialog closing might be due to successful trade
        }

        /// <summary>
        /// Restores the view state (world map vs colony map) and cursor position
        /// to where the user was before trading began.
        /// </summary>
        private static void RestoreViewState()
        {
            if (!savedWasOnWorldMap)
            {
                // User was on colony map - switch back to colony map and restore cursor
                CameraJumper.TryHideWorld();

                if (savedCursorPosition.IsValid)
                {
                    MapNavigationState.SetPendingRestorePosition(savedCursorPosition);
                    if (MapNavigationState.IsInitialized)
                    {
                        MapNavigationState.CurrentCursorPosition = savedCursorPosition;
                    }
                }
            }
            else
            {
                // User was on world map - ensure world view is shown and restore tile selection
                CameraJumper.TryShowWorld();

                if (savedWorldTile >= 0 && Find.WorldSelector != null)
                {
                    Find.WorldSelector.SelectedTile = savedWorldTile;
                    // Also update WorldNavigationState if it's tracking position
                    WorldNavigationState.CurrentSelectedTile = savedWorldTile;
                }
            }

            // Clear saved state
            savedCursorPosition = IntVec3.Invalid;
            savedWasOnWorldMap = false;
            savedWorldTile = -1;
        }

        /// <summary>
        /// Notifies the trade system that quantities have changed.
        /// Updates currency counts and marks the dialog's cached values as dirty.
        /// </summary>
        private static void NotifyTradeChanged()
        {
            // Update currency calculation using fresh deal reference
            TradeDeal deal = CurrentDeal;
            if (deal != null)
            {
                deal.UpdateCurrencyCount();
            }

            // Notify the dialog so it recalculates mass/food/etc for caravan trades
            if (currentDialog != null)
            {
                try
                {
                    MethodInfo method = AccessTools.Method(typeof(Dialog_Trade), "CountToTransferChanged");
                    if (method == null)
                    {
                        Log.Warning("RimWorld Access: Could not find CountToTransferChanged method via reflection");
                    }
                    else
                    {
                        method.Invoke(currentDialog, null);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"RimWorld Access: Could not notify dialog of trade change: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Refreshes the tradeable lists from the current deal and applies filtering/sorting.
        /// Shared items (both parties have) appear in BOTH tabs.
        /// Trade Summary contains items with pending trades (non-zero CountToTransfer).
        /// </summary>
        private static void RefreshTradeables()
        {
            TradeDeal deal = CurrentDeal;
            if (deal == null)
                return;

            colonyItemsList.Clear();
            traderItemsList.Clear();
            tradeSummaryList.Clear();

            // Organize all tradeables into categories
            List<Tradeable> allTradeables = deal.AllTradeables.ToList();

            foreach (Tradeable tradeable in allTradeables)
            {
                // Skip currency - handled via Alt+B for balance
                if (tradeable.IsCurrency)
                    continue;

                // Apply filter (only for inventory tabs, not summary)
                bool passesFilter = string.IsNullOrEmpty(filterText) ||
                    tradeable.Label.ToLower().Contains(filterText.ToLower());

                int colonyCount = tradeable.CountHeldBy(Transactor.Colony);
                int traderCount = tradeable.CountHeldBy(Transactor.Trader);

                // Colony items: colony has it (includes shared items)
                if (colonyCount > 0 && passesFilter)
                    colonyItemsList.Add(tradeable);

                // Trader items: trader has it (includes shared items)
                if (traderCount > 0 && passesFilter)
                    traderItemsList.Add(tradeable);

                // Trade Summary: any item with pending trade (always included, ignores filter)
                if (tradeable.CountToTransfer != 0)
                    tradeSummaryList.Add(tradeable);
            }

            // Sort inventory lists by category/value
            SortTradeableList(colonyItemsList);
            SortTradeableList(traderItemsList);

            // Sort summary list: buying items first (positive), then selling (negative)
            tradeSummaryList.Sort((a, b) =>
            {
                // Buying (positive) comes before selling (negative)
                bool aBuying = a.CountToTransfer > 0;
                bool bBuying = b.CountToTransfer > 0;
                if (aBuying != bBuying)
                    return bBuying.CompareTo(aBuying); // true (buying) before false (selling)

                // Within same action type, sort alphabetically
                return GetCleanLabel(a).CompareTo(GetCleanLabel(b));
            });

            // Clamp current index to valid range
            ClampCurrentIndex();
        }

        /// <summary>
        /// Lightweight update that only refreshes the Trade Summary list.
        /// Used after quantity changes since inventory lists don't change.
        /// </summary>
        private static void RefreshTradeSummaryOnly()
        {
            TradeDeal deal = CurrentDeal;
            if (deal == null)
                return;

            tradeSummaryList.Clear();

            foreach (Tradeable tradeable in deal.AllTradeables)
            {
                if (tradeable.IsCurrency)
                    continue;

                if (tradeable.CountToTransfer != 0)
                    tradeSummaryList.Add(tradeable);
            }

            // Sort summary list: buying items first (positive), then selling (negative)
            tradeSummaryList.Sort((a, b) =>
            {
                bool aBuying = a.CountToTransfer > 0;
                bool bBuying = b.CountToTransfer > 0;
                if (aBuying != bBuying)
                    return bBuying.CompareTo(aBuying);
                return GetCleanLabel(a).CompareTo(GetCleanLabel(b));
            });

            // Only clamp if we're in Trade Summary tab
            if (currentCategory == TradeCategory.TradeSummary)
                ClampCurrentIndex();
        }

        /// <summary>
        /// Sorts a tradeable list using the current sorters.
        /// </summary>
        private static void SortTradeableList(List<Tradeable> list)
        {
            if (list == null || list.Count == 0)
                return;

            // Sort using RimWorld's TransferableComparer system
            list.Sort(delegate(Tradeable a, Tradeable b)
            {
                // Non-tradeable items go to bottom
                if (!a.TraderWillTrade && b.TraderWillTrade)
                    return 1;
                if (a.TraderWillTrade && !b.TraderWillTrade)
                    return -1;

                // Apply primary sorter
                if (sorter1 != null)
                {
                    int result1 = sorter1.Comparer.Compare(a, b);
                    if (result1 != 0)
                        return result1;
                }

                // Apply secondary sorter
                if (sorter2 != null)
                {
                    int result2 = sorter2.Comparer.Compare(a, b);
                    if (result2 != 0)
                        return result2;
                }

                // Fall back to alphabetical by label
                return a.Label.CompareTo(b.Label);
            });
        }

        /// <summary>
        /// Gets the current category's list.
        /// </summary>
        private static List<Tradeable> GetCurrentList()
        {
            switch (currentCategory)
            {
                case TradeCategory.ColonyItems:
                    return colonyItemsList;
                case TradeCategory.TraderItems:
                    return traderItemsList;
                case TradeCategory.TradeSummary:
                    return tradeSummaryList;
                default:
                    return new List<Tradeable>();
            }
        }

        /// <summary>
        /// Gets the currently selected tradeable.
        /// </summary>
        private static Tradeable GetCurrentTradeable()
        {
            List<Tradeable> list = GetCurrentList();
            if (list.Count == 0 || currentIndex < 0 || currentIndex >= list.Count)
                return null;
            return list[currentIndex];
        }

        /// <summary>
        /// Clamps the current index to valid range for current category.
        /// Trade Summary tab allows index == list.Count for the balance entry.
        /// </summary>
        private static void ClampCurrentIndex()
        {
            List<Tradeable> list = GetCurrentList();
            if (list.Count == 0)
            {
                currentIndex = 0;
            }
            else if (currentCategory == TradeCategory.TradeSummary)
            {
                // Trade Summary allows index up to list.Count (balance entry)
                currentIndex = Mathf.Clamp(currentIndex, 0, list.Count);
            }
            else
            {
                currentIndex = Mathf.Clamp(currentIndex, 0, list.Count - 1);
            }
        }

        /// <summary>
        /// Checks if we're currently on the balance entry in Trade Summary.
        /// </summary>
        private static bool IsOnBalanceEntry()
        {
            return currentCategory == TradeCategory.TradeSummary &&
                   currentIndex == tradeSummaryList.Count &&
                   tradeSummaryList.Count > 0;
        }

        /// <summary>
        /// Moves to the next item in the current category.
        /// In quantity mode: Down arrow = less of current action (toward 0).
        /// Trade Summary includes a balance entry at the end.
        /// </summary>
        public static void SelectNext()
        {
            if (isInQuantityMode)
            {
                // Down arrow: less of current action (toward 0)
                // Context-aware via GetQuantityDelta
                int delta = GetQuantityDelta(upDirection: false);
                AdjustQuantity(delta);
                return;
            }

            List<Tradeable> list = GetCurrentList();
            if (list.Count == 0)
            {
                TolkHelper.Speak("No items in this category");
                return;
            }

            // Trade Summary has balance entry at index list.Count
            int maxIndex = currentCategory == TradeCategory.TradeSummary ? list.Count : list.Count - 1;

            // Respect wrap navigation setting
            if (currentIndex < maxIndex)
            {
                currentIndex++;
            }
            else if (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true)
            {
                currentIndex = 0;
            }
            // else: stay at end

            AnnounceCurrentSelection();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Moves to the previous item in the current category.
        /// In quantity mode: Up arrow = more of current action (away from 0).
        /// Trade Summary includes a balance entry at the end.
        /// </summary>
        public static void SelectPrevious()
        {
            if (isInQuantityMode)
            {
                // Up arrow: more of current action (away from 0)
                // Context-aware via GetQuantityDelta
                int delta = GetQuantityDelta(upDirection: true);
                AdjustQuantity(delta);
                return;
            }

            List<Tradeable> list = GetCurrentList();
            if (list.Count == 0)
            {
                TolkHelper.Speak("No items in this category");
                return;
            }

            // Trade Summary has balance entry at index list.Count
            int maxIndex = currentCategory == TradeCategory.TradeSummary ? list.Count : list.Count - 1;

            // Respect wrap navigation setting
            if (currentIndex > 0)
            {
                currentIndex--;
            }
            else if (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true)
            {
                currentIndex = maxIndex;
            }
            // else: stay at start

            AnnounceCurrentSelection();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Gets the list of available categories.
        /// Order: Their Items → Trade Summary (if pending) → Your Items
        /// </summary>
        private static List<TradeCategory> GetAvailableCategories()
        {
            var categories = new List<TradeCategory>
            {
                TradeCategory.TraderItems  // Their Items (what you can buy)
            };

            // Only show Trade Summary if there are pending trades
            if (tradeSummaryList.Count > 0)
            {
                categories.Add(TradeCategory.TradeSummary);
            }

            categories.Add(TradeCategory.ColonyItems);  // Your Items (what you can sell)

            return categories;
        }

        /// <summary>
        /// Switches to the next category, saving and restoring position per tab.
        /// </summary>
        public static void NextCategory()
        {
            if (isInQuantityMode)
            {
                TolkHelper.Speak("Exit quantity mode first (press Enter or Escape)");
                return;
            }

            var categories = GetAvailableCategories();
            int currentIdx = categories.IndexOf(currentCategory);
            if (currentIdx < 0) currentIdx = 0;

            // Save current position before switching
            tabPositions[currentCategory] = currentIndex;
            previousCategory = currentCategory;

            // Switch to next tab
            currentCategory = categories[(currentIdx + 1) % categories.Count];

            // Restore saved position for this tab, or start at 0
            currentIndex = tabPositions.TryGetValue(currentCategory, out int savedPos) ? savedPos : 0;
            ClampCurrentIndex();
            typeahead.ClearSearch();

            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            AnnounceCategorySwitch();
        }

        /// <summary>
        /// Switches to the previous category, saving and restoring position per tab.
        /// </summary>
        public static void PreviousCategory()
        {
            if (isInQuantityMode)
            {
                TolkHelper.Speak("Exit quantity mode first (press Enter or Escape)");
                return;
            }

            var categories = GetAvailableCategories();
            int currentIdx = categories.IndexOf(currentCategory);
            if (currentIdx < 0) currentIdx = 0;

            // Save current position before switching
            tabPositions[currentCategory] = currentIndex;
            previousCategory = currentCategory;

            // Switch to previous tab
            currentCategory = categories[(currentIdx + categories.Count - 1) % categories.Count];

            // Restore saved position for this tab, or start at 0
            currentIndex = tabPositions.TryGetValue(currentCategory, out int savedPos) ? savedPos : 0;
            ClampCurrentIndex();
            typeahead.ClearSearch();

            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            AnnounceCategorySwitch();
        }

        /// <summary>
        /// Toggles or enters quantity adjustment mode for the current item.
        /// If already in quantity mode, exits it. Otherwise, enters it.
        /// </summary>
        public static void EnterQuantityMode()
        {
            // If already in quantity mode, exit it
            if (isInQuantityMode)
            {
                isInQuantityMode = false;
                numericBuffer = ""; // Clear numeric input when exiting
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentSelection();
                return;
            }

            // Otherwise, try to enter quantity mode
            Tradeable tradeable = GetCurrentTradeable();
            if (!GuardHelper.RequireItem(tradeable)) return;

            if (!tradeable.Interactive)
            {
                TolkHelper.Speak("Cannot adjust quantity for this item", SpeechPriority.High);
                return;
            }

            // In gift mode, any item can be gifted regardless of TraderWillTrade
            if (!tradeable.TraderWillTrade && !TradeSession.giftMode)
            {
                TolkHelper.Speak("Trader will not trade this item");
                return;
            }

            isInQuantityMode = true;
            numericBuffer = ""; // Clear numeric input when entering
            SoundDefOf.Click.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Exits quantity adjustment mode and returns to list view.
        /// Returns true if we exited quantity mode, false if we were already in list view.
        /// </summary>
        public static bool ExitQuantityMode()
        {
            if (!isInQuantityMode)
                return false;

            isInQuantityMode = false;
            numericBuffer = ""; // Clear any pending numeric input
            SoundDefOf.Click.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
            return true;
        }

        /// <summary>
        /// Adjusts the trade quantity for the current item.
        /// </summary>
        public static void AdjustQuantity(int delta)
        {
            Tradeable tradeable = GetCurrentTradeable();
            if (tradeable == null)
                return;

            // In gift mode, any item can be gifted regardless of TraderWillTrade
            if (!tradeable.TraderWillTrade && !TradeSession.giftMode)
            {
                TolkHelper.Speak("Trader will not trade this item");
                return;
            }

            int newAmount = tradeable.CountToTransfer + delta;

            // Clamp to valid range
            int minAmount = tradeable.GetMinimumToTransfer();
            int maxAmount = tradeable.GetMaximumToTransfer();
            newAmount = Mathf.Clamp(newAmount, minAmount, maxAmount);

            if (tradeable.CanAdjustTo(newAmount))
            {
                tradeable.AdjustTo(newAmount);
                NotifyTradeChanged();
                RefreshTradeSummaryOnly(); // Lightweight update for Trade Summary
                AnnounceQuantityChange(tradeable);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
            else
            {
                TolkHelper.Speak("Cannot adjust to this amount", SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        /// <summary>
        /// Returns true if the current context is "selling-primary" - meaning Up/Home
        /// should move toward more negative (more selling).
        ///
        /// IMPORTANT: Gift mode is NOT selling-primary! In gift mode, the game inverts
        /// the sign convention: positive CountToTransfer = gifting (giving items).
        /// So gift mode behaves like buying (Up = +1 = more action).
        ///
        /// Selling-primary is ONLY true when: you have items AND trader doesn't AND NOT gift mode.
        /// </summary>
        private static bool IsSellingPrimaryContext()
        {
            // Gift mode uses POSITIVE numbers for gifting, so it's like buying direction
            if (TradeSession.giftMode)
                return false;

            Tradeable tradeable = GetCurrentTradeable();
            if (tradeable == null)
                return false;

            int colonyCount = tradeable.CountHeldBy(Transactor.Colony);
            int traderCount = tradeable.CountHeldBy(Transactor.Trader);

            // Selling-primary only if you have items but trader doesn't (and not gift mode)
            return colonyCount > 0 && traderCount == 0;
        }

        /// <summary>
        /// Gets the delta for quantity adjustment based on context.
        /// Selling-primary context: Up = -1 (sell/gift more), Down = +1 (toward 0)
        /// Buying/shared context: Up = +1 (buy more), Down = -1 (sell more or toward 0)
        /// </summary>
        private static int GetQuantityDelta(bool upDirection)
        {
            if (IsSellingPrimaryContext())
            {
                // Up = more negative (more selling/gifting)
                return upDirection ? -1 : 1;
            }
            else
            {
                // Up = more positive (more buying)
                return upDirection ? 1 : -1;
            }
        }

        /// <summary>
        /// Adjusts quantity by 1, respecting selling/buying context.
        /// Uses GetQuantityDelta for direction based on item type.
        /// </summary>
        public static void AdjustQuantitySingle(int direction)
        {
            // direction: 1 = Up/Plus, -1 = Down/Minus
            bool upDirection = direction > 0;
            int delta = GetQuantityDelta(upDirection);
            AdjustQuantity(delta);
        }

        /// <summary>
        /// Adjusts quantity by larger amounts (10).
        /// Uses GetQuantityDelta for direction based on item type.
        /// </summary>
        public static void AdjustQuantityLarge(int direction)
        {
            // direction: 1 = Up, -1 = Down
            bool upDirection = direction > 0;
            int delta = GetQuantityDelta(upDirection);
            AdjustQuantity(delta * 10);
        }

        /// <summary>
        /// Adjusts quantity by very large amounts (100).
        /// Uses GetQuantityDelta for direction based on item type.
        /// </summary>
        public static void AdjustQuantityVeryLarge(int direction)
        {
            // direction: 1 = Up, -1 = Down
            bool upDirection = direction > 0;
            int delta = GetQuantityDelta(upDirection);
            AdjustQuantity(delta * 100);
        }

        /// <summary>
        /// Sets quantity to maximum (End/Shift+End).
        /// Always goes to GetMaximumToTransfer() (most positive = most buying/gifting).
        /// </summary>
        public static void SetToMaximumAction()
        {
            Tradeable tradeable = GetCurrentTradeable();
            if (!GuardHelper.RequireItem(tradeable)) return;

            // In gift mode, any item can be gifted regardless of TraderWillTrade
            if (!tradeable.TraderWillTrade && !TradeSession.giftMode)
            {
                TolkHelper.Speak("Trader will not trade this item");
                return;
            }

            int targetAmount = tradeable.GetMaximumToTransfer();

            if (tradeable.CanAdjustTo(targetAmount))
            {
                tradeable.AdjustTo(targetAmount);
                NotifyTradeChanged();
                RefreshTradeSummaryOnly();
                AnnounceMaxAction(tradeable, targetAmount);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            else
            {
                TolkHelper.Speak("Cannot adjust to maximum");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        /// <summary>
        /// Sets quantity to minimum (Home/Shift+Home).
        /// Always goes to GetMinimumToTransfer() (most negative = most selling).
        /// </summary>
        public static void SetToMinimumAction()
        {
            Tradeable tradeable = GetCurrentTradeable();
            if (!GuardHelper.RequireItem(tradeable)) return;

            // In gift mode, any item can be gifted regardless of TraderWillTrade
            if (!tradeable.TraderWillTrade && !TradeSession.giftMode)
            {
                TolkHelper.Speak("Trader will not trade this item");
                return;
            }

            int targetAmount = tradeable.GetMinimumToTransfer();

            if (tradeable.CanAdjustTo(targetAmount))
            {
                tradeable.AdjustTo(targetAmount);
                NotifyTradeChanged();
                RefreshTradeSummaryOnly();
                AnnounceMaxAction(tradeable, targetAmount);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            else
            {
                TolkHelper.Speak("Cannot adjust to minimum");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        /// <summary>
        /// Announces the result of a max/min action.
        /// </summary>
        private static void AnnounceMaxAction(Tradeable tradeable, int amount)
        {
            int count = Math.Abs(amount);
            float value = Math.Abs(tradeable.CurTotalCurrencyCostForDestination);

            if (amount == 0)
            {
                TolkHelper.Speak($"Reset {GetCleanLabel(tradeable)} to none");
            }
            else if (amount < 0)
            {
                // Negative = selling/gifting
                string action = TradeSession.giftMode ? "Gifting" : "Selling";
                TolkHelper.Speak($"{action} all {count} {GetCleanLabel(tradeable)}, {FormatPrice(value)}");
            }
            else
            {
                // Positive = buying
                TolkHelper.Speak($"Buying all {count} {GetCleanLabel(tradeable)}, {FormatPrice(value)}");
            }
        }

        /// <summary>
        /// Resets the current item's quantity to zero.
        /// If in Trade Summary and it becomes empty, auto-switches to previous tab.
        /// </summary>
        public static void ResetCurrentItem()
        {
            // Can't reset the balance entry
            if (IsOnBalanceEntry())
            {
                TolkHelper.Speak("Cannot reset balance entry");
                return;
            }

            Tradeable tradeable = GetCurrentTradeable();
            if (!GuardHelper.RequireItem(tradeable)) return;

            if (tradeable.CanAdjustTo(0))
            {
                tradeable.AdjustTo(0);
                NotifyTradeChanged();
                TolkHelper.Speak($"Reset {GetCleanLabel(tradeable)}");
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();

                // Refresh Trade Summary list
                RefreshTradeSummaryOnly();

                // If Trade Summary is now empty, switch to previous tab
                if (currentCategory == TradeCategory.TradeSummary && tradeSummaryList.Count == 0)
                {
                    SwitchToPreviousTab();
                    return;
                }

                // Clamp index and announce new selection
                ClampCurrentIndex();
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Handles numeric input for quantity mode.
        /// Allows typing a number to set the exact quantity.
        /// Positive numbers = buying, negative numbers (after minus key) = selling.
        /// </summary>
        public static void HandleNumericInput(char c)
        {
            if (!isActive || !isInQuantityMode)
                return;

            Tradeable tradeable = GetCurrentTradeable();
            if (tradeable == null)
                return;

            float currentTime = Time.realtimeSinceStartup;

            // Check timeout for numeric buffer - reset if too much time has passed
            if (currentTime - lastNumericInputTime > NUMERIC_INPUT_TIMEOUT)
            {
                numericBuffer = "";
            }
            lastNumericInputTime = currentTime;

            // Handle minus sign - explicit override to SELL (negative quantity)
            if (c == '-' && string.IsNullOrEmpty(numericBuffer))
            {
                numericBuffer = "-";
                string action = TradeSession.giftMode ? "Gifting" : "Selling";
                TolkHelper.Speak($"{action}. Type quantity.");
                return;
            }

            // Handle plus sign - explicit override to BUY (positive quantity)
            if (c == '+' && string.IsNullOrEmpty(numericBuffer))
            {
                numericBuffer = "+";
                TolkHelper.Speak("Buying. Type quantity.");
                return;
            }

            // Build numeric buffer
            numericBuffer += c;

            // Try to parse as a number
            if (int.TryParse(numericBuffer, out int rawQty))
            {
                // Apply context-aware sign if user didn't specify one explicitly
                bool hasExplicitSign = numericBuffer.StartsWith("-") || numericBuffer.StartsWith("+");
                int targetQty = rawQty;

                if (!hasExplicitSign && IsSellingPrimaryContext())
                {
                    // In selling context (non-gift), positive input = negative quantity (selling)
                    // Note: Gift mode uses positive numbers for gifting, so no conversion needed
                    targetQty = -Math.Abs(rawQty);
                }
                // Clamp to valid range
                int minAmount = tradeable.GetMinimumToTransfer();
                int maxAmount = tradeable.GetMaximumToTransfer();
                targetQty = Mathf.Clamp(targetQty, minAmount, maxAmount);

                if (tradeable.CanAdjustTo(targetQty))
                {
                    tradeable.AdjustTo(targetQty);
                    NotifyTradeChanged();
                    RefreshTradeSummaryOnly();

                    // Announce the result with price (same format as arrow key adjustment)
                    string action = targetQty > 0 ? "Buying" :
                                   (targetQty < 0 ? (TradeSession.giftMode ? "Gifting" : "Selling") : "No trade");
                    int count = Math.Abs(targetQty);

                    if (targetQty == 0)
                    {
                        TolkHelper.Speak($"{GetCleanLabel(tradeable)}: No trade. Typing: {numericBuffer}");
                    }
                    else if (TradeSession.giftMode)
                    {
                        TolkHelper.Speak($"{action} {count} {GetCleanLabel(tradeable)}. Typing: {numericBuffer}");
                    }
                    else
                    {
                        float totalCost = Math.Abs(tradeable.CurTotalCurrencyCostForDestination);
                        TolkHelper.Speak($"{action} {count} {GetCleanLabel(tradeable)} for {FormatPrice(totalCost)}. Typing: {numericBuffer}");
                    }
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }
            }
            else if (numericBuffer != "-")
            {
                // Invalid number (but not just a minus sign)
                TolkHelper.Speak($"Invalid: {numericBuffer}");
                numericBuffer = "";
            }
        }

        /// <summary>
        /// Handles backspace in numeric input mode.
        /// </summary>
        public static void HandleNumericBackspace()
        {
            if (!isActive || !isInQuantityMode || string.IsNullOrEmpty(numericBuffer))
                return;

            Tradeable tradeable = GetCurrentTradeable();
            if (tradeable == null)
                return;

            // Remove the last character from the buffer
            numericBuffer = numericBuffer.Substring(0, numericBuffer.Length - 1);

            // If buffer is empty or just a sign, reset to zero
            if (string.IsNullOrEmpty(numericBuffer) || numericBuffer == "-" || numericBuffer == "+")
            {
                if (tradeable.CanAdjustTo(0))
                {
                    tradeable.AdjustTo(0);
                    NotifyTradeChanged();
                    RefreshTradeSummaryOnly();
                }
                string message = string.IsNullOrEmpty(numericBuffer) ? "Cleared" : $"Typing: {numericBuffer}";
                TolkHelper.Speak($"{GetCleanLabel(tradeable)}: No trade. {message}");
                return;
            }

            // Try to parse and apply the new quantity
            if (int.TryParse(numericBuffer, out int rawQty))
            {
                // Apply context-aware sign if user didn't specify one explicitly
                bool hasExplicitSign = numericBuffer.StartsWith("-") || numericBuffer.StartsWith("+");
                int targetQty = rawQty;

                if (!hasExplicitSign && IsSellingPrimaryContext())
                {
                    targetQty = -Math.Abs(rawQty);
                }

                // Clamp to valid range
                int minAmount = tradeable.GetMinimumToTransfer();
                int maxAmount = tradeable.GetMaximumToTransfer();
                targetQty = Mathf.Clamp(targetQty, minAmount, maxAmount);

                if (tradeable.CanAdjustTo(targetQty))
                {
                    tradeable.AdjustTo(targetQty);
                    NotifyTradeChanged();
                    RefreshTradeSummaryOnly();

                    // Announce result (same format as HandleNumericInput)
                    string action = targetQty > 0 ? "Buying" :
                                   (targetQty < 0 ? (TradeSession.giftMode ? "Gifting" : "Selling") : "No trade");
                    int count = Math.Abs(targetQty);

                    if (targetQty == 0)
                    {
                        TolkHelper.Speak($"{GetCleanLabel(tradeable)}: No trade. Typing: {numericBuffer}");
                    }
                    else if (TradeSession.giftMode)
                    {
                        TolkHelper.Speak($"{action} {count} {GetCleanLabel(tradeable)}. Typing: {numericBuffer}");
                    }
                    else
                    {
                        float totalCost = Math.Abs(tradeable.CurTotalCurrencyCostForDestination);
                        TolkHelper.Speak($"{action} {count} {GetCleanLabel(tradeable)} for {FormatPrice(totalCost)}. Typing: {numericBuffer}");
                    }
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }
            }
            else
            {
                // Buffer isn't a valid number yet, just announce what's typed
                TolkHelper.Speak($"Typing: {numericBuffer}");
            }
        }

        /// <summary>
        /// Clears the numeric input buffer.
        /// Called when exiting quantity mode.
        /// </summary>
        public static void ClearNumericBuffer()
        {
            numericBuffer = "";
        }

        /// <summary>
        /// Switches to the previous tab (used when Trade Summary becomes empty).
        /// Restores the saved position for that tab.
        /// </summary>
        private static void SwitchToPreviousTab()
        {
            // Save current position before switching (for when items are added back later)
            tabPositions[currentCategory] = currentIndex;

            // Get the tab to switch to (prefer previousCategory if it's still available)
            var categories = GetAvailableCategories();
            TradeCategory targetTab = previousCategory;

            // If previous tab isn't available, go to first tab
            if (!categories.Contains(targetTab))
            {
                targetTab = categories[0];
            }

            currentCategory = targetTab;
            currentIndex = tabPositions.TryGetValue(currentCategory, out int savedPos) ? savedPos : 0;
            ClampCurrentIndex();

            TolkHelper.Speak($"Trade Summary empty. Returning to {GetCategoryName()}");
            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Resets all trade quantities.
        /// </summary>
        public static void ResetAll()
        {
            TradeDeal deal = CurrentDeal;
            if (deal == null)
                return;

            deal.Reset();
            RefreshTradeables();
            TolkHelper.Speak("All trades reset");
            SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Toggles gift mode if available.
        /// </summary>
        public static void ToggleGiftMode()
        {
            if (cachedTrader == null || cachedTrader.Faction == null)
            {
                TolkHelper.Speak("Cannot gift to this trader", SpeechPriority.High);
                return;
            }

            if (cachedTrader.Faction.HostileTo(Faction.OfPlayer))
            {
                TolkHelper.Speak("Cannot gift to hostile faction", SpeechPriority.High);
                return;
            }

            if (cachedTrader.TradeCurrency == TradeCurrency.Favor)
            {
                TolkHelper.Speak("Cannot gift when trading for royal favor", SpeechPriority.High);
                return;
            }

            TradeSession.giftMode = !TradeSession.giftMode;
            CurrentDeal?.Reset();
            RefreshTradeables();

            // When entering gift mode, switch to your items tab (can only gift what you have)
            if (TradeSession.giftMode && currentCategory != TradeCategory.ColonyItems)
            {
                currentCategory = TradeCategory.ColonyItems;
                currentIndex = 0;
            }

            string mode = TradeSession.giftMode ? "gift mode" : "trade mode";
            TolkHelper.Speak($"Switched to {mode}");
            SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Executes the trade directly. Game handles any error dialogs.
        /// </summary>
        public static void AcceptTrade()
        {
            TradeDeal deal = CurrentDeal;
            if (deal == null)
            {
                TolkHelper.Speak("No trade to execute");
                return;
            }

            // Check if in gift mode
            if (TradeSession.giftMode)
            {
                AcceptGift();
                return;
            }

            // Check if trader has enough silver before executing
            if (TradeSession.deal.DoesTraderHaveEnoughSilver())
            {
                ExecuteTradeAction();
            }
            else
            {
                // Trader doesn't have enough funds - warn and ask for confirmation
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Warning: Trader does not have enough silver. Confirm to proceed anyway.", SpeechPriority.High);
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "ConfirmTraderShortFunds".Translate(),
                    ExecuteTradeAction));
            }
        }

        /// <summary>
        /// Actually executes the trade. Called directly or as confirmation callback.
        /// </summary>
        private static void ExecuteTradeAction()
        {
            TradeDeal deal = CurrentDeal;
            if (deal == null)
            {
                return;
            }

            // Try to execute the trade - game shows error dialogs if needed
            bool actuallyTraded = false;
            AcceptanceReport result = deal.TryExecute(out actuallyTraded);

            if (!result.Accepted)
            {
                // Announce failure reason - game may also show a dialog
                if (!string.IsNullOrEmpty(result.Reason))
                {
                    TolkHelper.Speak($"Cannot complete trade: {result.Reason}", SpeechPriority.High);
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }
                // Refresh our state in case the game modified trade data during the failed attempt
                RefreshTradeables();
                return;
            }

            if (actuallyTraded)
            {
                SoundDefOf.ExecuteTrade.PlayOneShotOnCamera();
                TolkHelper.Speak("Trade completed successfully");

                // Close our state - dialog closes naturally via TradeSession.Close
                Close();
            }
            else
            {
                TolkHelper.Speak("No items to trade");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                // Refresh in case state is out of sync
                RefreshTradeables();
            }
        }

        /// <summary>
        /// Accepts gifts (when in gift mode).
        /// Protected against errors to prevent state desync.
        /// </summary>
        private static void AcceptGift()
        {
            TradeDeal deal = CurrentDeal;
            if (deal == null || cachedTrader == null)
            {
                TolkHelper.Speak("No gift session active");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            if (cachedTrader.Faction == null)
            {
                TolkHelper.Speak("Cannot gift to this trader");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                RefreshTradeables();
                return;
            }

            // Calculate goodwill change
            int goodwillChange;
            try
            {
                goodwillChange = FactionGiftUtility.GetGoodwillChange(deal.AllTradeables.ToList(), cachedTrader.Faction);
            }
            catch (System.Exception ex)
            {
                Log.Warning($"RimWorld Access: Error calculating gift goodwill: {ex.Message}");
                TolkHelper.Speak("Error calculating gift value");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                RefreshTradeables();
                return;
            }

            if (goodwillChange <= 0)
            {
                TolkHelper.Speak("No gifts to offer");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                // Refresh in case state is out of sync
                RefreshTradeables();
                return;
            }

            // Build a look target for the gift
            GlobalTargetInfo lookTarget = GlobalTargetInfo.Invalid;

            // Try to get a valid look target
            if (cachedTrader is Pawn pawn)
            {
                lookTarget = new GlobalTargetInfo(pawn);
            }
            else if (cachedTrader is Settlement settlement)
            {
                lookTarget = new GlobalTargetInfo(settlement);
            }
            else if (cachedNegotiator != null)
            {
                lookTarget = new GlobalTargetInfo(cachedNegotiator);
            }

            // Execute the gift with error protection
            try
            {
                FactionGiftUtility.GiveGift(deal.AllTradeables.ToList(), cachedTrader.Faction, lookTarget);

                SoundDefOf.ExecuteTrade.PlayOneShotOnCamera();
                TolkHelper.Speak($"Gifts offered, goodwill +{goodwillChange}");

                // Close the trade dialog (don't call TradeSession.Close - see Close() comment)
                Close();
            }
            catch (System.Exception ex)
            {
                Log.Warning($"RimWorld Access: Error executing gift: {ex.Message}");
                TolkHelper.Speak("Error offering gifts");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                // Refresh state to prevent desync
                RefreshTradeables();
            }
        }

        /// <summary>
        /// Announces the current category switch.
        /// Trade Summary shows item count plus balance entry.
        /// Inventory tabs announce the relevant party's currency balance.
        /// </summary>
        private static void AnnounceCategorySwitch()
        {
            string categoryName = GetCategoryName();
            List<Tradeable> list = GetCurrentList();

            // Build announcement with currency info for inventory tabs
            string announcement = categoryName;

            // Add currency announcement for inventory tabs
            TradeDeal deal = TradeSession.deal ?? cachedDeal;
            Tradeable currency = deal?.CurrencyTradeable;

            if (currency != null)
            {
                bool isFavorTrade = cachedTrader?.TradeCurrency == TradeCurrency.Favor;
                if (currentCategory == TradeCategory.TraderItems)
                {
                    // Their Items tab - announce trader's currency (projected balance after pending trade)
                    int traderCurrency = currency.CountPostDealFor(Transactor.Trader);
                    string currencyStr = isFavorTrade ? $"{traderCurrency} favor" : ((float)traderCurrency).ToStringMoney();
                    announcement += $". Trader has {currencyStr}";
                }
                else if (currentCategory == TradeCategory.ColonyItems)
                {
                    // Your Items tab - announce player's currency (projected balance after pending trade)
                    int playerCurrency = currency.CountPostDealFor(Transactor.Colony);
                    string currencyStr = isFavorTrade ? $"{playerCurrency} favor" : ((float)playerCurrency).ToStringMoney();
                    announcement += $". You have {currencyStr}";
                }
            }

            TolkHelper.Speak(announcement);

            if (list.Count > 0)
            {
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Announces the currently selected item or balance entry.
        /// </summary>
        private static void AnnounceCurrentSelection()
        {
            // Check if we're on the balance entry in Trade Summary
            if (IsOnBalanceEntry())
            {
                AnnounceBalanceEntry();
                return;
            }

            Tradeable tradeable = GetCurrentTradeable();
            if (tradeable == null)
            {
                List<Tradeable> list = GetCurrentList();
                string categoryName = GetCategoryName();
                TolkHelper.Speak($"{categoryName} - No items");
                return;
            }

            string announcement = BuildTradeableAnnouncement(tradeable);
            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Announces the balance entry at the end of Trade Summary.
        /// In normal trade: "Net balance: Spending $50" or "Receiving $100"
        /// In gift mode: "Goodwill +15"
        /// </summary>
        private static void AnnounceBalanceEntry()
        {
            int total = tradeSummaryList.Count + 1;    // Items + balance entry
            string posStr = MenuHelper.FormatPosition(tradeSummaryList.Count, total); // 0-indexed position

            // Use TradeSession.deal directly for freshest data
            TradeDeal deal = TradeSession.deal ?? cachedDeal;

            if (TradeSession.giftMode)
            {
                // Gift mode: show goodwill gain
                if (cachedTrader?.Faction != null && deal != null)
                {
                    int goodwillChange = FactionGiftUtility.GetGoodwillChange(
                        deal.AllTradeables.ToList(),
                        cachedTrader.Faction);
                    string announcement = $"Goodwill +{goodwillChange}";
                    if (!string.IsNullOrEmpty(posStr))
                        announcement += $". {posStr}";
                    TolkHelper.Speak(announcement);
                }
                else
                {
                    string announcement = "Gift mode balance";
                    if (!string.IsNullOrEmpty(posStr))
                        announcement += $". {posStr}";
                    TolkHelper.Speak(announcement);
                }
            }
            else
            {
                // Normal trade: ensure currency is up-to-date before reading
                deal?.UpdateCurrencyCount();
                Tradeable currency = deal?.CurrencyTradeable;
                string currencyName = cachedTrader?.TradeCurrency == TradeCurrency.Favor ? "favor" : "silver";
                int transfer = currency?.CountToTransfer ?? 0;

                string balanceText;
                if (transfer < 0)
                {
                    balanceText = $"Net balance: Spending {-transfer} {currencyName}";
                }
                else if (transfer > 0)
                {
                    balanceText = $"Net balance: Receiving {transfer} {currencyName}";
                }
                else
                {
                    balanceText = "Net balance: Balanced trade";
                }

                if (!string.IsNullOrEmpty(posStr))
                    balanceText += $". {posStr}";
                TolkHelper.Speak(balanceText);
            }
        }

        /// <summary>
        /// Announces a quantity change.
        /// </summary>
        private static void AnnounceQuantityChange(Tradeable tradeable)
        {
            string action = tradeable.ActionToDo == TradeAction.PlayerBuys ? "Buying" :
                           (TradeSession.giftMode ? "Gifting" : "Selling");

            int count = Math.Abs(tradeable.CountToTransfer);

            if (tradeable.ActionToDo == TradeAction.None)
            {
                TolkHelper.Speak($"{GetCleanLabel(tradeable)}: No trade");
            }
            else
            {
                // Show price for both trade and gift mode (vanilla game shows prices in gift mode)
                float totalCost = Math.Abs(tradeable.CurTotalCurrencyCostForDestination);
                TolkHelper.Speak($"{action} {count} {GetCleanLabel(tradeable)}, value {FormatPrice(totalCost)}");
            }
        }

        /// <summary>
        /// Builds the announcement string for a tradeable.
        /// Tab-specific format for screen reader clarity.
        /// Shared items show both buy and sell info.
        /// </summary>
        private static string BuildTradeableAnnouncement(Tradeable tradeable)
        {
            List<Tradeable> list = GetCurrentList();

            int colonyCount = tradeable.CountHeldBy(Transactor.Colony);
            int traderCount = tradeable.CountHeldBy(Transactor.Trader);
            bool isShared = colonyCount > 0 && traderCount > 0;

            // If there's an active trade on this item, show that prominently
            if (tradeable.CountToTransfer != 0)
            {
                string action = tradeable.ActionToDo == TradeAction.PlayerBuys ? "Buying" :
                               (TradeSession.giftMode ? "Gifting" : "Selling");
                int count = Math.Abs(tradeable.CountToTransfer);

                // Trade Summary includes balance entry at the end
                int totalItems = currentCategory == TradeCategory.TradeSummary ? list.Count + 1 : list.Count;
                string position = MenuHelper.FormatPosition(currentIndex, totalItems);

                var parts = new List<string> { GetCleanLabel(tradeable) };
                parts.Add($"{action} {count}");
                // Show price in both trade and gift mode (vanilla game shows prices in gift mode)
                float totalCost = Math.Abs(tradeable.CurTotalCurrencyCostForDestination);
                string priceLabel = TradeSession.giftMode ? "Value" : "Total";
                parts.Add($"{priceLabel}: {FormatPrice(totalCost)}");
                // Position at end
                if (!string.IsNullOrEmpty(position))
                    parts.Add(position);
                return string.Join(". ", parts) + ".";
            }

            // In quantity mode, show what's available for adjustment
            if (isInQuantityMode)
            {
                return BuildQuantityModeAnnouncement(tradeable, colonyCount, traderCount);
            }

            // Standard navigation: tab-specific format
            string pos = MenuHelper.FormatPosition(currentIndex, list.Count);
            var info = new List<string> { GetCleanLabel(tradeable) };
            // Position will be added at end, not here

            switch (currentCategory)
            {
                case TradeCategory.ColonyItems:
                    // Your Items tab
                    if (isShared)
                    {
                        // Shared item: "Steel. Yours: 20 at $2.25. Theirs: 40 at $4.33."
                        if (tradeable.TraderWillTrade)
                        {
                            float sellPrice = tradeable.GetPriceFor(TradeAction.PlayerSells);
                            float buyPrice = tradeable.GetPriceFor(TradeAction.PlayerBuys);
                            string sellQuality = GetPriceQuality(tradeable, TradeAction.PlayerSells);
                            string buyQuality = GetPriceQuality(tradeable, TradeAction.PlayerBuys);

                            string yourPart = $"Yours: {colonyCount} at {FormatPrice(sellPrice)}";
                            if (!string.IsNullOrEmpty(sellQuality))
                                yourPart += $" ({sellQuality})";

                            string theirPart = $"Theirs: {traderCount} at {FormatPrice(buyPrice)}";
                            if (!string.IsNullOrEmpty(buyQuality))
                                theirPart += $" ({buyQuality})";

                            info.Add(yourPart);
                            info.Add(theirPart);
                        }
                        else
                        {
                            info.Add($"Yours: {colonyCount}");
                            info.Add($"Theirs: {traderCount}");
                            info.Add("Not tradeable");
                        }
                    }
                    else
                    {
                        // Only you have it: "Steel. 20 available. $2.25."
                        info.Add($"{colonyCount} available");
                        if (tradeable.TraderWillTrade)
                        {
                            float sellPrice = tradeable.GetPriceFor(TradeAction.PlayerSells);
                            string quality = GetPriceQuality(tradeable, TradeAction.PlayerSells);
                            string priceStr = FormatPrice(sellPrice);
                            if (!string.IsNullOrEmpty(quality))
                                priceStr += $", {quality}";
                            info.Add(priceStr);
                        }
                        else
                        {
                            info.Add("Not tradeable");
                        }
                    }
                    break;

                case TradeCategory.TraderItems:
                    // Their Items tab
                    if (isShared)
                    {
                        // Shared item: "Steel. Theirs: 40 at $4.33. Yours: 20 at $2.25."
                        if (tradeable.TraderWillTrade)
                        {
                            float sellPrice = tradeable.GetPriceFor(TradeAction.PlayerSells);
                            float buyPrice = tradeable.GetPriceFor(TradeAction.PlayerBuys);
                            string sellQuality = GetPriceQuality(tradeable, TradeAction.PlayerSells);
                            string buyQuality = GetPriceQuality(tradeable, TradeAction.PlayerBuys);

                            string theirPart = $"Theirs: {traderCount} at {FormatPrice(buyPrice)}";
                            if (!string.IsNullOrEmpty(buyQuality))
                                theirPart += $" ({buyQuality})";

                            string yourPart = $"Yours: {colonyCount} at {FormatPrice(sellPrice)}";
                            if (!string.IsNullOrEmpty(sellQuality))
                                yourPart += $" ({sellQuality})";

                            info.Add(theirPart);
                            info.Add(yourPart);
                        }
                        else
                        {
                            info.Add($"Theirs: {traderCount}");
                            info.Add($"Yours: {colonyCount}");
                            info.Add("Not tradeable");
                        }
                    }
                    else
                    {
                        // Only they have it: "Jade knife. 1 available. $250."
                        info.Add($"{traderCount} available");
                        if (tradeable.TraderWillTrade)
                        {
                            float buyPrice = tradeable.GetPriceFor(TradeAction.PlayerBuys);
                            string quality = GetPriceQuality(tradeable, TradeAction.PlayerBuys);
                            string priceStr = FormatPrice(buyPrice);
                            if (!string.IsNullOrEmpty(quality))
                                priceStr += $", {quality}";
                            info.Add(priceStr);
                        }
                        else
                        {
                            info.Add("Not tradeable");
                        }
                    }
                    break;
            }

            // Item description after price info
            string description = GetItemDescription(tradeable);
            if (!string.IsNullOrEmpty(description))
                info.Add(description);

            // Position at end
            if (!string.IsNullOrEmpty(pos))
                info.Add(pos);

            return string.Join(". ", info) + ".";
        }

        /// <summary>
        /// Builds announcement for quantity adjustment mode.
        /// For shared items, shows both buy and sell options.
        /// Gift mode: only gifting (no buying from trader).
        /// </summary>
        private static string BuildQuantityModeAnnouncement(Tradeable tradeable, int colonyCount, int traderCount)
        {
            var parts = new List<string> { GetCleanLabel(tradeable), "Adjusting" };
            bool isShared = colonyCount > 0 && traderCount > 0;

            // Gift mode: can only gift your items
            if (TradeSession.giftMode)
            {
                parts.Add($"Gift up to {colonyCount}");
            }
            else if (isShared)
            {
                // Shared item: can buy or sell
                parts.Add($"Sell up to {colonyCount}, buy up to {traderCount}");
            }
            else if (currentCategory == TradeCategory.ColonyItems)
            {
                // Only you have it - selling context
                parts.Add($"Sell up to {colonyCount}");
            }
            else
            {
                // Only they have it - buying context
                parts.Add($"Buy up to {traderCount}");
            }

            return string.Join(". ", parts) + ".";
        }

        /// <summary>
        /// Builds a detailed announcement for when Tab is pressed.
        /// Shows full inventory and price information.
        /// </summary>
        private static string BuildDetailedAnnouncement(Tradeable tradeable)
        {
            var parts = new List<string> { GetCleanLabel(tradeable) };
            string currencyName = cachedTrader?.TradeCurrency == TradeCurrency.Favor ? "favor" : "silver";

            int colonyCount = tradeable.CountHeldBy(Transactor.Colony);
            int traderCount = tradeable.CountHeldBy(Transactor.Trader);

            if (colonyCount > 0)
            {
                parts.Add($"You have: {colonyCount}");
                if (tradeable.TraderWillTrade)
                {
                    float sellPrice = tradeable.GetPriceFor(TradeAction.PlayerSells);
                    parts.Add($"Sell price: {sellPrice:F1} {currencyName} each");
                }
            }

            if (traderCount > 0)
            {
                parts.Add($"Trader has: {traderCount}");
                if (tradeable.TraderWillTrade)
                {
                    float buyPrice = tradeable.GetPriceFor(TradeAction.PlayerBuys);
                    parts.Add($"Buy price: {buyPrice:F1} {currencyName} each");
                }
            }

            if (tradeable.CountToTransfer != 0)
            {
                string action = tradeable.ActionToDo == TradeAction.PlayerBuys ? "Buying" : "Selling";
                int count = Math.Abs(tradeable.CountToTransfer);
                float totalCost = Math.Abs(tradeable.CurTotalCurrencyCostForDestination);
                parts.Add($"Current trade: {action} {count} for {totalCost:F0} {currencyName}");
            }

            if (!tradeable.TraderWillTrade)
            {
                parts.Add("Trader will not trade this item");
            }

            return string.Join(". ", parts) + ".";
        }

        /// <summary>
        /// Announces detailed item info (called on Tab press for brief details).
        /// </summary>
        public static void AnnounceDetailedInfo()
        {
            Tradeable tradeable = GetCurrentTradeable();
            if (!GuardHelper.RequireItem(tradeable)) return;

            string announcement = BuildDetailedAnnouncement(tradeable);
            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Shows price breakdown using StatBreakdownState (called on Tab press).
        /// Displays the same tooltip info that sighted players see when hovering over prices.
        /// </summary>
        public static void ShowPriceBreakdown()
        {
            Tradeable tradeable = GetCurrentTradeable();
            if (!GuardHelper.RequireItem(tradeable)) return;

            if (!tradeable.TraderWillTrade)
            {
                TolkHelper.Speak("Trader will not trade this item");
                return;
            }

            // Determine which price tooltip to show based on category
            TradeAction action;
            string title;

            if (currentCategory == TradeCategory.TraderItems)
            {
                // In trader's inventory - show buy price breakdown
                action = TradeAction.PlayerBuys;
                title = $"Buy Price: {GetCleanLabel(tradeable)}";
            }
            else
            {
                // In colony inventory or currency - show sell price breakdown
                action = TradeAction.PlayerSells;
                title = $"Sell Price: {GetCleanLabel(tradeable)}";
            }

            // In gift mode, price breakdown isn't relevant
            if (TradeSession.giftMode)
            {
                TolkHelper.Speak("Price breakdown not available in gift mode");
                return;
            }

            string tooltip = tradeable.GetPriceTooltip(action);
            if (string.IsNullOrEmpty(tooltip))
            {
                TolkHelper.Speak("No price information available");
                return;
            }

            // Strip the description header ("Price you pay to buy this." or "Price you receive...")
            // since we already have a title. The header is followed by \n\n before the breakdown.
            int breakdownStart = tooltip.IndexOf("\n\n");
            if (breakdownStart >= 0)
            {
                tooltip = tooltip.Substring(breakdownStart + 2);
            }

            // Use StatBreakdownState to display the tooltip in a navigable format
            StatBreakdownState.Open(title, tooltip);
        }

        /// <summary>
        /// Inspects the current item using Dialog_InfoCard (called on Alt+I).
        /// Opens the same info card that sighted players get via the "i" button.
        /// </summary>
        public static void InspectCurrentItem()
        {
            Tradeable tradeable = GetCurrentTradeable();
            if (!GuardHelper.RequireItem(tradeable)) return;

            Thing thing = tradeable.AnyThing;
            if (thing == null)
            {
                TolkHelper.Speak("Cannot inspect this item");
                return;
            }

            // Open the InfoCard dialog - InfoCardPatch will handle accessibility
            Dialog_InfoCard infoCard = new Dialog_InfoCard(thing);
            Find.WindowStack.Add(infoCard);
        }

        /// <summary>
        /// Formats a price to match RimWorld's display, including currency.
        /// For silver: "$421" (matches game's ToStringMoney)
        /// For favor: "5 favor"
        /// Values >= 10 or == 0 show as whole numbers, others show 2 decimals.
        /// </summary>
        private static string FormatPrice(float price)
        {
            // Match RimWorld's ToStringMoney logic: F0 for >= 10 or 0, F2 otherwise
            string format = (price >= 10f || price == 0f) ? "F0" : "F2";
            string number = price.ToString(format);

            // Silver uses "$" prefix (ToStringMoney), favor says "favor"
            bool isSilver = cachedTrader?.TradeCurrency != TradeCurrency.Favor;
            return isSilver ? "$" + number : number + " favor";
        }

        // Regex to match rich text tags like <color=#FF0000>...</color>, <b>, </b>, etc.
        private static readonly Regex RichTextTagRegex = new Regex(@"<[^>]+>", RegexOptions.Compiled);

        /// <summary>
        /// Strips rich text tags from a string.
        /// RimWorld uses tags like <color=#...>text</color> for colored text (e.g., prisoner names).
        /// </summary>
        private static string StripRichText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            return RichTextTagRegex.Replace(text, "");
        }

        /// <summary>
        /// Gets a clean label for a tradeable, stripped of rich text tags.
        /// For grouped pawns (animals with numerical names), returns label with gender/life stage.
        /// </summary>
        private static string GetCleanLabel(Tradeable tradeable)
        {
            if (tradeable == null)
                return "";

            // Check if this is a grouped pawn (multiple animals in one tradeable)
            if (tradeable.AnyThing is Pawn pawn)
            {
                int totalCount = tradeable.CountHeldBy(Transactor.Colony) + tradeable.CountHeldBy(Transactor.Trader);
                if (totalCount > 1)
                {
                    return PawnLabelHelper.BuildGroupedPawnLabel(pawn, totalCount);
                }
            }

            return StripRichText(tradeable.Label ?? "");
        }

        /// <summary>
        /// Gets the item's description from its ThingDef.
        /// Returns empty string if no description available.
        /// </summary>
        private static string GetItemDescription(Tradeable tradeable)
        {
            if (tradeable?.ThingDef?.description == null)
                return "";

            string desc = tradeable.ThingDef.description.Trim();
            // Remove any rich text tags
            desc = StripRichText(desc);
            // Remove trailing period since we add our own when joining
            if (desc.EndsWith("."))
                desc = desc.Substring(0, desc.Length - 1);
            return desc;
        }

        /// <summary>
        /// Gets a price quality descriptor based on RimWorld's PriceType.
        /// Returns empty string for normal prices.
        /// </summary>
        private static string GetPriceQuality(Tradeable tradeable, TradeAction action)
        {
            if (!tradeable.TraderWillTrade)
                return "";

            PriceType priceType = tradeable.PriceTypeFor(action);

            // For buying: cheap = good, expensive = bad
            // For selling: expensive = good (you get more), cheap = bad
            if (action == TradeAction.PlayerBuys)
            {
                switch (priceType)
                {
                    case PriceType.VeryCheap:
                        return "great deal";
                    case PriceType.Cheap:
                        return "good deal";
                    case PriceType.Expensive:
                        return "pricey";
                    case PriceType.Exorbitant:
                        return "very expensive";
                    default:
                        return "";
                }
            }
            else // PlayerSells
            {
                switch (priceType)
                {
                    case PriceType.VeryCheap:
                        return "poor offer";
                    case PriceType.Cheap:
                        return "low offer";
                    case PriceType.Expensive:
                        return "good offer";
                    case PriceType.Exorbitant:
                        return "great offer";
                    default:
                        return "";
                }
            }
        }

        /// <summary>
        /// Gets the name of the current category.
        /// Uses trader's name for their inventory tab.
        /// </summary>
        private static string GetCategoryName()
        {
            switch (currentCategory)
            {
                case TradeCategory.ColonyItems:
                    return "Your Items";
                case TradeCategory.TraderItems:
                    string traderName = cachedTrader?.TraderName ?? "Trader";
                    return $"{traderName}'s Items";
                case TradeCategory.TradeSummary:
                    return "Trade Summary";
                default:
                    return "Unknown";
            }
        }

        /// <summary>
        /// Announces both player's and trader's current currency holdings.
        /// Format: "You have $250. Trader has $500."
        /// </summary>
        public static void AnnounceTradeBalance()
        {
            // Use TradeSession.deal directly for freshest data
            TradeDeal deal = TradeSession.deal ?? cachedDeal;
            if (deal == null)
                return;

            // Ensure currency count is up-to-date before reading
            deal.UpdateCurrencyCount();

            Tradeable currency = deal.CurrencyTradeable;
            if (currency == null)
            {
                // Gift mode or no currency tradeable
                TolkHelper.Speak("No currency available");
                return;
            }

            int playerCurrency = currency.CountPostDealFor(Transactor.Colony);
            int traderCurrency = currency.CountPostDealFor(Transactor.Trader);

            bool isFavorTrade = cachedTrader?.TradeCurrency == TradeCurrency.Favor;
            string playerStr = isFavorTrade ? $"{playerCurrency} favor" : ((float)playerCurrency).ToStringMoney();
            string traderStr = isFavorTrade ? $"{traderCurrency} favor" : ((float)traderCurrency).ToStringMoney();

            string announcement = $"You have {playerStr}. Trader has {traderStr}";
            TolkHelper.Speak(announcement);
        }

        #region Typeahead Search Methods

        /// <summary>
        /// Gets the list of labels for items in the current category for typeahead search.
        /// </summary>
        private static List<string> GetItemLabels()
        {
            List<Tradeable> list = GetCurrentList();
            List<string> labels = new List<string>();
            foreach (Tradeable tradeable in list)
            {
                labels.Add(GetCleanLabel(tradeable));
            }
            return labels;
        }

        /// <summary>
        /// <summary>
        /// Handles typeahead character input from the layout-aware dispatcher.
        /// In quantity mode, digits go to numeric input; otherwise they typeahead-search.
        /// Letters always typeahead-search.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive) return;

            if (isInQuantityMode && char.IsDigit(c))
            {
                HandleNumericInput(c);
                return;
            }
            ProcessTypeaheadCharacter(c);
        }

        /// Processes a typeahead character input.
        /// </summary>
        /// <param name="c">The character typed</param>
        public static void ProcessTypeaheadCharacter(char c)
        {
            if (isInQuantityMode)
                return;

            List<string> labels = GetItemLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    currentIndex = newIndex;
                    AnnounceWithSearch();
                }
            }
            else
            {
                typeahead.SpeakNoMatches();
            }
        }

        /// <summary>
        /// Processes backspace for typeahead search.
        /// </summary>
        /// <returns>True if backspace was handled</returns>
        public static bool ProcessBackspace()
        {
            if (!typeahead.HasActiveSearch)
                return false;

            List<string> labels = GetItemLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    currentIndex = newIndex;
                }
                AnnounceWithSearch();
            }
            return true;
        }

        /// <summary>
        /// Clears the typeahead search and announces it.
        /// </summary>
        /// <returns>True if there was an active search to clear</returns>
        public static bool ClearTypeaheadSearch()
        {
            return typeahead.ClearSearchAndAnnounce();
        }

        /// <summary>
        /// Jumps to the first item in the current category.
        /// </summary>
        public static void JumpToFirst()
        {
            if (isInQuantityMode)
            {
                TolkHelper.Speak("Exit quantity mode first");
                return;
            }

            List<Tradeable> list = GetCurrentList();
            if (list.Count == 0)
            {
                TolkHelper.Speak("No items in this category");
                return;
            }

            currentIndex = MenuHelper.JumpToFirst();
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Jumps to the last item in the current category.
        /// </summary>
        public static void JumpToLast()
        {
            if (isInQuantityMode)
            {
                TolkHelper.Speak("Exit quantity mode first");
                return;
            }

            List<Tradeable> list = GetCurrentList();
            if (list.Count == 0)
            {
                TolkHelper.Speak("No items in this category");
                return;
            }

            // Trade Summary's "last" position is the balance entry at list.Count
            if (currentCategory == TradeCategory.TradeSummary)
            {
                currentIndex = list.Count; // Balance entry position
            }
            else
            {
                currentIndex = MenuHelper.JumpToLast(list.Count);
            }
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Selects the next match in the typeahead search results.
        /// </summary>
        public static void SelectNextMatch()
        {
            if (!typeahead.HasActiveSearch || typeahead.HasNoMatches)
                return;

            int nextIndex = typeahead.GetNextMatch(currentIndex);
            if (nextIndex >= 0)
            {
                currentIndex = nextIndex;
                AnnounceWithSearch();
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
        }

        /// <summary>
        /// Selects the previous match in the typeahead search results.
        /// </summary>
        public static void SelectPreviousMatch()
        {
            if (!typeahead.HasActiveSearch || typeahead.HasNoMatches)
                return;

            int prevIndex = typeahead.GetPreviousMatch(currentIndex);
            if (prevIndex >= 0)
            {
                currentIndex = prevIndex;
                AnnounceWithSearch();
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
        }

        /// <summary>
        /// Announces the current selection with search context if applicable.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            Tradeable tradeable = GetCurrentTradeable();
            if (!GuardHelper.RequireItem(tradeable)) return;

            string baseAnnouncement = BuildTradeableAnnouncement(tradeable);

            if (typeahead.HasActiveSearch)
            {
                TolkHelper.Speak(baseAnnouncement + typeahead.BuildSearchContextSuffix());
            }
            else
            {
                TolkHelper.Speak(baseAnnouncement);
            }
        }

        #endregion
    }
}
