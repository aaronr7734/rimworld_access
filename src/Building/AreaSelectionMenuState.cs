using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Accessible menu for selecting which area to expand/clear.
    /// Opens when an area designator is selected from the Architect menu.
    /// Replaces the vanilla FloatMenu that is inaccessible to screen readers.
    /// </summary>
    public static class AreaSelectionMenuState
    {
        private static bool isActive = false;
        private static Designator pendingDesignator = null;
        private static Action<Area> onAreaSelected = null;
        private static List<Area> availableAreas = new List<Area>();
        private static int selectedIndex = 0;
        private static bool includeManageOption = true;
        private static TypeaheadSearchHelper typeaheadHelper = null;

        private static int ManageAreasIndex => availableAreas.Count;
        private static int TotalOptions => availableAreas.Count + (includeManageOption ? 1 : 0);

        public static bool IsActive => isActive;

        public static void Open(Designator designator, Action<Area> callback)
        {
            if (designator == null || callback == null)
                return;

            Map map = Find.CurrentMap;
            if (map?.areaManager == null)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoMapLoaded".Loc());
                return;
            }

            availableAreas = map.areaManager.AllAreas
                .Where(a => a.Mutable)
                .ToList();

            if (availableAreas.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Building.AreaSelect.NoAreasAvailable".Loc());
            }

            isActive = true;
            pendingDesignator = designator;
            onAreaSelected = callback;
            selectedIndex = 0;

            // Initialize typeahead helper
            typeaheadHelper = new TypeaheadSearchHelper();

            string designatorLabel = designator.Label ?? (string)"RimWorldAccess.Building.AreaSelect.OpenFallbackName".Translate();
            TolkHelper.Speak("RimWorldAccess.Building.AreaSelect.OpenPrompt".Loc(designatorLabel));
            AnnounceCurrentSelection();
        }

        public static void Close()
        {
            isActive = false;
            pendingDesignator = null;
            onAreaSelected = null;
            availableAreas.Clear();
            selectedIndex = 0;
            typeaheadHelper = null;
        }

        public static void CloseWithoutDeselect()
        {
            isActive = false;
            pendingDesignator = null;
            onAreaSelected = null;
            availableAreas.Clear();
            selectedIndex = 0;
            typeaheadHelper = null;
        }

        public static void SelectNext()
        {
            if (TotalOptions == 0) return;
            selectedIndex = MenuHelper.SelectNext(selectedIndex, TotalOptions);
            AnnounceCurrentSelection();
        }

        public static void SelectPrevious()
        {
            if (TotalOptions == 0) return;
            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, TotalOptions);
            AnnounceCurrentSelection();
        }

        public static void Confirm()
        {
            if (selectedIndex == ManageAreasIndex && includeManageOption)
            {
                OpenManageAreas();
            }
            else if (selectedIndex >= 0 && selectedIndex < availableAreas.Count)
            {
                Area selected = availableAreas[selectedIndex];
                var callback = onAreaSelected;

                TolkHelper.Speak("RimWorldAccess.Building.AreaSelect.Selected".Loc(selected.Label));
                CloseWithoutDeselect();

                callback?.Invoke(selected);
            }
        }

        public static void Cancel()
        {
            TolkHelper.Speak("RimWorldAccess.Building.AreaSelect.Cancelled".Loc());
            Close();
            Find.DesignatorManager.Deselect();
        }

        private static void OpenManageAreas()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;

            Designator savedDesignator = pendingDesignator;
            Action<Area> savedCallback = onAreaSelected;

            Close();

            // Deselect the designator to prevent placement/shape modes from activating
            // while in Manage Areas. The callback will re-open the area selection menu
            // if the user returns from Manage Areas and re-selects the same designator.
            Find.DesignatorManager.Deselect();

            WindowlessAreaState.Open(map, () => {
                // The callback is invoked when Manage Areas closes.
                // Since we deselected the designator, this condition will be false
                // and the area selection menu will NOT automatically reopen.
                // This is the correct behavior - user explicitly went to Manage Areas.
                if (savedDesignator != null && Find.DesignatorManager.SelectedDesignator == savedDesignator)
                {
                    Open(savedDesignator, savedCallback);
                }
            });
        }

        /// <summary>
        /// Handles typeahead character input from the layout-aware dispatcher.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive) return;
            if (typeaheadHelper == null) return;

            var labels = GetAreaLabels();
            if (typeaheadHelper.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
            }
            else
            {
                typeaheadHelper.SpeakNoMatches();
            }
        }

        private static List<string> GetAreaLabels()
        {
            var labels = new List<string>();
            foreach (var area in availableAreas)
            {
                labels.Add(area.Label);
            }
            if (includeManageOption)
            {
                labels.Add("RimWorldAccess.Building.AreaSelect.ManageAreas".Translate());
            }
            return labels;
        }

        private static void AnnounceWithSearch()
        {
            if (selectedIndex == ManageAreasIndex && includeManageOption)
            {
                TolkHelper.Speak(typeaheadHelper.BuildItemAnnouncement("RimWorldAccess.Building.AreaSelect.ManageAreas".Translate()));
            }
            else if (selectedIndex >= 0 && selectedIndex < availableAreas.Count)
            {
                Area area = availableAreas[selectedIndex];
                TolkHelper.Speak(typeaheadHelper.BuildItemAnnouncement(area.Label));
            }
        }

        public static void JumpToFirst()
        {
            if (TotalOptions == 0) return;
            selectedIndex = MenuHelper.JumpToFirst();
            typeaheadHelper.ClearSearch();
            AnnounceCurrentSelection();
        }

        public static void JumpToLast()
        {
            if (TotalOptions == 0) return;
            selectedIndex = MenuHelper.JumpToLast(TotalOptions);
            typeaheadHelper.ClearSearch();
            AnnounceCurrentSelection();
        }

        private static void AnnounceCurrentSelection()
        {
            string position = MenuHelper.FormatPosition(selectedIndex, TotalOptions);

            if (selectedIndex == ManageAreasIndex && includeManageOption)
            {
                string label = "RimWorldAccess.Building.AreaSelect.ManageAreas".Translate();
                string announcement = string.IsNullOrEmpty(position)
                    ? label
                    : (string)"RimWorldAccess.Building.AreaSelect.ItemWithPosition".Translate(label, position);
                TolkHelper.Speak(announcement);
            }
            else if (selectedIndex >= 0 && selectedIndex < availableAreas.Count)
            {
                Area area = availableAreas[selectedIndex];
                int cellCount = area.TrueCount;
                string content = "RimWorldAccess.Building.AreaSelect.AreaWithCells".Translate(area.Label, cellCount);
                string announcement = string.IsNullOrEmpty(position)
                    ? content
                    : (string)"RimWorldAccess.Building.AreaSelect.ItemWithPosition".Translate(content, position);
                TolkHelper.Speak(announcement);
                area.MarkForDraw();
            }
        }

        public static bool HandleInput(KeyCode key)
        {
            if (!isActive) return false;

            // Home - jump to first
            if (key == KeyCode.Home)
            {
                JumpToFirst();
                return true;
            }

            // End - jump to last
            if (key == KeyCode.End)
            {
                JumpToLast();
                return true;
            }

            // Escape - clear search first, then cancel
            if (key == KeyCode.Escape)
            {
                if (typeaheadHelper.HasActiveSearch)
                {
                    typeaheadHelper.ClearSearchAndAnnounce();
                    AnnounceCurrentSelection();
                    return true;
                }
                Cancel();
                return true;
            }

            // Backspace for search
            if (key == KeyCode.Backspace && typeaheadHelper.HasActiveSearch)
            {
                var labels = GetAreaLabels();
                if (typeaheadHelper.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0)
                        selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
                return true;
            }

            // Up arrow - navigate with search awareness
            if (key == KeyCode.UpArrow)
            {
                if (typeaheadHelper.HasActiveSearch && !typeaheadHelper.HasNoMatches)
                {
                    int prevIndex = typeaheadHelper.GetPreviousMatch(selectedIndex);
                    if (prevIndex >= 0)
                    {
                        selectedIndex = prevIndex;
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    SelectPrevious();
                }
                return true;
            }

            // Down arrow - navigate with search awareness
            if (key == KeyCode.DownArrow)
            {
                if (typeaheadHelper.HasActiveSearch && !typeaheadHelper.HasNoMatches)
                {
                    int nextIndex = typeaheadHelper.GetNextMatch(selectedIndex);
                    if (nextIndex >= 0)
                    {
                        selectedIndex = nextIndex;
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    SelectNext();
                }
                return true;
            }

            // Enter - confirm selection
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                Confirm();
                return true;
            }

            // Typeahead characters (letters and numbers)
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            if (isLetter || isNumber)
            {
                return true;
            }

            // CRITICAL: Block ALL other input when menu is active
            return true;
        }
    }
}
