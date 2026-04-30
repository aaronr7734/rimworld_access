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

        // Remembers the label of the last item the user activated, so reopening
        // the pause menu lands on that item again (e.g. Esc out of Save returns
        // the cursor to "Save", not to the top of the list). Cleared by
        // CloseAndResetCursor when the user explicitly leaves the pause menu via
        // Escape, so the next time they pause they start at the top.
        private static string lastSelectedLabel = null;

        // Tracks the Game instance the saved cursor belongs to. Cleared in
        // Open() whenever we detect a different (or null) Game, so quitting
        // to the main menu and loading a new save doesn't carry the cursor
        // forward onto "Quit to main menu" in the new session.
        private static System.WeakReference<Game> lastSessionGame = null;

        // Set when a quit-confirmation dialog is queued. If the user cancels the
        // dialog (game stays running), WindowlessDialogState.Close notifies us via
        // OnWindowClosed and we reopen the pause menu with cursor restored.
        private static System.WeakReference<Window> reopenPendingForDialog = null;

        public static bool IsActive => isActive;
        public static TypeaheadSearchHelper Typeahead => typeahead;

        /// <summary>
        /// Opens the windowless pause menu with appropriate options based on game state.
        /// </summary>
        public static void Open()
        {
            // Reset the cursor when the underlying Game changes. After a
            // "Quit to main menu" + load, Current.Game is a new instance, so
            // the next pause should start at the top instead of restoring
            // the "Quit to main menu" cursor from the previous session.
            Game current = Current.Game;
            Game tracked = null;
            lastSessionGame?.TryGetTarget(out tracked);
            if (current == null || !ReferenceEquals(current, tracked))
            {
                lastSelectedLabel = null;
                reopenPendingForDialog = null;
                lastSessionGame = current != null
                    ? new System.WeakReference<Game>(current)
                    : null;
            }

            currentOptions = BuildMenuOptions();
            selectedIndex = 0;
            if (!string.IsNullOrEmpty(lastSelectedLabel))
            {
                int idx = currentOptions.FindIndex(o => o.Label == lastSelectedLabel);
                if (idx >= 0) selectedIndex = idx;
            }
            isActive = true;
            typeahead.ClearSearch();

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
        /// Closes the pause menu AND forgets the saved cursor position. Called when
        /// the user explicitly leaves the pause menu via Escape — we don't want a
        /// later pause-menu open to land on the last activated item.
        /// </summary>
        public static void CloseAndResetCursor()
        {
            lastSelectedLabel = null;
            reopenPendingForDialog = null;
            Close();
        }

        /// <summary>
        /// Notification from WindowlessDialogState.Close when any windowless dialog
        /// closes. If we were waiting on a quit-confirmation that the user just
        /// cancelled, reopen the pause menu so they can keep navigating.
        /// </summary>
        internal static void OnWindowClosed(Window closedWindow)
        {
            if (reopenPendingForDialog == null || closedWindow == null)
                return;

            if (!reopenPendingForDialog.TryGetTarget(out Window tracked) || tracked != closedWindow)
                return;

            reopenPendingForDialog = null;

            // If the player confirmed the quit, the game is transitioning out — a
            // long event is queued and we shouldn't reopen.
            if (LongEventHandler.AnyEventNowOrWaiting)
                return;
            if (Current.ProgramState != ProgramState.Playing)
                return;

            Open();
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
            lastSelectedLabel = selected.Label;

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

            // Home jumps to first option, End to last.
            if (key == KeyCode.Home)
            {
                selectedIndex = MenuHelper.JumpToFirst();
                typeahead.ClearSearch();
                AnnounceCurrentOption();
                Event.current.Use();
                return true;
            }

            if (key == KeyCode.End)
            {
                selectedIndex = MenuHelper.JumpToLast(currentOptions.Count);
                typeahead.ClearSearch();
                AnnounceCurrentOption();
                Event.current.Use();
                return true;
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
                TypeaheadCharacterBuffer.RequestCharacter(c =>
                {
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
                });
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
                    // Regular quit options. Confirmation goes through a vanilla
                    // Dialog_MessageBox so it gets the same dialog-box UX as every
                    // other confirmation in the mod (announces the warning, Enter to
                    // confirm, Escape to go back). Text key matches MainMenuDrawer.
                    options.Add(new PauseMenuOption(
                        "QuitToMainMenu".Translate(),
                        () => {
                            if (GameDataSaveLoader.CurrentGameStateIsValuable)
                            {
                                ShowQuitConfirmation(GenScene.GoToMainMenu);
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
                                ShowQuitConfirmation(Root.Shutdown);
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
        /// Shows the vanilla "ConfirmQuit" Dialog_MessageBox and registers it with our
        /// pending-dialog tracker so the pause menu reopens (cursor preserved on the
        /// "Quit to ..." item) if the user backs out of the dialog.
        /// </summary>
        private static void ShowQuitConfirmation(Action confirmed)
        {
            var dialog = Dialog_MessageBox.CreateConfirmation(
                "ConfirmQuit".Translate(),
                confirmed,
                destructive: true,
                null,
                WindowLayer.Super);
            reopenPendingForDialog = new System.WeakReference<Window>(dialog);
            Find.WindowStack.Add(dialog);
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
