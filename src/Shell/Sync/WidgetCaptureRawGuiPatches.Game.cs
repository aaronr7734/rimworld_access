using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LudeonTK;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// A mod's raw GUI.TextArea records the row Widgets.TextArea would, at the outermost of the
    /// public overloads (see <see cref="RawGuiOverloads"/>). Widgets.TextArea and
    /// DevGUI.TextAreaScrollable are bracketed so they never record twice.
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureRawTextAreaPatch
    {
        private static int depth;

        static IEnumerable<MethodBase> TargetMethods()
        {
            return RawGuiOverloads.Of("TextArea", typeof(Rect), typeof(string));
        }

        [HarmonyPrefix]
        public static void Prefix(Rect position, string text, out int __state)
        {
            __state = depth++ == 0 && !WidgetCapture.InWrappedTextArea ? WidgetCapture.RecordTextField(position, text, multiLine: true) : -1;
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, ref string __result)
        {
            WidgetCapture.MaybeOverrideText(__state, ref __result);
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            depth--;
        }
    }

    [HarmonyPatch]
    internal static class WidgetCaptureDevTextAreaBracketPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(DevGUI), "TextAreaScrollable");
        }

        [HarmonyPrefix]
        public static void Prefix()
        {
            WidgetCapture.EnterWrappedTextArea();
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            WidgetCapture.ExitWrappedTextArea();
        }
    }

    /// <summary>
    /// A mod's raw captioned GUI.Button records a Button row whose activation forces its return,
    /// at the outermost of the public string and GUIContent overloads (see
    /// <see cref="RawGuiOverloads"/>). Vanilla only calls it caption-less (ButtonInvisible,
    /// DevGUI's hit area, icon buttons), so an empty caption is never recorded.
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureRawButtonPatch
    {
        // Shared with the GUIContent overloads, which can forward into the string ones.
        internal static int Depth;

        static IEnumerable<MethodBase> TargetMethods()
        {
            return RawGuiOverloads.Of("Button", typeof(Rect), typeof(string));
        }

        [HarmonyPrefix]
        public static void Prefix(Rect position, string text, out int __state)
        {
            __state = Record(position, text);
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, ref bool __result)
        {
            WidgetCapture.MaybeForceActivate(__state, ref __result);
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            Depth--;
        }

        internal static int Record(Rect position, string text)
        {
            return Depth++ == 0 && !string.IsNullOrEmpty(text) ? WidgetCapture.RecordButton(position, text, !GUI.enabled) : -1;
        }
    }

    [HarmonyPatch]
    internal static class WidgetCaptureRawContentButtonPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            return RawGuiOverloads.Of("Button", typeof(Rect), typeof(GUIContent));
        }

        [HarmonyPrefix]
        public static void Prefix(Rect position, GUIContent content, out int __state)
        {
            __state = WidgetCaptureRawButtonPatch.Record(position, content?.text);
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, ref bool __result)
        {
            WidgetCapture.MaybeForceActivate(__state, ref __result);
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            WidgetCaptureRawButtonPatch.Depth--;
        }
    }

    /// <summary>
    /// A mod's raw GUI.Label records the read-only row Widgets.Label would, at the outermost of the
    /// public string and GUIContent overloads (see <see cref="RawGuiOverloads"/>). Both
    /// Widgets.Label overloads and DevGUI.Label are bracketed, and the window title is skipped.
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureRawLabelPatch
    {
        // Shared with the GUIContent overloads, which can forward into the string ones.
        internal static int Depth;

        static IEnumerable<MethodBase> TargetMethods()
        {
            return RawGuiOverloads.Of("Label", typeof(Rect), typeof(string));
        }

        [HarmonyPrefix]
        public static void Prefix(Rect position, string text)
        {
            Record(position, text);
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            Depth--;
        }

        internal static void Record(Rect position, string text)
        {
            if (Depth++ == 0 && WidgetCapture.RawLabelRecordable(text))
            {
                WidgetCapture.RecordLabel(position, text);
            }
        }
    }

    [HarmonyPatch]
    internal static class WidgetCaptureRawContentLabelPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            return RawGuiOverloads.Of("Label", typeof(Rect), typeof(GUIContent));
        }

        [HarmonyPrefix]
        public static void Prefix(Rect position, GUIContent content)
        {
            WidgetCaptureRawLabelPatch.Record(position, content?.text);
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            WidgetCaptureRawLabelPatch.Depth--;
        }
    }

    [HarmonyPatch(typeof(Widgets), "Label", new[] { typeof(Rect), typeof(GUIContent) })]
    internal static class WidgetCaptureContentLabelBracketPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            WidgetCapture.EnterWrappedLabel();
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            WidgetCapture.ExitWrappedLabel();
        }
    }

    [HarmonyPatch(typeof(DevGUI), nameof(DevGUI.Label))]
    internal static class WidgetCaptureDevLabelBracketPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            WidgetCapture.EnterWrappedLabel();
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            WidgetCapture.ExitWrappedLabel();
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.DrawHighlightSelected))]
    internal static class WidgetCaptureSelectedHighlightPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect rect)
        {
            WidgetCapture.RecordSelectedHighlight(rect);
        }
    }
}
