using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Numbers' neural heat bar, read as the heat gizmo's own "value / maximum" readout.</summary>
    internal sealed class NumbersEntropyColumnHandler : PawnColumnHandler
    {
        public static void Register()
        {
            CompatRegistration.PawnColumnHandler("Numbers.PawnColumnWorker_Entropy", _ => new NumbersEntropyColumnHandler());
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            if (!pawn.HasPsylink || pawn.psychicEntropy == null)
            {
                return "";
            }
            return "RimWorldAccess.Compat.Numbers.ValueOfMaximum".Loc(
                pawn.psychicEntropy.EntropyValue.ToString("F0"),
                pawn.psychicEntropy.MaxEntropy.ToString("F0")).ToString();
        }
    }
}
