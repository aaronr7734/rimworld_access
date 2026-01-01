using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper class for training tab data extraction and interactions.
    /// Provides methods for animal training, master assignment, and behavior settings.
    /// </summary>
    public static class TrainingTabHelper
    {
        /// <summary>
        /// Represents a trainable skill.
        /// </summary>
        public class TrainableInfo
        {
            public TrainableDef Def { get; set; }
            public string Label { get; set; }
            public bool IsLearned { get; set; }
            public bool IsEnabled { get; set; }
            public int CurrentSteps { get; set; }
            public int RequiredSteps { get; set; }
            public bool CanTrain { get; set; }
            public string UnavailableReason { get; set; }
            public string DetailedInfo { get; set; }
        }

        /// <summary>
        /// Represents master and behavior settings.
        /// </summary>
        public class MasterSettingsInfo
        {
            public Pawn Master { get; set; }
            public string MasterName { get; set; }
            public bool FollowDrafted { get; set; }
            public bool FollowFieldwork { get; set; }
            public List<Pawn> AvailableMasters { get; set; }

            public MasterSettingsInfo()
            {
                AvailableMasters = new List<Pawn>();
            }
        }

        #region Trainability & Wildness

        /// <summary>
        /// Gets trainability information for a pawn.
        /// </summary>
        public static string GetTrainabilityInfo(Pawn pawn)
        {
            if (pawn?.training == null)
                return "Not trainable";

            var sb = new StringBuilder();

            // Trainability level
            TrainabilityDef trainability = pawn.RaceProps.trainability;
            if (trainability != null)
            {
                sb.AppendLine($"Trainability: {trainability.LabelCap}");
            }

            // Wildness (it's a stat, not a property)
            float wildness = pawn.GetStatValue(StatDefOf.Wildness);
            sb.AppendLine($"Wildness: {wildness:P0}");
            sb.AppendLine();
            sb.AppendLine("Higher wildness makes training more difficult and increases the chance of bond breaks.");

            return sb.ToString().TrimEnd();
        }

        #endregion

        #region Master Settings

        /// <summary>
        /// Gets master and behavior settings for a pawn.
        /// </summary>
        public static MasterSettingsInfo GetMasterSettings(Pawn pawn)
        {
            var settings = new MasterSettingsInfo();

            if (pawn?.playerSettings == null)
                return settings;

            settings.Master = pawn.playerSettings.Master;
            settings.MasterName = settings.Master?.LabelShort.StripTags() ?? "None";
            settings.FollowDrafted = pawn.playerSettings.followDrafted;
            settings.FollowFieldwork = pawn.playerSettings.followFieldwork;

            // Get available masters (colonists)
            if (pawn.Map != null)
            {
                settings.AvailableMasters = pawn.Map.mapPawns.FreeColonistsSpawned.ToList();
            }

            return settings;
        }

        /// <summary>
        /// Sets the master for a pawn.
        /// </summary>
        public static bool SetMaster(Pawn animal, Pawn master)
        {
            try
            {
                if (animal?.playerSettings == null)
                    return false;

                animal.playerSettings.Master = master;
                string masterName = master?.LabelShort.StripTags() ?? "None";
                TolkHelper.Speak($"Master set to: {masterName}");
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
        /// Toggles follow drafted setting.
        /// </summary>
        public static bool ToggleFollowDrafted(Pawn pawn)
        {
            try
            {
                if (pawn?.playerSettings == null)
                    return false;

                pawn.playerSettings.followDrafted = !pawn.playerSettings.followDrafted;
                string status = pawn.playerSettings.followDrafted ? "enabled" : "disabled";
                TolkHelper.Speak($"Follow drafted {status}");
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error toggling follow drafted: {ex}");
                TolkHelper.Speak("Error toggling follow drafted", SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        /// <summary>
        /// Toggles follow fieldwork setting.
        /// </summary>
        public static bool ToggleFollowFieldwork(Pawn pawn)
        {
            try
            {
                if (pawn?.playerSettings == null)
                    return false;

                pawn.playerSettings.followFieldwork = !pawn.playerSettings.followFieldwork;
                string status = pawn.playerSettings.followFieldwork ? "enabled" : "disabled";
                TolkHelper.Speak($"Follow fieldwork {status}");
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error toggling follow fieldwork: {ex}");
                TolkHelper.Speak("Error toggling follow fieldwork", SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        #endregion

        #region Trainable Skills

        /// <summary>
        /// Gets all trainable skills for a pawn.
        /// </summary>
        public static List<TrainableInfo> GetTrainableSkills(Pawn pawn)
        {
            var trainables = new List<TrainableInfo>();

            if (pawn?.training == null)
                return trainables;

            var trainableDefs = DefDatabase<TrainableDef>.AllDefsListForReading;

            foreach (var trainable in trainableDefs)
            {
                var acceptanceReport = pawn.training.CanAssignToTrain(trainable);

                var info = new TrainableInfo
                {
                    Def = trainable,
                    Label = trainable.LabelCap,
                    IsLearned = pawn.training.HasLearned(trainable),
                    IsEnabled = pawn.training.GetWanted(trainable),
                    CanTrain = acceptanceReport.Accepted,
                    UnavailableReason = acceptanceReport.Reason,
                    DetailedInfo = GetTrainableDetailedInfo(pawn, trainable)
                };

                // Training steps - GetSteps is internal, can't access
                if (trainable.steps > 0)
                {
                    info.RequiredSteps = trainable.steps;
                    info.CurrentSteps = 0; // Can't access internal GetSteps method
                }

                trainables.Add(info);
            }

            // Sort by: can train, then learned, then alphabetically
            trainables = trainables
                .OrderByDescending(t => t.CanTrain)
                .ThenByDescending(t => t.IsLearned)
                .ThenBy(t => t.Label)
                .ToList();

            return trainables;
        }

        private static string GetTrainableDetailedInfo(Pawn pawn, TrainableDef trainable)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{trainable.LabelCap}:");
            sb.AppendLine();

            // Description
            if (!string.IsNullOrEmpty(trainable.description))
            {
                sb.AppendLine(trainable.description);
                sb.AppendLine();
            }

            // Status
            bool isLearned = pawn.training.HasLearned(trainable);
            bool isEnabled = pawn.training.GetWanted(trainable);

            if (isLearned)
            {
                sb.AppendLine("Status: Fully trained");
            }
            else if (isEnabled)
            {
                // GetSteps is internal, can't access - just show in progress
                sb.AppendLine($"Status: Training in progress");
            }
            else
            {
                sb.AppendLine("Status: Not being trained");
            }

            // Prerequisites
            if (trainable.prerequisites != null && trainable.prerequisites.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Prerequisites:");
                foreach (var prereq in trainable.prerequisites)
                {
                    bool prereqLearned = pawn.training.HasLearned(prereq);
                    string status = prereqLearned ? "[Learned]" : "[Not learned]";
                    sb.AppendLine($"  {prereq.LabelCap} {status}");
                }
            }

            // Can train check
            var acceptanceReport = pawn.training.CanAssignToTrain(trainable);
            if (!acceptanceReport.Accepted)
            {
                sb.AppendLine();
                sb.AppendLine($"Cannot train: {acceptanceReport.Reason}");
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Toggles training for a skill.
        /// </summary>
        public static bool ToggleTraining(Pawn pawn, TrainableDef trainable)
        {
            try
            {
                if (pawn?.training == null)
                    return false;

                var acceptanceReport = pawn.training.CanAssignToTrain(trainable);
                if (!acceptanceReport.Accepted)
                {
                    TolkHelper.Speak($"Cannot train: {acceptanceReport.Reason}", SpeechPriority.High);
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return false;
                }

                bool newState = !pawn.training.GetWanted(trainable);
                pawn.training.SetWantedRecursive(trainable, newState);

                string status = newState ? "enabled" : "disabled";
                TolkHelper.Speak($"Training {trainable.LabelCap} {status}");
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error toggling training: {ex}");
                TolkHelper.Speak("Error toggling training", SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        #endregion
    }
}
