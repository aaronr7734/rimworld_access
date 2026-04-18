using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages a windowless fishing zone configuration menu.
    /// Provides keyboard navigation through all fishing zone settings.
    /// Uses reflection to access Zone_Fishing from Odyssey DLC.
    /// </summary>
    public static class FishingZoneMenuState
    {
        private static Zone fishingZone = null;
        private static List<MenuItem> menuItems = null;
        private static int selectedIndex = 0;
        private static bool isActive = false;

        // Numeric input mode fields
        private static string numericBuffer = "";
        private static bool isNumericInputMode = false;

        // Fish species expansion
        private static bool fishSpeciesExpanded = false;
        private static List<ThingDef> fishSpeciesList = null;

        // Cached reflection info for Zone_Fishing
        private static Type zoneFishingType = null;
        private static PropertyInfo allowedProp = null;
        private static FieldInfo allowedField = null; // Private field for setting
        private static FieldInfo repeatModeField = null;
        private static FieldInfo repeatCountField = null;
        private static FieldInfo targetCountField = null;
        private static PropertyInfo ownedFishCountProp = null;
        private static FieldInfo pauseWhenSatisfiedField = null;
        private static FieldInfo unpauseAtCountField = null;
        private static FieldInfo targetPopulationPctField = null;
        private static PropertyInfo fishTypeProp = null;

        // Cached reflection for FishRepeatMode enum
        private static Type fishRepeatModeType = null;
        private static object doForeverValue = null;
        private static object repeatCountValue = null;
        private static object targetCountValue = null;

        private enum MenuItemType
        {
            ZoneStatus,
            AllowToggle,
            RepeatMode,
            RepeatCount,
            TargetCount,
            CurrentlyHave,
            PauseWhenSatisfied,
            UnpauseAt,
            MinimumPopulation,
            FishSpecies,
            FishSpeciesItem
        }

        private class MenuItem
        {
            public MenuItemType type;
            public string label;
            public string searchLabel;
            public object data;
            public bool isEditable;
            public int indentLevel;
            public string tooltip;

            public MenuItem(MenuItemType type, string label, string searchLabel = null, object data = null, bool editable = false, int indent = 0, string tooltip = null)
            {
                this.type = type;
                this.label = label;
                this.searchLabel = searchLabel ?? label;
                this.data = data;
                this.isEditable = editable;
                this.indentLevel = indent;
                this.tooltip = tooltip;
            }
        }

        private const string MenuKey = "FishingZoneMenu";

        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        public static bool IsActive => isActive;
        public static bool HasActiveSearch => typeahead.HasActiveSearch;
        public static bool HasNoMatches => typeahead.HasNoMatches;
        public static bool IsNumericInputMode => isNumericInputMode;

        /// <summary>
        /// Initializes reflection info for Zone_Fishing.
        /// </summary>
        private static bool InitializeReflection()
        {
            if (zoneFishingType != null)
                return true;

            // Find Zone_Fishing type
            zoneFishingType = AccessTools.TypeByName("RimWorld.Zone_Fishing");
            if (zoneFishingType == null)
            {
                Log.Warning("[RimWorld Access] Could not find Zone_Fishing type - Odyssey DLC may not be installed");
                return false;
            }

            // Get Zone_Fishing properties/fields
            allowedProp = zoneFishingType.GetProperty("Allowed"); // Public property for reading
            allowedField = zoneFishingType.GetField("allowed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); // Private field for setting
            repeatModeField = zoneFishingType.GetField("repeatMode");
            repeatCountField = zoneFishingType.GetField("repeatCount");
            targetCountField = zoneFishingType.GetField("targetCount");
            ownedFishCountProp = zoneFishingType.GetProperty("OwnedFishCount");
            pauseWhenSatisfiedField = zoneFishingType.GetField("pauseWhenSatisfied");
            unpauseAtCountField = zoneFishingType.GetField("unpauseAtCount");
            targetPopulationPctField = zoneFishingType.GetField("targetPopulationPct");
            fishTypeProp = zoneFishingType.GetProperty("FishType");

            // Find FishRepeatMode enum
            fishRepeatModeType = AccessTools.TypeByName("RimWorld.FishRepeatMode");
            if (fishRepeatModeType != null)
            {
                doForeverValue = Enum.Parse(fishRepeatModeType, "DoForever");
                repeatCountValue = Enum.Parse(fishRepeatModeType, "RepeatCount");
                targetCountValue = Enum.Parse(fishRepeatModeType, "TargetCount");
            }

            // WaterBody is accessed directly via map.waterBodyTracker (no reflection needed)
            // See TileInfoHelper.GetFlooringInfo() for the working pattern

            return true;
        }

        /// <summary>
        /// Opens the fishing zone configuration menu.
        /// </summary>
        public static void Open(Zone zone)
        {
            if (zone == null || zone.GetType().Name != "Zone_Fishing")
            {
                Log.Error("[RimWorld Access] Cannot open fishing zone menu: invalid zone");
                return;
            }

            if (!InitializeReflection())
            {
                TolkHelper.Speak("Fishing zone settings not available - Odyssey DLC may not be installed");
                return;
            }

            fishingZone = zone;
            menuItems = new List<MenuItem>();
            selectedIndex = 0;
            isActive = true;
            fishSpeciesExpanded = false;
            fishSpeciesList = null;
            typeahead.ClearSearch();
            MenuHelper.ResetLevel(MenuKey);

            BuildMenuItems();
            AnnounceCurrentSelection();

            Log.Message($"[RimWorld Access] Opened fishing zone menu for {zone.label}");
        }

        /// <summary>
        /// Closes the fishing zone configuration menu.
        /// </summary>
        public static void Close()
        {
            fishingZone = null;
            menuItems = null;
            selectedIndex = 0;
            isActive = false;
            isNumericInputMode = false;
            numericBuffer = "";
            fishSpeciesExpanded = false;
            fishSpeciesList = null;
            typeahead.ClearSearch();
            MenuHelper.ResetLevel(MenuKey);
        }

        private static void BuildMenuItems()
        {
            menuItems.Clear();

            // Zone status (read-only) - tooltip from "FishPopulationDesc"
            string fishPopTooltip = "FishPopulationDesc".Translate();
            menuItems.Add(new MenuItem(MenuItemType.ZoneStatus, GetZoneStatusLabel(), "Zone status", null, false, indent: 0, tooltip: fishPopTooltip));

            // Allow/Forbid toggle
            menuItems.Add(new MenuItem(MenuItemType.AllowToggle, GetAllowLabel(), "Allow fishing", null, true));

            // Repeat mode
            menuItems.Add(new MenuItem(MenuItemType.RepeatMode, GetRepeatModeLabel(), "Repeat mode", null, true));

            // Conditional items based on repeat mode
            object repeatMode = repeatModeField?.GetValue(fishingZone);

            if (repeatMode != null && repeatMode.Equals(repeatCountValue))
            {
                // Repeat count (only if mode is RepeatCount)
                menuItems.Add(new MenuItem(MenuItemType.RepeatCount, GetRepeatCountLabel(), "Repeat count", null, true));
            }

            if (repeatMode != null && repeatMode.Equals(targetCountValue))
            {
                // Target count
                menuItems.Add(new MenuItem(MenuItemType.TargetCount, GetTargetCountLabel(), "Target count", null, true));

                // Currently have (read-only)
                menuItems.Add(new MenuItem(MenuItemType.CurrentlyHave, GetCurrentlyHaveLabel(), "Currently have", null, false));

                // Pause when satisfied
                menuItems.Add(new MenuItem(MenuItemType.PauseWhenSatisfied, GetPauseWhenSatisfiedLabel(), "Pause when satisfied", null, true));

                // Unpause at (only if pause is enabled)
                bool pauseWhenSatisfied = pauseWhenSatisfiedField != null && (bool)pauseWhenSatisfiedField.GetValue(fishingZone);
                if (pauseWhenSatisfied)
                {
                    menuItems.Add(new MenuItem(MenuItemType.UnpauseAt, GetUnpauseAtLabel(), "Unpause at", null, true));
                }
            }

            // Minimum population slider - tooltip from "MinimumPopulationDesc"
            string minPopTooltip = "MinimumPopulationDesc".Translate();
            menuItems.Add(new MenuItem(MenuItemType.MinimumPopulation, GetMinPopulationLabel(), "Minimum population", null, true, indent: 0, tooltip: minPopTooltip));

            // Fish species (expandable) - indent level 0
            menuItems.Add(new MenuItem(MenuItemType.FishSpecies, GetFishSpeciesLabel(), "Fish species", null, true, indent: 0));

            // Add fish species items if expanded - indent level 1
            if (fishSpeciesExpanded && fishSpeciesList != null)
            {
                foreach (var fish in fishSpeciesList)
                {
                    string uncommonMarker = IsUncommonFish(fish) ? " (uncommon)" : "";
                    menuItems.Add(new MenuItem(MenuItemType.FishSpeciesItem, $"{fish.LabelCap}{uncommonMarker}", fish.LabelCap, fish, false, indent: 1));
                }
            }
        }

        #region Label Generators

        private static string GetZoneStatusLabel()
        {
            try
            {
                var waterBody = GetWaterBody();
                if (waterBody == null)
                    return "Zone info: No water body found";

                int population = Mathf.RoundToInt(waterBody.Population);
                int maxPop = Mathf.RoundToInt(waterBody.MaxPopulation);

                object fishTypeValue = fishTypeProp?.GetValue(fishingZone);
                string fishType = fishTypeValue?.ToString() ?? "Unknown";

                // Determine population status
                string status;
                float ratio = maxPop > 0 ? (float)population / maxPop : 0;
                if (ratio < 0.25f) status = "very low";
                else if (ratio < 0.5f) status = "low";
                else if (ratio < 0.75f) status = "moderate";
                else status = "healthy";

                return $"Zone info: {fishType}, {population}/{maxPop} fish ({status})";
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Error getting zone status: {ex.Message}");
                return "Zone info: Unable to read";
            }
        }

        private static string GetAllowLabel()
        {
            bool allowed = allowedProp != null && (bool)allowedProp.GetValue(fishingZone);
            return $"Allow fishing: {(allowed ? "Yes" : "No")}";
        }

        private static string GetRepeatModeLabel()
        {
            object repeatMode = repeatModeField?.GetValue(fishingZone);
            string modeName = repeatMode?.ToString() ?? "Unknown";

            // Make mode name more readable
            if (modeName == "DoForever") modeName = "Do forever";
            else if (modeName == "RepeatCount") modeName = "Repeat count";
            else if (modeName == "TargetCount") modeName = "Target count";

            return $"Repeat mode: {modeName}";
        }

        private static string GetRepeatCountLabel()
        {
            int count = repeatCountField != null ? (int)repeatCountField.GetValue(fishingZone) : 0;
            return $"Repeat count: {count}";
        }

        private static string GetTargetCountLabel()
        {
            int count = targetCountField != null ? (int)targetCountField.GetValue(fishingZone) : 0;
            return $"Target count: {count}";
        }

        private static string GetCurrentlyHaveLabel()
        {
            int count = ownedFishCountProp != null ? (int)ownedFishCountProp.GetValue(fishingZone) : 0;
            return $"Currently have: {count}";
        }

        private static string GetPauseWhenSatisfiedLabel()
        {
            bool pause = pauseWhenSatisfiedField != null && (bool)pauseWhenSatisfiedField.GetValue(fishingZone);
            return $"Pause when satisfied: {(pause ? "Yes" : "No")}";
        }

        private static string GetUnpauseAtLabel()
        {
            int count = unpauseAtCountField != null ? (int)unpauseAtCountField.GetValue(fishingZone) : 0;
            return $"Unpause at: {count}";
        }

        private static string GetMinPopulationLabel()
        {
            float pct = targetPopulationPctField != null ? (float)targetPopulationPctField.GetValue(fishingZone) : 0.5f;
            int pctInt = Mathf.RoundToInt(pct * 100f);

            // Get actual fish count at this threshold
            var waterBody = GetWaterBody();
            int maxPop = waterBody != null ? Mathf.RoundToInt(waterBody.MaxPopulation) : 0;
            int fishAtThreshold = Mathf.RoundToInt(pct * maxPop);

            return $"Minimum population: {pctInt}% ({fishAtThreshold} fish)";
        }

        private static string GetFishSpeciesLabel()
        {
            int count = GetFishSpeciesCount();
            string expandState = fishSpeciesExpanded ? "expanded" : "collapsed";
            return $"Fish species ({count}), {expandState}";
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Gets the WaterBody for the fishing zone using direct API (like TileInfoHelper does).
        /// </summary>
        private static WaterBody GetWaterBody()
        {
            try
            {
                if (fishingZone == null || fishingZone.Cells == null || !fishingZone.Cells.Any())
                    return null;

                var map = fishingZone.Map;
                if (map == null || map.waterBodyTracker == null)
                    return null;

                // Use direct API call like TileInfoHelper.GetFlooringInfo() does
                var firstCell = fishingZone.Cells.First();
                if (map.waterBodyTracker.TryGetWaterBodyAt(firstCell, out var waterBody))
                {
                    return waterBody;
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Error getting water body: {ex.Message}");
                return null;
            }
        }

        private static int GetFishSpeciesCount()
        {
            var waterBody = GetWaterBody();
            if (waterBody == null) return 0;

            int count = 0;
            var commonFish = waterBody.CommonFishIncludingExtras;
            if (commonFish != null) count += commonFish.Count();

            var uncommonFish = waterBody.UncommonFish;
            if (uncommonFish != null) count += uncommonFish.Count();

            return count;
        }

        private static void LoadFishSpeciesList()
        {
            fishSpeciesList = new List<ThingDef>();
            var waterBody = GetWaterBody();
            if (waterBody == null) return;

            var commonFish = waterBody.CommonFishIncludingExtras;
            if (commonFish != null) fishSpeciesList.AddRange(commonFish);

            var uncommonFish = waterBody.UncommonFish;
            if (uncommonFish != null) fishSpeciesList.AddRange(uncommonFish);
        }

        private static bool IsUncommonFish(ThingDef fish)
        {
            var waterBody = GetWaterBody();
            if (waterBody == null) return false;

            var uncommonFish = waterBody.UncommonFish;
            return uncommonFish != null && uncommonFish.Contains(fish);
        }

        #endregion

        #region Navigation

        public static void SelectNext()
        {
            if (menuItems == null || menuItems.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, menuItems.Count);
            AnnounceCurrentSelection();
        }

        public static void SelectPrevious()
        {
            if (menuItems == null || menuItems.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, menuItems.Count);
            AnnounceCurrentSelection();
        }

        public static void SetSelectedIndex(int index)
        {
            if (menuItems == null || menuItems.Count == 0)
                return;

            if (index >= 0 && index < menuItems.Count)
            {
                selectedIndex = index;
            }
        }

        private static List<string> GetSearchLabels()
        {
            List<string> labels = new List<string>();
            if (menuItems != null)
            {
                foreach (var item in menuItems)
                {
                    labels.Add(item.searchLabel ?? "");
                }
            }
            return labels;
        }

        public static bool ProcessTypeaheadCharacter(char c)
        {
            if (menuItems == null || menuItems.Count == 0)
                return false;

            var labels = GetSearchLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
                return true;
            }
            return false;
        }

        public static bool ProcessBackspace()
        {
            if (!typeahead.HasActiveSearch)
                return false;

            var labels = GetSearchLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                }
                AnnounceWithSearch();
                return true;
            }
            return false;
        }

        public static bool ClearTypeaheadSearch()
        {
            return typeahead.ClearSearchAndAnnounce();
        }

        public static int SelectNextMatch()
        {
            return typeahead.GetNextMatch(selectedIndex);
        }

        public static int SelectPreviousMatch()
        {
            return typeahead.GetPreviousMatch(selectedIndex);
        }

        public static string GetLastFailedSearch()
        {
            return typeahead.LastFailedSearch;
        }

        /// <summary>
        /// Handles typeahead character input from the layout-aware dispatcher.
        /// Wraps <see cref="ProcessTypeaheadCharacter"/> with the no-match announcement.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive) return;
            if (!ProcessTypeaheadCharacter(c))
            {
                TolkHelper.Speak($"No matches for '{GetLastFailedSearch()}'");
            }
        }

        public static void AnnounceWithSearch()
        {
            if (menuItems == null || menuItems.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];
            string announcement = item.label;

            if (typeahead.HasActiveSearch)
            {
                announcement += $", match {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} for '{typeahead.SearchBuffer}'";
            }
            else
            {
                announcement += $". {MenuHelper.FormatPosition(selectedIndex, menuItems.Count)}";
            }

            TolkHelper.Speak(announcement);
        }

        #endregion

        #region Value Adjustment

        public static void AdjustValue(int direction, int multiplier = 1)
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            if (!item.isEditable)
            {
                TolkHelper.Speak("This item cannot be adjusted", SpeechPriority.High);
                return;
            }

            switch (item.type)
            {
                case MenuItemType.RepeatMode:
                    CycleRepeatMode(direction);
                    break;

                case MenuItemType.RepeatCount:
                    AdjustRepeatCount(direction, multiplier);
                    break;

                case MenuItemType.TargetCount:
                    AdjustTargetCount(direction, multiplier);
                    break;

                case MenuItemType.UnpauseAt:
                    AdjustUnpauseAt(direction, multiplier);
                    break;

                case MenuItemType.MinimumPopulation:
                    AdjustMinPopulation(direction, multiplier);
                    break;

                case MenuItemType.FishSpecies:
                    // Right to expand, Left to collapse (treeview pattern)
                    if (direction > 0 && !fishSpeciesExpanded)
                    {
                        ExpandFishSpecies();
                    }
                    else if (direction < 0 && fishSpeciesExpanded)
                    {
                        CollapseFishSpecies();
                    }
                    else if (direction > 0 && fishSpeciesExpanded)
                    {
                        TolkHelper.Speak("Already expanded");
                    }
                    else if (direction < 0 && !fishSpeciesExpanded)
                    {
                        TolkHelper.Speak("Already collapsed");
                    }
                    break;

                case MenuItemType.FishSpeciesItem:
                    // Left on a fish species item collapses the parent
                    if (direction < 0)
                    {
                        CollapseFishSpecies();
                    }
                    else
                    {
                        TolkHelper.Speak("Press Alt+I to view fish info");
                    }
                    break;

                default:
                    TolkHelper.Speak("Use Enter to activate");
                    break;
            }
        }

        private static void ExpandFishSpecies()
        {
            fishSpeciesExpanded = true;
            LoadFishSpeciesList();
            BuildMenuItems();
            TolkHelper.Speak($"Expanded, {fishSpeciesList?.Count ?? 0} fish species");
            AnnounceCurrentSelection();
        }

        private static void CollapseFishSpecies()
        {
            fishSpeciesExpanded = false;
            // Find the fish species header item and move selection there
            int fishSpeciesIndex = menuItems.FindIndex(m => m.type == MenuItemType.FishSpecies);
            BuildMenuItems();
            if (fishSpeciesIndex >= 0 && fishSpeciesIndex < menuItems.Count)
            {
                selectedIndex = fishSpeciesIndex;
            }
            TolkHelper.Speak("Collapsed");
            AnnounceCurrentSelection();
        }

        private static void CycleRepeatMode(int direction)
        {
            if (repeatModeField == null || fishRepeatModeType == null)
                return;

            object currentMode = repeatModeField.GetValue(fishingZone);
            object newMode;

            // Cycle through: DoForever -> RepeatCount -> TargetCount -> DoForever
            if (direction > 0)
            {
                if (currentMode.Equals(doForeverValue))
                    newMode = repeatCountValue;
                else if (currentMode.Equals(repeatCountValue))
                    newMode = targetCountValue;
                else
                    newMode = doForeverValue;
            }
            else
            {
                if (currentMode.Equals(doForeverValue))
                    newMode = targetCountValue;
                else if (currentMode.Equals(targetCountValue))
                    newMode = repeatCountValue;
                else
                    newMode = doForeverValue;
            }

            repeatModeField.SetValue(fishingZone, newMode);
            BuildMenuItems(); // Rebuild to show/hide related options
            AnnounceCurrentSelection();
        }

        private static void AdjustRepeatCount(int direction, int multiplier = 1)
        {
            if (repeatCountField == null)
                return;

            int step = direction * multiplier;
            int oldValue = (int)repeatCountField.GetValue(fishingZone);
            int newValue = Mathf.Max(1, oldValue + step);

            if (newValue == oldValue)
            {
                TolkHelper.Speak(direction > 0 ? "Maximum" : "Minimum");
                return;
            }

            repeatCountField.SetValue(fishingZone, newValue);

            if (newValue == 1 && direction < 0)
            {
                TolkHelper.Speak("1, minimum");
            }
            else
            {
                TolkHelper.Speak(newValue.ToString());
            }

            menuItems[selectedIndex].label = GetRepeatCountLabel();
        }

        private static void AdjustTargetCount(int direction, int multiplier = 1)
        {
            if (targetCountField == null)
                return;

            int step = direction * multiplier;
            int oldValue = (int)targetCountField.GetValue(fishingZone);
            int newValue = Mathf.Max(1, oldValue + step);

            if (newValue == oldValue)
            {
                TolkHelper.Speak(direction > 0 ? "Maximum" : "Minimum");
                return;
            }

            targetCountField.SetValue(fishingZone, newValue);

            // Enforce unpauseAt constraint
            if (pauseWhenSatisfiedField != null && unpauseAtCountField != null)
            {
                bool pauseWhenSatisfied = (bool)pauseWhenSatisfiedField.GetValue(fishingZone);
                if (pauseWhenSatisfied)
                {
                    int unpauseAt = (int)unpauseAtCountField.GetValue(fishingZone);
                    if (unpauseAt >= newValue)
                    {
                        unpauseAtCountField.SetValue(fishingZone, newValue - 1);
                    }
                }
            }

            if (newValue == 1 && direction < 0)
            {
                TolkHelper.Speak("1, minimum");
            }
            else
            {
                TolkHelper.Speak(newValue.ToString());
            }

            menuItems[selectedIndex].label = GetTargetCountLabel();
        }

        private static void AdjustUnpauseAt(int direction, int multiplier = 1)
        {
            if (unpauseAtCountField == null || targetCountField == null)
                return;

            int step = direction * multiplier;
            int oldValue = (int)unpauseAtCountField.GetValue(fishingZone);
            int targetCount = (int)targetCountField.GetValue(fishingZone);
            int maxValue = targetCount - 1;
            int newValue = Mathf.Clamp(oldValue + step, 0, maxValue);

            if (newValue == oldValue)
            {
                TolkHelper.Speak(direction > 0 ? "Maximum" : "Minimum");
                return;
            }

            unpauseAtCountField.SetValue(fishingZone, newValue);

            if (newValue == 0 && direction < 0)
            {
                TolkHelper.Speak("0, minimum");
            }
            else if (newValue == maxValue && direction > 0)
            {
                TolkHelper.Speak($"{newValue}, maximum");
            }
            else
            {
                TolkHelper.Speak(newValue.ToString());
            }

            menuItems[selectedIndex].label = GetUnpauseAtLabel();
        }

        private static void AdjustMinPopulation(int direction, int multiplier = 1)
        {
            if (targetPopulationPctField == null)
                return;

            // Step by 5% (0.05) per unit, 10% for shift
            float stepSize = multiplier >= 10 ? 0.10f : 0.05f;
            float step = direction * stepSize;
            float oldValue = (float)targetPopulationPctField.GetValue(fishingZone);

            // Calculate minimum based on 10 fish
            var waterBody = GetWaterBody();
            int maxPop = waterBody != null ? Mathf.RoundToInt(waterBody.MaxPopulation) : 100;
            float minPct = maxPop > 0 ? 10f / maxPop : 0.1f;
            minPct = Mathf.Max(0.05f, minPct); // At least 5%

            float newValue = Mathf.Clamp(oldValue + step, minPct, 1.0f);

            // Round to nearest 5%
            newValue = Mathf.Round(newValue * 20f) / 20f;
            newValue = Mathf.Clamp(newValue, minPct, 1.0f);

            if (Mathf.Approximately(newValue, oldValue))
            {
                TolkHelper.Speak(direction > 0 ? "Maximum" : "Minimum");
                return;
            }

            targetPopulationPctField.SetValue(fishingZone, newValue);

            int pctInt = Mathf.RoundToInt(newValue * 100f);
            int fishAtThreshold = Mathf.RoundToInt(newValue * maxPop);

            if (Mathf.Approximately(newValue, minPct) && direction < 0)
            {
                TolkHelper.Speak($"{pctInt}% ({fishAtThreshold} fish), minimum");
            }
            else if (Mathf.Approximately(newValue, 1.0f) && direction > 0)
            {
                TolkHelper.Speak($"{pctInt}% ({fishAtThreshold} fish), maximum");
            }
            else
            {
                TolkHelper.Speak($"{pctInt}% ({fishAtThreshold} fish)");
            }

            menuItems[selectedIndex].label = GetMinPopulationLabel();
        }

        #endregion

        #region Min/Max Jumps

        public static void JumpToMin()
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            switch (item.type)
            {
                case MenuItemType.RepeatCount:
                    if (repeatCountField != null)
                    {
                        int current = (int)repeatCountField.GetValue(fishingZone);
                        if (current == 1)
                        {
                            MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Minimum);
                            return;
                        }
                        repeatCountField.SetValue(fishingZone, 1);
                        TolkHelper.Speak("1, minimum");
                        menuItems[selectedIndex].label = GetRepeatCountLabel();
                    }
                    break;

                case MenuItemType.TargetCount:
                    if (targetCountField != null)
                    {
                        int current = (int)targetCountField.GetValue(fishingZone);
                        if (current == 1)
                        {
                            MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Minimum);
                            return;
                        }
                        targetCountField.SetValue(fishingZone, 1);
                        if (pauseWhenSatisfiedField != null && unpauseAtCountField != null)
                        {
                            bool pauseWhenSatisfied = (bool)pauseWhenSatisfiedField.GetValue(fishingZone);
                            if (pauseWhenSatisfied)
                            {
                                unpauseAtCountField.SetValue(fishingZone, 0);
                            }
                        }
                        TolkHelper.Speak("1, minimum");
                        menuItems[selectedIndex].label = GetTargetCountLabel();
                    }
                    break;

                case MenuItemType.UnpauseAt:
                    if (unpauseAtCountField != null)
                    {
                        int current = (int)unpauseAtCountField.GetValue(fishingZone);
                        if (current == 0)
                        {
                            MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Minimum);
                            return;
                        }
                        unpauseAtCountField.SetValue(fishingZone, 0);
                        TolkHelper.Speak("0, minimum");
                        menuItems[selectedIndex].label = GetUnpauseAtLabel();
                    }
                    break;

                case MenuItemType.MinimumPopulation:
                    if (targetPopulationPctField != null)
                    {
                        var waterBody = GetWaterBody();
                        int maxPop = waterBody != null ? Mathf.RoundToInt(waterBody.MaxPopulation) : 100;
                        float minPct = maxPop > 0 ? 10f / maxPop : 0.1f;
                        minPct = Mathf.Max(0.05f, minPct);

                        float current = (float)targetPopulationPctField.GetValue(fishingZone);
                        if (Mathf.Approximately(current, minPct))
                        {
                            MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Minimum);
                            return;
                        }
                        targetPopulationPctField.SetValue(fishingZone, minPct);
                        int pctInt = Mathf.RoundToInt(minPct * 100f);
                        int fishAtThreshold = Mathf.RoundToInt(minPct * maxPop);
                        TolkHelper.Speak($"{pctInt}% ({fishAtThreshold} fish), minimum");
                        menuItems[selectedIndex].label = GetMinPopulationLabel();
                    }
                    break;

                default:
                    TolkHelper.Speak("This field cannot be adjusted");
                    break;
            }
        }

        public static void JumpToMax()
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            switch (item.type)
            {
                case MenuItemType.RepeatCount:
                case MenuItemType.TargetCount:
                    TolkHelper.Speak("No maximum limit. Use numeric input for large values.");
                    break;

                case MenuItemType.UnpauseAt:
                    if (unpauseAtCountField != null && targetCountField != null)
                    {
                        int targetCount = (int)targetCountField.GetValue(fishingZone);
                        int maxValue = targetCount - 1;
                        int current = (int)unpauseAtCountField.GetValue(fishingZone);
                        if (current == maxValue)
                        {
                            MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Maximum);
                            return;
                        }
                        unpauseAtCountField.SetValue(fishingZone, maxValue);
                        TolkHelper.Speak($"{maxValue}, maximum");
                        menuItems[selectedIndex].label = GetUnpauseAtLabel();
                    }
                    break;

                case MenuItemType.MinimumPopulation:
                    if (targetPopulationPctField != null)
                    {
                        float current = (float)targetPopulationPctField.GetValue(fishingZone);
                        if (Mathf.Approximately(current, 1.0f))
                        {
                            MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Maximum);
                            return;
                        }
                        targetPopulationPctField.SetValue(fishingZone, 1.0f);
                        var waterBody = GetWaterBody();
                        int maxPop = waterBody != null ? Mathf.RoundToInt(waterBody.MaxPopulation) : 100;
                        TolkHelper.Speak($"100% ({maxPop} fish), maximum");
                        menuItems[selectedIndex].label = GetMinPopulationLabel();
                    }
                    break;

                default:
                    TolkHelper.Speak("This field cannot be adjusted");
                    break;
            }
        }

        #endregion

        #region Action Execution

        public static void ExecuteSelected()
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            switch (item.type)
            {
                case MenuItemType.AllowToggle:
                    ToggleAllow();
                    break;

                case MenuItemType.PauseWhenSatisfied:
                    TogglePauseWhenSatisfied();
                    break;

                case MenuItemType.FishSpecies:
                    // Treeview pattern: Use Right to expand, Left to collapse
                    TolkHelper.Speak("Use Right arrow to expand, Left to collapse");
                    break;

                case MenuItemType.FishSpeciesItem:
                    // Open info card on Enter for fish species item
                    OpenFishInfoCard();
                    break;

                default:
                    TolkHelper.Speak("Use left/right arrows to adjust");
                    break;
            }
        }

        private static void ToggleAllow()
        {
            if (allowedField == null)
                return;

            bool current = allowedProp != null ? (bool)allowedProp.GetValue(fishingZone) : (bool)allowedField.GetValue(fishingZone);
            allowedField.SetValue(fishingZone, !current);
            BuildMenuItems();
            TolkHelper.Speak(!current ? "Fishing allowed" : "Fishing forbidden");
            AnnounceCurrentSelection();
        }

        private static void TogglePauseWhenSatisfied()
        {
            if (pauseWhenSatisfiedField == null)
                return;

            bool current = (bool)pauseWhenSatisfiedField.GetValue(fishingZone);
            pauseWhenSatisfiedField.SetValue(fishingZone, !current);

            // Ensure unpause threshold is valid
            if (!current && unpauseAtCountField != null && targetCountField != null)
            {
                int unpauseAt = (int)unpauseAtCountField.GetValue(fishingZone);
                int targetCount = (int)targetCountField.GetValue(fishingZone);
                if (unpauseAt >= targetCount)
                {
                    unpauseAtCountField.SetValue(fishingZone, targetCount - 1);
                }
            }

            BuildMenuItems();
            TolkHelper.Speak(!current ? "Pause when satisfied enabled" : "Pause when satisfied disabled");
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Opens the info card for the selected fish species.
        /// </summary>
        public static void OpenFishInfoCard()
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];
            if (item.type != MenuItemType.FishSpeciesItem || item.data == null)
            {
                TolkHelper.Speak("Select a fish species first");
                return;
            }

            ThingDef fishDef = item.data as ThingDef;
            if (fishDef != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(fishDef));
                TolkHelper.Speak($"Opening info card for {fishDef.LabelCap}");
            }
        }

        #endregion

        #region Numeric Input

        public static void StartNumericInput()
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            // Only allow numeric input for numeric fields
            if (item.type != MenuItemType.RepeatCount &&
                item.type != MenuItemType.TargetCount &&
                item.type != MenuItemType.UnpauseAt)
            {
                // Not a numeric field - execute the action instead
                ExecuteSelected();
                return;
            }

            numericBuffer = "";
            isNumericInputMode = true;
            TolkHelper.Speak("Type a number, then press Enter to confirm or Escape to cancel");
        }

        public static void HandleNumericDigit(char digit)
        {
            if (!isNumericInputMode) return;

            numericBuffer += digit;
            TolkHelper.Speak(numericBuffer, SpeechPriority.Low);
        }

        public static void HandleNumericBackspace()
        {
            if (!isNumericInputMode || numericBuffer.Length == 0) return;

            numericBuffer = numericBuffer.Substring(0, numericBuffer.Length - 1);
            if (numericBuffer.Length > 0)
            {
                TolkHelper.Speak(numericBuffer, SpeechPriority.Low);
            }
            else
            {
                TolkHelper.Speak("Empty", SpeechPriority.Low);
            }
        }

        public static void ConfirmNumericInput()
        {
            if (!isNumericInputMode) return;

            if (int.TryParse(numericBuffer, out int value) && value > 0)
            {
                ApplyNumericValue(value);
            }
            else
            {
                TolkHelper.Speak("Invalid number");
            }

            isNumericInputMode = false;
            numericBuffer = "";
        }

        public static void CancelNumericInput()
        {
            isNumericInputMode = false;
            numericBuffer = "";
            TolkHelper.Speak("Cancelled");
        }

        private static void ApplyNumericValue(int value)
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            switch (item.type)
            {
                case MenuItemType.RepeatCount:
                    if (repeatCountField != null)
                    {
                        int newValue = Mathf.Max(1, value);
                        repeatCountField.SetValue(fishingZone, newValue);
                        menuItems[selectedIndex].label = GetRepeatCountLabel();
                        TolkHelper.Speak(newValue.ToString());
                    }
                    break;

                case MenuItemType.TargetCount:
                    if (targetCountField != null)
                    {
                        int newValue = Mathf.Max(1, value);
                        targetCountField.SetValue(fishingZone, newValue);
                        // Ensure unpause constraint
                        if (pauseWhenSatisfiedField != null && unpauseAtCountField != null)
                        {
                            bool pauseWhenSatisfied = (bool)pauseWhenSatisfiedField.GetValue(fishingZone);
                            if (pauseWhenSatisfied)
                            {
                                int unpauseAt = (int)unpauseAtCountField.GetValue(fishingZone);
                                if (unpauseAt >= newValue)
                                {
                                    unpauseAtCountField.SetValue(fishingZone, newValue - 1);
                                }
                            }
                        }
                        menuItems[selectedIndex].label = GetTargetCountLabel();
                        TolkHelper.Speak(newValue.ToString());
                    }
                    break;

                case MenuItemType.UnpauseAt:
                    if (unpauseAtCountField != null && targetCountField != null)
                    {
                        int targetCount = (int)targetCountField.GetValue(fishingZone);
                        int newValue = Mathf.Clamp(value, 0, targetCount - 1);
                        unpauseAtCountField.SetValue(fishingZone, newValue);
                        menuItems[selectedIndex].label = GetUnpauseAtLabel();
                        TolkHelper.Speak(menuItems[selectedIndex].label);
                    }
                    break;

                default:
                    TolkHelper.Speak("Cannot apply numeric value to this field");
                    break;
            }
        }

        #endregion

        private static void AnnounceCurrentSelection()
        {
            if (selectedIndex >= 0 && selectedIndex < menuItems.Count)
            {
                MenuItem item = menuItems[selectedIndex];
                string announcement = item.label;

                // Add tooltip if present (trim trailing period to avoid double periods)
                if (!string.IsNullOrEmpty(item.tooltip))
                {
                    string tooltip = item.tooltip.TrimEnd('.', ' ');
                    announcement += $". {tooltip}";
                }

                // Add expanded/collapsed state for fish species header
                if (item.type == MenuItemType.FishSpecies)
                {
                    // State is already in the label
                }

                // Get sibling position for proper treeview announcements
                var (sibPos, sibTotal) = MenuHelper.GetSiblingPosition(menuItems, selectedIndex, m => m.indentLevel);
                announcement += $". {MenuHelper.FormatPosition(sibPos - 1, sibTotal)}";

                // Add level suffix (only announced when level changes)
                announcement += MenuHelper.GetLevelSuffix(MenuKey, item.indentLevel);

                TolkHelper.Speak(announcement);
            }
        }

        /// <summary>
        /// Jumps to first item - Home key (first sibling) or Ctrl+Home (absolute first).
        /// </summary>
        public static void JumpToFirst(bool ctrlPressed = false)
        {
            if (menuItems == null || menuItems.Count == 0)
                return;

            MenuHelper.HandleTreeHomeKey(menuItems, ref selectedIndex, m => m.indentLevel, ctrlPressed, ClearAndAnnounce);
        }

        /// <summary>
        /// Jumps to last item - End key (last sibling/descendant) or Ctrl+End (absolute last).
        /// </summary>
        public static void JumpToLast(bool ctrlPressed = false)
        {
            if (menuItems == null || menuItems.Count == 0)
                return;

            MenuHelper.HandleTreeEndKey(
                menuItems,
                ref selectedIndex,
                m => m.indentLevel,
                m => m.type == MenuItemType.FishSpecies && fishSpeciesExpanded,
                m => HasVisibleChildren(m),
                ctrlPressed,
                ClearAndAnnounce);
        }

        /// <summary>
        /// Checks if a menu item has visible children (fish species items when expanded).
        /// </summary>
        private static bool HasVisibleChildren(MenuItem item)
        {
            if (item.type != MenuItemType.FishSpecies || !fishSpeciesExpanded)
                return false;

            int itemIndex = menuItems.IndexOf(item);
            if (itemIndex < 0 || itemIndex >= menuItems.Count - 1)
                return false;

            return menuItems[itemIndex + 1].indentLevel > item.indentLevel;
        }

        /// <summary>
        /// Clears typeahead and announces current selection.
        /// </summary>
        private static void ClearAndAnnounce()
        {
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
        }
    }
}
