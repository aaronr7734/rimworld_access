using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Steam;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Navigation level for the options menu.
    /// </summary>
    public enum OptionsMenuLevel
    {
        CategoryList,    // Top level: choosing a category
        SettingsList     // Inside a category: adjusting settings
    }

    /// <summary>
    /// Manages a windowless accessible options menu.
    /// Two-level navigation: categories -> settings within category.
    /// Mirrors all settings from RimWorld's Dialog_Options.
    /// </summary>
    public static class WindowlessOptionsMenuState
    {
        private static bool isActive = false;
        private static OptionsMenuLevel currentLevel = OptionsMenuLevel.CategoryList;
        private static int selectedCategoryIndex = 0;
        private static int selectedSettingIndex = 0;
        private static List<OptionCategory> categories = new List<OptionCategory>();
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        public static bool IsActive => isActive;
        public static bool HasActiveSearch => typeahead.HasActiveSearch;
        public static bool HasNoMatches => typeahead.HasNoMatches;
        public static OptionsMenuLevel CurrentLevel => currentLevel;

        /// <summary>
        /// Opens the options menu at the category list level.
        /// </summary>
        public static void Open()
        {
            Open(0);
        }

        /// <summary>
        /// Opens the options menu at a specific category index.
        /// </summary>
        /// <param name="categoryIndex">The category index to open to (0=General, 3=Gameplay, etc.)</param>
        public static void Open(int categoryIndex)
        {
            Open(categoryIndex, -1);
        }

        /// <summary>
        /// Opens the options menu at a specific category and setting index.
        /// </summary>
        /// <param name="categoryIndex">The category index (0=General, 3=Gameplay, etc.)</param>
        /// <param name="settingIndex">The setting index within the category, or -1 to stay at category level</param>
        public static void Open(int categoryIndex, int settingIndex)
        {
            isActive = true;
            typeahead.ClearSearch();

            // Close pause menu
            WindowlessPauseMenuState.Close();

            // Build category list
            BuildCategories();

            // Set selected category (clamped to valid range)
            selectedCategoryIndex = Mathf.Clamp(categoryIndex, 0, Mathf.Max(0, categories.Count - 1));

            // Set level and setting index
            if (settingIndex >= 0 && categories.Count > selectedCategoryIndex)
            {
                var settings = categories[selectedCategoryIndex].Settings;
                selectedSettingIndex = Mathf.Clamp(settingIndex, 0, Mathf.Max(0, settings.Count - 1));
                currentLevel = OptionsMenuLevel.SettingsList;
            }
            else
            {
                selectedSettingIndex = 0;
                currentLevel = OptionsMenuLevel.CategoryList;
            }

            // Announce current state
            AnnounceCurrentState();
        }

        /// <summary>
        /// Closes the options menu and saves preferences.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentLevel = OptionsMenuLevel.CategoryList;
            selectedCategoryIndex = 0;
            selectedSettingIndex = 0;
            typeahead.ClearSearch();

            // Save all preferences when closing, like the native options dialog
            Prefs.Save();

            // Save RimWorld Access settings
            RimWorldAccessMod_Settings.Settings?.Write();
        }

        /// <summary>
        /// Moves selection to next item (category or setting).
        /// </summary>
        public static void SelectNext()
        {
            if (currentLevel == OptionsMenuLevel.CategoryList)
            {
                selectedCategoryIndex = MenuHelper.SelectNext(selectedCategoryIndex, categories.Count);
            }
            else // SettingsList
            {
                var settings = categories[selectedCategoryIndex].Settings;
                if (settings.Count > 0)
                {
                    selectedSettingIndex = MenuHelper.SelectNext(selectedSettingIndex, settings.Count);
                }
            }

            AnnounceCurrentState();
        }

        /// <summary>
        /// Moves selection to previous item (category or setting).
        /// </summary>
        public static void SelectPrevious()
        {
            if (currentLevel == OptionsMenuLevel.CategoryList)
            {
                selectedCategoryIndex = MenuHelper.SelectPrevious(selectedCategoryIndex, categories.Count);
            }
            else // SettingsList
            {
                var settings = categories[selectedCategoryIndex].Settings;
                if (settings.Count > 0)
                {
                    selectedSettingIndex = MenuHelper.SelectPrevious(selectedSettingIndex, settings.Count);
                }
            }

            AnnounceCurrentState();
        }

        /// <summary>
        /// Enters the selected category or toggles/cycles the selected setting.
        /// </summary>
        public static void ExecuteSelected()
        {
            if (currentLevel == OptionsMenuLevel.CategoryList)
            {
                // Enter the selected category
                currentLevel = OptionsMenuLevel.SettingsList;
                selectedSettingIndex = 0;
                typeahead.ClearSearch();
                AnnounceCurrentState();
            }
            else // SettingsList
            {
                // Toggle or cycle the current setting
                var setting = categories[selectedCategoryIndex].Settings[selectedSettingIndex];
                setting.Toggle();
                AnnounceCurrentState();
            }
        }

        /// <summary>
        /// Adjusts slider/dropdown settings left or right.
        /// </summary>
        public static void AdjustSetting(int direction)
        {
            if (currentLevel != OptionsMenuLevel.SettingsList)
                return;

            var setting = categories[selectedCategoryIndex].Settings[selectedSettingIndex];
            setting.Adjust(direction);
            AnnounceCurrentState();
        }

        /// <summary>
        /// Goes back one level or closes the menu.
        /// </summary>
        public static void GoBack()
        {
            if (currentLevel == OptionsMenuLevel.SettingsList)
            {
                // Go back to category list
                currentLevel = OptionsMenuLevel.CategoryList;
                typeahead.ClearSearch();
                AnnounceCurrentState();
            }
            else
            {
                // Close menu
                Close();

                // Only return to pause menu if in-game (not at main menu)
                if (Current.ProgramState == ProgramState.Playing)
                {
                    WindowlessPauseMenuState.Open();
                }
                // At main menu, just close - main menu navigation handles itself
            }
        }

        /// <summary>
        /// Processes a typeahead character for searching the current list.
        /// </summary>
        public static void ProcessTypeaheadCharacter(char c)
        {
            var labels = GetItemLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    if (currentLevel == OptionsMenuLevel.CategoryList)
                        selectedCategoryIndex = newIndex;
                    else
                        selectedSettingIndex = newIndex;
                    AnnounceWithSearch();
                }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
            }
        }

        /// <summary>
        /// Processes backspace key for typeahead search.
        /// </summary>
        public static void ProcessBackspace()
        {
            if (!typeahead.HasActiveSearch)
                return;

            var labels = GetItemLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    if (currentLevel == OptionsMenuLevel.CategoryList)
                        selectedCategoryIndex = newIndex;
                    else
                        selectedSettingIndex = newIndex;
                }
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Clears the typeahead search and announces.
        /// </summary>
        public static void ClearTypeaheadSearch()
        {
            typeahead.ClearSearchAndAnnounce();
        }

        /// <summary>
        /// Jumps to the first item in the current list.
        /// </summary>
        public static void JumpToFirst()
        {
            typeahead.ClearSearch();
            if (currentLevel == OptionsMenuLevel.CategoryList)
            {
                if (categories.Count > 0)
                    selectedCategoryIndex = MenuHelper.JumpToFirst();
            }
            else
            {
                var settings = categories[selectedCategoryIndex].Settings;
                if (settings.Count > 0)
                    selectedSettingIndex = MenuHelper.JumpToFirst();
            }
            AnnounceCurrentState();
        }

        /// <summary>
        /// Jumps to the last item in the current list.
        /// </summary>
        public static void JumpToLast()
        {
            typeahead.ClearSearch();
            if (currentLevel == OptionsMenuLevel.CategoryList)
            {
                if (categories.Count > 0)
                    selectedCategoryIndex = MenuHelper.JumpToLast(categories.Count);
            }
            else
            {
                var settings = categories[selectedCategoryIndex].Settings;
                if (settings.Count > 0)
                    selectedSettingIndex = MenuHelper.JumpToLast(settings.Count);
            }
            AnnounceCurrentState();
        }

        /// <summary>
        /// Selects the next match when typeahead is active.
        /// </summary>
        public static void SelectNextMatch()
        {
            int currentIndex = currentLevel == OptionsMenuLevel.CategoryList ? selectedCategoryIndex : selectedSettingIndex;
            int newIndex = typeahead.GetNextMatch(currentIndex);
            if (newIndex >= 0)
            {
                if (currentLevel == OptionsMenuLevel.CategoryList)
                    selectedCategoryIndex = newIndex;
                else
                    selectedSettingIndex = newIndex;
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Selects the previous match when typeahead is active.
        /// </summary>
        public static void SelectPreviousMatch()
        {
            int currentIndex = currentLevel == OptionsMenuLevel.CategoryList ? selectedCategoryIndex : selectedSettingIndex;
            int newIndex = typeahead.GetPreviousMatch(currentIndex);
            if (newIndex >= 0)
            {
                if (currentLevel == OptionsMenuLevel.CategoryList)
                    selectedCategoryIndex = newIndex;
                else
                    selectedSettingIndex = newIndex;
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Gets the labels for the current list level.
        /// </summary>
        private static List<string> GetItemLabels()
        {
            var labels = new List<string>();
            if (currentLevel == OptionsMenuLevel.CategoryList)
            {
                foreach (var category in categories)
                {
                    labels.Add(category.Name);
                }
            }
            else
            {
                var settings = categories[selectedCategoryIndex].Settings;
                foreach (var setting in settings)
                {
                    labels.Add(setting.Name);
                }
            }
            return labels;
        }

        /// <summary>
        /// Announces the current selection with search context if active.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (typeahead.HasActiveSearch)
            {
                string itemName;
                if (currentLevel == OptionsMenuLevel.CategoryList)
                {
                    itemName = categories[selectedCategoryIndex].Name;
                    TolkHelper.Speak($"{itemName}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'");
                }
                else
                {
                    var setting = categories[selectedCategoryIndex].Settings[selectedSettingIndex];
                    TolkHelper.Speak($"{setting.GetAnnouncement()}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'");
                }
            }
            else
            {
                AnnounceCurrentState();
            }
        }

        private static void AnnounceCurrentState()
        {
            if (currentLevel == OptionsMenuLevel.CategoryList)
            {
                string categoryName = categories[selectedCategoryIndex].Name;
                string positionPart = MenuHelper.FormatPosition(selectedCategoryIndex, categories.Count);
                string announcement = string.IsNullOrEmpty(positionPart)
                    ? $"{categoryName}."
                    : $"{categoryName}. {positionPart}";
                TolkHelper.Speak(announcement);
            }
            else // SettingsList
            {
                var setting = categories[selectedCategoryIndex].Settings[selectedSettingIndex];
                var currentSettings = categories[selectedCategoryIndex].Settings;
                string positionPart = MenuHelper.FormatPosition(selectedSettingIndex, currentSettings.Count);
                string announcement = string.IsNullOrEmpty(positionPart)
                    ? $"{setting.GetAnnouncement()}."
                    : $"{setting.GetAnnouncement()}. {positionPart}";
                TolkHelper.Speak(announcement);
            }
        }

        /// <summary>
        /// Restores all settings to defaults by deleting config files and restarting.
        /// </summary>
        private static void RestoreToDefaultSettings()
        {
            System.IO.FileInfo[] files = new System.IO.DirectoryInfo(GenFilePaths.ConfigFolderPath).GetFiles("*.xml");
            foreach (System.IO.FileInfo fileInfo in files)
            {
                try
                {
                    fileInfo.Delete();
                }
                catch (System.Exception)
                {
                    // Silently ignore deletion failures
                }
            }
            Find.WindowStack.Add(new Dialog_MessageBox("ResetAndRestart".Translate(), null, GenCommandLine.Restart));
        }

        /// <summary>
        /// Formats a day count using the game's translation keys for day/days.
        /// </summary>
        private static string FormatDays(float days)
        {
            if (days == 1f)
                return $"1 {"day".Translate()}";
            return $"{days} {"Days".Translate()}";
        }

        private static void BuildCategories()
        {
            categories.Clear();

            // General Category
            var general = new OptionCategory(OptionCategoryDefOf.General.LabelCap.ToString());

            // Language selection (only on main menu)
            if (Current.ProgramState == ProgramState.Entry)
            {
                general.Settings.Add(new ButtonSetting("ChooseLanguage".Translate().ToString(),
                    () => LanguageDatabase.activeLanguage.FriendlyNameNative + " (Press Enter to change)",
                    () => {
                        List<FloatMenuOption> options = new List<FloatMenuOption>();
                        foreach (LoadedLanguage lang in LanguageDatabase.AllLoadedLanguages)
                        {
                            LoadedLanguage localLang = lang;
                            options.Add(new FloatMenuOption(localLang.FriendlyNameNative, () => LanguageDatabase.SelectLanguage(localLang)));
                        }
                        WindowlessFloatMenuState.Open(options, false);
                    }));
            }

            // Autosave interval - use ChoiceSetting for keyboard navigation
            var autosaveValues = new List<float>();
            var autosaveLabels = new List<string>();

            if (Prefs.DevMode)
            {
                autosaveValues.Add(0.05f); autosaveLabels.Add(FormatDays(0.05f) + " (debug)");
                autosaveValues.Add(0.1f); autosaveLabels.Add(FormatDays(0.1f) + " (debug)");
                autosaveValues.Add(0.25f); autosaveLabels.Add(FormatDays(0.25f) + " (debug)");
            }
            autosaveValues.Add(0.5f); autosaveLabels.Add(FormatDays(0.5f));
            autosaveValues.Add(1f); autosaveLabels.Add(FormatDays(1f));
            autosaveValues.Add(3f); autosaveLabels.Add(FormatDays(3f));
            autosaveValues.Add(7f); autosaveLabels.Add(FormatDays(7f));
            autosaveValues.Add(14f); autosaveLabels.Add(FormatDays(14f));

            general.Settings.Add(new ChoiceSetting("AutosaveInterval".Translate().ToString(),
                () => {
                    // Find current index based on Prefs.AutosaveIntervalDays
                    float current = Prefs.AutosaveIntervalDays;
                    for (int i = 0; i < autosaveValues.Count; i++)
                    {
                        if (Mathf.Approximately(autosaveValues[i], current))
                            return i;
                    }
                    return 0; // Default to first option if not found
                },
                (index) => {
                    if (index >= 0 && index < autosaveValues.Count)
                        Prefs.AutosaveIntervalDays = autosaveValues[index];
                },
                autosaveLabels,
                "AutosaveIntervalTooltip".Translate().ToString()));

            general.Settings.Add(new SliderSetting("AutosavesCount".Translate("").ToString().TrimEnd(' ', ':'), () => Prefs.AutosavesCount, v => Prefs.AutosavesCount = Mathf.RoundToInt(v), 1f, 25f, 1f, false));
            general.Settings.Add(new CheckboxSetting("RunInBackground".Translate().ToString(), () => Prefs.RunInBackground, v => Prefs.RunInBackground = v));

            if (!DevModePermanentlyDisabledUtility.Disabled || Prefs.DevMode)
            {
                general.Settings.Add(new CheckboxSetting("DevelopmentMode".Translate().ToString(), () => Prefs.DevMode, v => Prefs.DevMode = v));
            }

            // Reset to defaults button
            general.Settings.Add(new ButtonSetting("RestoreToDefaultSettingsLabel".Translate().ToString(),
                () => "Press Enter to Reset All Settings",
                () => {
                    // Show confirmation dialog
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "ResetAndRestartConfirmationDialog".Translate(),
                        RestoreToDefaultSettings,
                        destructive: true));
                    TolkHelper.Speak("Opening reset confirmation dialog");
                }));

            categories.Add(general);

            // Graphics Category
            var graphics = new OptionCategory(OptionCategoryDefOf.Graphics.LabelCap.ToString());
            graphics.Settings.Add(new CheckboxSetting("TextureCompression".Translate().ToString(), () => Prefs.TextureCompression, v => Prefs.TextureCompression = v, "TextureCompression_Tooltip".Translate().ToString()));
            graphics.Settings.Add(new CheckboxSetting("PlantWindSway".Translate().ToString(), () => Prefs.PlantWindSway, v => Prefs.PlantWindSway = v));
            graphics.Settings.Add(new SliderSetting("ScreenShakeIntensity".Translate().ToString(), () => Prefs.ScreenShakeIntensity, v => Prefs.ScreenShakeIntensity = v, 0f, 2f, 0.1f, true));
            graphics.Settings.Add(new CheckboxSetting("SmoothCameraJumps".Translate().ToString(), () => Prefs.SmoothCameraJumps, v => Prefs.SmoothCameraJumps = v, "SmoothCameraJumpsDesc".Translate().ToString()));
            graphics.Settings.Add(new CheckboxSetting("GravshipCutscenes".Translate().ToString(), () => Prefs.GravshipCutscenes, v => Prefs.GravshipCutscenes = v, "GravshipCutscenesDesc".Translate().ToString()));
            categories.Add(graphics);

            // Audio Category
            var audio = new OptionCategory(OptionCategoryDefOf.Audio.LabelCap.ToString());
            audio.Settings.Add(new SliderSetting("MasterVolume".Translate().ToString(), () => Prefs.VolumeMaster, v => Prefs.VolumeMaster = v, 0f, 1f, 0.05f, true, "MasterVolumeTooltip".Translate().ToString()));
            audio.Settings.Add(new SliderSetting("GameVolume".Translate().ToString(), () => Prefs.VolumeGame, v => Prefs.VolumeGame = v, 0f, 1f, 0.05f, true, "GameVolumeTooltip".Translate().ToString()));
            audio.Settings.Add(new SliderSetting("MusicVolume".Translate().ToString(), () => Prefs.VolumeMusic, v => Prefs.VolumeMusic = v, 0f, 1f, 0.05f, true, "MusicVolumeTooltip".Translate().ToString()));
            audio.Settings.Add(new SliderSetting("AmbientVolume".Translate().ToString(), () => Prefs.VolumeAmbient, v => Prefs.VolumeAmbient = v, 0f, 1f, 0.05f, true, "AmbientVolumeTooltip".Translate().ToString()));
            audio.Settings.Add(new SliderSetting("UIVolume".Translate().ToString(), () => Prefs.VolumeUI, v => Prefs.VolumeUI = v, 0f, 1f, 0.05f, true, "UIVolumeTooltip".Translate().ToString()));
            categories.Add(audio);

            // Gameplay Category
            var gameplay = new OptionCategory(OptionCategoryDefOf.Gameplay.LabelCap.ToString());

            // Change storyteller (only in-game)
            if (Current.ProgramState == ProgramState.Playing)
            {
                gameplay.Settings.Add(new ButtonSetting("ChangeStoryteller".Translate().ToString(),
                    () => "Press Enter to Modify",
                    () => {
                        if (TutorSystem.AllowAction("ChooseStoryteller"))
                        {
                            // Close options menu before opening storyteller page
                            Close();
                            Find.WindowStack.Add(new Page_SelectStorytellerInGame());
                        }
                        else
                        {
                            TolkHelper.Speak("Cannot change storyteller right now", SpeechPriority.High);
                        }
                    }));
            }

            gameplay.Settings.Add(new SliderSetting("MaxNumberOfPlayerSettlements".Translate("").ToString().TrimEnd(' ', ':'), () => Prefs.MaxNumberOfPlayerSettlements, v => Prefs.MaxNumberOfPlayerSettlements = Mathf.RoundToInt(v), 1f, 5f, 1f, false));
            gameplay.Settings.Add(new CheckboxSetting("PauseOnLoad".Translate().ToString(), () => Prefs.PauseOnLoad, v => Prefs.PauseOnLoad = v));

            gameplay.Settings.Add(new EnumSetting<AutomaticPauseMode>("AutomaticPauseModeSetting".Translate().ToString(), () => Prefs.AutomaticPauseMode, v => Prefs.AutomaticPauseMode = v));
            gameplay.Settings.Add(new CheckboxSetting("LearningHelper".Translate().ToString(), () => Prefs.AdaptiveTrainingEnabled, v => Prefs.AdaptiveTrainingEnabled = v, "LearningHelperTooltip".Translate().ToString()));

            categories.Add(gameplay);

            // Interface Category
            var ui = new OptionCategory(OptionCategoryDefOf.Interface.LabelCap.ToString());
            ui.Settings.Add(new EnumSetting<TemperatureDisplayMode>("TemperatureMode".Translate().ToString(), () => Prefs.TemperatureMode, v => Prefs.TemperatureMode = v));
            ui.Settings.Add(new EnumSetting<ShowWeaponsUnderPortraitMode>("ShowWeaponsUnderPortrait".Translate().ToString(), () => Prefs.ShowWeaponsUnderPortraitMode, v => Prefs.ShowWeaponsUnderPortraitMode = v, "ShowWeaponsUnderPortraitTooltip".Translate().ToString()));
            ui.Settings.Add(new EnumSetting<AnimalNameDisplayMode>("ShowAnimalNames".Translate().ToString(), () => Prefs.AnimalNameMode, v => Prefs.AnimalNameMode = v));

            if (ModsConfig.BiotechActive)
            {
                ui.Settings.Add(new EnumSetting<MechNameDisplayMode>("ShowMechNames".Translate().ToString(), () => Prefs.MechNameMode, v => Prefs.MechNameMode = v));
            }

            ui.Settings.Add(new EnumSetting<DotHighlightDisplayMode>("DotHighlightDisplayMode".Translate().ToString(), () => Prefs.DotHighlightDisplayMode, v => Prefs.DotHighlightDisplayMode = v));
            ui.Settings.Add(new EnumSetting<HighlightStyleMode>("HighlightStyleMode".Translate().ToString(), () => Prefs.HighlightStyleMode, v => Prefs.HighlightStyleMode = v));
            ui.Settings.Add(new CheckboxSetting("ShowRealtimeClock".Translate().ToString(), () => Prefs.ShowRealtimeClock, v => Prefs.ShowRealtimeClock = v));
            ui.Settings.Add(new CheckboxSetting("TwelveHourClockMode".Translate().ToString(), () => Prefs.TwelveHourClockMode, v => Prefs.TwelveHourClockMode = v));
            ui.Settings.Add(new CheckboxSetting("HatsShownOnlyOnMap".Translate().ToString(), () => Prefs.HatsOnlyOnMap, v => Prefs.HatsOnlyOnMap = v));

            if (!SteamDeck.IsSteamDeck)
            {
                ui.Settings.Add(new CheckboxSetting("DisableTinyText".Translate().ToString(), () => Prefs.DisableTinyText, v => {
                    Prefs.DisableTinyText = v;
                    Widgets.ClearLabelCache();
                    GenUI.ClearLabelWidthCache();
                    if (Current.ProgramState == ProgramState.Playing)
                    {
                        Find.ColonistBar.drawer.ClearLabelCache();
                    }
                }));
            }

            ui.Settings.Add(new CheckboxSetting("CustomCursor".Translate().ToString(), () => !Prefs.CustomCursorEnabled, v => Prefs.CustomCursorEnabled = !v));
            ui.Settings.Add(new CheckboxSetting("VisibleMood".Translate().ToString(), () => Prefs.VisibleMood, v => Prefs.VisibleMood = v, "VisibleMoodDesc".Translate().ToString()));
            categories.Add(ui);

            // Controls Category
            var controls = new OptionCategory(OptionCategoryDefOf.Controls.LabelCap.ToString());
            controls.Settings.Add(new SliderSetting("MapDragSensitivity".Translate().ToString(), () => Prefs.MapDragSensitivity, v => Prefs.MapDragSensitivity = v, 0.8f, 2.5f, 0.05f, true));
            controls.Settings.Add(new CheckboxSetting("EdgeScreenScroll".Translate().ToString(), () => Prefs.EdgeScreenScroll, v => Prefs.EdgeScreenScroll = v));
            controls.Settings.Add(new CheckboxSetting("ZoomToMouse".Translate().ToString(), () => Prefs.ZoomToMouse, v => Prefs.ZoomToMouse = v));
            controls.Settings.Add(new CheckboxSetting("ZoomSwitchLayer".Translate().ToString(), () => Prefs.ZoomSwitchWorldLayer, v => Prefs.ZoomSwitchWorldLayer = v, "ZoomSwitchLayer_Tooltip".Translate().ToString()));
            controls.Settings.Add(new CheckboxSetting("RememberDrawStyle".Translate().ToString(), () => Prefs.RememberDrawStlyes, v => Prefs.RememberDrawStlyes = v, "RememberDrawStyle_Tooltip".Translate().ToString()));
            categories.Add(controls);

            // Dev Category (only if dev mode enabled)
            if (Prefs.DevMode)
            {
                var dev = new OptionCategory(OptionCategoryDefOf.Dev.LabelCap.ToString());
                dev.Settings.Add(new CheckboxSetting("EnableTestMapSizes".Translate().ToString(), () => Prefs.TestMapSizes, v => Prefs.TestMapSizes = v));
                dev.Settings.Add(new CheckboxSetting("LogVerbose".Translate().ToString(), () => Prefs.LogVerbose, v => Prefs.LogVerbose = v));
                dev.Settings.Add(new CheckboxSetting("ResetModsConfigOnCrash".Translate().ToString(), () => Prefs.ResetModsConfigOnCrash, v => Prefs.ResetModsConfigOnCrash = v));
                dev.Settings.Add(new CheckboxSetting("DisableQuickStartCryptoSickness".Translate().ToString(), () => Prefs.DisableQuickStartCryptoSickness, v => Prefs.DisableQuickStartCryptoSickness = v));
                dev.Settings.Add(new CheckboxSetting("StartDevPaletteOn".Translate().ToString(), () => Prefs.StartDevPaletteOn, v => Prefs.StartDevPaletteOn = v));
                dev.Settings.Add(new CheckboxSetting("OpenLogOnWarnings".Translate().ToString(), () => Prefs.OpenLogOnWarnings, v => Prefs.OpenLogOnWarnings = v));
                dev.Settings.Add(new CheckboxSetting("CloseLogWindowOnEscape".Translate().ToString(), () => Prefs.CloseLogWindowOnEscape, v => Prefs.CloseLogWindowOnEscape = v));
                categories.Add(dev);
            }

            // RimWorld Access settings - directly editable here
            var accessSettings = new OptionCategory("RimWorld Access");
            accessSettings.Settings.Add(new CheckboxSetting("Wrap Navigation",
                () => RimWorldAccessMod_Settings.Settings?.WrapNavigation ?? false,
                v => { if (RimWorldAccessMod_Settings.Settings != null) RimWorldAccessMod_Settings.Settings.WrapNavigation = v; }));
            accessSettings.Settings.Add(new CheckboxSetting("Announce Position",
                () => RimWorldAccessMod_Settings.Settings?.AnnouncePosition ?? true,
                v => { if (RimWorldAccessMod_Settings.Settings != null) RimWorldAccessMod_Settings.Settings.AnnouncePosition = v; }));
            accessSettings.Settings.Add(new CheckboxSetting("Show Pawn Activity on Map",
                () => RimWorldAccessMod_Settings.Settings?.ShowPawnActivityOnMap ?? true,
                v => { if (RimWorldAccessMod_Settings.Settings != null) RimWorldAccessMod_Settings.Settings.ShowPawnActivityOnMap = v; }));
            accessSettings.Settings.Add(new CheckboxSetting("Show Cover Info for Drafted and Hostile Pawns",
                () => RimWorldAccessMod_Settings.Settings?.ShowCoverInfo ?? true,
                v => { if (RimWorldAccessMod_Settings.Settings != null) RimWorldAccessMod_Settings.Settings.ShowCoverInfo = v; }));
            accessSettings.Settings.Add(new CheckboxSetting("Announce Terrain on Cursor Movement",
                () => RimWorldAccessMod_Settings.Settings?.AnnounceTerrain ?? true,
                v => { if (RimWorldAccessMod_Settings.Settings != null) RimWorldAccessMod_Settings.Settings.AnnounceTerrain = v; }));
            accessSettings.Settings.Add(new CheckboxSetting("Announce Depth Levels in Treeviews",
                () => RimWorldAccessMod_Settings.Settings?.AnnounceLevels ?? true,
                v => { if (RimWorldAccessMod_Settings.Settings != null) RimWorldAccessMod_Settings.Settings.AnnounceLevels = v; }));
            accessSettings.Settings.Add(new CheckboxSetting("Rashad Hates Treeviews (Submenu-Style Navigation)",
                () => RimWorldAccessMod_Settings.Settings?.SubmenuTreeNavigation ?? false,
                v => { if (RimWorldAccessMod_Settings.Settings != null) RimWorldAccessMod_Settings.Settings.SubmenuTreeNavigation = v; },
                "Changes how treeviews work. When you expand a category, it disappears and you navigate only its items. Press Left Arrow to go back. Your position is remembered when you return."));
            categories.Add(accessSettings);

            // Mod Settings Category - list all mods that have settings
            var modSettings = new OptionCategory("ModSettings".Translate().ToString());
            foreach (Mod mod in LoadedModManager.ModHandles)
            {
                if (!mod.SettingsCategory().NullOrEmpty())
                {
                    Mod localMod = mod; // Capture for closure
                    modSettings.Settings.Add(new ButtonSetting(
                        localMod.SettingsCategory(),
                        () => "Press Enter to open",
                        () => {
                            Find.WindowStack.Add(new Dialog_ModSettings(localMod));
                            TolkHelper.Speak($"Opening settings for {localMod.SettingsCategory()}");
                        }));
                }
            }
            if (modSettings.Settings.Count > 0)
            {
                categories.Add(modSettings);
            }
        }

        /// <summary>
        /// Represents a category of settings.
        /// </summary>
        private class OptionCategory
        {
            public string Name { get; }
            public List<OptionSetting> Settings { get; }

            public OptionCategory(string name)
            {
                Name = name;
                Settings = new List<OptionSetting>();
            }
        }

        /// <summary>
        /// Base class for all setting types.
        /// </summary>
        private abstract class OptionSetting
        {
            public string Name { get; }
            public string Tooltip { get; }

            protected OptionSetting(string name, string tooltip = null)
            {
                Name = name;
                Tooltip = tooltip;
            }

            public abstract string GetAnnouncement();
            public abstract void Toggle();
            public abstract void Adjust(int direction);

            protected string AppendTooltip(string announcement)
            {
                if (!string.IsNullOrEmpty(Tooltip))
                    return $"{announcement}. {Tooltip}";
                return announcement;
            }
        }

        /// <summary>
        /// Checkbox setting (boolean).
        /// </summary>
        private class CheckboxSetting : OptionSetting
        {
            private readonly Func<bool> getter;
            private readonly Action<bool> setter;

            public CheckboxSetting(string name, Func<bool> getter, Action<bool> setter, string tooltip = null)
                : base(name, tooltip)
            {
                this.getter = getter;
                this.setter = setter;
            }

            public override string GetAnnouncement()
            {
                bool value = getter();
                string onOff = value ? "On".Translate().ToString() : "Off".Translate().ToString();
                return AppendTooltip($"{Name}: {onOff}");
            }

            public override void Toggle()
            {
                bool current = getter();
                setter(!current);
            }

            public override void Adjust(int direction)
            {
                // Checkboxes toggle on left/right too
                Toggle();
            }
        }

        /// <summary>
        /// Slider setting (float or int).
        /// </summary>
        private class SliderSetting : OptionSetting
        {
            private readonly Func<float> getter;
            private readonly Action<float> setter;
            private readonly float min;
            private readonly float max;
            private readonly float step;
            private readonly bool showAsPercentage;

            public SliderSetting(string name, Func<float> getter, Action<float> setter, float min, float max, float step, bool showAsPercentage, string tooltip = null)
                : base(name, tooltip)
            {
                this.getter = getter;
                this.setter = setter;
                this.min = min;
                this.max = max;
                this.step = step;
                this.showAsPercentage = showAsPercentage;
            }

            public override string GetAnnouncement()
            {
                float value = getter();
                if (showAsPercentage)
                {
                    return AppendTooltip($"{Name}: {Mathf.RoundToInt(value * 100)}%");
                }
                else
                {
                    return AppendTooltip($"{Name}: {value:F1}");
                }
            }

            public override void Toggle()
            {
                // Sliders cycle through values on toggle
                Adjust(1);
            }

            public override void Adjust(int direction)
            {
                float current = getter();
                float newValue = Mathf.Clamp(current + (step * direction), min, max);
                setter(newValue);
            }
        }

        /// <summary>
        /// Enum dropdown setting.
        /// </summary>
        private class EnumSetting<T> : OptionSetting where T : struct, Enum
        {
            private readonly Func<T> getter;
            private readonly Action<T> setter;
            private readonly T[] values;

            public EnumSetting(string name, Func<T> getter, Action<T> setter, string tooltip = null)
                : base(name, tooltip)
            {
                this.getter = getter;
                this.setter = setter;
                this.values = (T[])Enum.GetValues(typeof(T));
            }

            public override string GetAnnouncement()
            {
                T current = getter();
                return AppendTooltip($"{Name}: {current}");
            }

            public override void Toggle()
            {
                Adjust(1);
            }

            public override void Adjust(int direction)
            {
                T current = getter();
                int currentIndex = Array.IndexOf(values, current);
                int newIndex = (currentIndex + direction + values.Length) % values.Length;
                setter(values[newIndex]);
            }
        }

        /// <summary>
        /// Button setting that opens a float menu or performs an action.
        /// </summary>
        private class ButtonSetting : OptionSetting
        {
            private readonly Func<string> valueGetter;
            private readonly Action onActivate;

            public ButtonSetting(string name, Func<string> valueGetter, Action onActivate, string tooltip = null)
                : base(name, tooltip)
            {
                this.valueGetter = valueGetter;
                this.onActivate = onActivate;
            }

            public override string GetAnnouncement()
            {
                return AppendTooltip($"{Name}: {valueGetter()}");
            }

            public override void Toggle()
            {
                onActivate?.Invoke();
            }

            public override void Adjust(int direction)
            {
                // Buttons don't respond to left/right arrows - only Enter
                // Just re-announce the current state
                TolkHelper.Speak(GetAnnouncement());
            }
        }

        /// <summary>
        /// Choice setting that allows cycling through discrete options with arrow keys.
        /// </summary>
        private class ChoiceSetting : OptionSetting
        {
            private readonly Func<int> currentIndexGetter;
            private readonly Action<int> indexSetter;
            private readonly List<string> choiceLabels;

            public ChoiceSetting(string name, Func<int> currentIndexGetter, Action<int> indexSetter, List<string> choiceLabels, string tooltip = null)
                : base(name, tooltip)
            {
                this.currentIndexGetter = currentIndexGetter;
                this.indexSetter = indexSetter;
                this.choiceLabels = choiceLabels;
            }

            public override string GetAnnouncement()
            {
                int index = currentIndexGetter();
                if (index >= 0 && index < choiceLabels.Count)
                {
                    return AppendTooltip($"{Name}: {choiceLabels[index]}");
                }
                return AppendTooltip($"{Name}: Unknown");
            }

            public override void Toggle()
            {
                // Cycle forward on toggle
                Adjust(1);
            }

            public override void Adjust(int direction)
            {
                int currentIndex = currentIndexGetter();
                int newIndex = (currentIndex + direction + choiceLabels.Count) % choiceLabels.Count;
                indexSetter(newIndex);
            }
        }
    }
}
