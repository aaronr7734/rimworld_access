using System;
using System.Collections.Generic;

namespace RimWorldAccess
{
    /// <summary>
    /// The target chosen for a Home-key jump onto a clump: a tile, plus whether it is the clump's
    /// geometric center (true) or the clump's nearest edge tile to the cursor (false).
    /// </summary>
    internal struct HomeTarget<TTile>
    {
        public TTile Tile;
        public bool IsCenter;
    }

    /// <summary>
    /// Shared "navigate a clump" logic for both scanners. A clump is a connected set of tiles
    /// (terrain patch, biome region, ore deposit, road/river segment, fog blob). The scanner
    /// points the user at the clump's nearest edge tile, and the Home key is context-sensitive:
    /// from outside the clump a manual Home jumps to that nearest edge tile; once the cursor is
    /// standing on the clump a manual Home jumps to the center instead. Auto-jump always targets
    /// the nearest edge tile. This is the single implementation both maps drive — the only
    /// per-map differences are the distance metric and how a tile is jumped to / announced.
    /// </summary>
    internal static class ClumpNav
    {
        public static bool Contains<TTile>(IEnumerable<TTile> members, TTile cursor)
        {
            if (members == null)
                return false;
            // Fast path for HashSet / List / arrays (all implement ICollection<T>).
            if (members is ICollection<TTile> collection)
                return collection.Contains(cursor);
            var comparer = EqualityComparer<TTile>.Default;
            foreach (var m in members)
                if (comparer.Equals(m, cursor))
                    return true;
            return false;
        }

        /// <summary>
        /// Returns the member tile nearest to <paramref name="cursor"/> by
        /// <paramref name="metric"/>, and its metric distance. If the cursor is itself a member,
        /// returns it with distance 0. Returns <c>default</c> with distance
        /// <see cref="float.PositiveInfinity"/> for an empty member set.
        /// </summary>
        public static TTile NearestMember<TTile>(IEnumerable<TTile> members, TTile cursor,
            Func<TTile, TTile, float> metric, out float distance)
        {
            TTile best = default(TTile);
            float bestDist = float.PositiveInfinity;
            var comparer = EqualityComparer<TTile>.Default;

            if (members != null)
            {
                foreach (var m in members)
                {
                    if (comparer.Equals(m, cursor)) { distance = 0f; return m; }
                    float d = metric(cursor, m);
                    if (d < bestDist) { bestDist = d; best = m; }
                }
            }

            distance = bestDist;
            return best;
        }

        /// <summary>
        /// True when this clump has a meaningful "center" to jump to from the cursor's current
        /// position: it has more than one tile, the cursor is standing on it, and the center is a
        /// different tile than the one the cursor occupies. Used to decide whether to append the
        /// "Press Home for center" hint to an announcement.
        /// </summary>
        public static bool OffersCenter<TTile>(IReadOnlyCollection<TTile> members, TTile center, TTile cursor)
        {
            if (members == null || members.Count <= 1)
                return false;
            if (!Contains(members, cursor))
                return false;
            return !EqualityComparer<TTile>.Default.Equals(center, cursor);
        }

        /// <summary>
        /// Decides where a Home press should land. A manual Home from on the clump jumps to the
        /// center when the clump has more than one tile and its center differs from the nearest
        /// edge tile; otherwise the jump targets the nearest edge tile. Auto-jump
        /// (<paramref name="manual"/> == false) always targets the nearest edge tile.
        /// </summary>
        public static HomeTarget<TTile> PlanHome<TTile>(IReadOnlyCollection<TTile> members,
            TTile center, TTile cursor, Func<TTile, TTile, float> metric, bool manual)
        {
            TTile nearest = NearestMember(members, cursor, metric, out _);

            bool centerOption = members != null && members.Count > 1
                && !EqualityComparer<TTile>.Default.Equals(center, nearest);
            bool onClump = Contains(members, cursor);

            if (manual && onClump && centerOption)
                return new HomeTarget<TTile> { Tile = center, IsCenter = true };

            return new HomeTarget<TTile> { Tile = nearest, IsCenter = false };
        }
    }
}
