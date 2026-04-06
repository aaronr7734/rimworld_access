using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper class for training tab data extraction and interactive actions.
    /// Provides methods for training state, master assignment, and behavior toggles.
    /// </summary>
    public static class TrainingTabHelper
    {
        /// <summary>
        /// Represents the training state of a single trainable skill.
        /// </summary>
        public class TrainableInfo
        {
            public TrainableDef Def { get; set; }
            public bool IsWanted { get; set; }
            public bool IsLearned { get; set; }
            public int CurrentSteps { get; set; }
            public int TotalSteps { get; set; }
            public bool CanTrain { get; set; }
            public string DisabledReason { get; set; }
        }

        /// <summary>
        /// Represents a potential master candidate for an animal.
        /// </summary>
        public class MasterCandidate
        {
            public Pawn Colonist { get; set; }
            public string Label { get; set; }
            public bool CanBeMaster { get; set; }
            public string DisabledReason { get; set; }
            public bool IsCurrent { get; set; }
        }

        private static readonly MethodInfo getStepsMethod =
            AccessTools.Method(typeof(Pawn_TrainingTracker), "GetSteps");

        /// <summary>
        /// Gets the current training steps for a trainable via reflection (internal method).
        /// </summary>
        public static int GetSteps(Pawn pawn, TrainableDef td)
        {
            if (pawn?.training == null || getStepsMethod == null)
                return 0;

            return (int)getStepsMethod.Invoke(pawn.training, new object[] { td });
        }

        /// <summary>
        /// Gets info for all visible trainable skills for a pawn.
        /// </summary>
        public static List<TrainableInfo> GetTrainableInfos(Pawn pawn)
        {
            var result = new List<TrainableInfo>();
            if (pawn?.training == null)
                return result;

            foreach (var td in TrainableUtility.TrainableDefsInListOrder)
            {
                bool visible;
                AcceptanceReport canTrain = pawn.training.CanAssignToTrain(td, out visible);
                if (!visible)
                    continue;

                result.Add(new TrainableInfo
                {
                    Def = td,
                    IsWanted = pawn.training.GetWanted(td),
                    IsLearned = pawn.training.HasLearned(td),
                    CurrentSteps = GetSteps(pawn, td),
                    TotalSteps = td.steps,
                    CanTrain = canTrain.Accepted,
                    DisabledReason = canTrain.Reason
                });
            }

            return result;
        }

        /// <summary>
        /// Builds the full description text for a trainable, matching vanilla DoTrainableTooltip.
        /// Includes description and prerequisite warnings.
        /// </summary>
        public static string GetTrainableDescription(Pawn pawn, TrainableInfo info)
        {
            string text = info.Def.description;
            if (!info.CanTrain && !string.IsNullOrEmpty(info.DisabledReason))
            {
                text += "\n\n" + info.DisabledReason;
            }
            else if (info.Def.prerequisites != null)
            {
                bool hasUnlearned = false;
                foreach (var prereq in info.Def.prerequisites)
                {
                    if (!pawn.training.HasLearned(prereq))
                    {
                        if (!hasUnlearned)
                        {
                            text += "\n";
                            hasUnlearned = true;
                        }
                        text += "\n" + "TrainingNeedsPrerequisite".Translate(prereq.LabelCap);
                    }
                }
            }
            return text;
        }

        /// <summary>
        /// Gets the list of potential master candidates for an animal.
        /// Mirrors vanilla MasterSelectButton_GenerateMenu logic.
        /// </summary>
        public static List<MasterCandidate> GetMasterCandidates(Pawn animal)
        {
            var result = new List<MasterCandidate>();
            Pawn currentMaster = animal.playerSettings?.Master;

            // "None" option
            result.Add(new MasterCandidate
            {
                Colonist = null,
                Label = "(" + "NoneLower".Translate() + ")",
                CanBeMaster = true,
                IsCurrent = currentMaster == null
            });

            foreach (Pawn col in PawnsFinder.AllMaps_FreeColonistsSpawned)
            {
                string label = RelationsUtility.LabelWithBondInfo(col, animal);
                bool canBeMaster = TrainableUtility.CanBeMaster(col, animal);
                string disabledReason = null;

                if (!canBeMaster)
                {
                    int level = col.skills.GetSkill(SkillDefOf.Animals).Level;
                    int required = TrainableUtility.MinimumHandlingSkill(animal);
                    if (level < required)
                    {
                        disabledReason = "SkillTooLow".Translate(
                            SkillDefOf.Animals.LabelCap, level, required);
                    }
                }

                result.Add(new MasterCandidate
                {
                    Colonist = col,
                    Label = label,
                    CanBeMaster = canBeMaster,
                    DisabledReason = disabledReason,
                    IsCurrent = currentMaster == col
                });
            }

            return result;
        }

        /// <summary>
        /// Toggles whether a trainable skill is wanted. Handles prerequisite cascading.
        /// </summary>
        public static bool ToggleTrainable(Pawn pawn, TrainableDef td)
        {
            try
            {
                bool currentWanted = pawn.training.GetWanted(td);
                bool newWanted = !currentWanted;
                pawn.training.SetWantedRecursive(td, newWanted);
                PlayerKnowledgeDatabase.KnowledgeDemonstrated(
                    ConceptDefOf.AnimalTraining, KnowledgeAmount.Total);

                string status = newWanted ? "Wanted" : "Not wanted";
                TolkHelper.Speak(status);
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error toggling trainable: {ex}");
                TolkHelper.Speak("Error toggling training", SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        /// <summary>
        /// Sets the master for an animal.
        /// </summary>
        public static bool SetMaster(Pawn animal, Pawn master)
        {
            try
            {
                if (animal?.playerSettings == null)
                    return false;
                animal.playerSettings.Master = master;
                string masterName = master != null
                    ? master.LabelShort
                    : "(" + "NoneLower".Translate().Resolve() + ")";
                TolkHelper.Speak(masterName);
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error setting master: {ex}");
                TolkHelper.Speak("Error setting master", SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        /// <summary>
        /// Toggles follow-when-drafted setting.
        /// </summary>
        public static bool ToggleFollowDrafted(Pawn pawn)
        {
            try
            {
                if (pawn?.playerSettings == null)
                    return false;
                pawn.playerSettings.followDrafted = !pawn.playerSettings.followDrafted;
                string state = pawn.playerSettings.followDrafted
                    ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
                TolkHelper.Speak(state);
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error toggling follow drafted: {ex}");
                TolkHelper.Speak("Error toggling setting", SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        /// <summary>
        /// Toggles follow-during-fieldwork setting.
        /// </summary>
        public static bool ToggleFollowFieldwork(Pawn pawn)
        {
            try
            {
                if (pawn?.playerSettings == null)
                    return false;
                pawn.playerSettings.followFieldwork = !pawn.playerSettings.followFieldwork;
                string state = pawn.playerSettings.followFieldwork
                    ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
                TolkHelper.Speak(state);
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error toggling follow fieldwork: {ex}");
                TolkHelper.Speak("Error toggling setting", SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        /// <summary>
        /// Toggles allow-foraging setting (Odyssey DLC).
        /// </summary>
        public static bool ToggleForaging(Pawn pawn)
        {
            try
            {
                if (pawn?.playerSettings == null)
                    return false;
                pawn.playerSettings.animalForage = !pawn.playerSettings.animalForage;
                string state = pawn.playerSettings.animalForage
                    ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
                TolkHelper.Speak(state);
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error toggling foraging: {ex}");
                TolkHelper.Speak("Error toggling setting", SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        /// <summary>
        /// Toggles allow-digging setting (Odyssey DLC).
        /// </summary>
        public static bool ToggleDigging(Pawn pawn)
        {
            try
            {
                if (pawn?.playerSettings == null)
                    return false;
                pawn.playerSettings.animalDig = !pawn.playerSettings.animalDig;
                string state = pawn.playerSettings.animalDig
                    ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
                TolkHelper.Speak(state);
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error toggling digging: {ex}");
                TolkHelper.Speak("Error toggling setting", SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }
    }
}
