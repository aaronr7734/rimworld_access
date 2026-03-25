using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State class for displaying mood information of the selected pawn.
    /// Triggered by Alt+M key combination.
    /// </summary>
    public static class MoodState
    {
        /// <summary>
        /// Displays mood information for the currently selected pawn.
        /// Shows mood level, mood description, and all thoughts affecting mood.
        /// In multi-select mode, opens a pawn picker menu with mood info for each pawn.
        /// </summary>
        public static void DisplayMoodInfo()
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
                TolkHelper.Speak(PawnInfoHelper.GetMoodInfo(pawnAtCursor));
                return;
            }

            // Multi-select with no cursor pawn: open pawn picker menu
            if (MultiSelectState.IsMultiSelectActive)
            {
                MultiSelectState.ValidateAndCleanupSelection();
                var options = new List<FloatMenuOption>();
                foreach (var pawn in MultiSelectState.SelectedPawns)
                {
                    string info = PawnInfoHelper.GetMoodInfo(pawn);
                    string label = $"{pawn.LabelShort}: {info}";
                    var p = pawn;
                    options.Add(new FloatMenuOption(label, () =>
                    {
                        TolkHelper.Speak(PawnInfoHelper.GetMoodInfo(p));
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

            TolkHelper.Speak(PawnInfoHelper.GetMoodInfo(selectedPawn));
        }
    }
}
