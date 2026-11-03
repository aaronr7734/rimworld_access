using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Numbers' ideoligion plate (<c>IdeoUIUtility.DrawIdeoPlate</c>): the ideoligion name, the
    /// plate's certainty and former-ideoligion tooltip, and Enter opens the same ideoligion info.
    /// </summary>
    internal sealed class NumbersIdeologyColumnHandler : PawnColumnHandler
    {
        public static void Register()
        {
            NumbersModule.RegisterWorker("Numbers.PawnColumnWorker_Ideology", new NumbersIdeologyColumnHandler());
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            return pawn.Ideo != null ? pawn.Ideo.name.StripTags() : "";
        }

        public override string CellTip(PawnColumnDef def, Pawn pawn)
        {
            if (pawn.Ideo == null || pawn.ideo == null)
            {
                return null;
            }
            var parts = new List<string>
            {
                "Certainty".Translate().CapitalizeFirst() + ": " + pawn.ideo.Certainty.ToStringPercent(),
            };
            if (pawn.ideo.PreviousIdeos.Any())
            {
                parts.Add("Formerly".Translate().CapitalizeFirst() + ": " + string.Join(", ", pawn.ideo.PreviousIdeos.Select(x => x.name)));
            }
            return CompatText.JoinSentences(parts).StripTags();
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            if (pawn.Ideo == null)
            {
                return PawnColumnActivation.NotHandled;
            }
            IdeoUIUtility.OpenIdeoInfo(pawn.Ideo);
            return PawnColumnActivation.OpenedUI;
        }

        public override bool ActivatesCellWithoutTable(PawnColumnDef def)
        {
            return true;
        }
    }
}
