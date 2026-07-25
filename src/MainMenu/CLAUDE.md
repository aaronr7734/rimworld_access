# MainMenu Module

## Purpose
Provides keyboard navigation for main menu and all game setup screens (scenario selection, storyteller, world generation, colonist editor).

## Files in This Module

### Patches (13 files)
- **MainMenuAccessibilityPatch.cs** - Main menu navigation (Prefix + Postfix)
- **ModListPatch.cs** - Mod manager (Page_ModsConfig) keyboard navigation
- **ModSettingsDialogPatch.cs** - `Dialog_ModSettings` lifecycle: capture-gating Prefix (Priority.First, `ModSettingsCaptureState.BeginFrame(__instance)`) + Finalizer (`EndFrame`, always runs) bracket the mod's own `DoWindowContents`; a Priority.High input Prefix routes keys to `ModSettingsMenuState` once open. The Postfix opens the menu only when no menu is already active **and** the frame captured at least one widget - so in heavily-modded setups a second on-screen `Dialog_ModSettings` (e.g. a "Mod Settings Framework" wrapper that draws nothing we recognise) can't re-bind or open an empty menu. `Window.PostClose` only tears down when the *bound* instance closes; `Window.OnCancelKeyPressed`/`OnAcceptKeyPressed` prefixes block vanilla's Escape **and** Enter handling (root CLAUDE.md's documented pattern) while our menu is active, so Enter activates a control instead of closing the dialog. Input_Prefix and the open Postfix are wrapped so our code can never throw into the game's window rendering.
- **ModSettingsWidgetPatches.cs** - Patches `Listing_Standard`/`Widgets` checkbox/button/radio/slider/label overloads (disambiguated by explicit argument-type arrays where overloaded). Gated on `ModSettingsCaptureState.Capturing` for zero overhead elsewhere. **Reentrancy guard:** `Listing_Standard` widgets are thin wrappers around the raw `Widgets.*` equivalents, which often call `Widgets.Label` internally (e.g. `CheckboxLabeled` -> `Widgets.CheckboxLabeled` -> `Widgets.Label`) - every patched Prefix calls `ModSettingsCaptureState.EnterWidget()` and only records if it returned true (outermost call); every Postfix calls `ExitWidget()` in a `finally` regardless of depth, so nesting stays balanced and one logical control records exactly once. The leaf `Widgets.Label` patch instead checks `AtTopLevel` (no Enter/Exit of its own). The outermost Postfix applies a pending activation queued by `ModSettingsMenuState` - writes the `ref` param directly (checkbox / by-ref `HorizontalSlider`) or overrides `__result` (button/radio/`SliderLabeled`/`Slider`) so the mod's own widget-reading code reacts as if the user had clicked/dragged it. Sliders are captured from `Listing_Standard.SliderLabeled`/`Slider` **and** the raw `Widgets.HorizontalSlider` overloads; a bare slider (no label of its own) adopts the standalone Label drawn just before it via `RecordSlider`. **Dropdown rows** (`ButtonTextLabeled`/`ButtonTextLabeledPct`) are captured as an activatable "{label}: {value}" button; the FloatMenu they open is routed into the accessible windowless menu by `DialogInterceptionPatch` (gated on `ModSettingsMenuState.IsActive`). **Text/numeric fields** are captured at the leaf `Widgets.TextField`/`TextArea` (which every string field AND every generic `TextFieldNumeric<T>` bottoms out in), so no generic method is patched; the leaf checks `AtTopLevel` like `Widgets.Label`, adopts the preceding standalone Label as its caption via `RecordTextField`, and applies a pending edit by overriding the returned string. **Integer entries** (`IntEntry`, both the `Listing_Standard` wrapper - which carries the `min` floor - and the raw `Widgets` overload) are recorded as one control via the depth guard (suppressing their four internal +/- `ButtonText` and inner numeric field). Two-handle range sliders (`Widgets.IntRange`/`FloatRange`) and fully custom hand-drawn controls remain out of scope.
- **LoadingScreenAccessibilityPatch.cs** - Loading screen announcements
- **ScenarioSelectionPatch.cs** - Scenario picker
- **StorytellerSelectionPatch.cs** - Storyteller selection (both main menu and in-game)
- **IdeologySelectionPatch.cs** - Ideology selection
- **StartingPawnPatch.cs** - Pawn selection screen (Page_ConfigureStartingPawns). PreOpen/PostClose lifecycle, DoWindowContents sync, DoNext/DoBack reflection.
- **StartingSitePatch.cs** - Starting location selection
- **WorldParamsPatch.cs** - World generation parameters
- **FactionLandingPatch.cs** - Faction relations dialog during starting site selection (PostOpen/PostClose lifecycle)
- **AnomalySettingsDialogPatch.cs** - `Dialog_AnomalySettings` lifecycle (PostOpen/PreClose) plus `OnAcceptKeyPressed`/`OnCancelKeyPressed` filtered prefixes that block vanilla's Enter/Escape handling while our state is active.

### States (13 files)
- **MenuNavigationState.cs** - Main menu navigation
- **ModListState.cs** - Mod manager keyboard navigation
- **ModSettingsMenuState.cs** - Windowless menu overlaid on a still-live `Dialog_ModSettings` (ModListState pattern, not WindowlessDialogState - the dialog must stay open so it keeps drawing/capturing/applying). Flat list of the current frame's `CapturedWidget`s (MenuHelper + TypeaheadSearchHelper, registered in `TypeaheadConsumerRegistry` at priority 4.71). Up/Down/Home/End navigate; **Enter or Space** toggles a checkbox / clicks a button / selects a radio / opens a dropdown row's choice menu / opens the modal editor on a text field / types an exact value into an integer entry; **Left/Right** adjusts the selected slider or steps an integer entry by its multiplier. Text fields and integer entries are edited through the shared modal `TextInputController` (registers as `TextInputManager.Active`); the committed value is queued back through the widget patches (raw string for a text field, parsed+floored int for an IntEntry). While a dropdown's windowless FloatMenu or a modal text-edit session is open on top of it, the settings list stands down (both the dialog `Input_Prefix` and the typeahead path check `WindowlessFloatMenuState.IsActive`/`TextInputManager.IsActive`). Sliders step on a "nice" grid (1/2/5 x 10^n near 1/20 of range) snapped to round multiples, compound rapid presses from the last-requested value (not the lagging capture), and announce the value **after the mod redraws its label** so the user hears it in the mod's own format ("130%", not "1.3"). Activating a section/tab button jumps the selection to the first control whose label changed (i.e. into the newly-shown section). Escape closes the dialog (`Find.WindowStack.TryRemove(doCloseSound: false)`). `NotifyFrameCaptured(dialog, widgets)` runs every frame from `ModSettingsCaptureState.EndFrame`, refreshes silently but **only for the bound dialog instance**, and drives the deferred slider/section announcements.
- **ModSettingsCaptureState.cs** - Per-frame shadow-list builder for `Dialog_ModSettings`, tagged with the `currentFrameDialog` it belongs to (so the menu only refreshes from the instance it bound to). `Capturing` gates `ModSettingsWidgetPatches`; `RecordWidget`/`RecordSlider` assign each drawn control a `DrawIndex`. A queued activation (`SetPendingBool`/`SetPendingFloat`/`SetPendingActivation`) is correlated back to a widget on a later frame by `(DrawIndex, Kind)` plus, for bool/activation only, `Label` - a mismatch safely drops it. Sliders skip the label check (their label embeds the live value, so it changes as they move) and expose `TryGetPendingFloat` so the menu can compound rapid presses.
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
