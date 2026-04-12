using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(CameraDriver), "ApplyPositionToGameObject")]
    public static class AudioZoomDecouplePatch
    {
        // Matches CameraDriver's closest-zoom Y (sizeRange.min -> Y=15). Pinning here keeps
        // the listener at the loudest, nearest audio profile the game ever produces.
        private const float FixedListenerY = 15f;

        private static GameObject proxyGO;
        private static AudioListener proxyListener;
        private static AudioListener cameraListener;
        private static bool initialized;

        public static float GetFixedListenerY() => FixedListenerY;

        // World-space position of the current audio listener anchor (pawn when camera is
        // following a pawn, camera otherwise). Consumers reading this for pan/distance
        // math should use XZ only; Y is pinned and not meaningful for map-space geometry.
        public static Vector3 GetListenerWorldPosition()
        {
            if (proxyGO != null) return proxyGO.transform.position;
            Camera cam = Find.Camera;
            if (cam == null) return Vector3.zero;
            Vector3 p = cam.transform.position;
            p.y = FixedListenerY;
            return p;
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            Camera cam = Find.Camera;
            if (cam == null) return;

            if (!initialized)
            {
                cameraListener = cam.GetComponent<AudioListener>();
                if (cameraListener != null) cameraListener.enabled = false;

                proxyGO = new GameObject("RimWorldAccess_FixedAudioListener");
                Object.DontDestroyOnLoad(proxyGO);
                proxyListener = proxyGO.AddComponent<AudioListener>();
                initialized = true;
            }

            proxyGO.transform.position = ResolveListenerAnchor(cam);
            proxyGO.transform.rotation = cam.transform.rotation;

            // RimWorld's "OneShotOnCamera" sources are parented to the camera so they sit
            // at the listener's ear. Since we moved the listener off the camera, follow
            // through and reparent those sources to the proxy. Cheap no-op after first run.
            GameObject camContainer = Find.Root?.soundRoot?.sourcePool?.sourcePoolCamera?.cameraSourcesContainer;
            if (camContainer != null && camContainer.transform.parent != proxyGO.transform)
            {
                camContainer.transform.SetParent(proxyGO.transform, worldPositionStays: false);
                camContainer.transform.localPosition = Vector3.zero;
            }
        }

        // When the user is following a specific pawn (camera mode = Pawn), anchor the
        // listener directly on the pawn so audio pans relative to that pawn rather than
        // to the camera (which lags behind pawn motion). Any arrow-key pan flips the
        // mode back to Cursor and the listener falls back to the camera anchor.
        private static Vector3 ResolveListenerAnchor(Camera cam)
        {
            if (MapNavigationState.CurrentCameraMode == CameraFollowMode.Pawn)
            {
                Pawn pawn = Find.Selector?.SingleSelectedThing as Pawn;
                if (pawn != null && pawn.Spawned && pawn.Map == Find.CurrentMap)
                {
                    Vector3 p = pawn.DrawPos;
                    p.y = FixedListenerY;
                    return p;
                }
            }

            Vector3 camPos = cam.transform.position;
            camPos.y = FixedListenerY;
            return camPos;
        }
    }

    [HarmonyPatch(typeof(SoundParamSource_CameraAltitude), "ValueFor")]
    public static class CameraAltitudeDecouplePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref float __result)
        {
            __result = AudioZoomDecouplePatch.GetFixedListenerY();
            return false;
        }
    }
}
