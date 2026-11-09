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
        /// A range control's caption: the fused adjacent Label, else the core's own "min - max"
        /// text (which also carries the control's name when the call site passed a labelKey —
        /// Verse/Widgets.cs:2225). Empty when neither exists.
        /// </summary>
        private string ResolveRangeCaption(FusionResult fusion)
        {
            if (fusion.LabelIndex >= 0)
            {
                return captureRows[fusion.LabelIndex].Label ?? "";
            }
            if (fusion.RangeTextIndex >= 0)
            {
                return captureRows[fusion.RangeTextIndex].Label ?? "";
            }
            return "";
        }

        /// <summary>One thumb of a range control as a navigable row — see the emission phase.</summary>
        private static PresentationRow BuildRangeRow(CapturedWidget row, int captureIndex, string caption, bool high, string section)
        {
            string label = string.IsNullOrEmpty(caption)
                ? (high ? "RimWorldAccess.UI.GenericWindow.RangeMaximum" : "RimWorldAccess.UI.GenericWindow.RangeMinimum").Translate().ToString()
                : (high ? "RimWorldAccess.UI.GenericWindow.RangeMaximumNamed" : "RimWorldAccess.UI.GenericWindow.RangeMinimumNamed").Translate(caption).ToString();
            return new PresentationRow
            {
                Source = row,
                Label = label,
                Kind = WidgetKind.Range,
                RangeHigh = high,
                Section = section,
                RingCaptureIndex = captureIndex,
                ActivateCaptureIndex = captureIndex,
                ScreenRect = row.ScreenRect,
                LabelScreenRect = row.ScreenRect,
                VisibleScreenRect = row.VisibleScreenRect,
                LabelVisibleScreenRect = row.VisibleScreenRect,
                Clip = row.Clip,
                LabelClip = row.Clip,
            };
        }

        /// <summary>
        /// Folds one Widgets.CheckboxLabeledSelectable run — caption Label (the anchor), the
        /// row-select ButtonInvisible drawn only while unselected, and the always-drawn 24px check
        /// toggle (Verse/Widgets.cs:1268-1300) — into ONE Checkbox row. Check state comes from the
        /// CheckTexMarker sniff, since CheckboxDraw is a raw texture draw with no widget. The
        /// toggle is the row's activation; the row-select target is the second affordance.
        /// </summary>
        private void FuseSelectableRow(bool[] consumed, FusionResult[] fusion, IReadOnlyList<CheckTexMarker> markers, int anchor)
        {
            int n = captureRows.Count;
            int selectIndex = -1;
            int checkIndex = -1;
            for (int j = anchor + 1; j < n; j++)
            {
                CapturedWidget m = captureRows[j];
                if (m.Composite == CompositeMember.SelectableRowSelect && selectIndex < 0)
                {
                    selectIndex = j;
                }
                else if (m.Composite == CompositeMember.SelectableRowCheck && checkIndex < 0)
                {
                    checkIndex = j;
                }
                else
                {
                    break;
                }
                consumed[j] = true;
            }
            fusion[anchor].KindOverride = WidgetKind.Checkbox;
            fusion[anchor].ActivateIndex = checkIndex;
            fusion[anchor].SecondaryIndex = selectIndex;
            CheckTexMarker? marker = FindOverlappingMarker(markers, captureRows[anchor], captureRows[anchor]);
            if (marker.HasValue && !marker.Value.Radio)
            {
                fusion[anchor].CheckOverride = MapTriState(marker.Value.State);
            }
        }

        /// <summary>
        /// Folds one contiguous Widgets.IntEntry / Listing_Standard.IntAdjuster stepper run into
        /// ONE row, anchored on the trailing value field when one exists (Enter opens the numeric
        /// session there) else on the run's first button. Only the small-button indexes get wired
        /// to Left/Right; the big ±(10×multiplier) pair stays unwired because vanilla's own
        /// Ctrl/Shift arithmetic scales the small step. The adjacent caption Label fuses by the
        /// blank Slider/TextField rule — these composites draw no label of their own.
        /// </summary>
        private void FuseStepperRun(bool[] consumed, FusionResult[] fusion, int first)
        {
            int n = captureRows.Count;
            int end = first;
            int valueIndex = -1;
            int minusSmall = -1, minusBig = -1, plusSmall = -1, plusBig = -1;
            while (end < n)
            {
                CapturedWidget m = captureRows[end];
                if (m.Kind == WidgetKind.Button && m.StepperButton)
                {
                    if (m.StepperPlus)
                    {
                        if (m.StepperBig) { plusBig = end; } else { plusSmall = end; }
                    }
                    else
                    {
                        if (m.StepperBig) { minusBig = end; } else { minusSmall = end; }
                    }
                    end++;
                    continue;
                }
                if (m.Kind == WidgetKind.TextField && m.StepperValueField && valueIndex < 0)
                {
                    // IntEntry draws its center field last, so the run ends here.
                    valueIndex = end;
                    end++;
                }
                break;
            }
            int anchor = valueIndex >= 0 ? valueIndex : first;
            for (int j = first; j < end; j++)
            {
                consumed[j] = j != anchor;
            }
            fusion[anchor].KindOverride = WidgetKind.Stepper;
            fusion[anchor].StepDownIndex = minusSmall;
            fusion[anchor].StepUpIndex = plusSmall;
            fusion[anchor].StepCaptions = BuildStepCaptions(minusBig, minusSmall, plusSmall, plusBig);
            if (first > 0)
            {
                CapturedWidget prev = captureRows[first - 1];
                if (IsStealableCaption(prev) && !consumed[first - 1]
                    && IsAdjacent(captureRows[first], prev))
                {
                    consumed[first - 1] = true;
                    fusion[anchor].LabelIndex = first - 1;
                }
            }
        }

        /// <summary>
        /// Folds a HAND-ROLLED spinner — two glyph Buttons with no capture bracket, found by
        /// <see cref="SpinnerGlyphs"/> — into the same one-row Stepper shape
        /// <see cref="FuseStepperRun"/> produces. The value may sit on either side of the buttons
        /// (HugsLib draws minus, plus, then its field; a caller drawing its own number puts it
        /// first), so both directions are tried, forward first. No big-step pair exists here, so
        /// both big indexes are always -1. A valueIsLabel anchor stays unconsumed so it remains a
        /// stealable caption for a later blank control's backward search.
        /// </summary>
        private void FuseGlyphStepperRun(bool[] consumed, FusionResult[] fusion, int headingBoundary, int minusIndex, int plusIndex)
        {
            int n = captureRows.Count;
            int valueIndex = -1;
            bool valueIsLabel = false;
            if (plusIndex + 1 < n && !consumed[plusIndex + 1])
            {
                CapturedWidget candidate = captureRows[plusIndex + 1];
                if (candidate.Kind == WidgetKind.TextField && string.IsNullOrEmpty(candidate.Label)
                    && string.IsNullOrEmpty(candidate.FieldLabel) && IsAdjacent(candidate, captureRows[plusIndex]))
                {
                    valueIndex = plusIndex + 1;
                }
            }
            if (valueIndex < 0)
            {
                // Backward: the caller drew its own number as a plain Label. Only the nearest
                // unconsumed stealable row is considered — a non-matching nearest row ends the
                // search rather than reaching past to something further back.
                for (int j = minusIndex - 1; j > headingBoundary; j--)
                {
                    if (!IsStealableCaption(captureRows[j]) || consumed[j])
                    {
                        continue;
                    }
                    if (SpinnerGlyphs.IsBareNumber(captureRows[j].Label) && IsAdjacent(captureRows[minusIndex], captureRows[j]))
                    {
                        valueIndex = j;
                        valueIsLabel = true;
                    }
                    break;
                }
            }

            int anchor = valueIndex >= 0 ? valueIndex : minusIndex;
            consumed[minusIndex] = anchor != minusIndex;
            consumed[plusIndex] = true;
            if (valueIndex >= 0)
            {
                consumed[valueIndex] = anchor != valueIndex;
            }

            fusion[anchor].KindOverride = WidgetKind.Stepper;
            fusion[anchor].StepDownIndex = minusIndex;
            fusion[anchor].StepUpIndex = plusIndex;
            fusion[anchor].StepCaptions = BuildStepCaptions(-1, minusIndex, plusIndex, -1);
            if (valueIsLabel)
            {
                fusion[anchor].StepValueOverride = captureRows[valueIndex].Label;
            }

            int runStart = valueIsLabel ? valueIndex : minusIndex;
            int captionIndex = FindAdjacentFusableLabel(captureRows, consumed, runStart, headingBoundary, captureRows[runStart]);
            if (captionIndex >= 0 && SpinnerGlyphs.IsBareNumber(captureRows[captionIndex].Label))
            {
                // That is the value, not the name.
                captionIndex = -1;
            }
            if (captionIndex >= 0)
            {
                consumed[captionIndex] = true;
                fusion[anchor].LabelIndex = captionIndex;
            }
            else if (valueIsLabel)
            {
                // No name found and the anchor's row IS the bare-number value: keep that number
                // out of the row's name, which BuildPresentationRows' Stepper fallback would
                // otherwise take from the anchor's own Label.
                fusion[anchor].LabelOverride = "";
            }
        }

        private void FuseValueLabeledSlider(bool[] consumed, FusionResult[] fusion, int sliderIndex, int headingBoundary)
        {
            CapturedWidget slider = captureRows[sliderIndex];
            if (!ValueLabeledSlider.LabelIsValue(slider.Label, slider.SliderValue))
            {
                return;
            }
            int captionIndex = sliderIndex - 1;
            int iconIndex = -1;
            if (captionIndex > headingBoundary && captureRows[captionIndex].Composite == CompositeMember.Icon)
            {
                iconIndex = captionIndex;
                captionIndex--;
            }
            if (captionIndex <= headingBoundary || consumed[captionIndex] || (iconIndex >= 0 && consumed[iconIndex]))
            {
                return;
            }
            CapturedWidget caption = captureRows[captionIndex];
            if (!IsStealableCaption(caption) || caption.Composite != CompositeMember.None
                || string.IsNullOrWhiteSpace(caption.Label) || !caption.Clip.Equals(slider.Clip)
                || !ValueLabeledSlider.CaptionsOnRow(LineBoxOf(caption), LineBoxOf(slider)))
            {
                return;
            }
            if (iconIndex >= 0)
            {
                CapturedWidget icon = captureRows[iconIndex];
                if (!icon.Clip.Equals(slider.Clip)
                    || !string.Equals((icon.Label ?? "").StripTags().Trim(), caption.Label.StripTags().Trim(), StringComparison.OrdinalIgnoreCase)
                    || !ValueLabeledSlider.IconBetween(LineBoxOf(caption), LineBoxOf(icon), LineBoxOf(slider)))
                {
                    return;
                }
                consumed[iconIndex] = true;
            }
            consumed[captionIndex] = true;
            fusion[sliderIndex].LabelIndex = captionIndex;
            fusion[sliderIndex].OwnLabelIsValue = true;
        }

        private static LineBox LineBoxOf(CapturedWidget row)
        {
            Rect r = row.ScreenRect;
            return new LineBox { X = r.x, Y = r.y, Width = r.width, Height = r.height };
        }

        /// <summary>
        /// The member buttons' captions in VISUAL left-to-right order (-10, -1, +1, +10), which
        /// is not draw order: Widgets.IntEntry anchors the plus pair right-edge with +10
        /// outermost. Absent members (IntAdjuster has no big pair) are skipped.
        /// </summary>
        private string BuildStepCaptions(int minusBig, int minusSmall, int plusSmall, int plusBig)
        {
            List<string> captions = new List<string>(4);
            AppendStepCaption(captions, minusBig);
            AppendStepCaption(captions, minusSmall);
            AppendStepCaption(captions, plusSmall);
            AppendStepCaption(captions, plusBig);
            return string.Join(", ", captions.ToArray());
        }

        private void AppendStepCaption(List<string> captions, int index)
        {
            if (index >= 0 && !string.IsNullOrEmpty(captureRows[index].Label))
            {
                captions.Add(captureRows[index].Label);
            }
        }

        /// <summary>
        /// Whether two capture rows may participate in a geometric fusion test: same clip context,
        /// or different contexts with BOTH visible screen rects non-empty and overlapping. The
        /// both-visible requirement is the safety: a scrolled-out row has an empty visible rect,
        /// so its unclipped rect can never collide with a widget below the fold.
        /// </summary>
        private static bool MayFuse(CapturedWidget a, CapturedWidget b)
        {
            if (a.Clip.Equals(b.Clip))
            {
                return true;   // caller still applies its own rect test
            }
            return a.VisibleScreenRect.width > 0f && b.VisibleScreenRect.width > 0f
                && a.VisibleScreenRect.Overlaps(b.VisibleScreenRect);
        }

        /// <summary>
        /// Phase 1's see-through test for a GapLine-only heading guess: an InvisibleButton hotspot
        /// ahead of the label, before the next REAL heading, whose rect overlaps it rule-(c) style
        /// means the label is a control caption. Weak headings ahead do not end the scan — this
        /// same rule may unstamp them on a later iteration.
        /// </summary>
        private static bool HasOverlappingControlHotspotAhead(List<CapturedWidget> rows, int labelIndex, bool hotspotOverlapOnly)
        {
            CapturedWidget label = rows[labelIndex];
            for (int j = labelIndex + 1; j < rows.Count; j++)
            {
                CapturedWidget row = rows[j];
                if (row.Kind == WidgetKind.Label && row.Heading && !row.HeadingFromGapLineOnly)
                {
                    return false;
                }
                if (row.Kind == WidgetKind.InvisibleButton && OverlapForPair(row, label))
                {
                    return true;
                }
                if (hotspotOverlapOnly)
                {
                    // Strong-heading callers accept only the containment-grade evidence above;
                    // the adjacency arm below is reserved for GapLine-only guesses.
                    continue;
                }
                // A BLANK control beside/below the label is the rule-(b)/(d) caption shape;
                // stamping that caption a heading would orphan the control's name.
                if ((row.Kind == WidgetKind.Slider || row.Kind == WidgetKind.Range || row.Kind == WidgetKind.TextField)
                    && string.IsNullOrWhiteSpace(row.Label) && IsAdjacent(row, label))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Overlap test for a pair <see cref="MayFuse"/> admitted: raw ScreenRects in the same clip, visible rects across clips.</summary>
        private static bool OverlapForPair(CapturedWidget a, CapturedWidget b)
        {
            if (a.Clip.Equals(b.Clip))
            {
                return a.ScreenRect.Overlaps(b.ScreenRect);
            }
            return a.VisibleScreenRect.Overlaps(b.VisibleScreenRect);
        }

        /// <summary>
        /// Rule (b)/(d) adjacency: a control directly below its label (small vertical gap,
        /// horizontal overlap) or sharing its row. Same clip uses raw ScreenRects; across clips the
        /// same math runs on the visible rects, both required non-empty so a scrolled-out row
        /// never fuses.
        /// </summary>
        private static bool IsAdjacent(CapturedWidget control, CapturedWidget label)
        {
            if (control.Clip.Equals(label.Clip))
            {
                return AdjacentRects(control.ScreenRect, label.ScreenRect);
            }
            // Cross-clip tier: both rects must be visible — see MayFuse.
            if (control.VisibleScreenRect.width > 0f && label.VisibleScreenRect.width > 0f)
            {
                return AdjacentRects(control.VisibleScreenRect, label.VisibleScreenRect);
            }
            return false;
        }

        /// <summary>
        /// The gap threshold is 18, not the tighter 12: Listing_Standard.Label / Gap(12f) / Slider
        /// leaves 12 + the listing's own 2px verticalSpacing = 14 real pixels, which 12 rejects.
        /// 18 stays well under one row height, so it cannot reach an unrelated row below.
        /// </summary>
        private static bool AdjacentRects(Rect controlRect, Rect labelRect)
        {
            bool verticalOverlap = controlRect.yMin < labelRect.yMax && labelRect.yMin < controlRect.yMax;
            if (verticalOverlap)
            {
                return true;
            }
            float gap = controlRect.y - labelRect.yMax;
            if (gap < 0f || gap > 18f)
            {
                return false;
            }
            return controlRect.xMin < labelRect.xMax && labelRect.xMin < controlRect.xMax;
        }

        /// <summary>Identical screen rects within sub-pixel tolerance — the mask-twin test (two draws of one rect expression can only differ by float noise).</summary>
        private static bool SameScreenRect(Rect a, Rect b)
        {
            return Mathf.Abs(a.x - b.x) < 0.5f && Mathf.Abs(a.y - b.y) < 0.5f
                && Mathf.Abs(a.width - b.width) < 0.5f && Mathf.Abs(a.height - b.height) < 0.5f;
        }

        /// <summary>
        /// The value-button-in-a-labeled-row search: the bounded backward walk of
        /// <see cref="FindFusableLabel"/>, admitting only a non-blank Label whose rect hosts the
        /// button as its row line (<see cref="RowBandHostsValueButton"/>) — plain text buttons
        /// need a stricter gate than rule (c)'s overlap.
        /// </summary>
        private static int FindRowCaptionForValueButton(List<CapturedWidget> rows, bool[] consumed, int fromIndexExclusive, int headingBoundary, CapturedWidget control)
        {
            for (int j = fromIndexExclusive - 1; j > headingBoundary; j--)
            {
                CapturedWidget label = rows[j];
                if (!IsStealableCaption(label) || consumed[j] || string.IsNullOrWhiteSpace(label.Label))
                {
                    continue;
                }
                if (MayFuse(control, label) && RowBandHostsValueButton(label, control))
                {
                    return j;
                }
            }
            return -1;
        }

        /// <summary>
        /// Whether <paramref name="label"/> is the row a value button lives in: label wider than
        /// the button and starting left of it, horizontal overlap (the button may hang past the
        /// label's right edge — rows that trim the label for a scrollbar still anchor the button
        /// to the untrimmed edge), and the button's vertical center inside the label's line.
        /// Cross-clip pairs compare visible rects, both required non-empty.
        /// </summary>
        private static bool RowBandHostsValueButton(CapturedWidget label, CapturedWidget control)
        {
            Rect l;
            Rect c;
            if (control.Clip.Equals(label.Clip))
            {
                l = label.ScreenRect;
                c = control.ScreenRect;
            }
            else if (control.VisibleScreenRect.width > 0f && label.VisibleScreenRect.width > 0f)
            {
                l = label.VisibleScreenRect;
                c = control.VisibleScreenRect;
            }
            else
            {
                return false;
            }
            if (l.width <= c.width || c.xMin <= l.xMin)
            {
                return false;
            }
            if (c.xMin >= l.xMax || l.xMin >= c.xMax)
            {
                return false;
            }
            float centerY = c.y + c.height / 2f;
            return centerY >= l.yMin && centerY <= l.yMax;
        }

        /// <summary>
        /// Whether a row may be consumed as some other control's caption: a non-heading Label.
        /// A Listing_Tree node's own label is excluded — it IS the TreeItem row, so letting a
        /// field drawn beside it (Verse/Listing_TreeDefs.cs:41-43) steal it deletes a tree row.
        /// </summary>
        private static bool IsStealableCaption(CapturedWidget label)
        {
            return label.Kind == WidgetKind.Label && !label.Heading
                && label.Composite != CompositeMember.TreeRowLabel;
        }

        /// <summary>
        /// Rule (c) label search: scans backward to the last heading and returns the nearest
        /// unconsumed Label that both MayFuse with and geometrically overlaps the control, else
        /// -1. Skipping past non-matching labels rather than stopping at the first is what lets a
        /// batched draw order (all captions, then all hotspots) fuse every pair, not just the last.
        /// </summary>
        private static int FindFusableLabel(List<CapturedWidget> rows, bool[] consumed, int fromIndexExclusive, int headingBoundary, CapturedWidget control)
        {
            for (int j = fromIndexExclusive - 1; j > headingBoundary; j--)
            {
                if (!IsStealableCaption(rows[j]) || consumed[j])
                {
                    continue;
                }
                if (MayFuse(control, rows[j]) && OverlapForPair(control, rows[j]))
                {
                    return j;
                }
            }
            return -1;
        }

        /// <summary>
        /// The hotspot-first card search: scans FORWARD for the first unconsumed Label drawn
        /// inside the control's own rect, stopping at the first row that is not a Label. Returns
        /// -1 when the caller drew no text inside its hotspot. A heading found this way is
        /// un-marked as it is taken — a title drawn inside a click target names that target and
        /// does not open a section — and the mutation is what stops the analysis loop from
        /// re-deciding the row as a heading when it reaches it.
        /// </summary>
        private static int FindEnclosedLabelAhead(List<CapturedWidget> rows, bool[] consumed, int fromIndexExclusive, int count, CapturedWidget control)
        {
            for (int j = fromIndexExclusive + 1; j < count; j++)
            {
                if (rows[j].Kind != WidgetKind.Label)
                {
                    return -1;
                }
                if (consumed[j] || rows[j].Composite == CompositeMember.TreeRowLabel)
                {
                    continue;
                }
                if (MayFuse(control, rows[j]) && EnclosesForPair(control, rows[j]))
                {
                    rows[j].Heading = false;
                    rows[j].HeadingFromGapLineOnly = false;
                    return j;
                }
            }
            return -1;
        }

        /// <summary>
        /// Containment twin of <see cref="OverlapForPair"/> on the same two-tier clip rule.
        /// Majority rather than strict containment: callers commonly overrun their panel by a
        /// pixel or clip a long line at the edge.
        /// </summary>
        private static bool EnclosesForPair(CapturedWidget outer, CapturedWidget inner)
        {
            Rect a = outer.Clip.Equals(inner.Clip) ? outer.ScreenRect : outer.VisibleScreenRect;
            Rect b = outer.Clip.Equals(inner.Clip) ? inner.ScreenRect : inner.VisibleScreenRect;
            float innerArea = b.width * b.height;
            if (innerArea <= 0f)
            {
                return false;
            }
            float ix = Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin));
            float iy = Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
            const float MajorityThreshold = 0.8f;
            return (ix * iy) / innerArea >= MajorityThreshold;
        }

        /// <summary>
        /// Rules (b)/(d) label search: the bounded backward walk of <see cref="FindFusableLabel"/>,
        /// but testing <see cref="IsAdjacent"/> instead of overlap, since a blank
        /// Slider/TextField/CheckboxMulti's caption is a separate row above it, never the same
        /// rect. Skipping non-adjacent labels lets a mod that batches its draws out of
        /// caption-then-control order still fuse.
        /// </summary>
        private static int FindAdjacentFusableLabel(List<CapturedWidget> rows, bool[] consumed, int fromIndexExclusive, int headingBoundary, CapturedWidget control)
        {
            for (int j = fromIndexExclusive - 1; j > headingBoundary; j--)
            {
                if (!IsStealableCaption(rows[j]) || consumed[j])
                {
                    continue;
                }
                if (IsAdjacent(control, rows[j])
                    || (j == fromIndexExclusive - 1 && DecorationGapAdjacent(control, rows[j])))
                {
                    return j;
                }
            }
            return -1;
        }

        /// <summary>"Name: 100%"-style caption carrying its control's value in one string — a digit
        /// right after the last colon, in a short tail, is the evidence.</summary>
        private static bool CaptionTextCarriesValue(string caption)
        {
            if (string.IsNullOrEmpty(caption))
            {
                return false;
            }
            int colon = caption.LastIndexOfAny(new[] { ':', '：' });
            if (colon < 1 || colon >= caption.Length - 1)
            {
                return false;
            }
            string tail = caption.Substring(colon + 1).Trim();
            return tail.Length > 0 && tail.Length <= 12 && char.IsDigit(tail[0]);
        }

        /// <summary>
        /// Caption, uncaptured decorative band, control (Simple Sidearms' speed-bias sliders): with
        /// nothing CAPTURED between the two rows, only decoration fills the space, so the gap cap
        /// relaxes to 48 — still under two row heights. Same-clip only; the cross-clip tiers keep
        /// the strict cap.
        /// </summary>
        private static bool DecorationGapAdjacent(CapturedWidget control, CapturedWidget label)
        {
            if (!control.Clip.Equals(label.Clip))
            {
                return false;
            }
            float gap = control.ScreenRect.y - label.ScreenRect.yMax;
            return gap >= 0f && gap <= 48f
                && control.ScreenRect.xMin < label.ScreenRect.xMax
                && label.ScreenRect.xMin < control.ScreenRect.xMax;
        }

        /// <summary>
        /// Rule (c)'s Button-vs-Checkbox-vs-RadioButton check: a marker decides the pair only when
        /// it shares a party's clip and overlaps that party's screen rect, or (cross-clip) its own
        /// visible rect is non-empty and overlaps either party's — the same both-visible safety, so
        /// a marker in a scrolled-out context never decides. An uncaptioned control passes itself
        /// as both parties.
        /// </summary>
        private static CheckTexMarker? FindOverlappingMarker(IReadOnlyList<CheckTexMarker> markers, CapturedWidget control, CapturedWidget label)
        {
            for (int i = 0; i < markers.Count; i++)
            {
                CheckTexMarker m = markers[i];
                bool sameClipHit = (m.Clip.Equals(control.Clip) && m.ScreenRect.Overlaps(control.ScreenRect))
                    || (m.Clip.Equals(label.Clip) && m.ScreenRect.Overlaps(label.ScreenRect));
                bool crossClipHit = m.VisibleScreenRect.width > 0f
                    && (m.VisibleScreenRect.Overlaps(control.VisibleScreenRect) || m.VisibleScreenRect.Overlaps(label.VisibleScreenRect));
                if (sameClipHit || crossClipHit)
                {
                    return m;
                }
            }
            return null;
        }

        /// <summary>
        /// Fallback for the read-only-checkbox second sweep, matching a label to a marker that
        /// shares its visual line but never overlaps it (a Label in <c>rect.LeftPart(0.8f)</c> and
        /// a checkbox texture in <c>rect.RightPart(0.2f)</c> are disjoint halves of one rect, so
        /// <see cref="FindOverlappingMarker"/> can never match them). Requires the SAME CLIP as the
        /// label — the cross-clip tier is deliberately not extended here — a marker whose Y range
        /// overlaps the label's row band, and whose left edge sits at or after the label's right
        /// edge, with a small epsilon for sub-pixel touch.
        /// <see cref="NothingBetweenLabelAndMarker"/> then refuses the match if anything else
        /// contests it. Only a Label left unconsumed by every other Phase 1 decision reaches here,
        /// so real InvisibleButton fusions keep first refusal.
        /// </summary>
        private static CheckTexMarker? FindSameRowMarker(IReadOnlyList<CheckTexMarker> markers, List<CapturedWidget> rows, int labelIndex, int n, CapturedWidget label)
        {
            Rect labelRect = label.ScreenRect;
            float epsilon = 2f;
            for (int i = 0; i < markers.Count; i++)
            {
                CheckTexMarker m = markers[i];
                if (!m.Clip.Equals(label.Clip))
                {
                    continue;
                }
                Rect markerRect = m.ScreenRect;
                bool sameBand = markerRect.yMin < labelRect.yMax && labelRect.yMin < markerRect.yMax;
                if (!sameBand || markerRect.xMin < labelRect.xMax - epsilon)
                {
                    continue;
                }
                if (NothingBetweenLabelAndMarker(rows, labelIndex, n, label.Clip, labelRect, markerRect))
                {
                    return m;
                }
            }
            return null;
        }

        /// <summary>
        /// Guard for <see cref="FindSameRowMarker"/>: refuses the match if any OTHER captured row
        /// sharing the label's clip and Y band overlaps the candidate marker (it belongs to that
        /// row) or sits in the gap between the label's right edge and the marker's left edge (the
        /// label is not the marker's caption). Checks rows regardless of their consumed flag — a
        /// row fused elsewhere still occupies screen space and can still contest geometrically.
        /// </summary>
        private static bool NothingBetweenLabelAndMarker(List<CapturedWidget> rows, int labelIndex, int n, GuiSpace.ClipKey clip, Rect labelRect, Rect markerRect)
        {
            for (int k = 0; k < n; k++)
            {
                if (k == labelIndex || !rows[k].Clip.Equals(clip))
                {
                    continue;
                }
                Rect r = rows[k].ScreenRect;
                bool sameBand = r.yMin < labelRect.yMax && labelRect.yMin < r.yMax;
                if (!sameBand)
                {
                    continue;
                }
                if (r.Overlaps(markerRect))
                {
                    return false;
                }
                if (r.xMin >= labelRect.xMax && r.xMax <= markerRect.xMin)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Turns the marker an InvisibleButton's fusion test found into the row's presented role:
        /// a radio texture makes it a RadioButton whose selected state IS the texture that drew
        /// (Widgets.cs:1420-1431), a checkbox texture a Checkbox with that fill state, no marker a
        /// plain Button.
        /// </summary>
        private static void ApplyMarkerPromotion(ref FusionResult result, CheckTexMarker? marker)
        {
            if (!marker.HasValue)
            {
                result.KindOverride = WidgetKind.Button;
                return;
            }
            if (marker.Value.Radio)
            {
                result.KindOverride = WidgetKind.RadioButton;
                result.SelectedOverride = marker.Value.State == MultiCheckboxState.On;
                return;
            }
            result.KindOverride = WidgetKind.Checkbox;
            result.CheckOverride = MapTriState(marker.Value.State);
        }

        private static CheckState MapTriState(MultiCheckboxState state)
        {
            switch (state)
            {
                case MultiCheckboxState.On:
                    return CheckState.Checked;
                case MultiCheckboxState.Partial:
                    return CheckState.PartiallyChecked;
                default:
                    return CheckState.Unchecked;
            }
        }

        /// <summary>
        /// The single check-state rule for both emission branches in BuildPresentationRows, fused
        /// and unfused, so they cannot drift: marker/composite override, then a vanilla tri-state,
        /// then a plain Widgets.Checkbox's Checked flag; null for everything else. The last tier
        /// matters because a blank checkbox that fuses carries no TriState at all.
        /// </summary>
        private static CheckState? ResolveCheckState(FusionResult fusion, CapturedWidget row)
        {
            return fusion.CheckOverride ?? (row.TriState.HasValue
                ? MapTriState(row.TriState.Value)
                : (row.Kind == WidgetKind.Checkbox ? (row.Checked ? CheckState.Checked : CheckState.Unchecked) : (CheckState?)null));
        }

        /// <summary>
        /// Counts the same-row Verse.WidgetRow run of ToggleableIcon controls starting right after
        /// <paramref name="labelIndex"/>. Such runs are contiguous in the capture stream by
        /// construction (a WidgetRow.Gap records no row), but <see cref="IsAdjacent"/> is still
        /// checked so an unrelated icon drawn after an unrelated label never folds; 0 when none follows.
        /// </summary>
        private static int CountToggleableIconRun(List<CapturedWidget> rows, bool[] consumed, int labelIndex, int n)
        {
            CapturedWidget label = rows[labelIndex];
            int count = 0;
            int j = labelIndex + 1;
            while (j < n && !consumed[j] && rows[j].Composite == CompositeMember.ToggleableIcon
                && IsAdjacent(rows[j], label))
            {
                count++;
                j++;
            }
            return count;
        }

        // Mirror shape: icons first, then a plain Label at the same WidgetRow y; 0 when none follows.
        private static int CountTrailingCaptionedIconRun(List<CapturedWidget> rows, bool[] consumed, int firstIcon, int n)
        {
            int j = firstIcon;
            while (j < n && !consumed[j] && rows[j].Composite == CompositeMember.ToggleableIcon)
            {
                j++;
            }
            if (j >= n || consumed[j])
            {
                return 0;
            }
            CapturedWidget caption = rows[j];
            bool plainCaption = caption.Kind == WidgetKind.Label && caption.Composite == CompositeMember.None
                && !caption.Heading && !string.IsNullOrEmpty(caption.Label);
            return plainCaption && caption.Rect.y == rows[firstIcon].Rect.y && caption.Clip.Equals(rows[firstIcon].Clip)
                ? j - firstIcon
                : 0;
        }

        /// <summary>
        /// Folds a caption+ToggleableIcon(s) run by giving each toggle its own
        /// <see cref="FusionResult.LabelOverride"/>: the shared caption alone
        /// for a lone toggle, "caption: icon name" when there are several, since the icon texture
        /// is the only thing that tells same-tooltip toggles apart. A toggle whose resolved tooltip
        /// exactly matches a run-mate's is marked <see cref="FusionResult.SuppressGenericTip"/> so
        /// <see cref="BuildDescription"/> does not repeat boilerplate. Tooltip resolution is safe
        /// here because this runs after the capture pass has closed.
        /// </summary>
        private static void FuseToggleableIconRun(FusionResult[] fusion, List<CapturedWidget> rows, int labelIndex, int firstIcon, int runLength)
        {
            CapturedWidget label = rows[labelIndex];
            string[] tips = null;
            if (runLength > 1)
            {
                tips = new string[runLength];
                for (int k = 0; k < runLength; k++)
                {
                    CapturedWidget icon = rows[firstIcon + k];
                    tips[k] = TooltipCapture.TryResolveAtScreen(icon.ScreenRect, icon.Clip);
                }
            }
            for (int k = 0; k < runLength; k++)
            {
                int iconIndex = firstIcon + k;
                fusion[iconIndex].LabelOverride = runLength == 1
                    ? (label.Label ?? "")
                    : ComposeToggleableIconLabel(label.Label, rows[iconIndex].IconTexName);
                if (tips == null || string.IsNullOrEmpty(tips[k]))
                {
                    continue;
                }
                for (int other = 0; other < runLength; other++)
                {
                    if (other != k && string.Equals(tips[other], tips[k], StringComparison.Ordinal))
                    {
                        fusion[iconIndex].SuppressGenericTip = true;
                        break;
                    }
                }
            }
        }

        /// <summary>"caption: icon name" for a multi-toggle run — falls back to the plain caption when the icon's texture name is unavailable or cleans to nothing.</summary>
        private static string ComposeToggleableIconLabel(string caption, string iconTexName)
        {
            string iconName = CleanTextureName(iconTexName, stripTrailingIcoToken: true);
            if (string.IsNullOrEmpty(iconName))
            {
                return caption ?? "";
            }
            return string.IsNullOrEmpty(caption) ? iconName : caption + ": " + iconName;
        }

        /// <summary>
        /// A mod-asset texture name cleaned for speech: strips a leading ALL-CAPS/digit prefix
        /// ending in '_', replaces remaining '_' with spaces, splits lower-to-upper camel-case
        /// boundaries, and — only when <paramref name="stripTrailingIcoToken"/> — drops a trailing
        /// "ICO"/"Icon" token, which is asset-pipeline noise no sighted player reads.
        /// Presentation-only: the result is never written back onto
        /// <see cref="CapturedWidget.Label"/>, which both activation channels match verbatim.
        /// </summary>
        private static string CleanTextureName(string texName, bool stripTrailingIcoToken)
        {
            if (string.IsNullOrEmpty(texName))
            {
                return texName ?? "";
            }
            string text = texName;
            int firstUnderscore = text.IndexOf('_');
            if (firstUnderscore > 0)
            {
                string prefix = text.Substring(0, firstUnderscore);
                bool prefixIsCapsOrDigits = true;
                foreach (char c in prefix)
                {
                    if (!char.IsUpper(c) && !char.IsDigit(c))
                    {
                        prefixIsCapsOrDigits = false;
                        break;
                    }
                }
                if (prefixIsCapsOrDigits)
                {
                    text = text.Substring(firstUnderscore + 1);
                }
            }
            text = text.Replace('_', ' ');
            var camelSplit = new StringBuilder(text.Length + 4);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (i > 0 && char.IsUpper(c) && char.IsLower(text[i - 1]))
                {
                    camelSplit.Append(' ');
                }
                camelSplit.Append(c);
            }
            text = camelSplit.ToString();
            if (stripTrailingIcoToken)
            {
                string[] tokens = text.Split(' ');
                if (tokens.Length > 1)
                {
                    string last = tokens[tokens.Length - 1];
                    if (string.Equals(last, "ICO", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(last, "Icon", StringComparison.OrdinalIgnoreCase))
                    {
                        text = string.Join(" ", tokens, 0, tokens.Length - 1);
                    }
                }
            }
            return text.Trim();
        }

        /// <summary>Unfused rows only: a generic TextField or FillableBar carries no label of its own, so it speaks the caption its wrapper's bracket carried (FieldLabel) — a text field falling back to a generic caption, a bar to nothing, since BuildDescription then names it by its own percentage; every other kind speaks its own (possibly blank) capture label as-is.</summary>
        private static string ResolvePresentationLabel(CapturedWidget row)
        {
            if (row.Kind == WidgetKind.FillableBar)
            {
                return row.FieldLabel ?? "";
            }
            if (row.Kind == WidgetKind.TextField && string.IsNullOrEmpty(row.Label))
            {
                if (!string.IsNullOrEmpty(row.FieldLabel))
                {
                    return row.FieldLabel;
                }
                return "RimWorldAccess.TextInput.LabelDefault".Loc().ToString();
            }
            if (row.Kind == WidgetKind.Button)
            {
                // A caption that is nothing but an arrowhead is a PICTURE of a direction and would
                // otherwise read as its Unicode character name. GlyphButtonNames defines how
                // narrow that is; a caption with any real text keeps every character.
                switch (GlyphButtonNames.Classify(row.Label))
                {
                    case GlyphDirection.Up:
                        return "RimWorldAccess.UI.GenericWindow.GlyphUp".Loc().ToString();
                    case GlyphDirection.Down:
                        return "RimWorldAccess.UI.GenericWindow.GlyphDown".Loc().ToString();
                    case GlyphDirection.Left:
                        return "RimWorldAccess.UI.GenericWindow.GlyphLeft".Loc().ToString();
                    case GlyphDirection.Right:
                        return "RimWorldAccess.UI.GenericWindow.GlyphRight".Loc().ToString();
                }
            }
            return row.Label ?? "";
        }

        /// <summary>
        /// True once the form carries at least two DISTINCT non-empty section names — the gate for
        /// the PageUp/PageDown section-jump claims. Section-less rows are ignored, so a dialog with
        /// one named section plus a title row leaves the claims dormant.
        /// </summary>
        private bool HasMultipleSections()
        {
            string first = null;
            for (int i = 0; i < presentationRows.Count; i++)
            {
                string section = presentationRows[i].Section;
                if (string.IsNullOrEmpty(section))
                {
                    continue;
                }
                if (first == null)
                {
                    first = section;
                }
                else if (section != first)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The window drew a tab STRIP: two or more captured <see cref="WidgetKind.Tab"/> rows —
        /// the gate for the Tab/Shift+Tab claims and for excluding tabs from
        /// <see cref="presentationRows"/>. Reads <see cref="tabRows"/>, snapshotted before that
        /// exclusion, so it stays accurate once tabs have left presentationRows.
        /// </summary>
        private bool HasTabBar()
        {
            return tabRows.Count >= 2;
        }

    }
}
