using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages state and keyboard navigation for the prisoner/slave management tab.
    /// Supports navigating through information, medical care, interaction modes, and ideology selection.
    /// </summary>
    public static class PrisonerTabState
    {
        private static bool isActive = false;
        private static Pawn currentPawn = null;
        private static TabSection currentSection = TabSection.Information;
        private static int selectedIndex = 0;

        // Cached lists for current pawn
        private static List<string> infoLines = new List<string>();
        private static List<PrisonerInteractionModeDef> exclusiveModes = new List<PrisonerInteractionModeDef>();
        private static List<PrisonerInteractionModeDef> nonExclusiveModes = new List<PrisonerInteractionModeDef>();
        private static List<SlaveInteractionModeDef> slaveModes = new List<SlaveInteractionModeDef>();

        // Typeahead search across every tabbable section (see HandleTypeahead). flatLocations maps
        // each searchable row (in the same order as the labels handed to the helper) back to its
        // (section, index) so match cycling can switch sections to land on a hit.
        private static readonly TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
        private static readonly List<KeyValuePair<TabSection, int>> flatLocations = new List<KeyValuePair<TabSection, int>>();

        public enum TabSection
        {
            Information,      // Read-only prisoner stats
            MedicalCare,      // Medical care level selection
            ExclusiveModes,   // Prisoner interaction modes (radio)
            NonExclusiveModes,// Non-exclusive modes (checkboxes)
            IdeologySelection // Ideology selection for Convert mode
        }

        public static bool IsActive => isActive;
        public static Pawn CurrentPawn => currentPawn;
        public static TabSection CurrentSection => currentSection;
        public static int SelectedIndex => selectedIndex;

        /// <summary>
        /// Opens the prisoner tab for the specified pawn.
        /// </summary>
        public static void Open(Pawn pawn)
        {
            if (pawn == null)
                return;

            if (!pawn.IsPrisonerOfColony && !pawn.IsSlaveOfColony)
            {
                TolkHelper.Speak($"{pawn.LabelShort} is not a prisoner or slave");
                return;
            }

            isActive = true;
            currentPawn = pawn;
            currentSection = TabSection.Information;
            selectedIndex = 0;

            RefreshTabData();
        }

        /// <summary>
        /// Closes the prisoner tab.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentPawn = null;
            currentSection = TabSection.Information;
            selectedIndex = 0;
            ClearCachedData();
            typeahead.ClearSearch();

            TolkHelper.Speak("Prisoner tab closed");
        }

        /// <summary>
        /// Refreshes all tab data for the current pawn.
        /// </summary>
        private static void RefreshTabData()
        {
            if (currentPawn == null)
                return;

            ClearCachedData();

            if (currentPawn.IsPrisonerOfColony)
            {
                // Build info lines
                string prisonerInfo = PrisonerTabHelper.GetPrisonerInfo(currentPawn);
                infoLines.AddRange(prisonerInfo.Split('\n'));

                // Load interaction modes
                exclusiveModes = PrisonerTabHelper.GetAvailableExclusiveInteractionModes(currentPawn);
                nonExclusiveModes = PrisonerTabHelper.GetAvailableNonExclusiveInteractionModes(currentPawn);

                // Announce pawn and first section
                AnnouncePrisonerOpened();
            }
            else if (currentPawn.IsSlaveOfColony)
            {
                // Build info lines
                string slaveInfo = PrisonerTabHelper.GetSlaveInfo(currentPawn);
                infoLines.AddRange(slaveInfo.Split('\n'));

                // Load slave modes
                slaveModes = PrisonerTabHelper.GetAvailableSlaveInteractionModes();

                // Announce pawn and first section
                AnnounceSlaveOpened();
            }
        }

        /// <summary>
        /// Navigates to the next section (Right arrow key).
        /// </summary>
        public static void NextSection()
        {
            if (!isActive || currentPawn == null)
                return;

            // Skip sections that don't apply
            do
            {
                currentSection = (TabSection)(((int)currentSection + 1) % GetSectionCount());
            }
            while (!IsSectionAvailable(currentSection));

            selectedIndex = 0;
            AnnounceCurrentSection();
        }

        /// <summary>
        /// Navigates to the previous section (Left arrow key).
        /// </summary>
        public static void PreviousSection()
        {
            if (!isActive || currentPawn == null)
                return;

            // Skip sections that don't apply
            do
            {
                int sectionCount = GetSectionCount();
                currentSection = (TabSection)(((int)currentSection - 1 + sectionCount) % sectionCount);
            }
            while (!IsSectionAvailable(currentSection));

            selectedIndex = 0;
            AnnounceCurrentSection();
        }

        /// <summary>
        /// Navigates down within the current section (Down arrow).
        /// For medical care section, increases the care level.
        /// </summary>
        public static void NavigateDown()
        {
            if (!isActive || currentPawn == null)
                return;

            // While a typeahead search is active, arrows cycle through matches (universal pattern).
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                MoveToFlatMatch(typeahead.GetNextMatch(CurrentFlatIndex()));
                return;
            }

            // Special handling for medical care - use up/down to adjust level
            if (currentSection == TabSection.MedicalCare)
            {
                AdjustMedicalCare(1);
                return;
            }

            int maxIndex = GetMaxIndexForCurrentSection();
            if (maxIndex <= 0)
                return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, maxIndex + 1);
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Navigates up within the current section (Up arrow).
        /// For medical care section, decreases the care level.
        /// </summary>
        public static void NavigateUp()
        {
            if (!isActive || currentPawn == null)
                return;

            // While a typeahead search is active, arrows cycle through matches (universal pattern).
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                MoveToFlatMatch(typeahead.GetPreviousMatch(CurrentFlatIndex()));
                return;
            }

            // Special handling for medical care - use up/down to adjust level
            if (currentSection == TabSection.MedicalCare)
            {
                AdjustMedicalCare(-1);
                return;
            }

            int maxIndex = GetMaxIndexForCurrentSection();
            if (maxIndex <= 0)
                return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, maxIndex + 1);
            AnnounceCurrentSelection();
        }


        /// <summary>
        /// Jumps to the first item in the current section (Home key).
        /// No-op for MedicalCare (single adjustable value).
        /// </summary>
        public static void NavigateToStart()
        {
            if (!isActive || currentPawn == null)
                return;

            // While a typeahead search is active, Home jumps to the first match (universal pattern).
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                MoveToFlatMatch(typeahead.GetFirstMatch());
                return;
            }

            if (currentSection == TabSection.MedicalCare)
                return;

            int maxIndex = GetMaxIndexForCurrentSection();
            if (maxIndex < 0)
                return;

            selectedIndex = 0;
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Jumps to the last item in the current section (End key).
        /// No-op for MedicalCare (single adjustable value).
        /// </summary>
        public static void NavigateToEnd()
        {
            if (!isActive || currentPawn == null)
                return;

            // While a typeahead search is active, End jumps to the last match (universal pattern).
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                MoveToFlatMatch(typeahead.GetLastMatch());
                return;
            }

            if (currentSection == TabSection.MedicalCare)
                return;

            int maxIndex = GetMaxIndexForCurrentSection();
            if (maxIndex < 0)
                return;

            selectedIndex = maxIndex;
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Executes the selected action (Enter key).
        /// </summary>
        public static void ExecuteAction()
        {
            if (!isActive || currentPawn == null)
                return;

            switch (currentSection)
            {
                case TabSection.Information:
                    // Read-only, just re-announce
                    AnnounceCurrentSelection();
                    break;

                case TabSection.MedicalCare:
                    // Already adjusted with arrow keys, just re-announce
                    AnnounceCurrentSelection();
                    break;

                case TabSection.ExclusiveModes:
                    SelectExclusiveMode();
                    break;

                case TabSection.NonExclusiveModes:
                    ToggleNonExclusiveMode();
                    break;

                case TabSection.IdeologySelection:
                    SelectIdeology();
                    break;
            }
        }

        /// <summary>
        /// Handles Escape within the tab. Returns true if consumed internally
        /// (e.g. backing out of the ideology picker), false if the caller should close the tab.
        /// </summary>
        public static bool HandleEscape()
        {
            if (!isActive || currentPawn == null)
                return false;

            // Escape clears an active typeahead search first (universal pattern), before backing out.
            if (typeahead.HasActiveSearch)
            {
                typeahead.ClearSearchAndAnnounce();
                AnnounceCurrentSelection();
                return true;
            }

            if (currentSection == TabSection.IdeologySelection)
            {
                currentSection = TabSection.ExclusiveModes;
                selectedIndex = exclusiveModes.IndexOf(PrisonerInteractionModeDefOf.Convert);
                if (selectedIndex < 0) selectedIndex = 0;
                AnnounceCurrentSection();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Toggles a checkbox (Space key) - for non-exclusive modes.
        /// </summary>
        public static void ToggleCheckbox()
        {
            if (!isActive || currentPawn == null)
                return;

            if (currentSection == TabSection.NonExclusiveModes)
            {
                ToggleNonExclusiveMode();
            }
        }

        #region Action Handlers

        private static void AdjustMedicalCare(int direction)
        {
            if (currentPawn.playerSettings == null)
                return;

            MedicalCareCategory current = currentPawn.playerSettings.medCare;
            MedicalCareCategory newCare = direction > 0
                ? PrisonerTabHelper.GetNextMedicalCare(current)
                : PrisonerTabHelper.GetPreviousMedicalCare(current);

            currentPawn.playerSettings.medCare = newCare;

            string label = PrisonerTabHelper.GetMedicalCareLabel(newCare);
            TolkHelper.Speak($"{"AllowMedicine".Translate()}: {label}");
        }

        private static void SelectExclusiveMode()
        {
            if (currentPawn.IsPrisonerOfColony)
            {
                if (selectedIndex >= 0 && selectedIndex < exclusiveModes.Count)
                {
                    PrisonerInteractionModeDef mode = exclusiveModes[selectedIndex];

                    // Apply the mode first — mirrors vanilla ITab_Pawn_Visitor.DrawExclusiveInteractionRow
                    // (line 469-473) which always calls SetExclusiveInteraction before any follow-up UX.
                    currentPawn.guest.SetExclusiveInteraction(mode);

                    // Mirror vanilla InteractionModeChanged (line 507-510): auto-assign primary ideo
                    // when switching to Convert if none has been chosen yet.
                    if (mode == PrisonerInteractionModeDefOf.Convert && currentPawn.guest.ideoForConversion == null
                        && Faction.OfPlayer.ideos != null)
                    {
                        currentPawn.guest.ideoForConversion = Faction.OfPlayer.ideos.PrimaryIdeo;
                    }

                    // For Convert with multiple player ideologies, jump into the picker so the
                    // user can confirm or change the target (vanilla exposes this via the ideo icon).
                    if (mode == PrisonerInteractionModeDefOf.Convert)
                    {
                        List<Ideo> ideologies = PrisonerTabHelper.GetPlayerIdeologies();
                        if (ideologies.Count > 1)
                        {
                            currentSection = TabSection.IdeologySelection;
                            int currentIdeoIndex = ideologies.IndexOf(currentPawn.guest.ideoForConversion);
                            selectedIndex = currentIdeoIndex >= 0 ? currentIdeoIndex : 0;
                            AnnounceCurrentSection();
                            return;
                        }
                    }

                    string description = PrisonerTabHelper.GetInteractionModeDescription(currentPawn, mode);
                    if (mode == PrisonerInteractionModeDefOf.Convert && currentPawn.guest.ideoForConversion != null)
                    {
                        TolkHelper.Speak($"Selected: {mode.LabelCap}. {"IdeoConversionTarget".Translate()}: {currentPawn.guest.ideoForConversion.name}. {description}");
                    }
                    else
                    {
                        TolkHelper.Speak($"Selected: {mode.LabelCap}. {description}");
                    }
                }
            }
            else if (currentPawn.IsSlaveOfColony)
            {
                if (selectedIndex >= 0 && selectedIndex < slaveModes.Count)
                {
                    SlaveInteractionModeDef mode = slaveModes[selectedIndex];

                    // Mirror vanilla's behavior: apply the mode immediately, then prompt for
                    // confirmation on execute-vs-neutral-faction. On cancel, revert to the
                    // previous mode (ITab_Pawn_Visitor.DoSlaveTab line 159-168).
                    SlaveInteractionModeDef previousMode = currentPawn.guest.slaveInteractionMode;
                    currentPawn.guest.slaveInteractionMode = mode;

                    if (mode == SlaveInteractionModeDefOf.Execute && currentPawn.SlaveFaction != null && !currentPawn.SlaveFaction.HostileTo(Faction.OfPlayer))
                    {
                        Pawn pawnForClosure = currentPawn;
                        SlaveInteractionModeDef revertTo = previousMode;
                        string confirmationMessage = "ExectueNeutralFactionSlave".Translate(
                            pawnForClosure.Named("PAWN"),
                            pawnForClosure.SlaveFaction.Named("FACTION"));

                        // Mirror vanilla DoSlaveTab: pop the game's own Dialog_MessageBox so the
                        // confirmation uses the standard window (announced and driven by
                        // MessageBoxAccessibilityPatch) with the game's localized Confirm/Cancel buttons.
                        var dialog = new Dialog_MessageBox(
                            confirmationMessage,
                            "Confirm".Translate(),
                            () =>
                            {
                                string desc = PrisonerTabHelper.GetSlaveInteractionModeDescription(pawnForClosure, mode);
                                TolkHelper.Speak($"{mode.LabelCap}. {desc}");
                            },
                            "Cancel".Translate(),
                            () =>
                            {
                                pawnForClosure.guest.slaveInteractionMode = revertTo;
                                TolkHelper.Speak(revertTo.LabelCap);
                            });
                        Find.WindowStack.Add(dialog);
                        return;
                    }

                    string description = PrisonerTabHelper.GetSlaveInteractionModeDescription(currentPawn, mode);
                    TolkHelper.Speak($"Selected: {mode.LabelCap}. {description}");
                }
            }
        }

        private static void ToggleNonExclusiveMode()
        {
            if (!currentPawn.IsPrisonerOfColony)
                return;

            if (selectedIndex >= 0 && selectedIndex < nonExclusiveModes.Count)
            {
                PrisonerInteractionModeDef mode = nonExclusiveModes[selectedIndex];
                bool currentState = currentPawn.guest.IsInteractionEnabled(mode);
                bool newState = !currentState;

                currentPawn.guest.ToggleNonExclusiveInteraction(mode, newState);

                // Handle hemogen farm special case
                if (ModsConfig.BiotechActive && mode == PrisonerInteractionModeDefOf.HemogenFarm)
                {
                    var bill = currentPawn.BillStack?.Bills?.FirstOrDefault(b => b.recipe == RecipeDefOf.ExtractHemogenPack);
                    if (newState && bill == null && SanguophageUtility.CanSafelyBeQueuedForHemogenExtraction(currentPawn))
                    {
                        HealthCardUtility.CreateSurgeryBill(currentPawn, RecipeDefOf.ExtractHemogenPack, null);
                    }
                    else if (!newState && bill != null)
                    {
                        currentPawn.BillStack.Bills.Remove(bill);
                    }
                }

                string state = newState ? "Enabled" : "Disabled";
                TolkHelper.Speak($"{mode.LabelCap}: {state}");
            }
        }

        private static void SelectIdeology()
        {
            List<Ideo> ideologies = PrisonerTabHelper.GetPlayerIdeologies();
            if (selectedIndex >= 0 && selectedIndex < ideologies.Count)
            {
                Ideo selected = ideologies[selectedIndex];
                currentPawn.guest.ideoForConversion = selected;

                // Check for warden warning (matches vanilla ITab_Pawn_Visitor line 326-330)
                string warning = "";
                if (currentPawn.MapHeld != null)
                {
                    bool hasWarden = false;
                    foreach (Pawn colonist in currentPawn.MapHeld.mapPawns.FreeColonistsSpawned)
                    {
                        if (colonist.workSettings.WorkIsActive(WorkTypeDefOf.Warden) && colonist.Ideo == selected)
                        {
                            hasWarden = true;
                            break;
                        }
                    }
                    if (!hasWarden)
                    {
                        warning = ". " + "NoWardenOfIdeo".Translate(selected.memberName.Named("MEMBERNAME"));
                    }
                }

                // Return to exclusive modes section with Convert preselected
                currentSection = TabSection.ExclusiveModes;
                selectedIndex = exclusiveModes.IndexOf(PrisonerInteractionModeDefOf.Convert);
                if (selectedIndex < 0) selectedIndex = 0;

                // Confirm both the active mode and the chosen target in a single announcement.
                string convertLabel = PrisonerInteractionModeDefOf.Convert.LabelCap;
                TolkHelper.Speak($"Selected: {convertLabel}. {"IdeoConversionTarget".Translate()}: {selected.name}{warning}");
            }
        }

        #endregion

        #region Announcements

        private static void AnnouncePrisonerOpened()
        {
            PrisonerInteractionModeDef currentMode = currentPawn.guest.ExclusiveInteractionMode;
            string mode = currentMode.LabelCap;
            if (currentMode == PrisonerInteractionModeDefOf.Convert && currentPawn.guest.ideoForConversion != null)
            {
                mode = $"{mode} ({currentPawn.guest.ideoForConversion.name})";
            }
            string care = PrisonerTabHelper.GetMedicalCareLabel(currentPawn.playerSettings.medCare);
            string allowMedicine = "AllowMedicine".Translate();
            TolkHelper.Speak($"Prisoner Tab: {currentPawn.LabelShort}. Current Mode: {mode}. {allowMedicine}: {care}. Press Left/Right to navigate sections, Up/Down within sections, Enter to select");
        }

        private static void AnnounceSlaveOpened()
        {
            string mode = currentPawn.guest.slaveInteractionMode.LabelCap;
            string announcement = $"Slave Tab: {currentPawn.LabelShort}. Current Mode: {mode}";

            if (currentPawn.needs.TryGetNeed(out Need_Suppression suppressionNeed))
            {
                announcement += $". {"Suppression".Translate()}: {suppressionNeed.CurLevel.ToStringPercent()}";
            }

            announcement += ". Press Left/Right to navigate sections, Up/Down within sections, Enter to select";
            TolkHelper.Speak(announcement);
        }

        private static void AnnounceCurrentSection()
        {
            if (currentPawn == null)
                return;

            switch (currentSection)
            {
                case TabSection.Information:
                    TolkHelper.Speak("Information Section - Press Down to read stats");
                    break;

                case TabSection.MedicalCare:
                    string careLevel = PrisonerTabHelper.GetMedicalCareLabel(currentPawn.playerSettings.medCare);
                    TolkHelper.Speak($"{"AllowMedicine".Translate()}: {careLevel}. Use Up/Down arrows to adjust");
                    break;

                case TabSection.ExclusiveModes:
                    if (currentPawn.IsPrisonerOfColony)
                    {
                        TolkHelper.Speak($"Interaction Modes - {exclusiveModes.Count} available. Currently: {currentPawn.guest.ExclusiveInteractionMode.LabelCap}");
                    }
                    else if (currentPawn.IsSlaveOfColony)
                    {
                        TolkHelper.Speak($"Slave Modes - {slaveModes.Count} available. Currently: {currentPawn.guest.slaveInteractionMode.LabelCap}");
                    }
                    break;

                case TabSection.NonExclusiveModes:
                    TolkHelper.Speak($"Non-Exclusive Modes - {nonExclusiveModes.Count} available. Press Space to toggle");
                    break;

                case TabSection.IdeologySelection:
                    TolkHelper.Speak("IdeoConversionTarget".Translate());
                    break;
            }

            // Announce first item
            if (selectedIndex == 0)
            {
                AnnounceCurrentSelection();
            }
        }

        private static void AnnounceCurrentSelection()
        {
            if (currentPawn == null)
                return;

            switch (currentSection)
            {
                case TabSection.Information:
                    if (selectedIndex >= 0 && selectedIndex < infoLines.Count)
                    {
                        TolkHelper.Speak(infoLines[selectedIndex]);
                    }
                    break;

                case TabSection.MedicalCare:
                    string careLevel = PrisonerTabHelper.GetMedicalCareLabel(currentPawn.playerSettings.medCare);
                    TolkHelper.Speak($"{"AllowMedicine".Translate()}: {careLevel}");
                    break;

                case TabSection.ExclusiveModes:
                    if (currentPawn.IsPrisonerOfColony && selectedIndex >= 0 && selectedIndex < exclusiveModes.Count)
                    {
                        PrisonerInteractionModeDef mode = exclusiveModes[selectedIndex];
                        bool isSelected = currentPawn.guest.ExclusiveInteractionMode == mode;
                        string selectedSuffix = isSelected ? ", selected" : "";
                        string description = PrisonerTabHelper.GetInteractionModeDescription(currentPawn, mode);
                        string extra = "";
                        if (mode == PrisonerInteractionModeDefOf.Convert && isSelected && currentPawn.guest.ideoForConversion != null)
                        {
                            extra = $". {"IdeoConversionTarget".Translate()}: {currentPawn.guest.ideoForConversion.name}";
                        }
                        TolkHelper.Speak($"{mode.LabelCap}{selectedSuffix}. {description}{extra}");
                    }
                    else if (currentPawn.IsSlaveOfColony && selectedIndex >= 0 && selectedIndex < slaveModes.Count)
                    {
                        SlaveInteractionModeDef mode = slaveModes[selectedIndex];
                        bool isSelected = currentPawn.guest.slaveInteractionMode == mode;
                        string selectedSuffix = isSelected ? ", selected" : "";
                        string description = PrisonerTabHelper.GetSlaveInteractionModeDescription(currentPawn, mode);
                        TolkHelper.Speak($"{mode.LabelCap}{selectedSuffix}. {description}");
                    }
                    break;

                case TabSection.NonExclusiveModes:
                    if (selectedIndex >= 0 && selectedIndex < nonExclusiveModes.Count)
                    {
                        PrisonerInteractionModeDef mode = nonExclusiveModes[selectedIndex];
                        bool isEnabled = currentPawn.guest.IsInteractionEnabled(mode);
                        string state = isEnabled ? "[ON]" : "[OFF]";
                        TolkHelper.Speak($"{state} {mode.LabelCap}. {mode.description}");
                    }
                    break;

                case TabSection.IdeologySelection:
                    List<Ideo> ideologies = PrisonerTabHelper.GetPlayerIdeologies();
                    if (selectedIndex >= 0 && selectedIndex < ideologies.Count)
                    {
                        Ideo ideo = ideologies[selectedIndex];
                        bool isCurrent = currentPawn.guest.ideoForConversion == ideo;
                        string selectedSuffix = isCurrent ? ", selected" : "";
                        TolkHelper.Speak($"{ideo.name}{selectedSuffix}");
                    }
                    break;
            }
        }

        #endregion

        #region Helper Methods

        private static int GetSectionCount()
        {
            return System.Enum.GetValues(typeof(TabSection)).Length;
        }

        private static bool IsSectionAvailable(TabSection section)
        {
            if (currentPawn == null)
                return false;

            switch (section)
            {
                case TabSection.Information:
                    return true; // Always available

                case TabSection.MedicalCare:
                    // Vanilla ITab_Pawn_Visitor only renders the medical care selector
                    // inside DoPrisonerTab (line 353-366), not DoSlaveTab — slave medical
                    // care is set from the Health tab instead.
                    return currentPawn.IsPrisonerOfColony;

                case TabSection.ExclusiveModes:
                    if (currentPawn.IsPrisonerOfColony)
                        return exclusiveModes.Count > 0;
                    if (currentPawn.IsSlaveOfColony)
                        return slaveModes.Count > 0;
                    return false;

                case TabSection.NonExclusiveModes:
                    return currentPawn.IsPrisonerOfColony && nonExclusiveModes.Count > 0;

                case TabSection.IdeologySelection:
                    // Only available when manually opened from Convert mode
                    return false; // User can't Tab to this, only reach it via Convert

                default:
                    return false;
            }
        }

        private static int GetMaxIndexForCurrentSection()
        {
            return GetMaxIndexForSection(currentSection);
        }

        private static int GetMaxIndexForSection(TabSection section)
        {
            switch (section)
            {
                case TabSection.Information:
                    return infoLines.Count - 1;

                case TabSection.MedicalCare:
                    return 0; // Single item, use arrows to adjust

                case TabSection.ExclusiveModes:
                    if (currentPawn.IsPrisonerOfColony)
                        return exclusiveModes.Count - 1;
                    if (currentPawn.IsSlaveOfColony)
                        return slaveModes.Count - 1;
                    return 0;

                case TabSection.NonExclusiveModes:
                    return nonExclusiveModes.Count - 1;

                case TabSection.IdeologySelection:
                    return PrisonerTabHelper.GetPlayerIdeologies().Count - 1;

                default:
                    return 0;
            }
        }

        /// <summary>
        /// Returns the label shown for a given row, used by typeahead so a search matches the same
        /// text the user hears. Mirrors the per-section announcements in AnnounceCurrentSelection.
        /// </summary>
        private static string GetSectionItemLabel(TabSection section, int index)
        {
            switch (section)
            {
                case TabSection.Information:
                    return (index >= 0 && index < infoLines.Count) ? infoLines[index] : "";

                case TabSection.MedicalCare:
                    return $"{"AllowMedicine".Translate()}: {PrisonerTabHelper.GetMedicalCareLabel(currentPawn.playerSettings.medCare)}";

                case TabSection.ExclusiveModes:
                    if (currentPawn.IsPrisonerOfColony && index >= 0 && index < exclusiveModes.Count)
                        return exclusiveModes[index].LabelCap;
                    if (currentPawn.IsSlaveOfColony && index >= 0 && index < slaveModes.Count)
                        return slaveModes[index].LabelCap;
                    return "";

                case TabSection.NonExclusiveModes:
                    return (index >= 0 && index < nonExclusiveModes.Count) ? nonExclusiveModes[index].LabelCap.ToString() : "";

                default:
                    return "";
            }
        }

        /// <summary>
        /// Typeahead across every tabbable section (column) of the prisoner/slave tab at once. Typing
        /// jumps to the first matching row anywhere in the tab — an info stat, medical care, or an
        /// interaction mode — switching the active section if the match lives elsewhere. Registered
        /// with the dispatcher ahead of the inspection tree this tab opens over, so the tree no longer
        /// steals the search.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive || currentPawn == null)
                return;

            var labels = BuildFlatRows();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex) && newIndex >= 0 && newIndex < flatLocations.Count)
            {
                MoveToFlatMatch(newIndex);
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
            }
        }

        /// <summary>
        /// Rebuilds <see cref="flatLocations"/> — every searchable row across all tabbable sections,
        /// in section/order — and returns the matching label list to hand to the typeahead helper.
        /// The two lists are index-aligned so a match index maps straight back to (section, index).
        /// </summary>
        private static List<string> BuildFlatRows()
        {
            flatLocations.Clear();
            var labels = new List<string>();
            foreach (TabSection section in System.Enum.GetValues(typeof(TabSection)))
            {
                if (!IsSectionAvailable(section))
                    continue;

                int max = GetMaxIndexForSection(section);
                for (int i = 0; i <= max; i++)
                {
                    labels.Add(GetSectionItemLabel(section, i));
                    flatLocations.Add(new KeyValuePair<TabSection, int>(section, i));
                }
            }
            return labels;
        }

        /// <summary>Flat index of the current (section, index) within <see cref="flatLocations"/>.</summary>
        private static int CurrentFlatIndex()
        {
            for (int i = 0; i < flatLocations.Count; i++)
            {
                if (flatLocations[i].Key == currentSection && flatLocations[i].Value == selectedIndex)
                    return i;
            }
            return 0;
        }

        /// <summary>Moves selection to a flat row (switching section if needed) and announces it.</summary>
        private static void MoveToFlatMatch(int flatIndex)
        {
            if (flatIndex < 0 || flatIndex >= flatLocations.Count)
                return;

            currentSection = flatLocations[flatIndex].Key;
            selectedIndex = flatLocations[flatIndex].Value;
            AnnounceWithSearch();
        }

        /// <summary>
        /// Announces the current row with its search-match position ("X of N matches for '...'"),
        /// matching the universal typeahead announcement every other menu uses. Falls back to the
        /// normal per-section announcement when no search is active.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (typeahead.HasActiveSearch)
            {
                string label = GetSectionItemLabel(currentSection, selectedIndex);
                TolkHelper.Speak($"{label}, {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'");
            }
            else
            {
                AnnounceCurrentSelection();
            }
        }

        private static void ClearCachedData()
        {
            infoLines.Clear();
            exclusiveModes.Clear();
            nonExclusiveModes.Clear();
            slaveModes.Clear();
            flatLocations.Clear();
        }

        #endregion
    }
}
