using System.Collections.Generic;
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
        /// In multi-select mode, opens a pawn picker menu with needs info for each pawn.
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

            // Try pawn at cursor first (takes priority over multi-select)
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

            if (pawnAtCursor != null)
            {
                TolkHelper.Speak(PawnInfoHelper.GetNeedsInfo(pawnAtCursor));
                return;
            }

            // Multi-select with no cursor pawn: open pawn picker menu
            if (MultiSelectState.IsMultiSelectActive)
            {
                MultiSelectState.ValidateAndCleanupSelection();
                var options = new List<FloatMenuOption>();
                foreach (var pawn in MultiSelectState.SelectedPawns)
                {
                    string info = PawnInfoHelper.GetNeedsInfo(pawn);
                    string label = $"{pawn.LabelShort}: {info}";
                    var p = pawn;
                    options.Add(new FloatMenuOption(label, () =>
                    {
                        TolkHelper.Speak(PawnInfoHelper.GetNeedsInfo(p));
                    }));
                }
                WindowlessFloatMenuState.Open(options, false);
                return;
            }

            Pawn selectedPawn = Find.Selector?.FirstSelectedObject as Pawn;
            if (selectedPawn == null)
            {
                TolkHelper.Speak("No pawn selected");
                return;
            }

            TolkHelper.Speak(PawnInfoHelper.GetNeedsInfo(selectedPawn));
        }
    }
}
