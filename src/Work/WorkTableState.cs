using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Table-layout work menu. Rows are colonists, columns are work types
    /// (vanilla priority order). Alt+S sorts by PawnColumnWorker_WorkPriority
    /// semantics. [ / ] cycle priority vanilla-style; Shift+[ / Shift+] apply
    /// the same cycle to every eligible colonist (matches vanilla shift-click
    /// header). Painting mirrors AnimalsMenuState: Shift+Arrow paint+move,
    /// Shift+Home/End paint range, Ctrl+Shift+Home/End paint whole column.
    /// </summary>
    public static class WorkTableState
    {
        public static bool IsActive { get; private set; }

        private static List<Pawn> pawns = new List<Pawn>();
        private static TabularMenuHelper<Pawn> tableHelper;
        private static Dictionary<Pawn, Dictionary<WorkTypeDef, int>> originalPriorities
            = new Dictionary<Pawn, Dictionary<WorkTypeDef, int>>();

        public static TabularMenuHelper<Pawn> TableHelper => tableHelper;
        public static TypeaheadSearchHelper Typeahead => tableHelper?.Typeahead;
        public static int CurrentRowIndex => tableHelper?.CurrentRowIndex ?? 0;
        public static int CurrentColumnIndex => tableHelper?.CurrentColumnIndex ?? 0;
        public static int PawnCount => pawns.Count;

        public static Pawn CurrentPawn =>
            pawns.Count > 0 && CurrentRowIndex >= 0 && CurrentRowIndex < pawns.Count
                ? pawns[CurrentRowIndex]
                : null;

        public static WorkTypeDef CurrentWorkType =>
            WorkTableHelper.WorkTypeForColumn(CurrentColumnIndex);

        public static bool IsManualMode => Find.PlaySettings.useWorkPriorities;

        #region Lifecycle

        public static void Open(Pawn initialPawn = null)
        {
            if (IsActive) return;
            if (Find.CurrentMap == null)
            {
                TolkHelper.Speak("No map loaded");
                return;
            }

            WorkTableHelper.RefreshWorkTypes();

            pawns = PlayerPawnsDisplayOrderUtility.InOrder(
                    Find.CurrentMap.mapPawns.FreeColonists
                        .Where(p => !p.DevelopmentalStage.Baby() && p.workSettings != null))
                .ToList();

            if (pawns.Count == 0)
            {
                TolkHelper.Speak("No colonists available");
                return;
            }

            SnapshotOriginalPriorities();

            tableHelper = new TabularMenuHelper<Pawn>(
                getColumnCount: () => WorkTableHelper.TotalColumnCount,
                getItemLabel: WorkTableHelper.GetPawnLabel,
                getColumnName: WorkTableHelper.GetColumnName,
                getColumnValue: WorkTableHelper.GetColumnValue,
                sortByColumn: (items, col, desc) => WorkTableHelper.SortPawnsByColumn(items, col, desc),
                getColumnTooltip: WorkTableHelper.GetColumnTooltip,
                isColumnSortable: WorkTableHelper.IsColumnSortable);

            tableHelper.Reset();
            tableHelper.SetDefaultOrder(pawns);

            if (initialPawn != null)
            {
                int idx = pawns.IndexOf(initialPawn);
                if (idx >= 0) tableHelper.CurrentRowIndex = idx;
            }
            // Land on the first work-type column rather than the Name column.
            tableHelper.CurrentColumnIndex = WorkTableHelper.WorkTypes.Count > 0 ? 1 : 0;

            IsActive = true;
            string mode = IsManualMode ? "manual priority mode" : "basic mode";
            TolkHelper.Speak(
                $"Work table, {pawns.Count} colonists, {WorkTableHelper.WorkTypes.Count} work types, {mode}");
            AnnounceCurrentCell(includePawnName: true);
        }

        public static void Confirm()
        {
            if (!IsActive) return;
            RefreshAllPawnsWorkGivers();
            CleanupState();
            TolkHelper.Speak("Work preferences saved");
        }

        public static void Cancel()
        {
            if (!IsActive) return;
            foreach (var pair in originalPriorities)
            {
                Pawn pawn = pair.Key;
                if (pawn?.workSettings == null) continue;
                foreach (var kvp in pair.Value)
                {
                    if (!pawn.WorkTypeIsDisabled(kvp.Key))
                        pawn.workSettings.SetPriority(kvp.Key, kvp.Value);
                }
                pawn.workSettings.Notify_UseWorkPrioritiesChanged();
            }
            CleanupState();
            TolkHelper.Speak("Work menu cancelled, changes discarded");
        }

        /// <summary>
        /// Closes the table view without saving/cancelling (used when swapping
        /// to the focused view). Changes are applied in real-time so they
        /// persist; only the menu UI closes.
        /// </summary>
        public static void CloseForSwap()
        {
            if (!IsActive) return;
            RefreshAllPawnsWorkGivers();
            CleanupState();
        }

        private static void CleanupState()
        {
            IsActive = false;
            pawns.Clear();
            originalPriorities.Clear();
            tableHelper?.ClearSearch();
            tableHelper = null;
        }

        private static void SnapshotOriginalPriorities()
        {
            originalPriorities.Clear();
            foreach (var p in pawns)
            {
                var snap = new Dictionary<WorkTypeDef, int>();
                foreach (var w in WorkTableHelper.WorkTypes)
                {
                    if (!p.WorkTypeIsDisabled(w))
                        snap[w] = p.workSettings.GetPriority(w);
                }
                originalPriorities[p] = snap;
            }
        }

        private static void RefreshAllPawnsWorkGivers()
        {
            foreach (var p in pawns)
                p.workSettings?.Notify_UseWorkPrioritiesChanged();
        }

        #endregion

        #region Navigation

        public static void SelectNextPawn()
        {
            if (pawns.Count == 0) return;
            tableHelper.SelectNextRow(pawns.Count);
            AnnounceCurrentCell(includePawnName: true);
        }

        public static void SelectPreviousPawn()
        {
            if (pawns.Count == 0) return;
            tableHelper.SelectPreviousRow(pawns.Count);
            AnnounceCurrentCell(includePawnName: true);
        }

        public static void SelectNextColumn()
        {
            if (pawns.Count == 0) return;
            tableHelper.SelectNextColumn();
            AnnounceCurrentCell(includePawnName: false);
        }

        public static void SelectPreviousColumn()
        {
            if (pawns.Count == 0) return;
            tableHelper.SelectPreviousColumn();
            AnnounceCurrentCell(includePawnName: false);
        }

        public static void JumpToFirst()
        {
            if (pawns.Count == 0) return;
            tableHelper.JumpToFirst(pawns.Count);
            AnnounceCurrentCell(includePawnName: true);
        }

        public static void JumpToLast()
        {
            if (pawns.Count == 0) return;
            tableHelper.JumpToLast(pawns.Count);
            AnnounceCurrentCell(includePawnName: true);
        }

        #endregion

        #region Priority Editing

        /// <summary>
        /// Sets the priority of the current cell to an absolute value (0-4).
        /// Invoked by number keys 0-4.
        /// </summary>
        public static void SetPriorityForCurrentCell(int priority)
        {
            if (priority < 0 || priority > 4) return;
            Pawn pawn = CurrentPawn;
            WorkTypeDef workType = CurrentWorkType;
            if (pawn == null || workType == null)
            {
                RejectAndAnnounce("Select a work type column first");
                return;
            }
            if (pawn.WorkTypeIsDisabled(workType))
            {
                AnnounceIncapable(pawn, workType);
                return;
            }

            ApplyPriority(pawn, workType, priority, announceChange: true);
            ResortIfNeeded();
        }

        /// <summary>
        /// Basic-mode toggle of current cell (Space). No-op in manual mode
        /// (number keys are used there).
        /// </summary>
        public static void ToggleCurrentCell()
        {
            Pawn pawn = CurrentPawn;
            WorkTypeDef workType = CurrentWorkType;
            if (pawn == null || workType == null) return;
            if (pawn.WorkTypeIsDisabled(workType))
            {
                AnnounceIncapable(pawn, workType);
                return;
            }
            int current = pawn.workSettings.GetPriority(workType);
            int next = current > 0 ? 0 : 3;
            ApplyPriority(pawn, workType, next, announceChange: true);
            ResortIfNeeded();
        }

        /// <summary>
        /// Cycles the current cell's priority by one step. decrease=true matches
        /// vanilla left-click HeaderClicked (number goes down / importance goes
        /// up); decrease=false matches right-click. In basic mode this flips
        /// between 3 (on) and 0 (off) by direction.
        /// </summary>
        public static void CycleCurrentCellPriority(bool decrease)
        {
            Pawn pawn = CurrentPawn;
            WorkTypeDef workType = CurrentWorkType;
            if (pawn == null || workType == null)
            {
                RejectAndAnnounce("Select a work type column first");
                return;
            }
            if (pawn.WorkTypeIsDisabled(workType))
            {
                AnnounceIncapable(pawn, workType);
                return;
            }

            int current = pawn.workSettings.GetPriority(workType);
            if (!TryComputeCycleTarget(current, decrease, out int next))
            {
                // No-op case (e.g., already at bound). Announce current state.
                AnnounceCurrentCell(includePawnName: false);
                return;
            }
            ApplyPriority(pawn, workType, next, announceChange: true);
            ResortIfNeeded();
        }

        /// <summary>
        /// Applies the same cycle operation to every eligible colonist in the
        /// current column. Matches vanilla PawnColumnWorker_WorkPriority.HeaderClicked
        /// with shift held.
        /// </summary>
        public static void CycleAllColonistsPriority(bool decrease)
        {
            WorkTypeDef workType = CurrentWorkType;
            if (workType == null)
            {
                RejectAndAnnounce("Select a work type column first");
                return;
            }

            var changed = new List<string>();
            bool anyLowSkillActivated = false;
            bool anyIdeoOpposedActivated = false;
            var ideoOpposedPawns = new List<Pawn>();

            foreach (var pawn in pawns)
            {
                if (pawn.workSettings == null || !pawn.workSettings.EverWork) continue;
                if (pawn.WorkTypeIsDisabled(workType)) continue;

                int current = pawn.workSettings.GetPriority(workType);
                if (!TryComputeCycleTarget(current, decrease, out int next)) continue;
                if (next == current) continue;

                bool wasActive = pawn.workSettings.WorkIsActive(workType);
                pawn.workSettings.SetPriority(workType, next);
                changed.Add(pawn.LabelShort);

                if (!wasActive && pawn.workSettings.WorkIsActive(workType))
                {
                    if (workType.relevantSkills.Any() &&
                        pawn.skills.AverageOfRelevantSkillsFor(workType) <= 2f)
                        anyLowSkillActivated = true;
                    if (pawn.Ideo != null && pawn.Ideo.IsWorkTypeConsideredDangerous(workType))
                    {
                        anyIdeoOpposedActivated = true;
                        ideoOpposedPawns.Add(pawn);
                    }
                }
            }

            if (changed.Count == 0)
            {
                RejectAndAnnounce($"No change for {workType.labelShort}");
                return;
            }

            PlayPriorityChangeSound();
            if (anyLowSkillActivated) SoundDefOf.Crunch.PlayOneShotOnCamera();
            if (anyIdeoOpposedActivated)
            {
                SoundDefOf.DislikedWorkTypeActivated.PlayOneShotOnCamera();
                foreach (var p in ideoOpposedPawns)
                    Messages.Message(
                        "MessageIdeoOpposedWorkTypeSelected".Translate(p, workType.gerundLabel),
                        p, MessageTypeDefOf.CautionInput, historical: false);
            }

            string action = decrease ? "raised" : "lowered";
            TolkHelper.Speak(
                $"{workType.labelShort} {action} for {MenuHelper.FormatNameList(changed)}");
            ResortIfNeeded();
        }

        /// <summary>
        /// Vanilla HeaderClicked priority cycle:
        ///   decrease (left-click): skip priority 1; else priority-1 with 0 wrapping to 4.
        ///   increase (right-click): skip priority 0; else priority+1 with 4 wrapping to 0.
        /// In basic mode: decrease sets 0->3 (enable); increase sets >0 -> 0 (disable).
        /// Returns false if the operation would be a no-op.
        /// </summary>
        private static bool TryComputeCycleTarget(int current, bool decrease, out int next)
        {
            if (!Find.PlaySettings.useWorkPriorities)
            {
                if (decrease)
                {
                    if (current == 0) { next = 3; return true; }
                    next = current; return false;
                }
                else
                {
                    if (current > 0) { next = 0; return true; }
                    next = current; return false;
                }
            }

            if (decrease)
            {
                if (current == 1) { next = 1; return false; }
                int n = current - 1;
                if (n < 0) n = 4;
                next = n;
                return true;
            }
            else
            {
                if (current == 0) { next = 0; return false; }
                int n = current + 1;
                if (n > 4) n = 0;
                next = n;
                return true;
            }
        }

        private static void ApplyPriority(Pawn pawn, WorkTypeDef workType, int newPriority, bool announceChange)
        {
            int current = pawn.workSettings.GetPriority(workType);
            if (current == newPriority)
            {
                if (announceChange)
                    TolkHelper.Speak($"{pawn.LabelShort} {workType.labelShort} already {StateLabel(newPriority)}");
                return;
            }
            bool wasActive = pawn.workSettings.WorkIsActive(workType);
            pawn.workSettings.SetPriority(workType, newPriority);
            PlayPriorityChangeSound();
            PlayActivationSounds(pawn, workType, wasActive);

            if (announceChange)
            {
                string stateLabel = StateLabel(newPriority);
                string stars = WorkTableHelper.PassionStars(
                    pawn.skills.MaxPassionOfRelevantSkillsFor(workType));
                if (!string.IsNullOrEmpty(stars)) stateLabel += " " + stars;
                TolkHelper.Speak(stateLabel);
            }
        }

        private static string StateLabel(int priority)
        {
            if (!Find.PlaySettings.useWorkPriorities)
                return priority > 0 ? "on" : "off";
            return priority > 0 ? $"priority {priority}" : "off";
        }

        private static void PlayPriorityChangeSound()
        {
            if (Find.PlaySettings.useWorkPriorities)
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
            else
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
        }

        private static void PlayActivationSounds(Pawn pawn, WorkTypeDef workType, bool wasActive)
        {
            if (wasActive) return;
            if (!pawn.workSettings.WorkIsActive(workType)) return;

            if (workType.relevantSkills.Any() &&
                pawn.skills.AverageOfRelevantSkillsFor(workType) <= 2f)
                SoundDefOf.Crunch.PlayOneShotOnCamera();
            if (pawn.Ideo != null && pawn.Ideo.IsWorkTypeConsideredDangerous(workType))
            {
                Messages.Message(
                    "MessageIdeoOpposedWorkTypeSelected".Translate(pawn, workType.gerundLabel),
                    pawn, MessageTypeDefOf.CautionInput, historical: false);
                SoundDefOf.DislikedWorkTypeActivated.PlayOneShotOnCamera();
            }
        }

        #endregion

        #region Painting

        public static void PaintDown()
        {
            if (pawns.Count < 2) return;
            WorkTypeDef workType = CurrentWorkType;
            if (workType == null)
            {
                RejectAndAnnounce("Cannot paint the Name column");
                return;
            }

            Pawn source = CurrentPawn;
            if (source == null) return;
            int brush = source.WorkTypeIsDisabled(workType)
                ? -1
                : source.workSettings.GetPriority(workType);
            if (brush < 0)
            {
                RejectAndAnnounce($"{source.LabelShort} is incapable of {workType.labelShort}");
                return;
            }

            tableHelper.SelectNextRow(pawns.Count);
            Pawn target = CurrentPawn;
            PaintSingle(target, workType, brush);
        }

        public static void PaintUp()
        {
            if (pawns.Count < 2) return;
            WorkTypeDef workType = CurrentWorkType;
            if (workType == null)
            {
                RejectAndAnnounce("Cannot paint the Name column");
                return;
            }
            Pawn source = CurrentPawn;
            if (source == null) return;
            int brush = source.WorkTypeIsDisabled(workType)
                ? -1
                : source.workSettings.GetPriority(workType);
            if (brush < 0)
            {
                RejectAndAnnounce($"{source.LabelShort} is incapable of {workType.labelShort}");
                return;
            }

            tableHelper.SelectPreviousRow(pawns.Count);
            Pawn target = CurrentPawn;
            PaintSingle(target, workType, brush);
        }

        private static void PaintSingle(Pawn target, WorkTypeDef workType, int brush)
        {
            string position = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, pawns.Count);
            if (target.WorkTypeIsDisabled(workType))
            {
                TolkHelper.Speak($"{target.LabelShort}: incapable. {position}");
                return;
            }
            int current = target.workSettings.GetPriority(workType);
            if (current == brush)
            {
                TolkHelper.Speak($"{target.LabelShort}: already {StateLabel(brush)}. {position}");
                return;
            }
            bool wasActive = target.workSettings.WorkIsActive(workType);
            target.workSettings.SetPriority(workType, brush);
            PlayPriorityChangeSound();
            PlayActivationSounds(target, workType, wasActive);
            TolkHelper.Speak($"{target.LabelShort}: {StateLabel(brush)}. {position}");
        }

        public static void PaintToFirst() => PaintRange(toward: -1);
        public static void PaintToLast() => PaintRange(toward: 1);

        private static void PaintRange(int toward)
        {
            if (pawns.Count == 0) return;
            WorkTypeDef workType = CurrentWorkType;
            if (workType == null)
            {
                RejectAndAnnounce("Cannot paint the Name column");
                return;
            }
            Pawn source = CurrentPawn;
            if (source == null) return;
            if (source.WorkTypeIsDisabled(workType))
            {
                RejectAndAnnounce($"{source.LabelShort} is incapable of {workType.labelShort}");
                return;
            }
            int brush = source.workSettings.GetPriority(workType);
            int currentRow = tableHelper.CurrentRowIndex;
            int startRow, endRow;
            if (toward < 0) { startRow = 0; endRow = currentRow; }
            else { startRow = currentRow; endRow = pawns.Count - 1; }

            PaintBulk(workType, brush, startRow, endRow, toward < 0);
        }

        public static void PaintEntireColumn()
        {
            if (pawns.Count == 0) return;
            WorkTypeDef workType = CurrentWorkType;
            if (workType == null)
            {
                RejectAndAnnounce("Cannot paint the Name column");
                return;
            }
            Pawn source = CurrentPawn;
            if (source == null) return;
            if (source.WorkTypeIsDisabled(workType))
            {
                RejectAndAnnounce($"{source.LabelShort} is incapable of {workType.labelShort}");
                return;
            }
            int brush = source.workSettings.GetPriority(workType);
            PaintBulk(workType, brush, 0, pawns.Count - 1, moveToStart: false);
        }

        private static void PaintBulk(WorkTypeDef workType, int brush, int startRow, int endRow, bool moveToStart)
        {
            var changed = new List<string>();
            bool anyLowSkill = false;
            bool anyIdeoOpposed = false;
            var ideoOpposedPawns = new List<Pawn>();

            for (int i = startRow; i <= endRow; i++)
            {
                Pawn p = pawns[i];
                if (p.WorkTypeIsDisabled(workType)) continue;
                int current = p.workSettings.GetPriority(workType);
                if (current == brush) continue;
                bool wasActive = p.workSettings.WorkIsActive(workType);
                p.workSettings.SetPriority(workType, brush);
                changed.Add(p.LabelShort);
                if (!wasActive && p.workSettings.WorkIsActive(workType))
                {
                    if (workType.relevantSkills.Any() &&
                        p.skills.AverageOfRelevantSkillsFor(workType) <= 2f)
                        anyLowSkill = true;
                    if (p.Ideo != null && p.Ideo.IsWorkTypeConsideredDangerous(workType))
                    {
                        anyIdeoOpposed = true;
                        ideoOpposedPawns.Add(p);
                    }
                }
            }

            tableHelper.CurrentRowIndex = moveToStart ? startRow : endRow;

            if (changed.Count == 0)
            {
                TolkHelper.Speak($"{workType.labelShort} already {StateLabel(brush)} for this range");
                return;
            }

            BulkSoundQueue.Queue(changed.Count, Find.PlaySettings.useWorkPriorities
                ? SoundDefOf.DragSlider
                : SoundDefOf.Checkbox_TurnedOn);
            if (anyLowSkill) SoundDefOf.Crunch.PlayOneShotOnCamera();
            if (anyIdeoOpposed)
            {
                SoundDefOf.DislikedWorkTypeActivated.PlayOneShotOnCamera();
                foreach (var p in ideoOpposedPawns)
                    Messages.Message(
                        "MessageIdeoOpposedWorkTypeSelected".Translate(p, workType.gerundLabel),
                        p, MessageTypeDefOf.CautionInput, historical: false);
            }
            TolkHelper.Speak(
                $"Painted {workType.labelShort} to {StateLabel(brush)} for {MenuHelper.FormatNameList(changed)}");
            ResortIfNeeded();
        }

        #endregion

        #region Sorting / Mode / Typeahead

        public static void ToggleSortByCurrentColumn()
        {
            if (pawns.Count == 0) return;
            var result = tableHelper.ToggleSortByCurrentColumn(pawns, out string direction, out bool sortCleared);
            if (result == null)
            {
                TolkHelper.Speak($"{tableHelper.GetCurrentColumnName()} cannot be sorted");
                return;
            }
            pawns = result.ToList();
            if (sortCleared)
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                TolkHelper.Speak("Sort cleared, default order");
            }
            else
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                TolkHelper.Speak($"Sorted by {tableHelper.GetCurrentColumnName()} ({direction})");
            }
            AnnounceCurrentCell(includePawnName: true);
        }

        private static void ResortIfNeeded()
        {
            if (!tableHelper.HasActiveSort) return;
            var resorted = tableHelper.ResortAfterEdit(pawns);
            if (resorted != null) pawns = resorted.ToList();
        }

        public static void ToggleMode()
        {
            Find.PlaySettings.useWorkPriorities = !Find.PlaySettings.useWorkPriorities;
            foreach (var p in pawns)
                p.workSettings?.Notify_UseWorkPrioritiesChanged();
            string mode = Find.PlaySettings.useWorkPriorities ? "Manual priority mode" : "Basic mode";
            TolkHelper.Speak(mode);
            AnnounceCurrentCell(includePawnName: false);
        }

        public static bool HandleTypeahead(char c)
        {
            if (pawns.Count == 0) return false;
            if (tableHelper.HandleTypeahead(c, pawns, out _))
            {
                AnnounceWithSearch();
                return true;
            }
            TolkHelper.Speak($"No matches for '{tableHelper.Typeahead.LastFailedSearch}'");
            return false;
        }

        public static bool HandleBackspace()
        {
            if (pawns.Count == 0) return false;
            if (tableHelper.HandleBackspace(pawns, out _))
            {
                if (tableHelper.Typeahead.HasActiveSearch)
                    AnnounceWithSearch();
                else
                    AnnounceCurrentCell(includePawnName: true);
                return true;
            }
            return false;
        }

        public static bool ClearSearchIfActive()
        {
            if (tableHelper?.Typeahead.HasActiveSearch == true)
            {
                tableHelper.Typeahead.ClearSearchAndAnnounce();
                AnnounceCurrentCell(includePawnName: true);
                return true;
            }
            return false;
        }

        #endregion

        #region Announcements

        public static void AnnounceCurrentCell(bool includePawnName)
        {
            if (pawns.Count == 0) return;
            Pawn pawn = CurrentPawn;
            if (pawn == null) return;
            string announcement = tableHelper.BuildCellAnnouncement(pawn, pawns.Count, includePawnName);
            TolkHelper.Speak(announcement);
        }

        public static void AnnounceWithSearch()
        {
            if (pawns.Count == 0) return;
            Pawn pawn = CurrentPawn;
            if (pawn == null) return;
            string announcement = tableHelper.BuildCellAnnouncementWithSearch(pawn, pawns.Count);
            TolkHelper.Speak(announcement);
        }

        private static void AnnounceIncapable(Pawn pawn, WorkTypeDef workType)
        {
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            string reasons = WorkTableHelper.BuildDisabledReasons(pawn, workType);
            string detail = string.IsNullOrEmpty(reasons) ? "" : $": {reasons}";
            TolkHelper.Speak(
                $"{pawn.LabelShort} cannot do {workType.labelShort}{detail}",
                SpeechPriority.High);
        }

        private static void RejectAndAnnounce(string message)
        {
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak(message);
        }

        #endregion
    }
}
