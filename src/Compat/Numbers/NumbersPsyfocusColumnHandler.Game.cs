using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Numbers' psyfocus bar: the current fill and the target marker the bar draws, as the psyfocus gizmo phrases them.</summary>
    internal sealed class NumbersPsyfocusColumnHandler : PawnColumnHandler
    {
        public static void Register()
        {
            CompatRegistration.PawnColumnHandler("Numbers.PawnColumnWorker_Psyfocus", _ => new NumbersPsyfocusColumnHandler());
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            if (!pawn.HasPsylink || pawn.psychicEntropy == null)
            {
                return "";
            }
            return "RimWorldAccess.Compat.Numbers.LevelWithTarget".Loc(
                pawn.psychicEntropy.CurrentPsyfocus.ToStringPercent(),
                pawn.psychicEntropy.TargetPsyfocus.ToStringPercent()).ToString();
        }
    }
}
