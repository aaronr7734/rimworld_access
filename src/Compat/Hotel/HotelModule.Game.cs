using HarmonyLib;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    internal sealed class HotelModule : CompatModule
    {
        public override string TargetPackageId => "Orion.Hospitality";

        public override bool ShouldActivate =>
            AccessTools.TypeByName("Hospitality.Gizmo_GuestBed") != null
            || AccessTools.TypeByName("CashRegister.Building_CashRegister") != null;

        public override void Activate(Harmony harmony)
        {
            HospitalityCompat.Register(harmony);
            HospitalityCompat.RegisterGizmoHandlers();
            HospitalityAreaColumnHandler.Register();
            HospitalityMoreColumnHandler.Register();
            CustomCheckboxLabeledTap.Patch(harmony, "Hospitality.CustomWidget", "Hospitality.Utilities.CustomWidget");
            CustomCheckboxLabeledTap.Patch(harmony, "CashRegister.CustomWidget");
            CashRegisterCompat.RegisterGizmoHandlers();
            CashRegisterShiftsCompat.Register(harmony);
            HospitalityGuestTabAdapter.TryRegister();
            GastronomyRestaurantTabAdapter.TryRegister();
            StorefrontStoreTabAdapter.TryRegister();
            HospitalitySocialBarsExtender.Register();
        }
    }
}
