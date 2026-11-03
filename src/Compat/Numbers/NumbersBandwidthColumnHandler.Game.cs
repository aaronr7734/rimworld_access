using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Numbers' mechanitor bandwidth blocks: used over total, then the cell tooltip one sentence per paragraph.</summary>
    internal sealed class NumbersBandwidthColumnHandler : PawnColumnHandler
    {
        public static void Register()
        {
            NumbersModule.RegisterWorker("Numbers.PawnColumnWorker_Bandwidth", new NumbersBandwidthColumnHandler());
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            Pawn_MechanitorTracker tracker = pawn.mechanitor;
            return tracker != null
                ? "RimWorldAccess.Inspection.Gizmo.Status.UsedTotal".Translate(tracker.UsedBandwidth, tracker.TotalBandwidth).ToString()
                : "";
        }

        public override string CellTip(PawnColumnDef def, Pawn pawn)
        {
            Pawn_MechanitorTracker tracker = pawn.mechanitor;
            return tracker != null
                ? GizmoTextUtility.FlattenNewlines(MechanitorBandwidthGizmoHandler.TooltipBody(tracker).Resolve().StripTags())
                : null;
        }
    }
}
