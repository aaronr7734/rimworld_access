# RimWorld Access

Screen reader accessibility for RimWorld. Uses the Tolk library to communicate with NVDA, JAWS, and other screen readers. Falls back to Windows SAPI if no screen reader is detected.

> **Note:** This mod is an early version. Errors may be present. This documentation is a rough overview—for detailed questions and clarifications, join the [Discord server](https://discord.gg/Aecaqnbr).

> **Bug Reports:** Bug reports involving mods other than RimWorld Access are currently unsupported. Please test with only Harmony and RimWorld Access enabled before reporting issues.

## Table of Contents

- [Installation](#installation)
- [Main Menu](#main-menu)
- [Map Navigation](#map-navigation)
- [Tile Information (Keys 1-7)](#tile-information-keys-1-7)
- [Time Controls](#time-controls)
- [Build & Zone Systems](#build--zone-systems)
- [Colonist Actions](#colonist-actions)
- [Work Menu (F1)](#work-menu-f1)
- [Schedule Menu (F2)](#schedule-menu-f2)
- [Assign Menu (F3)](#assign-menu-f3)
- [Animals Menu (F4)](#animals-menu-f4)
- [Scanner System](#scanner-system)
- [World Map (F8)](#world-map-f8)
- [Route Planner](#route-planner)
- [Caravans](#caravans)
- [Transport Pods](#transport-pods)
- [Colony Inventory (I)](#colony-inventory-i)
- [Trading System](#trading-system)
- [Other Shortcuts](#other-shortcuts)
- [Mod Manager](#mod-manager)

## Installation

### Step 1: Install Harmony (Required Dependency)

**Steam Users:**
1. Subscribe to Harmony on Steam Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077

**Non-Steam Users:**
1. Download the latest Harmony release from: https://github.com/pardeike/HarmonyRimWorld/releases/latest
2. Extract the Harmony folder to your RimWorld Mods directory (e.g., `C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\`)

### Step 2: Install RimWorld Access

1. Download the latest RimWorld Access release
2. Extract the `RimWorldAccess` folder to your RimWorld Mods directory (same location as Harmony above)

The folder structure should look like:
```
Mods\
├── RimWorldAccess\
│   ├── About\
│   │   └── About.xml
│   ├── Assemblies\
│   │   └── rimworld_access.dll
│   ├── Tolk.dll
│   └── nvdaControllerClient64.dll
└── Harmony\  (if installed manually)
```

### Step 3: Enable the Mods

Since RimWorld's mod menu is not accessible with a screen reader, you must manually edit the mods configuration file.

1. Close RimWorld if it is running
2. Open the ModsConfig.xml file in a text editor. The file is located at:
   `C:\Users\[YourUsername]\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\ModsConfig.xml`
   (You can also type `%APPDATA%\..\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\` in File Explorer's address bar)
3. Find the `<activeMods>` section
4. Add the following two lines at the beginning of the list, immediately after `<activeMods>`:
   ```xml
   <li>brrainz.harmony</li>
   <li>shane12300.rimworldaccess</li>
   ```
5. Save the file

**Example ModsConfig.xml after editing:**
```xml
<ModsConfigData>
  <version>1.6.4633 rev1261</version>
  <activeMods>
    <li>brrainz.harmony</li>
    <li>shane12300.rimworldaccess</li>
    <li>ludeon.rimworld</li>
    <!-- other mods and DLCs... -->
  </activeMods>
</ModsConfigData>
```

### Step 4: Launch the Game

Launch RimWorld. The mod will automatically initialize and you should hear your screen reader announce the main menu options.

## Main Menu

| Key | Action |
|-----|--------|
| Arrow Keys | Navigate menu options |
| Tab | Switch between main options and ideology presets (Ideology DLC) |
| Enter | Select menu item |

**Ideology Selection (Ideology DLC):** Full keyboard navigation with Tab to toggle between main options and preset browsing, Up/Down to navigate options.

## Map Navigation

| Key | Action |
|-----|--------|
| Arrow Keys | Move cursor one tile |
| T | Reads time, date and weather. |
| I | Open colony inventory |
| Enter | Open inspect panel |
| Escape | Open pause menu |

Tiles announce: pawns, buildings, orders (blueprints, jobs), items, plants, terrain, zone, roof status, and coordinates.

## Tile Information (Keys 1-7)

| Key | Info |
|-----|------|
| 1 | Items and pawns at cursor |
| 2 | Flooring details (terrain, beauty, path cost) |
| 3 | Plant information (species, growth, harvestable) |
| 4 | Brightness and temperature |
| 5 | Room statistics (role, owner, quality tiers for all stats) |
| 6 | Power information (status, network, generation/consumption) |
| 7 | Area information (home area, allowed areas) |


## Time Controls

| Key | Action |
|-----|--------|
| Shift+1 | Normal speed |
| Shift+2 | Fast speed (3x normal) |
| Shift+3 | Superfast speed (6x normal) |
| Space | Pause/unpause |

## Build & Zone Systems

| Key | Action |
|-----|--------|
| A | Open architect menu (select category → tool → material → place with Space) |
| Type letters | Typeahead search in architect menu (prioritizes name matches) |
| Tab | Toggle selection mode (box-selection ↔ single tile) for zones/orders |
| Space | Place building or set corners (box mode) / toggle tile (single mode) |
| Shift+Space | Cancel blueprint at cursor position |
| R | Rotate building |
| Left | Collapse to parent category (tree menus) |

### Building Placement Information

When placing buildings, helpful spatial information is announced:

- **Multi-tile buildings** announce which direction they extend from the cursor
- **Coolers** announce hot side and cold side orientation
- **Wind turbines** announce the clear area requirements
- **Transport pod launchers** announce fuel port location relative to the cursor

After placing a blueprint, navigating over it announces information as if the building were complete. This allows verification of orientation before construction begins. Use **Shift+Space** to remove and reposition a blueprint if needed.

### Selection Modes for Zones and Orders

When creating zones (stockpiles, growing zones, home areas) or placing multi-cell orders (mining, cutting, etc.), you can switch between two selection modes:

**Box-Selection Mode (Default):**
- Press Space to set the first corner
- Navigate with arrow keys to the opposite corner (preview updates automatically)
- Press Space again to confirm the rectangle
- Can place multiple rectangles before confirming with Enter

**Single Tile Mode:**
- Press Tab to switch to this mode
- Press Space to toggle individual tiles on/off at the cursor position
- Each toggle announces "Selected" or "Deselected" with coordinates and total count
- Press Enter when done to create the zone/order

Press Tab at any time to switch between modes.

## Colonist Actions

| Key | Action |
|-----|--------|
| , / . | Cycle previous/next colonist |
| Alt+C | Jump cursor to selected colonist |
| Enter | Open inspection menu (includes job queue, logs, health, etc.) |
| ] | Open order menu (with descriptions) |
| R | Toggle draft mode |
| G | Open gizmos with status values and labels |
| Alt+M | Display mood and thoughts |
| Alt+N | Display needs |
| Alt+H | Display health (detailed condition info) |
| Alt+B | Quick combat log dump |
| Alt+F | Unforbid all items on map |
| F1 | Work menu |
| F2 | Schedule menu |
| F3 | Assign menu |


**Inspection Menu (Enter):** Access pawn details including:
- **Job Queue** category: View and cancel queued jobs (Delete key)
- **Log** category: Combat log and social log with timestamps and jump-to-target
- Health, equipment, records, and more

## Work Menu (F1)

| Key | Action |
|-----|--------|
| Up/Down | Navigate work types |
| Tab / Shift+Tab | Next/previous colonist |
| M | Toggle simple/manual priority mode |
| Space | Toggle work type (simple) or toggle disabled/priority 3 (manual) |
| 0-4 | Set priority directly (manual mode) |
| Enter | Save and close |
| Escape | Cancel and close |

## Schedule Menu (F2)

| Key | Action |
|-----|--------|
| Up/Down | Navigate pawns |
| Left/Right | Navigate hours |
| Tab | Cycle assignment type (Anything/Work/Joy/Sleep/Meditate) |
| Space | Apply selection to cell |
| Shift+Right | Fill rest of row with selection |
| Ctrl+C / Ctrl+V | Copy/paste pawn schedule |
| Enter | Save and close |
| Escape | Cancel and close |

## Assign Menu (F3)

| Key | Action |
|-----|--------|
| Left/Right | Switch policy categories |
| Up/Down | Navigate policies |
| Enter | Apply policy to colonist |
| Tab | Next colonist |
| alt + E | Open policy editor |
| Escape | Close |

Categories: Outfit, Food Restrictions, Drug Policies, Allowed Areas, Reading Policies (Ideology DLC).

## Animals Menu (F4)

| Key | Action |
|-----|--------|
| Up/Down | Navigate animals |
| Left/Right | Navigate property columns |
| Enter | Toggle checkbox / open dropdown |
| alt + S | Sort by current column |
| Escape | Close |

Columns include: Name, Bond, Master, Slaughter, Gender, Age, Training, Follow settings, Area, Medical Care, Food Restriction, Release to Wild.

## Scanner System

Linear navigation through all map items by category. Always available during map navigation.

| Key | Action |
|-----|--------|
| Page Up/Down | Navigate items in subcategory |
| Ctrl+Page Up/Down | Switch categories |
| Shift+Page Up/Down | Switch subcategories |
| Alt+Page Up/Down | Navigate within bulk groups |
| Home | Jump cursor to current item |
| Alt+Home | Toggle auto-jump mode (cursor automatically follows scanner) |
| End | Read distance/direction to item |

**Categories:**
- **Colonists** - All colonists
- **Tame Animals** - Bonded, pets, livestock
- **Wild Animals** - Wildlife by species
- **Orders** - Construction, Haul, Hunt, Mine, Deconstruct, Uninstall, Cut, Harvest, Smooth, Tame, Slaughter
- **Buildings** - All structures
- **Zones** - Growing (with plant type), Stockpile, Fishing, Other
- **Rooms** - Named rooms (e.g., "Ann's Bedroom")
- **Trees** - Tree types
- **Plants** - Wild plants
- **Items** - All items by type
- **Mineable Tiles** - Stone types, chunks

**Auto-jump mode:** When enabled, the map cursor automatically jumps to each item as you navigate with Page Up/Down. Distance calculations always update based on current cursor position.

## World Map (F8)

Press **F8** to toggle between the colony map and world map views.

| Key | Action |
|-----|--------|
| Arrow Keys | Navigate tiles (Up=North, Down=South, Left=West, Right=East) |
| 1-5 | Detailed tile information |
| I | Inspect tile or caravan |
| S | Settlement browser |
| R | Open route planner |
| C | Form caravan (at colony) |
| G | Open gizmos (when caravan is stopped) |
| ] | Caravan orders |
| , / . | Cycle between caravans |
| Ctrl+Space | Select/deselect caravan for multi-selection |
| Home | Jump cursor to scanner selection |

### Roads and Rivers

The mod tracks roads and rivers on the world map. When navigating to a tile, information about any roads or paths is announced:

- "Stone road runs north to south"
- "Dirt path junction: west, south, and east"
- "River flows northeast"

As a road is followed, announcements update to reflect the direction it continues.

### Tile Information (Number Keys)

Press **1-5** on the world map for detailed tile information:

| Key | Information |
|-----|-------------|
| **1** | Growing period, rainfall, forageability, animal grazing, stone types |
| **2** | Movement difficulty, winter penalty, terrain type, elevation, current road, caravan paths |
| **3** | Disease frequency, tile pollution, nearby pollution, noxious haze (DLC content) |
| **4** | Coordinates and time zone |
| **5** | Region name |

### World Scanner

The world map has its own scanner system. Use **Ctrl+Page Up/Down** to cycle through categories:

- **Settlements** - Subcategories: Player settlements, Neutral, Hostile
- **Quest Sites** - Active quest locations
- **Caravans** - Traveling caravans
- **Biomes** - Biome type and approximate size (e.g., "Temperate forest, approximately 2,260 tiles")
- **Roads** - Road networks on the map

Press **Home** to jump the cursor to the currently selected scanner item.

### Caravan Path Visibility

When a caravan is selected (using comma/period), its planned route becomes visible on the map. When the cursor is on a tile in that path, the destination direction is announced (e.g., "Bob's caravan heading east").

To view paths for multiple caravans simultaneously:
1. Use **Comma/Period** to cycle to a caravan
2. Press **Ctrl+Space** to select it
3. Cycle to another caravan and press **Ctrl+Space** again
4. Now both caravan paths are visible as the cursor moves across the map

This is helpful when coordinating movement of multiple caravans. Press **Ctrl+Space** again on selected caravans to deselect them.

## Route Planner

Press **R** on the world map to activate the Route Planner. This allows planning travel routes and estimating journey times before committing to a caravan.

| Key | Action |
|-----|--------|
| Space | Add waypoint at cursor |
| Shift+Space | Remove waypoint at cursor |
| E | Hear estimated travel time |
| Escape | Exit route planner |

### How It Works

1. Navigate to the starting point (usually a colony)
2. Press **Space** to add the first waypoint
3. Navigate to the destination
4. Press **Space** to add another waypoint
5. The estimated travel time is announced using average caravan speed

When the Route Planner is active, the scanner gains a **Waypoints** category for navigating between waypoints. Press **2** to hear directions and arrival times for each point along the path.

> **Tip:** Starting the Route Planner with the first waypoint on an actual caravan will use that caravan's real travel speed instead of the average.

## Caravans

Caravans allow colonists to travel across the world map to trade, attack settlements, complete quests, and more.

### Forming a Caravan

Press **C** while on the world map at a colony to begin forming a caravan.

#### Route Selection

After pressing **C**, route selection mode activates:

1. Navigate to the destination
2. Press **Space** to add a waypoint
3. Optionally add more waypoints for complex routes
4. Press **Enter** to confirm and open the formation screen

> **Tip:** For auto-provision to calculate supplies for a round trip, add a waypoint at the destination AND a waypoint back at the colony.

When using the scanner during route selection, destinations are marked as reachable or unreachable based on terrain. Some quest locations spawn in areas that can only be reached by transport pods or shuttles.

#### Pawns Tab

The first tab shows available colonists. Each entry displays the pawn name and currently equipped weapon with its condition.

| Key | Action |
|-----|--------|
| Space/Enter | Toggle pawn selection |
| Alt+H | Quick health overview |
| Alt+M | Quick mood overview |
| Alt+N | Quick needs overview |
| Alt+I | Full inspection screen |

#### Items Tab

Navigate here with **Right Arrow** from the Pawns tab.

| Key | Action |
|-----|--------|
| Space/Enter | Open quantity menu |
| Delete | Remove item from caravan |
| Alt+I | Inspect item details |

#### Quantity Adjustment

These quantity controls work in caravan formation, transport pod loading, and trading screens:

| Key | Action |
|-----|--------|
| = or + | Increase by 1 |
| - | Decrease by 1 |
| Shift+Up | Increase by 10 |
| Shift+Down | Decrease by 10 |
| Ctrl+Up | Increase by 100 |
| Ctrl+Down | Decrease by 100 |
| Shift+Home | Set to maximum available |
| Shift+End | Set to zero |
| Delete | Set to zero (same as Shift+End) |
| Shift+Enter | Set to maximum given carrying capacity |
| Enter | Type a specific number, then press Enter to confirm |

The difference between **Shift+Home** and **Shift+Enter**: Shift+Home adds the maximum quantity available regardless of weight, while Shift+Enter adds only what the caravan can carry given current mass restrictions.

#### Travel Supplies Tab

Navigate here with **Right Arrow** from the Items tab. Same controls as the Items tab.

**Auto-Provision:**
- Press **Alt+A** to toggle auto-provisioning
- When enabled, the game automatically selects food and medicine based on estimated travel time
- The supplies tab is locked while auto-provision is active

#### Summary View

Press **Tab** to access the summary view showing caravan statistics:

- **Mass** - Current load vs. capacity
- **Speed** - Travel speed in tiles per day
- **Food** - Food supplies and foraging info
- **Visibility** - How visible the caravan is to enemies
- **Destination** - Where the caravan is heading
- **ETA** - Estimated time of arrival

Press **Alt+I** on any stat to see a detailed breakdown with tooltips explaining each contributing factor.

**Sending the Caravan:**

Press **Alt+S** to finalize and send the caravan.

### Caravan Management

#### Cycling Between Caravans

On the world map, use **Comma** and **Period** to cycle between caravans. Each caravan announces its current status:

- "Sam's Caravan - Traveling to [destination]"
- "Northern Expedition - Resting)"
- "Trade Group - Stopped, waiting for orders"

Press **Alt+C** to move the cursor to the currently selected caravan.

#### Commanding a Caravan to Travel

To send a caravan to a destination:

1. Select the caravan with **Comma/Period**
2. Navigate the cursor to the **destination tile** (not where the caravan currently is)
3. Press **]** (right bracket) to open the orders menu
4. Choose an action like "Travel to this tile" or "Enter [settlement name]"

The right bracket key is for giving orders about a target location—where the caravan should go or what it should do when it arrives.

#### Caravan Gizmos (Actions at Current Location)

When at the location of a caravan, press **G** to open the gizmo menu for actions at its current tile:

- **Camp** - Create a temporary map to mine, hunt, tame animals, or rest
- **Split this caravan** - Divide into two groups
- **Settle here** - Found a new colony (if enabled)
- Other context-specific options

The gizmo menu is for what the caravan does where it currently is, while the orders menu (]) is for where it should go next.

#### Reforming a Caravan

When ready to leave a temporary camp or quest location:

1. Press **Shift+C** to reform the caravan
2. Use the formation screen to select pawns and items
3. Press **Alt+S** to finalize

After reforming, use caravan orders (**]**) to set the next destination.

#### Splitting Caravans

To split a caravan into two groups:

1. Stop the caravan (if moving)
2. Press **G** for gizmos
3. Select **Split this caravan**
4. Choose which pawns join the new caravan
5. Choose which items the new caravan takes
6. In the summary view, use **Left/Right** to switch between viewing each caravan's stats
7. Press **Alt+S** to finalize the split

#### Merging Caravans

To merge two caravans:

1. Move both caravans to the same tile
2. Use **Comma/Period** to find each caravan
3. Press **Ctrl+Space** on each caravan to select them
4. Press **G** for gizmos
5. Select **Merge caravans**

### Caravan Inspect Screen

Press **enter** while on the same tile as a caravan to open the inspect screen. This is a navigable tree view.

| Key | Action |
|-----|--------|
| Up/Down | Navigate items |
| Right/Enter | Expand section |
| Left | Collapse section or go to parent |
| * (asterisk) | Expand all sections |
| Type letters | Search/filter items |
| Delete | Drop item (Items section) or banish pawn (Pawns section) |
| Alt+I | View stat breakdown with tooltips |
| Escape | Close inspect screen |

**Sections:**

- **Status** - Current activity, speed, location
- **Stats** - Mass, visibility, food situation (with full tooltips and breakdowns via Alt+I)
- **Pawns** - Expand to inspect individual pawns (press Delete to banish a pawn from the caravan)
- **Items** - All carried items (press Delete to drop)
- **Gear** - Each pawn's equipment (press Enter to swap between pawns or return to inventory)

### Multi-Map Navigation

When colonists are on multiple maps (e.g., main colony and a caravan camp):

| Key | Action |
|-----|--------|
| Comma/Period | Cycle between pawns on the current map |
| Shift+Comma/Shift+Period | Switch between maps |

When switching maps, the map name and number of colonists there is announced. The mod remembers which pawn was selected on each map.

## Transport Pods

Transport pods are powerful, rapid, one-way vehicles. A fully-fueled launcher can send a pod up to 66 tiles away, arriving in seconds. They become available in the mid-game after researching the prerequisite technologies.

### Building Transport Pod Launchers

When placing a transport pod launcher, the fuel port location is announced relative to the cursor. The launcher occupies two tiles, and the fuel port determines where fuel is loaded and where the transport pod itself will sit.

Once a launcher is built:
1. Press **G** on the launcher to open gizmos
2. Select **Build Transport Pod** to automatically construct a pod at the fuel port location
3. Optionally enable the **Auto-build** gizmo to automatically queue new pods after each launch

This approach is more efficient than manually placing transport pod buildings.

### Grouping Pods

Each pod has a 150kg capacity limit. For larger payloads, multiple pods can be grouped by placing their launchers adjacent to each other.

To group pods:
1. Press **G** on any pod in the group
2. Select **Group all pods** to automatically group all adjacent pods, or
3. Select **Group pods manually** to enter placement mode, then navigate to each desired pod, press **Space** to add it, and **Enter** when finished

Grouped pods share their combined capacity (e.g., three pods = 450kg).

### Loading Pods

After grouping (or with a single pod), the loading screen opens. This works similarly to caravan formation:

| Key | Action |
|-----|--------|
| Left/Right | Switch between Pawns and Items tabs |
| Up/Down | Navigate items |
| Space/Enter | Toggle selection or open quantity menu |
| Alt+I | Inspect item details |
| Alt+H/M/N | Quick pawn health/mood/needs |
| Alt+S | Begin loading the pods |

The same quantity adjustment shortcuts from caravan formation apply here (=, -, Shift+Up/Down, Ctrl+Up/Down, Shift+Home/End, Shift+Enter).

Once **Alt+S** is pressed, pawns will load the selected items into the pods. A notification appears when loading is complete.

> **Note:** Occasional alerts about missing items may appear after loading—this is a known game quirk where items are briefly "lost" after being loaded into pods.

### Launching Pods

Once pods are loaded:

1. Press **G** on the pod
2. Select **Launch pods**
3. The cursor moves to the world map
4. Navigate to the destination (use the scanner if needed)
5. Press **Enter** to select the destination

**Destination Options:**

| Destination Type | Result |
|-----------------|--------|
| Empty tile | Pods land and form a caravan |
| Tile with your caravan | Pod contents merge with that caravan |
| Settlement | Choose: Visit (form caravan), Gift (donate contents), or Attack (edge or center landing) |
| Your colony or map with your pawns | Enters placement mode to choose exact landing location |

**Placement Mode (friendly tiles):**

When launching to a tile with friendly pawns, placement mode allows choosing the exact landing spot:
1. Navigate to the desired landing location
2. Press **Space** to place, then **Enter** to confirm
3. Unpause the game—pods launch after a brief delay

> **Warning:** Pods landing on roofed areas (except mountains) will destroy the roof, potentially injuring nearby pawns. Pawns inside the pod are not harmed.

### Creative Uses

Transport pods have many strategic applications:
- Reinforcing a distant caravan quickly
- Sending supplies to colonists on a quest
- Dropping combat animals into enemy settlements
- Returning captured prisoners to their faction for relationship boosts
- Emergency medical evacuations

## Colony Inventory (I)

Press **I** to open the colony inventory, a tree view of all items in the colony.

| Key | Action |
|-----|--------|
| Up/Down | Navigate categories/items |
| Left/Right | Collapse/expand |
| Enter | Activate (expand or execute action) |
| * (asterisk) | Expand all categories |
| Type letters | Search/filter items |
| Escape | Close |

Actions per item: Jump to location, View details.

### Installing Buildings from Inventory

When a building is uninstalled (via the Uninstall gizmo), it becomes a minified item stored in a stockpile. These appear in the inventory under a **Buildings** category.

To reinstall a building:
1. Move the cursor to the desired installation location
2. Press **I** to open inventory
3. Navigate to the Buildings category
4. Expand the desired building
5. Select **Install**
6. Use **R** to rotate if needed—orientation information is announced
7. Press **Space** to place the blueprint

Once placed, a colonist will carry the building from the stockpile and install it at the designated location.

> **Note:** Not all buildings can be uninstalled. Some structures like coolers can only be deconstructed.

## Trading System

RimWorld offers multiple ways to trade: orbital traders, visiting caravans, settlements on the world map, and more. The trading interface works consistently across all methods.

### Trade Screen Layout

The trade screen has two tabs initially:
- **[Trader's Name]'s Items** - What the trader is selling
- **Your Items** - What can be sold

When any trades are queued, a third tab appears:
- **Trade Summary** - All pending buy/sell transactions

Use **Left/Right** to switch between tabs and **Up/Down** to navigate items.

### Item Information

As items are navigated, the following information is announced:
- Item name and quantity available
- Price (with quality indicators like "great deal" or "very expensive")
- Brief description

For items that both parties possess (shared items), additional details appear:
- How many the trader has and their buy price
- How many you have and your sell price

### Quantity Adjustment

| Key | Action |
|-----|--------|
| = or + | Buy/sell 1 more |
| - | Buy/sell 1 less |
| Shift+Up | Buy/sell 10 more |
| Shift+Down | Buy/sell 10 less |
| Ctrl+Up | Buy/sell 100 more |
| Ctrl+Down | Buy/sell 100 less |
| Shift+Home | Maximum buy/sell |
| Shift+End | Reset to zero |
| Enter | Type exact quantity, then Enter (positive = buy, negative = sell) |

For shared items (owned by both parties):
- Up/Plus directions increase buying quantity
- Down/Minus directions increase selling quantity
- When typing a quantity: positive numbers buy, negative numbers sell

### Inspection and Price Breakdown

| Key | Action |
|-----|--------|
| Alt+I | Full inspection of the selected item |
| Tab | Price breakdown (shows trade skill effects, faction relations, etc.) |

### Trade Summary Tab

The Trade Summary tab appears when any items have been queued for trade. It shows:
- All items being bought (positive quantities)
- All items being sold (negative quantities)
- Net balance at the bottom

Quantities can be adjusted directly in this tab using the same controls.

### Completing a Trade

| Key | Action |
|-----|--------|
| Alt+B | Announce current silver balance and trade outcome |
| Alt+R | Reset current item to zero (can also use delete) |
| Shift+Alt+R | Reset all trades |
| Alt+G | Toggle gift mode (donate items for relationship boost) |
| Alt+A | Accept and complete the trade |
| Escape | Cancel and close |

> **Note:** When trading with orbital traders or visitors at a colony, purchased items drop on the ground near the trader or trade beacon. Colonists will haul them to stockpiles automatically if space is available.

## Other Shortcuts

| Key | Action |
|-----|--------|
| L | View alerts and letters (Delete key to delete letters) |
| F6 | Research menu (with tree navigation and typeahead) |
| F7 | Quest menu |
| Delete | Delete save file (in save/load menu) or letter (in alerts menu) |


## Mod Manager

Accessible from Main Menu → Mods. Provides full keyboard navigation for enabling, disabling, and reordering mods.

| Key | Action |
|-----|--------|
| Up/Down | Navigate mods in current list |
| Left/Right | Switch between inactive/active mod columns |
| Enter | Toggle mod enable/disable |
| Ctrl+Up | Move mod up in load order (active list only) |
| Ctrl+Down | Move mod down in load order (active list only) |
| alt + M | Open mod settings (if mod has settings) |
| alt + I | Read full mod description and info |
| alt + S | Save mod changes |
| alt + R | Auto-sort mods (resolve load order issues) |
| alt + O | Open mod folder in file explorer |
| alt + W | Open Steam Workshop page for mod |
| alt + U | Upload mod to Steam Workshop (requires Dev Mode) |
| Escape | Close mod manager |

---

**Questions?** Join the [Discord server](https://discord.gg/XdxfyvSKaT) for support and discussion.
