# Rituals Module

## Purpose
Keyboard accessibility for any RimWorld dialog that derives from `Dialog_BeginLordJob`.
A single `LordJobDialogState` drives all three known subclasses through a uniform
`ILordJobDialogAdapter` abstraction:

| Dialog | DLC | Adapter |
|--------|-----|---------|
| `Dialog_BeginRitual` | Ideology | `IdeologyRitualAdapter` |
| `Dialog_BeginPsychicRitual` | Anomaly | `PsychicRitualAdapter` |
| `Dialog_BeginGravshipLaunch` | Odyssey | `GravshipLaunchAdapter` (extends `IdeologyRitualAdapter`) |

The DryadCaste dialog (Ideology Gauranlen tree caste selection) is a separate flat-menu
flow handled by `DryadCasteState` and is unrelated to the Lord-job dialog system.

## Files

**Core abstraction:** `LordJobDialogAdapter.cs` (interface, view types, factory, base class)
**Adapters:** `IdeologyRitualAdapter.cs`, `PsychicRitualAdapter.cs`, `GravshipLaunchAdapter.cs`
**State:** `LordJobDialogState.cs`
**Patches:** `RitualPatch.cs` (Lord-job dialogs), `DryadCastePatch.cs` (caste dialog)
**Helpers:** `RitualStatFormatter.cs` (announcement formatting over view types)
**Other state:** `DryadCasteState.cs`

## Architecture

### Adapter pattern

`LordJobDialogState` is dialog-agnostic — it holds an `ILordJobDialogAdapter` and never
references `Dialog_BeginRitual`, `RitualRole`, `PsychicRitualRoleDef`, or any other
dialog-specific type. The adapter encapsulates all reflection and per-dialog semantics.

View projections (`LordJobRoleView`, `LordJobPawnView`, `LordJobExtraToggle`,
`LordJobQualityRow`, `LordJobOutcomeRow`) flow from the adapter to the state. Each view
caches the adapter's native objects in an opaque `AdapterTag` field so the adapter can
round-trip them in `BuildPawnList(role)` / `ToggleAssignment(role, pawn)` calls.

```
Dialog opens
  -> RitualPatch postfix calls LordJobDialogState.Open(dialog)
       -> LordJobAdapterFactory.TryCreate(dialog, out adapter)
            -> IdeologyRitualAdapter | PsychicRitualAdapter | GravshipLaunchAdapter
       -> adapter.BuildRoleList() / BuildExtraToggles()
       -> Announce name + description + extra explanation + expected quality + warnings

User navigates / acts
  -> LordJobDialogState routes input to current mode
  -> mutations go through adapter (ToggleAssignment, ApplyExtraToggle, TryStart)
  -> adapter.Notify_AssignmentsChanged() recomputes quality

Dialog closes
  -> Window.PostClose patch announces adapter.ClosingAnnouncement and resets state
```

### Adding a new Lord-job dialog
1. Subclass `LordJobDialogAdapterBase` (or extend an existing adapter if you share UI semantics).
2. Implement `BuildRoleList`, `BuildPawnList`, `ToggleAssignment`, `LocalizedDialogName`, `ClosingAnnouncement`.
3. Override `AppendDialogSpecificWarnings` if your dialog has runtime warnings beyond `BlockingIssues`.
4. Override `BuildExtraToggles` / `ApplyExtraToggle` if your dialog has secondary controls.
5. Register the adapter in `LordJobAdapterFactory.TryCreate` (more-specific subclass first).
6. If your dialog overrides `Start()`, add it to `additionalDialogTypes` in `Core/rimworld_access.cs::ApplyLordJobStartPatches`.
7. If your dialog has its own `PostOpen` override, add a declarative `[HarmonyPatch]` on it; otherwise the existing `Window.PostOpen` filtered patches will handle it (add a new filter if needed).

### Three navigation modes

```csharp
enum NavigationMode { RoleList, PawnSelection, QualityStats }
```

Mode transitions:
- Tab toggles between RoleList/PawnSelection (whichever you came from) and QualityStats
- Enter on a role → PawnSelection mode
- Enter / Esc in PawnSelection → back to RoleList
- Esc in QualityStats → back to saved mode
- Esc in RoleList → game closes the dialog

## Key Shortcuts

### Role List Mode (Main View)
| Key | Action |
|-----|--------|
| Up/Down | Navigate roles and extra toggles |
| Enter | Open pawn selection (or toggle if on an extra toggle row) |
| Space | Toggle extra toggle (gravship checkboxes) |
| Tab | Toggle Quality Stats view |
| Alt+S | Start the Lord job |
| Home/End | Jump to first/last item |
| Typeahead | Jump to role/toggle starting with typed letters |
| Escape | Cancel dialog (game default) |

### Pawn Selection Mode
| Key | Action |
|-----|--------|
| Up/Down | Navigate pawns |
| Space | Toggle pawn selection |
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
| Up/Down | Navigate quality factors / outcome chances |
| Alt+I | Open StatBreakdown for the selected factor |
| Tab/Escape | Return to previous mode |

## Per-adapter notes

### IdeologyRitualAdapter
- Mirrors vanilla's grouping and counts via `RitualRoleAssignments.RoleGroups()`
- Surfaces the spectators row when `assignments.SpectatorsAllowed`
- Per-pawn suitability uses `RitualRoleColonist.usedStat` and `usedSkill` for stats like Medical or Tend Quality
- Extra quality rows expose Ideology development points for Fluid ideologies (`IdeoDevelopmentUtility.GetDevelopmentPointsOverOutcomeIndexCurveForRitual`)
- `Notify_AssignmentsChanged` calls both `RitualRoleAssignments.Notify_AssignmentsChanged` and the per-comp loop from `Dialog_BeginRitual.PostOpen` so quality factors reflect comp-derived state

### PsychicRitualAdapter
- Roles built from `PsychicRitualRoleAssignments.RoleGroups()` (psychic groups by `role.Label`, not by mergeId)
- Suitability line is `"Psychic sensitivity: {pawn.GetStatValue(StatDefOf.PsychicSensitivity).ToStringPercent()}"` plus any `psychicRitualDef.GetPawnTooltipExtras(pawn)`
- Role announcement includes a "Disallows: Sleeping, Drafted, …" line so blind users hear the assignability rules sighted users see only via tooltips
- Sleeping/drafted/outcome warnings use the Anomaly translation keys (`PsychicRitualWakingPawnsWarning`, `PsychicRitualUndraftPawnsWarning`, `psychicRitualDef.OutcomeWarnings`)
- Spectators are not supported (`PsychicRitualRoleAssignments.SpectatorsAllowed => false`); the adapter never produces a spectators row
- Cooldown registration happens inside `Dialog_BeginPsychicRitual.Start()` (via `PsychicRitualManager.RegisterCooldown`) — we don't reimplement it; we just call `OnAcceptKeyPressed`

### GravshipLaunchAdapter
- Extends `IdeologyRitualAdapter` because `Dialog_BeginGravshipLaunch` subclasses `Dialog_BeginRitual`
- Adds three `LordJobExtraToggle` rows: `forceVisitorsToLeave`, `boardColonyAnimals`, plus `boardColonyMechs` if Biotech is active
- `ApplyExtraToggle` writes the new value back to the dialog field via cached `FieldInfo`
- The dialog's `Start()` writes the checkbox values into `RitualBehaviorWorker_GravshipLaunch` before invoking the action callback — we don't replicate that, we just let the dialog do it via `OnAcceptKeyPressed → Start`

## Dependencies

**Requires:**
- ScreenReader/ (TolkHelper)
- Input/ (UnifiedKeyboardPatch)
- UI/ (TypeaheadSearchHelper, MenuHelper)
- World/ (StatBreakdownState for quality breakdown)
- Pawns/ (PawnInfoHelper for Alt+H/M/N/G)

**DLC:** Each adapter handles its own DLC presence via reflection. The mod loads cleanly without Anomaly or Odyssey; the corresponding adapter just never gets constructed.

## Priority in UnifiedKeyboardPatch
- 0.33: LordJobDialogState (covers all three Lord-job dialog types)
- 0.34: DryadCasteState
- 0.35: SplitCaravanState

## State Lifecycle

1. Subclass of `Dialog_BeginLordJob` opens
2. PostOpen patch (declarative for `Dialog_BeginRitual`, type-filtered Window.PostOpen for `Dialog_BeginPsychicRitual`) calls `LordJobDialogState.Open(dialog)`
3. Factory builds the appropriate adapter and seeds the state
4. User navigates/mutates via keyboard; mutations route through `adapter`
5. User starts (Alt+S) or cancels (Esc); state cleans up via `Window.PostClose` postfix and announces the adapter's closing line

## Testing Checklist

### Ideology rituals (regression)
- [ ] Wedding: open, navigate roles, assign/unassign, Tab to quality, Alt+S
- [ ] Funeral: forced/locked role behavior unchanged
- [ ] Conversion: spectator role enumeration works
- [ ] Childbirth: love-partner spectator suggestion still surfaces
- [ ] Toggling pawn correctly updates per-comp quality factors (Notify_AssignmentsChanged fix)
- [ ] Quality stats: Location row at top, Expected Quality row, dev points block for fluid ideologies, outcome chances appended at bottom

### Psychic rituals (new)
- [ ] Dialog opens with name, description, expected quality, role count
- [ ] Each role announces with Disallows line for non-default conditions
- [ ] Pawn list shows colonists with PsychicSensitivity
- [ ] Sleeping pawn assignment triggers `PsychicRitualWakingPawnWarning` after toggle
- [ ] Drafted pawn assignment triggers `PsychicRitualUndraftPawnWarning` after toggle
- [ ] Outcome warnings from `psychicRitualDef.OutcomeWarnings` appear in opening warnings and after toggles
- [ ] Alt+S starts the ritual; cooldown registers (verify gizmo)
- [ ] Escape cancels cleanly

### Gravship launch
- [ ] Three checkboxes appear at the bottom of the role list (or two if Biotech disabled)
- [ ] Up/Down navigates onto the checkboxes; Enter/Space toggles
- [ ] Toggle announcement: "{label}: {checked|unchecked}. {tooltip}"
- [ ] Alt+S launches with the new checkbox state respected (confirm animals/mechs/visitors behavior)

### Universal
- [ ] No Harmony warnings on mod load
- [ ] Typeahead works in role list AND pawn list
- [ ] Quality Stats mode shows outcome chances after factors
- [ ] Closing the dialog (Esc, X, Cancel button) all announce via the adapter's closing line
EOF
)