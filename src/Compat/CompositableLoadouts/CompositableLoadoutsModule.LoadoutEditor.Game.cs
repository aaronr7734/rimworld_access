using HarmonyLib;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    internal sealed partial class CompositableLoadoutsModule
    {
        partial void ActivateLoadoutEditor(Harmony harmony)
        {
            if (!ClCompat.Editor.Ready)
            {
                return;
            }
            ScopeForWindow.Register(ClCompat.LoadoutEditorWindowType, w => new ClLoadoutEditorScope(w));
            ClLoadoutEditorDrawPatch.Install(harmony);
        }
    }
}
