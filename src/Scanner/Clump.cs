using System;
using System.Collections.Generic;

namespace RimWorldAccess
{
    /// <summary>
    /// Coordinate-agnostic flood-fill and adjacency grouping shared by the local map scanner
    /// (square <c>IntVec3</c> grid) and the world map scanner (hex planet-tile grid, keyed by
    /// int tile id). Both scanners group contiguous tiles of the same kind — terrain patches,
    /// biome regions, ore deposits, stone, road/river segments, fog blobs — into "clumps", and
    /// this is the single implementation of that grouping.
    /// </summary>
    internal static class Clump
    {
        /// <summary>
        /// Returns every tile reachable from <paramref name="start"/> through
        /// <paramref name="neighbors"/> while staying inside <paramref name="valid"/>. Membership
        /// in <paramref name="valid"/> is the sole gate: callers that need a typed constraint
        /// (e.g. "same road type") encode it by building <paramref name="valid"/> from exactly the
        /// tiles that satisfy it, rather than filtering inside the neighbor function.
        /// </summary>
        public static HashSet<TTile> Fill<TTile>(TTile start, HashSet<TTile> valid,
            Func<TTile, IEnumerable<TTile>> neighbors)
        {
            var region = new HashSet<TTile>();
            if (valid == null || neighbors == null || !valid.Contains(start))
                return region;

            var queue = new Queue<TTile>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!valid.Contains(current) || region.Contains(current))
                    continue;

                region.Add(current);

                foreach (var neighbor in neighbors(current))
                {
                    if (valid.Contains(neighbor) && !region.Contains(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            return region;
        }

        /// <summary>
        /// Partitions <paramref name="positions"/> into connected member-sets (clumps). Each
        /// returned set is a maximal group of tiles mutually reachable via
        /// <paramref name="neighbors"/>. The caller wraps each set into its own region type and
        /// computes its center and distance; this method only performs the grouping. Discovery
        /// order matches a single linear sweep of the input, so callers that sort the result by
        /// distance afterwards (via a stable sort) get a deterministic order.
        /// </summary>
        public static List<HashSet<TTile>> GroupByAdjacency<TTile>(IEnumerable<TTile> positions,
            Func<TTile, IEnumerable<TTile>> neighbors)
        {
            var groups = new List<HashSet<TTile>>();
            var remaining = new HashSet<TTile>(positions);

            while (remaining.Count > 0)
            {
                TTile start = default(TTile);
                bool found = false;
                foreach (var p in remaining) { start = p; found = true; break; }
                if (!found)
                    break;

                var group = Fill(start, remaining, neighbors);
                if (group.Count == 0)
                {
                    // Defensive: start is always in `remaining`, so Fill returns it; this only
                    // guards against a misbehaving neighbor function so the loop cannot spin.
                    remaining.Remove(start);
                    continue;
                }

                groups.Add(group);
                foreach (var t in group)
                    remaining.Remove(t);
            }

            return groups;
        }
    }
}
