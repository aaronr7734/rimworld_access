using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Capture-on-demand for inspect tabs on any Thing or Zone the selector can select.
    /// Two-step by necessity: tree expansion runs at key-dispatch time, while the capture
    /// harness's mutation-inertness contract requires a Repaint event. The tree requests
    /// captures on node expansion, the pump runs them next Repaint, and the category node
    /// reads the cached rows at least a frame later. A cold cache falls back to the
    /// inspect-string presentation — capture failure must never cost users that floor.
    /// Entries are keyed by (target, tab) and re-captured on every request.
    /// A single-slot armed-activation channel rides alongside: the pump drains it ahead of
    /// the captures and re-runs the tab's draw with that one widget armed, so its own
    /// vanilla handler fires and the outcome is spoken.
    /// </summary>
    public static class InspectTabCaptureService
    {
        private sealed class Entry
        {
            public List<FoldedRow> Rows;
            public List<CapturedWidget> Widgets;
            // Aligned to Widgets, resolved at capture time because the detached tip index
            // only lives until the next detached pass, while the parity diff reads these
            // at key-dispatch time.
            public List<string> WidgetTips;
            public string Error;

            // How many bands Widgets folded into, empty ones included: the count a live
            // geometry pass must reproduce before the focus ring trusts its bands.
            public int BandCount;
        }

        private struct Request
        {
            public object Target;
            public InspectTabBase Tab;
        }

        /// <summary>What a pending request does to its matched widget.</summary>
        private enum ActivationKind { Activate, Adjust, SetSlider, SetText }

        /// <summary>
        /// A pending armed action: the kind/label/ordinal descriptor that re-finds the
        /// widget in a fresh pass, the action and its payload, and the spoken label.
        /// </summary>
        private sealed class ActivationRequest
        {
            public ActivationKind Action;
            public object Target;
            public InspectTabBase Tab;
            public WidgetKind Kind;
            public string Label;
            public int Ordinal;
            public string SpokenLabel;
            public int Direction;
            public float Value;
            public string Text;

            // Slider/text writes only: the tree's shared member and its presented node, so
            // a completed follow-up capture can refresh both in place.
            public InteractiveMember Member;
            public InspectionTreeItem Node;
        }

        // Single slot: a second press before the frame boundary supersedes the first.
        // Drained ahead of the capture pump.
        private static ActivationRequest pendingActivation;

        // The cache is reference-keyed on the LIVE tab instance, not its type: thing tabs
        // are InspectTabManager singletons, but zone tabs are per-zone-class static
        // instances the manager knows nothing about, and the instance the tree discovered
        // is the one that must draw. Cleared wholesale past this bound.
        private const int MaxCacheEntries = 128;

        private static readonly Dictionary<ValueTuple<object, InspectTabBase>, Entry> cache =
            new Dictionary<ValueTuple<object, InspectTabBase>, Entry>();

        private static readonly List<Request> pending = new List<Request>();

        /// <summary>
        /// Queues a capture of <paramref name="tab"/> for <paramref name="target"/> on the
        /// next Repaint frame. Always re-captures; duplicates in a batch collapse.
        /// </summary>
        public static void RequestCapture(object target, InspectTabBase tab)
        {
            if (target == null || tab == null)
            {
                return;
            }
            foreach (Request request in pending)
            {
                if (request.Target == target && request.Tab == tab)
                {
                    return;
                }
            }
            pending.Add(new Request { Target = target, Tab = tab });
        }

        /// <summary>
        /// Arms the identified control to fire its own vanilla handler on the next Repaint
        /// frame, announcing against <paramref name="spokenLabel"/>. Single slot.
        /// </summary>
        public static void RequestActivation(object target, InspectTabBase tab, WidgetKind kind, string label, int ordinal, string spokenLabel)
        {
            if (target == null || tab == null)
            {
                return;
            }
            pendingActivation = new ActivationRequest
            {
                Action = ActivationKind.Activate,
                Target = target,
                Tab = tab,
                Kind = kind,
                Label = label,
                Ordinal = ordinal,
                SpokenLabel = spokenLabel,
            };
        }

        /// <summary>Arms the identified slider to step one unit (+1/-1) next Repaint. Single slot.</summary>
        public static void RequestAdjust(object target, InspectTabBase tab, string label, int ordinal, int direction, string spokenLabel,
            InteractiveMember member = null, InspectionTreeItem node = null)
        {
            if (target == null || tab == null)
            {
                return;
            }
            pendingActivation = new ActivationRequest
            {
                Action = ActivationKind.Adjust,
                Target = target,
                Tab = tab,
                Kind = WidgetKind.Slider,
                Label = label,
                Ordinal = ordinal,
                Direction = direction,
                SpokenLabel = spokenLabel,
                Member = member,
                Node = node,
            };
        }

        /// <summary>Arms the identified slider to take a grid-snapped, clamped value. Single slot.</summary>
        public static void RequestSliderSet(object target, InspectTabBase tab, string label, int ordinal, float value, string spokenLabel,
            InteractiveMember member = null, InspectionTreeItem node = null)
        {
            if (target == null || tab == null)
            {
                return;
            }
            pendingActivation = new ActivationRequest
            {
                Action = ActivationKind.SetSlider,
                Target = target,
                Tab = tab,
                Kind = WidgetKind.Slider,
                Label = label,
                Ordinal = ordinal,
                Value = value,
                SpokenLabel = spokenLabel,
                Member = member,
                Node = node,
            };
        }

        /// <summary>
        /// Arms the ordinal-th text field — identity is the ordinal among text fields alone —
        /// to return <paramref name="text"/>, consumed as native typing. Single slot.
        /// </summary>
        public static void RequestTextWrite(object target, InspectTabBase tab, int ordinal, string text, string spokenLabel,
            InteractiveMember member = null, InspectionTreeItem node = null)
        {
            if (target == null || tab == null)
            {
                return;
            }
            pendingActivation = new ActivationRequest
            {
                Action = ActivationKind.SetText,
                Target = target,
                Tab = tab,
                Kind = WidgetKind.TextField,
                Label = "",
                Ordinal = ordinal,
                Text = text,
                SpokenLabel = spokenLabel,
                Member = member,
                Node = node,
            };
        }

        /// <summary>True only while <see cref="RunActivation"/> is executing — arms <see cref="DialogInterceptionPatch"/> to route any FloatMenu the fired handler opens through the windowless path.</summary>
        internal static bool ActivationInFlight { get; private set; }

        /// <summary>Clears every pending request and the cache at a session boundary (<see cref="StateResetRegistry"/>).</summary>
        public static void ResetSession()
        {
            pendingActivation = null;
            pending.Clear();
            cache.Clear();
            ActivationInFlight = false;
            treeRefreshAfterCapture = null;
            paritySectionAfterCapture = null;
            controlRefreshAfterCapture = null;
            InspectTabRowRing.Forget();
        }

        /// <summary>The last successful capture; false when the cache holds none and callers fall back.</summary>
        public static bool TryGetRows(object target, InspectTabBase tab, out List<FoldedRow> rows)
        {
            rows = null;
            if (target == null || tab == null)
            {
                return false;
            }
            if (cache.TryGetValue((target, tab), out Entry entry) && entry.Rows != null)
            {
                rows = entry.Rows;
                return true;
            }
            return false;
        }

        /// <summary>
        /// How many bands the cached capture folded from — the guard
        /// <see cref="InspectTabRowRing"/>'s live geometry pass checks its banding against.
        /// </summary>
        internal static bool TryGetBandCount(object target, InspectTabBase tab, out int bandCount)
        {
            bandCount = 0;
            if (target == null || tab == null)
            {
                return false;
            }
            if (cache.TryGetValue((target, tab), out Entry entry) && entry.Rows != null)
            {
                bandCount = entry.BandCount;
                return true;
            }
            return false;
        }

        /// <summary>The raw captured widget stream, or false when the cache holds none.</summary>
        public static bool TryGetWidgets(object target, InspectTabBase tab, out List<CapturedWidget> widgets)
        {
            widgets = null;
            if (target == null || tab == null)
            {
                return false;
            }
            if (cache.TryGetValue((target, tab), out Entry entry) && entry.Widgets != null)
            {
                widgets = entry.Widgets;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The raw widget stream and its per-widget tooltips for
        /// <see cref="CaptureParityDiff"/>, or false when the cache holds neither. Both
        /// lists come from one entry, so they are always index-aligned.
        /// </summary>
        internal static bool TryGetParityCapture(object target, InspectTabBase tab,
            out List<CapturedWidget> widgets, out List<string> widgetTips)
        {
            widgets = null;
            widgetTips = null;
            if (target == null || tab == null)
            {
                return false;
            }
            if (cache.TryGetValue((target, tab), out Entry entry)
                && entry.Widgets != null && entry.WidgetTips != null)
            {
                widgets = entry.Widgets;
                widgetTips = entry.WidgetTips;
                return true;
            }
            return false;
        }

        // Known tabs are captured too, for the parity diff, so a single pawn posts ~8-10
        // requests. The remainder stays pending for later frames: the user is always at
        // least a keystroke away from reading a category.
        private const int MaxRequestsPerFrame = 4;

        internal static void PumpOnRepaint()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }
            // The armed activation drains FIRST, even on a frame that also pumps captures.
            if (pendingActivation != null)
            {
                ActivationRequest request = pendingActivation;
                pendingActivation = null;
                RunActivation(request);
            }
            if (pending.Count == 0)
            {
                return;
            }
            if (cache.Count > MaxCacheEntries)
            {
                cache.Clear();
            }
            int count = Math.Min(MaxRequestsPerFrame, pending.Count);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    RunOne(pending[i]);
                }
            }
            finally
            {
                pending.RemoveRange(0, count);
            }
        }

        private static void RunOne(Request request)
        {
            var entry = new Entry();
            cache[(request.Target, request.Tab)] = entry;
            try
            {
                bool gone = request.Target is Thing thing
                    ? thing.Destroyed || !thing.SpawnedOrAnyParentSpawned
                    : request.Target is Zone zone && (zone.Map == null || !zone.Map.zoneManager.AllZones.Contains(zone));
                if (gone)
                {
                    entry.Error = "target no longer present";
#if DEBUG
                    TraceCaptureFailure(request.Target, request.Tab, entry.Error);
#endif
                    return;
                }
                var widgets = new List<CapturedWidget>();
                if (!InspectTabCaptureHarness.TryCapturePass(request.Tab, request.Target, widgets, out string error))
                {
                    entry.Error = error;
#if DEBUG
                    TraceCaptureFailure(request.Target, request.Tab, entry.Error);
#endif
                    return;
                }
                // The parity diff bands and compares the raw widgets, not the folded rows.
                entry.Widgets = widgets;
                // Tooltips resolve now, while the detached tip index is valid. Screen space
                // with clip equality, as CapturedRowFolder.FoldBand resolves: a tab's draw
                // spans nested GUI groups whose group-local rects all restart near (0,0),
                // and a local query there fuses every overlapping group's tips onto every row.
                var widgetTips = new List<string>(widgets.Count);
                foreach (CapturedWidget widget in widgets)
                {
                    widgetTips.Add(TooltipCapture.TryResolveDetachedAtScreen(widget.ScreenRect, widget.Clip));
                }
                entry.WidgetTips = widgetTips;
                List<FoldedRow> rows = CapturedRowFolder.Fold(widgets, out int bandCount);
                entry.BandCount = bandCount;
                if (rows.Count > 0)
                {
                    entry.Rows = rows;
                }
                else
                {
                    entry.Error = "tab drew no readable rows";
#if DEBUG
                    TraceCaptureFailure(request.Target, request.Tab, entry.Error);
#endif
                }
            }
            catch (Exception ex)
            {
                entry.Error = ex.ToString();
                Log.Warning($"[RimWorld Access] Capture failed for {request.Tab.GetType().Name}: {ex.Message}");
#if DEBUG
                TraceCaptureFailure(request.Target, request.Tab, entry.Error);
#endif
            }
            finally
            {
                if (treeRefreshAfterCapture.HasValue
                    && ReferenceEquals(treeRefreshAfterCapture.Value.Target, request.Target)
                    && ReferenceEquals(treeRefreshAfterCapture.Value.Tab, request.Tab))
                {
                    treeRefreshAfterCapture = null;
                    if (entry.Rows != null)
                    {
                        InspectionTreeBuilder.RebuildCapturedCategory(request.Target, request.Tab);
                    }
                }
                if (paritySectionAfterCapture.HasValue
                    && ReferenceEquals(paritySectionAfterCapture.Value.Target, request.Target)
                    && ReferenceEquals(paritySectionAfterCapture.Value.Tab, request.Tab))
                {
                    paritySectionAfterCapture = null;
                    if (entry.Widgets != null)
                    {
                        InspectionTreeBuilder.AppendUnmirroredSectionAfterRecapture(request.Target, request.Tab);
                    }
                }
                if (controlRefreshAfterCapture != null
                    && ReferenceEquals(controlRefreshAfterCapture.Target, request.Target)
                    && ReferenceEquals(controlRefreshAfterCapture.Tab, request.Tab))
                {
                    ControlRefreshRequest refresh = controlRefreshAfterCapture;
                    controlRefreshAfterCapture = null;
                    if (entry.Rows != null && entry.Widgets != null)
                    {
                        RunControlRefresh(refresh, entry);
                    }
                }
            }
        }

        /// <summary>
        /// Refreshes the acted slider/text control's shared <see cref="InteractiveMember"/>
        /// and its tree node from the follow-up capture. The widget is re-found by exact
        /// descriptor first, then by position among same-kind widgets while that population
        /// is unchanged — a slider whose raw label embeds the value changes identity across
        /// the write. Silent; a miss is a safe no-op.
        /// </summary>
        private static void RunControlRefresh(ControlRefreshRequest refresh, Entry entry)
        {
            int index = CaptureDescriptor.FindIndex(entry.Widgets, refresh.Kind, refresh.Label, refresh.Ordinal);
            if (index < 0 && refresh.KindOrdinal >= 0)
            {
                int kindCount = 0;
                int found = -1;
                for (int i = 0; i < entry.Widgets.Count; i++)
                {
                    if (entry.Widgets[i].Kind != refresh.Kind)
                    {
                        continue;
                    }
                    if (kindCount == refresh.KindOrdinal)
                    {
                        found = i;
                    }
                    kindCount++;
                }
                index = kindCount == refresh.KindCount ? found : -1;
            }
            if (index < 0)
            {
                return;
            }
            CapturedWidget widget = entry.Widgets[index];
            foreach (FoldedRow row in entry.Rows)
            {
                List<InteractiveMember> members = row.Interactives;
                if (members == null)
                {
                    continue;
                }
                for (int m = 0; m < members.Count; m++)
                {
                    if (!ReferenceEquals(members[m].Source, widget))
                    {
                        continue;
                    }
                    InteractiveMember fresh = members[m];
                    refresh.Member.RawLabel = fresh.RawLabel;
                    refresh.Member.Ordinal = fresh.Ordinal;
                    refresh.Member.Fragment = fresh.Fragment;
                    refresh.Member.Source = fresh.Source;
                    InspectionTreeBuilder.RelabelCapturedControl(refresh.Node, row, m);
                    return;
                }
            }
        }

        /// <summary>
        /// Fires the identified control's own vanilla handler via an armed capture pass,
        /// announces the outcome through <see cref="DevActionOutcome"/> as one utterance,
        /// then re-captures so the tree's parity rows reflect the change.
        /// </summary>
        private static void RunActivation(ActivationRequest request)
        {
            bool gone = request.Target is Thing thing
                ? thing.Destroyed || !thing.SpawnedOrAnyParentSpawned
                : request.Target is Zone zone && (zone.Map == null || !zone.Map.zoneManager.AllZones.Contains(zone));
            if (gone)
            {
                TolkHelper.SpeakData("RimWorldAccess.Inspection.Parity.NoLongerShown".Translate(request.SpokenLabel).ToString());
                return;
            }

            var sink = new List<CapturedWidget>();
            bool fired = false;
            string error = null;

            ActivationInFlight = true;
            try
            {
                DevActionOutcome.RunAndAnnounce(request.SpokenLabel,
                    () =>
                    {
                        // A failed pass logs its warning AFTER RunAndAnnounce returns:
                        // writing it here trips the announcer's log precedence and speaks
                        // the raw warning instead of the clean ActivateFailed message.
                        switch (request.Action)
                        {
                            case ActivationKind.Adjust:
                                InspectTabCaptureHarness.TryAdjustPass(request.Tab, request.Target,
                                    request.Label, request.Ordinal, request.Direction, sink, out fired, out error);
                                break;
                            case ActivationKind.SetSlider:
                                InspectTabCaptureHarness.TrySetSliderPass(request.Tab, request.Target,
                                    request.Label, request.Ordinal, request.Value, sink, out fired, out error);
                                break;
                            case ActivationKind.SetText:
                                InspectTabCaptureHarness.TrySetTextPass(request.Tab, request.Target,
                                    request.Ordinal, request.Text, sink, out fired, out error);
                                break;
                            default:
                                InspectTabCaptureHarness.TryActivatePass(request.Tab, request.Target,
                                    request.Kind, request.Label, request.Ordinal, sink, out fired, out error);
                                break;
                        }
                    },
                    () =>
                    {
                        if (error != null)
                        {
                            return "RimWorldAccess.Inspection.Parity.ActivateFailed".Translate(request.SpokenLabel).ToString();
                        }
                        if (!fired)
                        {
                            // A matched-but-gated control is refused by the armed channel,
                            // exactly as a mouse click would be.
                            return RimWorldAccess.Shell.WidgetCapture.ArmedGateBlocked
                                ? "RimWorldAccess.Inspection.Parity.Disabled".Translate(request.SpokenLabel).ToString()
                                : "RimWorldAccess.Inspection.Parity.NoLongerShown".Translate(request.SpokenLabel).ToString();
                        }
                        // A checkbox/slider/text-field/tab's new state isn't
                        // self-announcing, so its folded fragment is re-read and spoken.
                        // Buttons and radios return null: a button's outcome rides the
                        // window/float-menu/log precedences, and a radio's captured
                        // Selected state is recorded BEFORE the forced click, so the Done
                        // fallback is the honest answer.
                        switch (request.Kind)
                        {
                            case WidgetKind.Checkbox:
                            case WidgetKind.Slider:
                            case WidgetKind.TextField:
                            case WidgetKind.Tab:
                                return ResolveFragmentOutcome(request);
                            default:
                                return null;
                        }
                    });
            }
            finally
            {
                ActivationInFlight = false;
            }

            if (error != null)
            {
                Log.Warning("[RimWorld Access] Armed activation failed for "
                    + request.Tab.GetType().Name + ": " + error);
#if DEBUG
                TraceCaptureFailure(request.Target, request.Tab, "armed activation failed: " + error);
#endif
            }

            // A fired Activate can reshape the whole tab, so the recapture below also owes
            // the tree an in-place rebuild once its fresh rows land (see RunOne).
            if (request.Action == ActivationKind.Activate && fired && error == null
                && WindowlessInspectionState.IsActive)
            {
                treeRefreshAfterCapture = (request.Target, request.Tab);
            }
            else if (fired && error == null && request.Member != null
                && WindowlessInspectionState.IsActive)
            {
                // Writes get no rebuild — it would fight the cursor sitting on the
                // Increase/Decrease/Set-value children — but the acted control's member and
                // node label still owe a refresh from the recapture below.
                controlRefreshAfterCapture = BuildControlRefresh(request);
            }

            RequestCapture(request.Target, request.Tab);
        }

        /// <summary>
        /// The (target, tab) pair whose NEXT completed capture rebuilds that category's
        /// tree rows in place. Consumed by <see cref="RunOne"/> on success or failure alike,
        /// never retried.
        /// </summary>
        private static (object Target, InspectTabBase Tab)? treeRefreshAfterCapture;

        /// <summary>An adapter category rebuilt after its own action, owed its parity section once
        /// the post-action capture lands.</summary>
        private static (object Target, InspectTabBase Tab)? paritySectionAfterCapture;

        /// <summary>
        /// Drops the pre-action capture and queues a fresh one: diffing the rebuilt rows against
        /// the old draw would list every changed row as unmirrored.
        /// </summary>
        internal static void RecaptureForParity(object target, InspectTabBase tab)
        {
            if (target == null || tab == null)
            {
                return;
            }
            cache.Remove((target, tab));
            RequestCapture(target, tab);
            paritySectionAfterCapture = (target, tab);
        }

        /// <summary>
        /// One armed slider/text write's follow-up for <see cref="RunControlRefresh"/>.
        /// Consumed by <see cref="RunOne"/> on success or failure alike, never retried;
        /// single slot, like the activation channel it shadows.
        /// </summary>
        private sealed class ControlRefreshRequest
        {
            public object Target;
            public InspectTabBase Tab;
            public WidgetKind Kind;
            public string Label;
            public int Ordinal;
            public InteractiveMember Member;
            public InspectionTreeItem Node;

            // Position among same-kind widgets in the PRE-write stream, with that stream's
            // same-kind count: the fallback identity when the write changed the raw label.
            // -1 when the old stream is gone.
            public int KindOrdinal = -1;
            public int KindCount = -1;
        }

        private static ControlRefreshRequest controlRefreshAfterCapture;

        private static ControlRefreshRequest BuildControlRefresh(ActivationRequest request)
        {
            var refresh = new ControlRefreshRequest
            {
                Target = request.Target,
                Tab = request.Tab,
                Kind = request.Kind,
                Label = request.Label,
                Ordinal = request.Ordinal,
                Member = request.Member,
                Node = request.Node,
            };
            if (TryGetWidgets(request.Target, request.Tab, out List<CapturedWidget> widgets))
            {
                int index = CaptureDescriptor.FindIndex(widgets, request.Kind, request.Label, request.Ordinal);
                if (index >= 0)
                {
                    int kindOrdinal = 0;
                    int kindCount = 0;
                    for (int i = 0; i < widgets.Count; i++)
                    {
                        if (widgets[i].Kind != request.Kind)
                        {
                            continue;
                        }
                        if (i < index)
                        {
                            kindOrdinal++;
                        }
                        kindCount++;
                    }
                    refresh.KindOrdinal = kindOrdinal;
                    refresh.KindCount = kindCount;
                }
            }
            return refresh;
        }

        /// <summary>
        /// Re-reads the acted-on widget with a plain unarmed pass in the same frame and
        /// returns its folded fragment; null on any miss, falling back to the Done key.
        /// </summary>
        private static string ResolveFragmentOutcome(ActivationRequest request)
        {
            var freshSink = new List<CapturedWidget>();
            if (!InspectTabCaptureHarness.TryCapturePass(request.Tab, request.Target, freshSink, out _))
            {
                return null;
            }
            int index = CaptureDescriptor.FindIndex(freshSink, request.Kind, request.Label, request.Ordinal);
            if (index < 0)
            {
                return null;
            }
            FoldedRow row = CapturedRowFolder.FoldSubset(freshSink, new List<int> { index }, new string[freshSink.Count]);
            return row?.Text;
        }

#if DEBUG
        // Capture failures repeat on every tree open, so each distinct (tab, reason)
        // signature reports once and then every 50th recurrence, carrying its count.
        private static readonly Dictionary<string, int> tracedCaptureFailures =
            new Dictionary<string, int>();

        private static void TraceCaptureFailure(object target, InspectTabBase tab, string error)
        {
            string reason = FirstLine(error);
            string signature = (tab?.GetType().Name ?? "(null tab)") + "|" + reason;
            tracedCaptureFailures.TryGetValue(signature, out int seen);
            tracedCaptureFailures[signature] = ++seen;
            if (seen != 1 && seen % 50 != 0)
            {
                return;
            }
            string label = target is Thing thing ? thing.LabelCap.ToString()
                : target is Zone zone ? zone.label
                : target?.ToString() ?? "(null target)";
            ShellDev.QARecord("capture",
                (tab?.GetType().Name ?? "(null tab)") + " for " + label + ": " + reason
                + (seen > 1 ? " (x" + seen + ")" : ""));
        }

        /// <summary>The first line of an error, capped: a thrown capture reports a whole stack trace.</summary>
        private static string FirstLine(string error)
        {
            if (string.IsNullOrEmpty(error))
            {
                return "(no reason given)"; // l10n-exempt: DEBUG-only QA trace line, never spoken or shown
            }
            int br = error.IndexOfAny(new[] { '\r', '\n' });
            string line = br >= 0 ? error.Substring(0, br) : error;
            return line.Length > 200 ? line.Substring(0, 200) + "..." : line;
        }
#endif
    }

    /// <summary>
    /// Runs pending captures on Repaint frames. Postfix so every real window and focus-scope
    /// capture pass has drawn and closed before a detached pass opens, which is the ordering
    /// BeginDetachedPass relies on.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot), "UIRootOnGUI")]
    internal static class InspectTabCaptureServicePumpPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            InspectTabCaptureService.PumpOnRepaint();
        }
    }
}
