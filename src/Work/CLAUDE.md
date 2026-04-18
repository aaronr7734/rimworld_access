# Work Module

## Purpose
Work priority management. Two alternate views are provided; F1 opens whichever
was last used (persisted via `RimWorldAccessSettings.DefaultWorkMenuView`).

## Files
**Patches:** WorkMenuPatch.cs (input + overlays for both views, MainTabWindow_Work intercept, view-swap dispatcher)
**States:** WorkMenuState.cs (focused view), WorkTableState.cs (table view)
**Helpers:** WorkTableHelper.cs (column data for the table view)

## Views

### Focused view (`WorkMenuState`)
Priority-grouped per-pawn grid. 5 columns = Priority 1-4 + Disabled; rows are
the work types within each priority. Default for new users (less verbose).

### Table view (`WorkTableState`)
Pawn rows × work-type columns matching vanilla's `PawnTableDef.Work` layout.
Built on `TabularMenuHelper<Pawn>`. Cell value is terse: `"3, shooting: 15**"`
in manual mode or `"on, animals: 5"` in basic mode, `"incapable"` for disabled.
Column tooltip (announced on column change) includes gerundLabel, description,
`SpecificWorkListString` with emergency markers, and ideology warning.

## Key Shortcuts (both views unless noted)
- **Arrow keys** - Navigate
- **0-4** - Set priority directly
- **[** / **]** - Cycle priority vanilla-style (`[` more important, `]` less important)
- **Shift+[** / **Shift+]** - Cycle for all eligible colonists (vanilla shift-click header)
- **Space** - Toggle (basic mode)
- **Alt+M** - Toggle basic/manual mode
- **Enter** / **Escape** - Save & close
- **Ctrl+Tab** / **Ctrl+Shift+Tab** (Option+Tab on macOS) - Swap to the other view (persisted as new default)

### Focused view only
- **Up/Down** - Change priority level (column)
- **Left/Right** - Navigate within priority column
- **Tab** / **Shift+Tab** - Switch pawn
- **Shift+0-4** - Set current work type priority for all colonists

### Table view only
- **Up/Down** - Previous/next pawn (row)
- **Left/Right** - Previous/next column
- **Home/End** - First/last pawn
- **Alt+S** - Sort by current column (cycles descending → ascending → default)
- **Shift+Up/Down** - Paint current cell's priority to adjacent pawn + move
- **Shift+Home/End** - Paint to range (start/end)
- **Ctrl+Shift+Home/End** - Paint to entire column
- **A-Z** - Typeahead search by pawn name

## Architecture
Both State classes follow the State + Patch pattern. `WorkMenuOpener` centralizes
dispatch (`OpenDefaultView`, `SwapToTable`, `SwapToFocused`) and persists the
chosen view. `WorkMenuPatch` and `WorkTableMenuInputPatch` are separate
`UIRoot.UIRootOnGUI` prefix patches, each gated by its view's `IsActive` flag.
Priority changes in the table view use `PawnColumnWorker_WorkPriority.Compare`
semantics for sorting (skill-average-based).

## Dependencies
**Requires:** ScreenReader/, Input/, Pawns/ (pawn list), UI/ (TabularMenuHelper, MenuHelper, BulkSoundQueue, TypeaheadSearchHelper), Core/ (settings)

## Testing
- [ ] F1 opens the view from the persisted setting
- [ ] Alt+Tab swaps and persists
- [ ] Brackets cycle priority matching vanilla (1 stays on left-click, 0 stays on right-click)
- [ ] Shift+brackets apply to all eligible colonists
- [ ] Alt+S sorts by skill average (descending / ascending / cleared)
- [ ] Painting respects incapable pawns
- [ ] Basic mode supported in both views
