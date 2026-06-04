using System.Text;
using RimWorld;
using RimWorld.Utility;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State management for equipment-based jump targeting (Jump Pack, Locust Armor).
    /// Verb_Jump extends Verb directly (not Verb_CastAbility/IAbilityVerb), so it needs
    /// its own targeting state separate from AbilityTargetingState.
    /// </summary>
    public static class JumpTargetingState
    {
        private static bool isActive = false;
        private static Verb_Jump currentVerb = null;
        private static Pawn casterPawn = null;
        private static IntVec3 casterPosition = IntVec3.Invalid;
        private static Map casterMap = null;
        private static float jumpRange = 0f;

        /// <summary>
        /// Gets whether jump targeting mode is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Opens jump targeting state when a Verb_Jump begins targeting.
        /// </summary>
        public static void Open(Verb_Jump verb)
        {
            if (verb == null)
            {
                Log.Warning("RimWorld Access: JumpTargetingState.Open called with null verb");
                return;
            }

            currentVerb = verb;
            casterPawn = verb.CasterPawn;
            casterPosition = casterPawn?.Position ?? IntVec3.Invalid;
            casterMap = casterPawn?.Map;
            jumpRange = verb.EffectiveRange;
            isActive = true;

            string announcement = BuildStartAnnouncement();
            TolkHelper.Speak(announcement, SpeechPriority.Normal);
        }

        /// <summary>
        /// Closes jump targeting state.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentVerb = null;
            casterPawn = null;
            casterPosition = IntVec3.Invalid;
            casterMap = null;
            jumpRange = 0f;
        }

        /// <summary>
        /// Handles keyboard input during jump targeting.
        /// Returns true if input was handled.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive)
                return false;

            if (key == KeyCode.R && !shift && !ctrl && !alt)
            {
                AnnounceRangeInfo();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Announces detailed range and validity info for the current cursor position.
        /// Called when user presses R key during targeting.
        /// </summary>
        public static void AnnounceRangeInfo()
        {
            if (!isActive || casterPawn == null || casterMap == null)
            {
                TolkHelper.Speak("RimWorldAccess.Abilities.State.NoJumpTargeting".Loc(), SpeechPriority.Normal);
                return;
            }

            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
            if (!cursorPos.IsValid)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.InvalidCursorPosition".Loc(), SpeechPriority.Normal);
                return;
            }

            var sb = new StringBuilder();
            float distance = (cursorPos - casterPosition).LengthHorizontal;
            sb.Append("RimWorldAccess.Abilities.Range.Distance".Translate(distance.ToString("F0")));

            if (distance <= jumpRange)
                sb.Append("RimWorldAccess.Abilities.Range.InRange".Translate());
            else
                sb.Append("RimWorldAccess.Abilities.Range.OutOfRange".Translate(jumpRange.ToString("F0")));

            if (!GenSight.LineOfSight(casterPosition, cursorPos, casterMap))
                sb.Append("RimWorldAccess.Abilities.Range.NoLineOfSight".Translate());

            if (cursorPos.InBounds(casterMap) && !JumpUtility.ValidJumpTarget(casterPawn, casterMap, cursorPos))
            {
                string reason = GetInvalidReason(cursorPos);
                if (reason != null)
                    sb.Append("RimWorldAccess.Abilities.Jump.InvalidReasonSuffix".Translate(reason));
                else
                    sb.Append("RimWorldAccess.Abilities.Jump.InvalidTargetFallback".Translate());
            }
            else if (distance <= jumpRange && GenSight.LineOfSight(casterPosition, cursorPos, casterMap))
            {
                sb.Append("RimWorldAccess.Abilities.Jump.ValidSuffix".Translate());
            }

            TolkHelper.Speak(sb.ToString(), SpeechPriority.Normal);
        }

        /// <summary>
        /// Returns a prefix string for per-tile announcements during arrow navigation.
        /// Called by MapArrowKeyHandler.AddContextPrefix() on every cursor move.
        /// </summary>
        public static string GetJumpValidityPrefix(IntVec3 position)
        {
            if (!isActive || casterPawn == null || casterMap == null)
                return "";

            if (!position.IsValid || !position.InBounds(casterMap))
                return "RimWorldAccess.Abilities.Jump.PrefixInvalid".Translate();

            float distance = (position - casterPosition).LengthHorizontal;
            if (distance > jumpRange)
                return "RimWorldAccess.Abilities.Jump.PrefixOutOfRange".Translate();

            if (!GenSight.LineOfSight(casterPosition, position, casterMap))
                return "RimWorldAccess.Abilities.Jump.PrefixNoLineOfSight".Translate();

            if (position.Impassable(casterMap))
                return "RimWorldAccess.Abilities.Jump.PrefixImpassable".Translate();

            if (position.Fogged(casterMap))
                return "RimWorldAccess.Abilities.Jump.PrefixFogged".Translate();

            Building edifice = position.GetEdifice(casterMap);
            if (edifice is Building_Door door && !door.Open)
                return "RimWorldAccess.Abilities.Jump.PrefixClosedDoor".Translate();

            if (!position.WalkableBy(casterMap, casterPawn))
                return "RimWorldAccess.Abilities.Jump.PrefixNotWalkable".Translate();

            return "RimWorldAccess.Abilities.Jump.PrefixCanJump".Translate();
        }

        /// <summary>
        /// Validates the target position for the Enter key confirmation.
        /// Returns null if valid, or a specific error message if invalid.
        /// </summary>
        public static string ValidateAndGetError(IntVec3 targetPos)
        {
            if (!isActive || currentVerb == null || casterPawn == null || casterMap == null)
                return null;

            if (!targetPos.IsValid || !targetPos.InBounds(casterMap))
                return "RimWorldAccess.Abilities.Jump.ErrorInvalidPosition".Translate();

            float distance = (targetPos - casterPosition).LengthHorizontal;
            if (distance > jumpRange)
                return "RimWorldAccess.Abilities.Jump.ErrorOutOfRange"
                    .Translate(distance.ToString("F0"), jumpRange.ToString("F0"));

            if (!GenSight.LineOfSight(casterPosition, targetPos, casterMap))
                return "RimWorldAccess.Abilities.Jump.ErrorLineOfSight".Translate();

            if (targetPos.Impassable(casterMap))
                return "RimWorldAccess.Abilities.Jump.ErrorImpassable".Translate();

            if (targetPos.Fogged(casterMap))
                return "RimWorldAccess.Abilities.Jump.ErrorFogged".Translate();

            Building edifice = targetPos.GetEdifice(casterMap);
            if (edifice is Building_Door door && !door.Open)
                return "RimWorldAccess.Abilities.Jump.ErrorClosedDoor".Translate();

            if (!targetPos.WalkableBy(casterMap, casterPawn))
                return "RimWorldAccess.Abilities.Jump.ErrorNotWalkable".Translate();

            // Check ammo/charges
            var reloadable = currentVerb.ReloadableCompSource;
            if (reloadable != null && !reloadable.CanBeUsed(out var reason))
                return reason ?? "RimWorldAccess.Abilities.Jump.ErrorNoCharges".Translate().ToString();

            // Check if jump is already queued
            if (currentVerb.EquipmentSource != null &&
                !ReloadableUtility.CanUseConsideringQueuedJobs(casterPawn, currentVerb.EquipmentSource))
                return "RimWorldAccess.Abilities.Jump.ErrorAlreadyQueued".Translate();

            return null;
        }

        /// <summary>
        /// Builds the success announcement when a jump target is confirmed.
        /// </summary>
        public static string BuildSuccessAnnouncement(IntVec3 targetPos)
        {
            if (casterMap != null && targetPos.IsValid && targetPos.InBounds(casterMap))
            {
                var terrain = casterMap.terrainGrid.TerrainAt(targetPos);
                string terrainName = terrain?.label
                    ?? "RimWorldAccess.Abilities.Label.Location".Translate().ToString();
                return "RimWorldAccess.Abilities.Jump.SuccessTo".Translate(terrainName);
            }

            return "RimWorldAccess.Abilities.Jump.SuccessBare".Translate();
        }

        /// <summary>
        /// Builds the initial announcement when jump targeting starts.
        /// </summary>
        private static string BuildStartAnnouncement()
        {
            var sb = new StringBuilder();

            // Get the equipment label for the announcement
            string label = currentVerb.EquipmentSource?.LabelCap
                ?? "RimWorldAccess.Abilities.Label.Jump".Translate().ToString();
            sb.Append("RimWorldAccess.Abilities.Jump.Start".Translate(label, jumpRange.ToString("F0")));

            // Add charge info if available
            var reloadable = currentVerb.ReloadableCompSource;
            if (reloadable != null)
            {
                sb.Append("RimWorldAccess.Abilities.Jump.Charges"
                    .Translate(reloadable.RemainingCharges, reloadable.MaxCharges));
            }

            sb.Append("RimWorldAccess.Abilities.Jump.HintR".Translate());

            return sb.ToString();
        }

        /// <summary>
        /// Gets the specific reason a jump target is invalid.
        /// Used for detailed announcements.
        /// </summary>
        private static string GetInvalidReason(IntVec3 position)
        {
            if (position.Impassable(casterMap))
                return "RimWorldAccess.Abilities.Jump.ReasonImpassable".Translate();

            if (position.Fogged(casterMap))
                return "RimWorldAccess.Abilities.Jump.ReasonFogged".Translate();

            Building edifice = position.GetEdifice(casterMap);
            if (edifice is Building_Door door && !door.Open)
                return "RimWorldAccess.Abilities.Jump.ReasonClosedDoor".Translate();

            if (!position.WalkableBy(casterMap, casterPawn))
                return "RimWorldAccess.Abilities.Jump.ReasonNotWalkable".Translate();

            return null;
        }
    }
}
