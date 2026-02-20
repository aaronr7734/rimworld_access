# Pawns Module

## Purpose
Pawn information, character tabs, policies, and assignments.

## Files
**Patches:** PawnInfoPatch.cs, AssignMenuPatch.cs, DrugPolicyPatch.cs, FoodPolicyPatch.cs, OutfitPolicyPatch.cs
**States:** PawnSelectionState.cs, ColonistBarState.cs, HealthState.cs, HealthTabState.cs, MoodState.cs, NeedsState.cs, AssignMenuState.cs, BedAssignmentState.cs, WindowlessDrugPolicyState.cs, WindowlessFoodPolicyState.cs, WindowlessOutfitPolicyState.cs, WindowlessScheduleState.cs
**Helpers:** PawnInfoHelper.cs, HealthTabHelper.cs, SocialTabHelper.cs, InteractiveGearHelper.cs

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
- **Alt+A** - Quick area assignment for selected pawn
- **F2** - Schedule editor
- **F3** - Assign menu

## Architecture
PawnSelectionState integrates with scanner. ColonistBarState provides page-based bar navigation with Alt+Arrow/number keys, reordering via ColonistBar.Reorder(), and mech section after colonist pages. Quick info shortcuts (Alt+M/H/N) work without opening tabs. Full tabs navigate detailed character info.

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
