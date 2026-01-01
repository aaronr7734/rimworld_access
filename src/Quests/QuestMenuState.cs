using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages the windowless quest menu state for keyboard navigation.
    /// Organizes quests by tab (Available/Active/Historical) and provides navigation.
    /// </summary>
    public static class QuestMenuState
    {
        private static bool isActive = false;
        private static List<Quest> currentQuests = new List<Quest>();
        private static int currentIndex = 0;
        private static QuestsTab currentTab = QuestsTab.Available;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        // Mirror RimWorld's quest tab enum
        private enum QuestsTab
        {
            Available,
            Active,
            Historical
        }

        public static bool IsActive => isActive;
        public static TypeaheadSearchHelper Typeahead => typeahead;
        public static int CurrentIndex => currentIndex;

        /// <summary>
        /// Opens the quest menu and initializes with the available quests tab.
        /// </summary>
        public static void Open()
        {
            isActive = true;
            currentTab = QuestsTab.Available;
            currentIndex = 0;
            typeahead.ClearSearch();
            RefreshQuestList();
            TolkHelper.Speak("Quest menu");
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Closes the quest menu.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentQuests.Clear();
            typeahead.ClearSearch();
            TolkHelper.Speak("Quest menu closed");
        }

        /// <summary>
        /// Navigates to the next quest in the current tab.
        /// </summary>
        public static void SelectNext()
        {
            if (currentQuests.Count == 0)
            {
                TolkHelper.Speak("No quests in this tab");
                return;
            }

            currentIndex = MenuHelper.SelectNext(currentIndex, currentQuests.Count);
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Navigates to the previous quest in the current tab.
        /// </summary>
        public static void SelectPrevious()
        {
            if (currentQuests.Count == 0)
            {
                TolkHelper.Speak("No quests in this tab");
                return;
            }

            currentIndex = MenuHelper.SelectPrevious(currentIndex, currentQuests.Count);

            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Switches to the next tab (Available → Active → Historical → Available).
        /// </summary>
        public static void NextTab()
        {
            currentTab = (QuestsTab)(((int)currentTab + 1) % 3);
            currentIndex = 0;
            typeahead.ClearSearch();
            RefreshQuestList();
            AnnounceTabSwitch();
        }

        /// <summary>
        /// Switches to the previous tab (Historical → Active → Available → Historical).
        /// </summary>
        public static void PreviousTab()
        {
            currentTab = (QuestsTab)(((int)currentTab + 2) % 3);
            currentIndex = 0;
            typeahead.ClearSearch();
            RefreshQuestList();
            AnnounceTabSwitch();
        }

        /// <summary>
        /// Opens the detail view for the currently selected quest.
        /// </summary>
        public static void ViewSelectedQuest()
        {
            if (currentQuests.Count == 0)
            {
                TolkHelper.Speak("No quest selected");
                return;
            }

            Quest selectedQuest = currentQuests[currentIndex];

            // Build detailed information about the quest
            string details = BuildQuestDetails(selectedQuest);
            TolkHelper.Speak(details);
        }

        /// <summary>
        /// Accepts the currently selected quest if it's available.
        /// </summary>
        public static void AcceptQuest()
        {
            if (currentQuests.Count == 0 || currentTab != QuestsTab.Available)
            {
                TolkHelper.Speak("Cannot accept quest", SpeechPriority.High);
                return;
            }

            Quest selectedQuest = currentQuests[currentIndex];

            if (selectedQuest.State != QuestState.NotYetAccepted)
            {
                TolkHelper.Speak("Quest is not available to accept", SpeechPriority.High);
                return;
            }

            AcceptanceReport canAccept = QuestUtility.CanAcceptQuest(selectedQuest);
            if (!canAccept.Accepted)
            {
                TolkHelper.Speak($"Cannot accept: {canAccept.Reason}", SpeechPriority.High);
                return;
            }

            // Accept the quest
            SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
            selectedQuest.Accept(null);
            TolkHelper.Speak($"Accepted quest: {selectedQuest.name.StripTags()}");

            // Refresh the list
            RefreshQuestList();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Dismisses or resumes the currently selected quest.
        /// </summary>
        public static void ToggleDismissQuest()
        {
            if (currentQuests.Count == 0)
            {
                TolkHelper.Speak("No quest selected");
                return;
            }

            Quest selectedQuest = currentQuests[currentIndex];

            if (selectedQuest.Historical)
            {
                selectedQuest.hiddenInUI = true;
                TolkHelper.Speak($"Deleted quest: {selectedQuest.name.StripTags()}");
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            else
            {
                selectedQuest.dismissed = !selectedQuest.dismissed;
                string action = selectedQuest.dismissed ? "Dismissed" : "Resumed";
                TolkHelper.Speak($"{action} quest: {selectedQuest.name.StripTags()}");
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            RefreshQuestList();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Refreshes the quest list based on the current tab.
        /// </summary>
        private static void RefreshQuestList()
        {
            currentQuests.Clear();

            List<Quest> allQuests = Find.QuestManager.questsInDisplayOrder;

            foreach (Quest quest in allQuests)
            {
                if (ShouldShowQuest(quest))
                {
                    currentQuests.Add(quest);
                }
            }

            // Sort based on tab
            switch (currentTab)
            {
                case QuestsTab.Available:
                    currentQuests = currentQuests.OrderBy(q => q.TicksUntilExpiry).ToList();
                    break;
                case QuestsTab.Active:
                    currentQuests = currentQuests.OrderBy(q => q.TicksSinceAccepted).ToList();
                    break;
                case QuestsTab.Historical:
                    currentQuests = currentQuests.OrderBy(q => q.TicksSinceCleanup).ToList();
                    break;
            }

            // Clamp index to valid range
            if (currentIndex >= currentQuests.Count)
                currentIndex = Math.Max(0, currentQuests.Count - 1);
        }

        /// <summary>
        /// Determines if a quest should be shown in the current tab.
        /// </summary>
        private static bool ShouldShowQuest(Quest quest)
        {
            if (quest.hidden || quest.hiddenInUI)
                return false;

            switch (currentTab)
            {
                case QuestsTab.Available:
                    return quest.State == QuestState.NotYetAccepted && !quest.dismissed;

                case QuestsTab.Active:
                    return quest.State == QuestState.Ongoing && !quest.dismissed;

                case QuestsTab.Historical:
                    return quest.Historical || quest.dismissed;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Announces the current tab switch.
        /// </summary>
        private static void AnnounceTabSwitch()
        {
            string tabName = GetTabName();
            string countInfo = currentQuests.Count == 1 ? "1 quest" : $"{currentQuests.Count} quests";
            TolkHelper.Speak($"{tabName} tab - {countInfo}");

            if (currentQuests.Count > 0)
            {
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Announces the currently selected quest.
        /// </summary>
        private static void AnnounceCurrentSelection()
        {
            if (currentQuests.Count == 0)
            {
                TolkHelper.Speak($"{GetTabName()} tab - No quests");
                return;
            }

            Quest quest = currentQuests[currentIndex];
            string announcement = BuildQuestAnnouncement(quest);
            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Builds a short announcement for a quest.
        /// </summary>
        private static string BuildQuestAnnouncement(Quest quest)
        {
            string status = "";

            // Add position info
            string position = MenuHelper.FormatPosition(currentIndex, currentQuests.Count);

            // Add quest name (strip XML tags)
            string name = quest.name.StripTags();

            // Add state info
            if (quest.dismissed && !quest.Historical)
                status = " [Dismissed]";
            else if (quest.Historical)
            {
                switch (quest.State)
                {
                    case QuestState.EndedSuccess:
                        status = " [Completed]";
                        break;
                    case QuestState.EndedFailed:
                        status = " [Failed]";
                        break;
                    default:
                        status = " [Expired]";
                        break;
                }
            }

            // Add time info
            string timeInfo = GetShortTimeInfo(quest);
            if (!string.IsNullOrEmpty(timeInfo))
                timeInfo = $" - {timeInfo}";

            // Add challenge rating
            int rating = Math.Max(quest.challengeRating, 1);
            string ratingInfo = rating == 1 ? " (1 star)" : $" ({rating} stars)";

            return $"{name}{status}{timeInfo}{ratingInfo}. {position}";
        }

        /// <summary>
        /// Builds detailed information about a quest.
        /// </summary>
        private static string BuildQuestDetails(Quest quest)
        {
            List<string> details = new List<string>();

            details.Add($"Quest: {quest.name.StripTags()}");
            details.Add("");

            // Description
            if (!quest.description.RawText.NullOrEmpty())
            {
                details.Add(quest.description.Resolve().StripTags());
                details.Add("");
            }

            // State info
            if (quest.State == QuestState.NotYetAccepted && quest.TicksUntilExpiry > 0)
            {
                details.Add($"Expires in: {quest.TicksUntilExpiry.ToStringTicksToPeriod()}");
            }
            else if (quest.EverAccepted && !quest.Historical)
            {
                details.Add($"Accepted: {quest.TicksSinceAccepted.ToStringTicksToPeriod()} ago");
            }
            else if (quest.Historical)
            {
                string outcome = quest.State == QuestState.EndedSuccess ? "Completed" :
                                quest.State == QuestState.EndedFailed ? "Failed" : "Expired";
                details.Add($"Status: {outcome}");
                details.Add($"Finished: {quest.TicksSinceCleanup.ToStringTicksToPeriod()} ago");
            }

            // Challenge rating
            int rating = Math.Max(quest.challengeRating, 1);
            details.Add($"Challenge: {rating} star{(rating == 1 ? "" : "s")}");

            // Charity quest
            if (quest.charity)
            {
                details.Add("This is a charity quest");
            }

            // Controls
            details.Add("");
            details.Add("Controls:");
            details.Add("Enter: View details");
            details.Add("Alt+A: Accept quest (Available tab only)");
            details.Add("Alt+D: Dismiss/Resume/Delete quest");
            details.Add("Left/Right: Switch tabs");
            details.Add("Type to search, Escape: Close menu");

            return string.Join("\n", details);
        }

        /// <summary>
        /// Gets short time information for a quest.
        /// </summary>
        private static string GetShortTimeInfo(Quest quest)
        {
            if (quest.State == QuestState.NotYetAccepted && quest.TicksUntilExpiry >= 0)
            {
                return $"Expires in {quest.TicksUntilExpiry.ToStringTicksToPeriod(allowSeconds: true, shortForm: true)}";
            }
            else if (quest.Historical)
            {
                return $"{quest.TicksSinceCleanup.ToStringTicksToPeriod(allowSeconds: false, shortForm: true)} ago";
            }
            else if (quest.EverAccepted)
            {
                return $"Accepted {quest.TicksSinceAccepted.ToStringTicksToPeriod(allowSeconds: false, shortForm: true)} ago";
            }

            return "";
        }

        /// <summary>
        /// Gets the name of the current tab.
        /// </summary>
        private static string GetTabName()
        {
            switch (currentTab)
            {
                case QuestsTab.Available:
                    return "Available Quests";
                case QuestsTab.Active:
                    return "Active Quests";
                case QuestsTab.Historical:
                    return "Historical Quests";
                default:
                    return "Quests";
            }
        }

        /// <summary>
        /// Jumps to the first item in the list.
        /// </summary>
        public static void JumpToFirst()
        {
            if (currentQuests.Count == 0)
                return;

            currentIndex = MenuHelper.JumpToFirst();
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Jumps to the last item in the list.
        /// </summary>
        public static void JumpToLast()
        {
            if (currentQuests.Count == 0)
                return;

            currentIndex = MenuHelper.JumpToLast(currentQuests.Count);
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Gets a list of labels for all quests for typeahead search.
        /// </summary>
        public static List<string> GetItemLabels()
        {
            List<string> labels = new List<string>();
            foreach (var quest in currentQuests)
            {
                labels.Add(quest.name.StripTags());
            }
            return labels;
        }

        /// <summary>
        /// Sets the current index directly.
        /// </summary>
        public static void SetCurrentIndex(int index)
        {
            if (index >= 0 && index < currentQuests.Count)
            {
                currentIndex = index;
            }
        }

        /// <summary>
        /// Announces the current selection with search context if active.
        /// </summary>
        public static void AnnounceWithSearch()
        {
            if (currentQuests.Count == 0)
            {
                TolkHelper.Speak($"{GetTabName()} tab - No quests");
                return;
            }

            Quest quest = currentQuests[currentIndex];
            string announcement = BuildQuestAnnouncement(quest);

            // Add search context if active
            if (typeahead.HasActiveSearch)
            {
                announcement += $", match {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} for '{typeahead.SearchBuffer}'";
            }

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Handles backspace for typeahead search.
        /// </summary>
        public static void HandleBackspace()
        {
            if (!typeahead.HasActiveSearch)
                return;

            var labels = GetItemLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                    currentIndex = newIndex;
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Handles character input for typeahead search.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            var labels = GetItemLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    currentIndex = newIndex;
                    AnnounceWithSearch();
                }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
            }
        }
    }
}
