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

            if (!GuardHelper.RequireMap()) return;

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
                TolkHelper.Speak("RimWorldAccess.Animals.Wildlife.Menu.NoWildlife".Translate());
                return;
            }

            // Initialize column defs for game-native sorting
            WildlifeMenuHelper.InitColumnDefs();

            // Apply vanilla's default sort (by body size descending, then by label)
            // Matches PawnTable_Wildlife.LabelSortFunction
            wildlifeList = wildlifeList
                .OrderByDescending(p => p.RaceProps?.baseBodySize ?? 0)
                .ThenBy(p => p.def.label)
                .ToList();

            // Initialize table helper
            tableHelper = new TabularMenuHelper<Pawn>(
                getColumnCount: WildlifeMenuHelper.GetTotalColumnCount,
                getItemLabel: WildlifeMenuHelper.GetAnimalName,
                getColumnName: WildlifeMenuHelper.GetColumnName,
                getColumnValue: WildlifeMenuHelper.GetColumnValue,
                sortByColumn: (items, col, desc) => WildlifeMenuHelper.SortWildlifeByColumn(items.ToList(), col, desc),
                getColumnTooltip: (pawn, col) => WildlifeMenuHelper.GetColumnTooltip(pawn, col),
                isColumnSortable: WildlifeMenuHelper.IsColumnSortable
            );
            tableHelper.Reset();
            tableHelper.SetDefaultOrder(wildlifeList);

            IsActive = true;

            SoundDefOf.TabOpen.PlayOneShotOnCamera();

            string announcement = "RimWorldAccess.Animals.Wildlife.Menu.OpeningTitle".Translate(wildlifeList.Count).ToString();
            TolkHelper.Speak(announcement);
            AnnounceCurrentCell(includeAnimalName: true);
        }

        public static void Close()
        {
            IsActive = false;
            wildlifeList.Clear();
            tableHelper?.ClearSearch();
            SoundDefOf.TabClose.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Animals.Wildlife.Menu.Closed".Translate());
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
            Pawn animal = null;
            if (wildlifeList != null && wildlifeList.Count > 0)
            {
                animal = wildlifeList[tableHelper.CurrentRowIndex];
            }
            if (animal != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(animal));
            }
            else
            {
                InfoCardState.SpeakNoInfoCardAvailable();
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
            ResortAfterEdit();
        }

        private static void ToggleTame(Pawn pawn)
        {
            bool? result = WildlifeMenuHelper.ToggleTameDesignation(pawn);

            if (result == null)
            {
                // Cannot tame this animal
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Animals.Wildlife.Menu.CannotTame".Translate(), SpeechPriority.High);
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
            ResortAfterEdit();
        }

        /// <summary>
        /// Re-sorts the list if currently sorted by the column that was just edited.
        /// Keeps cursor at same index and announces the new item at that position.
        /// </summary>
        private static void ResortAfterEdit()
        {
            var resorted = tableHelper.ResortAfterEdit(wildlifeList);
            if (resorted != null)
            {
                wildlifeList = resorted.ToList();
                string cellText = tableHelper.BuildCellAnnouncement(
                    wildlifeList[tableHelper.CurrentRowIndex], wildlifeList.Count, includeItemName: true);
                string announcement = "RimWorldAccess.Animals.Sort.NowAt".Translate(cellText).ToString();
                TolkHelper.Speak(announcement);
            }
        }

        private static void JumpToAnimalOnMap(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null)
            {
                TolkHelper.Speak("RimWorldAccess.Animals.Wildlife.Menu.NotOnMap".Translate(), SpeechPriority.High);
                return;
            }

            IntVec3 position = pawn.Position;

            Close();

            MapNavigationState.CurrentCursorPosition = position;
            Find.CameraDriver?.JumpToCurrentMapLoc(position);

            string animalName = WildlifeMenuHelper.GetAnimalName(pawn);
            MapNavigationState.SpeakJumpedTo(animalName);
        }

        public static void ToggleSortByCurrentColumn()
        {
            var result = tableHelper.ToggleSortByCurrentColumn(wildlifeList, out string direction, out bool sortCleared);

            if (result == null)
            {
                // Column not sortable
                string colName = tableHelper.GetCurrentColumnName();
                TolkHelper.Speak("RimWorldAccess.Animals.Sort.CannotSort".Translate(colName));
                return;
            }

            wildlifeList = result.ToList();

            if (sortCleared)
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Animals.Sort.Cleared".Translate());
            }
            else
            {
                string columnName = tableHelper.GetCurrentColumnName();
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Animals.Sort.SortedBy".Translate(columnName, direction));
            }

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
                tableHelper.Typeahead.SpeakNoMatches();
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
                TolkHelper.Speak("RimWorldAccess.Animals.Wildlife.Menu.None".Translate());
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

        #region Painting

        /// <summary>
        /// Paints the current cell's value to the next row and moves down.
        /// </summary>
        public static void PaintDown()
        {
            if (wildlifeList.Count <= 1) return;

            int col = tableHelper.CurrentColumnIndex;
            if (!WildlifeMenuHelper.CanPaintColumn(col))
            {
                MenuHelper.SpeakCannotPaintColumn();
                return;
            }

            Pawn sourcePawn = wildlifeList[tableHelper.CurrentRowIndex];
            bool brushValue = WildlifeMenuHelper.GetPaintableValue(sourcePawn, col);

            tableHelper.SelectNextRow(wildlifeList.Count);
            Pawn targetPawn = wildlifeList[tableHelper.CurrentRowIndex];

            string colName = WildlifeMenuHelper.GetColumnName(col);
            string valueLabel = WildlifeMenuHelper.GetPaintValueLabel(col, brushValue);
            string pos = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, wildlifeList.Count);

            bool targetValue = WildlifeMenuHelper.GetPaintableValue(targetPawn, col);
            if (targetValue == brushValue)
            {
                TolkHelper.Speak("RimWorldAccess.Animals.Paint.Single.CellAlready".Translate(targetPawn.LabelShort, colName, valueLabel, pos));
                return;
            }

            bool applied = WildlifeMenuHelper.SetPaintableValue(targetPawn, col, brushValue);
            SoundDef sound = applied
                ? WildlifeMenuHelper.GetPaintSound(col, brushValue)
                : SoundDefOf.ClickReject;
            sound.PlayOneShotOnCamera();

            TolkHelper.Speak("RimWorldAccess.Animals.Paint.Single.CellApplied".Translate(targetPawn.LabelShort, colName, valueLabel, pos));
        }

        /// <summary>
        /// Paints the current cell's value to the previous row and moves up.
        /// </summary>
        public static void PaintUp()
        {
            if (wildlifeList.Count <= 1) return;

            int col = tableHelper.CurrentColumnIndex;
            if (!WildlifeMenuHelper.CanPaintColumn(col))
            {
                MenuHelper.SpeakCannotPaintColumn();
                return;
            }

            Pawn sourcePawn = wildlifeList[tableHelper.CurrentRowIndex];
            bool brushValue = WildlifeMenuHelper.GetPaintableValue(sourcePawn, col);

            tableHelper.SelectPreviousRow(wildlifeList.Count);
            Pawn targetPawn = wildlifeList[tableHelper.CurrentRowIndex];

            string colName = WildlifeMenuHelper.GetColumnName(col);
            string valueLabel = WildlifeMenuHelper.GetPaintValueLabel(col, brushValue);
            string pos = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, wildlifeList.Count);

            bool targetValue = WildlifeMenuHelper.GetPaintableValue(targetPawn, col);
            if (targetValue == brushValue)
            {
                TolkHelper.Speak("RimWorldAccess.Animals.Paint.Single.CellAlready".Translate(targetPawn.LabelShort, colName, valueLabel, pos));
                return;
            }

            bool applied = WildlifeMenuHelper.SetPaintableValue(targetPawn, col, brushValue);
            SoundDef sound = applied
                ? WildlifeMenuHelper.GetPaintSound(col, brushValue)
                : SoundDefOf.ClickReject;
            sound.PlayOneShotOnCamera();

            TolkHelper.Speak("RimWorldAccess.Animals.Paint.Single.CellApplied".Translate(targetPawn.LabelShort, colName, valueLabel, pos));
        }

        /// <summary>
        /// Bulk paints the current value from the current row to the last row.
        /// </summary>
        public static void PaintToLast()
        {
            PaintBulk(towardFirst: false, entireColumn: false);
        }

        /// <summary>
        /// Bulk paints the current value from the current row to the first row.
        /// </summary>
        public static void PaintToFirst()
        {
            PaintBulk(towardFirst: true, entireColumn: false);
        }

        /// <summary>
        /// Paints the current value to the entire column.
        /// </summary>
        public static void PaintEntireColumn(bool towardFirst)
        {
            PaintBulk(towardFirst, entireColumn: true);
        }

        private static void PaintBulk(bool towardFirst, bool entireColumn)
        {
            if (wildlifeList.Count == 0) return;

            int col = tableHelper.CurrentColumnIndex;
            if (!WildlifeMenuHelper.CanPaintColumn(col))
            {
                MenuHelper.SpeakCannotPaintColumn();
                return;
            }

            int currentRow = tableHelper.CurrentRowIndex;
            string colName = WildlifeMenuHelper.GetColumnName(col);

            int startRow, endRow;
            if (entireColumn)
            {
                startRow = 0;
                endRow = wildlifeList.Count - 1;
            }
            else if (towardFirst)
            {
                startRow = 0;
                endRow = currentRow;
            }
            else
            {
                startRow = currentRow;
                endRow = wildlifeList.Count - 1;
            }

            Pawn sourcePawn = wildlifeList[currentRow];
            bool brushValue = WildlifeMenuHelper.GetPaintableValue(sourcePawn, col);
            string valueLabel = WildlifeMenuHelper.GetPaintValueLabel(col, brushValue);
            SoundDef paintSound = WildlifeMenuHelper.GetPaintSound(col, brushValue);

            var changed = new List<string>();
            for (int i = startRow; i <= endRow; i++)
            {
                Pawn pawn = wildlifeList[i];
                bool currentValue = WildlifeMenuHelper.GetPaintableValue(pawn, col);
                if (currentValue != brushValue)
                {
                    if (WildlifeMenuHelper.SetPaintableValue(pawn, col, brushValue))
                        changed.Add(pawn.LabelShort);
                }
            }

            tableHelper.CurrentRowIndex = towardFirst ? startRow : endRow;

            if (changed.Count > 0)
            {
                BulkSoundQueue.Queue(changed.Count, paintSound);
                TolkHelper.Speak("RimWorldAccess.Animals.Paint.Bulk.CellApplied".Translate(colName, valueLabel, MenuHelper.FormatNameList(changed)));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Animals.Wildlife.Paint.Bulk.CellAlreadyAll".Translate(colName, valueLabel));
            }
        }

        #endregion
    }
}
