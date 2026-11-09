using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(StartingPawnUtility))]
    [HarmonyPatch("RandomizePawn")]
    public static class PawnFilterRandomizePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(int pawnIndex)
        {
            if (!PawnFilterData.HasActiveFilters())
                return true;

            if (!TutorSystem.AllowAction("RandomizePawn"))
                return false;

            RerollState.Start(pawnIndex);
            return false;
        }
    }
}
