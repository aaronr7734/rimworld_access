using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State for editing custom difficulty settings during character creation.
    /// Provides 2-level navigation: SectionList → SettingsList.
    /// Uses shared DifficultySettingsHelper for section/setting building.
    /// </summary>
    public static class CustomDifficultyEditState
    {
        public static bool IsActive { get; private set; }

        private enum Level { SectionList, SettingsList }
        private static Level currentLevel = Level.SectionList;

        private static int sectionIndex = 0;
        private static int settingIndex = 0;
        private static List<DifficultySection> sections = new List<DifficultySection>();
        private static Difficulty targetDifficulty;
        private static Page_SelectStoryteller targetPage;

        // Typeahead search for sections and settings
        private static TypeaheadSearchHelper sectionTypeahead = new TypeaheadSearchHelper();
        private static TypeaheadSearchHelper settingTypeahead = new TypeaheadSearchHelper();

        public static bool HasActiveSearch => currentLevel == Level.SectionList
            ? sectionTypeahead.HasActiveSearch
            : settingTypeahead.HasActiveSearch;

        public static bool IsInSettingsList => currentLevel == Level.SettingsList;

        // ===== OPEN/CLOSE =====

        public static void Open(Page_SelectStoryteller page)
        {
            targetPage = page;

            // Get the difficultyValues from the page via reflection
            targetDifficulty = (Difficulty)AccessTools.Field(typeof(Page_SelectStoryteller), "difficultyValues").GetValue(page);

            if (targetDifficulty == null)
            {
                Log.Error("[RimWorld Access] Could not get difficultyValues from Page_SelectStoryteller");
                return;
            }

            // Build sections using shared helper
            sections = DifficultySettingsHelper.BuildSections(
                targetDifficulty,
                onReset: OnResetToPreset,
                onAnomalyPlaystyleChanged: OnAnomalyPlaystyleChanged
            );

            sectionIndex = 0;
            settingIndex = 0;
            currentLevel = Level.SectionList;
            sectionTypeahead.ClearSearch();
            settingTypeahead.ClearSearch();
            IsActive = true;

            TolkHelper.Speak("DifficultyCustomSectionLabel".Translate());
            AnnounceSection();
        }

        public static void Close()
        {
            IsActive = false;
            targetPage = null;
            targetDifficulty = null;
            sections.Clear();
            sectionTypeahead.ClearSearch();
            settingTypeahead.ClearSearch();
        }

        /// <summary>
        /// Attempts to go back one level. Returns false if already at top level.
        /// </summary>
        public static bool GoBack()
        {
            if (currentLevel == Level.SettingsList)
            {
                currentLevel = Level.SectionList;
                settingTypeahead.ClearSearch();
                TolkHelper.Speak("Section list");
                AnnounceSection();
                return true;
            }
            return false;
        }

        // ===== CALLBACKS =====

        private static void OnResetToPreset(DifficultyDef def)
        {
            if (targetDifficulty == null) return;
            targetDifficulty.CopyFrom(def);
            TolkHelper.Speak($"All settings set to {def.LabelCap}");

            // Rebuild sections to reflect new values
            sections = DifficultySettingsHelper.BuildSections(
                targetDifficulty,
                onReset: OnResetToPreset,
                onAnomalyPlaystyleChanged: OnAnomalyPlaystyleChanged
            );

            // Return to section list after reset
            ReturnToSectionList();
        }

        private static void OnAnomalyPlaystyleChanged()
        {
            // Rebuild sections when anomaly playstyle changes (enables/disables related sliders)
            sections = DifficultySettingsHelper.BuildSections(
                targetDifficulty,
                onReset: OnResetToPreset,
                onAnomalyPlaystyleChanged: OnAnomalyPlaystyleChanged
            );
        }

        // ===== NAVIGATION =====

        public static void SelectNext()
        {
            if (currentLevel == Level.SectionList)
            {
                sectionTypeahead.ClearSearch();
                sectionIndex = MenuHelper.SelectNext(sectionIndex, sections.Count);
                AnnounceSection();
            }
            else
            {
                settingTypeahead.ClearSearch();
                var settings = GetEnabledSettings();
                settingIndex = MenuHelper.SelectNext(settingIndex, settings.Count);
                AnnounceSetting();
            }
        }

        public static void SelectPrevious()
        {
            if (currentLevel == Level.SectionList)
            {
                sectionTypeahead.ClearSearch();
                sectionIndex = MenuHelper.SelectPrevious(sectionIndex, sections.Count);
                AnnounceSection();
            }
            else
            {
                settingTypeahead.ClearSearch();
                var settings = GetEnabledSettings();
                settingIndex = MenuHelper.SelectPrevious(settingIndex, settings.Count);
                AnnounceSetting();
            }
        }

        public static void NavigateHome()
        {
            if (currentLevel == Level.SectionList)
            {
                sectionTypeahead.ClearSearch();
                sectionIndex = 0;
                AnnounceSection();
            }
            else
            {
                settingTypeahead.ClearSearch();
                settingIndex = 0;
                AnnounceSetting();
            }
        }

        public static void NavigateEnd()
        {
            if (currentLevel == Level.SectionList)
            {
                sectionTypeahead.ClearSearch();
                sectionIndex = sections.Count - 1;
                AnnounceSection();
            }
            else
            {
                settingTypeahead.ClearSearch();
                var settings = GetEnabledSettings();
                settingIndex = settings.Count - 1;
                AnnounceSetting();
            }
        }

        /// <summary>
        /// Enter the selected section's settings list, or toggle/execute a setting.
        /// </summary>
        public static void ExecuteOrEnter()
        {
            if (currentLevel == Level.SectionList)
            {
                // Enter settings list
                currentLevel = Level.SettingsList;
                settingIndex = 0;
                settingTypeahead.ClearSearch();

                var section = GetCurrentSection();
                if (section != null)
                {
                    var enabledSettings = GetEnabledSettings();
                    int settingCount = enabledSettings.Count;
                    TolkHelper.Speak($"{section.Name}. {settingCount} {section.ItemsLabel}");
                    if (settingCount > 0)
                    {
                        AnnounceSetting();
                    }
                    else
                    {
                        TolkHelper.Speak("No settings available in this section");
                    }
                }
            }
            else
            {
                // Toggle/execute the current setting
                var setting = GetCurrentSetting();
                if (setting != null)
                {
                    setting.Toggle();
                    // Only announce if we're still in the settings list
                    // (reset settings change level back to SectionList and handle their own announcements)
                    if (currentLevel == Level.SettingsList)
                    {
                        TolkHelper.Speak(setting.GetAdjustmentAnnouncement());
                    }
                }
            }
        }

        /// <summary>
        /// Adjust the current setting left (decrease).
        /// </summary>
        public static void AdjustLeft()
        {
            if (currentLevel != Level.SettingsList) return;

            var setting = GetCurrentSetting();
            if (setting != null)
            {
                setting.Adjust(-1);
                TolkHelper.Speak(setting.GetAdjustmentAnnouncement());
            }
        }

        /// <summary>
        /// Adjust the current setting right (increase).
        /// </summary>
        public static void AdjustRight()
        {
            if (currentLevel != Level.SettingsList) return;

            var setting = GetCurrentSetting();
            if (setting != null)
            {
                setting.Adjust(1);
                TolkHelper.Speak(setting.GetAdjustmentAnnouncement());
            }
        }

        // ===== TYPEAHEAD SEARCH =====

        public static bool HandleTypeahead(char character)
        {
            if (currentLevel == Level.SectionList)
            {
                var labels = sections.Select(s => s.Name).ToList();
                if (sectionTypeahead.ProcessCharacterInput(character, labels, out int newIndex))
                {
                    if (newIndex >= 0)
                    {
                        sectionIndex = newIndex;
                        AnnounceSectionWithSearch();
                    }
                }
                else
                {
                    sectionTypeahead.SpeakNoMatches();
                }
                return true;
            }
            else
            {
                var settings = GetEnabledSettings();
                var labels = settings.Select(s => s.Label).ToList();
                if (settingTypeahead.ProcessCharacterInput(character, labels, out int newIndex))
                {
                    if (newIndex >= 0)
                    {
                        settingIndex = newIndex;
                        AnnounceSettingWithSearch();
                    }
                }
                else
                {
                    settingTypeahead.SpeakNoMatches();
                }
                return true;
            }
        }

        public static bool HandleBackspace()
        {
            if (currentLevel == Level.SectionList)
            {
                if (!sectionTypeahead.HasActiveSearch) return false;

                var labels = sections.Select(s => s.Name).ToList();
                if (sectionTypeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0)
                    {
                        sectionIndex = newIndex;
                        AnnounceSectionWithSearch();
                    }
                }
                return true;
            }
            else
            {
                if (!settingTypeahead.HasActiveSearch) return false;

                var settings = GetEnabledSettings();
                var labels = settings.Select(s => s.Label).ToList();
                if (settingTypeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0)
                    {
                        settingIndex = newIndex;
                        AnnounceSettingWithSearch();
                    }
                }
                return true;
            }
        }

        public static bool ClearSearch()
        {
            if (currentLevel == Level.SectionList)
            {
                if (sectionTypeahead.ClearSearchAndAnnounce())
                {
                    AnnounceSection();
                    return true;
                }
                return false;
            }
            else
            {
                if (settingTypeahead.ClearSearchAndAnnounce())
                {
                    AnnounceSetting();
                    return true;
                }
                return false;
            }
        }

        public static void SelectNextMatch()
        {
            if (currentLevel == Level.SectionList)
            {
                if (!sectionTypeahead.HasActiveSearch) return;
                int next = sectionTypeahead.GetNextMatch(sectionIndex);
                if (next >= 0)
                {
                    sectionIndex = next;
                    AnnounceSectionWithSearch();
                }
            }
            else
            {
                if (!settingTypeahead.HasActiveSearch) return;
                int next = settingTypeahead.GetNextMatch(settingIndex);
                if (next >= 0)
                {
                    settingIndex = next;
                    AnnounceSettingWithSearch();
                }
            }
        }

        public static void SelectPreviousMatch()
        {
            if (currentLevel == Level.SectionList)
            {
                if (!sectionTypeahead.HasActiveSearch) return;
                int prev = sectionTypeahead.GetPreviousMatch(sectionIndex);
                if (prev >= 0)
                {
                    sectionIndex = prev;
                    AnnounceSectionWithSearch();
                }
            }
            else
            {
                if (!settingTypeahead.HasActiveSearch) return;
                int prev = settingTypeahead.GetPreviousMatch(settingIndex);
                if (prev >= 0)
                {
                    settingIndex = prev;
                    AnnounceSettingWithSearch();
                }
            }
        }

        // ===== RESET SECTION SHORTCUT =====

        /// <summary>
        /// Jumps directly into the reset/playstyle section's settings (Alt+R shortcut).
        /// </summary>
        public static void JumpToResetSection()
        {
            // The reset section is the last section
            if (sections.Count == 0) return;

            sectionIndex = sections.Count - 1;
            settingIndex = 0;
            currentLevel = Level.SettingsList;
            sectionTypeahead.ClearSearch();
            settingTypeahead.ClearSearch();

            var section = GetCurrentSection();
            if (section != null)
            {
                var enabledSettings = GetEnabledSettings();
                TolkHelper.Speak($"{section.Name}. {enabledSettings.Count} {section.ItemsLabel}");
                if (enabledSettings.Count > 0)
                {
                    AnnounceSetting();
                }
            }
        }

        /// <summary>
        /// Called after a reset preset is selected to return to section list.
        /// </summary>
        public static void ReturnToSectionList()
        {
            currentLevel = Level.SectionList;
            sectionIndex = 0;
            settingTypeahead.ClearSearch();
            TolkHelper.Speak("Section list");
            AnnounceSection();
        }

        // ===== HELPERS =====

        private static DifficultySection GetCurrentSection()
        {
            if (sectionIndex < 0 || sectionIndex >= sections.Count)
                return null;
            return sections[sectionIndex];
        }

        private static List<DifficultySetting> GetEnabledSettings()
        {
            var section = GetCurrentSection();
            if (section == null) return new List<DifficultySetting>();
            return section.Settings.Where(s => s.IsEnabled).ToList();
        }

        private static DifficultySetting GetCurrentSetting()
        {
            var settings = GetEnabledSettings();
            if (settingIndex < 0 || settingIndex >= settings.Count)
                return null;
            return settings[settingIndex];
        }

        // ===== ANNOUNCEMENTS =====

        private static void AnnounceSection()
        {
            var section = GetCurrentSection();
            if (section == null) return;

            var enabledSettings = GetEnabledSettings();
            string position = MenuHelper.FormatPosition(sectionIndex, sections.Count);
            string text = $"{section.Name}. {enabledSettings.Count} {section.ItemsLabel}";
            if (!string.IsNullOrEmpty(position))
            {
                text += $" ({position})";
            }
            TolkHelper.Speak(text);
        }

        private static void AnnounceSectionWithSearch()
        {
            var section = GetCurrentSection();
            if (section == null) return;

            if (sectionTypeahead.HasActiveSearch)
            {
                TolkHelper.Speak(sectionTypeahead.BuildItemAnnouncement(section.Name));
            }
            else
            {
                AnnounceSection();
            }
        }

        private static void AnnounceSetting()
        {
            var setting = GetCurrentSetting();
            if (setting == null)
            {
                TolkHelper.Speak("No setting selected");
                return;
            }

            var settings = GetEnabledSettings();
            string position = MenuHelper.FormatPosition(settingIndex, settings.Count);
            string text = setting.GetAnnouncement();
            if (!string.IsNullOrEmpty(position))
            {
                text += $" ({position})";
            }
            TolkHelper.Speak(text);
        }

        private static void AnnounceSettingWithSearch()
        {
            var setting = GetCurrentSetting();
            if (setting == null) return;

            if (settingTypeahead.HasActiveSearch)
            {
                TolkHelper.Speak(settingTypeahead.BuildItemAnnouncement(setting.Label));
            }
            else
            {
                AnnounceSetting();
            }
        }
    }
}
