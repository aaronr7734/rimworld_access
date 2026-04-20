# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

RimWorld Access is a C# mod for RimWorld that provides screen reader accessibility. It uses Harmony patches to inject keyboard navigation into RimWorld's UI and the Prism library for cross-platform screen reader integration (NVDA, JAWS, VoiceOver, Orca, SAPI, and more).

**Technology Stack:**
- .NET Framework 4.7.2
- HarmonyLib 2.3.3 (runtime patching)
- Prism (screen reader integration via P/Invoke — supports Windows, macOS, Linux)
- RimWorld 1.6 assemblies

## Building and Testing
### Build Commands

```bash
# Build and auto-deploy to RimWorld/Mods/RimWorldAccess/
dotnet build

# Build release package to release/RimWorldAccess/
dotnet build -c Release

# Update Prism native libraries to latest release
dotnet msbuild -t:UpdatePrism
```

**Build Output:**
- DLL: `bin/Debug/net472/rimworld_access.dll`
- Auto-deploys to: `$(RimWorldDir)\Mods\RimWorldAccess\Assemblies\`
- Native Prism libraries (prism.dll / libprism.dylib / libprism.so) copied to mod root

## Code Architecture

### Core Pattern: State + Patch

Every feature follows this architecture:

1. **State class** (`*State.cs`)
   - Maintains navigation state (selected index, list of items, etc.)
   - `IsActive` flag checked by UnifiedKeyboardPatch
   - Methods: `Open()`, `Close()`, `SelectNext()`, `SelectPrevious()`
   - Calls `TolkHelper.Speak()` to announce selections

2. **Patch class** (`*Patch.cs`)
   - Harmony patches that intercept RimWorld methods
   - Initializes State when UI opens (PostOpen/Postfix)
   - Resets State when UI closes
   - May inject accessibility into rendering code

3. **Helper class** (`*Helper.cs`)
   - Data extraction utilities
   - Reusable functions for formatting announcements
   - No state management

### Module Organization

The codebase is organized into 18 modules by game feature:

| Module | Files | Purpose |
|--------|-------|---------|
| **Core/** | 2 | Mod entry point, Harmony initialization |
| **ScreenReader/** | 5 | Prism screen reader integration and audio |
| **Input/** | 1 | UnifiedKeyboardPatch - central input router |
| **MainMenu/** | 19 | Main menu and game setup flow |
| **Map/** | 9 | Map navigation, cursor, scanner |
| **World/** | 8 | World map, settlements, caravans |
| **Building/** | 23 | Construction, zones, areas |
| **Inspection/** | 24 | Building/object inspection UI, Info Card |
| **Pawns/** | 25 | Pawn info and character tabs |
| **Work/** | 2 | Work priorities and schedules |
| **Animals/** | 6 | Animal and wildlife management |
| **Prisoner/** | 3 | Prisoner management |
| **Quests/** | 3 | Quests and notifications |
| **Combat/** | 2 | Combat and targeting |
| **Trade/** | 3 | Trading system |
| **Research/** | 2 | Research system |
| **UI/** | 13 | Generic dialogs and windowless menus |

Each module has its own `CLAUDE.md` with detailed documentation.

### Central Systems

**UnifiedKeyboardPatch** (`Input/UnifiedKeyboardPatch.cs`)
- Central keyboard input router for ALL accessibility features
- Patches `UIRoot.UIRootOnGUI` at Prefix level
- Priority system (lower number = higher priority, range -1 to 10)
- Checks `IsActive` flags before routing to State classes
- Calls `Event.current.Use()` to consume events and prevent default game behavior

**TolkHelper** (`ScreenReader/TolkHelper.cs`)
- Cross-platform screen reader integration via Prism library
- `TolkHelper.Speak(text, priority)` used by all modules
- Three priorities: Low (don't interrupt), Normal, High (interrupt)
- Backed by: `NativeLibraryLoader.cs` (cross-platform DLL loading) and `PrismNative.cs` (Prism C API bindings)
- Initialized in `Core/rimworld_access.cs`

**MapNavigationState** (`Map/MapNavigationState.cs`)
- Provides `CurrentCursorPosition` (IntVec3) used by 10+ modules
- Arrow key navigation with camera follow
- Jump modes for terrain features

### Dependency Graph

```
Core/rimworld_access.cs (entry point)
  └── ScreenReader/TolkHelper (initialize)
        └── Input/UnifiedKeyboardPatch (routes to all modules)
              ├── MainMenu/
              ├── Map/ → [Building, Inspection, Quests, Combat]
              ├── Pawns/ → [Work, Prisoner]
              ├── World/ → [Quests]
              ├── Animals/
              ├── Trade/
              ├── Research/
              └── UI/ → [All modules]
```

## Common Development Tasks

### Adding a New Feature

1. **Choose module directory** (or create new one under `src/`)
2. **Create State class:**
   ```csharp
   public static class MyFeatureState
   {
       public static bool IsActive { get; set; }
       private static int selectedIndex = 0;

       public static void Open()
       {
           IsActive = true;
           TolkHelper.Speak("Feature opened", SpeechPriority.Normal);
       }

       public static void Close()
       {
           IsActive = false;
       }
   }
   ```

3. **Create Patch class:**
   ```csharp
   [HarmonyPatch(typeof(RimWorldClass))]
   [HarmonyPatch("MethodName")]
   public static class MyFeaturePatch
   {
       [HarmonyPostfix]
       public static void Postfix()
       {
           MyFeatureState.Open();
       }
   }
   ```

4. **Add input routing to UnifiedKeyboardPatch.cs:**
   ```csharp
   // Priority 5: My Feature (K key)
   if (MyFeatureState.IsActive && key == KeyCode.K)
   {
       MyFeatureState.ExecuteAction();
       Event.current.Use();
       return;
   }
   ```

5. **Update module's CLAUDE.md**

### Modifying Existing Features

1. **Find the module** - Use module table above or search by feature name
2. **Read module's CLAUDE.md** - Understand dependencies and patterns
3. **Identify files:**
   - State class for navigation logic
   - Patch class for Harmony integration
   - Helper class for utilities
4. **Test thoroughly** - Verify screen reader announcements and keyboard navigation

### Finding Code

**By game feature:** Navigate to corresponding module directory (e.g., trading → `Trade/`)

**By keyboard shortcut:** Check `Input/UnifiedKeyboardPatch.cs` for complete routing

**By screen reader announcement:** Search for `TolkHelper.Speak()` calls
**RimWorld's decompiled code**: A decompiled copy of RimWorld is located at `./decompiled`. Search this code before making changes that require integration with the game's methods.  

## Building Menus and TreeViews

**See [`src/docs/menu-treeview.md`](src/docs/menu-treeview.md) for complete templates and reference.**

All menus and treeviews must use:
- `MenuHelper` for navigation (respects WrapNavigation, AnnouncePosition settings)
- `TypeaheadSearchHelper` for search functionality
- Standard keyboard patterns (Up/Down/Home/End, Escape to clear search, etc.)

**Quick Reference:**
- **Flat Menu:** Up/Down, Home/End, typeahead search, position announcements
- **TreeView:** Above + Left/Right for collapse/expand, Ctrl+Home/End for absolute navigation, level announcements

**Examples:** `ScenarioNavigationState.cs`, `ArchitectTreeState.cs`

## Harmony Patching Notes

- All patches auto-apply via `harmony.PatchAll()` in `Core/rimworld_access.cs`
- Use `[HarmonyPriority]` to control patch execution order (only needed for conflicts)
- **Prefix patches:** Run before original method, can block execution with `return false`
- **Postfix patches:** Run after original method completes
- Use `AccessTools` for reflection (accessing private methods/fields)

## Screen Reader Integration

- TolkHelper uses Prism for cross-platform screen reader access
- Prism auto-selects the best available backend (screen readers prioritized over TTS)
- Supported backends: NVDA, JAWS, SAPI, OneCore (Windows), VoiceOver (macOS), Orca, Speech Dispatcher (Linux)
- All navigation actions should announce via `TolkHelper.Speak()`
- Use `SpeechPriority.Low` for rapid navigation (don't interrupt)
- Use `SpeechPriority.High` for critical alerts

## State Lifecycle

1. Patch's PostOpen/Postfix initializes state: `MyState.Open()`, `IsActive = true`
2. UnifiedKeyboardPatch routes input to state while `IsActive == true`
3. State handles navigation, announces via TolkHelper
4. State closes: `MyState.Close()`, `IsActive = false`

## Important Conventions

- **IsActive flags:** All State classes must have this to prevent input conflicts
- **Event consumption:** Always call `Event.current.Use()` after handling input
- **Priority order:** Lower numbers = higher priority in UnifiedKeyboardPatch
- **Module CLAUDE.md:** Keep up to date with architectural changes
- **Conventional commits:** Use `feat:`, `fix:`, `refactor:`, `docs:`, etc.

## Keyboard Input Isolation (CRITICAL)

### The Problem

When multiple UI states are stacked (e.g., CaravanFormation → QuantityMenu → Inspection), pressing Escape should only close the topmost state. RimWorld's Window system has its own Cancel key handling via `Window.OnCancelKeyPressed()` that runs independently of our keyboard handler.

**Common symptoms of this bug:**
- Pressing Escape closes TWO dialogs instead of one
- Camera pan sound plays unexpectedly (RimWorld's default close sound)
- User returns to an unexpected screen after pressing Escape

### What Doesn't Work

These approaches do NOT reliably block RimWorld's Escape handling:
- `Event.current.Use()` - RimWorld's KeyBindingDef doesn't check if event was "used"
- `Event.current.keyCode = KeyCode.None` - Window system may check before we can clear it
- Frame tracking with `ClosedOnFrame` - Same timing issues

### The Solution: Harmony Patch on Window.OnCancelKeyPressed

**IMPORTANT:** `Event.current.Use()` and even `Event.current.keyCode = KeyCode.None` are NOT sufficient to block RimWorld's Escape handling!

RimWorld's `Window.OnCancelKeyPressed` is called by the WindowStack independently of our keyboard handler. The only reliable way to block it is to patch the method directly:

```csharp
// In CaravanFormationPatch.cs
[HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
[HarmonyPrefix]
public static bool Window_OnCancelKeyPressed_Prefix(Window __instance)
{
    // Only intercept for specific dialogs
    if (__instance is Dialog_FormCaravan || __instance is Dialog_SplitCaravan)
    {
        // Block the game's Cancel handling when our overlay menus are active
        if (QuantityMenuState.IsActive || WindowlessInspectionState.IsActive)
        {
            return false; // Skip original method - let our overlay handle the Escape
        }
    }
    return true; // Let original method run
}
```

### Current Implementation

The `Window.OnCancelKeyPressed` patch is in `CaravanFormationPatch.cs` and handles both:
- `Dialog_FormCaravan` - Caravan formation from colony
- `Dialog_SplitCaravan` - Splitting an existing caravan

When `QuantityMenuState.IsActive` or `WindowlessInspectionState.IsActive`, the patch returns `false` to prevent RimWorld from closing the underlying dialog.

### When to Extend This Pattern

If you add a new overlay menu that can appear over RimWorld dialogs with `closeOnCancel = true`:
1. Add your state's `IsActive` check to the existing `Window_OnCancelKeyPressed_Prefix`
2. Or create a similar patch for the specific dialog class if it overrides `OnCancelKeyPressed`

### Priority System Review

UnifiedKeyboardPatch processes input by priority (lower = higher priority):

| Priority | State | Escape Behavior |
|----------|-------|-----------------|
| 0.22 | WindowlessInspectionState (from caravan) | Close inspection only |
| 0.25 | QuantityMenuState | Close quantity menu only |
| 0.3 | CaravanFormationState | Close caravan dialog |
| 0.35 | SplitCaravanState | Close split dialog |
| ... | ... | ... |
| 8 | Global Escape | Open pause menu |

The frame checks are placed BEFORE priority 0.3 to catch Escape events that already closed higher-priority states.

### Debugging Checklist

If Escape closes the wrong dialog:
1. Check if the closing state has `ClosedOnFrame` implemented
2. Check if UnifiedKeyboardPatch has the frame check before the affected handler
3. Verify the priority order - higher priority states should be checked first
4. Check for `doCloseSound: true` in `WindowStack.TryRemove()` calls (should usually be `false` to let our TolkHelper announce instead)

### Additional Patterns

**Blocking game's default Escape handling:**
Some RimWorld dialogs have their own Escape handling (e.g., `OnAcceptKeyPressed`). Use Harmony Prefix patches to block:

```csharp
[HarmonyPatch("OnAcceptKeyPressed")]
[HarmonyPrefix]
public static bool OnAcceptKeyPressed_Prefix()
{
    if (MyState.IsActive)
        return false;  // Block game's handling
    return true;
}
```

**Silent dialog closing:**
Always use `doCloseSound: false` when closing dialogs programmatically:

```csharp
Find.WindowStack.TryRemove(dialog, doCloseSound: false);
TolkHelper.Speak("Dialog closed");  // Our announcement instead
```

## Workflow Notes

- **Main branch:** `master`
- **Bug reports:** Only test with Harmony + RimWorld Access enabled (no other mods)
- **Pull requests:** Link to issue using `Closes #123` or `Fixes #123`
- **Before opening PRs:** Open issue first for new features, wait for feedback

## Project Structure

```
mod/
├── src/                    # All C# source code (18 modules)
│   ├── Core/              # Entry point, initialization
│   ├── Input/             # UnifiedKeyboardPatch
│   ├── ScreenReader/      # TolkHelper, audio
│   ├── Map/               # Map navigation, scanner
│   └── [15 other modules]
├── About/                 # About.xml (mod metadata)
├── Sounds/                # Embedded audio resources
├── native/                # Prism native libraries (downloaded, gitignored)
│   ├── prism.dll          # Windows x64
│   ├── libprism.dylib     # macOS universal (Intel + Apple Silicon)
│   └── libprism.so        # Linux x64
├── rimworld_access.csproj # MSBuild project file
└── GamePaths.props.template

Build output:
├── bin/Debug/net472/      # Compiled DLL
└── release/RimWorldAccess/ # Release package
```

