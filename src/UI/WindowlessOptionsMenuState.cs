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
            isActive = true;
            currentLevel = OptionsMenuLevel.CategoryList;
            selectedCategoryIndex = 0;
            selectedSettingIndex = 0;
            typeahead.ClearSearch();

            // Close pause menu
            WindowlessPauseMenuState.Close();

            // Build category list
            BuildCategories();

            // Announce first category
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
                // Close menu and return to pause menu
                Close();
                WindowlessPauseMenuState.Open();
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
                    TolkHelper.Speak($"Category: {itemName}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'");
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
                TolkHelper.Speak($"Category: {categoryName}. {MenuHelper.FormatPosition(selectedCategoryIndex, categories.Count)}");
            }
            else // SettingsList
            {
                var setting = categories[selectedCategoryIndex].Settings[selectedSettingIndex];
                var currentSettings = categories[selectedCategoryIndex].Settings;
                TolkHelper.Speak($"{setting.GetAnnouncement()}. {MenuHelper.FormatPosition(selectedSettingIndex, currentSettings.Count)}");
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

        private static void BuildCategories()
        {
            categories.Clear();

            // General Category
            var general = new OptionCategory("General");

            // Language selection (only on main menu)
            if (Current.ProgramState == ProgramState.Entry)
            {
                general.Settings.Add(new ButtonSetting("Language",
                    () => LanguageDatabase.activeLanguage.DisplayName + " (Press Enter to change)",
                    () => {
                        List<FloatMenuOption> options = new List<FloatMenuOption>();
                        foreach (LoadedLanguage lang in LanguageDatabase.AllLoadedLanguages)
                        {
                            LoadedLanguage localLang = lang;
                            options.Add(new FloatMenuOption(localLang.DisplayName, () => LanguageDatabase.SelectLanguage(localLang)));
                        }
                        Find.WindowStack.Add(new FloatMenu(options));
                        TolkHelper.Speak("Opening language selection menu");
                    }));
            }

            // Autosave interval - use ChoiceSetting for keyboard navigation
            var autosaveValues = new List<float>();
            var autosaveLabels = new List<string>();

            if (Prefs.DevMode)
            {
                autosaveValues.Add(0.05f); autosaveLabels.Add("0.05 days (debug)");
                autosaveValues.Add(0.1f); autosaveLabels.Add("0.1 days (debug)");
                autosaveValues.Add(0.25f); autosaveLabels.Add("0.25 days (debug)");
            }
            autosaveValues.Add(0.5f); autosaveLabels.Add("0.5 days");
            autosaveValues.Add(1f); autosaveLabels.Add("1 day");
            autosaveValues.Add(3f); autosaveLabels.Add("3 days");
            autosaveValues.Add(7f); autosaveLabels.Add("7 days");
            autosaveValues.Add(14f); autosaveLabels.Add("14 days");

            general.Settings.Add(new ChoiceSetting("Autosave Interval",
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
                autosaveLabels));

            general.Settings.Add(new SliderSetting("Autosaves Count", () => Prefs.AutosavesCount, v => Prefs.AutosavesCount = Mathf.RoundToInt(v), 1f, 25f, 1f, false));
            general.Settings.Add(new CheckboxSetting("Run In Background", () => Prefs.RunInBackground, v => Prefs.RunInBackground = v));

            if (!DevModePermanentlyDisabledUtility.Disabled || Prefs.DevMode)
            {
                general.Settings.Add(new CheckboxSetting("Development Mode", () => Prefs.DevMode, v => Prefs.DevMode = v));
            }

            // Reset to defaults button
            general.Settings.Add(new ButtonSetting("Reset to Defaults",
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
            var graphics = new OptionCategory("Graphics");
            graphics.Settings.Add(new CheckboxSetting("Texture Compression", () => Prefs.TextureCompression, v => Prefs.TextureCompression = v));
            graphics.Settings.Add(new CheckboxSetting("Plant Wind Sway", () => Prefs.PlantWindSway, v => Prefs.PlantWindSway = v));
            graphics.Settings.Add(new SliderSetting("Screen Shake Intensity", () => Prefs.ScreenShakeIntensity, v => Prefs.ScreenShakeIntensity = v, 0f, 2f, 0.1f, true));
            graphics.Settings.Add(new CheckboxSetting("Smooth Camera Jumps", () => Prefs.SmoothCameraJumps, v => Prefs.SmoothCameraJumps = v));
            graphics.Settings.Add(new CheckboxSetting("Gravship Cutscenes", () => Prefs.GravshipCutscenes, v => Prefs.GravshipCutscenes = v));
            categories.Add(graphics);

            // Audio Category
            var audio = new OptionCategory("Audio");
            audio.Settings.Add(new SliderSetting("Master Volume", () => Prefs.VolumeMaster, v => Prefs.VolumeMaster = v, 0f, 1f, 0.05f, true));
            audio.Settings.Add(new SliderSetting("Game Volume", () => Prefs.VolumeGame, v => Prefs.VolumeGame = v, 0f, 1f, 0.05f, true));
            audio.Settings.Add(new SliderSetting("Music Volume", () => Prefs.VolumeMusic, v => Prefs.VolumeMusic = v, 0f, 1f, 0.05f, true));
            audio.Settings.Add(new SliderSetting("Ambient Volume", () => Prefs.VolumeAmbient, v => Prefs.VolumeAmbient = v, 0f, 1f, 0.05f, true));
            audio.Settings.Add(new SliderSetting("UI Volume", () => Prefs.VolumeUI, v => Prefs.VolumeUI = v, 0f, 1f, 0.05f, true));
            categories.Add(audio);

            // Gameplay Category
            var gameplay = new OptionCategory("Gameplay");

            // Change storyteller (only in-game)
            if (Current.ProgramState == ProgramState.Playing)
            {
                gameplay.Settings.Add(new ButtonSetting("Change Storyteller",
                    () => "Press Enter to Modify",
                    () => {
                        if (TutorSystem.AllowAction("ChooseStoryteller"))
                        {
                            // Close options menu before opening storyteller page
                            Close();
                            Find.WindowStack.Add(new Page_SelectStorytellerInGame());
                            TolkHelper.Speak("Opening Storyteller Selection");
                        }
                        else
                        {
                            TolkHelper.Speak("Cannot change storyteller right now", SpeechPriority.High);
                        }
                    }));
            }

            gameplay.Settings.Add(new SliderSetting("Max Player Settlements", () => Prefs.MaxNumberOfPlayerSettlements, v => Prefs.MaxNumberOfPlayerSettlements = Mathf.RoundToInt(v), 1f, 5f, 1f, false));
            gameplay.Settings.Add(new CheckboxSetting("Pause On Load", () => Prefs.PauseOnLoad, v => Prefs.PauseOnLoad = v));

            gameplay.Settings.Add(new EnumSetting<AutomaticPauseMode>("Automatic Pause Mode", () => Prefs.AutomaticPauseMode, v => Prefs.AutomaticPauseMode = v));
            gameplay.Settings.Add(new CheckboxSetting("Adaptive Training", () => Prefs.AdaptiveTrainingEnabled, v => Prefs.AdaptiveTrainingEnabled = v));

            categories.Add(gameplay);

            // Interface Category
            var ui = new OptionCategory("Interface");
            ui.Settings.Add(new EnumSetting<TemperatureDisplayMode>("Temperature Mode", () => Prefs.TemperatureMode, v => Prefs.TemperatureMode = v));
            ui.Settings.Add(new EnumSetting<ShowWeaponsUnderPortraitMode>("Show Weapons Under Portrait", () => Prefs.ShowWeaponsUnderPortraitMode, v => Prefs.ShowWeaponsUnderPortraitMode = v));
            ui.Settings.Add(new EnumSetting<AnimalNameDisplayMode>("Animal Name Display", () => Prefs.AnimalNameMode, v => Prefs.AnimalNameMode = v));

            if (ModsConfig.BiotechActive)
            {
                ui.Settings.Add(new EnumSetting<MechNameDisplayMode>("Mech Name Display", () => Prefs.MechNameMode, v => Prefs.MechNameMode = v));
            }

            ui.Settings.Add(new EnumSetting<DotHighlightDisplayMode>("Dot Highlight Display", () => Prefs.DotHighlightDisplayMode, v => Prefs.DotHighlightDisplayMode = v));
            ui.Settings.Add(new EnumSetting<HighlightStyleMode>("Highlight Style", () => Prefs.HighlightStyleMode, v => Prefs.HighlightStyleMode = v));
            ui.Settings.Add(new CheckboxSetting("Show Realtime Clock", () => Prefs.ShowRealtimeClock, v => Prefs.ShowRealtimeClock = v));
            ui.Settings.Add(new CheckboxSetting("12 Hour Clock", () => Prefs.TwelveHourClockMode, v => Prefs.TwelveHourClockMode = v));
            ui.Settings.Add(new CheckboxSetting("Hats Only On Map", () => Prefs.HatsOnlyOnMap, v => Prefs.HatsOnlyOnMap = v));

            if (!SteamDeck.IsSteamDeck)
            {
                ui.Settings.Add(new CheckboxSetting("Disable Tiny Text", () => Prefs.DisableTinyText, v => {
                    Prefs.DisableTinyText = v;
                    Widgets.ClearLabelCache();
                    GenUI.ClearLabelWidthCache();
                    if (Current.ProgramState == ProgramState.Playing)
                    {
                        Find.ColonistBar.drawer.ClearLabelCache();
                    }
                }));
            }

            ui.Settings.Add(new CheckboxSetting("Custom Cursor", () => !Prefs.CustomCursorEnabled, v => Prefs.CustomCursorEnabled = !v));
            ui.Settings.Add(new CheckboxSetting("Visible Mood", () => Prefs.VisibleMood, v => Prefs.VisibleMood = v));
            categories.Add(ui);

            // Controls Category
            var controls = new OptionCategory("Controls");
            controls.Settings.Add(new SliderSetting("Map Drag Sensitivity", () => Prefs.MapDragSensitivity, v => Prefs.MapDragSensitivity = v, 0.8f, 2.5f, 0.05f, true));
            controls.Settings.Add(new CheckboxSetting("Edge Screen Scroll", () => Prefs.EdgeScreenScroll, v => Prefs.EdgeScreenScroll = v));
            controls.Settings.Add(new CheckboxSetting("Zoom To Mouse", () => Prefs.ZoomToMouse, v => Prefs.ZoomToMouse = v));
            controls.Settings.Add(new CheckboxSetting("Zoom Switch World Layer", () => Prefs.ZoomSwitchWorldLayer, v => Prefs.ZoomSwitchWorldLayer = v));
            controls.Settings.Add(new CheckboxSetting("Remember Draw Styles", () => Prefs.RememberDrawStlyes, v => Prefs.RememberDrawStlyes = v));
            categories.Add(controls);

            // Dev Category (only if dev mode enabled)
            if (Prefs.DevMode)
            {
                var dev = new OptionCategory("Dev");
                dev.Settings.Add(new CheckboxSetting("Test Map Sizes", () => Prefs.TestMapSizes, v => Prefs.TestMapSizes = v));
                dev.Settings.Add(new CheckboxSetting("Log Verbose", () => Prefs.LogVerbose, v => Prefs.LogVerbose = v));
                dev.Settings.Add(new CheckboxSetting("Reset Mods Config On Crash", () => Prefs.ResetModsConfigOnCrash, v => Prefs.ResetModsConfigOnCrash = v));
                dev.Settings.Add(new CheckboxSetting("Disable QuickStart Crypto Sickness", () => Prefs.DisableQuickStartCryptoSickness, v => Prefs.DisableQuickStartCryptoSickness = v));
                dev.Settings.Add(new CheckboxSetting("Start Dev Palette On", () => Prefs.StartDevPaletteOn, v => Prefs.StartDevPaletteOn = v));
                dev.Settings.Add(new CheckboxSetting("Open Log On Warnings", () => Prefs.OpenLogOnWarnings, v => Prefs.OpenLogOnWarnings = v));
                dev.Settings.Add(new CheckboxSetting("Close Log Window On Escape", () => Prefs.CloseLogWindowOnEscape, v => Prefs.CloseLogWindowOnEscape = v));
                categories.Add(dev);
            }

            // Mod Settings Category - list all mods that have settings
            var modSettings = new OptionCategory("Mod Settings");
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

            protected OptionSetting(string name)
            {
                Name = name;
            }

            public abstract string GetAnnouncement();
            public abstract void Toggle();
            public abstract void Adjust(int direction);
        }

        /// <summary>
        /// Checkbox setting (boolean).
        /// </summary>
        private class CheckboxSetting : OptionSetting
        {
            private readonly Func<bool> getter;
            private readonly Action<bool> setter;

            public CheckboxSetting(string name, Func<bool> getter, Action<bool> setter)
                : base(name)
            {
                this.getter = getter;
                this.setter = setter;
            }

            public override string GetAnnouncement()
            {
                bool value = getter();
                return $"{Name}: {(value ? "On" : "Off")}";
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

            public SliderSetting(string name, Func<float> getter, Action<float> setter, float min, float max, float step, bool showAsPercentage)
                : base(name)
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
                    return $"{Name}: {Mathf.RoundToInt(value * 100)}%";
                }
                else
                {
                    return $"{Name}: {value:F1}";
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

            public EnumSetting(string name, Func<T> getter, Action<T> setter)
                : base(name)
            {
                this.getter = getter;
                this.setter = setter;
                this.values = (T[])Enum.GetValues(typeof(T));
            }

            public override string GetAnnouncement()
            {
                T current = getter();
                return $"{Name}: {current}";
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

            public ButtonSetting(string name, Func<string> valueGetter, Action onActivate)
                : base(name)
            {
                this.valueGetter = valueGetter;
                this.onActivate = onActivate;
            }

            public override string GetAnnouncement()
            {
                return $"{Name}: {valueGetter()}";
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

            public ChoiceSetting(string name, Func<int> currentIndexGetter, Action<int> indexSetter, List<string> choiceLabels)
                : base(name)
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
                    return $"{Name}: {choiceLabels[index]}";
                }
                return $"{Name}: Unknown";
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
