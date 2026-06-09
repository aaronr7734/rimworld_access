using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    public static class VehicleCargoLoadingPatch
    {
        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_LoadCargo_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!VehicleCargoLoadingState.IsLoadCargoDialog(__instance))
                    return;

                bool wasAccepted = VehicleCargoLoadingState.AcceptAttempted;
                VehicleCargoLoadingState.Close();

                if (!wasAccepted)
                    TolkHelper.Speak("Vehicle cargo loading cancelled");
            }
        }

        [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
        public static class Window_OnCancelKeyPressed_LoadCargo_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Window __instance)
            {
                if (!VehicleCargoLoadingState.IsLoadCargoDialog(__instance))
                    return true;

                if (QuantityMenuState.IsActive || WindowlessInspectionState.IsActive)
                    return false;

                return true;
            }
        }

        [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
        public static class Window_OnAcceptKeyPressed_LoadCargo_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Window __instance)
            {
                if (!VehicleCargoLoadingState.IsLoadCargoDialog(__instance))
                    return true;

                if (VehicleCargoLoadingState.IsActive)
                    return false;

                return true;
            }
        }
    }
}
