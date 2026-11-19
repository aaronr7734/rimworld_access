#if DEBUG
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess.DevBridge
{
    /// <summary>Starts the dev bridge and drains queued eval work on the main thread. Debug builds only.</summary>
    [HarmonyPatch(typeof(UIRoot), "UIRootOnGUI")]
    internal static class DevBridgeDrainPatch
    {
        internal static int LastGuiFrame = -1;

        [HarmonyPostfix]
        public static void Postfix()
        {
            LastGuiFrame = Time.frameCount;
            DevBridgeServer.EnsureStarted();
            MainThreadDispatcher.DrainPending();

            // After this pass's /inject arms ran, so "wait N" means N real frames (ShellDev.Sequence.Game.cs).
            RimWorldAccess.Shell.ShellDev.AdvanceSequence();
        }
    }

    /// <summary>
    /// 1.7 skips OnGUI while the window is occluded but still runs Update, so the bridge drains here
    /// when no GUI pass ran last frame (Update precedes OnGUI). Such evals have no Event.current.
    /// </summary>
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    internal static class DevBridgeUpdateDrainPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (DevBridgeServer.RecentlyActive)
                RimWorldAccess.Shell.ShellDev.KeepDrawingInBackground();

            if (Time.frameCount - DevBridgeDrainPatch.LastGuiFrame <= 1)
                return;

            DevBridgeServer.EnsureStarted();
            MainThreadDispatcher.DrainPending();
        }
    }
}
#endif
