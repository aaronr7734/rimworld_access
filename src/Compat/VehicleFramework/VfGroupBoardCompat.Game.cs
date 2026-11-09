using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Vehicle Framework replaces the right-click menu of several selected colonists on a vehicle
    /// with its own group-boarding menu (Patch_Misc.MultiSelectFloatMenu on
    /// Selector.HandleMapClicks), so the keyboard order brackets offer that same list: VF's own
    /// VehiclePawn.MultiplePawnFloatMenuOptions builds it and its menu is harvested before it
    /// reaches the window stack.
    /// </summary>
    internal static class VfGroupBoardCompat
    {
        private static Type vehiclePawnType;
        private static MethodInfo multiplePawnFloatMenuOptionsMethod;

        public static void Register()
        {
            var surface = new ReflectionSurface("VfGroupBoardCompat");
            vehiclePawnType = surface.Supplied("Vehicles.VehiclePawn", VfVehiclePawn.VehiclePawnType);
            multiplePawnFloatMenuOptionsMethod = surface.Method(vehiclePawnType, "MultiplePawnFloatMenuOptions",
                new[] { typeof(List<Pawn>) });
            if (surface.Ready)
            {
                OrderOptionOverrides.Register(GroupBoardOptions);
            }
        }

        /// <summary>
        /// Mirrors Vehicles.SelectionHelper.MultiSelectClicker's gate, reading the cursor cell where
        /// that method reads UI.MouseMapPosition, which the keyboard never moves.
        /// </summary>
        private static List<FloatMenuOption> GroupBoardOptions(List<Pawn> pawns, IntVec3 cell)
        {
            List<object> selected = Find.Selector.SelectedObjects;
            if (selected.Count <= 1)
            {
                return null;
            }
            foreach (object obj in selected)
            {
                if (!(obj is Pawn pawn) || pawn.Faction != Faction.OfPlayer || vehiclePawnType.IsInstanceOfType(pawn))
                {
                    return null;
                }
            }

            Map map = pawns[0].Map;
            if (map == null || !cell.InBounds(map))
            {
                return null;
            }
            foreach (Thing thing in map.thingGrid.ThingsAt(cell))
            {
                if (vehiclePawnType.IsInstanceOfType(thing))
                {
                    var boarders = new List<Pawn>(pawns);
                    return FloatMenuHarvest.Capture(() => multiplePawnFloatMenuOptionsMethod.Invoke(thing, new object[] { boarders }));
                }
            }
            return null;
        }
    }
}
