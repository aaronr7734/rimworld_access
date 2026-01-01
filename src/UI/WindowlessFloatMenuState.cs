using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages a "virtual" float menu without actually displaying a window.
    /// Stores FloatMenuOptions and handles keyboard navigation, then executes the selected option.
    /// </summary>
    public static class WindowlessFloatMenuState
    {
        private static List<FloatMenuOption> currentOptions = null;
        private static int selectedIndex = 0;
        private static bool isActive = false;
        private static bool givesColonistOrders = false;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
        private static List<object> savedSelection = null;

        /// <summary>
        /// Gets whether the windowless menu is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the windowless menu with the given options.
        /// </summary>
        public static void Open(List<FloatMenuOption> options, bool colonistOrders)
        {
            currentOptions = options;
            selectedIndex = 0;
            isActive = true;
            givesColonistOrders = colonistOrders;
            typeahead.ClearSearch();

            // Save current selection - some FloatMenu actions expect specific objects to be selected
            savedSelection = Find.Selector?.SelectedObjects?.ToList();

            // Announce the first option
            if (selectedIndex >= 0 && selectedIndex < options.Count)
            {
                string optionText = options[selectedIndex].Label;
                if (options[selectedIndex].Disabled)
                {
                    optionText += " (unavailable)";
                }
                TolkHelper.Speak($"{optionText}. {MenuHelper.FormatPosition(selectedIndex, options.Count)}");
            }
        }

        /// <summary>
        /// Closes the windowless menu.
        /// </summary>
        public static void Close()
        {
            currentOptions = null;
            selectedIndex = 0;
            isActive = false;
            typeahead.ClearSearch();
        }

        /// <summary>
        /// Moves selection to the next option.
        /// </summary>
        public static void SelectNext()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, currentOptions.Count);

            // Announce the new selection
            if (selectedIndex >= 0 && selectedIndex < currentOptions.Count)
            {
                string optionText = currentOptions[selectedIndex].Label;
                if (currentOptions[selectedIndex].Disabled)
                {
                    optionText += " (unavailable)";
                }
                TolkHelper.Speak($"{optionText}. {MenuHelper.FormatPosition(selectedIndex, currentOptions.Count)}");
            }
        }

        /// <summary>
        /// Moves selection to the previous option.
        /// </summary>
        public static void SelectPrevious()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, currentOptions.Count);

            // Announce the new selection
            if (selectedIndex >= 0 && selectedIndex < currentOptions.Count)
            {
                string optionText = currentOptions[selectedIndex].Label;
                if (currentOptions[selectedIndex].Disabled)
                {
                    optionText += " (unavailable)";
                }
                TolkHelper.Speak($"{optionText}. {MenuHelper.FormatPosition(selectedIndex, currentOptions.Count)}");
            }
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

            FloatMenuOption selectedOption = currentOptions[selectedIndex];

            if (selectedOption.Disabled)
            {
                TolkHelper.Speak(selectedOption.Label + " - unavailable");
                return;
            }

            // Restore saved selection before executing - some actions check Find.Selector.SelectedObjects
            if (savedSelection != null && Find.Selector != null)
            {
                Find.Selector.ClearSelection();
                foreach (var obj in savedSelection)
                {
                    if (obj is ISelectable selectable)
                    {
                        Find.Selector.Select(selectable, playSound: false, forceDesignatorDeselect: false);
                    }
                }
            }

            // Close the menu BEFORE executing the action
            // This allows the action to open a new menu if needed
            Close();

            // Call the Chosen method to execute the option's action
            selectedOption.Chosen(givesColonistOrders, null);

            // Announce selection
            TolkHelper.Speak($"{selectedOption.Label} selected");
        }

        /// <summary>
        /// Jumps to the first option in the menu.
        /// </summary>
        public static void JumpToFirst()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            selectedIndex = MenuHelper.JumpToFirst();
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Jumps to the last option in the menu.
        /// </summary>
        public static void JumpToLast()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            selectedIndex = MenuHelper.JumpToLast(currentOptions.Count);
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Handles typeahead character input for the float menu.
        /// Called from UnifiedKeyboardPatch to process alphanumeric characters.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive || currentOptions == null || currentOptions.Count == 0)
                return;

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
        }

        /// <summary>
        /// Handles backspace key for typeahead search.
        /// Called from UnifiedKeyboardPatch.
        /// </summary>
        public static void HandleBackspace()
        {
            if (!isActive || currentOptions == null || currentOptions.Count == 0)
                return;

            if (!typeahead.HasActiveSearch)
                return;

            var labels = GetItemLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                }
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Gets whether typeahead search is active.
        /// </summary>
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        /// <summary>
        /// Gets whether typeahead search has no matches.
        /// </summary>
        public static bool HasNoMatches => typeahead.HasNoMatches;

        /// <summary>
        /// Clears the typeahead search and announces the action.
        /// Returns true if there was an active search to clear.
        /// </summary>
        public static bool ClearTypeaheadSearch()
        {
            if (!typeahead.HasActiveSearch)
                return false;

            typeahead.ClearSearchAndAnnounce();
            AnnounceCurrentSelection();
            return true;
        }

        /// <summary>
        /// Handles keyboard input for the menu, including typeahead search.
        /// </summary>
        /// <returns>True if input was handled, false otherwise.</returns>
        public static bool HandleInput()
        {
            if (!isActive || currentOptions == null || currentOptions.Count == 0)
                return false;

            if (Event.current.type != EventType.KeyDown)
                return false;

            KeyCode key = Event.current.keyCode;

            // Handle Home - jump to first
            if (key == KeyCode.Home)
            {
                JumpToFirst();
                Event.current.Use();
                return true;
            }

            // Handle End - jump to last
            if (key == KeyCode.End)
            {
                JumpToLast();
                Event.current.Use();
                return true;
            }

            // Handle Escape - clear search FIRST, then close
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentSelection();
                    Event.current.Use();
                    return true;
                }
                // Let the caller handle normal escape (close menu)
                return false;
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

            // Handle * key - consume to prevent passthrough
            // Use KeyCode instead of Event.current.character (which is empty in Unity IMGUI)
            bool isStar = key == KeyCode.KeypadMultiply || (Event.current.shift && key == KeyCode.Alpha8);
            if (isStar)
            {
                Event.current.Use();
                return true;
            }

            // Handle Up arrow - navigate with search awareness
            if (key == KeyCode.UpArrow)
            {
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    // Navigate through matches only when there ARE matches
                    int prevIndex = typeahead.GetPreviousMatch(selectedIndex);
                    if (prevIndex >= 0)
                    {
                        selectedIndex = prevIndex;
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    // Navigate normally (either no search active, OR search with no matches)
                    SelectPrevious();
                }
                Event.current.Use();
                return true;
            }

            // Handle Down arrow - navigate with search awareness
            if (key == KeyCode.DownArrow)
            {
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    // Navigate through matches only when there ARE matches
                    int nextIndex = typeahead.GetNextMatch(selectedIndex);
                    if (nextIndex >= 0)
                    {
                        selectedIndex = nextIndex;
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    // Navigate normally (either no search active, OR search with no matches)
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
            // Use KeyCode instead of Event.current.character (which is empty in Unity IMGUI)
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            if (isLetter || isNumber)
            {
                char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
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
                Event.current.Use();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the list of labels for all current options.
        /// </summary>
        private static List<string> GetItemLabels()
        {
            var labels = new List<string>();
            if (currentOptions != null)
            {
                foreach (var option in currentOptions)
                {
                    labels.Add(option.Label ?? "");
                }
            }
            return labels;
        }

        /// <summary>
        /// Announces the current selection without search context.
        /// </summary>
        private static void AnnounceCurrentSelection()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= currentOptions.Count)
                return;

            string optionText = currentOptions[selectedIndex].Label;
            if (currentOptions[selectedIndex].Disabled)
            {
                optionText += " (unavailable)";
            }
            TolkHelper.Speak($"{optionText}. {MenuHelper.FormatPosition(selectedIndex, currentOptions.Count)}");
        }

        /// <summary>
        /// Announces the current selection with search context if applicable.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= currentOptions.Count)
                return;

            string optionText = currentOptions[selectedIndex].Label;
            if (currentOptions[selectedIndex].Disabled)
            {
                optionText += " (unavailable)";
            }

            if (typeahead.HasActiveSearch)
            {
                TolkHelper.Speak($"{optionText}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'");
            }
            else
            {
                TolkHelper.Speak($"{optionText}. {MenuHelper.FormatPosition(selectedIndex, currentOptions.Count)}");
            }
        }
    }
}
