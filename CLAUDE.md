# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

**##important note**
There's a file modification bug in Claude Code. The workaround is: always use complete absolute Windows paths with drive letters and backslashes for ALL file operations. Apply this rule to all file opperations going forward. 
## Project Overview

RimWorld Access is a native RimWorld mod that adds comprehensive keyboard accessibility features for visually impaired players. The mod uses Harmony patches to intercept UI rendering and input handling, providing direct screen reader support via the Tolk library and keyboard navigation throughout the game.

## Build Commands

**Build the mod:**
```bash
dotnet build
```

The build process automatically copies the compiled DLL and required
dependencies to a `Mods` directory inside the game's installation directory via a
post-build target.

**Deployed files:**
- `rimworld_access.dll` - The mod
- `Tolk.dll` - Screen reader bridge library
- `nvdaControllerClient64.dll` - NVDA communication library

**Note:** Audio files in the `Sounds/` folder are automatically embedded into the DLL during build.

**Target Framework:** .NET Framework 4.7.2

## Architecture

### Native Mod Integration
- Entry point: `rimworld_access.cs` - defines the `RimWorldAccessMod` static class with `[StaticConstructorOnStartup]` attribute
- On initialization (static constructor), creates a Harmony instance and applies all patches automatically via `harmony.PatchAll()`
- Harmony ID: `com.rimworldaccess.mainmenukeyboard`
- Requires Harmony mod as a dependency (declared in `About/About.xml`)
- Uses `Application.quitting` event for cleanup (TolkHelper.Shutdown())

### Core Design Pattern: State + Patch

The mod follows a consistent architectural pattern:
- **State classes** (`*State.cs`) - Maintain navigation state, track selections, and manage keyboard input
- **Patch classes** (`*Patch.cs`) - Harmony patches that intercept RimWorld UI methods to inject keyboard handling
- **Helper classes** (`*Helper.cs`) - Utility functions for common operations

### State Management System

All state classes (`*State.cs`) are static singletons that persist across game sessions. They typically include:
- `IsActive` - Boolean flag indicating if the state is currently handling input
- Navigation tracking (current selection, available options)
- Methods: `Open()`, `Close()`, `SelectNext()`, `SelectPrevious()`, `ExecuteSelected()`
- Screen reader integration via `TolkHelper.Speak()` for direct speech output

**Key State Classes:**
- `MenuNavigationState` - Main menu navigation
- `MapNavigationState` - In-game map tile cursor
- `WorldNavigationState` - World map tile navigation (F8 view)
- `SettlementBrowserState` - Settlement filtering and selection (S key on world map)
- `PawnSelectionState` - Pawn selection and cycling
- `ArchitectState` - Build menu (categories → tools → materials → placement)
- `ZoneCreationState` - Zone drawing mode
- `WorkMenuState` - Work priority UI
- `StorageSettingsMenuState` - Storage filter configuration
- `ScannerState` - Map item scanner with hierarchical categories and subcategories (always available via Page Up/Down)
- `NotificationMenuState` - Notification viewer for messages, letters, and alerts (L key)
- `DialogNavigationState` - Generic dialog navigation (handles all Dialog_NodeTree instances including research completion)
- `CaravanFormationState` - Caravan formation dialog (selecting pawns, items, and supplies)
- `WindowlessFloatMenuState` - Generic context menu handler
- `WindowlessPauseMenuState` - In-game pause menu
- `WindowlessSaveMenuState` - Save/load file selection
- `WindowlessOptionsMenuState` - Settings menu
- `WindowlessConfirmationState` - Yes/no confirmations

### Unified Input Handling

`UnifiedKeyboardPatch.cs` is the central keyboard input handler:
- Patches `UIRoot.UIRootOnGUI` at the Prefix level with High priority
- Processes all keyboard events before RimWorld's native input handling
- Implements priority system: confirmation dialogs → menus → gameplay shortcuts
- Calls `Event.current.Use()` to consume events and prevent default game behavior

**Input Priority Order:**
1. Delete confirmations
2. General confirmations
3. Save/load menu
4. Pause menu
5. Options menu
6. Scanner (J key)
7. Float menus (order-giving)
8. Escape key (open pause menu)
9. ] key (give orders to selected pawns)

**Note:** Dialog navigation (including research completion dialogs) is handled separately by `DialogAccessibilityPatch`, which patches `Dialog_NodeTree.DoWindowContents` directly rather than going through UnifiedKeyboardPatch.

### Harmony Patching Strategy

The mod uses multiple patching approaches:

**1. Prefix Patches** - Run before original method, used for:
- Input interception (UnifiedKeyboardPatch)
- Rebuilding UI structures that are created in local scope (MainMenuAccessibilityPatch)
- Canceling default behavior via `Event.current.Use()`

**2. Postfix Patches** - Run after original method, used for:
- Drawing visual highlights for selected items
- Initializing state after UI is constructed
- Injecting additional UI elements

**3. Reflection Usage**:
- `AccessTools.Method()` - Get references to private methods
- `AccessTools.Field()` - Access private fields
- Used heavily when RimWorld's UI structures aren't publicly accessible

### Screen Reader Integration

**TolkHelper.cs** - Direct screen reader communication via Tolk library
- Provides direct integration with NVDA, JAWS, and other screen readers
- Uses P/Invoke to call native Tolk.dll functions
- Supports speech priority levels (Low, Normal, High) for interruption control
- Falls back to SAPI (Windows built-in TTS) if no screen reader detected
- Implements direct NVDA communication fallback if Tolk detection fails

**Implementation Details:**
- `TolkHelper.Initialize()` - Loads Tolk and detects active screen reader
- `TolkHelper.Speak(text, priority)` - Sends text to screen reader
  - `SpeechPriority.Low` - Non-interrupting (navigation)
  - `SpeechPriority.Normal` - Default priority
  - `SpeechPriority.High` - Interrupts previous speech (errors, warnings)
- Direct NVDA fallback via `nvdaControllerClient64.dll` if Tolk fails to detect NVDA
- Every navigation action, menu selection, and status change speaks via TolkHelper

**Required DLLs:**
- `Tolk.dll` - Screen reader bridge library (120 KB)
- `nvdaControllerClient64.dll` - NVDA controller client for direct communication (150 KB)

### Audio Feedback System

The mod supports two approaches for audio feedback:

**1. Built-in Game Sounds (Recommended)**
- Use RimWorld's existing sound library via `SoundDefOf`
- No setup required, works immediately
- Examples: `SoundDefOf.Click`, `SoundDefOf.Tick_Tiny`, `SoundDefOf.TabOpen`
- Usage: `SoundDefOf.Click.PlayOneShotOnCamera();`

**2. Embedded Custom Sounds (Advanced)**
- Custom audio files can be embedded directly into the DLL
- Configured via `rimworld_access.csproj:7-12` (EmbeddedResource items)
- Audio files placed in project `Sounds/` folder are compiled into the DLL at build time
- No external files needed for deployment - sounds are inside the DLL binary

**EmbeddedAudioHelper.cs** - Custom audio loading and playback
- `LoadEmbeddedAudio(string resourcePath)` - Loads embedded audio file from DLL resources
- `PlayEmbeddedSound(string resourcePath, float volume)` - Loads and plays embedded audio
- Supports WAV format (16-bit PCM) with manual parsing
- OGG support is limited (requires workarounds due to Unity's async loading requirements)
- Uses reflection to extract embedded resources via `Assembly.GetManifestResourceStream()`
- Bypasses RimWorld's sound system, plays directly via Unity's `AudioSource` component

**Adding Custom Sounds:**
1. Place `.wav` files in project's `Sounds/` folder (e.g., `Sounds/navigate.wav`)
2. Build project - files are automatically embedded via MSBuild
3. Use: `EmbeddedAudioHelper.PlayEmbeddedSound("navigate.wav", 0.5f);`

**Recommended Format:** 16-bit PCM WAV files for best compatibility

**Design Philosophy:**
- Audio feedback provides **timing and context cues** (navigation, mode changes, confirmations)
- TolkHelper integration provides **content and speech** (menu labels, status, descriptions)
- Hybrid approach: non-speech audio + direct screen reader output

### Map Navigation System

**MapNavigationState + MapNavigationPatch**:
- Arrow keys move a virtual cursor across map tiles
- Cursor position stored as `IntVec3` (RimWorld's 3D integer coordinate type)
- Announces tile terrain, buildings, items, and pawns at cursor position
- Enter key at cursor position opens context menu for selected pawns
- Camera automatically follows cursor movement via `Find.CameraDriver.JumpToCurrentMapLoc()`

### Architect (Build) Menu Flow

**ArchitectState + ArchitectMenuPatch**:
1. Press **A** → Open category menu (Orders, Structure, Production, etc.)
2. Select category → Open tool menu (Wall, Door, Bed, etc.)
3. Select tool → If requires material, open material menu (Wood, Steel, etc.)
4. Select material → Enter placement mode
5. **ArchitectPlacementPatch** handles arrow key placement:
   - Arrow keys: Navigate placement cursor
   - Space: Place building at cursor position
   - Shift+Space: Cancel blueprint at cursor position
   - R: Rotate building
   - Enter: Confirm and exit placement mode
   - Escape: Cancel and exit placement mode

### Zone Management

**ZoneCreationState + ZoneCreationPatch**:
- Handles growing zones, stockpile zones, etc.
- Arrow keys to navigate, Enter to add/remove cells from zone
- Escape to finish zone creation
- Special plant selection menu for growing zones via **PlantSelectionMenuState**

### Notification Menu System

**NotificationMenuState** (L key):
- Unified viewer for all three RimWorld notification types:
  - **Messages** - Floating text notifications (bottom-left of screen)
  - **Letters** - Inbox messages (icons in bottom-right corner)
  - **Alerts** - Status warnings (text blocks in bottom-right corner)
- Two-level navigation system:
  1. **List View** - Browse all notifications with Up/Down arrows
  2. **Detail View** - Press Enter to see full explanation and additional details
- Each notification announces:
  - Type (Message/Letter/Alert)
  - Label/title text
  - Full explanation text (for letters and alerts)
  - Jump target availability
- **Jump-to-target** functionality:
  - In detail view, press Enter again to jump camera to the notification's target location
  - Automatically updates map cursor position if MapNavigationState is initialized
  - Closes menu after jumping
- **Navigation**:
  - Up/Down: Navigate through notifications (in list view) or scroll explanation text (in detail view)
  - Enter: Open detail view (from list) or jump to target (from detail)
  - Delete: Delete the selected letter (only letters can be deleted)
  - Escape: Go back (detail → list) or close menu (list → close)
- Collects notifications via reflection from:
  - `Messages.liveMessages` (private static field)
  - `Find.LetterStack.LettersListForReading` (public property)
  - `AlertsReadout.activeAlerts` (private instance field)

### Scanner System

**ScannerState + ScannerHelper**:
- Linear item scanner for navigating map objects with hierarchical category/subcategory structure
- **Always available** during map navigation - no need to open/close
- **Automatic item grouping**: Identical items (same type, quality, material) are grouped together
- **Key Bindings**:
  - **Page Up/Down**: Navigate through items in current subcategory (wraps around)
  - **Ctrl+Page Up/Down**: Switch between categories
  - **Shift+Page Up/Down**: Switch between subcategories within current category
  - **Alt+Page Up/Down**: Navigate through individual items within a bulk group
  - **Home**: Jump cursor to current item's position (or specific bulk item if navigating within group)
  - **End**: Read distance and direction from cursor to current item
- **Category Structure**:
  1. **Colonists** - Subcategories: Player-Controlled Pawns, NPCs (all non-player humanoids), Mechanoids (all mechanoids regardless of faction)
  2. **Tame Animals** - Player faction animals
  3. **Wild Animals** - Non-player faction animals
  4. **Buildings** - Subcategories: Walls & Doors, Other Buildings
  5. **Trees** - Subcategories: Harvestable Trees (wood yield > 0), Non-Harvestable Trees
  6. **Plants** - Subcategories: Harvestable Plants, Debris (grass, etc.)
  7. **Items** - Subcategories: All Items, Forbidden Items
  8. **Mineable Tiles** - Rock tiles with ore deposits
  9. **Orders** - Subcategories: Construction (blueprints and frames), Haul, Hunt, Mine, Deconstruct, Uninstall, Cut, Harvest, Smooth, Tame, Slaughter, Other
- **Behavior**:
  - Scanner keys work alongside map navigation (no modal state)
  - Item list refreshes automatically on each navigation action
  - Navigation position persists across keystrokes
  - Items sorted by distance within each subcategory (closest first)
  - Auto-skips empty categories/subcategories
  - **Order display at cursor**: When moving the map cursor, any active orders/designations at that tile are automatically announced (e.g., "Steel (Haul), granite (Mine)")
  - **Item announcements**:
    - Single items: "{label} - {distance} tiles"
    - Bulk groups: "{label} - {distance} tiles, {count} of {count}" (e.g., "Steel - 5.2 tiles, 15 of 15")
    - Within bulk group: "{label} - {distance} tiles, {position} of {count}" (e.g., "Steel - 5.2 tiles, 3 of 15")
  - Direction calculation uses 8-direction compass (N, NE, E, SE, S, SW, W, NW)
  - **Bulk grouping**: Identical items are grouped by def, quality, material, and HP (within 10%)
  - **Order grouping**: Designations are grouped by type (e.g., all Haul orders grouped together)

### Quest Menu System

**QuestMenuState** (Q key):
- Windowless keyboard-accessible quest browser organized into three tabs
- Tab structure mirrors RimWorld's native quest UI:
  - **Available** - Quests not yet accepted
  - **Active** - Currently ongoing quests
  - **Historical** - Completed, failed, expired, or dismissed quests
- Each quest displays:
  - Position in list (e.g., "3/15")
  - Quest name
  - Status badges ([Dismissed], [Completed], [Failed], [Expired])
  - Time information (expires in, accepted ago, finished ago)
  - Challenge rating (stars)
- **Navigation**:
  - Up/Down: Navigate through quests in current tab
  - Left/Right: Switch between tabs (Available ↔ Active ↔ Historical)
  - Enter: View detailed quest information
  - A: Accept quest (Available tab only)
  - D: Dismiss/Resume quest (Active/Available) or Delete (Historical)
  - Escape: Close menu
- Quest acceptance:
  - Checks `QuestUtility.CanAcceptQuest()` before accepting
  - Plays `SoundDefOf.Quest_Accepted` on success
  - Automatically refreshes list after acceptance
- Accesses quest data via `Find.QuestManager.questsInDisplayOrder`

### World Map Navigation System

**WorldNavigationState + WorldNavigationPatch**:
- Keyboard navigation for the world map (accessed via F8 to toggle world view)
- Arrow key navigation between world tiles using camera-relative directions
- **Automatic activation**: Detects world view by checking `World.renderer.wantedMode == WorldRenderMode.Planet`
- **Key Features**:
  - **Arrow keys**: Navigate between adjacent world tiles (uses dot product algorithm to find best neighbor tile in desired direction)
  - **Home**: Jump to player's home settlement
  - **End**: Jump to nearest player caravan
  - **Page Up/Down**: Cycle through settlements by distance from current position
  - **S key**: Open settlement browser with faction filtering
  - **I key**: Show caravan stats (if caravan is selected) or read detailed tile information (biome, terrain, temperature, elevation, roads, rivers)
  - **C key**: Form caravan (opens `Dialog_FormCaravan` when a player settlement is selected)
  - **] key**: Open order menu for selected caravan (at current cursor tile)
- **Caravan Formation**:
  - Press C key when a player settlement is selected
  - Validates that settlement has a map (`settlement.HasMap`)
  - Opens `Dialog_FormCaravan` with the settlement's map
  - Automatically activates `CaravanFormationState` for keyboard navigation
  - Provides feedback via TolkHelper
- **Caravan Order-Giving**:
  - Press ] key when a player caravan is selected
  - Calls `FloatMenuMakerWorld.ChoicesAtFor(tile, caravan)` to get available orders
  - Opens `WindowlessFloatMenuState` with filtered enabled options
  - Available orders include: Visit settlements/sites, Attack hostile settlements, Trade, Enter map locations
  - Orders automatically validate via RimWorld's native system
- **Settlement Browser** (`SettlementBrowserState`):
  - Modal menu for filtering and navigating settlements
  - Filter options: All, Player, Allied, Neutral, Hostile
  - Displays settlement name, faction, relationship, and distance
  - Up/Down: Navigate settlements, Left/Right: Switch filters, Enter: Jump to settlement, Escape: Close
- **Camera Integration**:
  - Automatically centers camera on selected tile via `Find.WorldCameraDriver.JumpTo()`
  - Syncs with RimWorld's native selection system via `Find.WorldSelector`
- **Visual Feedback**:
  - Draws on-screen overlay showing current tile info and keyboard shortcuts
  - Uses RimWorld's native tile highlight rendering
- **Implementation Details**:
  - Patches `WorldInterface.HandleLowPriorityInput` (Prefix + Postfix)
  - State persists across frame redraws
  - Supports hexagonal world grid navigation (`Find.WorldGrid.GetTileNeighbors()`)
  - Uses `WorldInfoHelper` for tile descriptions and settlement lists

### Caravan Formation System

**CaravanFormationState + CaravanFormationPatch**:
- Keyboard navigation for the `Dialog_FormCaravan` window
- Provides accessibility for selecting pawns, items, and travel supplies when forming caravans
- **Three-tab interface**:
  - **Pawns tab**: Select colonists, prisoners, and animals
  - **Items tab**: Select items and equipment to bring
  - **Travel Supplies tab**: Select food, medicine, and other supplies
- **Navigation**:
  - **Up/Down**: Navigate through items in current tab
  - **Left/Right**: Switch between tabs
  - **+/- keys**: Adjust quantity (increment/decrement by 1)
  - **Enter**: Toggle selection for pawns (0 ↔ max), adjust quantity for items
  - **D key**: Choose destination (switches to world view with keyboard navigation enabled)
  - **T key**: Send caravan (calls `Dialog_FormCaravan.TrySend()` via reflection)
  - **R key**: Reset selections (calls `CalculateAndRecacheTransferables()`)
  - **Escape**: Close/cancel dialog
- **Destination Selection Mode**:
  - Pressing D temporarily closes dialog and switches to world view
  - Use arrow keys to navigate to desired destination tile
  - Press Enter to confirm destination (sets route via `Find.WorldRoutePlanner`)
  - Press Escape to cancel and return to formation dialog
  - After selecting destination, automatically returns to formation dialog
- **Implementation Details**:
  - Patches `Dialog_FormCaravan.PostOpen`, `PostClose`, and `DoWindowContents`
  - Accesses transferables via reflection: `AccessTools.Field(typeof(Dialog_FormCaravan), "transferables")`
  - Calls `Notify_TransferablesChanged()` after quantity adjustments to update mass/food calculations
  - Filters transferables by tab: pawns (Pawn type), items (non-food), travel supplies (food/medicine)
  - Announces current item with position, label, selection status, and available quantity
  - Draws visual "Keyboard Mode Active" indicator in top-left corner
- **Pawn announcements**: Includes name, title, and selection status
- **Item announcements**: Includes label, current count, and maximum available
- **Auto-calculates**: Mass capacity, food duration, travel speed (via RimWorld's native systems)

### Caravan Reformation System

**CaravanFormationState.TriggerReformation()**:
- Allows keyboard-triggered caravan reformation from temporary encounter maps (ambushes, events)
- Keyboard shortcut: **Shift+C** (when on a temporary map)
- Automatically detects if the current map supports reformation:
  - Checks if map is NOT a player home (`!Map.IsPlayerHome`)
  - Verifies the map has a `FormCaravanComp` component
  - Validates reformation is allowed via `FormCaravanComp.CanFormOrReformCaravanNow`
  - Checks for active threats (hostile pawns) that would block reformation
- Opens `Dialog_FormCaravan` with `reform: true` parameter
- **Differences between Form and Reform**:
  - **Form**: Used at player settlements to create new caravans (triggered via C key in world view)
  - **Reform**: Used on temporary maps to continue a caravan's journey after resolving encounters (triggered via Shift+C on map)
  - Reform mode automatically strips corpse equipment and lists pawn inventories separately
  - Route selection is optional in reform mode (uses retained caravan destination if available)
  - Reform mode includes downed pawns, mentally broken pawns, and prisoners in selection
  - After reformation, the temporary map is automatically destroyed
- **Integration**:
  - Triggered from `UnifiedKeyboardPatch` (Priority 6.54)
  - Uses existing `CaravanFormationState` for keyboard navigation (works identically for both form and reform)
  - All tab navigation, pawn/item selection, and destination setting work the same as regular caravan formation
- **Use Case**: After a caravan ambush or random encounter, players can press Shift+C to reform their caravan and continue traveling

### Caravan Stats Viewer

**CaravanStatsState**:
- Displays comprehensive caravan information when I key is pressed with a caravan selected in world view
- Scrollable multi-section information display
- **Sections displayed**:
  1. **Caravan Name and Location**: Name, tile coordinates, latitude/longitude
  2. **Pawns**: Complete list of colonists (with titles and health), animals (grouped by kind), and prisoners
  3. **Mass**: Usage vs. capacity with percentage, immobilization warnings, and detailed capacity breakdown
  4. **Movement**: Current status (moving/resting/stopped/cannot move), speed in tiles per day, and movement blockers
  5. **Food**: Days worth of food, days until spoilage, foraging information
  6. **Destination**: Destination tile, settlement name (if applicable), ETA in days/hours, and arrival action
  7. **Visibility**: Visibility percentage (affects enemy detection chance)
  8. **Trading**: Trading capability status (if applicable)
- **Navigation**:
  - **Up/Down**: Scroll through information sections
  - **Escape**: Close stats viewer
- **Implementation**:
  - Only active in world view when a player-controlled caravan is selected
  - Integrated into WorldNavigationPatch input handling
  - Auto-calculates all stats using RimWorld's native systems (DaysWorthOfFoodCalculator, TilesPerDayCalculator, etc.)
  - Announces position and content via TolkHelper

### Dialog Navigation System

**DialogNavigationState + DialogAccessibilityPatch**:
- Provides keyboard navigation for ALL `Dialog_NodeTree` instances in the game
- This includes: research completion dialogs, quest dialogs, trade dialogs, etc.
- Patches `Dialog_NodeTree.DoWindowContents` (Postfix) to handle keyboard input
- Keyboard navigation:
  - Up/Down: Navigate between dialog options
  - Enter: Execute selected option (calls `DiaOption.Activate()` via reflection)
  - Escape: Handled by RimWorld's default dialog behavior
- Announces dialog text and selected options via TolkHelper
- Draws visual highlight on selected option for sighted users
- Uses reflection to access private `DiaNode` and `DiaOption` fields
- State automatically resets when dialog closes (via `PostClose` patch)
- **Important:** This patch handles keyboard input WITHIN the dialog's DoWindowContents method, separate from UnifiedKeyboardPatch

### Message Box Accessibility

**MessageBoxAccessibilityPatch**:
- Patches `Dialog_MessageBox` to announce confirmation dialogs and warnings
- Handles caravan formation confirmations (low food, no social skill, etc.)
- **PostOpen patch**: Announces message text, title, and available button options
- **DoWindowContents patch**: Handles keyboard input
  - Enter: Confirm (executes buttonAAction)
  - Escape: Cancel/Go Back (executes buttonBAction or cancelAction)
- Announces with high priority to interrupt other speech
- Examples of dialogs handled:
  - Caravan formation warnings ("You have 3.2 days of food. Are you sure?")
  - Game quit confirmations
  - Reset settings confirmations
  - Any Dialog_MessageBox.CreateConfirmation() usage

### Windowless Menu System

**WindowlessFloatMenuState**:
- Replaces RimWorld's `FloatMenu` windows with keyboard-navigable alternatives
- Stores `FloatMenuOption` objects from game's native menu system
- Up/Down arrows navigate, Enter executes, Escape closes
- Used for: order-giving, architect menu, zone creation, work priorities

### Reference to Decompiled Source

The game's source is located at `../decompiled` - consult before making changes:
- **RimWorld namespace** - Game-specific systems (jobs, factions, buildings)
- **Verse namespace** - Core engine (UI, coordinates, logging)
- **Verse.AI namespace** - AI behavior and job system

See `api_reference.md` in the mod directory for detailed namespace breakdown.

## Dependencies

**Mod Dependencies (declared in About.xml):**
- **Harmony** - Runtime patching library (Steam Workshop mod: brrainz.harmony)

**NuGet Packages:**
- **Krafs.Rimworld.Ref** - RimWorld reference assemblies (includes Assembly-CSharp and all Unity modules). This package provides stripped reference assemblies that contain only type signatures, making the project portable without distributing game code. Version mirrors RimWorld version (e.g., 1.6.4633 for RimWorld 1.6). See: https://github.com/krafs/RimRef
- **Lib.Harmony** - Compile-time reference only (runtime provided by Harmony mod)

## Common Keyboard Shortcuts

**Main Menu:**
- Arrow keys: Navigate menu
- Enter: Select menu item

**Mod Manager (from Main Menu → Mods):**
- Up/Down: Navigate mods in current list
- Left/Right: Switch between inactive/active mod columns
- Enter: Toggle mod enable/disable
- Ctrl+Up: Move mod up in load order (active list only)
- Ctrl+Down: Move mod down in load order (active list only)
- M: Open mod settings (if mod has settings)
- I: Read full mod description and info
- S: Save mod changes
- R: Auto-sort mods (resolve load order)
- O: Open mod folder in file explorer
- W: Open Steam Workshop page for mod
- U: Upload mod to Steam Workshop (requires Dev Mode)
- Escape: Close mod manager

**In-Game Navigation:**
- Arrow keys: Move map cursor (announces "not visible" first if drafted pawn cannot see the tile)
- Tab/Shift+Tab: Cycle selected pawns
- R: Toggle draft mode for selected pawn
- ]: Open order menu for selected pawns (includes draft mode orders like "Move here", "Attack", etc. when drafted)
- Enter: Open building inspection menu at cursor position
- A: Open architect (build) menu
- L: Open notification menu (view messages, letters, and alerts)
- Q: Open quest menu (view available, active, and historical quests)
- Z: Open zone menu
- / (forward slash): Open debug menu (requires DevMode enabled in Options → Miscellaneous)
- Shift+C: Reform caravan (on temporary encounter maps after ambushes or events)
- T: Announce time, weather, date, season, and days passed
- Alt+M: Display mood information for selected pawn (mood level, description, and thoughts affecting mood)
- Alt+F: Unforbid all forbidden items on the map
- Escape: Open pause menu

**World Map Navigation (F8 to toggle world view):**
- Arrow keys: Navigate between world tiles (camera-relative directions)
- Home: Jump to home settlement
- End: Jump to nearest player caravan
- Page Up/Down: Cycle through settlements by distance
- S: Open settlement browser (filter by faction relationship)
- I: Show caravan stats (if caravan selected) or read detailed tile information
- C: Form caravan (opens caravan formation dialog when settlement is selected)
- ]: Open order menu for selected caravan (at current cursor tile)
- Escape: Close settlement browser / Return to map view

**Caravan Formation Dialog:**
- Up/Down: Navigate through pawns/items/supplies
- Left/Right: Switch between tabs (Pawns, Items, Travel Supplies)
- +/-: Adjust quantity
- Enter: Toggle selection (pawns) or adjust quantity (items)
- D: Choose destination (opens world map, use arrows to navigate, Enter to confirm)
- T: Send caravan
- R: Reset selections
- Escape: Cancel

**Destination Selection Mode (after pressing D in caravan formation):**
- Arrow keys: Navigate world map
- Enter: Confirm selected tile as destination
- Escape: Cancel and return to formation dialog

**Caravan Stats Viewer (I key with caravan selected in world view):**
- Up/Down: Scroll through information sections
- Escape: Close viewer

**Scanner Navigation (always available):**
- Page Up/Down: Navigate through items in current subcategory
- Ctrl+Page Up/Down: Switch between categories
- Shift+Page Up/Down: Switch between subcategories
- Alt+Page Up/Down: Navigate through individual items in a bulk group
- Home: Jump cursor to current item (or specific bulk item if within group)
- End: Read distance and direction from cursor to item

**Architect/Building Mode:**
- Arrow keys: Navigate placement cursor
- Space: Place building or toggle cell selection (zones)
- Shift+Space: Cancel blueprint at cursor position
- R: Rotate building
- Enter: Confirm placement (zones) or exit placement mode
- Escape: Cancel and exit placement mode

**Within Menus:**
- Up/Down: Navigate options
- Left/Right: Adjust sliders (options menu)
- Enter: Confirm selection
- Escape: Go back / Close menu
- Delete: Delete selected save file (save menu)

**Dialog Navigation (research completion, quests, trade, etc.):**
- Up/Down: Navigate between dialog options
- Enter: Execute selected option
- Escape: Close dialog (default RimWorld behavior)

**Debug Menu (/ forward slash to open, requires DevMode):**
- Up/Down: Navigate through debug actions
- Right arrow: Switch to next tab (Actions → Output → Settings) OR enter submenu
- Left arrow: Switch to previous tab OR go back in category hierarchy
- Enter: Execute selected action
- Escape: Go back to parent menu or close
- Tab: Cycle through actions (native RimWorld feature)
- Filter text field: Type to filter actions by name
- Note: The keybinding can be changed in Options → Controls → Development → "toggle debug actions menu"

**Debug Tools (after activating a tool action):**
- Arrow keys: Move map cursor to target location
- Enter: Execute tool at cursor position
- Escape: Cancel tool

## Code Organization

### Patch Files (~24 files)
Files ending in `*Patch.cs` contain Harmony patches:
- `MainMenuAccessibilityPatch` - Main menu keyboard navigation
- `UnifiedKeyboardPatch` - Central keyboard input handler (includes F9 debug menu shortcut)
- `MapNavigationPatch` - Map cursor movement
- `WorldNavigationPatch` - World map navigation (F8 view, tile selection, caravan orders)
- `ArchitectMenuPatch` - Build menu opening
- `ArchitectPlacementPatch` - Build placement mode
- `ZoneCreationPatch`, `ZoneMenuPatch` - Zone management
- `WorkMenuPatch` - Work priorities UI
- `StorageSettingsMenuPatch` - Storage filters
- `DetailInfoPatch` - Pawn/building info panels
- `TimeControlAccessibilityPatch` - Game speed controls
- `NotificationAccessibilityPatch` - In-game notifications
- `DialogAccessibilityPatch` - Generic dialog navigation (all Dialog_NodeTree instances including research completion)
- `DialogDebugAccessibilityPatch` - Debug menu navigation (Dialog_Debug keyboard support, arrow keys, tab switching)
- `DebugToolAccessibilityPatch` - Debug tool keyboard support (Enter to execute at cursor, Escape to cancel)
- `MessageBoxAccessibilityPatch` - Message box and confirmation dialog announcements (caravan warnings, quit confirmations, etc.)
- `CaravanFormationPatch` - Caravan formation dialog keyboard navigation
- `ForbidTogglePatch` - Forbid/unforbid actions
- `PawnInfoPatch` - Pawn information display
- `ModListPatch` - Mod manager (Page_ModsConfig) keyboard navigation
- `ModSettingsDialogPatch` - Mod settings dialog announcements (Dialog_ModSettings)
- Plus patches for game setup screens (scenario, storyteller, world params, colonist editor, starting site)

### State Files (~26 files)
Files ending in `*State.cs` maintain navigation state for different game screens:
- `ScannerState` - Map item scanner with hierarchical navigation (J key)
- `NotificationMenuState` - Notification viewer for messages, letters, and alerts (L key)
- `QuestMenuState` - Quest browser for available, active, and historical quests (Q key)
- `WorldNavigationState` - World map tile navigation (F8 view, arrow keys, Home/End)
- `SettlementBrowserState` - Settlement browser with faction filtering (S key on world map)
- `CaravanFormationState` - Caravan formation dialog state (pawn/item/supply selection)
- `CaravanStatsState` - Caravan stats viewer (I key with caravan selected in world view)
- `MoodState` - Displays mood information and thoughts for selected pawns (Alt+M)
- `DialogNavigationState` - Dialog navigation state (all Dialog_NodeTree instances including research completion)
- `DebugMenuState` - Debug menu navigation (F9 key, arrow key navigation for debug actions)
- `ModListState` - Mod manager keyboard navigation (Main Menu → Mods, arrow keys, Enter to toggle)

### Helper Files (~6 files)
Utility classes for common operations:
- `TolkHelper` - Direct screen reader integration via Tolk library and NVDA controller
- `EmbeddedAudioHelper` - Load and play custom audio files embedded in DLL
- `PawnInfoHelper` - Pawn data extraction
- `TileInfoHelper` - Map tile information
- `ArchitectHelper` - Build menu construction
- `ScannerHelper` - Scanner item collection and categorization

### Entry Point
- `rimworld_access.cs` - Static class with `[StaticConstructorOnStartup]` attribute for mod initialization

### Mod Folder Structure
```
RimWorld/Mods/RimWorldAccess/
├── About/
│   └── About.xml              # Mod metadata and Harmony dependency
├── Assemblies/
│   └── rimworld_access.dll
├── Tolk.dll                   # Native DLL in mod root
└── nvdaControllerClient64.dll
```

### Build Configuration
- `rimworld_access.csproj` - Project file with:
  - Krafs.Rimworld.Ref NuGet package for RimWorld and Unity references (portable, no local game install required for compilation)
  - Lib.Harmony NuGet package (compile-time only, runtime provided by Harmony mod)
  - Native DLL deployment to mod root (for P/Invoke with explicit LoadLibraryW)
  - Post-build target to deploy mod to RimWorld Mods folder
  - Release target to package mod for distribution
  - Embedded resource configuration for `Sounds/**/*.wav`, `*.ogg`, `*.mp3` files
  - `EnableDefaultCompileItems=false` to exclude decompiled game source in RimWorld subfolder
  - `GamePaths.props` (optional) - Override `RimWorldDir` for custom game install paths (deployment only)

## Development Notes

- The mod logs to RimWorld's log via `Log.Message()`, `Log.Warning()`, and `Log.Error()` (from Verse namespace)
- All patches apply automatically via `harmony.PatchAll()` - no manual registration needed
- State persists across UI redraws, but menus are reconstructed each frame
- RimWorld uses Unity IMGUI (Immediate Mode GUI) - UI is redrawn every frame
- Event consumption via `Event.current.Use()` is critical to prevent default game behavior
- Harmony patches can stack - use `[HarmonyPriority]` attribute to control execution order
- The game runs at `Current.ProgramState` - check for `ProgramState.Playing` vs `ProgramState.Entry` (main menu)
- `Find.*` provides access to game managers: `Find.CurrentMap`, `Find.Selector`, `Find.WindowStack`, `Find.CameraDriver`

## Key RimWorld Types

- `IntVec3` - 3D integer coordinate (primary tile coordinate type)
- `Pawn` - Characters (colonists, animals, enemies)
- `Thing` - Physical objects in the world
- `Building` - Constructed structures
- `Map` - Current game map
- `FloatMenuOption` - Context menu option
- `Designator` - Build tool or order type
- `DesignationCategoryDef` - Category in architect menu
- `BuildableDef` - Definition for buildable structures
- `ThingDef` - Definition for things (materials, items, etc.)
