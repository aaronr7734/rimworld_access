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
        // ---------------------------------------------------------------
        // Presentation model
        // ---------------------------------------------------------------

        /// <summary>One navigable row: a capture row as-is, or a Label fused with the control it captions.</summary>
        private sealed class PresentationRow
        {
            public CapturedWidget Source;
            public string Label;
            public WidgetKind Kind;
            public CheckState? Check;

            /// <summary>
            /// Set only on a row promoted from an InvisibleButton by an overlapping RADIO
            /// CheckTexMarker, where Widgets.RadioButton draws state as a raw texture and the
            /// capture row's own Selected is meaningless. Null everywhere else.
            /// </summary>
            public bool? Selected;

            /// <summary>
            /// TreeItem rows only: the branch state the node's expander drew. Null on a LEAF —
            /// a non-openable Listing_Tree node draws no expander — which reads as "no branch
            /// state to speak" and as "nothing to toggle".
            /// </summary>
            public bool? Expanded;

            /// <summary>
            /// Range rows only: this row drives the MAXIMUM thumb. A range control emits two
            /// presentation rows over one capture row — vanilla's two thumbs — and every value,
            /// bound and adjust reads this flag to pick its end.
            /// </summary>
            public bool RangeHigh;

            public string Section;
            public int RingCaptureIndex;
            public int ActivateCaptureIndex;

            /// <summary>The row's selected state: the sniffed override when this row was promoted from a texture marker, otherwise the capture row's own flag.</summary>
            public bool EffectiveSelected
            {
                get { return Selected ?? (Source != null && Source.Selected); }
            }

            // Stepper rows only (a fused Widgets.IntEntry / Listing_Standard.IntAdjuster run):
            // the capture indexes of the SMALL minus/plus buttons Left/Right posts on. -1
            // elsewhere and on a degenerate run missing a polarity, which PostActivate ignores.
            // The big ±(10×multiplier) pair is deliberately unwired: vanilla's own Ctrl/Shift
            // arithmetic (GenUI.CurrentAdjustmentMultiplier) already scales the small button's
            // effect during the pass that consumes the injection.
            public int StepDownCaptureIndex = -1;
            public int StepUpCaptureIndex = -1;

            /// <summary>
            /// Labeled-composite rows: the SECOND affordance a composite offers on its one row —
            /// a CheckboxLabeledSelectable's row-select hotspot (the check toggle being the
            /// primary <see cref="ActivateCaptureIndex"/>), or a SelectableDef's delete icon. -1
            /// elsewhere, and on a composite that drew no second target. The
            /// <see cref="SelectRowActionId"/> claim fires it for a selectable row; a
            /// SelectableDef's delete icon deliberately stays unfired and unconsumed, remaining
            /// a navigable row of its own.
            /// </summary>
            public int SecondaryCaptureIndex = -1;

            // Stepper rows only: the member buttons' captions in visual left-to-right order.
            // Spoken as supplementary info (IntEntry) or as the row's value (IntAdjuster, which
            // draws no value of its own).
            public string StepCaptions;

            /// <summary>
            /// A hand-rolled spinner's value when the caller drew it as a plain Label rather
            /// than a text field. Null for every bracketed shape and for the hand-rolled shape
            /// that does use a field -- in both of those the anchor's own capture row carries
            /// the value.
            /// </summary>
            public string StepValue;

            /// <summary>
            /// Set on a toggle of a folded Label+ToggleableIcon run whose registered tooltip
            /// resolved identical to a sibling toggle's in the same run.
            /// </summary>
            public bool SuppressGenericTip;

            /// <summary>
            /// This TextField had a same-rect mask twin consumed into it (the password idiom:
            /// the real field, then mask glyphs painted over it). The captured text is the REAL
            /// value a sighted player never sees, so <see cref="BuildDescription"/> speaks a
            /// character count instead.
            /// </summary>
            public bool SpeechMasked;

            /// <summary>
            /// This Checkbox row was promoted off a plain Label with no InvisibleButton to fuse
            /// with — a checkbox texture drawn with deliberately no click target. The state is
            /// real and worth speaking; the control isn't. False on the
            /// InvisibleButton/ToggleableIcon promotions, which DO have a live click target.
            /// <see cref="ActivateFocusedRow"/> re-reads instead of posting,
            /// <see cref="BuildDescription"/> adds the ReadOnly word, and
            /// <see cref="RecordPassOutcome"/> excludes it from the interactive-elements gate.
            /// </summary>
            public bool ReadOnlyCheckbox;

            /// <summary>
            /// The borrowed caption is a NAME/VALUE pair
            /// (<see cref="CapturedWidget.LabelPairFolded"/>). That fold is capture evidence
            /// that the text already carries this control's current value, so a control drawing
            /// no value of its own must not speak a raw one on top of it.
            /// </summary>
            public bool CaptionCarriesValue;

            public Rect ScreenRect;
            public Rect LabelScreenRect;

            // Screen-space rects CLIPPED to what is on screen — the same Rect the hover hit
            // test uses, so a row scrolled out of its container is empty here and can never win
            // a pointer match.
            public Rect VisibleScreenRect;
            public Rect LabelVisibleScreenRect;

            // Clip context for ScreenRect/LabelScreenRect. Required alongside rect overlap by
            // every screen-space match below, tooltip resolution included; see
            // GuiSpace.ClipKey's remarks for why rect overlap alone is not safe.
            public GuiSpace.ClipKey Clip;
            public GuiSpace.ClipKey LabelClip;

            /// <summary>
            /// True for a plain Label row <see cref="AbsorbDisabledStripMembers"/> recognized as
            /// a research-gated tab drawn without the widget the strip probe reads. Lives
            /// EXCLUSIVELY in <see cref="tabRows"/> once true; <see cref="BuildTabFrameText"/>
            /// is the only reader and announces it as "tab, disabled".
            /// </summary>
            public bool DisabledTabStop;
        }

        /// <summary>Per-capture-row fusion outcome, computed by the analysis phase and consumed by the emission phase — see BuildPresentationRows.</summary>
        private struct FusionResult
        {
            public int LabelIndex;
            public WidgetKind? KindOverride;
            public CheckState? CheckOverride;
            // Radio twin of CheckOverride — see PresentationRow.Selected.
            public bool? SelectedOverride;
            // Tree twin of the two above — see PresentationRow.Expanded.
            public bool? ExpandedOverride;
            // Stepper anchors only — see PresentationRow's stepper fields.
            public int StepDownIndex;
            public int StepUpIndex;
            public string StepCaptions;

            /// <summary>
            /// A hand-rolled spinner's value when the caller drew it as a plain Label rather
            /// than a text field. Null for every bracketed shape and for the hand-rolled shape
            /// that does use a field -- in both of those the anchor's own capture row carries
            /// the value.
            /// </summary>
            public string StepValueOverride;

            /// <summary>
            /// Labeled composites whose VISIBLE row and live click target are different captured
            /// primitives: the row is emitted on the visible one (the ring outlines what is
            /// drawn) while injections post on this one. -1 leaves the row activating itself.
            /// </summary>
            public int ActivateIndex;

            /// <summary>Composite second affordance — see PresentationRow.SecondaryCaptureIndex.</summary>
            public int SecondaryIndex;

            /// <summary>A same-rect mask twin was consumed into this TextField — see PresentationRow.SpeechMasked.</summary>
            public bool SpeechMasked;

            /// <summary>
            /// Range rows only: the capture index of the "min - max" text the range core drew
            /// for itself, consumed so it cannot also read as an orphan row. -1 when the core
            /// drew none.
            /// </summary>
            public int RangeTextIndex;

            /// <summary>
            /// Set by <see cref="FuseToggleableIconRun"/> on a ToggleableIcon row a preceding
            /// same-row Label folded into: the composed name to use instead of
            /// <see cref="ResolvePresentationLabel"/>'s default.
            /// </summary>
            public string LabelOverride;

            /// <summary>Twin of <see cref="PresentationRow.SuppressGenericTip"/>, carried from Phase 1 to Phase 2 emission.</summary>
            public bool SuppressGenericTip;

            /// <summary>Twin of <see cref="PresentationRow.ReadOnlyCheckbox"/>, carried from Phase 1 to Phase 2 emission.</summary>
            public bool ReadOnlyCheckbox;
        }

        /// <summary>
        /// Recognises a panel heading drawn in <c>GameFont.Tiny</c> above a background panel,
        /// which neither the Medium-font nor the after-a-GapLine heading tier matches. The
        /// geometry/font-mix judgement lives in <see cref="TinyFontHeadingDetector"/>; this
        /// translates <see cref="captureRows"/> (raw draw order) into its input and stamps a
        /// qualifying row's <see cref="CapturedWidget.Heading"/> and
        /// <see cref="CapturedWidget.HeadingFromGapLineOnly"/> like the weak GapLine tier, so
        /// downstream rules need no extra branching. Must run before Phase 1 reads
        /// <c>Heading</c>.
        /// </summary>
        private void ApplyTinyFontHeadings()
        {
            int n = captureRows.Count;
            if (n == 0)
            {
                return;
            }
            var clipIds = new Dictionary<GuiSpace.ClipKey, int>();
            var candidates = new TinyHeadingCandidateRow[n];
            for (int i = 0; i < n; i++)
            {
                CapturedWidget row = captureRows[i];
                int clipId;
                if (!clipIds.TryGetValue(row.Clip, out clipId))
                {
                    clipId = clipIds.Count;
                    clipIds.Add(row.Clip, clipId);
                }
                candidates[i] = new TinyHeadingCandidateRow
                {
                    IsLabel = row.Kind == WidgetKind.Label,
                    IsTinyFont = row.Kind == WidgetKind.Label && row.TinyFont,
                    IsInteractive = row.Kind != WidgetKind.Label && row.Kind != WidgetKind.FillableBar,
                    ClipId = clipId,
                    Y = row.ScreenRect.y,
                    Height = row.ScreenRect.height,
                };
            }
            IReadOnlyList<bool> tinyHeadings = TinyFontHeadingDetector.FindHeadings(candidates);
            for (int i = 0; i < n; i++)
            {
                if (!tinyHeadings[i])
                {
                    continue;
                }
                CapturedWidget row = captureRows[i];
                row.Heading = true;
                row.HeadingFromGapLineOnly = true;
            }
        }

        /// <summary>
        /// Rebuilds <see cref="presentationRows"/> from <see cref="captureRows"/> and
        /// <see cref="WidgetCapture.CheckTexMarkers"/> every pass, in two forward walks over the
        /// draw-order list.
        ///
        /// Phase 1 (analysis, no emission) decides every fusion in draw order, so a later label
        /// search sees the true consumed-state of everything decided earlier in the same pass.
        /// Heading Labels are consumed as section context. A blank-labeled
        /// Slider/TextField/CheckboxMulti fuses with the nearest preceding, screen-adjacent,
        /// unconsumed non-heading Label; an InvisibleButton fuses with a preceding unconsumed
        /// Label it overlaps. Both are bounded backward SEARCHES
        /// (<see cref="FindAdjacentFusableLabel"/>, <see cref="FindFusableLabel"/>), never a
        /// stop-at-the-preceding-row test — a mod may draw unrelated rows between a caption and
        /// its control, or batch all captions before all hotspots. The search stops at the last
        /// heading: fusion never crosses one. A CheckTexMarker overlapping a non-dropdown pair
        /// picks Button vs Checkbox vs RadioButton (<see cref="ApplyMarkerPromotion"/>). An
        /// InvisibleButton with no fusable Label is dropped as an orphan unless it is a vanilla
        /// dropdown opener (emitted as a ComboBox) or an uncaptioned radio (emitted as a
        /// RadioButton). A row stamped by a composite bracket short-circuits all of that and
        /// folds by role instead of geometry.
        ///
        /// Geometric tests apply a two-tier clip rule (<see cref="MayFuse"/>): rows in the SAME
        /// <see cref="GuiSpace.ClipKey"/> match on raw screen rects; rows in DIFFERENT clips
        /// match only when both VISIBLE rects are non-empty and overlap. The visible-rect
        /// requirement is what stops a row scrolled below a fold from fusing with an unrelated
        /// widget whose unclipped rect happens to coincide.
        ///
        /// Phase 2 (emission) walks again with the final consumed flags and fusion map: headings
        /// update the running section name and emit nothing; consumed rows emit nothing; a Range
        /// control emits TWO rows, one per vanilla thumb, both pointing at its own capture index;
        /// everything else emits one <see cref="PresentationRow"/>, either carrying a fused
        /// Label's text (RingCaptureIndex = the Label's index, ActivateCaptureIndex = the
        /// control's) or mapping 1:1.
        /// </summary>
        private void BuildPresentationRows()
        {
            int n = captureRows.Count;
            IReadOnlyList<CheckTexMarker> checkTex = WidgetCapture.CheckTexMarkers;
            bool[] consumed = new bool[n];
            bool[] heading = new bool[n];
            FusionResult[] fusion = new FusionResult[n];
            for (int i = 0; i < n; i++)
            {
                fusion[i].LabelIndex = -1;
                fusion[i].StepDownIndex = -1;
                fusion[i].StepUpIndex = -1;
                fusion[i].ActivateIndex = -1;
                fusion[i].SecondaryIndex = -1;
                fusion[i].RangeTextIndex = -1;
            }

            // A TextField drawn twice at one rect is the password idiom: the real field first,
            // then mask glyphs painted over it. One control, one row: the mask twin is consumed
            // and the REAL field survives, since its captured text is what the mod's Confirm
            // reads and what edits must write. Only the immediately-next TextField is
            // considered — two different fields never legitimately share a rect.
            for (int i = 0; i < n; i++)
            {
                if (captureRows[i].Kind != WidgetKind.TextField || consumed[i])
                {
                    continue;
                }
                for (int j = i + 1; j < n; j++)
                {
                    if (captureRows[j].Kind != WidgetKind.TextField)
                    {
                        continue;
                    }
                    if (!consumed[j] && SameScreenRect(captureRows[i].ScreenRect, captureRows[j].ScreenRect))
                    {
                        consumed[j] = true;
                        fusion[i].SpeechMasked = true;
                    }
                    break;
                }
            }

            // Stamp qualifying GameFont.Tiny panel headings onto
            // captureRows BEFORE Phase 1 reads Heading — see ApplyTinyFontHeadings' remarks.
            ApplyTinyFontHeadings();

            bool hasCloseTwin = HasNonChromeCloseTwin();

            // ---- Phase 1: analysis ----
            int headingBoundary = -1;
            for (int i = 0; i < n; i++)
            {
                CapturedWidget row = captureRows[i];
                if (row.Kind == WidgetKind.Label && row.Heading)
                {
                    if (!HeadingTextCanName(ResolvePresentationLabel(row)))
                    {
                        // A heading NAMES its section; text with no letter or
                        // digit cannot name anything. Colony Manager Redux's
                        // never-updated clock placeholder draws a bare "---"
                        // in the heading font, which leaked as a "---."
                        // section prefix onto every row announced after it.
                        row.Heading = false;
                        row.HeadingFromGapLineOnly = false;
                    }
                    // A GapLine-only heading GUESS overlapped by a control hotspot ahead is
                    // that control's caption, not a section break: stamping it a heading would
                    // consume it, make it a fusion boundary, and orphan the control's hotspot
                    // away entirely.
                    else if (row.HeadingFromGapLineOnly && HasOverlappingControlHotspotAhead(captureRows, i, hotspotOverlapOnly: false))
                    {
                        row.Heading = false;
                        row.HeadingFromGapLineOnly = false;
                    }
                    // A STRONG (Medium-font) heading physically inside an InvisibleButton
                    // hotspot drawn after it is a hand-rolled control's caption, not a section
                    // break — stamping it a heading orphans the hotspot and leaves the header a
                    // dead Label the section can never be expanded from. Only the strict
                    // overlap arm is trusted: a blank control merely adjacent below stays
                    // weak-guess-only evidence, so a real Medium title above a search field
                    // keeps reading as a title.
                    else if (HasOverlappingControlHotspotAhead(captureRows, i, hotspotOverlapOnly: true))
                    {
                        row.Heading = false;
                        row.HeadingFromGapLineOnly = false;
                    }
                    else
                    {
                        heading[i] = true;
                        consumed[i] = true;
                        headingBoundary = i;
                        continue;
                    }
                }

                // Stepper and selectable-row fusion consume FORWARD (the whole
                // run is decided at its first member), the only two places
                // this loop does — every other branch below consumes backward.
                if (consumed[i])
                {
                    continue;
                }
                if (row.Composite == CompositeMember.FilterPanelHandoff)
                {
                    // A synthetic hand-off row standing in for a mod's whole ThingFilterUI
                    // panel: its interior is suppressed at capture time, so there is no vanilla
                    // widget at this capture index for PostActivate to re-fire.
                    // ActivateFocusedRow special-cases the payload instead of injecting a click.
                    fusion[i].KindOverride = WidgetKind.Button;
                    fusion[i].LabelOverride = "RimWorldAccess.Inspection.Tree.EditFilterList".Translate().ToString();
                    continue;
                }
                if (row.Composite == CompositeMember.SelectableRowLabel)
                {
                    // Forward-consuming, same self-marking discipline as the stepper run below:
                    // the anchor stays unconsumed (it is the emitted row), and a second scan
                    // from it would find no targets ahead and clear what the first one found.
                    // The guard states that invariant rather than repairing a live case.
                    if (fusion[i].KindOverride != WidgetKind.Checkbox)
                    {
                        FuseSelectableRow(consumed, fusion, checkTex, i);
                    }
                    continue;
                }
                if (row.Composite == CompositeMember.TreeRowExpander)
                {
                    // An expander whose node drew no LabelLeft (Listing_ResourceReadout) gets a
                    // TreeItem row of its own carrying depth and branch state, rather than a
                    // button named after the Collapse/Reveal texture asset. Harmless when a
                    // label DOES follow: that branch consumes this row on the next iteration.
                    fusion[i].KindOverride = WidgetKind.TreeItem;
                    fusion[i].ExpandedOverride = row.TreeOpen;
                    continue;
                }
                if (row.Composite == CompositeMember.TreeRowLabel)
                {
                    // A Listing_Tree node draws its expander FIRST and its label immediately
                    // after, so the pair folds BACKWARD: the label is the visible row the ring
                    // outlines, and the expander — already behind us, absent entirely on a leaf
                    // — is the click target vanilla's SetOpen hangs off.
                    fusion[i].KindOverride = WidgetKind.TreeItem;
                    if (i > 0 && !consumed[i - 1]
                        && captureRows[i - 1].Composite == CompositeMember.TreeRowExpander)
                    {
                        consumed[i - 1] = true;
                        fusion[i].ActivateIndex = i - 1;
                        fusion[i].ExpandedOverride = captureRows[i - 1].TreeOpen;
                    }
                    continue;
                }
                if (row.Kind == WidgetKind.Label && row.Composite == CompositeMember.None)
                {
                    // A Verse.WidgetRow caption immediately followed, on the SAME visual row,
                    // by 1..N WidgetRow.ToggleableIcon controls. Every icon in such a run shares
                    // one tooltip, so without this fold each toggle reads indistinguishably.
                    // Forward-consuming: the label is the anchor and is folded away here, so the
                    // loop cannot re-enter this decision.
                    int runLength = CountToggleableIconRun(captureRows, consumed, i, n);
                    if (runLength > 0)
                    {
                        FuseToggleableIconRun(fusion, captureRows, i, runLength);
                        consumed[i] = true;
                        continue;
                    }
                }
                if (row.Kind == WidgetKind.Range)
                {
                    // A range core draws its own "min - max" text through the hooked Label
                    // IMMEDIATELY after recording this row, so the pair folds FORWARD: the text
                    // row is consumed here and becomes the caption when the caller drew none.
                    // Two-part re-entry guard: the RangeTextIndex test refuses a second fold,
                    // and marking the target consumed makes consumed[] skip past it.
                    if (fusion[i].RangeTextIndex < 0 && i + 1 < n && !consumed[i + 1]
                        && captureRows[i + 1].Composite == CompositeMember.RangeText)
                    {
                        consumed[i + 1] = true;
                        fusion[i].RangeTextIndex = i + 1;
                    }
                    // A caller's OWN caption drawn above the control (the
                    // blank-Slider rule's shape) names it better than the
                    // rendered numbers do, so it wins when both exist.
                    if (i > 0)
                    {
                        CapturedWidget above = captureRows[i - 1];
                        if (IsStealableCaption(above) && !consumed[i - 1] && IsAdjacent(row, above))
                        {
                            consumed[i - 1] = true;
                            fusion[i].LabelIndex = i - 1;
                        }
                    }
                    continue;
                }
                if (row.StepperButton || row.StepperValueField)
                {
                    // The anchor is deliberately left unconsumed (it is the emitted row), so
                    // the loop reaches it again. Re-running the scan from it would find no
                    // buttons ahead and overwrite the step indexes and captions with empties —
                    // KindOverride is the marker that this run is already folded.
                    if (fusion[i].KindOverride != WidgetKind.Stepper)
                    {
                        FuseStepperRun(consumed, fusion, i);
                    }
                    continue;
                }
                // A HAND-ROLLED spinner: two glyph-only Buttons with no capture bracket.
                // Anchoring on the decrement glyph and requiring the increment partner
                // immediately after is what keeps a lone "-" reading as the real remove control
                // it is rather than half of an invented spinner. Must run before the
                // value-button branch below, which would otherwise claim each arrow for
                // whatever text sits nearest it.
                if (row.Kind == WidgetKind.Button && !row.StepperButton && !row.DropdownOpener
                    && !row.CloseX && row.Composite == CompositeMember.None
                    && SpinnerGlyphs.IsDecrement(row.Label) && i + 1 < n && !consumed[i + 1])
                {
                    CapturedWidget plusCandidate = captureRows[i + 1];
                    if (plusCandidate.Kind == WidgetKind.Button && !plusCandidate.StepperButton
                        && !plusCandidate.DropdownOpener && !plusCandidate.CloseX
                        && plusCandidate.Composite == CompositeMember.None
                        && SpinnerGlyphs.IsIncrement(plusCandidate.Label) && IsAdjacent(row, plusCandidate))
                    {
                        // Re-entry guard, same shape as the bracketed branch above: when the
                        // anchor turns out to be the value TextField (forward shape), the loop
                        // reaches that field's own index later and re-enters this scan only if
                        // it revisited the SAME minus button, which a single forward pass never
                        // does -- kept for symmetry with FuseStepperRun's own guard.
                        if (fusion[i].KindOverride != WidgetKind.Stepper)
                        {
                            FuseGlyphStepperRun(consumed, fusion, headingBoundary, i, i + 1);
                        }
                        continue;
                    }
                }

                // A window can draw both a corner close-X and a bottom Close button for the
                // same action, giving two identical "Close" rows. Drop the X and keep the bottom
                // button; a window with only doCloseX keeps its X as the sole close control.
                if (row.Kind == WidgetKind.Button && row.CloseX && window.doCloseX
                    && (window.doCloseButton || hasCloseTwin))
                {
                    consumed[i] = true;
                    continue;
                }
                if (row.Composite == CompositeMember.ToggleableIcon)
                {
                    // Verse.WidgetRow.ToggleableIcon: a ButtonImage whose on/off state is a
                    // bare GUI.DrawTexture overlay that marker promotion cannot reach (it only
                    // runs against InvisibleButton rows). The capture bracket read the state off
                    // the caller's ref bool, so this promotion is evidence, not geometry. It
                    // consumes nothing and writes only its own index, so it needs no re-entry
                    // guard.
                    fusion[i].KindOverride = WidgetKind.Checkbox;
                    fusion[i].CheckOverride = row.Checked ? CheckState.Checked : CheckState.Unchecked;
                    continue;
                }

                // A blank checkbox is fusable whether or not it is tri-state. The tri-state
                // restriction was the narrower case that happened to be needed first; the shape
                // is identical either way -- Widgets.Checkbox draws a bare 24-pixel box with no
                // caption of its own, so a caller that wants it named draws a Label beside it,
                // exactly as it does for a blank Slider or TextField.
                bool blankFusable = row.Kind == WidgetKind.Slider || row.Kind == WidgetKind.TextField
                    || row.Kind == WidgetKind.Checkbox;
                // A FieldLabel-carrying text field is not blank — its labeled wrapper's bracket
                // already named it — so the adjacency heuristic must not steal an unrelated
                // preceding Label for it. KindOverride != null excludes a stepper anchor the
                // glyph-run rule already fused, whose later visit would otherwise let an
                // adjacent Label overwrite the caption FuseGlyphStepperRun already consumed.
                if (blankFusable && fusion[i].KindOverride == null
                    && string.IsNullOrEmpty(row.Label) && string.IsNullOrEmpty(row.FieldLabel) && i > 0)
                {
                    // Bounded backward scan, not a stop-at-i-1 test: a caller may draw
                    // unrelated rows between a control and its caption, so
                    // FindAdjacentFusableLabel skips past consumed/non-label rows to the true
                    // caption. When the caption IS the preceding row it is the first candidate.
                    int captionIndex = FindAdjacentFusableLabel(captureRows, consumed, i, headingBoundary, row);
                    if (captionIndex < 0 && row.Kind == WidgetKind.Slider
                        && WidgetCapture.ListingColumnBreaks.Contains(i)
                        && !consumed[i - 1] && IsStealableCaption(captureRows[i - 1]))
                    {
                        // Listing overflow-wrapped the slider into a new column; the caption
                        // drawn right before the wrap is still its caption.
                        captionIndex = i - 1;
                    }
                    if (captionIndex >= 0)
                    {
                        consumed[captionIndex] = true;
                        fusion[i].LabelIndex = captionIndex;
                    }
                }
                else if (row.Kind == WidgetKind.Button && row.Composite == CompositeMember.HyperlinkLabel)
                {
                    // Widgets.HyperlinkWithIcon's two halves: this caption is what the ring
                    // outlines, while the ButtonInvisible drawn immediately after over the same
                    // rect handles the click. Nothing records between the two, so the target is
                    // the next row; consuming it forward is also the re-entry guard.
                    if (i + 1 < n && !consumed[i + 1]
                        && captureRows[i + 1].Composite == CompositeMember.HyperlinkTarget)
                    {
                        consumed[i + 1] = true;
                        fusion[i].ActivateIndex = i + 1;
                    }
                }
                else if (row.Kind == WidgetKind.Button && row.DropdownOpener && i > 0)
                {
                    // Labeled dropdown/combo buttons fuse like blankFusable above, but are
                    // never gated on a blank Label: these buttons carry their own caption (the
                    // current choice), which BuildDescription speaks as the combo's value.
                    CapturedWidget prev = captureRows[i - 1];
                    if (IsStealableCaption(prev) && !consumed[i - 1]
                        && IsAdjacent(row, prev))
                    {
                        consumed[i - 1] = true;
                        fusion[i].LabelIndex = i - 1;
                    }
                }
                else if (row.Kind == WidgetKind.Button && !string.IsNullOrWhiteSpace(row.Label) && i > 0)
                {
                    // A VALUE BUTTON hosted inside a labeled row: one row-wide caption Label
                    // plus a text button at the row's right edge on that label's own line, whose
                    // caption is the row's value or verb, not its name. Unfused, every such row
                    // reads as a nameless pair. The geometry gate (RowBandHostsValueButton) is
                    // deliberately stricter than rule (c)'s bare overlap — the label must be the
                    // ROW (wider than the button, starting left of it) and the button must sit
                    // on its line — so a bottom-row or corner button can never steal a paragraph
                    // it merely neighbors. The button's caption is spoken as the row's value.
                    int captionIndex = FindRowCaptionForValueButton(captureRows, consumed, i, headingBoundary, row);
                    if (captionIndex >= 0)
                    {
                        consumed[captionIndex] = true;
                        fusion[i].LabelIndex = captionIndex;
                    }
                }
                else if (row.Kind == WidgetKind.InvisibleButton)
                {
                    if (row.Composite == CompositeMember.ColorSwatch || row.Composite == CompositeMember.MedicalCareChoice)
                    {
                        // A Widgets.ColorBox swatch or a medical care icon is one choice out of a
                        // set — RadioButton grammar, with the chosen state read from vanilla's
                        // own test at capture time. Decided BEFORE the label search below: these
                        // runs draw many captionless hotspots, and letting them hunt backwards
                        // would strip the caption off whatever control precedes the run.
                        fusion[i].KindOverride = WidgetKind.RadioButton;
                        fusion[i].SelectedOverride = row.Selected;
                        continue;
                    }
                    if (row.Composite == CompositeMember.SelectableDefRow && i > 0
                        && captureRows[i - 1].Composite == CompositeMember.SelectableDefDelete)
                    {
                        // Listing_Standard.SelectableDef draws Label, delete icon, then this
                        // full-row hotspot, so the icon is the row immediately behind. NOT
                        // consumed: no delete claim exists to fire it from the fused row, and
                        // folding it away would put a clickable control out of reach.
                        fusion[i].SecondaryIndex = i - 1;
                    }
                    int labelIndex = FindFusableLabel(captureRows, consumed, i, headingBoundary, row);
                    if (labelIndex >= 0)
                    {
                        consumed[labelIndex] = true;
                        fusion[i].LabelIndex = labelIndex;
                        if (row.DropdownOpener)
                        {
                            // A checkmark overlapping a dropdown opener is
                            // decoration, not state — the mod's own dropdown
                            // drives its selection. Present as a ComboBox
                            // (the role rides on Source.DropdownOpener), never
                            // as a checkbox, so skip the CheckTexMarker lookup.
                            fusion[i].KindOverride = WidgetKind.Button;
                        }
                        else
                        {
                            ApplyMarkerPromotion(ref fusion[i], FindOverlappingMarker(checkTex, row, captureRows[labelIndex]));
                        }
                    }
                    else if (row.DropdownOpener)
                    {
                        // Icon-mode dropdown opener with no caption to fuse: emit it as its own
                        // ComboBox row rather than dropping it as an orphan. Label stays empty;
                        // the tooltip channel and ComboBox role carry its meaning.
                        fusion[i].KindOverride = WidgetKind.Button;
                    }
                    else
                    {
                        // Uncaptioned radio: Widgets.RadioButton draws a bare texture plus a
                        // same-rect ButtonInvisible and no label, so the call site's caption is
                        // frequently drawn AFTER it, out of the backward search's reach. Emit it
                        // anyway — the sniffed marker proves it is a real control. A CHECKBOX
                        // marker over an uncaptioned hotspot keeps being dropped; a nameless
                        // checkbox is what the orphan rule was written for.
                        CheckTexMarker? marker = FindOverlappingMarker(checkTex, row, row);
                        if (marker.HasValue && marker.Value.Radio)
                        {
                            ApplyMarkerPromotion(ref fusion[i], marker);
                            // The caption sits AFTER the hotspot for this shape, so the only
                            // fusable label is the next row. Forward-consuming: the fused label
                            // is marked consumed and the emitted row is the radio itself, so the
                            // loop cannot re-enter. Radios only — a trailing label usually
                            // belongs to a FOLLOWING control, and the sniffed marker is what
                            // makes this case certain.
                            if (i + 1 < n)
                            {
                                CapturedWidget next = captureRows[i + 1];
                                if (IsStealableCaption(next) && !consumed[i + 1]
                                    && IsAdjacent(row, next))
                                {
                                    consumed[i + 1] = true;
                                    fusion[i].LabelIndex = i + 1;
                                }
                            }
                        }
                        else
                        {
                            // Last resort before dropping the control: a HOTSPOT-FIRST CARD,
                            // where a caller paints a panel, lays its ButtonInvisible over the
                            // whole thing, then writes the text INSIDE it — out of the backward
                            // search's reach. Confined to the case where the alternative is
                            // losing the control outright, and it demands containment rather
                            // than mere overlap: text drawn INSIDE a click target belongs to it,
                            // text that merely brushes it could belong to anything.
                            int aheadIndex = FindEnclosedLabelAhead(captureRows, consumed, i, n, row);
                            if (aheadIndex >= 0)
                            {
                                consumed[aheadIndex] = true;
                                fusion[i].LabelIndex = aheadIndex;
                                fusion[i].KindOverride = WidgetKind.Button;
                                ApplyMarkerPromotion(ref fusion[i], FindOverlappingMarker(checkTex, row, captureRows[aheadIndex]));
                            }
                            else
                            {
                                consumed[i] = true; // orphan: dropped at emission
                            }
                        }
                    }
                }
            }

            // ---- Phase 1, second sweep: read-only checkbox textures over plain Labels ----
            // Runs AFTER the main loop has fully settled consumed[], never at each Label's own
            // iteration: a Label may be fused (consumed) by an InvisibleButton drawn LATER, and
            // deciding early would win that race and double-promote the row. Only a Label still
            // unconsumed after every Phase 1 decision is a genuine orphan.
            for (int i = 0; i < n; i++)
            {
                CapturedWidget row = captureRows[i];
                if (consumed[i] || heading[i] || row.Kind != WidgetKind.Label
                    || row.Composite != CompositeMember.None || string.IsNullOrEmpty(row.Label))
                {
                    continue;
                }
                // A caller may split ONE row rect into a left-part Label and a right-part
                // checkbox texture: same Y band, but edge-adjacent and never overlapping.
                // FindOverlappingMarker requires actual Rect.Overlaps and can never match that,
                // so FindSameRowMarker is the fallback for "same visual row, disjoint halves".
                CheckTexMarker? marker = FindOverlappingMarker(checkTex, row, row)
                    ?? FindSameRowMarker(checkTex, captureRows, i, n, row);
                MultiCheckboxState state;
                if (marker.HasValue && !marker.Value.Radio)
                {
                    // A radio texture over a plain Label is not this rule's
                    // shape (nothing in this vocabulary draws one that way
                    // today) — the rule's ask is specifically the
                    // checkbox-texture case, so a radio marker here is left
                    // for whatever the InvisibleButton path already covers.
                    state = marker.Value.State;
                    readOnlyCheckboxLatch[row.Label] = state;
                }
                else if (!readOnlyCheckboxLatch.TryGetValue(row.Label, out state))
                {
                    continue;
                }
                fusion[i].KindOverride = WidgetKind.Checkbox;
                fusion[i].CheckOverride = MapTriState(state);
                fusion[i].ReadOnlyCheckbox = true;
            }

            // ---- Phase 1b: demote content-less headings ----
            // A heading earns its silent-crossing only when it names a group of following rows.
            // A "heading" with no emitted row before the next heading (or the list's end)
            // governs nothing — it is the dialog's title or a standalone label, and swallowing
            // it silently hides it from the player. Demote it to an ordinary navigable row so it
            // is announced and re-readable. A fused control keeps consumed==false at its own
            // index, so it counts as governed content; a fused-away label or dropped orphan
            // does not.
            for (int i = 0; i < n; i++)
            {
                if (!heading[i])
                {
                    continue;
                }
                bool governsContent = false;
                for (int j = i + 1; j < n && !heading[j]; j++)
                {
                    if (!consumed[j])
                    {
                        governsContent = true;
                        break;
                    }
                }
                if (!governsContent)
                {
                    heading[i] = false;
                    consumed[i] = false;
                }
            }

            // ---- Phase 2: emission ----
            // Section ownership is GEOMETRIC, not draw order: a heading governs only the rows
            // below it sharing its column (horizontal span overlap; see SectionOwnership). A
            // window that draws one column fully before the next would otherwise hand its
            // heading to every row drawn afterward regardless of column, which makes the section
            // prefix flip-flop once SortPresentationRowsByVisualPosition interleaves the columns
            // band by band. A row the window drew as its own chrome (ResolveWindowChromeRows) is
            // exempt on both sides: it owns no section as a plain row, and names none as a
            // heading.
            Rect[] effectiveCaptureRect = new Rect[n];
            for (int i = 0; i < n; i++)
            {
                Rect r = captureRows[i].ScreenRect;
                effectiveCaptureRect[i] = HasUsableRect(r) ? r : (i > 0 ? effectiveCaptureRect[i - 1] : r);
            }
            bool[] windowChrome = ResolveWindowChromeRows();
            string[] ownerSections = ComputeSectionOwners(heading, windowChrome, effectiveCaptureRect);

            presentationRows.Clear();
            for (int i = 0; i < n; i++)
            {
                CapturedWidget row = captureRows[i];
                if (heading[i])
                {
                    continue;
                }
                if (consumed[i])
                {
                    continue;
                }
                string ownerSection = ownerSections[i];
                if (row.Kind == WidgetKind.Range)
                {
                    // ONE captured control, TWO navigable rows — vanilla's own
                    // two thumbs. Both point at the same capture index (the
                    // control is what the ring outlines and what every adjust
                    // posts against); PresentationRow.RangeHigh is what tells
                    // them apart everywhere downstream.
                    string caption = ResolveRangeCaption(fusion[i]);
                    presentationRows.Add(BuildRangeRow(row, i, caption, high: false, section: ownerSection));
                    presentationRows.Add(BuildRangeRow(row, i, caption, high: true, section: ownerSection));
                    continue;
                }

                PresentationRow pr = new PresentationRow { Source = row, Section = ownerSection };
                int labelIdx = fusion[i].LabelIndex;
                if (labelIdx >= 0)
                {
                    CapturedWidget label = captureRows[labelIdx];
                    pr.Label = label.Label;
                    pr.RingCaptureIndex = labelIdx;
                    pr.ActivateCaptureIndex = i;
                    pr.LabelScreenRect = label.ScreenRect;
                    pr.ScreenRect = row.ScreenRect;
                    pr.VisibleScreenRect = row.VisibleScreenRect;
                    pr.LabelVisibleScreenRect = label.VisibleScreenRect;
                    // Equal to each other by construction — fusion required
                    // clip equality in Phase 1 — kept as two fields anyway so
                    // callers never have to reason about which one is "the"
                    // clip for a fused row.
                    pr.Clip = row.Clip;
                    pr.LabelClip = label.Clip;
                    pr.Kind = fusion[i].KindOverride ?? row.Kind;
                    pr.Check = ResolveCheckState(fusion[i], row);
                    // "Name: 100%"-style captions carry the value in one string, which the
                    // pair-fold can never see; a digit right after the colon is the evidence.
                    pr.CaptionCarriesValue = label.LabelPairFolded || CaptionTextCarriesValue(label.Label);
                }
                else
                {
                    if (fusion[i].LabelOverride != null)
                    {
                        // A toggle from a folded Label+ToggleableIcon(s) run. KindOverride and
                        // CheckOverride are already set by the generic per-row ToggleableIcon
                        // rule in Phase 1, so only the label needs overriding here.
                        pr.Label = fusion[i].LabelOverride;
                    }
                    else if (fusion[i].KindOverride == WidgetKind.Stepper)
                    {
                        // A stepper anchor's underlying row is IntEntry's
                        // center TextField (or IntAdjuster's minus button) —
                        // never let the blank-TextField fallback caption
                        // ("text field") name the composite; an uncaptioned
                        // stepper stays blank like an uncaptioned button does.
                        pr.Label = row.Label ?? "";
                    }
                    else if (fusion[i].KindOverride == WidgetKind.TreeItem && row.Kind != WidgetKind.Label)
                    {
                        // A tree row anchored on its EXPANDER — the node drew
                        // no LabelLeft at all. The button's captured name is
                        // its Collapse/Reveal texture asset, never a node
                        // name, so the row speaks only its depth and branch
                        // state.
                        pr.Label = "";
                    }
                    else
                    {
                        pr.Label = ResolvePresentationLabel(row);
                    }
                    pr.RingCaptureIndex = i;
                    pr.ActivateCaptureIndex = i;
                    pr.ScreenRect = row.ScreenRect;
                    pr.LabelScreenRect = row.ScreenRect;
                    pr.VisibleScreenRect = row.VisibleScreenRect;
                    pr.LabelVisibleScreenRect = row.VisibleScreenRect;
                    pr.Clip = row.Clip;
                    pr.LabelClip = row.Clip;
                    // KindOverride is set 1:1 for an unfused icon-mode dropdown opener (mapping
                    // its InvisibleButton kind to Button so it presents as the ComboBox it is),
                    // an uncaptioned radio (same mapping, with the sniffed state on pr.Selected),
                    // a stepper anchor, and a Listing_Tree row.
                    pr.Kind = fusion[i].KindOverride ?? row.Kind;
                    // CheckOverride also reaches this branch: a
                    // CheckboxLabeledSelectable run is anchored on its own
                    // caption Label, whose capture row carries no check state
                    // of its own (the sniffed texture marker does).
                    pr.Check = ResolveCheckState(fusion[i], row);
                }
                if (fusion[i].ActivateIndex >= 0)
                {
                    // A composite whose visible row and live click target are
                    // different primitives — the ring keeps outlining what is
                    // drawn while injections post on what handles the click.
                    pr.ActivateCaptureIndex = fusion[i].ActivateIndex;
                }
                pr.Selected = fusion[i].SelectedOverride;
                pr.Expanded = fusion[i].ExpandedOverride;
                pr.StepDownCaptureIndex = fusion[i].StepDownIndex;
                pr.StepUpCaptureIndex = fusion[i].StepUpIndex;
                pr.StepCaptions = fusion[i].StepCaptions;
                pr.StepValue = fusion[i].StepValueOverride;
                pr.SecondaryCaptureIndex = fusion[i].SecondaryIndex;
                pr.SuppressGenericTip = fusion[i].SuppressGenericTip;
                pr.ReadOnlyCheckbox = fusion[i].ReadOnlyCheckbox;
                pr.SpeechMasked = fusion[i].SpeechMasked;
                presentationRows.Add(pr);
            }

            // Rich-text cleanup, one seam for every row this method built: a mod's caption can
            // carry RimWorld markup a sighted player never reads as text, which spoken verbatim
            // reads as literal angle-bracket tags. Vanilla's ColoredText.StripTags strips any
            // <...> span generically, so no per-tag list is needed. Deliberately post-loop: the
            // label assignment sites are scattered and this is the one point downstream of all
            // of them. Only the PRESENTATION label is touched — the raw CapturedWidget.Label
            // that PostActivate/PostAdjust/PostAdjustRange match against is never rewritten.
            for (int i = 0; i < presentationRows.Count; i++)
            {
                string label = presentationRows[i].Label;
                if (!string.IsNullOrEmpty(label))
                {
                    presentationRows[i].Label = label.StripTags();
                }
            }

            // Tab-bar grammar: once a window draws a genuine STRIP (>=2 Tab rows), tabs move
            // exclusively into the tab order and leave presentationRows — tabRows, extracted
            // here before any exclusion, is what HasTabBar and CycleTab read. The empty-content
            // guard keeps a window that is nothing BUT a tab strip navigable.
            PromoteHandRolledTabStrip();
            MarkTintSelectedButtonClusters();
            tabRows.Clear();
            for (int i = 0; i < presentationRows.Count; i++)
            {
                if (presentationRows[i].Kind == WidgetKind.Tab)
                {
                    tabRows.Add(presentationRows[i]);
                }
            }
            // Absorb any DISABLED strip member — a
            // research-gated tab a mod draws as a plain textured Label rather
            // than through the widget the strip probe recognizes — into
            // tabRows too, BEFORE the sort below so it lands in its correct
            // visual position among the real tabs rather than at the end.
            AbsorbDisabledStripMembers();
            // Row-major visual order, not draw order: a promoted strip may draw its clusters
            // out of visual sequence. (ScreenRect.y, ScreenRect.x) is also right for a vertical
            // strip, so one unconditional sort covers every strip shape.
            tabRows.Sort((a, b) =>
            {
                int byY = a.ScreenRect.y.CompareTo(b.ScreenRect.y);
                return byY != 0 ? byY : a.ScreenRect.x.CompareTo(b.ScreenRect.x);
            });
            if (tabRows.Count >= 2 && presentationRows.Count > tabRows.Count)
            {
                presentationRows.RemoveAll(r => r.Kind == WidgetKind.Tab || r.DisabledTabStop);
            }

            // Band label inheritance — after every other
            // fusion rule has had its turn (so it only ever sees a row STILL unlabeled), still
            // strictly before the visual-order sort below, so its own left-to-right reasoning
            // reads draw-order X positions exactly like every other geometric rule here.
            ApplyBandLabelInheritance();
            // A SEPARATE rule for a still-unlabeled Slider — see
            // ApplyBandLabelInheritance's own remarks for why this never contorts that one.
            ApplyVerticalSliderLabelInheritance();
            // Embedded vanilla pawn tables (Colony Manager Redux's overview
            // pane): one presentation row per visual pawn row, headers named
            // from the table's own column defs — see ApplyEmbeddedPawnTables.
            ApplyEmbeddedPawnTables();

            // Visual-order pass — strictly AFTER every fusion/exclusion decision above: the
            // backward label searches are written against DRAW order, so re-sorting first would
            // feed them rows in the wrong relation and break those fusions. tabRows holds the
            // same PresentationRow objects, not indices, so reordering here cannot affect it.
            // Last step of the rebuild, so OnGuiPass's model.SetCount clamps the cursor against
            // the already-sorted list.
            SortPresentationRowsByVisualPosition();
            RemoveAdjacentDuplicateLabels();
            ShapeBottomButtonBar();
            PublishSliderNames();
        }

        /// <summary>
        /// Republishes every finished slider caption under vanilla's own drag identity, so the
        /// mouse-drag reader can name a slider whose caller drew the name as a separate widget
        /// (<see cref="SliderCaptionIndex"/>). Last step of the rebuild, once every fusion and
        /// inheritance rule has had its say. Runs on the live draw bracket alone, which keeps a
        /// half-drawn or off-screen pass out of the index.
        /// </summary>
        private void PublishSliderNames()
        {
            for (int i = 0; i < presentationRows.Count; i++)
            {
                PresentationRow row = presentationRows[i];
                if (row.Kind != WidgetKind.Slider || row.Source == null || row.Source.SliderControlId == 0)
                {
                    continue;
                }
                string name = row.Label;
                if (row.CaptionCarriesValue && row.RingCaptureIndex >= 0 && row.RingCaptureIndex < captureRows.Count)
                {
                    // The caption is a folded "name: value" pair, and the drag
                    // reader supplies its own live value — so it wants the
                    // name half, never the composite's stale number.
                    name = captureRows[row.RingCaptureIndex].LabelPairName ?? row.Label;
                }
                SliderCaptionIndex.Publish(row.Source.SliderControlId, name, row.CaptionCarriesValue);
            }
        }

        /// <summary>
        /// Gives an unlabeled twin control the caption it visually shares with a labeled
        /// neighbour: on a "Label + checkbox + checkbox" row, ordinary fusion attaches the
        /// caption only to the nearest checkbox, leaving the partner nameless. The geometry
        /// judgement lives in <see cref="BandLabelInheritance"/>; this translates
        /// <see cref="presentationRows"/> into its input and applies its verdicts.
        /// </summary>
        private void ApplyBandLabelInheritance()
        {
            int n = presentationRows.Count;
            if (n == 0)
            {
                return;
            }
            var candidates = new BandLabelCandidate[n];
            var clipIds = new Dictionary<GuiSpace.ClipKey, int>();
            for (int i = 0; i < n; i++)
            {
                PresentationRow pr = presentationRows[i];
                int clipId;
                if (!clipIds.TryGetValue(pr.Clip, out clipId))
                {
                    clipId = clipIds.Count;
                    clipIds.Add(pr.Clip, clipId);
                }
                candidates[i] = new BandLabelCandidate
                {
                    Interactive = pr.Kind == WidgetKind.Button || pr.Kind == WidgetKind.Checkbox || pr.Kind == WidgetKind.RadioButton,
                    HasLabel = !string.IsNullOrWhiteSpace(pr.Label),
                    ClipId = clipId,
                    X = pr.ScreenRect.x,
                    Y = pr.ScreenRect.y,
                    Width = pr.ScreenRect.width,
                    Height = pr.ScreenRect.height,
                };
            }
            IReadOnlyList<BandLabelVerdict> verdicts = BandLabelInheritance.Compute(candidates);
            for (int i = 0; i < n; i++)
            {
                if (verdicts[i].DonorIndex < 0)
                {
                    continue;
                }
                string donorLabel = presentationRows[verdicts[i].DonorIndex].Label;
                presentationRows[i].Label = "RimWorldAccess.Shell.BandOrdinalLabel"
                    .Translate(donorLabel, verdicts[i].Ordinal).ToString();
            }
        }

        /// <summary>
        /// Embedded vanilla pawn tables. A window hosting a live <see cref="PawnTable"/> draws
        /// it through <c>PawnTable.PawnTableOnGUI</c>, which
        /// <see cref="WidgetCapture.RecordPawnTable"/> notes with the table's geometry. Two
        /// consequences, both scoped to rows that provably belong to that table:
        /// <list type="bullet">
        /// <item>BODY rows fuse into one presentation row per visual pawn row
        /// (<see cref="EmbeddedTableFusion"/>); the table draws column-major, so one pawn's name
        /// label, click target and activity cell arrive as three unrelated-looking rows.</item>
        /// <item>HEADER rows rename from the table's own column defs when the drawn text is a
        /// truncation — the def is what the game MEANS by the header. Accepted only when the
        /// drawn text is a prefix of it or pure ellipsis, so a full header is never touched.</item>
        /// </list>
        /// </summary>
        private void ApplyEmbeddedPawnTables()
        {
            IReadOnlyList<WidgetCapture.PawnTableNote> notes = WidgetCapture.PawnTableNotes;
            if (notes.Count == 0)
            {
                return;
            }
            for (int t = 0; t < notes.Count; t++)
            {
                WidgetCapture.PawnTableNote note = notes[t];
                RenamePawnTableHeaders(note);
                FusePawnTableBody(note);
            }
        }

        private void FusePawnTableBody(WidgetCapture.PawnTableNote note)
        {
            int containerIndex = FindTableBodyContainer(note);
            if (containerIndex < 0)
            {
                return;
            }
            var memberRows = new List<PresentationRow>();
            for (int i = 0; i < presentationRows.Count; i++)
            {
                PresentationRow row = presentationRows[i];
                if (row.Source != null && row.Source.ContainerIndex == containerIndex
                    && row.Kind != WidgetKind.Tab && !row.DisabledTabStop)
                {
                    memberRows.Add(row);
                }
            }
            if (memberRows.Count < 2)
            {
                return;
            }
            var cells = new TableCellCandidate[memberRows.Count];
            for (int i = 0; i < memberRows.Count; i++)
            {
                PresentationRow row = memberRows[i];
                cells[i] = new TableCellCandidate
                {
                    X = row.ScreenRect.x,
                    Y = row.ScreenRect.y,
                    Width = row.ScreenRect.width,
                    Height = row.ScreenRect.height,
                    Interactive = row.Kind != WidgetKind.Label && row.Kind != WidgetKind.FillableBar,
                    Label = row.Label,
                };
            }
            IReadOnlyList<TableRowGroup> groups = EmbeddedTableFusion.Compute(cells);
            var absorbed = new HashSet<PresentationRow>();
            foreach (TableRowGroup group in groups)
            {
                if (group.Members.Count < 2)
                {
                    continue;
                }
                PresentationRow primary = memberRows[group.PrimaryIndex];
                if (!string.IsNullOrWhiteSpace(group.FusedLabel))
                {
                    primary.Label = group.FusedLabel;
                }
                for (int m = 0; m < group.Members.Count; m++)
                {
                    PresentationRow member = memberRows[group.Members[m]];
                    if (!ReferenceEquals(member, primary))
                    {
                        absorbed.Add(member);
                    }
                }
            }
            if (absorbed.Count > 0)
            {
                presentationRows.RemoveAll(absorbed.Contains);
            }
        }

        /// <summary>The ScrollContainer the note's body viewport corresponds to, or -1: vanilla passed the SAME outRect to Widgets.BeginScrollView that the note derived from the table's own size, so the two agree to capture precision.</summary>
        private static int FindTableBodyContainer(WidgetCapture.PawnTableNote note)
        {
            IReadOnlyList<WidgetCapture.ScrollContainer> containers = WidgetCapture.ScrollContainers;
            for (int i = 0; i < containers.Count; i++)
            {
                Rect a = containers[i].OutRectScreen;
                Rect b = note.BodyOutRectScreen;
                if (Mathf.Abs(a.x - b.x) <= 2f && Mathf.Abs(a.y - b.y) <= 2f
                    && Mathf.Abs(a.width - b.width) <= 2f && Mathf.Abs(a.height - b.height) <= 2f)
                {
                    return containers[i].Index;
                }
            }
            return -1;
        }

        private void RenamePawnTableHeaders(WidgetCapture.PawnTableNote note)
        {
            if (note.ColumnDefs.Count == 0)
            {
                return;
            }
            for (int i = 0; i < presentationRows.Count; i++)
            {
                PresentationRow row = presentationRows[i];
                Vector2 center = row.ScreenRect.center;
                if (!note.HeaderRectScreen.ExpandedBy(1f).Contains(center))
                {
                    continue;
                }
                for (int c = 0; c < note.ColumnDefs.Count; c++)
                {
                    if (!note.ColumnHeaderRectsScreen[c].ExpandedBy(1f).Contains(center))
                    {
                        continue;
                    }
                    string resolved = ResolveColumnHeaderName(note.Table, note.ColumnDefs[c], row.Label);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        row.Label = resolved;
                    }
                    break;
                }
            }
        }

        private static readonly Dictionary<Type, MethodInfo> headerTipMethods = new Dictionary<Type, MethodInfo>();

        /// <summary>
        /// The untruncated name for a header cell, from the column's own decision objects: the
        /// first non-empty line of the worker's GetHeaderTip, then the def's LabelCap. Accepted
        /// only when the DRAWN text vouches for it — an ellipsis-stripped case-insensitive
        /// prefix, or drawn text that is pure ellipsis — so a fully-readable header, or a tip
        /// whose first line is unrelated boilerplate, is never rewritten.
        /// </summary>
        private static string ResolveColumnHeaderName(PawnTable table, PawnColumnDef def, string drawnLabel)
        {
            string drawn = (drawnLabel ?? "").StripTags().Trim();
            string drawnCore = drawn.TrimEnd('.', '…', '-', ' ');
            bool drawnIsEllipsisOnly = drawnCore.Length == 0 && drawn.Length > 0;
            try
            {
                foreach (string candidate in HeaderNameCandidates(table, def))
                {
                    string name = (candidate ?? "").StripTags().Trim();
                    if (string.IsNullOrWhiteSpace(name) || name == drawn)
                    {
                        continue;
                    }
                    bool vouched = drawnIsEllipsisOnly
                        || (drawnCore.Length > 0
                            && name.StartsWith(drawnCore, StringComparison.OrdinalIgnoreCase));
                    if (vouched)
                    {
                        return name;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("ResolveColumnHeaderName", ex);
            }
            return null;
        }

        private static IEnumerable<string> HeaderNameCandidates(PawnTable table, PawnColumnDef def)
        {
            PawnColumnWorker worker = def.Worker;
            if (worker != null)
            {
                MethodInfo tipMethod;
                Type workerType = worker.GetType();
                if (!headerTipMethods.TryGetValue(workerType, out tipMethod))
                {
                    tipMethod = AccessTools.Method(workerType, "GetHeaderTip");
                    headerTipMethods[workerType] = tipMethod;
                }
                string tip = null;
                if (tipMethod != null)
                {
                    try
                    {
                        tip = tipMethod.Invoke(worker, new object[] { table }) as string;
                    }
                    catch
                    {
                        tip = null;
                    }
                }
                if (!string.IsNullOrEmpty(tip))
                {
                    foreach (string line in tip.Split('\n'))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            yield return line;
                            break;
                        }
                    }
                }
            }
            if (!string.IsNullOrEmpty(def.label))
            {
                yield return def.LabelCap.ToString();
            }
        }

        /// <summary>
        /// Gives a still-unlabeled Slider the caption of the labeled row directly ABOVE it,
        /// for a slider whose caller drew no caption of its own and would otherwise announce as
        /// a bare "slider, 500". The geometry judgement lives in
        /// <see cref="VerticalLabelInheritance"/>; this translates
        /// <see cref="presentationRows"/> into its input and applies its verdicts. A donor's
        /// label is used AS-IS (no ordinal): there is exactly one recipient. Falls back to the
        /// row's own <see cref="PresentationRow.Section"/> when no qualifying row sits above.
        /// </summary>
        private void ApplyVerticalSliderLabelInheritance()
        {
            int n = presentationRows.Count;
            if (n == 0)
            {
                return;
            }
            var candidates = new VerticalLabelCandidate[n];
            var clipIds = new Dictionary<GuiSpace.ClipKey, int>();
            for (int i = 0; i < n; i++)
            {
                PresentationRow pr = presentationRows[i];
                int clipId;
                if (!clipIds.TryGetValue(pr.Clip, out clipId))
                {
                    clipId = clipIds.Count;
                    clipIds.Add(pr.Clip, clipId);
                }
                candidates[i] = new VerticalLabelCandidate
                {
                    Eligible = pr.Kind == WidgetKind.Slider,
                    HasLabel = !string.IsNullOrWhiteSpace(pr.Label),
                    ClipId = clipId,
                    X = pr.ScreenRect.x,
                    Y = pr.ScreenRect.y,
                    Width = pr.ScreenRect.width,
                    Height = pr.ScreenRect.height,
                };
            }
            IReadOnlyList<int> donors = VerticalLabelInheritance.ComputeDonors(candidates);
            for (int i = 0; i < n; i++)
            {
                if (donors[i] >= 0)
                {
                    presentationRows[i].Label = presentationRows[donors[i]].Label;
                }
                else if (presentationRows[i].Kind == WidgetKind.Slider
                    && string.IsNullOrWhiteSpace(presentationRows[i].Label)
                    && !string.IsNullOrEmpty(presentationRows[i].Section))
                {
                    presentationRows[i].Label = presentationRows[i].Section;
                }
            }
        }

    }
}
