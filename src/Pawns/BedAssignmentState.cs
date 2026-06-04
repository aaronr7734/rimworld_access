using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation for bed assignment.
    /// Handles assigning/unassigning pawns to beds, changing bed type, and toggling medical status.
    /// </summary>
    public static class BedAssignmentState
    {
        private enum MenuLevel
        {
            MainMenu,
            AssignMenu,
            UnassignMenu,
            BedTypeMenu
        }

        private enum MainMenuAction
        {
            AssignPawn,
            UnassignPawn,
            ChangeBedType,
            ToggleMedical,
            CloseMenu
        }

        private static bool isActive = false;
        private static Building_Bed selectedBed = null;
        private static MenuLevel currentMenuLevel = MenuLevel.MainMenu;
        private static int selectedIndex = 0;
        private static List<string> menuOptions = new List<string>();
        private static List<MainMenuAction> mainMenuActions = new List<MainMenuAction>();
        private static List<Pawn> candidatePawns = new List<Pawn>();
        private static List<Pawn> assignedPawns = new List<Pawn>();

        public static bool IsActive => isActive;
        public static Building_Bed SelectedBed => selectedBed;

        /// <summary>
        /// Opens the bed assignment menu for the given bed.
        /// </summary>
        public static void Open(Building_Bed bed)
        {
            if (bed == null)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bed.NoBed".Loc());
                return;
            }

            selectedBed = bed;
            isActive = true;
            currentMenuLevel = MenuLevel.MainMenu;
            selectedIndex = 0;

            BuildMainMenu();
        }

        /// <summary>
        /// Closes the bed assignment menu.
        /// </summary>
        public static void Close()
        {
            selectedBed = null;
            isActive = false;
            currentMenuLevel = MenuLevel.MainMenu;
            selectedIndex = 0;
            menuOptions.Clear();
            mainMenuActions.Clear();
            candidatePawns.Clear();
            assignedPawns.Clear();
        }

        /// <summary>
        /// Selects the next menu option.
        /// </summary>
        public static void SelectNext()
        {
            if (menuOptions.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, menuOptions.Count);
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Selects the previous menu option.
        /// </summary>
        public static void SelectPrevious()
        {
            if (menuOptions.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, menuOptions.Count);
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Executes the currently selected menu option.
        /// </summary>
        public static void ExecuteSelected()
        {
            if (selectedIndex >= menuOptions.Count)
                return;

            switch (currentMenuLevel)
            {
                case MenuLevel.MainMenu:
                    ExecuteMainMenuOption();
                    break;
                case MenuLevel.AssignMenu:
                    ExecuteAssignMenuOption();
                    break;
                case MenuLevel.UnassignMenu:
                    ExecuteUnassignMenuOption();
                    break;
                case MenuLevel.BedTypeMenu:
                    ExecuteBedTypeMenuOption();
                    break;
            }
        }

        /// <summary>
        /// Goes back to the previous menu level or closes the menu.
        /// </summary>
        public static void GoBack()
        {
            if (currentMenuLevel == MenuLevel.MainMenu)
            {
                Close();
                InspectionReturnHelper.AnnounceParentOrFallback("RimWorldAccess.Pawns.Bed.MenuClosed".Translate());
            }
            else
            {
                // Go back to main menu
                currentMenuLevel = MenuLevel.MainMenu;
                selectedIndex = 0;
                BuildMainMenu();
            }
        }

        #region Main Menu

        private static void BuildMainMenu()
        {
            menuOptions.Clear();
            mainMenuActions.Clear();

            if (selectedBed == null)
            {
                Close();
                return;
            }

            // Build menu options
            menuOptions.Add("RimWorldAccess.Pawns.Bed.Action.AssignPawn".Translate());
            mainMenuActions.Add(MainMenuAction.AssignPawn);

            // Add unassign option if bed has assignments
            CompAssignableToPawn_Bed comp = selectedBed.CompAssignableToPawn as CompAssignableToPawn_Bed;
            if (comp != null && comp.AssignedPawnsForReading.Count > 0)
            {
                menuOptions.Add("RimWorldAccess.Pawns.Bed.Action.UnassignPawn".Translate());
                mainMenuActions.Add(MainMenuAction.UnassignPawn);
            }

            menuOptions.Add("RimWorldAccess.Pawns.Bed.Action.ChangeBedType".Translate());
            mainMenuActions.Add(MainMenuAction.ChangeBedType);
            menuOptions.Add("RimWorldAccess.Pawns.Bed.Action.ToggleMedical".Translate());
            mainMenuActions.Add(MainMenuAction.ToggleMedical);
            menuOptions.Add("RimWorldAccess.Pawns.Bed.Action.CloseMenu".Translate());
            mainMenuActions.Add(MainMenuAction.CloseMenu);

            AnnounceMainMenu();
        }

        private static void AnnounceMainMenu()
        {
            if (selectedBed == null)
                return;

            CompAssignableToPawn_Bed comp = selectedBed.CompAssignableToPawn as CompAssignableToPawn_Bed;

            // Build bed info string
            string bedInfo = selectedBed.LabelCap;

            // Add bed type
            if (selectedBed.ForPrisoners)
                bedInfo += "RimWorldAccess.Pawns.Bed.ForPrisoners".Translate();
            else if (selectedBed.ForSlaves)
                bedInfo += "RimWorldAccess.Pawns.Bed.ForSlaves".Translate();
            else if (selectedBed.ForColonists)
                bedInfo += "RimWorldAccess.Pawns.Bed.ForColonists".Translate();

            // Add medical status
            bedInfo += (selectedBed.Medical
                ? "RimWorldAccess.Pawns.Bed.MedicalSuffix"
                : "RimWorldAccess.Pawns.Bed.NotMedicalSuffix").Translate();

            // Add assignment info
            if (comp != null)
            {
                if (comp.AssignedPawnsForReading.Count > 0)
                {
                    string assignedNames = string.Join(", ", comp.AssignedPawnsForReading.Select(p => p.LabelShort));
                    bedInfo += "RimWorldAccess.Pawns.Bed.AssignedToList".Translate(assignedNames);
                }
                else
                {
                    bedInfo += "RimWorldAccess.Pawns.Bed.UnassignedSuffix".Translate();
                }
            }

            // Announce bed info and current option
            string announcement = bedInfo;
            if (menuOptions.Count > 0 && selectedIndex < menuOptions.Count)
            {
                announcement += "RimWorldAccess.Pawns.Bed.OptionSuffix".Translate(menuOptions[selectedIndex]);
            }

            TolkHelper.Speak(announcement);
        }

        private static void ExecuteMainMenuOption()
        {
            if (selectedIndex >= mainMenuActions.Count)
                return;

            switch (mainMenuActions[selectedIndex])
            {
                case MainMenuAction.AssignPawn:
                    OpenAssignMenu();
                    break;
                case MainMenuAction.UnassignPawn:
                    OpenUnassignMenu();
                    break;
                case MainMenuAction.ChangeBedType:
                    OpenBedTypeMenu();
                    break;
                case MainMenuAction.ToggleMedical:
                    ToggleMedical();
                    break;
                case MainMenuAction.CloseMenu:
                    Close();
                    TolkHelper.Speak("RimWorldAccess.Pawns.Bed.MenuClosed".Loc());
                    break;
            }
        }

        #endregion

        #region Assign Menu

        private static void OpenAssignMenu()
        {
            CompAssignableToPawn_Bed comp = selectedBed.CompAssignableToPawn as CompAssignableToPawn_Bed;
            if (comp == null)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bed.CannotAssign".Loc(), SpeechPriority.High);
                return;
            }

            // Get candidate pawns
            candidatePawns = comp.AssigningCandidates.ToList();

            if (candidatePawns.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bed.NoCandidates".Loc());
                return;
            }

            // Build menu options
            menuOptions.Clear();
            foreach (Pawn pawn in candidatePawns)
            {
                string option;

                // Check if pawn can be assigned
                if (!comp.CanAssignTo(pawn))
                {
                    option = "RimWorldAccess.Pawns.Bed.AssignWithCannot".Translate(pawn.LabelShort);
                }
                else if (comp.IdeoligionForbids(pawn))
                {
                    option = "RimWorldAccess.Pawns.Bed.AssignIdeologyForbids".Translate(pawn.LabelShort);
                }
                else if (pawn.ownership?.OwnedBed != null)
                {
                    option = "RimWorldAccess.Pawns.Bed.AlreadyAssigned".Translate(pawn.LabelShort);
                }
                else
                {
                    option = pawn.LabelShort;
                }

                menuOptions.Add(option);
            }

            currentMenuLevel = MenuLevel.AssignMenu;
            selectedIndex = 0;

            // Announce first option
            TolkHelper.Speak("RimWorldAccess.Pawns.Bed.AssignFirstOption".Loc(menuOptions[0]));
        }

        private static void ExecuteAssignMenuOption()
        {
            if (selectedIndex >= candidatePawns.Count)
                return;

            Pawn selectedPawn = candidatePawns[selectedIndex];
            CompAssignableToPawn_Bed comp = selectedBed.CompAssignableToPawn as CompAssignableToPawn_Bed;

            if (comp == null)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bed.CannotAssignSingle".Loc(), SpeechPriority.High);
                return;
            }

            // Check if pawn can be assigned
            if (!comp.CanAssignTo(selectedPawn))
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bed.CannotAssignToBed".Loc(selectedPawn.LabelShort), SpeechPriority.High);
                return;
            }

            if (comp.IdeoligionForbids(selectedPawn))
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bed.IdeologyForbidsBed".Loc(selectedPawn.LabelShort));
                return;
            }

            // Try to assign the pawn
            comp.TryAssignPawn(selectedPawn);

            // Check if assignment succeeded
            if (comp.AssignedPawnsForReading.Contains(selectedPawn))
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bed.AssignedSuccess".Loc(selectedPawn.LabelShort, selectedBed.LabelCap));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bed.AssignFailed".Loc(selectedPawn.LabelShort), SpeechPriority.High);
            }

            // Go back to main menu
            currentMenuLevel = MenuLevel.MainMenu;
            selectedIndex = 0;
            BuildMainMenu();
        }

        #endregion

        #region Unassign Menu

        private static void OpenUnassignMenu()
        {
            CompAssignableToPawn_Bed comp = selectedBed.CompAssignableToPawn as CompAssignableToPawn_Bed;
            if (comp == null)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bed.CannotUnassign".Loc(), SpeechPriority.High);
                return;
            }

            // Get assigned pawns
            assignedPawns = comp.AssignedPawnsForReading.ToList();

            if (assignedPawns.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bed.NoneAssigned".Loc());
                return;
            }

            // Build menu options
            menuOptions.Clear();
            foreach (Pawn pawn in assignedPawns)
            {
                menuOptions.Add(pawn.LabelShort);
            }

            currentMenuLevel = MenuLevel.UnassignMenu;
            selectedIndex = 0;

            // Announce first option
            TolkHelper.Speak("RimWorldAccess.Pawns.Bed.UnassignFirstOption".Loc(menuOptions[0]));
        }

        private static void ExecuteUnassignMenuOption()
        {
            if (selectedIndex >= assignedPawns.Count)
                return;

            Pawn selectedPawn = assignedPawns[selectedIndex];
            CompAssignableToPawn_Bed comp = selectedBed.CompAssignableToPawn as CompAssignableToPawn_Bed;

            if (comp == null)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bed.CannotUnassignSingle".Loc(), SpeechPriority.High);
                return;
            }

            // Try to unassign the pawn
            comp.TryUnassignPawn(selectedPawn, true, false);

            // Check if unassignment succeeded
            if (!comp.AssignedPawnsForReading.Contains(selectedPawn))
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bed.UnassignedSuccess".Loc(selectedPawn.LabelShort, selectedBed.LabelCap));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Bed.UnassignFailed".Loc(selectedPawn.LabelShort), SpeechPriority.High);
            }

            // Go back to main menu
            currentMenuLevel = MenuLevel.MainMenu;
            selectedIndex = 0;
            BuildMainMenu();
        }

        #endregion

        #region Bed Type Menu

        private static void OpenBedTypeMenu()
        {
            menuOptions.Clear();
            menuOptions.Add("RimWorldAccess.Pawns.Bed.Type.Colonist".Translate());
            menuOptions.Add("RimWorldAccess.Pawns.Bed.Type.Prisoner".Translate());
            menuOptions.Add("RimWorldAccess.Pawns.Bed.Type.Slave".Translate());

            currentMenuLevel = MenuLevel.BedTypeMenu;
            selectedIndex = 0;

            // Set initial selection to current bed type
            if (selectedBed.ForPrisoners)
                selectedIndex = 1;
            else if (selectedBed.ForSlaves)
                selectedIndex = 2;
            else
                selectedIndex = 0;

            // Announce current selection
            TolkHelper.Speak("RimWorldAccess.Pawns.Bed.ChangeTypeOption".Loc(menuOptions[selectedIndex]));
        }

        private static void ExecuteBedTypeMenuOption()
        {
            if (selectedIndex >= menuOptions.Count)
                return;

            BedOwnerType newOwnerType = BedOwnerType.Colonist;
            switch (selectedIndex)
            {
                case 0:
                    newOwnerType = BedOwnerType.Colonist;
                    break;
                case 1:
                    newOwnerType = BedOwnerType.Prisoner;
                    break;
                case 2:
                    newOwnerType = BedOwnerType.Slave;
                    break;
            }

            // Check if changing to prisoner type
            if (newOwnerType == BedOwnerType.Prisoner)
            {
                // Validate that the room CAN be a valid prison cell (enclosed room)
                Room room = selectedBed.GetRoom();
                if (room == null || !Building_Bed.RoomCanBePrisonCell(room))
                {
                    TolkHelper.Speak("RimWorldAccess.Pawns.Bed.NeedEnclosed".Loc(), SpeechPriority.High);
                    return;
                }
            }

            // SetBedOwnerTypeByInterface fires Room.Notify_RoomShapeChanged so Room.isPrisonCell
            // updates immediately; the raw ForPrisoners setter does not. It iterates
            // Find.Selector.SelectedObjects, so ensure our bed is selected first.
            var selector = Find.Selector;
            var previousSelection = selector.SelectedObjects.ToList();
            var bedAlreadySelected = previousSelection.Contains(selectedBed);
            if (!bedAlreadySelected)
            {
                selector.ClearSelection();
                selector.Select(selectedBed, playSound: false, forceDesignatorDeselect: false);
            }
            try
            {
                selectedBed.SetBedOwnerTypeByInterface(newOwnerType);
            }
            finally
            {
                if (!bedAlreadySelected)
                {
                    selector.ClearSelection();
                    foreach (var obj in previousSelection)
                    {
                        selector.Select(obj, playSound: false, forceDesignatorDeselect: false);
                    }
                }
            }

            TolkHelper.Speak("RimWorldAccess.Pawns.Bed.TypeChanged".Loc(menuOptions[selectedIndex]));

            // Go back to main menu
            currentMenuLevel = MenuLevel.MainMenu;
            selectedIndex = 0;
            BuildMainMenu();
        }

        #endregion

        #region Toggle Medical

        private static void ToggleMedical()
        {
            if (selectedBed == null)
                return;

            // Toggle medical status
            selectedBed.Medical = !selectedBed.Medical;

            string status = (selectedBed.Medical
                ? "RimWorldAccess.Pawns.Bed.MedicalState.Medical"
                : "RimWorldAccess.Pawns.Bed.MedicalState.NotMedical").Translate();
            TolkHelper.Speak("RimWorldAccess.Pawns.Bed.MedicalStatus".Loc(status));

            // Rebuild main menu to reflect changes
            BuildMainMenu();
        }

        #endregion

        #region Helpers

        private static void AnnounceCurrentSelection()
        {
            if (menuOptions.Count == 0 || selectedIndex >= menuOptions.Count)
                return;

            string prefix = "";
            switch (currentMenuLevel)
            {
                case MenuLevel.MainMenu:
                    prefix = "";
                    break;
                case MenuLevel.AssignMenu:
                    prefix = "RimWorldAccess.Pawns.Bed.Prefix.Assign".Translate();
                    break;
                case MenuLevel.UnassignMenu:
                    prefix = "RimWorldAccess.Pawns.Bed.Prefix.Unassign".Translate();
                    break;
                case MenuLevel.BedTypeMenu:
                    prefix = "RimWorldAccess.Pawns.Bed.Prefix.BedType".Translate();
                    break;
            }

            TolkHelper.Speak("RimWorldAccess.Pawns.Bed.PrefixedOption".Loc(prefix, menuOptions[selectedIndex]));
        }

        #endregion
    }
}
