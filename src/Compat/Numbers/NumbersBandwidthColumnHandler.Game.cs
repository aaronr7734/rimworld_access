using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Numbers' mechanitor bandwidth blocks: used over total, with the tooltip its DoCell builds
    /// from <c>MechanitorBandwidthGizmo</c>'s parts, one sentence per tooltip paragraph.
    /// </summary>
    internal sealed class NumbersBandwidthColumnHandler : PawnColumnHandler
    {
        public static void Register()
        {
            var surface = new ReflectionSurface("NumbersBandwidthColumnHandler");
            Type workerType = surface.Type("Numbers.PawnColumnWorker_Bandwidth");
            if (surface.Ready)
            {
                PawnColumnHandlerRegistry.Register(workerType, new NumbersBandwidthColumnHandler());
            }
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            Pawn_MechanitorTracker tracker = pawn.mechanitor;
            if (tracker == null)
            {
                return "";
            }
            return "RimWorldAccess.Compat.Numbers.ValueOfMaximum".Loc(
                tracker.UsedBandwidth.ToString("F0"), tracker.TotalBandwidth.ToString("F0")).ToString();
        }

        public override string CellTip(PawnColumnDef def, Pawn pawn)
        {
            Pawn_MechanitorTracker tracker = pawn.mechanitor;
            if (tracker == null)
            {
                return null;
            }
            var parts = new List<string> { "BandwidthGizmoTip".Translate().Resolve() };
            if (tracker.UsedBandwidthFromSubjects > 0)
            {
                parts.Add("BandwidthUsage".Translate() + ": " + tracker.UsedBandwidthFromSubjects);
                parts.AddRange(MechGroups(tracker, gestating: false));
            }
            if (tracker.UsedBandwidthFromGestation > 0)
            {
                parts.Add("MechGestationBandwidthUsed".Translate() + ": " + tracker.UsedBandwidthFromGestation);
                parts.AddRange(MechGroups(tracker, gestating: true));
            }
            return CompatText.JoinSentences(parts).StripTags();
        }

        private static IEnumerable<string> MechGroups(Pawn_MechanitorTracker tracker, bool gestating)
        {
            return from p in tracker.OverseenPawns
                   where p.IsGestating() == gestating
                   group p by p.kindDef into g
                   select g.Key.LabelCap + " x" + g.Count() + " (+" + g.Sum(m => m.GetStatValue(StatDefOf.BandwidthCost)) + ")";
        }
    }
}
