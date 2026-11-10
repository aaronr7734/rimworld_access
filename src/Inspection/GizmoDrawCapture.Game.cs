using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>What one detached draw of a gizmo showed: an optional title line, the remaining readout, and its tooltip.</summary>
    internal sealed class GizmoDrawReading
    {
        public string Title;
        public string Readout;
        public string Tip;
        public int Frame;
    }

    /// <summary>
    /// Reads a gizmo no handler understands from its own GizmoOnGUI, drawn once offscreen through
    /// <see cref="InspectTabCaptureHarness.TryCaptureDraw"/>. Same two-step shape as
    /// <see cref="InspectTabCaptureService"/>: announcements run at key time but capture needs a
    /// Repaint, so reads only consult the cache and queue a refresh, and the inspect-tab pump's UIRoot
    /// postfix draws them on the next Repaint. The vanilla gizmo grid warms the cache for a single selection, because it rebuilds its
    /// gizmos every frame and the G menu snapshots fresh instances: those entries key by
    /// (type, owner), kept only when the owner shows one gizmo of that type.
    /// </summary>
    internal static class GizmoDrawCapture
    {
        private const int RefreshFrames = 15;
        private const int IdleRefreshFrames = 60;
        private const int MaxRequestsPerFrame = 4;
        private const int MaxCacheEntries = 128;
        private const int MaxTitleLength = 40;
        private const float ReadWarmSeconds = 1f;

        private struct Request
        {
            public Gizmo Gizmo;
            public ISelectable Owner;
            public bool ByOwner;
        }

        private static readonly Dictionary<Gizmo, GizmoDrawReading> byInstance = new Dictionary<Gizmo, GizmoDrawReading>();
        private static readonly Dictionary<(Type, ISelectable), GizmoDrawReading> byOwner = new Dictionary<(Type, ISelectable), GizmoDrawReading>();
        private static readonly List<Request> pending = new List<Request>();
        private static readonly Dictionary<Type, bool> eligibleByType = new Dictionary<Type, bool>();
        private static readonly Dictionary<Type, int> gridTypeCounts = new Dictionary<Type, int>();
        private static readonly List<Gizmo> gridEligible = new List<Gizmo>();
        private static float lastReadTime = float.NegativeInfinity;

        /// <summary>True while a probe draw runs; its sounds are silenced.</summary>
        internal static bool InFlight { get; private set; }

        /// <summary>Not a Command and claimed by no registered handler: the only gizmos read this way.</summary>
        internal static bool IsEligible(Gizmo gizmo)
        {
            if (gizmo == null || gizmo is Command)
                return false;
            Type type = gizmo.GetType();
            if (!eligibleByType.TryGetValue(type, out bool eligible))
            {
                eligible = !GizmoHandlerRegistry.HasNonFallbackHandler(type);
                eligibleByType[type] = eligible;
            }
            return eligible;
        }

        /// <summary>The cached reading for this gizmo, queueing a refresh when it is missing or stale. Never draws.</summary>
        internal static bool TryGet(Gizmo gizmo, ISelectable owner, out GizmoDrawReading reading)
        {
            reading = null;
            try
            {
                if (!IsEligible(gizmo))
                    return false;
                lastReadTime = Time.realtimeSinceStartup;

                byInstance.TryGetValue(gizmo, out reading);
                // The grid refreshes the owner entry continuously; prefer it over a stale instance read.
                if ((reading == null || IsStale(reading)) && owner != null
                    && byOwner.TryGetValue((gizmo.GetType(), owner), out GizmoDrawReading byOwnerReading)
                    && (reading == null || byOwnerReading.Frame > reading.Frame))
                    reading = byOwnerReading;

                if (reading == null || IsStale(reading))
                    Enqueue(new Request { Gizmo = gizmo, Owner = owner, ByOwner = false });
                return reading != null;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("GizmoDrawCapture.TryGet", ex);
                reading = null;
                return false;
            }
        }

        /// <summary>
        /// Warms the owner-keyed cache from the gizmos vanilla's grid just drew for one selected object.
        /// Missing entries always queue, so a menu's first read finds one; stale ones refresh only while read.
        /// </summary>
        internal static void NoteGridDrawn(IEnumerable<Gizmo> gizmos)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint || gizmos == null)
                return;
            if (Find.Selector == null || Find.Selector.NumSelected != 1 || !(Find.Selector.SingleSelectedObject is ISelectable owner))
                return;

            gridTypeCounts.Clear();
            gridEligible.Clear();
            foreach (Gizmo gizmo in gizmos)
            {
                if (IsEligible(gizmo))
                {
                    gridEligible.Add(gizmo);
                    gridTypeCounts.TryGetValue(gizmo.GetType(), out int count);
                    gridTypeCounts[gizmo.GetType()] = count + 1;
                }
            }
            if (gridEligible.Count == 0)
                return;

            bool refreshStale = GizmoNavigationState.IsActive || Time.realtimeSinceStartup - lastReadTime <= ReadWarmSeconds;
            foreach (Gizmo gizmo in gridEligible)
            {
                var key = (gizmo.GetType(), owner);
                if (gridTypeCounts[gizmo.GetType()] > 1)
                {
                    byOwner.Remove(key);
                    continue;
                }
                // Idle warming stays slow but alive, so the menu's first read is at most a second old.
                if (!byOwner.TryGetValue(key, out GizmoDrawReading cached)
                    || Time.frameCount - cached.Frame > (refreshStale ? RefreshFrames : IdleRefreshFrames))
                    Enqueue(new Request { Gizmo = gizmo, Owner = owner, ByOwner = true });
            }
            gridEligible.Clear();
        }

        internal static void PumpOnRepaint()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint || pending.Count == 0)
                return;

            if (byInstance.Count > MaxCacheEntries)
                byInstance.Clear();
            if (byOwner.Count > MaxCacheEntries)
                byOwner.Clear();

            int count = Math.Min(MaxRequestsPerFrame, pending.Count);
            try
            {
                for (int i = 0; i < count; i++)
                    RunOne(pending[i]);
            }
            finally
            {
                pending.RemoveRange(0, count);
            }
        }

        public static void Reset()
        {
            byInstance.Clear();
            byOwner.Clear();
            pending.Clear();
            eligibleByType.Clear();
            gridEligible.Clear();
            lastReadTime = float.NegativeInfinity;
        }

        private static bool IsStale(GizmoDrawReading reading)
        {
            return Time.frameCount - reading.Frame > RefreshFrames;
        }

        private static void Enqueue(Request request)
        {
            foreach (Request queued in pending)
            {
                bool same = request.ByOwner
                    ? queued.ByOwner && queued.Owner == request.Owner && queued.Gizmo.GetType() == request.Gizmo.GetType()
                    : !queued.ByOwner && queued.Gizmo == request.Gizmo;
                if (same)
                    return;
            }
            pending.Add(request);
        }

        private static void RunOne(Request request)
        {
            try
            {
                GizmoDrawReading reading = GizmoNavigationState.WithSelectableSelected(request.Owner,
                    () => request.Gizmo.Visible ? Capture(request.Gizmo) : null);
                if (reading == null)
                    return;
                if (request.ByOwner)
                    byOwner[(request.Gizmo.GetType(), request.Owner)] = reading;
                else
                    byInstance[request.Gizmo] = reading;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("GizmoDrawCapture.RunOne", ex);
            }
        }

        private static GizmoDrawReading Capture(Gizmo gizmo)
        {
            float maxWidth = UI.screenWidth;
            float width = gizmo.GetWidth(maxWidth);
            var widgets = new List<CapturedWidget>();
            Rect gizmoScreenRect = default(Rect);
            GuiSpace.ClipKey gizmoClip = default(GuiSpace.ClipKey);
            bool ok;
            string error;

            InFlight = true;
            try
            {
                ok = InspectTabCaptureHarness.TryCaptureDraw(() =>
                {
                    gizmoScreenRect = GuiSpace.ToScreen(new Rect(0f, 0f, width, Gizmo.Height));
                    gizmoClip = GuiSpace.CurrentClip();
                    gizmo.GizmoOnGUI(Vector2.zero, maxWidth, default(GizmoRenderParms));
                }, new Vector2(width, Gizmo.Height), widgets, out error);
            }
            finally
            {
                InFlight = false;
            }

            if (!ok)
            {
                ModLogger.LimitedError("GizmoDrawCapture " + gizmo.GetType().FullName, new InvalidOperationException(error));
                return null;
            }

            // Resolved now: the detached tip index only lives until the next detached pass.
            string tip = CompatText.Flatten(TooltipCapture.TryResolveDetachedAtScreen(gizmoScreenRect, gizmoClip));

            var lines = new List<string>();
            string title = null;
            foreach (List<int> band in CapturedRowFolder.BandsFor(widgets))
            {
                string line = ReadBand(widgets, band, out bool standaloneLabel);
                if (string.IsNullOrEmpty(line))
                    continue;
                if (title == null && lines.Count == 0 && standaloneLabel && IsTitleLike(line))
                    title = line;
                else
                    lines.Add(line);
            }

            if (title == null && lines.Count == 0 && string.IsNullOrEmpty(tip))
                return null;

            return new GizmoDrawReading
            {
                Title = title,
                Readout = lines.Count > 0 ? CompatText.JoinSentences(lines) : null,
                Tip = string.IsNullOrEmpty(tip) || tip == title ? null : tip,
                Frame = Time.frameCount,
            };
        }

        /// <summary>
        /// One visual row as text: labels and fillable bars folded together, with a "caption:" label
        /// paired with the value beside it. Controls are skipped: activation stays on the gizmo's own ProcessInput.
        /// </summary>
        private static string ReadBand(List<CapturedWidget> widgets, List<int> band, out bool standaloneLabel)
        {
            standaloneLabel = band.Count == 1 && widgets[band[0]].Kind == WidgetKind.Label;
            var parts = new List<string>();
            var subset = new List<int>();
            IList<string> noTips = new string[widgets.Count];
            for (int p = 0; p < band.Count; p++)
            {
                CapturedWidget widget = widgets[band[p]];
                string pair = CapturedRowFolder.CaptionValuePairAt(widgets, band, p);
                if (pair != null)
                {
                    AddFolded(parts, widgets, subset, noTips);
                    parts.Add(pair);
                    p++;
                }
                else if (widget.Kind == WidgetKind.FillableBar
                    || (widget.Kind == WidgetKind.Label && CompatText.Flatten(widget.SpokenLabel).Trim().Length > 0))
                {
                    subset.Add(band[p]);
                }
            }
            AddFolded(parts, widgets, subset, noTips);
            return string.Join(", ", parts);
        }

        private static void AddFolded(List<string> parts, List<CapturedWidget> widgets, List<int> subset, IList<string> tips)
        {
            if (subset.Count == 0)
                return;
            FoldedRow row = CapturedRowFolder.FoldSubset(widgets, subset, tips);
            subset.Clear();
            string text = row == null ? "" : CompatText.Flatten(row.Text).Trim();
            if (text.Length > 0)
                parts.Add(text);
        }

        /// <summary>A short line with a letter in it and no trailing colon reads as a heading, not a field.</summary>
        private static bool IsTitleLike(string line)
        {
            // A "caption: value" line is a readout, never the gizmo's name.
            if (line.Length > MaxTitleLength || line.EndsWith(":") || line.Contains(": "))
                return false;
            foreach (char c in line)
            {
                if (char.IsLetter(c))
                    return true;
            }
            return false;
        }
    }

    /// <summary>Feeds every gizmo the vanilla grid draws to the capture cache, Repaint only.</summary>
    [HarmonyPatch(typeof(GizmoGridDrawer), nameof(GizmoGridDrawer.DrawGizmoGrid))]
    internal static class GizmoDrawCaptureGridPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IEnumerable<Gizmo> gizmos, bool multipleSelected)
        {
            if (multipleSelected)
                return;
            try
            {
                GizmoDrawCapture.NoteGridDrawn(gizmos);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("GizmoDrawCaptureGridPatch", ex);
            }
        }
    }
}
