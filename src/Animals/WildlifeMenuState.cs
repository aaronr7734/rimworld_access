using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    public static class WildlifeMenuState
    {
        public static bool IsActive { get; private set; } = false;

        private static List<Pawn> wildlifeList = new List<Pawn>();
        private static int currentAnimalIndex = 0;
        private static int currentColumnIndex = 0;
        private static int sortColumnIndex = 4; // Default: Body Size (matches PawnTable_Wildlife)
        private static bool sortDescending = true; // Default: descending (matches PawnTable_Wildlife)
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        public static TypeaheadSearchHelper Typeahead => typeahead;
        public static int CurrentAnimalIndex => currentAnimalIndex;

        public static void Open()
        {
            // Prevent double-opening
            if (IsActive) return;

            if (Find.CurrentMap == null)
            {
                TolkHelper.Speak("No map loaded");
                return;
            }

            // Get all wild animals using same filter as MainTabWindow_Wildlife
            wildlifeList = Find.CurrentMap.mapPawns.AllPawns
                .Where(p => p.Spawned &&
                           (p.Faction == null || p.Faction == Faction.OfInsects) &&
                           p.AnimalOrWildMan() &&
                           !p.Position.Fogged(p.Map) &&
                           !p.IsPrisonerInPrisonCell())
                .ToList();

            if (wildlifeList.Count == 0)
            {
                TolkHelper.Speak("No wildlife found on map");
                return;
            }

            // Apply default sort (by body size descending, then by label)
            wildlifeList = WildlifeMenuHelper.DefaultSort(wildlifeList);

            currentAnimalIndex = 0;
            currentColumnIndex = 0;
            typeahead.ClearSearch();
            IsActive = true;

            SoundDefOf.TabOpen.PlayOneShotOnCamera();

            string announcement = $"Wildlife menu, {wildlifeList.Count} animals";
            TolkHelper.Speak(announcement);
            AnnounceCurrentCell(includeAnimalName: true);
        }

        public static void Close()
        {
            IsActive = false;
            wildlifeList.Clear();
            typeahead.ClearSearch();
            SoundDefOf.TabClose.PlayOneShotOnCamera();
            TolkHelper.Speak("Wildlife menu closed");
        }

        public static void SelectNextAnimal()
        {
            if (wildlifeList.Count == 0) return;

            // Wrap around to first when at end
            currentAnimalIndex = (currentAnimalIndex + 1) % wildlifeList.Count;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: true);
        }

        public static void SelectPreviousAnimal()
        {
            if (wildlifeList.Count == 0) return;

            // Wrap around to last when at start
            currentAnimalIndex = (currentAnimalIndex - 1 + wildlifeList.Count) % wildlifeList.Count;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: true);
        }

        public static void SelectNextColumn()
        {
            int totalColumns = WildlifeMenuHelper.GetTotalColumnCount();
            // Wrap around to first column when at end
            currentColumnIndex = (currentColumnIndex + 1) % totalColumns;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: false);
        }

        public static void SelectPreviousColumn()
        {
            int totalColumns = WildlifeMenuHelper.GetTotalColumnCount();
            // Wrap around to last column when at start
            currentColumnIndex = (currentColumnIndex - 1 + totalColumns) % totalColumns;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: false);
        }

        private static void AnnounceCurrentCell(bool includeAnimalName = true)
        {
            if (wildlifeList.Count == 0) return;

            Pawn currentAnimal = wildlifeList[currentAnimalIndex];
            string columnName = WildlifeMenuHelper.GetColumnName(currentColumnIndex);
            string columnValue = WildlifeMenuHelper.GetColumnValue(currentAnimal, currentColumnIndex);
            string position = MenuHelper.FormatPosition(currentAnimalIndex, wildlifeList.Count);

            string announcement;
            if (includeAnimalName)
            {
                string animalName = WildlifeMenuHelper.GetAnimalName(currentAnimal);
                announcement = $"{animalName} - {columnName}: {columnValue}. {position}";
            }
            else
            {
                announcement = $"{columnName}: {columnValue}";
            }

            TolkHelper.Speak(announcement);
        }

        public static void InteractWithCurrentCell()
        {
            if (wildlifeList.Count == 0) return;

            Pawn currentAnimal = wildlifeList[currentAnimalIndex];

            if (!WildlifeMenuHelper.IsColumnInteractive(currentColumnIndex))
            {
                // Just re-announce for non-interactive columns
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentCell(includeAnimalName: false);
                return;
            }

            // Handle interaction based on column type
            WildlifeMenuHelper.ColumnType type = (WildlifeMenuHelper.ColumnType)currentColumnIndex;

            switch (type)
            {
                case WildlifeMenuHelper.ColumnType.Hunt:
                    ToggleHunt(currentAnimal);
                    break;
                case WildlifeMenuHelper.ColumnType.Tame:
                    ToggleTame(currentAnimal);
                    break;
            }
        }

        private static void ToggleHunt(Pawn pawn)
        {
            bool isNowMarked = WildlifeMenuHelper.ToggleHuntDesignation(pawn);

            if (isNowMarked)
            {
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }
            else
            {
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }

            AnnounceCurrentCell(includeAnimalName: false);
        }

        private static void ToggleTame(Pawn pawn)
        {
            bool? result = WildlifeMenuHelper.ToggleTameDesignation(pawn);

            if (result == null)
            {
                // Cannot tame this animal
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Cannot tame this animal", SpeechPriority.High);
                return;
            }

            if (result.Value)
            {
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }
            else
            {
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }

            AnnounceCurrentCell(includeAnimalName: false);
        }

        public static void ToggleSortByCurrentColumn()
        {
            if (sortColumnIndex == currentColumnIndex)
            {
                // Same column - toggle direction
                sortDescending = !sortDescending;
            }
            else
            {
                // New column - sort ascending
                sortColumnIndex = currentColumnIndex;
                sortDescending = false;
            }

            // Re-sort the list
            wildlifeList = WildlifeMenuHelper.SortWildlifeByColumn(wildlifeList, sortColumnIndex, sortDescending);

            // Try to keep the same animal selected
            Pawn currentAnimal = null;
            if (currentAnimalIndex < wildlifeList.Count)
            {
                currentAnimal = wildlifeList[currentAnimalIndex];
            }

            if (currentAnimal != null)
            {
                currentAnimalIndex = wildlifeList.IndexOf(currentAnimal);
                if (currentAnimalIndex < 0) currentAnimalIndex = 0;
            }
            else
            {
                currentAnimalIndex = 0;
            }

            string direction = sortDescending ? "descending" : "ascending";
            string columnName = WildlifeMenuHelper.GetColumnName(sortColumnIndex);

            SoundDefOf.Click.PlayOneShotOnCamera();
            TolkHelper.Speak($"Sorted by {columnName} ({direction})");

            // Announce current cell after sorting (include animal name since position may have changed)
            AnnounceCurrentCell(includeAnimalName: true);
        }

        #region Typeahead Search

        /// <summary>
        /// Gets a list of animal names for typeahead search.
        /// Searches by animal name only, not by column values.
        /// </summary>
        public static List<string> GetItemLabels()
        {
            return wildlifeList.Select(p => WildlifeMenuHelper.GetAnimalName(p)).ToList();
        }

        /// <summary>
        /// Sets the current animal index directly.
        /// </summary>
        public static void SetCurrentAnimalIndex(int index)
        {
            if (index >= 0 && index < wildlifeList.Count)
            {
                currentAnimalIndex = index;
            }
        }

        /// <summary>
        /// Handles character input for typeahead search.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            var labels = GetItemLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    currentAnimalIndex = newIndex;
                    AnnounceWithSearch();
                }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
            }
        }

        /// <summary>
        /// Handles backspace for typeahead search.
        /// </summary>
        public static void HandleBackspace()
        {
            if (!typeahead.HasActiveSearch)
                return;

            var labels = GetItemLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                    currentAnimalIndex = newIndex;
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Announces the current selection with search context if active.
        /// </summary>
        public static void AnnounceWithSearch()
        {
            if (wildlifeList.Count == 0)
            {
                TolkHelper.Speak("No wildlife");
                return;
            }

            Pawn currentAnimal = wildlifeList[currentAnimalIndex];
            string animalName = WildlifeMenuHelper.GetAnimalName(currentAnimal);
            string columnName = WildlifeMenuHelper.GetColumnName(currentColumnIndex);
            string columnValue = WildlifeMenuHelper.GetColumnValue(currentAnimal, currentColumnIndex);
            string position = MenuHelper.FormatPosition(currentAnimalIndex, wildlifeList.Count);

            string announcement = $"{animalName} - {columnName}: {columnValue}. {position}";

            // Add search context if active
            if (typeahead.HasActiveSearch)
            {
                announcement += $", match {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} for '{typeahead.SearchBuffer}'";
            }

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Jumps to the first animal in the list.
        /// </summary>
        public static void JumpToFirst()
        {
            if (wildlifeList.Count == 0)
                return;

            currentAnimalIndex = 0;
            typeahead.ClearSearch();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: true);
        }

        /// <summary>
        /// Jumps to the last animal in the list.
        /// </summary>
        public static void JumpToLast()
        {
            if (wildlifeList.Count == 0)
                return;

            currentAnimalIndex = wildlifeList.Count - 1;
            typeahead.ClearSearch();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: true);
        }

        #endregion
    }
}
