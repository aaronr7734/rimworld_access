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

        public static void AerialVehicleStartChoosingDestinationPostfix(object __instance)
        {
            AerialVehicleLaunchState.OpenFromLauncher(__instance);
        }

        public static void AerialVehicleInFlightStartTargetingPostfix(object __instance)
        {
            AerialVehicleLaunchState.OpenFromAerialVehicle(__instance);
        }

        public static void VehicleCaravanLaunchAerialVehiclePrefix(object __instance)
        {
            AerialVehicleLaunchState.PrepareForVehicleCaravanLaunch(__instance);
        }

        public static void AerialVehicleLandingBeginTargetingPostfix(object __instance)
        {
            AerialVehicleLandingState.Open(__instance);
        }

        public static void AerialVehicleWorldTargeterStartPostfix(object __instance)
        {
            AerialVehicleLaunchState.SetActiveWorldTargeter(__instance);
        }

        public static void AerialVehicleWorldTargeterStopPostfix()
        {
            AerialVehicleLaunchState.Close();
        }

        public static void VehicleOrientationControllerStartPostfix(object __instance)
        {
            Thing vehicle = HarmonyLib.AccessTools.Field(__instance?.GetType(), "vehicle")?.GetValue(__instance) as Thing;
            if (vehicle != null)
                VehicleOrientationState.Open(vehicle);
        }

        public static void VehicleOrientationControllerStopPostfix()
        {
            VehicleOrientationState.Close();
        }
    }
}
