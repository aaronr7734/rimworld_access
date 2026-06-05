using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Accessible menu for assigning allowed areas to a selected pawn.
    /// Opened via Alt+A from the colony map when a pawn is selected.
    /// </summary>
    public static class PawnAreaMenuState
    {
        private static bool isActive = false;
        private static Pawn targetPawn = null;
        private static List<object> areaOptions = new List<object>();
        private static int selectedIndex = 0;
        private static TypeaheadSearchHelper typeaheadHelper = null;

        // Sentinel value for "Manage Areas" option (first in list)
        private static readonly object ManageAreasSentinel = new object();

        // Index constants for clarity
        private const int ManageAreasIndex = 0;
        private const int UnrestrictedIndex = 1;

        public static bool IsActive => isActive;

        public static void Open(Pawn pawn)
        {
            if (!GuardHelper.RequirePawn(pawn)) return;

            if (pawn.playerSettings == null || !pawn.playerSettings.SupportsAllowedAreas)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Area.NoSupport".Loc(pawn.LabelShort));
                return;
            }

            Map map = Find.CurrentMap;
            if (map?.areaManager == null)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Area.NoMap".Loc());
                return;
            }

            targetPawn = pawn;

            // Build options list: Manage Areas, Unrestricted, then assignable areas
            areaOptions.Clear();
            areaOptions.Add(ManageAreasSentinel);
            areaOptions.Add(null); // Unrestricted

            var areas = map.areaManager.AllAreas
                .Where(a => a.AssignableAsAllowed());
            foreach (var area in areas)
            {
                areaOptions.Add(area);
            }

            // Sync selection to pawn's current area
            Area currentArea = pawn.playerSettings.AreaRestrictionInPawnCurrentMap;
            if (currentArea == null)
            {
                selectedIndex = UnrestrictedIndex;
            }
            else
            {
                int index = areaOptions.IndexOf(currentArea);
                selectedIndex = index >= 0 ? index : UnrestrictedIndex;
            }

            isActive = true;
            typeaheadHelper = new TypeaheadSearchHelper();

            string currentAreaName = currentArea?.Label ?? "RimWorldAccess.Pawns.Area.Unrestricted".Translate().ToString();
            TolkHelper.Speak("RimWorldAccess.Pawns.Area.OpenAnnouncement".Loc(pawn.LabelShort, currentAreaName));
            AnnounceCurrentSelection();
        }

        public static void Close()
        {
            isActive = false;
            targetPawn = null;
            areaOptions.Clear();
            selectedIndex = 0;
            typeaheadHelper = null;
        }

        private static void CloseSilently()
        {
            isActive = false;
            areaOptions.Clear();
            selectedIndex = 0;
            typeaheadHelper = null;
            // Keep targetPawn - caller may need it
        }

        /// <summary>
        /// Handles typeahead character input from the layout-aware dispatcher.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive) return;
            if (typeaheadHelper == null) return;

            var labels = GetOptionLabels();
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

        public static void SelectNext()
        {
            if (areaOptions.Count == 0) return;
            selectedIndex = MenuHelper.SelectNext(selectedIndex, areaOptions.Count);
            AnnounceCurrentSelection();
        }

        public static void SelectPrevious()
        {
            if (areaOptions.Count == 0) return;
            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, areaOptions.Count);
            AnnounceCurrentSelection();
        }

        public static void JumpToFirst()
        {
            if (areaOptions.Count == 0) return;
            selectedIndex = MenuHelper.JumpToFirst();
            typeaheadHelper.ClearSearch();
            AnnounceCurrentSelection();
        }

        public static void JumpToLast()
        {
            if (areaOptions.Count == 0) return;
            selectedIndex = MenuHelper.JumpToLast(areaOptions.Count);
            typeaheadHelper.ClearSearch();
            AnnounceCurrentSelection();
        }

        public static void Confirm()
        {
            if (targetPawn == null || targetPawn.playerSettings == null)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Area.PawnGone".Loc());
                Close();
                return;
            }

            if (selectedIndex == ManageAreasIndex)
            {
                OpenManageAreas();
                return;
            }

            if (selectedIndex >= 0 && selectedIndex < areaOptions.Count)
            {
                object focused = areaOptions[selectedIndex];
                Area area = focused as Area; // null for Unrestricted

                targetPawn.playerSettings.AreaRestrictionInPawnCurrentMap = area;

                string areaName = area?.Label ?? "RimWorldAccess.Pawns.Area.Unrestricted".Translate().ToString();
                TolkHelper.Speak("RimWorldAccess.Pawns.Area.Applied".Loc(targetPawn.LabelShort, areaName));
                Close();
            }
        }

        public static void Cancel()
        {
            TolkHelper.Speak("RimWorldAccess.Pawns.Area.Cancelled".Loc());
            Close();
        }

        private static void OpenManageAreas()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;

            Pawn savedPawn = targetPawn;

            CloseSilently();

            // Callback for when returning from placement (Expand/Shrink)
            // This reopens Manage Areas instead of going back to area assignment
            Action returnToAreaMenuCallback = () =>
            {
                if (savedPawn != null && savedPawn.Spawned &&
                    savedPawn.playerSettings != null &&
                    savedPawn.playerSettings.SupportsAllowedAreas)
                {
                    Open(savedPawn);
                }
            };

            Action reopenManageAreasCallback = () =>
            {
                Find.DesignatorManager?.Deselect();

                if (map != null && Find.CurrentMap == map)
                {
                    WindowlessAreaState.Open(map, returnToAreaMenuCallback);
                }
                else
                {
                    returnToAreaMenuCallback();
                }
            };

            WindowlessAreaState.Open(map, reopenManageAreasCallback);
        }

        private static List<string> GetOptionLabels()
        {
            var labels = new List<string>();
            foreach (var option in areaOptions)
            {
                if (option == ManageAreasSentinel)
                    labels.Add("RimWorldAccess.Pawns.Area.ManageAreas".Translate());
                else if (option == null)
                    labels.Add("RimWorldAccess.Pawns.Area.Unrestricted".Translate());
                else if (option is Area area)
                    labels.Add(area.Label);
                else
                    labels.Add("RimWorldAccess.Pawns.Area.UnknownLabel".Translate());
            }
            return labels;
        }

        private static void AnnounceCurrentSelection()
        {
            if (areaOptions.Count == 0 || selectedIndex < 0 || selectedIndex >= areaOptions.Count)
                return;

            string position = MenuHelper.FormatPosition(selectedIndex, areaOptions.Count);
            object focused = areaOptions[selectedIndex];

            if (focused == ManageAreasSentinel)
            {
                string announcement = string.IsNullOrEmpty(position)
                    ? "RimWorldAccess.Pawns.Area.ManageAreas".Translate().ToString()
                    : "RimWorldAccess.Pawns.Area.ManageAreasWithPosition".Translate(position).ToString();
                TolkHelper.SpeakData(announcement);
            }
            else
            {
                Area area = focused as Area;
                string areaName = area?.Label ?? "RimWorldAccess.Pawns.Area.Unrestricted".Translate().ToString();

                // Indicate if this is the pawn's current area
                string currentSuffix = "";
                if (targetPawn?.playerSettings != null)
                {
                    Area pawnArea = targetPawn.playerSettings.AreaRestrictionInPawnCurrentMap;
                    if (area == pawnArea) // works for both null==null and area==area
                        currentSuffix = "RimWorldAccess.Pawns.Area.CurrentSuffix".Translate();
                }

                string content;
                if (area != null)
                {
                    int cellCount = area.TrueCount;
                    content = "RimWorldAccess.Pawns.Area.AreaWithCells".Translate(areaName, currentSuffix, cellCount);
                    area.MarkForDraw();
                }
                else
                {
                    content = "RimWorldAccess.Pawns.Area.AreaBare".Translate(areaName, currentSuffix);
                }

                string announcement = string.IsNullOrEmpty(position)
                    ? content
                    : "RimWorldAccess.Pawns.Area.WithPosition".Translate(content, position).ToString();
                TolkHelper.SpeakData(announcement);
            }
        }

        private static void AnnounceWithSearch()
        {
            if (areaOptions.Count == 0 || selectedIndex < 0 || selectedIndex >= areaOptions.Count)
                return;

            object focused = areaOptions[selectedIndex];

            if (focused == ManageAreasSentinel)
            {
                TolkHelper.SpeakData(typeaheadHelper.BuildItemAnnouncement("RimWorldAccess.Pawns.Area.ManageAreas".Translate()));
            }
            else
            {
                Area area = focused as Area;
                string areaName = area?.Label ?? "RimWorldAccess.Pawns.Area.Unrestricted".Translate().ToString();
                TolkHelper.SpeakData(typeaheadHelper.BuildItemAnnouncement(areaName));
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
                var labels = GetOptionLabels();
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
