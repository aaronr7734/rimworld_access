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
        // Announcements

        /// <summary>Speaks the focused row through the chassis; a window with nothing readable says so rather than composing an empty region.</summary>
        private void AnnounceCurrent()
        {
            int focused = RowIndex;
            if (focused < 0 || focused >= presentationRows.Count)
            {
                TolkHelper.Speak("RimWorldAccess.UI.GenericWindow.NoElements".Loc());
                pendingTabFramePrefix = null;
                entryTabFramePending = false;
                return;
            }
            AnnounceCurrentItem();
        }

        /// <summary>
        /// This screen's boundary context, spoken ahead of the row: window title on the first
        /// announcement after a push, then the tab frame, then the row's section the first time a row
        /// from it is read. The one-shot flags are consumed here, on the path that actually speaks —
        /// never in DescribeContentItem, which also runs from the typeahead haystack and debug dump.
        /// </summary>
        protected override string AnnouncePrefix(int region, int index)
        {
            if (index < 0 || index >= presentationRows.Count)
            {
                return null;
            }
            PresentationRow row = presentationRows[index];
            string prefix = SectionPrefixFor(row);

            // A tab-frame prefix wins either because CycleTab armed one for the switch it just made,
            // or because this is a fresh focus on a window with a promoted strip. Cleared
            // unconditionally so neither can leak onto a later announcement.
            string tabFrame = pendingTabFramePrefix;
            pendingTabFramePrefix = null;
            if (tabFrame == null && entryTabFramePending && HasTabBar())
            {
                tabFrame = BuildEntryTabFrameText();
            }
            entryTabFramePending = false;
            prefix = Join(tabFrame, prefix);

            if (announceTitleNext)
            {
                announceTitleNext = false;
                string title = ResolveWindowTitle();
                // Skip the title when the landing row already IS it: a content-less Medium-font
                // title demotes to row 0, so prepending would speak the same name twice.
                if (!string.IsNullOrEmpty(title) && title != row.Label)
                {
                    prefix = Join(title, prefix);
                }
            }
            return prefix;
        }

        private static string Join(string lead, string rest)
        {
            if (string.IsNullOrEmpty(lead))
            {
                return rest;
            }
            return string.IsNullOrEmpty(rest) ? lead : lead + ". " + rest;
        }

        /// <summary>
        /// The ONE utterance naming a tab's position in its strip ("{tab}. tab. {i} of {n}"), shared by
        /// <see cref="CycleTab"/> and <see cref="BuildEntryTabFrameText"/>. No selected/not-selected
        /// word: the landing IS the selection, and the captured flag still reads the pre-switch state
        /// until an injected click lands. An absorbed DISABLED stop overrides role/state to
        /// "tab, disabled" and speaks the row's captured label as-is, never re-derived.
        /// </summary>
        private string BuildTabFrameText(PresentationRow row, int tabPosition, int tabCount)
        {
            ElementDescription d = BuildDescription(row);
            d.PositionIndex = tabPosition;
            d.PositionCount = tabCount;
            d.Selected = null;
            if (row.DisabledTabStop)
            {
                d.Role = ElementRole.Tab;
                d.Disabled = true;
            }
            return AnnouncementComposer.ComposeFocus(d, TranslatedShellVocabulary.Instance, TextDialogShared.StandardComposeOptions());
        }

        /// <summary>
        /// The single utterance for a Tab/Shift+Tab switch: the tab's own row announcement positioned
        /// within the STRIP ("2 of 3") rather than the window's whole row list. Speaks immediately;
        /// only <see cref="CycleTab"/>'s DISABLED-stop branch still calls it, since landing there
        /// triggers no page rebuild for a later pass to announce.
        /// </summary>
        private void AnnounceTabSwitch(PresentationRow row, int tabPosition, int tabCount)
        {
            TolkHelper.SpeakData(ApplySectionPrefix(row, BuildTabFrameText(row, tabPosition, tabCount)));
        }

        /// <summary>
        /// The entry-side twin of <see cref="AnnounceTabSwitch"/>: the currently selected tab's frame,
        /// so a fresh focus on a window with a promoted strip names the page before the first row.
        /// </summary>
        private string BuildEntryTabFrameText()
        {
            int index = ResolveReferenceTabIndex();
            PresentationRow tab = tabRows[index];
            return ApplySectionPrefix(tab, BuildTabFrameText(tab, index + 1, tabRows.Count));
        }

        /// <summary>Prefixes <paramref name="composed"/> with the row's section name the first time a row from that section is announced.</summary>
        private string ApplySectionPrefix(PresentationRow row, string composed)
        {
            string section = SectionPrefixFor(row);
            return section == null
                ? composed
                : "RimWorldAccess.Shell.GenericWindow.SectionPrefix".Translate(section, composed).ToString();
        }

        /// <summary>
        /// The row's section name the first time a row from that section is announced, else null.
        /// Suppressed when it equals the window title the caller is about to prepend anyway.
        /// </summary>
        private string SectionPrefixFor(PresentationRow row)
        {
            // Cross first: a title-suppressed section is still recorded, so its later rows stay silent.
            string section = sectionPrefix.Cross(row.Section);
            if (section == null)
            {
                return null;
            }
            if (announceTitleNext)
            {
                string title = ResolveWindowTitle();
                if (!string.IsNullOrEmpty(title) && title == section)
                {
                    return null;
                }
            }
            return section;
        }

        private ElementDescription BuildDescription(PresentationRow row)
        {
            ElementDescription d = new ElementDescription();
            d.Label = row.Label;
            // Strips often draw the current tab inert; on a selected tab that is its state, not a gate.
            d.Disabled = row.Source != null && row.Source.Disabled
                && !(row.Kind == WidgetKind.Tab && row.EffectiveSelected);
            d.SearchMatch = (row.Source != null && row.Source.SearchMatch)
                || (row.RingCaptureIndex >= 0 && row.RingCaptureIndex < captureRows.Count
                    && captureRows[row.RingCaptureIndex].SearchMatch);
            if (row.SuppressGenericTip)
            {
                // One toggle of a multi-icon WidgetRow run whose registered tooltip resolved
                // byte-identical to a sibling's generic tip, saying nothing the row's own name does
                // not. A toggle whose tip genuinely differs is never marked and still speaks it.
            }
            else if (row.Source != null && row.Source.TipIsExact)
            {
                // The drawing call itself paired this tip with this row, so the rect lookups below can only add neighbours' tips.
                d.Extras = row.Source.Tip;
            }
            else
            {
                d.Extras = TooltipCapture.TryResolveAtScreen(row.ScreenRect, row.Clip);
            }
            if (!row.SuppressGenericTip && string.IsNullOrEmpty(d.Extras) && (row.Source == null || !row.Source.TipIsExact))
            {
                d.Extras = TooltipCapture.TryResolveAtScreen(row.LabelScreenRect, row.LabelClip);
            }
            if (!row.SuppressGenericTip && string.IsNullOrEmpty(d.Extras) && row.Source != null && !string.IsNullOrEmpty(row.Source.Tip))
            {
                // A tip a capture bracket carried on the row itself because the rect lookups above
                // structurally cannot reach it: Widgets.DefLabelWithIcon registers the def description
                // one clip context out from its Label, and TipIndex.QueryAtScreen requires equality.
                d.Extras = row.Source.Tip;
            }
            if (row.Source != null && row.Source.LabelIsTextureName && d.Label == row.Source.Label)
            {
                // Nothing named this icon-only button at draw time (the capture fell through to the
                // raw texture asset name), typically because the TipRegion is registered after the
                // ButtonImage draw; this scope's armed tip index can still supply the real label.
                // Guarded on the label still being the untouched capture string so a row another
                // fusion rule already renamed is never clobbered.
                if (!string.IsNullOrEmpty(d.Extras))
                {
                    string tipName = WidgetCapture.ShortTipName(d.Extras);
                    if (!string.IsNullOrEmpty(tipName))
                    {
                        d.Label = tipName;
                    }
                }
                else
                {
                    // No tooltip at all — the texture name is all there is; clean it for speech
                    // rather than read a raw asset filename ("CMR_padlock_closed").
                    d.Label = CleanTextureName(d.Label, stripTrailingIcoToken: false);
                }
            }
            if (!string.IsNullOrEmpty(d.Extras) && !string.IsNullOrEmpty(d.Label))
            {
                // An icon-only control is NAMED from its own tooltip, and that same TipRegion also
                // resolves through the geometric channel above, so the row would say one string twice.
                // Dropping the duplicate loses no information: a sighted player sees the label and the
                // tooltip is the same words on hover. Extended to a whole leading SENTENCE, since a
                // reconstructed tooltip usually opens with the control's own name; a tooltip that
                // merely starts with the same word keeps every character.
                d.Extras = RedundantLabelPrefix.Strip(d.Extras, d.Label);
                if (string.IsNullOrEmpty(d.Extras))
                    d.Extras = null;
            }
            // An icon inside the control's box may already resolve through the rect lookup above.
            if (!string.IsNullOrEmpty(row.Description) && (d.Extras == null || d.Extras.IndexOf(row.Description, StringComparison.Ordinal) < 0))
            {
                d.Extras = string.IsNullOrEmpty(d.Extras) ? row.Description : d.Extras + ". " + row.Description;
            }

            CapturedWidget source = row.Source;
            switch (row.Kind)
            {
                case WidgetKind.Checkbox:
                    d.Role = ElementRole.Checkbox;
                    d.Check = row.Check ?? CheckState.Unchecked;
                    if (row.ReadOnlyCheckbox)
                    {
                        // The state is real (vanilla drew it), the control is not; the composer speaks
                        // the ReadOnly word for any non-None role.
                        d.ReadOnly = true;
                    }
                    if (source != null && source.Composite == CompositeMember.SelectableRowLabel)
                    {
                        // A CheckboxLabeledSelectable row is both checkable and selectable, and
                        // vanilla draws the selection as a row highlight. Enter drives the check; the
                        // selection rides the same utterance as supplementary info, ahead of any tip.
                        string selection = (row.EffectiveSelected
                            ? "RimWorldAccess.UI.GenericWindow.RowSelected"
                            : "RimWorldAccess.UI.GenericWindow.RowNotSelected").Translate().ToString();
                        d.Extras = string.IsNullOrEmpty(d.Extras) ? selection : selection + ". " + d.Extras;
                    }
                    break;
                case WidgetKind.RadioButton:
                    d.Role = ElementRole.RadioButton;
                    d.Selected = row.EffectiveSelected;
                    break;
                case WidgetKind.Tab:
                    d.Role = ElementRole.Tab;
                    // EffectiveSelected, not the capture row's flag: a hand-rolled strip's tabs are
                    // captured as plain buttons with no selected state of their own, so the verdict
                    // read off the caller's tint lives on the presentation row.
                    d.Selected = row.EffectiveSelected;
                    if (!string.IsNullOrEmpty(source.Tip))
                    {
                        // The strip's tip is carried on the record itself: its TipRegion is group-local
                        // and mouse-gated, so the rect lookup above can never resolve it.
                        d.Extras = source.Tip;
                    }
                    break;
                case WidgetKind.Button:
                    // A dropdown opener presents as a combo box — a value chosen from a list — while
                    // every other captured button stays a plain Button.
                    d.Role = (source != null && source.DropdownOpener) ? ElementRole.ComboBox : ElementRole.Button;
                    if (row.RingCaptureIndex != row.ActivateCaptureIndex
                        && source != null && !string.IsNullOrEmpty(source.Label))
                    {
                        // Fused with a preceding caption Label: the button's own caption is the current
                        // CHOICE of a combo or the value/verb of a value button hosted in a labeled
                        // row, so it is the row's value.
                        d.Value = source.Label;
                    }
                    break;
                case WidgetKind.Slider:
                    d.Role = ElementRole.Slider;
                    // Own-label value rule: a non-blank own label ("Price for guests: 100%") already
                    // speaks the value, so a raw float would double it, possibly in another scale. A
                    // blank one carries none, so the value comes from the slider itself, unless the
                    // caller drew its own readout or the caption is proven to carry it (CaptionCarriesValue).
                    if (row.OwnLabelIsValue)
                    {
                        d.Value = source.Label;
                    }
                    else if (string.IsNullOrEmpty(source.Label) && !row.CaptionCarriesValue && !string.IsNullOrEmpty(row.ValueText))
                    {
                        d.Value = row.ValueText;
                    }
                    else if (string.IsNullOrEmpty(source.Label) && !row.CaptionCarriesValue)
                    {
                        d.Value = !string.IsNullOrEmpty(source.SliderValueText) ? source.SliderValueText : FormatSliderValue(source);
                    }
                    d.AtMinimum = source.SliderValue <= source.SliderMin + 0.0001f;
                    d.AtMaximum = source.SliderValue >= source.SliderMax - 0.0001f;
                    break;
                case WidgetKind.Range:
                    ApplyRangeState(d, row);
                    break;
                case WidgetKind.TextField:
                    d.Role = ElementRole.TextField;
                    if (string.IsNullOrEmpty(source.Text))
                    {
                        d.ValueBlank = true;
                    }
                    else if (IsSensitiveFieldLabel(d.Label) || row.SpeechMasked)
                    {
                        // A credential field: visual non-harm leaves the mod's plaintext rendering
                        // alone, but the spoken channel is a different exposure (audible, likely
                        // recorded), so speak a character count instead of the secret. Detected
                        // mod-agnostically by caption text alone.
                        d.Value = "RimWorldAccess.UI.GenericWindow.SensitiveValueSet".Translate(source.Text.Length).ToString();
                    }
                    else
                    {
                        // A percent field's captured text is already the percent-domain number a
                        // sighted player reads next to the "%" caption (suppressed at capture).
                        d.Value = source.NumericPercent
                            ? "RimWorldAccess.UI.GenericWindow.PercentValue".Translate(source.Text).ToString()
                            : source.Text;
                    }
                    if (source.NumericField)
                    {
                        // The call site's own harvested bounds, spoken ahead of any tooltip text.
                        string range = "RimWorldAccess.UI.GenericWindow.NumericRange".Translate(
                            FormatNumericBound(source.NumericMin, source.NumericIsInt),
                            FormatNumericBound(source.NumericMax, source.NumericIsInt)).ToString();
                        d.Extras = string.IsNullOrEmpty(d.Extras) ? range : range + ". " + d.Extras;
                    }
                    break;
                case WidgetKind.Stepper:
                    d.Role = ElementRole.Stepper;
                    if (!string.IsNullOrEmpty(row.StepValue))
                    {
                        // A hand-rolled spinner whose value the caller drew as a plain Label. The two
                        // signs ride Extras, as IntEntry's captions do: they are what a player sees.
                        d.Value = row.StepValue;
                        if (!string.IsNullOrEmpty(row.StepCaptions))
                        {
                            string steps = "RimWorldAccess.UI.GenericWindow.StepperSteps".Translate(row.StepCaptions).ToString();
                            d.Extras = string.IsNullOrEmpty(d.Extras) ? steps : steps + ". " + d.Extras;
                        }
                    }
                    else if (source != null && (source.StepperValueField || source.Kind == WidgetKind.TextField))
                    {
                        // IntEntry, or a hand-rolled spinner anchored on its own real field: the
                        // center numeric box's text is the composite's value, and the +/- captions ride
                        // Extras. Enter opens the numeric edit session on that field, so this row owns
                        // Enter and is not inert for the proceed seam.
                        d.EntersEditOnAccept = true;
                        if (string.IsNullOrEmpty(source.Text))
                        {
                            d.ValueBlank = true;
                        }
                        else
                        {
                            d.Value = source.Text;
                        }
                        if (!string.IsNullOrEmpty(row.StepCaptions))
                        {
                            string steps = "RimWorldAccess.UI.GenericWindow.StepperSteps".Translate(row.StepCaptions).ToString();
                            d.Extras = string.IsNullOrEmpty(d.Extras) ? steps : steps + ". " + d.Extras;
                        }
                    }
                    else
                    {
                        // IntAdjuster draws no value of its own — the two button captions ARE what a
                        // sighted player sees; never invent a value.
                        d.Value = row.StepCaptions;
                    }
                    break;
                case WidgetKind.TreeItem:
                    // The role word is silent for a tree item — depth and branch state ARE its
                    // announcement. Level is unconditional and 1-based, as in TreeRegionScope; a leaf
                    // leaves Expanded null. Position stays this reader's flat "x of y": a captured
                    // stream carries no sibling structure to count against.
                    d.Role = ElementRole.TreeItem;
                    d.Level = (source != null ? source.TreeLevel : 0) + 1;
                    d.Expanded = row.Expanded;
                    break;
                case WidgetKind.FillableBar:
                    // A bar draws no text of its own; its fill fraction is the information. Named by a
                    // FillableBarLabeled caption the percentage becomes the row's value; unnamed it is
                    // all the row has, so it stays the label.
                    d.Role = ElementRole.None;
                    d.ReadOnly = true;
                    if (string.IsNullOrEmpty(d.Label))
                    {
                        d.Label = source.FillPercent.ToStringPercent();
                    }
                    else
                    {
                        d.Value = source.FillPercent.ToStringPercent();
                    }
                    if (source.BarChangeRate != 0)
                    {
                        // The arrows vanilla draws beside the bar are the only rendering of the rate a
                        // sighted player gets.
                        string rate = (source.BarChangeRate > 0
                            ? "RimWorldAccess.UI.GenericWindow.BarRising"
                            : "RimWorldAccess.UI.GenericWindow.BarFalling").Translate().ToString();
                        d.Extras = string.IsNullOrEmpty(d.Extras) ? rate : rate + ". " + d.Extras;
                    }
                    break;
                default: // Label
                    d.Role = ElementRole.None;
                    d.ReadOnly = true;
                    break;
            }
            ApplyTypeInValue(d, row);
            return d;
        }

        /// <summary>
        /// The value and bound flags of ONE thumb of a range control, shared by the focus and
        /// state-change announcements. The row speaks only its own end, rendered the way vanilla
        /// rendered it (<see cref="FormatRangeEnd"/>), never an invented raw float. The bounds are
        /// vanilla's reachable ones, not the call site's raw pair: the low thumb stops a gap short of
        /// the upper bound and the high thumb a gap above the lower (Verse/Widgets.cs:2287-2300,
        /// :2387-2409).
        /// </summary>
        private static void ApplyRangeState(ElementDescription d, PresentationRow row)
        {
            CapturedWidget source = row.Source;
            d.Role = ElementRole.Slider;
            d.Value = FormatRangeEnd(source, row.RangeHigh);
            float value = row.RangeHigh ? source.RangeHigh : source.RangeLow;
            float reachableMin = row.RangeHigh ? source.RangeLimitMin + source.RangeGap : source.RangeLimitMin;
            float reachableMax = row.RangeHigh ? source.RangeLimitMax : source.RangeLimitMax - source.RangeGap;
            d.AtMinimum = value <= reachableMin + 0.0001f;
            d.AtMaximum = value >= reachableMax - 0.0001f;
        }

        /// <summary>
        /// One end of a range control, rendered by the formatter vanilla itself used for the text it
        /// drew: a quality range speaks the category label (QualityUtility.cs:39), an int range a
        /// plain integer (Widgets.cs:2324), a float range the caller's ToStringStyle (Widgets.cs:2222).
        /// </summary>
        private static string FormatRangeEnd(CapturedWidget source, bool high)
        {
            return FormatRangeValue(source.RangeFamily, source.RangeStyle,
                high ? source.RangeHigh : source.RangeLow);
        }

        /// <summary>Shared with the mouse-drag reader (<c>RangeDragSpeech</c>) so both speak a thumb in vanilla's own rendering.</summary>
        internal static string FormatRangeValue(RangeFamily family, ToStringStyle style, float value)
        {
            switch (family)
            {
                case RangeFamily.Quality:
                    return ((QualityCategory)Mathf.RoundToInt(value)).GetLabel();
                case RangeFamily.Int:
                    return Mathf.RoundToInt(value).ToStringCached();
                default:
                    return value.ToStringByStyle(style);
            }
        }

        /// <summary>
        /// Generic fallback formatting for an UNFUSED slider that drew no SliderValueText of its own;
        /// a fused slider's caption already carries the value and never calls this. A bare captured
        /// slider carries no label text to infer a percent convention from, so this formats the raw
        /// value.
        /// </summary>
        private static string FormatSliderValue(CapturedWidget row)
        {
            return SliderCaption.FormatValue(row.SliderValue);
        }

        private void AnnounceStateChange(PresentationRow row, bool captionMoved = false)
        {
            ElementDescription d = new ElementDescription();
            switch (row.Kind)
            {
                case WidgetKind.Checkbox:
                    d.Check = row.Check ?? CheckState.Unchecked;
                    if (row.Source != null && row.Source.Composite == CompositeMember.SelectableRowLabel)
                    {
                        // A CheckboxLabeledSelectable row carries two independent states vanilla draws
                        // separately, both keyboard-drivable, so a change to either speaks both —
                        // never a guess about which one moved.
                        d.Selected = row.EffectiveSelected;
                    }
                    break;
                case WidgetKind.RadioButton:
                case WidgetKind.Tab:
                    d.Selected = row.EffectiveSelected;
                    break;
                case WidgetKind.Slider:
                    // Own-label value rule, same test as BuildDescription: a non-blank own captured
                    // label already carries the mod's rendering of the value, and ComposeStateChange
                    // never repeats the row's name, so its refreshed label is the only channel left to
                    // speak it through. A blank own label carries no value, so the refreshed
                    // SliderValueText/raw float is spoken instead, unless the borrowed caption is a
                    // proven name/value pair. captionMoved is the evidence-based third tier: a fused
                    // caption whose text changed across this adjust just demonstrated it renders the
                    // value, which the pair-fold's CaptionCarriesValue can never see.
                    if (row.OwnLabelIsValue)
                    {
                        d.Value = row.Source.Label;
                    }
                    else if (!string.IsNullOrEmpty(row.Source.Label) || row.CaptionCarriesValue || captionMoved)
                    {
                        d.Value = row.Label;
                    }
                    else if (!string.IsNullOrEmpty(row.ValueText))
                    {
                        d.Value = row.ValueText;
                    }
                    else
                    {
                        d.Value = !string.IsNullOrEmpty(row.Source.SliderValueText) ? row.Source.SliderValueText : FormatSliderValue(row.Source);
                    }
                    d.AtMinimum = row.Source.SliderValue <= row.Source.SliderMin + 0.0001f;
                    d.AtMaximum = row.Source.SliderValue >= row.Source.SliderMax - 0.0001f;
                    break;
                case WidgetKind.Range:
                    // The thumb's refreshed value in vanilla's own rendering: a range draws its
                    // numbers itself, so the fused-slider "speak the caption" rule does not apply.
                    ApplyRangeState(d, row);
                    break;
                case WidgetKind.TreeItem:
                    if (!row.Expanded.HasValue)
                    {
                        return;
                    }
                    d.Expanded = row.Expanded;
                    break;
                case WidgetKind.Stepper:
                    // IntEntry's refreshed center-field text IS the new state (vanilla rewrites the
                    // editBuffer on every button click). IntAdjuster draws no value, so it follows the
                    // fused-slider rule: speak the row's refreshed label. A hand-rolled spinner whose
                    // value is a drawn Label (StepValue) is checked FIRST, because its Source is the
                    // value Label itself and row.Label is the caption found for it, not the number.
                    if (!string.IsNullOrEmpty(row.StepValue))
                    {
                        d.Value = row.StepValue;
                    }
                    else if (!string.IsNullOrEmpty(row.Source.Text))
                    {
                        d.Value = row.Source.Text;
                    }
                    else if (!string.IsNullOrEmpty(row.Label))
                    {
                        d.Value = row.Label;
                    }
                    else
                    {
                        d.ValueBlank = true;
                    }
                    break;
                default:
                    return;
            }
            ApplyTypeInValue(d, row);
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

#if DEBUG
        /// <summary>
        /// Dev-bridge screen dump. This scope has no regions, so the shape is its own: one line per
        /// row, index first, everything <see cref="BuildDescription"/> would speak plus the raw
        /// capture facts no announcement carries (Composite role, rect, whether a tip resolved).
        /// presentationRows first, then <see cref="tabRows"/>. Never moves the cursor or speaks.
        /// </summary>
        internal override string DebugDescribeSurface()
        {
            var sb = new StringBuilder();
            sb.Append("scope ").Append(Name);
            sb.Append("\npresentationRows (").Append(presentationRows.Count).Append(" items)");
            for (int i = 0; i < presentationRows.Count; i++)
            {
                AppendDebugRow(sb, i, presentationRows[i], current: i == RowIndex);
            }
            sb.Append("\ntabRows (").Append(tabRows.Count).Append(" items)");
            for (int i = 0; i < tabRows.Count; i++)
            {
                AppendDebugRow(sb, i, tabRows[i], current: false);
            }
            return sb.ToString();
        }

        /// <summary>One diagnostic line per row, marking the model's own cursor row with "» " (ScreenScope's own dump convention). Guarded so one row's BuildDescription throwing can't blank the rest of the dump.</summary>
        private void AppendDebugRow(StringBuilder sb, int index, PresentationRow row, bool current)
        {
            sb.Append('\n').Append(current ? "  » " : "    ").Append(index).Append(": ").Append(row.Kind);
            if (row.Source != null && row.Source.Composite != CompositeMember.None)
            {
                sb.Append(" composite=").Append(row.Source.Composite);
            }
            AppendQuoted(sb.Append(" label="), row.Label);
            try
            {
                ElementDescription d = BuildDescription(row);
                if (d.Check.HasValue)
                {
                    sb.Append(" check=").Append(d.Check.Value);
                }
                if (d.Selected.HasValue)
                {
                    sb.Append(" selected=").Append(d.Selected.Value);
                }
                if (d.Expanded.HasValue)
                {
                    sb.Append(" expanded=").Append(d.Expanded.Value);
                }
                if (!string.IsNullOrEmpty(d.Value))
                {
                    AppendQuoted(sb.Append(" value="), d.Value);
                }
                else if (d.ValueBlank)
                {
                    sb.Append(" value=(blank)");
                }
                if (d.AtMinimum)
                {
                    sb.Append(" atMin=True");
                }
                if (d.AtMaximum)
                {
                    sb.Append(" atMax=True");
                }
                if (d.Disabled)
                {
                    sb.Append(" disabled=True");
                }
                sb.Append(" tip=").Append(!string.IsNullOrEmpty(d.Extras));
                if (!string.IsNullOrEmpty(row.Description))
                {
                    AppendQuoted(sb.Append(" desc="), row.Description);
                }
            }
            catch (Exception ex)
            {
                sb.Append(" (describe threw: ").Append(ex.GetType().Name).Append(")");
            }
            if (row.Source != null && !string.IsNullOrEmpty(row.Source.Text) && row.Kind != WidgetKind.TextField)
            {
                // TextField's text already surfaced as BuildDescription's Value; every other kind that
                // carries raw Text would otherwise never show it in this dump.
                AppendQuoted(sb.Append(" text="), row.Source.Text);
            }
            Rect r = row.ScreenRect;
            sb.Append(" rect=(").Append(Mathf.RoundToInt(r.x)).Append(',').Append(Mathf.RoundToInt(r.y))
              .Append(',').Append(Mathf.RoundToInt(r.width)).Append(',').Append(Mathf.RoundToInt(r.height)).Append(')');
        }

        /// <summary>Dev-bridge dump of the capture rows behind <see cref="presentationRows"/>, in draw order, with the facts fusion reads.</summary>
        internal string DebugDescribeCaptureRows()
        {
            var sb = new StringBuilder();
            sb.Append("scope ").Append(Name).Append("\ncaptureRows (").Append(captureRows.Count).Append(" items)");
            IReadOnlyList<int> columnBreaks = WidgetCapture.ListingColumnBreaks;
            for (int i = 0; i < captureRows.Count; i++)
            {
                CapturedWidget row = captureRows[i];
                sb.Append('\n');
                if (columnBreaks.Contains(i))
                {
                    sb.Append("  -- column break --\n");
                }
                sb.Append("    ").Append(i).Append(": ").Append(row.Kind);
                if (row.Composite != CompositeMember.None)
                {
                    sb.Append(" composite=").Append(row.Composite);
                }
                AppendQuoted(sb.Append(" label="), row.Label);
                if (row.Heading)
                {
                    sb.Append(row.HeadingFromGapLineOnly ? " heading=GapLine" : " heading=True");
                }
                if (row.LabelPairFolded)
                {
                    sb.Append(" pairFolded=True");
                }
                if (row.OrphanTip)
                {
                    sb.Append(" orphanTip=True");
                }
                if (row.Kind == WidgetKind.Slider)
                {
                    sb.Append(" sliderValue=").Append(row.SliderValue);
                }
                Rect r = row.ScreenRect;
                sb.Append(" rect=(").Append(Mathf.RoundToInt(r.x)).Append(',').Append(Mathf.RoundToInt(r.y))
                  .Append(',').Append(Mathf.RoundToInt(r.width)).Append(',').Append(Mathf.RoundToInt(r.height)).Append(')');
                if (row.VisibleScreenRect.width <= 0f)
                {
                    sb.Append(" unseen");
                }
            }
            return sb.ToString();
        }

        private static StringBuilder AppendQuoted(StringBuilder sb, string text)
        {
            return sb.Append('"').Append((text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
        }
#endif
    }
}
