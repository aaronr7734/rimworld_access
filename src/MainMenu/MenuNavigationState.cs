using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    public static class MenuNavigationState
    {
        private static int currentColumn = 0;
        private static int selectedIndexColumn0 = 0;
        private static int selectedIndexColumn1 = 0;

        private static List<ListableOption> column0Options = new List<ListableOption>();
        private static List<ListableOption> column1Options = new List<ListableOption>();

        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        public static int CurrentColumn => currentColumn;
        public static int SelectedIndex => currentColumn == 0 ? selectedIndexColumn0 : selectedIndexColumn1;
        public static TypeaheadSearchHelper Typeahead => typeahead;

        /// <summary>
        /// True when the main menu is currently being rendered. Set by
        /// <see cref="MainMenuAccessibilityPatch"/>'s Postfix each frame.
        /// Used by the layout-aware typeahead dispatcher to gate input.
        /// </summary>
        private static int lastRenderedFrame = -1;
        public static bool IsActive => UnityEngine.Time.frameCount - lastRenderedFrame <= 1;
        public static void MarkRendered() { lastRenderedFrame = UnityEngine.Time.frameCount; }

        /// <summary>
        /// Handles typeahead character input from the layout-aware dispatcher.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!IsActive) return;

            // Search the whole menu (both columns) as one list — the main menu is a single
            // logical menu that merely renders in two columns, so a search shouldn't be scoped
            // to whichever column happens to be active.
            var labels = GetAllLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    SetCombinedIndex(newIndex);
                    AnnounceWithSearch();
                }
            }
            else
            {
                typeahead.SpeakNoMatches();
            }
        }

        public static void Initialize(List<ListableOption> col0, List<ListableOption> col1)
        {
            column0Options = col0;
            column1Options = col1;

            // Ensure indices are valid
            if (selectedIndexColumn0 >= col0.Count)
                selectedIndexColumn0 = Math.Max(0, col0.Count - 1);
            if (selectedIndexColumn1 >= col1.Count)
                selectedIndexColumn1 = Math.Max(0, col1.Count - 1);
        }

        public static void MoveUp()
        {
            // Typeahead spans the whole menu (both columns) as one list, so stepping a match
            // can cross from one column to the other — go through the combined index.
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                SetCombinedIndex(typeahead.GetPreviousMatch(GetCombinedIndex()));
                AnnounceWithSearch();
                return;
            }

            if (currentColumn == 0)
            {
                selectedIndexColumn0--;
                if (selectedIndexColumn0 < 0)
                    selectedIndexColumn0 = Math.Max(0, column0Options.Count - 1);
            }
            else
            {
                selectedIndexColumn1--;
                if (selectedIndexColumn1 < 0)
                    selectedIndexColumn1 = Math.Max(0, column1Options.Count - 1);
            }

            UpdateClipboard();
        }

        public static void MoveDown()
        {
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                SetCombinedIndex(typeahead.GetNextMatch(GetCombinedIndex()));
                AnnounceWithSearch();
                return;
            }

            if (currentColumn == 0)
            {
                selectedIndexColumn0++;
                if (selectedIndexColumn0 >= column0Options.Count)
                    selectedIndexColumn0 = 0;
            }
            else
            {
                selectedIndexColumn1++;
                if (selectedIndexColumn1 >= column1Options.Count)
                    selectedIndexColumn1 = 0;
            }

            UpdateClipboard();
        }

        public static void SwitchColumn()
        {
            currentColumn = (currentColumn == 0) ? 1 : 0;
            typeahead.ClearSearch();
            UpdateClipboard();
        }

        public static void ActivateSelected()
        {
            ListableOption selected = GetCurrentSelection();
            if (selected?.action != null)
            {
                selected.action();
            }
        }

        public static ListableOption GetCurrentSelection()
        {
            if (currentColumn == 0 && selectedIndexColumn0 >= 0 && selectedIndexColumn0 < column0Options.Count)
            {
                return column0Options[selectedIndexColumn0];
            }
            else if (currentColumn == 1 && selectedIndexColumn1 >= 0 && selectedIndexColumn1 < column1Options.Count)
            {
                return column1Options[selectedIndexColumn1];
            }
            return null;
        }

        private static void UpdateClipboard()
        {
            ListableOption selected = GetCurrentSelection();
            if (selected != null)
            {
                int count = currentColumn == 0 ? column0Options.Count : column1Options.Count;
                int index = currentColumn == 0 ? selectedIndexColumn0 : selectedIndexColumn1;
                string positionPart = MenuHelper.FormatPosition(index, count);
                string announcement = string.IsNullOrEmpty(positionPart)
                    ? $"{selected.label}."
                    : $"{selected.label}. {positionPart}";
                TolkHelper.SpeakData(announcement);
            }
        }

        public static void Reset()
        {
            currentColumn = 0;
            selectedIndexColumn0 = 0;
            selectedIndexColumn1 = 0;
            typeahead.ClearSearch();
        }

        /// <summary>
        /// Jumps to the first item in the current column.
        /// </summary>
        public static void JumpToFirst()
        {
            // During an active search, Home goes to the first match (anywhere in the menu)
            // and keeps the search.
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                SetCombinedIndex(typeahead.GetFirstMatch());
                AnnounceWithSearch();
                return;
            }
            typeahead.ClearSearch();
            if (currentColumn == 0)
            {
                selectedIndexColumn0 = 0;
            }
            else
            {
                selectedIndexColumn1 = 0;
            }
            UpdateClipboard();
        }

        /// <summary>
        /// Jumps to the last item in the current column.
        /// </summary>
        public static void JumpToLast()
        {
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                SetCombinedIndex(typeahead.GetLastMatch());
                AnnounceWithSearch();
                return;
            }
            typeahead.ClearSearch();
            if (currentColumn == 0)
            {
                selectedIndexColumn0 = Math.Max(0, column0Options.Count - 1);
            }
            else
            {
                selectedIndexColumn1 = Math.Max(0, column1Options.Count - 1);
            }
            UpdateClipboard();
        }

        /// <summary>
        /// Gets the list of labels for the current column for typeahead search.
        /// </summary>
        public static List<string> GetCurrentColumnLabels()
        {
            var labels = new List<string>();
            var currentList = currentColumn == 0 ? column0Options : column1Options;
            foreach (var option in currentList)
            {
                labels.Add(option.label);
            }
            return labels;
        }

        /// <summary>
        /// Labels for the whole menu — column 0 followed by column 1 — used by typeahead so a
        /// search spans both columns as one unified list.
        /// </summary>
        private static List<string> GetAllLabels()
        {
            var labels = new List<string>();
            foreach (var option in column0Options)
                labels.Add(option.label);
            foreach (var option in column1Options)
                labels.Add(option.label);
            return labels;
        }

        /// <summary>
        /// The current selection expressed as a single index across both columns
        /// (column 0 occupies 0..col0-1, column 1 follows). Pairs with <see cref="GetAllLabels"/>.
        /// </summary>
        private static int GetCombinedIndex()
        {
            return currentColumn == 0
                ? selectedIndexColumn0
                : column0Options.Count + selectedIndexColumn1;
        }

        /// <summary>
        /// Selects the item at a combined index, switching the active column as needed so a
        /// typeahead match in either column becomes the current selection.
        /// </summary>
        private static void SetCombinedIndex(int combinedIndex)
        {
            if (combinedIndex < 0) return;
            if (combinedIndex < column0Options.Count)
            {
                currentColumn = 0;
                selectedIndexColumn0 = combinedIndex;
            }
            else
            {
                currentColumn = 1;
                selectedIndexColumn1 = combinedIndex - column0Options.Count;
            }
        }

        /// <summary>
        /// Sets the selected index for the current column.
        /// </summary>
        public static void SetSelectedIndex(int index)
        {
            if (currentColumn == 0)
            {
                if (index >= 0 && index < column0Options.Count)
                    selectedIndexColumn0 = index;
            }
            else
            {
                if (index >= 0 && index < column1Options.Count)
                    selectedIndexColumn1 = index;
            }
        }

        /// <summary>
        /// Announces the current selection with search match info.
        /// </summary>
        public static void AnnounceWithSearch()
        {
            ListableOption selected = GetCurrentSelection();
            if (selected != null)
            {
                int count = currentColumn == 0 ? column0Options.Count : column1Options.Count;
                int index = currentColumn == 0 ? selectedIndexColumn0 : selectedIndexColumn1;
                string positionPart = MenuHelper.FormatPosition(index, count);
                string baseAnnouncement = string.IsNullOrEmpty(positionPart)
                    ? $"{selected.label}."
                    : $"{selected.label}. {positionPart}";
                if (typeahead.HasActiveSearch)
                {
                    TolkHelper.SpeakData(baseAnnouncement + typeahead.BuildSearchContextSuffix());
                }
                else
                {
                    TolkHelper.SpeakData(baseAnnouncement);
                }
            }
        }
    }
}
