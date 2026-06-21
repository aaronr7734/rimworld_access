#if DEBUG
using HarmonyLib;
using Verse;

namespace RimWorldAccess.DevBridge
{
    /// <summary>
    /// Drives the dev bridge from RimWorld's main-thread render loop. UIRootOnGUI runs every
    /// frame at both the main menu and in-game, so this both lazily starts the server and
    /// drains any eval work the HTTP thread has queued. Debug builds only.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot), "UIRootOnGUI")]
    internal static class DevBridgeDrainPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DevBridgeServer.EnsureStarted();
            MainThreadDispatcher.DrainPending();
        }
    }
}
#endif
