using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages the interactive two-point line placement for "Go here in formation" orders.
    /// When active, the player places two points using the map cursor and Space bar,
    /// then confirms with Enter to distribute pawns along the line.
    /// </summary>
    public static class LineFormationState
    {
        public static bool IsActive { get; private set; }

        private static IntVec3 firstPoint = IntVec3.Invalid;
        private static IntVec3 secondPoint = IntVec3.Invalid;
        private static List<Pawn> pawnsToMove = new List<Pawn>();

        /// <summary>
        /// Whether the first point has been placed.
        /// </summary>
        public static bool HasFirstPoint => firstPoint.IsValid;

        /// <summary>
        /// Whether both points have been placed and we're waiting for confirmation.
        /// </summary>
        public static bool HasBothPoints => firstPoint.IsValid && secondPoint.IsValid;

        /// <summary>
        /// Activates line formation mode with the given pawns.
        /// Called when the player selects "Go here in formation" from the float menu.
        /// </summary>
        public static void Activate(IEnumerable<Pawn> pawns)
        {
            pawnsToMove = pawns.Where(p => p != null && !p.Destroyed && p.Spawned).ToList();
            if (pawnsToMove.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Formation.NoValidPawns".Translate());
                return;
            }

            IsActive = true;
            firstPoint = IntVec3.Invalid;
            secondPoint = IntVec3.Invalid;

            TolkHelper.Speak("RimWorldAccess.Pawns.Formation.Activated".Translate(pawnsToMove.Count));
        }

        /// <summary>
        /// Places a point at the current cursor position.
        /// First call places the first point, second call places the second.
        /// </summary>
        public static void PlacePoint()
        {
            if (!IsActive)
                return;

            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
            if (!cursorPos.IsValid || !cursorPos.InBounds(Find.CurrentMap))
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Formation.InvalidPosition".Translate());
                return;
            }

            if (!HasFirstPoint)
            {
                firstPoint = cursorPos;
                TolkHelper.Speak("RimWorldAccess.Pawns.Formation.FirstPlaced".Translate());
            }
            else if (!HasBothPoints)
            {
                secondPoint = cursorPos;
                TolkHelper.Speak("RimWorldAccess.Pawns.Formation.SecondPlaced".Translate());
            }
        }

        /// <summary>
        /// Confirms the formation and issues goto orders to all pawns.
        /// Distributes pawns evenly along the line from first point to second point,
        /// in colonist bar order (same as vanilla MultiPawnGotoController).
        /// </summary>
        public static void Confirm()
        {
            if (!IsActive || !HasBothPoints)
                return;

            if (Find.CurrentMap == null || Find.Selector == null)
            {
                Cancel();
                return;
            }

            // Snapshot pawn jobs before issuing orders (for feedback)
            var validPawns = pawnsToMove
                .Where(p => p != null && !p.Destroyed && p.Spawned)
                .ToList();

            var jobsBefore = new Dictionary<Pawn, Job>();
            foreach (var p in validPawns)
                jobsBefore[p] = p.jobs?.curJob;

            // Use the game's own MultiPawnGotoController for line placement.
            // This handles duplicate-cell prevention, mech range checks,
            // exit map handling, and proper job creation.
            var controller = Find.Selector.gotoController;
            controller.StartInteraction(firstPoint);

            // Set the end point (private field)
            var endField = AccessTools.Field(typeof(MultiPawnGotoController), "end");
            endField.SetValue(controller, secondPoint);

            // Add all valid pawns
            foreach (var pawn in validPawns)
                controller.AddPawn(pawn);

            // FinalizeInteraction computes destinations, issues jobs, plays sound
            controller.FinalizeInteraction();

            // Announce results using job-diff feedback
            var succeeded = validPawns.Where(p => p.jobs?.curJob != jobsBefore[p]).ToList();
            var unchanged = validPawns.Where(p => p.jobs?.curJob == jobsBefore[p]).ToList();

            string everyone = ((string)"ConfirmAbandonHomeNegativeThoughts_Everyone".Translate()).TrimEnd(':', ' ');
            if (unchanged.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Formation.AllMoving".Translate(everyone));
            }
            else if (succeeded.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Formation.NoneMoving".Translate());
            }
            else if (unchanged.Count <= succeeded.Count)
            {
                string names = MenuHelper.FormatNameList(
                    unchanged.Select(p => p.LabelShort).ToList());
                TolkHelper.Speak("RimWorldAccess.Pawns.Formation.AllExcept".Translate(everyone, names));
            }
            else
            {
                string names = MenuHelper.FormatNameList(
                    succeeded.Select(p => p.LabelShort).ToList());
                TolkHelper.Speak("RimWorldAccess.Pawns.Formation.OnlySome".Translate(names));
            }

            Close();
        }

        /// <summary>
        /// Cancels formation mode without issuing any orders.
        /// </summary>
        public static void Cancel()
        {
            TolkHelper.Speak("RimWorldAccess.Pawns.Formation.Cancelled".Translate());
            Close();
        }

        /// <summary>
        /// Closes formation mode and resets all state.
        /// </summary>
        private static void Close()
        {
            IsActive = false;
            firstPoint = IntVec3.Invalid;
            secondPoint = IntVec3.Invalid;
            pawnsToMove.Clear();
        }
    }
}
