using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public readonly struct FootstepSpatialProfile
    {
        public FootstepSpatialProfile(float audibility, float pan, float normalizedDistance, float frontness, float presence, float brightness, float pitchFactor)
        {
            Audibility = audibility;
            Pan = pan;
            NormalizedDistance = normalizedDistance;
            Frontness = frontness;
            Presence = presence;
            Brightness = brightness;
            PitchFactor = pitchFactor;
        }

        public float Audibility { get; }
        public float Pan { get; }
        public float NormalizedDistance { get; }
        public float Frontness { get; }
        public float Presence { get; }
        public float Brightness { get; }
        public float PitchFactor { get; }
        public bool IsNearField => NormalizedDistance <= 0.35f;
    }

    public static class ScreenPanUtility
    {
        private static Camera cachedCamera;
        private const float MaxHearableRangeTiles = 30f;
        private const float NearFieldRangeTiles = 7f;
        private const float MidFieldRangeTiles = 18f;
        private const float StrongPanRangeTiles = 14f;

        // Optimized: Frame-based cache to avoid repeated lookups
        private static int lastFrameCameraChecked = -1;

        public static float CalculatePanForPawn(Pawn pawn)
        {
            FootstepSpatialProfile profile;
            if (!TryGetSpatialProfile(pawn, out profile))
            {
                return 0f;
            }

            return profile.Pan;
        }

        public static float GetAudibilityForPawn(Pawn pawn)
        {
            FootstepSpatialProfile profile;
            return TryGetSpatialProfile(pawn, out profile) ? profile.Audibility : 0f;
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

        public static bool TryGetSpatialProfile(Pawn pawn, out FootstepSpatialProfile profile)
        {
            profile = default;

            if (pawn == null) return false;

            Vector3 viewportPosition;
            if (!TryGetViewportPosition(pawn, out viewportPosition))
            {
                return false;
            }

            Vector3 cameraMapPosition;
            if (!TryGetCameraMapPosition(out cameraMapPosition))
            {
                return false;
            }

            float horizontalTileOffset = pawn.DrawPos.x - cameraMapPosition.x;
            float depthTileOffset = pawn.DrawPos.z - cameraMapPosition.z;
            float radialDistance = Mathf.Sqrt((horizontalTileOffset * horizontalTileOffset) + (depthTileOffset * depthTileOffset));

            if (radialDistance > MaxHearableRangeTiles)
            {
                return false;
            }

            float audibility;
            if (radialDistance <= NearFieldRangeTiles)
            {
                audibility = 1f;
            }
            else if (radialDistance <= MidFieldRangeTiles)
            {
                audibility = Mathf.Lerp(1f, 0.45f, Mathf.InverseLerp(NearFieldRangeTiles, MidFieldRangeTiles, radialDistance));
            }
            else
            {
                audibility = Mathf.Lerp(0.45f, 0f, Mathf.InverseLerp(MidFieldRangeTiles, MaxHearableRangeTiles, radialDistance));
            }

            if (audibility <= 0f)
            {
                return false;
            }

            float frontness = 1f - Mathf.Clamp01(viewportPosition.y);
            float normalizedDistance = Mathf.Clamp01(radialDistance / MaxHearableRangeTiles);
            float presence = Mathf.Lerp(0.94f, 1.04f, frontness) * Mathf.Lerp(1f, 0.9f, normalizedDistance);
            float brightness = Mathf.Clamp01(0.55f + (frontness * 0.15f) + ((1f - normalizedDistance) * 0.2f));
            float pitchFactor = Mathf.Lerp(0.85f, 1.0f, Mathf.Clamp01(viewportPosition.y * 2f));
            float normalizedPan = Mathf.Clamp(horizontalTileOffset / StrongPanRangeTiles, -1f, 1f);
            float pan = Mathf.Clamp(Mathf.Sign(normalizedPan) * Mathf.Pow(Mathf.Abs(normalizedPan), 0.8f) * 1.15f, -1f, 1f);

            profile = new FootstepSpatialProfile(
                audibility,
                pan,
                normalizedDistance,
                frontness,
                Mathf.Clamp(presence, 0.88f, 1.06f),
                brightness,
                pitchFactor);

            return true;
        }

        public static void ClearCachedCamera()
        {
            cachedCamera = null;
            lastFrameCameraChecked = -1;
        }

        private static bool TryGetCameraMapPosition(out Vector3 cameraMapPosition)
        {
            cameraMapPosition = Vector3.zero;

            try
            {
                Camera cam = GetCamera();
                if (cam != null)
                {
                    Vector3 worldPos = cam.transform.position;
                    cameraMapPosition = new Vector3(worldPos.x, 0f, worldPos.z);
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                if (Find.CameraDriver != null)
                {
                    IntVec3 mapPosition = Find.CameraDriver.MapPosition;
                    cameraMapPosition = new Vector3(mapPosition.x, 0f, mapPosition.z);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryGetViewportPosition(Pawn pawn, out Vector3 viewportPosition)
        {
            viewportPosition = Vector3.zero;
            if (pawn == null) return false;

            try
            {
                Camera cam = GetCamera();
                if (cam == null) return false;

                viewportPosition = cam.WorldToViewportPoint(pawn.DrawPos);
                return viewportPosition.z > 0f;
            }
            catch
            {
                viewportPosition = Vector3.zero;
                return false;
            }
        }
    }
}