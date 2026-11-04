using HarmonyLib;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    internal sealed partial class CompositableLoadoutsModule
    {
        partial void ActivateOverview(Harmony harmony)
        {
            if (!ClCompat.Overview.Ready || !ClCompat.Columns.Ready)
            {
                return;
            }
            ScopeForWindow.Register(ClCompat.OverviewWindowType, w => new ClOverviewScope(w));
            ClOverviewDrawPatch.Install(harmony);
        }
    }
}
