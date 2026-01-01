using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Navigation level for the storyteller selection.
    /// </summary>
    public enum StorytellerSelectionLevel
    {
        StorytellerList,  // Choosing a storyteller
        DifficultyList    // Choosing difficulty
    }

    /// <summary>
    /// Manages keyboard navigation for the storyteller selection page.
    /// </summary>
    public static class StorytellerSelectionState
    {
        private static bool isActive = false;
        private static StorytellerSelectionLevel currentLevel = StorytellerSelectionLevel.StorytellerList;
        private static int selectedStorytellerIndex = 0;
        private static int selectedDifficultyIndex = 0;
        private static List<StorytellerDef> storytellers = new List<StorytellerDef>();
        private static List<DifficultyDef> difficulties = new List<DifficultyDef>();

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the storyteller selection navigation.
        /// </summary>
        public static void Open()
        {
            isActive = true;
            currentLevel = StorytellerSelectionLevel.StorytellerList;

            // Get all visible storytellers
            storytellers = DefDatabase<StorytellerDef>.AllDefs
                .Where(st => st.listVisible)
                .OrderBy(st => st.listOrder)
                .ToList();

            // Get all difficulties
            difficulties = DefDatabase<DifficultyDef>.AllDefs.ToList();

            // Find current selections
            Storyteller current = Current.Game.storyteller;
            selectedStorytellerIndex = storytellers.IndexOf(current.def);
            if (selectedStorytellerIndex < 0) selectedStorytellerIndex = 0;

            selectedDifficultyIndex = difficulties.IndexOf(current.difficultyDef);
            if (selectedDifficultyIndex < 0) selectedDifficultyIndex = 0;

            AnnounceCurrentState();
        }

        /// <summary>
        /// Closes the storyteller selection.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentLevel = StorytellerSelectionLevel.StorytellerList;
        }

        /// <summary>
        /// Moves selection to next item.
        /// </summary>
        public static void SelectNext()
        {
            if (currentLevel == StorytellerSelectionLevel.StorytellerList)
            {
                selectedStorytellerIndex = MenuHelper.SelectNext(selectedStorytellerIndex, storytellers.Count);
                ApplyStorytellerSelection();
            }
            else // DifficultyList
            {
                selectedDifficultyIndex = MenuHelper.SelectNext(selectedDifficultyIndex, difficulties.Count);
                ApplyDifficultySelection();
            }

            AnnounceCurrentState();
        }

        /// <summary>
        /// Moves selection to previous item.
        /// </summary>
        public static void SelectPrevious()
        {
            if (currentLevel == StorytellerSelectionLevel.StorytellerList)
            {
                selectedStorytellerIndex = MenuHelper.SelectPrevious(selectedStorytellerIndex, storytellers.Count);
                ApplyStorytellerSelection();
            }
            else // DifficultyList
            {
                selectedDifficultyIndex = MenuHelper.SelectPrevious(selectedDifficultyIndex, difficulties.Count);
                ApplyDifficultySelection();
            }

            AnnounceCurrentState();
        }

        /// <summary>
        /// Switches between storyteller and difficulty selection.
        /// </summary>
        public static void SwitchLevel()
        {
            if (currentLevel == StorytellerSelectionLevel.StorytellerList)
            {
                currentLevel = StorytellerSelectionLevel.DifficultyList;
            }
            else
            {
                currentLevel = StorytellerSelectionLevel.StorytellerList;
            }

            AnnounceCurrentState();
        }

        /// <summary>
        /// Confirms selection and closes the page.
        /// </summary>
        public static void Confirm()
        {
            TolkHelper.Speak("Storyteller and difficulty confirmed. Closing selection.");
            Close();
            // The page will close itself via the close button
            Find.WindowStack.TryRemove(typeof(Page_SelectStorytellerInGame));
        }

        private static void ApplyStorytellerSelection()
        {
            if (selectedStorytellerIndex >= 0 && selectedStorytellerIndex < storytellers.Count)
            {
                Storyteller storyteller = Current.Game.storyteller;
                StorytellerDef oldDef = storyteller.def;
                storyteller.def = storytellers[selectedStorytellerIndex];

                if (storyteller.def != oldDef)
                {
                    storyteller.Notify_DefChanged();
                    TutorSystem.Notify_Event("ChooseStoryteller");
                }
            }
        }

        private static void ApplyDifficultySelection()
        {
            if (selectedDifficultyIndex >= 0 && selectedDifficultyIndex < difficulties.Count)
            {
                Storyteller storyteller = Current.Game.storyteller;
                DifficultyDef selectedDiff = difficulties[selectedDifficultyIndex];
                storyteller.difficultyDef = selectedDiff;

                // Copy difficulty values from the def
                if (!selectedDiff.isCustom)
                {
                    storyteller.difficulty.CopyFrom(selectedDiff);
                }
            }
        }

        private static void AnnounceCurrentState()
        {
            if (currentLevel == StorytellerSelectionLevel.StorytellerList)
            {
                if (selectedStorytellerIndex >= 0 && selectedStorytellerIndex < storytellers.Count)
                {
                    var st = storytellers[selectedStorytellerIndex];
                    TolkHelper.Speak($"Storyteller: {st.label}. {st.description}. Press Tab to switch to difficulty selection.");
                }
            }
            else // DifficultyList
            {
                if (selectedDifficultyIndex >= 0 && selectedDifficultyIndex < difficulties.Count)
                {
                    var diff = difficulties[selectedDifficultyIndex];
                    TolkHelper.Speak($"Difficulty: {diff.LabelCap}. {diff.description}. Press Tab to switch to storyteller selection. Press Enter to confirm.");
                }
            }
        }
    }
}
