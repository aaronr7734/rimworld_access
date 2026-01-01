# UI Module

## Purpose
Generic dialog navigation and windowless menu systems used across all modules.

## Files
**Patches:** DialogInterceptionPatch.cs, MessageBoxAccessibilityPatch.cs
**States:** WindowlessDialogState.cs, WindowlessFloatMenuState.cs, WindowlessPauseMenuState.cs, WindowlessSaveMenuState.cs, WindowlessOptionsMenuState.cs, WindowlessConfirmationState.cs, GiveNameDialogState.cs
**Utilities:** Dialog_NameAllowedArea.cs, DialogElementExtractor.cs, StatsHelper.cs

## Key Shortcuts
- **Escape** - Pause menu
- **Arrow Keys** - Navigate menus
- **Enter** - Confirm
- **Delete** - Delete save file (in save menu)

## Architecture
Windowless menus replace RimWorld's FloatMenu windows with keyboard-navigable alternatives. DialogInterceptionPatch handles all Dialog_NodeTree instances.

## Dependencies
**Requires:** ScreenReader/, Input/
**Used by:** All modules (confirmation dialogs, float menus used everywhere)

## Common Patterns
### Windowless Menu Pattern
```csharp
WindowlessFloatMenuState.Open(options);
// Up/Down navigate, Enter executes, Escape closes
```

### Dialog Navigation
All Dialog_NodeTree instances automatically get keyboard navigation via DialogInterceptionPatch.

## Testing
- [ ] Pause menu accessible
- [ ] Save/load menu functional
- [ ] Options menu navigable
- [ ] Confirmation dialogs work
- [ ] Float menus accessible
