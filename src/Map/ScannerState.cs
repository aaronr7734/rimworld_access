using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public static class ScannerState
    {
        private static List<ScannerCategory> categories = new List<ScannerCategory>();
        private static int currentCategoryIndex = 0;
        private static int currentSubcategoryIndex = 0;
        private static int currentItemIndex = 0;
        private static int currentBulkIndex = 0; // Index within a bulk group

        // The cursor position used to sort the current subcategory's items.
        // When the cursor moves, EnsureSortedForCurrentCursor detects the mismatch,
        // re-sorts by live distance, and jumps the selection to index 0 (closest item).
        private static IntVec3 lastSortedCursorPosition = IntVec3.Invalid;
        private static bool autoJumpMode = false; // Auto-jump to items when navigating

        // Saved focus state for temporary category operations
        private static int savedCategoryIndex = -1;
        private static int savedSubcategoryIndex = -1;
        private static int savedItemIndex = -1;
        private static int savedBulkIndex = -1;

        // Temporary category tracking
        private static ScannerCategory temporaryCategory = null;

        // Cache for CollectMapItems to avoid expensive re-collection on every keystroke
        private static List<ScannerCategory> cachedCategories = null;
        private static int lastThingStateHash = 0;
        private static int lastDesignationCount = 0;
        private static int lastZoneCount = 0;
        private static int lastZoneCellHash = 0;

        // Navigation session: within a sequence of Page Up/Down presses the sort order is
        // frozen so sequential navigation is stable and fast (no per-press re-sort). A session
        // ends when the cursor moves externally, the subcategory changes, the map state
        // changes (pawn dies, designation added, zone edited), or search state changes.
        private static bool navSessionActive = false;
        private static IntVec3 lastScannerCursor = IntVec3.Invalid;
        private static bool scannerDrivenJumpInProgress = false;

        /// <summary>
        /// Toggles auto-jump mode on/off (Alt+Home).
        /// When enabled, cursor automatically jumps to items as you navigate.
        /// </summary>
        public static void ToggleAutoJumpMode()
        {
            autoJumpMode = !autoJumpMode;
            string status = autoJumpMode ? "enabled" : "disabled";
            TolkHelper.Speak($"Auto-jump mode {status}", SpeechPriority.High);
        }

        /// <summary>
        /// Invalidates the current navigation session. Next Page Up/Down will re-sort from
        /// the current cursor position and start a fresh session.
        /// </summary>
        public static void InvalidateNavigationSession()
        {
            navSessionActive = false;
            lastScannerCursor = IntVec3.Invalid;
        }

        /// <summary>
        /// Called by MapNavigationState.CurrentCursorPosition setter whenever the cursor is
        /// written. Invalidates the nav session unless the write is coming from our own
        /// JumpToCurrent (which sets scannerDrivenJumpInProgress for the duration of the write).
        /// </summary>
        public static void NotifyCursorWritten()
        {
            if (!scannerDrivenJumpInProgress)
                InvalidateNavigationSession();
        }

        /// <summary>
        /// Starts a fresh navigation session: sorts the current subcategory from the current
        /// cursor, marks the session active, and records the cursor as the anchor. Called on
        /// the first Page Up/Down in a session and on Home/End.
        /// </summary>
        private static void BeginNavigationSession()
        {
            EnsureSortedForCurrentCursor();
            navSessionActive = true;
            lastScannerCursor = MapNavigationState.CurrentCursorPosition;
        }

        /// <summary>
        /// Saves the current scanner focus state for later restoration.
        /// Used when temporarily switching to a different category (e.g., viewing obstacles).
        /// </summary>
        public static void SaveFocus()
        {
            savedCategoryIndex = currentCategoryIndex;
            savedSubcategoryIndex = currentSubcategoryIndex;
            savedItemIndex = currentItemIndex;
            savedBulkIndex = currentBulkIndex;
        }

        /// <summary>
        /// Restores the previously saved scanner focus state.
        /// Call this after removing a temporary category to return to the previous position.
        /// </summary>
        public static void RestoreFocus()
        {
            if (savedCategoryIndex >= 0)
            {
                currentCategoryIndex = savedCategoryIndex;
                currentSubcategoryIndex = savedSubcategoryIndex;
                currentItemIndex = savedItemIndex;
                currentBulkIndex = savedBulkIndex;

                // Reset saved state
                savedCategoryIndex = -1;
                savedSubcategoryIndex = -1;
                savedItemIndex = -1;
                savedBulkIndex = -1;

                // Validate indices in case the category list changed
                ValidateIndices();
            }
        }

        /// <summary>
        /// Creates a temporary category with the given name and items, and selects it.
        /// Only one temporary category can exist at a time.
        /// The temporary category is a proper scanner category that:
        /// - Appears in the category cycle (Ctrl+PageUp/Down)
        /// - Is preserved across RefreshItems() calls
        /// - Is cleared when Invalidate() is called (e.g., map switch)
        /// </summary>
        /// <param name="name">The name for the temporary category</param>
        /// <param name="items">The items to include in the category</param>
        // Any temporary-category change (search activate, search clear) ends the session so
        // the next Page Down re-sorts in the new category context.
        public static void CreateTemporaryCategory(string name, List<ScannerItem> items)
        {
            // Remove any existing temporary category first
            RemoveTemporaryCategory();

            // Create the new temporary category with a single subcategory
            temporaryCategory = new ScannerCategory(name);
            var subcategory = new ScannerSubcategory($"{name}-All");
            subcategory.Items.AddRange(items);
            temporaryCategory.Subcategories.Add(subcategory);

            // Add to the categories list
            categories.Add(temporaryCategory);

            // Select the temporary category
            currentCategoryIndex = categories.Count - 1;
            currentSubcategoryIndex = 0;
            currentItemIndex = 0;
            currentBulkIndex = 0;

            // Search results are a different subcategory context — end any in-progress session.
            InvalidateNavigationSession();
        }

        /// <summary>
        /// Removes the temporary category if one exists.
        /// Call RestoreFocus() after this to return to the previous scanner position.
        /// </summary>
        public static void RemoveTemporaryCategory()
        {
            if (temporaryCategory != null)
            {
                categories.Remove(temporaryCategory);
                temporaryCategory = null;
                InvalidateNavigationSession();
            }
        }

        /// <summary>
        /// Computes the live distance from a cursor to a scanner item.
        /// For area-backed items (terrain regions, zones, rooms) this returns the distance
        /// to the NEAREST cell in the area — so the cursor being inside the area naturally
        /// returns 0 and the cursor being adjacent returns 1, instead of returning the
        /// (often misleading) distance to the area's geometric center.
        /// </summary>
        private static float ComputeLiveDistance(ScannerItem item, IntVec3 cursor)
        {
            if (item == null) return float.MaxValue;

            // Area-backed: terrain regions / mineable deposits with adjacency grouping.
            if (item.HasTerrainRegions && item.TerrainRegions.Count > 0)
            {
                float minDist = float.MaxValue;
                foreach (var region in item.TerrainRegions)
                {
                    if (region.AllPositions == null) continue;
                    foreach (var cell in region.AllPositions)
                    {
                        if (cell == cursor) return 0f;
                        float d = (cell - cursor).LengthHorizontal;
                        if (d < minDist) minDist = d;
                    }
                }
                return minDist == float.MaxValue ? (item.Position - cursor).LengthHorizontal : minDist;
            }

            // Zone: nearest cell in the zone's cell list.
            if (item.IsZone && item.Zone != null && item.Zone.cells != null && item.Zone.cells.Count > 0)
            {
                float minDist = float.MaxValue;
                foreach (var cell in item.Zone.cells)
                {
                    if (cell == cursor) return 0f;
                    float d = (cell - cursor).LengthHorizontal;
                    if (d < minDist) minDist = d;
                }
                return minDist;
            }

            // Room: nearest cell in the room's cells.
            if (item.IsRoom && item.Room != null)
            {
                float minDist = float.MaxValue;
                foreach (var cell in item.Room.Cells)
                {
                    if (cell == cursor) return 0f;
                    float d = (cell - cursor).LengthHorizontal;
                    if (d < minDist) minDist = d;
                }
                return minDist == float.MaxValue ? (item.Position - cursor).LengthHorizontal : minDist;
            }

            // Legacy bulk-terrain (list of positions, no region structure).
            if (item.IsTerrain && item.BulkTerrainPositions != null && item.BulkTerrainPositions.Count > 0)
            {
                float minDist = float.MaxValue;
                foreach (var cell in item.BulkTerrainPositions)
                {
                    if (cell == cursor) return 0f;
                    float d = (cell - cursor).LengthHorizontal;
                    if (d < minDist) minDist = d;
                }
                return minDist;
            }

            // Point-based items fall through to single-position distance.
            IntVec3 pos;
            if (item.Thing != null && item.Thing.Spawned && !item.Thing.Destroyed)
                pos = item.Thing.Position;
            else if (item.IsDesignation && item.Designation != null)
                pos = item.Designation.target.Cell;
            else
                pos = item.Position;

            return (pos - cursor).LengthHorizontal;
        }

        /// <summary>
        /// For a multi-region item, returns the index of the region whose nearest cell is
        /// closest to the cursor. Short-circuits to the containing region if the cursor is
        /// on one of its cells. Returns 0 if the item has no regions.
        /// </summary>
        private static int FindNearestRegionIndex(ScannerItem item, IntVec3 cursor)
        {
            if (!item.HasTerrainRegions || item.TerrainRegions.Count == 0)
                return 0;

            int bestIdx = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < item.TerrainRegions.Count; i++)
            {
                var region = item.TerrainRegions[i];
                if (region.AllPositions == null) continue;
                foreach (var cell in region.AllPositions)
                {
                    if (cell == cursor) return i;
                    float d = (cell - cursor).LengthHorizontal;
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestIdx = i;
                    }
                }
            }
            return bestIdx;
        }

        /// <summary>
        /// Builds the announcement string for a specific terrain region of an item.
        /// Used by both the primary announcement (with the nearest region) and the bulk
        /// navigation announcement (with the user-selected region) so the format stays
        /// consistent: "label: size, distance direction[, area N of M]".
        /// </summary>
        private static string BuildRegionAnnouncement(ScannerItem item, int regionIndex)
        {
            if (regionIndex < 0 || regionIndex >= item.TerrainRegions.Count)
                return item.Label;

            var region = item.TerrainRegions[regionIndex];
            var cursorPos = MapNavigationState.CurrentCursorPosition;

            // Find the nearest cell within THIS region (or 0 if cursor is on it).
            float distance = float.MaxValue;
            IntVec3 nearestCell = IntVec3.Invalid;
            if (region.AllPositions != null)
            {
                foreach (var cell in region.AllPositions)
                {
                    if (cell == cursorPos) { distance = 0f; nearestCell = cell; break; }
                    float d = (cell - cursorPos).LengthHorizontal;
                    if (d < distance) { distance = d; nearestCell = cell; }
                }
            }
            if (!nearestCell.IsValid)
            {
                nearestCell = region.CenterPosition;
                distance = (region.CenterPosition - cursorPos).LengthHorizontal;
            }

            var direction = GetDirectionFromCursor(nearestCell);

            // Build size description — include quantity for deep ore deposits.
            string sizeAndQuantity;
            if (region.TotalQuantity.HasValue && item.DeepOreDef != null)
            {
                sizeAndQuantity = $"{region.TileCount} tiles ({region.TotalQuantity.Value} {item.DeepOreDef.label})";
            }
            else
            {
                sizeAndQuantity = region.SizeDescription;
            }

            string announcement;
            if (direction != null)
            {
                announcement = $"{item.Label}: {sizeAndQuantity}, {distance:F1} tiles {direction}";
            }
            else
            {
                announcement = $"{item.Label}: {sizeAndQuantity}, here";
            }

            if (item.RegionCount > 1)
            {
                int regionPosition = regionIndex + 1;
                announcement += $", area {regionPosition} of {item.RegionCount}";
            }

            return announcement;
        }

        /// <summary>
        /// For an area-backed item, finds the cell in the item that is nearest to the cursor.
        /// Used for announcement direction/distance so the scanner says "rich soil, 2 tiles east"
        /// pointing at the nearest edge of the patch, not "12 tiles east" pointing at the center.
        /// Returns IntVec3.Invalid if the item is not area-backed or has no cells.
        /// </summary>
        private static IntVec3 FindNearestCell(ScannerItem item, IntVec3 cursor)
        {
            if (item == null) return IntVec3.Invalid;

            IntVec3 best = IntVec3.Invalid;
            float bestDist = float.MaxValue;

            void consider(IntVec3 cell)
            {
                float d = (cell - cursor).LengthHorizontal;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = cell;
                }
            }

            if (item.HasTerrainRegions)
            {
                foreach (var region in item.TerrainRegions)
                {
                    if (region.AllPositions == null) continue;
                    foreach (var cell in region.AllPositions)
                    {
                        if (cell == cursor) return cell;
                        consider(cell);
                    }
                }
                return best;
            }

            if (item.IsZone && item.Zone?.cells != null)
            {
                foreach (var cell in item.Zone.cells)
                {
                    if (cell == cursor) return cell;
                    consider(cell);
                }
                return best;
            }

            if (item.IsRoom && item.Room != null)
            {
                foreach (var cell in item.Room.Cells)
                {
                    if (cell == cursor) return cell;
                    consider(cell);
                }
                return best;
            }

            if (item.IsTerrain && item.BulkTerrainPositions != null)
            {
                foreach (var cell in item.BulkTerrainPositions)
                {
                    if (cell == cursor) return cell;
                    consider(cell);
                }
                return best;
            }

            return IntVec3.Invalid;
        }

        /// <summary>
        /// Re-sorts the current subcategory by live distance from the cursor IF the cursor has
        /// moved since the last sort. Does NOT modify currentItemIndex.
        ///
        /// Called by BeginNavigationSession at the start of a fresh Page Up/Down sequence and
        /// by category/subcategory switches that explicitly reset the index to 0 or Count-1.
        /// Within an active navigation session, no re-sort happens on each press — the frozen
        /// order is what makes sequential navigation stable and eliminates the ping-pong that
        /// occurred when distance was recomputed after every scanner-driven cursor jump.
        /// </summary>
        private static bool EnsureSortedForCurrentCursor()
        {
            if (!MapNavigationState.IsInitialized)
                return false;

            var cursor = MapNavigationState.CurrentCursorPosition;
            if (cursor == lastSortedCursorPosition)
                return false;

            var subcat = GetCurrentSubcategory();
            if (subcat == null || subcat.Items.Count == 0)
            {
                lastSortedCursorPosition = cursor;
                return false;
            }

            foreach (var item in subcat.Items)
                item.Distance = ComputeLiveDistance(item, cursor);

            // Stable sort (OrderBy) — so tied distances preserve input order.
            subcat.Items = subcat.Items.OrderBy(i => i.Distance).ToList();
            lastSortedCursorPosition = cursor;
            return true;
        }

        /// <summary>
        /// Updates only the Distance of the currently selected item (and bulk-group entries)
        /// without re-sorting. Used by bulk navigation where list order must not change.
        /// </summary>
        private static void UpdateCurrentItemDistance()
        {
            var cursor = MapNavigationState.CurrentCursorPosition;
            var item = GetCurrentItem();
            if (item == null) return;

            item.Distance = ComputeLiveDistance(item, cursor);
        }

        /// <summary>
        /// Invalidates the scanner cache, forcing a refresh on next access.
        /// Call this when switching maps or when map contents have significantly changed.
        /// Also clears any temporary category since it's no longer valid.
        /// </summary>
        public static void Invalidate()
        {
            categories.Clear();
            temporaryCategory = null;
            currentCategoryIndex = 0;
            currentSubcategoryIndex = 0;
            currentItemIndex = 0;
            currentBulkIndex = 0;
            lastSortedCursorPosition = IntVec3.Invalid;

            // Clear the collection cache
            cachedCategories = null;
            lastThingStateHash = 0;
            lastDesignationCount = 0;
            lastZoneCount = 0;
            lastZoneCellHash = 0;
            ScannerHelper.InvalidateCache();

            // End any in-progress navigation session.
            InvalidateNavigationSession();

            // Also clear saved focus since it's no longer valid
            savedCategoryIndex = -1;
            savedSubcategoryIndex = -1;
            savedItemIndex = -1;
            savedBulkIndex = -1;

            // Clear any active search
            ScannerSearchState.ClearSearchSilent();
        }

        /// <summary>
        /// Refreshes the scanner item list based on current cursor position.
        /// Called automatically by navigation methods.
        /// If there's an active search filter, updates the filter with fresh items.
        /// </summary>
        private static void RefreshItems()
        {
            if (!MapNavigationState.IsInitialized)
            {
                TolkHelper.Speak("Map navigation not initialized", SpeechPriority.High);
                return;
            }

            var map = Find.CurrentMap;
            if (map == null)
            {
                TolkHelper.Speak("No active map", SpeechPriority.High);
                return;
            }

            var cursorPos = MapNavigationState.CurrentCursorPosition;

            // Check if cached collection is still valid to avoid expensive re-collection
            int currentThingHash = map.listerThings.StateHashOfGroup(ThingRequestGroup.Everything);
            int currentDesignationCount = map.designationManager.AllDesignations.Count;
            int currentZoneCount = map.zoneManager.AllZones.Count;
            // Zone-count alone misses cell-level edits (dragging a stockpile to grow/shrink).
            // Mix a sum of per-zone cell counts into the hash so those edits invalidate the cache.
            int currentZoneCellHash = 0;
            foreach (var zone in map.zoneManager.AllZones)
            {
                int cellCount = zone?.cells?.Count ?? 0;
                currentZoneCellHash = unchecked(currentZoneCellHash * 31 + cellCount);
            }

            bool cacheValid = cachedCategories != null
                && cachedCategories.Count > 0
                && currentThingHash == lastThingStateHash
                && currentDesignationCount == lastDesignationCount
                && currentZoneCount == lastZoneCount
                && currentZoneCellHash == lastZoneCellHash;

            if (cacheValid)
            {
                categories = cachedCategories;

                // Re-add temporary category if it existed
                if (temporaryCategory != null)
                {
                    if (!categories.Contains(temporaryCategory))
                        categories.Add(temporaryCategory);
                }

                // On cache hit, the items may have been sorted at a previous cursor position.
                // Force EnsureSortedForCurrentCursor to re-sort on the next navigation.
                lastSortedCursorPosition = IntVec3.Invalid;

                ValidateIndices();
                return;
            }

            // Check if there's an active search filter that needs refreshing
            if (ScannerSearchState.HasActiveFilter)
            {
                // Remember if user was in the filter category before refresh
                bool wasInFilterCategory = IsInTemporaryCategory();
                int previousItemIndex = currentItemIndex;

                // Collect items once, then filter from the collected categories
                categories = ScannerHelper.CollectMapItems(map, cursorPos);
                cachedCategories = categories;
                lastThingStateHash = currentThingHash;
                lastDesignationCount = currentDesignationCount;
                lastZoneCount = currentZoneCount;
                lastZoneCellHash = currentZoneCellHash;
                lastSortedCursorPosition = cursorPos;
                // Map state changed (cache miss) — the previous navigation snapshot is no
                // longer valid. Pawn death / designation / zone edit all land here.
                InvalidateNavigationSession();

                // Get filtered items from already-collected categories
                var filteredItems = ScannerSearchState.RefreshMapFilter(categories);
                if (filteredItems != null)
                {
                    // Update the temporary category with fresh filtered items
                    if (filteredItems.Count > 0)
                    {
                        temporaryCategory = new ScannerCategory(ScannerSearchState.GetFilterCategoryName());
                        var subcategory = new ScannerSubcategory($"{ScannerSearchState.GetFilterCategoryName()}-All");
                        subcategory.Items.AddRange(filteredItems);
                        temporaryCategory.Subcategories.Add(subcategory);
                        categories.Add(temporaryCategory);

                        // If user was in filter category, keep them there
                        if (wasInFilterCategory)
                        {
                            currentCategoryIndex = categories.Count - 1; // Temp category is at end
                            currentSubcategoryIndex = 0;
                            // Try to preserve item index, clamping to valid range
                            currentItemIndex = Math.Min(previousItemIndex, filteredItems.Count - 1);
                            currentBulkIndex = 0;
                        }
                    }
                    else
                    {
                        // No matches - remove temporary category
                        temporaryCategory = null;

                        // If user was in filter category, restore their previous focus
                        if (wasInFilterCategory)
                        {
                            RestoreFocus();
                        }
                    }

                    if (categories.Count == 0)
                    {
                        TolkHelper.Speak("No items found on map", SpeechPriority.High);
                        return;
                    }

                    ValidateIndices();
                    return;
                }
            }

            // No active filter - standard refresh
            // Save temporary category before refresh (for non-filter temporary categories)
            var savedTemporaryCategory = temporaryCategory;

            // Collect items
            categories = ScannerHelper.CollectMapItems(map, cursorPos);
            cachedCategories = categories;
            lastThingStateHash = currentThingHash;
            lastDesignationCount = currentDesignationCount;
            lastZoneCount = currentZoneCount;
            lastZoneCellHash = currentZoneCellHash;
            // CollectMapItems sorted items by distance from cursorPos, so the current cursor
            // matches the sort order. Skip the next EnsureSortedForCurrentCursor re-sort.
            lastSortedCursorPosition = cursorPos;
            // Map state changed (cache miss) — old navigation snapshot is invalid.
            InvalidateNavigationSession();

            // Re-add temporary category if it existed
            if (savedTemporaryCategory != null)
            {
                temporaryCategory = savedTemporaryCategory;
                categories.Add(temporaryCategory);
            }

            if (categories.Count == 0)
            {
                TolkHelper.Speak("No items found on map", SpeechPriority.High);
                return;
            }

            // Validate and adjust indices if needed
            ValidateIndices();
        }

        public static void NextItem()
        {
            if (WorldNavigationState.IsActive) return;

            // Always refresh: catches pawn death, designations added, zone edits, etc.
            // RefreshItems is cheap on a cache hit and invalidates the nav session on a miss.
            bool wasEmpty = categories.Count == 0;
            RefreshItems();
            if (categories.Count == 0) return;
            if (wasEmpty) AnnounceCurrentCategory();

            var currentSubcat = GetCurrentSubcategory();
            if (currentSubcat == null || currentSubcat.Items.Count == 0) return;

            AdvanceInSession(forward: true, currentSubcat);
        }

        public static void PreviousItem()
        {
            if (WorldNavigationState.IsActive) return;

            bool wasEmpty = categories.Count == 0;
            RefreshItems();
            if (categories.Count == 0) return;
            if (wasEmpty) AnnounceCurrentCategory();

            var currentSubcat = GetCurrentSubcategory();
            if (currentSubcat == null || currentSubcat.Items.Count == 0) return;

            AdvanceInSession(forward: false, currentSubcat);
        }

        /// <summary>
        /// Shared navigation step for NextItem / PreviousItem. If no session is active,
        /// starts a fresh one (sorting by distance from the current cursor). If the cursor
        /// has drifted from the last scanner-driven position (e.g., some code path bypassed
        /// the property-setter hook), invalidates the session first as a safety net.
        /// Within an active session, advances the index through the frozen sort order.
        /// </summary>
        private static void AdvanceInSession(bool forward, ScannerSubcategory currentSubcat)
        {
            // Drift safety net: if the cursor is not where we last left it, invalidate.
            if (navSessionActive
                && lastScannerCursor.IsValid
                && MapNavigationState.CurrentCursorPosition != lastScannerCursor)
            {
                InvalidateNavigationSession();
            }

            if (!navSessionActive)
            {
                BeginNavigationSession();
            }

            if (forward)
            {
                currentItemIndex++;
                if (currentItemIndex >= currentSubcat.Items.Count)
                    currentItemIndex = 0; // Wrap to first item
            }
            else
            {
                currentItemIndex--;
                if (currentItemIndex < 0)
                    currentItemIndex = currentSubcat.Items.Count - 1; // Wrap to last item
            }
            currentBulkIndex = 0; // Reset bulk index when changing items

            if (autoJumpMode)
            {
                JumpToCurrent();
            }
            else
            {
                AnnounceCurrentItem();
            }

            // Record where the cursor ended up so the next press can detect drift.
            lastScannerCursor = MapNavigationState.CurrentCursorPosition;
        }

        public static void NextBulkItem()
        {
            if (WorldNavigationState.IsActive) return;

            // Initialize scanner if not already done
            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            var currentItem = GetCurrentItem();
            if (currentItem == null || !currentItem.IsBulkGroup) return;

            currentBulkIndex++;
            if (currentBulkIndex >= currentItem.BulkCount)
            {
                currentBulkIndex = 0; // Wrap to first bulk item
            }

            // Update distance for the current bulk entry (no re-sort — bulk order is stable).
            UpdateCurrentItemDistance();

            // Auto-jump if enabled
            if (autoJumpMode)
            {
                JumpToCurrent();
            }
            else
            {
                AnnounceCurrentBulkItem();
            }
        }

        public static void PreviousBulkItem()
        {
            if (WorldNavigationState.IsActive) return;

            // Initialize scanner if not already done
            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            var currentItem = GetCurrentItem();
            if (currentItem == null || !currentItem.IsBulkGroup) return;

            currentBulkIndex--;
            if (currentBulkIndex < 0)
            {
                currentBulkIndex = currentItem.BulkCount - 1; // Wrap to last bulk item
            }

            // Update distance for the current bulk entry (no re-sort — bulk order is stable).
            UpdateCurrentItemDistance();

            // Auto-jump if enabled
            if (autoJumpMode)
            {
                JumpToCurrent();
            }
            else
            {
                AnnounceCurrentBulkItem();
            }
        }

        public static void NextCategory()
        {
            if (WorldNavigationState.IsActive) return;

            RefreshItems();
            if (categories.Count == 0) return;

            // Changing category ends the current navigation session.
            InvalidateNavigationSession();

            currentCategoryIndex++;
            if (currentCategoryIndex >= categories.Count)
            {
                currentCategoryIndex = 0; // Wrap to first category
            }

            // Reset subcategory, item, and bulk indices — always land on first subcategory / first item.
            currentSubcategoryIndex = 0;
            currentItemIndex = 0;
            currentBulkIndex = 0;

            // Skip empty subcategories
            SkipEmptySubcategories(forward: true);

            // Ensure the newly-entered subcategory is sorted for the current cursor position.
            EnsureSortedForCurrentCursor();

            AnnounceCurrentCategory();
            AnnounceCurrentItem();
        }

        public static void PreviousCategory()
        {
            if (WorldNavigationState.IsActive) return;

            RefreshItems();
            if (categories.Count == 0) return;

            // Changing category ends the current navigation session.
            InvalidateNavigationSession();

            currentCategoryIndex--;
            if (currentCategoryIndex < 0)
            {
                currentCategoryIndex = categories.Count - 1; // Wrap to last category
            }

            // Reset subcategory, item, and bulk indices — always land on first subcategory / first item.
            currentSubcategoryIndex = 0;
            currentItemIndex = 0;
            currentBulkIndex = 0;

            // Skip empty subcategories
            SkipEmptySubcategories(forward: true);

            // Ensure the newly-entered subcategory is sorted for the current cursor position.
            EnsureSortedForCurrentCursor();

            AnnounceCurrentCategory();
            AnnounceCurrentItem();
        }

        public static void NextSubcategory()
        {
            if (WorldNavigationState.IsActive) return;

            RefreshItems();
            if (categories.Count == 0) return;

            var currentCategory = GetCurrentCategory();
            if (currentCategory == null) return;

            // Changing subcategory ends the current navigation session.
            InvalidateNavigationSession();

            int startIndex = currentSubcategoryIndex;
            do
            {
                currentSubcategoryIndex++;
                if (currentSubcategoryIndex >= currentCategory.Subcategories.Count)
                {
                    currentSubcategoryIndex = 0; // Wrap to first subcategory
                }

                // Break if we've cycled through all subcategories
                if (currentSubcategoryIndex == startIndex)
                    break;

            } while (GetCurrentSubcategory()?.IsEmpty ?? true);

            // Reset item and bulk indices
            currentItemIndex = 0;
            currentBulkIndex = 0;

            // Ensure the newly-entered subcategory is sorted for the current cursor.
            EnsureSortedForCurrentCursor();

            AnnounceCurrentSubcategory();
            AnnounceCurrentItem();
        }

        public static void PreviousSubcategory()
        {
            if (WorldNavigationState.IsActive) return;

            RefreshItems();
            if (categories.Count == 0) return;

            var currentCategory = GetCurrentCategory();
            if (currentCategory == null) return;

            // Changing subcategory ends the current navigation session.
            InvalidateNavigationSession();

            int startIndex = currentSubcategoryIndex;
            do
            {
                currentSubcategoryIndex--;
                if (currentSubcategoryIndex < 0)
                {
                    currentSubcategoryIndex = currentCategory.Subcategories.Count - 1; // Wrap to last subcategory
                }

                // Break if we've cycled through all subcategories
                if (currentSubcategoryIndex == startIndex)
                    break;

            } while (GetCurrentSubcategory()?.IsEmpty ?? true);

            // Reset item and bulk indices
            currentItemIndex = 0;
            currentBulkIndex = 0;

            // Ensure the newly-entered subcategory is sorted for the current cursor.
            EnsureSortedForCurrentCursor();

            AnnounceCurrentSubcategory();
            AnnounceCurrentItem();
        }

        public static void JumpToCurrent()
        {
            if (WorldNavigationState.IsActive) return;

            // Initialize scanner if not already done
            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            ScannerItem currentItem;
            bool removedStale = false;
            while (true)
            {
                currentItem = GetCurrentItem();
                if (currentItem == null)
                {
                    TolkHelper.Speak(removedStale ? "Item no longer exists" : "No item selected", SpeechPriority.High);
                    return;
                }

                currentItem.RefreshLabel();
                if (!currentItem.IsStale) break;

                RemoveCurrentStaleItem();
                removedStale = true;
            }

            if (removedStale)
            {
                TolkHelper.Speak("Item no longer exists", SpeechPriority.High);
            }

            IntVec3 targetPosition;

            if (currentItem.IsTerrain || currentItem.HasTerrainRegions)
            {
                // For terrain regions, jump to the region center
                if (currentItem.HasTerrainRegions && currentBulkIndex < currentItem.TerrainRegions.Count)
                {
                    targetPosition = currentItem.TerrainRegions[currentBulkIndex].CenterPosition;
                }
                // Legacy: bulk terrain positions
                else if (currentItem.BulkTerrainPositions != null && currentBulkIndex < currentItem.BulkTerrainPositions.Count)
                {
                    targetPosition = currentItem.BulkTerrainPositions[currentBulkIndex];
                }
                else
                {
                    targetPosition = currentItem.Position;
                }
            }
            else if (currentItem.IsDesignation)
            {
                // For designations, check if we're navigating bulk designations
                if (currentItem.BulkDesignations != null && currentBulkIndex < currentItem.BulkDesignations.Count)
                {
                    targetPosition = currentItem.BulkDesignations[currentBulkIndex].target.Cell;
                }
                else
                {
                    targetPosition = currentItem.Position;
                }
            }
            else if (currentItem.IsZone)
            {
                // For zones, use the calculated center position
                targetPosition = currentItem.Position;
            }
            else if (currentItem.IsRoom)
            {
                // For rooms, use the calculated center position
                targetPosition = currentItem.Position;
            }
            else if (currentItem.IsCapturedEntity)
            {
                // Held pawn is not Spawned and its Position is stale; jump to the
                // platform's position. Liveness is already validated by RefreshLabel above.
                targetPosition = currentItem.Position;
            }
            else
            {
                // Get the actual thing to jump to (considering bulk index)
                Thing targetThing = currentItem.Thing;
                if (currentItem.IsBulkGroup && currentItem.BulkThings != null && currentBulkIndex < currentItem.BulkThings.Count)
                {
                    targetThing = currentItem.BulkThings[currentBulkIndex];
                }
                if (targetThing == null || targetThing.Destroyed || !targetThing.Spawned)
                {
                    TolkHelper.Speak("Item no longer exists", SpeechPriority.High);
                    return;
                }
                targetPosition = targetThing.Position;
            }

            // Update map cursor position. Guard the write so NotifyCursorWritten does not
            // invalidate the in-progress navigation session — this is a scanner-driven move.
            scannerDrivenJumpInProgress = true;
            try
            {
                MapNavigationState.CurrentCursorPosition = targetPosition;
            }
            finally
            {
                scannerDrivenJumpInProgress = false;
            }

            // Jump camera to position
            Find.CameraDriver.JumpToCurrentMapLoc(targetPosition);

            // Announce the item being jumped to
            if (autoJumpMode)
            {
                // In auto-jump mode, announce item details
                if (currentItem.IsBulkGroup)
                {
                    AnnounceCurrentBulkItem();
                }
                else
                {
                    AnnounceCurrentItem();
                }
            }
            else
            {
                // Manual jump (Home key) - just announce the jump
                TolkHelper.Speak($"Jumped to {currentItem.Label}", SpeechPriority.Normal);
            }
        }

        /// <summary>
        /// Gets the position of the currently selected item in the scanner.
        /// Used by visual preview patches to highlight the current item.
        /// Returns IntVec3.Invalid if no item is selected.
        /// </summary>
        public static IntVec3 GetCurrentItemPosition()
        {
            var currentItem = GetCurrentItem();
            if (currentItem == null)
                return IntVec3.Invalid;

            return currentItem.Position;
        }

        /// <summary>
        /// Checks if the scanner is currently focused on a temporary category.
        /// </summary>
        public static bool IsInTemporaryCategory()
        {
            return temporaryCategory != null && GetCurrentCategory() == temporaryCategory;
        }

        public static void ReadDistanceAndDirection()
        {
            if (WorldNavigationState.IsActive) return;

            // Initialize scanner if not already done
            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            ScannerItem currentItem;
            bool removedStale = false;
            while (true)
            {
                currentItem = GetCurrentItem();
                if (currentItem == null)
                {
                    TolkHelper.Speak(removedStale ? "Item no longer exists" : "No item selected", SpeechPriority.High);
                    return;
                }

                currentItem.RefreshLabel();
                if (!currentItem.IsStale) break;

                RemoveCurrentStaleItem();
                removedStale = true;
            }

            if (removedStale)
            {
                TolkHelper.Speak("Item no longer exists", SpeechPriority.High);
            }

            // No re-sort here: ReadDistanceAndDirection only announces the current item's
            // live distance and direction. Sort order is maintained by the navigation session.
            var cursorPos = MapNavigationState.CurrentCursorPosition;
            IntVec3 targetPos;

            // Area-backed items: use the nearest cell in the area, not the center.
            // Makes "here" register when the cursor is inside the region/zone/room.
            if (currentItem.HasTerrainRegions || currentItem.IsZone || currentItem.IsRoom ||
                (currentItem.IsTerrain && currentItem.BulkTerrainPositions != null))
            {
                var nearestCell = FindNearestCell(currentItem, cursorPos);
                targetPos = nearestCell.IsValid ? nearestCell : currentItem.Position;
            }
            else
            {
                // Get the actual thing's LIVE position (considering bulk index)
                if (currentItem.IsBulkGroup && currentBulkIndex < currentItem.BulkCount)
                {
                    if (currentItem.BulkThings != null && currentBulkIndex < currentItem.BulkThings.Count)
                    {
                        Thing targetThing = currentItem.BulkThings[currentBulkIndex];
                        targetPos = targetThing.Position;
                    }
                    else
                    {
                        targetPos = currentItem.Thing?.Position ?? currentItem.Position;
                    }
                }
                else
                {
                    // Use live position from Thing if available (for moving targets like pawns)
                    targetPos = currentItem.Thing?.Position ?? currentItem.Position;
                }
            }

            var distance = (targetPos - cursorPos).LengthHorizontal;
            // Use our calculated targetPos for direction, not item.Position which may be stale
            var direction = GetDirectionFromCursor(targetPos);

            if (direction != null)
            {
                TolkHelper.Speak($"{distance:F1} tiles {direction}", SpeechPriority.Normal);
            }
            else
            {
                TolkHelper.Speak("here", SpeechPriority.Normal);
            }
        }

        private static ScannerCategory GetCurrentCategory()
        {
            if (currentCategoryIndex < 0 || currentCategoryIndex >= categories.Count)
                return null;

            return categories[currentCategoryIndex];
        }

        private static ScannerSubcategory GetCurrentSubcategory()
        {
            var category = GetCurrentCategory();
            if (category == null) return null;

            if (currentSubcategoryIndex < 0 || currentSubcategoryIndex >= category.Subcategories.Count)
                return null;

            return category.Subcategories[currentSubcategoryIndex];
        }

        private static ScannerItem GetCurrentItem()
        {
            var subcat = GetCurrentSubcategory();
            if (subcat == null) return null;

            if (currentItemIndex < 0 || currentItemIndex >= subcat.Items.Count)
                return null;

            return subcat.Items[currentItemIndex];
        }

        // Removes the current item from its subcategory (used when RefreshLabel marks it stale).
        // Clamps currentItemIndex to the remaining range and resets bulk navigation state.
        private static void RemoveCurrentStaleItem()
        {
            var subcat = GetCurrentSubcategory();
            if (subcat == null) return;
            if (currentItemIndex < 0 || currentItemIndex >= subcat.Items.Count) return;

            subcat.Items.RemoveAt(currentItemIndex);

            if (currentItemIndex >= subcat.Items.Count)
            {
                currentItemIndex = Math.Max(0, subcat.Items.Count - 1);
            }

            currentBulkIndex = 0;
        }

        private static void ValidateIndices()
        {
            // Ensure category index is valid
            if (currentCategoryIndex < 0 || currentCategoryIndex >= categories.Count)
            {
                currentCategoryIndex = 0;
            }

            // Ensure subcategory index is valid and not empty
            var category = GetCurrentCategory();
            if (category != null)
            {
                if (currentSubcategoryIndex < 0 || currentSubcategoryIndex >= category.Subcategories.Count)
                {
                    currentSubcategoryIndex = 0;
                }

                // Skip to first non-empty subcategory
                SkipEmptySubcategories(forward: true);
            }

            // Ensure item index is valid
            var subcat = GetCurrentSubcategory();
            if (subcat != null)
            {
                if (currentItemIndex < 0 || currentItemIndex >= subcat.Items.Count)
                {
                    currentItemIndex = 0;
                }
            }
        }

        private static void SkipEmptySubcategories(bool forward)
        {
            var category = GetCurrentCategory();
            if (category == null) return;

            int startIndex = currentSubcategoryIndex;
            int attempts = 0;
            int maxAttempts = category.Subcategories.Count;

            while ((GetCurrentSubcategory()?.IsEmpty ?? true) && attempts < maxAttempts)
            {
                if (forward)
                {
                    currentSubcategoryIndex++;
                    if (currentSubcategoryIndex >= category.Subcategories.Count)
                    {
                        currentSubcategoryIndex = 0;
                    }
                }
                else
                {
                    currentSubcategoryIndex--;
                    if (currentSubcategoryIndex < 0)
                    {
                        currentSubcategoryIndex = category.Subcategories.Count - 1;
                    }
                }

                attempts++;
            }

            // If all subcategories are empty, reset to start
            if (GetCurrentSubcategory()?.IsEmpty ?? true)
            {
                currentSubcategoryIndex = startIndex;
            }
        }

        /// <summary>
        /// Calculates the compass direction from the current cursor to a target position.
        /// Returns null if the cursor is at or very close to the target.
        /// Delegates to ScannerDirectionHelper.
        /// </summary>
        private static string GetDirectionFromCursor(IntVec3 targetPosition)
        {
            return ScannerDirectionHelper.GetCompassDirection(
                MapNavigationState.CurrentCursorPosition, targetPosition);
        }

        private static void AnnounceCurrentCategory()
        {
            var category = GetCurrentCategory();
            if (category == null) return;

            TolkHelper.Speak($"{category.Name} - {category.TotalItemCount} items", SpeechPriority.Normal);
        }

        private static void AnnounceCurrentSubcategory()
        {
            var subcat = GetCurrentSubcategory();
            if (subcat == null) return;

            TolkHelper.Speak($"{subcat.Name} - {subcat.Items.Count} items", SpeechPriority.Normal);
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
                        TolkHelper.Speak("Item no longer exists", SpeechPriority.High);
                    else
                        TolkHelper.Speak("No items in this category", SpeechPriority.Normal);
                    return;
                }

                item.RefreshLabel();
                if (!item.IsStale) break;

                RemoveCurrentStaleItem();
                removedStale = true;
            }

            if (removedStale)
            {
                TolkHelper.Speak("Item no longer exists", SpeechPriority.High);
            }

            // Special handling for terrain with regions.
            // The primary announcement describes the SPECIFIC region the cursor is on or
            // nearest to, not an aggregated view across all regions. This matches the bulk
            // announcement format ("area N of M") and avoids the misleading "290 tiles, here"
            // when only 100 of those tiles are the region the cursor is actually on.
            if (item.HasTerrainRegions)
            {
                // Pick the region the cursor is inside, or the one with the nearest cell.
                // Update currentBulkIndex so subsequent Alt+PgDn navigates from that region.
                currentBulkIndex = FindNearestRegionIndex(item, MapNavigationState.CurrentCursorPosition);
                TolkHelper.Speak(BuildRegionAnnouncement(item, currentBulkIndex), SpeechPriority.Normal);
                return;
            }

            // Get target position — for area-backed items (zones, rooms), use the nearest cell
            // in the area so "inside the zone" registers as "here" and "2 tiles from the edge"
            // registers as "2 tiles direction", not the distance to the geometric center.
            var currentCursorPos = MapNavigationState.CurrentCursorPosition;
            IntVec3 targetPos;
            if (item.IsZone || item.IsRoom)
            {
                var nearestCell = FindNearestCell(item, currentCursorPos);
                targetPos = nearestCell.IsValid ? nearestCell : item.Position;
            }
            else if (item.IsCapturedEntity)
            {
                // Held pawn is not Spawned and Thing.Position is stale; use the
                // platform position stored on the item.
                targetPos = item.Position;
            }
            else
            {
                targetPos = item.Thing != null && item.Thing.Spawned && !item.Thing.Destroyed
                    ? item.Thing.Position
                    : item.Position;
            }
            var freshDistance = (targetPos - currentCursorPos).LengthHorizontal;  // Calculate fresh distance
            var itemDirection = GetDirectionFromCursor(targetPos);

            // Build announcement with direction (use comma instead of hyphen to avoid "minus" reading)
            string basicAnnouncement;
            if (itemDirection != null)
            {
                basicAnnouncement = $"{item.Label}, {freshDistance:F1} tiles {itemDirection}";
            }
            else
            {
                basicAnnouncement = $"{item.Label}, here";
            }

            // Add location context, cover info, and activity for pawns (colonists, NPCs, animals)
            if (item.Thing is Pawn pawn)
            {
                string locationContext = TileInfoHelper.GetLocationContext(targetPos, Find.CurrentMap);
                if (!string.IsNullOrEmpty(locationContext))
                {
                    basicAnnouncement += $" {locationContext}";
                }

                // Add cover info for drafted/hostile pawns if enabled
                if (RimWorldAccessMod_Settings.Settings?.ShowCoverInfo ?? true)
                {
                    string coverInfo = CoverHelper.GetCoverInfo(pawn);
                    if (!string.IsNullOrEmpty(coverInfo))
                    {
                        basicAnnouncement += $", {coverInfo}";
                    }
                }

                // Add pawn activity if enabled in settings
                if (RimWorldAccessMod_Settings.Settings?.ShowPawnActivityOnMap ?? true)
                {
                    string activity = PawnHelper.GetPawnActivity(pawn);
                    if (!string.IsNullOrEmpty(activity))
                    {
                        basicAnnouncement += $", {activity}";
                    }
                }
            }

            // Add bulk count if this is a grouped item
            if (item.IsBulkGroup)
            {
                int position = currentBulkIndex + 1;
                basicAnnouncement += $", {position} of {item.BulkCount}";
            }

            TolkHelper.Speak(basicAnnouncement, SpeechPriority.Normal);
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
                        TolkHelper.Speak("Item no longer exists", SpeechPriority.High);
                    return;
                }

                item.RefreshLabel();
                if (!item.IsStale) break;

                RemoveCurrentStaleItem();
                removedStale = true;
            }

            if (removedStale)
            {
                TolkHelper.Speak("Item no longer exists", SpeechPriority.High);
            }

            if (currentBulkIndex < 0 || currentBulkIndex >= item.BulkCount)
                return;

            // For terrain regions (adjacency-grouped). Delegate to the shared region builder
            // so primary and bulk announcements stay formatted consistently.
            if (item.HasTerrainRegions)
            {
                if (currentBulkIndex >= item.TerrainRegions.Count)
                    return;
                TolkHelper.Speak(BuildRegionAnnouncement(item, currentBulkIndex), SpeechPriority.Normal);
                return;
            }

            // For legacy terrain bulk groups (non-adjacent grouping)
            if (item.IsTerrain && item.BulkTerrainPositions != null)
            {
                var terrainPosition = currentBulkIndex + 1;
                TolkHelper.Speak($"{item.Label} - {terrainPosition} of {item.BulkCount}", SpeechPriority.Normal);
                return;
            }

            // For designation bulk groups
            if (item.IsDesignation)
            {
                if (item.BulkDesignations == null || currentBulkIndex >= item.BulkDesignations.Count)
                    return;

                var targetDesignation = item.BulkDesignations[currentBulkIndex];
                var desTargetPos = targetDesignation.target.Cell;
                var desDistance = (desTargetPos - MapNavigationState.CurrentCursorPosition).LengthHorizontal;
                var desDirection = GetDirectionFromCursor(desTargetPos);
                var desPosition = currentBulkIndex + 1;

                // Build label from the specific designation target
                string designationLabel;
                if (targetDesignation.target.HasThing && targetDesignation.target.Thing != null)
                {
                    designationLabel = targetDesignation.target.Thing.LabelShort;
                }
                else
                {
                    // For cell-based designations, use the main item label
                    designationLabel = item.Label;
                }

                if (desDirection != null)
                {
                    TolkHelper.Speak($"{designationLabel}, {desDistance:F1} tiles {desDirection}, {desPosition} of {item.BulkCount}", SpeechPriority.Normal);
                }
                else
                {
                    TolkHelper.Speak($"{designationLabel}, here, {desPosition} of {item.BulkCount}", SpeechPriority.Normal);
                }
                return;
            }

            // For thing bulk groups, get label from the actual thing at this index
            if (item.BulkThings == null || currentBulkIndex >= item.BulkThings.Count)
                return;

            var targetThing = item.BulkThings[currentBulkIndex];
            if (targetThing == null)
                return;

            var thingTargetPos = targetThing.Position;
            var distance = (thingTargetPos - MapNavigationState.CurrentCursorPosition).LengthHorizontal;
            var thingDirection = GetDirectionFromCursor(thingTargetPos);
            var position = currentBulkIndex + 1;

            // Build label from this specific thing, not the group label
            string thingLabel = targetThing.LabelShort ?? targetThing.def?.label ?? item.Label;

            if (thingDirection != null)
            {
                TolkHelper.Speak($"{thingLabel}, {distance:F1} tiles {thingDirection}, {position} of {item.BulkCount}", SpeechPriority.Normal);
            }
            else
            {
                TolkHelper.Speak($"{thingLabel}, here, {position} of {item.BulkCount}", SpeechPriority.Normal);
            }
        }

        /// <summary>
        /// Jumps to the first item in the current subcategory.
        /// </summary>
        public static void JumpToFirstItem()
        {
            if (WorldNavigationState.IsActive) return;

            // Initialize scanner if not already done
            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            var currentSubcat = GetCurrentSubcategory();
            if (currentSubcat == null || currentSubcat.Items.Count == 0) return;

            // Home: start a fresh session re-anchored to the current cursor, then land on the
            // closest item. Subsequent Page Down walks forward from there in a stable order.
            InvalidateNavigationSession();
            BeginNavigationSession();
            currentItemIndex = 0;
            currentBulkIndex = 0;

            if (autoJumpMode)
            {
                JumpToCurrent();
            }
            else
            {
                AnnounceCurrentItem();
            }
            lastScannerCursor = MapNavigationState.CurrentCursorPosition;
        }

        /// <summary>
        /// Jumps to the last item in the current subcategory.
        /// </summary>
        public static void JumpToLastItem()
        {
            if (WorldNavigationState.IsActive) return;

            // Initialize scanner if not already done
            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            var currentSubcat = GetCurrentSubcategory();
            if (currentSubcat == null || currentSubcat.Items.Count == 0) return;

            // End: start a fresh session re-anchored to the current cursor, then land on the
            // farthest item. Subsequent Page Up walks backward from there in a stable order.
            InvalidateNavigationSession();
            BeginNavigationSession();
            currentItemIndex = currentSubcat.Items.Count - 1;
            currentBulkIndex = 0;

            if (autoJumpMode)
            {
                JumpToCurrent();
            }
            else
            {
                AnnounceCurrentItem();
            }
            lastScannerCursor = MapNavigationState.CurrentCursorPosition;
        }
    }
}
