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
    /// <summary>
    /// Widget-vocabulary capture engine behind <see cref="GenericWindowScope"/>. A sibling of
    /// <see cref="ListingRowCapture"/>, never an extension: that engine is design-locked to
    /// byte-identical OptionsScope behavior, and a generic scope needs ONE globally-ordered flat
    /// list across every widget kind (draw order carries the "3 of 12" position announcements).
    /// Each engine's own "armed" bool keeps their taps on shared methods from interfering.
    ///
    /// One Widgets-level tap per primitive suffices: every Listing_Standard wrapper in the
    /// vocabulary funnels through one of these primitives with no intermediate transform, and
    /// Harmony's <c>__state</c> carries the draw-order index from prefix to postfix of the same call.
    ///
    /// Tapped primitives: CheckboxLabeled/Checkbox, RadioButtonLabeled, ButtonText,
    /// HorizontalSlider, TextField, TextArea, Label, the icon buttons (ButtonImage/Fitted/
    /// Draggable) and the draggable text/invisible cores. Blank Labels are dropped; ButtonImageWithBG
    /// needs no tap (it funnels through ButtonText("")).
    ///
    /// Brackets stash context around an inner primitive's row instead of recording one:
    /// numeric text fields (TextFieldNumeric/Labeled/Percent/Vector, TextEntryLabeled) carry the
    /// call site's label and bounds; steppers (Widgets.IntEntry, Listing_Standard.IntAdjuster)
    /// mark their children so the scope fuses each run into one Stepper row — IntSetter stays
    /// unbracketed, its single labeled ButtonText already being a correct Button row; composites
    /// (FillableBarLabeled, DefLabelWithIcon, HyperlinkWithIcon, InfoCardButtonWorker,
    /// CheckboxLabeledSelectable, LabelDouble, SelectableDef) stamp member roles so each presents
    /// as the one control a sighted player sees; Listing_Tree's LabelLeft and OpenCloseWidget
    /// carry TreeLevel/TreeOpen so the pair folds into one TreeItem row.
    /// The unlabeled Widgets.RadioButton records only a bare ButtonInvisible, so the CheckTexMarker
    /// tap sniffs RadioButOnTex/RadioButOffTex to promote it.
    /// Ranges (IntRange/FloatRange/QualityRange) are the one family reaching no captured primitive
    /// — vanilla paints their thumbs with raw GUI.DrawTexture and drags them from raw
    /// Event.current — so their brackets RECORD a <see cref="WidgetKind.Range"/> row and stamp the
    /// core's "min - max" Label as <see cref="CompositeMember.RangeText"/>.
    ///
    /// Tooltip text is not captured here: <see cref="TooltipCapture"/> covers the TipRegion
    /// channel mod-wide and GenericWindowScope queries that engine directly.
    ///
    /// Injection: a scope posts a Request* call against the immediately preceding pass; the tap
    /// reaching that target on the NEXT pass performs the injection, so vanilla's own inline
    /// handling (a checkbox flip, a slider step, a dialog the button opens) runs unmodified. A
    /// post that outlives the pass it targeted is dropped at <see cref="EndPass"/>.
    /// RequestTextOverride is the exception — a live text edit spans many passes, so it survives
    /// EndPass and is re-posted every pass by the owning scope's TextFieldEditSession until
    /// <see cref="ClearTextOverride"/>.
    ///
    /// A DETACHED pass disables the scope injection channel entirely
    /// (<see cref="BeginDetachedPass"/>). Its only mutation path is the armed channel
    /// (<see cref="BeginArmedDetachedPass"/> and its Adjust/SliderSet/TextSet siblings): one
    /// widget matched by (kind, label, ordinal) fires its own vanilla handler, at most one per
    /// pass, over fields disjoint from the scope channel's.
    /// </summary>
    [StaticConstructorOnStartup]
    public static partial class WidgetCapture
    {
        private const float FocusRingExpand = 2f;
        private static readonly Color FocusRingColor = new Color(0.45f, 0.78f, 1f);

        /// <summary>Length past which a tooltip promoted to an icon button's name is cut back to its first sentence (<see cref="ShortTipName"/>).</summary>
        private const int MaxTipNameLength = 60;

        private static bool passOpen;

        // Frame BeginPass most recently opened a LIVE pass in — see EndLeakedLivePass. Not
        // meaningful for a detached pass, whose bracket is always synchronous within one call.
        private static int passOpenFrame;
        private static readonly List<CapturedWidget> items = new List<CapturedWidget>();

        // Screen-space checkbox-texture markers recorded this pass (see CheckTexMarker). One
        // shared list for live and detached passes; both start from an empty one.
        private static readonly List<CheckTexMarker> checkTexMarkers = new List<CheckTexMarker>();

        // Widgets.DrawOptionBackground calls recorded this pass (see OptionBackgroundMarker).
        private static readonly List<OptionBackgroundMarker> optionBackgroundMarkers = new List<OptionBackgroundMarker>();

        // Sink indexes at which a Listing.NewColumn ran this pass: rows recorded at or past a
        // break belong to the next column, which the presentation sort reads to order a
        // multi-column listing column-by-column instead of zigzagging across its bands.
        private static readonly List<int> listingColumnBreaks = new List<int>();

        // Generic scroll-follow: mods that cull rows outside their ScrollView's visible band never
        // issue a draw call for a culled row, so a list silently truncates. Every cull decision
        // reads scroll state flowing through Widgets.BeginScrollView(Rect, ref Vector2, Rect, bool)
        // and written back before content draws — the same state the mouse wheel mutates — so a
        // prefix nudging the ref Vector2 propagates into every cull decision that frame with zero
        // mod-specific code. Gated on an explicit armed request (ArmScrollNudge) so it never runs
        // unsolicited. Tracked only during a LIVE pass (an inspection-harness capture stays
        // read-only): every entry point below gates on passOpen && !detachedPass.

        /// <summary>
        /// One Widgets.BeginScrollView/EndScrollView bracket recorded during the current/most
        /// recent LIVE pass. Containers have no cross-pass identity of their own, so
        /// <see cref="Index"/> (position among the pass's BeginScrollView calls, in draw order)
        /// plus <see cref="OutRectScreen"/> are the tuple an armed nudge re-matches against; a
        /// moving or resized window invalidates the nudge rather than guessing.
        /// </summary>
        public sealed class ScrollContainer
        {
            public int Index;
            public Rect OutRectScreen;
            public float OutHeight;
            public float ViewHeight;
            public Vector2 ScrollPosition;
            public GuiSpace.ClipKey Clip;

            /// <summary>True when this pass's BeginScrollView call consumed the armed nudge and mutated its ref scrollPosition.</summary>
            public bool NudgeLandedThisPass;
        }

        /// <summary>A one-shot request to move a container's scroll offset before its NEXT BeginScrollView call — see ArmScrollNudge.</summary>
        private struct ScrollNudgeRequest
        {
            public int ContainerIndexAtArm;
            public Rect OutRectScreenAtArm;
            public float RequestedY;
            public int PassesRemaining;
        }

        private static readonly List<ScrollContainer> scrollContainers = new List<ScrollContainer>();
        private static readonly List<int> containerStack = new List<int>();
        private static ScrollNudgeRequest? armedNudge;
        private static int nudgeLandedContainerIndexThisPass = -1;

        /// <summary>The innermost open ScrollContainer's index for a row being recorded right now, or -1 outside every container — see CapturedWidget.ContainerIndex.</summary>
        internal static int CurrentContainerIndexForRecording
        {
            get { return containerStack.Count > 0 ? containerStack[containerStack.Count - 1] : -1; }
        }

        /// <summary>True while a nudge is armed and has not yet landed (or expired) — callers serialize on this so at most one nudge is ever in flight.</summary>
        public static bool HasArmedNudge
        {
            get { return armedNudge.HasValue; }
        }

        /// <summary>The ScrollContainer index an armed nudge landed on THIS pass, or -1 if none landed. Reset every BeginPass.</summary>
        public static int NudgeLandedContainerIndex
        {
            get { return nudgeLandedContainerIndexThisPass; }
        }

        /// <summary>Read-only snapshot of every container recorded by the current/most recent LIVE pass, in draw order (index-aligned with each entry's own Index).</summary>
        public static IReadOnlyList<ScrollContainer> ScrollContainers
        {
            get { return scrollContainers; }
        }

        public static bool TryGetScrollContainer(int index, out ScrollContainer container)
        {
            if (index >= 0 && index < scrollContainers.Count)
            {
                container = scrollContainers[index];
                return true;
            }
            container = null;
            return false;
        }

        /// <summary>
        /// Arms a one-shot scroll nudge: the NEXT pass's BeginScrollView call whose
        /// (sequential index, screen outRect) matches the tuple recorded THIS pass clamps its
        /// scrollPosition.y to <paramref name="requestedY"/>. Expires after a few passes if no
        /// matching container shows up. Only ever called by the live GenericWindowScope reading
        /// this same window's capture — never self-armed.
        /// </summary>
        public static void ArmScrollNudge(int containerIndex, Rect outRectScreen, float requestedY)
        {
            armedNudge = new ScrollNudgeRequest
            {
                ContainerIndexAtArm = containerIndex,
                OutRectScreenAtArm = outRectScreen,
                RequestedY = requestedY,
                PassesRemaining = 3,
            };
        }

        private const float ScrollRectMatchTolerance = 1.5f;

        /// <summary>
        /// Whether the scroll viewport a nudge was armed against is the one this pass is drawing.
        /// SIZE only, deliberately: a window may MOVE between the arming pass and the landing one
        /// (dragged, or recentering itself as it resizes), and testing position made every such
        /// nudge expire with the cursor's row scrolled out of sight. Identity does not rest on
        /// this test alone — the caller pairs it with the container's sequential index — so only
        /// two same-sized viewports swapping places within one window could confuse it.
        /// </summary>
        private static bool ScrollRectsMatch(Rect a, Rect b)
        {
            return Mathf.Abs(a.width - b.width) <= ScrollRectMatchTolerance
                && Mathf.Abs(a.height - b.height) <= ScrollRectMatchTolerance;
        }

        /// <summary>Widgets.BeginScrollView core prefix: pushes the container and applies an armed nudge if this is its match.</summary>
        internal static void RecordScrollContainerBegin(Rect outRect, ref Vector2 scrollPosition, Rect viewRect)
        {
            if (!passOpen || detachedPass)
            {
                return;
            }
            int index = scrollContainers.Count;
            Rect outRectScreen = GuiSpace.ToScreen(outRect);
            ScrollContainer container = new ScrollContainer
            {
                Index = index,
                OutRectScreen = outRectScreen,
                OutHeight = outRect.height,
                ViewHeight = viewRect.height,
                ScrollPosition = scrollPosition,
                Clip = GuiSpace.CurrentClip(),
            };
            if (armedNudge.HasValue)
            {
                ScrollNudgeRequest req = armedNudge.Value;
                if (req.ContainerIndexAtArm == index && ScrollRectsMatch(req.OutRectScreenAtArm, outRectScreen))
                {
                    float maxY = Mathf.Max(0f, viewRect.height - outRect.height);
                    scrollPosition.y = Mathf.Clamp(req.RequestedY, 0f, maxY);
                    container.ScrollPosition = scrollPosition;
                    container.NudgeLandedThisPass = true;
                    nudgeLandedContainerIndexThisPass = index;
                    armedNudge = null;
                }
            }
            scrollContainers.Add(container);
            containerStack.Add(index);
        }

        /// <summary>Widgets.EndScrollView core: pops the container stack pushed by RecordScrollContainerBegin.</summary>
        internal static void RecordScrollContainerEnd()
        {
            if (!passOpen || detachedPass)
            {
                return;
            }
            if (containerStack.Count > 0)
            {
                containerStack.RemoveAt(containerStack.Count - 1);
            }
        }

        // Embedded vanilla pawn tables: any window hosting a live RimWorld.PawnTable draws it
        // through the single entry point PawnTable.PawnTableOnGUI, whose geometry is derivable
        // from the table's own public properties. Noting it lets presentation fuse the per-cell
        // capture fragments into one row per visual pawn row and name header cells from the
        // table's column defs — gated on a REAL vanilla PawnTable having drawn, so no other
        // layout can match by coincidence.

        /// <summary>One vanilla PawnTable that drew during the current/most recent LIVE pass.</summary>
        public sealed class PawnTableNote
        {
            public PawnTable Table;

            /// <summary>The header band (window GUI space converted to screen), one cell per visible column.</summary>
            public Rect HeaderRectScreen;

            /// <summary>The body scroll viewport, same space as <see cref="ScrollContainer.OutRectScreen"/> — match by ScrollRectsMatch + containment, never by clip.</summary>
            public Rect BodyOutRectScreen;

            /// <summary>Per-visible-column header cell rects (same space as <see cref="HeaderRectScreen"/>), index-aligned with <see cref="ColumnDefs"/>.</summary>
            public List<Rect> ColumnHeaderRectsScreen = new List<Rect>();
            public List<PawnColumnDef> ColumnDefs = new List<PawnColumnDef>();
        }

        private static readonly List<PawnTableNote> pawnTableNotes = new List<PawnTableNote>();

        /// <summary>Every vanilla PawnTable the current/most recent LIVE pass drew, in draw order.</summary>
        public static IReadOnlyList<PawnTableNote> PawnTableNotes
        {
            get { return pawnTableNotes; }
        }

        private static readonly AccessTools.FieldRef<PawnTable, List<float>> pawnTableColumnWidths =
            AccessTools.FieldRefAccess<PawnTable, List<float>>("cachedColumnWidths");

        /// <summary>
        /// PawnTable.PawnTableOnGUI prefix body: records the table and its header/body geometry,
        /// mirroring the vanilla method's own layout math (RimWorld/PawnTable.cs:138-148). The
        /// caller wraps this in a try/catch — a mod's broken table must never break the pass.
        /// </summary>
        internal static void RecordPawnTable(PawnTable table, Vector2 position)
        {
            if (!passOpen || detachedPass || table == null)
            {
                return;
            }
            Vector2 size = table.Size;
            float headerHeight = table.HeaderHeight;
            if (size.x <= 1f || size.y <= headerHeight)
            {
                return;
            }
            var note = new PawnTableNote
            {
                Table = table,
                HeaderRectScreen = GuiSpace.ToScreen(new Rect(position.x, position.y, size.x, headerHeight)),
                BodyOutRectScreen = GuiSpace.ToScreen(new Rect(position.x, position.y + headerHeight, size.x, size.y - headerHeight)),
            };
            List<PawnColumnDef> columns = table.Columns;
            List<float> widths = pawnTableColumnWidths(table);
            if (columns != null && widths != null && widths.Count >= columns.Count)
            {
                float usable = size.x - 16f;
                int x = 0;
                for (int i = 0; i < columns.Count; i++)
                {
                    int width = i != columns.Count - 1 ? (int)widths[i] : (int)(usable - x);
                    note.ColumnDefs.Add(columns[i]);
                    note.ColumnHeaderRectsScreen.Add(GuiSpace.ToScreen(
                        new Rect((int)position.x + x, (int)position.y, width, (int)headerHeight)));
                    x += width;
                }
            }
            pawnTableNotes.Add(note);
        }

        // Detached pass (inspection capture harness): records into a caller sink instead of items
        // with every injection path disabled, so a capture-only pass can run in the same frame as
        // a scope's own pass without clobbering its Items or consuming its pending posts.
        private static bool detachedPass;

        /// <summary>True while an inspection-harness re-render is on the call stack, so observers outside this engine can stay silent for it.</summary>
        internal static bool DetachedPass
        {
            get { return detachedPass; }
        }

        private static List<CapturedWidget> detachedSink;
        private static int savedFocusedIndex;
        private static int savedTabStripDepth;

        // Armed-detached channel (inspect-tab activation): the sanctioned single-activation
        // exception to a detached pass's mutation-inertness. It fires exactly ONE captured
        // control's own vanilla handler, matched by (kind, label, ordinal-among-same-kind+label)
        // as the stream is re-recorded. armedFired/armedFireIndex stay readable AFTER
        // EndDetachedPass and reset only on the next BeginArmedDetachedPass.
        private static bool armedActive;
        private static WidgetKind armedKind;
        private static string armedLabel;
        private static int armedOrdinal;
        private static int armedSeen;
        private static int? armedFireIndex;
        private static bool armedFired;

        // What the armed pass does to the matched widget. Sliders, text fields and tabs carry no
        // vanilla disabled gate, so only Activate can be ArmedGateBlocked.
        private enum ArmedAction { Activate, Adjust, SetSlider, SetText }
        private static ArmedAction armedAction;
        private static int armedAdjustDirection;
        private static float armedSetValue;
        private static string armedSetText;
        // True when the armed widget was matched but vanilla drew it gated (Checkbox/RadioButton
        // `disabled`, ButtonText `!active`): the fire is refused and the caller announces the
        // control as disabled, because a mutator without its gate is a doctrine violation.
        private static bool armedGateBlocked;

        /// <summary>
        /// Where the next Record* writes. Two conditions send it to the scratch list instead of
        /// the real stream, both meaning "a sighted player is not looking at this": the
        /// filter-panel bracket below, and any draw under a clip nobody can see
        /// (<see cref="GuiSpace.ClipIsOffscreen"/> — the off-screen MEASUREMENT PASS every
        /// double-draw window runs). A DETACHED pass is exempt: the inspect-tab harness parks its
        /// draw in a far off-screen group on purpose, so the measurement-pass signal cannot mean
        /// there what it means during a live pass.
        ///
        /// Redirecting the SINK rather than returning early from each Record* is what keeps a
        /// suppressed pass harmless: every index the taps hand out is <c>sink.Count</c>, so a
        /// suppressed run numbers itself from zero and never disturbs the real stream's indexes,
        /// while the live injection channel resolves by descriptor and fires inside the same
        /// Record* call that matched. The scratch list is never read back.
        /// </summary>
        private static List<CapturedWidget> CurrentSink
        {
            get
            {
                if (RecordingSuppressed)
                {
                    return suppressedScratchSink;
                }
                return detachedPass ? detachedSink : items;
            }
        }

        /// <summary>True while Record* calls land in the scratch sink; the descriptor matchers skip such records so a suppressed widget never advances an ordinal count.</summary>
        private static bool RecordingSuppressed
        {
            get { return filterPanelSuppressDepth > 0 || (!detachedPass && ClipUnseen()); }
        }

        /// <summary>
        /// <see cref="GuiSpace.ClipIsOffscreen"/> refined by the scroll containers: a group scrolled
        /// entirely out of an on-screen viewport also reports an empty visible area, yet its rows
        /// are one nudge from view and must record like any scrolled-out row.
        /// </summary>
        internal static bool ClipUnseen()
        {
            return GuiSpace.ClipIsOffscreen() && !InsideOnScreenScrollContent();
        }

        private static bool InsideOnScreenScrollContent()
        {
            if (containerStack.Count == 0)
            {
                return false;
            }
            ScrollContainer container = scrollContainers[containerStack[containerStack.Count - 1]];
            Rect outRect = container.OutRectScreen;
            if (outRect.width <= 0f || outRect.height <= 0f
                || outRect.xMax <= 0f || outRect.yMax <= 0f
                || outRect.xMin >= UI.screenWidth || outRect.yMin >= UI.screenHeight)
            {
                return false;
            }
            Vector2 origin = GuiSpace.ToScreen(new Rect(0f, 0f, 0f, 0f)).position;
            float viewTop = outRect.yMin - container.ScrollPosition.y;
            return origin.x >= outRect.xMin - 1f && origin.x <= outRect.xMax + 1f
                && origin.y >= viewTop - 1f && origin.y <= viewTop + container.ViewHeight + 1f;
        }

        // ThingFilterUI.DoThingFilterConfigWindow suppression bracket: while > 0, CurrentSink
        // redirects every Record* to the scratch list, so the hundreds of rows the vanilla filter
        // tree draws never reach presentation — the one synthetic FilterPanelHandoff row
        // RecordFilterPanel added before this bracket opens stands in for the whole panel.
        private static int filterPanelSuppressDepth;

        /// <summary>
        /// The shared discard list for every suppression reason <see cref="CurrentSink"/> knows
        /// about. Cleared at each <see cref="BeginPass"/> so a window whose measurement pass draws
        /// hundreds of rows cannot accumulate them across frames.
        /// </summary>
        private static readonly List<CapturedWidget> suppressedScratchSink = new List<CapturedWidget>();

        internal static void EnterFilterPanelSuppression()
        {
            if (filterPanelSuppressDepth == 0)
            {
                suppressedScratchSink.Clear();
            }
            filterPanelSuppressDepth++;
        }

        internal static void ExitFilterPanelSuppression()
        {
            if (filterPanelSuppressDepth > 0)
            {
                filterPanelSuppressDepth--;
            }
        }

        /// <summary>TooltipCapture's own gate on the same bracket — see EnterFilterPanelSuppression's remarks.</summary>
        internal static bool FilterPanelSuppressed
        {
            get { return filterPanelSuppressDepth > 0; }
        }

        // Live-channel pendings are DESCRIPTOR-addressed (kind + raw label + ordinal among same
        // kind+label), never index-addressed: an IMGUI stream is not stable frame to frame
        // (hover-dependent rows appear and vanish), so an absolute index posted against one pass
        // can point at nothing — or a different control — on the next. The descriptor is
        // re-resolved by counting during each pass, exactly the armed channel's matching rule, and
        // the post expires after LivePostLifetimeFrames passes.
        private static bool pendingActivateSet;
        private static WidgetKind pendingActivateKind;
        private static string pendingActivateLabel = "";
        private static int pendingActivateOrdinal;
        private static int pendingActivateDeadline;
        private static int liveActivateSeen;
        private static int liveActivateFireIndex = -1;
        private static bool pendingAdjustSet;
        private static string pendingAdjustLabel = "";
        private static int pendingAdjustOrdinal;
        private static int pendingAdjustDeadline;
        private static int pendingAdjustDirection;
        private static bool pendingAdjustFractional;
        private static int liveAdjustSeen;
        private static int liveAdjustFireIndex = -1;
        // Range twin of the adjust channel, with its own fields rather than a flag on the slider
        // one: the two count DIFFERENT streams (RecordSlider vs RecordRange), so sharing
        // liveAdjustSeen would let an unlabeled slider shift a range's ordinal out from under a
        // post. pendingRangeAdjustHigh is which thumb moves — a slider descriptor has no slot for it.
        private static bool pendingRangeAdjustSet;
        private static string pendingRangeAdjustLabel = "";
        private static int pendingRangeAdjustOrdinal;
        private static int pendingRangeAdjustDeadline;
        private static int pendingRangeAdjustDirection;
        private static bool pendingRangeAdjustHigh;
        private static int liveRangeAdjustSeen;
        private static int liveRangeAdjustFireIndex = -1;
        private static int? pendingTextIndex;
        private static string pendingTextValue;

        /// <summary>
        /// How many frames a live descriptor post survives unconsumed. Long enough to ride out the
        /// Layout/Repaint event pairing and one frame of stream flutter; short enough that a post
        /// can never fire into a surface the user has since navigated away from.
        /// </summary>
        private const int LivePostLifetimeFrames = 3;

        // >0 while TabDrawer.DrawTabs is on the call stack — see RecordLabel. A counter, not a
        // bool: DrawTabsOverflow calls the core overload in a loop.
        private static int tabStripDepth;

        // >0 while a self-captioning Widgets primitive is on the call stack (ButtonText,
        // CheckboxLabeled, RadioButtonLabeled, HorizontalSlider, CustomButtonText — each draws its
        // caption through the hooked Label). The captions already ride the control's own row, so
        // RecordLabel drops them by EXACT text match. A slider drawn with vanilla's
        // leftAlignedLabel/rightAlignedLabel fills all three slots; without suppression its
        // caption and value would double-announce as orphan Label rows. Depth is defensive
        // unwinding only — these widgets do not nest, so three caption slots suffice.
        private static int selfCaptionDepth;
        private static string selfCaptionText0;
        private static string selfCaptionText1;
        private static string selfCaptionText2;

        // >0 while Widgets.CheckboxMulti is on the call stack — part of the ButtonInvisible
        // suppression bracket (see RecordInvisibleButton). Vanilla's CheckboxMulti draws via
        // ButtonImageDraggable/ButtonInvisibleDraggable rather than ButtonInvisible, so this is
        // defensive symmetry rather than a currently-live guard.
        private static int checkboxMultiDepth;
        private static int savedCheckboxMultiDepth;

        // >0 while a vanilla Widgets.Dropdown generic body is on the call stack. The opener button
        // it draws records with DropdownOpener set, so presentation speaks the ComboBox role.
        // Stays 0 whenever the open-generic patch could not be applied.
        private static int dropdownDepth;
        private static int savedDropdownDepth;

        // >0 while Listing_Standard.ButtonTextLabeledPct's body is on the call stack — the
        // label+button-in-one-row dropdown-style setting pattern. Its Widgets.ButtonText records
        // with DropdownOpener set, exactly like a real Dropdown opener, so presentation speaks the
        // ComboBox role and fuses with the row's caption Label the same way.
        private static int labeledButtonDepth;
        private static int savedLabeledButtonDepth;

        // >0 while a Widgets.TextFieldNumeric<int|float> body is on the call stack: the inner
        // Widgets.TextField records itself enriched with the stashed int-ness and the call site's
        // min/max. Plain statics rather than stacks because the family never nests two numeric
        // calls (TextFieldNumeric's body is a single TextField call); the depth counters are
        // defensive unwinding only.
        private static int numericFieldDepth;
        private static int savedNumericFieldDepth;
        private static bool numericFieldIsInt;
        private static float numericFieldMin;
        private static float numericFieldMax;

        // >0 while a labeled text-field wrapper (TextFieldNumericLabeled, TextEntryLabeled) is on
        // the call stack — its caption is suppressed by the self-caption bracket and carried on
        // the inner field's own row as FieldLabel instead.
        private static int fieldLabelDepth;
        private static int savedFieldLabelDepth;
        private static string fieldLabelText;

        // >0 while Widgets.TextFieldPercent is on the call stack — its inner numeric field records
        // NumericPercent (see CapturedWidget).
        private static int percentFieldDepth;
        private static int savedPercentFieldDepth;

        // >0 while Widgets.TextFieldVector is on the call stack; its three inner TextFieldNumeric
        // calls are named x/y/z in draw order via vectorAxisIndex (advanced by EnterNumericField).
        private static int vectorFieldDepth;
        private static int savedVectorFieldDepth;
        private static int vectorAxisIndex;
        private static string vectorAxisLabel;

        // >0 while a Widgets.IntEntry / Listing_Standard.IntAdjuster body is on the call stack:
        // the ButtonText children record as stepper members identified by stepperButtonOrdinal
        // (reset on Enter), and IntEntry's center TextFieldNumeric marks itself the value field.
        // Neither composite nests itself or the other, so plain statics suffice and the depth
        // counters are defensive unwinding only.
        private static int intEntryDepth;
        private static int savedIntEntryDepth;
        private static int intAdjusterDepth;
        private static int savedIntAdjusterDepth;
        private static int stepperButtonOrdinal;

        // Labeled-composite brackets, all the same shape as the counters above: unconditional
        // increment, guarded decrement, plain static stashes because none of these composites
        // nests itself or another (their bodies are flat Label/button/bar sequences).
        //
        // >0 while Widgets.FillableBarLabeled's body draws: its caption Label is suppressed and
        // carried on the bar's own row as FieldLabel.
        private static int fillableBarLabelDepth;
        private static int savedFillableBarLabelDepth;
        private static string fillableBarLabelText;

        // >0 while Listing_Standard.LabelDouble's body draws: the two halves are ONE "name: value"
        // row, so the right half is stashed here and folded into the left half's record.
        private static int labelDoubleDepth;
        private static int savedLabelDoubleDepth;
        private static string labelDoubleRightText;
        private static int labelDoubleSlot;

        // >0 while Widgets.DefLabelWithIcon's body draws: the def description it registers as a
        // tooltip is carried on the inner Label's row, because vanilla registers it OUTSIDE the
        // BeginGroup the Label draws in and the two land in different clip contexts.
        private static int defLabelIconDepth;
        private static int savedDefLabelIconDepth;
        private static string defLabelIconTip;

        // >0 while Widgets.HyperlinkWithIcon's body draws: its ButtonText caption and the
        // ButtonInvisible over the same rect are stamped as two halves of one link row, and the
        // caption's `active: false` paint flag is not mistaken for a gate — hyperlinkHidden
        // carries the only real one (a hidden link's ActivateHyperlink returns immediately).
        private static int hyperlinkDepth;
        private static int savedHyperlinkDepth;
        private static bool hyperlinkHidden;

        // >0 while Widgets.CheckboxLabeledSelectable's body draws: its two ButtonInvisible targets
        // are told apart by draw order (the row-select one draws first and ONLY while the row is
        // unselected), and the caption Label carries the row's selected state.
        private static int selectableRowDepth;
        private static int savedSelectableRowDepth;
        private static bool selectableRowSelected;
        private static int selectableRowOrdinal;

        // >0 while Listing_Standard.SelectableDef's body draws: its delete ButtonImage is named
        // after the row it deletes and its full-row ButtonInvisible is the row's click target.
        private static int selectableDefDepth;
        private static int savedSelectableDefDepth;
        private static string selectableDefName;

        // >0 while Widgets.InfoCardButtonWorker's body draws: its icon button gets the localized
        // info-card name instead of a texture asset name.
        private static int infoCardDepth;
        private static int savedInfoCardDepth;

        // Listing_Tree row brackets. A tree node draws its expander and its label through two
        // SEPARATE protected members (OpenCloseWidget then LabelLeft), so each gets its own
        // bracket and the two stamped rows are paired at presentation. Neither body nests the
        // other or itself, so plain statics carry the stashes.
        //
        // >0 while Listing_Tree.OpenCloseWidget's body draws.
        private static int listingTreeExpanderDepth;
        private static int savedListingTreeExpanderDepth;
        private static bool listingTreeExpanderOpen;

        // >0 while Listing_Tree.LabelLeft's body draws; the stash is the caller's UNTRUNCATED label.
        private static int listingTreeLabelDepth;
        private static int savedListingTreeLabelDepth;
        private static string listingTreeLabelText;

        // The tip vanilla itself paired with this node. LabelLeft registers it over a rect of
        // exactly lineHeight and THEN inflates the label rect 5px top and bottom before drawing
        // (Verse/Listing_Tree.cs:38-54), so the recorded row overlaps its neighbours' tip rects
        // and a geometric lookup would join all of them onto one row.
        private static string listingTreeTipText;

        // The indentLevel argument of whichever of the two is on the stack.
        private static int listingTreeIndentLevel;

        // Range brackets. Unlike every other bracket here these DO record a row of their own — a
        // range has no captured click target to enrich — but they behave like brackets for the one
        // Label the core draws inside them: RecordLabel stamps it CompositeMember.RangeText rather
        // than letting it record as an orphan caption. A depth counter rather than the exact-match
        // self-caption bracket, because FloatRange draws its text through LabelFit, whose third
        // branch ellipsizes it and would then miss an exact match.
        //
        // >0 while a Widgets.IntRange/FloatRange/QualityRange body draws.
        private static int rangeDepth;
        private static int savedRangeDepth;

        // >0 while Widgets.FloatRangeWithTypeIn's body draws: the two plain TextFields it puts
        // either side of the slider are the range's min and max in draw order, and record with no
        // caption of their own.
        private static int rangeTypeInDepth;
        private static int savedRangeTypeInDepth;
        private static int rangeTypeInOrdinal;

        // Capture expansion pack, same Enter/Exit shape as the brackets above, with ONE exception:
        // the icon family's three cores really do call each other, so iconDepth is load-bearing.

        // >0 while Widgets.LabelEllipses' body draws. It funnels into the already-hooked
        // Widgets.Label, so the row is captured either way; without this stash what it captures is
        // the CLAMPED string, an artifact of the column's width rather than the caller's own text.
        private static int labelEllipsesDepth;
        private static int savedLabelEllipsesDepth;
        private static string labelEllipsesText;

        // >0 while a Widgets.DefIcon / ThingIcon core draws. The three cores delegate INTO each
        // other (DefIcon picks ThingIcon for a ThingDef; ThingIcon(Thing) picks DefIcon for a
        // blueprint), so only the OUTERMOST call records.
        private static int iconDepth;
        private static int savedIconDepth;

        // >0 while Verse.WidgetRow.DefIcon's body draws: WidgetRow registers the TipRegion itself,
        // so this exact pairing is carried onto the row the inner Widgets.DefIcon records.
        private static int widgetRowDefIconDepth;
        private static int savedWidgetRowDefIconDepth;
        private static string widgetRowDefIconTip;

        // >0 while Verse.WidgetRow.ToggleableIcon's body draws. Its tooltip is the only human name
        // the control has (its ButtonImage is passed none, so the row would be named after a
        // texture asset), and toggleableIconRow is the draw-order index of the Button ButtonImage
        // is about to record — pinned at Enter so the postfix can stamp the POST-click state on it.
        private static int toggleableIconDepth;
        private static int savedToggleableIconDepth;
        private static string toggleableIconTip;
        private static int toggleableIconRow = -1;

        // The icon texture's asset name — see CapturedWidget.IconTexName. Only read while
        // toggleableIconDepth > 0, so it needs no save/restore of its own.
        private static string toggleableIconTexName;

        // >0 while a Widgets.ColorBox body draws: the swatch's color and whether vanilla drew it
        // as the chosen one (its own Color.IndistinguishableFrom test, the same decision that
        // draws the selection outline).
        private static int colorBoxDepth;
        private static int savedColorBoxDepth;
        private static Color colorBoxColor;
        private static bool colorBoxSelected;

        // >0 while MedicalCareSetter draws; vanilla casts each hotspot's draw order to its category.
        private static int medicalCareDepth;
        private static int savedMedicalCareDepth;
        private static MedicalCareCategory medicalCareCurrent;
        private static int medicalCareOrdinal;

        // See RecordCheckTexMarker/TryRecordOrphanTipRow's remarks.
        private static bool savedLastBareTextureValid;
        private static bool savedLastBareTipValid;
        private static bool savedPendingTipTextureValid;

        // True while inside a CheckboxMulti body: the ButtonImageDraggable tap gates on this so a
        // tri-state checkbox's own draggable draw is not double-recorded. RecordButton itself does
        // not check the suppression counters (other callers depend on that), so the patch prefix
        // checks this instead.
        internal static bool InsideCheckboxMulti
        {
            get { return checkboxMultiDepth > 0; }
        }

        /// <summary>
        /// Whether a def/thing icon drawn right now would produce a row (<see cref="RecordIcon"/>
        /// repeats the same test). Read by the icon taps BEFORE they resolve a label:
        /// <c>Def.LabelCap</c>/<c>Thing.LabelCap</c> are not free and those taps run on every icon
        /// the game draws, so the argument must not be evaluated while this engine is dormant.
        /// </summary>
        internal static bool WantsIconRow
        {
            get
            {
                return passOpen && iconDepth == 0 && defLabelIconDepth == 0 && hyperlinkDepth == 0
                    && tabStripDepth == 0 && selfCaptionDepth == 0;
            }
        }

        // Set by the Listing.GapLine tap while a pass is open; consumed and cleared by the next
        // Record* call that adds a row (see RecordLabel's Heading computation). A GapLine followed
        // by no real row self-heals on the next EndPass/BeginPass.
        private static bool gapLinePending;

        // The row index the owning scope's cursor sits on (from the immediately preceding pass),
        // or -1 to draw no ring. Drawn INLINE, in the same nested call that has the real rect,
        // because a generic window may draw inside a scroll group that closes before any later
        // postfix could reach the same coordinate space.
        private static int focusedIndex = -1;

        /// <summary>Rows recorded by the current/most recent pass, in draw order.</summary>
        public static IReadOnlyList<CapturedWidget> Items
        {
            get { return items; }
        }
    }
}
