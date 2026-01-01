# Building Module

## Purpose
Construction, architect menu, zones, and areas management.

## Files
**Patches:** ArchitectMenuPatch.cs, ArchitectPlacementPatch.cs, ZoneMenuPatch.cs, ZoneCreationPatch.cs, AreaPatch.cs, ForbidTogglePatch.cs
**States:** ArchitectState.cs, ZoneCreationState.cs, ZoneRenameState.cs, ZoneSettingsMenuState.cs, AreaPaintingState.cs, WindowlessAreaState.cs, PlantSelectionMenuState.cs, (plus 8 component states)
**Helpers:** ArchitectHelper.cs, BuildingComponentsHelper.cs

## Key Shortcuts
- **A** - Open architect menu
- **Arrow Keys** - Navigate menu, placement cursor
- **Space** - Place building / Toggle zone cell
- **Shift+Space** - Cancel blueprint
- **R** - Rotate building
- **Enter** - Confirm / Exit placement
- **Escape** - Cancel

## Architecture
ArchitectState uses enum modes: CategorySelection → ToolSelection → MaterialSelection → PlacementMode. Zone creation integrates with map cursor.

## Dependencies
**Requires:** ScreenReader/, Input/, Map/ (cursor position)

## Testing
- [ ] Architect menu navigates correctly
- [ ] Building placement works
- [ ] Zone creation functional
- [ ] Area management accessible
