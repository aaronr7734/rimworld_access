using System.Diagnostics;
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
        public static bool Prefix(int pawnIndex)
        {
            if (!PawnFilterData.HasActiveFilters())
                return true;

            if (!TutorSystem.AllowAction("RandomizePawn"))
                return false;

            var pawns = Find.GameInitData.startingAndOptionalPawns;
            var filter = PawnFilterData.ActiveFilter;
            int limit = filter.RerollLimit;
            int attempts = 0;
            var stopwatch = Stopwatch.StartNew();
            long nextAnnounceMs = 1500;

            do
            {
                Pawn pawn = pawns[pawnIndex];
                SpouseRelationUtility.Notify_PawnRegenerated(pawn);
                StartingPawnUtility.RandomizeInPlace(pawn);
                attempts++;

                if (stopwatch.ElapsedMilliseconds >= nextAnnounceMs)
                {
                    TolkHelper.Speak($"Searching, {attempts} attempts...", SpeechPriority.High);
                    nextAnnounceMs = stopwatch.ElapsedMilliseconds + 2000;
                }

                Pawn newPawn = pawns[pawnIndex];
                if (filter.Evaluate(newPawn) && StartingPawnUtility.WorkTypeRequirementsSatisfied())
                {
                    TutorSystem.Notify_Event("RandomizePawn");
                    PawnFilterData.LastRerollAttempts = attempts;
                    PawnFilterData.LastRerollSucceeded = true;
                    return false;
                }
            }
            while (attempts < limit);

            // Limit reached — keep last generated pawn
            TutorSystem.Notify_Event("RandomizePawn");
            PawnFilterData.LastRerollAttempts = attempts;
            PawnFilterData.LastRerollSucceeded = false;
            return false;
        }
    }
}
