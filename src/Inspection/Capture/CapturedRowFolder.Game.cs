using System;
using System.Collections.Generic;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>One readable line folded from a captured visual row.</summary>
    public sealed class FoldedRow
    {
        /// <summary>The row's fragments, left-to-right, comma-joined.</summary>
        public string Text = "";

        /// <summary>Tooltip(s) registered on the row's widgets, or null.</summary>
        public string Tip;

        /// <summary>
        /// The row's operable controls, left-to-right, or null when it has none. Each can be
        /// re-found and fired by an armed capture pass via its descriptor.
        /// </summary>
        public List<InteractiveMember> Interactives;

        /// <summary>
        /// The row's ordinal among <see cref="CapturedRowBander"/>'s bands — the identity a live
        /// geometry pass re-bands to, so the focus ring can find the row's real rect. Band ordinals,
        /// not row ordinals: <see cref="Fold"/> drops bands that fold to nothing and the parity diff
        /// folds only some, so neither list's own index survives.
        /// </summary>
        public int BandIndex = -1;
    }

    /// <summary>
    /// One operable control on a folded row: its <see cref="CaptureDescriptor"/> fields plus the
    /// rendered fragment used to speak it.
    /// </summary>
    public sealed class InteractiveMember
    {
        public WidgetKind Kind;
        public string RawLabel = "";
        public int Ordinal;
        public string Fragment = "";

        /// <summary>
        /// The captured widget this member was folded from, so the presentation layer can read a
        /// slider's value/bounds and a text field's text and multi-line flag.
        /// </summary>
        public CapturedWidget Source;
    }

    /// <summary>
    /// Folds a detached capture pass's widget stream into readable lines: bands it into visual rows
    /// via <see cref="CapturedRowBander"/>, renders each fragment through the same
    /// <see cref="AnnouncementComposer"/> vocabulary the generic window reader speaks with, and
    /// resolves each row's tooltip from the detached TooltipCapture index. Call immediately after
    /// the capture pass closes, in the same frame — the detached tip index is only valid until the
    /// next detached pass begins.
    /// </summary>
    public static class CapturedRowFolder
    {
        public static List<FoldedRow> Fold(List<CapturedWidget> widgets)
        {
            return Fold(widgets, out _);
        }

        /// <summary>
        /// <see cref="Fold"/>, also reporting how many bands the stream held — the count a live
        /// geometry pass must reproduce before its bands can be trusted to line up with these rows'
        /// <see cref="FoldedRow.BandIndex"/>.
        /// </summary>
        public static List<FoldedRow> Fold(List<CapturedWidget> widgets, out int bandCount)
        {
            var rows = new List<FoldedRow>();
            List<FoldedRow> bands = FoldBands(widgets);
            bandCount = bands.Count;
            foreach (FoldedRow row in bands)
            {
                if (row != null)
                {
                    rows.Add(row);
                }
            }
            return rows;
        }

        /// <summary>
        /// Folds each banded visual row in <see cref="CapturedRowBander.Band"/>'s order: a band that
        /// folds to nothing readable yields a null slot, so a caller can index by band ordinal. Must
        /// run at capture time — the tooltip resolution only holds until the next detached pass.
        /// </summary>
        public static List<FoldedRow> FoldBands(List<CapturedWidget> widgets)
        {
            var rows = new List<FoldedRow>();
            if (widgets == null || widgets.Count == 0)
            {
                return rows;
            }

            foreach (List<int> band in BandsFor(widgets))
            {
                FoldedRow row = FoldBand(widgets, band);
                if (row != null)
                {
                    row.BandIndex = rows.Count;
                }
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// Bands a captured stream into visual rows — the ONE entry point every caller uses, so this
        /// folder and <see cref="CaptureParityDiff"/> can never disagree about where a row starts.
        /// </summary>
        internal static List<List<int>> BandsFor(List<CapturedWidget> widgets)
        {
            // The bander's tolerances are UI points, the space screen rects are recorded in at any
            // Prefs.UIScale.
            return CapturedRowBander.Band(GeomsFor(widgets));
        }

        /// <summary>
        /// The banding geometry of each captured widget: group-local left edge plus the screen edge
        /// and clip context that decide which widgets describe the same visual row. This is the
        /// boundary where the game-side <see cref="GuiSpace.ClipKey"/> becomes the pure
        /// <see cref="TipClip"/> the bander compares.
        /// </summary>
        private static BandGeom[] GeomsFor(List<CapturedWidget> widgets)
        {
            var geoms = new BandGeom[widgets.Count];
            for (int i = 0; i < widgets.Count; i++)
            {
                CapturedWidget widget = widgets[i];
                geoms[i] = new BandGeom(widget.Rect.x, widget.ScreenRect.y,
                    ToTipClip(widget.Clip), HasClipContext(widget.Clip));
            }
            return geoms;
        }

        private static TipClip ToTipClip(GuiSpace.ClipKey clip)
        {
            Rect rect = clip.VisibleRect;
            return new TipClip(new TipRect(rect.x, rect.y, rect.width, rect.height), clip.Depth);
        }

        /// <summary>
        /// Whether a recorded clip key carries real context. GuiSpace.CurrentClip returns
        /// <c>default(ClipKey)</c> when its reflection bindings degraded; a real depth-0 context is
        /// the whole screen, so an empty visible rect is the unambiguous "no context" marker.
        /// </summary>
        private static bool HasClipContext(GuiSpace.ClipKey clip)
        {
            return clip.Depth != 0 || clip.VisibleRect.width > 0f || clip.VisibleRect.height > 0f;
        }

        /// <summary>
        /// Folds ONE banded visual row into a readable line, or null when the band carries no
        /// readable fragment.
        /// </summary>
        public static FoldedRow FoldBand(List<CapturedWidget> widgets, List<int> band)
        {
            // Live tooltip resolution, valid only while this pass's detached tip index is current.
            // Resolved in SCREEN space with clip-context equality, never by group-local rect: local
            // coordinates restart near (0,0) inside every nested GUI group, so unrelated groups'
            // registrations overlap numerically and TipIndex.Resolve joins every hit with ". ".
            return FoldCore(widgets, band,
                index => TooltipCapture.TryResolveDetachedAtScreen(widgets[index].ScreenRect, widgets[index].Clip));
        }

        /// <summary>
        /// Folds an arbitrary subset of a band with tooltips PRE-RESOLVED at capture time: by the
        /// time the parity diff runs, later captures have overwritten the detached tip index, so
        /// <paramref name="widgetTips"/> must be aligned to <paramref name="widgets"/>.
        /// </summary>
        public static FoldedRow FoldSubset(List<CapturedWidget> widgets, List<int> subset, IList<string> widgetTips)
        {
            return FoldCore(widgets, subset, index => widgetTips[index]);
        }

        private static FoldedRow FoldCore(List<CapturedWidget> widgets, List<int> band, Func<int, string> tipAt)
        {
            // Defense in depth: WidgetCapture suppresses a self-captioned widget's duplicate Label
            // at the source, but a mod drawing that caption as its own separate Label call would
            // still dup here. Drop a bare Label whose text a control row already carries.
            var controlLabels = new HashSet<string>();
            foreach (int index in band)
            {
                CapturedWidget widget = widgets[index];
                if (widget.Kind != WidgetKind.Label && widget.Kind != WidgetKind.FillableBar
                    && !string.IsNullOrEmpty(widget.Label))
                {
                    controlLabels.Add(widget.Label);
                }
            }

            // The whole-band check above catches a mod repeating its RAW caption exactly, but not
            // the same spoken text arriving with different markup between two draws of one control
            // family. Positions here are ADJACENT-ONLY, so a same-text Label elsewhere in the band —
            // a genuinely distinct repeated word in prose — is never touched.
            HashSet<int> adjacentDuplicateLabelPositions = FindAdjacentDuplicateLabels(widgets, band);

            // FillableBar caption fusion: a hand-rolled "LabelDouble" draws a caption Label on the
            // left half of a row and a bare Widgets.FillableBar on the right, edge-contiguous, same
            // Y. CaptureParityDiff's "Also shown" pass excludes bare FillableBars outright, so this
            // fold must consume the caption's fragment and hand it to the bar rather than leave two
            // widgets a diff could split. FillableBarLabeled carries its own caption and is excluded.
            Dictionary<int, int> fillableBarCaptionByPosition = FindFillableBarCaptions(widgets, band);
            HashSet<int> captionPositionsConsumed = fillableBarCaptionByPosition != null
                ? new HashSet<int>(fillableBarCaptionByPosition.Values)
                : null;

            // The band's first plain-text fragment, read BEFORE the main pass so a glyph-only button
            // appearing earlier in draw order can still be qualified by it.
            string bandLeadingText = FindBandLeadingText(widgets, band, controlLabels);

            var fragments = new List<string>();
            var tips = new List<string>();
            // Ordinals count against the FULL captured stream, matching the armed matcher's own
            // counting so both sides re-find the same widget.
            List<InteractiveMember> interactives = null;
            bool firstFragmentIsGlyphButton = false;
            int firstPlainTextFragmentIndex = -1;
            for (int p = 0; p < band.Count; p++)
            {
                int index = band[p];
                CapturedWidget widget = widgets[index];
                if (widget.Kind == WidgetKind.Label && controlLabels.Contains(widget.Label))
                {
                    continue;
                }
                if (widget.Kind == WidgetKind.Label && adjacentDuplicateLabelPositions != null
                    && adjacentDuplicateLabelPositions.Contains(p))
                {
                    continue;
                }
                if (captionPositionsConsumed != null && captionPositionsConsumed.Contains(p))
                {
                    continue; // folded into the following FillableBar's fragment below
                }
                // Resolved before the fragment so RenderFragment can skip folding widget.Tip on when
                // the row-level tip channel already carries the identical text.
                string tip = tipAt(index);
                MaybeNameFromLateTip(widget, tip);
                string fragment = RenderFragment(widget, tip, bandLeadingText);
                if (fillableBarCaptionByPosition != null
                    && fillableBarCaptionByPosition.TryGetValue(p, out int captionPosition)
                    && !string.IsNullOrWhiteSpace(fragment))
                {
                    string captionText = widgets[band[captionPosition]].SpokenLabel;
                    fragment = ComposeCaptionValue(captionText, fragment);
                }
                if (!string.IsNullOrWhiteSpace(fragment))
                {
                    if (fragments.Count == 0 && GlyphButtonName.IsGlyphOnlyName(widget.SpokenLabel))
                    {
                        firstFragmentIsGlyphButton = true;
                    }
                    if (widget.Kind == WidgetKind.Label && firstPlainTextFragmentIndex == -1)
                    {
                        firstPlainTextFragmentIndex = fragments.Count;
                    }
                    fragments.Add(fragment.Trim());
                }
                // A FilterPanelHandoff row stays WidgetKind.Label but is operable: activation opens
                // our full filter screen rather than re-firing a vanilla widget.
                if ((CaptureDescriptor.IsInteractiveKind(widget.Kind) || widget.Composite == CompositeMember.FilterPanelHandoff)
                    && !string.IsNullOrWhiteSpace(fragment))
                {
                    (interactives = interactives ?? new List<InteractiveMember>()).Add(new InteractiveMember
                    {
                        Kind = widget.Kind,
                        RawLabel = widget.Label ?? "",
                        Ordinal = CaptureDescriptor.OrdinalOf(widgets, index),
                        Fragment = fragment.Trim(),
                        Source = widget,
                    });
                }
                if (!string.IsNullOrWhiteSpace(tip) && !tips.Contains(tip) && !TipIsRowName(widget, tip))
                {
                    tips.Add(tip);
                }
            }

            // A glyph-only button leading the band would otherwise be the first thing spoken; move
            // the band's own text fragment to the front. Pure reorder — no bookkeeping changes.
            if (firstFragmentIsGlyphButton && firstPlainTextFragmentIndex > 0)
            {
                string leadingText = fragments[firstPlainTextFragmentIndex];
                fragments.RemoveAt(firstPlainTextFragmentIndex);
                fragments.Insert(0, leadingText);
            }

            if (fragments.Count == 0)
            {
                return null;
            }
            return new FoldedRow
            {
                Text = string.Join(", ", fragments),
                Tip = tips.Count > 0 ? string.Join(" ", tips) : null,
                Interactives = interactives,
            };
        }

        /// <summary>
        /// Maps a FillableBar's position within <paramref name="band"/> to its caption Label's
        /// position; null when the band has none.
        /// </summary>
        private static Dictionary<int, int> FindFillableBarCaptions(List<CapturedWidget> widgets, List<int> band)
        {
            Dictionary<int, int> result = null;
            for (int p = 1; p < band.Count; p++)
            {
                CapturedWidget bar = widgets[band[p]];
                if (bar.Kind != WidgetKind.FillableBar || !string.IsNullOrEmpty(bar.FieldLabel))
                {
                    continue;
                }
                CapturedWidget caption = widgets[band[p - 1]];
                if (caption.Kind != WidgetKind.Label || string.IsNullOrWhiteSpace(caption.SpokenLabel))
                {
                    continue;
                }
                if (!IsHorizontallyAdjacent(caption.Rect, bar.Rect))
                {
                    continue;
                }
                (result = result ?? new Dictionary<int, int>())[p] = p - 1;
            }
            return result;
        }

        /// <summary>Edge-contiguous, same tolerance as WidgetCapture's own label-pair fold geometry.</summary>
        private static bool IsHorizontallyAdjacent(Rect left, Rect right)
        {
            return Mathf.Abs(right.x - left.xMax) <= 2f;
        }

        /// <summary>
        /// Composes a caption/value pair through the same shared key WidgetCapture uses at capture
        /// time, trimming a caller-drawn trailing colon so the row never doubles the separator.
        /// </summary>
        internal static string ComposeCaptionValue(string caption, string value)
        {
            string trimmed = (caption ?? "").TrimEnd();
            if (trimmed.EndsWith(":"))
            {
                caption = trimmed.Substring(0, trimmed.Length - 1);
            }
            return "RimWorldAccess.UI.GenericWindow.LabelPair".Translate(caption, value).ToString();
        }

        /// <summary>
        /// Band positions of a bare Label fragment immediately adjacent to an interactive member
        /// whose own spoken name, tag-stripped, is the exact same text.
        /// </summary>
        private static HashSet<int> FindAdjacentDuplicateLabels(List<CapturedWidget> widgets, List<int> band)
        {
            HashSet<int> result = null;
            for (int p = 0; p < band.Count; p++)
            {
                CapturedWidget widget = widgets[band[p]];
                if (widget.Kind != WidgetKind.Label || string.IsNullOrWhiteSpace(widget.SpokenLabel))
                {
                    continue;
                }
                string stripped = widget.SpokenLabel.StripTags();
                if (HasAdjacentInteractiveMatch(widgets, band, p - 1, stripped)
                    || HasAdjacentInteractiveMatch(widgets, band, p + 1, stripped))
                {
                    (result = result ?? new HashSet<int>()).Add(p);
                }
            }
            return result;
        }

        private static bool HasAdjacentInteractiveMatch(List<CapturedWidget> widgets, List<int> band, int position, string strippedLabelText)
        {
            if (position < 0 || position >= band.Count)
            {
                return false;
            }
            CapturedWidget other = widgets[band[position]];
            if (!CaptureDescriptor.IsInteractiveKind(other.Kind) || string.IsNullOrWhiteSpace(other.SpokenLabel))
            {
                return false;
            }
            return string.Equals(other.SpokenLabel.StripTags(), strippedLabelText, StringComparison.Ordinal);
        }

        /// <summary>The band's first plain-text fragment, or null — the glyph-button qualifier's context.</summary>
        private static string FindBandLeadingText(List<CapturedWidget> widgets, List<int> band, HashSet<string> controlLabels)
        {
            foreach (int index in band)
            {
                CapturedWidget widget = widgets[index];
                if (widget.Kind == WidgetKind.Label && !controlLabels.Contains(widget.Label)
                    && !string.IsNullOrWhiteSpace(widget.SpokenLabel))
                {
                    return widget.SpokenLabel;
                }
            }
            return null;
        }

        /// <summary>
        /// LATE tooltip naming: an icon-only button whose caller registered its TipRegion AFTER
        /// drawing it cannot be named by WidgetCapture's synchronous tooltip tier, so it arrives
        /// still carrying its raw texture name and a <see cref="CapturedWidget.NeedsLateTipName"/>
        /// flag. Folding runs after the pass closed, when every registration is in.
        /// The tip must come through the caller's <c>tipAt</c> channel and never be re-queried live:
        /// a deferred fold must not be able to read a LATER pass's index. On failure the texture
        /// name stands. <see cref="CapturedWidget.Label"/> is deliberately left alone — only the
        /// spoken name changes, since both activation channels re-find a widget by that label.
        /// </summary>
        private static void MaybeNameFromLateTip(CapturedWidget widget, string tip)
        {
            if (!widget.NeedsLateTipName || string.IsNullOrWhiteSpace(tip))
            {
                return;
            }
            string name = WidgetCapture.ShortTipName(tip);
            if (string.IsNullOrEmpty(name))
            {
                return;
            }
            widget.TipName = name;
            // The row now takes its name from this tooltip, so TipIsRowName's suppression applies.
            widget.NameFromTip = true;
            widget.NeedsLateTipName = false;
        }

        /// <summary>
        /// Whether this row's tooltip is already the widget's spoken NAME: an icon-only button takes
        /// its name from the tooltip registered over it, and repeating that text as the row's tip
        /// would speak it twice. Only an EXACT match is suppressed, so when the name is a shortened
        /// first line the full tooltip still rides the row.
        /// </summary>
        private static bool TipIsRowName(CapturedWidget widget, string tip)
        {
            string name = widget.SpokenLabel;
            return widget.NameFromTip && !string.IsNullOrEmpty(name)
                && string.Equals(tip.Trim(), name, StringComparison.Ordinal);
        }

        /// <param name="rowTip">
        /// The row's tip already resolved via the detached tip channel, compared against
        /// <see cref="CapturedWidget.Tip"/> so a widget's own tooltip is folded onto its fragment
        /// only when it differs.
        /// </param>
        /// <param name="bandLeadingText">
        /// The band's leading plain-text fragment, or null; qualifies a punctuation-only button name.
        /// </param>
        private static string RenderFragment(CapturedWidget widget, string rowTip, string bandLeadingText = null)
        {
            // A synthetic row standing in for a mod's own ThingFilterUI panel. Kind stays Label at
            // capture time; here it presents as one operable Button-shaped fragment whose activation
            // opens our own filter screen.
            if (widget.Composite == CompositeMember.FilterPanelHandoff)
            {
                var filterDescription = new ElementDescription
                {
                    Label = "RimWorldAccess.Inspection.Tree.EditFilterList".Translate(),
                    Role = ElementRole.Button,
                };
                return AnnouncementComposer.ComposeFocus(filterDescription, TranslatedShellVocabulary.Instance, default(ComposeOptions));
            }

            // SpokenLabel throughout, never Label: a button named by the late tooltip retry speaks
            // that name while its capture-time label stays intact for re-finding. Plain text and
            // bars need no role vocabulary — a label IS its text, a bar its fill fraction.
            if (widget.Kind == WidgetKind.Label)
            {
                return widget.SpokenLabel;
            }
            if (widget.Kind == WidgetKind.FillableBar)
            {
                string barFragment = widget.FillPercent.ToStringPercent();
                if (widget.BarChangeRate != 0)
                {
                    // The arrows vanilla draws beside the bar are the only rendering of the rate a
                    // sighted player gets.
                    string rate = (widget.BarChangeRate > 0
                        ? "RimWorldAccess.UI.GenericWindow.BarRising"
                        : "RimWorldAccess.UI.GenericWindow.BarFalling").Translate().ToString();
                    barFragment = barFragment + ". " + rate;
                }
                return barFragment;
            }

            // A name that is nothing but punctuation is meaningless read alone, so it borrows the
            // band's own text as context. Keyed off the name's shape, never a specific mod's strings.
            string spokenName = widget.SpokenLabel;
            if (!string.IsNullOrEmpty(bandLeadingText) && GlyphButtonName.IsGlyphOnlyName(spokenName))
            {
                spokenName = "RimWorldAccess.Inspection.Tree.GlyphButtonQualifiedName".Translate(bandLeadingText, spokenName).ToString();
            }

            // The vanilla widget-level gate rides the fragment, as a sighted player sees the grey.
            var d = new ElementDescription { Label = spokenName, Disabled = widget.Disabled };
            bool carriesFieldLabel = false;
            switch (widget.Kind)
            {
                case WidgetKind.Checkbox:
                    d.Role = ElementRole.Checkbox;
                    // A CheckboxMulti row carries the vanilla tri-state; collapsing Partial to
                    // "unchecked" would misreport it.
                    d.Check = widget.TriState == MultiCheckboxState.Partial
                        ? CheckState.PartiallyChecked
                        : (widget.Checked ? CheckState.Checked : CheckState.Unchecked);
                    break;
                case WidgetKind.RadioButton:
                    d.Role = ElementRole.RadioButton;
                    d.Selected = widget.Selected;
                    break;
                case WidgetKind.Tab:
                    d.Role = ElementRole.Tab;
                    d.Selected = widget.Selected;
                    if (!string.IsNullOrEmpty(widget.Tip))
                    {
                        d.Extras = widget.Tip;
                    }
                    break;
                case WidgetKind.Button:
                    // A vanilla Widgets.Dropdown opener reads as a combo box.
                    d.Role = widget.DropdownOpener ? ElementRole.ComboBox : ElementRole.Button;
                    // A stepper bracket's +/- button carries the bracket's caption, never its own
                    // blank capture label.
                    carriesFieldLabel = widget.StepperButton;
                    break;
                case WidgetKind.Slider:
                    d.Role = ElementRole.Slider;
                    // A non-blank captured label means the mod formatted the value into it itself,
                    // already spoken via d.Label, so appending the raw float would double the
                    // number. A blank label carries no value, leaving the raw number the only
                    // rendering — this folder does no caption fusion.
                    if (string.IsNullOrEmpty(widget.Label))
                    {
                        bool wholeNumber = Mathf.Approximately(widget.SliderValue, Mathf.Round(widget.SliderValue));
                        d.Value = wholeNumber ? widget.SliderValue.ToString("F0") : widget.SliderValue.ToString("F2");
                    }
                    break;
                case WidgetKind.TextField:
                    d.Role = ElementRole.TextField;
                    if (string.IsNullOrEmpty(widget.Text))
                    {
                        d.ValueBlank = true;
                    }
                    else
                    {
                        d.Value = widget.Text;
                    }
                    carriesFieldLabel = true;
                    break;
                default:
                    return spokenName;
            }

            // Fold the widget's own tooltip onto every other kind as the Tab case does, but only
            // when it differs from the row-level tip, so a row never speaks one tooltip twice.
            if (widget.Kind != WidgetKind.Tab && !string.IsNullOrEmpty(widget.Tip) && widget.Tip != rowTip)
            {
                d.Extras = widget.Tip;
            }

            // Default options: no position and no interaction hints — a folded readout is static.
            string composed = AnnouncementComposer.ComposeFocus(d, TranslatedShellVocabulary.Instance, default(ComposeOptions));
            if (carriesFieldLabel && !string.IsNullOrEmpty(widget.FieldLabel))
            {
                composed = $"{widget.FieldLabel}: {composed}";
            }
            return composed;
        }
    }
}
