# MainMenu Module

## Purpose
Provides keyboard navigation for main menu and all game setup screens (scenario selection, storyteller, world generation, colonist editor).

## Files in This Module

### Patches (12 files)
- **MainMenuAccessibilityPatch.cs** - Main menu navigation (Prefix + Postfix)
- **ModListPatch.cs** - Mod manager (Page_ModsConfig) keyboard navigation
- **ModSettingsDialogPatch.cs** - Mod settings dialog announcements
- **LoadingScreenAccessibilityPatch.cs** - Loading screen announcements
- **ScenarioSelectionPatch.cs** - Scenario picker
- **StorytellerSelectionPatch.cs** - Storyteller selection (both main menu and in-game)
- **IdeologySelectionPatch.cs** - Ideology selection
- **StartingPawnPatch.cs** - Pawn selection screen (Page_ConfigureStartingPawns). PreOpen/PostClose lifecycle, DoWindowContents sync, DoNext/DoBack reflection.
- **StartingSitePatch.cs** - Starting location selection
- **WorldParamsPatch.cs** - World generation parameters
- **FactionLandingPatch.cs** - Faction relations dialog during starting site selection (PostOpen/PostClose lifecycle)
- **AnomalySettingsDialogPatch.cs** - `Dialog_AnomalySettings` lifecycle (PostOpen/PreClose) plus `OnAcceptKeyPressed`/`OnCancelKeyPressed` filtered prefixes that block vanilla's Enter/Escape handling while our state is active.

### States (11 files)
- **MenuNavigationState.cs** - Main menu navigation
- **ModListState.cs** - Mod manager keyboard navigation
- **ScenarioNavigationState.cs** - Scenario selection
- **StorytellerNavigationState.cs** - Storyteller picker
- **StorytellerSelectionState.cs** - In-game storyteller change
- **IdeologyNavigationState.cs** - Ideology picker
- **StartingPawnState.cs** - Pawn selection tree view. Two-tab shell (game start only): **Tab/Shift+Tab** flips between the Characters tree and the read-only **Team Skills** summary (best starting pawn per `pawnCreatorSummaryVisible` skill — parity with vanilla's TeamSkills panel; Up/Down/Home/End + typeahead by skill name). Characters tree: pawns grouped under Selected/Left behind headers, expandable to Bio/**Relations**/Traits/IncapableOf/Skills/Health/Possessions categories. The Relations category mirrors the vanilla character card's Relations panel (only pawns where `everSeenByPlayer && !hidePawnRelations`, i.e. roster pawns); opinions live in each relation's tooltip; **Enter on a relation jumps to that pawn's collapsed top-level node** and reads its full summary. Page Up/Down preserves position. Ctrl+Up/Down reorders. Alt+R randomizes, Alt+N renames, ] context menu. Escape always leaves the screen (back-confirm) from either tab.
- **StartingSiteContext.cs** - World-gen-only features (I-menu, R random, Ctrl+arrows biome jump, faction warnings, tile validation). Navigation shared with in-game via WorldNavigationState.
- **WorldParamsNavigationState.cs** - World gen settings
- **FactionLandingState.cs** - Faction relations dialog navigation (F key from starting site). Flat list with typeahead search, Alt+I for info card.
- **AnomalySettingsDialogState.cs** - Keyboard nav for `Dialog_AnomalySettings`. Single flat list (matches vanilla's actual UI structure and the in-game custom-difficulty Anomaly section): index 0 is the playstyle row (Left/Right cycles playstyles via `AnomalyPlaystyleSetting.Adjust`), followed by the conditional anomaly sliders (override / inactive / active / study) returned by `DifficultySettingsHelper.BuildAnomalySliders(useEnabledConditions: false)`. Up/Down navigates rows, Left/Right adjusts current row, Enter toggles, typeahead searches by label. Local copies of values are committed back to the underlying `Difficulty` only on Accept (Alt+S); Esc/X discards. Alt+R opens the "Set to Standard Playstyle" preset float menu (vanilla parity). Routed at priority -0.21 in `UnifiedKeyboardPatch` (must be near the top so the absorbing modal gets first crack at keys). Page_SelectStoryteller's `Notify_ManuallySetFocus` is suppressed while this state is active to avoid stealing focus back from the modal each frame.

### Helpers (1 file)
- **StartingPawnHelper.cs** - Builds tree data from game pawn data, context menu options, Biotech dev stage/xenotype submenus. `BuildRelationsItems` reuses `SocialTabHelper.GetRelations` filtered to the vanilla `ShouldShowPawnRelations` rule. `BuildTeamSkillSummary`/`FindBestSkillOwner` mirror `StartingPawnUtility.DrawSkillSummaries`.

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
