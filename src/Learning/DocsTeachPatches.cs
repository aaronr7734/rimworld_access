using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Contextual teaching triggers that fire on game events rather than on one of our own UI
    /// states opening. Kept beside <see cref="DocsTeacher"/> so the teaching layer owns all its
    /// triggers. The guiding rule: teach a feature BEFORE the player would need it, not as they
    /// use it (by which point they have already discovered it).
    /// </summary>
    [HarmonyPatch(typeof(ZoneManager), "RegisterZone")]
    public static class ZoneCreatedTeachPatch
    {
        // Making your first zone (typically a growing zone) is the moment you need a gizmo — to
        // set what to grow — yet have not opened one. Teach gizmos now, before the player goes
        // hunting for how to configure what they just placed. Gated to Playing so it does not
        // fire while zones are re-registered during save load or map generation.
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (Current.ProgramState != ProgramState.Playing)
                return;

            DocsTeacher.Teach("RWA_Gizmos");
        }
    }

    /// <summary>
    /// Teaches commanding several colonists at once the first time the map comes under threat —
    /// before the player is mid-fight wishing they could move the whole group together. Uses the
    /// game's own danger signal so it covers raids, manhunters, and any other hostile presence.
    /// </summary>
    [HarmonyPatch(typeof(DangerWatcher), "DangerRating", MethodType.Getter)]
    public static class DangerDetectedTeachPatch
    {
        [HarmonyPostfix]
        public static void Postfix(StoryDanger __result)
        {
            if (Current.ProgramState != ProgramState.Playing)
                return;
            if (__result == StoryDanger.None)
                return;

            DocsTeacher.Teach("RWA_MultiSelect");
        }
    }
}
