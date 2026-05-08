using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Optional reflection bridge for Vehicle Framework. Keeps RimWorld Access buildable
    /// and usable when Vehicle Framework is not installed.
    /// </summary>
    public static class VehicleFrameworkHelper
    {
        private const int VehicleFrameworkVehiclesTab = 10;

        private static Type vehiclePawnType;
        private static Type formCaravanPatchType;
        private static FieldInfo selectedTabField;

        public static bool IsVehiclePawn(Thing thing)
        {
            if (thing == null)
                return false;

            Type type = GetVehiclePawnType();
            if (type != null)
                return type.IsInstanceOfType(thing);

            return thing.GetType().FullName == "Vehicles.VehiclePawn";
        }

        public static bool HasVehicleTransferables(List<TransferableOneWay> transferables)
        {
            if (transferables == null)
                return false;

            foreach (TransferableOneWay transferable in transferables)
            {
                if (IsVehiclePawn(transferable?.AnyThing))
                    return true;
            }
            return false;
        }

        public static void SyncFormCaravanTab(int vanillaTabValue, bool vehiclesTab)
        {
            FieldInfo field = GetSelectedTabField();
            if (field == null)
                return;

            field.SetValue(null, vehiclesTab ? VehicleFrameworkVehiclesTab : vanillaTabValue);
        }

        private static Type GetVehiclePawnType()
        {
            if (vehiclePawnType == null)
            {
                vehiclePawnType = AccessTools.TypeByName("Vehicles.VehiclePawn");
            }
            return vehiclePawnType;
        }

        private static FieldInfo GetSelectedTabField()
        {
            if (selectedTabField != null)
                return selectedTabField;

            if (formCaravanPatchType == null)
            {
                formCaravanPatchType = AccessTools.TypeByName("Vehicles.Patch_FormCaravanDialog");
            }

            if (formCaravanPatchType == null)
                return null;

            selectedTabField = AccessTools.Field(formCaravanPatchType, "selectedTab");
            return selectedTabField;
        }
    }
}
