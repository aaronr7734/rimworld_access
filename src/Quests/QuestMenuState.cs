using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages the windowless quest menu state for keyboard navigation.
    /// Supports three modes: QuestList, QuestDetail, and RewardPreferences.
    /// </summary>
    public static class QuestMenuState
    {
        private static bool isActive = false;
        private static List<Quest> currentQuests = new List<Quest>();
        private static int currentIndex = 0;
        private static QuestsTab currentTab = QuestsTab.Available;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        // Mode tracking
        private static QuestMenuMode currentMode = QuestMenuMode.QuestList;

        // Detail view
        private static TwoLevelMenuHelper detailHelper = null;
        private static List<DetailLine> cachedDetailLines = new List<DetailLine>();

        // Reward preferences
        private static List<RewardPrefItem> rewardPrefItems = new List<RewardPrefItem>();
        private static int rewardPrefIndex = 0;

        // Reward choice float menu
        private static bool hasActiveRewardMenu = false;
        private static bool isInItemInspectionMenu = false;
        private static Quest rewardMenuQuest = null;
        private static List<QuestPart_Choice.Choice> rewardChoices = null;
        private static List<List<(Thing thing, Faction faction)>> choiceInspectables = null;
        private static List<(Thing thing, Faction faction)> currentInspectionItems = null;
        private static int savedChoiceIndex = -1;

        private enum QuestsTab
        {
            Available,
            Active,
            Historical
        }

        private enum QuestMenuMode
        {
            QuestList,
            QuestDetail,
            RewardPreferences
        }

        // === Public Properties ===

        public static bool IsActive => isActive;
        public static TypeaheadSearchHelper Typeahead => typeahead;
        public static int CurrentIndex => currentIndex;
        public static bool IsInDetailView => detailHelper != null && detailHelper.IsInDetailView;
        public static bool IsInButtonsSection => detailHelper != null && detailHelper.IsInButtonsSection;
        public static bool IsInRewardPrefsMode => currentMode == QuestMenuMode.RewardPreferences;
        public static bool HasActiveRewardMenu => hasActiveRewardMenu;
        public static bool IsInItemInspectionMenu => isInItemInspectionMenu;

        // =====================================================================
        // Open / Close
        // =====================================================================

        /// <summary>
        /// Opens the quest menu and initializes with the available quests tab.
        /// </summary>
        public static void Open()
        {
            isActive = true;
            currentTab = QuestsTab.Available;
            currentIndex = 0;
            currentMode = QuestMenuMode.QuestList;
            typeahead.ClearSearch();
            cachedDetailLines.Clear();
            rewardPrefItems.Clear();
            RefreshQuestList();

            InitializeDetailHelper();

            string openMessage = "Quest menu. Alt+A to accept, Alt+D to dismiss. Enter to arrow through quest details.";
            if (currentQuests.Count > 0)
            {
                openMessage += " " + BuildQuestAnnouncement(currentQuests[0]);
            }
            else
            {
                openMessage += " " + GetTabName() + " tab - No quests";
            }
            TolkHelper.Speak(openMessage);
        }

        /// <summary>
        /// Opens the quest menu and navigates to a specific quest.
        /// Called when activating "View Quest" button from a letter.
        /// </summary>
        public static void OpenAndSelectQuest(Quest quest)
        {
            if (quest == null)
            {
                TolkHelper.Speak("Quest no longer available");
                return;
            }

            QuestsTab targetTab = GetTabForQuest(quest);

            isActive = true;
            currentTab = targetTab;
            currentIndex = 0;
            currentMode = QuestMenuMode.QuestList;
            typeahead.ClearSearch();
            cachedDetailLines.Clear();
            rewardPrefItems.Clear();
            RefreshQuestList();

            InitializeDetailHelper();

            int index = currentQuests.FindIndex(q => q == quest);
            if (index >= 0)
            {
                currentIndex = index;
                TolkHelper.Speak("Quest menu");
                AnnounceCurrentSelection();
            }
            else
            {
                foreach (QuestsTab tab in Enum.GetValues(typeof(QuestsTab)))
                {
                    currentTab = tab;
                    RefreshQuestList();
                    index = currentQuests.FindIndex(q => q == quest);
                    if (index >= 0)
                    {
                        currentIndex = index;
                        TolkHelper.Speak("Quest menu");
                        AnnounceCurrentSelection();
                        return;
                    }
                }

                TolkHelper.Speak("Quest no longer available");
                Close();
            }
        }

        /// <summary>
        /// Closes the quest menu.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentQuests.Clear();
            typeahead.ClearSearch();
            currentMode = QuestMenuMode.QuestList;
            detailHelper?.Reset();
            cachedDetailLines.Clear();
            rewardPrefItems.Clear();
            CleanupRewardMenu();
            TolkHelper.Speak("Quest menu closed");
        }

        // =====================================================================
        // Quest List Navigation
        // =====================================================================

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

        public static void NextTab()
        {
            currentTab = (QuestsTab)(((int)currentTab + 1) % 3);
            currentIndex = 0;
            typeahead.ClearSearch();
            RefreshQuestList();
            AnnounceTabSwitch();
        }

        public static void PreviousTab()
        {
            currentTab = (QuestsTab)(((int)currentTab + 2) % 3);
            currentIndex = 0;
            typeahead.ClearSearch();
            RefreshQuestList();
            AnnounceTabSwitch();
        }

        public static void JumpToFirst()
        {
            if (currentQuests.Count == 0)
                return;

            currentIndex = MenuHelper.JumpToFirst();
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
        }

        public static void JumpToLast()
        {
            if (currentQuests.Count == 0)
                return;

            currentIndex = MenuHelper.JumpToLast(currentQuests.Count);
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
        }

        public static void SetCurrentIndex(int index)
        {
            if (index >= 0 && index < currentQuests.Count)
            {
                currentIndex = index;
            }
        }

        // =====================================================================
        // Detail View
        // =====================================================================

        /// <summary>
        /// Enters the detail view for the currently selected quest.
        /// Replaces the old ViewSelectedQuest() text dump.
        /// </summary>
        public static void EnterDetailView()
        {
            if (currentQuests.Count == 0)
            {
                TolkHelper.Speak("No quest selected");
                return;
            }

            Quest quest = currentQuests[currentIndex];
            currentMode = QuestMenuMode.QuestDetail;

            cachedDetailLines = BuildDetailContentLines(quest);

            typeahead.ClearSearch();
            detailHelper.RefreshButtons();
            detailHelper.EnterDetailView();
            detailHelper.AnnounceDetailPosition();
        }

        public static void SelectNextDetail()
        {
            if (detailHelper == null || !detailHelper.IsInDetailView) return;
            detailHelper.SelectNextDetailPosition();
        }

        public static void SelectPreviousDetail()
        {
            if (detailHelper == null || !detailHelper.IsInDetailView) return;
            detailHelper.SelectPreviousDetailPosition();
        }

        public static void SelectNextButton()
        {
            detailHelper?.SelectNextButton();
        }

        public static void SelectPreviousButton()
        {
            detailHelper?.SelectPreviousButton();
        }

        public static void ActivateCurrentButton()
        {
            if (detailHelper == null) return;
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
                        Log.Warning($"[RimWorld Access] Failed to activate quest button: {ex.Message}");
                        TolkHelper.Speak("Failed to activate button");
                    }
                }
            }
        }

        /// <summary>
        /// Goes back to list view from detail view, or closes the menu if in list view.
        /// </summary>
        public static void GoBackToList()
        {
            if (detailHelper != null && detailHelper.IsInDetailView)
            {
                detailHelper.GoBackToList();
                currentMode = QuestMenuMode.QuestList;
                typeahead.ClearSearch();
                TolkHelper.Speak("Back to list");
                AnnounceCurrentSelection();
            }
            else
            {
                Close();
            }
        }

        public static void JumpToDetailStart()
        {
            detailHelper?.JumpToDetailStart();
        }

        public static void JumpToDetailEnd()
        {
            detailHelper?.JumpToDetailEnd();
        }

        /// <summary>
        /// Opens an info card for the item/faction on the current detail line.
        /// </summary>
        public static void OpenInfoCard()
        {
            if (detailHelper == null || !detailHelper.IsInDetailView)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No info card available");
                return;
            }

            int position = detailHelper.DetailPosition;
            // Position 0 is header, 1-N are content lines
            int lineIndex = position - 1;

            if (lineIndex < 0 || lineIndex >= cachedDetailLines.Count)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No info card available");
                return;
            }

            DetailLine line = cachedDetailLines[lineIndex];

            if (line.InfoCardThing != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(line.InfoCardThing));
                return;
            }

            if (line.InfoCardFaction != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(line.InfoCardFaction));
                return;
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("No info card available");
        }

        // =====================================================================
        // Quest Actions (Accept / Dismiss)
        // =====================================================================

        /// <summary>
        /// Accepts the currently selected quest if it's available.
        /// Handles multi-choice and RequiresAccepter scenarios.
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

            // Multi-choice quests open a reward choice float menu
            if (QuestRewardHelper.HasMultipleChoices(selectedQuest))
            {
                OpenRewardChoiceMenu(selectedQuest);
                return;
            }

            // RequiresAccepter needs pawn selection
            if (selectedQuest.RequiresAccepter)
            {
                AcceptQuestWithPawnSelection(selectedQuest, null);
                return;
            }

            // Simple accept
            SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
            selectedQuest.Accept(null);
            TolkHelper.Speak($"Accepted quest: {selectedQuest.name.StripTags()}");

            if (IsInDetailView)
            {
                detailHelper.GoBackToList();
                currentMode = QuestMenuMode.QuestList;
            }
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

            if (IsInDetailView)
            {
                detailHelper.GoBackToList();
                currentMode = QuestMenuMode.QuestList;
            }
            RefreshQuestList();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Accepts a quest with a specific reward choice selected.
        /// </summary>
        private static void AcceptQuestWithChoice(Quest quest, QuestPart_Choice choicePart,
            QuestPart_Choice.Choice choice)
        {
            choicePart.Choose(choice);

            if (quest.RequiresAccepter)
            {
                AcceptQuestWithPawnSelection(quest, choice);
                return;
            }

            SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
            quest.Accept(null);
            string rewardDesc = QuestRewardHelper.BuildRewardDescription(choice.rewards);
            TolkHelper.Speak($"Accepted quest with: {rewardDesc}");

            detailHelper?.GoBackToList();
            currentMode = QuestMenuMode.QuestList;
            RefreshQuestList();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Opens a pawn selection float menu for RequiresAccepter quests.
        /// CLOSES QuestMenuState first to prevent priority routing conflicts.
        /// </summary>
        private static void AcceptQuestWithPawnSelection(Quest quest, QuestPart_Choice.Choice chosenReward)
        {
            var eligiblePawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoSuspended
                .Where(p => QuestUtility.CanPawnAcceptQuest(p, quest))
                .ToList();

            if (eligiblePawns.Count == 0)
            {
                TolkHelper.Speak("No eligible colonists to accept this quest", SpeechPriority.High);
                return;
            }

            var options = new List<FloatMenuOption>();
            foreach (Pawn pawn in eligiblePawns)
            {
                Pawn localPawn = pawn;
                string label = localPawn.LabelShort;
                if (localPawn.royalty != null && localPawn.royalty.AllTitlesInEffectForReading.Any())
                {
                    label += $" ({localPawn.royalty.MostSeniorTitle.def.GetLabelFor(localPawn)})";
                }

                options.Add(new FloatMenuOption(label, () =>
                {
                    SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
                    quest.Accept(localPawn);
                    TolkHelper.Speak($"Accepted quest with {localPawn.LabelShort}");
                }));
            }

            // Close quest menu BEFORE opening float menu to prevent priority routing conflict
            Close();
            TolkHelper.Speak("Select a colonist to accept this quest");
            WindowlessFloatMenuState.Open(options, false);
        }

        // =====================================================================
        // Reward Preferences
        // =====================================================================

        /// <summary>
        /// Toggles between QuestList and RewardPreferences mode.
        /// </summary>
        public static void ToggleRewardPreferencesMode()
        {
            if (currentMode == QuestMenuMode.RewardPreferences)
            {
                currentMode = QuestMenuMode.QuestList;
                TolkHelper.Speak("Quest list");
                AnnounceCurrentSelection();
            }
            else
            {
                currentMode = QuestMenuMode.RewardPreferences;
                rewardPrefItems = QuestRewardHelper.GetRewardPreferenceItems();
                rewardPrefIndex = 0;

                if (rewardPrefItems.Count == 0)
                {
                    TolkHelper.Speak("No reward preferences available");
                    currentMode = QuestMenuMode.QuestList;
                    return;
                }

                TolkHelper.Speak("Reward preferences");
                AnnounceRewardPref();
            }
        }

        public static void RewardPrefsNext()
        {
            if (rewardPrefItems.Count == 0) return;
            rewardPrefIndex = MenuHelper.SelectNext(rewardPrefIndex, rewardPrefItems.Count);
            AnnounceRewardPref();
        }

        public static void RewardPrefsPrevious()
        {
            if (rewardPrefItems.Count == 0) return;
            rewardPrefIndex = MenuHelper.SelectPrevious(rewardPrefIndex, rewardPrefItems.Count);
            AnnounceRewardPref();
        }

        public static void RewardPrefsToggle()
        {
            if (rewardPrefItems.Count == 0) return;
            var item = rewardPrefItems[rewardPrefIndex];
            QuestRewardHelper.ToggleRewardPreference(item);

            // Refresh to get updated labels
            rewardPrefItems = QuestRewardHelper.GetRewardPreferenceItems();
            if (rewardPrefIndex >= rewardPrefItems.Count)
                rewardPrefIndex = Math.Max(0, rewardPrefItems.Count - 1);
            AnnounceRewardPref();
        }

        public static void RewardPrefsJumpToFirst()
        {
            if (rewardPrefItems.Count == 0) return;
            rewardPrefIndex = 0;
            AnnounceRewardPref();
        }

        public static void RewardPrefsJumpToLast()
        {
            if (rewardPrefItems.Count == 0) return;
            rewardPrefIndex = rewardPrefItems.Count - 1;
            AnnounceRewardPref();
        }

        private static void AnnounceRewardPref()
        {
            if (rewardPrefItems.Count == 0) return;
            var item = rewardPrefItems[rewardPrefIndex];
            string position = MenuHelper.FormatPosition(rewardPrefIndex, rewardPrefItems.Count);
            TolkHelper.Speak($"{item.Label}. {position}");
        }

        // =====================================================================
        // Reward Choice Float Menu
        // =====================================================================

        /// <summary>
        /// Opens a float menu listing reward choices for a multi-choice quest.
        /// QuestMenuState stays active while the float menu is open.
        /// </summary>
        private static void OpenRewardChoiceMenu(Quest quest)
        {
            QuestPart_Choice choicePart = QuestRewardHelper.GetChoicePart(quest);
            if (choicePart == null || choicePart.choices.Count < 2)
                return;

            rewardMenuQuest = quest;
            rewardChoices = choicePart.choices;
            hasActiveRewardMenu = true;
            isInItemInspectionMenu = false;

            // Build inspectable items for each choice
            choiceInspectables = new List<List<(Thing thing, Faction faction)>>();
            var options = new List<FloatMenuOption>();

            for (int i = 0; i < rewardChoices.Count; i++)
            {
                int choiceIdx = i;
                var choice = rewardChoices[choiceIdx];
                string rewardDesc = QuestRewardHelper.BuildRewardDescription(choice.rewards);
                string label = rewardDesc;

                // Build inspectable items for this choice
                var inspectables = new List<(Thing thing, Faction faction)>();
                foreach (Reward reward in choice.rewards)
                {
                    if (reward is Reward_Items rewardItems && rewardItems.ItemsListForReading != null)
                    {
                        foreach (Thing item in rewardItems.ItemsListForReading)
                        {
                            if (item != null)
                                inspectables.Add((item, null));
                        }
                    }
                    else if (reward is Reward_Goodwill rg && rg.faction != null)
                    {
                        inspectables.Add((null, rg.faction));
                    }
                    else if (reward is Reward_RoyalFavor rf && rf.faction != null)
                    {
                        inspectables.Add((null, rf.faction));
                    }
                    else if (reward is Reward_Pawn rp && rp.pawn != null && !rp.detailsHidden)
                    {
                        inspectables.Add((rp.pawn, null));
                    }
                }
                choiceInspectables.Add(inspectables);

                options.Add(new FloatMenuOption(label, () =>
                {
                    // Accept with this choice
                    AcceptQuestWithChoice(rewardMenuQuest, choicePart, choice);
                    CleanupRewardMenu();
                }));
            }

            TolkHelper.Speak("Choose a reward");
            WindowlessFloatMenuState.Open(options, false);
        }

        /// <summary>
        /// Cleans up reward choice float menu state.
        /// Called when the float menu closes by any means.
        /// </summary>
        public static void CleanupRewardMenu()
        {
            hasActiveRewardMenu = false;
            isInItemInspectionMenu = false;
            rewardMenuQuest = null;
            rewardChoices = null;
            choiceInspectables = null;
            currentInspectionItems = null;
            savedChoiceIndex = -1;
        }

        /// <summary>
        /// Opens an item inspection sub-menu for the currently selected reward choice.
        /// Called when Alt+I is pressed in the reward choice float menu.
        /// </summary>
        public static void OpenItemInspectionForCurrentChoice()
        {
            if (choiceInspectables == null) return;

            int choiceIdx = WindowlessFloatMenuState.SelectedIndex;
            if (choiceIdx < 0 || choiceIdx >= choiceInspectables.Count)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No items to inspect");
                return;
            }

            var inspectables = choiceInspectables[choiceIdx];
            if (inspectables.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No items to inspect");
                return;
            }

            // Consolidate items by label (e.g., 5 stacks of plasteel → one "375x Plasteel" entry)
            var consolidated = new List<(string label, Thing thing, Faction faction)>();
            var grouped = new Dictionary<string, (int totalCount, Thing representative)>();
            var factionEntries = new List<(string label, Faction faction)>();

            foreach (var inspectable in inspectables)
            {
                if (inspectable.thing != null)
                {
                    string key = inspectable.thing.LabelNoCount;
                    if (grouped.ContainsKey(key))
                    {
                        var existing = grouped[key];
                        grouped[key] = (existing.totalCount + inspectable.thing.stackCount, existing.representative);
                    }
                    else
                    {
                        grouped[key] = (inspectable.thing.stackCount, inspectable.thing);
                    }
                }
                else if (inspectable.faction != null)
                {
                    factionEntries.Add((inspectable.faction.Name, inspectable.faction));
                }
            }

            foreach (var kvp in grouped)
            {
                int count = kvp.Value.totalCount;
                Thing rep = kvp.Value.representative;
                string itemName = rep.LabelNoCount.CapitalizeFirst();
                string itemLabel = count > 1 ? $"{count}x {itemName}" : itemName;
                consolidated.Add((itemLabel, rep, null));
            }
            foreach (var entry in factionEntries)
            {
                consolidated.Add((entry.label, null, entry.faction));
            }

            // Re-check after consolidation: single item opens info card directly
            if (consolidated.Count == 1)
            {
                var item = consolidated[0];
                if (item.thing != null)
                    Find.WindowStack.Add(new Dialog_InfoCard(item.thing));
                else if (item.faction != null)
                    Find.WindowStack.Add(new Dialog_InfoCard(item.faction));
                return;
            }

            if (consolidated.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No items to inspect");
                return;
            }

            // Set up item inspection state
            savedChoiceIndex = choiceIdx;
            isInItemInspectionMenu = true;

            // Build consolidated inspection items list for InspectCurrentItem
            currentInspectionItems = new List<(Thing thing, Faction faction)>();
            var options = new List<FloatMenuOption>();
            foreach (var entry in consolidated)
            {
                currentInspectionItems.Add((entry.thing, entry.faction));
                options.Add(new FloatMenuOption(entry.label, () => { }));
            }

            // Close current float menu and open item inspection menu
            WindowlessFloatMenuState.Close();
            TolkHelper.Speak("Choose item to inspect");
            WindowlessFloatMenuState.Open(options, false);
        }

        /// <summary>
        /// Opens an info card for the currently selected item in the inspection sub-menu.
        /// Called when Enter is pressed in item inspection mode (intercepted before float menu handler).
        /// </summary>
        public static void InspectCurrentItem()
        {
            if (currentInspectionItems == null) return;

            int idx = WindowlessFloatMenuState.SelectedIndex;
            if (idx < 0 || idx >= currentInspectionItems.Count)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No info card available");
                return;
            }

            var item = currentInspectionItems[idx];
            if (item.thing != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(item.thing));
            }
            else if (item.faction != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(item.faction));
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No info card available");
            }
        }

        /// <summary>
        /// Returns from item inspection sub-menu to the reward choice float menu.
        /// Called when Escape is pressed in item inspection mode.
        /// </summary>
        public static void ReturnToRewardChoiceMenu()
        {
            isInItemInspectionMenu = false;
            currentInspectionItems = null;

            // Close item inspection float menu
            WindowlessFloatMenuState.Close();

            // Rebuild and re-open choice menu at saved position
            if (rewardMenuQuest == null || rewardChoices == null)
            {
                CleanupRewardMenu();
                TolkHelper.Speak("Back to quest list");
                AnnounceCurrentSelection();
                return;
            }

            QuestPart_Choice choicePart = QuestRewardHelper.GetChoicePart(rewardMenuQuest);
            if (choicePart == null)
            {
                CleanupRewardMenu();
                TolkHelper.Speak("Back to quest list");
                AnnounceCurrentSelection();
                return;
            }

            var options = new List<FloatMenuOption>();
            for (int i = 0; i < rewardChoices.Count; i++)
            {
                int choiceIdx = i;
                var choice = rewardChoices[choiceIdx];
                string rewardDesc = QuestRewardHelper.BuildRewardDescription(choice.rewards);
                string label = rewardDesc;

                options.Add(new FloatMenuOption(label, () =>
                {
                    AcceptQuestWithChoice(rewardMenuQuest, choicePart, choice);
                    CleanupRewardMenu();
                }));
            }

            int restoreIndex = savedChoiceIndex >= 0 && savedChoiceIndex < options.Count
                ? savedChoiceIndex : 0;
            WindowlessFloatMenuState.Open(options, false, restoreIndex);
        }

        // =====================================================================
        // Typeahead Search
        // =====================================================================

        public static List<string> GetItemLabels()
        {
            List<string> labels = new List<string>();
            foreach (var quest in currentQuests)
            {
                labels.Add(quest.name.StripTags());
            }
            return labels;
        }

        public static void AnnounceWithSearch()
        {
            if (currentQuests.Count == 0)
            {
                TolkHelper.Speak($"{GetTabName()} tab - No quests");
                return;
            }

            Quest quest = currentQuests[currentIndex];
            string announcement = BuildQuestAnnouncement(quest);

            if (typeahead.HasActiveSearch)
            {
                announcement += $", match {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} for '{typeahead.SearchBuffer}'";
            }

            TolkHelper.Speak(announcement);
        }

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

        // =====================================================================
        // Private Helpers
        // =====================================================================

        private static void InitializeDetailHelper()
        {
            detailHelper = new TwoLevelMenuHelper(
                getContentLineCount: () => cachedDetailLines.Count,
                populateButtons: PopulateQuestButtons,
                getHeaderAnnouncement: GetDetailHeaderAnnouncement,
                getContentLineAnnouncement: (idx) =>
                    idx >= 0 && idx < cachedDetailLines.Count ? cachedDetailLines[idx].Text : "",
                endOfItemMessage: "End of quest details",
                startOfItemMessage: "Start of quest details"
            );
        }

        private static string GetDetailHeaderAnnouncement()
        {
            if (currentQuests.Count == 0 || currentIndex < 0 || currentIndex >= currentQuests.Count)
                return "";

            Quest quest = currentQuests[currentIndex];
            string name = quest.name.StripTags();
            string status;

            if (quest.State == QuestState.NotYetAccepted) status = "Available";
            else if (quest.State == QuestState.Ongoing && !quest.dismissed) status = "Active";
            else if (quest.State == QuestState.Ongoing && quest.dismissed) status = "Dismissed";
            else if (quest.State == QuestState.EndedSuccess) status = "Completed";
            else if (quest.State == QuestState.EndedFailed) status = "Failed";
            else status = "Expired";

            return $"Quest: {name} ({status})";
        }

        /// <summary>
        /// Builds the navigable content lines for the detail view.
        /// </summary>
        private static List<DetailLine> BuildDetailContentLines(Quest quest)
        {
            var lines = new List<DetailLine>();

            // Difficulty
            int rating = Math.Max(quest.challengeRating, 1);
            string ratingLine = $"{"Difficulty".Translate()}: {rating} star{(rating == 1 ? "" : "s")}";
            if (quest.charity)
                ratingLine += " (Charity quest)";
            lines.Add(new DetailLine(ratingLine));

            // Time info
            if (quest.State == QuestState.NotYetAccepted && quest.TicksUntilExpiry > 0)
            {
                lines.Add(new DetailLine("QuestExpiresIn".Translate(quest.TicksUntilExpiry.ToStringTicksToPeriod()).ToString()));
            }
            else if (quest.EverAccepted && !quest.Historical)
            {
                lines.Add(new DetailLine($"Accepted: {quest.TicksSinceAccepted.ToStringTicksToPeriod()} ago"));
            }
            else if (quest.Historical)
            {
                string outcome = quest.State == QuestState.EndedSuccess ? "Completed" :
                                 quest.State == QuestState.EndedFailed ? "Failed" : "Expired";
                lines.Add(new DetailLine($"Status: {outcome}"));
                lines.Add(new DetailLine($"Finished: {quest.TicksSinceCleanup.ToStringTicksToPeriod()} ago"));
            }

            // Active-quest deadlines from QuestPartActivable parts (matches vanilla MainTabWindow_Quests.DoRightAlignedInfo).
            // Each part's ExpiryInfoPart is already localized and formatted (e.g. "Ends in 3 days").
            if (quest.State == QuestState.Ongoing)
            {
                foreach (QuestPart part in quest.PartsListForReading)
                {
                    if (part is QuestPartActivable activable &&
                        activable.State == QuestPartState.Enabled &&
                        !activable.ExpiryInfoPart.NullOrEmpty())
                    {
                        lines.Add(new DetailLine(activable.ExpiryInfoPart));
                    }
                }
            }

            // Description (split into individual lines)
            if (!quest.description.RawText.NullOrEmpty())
            {
                string desc = quest.description.Resolve().StripTags();
                string[] descLines = desc.Split('\n');
                foreach (string line in descLines)
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        lines.Add(new DetailLine(trimmed));
                }
            }

            // Rewards (with info card targets)
            var rewardLines = QuestRewardHelper.BuildRewardDetailLines(quest);
            lines.AddRange(rewardLines);

            return lines;
        }

        /// <summary>
        /// Populates the action buttons for the detail view based on quest state.
        /// </summary>
        private static void PopulateQuestButtons(List<ButtonInfo> buttons)
        {
            if (currentQuests.Count == 0 || currentIndex < 0 || currentIndex >= currentQuests.Count)
                return;

            Quest quest = currentQuests[currentIndex];

            if (quest.State == QuestState.NotYetAccepted)
            {
                AcceptanceReport canAccept = QuestUtility.CanAcceptQuest(quest);
                QuestPart_Choice choicePart = QuestRewardHelper.GetChoicePart(quest);
                bool hasMultiChoice = choicePart != null && choicePart.choices.Count >= 2;

                if (hasMultiChoice)
                {
                    // One accept button per reward choice
                    for (int i = 0; i < choicePart.choices.Count; i++)
                    {
                        int choiceIdx = i;
                        string rewardDesc = QuestRewardHelper.BuildRewardDescription(
                            choicePart.choices[choiceIdx].rewards);
                        buttons.Add(new ButtonInfo
                        {
                            Label = $"Accept Choice {choiceIdx + 1}: {rewardDesc}",
                            Action = () => AcceptQuestWithChoice(quest, choicePart,
                                choicePart.choices[choiceIdx]),
                            IsDisabled = !canAccept.Accepted,
                            DisabledReason = canAccept.Accepted ? null : canAccept.Reason
                        });
                    }
                }
                else
                {
                    // Single accept button with reward description
                    string acceptLabel = "AcceptButton".Translate();
                    if (choicePart != null && choicePart.choices.Count == 1)
                    {
                        string rewardDesc = QuestRewardHelper.BuildRewardDescription(choicePart.choices[0].rewards);
                        if (!string.IsNullOrEmpty(rewardDesc))
                            acceptLabel = $"{acceptLabel}: {rewardDesc}";
                    }
                    buttons.Add(new ButtonInfo
                    {
                        Label = acceptLabel,
                        Action = () =>
                        {
                            AcceptanceReport report = QuestUtility.CanAcceptQuest(quest);
                            if (!report.Accepted)
                            {
                                TolkHelper.Speak($"Cannot accept: {report.Reason}", SpeechPriority.High);
                                return;
                            }

                            if (quest.RequiresAccepter)
                            {
                                AcceptQuestWithPawnSelection(quest, null);
                                return;
                            }

                            SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
                            quest.Accept(null);
                            TolkHelper.Speak($"Accepted quest: {quest.name.StripTags()}");
                            detailHelper?.GoBackToList();
                            currentMode = QuestMenuMode.QuestList;
                            RefreshQuestList();
                            AnnounceCurrentSelection();
                        },
                        IsDisabled = !canAccept.Accepted,
                        DisabledReason = canAccept.Accepted ? null : canAccept.Reason
                    });
                }

                // Dismiss button for available quests
                buttons.Add(new ButtonInfo
                {
                    Label = "CommandShuttleDismiss".Translate(),
                    Action = () =>
                    {
                        quest.dismissed = true;
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        TolkHelper.Speak($"Dismissed quest: {quest.name.StripTags()}");
                        detailHelper?.GoBackToList();
                        currentMode = QuestMenuMode.QuestList;
                        RefreshQuestList();
                        AnnounceCurrentSelection();
                    }
                });
            }
            else if (quest.State == QuestState.Ongoing)
            {
                buttons.Add(new ButtonInfo
                {
                    Label = quest.dismissed ? "Resume" : ((string)"CommandShuttleDismiss".Translate()),
                    Action = () =>
                    {
                        quest.dismissed = !quest.dismissed;
                        string action = quest.dismissed ? "Dismissed" : "Resumed";
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        TolkHelper.Speak($"{action} quest: {quest.name.StripTags()}");
                        detailHelper?.GoBackToList();
                        currentMode = QuestMenuMode.QuestList;
                        RefreshQuestList();
                        AnnounceCurrentSelection();
                    }
                });
            }
            else if (quest.Historical)
            {
                buttons.Add(new ButtonInfo
                {
                    Label = "Delete".Translate(),
                    Action = () =>
                    {
                        quest.hiddenInUI = true;
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                        TolkHelper.Speak($"Deleted quest: {quest.name.StripTags()}");
                        detailHelper?.GoBackToList();
                        currentMode = QuestMenuMode.QuestList;
                        RefreshQuestList();
                        AnnounceCurrentSelection();
                    }
                });
            }

            // Jump to location buttons
            var lookTargets = quest.QuestLookTargets.Where(t => CameraJumper.CanJump(t)).ToList();
            foreach (var target in lookTargets)
            {
                GlobalTargetInfo localTarget = target;
                string targetLabel = localTarget.Label;
                string buttonLabel = string.IsNullOrEmpty(targetLabel)
                    ? "Jump to location"
                    : (string)"JumpToTargetCustom".Translate(targetLabel);

                buttons.Add(new ButtonInfo
                {
                    Label = buttonLabel,
                    Action = () =>
                    {
                        CameraJumper.TryJumpAndSelect(localTarget);
                        Close();
                    }
                });
            }
        }

        private static QuestsTab GetTabForQuest(Quest quest)
        {
            if (quest.Historical || quest.dismissed)
                return QuestsTab.Historical;

            if (quest.State == QuestState.NotYetAccepted)
                return QuestsTab.Available;

            if (quest.State == QuestState.Ongoing)
                return QuestsTab.Active;

            return QuestsTab.Historical;
        }

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

            if (currentIndex >= currentQuests.Count)
                currentIndex = Math.Max(0, currentQuests.Count - 1);
        }

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

        private static string BuildQuestAnnouncement(Quest quest)
        {
            var parts = new List<string>();
            string name = quest.name.StripTags();

            // Name with status
            if (quest.dismissed && !quest.Historical)
                parts.Add($"{name}, Dismissed");
            else if (quest.Historical)
            {
                switch (quest.State)
                {
                    case QuestState.EndedSuccess:
                        parts.Add($"{name}, Completed");
                        break;
                    case QuestState.EndedFailed:
                        parts.Add($"{name}, Failed");
                        break;
                    default:
                        parts.Add($"{name}, Expired");
                        break;
                }
            }
            else
            {
                parts.Add(name);
            }

            // Difficulty
            int rating = Math.Max(quest.challengeRating, 1);
            string ratingText = rating == 1 ? "1 star" : $"{rating} stars";
            if (quest.charity)
                ratingText += ", charity quest";
            parts.Add(ratingText);

            // Time info
            string timeInfo = GetShortTimeInfo(quest);
            if (!string.IsNullOrEmpty(timeInfo))
                parts.Add(timeInfo);

            // Description
            if (!quest.description.RawText.NullOrEmpty())
            {
                string desc = quest.description.Resolve().StripTags();
                // Split on newlines, trim, filter empties, strip trailing periods to avoid ".." when joining
                var descLines = desc.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                var cleanLines = new List<string>();
                foreach (string line in descLines)
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        if (trimmed.EndsWith("."))
                            trimmed = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();
                        if (!string.IsNullOrEmpty(trimmed))
                            cleanLines.Add(trimmed);
                    }
                }
                if (cleanLines.Count > 0)
                    parts.Add(string.Join(". ", cleanLines));
            }

            // Rewards
            string rewardSummary = QuestRewardHelper.BuildCompactRewardSummary(quest);
            parts.Add($"Rewards: {rewardSummary}");

            // Position
            string position = MenuHelper.FormatPosition(currentIndex, currentQuests.Count);

            return string.Join(". ", parts) + ". " + position;
        }

        private static string GetShortTimeInfo(Quest quest)
        {
            if (quest.State == QuestState.NotYetAccepted && quest.TicksUntilExpiry >= 0)
            {
                return "QuestExpiresIn".Translate(
                    quest.TicksUntilExpiry.ToStringTicksToPeriod(allowSeconds: true, shortForm: true)).ToString();
            }
            else if (quest.Historical)
            {
                return $"{quest.TicksSinceCleanup.ToStringTicksToPeriod(allowSeconds: false, shortForm: true)} ago";
            }
            else if (quest.EverAccepted)
            {
                // Active quest with a bad-outcome deadline takes priority over "accepted ago"
                // (matches vanilla MainTabWindow_Quests.GetShortTimeInfo).
                foreach (QuestPart part in quest.PartsListForReading)
                {
                    if (part is QuestPart_Delay delayPart &&
                        delayPart.State == QuestPartState.Enabled &&
                        delayPart.isBad &&
                        !delayPart.expiryInfoPart.NullOrEmpty())
                    {
                        return "QuestExpiresIn".Translate(
                            delayPart.TicksLeft.ToStringTicksToPeriod(allowSeconds: false, shortForm: true, canUseDecimals: false)).ToString();
                    }
                }
                return $"Accepted {quest.TicksSinceAccepted.ToStringTicksToPeriod(allowSeconds: false, shortForm: true)} ago";
            }

            return "";
        }

        private static string GetTabName()
        {
            switch (currentTab)
            {
                case QuestsTab.Available:
                    return "AvailableQuests".Translate();
                case QuestsTab.Active:
                    return "ActiveQuests".Translate();
                case QuestsTab.Historical:
                    return "HistoricalQuests".Translate();
                default:
                    return "Quests";
            }
        }
    }
}
