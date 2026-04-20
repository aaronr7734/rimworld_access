using System;
using RimWorld;
using Verse;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Main state management for the History tab accessibility.
    /// Manages tab switching between Statistics and Messages tabs.
    /// (Graph tab is skipped for now - will be implemented later.)
    /// </summary>
    public static class HistoryState
    {
        /// <summary>
        /// Tab indices that match RimWorld's HistoryTab enum:
        /// Graph = 0, Messages = 1, Statistics = 2
        /// </summary>
        public enum Tab
        {
            Statistics = 0,  // Our first tab (RimWorld's tab index 2)
            Messages = 1     // Our second tab (RimWorld's tab index 1)
        }

        // RimWorld's actual tab indices for visual sync
        private const int RimWorldTabStatistics = 2;
        private const int RimWorldTabMessages = 1;

        private static bool isActive = false;
        private static Tab currentTab = Tab.Statistics;

        /// <summary>
        /// Gets whether the History menu is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets the currently selected tab.
        /// </summary>
        public static Tab CurrentTab => currentTab;

        /// <summary>
        /// Opens the History accessibility state.
        /// Called when MainTabWindow_History opens.
        /// </summary>
        public static void Open()
        {
            isActive = true;

            // Default to Statistics tab (simpler to implement and test first)
            currentTab = Tab.Statistics;
            SyncVisualTab();

            // Open the Statistics sub-state
            HistoryStatisticsState.Open();

            TolkHelper.Speak("History. Tab switches between Statistics and Messages tabs.");
        }

        /// <summary>
        /// Closes the History accessibility state.
        /// Called when MainTabWindow_History closes.
        /// </summary>
        public static void Close()
        {
            // Close all sub-states
            HistoryStatisticsState.Close();
            HistoryMessagesState.Close();

            isActive = false;
            currentTab = Tab.Statistics;
        }

        /// <summary>
        /// Switches to the next tab (Tab key).
        /// </summary>
        public static void NextTab()
        {
            CloseCurrentTabState();

            currentTab = (Tab)(((int)currentTab + 1) % 2);

            SyncVisualTab();
            AnnounceCurrentTab();
            OpenCurrentTabState();
        }

        /// <summary>
        /// Switches to the previous tab (Shift+Tab).
        /// </summary>
        public static void PreviousTab()
        {
            CloseCurrentTabState();

            currentTab = (Tab)(((int)currentTab + 2 - 1) % 2);

            SyncVisualTab();
            AnnounceCurrentTab();
            OpenCurrentTabState();
        }

        /// <summary>
        /// Closes the current tab's sub-state.
        /// </summary>
        private static void CloseCurrentTabState()
        {
            switch (currentTab)
            {
                case Tab.Statistics:
                    HistoryStatisticsState.Close();
                    break;
                case Tab.Messages:
                    HistoryMessagesState.Close();
                    break;
            }
        }

        /// <summary>
        /// Opens the current tab's sub-state.
        /// </summary>
        private static void OpenCurrentTabState()
        {
            switch (currentTab)
            {
                case Tab.Statistics:
                    HistoryStatisticsState.Open();
                    break;
                case Tab.Messages:
                    HistoryMessagesState.Open();
                    break;
            }
        }

        /// <summary>
        /// Syncs the visual UI tab to match our internal state.
        /// </summary>
        private static void SyncVisualTab()
        {
            int rimWorldTab = currentTab == Tab.Statistics ? RimWorldTabStatistics : RimWorldTabMessages;
            HistoryHelper.SetCurrentTab(rimWorldTab);
        }

        /// <summary>
        /// Announces the current tab name.
        /// </summary>
        private static void AnnounceCurrentTab()
        {
            string tabName = currentTab == Tab.Statistics ? "Statistics" : "Messages";
            TolkHelper.Speak($"{tabName} tab");
        }

        /// <summary>
        /// Gets the tab name for announcements.
        /// </summary>
        public static string GetTabName()
        {
            return currentTab == Tab.Statistics ? "Statistics" : "Messages";
        }

        /// <summary>
        /// Handles keyboard input for tab-level operations.
        /// Returns true if input was handled.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive)
                return false;

            // Tab/Shift+Tab switches tabs
            if (key == KeyCode.Tab && !ctrl && !alt)
            {
                if (shift)
                    PreviousTab();
                else
                    NextTab();
                return true;
            }

            // Block ALL unhandled keys to prevent game's native handlers from processing them
            // This makes the History tab modal - it captures all keyboard input while active
            return true;
        }

        /// <summary>
        /// Checks if any sub-state has an active typeahead search.
        /// Used by OnCancelKeyPressed patch to determine if Escape should clear search first.
        /// </summary>
        public static bool HasActiveTypeahead
        {
            get
            {
                if (currentTab == Tab.Statistics && HistoryStatisticsState.HasActiveSearch)
                    return true;
                if (currentTab == Tab.Messages && HistoryMessagesState.HasActiveSearch)
                    return true;
                return false;
            }
        }
    }
}
