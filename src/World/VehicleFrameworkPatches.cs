using RimWorld;
using Verse;

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

        public static void LoadCargoPostOpenPostfix(Window __instance)
        {
            VehicleCargoLoadingState.Open(__instance);
        }
    }
}
