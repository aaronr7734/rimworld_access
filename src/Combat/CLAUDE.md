# Combat Module

## Purpose
Combat log viewing and targeting announcements.

## Files
**Patches:** TargetingPatch.cs
**States:** CombatLogState.cs

## Key Shortcuts
- **Alt+B** - Open combat log

## Architecture
CombatLogState provides scrollable combat history. TargetingPatch announces target acquisition.

## Dependencies
**Requires:** ScreenReader/, Input/, Pawns/ (pawn's combat log)

## Testing
- [ ] Combat log accessible
- [ ] Targeting announcements work
