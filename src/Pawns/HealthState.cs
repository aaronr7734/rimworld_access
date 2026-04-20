using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State class for displaying health information of the pawn at the cursor position.
    /// Triggered by Alt+H key combination.
    /// </summary>
    public static class HealthState
    {
        /// <summary>
        /// Displays health information for the pawn at the current cursor position.
        /// Shows health state, conditions, bleeding, pain, and capacities.
        /// In multi-select mode, opens a pawn picker menu with health info for each pawn.
        /// </summary>
        public static void DisplayHealthInfo()
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

            // If cursor is on a pawn, show that pawn's info regardless of multi-select
            if (pawnAtCursor != null)
            {
                TolkHelper.Speak(PawnInfoHelper.GetHealthInfo(pawnAtCursor));
                return;
            }

            // Multi-select with no cursor pawn: open pawn picker menu
            if (MultiSelectState.IsMultiSelectActive)
            {
                MultiSelectState.ValidateAndCleanupSelection();
                var options = new List<FloatMenuOption>();
                foreach (var pawn in MultiSelectState.SelectedPawns)
                {
                    string info = PawnInfoHelper.GetHealthInfo(pawn);
                    string label = $"{pawn.LabelShort}: {info}";
                    var p = pawn;
                    options.Add(new FloatMenuOption(label, () =>
                    {
                        TolkHelper.Speak(PawnInfoHelper.GetHealthInfo(p));
                    }));
                }
                WindowlessFloatMenuState.Open(options, false);
                return;
            }

            // Fall back to selected pawn
            Pawn selectedPawn = Find.Selector?.FirstSelectedObject as Pawn;
            if (selectedPawn == null)
            {
                TolkHelper.Speak("No pawn selected");
                return;
            }

            TolkHelper.Speak(PawnInfoHelper.GetHealthInfo(selectedPawn));
        }
    }
}
