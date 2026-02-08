using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State management for CompTargetable/CompUsable item targeting mode.
    /// Tracks when a usable item (e.g., sentience catalyst, healer mech serum) is waiting
    /// for the user to select a target. Provides contextual announcements for start, error,
    /// and success.
    /// </summary>
    public static class ItemTargetingState
    {
        private static bool isActive = false;
        private static string itemLabel = null;

        /// <summary>
        /// Gets whether item targeting mode is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Opens item targeting state when a CompTargetable/CompUsable begins targeting.
        /// Uses the game's own mouse-attached label text (same text sighted users see)
        /// rather than inferring target types from TargetingParameters.
        /// </summary>
        public static void Open(ITargetingSource source)
        {
            if (source == null)
            {
                Log.Warning("RimWorld Access: ItemTargetingState.Open called with null source");
                return;
            }

            isActive = true;
            itemLabel = source.Caster?.LabelCap ?? "item";

            // Use the game's targeting instruction — the same text shown as a mouse-attached
            // label for sighted users. This ensures each item type gets correct messaging
            // without us having to infer "animal"/"pawn"/etc. from TargetingParameters.
            string mouseLabel = GetMouseLabel(source);

            string announcement = $"{itemLabel}: {mouseLabel}. Navigate with arrow keys, Enter to confirm.";
            TolkHelper.Speak(announcement, SpeechPriority.Normal);
        }

        /// <summary>
        /// Closes item targeting state.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            itemLabel = null;
        }

        /// <summary>
        /// Builds a success announcement with item context.
        /// Called from TargetingPatch before StopTargeting (which closes this state).
        /// </summary>
        public static string BuildSuccessAnnouncement(LocalTargetInfo target)
        {
            string targetLabel = target.HasThing ? target.Thing.LabelShort : "target";
            if (itemLabel != null)
                return $"Using {itemLabel} on {targetLabel}";
            return $"Target selected: {targetLabel}";
        }

        /// <summary>
        /// Gets an error message for when no valid thing is at the cursor position.
        /// </summary>
        public static string GetNoTargetErrorMessage()
        {
            return "No valid target at cursor";
        }

        /// <summary>
        /// Gets the game's mouse-attached label text for the given targeting source.
        /// This is the same text sighted users see attached to their mouse cursor.
        /// </summary>
        private static string GetMouseLabel(ITargetingSource source)
        {
            // Check for verb-specific targeting text first (most specific, per-item)
            string verbText = source.GetVerb?.verbProps?.mouseTargetingText;
            if (verbText != null)
                return verbText;

            // Use the same translation keys the game shows as mouse-attached labels.
            // CompTargetable.OnGUI shows "TargetGizmoMouse".Translate()
            // CompUsable.OnGUI shows "UseGizmoMouse".Translate()
            if (source is CompTargetable)
                return "TargetGizmoMouse".Translate();
            if (source is CompUsable)
                return "UseGizmoMouse".Translate();

            return "Select a target";
        }
    }
}
