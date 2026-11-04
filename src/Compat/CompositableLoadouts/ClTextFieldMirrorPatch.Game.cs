using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The tag picker's search box and the state editor's name boxes are raw GUI.TextFields the
    /// shell never sees, so an edit session posts its buffer into the backing string just before
    /// each draw: the mod filters and renders from it live, and Escape keeps what was typed.
    /// </summary>
    internal static class ClTextFieldMirrorPatch
    {
        public static void Install(Harmony harmony)
        {
            PatchPrefix(harmony, ClCompat.Dialogs.SelectorDrawSearchBar);
            PatchPrefix(harmony, ClCompat.Dialogs.EditorDoWindowContents);
        }

        private static void PatchPrefix(Harmony harmony, MethodInfo target)
        {
            if (target != null)
            {
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(ClTextFieldMirrorPatch), nameof(Prefix)));
            }
        }

        public static void Prefix(Window __instance)
        {
            ScreenScope scope = ScreenScopeDrawPatch.For(__instance);
            if (scope is ClTagSelectorScope selector)
            {
                selector.MirrorLive();
            }
            else if (scope is ClStateEditorScope editor)
            {
                editor.MirrorLive();
            }
        }
    }
}
