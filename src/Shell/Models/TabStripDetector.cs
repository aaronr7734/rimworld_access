using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>What a row is, for the purposes of recognising a tab strip — see <see cref="TabStripDetector"/>.</summary>
    public enum TabStripRowKind
    {
        /// <summary>Window furniture, not page content; skipped outright.</summary>
        Chrome,

        /// <summary>Text with nothing to operate; a caption above a strip does not end the search.</summary>
        Passive,

        /// <summary>A plain push button — the only primitive a hand-rolled tab can be built from.</summary>
        Button,

        /// <summary>Any other operable control: the page's real content has begun, so a strip past here is a toolbar.</summary>
        Content,
    }

    /// <summary>One row offered to <see cref="TabStripDetector"/>, in draw order, described without any GUI types.</summary>
    public struct TabStripRow
    {
        public TabStripRowKind Kind;

        /// <summary>
        /// Opaque identity of the clipping context the row was drawn under; only equality is used.
        /// </summary>
        public int ClipId;

        public float X;
        public float Y;
        public float Width;
        public float Height;
    }

    /// <summary>Whether a recognised strip reads left-to-right or top-to-bottom.</summary>
    public enum TabStripOrientation
    {
        Horizontal,
        Vertical,
    }

    /// <summary>A strip <see cref="TabStripDetector.TryFindLeadingStrip"/> recognised.</summary>
    public struct TabStripResult
    {
        /// <summary>Indices into the caller's row list, sorted in VISUAL order (by X for horizontal, by Y for vertical).</summary>
        public List<int> Members;

        public TabStripOrientation Orientation;

        /// <summary>
        /// True for tier 1, a horizontal strip tiled edge to edge in one clip, whose shape alone
        /// justifies promotion. False means the shape is only plausible (a clustered bar or a
        /// vertical column, both of which an ordinary button menu also produces) and the caller must
        /// get an independent selection verdict from
        /// <see cref="TabStripDetector.IndexOfDistinctTint"/> first.
        /// </summary>
        public bool ShapeAloneSuffices;
    }

    /// <summary>
    /// Recognises a hand-rolled tab strip — a row or column of plain buttons a caller drew to switch
    /// pages instead of using the engine's tab widget — from geometry alone, so any caller drawing
    /// that shape gains the tab grammar without the reader knowing anything about it.
    ///
    /// The shape is deliberately narrow, because a false positive moves real actions out of the arrow
    /// order into the tab order. The run must LEAD its surface: chrome and captions may precede it,
    /// but not one operable control, since a strip halfway down a page is a toolbar. Its buttons must
    /// be CONSECUTIVE in draw order (chrome may still interleave, being never a member) and must TILE
    /// into one band or column — same perpendicular position and size, and all the same size along
    /// the strip's axis — the layout that follows from dividing a length by a tab count and that
    /// independent buttons have no reason to produce. At least <see cref="MinimumTabs"/> are needed,
    /// which is what keeps an accept/cancel or previous/next pair from ever qualifying.
    ///
    /// Not every real strip is one tight horizontal band: some are drawn as several clusters on one
    /// bar, and some are vertical columns. Both are recognised but neither is trusted on shape alone
    /// (see <see cref="TabStripResult.ShapeAloneSuffices"/>), because those shapes are also what
    /// ordinary surfaces look like.
    ///
    /// One rule reads the rest of the surface: there must be a PAGE BEYOND the strip, a row below a
    /// horizontal bar or beside a vertical column, because a strip exists to switch the content
    /// beyond it. The trusted tier-1 shape is otherwise indistinguishable from a dialog's bottom
    /// action row tiled across its width, and a bottom action row is the one thing that can never
    /// have content drawn beneath it.
    ///
    /// A strip that fails every rule is left alone, its buttons still ordinary navigable buttons, so
    /// the failure mode of the whole recognition is the status quo.
    /// </summary>
    public static class TabStripDetector
    {
        /// <summary>Fewest tiled buttons that can be a tab strip: an equal-size accept/cancel pair is not a tab bar.</summary>
        public const int MinimumTabs = 3;

        /// <summary>Pixel slack for "same" position and size. Layout arithmetic that divides a length into equal parts leaves sub-pixel residue.</summary>
        private const float SizeEpsilon = 1.5f;

        /// <summary>
        /// Largest seam allowed between one tab and the next before it counts as a real gap rather
        /// than layout rounding. Real tab bars seam their buttons up to 6px apart while still reading
        /// as tightly tiled to a sighted player.
        /// </summary>
        private const float MaximumSeam = 6f;

        /// <summary>Two tints count as different once any channel's absolute delta passes this — see <see cref="IndexOfDistinctTint"/>.</summary>
        private const float TintEpsilon = 0.04f;

        /// <summary>Finds the leading tab strip; false with a default result when the rows do not form one.</summary>
        public static bool TryFindLeadingStrip(IReadOnlyList<TabStripRow> rows, out TabStripResult result)
        {
            result = default;
            if (rows == null)
            {
                return false;
            }

            int first = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                TabStripRowKind kind = rows[i].Kind;
                if (kind == TabStripRowKind.Chrome || kind == TabStripRowKind.Passive)
                {
                    continue;
                }
                if (kind == TabStripRowKind.Content)
                {
                    return false;
                }
                first = i;
                break;
            }
            if (first < 0)
            {
                return false;
            }

            TabStripRow anchor = rows[first];

            // The strip accretes member by member in draw order, and the FIRST button that does not
            // fit ENDS the strip rather than failing it — a caller may draw an unrelated full-width
            // button in the same band right after its tabs, and that is simply where the page's
            // content begins.
            //
            // "Fits" is the whole shape at once: same band position and size perpendicular to the
            // strip, same size along it. The equal-size rule is the main false-positive shield; no
            // real strip sizes its tabs to their text. Orientation is whatever the SECOND member
            // agrees with the anchor on, horizontal preferred on the degenerate tie. Members are
            // collected in draw order and sorted visually below, so a caller drawing its clusters out
            // of visual order still reads left to right. Chrome may interleave, and so may a Passive
            // row that FITS the shape — that is a DISABLED tab rendered as a read-only line, which
            // stays navigable while its enabled siblings promote around it. A Passive row of any
            // other shape, or a Content row, ends the run: a caption BETWEEN tabs is not a caption
            // ABOVE them. Clip context is a TIER gate, not a membership rule — one real bar can be
            // drawn as three separately-clipped clusters, and the rects compared here are globally
            // positioned so geometry survives that, but two unrelated side-by-side panels look the
            // same, so crossing a clip forfeits tier 1 and makes the selection verdict mandatory.
            bool crossClip = false;
            bool orientationKnown = false;
            TabStripOrientation orientation = TabStripOrientation.Horizontal;
            List<int> candidate = new List<int> { first };
            for (int i = first + 1; i < rows.Count; i++)
            {
                if (rows[i].Kind == TabStripRowKind.Chrome)
                {
                    continue;
                }
                TabStripRow row = rows[i];
                bool fitsHorizontal = Near(row.Y, anchor.Y) && Near(row.Height, anchor.Height)
                    && Near(row.Width, anchor.Width);
                bool fitsVertical = Near(row.X, anchor.X) && Near(row.Width, anchor.Width)
                    && Near(row.Height, anchor.Height);
                if (rows[i].Kind == TabStripRowKind.Passive)
                {
                    if (fitsHorizontal || fitsVertical)
                    {
                        continue;
                    }
                    break;
                }
                if (rows[i].Kind != TabStripRowKind.Button)
                {
                    break;
                }
                if (!orientationKnown)
                {
                    if (fitsHorizontal)
                    {
                        orientation = TabStripOrientation.Horizontal;
                        orientationKnown = true;
                    }
                    else if (fitsVertical)
                    {
                        orientation = TabStripOrientation.Vertical;
                        orientationKnown = true;
                    }
                    else
                    {
                        break;
                    }
                }
                else if (orientation == TabStripOrientation.Horizontal ? !fitsHorizontal : !fitsVertical)
                {
                    break;
                }
                if (row.ClipId != anchor.ClipId)
                {
                    crossClip = true;
                }
                candidate.Add(i);
            }

            if (candidate.Count < MinimumTabs)
            {
                return false;
            }

            if (orientation == TabStripOrientation.Horizontal)
            {
                candidate.Sort((a, b) => rows[a].X.CompareTo(rows[b].X));
            }
            else
            {
                candidate.Sort((a, b) => rows[a].Y.CompareTo(rows[b].Y));
            }

            float[] seams = new float[candidate.Count - 1];
            for (int i = 1; i < candidate.Count; i++)
            {
                TabStripRow prev = rows[candidate[i - 1]];
                TabStripRow next = rows[candidate[i]];
                seams[i - 1] = orientation == TabStripOrientation.Horizontal
                    ? next.X - (prev.X + prev.Width)
                    : next.Y - (prev.Y + prev.Height);
                if (seams[i - 1] < -MaximumSeam)
                {
                    // An overlap is never layout rounding: something else produced these rects.
                    return false;
                }
            }

            // The page-beyond rule: some non-member, non-chrome row must sit past the strip along its
            // perpendicular axis, or there is nothing for these tabs to switch. Rows are compared
            // against the anchor's band, the same reference every membership test used.
            bool hasPageBeyond = false;
            for (int i = 0; i < rows.Count && !hasPageBeyond; i++)
            {
                if (rows[i].Kind == TabStripRowKind.Chrome || candidate.Contains(i))
                {
                    continue;
                }
                TabStripRow row = rows[i];
                if (orientation == TabStripOrientation.Horizontal)
                {
                    hasPageBeyond = row.Y >= anchor.Y + anchor.Height - SizeEpsilon;
                }
                else
                {
                    hasPageBeyond = row.X >= anchor.X + anchor.Width - SizeEpsilon
                        || row.X + row.Width <= anchor.X + SizeEpsilon;
                }
            }
            if (!hasPageBeyond)
            {
                return false;
            }

            // Tier 1: a horizontal strip tiled edge to edge in ONE clip, no gap wider than a hairline.
            bool tier1 = orientation == TabStripOrientation.Horizontal && !crossClip;
            if (tier1)
            {
                for (int i = 0; i < seams.Length; i++)
                {
                    if (seams[i] > MaximumSeam)
                    {
                        tier1 = false;
                        break;
                    }
                }
            }

            // Tier 2 — a horizontal bar with gaps of any width, or a vertical column — deliberately
            // constrains seam widths no further: real cluster gaps land wherever the window width
            // puts them, so any threshold rejects real strips. The caller's selection-verdict gate is
            // the false-positive shield for these shapes.

            result = new TabStripResult
            {
                Members = candidate,
                Orientation = orientation,
                // Vertical is never tier 1: a stacked column of same-size buttons is also what a
                // settings menu looks like, so only a live selection verdict can promote it.
                ShapeAloneSuffices = tier1,
            };
            return true;
        }

        /// <summary>Plain color for detector input; keeps the detector free of UnityEngine types so it stays unit-testable.</summary>
        public struct TabTint
        {
            public float R, G, B, A;
        }

        /// <summary>
        /// Which member of a strip its caller drew as current, given each member's tint in strip
        /// order, or -1 when the tints carry no verdict. A caller marks the current tab by tinting it
        /// differently, but the DIRECTION varies — brighter, darker, or a pure hue shift — so a
        /// brightest-wins rule reads real strips backwards; this looks for the one member that
        /// differs from an otherwise-uniform rest, in any direction. Needs at least three tints,
        /// since with two "the odd one out" has no meaning.
        /// </summary>
        public static int IndexOfDistinctTint(IReadOnlyList<TabTint> tints)
        {
            if (tints == null || tints.Count < 3)
            {
                return -1;
            }

            int n = tints.Count;
            bool[,] differs = new bool[n, n];
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    bool d = Differs(tints[i], tints[j]);
                    differs[i, j] = d;
                    differs[j, i] = d;
                }
            }

            for (int c = 0; c < n; c++)
            {
                bool differsFromAllOthers = true;
                bool othersAllUniform = true;
                for (int i = 0; i < n && othersAllUniform; i++)
                {
                    if (i == c)
                    {
                        continue;
                    }
                    if (!differs[c, i])
                    {
                        differsFromAllOthers = false;
                    }
                    for (int j = i + 1; j < n; j++)
                    {
                        if (j == c)
                        {
                            continue;
                        }
                        if (differs[i, j])
                        {
                            othersAllUniform = false;
                            break;
                        }
                    }
                }
                if (differsFromAllOthers && othersAllUniform)
                {
                    return c;
                }
            }
            // Either every member matches or more than one deviates; neither is a verdict.
            return -1;
        }

        /// <summary>A run's current option: its one member drawn without background, else the distinct tint; a pair reads its tinted member against white.</summary>
        public static int IndexOfMarkedOption(IReadOnlyList<TabTint> tints, IReadOnlyList<bool> noBackground)
        {
            if (tints == null || tints.Count < 2)
            {
                return -1;
            }
            int bare = -1;
            for (int i = 0; noBackground != null && i < noBackground.Count && i < tints.Count; i++)
            {
                if (!noBackground[i])
                {
                    continue;
                }
                if (bare >= 0)
                {
                    bare = -1;
                    break;
                }
                bare = i;
            }
            if (bare >= 0)
            {
                return bare;
            }
            if (tints.Count >= 3)
            {
                return IndexOfDistinctTint(tints);
            }
            bool firstWhite = IsWhite(tints[0]);
            bool secondWhite = IsWhite(tints[1]);
            return firstWhite == secondWhite ? -1 : (firstWhite ? 1 : 0);
        }

        private static bool IsWhite(TabTint tint)
        {
            TabTint white = new TabTint { R = 1f, G = 1f, B = 1f, A = 1f };
            return !Differs(tint, white);
        }

        private static bool Differs(TabTint a, TabTint b)
        {
            return Diff(a.R, b.R) || Diff(a.G, b.G) || Diff(a.B, b.B) || Diff(a.A, b.A);
        }

        private static bool Diff(float a, float b)
        {
            float delta = a - b;
            return delta > TintEpsilon || delta < -TintEpsilon;
        }

        private static bool Near(float a, float b)
        {
            float delta = a - b;
            return delta <= SizeEpsilon && delta >= -SizeEpsilon;
        }
    }
}
