using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Draws the focus ring of <see cref="CashRegisterShiftsScope"/> and <see cref="CashRegisterShiftScope"/>
    /// from inside the Shifts tab's own draw, which runs in the inspect pane's ImmediateWindow. The
    /// targets are chosen to stay out of the 1.7 JIT's inliner (mod assemblies are not
    /// JIT-patchable): DoHeader and DoCell loop, and DrawShift is large, unlike the tab's small add
    /// and discard helpers. Repaint only, and never in the capture harness's offscreen pass.
    /// </summary>
    internal static class CashRegisterShiftsRingPatch
    {
        internal static void Install(Harmony harmony, MethodInfo doHeader, MethodInfo doCell, MethodInfo drawShift)
        {
            Patch(harmony, doHeader, nameof(HeaderPostfix));
            Patch(harmony, doCell, nameof(CellPostfix));
            Patch(harmony, drawShift, nameof(ShiftPostfix));
        }

        private static void Patch(Harmony harmony, MethodInfo target, string postfix)
        {
            if (target == null)
            {
                ModLogger.Error($"CashRegister shifts ring: no target for {postfix}, that part of the ring is skipped.");
                return;
            }
            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(CashRegisterShiftsRingPatch), postfix));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"CashRegister shifts ring: patching {target.Name} failed: {ex.Message}");
            }
        }

        public static void HeaderPostfix(ref float curY)
        {
            try
            {
                CashRegisterShiftsScope scope = LiveScope() as CashRegisterShiftsScope;
                if (scope != null)
                {
                    scope.OnHeaderDrawn(curY);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("CashRegister shifts header ring", ex);
            }
        }

        public static void CellPostfix(Rect rect, object timetable)
        {
            try
            {
                FocusScope scope = LiveScope();
                if (scope is CashRegisterShiftsScope list)
                {
                    list.OnCellDrawn(rect, timetable);
                }
                else if (scope is CashRegisterShiftScope one)
                {
                    one.OnCellDrawn(rect, timetable);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("CashRegister shifts cell ring", ex);
            }
        }

        public static void ShiftPostfix(object shift, ref float curY)
        {
            try
            {
                CashRegisterShiftsScope scope = LiveScope() as CashRegisterShiftsScope;
                if (scope != null)
                {
                    scope.OnShiftDrawn(shift, curY);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("CashRegister shifts row ring", ex);
            }
        }

        private static FocusScope LiveScope()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint || GuiSpace.ClipIsOffscreen())
            {
                return null;
            }
            return FocusStack.Top;
        }
    }
}
