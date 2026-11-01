using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Numbers' Equipment and Inventory icon strips. The cell lists the items in the order the
    /// worker draws them; Enter lists them, and each item opens the worker's own right-click
    /// menu: Information, plus Drop for a player-controlled colonist. Drop runs the action
    /// <c>PawnColumnWorker_Equipment.DropThing</c> returns, which for an inventory item is the
    /// same TryDrop the Inventory worker inlines.
    /// </summary>
    internal sealed class NumbersCarriedItemsColumnHandler : PawnColumnHandler
    {
        private readonly bool equipment;
        private readonly MethodInfo dropThing;

        private NumbersCarriedItemsColumnHandler(bool equipment, MethodInfo dropThing)
        {
            this.equipment = equipment;
            this.dropThing = dropThing;
        }

        public static void Register()
        {
            var surface = new ReflectionSurface("NumbersCarriedItemsColumnHandler");
            Type equipmentWorker = surface.Type("Numbers.PawnColumnWorker_Equipment");
            Type inventoryWorker = surface.Type("Numbers.PawnColumnWorker_Inventory");
            MethodInfo dropThing = surface.Method(equipmentWorker, "DropThing", new[] { typeof(Thing), typeof(Pawn) });
            if (surface.Ready)
            {
                PawnColumnHandlerRegistry.Register(equipmentWorker, new NumbersCarriedItemsColumnHandler(true, dropThing));
                PawnColumnHandlerRegistry.Register(inventoryWorker, new NumbersCarriedItemsColumnHandler(false, dropThing));
            }
        }

        private List<Thing> Items(Pawn pawn)
        {
            var items = new List<Thing>();
            if (!equipment)
            {
                if (pawn.inventory != null && pawn.inventory.innerContainer != null)
                {
                    items.AddRange(pawn.inventory.innerContainer);
                }
                return items;
            }
            if (pawn.equipment != null)
            {
                items.AddRange(pawn.equipment.AllEquipmentListForReading.Cast<Thing>());
            }
            if (pawn.apparel != null)
            {
                items.AddRange(pawn.apparel.WornApparel.OrderByDescending(ap => ap.def.apparel.bodyPartGroups[0].listOrder).Cast<Thing>());
            }
            return items;
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            return string.Join(", ", Items(pawn).Select(ItemLabelHelper.LabelWithCondition));
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            List<Thing> items = Items(pawn);
            if (items.Count == 0)
            {
                return PawnColumnActivation.NotHandled;
            }
            var options = new List<FloatMenuOption>(items.Count);
            foreach (Thing thing in items)
            {
                Thing item = thing;
                options.Add(new FloatMenuOption(ItemLabelHelper.LabelWithCondition(item), () => OpenItemMenu(item, pawn)));
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false, titleText: def.LabelCap.ToString());
            return PawnColumnActivation.OpenedUI;
        }

        private void OpenItemMenu(Thing thing, Pawn pawn)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("ThingInfo".Translate(), () => Find.WindowStack.Add(new Dialog_InfoCard(thing))),
            };
            if (pawn.IsColonistPlayerControlled)
            {
                var drop = dropThing.Invoke(null, new object[] { thing, pawn }) as Action;
                options.Add(new FloatMenuOption("DropThing".Translate(), drop));
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false, titleText: thing.LabelCap);
        }

        public override bool ActivatesCellWithoutTable(PawnColumnDef def)
        {
            return true;
        }
    }
}
