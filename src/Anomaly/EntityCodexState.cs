using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public static class EntityCodexState
    {
        private static bool isActive;
        private static Dialog_EntityCodex currentDialog;
        private static List<EntityCodexEntryDef> allEntries = new List<EntityCodexEntryDef>();
        private static int selectedIndex;
        private static TypeaheadSearchHelper typeaheadHelper = new TypeaheadSearchHelper();

        public static bool IsActive => isActive;
        public static bool HasActiveSearch => typeaheadHelper.HasActiveSearch;

        public static void Open(Dialog_EntityCodex dialog)
        {
            if (dialog == null)
                return;

            try
            {
                currentDialog = dialog;
                typeaheadHelper.ClearSearch();
                selectedIndex = 0;

                // Build flat entry list ordered by category listOrder, then orderInCategory, then label.
                allEntries = DefDatabase<EntityCategoryDef>.AllDefsListForReading
                    .Where(HasVisibleEntries)
                    .OrderBy(c => c.listOrder)
                    .SelectMany(c => DefDatabase<EntityCodexEntryDef>.AllDefsListForReading
                        .Where(e => e.Visible && e.category == c)
                        .OrderBy(e => e.orderInCategory)
                        .ThenBy(e => e.LabelCap.ToString()))
                    .ToList();

                isActive = true;

                string title = "EntityCodex".Translate().Resolve();
                TolkHelper.Speak($"{title}. {allEntries.Count} entries.", SpeechPriority.Normal);

                if (allEntries.Count > 0)
                    AnnounceCurrentSelection();
            }
            catch (Exception ex)
            {
                Log.Error($"[EntityCodexState] Error opening: {ex.Message}");
                Close();
            }
        }

        public static void Close()
        {
            isActive = false;
            currentDialog = null;
            allEntries.Clear();
            selectedIndex = 0;
            typeaheadHelper.ClearSearch();
        }

        public static bool HandleInput(Event evt)
        {
            if (!isActive || currentDialog == null) return false;
            if (evt.type != EventType.KeyDown) return false;

            var key = evt.keyCode;
            bool ctrl = evt.control;
            bool alt = KeyboardHelper.IsAltHeld;

            // Modal dialog: consume all modifier-key combos.
            if (ctrl || alt) return true;

            switch (key)
            {
                case KeyCode.Home:
                    typeaheadHelper.ClearSearch();
                    selectedIndex = MenuHelper.JumpToFirst();
                    AnnounceCurrentSelection();
                    return true;

                case KeyCode.End:
                    typeaheadHelper.ClearSearch();
                    selectedIndex = MenuHelper.JumpToLast(allEntries.Count);
                    AnnounceCurrentSelection();
                    return true;

                case KeyCode.Escape:
                    if (typeaheadHelper.HasActiveSearch)
                    {
                        typeaheadHelper.ClearSearchAndAnnounce();
                        AnnounceCurrentSelection();
                        return true;
                    }
                    currentDialog.Close(doCloseSound: false);
                    TolkHelper.Speak("Entity Codex closed.");
                    return true;

                case KeyCode.Backspace:
                    if (typeaheadHelper.HasActiveSearch)
                    {
                        var labelsBack = GetEntryLabels();
                        if (typeaheadHelper.ProcessBackspace(labelsBack, out int backIdx))
                        {
                            selectedIndex = backIdx;
                            AnnounceWithSearch();
                        }
                        else
                        {
                            AnnounceCurrentSelection();
                        }
                    }
                    return true;

                case KeyCode.UpArrow:
                    if (typeaheadHelper.HasActiveSearch && !typeaheadHelper.HasNoMatches)
                    {
                        int prev = typeaheadHelper.GetPreviousMatch(selectedIndex);
                        if (prev >= 0) selectedIndex = prev;
                        AnnounceWithSearch();
                    }
                    else if (allEntries.Count > 0)
                    {
                        selectedIndex = MenuHelper.SelectPrevious(selectedIndex, allEntries.Count);
                        AnnounceCurrentSelection();
                    }
                    return true;

                case KeyCode.DownArrow:
                    if (typeaheadHelper.HasActiveSearch && !typeaheadHelper.HasNoMatches)
                    {
                        int next = typeaheadHelper.GetNextMatch(selectedIndex);
                        if (next >= 0) selectedIndex = next;
                        AnnounceWithSearch();
                    }
                    else if (allEntries.Count > 0)
                    {
                        selectedIndex = MenuHelper.SelectNext(selectedIndex, allEntries.Count);
                        AnnounceCurrentSelection();
                    }
                    return true;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    AnnounceCurrentSelection();
                    return true;

                default:
                    bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                    bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;
                    if (isLetter || isNumber)
                    {
                        TypeaheadCharacterBuffer.RequestCharacter(c =>
                        {
                            var labels = GetEntryLabels();
                            if (typeaheadHelper.ProcessCharacterInput(c, labels, out int newIdx))
                            {
                                if (newIdx >= 0)
                                {
                                    selectedIndex = newIdx;
                                    AnnounceWithSearch();
                                }
                            }
                            else
                            {
                                TolkHelper.Speak($"No matches for '{typeaheadHelper.LastFailedSearch}'");
                            }
                        });
                        return true;
                    }
                    return true; // Modal window: consume all unhandled keys.
            }
        }

        private static void AnnounceCurrentSelection()
        {
            if (allEntries.Count == 0 || selectedIndex < 0 || selectedIndex >= allEntries.Count)
                return;

            string announcement = FormatEntryAnnouncement(selectedIndex);
            string position = MenuHelper.FormatPosition(selectedIndex, allEntries.Count);

            string fullText = string.IsNullOrEmpty(position)
                ? announcement
                : $"{announcement}, {position}";
            TolkHelper.Speak(fullText, SpeechPriority.Normal);
        }

        private static void AnnounceWithSearch()
        {
            if (allEntries.Count == 0 || selectedIndex < 0 || selectedIndex >= allEntries.Count)
                return;

            if (!typeaheadHelper.HasActiveSearch)
            {
                AnnounceCurrentSelection();
                return;
            }

            TolkHelper.Speak(
                $"{FormatEntryAnnouncement(selectedIndex)}, {typeaheadHelper.CurrentMatchPosition} of {typeaheadHelper.MatchCount} matches for '{typeaheadHelper.SearchBuffer}'");
        }

        private static string FormatEntryAnnouncement(int index)
        {
            var entry = allEntries[index];
            bool discovered = entry.Discovered;

            var sb = new StringBuilder();

            if (discovered)
            {
                sb.Append(entry.LabelCap.Resolve());
                string category = entry.category?.LabelCap.Resolve();
                if (!string.IsNullOrEmpty(category))
                {
                    sb.Append(", ");
                    sb.Append(category);
                }
                sb.Append(". ");
                sb.Append(SanitizeText(entry.Description));

                if (entry.linkedThings?.Count > 0)
                {
                    sb.Append(". ");
                    sb.Append(string.Join(", ", entry.linkedThings.Select(t => t.LabelCap.ToString())));
                    sb.Append(".");
                }
            }
            else
            {
                sb.Append("UndiscoveredEntity".Translate().Resolve());
                string category = entry.category?.LabelCap.Resolve();
                if (!string.IsNullOrEmpty(category))
                {
                    sb.Append(", ");
                    sb.Append(category);
                }
                sb.Append(". ");
                sb.Append("UndiscoveredEntityDesc".Translate().Resolve());
            }

            return sb.ToString();
        }

        private static List<string> GetEntryLabels()
        {
            var labels = new List<string>(allEntries.Count);
            string undiscoveredLabel = "UndiscoveredEntity".Translate().ToString();
            foreach (var entry in allEntries)
            {
                labels.Add(entry.Discovered ? entry.LabelCap.ToString() : undiscoveredLabel);
            }
            return labels;
        }

        private static bool HasVisibleEntries(EntityCategoryDef cat)
        {
            return DefDatabase<EntityCodexEntryDef>.AllDefsListForReading
                .Any(e => e.Visible && e.category == cat);
        }

        private static string SanitizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\n\n", ". ").Replace("\n", " ").Trim();
        }
    }
}
