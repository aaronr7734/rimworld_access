using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public static class StorytellerNavigationState
    {
        private static bool initialized = false;
        private static int storytellerIndex = 0;
        private static int difficultyIndex = -1; // -1 means not selected
        private static List<StorytellerDef> storytellers = new List<StorytellerDef>();
        private static List<DifficultyDef> difficulties = new List<DifficultyDef>();
        private static bool permadeathSelected = false;
        private static bool permadeathValue = false;

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

                storytellerIndex = 0;
                difficultyIndex = -1;
                permadeathSelected = Find.GameInitData.permadeathChosen;
                permadeathValue = Find.GameInitData.permadeath;
                initialized = true;
            }
        }

        public static void Reset()
        {
            initialized = false;
            storytellerIndex = 0;
            difficultyIndex = -1;
            storytellers.Clear();
            difficulties.Clear();
            permadeathSelected = false;
            permadeathValue = false;
        }

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

        public static void NavigateStorytellerUp()
        {
            if (storytellers.Count == 0) return;

            storytellerIndex--;
            if (storytellerIndex < 0)
                storytellerIndex = storytellers.Count - 1;

            CopyStorytellerToClipboard();
        }

        public static void NavigateStorytellerDown()
        {
            if (storytellers.Count == 0) return;

            storytellerIndex++;
            if (storytellerIndex >= storytellers.Count)
                storytellerIndex = 0;

            CopyStorytellerToClipboard();
        }

        public static void NavigateDifficultyUp()
        {
            if (difficulties.Count == 0) return;

            difficultyIndex--;
            if (difficultyIndex < 0)
                difficultyIndex = difficulties.Count - 1;

            CopyDifficultyToClipboard();
        }

        public static void NavigateDifficultyDown()
        {
            if (difficulties.Count == 0) return;

            difficultyIndex++;
            if (difficultyIndex >= difficulties.Count)
                difficultyIndex = 0;

            CopyDifficultyToClipboard();
        }

        public static void TogglePermadeath()
        {
            permadeathSelected = true;
            permadeathValue = !permadeathValue;

            // Update GameInitData
            Find.GameInitData.permadeathChosen = true;
            Find.GameInitData.permadeath = permadeathValue;

            CopyPermadeathToClipboard();
        }

        private static void CopyStorytellerToClipboard()
        {
            StorytellerDef storyteller = SelectedStoryteller;
            if (storyteller == null) return;

            string text = $"Storyteller: {storyteller.label} - {storyteller.description}";
            TolkHelper.Speak(text);
        }

        private static void CopyDifficultyToClipboard()
        {
            DifficultyDef difficulty = SelectedDifficulty;
            if (difficulty == null) return;

            StorytellerDef storyteller = SelectedStoryteller;
            string storytellerName = storyteller != null ? storyteller.label : "Unknown";

            string customSuffix = difficulty.isCustom ? " (Custom settings)" : "";
            string text = $"{storytellerName} - Difficulty: {difficulty.LabelCap}{customSuffix}";

            if (!string.IsNullOrEmpty(difficulty.description))
            {
                text += $" - {difficulty.description}";
            }

            TolkHelper.Speak(text);
        }

        private static void CopyPermadeathToClipboard()
        {
            string mode = permadeathValue ? "Commitment Mode (Permadeath)" : "Reload Anytime Mode";
            string description = permadeathValue
                ? "Cannot reload saves. One chance only!"
                : "Can reload saves anytime";

            string text = $"{mode} - {description}";
            TolkHelper.Speak(text);
        }

        public static int StorytellerCount => storytellers.Count;
        public static int DifficultyCount => difficulties.Count;
    }
}
