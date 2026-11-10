using RimWorld;
using RimWorldAccess.Shell;
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
        public static void Register()
        {
            CompatRegistration.PawnColumnHandler("Numbers.PawnColumnWorker_InventoryDropAll", _ => new NumbersDropAllColumnHandler());
        }

        private static bool IconShown(PawnColumnDef def, Pawn pawn)
        {
            return pawn.inventory != null && IconColumnHandler.GetIconFor(def, pawn) != null;
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
            IconColumnHandler.ClickedIcon(def, pawn);
            TolkHelper.SpeakData("RimWorldAccess.Compat.Numbers.InventoryDropped".Loc(pawn.LabelShortCap).ToString());
            return PawnColumnActivation.OpenedUI;
        }

        public override bool ActivatesCellWithoutTable(PawnColumnDef def)
        {
            return true;
        }
    }
}
