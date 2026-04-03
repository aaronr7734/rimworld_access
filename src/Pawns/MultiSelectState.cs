using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages multi-selection of map pawns for accessibility.
    /// Tracks which pawns are selected, syncs with Find.Selector, and provides
    /// focus-only navigation when multi-select is active.
    ///
    /// Follows the same pattern as WorldNavigationState's caravan multi-select
    /// (HashSet tracking, toggle, sync with game selector, validate/cleanup).
    /// </summary>
    public static class MultiSelectState
    {
        private static HashSet<Pawn> selectedPawns = new HashSet<Pawn>();
        private static Pawn focusedPawn = null;

        /// <summary>
        /// Whether multi-select mode is active (more than one pawn selected,
        /// or at least one pawn toggled via Alt+Space).
        /// </summary>
        public static bool IsMultiSelectActive { get; private set; }

        /// <summary>
        /// The currently focused pawn (bar cursor position).
        /// In multi-select mode, focus can move without changing the selection set.
        /// </summary>
        public static Pawn FocusedPawn => focusedPawn;

        /// <summary>
        /// Number of currently selected pawns.
        /// </summary>
        public static int SelectedCount => selectedPawns.Count;

        /// <summary>
        /// Gets a read-only view of the selected pawns.
        /// </summary>
        public static IReadOnlyCollection<Pawn> SelectedPawns => selectedPawns;

        /// <summary>
        /// Checks whether a specific pawn is in the multi-selection.
        /// </summary>
        public static bool IsPawnSelected(Pawn pawn)
        {
            return pawn != null && selectedPawns.Contains(pawn);
        }

        /// <summary>
        /// Toggles a pawn in/out of the multi-selection (Alt+Space).
        /// If this is the first toggle on a single-selected pawn, enters multi-select mode.
        /// </summary>
        public static void TogglePawn(Pawn pawn)
        {
            if (pawn == null)
            {
                TolkHelper.Speak("No pawn focused");
                return;
            }

            ValidateAndCleanupSelection();

            if (selectedPawns.Contains(pawn))
            {
                selectedPawns.Remove(pawn);
                if (selectedPawns.Count == 0)
                {
                    // Removed the last pawn — exit multi-select, single-select this pawn
                    IsMultiSelectActive = false;
                    focusedPawn = pawn;
                    SingleSelectFocusedPawn();
                    TolkHelper.Speak($"{pawn.LabelShort} deselected. Selection cleared.");
                }
                else if (selectedPawns.Count == 1)
                {
                    // Down to 1 pawn — exit multi-select, single-select the remaining pawn
                    IsMultiSelectActive = false;
                    focusedPawn = selectedPawns.First();
                    SingleSelectFocusedPawn();
                    TolkHelper.Speak($"{pawn.LabelShort} removed. {focusedPawn.LabelShort} selected.");
                }
                else
                {
                    SyncSelectionWithGame();
                    TolkHelper.Speak($"{pawn.LabelShort} removed. {selectedPawns.Count} selected.");
                }
            }
            else
            {
                // If not in multi-select mode yet, add the currently selected pawn first
                if (!IsMultiSelectActive)
                {
                    var currentlySelected = Find.Selector?.SingleSelectedThing as Pawn;
                    if (currentlySelected != null && currentlySelected != pawn)
                    {
                        selectedPawns.Add(currentlySelected);
                    }
                }

                selectedPawns.Add(pawn);
                IsMultiSelectActive = selectedPawns.Count > 1;
                SyncSelectionWithGame();
                TolkHelper.Speak($"{pawn.LabelShort} added. {selectedPawns.Count} selected.");
            }
        }

        /// <summary>
        /// Adds a pawn to the selection without toggle behavior (used by contiguous selection).
        /// </summary>
        public static void AddPawn(Pawn pawn)
        {
            if (pawn == null || selectedPawns.Contains(pawn))
                return;

            selectedPawns.Add(pawn);
            IsMultiSelectActive = true;
            SyncSelectionWithGame();
        }

        /// <summary>
        /// Extends the selection contiguously to the next pawn on the colonist bar (Alt+Shift+Right).
        /// If starting from single-select, adds the current pawn first.
        /// </summary>
        public static void SelectContiguousNext()
        {
            if (Find.CurrentMap == null)
                return;

            ValidateAndCleanupSelection();

            // If not in multi-select, add the currently selected pawn
            if (!IsMultiSelectActive)
            {
                var currentPawn = Find.Selector?.SingleSelectedThing as Pawn;
                if (currentPawn == null)
                {
                    TolkHelper.Speak("No pawn selected");
                    return;
                }
                selectedPawns.Add(currentPawn);
                focusedPawn = currentPawn;
                ColonistBarState.SyncBarPosition(currentPawn);
            }

            // Navigate focus to next pawn
            Pawn nextPawn = ColonistBarState.NavigateFocusRight();
            if (nextPawn == null)
                return;

            focusedPawn = nextPawn;
            bool alreadySelected = selectedPawns.Contains(nextPawn);
            AddPawn(nextPawn);
            IsMultiSelectActive = true;

            if (!alreadySelected)
            {
                TolkHelper.Speak($"{nextPawn.LabelShort} added. {selectedPawns.Count} selected.");
            }
            else
            {
                TolkHelper.Speak($"{nextPawn.LabelShort}, already selected. {selectedPawns.Count} selected.");
            }
        }

        /// <summary>
        /// Extends the selection contiguously to the previous pawn on the colonist bar (Alt+Shift+Left).
        /// If starting from single-select, adds the current pawn first.
        /// </summary>
        public static void SelectContiguousPrevious()
        {
            if (Find.CurrentMap == null)
                return;

            ValidateAndCleanupSelection();

            // If not in multi-select, add the currently selected pawn
            if (!IsMultiSelectActive)
            {
                var currentPawn = Find.Selector?.SingleSelectedThing as Pawn;
                if (currentPawn == null)
                {
                    TolkHelper.Speak("No pawn selected");
                    return;
                }
                selectedPawns.Add(currentPawn);
                focusedPawn = currentPawn;
                ColonistBarState.SyncBarPosition(currentPawn);
            }

            // Navigate focus to previous pawn
            Pawn prevPawn = ColonistBarState.NavigateFocusLeft();
            if (prevPawn == null)
                return;

            focusedPawn = prevPawn;
            bool alreadySelected = selectedPawns.Contains(prevPawn);
            AddPawn(prevPawn);
            IsMultiSelectActive = true;

            if (!alreadySelected)
            {
                TolkHelper.Speak($"{prevPawn.LabelShort} added. {selectedPawns.Count} selected.");
            }
            else
            {
                TolkHelper.Speak($"{prevPawn.LabelShort}, already selected. {selectedPawns.Count} selected.");
            }
        }

        /// <summary>
        /// Navigates focus to the next pawn without changing the selection (comma/period or Alt+Right in multi-select mode).
        /// Camera does NOT follow. Announces name, selected status, job, and position.
        /// </summary>
        public static void NavigateFocusNext()
        {
            Pawn pawn = ColonistBarState.NavigateFocusRight();
            if (pawn == null)
                return;

            focusedPawn = pawn;
            AnnounceFocusedPawn(pawn);
        }

        /// <summary>
        /// Navigates focus to the previous pawn without changing the selection.
        /// </summary>
        public static void NavigateFocusPrevious()
        {
            Pawn pawn = ColonistBarState.NavigateFocusLeft();
            if (pawn == null)
                return;

            focusedPawn = pawn;
            AnnounceFocusedPawn(pawn);
        }

        /// <summary>
        /// Clears multi-select and returns to single-select of the focused pawn (Alt+Escape).
        /// </summary>
        public static void ClearMultiSelect()
        {
            if (!IsMultiSelectActive)
            {
                TolkHelper.Speak("No multi-selection active");
                return;
            }

            selectedPawns.Clear();
            IsMultiSelectActive = false;
            SingleSelectFocusedPawn();

            // Announce the focused pawn with full info, same as normal navigation
            if (focusedPawn != null)
            {
                string task = focusedPawn.GetJobReport();
                if (string.IsNullOrEmpty(task))
                    task = "Idle";
                TolkHelper.Speak($"{focusedPawn.LabelShort} - {task}");
            }
        }

        /// <summary>
        /// Selects all colonists on the current map (Alt+Ctrl+Space when majority unselected).
        /// </summary>
        public static void SelectAllColonists(List<Pawn> allColonists)
        {
            if (allColonists == null || allColonists.Count == 0)
            {
                TolkHelper.Speak("No colonists on this map");
                return;
            }

            selectedPawns.Clear();
            foreach (var pawn in allColonists)
            {
                selectedPawns.Add(pawn);
            }

            IsMultiSelectActive = selectedPawns.Count > 1;
            if (selectedPawns.Count > 0)
            {
                focusedPawn = selectedPawns.First();
                ColonistBarState.SyncBarPosition(focusedPawn);
            }
            SyncSelectionWithGame();

            if (selectedPawns.Count <= 5)
            {
                string names = MenuHelper.FormatNameList(
                    selectedPawns.Select(p => p.LabelShort).ToList());
                TolkHelper.Speak($"All selected: {names}. {selectedPawns.Count} colonists.");
            }
            else
            {
                TolkHelper.Speak($"All {selectedPawns.Count} colonists selected");
            }
        }

        /// <summary>
        /// Activates multi-select with the given set of pawns.
        /// Used by group recall (Ctrl+F1-F5).
        /// </summary>
        public static void SetSelection(IEnumerable<Pawn> pawns)
        {
            selectedPawns.Clear();
            foreach (var pawn in pawns)
            {
                if (pawn != null && !pawn.Destroyed && pawn.Spawned && pawn.Map == Find.CurrentMap)
                {
                    selectedPawns.Add(pawn);
                }
            }

            if (selectedPawns.Count > 0)
            {
                IsMultiSelectActive = selectedPawns.Count > 1;
                focusedPawn = selectedPawns.First();
                ColonistBarState.SyncBarPosition(focusedPawn);
                SyncSelectionWithGame();
            }
            else
            {
                IsMultiSelectActive = false;
            }
        }

        /// <summary>
        /// Syncs our HashSet selection with RimWorld's Find.Selector.
        /// Called after every selection change.
        /// </summary>
        private static void SyncSelectionWithGame()
        {
            if (Find.Selector == null)
                return;

            Find.Selector.ClearSelection();
            foreach (var pawn in selectedPawns)
            {
                if (pawn != null && !pawn.Destroyed && pawn.Spawned)
                {
                    Find.Selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);
                }
            }

            // Set flag so G key shows gizmos for selected pawns (not cursor position)
            GizmoNavigationState.PawnJustSelected = true;
        }

        /// <summary>
        /// Validates the multi-selection and removes any dead, destroyed, despawned, or off-map pawns.
        /// Follows WorldNavigationState.ValidateAndCleanupSelection() pattern.
        /// </summary>
        public static void ValidateAndCleanupSelection()
        {
            if (selectedPawns.Count == 0)
                return;

            Map currentMap = Find.CurrentMap;
            selectedPawns.RemoveWhere(p =>
                p == null || p.Destroyed || p.Dead || !p.Spawned ||
                (currentMap != null && p.Map != currentMap));

            if (selectedPawns.Count <= 1)
            {
                IsMultiSelectActive = false;
                if (selectedPawns.Count == 1)
                {
                    focusedPawn = selectedPawns.First();
                    SingleSelectFocusedPawn();
                }
                else
                {
                    selectedPawns.Clear();
                    focusedPawn = null;
                }
            }
            else
            {
                SyncSelectionWithGame();
            }
        }

        /// <summary>
        /// Called when a pawn is selected via normal single-select (comma/period without multi-select).
        /// Exits multi-select mode if active.
        /// </summary>
        public static void NotifySingleSelect(Pawn pawn)
        {
            if (IsMultiSelectActive)
            {
                selectedPawns.Clear();
                IsMultiSelectActive = false;
            }
            focusedPawn = pawn;
        }

        /// <summary>
        /// Resets all multi-select state (e.g., on map change or game load).
        /// </summary>
        public static void Reset()
        {
            selectedPawns.Clear();
            IsMultiSelectActive = false;
            focusedPawn = null;
        }

        /// <summary>
        /// Single-selects the focused pawn in RimWorld's Selector.
        /// Used when exiting multi-select mode.
        /// </summary>
        private static void SingleSelectFocusedPawn()
        {
            if (focusedPawn == null || Find.Selector == null)
                return;

            if (!focusedPawn.Destroyed && focusedPawn.Spawned)
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(focusedPawn, playSound: false, forceDesignatorDeselect: false);
            }
        }

        /// <summary>
        /// Announces the focused pawn with selected status, job, and position.
        /// Format: "{name}, selected/not selected, {job}, X of Y"
        /// </summary>
        /// <summary>
        /// Sets the focused pawn without changing selection.
        /// Used by navigation methods that need to update focus externally.
        /// </summary>
        public static void SetFocusedPawn(Pawn pawn)
        {
            focusedPawn = pawn;
        }

        public static void AnnounceFocusedPawn(Pawn pawn)
        {
            string selectedStatus = selectedPawns.Contains(pawn) ? "selected" : "not selected";

            string task = pawn.GetJobReport();
            if (string.IsNullOrEmpty(task))
                task = "Idle";

            var list = ColonistBarState.IsOnMechSection
                ? Find.CurrentMap?.mapPawns?.SpawnedColonyMechs?.OrderBy(p => p.LabelShort).ToList()
                : ColonistBarState.GetColonistsPublic();

            int total = list?.Count ?? 0;
            string positionPart = MenuHelper.FormatPosition(ColonistBarState.BarPosition, total);

            string announcement = $"{pawn.LabelShort}, {selectedStatus}, {task}";
            if (!string.IsNullOrEmpty(positionPart))
                announcement += $". {positionPart}";

            TolkHelper.Speak(announcement);
        }
    }
}
