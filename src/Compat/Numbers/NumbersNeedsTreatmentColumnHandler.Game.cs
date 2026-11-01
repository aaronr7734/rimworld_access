using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Numbers' treatment icon: the needs-tending icon or the tended-well icon, decided by
    /// <c>HasHediffsNeedingTendByPlayer</c> as the worker decides it; the icon's tooltip lists the
    /// tendable conditions.
    /// </summary>
    internal sealed class NumbersNeedsTreatmentColumnHandler : PawnColumnHandler
    {
        private static readonly MethodInfo getIconTip = AccessTools.Method(typeof(PawnColumnWorker_Icon), "GetIconTip");

        public static void Register()
        {
            var surface = new ReflectionSurface("NumbersNeedsTreatmentColumnHandler");
            Type workerType = surface.Type("Numbers.PawnColumnWorker_NeedsTreatment");
            if (surface.Ready)
            {
                PawnColumnHandlerRegistry.Register(workerType, new NumbersNeedsTreatmentColumnHandler());
            }
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            if (pawn.health == null)
            {
                return "";
            }
            return pawn.health.HasHediffsNeedingTendByPlayer()
                ? "NeedsTendingNow".Translate().ToString()
                : "RimWorldAccess.Compat.Numbers.NoTendingNeeded".Loc().ToString();
        }

        public override string CellTip(PawnColumnDef def, Pawn pawn)
        {
            if (pawn.health == null)
            {
                return null;
            }
            string tip = getIconTip.Invoke(def.Worker, new object[] { pawn }) as string;
            return string.IsNullOrEmpty(tip) ? null : tip.StripTags();
        }
    }
}
