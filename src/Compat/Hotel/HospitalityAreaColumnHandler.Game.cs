using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The guest table's two area columns (accommodation and shopping, both on
    /// <c>PawnColumnWorker_AreaBase</c>). Each cell is a hand-copied mouse-drag area strip with no
    /// button primitive, so the generic tier cannot read or drive it. The cell reads the worker's own
    /// GetArea and names it with the same label function the strip draws; Enter opens a menu over
    /// the mod's own area list (GuestUtility.GetAreas plus the null entry the strip leads with), each
    /// option writing through the worker's own SetArea. One registration on the shared base covers
    /// both columns, since MethodInfo.Invoke on the abstract pair dispatches virtually.
    /// </summary>
    internal sealed class HospitalityAreaColumnHandler : PawnColumnHandler
    {
        private readonly MethodInfo getArea;
        private readonly MethodInfo setArea;
        private readonly MethodInfo isGuest;
        private readonly HospitalityAreas areas;
        private readonly Type shoppingColumnType;
        private readonly bool ready;

        private HospitalityAreaColumnHandler(Type areaBaseType)
        {
            var surface = new ReflectionSurface("HospitalityAreaColumnHandler");
            surface.Supplied("Hospitality.MainTab.PawnColumnWorker_AreaBase", areaBaseType);
            shoppingColumnType = surface.Type("Hospitality.MainTab.PawnColumnWorker_ShoppingArea");
            Type guestUtilityType = surface.Type("Hospitality.Utilities.GuestUtility");
            getArea = surface.Method(areaBaseType, "GetArea", new[] { typeof(Pawn) });
            setArea = surface.Method(areaBaseType, "SetArea", new[] { typeof(Pawn), typeof(Area) });
            isGuest = surface.Method(guestUtilityType, "IsGuest", new[] { typeof(Pawn) });
            areas = new HospitalityAreas(surface);
            ready = surface.Ready;
        }

        public static void Register()
        {
            CompatRegistration.PawnColumnHandler("Hospitality.MainTab.PawnColumnWorker_AreaBase", t =>
            {
                var handler = new HospitalityAreaColumnHandler(t);
                return handler.ready ? handler : null;
            });
        }

        // The shopping column's DoCell override draws GenericUtility.GetShoppingLabel; the
        // accommodation column keeps AreaGUI's AreaUtility.AreaAllowedLabel_Area.
        private string AreaLabel(PawnColumnWorker worker, Area area)
        {
            return shoppingColumnType.IsInstanceOfType(worker) ? areas.ShoppingLabel(area) : HospitalityAreas.StayLabel(area);
        }

        private bool IsGuest(Pawn pawn)
        {
            return (bool)isGuest.Invoke(null, new object[] { pawn });
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            // The worker's DoCell draws nothing for a non-guest.
            if (!IsGuest(pawn))
            {
                return "";
            }
            return AreaLabel(def.Worker, (Area)getArea.Invoke(def.Worker, new object[] { pawn }));
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            Map map = pawn.MapHeld;
            if (map == null || !IsGuest(pawn))
            {
                return PawnColumnActivation.NotHandled;
            }

            PawnColumnWorker worker = def.Worker;
            List<Area> choices = areas.Choices(map);
            var options = new List<FloatMenuOption>(choices.Count);
            foreach (Area area in choices)
            {
                Area chosen = area;
                options.Add(new FloatMenuOption(AreaLabel(worker, chosen),
                    () => setArea.Invoke(worker, new object[] { pawn, chosen })));
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
            return PawnColumnActivation.OpenedUI;
        }
    }
}
