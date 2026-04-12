using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    public static class CameraZoomUtility
    {
        // Occlusion fades from full strength at/under Close tier to zero at the Middle/Far boundary.
        private const float OcclusionFullRootSize = 13.8f;
        private const float OcclusionNoneRootSize = 42f;

        public static float GetOcclusionStrength()
        {
            // Setting off = no occlusion at all (raw value ignored via Lerp(1, raw, 0) = 1).
            if (RimWorldAccessMod_Settings.Settings == null || !RimWorldAccessMod_Settings.Settings.FootstepZoomScaling) return 0f;

            CameraDriver driver = Find.CameraDriver;
            if (driver == null) return 1f;

            float t = Mathf.InverseLerp(OcclusionFullRootSize, OcclusionNoneRootSize, driver.RootSize);
            return 1f - Mathf.Clamp01(t);
        }

        private const float ZoomStep = 4f;
        private static Verse.CameraZoomRange? lastAnnouncedZoom;

        public static void ZoomIn()
        {
            AdjustZoom(-ZoomStep, "in");
        }

        public static void ZoomOut()
        {
            AdjustZoom(ZoomStep, "out");
        }

        private static void AdjustZoom(float delta, string directionWord)
        {
            CameraDriver driver = Find.CameraDriver;
            if (driver == null) return;

            var range = driver.config.sizeRange;
            float previousSize = driver.RootSize;
            float newSize = Mathf.Clamp(previousSize + delta, range.min, range.max);

            if (Mathf.Approximately(newSize, previousSize))
            {
                TolkHelper.Speak("Zoom limit", SpeechPriority.Low);
                return;
            }

            driver.SetRootSize(newSize);
            SoundDefOf.DragSlider?.PlayOneShotOnCamera();

            Verse.CameraZoomRange currentZoom = driver.CurrentZoom;
            if (lastAnnouncedZoom != currentZoom)
            {
                lastAnnouncedZoom = currentZoom;
                TolkHelper.Speak(GetZoomLabel(currentZoom), SpeechPriority.Low);
            }
        }

        private static string GetZoomLabel(Verse.CameraZoomRange zoom)
        {
            switch (zoom)
            {
                case Verse.CameraZoomRange.Closest: return "Zoom: closest";
                case Verse.CameraZoomRange.Close: return "Zoom: close";
                case Verse.CameraZoomRange.Middle: return "Zoom: middle";
                case Verse.CameraZoomRange.Far: return "Zoom: far";
                case Verse.CameraZoomRange.Furthest: return "Zoom: furthest";
                default: return "Zoom changed";
            }
        }
    }
}