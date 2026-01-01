# Input Module

## Purpose
Central keyboard input routing system that coordinates all keyboard accessibility features across the mod.

## Files in This Module

### Central Input Handler (1 file)
- **UnifiedKeyboardPatch.cs** - Patches `UIRoot.UIRootOnGUI` at Prefix level with High priority, handles ALL keyboard input routing

## Key Architecture

### State Management
Does not manage its own state - routes input to all other State classes based on their IsActive flags.

### Input Handling
**Priority System** - Processes input in priority order (lower number = higher priority):
- Priority -1: Zone rename (blocks everything)
- Priority 0: Settlement browser, caravan stats/destination
- Priority 1-2: Confirmations (delete, general)
- Priority 2.5-4.8: Menus (area, trade, save, pause, options, research, quests, health, prisoner)
- Priority 5: Float menus (order-giving)
- Priority 6-7: Gameplay shortcuts (draft, mood, unforbid, schedule, gizmos, notifications)
- Priority 8: Pause menu (Escape key)
- Priority 9: Enter key (inspection)
- Priority 10: Right bracket (colonist orders)

### Dependencies
**Requires:** All State modules (checks their IsActive flags)
**Used by:** None (top-level input handler)

## Keyboard Shortcuts
ALL keyboard shortcuts route through this module. Key bindings include:
- **Escape** - Open pause menu
- **Enter** - Building inspection
- **]** - Colonist orders
- **A** - Architect menu
- **L** - Notifications
- **Q / F7** - Quests
- **S** - Settlement browser (world map)
- **I** - Caravan stats / Inspection menu
- **T** - Time/weather announcement
- **Alt+M** - Mood info
- **Alt+H** - Health info
- **Alt+N** - Needs info
- **Alt+F** - Unforbid all items
- **Shift+C** - Reform caravan
- **F2** - Schedule
- **F3** - Assign menu
- **F6** - Research
- **Page Up/Down** - Scanner navigation

## Integration with Core Systems

### UnifiedKeyboardPatch
This IS the UnifiedKeyboardPatch - central integration point for all keyboard input

### TolkHelper (Screen Reader)
Calls TolkHelper.Speak() for various global shortcuts (time announcement, unforbid confirmation)

### MapNavigationState
Checks MapNavigationState.IsActive to determine if map-specific keys should be processed

## Common Patterns

### Adding a New Shortcut
```csharp
// Priority 5: My Feature (K key)
if (key == KeyCode.K && !Event.current.shift && !Event.current.control)
{
    MyFeatureState.Open();
    Event.current.Use();
    return;
}
```

### Checking State IsActive
```csharp
if (WindowlessFloatMenuState.IsActive)
{
    // Route input to float menu
    // ...
    Event.current.Use();
    return;
}
```

### Event Consumption
```csharp
Event.current.Use();  // Prevents default game behavior
return;  // Exit early to prevent further processing
```

## RimWorld Integration

### Harmony Patches
- Patches `UIRoot.UIRootOnGUI` with `[HarmonyPrefix]`
- Runs at High priority (before most other patches)

### Reflection Usage
Extensive use of AccessTools to call private methods and access private fields across many RimWorld systems

### Game Systems Used
- `Event.current` - Unity IMGUI event system
- `Find.*` - RimWorld's game managers
- Various State classes from all modules

## Priority System Design

The priority system ensures correct input routing:
1. **Highest priority (lowest numbers)**: Modal dialogs that need exclusive input
2. **Medium priority**: Menus and UI overlays
3. **Lowest priority (highest numbers)**: Global shortcuts that should only work when nothing else is active

This prevents conflicts like accidentally opening the architect menu while typing in a text field.

## Testing Checklist
- [ ] All keyboard shortcuts work correctly
- [ ] Priority system prevents input conflicts
- [ ] Event.current.Use() prevents unwanted game actions
- [ ] State.IsActive checks work for all modules
- [ ] No infinite loops or performance issues
- [ ] Shortcuts don't conflict with vanilla RimWorld keys
