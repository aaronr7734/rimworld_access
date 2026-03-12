using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    public static class PawnFilterState
    {
        private static bool isActive = false;
        private static PawnFilter workingCopy;
        private static List<FilterMenuItem> menuItems = new List<FilterMenuItem>();
        private static int selectedIndex = 0;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        public static bool IsActive => isActive;

        public static void Open()
        {
            if (isActive) return;

            // Ensure active filter is initialized
            if (PawnFilterData.ActiveFilter.Skills.Count == 0)
                PawnFilterData.ActiveFilter.InitializeSkills();

            // Create working copy for save/discard behavior
            workingCopy = PawnFilterData.ActiveFilter.Clone();
            isActive = true;
            selectedIndex = 0;
            typeahead.ClearSearch();

            RebuildMenu();

            // Skip past first section header
            if (menuItems.Count > 1 && menuItems[0].IsSectionHeader)
                selectedIndex = 1;

            int filterCount = workingCopy.GetActiveFilterCount();
            string countPart = filterCount > 0 ? $" {filterCount} active filters." : "";
            TolkHelper.Speak($"Pawn filter editor.{countPart}");
            AnnounceCurrentItem();
        }

        public static void Close(bool save)
        {
            if (!isActive) return;

            if (save)
            {
                PawnFilterData.ActiveFilter.CopyFrom(workingCopy);
                int filterCount = PawnFilterData.ActiveFilter.GetActiveFilterCount();
                TolkHelper.Speak($"Filter saved. {filterCount} active filters.");
            }
            else
            {
                TolkHelper.Speak("Filter editor closed. Changes discarded.");
            }

            isActive = false;
            workingCopy = null;
            menuItems.Clear();
            selectedIndex = 0;
            typeahead.ClearSearch();
        }

        private static void RebuildMenu()
        {
            menuItems = PawnFilterHelper.BuildMenuItems(workingCopy);

            if (selectedIndex >= menuItems.Count)
                selectedIndex = menuItems.Count - 1;
            if (selectedIndex < 0)
                selectedIndex = 0;

            // Skip section header if we landed on one
            if (selectedIndex < menuItems.Count && menuItems[selectedIndex].IsSectionHeader)
                SkipToNextNonHeader(1);
        }

        public static bool HandleInput(KeyCode key, Event currentEvent)
        {
            if (!isActive || menuItems.Count == 0) return false;

            bool shift = currentEvent.shift;
            bool alt = currentEvent.alt;

            // Alt+S: Save and close
            if (alt && key == KeyCode.S)
            {
                Close(save: true);
                return true;
            }

            // Escape: Discard and close
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentItem();
                }
                else
                {
                    Close(save: false);
                }
                return true;
            }

            // Up/Down: Navigate (skip section headers)
            if (key == KeyCode.UpArrow)
            {
                typeahead.ClearSearch();
                NavigateUp();
                return true;
            }
            if (key == KeyCode.DownArrow)
            {
                typeahead.ClearSearch();
                NavigateDown();
                return true;
            }

            // Page Up/Down: Jump between section headers
            if (key == KeyCode.PageUp)
            {
                typeahead.ClearSearch();
                JumpToSection(-1);
                return true;
            }
            if (key == KeyCode.PageDown)
            {
                typeahead.ClearSearch();
                JumpToSection(1);
                return true;
            }

            // Left/Right: Adjust values
            if (key == KeyCode.LeftArrow)
            {
                typeahead.ClearSearch();
                AdjustValue(-1, shift);
                return true;
            }
            if (key == KeyCode.RightArrow)
            {
                typeahead.ClearSearch();
                AdjustValue(1, shift);
                return true;
            }

            // Enter/Space: Activate (cycle passion, open picker, etc.)
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Space)
            {
                typeahead.ClearSearch();
                ActivateItem();
                return true;
            }

            // Delete: Remove trait
            if (key == KeyCode.Delete)
            {
                typeahead.ClearSearch();
                DeleteCurrentTrait();
                return true;
            }

            // Home/End
            if (key == KeyCode.Home)
            {
                typeahead.ClearSearch();
                selectedIndex = 0;
                SkipToNextNonHeader(1);
                AnnounceCurrentItem();
                return true;
            }
            if (key == KeyCode.End)
            {
                typeahead.ClearSearch();
                selectedIndex = menuItems.Count - 1;
                SkipToNextNonHeader(-1);
                AnnounceCurrentItem();
                return true;
            }

            // Backspace: Remove search character
            if (key == KeyCode.Backspace)
            {
                if (typeahead.HasActiveSearch)
                {
                    var labels = menuItems.Where(i => !i.IsSectionHeader).Select(i => i.Label).ToList();
                    if (typeahead.ProcessBackspace(labels, out int newIndex))
                    {
                        if (newIndex >= 0)
                            selectedIndex = MapNonHeaderIndexToFull(newIndex);
                        AnnounceWithSearch();
                    }
                    return true;
                }
            }

            return false;
        }

        public static bool HandleCharacterInput(char c)
        {
            if (!isActive || menuItems.Count == 0) return false;

            if (char.IsLetterOrDigit(c))
            {
                HandleTypeahead(c);
                return true;
            }

            return false;
        }

        // ===== NAVIGATION =====

        private static void NavigateUp()
        {
            int prev = selectedIndex - 1;
            if (prev < 0) prev = menuItems.Count - 1;

            // Skip section headers
            while (prev >= 0 && menuItems[prev].IsSectionHeader)
            {
                prev--;
                if (prev < 0) prev = menuItems.Count - 1;
            }

            selectedIndex = prev;
            AnnounceCurrentItem();
        }

        private static void NavigateDown()
        {
            int next = selectedIndex + 1;
            if (next >= menuItems.Count) next = 0;

            // Skip section headers
            while (next < menuItems.Count && menuItems[next].IsSectionHeader)
            {
                next++;
                if (next >= menuItems.Count) next = 0;
            }

            selectedIndex = next;
            AnnounceCurrentItem();
        }

        private static void JumpToSection(int direction)
        {
            // Find section headers
            var headerIndices = new List<int>();
            for (int i = 0; i < menuItems.Count; i++)
            {
                if (menuItems[i].IsSectionHeader)
                    headerIndices.Add(i);
            }

            if (headerIndices.Count == 0) return;

            // Find current section
            int currentSection = -1;
            for (int i = headerIndices.Count - 1; i >= 0; i--)
            {
                if (headerIndices[i] <= selectedIndex)
                {
                    currentSection = i;
                    break;
                }
            }

            // Jump to target section
            int targetSection = currentSection + direction;
            if (targetSection < 0) targetSection = headerIndices.Count - 1;
            if (targetSection >= headerIndices.Count) targetSection = 0;

            // Land on first item after the header
            int targetIndex = headerIndices[targetSection] + 1;
            if (targetIndex >= menuItems.Count)
                targetIndex = headerIndices[targetSection]; // no items after header

            selectedIndex = targetIndex;

            // Announce section name then current item
            string sectionName = menuItems[headerIndices[targetSection]].Label;
            TolkHelper.Speak(sectionName);
            AnnounceCurrentItem();
        }

        private static void SkipToNextNonHeader(int direction)
        {
            int safety = menuItems.Count;
            while (safety-- > 0 && selectedIndex >= 0 && selectedIndex < menuItems.Count
                && menuItems[selectedIndex].IsSectionHeader)
            {
                selectedIndex += direction;
                if (selectedIndex >= menuItems.Count) selectedIndex = 0;
                if (selectedIndex < 0) selectedIndex = menuItems.Count - 1;
            }
        }

        // ===== VALUE ADJUSTMENT =====

        private static void AdjustValue(int direction, bool shift)
        {
            if (selectedIndex < 0 || selectedIndex >= menuItems.Count) return;
            var item = menuItems[selectedIndex];

            switch (item.ItemType)
            {
                case FilterItemType.Skill:
                    PawnFilterHelper.AdjustSkillLevel(item.SkillFilter, direction * (shift ? 5 : 1));
                    item.Label = PawnFilterHelper.FormatSkillLabel(item.SkillFilter);
                    AnnounceCurrentItem();
                    break;

                case FilterItemType.PassionMin:
                    PawnFilterHelper.AdjustPassion(workingCopy, isMin: true, direction * (shift ? 3 : 1));
                    item.Label = PawnFilterHelper.FormatPassionMinLabel(workingCopy);
                    UpdateItemLabel(FilterItemType.PassionMax, PawnFilterHelper.FormatPassionMaxLabel(workingCopy));
                    AnnounceCurrentItem();
                    break;

                case FilterItemType.PassionMax:
                    PawnFilterHelper.AdjustPassion(workingCopy, isMin: false, direction * (shift ? 3 : 1));
                    item.Label = PawnFilterHelper.FormatPassionMaxLabel(workingCopy);
                    UpdateItemLabel(FilterItemType.PassionMin, PawnFilterHelper.FormatPassionMinLabel(workingCopy));
                    AnnounceCurrentItem();
                    break;

                case FilterItemType.SkillPointsMin:
                    PawnFilterHelper.AdjustSkillPoints(workingCopy, isMin: true, direction * (shift ? 10 : 1));
                    item.Label = PawnFilterHelper.FormatSkillPointsMinLabel(workingCopy);
                    UpdateItemLabel(FilterItemType.SkillPointsMax, PawnFilterHelper.FormatSkillPointsMaxLabel(workingCopy));
                    AnnounceCurrentItem();
                    break;

                case FilterItemType.SkillPointsMax:
                    PawnFilterHelper.AdjustSkillPoints(workingCopy, isMin: false, direction * (shift ? 10 : 1));
                    item.Label = PawnFilterHelper.FormatSkillPointsMaxLabel(workingCopy);
                    UpdateItemLabel(FilterItemType.SkillPointsMin, PawnFilterHelper.FormatSkillPointsMinLabel(workingCopy));
                    AnnounceCurrentItem();
                    break;

                case FilterItemType.AgeMin:
                    PawnFilterHelper.AdjustAge(workingCopy, isMin: true, direction * (shift ? 5 : 1));
                    item.Label = PawnFilterHelper.FormatAgeMinLabel(workingCopy);
                    // Also update max label in case it was clamped
                    UpdateItemLabel(FilterItemType.AgeMax, PawnFilterHelper.FormatAgeMaxLabel(workingCopy));
                    AnnounceCurrentItem();
                    break;

                case FilterItemType.AgeMax:
                    PawnFilterHelper.AdjustAge(workingCopy, isMin: false, direction * (shift ? 5 : 1));
                    item.Label = PawnFilterHelper.FormatAgeMaxLabel(workingCopy);
                    // Also update min label in case it was clamped
                    UpdateItemLabel(FilterItemType.AgeMin, PawnFilterHelper.FormatAgeMinLabel(workingCopy));
                    AnnounceCurrentItem();
                    break;

                case FilterItemType.Gender:
                    PawnFilterHelper.CycleGender(workingCopy, direction);
                    item.Label = PawnFilterHelper.FormatGenderLabel(workingCopy);
                    AnnounceCurrentItem();
                    break;

                case FilterItemType.Health:
                    PawnFilterHelper.CycleHealth(workingCopy, direction);
                    item.Label = PawnFilterHelper.FormatHealthLabel(workingCopy);
                    AnnounceCurrentItem();
                    break;

                case FilterItemType.Work:
                    PawnFilterHelper.CycleWork(workingCopy, direction);
                    item.Label = PawnFilterHelper.FormatWorkLabel(workingCopy);
                    AnnounceCurrentItem();
                    break;

                case FilterItemType.RerollLimit:
                    PawnFilterHelper.AdjustRerollLimit(workingCopy, direction);
                    item.Label = PawnFilterHelper.FormatRerollLimitLabel(workingCopy);
                    AnnounceCurrentItem();
                    break;

                default:
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    break;
            }
        }

        private static void UpdateItemLabel(FilterItemType type, string newLabel)
        {
            for (int i = 0; i < menuItems.Count; i++)
            {
                if (menuItems[i].ItemType == type)
                {
                    menuItems[i].Label = newLabel;
                    break;
                }
            }
        }

        // ===== ACTIVATION =====

        private static void ActivateItem()
        {
            if (selectedIndex < 0 || selectedIndex >= menuItems.Count) return;
            var item = menuItems[selectedIndex];

            switch (item.ItemType)
            {
                case FilterItemType.Skill:
                    PawnFilterHelper.CyclePassion(item.SkillFilter);
                    item.Label = PawnFilterHelper.FormatSkillLabel(item.SkillFilter);
                    AnnounceCurrentItem();
                    break;

                case FilterItemType.AddRequiredTrait:
                    OpenTraitPicker(TraitFilterMode.Required);
                    break;

                case FilterItemType.AddExcludedTrait:
                    OpenTraitPicker(TraitFilterMode.Excluded);
                    break;

                case FilterItemType.ClearAll:
                    workingCopy.Reset();
                    workingCopy.InitializeSkills();
                    RebuildMenu();
                    // Jump to first non-header
                    selectedIndex = 0;
                    SkipToNextNonHeader(1);
                    TolkHelper.Speak("ClearAll".Translate());
                    AnnounceCurrentItem();
                    break;

                default:
                    // For items that use Left/Right, Enter/Space does nothing special
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    break;
            }
        }

        private static void OpenTraitPicker(TraitFilterMode mode)
        {
            var options = PawnFilterHelper.BuildTraitPickerOptions(workingCopy, mode, () =>
            {
                string modeLabel = mode == TraitFilterMode.Required
                    ? "Required".Translate()
                    : "Excluded".Translate();
                var lastTrait = workingCopy.Traits.Last();
                TolkHelper.Speak($"{modeLabel}: {lastTrait.Label} added");
                RebuildMenu();
                AnnounceCurrentItem();
            });

            if (options.Count == 0)
            {
                TolkHelper.Speak("No available traits");
                return;
            }

            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        private static void DeleteCurrentTrait()
        {
            if (selectedIndex < 0 || selectedIndex >= menuItems.Count) return;
            var item = menuItems[selectedIndex];

            if (item.ItemType != FilterItemType.TraitEntry || item.TraitFilter == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            string modeLabel = item.TraitFilter.Mode == TraitFilterMode.Required
                ? "Required".Translate()
                : "Excluded".Translate();
            string traitLabel = item.TraitFilter.Label;
            workingCopy.Traits.Remove(item.TraitFilter);
            TolkHelper.Speak($"{modeLabel}: {traitLabel} removed");

            RebuildMenu();
            AnnounceCurrentItem();
        }

        // ===== TYPEAHEAD =====

        private static void HandleTypeahead(char c)
        {
            // Build labels for non-header items only
            var labels = menuItems.Where(i => !i.IsSectionHeader).Select(i => i.Label).ToList();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                    selectedIndex = MapNonHeaderIndexToFull(newIndex);
                AnnounceWithSearch();
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
            }
        }

        private static int MapNonHeaderIndexToFull(int nonHeaderIndex)
        {
            int count = 0;
            for (int i = 0; i < menuItems.Count; i++)
            {
                if (!menuItems[i].IsSectionHeader)
                {
                    if (count == nonHeaderIndex)
                        return i;
                    count++;
                }
            }
            return 0;
        }

        // ===== ANNOUNCEMENTS =====

        private static void AnnounceCurrentItem()
        {
            if (selectedIndex < 0 || selectedIndex >= menuItems.Count) return;
            var item = menuItems[selectedIndex];

            // Count non-header items for position
            int position = 0;
            int total = 0;
            for (int i = 0; i < menuItems.Count; i++)
            {
                if (!menuItems[i].IsSectionHeader)
                {
                    total++;
                    if (i < selectedIndex) position++;
                    if (i == selectedIndex) position++;
                }
            }

            string positionPart = MenuHelper.FormatPosition(position - 1, total);
            string announcement = item.Label;
            if (!string.IsNullOrEmpty(positionPart))
                announcement += $" ({positionPart})";

            TolkHelper.Speak(announcement);
        }

        private static void AnnounceWithSearch()
        {
            if (!typeahead.HasActiveSearch) { AnnounceCurrentItem(); return; }
            var item = menuItems[selectedIndex];
            TolkHelper.Speak($"{item.Label}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'");
        }
    }
}
