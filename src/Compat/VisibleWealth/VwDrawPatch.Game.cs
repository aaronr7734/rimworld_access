using System;
using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Records where the wealth breakdown draws its rows, chart and toolbar so the focus ring can
    /// follow the keyboard cursor, and scrolls the list to the cursor's row before the scroll
    /// view begins.
    /// </summary>
    internal static class VwDrawPatch
    {
        private static VisibleWealthScope drawing;

        public static void Install(Harmony harmony)
        {
            Patch(harmony, VwCompat.DoWindowContentsMethod, nameof(WindowPrefix), nameof(WindowFinalizer), finalizer: true);
            Patch(harmony, VwCompat.NodeDrawMethod, nameof(NodePrefix), nameof(NodePostfix));
            Patch(harmony, VwCompat.PieDrawMethod, null, nameof(PiePostfix));
        }

        private static void Patch(Harmony harmony, MethodInfo target, string prefix, string after, bool finalizer = false)
        {
            if (target == null)
            {
                return;
            }
            HarmonyMethod before = prefix != null ? new HarmonyMethod(typeof(VwDrawPatch), prefix) : null;
            HarmonyMethod tail = after != null ? new HarmonyMethod(typeof(VwDrawPatch), after) { priority = Priority.Last } : null;
            if (finalizer)
            {
                harmony.Patch(target, prefix: before, finalizer: tail);
            }
            else
            {
                harmony.Patch(target, prefix: before, postfix: tail);
            }
        }

        public static void WindowPrefix(Window __instance, Rect inRect)
        {
            drawing = ScreenScopeDrawPatch.For(__instance) as VisibleWealthScope;
            drawing?.BeginPass(inRect);
        }

        public static Exception WindowFinalizer(Exception __exception)
        {
            drawing?.EndPass();
            drawing = null;
            return __exception;
        }

        public static void NodePrefix(ref float y, out float __state)
        {
            __state = y;
        }

        public static void NodePostfix(object __instance, float width, ref float y, float __state)
        {
            // A node whose subtree misses the search draws nothing and leaves y alone.
            if (drawing != null && y > __state)
            {
                drawing.RecordRow(__instance, new Rect(0f, __state, width, VwCompat.RowHeight));
            }
        }

        public static void PiePostfix(Rect viewRect)
        {
            drawing?.RecordChart(GuiSpace.VisibleScreenRect(viewRect));
        }
    }
}
