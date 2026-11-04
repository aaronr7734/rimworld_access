using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the pawn Gear tab ("Gear" category). Builds the summary
    /// block (mass carried, comfortable temperature range, overall armor),
    /// the Equipment/Apparel/Inventory subtree, each item's available
    /// actions, and executes the selected gear action (drop/consume/view
    /// info). The summary block and per-item masses replicate
    /// ITab_Pawn_Gear's own draw path verbatim (gates included).
    /// </summary>
    internal sealed class PawnGearAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Gear";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            Pawn pawn = InspectionTreeBuilder.GetPawnFromThing(obj);
            if (pawn == null)
                return;
            BuildGearChildren(categoryItem, pawn, mode);
        }

        /// <summary>
        /// Builds children for Gear category.
        /// </summary>
        private static void BuildGearChildren(InspectionTreeItem parentItem, Pawn pawn, InspectionMode mode)
        {
            BuildGearSummary(parentItem, pawn);
            PawnGearSectionRegistry.BuildAll(parentItem, pawn, mode);

            var gearCategories = new (string labelKey, InspectSectionKind kind)[]
            {
                ("Equipment", InspectSectionKind.GearEquipment),
                ("Apparel", InspectSectionKind.GearApparel),
                ("Inventory", InspectSectionKind.GearInventory),
            };

            foreach (var (labelKey, kind) in gearCategories)
            {
                InspectSectionKind localKind = kind;
                var gearItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = labelKey.Translate().ToString(),
                    Data = new InspectSectionDatum(pawn, kind),
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false,
                    // Auto-expand for typeahead so weapons/apparel/inventory items
                    // (lazily built below) are matchable by name without drilling in.
                    AutoExpandForSearch = true
                };

                gearItem.OnActivate = () => BuildGearItemsChildren(gearItem, pawn, localKind, mode);
                InspectNodeFactory.Attach(parentItem, gearItem);
            }

            BuildGearDevTool(parentItem, pawn, mode);
        }

        /// <summary>
        /// The gear tab's "Dev tool..." button (ITab_Pawn_Gear.FillTab :125),
        /// gated on <see cref="Prefs.DevMode"/> exactly like vanilla. Opens
        /// vanilla's own <c>DebugToolsPawns.PawnGearDevOptions</c> FloatMenu
        /// (Vehicle A — vanilla's own option builder and delegates) through the
        /// keyboard float-menu path. Full mode only: the options mutate the pawn,
        /// so they stay out of read-only inspection contexts. Dev mode
        /// grants full parity with every button; the
        /// label is a vanilla dev-tool literal, presented verbatim.
        /// </summary>
        private static void BuildGearDevTool(InspectionTreeItem parentItem, Pawn pawn, InspectionMode mode)
        {
            if (!Prefs.DevMode || mode != InspectionMode.Full)
                return;

            var devToolItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Action,
                Label = "Dev tool...", // l10n-exempt: verbatim vanilla dev label (ITab_Pawn_Gear.cs:125), itself unlocalized
                IndentLevel = parentItem.IndentLevel + 1,
                IsExpandable = false,
                OpensOverlayMenu = true
            };
            devToolItem.OnActivate = () =>
            {
                List<FloatMenuOption> options = DebugToolsPawns.PawnGearDevOptions(pawn);
                if (options == null || options.Count == 0)
                {
                    TolkHelper.Speak("RimWorldAccess.Dev.NoActions".Loc());
                    return;
                }
                WindowlessFloatMenuState.Open(options, colonistOrders: false);
            };
            InspectNodeFactory.Attach(parentItem, devToolItem);
        }

        /// <summary>
        /// The gear tab's summary block: mass carried, comfortable temperature
        /// range, and the overall armor section. Values, gates and wording
        /// replicate ITab_Pawn_Gear.TryDrawMassInfo /
        /// TryDrawComfyTemperatureRange / TryDrawOverallArmor verbatim.
        /// </summary>
        private static void BuildGearSummary(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (!pawn.Dead && ShouldShowInventory(pawn))
            {
                float mass = MassUtility.GearAndInventoryMass(pawn);
                float capacity = MassUtility.Capacity(pawn);
                InspectNodeFactory.DetailLine(parentItem,
                    "MassCarried".Translate(mass.ToString("0.##"), capacity.ToString("0.##")));
            }

            if (!pawn.Dead)
            {
                InspectNodeFactory.DetailLine(parentItem,
                    "ComfyTemperatureRange".Translate() + ": "
                    + pawn.GetStatValue(StatDefOf.ComfyTemperatureMin).ToStringTemperature("F0") + " ~ "
                    + pawn.GetStatValue(StatDefOf.ComfyTemperatureMax).ToStringTemperature("F0"));
            }

            if (ShouldShowOverallArmor(pawn))
            {
                var armorItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = "OverallArmor".Translate(),
                    ExpandedLabel = "OverallArmor".Translate(),
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false
                };
                InspectNodeFactory.DetailLine(armorItem,
                    "ArmorSharp".Translate() + ": " + GetOverallArmor(pawn, StatDefOf.ArmorRating_Sharp).ToStringPercent());
                InspectNodeFactory.DetailLine(armorItem,
                    "ArmorBlunt".Translate() + ": " + GetOverallArmor(pawn, StatDefOf.ArmorRating_Blunt).ToStringPercent());
                InspectNodeFactory.DetailLine(armorItem,
                    "ArmorHeat".Translate() + ": " + GetOverallArmor(pawn, StatDefOf.ArmorRating_Heat).ToStringPercent());
                // Fold the values into the collapsed label (the body-part
                // pattern) so the collapsed row speaks all three values.
                var armorLabels = armorItem.Children.Select(c => c.Label).ToList();
                armorItem.Label += $": {string.Join(". ", armorLabels)}";
                InspectNodeFactory.Attach(parentItem, armorItem);
            }
        }

        /// <summary>ITab_Pawn_Gear.TryDrawOverallArmor's accumulation, verbatim.</summary>
        private static float GetOverallArmor(Pawn pawn, StatDef stat)
        {
            float num = 0f;
            float num2 = Mathf.Clamp01(pawn.GetStatValue(stat) / 2f);
            List<BodyPartRecord> allParts = pawn.RaceProps.body.AllParts;
            List<Apparel> list = pawn.apparel != null ? pawn.apparel.WornApparel : null;
            for (int i = 0; i < allParts.Count; i++)
            {
                float num3 = 1f - num2;
                if (list != null)
                {
                    for (int j = 0; j < list.Count; j++)
                    {
                        if (list[j].def.apparel.CoversBodyPart(allParts[i]))
                        {
                            float num4 = Mathf.Clamp01(list[j].GetStatValue(stat) / 2f);
                            num3 *= 1f - num4;
                        }
                    }
                }
                num += allParts[i].coverageAbs * (1f - num3);
            }
            return Mathf.Clamp(num * 2f, 0f, 2f);
        }

        /// <summary>ITab_Pawn_Gear.ShouldShowInventory, verbatim.</summary>
        private static bool ShouldShowInventory(Pawn p)
        {
            if (!p.RaceProps.Humanlike)
            {
                return p.inventory.innerContainer.Any;
            }
            return true;
        }

        /// <summary>ITab_Pawn_Gear.ShouldShowApparel, verbatim.</summary>
        private static bool ShouldShowApparel(Pawn p)
        {
            if (p.apparel == null)
            {
                return false;
            }
            if (!p.RaceProps.Humanlike)
            {
                return p.apparel.WornApparel.Any();
            }
            return true;
        }

        /// <summary>ITab_Pawn_Gear.ShouldShowOverallArmor, verbatim.</summary>
        private static bool ShouldShowOverallArmor(Pawn p)
        {
            if (!p.RaceProps.Humanlike && !ShouldShowApparel(p)
                && !(p.GetStatValue(StatDefOf.ArmorRating_Sharp) > 0f)
                && !(p.GetStatValue(StatDefOf.ArmorRating_Blunt) > 0f))
            {
                return p.GetStatValue(StatDefOf.ArmorRating_Heat) > 0f;
            }
            return true;
        }

        /// <summary>
        /// Builds children for a specific gear category (Equipment/Apparel/Inventory).
        /// </summary>
        private static void BuildGearItemsChildren(InspectionTreeItem gearCatItem, Pawn pawn, InspectSectionKind gearCategory, InspectionMode mode)
        {
            if (gearCatItem.Children.Count > 0)
                return; // Already built

            List<InteractiveGearHelper.GearItem> items = null;

            switch (gearCategory)
            {
                case InspectSectionKind.GearEquipment:
                    items = InteractiveGearHelper.GetEquipmentItems(pawn);
                    break;
                case InspectSectionKind.GearApparel:
                    items = InteractiveGearHelper.GetApparelItems(pawn);
                    break;
                case InspectSectionKind.GearInventory:
                    items = InteractiveGearHelper.GetInventoryItems(pawn);
                    break;
            }

            if (items == null || items.Count == 0)
                return;

            foreach (var gearItem in items)
            {
                // The mass column from ITab_Pawn_Gear.DrawThingRow, verbatim
                // value (stat times stack count), appended to the row label.
                string massSuffix = gearItem.Thing != null
                    ? ", " + (gearItem.Thing.GetStatValue(StatDefOf.Mass) * gearItem.Thing.stackCount).ToStringMass()
                    : "";
                // Read-only mod-compat detail rows (RWoM enchantment stats,
                // JecsTools gear slots) stay expandable even in ReadOnly mode —
                // they carry no actions, only information a sighted player sees.
                bool hasCompatDetails = RwomEnchantmentAdapter.HasGearEnchantment(gearItem.Thing)
                    || JecsGearSlotCompat.HasSlots(gearItem.Thing);
                bool expandable = mode == InspectionMode.Full || hasCompatDetails;
                var item = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = gearItem.Label + massSuffix,
                    Data = gearItem,
                    IndentLevel = gearCatItem.IndentLevel + 1,
                    IsExpandable = expandable,
                    IsExpanded = false
                };

                if (expandable)
                {
                    item.OnActivate = () => BuildGearItemChildren(item, pawn, gearItem, mode);
                }

                InspectNodeFactory.Attach(gearCatItem, item);
            }
        }

        /// <summary>
        /// Builds a gear item's children: read-only mod-compat detail rows
        /// first (RWoM enchantment stats, JecsTools gear slots — present in
        /// both ReadOnly and Full mode), then the item's own actions in Full
        /// mode only.
        /// </summary>
        private static void BuildGearItemChildren(InspectionTreeItem gearItem, Pawn pawn, InteractiveGearHelper.GearItem gear, InspectionMode mode)
        {
            if (gearItem.Children.Count > 0)
                return; // Already built

            List<string> enchantLines = RwomEnchantmentAdapter.TryGetGearEnchantmentLines(gear.Thing);
            if (enchantLines != null)
            {
                foreach (string line in enchantLines)
                    InspectNodeFactory.DetailLine(gearItem, line);
            }
            JecsGearSlotCompat.AppendSlotRows(gearItem, gear.Thing);

            if (mode != InspectionMode.Full)
                return;

            var actions = InteractiveGearHelper.GetAvailableActions(gear, pawn);

            foreach (var action in actions)
            {
                var capturedAction = action;
                var actionItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = InteractiveGearHelper.GetActionLabel(capturedAction),
                    Data = new GearActionDatum(pawn, gear, capturedAction),
                    IndentLevel = gearItem.IndentLevel + 1,
                    IsExpandable = false
                };

                actionItem.OnActivate = () => ExecuteGearAction(pawn, gear, capturedAction);
                InspectNodeFactory.Attach(gearItem, actionItem);
            }
        }

        /// <summary>
        /// Executes a gear action.
        /// </summary>
        private static void ExecuteGearAction(Pawn pawn, InteractiveGearHelper.GearItem gear, GearAction action)
        {
            bool success = false;

            switch (action)
            {
                case GearAction.Drop:
                    success = InteractiveGearHelper.ExecuteDropAction(gear, pawn);
                    if (success)
                    {
                        // Rebuild tree to reflect changes
                        WindowlessInspectionState.RebuildTree();
                    }
                    break;
                case GearAction.Consume:
                    success = InteractiveGearHelper.ExecuteConsumeAction(gear, pawn);
                    if (success)
                    {
                        // Rebuild tree to reflect changes
                        WindowlessInspectionState.RebuildTree();
                    }
                    break;
                case GearAction.ViewInfo:
                    // Close current inspection menu and open new one for the item
                    // Pass the pawn as parent so Escape returns to the pawn's inspection
                    WindowlessInspectionState.Close();
                    WindowlessInspectionState.OpenForObject(gear.Thing, pawn);
                    break;
            }
        }
    }
}
