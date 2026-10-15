using System;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Converts a GUI-group-local rect to screen space, so widgets drawn in unrelated coordinate
    /// spaces (a dialog title at window-space (0,0) versus a checkbox inside a ScrollView whose
    /// local origin also restarts at (0,0)) can be matched generically instead of colliding on raw
    /// local-rect overlap.
    ///
    /// There is ONE such space, absolute UI points (the units of
    /// <c>Verse.UI.screenWidth</c>/<c>screenHeight</c>), correct at every clip depth and every
    /// <c>Prefs.UIScale</c> via the clip-stack translation described on
    /// <see cref="LocalToScreen"/>. The one shape it does not model is a non-translational GUI
    /// matrix inside the pass (a rotation or a scale about a pivot); no vanilla widget the
    /// capture layer taps is drawn under one.
    ///
    /// Screen rects alone cannot match two draws safely: the translation does not clamp to the
    /// active clip, so a row scrolled below a ScrollView's fold still resolves to a real
    /// off-screen rect that can numerically collide with an unrelated widget below the fold.
    /// <see cref="ClipKey"/> captures the active clip context (visible rect + stack depth) so
    /// callers can require CONTEXT equality alongside rect overlap; draws in different contexts
    /// never match regardless of numeric overlap. 1.7 lacks Unity's GUIClip members (WaylithClip).
    /// </summary>
    public static class GuiSpace
    {
        private static readonly Func<Rect> getVisibleRect;
        private static readonly Func<int> getDepth;

        static GuiSpace()
        {
            Type guiClipType = AccessTools.TypeByName("UnityEngine.GUIClip");
            bool unityClip = guiClipType != null && AccessTools.Property(guiClipType, "visibleRect") != null;
            if (!unityClip && WaylithClip.TryBind())
            {
                getVisibleRect = WaylithClip.VisibleRect;
                getDepth = WaylithClip.Depth;
                return;
            }
            getVisibleRect = ResolveVisibleRect();
            getDepth = ResolveDepth();
        }

        /// <summary>
        /// The active clip stack's context: the running intersection of every enclosing
        /// BeginGroup/ScrollView clip rect, plus stack depth to disambiguate two contexts sharing
        /// one visible rect. Exact-field equality is correct within a single pass.
        /// </summary>
        public struct ClipKey : IEquatable<ClipKey>
        {
            public Rect VisibleRect;
            public int Depth;

            public bool Equals(ClipKey other)
            {
                return VisibleRect.Equals(other.VisibleRect) && Depth == other.Depth;
            }

            public override bool Equals(object obj)
            {
                return obj is ClipKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return VisibleRect.GetHashCode() ^ Depth;
            }
        }

        private static Func<Rect> ResolveVisibleRect()
        {
            try
            {
                Type guiClipType = AccessTools.TypeByName("UnityEngine.GUIClip");
                PropertyInfo property = guiClipType == null ? null : AccessTools.Property(guiClipType, "visibleRect");
                MethodInfo getter = property == null ? null : property.GetGetMethod(true);
                if (getter == null)
                {
                    Log.Warning("[RimWorld Access] GuiSpace could not resolve UnityEngine.GUIClip.visibleRect; clip-context matching will degrade to one shared context (screen-rect-only matching).");
                    return null;
                }
                return AccessTools.MethodDelegate<Func<Rect>>(getter);
            }
            catch (Exception ex)
            {
                Log.Warning("[RimWorld Access] GuiSpace failed to bind UnityEngine.GUIClip.visibleRect: " + ex + "; clip-context matching will degrade to one shared context (screen-rect-only matching).");
                return null;
            }
        }

        private static Func<int> ResolveDepth()
        {
            try
            {
                Type guiClipType = AccessTools.TypeByName("UnityEngine.GUIClip");
                MethodInfo method = guiClipType == null
                    ? null
                    : AccessTools.Method(guiClipType, "Internal_GetCount", Type.EmptyTypes);
                if (method == null)
                {
                    Log.Warning("[RimWorld Access] GuiSpace could not resolve UnityEngine.GUIClip.Internal_GetCount(); clip-context matching will degrade to one shared context (screen-rect-only matching).");
                    return null;
                }
                return AccessTools.MethodDelegate<Func<int>>(method);
            }
            catch (Exception ex)
            {
                Log.Warning("[RimWorld Access] GuiSpace failed to bind UnityEngine.GUIClip.Internal_GetCount(): " + ex + "; clip-context matching will degrade to one shared context (screen-rect-only matching).");
                return null;
            }
        }

        /// <summary>
        /// The translation from the active GUI group's local space to absolute UI points, read from
        /// the live GUIClip stack (groups, scroll views, the GUI.Window origin) as vanilla's
        /// MouseoverSounds does. Never derive it from the pointer (UI mouse minus Event mouse): the
        /// two readings disagree for a frame after every pointer warp, and a rect published from that
        /// frame re-warps the pointer by the same error, forever. Zero outside an OnGUI pass.
        /// </summary>
        private static Vector2 LocalToScreen()
        {
            return Event.current == null ? Vector2.zero : GUIUtility.GUIToScreenPoint(Vector2.zero);
        }

        /// <summary>
        /// Converts <paramref name="guiRect"/> from the active GUI-group space to screen space.
        /// Only meaningful inside an active OnGUI pass, where the clip stack reflects live
        /// BeginGroup/ScrollView nesting. Sizes pass through untouched — IMGUI layout is already
        /// in UI points.
        /// </summary>
        public static Rect ToScreen(Rect guiRect)
        {
            Vector2 offset = LocalToScreen();
            return new Rect(guiRect.x + offset.x, guiRect.y + offset.y, guiRect.width, guiRect.height);
        }

        public static Rect Union(Rect a, Rect b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
        }

        /// <summary>
        /// The inverse of <see cref="ToScreen"/>: an absolute-UI-point rect expressed in the
        /// active GUI group's space, so a rect measured in one pass can be drawn in another.
        /// Translation only.
        /// </summary>
        public static Rect FromScreen(Rect screenRect)
        {
            Vector2 offset = LocalToScreen();
            return new Rect(screenRect.x - offset.x, screenRect.y - offset.y, screenRect.width, screenRect.height);
        }

        /// <summary>
        /// The part of <paramref name="guiRect"/> visible under the active clip stack, in screen
        /// space — empty when the rect is fully clipped away or the visibleRect binding degraded.
        /// GUIClip.visibleRect is in the CURRENT LOCAL space, so the intersection happens in local
        /// space and only the result is translated.
        /// </summary>
        public static Rect VisibleScreenRect(Rect guiRect)
        {
            if (getVisibleRect == null)
            {
                return default(Rect);
            }
            Rect vis = getVisibleRect();
            float xMin = Mathf.Max(guiRect.xMin, vis.xMin);
            float yMin = Mathf.Max(guiRect.yMin, vis.yMin);
            float xMax = Mathf.Min(guiRect.xMax, vis.xMax);
            float yMax = Mathf.Min(guiRect.yMax, vis.yMax);
            if (xMax <= xMin || yMax <= yMin)
            {
                return default(Rect);
            }
            return ToScreen(Rect.MinMaxRect(xMin, yMin, xMax, yMax));
        }

        /// <summary>
        /// <see cref="VisibleScreenRect"/> for a rect measured in a group whose clip stack is no
        /// longer live, such as a draw bracket's Postfix. The anchor is any widget the SAME group
        /// recorded during that pass: its local/screen pair carries the group's translation and
        /// <paramref name="anchorClip"/> its visible band, so the answer matches what the row
        /// would have produced mid-pass. Empty when the rect is clipped away, and for every rect
        /// when the <see cref="ClipKey"/> binding degraded.
        /// </summary>
        public static Rect VisibleScreenRectFrom(Rect anchorGuiRect, Rect anchorScreenRect, ClipKey anchorClip, Rect guiRect)
        {
            Rect vis = anchorClip.VisibleRect;
            float xMin = Mathf.Max(guiRect.xMin, vis.xMin);
            float yMin = Mathf.Max(guiRect.yMin, vis.yMin);
            float xMax = Mathf.Min(guiRect.xMax, vis.xMax);
            float yMax = Mathf.Min(guiRect.yMax, vis.yMax);
            if (xMax <= xMin || yMax <= yMin)
            {
                return default(Rect);
            }
            Vector2 offset = anchorScreenRect.position - anchorGuiRect.position;
            return new Rect(xMin + offset.x, yMin + offset.y, xMax - xMin, yMax - yMin);
        }

        /// <summary>
        /// True when NOTHING drawn under the active clip stack can be seen, however the surface
        /// is scrolled or resized: the OFF-SCREEN MEASUREMENT PASS signal. A caller sizing a
        /// scroll view around its own content draws it once into a scratch group parked far
        /// outside the screen, reads the layout height back, then draws the same content for
        /// real — every widget in that first pass runs its real code and hits every capture tap
        /// while being invisible, so a reader recording both passes shows each control twice.
        ///
        /// This asks about the CLIP, not the widget, because a merely scrolled-out row sits inside
        /// a viewport that is itself on screen, whereas a measurement pass runs under a clip region
        /// that is unreachable. Two scale-independent proofs: the running visible area is empty
        /// (Unity intersects each group with its parent), or the region sits at negative screen
        /// coordinates, where no positive GUI scale brings it back. Degrades to FALSE — record
        /// everything — when the reflection bindings are unavailable.
        /// </summary>
        public static bool ClipIsOffscreen()
        {
            if (getVisibleRect == null)
            {
                return false;
            }
            Rect vis = getVisibleRect();
            if (vis.width <= 0f || vis.height <= 0f)
            {
                return true;
            }
            Rect screen = ToScreen(vis);
            return screen.xMax <= 0f || screen.yMax <= 0f;
        }

        /// <summary>
        /// The active clip context, for matching alongside a screen rect. Falls back to
        /// <c>default(ClipKey)</c> — the same value every time — when either backing reflection
        /// binding failed to resolve, so every record shares one key and matching degrades to
        /// screen-rect-only rather than throwing.
        /// </summary>
        public static ClipKey CurrentClip()
        {
            if (getVisibleRect == null || getDepth == null)
            {
                return default(ClipKey);
            }
            return new ClipKey { VisibleRect = getVisibleRect(), Depth = getDepth() };
        }

        /// <summary>GUIClip's readings rebuilt from 1.7's <c>UnityStubs.Gui.IGuiBackend</c> group stack (clip rect in parent-local space, content origin, UI points). Reflection: absent on 1.6.</summary>
        private static class WaylithClip
        {
            private delegate void GetGroupFn(object backend, int index, out Rect rect, out Vector2 contentOrigin);

            private static Func<object> current;
            private static Func<object, int> groupCount;
            private static GetGroupFn getGroup;

            internal static bool TryBind()
            {
                try
                {
                    Type holder = AccessTools.TypeByName("UnityStubs.Gui.GuiBackend");
                    Type backendType = AccessTools.TypeByName("UnityStubs.Gui.IGuiBackend");
                    PropertyInfo currentProp = holder == null ? null : AccessTools.Property(holder, "Current");
                    PropertyInfo countProp = backendType == null ? null : AccessTools.Property(backendType, "GroupCount");
                    MethodInfo getGroupMethod = backendType == null ? null : AccessTools.Method(backendType, "GetGroup");
                    if (currentProp == null || countProp == null || getGroupMethod == null)
                    {
                        return false;
                    }

                    current = Expression.Lambda<Func<object>>(
                        Expression.Convert(Expression.Property(null, currentProp), typeof(object))).Compile();

                    ParameterExpression backend = Expression.Parameter(typeof(object), "backend");
                    groupCount = Expression.Lambda<Func<object, int>>(
                        Expression.Property(Expression.Convert(backend, backendType), countProp), backend).Compile();

                    ParameterExpression index = Expression.Parameter(typeof(int), "index");
                    ParameterExpression rect = Expression.Parameter(typeof(Rect).MakeByRefType(), "rect");
                    ParameterExpression origin = Expression.Parameter(typeof(Vector2).MakeByRefType(), "contentOrigin");
                    getGroup = Expression.Lambda<GetGroupFn>(
                        Expression.Call(Expression.Convert(backend, backendType), getGroupMethod, index, rect, origin),
                        backend, index, rect, origin).Compile();
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Warning("[RimWorld Access] GuiSpace failed to bind the 1.7 GUI backend's group stack: " + ex);
                    return false;
                }
            }

            internal static int Depth()
            {
                object backend = current();
                return backend == null ? 0 : groupCount(backend);
            }

            /// <summary>The running intersection of the screen and every open group, in the innermost group's local space, as GUIClip.visibleRect reports it.</summary>
            internal static Rect VisibleRect()
            {
                float xMin = 0f;
                float yMin = 0f;
                float xMax = UI.screenWidth;
                float yMax = UI.screenHeight;
                Vector2 parentOrigin = Vector2.zero;
                object backend = current();
                int count = backend == null ? 0 : groupCount(backend);
                for (int i = 0; i < count; i++)
                {
                    getGroup(backend, i, out Rect clip, out Vector2 origin);
                    xMin = Mathf.Max(xMin, clip.xMin + parentOrigin.x);
                    yMin = Mathf.Max(yMin, clip.yMin + parentOrigin.y);
                    xMax = Mathf.Min(xMax, clip.xMax + parentOrigin.x);
                    yMax = Mathf.Min(yMax, clip.yMax + parentOrigin.y);
                    parentOrigin = origin;
                }
                return Rect.MinMaxRect(xMin - parentOrigin.x, yMin - parentOrigin.y,
                    Mathf.Max(xMin, xMax) - parentOrigin.x, Mathf.Max(yMin, yMax) - parentOrigin.y);
            }
        }
    }
}
