# Abilities Module

## Purpose
Screen reader accessibility for psycast and ability targeting, providing range information, affected target previews, world map targeting support for abilities like Farskip, and equipment-based jump targeting (Jump Pack, Locust Armor).

## Files
**Patches:** AbilityTargetingPatch.cs
**States:** AbilityTargetingState.cs, WorldAbilityTargetingState.cs, ItemTargetingState.cs, JumpTargetingState.cs
**Helpers:** AbilityTargetingHelper.cs

## Key Shortcuts

### During Jump Targeting (Jump Pack, Locust Armor)
- **R** - Announce range info (distance, in/out of range, LOS, jump validity)
- **Enter** - Confirm jump target (handled by TargetingPatch)
- **Escape** - Cancel targeting
- **Arrow Keys** - Move cursor with per-tile validity prefixes ("Can jump", "Out of range", etc.)

### During Map Ability Targeting (Psycasts, etc.)
- **R** - Announce range info (distance to cursor, in/out of range, LOS status)
- **T** - Announce affected targets (AOE preview)
- **Enter** - Confirm target (handled by TargetingPatch)
- **Escape** - Cancel targeting
- **Arrow Keys** - Move cursor (via MapNavigationState)

### During World Ability Targeting (Farskip)
- **Enter** - Confirm destination
- **Escape** - Cancel and return to map
- **Arrow Keys** - Navigate world tiles (via WorldNavigationState)

## Architecture

### State Classes

**JumpTargetingState** - Manages equipment-based jump targeting (Jump Pack, Locust Armor)
- Verb_Jump extends Verb directly, not Verb_CastAbility/IAbilityVerb, so needs separate state
- Tracks Verb_Jump, caster pawn, position, map, and range from verb.EffectiveRange
- Provides per-tile validity prefixes during arrow navigation ("Can jump", "Out of range", etc.)
- Validates using game's JumpUtility.ValidJumpTarget() and GenSight.LineOfSight()
- Reports specific failure reasons: impassable, fogged, closed door, not walkable, no LOS, out of range
- Opens automatically when Verb_Jump targeting starts (detected in AbilityTargetingPatch)

**AbilityTargetingState** - Manages map-based ability targeting
- Tracks current ability being targeted
- Caches caster position for distance calculations
- Provides range validation and affected targets preview
- Opens automatically when Verb_CastAbility targeting starts

**WorldAbilityTargetingState** - Manages world map ability targeting (Farskip, etc.)
- Follows TransportPodLaunchState pattern
- Opens when abilities with `targetWorldCell = true` are activated
- Integrates with WorldNavigationState for tile navigation
- Uses ability.ValidateGlobalTarget() for destination validation

**ItemTargetingState** - Manages CompTargetable/CompUsable item targeting (sentience catalyst, mech serums, etc.)
- Lightweight: no custom key handlers (no R/T key support needed)
- Opened by BeginTargeting postfix when source is not an ability verb
- Closed by StopTargeting postfix
- Provides contextual announcements (start, success, error) via TargetingPatch integration
- Infers target type description from TargetingParameters (animal, pawn, building, etc.)

### Helper Class

**AbilityTargetingHelper** - Utility methods for ability information extraction
- GetRange, GetAOERadius, HasAOE - Range and AOE properties
- GetPsyfocusCostString, GetEntropyGainString - Cost formatting
- CalculateDistance, IsInRange, HasLineOfSight - Validation helpers
- GetAffectedPawns - AOE target preview
- Build*Announcement methods - Format announcements

### Harmony Patches

**AbilityTargetingPatch**
- Patches `Targeter.BeginTargeting(ITargetingSource...)` to detect ability and item targeting start
- Patches `Targeter.StopTargeting` to close AbilityTargetingState and ItemTargetingState
- Patches `WorldTargeter.StopTargeting` to close WorldAbilityTargetingState
- Patches `Command_Ability.ProcessInput` to detect world targeting start

### Integration with Existing Systems

**TargetingPatch.cs** (Combat module)
- Enhanced with ability-specific validation before standard ValidateTarget
- Checks psycast immunity, range, and line of sight with specific messages

**UnifiedKeyboardPatch.cs** (Input module)
- Priority 0.373: AbilityTargetingState input routing (R, T keys)
- Priority 0.376: WorldAbilityTargetingState input routing (Enter, Escape)

**WorldNavigationState.cs** (World module)
- AnnounceTile() enhanced to include ability destination info when WorldAbilityTargetingState.IsActive

## RimWorld Classes Used

| Class | Purpose |
|-------|---------|
| `Ability` | Base ability class with def, verb, pawn, GetAffectedTargets() |
| `Psycast` | Extends Ability with CanApplyPsycastTo() for immunity check |
| `AbilityDef` | Definition with range, EffectRadius, PsyfocusCost, EntropyGain, targetWorldCell |
| `Verb_CastAbility` | Verb for casting abilities, implements ITargetingSource, IAbilityVerb |
| `IAbilityVerb` | Interface for all ability verbs (Verb_CastAbility, Verb_AbilityShoot) |
| `CompAbilityEffect_WithDest` | Comp for dual-target abilities (Skip), implements ITargetingSource |
| `Targeter` | Map targeting system with BeginTargeting/StopTargeting |
| `WorldTargeter` | World map targeting system |
| `Command_Ability` | Gizmo for activating abilities |
| `Verb_Jump` | Equipment-based jump verb (Jump Pack, Locust Armor) - extends Verb directly |
| `JumpUtility` | Static methods for jump validation (ValidJumpTarget) and execution |
| `CompApparelReloadable` | Manages charges for reloadable apparel (jump pack fuel) |

## Announcement Formats

**Targeting Start:**
```
"Skip targeting. Range: 28 tiles."
"Berserk Pulse targeting. Range: 15 tiles, AOE radius: 3 tiles. Psyfocus: 2%, Neural heat: 25."
```

**Range Info (R key):**
```
"Distance: 12 tiles, IN RANGE"
"Distance: 35 tiles, OUT OF RANGE (max 28)"
"Distance: 8 tiles, IN RANGE, NO LINE OF SIGHT"
```

**Affected Targets (T key):**
```
"Affected: 3 - Colonist A, Pirate B, Muffalo C"
"Target: Pirate Captain"
"No valid targets at cursor"
```

**Immunity Warning:**
```
"Mechanoid Centipede is immune to psychic effects"
```

## Dependencies
**Requires:** ScreenReader/, Input/, Map/ (cursor position), World/ (world navigation), Combat/ (TargetingPatch)
**Used by:** None (self-contained)

## Testing Checklist
- [ ] Psycast targeting announces ability info on start
- [ ] R key announces distance and range status
- [ ] T key announces affected targets for AOE abilities
- [ ] Psycast immunity is detected and announced
- [ ] Out of range targets show specific error message
- [ ] No LOS targets show specific error message
- [ ] Enter confirms target (handled by TargetingPatch)
- [ ] Farskip opens world map and activates WorldAbilityTargetingState
- [ ] World tiles show ability destination validity info
- [ ] Enter on world map confirms destination
- [ ] Escape cancels targeting and returns to map
- [ ] CompUsable/CompTargetable items announce targeting mode on start
- [ ] Enter on valid animal confirms item targeting with contextual message
- [ ] Enter on empty tile during item targeting speaks error message
- [ ] Escape cancels item targeting cleanly
- [ ] Jump Pack gizmo activation announces range and charges
- [ ] Arrow navigation during jump targeting shows per-tile validity prefixes
- [ ] R key announces detailed distance, range, LOS, and jump validity
- [ ] Enter on valid jump tile confirms with success message
- [ ] Enter on invalid jump tile shows specific error and stays in targeting
- [ ] Escape cancels jump targeting cleanly
- [ ] Locust Armor uses same jump targeting flow

## Known Issues / Notes

**Ability Detection:**
- JumpTargetingState opens when `Verb_Jump` is detected (Jump Pack, Locust Armor - equipment verbs extending Verb directly)
- AbilityTargetingState opens when any `IAbilityVerb` is detected as targeting source (catches Verb_CastAbility, Verb_CastAbilityJump, Verb_CastAbilityTouch, Verb_AbilityShoot, and modded verbs)
- WorldAbilityTargetingState opens when `ability.def.targetWorldCell == true`
- Self-cast abilities (`targetRequired == false`) are announced by GizmoNavigationState
- ItemTargetingState opens for any other ITargetingSource (CompTargetable, CompUsable items)

**AOE Radius Precision:**
- Game uses float radius (e.g., 2.9, 3.9) which truncates to visual ring
- Announcements show raw value, may differ from visual ring by ~0.1

**Destination Selection (Dual-Target Abilities):**
- Abilities with `CompAbilityEffect_WithDest` (e.g., Skip) have two-phase targeting
- After first target, AbilityTargetingState.EnterDestinationPhase() updates range context
- Range origin shifts from caster to selected target; range uses destination comp's range
- R key reports distance from selected target during destination phase

**Touch Range Abilities:**
- `Verb_CastAbilityTouch` and zero-range abilities announce "Touch range"
- Range validation is skipped (game handles reachability internally)
