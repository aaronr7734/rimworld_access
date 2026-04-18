using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State class for displaying top skills of the selected pawn.
    /// Triggered by Alt+K key combination.
    /// </summary>
    public static class SkillsState
    {
        /// <summary>
        /// Displays top 3 skills for the currently selected pawn.
        /// Shows skill name, level, and passion.
        /// In multi-select mode, opens a pawn picker menu with skills info for each pawn.
        /// </summary>
        public static void DisplaySkillsInfo()
        {
            if (!GuardHelper.RequireInGame()) return;
            if (!GuardHelper.RequireMap()) return;

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
                TolkHelper.Speak(PawnInfoHelper.GetTopSkillsInfo(pawnAtCursor));
                return;
            }

            // Multi-select with no cursor pawn: open pawn picker menu
            if (MultiSelectState.IsMultiSelectActive)
            {
                MultiSelectState.ValidateAndCleanupSelection();
                var options = new List<FloatMenuOption>();
                foreach (var pawn in MultiSelectState.SelectedPawns)
                {
                    string info = PawnInfoHelper.GetTopSkillsInfo(pawn);
                    string label = $"{pawn.LabelShort}: {info}";
                    var p = pawn;
                    options.Add(new FloatMenuOption(label, () =>
                    {
                        TolkHelper.Speak(PawnInfoHelper.GetTopSkillsInfo(p));
                    }));
                }
                WindowlessFloatMenuState.Open(options, false);
                return;
            }

            Pawn selectedPawn = Find.Selector?.FirstSelectedObject as Pawn;
            if (!GuardHelper.RequirePawn(selectedPawn)) return;

            TolkHelper.Speak(PawnInfoHelper.GetTopSkillsInfo(selectedPawn));
        }
    }
}
