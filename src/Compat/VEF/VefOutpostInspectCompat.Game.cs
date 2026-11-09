using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld.Planet;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// World-map inspection for the outpost framework's <c>Outposts.Outpost</c>, whose pawns and
    /// stock live only in its world inspect tabs (WITab_Outpost_Health/Needs/Gear/Items), which the
    /// world-object tree cannot read. An outpost node expands into its inspect-pane lines, a Pawns
    /// section whose rows carry the same health, needs, mood and gear readouts the caravan
    /// inspect screen speaks, and an Items section of the stored stacks. A claimed object opens
    /// as a tree even alone on its tile, where an unclaimed one only speaks its description.
    /// </summary>
    internal static class VefOutpostInspectCompat
    {
        private static readonly Type outpostType;
        private static readonly PropertyInfo allPawnsProperty;
        private static readonly PropertyInfo thingsProperty;
        private static readonly bool ready;

        static VefOutpostInspectCompat()
        {
            var surface = new ReflectionSurface("VefOutpostInspectCompat");
            outpostType = surface.Type("Outposts.Outpost");
            allPawnsProperty = surface.Property(outpostType, "AllPawns");
            thingsProperty = surface.Property(outpostType, "Things");
            ready = surface.Ready;
        }

        public static void Register()
        {
            if (ready)
                WorldObjectSelectionState.RegisterChildrenBuilder(obj => outpostType.IsInstanceOfType(obj), BuildChildren);
        }

        private static void BuildChildren(InspectionTreeItem node, WorldObject outpost)
        {
            InspectNodeFactory.GuardedBuild("VefOutpostInspectCompat inspect string",
                () => InspectNodeFactory.DetailLines(node, outpost.GetInspectString()));
            InspectNodeFactory.GuardedBuild("VefOutpostInspectCompat pawns", () => BuildPawns(node, outpost));
            InspectNodeFactory.GuardedBuild("VefOutpostInspectCompat items", () => BuildItems(node, outpost));
        }

        private static void BuildPawns(InspectionTreeItem node, WorldObject outpost)
        {
            List<Pawn> pawns = ((IEnumerable<Pawn>)allPawnsProperty.GetValue(outpost)).ToList();
            InspectionTreeItem section = Group(node, "RimWorldAccess.Caravan.Inspect.CategoryPawns".Translate(pawns.Count), null);
            foreach (Pawn pawn in pawns.OrderBy(p => p.LabelShortCap))
            {
                string label = pawn.story?.TitleCap.NullOrEmpty() == false
                    ? (string)"RimWorldAccess.Compat.Vef.OutpostPawnWithTitle".Translate(pawn.LabelShortCap, pawn.story.TitleCap)
                    : pawn.LabelShortCap;
                InspectionTreeItem row = Group(section, label, pawn);
                if (pawn.health != null)
                    InspectNodeFactory.DetailLine(row, PawnInfoHelper.GetHealthInfo(pawn));
                if (pawn.needs?.mood != null)
                    InspectNodeFactory.DetailLine(row, PawnInfoHelper.GetMoodInfo(pawn));
                if (pawn.needs != null)
                    InspectNodeFactory.DetailLine(row, PawnInfoHelper.GetNeedsInfo(pawn));
                InspectNodeFactory.DetailLine(row, PawnInfoHelper.GetGearInfo(pawn));
            }
        }

        private static void BuildItems(InspectionTreeItem node, WorldObject outpost)
        {
            List<Thing> things = ((IEnumerable<Thing>)thingsProperty.GetValue(outpost)).ToList();
            if (things.Count == 0)
            {
                InspectNodeFactory.DetailLine(node, "RimWorldAccess.Caravan.Inspect.CategoryItemsEmpty".Translate());
                return;
            }
            InspectionTreeItem section = Group(node, "RimWorldAccess.Caravan.Inspect.CategoryItems".Translate(things.Sum(t => t.stackCount)), null);
            foreach (Thing thing in things.OrderBy(t => t.LabelCap.ToString()))
            {
                InspectNodeFactory.ItemRow(section, thing.LabelCap, thing).LinkedDef = thing.def;
            }
        }

        // Built eagerly: the world-object tree expands nodes by toggling, and treats any WorldObject
        // Data as a node still to build, so groups carry their pawn or nothing.
        private static InspectionTreeItem Group(InspectionTreeItem parent, string label, object data)
        {
            var item = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = label,
                Data = data,
                IndentLevel = parent.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false
            };
            InspectNodeFactory.Attach(parent, item);
            return item;
        }
    }
}
