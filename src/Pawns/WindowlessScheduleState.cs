using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Sound;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Column types for the schedule menu.
    /// </summary>
    public enum ScheduleColumnMode
    {
        Schedule,
        Areas
    }

    /// <summary>
    /// Manages the state and navigation for the windowless schedule/timetable interface.
    /// Uses TabularMenuHelper for standardized 2D grid navigation (pawns x 24 hours).
    /// Includes dual-column interface with Schedule and Allowed Areas columns.
    /// </summary>
    public static class WindowlessScheduleState
    {
        // === Core state ===
        private static bool isActive = false;
        private static TabularMenuHelper<Pawn> tableHelper;
        private static List<Pawn> pawns = new List<Pawn>();

        // === Brush state ===
        private static TimeAssignmentDef selectedAssignment = null;
        private static List<TimeAssignmentDef> availableAssignments = new List<TimeAssignmentDef>();

        // === Copy/paste ===
        private static List<TimeAssignmentDef> copiedSchedule = null;

        // === Areas column state ===
        private static ScheduleColumnMode currentColumn = ScheduleColumnMode.Schedule;
        private static List<object> availableAreas = new List<object>();
        private static int selectedAreaIndex = 0;
        private static readonly object ManageAreasSentinel = new object();
        private const int ManageAreasIndex = 0;
        private const int UnrestrictedIndex = 1;

        // === Properties ===
        public static bool IsActive => isActive;
        public static List<Pawn> Pawns => pawns;
        public static TimeAssignmentDef SelectedAssignment => selectedAssignment;
        public static ScheduleColumnMode CurrentColumn => currentColumn;
        public static bool IsInAreasColumn => currentColumn == ScheduleColumnMode.Areas;
        public static TypeaheadSearchHelper Typeahead => tableHelper?.Typeahead;
        public static int SelectedPawnIndex => tableHelper?.CurrentRowIndex ?? 0;
        public static int SelectedHourIndex => tableHelper?.CurrentColumnIndex ?? 0;

        /// <summary>
        /// Gets the localized schedule tab label from the game's MainButtonDef.
        /// </summary>
        private static string ScheduleTabLabel
        {
            get
            {
                var def = DefDatabase<MainButtonDef>.GetNamed("Schedule", errorOnFail: false);
                return def?.LabelCap ?? "Schedule";
            }
        }

        private static readonly SoundDef BulkPaintSound = SoundDefOf.Designate_DragStandard_Changed_NoCam;

        // === Column definition delegates for TabularMenuHelper ===

        private static string GetHourColumnName(int hourIndex)
        {
            return $"Hour {hourIndex}";
        }

        private static string GetCellValue(Pawn pawn, int hourIndex)
        {
            if (pawn.timetable == null) return "";
            return pawn.timetable.GetAssignment(hourIndex).LabelCap;
        }

        private static IList<Pawn> SortPawnsByColumn(IList<Pawn> items, int column, bool descending)
        {
            return descending
                ? items.OrderByDescending(p => p.LabelShort).ToList()
                : items.OrderBy(p => p.LabelShort).ToList();
        }

        // ===== Open / Close =====

        /// <summary>
        /// Opens the schedule menu. Initializes pawn list, brush, and TabularMenuHelper.
        /// </summary>
        public static void Open()
        {
            isActive = true;
            copiedSchedule = null;
            currentColumn = ScheduleColumnMode.Schedule;
            availableAreas.Clear();
            selectedAreaIndex = 0;

            // Build pawn list (colonists + controllable subhumans, excluding babies)
            pawns.Clear();
            if (Find.CurrentMap?.mapPawns?.FreeColonists != null)
            {
                pawns.AddRange(Find.CurrentMap.mapPawns.FreeColonists
                    .Where(p => !p.DevelopmentalStage.Baby()));
            }
            if (Find.CurrentMap?.mapPawns?.SpawnedColonyAnimals != null)
            {
                pawns.AddRange(Find.CurrentMap.mapPawns.SpawnedColonyAnimals
                    .Where(p => p.RaceProps.intelligence >= Intelligence.ToolUser));
            }
            pawns = pawns.OrderBy(p => p.LabelShort).ToList();

            // Build assignment list from game data (order matches vanilla UI)
            availableAssignments = DefDatabase<TimeAssignmentDef>.AllDefsListForReading.ToList();

            // Read default brush from game (persists within session, defaults to Work)
            selectedAssignment = TimeAssignmentSelector.selectedAssignment;
            if (selectedAssignment == null && availableAssignments.Count > 0)
                selectedAssignment = availableAssignments[0];

            // Initialize TabularMenuHelper for 2D grid navigation
            tableHelper = new TabularMenuHelper<Pawn>(
                getColumnCount: () => 24,
                getItemLabel: pawn => pawn.LabelShort,
                getColumnName: GetHourColumnName,
                getColumnValue: GetCellValue,
                sortByColumn: SortPawnsByColumn
            );
            tableHelper.Reset();

            // Set initial hour to current game hour
            tableHelper.CurrentColumnIndex = GenLocalDate.HourOfDay(Find.CurrentMap);

            // Focus selected pawn if any
            if (Find.Selector.SingleSelectedThing is Pawn selectedPawn)
            {
                int index = pawns.IndexOf(selectedPawn);
                if (index >= 0)
                    tableHelper.CurrentRowIndex = index;
            }

            AnnounceOpening();
        }

        /// <summary>
        /// Closes the schedule menu.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            pawns.Clear();
            copiedSchedule = null;
            availableAssignments.Clear();
            currentColumn = ScheduleColumnMode.Schedule;
            availableAreas.Clear();
            selectedAreaIndex = 0;

            TolkHelper.Speak(ScheduleTabLabel + " closed");
        }

        // ===== Announcements =====

        private static void AnnounceOpening()
        {
            var parts = new List<string>();

            // Menu name
            parts.Add(ScheduleTabLabel + ".");

            // Current position
            if (pawns.Count > 0)
                parts.Add(BuildCellAnnouncement());

            // Current brush
            if (selectedAssignment != null)
                parts.Add($"Currently selected brush: {selectedAssignment.LabelCap}.");

            // Brush selection help with number keys
            if (availableAssignments.Count > 0)
            {
                parts.Add("Press the following numbers to choose your brush:");
                for (int i = 0; i < availableAssignments.Count && i < 10; i++)
                {
                    int displayKey = (i + 1) % 10; // 1,2,3...9,0
                    parts.Add($"{displayKey}: {availableAssignments[i].LabelCap}.");
                }
            }

            // Usage instructions
            parts.Add("Press space to apply the active brush to the current cell.");
            parts.Add("Hold down the shift key and tap the arrow keys to paint the selected brush across multiple cells.");

            // Copy/paste shortcuts
            parts.Add($"{"Copy".Translate()} Ctrl+C, {"Paste".Translate()} Ctrl+V.");

            // Column switch hint
            parts.Add($"Tab for {"AllowedArea".Translate()}.");

            TolkHelper.Speak(string.Join(" ", parts));
        }

        /// <summary>
        /// Builds a full cell announcement with pawn name, hour, assignment, and position.
        /// </summary>
        private static string BuildCellAnnouncement()
        {
            if (pawns.Count == 0 || tableHelper.CurrentRowIndex < 0 || tableHelper.CurrentRowIndex >= pawns.Count)
                return "No pawns available";

            Pawn pawn = pawns[tableHelper.CurrentRowIndex];
            if (pawn.timetable == null)
                return $"{pawn.LabelShort}: No schedule";

            int hour = tableHelper.CurrentColumnIndex;
            TimeAssignmentDef assignment = pawn.timetable.GetAssignment(hour);

            string pawnPos = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, pawns.Count);
            string hourPos = MenuHelper.FormatPosition(hour, 24);

            string message = $"{pawn.LabelShort}, Hour {hour}: {assignment.LabelCap}";
            if (!string.IsNullOrEmpty(pawnPos) || !string.IsNullOrEmpty(hourPos))
                message += $". Pawn {pawnPos}, Hour {hourPos}.";

            // Add search context if active
            if (tableHelper.Typeahead.HasActiveSearch)
                message += $", match {tableHelper.Typeahead.CurrentMatchPosition} of {tableHelper.Typeahead.MatchCount} for '{tableHelper.Typeahead.SearchBuffer}'";

            return message;
        }

        private static void AnnounceCurrentCell()
        {
            TolkHelper.Speak(BuildCellAnnouncement());
        }

        private static void AnnouncePaint(Pawn pawn, int hour, TimeAssignmentDef assignment)
        {
            TolkHelper.Speak($"{pawn.LabelShort}, Hour {hour} set to {assignment.LabelCap}");
        }

        // ===== Brush Selection =====

        /// <summary>
        /// Selects a brush by index (0-based). Called from number keys 1-9,0 mapped to indices 0-9.
        /// </summary>
        public static void SelectBrush(int index)
        {
            if (index < 0 || index >= availableAssignments.Count)
            {
                int displayKey = (index + 1) % 10;
                TolkHelper.Speak($"No brush assigned to key {displayKey}");
                return;
            }

            selectedAssignment = availableAssignments[index];
            TimeAssignmentSelector.selectedAssignment = selectedAssignment;
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            TolkHelper.Speak(selectedAssignment.LabelCap);
        }

        // ===== Apply Brush =====

        /// <summary>
        /// Applies the selected brush to the current cell (Space / Enter in Schedule column).
        /// </summary>
        public static void ApplyBrush()
        {
            if (pawns.Count == 0 || tableHelper.CurrentRowIndex < 0 || tableHelper.CurrentRowIndex >= pawns.Count)
                return;
            if (selectedAssignment == null)
                return;

            Pawn pawn = pawns[tableHelper.CurrentRowIndex];
            if (pawn.timetable == null)
                return;

            int hour = tableHelper.CurrentColumnIndex;
            TimeAssignmentDef currentAssignment = pawn.timetable.GetAssignment(hour);

            if (currentAssignment != selectedAssignment)
            {
                pawn.timetable.SetAssignment(hour, selectedAssignment);
                SoundDefOf.Designate_DragStandard_Changed_NoCam.PlayOneShotOnCamera();
                AnnounceCurrentCell();
            }
            else
            {
                TolkHelper.Speak($"Hour {hour}: already set to {selectedAssignment.LabelCap}");
            }
        }

        // ===== Navigation =====

        /// <summary>
        /// Moves to the previous pawn (Up arrow). Works in both columns.
        /// </summary>
        public static void MoveUp()
        {
            if (pawns.Count == 0) return;

            tableHelper.SelectPreviousRow(pawns.Count);

            if (currentColumn == ScheduleColumnMode.Areas)
            {
                SyncAreaIndexToCurrentPawn();
                AnnounceAreaSelection();
            }
            else
            {
                AnnounceCurrentCell();
            }
        }

        /// <summary>
        /// Moves to the next pawn (Down arrow). Works in both columns.
        /// </summary>
        public static void MoveDown()
        {
            if (pawns.Count == 0) return;

            tableHelper.SelectNextRow(pawns.Count);

            if (currentColumn == ScheduleColumnMode.Areas)
            {
                SyncAreaIndexToCurrentPawn();
                AnnounceAreaSelection();
            }
            else
            {
                AnnounceCurrentCell();
            }
        }

        /// <summary>
        /// Moves to the previous hour (Left arrow, Schedule column only).
        /// </summary>
        public static void MoveLeft()
        {
            tableHelper.SelectPreviousColumn();
            AnnounceCurrentCell();
        }

        /// <summary>
        /// Moves to the next hour (Right arrow, Schedule column only).
        /// </summary>
        public static void MoveRight()
        {
            tableHelper.SelectNextColumn();
            AnnounceCurrentCell();
        }

        /// <summary>
        /// Jumps to hour 0 (Home key, Schedule column).
        /// </summary>
        public static void JumpToFirstHour()
        {
            tableHelper.CurrentColumnIndex = 0;
            AnnounceCurrentCell();
        }

        /// <summary>
        /// Jumps to hour 23 (End key, Schedule column).
        /// </summary>
        public static void JumpToLastHour()
        {
            tableHelper.CurrentColumnIndex = 23;
            AnnounceCurrentCell();
        }

        // ===== Painting (Shift+Arrows) =====

        /// <summary>
        /// Moves up and paints the brush (Shift+Up, Schedule column).
        /// </summary>
        public static void PaintUp()
        {
            if (pawns.Count == 0 || selectedAssignment == null) return;
            tableHelper.SelectPreviousRow(pawns.Count);
            ApplyBrushAndAnnouncePaint();
        }

        /// <summary>
        /// Moves down and paints the brush (Shift+Down, Schedule column).
        /// </summary>
        public static void PaintDown()
        {
            if (pawns.Count == 0 || selectedAssignment == null) return;
            tableHelper.SelectNextRow(pawns.Count);
            ApplyBrushAndAnnouncePaint();
        }

        /// <summary>
        /// Moves left and paints the brush (Shift+Left, Schedule column).
        /// </summary>
        public static void PaintLeft()
        {
            if (selectedAssignment == null) return;
            tableHelper.SelectPreviousColumn();
            ApplyBrushAndAnnouncePaint();
        }

        /// <summary>
        /// Moves right and paints the brush (Shift+Right, Schedule column).
        /// </summary>
        public static void PaintRight()
        {
            if (selectedAssignment == null) return;
            tableHelper.SelectNextColumn();
            ApplyBrushAndAnnouncePaint();
        }

        private static void ApplyBrushAndAnnouncePaint()
        {
            if (tableHelper.CurrentRowIndex < 0 || tableHelper.CurrentRowIndex >= pawns.Count)
                return;

            Pawn pawn = pawns[tableHelper.CurrentRowIndex];
            if (pawn.timetable == null) return;

            int hour = tableHelper.CurrentColumnIndex;
            TimeAssignmentDef currentAssignment = pawn.timetable.GetAssignment(hour);

            if (currentAssignment != selectedAssignment)
            {
                pawn.timetable.SetAssignment(hour, selectedAssignment);
                SoundDefOf.Designate_DragStandard_Changed_NoCam.PlayOneShotOnCamera();
                AnnouncePaint(pawn, hour, selectedAssignment);
            }
            else
            {
                TolkHelper.Speak($"{pawn.LabelShort}, Hour {hour}: already set to {selectedAssignment.LabelCap}");
            }
        }

        // ===== Bulk Painting (Shift+Home/End, Ctrl+Shift+Home/End) =====

        /// <summary>
        /// Groups a sorted list of hour indices into consecutive ranges and formats them
        /// (e.g., [0,1,2,5,7,8,9] becomes "hours 0 through 2, 5, and 7 through 9").
        /// </summary>
        private static string FormatChangedHourRanges(List<int> hours)
        {
            if (hours.Count == 0) return "";

            var ranges = new List<string>();
            int rangeStart = hours[0];
            int rangeEnd = hours[0];

            for (int i = 1; i < hours.Count; i++)
            {
                if (hours[i] == rangeEnd + 1)
                {
                    rangeEnd = hours[i];
                }
                else
                {
                    ranges.Add(rangeStart == rangeEnd ? $"{rangeStart}" : $"{rangeStart} through {rangeEnd}");
                    rangeStart = hours[i];
                    rangeEnd = hours[i];
                }
            }
            ranges.Add(rangeStart == rangeEnd ? $"{rangeStart}" : $"{rangeStart} through {rangeEnd}");

            string label = hours.Count == 1 ? "hour" : "hours";
            return $"{label} {MenuHelper.FormatNameList(ranges)}";
        }

        /// <summary>
        /// Paints from current hour to hour 0 for the current pawn (Shift+Home).
        /// </summary>
        public static void PaintToFirstHour()
        {
            if (pawns.Count == 0 || selectedAssignment == null) return;
            if (tableHelper.CurrentRowIndex < 0 || tableHelper.CurrentRowIndex >= pawns.Count) return;

            Pawn pawn = pawns[tableHelper.CurrentRowIndex];
            if (pawn.timetable == null) return;

            int startHour = tableHelper.CurrentColumnIndex;
            var changedHours = new List<int>();

            for (int h = startHour; h >= 0; h--)
            {
                if (pawn.timetable.GetAssignment(h) != selectedAssignment)
                {
                    pawn.timetable.SetAssignment(h, selectedAssignment);
                    changedHours.Add(h);
                }
            }

            tableHelper.CurrentColumnIndex = 0;
            BulkSoundQueue.Queue(changedHours.Count, BulkPaintSound);

            if (changedHours.Count > 0)
            {
                changedHours.Sort();
                TolkHelper.Speak($"Painted {selectedAssignment.LabelCap} on {FormatChangedHourRanges(changedHours)} for {pawn.LabelShort}");
            }
            else
            {
                string hourRange = startHour == 0 ? "Hour 0" : $"Hours 0 through {startHour}";
                TolkHelper.Speak($"{hourRange} already set to {selectedAssignment.LabelCap} for {pawn.LabelShort}");
            }
        }

        /// <summary>
        /// Paints from current hour to hour 23 for the current pawn (Shift+End).
        /// </summary>
        public static void PaintToLastHour()
        {
            if (pawns.Count == 0 || selectedAssignment == null) return;
            if (tableHelper.CurrentRowIndex < 0 || tableHelper.CurrentRowIndex >= pawns.Count) return;

            Pawn pawn = pawns[tableHelper.CurrentRowIndex];
            if (pawn.timetable == null) return;

            int startHour = tableHelper.CurrentColumnIndex;
            var changedHours = new List<int>();

            for (int h = startHour; h <= 23; h++)
            {
                if (pawn.timetable.GetAssignment(h) != selectedAssignment)
                {
                    pawn.timetable.SetAssignment(h, selectedAssignment);
                    changedHours.Add(h);
                }
            }

            tableHelper.CurrentColumnIndex = 23;
            BulkSoundQueue.Queue(changedHours.Count, BulkPaintSound);

            if (changedHours.Count > 0)
            {
                TolkHelper.Speak($"Painted {selectedAssignment.LabelCap} on {FormatChangedHourRanges(changedHours)} for {pawn.LabelShort}");
            }
            else
            {
                string hourRange = startHour == 23 ? "Hour 23" : $"Hours {startHour} through 23";
                TolkHelper.Speak($"{hourRange} already set to {selectedAssignment.LabelCap} for {pawn.LabelShort}");
            }
        }

        /// <summary>
        /// Paints from current pawn to first pawn at the current hour (Ctrl+Shift+Home).
        /// </summary>
        public static void PaintToFirstPawn()
        {
            if (pawns.Count == 0 || selectedAssignment == null) return;
            if (tableHelper.CurrentRowIndex < 0 || tableHelper.CurrentRowIndex >= pawns.Count) return;

            int hour = tableHelper.CurrentColumnIndex;
            int startIndex = tableHelper.CurrentRowIndex;
            var changedNames = new List<string>();

            for (int i = 0; i <= startIndex; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn.timetable == null) continue;

                if (pawn.timetable.GetAssignment(hour) != selectedAssignment)
                {
                    pawn.timetable.SetAssignment(hour, selectedAssignment);
                    changedNames.Add(pawn.LabelShort);
                }
            }

            int savedColumn = tableHelper.CurrentColumnIndex;
            tableHelper.JumpToFirst(pawns.Count);
            tableHelper.CurrentColumnIndex = savedColumn;

            BulkSoundQueue.Queue(changedNames.Count, BulkPaintSound);

            if (changedNames.Count > 0)
            {
                TolkHelper.Speak($"Painted {selectedAssignment.LabelCap} on hour {hour} for {MenuHelper.FormatNameList(changedNames)}");
            }
            else
            {
                TolkHelper.Speak($"Hour {hour} already set to {selectedAssignment.LabelCap} for all pawns");
            }
        }

        /// <summary>
        /// Paints from current pawn to last pawn at the current hour (Ctrl+Shift+End).
        /// </summary>
        public static void PaintToLastPawn()
        {
            if (pawns.Count == 0 || selectedAssignment == null) return;
            if (tableHelper.CurrentRowIndex < 0 || tableHelper.CurrentRowIndex >= pawns.Count) return;

            int hour = tableHelper.CurrentColumnIndex;
            int startIndex = tableHelper.CurrentRowIndex;
            var changedNames = new List<string>();

            for (int i = startIndex; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn.timetable == null) continue;

                if (pawn.timetable.GetAssignment(hour) != selectedAssignment)
                {
                    pawn.timetable.SetAssignment(hour, selectedAssignment);
                    changedNames.Add(pawn.LabelShort);
                }
            }

            int savedColumn = tableHelper.CurrentColumnIndex;
            tableHelper.JumpToLast(pawns.Count);
            tableHelper.CurrentColumnIndex = savedColumn;

            BulkSoundQueue.Queue(changedNames.Count, BulkPaintSound);

            if (changedNames.Count > 0)
            {
                TolkHelper.Speak($"Painted {selectedAssignment.LabelCap} on hour {hour} for {MenuHelper.FormatNameList(changedNames)}");
            }
            else
            {
                TolkHelper.Speak($"Hour {hour} already set to {selectedAssignment.LabelCap} for all pawns");
            }
        }

        // ===== Copy / Paste =====

        /// <summary>
        /// Copies the current pawn's entire schedule to clipboard (Ctrl+C).
        /// </summary>
        public static void CopySchedule()
        {
            if (pawns.Count == 0 || tableHelper.CurrentRowIndex < 0 || tableHelper.CurrentRowIndex >= pawns.Count)
                return;

            Pawn pawn = pawns[tableHelper.CurrentRowIndex];
            if (pawn.timetable == null) return;

            copiedSchedule = new List<TimeAssignmentDef>();
            for (int hour = 0; hour < 24; hour++)
            {
                copiedSchedule.Add(pawn.timetable.GetAssignment(hour));
            }

            TolkHelper.Speak($"{"Copy".Translate()}: {pawn.LabelShort}");
        }

        /// <summary>
        /// Pastes the copied schedule to the current pawn (Ctrl+V).
        /// </summary>
        public static void PasteSchedule()
        {
            if (copiedSchedule == null)
            {
                TolkHelper.Speak("No schedule copied");
                return;
            }

            if (pawns.Count == 0 || tableHelper.CurrentRowIndex < 0 || tableHelper.CurrentRowIndex >= pawns.Count)
                return;

            Pawn pawn = pawns[tableHelper.CurrentRowIndex];
            if (pawn.timetable == null) return;

            for (int hour = 0; hour < 24; hour++)
            {
                pawn.timetable.SetAssignment(hour, copiedSchedule[hour]);
            }

            TolkHelper.Speak($"{"Paste".Translate()}: {pawn.LabelShort}");
        }

        // ===== Typeahead Search =====

        /// <summary>
        /// Handles character input for typeahead pawn name search (letter keys).
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (tableHelper.HandleTypeahead(c, pawns, out _))
            {
                if (currentColumn == ScheduleColumnMode.Areas)
                {
                    SyncAreaIndexToCurrentPawn();
                    AnnounceAreaSelection();
                }
                else
                {
                    AnnounceCurrentCell();
                }
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

            tableHelper.HandleBackspace(pawns, out _);

            if (currentColumn == ScheduleColumnMode.Areas)
            {
                SyncAreaIndexToCurrentPawn();
                AnnounceAreaSelection();
            }
            else
            {
                AnnounceCurrentCell();
            }
        }

        // ===== Column Switching =====

        /// <summary>
        /// Switches between Schedule and Areas columns (Tab / Shift+Tab).
        /// </summary>
        public static void SwitchColumn()
        {
            currentColumn = currentColumn == ScheduleColumnMode.Schedule
                ? ScheduleColumnMode.Areas
                : ScheduleColumnMode.Schedule;

            tableHelper.ClearSearch();

            if (currentColumn == ScheduleColumnMode.Areas)
            {
                LoadAvailableAreas();
                SyncAreaIndexToCurrentPawn();
            }

            AnnounceColumnSwitch();
        }

        private static void AnnounceColumnSwitch()
        {
            if (currentColumn == ScheduleColumnMode.Schedule)
            {
                var parts = new List<string> { ScheduleTabLabel + "." };
                if (selectedAssignment != null)
                    parts.Add($"Currently selected brush: {selectedAssignment.LabelCap}.");
                parts.Add(BuildCellAnnouncement());
                TolkHelper.Speak(string.Join(" ", parts));
            }
            else
            {
                if (pawns.Count == 0 || tableHelper.CurrentRowIndex < 0 || tableHelper.CurrentRowIndex >= pawns.Count)
                {
                    TolkHelper.Speak($"{"AllowedArea".Translate()}. No pawns available");
                    return;
                }

                Pawn pawn = pawns[tableHelper.CurrentRowIndex];
                string areaName = GetAreaName(selectedAreaIndex);
                string pawnPos = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, pawns.Count);
                string areaPos = MenuHelper.FormatPosition(selectedAreaIndex, availableAreas.Count);
                TolkHelper.Speak($"{"AllowedArea".Translate()}. {pawn.LabelShort}: {areaName}. Pawn {pawnPos}, Area {areaPos}");
            }
        }

        // ===== Areas Column =====

        /// <summary>
        /// Returns true if currently focused on "Manage Areas" option.
        /// </summary>
        public static bool IsOnManageAreas()
        {
            return currentColumn == ScheduleColumnMode.Areas && selectedAreaIndex == ManageAreasIndex;
        }

        /// <summary>
        /// Loads available areas from the current map's area manager.
        /// Order: ManageAreasSentinel, null (Unrestricted), then Area objects.
        /// </summary>
        private static void LoadAvailableAreas()
        {
            availableAreas.Clear();
            availableAreas.Add(ManageAreasSentinel);
            availableAreas.Add(null); // Unrestricted

            if (Find.CurrentMap?.areaManager != null)
            {
                var areas = Find.CurrentMap.areaManager.AllAreas
                    .Where(a => a.AssignableAsAllowed());
                availableAreas.AddRange(areas);
            }
        }

        /// <summary>
        /// Syncs selectedAreaIndex to match current pawn's assigned area.
        /// </summary>
        private static void SyncAreaIndexToCurrentPawn()
        {
            if (pawns.Count == 0 || tableHelper.CurrentRowIndex < 0 || tableHelper.CurrentRowIndex >= pawns.Count)
                return;

            Pawn pawn = pawns[tableHelper.CurrentRowIndex];
            Area currentArea = pawn.playerSettings?.AreaRestrictionInPawnCurrentMap;

            if (currentArea == null)
            {
                selectedAreaIndex = UnrestrictedIndex;
            }
            else
            {
                int index = availableAreas.IndexOf(currentArea);
                selectedAreaIndex = index >= 0 ? index : UnrestrictedIndex;
            }
        }

        /// <summary>
        /// Gets a localized area name for the given index.
        /// </summary>
        private static string GetAreaName(int index)
        {
            if (index < 0 || index >= availableAreas.Count)
                return "NoAreaAllowed".Translate();

            object focused = availableAreas[index];
            if (focused == ManageAreasSentinel)
                return "ManageAreas".Translate();
            if (focused == null)
                return "NoAreaAllowed".Translate();
            if (focused is Area area)
                return area.Label;
            return "NoAreaAllowed".Translate();
        }

        /// <summary>
        /// Selects the next area focus (Right arrow in Areas column).
        /// </summary>
        public static void SelectNextArea()
        {
            if (currentColumn != ScheduleColumnMode.Areas || availableAreas.Count == 0)
                return;

            selectedAreaIndex = MenuHelper.SelectNext(selectedAreaIndex, availableAreas.Count);
            AnnounceAreaFocus();
        }

        /// <summary>
        /// Selects the previous area focus (Left arrow in Areas column).
        /// </summary>
        public static void SelectPreviousArea()
        {
            if (currentColumn != ScheduleColumnMode.Areas || availableAreas.Count == 0)
                return;

            selectedAreaIndex = MenuHelper.SelectPrevious(selectedAreaIndex, availableAreas.Count);
            AnnounceAreaFocus();
        }

        /// <summary>
        /// Announces the currently focused area.
        /// </summary>
        private static void AnnounceAreaFocus()
        {
            if (availableAreas.Count == 0 || selectedAreaIndex < 0 || selectedAreaIndex >= availableAreas.Count)
                return;

            string areaName = GetAreaName(selectedAreaIndex);
            string position = MenuHelper.FormatPosition(selectedAreaIndex, availableAreas.Count);
            TolkHelper.Speak($"{areaName}. {position}");
        }

        /// <summary>
        /// Confirms the currently focused area selection (Enter key in Areas column).
        /// If on ManageAreas, opens WindowlessAreaState.
        /// Otherwise applies the focused area to the current pawn.
        /// </summary>
        public static void ConfirmAreaSelection()
        {
            if (currentColumn != ScheduleColumnMode.Areas)
                return;

            if (selectedAreaIndex == ManageAreasIndex)
            {
                OpenManageAreas();
                return;
            }

            ApplyAreaToCurrentPawn();
        }

        /// <summary>
        /// Opens the Manage Areas screen.
        /// </summary>
        public static void OpenManageAreas()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;

            int savedPawnIndex = tableHelper.CurrentRowIndex;

            Action returnToScheduleCallback = () => {
                LoadAvailableAreas();
                selectedAreaIndex = ManageAreasIndex;
                tableHelper.CurrentRowIndex = savedPawnIndex;
                AnnounceAreaFocus();
            };

            Action reopenManageAreasCallback = () => {
                Find.DesignatorManager?.Deselect();

                if (map != null && Find.CurrentMap == map)
                {
                    WindowlessAreaState.Open(map, returnToScheduleCallback);
                }
                else
                {
                    returnToScheduleCallback();
                }
            };

            WindowlessAreaState.Open(map, reopenManageAreasCallback);
        }

        /// <summary>
        /// Applies the currently focused area to the current pawn.
        /// </summary>
        private static void ApplyAreaToCurrentPawn()
        {
            if (pawns.Count == 0 || tableHelper.CurrentRowIndex < 0 || tableHelper.CurrentRowIndex >= pawns.Count)
                return;

            if (selectedAreaIndex == ManageAreasIndex)
                return;

            Pawn pawn = pawns[tableHelper.CurrentRowIndex];
            if (pawn.playerSettings == null)
                return;

            object focused = availableAreas[selectedAreaIndex];
            Area area = focused as Area;

            pawn.playerSettings.AreaRestrictionInPawnCurrentMap = area;

            string areaName = area?.Label ?? "NoAreaAllowed".Translate();
            string position = MenuHelper.FormatPosition(selectedAreaIndex, availableAreas.Count);
            TolkHelper.Speak($"{pawn.LabelShort}: {areaName} applied. {position}");
        }

        /// <summary>
        /// Applies the source pawn's actual assigned area to the pawn below and moves down (Shift+Down in Areas column).
        /// </summary>
        public static void ApplyAreaToPawnBelow()
        {
            if (currentColumn != ScheduleColumnMode.Areas || pawns.Count <= 1)
                return;

            Pawn sourcePawn = pawns[tableHelper.CurrentRowIndex];
            Area sourceArea = sourcePawn.playerSettings?.AreaRestrictionInPawnCurrentMap;

            tableHelper.SelectNextRow(pawns.Count);

            Pawn targetPawn = pawns[tableHelper.CurrentRowIndex];
            if (targetPawn.playerSettings != null)
            {
                targetPawn.playerSettings.AreaRestrictionInPawnCurrentMap = sourceArea;
            }

            SyncAreaIndexToCurrentPawn();

            string areaName = sourceArea?.Label ?? "NoAreaAllowed".Translate();
            string pawnPosition = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, pawns.Count);
            TolkHelper.Speak($"{targetPawn.LabelShort}: {areaName} applied. Pawn {pawnPosition}");
        }

        /// <summary>
        /// Applies the source pawn's actual assigned area to the pawn above and moves up (Shift+Up in Areas column).
        /// </summary>
        public static void ApplyAreaToPawnAbove()
        {
            if (currentColumn != ScheduleColumnMode.Areas || pawns.Count <= 1)
                return;

            Pawn sourcePawn = pawns[tableHelper.CurrentRowIndex];
            Area sourceArea = sourcePawn.playerSettings?.AreaRestrictionInPawnCurrentMap;

            tableHelper.SelectPreviousRow(pawns.Count);

            Pawn targetPawn = pawns[tableHelper.CurrentRowIndex];
            if (targetPawn.playerSettings != null)
            {
                targetPawn.playerSettings.AreaRestrictionInPawnCurrentMap = sourceArea;
            }

            SyncAreaIndexToCurrentPawn();

            string areaName = sourceArea?.Label ?? "NoAreaAllowed".Translate();
            string pawnPosition = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, pawns.Count);
            TolkHelper.Speak($"{targetPawn.LabelShort}: {areaName} applied. Pawn {pawnPosition}");
        }

        /// <summary>
        /// Announces current area selection (pawn's assigned area and position).
        /// </summary>
        private static void AnnounceAreaSelection()
        {
            if (pawns.Count == 0 || tableHelper.CurrentRowIndex < 0 || tableHelper.CurrentRowIndex >= pawns.Count)
            {
                TolkHelper.Speak("No pawns available");
                return;
            }

            Pawn pawn = pawns[tableHelper.CurrentRowIndex];
            string areaName = GetAreaName(selectedAreaIndex);
            string pawnPos = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, pawns.Count);
            string areaPos = MenuHelper.FormatPosition(selectedAreaIndex, availableAreas.Count);

            TolkHelper.Speak($"{pawn.LabelShort}: {areaName}. Pawn {pawnPos}, Area {areaPos}");
        }

        /// <summary>
        /// Jumps to first pawn, maintaining current column (Ctrl+Home).
        /// </summary>
        public static void JumpToFirstPawn()
        {
            if (pawns.Count == 0) return;
            int savedColumn = tableHelper.CurrentColumnIndex;
            tableHelper.JumpToFirst(pawns.Count);
            tableHelper.CurrentColumnIndex = savedColumn;
            if (currentColumn == ScheduleColumnMode.Areas)
            {
                SyncAreaIndexToCurrentPawn();
                AnnounceAreaSelection();
            }
            else
            {
                AnnounceCurrentCell();
            }
        }

        /// <summary>
        /// Jumps to last pawn, maintaining current column (Ctrl+End).
        /// </summary>
        public static void JumpToLastPawn()
        {
            if (pawns.Count == 0) return;
            int savedColumn = tableHelper.CurrentColumnIndex;
            tableHelper.JumpToLast(pawns.Count);
            tableHelper.CurrentColumnIndex = savedColumn;
            if (currentColumn == ScheduleColumnMode.Areas)
            {
                SyncAreaIndexToCurrentPawn();
                AnnounceAreaSelection();
            }
            else
            {
                AnnounceCurrentCell();
            }
        }

        /// <summary>
        /// Opens context menu with bulk area operations.
        /// </summary>
        public static void OpenAreaContextMenu()
        {
            if (currentColumn != ScheduleColumnMode.Areas)
            {
                TolkHelper.Speak("Context menu only available in Areas column");
                return;
            }

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Set All to Home Area", () => SetAllToHomeArea()),
                new FloatMenuOption("Clear All Allowed Areas", () => ClearAllAreas())
            };

            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        private static void SetAllToHomeArea()
        {
            Area homeArea = Find.CurrentMap?.areaManager?.Home;
            if (homeArea == null)
            {
                TolkHelper.Speak("No home area defined");
                return;
            }

            int count = 0;
            foreach (var pawn in pawns)
            {
                if (pawn.playerSettings != null && pawn.playerSettings.SupportsAllowedAreas)
                {
                    pawn.playerSettings.AreaRestrictionInPawnCurrentMap = homeArea;
                    count++;
                }
            }

            selectedAreaIndex = availableAreas.IndexOf(homeArea);
            if (selectedAreaIndex < 0) selectedAreaIndex = 0;

            TolkHelper.Speak($"Set {count} pawns to Home area");
        }

        private static void ClearAllAreas()
        {
            int count = 0;
            foreach (var pawn in pawns)
            {
                if (pawn.playerSettings != null && pawn.playerSettings.SupportsAllowedAreas)
                {
                    pawn.playerSettings.AreaRestrictionInPawnCurrentMap = null;
                    count++;
                }
            }

            selectedAreaIndex = UnrestrictedIndex;
            TolkHelper.Speak($"Cleared allowed areas for {count} pawns");
        }
    }
}
