using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The rect vanilla itself drew for each button of the bottom main button
    /// bar, keyed by <see cref="MainButtonDef"/>, plus the description that def
    /// answers with.
    ///
    /// A registry rather than arithmetic, and a def rather than the capture
    /// stream, for two reasons the bar's own draw code states. The layout is
    /// screen-width arithmetic over the VISIBLE buttons with a half-width share
    /// for minimized ones and the remainder handed to the last
    /// (decompiled RimWorld/MainButtonsRoot.cs:64-93), so nothing outside that
    /// loop knows a button's rect. And the buttons draw through
    /// <c>Widgets.ButtonTextSubtle</c>, whose caption is a separate widget in a
    /// rect offset past the button's own right edge while an icon button passes
    /// no caption at all (RimWorld/MainButtonWorker.cs:84-91): the stream can
    /// neither tell one button's caption from the next's nor name the Menu and
    /// Factions buttons at all.
    /// </summary>
    internal static class MainButtonRectRegistry
    {
        private static readonly Dictionary<MainButtonDef, Rect> rects = new Dictionary<MainButtonDef, Rect>();

        /// <summary>Draw order of <see cref="rects"/>' keys — the ordinal hover identifies a hit button by.</summary>
        private static readonly List<MainButtonDef> drawOrder = new List<MainButtonDef>();

        private static bool recording;

        /// <summary>Frame of the most recent bar draw — see <see cref="TryHitTest"/>.</summary>
        private static int lastDrawFrame = -1;

        /// <summary>Hotkey label memo for the button under a continuous dwell; see <see cref="ReachableHotkeyLabel"/>.</summary>
        private static MainButtonDef hotkeyDef;
        private static string hotkeyLabel;
        private static int hotkeyFrame = -1;

        /// <summary>
        /// Opens the recording window and drops every rect from the previous bar
        /// draw, so a button that has since stopped being visible can never be
        /// hit-tested against a stale rect.
        /// </summary>
        internal static void BeginFrame()
        {
            rects.Clear();
            drawOrder.Clear();
            lastDrawFrame = Time.frameCount;
            recording = true;
        }

        /// <summary>Closes the recording window. Called from a <c>finally</c> so a thrown draw pass cannot leave it open.</summary>
        internal static void EndFrame()
        {
            recording = false;
        }

        /// <summary>No-op unless called between <see cref="BeginFrame"/> and <see cref="EndFrame"/>.</summary>
        internal static void Record(MainButtonDef def, Rect rect)
        {
            if (!recording || def == null)
            {
                return;
            }
            if (!rects.ContainsKey(def))
            {
                drawOrder.Add(def);
            }
            rects[def] = rect;
        }

        /// <summary>
        /// The button whose drawn rect contains <paramref name="point"/> (UI
        /// space), with its draw ordinal. Last drawn wins an overlap, matching
        /// the topmost thing a pointer would hit. A bar that has not drawn since
        /// the previous frame owns nothing — the map screen keeps its own bar
        /// hidden in screenshot mode and while a long event runs.
        /// </summary>
        internal static bool TryHitTest(Vector2 point, out MainButtonDef def, out int ordinal)
        {
            if (Time.frameCount - lastDrawFrame > 1)
            {
                def = null;
                ordinal = -1;
                return false;
            }
            for (int i = drawOrder.Count - 1; i >= 0; i--)
            {
                if (rects.TryGetValue(drawOrder[i], out Rect rect) && rect.Contains(point))
                {
                    def = drawOrder[i];
                    ordinal = i;
                    return true;
                }
            }
            def = null;
            ordinal = -1;
            return false;
        }

        /// <summary>
        /// A main button's spoken name: its own label, or its defName split into words
        /// for a modded def that ships no label because the button never draws a caption.
        /// </summary>
        internal static string LabelOf(MainButtonDef def)
        {
            string label = def.LabelCap.ToString();
            return string.IsNullOrWhiteSpace(label)
                ? GenText.SplitCamelCase(def.defName).Replace("_", " ").CapitalizeFirst().TrimStart()
                : label;
        }

        /// <summary>
        /// What the button says: its own label, the key vanilla opens it with,
        /// the bar it fills (research progress is the one worker that draws one)
        /// and the description its tooltip shows.
        /// </summary>
        internal static ElementDescription Describe(MainButtonDef def)
        {
            var description = new ElementDescription();
            if (def == null)
            {
                return description;
            }
            description.Label = LabelOf(def);
            description.Role = ElementRole.Button;
            string hotkey = ReachableHotkeyLabel(def);
            if (hotkey != null)
            {
                description.Hotkey = hotkey;
            }
            // A broken modded worker must not cost the button its label.
            try
            {
                MainButtonWorker worker = def.Worker;
                if (worker != null)
                {
                    description.Disabled = worker.Disabled;
                    float bar = worker.ButtonBarPercent;
                    if (bar > 0f)
                    {
                        description.Value = bar.ToStringPercent();
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Main button describe error", ex);
            }
            description.Extras = def.description;
            return description;
        }

        /// <summary>
        /// Hover describes the button under the pointer on every repaint, and the reachability walk
        /// over the focus stack is the costly part, so it is computed once per dwell.
        /// </summary>
        private static string ReachableHotkeyLabel(MainButtonDef def)
        {
            int frame = Time.frameCount;
            if (def != hotkeyDef || frame - hotkeyFrame > 1)
            {
                hotkeyDef = def;
                hotkeyLabel = VanillaBindings.ReachableHotkeyLabel(def.hotKey);
            }
            hotkeyFrame = frame;
            return hotkeyLabel;
        }
    }

    /// <summary>
    /// Feeds <see cref="MainButtonRectRegistry"/> from vanilla's own per-button
    /// draw call and evaluates hover once the whole bar has drawn.
    /// </summary>
    [HarmonyPatch(typeof(MainButtonsRoot), nameof(MainButtonsRoot.MainButtonsOnGUI))]
    internal static class MainButtonBarHoverPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static void Prefix()
        {
            try
            {
                MainButtonRectRegistry.BeginFrame();
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Main button bar hover error", ex);
            }
        }

        /// <summary>
        /// A finalizer, not a postfix: the recording window must close even when
        /// a modded worker's draw throws, or the next frame's hit test would run
        /// against half a bar.
        /// </summary>
        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception)
        {
            try
            {
                if (__exception == null)
                {
                    HoverSpeech.EvaluateMainButtonBar();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Main button bar hover error", ex);
            }
            finally
            {
                MainButtonRectRegistry.EndFrame();
            }
            return __exception;
        }
    }

    /// <summary>
    /// The per-button half. <see cref="MainButtonWorker.DoButton"/> is virtual
    /// and a mod's worker may override it without calling base, which would
    /// escape a patch on the base type (the CLAUDE.md inherited-method rule), so
    /// every subclass that DECLARES the method is patched, found by walking the
    /// type graph rather than by hand-listing.
    /// </summary>
    [HarmonyPatch]
    internal static class MainButtonRectCapturePatch
    {
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            // Routed through SafeTypeSweep for the same reason the gizmo rect
            // capture is (see its header): a single poisoned mod type can throw
            // out of the assignability test or the method lookup, not just out of
            // the initial enumeration.
            foreach (Type type in SafeTypeSweep.AssignableFromSafe(typeof(MainButtonWorker),
                (_, ex) => ModLogger.LimitedError("Main button rect capture error", ex)))
            {
                if (!SafeTypeSweep.TryEvaluate(type,
                        t => AccessTools.DeclaredMethod(t, "DoButton", new[] { typeof(Rect) }),
                        out MethodInfo declared,
                        (_, ex) => ModLogger.LimitedError("Main button rect capture error", ex)))
                {
                    continue;
                }
                if (declared != null && !declared.IsAbstract && !declared.ContainsGenericParameters)
                {
                    yield return declared;
                }
            }
        }

        // Positional binding: an override is free to rename the parameter, and
        // Harmony's by-name binding throws at patch time when it does.
        [HarmonyPrefix]
        internal static void Prefix(MainButtonWorker __instance, [HarmonyArgument(0)] Rect rect)
        {
            try
            {
                MainButtonRectRegistry.Record(__instance != null ? __instance.def : null, rect);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Main button rect capture error", ex);
            }
        }
    }
}
