# Animals Module

## Purpose
Tame animal and wildlife management.

## Files
**Patches:** AnimalsMenuPatch.cs, WildlifeMenuPatch.cs
**States:** AnimalsMenuState.cs, WildlifeMenuState.cs
**Helpers:** AnimalsMenuHelper.cs, WildlifeMenuHelper.cs

## Key Shortcuts
- **Arrow Keys** - Navigate animal lists
- **Enter** - Select action

## Architecture
Separate menus for tame animals (training, slaughter, zones) and wildlife (hunt, tame, info).

## Dependencies
**Requires:** ScreenReader/, Input/, Map/ (animal positions)

## Testing
- [ ] Animals menu accessible
- [ ] Wildlife menu functional
- [ ] Animal actions work correctly
