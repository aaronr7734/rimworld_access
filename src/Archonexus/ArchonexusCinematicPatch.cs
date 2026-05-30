using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Announces the Archonexus settlement transition cinematic
    /// (Screen_ArchonexusSettlementCinematics) when it opens. The vanilla screen
    /// auto-advances on a timer with no input or close button, so a blind player
    /// would otherwise hear nothing during the transition. We speak the same
    /// "SoldColonyDescription" text the cinematic shows visually.
    /// </summary>
    [HarmonyPatch(typeof(Window), "PostOpen")]
    public static class ArchonexusCinematicPatch_PostOpen
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is global::Screen_ArchonexusSettlementCinematics)
            {
                TolkHelper.Speak("SoldColonyDescription".Translate(), SpeechPriority.High);
            }
        }
    }
}
