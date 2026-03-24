using System;
using System.Collections.Generic;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    public static class ExtraMenusState
    {
        private static List<MenuOption> currentOptions = null;
        private static int selectedIndex = 0;
        private static bool isActive = false;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        public static bool IsActive => isActive;
        public static TypeaheadSearchHelper Typeahead => typeahead;
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        public static void Open()
        {
            var options = BuildMenuOptions();

            if (options.Count == 0)
            {
                TolkHelper.Speak("No extra menus available");
                return;
            }

            if (options.Count == 1)
            {
                TolkHelper.Speak(options[0].Label);
                options[0].Action?.Invoke();
                return;
            }

            currentOptions = options;
            selectedIndex = 0;
            isActive = true;
            typeahead.ClearSearch();
            AnnounceCurrentOption();
        }

        public static void Close()
        {
            currentOptions = null;
            selectedIndex = 0;
            isActive = false;
            typeahead.ClearSearch();
        }

        public static void SelectNext()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, currentOptions.Count);
            AnnounceCurrentOption();
        }

        public static void SelectPrevious()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, currentOptions.Count);
            AnnounceCurrentOption();
        }

        public static void ExecuteSelected()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= currentOptions.Count)
                return;

            MenuOption selected = currentOptions[selectedIndex];

            Close();

            selected.Action?.Invoke();
        }

        private static void AnnounceCurrentOption()
        {
            if (selectedIndex >= 0 && selectedIndex < currentOptions.Count)
            {
                TolkHelper.Speak($"{currentOptions[selectedIndex].Label}. {MenuHelper.FormatPosition(selectedIndex, currentOptions.Count)}");
            }
        }

        public static bool HandleInput()
        {
            if (!isActive || currentOptions == null || currentOptions.Count == 0)
                return false;

            if (Event.current.type != EventType.KeyDown)
                return false;

            KeyCode key = Event.current.keyCode;

            // Handle Escape - clear search first, then let caller close
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    Event.current.Use();
                    return true;
                }
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

            // Handle Up arrow
            if (key == KeyCode.UpArrow)
            {
                if (typeahead.HasActiveSearch)
                {
                    if (typeahead.HasNoMatches)
                    {
                        selectedIndex = MenuHelper.SelectPrevious(selectedIndex, currentOptions.Count);
                        AnnounceWithSearch();
                    }
                    else
                    {
                        int prevIndex = typeahead.GetPreviousMatch(selectedIndex);
                        if (prevIndex >= 0)
                        {
                            selectedIndex = prevIndex;
                            AnnounceWithSearch();
                        }
                    }
                }
                else
                {
                    SelectPrevious();
                }
                Event.current.Use();
                return true;
            }

            // Handle Down arrow
            if (key == KeyCode.DownArrow)
            {
                if (typeahead.HasActiveSearch)
                {
                    if (typeahead.HasNoMatches)
                    {
                        selectedIndex = MenuHelper.SelectNext(selectedIndex, currentOptions.Count);
                        AnnounceWithSearch();
                    }
                    else
                    {
                        int nextIndex = typeahead.GetNextMatch(selectedIndex);
                        if (nextIndex >= 0)
                        {
                            selectedIndex = nextIndex;
                            AnnounceWithSearch();
                        }
                    }
                }
                else
                {
                    SelectNext();
                }
                Event.current.Use();
                return true;
            }

            // Handle Home - jump to first item
            if (key == KeyCode.Home)
            {
                selectedIndex = 0;
                typeahead.ClearSearch();
                AnnounceCurrentOption();
                Event.current.Use();
                return true;
            }

            // Handle End - jump to last item
            if (key == KeyCode.End)
            {
                selectedIndex = currentOptions.Count - 1;
                typeahead.ClearSearch();
                AnnounceCurrentOption();
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

        private static List<string> GetItemLabels()
        {
            var labels = new List<string>();
            if (currentOptions != null)
            {
                foreach (var option in currentOptions)
                {
                    labels.Add(option.Label);
                }
            }
            return labels;
        }

        private static void AnnounceWithSearch()
        {
            if (!isActive || currentOptions == null || currentOptions.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= currentOptions.Count)
                return;

            string label = currentOptions[selectedIndex].Label;

            if (typeahead.HasActiveSearch)
            {
                if (typeahead.HasNoMatches)
                {
                    TolkHelper.Speak($"{label}. {MenuHelper.FormatPosition(selectedIndex, currentOptions.Count)}. No matches for '{typeahead.LastFailedSearch}'");
                }
                else
                {
                    TolkHelper.Speak($"{label}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'");
                }
            }
            else
            {
                AnnounceCurrentOption();
            }
        }

        private static List<MenuOption> BuildMenuOptions()
        {
            var options = new List<MenuOption>();

            options.Add(new MenuOption("Factions", () =>
            {
                MainButtonDefOf.Factions.Worker.Activate();
            }));

            if (ModsConfig.IdeologyActive)
            {
                options.Add(new MenuOption("Ideology", () =>
                {
                    MainButtonDefOf.Ideos.Worker.Activate();
                }));
            }

            return options;
        }

        private class MenuOption
        {
            public string Label { get; }
            public Action Action { get; }

            public MenuOption(string label, Action action)
            {
                Label = label;
                Action = action;
            }
        }
    }
}
