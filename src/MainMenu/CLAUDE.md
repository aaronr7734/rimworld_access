# MainMenu Module

## Purpose
Provides keyboard navigation for main menu and all game setup screens (scenario selection, storyteller, world generation, colonist editor).

## Files in This Module

### Patches (10 files)
- **MainMenuAccessibilityPatch.cs** - Main menu navigation (Prefix + Postfix)
- **ModListPatch.cs** - Mod manager (Page_ModsConfig) keyboard navigation
- **ModSettingsDialogPatch.cs** - Mod settings dialog announcements
- **LoadingScreenAccessibilityPatch.cs** - Loading screen announcements
- **ScenarioSelectionPatch.cs** - Scenario picker
- **StorytellerSelectionPatch.cs** - Storyteller selection (both main menu and in-game)
- **IdeologySelectionPatch.cs** - Ideology selection
- **ColonistEditorPatch.cs** - Character editor (Prepare Carefully mode)
- **StartingSitePatch.cs** - Starting location selection
- **WorldParamsPatch.cs** - World generation parameters

### States (9 files)
- **MenuNavigationState.cs** - Main menu navigation
- **ModListState.cs** - Mod manager keyboard navigation
- **ScenarioNavigationState.cs** - Scenario selection
- **StorytellerNavigationState.cs** - Storyteller picker
- **StorytellerSelectionState.cs** - In-game storyteller change
- **IdeologyNavigationState.cs** - Ideology picker
- **ColonistEditorNavigationState.cs** - Character customization
- **StartingSiteNavigationState.cs** - Map site picker
- **WorldParamsNavigationState.cs** - World gen settings

## Key Architecture

### State Management
Each game setup screen has its own State class that tracks current selection and handles navigation.

### Input Handling
All input handled via UnifiedKeyboardPatch. Main menu has special Priority 0 handling for initial game load.

### Dependencies
**Requires:** ScreenReader/, Input/
**Used by:** None (only active at main menu and game setup)

## Keyboard Shortcuts
- **Arrow Up/Down** - Navigate within current menu/list
- **Arrow Left/Right** - Switch between menu columns (main menu) or categories
- **Enter** - Confirm selection
- **Escape** - Go back / Cancel
- **Tab** - Cycle options (some screens)

## Integration with Core Systems

### UnifiedKeyboardPatch
Each State's IsActive flag is checked before routing input.

### TolkHelper (Screen Reader)
All menu items and selections are announced via TolkHelper.Speak().

### MapNavigationState
Not applicable - main menu has no map.

## Common Patterns

### Menu Reconstruction
Some patches (like MainMenuAccessibilityPatch) must manually rebuild menu structures because original items are created in local scope.

### Selection Highlighting
Visual highlight drawn using `Widgets.DrawHighlight()` to show selected menu item for sighted users.

### Navigation Wrapping
All navigation wraps around - pressing Down at bottom goes to top, pressing Right in last column goes to first column.

## RimWorld Integration

### Harmony Patches
- Prefix patches: Rebuild menu structures, intercept input
- Postfix patches: Draw highlights, initialize state, handle keyboard

### Reflection Usage
Extensive use of `AccessTools` to call private menu methods like `InitLearnToPlay`, `CloseMainTab`, etc.

### Game Systems Used
- `ProgramState.Entry` vs `ProgramState.Playing` to detect main menu vs in-game
- `Find.*` for various game managers
- `Widgets.*` for UI drawing

## Testing Checklist
- [ ] Main menu navigation works (arrow keys, enter)
- [ ] Mod manager accessible (enable/disable mods, reorder)
- [ ] All game setup screens navigable
- [ ] Screen reader announces all selections
- [ ] Visual highlights visible
- [ ] Can complete full game setup flow with keyboard only
