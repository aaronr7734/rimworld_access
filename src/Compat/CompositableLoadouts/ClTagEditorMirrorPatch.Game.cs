using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The tag editor's name, search and quantity boxes are raw IMGUI fields the shell never sees,
    /// so the live edit buffer is posted into the backing field before each draw, where the mod
    /// renders and filters from it.
    /// </summary>
    internal static class ClTagEditorMirrorPatch
    {
        public static void Prefix(Window __instance)
        {
            ClTagEditorScope.For(__instance)?.MirrorLive();
        }
    }
}
