using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Replacement order menus for selections a mod takes over on right-click (a
    /// <c>Selector.HandleMapClicks</c> prefix that opens its own FloatMenu instead of vanilla's).
    /// The keyboard order brackets never pass through that click path, so a mod registers its
    /// replacement here: given the selected pawns and the cursor cell, it returns the options its
    /// own menu would list, or null to leave vanilla's list in place.
    /// </summary>
    internal static class OrderOptionOverrides
    {
        private static readonly List<Func<List<Pawn>, IntVec3, List<FloatMenuOption>>> overrides =
            new List<Func<List<Pawn>, IntVec3, List<FloatMenuOption>>>();

        public static void Register(Func<List<Pawn>, IntVec3, List<FloatMenuOption>> provider)
        {
            overrides.Add(provider);
        }

        public static List<FloatMenuOption> TryGet(List<Pawn> pawns, IntVec3 cell)
        {
            foreach (Func<List<Pawn>, IntVec3, List<FloatMenuOption>> provider in overrides)
            {
                try
                {
                    List<FloatMenuOption> options = provider(pawns, cell);
                    if (options != null)
                    {
                        return options;
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("OrderOptionOverrides", ex);
                }
            }
            return null;
        }
    }
}
