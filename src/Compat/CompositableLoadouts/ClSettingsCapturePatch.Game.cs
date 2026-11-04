using System;
using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Feeds the generic reader the two things the mod's settings page draws outside any vanilla
    /// widget: the inter-game panel's tag name box, a raw GUI.TextField behind the mod's own
    /// <c>GUIUtility.InputField</c>, recorded and overridden like a Widgets.TextField so the shared
    /// edit session types into it live; and a section heading at the top of each of the panel's two
    /// tag lists, whose captions are drawn side by side before either list.
    /// </summary>
    internal static class ClSettingsCapturePatch
    {
        // InputField serves every mod window; only the settings panel's box is the reader's.
        private static int panelDepth;

        public static void Install(Harmony harmony)
        {
            MethodInfo draw = ClCompat.Settings.InterGameDraw;
            MethodInfo input = ClCompat.Settings.InputField;
            MethodInfo list = ClCompat.Settings.TagSummaryView;
            if (draw == null || input == null || list == null)
            {
                return;
            }
            harmony.Patch(draw,
                prefix: new HarmonyMethod(typeof(ClSettingsCapturePatch), nameof(DrawPrefix)),
                finalizer: new HarmonyMethod(typeof(ClSettingsCapturePatch), nameof(DrawFinalizer)));
            harmony.Patch(input,
                prefix: new HarmonyMethod(typeof(ClSettingsCapturePatch), nameof(InputPrefix)),
                postfix: new HarmonyMethod(typeof(ClSettingsCapturePatch), nameof(InputPostfix)));
            harmony.Patch(list, prefix: new HarmonyMethod(typeof(ClSettingsCapturePatch), nameof(ListPrefix)));
        }

        public static void DrawPrefix()
        {
            panelDepth++;
        }

        public static Exception DrawFinalizer(Exception __exception)
        {
            panelDepth = Math.Max(0, panelDepth - 1);
            return __exception;
        }

        public static void InputPrefix(Rect rect, ref string buff, out int __state)
        {
            __state = panelDepth > 0 ? WidgetCapture.RecordTextField(rect, buff, multiLine: false) : -1;
        }

        public static void InputPostfix(int __state, ref string buff)
        {
            if (__state >= 0)
            {
                WidgetCapture.MaybeOverrideText(__state, ref buff);
            }
        }

        public static void ListPrefix(ref Rect rect, bool load)
        {
            string caption = CompatText.ModText(load ? "CompositableLoadouts.LoadedTags" : "CompositableLoadouts.SavedTags");
            // The caption line the panel popped off this column; RecordLabel tells a heading by
            // the font, and nothing is drawn here.
            Rect captionRect = new Rect(rect.x, rect.y - Text.LineHeight, rect.width, Text.LineHeight);
            GameFont font = Text.Font;
            Text.Font = GameFont.Medium;
            try
            {
                WidgetCapture.RecordLabel(captionRect, caption);
            }
            finally
            {
                Text.Font = font;
            }
        }
    }
}
