RimWorld Access - screen reader accessibility for RimWorld
==========================================

OVERVIEW
--------
This mod adds keyboard navigation and direct screen reader support to RimWorld.
Uses the Tolk library to communicate directly with NVDA, JAWS, and other screen readers.
Falls back to SAPI (Windows built-in TTS) if no screen reader is detected.

Installation:
1. Install RimWorld, and run it once to set up the folder structure correctly.
2. Install MelonLoader for RimWorld.
3. Copy all three DLL files to the RimWorld Mods folder:
   - rimworld_access.dll (the mod)
   - Tolk.dll (screen reader bridge library)
   - nvdaControllerClient64.dll (NVDA communication library)
4. Launch the game. UI text will be spoken through your screen reader or SAPI.

Note: The mod works with or without a screen reader running. If no screen reader is
detected, it automatically uses Windows SAPI for speech output.

MAIN MENU
---------
Arrow Keys    Navigate menu options
Enter         Select menu item


IN-GAME NAVIGATION
------------------
RimWorld Access provides tile-by-tile map navigation using arrow keys..

BASIC MOVEMENT:
Arrow Keys    Move cursor one tile in any direction
              - Up Arrow: North (positive Z axis)
              - Down Arrow: South (negative Z axis)
              - Left Arrow: West (negative X axis)
              - Right Arrow: East (positive X axis)

, (comma)     Cycle to previous colonist 
. (period)    Cycle to next colonist 
I   Open colony-wide inventory menu
Enter         Open inspect pannel.
Escape        Open pause menu

NAVIGATION FEATURES:
- Audio feedback: Plays terrain-specific sounds when moving (grass, stone, wood, etc.)
- Boundary detection: "Map boundary" message when attempting to move past map edge
- Automatic announcements: Tile information spoken via screen reader after each move
- Fog of war: Tiles in unexplored areas announce as "unseen" with no other info

TILE SUMMARY FORMAT:
When you move to a tile, you'll hear information in this order:
1. Pawns (up to 3 by name, then "and X more pawns")
2. Buildings (up to 2 with power/temperature info, then "and X more buildings")
3. Items (single item with stack count, or "X items" for multiple)
4. Plants (first plant if nothing else is present)
5. Terrain type (only if no audio feedback available)
6. Zone name (e.g., "stockpile zone 1", "growing zone 2")
7. Roofed status (only announced when roofed - unroofed tiles say nothing)
8. Light level (dark, dim, lit, bright)
9. Coordinates (e.g., "103, 204")

Examples:
- "John, Steel wall, roofed, lit, 52, 48"
- "5 items, growing zone 1, bright, 103, 204"
- "unseen" (for unexplored tiles)
- "Map boundary" (when at edge)

(Note: All menus use Arrow Keys to navigate, Enter to confirm, Escape to close)


TILE INFORMATION HOTKEYS (Keys 1-5)
------------------------------------
While navigating the map, press number keys (1-5) to get detailed information about
the current cursor position. These work with both top row number keys and numpad.

NOTE: Shift+1/2/3 are reserved for time controls. Number keys only work when Shift
is NOT held down.

KEY 1 - ITEMS AND PAWNS
Lists all items and pawns at the cursor position:
- Pawns: All colonists/animals by name (e.g., "John, Sarah, Mike")
- Items: Up to 10 items with stack counts (e.g., "Steel x50, Wood x100, Medicine x20")
- Shows "Forbidden" prefix for forbidden items
- Shows "and X more" if more than 10 items present
- Returns "no items or pawns" if tile is empty

KEY 2 - FLOORING DETAILS
Shows terrain information:
- Terrain type (e.g., "Slate floor", "Soil", "Concrete")
- Smoothness (smooth/rough for stone floors)
- Beauty rating (if non-zero)
- Cleanliness value (if non-zero)
- Path cost (movement speed modifier)
Example: "Slate floor, smooth, beauty 2, cleanliness 0.2, path cost 0"

KEY 3 - PLANT INFORMATION
Displays plant details:
- Plant species name
- Growth percentage (0-100%)
- Harvestable status (harvestable/not harvestable)
- Dying status (if applicable)
- Lists multiple plants if present
Example: "Potato plant (75% grown), not harvestable"
Returns "no plants" if no plants at cursor

KEY 4 - BRIGHTNESS AND TEMPERATURE (COMBINED)
Shows environmental conditions:
- Light level description (dark, dim, lit, bright)
- Glow value (numeric light level, 0.0-1.0+)
- Temperature in Celsius
- Indoor/outdoor status (based on roof presence)
- Temperature control buildings (coolers, heaters, vents with settings)
Example: "lit (0.65 glow), 21.3°C, indoors. Cooler: cooling north, heating south, target 18°C"

KEY 5 - ROOM STATISTICS
Shows room quality metrics (only for enclosed rooms with roofs):
- Room type/role (Bedroom, Dining room, Hospital, etc.)
- Impressiveness rating (affects mood)
- Cleanliness value (affects disease/infection chance)
- Wealth value (total value of room contents)
Example: "Bedroom, impressiveness 45, cleanliness 0.3, wealth 1250"
Returns "outdoors" if no roof present
Returns "no room" if room data unavailable

COOLDOWN: All detail requests have a 0.3 second cooldown to prevent accidental spam

TIME CONTROLS
-------------
Shift+1       Set time speed to Normal (with sound feedback)
Shift+2       Set time speed to Fast (with sound feedback)
Shift+3       Set time speed to Superfast (with sound feedback)
Space         Pause/unpause game


BUILD SYSTEM (Press Tab)
-------------------------
1. Select category (Orders, Structure, Production, etc.)
2. Select build tool (Wall, Door, Bed, etc.)
3. If required, select material (Wood, Steel, etc.)
4. Use Arrow Keys to position, space to place, enter to confirm, or Escape to cancel


ZONE SYSTEM (Press Z)
---------------------
1. Select zone type (Growing zone, Stockpile, etc.)
2. Use Arrow Keys to navigate, space to add/remove cells, enter to confirm, or escape to cancel.


SCHEDULE MENU (Press F2)
-------------------------
Manage colonist work schedules across all 24 hours:

Arrow Keys    Navigate grid (Up/Down: pawns, Left/Right: hours)
Tab           Cycle assignment type and apply to current cell (Anything/Work/Joy/Sleep/Meditate)
Space         Apply current selection to cell
Shift+Right   Fill rest of row with selected assignment
Ctrl+C        Copy current pawn's entire schedule
Ctrl+V        Paste copied schedule to current pawn
Enter         Save all pending changes and close
Escape        Cancel changes and close



ANIMALS MENU F4
---------------------------------
Comprehensive keyboard-accessible interface for managing all colony animals. Access all
animal information and settings without requiring the mouse.

Navigation:
Up/Down       Navigate between animals in the list
Left/Right    Navigate between property columns for current animal
Enter         Toggle checkboxes or open dropdown menus for current property
S             Sort by current column (toggles ascending/descending)
Escape        Close animals menu

Available Columns (20+ total):
- Name: Animal's name and species
- Bond: Bonded colonist relationship
- Master: Assigned master (dropdown to select colonist)
- Slaughter: Mark for slaughter (checkbox)
- Gender: Male/Female/None
- Life Stage: Baby/Juvenile/Adult
- Age: Years old
- Pregnant: Pregnancy status
- Training: All available trainings (Obedience, Release, Rescue, Haul, etc.)
  * Each training shows status with description
  * Announces prerequisites if not met
  * Shows training progress for partially trained skills
- Follow Drafted: Follow master when drafted (checkbox)
- Follow Fieldwork: Follow colonists doing fieldwork (checkbox)
- Allowed Area: Movement restriction (dropdown)
- Medical Care: Medical care level (dropdown)
- Food Restriction: Food policy (dropdown)
- Release to Wild: Mark for release (checkbox)

Smart Announcements:
- Full context (animal name + property) announced when:
  * Opening menu or changing animals (Up/Down)
  * After sorting (since animal position may change)
- Concise updates (property only) announced when:
  * Navigating between properties (Left/Right)
  * After toggling checkboxes or selecting from menus

Examples:
Opening or selecting new animal:
  "Husky 1 (Husky) - Master: John"

Navigating properties:
  "Obedience: Trained - The animal will follow its master when drafted."
  "Slaughter: Not marked - Designate this animal for slaughter."

After toggling:
  "Slaughter: Marked for slaughter - Designate this animal for slaughter."

Training with prerequisites:
  "Release: Disabled - Needs Obedience first - The animal will be released on command."


COLONIST ACTIONS
----------------
]             Open order menu (includes draft mode orders like Move, Attack)
R             Toggle draft mode for selected colonist
Alt+M         Display mood information and thoughts
Alt+N         Display needs
Alt+H         Display health
Alt+F         Unforbid all forbidden items on the map
F1            Open work menu (see Work Menu section below)
F2            Open schedule menu (manage colonist timetables)
F3            Open assign menu (manage outfits, food, drugs, areas, reading)
g open gismos for selected colonist.  Gismos include options like fire at will, draft, etc.  G also opens gismos on a selected object.  

WORK MENU (Press F1)
--------------------
Manage colonist work assignments with support for both simple mode and manual priorities.

Common Controls:
Up/Down       Navigate work types (stops at top/bottom)
Tab           Switch to next colonist
Shift+Tab     Switch to previous colonist
M             Toggle between simple mode and manual priorities mode
Enter         Save all changes and close
Escape        Cancel changes and close

Simple Mode (toggle work types on/off):
Space         Toggle selected work type enabled/disabled

Manual Priority Mode (assign priority numbers 1-4):
0-4           Set priority directly (0=disabled, 1=highest, 4=lowest)
Space         Quick toggle between disabled and default (priority 3)
Shift+Up      Move work type up in priority order (affects execution when priorities equal)
Shift+Down    Move work type down in priority order (affects execution when priorities equal)

Notes:
- In manual priority mode, lower numbers execute first (1 before 2 before 3 before 4)
- Work types with the same priority number use list order as tiebreaker
- Changes are marked as "(pending)" until you press Enter to save
- Column reordering (Shift+Up/Down) affects all colonists globally.


ASSIGN MENU (Press F3)
----------------------
Manage colonist assignments across five policy categories: Outfit, Food Restrictions,
Drug Policies, Allowed Areas, and Reading Policies (Ideology DLC only).

Navigation:
Left/Right    Switch between policy categories (columns)
Up/Down       Navigate available policies within current category
Enter         Apply selected policy to current colonist
Tab           Switch to next colonist
E         Open policy editor/manager for current category
Escape        Close assign menu

Policy Editor Controls:
When you press E to open a policy editor, you can manage policies:

Up/Down       Navigate policy list (in policy list mode)
Tab           Enter actions menu
escape     Return to policy list
Enter         Execute selected action or confirm
Escape        Go back/close editor

Available Actions:
- New Policy: Create a new policy
- Rename Policy: Rename the selected policy
- Duplicate Policy: Make a copy of the selected policy
- Delete Policy: Delete the selected policy (if not in use)
- Set as Default: Set as the default policy for new colonists
- Edit Filter: Edit apparel/food filters (navigate with arrows, Space to toggle, Enter to expand/collapse)
- Edit Drugs: Configure drug usage settings (navigate drugs, edit dosage schedules and conditions)
- Close: Return to assign menu

Notes:
- Outfit policies let you control what clothes colonists will wear
- Food policies restrict what foods colonists can eat
- Drug policies control when and how colonists use drugs
- Allowed areas restrict where colonists can go
- Reading policies (Ideology DLC) control colonist ideological development.


SCANNER SYSTEM (Always Available on Map)
-----------------------------------------
The scanner provides linear navigation through all map items organized into categories
and subcategories. Identical items (same type, quality, material) are automatically
grouped together for efficient navigation.

Basic Navigation:
Page Up/Down           Navigate through items in current subcategory
Ctrl+Page Up/Down      Switch between categories
Shift+Page Up/Down     Switch between subcategories
Alt+Page Up/Down       Navigate through individual items within a bulk group
Home                   Jump cursor to current item (or specific bulk item if within group)
End                    Read distance and direction from cursor to current item

Categories:
1. Colonists
   - Player-Controlled Pawns (your colonists)
   - NPCs (all non-player humanoids: hostiles, friendlies, traders, prisoners)
2. Tame Animals (player faction animals)
3. Wild Animals (non-player faction animals)
4. Buildings
   - Walls & Doors
   - Other Buildings
5. Trees
   - Harvestable Trees (wood yield)
   - Non-Harvestable Trees
6. Plants
   - Harvestable Plants
   - Debris (grass, etc.)
7. Items
   - All Items
   - Forbidden Items
8. Mineable Tiles (rock tiles with ore deposits)

Announcement Format:
- Single items: "Steel wall - 12.5 tiles"
- Bulk groups: "Steel - 5.2 tiles, 1 of 15" (shows position in group)
- When navigating within bulk group with Alt+Page Up/Down:
  "Steel - 8.3 tiles, 3 of 15" (individual item position and distance)

Features:
- Always available during map navigation - no need to open/close
- Items sorted by distance (closest first)
- Automatic grouping of identical items
- Empty categories/subcategories are automatically skipped
- Navigation position persists across keystrokes
- Direction calculations use 8-direction compass (N, NE, E, SE, S, SW, W, NW)

NOTE: Scanner keys (Page Up/Down, Home, End) are disabled in world view and repurposed
for world map navigation (see WORLD MAP NAVIGATION section below).


WORLD MAP NAVIGATION (Press F8)
--------------------------------
The world map shows the entire planet with settlements, biomes, and world tiles. All
navigation is keyboard-accessible with automatic camera movement and screen reader
announcements.

Opening World View:
F8                Toggle between map view and world view

Tile Navigation (Camera-Relative):
Arrow Keys        Move to neighboring tiles
                  - Navigation is relative to camera rotation
                  - Up moves "forward" on screen, down moves "back"
                  - Each keypress moves to the closest neighbor in that direction
                  - Tile info announced automatically (biome, temperature, settlements)

Quick Navigation:
Home              Jump to player's home settlement
End               Jump to nearest player caravan
Page Down         Cycle to next settlement (sorted by distance from current tile)
Page Up           Cycle to previous settlement (sorted by distance from current tile)

Tile Information:
I                 Read detailed tile information (coordinates, biome, elevation,
                  temperature, world objects)

Settlement Browser (Press S):
Opens a full-featured settlement browser with faction filtering.

Navigation:
Up/Down           Navigate through settlement list
Shift+Left        Previous faction filter
Shift+Right       Next faction filter
Enter             Jump to selected settlement and close browser
Escape            Close settlement browser

Faction Filters (cycle with Shift+Left/Right):
- All settlements (default)
- Player settlements only
- Allied settlements only
- Neutral settlements only
- Hostile settlements only

Settlement Announcements:
Each settlement shows: position, name, faction, relationship, distance
Example: "3 of 15: Trade Post Alpha, Empire, Ally, 48.2 tiles"

Tile Announcement Format:
Tiles announce: biome, hilliness, temperature, and any settlements/objects present
Example: "Temperate forest, Small hills, 18°C, Trading Post (Empire, Ally)"

Important Notes:
- Map-specific keys are disabled in world view (A for architect, Z for zones, etc.)
- Scanner keys (Page Up/Down, Home, End) work differently in world view
- Arrow keys navigate world tiles instead of map tiles
- Press F8 or Escape to return to map view


COLONY INVENTORY MENU (Press I / Shift+i)
------------------------------------------
Displays all items stored across your entire colony in a hierarchical menu organized
by category. Items from all stockpiles and storage buildings are combined and totaled
together (e.g., 100 wood in stockpile 1 + 300 wood in stockpile 2 = Wood x400).

Navigation:
Up/Down       Navigate through categories, items, and actions
Left/Right    Collapse/expand categories and items
Enter         Activate selection (expand/collapse or execute action)
Escape        Close inventory menu

Structure:
1. Categories (Foods, Resources, Weapons, Apparel, etc.)
   └─ Items with total quantities (Wood x400, Steel x250)
      └─ Actions:
         - Jump to location: Moves camera and cursor to where item is stored
         - View details: Shows item description, market value, mass, and storage info

Notes:
- All items of the same type are combined regardless of storage location
- Categories show the number of item types they contain
- Jump to location feature closes the menu and moves you to the first storage location
- Empty colonies will show "No items in colony storage"


TRADING SYSTEM
--------------
The mod provides a windowless, keyboard-accessible trading interface that replaces the
visual trade dialog with full feature parity. All trades are confirmed before execution.

Opening Trades:
- Interact with traders normally (caravans, orbital ships, settlements)
- The accessible trade menu automatically replaces the visual dialog

List View Mode:
Arrow Up/Down     Navigate items in current category
Arrow Left/Right  Switch between categories (Currency → Colony Items → Trader Items)
Enter             Enter quantity adjustment mode for selected item
A                 Accept trade (shows confirmation dialog)
G                 Toggle gift mode (when available with friendly factions)
P                 Show detailed price breakdown for current item
B                 Announce current trade balance (silver/favor spending)
R                 Reset current item quantity to zero
Shift+R           Reset all trade quantities
Escape            Close trade menu

Quantity Adjustment Mode:
Up/Down or +/-    Adjust quantity by ±1
Shift+Up/Down     Adjust quantity by ±10
Ctrl+Up/Down      Adjust quantity by ±100
Alt+Up            Set to maximum sell amount
Alt+Down          Set to maximum buy amount
Enter             Exit adjustment mode and return to list view
Escape            Exit adjustment mode and return to list view

Trade Confirmation Dialog:
When you press A to accept a trade, a summary appears showing:
- YOU GET: Items/silver you will receive
- YOU LOSE: Items/silver you will spend

Enter             Confirm and execute the trade
Escape            Cancel and return to trading menu

Features:
- Three-category organization (Currency, Colony Items, Trader Items)
- Real-time price calculations with negotiator skill bonuses
- Support for all trader types (Pawns, Orbital Ships, Settlements)
- Gift mode for friendly factions (affects goodwill instead of trading)
- Royal favor trading support (Royalty DLC)
- Caravan trading with mass/capacity tracking
- Comprehensive trade summary before execution
- Validation for insufficient funds, trader inventory, ideology restrictions
- Direct screen reader output via Tolk library
- Audio feedback using RimWorld's built-in sounds

Information Announced:
For each item: "(Position/Total) ItemName | Colony: X Sell: Y silver | Trader: Z Buy: W silver | Current: Action N for M silver"

Example:
"(5/42) Steel | Colony: 500 Sell: 2.1 silver | Trader: 200 Buy: 3.0 silver | Current: Selling 50 for 105 silver"

Trade Confirmation Example:
"Proposed Trade:

YOU GET:
  + 20 pemmican
  + 1 herbal medicine
  + 1 steel knife (good)

YOU LOSE:
  - 149 silver

Press Enter to confirm trade, Escape to cancel and return to trading"


OTHER SHORTCUTS
---------------
Delete        Delete selected save file (in save/load menu)
F6            Open research project selection menu
F7            Open quest menu (view available, active, and historical quests)

