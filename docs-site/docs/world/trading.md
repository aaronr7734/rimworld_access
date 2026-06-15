# Trading

Trading turns surplus goods into silver, and silver into things you need. In the trade window you browse goods, set quantities, check the running balance, and accept the deal.

For the game-mechanics side (which trader buys what, how prices and markup work, what is worth selling), see the [RimWorld wiki's Trading article](https://rimworldwiki.com/wiki/Trading) and [Trade price improvement](https://rimworldwiki.com/wiki/Trade_price_improvement).

## How trading starts

You never open the trade window directly. A colonist has to physically reach the other party first.

### A trader visits your colony

Caravans from other factions sometimes wander onto your map. Select one of your colonists, then press **`]`** on the lead trader to open the [context menu](../concepts/context-menu.md) and choose Trade. Your colonist walks over and the window opens when they arrive.

A higher Social skill on the colonist you send means better prices.

### You visit a settlement

Send a [caravan](caravans.md) to a settlement. With the caravan selected on the [world map](world-map.md), press **`]`** on the settlement's tile. The orders there include traveling to the tile to visit and trade, and (for a hostile target) attacking. Choose the trade option, and the trade window opens when the caravan arrives.

Before making the trip, you can preview what a settlement is willing to buy. Navigate to the settlement on the world map, press **G** to open its [gizmos](../concepts/gizmos.md), and choose Show Sellable Items. See [Previewing what a settlement will buy](#previewing-what-a-settlement-will-buy) below.

### An orbital trade ship calls in

Build a comms console and an orbital trade beacon (both require the Microelectronics research), and trade ships passing overhead become reachable. When a ship is in range, send a colonist to the comms console: press **`]`** on the console and pick the ship. Only items within range of a beacon can be sold this way; anything you buy drops in by pod onto the beacon's open tiles. See the wiki's [Comms console article](https://rimworldwiki.com/wiki/Comms_console) for details.

## Inside the trade window

When the window opens, the mod announces who you are dealing with and the two keys you will reach for most, for example "Trading with Bob's Caravan (bulk goods trader). Alt+B for balance, Alt+A to accept." The game pauses while you trade.

The window is organized into tabs. Move between them with **Left** and **Right**:

- **The trader's items** (leftmost tab, named after the trader): what they have for sale.
- **Trade Summary**: a running list of everything queued so far, plus a balance line. This tab only appears once you have queued at least one item.
- **Your items**: what your colony or caravan can sell.

Each tab remembers your cursor position when you switch away and back.

### Browsing the goods

**Up** and **Down** move through the list. **Home** jumps to the first item; **End** jumps to the last. Start typing to jump to an item by name (typeahead works here as elsewhere); **Backspace** edits the search and **Escape** clears it.

Each item is announced with its name, quantity available, and price. Items both sides carry show both prices at once so you can compare directions.

The mod does not yet tell you whether a given price is a good deal or a bad one. In the visual window, RimWorld colors favorable prices and unfavorable ones differently, and that signal is not announced today. To judge a price for now, open the price breakdown described below, or check the wiki for typical market values.

### Reading prices and your silver

Silver is the currency for almost all trades. (Royal tribute collectors trade for [honor instead](https://rimworldwiki.com/wiki/Trading); the mod says "favor" in place of silver in that case.)

To hear how much silver each side has, press **Alt+B**: "You have X silver. Trader has Y silver." Worth checking before a large buy, since a trader can only pay you with the silver they are carrying.

For the full reason behind a single item's price, put the cursor on the item and press **Tab**. That opens a navigable price breakdown showing the market value, trader markup, your colonist's Social bonus, and so on. **Alt+P** also opens the same breakdown. To open the full [info card](../concepts/info-card.md) for an item, press **Alt+I**.

## Buying and selling

Each item starts at a quantity of zero. Negative quantities mean you are selling; positive quantities mean you are buying. The announcements say "Buying 10" or "Selling 5" plainly.

Press **Enter** to drop into quantity mode for the current item. In quantity mode:

- **Up** and **Down** change the amount by one.
- **Shift+Up / Shift+Down** change it by ten.
- **Ctrl+Up / Ctrl+Down** change it by a hundred.
- Type a number directly to buy that many. To sell, press **-** first, then type the quantity. **Backspace** fixes a typo.
- **Home** sets the maximum sell amount; **End** sets the maximum buy amount.
- **Enter** or **Escape** exits quantity mode and returns to the list.

Without entering quantity mode, **-** decreases and **+** (or **=**) increases the current item's amount by one. The mod reads back the running total as you adjust, for example "Buying 10 Steel, value 43 silver."

To zero out an item, press **Delete** or **Alt+R**. To wipe every pending trade and start clean, press **Shift+Alt+R**.

### Checking the running deal

Once you have queued at least one item, switch to the Trade Summary tab to see the whole deal: what you are buying, what you are selling, and a balance line at the bottom. The balance reads "Net balance: Spending 50 silver," "Net balance: Receiving 100 silver," or "Net balance: Balanced trade." If you remove the last queued item, this tab empties and the mod returns you to the previous tab.

## Completing the trade

Press **Alt+A** to accept. The mod confirms with "Trade completed successfully," your colonist hands over the goods, and anything you bought is delivered. You can also close the window and return to the same trader later while they are still around.

If the trader does not have enough silver to cover what you are selling, the mod warns you and asks for confirmation before proceeding. A trader can only pay with the silver they are carrying. They pay what they can, and you do not receive goods to make up the difference, so check their silver with **Alt+B** before a large sale.

To leave without trading, press **Escape**. (If you are in quantity mode, the first Escape exits quantity mode; the second closes the window.) The mod announces "Trade cancelled."

### Giving gifts

Press **Alt+G** to toggle gift mode. In gift mode you hand items over for goodwill rather than silver. The balance line shows the goodwill you will gain, for example "Goodwill +15." You cannot gift to a hostile faction or while trading for royal favor, and the mod will tell you if you try.

## Previewing what a settlement will buy

Before sending a caravan on a long trip, you can check what a settlement wants. On the [world map](world-map.md), navigate to the settlement, press **G** for its gizmos, and choose Show Sellable Items. This opens a read-only list:

- **Left / Right**: switch between category tabs.
- **Up / Down**: move through items; **Home** and **End** jump to the ends.
- Type to search by name; **Backspace** edits the search; **Escape** clears it, then closes the window.

The window also shows when the settlement last restocked (or that it has not been visited yet), so you can judge whether the trip is worth making.

## Learn more

We handle the buttons; the wiki handles the strategy. For trader types, markup rates, what sells well, calling orbital traders, and the comms console, read the [RimWorld wiki's Trading article](https://rimworldwiki.com/wiki/Trading). For price bonuses from Social skill, see [Trade price improvement](https://rimworldwiki.com/wiki/Trade_price_improvement).

Related pages:

- [Caravans](caravans.md): how you reach other settlements to trade with them
- [The context menu](../concepts/context-menu.md): the **`]`** key used to start a trade
- [The info card](../concepts/info-card.md): **Alt+I** for an item's full stats during a trade
