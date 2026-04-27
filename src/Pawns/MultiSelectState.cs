using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages multi-selection of map pawns for accessibility.
    ///
    /// Find.Selector is the source of truth for the selected pawn set.
    /// This class only owns:
    ///   - focusedPawn: our navigation cursor, which in multi-select mode can
    ///     move independently of the selection.
    ///   - The mode semantics around toggling, group recall, contiguous selection,
    ///     and announcements.
    ///
    /// Because the selection is read live from Find.Selector, stale state cannot
    /// survive a save/load: a freshly loaded game starts with an empty Selector.
    /// focusedPawn is still static, so it is validated on access and cleared on
    /// <see cref="Reset"/> (called from GameStartPatch on FinalizeInit).
    /// </summary>
    public static class MultiSelectState
    {
        private static Pawn focusedPawn = null;

        /// <summary>
        /// The anchor pawn for contiguous (Shift+Alt+Arrow) range selection.
        /// The selected range is conceptually [min(anchor, focus), max(anchor, focus)].
        /// Moving focus AWAY from the anchor extends the range; moving TOWARD
        /// the anchor shrinks it (deselects the pawn being left behind).
        /// Cleared whenever the selection context changes (single-select,
        /// clear-multi, group recall, select-all, load).
        /// </summary>
        private static Pawn rangeAnchorPawn = null;

        /// <summary>
        /// User-intent flag: true once the user has explicitly entered multi-select
        /// (via Alt+Space, contiguous range, select-all, or group recall) and not
        /// yet exited it (single-select, clear, or removing the last pawn).
        ///
        /// This is distinct from <see cref="IsMultiSelectActive"/>, which is derived
        /// purely from how many pawns are selected. A user can be in multi-select
        /// mode with only a single pawn — that's the "I just started multi-selecting,
        /// here's my first pawn" state — and Alt+Space on that lone pawn should
        /// remove it and exit the mode rather than just deselecting.
        /// </summary>
        private static bool inMultiSelectMode = false;

        /// <summary>
        /// Whether multi-select mode is active (more than one pawn selected in
        /// Find.Selector). Derived from the game's own selection state.
        /// </summary>
        public static bool IsMultiSelectActive
            => (Find.Selector?.SelectedPawns?.Count ?? 0) > 1;

        /// <summary>
        /// Whether the user is in multi-select MODE — a stickier state than
        /// <see cref="IsMultiSelectActive"/>. True when:
        ///   - More than one pawn is selected (always implies multi-select), OR
        ///   - The user explicitly entered multi-select via Alt+Space / contiguous
        ///     range / select-all / group recall and the selection is non-empty.
        ///
        /// Use this when deciding whether Alt+Space should ADD/REMOVE from the
        /// multi-select list (mode active) or START multi-select (mode inactive).
        /// Use <see cref="IsMultiSelectActive"/> when deciding whether to apply
        /// an action to multiple pawns at once (drafting, gizmo broadcast, etc.).
        /// </summary>
        public static bool IsMultiSelectMode
        {
            get
            {
                int count = SelectedCount;
                if (count > 1)
                    return true;
                if (count == 0)
                    return false;
                return inMultiSelectMode;
            }
        }

        /// <summary>
        /// The currently focused pawn (bar cursor position).
        /// In multi-select mode, focus can move without changing the selection set.
        /// Returns null if the stored focus is no longer valid (destroyed, dead,
        /// despawned, or on a different map).
        /// </summary>
        public static Pawn FocusedPawn
        {
            get
            {
                if (focusedPawn == null)
                    return null;
                if (!IsPawnValid(focusedPawn))
                {
                    focusedPawn = null;
                    return null;
                }
                return focusedPawn;
            }
        }

        /// <summary>
        /// Number of currently selected pawns.
        /// </summary>
        public static int SelectedCount
            => Find.Selector?.SelectedPawns?.Count ?? 0;

        /// <summary>
        /// Gets a snapshot of the selected pawns. Returns a fresh list because
        /// Find.Selector.SelectedPawns reuses a shared static buffer.
        /// </summary>
        public static IReadOnlyCollection<Pawn> SelectedPawns
        {
            get
            {
                var sel = Find.Selector?.SelectedPawns;
                return sel == null
                    ? (IReadOnlyCollection<Pawn>)System.Array.Empty<Pawn>()
                    : sel.ToList();
            }
        }

        /// <summary>
        /// Checks whether a specific pawn is in the game's current selection.
        /// </summary>
        public static bool IsPawnSelected(Pawn pawn)
        {
            return pawn != null && Find.Selector != null && Find.Selector.IsSelected(pawn);
        }

        private static bool IsPawnValid(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned)
                return false;
            Map currentMap = Find.CurrentMap;
            return currentMap == null || pawn.Map == currentMap;
        }

        /// <summary>
        /// Toggles a pawn in/out of the multi-selection (Alt+Space).
        ///
        /// When multi-select mode is NOT active, Alt+Space ENTERS the mode with
        /// the focused pawn as the first item — the pawn stays selected (or is
        /// added to the selection if not already), and the mode flag is set so
        /// subsequent Alt+Space presses toggle membership.
        ///
        /// When multi-select mode IS active, Alt+Space toggles the pawn in/out
        /// of the multi-select list. Removing the last pawn exits the mode.
        /// </summary>
        public static void TogglePawn(Pawn pawn)
        {
            if (pawn == null)
            {
                TolkHelper.Speak("No pawn focused");
                return;
            }

            ValidateAndCleanupSelection();

            var selector = Find.Selector;
            if (selector == null)
                return;

            // Not in multi-select mode → ENTER mode with this pawn as the first item.
            // Don't deselect anything; if the pawn isn't already selected, add it.
            if (!IsMultiSelectMode)
            {
                bool alreadySelected = selector.IsSelected(pawn);
                if (!alreadySelected)
                    selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);

                focusedPawn = pawn;
                inMultiSelectMode = true;
                rangeAnchorPawn = null;
                GizmoNavigationState.PawnJustSelected = true;
                ColonistBarState.SyncBarPosition(pawn);

                int count = selector.SelectedPawns.Count;
                TolkHelper.Speak($"{pawn.LabelShort} added. Multi-select started. {count} selected.");
                return;
            }

            // In multi-select mode → toggle membership.
            if (selector.IsSelected(pawn))
            {
                selector.Deselect(pawn);
                int remaining = selector.SelectedPawns.Count;

                if (remaining == 0)
                {
                    inMultiSelectMode = false;
                    focusedPawn = null;
                    rangeAnchorPawn = null;
                    TolkHelper.Speak($"{pawn.LabelShort} removed. Multi-select disabled.");
                }
                else
                {
                    if (focusedPawn == pawn)
                        focusedPawn = selector.SelectedPawns.First();
                    GizmoNavigationState.PawnJustSelected = true;
                    TolkHelper.Speak($"{pawn.LabelShort} removed. {remaining} selected.");
                }
            }
            else
            {
                selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);
                focusedPawn = pawn;
                GizmoNavigationState.PawnJustSelected = true;
                int count = selector.SelectedPawns.Count;
                TolkHelper.Speak($"{pawn.LabelShort} added. {count} selected.");
            }
        }

        /// <summary>
        /// Adds a pawn to the selection without toggle behavior (used by contiguous selection).
        /// </summary>
        public static void AddPawn(Pawn pawn)
        {
            if (pawn == null)
                return;
            var selector = Find.Selector;
            if (selector == null || selector.IsSelected(pawn))
                return;

            selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);
            GizmoNavigationState.PawnJustSelected = true;
        }

        /// <summary>
        /// Extends or shrinks the contiguous range rightward (Alt+Shift+Right).
        /// Explorer/Finder semantics: the range is anchored at the pawn where
        /// the Shift selection began; moving right extends when on the right
        /// side of the anchor and shrinks when on the left side.
        /// </summary>
        public static void SelectContiguousNext()
        {
            if (Find.CurrentMap == null)
                return;

            ValidateAndCleanupSelection();

            var selector = Find.Selector;
            if (selector == null)
                return;

            if (!EnsureRangeAnchor(selector))
                return;

            ExtendRange(selector, moveRight: true);
        }

        /// <summary>
        /// Extends or shrinks the contiguous range leftward (Alt+Shift+Left).
        /// See <see cref="SelectContiguousNext"/> for the anchor semantics.
        /// </summary>
        public static void SelectContiguousPrevious()
        {
            if (Find.CurrentMap == null)
                return;

            ValidateAndCleanupSelection();

            var selector = Find.Selector;
            if (selector == null)
                return;

            if (!EnsureRangeAnchor(selector))
                return;

            ExtendRange(selector, moveRight: false);
        }

        /// <summary>
        /// Establishes or validates the range anchor for Shift+Alt+Arrow selection.
        /// If the anchor is missing or no longer part of the selection (e.g. it
        /// was removed by another action), re-anchors to the current single-
        /// selected pawn or, failing that, the focused pawn.
        /// </summary>
        /// <returns>True if an anchor is now in place and the caller may proceed.</returns>
        private static bool EnsureRangeAnchor(Selector selector)
        {
            if (rangeAnchorPawn != null &&
                IsPawnValid(rangeAnchorPawn) &&
                selector.IsSelected(rangeAnchorPawn))
            {
                return true;
            }

            Pawn anchor = selector.SingleSelectedThing as Pawn;
            if (anchor == null || !IsPawnValid(anchor))
                anchor = IsPawnValid(focusedPawn) ? focusedPawn : null;

            if (anchor == null)
            {
                TolkHelper.Speak("No pawn selected");
                rangeAnchorPawn = null;
                return false;
            }

            if (!selector.IsSelected(anchor))
                selector.Select(anchor, playSound: false, forceDesignatorDeselect: false);

            focusedPawn = anchor;
            rangeAnchorPawn = anchor;
            inMultiSelectMode = true;
            ColonistBarState.SyncBarPosition(anchor);
            return true;
        }

        /// <summary>
        /// Moves focus one step in the requested direction and adjusts the
        /// selection: extends if moving away from the anchor, shrinks (deselects
        /// the pawn being left behind) if moving toward the anchor.
        /// </summary>
        private static void ExtendRange(Selector selector, bool moveRight)
        {
            Pawn oldFocus = focusedPawn;
            int oldIdx = ColonistBarState.GetGlobalBarIndex(oldFocus);
            int anchorIdx = ColonistBarState.GetGlobalBarIndex(rangeAnchorPawn);

            Pawn newFocus = moveRight
                ? ColonistBarState.NavigateFocusRight()
                : ColonistBarState.NavigateFocusLeft();

            if (newFocus == null || newFocus == oldFocus)
                return;

            focusedPawn = newFocus;

            bool haveIndices = oldIdx >= 0 && anchorIdx >= 0;
            bool shrinking = haveIndices && oldFocus != null &&
                (moveRight ? oldIdx < anchorIdx : oldIdx > anchorIdx);

            if (shrinking)
            {
                if (selector.IsSelected(oldFocus))
                    selector.Deselect(oldFocus);
                GizmoNavigationState.PawnJustSelected = true;
                int count = selector.SelectedPawns.Count;
                TolkHelper.Speak($"{oldFocus.LabelShort} removed. {newFocus.LabelShort}, {count} selected.");
            }
            else
            {
                bool alreadySelected = selector.IsSelected(newFocus);
                AddPawn(newFocus);
                int count = selector.SelectedPawns.Count;

                if (!alreadySelected)
                    TolkHelper.Speak($"{newFocus.LabelShort} added. {count} selected.");
                else
                    TolkHelper.Speak($"{newFocus.LabelShort}, already selected. {count} selected.");
            }
        }

        /// <summary>
        /// Navigates focus to the next pawn without changing the selection.
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
            if (!IsMultiSelectMode)
            {
                TolkHelper.Speak("No multi-selection active");
                return;
            }

            rangeAnchorPawn = null;
            inMultiSelectMode = false;
            SingleSelectFocusedPawn();

            if (focusedPawn != null)
            {
                string task = focusedPawn.GetJobReport();
                if (string.IsNullOrEmpty(task))
                    task = "Idle";
                TolkHelper.Speak($"Selection cleared. {focusedPawn.LabelShort}, {task}");
            }
            else
            {
                TolkHelper.Speak("Selection cleared");
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

            var selector = Find.Selector;
            if (selector == null)
                return;

            rangeAnchorPawn = null;

            selector.ClearSelection();
            foreach (var pawn in allColonists)
            {
                if (IsPawnValid(pawn))
                    selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);
            }

            var selected = selector.SelectedPawns.ToList();
            if (selected.Count > 0)
            {
                focusedPawn = selected.First();
                ColonistBarState.SyncBarPosition(focusedPawn);
                inMultiSelectMode = true;
            }

            GizmoNavigationState.PawnJustSelected = true;

            if (selected.Count <= 5)
            {
                string names = MenuHelper.FormatNameList(
                    selected.Select(p => p.LabelShort).ToList());
                TolkHelper.Speak($"All selected: {names}. {selected.Count} colonists.");
            }
            else
            {
                TolkHelper.Speak($"All {selected.Count} colonists selected");
            }
        }

        /// <summary>
        /// Activates multi-select with the given set of pawns.
        /// Used by group recall (Ctrl+F1-F5).
        /// </summary>
        public static void SetSelection(IEnumerable<Pawn> pawns)
        {
            var selector = Find.Selector;
            if (selector == null)
                return;

            rangeAnchorPawn = null;

            selector.ClearSelection();
            foreach (var pawn in pawns)
            {
                if (IsPawnValid(pawn))
                    selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);
            }

            var selected = selector.SelectedPawns.ToList();
            if (selected.Count > 0)
            {
                focusedPawn = selected.First();
                ColonistBarState.SyncBarPosition(focusedPawn);
                GizmoNavigationState.PawnJustSelected = true;
                inMultiSelectMode = true;
            }
            else
            {
                inMultiSelectMode = false;
            }
        }

        /// <summary>
        /// Validates the current selection and deselects any dead, destroyed,
        /// despawned, or off-map pawns from Find.Selector. Also invalidates
        /// focusedPawn if it no longer refers to a valid pawn.
        /// </summary>
        public static void ValidateAndCleanupSelection()
        {
            var selector = Find.Selector;
            if (selector == null)
                return;

            var snapshot = selector.SelectedPawns.ToList();
            if (snapshot.Count == 0)
            {
                if (focusedPawn != null && !IsPawnValid(focusedPawn))
                    focusedPawn = null;
                inMultiSelectMode = false;
                return;
            }

            foreach (var pawn in snapshot)
            {
                if (!IsPawnValid(pawn))
                    selector.Deselect(pawn);
            }

            if (focusedPawn != null && !IsPawnValid(focusedPawn))
                focusedPawn = null;

            if (rangeAnchorPawn != null &&
                (!IsPawnValid(rangeAnchorPawn) || !selector.IsSelected(rangeAnchorPawn)))
            {
                rangeAnchorPawn = null;
            }

            if (focusedPawn == null && selector.SelectedPawns.Count == 1)
                focusedPawn = selector.SelectedPawns.First();
        }

        /// <summary>
        /// Called when a pawn is selected via normal single-select (comma/period).
        /// Updates the focus cursor to track the newly selected pawn and exits
        /// multi-select mode (single-selecting is the canonical way out).
        /// </summary>
        public static void NotifySingleSelect(Pawn pawn)
        {
            focusedPawn = pawn;
            rangeAnchorPawn = null;
            inMultiSelectMode = false;
        }

        /// <summary>
        /// Resets transient state (the focus cursor). Called on game load from
        /// GameStartPatch so a stale Pawn reference from a previous session
        /// cannot leak into the new game. Find.Selector is already fresh on load,
        /// so no selection clearing is needed.
        /// </summary>
        public static void Reset()
        {
            focusedPawn = null;
            rangeAnchorPawn = null;
            inMultiSelectMode = false;
        }

        /// <summary>
        /// Single-selects the focused pawn in RimWorld's Selector.
        /// Used when exiting multi-select mode.
        /// </summary>
        private static void SingleSelectFocusedPawn()
        {
            var selector = Find.Selector;
            if (focusedPawn == null || selector == null)
                return;

            if (!focusedPawn.Destroyed && focusedPawn.Spawned)
            {
                selector.ClearSelection();
                selector.Select(focusedPawn, playSound: false, forceDesignatorDeselect: false);
                GizmoNavigationState.PawnJustSelected = true;
            }
        }

        /// <summary>
        /// Sets the focused pawn without changing selection.
        /// Used by navigation methods that need to update focus externally.
        /// </summary>
        public static void SetFocusedPawn(Pawn pawn)
        {
            focusedPawn = pawn;
        }

        /// <summary>
        /// Announces the focused pawn with selected status, job, and position.
        /// Format: "{name}, selected/not selected, {job}, X of Y"
        /// </summary>
        public static void AnnounceFocusedPawn(Pawn pawn)
        {
            string selectedStatus = IsPawnSelected(pawn) ? "selected" : "not selected";

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
