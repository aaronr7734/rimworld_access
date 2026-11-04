using HarmonyLib;

namespace RimWorldAccess
{
    internal sealed partial class CompositableLoadoutsModule
    {
        partial void ActivateBillsAndSettings(Harmony harmony)
        {
            if (ClCompat.Bills.Ready)
            {
                BillConfigExtraRow.RegisterSource(ClBillRows.RowsFor);
                ClBillPanelRingPatch.Install(harmony);
            }
            if (ClCompat.Settings.Ready)
            {
                ClSettingsCapturePatch.Install(harmony);
                ClPanicToolbar.Register();
            }
        }
    }
}
