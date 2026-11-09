using System;
using HarmonyLib;
using LudeonTK;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    // -----------------------------------------------------------------------
    // Harmony taps for LudeonTK.DevGUI, the dev tools' own widget primitives.
    //
    // The dev windows draw NOTHING through Verse.Widgets: DevGUI is a parallel
    // set of primitives over raw GUI calls (decompiled LudeonTK/DevGUI.cs), so
    // every Widgets tap in WidgetCapturePatches misses them entirely and the
    // debug log's message rows, the tweak-values rows and the debug inspector's
    // dump all captured as nothing at all — the window read as empty to hover
    // and to every reader that works off the capture stream.
    //
    // One tap per primitive, feeding the SAME record calls the Widgets twins
    // feed, so a dev window's rows arrive in the stream indistinguishable from
    // any other window's and the whole reader stack works on them unchanged.
    // Cheap when nothing is listening: every record call returns immediately
    // unless a capture pass is open.
    // -----------------------------------------------------------------------

    [HarmonyPatch(typeof(DevGUI), nameof(DevGUI.Label))]
    internal static class DevGuiCaptureLabelPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, string label)
        {
            try
            {
                WidgetCapture.RecordLabel(rect, label);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("DevGUI label capture error", ex);
            }
        }
    }

    [HarmonyPatch(typeof(DevGUI), nameof(DevGUI.ButtonText))]
    internal static class DevGuiCaptureButtonTextPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, string label, out int __state)
        {
            __state = -1;
            try
            {
                __state = WidgetCapture.RecordButton(rect, label, disabled: false);
                WidgetCapture.EnterSelfCaptioned(label);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("DevGUI button capture error", ex);
            }
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, ref bool __result)
        {
            try
            {
                WidgetCapture.ExitSelfCaptioned();
                WidgetCapture.MaybeForceActivate(__state, ref __result);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("DevGUI button capture error", ex);
            }
        }
    }

    /// <summary>
    /// The dev tools' clickable row: <c>DevGUI.Label</c> paints the text and this paints the hit
    /// area over it (decompiled LudeonTK/EditWindow_Log.cs:242-258 is the shape — repeat count,
    /// invisible button, then the message text). Recording it as an invisible button is what lets
    /// the capture layer's own fusion pass marry it to the label that names it, exactly as it does
    /// for <c>Widgets.ButtonInvisible</c>.
    /// </summary>
    [HarmonyPatch(typeof(DevGUI), nameof(DevGUI.ButtonInvisible))]
    internal static class DevGuiCaptureButtonInvisiblePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect butRect, out int __state)
        {
            __state = -1;
            try
            {
                __state = WidgetCapture.RecordInvisibleButton(butRect);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("DevGUI button capture error", ex);
            }
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, ref bool __result)
        {
            try
            {
                WidgetCapture.MaybeForceActivate(__state, ref __result);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("DevGUI button capture error", ex);
            }
        }
    }

    [HarmonyPatch]
    internal static class DevGuiCaptureCheckboxLabeledPatch
    {
        /// <summary>TargetMethod, not the attribute's Type[] form: a by-ref parameter type is not a compile-time constant (CS0182), the same reason the Widgets twin needs it.</summary>
        static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(DevGUI), nameof(DevGUI.CheckboxLabeled),
                new Type[] { typeof(Rect), typeof(string), typeof(bool).MakeByRefType() });
        }

        [HarmonyPrefix]
        public static void Prefix(Rect rect, string label, ref bool checkOn)
        {
            try
            {
                WidgetCapture.RecordAndMaybeToggleCheckbox(rect, label, ref checkOn, disabled: false);
                WidgetCapture.EnterSelfCaptioned(label);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("DevGUI checkbox capture error", ex);
            }
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitSelfCaptioned();
        }
    }

    [HarmonyPatch]
    internal static class DevGuiCaptureTextFieldPatch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(DevGUI), nameof(DevGUI.TextField),
                new Type[] { typeof(Rect), typeof(string) });
        }

        [HarmonyPrefix]
        public static void Prefix(Rect rect, string text, out int __state)
        {
            __state = -1;
            try
            {
                __state = WidgetCapture.RecordTextField(rect, text, multiLine: false);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("DevGUI text field capture error", ex);
            }
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, ref string __result)
        {
            try
            {
                WidgetCapture.MaybeOverrideText(__state, ref __result);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("DevGUI text field capture error", ex);
            }
        }
    }

    /// <summary>
    /// The dev scroll views, so a captured dev row carries the same clip and scroll geometry a
    /// Widgets row does — without it every row inside a scrolled dev list reports the rect it was
    /// laid out at rather than the one it is drawn at, and hover hit-tests the wrong place.
    /// </summary>
    [HarmonyPatch]
    internal static class DevGuiCaptureScrollViewPatch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(DevGUI), nameof(DevGUI.BeginScrollView),
                new Type[] { typeof(Rect), typeof(Vector2).MakeByRefType(), typeof(Rect), typeof(bool) });
        }

        [HarmonyPrefix]
        public static void Prefix(Rect outRect, ref Vector2 scrollPosition, Rect viewRect)
        {
            try
            {
                WidgetCapture.RecordScrollContainerBegin(outRect, ref scrollPosition, viewRect);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("DevGUI scroll capture error", ex);
            }
            WidgetCapture.EnterWrappedScrollView();
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            WidgetCapture.ExitWrappedScrollView();
        }
    }

    [HarmonyPatch(typeof(DevGUI), nameof(DevGUI.EndScrollView))]
    internal static class DevGuiCaptureEndScrollViewPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            WidgetCapture.EnterWrappedScrollView();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                WidgetCapture.RecordScrollContainerEnd();
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("DevGUI scroll capture error", ex);
            }
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            WidgetCapture.ExitWrappedScrollView();
        }
    }
}
