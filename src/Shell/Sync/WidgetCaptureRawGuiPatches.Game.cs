using System.Reflection;
using HarmonyLib;
using LudeonTK;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// A mod's raw GUI.TextArea records the row Widgets.TextArea would. Every overload funnels into
    /// this one; Widgets.TextArea and DevGUI.TextAreaScrollable are bracketed so they never record twice.
    /// </summary>
    [HarmonyPatch(typeof(GUI), "TextArea", new[] { typeof(Rect), typeof(string), typeof(int), typeof(GUIStyle) })]
    internal static class WidgetCaptureRawTextAreaPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect position, string text, out int __state)
        {
            __state = WidgetCapture.InWrappedTextArea ? -1 : WidgetCapture.RecordTextField(position, text, multiLine: true);
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, ref string __result)
        {
            WidgetCapture.MaybeOverrideText(__state, ref __result);
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
    /// A mod's raw captioned GUI.Button records a Button row whose activation forces its return.
    /// Every overload funnels into ButtonImpl. Vanilla only reaches it caption-less (ButtonInvisible,
    /// DevGUI's hit area, icon buttons), so an empty caption is never recorded.
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureRawButtonPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(GUI), "ButtonImpl");
        }

        [HarmonyPrefix]
        public static void Prefix(Rect position, GUIContent content, out int __state)
        {
            string text = content?.text;
            __state = string.IsNullOrEmpty(text) ? -1 : WidgetCapture.RecordButton(position, text, !GUI.enabled);
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, ref bool __result)
        {
            WidgetCapture.MaybeForceActivate(__state, ref __result);
        }
    }

    /// <summary>
    /// A mod's raw GUI.Label records the read-only row Widgets.Label would. Both Widgets.Label
    /// overloads and DevGUI.Label are bracketed, and the window title is skipped.
    /// </summary>
    [HarmonyPatch(typeof(GUI), "Label", new[] { typeof(Rect), typeof(string), typeof(GUIStyle) })]
    internal static class WidgetCaptureRawLabelPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect position, string text)
        {
            if (WidgetCapture.RawLabelRecordable(text))
            {
                WidgetCapture.RecordLabel(position, text);
            }
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
