using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// The Carry cell as Carry Different Medicine Types draws it (PawnColumnWorker_Carry_DoCell_Patch):
    /// one icon per stock entry with a positive count, else a crossed-out medicine icon, over a
    /// button that opens Dialog_SetMedicines. Never reads through Pawn_InventoryStockTracker's
    /// getters: they re-create vanilla's Medicine entry, which the mod's dialog removes.
    /// </summary>
    internal sealed class CdmtCarryColumnHandler : PawnColumnHandler, IPawnColumnValueCopier
    {
        internal const string DialogTypeName = "CarryDifferentMedicineTypes.Dialog_SetMedicines";

        private readonly ConstructorInfo dialogCtor;
        private readonly MethodInfo addAllGroups;
        private readonly MethodInfo orderByPotency;
        private readonly FieldInfo stockGroupDefs;

        private CdmtCarryColumnHandler(ConstructorInfo dialogCtor, MethodInfo addAllGroups, MethodInfo orderByPotency, FieldInfo stockGroupDefs)
        {
            this.dialogCtor = dialogCtor;
            this.addAllGroups = addAllGroups;
            this.orderByPotency = orderByPotency;
            this.stockGroupDefs = stockGroupDefs;
        }

        public static void Register()
        {
            var surface = new ReflectionSurface("CdmtCarryColumnHandler");
            Type dialogType = surface.Type(DialogTypeName);
            Type startUpType = surface.Type("CarryDifferentMedicineTypes.StartUp");
            ConstructorInfo dialogCtor = surface.Constructor(dialogType, new[] { typeof(Pawn) });
            MethodInfo addAllGroups = surface.Method(dialogType, "AddAllPossibleInventoryStockGroupDefsToPawn", Type.EmptyTypes);
            MethodInfo orderByPotency = surface.Method(dialogType, "OrderEntriesByMedPotency", Type.EmptyTypes);
            FieldInfo stockGroupDefs = surface.Field(startUpType, "stockGroupDefs");
            if (surface.Ready)
            {
                PawnColumnHandlerRegistry.Register(typeof(PawnColumnWorker_Carry),
                    new CdmtCarryColumnHandler(dialogCtor, addAllGroups, orderByPotency, stockGroupDefs));
            }
        }

        /// <summary>RenderIcons' filter and order; each icon is the group's default thing, not the entry's.</summary>
        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            if (pawn.inventoryStock == null)
            {
                return "";
            }
            var entries = new List<string>();
            foreach (KeyValuePair<InventoryStockGroupDef, InventoryStockEntry> pair in pawn.inventoryStock.stockEntries)
            {
                ThingDef medicine = pair.Key != null ? pair.Key.DefaultThingDef : null;
                if (pair.Value != null && pair.Value.count > 0 && medicine != null && medicine.IsMedicine)
                {
                    entries.Add("RimWorldAccess.Compat.Cdmt.CarryEntry".Translate(medicine.label, pair.Value.count).Resolve());
                }
            }
            return entries.Count > 0 ? entries.ToCommaList().CapitalizeFirst() : "None".Translate().Resolve();
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            if (pawn.inventoryStock == null)
            {
                return PawnColumnActivation.NotHandled;
            }
            Find.WindowStack.Add((Window)dialogCtor.Invoke(new object[] { pawn }));
            SoundDefOf.Click.PlayOneShotOnCamera(null);
            return PawnColumnActivation.OpenedUI;
        }

        public override bool ActivatesCellWithoutTable(PawnColumnDef def)
        {
            return true;
        }

        /// <summary>
        /// Prepares the target exactly as the dialog's PostOpen does, through the mod's own methods on
        /// a never-opened instance (Window's constructor touches no window stack), then copies every
        /// medicine group's count.
        /// </summary>
        public bool TryCopyValue(PawnColumnDef def, Pawn sourcePawn, Pawn targetPawn)
        {
            if (sourcePawn.inventoryStock == null || targetPawn.inventoryStock == null)
            {
                return false;
            }
            try
            {
                object detached = dialogCtor.Invoke(new object[] { targetPawn });
                addAllGroups.Invoke(detached, null);
                orderByPotency.Invoke(detached, null);
                var groups = stockGroupDefs.GetValue(null) as IEnumerable;
                if (groups == null)
                {
                    return false;
                }
                Pawn_InventoryStockTracker source = sourcePawn.inventoryStock;
                Pawn_InventoryStockTracker target = targetPawn.inventoryStock;
                foreach (InventoryStockGroupDef group in groups)
                {
                    InventoryStockEntry entry;
                    source.stockEntries.TryGetValue(group, out entry);
                    if (entry != null && entry.thingDef != null && group.thingDefs.Contains(entry.thingDef))
                    {
                        target.SetThingForGroup(group, entry.thingDef);
                    }
                    // MUTATION-C: mirrors CarryDifferentMedicineTypes Dialog_SetMedicines.DoRow slider write; the slider has no gate beyond its 0..group.max range, which SetCountForGroup clamps to.
                    target.SetCountForGroup(group, entry != null ? entry.count : 0);
                }
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("CdmtCarryColumnHandler.TryCopyValue", ex);
                return false;
            }
        }
    }
}
