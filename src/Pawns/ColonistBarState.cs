using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Provides page-based navigation of the colonist bar with keyboard shortcuts.
    ///
    /// The bar is organized into pages of 10:
    /// - Alt+Left/Right: navigate linearly (crosses page boundaries)
    /// - Alt+1-0: jump to position 1-10 on current page
    /// - Alt+Up/Down: move between pages (colonist pages, then mech pages)
    /// - Ctrl+Alt+Left/Right: reorder colonists using shift/insert
    /// - Comma/Period: when on mech page, cycles mechs instead of colonists
    /// </summary>
    public static class ColonistBarState
    {
        public const int PageSize = 10;

        /// <summary>
        /// 0-indexed position into the current section's list (colonists or mechs).
        /// </summary>
        private static int barPosition = 0;

        /// <summary>
        /// Whether we're currently viewing the mech section (after all colonist pages).
        /// </summary>
        private static bool onMechSection = false;

        /// <summary>
        /// The map ID we last navigated on. Used to detect map changes and reset.
        /// </summary>
        private static int lastMapId = -1;

        // ===== PUBLIC PROPERTIES =====

        /// <summary>
        /// Whether the bar cursor is currently on the mechanoid section.
        /// When true, comma/period should cycle mechs instead of colonists.
        /// </summary>
        public static bool IsOnMechSection => onMechSection;

        /// <summary>
        /// Current page number (0-indexed). Derived from bar position.
        /// </summary>
        public static int CurrentPage => barPosition / PageSize;

        /// <summary>
        /// Position within the current page (0-indexed, 0-9).
        /// </summary>
        public static int PositionInPage => barPosition % PageSize;

        /// <summary>
        /// Current 0-indexed bar position. Used by MultiSelectState for position announcements.
        /// </summary>
        public static int BarPosition => barPosition;

        // ===== DATA SOURCES =====

        /// <summary>
        /// Gets colonists on the current map in bar display order.
        /// Same source as PawnSelectionState uses for comma/period cycling.
        /// </summary>
        private static List<Pawn> GetColonists()
        {
            if (Find.ColonistBar == null || Find.CurrentMap == null)
                return new List<Pawn>();

            return Find.ColonistBar.GetColonistsInOrder()
                .Where(p => p != null &&
                            p.Spawned &&
                            p.Map == Find.CurrentMap &&
                            p.def.selectable)
                .ToList();
        }

        /// <summary>
        /// Gets colony mechs on the current map. Only available with Biotech DLC.
        /// </summary>
        private static List<Pawn> GetMechs()
        {
            if (!ModsConfig.BiotechActive || Find.CurrentMap == null)
                return new List<Pawn>();

            return Find.CurrentMap.mapPawns.SpawnedColonyMechs
                .OrderBy(p => p.LabelShort)
                .ToList();
        }

        /// <summary>
        /// Gets the list for the current section (colonists or mechs).
        /// </summary>
        private static List<Pawn> GetCurrentList()
        {
            return onMechSection ? GetMechs() : GetColonists();
        }

        /// <summary>
        /// Finds the entry group number for a pawn by looking it up in the colonist bar entries.
        /// </summary>
        private static int GetGroupForPawn(Pawn pawn)
        {
            var entries = Find.ColonistBar.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].pawn == pawn)
                    return entries[i].group;
            }
            return -1;
        }

        /// <summary>
        /// Finds a pawn's index within its group's entries, counting the same way
        /// ColonistBar.Reorder() does (all non-null pawns in the group).
        /// </summary>
        private static int GetEntryIndexForPawn(Pawn pawn, int group)
        {
            var entries = Find.ColonistBar.Entries;
            int indexInGroup = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].group == group && entries[i].pawn != null)
                {
                    if (entries[i].pawn == pawn)
                        return indexInGroup;
                    indexInGroup++;
                }
            }
            return -1;
        }

        /// <summary>
        /// Assigns sequential displayOrder values (0, 1, 2, ...) to all pawns in the group.
        /// Reorder() breaks when multiple pawns share the same displayOrder value,
        /// because it bumps ALL pawns at the target order, preventing the moved pawn
        /// from actually passing them. Normalizing before each Reorder call fixes this.
        /// </summary>
        private static void NormalizeGroupDisplayOrders(int group)
        {
            var entries = Find.ColonistBar.Entries;
            int order = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].group == group && entries[i].pawn != null)
                {
                    entries[i].pawn.playerSettings.displayOrder = order;
                    order++;
                }
            }
        }

        // ===== MAP CHANGE DETECTION =====

        /// <summary>
        /// Checks if the map has changed since last navigation, and resets if so.
        /// Called at the start of every navigation action.
        /// </summary>
        private static void CheckMapChange()
        {
            int currentMapId = Find.CurrentMap?.uniqueID ?? -1;
            if (currentMapId != lastMapId)
            {
                barPosition = 0;
                onMechSection = false;
                lastMapId = currentMapId;
            }
        }

        /// <summary>
        /// Clamps barPosition to valid range for the current list.
        /// Handles colonist death/departure shrinking the list.
        /// </summary>
        private static void ClampPosition()
        {
            var list = GetCurrentList();
            if (list.Count == 0)
            {
                barPosition = 0;
                return;
            }
            if (barPosition >= list.Count)
                barPosition = list.Count - 1;
            if (barPosition < 0)
                barPosition = 0;
        }

        // ===== NAVIGATION =====

        /// <summary>
        /// Navigate right (Alt+Right). Moves to next pawn, crossing page boundaries.
        /// </summary>
        public static void NavigateRight()
        {
            CheckMapChange();
            var list = GetCurrentList();

            if (list.Count == 0)
            {
                AnnounceEmpty();
                return;
            }

            ClampPosition();

            if (barPosition < list.Count - 1)
            {
                barPosition++;
            }
            else
            {
                // At end of current section - try crossing to mech section
                if (!onMechSection && GetMechs().Count > 0)
                {
                    onMechSection = true;
                    barPosition = 0;
                    AnnounceSectionChange();
                }
                else
                {
                    SelectAndAnnounce();
                    return;
                }
            }

            SelectAndAnnounce();
        }

        /// <summary>
        /// Navigate left (Alt+Left). Moves to previous pawn, crossing page boundaries.
        /// </summary>
        public static void NavigateLeft()
        {
            CheckMapChange();
            var list = GetCurrentList();

            if (list.Count == 0)
            {
                AnnounceEmpty();
                return;
            }

            ClampPosition();

            if (barPosition > 0)
            {
                barPosition--;
            }
            else
            {
                // At start of current section - try crossing back to colonist section
                if (onMechSection)
                {
                    var colonists = GetColonists();
                    if (colonists.Count > 0)
                    {
                        onMechSection = false;
                        barPosition = colonists.Count - 1;
                        AnnounceSectionChange();
                    }
                    else
                    {
                        TolkHelper.Speak("Start of bar");
                        return;
                    }
                }
                else
                {
                    SelectAndAnnounce();
                    return;
                }
            }

            SelectAndAnnounce();
        }

        /// <summary>
        /// Page down (Alt+Down). Jumps to next page of 10, preserving position within the page.
        /// If on last colonist page, switches to mech section.
        /// </summary>
        public static void PageDown()
        {
            CheckMapChange();
            int posInPage = PositionInPage;

            if (!onMechSection)
            {
                var colonists = GetColonists();
                if (colonists.Count == 0)
                {
                    // No colonists - try mechs
                    var mechs = GetMechs();
                    if (mechs.Count > 0)
                    {
                        onMechSection = true;
                        barPosition = System.Math.Min(posInPage, mechs.Count - 1);
                        AnnounceSectionChange();
                        SelectAndAnnounce();
                    }
                    else
                    {
                        AnnounceEmpty();
                    }
                    return;
                }

                int targetPosition = barPosition + PageSize;
                if (targetPosition >= colonists.Count)
                    targetPosition = colonists.Count - 1;

                if (targetPosition / PageSize == CurrentPage)
                {
                    // Couldn't move to a new page - try mechs
                    var mechs = GetMechs();
                    if (mechs.Count > 0)
                    {
                        onMechSection = true;
                        barPosition = System.Math.Min(posInPage, mechs.Count - 1);
                        AnnounceSectionChange();
                        SelectAndAnnounce();
                    }
                    else
                    {
                        TolkHelper.Speak("Last page");
                    }
                }
                else
                {
                    barPosition = targetPosition;
                    AnnouncePageChange();
                    SelectAndAnnounce();
                }
            }
            else
            {
                // Already on mech section
                var mechs = GetMechs();
                if (mechs.Count == 0)
                {
                    TolkHelper.Speak("No mechs on this map");
                    return;
                }

                int targetPosition = barPosition + PageSize;
                if (targetPosition >= mechs.Count)
                    targetPosition = mechs.Count - 1;

                if (targetPosition / PageSize == CurrentPage)
                {
                    TolkHelper.Speak("Last page");
                }
                else
                {
                    barPosition = targetPosition;
                    AnnouncePageChange();
                    SelectAndAnnounce();
                }
            }
        }

        /// <summary>
        /// Page up (Alt+Up). Jumps to previous page of 10, preserving position within the page.
        /// If on first mech page, switches back to colonist section.
        /// </summary>
        public static void PageUp()
        {
            CheckMapChange();
            int posInPage = PositionInPage;

            if (onMechSection)
            {
                if (CurrentPage > 0)
                {
                    // Move to previous mech page, preserve position
                    barPosition = (CurrentPage - 1) * PageSize + posInPage;
                    AnnouncePageChange();
                    SelectAndAnnounce();
                }
                else
                {
                    // On first mech page - switch back to colonists
                    var colonists = GetColonists();
                    if (colonists.Count > 0)
                    {
                        onMechSection = false;
                        // Go to last colonist page, preserve position (clamped)
                        int lastPageStart = ((colonists.Count - 1) / PageSize) * PageSize;
                        barPosition = System.Math.Min(lastPageStart + posInPage, colonists.Count - 1);
                        AnnounceSectionChange();
                        SelectAndAnnounce();
                    }
                    else
                    {
                        TolkHelper.Speak("First page");
                    }
                }
            }
            else
            {
                // On colonist section
                if (CurrentPage > 0)
                {
                    // Previous page, preserve position
                    barPosition = (CurrentPage - 1) * PageSize + posInPage;
                    AnnouncePageChange();
                    SelectAndAnnounce();
                }
                else
                {
                    TolkHelper.Speak("First page");
                }
            }
        }

        /// <summary>
        /// Jump to a position on the current page (Alt+1 through Alt+0).
        /// positionOnPage is 0-indexed (0 = first position, 9 = tenth position).
        /// </summary>
        public static void JumpToPosition(int positionOnPage)
        {
            CheckMapChange();
            var list = GetCurrentList();

            if (list.Count == 0)
            {
                AnnounceEmpty();
                return;
            }

            int targetIndex = CurrentPage * PageSize + positionOnPage;

            if (targetIndex >= list.Count)
            {
                TolkHelper.Speak($"No {(onMechSection ? "mech" : "colonist")} at position {positionOnPage + 1}");
                return;
            }

            barPosition = targetIndex;
            SelectAndAnnounce();
        }

        // ===== REORDERING =====

        /// <summary>
        /// Move current colonist right (Ctrl+Alt+Right). Uses shift/insert reorder.
        /// Not available for mechs.
        /// </summary>
        public static void MoveRight()
        {
            CheckMapChange();

            if (onMechSection)
            {
                TolkHelper.Speak("Cannot reorder mechs");
                return;
            }

            var colonists = GetColonists();
            if (colonists.Count < 2)
                return;

            ClampPosition();

            if (barPosition >= colonists.Count - 1)
            {
                TolkHelper.Speak("Already at last position");
                return;
            }

            Pawn pawnToMove = colonists[barPosition];
            Pawn swapWith = colonists[barPosition + 1];

            int group = GetGroupForPawn(pawnToMove);
            if (group < 0) return;

            // Fix duplicate displayOrder values that break Reorder
            NormalizeGroupDisplayOrders(group);
            Find.ColonistBar.MarkColonistsDirty();

            int fromIndex = GetEntryIndexForPawn(pawnToMove, group);
            int targetIndex = GetEntryIndexForPawn(swapWith, group);
            if (fromIndex < 0 || targetIndex < 0) return;

            Find.ColonistBar.Reorder(fromIndex, targetIndex + 1, group);
            barPosition++;

            AnnounceReorder(pawnToMove);
        }

        /// <summary>
        /// Move current colonist left (Ctrl+Alt+Left). Uses shift/insert reorder.
        /// Not available for mechs.
        /// </summary>
        public static void MoveLeft()
        {
            CheckMapChange();

            if (onMechSection)
            {
                TolkHelper.Speak("Cannot reorder mechs");
                return;
            }

            var colonists = GetColonists();
            if (colonists.Count < 2)
                return;

            ClampPosition();

            if (barPosition <= 0)
            {
                TolkHelper.Speak("Already at first position");
                return;
            }

            Pawn pawnToMove = colonists[barPosition];
            Pawn swapWith = colonists[barPosition - 1];

            int group = GetGroupForPawn(pawnToMove);
            if (group < 0) return;

            // Fix duplicate displayOrder values that break Reorder
            NormalizeGroupDisplayOrders(group);
            Find.ColonistBar.MarkColonistsDirty();

            int fromIndex = GetEntryIndexForPawn(pawnToMove, group);
            int targetIndex = GetEntryIndexForPawn(swapWith, group);
            if (fromIndex < 0 || targetIndex < 0) return;

            Find.ColonistBar.Reorder(fromIndex, targetIndex, group);
            barPosition--;

            AnnounceReorder(pawnToMove);
        }

        /// <summary>
        /// Move current colonist down one page (Ctrl+Alt+Down). Moves to the position
        /// directly below on the next page, or to the last position on the next page
        /// if the direct-below slot doesn't exist. Not available for mechs.
        /// </summary>
        public static void MoveDown()
        {
            CheckMapChange();

            if (onMechSection)
            {
                TolkHelper.Speak("Cannot reorder mechs");
                return;
            }

            var colonists = GetColonists();
            if (colonists.Count < 2)
                return;

            ClampPosition();

            // Target: directly below on next page, clamped to last position
            int targetBarPosition = System.Math.Min(barPosition + PageSize, colonists.Count - 1);

            if (targetBarPosition / PageSize == barPosition / PageSize)
            {
                TolkHelper.Speak("Already on last page");
                return;
            }

            Pawn pawnToMove = colonists[barPosition];
            Pawn targetPawn = colonists[targetBarPosition];

            int group = GetGroupForPawn(pawnToMove);
            if (group < 0) return;

            NormalizeGroupDisplayOrders(group);
            Find.ColonistBar.MarkColonistsDirty();

            int fromIndex = GetEntryIndexForPawn(pawnToMove, group);
            int targetIndex = GetEntryIndexForPawn(targetPawn, group);
            if (fromIndex < 0 || targetIndex < 0) return;

            // Moving forward: insert after target (same as MoveRight)
            Find.ColonistBar.Reorder(fromIndex, targetIndex + 1, group);

            AnnounceReorder(pawnToMove);
        }

        /// <summary>
        /// Move current colonist up one page (Ctrl+Alt+Up). Moves to the position
        /// directly above on the previous page, or to position 0 if the direct-above
        /// slot doesn't exist. Not available for mechs.
        /// </summary>
        public static void MoveUp()
        {
            CheckMapChange();

            if (onMechSection)
            {
                TolkHelper.Speak("Cannot reorder mechs");
                return;
            }

            var colonists = GetColonists();
            if (colonists.Count < 2)
                return;

            ClampPosition();

            // Target: directly above on previous page, clamped to first position
            int targetBarPosition = System.Math.Max(barPosition - PageSize, 0);

            if (targetBarPosition / PageSize == barPosition / PageSize)
            {
                TolkHelper.Speak("Already on first page");
                return;
            }

            Pawn pawnToMove = colonists[barPosition];
            Pawn targetPawn = colonists[targetBarPosition];

            int group = GetGroupForPawn(pawnToMove);
            if (group < 0) return;

            NormalizeGroupDisplayOrders(group);
            Find.ColonistBar.MarkColonistsDirty();

            int fromIndex = GetEntryIndexForPawn(pawnToMove, group);
            int targetIndex = GetEntryIndexForPawn(targetPawn, group);
            if (fromIndex < 0 || targetIndex < 0) return;

            // Moving backward: insert before target (same as MoveLeft)
            Find.ColonistBar.Reorder(fromIndex, targetIndex, group);

            AnnounceReorder(pawnToMove);
        }

        // ===== SYNC WITH COMMA/PERIOD =====

        /// <summary>
        /// Called after comma/period selects a pawn, to keep bar position in sync.
        /// </summary>
        public static void SyncBarPosition(Pawn pawn)
        {
            if (pawn == null)
                return;

            CheckMapChange();

            // Check colonists first
            var colonists = GetColonists();
            int idx = colonists.IndexOf(pawn);
            if (idx >= 0)
            {
                barPosition = idx;
                onMechSection = false;
                return;
            }

            // Check mechs
            if (pawn.IsColonyMech)
            {
                var mechs = GetMechs();
                idx = mechs.IndexOf(pawn);
                if (idx >= 0)
                {
                    barPosition = idx;
                    onMechSection = true;
                }
            }
        }

        // ===== MECH CYCLING (for comma/period when on mech page) =====

        /// <summary>
        /// Select next mech (period key when on mech section).
        /// Returns the selected mech, or null if none.
        /// </summary>
        public static Pawn SelectNextMech()
        {
            CheckMapChange();
            var mechs = GetMechs();
            if (mechs.Count == 0)
                return null;

            ClampPosition();
            barPosition = (barPosition + 1) % mechs.Count;
            return mechs[barPosition];
        }

        /// <summary>
        /// Select previous mech (comma key when on mech section).
        /// Returns the selected mech, or null if none.
        /// </summary>
        public static Pawn SelectPreviousMech()
        {
            CheckMapChange();
            var mechs = GetMechs();
            if (mechs.Count == 0)
                return null;

            ClampPosition();
            barPosition = (barPosition - 1 + mechs.Count) % mechs.Count;
            return mechs[barPosition];
        }

        // ===== SELECTION AND ANNOUNCEMENTS =====

        /// <summary>
        /// Selects the pawn at the current bar position in-game and announces.
        /// </summary>
        private static void SelectAndAnnounce()
        {
            var list = GetCurrentList();
            ClampPosition();

            if (list.Count == 0 || barPosition >= list.Count)
            {
                AnnounceEmpty();
                return;
            }

            Pawn pawn = list[barPosition];
            SelectPawnInGame(pawn);
            AnnouncePawn(pawn, list.Count);
        }

        /// <summary>
        /// Selects a pawn in-game: clears selection, selects pawn, jumps camera, enables pawn follow.
        /// Same behavior as comma/period selection in ThingSelectionUtilityPatch.
        /// </summary>
        private static void SelectPawnInGame(Pawn pawn)
        {
            if (pawn == null)
                return;

            if (Find.Selector != null)
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(pawn);
            }

            if (Find.CameraDriver != null)
            {
                Find.CameraDriver.JumpToCurrentMapLoc(pawn.Position);
            }

            MapNavigationState.CurrentCameraMode = CameraFollowMode.Pawn;
            GizmoNavigationState.PawnJustSelected = true;

            // Keep PawnSelectionState in sync
            PawnSelectionState.SyncFromBarNavigation(pawn);
        }

        /// <summary>
        /// Announces the current pawn. Format: "{Name} selected - {task}"
        /// Appends position if AnnouncePosition setting is enabled.
        /// </summary>
        private static void AnnouncePawn(Pawn pawn, int totalInSection)
        {
            string task = pawn.GetJobReport();
            if (string.IsNullOrEmpty(task))
                task = "Idle";

            string announcement = pawn.LabelShort;

            if (RimWorldAccessMod_Settings.Settings?.ShowCoverInfo ?? true)
            {
                string coverInfo = CoverHelper.GetCoverInfo(pawn);
                if (!string.IsNullOrEmpty(coverInfo))
                    announcement += $", {coverInfo}";
            }

            announcement += $" - {task}";

            string positionPart = MenuHelper.FormatPosition(barPosition, totalInSection);
            if (!string.IsNullOrEmpty(positionPart))
                announcement += $". {positionPart}";

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Announces page change (e.g., "Page 2" or "Mechs page 1").
        /// </summary>
        private static void AnnouncePageChange()
        {
            string section = onMechSection ? "Mechs page" : "Page";
            TolkHelper.Speak($"{section} {CurrentPage + 1}");
        }

        /// <summary>
        /// Announces section change (switching between colonists and mechs).
        /// </summary>
        private static void AnnounceSectionChange()
        {
            if (onMechSection)
                TolkHelper.Speak("Mechs");
            else
                TolkHelper.Speak("Colonists");
        }

        /// <summary>
        /// Announces reorder result.
        /// </summary>
        private static void AnnounceReorder(Pawn pawn)
        {
            // Re-fetch list after reorder to get fresh positions
            var colonists = GetColonists();
            int newIndex = colonists.IndexOf(pawn);
            if (newIndex < 0)
                return;

            // Follow the moved pawn
            barPosition = newIndex;

            // Build neighbor context
            string context;
            if (newIndex == 0 && colonists.Count == 1)
                context = "only colonist";
            else if (newIndex == 0)
                context = "leftmost";
            else if (newIndex == colonists.Count - 1)
                context = "rightmost";
            else
                context = $"between {colonists[newIndex - 1].LabelShort} and {colonists[newIndex + 1].LabelShort}";

            TolkHelper.Speak($"{pawn.LabelShort}, position {newIndex + 1}, {context}");
        }

        /// <summary>
        /// Announces that the current section is empty.
        /// </summary>
        private static void AnnounceEmpty()
        {
            if (onMechSection)
                TolkHelper.Speak("No mechs on this map");
            else
                TolkHelper.Speak("No colonists on this map");
        }

        // ===== FOCUS-ONLY NAVIGATION (for multi-select mode) =====

        /// <summary>
        /// Gets colonists on the current map in bar display order (public accessor for MultiSelectState).
        /// </summary>
        public static List<Pawn> GetColonistsPublic()
        {
            return GetColonists();
        }

        /// <summary>
        /// Returns the pawn at the current bar position without selecting it.
        /// </summary>
        public static Pawn GetPawnAtCurrentPosition()
        {
            CheckMapChange();
            ClampPosition();
            var list = GetCurrentList();
            if (list.Count == 0 || barPosition >= list.Count)
                return null;
            return list[barPosition];
        }

        /// <summary>
        /// Moves the bar cursor right and returns the pawn at the new position.
        /// Does NOT select the pawn in-game or move the camera.
        /// Used for multi-select focus navigation and contiguous selection.
        /// </summary>
        public static Pawn NavigateFocusRight()
        {
            CheckMapChange();
            var list = GetCurrentList();

            if (list.Count == 0)
            {
                AnnounceEmpty();
                return null;
            }

            ClampPosition();

            if (barPosition < list.Count - 1)
            {
                barPosition++;
            }
            else
            {
                // At end of current section - try crossing to mech section
                if (!onMechSection && GetMechs().Count > 0)
                {
                    onMechSection = true;
                    barPosition = 0;
                    AnnounceSectionChange();
                }
                else
                {
                    // Already at end, return current pawn
                    return list[barPosition];
                }
            }

            list = GetCurrentList();
            ClampPosition();
            return list.Count > 0 ? list[barPosition] : null;
        }

        /// <summary>
        /// Moves the bar cursor left and returns the pawn at the new position.
        /// Does NOT select the pawn in-game or move the camera.
        /// Used for multi-select focus navigation and contiguous selection.
        /// </summary>
        public static Pawn NavigateFocusLeft()
        {
            CheckMapChange();
            var list = GetCurrentList();

            if (list.Count == 0)
            {
                AnnounceEmpty();
                return null;
            }

            ClampPosition();

            if (barPosition > 0)
            {
                barPosition--;
            }
            else
            {
                // At start of current section - try crossing back to colonist section
                if (onMechSection)
                {
                    var colonists = GetColonists();
                    if (colonists.Count > 0)
                    {
                        onMechSection = false;
                        barPosition = colonists.Count - 1;
                        AnnounceSectionChange();
                    }
                    else
                    {
                        return list.Count > 0 ? list[0] : null;
                    }
                }
                else
                {
                    // Already at start, return current pawn
                    return list[barPosition];
                }
            }

            list = GetCurrentList();
            ClampPosition();
            return list.Count > 0 ? list[barPosition] : null;
        }

        /// <summary>
        /// Jump to a position on the current page without selecting (focus-only for multi-select).
        /// Returns the pawn at the target position, or null if invalid.
        /// </summary>
        public static Pawn JumpFocusToPosition(int positionOnPage)
        {
            CheckMapChange();
            var list = GetCurrentList();
            if (list.Count == 0)
                return null;

            int targetIndex = CurrentPage * PageSize + positionOnPage;
            if (targetIndex >= list.Count)
            {
                TolkHelper.Speak($"No {(onMechSection ? "mech" : "colonist")} at position {positionOnPage + 1}");
                return null;
            }

            barPosition = targetIndex;
            return list[barPosition];
        }

        /// <summary>
        /// Page down without selecting (focus-only for multi-select).
        /// Returns the pawn at the new position, or null.
        /// </summary>
        public static Pawn PageFocusDown()
        {
            CheckMapChange();
            int posInPage = PositionInPage;

            if (!onMechSection)
            {
                var colonists = GetColonists();
                if (colonists.Count == 0)
                {
                    var mechs = GetMechs();
                    if (mechs.Count > 0)
                    {
                        onMechSection = true;
                        barPosition = System.Math.Min(posInPage, mechs.Count - 1);
                        AnnounceSectionChange();
                        return GetMechs().Count > 0 ? GetMechs()[barPosition] : null;
                    }
                    return null;
                }

                int targetPosition = barPosition + PageSize;
                if (targetPosition >= colonists.Count)
                    targetPosition = colonists.Count - 1;

                if (targetPosition / PageSize == CurrentPage)
                {
                    // Same page — try mechs
                    var mechs = GetMechs();
                    if (mechs.Count > 0)
                    {
                        onMechSection = true;
                        barPosition = System.Math.Min(posInPage, mechs.Count - 1);
                        AnnounceSectionChange();
                        return GetMechs().Count > 0 ? GetMechs()[barPosition] : null;
                    }
                    return null;
                }

                barPosition = targetPosition;
                AnnouncePageChange();
            }
            else
            {
                var mechs = GetMechs();
                if (mechs.Count == 0)
                    return null;

                int targetPosition = barPosition + PageSize;
                if (targetPosition >= mechs.Count)
                    targetPosition = mechs.Count - 1;

                if (targetPosition / PageSize == CurrentPage)
                    return null; // Already on last mech page

                barPosition = targetPosition;
                AnnouncePageChange();
            }

            var list = GetCurrentList();
            ClampPosition();
            return list.Count > 0 ? list[barPosition] : null;
        }

        /// <summary>
        /// Page up without selecting (focus-only for multi-select).
        /// Returns the pawn at the new position, or null.
        /// </summary>
        public static Pawn PageFocusUp()
        {
            CheckMapChange();
            int posInPage = PositionInPage;

            if (onMechSection)
            {
                if (CurrentPage > 0)
                {
                    barPosition = (CurrentPage - 1) * PageSize + posInPage;
                    AnnouncePageChange();
                }
                else
                {
                    // First mech page — go back to colonists
                    var colonists = GetColonists();
                    if (colonists.Count > 0)
                    {
                        onMechSection = false;
                        barPosition = colonists.Count - 1;
                        AnnounceSectionChange();
                    }
                    else
                    {
                        return null;
                    }
                }
            }
            else
            {
                if (CurrentPage > 0)
                {
                    barPosition = (CurrentPage - 1) * PageSize + posInPage;
                    AnnouncePageChange();
                }
                else
                {
                    return null; // Already on first page
                }
            }

            var list = GetCurrentList();
            ClampPosition();
            return list.Count > 0 ? list[barPosition] : null;
        }

        // ===== RESET =====

        /// <summary>
        /// Resets bar state (e.g., when loading a new game).
        /// </summary>
        public static void Reset()
        {
            barPosition = 0;
            onMechSection = false;
            lastMapId = -1;
        }
    }
}
