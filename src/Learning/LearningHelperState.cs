using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages the windowless learning helper menu state for browsing tutorial concepts.
    /// Two modes: Active Lessons (unlearned concepts) and All Lessons (full concept database).
    /// Two-level navigation: Up/Down to navigate list, Enter to open detail view with help text,
    /// Tab to toggle between active/all modes.
    ///
    /// Knowledge increases progressively as the user arrows through content lines in detail view.
    /// Each new line visited fills a proportional amount of the remaining knowledge gap.
    /// Knowledge is committed to the database when leaving detail view (Escape) or via
    /// the "Mark as Learned" button.
    /// </summary>
    public static class LearningHelperState
    {
        private static bool isActive = false;
        private static bool showAllMode = false;
        private static List<ConceptDef> concepts = null;
        private static int currentIndex = 0;
        private static TwoLevelMenuHelper detailHelper = null;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        // Cached reflection access to LearningReadout.activeConcepts
        private static FieldInfo activeConceptsField = null;

        // Progressive reading state — knowledge is tracked locally while in detail view
        // and only committed to the game's database when leaving detail view.
        // This prevents the game from auto-removing the concept while the user is still reading.
        private static float pendingKnowledge = 0f;
        private static float detailStartKnowledge = 0f;
        private static HashSet<int> visitedLines = new HashSet<int>();
        private static int totalContentLines = 0;

        public static bool IsActive => isActive;
        public static bool IsInDetailView => detailHelper?.IsInDetailView ?? false;
        public static bool IsInButtonsSection => detailHelper?.IsInButtonsSection ?? false;
        public static TypeaheadSearchHelper Typeahead => typeahead;
        public static int CurrentIndex => currentIndex;
        public static bool ShowAllMode => showAllMode;

        /// <summary>
        /// Opens the learning helper menu. Starts in active lessons mode.
        /// </summary>
        public static void Open()
        {
            if (!TutorSystem.AdaptiveTrainingEnabled || TutorSystem.TutorialMode)
            {
                TolkHelper.Speak("LearningHelper".Translate() + " " + "disabled");
                return;
            }

            showAllMode = false;
            concepts = CollectActiveConcepts();

            isActive = true;
            currentIndex = 0;
            typeahead.ClearSearch();
            ResetReadingProgress();

            InitializeDetailHelper();

            if (concepts.Count == 0)
            {
                TolkHelper.Speak("LearningHelper".Translate() + ". " +
                    "No active lessons. Press Tab to browse all lessons.");
            }
            else
            {
                TolkHelper.Speak("LearningHelper".Translate());
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Closes the learning helper menu.
        /// </summary>
        public static void Close()
        {
            // Commit any pending knowledge before closing
            CommitPendingKnowledge();

            isActive = false;
            concepts = null;
            currentIndex = 0;
            showAllMode = false;
            typeahead.ClearSearch();
            ResetReadingProgress();
            detailHelper?.Reset();
        }

        /// <summary>
        /// Closes the menu and announces closure.
        /// </summary>
        public static void CloseMenu()
        {
            Close();
            TolkHelper.Speak("LearningHelper".Translate() + " closed");
        }

        /// <summary>
        /// Toggles between active lessons and all lessons mode.
        /// </summary>
        public static void ToggleMode()
        {
            if (detailHelper != null && detailHelper.IsInDetailView)
            {
                CommitPendingKnowledge();
                ResetReadingProgress();
                detailHelper.GoBackToList();
            }

            showAllMode = !showAllMode;
            typeahead.ClearSearch();

            if (showAllMode)
            {
                concepts = CollectAllConcepts();
                currentIndex = 0;
                InitializeDetailHelper();
                TolkHelper.Speak($"{"LearningHelper".Translate()}. All lessons, {concepts.Count} total");
            }
            else
            {
                concepts = CollectActiveConcepts();
                currentIndex = 0;
                InitializeDetailHelper();
                TolkHelper.Speak($"{"LearningHelper".Translate()}. Active lessons");
            }

            if (concepts.Count > 0)
            {
                AnnounceCurrentSelection();
            }
            else if (showAllMode)
            {
                TolkHelper.Speak("No lessons available");
            }
            else
            {
                TolkHelper.Speak("No active lessons. Press Tab to browse all lessons.");
            }
        }

        /// <summary>
        /// Moves selection to the next item or detail position.
        /// </summary>
        public static void SelectNext()
        {
            if (concepts == null || concepts.Count == 0)
                return;

            if (detailHelper.IsInDetailView)
            {
                detailHelper.SelectNextDetailPosition();
                TrackLineVisit();
            }
            else
            {
                currentIndex = MenuHelper.SelectNext(currentIndex, concepts.Count);
                detailHelper.ResetDetailPosition();
                detailHelper.RefreshButtons();
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Moves selection to the previous item or detail position.
        /// </summary>
        public static void SelectPrevious()
        {
            if (concepts == null || concepts.Count == 0)
                return;

            if (detailHelper.IsInDetailView)
            {
                detailHelper.SelectPreviousDetailPosition();
                TrackLineVisit();
            }
            else
            {
                currentIndex = MenuHelper.SelectPrevious(currentIndex, concepts.Count);
                detailHelper.ResetDetailPosition();
                detailHelper.RefreshButtons();
                AnnounceCurrentSelection();
            }
        }

        public static void SelectNextButton()
        {
            detailHelper?.SelectNextButton();
        }

        public static void SelectPreviousButton()
        {
            detailHelper?.SelectPreviousButton();
        }

        /// <summary>
        /// Opens detail view for the current concept. Initializes progressive reading state.
        /// </summary>
        public static void EnterDetailView()
        {
            if (concepts == null || concepts.Count == 0)
                return;

            if (currentIndex < 0 || currentIndex >= concepts.Count)
                return;

            ConceptDef conc = concepts[currentIndex];

            // Initialize progressive reading state
            detailStartKnowledge = PlayerKnowledgeDatabase.GetKnowledge(conc);
            pendingKnowledge = detailStartKnowledge;
            visitedLines.Clear();
            totalContentLines = SplitHelpText(conc).Length;

            typeahead.ClearSearch();
            detailHelper.RefreshButtons();
            detailHelper.EnterDetailView();
            detailHelper.AnnounceDetailPosition();
        }

        /// <summary>
        /// Activates the currently selected button.
        /// </summary>
        public static void ActivateCurrentButton()
        {
            if (!detailHelper.ActivateCurrentButton())
                return;

            ButtonInfo button = detailHelper.GetCurrentButton();
            if (button == null) return;

            try
            {
                button.Action?.Invoke();

                if (!isActive)
                    return;

                // Mark as Learned sets to 1.0 — update our pending state to match
                pendingKnowledge = 1f;

                // Refresh button to show "Already Learned"
                detailHelper.RefreshButtons();
                TolkHelper.Speak("MarkLearned".Translate() + ". 100%");
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Failed to activate learning helper button: {ex.Message}");
                TolkHelper.Speak("Failed to activate button");
            }
        }

        /// <summary>
        /// Handles Escape key: clear search → detail→list → close.
        /// When leaving detail view, commits pending knowledge to the database.
        /// </summary>
        public static void HandleEscape()
        {
            if (detailHelper != null && detailHelper.IsInDetailView)
            {
                // Commit reading progress before leaving detail view
                CommitPendingKnowledge();
                ResetReadingProgress();

                detailHelper.GoBackToList();
                typeahead.ClearSearch();

                // Refresh list in case completion removed the concept from active list
                RefreshConcepts();
                if (concepts.Count == 0)
                {
                    if (showAllMode)
                    {
                        TwoLevelMenuHelper.SpeakReturnToList("No lessons available");
                    }
                    else
                    {
                        TwoLevelMenuHelper.SpeakReturnToList("No active lessons remaining. Press Tab to browse all lessons.");
                    }
                    return;
                }

                if (currentIndex >= concepts.Count)
                    currentIndex = concepts.Count - 1;

                detailHelper.ResetDetailPosition();
                detailHelper.RefreshButtons();
                TwoLevelMenuHelper.SpeakReturnToList();
                AnnounceCurrentSelection();
            }
            else
            {
                CloseMenu();
            }
        }

        /// <summary>
        /// Handles Backspace for typeahead search deletion.
        /// </summary>
        public static void HandleBackspace()
        {
            if (!showAllMode || detailHelper.IsInDetailView)
                return;

            if (typeahead.ProcessBackspace(GetItemLabels(), out int newIndex))
            {
                if (newIndex >= 0)
                {
                    currentIndex = newIndex;
                }
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Handles typeahead character input.
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
                typeahead.SpeakNoMatches();
            }
        }

        public static void SetCurrentIndex(int index)
        {
            if (concepts == null || concepts.Count == 0) return;
            currentIndex = Mathf.Clamp(index, 0, concepts.Count - 1);
            detailHelper?.ResetDetailPosition();
            detailHelper?.RefreshButtons();
        }

        public static void JumpToFirst()
        {
            if (concepts == null || concepts.Count == 0)
                return;

            bool wasInDetailView = detailHelper.IsInDetailView;
            if (wasInDetailView)
            {
                CommitPendingKnowledge();
                ResetReadingProgress();
            }
            detailHelper.GoBackToList();
            currentIndex = MenuHelper.JumpToFirst();
            detailHelper.ResetDetailPosition();
            typeahead.ClearSearch();
            detailHelper.RefreshButtons();

            if (wasInDetailView)
            {
                RefreshConcepts();
                if (currentIndex >= concepts.Count && concepts.Count > 0)
                    currentIndex = concepts.Count - 1;
                TwoLevelMenuHelper.SpeakReturnToList();
            }
            AnnounceCurrentSelection();
        }

        public static void JumpToLast()
        {
            if (concepts == null || concepts.Count == 0)
                return;

            bool wasInDetailView = detailHelper.IsInDetailView;
            if (wasInDetailView)
            {
                CommitPendingKnowledge();
                ResetReadingProgress();
            }
            detailHelper.GoBackToList();
            currentIndex = MenuHelper.JumpToLast(concepts.Count);
            detailHelper.ResetDetailPosition();
            typeahead.ClearSearch();
            detailHelper.RefreshButtons();

            if (wasInDetailView)
            {
                RefreshConcepts();
                if (currentIndex >= concepts.Count && concepts.Count > 0)
                    currentIndex = concepts.Count - 1;
                TwoLevelMenuHelper.SpeakReturnToList();
            }
            AnnounceCurrentSelection();
        }

        public static void JumpToDetailStart()
        {
            detailHelper?.JumpToDetailStart();
            TrackLineVisit();
        }

        public static void JumpToDetailEnd()
        {
            detailHelper?.JumpToDetailEnd();
            TrackLineVisit();
        }

        /// <summary>
        /// Announces current selection with search context.
        /// </summary>
        public static void AnnounceWithSearch()
        {
            if (concepts == null || concepts.Count == 0 || currentIndex < 0 || currentIndex >= concepts.Count)
                return;

            ConceptDef conc = concepts[currentIndex];
            string label = conc.LabelCap;
            string knowledge = GetKnowledgeText(conc);
            string position = MenuHelper.FormatPosition(currentIndex, concepts.Count);
            string searchInfo = typeahead.HasActiveSearch ? $" Search: '{typeahead.SearchBuffer}'" : "";

            TolkHelper.Speak($"{label}, {knowledge}. {position}{searchInfo}");
        }

        // ===== Progressive Reading Knowledge =====

        /// <summary>
        /// Tracks which content line the user is currently on and updates pending knowledge.
        /// Called after each detail view navigation.
        /// </summary>
        private static void TrackLineVisit()
        {
            if (detailHelper == null || !detailHelper.IsInDetailView)
                return;

            if (concepts == null || currentIndex < 0 || currentIndex >= concepts.Count)
                return;

            int pos = detailHelper.DetailPosition;

            // Content lines are at positions 1 through totalContentLines (0 = header, after = buttons)
            if (pos >= 1 && pos <= totalContentLines && totalContentLines > 0)
            {
                int lineIndex = pos - 1;
                if (visitedLines.Add(lineIndex)) // Returns true only if newly added
                {
                    // Spread the remaining knowledge gap evenly across all content lines
                    float knowledgePerLine = (1f - detailStartKnowledge) / totalContentLines;
                    pendingKnowledge = Mathf.Clamp01(detailStartKnowledge + visitedLines.Count * knowledgePerLine);
                }
            }
        }

        /// <summary>
        /// Commits the pending knowledge to the game's database.
        /// Partial progress is persisted (e.g., 60% stays as 60%).
        /// The game only triggers "newly learned" removal at >= 99.9%,
        /// so partial commits won't cause the lesson to disappear from the active list.
        /// </summary>
        private static void CommitPendingKnowledge()
        {
            if (concepts == null || currentIndex < 0 || currentIndex >= concepts.Count)
                return;

            ConceptDef conc = concepts[currentIndex];

            // Only update if we have new knowledge to commit
            if (pendingKnowledge > PlayerKnowledgeDatabase.GetKnowledge(conc))
            {
                PlayerKnowledgeDatabase.SetKnowledge(conc, pendingKnowledge);
            }
        }

        /// <summary>
        /// Resets progressive reading state.
        /// </summary>
        private static void ResetReadingProgress()
        {
            pendingKnowledge = 0f;
            detailStartKnowledge = 0f;
            visitedLines.Clear();
            totalContentLines = 0;
        }

        /// <summary>
        /// Gets the knowledge percentage text for a concept.
        /// Uses pending knowledge if we're in detail view for the current concept.
        /// </summary>
        private static string GetKnowledgeText(ConceptDef conc)
        {
            float knowledge;
            if (detailHelper != null && detailHelper.IsInDetailView &&
                concepts != null && currentIndex >= 0 && currentIndex < concepts.Count &&
                concepts[currentIndex] == conc)
            {
                // Use pending (live) knowledge while reading
                knowledge = pendingKnowledge;
            }
            else
            {
                knowledge = PlayerKnowledgeDatabase.GetKnowledge(conc);
            }

            int percent = Mathf.Clamp(Mathf.RoundToInt(knowledge * 100f), 0, 100);
            return $"{percent}%";
        }

        // ===== Private Methods =====

        private static void InitializeDetailHelper()
        {
            detailHelper = new TwoLevelMenuHelper(
                getContentLineCount: () =>
                {
                    if (concepts == null || currentIndex < 0 || currentIndex >= concepts.Count)
                        return 0;
                    return SplitHelpText(concepts[currentIndex]).Length;
                },
                populateButtons: PopulateButtons,
                getHeaderAnnouncement: () =>
                {
                    if (concepts == null || currentIndex < 0 || currentIndex >= concepts.Count)
                        return "";
                    ConceptDef conc = concepts[currentIndex];
                    return $"{conc.LabelCap}, {GetKnowledgeText(conc)}";
                },
                getContentLineAnnouncement: (idx) =>
                {
                    if (concepts == null || currentIndex < 0 || currentIndex >= concepts.Count)
                        return "";
                    string[] lines = SplitHelpText(concepts[currentIndex]);
                    return idx >= 0 && idx < lines.Length ? lines[idx] : "";
                },
                endOfItemMessage: "End of lesson",
                startOfItemMessage: "Start of lesson",
                openFirstMessage: "Press Enter to open lesson first"
            );
            detailHelper.RefreshButtons();
        }

        private static void PopulateButtons(List<ButtonInfo> buttons)
        {
            if (concepts == null || concepts.Count == 0) return;
            if (currentIndex < 0 || currentIndex >= concepts.Count) return;

            ConceptDef conc = concepts[currentIndex];

            // Check both database and pending state for completion
            bool isComplete = PlayerKnowledgeDatabase.IsComplete(conc) || pendingKnowledge >= 0.999f;

            if (isComplete)
            {
                buttons.Add(new ButtonInfo
                {
                    Label = "AlreadyLearned".Translate(),
                    IsDisabled = true,
                    DisabledReason = "AlreadyLearned".Translate()
                });
            }
            else
            {
                buttons.Add(new ButtonInfo
                {
                    Label = "MarkLearned".Translate(),
                    Action = () =>
                    {
                        pendingKnowledge = 1f;
                        PlayerKnowledgeDatabase.SetKnowledge(conc, 1f);
                    }
                });
            }
        }

        private static void AnnounceCurrentSelection()
        {
            if (concepts == null || concepts.Count == 0)
                return;

            if (currentIndex < 0 || currentIndex >= concepts.Count)
                return;

            if (detailHelper != null && detailHelper.IsInDetailView)
            {
                detailHelper.AnnounceDetailPosition();
                return;
            }

            ConceptDef conc = concepts[currentIndex];
            string label = conc.LabelCap;
            string knowledge = GetKnowledgeText(conc);
            string position = MenuHelper.FormatPosition(currentIndex, concepts.Count);

            TolkHelper.Speak($"{label}, {knowledge}. {position}");
        }

        private static string[] SplitHelpText(ConceptDef conc)
        {
            string text = conc.HelpTextAdjusted;
            if (string.IsNullOrEmpty(text))
                return new string[] { "No help text available" };

            // Split by newlines, filter empty lines
            string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            // Trim whitespace from each line
            for (int i = 0; i < lines.Length; i++)
                lines[i] = lines[i].Trim();

            // Filter out empty lines after trimming
            lines = lines.Where(l => l.Length > 0).ToArray();

            return lines.Length > 0 ? lines : new string[] { text.Trim() };
        }

        private static List<string> GetItemLabels()
        {
            List<string> labels = new List<string>();
            if (concepts != null)
            {
                foreach (var conc in concepts)
                    labels.Add(conc.LabelCap);
            }
            return labels;
        }

        /// <summary>
        /// Collects currently active (unlearned) concepts from the game's LearningReadout.
        /// </summary>
        private static List<ConceptDef> CollectActiveConcepts()
        {
            try
            {
                if (Find.Tutor?.learningReadout == null)
                    return new List<ConceptDef>();

                if (activeConceptsField == null)
                {
                    activeConceptsField = AccessTools.Field(typeof(LearningReadout), "activeConcepts");
                }

                if (activeConceptsField == null)
                {
                    Log.Warning("[RimWorld Access] Could not find activeConcepts field on LearningReadout");
                    return new List<ConceptDef>();
                }

                var activeConcepts = activeConceptsField.GetValue(Find.Tutor.learningReadout) as List<ConceptDef>;
                if (activeConcepts == null)
                    return new List<ConceptDef>();

                // Return a copy sorted by priority (lower = higher priority)
                return activeConcepts.OrderBy(c => c.priority).ToList();
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Failed to collect active concepts: {ex.Message}");
                return new List<ConceptDef>();
            }
        }

        /// <summary>
        /// Collects all non-triggered concepts from the game's concept database.
        /// Matches the filter used by LearningReadout in showAllMode.
        /// </summary>
        private static List<ConceptDef> CollectAllConcepts()
        {
            try
            {
                return DefDatabase<ConceptDef>.AllDefsListForReading
                    .Where(c => !c.TriggeredDirect)
                    .OrderBy(c => c.priority)
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Failed to collect all concepts: {ex.Message}");
                return new List<ConceptDef>();
            }
        }

        /// <summary>
        /// Refreshes the concepts list based on current mode.
        /// </summary>
        private static void RefreshConcepts()
        {
            if (showAllMode)
            {
                concepts = CollectAllConcepts();
            }
            else
            {
                concepts = CollectActiveConcepts();
            }
        }
    }
}
