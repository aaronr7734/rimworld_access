using System.Collections.Generic;
using System.Linq;

namespace RimWorldAccess
{
    /// <summary>
    /// One legal position for the building being placed: map coordinates, distance from the cursor
    /// at sweep time, the facing it must be turned to (null when the held rotation is best), and
    /// how well it would work there. Facings are <c>Rot4.AsInt</c> values, free of game types.
    /// </summary>
    public readonly struct PlacementSpot
    {
        public readonly int X;
        public readonly int Z;
        public readonly float Distance;
        public readonly int? RequiredFacing;

        public readonly float Quality;

        public readonly string QualityPhrase;

        public PlacementSpot(int x, int z, float distance, int? requiredFacing)
            : this(x, z, distance, requiredFacing, PlacementSpotRanking.FullQuality, null)
        {
        }

        public PlacementSpot(int x, int z, float distance, int? requiredFacing, float quality, string qualityPhrase)
        {
            X = x;
            Z = z;
            Distance = distance;
            RequiredFacing = requiredFacing;
            Quality = quality;
            QualityPhrase = qualityPhrase;
        }
    }

    /// <summary>One facing a building fits at, with how well it would work facing that way.</summary>
    public readonly struct FacingRating
    {
        public readonly int Facing;
        public readonly float Quality;
        public readonly string Phrase;

        public FacingRating(int facing, float quality, string phrase)
        {
            Facing = facing;
            Quality = quality;
            Phrase = phrase;
        }
    }

    /// <summary>
    /// The cap, ordering, stop rule and facing choice behind the placement-spot scanner category,
    /// free of game types so they are unit-tested; the map sweep lives in PlacementSpotScanner.Game.cs.
    /// </summary>
    public static class PlacementSpotRanking
    {
        /// <summary>How many positions the category lists. The one place to revisit the cap.</summary>
        public const int MaxSpots = 25;

        /// <summary>The quality of a spot nothing can beat, and of every spot no rule rates.</summary>
        public const float FullQuality = 1f;

        /// <summary>A last resort's score: listed, but worse than any real option.</summary>
        public const float FallbackQuality = 0.01f;

        /// <summary>Working spots a rated sweep gathers before it stops looking for better ones;
        /// bounds the cost where perfect spots are rare, as each rated cell tries every facing.</summary>
        public const int MaxRatedCandidates = MaxSpots * 4;

        /// <summary>
        /// How far from the cursor the sweep looks; it only bounds the hopeless case where almost
        /// nothing fits. Must stay under GenRadial.MaxRadialPatternRadius (~79 with the
        /// 20,000-cell pattern) or the sweep silently truncates.
        /// </summary>
        public const float SearchRadius = 60f;

        /// <summary>How far the cursor may drift from the last sweep's origin before the category
        /// is re-swept, keeping spots relevant without a sweep on every step.</summary>
        public const float ResweepDistance = SearchRadius / 2f;

        /// <summary>
        /// Best first, then nearest, capped at <see cref="MaxSpots"/>; spots rated 0 or below would
        /// not work and are dropped. Stable, so equal spots keep the sweep's radial order.
        /// </summary>
        public static List<PlacementSpot> Select(IEnumerable<PlacementSpot> candidates)
        {
            if (candidates == null)
                return new List<PlacementSpot>();

            return candidates
                .Where(spot => spot.Quality > 0f)
                .OrderByDescending(spot => spot.Quality)
                .ThenBy(spot => spot.Distance)
                .Take(MaxSpots)
                .ToList();
        }

        /// <summary>Whether a nearest-first sweep can stop: no later cell outranks <see cref="MaxSpots"/>
        /// full-quality spots, which for an unrated building is the plain cap.</summary>
        public static bool SweepComplete(int fullQualitySpots, int workingSpots)
        {
            return fullQualitySpots >= MaxSpots || workingSpots >= MaxRatedCandidates;
        }

        /// <summary>Last resorts never fill <see cref="MaxRatedCandidates"/>, so they cannot end the sweep early.</summary>
        public static bool CountsAsWorking(float quality)
        {
            return quality > FallbackQuality;
        }

        /// <summary>
        /// The best-working facing that fits, false when none works (rated 0 or below). The held
        /// facing wins a tie, leaving <paramref name="requiredFacing"/> null, so turning is only
        /// asked for when it works better; otherwise the first in the list wins.
        /// </summary>
        public static bool TryChooseFacing(int currentFacing, IReadOnlyList<FacingRating> ratings, out FacingRating best, out int? requiredFacing)
        {
            best = default(FacingRating);
            requiredFacing = null;
            if (ratings == null)
                return false;

            bool found = false;
            for (int i = 0; i < ratings.Count; i++)
            {
                FacingRating rating = ratings[i];
                if (rating.Quality <= 0f)
                    continue;
                if (!found || rating.Quality > best.Quality
                    || (rating.Quality == best.Quality && rating.Facing == currentFacing))
                {
                    best = rating;
                    found = true;
                }
            }

            if (found && best.Facing != currentFacing)
                requiredFacing = best.Facing;
            return found;
        }
    }
}
