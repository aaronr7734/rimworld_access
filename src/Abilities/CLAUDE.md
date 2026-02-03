# Abilities Module

## Purpose
Screen reader accessibility for psycast and ability targeting, providing range information, affected target previews, and world map targeting support for abilities like Farskip.

## Files
**Patches:** AbilityTargetingPatch.cs
**States:** AbilityTargetingState.cs, WorldAbilityTargetingState.cs
**Helpers:** AbilityTargetingHelper.cs

## Key Shortcuts

### During Map Ability Targeting (Psycasts, etc.)
- **R** - Announce range info (distance to cursor, in/out of range, LOS status)
- **T** - Announce affected targets (AOE preview)
- **I** - Announce full ability info (name, level, range, costs)
- **Enter** - Confirm target (handled by TargetingPatch)
- **Escape** - Cancel targeting
- **Arrow Keys** - Move cursor (via MapNavigationState)

### During World Ability Targeting (Farskip)
- **I** - Announce full ability info
- **Enter** - Confirm destination
- **Escape** - Cancel and return to map
- **Arrow Keys** - Navigate world tiles (via WorldNavigationState)

## Architecture

### State Classes

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

### Helper Class

**AbilityTargetingHelper** - Utility methods for ability information extraction
- GetRange, GetAOERadius, HasAOE - Range and AOE properties
- GetPsyfocusCostString, GetEntropyGainString - Cost formatting
- CalculateDistance, IsInRange, HasLineOfSight - Validation helpers
- GetAffectedPawns - AOE target preview
- Build*Announcement methods - Format announcements

### Harmony Patches

**AbilityTargetingPatch**
- Patches `Targeter.BeginTargeting(ITargetingSource...)` to detect ability targeting start
- Patches `Targeter.StopTargeting` to close AbilityTargetingState
- Patches `WorldTargeter.StopTargeting` to close WorldAbilityTargetingState
- Patches `Command_Ability.ProcessInput` to detect world targeting start

### Integration with Existing Systems

**TargetingPatch.cs** (Combat module)
- Enhanced with ability-specific validation before standard ValidateTarget
- Checks psycast immunity, range, and line of sight with specific messages

**UnifiedKeyboardPatch.cs** (Input module)
- Priority 0.373: AbilityTargetingState input routing (R, T, I keys)
- Priority 0.376: WorldAbilityTargetingState input routing (Enter, Escape, I)

**WorldNavigationState.cs** (World module)
- AnnounceTile() enhanced to include ability destination info when WorldAbilityTargetingState.IsActive

## RimWorld Classes Used

| Class | Purpose |
|-------|---------|
| `Ability` | Base ability class with def, verb, pawn, GetAffectedTargets() |
| `Psycast` | Extends Ability with CanApplyPsycastTo() for immunity check |
| `AbilityDef` | Definition with range, EffectRadius, PsyfocusCost, EntropyGain, targetWorldCell |
| `Verb_CastAbility` | Verb for casting abilities, implements ITargetingSource |
| `Targeter` | Map targeting system with BeginTargeting/StopTargeting |
| `WorldTargeter` | World map targeting system |
| `Command_Ability` | Gizmo for activating abilities |

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

**Ability Info (I key):**
```
"Skip, level 4. Range: 28 tiles. Requires line of sight. Psyfocus cost: 2%. Neural heat: 30. Cooldown: 1 hour"
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
- [ ] I key announces full ability info
- [ ] Psycast immunity is detected and announced
- [ ] Out of range targets show specific error message
- [ ] No LOS targets show specific error message
- [ ] Enter confirms target (handled by TargetingPatch)
- [ ] Farskip opens world map and activates WorldAbilityTargetingState
- [ ] World tiles show ability destination validity info
- [ ] Enter on world map confirms destination
- [ ] Escape cancels targeting and returns to map

## Known Issues / Notes

**Ability Detection:**
- AbilityTargetingState opens when `Verb_CastAbility` is detected as targeting source
- WorldAbilityTargetingState opens when `ability.def.targetWorldCell == true`

**AOE Radius Precision:**
- Game uses float radius (e.g., 2.9, 3.9) which truncates to visual ring
- Announcements show raw value, may differ from visual ring by ~0.1

**Destination Selection:**
- Some abilities have two-phase targeting (target + destination)
- AbilityTargetingState doesn't re-open for destination phase
