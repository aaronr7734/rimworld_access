using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages a tabbed accessible interface for the growth moment dialog.
    /// Tab 1 (Info): Growth summary, flavor text, work types, nickname changes.
    /// Tab 2 (Passions): Skill passion selection (only if passions offered, tiers 4-8).
    /// Tab 3 (Traits): Trait selection with full descriptions.
    /// Tab/Shift+Tab switches between tabs. Alt+S confirms choices.
    /// </summary>
    public static class GrowthMomentState
    {
        private enum Tab { Info, Passions, Traits }

        private class PassionItem
        {
            public SkillDef Skill;
            public Passion CurrentPassion;
            public Passion NewPassion;
            public int SkillLevel;
            public string Label;
        }

        private class TraitItem
        {
            public Trait TraitOption;
            public string Label;
            public string Description;
            public bool IsNoTrait;
        }

        private static bool isActive = false;
        private static ChoiceLetter_GrowthMoment letter;
        private static Window dialog;
        private static bool isArchiveView;

        // Tab management
        private static List<Tab> availableTabs = new List<Tab>();
        private static int currentTabIndex = 0;

        // Info tab
        private static string[] infoLines;
        private static int infoIndex = 0;

        // Passions tab
        private static List<PassionItem> passionItems = new List<PassionItem>();
        private static int passionIndex = 0;
        private static TypeaheadSearchHelper passionTypeahead = new TypeaheadSearchHelper();

        // Traits tab
        private static List<TraitItem> traitItems = new List<TraitItem>();
        private static int traitIndex = 0;
        private static TypeaheadSearchHelper traitTypeahead = new TypeaheadSearchHelper();

        // Selection tracking
        private static List<SkillDef> selectedPassions = new List<SkillDef>();
        private static Trait selectedTrait;

        public static bool IsActive => isActive;

        private static bool HasChoicesToMake()
        {
            return !isArchiveView && (passionItems.Count > 0 || traitItems.Count > 0);
        }

        /// <summary>
        /// Opens the growth moment accessible interface.
        /// Extracts data from the letter, builds tabs, reads info aloud, then focuses first selection tab.
        /// </summary>
        public static void Open(ChoiceLetter_GrowthMoment growthLetter, Window growthDialog)
        {
            if (growthLetter == null || growthDialog == null)
            {
                Log.Error("[RimWorld Access] Cannot open growth moment state: null letter or dialog");
                return;
            }

            letter = growthLetter;
            dialog = growthDialog;
            isArchiveView = letter.ArchiveView;
            isActive = true;

            // Reset selections
            selectedPassions.Clear();
            selectedTrait = null;

            // Build tab content
            BuildInfoLines();
            BuildPassionItems();
            BuildTraitItems();
            BuildAvailableTabs();

            // Reset indices
            infoIndex = 0;
            passionIndex = 0;
            traitIndex = 0;
            passionTypeahead.ClearSearch();
            traitTypeahead.ClearSearch();

            // Announce opening with full info
            AnnounceOpening();

            // Auto-focus first selection tab (skip Info)
            if (!isArchiveView && availableTabs.Count > 1)
            {
                currentTabIndex = 1;
                AnnounceTabSwitch();
                AnnounceCurrentItem();
            }
            else
            {
                currentTabIndex = 0;
                AnnounceCurrentItem();
            }
        }

        /// <summary>
        /// Closes the growth moment state.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            letter = null;
            dialog = null;
            passionItems.Clear();
            traitItems.Clear();
            selectedPassions.Clear();
            selectedTrait = null;
            infoLines = null;
            availableTabs.Clear();
            passionTypeahead.ClearSearch();
            traitTypeahead.ClearSearch();
        }

        /// <summary>
        /// Handles all keyboard input for the growth moment state.
        /// </summary>
        /// <returns>True if input was handled.</returns>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive)
                return false;

            if (Event.current.type != EventType.KeyDown)
                return false;

            // Alt+S: Confirm choices
            if (key == KeyCode.S && alt && !ctrl && !shift)
            {
                if (!isArchiveView)
                    ConfirmChoices();
                return true;
            }

            // Tab / Shift+Tab: switch tabs
            if (key == KeyCode.Tab && !ctrl && !alt)
            {
                SwitchTab(!shift);
                return true;
            }

            // Home - jump to first
            if (key == KeyCode.Home && !ctrl && !alt)
            {
                JumpToFirst();
                return true;
            }

            // End - jump to last
            if (key == KeyCode.End && !ctrl && !alt)
            {
                JumpToLast();
                return true;
            }

            // Escape
            if (key == KeyCode.Escape)
            {
                var typeahead = GetCurrentTypeahead();
                if (typeahead != null && typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentItem();
                    return true;
                }

                if (isArchiveView)
                {
                    CloseDialog();
                    return true;
                }

                PostponeChoices();
                return true;
            }

            // Up arrow
            if (key == KeyCode.UpArrow)
            {
                SelectPrevious();
                return true;
            }

            // Down arrow
            if (key == KeyCode.DownArrow)
            {
                SelectNext();
                return true;
            }

            // Enter / Space - toggle selection
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Space)
            {
                ToggleSelection();
                return true;
            }

            // Backspace for search
            if (key == KeyCode.Backspace)
            {
                var typeahead = GetCurrentTypeahead();
                if (typeahead != null && typeahead.HasActiveSearch)
                {
                    var labels = GetCurrentLabels();
                    if (typeahead.ProcessBackspace(labels, out int newIndex))
                    {
                        if (newIndex >= 0)
                            SetCurrentIndex(newIndex);
                        AnnounceWithSearch();
                    }
                    return true;
                }
                return false;
            }

            // Typeahead characters (passion and trait tabs only)
            Tab currentTab = availableTabs[currentTabIndex];
            if (currentTab != Tab.Info)
            {
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                if ((isLetter || isNumber) && !alt)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Public entry point for the unified typeahead dispatcher.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!IsActive) return;
            var typeahead = GetCurrentTypeahead();
            var labels = GetCurrentLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    SetCurrentIndex(newIndex);
                    AnnounceWithSearch();
                }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
            }
        }

        // ========== Tab Switching ==========

        private static void SwitchTab(bool forward)
        {
            if (availableTabs.Count <= 1)
            {
                TolkHelper.Speak("Only one tab available");
                return;
            }

            var typeahead = GetCurrentTypeahead();
            typeahead?.ClearSearch();

            if (forward)
            {
                currentTabIndex++;
                if (currentTabIndex >= availableTabs.Count)
                {
                    if (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true)
                        currentTabIndex = 0;
                    else
                        currentTabIndex = availableTabs.Count - 1;
                }
            }
            else
            {
                currentTabIndex--;
                if (currentTabIndex < 0)
                {
                    if (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true)
                        currentTabIndex = availableTabs.Count - 1;
                    else
                        currentTabIndex = 0;
                }
            }

            AnnounceTabSwitch();
            AnnounceCurrentItem();
        }

        private static void AnnounceTabSwitch()
        {
            Tab tab = availableTabs[currentTabIndex];
            string tabName = GetTabName(tab);
            string position = MenuHelper.FormatPosition(currentTabIndex, availableTabs.Count);
            string positionSuffix = string.IsNullOrEmpty(position) ? "" : $". {position}";

            if (tab == Tab.Passions && !isArchiveView)
            {
                string chooseText = letter.passionGainsCount == 1
                    ? "Choose 1"
                    : $"Choose {letter.passionGainsCount} of {passionItems.Count}";
                TolkHelper.Speak($"{tabName}. {chooseText}{positionSuffix}");
            }
            else if (tab == Tab.Traits && !isArchiveView)
            {
                TolkHelper.Speak($"{tabName}. Choose 1{positionSuffix}");
            }
            else
            {
                TolkHelper.Speak($"{tabName}{positionSuffix}");
            }
        }

        // ========== Navigation ==========

        private static void SelectNext()
        {
            Tab tab = availableTabs[currentTabIndex];
            switch (tab)
            {
                case Tab.Info:
                    if (infoLines != null && infoLines.Length > 0)
                        infoIndex = MenuHelper.SelectNext(infoIndex, infoLines.Length);
                    break;
                case Tab.Passions:
                    if (passionItems.Count > 0)
                        passionIndex = MenuHelper.SelectNext(passionIndex, passionItems.Count);
                    break;
                case Tab.Traits:
                    if (traitItems.Count > 0)
                        traitIndex = MenuHelper.SelectNext(traitIndex, traitItems.Count);
                    break;
            }
            AnnounceCurrentItem();
        }

        private static void SelectPrevious()
        {
            Tab tab = availableTabs[currentTabIndex];
            switch (tab)
            {
                case Tab.Info:
                    if (infoLines != null && infoLines.Length > 0)
                        infoIndex = MenuHelper.SelectPrevious(infoIndex, infoLines.Length);
                    break;
                case Tab.Passions:
                    if (passionItems.Count > 0)
                        passionIndex = MenuHelper.SelectPrevious(passionIndex, passionItems.Count);
                    break;
                case Tab.Traits:
                    if (traitItems.Count > 0)
                        traitIndex = MenuHelper.SelectPrevious(traitIndex, traitItems.Count);
                    break;
            }
            AnnounceCurrentItem();
        }

        private static void JumpToFirst()
        {
            Tab tab = availableTabs[currentTabIndex];
            switch (tab)
            {
                case Tab.Info:
                    infoIndex = MenuHelper.JumpToFirst();
                    break;
                case Tab.Passions:
                    passionIndex = MenuHelper.JumpToFirst();
                    break;
                case Tab.Traits:
                    traitIndex = MenuHelper.JumpToFirst();
                    break;
            }
            AnnounceCurrentItem();
        }

        private static void JumpToLast()
        {
            Tab tab = availableTabs[currentTabIndex];
            switch (tab)
            {
                case Tab.Info:
                    infoIndex = MenuHelper.JumpToLast(infoLines?.Length ?? 0);
                    break;
                case Tab.Passions:
                    passionIndex = MenuHelper.JumpToLast(passionItems.Count);
                    break;
                case Tab.Traits:
                    traitIndex = MenuHelper.JumpToLast(traitItems.Count);
                    break;
            }
            AnnounceCurrentItem();
        }

        // ========== Selection ==========

        private static void ToggleSelection()
        {
            Tab tab = availableTabs[currentTabIndex];

            if (tab == Tab.Info)
            {
                if (!isArchiveView && !HasChoicesToMake())
                    ConfirmChoices();
                return;
            }

            if (isArchiveView)
            {
                TolkHelper.Speak("Archive view, read only");
                return;
            }

            if (tab == Tab.Passions)
                TogglePassion();
            else if (tab == Tab.Traits)
                SelectTrait();
        }

        private static void TogglePassion()
        {
            if (passionIndex < 0 || passionIndex >= passionItems.Count)
                return;

            PassionItem item = passionItems[passionIndex];

            if (letter.passionGainsCount == 1)
            {
                // Radio button mode: select this one, deselect any other
                selectedPassions.Clear();
                selectedPassions.Add(item.Skill);
                TolkHelper.Speak($"{item.Label} selected. {selectedPassions.Count} of {letter.passionGainsCount} passions chosen");
            }
            else
            {
                // Checkbox mode: toggle
                if (selectedPassions.Contains(item.Skill))
                {
                    selectedPassions.Remove(item.Skill);
                    TolkHelper.Speak($"{item.Label} deselected. {selectedPassions.Count} of {letter.passionGainsCount} passions chosen");
                }
                else
                {
                    if (selectedPassions.Count >= letter.passionGainsCount)
                    {
                        TolkHelper.Speak($"Already selected {letter.passionGainsCount} of {letter.passionGainsCount} passions. Deselect one first");
                        return;
                    }
                    selectedPassions.Add(item.Skill);
                    TolkHelper.Speak($"{item.Label} selected. {selectedPassions.Count} of {letter.passionGainsCount} passions chosen");
                }
            }
        }

        private static void SelectTrait()
        {
            if (traitIndex < 0 || traitIndex >= traitItems.Count)
                return;

            TraitItem item = traitItems[traitIndex];

            if (item.IsNoTrait)
            {
                selectedTrait = ChoiceLetter_GrowthMoment.NoTrait;
                TolkHelper.Speak($"No trait selected");
            }
            else
            {
                selectedTrait = item.TraitOption;
                TolkHelper.Speak($"{item.Label} selected");
            }
        }

        // ========== Confirm / Postpone ==========

        private static void ConfirmChoices()
        {
            if (isArchiveView)
                return;

            // Validate passions
            if (!letter.passionChoices.NullOrEmpty() && selectedPassions.Count != letter.passionGainsCount)
            {
                if (letter.passionGainsCount == 1)
                    TolkHelper.Speak("SelectPassionSingular".Translate(), SpeechPriority.High);
                else
                    TolkHelper.Speak("SelectPassionsPlural".Translate(letter.passionGainsCount), SpeechPriority.High);
                return;
            }

            // Validate traits
            if (!letter.traitChoices.NullOrEmpty() && selectedTrait == null)
            {
                TolkHelper.Speak("SelectATrait".Translate(), SpeechPriority.High);
                return;
            }

            // Save references before closing — TryRemove triggers our PostClose patch
            // which calls Close() and nulls out letter/dialog
            string pawnName = letter.pawn?.LabelShort ?? "pawn";
            var letterRef = letter;
            var dialogRef = dialog;

            // Apply choices
            letterRef.MakeChoices(selectedPassions, selectedTrait);

            // Close our state first so PostClose patch won't double-close
            Close();

            // Now close the dialog window and remove the letter
            if (dialogRef != null)
                Find.WindowStack.TryRemove(dialogRef, doCloseSound: false);
            Find.LetterStack.RemoveLetter(letterRef);

            TolkHelper.Speak($"Growth choices confirmed for {pawnName}");
        }

        private static void PostponeChoices()
        {
            if (!HasChoicesToMake())
            {
                ConfirmChoices();
                return;
            }

            if (letter.ShouldAutomaticallyOpenLetter)
            {
                TolkHelper.Speak("MessageCannotPostponeGrowthMoment".Translate(letter.pawn.Named("PAWN")), SpeechPriority.High);
                return;
            }

            CloseDialog();
        }

        private static void CloseDialog()
        {
            var dialogRef = dialog;

            // Close our state first so PostClose patch won't double-close
            Close();

            if (dialogRef != null)
                Find.WindowStack.TryRemove(dialogRef, doCloseSound: false);

            TolkHelper.Speak("Growth moment postponed");
        }

        // ========== Announcements ==========

        private static void AnnounceOpening()
        {
            if (letter == null)
                return;

            string pawnName = letter.pawn?.LabelShort ?? "pawn";
            int age = letter.pawn?.ageTracker?.AgeBiologicalYears ?? 0;

            var parts = new List<string>();

            // Read all info lines
            if (infoLines != null)
            {
                foreach (string line in infoLines)
                {
                    parts.Add(line);
                }
            }

            // Navigation instructions
            if (!isArchiveView)
            {
                if (!HasChoicesToMake())
                {
                    parts.Add("Press Enter to confirm");
                }
                else
                {
                    string tabCount = $"{availableTabs.Count} tabs";

                    if (!letter.passionChoices.NullOrEmpty() && letter.passionGainsCount > 0)
                    {
                        parts.Add($"Choose {letter.passionGainsCount} of {passionItems.Count} passions and 1 trait. {tabCount}. Tab to switch pages, Alt+S to confirm");
                    }
                    else
                    {
                        parts.Add($"Choose 1 trait. {tabCount}. Tab to switch pages, Alt+S to confirm");
                    }
                }
            }

            TolkHelper.Speak(string.Join(". ", parts));
        }

        private static void AnnounceCurrentItem()
        {
            Tab tab = availableTabs[currentTabIndex];

            switch (tab)
            {
                case Tab.Info:
                    AnnounceInfoItem();
                    break;
                case Tab.Passions:
                    AnnouncePassionItem();
                    break;
                case Tab.Traits:
                    AnnounceTraitItem();
                    break;
            }
        }

        private static void AnnounceInfoItem()
        {
            if (infoLines == null || infoLines.Length == 0)
            {
                TolkHelper.Speak("No information available");
                return;
            }

            if (infoIndex < 0 || infoIndex >= infoLines.Length)
                return;

            string line = infoLines[infoIndex];
            string position = MenuHelper.FormatPosition(infoIndex, infoLines.Length);
            string positionSuffix = string.IsNullOrEmpty(position) ? "" : $". {position}";

            TolkHelper.Speak($"{line}{positionSuffix}");
        }

        private static void AnnouncePassionItem()
        {
            if (passionItems.Count == 0)
            {
                TolkHelper.Speak("No passions available");
                return;
            }

            if (passionIndex < 0 || passionIndex >= passionItems.Count)
                return;

            PassionItem item = passionItems[passionIndex];
            bool isSelected = selectedPassions.Contains(item.Skill);
            string selectedText = isSelected ? "Selected" : "Not selected";
            string currentName = GetPassionName(item.CurrentPassion);
            string newName = GetPassionName(item.NewPassion);
            string position = MenuHelper.FormatPosition(passionIndex, passionItems.Count);
            string positionSuffix = string.IsNullOrEmpty(position) ? "" : $". {position}";

            TolkHelper.Speak($"{item.Label}, {currentName} to {newName}. {selectedText}{positionSuffix}");
        }

        private static void AnnounceTraitItem()
        {
            if (traitItems.Count == 0)
            {
                TolkHelper.Speak("No traits available");
                return;
            }

            if (traitIndex < 0 || traitIndex >= traitItems.Count)
                return;

            TraitItem item = traitItems[traitIndex];
            bool isSelected;
            if (item.IsNoTrait)
                isSelected = selectedTrait == ChoiceLetter_GrowthMoment.NoTrait;
            else
                isSelected = selectedTrait == item.TraitOption;

            string selectedText = isSelected ? "Selected" : "Not selected";
            string position = MenuHelper.FormatPosition(traitIndex, traitItems.Count);
            string positionSuffix = string.IsNullOrEmpty(position) ? "" : $". {position}";

            if (!string.IsNullOrEmpty(item.Description))
                TolkHelper.Speak($"{item.Label}. {item.Description}. {selectedText}{positionSuffix}");
            else
                TolkHelper.Speak($"{item.Label}. {selectedText}{positionSuffix}");
        }

        private static void AnnounceWithSearch()
        {
            Tab tab = availableTabs[currentTabIndex];
            TypeaheadSearchHelper typeahead = GetCurrentTypeahead();

            // Get base announcement
            AnnounceCurrentItem();

            // Append search context if we have it - but since AnnounceCurrentItem already spoke,
            // we need to combine them. For now, just announce the current item.
            // The typeahead match feedback is handled by the search helper itself.
        }

        // ========== Data Building ==========

        private static void BuildInfoLines()
        {
            var lines = new List<string>();

            if (letter.text != null)
            {
                string cleanText = StripTags(letter.text.Resolve());
                string[] splitLines = cleanText.Split('\n');
                foreach (string line in splitLines)
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        lines.Add(trimmed);
                }
            }

            // Add growth tier info
            if (letter.growthTier >= 0)
            {
                lines.Add($"Growth tier: {letter.growthTier}");
            }

            // Add nickname change info
            if (letter.pawn?.Name != null && letter.oldName != null && letter.pawn.Name != letter.oldName)
            {
                string oldFull = letter.oldName.ToStringFull;
                string newShort = letter.pawn.LabelShort;
                lines.Add($"Nickname changed from {StripTags(oldFull)} to {StripTags(newShort)}");
            }

            // Archive view: add chosen passion/trait info
            if (isArchiveView)
            {
                if (!letter.chosenPassions.NullOrEmpty())
                {
                    string passionList = string.Join(", ", letter.chosenPassions.Select(s => s.label));
                    lines.Add($"Chosen passions: {passionList}");
                }

                if (letter.chosenTrait != null)
                {
                    string traitLabel = letter.chosenTrait == ChoiceLetter_GrowthMoment.NoTrait
                        ? "No trait"
                        : letter.chosenTrait.LabelCap;
                    lines.Add($"Chosen trait: {traitLabel}");
                }
            }

            infoLines = lines.ToArray();
        }

        private static void BuildPassionItems()
        {
            passionItems.Clear();

            if (isArchiveView || letter.passionChoices.NullOrEmpty() || letter.passionGainsCount <= 0)
                return;

            foreach (SkillDef skillDef in letter.passionChoices)
            {
                SkillRecord skill = letter.pawn?.skills?.GetSkill(skillDef);
                if (skill == null) continue;

                passionItems.Add(new PassionItem
                {
                    Skill = skillDef,
                    CurrentPassion = skill.passion,
                    NewPassion = skill.passion.IncrementPassion(),
                    SkillLevel = skill.Level,
                    Label = skillDef.LabelCap
                });
            }
        }

        private static void BuildTraitItems()
        {
            traitItems.Clear();

            if (isArchiveView || letter.traitChoices.NullOrEmpty())
                return;

            foreach (Trait trait in letter.traitChoices)
            {
                string description = "";
                try
                {
                    description = StripTags(trait.TipString(letter.pawn));
                }
                catch
                {
                    // TipString can throw if pawn state is unexpected
                }

                traitItems.Add(new TraitItem
                {
                    TraitOption = trait,
                    Label = trait.LabelCap,
                    Description = description,
                    IsNoTrait = false
                });
            }

            // Add "No trait" option if applicable
            if (letter.noTraitOptionShown)
            {
                string noTraitLabel = "BirthdayNoTraitChoice".Translate();
                string noTraitDesc = "";
                try
                {
                    noTraitDesc = StripTags("BirthdayNoTraitChoiceTooltip".Translate(letter.pawn));
                }
                catch { }

                traitItems.Add(new TraitItem
                {
                    TraitOption = ChoiceLetter_GrowthMoment.NoTrait,
                    Label = noTraitLabel,
                    Description = noTraitDesc,
                    IsNoTrait = true
                });
            }
        }

        private static void BuildAvailableTabs()
        {
            availableTabs.Clear();
            availableTabs.Add(Tab.Info);

            if (!isArchiveView)
            {
                if (passionItems.Count > 0)
                    availableTabs.Add(Tab.Passions);

                if (traitItems.Count > 0)
                    availableTabs.Add(Tab.Traits);
            }
        }

        // ========== Helpers ==========

        private static Tab CurrentTab => availableTabs.Count > 0 ? availableTabs[currentTabIndex] : Tab.Info;

        private static string GetTabName(Tab tab)
        {
            switch (tab)
            {
                case Tab.Info: return "Info";
                case Tab.Passions: return "Passions";
                case Tab.Traits: return "Traits";
                default: return "Unknown";
            }
        }

        private static string GetPassionName(Passion passion)
        {
            switch (passion)
            {
                case Passion.None: return "None";
                case Passion.Minor: return "Minor";
                case Passion.Major: return "Major";
                default: return passion.ToString();
            }
        }

        private static TypeaheadSearchHelper GetCurrentTypeahead()
        {
            Tab tab = availableTabs[currentTabIndex];
            switch (tab)
            {
                case Tab.Passions: return passionTypeahead;
                case Tab.Traits: return traitTypeahead;
                default: return null;
            }
        }

        private static List<string> GetCurrentLabels()
        {
            Tab tab = availableTabs[currentTabIndex];
            switch (tab)
            {
                case Tab.Passions:
                    return passionItems.Select(p => p.Label).ToList();
                case Tab.Traits:
                    return traitItems.Select(t => t.Label).ToList();
                default:
                    return new List<string>();
            }
        }

        private static void SetCurrentIndex(int index)
        {
            Tab tab = availableTabs[currentTabIndex];
            switch (tab)
            {
                case Tab.Passions:
                    if (index >= 0 && index < passionItems.Count)
                        passionIndex = index;
                    break;
                case Tab.Traits:
                    if (index >= 0 && index < traitItems.Count)
                        traitIndex = index;
                    break;
            }
        }

        private static string StripTags(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return Regex.Replace(text, @"</?[a-zA-Z][^>]*>", "");
        }
    }
}
