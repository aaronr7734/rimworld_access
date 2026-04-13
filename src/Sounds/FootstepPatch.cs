using HarmonyLib;
using Verse;
using Verse.AI;

namespace RimWorldAccess
{
    /// <summary>
    /// Event-driven footstep trigger. Fires exactly once when a pawn enters a new tile,
    /// replacing the old polling MapComponent that iterated all pawns every 3 ticks.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_PathFollower))]
    [HarmonyPatch("TryEnterNextPathCell")]
    public static class FootstepPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn ___pawn, out IntVec3 __state)
        {
            __state = ___pawn?.Position ?? IntVec3.Invalid;
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn ___pawn, IntVec3 __state)
        {
            if (___pawn == null || !___pawn.Spawned) return;
            if (___pawn.Position == __state) return;
            if (___pawn.Map != Find.CurrentMap) return;

            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings == null || !settings.EnableSoundEffects || !settings.FootstepsEnabled) return;

            // Cheap race-level short-circuit: if every category for this race is disabled,
            // we never need to touch the pawn again — zero footprint per tile entry.
            bool humanlike = ___pawn.RaceProps?.Humanlike == true;
            bool animal = ___pawn.RaceProps?.Animal == true;
            bool mech = !humanlike && !animal && (___pawn.RaceProps?.IsMechanoid == true ||
                ___pawn.def?.defName?.ToLowerInvariant().Contains("mech") == true);

            if (humanlike && !FootstepClassifier.AnyHumanlikeEnabled) return;
            if (animal && !FootstepClassifier.AnyAnimalEnabled) return;
            if (mech && !FootstepClassifier.AnyMechEnabled) return;
            if (!humanlike && !animal && !mech) return;

            FootstepManager.Instance.ProcessFootstep(___pawn);
        }
    }
}
