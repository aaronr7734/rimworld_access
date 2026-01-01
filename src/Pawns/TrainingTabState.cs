using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// State handler for the Training tab in the inspection menu.
    /// Manages hierarchical navigation through training information and settings.
    /// </summary>
    public static class TrainingTabState
    {
        private enum MenuLevel
        {
            SectionMenu,           // Level 1: Choose section
            TrainabilityInfo,      // Level 2a: View trainability info
            MasterSettingsList,    // Level 2b: List master settings
            ChangeMaster,          // Level 3b: Change master
            TrainablesList,        // Level 2c: List trainable skills
            TrainableDetail        // Level 3c: View trainable details
        }

        private static bool isActive = false;
        private static Pawn currentPawn = null;

        private static MenuLevel currentLevel = MenuLevel.SectionMenu;
        private static int sectionIndex = 0;
        private static readonly List<string> sections = new List<string> { "Trainability Info", "Master & Behavior", "Trainable Skills" };

        // Master settings
        private static TrainingTabHelper.MasterSettingsInfo masterSettings = null;
        private static int settingIndex = 0;
        private static readonly List<string> settingNames = new List<string> { "Master", "Follow Drafted", "Follow Fieldwork" };
        private static int masterChoiceIndex = 0;

        // Trainable skills
        private static List<TrainingTabHelper.TrainableInfo> trainables = new List<TrainingTabHelper.TrainableInfo>();
        private static int trainableIndex = 0;

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the training tab for a pawn.
        /// </summary>
        public static void Open(Pawn pawn)
        {
            if (pawn == null)
                return;

            currentPawn = pawn;
            isActive = true;
            currentLevel = MenuLevel.SectionMenu;
            sectionIndex = 0;

            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Closes the training tab.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentPawn = null;
            SoundDefOf.TabClose.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Handles keyboard input.
        /// </summary>
        public static bool HandleInput(Event evt)
        {
            if (!isActive || evt.type != EventType.KeyDown)
                return false;

            KeyCode key = evt.keyCode;

            // Handle Escape - go back or close
            if (key == KeyCode.Escape)
            {
                evt.Use();
                GoBack();
                return true;
            }

            // Handle arrow keys
            if (key == KeyCode.UpArrow)
            {
                evt.Use();
                SelectPrevious();
                return true;
            }

            if (key == KeyCode.DownArrow)
            {
                evt.Use();
                SelectNext();
                return true;
            }

            // Handle Enter - drill down or execute
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                evt.Use();
                DrillDown();
                return true;
            }

            // Handle Space - toggle training
            if (key == KeyCode.Space && currentLevel == MenuLevel.TrainablesList)
            {
                evt.Use();
                ToggleTraining();
                return true;
            }

            return false;
        }

        private static void SelectNext()
        {
            switch (currentLevel)
            {
                case MenuLevel.SectionMenu:
                    sectionIndex = MenuHelper.SelectNext(sectionIndex, sections.Count);
                    break;

                case MenuLevel.MasterSettingsList:
                    settingIndex = MenuHelper.SelectNext(settingIndex, settingNames.Count);
                    break;

                case MenuLevel.ChangeMaster:
                    if (masterSettings != null && masterSettings.AvailableMasters.Count > 0)
                        masterChoiceIndex = MenuHelper.SelectNext(masterChoiceIndex, masterSettings.AvailableMasters.Count + 1); // +1 for "None"
                    break;

                case MenuLevel.TrainablesList:
                    if (trainables.Count > 0)
                        trainableIndex = MenuHelper.SelectNext(trainableIndex, trainables.Count);
                    break;
            }

            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        private static void SelectPrevious()
        {
            switch (currentLevel)
            {
                case MenuLevel.SectionMenu:
                    sectionIndex = MenuHelper.SelectPrevious(sectionIndex, sections.Count);
                    break;

                case MenuLevel.MasterSettingsList:
                    settingIndex = MenuHelper.SelectPrevious(settingIndex, settingNames.Count);
                    break;

                case MenuLevel.ChangeMaster:
                    if (masterSettings != null && masterSettings.AvailableMasters.Count > 0)
                        masterChoiceIndex = MenuHelper.SelectPrevious(masterChoiceIndex, masterSettings.AvailableMasters.Count + 1);
                    break;

                case MenuLevel.TrainablesList:
                    if (trainables.Count > 0)
                        trainableIndex = MenuHelper.SelectPrevious(trainableIndex, trainables.Count);
                    break;
            }

            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        private static void DrillDown()
        {
            switch (currentLevel)
            {
                case MenuLevel.SectionMenu:
                    string section = sections[sectionIndex];
                    if (section == "Trainability Info")
                    {
                        currentLevel = MenuLevel.TrainabilityInfo;
                    }
                    else if (section == "Master & Behavior")
                    {
                        masterSettings = TrainingTabHelper.GetMasterSettings(currentPawn);
                        currentLevel = MenuLevel.MasterSettingsList;
                        settingIndex = 0;
                    }
                    else if (section == "Trainable Skills")
                    {
                        trainables = TrainingTabHelper.GetTrainableSkills(currentPawn);
                        if (trainables.Count == 0)
                        {
                            TolkHelper.Speak("No trainable skills");
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            return;
                        }
                        currentLevel = MenuLevel.TrainablesList;
                        trainableIndex = 0;
                    }
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

                case MenuLevel.MasterSettingsList:
                    string setting = settingNames[settingIndex];
                    if (setting == "Master")
                    {
                        currentLevel = MenuLevel.ChangeMaster;
                        masterChoiceIndex = 0;
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        AnnounceCurrentSelection();
                    }
                    else if (setting == "Follow Drafted")
                    {
                        TrainingTabHelper.ToggleFollowDrafted(currentPawn);
                        masterSettings = TrainingTabHelper.GetMasterSettings(currentPawn);
                        AnnounceCurrentSelection();
                    }
                    else if (setting == "Follow Fieldwork")
                    {
                        TrainingTabHelper.ToggleFollowFieldwork(currentPawn);
                        masterSettings = TrainingTabHelper.GetMasterSettings(currentPawn);
                        AnnounceCurrentSelection();
                    }
                    break;

                case MenuLevel.ChangeMaster:
                    if (masterSettings != null)
                    {
                        Pawn newMaster = null;
                        if (masterChoiceIndex < masterSettings.AvailableMasters.Count)
                        {
                            newMaster = masterSettings.AvailableMasters[masterChoiceIndex];
                        }
                        TrainingTabHelper.SetMaster(currentPawn, newMaster);
                        masterSettings = TrainingTabHelper.GetMasterSettings(currentPawn);
                        currentLevel = MenuLevel.MasterSettingsList;
                        AnnounceCurrentSelection();
                    }
                    break;

                case MenuLevel.TrainablesList:
                    if (trainableIndex >= 0 && trainableIndex < trainables.Count)
                    {
                        currentLevel = MenuLevel.TrainableDetail;
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        AnnounceCurrentSelection();
                    }
                    break;
            }
        }

        private static void ToggleTraining()
        {
            if (trainableIndex >= 0 && trainableIndex < trainables.Count)
            {
                var trainable = trainables[trainableIndex];
                if (TrainingTabHelper.ToggleTraining(currentPawn, trainable.Def))
                {
                    trainables = TrainingTabHelper.GetTrainableSkills(currentPawn);
                    AnnounceCurrentSelection();
                }
            }
        }

        private static void GoBack()
        {
            switch (currentLevel)
            {
                case MenuLevel.SectionMenu:
                    Close();
                    TolkHelper.Speak("Closed Training tab");
                    break;

                case MenuLevel.TrainabilityInfo:
                case MenuLevel.MasterSettingsList:
                case MenuLevel.TrainablesList:
                    currentLevel = MenuLevel.SectionMenu;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

                case MenuLevel.ChangeMaster:
                    currentLevel = MenuLevel.MasterSettingsList;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

                case MenuLevel.TrainableDetail:
                    currentLevel = MenuLevel.TrainablesList;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;
            }
        }

        private static void AnnounceCurrentSelection()
        {
            var sb = new StringBuilder();

            switch (currentLevel)
            {
                case MenuLevel.SectionMenu:
                    sb.AppendLine($"Training - {sections[sectionIndex]}");
                    sb.AppendLine($"Section {MenuHelper.FormatPosition(sectionIndex, sections.Count)}");
                    sb.AppendLine("Press Enter to open");
                    break;

                case MenuLevel.TrainabilityInfo:
                    string info = TrainingTabHelper.GetTrainabilityInfo(currentPawn);
                    sb.AppendLine(info);
                    break;

                case MenuLevel.MasterSettingsList:
                    if (masterSettings != null)
                    {
                        string settingName = settingNames[settingIndex];
                        sb.AppendLine($"{settingName}");

                        if (settingName == "Master")
                        {
                            sb.AppendLine($"Current: {masterSettings.MasterName}");
                        }
                        else if (settingName == "Follow Drafted")
                        {
                            string status = masterSettings.FollowDrafted ? "Enabled" : "Disabled";
                            sb.AppendLine($"Current: {status}");
                        }
                        else if (settingName == "Follow Fieldwork")
                        {
                            string status = masterSettings.FollowFieldwork ? "Enabled" : "Disabled";
                            sb.AppendLine($"Current: {status}");
                        }

                        sb.AppendLine($"Setting {MenuHelper.FormatPosition(settingIndex, settingNames.Count)}");
                        sb.AppendLine("Press Enter to change");
                    }
                    break;

                case MenuLevel.ChangeMaster:
                    if (masterSettings != null)
                    {
                        string masterName = "None";
                        if (masterChoiceIndex < masterSettings.AvailableMasters.Count)
                        {
                            masterName = masterSettings.AvailableMasters[masterChoiceIndex].LabelShort.StripTags();
                        }

                        sb.AppendLine($"Master: {masterName}");
                        sb.AppendLine($"Option {MenuHelper.FormatPosition(masterChoiceIndex, masterSettings.AvailableMasters.Count + 1)}");
                        sb.AppendLine("Press Enter to confirm");
                    }
                    break;

                case MenuLevel.TrainablesList:
                    if (trainableIndex >= 0 && trainableIndex < trainables.Count)
                    {
                        var trainable = trainables[trainableIndex];
                        string status = trainable.IsLearned ? "[Learned]" : trainable.IsEnabled ? "[Training]" : "[Not training]";
                        string availability = trainable.CanTrain ? "" : " [Cannot train]";
                        sb.AppendLine($"{trainable.Label} {status}{availability}");
                        if (trainable.RequiredSteps > 0)
                        {
                            sb.AppendLine($"Progress: {trainable.CurrentSteps} / {trainable.RequiredSteps}");
                        }
                        sb.AppendLine($"Skill {MenuHelper.FormatPosition(trainableIndex, trainables.Count)}");
                        sb.AppendLine("Press Space to toggle, Enter for details");
                    }
                    break;

                case MenuLevel.TrainableDetail:
                    if (trainableIndex >= 0 && trainableIndex < trainables.Count)
                    {
                        var trainable = trainables[trainableIndex];
                        sb.AppendLine(trainable.DetailedInfo);
                    }
                    break;
            }

            TolkHelper.Speak(sb.ToString());
        }
    }
}
