using System;
using System.Collections.Generic;
using System.Linq;
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
        private static TradeCategory currentCategory = TradeCategory.Currency;
        private static int currentIndex = 0;
        private static bool isInQuantityMode = false;
        private static bool hasAnnouncedControls = false;

        // References to active trade session
        private static TradeDeal cachedDeal = null;
        private static ITrader cachedTrader = null;
        private static Pawn cachedNegotiator = null;

        // Cached tradeables organized by category
        private static List<Tradeable> currencyList = new List<Tradeable>();
        private static List<Tradeable> colonyItemsList = new List<Tradeable>();
        private static List<Tradeable> traderItemsList = new List<Tradeable>();

        // Filter and sort state
        private static string filterText = "";
        private static TransferableSorterDef sorter1 = null;
        private static TransferableSorterDef sorter2 = null;

        // Typeahead search
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

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
        /// Trade categories for navigation.
        /// </summary>
        private enum TradeCategory
        {
            Currency,      // Silver or Royal Favor
            ColonyItems,   // Items colony can sell
            TraderItems    // Items trader is selling
        }

        /// <summary>
        /// Opens the trade menu by intercepting an active TradeSession.
        /// </summary>
        public static void Open()
        {
            if (!TradeSession.Active)
            {
                TolkHelper.Speak("No active trade session");
                return;
            }

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
            currentCategory = TradeCategory.Currency;
            currentIndex = 0;
            isInQuantityMode = false;
            hasAnnouncedControls = false;
            filterText = "";
            typeahead.ClearSearch();

            // Build initial tradeable lists
            RefreshTradeables();

            // Announce opening with controls
            string traderName = cachedTrader.TraderName ?? "Unknown Trader";
            string traderKind = cachedTrader.TraderKind?.label ?? "trader";
            string controls = "Controls: Up/Down: Navigate | Left/Right: Switch categories | Enter: Adjust quantity | Alt+A: Accept trade | Alt+G: Toggle gift mode | Alt+B: Balance | Escape: Cancel";
            TolkHelper.Speak($"Trading with {traderName} ({traderKind}). {controls}");

            SoundDefOf.TabOpen.PlayOneShotOnCamera();

            hasAnnouncedControls = true;

            // Announce first item without controls
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Closes the trade menu without executing the trade.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            isInQuantityMode = false;
            hasAnnouncedControls = false;
            currentIndex = 0;
            currentCategory = TradeCategory.Currency;

            currencyList.Clear();
            colonyItemsList.Clear();
            traderItemsList.Clear();

            cachedDeal = null;
            cachedTrader = null;
            cachedNegotiator = null;
            filterText = "";
            typeahead.ClearSearch();

            TolkHelper.Speak("Trade cancelled");
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Refreshes the tradeable lists from the current deal and applies filtering/sorting.
        /// </summary>
        private static void RefreshTradeables()
        {
            if (cachedDeal == null)
                return;

            currencyList.Clear();
            colonyItemsList.Clear();
            traderItemsList.Clear();

            // Get currency tradeable (always first)
            Tradeable currency = cachedDeal.CurrencyTradeable;
            if (currency != null)
            {
                currencyList.Add(currency);
            }

            // Organize all other tradeables
            List<Tradeable> allTradeables = cachedDeal.AllTradeables.ToList();

            foreach (Tradeable tradeable in allTradeables)
            {
                // Skip currency (already added)
                if (tradeable.IsCurrency)
                    continue;

                // Apply filter
                if (!string.IsNullOrEmpty(filterText))
                {
                    if (!tradeable.Label.ToLower().Contains(filterText.ToLower()))
                        continue;
                }

                // Categorize by what the player can do
                int colonyCount = tradeable.CountHeldBy(Transactor.Colony);
                int traderCount = tradeable.CountHeldBy(Transactor.Trader);

                if (colonyCount > 0)
                {
                    // Colony has it - can potentially sell
                    colonyItemsList.Add(tradeable);
                }
                else if (traderCount > 0)
                {
                    // Only trader has it - can only buy
                    traderItemsList.Add(tradeable);
                }
            }

            // Sort both lists
            SortTradeableList(colonyItemsList);
            SortTradeableList(traderItemsList);

            // Clamp current index to valid range
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
                case TradeCategory.Currency:
                    return currencyList;
                case TradeCategory.ColonyItems:
                    return colonyItemsList;
                case TradeCategory.TraderItems:
                    return traderItemsList;
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
        /// </summary>
        private static void ClampCurrentIndex()
        {
            List<Tradeable> list = GetCurrentList();
            if (list.Count == 0)
                currentIndex = 0;
            else
                currentIndex = Mathf.Clamp(currentIndex, 0, list.Count - 1);
        }

        /// <summary>
        /// Moves to the next item in the current category.
        /// </summary>
        public static void SelectNext()
        {
            if (isInQuantityMode)
            {
                AdjustQuantity(1);
                return;
            }

            List<Tradeable> list = GetCurrentList();
            if (list.Count == 0)
            {
                TolkHelper.Speak("No items in this category");
                return;
            }

            currentIndex = MenuHelper.SelectNext(currentIndex, list.Count);
            AnnounceCurrentSelection();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Moves to the previous item in the current category.
        /// </summary>
        public static void SelectPrevious()
        {
            if (isInQuantityMode)
            {
                AdjustQuantity(-1);
                return;
            }

            List<Tradeable> list = GetCurrentList();
            if (list.Count == 0)
            {
                TolkHelper.Speak("No items in this category");
                return;
            }

            currentIndex = MenuHelper.SelectPrevious(currentIndex, list.Count);

            AnnounceCurrentSelection();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Switches to the next category.
        /// </summary>
        public static void NextCategory()
        {
            if (isInQuantityMode)
            {
                TolkHelper.Speak("Exit quantity mode first (press Enter or Escape)");
                return;
            }

            currentCategory = (TradeCategory)(((int)currentCategory + 1) % 3);
            currentIndex = 0;
            ClampCurrentIndex();
            typeahead.ClearSearch();

            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            AnnounceCategorySwitch();
        }

        /// <summary>
        /// Switches to the previous category.
        /// </summary>
        public static void PreviousCategory()
        {
            if (isInQuantityMode)
            {
                TolkHelper.Speak("Exit quantity mode first (press Enter or Escape)");
                return;
            }

            currentCategory = (TradeCategory)(((int)currentCategory + 2) % 3);
            currentIndex = 0;
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
                hasAnnouncedControls = false; // Reset so controls are announced next time
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentSelection();
                return;
            }

            // Otherwise, try to enter quantity mode
            Tradeable tradeable = GetCurrentTradeable();
            if (tradeable == null)
            {
                TolkHelper.Speak("No item selected");
                return;
            }

            if (!tradeable.Interactive)
            {
                TolkHelper.Speak("Cannot adjust quantity for this item", SpeechPriority.High);
                return;
            }

            if (!tradeable.TraderWillTrade)
            {
                TolkHelper.Speak("Trader will not trade this item");
                return;
            }

            isInQuantityMode = true;
            hasAnnouncedControls = false; // Reset so controls are announced when entering quantity mode
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
            hasAnnouncedControls = false; // Reset so controls are announced next time
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

            int newAmount = tradeable.CountToTransfer + delta;

            // Clamp to valid range
            int minAmount = tradeable.GetMinimumToTransfer();
            int maxAmount = tradeable.GetMaximumToTransfer();
            newAmount = Mathf.Clamp(newAmount, minAmount, maxAmount);

            if (tradeable.CanAdjustTo(newAmount))
            {
                tradeable.AdjustTo(newAmount);
                cachedDeal.UpdateCurrencyCount();
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
        /// Adjusts quantity by larger amounts (10).
        /// </summary>
        public static void AdjustQuantityLarge(int direction)
        {
            AdjustQuantity(direction * 10);
        }

        /// <summary>
        /// Adjusts quantity by very large amounts (100).
        /// </summary>
        public static void AdjustQuantityVeryLarge(int direction)
        {
            AdjustQuantity(direction * 100);
        }

        /// <summary>
        /// Sets quantity to maximum sell amount.
        /// </summary>
        public static void SetToMaximumSell()
        {
            Tradeable tradeable = GetCurrentTradeable();
            if (tradeable == null)
                return;

            int minAmount = tradeable.GetMinimumToTransfer();
            if (tradeable.CanAdjustTo(minAmount))
            {
                tradeable.AdjustTo(minAmount);
                cachedDeal.UpdateCurrencyCount();
                AnnounceQuantityChange(tradeable);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
        }

        /// <summary>
        /// Sets quantity to maximum buy amount.
        /// </summary>
        public static void SetToMaximumBuy()
        {
            Tradeable tradeable = GetCurrentTradeable();
            if (tradeable == null)
                return;

            int maxAmount = tradeable.GetMaximumToTransfer();
            if (tradeable.CanAdjustTo(maxAmount))
            {
                tradeable.AdjustTo(maxAmount);
                cachedDeal.UpdateCurrencyCount();
                AnnounceQuantityChange(tradeable);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
        }

        /// <summary>
        /// Resets the current item's quantity to zero.
        /// </summary>
        public static void ResetCurrentItem()
        {
            Tradeable tradeable = GetCurrentTradeable();
            if (tradeable == null)
            {
                TolkHelper.Speak("No item selected");
                return;
            }

            if (tradeable.CanAdjustTo(0))
            {
                tradeable.AdjustTo(0);
                cachedDeal.UpdateCurrencyCount();
                TolkHelper.Speak($"Reset {tradeable.Label}");
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Resets all trade quantities.
        /// </summary>
        public static void ResetAll()
        {
            if (cachedDeal == null)
                return;

            cachedDeal.Reset();
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
            cachedDeal.Reset();
            RefreshTradeables();

            string mode = TradeSession.giftMode ? "gift mode" : "trade mode";
            TolkHelper.Speak($"Switched to {mode}");
            SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Initiates the trade acceptance process - shows confirmation dialog.
        /// </summary>
        public static void AcceptTrade()
        {
            if (cachedDeal == null)
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

            // Build trade summary
            string tradeSummary = BuildTradeSummary();

            // Check if there's anything to trade
            if (string.IsNullOrEmpty(tradeSummary))
            {
                TolkHelper.Speak("No items to trade");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            // Open confirmation dialog
            TradeConfirmationState.Open(tradeSummary, ExecuteTradeConfirmed);
        }

        /// <summary>
        /// Actually executes the trade after confirmation.
        /// </summary>
        private static void ExecuteTradeConfirmed()
        {
            if (cachedDeal == null)
                return;

            // Try to execute the trade
            bool actuallyTraded = false;
            AcceptanceReport result = cachedDeal.TryExecute(out actuallyTraded);

            if (!result.Accepted)
            {
                TolkHelper.Speak($"Cannot complete trade: {result.Reason}", SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            if (actuallyTraded)
            {
                SoundDefOf.ExecuteTrade.PlayOneShotOnCamera();
                TolkHelper.Speak("Trade completed successfully");

                // Close the trade and session
                Close();
                TradeSession.Close();
            }
            else
            {
                TolkHelper.Speak("No items to trade");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        /// <summary>
        /// Builds a summary of the current trade showing what you get and what you lose.
        /// </summary>
        private static string BuildTradeSummary()
        {
            if (cachedDeal == null)
                return "";

            List<string> itemsGained = new List<string>();
            List<string> itemsLost = new List<string>();

            string currencyName = cachedTrader.TradeCurrency == TradeCurrency.Favor ? "favor" : "silver";
            Tradeable currency = cachedDeal.CurrencyTradeable;
            int currencyTransfer = currency?.CountToTransfer ?? 0;

            // Collect all tradeables
            foreach (Tradeable tradeable in cachedDeal.AllTradeables)
            {
                if (tradeable.IsCurrency)
                    continue; // Handle currency separately

                if (tradeable.ActionToDo == TradeAction.PlayerBuys)
                {
                    int count = Math.Abs(tradeable.CountToTransfer);
                    itemsGained.Add($"{count} {tradeable.Label}");
                }
                else if (tradeable.ActionToDo == TradeAction.PlayerSells)
                {
                    int count = Math.Abs(tradeable.CountToTransfer);
                    itemsLost.Add($"{count} {tradeable.Label}");
                }
            }

            // If nothing is being traded, return empty
            if (itemsGained.Count == 0 && itemsLost.Count == 0 && currencyTransfer == 0)
                return "";

            // Build summary
            List<string> summaryLines = new List<string>();
            summaryLines.Add("Proposed Trade:");
            summaryLines.Add("");

            // What you get
            summaryLines.Add("YOU GET:");
            if (itemsGained.Count > 0)
            {
                foreach (string item in itemsGained)
                {
                    summaryLines.Add($"  + {item}");
                }
            }
            // RimWorld's currency transfer is inverted: negative CountToTransfer means paying (spending)
            // This is because UpdateCurrencyCount() sets it to -cost when buying
            if (currencyTransfer > 0) // Positive CountToTransfer means receiving currency
            {
                summaryLines.Add($"  + {currencyTransfer} {currencyName}");
            }
            if (itemsGained.Count == 0 && currencyTransfer <= 0)
            {
                summaryLines.Add("  (nothing)");
            }

            summaryLines.Add("");

            // What you lose
            summaryLines.Add("YOU LOSE:");
            if (itemsLost.Count > 0)
            {
                foreach (string item in itemsLost)
                {
                    summaryLines.Add($"  - {item}");
                }
            }
            if (currencyTransfer < 0) // Negative CountToTransfer means spending currency
            {
                summaryLines.Add($"  - {Math.Abs(currencyTransfer)} {currencyName}");
            }
            if (itemsLost.Count == 0 && currencyTransfer >= 0)
            {
                summaryLines.Add("  (nothing)");
            }

            return string.Join("\n", summaryLines);
        }

        /// <summary>
        /// Accepts gifts (when in gift mode).
        /// </summary>
        private static void AcceptGift()
        {
            if (cachedDeal == null || cachedTrader == null)
                return;

            // Calculate goodwill change
            int goodwillChange = FactionGiftUtility.GetGoodwillChange(cachedDeal.AllTradeables.ToList(), cachedTrader.Faction);

            if (goodwillChange <= 0)
            {
                TolkHelper.Speak("No gifts to offer");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
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

            // Execute the gift
            FactionGiftUtility.GiveGift(cachedDeal.AllTradeables.ToList(), cachedTrader.Faction, lookTarget);

            SoundDefOf.ExecuteTrade.PlayOneShotOnCamera();
            TolkHelper.Speak($"Gifts offered, goodwill +{goodwillChange}");

            // Close the trade and session
            Close();
            TradeSession.Close();
        }

        /// <summary>
        /// Shows detailed price breakdown for the current item.
        /// </summary>
        public static void ShowPriceBreakdown()
        {
            Tradeable tradeable = GetCurrentTradeable();
            if (tradeable == null)
            {
                TolkHelper.Speak("No item selected");
                return;
            }

            // Get price tooltips
            string breakdown = BuildPriceBreakdown(tradeable);
            TolkHelper.Speak(breakdown);
        }

        /// <summary>
        /// Builds a detailed price breakdown string.
        /// </summary>
        private static string BuildPriceBreakdown(Tradeable tradeable)
        {
            List<string> lines = new List<string>();

            lines.Add($"{tradeable.Label} - Price Breakdown");
            lines.Add("");

            int colonyCount = tradeable.CountHeldBy(Transactor.Colony);
            int traderCount = tradeable.CountHeldBy(Transactor.Trader);

            // Sell price (if colony has it)
            if (colonyCount > 0 && tradeable.TraderWillTrade)
            {
                float sellPrice = tradeable.GetPriceFor(TradeAction.PlayerSells);
                string sellTooltip = tradeable.GetPriceTooltip(TradeAction.PlayerSells);

                lines.Add("SELLING:");
                lines.Add($"Price per unit: {sellPrice:F1} silver");
                lines.Add(sellTooltip.StripTags());
                lines.Add("");
            }

            // Buy price (if trader has it)
            if (traderCount > 0 && tradeable.TraderWillTrade)
            {
                float buyPrice = tradeable.GetPriceFor(TradeAction.PlayerBuys);
                string buyTooltip = tradeable.GetPriceTooltip(TradeAction.PlayerBuys);

                lines.Add("BUYING:");
                lines.Add($"Price per unit: {buyPrice:F1} silver");
                lines.Add(buyTooltip.StripTags());
                lines.Add("");
            }

            if (!tradeable.TraderWillTrade)
            {
                lines.Add("Trader will not trade this item");
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Announces the current category switch.
        /// </summary>
        private static void AnnounceCategorySwitch()
        {
            string categoryName = GetCategoryName();
            List<Tradeable> list = GetCurrentList();
            TolkHelper.Speak($"{categoryName} - {list.Count} items");

            if (list.Count > 0)
            {
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Announces the currently selected item.
        /// </summary>
        private static void AnnounceCurrentSelection()
        {
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
        /// Announces a quantity change.
        /// </summary>
        private static void AnnounceQuantityChange(Tradeable tradeable)
        {
            string action = tradeable.ActionToDo == TradeAction.PlayerBuys ? "Buying" :
                           tradeable.ActionToDo == TradeAction.PlayerSells ? "Selling" :
                           "No trade";

            int count = Math.Abs(tradeable.CountToTransfer);
            float totalCost = tradeable.CurTotalCurrencyCostForDestination;

            string currencyName = cachedTrader.TradeCurrency == TradeCurrency.Favor ? "favor" : "silver";

            if (tradeable.ActionToDo == TradeAction.None)
            {
                TolkHelper.Speak($"{tradeable.Label}: No trade");
            }
            else
            {
                TolkHelper.Speak($"{action} {count} {tradeable.Label} for {totalCost:F0} {currencyName}");
            }
        }

        /// <summary>
        /// Builds the announcement string for a tradeable.
        /// </summary>
        private static string BuildTradeableAnnouncement(Tradeable tradeable)
        {
            List<string> parts = new List<string>();

            // Position info
            List<Tradeable> list = GetCurrentList();
            parts.Add($"({MenuHelper.FormatPosition(currentIndex, list.Count)})");

            // Item name
            parts.Add(tradeable.Label);

            // Mode indicator
            if (isInQuantityMode)
            {
                parts.Add("[Adjusting]");
            }

            // Quantity and price info
            int colonyCount = tradeable.CountHeldBy(Transactor.Colony);
            int traderCount = tradeable.CountHeldBy(Transactor.Trader);

            string currencyName = cachedTrader.TradeCurrency == TradeCurrency.Favor ? "favor" : "silver";

            // Colony inventory and sell price
            if (colonyCount > 0)
            {
                parts.Add($"Colony: {colonyCount}");
                if (tradeable.TraderWillTrade)
                {
                    float sellPrice = tradeable.GetPriceFor(TradeAction.PlayerSells);
                    parts.Add($"Sell: {sellPrice:F1} {currencyName}");
                }
            }

            // Trader inventory and buy price
            if (traderCount > 0)
            {
                parts.Add($"Trader: {traderCount}");
                if (tradeable.TraderWillTrade)
                {
                    float buyPrice = tradeable.GetPriceFor(TradeAction.PlayerBuys);
                    parts.Add($"Buy: {buyPrice:F1} {currencyName}");
                }
            }

            // Current trade action
            if (tradeable.CountToTransfer != 0)
            {
                string action = tradeable.ActionToDo == TradeAction.PlayerBuys ? "Buying" : "Selling";
                int count = Math.Abs(tradeable.CountToTransfer);
                float totalCost = tradeable.CurTotalCurrencyCostForDestination;
                parts.Add($"Current: {action} {count} for {totalCost:F0} {currencyName}");
            }

            // Trade restrictions
            if (!tradeable.TraderWillTrade)
            {
                parts.Add("[Trader will not trade]");
            }

            // Additional controls hint (only when entering quantity mode or first time)
            if (isInQuantityMode && !hasAnnouncedControls)
            {
                parts.Add("(Up/Down: ±1, Shift: ±10, Ctrl: ±100, Alt+Up: Max sell, Alt+Down: Max buy, Enter: Confirm)");
                hasAnnouncedControls = true;
            }

            return string.Join(" | ", parts);
        }

        /// <summary>
        /// Gets the name of the current category.
        /// </summary>
        private static string GetCategoryName()
        {
            switch (currentCategory)
            {
                case TradeCategory.Currency:
                    return "Currency";
                case TradeCategory.ColonyItems:
                    return "Colony Items";
                case TradeCategory.TraderItems:
                    return "Trader Items";
                default:
                    return "Unknown";
            }
        }

        /// <summary>
        /// Announces the current trade balance.
        /// </summary>
        public static void AnnounceTradeBalance()
        {
            if (cachedDeal == null)
                return;

            Tradeable currency = cachedDeal.CurrencyTradeable;
            if (currency == null)
                return;

            int transfer = currency.CountToTransfer;
            string currencyName = cachedTrader.TradeCurrency == TradeCurrency.Favor ? "favor" : "silver";

            string balanceText;
            if (transfer > 0)
            {
                balanceText = $"Spending {transfer} {currencyName}";
            }
            else if (transfer < 0)
            {
                balanceText = $"Receiving {-transfer} {currencyName}";
            }
            else
            {
                balanceText = "Balanced trade (no currency exchange)";
            }

            TolkHelper.Speak(balanceText);
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
                labels.Add(tradeable.Label ?? "");
            }
            return labels;
        }

        /// <summary>
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
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
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

            currentIndex = MenuHelper.JumpToLast(list.Count);
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
            if (tradeable == null)
            {
                TolkHelper.Speak("No item selected");
                return;
            }

            string baseAnnouncement = BuildTradeableAnnouncement(tradeable);

            if (typeahead.HasActiveSearch)
            {
                string searchContext = $", match {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} for '{typeahead.SearchBuffer}'";
                TolkHelper.Speak(baseAnnouncement + searchContext);
            }
            else
            {
                TolkHelper.Speak(baseAnnouncement);
            }
        }

        #endregion
    }
}
