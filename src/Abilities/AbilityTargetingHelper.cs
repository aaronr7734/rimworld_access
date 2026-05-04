using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper methods for extracting and formatting ability targeting information.
    /// </summary>
    public static class AbilityTargetingHelper
    {
        /// <summary>
        /// Gets the effective casting range of an ability.
        /// Uses the verb's EffectiveRange which accounts for equipment bonuses (e.g., jump pack range).
        /// Returns 0 for touch/melee abilities (no ranged check - game uses reachability instead).
        /// </summary>
        public static float GetRange(Ability ability)
        {
            // Use verb's EffectiveRange when available (accounts for stat bonuses like JumpRange)
            if (ability?.verb is Verb_CastAbility castVerb)
                return castVerb.EffectiveRange;

            if (ability?.def?.verbProperties == null)
                return 0f;

            return ability.def.verbProperties.range;
        }

        /// <summary>
        /// Checks if an ability uses touch/melee range (no ranged distance check).
        /// </summary>
        public static bool IsTouchRange(Ability ability)
        {
            return ability?.verb is Verb_CastAbilityTouch || GetRange(ability) <= 0f;
        }

        /// <summary>
        /// Gets the AOE radius of an ability, or 0 if not an AOE ability.
        /// </summary>
        public static float GetAOERadius(Ability ability)
        {
            if (ability?.def == null)
                return 0f;

            return ability.def.EffectRadius;
        }

        /// <summary>
        /// Checks if an ability has area of effect.
        /// </summary>
        public static bool HasAOE(Ability ability)
        {
            return ability?.def?.HasAreaOfEffect ?? false;
        }

        /// <summary>
        /// Checks if an ability is a psycast.
        /// </summary>
        public static bool IsPsycast(Ability ability)
        {
            return ability is Psycast;
        }

        /// <summary>
        /// Checks if an ability can target empty locations (cells without things).
        /// </summary>
        public static bool CanTargetLocations(Ability ability)
        {
            return ability?.verb?.targetParams?.canTargetLocations ?? false;
        }

        /// <summary>
        /// Checks if an ability requires a pawn target.
        /// </summary>
        public static bool RequiresPawnTarget(Ability ability)
        {
            var targetParams = ability?.verb?.targetParams;
            if (targetParams == null)
                return false;

            // If it can target locations, it doesn't require a pawn
            if (targetParams.canTargetLocations)
                return false;

            // If it can only target pawns, it requires a pawn
            return targetParams.canTargetPawns;
        }

        /// <summary>
        /// Gets a description of what kind of target the ability requires.
        /// </summary>
        public static string GetTargetRequirementDescription(Ability ability)
        {
            var targetParams = ability?.verb?.targetParams;
            if (targetParams == null)
                return "unknown";

            if (targetParams.canTargetLocations)
                return "location or pawn";

            if (targetParams.canTargetPawns && targetParams.canTargetAnimals && targetParams.canTargetHumans)
                return "pawn";

            if (targetParams.canTargetPawns && targetParams.canTargetHumans && !targetParams.canTargetAnimals)
                return "humanlike pawn";

            if (targetParams.canTargetPawns && targetParams.canTargetAnimals && !targetParams.canTargetHumans)
                return "animal";

            if (targetParams.canTargetBuildings)
                return "building or structure";

            return "valid target";
        }

        /// <summary>
        /// Gets the psyfocus cost of an ability as a percentage string.
        /// Returns null if not a psycast or no psyfocus cost.
        /// </summary>
        public static string GetPsyfocusCostString(Ability ability)
        {
            if (ability?.def == null)
                return null;

            float cost = ability.def.PsyfocusCost;
            if (cost <= float.Epsilon)
                return null;

            return $"{(cost * 100f):F0}%";
        }

        /// <summary>
        /// Gets the entropy (neural heat) gain of an ability.
        /// Returns null if no entropy gain.
        /// </summary>
        public static string GetEntropyGainString(Ability ability)
        {
            if (ability?.def == null)
                return null;

            float entropy = ability.def.EntropyGain;
            if (entropy <= float.Epsilon)
                return null;

            return $"{entropy:F0}";
        }

        /// <summary>
        /// Calculates the distance between two positions.
        /// </summary>
        public static float CalculateDistance(IntVec3 from, IntVec3 to)
        {
            return (to - from).LengthHorizontal;
        }

        /// <summary>
        /// Checks if target is within ability range.
        /// </summary>
        public static bool IsInRange(Ability ability, IntVec3 casterPos, IntVec3 targetPos)
        {
            // Touch range - game handles reachability, we skip range check
            if (IsTouchRange(ability))
                return true;

            float range = GetRange(ability);

            // Range 0 means unlimited or self-only
            if (range <= float.Epsilon)
                return true;

            float distance = CalculateDistance(casterPos, targetPos);
            return distance <= range;
        }

        /// <summary>
        /// Checks line of sight between caster and target.
        /// </summary>
        public static bool HasLineOfSight(Pawn caster, IntVec3 targetPos)
        {
            if (caster?.Map == null)
                return false;

            return GenSight.LineOfSight(caster.Position, targetPos, caster.Map);
        }

        /// <summary>
        /// Checks if an ability requires line of sight.
        /// </summary>
        public static bool RequiresLineOfSight(Ability ability)
        {
            return ability?.def?.verbProperties?.requireLineOfSight ?? true;
        }

        /// <summary>
        /// Checks if a psycast can be applied to a target (psychic immunity check).
        /// </summary>
        public static bool CanApplyPsycastTo(Ability ability, LocalTargetInfo target)
        {
            if (ability is Psycast psycast)
            {
                return psycast.CanApplyPsycastTo(target);
            }
            return true;
        }

        /// <summary>
        /// Gets the label for a target (pawn name, thing label, or cell position).
        /// </summary>
        public static string GetTargetLabel(LocalTargetInfo target)
        {
            if (!target.IsValid)
                return "invalid";

            if (target.HasThing)
            {
                return target.Thing.LabelShort ?? target.Thing.Label ?? "unknown";
            }

            return target.Cell.ToString();
        }

        /// <summary>
        /// Gets pawns that would be affected by an AOE ability at a given position.
        /// </summary>
        public static List<Pawn> GetAffectedPawns(Ability ability, IntVec3 center, Map map)
        {
            var affected = new List<Pawn>();

            if (ability?.def == null || map == null)
                return affected;

            if (!ability.def.HasAreaOfEffect)
            {
                // Single target - check if there's a pawn at center
                var pawn = center.GetFirstPawn(map);
                if (pawn != null)
                {
                    affected.Add(pawn);
                }
                return affected;
            }

            float radius = ability.def.EffectRadius;

            // Get all things in radius and filter to pawns
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(center, map, radius, useCenter: true))
            {
                if (thing is Pawn pawn)
                {
                    // Check if ability can target this pawn type
                    var targetParams = ability.verb?.targetParams;
                    if (targetParams != null && !targetParams.CanTarget(pawn))
                        continue;

                    // Check psychic immunity for psycasts
                    if (!CanApplyPsycastTo(ability, pawn))
                        continue;

                    affected.Add(pawn);
                }
            }

            return affected;
        }

        /// <summary>
        /// Builds the initial targeting announcement string.
        /// Format: "{AbilityName} targeting. Range: {range} tiles. {cost info}"
        /// </summary>
        public static string BuildTargetingStartAnnouncement(Ability ability)
        {
            if (ability?.def == null)
                return "Ability targeting";

            var sb = new StringBuilder();
            sb.Append(ability.def.LabelCap);
            sb.Append(" targeting");

            // Range info
            if (IsTouchRange(ability))
            {
                sb.Append(". Touch range");
            }
            else
            {
                float range = GetRange(ability);
                if (range > 0)
                {
                    sb.Append($". Range: {range:F0} tiles");
                }
            }

            // AOE radius
            if (HasAOE(ability))
            {
                float aoeRadius = GetAOERadius(ability);
                sb.Append($", AOE radius: {aoeRadius:F0} tiles");
            }

            // Psyfocus cost
            string psyfocus = GetPsyfocusCostString(ability);
            if (psyfocus != null)
            {
                sb.Append($". Psyfocus: {psyfocus}");
            }

            // Neural heat gain
            string entropy = GetEntropyGainString(ability);
            if (entropy != null)
            {
                sb.Append($", Neural heat: {entropy}");
            }

            // Add keyboard hints as a separate sentence
            if (HasAOE(ability))
            {
                sb.Append(". Press R to check distance and line of sight, T to see affected targets.");
            }
            else
            {
                sb.Append(". Press R to check distance and line of sight.");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Builds range info announcement.
        /// Format: "Distance: {dist} tiles, IN RANGE" or "Distance: {dist} tiles, OUT OF RANGE (max {range})"
        /// </summary>
        public static string BuildRangeInfoAnnouncement(Ability ability, IntVec3 casterPos, IntVec3 cursorPos, Map map)
        {
            if (ability?.def == null)
                return "No ability active";

            var sb = new StringBuilder();

            float distance = CalculateDistance(casterPos, cursorPos);
            sb.Append($"Distance: {distance:F0} tiles");

            // Check if in range
            if (IsTouchRange(ability))
            {
                // Touch range - game uses reachability, we just report distance
                bool adjacent = casterPos.AdjacentTo8WayOrInside(cursorPos);
                sb.Append(adjacent ? ", IN RANGE (touch)" : ", must be adjacent (touch range)");
            }
            else
            {
                float range = GetRange(ability);
                if (range > 0)
                {
                    if (distance <= range)
                    {
                        sb.Append(", IN RANGE");
                    }
                    else
                    {
                        sb.Append($", OUT OF RANGE (max {range:F0})");
                    }
                }
            }

            // Check line of sight if required and in range
            if (RequiresLineOfSight(ability) && ability.pawn != null && map != null)
            {
                bool hasLOS = HasLineOfSight(ability.pawn, cursorPos);
                if (!hasLOS)
                {
                    sb.Append(", NO LINE OF SIGHT");
                }
            }

            // Check if there's a valid target at cursor
            if (map != null && !CanTargetLocations(ability))
            {
                var pawn = cursorPos.GetFirstPawn(map);
                if (pawn == null)
                {
                    string requirement = GetTargetRequirementDescription(ability);
                    sb.Append($". No {requirement} at cursor");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Builds affected targets announcement.
        /// Format: "Affected: {count} - {names}" or "No valid targets at cursor"
        /// </summary>
        public static string BuildAffectedTargetsAnnouncement(Ability ability, IntVec3 cursorPos, Map map)
        {
            if (ability?.def == null || map == null)
                return "No ability active";

            var affected = GetAffectedPawns(ability, cursorPos, map);

            if (affected.Count == 0)
            {
                // No valid targets - explain what's needed
                if (!CanTargetLocations(ability))
                {
                    string requirement = GetTargetRequirementDescription(ability);
                    if (ability.def.HasAreaOfEffect)
                    {
                        return $"No valid targets. This ability requires targeting a {requirement}, then affects others within {ability.def.EffectRadius:F0} tile radius";
                    }
                    return $"No valid target at cursor. This ability requires a {requirement}";
                }
                return "No pawns within AOE radius";
            }

            // Build the target we'll show comp warnings for. Pawn-target abilities
            // (e.g., bloodfeed) ignore cell-only LocalTargetInfo — wrap the affected
            // pawn directly so the comp can read target.Pawn.
            LocalTargetInfo warnTarget = affected.Count > 0
                ? new LocalTargetInfo(affected[0])
                : new LocalTargetInfo(cursorPos);
            string warnings = GetExtraTargetWarnings(ability, warnTarget);

            if (!ability.def.HasAreaOfEffect)
            {
                // Single target
                string targetLine = $"Target: {affected[0].LabelShort}";
                if (!string.IsNullOrEmpty(warnings))
                    targetLine += $". Warning: {warnings}";
                return targetLine;
            }

            // AOE - list affected
            var sb = new StringBuilder();
            sb.Append($"Affected: {affected.Count}");

            // List first few names
            int maxNames = 5;
            var names = affected.Take(maxNames).Select(p => p.LabelShort);
            sb.Append(" - ");
            sb.Append(string.Join(", ", names));

            if (affected.Count > maxNames)
            {
                sb.Append($", and {affected.Count - maxNames} more");
            }

            if (!string.IsNullOrEmpty(warnings))
                sb.Append($". Warning: {warnings}");

            return sb.ToString();
        }

        /// <summary>
        /// Collects mouse-attachment warnings from each of the ability's effect comps.
        /// These are the cursor-side messages sighted players see (e.g., bloodfeed's
        /// "Will kill" / "Will cause serious blood loss"). Returns null if no comp
        /// returns a non-empty string. Multiple warnings are joined with periods so
        /// they read naturally without using newlines as separators.
        /// </summary>
        public static string GetExtraTargetWarnings(Ability ability, LocalTargetInfo target)
        {
            if (ability?.comps == null || !target.IsValid)
                return null;

            var warnings = new List<string>();
            foreach (var comp in ability.comps)
            {
                // ExtraLabelMouseAttachment is defined on CompAbilityEffect, not the
                // base AbilityComp — only effect comps emit cursor-side warnings.
                if (!(comp is CompAbilityEffect effectComp)) continue;
                string text = null;
                try { text = effectComp.ExtraLabelMouseAttachment(target); }
                catch { continue; }
                if (string.IsNullOrEmpty(text)) continue;
                // Strip color tags and flatten any newlines the comp embedded.
                string clean = text.StripTags()
                    .Replace("\r", " ")
                    .Replace("\n", ". ")
                    .Trim();
                if (!string.IsNullOrEmpty(clean))
                    warnings.Add(clean);
            }
            if (warnings.Count == 0) return null;
            return string.Join(". ", warnings);
        }

        /// <summary>
        /// Gets an immunity message if target is immune.
        /// Returns null if target is not immune.
        /// </summary>
        public static string GetImmunityMessage(Ability ability, LocalTargetInfo target)
        {
            if (!target.HasThing || !(target.Thing is Pawn pawn))
                return null;

            if (ability is Psycast psycast)
            {
                if (!psycast.CanApplyPsycastTo(target))
                {
                    return $"{pawn.LabelShort} is immune to psychic effects";
                }
            }

            return null;
        }
    }
}
