using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Numbers' drop-all icon. The icon shows only while the worker's own gate allows a drop, so
    /// the cell says the action is available; Enter runs the worker's ClickedIcon, which drops
    /// at once with no confirmation, exactly as a click does, then reports it.
    /// </summary>
    internal sealed class NumbersDropAllColumnHandler : PawnColumnHandler
    {
        private static readonly MethodInfo getIconFor = AccessTools.Method(typeof(PawnColumnWorker_Icon), "GetIconFor");
        private static readonly MethodInfo clickedIcon = AccessTools.Method(typeof(PawnColumnWorker_Icon), "ClickedIcon");

        public static void Register()
        {
            var surface = new ReflectionSurface("NumbersDropAllColumnHandler");
            Type workerType = surface.Type("Numbers.PawnColumnWorker_InventoryDropAll");
            if (surface.Ready)
            {
                PawnColumnHandlerRegistry.Register(workerType, new NumbersDropAllColumnHandler());
            }
        }

        private static bool IconShown(PawnColumnDef def, Pawn pawn)
        {
            return pawn.inventory != null && getIconFor.Invoke(def.Worker, new object[] { pawn }) as Texture2D != null;
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            return IconShown(def, pawn) ? "RimWorldAccess.Compat.Numbers.DropAllAvailable".Loc().ToString() : "";
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            if (!IconShown(def, pawn))
            {
                return PawnColumnActivation.NotHandled;
            }
            clickedIcon.Invoke(def.Worker, new object[] { pawn });
            TolkHelper.SpeakData("RimWorldAccess.Compat.Numbers.InventoryDropped".Loc(pawn.LabelShortCap).ToString());
            return PawnColumnActivation.OpenedUI;
        }

        public override bool ActivatesCellWithoutTable(PawnColumnDef def)
        {
            return true;
        }
    }
}
