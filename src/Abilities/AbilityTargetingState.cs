using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Targeting session state for abilities and psycasts, with the range, affected-target
    /// and rejection-diagnosis announcements the map scope speaks during targeting.
    /// </summary>
    public static class AbilityTargetingState
    {
        private static bool isActive = false;
        private static Ability currentAbility = null;
        private static IntVec3 casterPosition = IntVec3.Invalid;
        private static Map casterMap = null;
        private static bool isDestinationPhase = false;
        private static float destinationRange = 0f;
        private static bool destinationRequiresLineOfSight = false;
        private static CompAbilityEffect_WithDest destinationComp = null;

        public static bool IsActive => isActive;

        /// <summary>
        /// Whether targeting is in the destination-selection phase of a dual-target ability.
        /// Callers must gate on it: the game emits no Messages for destination rejections.
        /// </summary>
        public static bool IsDestinationPhase => isDestinationPhase;

        public static Ability CurrentAbility => currentAbility;

        /// <summary>Range/LOS origin: the caster, or the first target once in destination phase.</summary>
        public static IntVec3 CasterPosition => casterPosition;

        /// <summary>Opens the session and announces that targeting started.</summary>
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

            string announcement = AbilityTargetingHelper.BuildTargetingStartAnnouncement(ability);
            TolkHelper.SpeakData(announcement, SpeechPriority.Normal);
        }

        public static void Close()
        {
            isActive = false;
            currentAbility = null;
            casterPosition = IntVec3.Invalid;
            casterMap = null;
            isDestinationPhase = false;
            destinationRange = 0f;
            destinationRequiresLineOfSight = false;
            destinationComp = null;
        }

        /// <summary>
        /// Enters the destination phase of a dual-target ability: the range origin moves to
        /// the selected target and the comp's own range replaces the verb's.
        /// </summary>
        public static void EnterDestinationPhase(IntVec3 selectedTargetPos, CompAbilityEffect_WithDest destComp)
        {
            casterPosition = selectedTargetPos;
            destinationComp = destComp;
            destinationRange = destComp?.Props?.range ?? 0f;
            destinationRequiresLineOfSight = destComp?.Props?.requiresLineOfSight ?? false;
            isDestinationPhase = true;
        }

        /// <summary>
        /// Null when the destination cell is a valid teleport target, else the reason it is
        /// not. Wraps <c>CanPlaceSelectedTargetAt</c>, which answers only yes/no, with the
        /// reason detection that gives the same information as the ring color.
        /// </summary>
        public static string ValidateDestinationCell(IntVec3 cursorPos)
        {
            if (!isDestinationPhase || destinationComp == null || casterMap == null)
                return null;

            var target = new LocalTargetInfo(cursorPos);
            if (destinationComp.CanPlaceSelectedTargetAt(target))
                return null;

            if (cursorPos.Impassable(casterMap))
                return (string)"RimWorldAccess.Abilities.Teleport.Impassable".Translate();

            var door = cursorPos.GetDoor(casterMap);
            if (door != null && !door.Open)
                return (string)"RimWorldAccess.Abilities.Teleport.DoorClosed".Translate();

            foreach (var thing in cursorPos.GetThingList(casterMap))
            {
                if (thing.def.category == ThingCategory.Item)
                    return (string)"RimWorldAccess.Abilities.Teleport.ItemInCell".Translate(thing.LabelShort);
            }

            var edifice = cursorPos.GetEdifice(casterMap);
            if (edifice != null)
                return (string)"RimWorldAccess.Abilities.Teleport.BlockedBy".Translate(edifice.LabelShort);

            if (!cursorPos.Standable(casterMap))
                return (string)"RimWorldAccess.Abilities.Teleport.NotStandable".Translate();

            return (string)"RimWorldAccess.Abilities.Teleport.Generic".Translate();
        }

        /// <summary>R: announces range and distance for the cursor position.</summary>
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
                float distance = AbilityTargetingHelper.CalculateDistance(casterPosition, cursorPos);
                var sb = new System.Text.StringBuilder();
                sb.Append("RimWorldAccess.Abilities.Range.Distance".Translate(distance.ToString("F0")));
                if (distance <= destinationRange)
                    sb.Append("RimWorldAccess.Abilities.Range.InRange".Translate());
                else
                    sb.Append("RimWorldAccess.Abilities.Range.OutOfRange".Translate(destinationRange.ToString("F0")));
                announcement = sb.ToString();

                // LOS and cell validity ride along so one R press gives the full picture.
                if (destinationRequiresLineOfSight && casterMap != null
                    && !GenSight.LineOfSight(casterPosition, cursorPos, casterMap))
                {
                    announcement += (string)"RimWorldAccess.Abilities.Range.NoLineOfSight".Translate();
                }
                string cellError = ValidateDestinationCell(cursorPos);
                if (cellError != null)
                    announcement += $". {cellError}";
                else
                    announcement += (string)"RimWorldAccess.Abilities.Destination.ValidTeleport".Translate();
            }
            else
            {
                announcement = AbilityTargetingHelper.BuildRangeInfoAnnouncement(
                    currentAbility, casterPosition, cursorPos, casterMap);
            }
            TolkHelper.SpeakData(announcement, SpeechPriority.Normal);
        }

        /// <summary>T: announces the targets affected at the cursor position.</summary>
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

            // A destination tile has no AOE; describe the cell instead.
            if (isDestinationPhase)
            {
                TolkHelper.SpeakData(BuildDestinationCellAnnouncement(cursorPos), SpeechPriority.Normal);
                return;
            }

            string announcement = AbilityTargetingHelper.BuildAffectedTargetsAnnouncement(
                currentAbility, cursorPos, casterMap);
            TolkHelper.SpeakData(announcement, SpeechPriority.Normal);
        }

        /// <summary>
        /// Describes a destination cell — terrain, contents, and validity — so the user can
        /// judge the teleport target before pressing Enter.
        /// </summary>
        private static string BuildDestinationCellAnnouncement(IntVec3 cursorPos)
        {
            var sb = new StringBuilder();

            var terrain = casterMap.terrainGrid.TerrainAt(cursorPos);
            string terrainName = terrain?.label
                ?? "RimWorldAccess.Abilities.Label.Ground".Translate().ToString();
            sb.Append((string)"RimWorldAccess.Abilities.Destination.Terrain".Translate(terrainName));

            var things = cursorPos.GetThingList(casterMap);
            var pawnsAtCell = things.OfType<Pawn>()
                .Where(p => !HiddenPawns.IsHidden(p))
                .Select(p => p.LabelShort).ToList();
            var otherThings = things
                .Where(t => !(t is Pawn)
                         && t.def.category != ThingCategory.Mote
                         && t.def.category != ThingCategory.Filth)
                .Select(t => t.LabelShort)
                .ToList();

            if (pawnsAtCell.Count > 0)
                sb.Append((string)"RimWorldAccess.Abilities.Destination.Contains".Translate(string.Join(", ", pawnsAtCell)));
            if (otherThings.Count > 0)
                sb.Append((string)"RimWorldAccess.Abilities.Destination.Contains".Translate(string.Join(", ", otherThings)));

            string cellError = ValidateDestinationCell(cursorPos);
            if (cellError != null)
                sb.Append($". {cellError}");
            else
                sb.Append((string)"RimWorldAccess.Abilities.Destination.ValidTeleport".Translate());

            return sb.ToString();
        }

        /// <summary>The target's immunity message, or null when none applies.</summary>
        public static string GetImmunityMessage(LocalTargetInfo target)
        {
            if (!isActive || currentAbility == null)
                return null;

            return AbilityTargetingHelper.GetImmunityMessage(currentAbility, target);
        }

        /// <summary>Out-of-range error for the target position, or null when in range.</summary>
        public static string ValidateRange(IntVec3 targetPos)
        {
            if (!isActive || currentAbility == null)
                return null;

            float range = isDestinationPhase ? destinationRange : AbilityTargetingHelper.GetRange(currentAbility);

            // Zero range is touch/melee; the game handles reachability itself.
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

        /// <summary>Line-of-sight error for the target position, or null when clear.</summary>
        public static string ValidateLineOfSight(IntVec3 targetPos)
        {
            if (!isActive || currentAbility == null || casterMap == null)
                return null;

            // In destination phase LOS runs from the selected target, not the caster, and is
            // driven by the comp's requiresLineOfSight, which can differ from the verb's.
            if (isDestinationPhase)
            {
                if (!destinationRequiresLineOfSight)
                    return null;
                if (!GenSight.LineOfSight(casterPosition, targetPos, casterMap))
                    return (string)"RimWorldAccess.Abilities.Teleport.NoLineOfSight".Translate();
                return null;
            }

            if (currentAbility.pawn == null)
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
        /// Error when the ability needs a target and the cursor has none, or null. Keeps an
        /// empty cell from being reported as merely "out of range".
        /// </summary>
        public static string ValidateTargetPresent(LocalTargetInfo target, IntVec3 cursorPos)
        {
            if (!isActive || currentAbility == null || casterMap == null)
                return null;

            // The destination phase targets a cell, not a pawn (targetParams sets
            // canTargetLocations), and the game validates the cell downstream.
            if (isDestinationPhase)
                return null;

            if (AbilityTargetingHelper.CanTargetLocations(currentAbility))
                return null;

            if (target.HasThing)
                return null;

            var pawn = cursorPos.GetFirstPawn(casterMap);
            if (pawn != null)
                return null;

            // An AOE ability spreads from a pawn, so name the nearby ones it could reach.
            if (currentAbility.def.HasAreaOfEffect)
            {
                float radius = currentAbility.def.EffectRadius;
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
        /// Why ValidateTarget rejected the cursor, for rejections the game left silent. Reruns
        /// vanilla's own range and LOS checks, then falls back to a generic message; both
        /// phases work, since the validators pick their origin from the phase.
        /// </summary>
        public static string DiagnoseRejection(LocalTargetInfo target, IntVec3 cursorPos)
        {
            if (!isActive || currentAbility == null)
                return null;

            string rangeError = ValidateRange(cursorPos);
            if (rangeError != null)
                return rangeError;

            string losError = ValidateLineOfSight(cursorPos);
            if (losError != null)
                return losError;

            return isDestinationPhase
                ? (string)"RimWorldAccess.Abilities.Teleport.InvalidDestination".Translate()
                : (string)"RimWorldAccess.Abilities.World.InvalidTarget".Translate();
        }

        /// <summary>
        /// Builds the success announcement including AOE affected targets.
        /// </summary>
        public static string BuildSuccessAnnouncement(LocalTargetInfo target, IntVec3 cursorPos)
        {
            if (!isActive || currentAbility == null || casterMap == null)
            {
                return "RimWorldAccess.Abilities.Success.TargetingLocation".Translate(
                    KeyboardTargeterConfirm.TargetLabel(target));
            }

            if (currentAbility.def.HasAreaOfEffect)
            {
                var affected = AbilityTargetingHelper.GetAffectedPawns(currentAbility, cursorPos, casterMap);

                if (affected.Count == 0)
                {
                    // A location-targeting ability expects no pawns; confirm the location.
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
                    var names = affected.Select(p => p.LabelShort).ToCommaList(useAnd: true);
                    return "RimWorldAccess.Abilities.Success.TargetingMany".Translate(affected.Count, names);
                }
            }

            string baseLine;
            if (target.HasThing)
            {
                baseLine = (string)"RimWorldAccess.Abilities.Success.TargetingLocation".Translate(target.Thing.LabelShort);
            }
            else
            {
                var terrain = casterMap.terrainGrid.TerrainAt(cursorPos);
                string terrainName = terrain?.label
                    ?? (string)"RimWorldAccess.Abilities.Label.Ground".Translate();
                baseLine = (string)"RimWorldAccess.Abilities.Success.TargetingLocation".Translate(terrainName);
            }

            // Effect comps emit their own cursor-side warnings (bloodfeed's "Will kill").
            string warnings = AbilityTargetingHelper.GetExtraTargetWarnings(currentAbility, target);
            if (!string.IsNullOrEmpty(warnings))
                baseLine += (string)"RimWorldAccess.Abilities.Success.WarningsSuffix".Translate(warnings);
            return baseLine;
        }
    }
}
