using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public static class RerollState
    {
        private static bool isActive;
        private static int pawnIndex;
        private static PawnFilter filter;
        private static Pawn pawn;
        private static int attempts;
        private static int limit;
        private static PawnGenerationRequest request;
        private static Faction resolvedFaction;
        private static XenotypeDef xenotype;
        private static Stopwatch stopwatch;
        private static long nextAnnounceMs;
        private static bool needsHealthCheck;
        private static bool needsWorkCheck;

        private static MethodInfo randomAgeMethodInfo;
        private static MethodInfo randomTraitMethodInfo;
        private static MethodInfo randomSkillMethodInfo;
        private static MethodInfo randomHealthMethodInfo;
        private static MethodInfo randomBodyTypeMethodInfo;
        private static MethodInfo randomGeneMethodInfo;
        private static bool reflectionInitialized;
        private static bool reflectionAvailable;

        public static bool IsActive => isActive;

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
                Log.Warning($"[RimWorld Access] Failed to initialize reroll reflection: {ex.Message}");
                reflectionAvailable = false;
            }
        }

        public static void Start(int index)
        {
            InitializeReflection();

            pawnIndex = index;
            filter = PawnFilterData.ActiveFilter;
            limit = filter.RerollLimit;
            attempts = 0;
            needsHealthCheck = filter.Health != HealthFilterMode.AllowAll;
            needsWorkCheck = filter.Work != WorkFilterMode.AllowAll;

            var pawns = Find.GameInitData.startingAndOptionalPawns;
            pawn = pawns[pawnIndex];

            // Initial full randomize (attempt 1)
            SpouseRelationUtility.Notify_PawnRegenerated(pawn);
            pawn = StartingPawnUtility.RandomizeInPlace(pawn);
            attempts++;

            if (filter.Evaluate(pawn) && StartingPawnUtility.WorkTypeRequirementsSatisfied())
            {
                PawnFilterData.LastRerollAttempts = attempts;
                PawnFilterData.LastRerollSucceeded = true;
                TutorSystem.Notify_Event("RandomizePawn");
                StartingPawnState.OnRerollComplete(true, attempts, false);
                return;
            }

            if (!reflectionAvailable)
            {
                // Fallback: synchronous simple reroll without reflection optimization
                FallbackReroll();
                return;
            }

            // Set up cached state for fast reroll
            request = StartingPawnUtility.GetGenerationRequest(pawnIndex);
            request.ValidateAndFix();

            if (filter.Gender.HasValue)
                pawn.gender = filter.Gender.Value;

            Faction faction;
            resolvedFaction = request.Faction ??
                (!Find.FactionManager.TryGetRandomNonColonyHumanlikeFaction(out faction, false, true)
                    ? Faction.OfAncients : faction);

            xenotype = ModsConfig.BiotechActive ? PawnGenerator.GetXenotypeForGeneratedPawn(request) : null;

            isActive = true;
            stopwatch = Stopwatch.StartNew();
            nextAnnounceMs = 1500;
            // Announcement removed — Escape blocking in UnifiedKeyboardPatch handles cancellation silently
        }

        public static void ProcessBatch()
        {
            if (!isActive) return;

            var batchStopwatch = Stopwatch.StartNew();
            const long batchTimeMs = 10; // yield after ~10ms to keep UI responsive

            while (attempts < limit && batchStopwatch.ElapsedMilliseconds < batchTimeMs)
            {
                try
                {
                    attempts++;

                    // Generate age (skip RedressPawn — only appearance/gear)
                    // Reuse existing age tracker to avoid LifeStageWorker crash mid-game
                    // (Notify_LifeStageStarted accesses pawn.Drawer which is null for unspawned pawns)
                    randomAgeMethodInfo.Invoke(null, new object[] { pawn, request });

                    if (!filter.CheckAgeForFastReroll(pawn))
                        continue;

                    // Generate backstory, traits, skills
                    pawn.story.traits = new TraitSet(pawn);
                    pawn.skills = new Pawn_SkillTracker(pawn);
                    PawnBioAndNameGenerator.GiveAppropriateBioAndNameTo(pawn, resolvedFaction.def, request, xenotype);
                    randomTraitMethodInfo.Invoke(null, new object[] { pawn, request });
                    randomSkillMethodInfo.Invoke(null, new object[] { pawn, request });

                    if (!filter.CheckSkillsAndTraitsForFastReroll(pawn))
                        continue;

                    // Generate health — only if health filter is active
                    if (needsHealthCheck)
                    {
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
                    }

                    // Work check — only if work filter is active
                    if (needsWorkCheck)
                    {
                        pawn.workSettings?.EnableAndInitialize();
                        if (!filter.CheckWorkForFastReroll(pawn))
                            continue;
                    }

                    // Scenario traits (may add forced traits)
                    Find.Scenario.Notify_PawnGenerated(pawn, request.Context, true);
                    if (!filter.Evaluate(pawn))
                        continue;

                    // Match found! Finalize appearance.
                    FinalizeMatch();
                    return;
                }
                catch (Exception ex)
                {
                    var inner = ex is System.Reflection.TargetInvocationException tie ? tie.InnerException : ex;
                    Log.Warning($"[RimWorld Access] Error during reroll (attempt {attempts}): {inner?.Message ?? ex.Message}");
                    try
                    {
                        SpouseRelationUtility.Notify_PawnRegenerated(pawn);
                        pawn = StartingPawnUtility.RandomizeInPlace(pawn);
                    }
                    catch (Exception ex2)
                    {
                        Log.Error($"[RimWorld Access] Critical error in pawn cleanup: {ex2.Message}");
                        Finalize(false, false);
                        return;
                    }
                }
            }

            // Check if limit reached
            if (attempts >= limit)
            {
                Finalize(false, false);
                return;
            }

            // Progress announcement
            if (stopwatch.ElapsedMilliseconds >= nextAnnounceMs)
            {
                TolkHelper.Speak("RimWorldAccess.Reroll.SearchingAttempts".Loc(attempts), SpeechPriority.High);
                nextAnnounceMs = stopwatch.ElapsedMilliseconds + 2000;
            }
        }

        public static void Cancel()
        {
            if (!isActive) return;
            Finalize(false, true);
        }

        private static void FinalizeMatch()
        {
            // Redress pawn once to set gear/clothing matching final stats
            PawnGenerator.RedressPawn(pawn, request);

            // Generate genes and body type (deferred expensive ops)
            if (ModsConfig.BiotechActive && randomGeneMethodInfo != null)
            {
                pawn.genes = new Pawn_GeneTracker(pawn);
                randomGeneMethodInfo.Invoke(null, new object[] { pawn, xenotype, request });
            }

            randomBodyTypeMethodInfo.Invoke(null, new object[] { pawn, request });

            Finalize(true, false);
        }

        private static void Finalize(bool success, bool cancelled)
        {
            isActive = false;
            PawnFilterData.LastRerollAttempts = attempts;
            PawnFilterData.LastRerollSucceeded = success;
            TutorSystem.Notify_Event("RandomizePawn");
            StartingPawnState.OnRerollComplete(success, attempts, cancelled);
        }

        private static void FallbackReroll()
        {
            // Simple fallback when reflection is unavailable — synchronous full-pawn regeneration
            var pawns = Find.GameInitData.startingAndOptionalPawns;

            while (attempts < limit)
            {
                pawn = pawns[pawnIndex];
                SpouseRelationUtility.Notify_PawnRegenerated(pawn);
                StartingPawnUtility.RandomizeInPlace(pawn);
                attempts++;

                Pawn newPawn = pawns[pawnIndex];
                if (filter.Evaluate(newPawn) && StartingPawnUtility.WorkTypeRequirementsSatisfied())
                {
                    PawnFilterData.LastRerollAttempts = attempts;
                    PawnFilterData.LastRerollSucceeded = true;
                    TutorSystem.Notify_Event("RandomizePawn");
                    StartingPawnState.OnRerollComplete(true, attempts, false);
                    return;
                }
            }

            PawnFilterData.LastRerollAttempts = attempts;
            PawnFilterData.LastRerollSucceeded = false;
            TutorSystem.Notify_Event("RandomizePawn");
            StartingPawnState.OnRerollComplete(false, attempts, false);
        }
    }
}
