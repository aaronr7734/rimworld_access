# Pawns Module

## Purpose
Pawn information, character tabs, policies, and assignments.

## Files
**Patches:** PawnInfoPatch.cs, AssignMenuPatch.cs, PolicyEditorPatch.cs, PawnSkillsTablePatch.cs
**States:** PawnSelectionState.cs, ColonistBarState.cs, HealthState.cs, HealthTabState.cs, MoodState.cs, NeedsState.cs, AssignMenuState.cs, BedAssignmentState.cs, PolicyEditorState.cs, DrugPolicyEditorState.cs, ReadingPolicyEditorState.cs, WindowlessScheduleState.cs, PawnSkillsTableState.cs
**Helpers:** PawnInfoHelper.cs, HealthTabHelper.cs, SocialTabHelper.cs, InteractiveGearHelper.cs, PawnSkillsTableHelper.cs

## Key Shortcuts
- **Tab/Shift+Tab** - Cycle selected pawns
- **Comma/Period** - Cycle pawns (cycles mechs when on mech page)
- **Alt+Left/Right** - Navigate colonist bar linearly (crosses page boundaries)
- **Alt+1 through Alt+0** - Jump to position 1-10 on current page
- **Alt+Down/Up** - Move between pages of 10 (colonist pages, then mech pages)
- **Ctrl+Alt+Left/Right** - Reorder: shift selected colonist left/right on bar
- **Ctrl+Alt+Down/Up** - Reorder: move selected colonist between pages
- **Ctrl+Alt+Enter** - Open inspection tree for selected pawn
- **Alt+M** - Quick mood info
- **Alt+H** - Quick health info
- **Alt+N** - Quick needs info
- **Alt+K** - Quick top 3 skills info
- **Alt+P** - Open pawn skills table (all colonists x all skills, read-only)
- **Alt+A** - Quick area assignment for selected pawn
- **F2** - Schedule editor
- **F3** - Assign menu

### Assign Menu Painting
- **Shift+Down/Up** - Paint current cell's value to next/previous pawn
- **Shift+Home/End** - Bulk paint from current row to first/last
- **Ctrl+Shift+Home/End** - Paint entire column

## Architecture
PawnSelectionState integrates with scanner. ColonistBarState provides page-based bar navigation with Alt+Arrow/number keys, reordering via ColonistBar.Reorder(), and mech section after colonist pages. Quick info shortcuts (Alt+M/H/N) work without opening tabs. Full tabs navigate detailed character info.

### PawnSkillsTableState
- Global Alt+P opens a read-only 2D table: rows = colonists (colonist bar order), columns = Name + all SkillDefs (vanilla SkillUI order, `listOrder` descending)
- Built on `TabularMenuHelper<Pawn>` (same pattern as WorkTableState). Cell value: `"[passion, ]{level}, {LevelDescriptor}"` or `"incapable"` for disabled skills
- Column tooltip announces the skill def's description (once on column change)
- Alt+S sorts by current column (descending → ascending → default); skill columns sort by level with passion as tiebreaker, disabled pawns sort to the bottom
- Typeahead search by pawn name (A-Z)
- Arrow keys, Home/End, Escape/Enter close, Alt+P toggles

### ColonistBarState
- Virtual "bar" overlaid on flat colonist list (pages of 10)
- Colonists from `Find.ColonistBar.GetColonistsInOrder()` filtered by current map
- Mechs from `mapPawns.SpawnedColonyMechs` (Biotech DLC)
- Alt+Down/Up navigates: colonist pages first, then mech pages
- Reordering uses `ColonistBar.Reorder()` which sets `pawn.playerSettings.displayOrder`
- Syncs with PawnSelectionState when comma/period used, and vice versa
- Resets bar position on map change

## Dependencies
**Requires:** ScreenReader/, Input/, Map/ (selected pawn, camera mode)
**Used by:** Work/, Prisoner/

## Testing
- [ ] Pawn cycling works (comma/period)
- [ ] Colonist bar navigation (Alt+Left/Right)
- [ ] Page navigation (Alt+Up/Down)
- [ ] Position jumping (Alt+1-0)
- [ ] Colonist reordering (Ctrl+Alt+Left/Right) with neighbor announcements
- [ ] Cross-page reordering (Ctrl+Alt+Down/Up)
- [ ] Ctrl+Alt+Enter opens inspection tree for selected pawn
- [ ] Mech page navigation
- [ ] Mech cycling with comma/period on mech page
- [ ] Bar position syncs with comma/period
- [ ] AnnouncePosition setting respected
- [ ] Quick info shortcuts functional
- [ ] Character tabs navigable
- [ ] Policy editors accessible
- [ ] Assign: Shift+Down/Up paints column values (policies, hostility, medical care)
- [ ] Assign: Shift+Home/End bulk paints with name list announcement
- [ ] Assign: Ctrl+Shift+Home/End paints entire column
- [ ] Assign: Non-paintable columns (Name, Ideo, Xenotype) show "Cannot paint"
