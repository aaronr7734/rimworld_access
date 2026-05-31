using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// State management for navigating the Statistics tab in the History window.
    /// Provides Up/Down navigation through colony statistics with typeahead search.
    /// </summary>
    public static class HistoryStatisticsState
    {
        private static bool isActive = false;
        private static List<HistoryHelper.StatisticEntry> statistics = null;
        private static int selectedIndex = 0;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        /// <summary>
        /// Gets whether the Statistics tab navigation is active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets whether there is an active typeahead search.
        /// </summary>
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        /// <summary>
        /// Gets the typeahead helper for external access (e.g., for match navigation).
        /// </summary>
        public static TypeaheadSearchHelper Typeahead => typeahead;

        /// <summary>
        /// Gets the current selected index.
        /// </summary>
        public static int CurrentIndex => selectedIndex;

        /// <summary>
        /// Opens the Statistics tab navigation.
        /// </summary>
        public static void Open()
        {
            statistics = HistoryHelper.CollectStatistics();
            isActive = true;
            selectedIndex = 0;
            typeahead.ClearSearch();

            if (statistics.Count == 0)
            {
                TolkHelper.Speak("No statistics available");
                return;
            }

            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Closes the Statistics tab navigation.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            statistics = null;
            selectedIndex = 0;
            typeahead.ClearSearch();
        }

        /// <summary>
        /// Moves to the next statistic.
        /// </summary>
        public static void SelectNext()
        {
            if (statistics == null || statistics.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, statistics.Count);
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Moves to the previous statistic.
        /// </summary>
        public static void SelectPrevious()
        {
            if (statistics == null || statistics.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, statistics.Count);
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Jumps to the first statistic.
        /// </summary>
        public static void JumpToFirst()
        {
            if (statistics == null || statistics.Count == 0)
                return;

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                selectedIndex = typeahead.GetFirstMatch();
                AnnounceWithSearch();
                return;
            }

            selectedIndex = MenuHelper.JumpToFirst();
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Jumps to the last statistic.
        /// </summary>
        public static void JumpToLast()
        {
            if (statistics == null || statistics.Count == 0)
                return;

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                selectedIndex = typeahead.GetLastMatch();
                AnnounceWithSearch();
                return;
            }

            selectedIndex = MenuHelper.JumpToLast(statistics.Count);
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Sets the current index directly.
        /// </summary>
        public static void SetCurrentIndex(int index)
        {
            if (statistics != null && index >= 0 && index < statistics.Count)
            {
                selectedIndex = index;
            }
        }

        /// <summary>
        /// Gets labels for typeahead search.
        /// </summary>
        public static List<string> GetLabels()
        {
            return HistoryHelper.GetStatisticLabels(statistics ?? new List<HistoryHelper.StatisticEntry>());
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
                    selectedIndex = newIndex;
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Announces the current selection.
        /// </summary>
        private static void AnnounceCurrentSelection()
        {
            if (statistics == null || statistics.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= statistics.Count)
                return;

            var stat = statistics[selectedIndex];
            string announcement = stat.ToAnnouncement();
            string position = MenuHelper.FormatPosition(selectedIndex, statistics.Count);
            if (!string.IsNullOrEmpty(position))
                announcement += $" {position}";

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Announces current selection with search context.
        /// </summary>
        public static void AnnounceWithSearch()
        {
            if (statistics == null || statistics.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= statistics.Count)
                return;

            var stat = statistics[selectedIndex];
            string announcement = stat.ToAnnouncement();
            string position = MenuHelper.FormatPosition(selectedIndex, statistics.Count);
            if (!string.IsNullOrEmpty(position))
                announcement += $" {position}";

            if (typeahead.HasActiveSearch)
            {
                announcement += $", match {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} for '{typeahead.SearchBuffer}'";
            }

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Handles keyboard input for the Statistics tab.
        /// Returns true if input was handled.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive)
                return false;

            // Let Tab key pass through to HistoryState for tab switching
            if (key == KeyCode.Tab)
                return false;

            // Home - jump to first
            if (key == KeyCode.Home)
            {
                JumpToFirst();
                return true;
            }

            // End - jump to last
            if (key == KeyCode.End)
            {
                JumpToLast();
                return true;
            }

            // Escape - clear search first, then let parent handle close
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentSelection();
                    return true;
                }
                // Let parent (HistoryState or game) handle Escape when no search is active
                return false;
            }

            // Backspace - handle search
            if (key == KeyCode.Backspace)
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
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    int newIndex = typeahead.GetNextMatch(selectedIndex);
                    if (newIndex >= 0)
                    {
                        selectedIndex = newIndex;
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
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    int newIndex = typeahead.GetPreviousMatch(selectedIndex);
                    if (newIndex >= 0)
                    {
                        selectedIndex = newIndex;
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    SelectPrevious();
                }
                return true;
            }

            // Typeahead characters (letters and numbers, NOT with Alt modifier)
            if (!alt)
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
