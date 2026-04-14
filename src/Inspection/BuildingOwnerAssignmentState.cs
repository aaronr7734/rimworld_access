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

        private static bool isActive = false;
        private static ThingWithComps selectedBuilding = null;
        private static CompAssignableToPawn selectedComp = null;
        private static MenuLevel currentMenuLevel = MenuLevel.MainMenu;
        private static int selectedIndex = 0;
        private static List<string> menuOptions = new List<string>();
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
                TolkHelper.Speak("No building to configure");
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
                InspectionReturnHelper.AnnounceParentOrFallback("Assignment menu closed");
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

            if (selectedBuilding == null || selectedComp == null)
            {
                Close();
                return;
            }

            menuOptions.Add("Assign owner");

            if (selectedComp.AssignedPawnsForReading.Count > 0)
            {
                menuOptions.Add("Unassign owner");
            }

            menuOptions.Add("Close menu");

            AnnounceMainMenu();
        }

        private static void AnnounceMainMenu()
        {
            if (selectedBuilding == null || selectedComp == null)
                return;

            string info = $"{selectedBuilding.LabelCap}";

            if (selectedComp.AssignedPawnsForReading.Count > 0)
            {
                string assignedNames = string.Join(", ", selectedComp.AssignedPawnsForReading.Select(p => p.LabelShort));
                info += $" - Assigned to: {assignedNames}";
            }
            else
            {
                info += " - Unassigned";
            }

            if (menuOptions.Count > 0 && selectedIndex < menuOptions.Count)
            {
                info += $" - {menuOptions[selectedIndex]}";
            }

            TolkHelper.Speak(info);
        }

        private static void ExecuteMainMenuOption()
        {
            if (selectedIndex >= menuOptions.Count)
                return;

            string option = menuOptions[selectedIndex];

            switch (option)
            {
                case "Assign owner":
                    OpenAssignMenu();
                    break;
                case "Unassign owner":
                    OpenUnassignMenu();
                    break;
                case "Close menu":
                    Close();
                    TolkHelper.Speak("Assignment menu closed");
                    break;
            }
        }

        #endregion

        #region Assign Menu

        private static void OpenAssignMenu()
        {
            if (selectedComp == null)
            {
                TolkHelper.Speak("Cannot assign owners to this building", SpeechPriority.High);
                return;
            }

            candidatePawns = selectedComp.AssigningCandidates.ToList();

            if (candidatePawns.Count == 0)
            {
                TolkHelper.Speak("No available pawns to assign");
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
                    option += !string.IsNullOrEmpty(reason) ? $" ({reason.StripTags()})" : " (Cannot assign)";
                }
                else if (selectedComp.IdeoligionForbids(pawn))
                {
                    option += " (Ideology forbids)";
                }
                else if (selectedComp.AssignedAnything(pawn))
                {
                    option += " (Has other assignment)";
                }

                menuOptions.Add(option);
            }

            currentMenuLevel = MenuLevel.AssignMenu;
            selectedIndex = 0;

            TolkHelper.Speak($"Assign owner - {menuOptions[0]}");
        }

        private static void ExecuteAssignMenuOption()
        {
            if (selectedIndex >= candidatePawns.Count)
                return;

            Pawn selectedPawn = candidatePawns[selectedIndex];

            if (selectedComp == null)
            {
                TolkHelper.Speak("Cannot assign owner", SpeechPriority.High);
                return;
            }

            AcceptanceReport report = selectedComp.CanAssignTo(selectedPawn);
            if (!report.Accepted)
            {
                string reason = report.Reason;
                string message = !string.IsNullOrEmpty(reason)
                    ? $"Cannot assign {selectedPawn.LabelShort}: {reason.StripTags()}"
                    : $"Cannot assign {selectedPawn.LabelShort} to this building";
                TolkHelper.Speak(message, SpeechPriority.High);
                return;
            }

            if (selectedComp.IdeoligionForbids(selectedPawn))
            {
                TolkHelper.Speak($"Ideology forbids {selectedPawn.LabelShort} from using this building");
                return;
            }

            selectedComp.TryAssignPawn(selectedPawn);

            if (selectedComp.AssignedPawnsForReading.Contains(selectedPawn))
            {
                TolkHelper.Speak($"{selectedPawn.LabelShort} assigned to {selectedBuilding.LabelCap}");
            }
            else
            {
                TolkHelper.Speak($"Failed to assign {selectedPawn.LabelShort}", SpeechPriority.High);
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
                TolkHelper.Speak("Cannot unassign owners from this building", SpeechPriority.High);
                return;
            }

            assignedPawns = selectedComp.AssignedPawnsForReading.ToList();

            if (assignedPawns.Count == 0)
            {
                TolkHelper.Speak("No pawns assigned to this building");
                return;
            }

            menuOptions.Clear();
            foreach (Pawn pawn in assignedPawns)
            {
                menuOptions.Add(pawn.LabelShort);
            }

            currentMenuLevel = MenuLevel.UnassignMenu;
            selectedIndex = 0;

            TolkHelper.Speak($"Unassign owner - {menuOptions[0]}");
        }

        private static void ExecuteUnassignMenuOption()
        {
            if (selectedIndex >= assignedPawns.Count)
                return;

            Pawn selectedPawn = assignedPawns[selectedIndex];

            if (selectedComp == null)
            {
                TolkHelper.Speak("Cannot unassign owner", SpeechPriority.High);
                return;
            }

            selectedComp.TryUnassignPawn(selectedPawn, true, false);

            if (!selectedComp.AssignedPawnsForReading.Contains(selectedPawn))
            {
                TolkHelper.Speak($"{selectedPawn.LabelShort} unassigned from {selectedBuilding.LabelCap}");
            }
            else
            {
                TolkHelper.Speak($"Failed to unassign {selectedPawn.LabelShort}", SpeechPriority.High);
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

            string prefix = "";
            switch (currentMenuLevel)
            {
                case MenuLevel.MainMenu:
                    prefix = "";
                    break;
                case MenuLevel.AssignMenu:
                    prefix = "Assign: ";
                    break;
                case MenuLevel.UnassignMenu:
                    prefix = "Unassign: ";
                    break;
            }

            TolkHelper.Speak($"{prefix}{menuOptions[selectedIndex]}");
        }

        #endregion
    }
}
