# Work Module

## Purpose
Work priority management and schedule editing.

## Files
**Patches:** WorkMenuPatch.cs
**States:** WorkMenuState.cs

## Key Shortcuts
- **Arrow Keys** - Navigate work priority grid (pawn rows, work columns)
- **1-4** - Set priority level
- **0** - Disable work type

## Architecture
2D grid navigation (pawn rows × work columns). Manual priority mode support. Schedule handled by WindowlessScheduleState in Pawns/ module.

## Dependencies
**Requires:** ScreenReader/, Input/, Pawns/ (pawn list)

## Testing
- [ ] Work grid navigates correctly
- [ ] Priority setting works
- [ ] Manual priority mode supported
