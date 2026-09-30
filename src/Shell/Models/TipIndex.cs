using System;
using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>Axis-aligned rectangle for tooltip geometry, deliberately not UnityEngine.Rect so
    /// this file stays pure and links into the test project.</summary>
    public readonly struct TipRect
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Width;
        public readonly float Height;

        public TipRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        /// <summary>Strict-interior overlap: rectangles that merely share an edge do not overlap.</summary>
        public bool Overlaps(TipRect other)
        {
            return other.X < X + Width
                && other.X + other.Width > X
                && other.Y < Y + Height
                && other.Y + other.Height > Y;
        }
    }

    /// <summary>Pure mirror of the game-side <c>GuiSpace.ClipKey</c> — the active GUIClip context —
    /// carried alongside a screen rect so <see cref="TipIndex.QueryAtScreen"/> can require context
    /// equality rather than rect overlap alone.</summary>
    public struct TipClip : IEquatable<TipClip>
    {
        // Two reads of the IDENTICAL GUIClip context taken microseconds apart can differ in the last
        // bit or two of Unclip's matrix-transformed float math, worse under a non-identity GUI matrix;
        // exact comparison then treats one context as two and the tip never resolves. A genuinely
        // different clip differs by at least a real pixel, so this tolerance cannot reintroduce the
        // cross-context collisions QueryAtScreen exists to prevent.
        private const float Epsilon = 0.01f;

        public readonly TipRect Rect;
        public readonly int Depth;

        public TipClip(TipRect rect, int depth)
        {
            Rect = rect;
            Depth = depth;
        }

        public bool Equals(TipClip other)
        {
            return ApproximatelyEqual(Rect.X, other.Rect.X) && ApproximatelyEqual(Rect.Y, other.Rect.Y)
                && ApproximatelyEqual(Rect.Width, other.Rect.Width) && ApproximatelyEqual(Rect.Height, other.Rect.Height)
                && Depth == other.Depth;
        }

        private static bool ApproximatelyEqual(float a, float b)
        {
            float diff = a - b;
            return diff > -Epsilon && diff < Epsilon;
        }

        public override bool Equals(object obj)
        {
            return obj is TipClip other && Equals(other);
        }

        /// <summary>Deliberately NOT epsilon-aware, so two values <see cref="Equals"/> accepts can hash
        /// differently. Safe only because TipClip is never a Dictionary/HashSet key: it is compared
        /// solely through <see cref="IEquatable{T}"/> in TipIndex's linear scan.</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Depth;
                hash = (hash * 397) ^ Rect.X.GetHashCode();
                hash = (hash * 397) ^ Rect.Y.GetHashCode();
                hash = (hash * 397) ^ Rect.Width.GetHashCode();
                hash = (hash * 397) ^ Rect.Height.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// One GUI pass worth of captured tooltip registrations, queryable by element rectangle.
    /// Mirrors vanilla TooltipHandler semantics: a region can stack several tips, highest priority
    /// renders first, and re-registering a uniqueId within a pass replaces the earlier entry. Text
    /// resolves lazily because vanilla textGetter delegates can be expensive and registrations
    /// arrive every repaint.
    ///
    /// Each entry carries both a local rect (<see cref="QueryAt"/> compares here) and a screen-space
    /// rect (<see cref="QueryAtScreen"/>): local rects restart at (0,0) inside unrelated GUI groups,
    /// so two such widgets would otherwise steal each other's tooltip.
    ///
    /// <see cref="QueryAtScreen"/> also requires <see cref="TipClip"/> equality, not just screen-rect
    /// overlap. <c>Unclip</c> does not clamp to the active clip region, so a row scrolled below a
    /// ScrollView's fold still unclips to a real offscreen rect that can numerically collide with an
    /// unrelated widget. Draws in the same context have consistent screen rects by construction;
    /// draws in different contexts never match.
    ///
    /// Pure: no Unity/Verse types, links into the test project. The screen rect and clip are passed
    /// in by the caller rather than computed here.
    /// </summary>
    public sealed class TipIndex
    {
        private struct Entry
        {
            public TipRect Rect;
            public TipRect ScreenRect;
            public TipClip Clip;
            public int UniqueId;
            public int Priority;
            public Func<string> Resolve;
        }

        private readonly List<Entry> entries = new List<Entry>();

        public int Count
        {
            get { return entries.Count; }
        }

        public void Clear()
        {
            entries.Clear();
        }

        /// <summary>Records one tip registration; a later registration with the same uniqueId replaces
        /// the earlier one, as vanilla's own uniqueId-keyed dictionary does. Null resolvers are
        /// ignored.</summary>
        public void Add(TipRect rect, TipRect screenRect, TipClip clip, int uniqueId, int priority, Func<string> resolve)
        {
            if (resolve == null)
            {
                return;
            }
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].UniqueId == uniqueId)
                {
                    entries[i] = new Entry { Rect = rect, ScreenRect = screenRect, Clip = clip, UniqueId = uniqueId, Priority = priority, Resolve = resolve };
                    return;
                }
            }
            entries.Add(new Entry { Rect = rect, ScreenRect = screenRect, Clip = clip, UniqueId = uniqueId, Priority = priority, Resolve = resolve });
        }

        /// <summary>Resolves every tip whose LOCAL rect overlaps <paramref name="elementRect"/>, highest
        /// priority first with ties in registration order, joined with ". ". Null when nothing overlaps
        /// or everything resolves empty.</summary>
        public string QueryAt(TipRect elementRect)
        {
            return Resolve(elementRect, useScreenRect: false, clipFilter: null);
        }

        /// <summary>As <see cref="QueryAt"/>, but matched against each entry's SCREEN rect and requiring
        /// its <see cref="TipClip"/> to equal <paramref name="clip"/>, which is what prevents
        /// cross-context tooltip theft.</summary>
        public string QueryAtScreen(TipRect elementScreenRect, TipClip clip)
        {
            return Resolve(elementScreenRect, useScreenRect: true, clipFilter: clip);
        }

        /// <summary>
        /// Vanilla's float menu stacks rows with a deliberate one-pixel overlap (Verse/FloatMenu.cs:282),
        /// so a row's tip clips its neighbours' edges. A neighbour's tip is never this element's, even
        /// when it is the only hit, as on a row that registers no tip of its own.
        /// </summary>
        private const float EdgeBleedThickness = 2f;

        private static bool IsEdgeBleed(TipRect a, TipRect b)
        {
            float overlapWidth = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
            float overlapHeight = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
            return overlapWidth < EdgeBleedThickness || overlapHeight < EdgeBleedThickness;
        }

        private string Resolve(TipRect elementRect, bool useScreenRect, TipClip? clipFilter)
        {
            List<Entry> hits = null;
            for (int i = 0; i < entries.Count; i++)
            {
                if (clipFilter.HasValue && !entries[i].Clip.Equals(clipFilter.Value))
                {
                    continue;
                }
                TipRect candidate = useScreenRect ? entries[i].ScreenRect : entries[i].Rect;
                if (candidate.Overlaps(elementRect) && !IsEdgeBleed(candidate, elementRect))
                {
                    if (hits == null)
                    {
                        hits = new List<Entry>();
                    }
                    hits.Add(entries[i]);
                }
            }
            if (hits == null)
            {
                return null;
            }

            if (hits.Count > 1)
            {
                // Wider neighbour overlaps (float-rounded columns): a tip meant for this element
                // contains its center, so narrowing to those only ever removes candidates.
                float centerX = elementRect.X + elementRect.Width / 2f;
                float centerY = elementRect.Y + elementRect.Height / 2f;
                List<Entry> centered = null;
                for (int i = 0; i < hits.Count; i++)
                {
                    TipRect r = useScreenRect ? hits[i].ScreenRect : hits[i].Rect;
                    if (centerX >= r.X && centerX <= r.X + r.Width && centerY >= r.Y && centerY <= r.Y + r.Height)
                    {
                        (centered = centered ?? new List<Entry>()).Add(hits[i]);
                    }
                }
                if (centered != null && centered.Count > 0)
                {
                    hits = centered;
                }
            }

            // Insertion sort: stable, and hit lists are tiny.
            for (int i = 1; i < hits.Count; i++)
            {
                Entry current = hits[i];
                int j = i - 1;
                while (j >= 0 && hits[j].Priority < current.Priority)
                {
                    hits[j + 1] = hits[j];
                    j--;
                }
                hits[j + 1] = current;
            }

            string joined = null;
            for (int i = 0; i < hits.Count; i++)
            {
                string text;
                try
                {
                    text = hits[i].Resolve();
                }
                catch (Exception)
                {
                    // A textGetter assuming hover-time state may throw when resolved without a mouse;
                    // skip that tip rather than losing the whole announcement.
                    continue;
                }
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }
                joined = joined == null ? text : joined + ". " + text;
            }
            return joined;
        }
    }
}
