using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Faction filter type for settlement browser.
    /// </summary>
    public enum FactionFilter
    {
        All,
        Player,
        Allied,
        Neutral,
        Hostile
    }

    /// <summary>
    /// State management for the settlement browser (S key in world view).
    /// Allows browsing settlements by faction and distance.
    /// </summary>
    public static class SettlementBrowserState
    {
        private static bool isActive = false;
        private static List<Settlement> filteredSettlements = new List<Settlement>();
        private static int currentIndex = 0;
        private static FactionFilter currentFilter = FactionFilter.All;
        private static PlanetTile originTile = PlanetTile.Invalid;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        /// <summary>
        /// Gets whether the settlement browser is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the settlement browser from the specified origin tile.
        /// </summary>
        public static void Open(PlanetTile origin)
        {
            if (Find.WorldObjects == null)
            {
                TolkHelper.Speak("World objects not available", SpeechPriority.High);
                return;
            }

            isActive = true;
            originTile = origin;
            currentIndex = 0;
            currentFilter = FactionFilter.All;
            typeahead.ClearSearch();

            RefreshSettlementList();

            if (filteredSettlements.Count == 0)
            {
                TolkHelper.Speak("No settlements found");
                return;
            }

            AnnounceCurrentSettlement();
        }

        /// <summary>
        /// Closes the settlement browser.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            filteredSettlements.Clear();
            currentIndex = 0;
            originTile = PlanetTile.Invalid;
            typeahead.ClearSearch();
            TolkHelper.Speak("Settlement browser closed");
        }

        /// <summary>
        /// Refreshes the settlement list based on current filter.
        /// </summary>
        private static void RefreshSettlementList()
        {
            if (Find.WorldObjects?.Settlements == null)
            {
                filteredSettlements.Clear();
                return;
            }

            List<Settlement> allSettlements = Find.WorldObjects.Settlements;

            // Filter by faction relationship
            IEnumerable<Settlement> filtered = allSettlements;

            switch (currentFilter)
            {
                case FactionFilter.Player:
                    filtered = allSettlements.Where(s => s.Faction == Faction.OfPlayer);
                    break;

                case FactionFilter.Allied:
                    filtered = allSettlements.Where(s =>
                        s.Faction != Faction.OfPlayer &&
                        !s.Faction.HostileTo(Faction.OfPlayer) &&
                        s.Faction.PlayerRelationKind == FactionRelationKind.Ally);
                    break;

                case FactionFilter.Neutral:
                    filtered = allSettlements.Where(s =>
                        s.Faction != Faction.OfPlayer &&
                        !s.Faction.HostileTo(Faction.OfPlayer) &&
                        s.Faction.PlayerRelationKind == FactionRelationKind.Neutral);
                    break;

                case FactionFilter.Hostile:
                    filtered = allSettlements.Where(s =>
                        s.Faction != Faction.OfPlayer &&
                        s.Faction.HostileTo(Faction.OfPlayer));
                    break;

                case FactionFilter.All:
                default:
                    // No filtering
                    break;
            }

            // Sort by distance from origin tile
            if (originTile.Valid && Find.WorldGrid != null)
            {
                filteredSettlements = filtered
                    .OrderBy(s => Find.WorldGrid.ApproxDistanceInTiles(originTile, s.Tile))
                    .ToList();
            }
            else
            {
                filteredSettlements = filtered.ToList();
            }

            // Validate current index
            if (currentIndex >= filteredSettlements.Count)
                currentIndex = 0;
        }

        /// <summary>
        /// Cycles to the next faction filter.
        /// </summary>
        public static void NextFilter()
        {
            currentFilter = (FactionFilter)(((int)currentFilter + 1) % 5);
            currentIndex = 0;
            typeahead.ClearSearch();
            RefreshSettlementList();
            AnnounceFilter();

            if (filteredSettlements.Count > 0)
            {
                AnnounceCurrentSettlement();
            }
            else
            {
                TolkHelper.Speak("No settlements match this filter");
            }
        }

        /// <summary>
        /// Cycles to the previous faction filter.
        /// </summary>
        public static void PreviousFilter()
        {
            currentFilter = (FactionFilter)(((int)currentFilter + 4) % 5);
            currentIndex = 0;
            typeahead.ClearSearch();
            RefreshSettlementList();
            AnnounceFilter();

            if (filteredSettlements.Count > 0)
            {
                AnnounceCurrentSettlement();
            }
            else
            {
                TolkHelper.Speak("No settlements match this filter");
            }
        }

        /// <summary>
        /// Selects the next settlement in the list.
        /// </summary>
        public static void SelectNext()
        {
            if (filteredSettlements.Count == 0)
            {
                TolkHelper.Speak("No settlements available");
                return;
            }

            // If typeahead is active with matches, navigate to next match
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int nextMatch = typeahead.GetNextMatch(currentIndex);
                if (nextMatch >= 0)
                {
                    currentIndex = nextMatch;
                    AnnounceWithSearch();
                }
                return;
            }

            // Navigate normally (either no search active, OR search with no matches)
            currentIndex = MenuHelper.SelectNext(currentIndex, filteredSettlements.Count);

            AnnounceCurrentSettlement();
        }

        /// <summary>
        /// Selects the previous settlement in the list.
        /// </summary>
        public static void SelectPrevious()
        {
            if (filteredSettlements.Count == 0)
            {
                TolkHelper.Speak("No settlements available");
                return;
            }

            // If typeahead is active with matches, navigate to previous match
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int prevMatch = typeahead.GetPreviousMatch(currentIndex);
                if (prevMatch >= 0)
                {
                    currentIndex = prevMatch;
                    AnnounceWithSearch();
                }
                return;
            }

            // Navigate normally (either no search active, OR search with no matches)
            currentIndex = MenuHelper.SelectPrevious(currentIndex, filteredSettlements.Count);

            AnnounceCurrentSettlement();
        }

        /// <summary>
        /// Jumps to the first settlement in the list.
        /// </summary>
        public static void JumpToFirst()
        {
            if (filteredSettlements.Count == 0)
            {
                TolkHelper.Speak("No settlements available");
                return;
            }

            currentIndex = MenuHelper.JumpToFirst();
            AnnounceCurrentSettlement();
        }

        /// <summary>
        /// Jumps to the last settlement in the list.
        /// </summary>
        public static void JumpToLast()
        {
            if (filteredSettlements.Count == 0)
            {
                TolkHelper.Speak("No settlements available");
                return;
            }

            currentIndex = MenuHelper.JumpToLast(filteredSettlements.Count);
            AnnounceCurrentSettlement();
        }

        /// <summary>
        /// Jumps the camera to the currently selected settlement and closes the browser.
        /// </summary>
        public static void JumpToSelected()
        {
            if (filteredSettlements.Count == 0 || currentIndex < 0 || currentIndex >= filteredSettlements.Count)
            {
                TolkHelper.Speak("No settlement selected");
                return;
            }

            Settlement selected = filteredSettlements[currentIndex];

            // Update world navigation state
            WorldNavigationState.CurrentSelectedTile = selected.Tile;

            // Sync with game's selection system
            if (Find.WorldSelector != null)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.Select(selected);
                Find.WorldSelector.SelectedTile = selected.Tile;
            }

            // Jump camera
            if (Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.JumpTo(selected.Tile);
            }

            // Close browser first
            Close();

            // If we're choosing a destination for caravan, set it directly
            if (CaravanFormationState.IsChoosingDestination)
            {
                CaravanFormationState.SetDestination(selected.Tile);
            }
            else
            {
                // Announce the tile info (which includes the settlement name)
                WorldNavigationState.AnnounceTile();
            }
        }

        /// <summary>
        /// Announces the current faction filter.
        /// </summary>
        private static void AnnounceFilter()
        {
            string filterName;
            switch (currentFilter)
            {
                case FactionFilter.Player:
                    filterName = "Player settlements";
                    break;
                case FactionFilter.Allied:
                    filterName = "Allied settlements";
                    break;
                case FactionFilter.Neutral:
                    filterName = "Neutral settlements";
                    break;
                case FactionFilter.Hostile:
                    filterName = "Hostile settlements";
                    break;
                case FactionFilter.All:
                    filterName = "All settlements";
                    break;
                default:
                    filterName = "Unknown filter";
                    break;
            }

            TolkHelper.Speak($"{filterName}, {filteredSettlements.Count} found");
        }

        /// <summary>
        /// Announces the currently selected settlement.
        /// </summary>
        private static void AnnounceCurrentSettlement()
        {
            if (filteredSettlements.Count == 0)
            {
                TolkHelper.Speak("No settlements available");
                return;
            }

            if (currentIndex < 0 || currentIndex >= filteredSettlements.Count)
                return;

            Settlement settlement = filteredSettlements[currentIndex];

            // Calculate distance from origin
            float distance = 0f;
            if (originTile.Valid && Find.WorldGrid != null)
            {
                distance = Find.WorldGrid.ApproxDistanceInTiles(originTile, settlement.Tile);
            }

            // Get faction relationship
            string relationship = "Unknown";
            if (settlement.Faction == Faction.OfPlayer)
            {
                relationship = "Player";
            }
            else if (settlement.Faction.HostileTo(Faction.OfPlayer))
            {
                relationship = "Hostile";
            }
            else
            {
                relationship = settlement.Faction.PlayerRelationKind.GetLabel();
            }

            // Build announcement
            string announcement = $"{settlement.Label}, {settlement.Faction.Name}, {relationship}, {distance:F1} tiles. {MenuHelper.FormatPosition(currentIndex, filteredSettlements.Count)}";

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Gets the list of settlement labels for typeahead search.
        /// </summary>
        private static List<string> GetSettlementLabels()
        {
            return filteredSettlements.Select(s => s.Label).ToList();
        }

        /// <summary>
        /// Announces the current settlement with search information.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (filteredSettlements.Count == 0)
            {
                TolkHelper.Speak("No settlements available");
                return;
            }

            if (currentIndex < 0 || currentIndex >= filteredSettlements.Count)
                return;

            Settlement settlement = filteredSettlements[currentIndex];

            // Calculate distance from origin
            float distance = 0f;
            if (originTile.Valid && Find.WorldGrid != null)
            {
                distance = Find.WorldGrid.ApproxDistanceInTiles(originTile, settlement.Tile);
            }

            // Get faction relationship
            string relationship = "Unknown";
            if (settlement.Faction == Faction.OfPlayer)
            {
                relationship = "Player";
            }
            else if (settlement.Faction.HostileTo(Faction.OfPlayer))
            {
                relationship = "Hostile";
            }
            else
            {
                relationship = settlement.Faction.PlayerRelationKind.GetLabel();
            }

            // Build announcement with search info
            string searchInfo = $"'{typeahead.SearchBuffer}' match {typeahead.CurrentMatchPosition} of {typeahead.MatchCount}";
            string announcement = $"{searchInfo}: {settlement.Label}, {settlement.Faction.Name}, {relationship}, {distance:F1} tiles";

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Handles typeahead character input for the settlement browser.
        /// Called from UnifiedKeyboardPatch to process alphanumeric characters.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive)
                return;

            var labels = GetSettlementLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    currentIndex = newIndex;
                    AnnounceWithSearch();
                }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
            }
        }

        /// <summary>
        /// Handles backspace key for typeahead search.
        /// Called from UnifiedKeyboardPatch.
        /// </summary>
        public static void HandleBackspace()
        {
            if (!isActive)
                return;

            if (!typeahead.HasActiveSearch)
                return;

            var labels = GetSettlementLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    currentIndex = newIndex;
                }
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Gets whether typeahead search is active.
        /// </summary>
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        /// <summary>
        /// Handles keyboard input for the settlement browser.
        /// Called from WorldNavigationPatch or UnifiedKeyboardPatch.
        /// </summary>
        public static bool HandleInput(KeyCode key)
        {
            if (!isActive)
                return false;

            bool shift = Input.GetKey(KeyCode.LeftShift) ||
                        Input.GetKey(KeyCode.RightShift);

            // Handle Home - jump to first
            if (key == KeyCode.Home)
            {
                JumpToFirst();
                Event.current.Use();
                return true;
            }

            // Handle End - jump to last
            if (key == KeyCode.End)
            {
                JumpToLast();
                Event.current.Use();
                return true;
            }

            // Handle Escape - clear search FIRST, then close
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentSettlement();
                    Event.current.Use();
                    return true;
                }
                Close();
                // If we're in destination selection mode, cancel it and return to caravan dialog
                if (CaravanFormationState.IsChoosingDestination)
                {
                    CaravanFormationState.CancelDestinationSelection();
                }
                return true;
            }

            // Handle Backspace for search
            if (key == KeyCode.Backspace && typeahead.HasActiveSearch)
            {
                var labels = GetSettlementLabels();
                if (typeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0) currentIndex = newIndex;
                    AnnounceWithSearch();
                }
                Event.current.Use();
                return true;
            }

            // Handle * key - consume to prevent passthrough
            // Use KeyCode instead of Event.current.character (which is empty in Unity IMGUI)
            bool isStar = key == KeyCode.KeypadMultiply || (Event.current.shift && key == KeyCode.Alpha8);
            if (isStar)
            {
                Event.current.Use();
                return true;
            }

            // Handle typeahead characters
            // Use KeyCode instead of Event.current.character (which is empty in Unity IMGUI)
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            if (isLetter || isNumber)
            {
                char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                var labels = GetSettlementLabels();
                if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
                {
                    if (newIndex >= 0)
                    {
                        currentIndex = newIndex;
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
                }
                Event.current.Use();
                return true;
            }

            switch (key)
            {
                case KeyCode.UpArrow:
                    if (shift)
                    {
                        // Shift+Up does nothing in settlement browser
                        return false;
                    }
                    SelectPrevious();
                    return true;

                case KeyCode.DownArrow:
                    if (shift)
                    {
                        // Shift+Down does nothing in settlement browser
                        return false;
                    }
                    SelectNext();
                    return true;

                case KeyCode.LeftArrow:
                    if (shift)
                    {
                        PreviousFilter();
                        return true;
                    }
                    return false;

                case KeyCode.RightArrow:
                    if (shift)
                    {
                        NextFilter();
                        return true;
                    }
                    return false;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    JumpToSelected();
                    return true;

                default:
                    return false;
            }
        }
    }
}
