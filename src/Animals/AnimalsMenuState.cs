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
        private static int currentAnimalIndex = 0;
        private static int currentColumnIndex = 0;
        private static int sortColumnIndex = 0; // Column used for sorting (default: Name)
        private static bool sortDescending = false;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        // Submenu state
        private enum SubmenuType { None, Master, AllowedArea, MedicalCare, FoodRestriction }
        private static SubmenuType activeSubmenu = SubmenuType.None;
        private static int submenuSelectedIndex = 0;
        private static List<object> submenuOptions = new List<object>();
        private static TypeaheadSearchHelper submenuTypeahead = new TypeaheadSearchHelper();

        public static TypeaheadSearchHelper Typeahead => typeahead;
        public static TypeaheadSearchHelper SubmenuTypeahead => submenuTypeahead;
        public static int CurrentAnimalIndex => currentAnimalIndex;
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

            // Apply default sort (by name)
            animalsList = AnimalsMenuHelper.SortAnimalsByColumn(animalsList, sortColumnIndex, sortDescending);

            currentAnimalIndex = 0;
            currentColumnIndex = 0;
            activeSubmenu = SubmenuType.None;
            typeahead.ClearSearch();
            IsActive = true;

            SoundDefOf.TabOpen.PlayOneShotOnCamera();

            string announcement = $"Animals menu, {animalsList.Count} animals";
            TolkHelper.Speak(announcement);
            AnnounceCurrentCell(includeAnimalName: true);
        }

        public static void Close()
        {
            IsActive = false;
            activeSubmenu = SubmenuType.None;
            animalsList.Clear();
            typeahead.ClearSearch();
            SoundDefOf.TabClose.PlayOneShotOnCamera();
            TolkHelper.Speak("Animals menu closed");
        }

        public static void SelectNextAnimal()
        {
            if (animalsList.Count == 0) return;

            // Wrap around to first when at end
            currentAnimalIndex = (currentAnimalIndex + 1) % animalsList.Count;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: true);
        }

        public static void SelectPreviousAnimal()
        {
            if (animalsList.Count == 0) return;

            // Wrap around to last when at start
            currentAnimalIndex = (currentAnimalIndex - 1 + animalsList.Count) % animalsList.Count;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: true);
        }

        public static void SelectNextColumn()
        {
            int totalColumns = AnimalsMenuHelper.GetTotalColumnCount();
            // Wrap around to first column when at end
            currentColumnIndex = (currentColumnIndex + 1) % totalColumns;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: false);
        }

        public static void SelectPreviousColumn()
        {
            int totalColumns = AnimalsMenuHelper.GetTotalColumnCount();
            // Wrap around to last column when at start
            currentColumnIndex = (currentColumnIndex - 1 + totalColumns) % totalColumns;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: false);
        }

        private static void AnnounceCurrentCell(bool includeAnimalName = true)
        {
            if (animalsList.Count == 0) return;

            Pawn currentAnimal = animalsList[currentAnimalIndex];
            string columnName = AnimalsMenuHelper.GetColumnName(currentColumnIndex);
            string columnValue = AnimalsMenuHelper.GetColumnValue(currentAnimal, currentColumnIndex);
            string position = MenuHelper.FormatPosition(currentAnimalIndex, animalsList.Count);

            string announcement;
            if (includeAnimalName)
            {
                string animalName = AnimalsMenuHelper.GetAnimalName(currentAnimal);
                announcement = $"{animalName} - {columnName}: {columnValue}. {position}";
            }
            else
            {
                announcement = $"{columnName}: {columnValue}";
            }

            TolkHelper.Speak(announcement);
        }

        public static void InteractWithCurrentCell()
        {
            if (animalsList.Count == 0) return;

            Pawn currentAnimal = animalsList[currentAnimalIndex];

            if (!AnimalsMenuHelper.IsColumnInteractive(currentColumnIndex))
            {
                // Just re-announce for non-interactive columns
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentCell(includeAnimalName: false);
                return;
            }

            // Handle interaction based on column type
            if (currentColumnIndex < 8) // Fixed columns before training
            {
                switch ((AnimalsMenuHelper.ColumnType)currentColumnIndex)
                {
                    case AnimalsMenuHelper.ColumnType.Master:
                        OpenMasterSubmenu(currentAnimal);
                        break;
                    case AnimalsMenuHelper.ColumnType.Slaughter:
                        ToggleSlaughter(currentAnimal);
                        break;
                }
            }
            else if (currentColumnIndex < 8 + AnimalsMenuHelper.GetAllTrainables().Count)
            {
                // Training column
                ToggleTraining(currentAnimal, currentColumnIndex);
            }
            else
            {
                // Fixed columns after training
                int fixedIndex = currentColumnIndex - 8 - AnimalsMenuHelper.GetAllTrainables().Count;
                AnimalsMenuHelper.ColumnType type = (AnimalsMenuHelper.ColumnType)(8 + fixedIndex);

                switch (type)
                {
                    case AnimalsMenuHelper.ColumnType.FollowDrafted:
                        ToggleFollowDrafted(currentAnimal);
                        break;
                    case AnimalsMenuHelper.ColumnType.FollowFieldwork:
                        ToggleFollowFieldwork(currentAnimal);
                        break;
                    case AnimalsMenuHelper.ColumnType.AllowedArea:
                        OpenAllowedAreaSubmenu(currentAnimal);
                        break;
                    case AnimalsMenuHelper.ColumnType.MedicalCare:
                        OpenMedicalCareSubmenu(currentAnimal);
                        break;
                    case AnimalsMenuHelper.ColumnType.FoodRestriction:
                        OpenFoodRestrictionSubmenu(currentAnimal);
                        break;
                    case AnimalsMenuHelper.ColumnType.ReleaseToWild:
                        ToggleReleaseToWild(currentAnimal);
                        break;
                }
            }
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

        // === Submenu System ===

        private static void OpenMasterSubmenu(Pawn pawn)
        {
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

        private static void OpenAllowedAreaSubmenu(Pawn pawn)
        {
            List<Area> areas = AnimalsMenuHelper.GetAvailableAreas();

            submenuOptions.Clear();
            submenuOptions.Add(null); // null = unrestricted
            submenuOptions.AddRange(areas.Cast<object>());

            submenuSelectedIndex = 0;

            // Find current area in list
            if (pawn.playerSettings?.AreaRestrictionInPawnCurrentMap != null)
            {
                for (int i = 0; i < areas.Count; i++)
                {
                    if (areas[i] == pawn.playerSettings.AreaRestrictionInPawnCurrentMap)
                    {
                        submenuSelectedIndex = i + 1;
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

        private static void OpenFoodRestrictionSubmenu(Pawn pawn)
        {
            List<FoodPolicy> policies = AnimalsMenuHelper.GetFoodPolicies();

            submenuOptions.Clear();
            submenuOptions.AddRange(policies.Cast<object>());

            // Find current food restriction
            submenuSelectedIndex = 0;
            if (pawn.foodRestriction?.CurrentFoodPolicy != null)
            {
                for (int i = 0; i < policies.Count; i++)
                {
                    if (policies[i] == pawn.foodRestriction.CurrentFoodPolicy)
                    {
                        submenuSelectedIndex = i;
                        break;
                    }
                }
            }

            activeSubmenu = SubmenuType.FoodRestriction;
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
            if (option == null)
            {
                return activeSubmenu == SubmenuType.Master ? "None" : "Unrestricted";
            }
            else if (option is Pawn colonist)
            {
                return colonist.Name.ToStringShort;
            }
            else if (option is Area area)
            {
                return area.Label;
            }
            else if (option is MedicalCareCategory medCare)
            {
                return medCare.GetLabel();
            }
            else if (option is FoodPolicy foodPolicy)
            {
                return foodPolicy.label;
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

            string optionText = "Unknown";

            object selectedOption = submenuOptions[submenuSelectedIndex];

            if (selectedOption == null)
            {
                optionText = activeSubmenu == SubmenuType.Master ? "None" : "Unrestricted";
            }
            else if (selectedOption is Pawn colonist)
            {
                optionText = colonist.Name.ToStringShort;
            }
            else if (selectedOption is Area area)
            {
                optionText = area.Label;
            }
            else if (selectedOption is MedicalCareCategory medCare)
            {
                optionText = medCare.GetLabel();
            }
            else if (selectedOption is FoodPolicy foodPolicy)
            {
                optionText = foodPolicy.label;
            }

            string announcement = $"{optionText} ({MenuHelper.FormatPosition(submenuSelectedIndex, submenuOptions.Count)})";
            TolkHelper.Speak(announcement);
        }

        private static void ApplySubmenuSelection()
        {
            if (animalsList.Count == 0 || submenuOptions.Count == 0) return;

            Pawn currentAnimal = animalsList[currentAnimalIndex];
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
                    if (currentAnimal.playerSettings != null)
                    {
                        currentAnimal.playerSettings.AreaRestrictionInPawnCurrentMap = selectedOption as Area;
                    }
                    break;

                case SubmenuType.MedicalCare:
                    if (currentAnimal.playerSettings != null && selectedOption is MedicalCareCategory medCare)
                    {
                        currentAnimal.playerSettings.medCare = medCare;
                    }
                    break;

                case SubmenuType.FoodRestriction:
                    if (currentAnimal.foodRestriction != null && selectedOption is FoodPolicy foodPolicy)
                    {
                        currentAnimal.foodRestriction.CurrentFoodPolicy = foodPolicy;
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
            if (sortColumnIndex == currentColumnIndex)
            {
                // Same column - toggle direction
                sortDescending = !sortDescending;
            }
            else
            {
                // New column - sort ascending
                sortColumnIndex = currentColumnIndex;
                sortDescending = false;
            }

            // Re-sort the list
            animalsList = AnimalsMenuHelper.SortAnimalsByColumn(animalsList, sortColumnIndex, sortDescending);

            // Try to keep the same animal selected
            Pawn currentAnimal = null;
            if (currentAnimalIndex < animalsList.Count)
            {
                currentAnimal = animalsList[currentAnimalIndex];
            }

            if (currentAnimal != null)
            {
                currentAnimalIndex = animalsList.IndexOf(currentAnimal);
                if (currentAnimalIndex < 0) currentAnimalIndex = 0;
            }
            else
            {
                currentAnimalIndex = 0;
            }

            string direction = sortDescending ? "descending" : "ascending";
            string columnName = AnimalsMenuHelper.GetColumnName(sortColumnIndex);

            SoundDefOf.Click.PlayOneShotOnCamera();
            TolkHelper.Speak($"Sorted by {columnName} ({direction})");

            // Announce current cell after sorting (include animal name since position may have changed)
            AnnounceCurrentCell(includeAnimalName: true);
        }

        #region Typeahead Search

        /// <summary>
        /// Gets a list of animal names for typeahead search.
        /// Searches by animal name only, not by column values.
        /// </summary>
        public static List<string> GetItemLabels()
        {
            return animalsList.Select(p => AnimalsMenuHelper.GetAnimalName(p)).ToList();
        }

        /// <summary>
        /// Sets the current animal index directly.
        /// </summary>
        public static void SetCurrentAnimalIndex(int index)
        {
            if (index >= 0 && index < animalsList.Count)
            {
                currentAnimalIndex = index;
            }
        }

        /// <summary>
        /// Handles character input for typeahead search.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            var labels = GetItemLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    currentAnimalIndex = newIndex;
                    AnnounceWithSearch();
                }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
            }
        }

        /// <summary>
        /// Handles backspace for typeahead search.
        /// </summary>
        public static void HandleBackspace()
        {
            if (!typeahead.HasActiveSearch)
                return;

            var labels = GetItemLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                    currentAnimalIndex = newIndex;
                AnnounceWithSearch();
            }
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

            Pawn currentAnimal = animalsList[currentAnimalIndex];
            string animalName = AnimalsMenuHelper.GetAnimalName(currentAnimal);
            string columnName = AnimalsMenuHelper.GetColumnName(currentColumnIndex);
            string columnValue = AnimalsMenuHelper.GetColumnValue(currentAnimal, currentColumnIndex);
            string position = MenuHelper.FormatPosition(currentAnimalIndex, animalsList.Count);

            string announcement = $"{animalName} - {columnName}: {columnValue}. {position}";

            // Add search context if active
            if (typeahead.HasActiveSearch)
            {
                announcement += $", match {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} for '{typeahead.SearchBuffer}'";
            }

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Jumps to the first animal in the list.
        /// </summary>
        public static void JumpToFirst()
        {
            if (animalsList.Count == 0)
                return;

            currentAnimalIndex = 0;
            typeahead.ClearSearch();
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

            currentAnimalIndex = animalsList.Count - 1;
            typeahead.ClearSearch();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeAnimalName: true);
        }

        #endregion
    }
}
