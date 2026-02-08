using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// State handler for Health tab operations and medical settings.
    /// Accessed via inspection tree: Health → Operations or Health → Health Settings.
    /// </summary>
    public static class HealthTabState
    {
        private enum MenuLevel
        {
            MedicalSettingsList,   // List medical settings
            MedicalSettingChange,  // Change a medical setting
            OperationsList,        // List operations
            OperationActions,      // Actions for operation
            AddRecipeList,         // List available recipes to add
            SelectBodyPart,        // Select body part for recipe
        }

        private static bool isActive = false;
        private static Pawn currentPawn = null;

        private static MenuLevel currentLevel = MenuLevel.OperationsList;

        // Medical Settings
        private static int medicalSettingIndex = 0;
        private static readonly List<string> medicalSettings = new List<string> { "Food Restriction", "Medical Care", "Self-Tend" };
        private static string currentSettingName = "";
        private static List<FoodPolicy> availableFoodRestrictions = new List<FoodPolicy>();
        private static List<MedicalCareCategory> availableMedicalCare = new List<MedicalCareCategory>();
        private static int settingChoiceIndex = 0;

        // Operations
        private static List<Bill> queuedOperations = new List<Bill>();
        private static List<RecipeDef> availableRecipes = new List<RecipeDef>();
        private static RecipeDef selectedRecipe = null;
        private static List<BodyPartRecord> partsForRecipe = new List<BodyPartRecord>();
        private static int operationIndex = 0;
        private static int recipeIndex = 0;
        private static int partSelectionIndex = 0;
        private static readonly List<string> operationActions = new List<string> { "View Details", "Remove Operation", "Go Back" };
        private static int operationActionIndex = 0;

        // Typeahead search for recipe and body part lists
        private static TypeaheadSearchHelper recipeTypeahead = new TypeaheadSearchHelper();
        private static TypeaheadSearchHelper bodyPartTypeahead = new TypeaheadSearchHelper();

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens directly to the Operations section.
        /// </summary>
        public static void OpenOperations(Pawn pawn)
        {
            if (pawn == null)
                return;

            currentPawn = pawn;
            isActive = true;
            currentLevel = MenuLevel.OperationsList;
            operationIndex = 0;
            recipeTypeahead.ClearSearch();
            bodyPartTypeahead.ClearSearch();

            // Build operations list
            queuedOperations.Clear();

            if (currentPawn.BillStack != null)
            {
                queuedOperations.AddRange(currentPawn.BillStack.Bills);
            }

            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Opens directly to the Medical Settings section.
        /// </summary>
        public static void OpenMedicalSettings(Pawn pawn)
        {
            if (pawn == null)
                return;

            currentPawn = pawn;
            isActive = true;
            currentLevel = MenuLevel.MedicalSettingsList;
            medicalSettingIndex = 0;

            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Closes the health tab.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentPawn = null;
            recipeTypeahead.ClearSearch();
            bodyPartTypeahead.ClearSearch();
            SoundDefOf.TabClose.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Whether a typeahead search is active for the current menu level.
        /// </summary>
        private static bool HasActiveSearch =>
            (currentLevel == MenuLevel.AddRecipeList && recipeTypeahead.HasActiveSearch) ||
            (currentLevel == MenuLevel.SelectBodyPart && bodyPartTypeahead.HasActiveSearch);

        /// <summary>
        /// Handles keyboard input.
        /// </summary>
        public static bool HandleInput(Event evt)
        {
            if (!isActive || evt.type != EventType.KeyDown)
                return false;

            KeyCode key = evt.keyCode;

            // Handle Escape - clear search first, then go back
            if (key == KeyCode.Escape)
            {
                evt.Use();
                if (HasActiveSearch)
                {
                    if (currentLevel == MenuLevel.AddRecipeList)
                        recipeTypeahead.ClearSearchAndAnnounce();
                    else if (currentLevel == MenuLevel.SelectBodyPart)
                        bodyPartTypeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentSelection();
                }
                else
                {
                    GoBack();
                }
                return true;
            }

            // Handle Up/Down - match navigation when searching, normal navigation otherwise
            if (key == KeyCode.UpArrow)
            {
                evt.Use();
                if (HasActiveSearch)
                    SelectPreviousMatch();
                else
                    SelectPrevious();
                return true;
            }

            if (key == KeyCode.DownArrow)
            {
                evt.Use();
                if (HasActiveSearch)
                    SelectNextMatch();
                else
                    SelectNext();
                return true;
            }

            // Handle Home/End
            if (key == KeyCode.Home)
            {
                evt.Use();
                NavigateHome();
                return true;
            }

            if (key == KeyCode.End)
            {
                evt.Use();
                NavigateEnd();
                return true;
            }

            // Handle Enter - drill down or execute
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                evt.Use();
                DrillDown();
                return true;
            }

            // Handle Backspace - typeahead
            if (key == KeyCode.Backspace)
            {
                if (HandleTypeaheadBackspace())
                {
                    evt.Use();
                    return true;
                }
                return false;
            }

            return false;
        }

        /// <summary>
        /// Handles character input for typeahead search.
        /// Called from UnifiedKeyboardPatch's character routing section.
        /// </summary>
        public static bool HandleCharacterInput(char c)
        {
            if (!isActive)
                return false;
            return HandleTypeaheadInput(c);
        }

        private static void SelectNext()
        {
            switch (currentLevel)
            {
                case MenuLevel.MedicalSettingsList:
                    medicalSettingIndex = MenuHelper.SelectNext(medicalSettingIndex, medicalSettings.Count);
                    break;

                case MenuLevel.MedicalSettingChange:
                    if (currentSettingName == "Food Restriction")
                        settingChoiceIndex = MenuHelper.SelectNext(settingChoiceIndex, availableFoodRestrictions.Count);
                    else if (currentSettingName == "Medical Care")
                        settingChoiceIndex = MenuHelper.SelectNext(settingChoiceIndex, availableMedicalCare.Count);
                    break;

                case MenuLevel.OperationsList:
                    int totalOps = queuedOperations.Count + 1; // +1 for "Add Operation"
                    operationIndex = MenuHelper.SelectNext(operationIndex, totalOps);
                    break;

                case MenuLevel.OperationActions:
                    operationActionIndex = MenuHelper.SelectNext(operationActionIndex, operationActions.Count);
                    break;

                case MenuLevel.AddRecipeList:
                    if (availableRecipes.Count > 0)
                    {
                        recipeTypeahead.ClearSearch();
                        recipeIndex = MenuHelper.SelectNext(recipeIndex, availableRecipes.Count);
                    }
                    break;

                case MenuLevel.SelectBodyPart:
                    if (partsForRecipe.Count > 0)
                    {
                        bodyPartTypeahead.ClearSearch();
                        partSelectionIndex = MenuHelper.SelectNext(partSelectionIndex, partsForRecipe.Count);
                    }
                    break;
            }

            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        private static void SelectPrevious()
        {
            switch (currentLevel)
            {
                case MenuLevel.MedicalSettingsList:
                    medicalSettingIndex = MenuHelper.SelectPrevious(medicalSettingIndex, medicalSettings.Count);
                    break;

                case MenuLevel.MedicalSettingChange:
                    if (currentSettingName == "Food Restriction")
                        settingChoiceIndex = MenuHelper.SelectPrevious(settingChoiceIndex, availableFoodRestrictions.Count);
                    else if (currentSettingName == "Medical Care")
                        settingChoiceIndex = MenuHelper.SelectPrevious(settingChoiceIndex, availableMedicalCare.Count);
                    break;

                case MenuLevel.OperationsList:
                    int totalOps = queuedOperations.Count + 1;
                    operationIndex = MenuHelper.SelectPrevious(operationIndex, totalOps);
                    break;

                case MenuLevel.OperationActions:
                    operationActionIndex = MenuHelper.SelectPrevious(operationActionIndex, operationActions.Count);
                    break;

                case MenuLevel.AddRecipeList:
                    if (availableRecipes.Count > 0)
                    {
                        recipeTypeahead.ClearSearch();
                        recipeIndex = MenuHelper.SelectPrevious(recipeIndex, availableRecipes.Count);
                    }
                    break;

                case MenuLevel.SelectBodyPart:
                    if (partsForRecipe.Count > 0)
                    {
                        bodyPartTypeahead.ClearSearch();
                        partSelectionIndex = MenuHelper.SelectPrevious(partSelectionIndex, partsForRecipe.Count);
                    }
                    break;
            }

            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        private static void NavigateHome()
        {
            switch (currentLevel)
            {
                case MenuLevel.MedicalSettingsList:
                    medicalSettingIndex = 0;
                    break;
                case MenuLevel.MedicalSettingChange:
                    settingChoiceIndex = 0;
                    break;
                case MenuLevel.OperationsList:
                    operationIndex = 0;
                    break;
                case MenuLevel.OperationActions:
                    operationActionIndex = 0;
                    break;
                case MenuLevel.AddRecipeList:
                    recipeTypeahead.ClearSearch();
                    if (availableRecipes.Count > 0)
                        recipeIndex = 0;
                    break;
                case MenuLevel.SelectBodyPart:
                    bodyPartTypeahead.ClearSearch();
                    if (partsForRecipe.Count > 0)
                        partSelectionIndex = 0;
                    break;
            }

            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        private static void NavigateEnd()
        {
            switch (currentLevel)
            {
                case MenuLevel.MedicalSettingsList:
                    medicalSettingIndex = medicalSettings.Count - 1;
                    break;
                case MenuLevel.MedicalSettingChange:
                    if (currentSettingName == "Food Restriction")
                        settingChoiceIndex = availableFoodRestrictions.Count - 1;
                    else if (currentSettingName == "Medical Care")
                        settingChoiceIndex = availableMedicalCare.Count - 1;
                    break;
                case MenuLevel.OperationsList:
                    operationIndex = queuedOperations.Count; // Last item is "Add Operation"
                    break;
                case MenuLevel.OperationActions:
                    operationActionIndex = operationActions.Count - 1;
                    break;
                case MenuLevel.AddRecipeList:
                    recipeTypeahead.ClearSearch();
                    if (availableRecipes.Count > 0)
                        recipeIndex = availableRecipes.Count - 1;
                    break;
                case MenuLevel.SelectBodyPart:
                    bodyPartTypeahead.ClearSearch();
                    if (partsForRecipe.Count > 0)
                        partSelectionIndex = partsForRecipe.Count - 1;
                    break;
            }

            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        private static void SelectNextMatch()
        {
            if (currentLevel == MenuLevel.AddRecipeList && recipeTypeahead.HasActiveSearch)
            {
                int next = recipeTypeahead.GetNextMatch(recipeIndex);
                if (next >= 0) recipeIndex = next;
            }
            else if (currentLevel == MenuLevel.SelectBodyPart && bodyPartTypeahead.HasActiveSearch)
            {
                int next = bodyPartTypeahead.GetNextMatch(partSelectionIndex);
                if (next >= 0) partSelectionIndex = next;
            }

            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceWithSearch();
        }

        private static void SelectPreviousMatch()
        {
            if (currentLevel == MenuLevel.AddRecipeList && recipeTypeahead.HasActiveSearch)
            {
                int prev = recipeTypeahead.GetPreviousMatch(recipeIndex);
                if (prev >= 0) recipeIndex = prev;
            }
            else if (currentLevel == MenuLevel.SelectBodyPart && bodyPartTypeahead.HasActiveSearch)
            {
                int prev = bodyPartTypeahead.GetPreviousMatch(partSelectionIndex);
                if (prev >= 0) partSelectionIndex = prev;
            }

            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceWithSearch();
        }

        private static bool HandleTypeaheadInput(char character)
        {
            if (currentLevel == MenuLevel.AddRecipeList && availableRecipes.Count > 0)
            {
                var labels = availableRecipes.Select(r => r.LabelCap.ToString().StripTags()).ToList();
                if (recipeTypeahead.ProcessCharacterInput(character, labels, out int newIndex))
                {
                    if (newIndex >= 0) recipeIndex = newIndex;
                    AnnounceWithSearch();
                }
                else
                {
                    TolkHelper.Speak($"No matches for '{recipeTypeahead.LastFailedSearch}'");
                }
                return true;
            }
            else if (currentLevel == MenuLevel.SelectBodyPart && partsForRecipe.Count > 0)
            {
                var labels = partsForRecipe.Select(p => p.Label).ToList();
                if (bodyPartTypeahead.ProcessCharacterInput(character, labels, out int newIndex))
                {
                    if (newIndex >= 0) partSelectionIndex = newIndex;
                    AnnounceWithSearch();
                }
                else
                {
                    TolkHelper.Speak($"No matches for '{bodyPartTypeahead.LastFailedSearch}'");
                }
                return true;
            }

            return false;
        }

        private static bool HandleTypeaheadBackspace()
        {
            if (currentLevel == MenuLevel.AddRecipeList && recipeTypeahead.HasActiveSearch)
            {
                var labels = availableRecipes.Select(r => r.LabelCap.ToString().StripTags()).ToList();
                if (recipeTypeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0) recipeIndex = newIndex;
                    AnnounceWithSearch();
                }
                return true;
            }
            else if (currentLevel == MenuLevel.SelectBodyPart && bodyPartTypeahead.HasActiveSearch)
            {
                var labels = partsForRecipe.Select(p => p.Label).ToList();
                if (bodyPartTypeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0) partSelectionIndex = newIndex;
                    AnnounceWithSearch();
                }
                return true;
            }

            return false;
        }

        private static void DrillDown()
        {
            // Clear any active search when drilling down
            recipeTypeahead.ClearSearch();
            bodyPartTypeahead.ClearSearch();

            switch (currentLevel)
            {
                case MenuLevel.MedicalSettingsList:
                    currentSettingName = medicalSettings[medicalSettingIndex];
                    if (currentSettingName == "Food Restriction")
                    {
                        availableFoodRestrictions = HealthTabHelper.GetAvailableFoodRestrictions();
                        if (availableFoodRestrictions.Count == 0)
                        {
                            TolkHelper.Speak("No food restrictions available");
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            return;
                        }
                        currentLevel = MenuLevel.MedicalSettingChange;
                        settingChoiceIndex = 0;
                    }
                    else if (currentSettingName == "Medical Care")
                    {
                        availableMedicalCare = HealthTabHelper.GetAvailableMedicalCare();
                        currentLevel = MenuLevel.MedicalSettingChange;
                        settingChoiceIndex = 0;
                    }
                    else if (currentSettingName == "Self-Tend")
                    {
                        HealthTabHelper.ToggleSelfTend(currentPawn);
                        AnnounceCurrentSelection();
                        return;
                    }
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

                case MenuLevel.MedicalSettingChange:
                    if (currentSettingName == "Food Restriction")
                    {
                        if (settingChoiceIndex >= 0 && settingChoiceIndex < availableFoodRestrictions.Count)
                        {
                            HealthTabHelper.SetFoodRestriction(currentPawn, availableFoodRestrictions[settingChoiceIndex]);
                            currentLevel = MenuLevel.MedicalSettingsList;
                            AnnounceCurrentSelection();
                        }
                    }
                    else if (currentSettingName == "Medical Care")
                    {
                        if (settingChoiceIndex >= 0 && settingChoiceIndex < availableMedicalCare.Count)
                        {
                            HealthTabHelper.SetMedicalCare(currentPawn, availableMedicalCare[settingChoiceIndex]);
                            currentLevel = MenuLevel.MedicalSettingsList;
                            AnnounceCurrentSelection();
                        }
                    }
                    break;

                case MenuLevel.OperationsList:
                    if (operationIndex < queuedOperations.Count)
                    {
                        currentLevel = MenuLevel.OperationActions;
                        operationActionIndex = 0;
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        AnnounceCurrentSelection();
                    }
                    else
                    {
                        // "Add Operation" selected
                        availableRecipes = HealthTabHelper.GetAvailableRecipes(currentPawn);
                        if (availableRecipes.Count == 0)
                        {
                            TolkHelper.Speak("No operations available");
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            return;
                        }
                        currentLevel = MenuLevel.AddRecipeList;
                        recipeIndex = 0;
                        recipeTypeahead.ClearSearch();
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        AnnounceCurrentSelection();
                    }
                    break;

                case MenuLevel.OperationActions:
                    string action = operationActions[operationActionIndex];
                    if (action == "View Details")
                    {
                        if (operationIndex >= 0 && operationIndex < queuedOperations.Count)
                        {
                            var bill = queuedOperations[operationIndex];
                            TolkHelper.Speak($"{bill.LabelCap.StripTags()}\n\nPress Escape to go back");
                            SoundDefOf.Click.PlayOneShotOnCamera();
                        }
                    }
                    else if (action == "Remove Operation")
                    {
                        if (operationIndex >= 0 && operationIndex < queuedOperations.Count)
                        {
                            var bill = queuedOperations[operationIndex];
                            HealthTabHelper.RemoveOperation(currentPawn, bill);
                            queuedOperations = HealthTabHelper.GetQueuedOperations(currentPawn);
                            currentLevel = MenuLevel.OperationsList;
                            operationIndex = 0;
                            AnnounceCurrentSelection();
                        }
                    }
                    else if (action == "Go Back")
                    {
                        currentLevel = MenuLevel.OperationsList;
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        AnnounceCurrentSelection();
                    }
                    break;

                case MenuLevel.AddRecipeList:
                    if (recipeIndex >= 0 && recipeIndex < availableRecipes.Count)
                    {
                        selectedRecipe = availableRecipes[recipeIndex];

                        // Get parts that this recipe can apply to
                        partsForRecipe = HealthTabHelper.GetPartsForRecipe(currentPawn, selectedRecipe);

                        if (partsForRecipe.Count == 0)
                        {
                            // Recipe doesn't require a specific part, add it directly
                            if (selectedRecipe.Worker.AvailableOnNow(currentPawn, null))
                            {
                                HealthTabHelper.AddOperation(currentPawn, selectedRecipe, null);
                                queuedOperations = HealthTabHelper.GetQueuedOperations(currentPawn);
                                currentLevel = MenuLevel.OperationsList;
                                operationIndex = 0;
                                AnnounceCurrentSelection();
                            }
                            else
                            {
                                TolkHelper.Speak("This operation is not available", SpeechPriority.High);
                                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            }
                        }
                        else if (partsForRecipe.Count == 1)
                        {
                            // Only one valid part, add operation directly
                            HealthTabHelper.AddOperation(currentPawn, selectedRecipe, partsForRecipe[0]);
                            queuedOperations = HealthTabHelper.GetQueuedOperations(currentPawn);
                            currentLevel = MenuLevel.OperationsList;
                            operationIndex = 0;
                            AnnounceCurrentSelection();
                        }
                        else
                        {
                            // Multiple parts available, let user choose
                            currentLevel = MenuLevel.SelectBodyPart;
                            partSelectionIndex = 0;
                            bodyPartTypeahead.ClearSearch();
                            SoundDefOf.Click.PlayOneShotOnCamera();
                            AnnounceCurrentSelection();
                        }
                    }
                    break;

                case MenuLevel.SelectBodyPart:
                    if (partSelectionIndex >= 0 && partSelectionIndex < partsForRecipe.Count)
                    {
                        var selectedPart = partsForRecipe[partSelectionIndex];
                        if (selectedRecipe.Worker.AvailableOnNow(currentPawn, selectedPart))
                        {
                            HealthTabHelper.AddOperation(currentPawn, selectedRecipe, selectedPart);
                            queuedOperations = HealthTabHelper.GetQueuedOperations(currentPawn);
                            currentLevel = MenuLevel.OperationsList;
                            operationIndex = 0;
                            AnnounceCurrentSelection();
                        }
                        else
                        {
                            TolkHelper.Speak("This operation is not available on this body part", SpeechPriority.High);
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        }
                    }
                    break;
            }
        }

        private static void GoBack()
        {
            recipeTypeahead.ClearSearch();
            bodyPartTypeahead.ClearSearch();

            switch (currentLevel)
            {
                case MenuLevel.MedicalSettingsList:
                case MenuLevel.OperationsList:
                    Close();
                    WindowlessInspectionState.ReannounceCurrentSelection();
                    break;

                case MenuLevel.MedicalSettingChange:
                    currentLevel = MenuLevel.MedicalSettingsList;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

                case MenuLevel.OperationActions:
                case MenuLevel.AddRecipeList:
                    currentLevel = MenuLevel.OperationsList;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

                case MenuLevel.SelectBodyPart:
                    currentLevel = MenuLevel.AddRecipeList;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;
            }
        }

        private static void AnnounceCurrentSelection()
        {
            var sb = new StringBuilder();

            switch (currentLevel)
            {
                case MenuLevel.MedicalSettingsList:
                    string setting = medicalSettings[medicalSettingIndex];
                    sb.AppendLine($"{setting}");

                    if (setting == "Food Restriction")
                    {
                        string current = HealthTabHelper.GetCurrentFoodRestriction(currentPawn);
                        sb.AppendLine($"Current: {current}");
                    }
                    else if (setting == "Medical Care")
                    {
                        string current = HealthTabHelper.GetCurrentMedicalCare(currentPawn);
                        sb.AppendLine($"Current: {current}");
                    }
                    else if (setting == "Self-Tend")
                    {
                        bool enabled = HealthTabHelper.GetSelfTendEnabled(currentPawn);
                        sb.AppendLine($"Current: {(enabled ? "Enabled" : "Disabled")}");
                    }

                    {
                        string pos = MenuHelper.FormatPosition(medicalSettingIndex, medicalSettings.Count);
                        if (!string.IsNullOrEmpty(pos)) sb.AppendLine(pos);
                    }
                    break;

                case MenuLevel.MedicalSettingChange:
                    if (currentSettingName == "Food Restriction")
                    {
                        if (settingChoiceIndex >= 0 && settingChoiceIndex < availableFoodRestrictions.Count)
                        {
                            var restriction = availableFoodRestrictions[settingChoiceIndex];
                            sb.AppendLine($"{restriction.label}");
                            sb.AppendLine($"Option {MenuHelper.FormatPosition(settingChoiceIndex, availableFoodRestrictions.Count)}");
                        }
                    }
                    else if (currentSettingName == "Medical Care")
                    {
                        if (settingChoiceIndex >= 0 && settingChoiceIndex < availableMedicalCare.Count)
                        {
                            var care = availableMedicalCare[settingChoiceIndex];
                            sb.AppendLine($"{care.GetLabel()}");
                            sb.AppendLine($"Option {MenuHelper.FormatPosition(settingChoiceIndex, availableMedicalCare.Count)}");
                        }
                    }
                    break;

                case MenuLevel.OperationsList:
                    {
                        string opsPos = MenuHelper.FormatPosition(operationIndex, queuedOperations.Count + 1);
                        if (operationIndex < queuedOperations.Count)
                        {
                            var bill = queuedOperations[operationIndex];
                            sb.Append($"Queued: {bill.LabelCap.StripTags()}");
                        }
                        else
                        {
                            sb.Append("Add Operation");
                        }
                        if (!string.IsNullOrEmpty(opsPos)) sb.Append($" ({opsPos})");
                        sb.AppendLine();
                    }
                    break;

                case MenuLevel.OperationActions:
                    {
                        sb.Append($"{operationActions[operationActionIndex]}");
                        string actPos = MenuHelper.FormatPosition(operationActionIndex, operationActions.Count);
                        if (!string.IsNullOrEmpty(actPos)) sb.Append($" ({actPos})");
                        sb.AppendLine();
                    }
                    break;

                case MenuLevel.AddRecipeList:
                    if (recipeIndex >= 0 && recipeIndex < availableRecipes.Count)
                    {
                        var recipe = availableRecipes[recipeIndex];
                        sb.AppendLine($"{recipe.LabelCap.ToString().StripTags()}");

                        if (!string.IsNullOrEmpty(recipe.description))
                        {
                            sb.AppendLine(recipe.description);
                        }

                        // Show ingredient requirements
                        if (recipe.ingredients != null && recipe.ingredients.Count > 0)
                        {
                            sb.Append("Requires: ");
                            foreach (var ingredient in recipe.ingredients)
                            {
                                sb.Append($"{ingredient.Summary}, ");
                            }
                            sb.Length -= 2; // Remove trailing ", "
                            sb.AppendLine();
                        }

                        // Announce missing (non-blocking) ingredients if any
                        if (currentPawn.MapHeld != null)
                        {
                            var missingIngredients = recipe.PotentiallyMissingIngredients(null, currentPawn.MapHeld).ToList();
                            if (missingIngredients.Count > 0)
                            {
                                sb.AppendLine("Missing: " + string.Join(", ", missingIngredients.Select(x => x.label)));
                            }
                        }

                        {
                            string recPos = MenuHelper.FormatPosition(recipeIndex, availableRecipes.Count);
                            if (!string.IsNullOrEmpty(recPos)) sb.AppendLine(recPos);
                        }
                    }
                    break;

                case MenuLevel.SelectBodyPart:
                    if (partSelectionIndex >= 0 && partSelectionIndex < partsForRecipe.Count)
                    {
                        var part = partsForRecipe[partSelectionIndex];
                        sb.AppendLine($"{selectedRecipe.LabelCap.ToString().StripTags()}");
                        sb.AppendLine($"Body part: {part.Label}");

                        // Show health information about the part
                        float health = currentPawn.health.hediffSet.GetPartHealth(part);
                        float maxHealth = part.def.GetMaxHealth(currentPawn);
                        sb.AppendLine($"Health: {health:F0} / {maxHealth:F0}");

                        {
                            string partPos = MenuHelper.FormatPosition(partSelectionIndex, partsForRecipe.Count);
                            if (!string.IsNullOrEmpty(partPos)) sb.AppendLine(partPos);
                        }
                    }
                    break;
            }

            TolkHelper.Speak(sb.ToString());
        }

        private static void AnnounceWithSearch()
        {
            if (currentLevel == MenuLevel.AddRecipeList && recipeTypeahead.HasActiveSearch)
            {
                if (recipeIndex >= 0 && recipeIndex < availableRecipes.Count)
                {
                    var recipe = availableRecipes[recipeIndex];
                    TolkHelper.Speak($"{recipe.LabelCap.ToString().StripTags()}, {recipeTypeahead.CurrentMatchPosition} of {recipeTypeahead.MatchCount} matches for '{recipeTypeahead.SearchBuffer}'");
                }
            }
            else if (currentLevel == MenuLevel.SelectBodyPart && bodyPartTypeahead.HasActiveSearch)
            {
                if (partSelectionIndex >= 0 && partSelectionIndex < partsForRecipe.Count)
                {
                    var part = partsForRecipe[partSelectionIndex];
                    TolkHelper.Speak($"{part.Label}, {bodyPartTypeahead.CurrentMatchPosition} of {bodyPartTypeahead.MatchCount} matches for '{bodyPartTypeahead.SearchBuffer}'");
                }
            }
            else
            {
                AnnounceCurrentSelection();
            }
        }
    }
}
