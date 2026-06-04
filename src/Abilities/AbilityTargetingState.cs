using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State management for ability/psycast map targeting.
    /// Tracks when an ability is being targeted and provides range/affected target announcements.
    /// </summary>
    public static class AbilityTargetingState
    {
        private static bool isActive = false;
        private static Ability currentAbility = null;
        private static IntVec3 casterPosition = IntVec3.Invalid;
        private static Map casterMap = null;
        private static bool isDestinationPhase = false;
        private static float destinationRange = 0f;

        /// <summary>
        /// Gets whether ability targeting mode is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets the current ability being targeted.
        /// </summary>
        public static Ability CurrentAbility => currentAbility;

        /// <summary>
        /// Gets the caster's position for distance calculations.
        /// </summary>
        public static IntVec3 CasterPosition => casterPosition;

        /// <summary>
        /// Opens ability targeting state when an ability begins targeting.
        /// </summary>
        public static void Open(Ability ability)
        {
            if (ability == null)
            {
                Log.Warning("RimWorld Access: AbilityTargetingState.Open called with null ability");
                return;
            }

            currentAbility = ability;
            casterPosition = ability.pawn?.Position ?? IntVec3.Invalid;
            casterMap = ability.pawn?.Map;
            isActive = true;

            // Announce targeting start
            string announcement = AbilityTargetingHelper.BuildTargetingStartAnnouncement(ability);
            TolkHelper.Speak(announcement, SpeechPriority.Normal);
        }

        /// <summary>
        /// Closes ability targeting state.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentAbility = null;
            casterPosition = IntVec3.Invalid;
            casterMap = null;
            isDestinationPhase = false;
            destinationRange = 0f;
        }

        /// <summary>
        /// Transitions to destination phase for dual-target abilities (e.g., Skip).
        /// Updates the range origin to the first selected target and uses the destination
        /// comp's range instead of the ability's verb range.
        /// Called from TargetingPatch when DestinationSelector is detected.
        /// </summary>
        public static void EnterDestinationPhase(IntVec3 selectedTargetPos, float destRange)
        {
            casterPosition = selectedTargetPos;  // Range is now measured from the selected target
            destinationRange = destRange;
            isDestinationPhase = true;
        }


        /// <summary>
        /// Announces range and distance information for the current cursor position.
        /// Called when user presses R key during targeting.
        /// </summary>
        public static void AnnounceRangeInfo()
        {
            if (!isActive || currentAbility == null)
            {
                TolkHelper.Speak("RimWorldAccess.Abilities.State.NoAbilityTargeting".Loc(), SpeechPriority.Normal);
                return;
            }

            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
            if (!cursorPos.IsValid)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.InvalidCursorPosition".Loc(), SpeechPriority.Normal);
                return;
            }

            string announcement;
            if (isDestinationPhase && destinationRange > 0f)
            {
                // During destination phase, use destination-specific range from the selected target
                float distance = AbilityTargetingHelper.CalculateDistance(casterPosition, cursorPos);
                var sb = new System.Text.StringBuilder();
                sb.Append("RimWorldAccess.Abilities.Range.Distance".Translate(distance.ToString("F0")));
                if (distance <= destinationRange)
                    sb.Append("RimWorldAccess.Abilities.Range.InRange".Translate());
                else
                    sb.Append("RimWorldAccess.Abilities.Range.OutOfRange".Translate(destinationRange.ToString("F0")));
                announcement = sb.ToString();
            }
            else
            {
                announcement = AbilityTargetingHelper.BuildRangeInfoAnnouncement(
                    currentAbility, casterPosition, cursorPos, casterMap);
            }
            TolkHelper.Speak(announcement, SpeechPriority.Normal);
        }

        /// <summary>
        /// Announces affected targets at the current cursor position.
        /// Called when user presses T key during targeting.
        /// </summary>
        public static void AnnounceAffectedTargets()
        {
            if (!isActive || currentAbility == null)
            {
                TolkHelper.Speak("RimWorldAccess.Abilities.State.NoAbilityTargeting".Loc(), SpeechPriority.Normal);
                return;
            }

            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
            if (!cursorPos.IsValid || casterMap == null)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.InvalidCursorPosition".Loc(), SpeechPriority.Normal);
                return;
            }

            string announcement = AbilityTargetingHelper.BuildAffectedTargetsAnnouncement(
                currentAbility, cursorPos, casterMap);
            TolkHelper.Speak(announcement, SpeechPriority.Normal);
        }

        /// <summary>
        /// Handles keyboard input during ability targeting.
        /// Returns true if input was handled.
        /// </summary>
        public static bool HandleInput(UnityEngine.KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive)
                return false;

            // R - Announce range info
            if (key == UnityEngine.KeyCode.R && !shift && !ctrl && !alt)
            {
                AnnounceRangeInfo();
                return true;
            }

            // T - Announce affected targets
            if (key == UnityEngine.KeyCode.T && !shift && !ctrl && !alt)
            {
                AnnounceAffectedTargets();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets immunity message for a target, if applicable.
        /// Used by TargetingPatch to provide specific rejection messages.
        /// </summary>
        public static string GetImmunityMessage(LocalTargetInfo target)
        {
            if (!isActive || currentAbility == null)
                return null;

            return AbilityTargetingHelper.GetImmunityMessage(currentAbility, target);
        }

        /// <summary>
        /// Validates that a target is in range for the current ability.
        /// Returns an error message if out of range, or null if valid.
        /// </summary>
        public static string ValidateRange(IntVec3 targetPos)
        {
            if (!isActive || currentAbility == null)
                return null;

            float range = isDestinationPhase ? destinationRange : AbilityTargetingHelper.GetRange(currentAbility);

            // Zero range means touch/melee - game handles reachability internally
            if (range <= 0f)
                return null;

            float distance = AbilityTargetingHelper.CalculateDistance(casterPosition, targetPos);
            if (distance > range)
            {
                return "RimWorldAccess.Combat.Target.OutOfRange"
                    .Translate(distance.ToString("F0"), range.ToString("F0"));
            }

            return null;
        }

        /// <summary>
        /// Validates line of sight to target.
        /// Returns an error message if no LOS, or null if valid.
        /// </summary>
        public static string ValidateLineOfSight(IntVec3 targetPos)
        {
            if (!isActive || currentAbility == null || currentAbility.pawn == null)
                return null;

            if (AbilityTargetingHelper.RequiresLineOfSight(currentAbility))
            {
                if (!AbilityTargetingHelper.HasLineOfSight(currentAbility.pawn, targetPos))
                {
                    return "RimWorldAccess.Abilities.Validate.LineOfSight".Translate();
                }
            }

            return null;
        }

        /// <summary>
        /// Validates that there is a valid target present when the ability requires one.
        /// This prevents the confusing "out of range" message when targeting empty cells
        /// with abilities that require a pawn target.
        /// Returns an error message if no valid target, or null if valid.
        /// </summary>
        public static string ValidateTargetPresent(LocalTargetInfo target, IntVec3 cursorPos)
        {
            if (!isActive || currentAbility == null || casterMap == null)
                return null;

            // If the ability can target locations (like Wallraise, Smokepop), any cell is valid
            if (AbilityTargetingHelper.CanTargetLocations(currentAbility))
                return null;

            // If we have a thing target, it's valid
            if (target.HasThing)
                return null;

            // Check if there's a pawn at the cursor position
            var pawn = cursorPos.GetFirstPawn(casterMap);
            if (pawn != null)
                return null; // There is a pawn, let the game's validation handle specifics

            // No valid target directly at cursor
            // For AOE abilities, explain that you must target a pawn (the AOE spreads from them)
            if (currentAbility.def.HasAreaOfEffect)
            {
                float radius = currentAbility.def.EffectRadius;
                // Check if there are pawns nearby that could be targeted
                var nearby = AbilityTargetingHelper.GetAffectedPawns(currentAbility, cursorPos, casterMap);
                if (nearby.Count > 0)
                {
                    string names = string.Join(", ", nearby.Select(p => p.LabelShort));
                    return "RimWorldAccess.Abilities.Validate.NoPawnAoeNearby".Translate(names);
                }
                return "RimWorldAccess.Abilities.Validate.NoPawnAoeRadius".Translate(radius.ToString("F0"));
            }

            string requirement = AbilityTargetingHelper.GetTargetRequirementDescription(currentAbility);
            return "RimWorldAccess.Abilities.Validate.NoValidAtCursor".Translate(requirement);
        }

        /// <summary>
        /// Builds the success announcement including AOE affected targets.
        /// </summary>
        public static string BuildSuccessAnnouncement(LocalTargetInfo target, IntVec3 cursorPos)
        {
            if (!isActive || currentAbility == null || casterMap == null)
            {
                string label = target.HasThing
                    ? target.Thing.LabelShort
                    : "RimWorldAccess.Abilities.Label.Location".Translate().ToString();
                return "RimWorldAccess.Abilities.Success.TargetingLocation".Translate(label);
            }

            // For AOE abilities, list all affected pawns
            if (currentAbility.def.HasAreaOfEffect)
            {
                var affected = AbilityTargetingHelper.GetAffectedPawns(currentAbility, cursorPos, casterMap);

                if (affected.Count == 0)
                {
                    // For location-targeting abilities (Solar Pinhole, Wallraise, etc.),
                    // no pawns is expected - just confirm the location
                    if (AbilityTargetingHelper.CanTargetLocations(currentAbility))
                    {
                        var terrain = casterMap.terrainGrid.TerrainAt(cursorPos);
                        string terrainName = terrain?.label
                            ?? "RimWorldAccess.Abilities.Label.Ground".Translate().ToString();
                        return "RimWorldAccess.Abilities.Success.TargetingLocation".Translate(terrainName);
                    }
                    return "RimWorldAccess.Abilities.Success.TargetingLocationNoPawns".Translate();
                }
                else if (affected.Count == 1)
                {
                    return "RimWorldAccess.Abilities.Success.TargetingOne".Translate(affected[0].LabelShort);
                }
                else
                {
                    // Use RimWorld's ToCommaList with useAnd for proper grammar (A, B, and C)
                    var names = affected.Select(p => p.LabelShort).ToCommaList(useAnd: true);
                    return "RimWorldAccess.Abilities.Success.TargetingMany".Translate(affected.Count, names);
                }
            }

            // For non-AOE abilities
            if (target.HasThing)
            {
                return "RimWorldAccess.Abilities.Success.TargetingOne".Translate(target.Thing.LabelShort);
            }
            else
            {
                // Cell-only target (like Wallraise, Smokepop)
                // Try to describe what's at the cell
                var terrain = casterMap.terrainGrid.TerrainAt(cursorPos);
                string terrainName = terrain?.label
                    ?? "RimWorldAccess.Abilities.Label.Ground".Translate().ToString();
                return "RimWorldAccess.Abilities.Success.TargetingLocation".Translate(terrainName);
            }
        }
    }
}
