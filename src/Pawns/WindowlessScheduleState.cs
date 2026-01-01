using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages the state and navigation for the windowless schedule/timetable interface.
    /// Provides 2D grid navigation (pawns x hours) with keyboard controls.
    /// Similar to WorkMenuState but for managing colonist schedules without opening the native tab.
    /// </summary>
    public static class WindowlessScheduleState
    {
        private static bool isActive = false;
        private static int selectedPawnIndex = 0;
        private static int selectedHourIndex = 0;
        private static TimeAssignmentDef selectedAssignment = null;
        private static List<Pawn> pawns = new List<Pawn>();
        private static List<TimeAssignmentDef> copiedSchedule = null;

        // Track pending changes: Dictionary<Pawn, Dictionary<Hour, NewAssignment>>
        private static Dictionary<Pawn, Dictionary<int, TimeAssignmentDef>> pendingChanges = new Dictionary<Pawn, Dictionary<int, TimeAssignmentDef>>();

        // Track original schedules for revert: Dictionary<Pawn, List<TimeAssignmentDef>>
        private static Dictionary<Pawn, List<TimeAssignmentDef>> originalSchedules = new Dictionary<Pawn, List<TimeAssignmentDef>>();

        public static bool IsActive => isActive;
        public static int SelectedPawnIndex => selectedPawnIndex;
        public static int SelectedHourIndex => selectedHourIndex;
        public static List<Pawn> Pawns => pawns;
        public static TimeAssignmentDef SelectedAssignment => selectedAssignment;

        /// <summary>
        /// Opens the schedule menu. Initializes pawn list and sets focus to current game hour.
        /// </summary>
        public static void Open()
        {
            isActive = true;
            selectedPawnIndex = 0;
            selectedHourIndex = GenLocalDate.HourOfDay(Find.CurrentMap);
            copiedSchedule = null;
            pendingChanges.Clear();
            originalSchedules.Clear();

            // Get list of pawns (colonists + controllable subhumans, excluding babies)
            pawns.Clear();
            if (Find.CurrentMap?.mapPawns?.FreeColonists != null)
            {
                pawns.AddRange(Find.CurrentMap.mapPawns.FreeColonists
                    .Where(p => !p.DevelopmentalStage.Baby()));
            }

            // Add colony subhumans (controllable animals, etc.)
            if (Find.CurrentMap?.mapPawns?.SpawnedColonyAnimals != null)
            {
                pawns.AddRange(Find.CurrentMap.mapPawns.SpawnedColonyAnimals
                    .Where(p => p.RaceProps.intelligence >= Intelligence.ToolUser));
            }

            // Sort by label for consistency
            pawns = pawns.OrderBy(p => p.LabelShort).ToList();

            // Store original schedules for each pawn (for revert on cancel)
            foreach (var pawn in pawns)
            {
                if (pawn.timetable != null)
                {
                    var originalSchedule = new List<TimeAssignmentDef>();
                    for (int hour = 0; hour < 24; hour++)
                    {
                        originalSchedule.Add(pawn.timetable.GetAssignment(hour));
                    }
                    originalSchedules[pawn] = originalSchedule;
                }
            }

            // Default to "Anything" assignment
            selectedAssignment = TimeAssignmentDefOf.Anything;

            // If we have a selected pawn, try to focus on them
            if (Find.Selector.SingleSelectedThing is Pawn selectedPawn)
            {
                int index = pawns.IndexOf(selectedPawn);
                if (index >= 0)
                {
                    selectedPawnIndex = index;
                }
            }

            TolkHelper.Speak("Schedule");
            UpdateClipboard();
        }

        /// <summary>
        /// Confirms all pending changes, applies them to pawns, and closes the schedule menu.
        /// </summary>
        public static void Confirm()
        {
            int changesApplied = 0;

            // Apply all pending changes
            foreach (var pawnChanges in pendingChanges)
            {
                Pawn pawn = pawnChanges.Key;
                if (pawn.timetable == null)
                    continue;

                foreach (var hourChange in pawnChanges.Value)
                {
                    int hour = hourChange.Key;
                    TimeAssignmentDef newAssignment = hourChange.Value;
                    pawn.timetable.SetAssignment(hour, newAssignment);
                    changesApplied++;
                }
            }

            string message = changesApplied > 0
                ? $"Applied {changesApplied} schedule changes"
                : "No changes made";

            TolkHelper.Speak(message);

            // Close and cleanup
            isActive = false;
            pawns.Clear();
            selectedPawnIndex = 0;
            selectedHourIndex = 0;
            selectedAssignment = null;
            copiedSchedule = null;
            pendingChanges.Clear();
            originalSchedules.Clear();
        }

        /// <summary>
        /// Cancels all pending changes, reverts to original schedules, and closes the schedule menu.
        /// </summary>
        public static void Cancel()
        {
            // Revert all changes back to original
            foreach (var pawnOriginal in originalSchedules)
            {
                Pawn pawn = pawnOriginal.Key;
                if (pawn.timetable == null)
                    continue;

                List<TimeAssignmentDef> originalSchedule = pawnOriginal.Value;
                for (int hour = 0; hour < 24; hour++)
                {
                    pawn.timetable.SetAssignment(hour, originalSchedule[hour]);
                }
            }

            TolkHelper.Speak("Schedule changes cancelled");

            // Close and cleanup
            isActive = false;
            pawns.Clear();
            selectedPawnIndex = 0;
            selectedHourIndex = 0;
            selectedAssignment = null;
            copiedSchedule = null;
            pendingChanges.Clear();
            originalSchedules.Clear();
        }

        /// <summary>
        /// Moves selection up to previous pawn (wraps around).
        /// </summary>
        public static void MoveUp()
        {
            if (pawns.Count == 0)
                return;

            selectedPawnIndex = MenuHelper.SelectPrevious(selectedPawnIndex, pawns.Count);
            UpdateClipboard();
        }

        /// <summary>
        /// Moves selection down to next pawn (wraps around).
        /// </summary>
        public static void MoveDown()
        {
            if (pawns.Count == 0)
                return;

            selectedPawnIndex = MenuHelper.SelectNext(selectedPawnIndex, pawns.Count);
            UpdateClipboard();
        }

        /// <summary>
        /// Moves selection left to previous hour (wraps around).
        /// </summary>
        public static void MoveLeft()
        {
            selectedHourIndex = MenuHelper.SelectPrevious(selectedHourIndex, 24);
            UpdateClipboard();
        }

        /// <summary>
        /// Moves selection right to next hour (wraps around).
        /// </summary>
        public static void MoveRight()
        {
            selectedHourIndex = MenuHelper.SelectNext(selectedHourIndex, 24);
            UpdateClipboard();
        }

        /// <summary>
        /// Cycles forward through available time assignment types for current cell.
        /// Order: Anything -> Work -> Joy -> Sleep -> Meditate (if available)
        /// </summary>
        public static void CycleAssignment()
        {
            if (pawns.Count == 0 || selectedPawnIndex < 0 || selectedPawnIndex >= pawns.Count)
                return;

            Pawn pawn = pawns[selectedPawnIndex];
            if (pawn.timetable == null)
                return;

            var availableAssignments = GetAvailableAssignments();
            if (availableAssignments.Count == 0)
                return;

            // Get the CURRENT cell's assignment, not the tracking variable
            TimeAssignmentDef currentCellAssignment = pawn.timetable.GetAssignment(selectedHourIndex);
            int currentIndex = availableAssignments.IndexOf(currentCellAssignment);
            if (currentIndex < 0) currentIndex = 0;

            // Wrap around: at end, go to start
            currentIndex = (currentIndex + 1) % availableAssignments.Count;
            selectedAssignment = availableAssignments[currentIndex];

            // Apply to current cell immediately
            ApplyAssignment();
        }

        /// <summary>
        /// Cycles backward through available time assignment types for current cell.
        /// Order: Meditate (if available) -> Sleep -> Joy -> Work -> Anything
        /// </summary>
        public static void CycleAssignmentBackward()
        {
            if (pawns.Count == 0 || selectedPawnIndex < 0 || selectedPawnIndex >= pawns.Count)
                return;

            Pawn pawn = pawns[selectedPawnIndex];
            if (pawn.timetable == null)
                return;

            var availableAssignments = GetAvailableAssignments();
            if (availableAssignments.Count == 0)
                return;

            // Get the CURRENT cell's assignment, not the tracking variable
            TimeAssignmentDef currentCellAssignment = pawn.timetable.GetAssignment(selectedHourIndex);
            int currentIndex = availableAssignments.IndexOf(currentCellAssignment);
            if (currentIndex < 0) currentIndex = 0;

            // Wrap around: at start, go to end
            currentIndex = (currentIndex - 1 + availableAssignments.Count) % availableAssignments.Count;
            selectedAssignment = availableAssignments[currentIndex];

            // Apply to current cell immediately
            ApplyAssignment();
        }

        /// <summary>
        /// Gets the list of available time assignments (includes Meditate if Royalty active).
        /// </summary>
        private static List<TimeAssignmentDef> GetAvailableAssignments()
        {
            var assignments = new List<TimeAssignmentDef>
            {
                TimeAssignmentDefOf.Anything,
                TimeAssignmentDefOf.Work,
                TimeAssignmentDefOf.Joy,
                TimeAssignmentDefOf.Sleep
            };

            // Add Meditate if it exists (Royalty DLC)
            if (TimeAssignmentDefOf.Meditate != null)
            {
                assignments.Add(TimeAssignmentDefOf.Meditate);
            }

            return assignments;
        }

        /// <summary>
        /// Applies the currently selected assignment to the current cell.
        /// Changes are applied immediately (for visual feedback) but tracked as pending.
        /// </summary>
        public static void ApplyAssignment()
        {
            if (pawns.Count == 0 || selectedPawnIndex < 0 || selectedPawnIndex >= pawns.Count)
                return;

            Pawn pawn = pawns[selectedPawnIndex];
            if (pawn.timetable == null)
                return;

            // Apply the change immediately (for visual feedback)
            pawn.timetable.SetAssignment(selectedHourIndex, selectedAssignment);

            // Track as pending change
            if (!pendingChanges.ContainsKey(pawn))
            {
                pendingChanges[pawn] = new Dictionary<int, TimeAssignmentDef>();
            }
            pendingChanges[pawn][selectedHourIndex] = selectedAssignment;

            string message = $"{pawn.LabelShort}, Hour {selectedHourIndex}: {selectedAssignment.label} (pending)";
            TolkHelper.Speak(message);
        }

        /// <summary>
        /// Fills the rest of the current row (from current hour to end) with the selected assignment.
        /// </summary>
        public static void FillRow()
        {
            if (pawns.Count == 0 || selectedPawnIndex < 0 || selectedPawnIndex >= pawns.Count)
                return;

            Pawn pawn = pawns[selectedPawnIndex];
            if (pawn.timetable == null)
                return;

            // Track pending changes
            if (!pendingChanges.ContainsKey(pawn))
            {
                pendingChanges[pawn] = new Dictionary<int, TimeAssignmentDef>();
            }

            int cellsFilled = 0;
            for (int hour = selectedHourIndex; hour <= 23; hour++)
            {
                pawn.timetable.SetAssignment(hour, selectedAssignment);
                pendingChanges[pawn][hour] = selectedAssignment;
                cellsFilled++;
            }

            string message = $"{pawn.LabelShort}: Filled {cellsFilled} hours with {selectedAssignment.label} (pending)";
            TolkHelper.Speak(message);
        }

        /// <summary>
        /// Copies the current pawn's entire schedule to clipboard storage.
        /// </summary>
        public static void CopySchedule()
        {
            if (pawns.Count == 0 || selectedPawnIndex < 0 || selectedPawnIndex >= pawns.Count)
                return;

            Pawn pawn = pawns[selectedPawnIndex];
            if (pawn.timetable == null)
                return;

            copiedSchedule = new List<TimeAssignmentDef>();
            for (int hour = 0; hour < 24; hour++)
            {
                copiedSchedule.Add(pawn.timetable.GetAssignment(hour));
            }

            TolkHelper.Speak($"Copied schedule from {pawn.LabelShort}");
        }

        /// <summary>
        /// Pastes the copied schedule to the current pawn.
        /// </summary>
        public static void PasteSchedule()
        {
            if (copiedSchedule == null)
            {
                TolkHelper.Speak("No schedule copied");
                return;
            }

            if (pawns.Count == 0 || selectedPawnIndex < 0 || selectedPawnIndex >= pawns.Count)
                return;

            Pawn pawn = pawns[selectedPawnIndex];
            if (pawn.timetable == null)
                return;

            // Track pending changes
            if (!pendingChanges.ContainsKey(pawn))
            {
                pendingChanges[pawn] = new Dictionary<int, TimeAssignmentDef>();
            }

            for (int hour = 0; hour < 24; hour++)
            {
                pawn.timetable.SetAssignment(hour, copiedSchedule[hour]);
                pendingChanges[pawn][hour] = copiedSchedule[hour];
            }

            TolkHelper.Speak($"Pasted schedule to {pawn.LabelShort} (pending)");
        }

        /// <summary>
        /// Updates clipboard with current cell information for screen reader.
        /// </summary>
        private static void UpdateClipboard()
        {
            if (pawns.Count == 0 || selectedPawnIndex < 0 || selectedPawnIndex >= pawns.Count)
            {
                TolkHelper.Speak("No pawns available");
                return;
            }

            Pawn pawn = pawns[selectedPawnIndex];
            if (pawn.timetable == null)
            {
                TolkHelper.Speak($"{pawn.LabelShort}: No schedule");
                return;
            }

            TimeAssignmentDef currentAssignment = pawn.timetable.GetAssignment(selectedHourIndex);

            // Check if this cell has pending changes
            bool hasPendingChange = pendingChanges.ContainsKey(pawn) &&
                                   pendingChanges[pawn].ContainsKey(selectedHourIndex);

            string pendingIndicator = hasPendingChange ? " (pending)" : "";
            string message = $"{pawn.LabelShort}, Hour {selectedHourIndex}: {currentAssignment.label}{pendingIndicator}";
            TolkHelper.Speak(message);
        }
    }
}
