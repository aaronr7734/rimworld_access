using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Navigation level for the storyteller selection.
    /// </summary>
    public enum StorytellerSelectionLevel
    {
        StorytellerList,      // Choosing a storyteller
        DifficultyList,       // Choosing difficulty preset
        CustomSectionList,    // Navigating custom difficulty sections
        CustomSettingsList    // Navigating settings within a section
    }

    /// <summary>
    /// Manages keyboard navigation for the storyteller selection page.
    /// Supports storyteller selection, difficulty presets, and custom difficulty settings.
    /// </summary>
    public static class StorytellerSelectionState
    {
        private static bool isActive = false;
        private static StorytellerSelectionLevel currentLevel = StorytellerSelectionLevel.StorytellerList;

        // Storyteller and difficulty lists
        private static int selectedStorytellerIndex = 0;
        private static int selectedDifficultyIndex = 0;
        private static List<StorytellerDef> storytellers = new List<StorytellerDef>();
        private static List<DifficultyDef> difficulties = new List<DifficultyDef>();

        // Custom difficulty navigation
        private static int selectedSectionIndex = 0;
        private static int selectedSettingIndex = 0;
        private static List<DifficultySection> sections = new List<DifficultySection>();

        // Typeahead search
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        public static bool IsActive => isActive;
        public static StorytellerSelectionLevel CurrentLevel => currentLevel;
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        // Helper to get current enabled settings for navigation
        private static List<DifficultySetting> GetEnabledSettings()
        {
            if (sections.Count == 0 || selectedSectionIndex >= sections.Count)
                return new List<DifficultySetting>();
            return sections[selectedSectionIndex].Settings.Where(s => s.IsEnabled).ToList();
        }

        // Map from enabled setting index to actual setting index
        private static int EnabledIndexToActualIndex(int enabledIndex)
        {
            if (sections.Count == 0 || selectedSectionIndex >= sections.Count)
                return 0;
            var allSettings = sections[selectedSectionIndex].Settings;
            int enabledCount = 0;
            for (int i = 0; i < allSettings.Count; i++)
            {
                if (allSettings[i].IsEnabled)
                {
                    if (enabledCount == enabledIndex)
                        return i;
                    enabledCount++;
                }
            }
            return 0;
        }

        // Map from actual setting index to enabled setting index
        private static int ActualIndexToEnabledIndex(int actualIndex)
        {
            if (sections.Count == 0 || selectedSectionIndex >= sections.Count)
                return 0;
            var allSettings = sections[selectedSectionIndex].Settings;
            int enabledCount = 0;
            for (int i = 0; i < actualIndex && i < allSettings.Count; i++)
            {
                if (allSettings[i].IsEnabled)
                    enabledCount++;
            }
            return enabledCount;
        }

        /// <summary>
        /// Opens the storyteller selection navigation.
        /// </summary>
        public static void Open()
        {
            isActive = true;
            currentLevel = StorytellerSelectionLevel.StorytellerList;
            typeahead.ClearSearch();

            // Get all visible storytellers
            storytellers = DefDatabase<StorytellerDef>.AllDefs
                .Where(st => st.listVisible)
                .OrderBy(st => st.listOrder)
                .ToList();

            // Get all difficulties
            difficulties = DefDatabase<DifficultyDef>.AllDefs.ToList();

            // Find current selections
            Storyteller current = Current.Game.storyteller;
            selectedStorytellerIndex = storytellers.IndexOf(current.def);
            if (selectedStorytellerIndex < 0) selectedStorytellerIndex = 0;

            selectedDifficultyIndex = difficulties.IndexOf(current.difficultyDef);
            if (selectedDifficultyIndex < 0) selectedDifficultyIndex = 0;

            // Build custom difficulty sections
            BuildCustomDifficultySections();

            AnnounceCurrentState();
        }

        /// <summary>
        /// Closes the storyteller selection.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentLevel = StorytellerSelectionLevel.StorytellerList;
            typeahead.ClearSearch();
        }

        /// <summary>
        /// Moves selection to next item.
        /// </summary>
        public static void SelectNext()
        {
            switch (currentLevel)
            {
                case StorytellerSelectionLevel.StorytellerList:
                    selectedStorytellerIndex = MenuHelper.SelectNext(selectedStorytellerIndex, storytellers.Count);
                    ApplyStorytellerSelection();
                    break;

                case StorytellerSelectionLevel.DifficultyList:
                    selectedDifficultyIndex = MenuHelper.SelectNext(selectedDifficultyIndex, difficulties.Count);
                    ApplyDifficultySelection();
                    break;

                case StorytellerSelectionLevel.CustomSectionList:
                    selectedSectionIndex = MenuHelper.SelectNext(selectedSectionIndex, sections.Count);
                    break;

                case StorytellerSelectionLevel.CustomSettingsList:
                    {
                        var enabledSettings = GetEnabledSettings();
                        if (enabledSettings.Count > 0)
                        {
                            int enabledIndex = ActualIndexToEnabledIndex(selectedSettingIndex);
                            enabledIndex = MenuHelper.SelectNext(enabledIndex, enabledSettings.Count);
                            selectedSettingIndex = EnabledIndexToActualIndex(enabledIndex);
                        }
                    }
                    break;
            }

            AnnounceCurrentState();
        }

        /// <summary>
        /// Moves selection to previous item.
        /// </summary>
        public static void SelectPrevious()
        {
            switch (currentLevel)
            {
                case StorytellerSelectionLevel.StorytellerList:
                    selectedStorytellerIndex = MenuHelper.SelectPrevious(selectedStorytellerIndex, storytellers.Count);
                    ApplyStorytellerSelection();
                    break;

                case StorytellerSelectionLevel.DifficultyList:
                    selectedDifficultyIndex = MenuHelper.SelectPrevious(selectedDifficultyIndex, difficulties.Count);
                    ApplyDifficultySelection();
                    break;

                case StorytellerSelectionLevel.CustomSectionList:
                    selectedSectionIndex = MenuHelper.SelectPrevious(selectedSectionIndex, sections.Count);
                    break;

                case StorytellerSelectionLevel.CustomSettingsList:
                    {
                        var enabledSettings = GetEnabledSettings();
                        if (enabledSettings.Count > 0)
                        {
                            int enabledIndex = ActualIndexToEnabledIndex(selectedSettingIndex);
                            enabledIndex = MenuHelper.SelectPrevious(enabledIndex, enabledSettings.Count);
                            selectedSettingIndex = EnabledIndexToActualIndex(enabledIndex);
                        }
                    }
                    break;
            }

            AnnounceCurrentState();
        }

        /// <summary>
        /// Switches between storyteller and difficulty selection (Tab key).
        /// Only works at top two levels.
        /// </summary>
        public static void SwitchLevel()
        {
            if (currentLevel == StorytellerSelectionLevel.StorytellerList)
            {
                currentLevel = StorytellerSelectionLevel.DifficultyList;
                typeahead.ClearSearch();
                AnnounceCurrentState();
            }
            else if (currentLevel == StorytellerSelectionLevel.DifficultyList)
            {
                currentLevel = StorytellerSelectionLevel.StorytellerList;
                typeahead.ClearSearch();
                AnnounceCurrentState();
            }
            // Tab does nothing in custom settings levels - use Escape to go back
        }

        /// <summary>
        /// Handles Enter key - confirms selection or enters deeper level.
        /// </summary>
        public static void ExecuteOrEnter()
        {
            switch (currentLevel)
            {
                case StorytellerSelectionLevel.StorytellerList:
                case StorytellerSelectionLevel.DifficultyList:
                    // Check if Custom difficulty is selected
                    if (currentLevel == StorytellerSelectionLevel.DifficultyList &&
                        selectedDifficultyIndex >= 0 && selectedDifficultyIndex < difficulties.Count &&
                        difficulties[selectedDifficultyIndex].isCustom)
                    {
                        // Enter custom settings
                        currentLevel = StorytellerSelectionLevel.CustomSectionList;
                        selectedSectionIndex = 0;
                        typeahead.ClearSearch();
                        TolkHelper.Speak("RimWorldAccess.Storyteller.CustomDifficultyOpenInstructions".Loc());
                        AnnounceCurrentState();
                    }
                    else
                    {
                        // Confirm and close
                        Confirm();
                    }
                    break;

                case StorytellerSelectionLevel.CustomSectionList:
                    // Enter the selected section
                    if (sections.Count > 0 && selectedSectionIndex < sections.Count)
                    {
                        var enabledSettings = GetEnabledSettings();
                        if (enabledSettings.Count > 0)
                        {
                            currentLevel = StorytellerSelectionLevel.CustomSettingsList;
                            selectedSettingIndex = EnabledIndexToActualIndex(0); // First enabled setting
                            typeahead.ClearSearch();
                            AnnounceCurrentState();
                        }
                        else
                        {
                            TolkHelper.Speak("RimWorldAccess.Storyteller.NoSettingsInSection".Loc());
                        }
                    }
                    break;

                case StorytellerSelectionLevel.CustomSettingsList:
                    // Toggle checkbox settings
                    ToggleCurrentSetting();
                    break;
            }
        }

        /// <summary>
        /// Handles Escape key - goes back one level or closes.
        /// Returns true if handled, false if should close the dialog.
        /// </summary>
        public static bool GoBack()
        {
            switch (currentLevel)
            {
                case StorytellerSelectionLevel.CustomSettingsList:
                    currentLevel = StorytellerSelectionLevel.CustomSectionList;
                    typeahead.ClearSearch();
                    AnnounceCurrentState();
                    return true;

                case StorytellerSelectionLevel.CustomSectionList:
                    currentLevel = StorytellerSelectionLevel.DifficultyList;
                    typeahead.ClearSearch();
                    AnnounceCurrentState();
                    return true;

                default:
                    // At top level - let caller close the dialog
                    return false;
            }
        }

        /// <summary>
        /// Adjusts the current setting value (Left/Right arrows).
        /// </summary>
        public static void AdjustCurrentSetting(int direction)
        {
            if (currentLevel != StorytellerSelectionLevel.CustomSettingsList)
                return;

            if (sections.Count == 0 || selectedSectionIndex >= sections.Count)
                return;

            var settings = sections[selectedSectionIndex].Settings;
            if (settings.Count == 0 || selectedSettingIndex >= settings.Count)
                return;

            var setting = settings[selectedSettingIndex];
            setting.Adjust(direction);
            // Use adjustment announcement (may be shorter than full announcement for some settings)
            TolkHelper.Speak(setting.GetAdjustmentAnnouncement());
        }

        /// <summary>
        /// Toggles the current checkbox setting (Space or Enter).
        /// </summary>
        public static void ToggleCurrentSetting()
        {
            if (currentLevel != StorytellerSelectionLevel.CustomSettingsList)
                return;

            if (sections.Count == 0 || selectedSectionIndex >= sections.Count)
                return;

            var settings = sections[selectedSectionIndex].Settings;
            if (settings.Count == 0 || selectedSettingIndex >= settings.Count)
                return;

            var setting = settings[selectedSettingIndex];
            setting.Toggle();
            AnnounceCurrentState();
        }

        /// <summary>
        /// Opens the reset to preset menu (Alt+R).
        /// Resets ALL custom difficulty settings to match a preset.
        /// </summary>
        public static void OpenResetToPresetMenu()
        {
            if (currentLevel != StorytellerSelectionLevel.CustomSectionList &&
                currentLevel != StorytellerSelectionLevel.CustomSettingsList)
                return;

            var options = new List<FloatMenuOption>();
            foreach (DifficultyDef def in DefDatabase<DifficultyDef>.AllDefs)
            {
                if (!def.isCustom)
                {
                    DifficultyDef localDef = def;
                    options.Add(new FloatMenuOption(def.LabelCap, () =>
                    {
                        Current.Game.storyteller.difficulty.CopyFrom(localDef);
                        TolkHelper.Speak("RimWorldAccess.Storyteller.AllSettingsResetTo".Loc(localDef.LabelCap));
                        // Rebuild sections to reflect new values
                        BuildCustomDifficultySections();
                        AnnounceCurrentState();
                    }));
                }
            }

            // Use accessible WindowlessFloatMenuState instead of native FloatMenu
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
            TolkHelper.Speak("RimWorldAccess.Storyteller.ResetSelectPreset".Loc());
        }

        /// <summary>
        /// Jumps to the first item in the current list (Home key).
        /// </summary>
        public static void JumpToFirst()
        {
            typeahead.ClearSearch();
            switch (currentLevel)
            {
                case StorytellerSelectionLevel.StorytellerList:
                    selectedStorytellerIndex = 0;
                    ApplyStorytellerSelection();
                    break;
                case StorytellerSelectionLevel.DifficultyList:
                    selectedDifficultyIndex = 0;
                    ApplyDifficultySelection();
                    break;
                case StorytellerSelectionLevel.CustomSectionList:
                    selectedSectionIndex = 0;
                    break;
                case StorytellerSelectionLevel.CustomSettingsList:
                    var enabledSettings = GetEnabledSettings();
                    if (enabledSettings.Count > 0)
                    {
                        selectedSettingIndex = EnabledIndexToActualIndex(0);
                    }
                    break;
            }
            AnnounceCurrentState();
        }

        /// <summary>
        /// Jumps to the last item in the current list (End key).
        /// </summary>
        public static void JumpToLast()
        {
            typeahead.ClearSearch();
            switch (currentLevel)
            {
                case StorytellerSelectionLevel.StorytellerList:
                    selectedStorytellerIndex = storytellers.Count - 1;
                    ApplyStorytellerSelection();
                    break;
                case StorytellerSelectionLevel.DifficultyList:
                    selectedDifficultyIndex = difficulties.Count - 1;
                    ApplyDifficultySelection();
                    break;
                case StorytellerSelectionLevel.CustomSectionList:
                    selectedSectionIndex = sections.Count - 1;
                    break;
                case StorytellerSelectionLevel.CustomSettingsList:
                    var enabledSettings = GetEnabledSettings();
                    if (enabledSettings.Count > 0)
                    {
                        selectedSettingIndex = EnabledIndexToActualIndex(enabledSettings.Count - 1);
                    }
                    break;
            }
            AnnounceCurrentState();
        }

        /// <summary>
        /// Adjusts slider by a percentage of its total possible positions.
        /// </summary>
        /// <param name="percent">Percentage of total positions to move (e.g., 0.1 = 10%, -0.25 = -25%)</param>
        public static void AdjustCurrentSettingByPercent(float percent)
        {
            if (currentLevel != StorytellerSelectionLevel.CustomSettingsList)
                return;

            if (sections.Count == 0 || selectedSectionIndex >= sections.Count)
                return;

            var settings = sections[selectedSectionIndex].Settings;
            if (settings.Count == 0 || selectedSettingIndex >= settings.Count)
                return;

            var setting = settings[selectedSettingIndex];
            if (setting is DifficultySliderSetting sliderSetting)
            {
                sliderSetting.AdjustByPercentOfPositions(percent);
                TolkHelper.Speak(setting.GetAdjustmentAnnouncement());
            }
            else
            {
                // For checkboxes, just toggle
                setting.Toggle();
                TolkHelper.Speak(setting.GetAdjustmentAnnouncement());
            }
        }

        /// <summary>
        /// Sets the current slider setting to its maximum value (Shift+Home).
        /// </summary>
        public static void SetCurrentSettingToMax()
        {
            if (currentLevel != StorytellerSelectionLevel.CustomSettingsList)
                return;

            if (sections.Count == 0 || selectedSectionIndex >= sections.Count)
                return;

            var settings = sections[selectedSectionIndex].Settings;
            if (settings.Count == 0 || selectedSettingIndex >= settings.Count)
                return;

            var setting = settings[selectedSettingIndex];
            if (setting is DifficultySliderSetting sliderSetting)
            {
                sliderSetting.SetToMax();
                TolkHelper.Speak(setting.GetAdjustmentAnnouncement());
            }
        }

        /// <summary>
        /// Sets the current slider setting to its minimum value (Shift+End).
        /// </summary>
        public static void SetCurrentSettingToMin()
        {
            if (currentLevel != StorytellerSelectionLevel.CustomSettingsList)
                return;

            if (sections.Count == 0 || selectedSectionIndex >= sections.Count)
                return;

            var settings = sections[selectedSectionIndex].Settings;
            if (settings.Count == 0 || selectedSettingIndex >= settings.Count)
                return;

            var setting = settings[selectedSettingIndex];
            if (setting is DifficultySliderSetting sliderSetting)
            {
                sliderSetting.SetToMin();
                TolkHelper.Speak(setting.GetAdjustmentAnnouncement());
            }
        }

        /// <summary>
        /// Clears the typeahead search.
        /// </summary>
        public static void ClearTypeaheadSearch()
        {
            typeahead.ClearSearchAndAnnounce();
        }

        /// <summary>
        /// Handles typeahead character input from the layout-aware dispatcher.
        /// Wraps <see cref="ProcessTypeaheadCharacter"/> with the same letter-or-digit
        /// gate and ToLower normalization as the legacy KeyCode-based dispatch.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive) return;
            if (!char.IsLetterOrDigit(c)) return;
            ProcessTypeaheadCharacter(char.ToLower(c));
        }

        /// <summary>
        /// Processes typeahead character input.
        /// </summary>
        public static void ProcessTypeaheadCharacter(char c)
        {
            var labels = GetCurrentLevelLabels();
            if (labels.Count == 0) return;

            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    SetCurrentIndex(newIndex);
                    AnnounceWithSearch();
                }
            }
            else
            {
                typeahead.SpeakNoMatches();
            }
        }

        /// <summary>
        /// Processes backspace for typeahead search.
        /// </summary>
        public static void ProcessBackspace()
        {
            if (!typeahead.HasActiveSearch) return;

            var labels = GetCurrentLevelLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    SetCurrentIndex(newIndex);
                }
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Selects next typeahead match.
        /// </summary>
        public static void SelectNextMatch()
        {
            int currentIndex = GetCurrentIndex();
            int newIndex = typeahead.GetNextMatch(currentIndex);
            if (newIndex >= 0)
            {
                SetCurrentIndex(newIndex);
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Selects previous typeahead match.
        /// </summary>
        public static void SelectPreviousMatch()
        {
            int currentIndex = GetCurrentIndex();
            int newIndex = typeahead.GetPreviousMatch(currentIndex);
            if (newIndex >= 0)
            {
                SetCurrentIndex(newIndex);
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Confirms selection and closes the page.
        /// </summary>
        public static void Confirm()
        {
            TolkHelper.Speak("RimWorldAccess.Storyteller.Confirmed".Loc());
            Close();
            Find.WindowStack.TryRemove(typeof(Page_SelectStorytellerInGame));
        }

        // ===== Private Methods =====

        private static void ApplyStorytellerSelection()
        {
            if (selectedStorytellerIndex >= 0 && selectedStorytellerIndex < storytellers.Count)
            {
                Storyteller storyteller = Current.Game.storyteller;
                StorytellerDef oldDef = storyteller.def;
                storyteller.def = storytellers[selectedStorytellerIndex];

                if (storyteller.def != oldDef)
                {
                    storyteller.Notify_DefChanged();
                    TutorSystem.Notify_Event("ChooseStoryteller");
                }
            }
        }

        private static void ApplyDifficultySelection()
        {
            if (selectedDifficultyIndex >= 0 && selectedDifficultyIndex < difficulties.Count)
            {
                Storyteller storyteller = Current.Game.storyteller;
                DifficultyDef selectedDiff = difficulties[selectedDifficultyIndex];
                storyteller.difficultyDef = selectedDiff;

                // Copy difficulty values from the def (only for non-custom)
                if (!selectedDiff.isCustom)
                {
                    storyteller.difficulty.CopyFrom(selectedDiff);
                }
            }
        }

        private static void BuildCustomDifficultySections()
        {
            Storyteller storyteller = Current.Game.storyteller;
            sections = DifficultySettingsHelper.BuildSections(
                storyteller.difficulty,
                onReset: (DifficultyDef def) =>
                {
                    storyteller.difficulty.CopyFrom(def);
                    TolkHelper.Speak("RimWorldAccess.Storyteller.AllSettingsResetTo".Loc(def.LabelCap));
                    // Rebuild sections to reflect new values
                    BuildCustomDifficultySections();
                    // Navigate back to section list
                    currentLevel = StorytellerSelectionLevel.CustomSectionList;
                    AnnounceCurrentState();
                },
                onAnomalyPlaystyleChanged: () => BuildCustomDifficultySections()
            );
        }

        private static List<string> GetCurrentLevelLabels()
        {
            var labels = new List<string>();
            switch (currentLevel)
            {
                case StorytellerSelectionLevel.StorytellerList:
                    foreach (var st in storytellers)
                        labels.Add(st.label);
                    break;
                case StorytellerSelectionLevel.DifficultyList:
                    foreach (var diff in difficulties)
                        labels.Add(diff.LabelCap);
                    break;
                case StorytellerSelectionLevel.CustomSectionList:
                    foreach (var section in sections)
                        labels.Add(section.Name);
                    break;
                case StorytellerSelectionLevel.CustomSettingsList:
                    if (sections.Count > 0 && selectedSectionIndex < sections.Count)
                    {
                        // Only include enabled settings to match navigation behavior
                        foreach (var setting in sections[selectedSectionIndex].Settings.Where(s => s.IsEnabled))
                            labels.Add(setting.Label);
                    }
                    break;
            }
            return labels;
        }

        private static int GetCurrentIndex()
        {
            switch (currentLevel)
            {
                case StorytellerSelectionLevel.StorytellerList:
                    return selectedStorytellerIndex;
                case StorytellerSelectionLevel.DifficultyList:
                    return selectedDifficultyIndex;
                case StorytellerSelectionLevel.CustomSectionList:
                    return selectedSectionIndex;
                case StorytellerSelectionLevel.CustomSettingsList:
                    // Return enabled index for consistency with typeahead labels
                    return ActualIndexToEnabledIndex(selectedSettingIndex);
                default:
                    return 0;
            }
        }

        private static void SetCurrentIndex(int index)
        {
            switch (currentLevel)
            {
                case StorytellerSelectionLevel.StorytellerList:
                    selectedStorytellerIndex = index;
                    ApplyStorytellerSelection();
                    break;
                case StorytellerSelectionLevel.DifficultyList:
                    selectedDifficultyIndex = index;
                    ApplyDifficultySelection();
                    break;
                case StorytellerSelectionLevel.CustomSectionList:
                    selectedSectionIndex = index;
                    break;
                case StorytellerSelectionLevel.CustomSettingsList:
                    // Typeahead index is based on enabled settings only, convert to actual index
                    selectedSettingIndex = EnabledIndexToActualIndex(index);
                    break;
            }
        }

        private static void AnnounceWithSearch()
        {
            if (typeahead.HasActiveSearch)
            {
                string itemName = GetCurrentItemAnnouncement();
                TolkHelper.Speak(typeahead.BuildItemAnnouncement(itemName));
            }
            else
            {
                AnnounceCurrentState();
            }
        }

        private static string GetCurrentItemAnnouncement()
        {
            switch (currentLevel)
            {
                case StorytellerSelectionLevel.StorytellerList:
                    if (selectedStorytellerIndex >= 0 && selectedStorytellerIndex < storytellers.Count)
                    {
                        var st = storytellers[selectedStorytellerIndex];
                        return "RimWorldAccess.Storyteller.LabelDot".Translate(st.label, st.description.StripTags());
                    }
                    break;
                case StorytellerSelectionLevel.DifficultyList:
                    if (selectedDifficultyIndex >= 0 && selectedDifficultyIndex < difficulties.Count)
                    {
                        var diff = difficulties[selectedDifficultyIndex];
                        string customHint = diff.isCustom ? (string)"RimWorldAccess.Storyteller.CustomizeHintSuffix".Translate() : "";
                        return "RimWorldAccess.Storyteller.LabelDotWithHint".Translate(diff.LabelCap, diff.description.StripTags(), customHint);
                    }
                    break;
                case StorytellerSelectionLevel.CustomSectionList:
                    if (selectedSectionIndex >= 0 && selectedSectionIndex < sections.Count)
                    {
                        var section = sections[selectedSectionIndex];
                        return "RimWorldAccess.Storyteller.SectionWithCount".Translate(section.Name, section.Settings.Count, section.ItemsLabel);
                    }
                    break;
                case StorytellerSelectionLevel.CustomSettingsList:
                    if (sections.Count > 0 && selectedSectionIndex < sections.Count)
                    {
                        var settings = sections[selectedSectionIndex].Settings;
                        if (selectedSettingIndex >= 0 && selectedSettingIndex < settings.Count)
                        {
                            return settings[selectedSettingIndex].GetAnnouncement();
                        }
                    }
                    break;
            }
            return "";
        }

        private static void AnnounceCurrentState()
        {
            string announcement = GetCurrentItemAnnouncement();
            string position = "";

            switch (currentLevel)
            {
                case StorytellerSelectionLevel.StorytellerList:
                    position = MenuHelper.FormatPosition(selectedStorytellerIndex, storytellers.Count);
                    if (!string.IsNullOrEmpty(position))
                        announcement += "RimWorldAccess.Storyteller.PositionSpacePrefix".Translate(position);
                    announcement += "RimWorldAccess.Storyteller.SwitchToDifficultyHint".Translate();
                    break;

                case StorytellerSelectionLevel.DifficultyList:
                    position = MenuHelper.FormatPosition(selectedDifficultyIndex, difficulties.Count);
                    if (!string.IsNullOrEmpty(position))
                        announcement += "RimWorldAccess.Storyteller.PositionSpacePrefix".Translate(position);
                    announcement += "RimWorldAccess.Storyteller.SwitchToStorytellerHint".Translate();
                    break;

                case StorytellerSelectionLevel.CustomSectionList:
                    position = MenuHelper.FormatPosition(selectedSectionIndex, sections.Count);
                    if (!string.IsNullOrEmpty(position))
                        announcement += "RimWorldAccess.Storyteller.PositionSpacePrefix".Translate(position);
                    break;

                case StorytellerSelectionLevel.CustomSettingsList:
                    if (sections.Count > 0 && selectedSectionIndex < sections.Count)
                    {
                        // Use enabled settings count for correct position announcement
                        var enabledSettings = GetEnabledSettings();
                        int enabledIndex = ActualIndexToEnabledIndex(selectedSettingIndex);
                        position = MenuHelper.FormatPosition(enabledIndex, enabledSettings.Count);
                        if (!string.IsNullOrEmpty(position))
                            announcement += "RimWorldAccess.Storyteller.PositionSpacePrefix".Translate(position);
                    }
                    break;
            }

            TolkHelper.Speak(announcement);
        }

    }
}
