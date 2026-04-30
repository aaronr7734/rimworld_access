using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages navigation state for the Page_SelectStoryteller screen.
    /// Handles storyteller selection, difficulty selection, and permadeath mode selection.
    /// Uses modern patterns: MenuHelper for navigation, TypeaheadSearchHelper for search.
    /// </summary>
    public static class StorytellerNavigationState
    {
        private static bool initialized = false;
        private static int storytellerIndex = 0;
        private static int difficultyIndex = -1; // -1 means not yet selected
        private static int permadeathIndex = -1; // -1 = not selected, 0 = Reload Anytime, 1 = Commitment

        private static List<StorytellerDef> storytellers = new List<StorytellerDef>();
        private static List<DifficultyDef> difficulties = new List<DifficultyDef>();

        // Permadeath option labels and descriptions from game translation keys
        private static string GetPermadeathLabel(int index)
        {
            return index == 0
                ? "ReloadAnytimeMode".Translate().ToString()
                : "CommitmentMode".Translate().ToString();
        }

        private static string GetPermadeathDescription(int index)
        {
            string fullText = index == 0
                ? "ReloadAnytimeModeInfo".Translate().ToString()
                : "PermadeathModeInfo".Translate().ToString();
            // Extract just the first sentence/line for brevity
            int newlineIndex = fullText.IndexOf("\n");
            if (newlineIndex > 0)
                return fullText.Substring(0, newlineIndex).TrimEnd();
            return fullText;
        }

        // Typeahead search helpers
        private static TypeaheadSearchHelper storytellerTypeahead = new TypeaheadSearchHelper();
        private static TypeaheadSearchHelper difficultyTypeahead = new TypeaheadSearchHelper();

        // ===== PUBLIC PROPERTIES =====

        public static int StorytellerCount => storytellers.Count;
        public static int DifficultyCount => difficulties.Count;
        public static int PermadeathCount => 2;

        public static bool HasActiveStorytellerSearch => storytellerTypeahead.HasActiveSearch;
        public static bool HasActiveDifficultySearch => difficultyTypeahead.HasActiveSearch;

        public static StorytellerDef SelectedStoryteller
        {
            get
            {
                if (storytellerIndex < 0 || storytellerIndex >= storytellers.Count)
                    return null;
                return storytellers[storytellerIndex];
            }
        }

        public static DifficultyDef SelectedDifficulty
        {
            get
            {
                if (difficultyIndex < 0 || difficultyIndex >= difficulties.Count)
                    return null;
                return difficulties[difficultyIndex];
            }
        }

        public static int PermadeathSelectedIndex => permadeathIndex;

        // ===== INITIALIZATION =====

        public static void Initialize()
        {
            if (!initialized)
            {
                // Get all storytellers ordered by listOrder
                storytellers = DefDatabase<StorytellerDef>.AllDefs
                    .Where(d => d.listVisible)
                    .OrderBy(d => d.listOrder)
                    .ToList();

                // Get all difficulties
                difficulties = DefDatabase<DifficultyDef>.AllDefs.ToList();

                // Start with first storyteller selected (game also does this)
                storytellerIndex = 0;
                // Difficulty and permadeath start unselected - will auto-select on first Tab
                difficultyIndex = -1;
                permadeathIndex = -1;

                // Clear any previous search state
                storytellerTypeahead.ClearSearch();
                difficultyTypeahead.ClearSearch();

                initialized = true;
            }
        }

        public static void Reset()
        {
            initialized = false;
            storytellerIndex = 0;
            difficultyIndex = -1;
            permadeathIndex = -1;
            storytellers.Clear();
            difficulties.Clear();
            storytellerTypeahead.ClearSearch();
            difficultyTypeahead.ClearSearch();
        }

        // ===== FIRST-VISIT AUTO-SELECT =====
        // These methods auto-select the first item ONLY if nothing is selected yet.
        // Subsequent visits preserve the user's selection.

        /// <summary>
        /// Called when user tabs to Storyteller mode.
        /// Storyteller should always have a selection (game auto-selects first too).
        /// </summary>
        public static void EnsureStorytellerSelected()
        {
            if (storytellerIndex < 0 && storytellers.Count > 0)
            {
                storytellerIndex = 0;
            }
        }

        /// <summary>
        /// Called when user tabs to Difficulty mode.
        /// Auto-selects first difficulty only on first visit.
        /// </summary>
        public static void EnsureDifficultySelected()
        {
            if (difficultyIndex < 0 && difficulties.Count > 0)
            {
                difficultyIndex = 0;
            }
        }

        /// <summary>
        /// Called when user tabs to Permadeath mode.
        /// Auto-selects "Reload Anytime Mode" only on first visit.
        /// </summary>
        public static void EnsurePermadeathSelected()
        {
            if (permadeathIndex < 0)
            {
                permadeathIndex = 0;
                UpdateGameInitDataPermadeath();
            }
        }

        // ===== STORYTELLER NAVIGATION =====

        public static void NavigateStorytellerUp()
        {
            if (storytellers.Count == 0) return;
            storytellerTypeahead.ClearSearch();
            storytellerIndex = MenuHelper.SelectPrevious(storytellerIndex, storytellers.Count);
            AnnounceStoryteller();
        }

        public static void NavigateStorytellerDown()
        {
            if (storytellers.Count == 0) return;
            storytellerTypeahead.ClearSearch();
            storytellerIndex = MenuHelper.SelectNext(storytellerIndex, storytellers.Count);
            AnnounceStoryteller();
        }

        public static void NavigateStorytellerHome()
        {
            if (storytellers.Count == 0) return;
            storytellerTypeahead.ClearSearch();
            storytellerIndex = 0;
            AnnounceStoryteller();
        }

        public static void NavigateStorytellerEnd()
        {
            if (storytellers.Count == 0) return;
            storytellerTypeahead.ClearSearch();
            storytellerIndex = storytellers.Count - 1;
            AnnounceStoryteller();
        }

        // ===== DIFFICULTY NAVIGATION =====

        public static void NavigateDifficultyUp()
        {
            if (difficulties.Count == 0) return;
            difficultyTypeahead.ClearSearch();
            // If not yet selected, start at 0 then go to previous
            if (difficultyIndex < 0) difficultyIndex = 0;
            difficultyIndex = MenuHelper.SelectPrevious(difficultyIndex, difficulties.Count);
            AnnounceAndApplyDifficulty();
        }

        public static void NavigateDifficultyDown()
        {
            if (difficulties.Count == 0) return;
            difficultyTypeahead.ClearSearch();
            // If not yet selected, start at -1 so SelectNext gives 0
            difficultyIndex = MenuHelper.SelectNext(difficultyIndex < 0 ? -1 : difficultyIndex, difficulties.Count);
            AnnounceAndApplyDifficulty();
        }

        public static void NavigateDifficultyHome()
        {
            if (difficulties.Count == 0) return;
            difficultyTypeahead.ClearSearch();
            difficultyIndex = 0;
            AnnounceAndApplyDifficulty();
        }

        public static void NavigateDifficultyEnd()
        {
            if (difficulties.Count == 0) return;
            difficultyTypeahead.ClearSearch();
            difficultyIndex = difficulties.Count - 1;
            AnnounceAndApplyDifficulty();
        }

        // ===== PERMADEATH NAVIGATION =====

        public static void NavigatePermadeathUp()
        {
            // If not yet selected, start at 0 then go to previous
            if (permadeathIndex < 0) permadeathIndex = 0;
            permadeathIndex = MenuHelper.SelectPrevious(permadeathIndex, 2);
            UpdateGameInitDataPermadeath();
            AnnouncePermadeath();
        }

        public static void NavigatePermadeathDown()
        {
            // If not yet selected, start at -1 so SelectNext gives 0
            permadeathIndex = MenuHelper.SelectNext(permadeathIndex < 0 ? -1 : permadeathIndex, 2);
            UpdateGameInitDataPermadeath();
            AnnouncePermadeath();
        }

        public static void NavigatePermadeathHome()
        {
            permadeathIndex = 0;
            UpdateGameInitDataPermadeath();
            AnnouncePermadeath();
        }

        public static void NavigatePermadeathEnd()
        {
            permadeathIndex = 1;
            UpdateGameInitDataPermadeath();
            AnnouncePermadeath();
        }

        // ===== TYPEAHEAD SEARCH =====

        public static bool HandleStorytellerTypeahead(char character)
        {
            if (storytellers.Count == 0) return false;

            var labels = storytellers.Select(s => s.label).ToList();
            if (storytellerTypeahead.ProcessCharacterInput(character, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    storytellerIndex = newIndex;
                    AnnounceStorytellerWithSearch();
                }
            }
            else
            {
                storytellerTypeahead.SpeakNoMatches();
            }
            return true;
        }

        public static bool HandleStorytellerTypeaheadBackspace()
        {
            if (!storytellerTypeahead.HasActiveSearch) return false;

            var labels = storytellers.Select(s => s.label).ToList();
            if (storytellerTypeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    storytellerIndex = newIndex;
                    AnnounceStorytellerWithSearch();
                }
            }
            return true;
        }

        public static bool ClearStorytellerTypeaheadSearch()
        {
            if (storytellerTypeahead.ClearSearchAndAnnounce())
            {
                AnnounceStoryteller();
                return true;
            }
            return false;
        }

        public static bool SelectNextStorytellerMatch()
        {
            if (!storytellerTypeahead.HasActiveSearch) return false;
            int next = storytellerTypeahead.GetNextMatch(storytellerIndex);
            if (next >= 0)
            {
                storytellerIndex = next;
                AnnounceStorytellerWithSearch();
            }
            return true;
        }

        public static bool SelectPreviousStorytellerMatch()
        {
            if (!storytellerTypeahead.HasActiveSearch) return false;
            int prev = storytellerTypeahead.GetPreviousMatch(storytellerIndex);
            if (prev >= 0)
            {
                storytellerIndex = prev;
                AnnounceStorytellerWithSearch();
            }
            return true;
        }

        public static bool HandleDifficultyTypeahead(char character)
        {
            if (difficulties.Count == 0) return false;

            var labels = difficulties.Select(d => d.LabelCap.ToString()).ToList();
            if (difficultyTypeahead.ProcessCharacterInput(character, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    difficultyIndex = newIndex;
                    AnnounceDifficultyWithSearch();
                }
            }
            else
            {
                difficultyTypeahead.SpeakNoMatches();
            }
            return true;
        }

        public static bool HandleDifficultyTypeaheadBackspace()
        {
            if (!difficultyTypeahead.HasActiveSearch) return false;

            var labels = difficulties.Select(d => d.LabelCap.ToString()).ToList();
            if (difficultyTypeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    difficultyIndex = newIndex;
                    AnnounceDifficultyWithSearch();
                }
            }
            return true;
        }

        public static bool ClearDifficultyTypeaheadSearch()
        {
            if (difficultyTypeahead.ClearSearchAndAnnounce())
            {
                AnnounceAndApplyDifficulty();
                return true;
            }
            return false;
        }

        public static bool SelectNextDifficultyMatch()
        {
            if (!difficultyTypeahead.HasActiveSearch) return false;
            int next = difficultyTypeahead.GetNextMatch(difficultyIndex);
            if (next >= 0)
            {
                difficultyIndex = next;
                AnnounceDifficultyWithSearch();
            }
            return true;
        }

        public static bool SelectPreviousDifficultyMatch()
        {
            if (!difficultyTypeahead.HasActiveSearch) return false;
            int prev = difficultyTypeahead.GetPreviousMatch(difficultyIndex);
            if (prev >= 0)
            {
                difficultyIndex = prev;
                AnnounceDifficultyWithSearch();
            }
            return true;
        }

        // ===== ANNOUNCEMENTS =====

        public static void AnnounceStoryteller()
        {
            StorytellerDef storyteller = SelectedStoryteller;
            if (storyteller == null) return;

            string position = MenuHelper.FormatPosition(storytellerIndex, storytellers.Count);
            string text = "RimWorldAccess.Storyteller.LabelDescription".Translate(storyteller.label, storyteller.description);
            if (!string.IsNullOrEmpty(position))
            {
                text += "RimWorldAccess.Storyteller.WithPositionSuffix".Translate(position);
            }
            TolkHelper.Speak(text);
        }

        private static void AnnounceStorytellerWithSearch()
        {
            StorytellerDef storyteller = SelectedStoryteller;
            if (storyteller == null) return;

            if (storytellerTypeahead.HasActiveSearch)
            {
                TolkHelper.Speak(storytellerTypeahead.BuildItemAnnouncement(storyteller.label));
            }
            else
            {
                AnnounceStoryteller();
            }
        }

        public static void AnnounceDifficulty()
        {
            DifficultyDef difficulty = SelectedDifficulty;
            if (difficulty == null) return;

            string position = MenuHelper.FormatPosition(difficultyIndex, difficulties.Count);
            string customSuffix = difficulty.isCustom ? (string)"RimWorldAccess.Storyteller.CustomSettingsSuffix".Translate() : "";
            string text = difficulty.LabelCap + customSuffix;

            if (!string.IsNullOrEmpty(difficulty.description))
            {
                text += "RimWorldAccess.Storyteller.WithDescriptionSuffix".Translate(difficulty.description.StripTags());
            }

            if (!string.IsNullOrEmpty(position))
            {
                text += "RimWorldAccess.Storyteller.WithPositionSuffix".Translate(position);
            }

            TolkHelper.Speak(text);
        }

        private static void AnnounceAndApplyDifficulty()
        {
            AnnounceDifficulty();
        }

        private static void AnnounceDifficultyWithSearch()
        {
            DifficultyDef difficulty = SelectedDifficulty;
            if (difficulty == null) return;

            if (difficultyTypeahead.HasActiveSearch)
            {
                TolkHelper.Speak(difficultyTypeahead.BuildItemAnnouncement(difficulty.LabelCap));
            }
            else
            {
                AnnounceDifficulty();
            }
        }

        public static void AnnouncePermadeath()
        {
            if (permadeathIndex < 0 || permadeathIndex >= 2) return;

            string position = MenuHelper.FormatPosition(permadeathIndex, 2);
            string label = GetPermadeathLabel(permadeathIndex);
            string description = GetPermadeathDescription(permadeathIndex);

            string text = "RimWorldAccess.Storyteller.LabelDescription".Translate(label, description);
            if (!string.IsNullOrEmpty(position))
            {
                text += "RimWorldAccess.Storyteller.WithPositionSuffix".Translate(position);
            }
            TolkHelper.Speak(text);
        }

        // ===== GAME DATA SYNC =====

        private static void UpdateGameInitDataPermadeath()
        {
            if (permadeathIndex >= 0)
            {
                Find.GameInitData.permadeathChosen = true;
                Find.GameInitData.permadeath = (permadeathIndex == 1);
            }
        }
    }
}
