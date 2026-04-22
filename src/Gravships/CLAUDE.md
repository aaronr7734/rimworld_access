# Gravships Module

## Purpose
Keyboard accessibility for gravship destination selection and supporting utilities. Landing placement is handled by the Building module's existing designator system.

## Files

**States:** GravshipDestinationState.cs
**Patches:** GravshipPatch.cs
**Helpers:** GravshipHelper.cs

## Key Shortcuts

### Destination Selection (World Map)
| Key | Action |
|-----|--------|
| Arrow Keys | Navigate world tiles (WorldNavigationState) |
| PageUp/PageDown | Scanner navigation |
| Enter | Confirm destination |
| Escape | Cancel targeting |
| F | Announce fuel status |

### Landing Placement (Map)
Handled by Building module's ShapePlacementState with Manual shape:
| Key | Action |
|-----|--------|
| Arrow Keys | Move cursor to position gravship |
| R/Shift+R | Rotate gravship facing |
| Space | Set position |
| Enter | Confirm placement |
| Escape | Cancel |

## Architecture

### Destination Selection
- `CompPilotConsole.StartChoosingDestination_NewTemp()` → opens TilePicker on world map
- `GravshipPatch` postfix activates `GravshipDestinationState`
- State integrates with WorldNavigationState/WorldScannerState for fuel cost announcements
- Uses `GravshipUtility.TryGetPathFuelCost()` for cost calculation
- Invokes TilePicker's validator and tileChosen callbacks via reflection on Enter

### Landing Placement
- `Designator_MoveGravship` detected in `DesignatorManagerPatch`
- Routed to `ShapePlacementState` with Manual shape
- Rotation handled in `ArchitectPlacementPatch.HandleRotateKey()` via marker's `GravshipRotation`
- Standard `CanDesignateCell()`/`DesignateSingleCell()` API

### Launch Ritual Checkboxes
Handled by the Rituals module via `GravshipLaunchAdapter`. The adapter exposes the
three checkboxes (`forceVisitorsToLeave`, `boardColonyAnimals`, `boardColonyMechs`)
as `LordJobExtraToggle` items appended to the role list. Enter/Space toggles them.
See `src/Rituals/CLAUDE.md` for the full Lord-job dialog architecture.

## Dependencies
**Requires:** ScreenReader/, Input/, World/ (navigation, scanner), Building/ (placement)
**Used by:** Rituals/ (gravship adapter)

## Priority in UnifiedKeyboardPatch
- 0.365: GravshipDestinationState (after TransportPodLaunchState at 0.36)

## Key Game Classes
| Class | Purpose |
|-------|---------|
| `CompPilotConsole` | Launch interface, destination selection |
| `Building_GravEngine` | Central engine with fuel/component/substructure data |
| `TilePicker` | World map tile selection (different from WorldTargeter) |
| `Designator_MoveGravship` | Landing placement designator |
| `GravshipLandingMarker` | Landing position marker |
| `Dialog_BeginGravshipLaunch` | Launch ritual dialog with checkboxes |
| `GravshipUtility` | Fuel cost calculation |

## Testing Checklist
- [ ] Destination targeting opens from pilot console gizmo
- [ ] World navigation announces fuel costs for each tile
- [ ] Scanner announces fuel costs for destinations
- [ ] Enter confirms valid destination
- [ ] Escape cancels targeting and returns to map
- [ ] F announces fuel status summary
- [ ] Out-of-range tiles announce error
- [ ] Landing designator enters ShapePlacementState
- [ ] R/Shift+R rotates gravship facing
- [ ] CanDesignateCell validation announces blocking reasons
- [ ] Space/Enter confirms landing position
- [ ] Launch ritual shows checkboxes at bottom of role list
- [ ] Enter/Space toggles checkboxes
- [ ] Checkbox announcements include label + checked/unchecked + tooltip
