# RimWorld Access Source Code

## Module Organization

This codebase contains 18 modules organized by game feature:

| Module | Files | Purpose |
|--------|-------|---------|
| Core/ | 2 | Mod entry point and logging |
| ScreenReader/ | 3 | TolkHelper and audio integration |
| Input/ | 1 | Central keyboard input routing (UnifiedKeyboardPatch) |
| MainMenu/ | 19 | Main menu and game setup flow |
| Map/ | 9 | Map navigation, cursor, scanner |
| World/ | 8 | World map, settlements, caravans |
| Building/ | 22 | Construction, zones, areas |
| Inspection/ | 18 | Building/object inspection UI |
| Pawns/ | 25 | Pawn info and character tabs |
| Work/ | 2 | Work priorities and schedules |
| Animals/ | 6 | Animal and wildlife management |
| Prisoner/ | 3 | Prisoner management |
| Quests/ | 3 | Quests and notifications |
| Combat/ | 2 | Combat and targeting |
| Trade/ | 3 | Trading system |
| Research/ | 2 | Research system |
| UI/ | 13 | Generic dialogs and windowless menus |

**Total:** 141 source files organized into 18 granular modules

## Architectural Pattern: State + Patch

Every feature follows this pattern:
- **State class** (`*State.cs`) - Navigation state, IsActive flag, SelectNext/SelectPrevious methods
- **Patch class** (`*Patch.cs`) - Harmony patches that inject keyboard handling into RimWorld UI
- **Helper class** (`*Helper.cs`) - Data extraction and utility functions

## Central Systems

### UnifiedKeyboardPatch (Input/ module)
Central keyboard input handler that routes to all State classes:
- Patches `UIRoot.UIRootOnGUI` at Prefix level
- Priority system: Lower number = higher priority
- Checks `IsActive` flags before routing input
- Calls `Event.current.Use()` to consume events

### TolkHelper (ScreenReader/ module)
Direct screen reader integration used by all modules:
- `TolkHelper.Speak(text, priority)` - Announce to screen reader
- Three priority levels: Low (don't interrupt), Normal (default), High (interrupt)
- P/Invoke to Tolk.dll and nvdaControllerClient64.dll
- Initialized in Core/ module

### MapNavigationState (Map/ module)
Provides cursor position used by 10+ modules:
- `CurrentCursorPosition` (IntVec3) - Map tile cursor
- Arrow key navigation with camera follow
- Jump modes: terrain, buildings, geysers, trees, mineable

## Dependency Graph

```
Core/
  └── ScreenReader/ (TolkHelper)
        └── Input/ (UnifiedKeyboardPatch)
              ├── MainMenu/
              ├── Map/ → [Building, Inspection, Quests, Combat]
              ├── Pawns/ → [Work, Prisoner]
              ├── World/ → [Quests]
              ├── Animals/
              ├── Trade/
              ├── Research/
              └── UI/ → [All modules]
```

## Finding Code

### By Game Feature
1. Identify the game screen (e.g., "building inspection", "caravan formation")
2. Look in corresponding module directory
3. Files follow naming: `<Feature>State.cs`, `<Feature>Patch.cs`, `<Feature>Helper.cs`

### By Keyboard Shortcut
See Input/UnifiedKeyboardPatch.cs for complete keyboard routing with priority levels

### By Screen Reader Announcement
Search for `TolkHelper.Speak()` calls across all State classes

## Building and Deploying

**Build:**
```bash
dotnet build
```

**Output:**
- DLL: `bin/Debug/net472/rimworld_access.dll`
- Auto-deploys to: `RimWorld/Mods/RimWorldAccess/`

**Test:**
1. Launch RimWorld
2. Check console: "[RimWorld Access] Total patches applied: <count>"
3. Test keyboard navigation per module

## Contributing

### Adding New Features
1. Choose appropriate module (or create new module directory)
2. Follow State + Patch pattern:
   - Create `<Feature>State.cs` with IsActive flag
   - Create `<Feature>Patch.cs` with Harmony patch
   - Add input routing to UnifiedKeyboardPatch.cs
3. Use `TolkHelper.Speak()` for screen reader announcements
4. Update module's CLAUDE.md

### Module Documentation
Each module has detailed CLAUDE.md with:
- Architecture patterns
- Keyboard shortcuts
- Dependencies
- Testing checklist

See individual module folders for details.

## Key Implementation Notes

### Harmony Patching
- All patches apply automatically via `harmony.PatchAll()` in rimworld_access.cs
- Use `[HarmonyPriority]` attribute to control patch execution order
- Prefix patches can block original method with `return false`
- Postfix patches run after original method completes

### Screen Reader Integration
- TolkHelper uses P/Invoke to call native Tolk.dll functions
- Fallback chain: Detected screen reader → Direct NVDA → SAPI
- All State classes announce selections via `TolkHelper.Speak()`

### Input Handling
- UnifiedKeyboardPatch checks all State.IsActive flags
- Event.current.Use() prevents default game behavior
- Priority system ensures correct input routing order

### State Lifecycle
1. Patch's PostOpen/Postfix initializes state
2. State.IsActive = true
3. UnifiedKeyboardPatch routes input to state
4. State.Close() resets state, IsActive = false

## Module-Specific Notes

### Cross-Module Dependencies
- **MapNavigationState.CurrentCursorPosition** - Used by Building, Inspection, Quests, Combat, Scanner
- **PawnSelectionState** - Integrates with scanner (comma/period keys)
- **WindowlessDialogState** - Generic dialog handler used across UI components

### Shared Concepts
- **Windowless Menus** - Keyboard-navigable alternatives to RimWorld's FloatMenu windows
- **IsActive Flags** - All State classes have this to prevent input conflicts
- **TolkHelper Announcements** - Every navigation action speaks to screen reader

## Common Patterns

### Opening a State
```csharp
MyFeatureState.Open();
TolkHelper.Speak("My feature opened", SpeechPriority.Normal);
```

### Navigating in State
```csharp
MyFeatureState.SelectNext(); // Wraps around at end
TolkHelper.Speak(currentItem.Label, SpeechPriority.Low);
```

### Closing a State
```csharp
MyFeatureState.Close();
MyFeatureState.IsActive = false;
```

### Adding to UnifiedKeyboardPatch
```csharp
// Priority 5: My Feature (shortcut: F key)
if (MyFeatureState.IsActive)
{
    if (key == KeyCode.F)
    {
        MyFeatureState.ExecuteSelected();
        Event.current.Use();
        return;
    }
}
```
