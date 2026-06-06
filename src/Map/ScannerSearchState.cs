using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Core search state management for both map and world scanners.
    /// Allows typeahead search to filter scanner items by name.
    /// </summary>
    public static class ScannerSearchState
    {
        // State
        private static string searchBuffer = "";
        private static bool isOnWorldMap = false;
        private static bool isSearchModeActive = false;

        // Active filter (persists after search is confirmed with Enter)
        private static string activeFilterQuery = "";
        private static bool activeFilterIsWorldMap = false;

        // Previous filter state for restoration on cancel
        private static string savedFilterQuery = "";
        private static bool savedFilterIsWorldMap = false;


        /// <summary>
        /// Returns true if search input mode is active (user is typing).
        /// </summary>
        public static bool IsActive => isSearchModeActive;

        /// <summary>
        /// Returns true if there's an active filter (search was confirmed with Enter).
        /// </summary>
        public static bool HasActiveFilter => !string.IsNullOrEmpty(activeFilterQuery);

        /// <summary>
        /// Gets the current search string.
        /// </summary>
        public static string SearchBuffer => searchBuffer;

        /// <summary>
        /// Activates search mode (Z key pressed).
        /// Saves existing filter for restoration on cancel, then clears it.
        /// </summary>
        /// <param name="onWorldMap">True if on world map, false if on colony map</param>
        public static void Activate(bool onWorldMap)
        {
            // Save the previous filter state BEFORE clearing (for restoration on cancel)
            savedFilterQuery = activeFilterQuery;
            savedFilterIsWorldMap = activeFilterIsWorldMap;

            // Clear any existing filter - new search will replace it
            if (HasActiveFilter)
            {
                activeFilterQuery = "";
                if (activeFilterIsWorldMap)
                {
                    WorldScannerState.RemoveTemporaryCategory();
                }
                else
                {
                    ScannerState.RemoveTemporaryCategory();
                }
            }

            isOnWorldMap = onWorldMap;
            searchBuffer = "";
            isSearchModeActive = true;

            // Save focus before we start modifying scanner state
            // Only save focus if there was no previous filter (filter already has its own position)
            if (string.IsNullOrEmpty(savedFilterQuery))
            {
                if (onWorldMap)
                {
                    if (!WorldScannerState.IsInTemporaryCategory())
                    {
                        WorldScannerState.SaveFocus();
                    }
                }
                else
                {
                    if (!ScannerState.IsInTemporaryCategory())
                    {
                        ScannerState.SaveFocus();
                    }
                }
            }

            TolkHelper.Speak("RimWorldAccess.Map.Search.Activated".Loc(), SpeechPriority.Normal);
        }

        /// <summary>
        /// Handles a character input (letter key typed).
        /// </summary>
        /// <param name="c">The character to add to search buffer</param>
        public static void HandleCharacter(char c)
        {
            searchBuffer += c;
            TolkHelper.SpeakData(c.ToString(), SpeechPriority.Low);
            UpdateSearchResults();
        }

        /// <summary>
        /// Removes the last character from the search buffer.
        /// </summary>
        public static void HandleBackspace()
        {
            if (string.IsNullOrEmpty(searchBuffer))
                return;

            char deleted = searchBuffer[searchBuffer.Length - 1];
            searchBuffer = searchBuffer.Substring(0, searchBuffer.Length - 1);
            TolkHelper.Speak("RimWorldAccess.Map.Input.Deleted".Loc(deleted), SpeechPriority.Low);

            if (string.IsNullOrEmpty(searchBuffer))
            {
                // Buffer is empty, cancel search
                CancelSearch();
            }
            else
            {
                // Update results with shortened buffer
                UpdateSearchResults();
            }
        }

        /// <summary>
        /// Confirms the search and keeps the filter active (Enter key).
        /// Exits search input mode but preserves the search category.
        /// The filter will refresh automatically when the scanner refreshes.
        /// Clears any saved filter state - new filter permanently replaces old.
        /// </summary>
        public static void ConfirmSearch()
        {
            if (IsActive && !string.IsNullOrEmpty(searchBuffer))
            {
                // Store the filter for live refresh
                activeFilterQuery = searchBuffer;
                activeFilterIsWorldMap = isOnWorldMap;

                searchBuffer = "";
                isSearchModeActive = false;

                // Clear saved filter - new one replaces it permanently
                savedFilterQuery = "";
                savedFilterIsWorldMap = false;

                TolkHelper.Speak("RimWorldAccess.Map.Search.NowFiltering".Loc(activeFilterQuery), SpeechPriority.Normal);
            }
            else
            {
                // No search query, just cancel
                CancelSearch();
            }
        }

        /// <summary>
        /// Cancels the search and reverts to previous scanner state (Escape key).
        /// If there was a previous filter, restores it. Otherwise, restores focus to pre-search position.
        /// </summary>
        public static void CancelSearch()
        {
            searchBuffer = "";
            isSearchModeActive = false;

            // Remove current search temporary category
            if (isOnWorldMap)
            {
                WorldScannerState.RemoveTemporaryCategory();
            }
            else
            {
                ScannerState.RemoveTemporaryCategory();
            }

            // Restore previous filter if there was one
            if (!string.IsNullOrEmpty(savedFilterQuery))
            {
                activeFilterQuery = savedFilterQuery;
                activeFilterIsWorldMap = savedFilterIsWorldMap;

                // Recreate the previous filter's temporary category
                if (savedFilterIsWorldMap)
                {
                    var matching = RefreshWorldFilter();
                    if (matching != null && matching.Count > 0)
                    {
                        WorldScannerState.CreateTemporaryCategory("RimWorldAccess.Map.Search.CategoryName".Translate(activeFilterQuery), matching);
                    }
                }
                else
                {
                    var map = Find.CurrentMap;
                    var cursor = MapNavigationState.CurrentCursorPosition;
                    var matching = RefreshMapFilter(map, cursor);
                    if (matching != null && matching.Count > 0)
                    {
                        ScannerState.CreateTemporaryCategory("RimWorldAccess.Map.Search.CategoryName".Translate(activeFilterQuery), matching);
                    }
                }

                TolkHelper.Speak("RimWorldAccess.Map.Search.RestoredFilter".Loc(savedFilterQuery), SpeechPriority.Normal);
            }
            else
            {
                activeFilterQuery = "";
                // Restore saved focus (pre-search position)
                if (isOnWorldMap)
                {
                    WorldScannerState.RestoreFocus();
                }
                else
                {
                    ScannerState.RestoreFocus();
                }
                TolkHelper.Speak("RimWorldAccess.Map.Search.Cancelled".Loc(), SpeechPriority.Normal);
            }

            // Clear saved state
            savedFilterQuery = "";
            savedFilterIsWorldMap = false;
        }

        /// <summary>
        /// Clears the active search filter and removes the search temporary category.
        /// Called by Ctrl+Z when not actively searching.
        /// If the user is currently in the search category, restores focus to pre-search position.
        /// Does nothing if there is no active filter.
        /// </summary>
        public static void ClearActiveFilter()
        {
            if (!HasActiveFilter || IsActive)
                return;

            if (activeFilterIsWorldMap)
            {
                bool wasInFilter = WorldScannerState.IsInTemporaryCategory();
                WorldScannerState.RemoveTemporaryCategory();
                if (wasInFilter)
                    WorldScannerState.RestoreFocus();
            }
            else
            {
                bool wasInFilter = ScannerState.IsInTemporaryCategory();
                ScannerState.RemoveTemporaryCategory();
                if (wasInFilter)
                    ScannerState.RestoreFocus();
            }

            activeFilterQuery = "";
            activeFilterIsWorldMap = false;
            savedFilterQuery = "";
            savedFilterIsWorldMap = false;

            TolkHelper.Speak("RimWorldAccess.Map.Search.FilterCleared".Loc(), SpeechPriority.Normal);
        }

        /// <summary>
        /// Clears the search without announcement.
        /// Used when map is invalidated or switched.
        /// Also clears any active filter.
        /// </summary>
        public static void ClearSearchSilent()
        {
            searchBuffer = "";
            isSearchModeActive = false;
            activeFilterQuery = "";
            // Note: temporary category removal is handled by ScannerState.Invalidate()
        }

        /// <summary>
        /// Refreshes the active filter with fresh items from the map.
        /// Called by CancelSearch when restoring a previous filter.
        /// Returns the updated list of matching items, or null if no active filter.
        /// </summary>
        public static List<ScannerItem> RefreshMapFilter(Map map, IntVec3 cursorPosition)
        {
            if (string.IsNullOrEmpty(activeFilterQuery) || activeFilterIsWorldMap)
                return null;

            var categories = ScannerHelper.CollectMapItems(map, cursorPosition);
            return RefreshMapFilter(categories);
        }

        /// <summary>
        /// Refreshes the active filter from pre-collected categories.
        /// Called by ScannerState.RefreshItems() to avoid double-collecting.
        /// Returns the updated list of matching items, or null if no active filter.
        /// </summary>
        public static List<ScannerItem> RefreshMapFilter(List<ScannerCategory> preCollectedCategories)
        {
            if (string.IsNullOrEmpty(activeFilterQuery) || activeFilterIsWorldMap)
                return null;

            var allItems = FlattenFromAllCategory(preCollectedCategories)
                .Where(item => item.Label != ScannerHelper.UnexploredAreaLabel)
                .ToList();

            return ScannerSearchEngine.FilterAndRank(
                allItems, activeFilterQuery, item => item.Label, item => item.Distance);
        }

        /// <summary>
        /// Returns the items from the top-level "All" category's "All-All" subcategory.
        /// That subcategory is the authoritative, reference-deduped, grouped-once view of
        /// every item on the map. Falls back to an empty list if the "All" category wasn't
        /// produced (e.g., an empty map). See CollectAllMapItemsFlat for why we don't
        /// iterate every subcategory ourselves.
        /// </summary>
        private static List<ScannerItem> FlattenFromAllCategory(List<ScannerCategory> categories)
        {
            if (categories == null)
                return new List<ScannerItem>();

            foreach (var category in categories)
            {
                if (category.Name == "All")
                    return category.AllSubcategory?.Items.ToList() ?? new List<ScannerItem>();
            }

            return new List<ScannerItem>();
        }

        /// <summary>
        /// Refreshes the active filter with fresh items from the world map.
        /// Called when restoring a previous filter on the world map.
        /// Returns the updated list of matching items, or null if no active filter.
        /// </summary>
        public static List<WorldScannerItem> RefreshWorldFilter()
        {
            if (string.IsNullOrEmpty(activeFilterQuery) || !activeFilterIsWorldMap)
                return null;

            var originTile = WorldNavigationState.CurrentSelectedTile;
            var allItems = CollectAllWorldItemsFlat();

            return ScannerSearchEngine.FilterAndRank(
                allItems, activeFilterQuery,
                item => item.Label,
                item => item.GetDistance(originTile, 0));
        }

        /// <summary>
        /// Gets the name for the current filter category.
        /// </summary>
        public static string GetFilterCategoryName()
        {
            return "RimWorldAccess.Map.Search.CategoryName".Translate(activeFilterQuery);
        }

        /// <summary>
        /// Updates search results based on current buffer.
        /// </summary>
        private static void UpdateSearchResults()
        {
            if (string.IsNullOrEmpty(searchBuffer))
                return;

            if (isOnWorldMap)
            {
                UpdateWorldSearchResults();
            }
            else
            {
                UpdateMapSearchResults();
            }
        }

        /// <summary>
        /// Updates search results for colony map scanner.
        /// </summary>
        private static void UpdateMapSearchResults()
        {
            var map = Find.CurrentMap;
            if (map == null || !MapNavigationState.IsInitialized)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Search.NoMap".Loc(), SpeechPriority.High);
                return;
            }

            var cursor = MapNavigationState.CurrentCursorPosition;

            // Collect all items from all categories. Fog-of-war regions all share the same
            // "unexplored area" label, so exclude them from search to avoid noise.
            var allItems = CollectAllMapItemsFlat(map, cursor)
                .Where(item => item.Label != ScannerHelper.UnexploredAreaLabel)
                .ToList();

            var matching = ScannerSearchEngine.FilterAndRank(
                allItems, searchBuffer, item => item.Label, item => item.Distance);

            // Group identical items
            matching = GroupIdenticalItems(matching, cursor);

            if (matching.Count == 0)
            {
                // No matches - announce and clear buffer so user can type again
                TolkHelper.Speak("RimWorldAccess.Search.NoMatches".Loc(searchBuffer), SpeechPriority.Normal);
                searchBuffer = "";
                return;
            }

            // Create temporary category with results
            // Note: Focus is saved in Activate() when search starts
            ScannerState.CreateTemporaryCategory("RimWorldAccess.Map.Search.CategoryName".Translate(searchBuffer), matching);

            // Announce results
            AnnounceSearchResults(matching);
        }

        /// <summary>
        /// Updates search results for world map scanner.
        /// </summary>
        private static void UpdateWorldSearchResults()
        {
            if (!WorldNavigationState.IsActive || !WorldNavigationState.IsInitialized)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Search.WorldNavInactive".Loc(), SpeechPriority.High);
                return;
            }

            var originTile = WorldNavigationState.CurrentSelectedTile;

            // Collect all items from all categories (world scanner collects on refresh)
            var allItems = CollectAllWorldItemsFlat();

            var matching = ScannerSearchEngine.FilterAndRank(
                allItems, searchBuffer,
                item => item.Label,
                item => item.GetDistance(originTile, 0));

            if (matching.Count == 0)
            {
                // No matches - announce and clear buffer so user can type again
                TolkHelper.Speak("RimWorldAccess.Search.NoMatches".Loc(searchBuffer), SpeechPriority.Normal);
                searchBuffer = "";
                return;
            }

            // Create temporary category with results
            // Note: Focus is saved in Activate() when search starts
            WorldScannerState.CreateTemporaryCategory("RimWorldAccess.Map.Search.CategoryName".Translate(searchBuffer), matching);

            // Announce results
            AnnounceWorldSearchResults(matching);
        }

        /// <summary>
        /// Collects all scanner items into a single flat list by reading from the top-level
        /// "All" category's "All-All" subcategory. That subcategory is built with reference
        /// dedup and then grouped exactly once, so every unique item/bulk appears here once.
        /// A naive flatten of every subcategory would over-count: the per-subcategory
        /// GroupIdenticalItems pass creates a distinct bulk ScannerItem per subcategory even
        /// when they all wrap the same underlying Things, which reference-dedup cannot catch.
        /// </summary>
        private static List<ScannerItem> CollectAllMapItemsFlat(Map map, IntVec3 cursorPosition)
        {
            var categories = ScannerHelper.CollectMapItems(map, cursorPosition);
            return FlattenFromAllCategory(categories);
        }

        /// <summary>
        /// Collects all world scanner items flattened into a single list.
        /// Delegates to WorldScannerState which builds all categories (settlements, biomes, roads, etc.)
        /// and returns them flattened, mirroring how CollectAllMapItemsFlat uses ScannerHelper.
        /// </summary>
        private static List<WorldScannerItem> CollectAllWorldItemsFlat()
        {
            return WorldScannerState.CollectAllItemsFlat();
        }

        /// <summary>
        /// Groups identical map items together.
        /// Reuses the logic from ScannerHelper.
        /// </summary>
        private static List<ScannerItem> GroupIdenticalItems(List<ScannerItem> items, IntVec3 cursorPosition)
        {
            // Note: Items coming from search are already individual items,
            // but we need to group them by type for efficient navigation
            var grouped = new List<ScannerItem>();
            var processedLabels = new HashSet<string>();

            foreach (var item in items)
            {
                // For simplicity, keep items as-is since they're already sorted by relevance
                // Grouping by type would lose the relevance ordering
                grouped.Add(item);
            }

            return grouped;
        }

        /// <summary>
        /// Announces search results for map scanner.
        /// </summary>
        private static void AnnounceSearchResults(List<ScannerItem> results)
        {
            if (results.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Search.NoResults".Loc(), SpeechPriority.Normal);
                return;
            }

            string firstResult = results[0].Label;
            int count = results.Count;

            if (count == 1)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Search.Result.One".Loc(firstResult), SpeechPriority.Normal);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Map.Search.Result.Many".Loc(firstResult, count), SpeechPriority.Normal);
            }
        }

        /// <summary>
        /// Announces search results for world scanner.
        /// </summary>
        private static void AnnounceWorldSearchResults(List<WorldScannerItem> results)
        {
            if (results.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Search.NoResults".Loc(), SpeechPriority.Normal);
                return;
            }

            string firstResult = results[0].Label;
            int count = results.Count;

            if (count == 1)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Search.Result.One".Loc(firstResult), SpeechPriority.Normal);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Map.Search.Result.Many".Loc(firstResult, count), SpeechPriority.Normal);
            }
        }

    }
}
