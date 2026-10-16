using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection-only surface for Vanilla Expanded Framework's outpost picker,
    /// <c>Outposts.Dialog_CreateCamp</c>, opened by a caravan's Create outpost gizmo when Vanilla
    /// Outposts Expanded supplies outpost defs. Reads the dialog's own per-outpost verdicts
    /// (<c>validity</c>: blocked reason, requirements tooltip) in the order the dialog draws them
    /// (<c>OutpostsMod.Outposts</c>). <see cref="Shell.VefCreateOutpostScope"/> is the sole consumer.
    /// </summary>
    internal static class VefCreateOutpostCompat
    {
        private static readonly Type dialogType;
        private static readonly FieldInfo validityField;
        private static readonly FieldInfo outpostsField;
        private static readonly bool ready;

        public static bool Ready => ready;
        public static Type DialogType => dialogType;

        static VefCreateOutpostCompat()
        {
            var surface = new ReflectionSurface("VefCreateOutpostCompat");
            dialogType = surface.Type("Outposts.Dialog_CreateCamp");
            Type modType = surface.Type("Outposts.OutpostsMod");
            validityField = surface.Field(dialogType, "validity");
            outpostsField = surface.Field(modType, "Outposts");
            ready = surface.Ready;
        }

        internal readonly struct OutpostEntry
        {
            public readonly WorldObjectDef Def;
            public readonly string BlockedReason;
            public readonly string Requirements;

            public OutpostEntry(WorldObjectDef def, string blockedReason, string requirements)
            {
                Def = def;
                BlockedReason = blockedReason;
                Requirements = requirements;
            }
        }

        /// <summary>Every outpost the dialog lists, in its draw order, with the dialog's own verdicts.</summary>
        public static List<OutpostEntry> Entries(Window dialog)
        {
            var result = new List<OutpostEntry>();
            if (!ready)
                return result;
            try
            {
                var validity = validityField.GetValue(dialog) as IDictionary;
                if (!(outpostsField.GetValue(null) is IList outposts) || validity == null)
                    return result;
                foreach (object o in outposts)
                {
                    if (!(o is WorldObjectDef def))
                        continue;
                    string reason = null;
                    string requirements = null;
                    if (validity[def] is Pair<string, string> verdict)
                    {
                        reason = verdict.First;
                        requirements = verdict.Second;
                    }
                    result.Add(new OutpostEntry(def, reason, requirements));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefCreateOutpostCompat.Entries failed: {ex.Message}");
            }
            return result;
        }
    }
}
