using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages the state and navigation for the grid-based work assignment menu.
    ///
    /// Manual Mode: 5-row virtual grid (Priority 1, 2, 3, 4, Disabled)
    /// Basic Mode: Single list of all tasks with enable/disable toggle
    ///
    /// Up/Down navigates between priority levels.
    /// Left/Right navigates between tasks within a priority level.
    /// Shift+Left/Right reorders tasks within their priority level for execution order.
    /// </summary>
    public static class WorkMenuState
    {
        private static bool isActive = false;
        private static Pawn currentPawn = null;
        private static int currentPawnIndex = 0;
        private static List<Pawn> allPawns = new List<Pawn>();

        // Grid navigation state (manual mode)
        private static int currentColumn = 0; // 0-4: Priority 1-4, then Disabled
        private static int currentRow = 0;

        // Basic mode navigation state
        private static int basicModeIndex = 0;

        // Work entries organized by priority column
        // Index 0 = Priority 1, Index 1 = Priority 2, etc., Index 4 = Disabled (0)
        private static List<List<WorkTypeEntry>> columns = new List<List<WorkTypeEntry>>();

        // Flat list for basic mode and searching
        private static List<WorkTypeEntry> allEntries = new List<WorkTypeEntry>();

        // Original priorities for tracking changes
        private static Dictionary<WorkTypeDef, int> originalPriorities = new Dictionary<WorkTypeDef, int>();

        // Original order for each priority level (for revert)
        private static Dictionary<WorkTypeDef, int> originalNaturalPriorities = new Dictionary<WorkTypeDef, int>();

        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
        private static bool searchJumpPending = false;
        private static int searchTargetColumn = -1;
        private static int searchTargetRow = -1;

        // Track if changes were made to current pawn
        private static bool hasUnsavedChanges = false;

        public static bool IsActive => isActive;
        public static Pawn CurrentPawn => currentPawn;
        public static int CurrentPawnIndex => currentPawnIndex;
        public static int TotalPawns => allPawns.Count;
        public static TypeaheadSearchHelper Typeahead => typeahead;
        public static int CurrentColumn => currentColumn;
        public static int CurrentRow => currentRow;
        public static bool IsManualMode => Find.PlaySettings.useWorkPriorities;
        public static bool SearchJumpPending => searchJumpPending;

        /// <summary>
        /// Opens the work menu for the specified pawn.
        /// </summary>
        public static void Open(Pawn pawn)
        {
            if (pawn == null || pawn.workSettings == null)
                return;

            isActive = true;
            currentPawn = pawn;
            currentColumn = 0;
            currentRow = 0;
            basicModeIndex = 0;
            typeahead.ClearSearch();
            searchJumpPending = false;
            hasUnsavedChanges = false;

            // Build list of all colonists
            allPawns.Clear();
            if (Find.CurrentMap != null)
            {
                allPawns = Find.CurrentMap.mapPawns.FreeColonists
                    .Where(p => !p.DevelopmentalStage.Baby())
                    .ToList();
                currentPawnIndex = allPawns.IndexOf(pawn);
                if (currentPawnIndex < 0)
                    currentPawnIndex = 0;
            }

            LoadWorkTypesForCurrentPawn();

            // Position cursor at first populated column in manual mode
            if (IsManualMode)
            {
                FindFirstPopulatedColumn();
            }

            TolkHelper.Speak("Work menu");
            AnnounceCurrentPosition(true);
        }

        /// <summary>
        /// Loads work types for the current pawn and organizes them into columns.
        /// </summary>
        private static void LoadWorkTypesForCurrentPawn()
        {
            if (currentPawn == null || currentPawn.workSettings == null)
                return;

            // Clear previous state
            columns.Clear();
            for (int i = 0; i < 5; i++)
                columns.Add(new List<WorkTypeEntry>());

            allEntries.Clear();
            originalPriorities.Clear();
            originalNaturalPriorities.Clear();

            // Build the list of work types
            var allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading
                .Where(w => w.visible)
                .OrderByDescending(w => w.naturalPriority)
                .ToList();

            foreach (var workType in allWorkTypes)
            {
                bool isPermanentlyDisabled = currentPawn.WorkTypeIsDisabled(workType);
                int priority = isPermanentlyDisabled ? 0 : currentPawn.workSettings.GetPriority(workType);

                var entry = new WorkTypeEntry
                {
                    WorkType = workType,
                    IsPermanentlyDisabled = isPermanentlyDisabled,
                    CurrentPriority = priority
                };

                allEntries.Add(entry);
                originalPriorities[workType] = priority;
                originalNaturalPriorities[workType] = workType.naturalPriority;

                // Add to appropriate column
                int columnIndex = PriorityToColumnIndex(priority);
                columns[columnIndex].Add(entry);
            }

            // Sort disabled column: voluntarily disabled first, then permanently disabled
            columns[4] = columns[4]
                .OrderBy(e => e.IsPermanentlyDisabled ? 1 : 0)
                .ThenByDescending(e => e.WorkType.naturalPriority)
                .ToList();
        }

        /// <summary>
        /// Converts priority value (0-4) to column index (0-4).
        /// Priority 1 = column 0, Priority 2 = column 1, etc.
        /// Priority 0 (disabled) = column 4.
        /// </summary>
        private static int PriorityToColumnIndex(int priority)
        {
            if (priority == 0) return 4; // Disabled
            return priority - 1; // Priority 1-4 -> column 0-3
        }

        /// <summary>
        /// Converts column index (0-4) to priority value (0-4).
        /// </summary>
        private static int ColumnIndexToPriority(int columnIndex)
        {
            if (columnIndex == 4) return 0; // Disabled
            return columnIndex + 1; // Column 0-3 -> Priority 1-4
        }

        /// <summary>
        /// Finds the first non-empty column and positions cursor there.
        /// </summary>
        private static void FindFirstPopulatedColumn()
        {
            for (int i = 0; i < 5; i++)
            {
                if (columns[i].Count > 0)
                {
                    currentColumn = i;
                    currentRow = 0;
                    return;
                }
            }
            // All columns empty (shouldn't happen)
            currentColumn = 0;
            currentRow = 0;
        }

        /// <summary>
        /// Closes the menu without applying changes (reverts all).
        /// </summary>
        public static void Cancel()
        {
            // Revert all priorities to original
            if (currentPawn != null && currentPawn.workSettings != null)
            {
                foreach (var kvp in originalPriorities)
                {
                    if (!currentPawn.WorkTypeIsDisabled(kvp.Key))
                    {
                        currentPawn.workSettings.SetPriority(kvp.Key, kvp.Value);
                    }
                }

                // Revert natural priorities (execution order)
                foreach (var kvp in originalNaturalPriorities)
                {
                    kvp.Key.naturalPriority = kvp.Value;
                }

                // Refresh work givers cache
                currentPawn.workSettings.Notify_UseWorkPrioritiesChanged();
            }

            CleanupState();
            TolkHelper.Speak("Work menu cancelled, changes discarded");
        }

        /// <summary>
        /// Closes the menu and applies all pending changes.
        /// </summary>
        public static void Confirm()
        {
            if (currentPawn == null || currentPawn.workSettings == null)
            {
                Cancel();
                return;
            }

            string pawnName = currentPawn.LabelShort;

            // Changes are already applied in real-time, just need to finalize
            // Force refresh of work givers cache
            currentPawn.workSettings.Notify_UseWorkPrioritiesChanged();

            // Refresh all pawns if natural priorities changed
            RefreshAllPawnsWorkGivers();

            CleanupState();
            TolkHelper.Speak($"{pawnName}'s work preferences saved");
        }

        /// <summary>
        /// Saves changes for current pawn and switches to another pawn.
        /// </summary>
        private static void SaveAndSwitchPawn(int newPawnIndex)
        {
            string previousPawnName = currentPawn?.LabelShort ?? "Unknown";
            bool hadChanges = hasUnsavedChanges;

            if (currentPawn != null && currentPawn.workSettings != null)
            {
                // Changes are applied in real-time, just refresh
                currentPawn.workSettings.Notify_UseWorkPrioritiesChanged();
            }

            // Preserve the current column when switching pawns
            int preservedColumn = currentColumn;

            currentPawnIndex = newPawnIndex;
            currentPawn = allPawns[currentPawnIndex];
            basicModeIndex = 0;
            typeahead.ClearSearch();
            searchJumpPending = false;
            hasUnsavedChanges = false;

            LoadWorkTypesForCurrentPawn();

            if (IsManualMode)
            {
                // Stay at the same priority level
                currentColumn = preservedColumn;
                currentRow = 0;
            }

            if (hadChanges)
            {
                TolkHelper.Speak($"{previousPawnName}'s work preferences saved. Now editing: {currentPawn.LabelShort}. {MenuHelper.FormatPosition(currentPawnIndex, allPawns.Count)}");
            }
            else
            {
                TolkHelper.Speak($"Now editing: {currentPawn.LabelShort}. {MenuHelper.FormatPosition(currentPawnIndex, allPawns.Count)}");
            }
            AnnounceCurrentPosition(true);
        }

        private static void CleanupState()
        {
            isActive = false;
            currentPawn = null;
            currentPawnIndex = 0;
            allPawns.Clear();
            columns.Clear();
            allEntries.Clear();
            originalPriorities.Clear();
            originalNaturalPriorities.Clear();
            typeahead.ClearSearch();
            searchJumpPending = false;
        }

        private static void RefreshAllPawnsWorkGivers()
        {
            if (Find.CurrentMap != null)
            {
                foreach (Pawn pawn in Find.CurrentMap.mapPawns.FreeColonists)
                {
                    if (pawn.workSettings != null)
                    {
                        pawn.workSettings.Notify_UseWorkPrioritiesChanged();
                    }
                }
            }
        }

        #region Navigation

        /// <summary>
        /// Moves cursor up to previous priority column (manual mode only).
        /// Not used in basic mode.
        /// </summary>
        public static void MoveUp()
        {
            if (!IsManualMode)
            {
                TolkHelper.Speak("Up/Down navigation only in manual mode. Press Alt+M to switch.");
                return;
            }

            // Navigate to previous (higher) priority column
            if (currentColumn > 0)
            {
                currentColumn--;
                ClampRowToColumn();
                AnnounceCurrentPosition(true);
            }
            else
            {
                TolkHelper.Speak("At highest priority");
            }
        }

        /// <summary>
        /// Moves cursor down to next priority column (manual mode only).
        /// Not used in basic mode.
        /// </summary>
        public static void MoveDown()
        {
            if (!IsManualMode)
            {
                TolkHelper.Speak("Up/Down navigation only in manual mode. Press Alt+M to switch.");
                return;
            }

            // Navigate to next (lower) priority column
            if (currentColumn < 4)
            {
                currentColumn++;
                ClampRowToColumn();
                AnnounceCurrentPosition(true);
            }
            else
            {
                TolkHelper.Speak("At disabled column");
            }
        }

        /// <summary>
        /// Jumps to the first item in the current column (manual) or list (basic).
        /// </summary>
        public static void JumpToFirst()
        {
            if (IsManualMode)
            {
                var col = columns[currentColumn];
                if (col.Count == 0)
                {
                    TolkHelper.Speak(GetColumnName(currentColumn) + ": empty");
                    return;
                }

                if (currentRow == 0)
                {
                    TolkHelper.Speak("Already at leftmost task");
                    return;
                }

                currentRow = 0;
                AnnounceCurrentPosition(false);
            }
            else
            {
                if (allEntries.Count == 0) return;

                if (basicModeIndex == 0)
                {
                    TolkHelper.Speak("Already at leftmost task");
                    return;
                }

                basicModeIndex = 0;
                AnnounceCurrentPosition(false);
            }
        }

        /// <summary>
        /// Jumps to the last item in the current column (manual) or list (basic).
        /// </summary>
        public static void JumpToLast()
        {
            if (IsManualMode)
            {
                var col = columns[currentColumn];
                if (col.Count == 0)
                {
                    TolkHelper.Speak(GetColumnName(currentColumn) + ": empty");
                    return;
                }

                if (currentRow == col.Count - 1)
                {
                    TolkHelper.Speak("Already at rightmost task");
                    return;
                }

                currentRow = col.Count - 1;
                AnnounceCurrentPosition(false);
            }
            else
            {
                if (allEntries.Count == 0) return;

                if (basicModeIndex == allEntries.Count - 1)
                {
                    TolkHelper.Speak("Already at rightmost task");
                    return;
                }

                basicModeIndex = allEntries.Count - 1;
                AnnounceCurrentPosition(false);
            }
        }

        /// <summary>
        /// Moves cursor left within current column (manual) or list (basic).
        /// Navigates to earlier task in execution order.
        /// </summary>
        public static void MoveLeft()
        {
            if (IsManualMode)
            {
                var col = columns[currentColumn];
                if (col.Count == 0)
                {
                    TolkHelper.Speak(GetColumnName(currentColumn) + ": empty");
                    return;
                }

                if (currentRow > 0)
                {
                    currentRow--;
                    AnnounceCurrentPosition(false);
                }
                else
                {
                    TolkHelper.Speak("At leftmost task");
                }
            }
            else
            {
                // Basic mode - navigate task list
                if (allEntries.Count == 0) return;

                if (basicModeIndex > 0)
                {
                    basicModeIndex--;
                    AnnounceCurrentPosition(false);
                }
                else
                {
                    TolkHelper.Speak("At leftmost task");
                }
            }
        }

        /// <summary>
        /// Moves cursor right within current column (manual) or list (basic).
        /// Navigates to later task in execution order.
        /// </summary>
        public static void MoveRight()
        {
            if (IsManualMode)
            {
                var col = columns[currentColumn];
                if (col.Count == 0)
                {
                    TolkHelper.Speak(GetColumnName(currentColumn) + ": empty");
                    return;
                }

                if (currentRow < col.Count - 1)
                {
                    currentRow++;
                    AnnounceCurrentPosition(false);
                }
                else
                {
                    TolkHelper.Speak("At rightmost task");
                }
            }
            else
            {
                // Basic mode - navigate task list
                if (allEntries.Count == 0) return;

                if (basicModeIndex < allEntries.Count - 1)
                {
                    basicModeIndex++;
                    AnnounceCurrentPosition(false);
                }
                else
                {
                    TolkHelper.Speak("At rightmost task");
                }
            }
        }

        /// <summary>
        /// Clamps current row to valid range for current column.
        /// </summary>
        private static void ClampRowToColumn()
        {
            var col = columns[currentColumn];
            if (col.Count == 0)
            {
                currentRow = 0;
            }
            else if (currentRow >= col.Count)
            {
                currentRow = col.Count - 1;
            }
        }

        /// <summary>
        /// Switches to next pawn, saving current changes.
        /// </summary>
        public static void SwitchToNextPawn()
        {
            if (allPawns.Count == 0) return;

            int newIndex = (currentPawnIndex + 1) % allPawns.Count;
            SaveAndSwitchPawn(newIndex);
        }

        /// <summary>
        /// Switches to previous pawn, saving current changes.
        /// </summary>
        public static void SwitchToPreviousPawn()
        {
            if (allPawns.Count == 0) return;

            int newIndex = (currentPawnIndex - 1 + allPawns.Count) % allPawns.Count;
            SaveAndSwitchPawn(newIndex);
        }

        #endregion

        #region Task Operations

        /// <summary>
        /// Sets priority for current task (manual mode) or toggles (basic mode).
        /// In manual mode: moves task to specified priority column.
        /// </summary>
        public static void SetPriority(int priority)
        {
            if (priority < 0 || priority > 4) return;

            WorkTypeEntry entry = GetCurrentEntry();
            if (entry == null) return;

            if (entry.IsPermanentlyDisabled)
            {
                AnnounceCannotEnable(entry);
                return;
            }

            if (IsManualMode)
            {
                int newColumnIndex = PriorityToColumnIndex(priority);

                if (newColumnIndex == currentColumn)
                {
                    // Already in this column
                    TolkHelper.Speak($"{entry.WorkType.labelShort} already at {GetColumnName(newColumnIndex)}");
                    return;
                }

                // Remove from current column
                var oldColumn = columns[currentColumn];
                oldColumn.Remove(entry);

                // Update priority
                entry.CurrentPriority = priority;
                currentPawn.workSettings.SetPriority(entry.WorkType, priority);
                hasUnsavedChanges = true;

                // Add to new column in correct naturalPriority order
                var newColumn = columns[newColumnIndex];
                int insertIndex = FindInsertionIndex(newColumn, entry);
                newColumn.Insert(insertIndex, entry);

                // Announce the move with placement context (except for disabled)
                if (newColumnIndex == 4) // Disabled
                {
                    TolkHelper.Speak($"{entry.WorkType.labelShort} disabled");
                }
                else
                {
                    string placementContext = GetPlacementContext(newColumn, insertIndex);
                    if (string.IsNullOrEmpty(placementContext))
                    {
                        TolkHelper.Speak($"{entry.WorkType.labelShort} set to {GetColumnName(newColumnIndex)}");
                    }
                    else
                    {
                        TolkHelper.Speak($"{entry.WorkType.labelShort} set to {GetColumnName(newColumnIndex)}, {placementContext}");
                    }
                }

                if (oldColumn.Count == 0)
                {
                    TolkHelper.Speak($"Your cursor is now at {GetColumnName(currentColumn)}, which is empty");
                }
                else
                {
                    // Stay in current column, move to next item (or clamp to last)
                    if (currentRow >= oldColumn.Count)
                        currentRow = oldColumn.Count - 1;
                    AnnounceCursorMovedTo();
                }
            }
            else
            {
                // Basic mode: just set the priority directly
                entry.CurrentPriority = priority;
                currentPawn.workSettings.SetPriority(entry.WorkType, priority);
                hasUnsavedChanges = true;
                AnnounceCurrentPosition(false);
            }
        }

        /// <summary>
        /// Toggles current task between enabled (priority 3) and disabled (priority 0).
        /// </summary>
        public static void ToggleSelected()
        {
            WorkTypeEntry entry = GetCurrentEntry();
            if (entry == null) return;

            if (entry.IsPermanentlyDisabled)
            {
                AnnounceCannotEnable(entry);
                return;
            }

            int newPriority = (entry.CurrentPriority == 0) ? 3 : 0;
            SetPriority(newPriority);
        }

        /// <summary>
        /// Toggles between basic and manual priority modes.
        /// </summary>
        public static void ToggleMode()
        {
            Find.PlaySettings.useWorkPriorities = !Find.PlaySettings.useWorkPriorities;

            // Rebuild columns since priorities may have changed meaning
            LoadWorkTypesForCurrentPawn();

            if (IsManualMode)
            {
                // Find column containing the previously selected entry
                FindFirstPopulatedColumn();
                string mode = "Manual priority mode";
                TolkHelper.Speak(mode);
            }
            else
            {
                basicModeIndex = 0;
                string mode = "Basic mode";
                TolkHelper.Speak(mode);
            }

            AnnounceCurrentPosition(true);
        }

        #endregion

        #region Type-ahead Search

        /// <summary>
        /// Processes a character input for type-ahead search.
        /// Searches ALL columns/entries regardless of current position.
        /// </summary>
        public static bool ProcessSearchCharacter(char c)
        {
            var labels = allEntries.Select(e => e.WorkType.labelShort).ToList();

            if (typeahead.ProcessCharacterInput(c, labels, out int matchIndex))
            {
                if (matchIndex >= 0)
                {
                    // Find which column and row this entry is in
                    var entry = allEntries[matchIndex];
                    FindEntryPosition(entry, out int col, out int row);

                    searchJumpPending = true;
                    searchTargetColumn = col;
                    searchTargetRow = row;

                    string colName = GetColumnName(col);
                    string taskAnnouncement = BuildTaskAnnouncement(entry, false);

                    TolkHelper.Speak($"{taskAnnouncement}, {colName}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount}, press Enter to jump to this task");
                }
                return true;
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
                return false;
            }
        }

        /// <summary>
        /// Navigates to next search match.
        /// </summary>
        public static void NextSearchMatch()
        {
            if (!typeahead.HasActiveSearch || typeahead.HasNoMatches) return;

            var labels = allEntries.Select(e => e.WorkType.labelShort).ToList();
            int currentIndex = GetCurrentEntryIndex();
            int matchIndex = typeahead.GetNextMatch(currentIndex);

            if (matchIndex >= 0)
            {
                var entry = allEntries[matchIndex];
                FindEntryPosition(entry, out int col, out int row);

                searchJumpPending = true;
                searchTargetColumn = col;
                searchTargetRow = row;

                string colName = GetColumnName(col);
                string taskAnnouncement = BuildTaskAnnouncement(entry, false);

                TolkHelper.Speak($"{taskAnnouncement}, {colName}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount}, press Enter to jump to this task");
            }
        }

        /// <summary>
        /// Navigates to previous search match.
        /// </summary>
        public static void PreviousSearchMatch()
        {
            if (!typeahead.HasActiveSearch || typeahead.HasNoMatches) return;

            int currentIndex = GetCurrentEntryIndex();
            int matchIndex = typeahead.GetPreviousMatch(currentIndex);

            if (matchIndex >= 0)
            {
                var entry = allEntries[matchIndex];
                FindEntryPosition(entry, out int col, out int row);

                searchJumpPending = true;
                searchTargetColumn = col;
                searchTargetRow = row;

                string colName = GetColumnName(col);
                string taskAnnouncement = BuildTaskAnnouncement(entry, false);

                TolkHelper.Speak($"{taskAnnouncement}, {colName}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount}, press Enter to jump to this task");
            }
        }

        /// <summary>
        /// Jumps cursor to the search result position.
        /// </summary>
        public static void JumpToSearchResult()
        {
            if (!searchJumpPending) return;

            if (IsManualMode)
            {
                currentColumn = searchTargetColumn;
                currentRow = searchTargetRow;
            }
            else
            {
                // Find the entry and set basicModeIndex
                var entry = columns[searchTargetColumn][searchTargetRow];
                basicModeIndex = allEntries.IndexOf(entry);
            }

            searchJumpPending = false;
            typeahead.ClearSearch();
            AnnounceCurrentPosition(true);
        }

        /// <summary>
        /// Handles backspace in search.
        /// </summary>
        public static bool ProcessBackspace()
        {
            if (!typeahead.HasActiveSearch) return false;

            var labels = allEntries.Select(e => e.WorkType.labelShort).ToList();
            if (typeahead.ProcessBackspace(labels, out int matchIndex))
            {
                if (typeahead.HasActiveSearch && matchIndex >= 0)
                {
                    var entry = allEntries[matchIndex];
                    FindEntryPosition(entry, out int col, out int row);

                    searchJumpPending = true;
                    searchTargetColumn = col;
                    searchTargetRow = row;

                    string colName = GetColumnName(col);
                    string taskAnnouncement = BuildTaskAnnouncement(entry, false);

                    TolkHelper.Speak($"{taskAnnouncement}, {colName}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount}, press Enter to jump to this task");
                }
                else if (!typeahead.HasActiveSearch)
                {
                    searchJumpPending = false;
                    AnnounceCurrentPosition(true);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Clears search and announces if search was active.
        /// </summary>
        public static bool ClearSearchIfActive()
        {
            if (typeahead.HasActiveSearch)
            {
                typeahead.ClearSearchAndAnnounce();
                searchJumpPending = false;
                AnnounceCurrentPosition(true);
                return true;
            }
            return false;
        }

        private static void FindEntryPosition(WorkTypeEntry entry, out int column, out int row)
        {
            for (int c = 0; c < 5; c++)
            {
                int r = columns[c].IndexOf(entry);
                if (r >= 0)
                {
                    column = c;
                    row = r;
                    return;
                }
            }
            column = 0;
            row = 0;
        }

        private static int GetCurrentEntryIndex()
        {
            var entry = GetCurrentEntry();
            return entry != null ? allEntries.IndexOf(entry) : 0;
        }

        #endregion

        #region Announcements

        /// <summary>
        /// Announces that the cursor has moved to a new position after an item was moved away.
        /// </summary>
        private static void AnnounceCursorMovedTo()
        {
            WorkTypeEntry entry = GetCurrentEntry();
            if (entry == null) return;

            if (IsManualMode)
            {
                var col = columns[currentColumn];
                string taskAnnouncement = BuildTaskAnnouncement(entry, true);
                string position = MenuHelper.FormatPosition(currentRow, col.Count);
                TolkHelper.Speak($"Your cursor is now at: {taskAnnouncement} {position}");
            }
            else
            {
                string taskAnnouncement = BuildTaskAnnouncement(entry, true);
                string position = MenuHelper.FormatPosition(basicModeIndex, allEntries.Count);
                string status = entry.CurrentPriority > 0 ? "enabled" : "disabled";
                TolkHelper.Speak($"Your cursor is now at: {taskAnnouncement} {status}, {position}");
            }
        }

        /// <summary>
        /// Announces the current position with full context.
        /// </summary>
        /// <param name="includeColumnName">Whether to include column name (for column changes)</param>
        private static void AnnounceCurrentPosition(bool includeColumnName)
        {
            WorkTypeEntry entry = GetCurrentEntry();

            if (IsManualMode)
            {
                var col = columns[currentColumn];
                if (col.Count == 0)
                {
                    TolkHelper.Speak($"{GetColumnName(currentColumn)}: empty");
                    return;
                }

                string taskAnnouncement = BuildTaskAnnouncement(entry, true);
                string position = MenuHelper.FormatPosition(currentRow, col.Count);

                if (includeColumnName)
                {
                    TolkHelper.Speak($"{GetColumnName(currentColumn)}, {taskAnnouncement} {position}");
                }
                else
                {
                    TolkHelper.Speak($"{taskAnnouncement} {position}");
                }
            }
            else
            {
                // Basic mode
                if (allEntries.Count == 0)
                {
                    TolkHelper.Speak("No work types available");
                    return;
                }

                string taskAnnouncement = BuildTaskAnnouncement(entry, true);
                string position = MenuHelper.FormatPosition(basicModeIndex, allEntries.Count);
                string status = entry.CurrentPriority > 0 ? "enabled" : "disabled";

                TolkHelper.Speak($"{taskAnnouncement} {status}, {position}");
            }
        }

        /// <summary>
        /// Builds the task announcement string with skills and passions.
        /// </summary>
        private static string BuildTaskAnnouncement(WorkTypeEntry entry, bool includeFullDetails)
        {
            if (entry == null) return "No task selected";

            var workType = entry.WorkType;
            var sb = new StringBuilder();

            sb.Append(workType.labelShort);

            if (entry.IsPermanentlyDisabled)
            {
                sb.Append(". Permanently disabled: ");
                var reasons = currentPawn.GetReasonsForDisabledWorkType(workType);
                sb.Append(string.Join(", ", reasons.Select(r => r.ToString())));
                return sb.ToString();
            }

            if (!includeFullDetails)
            {
                return sb.ToString();
            }

            // Get relevant skills
            var relevantSkills = workType.relevantSkills;

            if (relevantSkills == null || relevantSkills.Count == 0)
            {
                sb.Append(". No relevant skills or passions.");
                return sb.ToString();
            }

            // Check if skill names are redundant with task name
            bool skillNamesRedundant = relevantSkills.Count == 1 &&
                string.Equals(relevantSkills[0].skillLabel, workType.labelShort, StringComparison.OrdinalIgnoreCase);

            if (!skillNamesRedundant && relevantSkills.Count > 0)
            {
                sb.Append(". Uses ");
                if (relevantSkills.Count == 1)
                {
                    sb.Append(relevantSkills[0].skillLabel);
                }
                else
                {
                    for (int i = 0; i < relevantSkills.Count; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append(i == relevantSkills.Count - 1 ? " and " : ", ");
                        }
                        sb.Append(relevantSkills[i].skillLabel);
                    }
                }
            }

            // Get skill level (average of relevant skills)
            float avgSkill = currentPawn.skills.AverageOfRelevantSkillsFor(workType);
            int skillLevel = Math.Min(20, Math.Max(0, (int)Math.Round(avgSkill)));
            string descriptor = $"Skill{skillLevel}".Translate();

            sb.Append($". Skill level: {skillLevel}, {descriptor}");

            // Get passion
            Passion passion = currentPawn.skills.MaxPassionOfRelevantSkillsFor(workType);
            string passionText;
            switch (passion)
            {
                case Passion.Major:
                    passionText = "Burning passion";
                    break;
                case Passion.Minor:
                    passionText = "Passion";
                    break;
                default:
                    passionText = "No passion";
                    break;
            }
            sb.Append($". {passionText}.");

            return sb.ToString();
        }

        private static void AnnounceCannotEnable(WorkTypeEntry entry)
        {
            var reasons = currentPawn.GetReasonsForDisabledWorkType(entry.WorkType);
            string reasonText = string.Join(", ", reasons.Select(r => r.ToString()));
            TolkHelper.Speak($"Cannot enable - permanently disabled due to: {reasonText}", SpeechPriority.High);
        }

        private static string GetColumnName(int columnIndex)
        {
            switch (columnIndex)
            {
                case 0: return "Priority 1";
                case 1: return "Priority 2";
                case 2: return "Priority 3";
                case 3: return "Priority 4";
                case 4: return "Disabled";
                default: return "Unknown";
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Gets a placement context string describing where an item was placed in a column.
        /// </summary>
        private static string GetPlacementContext(List<WorkTypeEntry> column, int insertedIndex)
        {
            // Count non-permanently-disabled items for context
            int movableCount = column.Count(e => !e.IsPermanentlyDisabled);

            // If alone, no context needed
            if (movableCount <= 1)
            {
                return "";
            }

            // Find the boundaries of movable items
            int lastMovableIndex = column.FindIndex(e => e.IsPermanentlyDisabled) - 1;
            if (lastMovableIndex < 0) lastMovableIndex = column.Count - 1;

            // If inserted item is permanently disabled, no placement context
            if (insertedIndex > lastMovableIndex)
            {
                return "";
            }

            bool atLeftEdge = insertedIndex == 0;
            bool atRightEdge = insertedIndex == lastMovableIndex;

            if (movableCount == 2)
            {
                // Two items - say "placed left of X" or "placed right of X"
                if (atLeftEdge)
                {
                    string rightNeighbor = column[1].WorkType.labelShort;
                    return $"placed left of {rightNeighbor}";
                }
                else
                {
                    string leftNeighbor = column[0].WorkType.labelShort;
                    return $"placed right of {leftNeighbor}";
                }
            }
            else
            {
                // Three or more items
                if (atLeftEdge)
                {
                    string rightNeighbor = column[1].WorkType.labelShort;
                    return $"placed first, left of {rightNeighbor}";
                }
                else if (atRightEdge)
                {
                    string leftNeighbor = column[insertedIndex - 1].WorkType.labelShort;
                    return $"placed last, right of {leftNeighbor}";
                }
                else
                {
                    // In the middle - between left and right neighbors
                    string leftNeighbor = column[insertedIndex - 1].WorkType.labelShort;
                    string rightNeighbor = column[insertedIndex + 1].WorkType.labelShort;
                    return $"placed between {leftNeighbor} and {rightNeighbor}";
                }
            }
        }

        /// <summary>
        /// Finds the correct insertion index for an entry in a column based on naturalPriority.
        /// Maintains descending naturalPriority order, with permanently disabled at the end.
        /// </summary>
        private static int FindInsertionIndex(List<WorkTypeEntry> column, WorkTypeEntry entry)
        {
            int entryNaturalPriority = entry.WorkType.naturalPriority;
            bool entryIsPermanentlyDisabled = entry.IsPermanentlyDisabled;

            for (int i = 0; i < column.Count; i++)
            {
                var existing = column[i];

                // Permanently disabled entries go at the end
                if (!entryIsPermanentlyDisabled && existing.IsPermanentlyDisabled)
                {
                    return i;
                }

                // Within same disabled status, sort by naturalPriority descending
                if (entryIsPermanentlyDisabled == existing.IsPermanentlyDisabled)
                {
                    if (entryNaturalPriority > existing.WorkType.naturalPriority)
                    {
                        return i;
                    }
                }
            }

            return column.Count;
        }

        /// <summary>
        /// Gets the currently selected work type entry.
        /// </summary>
        public static WorkTypeEntry GetCurrentEntry()
        {
            if (IsManualMode)
            {
                var col = columns[currentColumn];
                if (col.Count == 0 || currentRow < 0 || currentRow >= col.Count)
                    return null;
                return col[currentRow];
            }
            else
            {
                if (allEntries.Count == 0 || basicModeIndex < 0 || basicModeIndex >= allEntries.Count)
                    return null;
                return allEntries[basicModeIndex];
            }
        }

        /// <summary>
        /// Gets all entries as a flat list (for overlay display).
        /// </summary>
        public static List<WorkTypeEntry> GetAllEntries() => allEntries;

        /// <summary>
        /// Gets the columns (for overlay display).
        /// </summary>
        public static List<List<WorkTypeEntry>> GetColumns() => columns;

        #endregion

        /// <summary>
        /// Represents a work type entry in the menu.
        /// </summary>
        public class WorkTypeEntry
        {
            public WorkTypeDef WorkType { get; set; }
            public bool IsPermanentlyDisabled { get; set; }
            public int CurrentPriority { get; set; }
        }
    }
}
