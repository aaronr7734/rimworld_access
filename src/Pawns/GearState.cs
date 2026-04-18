using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State class for displaying gear information of the pawn at the cursor position.
    /// Triggered by Alt+G key combination.
    /// </summary>
    public static class GearState
    {
        /// <summary>
        /// Displays gear information for the pawn at the current cursor position.
        /// Shows weapon being wielded and apparel being worn, with quality.
        /// In multi-select mode, opens a pawn picker menu with gear info for each pawn.
        /// </summary>
        public static void DisplayGearInfo()
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
                TolkHelper.Speak(PawnInfoHelper.GetGearInfo(pawnAtCursor));
                return;
            }

            // Multi-select with no cursor pawn: open pawn picker menu
            if (MultiSelectState.IsMultiSelectActive)
            {
                MultiSelectState.ValidateAndCleanupSelection();
                var options = new List<FloatMenuOption>();
                foreach (var pawn in MultiSelectState.SelectedPawns)
                {
                    string info = PawnInfoHelper.GetGearInfo(pawn);
                    string label = $"{pawn.LabelShort}: {info}";
                    var p = pawn;
                    options.Add(new FloatMenuOption(label, () =>
                    {
                        TolkHelper.Speak(PawnInfoHelper.GetGearInfo(p));
                    }));
                }
                WindowlessFloatMenuState.Open(options, false);
                return;
            }

            Pawn selectedPawn = Find.Selector?.FirstSelectedObject as Pawn;
            if (!GuardHelper.RequirePawn(selectedPawn)) return;

            TolkHelper.Speak(PawnInfoHelper.GetGearInfo(selectedPawn));
        }
    }
}
