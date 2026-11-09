using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    public sealed partial class GenericWindowScope
    {
        /// <summary>
        /// Relabels a row of plain buttons that the layout geometry says is a hand-rolled tab strip,
        /// so the rest of the tab grammar (<see cref="HasTabBar"/>, <see cref="CycleTab"/>, the
        /// strip's exclusion from the arrow order) applies to it unchanged.
        /// <see cref="TabStripDetector"/> owns the geometry judgement.
        ///
        /// Relabelling never writes selection state: activation still rides the button's own click,
        /// because <see cref="PostActivate"/> posts against the CAPTURED row, which stays a
        /// <see cref="WidgetKind.Button"/>.
        ///
        /// Real tab rows are invisible to the search — neither anchor material nor strip breaker —
        /// so a window drawing a vanilla strip inside its page keeps its hand-rolled strip. Their
        /// presence does forfeit shape-alone trust, since that is exactly where a tight button row
        /// is most likely a toolbar; promotion beside real tabs needs the tint verdict.
        /// </summary>
        private void PromoteHandRolledTabStrip()
        {
            bool besideRealTabs = false;
            for (int i = 0; i < presentationRows.Count; i++)
            {
                if (presentationRows[i].Kind == WidgetKind.Tab)
                {
                    besideRealTabs = true;
                    break;
                }
            }

            if (presentationRows.Count < TabStripDetector.MinimumTabs)
            {
                return;
            }

            // The detector's pixel thresholds are calibrated in the logical units mod layout
            // arithmetic uses, but ScreenRect is post-UI-scale, so seams and size residue grow past
            // the thresholds at scale > 1. Divide the probe back to logical units; uniform scaling
            // cannot change which rows band, tile or gap.
            float uiScale = Prefs.UIScale > 0f ? Prefs.UIScale : 1f;
            IReadOnlyList<OptionBackgroundMarker> markers = WidgetCapture.OptionBackgroundMarkers;
            List<TabStripRow> probe = new List<TabStripRow>();
            // Probe index -> presentationRows index; real tab rows are left out of the probe.
            List<int> probeRows = new List<int>();
            for (int i = 0; i < presentationRows.Count; i++)
            {
                PresentationRow row = presentationRows[i];
                if (row.Kind == WidgetKind.Tab)
                {
                    continue;
                }
                TabStripRowKind kind = ClassifyForTabStrip(row, markers);
                probeRows.Add(i);
                probe.Add(new TabStripRow
                {
                    Kind = kind,
                    // Only equality is read, and a collision would still have to survive the
                    // band, size and seam tests to matter.
                    ClipId = row.Clip.GetHashCode(),
                    X = row.ScreenRect.x / uiScale,
                    Y = row.ScreenRect.y / uiScale,
                    Width = row.ScreenRect.width / uiScale,
                    Height = row.ScreenRect.height / uiScale,
                });
                if (kind == TabStripRowKind.Content)
                {
                    // The detector reads nothing past the first operable non-button, so stop
                    // rather than describe a long settings page every pass.
                    break;
                }
            }

            TabStripResult result;
            if (!TabStripDetector.TryFindLeadingStrip(probe, out result))
            {
                return;
            }

            List<TabStripDetector.TabTint> tints = new List<TabStripDetector.TabTint>(result.Members.Count);
            bool anyMarkerAdmitted = false;
            for (int i = 0; i < result.Members.Count; i++)
            {
                CapturedWidget source = presentationRows[probeRows[result.Members[i]]].Source;
                Color tint = source != null ? source.RecordColor : Color.white;
                tints.Add(new TabStripDetector.TabTint { R = tint.r, G = tint.g, B = tint.b, A = tint.a });
                if (source != null && source.Kind == WidgetKind.InvisibleButton)
                {
                    anyMarkerAdmitted = true;
                }
            }
            int selected = TabStripDetector.IndexOfDistinctTint(tints);
            if (selected < 0)
            {
                selected = OptionBackgroundSelectionVerdict(result, probeRows, markers);
            }

            // Tier 1 promotes on shape alone; selected may stay -1 when the caller marks its
            // current tab some other way. Tier 2 shapes (clustered bar, vertical column) also
            // describe ordinary button rows, so they promote only on a selection verdict — a
            // distinct tint, or the option-background markers' own selected flag. A strip any of
            // whose members entered only through an option-background marker always needs the
            // verdict: invisible buttons over highlight boxes are also what selection LISTS look
            // like, and a list with no current selection must stay ordinary buttons.
            if ((besideRealTabs || !result.ShapeAloneSuffices || anyMarkerAdmitted) && selected < 0)
            {
                return;
            }

            for (int i = 0; i < result.Members.Count; i++)
            {
                PresentationRow row = presentationRows[probeRows[result.Members[i]]];
                row.Kind = WidgetKind.Tab;
                // Null, not false, without a tint verdict, so EffectiveSelected falls through to
                // the captured row's own flag rather than asserting a selection.
                row.Selected = selected < 0 ? (bool?)null : (i == selected);
            }
        }

        /// <summary>A registered window's current-page button and the consecutive plain buttons on its clip and line become a tab strip (<see cref="WindowPageButtonRegistry"/>).</summary>
        private void PromoteRegisteredPageButtons()
        {
            string current = WindowPageButtonRegistry.CurrentCaption(window);
            if (string.IsNullOrEmpty(current))
            {
                return;
            }
            IReadOnlyList<OptionBackgroundMarker> markers = WidgetCapture.OptionBackgroundMarkers;
            int selected = presentationRows.FindIndex(r => ClassifyForTabStrip(r, markers) == TabStripRowKind.Button
                && r.Source.Kind == WidgetKind.Button && string.Equals(r.Source.Label, current, StringComparison.Ordinal));
            if (selected < 0)
            {
                return;
            }
            PresentationRow anchor = presentationRows[selected];
            Func<PresentationRow, bool> isMember = r => ClassifyForTabStrip(r, markers) == TabStripRowKind.Button
                && r.Source.Kind == WidgetKind.Button && r.Clip.Equals(anchor.Clip) && SharesBand(r.ScreenRect, anchor.ScreenRect);
            int first = selected;
            while (first > 0 && isMember(presentationRows[first - 1]))
            {
                first--;
            }
            int last = selected;
            while (last + 1 < presentationRows.Count && isMember(presentationRows[last + 1]))
            {
                last++;
            }
            if (first == last)
            {
                return;
            }
            for (int i = first; i <= last; i++)
            {
                presentationRows[i].Kind = WidgetKind.Tab;
                presentationRows[i].Selected = i == selected;
            }
        }

        /// <summary>A uniform, edge-tiled button run with exactly one tinted member (the
        /// hand-rolled enum-selector idiom) becomes a radio group, tinted reading selected;
        /// runs after tab promotion, activation still posts the button's own click.</summary>
        private void MarkTintSelectedButtonClusters()
        {
            int n = presentationRows.Count;
            int i = 0;
            while (i < n)
            {
                if (!IsTintClusterCandidate(presentationRows[i]))
                {
                    i++;
                    continue;
                }
                int end = i + 1;
                while (end < n && IsTintClusterCandidate(presentationRows[end])
                    && TilesRightOf(presentationRows[end - 1], presentationRows[end]))
                {
                    end++;
                }
                int count = end - i;
                if (count >= 3)
                {
                    var tints = new List<TabStripDetector.TabTint>(count);
                    for (int m = i; m < end; m++)
                    {
                        Color tint = presentationRows[m].Source.RecordColor;
                        tints.Add(new TabStripDetector.TabTint { R = tint.r, G = tint.g, B = tint.b, A = tint.a });
                    }
                    int selected = TabStripDetector.IndexOfDistinctTint(tints);
                    if (selected >= 0)
                    {
                        for (int m = i; m < end; m++)
                        {
                            presentationRows[m].Kind = WidgetKind.RadioButton;
                            presentationRows[m].Selected = m - i == selected;
                        }
                    }
                }
                i = end;
            }
        }

        private static bool IsTintClusterCandidate(PresentationRow row)
        {
            return row.Kind == WidgetKind.Button
                && !row.ReadOnlyCheckbox
                && row.Source != null
                && row.Source.Kind == WidgetKind.Button
                && !row.Source.CloseX
                && !row.Source.DropdownOpener
                && !row.Source.StepperButton
                && row.Source.Composite == CompositeMember.None
                && row.RingCaptureIndex == row.ActivateCaptureIndex
                && !string.IsNullOrEmpty(row.Label);
        }

        /// <summary>Next tile of the run: same clip, band and size, starting at a's right edge.
        /// The seam cap keeps side-by-side columns' selectors from merging.</summary>
        private static bool TilesRightOf(PresentationRow a, PresentationRow b)
        {
            if (a.Source == null || b.Source == null || !a.Clip.Equals(b.Clip))
            {
                return false;
            }
            Rect ra = a.Source.Rect;
            Rect rb = b.Source.Rect;
            return Mathf.Abs(ra.y - rb.y) <= 1f
                && Mathf.Abs(ra.height - rb.height) <= 1f
                && Mathf.Abs(ra.width - rb.width) <= 1f
                && rb.xMin >= ra.xMax - 1f
                && rb.xMin <= ra.xMax + 4f;
        }

        /// <summary>
        /// How one presentation row reads to the tab-strip search. Only a plain push button can be
        /// a hand-rolled tab: dropdown openers, composite halves and read-only promotions already
        /// mean something else, and calling one a tab would mis-speak it and drop it from the
        /// arrow order.
        /// </summary>
        private static TabStripRowKind ClassifyForTabStrip(PresentationRow row, IReadOnlyList<OptionBackgroundMarker> markers)
        {
            if (row.Source != null && row.Source.CloseX)
            {
                return TabStripRowKind.Chrome;
            }
            if (row.Kind == WidgetKind.Label || row.Kind == WidgetKind.FillableBar)
            {
                return TabStripRowKind.Passive;
            }
            if (row.Kind == WidgetKind.Button && !row.ReadOnlyCheckbox
                && row.Source != null && row.Source.Kind == WidgetKind.Button
                && !row.Source.DropdownOpener
                && row.Source.Composite == CompositeMember.None
                && row.RingCaptureIndex == row.ActivateCaptureIndex)
            {
                return TabStripRowKind.Button;
            }
            // A fused Label + ButtonInvisible pair riding a Widgets.DrawOptionBackground box is
            // the other hand-rolled tab idiom (icon + label over an invisible hotspot). Admitted
            // only WITH the marker: a bare invisible-button run is any list at all. The row's
            // ScreenRect is already the hotspot — Phase 2 emission puts the control's rect there —
            // so the geometry probe sees the tiling, not the narrower label.
            if (row.Kind == WidgetKind.Button && !row.ReadOnlyCheckbox
                && row.Source != null && row.Source.Kind == WidgetKind.InvisibleButton
                && row.Source.Composite == CompositeMember.None
                && FindOptionMarker(row, markers) >= 0)
            {
                return TabStripRowKind.Button;
            }
            return TabStripRowKind.Content;
        }

        /// <summary>
        /// Index of the option-background marker under <paramref name="row"/>'s hotspot, or -1:
        /// same clip, and the intersection covers most of the hotspot — the box and the invisible
        /// button are two draws of one rect expression, give or take an inset.
        /// </summary>
        private static int FindOptionMarker(PresentationRow row, IReadOnlyList<OptionBackgroundMarker> markers)
        {
            Rect r = row.ScreenRect;
            float area = r.width * r.height;
            if (area <= 0f)
            {
                return -1;
            }
            for (int i = 0; i < markers.Count; i++)
            {
                OptionBackgroundMarker m = markers[i];
                if (!m.Clip.Equals(row.Clip))
                {
                    continue;
                }
                float ix = Mathf.Max(0f, Mathf.Min(m.ScreenRect.xMax, r.xMax) - Mathf.Max(m.ScreenRect.xMin, r.xMin));
                float iy = Mathf.Max(0f, Mathf.Min(m.ScreenRect.yMax, r.yMax) - Mathf.Max(m.ScreenRect.yMin, r.yMin));
                if ((ix * iy) / area >= 0.8f)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Selection verdict from option-background markers: every strip member must sit on a
        /// marker and exactly one marker must be selected — the member index of that one, else -1.
        /// Anything looser (some members unmarked, zero or several selected) is no verdict, not a
        /// guess.
        /// </summary>
        private int OptionBackgroundSelectionVerdict(TabStripResult result, List<int> probeRows, IReadOnlyList<OptionBackgroundMarker> markers)
        {
            int selected = -1;
            for (int i = 0; i < result.Members.Count; i++)
            {
                int markerIndex = FindOptionMarker(presentationRows[probeRows[result.Members[i]]], markers);
                if (markerIndex < 0)
                {
                    return -1;
                }
                if (markers[markerIndex].Selected)
                {
                    if (selected >= 0)
                    {
                        return -1;
                    }
                    selected = i;
                }
            }
            return selected;
        }

        /// <summary>
        /// Once a strip of two or more real <see cref="WidgetKind.Tab"/> rows exists, finds rows
        /// that read as greyed-out tabs a mod drew as plain textured Labels;
        /// <see cref="DisabledTabStripAbsorption"/> owns the geometry test. Only
        /// <see cref="WidgetKind.Label"/> rows are eligible — a same-sized button or checkbox in
        /// the strip's clip is a real control and must never be swallowed as "disabled". Matches
        /// are appended to <see cref="tabRows"/> before the caller's visual-order sort so they land
        /// among the real tabs rather than at the end.
        /// </summary>
        private void AbsorbDisabledStripMembers()
        {
            if (tabRows.Count < 2)
            {
                return;
            }
            var clipIds = new Dictionary<GuiSpace.ClipKey, int>();
            var stripMembers = new DisabledStripCandidate[tabRows.Count];
            for (int i = 0; i < tabRows.Count; i++)
            {
                stripMembers[i] = ToDisabledStripCandidate(tabRows[i], clipIds);
            }

            List<PresentationRow> candidateRows = new List<PresentationRow>();
            List<DisabledStripCandidate> candidates = new List<DisabledStripCandidate>();
            for (int i = 0; i < presentationRows.Count; i++)
            {
                PresentationRow row = presentationRows[i];
                if (row.Kind != WidgetKind.Label)
                {
                    continue;
                }
                candidateRows.Add(row);
                candidates.Add(ToDisabledStripCandidate(row, clipIds));
            }

            IReadOnlyList<bool> absorbed = DisabledTabStripAbsorption.FindAbsorbedRows(stripMembers, candidates);
            for (int i = 0; i < absorbed.Count; i++)
            {
                if (!absorbed[i])
                {
                    continue;
                }
                candidateRows[i].DisabledTabStop = true;
                tabRows.Add(candidateRows[i]);
            }
        }

        private static DisabledStripCandidate ToDisabledStripCandidate(PresentationRow row, Dictionary<GuiSpace.ClipKey, int> clipIds)
        {
            int clipId;
            if (!clipIds.TryGetValue(row.Clip, out clipId))
            {
                clipId = clipIds.Count;
                clipIds.Add(row.Clip, clipId);
            }
            return new DisabledStripCandidate
            {
                ClipId = clipId,
                X = row.ScreenRect.x,
                Y = row.ScreenRect.y,
                Width = row.ScreenRect.width,
                Height = row.ScreenRect.height,
            };
        }

        /// <summary>
        /// Drops a plain Label row that sits immediately beside an interactive row whose spoken
        /// name is the exact same text: the mod drew a caption the control already names itself.
        /// Exact equality and immediate adjacency only, so a repeated word elsewhere in the window
        /// survives. Runs last, after sorting, because adjacency here means what the arrow keys
        /// walk rather than draw order.
        /// </summary>
        private void RemoveAdjacentDuplicateLabels()
        {
            List<int> drop = null;
            for (int i = 0; i < presentationRows.Count; i++)
            {
                PresentationRow row = presentationRows[i];
                if (row.Kind != WidgetKind.Label || string.IsNullOrEmpty(row.Label))
                {
                    continue;
                }
                if (IsDuplicateOfAdjacentControl(row, i - 1) || IsDuplicateOfAdjacentControl(row, i + 1))
                {
                    (drop = drop ?? new List<int>()).Add(i);
                }
            }
            if (drop == null)
            {
                return;
            }
            for (int i = drop.Count - 1; i >= 0; i--)
            {
                presentationRows.RemoveAt(drop[i]);
            }
        }

        private bool IsDuplicateOfAdjacentControl(PresentationRow label, int neighborIndex)
        {
            if (neighborIndex < 0 || neighborIndex >= presentationRows.Count)
            {
                return false;
            }
            PresentationRow neighbor = presentationRows[neighborIndex];
            return RimWorldAccess.CaptureDescriptor.IsInteractiveKind(neighbor.Kind)
                && !string.IsNullOrEmpty(neighbor.Label)
                && string.Equals(neighbor.Label, label.Label, StringComparison.Ordinal);
        }

        /// <summary>True when the text contains at least one letter or digit — the minimum for a heading to actually name a section.</summary>
        private static bool HeadingTextCanName(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsLetterOrDigit(text[i]))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Which captured rows the window drew as chrome rather than content.
        /// <c>DoWindowContents</c> runs inside <c>windowDrawing.BeginGroup</c>, so content sits at
        /// least one clip deeper than the pass root while the close-X, the bottom Close button and
        /// CommonSearchWidget draw at the root level. Classifies nothing unless some row really is
        /// deeper: clip bindings can degrade to one shared key, and a window whose IWindowDrawing
        /// opens no group would otherwise read as all-chrome.
        /// </summary>
        private bool[] ResolveWindowChromeRows()
        {
            int n = captureRows.Count;
            var chrome = new bool[n];
            bool anyDeeper = false;
            for (int i = 0; i < n; i++)
            {
                if (captureRows[i].Clip.Depth > passRootClipDepth)
                {
                    anyDeeper = true;
                    break;
                }
            }
            if (!anyDeeper)
            {
                return chrome;
            }
            for (int i = 0; i < n; i++)
            {
                chrome[i] = captureRows[i].Clip.Depth <= passRootClipDepth;
            }
            return chrome;
        }

        /// <summary>
        /// Whether some row other than the corner close-X offers the same close action: vanilla's
        /// doCloseButton, or a close-captioned button a window drew for itself. The caption is
        /// compared against the game's resolved string, never an English literal — it is the same
        /// string WidgetCapture names the X texture with.
        /// </summary>
        private bool HasNonChromeCloseTwin()
        {
            string closeCaption = "CloseButton".Translate();
            for (int i = 0; i < captureRows.Count; i++)
            {
                CapturedWidget row = captureRows[i];
                if (row.Kind == WidgetKind.Button && !row.CloseX && !row.Disabled
                    && row.Composite == CompositeMember.None
                    && string.Equals(row.Label, closeCaption, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Translates captured rows into <see cref="SectionOwnership"/>'s pure geometry and back into each row's owning heading label — the same shape as <see cref="ApplyVerticalSliderLabelInheritance"/>'s translation of <see cref="VerticalLabelInheritance"/>.</summary>
        private string[] ComputeSectionOwners(bool[] heading, bool[] windowChrome, Rect[] effectiveRect)
        {
            int n = effectiveRect.Length;
            var candidates = new SectionCandidate[n];
            for (int i = 0; i < n; i++)
            {
                Rect r = effectiveRect[i];
                candidates[i] = new SectionCandidate
                {
                    IsHeading = heading[i],
                    IsChrome = windowChrome[i],
                    XMin = r.xMin,
                    XMax = r.xMax,
                    YMin = r.yMin,
                };
            }
            IReadOnlyList<int> ownerIndex = SectionOwnership.ComputeOwners(candidates);
            var owners = new string[n];
            for (int i = 0; i < n; i++)
            {
                owners[i] = ownerIndex[i] < 0 ? null : captureRows[ownerIndex[i]].Label;
            }
            return owners;
        }

        /// <summary>
        /// A control drawn out of top-to-bottom visual order (Colony Manager
        /// Redux emits its threshold slider LAST though it sits second from
        /// the top; Livestock/Production draw a tab's body before the strip
        /// that labels it) must still be NAVIGATED in the order a sighted
        /// player perceives, not the order the mod happened to call Widgets
        /// in. Stable sort of the final <see cref="presentationRows"/> list by
        /// <see cref="PresentationRow.ScreenRect"/>: rows are clustered into
        /// horizontal bands (two rows whose y-ranges overlap by more than half
        /// the shorter one's height share a band — the same "sits in the same
        /// visual row" test a sighted player applies), bands are ordered
        /// top-to-bottom, and rows within a band are ordered left-to-right.
        /// Uses <see cref="Enumerable.OrderBy{TSource,TKey}"/>/ThenBy, which
        /// the framework guarantees stable, so two rows whose band and x both
        /// tie (including every row this method does not distinguish at all)
        /// keep their original draw-order relative to one another — no
        /// gratuitous reordering beyond what visual position actually
        /// demands. A row with no usable <see cref="Rect"/> (never drew, or a
        /// degenerate capture) borrows its immediately preceding row's
        /// effective rect rather than sorting on (0,0): it then shares that
        /// row's exact band-and-x key, and stability keeps it sitting right
        /// after that predecessor — the only sensible position for a row with
        /// no visual position of its own.
        ///
        /// Before the band/x key is applied, rows are
        /// ranked by the PANE (capture clip) they belong to — see
        /// <see cref="PaneOrder"/> — so a window built from several
        /// side-by-side scroll panes (Colony Manager Redux's job list /
        /// settings / allowed-animals columns) reads pane by pane rather than
        /// zipper-interleaved by raw screen position (the panes' rects
        /// overlap in X, which is exactly what made the old whole-window
        /// Y-then-X sort zigzag between them). The pane rank is the PRIMARY
        /// key and band/x stays secondary, so two rows in the same clip never
        /// reorder relative to each other — only rows in DIFFERENT clips can
        /// move relative to one another, and only to group by pane.
        /// </summary>
        private void SortPresentationRowsByVisualPosition()
        {
            int n = presentationRows.Count;
            if (n <= 1)
            {
                return;
            }

            Rect[] effectiveRect = new Rect[n];
            for (int i = 0; i < n; i++)
            {
                Rect r = presentationRows[i].ScreenRect;
                effectiveRect[i] = HasUsableRect(r) ? r : (i > 0 ? effectiveRect[i - 1] : r);
            }

            var clipIds = new Dictionary<GuiSpace.ClipKey, int>();
            var paneRows = new PaneOrderRow[n];
            for (int i = 0; i < n; i++)
            {
                GuiSpace.ClipKey clip = presentationRows[i].Clip;
                int clipId;
                if (!clipIds.TryGetValue(clip, out clipId))
                {
                    clipId = clipIds.Count;
                    clipIds.Add(clip, clipId);
                }
                CapturedWidget source = presentationRows[i].Source;
                paneRows[i] = new PaneOrderRow
                {
                    ClipId = clipId,
                    ContainerId = source != null && source.ContainerIndex >= 0 ? source.ContainerIndex : (int?)null,
                    X = effectiveRect[i].xMin,
                    XMax = effectiveRect[i].xMax,
                    Y = effectiveRect[i].yMin,
                    YMax = effectiveRect[i].yMax,
                };
            }
            IReadOnlyList<int> paneRank = PaneOrder.ComputePaneRanks(paneRows);

            // Listing column rank, secondary to pane: a multi-column listing (explicit NewColumn
            // or Listing's own overflow wrap) reads column by column, exactly the order a sighted
            // player scans it, instead of the band sort zigzagging across the columns. Rows are
            // assigned by where their ring capture index falls among the pass's recorded column
            // breaks; a window with no listing columns leaves every rank 0 and the sort unchanged.
            IReadOnlyList<int> columnBreaks = WidgetCapture.ListingColumnBreaks;
            int[] columnRank = new int[n];
            if (columnBreaks.Count > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    int anchor = presentationRows[i].RingCaptureIndex;
                    if (anchor < 0)
                    {
                        columnRank[i] = i > 0 ? columnRank[i - 1] : 0;
                        continue;
                    }
                    int rank = 0;
                    for (int b = 0; b < columnBreaks.Count && columnBreaks[b] <= anchor; b++)
                    {
                        rank++;
                    }
                    columnRank[i] = rank;
                }
            }

            // Band discovery: walk rows top-to-bottom (stable order-by-y, so
            // two rows starting at the exact same y keep their draw-order
            // relation here too) and start a new band whenever the next row
            // fails the overlap test against the PREVIOUS row visited — a
            // chained/transitive grouping, the standard shape for banding a
            // column of possibly-staggered rows. Deliberately still computed
            // across the WHOLE window rather than per-pane: pane rank already
            // separates rows from different clips before band/x is ever
            // consulted, so a cross-pane band tie here is harmless — it can
            // never surface as long as the pane ranks differ.
            int[] byTop = Enumerable.Range(0, n).OrderBy(i => effectiveRect[i].yMin).ToArray();
            int[] band = new int[n];
            int bandCount = 0;
            Rect previousRect = default(Rect);
            for (int k = 0; k < n; k++)
            {
                int i = byTop[k];
                Rect r = effectiveRect[i];
                if (k == 0 || !SharesBand(r, previousRect))
                {
                    bandCount++;
                }
                band[i] = bandCount - 1;
                previousRect = r;
            }

            List<PresentationRow> sorted = presentationRows
                .Select((row, i) => new { row, pane = paneRank[i], column = columnRank[i], band = band[i], x = effectiveRect[i].xMin })
                .OrderBy(e => e.pane)
                .ThenBy(e => e.column)
                .ThenBy(e => e.band)
                .ThenBy(e => e.x)
                .Select(e => e.row)
                .ToList();
            presentationRows.Clear();
            presentationRows.AddRange(sorted);
        }

        private static bool HasUsableRect(Rect r)
        {
            return r.width > 0f && r.height > 0f;
        }

        /// <summary>
        /// First bottom-bar row in <see cref="presentationRows"/>, or -1: the trailing run of
        /// Button rows sharing the bottommost visual band (at least two), plus the corner
        /// close-X relocated behind them. MoveItem and the horizontal claims read this for the
        /// bar's message-reader grammar (Left/Right walk it, Up leaves it, Down holds).
        /// </summary>
        private int buttonBarStart = -1;

        /// <summary>After the visual sort: the corner close-X moves to the very end (chrome is not the first thing a reader meets), then the bar is found.</summary>
        private void ShapeBottomButtonBar()
        {
            buttonBarStart = -1;
            int n = presentationRows.Count;
            if (n < 2)
            {
                return;
            }
            for (int i = 0; i < n - 1; i++)
            {
                PresentationRow row = presentationRows[i];
                if (row.Kind == WidgetKind.Button && row.Source != null && row.Source.CloseX)
                {
                    presentationRows.RemoveAt(i);
                    presentationRows.Add(row);
                    break;
                }
            }
            int start = n;
            int barButtons = 0;
            Rect anchor = default(Rect);
            bool anchorSet = false;
            for (int i = n - 1; i >= 0; i--)
            {
                PresentationRow row = presentationRows[i];
                // Checkboxes drawn on the strip ("Do not show this again") belong to it too.
                if (row.Kind != WidgetKind.Button && row.Kind != WidgetKind.Checkbox)
                {
                    break;
                }
                // Only real strip members anchor and test the band; the close-X joins regardless.
                if (row.Source == null || !row.Source.CloseX)
                {
                    Rect r = row.ScreenRect;
                    if (!anchorSet)
                    {
                        anchor = r;
                        anchorSet = true;
                    }
                    else if (!SharesBand(r, anchor))
                    {
                        break;
                    }
                }
                if (row.Kind == WidgetKind.Button)
                {
                    barButtons++;
                }
                start = i;
            }
            // A lone trailing control, a buttonless run of checkboxes, or a window that is
            // nothing but its bar keeps plain rows.
            if (start > 0 && n - start >= 2 && barButtons > 0)
            {
                buttonBarStart = start;
            }
        }

        /// <summary>Band membership test for <see cref="SortPresentationRowsByVisualPosition"/>: vertical overlap exceeding half the shorter row's height.</summary>
        private static bool SharesBand(Rect a, Rect b)
        {
            float overlap = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            if (overlap <= 0f)
            {
                return false;
            }
            float shorterHeight = Mathf.Min(a.height, b.height);
            return shorterHeight > 0f && overlap > shorterHeight * 0.5f;
        }

    }
}
