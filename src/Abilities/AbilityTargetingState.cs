using System;
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
        }

        /// <summary>
        /// Announces range and distance information for the current cursor position.
        /// Called when user presses R key during targeting.
        /// </summary>
        public static void AnnounceRangeInfo()
        {
            if (!isActive || currentAbility == null)
            {
                TolkHelper.Speak("No ability targeting active", SpeechPriority.Normal);
                return;
            }

            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
            if (!cursorPos.IsValid)
            {
                TolkHelper.Speak("Invalid cursor position", SpeechPriority.Normal);
                return;
            }

            string announcement = AbilityTargetingHelper.BuildRangeInfoAnnouncement(
                currentAbility, casterPosition, cursorPos, casterMap);
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
                TolkHelper.Speak("No ability targeting active", SpeechPriority.Normal);
                return;
            }

            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
            if (!cursorPos.IsValid || casterMap == null)
            {
                TolkHelper.Speak("Invalid cursor position", SpeechPriority.Normal);
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

            if (!AbilityTargetingHelper.IsInRange(currentAbility, casterPosition, targetPos))
            {
                float distance = AbilityTargetingHelper.CalculateDistance(casterPosition, targetPos);
                float range = AbilityTargetingHelper.GetRange(currentAbility);
                return $"Out of range. Distance: {distance:F0}, max range: {range:F0}";
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
                    return "No line of sight to target";
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
                    return $"No pawn at cursor. Nearby targets: {names}. Move cursor directly onto a pawn to target them";
                }
                return $"No pawn at cursor. This AOE ability requires targeting a pawn directly, then affects others within {radius:F0} tiles";
            }

            string requirement = AbilityTargetingHelper.GetTargetRequirementDescription(currentAbility);
            return $"No {requirement} at cursor. Move cursor onto a valid target";
        }

        /// <summary>
        /// Builds the success announcement including AOE affected targets.
        /// </summary>
        public static string BuildSuccessAnnouncement(LocalTargetInfo target, IntVec3 cursorPos)
        {
            if (!isActive || currentAbility == null || casterMap == null)
            {
                string label = target.HasThing ? target.Thing.LabelShort : "location";
                return $"Targeting: {label}";
            }

            // For AOE abilities, list all affected pawns
            if (currentAbility.def.HasAreaOfEffect)
            {
                var affected = AbilityTargetingHelper.GetAffectedPawns(currentAbility, cursorPos, casterMap);

                if (affected.Count == 0)
                {
                    return "Targeting location. No pawns in radius";
                }
                else if (affected.Count == 1)
                {
                    return $"Targeting: {affected[0].LabelShort}";
                }
                else
                {
                    // Use RimWorld's ToCommaList with useAnd for proper grammar (A, B, and C)
                    var names = affected.Select(p => p.LabelShort).ToCommaList(useAnd: true);
                    return $"Targeting {affected.Count} pawns: {names}";
                }
            }

            // For non-AOE abilities
            if (target.HasThing)
            {
                return $"Targeting: {target.Thing.LabelShort}";
            }
            else
            {
                // Cell-only target (like Wallraise, Smokepop)
                // Try to describe what's at the cell
                var terrain = casterMap.terrainGrid.TerrainAt(cursorPos);
                string terrainName = terrain?.label ?? "ground";
                return $"Targeting: {terrainName}";
            }
        }
    }
}
