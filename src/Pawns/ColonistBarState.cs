using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Page-based keyboard navigation of the colonist bar. The bar is an ordered list of sections —
    /// colonists, colony mechs, then other player-controlled units (see PlayerControllables for that
    /// section's membership signal) — each paged in groups of 10. Crossing past the end of a section
    /// moves into the next non-empty one; empty sections are never landed on or announced. Colonists
    /// are the only reorderable section, and on any other section comma/period cycles within that
    /// section instead of colonists.
    /// </summary>
    public static class ColonistBarState
    {
        public const int PageSize = 10;

        /// <summary>One section of the bar. GetPawns is a live supplier, never cached across frames.</summary>
        private sealed class BarSection
        {
            public string Id;
            public System.Func<List<Pawn>> GetPawns;
            public bool Reorderable;
            public string SectionNameKey;
            public string PageKey;
            public string NoneHereKey;
            public string NoneAtPositionKey;
        }

        private static readonly List<BarSection> Sections = new List<BarSection>
        {
            new BarSection
            {
                Id = "colonists",
                GetPawns = GetColonists,
                Reorderable = true,
                SectionNameKey = "RimWorldAccess.Pawns.Bar.SectionColonists",
                PageKey = "RimWorldAccess.Pawns.Bar.Page",
                NoneHereKey = "RimWorldAccess.Pawns.Bar.NoColonistsHere",
                NoneAtPositionKey = "RimWorldAccess.Pawns.Bar.NoColonistAtPosition",
            },
            new BarSection
            {
                Id = "mechs",
                GetPawns = GetMechs,
                Reorderable = false,
                SectionNameKey = "RimWorldAccess.Pawns.Bar.SectionMechs",
                PageKey = "RimWorldAccess.Pawns.Bar.MechsPage",
                NoneHereKey = "RimWorldAccess.Pawns.Bar.NoMechsHere",
                NoneAtPositionKey = "RimWorldAccess.Pawns.Bar.NoMechAtPosition",
            },
            new BarSection
            {
                Id = "controllables",
                GetPawns = GetControllables,
                Reorderable = false,
                SectionNameKey = "RimWorldAccess.Pawns.Bar.SectionControllables",
                PageKey = "RimWorldAccess.Pawns.Bar.ControllablesPage",
                NoneHereKey = "RimWorldAccess.Pawns.Bar.NoControllablesHere",
                NoneAtPositionKey = "RimWorldAccess.Pawns.Bar.NoControllableAtPosition",
            },
        };

        /// <summary>0-indexed position into the current section's list.</summary>
        private static int barPosition = 0;

        private static int sectionIndex = 0;

        /// <summary>The map last navigated on; a change resets the cursor.</summary>
        private static int lastMapId = -1;

        public static bool IsOnMechSection => Sections[sectionIndex].Id == "mechs";

        /// <summary>True on mechs or other controllables, where comma/period cycles within the section.</summary>
        public static bool IsOnNonColonistSection => Sections[sectionIndex].Id != "colonists";

        /// <summary>The current section's "no pawns here" key, for callers that announce a cycling failure themselves.</summary>
        public static string CurrentSectionNoneHereKey => Sections[sectionIndex].NoneHereKey;

        /// <summary>Current page number, 0-indexed.</summary>
        public static int CurrentPage => barPosition / PageSize;

        /// <summary>Position within the current page, 0-9.</summary>
        public static int PositionInPage => barPosition % PageSize;

        /// <summary>Current 0-indexed bar position.</summary>
        public static int BarPosition => barPosition;

        /// <summary>Colonists on the current map in bar display order, the same source comma/period cycling uses.</summary>
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

        /// <summary>Colony mechs on the current map; empty without Biotech.</summary>
        private static List<Pawn> GetMechs()
        {
            if (!ModsConfig.BiotechActive || Find.CurrentMap == null)
                return new List<Pawn>();

            // Mirrors PawnTable_Mechs.LabelSortFunction so the bar and the Mechs menu agree; the
            // NaturalStringComparer tiebreaker keeps "Lifter 10" after "Lifter 2".
            return Find.CurrentMap.mapPawns.SpawnedColonyMechs
                .OrderBy(p => p.GetOverseer()?.thingIDNumber ?? int.MaxValue)
                .ThenBy(p => p.GetMechControlGroup()?.Index ?? int.MaxValue)
                .ThenBy(p => p.KindLabel)
                .ThenBy(p => p.Label, NaturalStringComparer.Instance)
                .ToList();
        }

        /// <summary>
        /// Player-controlled units on the current map that are neither colonists nor colony mechs.
        /// Pawns already in GetColonists are excluded, guarding against a mod that injects its own
        /// pawns into the vanilla colonist bar.
        /// </summary>
        private static List<Pawn> GetControllables()
        {
            Map map = Find.CurrentMap;
            if (map == null)
                return new List<Pawn>();

            var colonists = GetColonists();
            return PlayerControllables.OnMap(map)
                .Where(p => !colonists.Contains(p))
                .ToList();
        }

        private static List<Pawn> GetCurrentList()
        {
            return Sections[sectionIndex].GetPawns();
        }

        /// <summary>The next section after fromIndex with at least one pawn, or -1.</summary>
        private static int NextNonEmptySection(int fromIndex)
        {
            for (int i = fromIndex + 1; i < Sections.Count; i++)
            {
                if (Sections[i].GetPawns().Count > 0)
                    return i;
            }
            return -1;
        }

        /// <summary>The previous section before fromIndex with at least one pawn, or -1.</summary>
        private static int PreviousNonEmptySection(int fromIndex)
        {
            for (int i = fromIndex - 1; i >= 0; i--)
            {
                if (Sections[i].GetPawns().Count > 0)
                    return i;
            }
            return -1;
        }

        /// <summary>The pawn's colonist-bar entry group, or -1 when it has no entry.</summary>
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

        /// <summary>The pawn's index within its group, counted the way ColonistBar.Reorder counts (non-null pawns only).</summary>
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
        /// Assigns sequential displayOrder values across the group. Required before every Reorder
        /// call: duplicate displayOrder values make Reorder bump ALL pawns at the target order, so
        /// the moved pawn never passes them.
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

        /// <summary>Resets the cursor when the map changed. Called at the start of every navigation action.</summary>
        private static void CheckMapChange()
        {
            int currentMapId = Find.CurrentMap?.uniqueID ?? -1;
            if (currentMapId != lastMapId)
            {
                barPosition = 0;
                sectionIndex = 0;
                lastMapId = currentMapId;
            }
        }

        /// <summary>Clamps barPosition into the current list, which shrinks when a colonist dies or leaves.</summary>
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

        /// <summary>Moves to the next pawn, crossing page and section boundaries.</summary>
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
                int nextSection = NextNonEmptySection(sectionIndex);
                if (nextSection < 0)
                {
                    MenuHelper.PlayEdgeTone();
                    return;
                }
                sectionIndex = nextSection;
                barPosition = 0;
                AnnounceSectionChange();
            }

            SelectAndAnnounce();
        }

        /// <summary>Moves to the previous pawn, crossing page and section boundaries.</summary>
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
                int prevSection = sectionIndex == 0 ? -1 : PreviousNonEmptySection(sectionIndex);
                if (prevSection < 0)
                {
                    MenuHelper.PlayEdgeTone();
                    return;
                }
                sectionIndex = prevSection;
                barPosition = Sections[prevSection].GetPawns().Count - 1;
                AnnounceSectionChange();
            }

            SelectAndAnnounce();
        }

        /// <summary>
        /// Jumps to the next page, preserving position within the page; on a section's last page it
        /// switches to the next non-empty section.
        /// </summary>
        public static void PageDown()
        {
            CheckMapChange();
            int posInPage = PositionInPage;

            var list = GetCurrentList();
            if (list.Count == 0)
            {
                int emptySectionNext = NextNonEmptySection(sectionIndex);
                if (emptySectionNext >= 0)
                {
                    sectionIndex = emptySectionNext;
                    var newList = Sections[emptySectionNext].GetPawns();
                    barPosition = System.Math.Min(posInPage, newList.Count - 1);
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
            if (targetPosition >= list.Count)
                targetPosition = list.Count - 1;

            if (targetPosition / PageSize == CurrentPage)
            {
                // No new page inside this section; cross into the next non-empty one.
                int nextSection = NextNonEmptySection(sectionIndex);
                if (nextSection >= 0)
                {
                    sectionIndex = nextSection;
                    var newList = Sections[nextSection].GetPawns();
                    barPosition = System.Math.Min(posInPage, newList.Count - 1);
                    AnnounceSectionChange();
                    SelectAndAnnounce();
                }
                else
                {
                    MenuHelper.PlayEdgeTone();
                }
            }
            else
            {
                barPosition = targetPosition;
                AnnouncePageChange();
                SelectAndAnnounce();
            }
        }

        /// <summary>
        /// Jumps to the previous page, preserving position within the page; on a section's first page
        /// it switches back to the previous non-empty section.
        /// </summary>
        public static void PageUp()
        {
            CheckMapChange();
            int posInPage = PositionInPage;

            if (CurrentPage > 0)
            {
                barPosition = (CurrentPage - 1) * PageSize + posInPage;
                AnnouncePageChange();
                SelectAndAnnounce();
                return;
            }

            int prevSection = PreviousNonEmptySection(sectionIndex);
            if (prevSection >= 0)
            {
                sectionIndex = prevSection;
                var prevList = Sections[prevSection].GetPawns();
                // Land on the previous section's last page, keeping the position within it.
                int lastPageStart = ((prevList.Count - 1) / PageSize) * PageSize;
                barPosition = System.Math.Min(lastPageStart + posInPage, prevList.Count - 1);
                AnnounceSectionChange();
                SelectAndAnnounce();
            }
            else
            {
                MenuHelper.PlayEdgeTone();
            }
        }

        /// <summary>Jumps to a 0-indexed position on the current page.</summary>
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
                TolkHelper.SpeakData(Sections[sectionIndex].NoneAtPositionKey.Translate((positionOnPage + 1).ToString()).ToString());
                return;
            }

            barPosition = targetIndex;
            SelectAndAnnounce();
        }

        // A second press of the same position within the threshold forces a full camera jump,
        // overriding multi-select focus mode.
        private static int lastAltNumberPosition = -1;
        private static float lastAltNumberTime = -1f;
        private const float AltNumberDoubleTapThreshold = 0.5f;

        /// <summary>
        /// First press selects with a camera snap (focus-only in multi-select); a second press of the
        /// same position within the threshold does the full cursor-and-camera jump.
        /// </summary>
        public static void HandleAltNumberPress(int positionOnPage)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            bool isDoubleTap = lastAltNumberPosition == positionOnPage &&
                               now - lastAltNumberTime <= AltNumberDoubleTapThreshold;

            if (isDoubleTap)
            {
                lastAltNumberPosition = -1;
                lastAltNumberTime = -1f;
                JumpCursorAndCameraToPosition(positionOnPage);
                return;
            }

            lastAltNumberPosition = positionOnPage;
            lastAltNumberTime = now;

            if (MultiSelectState.IsMultiSelectMode)
            {
                var pawn = JumpFocusToPosition(positionOnPage);
                if (pawn != null)
                {
                    MultiSelectState.SetFocusedPawn(pawn);
                    MultiSelectState.AnnounceFocusedPawn(pawn);
                }
            }
            else
            {
                JumpToPosition(positionOnPage);
            }
        }

        /// <summary>
        /// Acts as if the cursor moved to the pawn's tile: selects it, moves the map cursor, snaps
        /// the camera, plays terrain audio and announces the pawn with its tile info.
        /// </summary>
        private static void JumpCursorAndCameraToPosition(int positionOnPage)
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
                TolkHelper.SpeakData(Sections[sectionIndex].NoneAtPositionKey.Translate((positionOnPage + 1).ToString()).ToString());
                return;
            }

            barPosition = targetIndex;
            Pawn pawn = list[targetIndex];
            var map = Find.CurrentMap;
            var pos = pawn.Position;

            // Selecting a different pawn would end an active targeting or placement session;
            // redirect to a cursor jump instead.
            if (PawnSelectionState.TryRedirectForTargetingOrPlacement(pawn))
                return;

            if (Find.Selector != null)
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(pawn, playSound: false, forceDesignatorDeselect: !ShapePlacementState.IsActive);
            }
            PawnSelectionState.SyncFromBarNavigation(pawn);

            MapNavigationState.CurrentCursorPosition = pos;
            if (Find.CameraDriver != null)
                Find.CameraDriver.JumpToCurrentMapLoc(pos);
            MapNavigationState.CurrentCameraMode = CameraFollowMode.Cursor;

            TerrainAudioHelper.PlayCellAudio(pos, map, 0.5f);

            MapNavigationState.LastAnnouncedInfo = "";
            string tileInfo = TileInfoHelper.GetTileSummary(pos, map);
            TolkHelper.SpeakData("RimWorldAccess.Pawns.Bar.JumpedTo".Translate(pawn.LabelShort, tileInfo).ToString());
            MapNavigationState.LastAnnouncedInfo = tileInfo;
        }

        /// <summary>Shift/insert-reorders the current colonist one place right. Colonists section only.</summary>
        public static void MoveRight()
        {
            CheckMapChange();

            if (!Sections[sectionIndex].Reorderable)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bar.CannotReorderSection".Loc());
                return;
            }

            var colonists = GetColonists();
            if (colonists.Count < 2)
                return;

            ClampPosition();

            if (barPosition >= colonists.Count - 1)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bar.AlreadyAtLastPosition".Loc());
                return;
            }

            Pawn pawnToMove = colonists[barPosition];
            Pawn swapWith = colonists[barPosition + 1];

            int group = GetGroupForPawn(pawnToMove);
            if (group < 0) return;

            NormalizeGroupDisplayOrders(group);
            Find.ColonistBar.MarkColonistsDirty();

            int fromIndex = GetEntryIndexForPawn(pawnToMove, group);
            int targetIndex = GetEntryIndexForPawn(swapWith, group);
            if (fromIndex < 0 || targetIndex < 0) return;

            Find.ColonistBar.Reorder(fromIndex, targetIndex + 1, group);
            barPosition++;

            AnnounceReorder(pawnToMove);
        }

        /// <summary>Shift/insert-reorders the current colonist one place left. Colonists section only.</summary>
        public static void MoveLeft()
        {
            CheckMapChange();

            if (!Sections[sectionIndex].Reorderable)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bar.CannotReorderSection".Loc());
                return;
            }

            var colonists = GetColonists();
            if (colonists.Count < 2)
                return;

            ClampPosition();

            if (barPosition <= 0)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bar.AlreadyAtFirstPosition".Loc());
                return;
            }

            Pawn pawnToMove = colonists[barPosition];
            Pawn swapWith = colonists[barPosition - 1];

            int group = GetGroupForPawn(pawnToMove);
            if (group < 0) return;

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
        /// Reorders the current colonist to the slot directly below on the next page, clamped to
        /// that page's last position. Colonists section only.
        /// </summary>
        public static void MoveDown()
        {
            CheckMapChange();

            if (!Sections[sectionIndex].Reorderable)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bar.CannotReorderSection".Loc());
                return;
            }

            var colonists = GetColonists();
            if (colonists.Count < 2)
                return;

            ClampPosition();

            int targetBarPosition = System.Math.Min(barPosition + PageSize, colonists.Count - 1);

            if (targetBarPosition / PageSize == barPosition / PageSize)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bar.AlreadyOnLastPage".Loc());
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

            // Moving forward inserts after the target.
            Find.ColonistBar.Reorder(fromIndex, targetIndex + 1, group);

            AnnounceReorder(pawnToMove);
        }

        /// <summary>
        /// Reorders the current colonist to the slot directly above on the previous page, clamped to
        /// position 0. Colonists section only.
        /// </summary>
        public static void MoveUp()
        {
            CheckMapChange();

            if (!Sections[sectionIndex].Reorderable)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bar.CannotReorderSection".Loc());
                return;
            }

            var colonists = GetColonists();
            if (colonists.Count < 2)
                return;

            ClampPosition();

            int targetBarPosition = System.Math.Max(barPosition - PageSize, 0);

            if (targetBarPosition / PageSize == barPosition / PageSize)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bar.AlreadyOnFirstPage".Loc());
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

            // Moving backward inserts before the target.
            Find.ColonistBar.Reorder(fromIndex, targetIndex, group);

            AnnounceReorder(pawnToMove);
        }

        /// <summary>Keeps the bar position in sync after an external selection, settling on the first section that lists the pawn.</summary>
        public static void SyncBarPosition(Pawn pawn)
        {
            if (pawn == null)
                return;

            CheckMapChange();

            for (int i = 0; i < Sections.Count; i++)
            {
                var pawns = Sections[i].GetPawns();
                int idx = pawns.IndexOf(pawn);
                if (idx >= 0)
                {
                    barPosition = idx;
                    sectionIndex = i;
                    return;
                }
            }
        }

        /// <summary>The next pawn in the current section, wrapping; null when the section is empty.</summary>
        public static Pawn SelectNextInSection()
        {
            CheckMapChange();
            var list = GetCurrentList();
            if (list.Count == 0)
                return null;

            ClampPosition();
            if (barPosition == list.Count - 1 && list.Count > 1)
                MenuHelper.PlayWrapTone();
            barPosition = (barPosition + 1) % list.Count;
            return list[barPosition];
        }

        /// <summary>The previous pawn in the current section, wrapping; null when the section is empty.</summary>
        public static Pawn SelectPreviousInSection()
        {
            CheckMapChange();
            var list = GetCurrentList();
            if (list.Count == 0)
                return null;

            ClampPosition();
            if (barPosition == 0 && list.Count > 1)
                MenuHelper.PlayWrapTone();
            barPosition = (barPosition - 1 + list.Count) % list.Count;
            return list[barPosition];
        }

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

        /// <summary>Clears the selection, selects the pawn, jumps the camera and enables pawn follow.</summary>
        private static void SelectPawnInGame(Pawn pawn)
        {
            if (pawn == null)
                return;

            // Redirect to a cursor jump while targeting or placing, so the session stays alive.
            if (PawnSelectionState.TryRedirectForTargetingOrPlacement(pawn))
                return;

            if (Find.Selector != null)
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(pawn, playSound: true, forceDesignatorDeselect: !ShapePlacementState.IsActive);
            }

            if (Find.CameraDriver != null)
            {
                Find.CameraDriver.JumpToCurrentMapLoc(pawn.Position);
            }

            MapNavigationState.CurrentCameraMode = CameraFollowMode.Pawn;
            GizmoNavigationState.PawnJustSelected = true;

            PawnSelectionState.SyncFromBarNavigation(pawn);
        }

        /// <summary>
        /// One bar entry's spoken line: who it is, where it is, what it is doing. Shared with the
        /// pointer-hover reader so a hovered and a focused entry read alike; the position fragment
        /// belongs to the keyboard cursor and stays with the caller. A dead colonist answers with the
        /// corpse's label, since vanilla resolves the same entry to the corpse and a corpse has no
        /// job to report.
        /// </summary>
        internal static string ComposeEntryLine(Thing entry)
        {
            Pawn pawn = entry as Pawn;
            if (pawn == null || pawn.Dead)
                return entry != null ? entry.LabelCap : "";

            string task = pawn.GetJobReport();
            if (string.IsNullOrEmpty(task))
                task = "RimWorldAccess.Pawns.Bar.Idle".Translate();

            var contextParts = new List<string>();
            if (pawn.Spawned && pawn.Map != null)
            {
                string location = TileInfoHelper.GetLocationContextPlain(pawn.Position, pawn.Map);
                if (!string.IsNullOrEmpty(location))
                    contextParts.Add(location);
            }

            if (RimWorldAccessMod_Settings.Settings?.ShowCoverInfo ?? true)
            {
                string coverInfo = CoverHelper.GetCoverInfo(pawn);
                if (!string.IsNullOrEmpty(coverInfo))
                    contextParts.Add(coverInfo);
            }

            return contextParts.Count > 0
                ? "RimWorldAccess.Pawns.Bar.PawnTaskWithCover".Translate(pawn.LabelShort, contextParts.ToCommaList(useAnd: false), task).ToString()
                : "RimWorldAccess.Pawns.Bar.PawnTask".Translate(pawn.LabelShort, task).ToString();
        }

        /// <summary>Announces the current pawn, appending its position when that setting is on.</summary>
        private static void AnnouncePawn(Pawn pawn, int totalInSection)
        {
            string announcement = ComposeEntryLine(pawn);
            string positionPart = MenuHelper.FormatPosition(barPosition, totalInSection);
            if (!string.IsNullOrEmpty(positionPart))
                announcement = "RimWorldAccess.Pawns.Bar.WithPosition".Translate(announcement, positionPart).ToString();

            TolkHelper.SpeakData(announcement);
        }

        private static void AnnouncePageChange()
        {
            string pageNum = (CurrentPage + 1).ToString();
            TolkHelper.SpeakData(Sections[sectionIndex].PageKey.Translate(pageNum).ToString());
        }

        private static void AnnounceSectionChange()
        {
            TolkHelper.Speak(Sections[sectionIndex].SectionNameKey.Loc());
        }

        /// <summary>Announces where the moved pawn landed, with its new neighbours, and follows it.</summary>
        private static void AnnounceReorder(Pawn pawn)
        {
            var colonists = GetColonists();
            int newIndex = colonists.IndexOf(pawn);
            if (newIndex < 0)
                return;

            barPosition = newIndex;

            string context;
            if (newIndex == 0 && colonists.Count == 1)
                context = "RimWorldAccess.Pawns.Bar.OnlyColonist".Translate().ToString();
            else if (newIndex == 0)
                context = "RimWorldAccess.Pawns.Bar.Leftmost".Translate().ToString();
            else if (newIndex == colonists.Count - 1)
                context = "RimWorldAccess.Pawns.Bar.Rightmost".Translate().ToString();
            else
                context = "RimWorldAccess.Pawns.Bar.Between".Translate(colonists[newIndex - 1].LabelShort, colonists[newIndex + 1].LabelShort).ToString();

            TolkHelper.SpeakData("RimWorldAccess.Pawns.Bar.ReorderResult".Translate(pawn.LabelShort, (newIndex + 1).ToString(), context).ToString());
        }

        private static void AnnounceEmpty()
        {
            TolkHelper.Speak(Sections[sectionIndex].NoneHereKey.Loc());
        }

        public static List<Pawn> GetColonistsPublic()
        {
            return GetColonists();
        }

        public static List<Pawn> GetCurrentSectionPawns()
        {
            return GetCurrentList();
        }

        /// <summary>A bar index comparable across every section, or -1 when the pawn is not on this map's bar.</summary>
        public static int GetGlobalBarIndex(Pawn pawn)
        {
            if (pawn == null)
                return -1;

            int offset = 0;
            foreach (var section in Sections)
            {
                var pawns = section.GetPawns();
                int idx = pawns.IndexOf(pawn);
                if (idx >= 0)
                    return offset + idx;
                offset += pawns.Count;
            }

            return -1;
        }

        /// <summary>
        /// Focuses the bar on the pawn under the map cursor, searching every section so this works on
        /// vehicles too; announces "not on bar" when the cursor is over no bar-eligible pawn.
        /// </summary>
        public static void FocusPawnByCursor()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bar.NotOnBar".Loc());
                return;
            }

            IntVec3 cursor = MapNavigationState.CurrentCursorPosition;
            if (!cursor.IsValid || !cursor.InBounds(map))
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bar.NotOnBar".Loc());
                return;
            }

            Pawn pawn = cursor.GetThingList(map)
                .OfType<Pawn>()
                .FirstOrDefault(p => p != null && p.Spawned);

            if (pawn == null)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bar.NotOnBar".Loc());
                return;
            }

            CheckMapChange();

            bool onBar = Sections.Any(section => section.GetPawns().Contains(pawn));
            if (!onBar)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bar.NotOnBar".Loc());
                return;
            }

            SelectPawnInGame(pawn);
            SyncBarPosition(pawn);
            AnnouncePawn(pawn, GetCurrentList().Count);
        }

        /// <summary>The pawn at the current bar position, without selecting it.</summary>
        public static Pawn GetPawnAtCurrentPosition()
        {
            CheckMapChange();
            ClampPosition();
            var list = GetCurrentList();
            if (list.Count == 0 || barPosition >= list.Count)
                return null;
            return list[barPosition];
        }

        /// <summary>Moves the cursor right and returns the pawn there; never selects it or moves the camera.</summary>
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
                int nextSection = NextNonEmptySection(sectionIndex);
                if (nextSection < 0)
                {
                    MenuHelper.PlayEdgeTone();
                    return null;
                }
                sectionIndex = nextSection;
                barPosition = 0;
                AnnounceSectionChange();
            }

            list = GetCurrentList();
            ClampPosition();
            return list.Count > 0 ? list[barPosition] : null;
        }

        /// <summary>Moves the cursor left and returns the pawn there; never selects it or moves the camera.</summary>
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
                int prevSection = sectionIndex == 0 ? -1 : PreviousNonEmptySection(sectionIndex);
                if (prevSection < 0)
                {
                    MenuHelper.PlayEdgeTone();
                    return null;
                }
                sectionIndex = prevSection;
                barPosition = Sections[prevSection].GetPawns().Count - 1;
                AnnounceSectionChange();
            }

            list = GetCurrentList();
            ClampPosition();
            return list.Count > 0 ? list[barPosition] : null;
        }

        /// <summary>Moves the cursor to a position on the current page without selecting; null when out of range.</summary>
        public static Pawn JumpFocusToPosition(int positionOnPage)
        {
            CheckMapChange();
            var list = GetCurrentList();
            if (list.Count == 0)
                return null;

            int targetIndex = CurrentPage * PageSize + positionOnPage;
            if (targetIndex >= list.Count)
            {
                TolkHelper.SpeakData(Sections[sectionIndex].NoneAtPositionKey.Translate((positionOnPage + 1).ToString()).ToString());
                return null;
            }

            barPosition = targetIndex;
            return list[barPosition];
        }

        /// <summary>Pages down without selecting; returns the pawn at the new position, or null.</summary>
        public static Pawn PageFocusDown()
        {
            CheckMapChange();
            int posInPage = PositionInPage;

            var list = GetCurrentList();
            if (list.Count == 0)
            {
                int nextSection = NextNonEmptySection(sectionIndex);
                if (nextSection < 0)
                    return null;

                sectionIndex = nextSection;
                var newList = Sections[nextSection].GetPawns();
                barPosition = System.Math.Min(posInPage, newList.Count - 1);
                AnnounceSectionChange();
                return newList.Count > 0 ? newList[barPosition] : null;
            }

            int targetPosition = barPosition + PageSize;
            if (targetPosition >= list.Count)
                targetPosition = list.Count - 1;

            if (targetPosition / PageSize == CurrentPage)
            {
                int nextSection = NextNonEmptySection(sectionIndex);
                if (nextSection < 0)
                    return null;

                sectionIndex = nextSection;
                var newList = Sections[nextSection].GetPawns();
                barPosition = System.Math.Min(posInPage, newList.Count - 1);
                AnnounceSectionChange();
                return newList.Count > 0 ? newList[barPosition] : null;
            }

            barPosition = targetPosition;
            AnnouncePageChange();

            list = GetCurrentList();
            ClampPosition();
            return list.Count > 0 ? list[barPosition] : null;
        }

        /// <summary>Pages up without selecting; returns the pawn at the new position, or null.</summary>
        public static Pawn PageFocusUp()
        {
            CheckMapChange();
            int posInPage = PositionInPage;

            if (CurrentPage > 0)
            {
                barPosition = (CurrentPage - 1) * PageSize + posInPage;
                AnnouncePageChange();
            }
            else
            {
                int prevSection = PreviousNonEmptySection(sectionIndex);
                if (prevSection < 0)
                    return null;

                sectionIndex = prevSection;
                barPosition = Sections[prevSection].GetPawns().Count - 1;
                AnnounceSectionChange();
            }

            var list = GetCurrentList();
            ClampPosition();
            return list.Count > 0 ? list[barPosition] : null;
        }

        public static void Reset()
        {
            barPosition = 0;
            sectionIndex = 0;
            lastMapId = -1;
        }
    }
}
