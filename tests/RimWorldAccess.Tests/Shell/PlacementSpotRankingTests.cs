using System.Collections.Generic;
using RimWorldAccess;

namespace RimWorldAccess.Tests.Shell
{
    public class PlacementSpotRankingTests
    {
        // Rot4.AsInt values, the form the pure layer carries facings in.
        private const int North = 0;
        private const int East = 1;
        private const int South = 2;
        private const int West = 3;

        private static PlacementSpot Spot(float distance, int? requiredFacing = null)
        {
            return new PlacementSpot(0, (int)distance, distance, requiredFacing);
        }

        [Fact]
        public void SelectCapsTheListAtMaxSpots()
        {
            var candidates = new List<PlacementSpot>();
            for (int i = 0; i < PlacementSpotRanking.MaxSpots * 3; i++)
                candidates.Add(Spot(i));

            var selected = PlacementSpotRanking.Select(candidates);

            Assert.Equal(PlacementSpotRanking.MaxSpots, selected.Count);
        }

        [Fact]
        public void SelectKeepsTheNearestCandidatesWhenTheCapBites()
        {
            var candidates = new List<PlacementSpot>();
            for (int i = PlacementSpotRanking.MaxSpots * 2; i >= 0; i--)
                candidates.Add(Spot(i));

            var selected = PlacementSpotRanking.Select(candidates);

            Assert.Equal(0f, selected[0].Distance);
            Assert.Equal(PlacementSpotRanking.MaxSpots - 1, selected[selected.Count - 1].Distance);
        }

        [Fact]
        public void SelectOrdersNearestFirst()
        {
            var selected = PlacementSpotRanking.Select(new List<PlacementSpot>
            {
                Spot(9f), Spot(1f), Spot(4.5f), Spot(2f),
            });

            Assert.Equal(new[] { 1f, 2f, 4.5f, 9f }, selected.ConvertAll(s => s.Distance).ToArray());
        }

        [Fact]
        public void SelectKeepsTheSweepOrderForEqualDistances()
        {
            var selected = PlacementSpotRanking.Select(new List<PlacementSpot>
            {
                new PlacementSpot(5, 0, 3f, null),
                new PlacementSpot(-5, 0, 3f, null),
                new PlacementSpot(0, 3, 3f, null),
            });

            Assert.Equal(new[] { 5, -5, 0 }, selected.ConvertAll(s => s.X).ToArray());
        }

        [Fact]
        public void SelectToleratesNoCandidates()
        {
            Assert.Empty(PlacementSpotRanking.Select(null));
            Assert.Empty(PlacementSpotRanking.Select(new List<PlacementSpot>()));
        }

        [Fact]
        public void SelectPutsBetterSpotsFirstThenNearer()
        {
            var selected = PlacementSpotRanking.Select(new List<PlacementSpot>
            {
                Rated(1f, 0.5f),
                Rated(2f, 1f),
                Rated(3f, 0.8f),
                Rated(4f, 1f),
                Rated(5f, 0.5f),
            });

            Assert.Equal(new[] { 2f, 4f, 3f, 1f, 5f }, selected.ConvertAll(s => s.Distance).ToArray());
        }

        [Fact]
        public void SelectKeepsTheSweepOrderForEqualQualityAndDistance()
        {
            var selected = PlacementSpotRanking.Select(new List<PlacementSpot>
            {
                new PlacementSpot(5, 0, 3f, null, 0.5f, "a"),
                new PlacementSpot(-5, 0, 3f, null, 0.5f, "b"),
                new PlacementSpot(0, 3, 3f, null, 0.5f, "c"),
            });

            Assert.Equal(new[] { "a", "b", "c" }, selected.ConvertAll(s => s.QualityPhrase).ToArray());
        }

        [Fact]
        public void SelectDropsSpotsThatWouldNotWork()
        {
            var selected = PlacementSpotRanking.Select(new List<PlacementSpot>
            {
                Rated(1f, 0f),
                Rated(2f, 0.1f),
                Rated(3f, -1f),
            });

            Assert.Single(selected);
            Assert.Equal(2f, selected[0].Distance);
        }

        [Fact]
        public void UnratedSpotsAreAtFullQuality()
        {
            PlacementSpot spot = Spot(4f);

            Assert.Equal(PlacementSpotRanking.FullQuality, spot.Quality);
            Assert.Null(spot.QualityPhrase);
        }

        [Fact]
        public void TheCapKeepsTheBestWhenItBites()
        {
            var candidates = new List<PlacementSpot>();
            for (int i = 0; i < PlacementSpotRanking.MaxSpots * 2; i++)
                candidates.Add(Rated(i, 0.5f));
            candidates.Add(Rated(1000f, 1f));

            var selected = PlacementSpotRanking.Select(candidates);

            Assert.Equal(PlacementSpotRanking.MaxSpots, selected.Count);
            Assert.Equal(1000f, selected[0].Distance);
        }

        [Fact]
        public void TheSweepStopsOnceTheCapIsFilledAtFullQuality()
        {
            Assert.False(PlacementSpotRanking.SweepComplete(PlacementSpotRanking.MaxSpots - 1, PlacementSpotRanking.MaxSpots - 1));
            Assert.True(PlacementSpotRanking.SweepComplete(PlacementSpotRanking.MaxSpots, PlacementSpotRanking.MaxSpots));
        }

        [Fact]
        public void ImperfectSpotsKeepTheSweepLookingUntilThePoolIsFull()
        {
            Assert.False(PlacementSpotRanking.SweepComplete(0, PlacementSpotRanking.MaxSpots));
            Assert.False(PlacementSpotRanking.SweepComplete(3, PlacementSpotRanking.MaxRatedCandidates - 1));
            Assert.True(PlacementSpotRanking.SweepComplete(3, PlacementSpotRanking.MaxRatedCandidates));
        }

        [Fact]
        public void LastResortsAreListedBelowRealOptionsButNeverFillThePool()
        {
            Assert.False(PlacementSpotRanking.CountsAsWorking(PlacementSpotRanking.FallbackQuality));
            Assert.True(PlacementSpotRanking.CountsAsWorking(PlacementSpotRanking.FallbackQuality + 0.001f));

            var selected = PlacementSpotRanking.Select(new List<PlacementSpot>
            {
                Rated(1f, PlacementSpotRanking.FallbackQuality),
                Rated(9f, 0.02f),
            });

            Assert.Equal(new[] { 9f, 1f }, selected.ConvertAll(s => s.Distance).ToArray());
        }

        [Fact]
        public void ACellThatFitsAtTheCurrentRotationNeedsNoFacing()
        {
            bool isSpot = PlacementSpotRanking.TryChooseFacing(East, Facings(East), out FacingRating best, out int? required);

            Assert.True(isSpot);
            Assert.Equal(East, best.Facing);
            Assert.Null(required);
        }

        [Fact]
        public void TheCurrentRotationWinsEvenWhenOthersAlsoFit()
        {
            bool isSpot = PlacementSpotRanking.TryChooseFacing(South, Facings(North, South, West), out _, out int? required);

            Assert.True(isSpot);
            Assert.Null(required);
        }

        [Fact]
        public void ACellThatFitsOnlySidewaysReportsThatFacing()
        {
            bool isSpot = PlacementSpotRanking.TryChooseFacing(North, Facings(West, East), out _, out int? required);

            Assert.True(isSpot);
            Assert.Equal(West, required);
        }

        [Fact]
        public void ABetterWorkingFacingBeatsTheCurrentOne()
        {
            var ratings = new[]
            {
                new FacingRating(North, 0.6f),
                new FacingRating(East, 1f),
                new FacingRating(South, 0.8f),
            };

            bool isSpot = PlacementSpotRanking.TryChooseFacing(North, ratings, out FacingRating best, out int? required);

            Assert.True(isSpot);
            Assert.Equal(East, required);
            Assert.Equal(1f, best.Quality);
        }

        [Fact]
        public void TheCurrentRotationWinsATieOnQuality()
        {
            var ratings = new[]
            {
                new FacingRating(West, 0.6f),
                new FacingRating(South, 0.6f),
            };

            PlacementSpotRanking.TryChooseFacing(South, ratings, out FacingRating best, out int? required);

            Assert.Equal(South, best.Facing);
            Assert.Null(required);
        }

        [Fact]
        public void FacingsThatWouldNotWorkDoNotCount()
        {
            var ratings = new[]
            {
                new FacingRating(North, 0f),
                new FacingRating(East, 0.2f),
            };

            bool isSpot = PlacementSpotRanking.TryChooseFacing(North, ratings, out _, out int? required);

            Assert.True(isSpot);
            Assert.Equal(East, required);

            Assert.False(PlacementSpotRanking.TryChooseFacing(North, new[] { new FacingRating(North, 0f) }, out _, out required));
            Assert.Null(required);
        }

        [Fact]
        public void ACellNothingFitsInIsNotASpot()
        {
            Assert.False(PlacementSpotRanking.TryChooseFacing(North, new FacingRating[0], out _, out int? required));
            Assert.Null(required);

            Assert.False(PlacementSpotRanking.TryChooseFacing(North, null, out _, out required));
            Assert.Null(required);
        }

        private static PlacementSpot Rated(float distance, float quality)
        {
            return new PlacementSpot(0, (int)distance, distance, null, quality, null);
        }

        private static FacingRating[] Facings(params int[] facings)
        {
            return System.Array.ConvertAll(facings, f => new FacingRating(f, PlacementSpotRanking.FullQuality));
        }
    }
}
