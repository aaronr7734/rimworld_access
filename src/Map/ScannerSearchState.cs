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

        // Word separators for match type detection
        private static readonly char[] WordSeparators = { ' ', '-', '_', '(', ')', '[', ']', '/', '\\', '.', ',' };

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

            TolkHelper.Speak("Search", SpeechPriority.Normal);
        }

        /// <summary>
        /// Handles a character input (letter key typed).
        /// </summary>
        /// <param name="c">The character to add to search buffer</param>
        public static void HandleCharacter(char c)
        {
            searchBuffer += c;
            TolkHelper.Speak(c.ToString(), SpeechPriority.Low);
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
            TolkHelper.Speak($"Deleted {deleted}", SpeechPriority.Low);

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

                TolkHelper.Speak($"Scanner now filtering {activeFilterQuery}", SpeechPriority.Normal);
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
                        WorldScannerState.CreateTemporaryCategory($"Search: {activeFilterQuery}", matching);
                    }
                }
                else
                {
                    var map = Find.CurrentMap;
                    var cursor = MapNavigationState.CurrentCursorPosition;
                    var matching = RefreshMapFilter(map, cursor);
                    if (matching != null && matching.Count > 0)
                    {
                        ScannerState.CreateTemporaryCategory($"Search: {activeFilterQuery}", matching);
                    }
                }

                TolkHelper.Speak($"Search cancelled. Restored filter: {savedFilterQuery}", SpeechPriority.Normal);
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
                TolkHelper.Speak("Search cancelled", SpeechPriority.Normal);
            }

            // Clear saved state
            savedFilterQuery = "";
            savedFilterIsWorldMap = false;
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

            // Flatten all items from pre-collected categories
            var allItems = new List<ScannerItem>();
            foreach (var category in preCollectedCategories)
            {
                foreach (var subcat in category.Subcategories)
                {
                    allItems.AddRange(subcat.Items);
                }
            }

            // Filter and prioritize by match type
            var firstWordMatches = new List<ScannerItem>();
            var otherWordMatches = new List<ScannerItem>();
            var containsMatches = new List<ScannerItem>();

            foreach (var item in allItems)
            {
                var matchType = GetMatchType(activeFilterQuery, item.Label);
                switch (matchType)
                {
                    case MatchType.FirstWord:
                        firstWordMatches.Add(item);
                        break;
                    case MatchType.OtherWord:
                        otherWordMatches.Add(item);
                        break;
                    case MatchType.Contains:
                        containsMatches.Add(item);
                        break;
                }
            }

            // Sort by relevance first, then distance within each tier
            var matching = firstWordMatches.OrderBy(i => i.Distance)
                .Concat(otherWordMatches.OrderBy(i => i.Distance))
                .Concat(containsMatches.OrderBy(i => i.Distance))
                .ToList();

            return matching;
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

            // Collect all items from world
            var allItems = CollectAllWorldItemsFlat();

            // Filter and prioritize by match type
            var firstWordMatches = new List<WorldScannerItem>();
            var otherWordMatches = new List<WorldScannerItem>();
            var containsMatches = new List<WorldScannerItem>();

            foreach (var item in allItems)
            {
                var matchType = GetMatchType(activeFilterQuery, item.Label);
                switch (matchType)
                {
                    case MatchType.FirstWord:
                        firstWordMatches.Add(item);
                        break;
                    case MatchType.OtherWord:
                        otherWordMatches.Add(item);
                        break;
                    case MatchType.Contains:
                        containsMatches.Add(item);
                        break;
                }
            }

            // Sort by relevance first, then distance within each tier
            var matching = firstWordMatches.OrderBy(i => i.GetDistance(originTile, 0))
                .Concat(otherWordMatches.OrderBy(i => i.GetDistance(originTile, 0)))
                .Concat(containsMatches.OrderBy(i => i.GetDistance(originTile, 0)))
                .ToList();

            return matching;
        }

        /// <summary>
        /// Gets the name for the current filter category.
        /// </summary>
        public static string GetFilterCategoryName()
        {
            return $"Search: {activeFilterQuery}";
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
                TolkHelper.Speak("No map available", SpeechPriority.High);
                return;
            }

            var cursor = MapNavigationState.CurrentCursorPosition;

            // Collect all items from all categories
            var allItems = CollectAllMapItemsFlat(map, cursor);

            // Filter and prioritize by match type
            var firstWordMatches = new List<ScannerItem>();
            var otherWordMatches = new List<ScannerItem>();
            var containsMatches = new List<ScannerItem>();

            foreach (var item in allItems)
            {
                var matchType = GetMatchType(searchBuffer, item.Label);
                switch (matchType)
                {
                    case MatchType.FirstWord:
                        firstWordMatches.Add(item);
                        break;
                    case MatchType.OtherWord:
                        otherWordMatches.Add(item);
                        break;
                    case MatchType.Contains:
                        containsMatches.Add(item);
                        break;
                }
            }

            // Sort by relevance first, then distance within each tier
            var matching = firstWordMatches.OrderBy(i => i.Distance)
                .Concat(otherWordMatches.OrderBy(i => i.Distance))
                .Concat(containsMatches.OrderBy(i => i.Distance))
                .ToList();

            // Group identical items
            matching = GroupIdenticalItems(matching, cursor);

            if (matching.Count == 0)
            {
                // No matches - announce and clear buffer so user can type again
                TolkHelper.Speak($"No matches for '{searchBuffer}'", SpeechPriority.Normal);
                searchBuffer = "";
                return;
            }

            // Create temporary category with results
            // Note: Focus is saved in Activate() when search starts
            ScannerState.CreateTemporaryCategory($"Search: {searchBuffer}", matching);

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
                TolkHelper.Speak("World navigation not active", SpeechPriority.High);
                return;
            }

            var originTile = WorldNavigationState.CurrentSelectedTile;

            // Collect all items from all categories (world scanner collects on refresh)
            var allItems = CollectAllWorldItemsFlat();

            // Filter and prioritize by match type
            var firstWordMatches = new List<WorldScannerItem>();
            var otherWordMatches = new List<WorldScannerItem>();
            var containsMatches = new List<WorldScannerItem>();

            foreach (var item in allItems)
            {
                var matchType = GetMatchType(searchBuffer, item.Label);
                switch (matchType)
                {
                    case MatchType.FirstWord:
                        firstWordMatches.Add(item);
                        break;
                    case MatchType.OtherWord:
                        otherWordMatches.Add(item);
                        break;
                    case MatchType.Contains:
                        containsMatches.Add(item);
                        break;
                }
            }

            // Sort by relevance first, then distance within each tier
            var matching = firstWordMatches.OrderBy(i => i.GetDistance(originTile, 0))
                .Concat(otherWordMatches.OrderBy(i => i.GetDistance(originTile, 0)))
                .Concat(containsMatches.OrderBy(i => i.GetDistance(originTile, 0)))
                .ToList();

            if (matching.Count == 0)
            {
                // No matches - announce and clear buffer so user can type again
                TolkHelper.Speak($"No matches for '{searchBuffer}'", SpeechPriority.Normal);
                searchBuffer = "";
                return;
            }

            // Create temporary category with results
            // Note: Focus is saved in Activate() when search starts
            WorldScannerState.CreateTemporaryCategory($"Search: {searchBuffer}", matching);

            // Announce results
            AnnounceWorldSearchResults(matching);
        }

        /// <summary>
        /// Collects all scanner items from all categories flattened into a single list.
        /// </summary>
        private static List<ScannerItem> CollectAllMapItemsFlat(Map map, IntVec3 cursorPosition)
        {
            var categories = ScannerHelper.CollectMapItems(map, cursorPosition);
            var allItems = new List<ScannerItem>();

            foreach (var category in categories)
            {
                foreach (var subcat in category.Subcategories)
                {
                    allItems.AddRange(subcat.Items);
                }
            }

            return allItems;
        }

        /// <summary>
        /// Collects all world scanner items flattened into a single list.
        /// This triggers a fresh collection from world objects.
        /// </summary>
        private static List<WorldScannerItem> CollectAllWorldItemsFlat()
        {
            var allItems = new List<WorldScannerItem>();
            var originTile = WorldNavigationState.CurrentSelectedTile;

            // Collect settlements
            var settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                foreach (var settlement in settlements)
                {
                    if (settlement.Faction == null || !settlement.Tile.Valid)
                        continue;

                    allItems.Add(new WorldScannerItem(settlement));
                }
            }

            // Collect caravans
            var caravans = Find.WorldObjects?.Caravans?
                .Where(c => c.Faction == RimWorld.Faction.OfPlayer)
                .ToList();

            if (caravans != null)
            {
                foreach (var caravan in caravans)
                {
                    allItems.Add(new WorldScannerItem(caravan));
                }
            }

            // Collect other world objects (sites, etc.)
            var allObjects = Find.WorldObjects?.AllWorldObjects;
            if (allObjects != null)
            {
                foreach (var worldObj in allObjects)
                {
                    // Skip settlements and caravans (already added)
                    if (worldObj is RimWorld.Planet.Settlement || worldObj is RimWorld.Planet.Caravan)
                        continue;
                    if (!worldObj.Tile.Valid)
                        continue;

                    allItems.Add(new WorldScannerItem(worldObj));
                }
            }

            return allItems;
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
                TolkHelper.Speak("No results", SpeechPriority.Normal);
                return;
            }

            string firstResult = results[0].Label;
            int count = results.Count;

            if (count == 1)
            {
                TolkHelper.Speak($"{firstResult}, 1 result", SpeechPriority.Normal);
            }
            else
            {
                TolkHelper.Speak($"{firstResult}, {count} results", SpeechPriority.Normal);
            }
        }

        /// <summary>
        /// Announces search results for world scanner.
        /// </summary>
        private static void AnnounceWorldSearchResults(List<WorldScannerItem> results)
        {
            if (results.Count == 0)
            {
                TolkHelper.Speak("No results", SpeechPriority.Normal);
                return;
            }

            string firstResult = results[0].Label;
            int count = results.Count;

            if (count == 1)
            {
                TolkHelper.Speak($"{firstResult}, 1 result", SpeechPriority.Normal);
            }
            else
            {
                TolkHelper.Speak($"{firstResult}, {count} results", SpeechPriority.Normal);
            }
        }

        #region Match Type Detection

        private enum MatchType
        {
            None,
            FirstWord,      // Match at start of label/first word
            OtherWord,      // Match on other words in name
            Contains        // Match anywhere in label
        }

        /// <summary>
        /// Determines what type of match (if any) exists between search and label.
        /// Uses same prioritization as TypeaheadSearchHelper.
        /// </summary>
        private static MatchType GetMatchType(string search, string label)
        {
            if (string.IsNullOrEmpty(search) || string.IsNullOrEmpty(label))
                return MatchType.None;

            string searchLower = search.ToLowerInvariant();
            string labelLower = label.ToLowerInvariant().Trim();

            // Strip parenthetical content for name-based matching
            string nameOnly = StripParentheticalContent(labelLower);

            // Check if label/first word starts with search
            if (nameOnly.StartsWith(searchLower))
            {
                return MatchType.FirstWord;
            }

            // Check first word specifically (before any separator)
            string[] nameWords = nameOnly.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (nameWords.Length > 0 && nameWords[0].StartsWith(searchLower))
            {
                return MatchType.FirstWord;
            }

            // Check other words in the name
            for (int i = 1; i < nameWords.Length; i++)
            {
                if (nameWords[i].StartsWith(searchLower))
                {
                    return MatchType.OtherWord;
                }
            }

            // Check if search appears anywhere in label (contains match)
            if (labelLower.Contains(searchLower))
            {
                return MatchType.Contains;
            }

            return MatchType.None;
        }

        /// <summary>
        /// Strips content inside parentheses from a string.
        /// </summary>
        private static string StripParentheticalContent(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var result = new System.Text.StringBuilder();
            int depth = 0;

            foreach (char c in text)
            {
                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    if (depth > 0) depth--;
                }
                else if (depth == 0)
                {
                    result.Append(c);
                }
            }

            return result.ToString().Trim();
        }

        #endregion
    }
}
