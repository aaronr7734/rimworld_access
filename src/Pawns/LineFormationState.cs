using System.Collections.Generic;
using System.Linq;
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
                TolkHelper.Speak("No valid pawns to move");
                return;
            }

            IsActive = true;
            firstPoint = IntVec3.Invalid;
            secondPoint = IntVec3.Invalid;

            TolkHelper.Speak(
                $"Formation line mode. {pawnsToMove.Count} pawns. " +
                "Navigate with arrow keys, press Space to place first point.");
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
                TolkHelper.Speak("Invalid position");
                return;
            }

            if (!HasFirstPoint)
            {
                firstPoint = cursorPos;
                TolkHelper.Speak(
                    "First point placed. Navigate to second point and press Space.");
            }
            else if (!HasBothPoints)
            {
                secondPoint = cursorPos;
                TolkHelper.Speak(
                    "Formation line set. Press Enter to confirm, Escape to cancel.");
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

            Map map = Find.CurrentMap;
            if (map == null)
            {
                Cancel();
                return;
            }

            var movedNames = new List<string>();
            var failedNames = new List<string>();
            for (int i = 0; i < pawnsToMove.Count; i++)
            {
                Pawn pawn = pawnsToMove[i];
                if (pawn == null || pawn.Destroyed || !pawn.Spawned)
                    continue;

                // Calculate interpolated position along the line
                float t = pawnsToMove.Count <= 1 ? 0.5f : (float)i / (pawnsToMove.Count - 1);
                Vector3 rootVec = Vector3.Lerp(
                    firstPoint.ToVector3Shifted(),
                    secondPoint.ToVector3Shifted(),
                    t);
                IntVec3 root = rootVec.ToIntVec3();

                // Find best walkable cell near the calculated position
                IntVec3 dest = RCellFinder.BestOrderedGotoDestNear(root, pawn);

                if (dest.IsValid)
                {
                    Job job = JobMaker.MakeJob(JobDefOf.Goto, dest);
                    if (pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
                    {
                        FleckMaker.Static(dest, map, FleckDefOf.FeedbackGoto);
                        movedNames.Add(pawn.LabelShort);
                    }
                    else
                    {
                        failedNames.Add(pawn.LabelShort);
                    }
                }
                else
                {
                    failedNames.Add(pawn.LabelShort);
                }
            }

            string everyone = ((string)"ConfirmAbandonHomeNegativeThoughts_Everyone".Translate()).TrimEnd(':', ' ');
            if (movedNames.Count > 0)
            {
                SoundDefOf.ColonistOrdered.PlayOneShotOnCamera();
                if (failedNames.Count == 0)
                {
                    TolkHelper.Speak($"{everyone} moving in formation");
                }
                else if (failedNames.Count <= movedNames.Count)
                {
                    string names = MenuHelper.FormatNameList(failedNames);
                    TolkHelper.Speak($"{everyone} except {names} moving in formation");
                }
                else
                {
                    string names = MenuHelper.FormatNameList(movedNames);
                    TolkHelper.Speak($"Only {names} moving in formation");
                }
            }
            else
            {
                TolkHelper.Speak("No pawns could reach their destinations");
            }

            Close();
        }

        /// <summary>
        /// Cancels formation mode without issuing any orders.
        /// </summary>
        public static void Cancel()
        {
            TolkHelper.Speak("Formation cancelled");
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
