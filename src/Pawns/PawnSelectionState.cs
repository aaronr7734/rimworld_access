using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Maintains the state of pawn selection cycling for accessibility features.
    /// Tracks the currently selected colonist when cycling with comma and period keys.
    /// </summary>
    public static class PawnSelectionState
    {
        private static int currentSelectedIndex = -1;
        private static Pawn lastSelectedPawn = null;

        /// <summary>
        /// Gets the list of selectable colonists in display order.
        /// This matches the order shown in the colonist bar.
        /// </summary>
        private static List<Pawn> GetSelectableColonists()
        {
            if (Find.ColonistBar == null)
                return new List<Pawn>();

            // Get colonists in the order they appear in the colonist bar
            var colonists = Find.ColonistBar.GetColonistsInOrder();

            // Filter to only spawned, selectable colonists on the current map
            return colonists
                .Where(p => p != null &&
                            p.Spawned &&
                            p.Map == Find.CurrentMap &&
                            p.def.selectable)
                .ToList();
        }

        /// <summary>
        /// Selects the next colonist in the list (period key).
        /// Returns the selected pawn, or null if no colonists available.
        /// </summary>
        public static Pawn SelectNextColonist()
        {
            var colonistList = GetSelectableColonists();

            if (colonistList.Count == 0)
                return null;

            // Find the index of the last pawn we selected
            int foundIndex = -1;
            if (lastSelectedPawn != null)
            {
                foundIndex = colonistList.IndexOf(lastSelectedPawn);
            }

            // If last selected pawn not found (dead, left map, etc.), check current game selection
            if (foundIndex == -1 && Find.Selector != null && Find.Selector.NumSelected > 0)
            {
                var currentlySelected = Find.Selector.FirstSelectedObject as Pawn;
                if (currentlySelected != null)
                {
                    foundIndex = colonistList.IndexOf(currentlySelected);
                }
            }

            // Calculate next index
            if (foundIndex == -1)
            {
                // No valid previous selection, start at beginning
                currentSelectedIndex = 0;
            }
            else
            {
                // Move to next, wrapping around to start
                currentSelectedIndex = (foundIndex + 1) % colonistList.Count;
            }

            lastSelectedPawn = colonistList[currentSelectedIndex];
            return lastSelectedPawn;
        }

        /// <summary>
        /// Selects the previous colonist in the list (comma key).
        /// Returns the selected pawn, or null if no colonists available.
        /// </summary>
        public static Pawn SelectPreviousColonist()
        {
            var colonistList = GetSelectableColonists();

            if (colonistList.Count == 0)
                return null;

            // Find the index of the last pawn we selected
            int foundIndex = -1;
            if (lastSelectedPawn != null)
            {
                foundIndex = colonistList.IndexOf(lastSelectedPawn);
            }

            // If last selected pawn not found (dead, left map, etc.), check current game selection
            if (foundIndex == -1 && Find.Selector != null && Find.Selector.NumSelected > 0)
            {
                var currentlySelected = Find.Selector.FirstSelectedObject as Pawn;
                if (currentlySelected != null)
                {
                    foundIndex = colonistList.IndexOf(currentlySelected);
                }
            }

            // Calculate previous index
            if (foundIndex == -1)
            {
                // No valid previous selection, start at end
                currentSelectedIndex = colonistList.Count - 1;
            }
            else
            {
                // Move to previous, wrapping around to end
                currentSelectedIndex = (foundIndex - 1 + colonistList.Count) % colonistList.Count;
            }

            lastSelectedPawn = colonistList[currentSelectedIndex];
            return lastSelectedPawn;
        }

        /// <summary>
        /// Resets the selection state.
        /// </summary>
        public static void Reset()
        {
            currentSelectedIndex = -1;
            lastSelectedPawn = null;
        }
    }
}
