using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>A rule's rating of one cell and facing. <paramref name="describe"/> false asks for
    /// the score alone, so a sweep skips building phrases it will never speak.</summary>
    internal delegate PlacementQuality PlacementRating(IntVec3 cell, Rot4 rotation, bool describe);

    /// <summary>
    /// Rules for how well a building would work at a cell vanilla's own placement gate accepts.
    /// Some buildings would not work at all there, because the deciding rule lives only in a place
    /// worker's ghost draw (the water mill's overlap with other mills); others work in proportion to
    /// something about the cell (a wind turbine's clear wind path, a solar panel's open sky).
    /// <see cref="PlacementSpotScanner"/> drops spots rated 0 and lists the rest best first. Manual
    /// placement never consults it: a cell vanilla allows stays placeable.
    ///
    /// A registrant supplies a def filter and a per-sweep factory, so whatever the rating needs from
    /// the map (the existing buildings it must not overlap, a mod's grid) is gathered once per
    /// sweep rather than once per candidate cell. A rule that describes something the placement
    /// cursor also speaks must build its phrase from the same calculation, so the two never differ.
    /// </summary>
    internal static class PlacementUsefulnessRegistry
    {
        private struct Entry
        {
            public Predicate<ThingDef> AppliesTo;
            public Func<ThingDef, Map, PlacementRating> MakeRating;
            public bool RotationIndependent;
        }

        private static readonly List<Entry> entries = new List<Entry>
        {
            new Entry
            {
                AppliesTo = WatermillPlacementUsefulness.AppliesTo,
                MakeRating = WatermillPlacementUsefulness.MakeRating,
            },
            new Entry
            {
                AppliesTo = WindTurbineGhostNotePatch.AppliesTo,
                MakeRating = WindTurbineGhostNotePatch.MakeRating,
            },
            new Entry
            {
                // Not rotation-independent: an even footprint shifts with rotation.
                AppliesTo = SolarPlacementQuality.AppliesTo,
                MakeRating = SolarPlacementQuality.MakeRating,
            },
            new Entry
            {
                AppliesTo = DeepDrillPlacementQuality.AppliesTo,
                MakeRating = DeepDrillPlacementQuality.MakeRating,
                RotationIndependent = true,
            },
        };

        public static bool HasPlaceWorker(ThingDef def, Type worker)
        {
            return def.placeWorkers != null && def.placeWorkers.Contains(worker);
        }

        /// <param name="appliesTo">Which defs the rule covers; asked once per sweep.</param>
        /// <param name="makeRating">Builds the sweep's rating of a cell and rotation. May return null
        /// when the rule has nothing to judge on this map.</param>
        /// <param name="rotationIndependent">The rating never reads the rotation, so a sweep rates
        /// each cell once instead of once per facing.</param>
        public static void Register(Predicate<ThingDef> appliesTo, Func<ThingDef, Map, PlacementRating> makeRating,
            bool rotationIndependent = false)
        {
            if (appliesTo == null || makeRating == null)
                return;
            entries.Add(new Entry { AppliesTo = appliesTo, MakeRating = makeRating, RotationIndependent = rotationIndependent });
        }

        /// <summary>
        /// The combined rating of every rule that applies to <paramref name="def"/> on this map, built
        /// once for one sweep, or null when none does. Scores multiply, as each rule judges an
        /// independent reason the building would work less well; phrases join in registration order.
        /// A rule at full quality says nothing in the list, so silence means nothing is lost there.
        /// </summary>
        /// <param name="rotationIndependent">Whether every rule in the combined rating ignores rotation.</param>
        public static PlacementRating RatingFor(ThingDef def, Map map, out bool rotationIndependent)
        {
            rotationIndependent = true;
            var ratings = new List<PlacementRating>();
            foreach (Entry entry in entries)
            {
                try
                {
                    if (!entry.AppliesTo(def))
                        continue;
                    PlacementRating rating = entry.MakeRating(def, map);
                    if (rating == null)
                        continue;
                    ratings.Add(rating);
                    rotationIndependent &= entry.RotationIndependent;
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("Placement usefulness rule error", ex);
                }
            }

            if (ratings.Count == 0)
            {
                rotationIndependent = false;
                return null;
            }
            return (cell, rotation, describe) => Combine(ratings, cell, rotation, describe);
        }

        private static PlacementQuality Combine(List<PlacementRating> ratings, IntVec3 cell, Rot4 rotation, bool describe)
        {
            float score = PlacementSpotRanking.FullQuality;
            List<string> phrases = null;
            for (int i = 0; i < ratings.Count; i++)
            {
                PlacementQuality quality;
                try
                {
                    quality = ratings[i](cell, rotation, describe);
                }
                catch (Exception ex)
                {
                    // A throwing rule must not empty the whole list, so it counts as no objection.
                    ModLogger.LimitedError("Placement usefulness rule error", ex);
                    continue;
                }

                score *= quality.Score;
                if (quality.Score < PlacementSpotRanking.FullQuality && !string.IsNullOrEmpty(quality.Phrase))
                    (phrases ?? (phrases = new List<string>())).Add(quality.Phrase);
            }
            return new PlacementQuality(score, phrases?.ToCommaList());
        }
    }
}
