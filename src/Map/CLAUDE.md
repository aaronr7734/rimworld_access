# Map Module

## Purpose
Provides keyboard navigation for the map view, including cursor movement, scanner system for finding objects, and tile information display.

## Files in This Module

### Patches (3 files)
- **MapNavigationPatch.cs** - Arrow key cursor movement
- **DetailInfoPatch.cs** - Tile info display (keys 1-5)
- **TimeControlAccessibilityPatch.cs** - Time speed announcements

### States (5 files)
- **MapNavigationState.cs** - Cursor position, jump modes
- **ScannerState.cs** - Hierarchical item scanner
- **TimeAnnouncementState.cs** - Time/date/weather info
- **PlaySettingsMenuState.cs** - Play settings overlay

### Helpers (2 files)
- **ScannerHelper.cs** - Collects and categorizes map items
- **TileInfoHelper.cs** - Extracts tile information

## Key Architecture

### State Management
MapNavigationState maintains an IntVec3 cursor position that persists across frames. Scanner maintains hierarchical category/subcategory position.

### Input Handling
Arrow keys handled at Priority 9-10 in UnifiedKeyboardPatch. Scanner uses Page Up/Down (always available).

### Dependencies
**Requires:** ScreenReader/, Input/
**Used by:** Building/, Inspection/, Quests/, Combat/ (all use cursor position)

## Keyboard Shortcuts
- **Arrow Keys** - Move map cursor (announces tile contents)
- **Page Up/Down** - Scanner navigation (items in current subcategory)
- **Ctrl+Page Up/Down** - Switch scanner categories
- **Shift+Page Up/Down** - Switch scanner subcategories
- **Alt+Page Up/Down** - Navigate within bulk item groups
- **Home** - Jump cursor to scanned item
- **End** - Read distance/direction to scanned item
- **T** - Announce time, weather, date
- **1-5** - Display different tile info types

## Integration with Core Systems

### UnifiedKeyboardPatch
MapNavigationState.IsActive checked to enable map-specific keys. Scanner always available during map view.

### TolkHelper (Screen Reader)
Announces tile contents on every cursor move. Scanner announces items with distance and category info.

### MapNavigationState
This module PROVIDES MapNavigationState.CurrentCursorPosition used by 10+ other modules.

## Common Patterns

### Cursor Movement
```csharp
MapNavigationState.MoveCursor(direction);
Find.CameraDriver.JumpToCurrentMapLoc(position);
TolkHelper.Speak(TileInfoHelper.GetTileDescription(position));
```

### Scanner Navigation
```csharp
ScannerState.SelectNext(); // Within subcategory
TolkHelper.Speak($"{item.Label} - {distance} tiles");
```

## RimWorld Integration

### Harmony Patches
- Patches `PlaySettings.DoPlaySettingsGlobalControls` for map navigation
- Patches `TimeControls.DoTimeControlsGUI` for time announcements

### Reflection Usage
Limited - mostly uses public RimWorld APIs

### Game Systems Used
- `Find.CurrentMap` - Current active map
- `Find.CameraDriver` - Camera positioning
- `Find.Selector` - Selected objects
- `IntVec3` - RimWorld's 3D integer coordinate type

## Scanner System

**11 Categories:**
1. Colonists (Player Pawns, NPCs, Mechanoids)
2. Tame Animals
3. Wild Animals
4. Buildings (Walls/Doors, Other Buildings)
5. Trees (Harvestable, Non-Harvestable)
6. Plants (Harvestable, Debris)
7. Items (All Items, Forbidden Items)
8. Mineable Tiles
9. Orders (Construction, Haul, Hunt, Mine, etc.)

**Features:**
- Hierarchical navigation (category → subcategory → item)
- Items sorted by distance (closest first)
- Bulk grouping for identical items
- Auto-skip empty categories

## Testing Checklist
- [ ] Arrow keys move cursor correctly
- [ ] Camera follows cursor movement
- [ ] Tile contents announced accurately
- [ ] Scanner finds all item types
- [ ] Scanner categories/subcategories navigate correctly
- [ ] Home key jumps cursor to scanned item
- [ ] Time announcement works (T key)
- [ ] Order display at cursor works
