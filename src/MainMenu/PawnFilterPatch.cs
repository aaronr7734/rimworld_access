using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(StartingPawnUtility))]
    [HarmonyPatch("RandomizePawn")]
    public static class PawnFilterRandomizePatch
    {
        private static MethodInfo randomAgeMethodInfo;
        private static MethodInfo randomTraitMethodInfo;
        private static MethodInfo randomSkillMethodInfo;
        private static MethodInfo randomHealthMethodInfo;
        private static MethodInfo randomBodyTypeMethodInfo;
        private static MethodInfo randomGeneMethodInfo;
        private static bool reflectionInitialized = false;
        private static bool reflectionAvailable = false;

        private static void InitializeReflection()
        {
            if (reflectionInitialized) return;
            reflectionInitialized = true;

            try
            {
                randomAgeMethodInfo = typeof(PawnGenerator)
                    .GetMethod("GenerateRandomAge", BindingFlags.NonPublic | BindingFlags.Static);
                randomTraitMethodInfo = typeof(PawnGenerator)
                    .GetMethod("GenerateTraits", BindingFlags.NonPublic | BindingFlags.Static);
                randomSkillMethodInfo = typeof(PawnGenerator)
                    .GetMethod("GenerateSkills", BindingFlags.NonPublic | BindingFlags.Static);
                randomHealthMethodInfo = typeof(PawnGenerator)
                    .GetMethod("GenerateInitialHediffs", BindingFlags.NonPublic | BindingFlags.Static);
                randomBodyTypeMethodInfo = typeof(PawnGenerator)
                    .GetMethod("GenerateBodyType", BindingFlags.NonPublic | BindingFlags.Static);
                randomGeneMethodInfo = typeof(PawnGenerator)
                    .GetMethod("GenerateGenes", BindingFlags.NonPublic | BindingFlags.Static);

                reflectionAvailable = randomAgeMethodInfo != null
                    && randomTraitMethodInfo != null
                    && randomSkillMethodInfo != null
                    && randomHealthMethodInfo != null
                    && randomBodyTypeMethodInfo != null;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Failed to initialize fast reroll reflection: {ex.Message}");
                reflectionAvailable = false;
            }
        }

        [HarmonyPrefix]
        public static bool Prefix(int pawnIndex)
        {
            if (!PawnFilterData.HasActiveFilters())
                return true;

            if (!TutorSystem.AllowAction("RandomizePawn"))
                return false;

            var filter = PawnFilterData.ActiveFilter;

            if (filter.Algorithm == RerollAlgorithm.Fast)
            {
                InitializeReflection();
                if (reflectionAvailable)
                {
                    FastReroll(pawnIndex, filter);
                    return false;
                }
                else
                {
                    Log.Warning("[RimWorld Access] Fast reroll reflection unavailable, falling back to normal algorithm.");
                }
            }

            NormalReroll(pawnIndex, filter);
            return false;
        }

        private static void NormalReroll(int pawnIndex, PawnFilter filter)
        {
            var pawns = Find.GameInitData.startingAndOptionalPawns;
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
                    return;
                }
            }
            while (attempts < limit);

            // Limit reached — keep last generated pawn
            TutorSystem.Notify_Event("RandomizePawn");
            PawnFilterData.LastRerollAttempts = attempts;
            PawnFilterData.LastRerollSucceeded = false;
        }

        private static void FastReroll(int pawnIndex, PawnFilter filter)
        {
            var pawns = Find.GameInitData.startingAndOptionalPawns;
            Pawn pawn = pawns[pawnIndex];
            int limit = filter.RerollLimit;
            int attempts = 0;
            var stopwatch = Stopwatch.StartNew();
            long nextAnnounceMs = 1500;

            // Initial randomize to set baseline
            SpouseRelationUtility.Notify_PawnRegenerated(pawn);
            pawn = StartingPawnUtility.RandomizeInPlace(pawn);
            attempts++;

            if (filter.Evaluate(pawn) && StartingPawnUtility.WorkTypeRequirementsSatisfied())
            {
                TutorSystem.Notify_Event("RandomizePawn");
                PawnFilterData.LastRerollAttempts = attempts;
                PawnFilterData.LastRerollSucceeded = true;
                return;
            }

            PawnGenerationRequest request = StartingPawnUtility.GetGenerationRequest(pawnIndex);
            request.ValidateAndFix();

            Faction faction;
            Faction resolvedFaction = request.Faction ??
                (!Find.FactionManager.TryGetRandomNonColonyHumanlikeFaction(out faction, false, true)
                    ? Faction.OfAncients : faction);

            XenotypeDef xenotype = ModsConfig.BiotechActive ? PawnGenerator.GetXenotypeForGeneratedPawn(request) : null;

            while (attempts < limit)
            {
                try
                {
                    attempts++;

                    if (stopwatch.ElapsedMilliseconds >= nextAnnounceMs)
                    {
                        TolkHelper.Speak($"Searching, {attempts} attempts...", SpeechPriority.High);
                        nextAnnounceMs = stopwatch.ElapsedMilliseconds + 2000;
                    }

                    PawnGenerator.RedressPawn(pawn, request);

                    // Generate age
                    pawn.ageTracker = new Pawn_AgeTracker(pawn);
                    randomAgeMethodInfo.Invoke(null, new object[] { pawn, request });

                    if (!filter.CheckAgeForFastReroll(pawn))
                        continue;

                    // Generate traits and skills
                    pawn.story.traits = new TraitSet(pawn);
                    pawn.skills = new Pawn_SkillTracker(pawn);
                    PawnBioAndNameGenerator.GiveAppropriateBioAndNameTo(pawn, resolvedFaction.def, request, xenotype);
                    randomTraitMethodInfo.Invoke(null, new object[] { pawn, request });
                    randomSkillMethodInfo.Invoke(null, new object[] { pawn, request });

                    if (!filter.CheckSkillsAndTraitsForFastReroll(pawn))
                        continue;

                    // Generate health
                    bool healthGenSuccess = false;
                    for (int i = 0; i < 100 && !healthGenSuccess; i++)
                    {
                        pawn.health.Reset();
                        try
                        {
                            Find.Scenario.Notify_NewPawnGenerating(pawn, request.Context);
                            randomHealthMethodInfo.Invoke(null, new object[] { pawn, request });

                            if (!(pawn.Dead || pawn.Destroyed || pawn.Downed))
                                healthGenSuccess = true;
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    if (!filter.CheckHealthForFastReroll(pawn))
                        continue;

                    pawn.workSettings?.EnableAndInitialize();
                    if (!filter.CheckWorkForFastReroll(pawn))
                        continue;

                    // Scenario traits
                    Find.Scenario.Notify_PawnGenerated(pawn, request.Context, true);
                    if (!filter.Evaluate(pawn))
                        continue;

                    // Generate genes and body type
                    if (ModsConfig.BiotechActive && randomGeneMethodInfo != null)
                    {
                        pawn.genes = new Pawn_GeneTracker(pawn);
                        randomGeneMethodInfo.Invoke(null, new object[] { pawn, xenotype, request });
                    }

                    randomBodyTypeMethodInfo.Invoke(null, new object[] { pawn, request });

                    TutorSystem.Notify_Event("RandomizePawn");
                    PawnFilterData.LastRerollAttempts = attempts;
                    PawnFilterData.LastRerollSucceeded = true;
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimWorld Access] Error during fast pawn generation (attempt {attempts}): {ex.Message}");
                    try
                    {
                        SpouseRelationUtility.Notify_PawnRegenerated(pawn);
                        pawn = StartingPawnUtility.RandomizeInPlace(pawn);
                    }
                    catch (Exception ex2)
                    {
                        Log.Error($"[RimWorld Access] Critical error in pawn cleanup: {ex2.Message}");
                        break;
                    }
                }
            }

            // Limit reached
            TutorSystem.Notify_Event("RandomizePawn");
            PawnFilterData.LastRerollAttempts = attempts;
            PawnFilterData.LastRerollSucceeded = false;
        }
    }
}
