using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages navigation state for the faction configuration section of Page_CreateWorldParams.
    /// Handles both the faction list and the "Add Faction" overlay menu.
    /// Uses modern patterns: MenuHelper for navigation, TypeaheadSearchHelper for search.
    /// </summary>
    public static class FactionsNavigationState
    {
        // ===== STATE =====
        public static bool IsActive { get; private set; }
        public static bool IsAddMenuOpen { get; private set; }

        private static Page_CreateWorldParams currentInstance;
        private static int selectedIndex = 0;

        // Add menu overlay state
        private static List<AddMenuOption> addMenuOptions = new List<AddMenuOption>();
        private static int addMenuIndex = 0;

        // Typeahead search helpers
        private static TypeaheadSearchHelper factionTypeahead = new TypeaheadSearchHelper();
        private static TypeaheadSearchHelper addMenuTypeahead = new TypeaheadSearchHelper();

        // ===== PUBLIC PROPERTIES =====
        public static bool HasActiveTypeahead => factionTypeahead.HasActiveSearch || addMenuTypeahead.HasActiveSearch;
        public static bool HasFactionListTypeahead => factionTypeahead.HasActiveSearch;
        public static bool HasAddMenuTypeahead => addMenuTypeahead.HasActiveSearch;

        // ===== ADD MENU OPTION CLASS =====
        private class AddMenuOption
        {
            public FactionDef Faction { get; set; }
            public string Label { get; set; }
            public bool IsDisabled { get; set; }
            public string DisabledReason { get; set; }
        }

        // ===== LIFECYCLE =====

        public static void Initialize(Page_CreateWorldParams instance)
        {
            currentInstance = instance;
        }

        public static void Reset()
        {
            IsActive = false;
            IsAddMenuOpen = false;
            currentInstance = null;
            selectedIndex = 0;
            addMenuIndex = 0;
            addMenuOptions.Clear();
            factionTypeahead.ClearSearch();
            addMenuTypeahead.ClearSearch();
        }

        /// <summary>
        /// Called when Tab switches to factions section.
        /// </summary>
        public static void Activate()
        {
            IsActive = true;
            // Don't reset selectedIndex - remember where we were
            factionTypeahead.ClearSearch();

            var factions = GetVisibleFactions();

            // Validate selectedIndex is still in bounds
            if (selectedIndex >= factions.Count)
            {
                selectedIndex = Math.Max(0, factions.Count - 1);
            }

            if (factions.Count > 0)
            {
                // Announce section name, then current faction, then Alt+A hint
                TolkHelper.Speak("Factions");
                AnnounceCurrentFaction();
                TolkHelper.Speak("Press Alt+A to add factions.", SpeechPriority.Low);
            }
            else
            {
                TolkHelper.Speak("Factions. No factions in list. Press Alt+A to add factions.");
            }

            // Announce warnings if any
            AnnounceWarnings();
        }

        /// <summary>
        /// Called when switching away from factions section.
        /// </summary>
        public static void Deactivate()
        {
            IsActive = false;
            IsAddMenuOpen = false;
            factionTypeahead.ClearSearch();
            addMenuTypeahead.ClearSearch();
        }

        // ===== FACTION LIST NAVIGATION =====

        public static void NavigateUp()
        {
            var factions = GetVisibleFactions();
            if (factions.Count == 0) return;

            factionTypeahead.ClearSearch();
            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, factions.Count);
            AnnounceCurrentFaction();
        }

        public static void NavigateDown()
        {
            var factions = GetVisibleFactions();
            if (factions.Count == 0) return;

            factionTypeahead.ClearSearch();
            selectedIndex = MenuHelper.SelectNext(selectedIndex, factions.Count);
            AnnounceCurrentFaction();
        }

        public static void NavigateHome()
        {
            var factions = GetVisibleFactions();
            if (factions.Count == 0) return;

            factionTypeahead.ClearSearch();
            selectedIndex = 0;
            AnnounceCurrentFaction();
        }

        public static void NavigateEnd()
        {
            var factions = GetVisibleFactions();
            if (factions.Count == 0) return;

            factionTypeahead.ClearSearch();
            selectedIndex = factions.Count - 1;
            AnnounceCurrentFaction();
        }

        // ===== FACTION LIST ACTIONS =====

        public static void DeleteSelectedFaction()
        {
            var factions = GetFactionsList();
            var visibleFactions = GetVisibleFactions();

            if (visibleFactions.Count == 0 || selectedIndex < 0 || selectedIndex >= visibleFactions.Count)
            {
                TolkHelper.Speak("No faction selected");
                return;
            }

            FactionDef selectedFaction = visibleFactions[selectedIndex];

            // Check if locked by scenario
            if (IsFactionLocked(selectedFaction))
            {
                TolkHelper.Speak($"Cannot remove {selectedFaction.LabelCap}: locked by scenario");
                return;
            }

            // Check tutorial
            if (!TutorSystem.AllowAction("ConfiguringWorldFactions"))
            {
                TolkHelper.Speak("Cannot modify factions in tutorial mode");
                return;
            }

            // Find and remove from the actual list
            int actualIndex = factions.IndexOf(selectedFaction);
            if (actualIndex >= 0)
            {
                factions.RemoveAt(actualIndex);

                // Adjust selection
                visibleFactions = GetVisibleFactions();
                if (selectedIndex >= visibleFactions.Count)
                {
                    selectedIndex = Math.Max(0, visibleFactions.Count - 1);
                }

                TolkHelper.Speak($"{selectedFaction.LabelCap} removed. {visibleFactions.Count} factions remaining.");

                if (visibleFactions.Count > 0)
                {
                    AnnounceCurrentFaction();
                }
                else
                {
                    TolkHelper.Speak("No factions remaining. Press Alt+A to add factions.");
                }

                // Check for warnings after removal
                AnnounceWarnings();
            }
        }

        // ===== ADD MENU =====

        public static void OpenAddMenu()
        {
            // Check tutorial
            if (!TutorSystem.AllowAction("ConfiguringWorldFactions"))
            {
                TolkHelper.Speak("Cannot modify factions in tutorial mode");
                return;
            }

            RefreshAddMenuOptions();

            if (addMenuOptions.Count == 0)
            {
                TolkHelper.Speak("No factions available to add");
                return;
            }

            IsAddMenuOpen = true;
            addMenuIndex = 0;
            addMenuTypeahead.ClearSearch();

            TolkHelper.Speak("Add faction menu opened");
            AnnounceAddMenuOption();
        }

        public static void CloseAddMenu()
        {
            IsAddMenuOpen = false;
            addMenuTypeahead.ClearSearch();
            TolkHelper.Speak("Add menu closed");

            // Re-announce current faction
            var visibleFactions = GetVisibleFactions();
            if (visibleFactions.Count > 0)
            {
                AnnounceCurrentFaction();
            }
        }

        public static void AddMenuNavigateUp()
        {
            if (addMenuOptions.Count == 0) return;

            addMenuTypeahead.ClearSearch();
            addMenuIndex = MenuHelper.SelectPrevious(addMenuIndex, addMenuOptions.Count);
            AnnounceAddMenuOption();
        }

        public static void AddMenuNavigateDown()
        {
            if (addMenuOptions.Count == 0) return;

            addMenuTypeahead.ClearSearch();
            addMenuIndex = MenuHelper.SelectNext(addMenuIndex, addMenuOptions.Count);
            AnnounceAddMenuOption();
        }

        public static void AddMenuNavigateHome()
        {
            if (addMenuOptions.Count == 0) return;

            addMenuTypeahead.ClearSearch();
            addMenuIndex = 0;
            AnnounceAddMenuOption();
        }

        public static void AddMenuNavigateEnd()
        {
            if (addMenuOptions.Count == 0) return;

            addMenuTypeahead.ClearSearch();
            addMenuIndex = addMenuOptions.Count - 1;
            AnnounceAddMenuOption();
        }

        public static void AddMenuConfirm()
        {
            if (addMenuOptions.Count == 0 || addMenuIndex < 0 || addMenuIndex >= addMenuOptions.Count)
            {
                TolkHelper.Speak("No faction selected");
                return;
            }

            AddMenuOption option = addMenuOptions[addMenuIndex];

            if (option.IsDisabled)
            {
                TolkHelper.Speak($"Cannot add {option.Faction.LabelCap}: {option.DisabledReason}");
                return;
            }

            // Add the faction
            var factions = GetFactionsList();
            factions.Add(option.Faction);

            int newCount = factions.Count(f => f == option.Faction);
            TolkHelper.Speak($"{option.Faction.LabelCap} added. Now {newCount} in list.");

            // Refresh the menu options (counts may have changed, some may now be disabled)
            RefreshAddMenuOptions();

            // Stay in add menu so user can add more factions
            if (addMenuIndex >= addMenuOptions.Count)
            {
                addMenuIndex = Math.Max(0, addMenuOptions.Count - 1);
            }

            if (addMenuOptions.Count > 0)
            {
                AnnounceAddMenuOption();
            }
        }

        // ===== TYPEAHEAD - FACTION LIST =====

        public static bool HandleFactionTypeahead(char character)
        {
            var visibleFactions = GetVisibleFactions();
            if (visibleFactions.Count == 0) return false;

            var labels = visibleFactions.Select(f => f.LabelCap.ToString()).ToList();
            if (factionTypeahead.ProcessCharacterInput(character, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    AnnounceFactionWithSearch();
                }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{factionTypeahead.LastFailedSearch}'");
            }
            return true;
        }

        public static bool HandleFactionTypeaheadBackspace()
        {
            if (!factionTypeahead.HasActiveSearch) return false;

            var visibleFactions = GetVisibleFactions();
            var labels = visibleFactions.Select(f => f.LabelCap.ToString()).ToList();
            if (factionTypeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    AnnounceFactionWithSearch();
                }
            }
            return true;
        }

        public static bool ClearFactionTypeahead()
        {
            if (factionTypeahead.ClearSearchAndAnnounce())
            {
                AnnounceCurrentFaction();
                return true;
            }
            return false;
        }

        public static bool SelectNextFactionMatch()
        {
            if (!factionTypeahead.HasActiveSearch) return false;
            int next = factionTypeahead.GetNextMatch(selectedIndex);
            if (next >= 0)
            {
                selectedIndex = next;
                AnnounceFactionWithSearch();
            }
            return true;
        }

        public static bool SelectPreviousFactionMatch()
        {
            if (!factionTypeahead.HasActiveSearch) return false;
            int prev = factionTypeahead.GetPreviousMatch(selectedIndex);
            if (prev >= 0)
            {
                selectedIndex = prev;
                AnnounceFactionWithSearch();
            }
            return true;
        }

        // ===== TYPEAHEAD - ADD MENU =====

        public static bool HandleAddMenuTypeahead(char character)
        {
            if (addMenuOptions.Count == 0) return false;

            var labels = addMenuOptions.Select(o => o.Faction.LabelCap.ToString()).ToList();
            if (addMenuTypeahead.ProcessCharacterInput(character, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    addMenuIndex = newIndex;
                    AnnounceAddMenuWithSearch();
                }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{addMenuTypeahead.LastFailedSearch}'");
            }
            return true;
        }

        public static bool HandleAddMenuTypeaheadBackspace()
        {
            if (!addMenuTypeahead.HasActiveSearch) return false;

            var labels = addMenuOptions.Select(o => o.Faction.LabelCap.ToString()).ToList();
            if (addMenuTypeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    addMenuIndex = newIndex;
                    AnnounceAddMenuWithSearch();
                }
            }
            return true;
        }

        public static bool ClearAddMenuTypeahead()
        {
            if (addMenuTypeahead.ClearSearchAndAnnounce())
            {
                AnnounceAddMenuOption();
                return true;
            }
            return false;
        }

        public static bool SelectNextAddMenuMatch()
        {
            if (!addMenuTypeahead.HasActiveSearch) return false;
            int next = addMenuTypeahead.GetNextMatch(addMenuIndex);
            if (next >= 0)
            {
                addMenuIndex = next;
                AnnounceAddMenuWithSearch();
            }
            return true;
        }

        public static bool SelectPreviousAddMenuMatch()
        {
            if (!addMenuTypeahead.HasActiveSearch) return false;
            int prev = addMenuTypeahead.GetPreviousMatch(addMenuIndex);
            if (prev >= 0)
            {
                addMenuIndex = prev;
                AnnounceAddMenuWithSearch();
            }
            return true;
        }

        // ===== HELPERS =====

        private static List<FactionDef> GetFactionsList()
        {
            if (currentInstance == null) return new List<FactionDef>();
            return (List<FactionDef>)AccessTools.Field(typeof(Page_CreateWorldParams), "factions").GetValue(currentInstance);
        }

        private static List<FactionDef> GetVisibleFactions()
        {
            return GetFactionsList().Where(f => f.displayInFactionSelection).ToList();
        }

        private static bool IsFactionLocked(FactionDef faction)
        {
            // Check scenario parts for preventRemovalOfFaction
            // During world creation, Current.Game.Scenario should be set
            Scenario scenario = Current.Game?.Scenario;
            if (scenario == null) return false;

            // Use AllParts (public property) to iterate scenario parts
            foreach (ScenPart part in scenario.AllParts)
            {
                // Check if this part's def prevents removal of this faction
                if (part.def.preventRemovalOfFaction == faction)
                {
                    return true;
                }
            }
            return false;
        }

        private static AcceptanceReport CanAddFaction(FactionDef f)
        {
            var factions = GetFactionsList();

            // Check total non-hidden limit (12)
            if (!f.hidden && factions.Count(x => !x.hidden) >= 12)
            {
                return "Maximum 12 factions allowed";
            }

            // Check per-faction limit
            if (f.maxConfigurableAtWorldCreation > 0 && factions.Count(x => x == f) >= f.maxConfigurableAtWorldCreation)
            {
                return $"Maximum {f.maxConfigurableAtWorldCreation} of this faction type";
            }

            return true;
        }

        private static void RefreshAddMenuOptions()
        {
            addMenuOptions.Clear();
            var currentFactions = GetFactionsList();

            foreach (FactionDef def in FactionGenerator.ConfigurableFactions)
            {
                if (!def.displayInFactionSelection) continue;

                var option = new AddMenuOption { Faction = def };
                int count = currentFactions.Count(x => x == def);

                AcceptanceReport canAdd = CanAddFaction(def);

                if (!canAdd)
                {
                    option.IsDisabled = true;
                    option.DisabledReason = canAdd.Reason;
                    option.Label = $"{def.LabelCap} ({canAdd.Reason})";
                }
                else if (count > 0)
                {
                    option.Label = $"{def.LabelCap} ({count})";
                }
                else
                {
                    option.Label = def.LabelCap.ToString();
                }

                addMenuOptions.Add(option);
            }
        }

        private static List<string> GetCurrentWarnings()
        {
            var warnings = new List<string>();
            var factions = GetFactionsList();
            int visibleCount = factions.Count(x => !x.hidden);

            if (visibleCount == 0)
            {
                warnings.Add("Warning: Without factions, raids, trading, and quests won't occur as normal.");
                return warnings;
            }

            // Empire warning (Royalty)
            if (ModsConfig.RoyaltyActive)
            {
                bool hasEmpire = factions.Any(f => f.defName == "Empire");
                if (!hasEmpire)
                {
                    warnings.Add("Warning: Missing Empire faction may reduce Royalty quest content.");
                }
            }

            // Mechanoid warning
            bool hasMechanoid = factions.Any(f => f.defName == "Mechanoid");
            if (!hasMechanoid)
            {
                warnings.Add("Warning: Missing Mechanoids may reduce quest variety.");
            }

            // Insect warning
            bool hasInsect = factions.Any(f => f.defName == "Insect");
            if (!hasInsect)
            {
                warnings.Add("Warning: Missing Insects may reduce quest variety.");
            }

            return warnings;
        }

        // ===== ANNOUNCEMENTS =====

        private static void AnnounceCurrentFaction()
        {
            var visibleFactions = GetVisibleFactions();
            if (selectedIndex < 0 || selectedIndex >= visibleFactions.Count) return;

            FactionDef faction = visibleFactions[selectedIndex];
            string position = MenuHelper.FormatPosition(selectedIndex, visibleFactions.Count);

            string text = faction.LabelCap.ToString();

            // Include description (use Description property to get xenotype info if Biotech active)
            if (!string.IsNullOrEmpty(faction.Description))
            {
                text += $". {faction.Description.StripTags()}";
            }

            if (!string.IsNullOrEmpty(position))
            {
                text += $" {position}";
            }

            // Note if locked
            if (IsFactionLocked(faction))
            {
                text += " (locked by scenario)";
            }

            TolkHelper.Speak(text);
        }

        private static void AnnounceFactionWithSearch()
        {
            var visibleFactions = GetVisibleFactions();
            if (selectedIndex < 0 || selectedIndex >= visibleFactions.Count) return;

            FactionDef faction = visibleFactions[selectedIndex];

            if (factionTypeahead.HasActiveSearch)
            {
                TolkHelper.Speak($"{faction.LabelCap}, {factionTypeahead.CurrentMatchPosition} of {factionTypeahead.MatchCount} matches for '{factionTypeahead.SearchBuffer}'");
            }
            else
            {
                AnnounceCurrentFaction();
            }
        }

        private static void AnnounceAddMenuOption()
        {
            if (addMenuIndex < 0 || addMenuIndex >= addMenuOptions.Count) return;

            AddMenuOption option = addMenuOptions[addMenuIndex];
            string position = MenuHelper.FormatPosition(addMenuIndex, addMenuOptions.Count);

            string text = option.Label;

            // Include description (use Description property to get xenotype info if Biotech active)
            if (!string.IsNullOrEmpty(option.Faction.Description))
            {
                text += $". {option.Faction.Description.StripTags()}";
            }

            if (!string.IsNullOrEmpty(position))
            {
                text += $" {position}";
            }

            if (option.IsDisabled)
            {
                text += " (disabled)";
            }

            TolkHelper.Speak(text);
        }

        private static void AnnounceAddMenuWithSearch()
        {
            if (addMenuIndex < 0 || addMenuIndex >= addMenuOptions.Count) return;

            AddMenuOption option = addMenuOptions[addMenuIndex];

            if (addMenuTypeahead.HasActiveSearch)
            {
                string status = option.IsDisabled ? " (disabled)" : "";
                TolkHelper.Speak($"{option.Faction.LabelCap}{status}, {addMenuTypeahead.CurrentMatchPosition} of {addMenuTypeahead.MatchCount} matches for '{addMenuTypeahead.SearchBuffer}'");
            }
            else
            {
                AnnounceAddMenuOption();
            }
        }

        private static void AnnounceWarnings()
        {
            var warnings = GetCurrentWarnings();
            if (warnings.Count > 0)
            {
                // Announce first warning with a slight delay to not overlap with faction announcement
                string warningText = string.Join(" ", warnings);
                TolkHelper.Speak(warningText, SpeechPriority.Low);
            }
        }
    }
}
