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
        public static void Register()
        {
            CompatRegistration.PawnColumnHandler("Numbers.PawnColumnWorker_NeedsTreatment", _ => new NumbersNeedsTreatmentColumnHandler());
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
            string tip = IconColumnHandler.GetIconTip(def, pawn);
            return string.IsNullOrEmpty(tip) ? null : tip.StripTags();
        }
    }
}
