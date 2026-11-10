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
    /// Repaint, so reads only consult the cache and queue a refresh, and the pump draws on the next
    /// Repaint. The vanilla gizmo grid warms the cache for a single selection, because it rebuilds its
    /// gizmos every frame and the G menu snapshots fresh instances: those entries key by
    /// (type, owner), kept only when the owner shows one gizmo of that type.
    /// </summary>
    internal static class GizmoDrawCapture
    {
        private const int RefreshFrames = 15;
        private const int MaxRequestsPerFrame = 4;
        private const int MaxCacheEntries = 128;
        private const int MaxTitleLength = 40;

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

        /// <summary>Warms the owner-keyed cache from the gizmos vanilla's grid just drew for one selected object.</summary>
        internal static void NoteGridDrawn(IEnumerable<Gizmo> gizmos)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint || gizmos == null)
                return;
            if (Find.Selector == null || Find.Selector.NumSelected != 1 || !(Find.Selector.SingleSelectedObject is ISelectable owner))
                return;

            gridTypeCounts.Clear();
            foreach (Gizmo gizmo in gizmos)
            {
                if (IsEligible(gizmo))
                {
                    gridTypeCounts.TryGetValue(gizmo.GetType(), out int count);
                    gridTypeCounts[gizmo.GetType()] = count + 1;
                }
            }
            if (gridTypeCounts.Count == 0)
                return;

            foreach (Gizmo gizmo in gizmos)
            {
                if (!IsEligible(gizmo))
                    continue;
                var key = (gizmo.GetType(), owner);
                if (gridTypeCounts[gizmo.GetType()] > 1)
                {
                    byOwner.Remove(key);
                    continue;
                }
                if (!byOwner.TryGetValue(key, out GizmoDrawReading cached) || IsStale(cached))
                    Enqueue(new Request { Gizmo = gizmo, Owner = owner, ByOwner = true });
            }
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
        /// One visual row as text: labels, a "caption:" label paired with the value beside it, and
        /// fillable bars. Controls are skipped: activation stays on the gizmo's own ProcessInput.
        /// </summary>
        private static string ReadBand(List<CapturedWidget> widgets, List<int> band, out bool standaloneLabel)
        {
            standaloneLabel = band.Count == 1 && widgets[band[0]].Kind == WidgetKind.Label;
            var parts = new List<string>();
            IList<string> noTips = null;
            for (int p = 0; p < band.Count; p++)
            {
                CapturedWidget widget = widgets[band[p]];
                CapturedWidget next = p + 1 < band.Count ? widgets[band[p + 1]] : null;
                if (widget.Kind == WidgetKind.Label)
                {
                    string text = CompatText.Flatten(widget.SpokenLabel).Trim();
                    if (text.Length == 0)
                        continue;
                    if (next != null && next.Kind == WidgetKind.FillableBar)
                    {
                        noTips = noTips ?? new string[widgets.Count];
                        AddFolded(parts, widgets, new List<int> { band[p], band[p + 1] }, noTips);
                        p++;
                        continue;
                    }
                    if (text.EndsWith(":") && next != null && next.Kind == WidgetKind.Label)
                    {
                        string value = CompatText.Flatten(next.SpokenLabel).Trim();
                        if (value.Length > 0)
                        {
                            parts.Add(CapturedRowFolder.ComposeCaptionValue(text, value));
                            p++;
                            continue;
                        }
                    }
                    parts.Add(text);
                }
                else if (widget.Kind == WidgetKind.FillableBar)
                {
                    noTips = noTips ?? new string[widgets.Count];
                    AddFolded(parts, widgets, new List<int> { band[p] }, noTips);
                }
            }
            return string.Join(", ", parts);
        }

        private static void AddFolded(List<string> parts, List<CapturedWidget> widgets, List<int> subset, IList<string> tips)
        {
            FoldedRow row = CapturedRowFolder.FoldSubset(widgets, subset, tips);
            if (row != null && !string.IsNullOrWhiteSpace(row.Text))
                parts.Add(row.Text.Trim());
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

    /// <summary>
    /// Runs queued gizmo captures on Repaint, from the same UIRoot postfix as the inspect-tab pump,
    /// so no real window's capture pass is open when the detached one begins.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot), "UIRootOnGUI")]
    internal static class GizmoDrawCapturePumpPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                GizmoDrawCapture.PumpOnRepaint();
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("GizmoDrawCapturePumpPatch", ex);
            }
        }
    }
}
