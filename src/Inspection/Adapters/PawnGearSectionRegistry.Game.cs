using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Sections other mods draw into the Gear tab between the overall armor block and the
    /// equipment list, the spot their FillTab transpilers insert at. Compat modules register a
    /// builder from Activate; each builder decides for itself whether the pawn gets a section.
    /// </summary>
    internal static class PawnGearSectionRegistry
    {
        private static readonly List<Action<InspectionTreeItem, Pawn, InspectionMode>> builders =
            new List<Action<InspectionTreeItem, Pawn, InspectionMode>>();

        public static void Register(Action<InspectionTreeItem, Pawn, InspectionMode> builder)
        {
            builders.Add(builder);
        }

        internal static void BuildAll(InspectionTreeItem gearCategory, Pawn pawn, InspectionMode mode)
        {
            foreach (Action<InspectionTreeItem, Pawn, InspectionMode> builder in builders)
            {
                InspectNodeFactory.GuardedBuild("Gear tab section", () => builder(gearCategory, pawn, mode));
            }
        }
    }
}
