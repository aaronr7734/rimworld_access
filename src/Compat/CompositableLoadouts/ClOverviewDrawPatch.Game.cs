using System;
using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Records where the overview draws its toolbar, rows, names, chips and stats panel, so the
    /// focus ring can follow the keyboard cursor. Rows scrolled out of view are never drawn, so a
    /// pass also scrolls the table to the cursor's row before the scroll view begins.
    /// </summary>
    internal static class ClOverviewDrawPatch
    {
        private static ClOverviewScope drawing;

        public static void Install(Harmony harmony)
        {
            Patch(harmony, ClCompat.Overview.DoWindowContentsMethod, nameof(WindowPrefix), nameof(WindowFinalizer), finalizer: true);
            Patch(harmony, ClCompat.Overview.DrawToolbarMethod, nameof(ToolbarPrefix), null);
            Patch(harmony, ClCompat.Overview.DrawRowMethod, null, nameof(RowPostfix));
            Patch(harmony, ClCompat.Overview.DrawNameMethod, null, nameof(NamePostfix));
            Patch(harmony, ClCompat.Overview.DrawChipMethod, null, nameof(ChipPostfix));
            Patch(harmony, ClCompat.Overview.StatsPanelDrawMethod, nameof(StatsPrefix), null);
        }

        private static void Patch(Harmony harmony, MethodInfo target, string prefix, string after, bool finalizer = false)
        {
            if (target == null)
            {
                return;
            }
            HarmonyMethod before = prefix != null ? new HarmonyMethod(typeof(ClOverviewDrawPatch), prefix) : null;
            HarmonyMethod tail = after != null ? new HarmonyMethod(typeof(ClOverviewDrawPatch), after) : null;
            if (finalizer)
            {
                harmony.Patch(target, prefix: before, finalizer: tail);
            }
            else
            {
                harmony.Patch(target, prefix: before, postfix: tail);
            }
        }

        public static void WindowPrefix(Window __instance)
        {
            drawing = ScreenScopeDrawPatch.For(__instance) as ClOverviewScope;
            drawing?.BeginPass();
        }

        public static Exception WindowFinalizer(Exception __exception)
        {
            drawing = null;
            return __exception;
        }

        public static void ToolbarPrefix(Rect rect)
        {
            drawing?.RecordToolbar(GuiSpace.VisibleScreenRect(rect));
        }

        public static void RowPostfix(Rect rect, Pawn pawn)
        {
            drawing?.RecordRow(pawn, rect, GuiSpace.VisibleScreenRect(rect), GuiSpace.CurrentClip().VisibleRect);
        }

        public static void NamePostfix(Rect rect, Pawn pawn)
        {
            drawing?.RecordName(pawn, GuiSpace.VisibleScreenRect(rect));
        }

        public static void ChipPostfix(Rect rect, object[] __args)
        {
            drawing?.RecordChip(__args[2], GuiSpace.VisibleScreenRect(rect));
        }

        public static void StatsPrefix(object __instance, Rect rect)
        {
            drawing?.RecordStats(__instance, GuiSpace.VisibleScreenRect(rect));
        }
    }
}
