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
    /// <summary>
    /// Keyboard-navigable reader for an UNREGISTERED window — no bespoke scope matches its type or
    /// hierarchy, so <see cref="ScopeForWindow"/>'s two lookups both miss. Reads whatever the
    /// window's own <c>DoWindowContents</c> draws through <see cref="WidgetCapture"/> and presents
    /// it as one flat, navigable content region. One instance per open window, constructed as
    /// <see cref="ScopeForWindow"/>'s fallback factory; <see cref="CapturedAnything"/> is the coarse
    /// signal (any raw capture row at all) the attach layer reads to decide between this scope and a
    /// shallower announce for a window that uses none of the captured vocabulary.
    ///
    /// <b>Own region, not the chassis's captured-extras region.</b> The extras region folds through
    /// <c>CapturedRowFolder</c> and DROPS rows carrying no interactive control, which would delete
    /// most of an unknown mod window (a plain Label here is navigable read-only context); it also
    /// addresses vanilla by DESCRIPTOR, which cannot name one thumb of a
    /// <see cref="WidgetKind.Range"/>, one member of a fused pair, or one button of a stepper run —
    /// all driven here by capture INDEX. <see cref="IncludeCapturedExtrasRegion"/> therefore stays
    /// false and <see cref="ScreenScopeDrawPatch"/> never claims these windows.
    ///
    /// <b>Bracketing.</b> The patch brackets <see cref="WidgetCapture.BeginPass"/>/
    /// <see cref="WidgetCapture.EndPass"/> around the whole private, non-virtual
    /// <c>Window.InnerWindowOnGUI(int)</c>: <c>DoWindowContents</c> is abstract (no shared body to
    /// patch) and <c>WindowOnGUI</c> is overridable, while InnerWindowOnGUI is universal, is the
    /// sole caller of DoWindowContents, and wraps it in the window's own BeginGroup so captured
    /// rects are already window-local. Bracketing the whole call deliberately includes the window's
    /// close-X/close-button chrome, which a sighted player can click too.
    /// The patch resolves its instance by window identity via <see cref="byWindow"/>, never
    /// <c>FocusStack.Top</c> — several such scopes coexist (an unregistered dialog can open
    /// another). Unity draws one window's InnerWindowOnGUI at a time, so each scope safely
    /// snapshots the shared capture list into its own <see cref="captureRows"/> inside its own
    /// <see cref="OnGuiPass"/>.
    ///
    /// <b>Presentation model.</b> Navigation, injection and announcements read
    /// <see cref="presentationRows"/>, rebuilt every pass from the raw capture snapshot by
    /// <see cref="BuildPresentationRows"/> (which documents the two-phase algorithm): a heading
    /// Label becomes section context rather than a row; a blank-labeled Slider/TextField/
    /// CheckboxMulti fuses with its screen-adjacent preceding Label; an InvisibleButton fuses with
    /// the nearest overlapping preceding Label (presenting as Checkbox/RadioButton when a
    /// <see cref="CheckTexMarker"/> also overlaps, since only the drawn texture carries the state);
    /// labeled composites fold on the composite brackets' <see cref="CompositeMember"/> stamps
    /// rather than geometry; a ToggleableIcon button is promoted to a Checkbox and a ColorBox
    /// swatch to a RadioButton; a Listing_Tree node folds to one
    /// <see cref="ElementRole.TreeItem"/> row whose Enter posts on the expander so vanilla's
    /// <c>SetOpen</c> runs; and a Range splits into TWO <see cref="ElementRole.Slider"/> rows over
    /// one capture index, one per vanilla thumb, adjusting through
    /// <see cref="WidgetCapture.RequestAdjustRange"/> with the core's "min - max" text consumed
    /// into their shared caption.
    /// Every injection call and the focus-ring argument to <see cref="WidgetCapture.BeginPass"/>
    /// must use a CAPTURE index — <see cref="PresentationRow.ActivateCaptureIndex"/> or
    /// <see cref="PresentationRow.RingCaptureIndex"/> — never the presentation index; only
    /// <see cref="ListModel"/>/typeahead/section logic operates on presentation indices.
    ///
    /// <b>Element model.</b> Non-heading <see cref="WidgetKind.Label"/> rows stay navigable and are
    /// announced read-only; a heading Label becomes no row at all and names the section every row
    /// below it IN ITS OWN COLUMN belongs to (see <see cref="SectionOwnership"/> — ownership is
    /// geometric, not draw order, so a heading never bleeds into a sibling column), spoken via
    /// <see cref="ApplySectionPrefix"/> and the PageUp/PageDown jumps. Window CHROME — anything
    /// drawn outside the group InnerWindowOnGUI wraps DoWindowContents in (see
    /// <see cref="ResolveWindowChromeRows"/>) — belongs to no section, names none, and never counts
    /// toward <see cref="HasMultipleSections"/>, while staying present and operable.
    ///
    /// <b>Own-label slider value rule.</b> Whether a value needs speaking turns on the slider's own
    /// captured <see cref="CapturedWidget.Label"/>, never on RingCaptureIndex vs.
    /// ActivateCaptureIndex. A non-blank own label already holds the only value a sighted player
    /// sees, so <see cref="BuildDescription"/> speaks it once, never beside an invented raw float;
    /// when it is ONLY the value and a same-line name Label fused onto it (OwnLabelIsValue), it is
    /// spoken as the value under that name. A blank own label means <see cref="BuildDescription"/>
    /// and <see cref="AnnounceStateChange"/> speak the refreshed SliderValueText or the
    /// FormatSliderValue guess, keeping AtMinimum/AtMaximum either way. The same test governs
    /// <see cref="RimWorldAccess.CapturedRowFolder"/>'s ITab slider path.
    ///
    /// <b>Activation.</b> Checkbox/RadioButton/Button post
    /// <see cref="WidgetCapture.RequestActivate"/>, so vanilla's own click handling and side
    /// effects run unmodified on the next pass; Slider posts
    /// <see cref="WidgetCapture.RequestAdjust"/> on Left/Right (Enter merely re-reads the row);
    /// Range posts <see cref="WidgetCapture.RequestAdjustRange"/> inside vanilla's gap clamp;
    /// TextField opens a modal <see cref="TextFieldEditSession"/> posting
    /// <see cref="WidgetCapture.RequestTextOverride"/> every pass (forcing the RETURN value is the
    /// only lever when there is no backing field to reflect into). All target the focused row's
    /// ActivateCaptureIndex, except <see cref="OnSelectRow"/>, which targets a
    /// CheckboxLabeledSelectable's second hotspot
    /// (<see cref="PresentationRow.SecondaryCaptureIndex"/>) — vanilla gives that control two
    /// disjoint click targets.
    ///
    /// <b>Tab-bar grammar.</b> A tab is a section of a screen: Tab/Shift+Tab switch tabs, the
    /// arrows navigate inside the current one. The strip is whatever <see cref="WidgetCapture"/>
    /// recorded as <see cref="WidgetKind.Tab"/> rows, including a hand-rolled button strip
    /// <see cref="PromoteHandRolledTabStrip"/> recognised from its layout; the claims arm only at
    /// two or more (<see cref="HasTabBar"/>) and the switch rides the record's own
    /// <c>clickedAction</c> via <see cref="ActivateTabRow"/>, never a hand-set selection.
    /// <see cref="CycleTab"/> cycles from the tab vanilla draws as selected and speaks one
    /// utterance carrying the tab's position; only that path suppresses the deferred state-change
    /// channel, since Enter on a tab row keeps its own. Once <see cref="HasTabBar"/> is true, Tab
    /// rows leave <see cref="presentationRows"/> entirely (guarded so a window that is nothing but
    /// a strip does not go arrow-dead), so <see cref="CycleTab"/>/<see cref="HasTabBar"/> read the
    /// separate <see cref="tabRows"/> snapshot and the cursor lands on the first presentation row
    /// after a cycle.
    ///
    /// Checkbox/RadioButton/Slider/Tab state changes are announced DEFERRED: the tap records what a
    /// pass DRAWS, and the mod's own field assignment lands after that pass's capture, so an
    /// immediate announcement would speak the pre-request state.
    /// <see cref="ArmPendingStateChange"/>/<see cref="ResolvePendingStateChange"/> snapshot the
    /// row and hold the utterance until a later pass differs, with a 3-pass timeout that announces
    /// the unchanged state anyway — the honest "the mod clamped or rejected it" case.
    ///
    /// <b>Safety valve.</b> Every claim is gated on <see cref="HasInteractiveElements"/>: a
    /// <see cref="GracePasses"/>-pass benefit of the doubt (windows can draw nothing on frame 1),
    /// then permanent passivity if no non-Label presentation row was ever seen; once one is seen
    /// the gate stays open, so it never flaps. An orphan InvisibleButton and a LEAF tree row do not
    /// count; a fused pair, a marker-proven uncaptioned control, and an openable tree row do.
    ///
    /// <b>Escape/Accept posture.</b> <see cref="OwnsCancel"/> is STATE-BASED because
    /// <see cref="WindowCancelKeyRouterPatch"/> consults it from the WINDOW pass, which can run
    /// before the dispatcher's main pass — a constant false left search-active Escape falling
    /// through to the unknown window's <c>closeOnCancel</c> chain before
    /// <see cref="ShellFrameStamps.MarkCancelConsumed"/> could land (the stamp remains as the belt
    /// for main-pass-first orderings). For a COEXISTING (non-absorbing) window the scope also owns
    /// cancel outside search: vanilla Escape there opens the pause menu over the still-open window,
    /// stranding a keyboard user, so the claim closes it via vanilla <c>Close</c>. An ABSORBING
    /// window is claimed too — in-game the dispatcher runs first and its modal swallow eats Escape
    /// before closeOnCancel ever sees it — but <see cref="OnCancelClaim"/> re-enters
    /// <c>Find.WindowStack.Notify_PressedCancel()</c> (vehicle A) guarded on the window still being
    /// on the stack, and <see cref="OwnsCancel"/> deliberately excludes that case so the router does
    /// not block the re-entrant call. <see cref="OwnsAccept"/> is TRUE: Enter already does the
    /// per-element thing via injection, so vanilla's deferred Accept re-test must not also fire.
    ///
    /// <b>Known gaps.</b> A captionless ButtonInvisible with nothing proving it is a control (no
    /// tooltip names it, no caption is written inside it: <see cref="FindEnclosedLabelAhead"/>) is
    /// dropped as an orphan. Label/control fusion is a
    /// screen-space heuristic under a two-tier clip rule: same clip context compares raw screen
    /// rects, a cross-clip pair fuses only when both VISIBLE rects are non-empty and overlap, so a
    /// scrolled-out row can never fuse with or steal the tooltip of an unrelated widget (see
    /// <see cref="GuiSpace.ClipKey"/> and <see cref="MayFuse"/>). A caption laid out far from its
    /// control can still be missed, and a hand-rolled grid making no Widgets calls is invisible
    /// here by construction — which is what the safety valve is for.
    /// </summary>
    public sealed partial class GenericWindowScope : ScreenScope
    {
        private const string IncreaseActionId = "genericWindow.setting.increase";
        private const string DecreaseActionId = "genericWindow.setting.decrease";
        private const string StepperIncreaseActionId = "genericWindow.stepper.increase";
        private const string StepperDecreaseActionId = "genericWindow.stepper.decrease";
        private const string JumpToPreviousSectionActionId = "genericWindow.jumpToPreviousSection";
        private const string JumpToNextSectionActionId = "genericWindow.jumpToNextSection";
        private const string SelectRowActionId = "genericWindow.row.select";
        private const int GracePasses = 3;

        private static readonly Dictionary<Window, GenericWindowScope> byWindow = new Dictionary<Window, GenericWindowScope>();

        private readonly Window window;
        private readonly TextFieldEditSession session = new TextFieldEditSession();

        // Raw draw-order snapshot, kept only for ResolveWindowTitle's first-row fallback and the
        // CapturedAnything signal; everything else reads presentationRows.
        private readonly List<CapturedWidget> captureRows = new List<CapturedWidget>();
        private readonly List<PresentationRow> presentationRows = new List<PresentationRow>();

        /// <summary>
        /// Every captured <see cref="WidgetKind.Tab"/> row, extracted by
        /// <see cref="BuildPresentationRows"/> BEFORE the tab-bar exclusion — what
        /// <see cref="HasTabBar"/> and <see cref="CycleTab"/> read once tabs have left
        /// <see cref="presentationRows"/>.
        /// </summary>
        private readonly List<PresentationRow> tabRows = new List<PresentationRow>();

        /// <summary>
        /// Strip position <see cref="CycleTab"/> last switched to, or -1 before the first switch.
        /// Read only when no tab reports itself as selected.
        /// </summary>
        private int lastCycledTabIndex = -1;

        /// <summary>
        /// Last known state of every Label row promoted to a read-only checkbox, keyed by label
        /// text. A call site that gates its own <c>DrawTextureFitted</c> call on Repaint skips the
        /// capture tap on other passes, so a marker-less pass keeps the row's last confirmed state
        /// instead of flickering back to a bare Label; a pass with the marker always overwrites.
        /// Bounded by the window's row count; stale entries are never looked up again.
        /// </summary>
        private readonly Dictionary<string, MultiCheckboxState> readOnlyCheckboxLatch = new Dictionary<string, MultiCheckboxState>();

        private readonly HashSet<string> highlightToggleLabels = new HashSet<string>();

        /// <summary>
        /// Snapshot of the acted-on row's comparable state at request time, armed by
        /// <see cref="ArmPendingStateChange"/> and resolved by
        /// <see cref="ResolvePendingStateChange"/>.
        /// </summary>
        private struct PendingStateChange
        {
            public int PresentationIndex;
            public CheckState? Check;
            public float SliderValue;
            public bool Selected;
            // A stepper's comparable state: vanilla rewrites IntEntry's editBuffer on every click.
            // Constant "" for kinds carrying no text, so it never perturbs the other comparisons.
            public string Text;
            // Callers often draw the value INTO the caption of a control that shows none of its own
            // (the Listing_Standard.IntAdjuster shape), making the label the only moving observable.
            public string Label;
            // A tree row's branch state; null elsewhere (and on a leaf).
            public bool? Expanded;
            // Both thumbs: moving one can push the other through vanilla's gap clamp, and either
            // move is a real change. Constant 0 for every other kind.
            public float RangeLow;
            public float RangeHigh;
            public int PassesLeft;

            /// <summary>
            /// presentationRows.Count at arm time, for <see cref="TryAnnounceContentChange"/>'s
            /// row-count-delta test — a silent whole-page swap leaves every state field above equal
            /// (or removes the row), so this is the only signal that anything happened.
            /// </summary>
            public int RowCountAtArm;

            /// <summary>
            /// Every Label row's text at arm time, index-aligned with presentationRows (null for
            /// non-Label rows). A pager button can swap a SIBLING label in place while the acted row
            /// and the row count stay identical; that changed text is the activation's outcome.
            /// </summary>
            public List<string> LabelTextsAtArm;
        }

        private PendingStateChange? pendingStateChange;
        private bool pendingAnnounce;

        /// <summary>
        /// The <see cref="BuildTabFrameText"/> utterance a deferred <see cref="AnnounceCurrent"/>
        /// prepends once armed by <see cref="CycleTab"/>. Null when no switch is in flight; always
        /// cleared the next time <see cref="AnnounceCurrent"/> runs so a stale frame cannot leak
        /// onto a later announcement.
        /// </summary>
        private string pendingTabFramePrefix;

        /// <summary>
        /// Holds a tab switch's deferred announcement until the row list SETTLES (two consecutive
        /// passes agreeing on the row count), since the switched-to page may draw a partial surface
        /// on the pass the injected click lands; <see cref="announceSettlePassesLeft"/> is the
        /// honesty deadline for a page whose count never stops moving. Only <see cref="CycleTab"/>
        /// arms this — every other <see cref="pendingAnnounce"/> user reads a page already drawn.
        /// </summary>
        private bool announceSettleArmed;
        private int announceSettleCount;
        private int announceSettlePassesLeft;

        /// <summary>
        /// True from <see cref="OnFocus"/> until the next <see cref="AnnounceCurrent"/>, so a
        /// landing on a tabbed window names the tab it landed on. Armed only from
        /// <see cref="OnFocus"/>, never from <see cref="ReAnnounceRow"/>: a text-edit exit re-reads
        /// the row it left and must not re-speak the tab frame.
        /// </summary>
        private bool entryTabFramePending;

        /// <summary>Which deferred navigation step <see cref="PendingReveal"/> completes once an armed scroll nudge lands.</summary>
        private enum RevealAction { Next, Previous, First, Last }

        /// <summary>
        /// A Move/JumpBoundary armed instead of running synchronously because the focused row sat
        /// at a culled scroll boundary with content beyond it — see <see cref="TryArmBoundaryNudge"/>/
        /// <see cref="TryArmEdgeNudge"/> and <see cref="ResolvePendingReveal"/>. The arming pass
        /// moves and announces nothing; the step completes silently once the nudge lands, or is
        /// abandoned on expiry — exactly one announcement either way.
        /// </summary>
        private struct PendingReveal
        {
            public RevealAction Action;
            public int ContainerIndex;
            public int PassesLeft;
        }

        private PendingReveal? pendingReveal;

        /// <summary>
        /// One-shot: the scope's own text session just ended and already queued
        /// the row's re-read, so the OnFocus that arrives when TextSessionScope
        /// pops off the stack must not queue a second, identical one.
        /// </summary>
        private bool editExitAnnounceQueued;
        private bool announceTitleNext;
        private string cachedTitle;
        private int editingIndex = -1;
        private readonly SectionPrefixTracker sectionPrefix = new SectionPrefixTracker(trackSilentRows: false, speakFirstLanding: true);

        /// <summary>Clip depth in effect when <see cref="BeginDrawPass"/> ran — the window's own level, one shallower than anything <c>DoWindowContents</c> draws. See <see cref="ResolveWindowChromeRows"/>.</summary>
        private int passRootClipDepth;

        private int armedPasses;
        private bool sawInteractive;

        /// <summary>Any row at all (including plain Label rows) was ever captured for this window. Read by the attach layer.</summary>
        public bool CapturedAnything { get; private set; }

        public GenericWindowScope(Window window)
        {
            this.window = window;

            Func<bool> active = delegate { return ScreenActive; };
            // Left/Right adjust whatever VALUE control has focus: a slider, or one end of a range
            // (presented as two Slider rows, so the same claims widen rather than gaining siblings).
            Func<bool> adjustableFocused = delegate
            {
                return active() && (CurrentKind == WidgetKind.Slider || CurrentKind == WidgetKind.Range);
            };
            Func<bool> stepperFocused = delegate { return active() && CurrentKind == WidgetKind.Stepper; };
            // Cancel is claimed unconditionally once active; the chassis's own Cancel claim is
            // registered FIRST and takes the key while a search is live, so search-clear never
            // reaches here. See the class remarks (and OnCancelClaim) for the coexisting- and
            // absorbing-window branches.
            Func<bool> cancelClaim = delegate
            {
                return active();
            };
            Func<bool> hasSections = delegate { return active() && HasMultipleSections(); };
            // A Widgets.CheckboxLabeledSelectable row offers TWO disjoint hotspots a sighted player
            // clicks separately (Verse/Widgets.cs:1287/:1296), so it needs two keys.
            Func<bool> selectableRowFocused = delegate { return active() && CurrentIsSelectableRow; };
            Func<bool> tabBarPresent = delegate { return active() && HasTabBar(); };
            // The bottom button bar's Left/Right; reached because the chassis's horizontal
            // claims stand down on a plain button row.
            Func<bool> barButtonFocused = delegate
            {
                return active() && buttonBarStart >= 0 && RowIndex >= buttonBarStart;
            };

            // The value chords stay this screen's own: a slider row and a stepper row answer the
            // SAME arrows with different vehicles and different chord lists (the stepper's modified
            // chords are required — vanilla reads the physical modifier during the injected click,
            // see StepStepper), which one CanAdjustContentItem seam cannot express, so that seam
            // stays false and the chassis's horizontal claims stand down.
            Claim(IncreaseActionId, OnIncrease, when: adjustableFocused);
            Claim(DecreaseActionId, OnDecrease, when: adjustableFocused);
            Claim(StepperIncreaseActionId, OnStepperIncrease, when: stepperFocused);
            Claim(StepperDecreaseActionId, OnStepperDecrease, when: stepperFocused);
            Claim(JumpToPreviousSectionActionId, OnJumpToPreviousSection, when: hasSections);
            Claim(JumpToNextSectionActionId, OnJumpToNextSection, when: hasSections);
            // Space on a CheckboxLabeledSelectable composite means the narrower "select this row";
            // a real claim beats the chassis's Space-activates fallback, and Enter still reaches
            // the check itself.
            Claim(SelectRowActionId, OnSelectRow, when: selectableRowFocused);
            Claim(SharedMenuGrammar.Cancel, OnCancelClaim, when: cancelClaim);
            // Tab/Shift+Tab switch TABS here, never regions; EnableRegionCycling is false so the
            // chassis's Tab claims stand down. Dormant on a window with no strip.
            Claim(SharedMenuGrammar.NextRegion, OnNextTab, when: tabBarPresent);
            Claim(SharedMenuGrammar.PreviousRegion, OnPreviousTab, when: tabBarPresent);
            Claim(SharedMenuGrammar.NextHorizontal, e => MoveWithinButtonBar(1), when: barButtonFocused);
            Claim(SharedMenuGrammar.PreviousHorizontal, e => MoveWithinButtonBar(-1), when: barButtonFocused);
        }

        // The ScreenScope contract: one content region over presentationRows.

        /// <summary>
        /// One region, always — never flipped to zero by the safety valve, which would rebuild the
        /// ListModel and lose the row cursor every time a float menu opened over the window. The
        /// valve stands the chassis's claims down via <see cref="ContentItemOwnsNavigationKeys"/>.
        /// </summary>
        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>Never spoken — a one-region screen announces no region frame; this names the window for the debug surface.</summary>
        protected override string ContentRegionName(int region)
        {
            return ResolveWindowTitle() ?? "";
        }

        protected override int ContentItemCount(int region)
        {
            return presentationRows.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (index < 0 || index >= presentationRows.Count)
            {
                return new ElementDescription();
            }
            ElementDescription d = BuildDescription(presentationRows[index]);
            // Bar buttons count among themselves ("2 of 3"), not among every row of the window.
            if (buttonBarStart >= 0 && index >= buttonBarStart && !d.PositionIndex.HasValue)
            {
                d.PositionIndex = index - buttonBarStart + 1;
                d.PositionCount = presentationRows.Count - buttonBarStart;
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            ActivateFocusedRow();
        }

        /// <summary>
        /// No-op: rows are rebuilt by the window's own draw pass (<see cref="OnGuiPass"/>), never
        /// on demand — an arbitrary window's content exists only while it is drawing.
        /// </summary>
        protected override void RefreshContent()
        {
        }

        /// <summary>A captured button already IS a presentation row, so a Buttons region would present each one twice.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Tab belongs to the window's own tab strip here — see the constructor's tab claims.</summary>
        protected override bool EnableRegionCycling
        {
            get { return false; }
        }

        /// <summary>No table region exists on this screen, so Alt+S has nothing to sort.</summary>
        protected override bool EnableSortChord
        {
            get { return false; }
        }

        /// <summary>
        /// The safety valve and foreign-window guard, expressed where the chassis can act on them:
        /// while this screen cannot answer for its window, the chassis's Up/Down/Home/End claims
        /// stand down and the keys fall through untouched.
        /// </summary>
        protected override bool ContentItemOwnsNavigationKeys(int region, int index)
        {
            return !ScreenActive;
        }

        /// <summary>
        /// The row's own label, never the composed announcement: role and state words would pollute
        /// the haystack, and composing one resolves tooltips per row, per keystroke.
        /// </summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            return row >= 0 && row < presentationRows.Count ? presentationRows[row].Label ?? "" : "";
        }

        protected internal override Window OwnedWindow
        {
            get { return window; }
        }

        /// <summary>Whether this screen can answer for its window at all: the safety valve plus the foreign-window guard. Gates every claim.</summary>
        private bool ScreenActive
        {
            get { return HasInteractiveElements && !TextDialogShared.ForeignWindowAbove(window); }
        }

        /// <summary>The focused presentation row's index, or -1 when the cursor is nowhere.</summary>
        private int RowIndex
        {
            get
            {
                ListModel region = Model.CurrentRegion;
                return region == null ? -1 : region.Index;
            }
        }

        /// <summary>Moves the row cursor without announcing — the caller owns the one utterance.</summary>
        private void MoveToRow(int index)
        {
            ListModel region = Model.CurrentRegion;
            if (region != null)
            {
                region.MoveTo(index);
            }
        }

        public override string Name
        {
            get { return "generic-window"; }
        }

        public override bool IsModal
        {
            get { return !TextDialogShared.ForeignWindowAbove(window); }
        }

        /// <summary>
        /// STATE-BASED, and deliberately NARROWER than the constructor's <c>cancelClaim</c> gate:
        /// search-clear, or the coexisting-window close for a non-absorbing window. An absorbing
        /// window is excluded on purpose — <see cref="OnCancelClaim"/> reaches vanilla's close by
        /// re-entering this window's patched <c>OnCancelKeyPressed</c>, and claiming cancel here
        /// would make <see cref="WindowCancelKeyRouterPatch"/> block that re-entrant call.
        /// </summary>
        public override bool OwnsCancel
        {
            get
            {
                return ScreenActive && (base.OwnsCancel || !window.absorbInputAroundWindow);
            }
        }

        internal bool Owns(Window w)
        {
            return ReferenceEquals(w, window);
        }

        internal static GenericWindowScope For(Window w)
        {
            GenericWindowScope scope;
            return byWindow.TryGetValue(w, out scope) ? scope : null;
        }

        /// <summary>True once a non-Label presentation row has ever been captured, or while the <see cref="GracePasses"/> grace window is still open.</summary>
        private bool HasInteractiveElements
        {
            get { return sawInteractive || armedPasses < GracePasses; }
        }

        private WidgetKind CurrentKind
        {
            get
            {
                int index = RowIndex;
                return (index >= 0 && index < presentationRows.Count) ? presentationRows[index].Kind : WidgetKind.Label;
            }
        }

        /// <summary>
        /// The focused row is a Widgets.CheckboxLabeledSelectable composite — the one control
        /// carrying a SELECTED state alongside its check, with its own click target. Read off the
        /// composite bracket's stamp, never the presented kind, which is a plain Checkbox.
        /// </summary>
        private bool CurrentIsSelectableRow
        {
            get
            {
                int index = RowIndex;
                if (index < 0 || index >= presentationRows.Count)
                {
                    return false;
                }
                CapturedWidget source = presentationRows[index].Source;
                return source != null && source.Composite == CompositeMember.SelectableRowLabel;
            }
        }

        public override void OnPush()
        {
            base.OnPush();
            byWindow[window] = this;
            TooltipCapture.Arm();
            announceTitleNext = true;
            sectionPrefix.Reset();
        }

        public override void OnPop()
        {
            base.OnPop();
            byWindow.Remove(window);
            TooltipCapture.Disarm();
            session.CancelIfActive();
            WidgetCapture.ClearTextOverride();
        }

        public override void OnFocus()
        {
            // Rows come from the window's next draw pass, so the landing announcement is deferred to
            // OnGuiPass; the chassis entry announcement would otherwise fire against whatever the
            // PREVIOUS pass left in the model.
            SuppressNextEntryAnnouncement();
            base.OnFocus();
            if (editExitAnnounceQueued)
            {
                // Returning from this scope's own text session: the exit callback queued the re-read
                // and the row context is unchanged, so the section prefix must not reset either.
                editExitAnnounceQueued = false;
                return;
            }
            pendingAnnounce = true;
            entryTabFramePending = true;
            sectionPrefix.Reset();
        }

    }
}
