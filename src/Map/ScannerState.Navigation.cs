using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public static partial class ScannerState
    {
        public static void NextCategory()
        {
            CycleCategory(1);
        }

        public static void PreviousCategory()
        {
            CycleCategory(-1);
        }

        /// <summary>
        /// Ctrl+PageUp/PageDown. Walks the cached ring and rebuilds ONLY the category it lands
        /// on: the ring's shape and the other counts stay as of the last full build, and the next
        /// item-level action closes that window. "All", "Uncategorized" and a cold ring rebuild
        /// fully.
        /// </summary>
        private static void CycleCategory(int delta)
        {
            if (WorldNavigationState.IsActive) return;

            if (cachedCategories == null || scannerCursor.Categories.Count == 0)
            {
                RefreshItems();
            }
            if (scannerCursor.Categories.Count == 0) return;

            // Changing category ends the navigation session.
            InvalidateNavigationSession();

            int count = scannerCursor.Categories.Count;
            for (int step = 0; step < count; step++)
            {
                int unwrapped = scannerCursor.CategoryIndex + delta;
                if (count > 1 && (unwrapped < 0 || unwrapped >= count))
                    MenuHelper.PlayWrapTone();
                scannerCursor.CategoryIndex = ((unwrapped % count) + count) % count;
                scannerCursor.SubcategoryIndex = 0;
                scannerCursor.ItemIndex = 0;
                scannerCursor.ExtraIndex = 0;

                if (RebuildLandedCategory())
                    break;

                // The rebuild dropped an empty category from the ring; keep cycling.
                count = scannerCursor.Categories.Count;
                if (count == 0) return;
                if (delta > 0)
                    scannerCursor.CategoryIndex--;
            }

            SkipEmptySubcategories(forward: true);
            EnsureSortedForCurrentCursor();

            AnnounceCurrentCategory();
            AnnounceCurrentItem();
        }

        public static void NextSubcategory()
        {
            if (WorldNavigationState.IsActive) return;

            RefreshItems();
            if (scannerCursor.Categories.Count == 0) return;

            var currentCategory = GetCurrentCategory();
            if (currentCategory == null) return;

            // Changing subcategory ends the navigation session.
            InvalidateNavigationSession();

            int startIndex = scannerCursor.SubcategoryIndex;
            do
            {
                scannerCursor.SubcategoryIndex++;
                if (scannerCursor.SubcategoryIndex >= currentCategory.Subcategories.Count)
                {
                    scannerCursor.SubcategoryIndex = 0;
                    MenuHelper.PlayWrapTone();
                }

                if (scannerCursor.SubcategoryIndex == startIndex)
                    break;

            } while (GetCurrentSubcategory()?.IsEmpty ?? true);

            scannerCursor.ItemIndex = 0;
            scannerCursor.ExtraIndex = 0;
            EnsureSortedForCurrentCursor();

            AnnounceCurrentSubcategory();
            AnnounceCurrentItem();
        }

        public static void PreviousSubcategory()
        {
            if (WorldNavigationState.IsActive) return;

            RefreshItems();
            if (scannerCursor.Categories.Count == 0) return;

            var currentCategory = GetCurrentCategory();
            if (currentCategory == null) return;

            // Changing subcategory ends the navigation session.
            InvalidateNavigationSession();

            int startIndex = scannerCursor.SubcategoryIndex;
            do
            {
                scannerCursor.SubcategoryIndex--;
                if (scannerCursor.SubcategoryIndex < 0)
                {
                    scannerCursor.SubcategoryIndex = currentCategory.Subcategories.Count - 1;
                    MenuHelper.PlayWrapTone();
                }

                if (scannerCursor.SubcategoryIndex == startIndex)
                    break;

            } while (GetCurrentSubcategory()?.IsEmpty ?? true);

            scannerCursor.ItemIndex = 0;
            scannerCursor.ExtraIndex = 0;
            EnsureSortedForCurrentCursor();

            AnnounceCurrentSubcategory();
            AnnounceCurrentItem();
        }

        public static void JumpToCurrent(bool manual = false)
        {
            if (WorldNavigationState.IsActive) return;

            if (scannerCursor.Categories.Count == 0)
            {
                RefreshItems();
                if (scannerCursor.Categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            ScannerItem currentItem;
            bool removedStale = false;
            while (true)
            {
                currentItem = GetCurrentItem();
                if (currentItem == null)
                {
                    TolkHelper.Speak((removedStale
                        ? "RimWorldAccess.Map.Scanner.ItemGone"
                        : "RimWorldAccess.Map.Scanner.NoItemSelected").Loc(), SpeechPriority.High);
                    return;
                }

                currentItem.RefreshLabel();
                if (!currentItem.IsStale) break;

                RemoveCurrentStaleItem();
                removedStale = true;
            }

            if (removedStale)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Scanner.ItemGone".Loc(), SpeechPriority.High);
            }

            IntVec3 cursorBeforeJump = MapNavigationState.CurrentCursorPosition;
            bool jumpedToCenter = false;
            TerrainRegion jumpRegion = null;
            IntVec3 targetPosition;

            if (currentItem.IsTerrain || currentItem.HasTerrainRegions)
            {
                // From off the patch, land on the nearest edge tile; a manual Home from on the
                // patch jumps to the region center. Auto-jump always takes the nearest tile.
                if (currentItem.HasTerrainRegions && scannerCursor.ExtraIndex < currentItem.TerrainRegions.Count)
                {
                    var region = currentItem.TerrainRegions[scannerCursor.ExtraIndex];
                    jumpRegion = region;
                    var plan = ClumpNav.PlanHome(region.AllPositions, region.CenterPosition,
                        cursorBeforeJump, CellMetric, manual);
                    targetPosition = plan.Tile;
                    jumpedToCenter = plan.IsCenter;
                }
                else if (currentItem.BulkTerrainPositions != null && scannerCursor.ExtraIndex < currentItem.BulkTerrainPositions.Count)
                {
                    targetPosition = currentItem.BulkTerrainPositions[scannerCursor.ExtraIndex];
                }
                else
                {
                    targetPosition = currentItem.Position;
                }
            }
            else if (currentItem.IsDesignation)
            {
                if (currentItem.BulkDesignations != null && scannerCursor.ExtraIndex < currentItem.BulkDesignations.Count)
                {
                    targetPosition = currentItem.BulkDesignations[scannerCursor.ExtraIndex].target.Cell;
                }
                else
                {
                    targetPosition = currentItem.Position;
                }
            }
            else if (currentItem.IsZone)
            {
                targetPosition = currentItem.Position;
            }
            else if (currentItem.IsRoom)
            {
                targetPosition = currentItem.Position;
            }
            else if (currentItem.IsCapturedEntity)
            {
                // A held pawn is not Spawned and its own Position is stale; the item carries the
                // platform's position instead.
                targetPosition = currentItem.Position;
            }
            else
            {
                Thing targetThing = currentItem.Thing;
                if (currentItem.IsBulkGroup && currentItem.BulkThings != null && scannerCursor.ExtraIndex < currentItem.BulkThings.Count)
                {
                    targetThing = currentItem.BulkThings[scannerCursor.ExtraIndex];
                }
                if (targetThing == null || targetThing.Destroyed || !targetThing.Spawned)
                {
                    TolkHelper.Speak("RimWorldAccess.Map.Scanner.ItemGone".Loc(), SpeechPriority.High);
                    return;
                }
                targetPosition = targetThing.Position;
            }

            // The jump makes this tile the gizmo context, so G targets it rather than a pawn
            // picked earlier. Before the no-move guard, so a Home onto the current tile still
            // re-asserts tile context.
            GizmoNavigationState.PawnJustSelected = false;

            // Landing on the tile already occupied confirms position instead of replaying the
            // terrain sound and the full tile announcement.
            if (targetPosition == cursorBeforeJump)
            {
                bool atCenter = jumpRegion != null
                    && jumpRegion.TileCount > 1
                    && cursorBeforeJump == jumpRegion.CenterPosition;
                string where = atCenter
                    ? (string)"RimWorldAccess.Map.Scanner.AlreadyAtCenter".Translate(currentItem.Label)
                    : (string)"RimWorldAccess.Map.Scanner.AlreadyAt".Translate(currentItem.Label);
                TolkHelper.SpeakData(where, SpeechPriority.Normal);
                return;
            }

            // Guard the write so NotifyCursorWritten does not invalidate the navigation session:
            // this move is scanner-driven.
            navSession.ScannerDrivenJumpInProgress = true;
            try
            {
                MapNavigationState.CurrentCursorPosition = targetPosition;
            }
            finally
            {
                navSession.ScannerDrivenJumpInProgress = false;
            }

            // Cursor-landing lessons fire for a scanner jump exactly as for arrow movement.
            DocsTeacher.NotifyCursorLanded(targetPosition, Find.CurrentMap);

            Find.CameraDriver.JumpToCurrentMapLoc(targetPosition);

            // Every jump announces like an arrow-key step: terrain sound plus tile contents,
            // computed after the move. A jump to a patch center leads with the move delta.
            MapNavigationState.CurrentCameraMode = CameraFollowMode.Cursor;
            TerrainAudioHelper.PlayCellAudio(targetPosition, Find.CurrentMap, 0.5f);

            string prefix = null;
            if (jumpedToCenter)
            {
                string dir = ScannerDirectionHelper.GetCompassDirection(cursorBeforeJump, targetPosition);
                float dist = (targetPosition - cursorBeforeJump).LengthHorizontal;
                prefix = dir != null
                    ? "RimWorldAccess.Map.Scanner.JumpedTilesToCenter".Translate(dist.ToString("F0"), dir).ToString()
                    : "RimWorldAccess.Map.Scanner.JumpedToCenter".Translate().ToString();
            }
            MapArrowKeyHandler.AnnouncePosition(targetPosition, Find.CurrentMap, prefix);
        }

        /// <summary>The focused item's position, or IntVec3.Invalid when none is selected.</summary>
        public static IntVec3 GetCurrentItemPosition()
        {
            var currentItem = GetCurrentItem();
            if (currentItem == null)
                return IntVec3.Invalid;

            return currentItem.Position;
        }

        /// <summary>
        /// True while a Page Up/Down browsing session is live: from BeginNavigationSession until
        /// the map cursor is written from outside the scanner. The visual emphasis lasts exactly
        /// this long.
        /// </summary>
        internal static bool NavigationSessionActive => navSession.Active;

        /// <summary>The focused scanner item, for the visual emphasis driver. Pure read.</summary>
        internal static ScannerItem CurrentFocusedItem => GetCurrentItem();

        /// <summary>Whether the scanner is focused on a temporary category.</summary>
        public static bool IsInTemporaryCategory()
        {
            return scannerCursor.IsInTemporaryCategory();
        }

        /// <summary>
        /// Re-points the indices onto the temporary category, which always sits at the end of the
        /// list after a rebuild. Both indices are clamped, so the selection stays valid.
        /// </summary>
        private static void FocusTemporaryCategory(int desiredSubcategoryIndex, int desiredItemIndex)
        {
            scannerCursor.FocusTemporaryCategory(desiredSubcategoryIndex, desiredItemIndex);
        }

        public static void ReadDistanceAndDirection()
        {
            if (WorldNavigationState.IsActive) return;

            if (scannerCursor.Categories.Count == 0)
            {
                RefreshItems();
                if (scannerCursor.Categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            ScannerItem currentItem;
            bool removedStale = false;
            while (true)
            {
                currentItem = GetCurrentItem();
                if (currentItem == null)
                {
                    TolkHelper.Speak((removedStale
                        ? "RimWorldAccess.Map.Scanner.ItemGone"
                        : "RimWorldAccess.Map.Scanner.NoItemSelected").Loc(), SpeechPriority.High);
                    return;
                }

                currentItem.RefreshLabel();
                if (!currentItem.IsStale) break;

                RemoveCurrentStaleItem();
                removedStale = true;
            }

            if (removedStale)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Scanner.ItemGone".Loc(), SpeechPriority.High);
            }

            // No re-sort: this only reads the live distance and direction. Sort order belongs to
            // the navigation session.
            var cursorPos = MapNavigationState.CurrentCursorPosition;
            IntVec3 targetPos;

            // Area-backed items measure from the nearest cell, not the center, so a cursor inside
            // the area registers as "here".
            if (currentItem.HasTerrainRegions || currentItem.IsZone || currentItem.IsRoom ||
                (currentItem.IsTerrain && currentItem.BulkTerrainPositions != null))
            {
                var nearestCell = FindNearestCell(currentItem, cursorPos);
                targetPos = nearestCell.IsValid ? nearestCell : currentItem.Position;
            }
            else
            {
                if (currentItem.IsBulkGroup && scannerCursor.ExtraIndex < currentItem.BulkCount)
                {
                    if (currentItem.BulkThings != null && scannerCursor.ExtraIndex < currentItem.BulkThings.Count)
                    {
                        Thing targetThing = currentItem.BulkThings[scannerCursor.ExtraIndex];
                        targetPos = targetThing.Position;
                    }
                    else
                    {
                        targetPos = currentItem.Thing?.Position ?? currentItem.Position;
                    }
                }
                else
                {
                    // Live position, for moving targets like pawns.
                    targetPos = currentItem.Thing?.Position ?? currentItem.Position;
                }
            }

            var distance = (targetPos - cursorPos).LengthHorizontal;
            var direction = GetDirectionFromCursor(targetPos);

            string reading = direction != null
                ? (string)"RimWorldAccess.Map.Scanner.DistanceDirection".Translate(distance.ToString("F1"), direction)
                : (string)"RimWorldAccess.Map.Scanner.Here".Translate();

            if (!string.IsNullOrEmpty(currentItem.DetailSuffix))
            {
                reading += "RimWorldAccess.Map.Scanner.Item.CommaSuffix".Translate(currentItem.DetailSuffix);
            }

            TolkHelper.SpeakData(reading, SpeechPriority.Normal);
        }

        private static ScannerCategory GetCurrentCategory() => scannerCursor.GetCurrentCategory();

        private static ScannerSubcategory GetCurrentSubcategory() => scannerCursor.GetCurrentSubcategory();

        private static ScannerItem GetCurrentItem() => scannerCursor.GetCurrentItem();

        // Drops a stale item and clamps the item index to the remaining range.
        private static void RemoveCurrentStaleItem()
        {
            var subcat = GetCurrentSubcategory();
            if (subcat == null) return;
            if (scannerCursor.ItemIndex < 0 || scannerCursor.ItemIndex >= subcat.Items.Count) return;

            subcat.Items.RemoveAt(scannerCursor.ItemIndex);

            if (scannerCursor.ItemIndex >= subcat.Items.Count)
            {
                scannerCursor.ItemIndex = Math.Max(0, subcat.Items.Count - 1);
            }

            scannerCursor.ExtraIndex = 0;
        }

        private static void ValidateIndices() => scannerCursor.ValidateIndices();

        private static void SkipEmptySubcategories(bool forward) => scannerCursor.SkipEmptySubcategories(forward);

        /// <summary>Compass direction from the cursor, or null when it is at the target.</summary>
        private static string GetDirectionFromCursor(IntVec3 targetPosition)
        {
            return ScannerDirectionHelper.GetCompassDirection(
                MapNavigationState.CurrentCursorPosition, targetPosition);
        }

        private static void AnnounceCurrentCategory()
        {
            var category = GetCurrentCategory();
            if (category == null) return;

            AnnounceHeading(ScannerNameLocalizer.LocalizeCategoryName(category.Name), category.LiveCount);
        }

        private static void AnnounceCurrentSubcategory()
        {
            var subcat = GetCurrentSubcategory();
            if (subcat == null) return;

            AnnounceHeading(ScannerNameLocalizer.LocalizeSubcategoryName(subcat.Name), subcat.LiveCount);
        }

        // Counts what exists now (live bulk members, no destroyed things); every caller follows with AnnounceCurrentItem.
        private static void AnnounceHeading(string name, int count)
        {
            TolkHelper.Speak(count == 1
                ? "RimWorldAccess.Map.Scanner.CategoryAnnouncementOne".Loc(name)
                : "RimWorldAccess.Map.Scanner.CategoryAnnouncementMany".Loc(name, count), SpeechPriority.Normal);
        }

        private static void AnnounceCurrentItem()
        {
            ScannerItem item;
            bool removedStale = false;
            while (true)
            {
                item = GetCurrentItem();
                if (item == null)
                {
                    if (removedStale)
                        TolkHelper.Speak("RimWorldAccess.Map.Scanner.ItemGone".Loc(), SpeechPriority.High);
                    else
                        TolkHelper.Speak("RimWorldAccess.Map.Scanner.EmptyCategory".Loc(), SpeechPriority.Normal);
                    return;
                }

                item.RefreshLabel();
                if (!item.IsStale) break;

                RemoveCurrentStaleItem();
                removedStale = true;
            }

            if (removedStale)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Scanner.ItemGone".Loc(), SpeechPriority.High);
            }

            // Terrain regions announce the SPECIFIC region the cursor is on or nearest to, never
            // an aggregate across regions, which would misreport the tile count. The extra index
            // moves too, so Alt+PgDn continues from that region.
            if (item.HasTerrainRegions)
            {
                scannerCursor.ExtraIndex = FindNearestRegionIndex(item, MapNavigationState.CurrentCursorPosition);
                TolkHelper.SpeakData(BuildRegionAnnouncement(item, scannerCursor.ExtraIndex), SpeechPriority.Normal);
                return;
            }

            // Zones and rooms measure from the nearest cell, so being inside reads as "here" and
            // the edge distance is reported, not the distance to the geometric center.
            var currentCursorPos = MapNavigationState.CurrentCursorPosition;
            IntVec3 targetPos;
            if (item.IsZone || item.IsRoom)
            {
                var nearestCell = FindNearestCell(item, currentCursorPos);
                targetPos = nearestCell.IsValid ? nearestCell : item.Position;
            }
            else if (item.IsCapturedEntity)
            {
                // A held pawn is not Spawned; the item carries the platform position.
                targetPos = item.Position;
            }
            else
            {
                targetPos = item.Thing != null && item.Thing.Spawned && !item.Thing.Destroyed
                    ? item.Thing.Position
                    : item.Position;
            }
            var freshDistance = (targetPos - currentCursorPos).LengthHorizontal;
            var itemDirection = GetDirectionFromCursor(targetPos);

            string basicAnnouncement;
            if (itemDirection != null)
            {
                basicAnnouncement = "RimWorldAccess.Map.Scanner.Item.WithDirection".Translate(item.Label, freshDistance.ToString("F1"), itemDirection);
            }
            else
            {
                basicAnnouncement = "RimWorldAccess.Map.Scanner.Item.Here".Translate(item.Label);
            }

            if (!string.IsNullOrEmpty(item.DetailSuffix))
            {
                basicAnnouncement += "RimWorldAccess.Map.Scanner.Item.CommaSuffix".Translate(item.DetailSuffix);
            }

            if (item.Thing is Pawn pawn)
            {
                string locationContext = TileInfoHelper.GetLocationContext(targetPos, Find.CurrentMap);
                if (!string.IsNullOrEmpty(locationContext))
                {
                    basicAnnouncement += "RimWorldAccess.Map.Scanner.Item.LocationSuffix".Translate(locationContext);
                }

                if (RimWorldAccessMod_Settings.Settings?.ShowCoverInfo ?? true)
                {
                    string coverInfo = CoverHelper.GetCoverInfo(pawn);
                    if (!string.IsNullOrEmpty(coverInfo))
                    {
                        basicAnnouncement += "RimWorldAccess.Map.Scanner.Item.CommaSuffix".Translate(coverInfo);
                    }
                }

                if (RimWorldAccessMod_Settings.Settings?.ShowPawnHealthInScanner ?? true)
                {
                    string health = PawnHealthSummary.Of(pawn).Tag();
                    if (!string.IsNullOrEmpty(health))
                    {
                        basicAnnouncement += "RimWorldAccess.Map.Scanner.Item.CommaSuffix".Translate(health);
                    }
                }

                if (RimWorldAccessMod_Settings.Settings?.ShowPawnActivityOnMap ?? true)
                {
                    string activity = PawnHelper.GetPawnActivity(pawn);
                    if (!string.IsNullOrEmpty(activity))
                    {
                        basicAnnouncement += "RimWorldAccess.Map.Scanner.Item.CommaSuffix".Translate(activity);
                    }
                }
            }

            if (item.IsBulkGroup)
            {
                int position = scannerCursor.ExtraIndex + 1;
                basicAnnouncement += "RimWorldAccess.Map.Scanner.Item.BulkSuffix".Translate(position, item.BulkCount);
            }

            TolkHelper.SpeakData(basicAnnouncement, SpeechPriority.Normal);
        }

        private static void AnnounceCurrentBulkItem()
        {
            ScannerItem item;
            bool removedStale = false;
            while (true)
            {
                item = GetCurrentItem();
                if (item == null || !item.IsBulkGroup)
                {
                    if (removedStale)
                        TolkHelper.Speak("RimWorldAccess.Map.Scanner.ItemGone".Loc(), SpeechPriority.High);
                    return;
                }

                item.RefreshLabel();
                if (!item.IsStale) break;

                RemoveCurrentStaleItem();
                removedStale = true;
            }

            if (removedStale)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Scanner.ItemGone".Loc(), SpeechPriority.High);
            }

            if (scannerCursor.ExtraIndex < 0 || scannerCursor.ExtraIndex >= item.BulkCount)
                return;

            // The shared region builder keeps primary and bulk announcements consistent.
            if (item.HasTerrainRegions)
            {
                if (scannerCursor.ExtraIndex >= item.TerrainRegions.Count)
                    return;
                TolkHelper.SpeakData(BuildRegionAnnouncement(item, scannerCursor.ExtraIndex), SpeechPriority.Normal);
                return;
            }

            if (item.IsTerrain && item.BulkTerrainPositions != null)
            {
                var terrainPosition = scannerCursor.ExtraIndex + 1;
                TolkHelper.Speak("RimWorldAccess.Map.Scanner.Item.TerrainBulk".Loc(item.Label, terrainPosition, item.BulkCount), SpeechPriority.Normal);
                return;
            }

            if (item.IsDesignation)
            {
                if (item.BulkDesignations == null || scannerCursor.ExtraIndex >= item.BulkDesignations.Count)
                    return;

                var targetDesignation = item.BulkDesignations[scannerCursor.ExtraIndex];
                var desTargetPos = targetDesignation.target.Cell;
                var desDistance = (desTargetPos - MapNavigationState.CurrentCursorPosition).LengthHorizontal;
                var desDirection = GetDirectionFromCursor(desTargetPos);
                var desPosition = scannerCursor.ExtraIndex + 1;

                string designationLabel;
                if (targetDesignation.target.HasThing && targetDesignation.target.Thing != null)
                {
                    designationLabel = targetDesignation.target.Thing.LabelShort;
                }
                else
                {
                    // Cell-based designations have no thing to name.
                    designationLabel = item.Label;
                }

                if (desDirection != null)
                {
                    TolkHelper.Speak("RimWorldAccess.Map.Scanner.Item.WithDirectionBulk".Loc(designationLabel, desDistance.ToString("F1"), desDirection, desPosition, item.BulkCount), SpeechPriority.Normal);
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Map.Scanner.Item.HereBulk".Loc(designationLabel, desPosition, item.BulkCount), SpeechPriority.Normal);
                }
                return;
            }

            if (item.BulkThings == null || scannerCursor.ExtraIndex >= item.BulkThings.Count)
                return;

            var targetThing = item.BulkThings[scannerCursor.ExtraIndex];
            if (targetThing == null)
                return;

            var thingTargetPos = targetThing.Position;
            var distance = (thingTargetPos - MapNavigationState.CurrentCursorPosition).LengthHorizontal;
            var thingDirection = GetDirectionFromCursor(thingTargetPos);
            var position = scannerCursor.ExtraIndex + 1;

            // This specific thing, not the group label.
            string thingLabel = targetThing.LabelShort ?? targetThing.def?.label ?? item.Label;

            if (thingDirection != null)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Scanner.Item.WithDirectionBulk".Loc(thingLabel, distance.ToString("F1"), thingDirection, position, item.BulkCount), SpeechPriority.Normal);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Map.Scanner.Item.HereBulk".Loc(thingLabel, position, item.BulkCount), SpeechPriority.Normal);
            }
        }

        /// <summary>Home: the closest item in the current subcategory.</summary>
        public static void JumpToFirstItem()
        {
            if (WorldNavigationState.IsActive) return;

            if (scannerCursor.Categories.Count == 0)
            {
                RefreshItems();
                if (scannerCursor.Categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            var currentSubcat = GetCurrentSubcategory();
            if (currentSubcat == null || currentSubcat.Items.Count == 0) return;

            // A fresh session re-anchored to the cursor, so Page Down walks forward in a stable
            // order from here.
            InvalidateNavigationSession();
            BeginNavigationSession();
            scannerCursor.ItemIndex = 0;
            scannerCursor.ExtraIndex = 0;

            if (autoJumpMode)
            {
                JumpToCurrent();
            }
            else
            {
                AnnounceCurrentItem();
            }
            navSession.RecordPosition(MapNavigationState.CurrentCursorPosition);
        }

        /// <summary>End: the farthest item in the current subcategory.</summary>
        public static void JumpToLastItem()
        {
            if (WorldNavigationState.IsActive) return;

            if (scannerCursor.Categories.Count == 0)
            {
                RefreshItems();
                if (scannerCursor.Categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            var currentSubcat = GetCurrentSubcategory();
            if (currentSubcat == null || currentSubcat.Items.Count == 0) return;

            // A fresh session re-anchored to the cursor, so Page Up walks backward in a stable
            // order from here.
            InvalidateNavigationSession();
            BeginNavigationSession();
            scannerCursor.ItemIndex = currentSubcat.Items.Count - 1;
            scannerCursor.ExtraIndex = 0;

            if (autoJumpMode)
            {
                JumpToCurrent();
            }
            else
            {
                AnnounceCurrentItem();
            }
            navSession.RecordPosition(MapNavigationState.CurrentCursorPosition);
        }
    }
}
