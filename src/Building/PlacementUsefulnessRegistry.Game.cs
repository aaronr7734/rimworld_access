using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Rules for buildings that vanilla's own placement gate accepts at a cell where they would
    /// not actually work, because the deciding rule lives only in a place worker's ghost draw (the
    /// water mill's overlap with other mills is the vanilla case). Consulted by
    /// <see cref="PlacementSpotScanner"/> in addition to <c>CanDesignateCell</c>, so recommended
    /// spots are cells the building would work at. Manual placement never consults it: a cell
    /// vanilla allows stays placeable.
    ///
    /// A registrant supplies a def filter and a per-sweep factory, so whatever the test needs from
    /// the map (the existing buildings it must not overlap, a mod's grid) is gathered once per
    /// sweep rather than once per candidate cell.
    /// </summary>
    internal static class PlacementUsefulnessRegistry
    {
        private struct Entry
        {
            public Predicate<ThingDef> AppliesTo;
            public Func<ThingDef, Map, Func<IntVec3, Rot4, bool>> MakeTest;
        }

        private static readonly List<Entry> entries = new List<Entry>
        {
            new Entry
            {
                AppliesTo = WatermillPlacementUsefulness.AppliesTo,
                MakeTest = WatermillPlacementUsefulness.MakeTest,
            },
        };

        /// <param name="appliesTo">Which defs the rule covers; asked once per sweep.</param>
        /// <param name="makeTest">Builds the sweep's cell test, true when the building would work at
        /// that cell and rotation. May return null when the rule has nothing to check on this map.</param>
        public static void Register(Predicate<ThingDef> appliesTo, Func<ThingDef, Map, Func<IntVec3, Rot4, bool>> makeTest)
        {
            if (appliesTo == null || makeTest == null)
                return;
            entries.Add(new Entry { AppliesTo = appliesTo, MakeTest = makeTest });
        }

        /// <summary>The cell tests that apply to <paramref name="def"/> on this map, built once for one sweep.</summary>
        public static List<Func<IntVec3, Rot4, bool>> TestsFor(ThingDef def, Map map)
        {
            var tests = new List<Func<IntVec3, Rot4, bool>>();
            foreach (Entry entry in entries)
            {
                try
                {
                    if (!entry.AppliesTo(def))
                        continue;
                    Func<IntVec3, Rot4, bool> test = entry.MakeTest(def, map);
                    if (test != null)
                        tests.Add(test);
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("Placement usefulness rule error", ex);
                }
            }
            return tests;
        }
    }
}
