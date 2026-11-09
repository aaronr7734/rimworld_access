using System;
using System.Collections.Generic;
using System.Linq;

namespace RimWorldAccess.Shell
{
    /// <summary>One row's geometry as <see cref="PaneOrder"/> needs it, described without any GUI/game types.</summary>
    public struct PaneOrderRow
    {
        /// <summary>Opaque identity of the row's clipping context — only equality is used, exactly like <see cref="BandLabelCandidate.ClipId"/>.</summary>
        public int ClipId;

        /// <summary>Opaque identity of the innermost scroll view the row drew in, or null outside every scroll view.</summary>
        public int? ContainerId;

        public float X;
        public float XMax;
        public float Y;
        public float YMax;
    }

    /// <summary>
    /// Ranks each row's pane, so a window of side-by-side scroll panes (Colony Manager Redux's
    /// three columns) reads pane by pane instead of zipper-interleaved by raw Y-then-X. A pane
    /// is a capture clip, ordered by the minimum (Y, X) of its rows; clip visible rects are in
    /// local space, so the rows' screen rects are the only reliable position.
    ///
    /// Every BeginGroup opens a clip, so a listing nested inside a scroll view (a collapsible
    /// section, Listing_Standard.BeginSection) is a clip of its own though it is a stretch of
    /// the same column. Clips of one scroll view whose X spans overlap are one pane, unless
    /// that would join two clips side by side (overlapping in Y, disjoint in X).
    ///
    /// The rank is the caller's PRIMARY sort key; rows of one pane tie, so their order is left
    /// entirely to the caller's secondary Y-then-X key.
    /// </summary>
    public static class PaneOrder
    {
        private sealed class Clip
        {
            public int? ContainerId;
            public float MinX = float.MaxValue;
            public float MaxX = float.MinValue;
            public float MinY = float.MaxValue;
            public float MaxY = float.MinValue;
            public Clip Parent;
            public List<Clip> Members;

            public Clip Root()
            {
                Clip root = this;
                while (root.Parent != null)
                {
                    root = root.Parent;
                }
                return root;
            }
        }

        /// <summary>
        /// Computes one pane rank per row in <paramref name="rows"/>, index-aligned with the
        /// input. Never null. A single distinct <see cref="PaneOrderRow.ClipId"/> across every
        /// row (including the "no clip info at all" case, where a caller assigns every row the
        /// same id) yields rank 0 for all of them — the no-op case that leaves a caller's
        /// existing secondary sort as the sole ordering, matching today's behavior exactly.
        /// </summary>
        public static IReadOnlyList<int> ComputePaneRanks(IReadOnlyList<PaneOrderRow> rows)
        {
            int n = rows == null ? 0 : rows.Count;
            var ranks = new int[n];
            if (n == 0)
            {
                return ranks;
            }

            var clips = new Dictionary<int, Clip>();
            var order = new List<Clip>();
            for (int i = 0; i < n; i++)
            {
                PaneOrderRow row = rows[i];
                Clip clip;
                if (!clips.TryGetValue(row.ClipId, out clip))
                {
                    clip = new Clip { ContainerId = row.ContainerId };
                    clip.Members = new List<Clip> { clip };
                    clips.Add(row.ClipId, clip);
                    order.Add(clip);
                }
                clip.MinX = Math.Min(clip.MinX, row.X);
                clip.MaxX = Math.Max(clip.MaxX, Math.Max(row.X, row.XMax));
                clip.MinY = Math.Min(clip.MinY, row.Y);
                clip.MaxY = Math.Max(clip.MaxY, Math.Max(row.Y, row.YMax));
            }

            for (int a = 0; a < order.Count; a++)
            {
                for (int b = a + 1; b < order.Count; b++)
                {
                    if (!SameColumnOfOneScrollView(order[a], order[b]))
                    {
                        continue;
                    }
                    Clip rootA = order[a].Root();
                    Clip rootB = order[b].Root();
                    if (rootA != rootB && !AnySideBySide(rootA.Members, rootB.Members))
                    {
                        rootB.Parent = rootA;
                        rootA.Members.AddRange(rootB.Members);
                    }
                }
            }

            var paneMinY = new Dictionary<Clip, float>();
            var paneMinX = new Dictionary<Clip, float>();
            foreach (Clip clip in order)
            {
                Clip pane = clip.Root();
                float y;
                if (!paneMinY.TryGetValue(pane, out y) || clip.MinY < y)
                {
                    paneMinY[pane] = clip.MinY;
                }
                float x;
                if (!paneMinX.TryGetValue(pane, out x) || clip.MinX < x)
                {
                    paneMinX[pane] = clip.MinX;
                }
            }

            List<Clip> paneOrder = paneMinY.Keys
                .OrderBy(pane => paneMinY[pane])
                .ThenBy(pane => paneMinX[pane])
                .ToList();

            var rank = new Dictionary<Clip, int>();
            for (int i = 0; i < paneOrder.Count; i++)
            {
                rank[paneOrder[i]] = i;
            }

            for (int i = 0; i < n; i++)
            {
                ranks[i] = rank[clips[rows[i].ClipId].Root()];
            }
            return ranks;
        }

        private static bool SameColumnOfOneScrollView(Clip a, Clip b)
        {
            return a.ContainerId.HasValue && a.ContainerId == b.ContainerId
                && a.MinX < b.MaxX && b.MinX < a.MaxX;
        }

        private static bool AnySideBySide(List<Clip> left, List<Clip> right)
        {
            foreach (Clip a in left)
            {
                foreach (Clip b in right)
                {
                    bool sharedBand = a.MinY < b.MaxY && b.MinY < a.MaxY;
                    bool disjointX = a.MaxX <= b.MinX || b.MaxX <= a.MinX;
                    if (sharedBand && disjointX)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
