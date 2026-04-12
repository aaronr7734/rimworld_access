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

            Vector3 p = cam.transform.position;
            p.y = FixedListenerY;
            proxyGO.transform.position = p;
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
