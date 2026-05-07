# Input Module

## Purpose
Central keyboard input routing system that coordinates all keyboard accessibility features across the mod.

## Files in This Module

### Central Input Handler
- **UnifiedKeyboardPatch.cs** - Patches `UIRoot.UIRootOnGUI` at Prefix level with High priority, handles ALL keyboard input routing
- **KeyboardHelper.cs** - Layout remapping helpers (AltGr `]`, AZERTY `*`, etc.) and `IsCtrlHeld` / `IsAltHeld`
- **KeyboardIsolationPatch.cs** - Blocks game keybindings while overlays are active
- **MapArrowKeyHandler.cs** - Map arrow-key navigation

### Unified Text Input Pipeline (`TextInput/`)
- **TextFieldSpec.cs** - Immutable per-field constraints (max length, allowed-chars regex, grammar specials, filename rules, custom validator). Static factories: `ForRimWorldDialog`, `ForIRenameable`, `Unrestricted`.
- **TextFieldValidator.cs** - Pure validation returning `ValidationResult`. `AnnounceRejection` produces the localized speech string.
- **TextInputController.cs** - One editing session: Begin/HandleCharacter/HandleBackspace/HandleDelete/HandleCopy/HandlePaste/HandleEnter/HandleEscape + cursor-review methods (HandleArrowLeft/Right, HandleHome/End — each takes `shift` and `ctrl` modifiers). `HandleEvent(Event)` dispatches IMGUI key events through the full set. Modal mode registers with `TextInputManager` and owns all keys including cursor nav; embedded mode leaves arrow keys for the surrounding state (cursor nav is only reachable if the site explicitly forwards arrow events).
- **TextInputManager.cs** - Static single-active-session registry. UnifiedKeyboardPatch's priority -1.6 dispatch routes ALL keys to `Active` when set.
- **RimWorldDialogIntrospector.cs** - Reflection-cached extractor for vanilla dialog constraints (`Dialog_Rename<T>.MaxNameLength` + `NameIsValid`, `Dialog_GiveName.FirstCharLimit` + `IsValidName`, etc.). Pawn-name fields use `CharacterCardUtility.ValidNameRegex`.
- **ITypeaheadConsumer.cs** - Delegate-based typeahead consumer (`TypeaheadConsumer` + `TypeaheadDispatcher`). Decouples typeahead detection from KeyCode gating — fixes the OlegTheSnowman Cyrillic-layout bug.
- **TypeaheadConsumerRegistry.cs** - Registers every typeahead-consuming State at mod init.

## Key Architecture

### State Management
Does not manage its own state - routes input to all other State classes based on their IsActive flags.

### Input Handling
**Priority System** - Processes input in priority order (lower number = higher priority):
- Priority -1.6: `TextInputManager.Active` — modal text-edit session swallows ALL keys (chars + Enter/Escape/Backspace + Ctrl+C/V). Replaces the old per-state -1 / -0.95 / -0.94 modal blocks.
- Priority -1.5: Layout-aware character dispatch. `TypeaheadDispatcher.TryDispatchChar` walks registered consumers and routes the layout-aware `Event.character` to the first active one. Per-state branches handle embedded text fields below.
- Priority 0: Settlement browser, caravan stats/destination
- Priority 1-2: Confirmations (delete, general)
- Priority 2.5-4.8: Menus (area, trade, save, pause, options, research, quests, health, prisoner)
- Priority 5: Float menus (order-giving)
- Priority 6-7: Gameplay shortcuts (draft, mood, unforbid, schedule, gizmos, notifications)
- Priority 8: Pause menu (Escape key)
- Priority 9: Enter key (inspection)
- Priority 10: Right bracket (colonist orders)

### Text Input Architecture

All text-edit sites in the mod (renames, scenario editor, pawn naming, xenotype/xenogerm, etc.) flow through `TextInputController`:

```csharp
// Modal — controller registers as TextInputManager.Active and the priority -1.6
// dispatch in UnifiedKeyboardPatch routes every key to it. Architect/scanner/etc.
// shortcuts are blocked.
private static readonly TextInputController controller = new TextInputController();

public static void Open(IRenameable target)
{
    var spec = TextFieldSpec.ForIRenameable(target, "RimWorldAccess.TextInput.LabelZone");
    controller.Begin(target.RenamableLabel, spec, OnConfirm, OnCancel, replaceOnType: true);
}

private static void OnConfirm(string newName) { target.RenamableLabel = newName; }
private static void OnCancel() { TolkHelper.Speak("Cancelled"); }
```

For text fields embedded inside a larger menu (where Up/Down should still navigate the surrounding list), pass `modal: false` to `Begin`. The surrounding state's input handler then forwards Enter/Escape/Backspace/chars/Ctrl+C/V to the controller's individual methods.

### Typeahead Architecture

Typeahead-consuming States register with `TypeaheadDispatcher` at mod init (see `TypeaheadConsumerRegistry`). The priority -1.5 character handler dispatches the layout-aware `Event.character` to the first registered consumer whose `IsActive()` returns true. This works on any keyboard layout because the gate is `char.IsLetter(c) || char.IsDigit(c)`, not a `KeyCode.A..Z` range — fixing the bug where Cyrillic/Ukrainian "б" was reported by Unity as `KeyCode.Comma` and silently dropped.

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
- **Alt+A** - Assign allowed area to selected pawn
- **Alt+F** - Unforbid all items
- **C** - Reform caravan
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
