using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>Which Listing_Standard widget a captured row came from.</summary>
    internal enum ListingRowKind
    {
        /// <summary>Listing_Standard.Label / SubLabel — plain text, no action.</summary>
        Label,
        /// <summary>Listing_Standard.CheckboxLabeled (tooltip/height/labelPct overload).</summary>
        Checkbox,
        /// <summary>Listing_Standard.SliderLabeled.</summary>
        Slider,
        /// <summary>Listing_Standard.ButtonText — a plain action button, single label.</summary>
        Button,
        /// <summary>Listing_Standard.ButtonTextLabeledPct — label + current-value button (opens a FloatMenu, a confirmation, or another window).</summary>
        ComboBox,
        /// <summary>Listing_Standard.RadioButton.</summary>
        Radio,
    }

    /// <summary>
    /// Which row a listing-ring client wants ringed this pass; at most one of
    /// <see cref="RowObject"/> and <see cref="RowKey"/> is ever set.
    /// </summary>
    internal struct ListingRingFocus
    {
        /// <summary>Object form: the row's own object, ringed on ReferenceEquals.</summary>
        public object RowObject { get; set; }

        /// <summary>Key form: a non-display token vanilla itself built the row from.</summary>
        public string RowKey { get; set; }

        /// <summary>Veto for key form; for object form a DEBUG-only disagreement note, never a veto.</summary>
        public string LabelTripwire { get; set; }

        public static ListingRingFocus None
        {
            get { return default(ListingRingFocus); }
        }
    }

    /// <summary>
    /// A scope that wants the focus ring drawn on its window's own Listing_Standard rows.
    /// Registered with ScreenScopeDrawPatch's listing arm in OnPush, unregistered in OnPop.
    /// </summary>
    internal interface IListingRingClient
    {
        /// <summary>The window whose InnerWindowOnGUI pass this client rides. Never null for registered clients.</summary>
        Window ListingRingWindow { get; }

        /// <summary>Computed fresh each pass from live shared state. Return <see cref="ListingRingFocus.None"/> to draw nothing.</summary>
        ListingRingFocus CurrentListingFocus();
    }

    /// <summary>
    /// One row Dialog_Options actually drew, in draw order. Mutable: the inner Widgets-level taps
    /// fill in Rect (and the value fields) after the outer Listing_Standard-level tap that created
    /// the row has already run — see <see cref="ListingRowCapture"/>.
    /// </summary>
    internal sealed class CapturedListingRow
    {
        public ListingRowKind Kind;
        public string Label = "";
        public string Tooltip;
        public Rect Rect;

        /// <summary>Rect clipped to what is actually on screen, in absolute UI points — empty when scrolled out.</summary>
        public Rect VisibleScreenRect;

        /// <summary>The clip the row was drawn under; its depth breaks pointer-routing ties.</summary>
        public GuiSpace.ClipKey Clip;

        // Checkbox
        public bool Checked;
        public bool Disabled;

        // Slider
        public float SliderValue;
        public float SliderMin;
        public float SliderMax;
        public float SliderRoundTo;

        // ComboBox: the button's own rendered text (current value / verb).
        public string Value;

        // True when this row was recorded by the raw-widget taps rather than an outer
        // Listing_Standard tap. Presentation never branches on it; it exists only so the merge
        // logic can recognize a row it just added without a kind check that would also match an
        // ordinary bracketed Label row.
        public bool RawSource;
    }

    /// <summary>
    /// One marked row as drawn: the identity its marker carried, the caption vanilla put in it,
    /// and its geometry in absolute UI points. Recorded for every marked row, not just the ringed
    /// one, because pointer routing must match the row the mouse is over.
    /// </summary>
    internal struct MarkedRowGeometry
    {
        public object RowObject;
        public string RowKey;
        public string DrawnLabel;
        public Rect VisibleScreenRect;
        public GuiSpace.ClipKey Clip;

        /// <summary>Whether <paramref name="focus"/> names this row, by the rule the ring itself applies.</summary>
        internal bool Matches(ListingRingFocus focus)
        {
            if (RowObject != null)
            {
                return ReferenceEquals(RowObject, focus.RowObject);
            }
            if (RowKey == null || !string.Equals(RowKey, focus.RowKey, StringComparison.Ordinal))
            {
                return false;
            }
            if (focus.LabelTripwire == null)
            {
                // A synthetic site token is too weak to stand on its own.
                return RowKey.IndexOf('#') < 0;
            }
            return DrawnLabel == null || string.Equals(DrawnLabel, focus.LabelTripwire, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Pass-bracketed taps on the Listing_Standard widgets Dialog_Options' category methods (and
    /// OptionsRwaCategory's settings pane, drawn with the same widgets) call: each row's
    /// kind/label/tooltip/rect/value is recorded in draw order, and a posted keyboard
    /// activation/adjustment is replayed through the SAME call on the next draw pass so vanilla's
    /// inline change handling (Prefs writes, cache clears, restart confirms, FloatMenu opens) runs
    /// unmodified.
    ///
    /// Two-level tap per kind (except Label/SubLabel, which return their own Rect): the outer
    /// prefix on the Listing_Standard method sees the arguments but not the Rect, computed from
    /// private Listing cursor state, so it adds the row and marks it pending-rect; the inner
    /// postfix on the underlying Widgets call does receive the Rect (and the return value), and
    /// attaches it plus any requested step/click. BeginPass/EndPass is armed ONLY by OptionsScope's
    /// DoWindowContents draw patch.
    ///
    /// The focus ring must be drawn inline from the Attach*/Record* method that has the rect: pane
    /// rows draw inside DoOptions' scroll group, which closes before OptionsScope's postfix runs,
    /// putting a rect captured there in the wrong coordinate space. A pass opened with a
    /// <see cref="ListingRingFocus"/> instead of a row index rings by identity from an IL marker
    /// (<see cref="ListingRowMarkerInjector"/>) at the universal Listing.GetRect rect source; such
    /// passes leave focusedIndex at -1 and never carry an activation request.
    ///
    /// Keyboard activation never writes Prefs/game state directly: a scope posts
    /// RequestActivate/RequestAdjust for a draw-order index, and a post whose index the next pass
    /// no longer has is silently dropped.
    ///
    /// RAW-ROW CAPTURE: DoGameplayOptions draws its "NamesYouWantToSee" list with raw
    /// Widgets.Label/ButtonImage calls (1.7 adds CheckboxLabeled + TipRegion), so raw taps cover it, gated on InsideDoOptions (excluding the rail label and OK button drawn outside
    /// DoOptions) AND listingBodyDepth == 0 (outer-tapped methods draw their own caption via a
    /// nested raw Label the tap would otherwise record twice). The button tap merges into the
    /// immediately preceding raw Label row when their rects vertically overlap. The Mods category's
    /// captions are captured too, but OptionsScope reads that category from its own reader.
    /// </summary>
    internal static class ListingRowCapture
    {
        private const float FocusRingExpand = 2f;
        private static readonly Color FocusRingColor = new Color(0.45f, 0.78f, 1f);

        private static bool passOpen;
        private static readonly List<CapturedListingRow> items = new List<CapturedListingRow>();

        // Every marked row this pass drew, in draw order — the pointer-routing counterpart of the
        // single ring the pass paints.
        private static readonly List<MarkedRowGeometry> markedRows = new List<MarkedRowGeometry>();

#if DEBUG
        private static readonly HashSet<string> noTripwireWarned = new HashSet<string>();
#endif

        // The row most recently added by an outer tap, awaiting its Rect (and value/click) from
        // the paired inner tap.
        private static CapturedListingRow pendingRectRow;

        // The label of the row vanilla is drawing right now, published by the Label tap's PREFIX:
        // that tap can only record its row once Label has returned a rect, which is after the ring
        // point inside Label's own GetRect call has had to judge the tripwire.
        private static string pendingDrawnLabel;

        // The caption of the SliderLabeled call currently on the stack, with the frame it was
        // published in — see EnterSliderLabel.
        private static string pendingSliderLabel;
        private static int pendingSliderLabelFrame = -1;

        private static string pendingRawTip;
        private static Rect pendingRawTipRect;
        private static bool rawCheckboxOpen;

        private static int? pendingActivateIndex;
        private static int? pendingAdjustIndex;
        private static int pendingAdjustDirection;

        // >0 while Dialog_Options.DoOptions is on the call stack — see the class remarks'
        // RAW-ROW CAPTURE section.
        private static int insideDoOptionsDepth;

        // >0 while one of the outer-tapped Listing_Standard methods is on the call stack; stops
        // the raw Widgets.Label/ButtonImage taps double-recording a caption those methods draw
        // internally.
        private static int listingBodyDepth;

        // The identity a marker made pending. The first two are one-shot, taken by the very next
        // Listing.GetRect; the section key goes to EnterKeyedRowFromPending instead, and
        // currentKeyedRow then rings every row its helper draws until ExitKeyedRow.
        private static object pendingRowObject;
        private static string pendingRowKey;
        private static string pendingSectionKey;
        private static string currentKeyedRow;

        private static ListingRingFocus ringFocus;

        internal static bool IsPassOpen
        {
            get { return passOpen; }
        }

        internal static bool InsideDoOptions
        {
            get { return insideDoOptionsDepth > 0; }
        }

        internal static void EnterDoOptions()
        {
            insideDoOptionsDepth++;
        }

        internal static void ExitDoOptions()
        {
            if (insideDoOptionsDepth > 0)
            {
                insideDoOptionsDepth--;
            }
        }

        internal static void EnterListingBody()
        {
            listingBodyDepth++;
        }

        internal static void ExitListingBody()
        {
            if (listingBodyDepth > 0)
            {
                listingBodyDepth--;
            }
        }

        // The pane row index the owning scope's cursor sits on, or -1 when the pane has no focus
        // this pass. Set fresh by BeginPass; consulted by every Attach*/Record* method to draw the
        // ring inline, in the same nested call that has the real rect.
        private static int focusedIndex = -1;

        /// <summary>Rows recorded by the current/most recent pass, in draw order.</summary>
        public static IReadOnlyList<CapturedListingRow> Items
        {
            get { return items; }
        }

        /// <summary>
        /// Start recording; clears the previous pass. Call from the armed surface's draw prefix,
        /// passing the pane row index (from the preceding pass) to ring, or -1 for no ring.
        /// </summary>
        public static void BeginPass(int focusedIndex)
        {
            BeginPass(ListingRingFocus.None);
            ListingRowCapture.focusedIndex = focusedIndex;
        }

        /// <summary>
        /// Start recording for a listing-ring client. Rows are identified by the marker their draw
        /// site carries, so focusedIndex stays -1 and no ordinal is consulted.
        /// </summary>
        public static void BeginPass(ListingRingFocus focus)
        {
            items.Clear();
            markedRows.Clear();
            pendingRectRow = null;
            pendingDrawnLabel = null;
            pendingRawTip = null;
            rawCheckboxOpen = false;
            focusedIndex = -1;
            ringFocus = focus;
            ClearPendingIdentity();
            passOpen = true;
            // Self-heal: a body that threw last pass never ran its postfix to unwind these.
            insideDoOptionsDepth = 0;
            listingBodyDepth = 0;
#if DEBUG
            if (focus.RowObject != null && focus.RowKey != null)
            {
                ShellDev.QARecord("capture", "listing ring focus set both an object and a key");
            }
#endif
        }

        /// <summary>Stop recording. Call from the armed surface's draw postfix.</summary>
        public static void EndPass()
        {
            passOpen = false;
            pendingRectRow = null;
            pendingDrawnLabel = null;
            focusedIndex = -1;
            ringFocus = ListingRingFocus.None;
            ClearPendingIdentity();
            InjectedClickGuard.InFlight = false;
        }

        private static void ClearPendingIdentity()
        {
            pendingRowObject = null;
            pendingRowKey = null;
            pendingSectionKey = null;
            currentKeyedRow = null;
        }

        // The label vanilla has drawn for the row the ring point is judging.
        private static string DrawnRowLabel
        {
            get { return pendingDrawnLabel ?? (pendingRectRow != null ? pendingRectRow.Label : null); }
        }

        private static void DrawFocusRingIfFocused(int index, Rect rect)
        {
            if (index != focusedIndex)
            {
                return;
            }
            DrawRing(rect);
        }

        private static void DrawRing(Rect rect)
        {
            if (rect.width <= 0f)
            {
                return;
            }
            Rect expanded = rect.ExpandedBy(FocusRingExpand);
            Color previous = GUI.color;
            GUI.color = FocusRingColor;
            Widgets.DrawBox(expanded, 2);
            GUI.color = previous;
        }

        // Marked rows: pending identity in, one ring out at Listing.GetRect.

        // Public because injected IL calls these directly.
        public static void SetPendingRowObject(object rowObject)
        {
            if (!passOpen)
            {
                return;
            }
            pendingRowObject = rowObject;
        }

        public static void SetPendingRowKey(string rowKey)
        {
            if (!passOpen)
            {
                return;
            }
            pendingRowKey = rowKey;
        }

        public static void SetPendingSectionKey(string sectionKey)
        {
            if (!passOpen)
            {
                return;
            }
            pendingSectionKey = sectionKey;
        }

        /// <summary>Opens a sticky keyed row: every row drawn until <see cref="ExitKeyedRow"/> carries this key.</summary>
        internal static void EnterKeyedRow(string rowKey)
        {
            if (!passOpen)
            {
                return;
            }
            currentKeyedRow = rowKey;
        }

        /// <summary>As <see cref="EnterKeyedRow"/>, taking the key a marker left in the section slot.</summary>
        internal static void EnterKeyedRowFromPending()
        {
            if (!passOpen)
            {
                return;
            }
            currentKeyedRow = pendingSectionKey;
            pendingSectionKey = null;
        }

        internal static void ExitKeyedRow()
        {
            currentKeyedRow = null;
        }

        /// <summary>
        /// The single ring point. Runs on every Listing.GetRect in the game, so the pass test comes
        /// first and the pending test second, with nothing before them.
        /// </summary>
        internal static void RingMarkedRow(Rect rect)
        {
            if (!passOpen)
            {
                return;
            }
            object rowObject = pendingRowObject;
            // An object marker outranks any key, and both one-shots are spent by this row whether
            // or not it turns out to be the ringed one.
            string rowKey = rowObject != null ? null : (pendingRowKey ?? currentKeyedRow);
            pendingRowObject = null;
            pendingRowKey = null;
            if (rowObject == null && rowKey == null)
            {
                return;
            }
            MarkedRowGeometry row = new MarkedRowGeometry
            {
                RowObject = rowObject,
                RowKey = rowKey,
                DrawnLabel = DrawnRowLabel,
                VisibleScreenRect = GuiSpace.VisibleScreenRect(rect),
                Clip = GuiSpace.CurrentClip(),
            };
            markedRows.Add(row);
            if (!row.Matches(ringFocus))
            {
                return;
            }
#if DEBUG
            if (rowObject != null && row.DrawnLabel != null && ringFocus.LabelTripwire != null
                && !string.Equals(row.DrawnLabel, ringFocus.LabelTripwire, StringComparison.Ordinal))
            {
                // Never a veto: a reference match is complete, so a label disagreement means the
                // model is wrong about the row.
                ShellDev.QARecord("capture", "listing ring label disagrees: model \"" + ringFocus.LabelTripwire
                    + "\", vanilla \"" + row.DrawnLabel + "\"");
            }
            if (rowKey != null && ringFocus.LabelTripwire == null && noTripwireWarned.Add(rowKey))
            {
                ShellDev.QARecord("capture", "listing ring key \"" + rowKey + "\" has no tripwire");
            }
#endif
            DrawRing(rect);
        }

        /// <summary>
        /// Contributes every marked row <paramref name="focus"/> names as a pointer-routing
        /// candidate for (<paramref name="region"/>, <paramref name="index"/>). A many-to-one focus
        /// legitimately yields several candidates aimed at the same target.
        /// </summary>
        internal static void AddRouteCandidates(ListingRingFocus focus, int region, int index,
            List<PointerHitCandidate> candidates, List<ScreenScope.RouteTarget> targets)
        {
            if (focus.RowObject == null && focus.RowKey == null)
            {
                return;
            }
            for (int i = 0; i < markedRows.Count; i++)
            {
                if (!markedRows[i].Matches(focus))
                {
                    continue;
                }
                candidates.Add(new PointerHitCandidate
                {
                    Primary = markedRows[i].VisibleScreenRect,
                    ClipDepth = markedRows[i].Clip.Depth,
                });
                targets.Add(new ScreenScope.RouteTarget { Region = region, Index = index });
            }
        }

        /// <summary>
        /// Ask the tap reaching draw-order index <paramref name="index"/> on the NEXT pass to flip
        /// a checkbox or force a button-click true. Index is the row's position in the immediately
        /// preceding pass's <see cref="Items"/>.
        /// </summary>
        public static void RequestActivate(int index)
        {
            pendingActivateIndex = index;
        }

        /// <summary>Ask the slider tap reaching index <paramref name="index"/> on the NEXT pass to step by one unit in <paramref name="direction"/> (+1/-1).</summary>
        public static void RequestAdjust(int index, int direction)
        {
            pendingAdjustIndex = index;
            pendingAdjustDirection = direction;
        }

        // Outer taps (Listing_Standard level): identity + request consumption.

        internal static void RecordCheckbox(string label, string tooltip, ref bool checkOn)
        {
            if (!passOpen)
            {
                return;
            }
            if (pendingActivateIndex.HasValue && pendingActivateIndex.Value == items.Count)
            {
                checkOn = !checkOn;
                pendingActivateIndex = null;
            }
            RecordCheckboxRow(label, tooltip, checkOn);
        }

        /// <summary>The ring-only checkbox row (the tabIn overload's tap): never injectable, so no activation request is consulted.</summary>
        internal static void RecordCheckboxRow(string label, string tooltip, bool checkOn)
        {
            if (!passOpen)
            {
                return;
            }
            CapturedListingRow row = new CapturedListingRow
            {
                Kind = ListingRowKind.Checkbox,
                Label = label ?? "",
                Tooltip = tooltip,
                Checked = checkOn,
            };
            items.Add(row);
            pendingRectRow = row;
        }

        /// <summary>Ring-only, like <see cref="RecordCheckboxRow"/>: the label source for a marked radio row's tripwire.</summary>
        internal static void RecordRadio(string label, string tooltip)
        {
            if (!passOpen)
            {
                return;
            }
            CapturedListingRow row = new CapturedListingRow
            {
                Kind = ListingRowKind.Radio,
                Label = label ?? "",
                Tooltip = tooltip,
            };
            items.Add(row);
            pendingRectRow = row;
        }

        /// <summary>
        /// No inner tap pairs with RadioButton, so its outer tap drops the pending row itself
        /// rather than leave a stale label behind for the next row's tripwire.
        /// </summary>
        internal static void ClearPendingRectRow()
        {
            pendingRectRow = null;
        }

        /// <summary>
        /// Publishes the label of a Label row for the duration of its draw so the ring point inside
        /// its GetRect has a tripwire source. Touches no row state, so capture order is unaffected.
        /// </summary>
        internal static void EnterDrawnLabel(string label)
        {
            if (!passOpen)
            {
                return;
            }
            pendingDrawnLabel = label;
        }

        internal static void ExitDrawnLabel()
        {
            pendingDrawnLabel = null;
        }

        /// <summary>
        /// Publishes the caption <c>Listing_Standard.SliderLabeled</c> drew for the duration of its
        /// body so the mouse-drag reader on the nested <c>Widgets.HorizontalSlider</c> can name the
        /// slider; vanilla discards the label before delegating, leaving the drag reader with a bare
        /// number otherwise. Deliberately NOT gated on <see cref="passOpen"/>, unlike
        /// <see cref="EnterDrawnLabel"/>: most SliderLabeled calls are on surfaces no capture pass
        /// is armed for, and those are the point.
        /// </summary>
        internal static void EnterSliderLabel(string label)
        {
            pendingSliderLabel = label;
            pendingSliderLabelFrame = Time.frameCount;
        }

        internal static void ExitSliderLabel()
        {
            pendingSliderLabel = null;
        }

        /// <summary>
        /// The SliderLabeled caption live right now, or null. Frame-stamped because a throwing body
        /// skips the postfix that would clear it, and nothing else would notice the leak.
        /// </summary>
        internal static string CurrentSliderLabel
        {
            get { return pendingSliderLabelFrame == Time.frameCount ? pendingSliderLabel : null; }
        }

        internal static void RecordSlider(string label, string tooltip)
        {
            if (!passOpen)
            {
                return;
            }
            CapturedListingRow row = new CapturedListingRow
            {
                Kind = ListingRowKind.Slider,
                Label = label ?? "",
                Tooltip = tooltip,
            };
            items.Add(row);
            pendingRectRow = row;
        }

        internal static void RecordComboBox(string label, string tooltip)
        {
            if (!passOpen)
            {
                return;
            }
            CapturedListingRow row = new CapturedListingRow
            {
                Kind = ListingRowKind.ComboBox,
                Label = label ?? "",
                Tooltip = tooltip,
            };
            items.Add(row);
            pendingRectRow = row;
        }

        internal static void RecordButton(string label)
        {
            if (!passOpen)
            {
                return;
            }
            CapturedListingRow row = new CapturedListingRow
            {
                Kind = ListingRowKind.Button,
                Label = label ?? "",
            };
            items.Add(row);
            pendingRectRow = row;
        }

        internal static void RecordLabel(string label, Rect rect, string tooltip = null)
        {
            if (!passOpen)
            {
                return;
            }
            CapturedListingRow row = new CapturedListingRow
            {
                Kind = ListingRowKind.Label,
                Label = label ?? "",
                Tooltip = tooltip,
            };
            StampRect(row, rect);
            items.Add(row);
            DrawFocusRingIfFocused(items.Count - 1, rect);
        }

        /// <summary>
        /// Records a row's geometry both ways: the Listing-local rect the focus ring draws against,
        /// and the absolute-UI-point pair pointer routing matches on. The screen pair can only be
        /// taken here, while the clip stack is still live.
        /// </summary>
        private static void StampRect(CapturedListingRow row, Rect rect)
        {
            row.Rect = rect;
            row.VisibleScreenRect = GuiSpace.VisibleScreenRect(rect);
            row.Clip = GuiSpace.CurrentClip();
        }

        // Inner taps (Widgets level): rect + value params + injection.

        internal static void AttachCheckboxRect(Rect rect)
        {
            if (!passOpen || pendingRectRow == null || pendingRectRow.Kind != ListingRowKind.Checkbox)
            {
                return;
            }
            CapturedListingRow row = pendingRectRow;
            StampRect(row, rect);
            pendingRectRow = null;
            DrawFocusRingIfFocused(items.IndexOf(row), rect);
        }

        internal static void AttachSliderRectAndMaybeStep(Rect rect, float min, float max, float roundTo, ref float result)
        {
            if (!passOpen || pendingRectRow == null || pendingRectRow.Kind != ListingRowKind.Slider)
            {
                return;
            }
            CapturedListingRow row = pendingRectRow;
            int index = items.IndexOf(row);
            if (pendingAdjustIndex.HasValue && pendingAdjustIndex.Value == index)
            {
                // Grid-quantized, never value+step: accumulation compounds float dust.
                result = SliderStep.Stepped(result, pendingAdjustDirection, min, max, roundTo);
                pendingAdjustIndex = null;
            }
            StampRect(row, rect);
            row.SliderMin = min;
            row.SliderMax = max;
            row.SliderRoundTo = roundTo;
            row.SliderValue = result;
            pendingRectRow = null;
            DrawFocusRingIfFocused(index, rect);
        }

        /// <summary>
        /// Shared by ButtonTextLabeledPct (ComboBox rows, which also record the rendered button
        /// text as the row's Value) and ButtonText (Button rows, no separate value). Forces a true
        /// click-result when this row's index has a pending activation.
        /// </summary>
        internal static void AttachButtonRectAndMaybeActivate(Rect rect, string renderedLabel, ref bool result)
        {
            if (!passOpen || pendingRectRow == null
                || (pendingRectRow.Kind != ListingRowKind.ComboBox && pendingRectRow.Kind != ListingRowKind.Button))
            {
                return;
            }
            CapturedListingRow row = pendingRectRow;
            int index = items.IndexOf(row);
            StampRect(row, rect);
            if (row.Kind == ListingRowKind.ComboBox)
            {
                row.Value = renderedLabel ?? "";
            }
            if (pendingActivateIndex.HasValue && pendingActivateIndex.Value == index)
            {
                result = true;
                pendingActivateIndex = null;
                InjectedClickGuard.InFlight = true;
            }
            pendingRectRow = null;
            DrawFocusRingIfFocused(index, rect);
        }

        // Raw-widget taps: see the class remarks' RAW-ROW CAPTURE section.

        private static bool RawTapsActive
        {
            get { return passOpen && InsideDoOptions && listingBodyDepth == 0 && pendingRectRow == null; }
        }

        private static bool VerticallyOverlaps(Rect a, Rect b)
        {
            return a.yMin < b.yMax && b.yMin < a.yMax;
        }

        private static Rect UnionRect(Rect a, Rect b)
        {
            float xMin = Mathf.Min(a.xMin, b.xMin);
            float yMin = Mathf.Min(a.yMin, b.yMin);
            float xMax = Mathf.Max(a.xMax, b.xMax);
            float yMax = Mathf.Max(a.yMax, b.yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        internal static void RecordRawLabel(Rect rect, string label)
        {
            if (!RawTapsActive || string.IsNullOrWhiteSpace(label))
            {
                return;
            }
            CapturedListingRow row = new CapturedListingRow
            {
                Kind = ListingRowKind.Label,
                Label = label,
                RawSource = true,
                Tooltip = TakePendingRawTip(rect),
            };
            StampRect(row, rect);
            items.Add(row);
            DrawFocusRingIfFocused(items.Count - 1, rect);
        }

        /// <summary>A raw Widgets.CheckboxLabeled row (1.7's texture compression), bracketed as a listing body so its caption Label is not recorded twice.</summary>
        internal static void EnterRawCheckbox(Rect rect, string label, ref bool checkOn, bool disabled)
        {
            if (!RawTapsActive || string.IsNullOrWhiteSpace(label))
            {
                return;
            }
            if (pendingActivateIndex.HasValue && pendingActivateIndex.Value == items.Count)
            {
                pendingActivateIndex = null;
                if (!disabled)
                {
                    checkOn = !checkOn;
                }
            }
            CapturedListingRow row = new CapturedListingRow
            {
                Kind = ListingRowKind.Checkbox,
                Label = label,
                Checked = checkOn,
                Disabled = disabled,
                RawSource = true,
                Tooltip = TakePendingRawTip(rect),
            };
            StampRect(row, rect);
            items.Add(row);
            DrawFocusRingIfFocused(items.Count - 1, rect);
            EnterListingBody();
            rawCheckboxOpen = true;
        }

        internal static void ExitRawCheckbox()
        {
            if (rawCheckboxOpen)
            {
                rawCheckboxOpen = false;
                ExitListingBody();
            }
        }

        /// <summary>A raw TipRegion inside DoOptions; it joins the raw row its rect overlaps, whichever was drawn first.</summary>
        internal static void RecordRawTip(Rect rect, TipSignal tip)
        {
            if (!RawTapsActive)
            {
                return;
            }
            string text = tip.textGetter != null ? tip.textGetter() : tip.text;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            CapturedListingRow last = items.Count > 0 ? items[items.Count - 1] : null;
            if (last != null && last.RawSource && last.Tooltip == null && last.Rect.Overlaps(rect))
            {
                last.Tooltip = text;
                return;
            }
            pendingRawTip = text;
            pendingRawTipRect = rect;
        }

        private static string TakePendingRawTip(Rect rowRect)
        {
            if (pendingRawTip == null || !pendingRawTipRect.Overlaps(rowRect))
            {
                return null;
            }
            string tip = pendingRawTip;
            pendingRawTip = null;
            return tip;
        }

        /// <summary>
        /// Records an icon-only button drawn raw inside DoOptions. If the row most recently added
        /// by <see cref="RecordRawLabel"/> is still the last item and its rect vertically overlaps
        /// this button's, the two are the SAME visual row and are merged in place into one Button
        /// row; otherwise the row is recorded standalone. Either way honors a pending keyboard
        /// activation like <see cref="AttachButtonRectAndMaybeActivate"/>.
        /// </summary>
        internal static void RecordRawButtonImage(Rect rect, Texture2D tex, ref bool result)
        {
            if (!RawTapsActive)
            {
                return;
            }
            string buttonLabel = WidgetCapture.ImageButtonLabel(tex, null);
            CapturedListingRow row;
            int index;
            if (items.Count > 0)
            {
                CapturedListingRow last = items[items.Count - 1];
                if (last.RawSource && last.Kind == ListingRowKind.Label && VerticallyOverlaps(last.Rect, rect))
                {
                    row = last;
                    index = items.Count - 1;
                    row.Kind = ListingRowKind.Button;
                    row.Label = "RimWorldAccess.Options.RawRowAction".Translate(buttonLabel, last.Label);
                    StampRect(row, UnionRect(last.Rect, rect));
                    row.RawSource = true;
                    RecordRawButtonImageActivateAndRing(row, index, ref result);
                    return;
                }
            }
            row = new CapturedListingRow
            {
                Kind = ListingRowKind.Button,
                Label = buttonLabel,
                RawSource = true,
            };
            StampRect(row, rect);
            items.Add(row);
            index = items.Count - 1;
            RecordRawButtonImageActivateAndRing(row, index, ref result);
        }

        private static void RecordRawButtonImageActivateAndRing(CapturedListingRow row, int index, ref bool result)
        {
            if (pendingActivateIndex.HasValue && pendingActivateIndex.Value == index)
            {
                result = true;
                pendingActivateIndex = null;
                InjectedClickGuard.InFlight = true;
            }
            DrawFocusRingIfFocused(index, row.Rect);
        }
    }

    // Outer taps: one Harmony patch class per Listing_Standard overload.

    /// <summary>
    /// The tooltip/height/labelPct overload — the only CheckboxLabeled shape Dialog_Options calls.
    /// Targeted via TargetMethod because a by-ref parameter type cannot appear in an attribute
    /// argument array (CS0182).
    /// </summary>
    [HarmonyPatch]
    internal static class ListingRowCheckboxTapPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Listing_Standard), "CheckboxLabeled",
                new Type[] { typeof(string), typeof(bool).MakeByRefType(), typeof(string), typeof(float), typeof(float) });
        }

        [HarmonyPrefix]
        public static void Prefix(string label, ref bool checkOn, string tooltip)
        {
            ListingRowCapture.EnterListingBody();
            ListingRowCapture.RecordCheckbox(label, tooltip, ref checkOn);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            ListingRowCapture.ExitListingBody();
        }
    }

    [HarmonyPatch(typeof(Listing_Standard), "SliderLabeled",
        new Type[] { typeof(string), typeof(float), typeof(float), typeof(float), typeof(float), typeof(string) })]
    internal static class ListingRowSliderTapPatch
    {
        [HarmonyPrefix]
        public static void Prefix(string label, string tooltip)
        {
            ListingRowCapture.EnterListingBody();
            ListingRowCapture.EnterSliderLabel(label);
            ListingRowCapture.RecordSlider(label, tooltip);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            ListingRowCapture.ExitSliderLabel();
            ListingRowCapture.ExitListingBody();
        }
    }

    [HarmonyPatch(typeof(Listing_Standard), "ButtonTextLabeledPct",
        new Type[] { typeof(string), typeof(string), typeof(float), typeof(TextAnchor), typeof(string), typeof(string), typeof(Texture2D) })]
    internal static class ListingRowComboBoxTapPatch
    {
        [HarmonyPrefix]
        public static void Prefix(string label, string tooltip)
        {
            ListingRowCapture.EnterListingBody();
            ListingRowCapture.RecordComboBox(label, tooltip);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            ListingRowCapture.ExitListingBody();
        }
    }

    /// <summary>The plain action-button overload; otherwise-uncapturable rows such as DoVideoOptions' Borderless Fullscreen instructions button.</summary>
    [HarmonyPatch(typeof(Listing_Standard), "ButtonText",
        new Type[] { typeof(string), typeof(string), typeof(float) })]
    internal static class ListingRowButtonTapPatch
    {
        [HarmonyPrefix]
        public static void Prefix(string label)
        {
            ListingRowCapture.EnterListingBody();
            ListingRowCapture.RecordButton(label);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            ListingRowCapture.ExitListingBody();
        }
    }

    /// <summary>Dialog_Options only calls the TaggedString overload, which forwards to this string overload — the one that computes and returns the Rect.</summary>
    [HarmonyPatch(typeof(Listing_Standard), "Label",
        new Type[] { typeof(string), typeof(float), typeof(TipSignal?) })]
    internal static class ListingRowLabelTapPatch
    {
        [HarmonyPrefix]
        public static void Prefix(string label)
        {
            ListingRowCapture.EnterListingBody();
            ListingRowCapture.EnterDrawnLabel(label);
        }

        [HarmonyPostfix]
        public static void Postfix(string label, TipSignal? tipSignal, Rect __result)
        {
            ListingRowCapture.ExitDrawnLabel();
            ListingRowCapture.ExitListingBody();
            string tooltip = tipSignal.HasValue ? tipSignal.Value.text : null;
            ListingRowCapture.RecordLabel(label, __result, tooltip);
        }
    }

    /// <summary>
    /// The RadioButton core overload the two convenience overloads funnel into, so one tap sees
    /// every caller. Label source only: a radio row's identity comes from its marker.
    /// </summary>
    [HarmonyPatch(typeof(Listing_Standard), "RadioButton",
        new Type[] { typeof(string), typeof(bool), typeof(float), typeof(float), typeof(string), typeof(float?), typeof(bool) })]
    internal static class ListingRowRadioTapPatch
    {
        [HarmonyPrefix]
        public static void Prefix(string label, string tooltip)
        {
            ListingRowCapture.EnterListingBody();
            ListingRowCapture.RecordRadio(label, tooltip);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            ListingRowCapture.ExitListingBody();
            ListingRowCapture.ClearPendingRectRow();
        }
    }

    /// <summary>
    /// The tabIn CheckboxLabeled overload. Label source only, like the radio tap; Dialog_Options
    /// calls only the 5-arg tooltip shape, so the Options pane never sees this one.
    /// </summary>
    [HarmonyPatch]
    internal static class ListingRowTabInCheckboxTapPatch
    {
        // TargetMethod for the same CS0182 reason as ListingRowCheckboxTapPatch.
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Listing_Standard), "CheckboxLabeled",
                new Type[] { typeof(string), typeof(bool).MakeByRefType(), typeof(float) });
        }

        [HarmonyPrefix]
        public static void Prefix(string label, ref bool checkOn)
        {
            ListingRowCapture.EnterListingBody();
            ListingRowCapture.RecordCheckboxRow(label, null, checkOn);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            ListingRowCapture.ExitListingBody();
        }
    }

    [HarmonyPatch(typeof(Listing_Standard), "SubLabel", new Type[] { typeof(string), typeof(float) })]
    internal static class ListingRowSubLabelTapPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            ListingRowCapture.EnterListingBody();
        }

        [HarmonyPostfix]
        public static void Postfix(string label, Rect __result)
        {
            ListingRowCapture.ExitListingBody();
            ListingRowCapture.RecordLabel(label, __result);
        }
    }

    // Inner taps: the underlying Widgets calls that actually carry the Rect.

    /// <summary>
    /// The ring point for marked rows. Listing.GetRect is non-virtual and no subclass redeclares
    /// it, so this single declaring type covers Listing_Standard and Listing_ScenEdit alike.
    /// </summary>
    [HarmonyPatch(typeof(Listing), "GetRect", new Type[] { typeof(float), typeof(float) })]
    internal static class ListingRowRectRingPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect __result)
        {
            ListingRowCapture.RingMarkedRow(__result);
        }
    }

    [HarmonyPatch]
    internal static class ListingRowCheckboxRectTapPatch
    {
        // TargetMethod for the same CS0182 reason as ListingRowCheckboxTapPatch.
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "CheckboxLabeled",
                new Type[] { typeof(Rect), typeof(string), typeof(bool).MakeByRefType(), typeof(bool), typeof(Texture2D), typeof(Texture2D), typeof(bool), typeof(bool) });
        }

        [HarmonyPrefix]
        public static void Prefix(Rect rect, string label, ref bool checkOn, bool disabled)
        {
            ListingRowCapture.EnterRawCheckbox(rect, label, ref checkOn, disabled);
        }

        [HarmonyPostfix]
        public static void Postfix(Rect rect)
        {
            ListingRowCapture.AttachCheckboxRect(rect);
            ListingRowCapture.ExitRawCheckbox();
        }
    }

    [HarmonyPatch(typeof(Widgets), "HorizontalSlider",
        new Type[] { typeof(Rect), typeof(float), typeof(float), typeof(float), typeof(bool), typeof(string), typeof(string), typeof(string), typeof(float) })]
    internal static class ListingRowSliderRectTapPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect rect, float min, float max, float roundTo, ref float __result)
        {
            ListingRowCapture.AttachSliderRectAndMaybeStep(rect, min, max, roundTo, ref __result);
        }
    }

    /// <summary>
    /// The Color-taking ButtonText overload both public overloads funnel through, so this single
    /// tap sees clicks from ButtonTextLabeledPct and the plain ButtonText wrapper alike.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "ButtonText",
        new Type[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(Color), typeof(bool), typeof(TextAnchor?) })]
    internal static class ListingRowButtonRectTapPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect rect, string label, ref bool __result)
        {
            ListingRowCapture.AttachButtonRectAndMaybeActivate(rect, label, ref __result);
        }
    }

    // Raw-widget taps: see the class remarks' RAW-ROW CAPTURE section. DoOptions is private with a
    // single overload, so a type-less HarmonyPatch attribute resolves it.

    [HarmonyPatch(typeof(Dialog_Options), "DoOptions")]
    internal static class ListingRowDoOptionsGatePatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            ListingRowCapture.EnterDoOptions();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            ListingRowCapture.ExitDoOptions();
        }
    }

    /// <summary>Dialog_Options' player-facing text goes through the TaggedString overload, which delegates to this string core.</summary>
    [HarmonyPatch(typeof(Widgets), "Label", new Type[] { typeof(Rect), typeof(string) })]
    internal static class ListingRowRawLabelTapPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect rect, string label)
        {
            ListingRowCapture.RecordRawLabel(rect, label);
        }
    }

    /// <summary>The 6-arg core every ButtonImage overload funnels through, including the 4-arg shape Dialog_Options' preferred-names delete icon calls.</summary>
    [HarmonyPatch(typeof(TooltipHandler), "TipRegion", new Type[] { typeof(Rect), typeof(TipSignal) })]
    internal static class ListingRowRawTipTapPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, TipSignal tip)
        {
            ListingRowCapture.RecordRawTip(rect, tip);
        }
    }

    [HarmonyPatch(typeof(Widgets), "ButtonImage",
        new Type[] { typeof(Rect), typeof(Texture2D), typeof(Color), typeof(Color), typeof(bool), typeof(string) })]
    internal static class ListingRowRawButtonImageTapPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect butRect, Texture2D tex, ref bool __result)
        {
            ListingRowCapture.RecordRawButtonImage(butRect, tex, ref __result);
        }
    }
}
