using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(CameraDriver), "ApplyPositionToGameObject")]
    public static class AudioZoomDecouplePatch
    {
        private static GameObject proxyGO;
        private static AudioListener proxyListener;
        private static AudioListener cameraListener;
        private static bool initialized;

        // World-space position of the current audio listener anchor (pawn when camera is
        // following a pawn, camera otherwise). Y tracks the camera altitude so Unity's
        // 3D panner produces the same pan geometry as vanilla.
        public static Vector3 GetListenerWorldPosition()
        {
            if (proxyGO != null) return proxyGO.transform.position;
            Camera cam = Find.Camera;
            if (cam == null) return Vector3.zero;
            return cam.transform.position;
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

        // When following a pawn, anchor XZ on the pawn but keep Y at camera altitude so
        // panning matches what a player would hear from the camera's vantage. Any arrow-key
        // pan flips the mode back to Cursor and the listener falls back to the camera.
        private static Vector3 ResolveListenerAnchor(Camera cam)
        {
            Vector3 camPos = cam.transform.position;
            if (MapNavigationState.CurrentCameraMode == CameraFollowMode.Pawn)
            {
                Pawn pawn = Find.Selector?.SingleSelectedThing as Pawn;
                if (pawn != null && pawn.Spawned && pawn.Map == Find.CurrentMap)
                {
                    Vector3 p = pawn.DrawPos;
                    p.y = camPos.y;
                    return p;
                }
            }
            return camPos;
        }
    }
}
