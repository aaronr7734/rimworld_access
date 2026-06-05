using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Verse.Sound;
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
                allPawns = PlayerPawnsDisplayOrderUtility.InOrder(
                        Find.CurrentMap.mapPawns.FreeColonists
                            .Where(p => !p.DevelopmentalStage.Baby()))
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

            TolkHelper.Speak("RimWorldAccess.Work.OpeningTitle".Loc());
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
            TolkHelper.Speak("RimWorldAccess.Work.Cancelled".Loc());
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

            // Changes are already applied in real-time, just need to finalize
            // Force refresh of work givers cache
            currentPawn.workSettings.Notify_UseWorkPrioritiesChanged();

            // Refresh all pawns if natural priorities changed
            RefreshAllPawnsWorkGivers();

            CleanupState();
            TolkHelper.Speak("RimWorldAccess.Work.Saved".Loc());
        }

        /// <summary>
        /// Saves changes for current pawn and switches to another pawn.
        /// </summary>
        private static void SaveAndSwitchPawn(int newPawnIndex)
        {
            string previousPawnName = currentPawn?.LabelShort ?? "RimWorldAccess.Work.UnknownPawn".Translate().ToString();
            bool hadChanges = hasUnsavedChanges;

            if (currentPawn != null && currentPawn.workSettings != null)
            {
                // Changes are applied in real-time, just refresh
                currentPawn.workSettings.Notify_UseWorkPrioritiesChanged();
            }

            // Preserve the current WorkTypeDef to follow it across pawn switches
            WorkTypeDef preservedWorkType = GetCurrentEntry()?.WorkType;

            currentPawnIndex = newPawnIndex;
            currentPawn = allPawns[currentPawnIndex];
            typeahead.ClearSearch();
            searchJumpPending = false;
            hasUnsavedChanges = false;

            LoadWorkTypesForCurrentPawn();

            // Restore cursor to the same work type on the new pawn
            if (preservedWorkType != null)
            {
                if (IsManualMode)
                {
                    if (FindWorkTypePosition(preservedWorkType, out int col, out int row))
                    {
                        currentColumn = col;
                        currentRow = row;
                    }
                    else
                    {
                        FindFirstPopulatedColumn();
                    }
                }
                else
                {
                    int idx = allEntries.FindIndex(e => e.WorkType == preservedWorkType);
                    basicModeIndex = idx >= 0 ? idx : 0;
                }
            }
            else
            {
                if (IsManualMode)
                {
                    FindFirstPopulatedColumn();
                }
                else
                {
                    basicModeIndex = 0;
                }
            }

            // Brief announcement for rapid pawn cycling
            string briefAnnouncement = BuildPawnSwitchAnnouncement();
            if (hadChanges)
            {
                TolkHelper.Speak("RimWorldAccess.Work.PawnSwitch.SavedPrefix".Loc(previousPawnName, briefAnnouncement));
            }
            else
            {
                TolkHelper.SpeakData(briefAnnouncement);
            }
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
                TolkHelper.Speak("RimWorldAccess.Work.UpDownOnlyManual".Loc());
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
                AnnounceCurrentPosition(false);
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
                TolkHelper.Speak("RimWorldAccess.Work.UpDownOnlyManual".Loc());
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
                AnnounceCurrentPosition(false);
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
                    TolkHelper.Speak("RimWorldAccess.Work.ColumnEmpty".Loc(GetColumnName(currentColumn)));
                    return;
                }

                if (currentRow == 0)
                {
                    AnnounceCurrentPosition(false);
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
                    AnnounceCurrentPosition(false);
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
                    TolkHelper.Speak("RimWorldAccess.Work.ColumnEmpty".Loc(GetColumnName(currentColumn)));
                    return;
                }

                if (currentRow == col.Count - 1)
                {
                    AnnounceCurrentPosition(false);
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
                    AnnounceCurrentPosition(false);
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
                    TolkHelper.Speak("RimWorldAccess.Work.ColumnEmpty".Loc(GetColumnName(currentColumn)));
                    return;
                }

                if (currentRow > 0)
                {
                    currentRow--;
                    AnnounceCurrentPosition(false);
                }
                else
                {
                    AnnounceCurrentPosition(false);
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
                    AnnounceCurrentPosition(false);
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
                    TolkHelper.Speak("RimWorldAccess.Work.ColumnEmpty".Loc(GetColumnName(currentColumn)));
                    return;
                }

                if (currentRow < col.Count - 1)
                {
                    currentRow++;
                    AnnounceCurrentPosition(false);
                }
                else
                {
                    AnnounceCurrentPosition(false);
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
                    AnnounceCurrentPosition(false);
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

            int newIndex = MenuHelper.SelectNext(currentPawnIndex, allPawns.Count);
            if (newIndex != currentPawnIndex)
                SaveAndSwitchPawn(newIndex);
        }

        /// <summary>
        /// Switches to previous pawn, saving current changes.
        /// </summary>
        public static void SwitchToPreviousPawn()
        {
            if (allPawns.Count == 0) return;

            int newIndex = MenuHelper.SelectPrevious(currentPawnIndex, allPawns.Count);
            if (newIndex != currentPawnIndex)
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

            bool wasActive = currentPawn.workSettings.WorkIsActive(entry.WorkType);

            if (IsManualMode)
            {
                int newColumnIndex = PriorityToColumnIndex(priority);

                if (newColumnIndex == currentColumn)
                {
                    // Already in this column
                    TolkHelper.Speak("RimWorldAccess.Work.SetPriority.AlreadyAtPriority".Loc(entry.WorkType.labelShort, PriorityWord(newColumnIndex)));
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

                SoundDefOf.DragSlider.PlayOneShotOnCamera();
                PlayActivationSounds(currentPawn, entry.WorkType, wasActive);

                // Announce the move with placement context (except for disabled)
                if (newColumnIndex == 4) // Disabled
                {
                    TolkHelper.Speak("RimWorldAccess.Work.SetPriority.MovedToDisabled".Loc(entry.WorkType.labelShort));
                }
                else
                {
                    string placementContext = GetPlacementContext(newColumn, insertIndex);
                    string trimmed = StripPlacedPrefix(placementContext);
                    if (string.IsNullOrEmpty(trimmed))
                    {
                        TolkHelper.Speak("RimWorldAccess.Work.SetPriority.MovedToPriority".Loc(entry.WorkType.labelShort, PriorityWord(newColumnIndex)));
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Work.SetPriority.MovedToPriorityWithContext".Loc(entry.WorkType.labelShort, PriorityWord(newColumnIndex), trimmed));
                    }
                }

                if (oldColumn.Count == 0)
                {
                    TolkHelper.Speak("RimWorldAccess.Work.CursorNowEmpty".Loc(GetColumnName(currentColumn)));
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

                bool nowActive = currentPawn.workSettings.WorkIsActive(entry.WorkType);
                if (!wasActive && nowActive)
                    SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                else if (wasActive && !nowActive)
                    SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                PlayActivationSounds(currentPawn, entry.WorkType, wasActive);

                AnnounceCurrentPosition(false);
            }
        }

        /// <summary>
        /// Plays vanilla warning sounds when work transitions from inactive to active:
        /// Crunch for low-skill pawns, DislikedWorkTypeActivated for ideo-opposed work.
        /// Mirrors WidgetsWork.DrawWorkBoxFor.
        /// </summary>
        private static void PlayActivationSounds(Pawn pawn, WorkTypeDef workType, bool wasActive)
        {
            if (wasActive) return;
            if (!pawn.workSettings.WorkIsActive(workType)) return;

            if (workType.relevantSkills.Any() && pawn.skills.AverageOfRelevantSkillsFor(workType) <= 2f)
            {
                SoundDefOf.Crunch.PlayOneShotOnCamera();
            }
            if (pawn.Ideo != null && pawn.Ideo.IsWorkTypeConsideredDangerous(workType))
            {
                Messages.Message("MessageIdeoOpposedWorkTypeSelected".Translate(pawn, workType.gerundLabel), pawn, MessageTypeDefOf.CautionInput, historical: false);
                SoundDefOf.DislikedWorkTypeActivated.PlayOneShotOnCamera();
            }
        }

        /// <summary>
        /// Returns the bare priority word for announcements: "1".."4" or "disabled".
        /// </summary>
        private static string PriorityWord(int columnIndex)
        {
            switch (columnIndex)
            {
                case 0: return "RimWorldAccess.Work.PriorityWord.1".Translate();
                case 1: return "RimWorldAccess.Work.PriorityWord.2".Translate();
                case 2: return "RimWorldAccess.Work.PriorityWord.3".Translate();
                case 3: return "RimWorldAccess.Work.PriorityWord.4".Translate();
                case 4: return "RimWorldAccess.Work.PriorityWord.Disabled".Translate();
                default: return "";
            }
        }

        /// <summary>
        /// Strips the leading "placed " from a placement context string.
        /// </summary>
        private static string StripPlacedPrefix(string placement)
        {
            if (string.IsNullOrEmpty(placement)) return placement;
            string prefix = "RimWorldAccess.Work.Placement.PlacedPrefix".Translate();
            if (placement.StartsWith(prefix)) return placement.Substring(prefix.Length);
            return placement;
        }

        /// <summary>
        /// Sets the priority for the current work type across ALL eligible pawns.
        /// Mirrors vanilla's shift-click header behavior from PawnColumnWorker_WorkPriority.
        /// Manual mode only. Cursor stays in original column (same as single-pawn SetPriority).
        /// </summary>
        public static void SetPriorityForAllPawns(int priority)
        {
            if (priority < 0 || priority > 4) return;
            if (!IsManualMode) return;

            WorkTypeEntry entry = GetCurrentEntry();
            if (entry == null)
            {
                TolkHelper.Speak("RimWorldAccess.Work.NoWorkTypeSelected".Loc());
                return;
            }

            if (entry.IsPermanentlyDisabled)
            {
                AnnounceCannotEnable(entry);
                return;
            }

            WorkTypeDef workType = entry.WorkType;
            string jobName = workType.labelShort;

            // Track results for announcement
            var changedNames = new List<string>();
            var alreadySetNames = new List<string>();
            var incapableNames = new List<string>();
            bool anyLowSkillActivated = false;
            bool anyIdeoOpposedActivated = false;
            var ideoOpposedPawns = new List<Pawn>();

            foreach (Pawn pawn in allPawns)
            {
                // Eligibility check matching vanilla's HeaderClicked logic
                if (pawn.workSettings == null || !pawn.workSettings.EverWork || pawn.WorkTypeIsDisabled(workType))
                {
                    incapableNames.Add(pawn.LabelShort);
                    continue;
                }

                int currentPriority = pawn.workSettings.GetPriority(workType);
                if (currentPriority == priority)
                {
                    alreadySetNames.Add(pawn.LabelShort);
                    continue;
                }

                bool wasActive = pawn.workSettings.WorkIsActive(workType);
                pawn.workSettings.SetPriority(workType, priority);
                changedNames.Add(pawn.LabelShort);

                if (!wasActive && pawn.workSettings.WorkIsActive(workType))
                {
                    if (workType.relevantSkills.Any() && pawn.skills.AverageOfRelevantSkillsFor(workType) <= 2f)
                        anyLowSkillActivated = true;
                    if (pawn.Ideo != null && pawn.Ideo.IsWorkTypeConsideredDangerous(workType))
                    {
                        anyIdeoOpposedActivated = true;
                        ideoOpposedPawns.Add(pawn);
                    }
                }
            }

            if (changedNames.Count > 0)
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
            if (anyLowSkillActivated)
                SoundDefOf.Crunch.PlayOneShotOnCamera();
            if (anyIdeoOpposedActivated)
            {
                SoundDefOf.DislikedWorkTypeActivated.PlayOneShotOnCamera();
                foreach (var p in ideoOpposedPawns)
                    Messages.Message("MessageIdeoOpposedWorkTypeSelected".Translate(p, workType.gerundLabel), p, MessageTypeDefOf.CautionInput, historical: false);
            }

            // Update the current pawn's internal column structure if their priority changed
            bool currentPawnChanged = changedNames.Contains(currentPawn.LabelShort);
            if (currentPawnChanged)
            {
                int newColumnIndex = PriorityToColumnIndex(priority);

                // Remove from current column
                var oldColumn = columns[currentColumn];
                oldColumn.Remove(entry);

                // Update entry's tracked priority
                entry.CurrentPriority = priority;

                // Add to new column in correct naturalPriority order
                var newColumn = columns[newColumnIndex];
                int insertIndex = FindInsertionIndex(newColumn, entry);
                newColumn.Insert(insertIndex, entry);

                hasUnsavedChanges = true;
            }

            // Speak bulk announcement first
            string announcement = BuildBulkSetAnnouncement(jobName, priority, changedNames, alreadySetNames, incapableNames);
            TolkHelper.SpeakData(announcement);

            // Then handle cursor position (stays in original column, same as single-pawn SetPriority)
            if (currentPawnChanged)
            {
                var oldColumn = columns[currentColumn];
                if (oldColumn.Count == 0)
                {
                    TolkHelper.Speak("RimWorldAccess.Work.CursorNowEmpty".Loc(GetColumnName(currentColumn)));
                }
                else
                {
                    if (currentRow >= oldColumn.Count)
                        currentRow = oldColumn.Count - 1;
                    AnnounceCursorMovedTo();
                }
            }
        }

        /// <summary>
        /// Builds the announcement string for a bulk priority set operation.
        /// Treats "already at target priority" as success. Only incapable pawns are exceptions.
        /// </summary>
        private static string BuildBulkSetAnnouncement(
            string jobName,
            int priority,
            List<string> changedNames,
            List<string> alreadySetNames,
            List<string> incapableNames)
        {
            int totalPawns = allPawns.Count;
            string priorityLabel = priority == 0
                ? "RimWorldAccess.Work.PriorityWord.Disabled".Translate().ToString()
                : "RimWorldAccess.Work.PriorityLabel".Translate(priority).ToString();
            int successCount = changedNames.Count + alreadySetNames.Count;

            // Nobody can do this job
            if (successCount == 0)
            {
                return "RimWorldAccess.Work.BulkSet.NoCapableColonists".Translate(jobName);
            }

            // Everyone was already at this priority
            if (changedNames.Count == 0)
            {
                if (incapableNames.Count == 0)
                {
                    return "RimWorldAccess.Work.BulkSet.AlreadyForAll".Translate(jobName, priorityLabel);
                }
                return "RimWorldAccess.Work.BulkSet.AlreadyForAllCapable".Translate(jobName, priorityLabel);
            }

            // All capable colonists were changed or already set (no incapable exceptions)
            if (incapableNames.Count == 0)
            {
                return "RimWorldAccess.Work.BulkSet.SetForAll".Translate(jobName, priorityLabel);
            }

            // Some incapable exceptions exist
            if (incapableNames.Count <= 5 && incapableNames.Count <= totalPawns / 2)
            {
                string exceptList = FormatNameList(incapableNames);
                return "RimWorldAccess.Work.BulkSet.SetForAllExcept".Translate(jobName, priorityLabel, exceptList);
            }
            else
            {
                // Many incapable - list who was changed
                string changedList = FormatNameList(changedNames);
                if (alreadySetNames.Count > 0)
                {
                    string key = alreadySetNames.Count == 1
                        ? "RimWorldAccess.Work.BulkSet.SetForListWithAlreadySetOne"
                        : "RimWorldAccess.Work.BulkSet.SetForListWithAlreadySetMany";
                    return key.Translate(jobName, priorityLabel, changedList, alreadySetNames.Count);
                }
                return "RimWorldAccess.Work.BulkSet.SetForList".Translate(jobName, priorityLabel, changedList);
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
                TolkHelper.Speak("RimWorldAccess.Work.Mode.Manual".Loc());
            }
            else
            {
                basicModeIndex = 0;
                TolkHelper.Speak("RimWorldAccess.Work.Mode.Basic".Loc());
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

                    TolkHelper.Speak("RimWorldAccess.Work.Search.JumpPrompt".Loc(taskAnnouncement, colName, typeahead.CurrentMatchPosition, typeahead.MatchCount));
                }
                return true;
            }
            else
            {
                typeahead.SpeakNoMatches();
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

                TolkHelper.Speak("RimWorldAccess.Work.Search.JumpPrompt".Loc(taskAnnouncement, colName, typeahead.CurrentMatchPosition, typeahead.MatchCount));
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

                TolkHelper.Speak("RimWorldAccess.Work.Search.JumpPrompt".Loc(taskAnnouncement, colName, typeahead.CurrentMatchPosition, typeahead.MatchCount));
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

                    TolkHelper.Speak("RimWorldAccess.Work.Search.JumpPrompt".Loc(taskAnnouncement, colName, typeahead.CurrentMatchPosition, typeahead.MatchCount));
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

        /// <summary>
        /// Finds the column and row position of a WorkTypeDef in the current columns.
        /// Unlike FindEntryPosition which matches by object reference, this matches by
        /// WorkTypeDef identity. Needed because WorkTypeEntry objects are recreated per
        /// pawn by LoadWorkTypesForCurrentPawn().
        /// </summary>
        private static bool FindWorkTypePosition(WorkTypeDef workType, out int column, out int row)
        {
            for (int c = 0; c < 5; c++)
            {
                for (int r = 0; r < columns[c].Count; r++)
                {
                    if (columns[c][r].WorkType == workType)
                    {
                        column = c;
                        row = r;
                        return true;
                    }
                }
            }
            column = 0;
            row = 0;
            return false;
        }

        /// <summary>
        /// Formats a list of names with proper English grammar.
        /// 1 name: "Alice". 2 names: "Alice and Bob". 3+: "Alice, Bob, and Carol".
        /// </summary>
        private static string FormatNameList(List<string> names)
        {
            if (names.Count == 0) return "";
            if (names.Count == 1) return names[0];
            if (names.Count == 2) return "RimWorldAccess.Work.NameList.Two".Translate(names[0], names[1]);
            string separator = "RimWorldAccess.Work.NameList.Separator".Translate();
            string lastJoiner = "RimWorldAccess.Work.NameList.ThreePlusLastJoiner".Translate();
            return string.Join(separator, names.Take(names.Count - 1)) + lastJoiner + names[names.Count - 1];
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
                TolkHelper.Speak("RimWorldAccess.Work.Position.WithoutColumn".Loc(taskAnnouncement, position));
            }
            else
            {
                string taskAnnouncement = BuildTaskAnnouncement(entry, true);
                string position = MenuHelper.FormatPosition(basicModeIndex, allEntries.Count);
                string status = entry.CurrentPriority > 0
                    ? "RimWorldAccess.Work.Status.Enabled".Translate().ToString()
                    : "RimWorldAccess.Work.Status.Disabled".Translate().ToString();
                TolkHelper.Speak("RimWorldAccess.Work.Position.BasicModeItem".Loc(taskAnnouncement, status, position));
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
                    TolkHelper.Speak("RimWorldAccess.Work.ColumnEmpty".Loc(GetColumnName(currentColumn)));
                    return;
                }

                string taskAnnouncement = BuildTaskAnnouncement(entry, true);
                string position = MenuHelper.FormatPosition(currentRow, col.Count);

                if (includeColumnName)
                {
                    TolkHelper.Speak("RimWorldAccess.Work.Position.WithColumn".Loc(GetColumnName(currentColumn), taskAnnouncement, position));
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Work.Position.WithoutColumn".Loc(taskAnnouncement, position));
                }
            }
            else
            {
                // Basic mode
                if (allEntries.Count == 0)
                {
                    TolkHelper.Speak("RimWorldAccess.Work.NoWorkTypesAvailable".Loc());
                    return;
                }

                string taskAnnouncement = BuildTaskAnnouncement(entry, true);
                string position = MenuHelper.FormatPosition(basicModeIndex, allEntries.Count);
                string status = entry.CurrentPriority > 0
                    ? "RimWorldAccess.Work.Status.Enabled".Translate().ToString()
                    : "RimWorldAccess.Work.Status.Disabled".Translate().ToString();

                TolkHelper.Speak("RimWorldAccess.Work.Position.BasicModeItem".Loc(taskAnnouncement, status, position));
            }
        }

        /// <summary>
        /// Builds a full announcement for pawn switching via Tab.
        /// Includes pawn name, work type with priority, skill/passion details, and pawn position.
        /// </summary>
        private static string BuildPawnSwitchAnnouncement()
        {
            string pawnName = currentPawn?.LabelShort ?? "RimWorldAccess.Work.UnknownPawn".Translate().ToString();
            WorkTypeEntry entry = GetCurrentEntry();
            string pawnPosition = MenuHelper.FormatPosition(currentPawnIndex, allPawns.Count);

            if (entry == null)
            {
                return "RimWorldAccess.Work.PawnSwitch.NoEntry".Translate(pawnName, pawnPosition);
            }

            // Get full task details from BuildTaskAnnouncement (starts with labelShort)
            string fullDetails = BuildTaskAnnouncement(entry, true);
            string labelShort = entry.WorkType.labelShort;
            string detailsSuffix = fullDetails.Substring(labelShort.Length).TrimEnd('.');

            if (entry.IsPermanentlyDisabled)
            {
                // "PawnName. mine. Permanently disabled: reasons. 2 of 19"
                return "RimWorldAccess.Work.PawnSwitch.PermanentlyDisabled".Translate(pawnName, labelShort, detailsSuffix, pawnPosition);
            }

            // Insert priority after work type name
            string priorityLabel = entry.CurrentPriority == 0
                ? "RimWorldAccess.Work.PriorityWord.Disabled".Translate().ToString()
                : GetColumnName(PriorityToColumnIndex(entry.CurrentPriority));

            // "PawnName. mine, Priority 2. Uses Mining. Skill level: 5, Skilled. No passion. 2 of 19"
            return "RimWorldAccess.Work.PawnSwitch.WithPriority".Translate(pawnName, labelShort, priorityLabel, detailsSuffix, pawnPosition);
        }

        /// <summary>
        /// Builds the task announcement string with skills and passions.
        /// </summary>
        private static string BuildTaskAnnouncement(WorkTypeEntry entry, bool includeFullDetails)
        {
            if (entry == null) return "RimWorldAccess.Work.NoTaskSelected".Translate();

            var workType = entry.WorkType;
            var sb = new StringBuilder();

            sb.Append(workType.labelShort);

            if (entry.IsPermanentlyDisabled)
            {
                sb.Append("RimWorldAccess.Work.Task.PermanentlyDisabledPrefix".Translate().ToString());
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
                sb.Append("RimWorldAccess.Work.Task.UnskilledLabor".Translate().ToString());
                return sb.ToString();
            }

            // Check if skill names are redundant with task name
            bool skillNamesRedundant = relevantSkills.Count == 1 &&
                string.Equals(relevantSkills[0].skillLabel, workType.labelShort, StringComparison.OrdinalIgnoreCase);

            float avgSkill = currentPawn.skills.AverageOfRelevantSkillsFor(workType);
            int skillLevel = Math.Min(20, Math.Max(0, (int)Math.Round(avgSkill)));

            if (skillNamesRedundant)
            {
                sb.Append("RimWorldAccess.Work.Task.LevelSuffix".Translate(skillLevel).ToString());
            }
            else
            {
                sb.Append(". ");
                string skillAndSeparator = "RimWorldAccess.Work.Task.SkillListAndSeparator".Translate();
                for (int i = 0; i < relevantSkills.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(i == relevantSkills.Count - 1 ? skillAndSeparator : ", ");
                    }
                    sb.Append(relevantSkills[i].skillLabel);
                }
                sb.Append("RimWorldAccess.Work.Task.SkillsAndLevelSuffix".Translate(skillLevel).ToString());
            }

            // Get passion (only announced when present)
            Passion passion = currentPawn.skills.MaxPassionOfRelevantSkillsFor(workType);
            switch (passion)
            {
                case Passion.Major:
                    sb.Append("RimWorldAccess.Work.Task.BurningPassion".Translate().ToString());
                    break;
                case Passion.Minor:
                    sb.Append("RimWorldAccess.Work.Task.Passion".Translate().ToString());
                    break;
                default:
                    sb.Append(".");
                    break;
            }

            return sb.ToString();
        }

        private static void AnnounceCannotEnable(WorkTypeEntry entry)
        {
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            var reasons = currentPawn.GetReasonsForDisabledWorkType(entry.WorkType);
            string reasonText = string.Join(", ", reasons.Select(r => r.ToString()));
            TolkHelper.Speak("RimWorldAccess.Work.CannotEnable".Loc(reasonText), SpeechPriority.High);
        }

        private static string GetColumnName(int columnIndex)
        {
            switch (columnIndex)
            {
                case 0: return "RimWorldAccess.Work.Column.Priority1".Translate();
                case 1: return "RimWorldAccess.Work.Column.Priority2".Translate();
                case 2: return "RimWorldAccess.Work.Column.Priority3".Translate();
                case 3: return "RimWorldAccess.Work.Column.Priority4".Translate();
                case 4: return "RimWorldAccess.Work.Column.Disabled".Translate();
                default: return "RimWorldAccess.Work.Column.Unknown".Translate();
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
                    return "RimWorldAccess.Work.Placement.LeftOf".Translate(rightNeighbor);
                }
                else
                {
                    string leftNeighbor = column[0].WorkType.labelShort;
                    return "RimWorldAccess.Work.Placement.RightOf".Translate(leftNeighbor);
                }
            }
            else
            {
                // Three or more items
                if (atLeftEdge)
                {
                    string rightNeighbor = column[1].WorkType.labelShort;
                    return "RimWorldAccess.Work.Placement.FirstLeftOf".Translate(rightNeighbor);
                }
                else if (atRightEdge)
                {
                    string leftNeighbor = column[insertedIndex - 1].WorkType.labelShort;
                    return "RimWorldAccess.Work.Placement.LastRightOf".Translate(leftNeighbor);
                }
                else
                {
                    // In the middle - between left and right neighbors
                    string leftNeighbor = column[insertedIndex - 1].WorkType.labelShort;
                    string rightNeighbor = column[insertedIndex + 1].WorkType.labelShort;
                    return "RimWorldAccess.Work.Placement.Between".Translate(leftNeighbor, rightNeighbor);
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
