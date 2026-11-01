using HarmonyLib;

namespace RimWorldAccess
{
    internal sealed class VehicleFrameworkModule : CompatModule
    {
        public override string TargetPackageId => "SmashPhil.VehicleFramework";

        // Probed by type rather than package id: forks and renamed uploads keep the assembly
        // types. SmashTools travels inside VF but is probed separately because the
        // world-targeter compat rides SmashTools alone.
        public override bool ShouldActivate =>
            AccessTools.TypeByName("Vehicles.VehiclePawn") != null
            || AccessTools.TypeByName("SmashTools.Targeting.TargeterDispatcher") != null;

        public override void Activate(Harmony harmony)
        {
            VfGizmoCompat.RegisterGizmoHandlers();
            VfAutopilotCompat.Register(harmony);
            VfTurretTargeterCompat.Register(harmony);
            VfHaulTargeterCompat.Register(harmony);
            VfWorldTargeterCompat.Register(harmony);
            VfLandingTargeterCompat.Register(harmony);
            VfOrientationCompat.Register(harmony);
            VfPassengersTabCompat.RegisterTabAdapters();
            VfUpgradesTabCompat.RegisterTabAdapters();
            VfHealthTabCompat.RegisterTabAdapters();
            VfCargoTabCompat.RegisterTabAdapters();
            VfVehicleActionsCompat.Register();
            VfDialogCompat.RegisterDialogScopes();
            VfDialogCompat.ApplyPostClosePatches(harmony);
            VfCaravanCompat.Register();
        }
    }
}
