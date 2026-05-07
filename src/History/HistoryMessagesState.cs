using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// State management for navigating the Messages/Archive tab in the History window.
    /// Provides two-level navigation (list view and detail view) with filtering,
    /// using TwoLevelMenuHelper for detail view navigation.
    /// </summary>
    public static class HistoryMessagesState
    {
        private static bool isActive = false;
        private static List<HistoryHelper.ArchiveItemWrapper> items = null;
        private static int selectedIndex = 0;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
        private static TwoLevelMenuHelper detailHelper = null;

        // Filter states
        private static bool showLetters = true;
        private static bool showMessages = false;

        /// <summary>
        /// Gets whether the Messages tab navigation is active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets whether we are in detail view.
        /// </summary>
        public static bool IsInDetailView => detailHelper?.IsInDetailView ?? false;

        /// <summary>
        /// Gets whether we are currently in the buttons section of detail view.
        /// </summary>
        public static bool IsInButtonsSection => detailHelper?.IsInButtonsSection ?? false;

        /// <summary>
        /// Gets whether there is an active typeahead search.
        /// </summary>
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        /// <summary>
        /// Gets the typeahead helper for external access.
        /// </summary>
        public static TypeaheadSearchHelper Typeahead => typeahead;

        /// <summary>
        /// Gets the current selected index.
        /// </summary>
        public static int CurrentIndex => selectedIndex;

        /// <summary>
        /// Opens the Messages tab navigation.
        /// </summary>
        public static void Open()
        {
            // Get initial filter states from the game's History window
            var filters = HistoryHelper.GetFilterStates();
            showLetters = filters.showLetters;
            showMessages = filters.showMessages;

            RefreshItems();

            isActive = true;
            selectedIndex = 0;
            typeahead.ClearSearch();

            // Initialize the detail helper
            detailHelper = new TwoLevelMenuHelper(
                getContentLineCount: () => items != null && selectedIndex >= 0 && selectedIndex < items.Count
                    ? items[selectedIndex].TooltipLines.Length
                    : 0,
                populateButtons: PopulateButtonsForCurrentItem,
                getHeaderAnnouncement: () => items != null && selectedIndex >= 0 && selectedIndex < items.Count
                    ? $"{items[selectedIndex].TypeLabel}: {items[selectedIndex].Label}"
                    : "",
                getContentLineAnnouncement: (idx) => items != null && selectedIndex >= 0 && selectedIndex < items.Count
                    && idx >= 0 && idx < items[selectedIndex].TooltipLines.Length
                    ? items[selectedIndex].TooltipLines[idx]
                    : "",
                endOfItemMessage: "End of letter",
                startOfItemMessage: "Start of letter"
            );
            detailHelper.RefreshButtons();

            string hotkeyHint = "Alt+L to show or hide letters, Alt+M to show or hide messages";

            if (items.Count == 0)
            {
                TolkHelper.Speak($"No archived items. {hotkeyHint}.");
                return;
            }

            AnnounceCurrentSelectionWithSuffix(". " + hotkeyHint);
        }

        /// <summary>
        /// Announces the current selection with a suffix (e.g. hotkey hints on tab entry).
        /// </summary>
        private static void AnnounceCurrentSelectionWithSuffix(string suffix)
        {
            if (items == null || items.Count == 0)
                return;
            if (selectedIndex < 0 || selectedIndex >= items.Count)
                return;

            var item = items[selectedIndex];
            string announcement = item.BuildListAnnouncement(selectedIndex, items.Count) + suffix;
            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Closes the Messages tab navigation.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            items = null;
            selectedIndex = 0;
            typeahead.ClearSearch();
            detailHelper?.Reset();
        }

        /// <summary>
        /// Refreshes the items list based on current filter settings.
        /// </summary>
        private static void RefreshItems()
        {
            items = HistoryHelper.CollectArchiveItems(showLetters, showMessages);
        }

        /// <summary>
        /// Toggles the Letters filter (Alt+L).
        /// </summary>
        public static void ToggleLettersFilter()
        {
            showLetters = !showLetters;
            HistoryHelper.SetFilterStates(showLetters, showMessages);

            int oldIndex = selectedIndex;
            RefreshItems();

            // Clamp index to valid range
            if (items.Count > 0)
            {
                selectedIndex = Math.Min(oldIndex, items.Count - 1);
            }
            else
            {
                selectedIndex = 0;
            }

            // Exit detail view when filters change
            detailHelper?.GoBackToList();
            detailHelper?.ResetDetailPosition();
            detailHelper?.RefreshButtons();

            string status = showLetters ? "Showing letters" : "Hiding letters";
            TolkHelper.Speak($"{status}. {items.Count} items.");

            if (items.Count > 0)
            {
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Toggles the Messages filter (Alt+M).
        /// </summary>
        public static void ToggleMessagesFilter()
        {
            showMessages = !showMessages;
            HistoryHelper.SetFilterStates(showLetters, showMessages);

            int oldIndex = selectedIndex;
            RefreshItems();

            // Clamp index to valid range
            if (items.Count > 0)
            {
                selectedIndex = Math.Min(oldIndex, items.Count - 1);
            }
            else
            {
                selectedIndex = 0;
            }

            // Exit detail view when filters change
            detailHelper?.GoBackToList();
            detailHelper?.ResetDetailPosition();
            detailHelper?.RefreshButtons();

            string status = showMessages ? "Showing messages" : "Hiding messages";
            TolkHelper.Speak($"{status}. {items.Count} items.");

            if (items.Count > 0)
            {
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Moves to the next item.
        /// </summary>
        public static void SelectNext()
        {
            if (items == null || items.Count == 0)
                return;

            if (detailHelper != null && detailHelper.IsInDetailView)
            {
                detailHelper.SelectNextDetailPosition();
            }
            else
            {
                selectedIndex = MenuHelper.SelectNext(selectedIndex, items.Count);
                detailHelper?.ResetDetailPosition();
                detailHelper?.RefreshButtons();
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Moves to the previous item.
        /// </summary>
        public static void SelectPrevious()
        {
            if (items == null || items.Count == 0)
                return;

            if (detailHelper != null && detailHelper.IsInDetailView)
            {
                detailHelper.SelectPreviousDetailPosition();
            }
            else
            {
                selectedIndex = MenuHelper.SelectPrevious(selectedIndex, items.Count);
                detailHelper?.ResetDetailPosition();
                detailHelper?.RefreshButtons();
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Jumps to the first item.
        /// </summary>
        public static void JumpToFirst()
        {
            if (items == null || items.Count == 0)
                return;

            bool wasInDetailView = detailHelper?.IsInDetailView ?? false;
            detailHelper?.GoBackToList();
            selectedIndex = MenuHelper.JumpToFirst();
            typeahead.ClearSearch();
            detailHelper?.ResetDetailPosition();
            detailHelper?.RefreshButtons();

            if (wasInDetailView)
                TolkHelper.Speak("Back to list");
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Jumps to the last item.
        /// </summary>
        public static void JumpToLast()
        {
            if (items == null || items.Count == 0)
                return;

            bool wasInDetailView = detailHelper?.IsInDetailView ?? false;
            detailHelper?.GoBackToList();
            selectedIndex = MenuHelper.JumpToLast(items.Count);
            typeahead.ClearSearch();
            detailHelper?.ResetDetailPosition();
            detailHelper?.RefreshButtons();

            if (wasInDetailView)
                TolkHelper.Speak("Back to list");
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Sets the current index directly.
        /// </summary>
        public static void SetCurrentIndex(int index)
        {
            if (items != null && index >= 0 && index < items.Count)
            {
                selectedIndex = index;
            }
        }

        /// <summary>
        /// Gets labels for typeahead search.
        /// </summary>
        public static List<string> GetLabels()
        {
            return HistoryHelper.GetArchiveLabels(items ?? new List<HistoryHelper.ArchiveItemWrapper>());
        }

        /// <summary>
        /// Handles typeahead character input.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            var labels = GetLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    detailHelper?.RefreshButtons();
                    AnnounceWithSearch();
                }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
            }
        }

        /// <summary>
        /// Handles backspace for search.
        /// </summary>
        public static void HandleBackspace()
        {
            if (!typeahead.HasActiveSearch)
                return;

            var labels = GetLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    detailHelper?.RefreshButtons();
                }
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Enters detail view for the current item.
        /// </summary>
        public static void EnterDetailView()
        {
            if (items == null || items.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= items.Count)
                return;

            typeahead.ClearSearch();
            detailHelper?.RefreshButtons();
            detailHelper?.EnterDetailView();
            detailHelper?.AnnounceDetailPosition();
        }

        /// <summary>
        /// Goes back from detail view to list view.
        /// </summary>
        public static void GoBackToList()
        {
            if (detailHelper != null && detailHelper.GoBackToList())
            {
                typeahead.ClearSearch();
                TolkHelper.Speak("Back to list");
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Handles the Escape key: goes back from detail view, or returns false for parent to handle.
        /// </summary>
        public static bool HandleEscape()
        {
            if (detailHelper != null && detailHelper.IsInDetailView)
            {
                GoBackToList();
                return true;
            }

            // Let parent handle (close the tab/window)
            return false;
        }

        /// <summary>
        /// Navigates to the next button (Right arrow).
        /// </summary>
        public static void SelectNextButton()
        {
            detailHelper?.SelectNextButton();
        }

        /// <summary>
        /// Navigates to the previous button (Left arrow).
        /// </summary>
        public static void SelectPreviousButton()
        {
            detailHelper?.SelectPreviousButton();
        }

        /// <summary>
        /// Activates the currently selected button.
        /// </summary>
        public static void ActivateCurrentButton()
        {
            if (detailHelper == null)
                return;

            if (detailHelper.ActivateCurrentButton())
            {
                var button = detailHelper.GetCurrentButton();
                if (button != null)
                {
                    try
                    {
                        button.Action?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"RimWorld Access: Failed to activate button: {ex.Message}");
                        TolkHelper.Speak("Failed to activate button");
                    }
                }
            }
        }

        /// <summary>
        /// Toggles the pin status of the current item.
        /// </summary>
        public static void TogglePin()
        {
            if (items == null || items.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= items.Count)
                return;

            var item = items[selectedIndex];
            item.TogglePin();

            string status = item.IsPinned ? "Pinned" : "Unpinned";
            TolkHelper.Speak(status);

            // Refresh buttons if in detail view (pin/unpin button label changes)
            if (detailHelper != null && detailHelper.IsInDetailView)
            {
                detailHelper.RefreshButtons();
            }
        }

        /// <summary>
        /// Jumps to the location of the current item and closes the History window.
        /// </summary>
        public static void JumpToLocation()
        {
            if (items == null || items.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= items.Count)
                return;

            var item = items[selectedIndex];

            if (!item.HasValidTarget)
            {
                TolkHelper.Speak("No location available");
                return;
            }

            // Close the History window before jumping
            CloseHistoryWindow();

            item.JumpTo();
        }

        /// <summary>
        /// Closes the History window and cleans up all state.
        /// </summary>
        private static void CloseHistoryWindow()
        {
            // Close our states first
            Close();
            HistoryStatisticsState.Close();
            HistoryState.Close();

            // Close the actual History window
            var historyWindow = HistoryHelper.GetOpenHistoryWindow();
            if (historyWindow != null)
            {
                Find.WindowStack.TryRemove(historyWindow, doCloseSound: false);
            }
        }

        /// <summary>
        /// Opens the current item's full content.
        /// </summary>
        public static void OpenCurrentItem()
        {
            if (items == null || items.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= items.Count)
                return;

            items[selectedIndex].Open();
        }

        #region Private Helper Methods

        /// <summary>
        /// Populates the button list for the current item.
        /// This is the delegate passed to TwoLevelMenuHelper.
        /// </summary>
        private static void PopulateButtonsForCurrentItem(List<ButtonInfo> buttons)
        {
            if (items == null || items.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= items.Count)
                return;

            var item = items[selectedIndex];

            // Open button
            buttons.Add(new ButtonInfo
            {
                Label = "Open",
                Action = () =>
                {
                    item.Open();
                    // Stay in the menu after opening
                }
            });

            // Jump to Location button (if valid target)
            if (item.HasValidTarget)
            {
                buttons.Add(new ButtonInfo
                {
                    Label = "Jump to Location",
                    Action = () =>
                    {
                        // Close the History window before jumping
                        CloseHistoryWindow();
                        item.JumpTo();
                    }
                });
            }

            // Pin/Unpin button
            buttons.Add(new ButtonInfo
            {
                Label = item.IsPinned ? "Unpin" : "Pin",
                Action = () =>
                {
                    item.TogglePin();
                    detailHelper?.RefreshButtons();
                    TolkHelper.Speak(item.IsPinned ? "Pinned" : "Unpinned");
                }
            });
        }

        /// <summary>
        /// Announces the current selection in list view.
        /// </summary>
        private static void AnnounceCurrentSelection()
        {
            if (items == null || items.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= items.Count)
                return;

            if (detailHelper != null && detailHelper.IsInDetailView)
            {
                detailHelper.AnnounceDetailPosition();
                return;
            }

            var item = items[selectedIndex];
            string announcement = item.BuildListAnnouncement(selectedIndex, items.Count);
            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Announces current selection with search context.
        /// </summary>
        public static void AnnounceWithSearch()
        {
            if (items == null || items.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= items.Count)
                return;

            var item = items[selectedIndex];
            string announcement = item.BuildListAnnouncement(selectedIndex, items.Count);

            if (typeahead.HasActiveSearch)
            {
                announcement += $", match {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} for '{typeahead.SearchBuffer}'";
            }

            TolkHelper.Speak(announcement);
        }

        #endregion

        /// <summary>
        /// Handles keyboard input for the Messages tab.
        /// Returns true if input was handled.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive)
                return false;

            // Let Tab key pass through to HistoryState for tab switching
            if (key == KeyCode.Tab)
                return false;

            // Home - jump to start of detail view or first item in list
            if (key == KeyCode.Home)
            {
                if (IsInDetailView)
                    detailHelper?.JumpToDetailStart();
                else
                    JumpToFirst();
                return true;
            }

            // End - jump to end of detail view (buttons) or last item in list
            if (key == KeyCode.End)
            {
                if (IsInDetailView)
                    detailHelper?.JumpToDetailEnd();
                else
                    JumpToLast();
                return true;
            }

            // Escape - clear search first, then go back
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentSelection();
                    return true;
                }
                else if (HandleEscape())
                {
                    return true;
                }
                // Let parent handle
                return false;
            }

            // Backspace - handle search (only in list view)
            if (key == KeyCode.Backspace && !IsInDetailView)
            {
                if (typeahead.HasActiveSearch)
                {
                    HandleBackspace();
                    return true;
                }
                return false;
            }

            // Down arrow
            if (key == KeyCode.DownArrow)
            {
                if (!IsInDetailView && typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    int newIndex = typeahead.GetNextMatch(selectedIndex);
                    if (newIndex >= 0)
                    {
                        selectedIndex = newIndex;
                        detailHelper?.RefreshButtons();
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    SelectNext();
                }
                return true;
            }

            // Up arrow
            if (key == KeyCode.UpArrow)
            {
                if (!IsInDetailView && typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    int newIndex = typeahead.GetPreviousMatch(selectedIndex);
                    if (newIndex >= 0)
                    {
                        selectedIndex = newIndex;
                        detailHelper?.RefreshButtons();
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    SelectPrevious();
                }
                return true;
            }

            // Left arrow - navigate to previous button (helper announces appropriate message if not valid)
            if (key == KeyCode.LeftArrow)
            {
                SelectPreviousButton();
                return true;
            }

            // Right arrow - navigate to next button (helper announces appropriate message if not valid)
            if (key == KeyCode.RightArrow)
            {
                SelectNextButton();
                return true;
            }

            // Enter - open detail view or activate button
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                if (!IsInDetailView)
                {
                    EnterDetailView();
                }
                else if (IsInButtonsSection)
                {
                    ActivateCurrentButton();
                }
                return true;
            }

            // Alt+L - toggle Letters filter
            if (key == KeyCode.L && alt && !shift && !ctrl)
            {
                ToggleLettersFilter();
                return true;
            }

            // Alt+M - toggle Messages filter
            if (key == KeyCode.M && alt && !shift && !ctrl)
            {
                ToggleMessagesFilter();
                return true;
            }

            // Alt+P - toggle pin (shortcut)
            if (key == KeyCode.P && alt && !shift && !ctrl)
            {
                TogglePin();
                return true;
            }

            // Alt+J - jump to location (shortcut)
            if (key == KeyCode.J && alt && !shift && !ctrl)
            {
                JumpToLocation();
                return true;
            }

            // Typeahead characters (letters and numbers, NOT with Alt modifier, only in list view)
            if (!alt && !IsInDetailView)
            {
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                if (isLetter || isNumber)
                {
                    return true;
                }
            }

            // Block ALL unhandled keys to prevent game's native handlers from processing them
            // This makes the History tab modal - it captures all keyboard input while active
            return true;
        }
    }
}
