# World Module

## Purpose
Keyboard navigation for world map (F8 view), settlement browsing, and caravan formation/management.

## Files
**Patches:** WorldNavigationPatch.cs, CaravanFormationPatch.cs, MessageBoxAccessibilityPatch.cs
**States:** WorldNavigationState.cs, SettlementBrowserState.cs, QuestLocationsBrowserState.cs, CaravanFormationState.cs, CaravanStatsState.cs
**Helpers:** WorldInfoHelper.cs

## Key Shortcuts
- **Arrow Keys** - Navigate world tiles
- **Home** - Jump to home settlement
- **End** - Jump to nearest caravan
- **Page Up/Down** - Cycle settlements by distance
- **S** - Settlement browser (filter by faction)
- **I** - Caravan stats / Tile info
- **C** - Form caravan
- **]** - Caravan orders
- **D** - Choose destination (in caravan formation)

## Architecture
WorldNavigationState tracks current world tile. Camera-relative directional navigation. Settlement browser provides faction filtering.

## Dependencies
**Requires:** ScreenReader/, Input/, Map/ (cursor sync)
**Used by:** Quests/ (quest jump targets)

## Testing
- [ ] Arrow keys navigate world tiles
- [ ] Settlement browser filters work
- [ ] Caravan formation keyboard nav works
- [ ] Destination selection functional
