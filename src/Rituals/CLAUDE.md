# Rituals Module

## Purpose
Keyboard accessibility for RimWorld's ritual system and dryad caste selection dialogs. Handles all ritual types generically (weddings, funerals, childbirth, conversions, speeches, etc.) and provides flat menu keyboard navigation for Gauranlen tree dryad caste selection.

## Files

**States:** RitualState.cs, DryadCasteState.cs
**Patches:** RitualPatch.cs, DryadCastePatch.cs
**Helpers:** RitualTreeBuilder.cs, RitualStatFormatter.cs

## Key Shortcuts (Ritual Dialog)

### Role List Mode (Main View)
| Key | Action |
|-----|--------|
| Up/Down | Navigate roles |
| Enter | Enter pawn selection for role |
| Tab | Toggle quality stats view |
| Alt+S | Start ritual |
| Home/End | Jump to first/last role |
| Typeahead | Jump to role starting with typed letters |
| Escape | Cancel dialog (let game handle) |

### Pawn Selection Mode
| Key | Action |
|-----|--------|
| Up/Down | Navigate pawns |
| Space | Toggle pawn selection (select/deselect) |
| Enter | Confirm selection and return to role list |
| Escape | Cancel and return to role list |
| Alt+I | Open pawn info card |
| Alt+H | View pawn health |
| Alt+M | View pawn mood |
| Alt+N | View pawn needs |
| Alt+G | View pawn gear |
| Home/End | Jump to first/last pawn |
| Typeahead | Jump to pawn starting with typed letters |

### Quality Stats Mode
| Key | Action |
|-----|--------|
| Up/Down | Navigate quality factors |
| Alt+I | Open stat breakdown (StatBreakdownState) |
| Tab/Escape | Return to previous mode |

## Architecture

### Generic Design
All rituals use `Dialog_BeginRitual` which uses `RitualRoleAssignments` for role management. The code is 100% generic with no ritual-specific branching.

### Three Navigation Modes
```csharp
enum NavigationMode { RoleList, PawnSelection, QualityStats }
```

### Data Flow
```
Dialog_BeginRitual.PostOpen
    -> RitualState.Open(dialog)
        -> Extract RitualRoleAssignments via reflection
        -> RitualTreeBuilder.BuildRoleList()
        -> Announce opening

User navigates roles (Up/Down)
    -> RitualState.SelectNextRole/SelectPreviousRole
    -> RitualStatFormatter.FormatRoleAnnouncement()
    -> TolkHelper.Speak()

User presses Enter on role
    -> RitualState.EnterPawnSelection()
        -> RitualTreeBuilder.BuildPawnList()
        -> Switch to PawnSelection mode

User selects pawn (Space)
    -> RitualRoleAssignments.TryAssign()
    -> Announce assignment change

User confirms (Enter)
    -> RitualState.ConfirmPawnSelection()
    -> Return to RoleList mode
```

### Dryad Caste Menu (Flat Navigation)

Dryad castes are displayed as a flat list in the game's UI, with visual dependency lines connecting related castes. The accessibility implementation provides simple menu navigation:

- Navigate through all available castes with Up/Down arrows
- Each caste shows status: current mode, available, or locked (with reason)
- Enter attempts to apply the selected caste (opens confirmation)
- Escape closes the dialog
- Home/End jump to first/last caste

The dependency information (which castes require other castes) is announced in the status alongside caste information but doesn't affect the flat menu structure.

### Accessing Data

```csharp
// Get assignments (protected field)
var assignmentsField = AccessTools.Field(typeof(Dialog_BeginRitual), "assignments");
var assignments = assignmentsField.GetValue(dialog) as RitualRoleAssignments;

// Get roles
List<RitualRole> roles = assignments.AllRolesForReading;

// Get assigned pawns for a role
List<Pawn> pawns = assignments.AssignedPawns(role).ToList();

// Get pawn suitability (from RitualRoleColonist)
if (role is RitualRoleColonist colonistRole && colonistRole.usedStat != null)
{
    string value = colonistRole.usedStat.Worker.ValueToStringFor(pawn);
}
```

## Dependencies

**Requires:**
- ScreenReader/ (TolkHelper for announcements)
- Input/ (UnifiedKeyboardPatch for keyboard routing)
- UI/ (TypeaheadSearchHelper, MenuHelper)
- World/ (StatBreakdownState for quality breakdown)
- Pawns/ (PawnInfoHelper for Alt+H/M/N/G)

**DLC Required:** Ideology (for most rituals), Anomaly (for some rituals)

## Priority in UnifiedKeyboardPatch

RitualState is handled at Priority 0.33, DryadCasteState at 0.34, and split caravan at 0.35.

## State Lifecycle

1. User clicks ritual gizmo or button
2. `Dialog_BeginRitual` opens
3. `RitualPatch.PostOpen_Postfix` triggers `RitualState.Open(dialog)`
4. State extracts role assignments, builds role list
5. User navigates with keyboard, `HandleInput()` processes keys
6. User starts ritual (Alt+S) or cancels (Escape)
7. `RitualPatch.PostClose_Postfix` triggers `RitualState.Close()`

## Testing Checklist

### Opening
- [ ] Dialog opens and announces ritual name + role count
- [ ] First role is announced automatically

### Role Navigation
- [ ] Up/Down navigates roles
- [ ] Announces: role name, assigned count, max, required/locked status
- [ ] Home/End jump to first/last role
- [ ] Typeahead finds roles by name

### Pawn Selection
- [ ] Enter opens pawn selection for non-locked roles
- [ ] Locked roles announce "Role is locked" and don't open
- [ ] Pawns show suitability stats (Medical, Tend Quality, etc.)
- [ ] Assigned pawns marked, forced pawns show "cannot change"
- [ ] Space toggles selection
- [ ] Enter confirms selection
- [ ] Alt+H/M/N/G show pawn health/mood/needs/gear
- [ ] Alt+I opens pawn info card
- [ ] Escape returns to role list

### Quality Stats
- [ ] Tab toggles quality stats view
- [ ] Shows all quality factors with change values
- [ ] Alt+I opens StatBreakdownState for selected factor
- [ ] Tab/Escape returns to previous mode

### Starting Ritual
- [ ] Alt+S starts the ritual
- [ ] Validation errors announced (missing required roles, etc.)
- [ ] Successful start closes dialog

### Universal
- [ ] Works for wedding, funeral, conversion, childbirth, etc.
- [ ] No ritual-specific code needed
- [ ] Typeahead search works in all list modes
- [ ] Menu wrapping respects mod setting

### Dryad Caste Dialog
- [ ] Opening dialog announces connected pawn and current mode
- [ ] Menu navigation: Up/Down navigate through castes, Home/End jump to first/last
- [ ] Each caste announces with name and status (current, available, locked with reason)
- [ ] Position announcements: "X of Y" for navigation context
- [ ] Enter on caste selects it and attempts to apply (opens confirmation if available)
- [ ] Locked castes announce specific reason (missing memes, requires prior caste, etc.)
- [ ] Escape closes the dialog cleanly
- [ ] State closes automatically if dialog closes by any path
