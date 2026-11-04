using HarmonyLib;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    internal sealed partial class CompositableLoadoutsModule
    {
        partial void ActivateTagEditor(Harmony harmony)
        {
            if (!ClCompat.TagEditor.Ready || !ClCompat.Dialogs.Ready)
            {
                return;
            }
            ScopeForWindow.Register(ClCompat.TagEditorWindowType, w => new ClTagEditorScope(w));
            harmony.Patch(ClCompat.TagEditor.DrawMethod,
                prefix: new HarmonyMethod(typeof(ClTagEditorMirrorPatch), nameof(ClTagEditorMirrorPatch.Prefix)));
        }
    }
}
