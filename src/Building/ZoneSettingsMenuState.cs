using System;
using System.Collections.Generic;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages a unified zone settings menu accessible via Z key when cursor is on a zone.
    /// Provides options for storage settings, plant selection, zone expansion, and deletion.
    /// </summary>
    public static class ZoneSettingsMenuState
    {
        private static List<MenuOption> currentOptions = null;
        private static int selectedIndex = 0;
        private static bool isActive = false;
        private static Zone currentZone = null;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        public static bool IsActive => isActive;

        /// <summary>
        /// Gets whether typeahead search is active.
        /// </summary>
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        /// <summary>
        /// Gets whether typeahead search has no matches.
        /// </summary>
        public static bool HasNoMatches => typeahead.HasNoMatches;

        /// <summary>
        /// Opens the zone settings menu for the specified zone.
        /// </summary>
        public static void Open(Zone zone)
        {
            if (zone == null)
            {
                Log.Error("Cannot open zone settings menu: zone is null");
                return;
            }

            currentZone = zone;
            BuildMenuOptions();
            selectedIndex = 0;
            isActive = true;
            typeahead.ClearSearch();

            // Announce menu opened and first option
            TolkHelper.Speak($"Zone settings for {zone.label}");
            AnnounceCurrentOption();

            Log.Message($"Opened zone settings menu for: {zone.label}");
        }

        /// <summary>
        /// Closes the zone settings menu.
        /// </summary>
        public static void Close()
        {
            currentOptions = null;
            selectedIndex = 0;
            isActive = false;
            currentZone = null;
            typeahead.ClearSearch();
        }

        /// <summary>
        /// Moves selection to next option.
        /// </summary>
        public static void SelectNext()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, currentOptions.Count);
            AnnounceCurrentOption();
        }

        /// <summary>
        /// Moves selection to previous option.
        /// </summary>
        public static void SelectPrevious()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, currentOptions.Count);
            AnnounceCurrentOption();
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

            MenuOption selected = currentOptions[selectedIndex];

            // Execute the action
            selected.Action?.Invoke();
        }

        /// <summary>
        /// Jumps to the first option in the list.
        /// </summary>
        public static void JumpToFirst()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            selectedIndex = MenuHelper.JumpToFirst();
            typeahead.ClearSearch();
            AnnounceCurrentOption();
        }

        /// <summary>
        /// Jumps to the last option in the list.
        /// </summary>
        public static void JumpToLast()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            selectedIndex = MenuHelper.JumpToLast(currentOptions.Count);
            typeahead.ClearSearch();
            AnnounceCurrentOption();
        }

        /// <summary>
        /// Processes a typeahead character for search.
        /// </summary>
        public static void ProcessTypeaheadCharacter(char c)
        {
            if (!isActive || currentOptions == null || currentOptions.Count == 0)
                return;

            var labels = GetOptionLabels();
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
        /// Processes backspace for typeahead search.
        /// </summary>
        public static void ProcessBackspace()
        {
            if (!isActive || currentOptions == null || currentOptions.Count == 0)
                return;

            if (!typeahead.HasActiveSearch)
                return;

            var labels = GetOptionLabels();
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
        /// Clears the typeahead search and announces.
        /// </summary>
        public static void ClearTypeaheadSearch()
        {
            typeahead.ClearSearchAndAnnounce();
            AnnounceCurrentOption();
        }

        /// <summary>
        /// Selects the next match in the filtered list.
        /// </summary>
        public static void SelectNextMatch()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            int nextIndex = typeahead.GetNextMatch(selectedIndex);
            if (nextIndex >= 0)
            {
                selectedIndex = nextIndex;
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Selects the previous match in the filtered list.
        /// </summary>
        public static void SelectPreviousMatch()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            int prevIndex = typeahead.GetPreviousMatch(selectedIndex);
            if (prevIndex >= 0)
            {
                selectedIndex = prevIndex;
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Gets the labels for all current options.
        /// </summary>
        private static List<string> GetOptionLabels()
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
        /// Announces the current selection with search context if applicable.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= currentOptions.Count)
                return;

            MenuOption current = currentOptions[selectedIndex];

            if (typeahead.HasActiveSearch)
            {
                TolkHelper.Speak($"{current.Label}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'");
            }
            else
            {
                AnnounceCurrentOption();
            }
        }

        private static void AnnounceCurrentOption()
        {
            if (selectedIndex >= 0 && selectedIndex < currentOptions.Count)
            {
                string label = currentOptions[selectedIndex].Label;
                string position = MenuHelper.FormatPosition(selectedIndex, currentOptions.Count);
                TolkHelper.Speak($"{label}. {position}");
            }
        }

        /// <summary>
        /// Builds the list of menu options based on the current zone type.
        /// Universal options (Expand, Delete) come first, then zone-specific settings.
        /// </summary>
        private static void BuildMenuOptions()
        {
            currentOptions = new List<MenuOption>();

            if (currentZone == null)
                return;

            // Universal options for all zone types (shown first)

            // Edit zone (expand/shrink)
            currentOptions.Add(new MenuOption(
                "Edit Zone",
                ExpandCurrentZone
            ));

            // Rename zone
            currentOptions.Add(new MenuOption(
                "Rename Zone",
                RenameCurrentZone
            ));

            // Delete zone
            currentOptions.Add(new MenuOption(
                "Delete Zone",
                DeleteCurrentZone
            ));

            // Zone-specific options (shown after universal options)
            if (currentZone is Zone_Stockpile stockpile)
            {
                // Configure storage settings
                currentOptions.Add(new MenuOption(
                    "Storage Settings",
                    () => {
                        StorageSettings settings = stockpile.GetStoreSettings();
                        if (settings != null)
                        {
                            Close(); // Close this menu before opening storage settings
                            StorageSettingsMenuState.Open(settings);
                            TolkHelper.Speak($"Storage settings for {currentZone.label}");
                        }
                        else
                        {
                            TolkHelper.Speak("Cannot access storage settings", SpeechPriority.High);
                        }
                    }
                ));
            }
            else if (currentZone is Zone_Growing growingZone)
            {
                // Plant selection menu
                currentOptions.Add(new MenuOption(
                    "Plant Settings",
                    () => {
                        Close(); // Close this menu before opening plant selection
                        PlantSelectionMenuState.Open(growingZone);
                        TolkHelper.Speak($"Plant selection for {currentZone.label}");
                    }
                ));

                // Auto-sow toggle
                currentOptions.Add(new MenuOption(
                    $"Auto-Sow: {(growingZone.allowSow ? "On" : "Off")}",
                    () => {
                        growingZone.allowSow = !growingZone.allowSow;
                        TolkHelper.Speak($"Auto-Sow {(growingZone.allowSow ? "enabled" : "disabled")}");
                        BuildMenuOptions(); // Refresh to update label
                        AnnounceCurrentOption();
                    }
                ));

                // Auto-harvest toggle
                currentOptions.Add(new MenuOption(
                    $"Auto-Harvest: {(growingZone.allowCut ? "On" : "Off")}",
                    () => {
                        growingZone.allowCut = !growingZone.allowCut;
                        TolkHelper.Speak($"Auto-Harvest {(growingZone.allowCut ? "enabled" : "disabled")}");
                        BuildMenuOptions(); // Refresh
                        AnnounceCurrentOption();
                    }
                ));
            }

            // Cancel (always last)
            currentOptions.Add(new MenuOption(
                "Cancel",
                Close
            ));
        }

        /// <summary>
        /// Expands the current zone by entering expansion mode.
        /// </summary>
        private static void ExpandCurrentZone()
        {
            if (currentZone == null)
            {
                TolkHelper.Speak("Cannot expand: no zone selected", SpeechPriority.High);
                return;
            }

            Zone zoneToExpand = currentZone; // Save reference before closing
            Close(); // Close menu before entering expansion mode
            ZoneCreationState.EnterExpansionMode(zoneToExpand);
        }

        /// <summary>
        /// Renames the current zone by opening the rename dialog.
        /// </summary>
        private static void RenameCurrentZone()
        {
            if (currentZone == null)
            {
                TolkHelper.Speak("Cannot rename: no zone selected", SpeechPriority.High);
                return;
            }

            Zone zoneToRename = currentZone; // Save reference before closing
            Close(); // Close menu before opening rename dialog
            ZoneRenameState.Open(zoneToRename);
        }

        /// <summary>
        /// Deletes the current zone after confirmation.
        /// </summary>
        private static void DeleteCurrentZone()
        {
            if (currentZone == null)
            {
                TolkHelper.Speak("Cannot delete: no zone selected", SpeechPriority.High);
                return;
            }

            string zoneName = currentZone.label;
            Zone zoneToDelete = currentZone;

            Close(); // Close menu before opening confirmation

            WindowlessConfirmationState.Open(
                $"Delete {zoneName}? This cannot be undone.",
                () => {
                    try
                    {
                        zoneToDelete.Delete(playSound: true); // RimWorld handles all cleanup
                        TolkHelper.Speak($"Deleted {zoneName}", SpeechPriority.High);
                        Log.Message($"Deleted zone: {zoneName}");
                    }
                    catch (Exception ex)
                    {
                        TolkHelper.Speak($"Error deleting zone: {ex.Message}", SpeechPriority.High);
                        Log.Error($"Error deleting zone: {ex}");
                    }
                }
            );
        }

        /// <summary>
        /// Simple data structure for menu options.
        /// </summary>
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
