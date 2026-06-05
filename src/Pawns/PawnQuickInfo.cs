using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared entry point for the Alt+H / Alt+M / Alt+N / Alt+G / Alt+K
    /// pawn quick-info hotkeys. Each hotkey-specific state class (HealthState,
    /// MoodState, NeedsState, GearState, SkillsState) is now a thin wrapper
    /// that delegates here with its own PawnInfoHelper extractor.
    ///
    /// Resolution order:
    ///   1. Pawn at cursor (takes priority even when multiple pawns are selected).
    ///   2. Multi-select — opens a picker float menu.
    ///   3. Single selected pawn via Find.Selector.
    ///   4. "No pawn selected" announcement via GuardHelper.
    /// </summary>
    public static class PawnQuickInfo
    {
        /// <summary>
        /// Resolves the target pawn for a quick-info hotkey and speaks the
        /// extracted info. Returns early (with the canonical announcement)
        /// if the game state isn't ready or no pawn can be resolved.
        /// </summary>
        /// <param name="extractor">
        /// Function that turns a pawn into the announcement text
        /// (e.g. <c>PawnInfoHelper.GetGearInfo</c>).
        /// </param>
        public static void Display(Func<Pawn, string> extractor)
        {
            if (!GuardHelper.RequireInGame()) return;
            if (!GuardHelper.RequireMap(out Map map)) return;

            Pawn pawnAtCursor = TryResolvePawnAtCursor(map);
            if (pawnAtCursor != null)
            {
                TolkHelper.SpeakData(extractor(pawnAtCursor));
                return;
            }

            if (MultiSelectState.IsMultiSelectActive)
            {
                MultiSelectState.ValidateAndCleanupSelection();
                var options = new List<FloatMenuOption>();
                foreach (var pawn in MultiSelectState.SelectedPawns)
                {
                    string info = extractor(pawn);
                    string label = "RimWorldAccess.Pawns.QuickInfo.PickerRow".Translate(pawn.LabelShort, info);
                    var p = pawn;
                    options.Add(new FloatMenuOption(label, () => TolkHelper.SpeakData(extractor(p))));
                }
                WindowlessFloatMenuState.Open(options, false);
                return;
            }

            Pawn selectedPawn = Find.Selector?.FirstSelectedObject as Pawn;
            if (!GuardHelper.RequirePawn(selectedPawn)) return;

            TolkHelper.SpeakData(extractor(selectedPawn));
        }

        private static Pawn TryResolvePawnAtCursor(Map map)
        {
            if (!MapNavigationState.IsInitialized) return null;

            IntVec3 cursor = MapNavigationState.CurrentCursorPosition;
            if (!cursor.IsValid || !cursor.InBounds(map)) return null;

            return map.thingGrid.ThingsListAt(cursor).OfType<Pawn>().FirstOrDefault();
        }
    }
}
