using HarmonyLib;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>Every one-shot sound funnels through PlayOneShot, including PlayOneShotOnCamera.</summary>
    [HarmonyPatch(typeof(SoundStarter), nameof(SoundStarter.PlayOneShot))]
    internal static class FlightRecorderSoundPatch
    {
        [HarmonyPrefix]
        public static void Prefix(SoundDef soundDef)
        {
            FlightRecorder.RecordSound(soundDef);
        }
    }
}
