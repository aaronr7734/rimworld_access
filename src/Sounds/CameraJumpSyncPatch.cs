using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    // CameraDriver.JumpToCurrentMapLoc mutates rootPos immediately but defers the
    // transform update to the next frame via LongEventHandler.ExecuteWhenFinished.
    // During that gap the audio listener sits at the old position while sustainers
    // sample against the new rootPos, causing a brief cutout. Running the deferred
    // transform update synchronously here closes the gap on the jump frame.
    [HarmonyPatch(typeof(CameraDriver))]
    [HarmonyPatch("JumpToCurrentMapLoc", new[] { typeof(Vector3) })]
    public static class CameraJumpSyncPatch
    {
        private static readonly MethodInfo ApplyPositionToGameObjectMethod =
            AccessTools.Method(typeof(CameraDriver), "ApplyPositionToGameObject");

        [HarmonyPostfix]
        public static void Postfix(CameraDriver __instance)
        {
            ApplyPositionToGameObjectMethod?.Invoke(__instance, null);
        }
    }
}
