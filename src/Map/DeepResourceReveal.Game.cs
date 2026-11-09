using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Whether deep resources are readable on a map: an active ground-penetrating scanner, or a
    /// compat module's own reveal rule for a mod that shows the deep-resource readout without one.
    /// </summary>
    internal static class DeepResourceReveal
    {
        private static readonly List<Func<Map, bool>> reveals = new List<Func<Map, bool>>();

        /// <param name="reveal">True when the mod is showing deep resources on that map right now.</param>
        public static void Register(Func<Map, bool> reveal)
        {
            if (reveal != null)
                reveals.Add(reveal);
        }

        public static bool Revealed(Map map)
        {
            return map.deepResourceGrid.AnyActiveDeepScannersOnMap() || ModRevealed(map);
        }

        /// <summary>The compat reveal rules alone, for a context whose vanilla gate is already checked.</summary>
        public static bool ModRevealed(Map map)
        {
            foreach (Func<Map, bool> reveal in reveals)
            {
                try
                {
                    if (reveal(map))
                        return true;
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("Deep resource reveal error", ex);
                }
            }
            return false;
        }
    }
}
