using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// While a building is being placed, offers the positions it actually fits in as a temporary
    /// scanner category, best first and nearest first among equals, announcing the facing a spot
    /// needs when the held rotation does not fit there (or works worse) and how well the building
    /// would work there. The scanner's Home jump lands on the spot and applies that
    /// facing, leaving Space to place as normal. The category is an OFFER: it parks at the front of
    /// the category cycle without stealing the scanner focus.
    ///
    /// No vanilla API precomputes legal cells, so the sweep walks GenRadial's nearest-first pattern
    /// and asks the designator's own CanDesignateCell about each cell. Blueprints and frames are
    /// Things at their cells, so a just-placed blueprint is honored for free. How well a building
    /// would work at a cell the gate accepts (the water mill's overlap rule, a wind turbine's wind
    /// path, mod rules) comes from <see cref="PlacementUsefulnessRegistry"/>; spots it rates 0 are
    /// dropped.
    ///
    /// Distances are recomputed only on rebuild; the scanner re-derives live distance and direction
    /// per announcement, so a single cursor step is not a rebuild trigger. Rebuilds happen when the
    /// designator or placing rotation changes, when a placement completes, when the cursor drifts
    /// past <see cref="PlacementSpotRanking.ResweepDistance"/> from the sweep origin, and when
    /// category cycling lands here (<see cref="RefreshOnEntry"/>).
    /// </summary>
    public static class PlacementSpotScanner
    {
        private static Designator_Place sweptFor;
        private static Rot4 sweptRotation;
        private static IntVec3 sweptOrigin = IntVec3.Invalid;
        private static bool categoryLive;

        // Facing to apply when the Home jump lands on this cell. Only spots that need turning
        // are present; a spot that fits as currently rotated has no entry.
        private static readonly Dictionary<IntVec3, Rot4> requiredFacings = new Dictionary<IntVec3, Rot4>();

        /// <summary>Per-frame build/teardown, driven from <see cref="Shell.PlacementScopeMirror"/> so
        /// the category's lifetime matches placement mode's exactly.</summary>
        public static void Reconcile()
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                Reset();
                return;
            }

            Designator_Place designator = ActivePlaceDesignator();
            if (designator == null)
            {
                Teardown();
                return;
            }

            Rot4 rotation = BuildingReflection.GetPlacingRot(designator);
            if (designator != sweptFor || rotation != sweptRotation || CursorLeftSweep())
                Rebuild(designator, rotation);
        }

        /// <summary>Whether the cursor has left the last sweep's origin far enough that the listed spots
        /// describe somewhere the player no longer is. Held back while a browsing session is live: the
        /// rebuild would invalidate that session's frozen order mid-browse, and the session's end is
        /// the natural moment to re-sweep.</summary>
        private static bool CursorLeftSweep()
        {
            if (ScannerState.NavigationSessionActive)
                return false;
            IntVec3 cursor = MapNavigationState.CurrentCursorPosition;
            return cursor.IsValid && sweptOrigin.IsValid
                && (cursor - sweptOrigin).LengthHorizontal > PlacementSpotRanking.ResweepDistance;
        }

        /// <summary>Category cycling landed here: re-sweep from wherever the cursor is now, so entering
        /// always lists spots around the player. False when nothing fits any more and the category
        /// dropped out of the ring.</summary>
        private static bool RefreshOnEntry()
        {
            Designator_Place designator = ActivePlaceDesignator();
            if (designator == null || !categoryLive)
                return true;

            Rot4 rotation = BuildingReflection.GetPlacingRot(designator);
            if (designator == sweptFor && rotation == sweptRotation
                && MapNavigationState.CurrentCursorPosition == sweptOrigin)
                return true;

            Rebuild(designator, rotation);
            return categoryLive;
        }

        /// <summary>After a successful placement the new blueprint changes what fits, so the next
        /// <see cref="Reconcile"/> re-sweeps.</summary>
        public static void NotifyPlacementCompleted()
        {
            sweptFor = null;
        }

        /// <summary>Applies the landed spot's required facing after a scanner Home jump; a no-op unless
        /// the live temporary category is ours and that cell needs turning.</summary>
        public static void ApplyFacingAtCursor()
        {
            if (!categoryLive || requiredFacings.Count == 0)
                return;
            if (!ScannerState.IsInTemporaryCategory())
                return;
            if (!requiredFacings.TryGetValue(MapNavigationState.CurrentCursorPosition, out Rot4 facing))
                return;

            ArchitectState.SetBuildingRotation(facing);
        }

        /// <summary>Session-boundary reset: drops the tracking fields without touching the scanner,
        /// whose categories the boundary rebuilds anyway.</summary>
        public static void Reset()
        {
            sweptFor = null;
            sweptOrigin = IntVec3.Invalid;
            categoryLive = false;
            requiredFacings.Clear();
            // The refresh hook must not outlive the placement session it was registered for.
            ScannerState.TemporaryCategoryRefreshHook = null;
        }

        private static Designator_Place ActivePlaceDesignator()
        {
            if (Find.CurrentMap == null || !MapNavigationState.IsInitialized)
                return null;

            // Orders, zones and area designators have no footprint to fit, and an install
            // designator placing a non-ThingDef has nothing to test rotations against.
            return Find.DesignatorManager?.SelectedDesignator is Designator_Place place
                && place.PlacingDef is ThingDef
                ? place
                : null;
        }

        private static void Teardown()
        {
            sweptFor = null;
            RemoveCategory();
        }

        private static void RemoveCategory()
        {
            requiredFacings.Clear();

            if (!categoryLive)
                return;

            categoryLive = false;
            ScannerState.TemporaryCategoryRefreshHook = null;
            // Focus is only restored for a player the removal actually displaces — one who
            // chose to navigate into the category; everyone else keeps their place untouched.
            bool wasInside = ScannerState.IsInTemporaryCategory();
            ScannerState.RemoveTemporaryCategory();
            if (wasInside)
                ScannerState.RestoreFocus();
        }

        private static void Rebuild(Designator_Place designator, Rot4 rotation)
        {
            sweptFor = designator;
            sweptRotation = rotation;

            Map map = Find.CurrentMap;
            var def = (ThingDef)designator.PlacingDef;

            IntVec3 cursor = MapNavigationState.CurrentCursorPosition;
            if (!cursor.IsValid)
                cursor = map.Center;
            sweptOrigin = cursor;

            List<PlacementSpot> spots = Sweep(designator, def, map, cursor, rotation);
            if (spots.Count == 0)
            {
                // Nothing fits within reach: hand the player their own scanner category back
                // rather than an empty one.
                RemoveCategory();
                return;
            }

            PublishCategory(def, cursor, spots);
        }

        private static List<PlacementSpot> Sweep(
            Designator_Place designator, ThingDef def, Map map, IntVec3 cursor, Rot4 rotation)
        {
            var context = new SweepContext(designator, def, map);
            var spots = new List<PlacementSpot>();
            var ratings = new List<FacingRating>();
            int fullQualitySpots = 0;
            int workingSpots = 0;

            try
            {
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(cursor, PlacementSpotRanking.SearchRadius, useCenter: true))
                {
                    if (PlacementSpotRanking.SweepComplete(fullQualitySpots, workingSpots))
                        break;
                    // No dedup set needed: GenRadial's pattern visits each offset exactly once.
                    if (!cell.InBounds(map))
                        continue;

                    RateFacings(context, def, cell, rotation, ratings);
                    if (!PlacementSpotRanking.TryChooseFacing(rotation.AsInt, ratings, out FacingRating best, out int? requiredFacing))
                        continue;

                    if (best.Quality >= PlacementSpotRanking.FullQuality)
                        fullQualitySpots++;
                    if (PlacementSpotRanking.CountsAsWorking(best.Quality))
                        workingSpots++;
                    spots.Add(new PlacementSpot(cell.x, cell.z, (cell - cursor).LengthHorizontal, requiredFacing,
                        best.Quality, best.Phrase));
                }
            }
            finally
            {
                BuildingReflection.SetPlacingRot(designator, rotation);
            }

            return PlacementSpotRanking.Select(spots);
        }

        /// <summary>Every facing worth considering at a cell: the first fit, held rotation first, unless
        /// a rule rates the building, when turning it can make it work better.</summary>
        private static void RateFacings(SweepContext context, ThingDef def, IntVec3 cell, Rot4 rotation, List<FacingRating> ratings)
        {
            ratings.Clear();
            if (context.TryRate(cell, rotation, out PlacementQuality held))
            {
                ratings.Add(new FacingRating(rotation.AsInt, held.Score, held.Phrase));
                if (!context.Rated || held.Score >= PlacementSpotRanking.FullQuality)
                    return;
            }

            foreach (Rot4 alternate in AlternateFacings(def, rotation))
            {
                if (!context.TryRate(cell, alternate, out PlacementQuality quality))
                    continue;
                ratings.Add(new FacingRating(alternate.AsInt, quality.Score, quality.Phrase));
                if (!context.Rated || quality.Score >= PlacementSpotRanking.FullQuality)
                    return;
            }
        }

        private static IEnumerable<Rot4> AlternateFacings(ThingDef def, Rot4 rotation)
        {
            // Vanilla's own rotate gate (decompiled/RimWorld/Designator_Place.cs:109,124).
            if (!def.rotatable)
                yield break;

            for (int step = 1; step < 4; step++)
                yield return new Rot4((rotation.AsInt + step) % 4);
        }

        private static void PublishCategory(ThingDef def, IntVec3 cursor, List<PlacementSpot> spots)
        {
            requiredFacings.Clear();

            string itemLabel = "RimWorldAccess.Building.Place.SpotScannerItemLabel".Translate();
            var items = new List<ScannerItem>();
            var details = new List<string>(2);
            foreach (PlacementSpot spot in spots)
            {
                var cell = new IntVec3(spot.X, 0, spot.Z);
                var item = new ScannerItem(cell, itemLabel, cursor);

                details.Clear();
                if (spot.RequiredFacing.HasValue)
                {
                    var facing = new Rot4(spot.RequiredFacing.Value);
                    requiredFacings[cell] = facing;
                    details.Add("RimWorldAccess.Building.Place.SpotFacing".Translate(
                        BuildingCellHelper.GetCardinalDirection(facing.FacingCell)));
                }
                if (!string.IsNullOrEmpty(spot.QualityPhrase))
                    details.Add(spot.QualityPhrase.UncapitalizeFirst());
                if (details.Count > 0)
                    item.DetailSuffix = details.ToCommaList();

                items.Add(item);
            }

            // There is one temporary-category slot, and an active search filter owns it and rebuilds it
            // on every navigation, so clear the filter (silently) for placement to take it over.
            if (ScannerSearchState.HasActiveFilter)
                ScannerSearchState.ClearSearchSilent();

            // Only the first build saves the player's own scanner focus; a rebuild would otherwise
            // save this category as the thing to restore to.
            if (!categoryLive)
                ScannerState.SaveFocus();

            ScannerState.OfferTemporaryCategory(
                "RimWorldAccess.Building.Place.SpotScannerCategory".Translate(def.LabelCap), items);
            ScannerState.TemporaryCategoryRefreshHook = RefreshOnEntry;
            categoryLive = true;
        }

        /// <summary>What the per-cell test needs resolved once per sweep: the designator whose gate
        /// decides, plus the exclusions that gate does not cover.</summary>
        private sealed class SweepContext
        {
            private readonly Designator_Place designator;
            private readonly ThingDef def;
            private readonly Map map;
            private readonly bool checkMeditationProtection;
            private readonly Func<IntVec3, Rot4, PlacementQuality> rating;

            public SweepContext(Designator_Place designator, ThingDef def, Map map)
            {
                this.designator = designator;
                this.def = def;
                this.map = map;

                checkMeditationProtection =
                    MeditationProtectionHelper.IsArtificialBuilding(def, Faction.OfPlayer);
                rating = PlacementUsefulnessRegistry.RatingFor(def, map);
            }

            /// <summary>Whether a usefulness rule rates this building, so spots can differ in quality.</summary>
            public bool Rated => rating != null;

            /// <summary>Whether the building fits at the cell and facing, and how well it would work
            /// there. Cheapest exclusion first: vanilla's validator, then meditation/Gauranlen
            /// protection, then the registered usefulness rules.</summary>
            public bool TryRate(IntVec3 cell, Rot4 rotation, out PlacementQuality quality)
            {
                quality = PlacementQuality.Full;
                BuildingReflection.SetPlacingRot(designator, rotation);
                if (!designator.CanDesignateCell(cell).Accepted)
                    return false;

                // HandleSpaceKey refuses these cells outright, so listing them would be a lie. Safe per
                // candidate: the helper reads two small lister groups, never a per-cell cache.
                if (checkMeditationProtection
                    && MeditationProtectionHelper.CheckProtection(map, def, Faction.OfPlayer, cell, rotation).IsProtected)
                    return false;

                if (rating != null)
                    quality = rating(cell, rotation);
                return true;
            }
        }
    }
}
