using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Watches every Window close to notify WindowlessSaveMenuState when a mod- or
    /// version-mismatch dialog (queued by CheckVersionAndLoadGame) resolves. If the
    /// user backed out without loading, the save/load menu reactivates itself where
    /// it left off; if the load actually fired, the menu fully closes.
    ///
    /// Hooking Window.PostClose rather than per-dialog classes covers both
    /// Dialog_ModMismatch and the Dialog_MessageBox variant used for version
    /// mismatches, and remains correct if vanilla ever adds a third compat dialog.
    /// </summary>
    [HarmonyPatch(typeof(Window), "PostClose")]
    public static class LoadDialogWatcherPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Window __instance)
        {
            WindowlessSaveMenuState.OnWindowClosed(__instance);
        }
    }
}
