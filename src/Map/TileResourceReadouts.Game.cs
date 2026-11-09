using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Extra per-cell resource lines a compat module adds to the resources key, for map layers a
    /// mod draws as an overlay (a water table, a subsurface deposit) that no vanilla grid holds.
    /// A provider returns null for a cell it has nothing to say about, so an absent mod costs one
    /// empty list walk.
    /// </summary>
    internal static class TileResourceReadouts
    {
        private static readonly List<Func<IntVec3, Map, string>> providers = new List<Func<IntVec3, Map, string>>();

        public static void Register(Func<IntVec3, Map, string> provider)
        {
            if (provider != null)
                providers.Add(provider);
        }

        /// <summary>Every provider's line for the cell, in registration order; a throwing provider costs only its own line.</summary>
        public static List<string> Collect(IntVec3 position, Map map)
        {
            var lines = new List<string>();
            foreach (var provider in providers)
            {
                try
                {
                    string line = provider(position, map);
                    if (!string.IsNullOrEmpty(line))
                        lines.Add(line);
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("Tile resource readout error", ex);
                }
            }
            return lines;
        }
    }
}
