using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation for generic building owner assignment.
    /// Works with any building that has CompAssignableToPawn (meditation spots, thrones, graves, etc.).
    /// Beds use BedAssignmentState instead for their specialized features.
    /// </summary>
    public static class BuildingOwnerAssignmentState
    {
        private enum MenuLevel
        {
            MainMenu,
            AssignMenu,
            UnassignMenu
        }

        private enum MainAction
        {
            AssignOwner,
            UnassignOwner,
            CloseMenu
        }

        private static bool isActive = false;
        private static ThingWithComps selectedBuilding = null;
        private static CompAssignableToPawn selectedComp = null;
        private static MenuLevel currentMenuLevel = MenuLevel.MainMenu;
        private static int selectedIndex = 0;
        private static List<string> menuOptions = new List<string>();
        private static List<MainAction> mainActions = new List<MainAction>();
        private static List<Pawn> candidatePawns = new List<Pawn>();
        private static List<Pawn> assignedPawns = new List<Pawn>();

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the owner assignment menu for the given building.
        /// </summary>
        public static void Open(ThingWithComps building, CompAssignableToPawn comp)
        {
            if (building == null || comp == null)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoBuildingToConfigure".Loc());
                return;
            }

            selectedBuilding = building;
            selectedComp = comp;
            isActive = true;
            currentMenuLevel = MenuLevel.MainMenu;
            selectedIndex = 0;

            BuildMainMenu();
        }

        /// <summary>
        /// Closes the owner assignment menu.
        /// </summary>
        public static void Close()
        {
            selectedBuilding = null;
            selectedComp = null;
            isActive = false;
            currentMenuLevel = MenuLevel.MainMenu;
            selectedIndex = 0;
            menuOptions.Clear();
            mainActions.Clear();
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
                InspectionReturnHelper.AnnounceParentOrFallback("RimWorldAccess.Inspection.OwnerAssignment.MenuClosed".Translate());
            }
            else
            {
                currentMenuLevel = MenuLevel.MainMenu;
                selectedIndex = 0;
                BuildMainMenu();
            }
        }

        #region Main Menu

        private static void BuildMainMenu()
        {
            menuOptions.Clear();
            mainActions.Clear();

            if (selectedBuilding == null || selectedComp == null)
            {
                Close();
                return;
            }

            menuOptions.Add("RimWorldAccess.Inspection.OwnerAssignment.MainMenu.AssignOwner".Translate());
            mainActions.Add(MainAction.AssignOwner);

            if (selectedComp.AssignedPawnsForReading.Count > 0)
            {
                menuOptions.Add("RimWorldAccess.Inspection.OwnerAssignment.MainMenu.UnassignOwner".Translate());
                mainActions.Add(MainAction.UnassignOwner);
            }

            menuOptions.Add("RimWorldAccess.Inspection.OwnerAssignment.MainMenu.CloseMenu".Translate());
            mainActions.Add(MainAction.CloseMenu);

            AnnounceMainMenu();
        }

        private static void AnnounceMainMenu()
        {
            if (selectedBuilding == null || selectedComp == null)
                return;

            if (menuOptions.Count == 0 || selectedIndex >= menuOptions.Count)
                return;

            string currentOption = menuOptions[selectedIndex];
            string info;

            if (selectedComp.AssignedPawnsForReading.Count > 0)
            {
                string assignedNames = string.Join(", ", selectedComp.AssignedPawnsForReading.Select(p => p.LabelShort));
                info = "RimWorldAccess.Inspection.OwnerAssignment.MainAnnouncement.Assigned".Translate(
                    selectedBuilding.LabelCap, assignedNames, currentOption);
            }
            else
            {
                info = "RimWorldAccess.Inspection.OwnerAssignment.MainAnnouncement.Unassigned".Translate(
                    selectedBuilding.LabelCap, currentOption);
            }

            TolkHelper.Speak(info);
        }

        private static void ExecuteMainMenuOption()
        {
            if (selectedIndex >= mainActions.Count)
                return;

            switch (mainActions[selectedIndex])
            {
                case MainAction.AssignOwner:
                    OpenAssignMenu();
                    break;
                case MainAction.UnassignOwner:
                    OpenUnassignMenu();
                    break;
                case MainAction.CloseMenu:
                    Close();
                    TolkHelper.Speak("RimWorldAccess.Inspection.OwnerAssignment.MenuClosed".Loc());
                    break;
            }
        }

        #endregion

        #region Assign Menu

        private static void OpenAssignMenu()
        {
            if (selectedComp == null)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.OwnerAssignment.CannotAssignBuilding".Loc(), SpeechPriority.High);
                return;
            }

            candidatePawns = selectedComp.AssigningCandidates.ToList();

            if (candidatePawns.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.OwnerAssignment.NoCandidates".Loc());
                return;
            }

            menuOptions.Clear();
            foreach (Pawn pawn in candidatePawns)
            {
                string option = pawn.LabelShort;

                AcceptanceReport report = selectedComp.CanAssignTo(pawn);
                if (!report.Accepted)
                {
                    string reason = report.Reason;
                    if (!string.IsNullOrEmpty(reason))
                        option += "RimWorldAccess.Inspection.OwnerAssignment.AssignReason.CannotAssignWithReason".Translate(reason.StripTags());
                    else
                        option += "RimWorldAccess.Inspection.OwnerAssignment.AssignReason.CannotAssign".Translate();
                }
                else if (selectedComp.IdeoligionForbids(pawn))
                {
                    option += "RimWorldAccess.Inspection.OwnerAssignment.AssignReason.IdeologyForbids".Translate();
                }
                else if (selectedComp.AssignedAnything(pawn))
                {
                    option += "RimWorldAccess.Inspection.OwnerAssignment.AssignReason.HasOtherAssignment".Translate();
                }

                menuOptions.Add(option);
            }

            currentMenuLevel = MenuLevel.AssignMenu;
            selectedIndex = 0;

            TolkHelper.Speak("RimWorldAccess.Inspection.OwnerAssignment.AssignSubmenu.Announce".Loc(menuOptions[0]));
        }

        private static void ExecuteAssignMenuOption()
        {
            if (selectedIndex >= candidatePawns.Count)
                return;

            Pawn selectedPawn = candidatePawns[selectedIndex];

            if (selectedComp == null)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.OwnerAssignment.CannotAssignOwner".Loc(), SpeechPriority.High);
                return;
            }

            AcceptanceReport report = selectedComp.CanAssignTo(selectedPawn);
            if (!report.Accepted)
            {
                string reason = report.Reason;
                string message = !string.IsNullOrEmpty(reason)
                    ? "RimWorldAccess.Inspection.OwnerAssignment.CannotAssignPawnWithReason".Translate(selectedPawn.LabelShort, reason.StripTags()).ToString()
                    : "RimWorldAccess.Inspection.OwnerAssignment.CannotAssignPawn".Translate(selectedPawn.LabelShort).ToString();
                TolkHelper.Speak(message, SpeechPriority.High);
                return;
            }

            if (selectedComp.IdeoligionForbids(selectedPawn))
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.OwnerAssignment.IdeologyForbidsPawn".Loc(selectedPawn.LabelShort));
                return;
            }

            selectedComp.TryAssignPawn(selectedPawn);

            if (selectedComp.AssignedPawnsForReading.Contains(selectedPawn))
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.OwnerAssignment.ResultAssigned".Loc(selectedPawn.LabelShort, selectedBuilding.LabelCap));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.OwnerAssignment.ResultAssignFailed".Loc(selectedPawn.LabelShort), SpeechPriority.High);
            }

            currentMenuLevel = MenuLevel.MainMenu;
            selectedIndex = 0;
            BuildMainMenu();
        }

        #endregion

        #region Unassign Menu

        private static void OpenUnassignMenu()
        {
            if (selectedComp == null)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.OwnerAssignment.CannotUnassignBuilding".Loc(), SpeechPriority.High);
                return;
            }

            assignedPawns = selectedComp.AssignedPawnsForReading.ToList();

            if (assignedPawns.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.OwnerAssignment.NoneAssigned".Loc());
                return;
            }

            menuOptions.Clear();
            foreach (Pawn pawn in assignedPawns)
            {
                menuOptions.Add(pawn.LabelShort);
            }

            currentMenuLevel = MenuLevel.UnassignMenu;
            selectedIndex = 0;

            TolkHelper.Speak("RimWorldAccess.Inspection.OwnerAssignment.UnassignSubmenu.Announce".Loc(menuOptions[0]));
        }

        private static void ExecuteUnassignMenuOption()
        {
            if (selectedIndex >= assignedPawns.Count)
                return;

            Pawn selectedPawn = assignedPawns[selectedIndex];

            if (selectedComp == null)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.OwnerAssignment.CannotUnassignOwner".Loc(), SpeechPriority.High);
                return;
            }

            selectedComp.TryUnassignPawn(selectedPawn, true, false);

            if (!selectedComp.AssignedPawnsForReading.Contains(selectedPawn))
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.OwnerAssignment.ResultUnassigned".Loc(selectedPawn.LabelShort, selectedBuilding.LabelCap));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.OwnerAssignment.ResultUnassignFailed".Loc(selectedPawn.LabelShort), SpeechPriority.High);
            }

            currentMenuLevel = MenuLevel.MainMenu;
            selectedIndex = 0;
            BuildMainMenu();
        }

        #endregion

        #region Helpers

        private static void AnnounceCurrentSelection()
        {
            if (menuOptions.Count == 0 || selectedIndex >= menuOptions.Count)
                return;

            switch (currentMenuLevel)
            {
                case MenuLevel.MainMenu:
                    AnnounceMainMenu();
                    return;
                case MenuLevel.AssignMenu:
                    TolkHelper.Speak("RimWorldAccess.Inspection.OwnerAssignment.AssignSubmenu.Item".Loc(menuOptions[selectedIndex]));
                    return;
                case MenuLevel.UnassignMenu:
                    TolkHelper.Speak("RimWorldAccess.Inspection.OwnerAssignment.UnassignSubmenu.Item".Loc(menuOptions[selectedIndex]));
                    return;
            }
        }

        #endregion
    }
}
