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
                TolkHelper.Speak("No jump targeting active", SpeechPriority.Normal);
                return;
            }

            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
            if (!cursorPos.IsValid)
            {
                TolkHelper.Speak("Invalid cursor position", SpeechPriority.Normal);
                return;
            }

            var sb = new StringBuilder();
            float distance = (cursorPos - casterPosition).LengthHorizontal;
            sb.Append($"Distance: {distance:F0} tiles");

            if (distance <= jumpRange)
                sb.Append(", IN RANGE");
            else
                sb.Append($", OUT OF RANGE (max {jumpRange:F0})");

            if (!GenSight.LineOfSight(casterPosition, cursorPos, casterMap))
                sb.Append(", NO LINE OF SIGHT");

            if (cursorPos.InBounds(casterMap) && !JumpUtility.ValidJumpTarget(casterPawn, casterMap, cursorPos))
            {
                string reason = GetInvalidReason(cursorPos);
                if (reason != null)
                    sb.Append($", {reason}");
                else
                    sb.Append(", invalid jump target");
            }
            else if (distance <= jumpRange && GenSight.LineOfSight(casterPosition, cursorPos, casterMap))
            {
                sb.Append(", valid jump target");
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
                return "Invalid, ";

            float distance = (position - casterPosition).LengthHorizontal;
            if (distance > jumpRange)
                return "Out of range, ";

            if (!GenSight.LineOfSight(casterPosition, position, casterMap))
                return "No line of sight, ";

            if (position.Impassable(casterMap))
                return "Impassable, ";

            if (position.Fogged(casterMap))
                return "Fogged, ";

            Building edifice = position.GetEdifice(casterMap);
            if (edifice is Building_Door door && !door.Open)
                return "Closed door, ";

            if (!position.WalkableBy(casterMap, casterPawn))
                return "Not walkable, ";

            return "Can jump, ";
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
                return "Invalid target position";

            float distance = (targetPos - casterPosition).LengthHorizontal;
            if (distance > jumpRange)
                return $"Out of range. Distance: {distance:F0} tiles, max range: {jumpRange:F0}";

            if (!GenSight.LineOfSight(casterPosition, targetPos, casterMap))
                return "No line of sight to target";

            if (targetPos.Impassable(casterMap))
                return "Cannot jump here: impassable terrain";

            if (targetPos.Fogged(casterMap))
                return "Cannot jump here: fogged area";

            Building edifice = targetPos.GetEdifice(casterMap);
            if (edifice is Building_Door door && !door.Open)
                return "Cannot jump here: closed door";

            if (!targetPos.WalkableBy(casterMap, casterPawn))
                return "Cannot jump here: not walkable";

            // Check ammo/charges
            var reloadable = currentVerb.ReloadableCompSource;
            if (reloadable != null && !reloadable.CanBeUsed(out var reason))
                return reason ?? "No charges remaining";

            // Check if jump is already queued
            if (currentVerb.EquipmentSource != null &&
                !ReloadableUtility.CanUseConsideringQueuedJobs(casterPawn, currentVerb.EquipmentSource))
                return "Jump already queued";

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
                string terrainName = terrain?.label ?? "location";
                return $"Jumping to {terrainName}";
            }

            return "Jumping";
        }

        /// <summary>
        /// Builds the initial announcement when jump targeting starts.
        /// </summary>
        private static string BuildStartAnnouncement()
        {
            var sb = new StringBuilder();

            // Get the equipment label for the announcement
            string label = currentVerb.EquipmentSource?.LabelCap ?? "Jump";
            sb.Append($"{label} targeting. Range: {jumpRange:F0} tiles");

            // Add charge info if available
            var reloadable = currentVerb.ReloadableCompSource;
            if (reloadable != null)
            {
                sb.Append($". {reloadable.RemainingCharges}/{reloadable.MaxCharges} charges");
            }

            sb.Append(". Press R to check distance and line of sight.");

            return sb.ToString();
        }

        /// <summary>
        /// Gets the specific reason a jump target is invalid.
        /// Used for detailed announcements.
        /// </summary>
        private static string GetInvalidReason(IntVec3 position)
        {
            if (position.Impassable(casterMap))
                return "impassable terrain";

            if (position.Fogged(casterMap))
                return "fogged area";

            Building edifice = position.GetEdifice(casterMap);
            if (edifice is Building_Door door && !door.Open)
                return "closed door";

            if (!position.WalkableBy(casterMap, casterPawn))
                return "not walkable";

            return null;
        }
    }
}
