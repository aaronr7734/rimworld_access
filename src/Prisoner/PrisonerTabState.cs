using System.Collections.Generic;
using System.Linq;
using System.Text;
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
                TolkHelper.Speak("RimWorldAccess.Prisoner.Guest.NotPrisonerOrSlave".Loc(pawn.LabelShort));
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

            TolkHelper.Speak("RimWorldAccess.Prisoner.Tab.Closed".Loc());
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
            TolkHelper.Speak("RimWorldAccess.Prisoner.MedicalCare.Announce".Loc(label));
        }

        private static void SelectExclusiveMode()
        {
            if (currentPawn.IsPrisonerOfColony)
            {
                if (selectedIndex >= 0 && selectedIndex < exclusiveModes.Count)
                {
                    PrisonerInteractionModeDef mode = exclusiveModes[selectedIndex];

                    // Check for special cases
                    if (mode == PrisonerInteractionModeDefOf.Convert)
                    {
                        // Switch to ideology selection section
                        currentSection = TabSection.IdeologySelection;
                        selectedIndex = 0;
                        AnnounceCurrentSection();
                        return;
                    }

                    // Set the mode
                    currentPawn.guest.SetExclusiveInteraction(mode);

                    // Handle mode-specific logic
                    if (mode == PrisonerInteractionModeDefOf.Convert && currentPawn.guest.ideoForConversion == null)
                    {
                        currentPawn.guest.ideoForConversion = Faction.OfPlayer.ideos.PrimaryIdeo;
                    }

                    string description = PrisonerTabHelper.GetInteractionModeDescription(currentPawn, mode);
                    TolkHelper.Speak("RimWorldAccess.Prisoner.Action.Selected".Loc(mode.LabelCap, description));
                }
            }
            else if (currentPawn.IsSlaveOfColony)
            {
                if (selectedIndex >= 0 && selectedIndex < slaveModes.Count)
                {
                    SlaveInteractionModeDef mode = slaveModes[selectedIndex];

                    // Check for execution confirmation
                    if (mode == SlaveInteractionModeDefOf.Execute && currentPawn.SlaveFaction != null && !currentPawn.SlaveFaction.HostileTo(Faction.OfPlayer))
                    {
                        // Warn about neutral faction
                        TolkHelper.Speak("RimWorldAccess.Prisoner.Action.ExecuteNeutralWarning".Loc(currentPawn.SlaveFaction.Name));
                        // For now, just set it - confirmation dialogs would require more complex handling
                    }

                    currentPawn.guest.slaveInteractionMode = mode;

                    string description = PrisonerTabHelper.GetSlaveInteractionModeDescription(currentPawn, mode);
                    TolkHelper.Speak("RimWorldAccess.Prisoner.Action.Selected".Loc(mode.LabelCap, description));
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

                string state = newState
                    ? "RimWorldAccess.Prisoner.Action.ToggleEnabled".Translate().ToString()
                    : "RimWorldAccess.Prisoner.Action.ToggleDisabled".Translate().ToString();
                TolkHelper.Speak("RimWorldAccess.Prisoner.Action.ToggleMode".Loc(mode.LabelCap, state));
            }
        }

        private static void SelectIdeology()
        {
            List<Ideo> ideologies = PrisonerTabHelper.GetPlayerIdeologies();
            if (selectedIndex >= 0 && selectedIndex < ideologies.Count)
            {
                Ideo selected = ideologies[selectedIndex];
                currentPawn.guest.ideoForConversion = selected;

                // Check for warden warning
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
                        warning = "RimWorldAccess.Prisoner.Warning.NoIdeoWardenInline".Translate();
                    }
                }

                TolkHelper.Speak("RimWorldAccess.Prisoner.Action.ConversionTarget".Loc(selected.name, warning));

                // Return to exclusive modes section
                currentSection = TabSection.ExclusiveModes;
                selectedIndex = exclusiveModes.IndexOf(PrisonerInteractionModeDefOf.Convert);
                if (selectedIndex < 0) selectedIndex = 0;
            }
        }

        #endregion

        #region Announcements

        private static void AnnouncePrisonerOpened()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("RimWorldAccess.Prisoner.Tab.OpenedPrisoner".Translate(currentPawn.LabelShort).ToString());
            sb.AppendLine("RimWorldAccess.Prisoner.Tab.CurrentMode".Translate(currentPawn.guest.ExclusiveInteractionMode.LabelCap).ToString());
            sb.AppendLine("RimWorldAccess.Prisoner.MedicalCare.Announce".Translate(PrisonerTabHelper.GetMedicalCareLabel(currentPawn.playerSettings.medCare)).ToString());
            sb.AppendLine("RimWorldAccess.Prisoner.Tab.NavigationHint".Translate().ToString());

            TolkHelper.SpeakData(sb.ToString().TrimEnd());
        }

        private static void AnnounceSlaveOpened()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("RimWorldAccess.Prisoner.Tab.OpenedSlave".Translate(currentPawn.LabelShort).ToString());
            sb.AppendLine("RimWorldAccess.Prisoner.Tab.CurrentMode".Translate(currentPawn.guest.slaveInteractionMode.LabelCap).ToString());

            if (currentPawn.needs.TryGetNeed(out Need_Suppression suppressionNeed))
            {
                sb.AppendLine("RimWorldAccess.Prisoner.Slave.Suppression".Translate(suppressionNeed.CurLevel.ToString("P0")).ToString());
            }

            sb.AppendLine("RimWorldAccess.Prisoner.Tab.NavigationHint".Translate().ToString());

            TolkHelper.SpeakData(sb.ToString().TrimEnd());
        }

        private static void AnnounceCurrentSection()
        {
            if (currentPawn == null)
                return;

            switch (currentSection)
            {
                case TabSection.Information:
                    TolkHelper.Speak("RimWorldAccess.Prisoner.Section.Information".Loc());
                    break;

                case TabSection.MedicalCare:
                    string careLevel = PrisonerTabHelper.GetMedicalCareLabel(currentPawn.playerSettings.medCare);
                    TolkHelper.Speak("RimWorldAccess.Prisoner.MedicalCare.AnnounceWithHint".Loc(careLevel));
                    break;

                case TabSection.ExclusiveModes:
                    if (currentPawn.IsPrisonerOfColony)
                    {
                        TolkHelper.Speak("RimWorldAccess.Prisoner.Section.ExclusiveModes".Loc(exclusiveModes.Count, currentPawn.guest.ExclusiveInteractionMode.LabelCap));
                    }
                    else if (currentPawn.IsSlaveOfColony)
                    {
                        TolkHelper.Speak("RimWorldAccess.Prisoner.Section.SlaveModes".Loc(slaveModes.Count, currentPawn.guest.slaveInteractionMode.LabelCap));
                    }
                    break;

                case TabSection.NonExclusiveModes:
                    TolkHelper.Speak("RimWorldAccess.Prisoner.Section.NonExclusiveModes".Loc(nonExclusiveModes.Count));
                    break;

                case TabSection.IdeologySelection:
                    TolkHelper.Speak("RimWorldAccess.Prisoner.Section.IdeologySelection".Loc());
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
                        TolkHelper.SpeakData(infoLines[selectedIndex]);
                    }
                    break;

                case TabSection.MedicalCare:
                    string careLevel = PrisonerTabHelper.GetMedicalCareLabel(currentPawn.playerSettings.medCare);
                    TolkHelper.Speak("RimWorldAccess.Prisoner.MedicalCare.Announce".Loc(careLevel));
                    break;

                case TabSection.ExclusiveModes:
                    if (currentPawn.IsPrisonerOfColony && selectedIndex >= 0 && selectedIndex < exclusiveModes.Count)
                    {
                        PrisonerInteractionModeDef mode = exclusiveModes[selectedIndex];
                        bool isSelected = currentPawn.guest.ExclusiveInteractionMode == mode;
                        string marker = isSelected ? "RimWorldAccess.Prisoner.Item.MarkerActive".Translate().ToString() : "";
                        string description = PrisonerTabHelper.GetInteractionModeDescription(currentPawn, mode);
                        TolkHelper.Speak("RimWorldAccess.Prisoner.Item.ModeWithDescription".Loc(marker, mode.LabelCap, description));
                    }
                    else if (currentPawn.IsSlaveOfColony && selectedIndex >= 0 && selectedIndex < slaveModes.Count)
                    {
                        SlaveInteractionModeDef mode = slaveModes[selectedIndex];
                        bool isSelected = currentPawn.guest.slaveInteractionMode == mode;
                        string marker = isSelected ? "RimWorldAccess.Prisoner.Item.MarkerActive".Translate().ToString() : "";
                        string description = PrisonerTabHelper.GetSlaveInteractionModeDescription(currentPawn, mode);
                        TolkHelper.Speak("RimWorldAccess.Prisoner.Item.ModeWithDescription".Loc(marker, mode.LabelCap, description));
                    }
                    break;

                case TabSection.NonExclusiveModes:
                    if (selectedIndex >= 0 && selectedIndex < nonExclusiveModes.Count)
                    {
                        PrisonerInteractionModeDef mode = nonExclusiveModes[selectedIndex];
                        bool isEnabled = currentPawn.guest.IsInteractionEnabled(mode);
                        string state = isEnabled
                            ? "RimWorldAccess.Prisoner.Item.StateOn".Translate().ToString()
                            : "RimWorldAccess.Prisoner.Item.StateOff".Translate().ToString();
                        TolkHelper.Speak("RimWorldAccess.Prisoner.Item.NonExclusiveMode".Loc(state, mode.LabelCap, mode.description));
                    }
                    break;

                case TabSection.IdeologySelection:
                    List<Ideo> ideologies = PrisonerTabHelper.GetPlayerIdeologies();
                    if (selectedIndex >= 0 && selectedIndex < ideologies.Count)
                    {
                        Ideo ideo = ideologies[selectedIndex];
                        bool isCurrent = currentPawn.guest.ideoForConversion == ideo;
                        string marker = isCurrent ? "RimWorldAccess.Prisoner.Item.MarkerCurrent".Translate().ToString() : "";
                        TolkHelper.Speak("RimWorldAccess.Prisoner.Item.Ideo".Loc(marker, ideo.name));
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
                    return true; // Always available

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
            switch (currentSection)
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

        private static void ClearCachedData()
        {
            infoLines.Clear();
            exclusiveModes.Clear();
            nonExclusiveModes.Clear();
            slaveModes.Clear();
        }

        #endregion
    }
}
