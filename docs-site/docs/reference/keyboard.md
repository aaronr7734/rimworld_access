# Keyboard reference

Every key the mod uses, grouped by where you use it. The same key can do different things depending on the screen you're on, so this page is organized by context. One short note on context-sensitivity appears at the end.

## Global

These work most of the time while a game is running.

| Key | What it does |
|-----|--------------|
| **Tab** | Open the Architect menu (build) |
| **F1** | Work tab |
| **F2** | Schedule tab |
| **F3** | Assign tab |
| **F4** | Animals tab, or Mechs, or a chooser if you have both |
| **F5** | Wildlife tab |
| **F6** | Research tab |
| **F7** | Quests tab |
| **F8** | Toggle the world map on or back off |
| **F9** | History tab |
| **F12** | Extra menus picker: Factions, plus Ideology if that DLC is active |
| **Space** | Pause and unpause |
| **Shift+1** | Normal game speed |
| **Shift+2** | Fast game speed |
| **Shift+3** | Superfast game speed |
| **T** | Announce the current time, date, weather, and season |
| **Alt+T** | Announce current game speed, whether the game is paused, and performance (ticks per second) |
| **?** | Open the Learning Helper (RimWorld's built-in tutorial tips) |
| **Z** | Search by name (on the map, feeds the scanner) |
| **Escape** | Cancel, close, or back out |

## On the map

These act on the tile under the cursor, or on the selected pawn.

### Moving around

| Key | What it does |
|-----|--------------|
| **Arrow keys** | Move the cursor one tile; the camera follows |
| **Ctrl+Arrow** | Jump using the current jump mode |
| **Shift+Up / Shift+Down** | Cycle the jump mode forward or backward (the cursor does not move) |
| **Shift+Left / Shift+Right** | Adjust the preset jump distance by 1 (Preset Distance mode only) |
| **Shift+Ctrl+Left / Shift+Ctrl+Right** | Adjust the preset jump distance by 10 |
| **Ctrl+G** | Open the coordinate jump dialog. Type X, then press comma or Space, then type Z. Blank field keeps the current coordinate. Prefix with `+` or `-` for a relative offset |

Two jump modes are documented here: **Preset Distance** (the default) and **Adjacent to Wall**. Cycle between them with **Shift+Up / Shift+Down**.

### Tile info (number keys)

The mod announces much of this automatically as you arrow around. These keys are on-demand spot checks for the detail you want.

| Key | What it reads |
|-----|---------------|
| **1** | Items and pawns at the cursor |
| **2** | Terrain (label, fertility, smoothness, beauty, cleanliness, path cost) |
| **3** | Harvestable things: plants (species, growth, harvestable or dying); with the right DLC and equipment, also fish and deep-scanner mineral targets |
| **4** | Light, brightness, and temperature |
| **5** | Room stats |
| **6** | Power |
| **7** | Areas this tile belongs to |

On the world map the number keys mean different things. See [World and caravans](#world-and-caravans).

### Selecting pawns

For the full walkthrough, see [Selecting pawns](../concepts/selecting-pawns.md).

| Key | What it does |
|-----|--------------|
| **Comma / Period** | Cycle to the previous or next colonist |
| **Alt+1 ... Alt+0** | Select colonist 1 through 10 |
| **Alt+Down** | Page to colonists 11-20 (then Alt+1-0 to select within that page) |
| **Alt+Left / Alt+Right** | Move through pawns on the colonist bar |
| **Ctrl+Alt+Left / Ctrl+Alt+Right** | Reorder the selected pawn along the colonist bar |
| **Alt+Space** | Multi-select (mainly for combat; multi-select breaks many gizmos that expect a single pawn) |
| **Ctrl+Shift+F1-F4** | Save the current selection as a combat group |
| **Ctrl+F1-F4** | Recall a saved combat group |
| **/** | Focus the cursor pawn on the colonist or mech bar |
| **Alt+C** | Jump the camera to the selected pawn (or open a chooser if several are selected) |

### Pawns at a glance

These prioritize whatever is under the cursor, then fall back to the pawn selected with comma/period or Alt+number.

| Key | What it reads |
|-----|---------------|
| **Alt+H** | Health |
| **Alt+M** | Mood |
| **Alt+N** | Needs |
| **Alt+G** | Gear |
| **Alt+K** | Top 3 skills |
| **Alt+P** | Skills table for all colonists |
| **Alt+B** | Read the selected pawn's combat log (10 most recent battle entries) |

Alt+H, Alt+M, Alt+N, and Alt+G work on your pawns, enemy pawns, and animals. Alt+P opens a sortable table covering every colonist.

For checking on pawns in depth, see [Checking on pawns](../concepts/checking-on-pawns.md).

### Acting on the map

| Key | What it does |
|-----|--------------|
| **Alt+F** | Un-forbid every item on the map at once |
| **Alt+A** | Assign an allowed area to the selected pawn (prioritizes the cursor) |
| **Alt+I** | Open the info card for whatever the cursor is on, or for the item referenced in a menu |
| **G** | Open the gizmo menu for the object under the cursor |
| **`]`** | Open the context menu for the tile under the cursor |
| **`[`** | Carry out the top context-menu option immediately |
| **Shift+`[`** | Queue the top option instead of replacing the current one |

### Drafting and giving orders

How you control pawns in a fight. The [combat page](../colony/combat.md) covers these in depth.

| Key | What it does |
|-----|--------------|
| **R** | Draft or undraft the selected pawn |

### Targeting a weapon or ability

After starting an attack from the gizmo menu (**G**, choose the attack, then **Enter**):

| Key | What it does |
|-----|--------------|
| **Arrow keys, then Enter** | Move the cursor to the target, Enter to fire |
| **Escape** | Cancel targeting |
| **R** | Announce distance, whether the target is in range, and line of sight |
| **T** | Announce who is caught in an area-of-effect ability at the cursor |

Targeting stays open if the target is invalid or out of range, and tells you why, so you can adjust and retry.

### Opening detail screens

| Key | What it does |
|-----|--------------|
| **Enter** | Inspect the thing under the cursor |
| **Ctrl+Alt+Enter** | Open the inspect view for the selected pawn (the one chosen on the colonist bar) |

## The scanner

The scanner steps through categories, subcategories, item types, and individual instances, then lets you jump straight to a target.

| Key | What it does |
|-----|--------------|
| **Ctrl+Page Up / Ctrl+Page Down** | Move between categories |
| **Shift+Page Up / Shift+Page Down** | Move between subcategories |
| **Page Up / Page Down** | Move between item types (ordered by distance) |
| **Alt+Page Up / Alt+Page Down** | Move between individual instances within a group |
| **Home** | Jump the cursor to the nearest tile in the current target. Press again while standing on the target to jump to its center |
| **End** | Announce distance and direction to the scanner target |
| **Alt+Home** | Toggle scanner auto-jump mode (the scanner jumps the cursor automatically when you change categories) |

Home also pairs with the search: run a **Z** search, press **Enter** to focus the scanner on the result, then **Home** to jump to it.

## Building placement

Once you have picked a designator in the Architect menu, place it on the map with these.

| Key | What it does |
|-----|--------------|
| **Space** | Set a point (first corner, then second) |
| **Shift+Space** | Remove the last placed point or blueprint at the cursor |
| **Enter** | Confirm and place the designation |
| **`=` (or `+`)** | Add another shape, keeping the blueprints already placed |
| **`-`** | Remove the last placed segment |
| **Ctrl+A** | Select all: steps from the enclosing area, to the whole map, to nothing |
| **Ctrl+Shift+A** | Step back one level of select-all scope |
| **Tab** | Switch the designator's shape (filled rectangle, empty rectangle, manual) |

The jump-mode keys from [Moving around](#moving-around) (**Ctrl+Arrow** to jump, **Shift+Left/Right** to adjust the distance) are how you extend a shape to an exact size before setting the second point.

## Menus and tables

These apply across the mod's menus, tree views, and tables.

| Key | What it does |
|-----|--------------|
| **Tab / Shift+Tab** | Move between the panels of a screen |
| **Up / Down** | Move between items or rows |
| **Left / Right** | Move between columns or categories; in a tree, collapse or expand |
| **Home / End** | Jump to the first or last item |
| **Type any text** | Type-ahead: jump to the matching entry. Works in every menu and list |
| **Enter / Space** | Select, deselect, or toggle a checkbox |
| **Alt+S** | In a table: sort by the selected column. In a dialog or filter: save, confirm, or advance |
| **Shift+8 (asterisk)** | Expand all sibling nodes at the current tree level |

### The Enter versus Alt+S rule

In a screen where **Enter** selects and deselects items, **Alt+S** is what confirms the screen and moves forward. If **Enter** does nothing special on that screen, **Enter** finishes it. See [Navigating menus](../concepts/navigating-menus.md) for the full story.

### In tables specifically

Tables typically show pawns as rows and attributes as columns. On top of the keys above:

| Key | What it does |
|-----|--------------|
| **Alt+S** | Sort by the selected column; press again to toggle descending, ascending, then clear |
| **Shift+Up / Shift+Down** | Paint the current cell's value onto the rows above or below |
| **Shift+Home / Shift+End** | Paint from the current row to the top or bottom |
| **Ctrl+Shift+Home / Ctrl+Shift+End** | Paint the column across every row |
| **`=` / `-`** | Adjust a numeric cell by one (Enter to type a number) |
| **`]`** | Open a context menu for that column, category, or cell |

## World and caravans

On the world map (**F8**), several keys take on travel-specific meanings.

### Tile info (number keys, world map)

| Key | What it reads |
|-----|---------------|
| **1** | Growing conditions: growing period, rainfall, forageability, grazing, stone types |
| **2** | Movement and travel cost: difficulty, terrain, hilliness, elevation, roads, rivers |
| **3** | Disease frequency (and pollution or haze fields with the right DLC) |
| **4** | Time zone and global coordinates |
| **5** | Regions and landmarks |

Biome is announced when you arrow onto a tile, not via a number key.

### Travel and caravans

| Key | What it does |
|-----|--------------|
| **C** | Start choosing a caravan route (add one or more waypoints) |
| **R** | Open the route planner to preview paths and travel times |
| **Space** | Set a waypoint (or re-announce the current tile during site selection) |
| **Enter** | Inspect a caravan or enter your own settlement at the cursor. During site selection, confirm settling there |
| **Comma / Period** | Cycle between your caravans |
| **Ctrl+Space** | Add or remove a caravan from a multi-selection (then merge them with the caravan's **G** gizmo) |
| **Alt+A** | Toggle auto-provision (lets the game pick supplies) |
| **Alt+S** | Send the caravan |
| **`]`** | Act on the tile: travel to, trade with, attack, settle, and so on |
| **G** | Open the selected caravan's gizmos: settle, split, rest, merge |
| **Delete** | Remove a pawn or item from the caravan |
| **F8** | Toggle the world view off and return to the colony |
| **Alt+Home** | Jump to your colony |
| **Alt+J** | Toggle world-map scanner auto-jump mode |
| **End** | Announce distance and direction to the world scanner target |
| **Alt+End** | Jump to the nearest caravan |

**Enter does not settle a tile from the in-game world map.** Settling is done via a gizmo on the caravan. See [Caravans](../world/caravans.md) and [The world map](../world/world-map.md).

## Trade window

When trading with a visiting trader, a settlement, or an orbital trader, the [trade screen](../world/trading.md) uses these. Negative quantities sell; positive quantities buy.

| Key | What it does |
|-----|--------------|
| **Up / Down** | Move through the goods, or adjust the quantity by 1 in quantity mode |
| **Shift+Up / Shift+Down** | Adjust the quantity by 10 |
| **Ctrl+Up / Ctrl+Down** | Adjust the quantity by 100 |
| **Left / Right** | Switch tabs |
| **Enter** | Enter or leave quantity mode for the current item |
| **Home / End** | Max sell or max buy in quantity mode, or jump to the first or last item |
| **Type a number** | Set a quantity (lead with `-` to sell); `+` and `-` nudge by one |
| **Alt+A** | Accept the trade |
| **Alt+B** | Announce the silver (or favor) on each side |
| **Tab** | Price breakdown for the current item |
| **Alt+I** | Info card for the current item |
| **Alt+G** | Toggle gift mode (give goods for goodwill) |
| **Delete or Alt+R** | Reset the current item; **Shift+Alt+R** resets everything |
| **Escape** | Close the trade |

## Context-dependent keys

A handful of keys do different jobs depending on where you are. This is normal behavior shared by most applications; the list below is for reference when a key does something unexpected.

- **R**: on the colony map, draft or undraft; on the world map, jump to a random tile (site selection) or open the route planner; during targeting, announce distance and range.
- **Alt+F**: on the colony map, un-forbid every item; during pawn creation, open the reroll filters.
- **Alt+A**: on the colony map, assign an allowed area; in caravan formation, toggle auto-provision; in the Quests tab, accept the quest; in a trade, accept the trade.
- **Alt+M**: on the colony map, read a pawn's mood; in the Work tab, toggle between manual and basic priority mode.
- **Alt+B**: on the colony map, read a pawn's combat log; in a trade, announce the silver/favor balance.
- **Alt+G**: on the colony map, read a pawn's gear; in a trade, toggle gift mode.
- **Alt+P**: on the colony map, open the skills table; in a trade, show the price breakdown.
- **Alt+R**: during pawn creation, reroll the current pawn; in a trade, reset the current item.
- **Alt+S**: in a table, sort by the selected column; in a dialog or filter, save or advance; in caravan formation, send the caravan.

Mac users: see [Playing on Mac](playing-on-mac.md) for how modifier keys map on macOS.
