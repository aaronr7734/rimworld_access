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
        private static TabularMenuHelper<Pawn> tableHelper;

        public static TypeaheadSearchHelper Typeahead => tableHelper?.Typeahead;
        public static int CurrentAnimalIndex => tableHelper?.CurrentRowIndex ?? 0;

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

            // Initialize table helper
            tableHelper = new TabularMenuHelper<Pawn>(
                getColumnCount: WildlifeMenuHelper.GetTotalColumnCount,
                getItemLabel: WildlifeMenuHelper.GetAnimalName,
                getColumnName: WildlifeMenuHelper.GetColumnName,
                getColumnValue: WildlifeMenuHelper.GetColumnValue,
                sortByColumn: (items, col, desc) => WildlifeMenuHelper.SortWildlifeByColumn(items.ToList(), col, desc),
                defaultSortColumn: 1,  // Predator
                defaultSortDescending: false,
                getColumnTooltip: WildlifeMenuHelper.GetColumnTooltip
            );
            tableHelper.Reset(1, false);

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
            tableHelper?.ClearSearch();
            SoundDefOf.TabClose.PlayOneShotOnCamera();
            TolkHelper.Speak("Wildlife menu closed");
        }

        public static void SelectNextAnimal()
        {
            if (wildlifeList.Count == 0) return;
            tableHelper.SelectNextRow(wildlifeList.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: true);
        }

        public static void SelectPreviousAnimal()
        {
            if (wildlifeList.Count == 0) return;
            tableHelper.SelectPreviousRow(wildlifeList.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: true);
        }

        public static void SelectNextColumn()
        {
            tableHelper.SelectNextColumn();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: false);
        }

        public static void SelectPreviousColumn()
        {
            tableHelper.SelectPreviousColumn();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: false);
        }

        private static void AnnounceCurrentCell(bool includeAnimalName = true)
        {
            if (wildlifeList.Count == 0) return;

            Pawn currentAnimal = wildlifeList[tableHelper.CurrentRowIndex];
            string announcement = tableHelper.BuildCellAnnouncement(currentAnimal, wildlifeList.Count, includeAnimalName);
            TolkHelper.Speak(announcement);
        }

        public static void InteractWithCurrentCell()
        {
            if (wildlifeList.Count == 0) return;

            Pawn currentAnimal = wildlifeList[tableHelper.CurrentRowIndex];

            if (!WildlifeMenuHelper.IsColumnInteractive(tableHelper.CurrentColumnIndex))
            {
                // Just re-announce for non-interactive columns
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentCell(includeAnimalName: false);
                return;
            }

            // Handle interaction based on column type
            WildlifeMenuHelper.ColumnType type = (WildlifeMenuHelper.ColumnType)tableHelper.CurrentColumnIndex;

            switch (type)
            {
                case WildlifeMenuHelper.ColumnType.Name:
                    JumpToAnimalOnMap(currentAnimal);
                    break;
                case WildlifeMenuHelper.ColumnType.Hunt:
                    ToggleHunt(currentAnimal);
                    break;
                case WildlifeMenuHelper.ColumnType.Tame:
                    ToggleTame(currentAnimal);
                    break;
            }
        }

        /// <summary>
        /// Opens an info card for the currently selected wild animal.
        /// </summary>
        public static void OpenInfoCard()
        {
            if (wildlifeList == null || wildlifeList.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No info card available");
                return;
            }
            Pawn animal = wildlifeList[tableHelper.CurrentRowIndex];
            if (animal != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(animal));
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No info card available");
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

        private static void JumpToAnimalOnMap(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null)
            {
                TolkHelper.Speak("Animal not on map", SpeechPriority.High);
                return;
            }

            IntVec3 position = pawn.Position;

            Close();

            MapNavigationState.CurrentCursorPosition = position;
            Find.CameraDriver?.JumpToCurrentMapLoc(position);

            string animalName = WildlifeMenuHelper.GetAnimalName(pawn);
            TolkHelper.Speak($"Jumped to {animalName}");
        }

        public static void ToggleSortByCurrentColumn()
        {
            wildlifeList = tableHelper.ToggleSortByCurrentColumn(wildlifeList, out string direction).ToList();

            string columnName = tableHelper.GetCurrentColumnName();

            SoundDefOf.Click.PlayOneShotOnCamera();
            TolkHelper.Speak($"Sorted by {columnName} ({direction})");

            // Announce current cell after sorting (include animal name since position may have changed)
            AnnounceCurrentCell(includeAnimalName: true);
        }

        #region Typeahead Search

        /// <summary>
        /// Gets a list of animal names for typeahead search.
        /// </summary>
        public static List<string> GetItemLabels()
        {
            return tableHelper.GetItemLabels(wildlifeList);
        }

        /// <summary>
        /// Sets the current animal index directly.
        /// </summary>
        public static void SetCurrentAnimalIndex(int index)
        {
            if (index >= 0 && index < wildlifeList.Count)
            {
                tableHelper.CurrentRowIndex = index;
            }
        }

        /// <summary>
        /// Handles character input for typeahead search.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (tableHelper.HandleTypeahead(c, wildlifeList, out _))
            {
                AnnounceWithSearch();
            }
            else
            {
                TolkHelper.Speak($"No matches for '{tableHelper.Typeahead.LastFailedSearch}'");
            }
        }

        /// <summary>
        /// Handles backspace for typeahead search.
        /// </summary>
        public static void HandleBackspace()
        {
            if (!tableHelper.Typeahead.HasActiveSearch)
                return;

            tableHelper.HandleBackspace(wildlifeList, out _);
            AnnounceWithSearch();
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

            Pawn currentAnimal = wildlifeList[tableHelper.CurrentRowIndex];
            string announcement = tableHelper.BuildCellAnnouncementWithSearch(currentAnimal, wildlifeList.Count);
            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Jumps to the first animal in the list.
        /// </summary>
        public static void JumpToFirst()
        {
            if (wildlifeList.Count == 0)
                return;

            tableHelper.JumpToFirst(wildlifeList.Count);
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

            tableHelper.JumpToLast(wildlifeList.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: true);
        }

        #endregion
    }
}
