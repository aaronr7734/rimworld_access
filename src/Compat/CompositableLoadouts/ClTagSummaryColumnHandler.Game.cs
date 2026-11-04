using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>The "loadout overview" column (<c>PawnColumnWorker_TagSummary</c>): the pawn's tag names as DoCell joins them.</summary>
    internal sealed class ClTagSummaryColumnHandler : PawnColumnHandler
    {
        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            if (!ClCompat.IsValidLoadoutHolder(pawn))
            {
                return "";
            }
            return string.Join(", ", ClCompat.Elements(ClCompat.LoadoutOf(pawn))
                .Select(e => ClCompat.TagName(ClCompat.ElementTag(e))));
        }
    }
}
