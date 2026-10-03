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
    /// <summary>Which Widgets-level control a captured row came from.</summary>
    public enum WidgetKind
    {
        /// <summary>Widgets.Label / Listing_Standard.Label — plain text, no action.</summary>
        Label,
        Checkbox,
        RadioButton,
        /// <summary>Widgets.ButtonText — every Listing_Standard button wrapper (ButtonText, ButtonTextLabeledPct, IntAdjuster, IntSetter, IntEntry's four steppers) funnels here.</summary>
        Button,
        Slider,
        /// <summary>Widgets.TextField or Widgets.TextArea (see <see cref="CapturedWidget.MultiLine"/>). TextFieldNumeric&lt;T&gt;, TextEntryLabeled and IntEntry's numeric box all funnel to one of these two.</summary>
        TextField,
        /// <summary>One TabRecord from a TabDrawer.DrawTabs tab strip — every DrawTabs/DrawTabsOverflow overload funnels through the one patched core.</summary>
        Tab,
        /// <summary>Widgets.FillableBar — a bar whose only information is <see cref="CapturedWidget.FillPercent"/> (any caption is a separate Label row); non-interactive, presented as a read-only value row.</summary>
        FillableBar,
        /// <summary>A Widgets.ButtonInvisible click target, never presented directly: the owning scope fuses it into an overlapping Label row or drops it as an orphan when nothing overlaps.</summary>
        InvisibleButton,
        /// <summary>Never recorded by any tap: the presentation-side kind GenericWindowScope assigns when it fuses a Widgets.IntEntry / Listing_Standard.IntAdjuster run into one navigable row.</summary>
        Stepper,
        /// <summary>Never recorded by any tap: the presentation-side kind assigned when GenericWindowScope folds a Verse.Listing_Tree node's expander and label rows into one navigable row, spoken as ElementRole.TreeItem.</summary>
        TreeItem,
        /// <summary>
        /// One Widgets.IntRange / FloatRange / QualityRange control — recorded from a
        /// bracket rather than from a primitive it draws, because vanilla paints both
        /// thumbs with raw GUI.DrawTexture and drags them from raw Event.current code, so
        /// no captured click target exists. Two presentation rows share one capture row,
        /// one per thumb.
        /// </summary>
        Range,
    }

    /// <summary>
    /// Which of the three range cores a <see cref="WidgetKind.Range"/> row came from —
    /// the discriminator that decides how its two ends are SPOKEN, never how they are
    /// stored: all three carry floats, since vanilla's own drag arithmetic is float in
    /// all three (Verse/Widgets.cs:2216/:2318/:2431).
    /// </summary>
    public enum RangeFamily
    {
        /// <summary>Widgets.IntRange — ends spoken as plain integers, as vanilla renders them (Widgets.cs:2324).</summary>
        Int,

        /// <summary>Widgets.FloatRange — ends spoken through the caller's own ToStringStyle (Widgets.cs:2222).</summary>
        Float,

        /// <summary>Widgets.QualityRange — ends spoken as quality LABELS via QualityCategory.GetLabel, the source vanilla's drawn text uses (Widgets.cs:2437).</summary>
        Quality,
    }

    /// <summary>
    /// Which member of a labeled composite a captured row is, when one of the composite
    /// brackets marked it. Mutually exclusive by construction — a row is drawn inside at
    /// most one composite body, in exactly one role.
    /// </summary>
    public enum CompositeMember
    {
        None,

        /// <summary>
        /// Widgets.HyperlinkWithIcon's visible caption: a ButtonText drawn
        /// <c>active: false</c>, which is a paint instruction there (no mouseover sound),
        /// not a gate — the live click target is the sibling
        /// <see cref="HyperlinkTarget"/> over the same rect (Verse/Widgets.cs:1153).
        /// </summary>
        HyperlinkLabel,

        /// <summary>The ButtonInvisible Widgets.HyperlinkWithIcon draws over its caption; its click runs <c>hyperlink.ActivateHyperlink()</c>.</summary>
        HyperlinkTarget,

        /// <summary>Widgets.CheckboxLabeledSelectable's caption Label — the run's anchor, carrying the row's own selected state in <see cref="CapturedWidget.Selected"/>.</summary>
        SelectableRowLabel,

        /// <summary>Its row-select ButtonInvisible, drawn only while the row is NOT selected (Verse/Widgets.cs:1287).</summary>
        SelectableRowSelect,

        /// <summary>Its 24-pixel check-toggle ButtonInvisible, always drawn (Verse/Widgets.cs:1296).</summary>
        SelectableRowCheck,

        /// <summary>Listing_Standard.SelectableDef's delete ButtonImage, named after the row it deletes.</summary>
        SelectableDefDelete,

        /// <summary>Listing_Standard.SelectableDef's full-row select ButtonInvisible.</summary>
        SelectableDefRow,

        /// <summary>
        /// The Widgets.ButtonImage Verse.WidgetRow.ToggleableIcon draws
        /// (Verse/WidgetRow.cs:178). The button is the live control; the on/off state a
        /// sighted player reads is a bare GUI.DrawTexture overlay drawn after it, so the
        /// bracket carries that state in <see cref="CapturedWidget.Checked"/>.
        /// </summary>
        ToggleableIcon,

        /// <summary>
        /// The ButtonInvisible a Widgets.ColorBox swatch draws over itself
        /// (Verse/Widgets.cs:2937). Its click already assigns the caller's
        /// <c>ref Color</c>, so the row is operable as recorded; the bracket only supplies
        /// the identity vanilla renders as a filled square (hex value, in
        /// <see cref="CapturedWidget.Label"/>) and the chosen state it draws as an outline.
        /// </summary>
        ColorSwatch,

        /// <summary>A MedicalCareUtility.MedicalCareSetter icon hotspot (RimWorld/MedicalCareUtility.cs:36-61), named by its hover tooltip and boxed when chosen.</summary>
        MedicalCareChoice,

        /// <summary>
        /// A standalone Widgets.DefIcon / ThingIcon draw, recorded as a read-only Label row
        /// named after its def because the widget draws no captured primitive at all.
        /// Marked so consumers can tell a picture apart from text a caller wrote.
        /// </summary>
        Icon,

        /// <summary>
        /// The ButtonImage Verse.Listing_Tree.OpenCloseWidget draws for an OPENABLE node
        /// (Verse/Listing_Tree.cs:59; a leaf returns before drawing anything). Carries the
        /// node's depth in <see cref="CapturedWidget.TreeLevel"/> and its branch state in
        /// <see cref="CapturedWidget.TreeOpen"/>.
        /// </summary>
        TreeRowExpander,

        /// <summary>The Label Verse.Listing_Tree.LabelLeft draws for a node (Listing_Tree.cs:36-57), immediately after its expander when it has one.</summary>
        TreeRowLabel,

        /// <summary>
        /// The "min - max" text a range core draws through the hooked Label immediately
        /// after its own <see cref="WidgetKind.Range"/> row (Verse/Widgets.cs:2334). Still
        /// RECORDED — the inspect-tab reader folds this same raw stream, where that text is
        /// the only rendering of a filter's hit-point/quality range — and consumed at
        /// presentation, becoming the two thumb rows' caption when the caller drew none.
        /// </summary>
        RangeText,

        /// <summary>
        /// A synthetic hand-off row Verse.ThingFilterUI.DoThingFilterConfigWindow mints in
        /// place of the hundreds of rows its filter tree would otherwise draw. Kind stays
        /// <see cref="WidgetKind.Label"/> — this discriminator is the only thing marking it
        /// — so a presentation layer that does not know about it still renders a harmless
        /// plain row. The call's arguments live on <see cref="CapturedWidget.Payload"/> as
        /// a <see cref="CapturedFilterPanel"/>.
        /// </summary>
        FilterPanelHandoff,
    }

    /// <summary>
    /// The <see cref="CompositeMember.FilterPanelHandoff"/> row's payload: the arguments a
    /// Verse.ThingFilterUI.DoThingFilterConfigWindow call was invoked with, so presentation
    /// can hand them to Inspection.ThingFilterMenuState.Open instead of replaying the
    /// panel's own rows.
    /// </summary>
    public sealed class CapturedFilterPanel
    {
        public ThingFilter Filter;
        public ThingFilter ParentFilter;
        public IEnumerable<SpecialThingFilterDef> ForceHiddenFilters;
        public bool ForceHideHitPointsConfig;
        public bool ForceHideQualityConfig;
    }

    /// <summary>One row recorded during the armed window's own draw pass, in draw order.</summary>
    public sealed class CapturedWidget
    {
        public WidgetKind Kind;
        public string Label = "";
        public Rect Rect;

        // Screen-space rect (GuiSpace.ToScreen), for tooltip matching and adjacency fusion
        // across unrelated GUI-group spaces. The local Rect above stays the one the inline
        // focus ring draws against: the ring's Widgets.DrawBox call runs in that group space.
        public Rect ScreenRect;

        // The GUIClip context ScreenRect was unclipped under. ScreenRect alone collides
        // across unrelated clips — a row scrolled past a ScrollView's fold still unclips to
        // an off-screen rect that can overlap an unrelated widget — so every screen-space
        // match in this file requires Clip equality alongside rect overlap.
        public GuiSpace.ClipKey Clip;

        /// <summary>
        /// The visible portion of <see cref="Rect"/> under the clip stack active at record
        /// time — empty when the widget was fully clipped away. Fusion's cross-clip tests
        /// require BOTH parties' visible rects to be non-empty and overlapping, which is
        /// what makes relaxing the clip-equality gate safe.
        /// </summary>
        public Rect VisibleScreenRect;

        /// <summary>
        /// Index of the innermost <see cref="WidgetCapture.ScrollContainer"/> this row drew
        /// inside, or -1 outside every tracked ScrollView. Stamped by this initializer
        /// rather than threaded through every construction site: construction is exactly
        /// when a record method builds its row, so the ambient value is always the live one.
        /// </summary>
        public int ContainerIndex = WidgetCapture.CurrentContainerIndexForRecording;

        /// <summary>
        /// Recorded inside a vanilla Widgets.Dropdown body: the row IS the dropdown's
        /// opener, so presentation speaks ComboBox instead of Button. Stays false whenever
        /// the generic patch could not be applied.
        /// </summary>
        public bool DropdownOpener;

        /// <summary>
        /// Button rows only: this row's caption came from the tooltip over its own rect.
        /// The folding path reads it to keep that tooltip from being spoken twice, as the
        /// row's name and again as its tip.
        /// </summary>
        public bool NameFromTip;

        /// <summary>
        /// Button rows only: nothing named this row at DRAW time and it fell through to the
        /// raw texture name. A caller registering its TipRegion AFTER the button draw is
        /// invisible to that synchronous tier, so the folding path retries once the pass's
        /// registrations are in and fills <see cref="TipName"/> on success.
        /// </summary>
        public bool NeedsLateTipName;

        /// <summary>
        /// The name the late tooltip retry resolved, or null. <see cref="SpokenLabel"/>
        /// prefers it, but <see cref="Label"/> stays the capture-time string: both
        /// activation channels re-find a widget by kind + label + ordinal against what the
        /// NEXT pass records, so a renamed row could never be fired again.
        /// </summary>
        public string TipName;

        /// <summary>The name presentation speaks: the late tooltip name when one was resolved, else the capture-time label.</summary>
        public string SpokenLabel
        {
            get { return !string.IsNullOrEmpty(TipName) ? TipName : Label; }
        }

        /// <summary>
        /// Button rows only: nothing named this row at all — <see cref="Label"/> is the
        /// last-resort texture-asset name. Set unconditionally, independent of which
        /// tooltip channel is open, so a channel-agnostic reader can still promote a tip
        /// registered over this row to its name once that channel's index has it.
        /// </summary>
        public bool LabelIsTextureName;

        // Button rows only: the caption's texture was TexButton.CloseXSmall/CloseXBig, the
        // corner close-X vanilla draws for doCloseX. Lets the owning scope drop a redundant
        // X when the same window also draws a bottom Close button for the identical action.
        public bool CloseX;

        /// <summary>
        /// Button rows only: the live <c>GUI.color</c> tint set when this button was drawn.
        /// IMGUI has no "selected button" concept, so a caller hand-rolling
        /// mutually-exclusive buttons signals the current one the only way immediate mode
        /// offers: full tint on it, dimmed on the rest. White by default, so a comparison
        /// across a set only finds a difference the caller made deliberately.
        /// </summary>
        public Color RecordColor;

        // Label rows only: drawn in GameFont.Medium (a dialog/window title) or immediately
        // preceded by a Listing.GapLine.
        public bool Heading;

        // Label rows only: this row has absorbed an immediately following co-located
        // sibling label via RecordLabel's pair-fold. Caps the fold at one per row: a third
        // label sharing the spot repaints, it does not extend the pair.
        public bool LabelPairFolded;

        // The NAME half of that fold, kept apart from the composed "name: value" Label so a
        // reader appending a fresher value has something to append it to.
        public string LabelPairName;

        // Label rows only: Heading came SOLELY from a weak, revocable tier — "immediately
        // after a Listing.GapLine", or the Tiny-font tier TinyFontHeadingDetector sets
        // later — never from the strong GameFont.Medium tier. A GapLine can be a plain
        // visual rule between unrelated rows, so a caption right after one is only a
        // heading GUESS, revoked retroactively when a later capture shows it was captioning
        // something on its own row; the Tiny-font tier is revoked by the same check.
        public bool HeadingFromGapLineOnly;

        // Label rows only: drawn in GameFont.Tiny. A raw capture fact — whether such a
        // label qualifies as a heading this pass (Tiny a strict minority of the pass's
        // labels, an operable control following closely) is decided later in
        // TinyFontHeadingDetector, which then sets Heading/HeadingFromGapLineOnly itself.
        public bool TinyFont;

        // Label rows only: Verse.Text.Anchor at record time. Used only to order a folded
        // pair's name/value halves — never read by presentation or matching.
        public TextAnchor RecordAnchor;

        // Vanilla's own widget-level gate as drawn: CheckboxLabeled/Checkbox/
        // RadioButtonLabeled `disabled`, ButtonText/CustomButtonText `!active`. Presentation
        // speaks it and BOTH activation channels refuse a gated fire, exactly as vanilla
        // refuses the mouse click (a mutator without its gate is a doctrine violation).
        // Sliders/text fields/tabs carry no vanilla gate.
        public bool Disabled;

        // Checkbox
        public bool Checked;

        // CheckboxMulti rows only: the vanilla tri-state value, distinct from Checked
        // (which collapses On/Partial/Off to a bool for plain Checkbox rows).
        public MultiCheckboxState? TriState;

        // RadioButton; also the ROW-selected state of a Widgets.CheckboxLabeledSelectable
        // caption Label, which is selectable AND checkable with the two states drawn
        // independently.
        public bool Selected;

        // Slider
        public float SliderValue;
        public float SliderMin;
        public float SliderMax;
        public float SliderRoundTo;

        // The slider's own rightAlignedLabel when vanilla's HorizontalSlider drew one — the
        // caller's own rendering of the value, preferred over a FormatSliderValue guess for
        // an unfused row. Empty when it drew none.
        public string SliderValueText = "";

        // Vanilla's own drag identity for this slider: the sliderDraggingID hash it steers a
        // mouse gesture by (Verse/Widgets.cs:2081), recomputed by SliderDragSpeech.ControlId.
        // 0 for a hand-rolled slider, which has no such identity to match.
        public int SliderControlId;

        // TextField/TextArea
        public string Text = "";
        public bool MultiLine;

        // TextField rows only: the recording TextField ran inside a
        // Widgets.TextFieldNumeric<int|float> body, carrying the CALL SITE's own min/max so
        // the keyboard edit session validates against vanilla's exact bounds — harvested,
        // never invented. NumericPercent marks TextFieldPercent's inner field, whose buffer
        // AND bounds both live in the percent domain the player types in, so no conversion
        // is needed downstream.
        public bool NumericField;
        public bool NumericIsInt;
        public float NumericMin;
        public float NumericMax;
        public bool NumericPercent;

        /// <summary>
        /// TextField/TextArea/FillableBar rows only: the caption a labeled wrapper's bracket
        /// carried onto this row. Deliberately NOT stored in <see cref="Label"/>: a text
        /// field's live/armed identity is its ordinal among EMPTY-labeled text fields, which
        /// must not shift under enrichment.
        /// </summary>
        public string FieldLabel = "";

        /// <summary>Which member of a labeled composite this row is, when a composite bracket marked it.</summary>
        public CompositeMember Composite;

        /// <summary>
        /// Kind-specific structured data <see cref="Kind"/>/<see cref="Composite"/> alone
        /// cannot carry — currently only <see cref="CapturedFilterPanel"/> for a
        /// <see cref="CompositeMember.FilterPanelHandoff"/> row. Null otherwise.
        /// </summary>
        public object Payload;

        /// <summary>
        /// ToggleableIcon rows only: the icon texture's raw asset name, recorded even though
        /// <see cref="Label"/> carries the tooltip — a caller may draw several same-row
        /// ToggleableIcons that all pass the identical tooltip, leaving the texture as the
        /// only thing telling them apart. Presentation cleans it for speech.
        /// </summary>
        public string IconTexName;

        /// <summary>
        /// TreeRowExpander/TreeRowLabel rows only: the node's nesting depth, 0 at the root,
        /// taken from the <c>indentLevel</c> argument vanilla was passed rather than
        /// reverse-computed from the rect (Listing_Tree.XAtIndentLevel multiplies it by a
        /// per-listing nestIndentWidth a subclass may change).
        /// </summary>
        public int TreeLevel;

        /// <summary>
        /// TreeRowExpander rows only: the branch is OPEN — vanilla drew TexButton.Collapse
        /// rather than TexButton.Reveal (Verse/Listing_Tree.cs:69). The drawn texture is the
        /// state a sighted player reads, and the only one honoring an overridden IsOpen.
        /// </summary>
        public bool TreeOpen;

        // Button rows only: one of a Widgets.IntEntry / Listing_Standard.IntAdjuster
        // composite's +/- buttons, identified by draw-order ordinal inside the bracket
        // (IntEntry -10x, -1x, +10x, +1x then the center TextFieldNumeric, Widgets.cs:2186;
        // IntAdjuster -N then +N, Listing_Standard.cs:394). Presentation fuses the run into
        // one Stepper row.
        public bool StepperButton;

        /// <summary>StepperButton rows only: true for a plus button, false for a minus button.</summary>
        public bool StepperPlus;

        /// <summary>
        /// StepperButton rows only: true for IntEntry's ±(10×multiplier) pair. The keyboard
        /// chords drive only the small pair — GenUI.CurrentAdjustmentMultiplier already
        /// scales that step to the big pair's effect and beyond.
        /// </summary>
        public bool StepperBig;

        /// <summary>TextField rows only: Widgets.IntEntry's center numeric box — the fused Stepper row's value field.</summary>
        public bool StepperValueField;

        // Range rows only: the control's state as vanilla was about to draw it.
        // RangeLow/RangeHigh are the two thumbs, RangeLimitMin/RangeLimitMax the bounds the
        // call site allows, RangeGap the closest the thumbs may come (FloatRange's `gap`,
        // IntRange's `minWidth`, 0 for a quality range) and RangeRoundTo vanilla's own drag
        // granularity — every one harvested from the call, never assumed. All three families
        // store floats because vanilla's clamp arithmetic is float in all three; RangeStyle
        // carries a FloatRange's ToStringStyle so a row announces the value vanilla itself
        // would have rendered.
        public RangeFamily RangeFamily;
        public float RangeLow;
        public float RangeHigh;
        public float RangeLimitMin;
        public float RangeLimitMax;
        public float RangeGap;
        public float RangeRoundTo;
        public ToStringStyle RangeStyle;

        // Tab: TabRecord.GetTip() carried directly — the tab strip's own TipRegion is drawn
        // group-local and mouse-gated, so the geometric TooltipCapture lookup can never
        // resolve it.
        public string Tip;

        /// <summary>
        /// <see cref="Tip"/> came from the drawing call itself, so it is the row's exact
        /// tooltip and WINS over the geometric lookup rather than serving as its fallback.
        /// Set by brackets whose widget inflates its rect after registering the tip
        /// (Listing_Tree), where rect overlap otherwise joins neighbouring rows' tooltips
        /// onto one row.
        /// </summary>
        public bool TipIsExact;

        // FillableBar (detached passes only)
        public float FillPercent;

        /// <summary>
        /// FillableBar rows only: the signed change rate a following
        /// Widgets.FillableBarChangeArrows drew beside this bar (0 when it drew none) — a
        /// separate draw over the bar's rect, and the only rendering of the rate a sighted
        /// player gets.
        /// </summary>
        public int BarChangeRate;
    }

    /// <summary>
    /// One GUI.DrawTexture call whose texture matched Widgets.CheckboxOnTex/OffTex/
    /// PartialTex or Widgets.RadioButOnTex/RadioButOffTex, recorded in screen space so a
    /// scope can tell a hand-rolled checkbox's fill state (or an unlabeled radio button's
    /// chosen state) apart from its Label/InvisibleButton rows: both draw caption, click
    /// target and texture as independent primitives sharing no local coordinate space.
    /// Carries its GUIClip context too, so the marker-vs-row overlap test requires context
    /// equality — see <see cref="CapturedWidget.Clip"/>.
    /// </summary>
    /// <summary>
    /// One <c>Widgets.DrawOptionBackground</c> call recorded during a capture pass. The vanilla
    /// idiom for a hand-rolled selectable box: an invisible button drawn over one of these is a
    /// selectable option, and the <see cref="Selected"/> flag is the caller's own selection state
    /// — the verdict tab-strip promotion reads when no tint distinguishes the current tab.
    /// </summary>
    public struct OptionBackgroundMarker
    {
        public Rect ScreenRect;
        public Rect VisibleScreenRect;
        public GuiSpace.ClipKey Clip;
        public bool Selected;
    }

    public struct CheckTexMarker
    {
        public Rect ScreenRect;
        public GuiSpace.ClipKey Clip;
        // Lets the cross-clip fusion test match a marker against a pair drawn in a different
        // clip when both are actually visible.
        public Rect VisibleScreenRect;
        public MultiCheckboxState State;

        /// <summary>
        /// The marker came from a RADIO texture, not a checkbox one: State is then On/Off
        /// only (Widgets.RadioButtonDraw has no partial texture) and means chosen. Shares
        /// the marker type because both families are sniffed off the same GUI.DrawTexture
        /// tap and consumed by the same overlap test.
        /// </summary>
        public bool Radio;
    }
}
