using RimWorld;
using UnityEngine;
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
                "RimWorldAccess.Abilities.Item.StartWithInstruction".Loc(itemLabel, mouseLabel),
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
                "RimWorldAccess.Abilities.Item.StartCallback".Loc(itemLabel),
                SpeechPriority.Normal);
        }

        /// <summary>
        /// The user-facing label for the active item-targeting session — typically the
        /// localized float-menu option that started it (e.g.,
        /// "Force target to wear alpaca wool face mask"). Strictly more accurate than
        /// describing TargetingParameters' boolean flags, because callback-based targeting
        /// (ForForceWear, force-equip, etc.) often pairs permissive defaults with a real
        /// filter in TargetingParameters.validator that we cannot describe textually.
        /// </summary>
        public static string ItemLabel => itemLabel;

        /// <summary>
        /// Handles R / T during item targeting so they don't fall through to the game's
        /// global shortcuts (R = draft selected pawn, T = time/weather announcement),
        /// which is jarring mid-cast. Mirrors the AbilityTargetingState / JumpTargetingState
        /// convention of consuming R and T while targeting is active.
        ///
        /// Item targeting has no range and no AOE preview to give. R announces "no range
        /// constraint" (with the active session label as a reminder of what the user is
        /// doing); T is consumed silently — arrow-key navigation already announces what's
        /// at the cursor on every move, so re-reading on T would just be redundant chatter.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive)
                return false;
            if (shift || ctrl || alt)
                return false;

            if (key == KeyCode.R)
            {
                AnnounceRangeInfo();
                return true;
            }
            if (key == KeyCode.T)
            {
                // Consumed silently — see method docs.
                return true;
            }
            return false;
        }

        private static void AnnounceRangeInfo()
        {
            string label = !string.IsNullOrEmpty(itemLabel)
                ? itemLabel
                : "RimWorldAccess.Abilities.Item.DefaultInstruction".Translate().ToString();
            TolkHelper.SpeakData(
                (string)"RimWorldAccess.Abilities.Item.NoRangeConstraint".Translate(label),
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

            // canTargetPawns, canTargetBuildings, and several subtypes all default to TRUE
            // on a fresh TargetingParameters. CanTarget() only treats a pawn as targetable
            // when canTargetPawns AND at least one species subtype (humans / animals / mechs /
            // subhumans) is enabled, so checking canTargetPawns alone is misleading.
            // Example: TargetingParameters.ForCell() — used for shuttle "land in existing map"
            // cell selection — leaves canTargetPawns at its default true but disables every
            // species subtype, so picking a pawn would actually fail. Before this guard, the
            // announcement said "Select pawn to target" for a cell-only target.
            bool pawnsActuallyTargetable = parameters.canTargetPawns
                && (parameters.canTargetHumans || parameters.canTargetAnimals
                    || parameters.canTargetMechs || parameters.canTargetSubhumans);

            if (pawnsActuallyTargetable)
                return "Select pawn to target";
            // Same defaulting trap for buildings: if locations are explicitly enabled, prefer
            // the location label over a stale canTargetBuildings=true default.
            if (parameters.canTargetBuildings && !parameters.canTargetLocations)
                return "Select building to target";
            if (parameters.canTargetItems)
                return "RimWorldAccess.Abilities.Item.InferItem".Translate();
            if (parameters.canTargetLocations)
                return "Select a cell";

            return "RimWorldAccess.Abilities.Item.DefaultInstruction".Translate();
        }
    }
}
