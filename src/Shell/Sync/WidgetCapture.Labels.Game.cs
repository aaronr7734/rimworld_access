using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    public static partial class WidgetCapture
    {
        internal static void RecordLabel(Rect rect, string label)
        {
            // Inside a DrawTabs strip every Widgets.Label is a tab caption, already a Tab row.
            if (!passOpen || tabStripDepth > 0)
            {
                return;
            }
            if (labelDoubleDepth > 0)
            {
                // LabelDouble draws one "name: value" row as two Labels; the left half carries the
                // pair and the right folds into it.
                if (labelDoubleSlot++ > 0)
                {
                    return;
                }
                label = ComposeLabelPair(label, labelDoubleRightText);
            }
            if (listingTreeLabelDepth > 0 && !string.IsNullOrEmpty(listingTreeLabelText))
            {
                // LabelLeft truncates to the column width, so the ellipsis belongs to the column,
                // not the node's name; the row carries the caller's string.
                label = listingTreeLabelText;
            }
            if (labelEllipsesDepth > 0 && !string.IsNullOrEmpty(labelEllipsesText))
            {
                // The same ellipsis artifact. Restoring it before the self-caption test keeps a
                // widget that captions itself through LabelEllipses matching its suppression.
                label = labelEllipsesText;
            }
            // A self-captioning widget's caption is already on the control row, and a blank label
            // draws nothing a sighted player reads.
            if (string.IsNullOrWhiteSpace(label)
                || (selfCaptionDepth > 0 && (label == selfCaptionText0 || label == selfCaptionText1 || label == selfCaptionText2)))
            {
                return;
            }
            // Pair-fold: some mods draw a NAME label and a VALUE label as two independent
            // Widgets.Label calls sharing, or majority-overlapping, one rect. Left uncaught, the
            // backward-scan caption fusion picks one of the two as the control's caption and the
            // other vanishes, so fold here at capture into the earlier row. Restricted to plain
            // label calls (no composite depth active), so it never touches LabelDouble, tree,
            // selectable-row, or range rows, which fold their own way or must stay independent.
            //
            // The !gapLinePending guard below still blocks this fold from reaching BACKWARD across
            // a GapLine boundary into the previous group's last row, but a prevRow.Heading that is
            // only a WEAK gap-line guess is allowed through: a mod using GapLine as a plain row
            // separator would otherwise have every pair's own name label marked a heading and lost.
            // A GameFont.Medium heading is never let through this way; the success branch below
            // corrects the guess once it is proven wrong.
            if (labelDoubleDepth == 0 && rangeDepth == 0 && listingTreeLabelDepth == 0
                && selectableRowDepth == 0 && defLabelIconDepth == 0
                && Text.Font != GameFont.Medium && !gapLinePending)
            {
                List<CapturedWidget> maybeSink = CurrentSink;
                if (maybeSink.Count > 0)
                {
                    CapturedWidget prevRow = maybeSink[maybeSink.Count - 1];
                    if (prevRow.Kind == WidgetKind.Label && (!prevRow.Heading || prevRow.HeadingFromGapLineOnly)
                        && prevRow.Label != label && prevRow.Clip.Equals(GuiSpace.CurrentClip())
                        && RectsCoLocatedForLabelFold(prevRow.Rect, rect))
                    {
                        if (prevRow.LabelPairFolded)
                        {
                            // Third and later co-located segments — a caller splitting one line
                            // into name, value and score cells — append as further value text, so
                            // the whole line stays one row and a control below can fuse with all
                            // of it rather than stealing the last cell alone.
                            prevRow.Label = prevRow.Label + " " + label;
                            prevRow.Rect = GuiSpace.Union(prevRow.Rect, rect);
                            prevRow.ScreenRect = GuiSpace.Union(prevRow.ScreenRect, GuiSpace.ToScreen(rect));
                            prevRow.VisibleScreenRect = RectUnionVisible(prevRow.VisibleScreenRect, GuiSpace.VisibleScreenRect(rect));
                            DrawFocusRingIfFocused(maybeSink.Count - 1, prevRow.Rect);
                            return;
                        }
                        TextAnchor anchor = Text.Anchor;
                        string nameHalf;
                        string valueHalf;
                        if (IsLeftTextAnchor(prevRow.RecordAnchor) && !IsLeftTextAnchor(anchor))
                        {
                            nameHalf = prevRow.Label;
                            valueHalf = label;
                        }
                        else if (IsLeftTextAnchor(anchor) && !IsLeftTextAnchor(prevRow.RecordAnchor))
                        {
                            nameHalf = label;
                            valueHalf = prevRow.Label;
                        }
                        else
                        {
                            // Neither anchor distinguishes them, so compose in draw order.
                            nameHalf = prevRow.Label;
                            valueHalf = label;
                        }
                        prevRow.Label = ComposeLabelPair(nameHalf, valueHalf);
                        prevRow.LabelPairName = nameHalf;
                        prevRow.Rect = GuiSpace.Union(prevRow.Rect, rect);
                        prevRow.ScreenRect = GuiSpace.Union(prevRow.ScreenRect, GuiSpace.ToScreen(rect));
                        prevRow.VisibleScreenRect = RectUnionVisible(prevRow.VisibleScreenRect, GuiSpace.VisibleScreenRect(rect));
                        prevRow.LabelPairFolded = true;
                        if (prevRow.HeadingFromGapLineOnly)
                        {
                            // A real section heading is never half of a name/value pair sharing a
                            // rect, so un-guess it and let the row present as NAME: VALUE instead
                            // of vanishing into silent section context.
                            prevRow.Heading = false;
                            prevRow.HeadingFromGapLineOnly = false;
                        }
                        DrawFocusRingIfFocused(maybeSink.Count - 1, prevRow.Rect);
                        return;
                    }
                }
            }
            // Heading detection: Dialog_ModSettings draws its title in GameFont.Medium, and mods
            // draw section headers after a Listing.GapLine. Read gapLinePending BEFORE clearing it —
            // this label is the row that consumes it. A composite's own caption is never a heading:
            // it names the control beside it, not the rows that follow.
            bool notComposite = labelDoubleDepth == 0 && selectableRowDepth == 0 && listingTreeLabelDepth == 0
                && rangeDepth == 0;
            bool mediumFontHeading = Text.Font == GameFont.Medium && notComposite;
            bool gapLineHeading = gapLinePending && notComposite;
            bool heading = mediumFontHeading || gapLineHeading;
            gapLinePending = false;
            List<CapturedWidget> sink = CurrentSink;
            int index = sink.Count;
            CapturedWidget row = new CapturedWidget { Kind = WidgetKind.Label, Label = label, Rect = rect, ScreenRect = GuiSpace.ToScreen(rect), VisibleScreenRect = GuiSpace.VisibleScreenRect(rect), Clip = GuiSpace.CurrentClip(), Heading = heading, HeadingFromGapLineOnly = gapLineHeading && !mediumFontHeading, TinyFont = Text.Font == GameFont.Tiny, RecordAnchor = Text.Anchor };
            if (rangeDepth > 0)
            {
                // The range core's own "min - max" text, kept as a row for the inspect-tab reader,
                // which has no other rendering of a filter's range; GenericWindowScope consumes it
                // into the control's two thumb rows.
                row.Composite = CompositeMember.RangeText;
            }
            else if (listingTreeLabelDepth > 0)
            {
                row.Composite = CompositeMember.TreeRowLabel;
                row.TreeLevel = listingTreeIndentLevel;
                row.Tip = listingTreeTipText;
                row.TipIsExact = true;
            }
            else if (selectableRowDepth > 0)
            {
                row.Composite = CompositeMember.SelectableRowLabel;
                row.Selected = selectableRowSelected;
            }
            else if (defLabelIconDepth > 0 && !string.IsNullOrEmpty(defLabelIconTip))
            {
                // DefLabelWithIcon registers the def description BEFORE its own BeginGroup and
                // draws this Label inside it, so tip and label land in different clip contexts and
                // TipIndex's context rule can never match them by rect. Carry it on the row.
                row.Tip = defLabelIconTip;
            }
            sink.Add(row);
            DrawFocusRingIfFocused(index, rect);
        }

        /// <summary>The one place a composite's two text halves become one spoken row; whole-phrase key, never a hardcoded joiner.</summary>
        private static string ComposeLabelPair(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(right))
            {
                return left;
            }
            if (string.IsNullOrWhiteSpace(left))
            {
                return right;
            }
            // A caller that already drew its own trailing colon must not also receive the LabelPair
            // key's ": ", which would double the separator.
            string trimmedLeft = left.TrimEnd();
            if (trimmedLeft.EndsWith(":"))
            {
                left = trimmedLeft.Substring(0, trimmedLeft.Length - 1);
            }
            return "RimWorldAccess.UI.GenericWindow.LabelPair".Translate(left, right).ToString();
        }

        /// <summary>
        /// RecordLabel's pair-fold geometry test: true for a near-identical rect, or a majority
        /// mutual overlap where two trimmed halves of one rect overlap heavily without matching.
        /// </summary>
        private static bool RectsCoLocatedForLabelFold(Rect a, Rect b)
        {
            const float NearIdenticalEpsilon = 0.5f;
            if (Mathf.Abs(a.x - b.x) <= NearIdenticalEpsilon && Mathf.Abs(a.y - b.y) <= NearIdenticalEpsilon
                && Mathf.Abs(a.width - b.width) <= NearIdenticalEpsilon && Mathf.Abs(a.height - b.height) <= NearIdenticalEpsilon)
            {
                return true;
            }
            float areaA = a.width * a.height;
            float areaB = b.width * b.height;
            if (areaA <= 0f || areaB <= 0f)
            {
                return false;
            }
            float ix = Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin));
            float iy = Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
            float intersection = ix * iy;
            const float MajorityOverlapThreshold = 0.8f;
            if (intersection / areaA >= MajorityOverlapThreshold && intersection / areaB >= MajorityOverlapThreshold)
            {
                return true;
            }
            // Edge-contiguous halves of one LabelDouble-shaped row: same y-band, same height,
            // horizontally adjacent with at most a hairline gap, in either draw order.
            const float EdgeContiguousEpsilon = 2f;
            if (Mathf.Abs(a.y - b.y) <= NearIdenticalEpsilon && Mathf.Abs(a.height - b.height) <= NearIdenticalEpsilon)
            {
                float gap = Mathf.Min(Mathf.Abs(b.xMin - a.xMax), Mathf.Abs(a.xMin - b.xMax));
                if (gap <= EdgeContiguousEpsilon)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Left-anchored text is a fold pair's name half; right/center anchors are the value half.</summary>
        private static bool IsLeftTextAnchor(TextAnchor anchor)
        {
            return anchor == TextAnchor.UpperLeft || anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.LowerLeft;
        }

        /// <summary>As <see cref="GuiSpace.Union"/>, but a zero-area side is dropped instead of corrupting the union with a phantom (0,0) corner.</summary>
        private static Rect RectUnionVisible(Rect a, Rect b)
        {
            bool aValid = a.width > 0f && a.height > 0f;
            bool bValid = b.width > 0f && b.height > 0f;
            if (!aValid)
            {
                return b;
            }
            if (!bValid)
            {
                return a;
            }
            return GuiSpace.Union(a, b);
        }

        /// <summary>
        /// One whole TabDrawer.DrawTabs strip, recorded from the patched core overload's prefix.
        /// Per-tab rects are recomputed with the drawer's own layout formula; the prefix runs before
        /// its BeginGroup, so these window-space rects are where the tabs land and the focus ring
        /// may draw against them directly. A pending activation invokes the record's own
        /// clickedAction with vanilla's click behavior — sound first, selected tab a no-op — BEFORE
        /// the strip draws, so the switched page renders this same pass.
        /// </summary>
        internal static void RecordTabs(Rect baseRect, System.Collections.IList tabs, float maxTabWidth)
        {
            if (!passOpen || tabs == null || tabs.Count == 0)
            {
                return;
            }
            float tabWidth = (baseRect.width + (float)(tabs.Count - 1) * 10f) / (float)tabs.Count;
            if (tabWidth > maxTabWidth)
            {
                tabWidth = maxTabWidth;
            }
            gapLinePending = false;
            List<CapturedWidget> sink = CurrentSink;
            for (int i = 0; i < tabs.Count; i++)
            {
                TabRecord tab = tabs[i] as TabRecord;
                if (tab == null)
                {
                    continue;
                }
                int index = sink.Count;
                Rect rect = new Rect(baseRect.x + (float)i * (tabWidth - 10f), baseRect.y - 31f, tabWidth, 32f);
                if (detachedPass)
                {
                    // Armed detached channel: switch to this one tab with vanilla's click
                    // semantics, as the live branch below does. Tabs carry no gate.
                    if (MaybeArmMatch(WidgetKind.Tab, tab.label ?? "", index))
                    {
                        armedFired = true;
                        if (!tab.Selected)
                        {
                            SoundDefOf.RowTabSelect.PlayOneShotOnCamera();
                            if (tab.clickedAction != null)
                            {
                                tab.clickedAction();
                            }
                        }
                    }
                }
                else
                {
                    MaybeLiveActivateMatch(WidgetKind.Tab, tab.label ?? "", index);
                    if (liveActivateFireIndex == index)
                    {
                        ClearPendingActivate();
                        if (!tab.Selected)
                        {
                            SoundDefOf.RowTabSelect.PlayOneShotOnCamera();
                            if (tab.clickedAction != null)
                            {
                                tab.clickedAction();
                            }
                        }
                    }
                }
                sink.Add(new CapturedWidget
                {
                    Kind = WidgetKind.Tab,
                    Label = tab.label ?? "",
                    Rect = rect,
                    ScreenRect = GuiSpace.ToScreen(rect),
                    VisibleScreenRect = GuiSpace.VisibleScreenRect(rect),
                    Clip = GuiSpace.CurrentClip(),
                    Selected = tab.Selected,
                    Tip = tab.GetTip(),
                });
                DrawFocusRingIfFocused(index, rect);
            }
        }

        internal static void EnterTabStrip()
        {
            tabStripDepth++;
        }

        internal static void ExitTabStrip()
        {
            if (tabStripDepth > 0)
            {
                tabStripDepth--;
            }
        }

        // The two primitives that are the only capture gaps across the card draw paths. They record
        // on live scope passes and detached capture passes alike. Known side effect:
        // ButtonTextSubtle draws an internal FillableBar when barPercent is set, so such buttons
        // contribute a read-only percent row beside their own label, which a sighted player sees too.

        /// <summary>
        /// Widgets.FillableBar's core overload, from its prefix: the body contracts and rescales its
        /// rect parameter, so a postfix would see mutated geometry. The bar draws no text — the fill
        /// fraction is the information — and a FillableBarLabeled bar carries its own caption here,
        /// the bracket having suppressed that Label.
        /// </summary>
        internal static void RecordFillableBar(Rect rect, float fillPercent)
        {
            if (!passOpen)
            {
                return;
            }
            gapLinePending = false;
            List<CapturedWidget> sink = CurrentSink;
            int index = sink.Count;
            sink.Add(new CapturedWidget
            {
                Kind = WidgetKind.FillableBar,
                Rect = rect,
                ScreenRect = GuiSpace.ToScreen(rect),
                VisibleScreenRect = GuiSpace.VisibleScreenRect(rect),
                Clip = GuiSpace.CurrentClip(),
                FillPercent = fillPercent,
                FieldLabel = fillableBarLabelDepth > 0 ? (fillableBarLabelText ?? "") : "",
            });
            DrawFocusRingIfFocused(index, rect);
        }

        /// <summary>
        /// FillableBarChangeArrows is an annotation, never a row: the arrows are a rate cue drawn
        /// beside a bar the pass already recorded, over that bar's own rect, so the signed rate lands
        /// on that row and presentation speaks it as rising or falling. Only the most recently
        /// recorded bar can own them, or a rate would attach to an unrelated bar further up the
        /// stream. The float overload scales and truncates into this int core, so one tap covers
        /// both, including vanilla's too-small case, which truncates to zero and annotates nothing.
        /// </summary>
        internal static void RecordBarChangeArrows(Rect barRect, int changeRate)
        {
            if (!passOpen || changeRate == 0)
            {
                return;
            }
            List<CapturedWidget> sink = CurrentSink;
            Rect screenRect = GuiSpace.ToScreen(barRect);
            GuiSpace.ClipKey clip = GuiSpace.CurrentClip();
            for (int i = sink.Count - 1; i >= 0; i--)
            {
                if (sink[i].Kind != WidgetKind.FillableBar)
                {
                    continue;
                }
                if (sink[i].Clip.Equals(clip) && sink[i].ScreenRect.Overlaps(screenRect))
                {
                    sink[i].BarChangeRate = changeRate;
                }
                return;
            }
        }

        /// <summary>
        /// Start of a self-captioning widget's body: its captions, drawn via the hooked Label, are
        /// suppressed by exact match. Most callers pass one; HorizontalSlider's left- and
        /// right-aligned labels fill the second and third slots.
        /// </summary>
        internal static void EnterSelfCaptioned(string caption, string caption2 = null, string caption3 = null)
        {
            if (passOpen)
            {
                selfCaptionDepth++;
                selfCaptionText0 = caption;
                selfCaptionText1 = caption2;
                selfCaptionText2 = caption3;
            }
        }

        internal static void ExitSelfCaptioned()
        {
            if (selfCaptionDepth > 0)
            {
                selfCaptionDepth--;
            }
        }

        /// <summary>
        /// Widgets.CustomButtonText exit: records the whole button as one Button row with the final
        /// rect, since cacheHeight resizes the ref rect inside the call. Its caption drew through the
        /// hooked Label while the self-caption bracket suppressed it, so the row carries that text.
        /// Returns the row's draw-order index so the patch can route a pending activation through
        /// MaybeForceActivate.
        /// </summary>
    }
}
