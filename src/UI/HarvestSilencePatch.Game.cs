using HarmonyLib;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// A menu harvest runs a mod's or vanilla's click path only to read the menu it would open, so
    /// nothing it does may be heard: FloatMenu's constructor plays its open sound before the
    /// window is intercepted, and a probed ProcessInput plays the command's click.
    /// </summary>
    [HarmonyPatch(typeof(SoundStarter), nameof(SoundStarter.PlayOneShot))]
    internal static class HarvestSilencePatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !FloatMenuHarvest.InFlight && !MaterialMenuHarvest.InFlight;
        }
    }
}
