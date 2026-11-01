using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The trade screen (<see cref="Dialog_Trade"/>) as three flat lists: the classic view, and the
    /// default. <see cref="TradeScope"/> shows the same dialog as one sortable table; Ctrl+Tab swaps
    /// views on the open dialog and the choice persists (DefaultTradeView).
    ///
    /// Regions, in vanilla's own row order (the dialog's cachedTradeables, its will-not-trade
    /// filter, sorters and search filter included): the trader's goods, the pending deal, the
    /// colony's goods. Goods both sides hold appear in both goods lists with both prices. The
    /// pending region lists every row with a non-zero count, buys before sells, and ends with the
    /// balance row: net currency in trade mode, the goodwill change in gift mode. It stays out of
    /// the Tab cycle while nothing is pending. In gift mode the currency row joins the colony's
    /// goods, as vanilla lets silver itself be gifted.
    ///
    /// Left/Right alone cycle the three goods sections, as the quest tab's strip does; Tab hops
    /// between the goods area and the toolbar, returning to the section and row it left, or to the
    /// top of the nearest section when that one has since emptied. A row speaks its name and pending action; counts, prices with vanilla's
    /// price-colour word, the running total and the description ride the Extras channel so the
    /// announcement configuration can move or silence them. Enter opens the shared quantity
    /// chooser in its step-by-one signed mode; +/- step by one in the row's primary direction,
    /// Shift and Ctrl with Up/Down by ten and a hundred, Shift+Home/End jump to the range ends.
    /// The Buttons region carries vanilla's two sort dropdowns, captured and pressed through
    /// their own click, then the declared toolbar.
    ///
    /// The ring and the auto-scroll come from <see cref="TradeRowGeometry"/>, so the row under the
    /// cursor is always the row on screen. Escape closes through <see cref="TradeNavigationState"/>
    /// exactly as the table view does.
    /// </summary>
    public sealed class TradeClassicScope : ScreenScope
    {
        private const int TheirRegion = 0;
        private const int SummaryRegion = 1;
        private const int YourRegion = 2;
        private const int RegionCount = 3;

        private static readonly AccessTools.FieldRef<Dialog_Trade, List<Tradeable>> cachedTradeablesField =
            AccessTools.FieldRefAccess<Dialog_Trade, List<Tradeable>>("cachedTradeables");
        private static readonly AccessTools.FieldRef<Dialog_Trade, Tradeable> cachedCurrencyField =
            AccessTools.FieldRefAccess<Dialog_Trade, Tradeable>("cachedCurrencyTradeable");
        private static readonly AccessTools.FieldRef<Dialog_Trade, bool> giftsOnlyField =
            AccessTools.FieldRefAccess<Dialog_Trade, bool>("giftsOnly");
        private static readonly AccessTools.FieldRef<Dialog_Trade, QuickSearchWidget> quickSearchField =
            AccessTools.FieldRefAccess<Dialog_Trade, QuickSearchWidget>("quickSearchWidget");

        private readonly Dialog_Trade dialog;
        private readonly bool swappedIn;
        private readonly List<Tradeable> theirItems = new List<Tradeable>();
        private readonly List<Tradeable> yourItems = new List<Tradeable>();
        private readonly List<Tradeable> pending = new List<Tradeable>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        /// <summary>The goods section last visited, the one Tab returns to from the toolbar.</summary>
        private int goodsRegion = TheirRegion;
        private int previousRegion = TheirRegion;

        public TradeClassicScope(Dialog_Trade dialog, bool swappedIn = false)
        {
            this.dialog = dialog;
            this.swappedIn = swappedIn;

            Claim(SharedMenuGrammar.Cancel,
                delegate { TradeNavigationState.CloseAndAnnounceCancel(); },
                when: () => !TypeaheadHasActiveSearch);

            Claim("trade.quantity.increase", delegate { AdjustCurrent(1); });
            Claim("trade.quantity.decrease", delegate { AdjustCurrent(-1); });
            Claim("trade.quantity.increaseTen", delegate { AdjustCurrent(10); });
            Claim("trade.quantity.decreaseTen", delegate { AdjustCurrent(-10); });
            Claim("trade.quantity.increaseHundred", delegate { AdjustCurrent(100); });
            Claim("trade.quantity.decreaseHundred", delegate { AdjustCurrent(-100); });
            Claim("trade.quantity.max", delegate { SetCurrentExtreme(max: true); });
            Claim("trade.quantity.min", delegate { SetCurrentExtreme(max: false); });
            Claim("trade.item.reset", delegate { ResetCurrentItem(); });
            Claim("trade.accept", delegate { TradeNavigationState.AcceptTrade(); });
            Claim("trade.resetAll", delegate { ResetAll(); });
            Claim("trade.giftMode.toggle", delegate { ToggleGiftMode(); });
            Claim("trade.priceBreakdown", delegate { ShowPriceBreakdown(); });
            Claim("trade.balance", delegate { TradeNavigationState.AnnounceTradeBalance(); });
            Claim("trade.inspect", delegate { TradeRowActions.InspectItem(CurrentTradeable()); });
            Claim("trade.showSellableItems", delegate { TradeRowActions.ShowSellableItems(); });
            Claim("trade.swapView", delegate { TradeViewOpener.SwapView(dialog); });

            // Reached because the base's Left/Right claims stand down on a flat row; the toolbar keeps the base's walk.
            Claim(SharedMenuGrammar.NextHorizontal, delegate { SwitchSection(1); }, when: InContentRegion);
            Claim(SharedMenuGrammar.PreviousHorizontal, delegate { SwitchSection(-1); }, when: InContentRegion);
        }

        public override string Name
        {
            get { return "tradeClassic"; }
        }

        public override bool IsLive
        {
            get { return TradeNavigationState.IsActive; }
        }

        /// <summary>True: TradeNavigationPatch's blocker eats vanilla's Escape close for the whole session, so this scope owns Cancel outright.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>The row's own name only: the stat tail must never match a search.</summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            if (OnBalanceRow(region, row))
            {
                return BalanceLabel();
            }
            return TradeNavigationState.GetCleanLabel(ItemAt(region, row));
        }

        /// <summary>Vanilla's own search box filters by substring, so its survivors need the substring tier too.</summary>
        protected override bool TypeaheadSubstringFallback
        {
            get
            {
                QuickSearchWidget search = dialog == null ? null : quickSearchField(dialog);
                return search != null && search.filter.Active;
            }
        }

        /// <summary>Row positions survive a Tab to the toolbar and back.</summary>
        public override bool RememberTabPositions
        {
            get { return true; }
        }

        /// <summary>Tab has two stops and Left/Right three, so no single "section x of y" fits.</summary>
        protected override bool RegionNamesCarryPosition
        {
            get { return true; }
        }

        /// <summary>Shift+Enter presses accept from anywhere; the label is vanilla's own live button text.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "trade.accept"; }
        }

        // ------------------------------------------------------------------
        // Content contract.
        // ------------------------------------------------------------------

        protected override int ContentRegionCount
        {
            get { return RegionCount; }
        }

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case TheirRegion:
                {
                    string traderName = TradeSession.trader?.TraderName;
                    if (string.IsNullOrEmpty(traderName))
                    {
                        traderName = "RimWorldAccess.Sellable.Open.TraderFallback".Translate().ToString();
                    }
                    return "RimWorldAccess.Trade.Tab.TraderItems".Translate(traderName).ToString();
                }
                case SummaryRegion:
                    return "RimWorldAccess.Trade.Tab.TradeSummary".Translate().ToString();
                default:
                    return "RimWorldAccess.Trade.Tab.YourItems".Translate().ToString();
            }
        }

        protected override int ContentItemCount(int region)
        {
            switch (region)
            {
                case TheirRegion: return theirItems.Count;
                case SummaryRegion: return pending.Count == 0 ? 0 : pending.Count + 1;
                default: return yourItems.Count;
            }
        }

        /// <summary>The pending deal is nothing to visit while nothing is pending.</summary>
        protected override bool ContentRegionAlwaysNavigable(int region)
        {
            return region != SummaryRegion;
        }

        /// <summary>Entering a goods list reads that side's projected currency, as the header above it shows.</summary>
        protected override string ContentRegionEntryDetail(int region)
        {
            if (region == SummaryRegion)
            {
                return null;
            }
            string holding = TradeNavigationState.FormattedHolding(
                region == TheirRegion ? Transactor.Trader : Transactor.Colony);
            if (holding == null)
            {
                return null;
            }
            return (region == TheirRegion
                ? "RimWorldAccess.Trade.Currency.TraderHas"
                : "RimWorldAccess.Trade.Currency.YouHave").Translate(holding).ToString();
        }

        /// <summary>
        /// Rebuilt from the dialog's own list every refresh: sorters, the search filter, gift
        /// toggles and reset all replace or reorder it. Pending rows sort buys first, then by
        /// name, so the summary reads as a receipt.
        /// </summary>
        protected override void RefreshContent()
        {
            theirItems.Clear();
            yourItems.Clear();
            pending.Clear();
            if (dialog == null)
            {
                return;
            }
            Tradeable currency = cachedCurrencyField(dialog);
            if (currency != null && currency.Interactive)
            {
                yourItems.Add(currency);
                if (currency.CountToTransfer != 0)
                {
                    pending.Add(currency);
                }
            }
            List<Tradeable> cached = cachedTradeablesField(dialog);
            if (cached != null)
            {
                for (int i = 0; i < cached.Count; i++)
                {
                    Tradeable t = cached[i];
                    if (t.CountHeldBy(Transactor.Trader) > 0)
                    {
                        theirItems.Add(t);
                    }
                    if (t.CountHeldBy(Transactor.Colony) > 0)
                    {
                        yourItems.Add(t);
                    }
                    if (t.CountToTransfer != 0)
                    {
                        pending.Add(t);
                    }
                }
            }
            pending.SortStable((a, b) =>
            {
                bool aBuying = a.ActionToDo == TradeAction.PlayerBuys;
                bool bBuying = b.ActionToDo == TradeAction.PlayerBuys;
                if (aBuying != bBuying)
                {
                    return bBuying.CompareTo(aBuying);
                }
                return string.Compare(TradeNavigationState.GetCleanLabel(a), TradeNavigationState.GetCleanLabel(b),
                    StringComparison.CurrentCultureIgnoreCase);
            });
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            // Enter opens the quantity chooser (the balance row announces the balance), so these rows own Enter and never arm the proceed confirm.
            d.KeepsAccept = true;
            if (OnBalanceRow(region, index))
            {
                d.Label = BalanceLabel();
                d.Extras = TradeNavigationState.HoldingsLine();
                return d;
            }
            Tradeable t = ItemAt(region, index);
            if (t == null)
            {
                return d;
            }
            d.Label = TradeRowActions.RowLabel(t);
            d.Extras = RowDetail(t, region);
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            TypeaheadReset();
            if (OnBalanceRow(region, index))
            {
                TradeNavigationState.AnnounceTradeBalance();
                return;
            }
            Tradeable t = ItemAt(region, index);
            if (t == null || !TradeRowActions.CanAdjust(t, announceReason: true))
            {
                return;
            }
            TradeRowActions.OpenQuantityMenu(t);
        }

        private bool InContentRegion()
        {
            RefreshModel();
            return Model.RegionIndex < RegionCount;
        }

        /// <summary>Left/Right: the next goods section holding something, wrapping.</summary>
        private void SwitchSection(int direction)
        {
            RefreshModel();
            if (RelocatedThisRefresh)
            {
                return;
            }
            int current = Model.RegionIndex;
            for (int step = 1; step < RegionCount; step++)
            {
                int target = ((current + direction * step) % RegionCount + RegionCount) % RegionCount;
                if (Model.IsRegionNavigable(target))
                {
                    LandIn(target, toTop: false);
                    return;
                }
            }
            MenuHelper.PlayEdgeTone();
        }

        /// <summary>Tab: the toolbar from any goods section, and back to the section left; one that emptied meanwhile yields to the top of its nearest neighbour.</summary>
        protected override void MoveRegion(bool forward)
        {
            RefreshModel();
            if (RelocatedThisRefresh)
            {
                return;
            }
            if (Model.RegionIndex < RegionCount)
            {
                if (!HasActionsRegion())
                {
                    MenuHelper.PlayEdgeTone();
                    return;
                }
                LandIn(ActionsRegionIndex(), toTop: false);
                return;
            }
            if (Model.IsRegionNavigable(goodsRegion))
            {
                LandIn(goodsRegion, toTop: false);
                return;
            }
            int nearest = NearestNavigableGoodsRegion(goodsRegion);
            if (nearest < 0)
            {
                MenuHelper.PlayEdgeTone();
                return;
            }
            LandIn(nearest, toTop: true);
        }

        private int NearestNavigableGoodsRegion(int from)
        {
            for (int distance = 1; distance < RegionCount; distance++)
            {
                if (from - distance >= 0 && Model.IsRegionNavigable(from - distance))
                {
                    return from - distance;
                }
                if (from + distance < RegionCount && Model.IsRegionNavigable(from + distance))
                {
                    return from + distance;
                }
            }
            return -1;
        }

        /// <summary>The landing every region move shares: the model jump, then the same sound and utterance a Tab press makes.</summary>
        private void LandIn(int target, bool toTop)
        {
            MoveResult moved = Model.MoveToRegion(target);
            if (!MenuHelper.SoundMove(moved))
            {
                return;
            }
            if (toTop && Model.CurrentRegion != null)
            {
                Model.CurrentRegion.MoveTo(0);
            }
            TypeaheadReset();
            OnRegionChanged(moved);
            TabSwitchSound?.PlayOneShotOnCamera();
            NotifyCursorSettled();
            AnnounceRegion();
        }

        /// <summary>Leaving the pending list because it emptied lands at the top of the neighbour, never on a remembered row.</summary>
        protected override void OnRegionChanged(MoveResult result)
        {
            if (previousRegion == SummaryRegion && pending.Count == 0
                && Model.RegionIndex < RegionCount && Model.CurrentRegion != null)
            {
                Model.CurrentRegion.MoveTo(0);
            }
            previousRegion = Model.RegionIndex;
        }

        // ------------------------------------------------------------------
        // Buttons: vanilla's sort dropdowns (captured) plus the declared toolbar.
        // ------------------------------------------------------------------

        protected override bool CaptureWindowButtons
        {
            get { return true; }
        }

        /// <summary>
        /// Only the two sort dropdowns survive the capture: their labels are the game's own
        /// TransferableSorterDef labels (TransferableUIUtility.DoTransferableSorters). The
        /// per-row count buttons and the bottom buttons are dropped; the toolbar declares the
        /// latter itself so their outcomes are spoken.
        /// </summary>
        protected override bool KeepCapturedButton(string rawLabel)
        {
            return IsSorterLabel(rawLabel);
        }

        protected override string CapturedButtonLabel(int captureIndex, string rawLabel)
        {
            return (captureIndex == 0
                ? "RimWorldAccess.Trade.Sort.By"
                : "RimWorldAccess.Trade.Sort.ThenBy").Translate(rawLabel).ToString();
        }

        private static bool IsSorterLabel(string rawLabel)
        {
            if (string.IsNullOrEmpty(rawLabel))
            {
                return false;
            }
            List<TransferableSorterDef> sorters = DefDatabase<TransferableSorterDef>.AllDefsListForReading;
            for (int i = 0; i < sorters.Count; i++)
            {
                string label = sorters[i].LabelCap.ToString();
                if (rawLabel == label)
                {
                    return true;
                }
                // Truncate appends an ellipsis to a label wider than the dropdown.
                if (rawLabel.EndsWith("...") && label.StartsWith(rawLabel.Substring(0, rawLabel.Length - 3)))
                {
                    return true;
                }
            }
            return false;
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                TradeRowActions.AddToolbarActions(actions, dialog, dialog != null && giftsOnlyField(dialog),
                    ResetAll, ToggleGiftMode, TradeView.Classic);
                return actions;
            }
        }

        // ------------------------------------------------------------------
        // Lifecycle and the visual cursor.
        // ------------------------------------------------------------------

        protected override string ComposeOpenAnnouncement()
        {
            if (swappedIn)
            {
                return TradeViewOpener.ViewName(TradeView.Classic);
            }
            return TradeRowActions.OpeningHeading();
        }

        /// <summary>The focused row's rect off the dialog's own layout; the quantity chooser rings the count field itself while open.</summary>
        protected internal override Rect FocusedContentRect()
        {
            if (QuantityMenuState.IsActive || Model.RegionIndex >= RegionCount)
            {
                return default(Rect);
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return default(Rect);
            }
            return TradeRowGeometry.RowRect(dialog, RowTradeable(Model.RegionIndex, region.Index));
        }

        protected override void OnCursorSettled(int region, int index)
        {
            previousRegion = region;
            if (region >= RegionCount)
            {
                return;
            }
            goodsRegion = region;
            TradeRowGeometry.ScrollIntoView(dialog, RowTradeable(region, index));
        }

        // ------------------------------------------------------------------
        // Actions on the current row.
        // ------------------------------------------------------------------

        private void AdjustCurrent(int steps)
        {
            if (OnBalanceRow(Model.RegionIndex, CurrentIndex()))
            {
                TradeNavigationState.AnnounceTradeBalance();
                return;
            }
            TradeRowActions.AdjustBy(CurrentTradeable(), steps);
        }

        private void SetCurrentExtreme(bool max)
        {
            if (OnBalanceRow(Model.RegionIndex, CurrentIndex()))
            {
                TradeNavigationState.AnnounceTradeBalance();
                return;
            }
            TradeRowActions.SetExtreme(CurrentTradeable(), max);
        }

        /// <summary>Delete: the row back to zero. In the pending list the row leaves, so the new row under the cursor is read.</summary>
        private void ResetCurrentItem()
        {
            if (OnBalanceRow(Model.RegionIndex, CurrentIndex()))
            {
                TolkHelper.Speak("RimWorldAccess.Trade.Reset.CannotResetBalance".Loc());
                return;
            }
            bool inSummary = Model.RegionIndex == SummaryRegion;
            TradeRowActions.ResetItem(CurrentTradeable());
            if (!inSummary)
            {
                return;
            }
            RefreshModel();
            if (!RelocatedThisRefresh && !Model.CurrentRegionIsEmpty)
            {
                AnnounceCurrentItem();
            }
        }

        /// <summary>Vanilla's Reset button verbatim: deal.Reset + CacheTradeables + CountToTransferChanged.</summary>
        private void ResetAll()
        {
            TradeNavigationState.ResetDeal();
            TolkHelper.Speak("RimWorldAccess.Trade.Reset.AllTrades".Loc());
            SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            RefreshModel();
            if (!RelocatedThisRefresh)
            {
                AnnounceCurrentItem();
            }
        }

        /// <summary>Gift mode can only give what the colony holds, so entering it lands on the colony's goods.</summary>
        private void ToggleGiftMode()
        {
            if (!TradeNavigationState.ToggleGiftMode())
            {
                return;
            }
            RefreshModel();
            if (RelocatedThisRefresh)
            {
                return;
            }
            if (TradeSession.giftMode && Model.RegionIndex != YourRegion && Model.IsRegionNavigable(YourRegion))
            {
                LandIn(YourRegion, toTop: false);
                return;
            }
            AnnounceCurrentItem();
        }

        /// <summary>Alt+P: the side the list offers; in the pending list, the side of the pending action.</summary>
        private void ShowPriceBreakdown()
        {
            if (OnBalanceRow(Model.RegionIndex, CurrentIndex()))
            {
                TradeNavigationState.AnnounceTradeBalance();
                return;
            }
            Tradeable t = CurrentTradeable();
            if (t == null)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoItemSelected".Loc());
                return;
            }
            TradeAction action;
            switch (Model.RegionIndex)
            {
                case TheirRegion:
                    action = TradeAction.PlayerBuys;
                    break;
                case YourRegion:
                    action = TradeAction.PlayerSells;
                    break;
                default:
                    action = t.ActionToDo == TradeAction.PlayerBuys ? TradeAction.PlayerBuys : TradeAction.PlayerSells;
                    break;
            }
            TradeNavigationState.ShowPriceBreakdown(t, action);
        }

        // ------------------------------------------------------------------
        // Rows.
        // ------------------------------------------------------------------

        private List<Tradeable> ItemsIn(int region)
        {
            switch (region)
            {
                case TheirRegion: return theirItems;
                case SummaryRegion: return pending;
                case YourRegion: return yourItems;
                default: return null;
            }
        }

        private Tradeable ItemAt(int region, int index)
        {
            List<Tradeable> items = ItemsIn(region);
            if (items == null || index < 0 || index >= items.Count)
            {
                return null;
            }
            return items[index];
        }

        private bool OnBalanceRow(int region, int index)
        {
            return region == SummaryRegion && pending.Count > 0 && index == pending.Count;
        }

        /// <summary>The tradeable a row draws as: the balance row IS vanilla's pinned currency row.</summary>
        private Tradeable RowTradeable(int region, int index)
        {
            if (OnBalanceRow(region, index))
            {
                return dialog == null ? null : cachedCurrencyField(dialog);
            }
            return ItemAt(region, index);
        }

        private int CurrentIndex()
        {
            ListModel region = Model.CurrentRegion;
            return region == null || region.IsEmpty ? -1 : region.Index;
        }

        /// <summary>The tradeable under the cursor, or null on the balance row and outside the goods lists.</summary>
        private Tradeable CurrentTradeable()
        {
            RefreshModel();
            if (Model.RegionIndex >= RegionCount)
            {
                return null;
            }
            return ItemAt(Model.RegionIndex, CurrentIndex());
        }

        /// <summary>Net currency after the deal in trade mode; the goodwill change in gift mode.</summary>
        private string BalanceLabel()
        {
            TradeDeal deal = TradeSession.deal;
            if (TradeSession.giftMode)
            {
                Faction faction = TradeSession.trader?.Faction;
                if (faction == null || deal == null)
                {
                    return "RimWorldAccess.Trade.Balance.GiftFallback".Translate().ToString();
                }
                int goodwill = FactionGiftUtility.GetGoodwillChange(deal.AllTradeables, faction);
                return "RimWorldAccess.Trade.Balance.Goodwill".Translate(goodwill.ToStringWithSign()).ToString();
            }
            deal?.UpdateCurrencyCount();
            Tradeable currency = deal?.CurrencyTradeable;
            string noun = (TradeSession.trader?.TradeCurrency == TradeCurrency.Favor
                ? "RimWorldAccess.Trade.Currency.FavorNoun"
                : "RimWorldAccess.Trade.Currency.SilverNoun").Translate();
            int transfer = currency != null ? currency.CountToTransferToSource : 0;
            if (transfer < 0)
            {
                return "RimWorldAccess.Trade.Balance.Spending".Translate(-transfer, noun).ToString();
            }
            if (transfer > 0)
            {
                return "RimWorldAccess.Trade.Balance.Receiving".Translate(transfer, noun).ToString();
            }
            return "RimWorldAccess.Trade.Balance.Balanced".Translate().ToString();
        }

        /// <summary>
        /// The row's figures in the order the list reads them: the running total of a pending row,
        /// then this side's count and price before the other side's, then the description vanilla
        /// shows on hover. Price words come from the game's own PriceType colouring.
        /// </summary>
        private static string RowDetail(Tradeable t, int region)
        {
            var parts = new List<string>();
            if (t.CountToTransfer != 0)
            {
                parts.Add(TradeRowActions.PendingTotalLine(t));
            }
            int colonyCount = t.CountHeldBy(Transactor.Colony);
            int traderCount = t.CountHeldBy(Transactor.Trader);
            if (t.IsCurrency)
            {
                parts.Add("RimWorldAccess.Trade.Item.YoursOnly".Translate(colonyCount).ToString());
            }
            else if (colonyCount > 0 && traderCount > 0)
            {
                string yours;
                string theirs;
                if (t.TraderWillTrade)
                {
                    yours = SidePhrase("RimWorldAccess.Trade.Item.YoursAt", "RimWorldAccess.Trade.Item.YoursAtWithQuality",
                        t, TradeAction.PlayerSells, colonyCount);
                    theirs = SidePhrase("RimWorldAccess.Trade.Item.TheirsAt", "RimWorldAccess.Trade.Item.TheirsAtWithQuality",
                        t, TradeAction.PlayerBuys, traderCount);
                }
                else
                {
                    yours = "RimWorldAccess.Trade.Item.YoursOnly".Translate(colonyCount).ToString();
                    theirs = "RimWorldAccess.Trade.Item.TheirsOnly".Translate(traderCount).ToString();
                }
                bool yoursFirst = region == YourRegion
                    || (region == SummaryRegion && t.ActionToDo != TradeAction.PlayerBuys);
                parts.Add(yoursFirst ? yours : theirs);
                parts.Add(yoursFirst ? theirs : yours);
                if (!t.TraderWillTrade)
                {
                    parts.Add("RimWorldAccess.Trade.Item.NotTradeable".Translate().ToString());
                }
            }
            else
            {
                bool colonyHolds = colonyCount > 0;
                if (t.TraderWillTrade)
                {
                    parts.Add(colonyHolds
                        ? SidePhrase("RimWorldAccess.Trade.Item.YoursAt", "RimWorldAccess.Trade.Item.YoursAtWithQuality",
                            t, TradeAction.PlayerSells, colonyCount)
                        : SidePhrase("RimWorldAccess.Trade.Item.TheirsAt", "RimWorldAccess.Trade.Item.TheirsAtWithQuality",
                            t, TradeAction.PlayerBuys, traderCount));
                }
                else
                {
                    parts.Add(colonyHolds
                        ? "RimWorldAccess.Trade.Item.YoursOnly".Translate(colonyCount).ToString()
                        : "RimWorldAccess.Trade.Item.TheirsOnly".Translate(traderCount).ToString());
                    parts.Add("RimWorldAccess.Trade.Item.NotTradeable".Translate().ToString());
                }
            }
            string description = t.HasAnyThing ? GizmoTextUtility.FlattenNewlines(t.TipDescription.StripTags()) : "";
            if (!string.IsNullOrEmpty(description))
            {
                parts.Add(description.TrimEnd('.'));
            }
            return string.Join(". ", parts);
        }

        /// <summary>"Yours: 20 at $2.25" or, when the game colours the price, "Yours: 20 at $2.25 (good offer)".</summary>
        private static string SidePhrase(string baseKey, string qualityKey, Tradeable t, TradeAction action, int count)
        {
            string price = TradeNavigationState.FormatPrice(t.GetPriceFor(action));
            string quality = TradeNavigationState.GetPriceQuality(t, action);
            return string.IsNullOrEmpty(quality)
                ? baseKey.Translate(count, price).ToString()
                : qualityKey.Translate(count, price, quality).ToString();
        }
    }
}
