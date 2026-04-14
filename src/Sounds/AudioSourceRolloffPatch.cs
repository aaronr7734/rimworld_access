using HarmonyLib;
using UnityEngine;
using Verse.Sound;

namespace RimWorldAccess
{
    // RimWorld uses linear rolloff with per-SubSoundDef distRange (typically tuned for
    // close play). With our AudioListener tracking the real camera altitude (up to Y=65),
    // spatialized sounds with small distRanges become inaudible at zoom-out. We extend
    // each source's rolloff to cover the full camera altitude while preserving the
    // min-to-max ratio so the curve keeps its shape.
    internal static class RolloffExtender
    {
        // Covers CameraDriver.MaxAltitude (65, private const) with headroom.
        private const float AltitudeCeiling = 70f;

        public static void Extend(AudioSource source)
        {
            if (source == null) return;
            float originalMax = source.maxDistance;
            if (originalMax <= 0f) return;
            float ceiling = Mathf.Max(originalMax, AltitudeCeiling + originalMax * 0.25f);
            if (ceiling <= originalMax) return;
            float scale = ceiling / originalMax;
            source.maxDistance = ceiling;
            source.minDistance *= scale;
        }
    }

    [HarmonyPatch(typeof(SampleSustainer), "TryMakeAndPlay")]
    public static class SampleSustainer_TryMakeAndPlay_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(SampleSustainer __result)
        {
            if (__result == null) return;
            RolloffExtender.Extend(__result.source);
        }
    }

    [HarmonyPatch(typeof(SampleOneShot), "TryMakeAndPlay")]
    public static class SampleOneShot_TryMakeAndPlay_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(SampleOneShot __result, SubSoundDef def)
        {
            if (__result == null || def == null) return;
            if (def.onCamera) return;
            RolloffExtender.Extend(__result.source);
        }
    }
}
