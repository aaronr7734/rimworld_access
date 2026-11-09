using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Inspection-tree adapter for Vanilla Expanded Framework's animal Genes tab
    /// (<c>VEF.AnimalGenes.ITab_AnimalGenes</c>). The tab is an icon grid whose gene descriptions
    /// sit behind Mouse.IsOver-gated tooltips the capture harness cannot see, so this reads the
    /// comp directly in the tab's own draw order: genes sorted by family order, then stability,
    /// lifespan and the feratype. Gene and feratype rows link their defs, the tab's own click
    /// target being each def's info card.
    /// </summary>
    internal sealed class VefAnimalGenesTabAdapter : InspectNodeAdapter
    {
        private readonly Type compType;
        private readonly MethodInfo thingForGenesMethod;
        private readonly MethodInfo totalStabilityMethod;
        private readonly MethodInfo feratypeDescMethod;
        private readonly FieldInfo genesField;
        private readonly FieldInfo feratypeField;
        private readonly FieldInfo activeField;
        private readonly PropertyInfo lifespanFactorProperty;
        private readonly PropertyInfo descriptionFullProperty;
        private readonly FieldInfo familyTagField;
        private readonly FieldInfo familyOrderField;
        private readonly FieldInfo feratypeFamilyField;
        private readonly bool ready;

        public override bool Ready => ready;

        public VefAnimalGenesTabAdapter(Type tabType)
        {
            var surface = new ReflectionSurface("VefAnimalGenesTabAdapter");
            surface.Supplied("VEF.AnimalGenes.ITab_AnimalGenes", tabType);
            compType = surface.Type("VEF.AnimalGenes.CompAnimalGenes");
            Type geneDefType = surface.Type("VEF.AnimalGenes.AnimalGeneDef");
            Type familyTagType = surface.Type("VEF.AnimalGenes.AnimalGeneFamilyTagDef");
            Type feratypeType = surface.Type("VEF.AnimalGenes.FeratypeDef");
            Type utilityType = surface.Type("VEF.AnimalGenes.AnimalGeneUtility");

            thingForGenesMethod = surface.Method(tabType, "ThingForGenes", new[] { typeof(Thing) });
            totalStabilityMethod = compType != null ? surface.Method(utilityType, "GetTotalStability", new[] { compType }) : null;
            feratypeDescMethod = feratypeType != null ? surface.Method(feratypeType, "FeratypeDescWithExtra", new[] { feratypeType }) : null;
            genesField = surface.Field(compType, "genes");
            feratypeField = surface.Field(compType, "feratype");
            activeField = surface.Field(compType, "active");
            lifespanFactorProperty = surface.Property(compType, "LifeSpanFactor");
            descriptionFullProperty = surface.Property(geneDefType, "DescriptionFull");
            familyTagField = surface.Field(geneDefType, "familyTag");
            familyOrderField = surface.Field(familyTagType, "order");
            feratypeFamilyField = surface.Field(feratypeType, "feratypeFamily");
            ready = surface.Ready;
        }

        public override string CategoryKey => "VEF Animal Genes";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override string DisplayName(InspectTabBase tab) => tab.labelKey.Translate();

        public override string CategoryDisplayName(object obj) => CompatText.ModText("VRE_TabAnimalGenes");

        public override bool CanExpand(object obj)
        {
            return Comp(obj) != null;
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            ThingComp comp = Comp(obj);
            if (comp == null)
                return;

            InspectNodeFactory.GuardedBuild("VefAnimalGenesTabAdapter genes", () => BuildGenes(categoryItem, comp));
            InspectNodeFactory.GuardedBuild("VefAnimalGenesTabAdapter summary", () => BuildSummary(categoryItem, comp));
        }

        /// <summary>The tab's own subject and visibility gate: an active comp with at least one gene.</summary>
        private ThingComp Comp(object obj)
        {
            if (!ready || !(obj is Thing thing))
                return null;
            try
            {
                var target = thingForGenesMethod.Invoke(null, new object[] { thing }) as ThingWithComps;
                ThingComp comp = target?.AllComps.FirstOrDefault(c => compType.IsInstanceOfType(c));
                if (comp == null || !(bool)activeField.GetValue(comp))
                    return null;
                return genesField.GetValue(comp) is IList genes && genes.Count > 0 ? comp : null;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("VefAnimalGenesTabAdapter.Comp", ex);
                return null;
            }
        }

        private void BuildGenes(InspectionTreeItem categoryItem, ThingComp comp)
        {
            InspectionTreeItem section = InspectNodeFactory.Section(categoryItem,
                CompatText.ModText("VRE_AnimalGenes"), comp, s => BuildGeneRows(s, comp));
            section.Detail = CompatText.Flatten(CompatText.ModText("VRE_AnimalGenesDesc"));
        }

        private void BuildGeneRows(InspectionTreeItem section, ThingComp comp)
        {
            IEnumerable<Def> ordered = ((IList)genesField.GetValue(comp)).Cast<Def>()
                .OrderBy(g => (float)familyOrderField.GetValue(familyTagField.GetValue(g)));
            foreach (Def gene in ordered)
            {
                InspectionTreeItem row = DefRow(section, gene.LabelCap, gene);
                InspectNodeFactory.DetailLines(row, (string)descriptionFullProperty.GetValue(gene), redundantWithLabel: gene.LabelCap);
                row.IsExpandable = row.Children.Count > 0;
            }
        }

        private void BuildSummary(InspectionTreeItem categoryItem, ThingComp comp)
        {
            int stability = (int)totalStabilityMethod.Invoke(null, new object[] { comp });
            InspectionTreeItem stabilityRow = DefRow(categoryItem,
                "RimWorldAccess.Compat.Vef.AnimalGeneStat".Translate(CompatText.ModText("VRE_Stability").CapitalizeFirst(), stability), null);
            InspectNodeFactory.DetailLines(stabilityRow, CompatText.ModText("VRE_StabilityDesc"));

            float lifespan = (float)lifespanFactorProperty.GetValue(comp);
            InspectionTreeItem lifespanRow = DefRow(categoryItem,
                "RimWorldAccess.Compat.Vef.AnimalGeneFactor".Translate(CompatText.ModText("VRE_LifespanFactor").CapitalizeFirst(), lifespan.ToStringPercent()), null);
            InspectNodeFactory.DetailLines(lifespanRow, CompatText.ModText("VRE_LifespanFactorDesc"));

            var feratype = feratypeField.GetValue(comp) as Def;
            if (feratype == null)
                return;
            InspectionTreeItem feratypeRow = DefRow(categoryItem,
                "RimWorldAccess.Compat.Vef.AnimalGeneStat".Translate(CompatText.ModText("VRE_Feratype"), feratype.LabelCap), feratype);
            InspectNodeFactory.DetailLines(feratypeRow, (string)feratypeDescMethod.Invoke(null, new object[] { feratype }));
            if (feratypeFamilyField.GetValue(feratype) is Def family)
            {
                InspectNodeFactory.DetailLine(feratypeRow, "RimWorldAccess.Compat.Vef.AnimalGeneStat".Translate(
                    CompatText.ModText("VRE_FeratypeFamily").CapitalizeFirst(), family.LabelCap));
            }
        }

        /// <summary>An expandable row whose children are its tooltip lines; a def row opens that def's info card, as the tab's click does.</summary>
        private static InspectionTreeItem DefRow(InspectionTreeItem parent, string label, Def def)
        {
            var row = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = label,
                Data = def,
                LinkedDef = def,
                IndentLevel = parent.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false
            };
            InspectNodeFactory.Attach(parent, row);
            return row;
        }
    }
}
