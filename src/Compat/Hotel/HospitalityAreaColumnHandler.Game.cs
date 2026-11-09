using System;
using System.Collections;
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
        private readonly MethodInfo getAreas;
        private readonly MethodInfo shoppingLabel;
        private readonly Type areaBaseType;
        private readonly Type shoppingColumnType;
        private readonly bool ready;

        private HospitalityAreaColumnHandler()
        {
            var surface = new ReflectionSurface("HospitalityAreaColumnHandler");
            areaBaseType = surface.Type("Hospitality.MainTab.PawnColumnWorker_AreaBase");
            shoppingColumnType = surface.Type("Hospitality.MainTab.PawnColumnWorker_ShoppingArea");
            Type guestUtilityType = surface.Type("Hospitality.Utilities.GuestUtility");
            Type genericUtilityType = surface.Type("Hospitality.Utilities.GenericUtility");
            getArea = surface.Method(areaBaseType, "GetArea", new[] { typeof(Pawn) });
            setArea = surface.Method(areaBaseType, "SetArea", new[] { typeof(Pawn), typeof(Area) });
            isGuest = surface.Method(guestUtilityType, "IsGuest", new[] { typeof(Pawn) });
            getAreas = surface.Method(guestUtilityType, "GetAreas", new[] { typeof(Map) });
            shoppingLabel = surface.Method(genericUtilityType, "GetShoppingLabel", new[] { typeof(Area) });
            ready = surface.Ready;
        }

        public static void Register()
        {
            try
            {
                var handler = new HospitalityAreaColumnHandler();
                if (handler.ready)
                {
                    PawnColumnHandlerRegistry.Register(handler.areaBaseType, handler);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Hospitality area column registration failed: {ex.Message}");
            }
        }

        // The shopping column's DoCell override draws GenericUtility.GetShoppingLabel; the
        // accommodation column keeps AreaGUI's AreaUtility.AreaAllowedLabel_Area.
        private string AreaLabel(PawnColumnWorker worker, Area area)
        {
            string label = shoppingColumnType.IsInstanceOfType(worker)
                ? (string)shoppingLabel.Invoke(null, new object[] { area })
                : AreaUtility.AreaAllowedLabel_Area(area);
            return label.StripTags();
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
            if (map == null || !IsGuest(pawn) || !(getAreas.Invoke(null, new object[] { map }) is IEnumerable areas))
            {
                return PawnColumnActivation.NotHandled;
            }

            PawnColumnWorker worker = def.Worker;
            var choices = new List<Area> { null };
            foreach (object area in areas)
            {
                if (area is Area a)
                {
                    choices.Add(a);
                }
            }
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
