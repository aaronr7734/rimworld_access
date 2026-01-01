using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State class for displaying needs information of the pawn at the cursor position.
    /// Triggered by Alt+N key combination.
    /// </summary>
    public static class NeedsState
    {
        /// <summary>
        /// Displays needs information for the pawn at the current cursor position.
        /// Shows all needs with their current percentages and trends.
        /// </summary>
        public static void DisplayNeedsInfo()
        {
            // Check if we're in-game
            if (Current.ProgramState != ProgramState.Playing)
            {
                TolkHelper.Speak("Not in game");
                return;
            }

            // Check if there's a current map
            if (Find.CurrentMap == null)
            {
                TolkHelper.Speak("No map loaded");
                return;
            }

            // Try pawn at cursor first
            Pawn pawnAtCursor = null;
            if (MapNavigationState.IsInitialized)
            {
                IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;
                if (cursorPosition.IsValid && cursorPosition.InBounds(Find.CurrentMap))
                {
                    pawnAtCursor = Find.CurrentMap.thingGrid.ThingsListAt(cursorPosition)
                        .OfType<Pawn>().FirstOrDefault();
                }
            }

            // Fall back to selected pawn
            if (pawnAtCursor == null)
                pawnAtCursor = Find.Selector?.FirstSelectedObject as Pawn;

            if (pawnAtCursor == null)
            {
                TolkHelper.Speak("No pawn selected");
                return;
            }

            // Get needs information using PawnInfoHelper
            string needsInfo = PawnInfoHelper.GetNeedsInfo(pawnAtCursor);

            // Copy to clipboard for screen reader
            TolkHelper.Speak(needsInfo);
        }
    }
}
