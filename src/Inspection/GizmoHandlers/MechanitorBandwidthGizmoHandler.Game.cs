using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Announcement handler for MechanitorBandwidthGizmo (Biotech). Ports the
    /// legacy label case and GetMechanitorBandwidthStatus, and adds the vanilla
    /// hover tooltip as the description facet by replicating the tooltip
    /// construction from MechanitorBandwidthGizmo.GizmoOnGUI verbatim (the
    /// tooltip only ever exists render-side via TooltipHandler.TipRegion, so it
    /// must be rebuilt from the tracker; see the lazy-populate rule). All
    /// tracker members used here are public — only the gizmo's `tracker` field
    /// itself is private, read via one cached FieldInfo and then typed.
    /// </summary>
    internal sealed class MechanitorBandwidthGizmoHandler : GizmoHandlerBase
    {
        /// <summary>
        /// MechanitorBandwidthGizmo.tracker (private Pawn_MechanitorTracker).
        /// </summary>
        private static readonly FieldInfo TrackerField =
            typeof(MechanitorBandwidthGizmo).GetField("tracker",
                BindingFlags.Instance | BindingFlags.NonPublic);

        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;
            if (!(gizmo is MechanitorBandwidthGizmo))
                return false;

            label = "RimWorldAccess.Inspection.Gizmo.Type.MechanitorBandwidth".Translate();
            return true;
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            Pawn_MechanitorTracker tracker = GetTracker(gizmo);
            if (tracker == null)
                return false;

            status = "RimWorldAccess.Inspection.Gizmo.Status.UsedTotal"
                .Translate(tracker.UsedBandwidth, tracker.TotalBandwidth);
            return true;
        }

        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;
            Pawn_MechanitorTracker tracker = GetTracker(gizmo);
            if (tracker == null)
                return false;

            try
            {
                // Mirror of the tooltip built in MechanitorBandwidthGizmo.GizmoOnGUI.
                // Differences are speech-only: the Colorize() on the "Bandwidth"
                // heading is dropped (purely visual), and the finished text is
                // stripped of tags and flattened (newlines never reach speech).
                string text = tracker.UsedBandwidth.ToString("F0") + " / " + tracker.TotalBandwidth.ToString("F0");
                TaggedString taggedString = "Bandwidth".Translate() + ": " + text + "\n\n" + TooltipBody(tracker);
                description = GizmoTextUtility.FlattenNewlines(taggedString.Resolve().StripTags());
                return !string.IsNullOrEmpty(description);
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception building mechanitor bandwidth description: {ex.Message}");
                return false;
            }
        }

        internal static TaggedString TooltipBody(Pawn_MechanitorTracker tracker)
        {
            TaggedString taggedString = "BandwidthGizmoTip".Translate();
            List<Pawn> overseenPawns = tracker.OverseenPawns;
            int usedBandwidthFromSubjects = tracker.UsedBandwidthFromSubjects;
            if (usedBandwidthFromSubjects > 0 && overseenPawns != null)
            {
                taggedString += string.Concat("\n\n" + ("BandwidthUsage".Translate() + ": "),
                    usedBandwidthFromSubjects.ToString());
                taggedString += "\n\n" + MechGroups(overseenPawns, gestating: false).ToLineList(" - ");
            }

            int usedBandwidthFromGestation = tracker.UsedBandwidthFromGestation;
            if (usedBandwidthFromGestation > 0 && overseenPawns != null)
            {
                taggedString += string.Concat("\n\n" + "MechGestationBandwidthUsed".Translate() + ": ",
                    usedBandwidthFromGestation.ToString());
                taggedString += "\n\n" + MechGroups(overseenPawns, gestating: true).ToLineList(" - ");
            }
            return taggedString;
        }

        private static IEnumerable<string> MechGroups(List<Pawn> overseenPawns, bool gestating)
        {
            return from p in overseenPawns
                where p != null && p.IsGestating() == gestating
                group p by p.kindDef into p
                select string.Concat(p.Key.LabelCap + " x", p.Count().ToString(), " (+",
                    p.Sum((Pawn mech) => mech.GetStatValue(StatDefOf.BandwidthCost)).ToString(), ")");
        }

        /// <summary>
        /// Reads the gizmo's private tracker field and returns it typed, or null
        /// when the gizmo is not a MechanitorBandwidthGizmo or the field is
        /// missing/unset (each facet then defers to the legacy fallback).
        /// </summary>
        private static Pawn_MechanitorTracker GetTracker(Gizmo gizmo)
        {
            if (!(gizmo is MechanitorBandwidthGizmo) || TrackerField == null)
                return null;
            return TrackerField.GetValue(gizmo) as Pawn_MechanitorTracker;
        }
    }
}
