using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Profile;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages a windowless pause menu accessible via Escape key.
    /// Provides keyboard navigation through pause menu options without rendering UI.
    /// </summary>
    public static class WindowlessPauseMenuState
    {
        private static List<PauseMenuOption> currentOptions = null;
        private static int selectedIndex = 0;
        private static bool isActive = false;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        public static bool IsActive => isActive;
        public static TypeaheadSearchHelper Typeahead => typeahead;

        /// <summary>
        /// Opens the windowless pause menu with appropriate options based on game state.
        /// </summary>
        public static void Open()
        {
            currentOptions = BuildMenuOptions();
            selectedIndex = 0;
            isActive = true;
            typeahead.ClearSearch();

            // Announce first option
            AnnounceCurrentOption();
        }

        /// <summary>
        /// Closes the pause menu.
        /// </summary>
        public static void Close()
        {
            currentOptions = null;
            selectedIndex = 0;
            isActive = false;
            typeahead.ClearSearch();
        }

        /// <summary>
        /// Moves selection to next option.
        /// </summary>
        public static void SelectNext()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, currentOptions.Count);
            AnnounceCurrentOption();
        }

        /// <summary>
        /// Moves selection to previous option.
        /// </summary>
        public static void SelectPrevious()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, currentOptions.Count);
            AnnounceCurrentOption();
        }

        /// <summary>
        /// Executes the currently selected option.
        /// </summary>
        public static void ExecuteSelected()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= currentOptions.Count)
                return;

            PauseMenuOption selected = currentOptions[selectedIndex];

            // Close menu before executing (allows action to open new menu)
            Close();

            // Execute the action
            selected.Action?.Invoke();
        }

        private static void AnnounceCurrentOption()
        {
            if (selectedIndex >= 0 && selectedIndex < currentOptions.Count)
            {
                TolkHelper.Speak($"{currentOptions[selectedIndex].Label}. {MenuHelper.FormatPosition(selectedIndex, currentOptions.Count)}");
            }
        }

        /// <summary>
        /// Gets whether typeahead search is active.
        /// </summary>
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        /// <summary>
        /// Handles keyboard input for the pause menu, including typeahead search.
        /// </summary>
        /// <returns>True if input was handled, false otherwise.</returns>
        public static bool HandleInput()
        {
            if (!isActive || currentOptions == null || currentOptions.Count == 0)
                return false;

            if (Event.current.type != EventType.KeyDown)
                return false;

            KeyCode key = Event.current.keyCode;

            // Handle Escape - clear search FIRST, then close
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    Event.current.Use();
                    return true;
                }
                // Let the caller handle normal escape (close menu)
                return false;
            }

            // Handle Backspace for search
            if (key == KeyCode.Backspace && typeahead.HasActiveSearch)
            {
                var labels = GetItemLabels();
                if (typeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0)
                        selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
                Event.current.Use();
                return true;
            }

            // Handle Up arrow - navigate with search awareness
            if (key == KeyCode.UpArrow)
            {
                if (typeahead.HasActiveSearch)
                {
                    if (typeahead.HasNoMatches)
                    {
                        // No matches - navigate normally but keep search text
                        selectedIndex = MenuHelper.SelectPrevious(selectedIndex, currentOptions.Count);
                        AnnounceWithSearch();
                    }
                    else
                    {
                        int prevIndex = typeahead.GetPreviousMatch(selectedIndex);
                        if (prevIndex >= 0)
                        {
                            selectedIndex = prevIndex;
                            AnnounceWithSearch();
                        }
                    }
                }
                else
                {
                    SelectPrevious();
                }
                Event.current.Use();
                return true;
            }

            // Handle Down arrow - navigate with search awareness
            if (key == KeyCode.DownArrow)
            {
                if (typeahead.HasActiveSearch)
                {
                    if (typeahead.HasNoMatches)
                    {
                        // No matches - navigate normally but keep search text
                        selectedIndex = MenuHelper.SelectNext(selectedIndex, currentOptions.Count);
                        AnnounceWithSearch();
                    }
                    else
                    {
                        int nextIndex = typeahead.GetNextMatch(selectedIndex);
                        if (nextIndex >= 0)
                        {
                            selectedIndex = nextIndex;
                            AnnounceWithSearch();
                        }
                    }
                }
                else
                {
                    SelectNext();
                }
                Event.current.Use();
                return true;
            }

            // Handle Enter - execute selected
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                ExecuteSelected();
                Event.current.Use();
                return true;
            }

            // Handle typeahead characters
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            if (isLetter || isNumber)
            {
                char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                var labels = GetItemLabels();
                if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
                {
                    if (newIndex >= 0)
                    {
                        selectedIndex = newIndex;
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
                }
                Event.current.Use();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the list of labels for all menu items.
        /// </summary>
        private static List<string> GetItemLabels()
        {
            var labels = new List<string>();
            if (currentOptions != null)
            {
                foreach (var option in currentOptions)
                {
                    labels.Add(option.Label);
                }
            }
            return labels;
        }

        /// <summary>
        /// Announces the current selection with search context if applicable.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (!isActive || currentOptions == null || currentOptions.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= currentOptions.Count)
                return;

            string label = currentOptions[selectedIndex].Label;

            if (typeahead.HasActiveSearch)
            {
                if (typeahead.HasNoMatches)
                {
                    TolkHelper.Speak($"{label}. {MenuHelper.FormatPosition(selectedIndex, currentOptions.Count)}. No matches for '{typeahead.LastFailedSearch}'");
                }
                else
                {
                    TolkHelper.Speak($"{label}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'");
                }
            }
            else
            {
                AnnounceCurrentOption();
            }
        }

        /// <summary>
        /// Builds the list of menu options based on current game state.
        /// </summary>
        private static List<PauseMenuOption> BuildMenuOptions()
        {
            List<PauseMenuOption> options = new List<PauseMenuOption>();

            // Only show these options if actually in-game
            if (Current.ProgramState == ProgramState.Playing)
            {
                bool anyGameFiles = GenFilePaths.AllSavedGameFiles.Any();
                bool isPermadeath = Current.Game.Info.permadeathMode;
                bool canSave = !GameDataSaveLoader.SavingIsTemporarilyDisabled;

                // Save option (not in permadeath)
                if (!isPermadeath && canSave)
                {
                    options.Add(new PauseMenuOption(
                        "Save".Translate(),
                        () => WindowlessSaveMenuState.Open(SaveLoadMode.Save)
                    ));
                }

                // Load option (not in permadeath)
                if (!isPermadeath && anyGameFiles)
                {
                    options.Add(new PauseMenuOption(
                        "LoadGame".Translate(),
                        () => WindowlessSaveMenuState.Open(SaveLoadMode.Load)
                    ));
                }

                // Review Scenario
                options.Add(new PauseMenuOption(
                    "ReviewScenario".Translate(),
                    () => {
                        string scenarioText = Find.Scenario.name + ": " + Find.Scenario.GetFullInformationText();
                        TolkHelper.Speak(scenarioText);
                    }
                ));

                // Options
                options.Add(new PauseMenuOption(
                    "Options".Translate(),
                    () => WindowlessOptionsMenuState.Open()
                ));

                // Play Settings (auto-rebuild, auto-expand home area)
                options.Add(new PauseMenuOption(
                    "Play Settings",
                    () => PlaySettingsMenuState.Open()
                ));

                // Quit options for permadeath mode
                if (isPermadeath && canSave)
                {
                    options.Add(new PauseMenuOption(
                        "SaveAndQuitToMainMenu".Translate(),
                        () => {
                            LongEventHandler.QueueLongEvent(delegate {
                                GameDataSaveLoader.SaveGame(Current.Game.Info.permadeathModeUniqueName);
                                MemoryUtility.ClearAllMapsAndWorld();
                            }, "Entry", "SavingLongEvent", doAsynchronously: false, null, showExtraUIInfo: false);
                        }
                    ));

                    options.Add(new PauseMenuOption(
                        "SaveAndQuitToOS".Translate(),
                        () => {
                            LongEventHandler.QueueLongEvent(delegate {
                                GameDataSaveLoader.SaveGame(Current.Game.Info.permadeathModeUniqueName);
                                LongEventHandler.ExecuteWhenFinished(Root.Shutdown);
                            }, "SavingLongEvent", doAsynchronously: false, null, showExtraUIInfo: false);
                        }
                    ));
                }
                else
                {
                    // Regular quit options
                    options.Add(new PauseMenuOption(
                        "QuitToMainMenu".Translate(),
                        () => {
                            if (GameDataSaveLoader.CurrentGameStateIsValuable)
                            {
                                // Show confirmation
                                TolkHelper.Speak("Confirm quit to main menu? Press Enter to confirm, Escape to cancel");
                                WindowlessConfirmationState.Open(
                                    "ConfirmQuit".Translate(),
                                    GenScene.GoToMainMenu
                                );
                            }
                            else
                            {
                                GenScene.GoToMainMenu();
                            }
                        }
                    ));

                    options.Add(new PauseMenuOption(
                        "QuitToOS".Translate(),
                        () => {
                            if (GameDataSaveLoader.CurrentGameStateIsValuable)
                            {
                                // Show confirmation
                                TolkHelper.Speak("Confirm quit to desktop? Press Enter to confirm, Escape to cancel");
                                WindowlessConfirmationState.Open(
                                    "ConfirmQuit".Translate(),
                                    Root.Shutdown
                                );
                            }
                            else
                            {
                                Root.Shutdown();
                            }
                        }
                    ));
                }

                // Resume game (close menu)
                options.Add(new PauseMenuOption(
                    "ResumeGame".Translate(),
                    () => {
                        // Just close the menu
                        TolkHelper.Speak("Resumed game");
                    }
                ));
            }

            return options;
        }

        /// <summary>
        /// Simple data structure for pause menu options.
        /// </summary>
        private class PauseMenuOption
        {
            public string Label { get; }
            public Action Action { get; }

            public PauseMenuOption(string label, Action action)
            {
                Label = label;
                Action = action;
            }
        }
    }
}
