using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Targeting session state for usable items (CompTargetable/CompUsable), plus the
    /// announcements for start, error, and success.
    /// </summary>
    public static class ItemTargetingState
    {
        private static bool isActive = false;
        private static string itemLabel = null;

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the session for a CompTargetable/CompUsable, announcing the game's own
        /// mouse-attached instruction rather than inferring types from TargetingParameters.
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

            string mouseLabel = GetMouseLabel(source);

            TolkHelper.Speak(
                "RimWorldAccess.Abilities.Item.StartWithInstruction".Loc(itemLabel, mouseLabel),
                SpeechPriority.Normal);
        }

        /// <summary>
        /// Opens the session for a callback-based targeter (no ITargetingSource), as used by
        /// float-menu actions calling <c>Find.Targeter.BeginTargeting(parameters, action)</c>.
        /// <paramref name="label"/> is the option's already-localized text.
        /// </summary>
        public static void Open(string label, TargetingParameters parameters)
        {
            isActive = true;
            itemLabel = !string.IsNullOrEmpty(label) ? label : InferLabelFromParameters(parameters);

            TolkHelper.Speak(
                "RimWorldAccess.Abilities.Item.StartCallback".Loc(itemLabel),
                SpeechPriority.Normal);
        }

        /// <summary>
        /// User-facing label for the active session, normally the localized float-menu option
        /// that started it. Preferred over describing TargetingParameters' flags: callback
        /// targeting often pairs permissive defaults with an undescribable <c>validator</c>.
        /// </summary>
        public static string ItemLabel => itemLabel;

        /// <summary>
        /// R handler: item targeting has no range or AOE, so this announces "no range
        /// constraint" with the session label. T has no counterpart — arrow navigation already
        /// reads the cursor, so TargetingScope's affectedTargets claim swallows T silently
        /// (it must still claim it, or T falls through to the game's time/weather shortcut).
        /// </summary>
        internal static void AnnounceRangeInfo()
        {
            string label = !string.IsNullOrEmpty(itemLabel)
                ? itemLabel
                : "RimWorldAccess.Abilities.Item.DefaultInstruction".Translate().ToString();
            TolkHelper.SpeakData(
                (string)"RimWorldAccess.Abilities.Item.NoRangeConstraint".Translate(label),
                SpeechPriority.Normal);
        }

        public static void Close()
        {
            isActive = false;
            itemLabel = null;
        }

        /// <summary>
        /// Builds the success announcement. Must be called before <see cref="Close"/>.
        /// </summary>
        public static string BuildSuccessAnnouncement(LocalTargetInfo target)
        {
            string targetLabel = KeyboardTargeterConfirm.TargetLabel(target);
            if (itemLabel != null)
                return "RimWorldAccess.Abilities.Item.SuccessUsing".Translate(itemLabel, targetLabel);
            return "RimWorldAccess.Abilities.Item.SuccessSelected".Translate(targetLabel);
        }

        /// <summary>Error message for a cursor position holding no valid thing.</summary>
        public static string GetNoTargetErrorMessage()
        {
            return "RimWorldAccess.Abilities.Item.NoTargetAtCursor".Translate();
        }

        /// <summary>The mouse-attached label the game shows sighted users for this source.</summary>
        private static string GetMouseLabel(ITargetingSource source)
        {
            string verbText = source.GetVerb?.verbProps?.mouseTargetingText;
            if (verbText != null)
                return verbText;

            // The keys CompTargetable.OnGUI and CompUsable.OnGUI draw.
            if (source is CompTargetable)
                return "TargetGizmoMouse".Translate();
            if (source is CompUsable)
                return "UseGizmoMouse".Translate();

            return "RimWorldAccess.Abilities.Item.DefaultInstruction".Translate();
        }

        /// <summary>
        /// Fallback label when no float-menu label was captured: a coarse description
        /// inferred from the allowed target kinds.
        /// </summary>
        private static string InferLabelFromParameters(TargetingParameters parameters)
        {
            if (parameters == null)
                return "RimWorldAccess.Abilities.Item.DefaultInstruction".Translate();

            // canTargetPawns, canTargetBuildings and several subtypes default to true, so the
            // flag alone is misleading: CanTarget accepts a pawn only when canTargetPawns AND
            // one species flag is set (ForCell leaves canTargetPawns true with none set).
            // canTargetSubhumans/canTargetEntities are excluded on purpose — they can only
            // subtract, and CanTarget rejects both kinds earlier, at its !canTargetHumans and
            // !canTargetAnimals checks (TargetingParameters.CanTarget:178-206).
            bool pawnsActuallyTargetable = parameters.canTargetPawns
                && (parameters.canTargetHumans || parameters.canTargetAnimals
                    || parameters.canTargetMechs);

            if (pawnsActuallyTargetable)
                return (string)"RimWorldAccess.Abilities.Item.InferPawn".Translate();
            // Same defaulting trap: an explicit canTargetLocations outranks the building default.
            if (parameters.canTargetBuildings && !parameters.canTargetLocations)
                return (string)"RimWorldAccess.Abilities.Item.InferBuilding".Translate();
            if (parameters.canTargetItems)
                return (string)"RimWorldAccess.Abilities.Item.InferItem".Translate();
            if (parameters.canTargetLocations)
                return (string)"RimWorldAccess.Abilities.Item.InferLocation".Translate();

            return "RimWorldAccess.Abilities.Item.DefaultInstruction".Translate();
        }
    }
}
