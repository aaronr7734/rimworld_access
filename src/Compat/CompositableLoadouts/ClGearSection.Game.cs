using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The mod's Gear tab section (Panel_GearTab.DrawTags): the loadout and tag editor buttons, then
    /// "Field hand 1/2: Surgery queued" per prosthetic, worded exactly as DrawProsthetics labels it.
    /// </summary>
    internal static class ClGearSection
    {
        public static void Build(InspectionTreeItem gearCategory, Pawn pawn, InspectionMode mode)
        {
            if (!ClCompat.IsValidLoadoutHolder(pawn))
            {
                return;
            }
            InspectNodeFactory.Section(gearCategory, Shell.CompatText.ModText("CompositableLoadouts.TagLoadoutComposition"),
                null, section => BuildChildren(section, pawn, mode));
        }

        private static void BuildChildren(InspectionTreeItem section, Pawn pawn, InspectionMode mode)
        {
            if (mode == InspectionMode.Full)
            {
                InspectNodeFactory.ActionRow(section,
                    Shell.CompatText.ModArgs("CompositableLoadouts.EditXLoadout", pawn.LabelShort), null,
                    () => ClCompat.OpenLoadoutEditor(pawn));
                InspectNodeFactory.ActionRow(section, Shell.CompatText.ModText("CompositableLoadouts.EditOrCreateTags"), null,
                    () => ClCompat.OpenTagEditor(null));
            }

            object loadout = ClCompat.LoadoutOf(pawn);
            object slots = ClCompat.Gear.CurrentSlots(pawn, loadout);
            foreach (object item in ClCompat.Gear.ActiveProsthetics(loadout))
            {
                ThingDef def = ClCompat.ItemRepresentativeDef(item);
                if (def == null)
                {
                    continue;
                }
                int quantity = ClCompat.ItemQuantity(item);
                int satisfied = Mathf.Min(ClCompat.Gear.SatisfiedCount(pawn, def), quantity);
                InspectNodeFactory.DetailLine(section, "RimWorldAccess.Compat.CompositableLoadouts.ProstheticRow".Translate(
                    def.LabelCap, satisfied, quantity, ClCompat.Gear.ProstheticStatus(pawn, loadout, slots, item)));
            }
        }
    }
}
