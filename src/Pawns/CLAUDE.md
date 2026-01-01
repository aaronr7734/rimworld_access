# Pawns Module

## Purpose
Pawn information, character tabs, policies, and assignments.

## Files
**Patches:** PawnInfoPatch.cs, AssignMenuPatch.cs, DrugPolicyPatch.cs, FoodPolicyPatch.cs, OutfitPolicyPatch.cs
**States:** PawnSelectionState.cs, HealthState.cs, HealthTabState.cs, MoodState.cs, NeedsState.cs, AssignMenuState.cs, BedAssignmentState.cs, WindowlessDrugPolicyState.cs, WindowlessFoodPolicyState.cs, WindowlessOutfitPolicyState.cs, WindowlessScheduleState.cs
**Helpers:** PawnInfoHelper.cs, HealthTabHelper.cs, SocialTabHelper.cs, InteractiveGearHelper.cs

## Key Shortcuts
- **Tab/Shift+Tab** - Cycle selected pawns
- **Comma/Period** - Cycle pawns (integrates with scanner)
- **Alt+M** - Quick mood info
- **Alt+H** - Quick health info
- **Alt+N** - Quick needs info
- **F2** - Schedule editor
- **F3** - Assign menu

## Architecture
PawnSelectionState integrates with scanner. Quick info shortcuts (Alt+M/H/N) work without opening tabs. Full tabs navigate detailed character info.

## Dependencies
**Requires:** ScreenReader/, Input/, Map/ (selected pawn)
**Used by:** Work/, Prisoner/

## Testing
- [ ] Pawn cycling works
- [ ] Quick info shortcuts functional
- [ ] Character tabs navigable
- [ ] Policy editors accessible
