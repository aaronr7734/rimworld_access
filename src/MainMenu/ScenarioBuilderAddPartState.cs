using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State for the Add Part menu in the Scenario Builder.
    /// Shows a searchable list of all available scenario parts.
    /// </summary>
    public static class ScenarioBuilderAddPartState
    {
        public static bool IsActive { get; private set; }

        private static Scenario currentScenario;
        private static Action<ScenPartDef> onPartSelected;

        private static List<ScenPartDef> availableParts = new List<ScenPartDef>();
        private static int selectedIndex = 0;
        private static TypeaheadSearchHelper typeaheadHelper = new TypeaheadSearchHelper();

        /// <summary>
        /// Opens the add part menu.
        /// </summary>
        public static void Open(Scenario scenario, Action<ScenPartDef> onSelected)
        {
            currentScenario = scenario;
            onPartSelected = onSelected;
            typeaheadHelper.ClearSearch();

            // Build list of addable parts
            BuildPartsList();

            if (availableParts.Count == 0)
            {
                TolkHelper.Speak("No parts available to add.");
                return;
            }

            selectedIndex = 0;
            IsActive = true;

            TolkHelper.Speak($"Add Part. {availableParts.Count} parts available. Type to search.");
            AnnounceCurrentPart();
        }

        /// <summary>
        /// Closes the add part menu.
        /// </summary>
        public static void Close(bool selectPart)
        {
            IsActive = false;

            if (selectPart && selectedIndex >= 0 && selectedIndex < availableParts.Count)
            {
                onPartSelected?.Invoke(availableParts[selectedIndex]);
            }
            else
            {
                onPartSelected?.Invoke(null);
            }

            currentScenario = null;
            availableParts.Clear();
            typeaheadHelper.ClearSearch();
        }

        /// <summary>
        /// Builds the list of parts that can be added to the scenario.
        /// </summary>
        private static void BuildPartsList()
        {
            availableParts.Clear();

            if (currentScenario == null) return;

            // Get all addable parts, filtered by PlayerAddRemovable
            var addable = ScenarioMaker.AddableParts(currentScenario)
                .Where(p => p.PlayerAddRemovable)
                .OrderBy(p => p.label)
                .ToList();

            availableParts.AddRange(addable);
        }

        /// <summary>
        /// Announces the currently selected part.
        /// </summary>
        private static void AnnounceCurrentPart()
        {
            if (availableParts.Count == 0)
            {
                TolkHelper.Speak("No parts available.");
                return;
            }

            var part = availableParts[selectedIndex];
            string positionPart = MenuHelper.FormatPosition(selectedIndex, availableParts.Count);

            // Build the text with label and description (tooltip)
            // Format: "Label: description" or just "Label" if no description
            string text = part.LabelCap;
            if (!string.IsNullOrEmpty(part.description))
            {
                text += $": {part.description}";
            }

            // Add position or search info
            if (typeaheadHelper.HasActiveSearch)
            {
                text += typeaheadHelper.BuildSearchContextSuffix();
            }
            else if (!string.IsNullOrEmpty(positionPart))
            {
                text += $" ({positionPart})";
            }

            TolkHelper.Speak(text);
        }

        #region Navigation

        private static void SelectNext()
        {
            if (availableParts.Count == 0) return;

            typeaheadHelper.ClearSearch();
            selectedIndex = MenuHelper.SelectNext(selectedIndex, availableParts.Count);
            AnnounceCurrentPart();
        }

        private static void SelectPrevious()
        {
            if (availableParts.Count == 0) return;

            typeaheadHelper.ClearSearch();
            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, availableParts.Count);
            AnnounceCurrentPart();
        }

        private static void JumpToFirst()
        {
            if (availableParts.Count == 0) return;

            typeaheadHelper.ClearSearch();
            selectedIndex = 0;
            AnnounceCurrentPart();
        }

        private static void JumpToLast()
        {
            if (availableParts.Count == 0) return;

            typeaheadHelper.ClearSearch();
            selectedIndex = availableParts.Count - 1;
            AnnounceCurrentPart();
        }

        private static void SelectNextMatch()
        {
            if (!typeaheadHelper.HasActiveSearch) return;

            int next = typeaheadHelper.GetNextMatch(selectedIndex);
            if (next >= 0)
            {
                selectedIndex = next;
                AnnounceCurrentPart();
            }
        }

        private static void SelectPreviousMatch()
        {
            if (!typeaheadHelper.HasActiveSearch) return;

            int prev = typeaheadHelper.GetPreviousMatch(selectedIndex);
            if (prev >= 0)
            {
                selectedIndex = prev;
                AnnounceCurrentPart();
            }
        }

        #endregion

        #region Typeahead

        private static bool HandleTypeahead(char character)
        {
            if (availableParts.Count == 0) return false;

            var labels = availableParts.Select(p => p.LabelCap.ToString()).ToList();

            if (typeaheadHelper.ProcessCharacterInput(character, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    AnnounceCurrentPart();
                }
            }
            else
            {
                typeaheadHelper.SpeakNoMatches();
            }

            return true;
        }

        private static bool HandleTypeaheadBackspace()
        {
            if (!typeaheadHelper.HasActiveSearch) return false;

            var labels = availableParts.Select(p => p.LabelCap.ToString()).ToList();

            if (typeaheadHelper.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    AnnounceCurrentPart();
                }
            }

            return true;
        }

        private static bool ClearTypeahead()
        {
            if (typeaheadHelper.ClearSearchAndAnnounce())
            {
                AnnounceCurrentPart();
                return true;
            }
            return false;
        }

        #endregion

        #region Input Handling

        /// <summary>
        /// Handles keyboard input for the add part menu.
        /// Returns true if the input was handled.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!IsActive) return false;

            switch (key)
            {
                case KeyCode.UpArrow:
                    if (typeaheadHelper.HasActiveSearch)
                        SelectPreviousMatch();
                    else
                        SelectPrevious();
                    return true;

                case KeyCode.DownArrow:
                    if (typeaheadHelper.HasActiveSearch)
                        SelectNextMatch();
                    else
                        SelectNext();
                    return true;

                case KeyCode.Home:
                    JumpToFirst();
                    return true;

                case KeyCode.End:
                    JumpToLast();
                    return true;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Close(selectPart: true);
                    return true;

                case KeyCode.Escape:
                    if (typeaheadHelper.HasActiveSearch)
                    {
                        ClearTypeahead();
                    }
                    else
                    {
                        Close(selectPart: false);
                        TolkHelper.Speak("Cancelled");
                    }
                    return true;

                case KeyCode.Backspace:
                    if (typeaheadHelper.HasActiveSearch)
                    {
                        HandleTypeaheadBackspace();
                    }
                    // Always consume backspace to prevent leaking to parent state
                    return true;

                // CRITICAL: Consume navigation keys to prevent leaking to parent state
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                case KeyCode.Tab:
                case KeyCode.Delete:
                    // Silently consume - these don't apply to flat menu navigation
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Handles character input for typeahead.
        /// </summary>
        public static bool HandleCharacterInput(char character)
        {
            if (!IsActive) return false;

            if (char.IsLetterOrDigit(character))
            {
                return HandleTypeahead(character);
            }

            return false;
        }

        #endregion
    }
}
