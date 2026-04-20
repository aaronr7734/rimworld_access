using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(Dialog_ChooseNewWanderers))]
    [HarmonyPatch("PreOpen")]
    public static class WandererPreOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Dialog_ChooseNewWanderers __instance)
        {
            try
            {
                // Close the notification menu if it was open (user activated letter button)
                if (NotificationMenuState.IsActive)
                    NotificationMenuState.Close();

                WandererPatch.SetInstance(__instance);
                PawnFilterData.Initialize();
                StartingPawnState.Open(PawnEditorContext.Wanderer);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in WandererPreOpenPatch: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Dialog_ChooseNewWanderers))]
    [HarmonyPatch("PostClose")]
    public static class WandererPostClosePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Dialog_ChooseNewWanderers __instance)
        {
            try
            {
                StartingPawnState.Close();
                WandererPatch.SetInstance(null);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in WandererPostClosePatch: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Dialog_ChooseNewWanderers))]
    [HarmonyPatch("DoWindowContents")]
    public static class WandererDoWindowContentsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Dialog_ChooseNewWanderers __instance)
        {
            try
            {
                // Process reroll batch each frame
                RerollState.ProcessBatch();

                if (!StartingPawnState.IsActive) return;

                StartingPawnState.CheckPendingRenameRebuild();

                int pawnIdx = StartingPawnState.GetSelectedPawnIndex();
                AccessTools.Field(typeof(Dialog_ChooseNewWanderers), "curPawnIndex")
                    .SetValue(__instance, pawnIdx);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in WandererDoWindowContentsPatch: {ex}");
            }
        }
    }

    /// <summary>
    /// Block Escape from closing the wanderer dialog when our state is active.
    /// RimWorld's Window.OnCancelKeyPressed runs independently of our keyboard handler.
    /// </summary>
    [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
    public static class WandererCancelBlockPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Window __instance)
        {
            if (__instance is Dialog_ChooseNewWanderers && StartingPawnState.IsActive)
                return false;
            return true;
        }
    }

    /// <summary>
    /// Block Enter from closing the wanderer dialog when our state is active.
    /// Window's default OnAcceptKeyPressed closes the dialog — we handle Enter ourselves.
    /// </summary>
    [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
    public static class WandererAcceptBlockPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Window __instance)
        {
            if (__instance is Dialog_ChooseNewWanderers && StartingPawnState.IsActive)
                return false;
            return true;
        }
    }

    public static class WandererPatch
    {
        private static Dialog_ChooseNewWanderers instance;
        private static FieldInfo generationIndexField;
        private static PropertyInfo defaultRequestProperty;

        public static void SetInstance(Dialog_ChooseNewWanderers dialog)
        {
            instance = dialog;
        }

        public static Dialog_ChooseNewWanderers GetInstance()
        {
            return instance;
        }

        public static void ConfirmWanderers()
        {
            if (instance == null) return;

            try
            {
                var parms = new IncidentParms();
                parms.target = Find.AnyPlayerHomeMap;
                parms.forced = true;
                Find.Storyteller.TryFire(new FiringIncident(
                    IncidentDefOf.GameEndedWanderersJoin, null, parms));

                Find.LetterStack.RemoveLetter(
                    Find.LetterStack.LettersListForReading.Find(
                        letter => letter.def == LetterDefOf.GameEnded));

                Find.GameEnder.gameEnding = false;
                Find.GameEnder.newWanderersCreatedTick = Find.TickManager.TicksGame;
                instance.Close();
                Current.Game.InitData = null;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error confirming wanderers: {ex}");
            }
        }

        public static void CloseDialog()
        {
            if (instance == null) return;
            instance.Close();
        }

        public static void AddPawn()
        {
            if (instance == null) return;

            try
            {
                if (generationIndexField == null)
                    generationIndexField = AccessTools.Field(typeof(Dialog_ChooseNewWanderers), "generationIndex");
                if (defaultRequestProperty == null)
                    defaultRequestProperty = AccessTools.Property(typeof(Dialog_ChooseNewWanderers), "DefaultStartingPawnRequest");

                Find.GameInitData.startingPawnCount++;

                int genIdx = (int)generationIndexField.GetValue(instance);
                var request = (PawnGenerationRequest)defaultRequestProperty.GetValue(null);
                StartingPawnUtility.SetGenerationRequest(genIdx, request);
                StartingPawnUtility.AddNewPawn(genIdx);
                genIdx++;
                generationIndexField.SetValue(instance, genIdx);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error adding wanderer pawn: {ex}");
            }
        }

        public static void RemovePawn(int pawnIndex)
        {
            var pawns = Find.GameInitData.startingAndOptionalPawns;
            if (pawnIndex < 0 || pawnIndex >= pawns.Count) return;

            Find.GameInitData.startingPawnCount--;
            pawns.RemoveAt(pawnIndex);
        }
    }
}
