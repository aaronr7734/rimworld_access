using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    public static class AnimalsMenuState
    {
        public static bool IsActive { get; private set; } = false;

        private static List<Pawn> animalsList = new List<Pawn>();
        private static TabularMenuHelper<Pawn> tableHelper;

        // Submenu state
        private enum SubmenuType { None, Master, AllowedArea, MedicalCare }
        private static SubmenuType activeSubmenu = SubmenuType.None;
        private static int submenuSelectedIndex = 0;
        private static List<object> submenuOptions = new List<object>();
        private static TypeaheadSearchHelper submenuTypeahead = new TypeaheadSearchHelper();

        // Last applied area for bulk operations (null = Unrestricted)
        private static Area lastAppliedArea = null;

        public static TypeaheadSearchHelper Typeahead => tableHelper?.Typeahead;
        public static TypeaheadSearchHelper SubmenuTypeahead => submenuTypeahead;
        public static int CurrentAnimalIndex => tableHelper?.CurrentRowIndex ?? 0;
        public static int SubmenuSelectedIndex => submenuSelectedIndex;
        public static bool IsInSubmenu => activeSubmenu != SubmenuType.None;

        public static void Open()
        {
            // Prevent double-opening
            if (IsActive) return;

            if (Find.CurrentMap == null)
            {
                TolkHelper.Speak("No map loaded");
                return;
            }

            // Get all colony animals
            animalsList = Find.CurrentMap.mapPawns.ColonyAnimals.ToList();

            if (animalsList.Count == 0)
            {
                TolkHelper.Speak("No colony animals found");
                return;
            }

            // Initialize table helper
            tableHelper = new TabularMenuHelper<Pawn>(
                getColumnCount: AnimalsMenuHelper.GetTotalColumnCount,
                getItemLabel: AnimalsMenuHelper.GetAnimalName,
                getColumnName: AnimalsMenuHelper.GetColumnName,
                getColumnValue: AnimalsMenuHelper.GetColumnValue,
                sortByColumn: (items, col, desc) => AnimalsMenuHelper.SortAnimalsByColumn(items.ToList(), col, desc),
                defaultSortColumn: 0,  // Name
                defaultSortDescending: false,
                getColumnTooltip: (pawn, col) => AnimalsMenuHelper.GetColumnTooltip(pawn, col)
            );

            // Apply default sort (by name)
            animalsList = AnimalsMenuHelper.SortAnimalsByColumn(animalsList, 0, false);

            tableHelper.Reset(0, false);
            activeSubmenu = SubmenuType.None;
            IsActive = true;

            SoundDefOf.TabOpen.PlayOneShotOnCamera();

            string announcement = $"Animals menu, {animalsList.Count} animals";
            TolkHelper.Speak(announcement);
            AnnounceCurrentCell(includeAnimalName: true);
        }

        public static void Close(bool silent = false)
        {
            IsActive = false;
            activeSubmenu = SubmenuType.None;
            animalsList.Clear();
            tableHelper?.ClearSearch();

            // Ensure any stale designator from area management is cleared
            Find.DesignatorManager?.Deselect();

            if (!silent)
            {
                SoundDefOf.TabClose.PlayOneShotOnCamera();
                TolkHelper.Speak("Animals menu closed");
            }
        }

        public static void SelectNextAnimal()
        {
            if (animalsList.Count == 0) return;
            tableHelper.SelectNextRow(animalsList.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: true);
        }

        public static void SelectPreviousAnimal()
        {
            if (animalsList.Count == 0) return;
            tableHelper.SelectPreviousRow(animalsList.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: true);
        }

        public static void SelectNextColumn()
        {
            tableHelper.SelectNextColumn();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: false);
        }

        public static void SelectPreviousColumn()
        {
            tableHelper.SelectPreviousColumn();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: false);
        }

        private static void AnnounceCurrentCell(bool includeAnimalName = true)
        {
            if (animalsList.Count == 0) return;

            Pawn currentAnimal = animalsList[tableHelper.CurrentRowIndex];
            string announcement = tableHelper.BuildCellAnnouncement(currentAnimal, animalsList.Count, includeAnimalName);
            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Opens an info card for the currently selected colony animal.
        /// </summary>
        public static void OpenInfoCard()
        {
            if (animalsList == null || animalsList.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No info card available");
                return;
            }
            Pawn animal = animalsList[tableHelper.CurrentRowIndex];
            if (animal != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(animal));
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No info card available");
            }
        }

        public static void InteractWithCurrentCell()
        {
            if (animalsList.Count == 0) return;

            Pawn currentAnimal = animalsList[tableHelper.CurrentRowIndex];
            int currentColumnIndex = tableHelper.CurrentColumnIndex;

            if (!AnimalsMenuHelper.IsColumnInteractive(currentColumnIndex))
            {
                // Just re-announce for non-interactive columns
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentCell(includeAnimalName: false);
                return;
            }

            int fixedColumnsBeforeTraining = 5; // Name, Gender, Age, LifeStage, Pregnant
            int trainingCount = AnimalsMenuHelper.GetAllTrainables().Count;

            // Handle interaction based on column type
            if (currentColumnIndex < fixedColumnsBeforeTraining)
            {
                // Fixed columns before training
                AnimalsMenuHelper.ColumnType type = (AnimalsMenuHelper.ColumnType)currentColumnIndex;
                if (type == AnimalsMenuHelper.ColumnType.Name)
                {
                    // Jump to animal on map
                    JumpToAnimalOnMap(currentAnimal);
                }
                else
                {
                    // Other fixed columns are not interactive
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentCell(includeAnimalName: false);
                }
            }
            else if (currentColumnIndex < fixedColumnsBeforeTraining + trainingCount)
            {
                // Training column
                ToggleTraining(currentAnimal, currentColumnIndex);
            }
            else
            {
                // Fixed columns after training - use dynamic column type lookup
                var columnType = AnimalsMenuHelper.GetColumnTypeAfterTraining(currentColumnIndex);
                if (columnType == null)
                {
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentCell(includeAnimalName: false);
                    return;
                }

                switch (columnType.Value)
                {
                    case AnimalsMenuHelper.ColumnType.SpecialTrainable:
                        ToggleSpecialTrainable(currentAnimal);
                        break;
                    case AnimalsMenuHelper.ColumnType.FollowDrafted:
                        ToggleFollowDrafted(currentAnimal);
                        break;
                    case AnimalsMenuHelper.ColumnType.FollowFieldwork:
                        ToggleFollowFieldwork(currentAnimal);
                        break;
                    case AnimalsMenuHelper.ColumnType.AnimalDig:
                        ToggleAnimalDig(currentAnimal);
                        break;
                    case AnimalsMenuHelper.ColumnType.AnimalForage:
                        ToggleAnimalForage(currentAnimal);
                        break;
                    case AnimalsMenuHelper.ColumnType.Master:
                        OpenMasterSubmenu(currentAnimal);
                        break;
                    case AnimalsMenuHelper.ColumnType.Sterile:
                        ToggleSterilization(currentAnimal);
                        break;
                    case AnimalsMenuHelper.ColumnType.Slaughter:
                        ToggleSlaughter(currentAnimal);
                        break;
                    case AnimalsMenuHelper.ColumnType.MedicalCare:
                        OpenMedicalCareSubmenu(currentAnimal);
                        break;
                    case AnimalsMenuHelper.ColumnType.ReleaseToWild:
                        ToggleReleaseToWild(currentAnimal);
                        break;
                    case AnimalsMenuHelper.ColumnType.AllowedArea:
                        OpenAllowedAreaSubmenu(currentAnimal);
                        break;
                    // MentalState, Bond are display-only (handled by IsColumnInteractive returning false)
                }
            }
        }

        private static void JumpToAnimalOnMap(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null)
            {
                TolkHelper.Speak("Animal not on map", SpeechPriority.High);
                return;
            }

            // Get animal's position
            IntVec3 position = pawn.Position;

            // Close the animals menu
            Close();

            // Move map cursor to animal's position
            MapNavigationState.CurrentCursorPosition = position;

            // Jump camera to position
            Find.CameraDriver?.JumpToCurrentMapLoc(position);

            // Announce the jump
            string animalName = pawn.Name != null ? pawn.Name.ToStringShort : pawn.def.LabelCap.ToString();
            TolkHelper.Speak($"Jumped to {animalName}");
        }

        // === Cell Interaction Methods ===

        private static void ToggleSlaughter(Pawn pawn)
        {
            if (pawn.Map == null) return;

            Designation existing = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Slaughter);

            if (existing != null)
            {
                // Remove slaughter designation
                pawn.Map.designationManager.RemoveDesignation(existing);
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }
            else
            {
                // Check if bonded
                bool isBonded = pawn.relations?.GetFirstDirectRelationPawn(PawnRelationDefOf.Bond) != null;

                if (isBonded)
                {
                    TolkHelper.Speak($"{pawn.Name.ToStringShort} is bonded. Marking for slaughter anyway.");
                }

                // Add slaughter designation
                pawn.Map.designationManager.AddDesignation(new Designation(pawn, DesignationDefOf.Slaughter));
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }

            AnnounceCurrentCell(includeAnimalName: false);
        }

        private static void ToggleTraining(Pawn pawn, int columnIndex)
        {
            TrainableDef trainable = AnimalsMenuHelper.GetTrainableAtColumn(columnIndex);
            if (trainable == null || pawn.training == null) return;

            AcceptanceReport canTrain = pawn.training.CanAssignToTrain(trainable);
            if (!canTrain.Accepted)
            {
                TolkHelper.Speak($"{pawn.Name.ToStringShort} cannot be trained in {trainable.LabelCap}", SpeechPriority.High);
                return;
            }

            bool currentlyWanted = pawn.training.GetWanted(trainable);
            pawn.training.SetWantedRecursive(trainable, !currentlyWanted);

            if (!currentlyWanted)
            {
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }
            else
            {
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }

            AnnounceCurrentCell(includeAnimalName: false);
        }

        private static void ToggleFollowDrafted(Pawn pawn)
        {
            if (pawn.playerSettings == null) return;

            // Check if animal has learned Obedience (Guard)
            if (pawn.training?.HasLearned(TrainableDefOf.Obedience) != true)
            {
                TolkHelper.Speak($"{pawn.LabelShort} requires {TrainableDefOf.Obedience.LabelCap} training", SpeechPriority.High);
                return;
            }

            pawn.playerSettings.followDrafted = !pawn.playerSettings.followDrafted;

            if (pawn.playerSettings.followDrafted)
            {
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }
            else
            {
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }

            AnnounceCurrentCell(includeAnimalName: false);
        }

        private static void ToggleFollowFieldwork(Pawn pawn)
        {
            if (pawn.playerSettings == null) return;

            // Check if animal has learned Obedience (Guard)
            if (pawn.training?.HasLearned(TrainableDefOf.Obedience) != true)
            {
                TolkHelper.Speak($"{pawn.LabelShort} requires {TrainableDefOf.Obedience.LabelCap} training", SpeechPriority.High);
                return;
            }

            pawn.playerSettings.followFieldwork = !pawn.playerSettings.followFieldwork;

            if (pawn.playerSettings.followFieldwork)
            {
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }
            else
            {
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }

            AnnounceCurrentCell(includeAnimalName: false);
        }

        private static void ToggleAnimalDig(Pawn pawn)
        {
            if (pawn.playerSettings == null) return;

            // Check if animal has learned Dig
            if (pawn.training?.HasLearned(TrainableDefOf.Dig) != true)
            {
                TolkHelper.Speak($"{pawn.LabelShort} has not learned {TrainableDefOf.Dig.LabelCap}", SpeechPriority.High);
                return;
            }

            pawn.playerSettings.animalDig = !pawn.playerSettings.animalDig;

            if (pawn.playerSettings.animalDig)
            {
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }
            else
            {
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }

            AnnounceCurrentCell(includeAnimalName: false);
        }

        private static void ToggleAnimalForage(Pawn pawn)
        {
            if (pawn.playerSettings == null) return;

            // Check if animal has learned Forage
            if (pawn.training?.HasLearned(TrainableDefOf.Forage) != true)
            {
                TolkHelper.Speak($"{pawn.LabelShort} has not learned {TrainableDefOf.Forage.LabelCap}", SpeechPriority.High);
                return;
            }

            pawn.playerSettings.animalForage = !pawn.playerSettings.animalForage;

            if (pawn.playerSettings.animalForage)
            {
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }
            else
            {
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }

            AnnounceCurrentCell(includeAnimalName: false);
        }

        private static void ToggleSpecialTrainable(Pawn pawn)
        {
            var specialTrainables = AnimalsMenuHelper.GetSpecialTrainables(pawn);
            if (specialTrainables.Count == 0)
            {
                TolkHelper.Speak($"{pawn.LabelShort} has no special abilities", SpeechPriority.High);
                return;
            }

            if (pawn.training == null) return;

            // Determine current state - if ANY are wanted, we'll turn all off; otherwise turn all on
            bool anyWanted = false;
            foreach (var trainable in specialTrainables)
            {
                if (pawn.training.GetWanted(trainable))
                {
                    anyWanted = true;
                    break;
                }
            }

            // Toggle all special trainables
            bool newState = !anyWanted;
            foreach (var trainable in specialTrainables)
            {
                pawn.training.SetWantedRecursive(trainable, newState);
            }

            if (newState)
            {
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }
            else
            {
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }

            AnnounceCurrentCell(includeAnimalName: false);
        }

        private static void ToggleReleaseToWild(Pawn pawn)
        {
            if (pawn.Map == null) return;

            Designation existing = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.ReleaseAnimalToWild);

            if (existing != null)
            {
                pawn.Map.designationManager.RemoveDesignation(existing);
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }
            else
            {
                pawn.Map.designationManager.AddDesignation(new Designation(pawn, DesignationDefOf.ReleaseAnimalToWild));
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }

            AnnounceCurrentCell(includeAnimalName: false);
        }

        private static void ToggleSterilization(Pawn pawn)
        {
            // Can't toggle if already sterilized
            if (AnimalsMenuHelper.IsAnimalSterilized(pawn))
            {
                TolkHelper.Speak($"{pawn.LabelShort} is already sterilized", SpeechPriority.High);
                return;
            }

            // Check if sterilization is currently scheduled
            bool hasScheduled = AnimalsMenuHelper.HasSterilizationScheduled(pawn);

            if (hasScheduled)
            {
                // Cancel all sterilization operations
                var billsToRemove = pawn.BillStack.Bills.Where(b => b.recipe == RecipeDefOf.Sterilize).ToList();
                foreach (var bill in billsToRemove)
                {
                    pawn.BillStack.Delete(bill);
                }
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }
            else
            {
                // Check if animal belongs to another faction
                if (pawn.HomeFaction != Faction.OfPlayer && pawn.HomeFaction != null)
                {
                    // Vanilla shows a confirmation dialog for faction animals - we'll just warn and proceed
                    TolkHelper.Speak($"Warning: {pawn.LabelShort} belongs to {pawn.HomeFaction.Name}. Scheduling sterilization anyway.");
                }

                // Schedule sterilization
                HealthCardUtility.CreateSurgeryBill(pawn, RecipeDefOf.Sterilize, null);
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }

            AnnounceCurrentCell(includeAnimalName: false);
        }

        // === Submenu System ===

        private static void OpenMasterSubmenu(Pawn pawn)
        {
            // Check if animal has learned Obedience (Guard)
            if (pawn.training?.HasLearned(TrainableDefOf.Obedience) != true)
            {
                TolkHelper.Speak($"{pawn.LabelShort} requires {TrainableDefOf.Obedience.LabelCap} training to have a master", SpeechPriority.High);
                return;
            }

            List<Pawn> colonists = AnimalsMenuHelper.GetAvailableColonists();

            // Add "None" option at the beginning
            submenuOptions.Clear();
            submenuOptions.Add(null); // null = no master
            submenuOptions.AddRange(colonists.Cast<object>());

            submenuSelectedIndex = 0;

            // Find current master in list
            if (pawn.playerSettings?.Master != null)
            {
                for (int i = 0; i < colonists.Count; i++)
                {
                    if (colonists[i] == pawn.playerSettings.Master)
                    {
                        submenuSelectedIndex = i + 1; // +1 because of "None" option
                        break;
                    }
                }
            }

            activeSubmenu = SubmenuType.Master;
            submenuTypeahead.ClearSearch();
            SoundDefOf.Click.PlayOneShotOnCamera();
            AnnounceSubmenuOption();
        }

        // Marker string for "Manage Areas" option in AllowedArea submenu
        private const string ManageAreasMarker = "ManageAreas";

        private static void OpenAllowedAreaSubmenu(Pawn pawn)
        {
            List<Area> areas = AnimalsMenuHelper.GetAvailableAreas();

            submenuOptions.Clear();
            submenuOptions.Add(ManageAreasMarker);  // "Manage Areas" first
            submenuOptions.Add(null); // null = unrestricted (second)
            submenuOptions.AddRange(areas.Cast<object>());

            submenuSelectedIndex = 1;  // Default to "Unrestricted", NOT "Manage Areas"

            // Find current area in list
            if (pawn.playerSettings?.AreaRestrictionInPawnCurrentMap != null)
            {
                for (int i = 0; i < areas.Count; i++)
                {
                    if (areas[i] == pawn.playerSettings.AreaRestrictionInPawnCurrentMap)
                    {
                        submenuSelectedIndex = i + 2;  // +2 for ManageAreas and null
                        break;
                    }
                }
            }

            activeSubmenu = SubmenuType.AllowedArea;
            submenuTypeahead.ClearSearch();
            SoundDefOf.Click.PlayOneShotOnCamera();
            AnnounceSubmenuOption();
        }

        private static void OpenMedicalCareSubmenu(Pawn pawn)
        {
            List<MedicalCareCategory> levels = AnimalsMenuHelper.GetMedicalCareLevels();

            submenuOptions.Clear();
            submenuOptions.AddRange(levels.Cast<object>());

            // Find current medical care level
            submenuSelectedIndex = 0;
            if (pawn.playerSettings != null)
            {
                for (int i = 0; i < levels.Count; i++)
                {
                    if (levels[i] == pawn.playerSettings.medCare)
                    {
                        submenuSelectedIndex = i;
                        break;
                    }
                }
            }

            activeSubmenu = SubmenuType.MedicalCare;
            submenuTypeahead.ClearSearch();
            SoundDefOf.Click.PlayOneShotOnCamera();
            AnnounceSubmenuOption();
        }


        public static void SubmenuSelectNext()
        {
            if (submenuOptions.Count == 0) return;
            // Wrap around
            submenuSelectedIndex = (submenuSelectedIndex + 1) % submenuOptions.Count;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceSubmenuOption();
        }

        public static void SubmenuSelectPrevious()
        {
            if (submenuOptions.Count == 0) return;
            // Wrap around
            submenuSelectedIndex = (submenuSelectedIndex - 1 + submenuOptions.Count) % submenuOptions.Count;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceSubmenuOption();
        }

        public static void SubmenuApply()
        {
            ApplySubmenuSelection();
        }

        public static void SubmenuCancel()
        {
            CloseSubmenu();
        }

        /// <summary>
        /// Gets a list of submenu option labels for typeahead search.
        /// </summary>
        public static List<string> GetSubmenuOptionLabels()
        {
            var labels = new List<string>();
            foreach (var option in submenuOptions)
            {
                labels.Add(GetSubmenuOptionText(option));
            }
            return labels;
        }

        /// <summary>
        /// Gets the display text for a submenu option.
        /// </summary>
        private static string GetSubmenuOptionText(object option)
        {
            if (option is string s && s == ManageAreasMarker)
            {
                return "Manage Areas";
            }
            else if (option == null)
            {
                return activeSubmenu == SubmenuType.Master ? "None" : "Unrestricted";
            }
            else if (option is Pawn colonist)
            {
                return colonist.LabelShort;
            }
            else if (option is Area area)
            {
                return area.Label;
            }
            else if (option is MedicalCareCategory medCare)
            {
                return medCare.GetLabel();
            }
            return "Unknown";
        }

        /// <summary>
        /// Sets the submenu selected index directly.
        /// </summary>
        public static void SetSubmenuSelectedIndex(int index)
        {
            if (index >= 0 && index < submenuOptions.Count)
            {
                submenuSelectedIndex = index;
            }
        }

        /// <summary>
        /// Handles character input for submenu typeahead search.
        /// </summary>
        public static void SubmenuHandleTypeahead(char c)
        {
            var labels = GetSubmenuOptionLabels();
            if (submenuTypeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    submenuSelectedIndex = newIndex;
                    AnnounceSubmenuWithSearch();
                }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{submenuTypeahead.LastFailedSearch}'");
            }
        }

        /// <summary>
        /// Handles backspace for submenu typeahead search.
        /// </summary>
        public static void SubmenuHandleBackspace()
        {
            if (!submenuTypeahead.HasActiveSearch)
                return;

            var labels = GetSubmenuOptionLabels();
            if (submenuTypeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                    submenuSelectedIndex = newIndex;
                AnnounceSubmenuWithSearch();
            }
        }

        /// <summary>
        /// Announces the current submenu selection with search context if active.
        /// </summary>
        public static void AnnounceSubmenuWithSearch()
        {
            if (submenuOptions.Count == 0) return;

            object selectedOption = submenuOptions[submenuSelectedIndex];
            string optionText = GetSubmenuOptionText(selectedOption);
            string position = MenuHelper.FormatPosition(submenuSelectedIndex, submenuOptions.Count);

            string announcement = $"{optionText}. {position}";

            // Add search context if active
            if (submenuTypeahead.HasActiveSearch)
            {
                announcement += $", match {submenuTypeahead.CurrentMatchPosition} of {submenuTypeahead.MatchCount} for '{submenuTypeahead.SearchBuffer}'";
            }

            TolkHelper.Speak(announcement);
        }

        private static void AnnounceSubmenuOption()
        {
            if (submenuOptions.Count == 0) return;

            object selectedOption = submenuOptions[submenuSelectedIndex];
            string optionText = GetSubmenuOptionText(selectedOption);

            string announcement = $"{optionText} ({MenuHelper.FormatPosition(submenuSelectedIndex, submenuOptions.Count)})";
            TolkHelper.Speak(announcement);
        }

        private static void ApplySubmenuSelection()
        {
            if (animalsList.Count == 0 || submenuOptions.Count == 0) return;

            Pawn currentAnimal = animalsList[tableHelper.CurrentRowIndex];
            object selectedOption = submenuOptions[submenuSelectedIndex];

            switch (activeSubmenu)
            {
                case SubmenuType.Master:
                    if (currentAnimal.playerSettings != null)
                    {
                        currentAnimal.playerSettings.Master = selectedOption as Pawn;
                    }
                    break;

                case SubmenuType.AllowedArea:
                    // Check if "Manage Areas" was selected
                    if (selectedOption is string marker && marker == ManageAreasMarker)
                    {
                        int savedAnimalIndex = tableHelper.CurrentRowIndex;
                        int savedColumn = tableHelper.CurrentColumnIndex;
                        Map currentMap = Find.CurrentMap;
                        CloseSubmenu();

                        // Callback that returns to Animals submenu when Manage Areas closes
                        Action returnToSubmenuCallback = () => {
                            if (savedAnimalIndex < animalsList.Count)
                            {
                                tableHelper.CurrentRowIndex = savedAnimalIndex;
                                tableHelper.CurrentColumnIndex = savedColumn;
                                OpenAllowedAreaSubmenu(animalsList[savedAnimalIndex]);
                                submenuSelectedIndex = 0;  // Manage Areas
                                AnnounceSubmenuOption();
                            }
                        };

                        // Callback for when returning from placement (Expand/Shrink)
                        // This reopens Manage Areas instead of going back to submenu
                        Action reopenManageAreasCallback = () => {
                            // Deselect the designator since we're done with placement
                            Find.DesignatorManager?.Deselect();

                            if (currentMap != null && Find.CurrentMap == currentMap)
                            {
                                // Reopen Manage Areas with the return-to-submenu callback
                                WindowlessAreaState.Open(currentMap, returnToSubmenuCallback);
                            }
                            else
                            {
                                // Map changed, just return to submenu
                                returnToSubmenuCallback();
                            }
                        };

                        WindowlessAreaState.Open(currentMap, reopenManageAreasCallback);
                        return;  // Don't close submenu normally - it was already closed
                    }

                    if (currentAnimal.playerSettings != null)
                    {
                        Area area = selectedOption as Area;
                        currentAnimal.playerSettings.AreaRestrictionInPawnCurrentMap = area;
                        lastAppliedArea = area;  // Store for bulk operations
                    }
                    break;

                case SubmenuType.MedicalCare:
                    if (currentAnimal.playerSettings != null && selectedOption is MedicalCareCategory medCare)
                    {
                        currentAnimal.playerSettings.medCare = medCare;
                    }
                    break;
            }

            SoundDefOf.Click.PlayOneShotOnCamera();
            CloseSubmenu();
            AnnounceCurrentCell(includeAnimalName: false);
        }

        private static void CloseSubmenu()
        {
            activeSubmenu = SubmenuType.None;
            submenuOptions.Clear();
            submenuSelectedIndex = 0;
            submenuTypeahead.ClearSearch();
        }

        public static void ToggleSortByCurrentColumn()
        {
            animalsList = tableHelper.ToggleSortByCurrentColumn(animalsList, out string direction).ToList();

            string columnName = tableHelper.GetCurrentColumnName();

            SoundDefOf.Click.PlayOneShotOnCamera();
            TolkHelper.Speak($"Sorted by {columnName} ({direction})");

            // Announce current cell after sorting (include animal name since position may have changed)
            AnnounceCurrentCell(includeAnimalName: true);
        }

        #region Typeahead Search

        /// <summary>
        /// Gets a list of animal names for typeahead search.
        /// </summary>
        public static List<string> GetItemLabels()
        {
            return tableHelper.GetItemLabels(animalsList);
        }

        /// <summary>
        /// Sets the current animal index directly.
        /// </summary>
        public static void SetCurrentAnimalIndex(int index)
        {
            if (index >= 0 && index < animalsList.Count)
            {
                tableHelper.CurrentRowIndex = index;
            }
        }

        /// <summary>
        /// Handles character input for typeahead search.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (tableHelper.HandleTypeahead(c, animalsList, out _))
            {
                AnnounceWithSearch();
            }
            else
            {
                TolkHelper.Speak($"No matches for '{tableHelper.Typeahead.LastFailedSearch}'");
            }
        }

        /// <summary>
        /// Handles backspace for typeahead search.
        /// </summary>
        public static void HandleBackspace()
        {
            if (!tableHelper.Typeahead.HasActiveSearch)
                return;

            tableHelper.HandleBackspace(animalsList, out _);
            AnnounceWithSearch();
        }

        /// <summary>
        /// Announces the current selection with search context if active.
        /// </summary>
        public static void AnnounceWithSearch()
        {
            if (animalsList.Count == 0)
            {
                TolkHelper.Speak("No animals");
                return;
            }

            Pawn currentAnimal = animalsList[tableHelper.CurrentRowIndex];
            string announcement = tableHelper.BuildCellAnnouncementWithSearch(currentAnimal, animalsList.Count);
            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Jumps to the first animal in the list.
        /// </summary>
        public static void JumpToFirst()
        {
            if (animalsList.Count == 0)
                return;

            tableHelper.JumpToFirst(animalsList.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: true);
        }

        /// <summary>
        /// Jumps to the last animal in the list.
        /// </summary>
        public static void JumpToLast()
        {
            if (animalsList.Count == 0)
                return;

            tableHelper.JumpToLast(animalsList.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: true);
        }

        #endregion

        #region Bulk Area Apply

        /// <summary>
        /// Checks if the current column is the AllowedArea column.
        /// </summary>
        private static bool IsOnAllowedAreaColumn()
        {
            int currentColumnIndex = tableHelper.CurrentColumnIndex;
            var columnType = AnimalsMenuHelper.GetColumnTypeAfterTraining(currentColumnIndex);
            return columnType == AnimalsMenuHelper.ColumnType.AllowedArea;
        }

        /// <summary>
        /// Applies the last-used area to the next animal and moves down.
        /// Only works when on Allowed Area column and not in a submenu.
        /// </summary>
        public static void ApplyLastAreaToNextAnimal()
        {
            if (activeSubmenu != SubmenuType.None) return;
            if (!IsOnAllowedAreaColumn()) return;
            if (animalsList.Count <= 1) return;

            // Move to next animal
            tableHelper.SelectNextRow(animalsList.Count);
            Pawn animal = animalsList[tableHelper.CurrentRowIndex];

            // Apply last area
            if (animal.playerSettings != null)
            {
                animal.playerSettings.AreaRestrictionInPawnCurrentMap = lastAppliedArea;
            }

            SoundDefOf.Click.PlayOneShotOnCamera();

            string areaName = lastAppliedArea?.Label ?? "Unrestricted";
            string position = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, animalsList.Count);
            TolkHelper.Speak($"{animal.LabelShort}: {areaName} applied. {position}");
        }

        /// <summary>
        /// Applies the last-used area to the previous animal and moves up.
        /// Only works when on Allowed Area column and not in a submenu.
        /// </summary>
        public static void ApplyLastAreaToPreviousAnimal()
        {
            if (activeSubmenu != SubmenuType.None) return;
            if (!IsOnAllowedAreaColumn()) return;
            if (animalsList.Count <= 1) return;

            // Move to previous animal
            tableHelper.SelectPreviousRow(animalsList.Count);
            Pawn animal = animalsList[tableHelper.CurrentRowIndex];

            // Apply last area
            if (animal.playerSettings != null)
            {
                animal.playerSettings.AreaRestrictionInPawnCurrentMap = lastAppliedArea;
            }

            SoundDefOf.Click.PlayOneShotOnCamera();

            string areaName = lastAppliedArea?.Label ?? "Unrestricted";
            string position = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, animalsList.Count);
            TolkHelper.Speak($"{animal.LabelShort}: {areaName} applied. {position}");
        }

        #endregion

        #region Painting

        /// <summary>
        /// Paints the current cell's value to the next row and moves down.
        /// </summary>
        public static void PaintDown()
        {
            if (activeSubmenu != SubmenuType.None) return;
            if (animalsList.Count <= 1) return;

            int col = tableHelper.CurrentColumnIndex;
            if (!AnimalsMenuHelper.CanPaintColumn(col))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Cannot paint this column");
                return;
            }

            Pawn sourcePawn = animalsList[tableHelper.CurrentRowIndex];
            var columnType = AnimalsMenuHelper.GetColumnTypeAfterTraining(col);

            if (columnType == AnimalsMenuHelper.ColumnType.AllowedArea)
            {
                lastAppliedArea = sourcePawn.playerSettings?.AreaRestrictionInPawnCurrentMap;
                tableHelper.SelectNextRow(animalsList.Count);
                Pawn target = animalsList[tableHelper.CurrentRowIndex];
                string areaName = lastAppliedArea?.Label ?? "Unrestricted";
                string position = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, animalsList.Count);
                Area targetArea = target.playerSettings?.AreaRestrictionInPawnCurrentMap;
                if (targetArea == lastAppliedArea)
                {
                    TolkHelper.Speak($"{target.LabelShort}: already set to {areaName}. {position}");
                }
                else
                {
                    if (target.playerSettings != null)
                        target.playerSettings.AreaRestrictionInPawnCurrentMap = lastAppliedArea;
                    SoundDefOf.Designate_DragStandard_Changed_NoCam.PlayOneShotOnCamera();
                    TolkHelper.Speak($"{target.LabelShort}: {areaName} applied. {position}");
                }
                return;
            }

            if (columnType == AnimalsMenuHelper.ColumnType.Master)
            {
                Pawn sourceMaster = sourcePawn.playerSettings?.Master;
                string masterName = sourceMaster?.LabelShort ?? "None".Translate().Resolve();
                tableHelper.SelectNextRow(animalsList.Count);
                Pawn target = animalsList[tableHelper.CurrentRowIndex];
                string position = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, animalsList.Count);
                if (target.playerSettings == null || target.training?.HasLearned(TrainableDefOf.Obedience) != true)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak($"{target.LabelShort}: requires {TrainableDefOf.Obedience.LabelCap}. {position}");
                }
                else if (target.playerSettings.Master == sourceMaster)
                {
                    TolkHelper.Speak($"{target.LabelShort}: Master already {masterName}. {position}");
                }
                else
                {
                    target.playerSettings.Master = sourceMaster;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    TolkHelper.Speak($"{target.LabelShort}: Master {masterName} applied. {position}");
                }
                return;
            }

            if (columnType == AnimalsMenuHelper.ColumnType.MedicalCare)
            {
                var sourceCare = sourcePawn.playerSettings?.medCare ?? MedicalCareCategory.NoCare;
                string careLabel = sourceCare.GetLabel();
                tableHelper.SelectNextRow(animalsList.Count);
                Pawn target = animalsList[tableHelper.CurrentRowIndex];
                string position = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, animalsList.Count);
                if (target.playerSettings == null)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak($"{target.LabelShort}: cannot set medical care. {position}");
                }
                else if (target.playerSettings.medCare == sourceCare)
                {
                    TolkHelper.Speak($"{target.LabelShort}: Medical care already {careLabel}. {position}");
                }
                else
                {
                    target.playerSettings.medCare = sourceCare;
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    TolkHelper.Speak($"{target.LabelShort}: Medical care {careLabel} applied. {position}");
                }
                return;
            }

            bool brushValue = AnimalsMenuHelper.GetPaintableValue(sourcePawn, col);
            tableHelper.SelectNextRow(animalsList.Count);
            Pawn targetPawn = animalsList[tableHelper.CurrentRowIndex];

            string colName = AnimalsMenuHelper.GetColumnName(col);
            string valueLabel = AnimalsMenuHelper.GetPaintValueLabel(col, brushValue);
            string pos = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, animalsList.Count);

            bool targetValue = AnimalsMenuHelper.GetPaintableValue(targetPawn, col);
            if (targetValue == brushValue)
            {
                TolkHelper.Speak($"{targetPawn.LabelShort}: {colName} already {valueLabel}. {pos}");
                return;
            }

            bool applied = AnimalsMenuHelper.SetPaintableValue(targetPawn, col, brushValue);
            SoundDef sound = applied
                ? AnimalsMenuHelper.GetPaintSound(col, brushValue)
                : SoundDefOf.ClickReject;
            sound.PlayOneShotOnCamera();

            TolkHelper.Speak($"{targetPawn.LabelShort}: {colName} {valueLabel}. {pos}");
        }

        /// <summary>
        /// Paints the current cell's value to the previous row and moves up.
        /// </summary>
        public static void PaintUp()
        {
            if (activeSubmenu != SubmenuType.None) return;
            if (animalsList.Count <= 1) return;

            int col = tableHelper.CurrentColumnIndex;
            if (!AnimalsMenuHelper.CanPaintColumn(col))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Cannot paint this column");
                return;
            }

            Pawn sourcePawn = animalsList[tableHelper.CurrentRowIndex];
            var columnType = AnimalsMenuHelper.GetColumnTypeAfterTraining(col);

            if (columnType == AnimalsMenuHelper.ColumnType.AllowedArea)
            {
                lastAppliedArea = sourcePawn.playerSettings?.AreaRestrictionInPawnCurrentMap;
                tableHelper.SelectPreviousRow(animalsList.Count);
                Pawn target = animalsList[tableHelper.CurrentRowIndex];
                string areaName = lastAppliedArea?.Label ?? "Unrestricted";
                string position = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, animalsList.Count);
                Area targetArea = target.playerSettings?.AreaRestrictionInPawnCurrentMap;
                if (targetArea == lastAppliedArea)
                {
                    TolkHelper.Speak($"{target.LabelShort}: already set to {areaName}. {position}");
                }
                else
                {
                    if (target.playerSettings != null)
                        target.playerSettings.AreaRestrictionInPawnCurrentMap = lastAppliedArea;
                    SoundDefOf.Designate_DragStandard_Changed_NoCam.PlayOneShotOnCamera();
                    TolkHelper.Speak($"{target.LabelShort}: {areaName} applied. {position}");
                }
                return;
            }

            if (columnType == AnimalsMenuHelper.ColumnType.Master)
            {
                Pawn sourceMaster = sourcePawn.playerSettings?.Master;
                string masterName = sourceMaster?.LabelShort ?? "None".Translate().Resolve();
                tableHelper.SelectPreviousRow(animalsList.Count);
                Pawn target = animalsList[tableHelper.CurrentRowIndex];
                string position = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, animalsList.Count);
                if (target.playerSettings == null || target.training?.HasLearned(TrainableDefOf.Obedience) != true)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak($"{target.LabelShort}: requires {TrainableDefOf.Obedience.LabelCap}. {position}");
                }
                else if (target.playerSettings.Master == sourceMaster)
                {
                    TolkHelper.Speak($"{target.LabelShort}: Master already {masterName}. {position}");
                }
                else
                {
                    target.playerSettings.Master = sourceMaster;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    TolkHelper.Speak($"{target.LabelShort}: Master {masterName} applied. {position}");
                }
                return;
            }

            if (columnType == AnimalsMenuHelper.ColumnType.MedicalCare)
            {
                var sourceCare = sourcePawn.playerSettings?.medCare ?? MedicalCareCategory.NoCare;
                string careLabel = sourceCare.GetLabel();
                tableHelper.SelectPreviousRow(animalsList.Count);
                Pawn target = animalsList[tableHelper.CurrentRowIndex];
                string position = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, animalsList.Count);
                if (target.playerSettings == null)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak($"{target.LabelShort}: cannot set medical care. {position}");
                }
                else if (target.playerSettings.medCare == sourceCare)
                {
                    TolkHelper.Speak($"{target.LabelShort}: Medical care already {careLabel}. {position}");
                }
                else
                {
                    target.playerSettings.medCare = sourceCare;
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    TolkHelper.Speak($"{target.LabelShort}: Medical care {careLabel} applied. {position}");
                }
                return;
            }

            bool brushValue = AnimalsMenuHelper.GetPaintableValue(sourcePawn, col);
            tableHelper.SelectPreviousRow(animalsList.Count);
            Pawn targetPawn = animalsList[tableHelper.CurrentRowIndex];

            string colName = AnimalsMenuHelper.GetColumnName(col);
            string valueLabel = AnimalsMenuHelper.GetPaintValueLabel(col, brushValue);
            string pos = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, animalsList.Count);

            bool targetValue = AnimalsMenuHelper.GetPaintableValue(targetPawn, col);
            if (targetValue == brushValue)
            {
                TolkHelper.Speak($"{targetPawn.LabelShort}: {colName} already {valueLabel}. {pos}");
                return;
            }

            bool applied = AnimalsMenuHelper.SetPaintableValue(targetPawn, col, brushValue);
            SoundDef sound = applied
                ? AnimalsMenuHelper.GetPaintSound(col, brushValue)
                : SoundDefOf.ClickReject;
            sound.PlayOneShotOnCamera();

            TolkHelper.Speak($"{targetPawn.LabelShort}: {colName} {valueLabel}. {pos}");
        }

        /// <summary>
        /// Bulk paints the current value from the current row to the last row.
        /// </summary>
        public static void PaintToLast()
        {
            PaintBulk(towardFirst: false, entireColumn: false);
        }

        /// <summary>
        /// Bulk paints the current value from the current row to the first row.
        /// </summary>
        public static void PaintToFirst()
        {
            PaintBulk(towardFirst: true, entireColumn: false);
        }

        /// <summary>
        /// Paints the current value to the entire column.
        /// Cursor moves to first row (Ctrl+Shift+Home) or last row (Ctrl+Shift+End).
        /// </summary>
        public static void PaintEntireColumn(bool towardFirst)
        {
            PaintBulk(towardFirst, entireColumn: true);
        }

        private static void PaintBulk(bool towardFirst, bool entireColumn)
        {
            if (activeSubmenu != SubmenuType.None) return;
            if (animalsList.Count == 0) return;

            int col = tableHelper.CurrentColumnIndex;
            if (!AnimalsMenuHelper.CanPaintColumn(col))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Cannot paint this column");
                return;
            }

            var columnType = AnimalsMenuHelper.GetColumnTypeAfterTraining(col);
            int currentRow = tableHelper.CurrentRowIndex;
            string colName = AnimalsMenuHelper.GetColumnName(col);

            int startRow, endRow;
            if (entireColumn)
            {
                startRow = 0;
                endRow = animalsList.Count - 1;
            }
            else if (towardFirst)
            {
                startRow = 0;
                endRow = currentRow;
            }
            else
            {
                startRow = currentRow;
                endRow = animalsList.Count - 1;
            }

            // AllowedArea painting
            if (columnType == AnimalsMenuHelper.ColumnType.AllowedArea)
            {
                Pawn source = animalsList[currentRow];
                lastAppliedArea = source.playerSettings?.AreaRestrictionInPawnCurrentMap;
                string areaName = lastAppliedArea?.Label ?? "Unrestricted";

                var changedNames = new List<string>();
                for (int i = startRow; i <= endRow; i++)
                {
                    Pawn pawn = animalsList[i];
                    if (pawn.playerSettings == null) continue;
                    if (pawn.playerSettings.AreaRestrictionInPawnCurrentMap != lastAppliedArea)
                    {
                        pawn.playerSettings.AreaRestrictionInPawnCurrentMap = lastAppliedArea;
                        changedNames.Add(pawn.LabelShort);
                    }
                }

                tableHelper.CurrentRowIndex = towardFirst ? startRow : endRow;

                if (changedNames.Count > 0)
                {
                    BulkSoundQueue.Queue(changedNames.Count, SoundDefOf.Designate_DragStandard_Changed_NoCam);
                    TolkHelper.Speak($"Painted allowed area to {areaName} for {MenuHelper.FormatNameList(changedNames)}");
                }
                else
                {
                    TolkHelper.Speak($"Allowed area already {areaName} for all animals");
                }
                return;
            }

            // Master painting
            if (columnType == AnimalsMenuHelper.ColumnType.Master)
            {
                Pawn source = animalsList[currentRow];
                Pawn sourceMaster = source.playerSettings?.Master;
                string masterName = sourceMaster?.LabelShort ?? "None".Translate().Resolve();

                var changedNames = new List<string>();
                for (int i = startRow; i <= endRow; i++)
                {
                    Pawn pawn = animalsList[i];
                    if (pawn.playerSettings == null || pawn.training?.HasLearned(TrainableDefOf.Obedience) != true)
                        continue;
                    if (pawn.playerSettings.Master != sourceMaster)
                    {
                        pawn.playerSettings.Master = sourceMaster;
                        changedNames.Add(pawn.LabelShort);
                    }
                }

                tableHelper.CurrentRowIndex = towardFirst ? startRow : endRow;

                if (changedNames.Count > 0)
                {
                    BulkSoundQueue.Queue(changedNames.Count, SoundDefOf.Click);
                    TolkHelper.Speak($"Painted Master to {masterName} for {MenuHelper.FormatNameList(changedNames)}");
                }
                else
                {
                    TolkHelper.Speak($"Master already {masterName} for all animals");
                }
                return;
            }

            // MedicalCare painting
            if (columnType == AnimalsMenuHelper.ColumnType.MedicalCare)
            {
                Pawn source = animalsList[currentRow];
                var sourceCare = source.playerSettings?.medCare ?? MedicalCareCategory.NoCare;
                string careLabel = sourceCare.GetLabel();

                var changedNames = new List<string>();
                for (int i = startRow; i <= endRow; i++)
                {
                    Pawn pawn = animalsList[i];
                    if (pawn.playerSettings == null) continue;
                    if (pawn.playerSettings.medCare != sourceCare)
                    {
                        pawn.playerSettings.medCare = sourceCare;
                        changedNames.Add(pawn.LabelShort);
                    }
                }

                tableHelper.CurrentRowIndex = towardFirst ? startRow : endRow;

                if (changedNames.Count > 0)
                {
                    BulkSoundQueue.Queue(changedNames.Count, SoundDefOf.Tick_High);
                    TolkHelper.Speak($"Painted Medical care to {careLabel} for {MenuHelper.FormatNameList(changedNames)}");
                }
                else
                {
                    TolkHelper.Speak($"Medical care already {careLabel} for all animals");
                }
                return;
            }

            // Checkbox/boolean painting
            Pawn sourcePawn = animalsList[currentRow];
            bool brushValue = AnimalsMenuHelper.GetPaintableValue(sourcePawn, col);
            string valueLabel = AnimalsMenuHelper.GetPaintValueLabel(col, brushValue);
            SoundDef paintSound = AnimalsMenuHelper.GetPaintSound(col, brushValue);

            var changed = new List<string>();
            for (int i = startRow; i <= endRow; i++)
            {
                Pawn pawn = animalsList[i];
                bool currentValue = AnimalsMenuHelper.GetPaintableValue(pawn, col);
                if (currentValue != brushValue)
                {
                    if (AnimalsMenuHelper.SetPaintableValue(pawn, col, brushValue))
                        changed.Add(pawn.LabelShort);
                }
            }

            tableHelper.CurrentRowIndex = towardFirst ? startRow : endRow;

            if (changed.Count > 0)
            {
                BulkSoundQueue.Queue(changed.Count, paintSound);
                TolkHelper.Speak($"Painted {colName} {valueLabel} for {MenuHelper.FormatNameList(changed)}");
            }
            else
            {
                TolkHelper.Speak($"{colName} already {valueLabel} for all animals");
            }
        }

        #endregion
    }
}
