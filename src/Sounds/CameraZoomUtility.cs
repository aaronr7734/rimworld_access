using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public static class CameraZoomUtility
    {
        private static Camera cachedCamera;

        private const float MinZoomVolume = 0.3f;
        private const float MaxZoomVolume = 1.5f;
        private const float DefaultZoomDistance = 25f;
        private const float MinZoomDistance = 10f;
        private const float MaxZoomDistance = 50f;

        // Optimized: Frame-based cache to avoid repeated lookups
        private static int lastFrameCameraChecked = -1;

        public static float GetZoomVolumeScale()
        {
            if (RimWorldAccessMod_Settings.Settings == null || !RimWorldAccessMod_Settings.Settings.FootstepZoomScaling) return 1f;

            try
            {
                Camera cam = GetCamera();
                if (cam == null) return 1f;

                float zoomDistance = GetCameraZoomDistance(cam);
                
                float normalizedZoom = Mathf.InverseLerp(MinZoomDistance, MaxZoomDistance, zoomDistance);
                
                float volumeScale = Mathf.Lerp(MaxZoomVolume, MinZoomVolume, normalizedZoom);
                
                return Mathf.Clamp(volumeScale, MinZoomVolume, MaxZoomVolume);
            }
            catch
            {
                return 1f;
            }
        }

        private static float GetCameraZoomDistance(Camera cam)
        {
            if (cam == null) return DefaultZoomDistance;

            if (cam.orthographic)
            {
                return cam.orthographicSize * 2f;
            }

            return cam.fieldOfView;
        }

        private static Camera GetCamera()
        {
            // Optimized: Simple check without lock - Unity runs on main thread
            int currentFrame = UnityEngine.Time.frameCount;
            if (cachedCamera != null && lastFrameCameraChecked == currentFrame)
            {
                return cachedCamera;
            }

            cachedCamera = Find.Camera ?? UnityEngine.Camera.main;
            lastFrameCameraChecked = currentFrame;
            return cachedCamera;
        }

        public static void ClearCachedCamera()
        {
            cachedCamera = null;
            lastFrameCameraChecked = -1;
        }
    }
}