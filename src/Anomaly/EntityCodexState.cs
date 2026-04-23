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
        private static EntityCategoryDef lastAnnouncedCategory;
        private static TypeaheadSearchHelper typeaheadHelper = new TypeaheadSearchHelper();

        private static System.Reflection.FieldInfo selectedEntryField;

        public static bool IsActive => isActive;
        public static bool HasActiveSearch => typeaheadHelper.HasActiveSearch;

        public static void Open(Dialog_EntityCodex dialog)
        {
            if (dialog == null)
                return;

            try
            {
                if (selectedEntryField == null)
                    selectedEntryField = HarmonyLib.AccessTools.Field(typeof(Dialog_EntityCodex), "selectedEntry");

                currentDialog = dialog;
                typeaheadHelper.ClearSearch();
                selectedIndex = 0;
                lastAnnouncedCategory = null;

                // Match vanilla sort: (orderInCategory, label). Dialog_EntityCodex uses .label, not .LabelCap.
                allEntries = DefDatabase<EntityCategoryDef>.AllDefsListForReading
                    .Where(HasVisibleEntries)
                    .OrderBy(c => c.listOrder)
                    .SelectMany(c => DefDatabase<EntityCodexEntryDef>.AllDefsListForReading
                        .Where(e => e.Visible && e.category == c)
                        .OrderBy(e => e.orderInCategory)
                        .ThenBy(e => e.label))
                    .ToList();

                // Seed selected index from the dialog's selectedEntry so "View entity codex" letter
                // actions and other pre-selected opens land on the correct entry.
                var dialogSelected = selectedEntryField?.GetValue(dialog) as EntityCodexEntryDef;
                if (dialogSelected != null)
                {
                    int idx = allEntries.IndexOf(dialogSelected);
                    if (idx >= 0) selectedIndex = idx;
                }

                isActive = true;

                string title = "EntityCodex".Translate().Resolve();
                TolkHelper.Speak($"{title}. {allEntries.Count} entries.", SpeechPriority.Normal);

                string desc = "EntityCodexDesc".Translate().Resolve();
                if (!string.IsNullOrEmpty(desc))
                    TolkHelper.Speak(SanitizeText(desc), SpeechPriority.Normal);

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
            lastAnnouncedCategory = null;
            typeaheadHelper.ClearSearch();
        }

        public static bool HandleInput(Event evt)
        {
            if (!isActive || currentDialog == null) return false;

            // Defer to the drill-in float menu when it's open. Without this, EntityCodex (priority
            // 4.64) eats keys before they reach WindowlessFloatMenuState (priority 5), so the
            // user can't navigate the picker we just opened. Same pattern as MechControlGroupState.
            if (WindowlessFloatMenuState.IsActive) return false;

            if (evt.type != EventType.KeyDown) return false;

            var key = evt.keyCode;
            bool ctrl = evt.control;
            bool alt = KeyboardHelper.IsAltHeld;

            // Alt+I drill-in picker — handled BEFORE the modifier swallow below so blind users
            // get the same picker drill-in convention as the rest of the mod.
            if (alt && key == KeyCode.I)
            {
                OpenDrillInPicker();
                return true;
            }

            // Modal dialog: consume all other modifier-key combos so they don't reach the game.
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
                    // PostClose patch speaks the close announcement so X-button and Close-button
                    // paths also announce.
                    currentDialog.Close(doCloseSound: false);
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

            // If category changed since last announcement, prepend the category name so users hear
            // when they've moved into a new section (vanilla shows category headers visually).
            // Category def labels are translated, so we don't add an English "Category:" prefix.
            var currentCategory = allEntries[selectedIndex].category;
            string categoryPrefix = "";
            if (currentCategory != lastAnnouncedCategory)
            {
                string categoryLabel = currentCategory?.LabelCap.Resolve();
                if (!string.IsNullOrEmpty(categoryLabel))
                    categoryPrefix = $"{categoryLabel}. ";
                lastAnnouncedCategory = currentCategory;
            }

            string announcement = FormatEntryAnnouncement(selectedIndex);
            string position = MenuHelper.FormatPosition(selectedIndex, allEntries.Count);

            string fullText = string.IsNullOrEmpty(position)
                ? $"{categoryPrefix}{announcement}"
                : $"{categoryPrefix}{announcement}, {position}";
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
                    string undiscoveredItem = "Undiscovered".Translate().Resolve();
                    var codex = Find.EntityCodex;
                    var linkedLabels = entry.linkedThings.Select(t =>
                        (codex != null && codex.Discovered(t))
                            ? t.LabelCap.ToString()
                            : undiscoveredItem);
                    sb.Append(". ");
                    sb.Append(string.Join(", ", linkedLabels));
                    sb.Append(".");
                }

                if (entry.discoveredResearchProjects?.Count > 0)
                {
                    sb.Append(" ");
                    sb.Append("ResearchUnlocks".Translate().Resolve());
                    sb.Append(": ");
                    sb.Append(string.Join(", ", entry.discoveredResearchProjects.Select(r => r.LabelCap.ToString())));
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

        private static void OpenDrillInPicker()
        {
            if (allEntries.Count == 0 || selectedIndex < 0 || selectedIndex >= allEntries.Count)
                return;

            var entry = allEntries[selectedIndex];
            if (!entry.Discovered)
            {
                TolkHelper.Speak("UndiscoveredEntityDesc".Translate().Resolve());
                return;
            }

            var options = new List<FloatMenuOption>();
            var codex = Find.EntityCodex;

            if (entry.linkedThings != null)
            {
                foreach (var linkedThing in entry.linkedThings)
                {
                    if (linkedThing == null) continue;
                    if (codex == null || !codex.Discovered(linkedThing)) continue;
                    var captured = linkedThing;
                    options.Add(new FloatMenuOption(
                        captured.LabelCap.ToString(),
                        () => Find.WindowStack.Add(new Dialog_InfoCard(captured))));
                }
            }

            if (entry.discoveredResearchProjects != null)
            {
                string researchPrefix = "ResearchUnlocks".Translate().Resolve();
                foreach (var project in entry.discoveredResearchProjects)
                {
                    if (project == null) continue;
                    var captured = project;
                    options.Add(new FloatMenuOption(
                        $"{researchPrefix}: {captured.LabelCap}",
                        () => WindowlessResearchMenuState.OpenAndSelectProject(captured)));
                }
            }

            if (options.Count == 0)
            {
                TolkHelper.Speak("None".Translate().Resolve());
                return;
            }

            WindowlessFloatMenuState.Open(options, colonistOrders: false);
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
