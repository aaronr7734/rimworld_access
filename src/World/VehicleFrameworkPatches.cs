using RimWorld;

namespace RimWorldAccess
{
    public static class VehicleFrameworkPatches
    {
        public static void TrySendVehicleCaravanPrefix(Dialog_FormCaravan formCaravan)
        {
            VehicleFrameworkHelper.PrepareVehicleSend(formCaravan);
        }

        public static void RecacheTransferablesPostfix(object __instance)
        {
            VehicleFrameworkHelper.PostprocessVehicleFormationRecache(__instance);
        }
    }
}
