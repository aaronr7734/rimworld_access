using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The keyboard-confirm resolution every map targeter shares: is the current
    /// IMGUI event our Enter-at-the-virtual-cursor confirm, and what target does
    /// that cell hold? Callers gate their own targeter validity first and own
    /// what the confirm DOES; this owns only event shape, cursor validity (with
    /// the standard invalid-position announce), and the TargetsAt resolution that
    /// must never fall back to the real mouse position. Nothing here is wrapped in
    /// a try/catch, so each caller's own exception handling (or deliberate lack of
    /// it) keeps behaving exactly as it did inline — a null targetParams still
    /// throws out of <see cref="ResolveTargetAt"/> the way GenUI.TargetsAt does.
    /// </summary>
    internal static class KeyboardTargeterConfirm
    {
        public enum Outcome
        {
            /// <summary>Not our event; the caller's prefix returns true (original body runs).</summary>
            NotOurs,
            /// <summary>Our confirm, but the cursor cell is invalid; announced and consumed — the caller's prefix returns false.</summary>
            InvalidPosition,
            /// <summary>Our confirm at a valid cell; target resolved — the caller acts, consumes the event, and returns false.</summary>
            Confirmed,
        }

        /// <summary>
        /// Event shape and cursor validity only, for targeters that confirm the cursor cell
        /// itself rather than a Thing standing on it. Callers that touch Event.current before
        /// this (rotation shortcuts and the like) keep their own null guard.
        /// </summary>
        public static Outcome TryResolveCell(out IntVec3 cell, SpeechPriority invalidPriority = SpeechPriority.Normal)
        {
            cell = IntVec3.Invalid;

            if (Event.current.type != EventType.KeyDown)
                return Outcome.NotOurs;

            KeyCode key = Event.current.keyCode;
            if (key != KeyCode.Return && key != KeyCode.KeypadEnter)
                return Outcome.NotOurs;

            if (!MapNavigationState.IsInitialized)
                return Outcome.NotOurs;

            cell = MapNavigationState.CurrentCursorPosition;
            if (!cell.IsValid || Find.CurrentMap == null || !cell.InBounds(Find.CurrentMap))
            {
                TolkHelper.Speak("RimWorldAccess.Combat.Target.InvalidPosition".Loc(), invalidPriority);
                Event.current.Use();
                return Outcome.InvalidPosition;
            }

            return Outcome.Confirmed;
        }

        /// <summary>
        /// As <see cref="TryResolveCell"/>, and on Confirmed also resolves what the cursor cell
        /// holds via <see cref="ResolveTargetAt"/>.
        /// </summary>
        public static Outcome TryResolve(TargetingParameters targetParams, out LocalTargetInfo target, out IntVec3 cell,
            SpeechPriority invalidPriority = SpeechPriority.Normal)
        {
            target = LocalTargetInfo.Invalid;

            Outcome outcome = TryResolveCell(out cell, invalidPriority);
            if (outcome != Outcome.Confirmed)
                return outcome;

            target = ResolveTargetAt(cell, targetParams);
            return Outcome.Confirmed;
        }

        /// <summary>
        /// GenUI.TargetsAt falls back to the real mouse position unless thingsOnly:true, so
        /// resolve a Thing target explicitly and fall back to a manual cell target built from
        /// OUR virtual cursor — never UI.MouseCell. With thingsOnly:true the enumeration yields
        /// Things only, so a resolved target is a Thing target and the fallback is the only
        /// source of cell targets.
        /// </summary>
        public static LocalTargetInfo ResolveTargetAt(IntVec3 cell, TargetingParameters targetParams)
        {
            LocalTargetInfo target = GenUI.TargetsAt(cell.ToVector3Shifted(), targetParams, thingsOnly: true, null)
                .FirstOrFallback(LocalTargetInfo.Invalid);
            return target.IsValid ? target : new LocalTargetInfo(cell);
        }

        public static string TargetLabel(LocalTargetInfo target)
        {
            if (target.HasThing)
                return target.Thing.LabelShort;
            IntVec3 cell = target.Cell;
            return "RimWorldAccess.Combat.Target.LocationAt".Translate(
                "RimWorldAccess.Input.Cursor.Coordinates".Translate(cell.x, cell.z)).ToString();
        }
    }
}
