# Core Module

## Purpose
Mod entry point and initialization. Applies all Harmony patches and sets up TolkHelper for screen reader integration.

## Files in This Module

### Entry Point (1 file)
- **rimworld_access.cs** - Static class with `[StaticConstructorOnStartup]` attribute, initializes Harmony and TolkHelper

### Utilities (1 file)
- **ModLogger.cs** - Centralized logging utility for debug messages

## Key Architecture

### State Management
No state management - this module only initializes the mod.

### Input Handling
No direct input handling - this module sets up the infrastructure used by other modules.

### Dependencies
**Requires:** None (loaded first)
**Used by:** All modules (provides Harmony patching and TolkHelper initialization)

## Initialization Flow

1. **Static Constructor Runs** - `[StaticConstructorOnStartup]` triggers when RimWorld loads
2. **Create Harmony Instance** - `new Harmony("com.rimworldaccess.mainmenukeyboard")`
3. **Apply All Patches** - `harmony.PatchAll()` automatically finds and applies all `[HarmonyPatch]` classes
4. **Initialize TolkHelper** - `TolkHelper.Initialize()` loads screen reader libraries
5. **Log Success** - Outputs patch count to RimWorld console

## Integration with Core Systems

### UnifiedKeyboardPatch
Not applicable - Core module initializes Harmony which enables patching.

### TolkHelper (Screen Reader)
TolkHelper is initialized in this module's static constructor. All other modules depend on this initialization.

### MapNavigationState
Not applicable

## Common Patterns

### Entry Point Pattern
```csharp
[StaticConstructorOnStartup]
public static class RimWorldAccessMod
{
    static RimWorldAccessMod()
    {
        // Initialization code runs automatically
    }
}
```

## RimWorld Integration

### Harmony Patches
- Uses `harmony.PatchAll()` to automatically discover and apply all patches
- Harmony ID: `"com.rimworldaccess.mainmenukeyboard"`

### Reflection Usage
None in this module

### Game Systems Used
- `Application.quitting` event for cleanup (TolkHelper.Shutdown())

## Testing Checklist
- [ ] Mod loads without errors (check RimWorld console)
- [ ] Harmony patches apply successfully (console shows patch count)
- [ ] TolkHelper initializes correctly (screen reader announcements work)
- [ ] No conflicts with other mods
