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
            itemLabel = source.Caster?.LabelCap ?? "RimWorldAccess.Abilities.Label.Item".Translate().ToString();

            // Use the game's targeting instruction — the same text shown as a mouse-attached
            // label for sighted users. This ensures each item type gets correct messaging
            // without us having to infer "animal"/"pawn"/etc. from TargetingParameters.
            string mouseLabel = GetMouseLabel(source);

            TolkHelper.Speak(
                "RimWorldAccess.Abilities.Item.StartWithInstruction".Translate(itemLabel, mouseLabel),
                SpeechPriority.Normal);
        }

        /// <summary>
        /// Opens item targeting state for a callback-based targeter (no ITargetingSource).
        /// Used by float-menu actions that call Find.Targeter.BeginTargeting(parameters, action)
        /// directly — e.g., "Force [pawn] to wear [apparel]" from FloatMenuOptionProvider_DressOtherPawn.
        /// The label is the float-menu option's already-localized text.
        /// </summary>
        public static void Open(string label, TargetingParameters parameters)
        {
            isActive = true;
            itemLabel = !string.IsNullOrEmpty(label) ? label : InferLabelFromParameters(parameters);

            TolkHelper.Speak(
                "RimWorldAccess.Abilities.Item.StartCallback".Translate(itemLabel),
                SpeechPriority.Normal);
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
            string targetLabel = target.HasThing
                ? target.Thing.LabelShort
                : "RimWorldAccess.Abilities.Label.Target".Translate().ToString();
            if (itemLabel != null)
                return "RimWorldAccess.Abilities.Item.SuccessUsing".Translate(itemLabel, targetLabel);
            return "RimWorldAccess.Abilities.Item.SuccessSelected".Translate(targetLabel);
        }

        /// <summary>
        /// Gets an error message for when no valid thing is at the cursor position.
        /// </summary>
        public static string GetNoTargetErrorMessage()
        {
            return "RimWorldAccess.Abilities.Item.NoTargetAtCursor".Translate();
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

            return "RimWorldAccess.Abilities.Item.DefaultInstruction".Translate();
        }

        /// <summary>
        /// Fallback label when no float-menu label was captured (e.g., a third-party caller
        /// invokes Targeter.BeginTargeting directly). Infers a coarse description from the
        /// allowed target kinds so the user at least knows targeting started.
        /// </summary>
        private static string InferLabelFromParameters(TargetingParameters parameters)
        {
            if (parameters == null)
                return "RimWorldAccess.Abilities.Item.DefaultInstruction".Translate();

            if (parameters.canTargetPawns || parameters.canTargetHumans
                || parameters.canTargetAnimals || parameters.canTargetMechs)
                return "RimWorldAccess.Abilities.Item.InferPawn".Translate();
            if (parameters.canTargetBuildings)
                return "RimWorldAccess.Abilities.Item.InferBuilding".Translate();
            if (parameters.canTargetItems)
                return "RimWorldAccess.Abilities.Item.InferItem".Translate();
            if (parameters.canTargetLocations)
                return "RimWorldAccess.Abilities.Item.InferLocation".Translate();

            return "RimWorldAccess.Abilities.Item.DefaultInstruction".Translate();
        }
    }
}
