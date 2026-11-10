using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The guest table's unlabelled "..." column (<c>PawnColumnWorker_ButtonMore</c>), whose button
    /// jumps to the guest and opens its Guest tab. The glyph names nothing, so the column and cell
    /// carry a spoken name; Enter still clicks the captured button itself.
    /// </summary>
    internal sealed class HospitalityMoreColumnHandler : PawnColumnHandler
    {
        public static void Register()
        {
            CompatRegistration.PawnColumnHandler("Hospitality.MainTab.PawnColumnWorker_ButtonMore",
                _ => new HospitalityMoreColumnHandler());
        }

        public override string HeaderLabel(PawnColumnDef def)
        {
            return "RimWorldAccess.Compat.Hospitality.ShowGuestTab".Translate();
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            return HeaderLabel(def);
        }

        public override bool SuppressCapturedExtras(PawnColumnDef def)
        {
            return false;
        }
    }
}
