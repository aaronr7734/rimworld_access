using HarmonyLib;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    internal sealed partial class CompositableLoadoutsModule
    {
        partial void ActivateItemSpecifier(Harmony harmony)
        {
            if (!ClCompat.ItemSpecifier.Ready)
            {
                return;
            }
            ScopeForWindow.Register(ClCompat.ItemSpecifierWindowType, w => new ClItemSpecifierScope(w));
            ClItemSpecifierDrawPatch.Install(harmony);
        }
    }
}
