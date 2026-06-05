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
        // Setting indices (for index-based comparison instead of string matching)
        private const int SettingFoodRestriction = 0;
        private const int SettingMedicalCare = 1;
        private const int SettingSelfTend = 2;
        private const int MedicalSettingCount = 3;
        private static int currentSettingIndex = -1;

        /// <summary>
        /// Gets the translated label for a medical setting by index.
        /// </summary>
        private static string GetMedicalSettingLabel(int index)
        {
            switch (index)
            {
                case SettingFoodRestriction: return "AllowFood".Translate();
                case SettingMedicalCare: return "AllowMedicine".Translate();
                case SettingSelfTend: return "AllowSelfTend".Translate();
                default: return "";
            }
        }

        /// <summary>
        /// Gets the translated label for an operation action by index.
        /// </summary>
        private static string GetOperationActionLabel(int index)
        {
            switch (index)
            {
                case ActionViewDetails: return "TabBookContents".Translate();
                case ActionRemoveOperation: return "DeleteBillTip".Translate();
                case ActionGoBack: return "GoBack".Translate();
                default: return "";
            }
        }
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
        // Operation action indices
        private const int ActionViewDetails = 0;
        private const int ActionRemoveOperation = 1;
        private const int ActionGoBack = 2;
        private const int OperationActionCount = 3;
        private static int operationActionIndex = 0;

        // Typeahead search for all navigable lists
        private static TypeaheadSearchHelper settingsTypeahead = new TypeaheadSearchHelper();
        private static TypeaheadSearchHelper settingChoiceTypeahead = new TypeaheadSearchHelper();
        private static TypeaheadSearchHelper operationsTypeahead = new TypeaheadSearchHelper();
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
            settingsTypeahead.ClearSearch();
            settingChoiceTypeahead.ClearSearch();
            operationsTypeahead.ClearSearch();
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
            settingsTypeahead.ClearSearch();
            settingChoiceTypeahead.ClearSearch();
            operationsTypeahead.ClearSearch();
            recipeTypeahead.ClearSearch();
            bodyPartTypeahead.ClearSearch();
            SoundDefOf.TabClose.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Whether a typeahead search is active for the current menu level.
        /// </summary>
        private static bool HasActiveSearch =>
            (currentLevel == MenuLevel.MedicalSettingsList && settingsTypeahead.HasActiveSearch) ||
            (currentLevel == MenuLevel.MedicalSettingChange && settingChoiceTypeahead.HasActiveSearch) ||
            (currentLevel == MenuLevel.OperationsList && operationsTypeahead.HasActiveSearch) ||
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
                    if (currentLevel == MenuLevel.MedicalSettingsList)
                        settingsTypeahead.ClearSearchAndAnnounce();
                    else if (currentLevel == MenuLevel.MedicalSettingChange)
                        settingChoiceTypeahead.ClearSearchAndAnnounce();
                    else if (currentLevel == MenuLevel.OperationsList)
                        operationsTypeahead.ClearSearchAndAnnounce();
                    else if (currentLevel == MenuLevel.AddRecipeList)
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

            // Handle Ctrl+Up/Down - reorder queued operations
            if (evt.control && currentLevel == MenuLevel.OperationsList)
            {
                if (key == KeyCode.UpArrow)
                {
                    evt.Use();
                    ReorderOperation(-1);
                    return true;
                }
                if (key == KeyCode.DownArrow)
                {
                    evt.Use();
                    ReorderOperation(1);
                    return true;
                }
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

            // Consume letter keys to prevent game shortcut leaking (R=draft, T=time, etc.)
            // Actual typeahead handled via HandleCharacterInput on the character event
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            if (isLetter && !KeyboardHelper.IsAltHeld)
            {
                evt.Use();
                return true;
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
                    medicalSettingIndex = MenuHelper.SelectNext(medicalSettingIndex, MedicalSettingCount);
                    break;

                case MenuLevel.MedicalSettingChange:
                    if (currentSettingIndex == SettingFoodRestriction)
                        settingChoiceIndex = MenuHelper.SelectNext(settingChoiceIndex, availableFoodRestrictions.Count);
                    else if (currentSettingIndex == SettingMedicalCare)
                        settingChoiceIndex = MenuHelper.SelectNext(settingChoiceIndex, availableMedicalCare.Count);
                    break;

                case MenuLevel.OperationsList:
                    operationsTypeahead.ClearSearch();
                    int totalOps = queuedOperations.Count + 1; // +1 for "Add Operation"
                    operationIndex = MenuHelper.SelectNext(operationIndex, totalOps);
                    break;

                case MenuLevel.OperationActions:
                    operationActionIndex = MenuHelper.SelectNext(operationActionIndex, OperationActionCount);
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
                    medicalSettingIndex = MenuHelper.SelectPrevious(medicalSettingIndex, MedicalSettingCount);
                    break;

                case MenuLevel.MedicalSettingChange:
                    if (currentSettingIndex == SettingFoodRestriction)
                        settingChoiceIndex = MenuHelper.SelectPrevious(settingChoiceIndex, availableFoodRestrictions.Count);
                    else if (currentSettingIndex == SettingMedicalCare)
                        settingChoiceIndex = MenuHelper.SelectPrevious(settingChoiceIndex, availableMedicalCare.Count);
                    break;

                case MenuLevel.OperationsList:
                    operationsTypeahead.ClearSearch();
                    int totalOps = queuedOperations.Count + 1;
                    operationIndex = MenuHelper.SelectPrevious(operationIndex, totalOps);
                    break;

                case MenuLevel.OperationActions:
                    operationActionIndex = MenuHelper.SelectPrevious(operationActionIndex, OperationActionCount);
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
                    medicalSettingIndex = MedicalSettingCount - 1;
                    break;
                case MenuLevel.MedicalSettingChange:
                    if (currentSettingIndex == SettingFoodRestriction)
                        settingChoiceIndex = availableFoodRestrictions.Count - 1;
                    else if (currentSettingIndex == SettingMedicalCare)
                        settingChoiceIndex = availableMedicalCare.Count - 1;
                    break;
                case MenuLevel.OperationsList:
                    operationIndex = queuedOperations.Count; // Last item is "Add Operation"
                    break;
                case MenuLevel.OperationActions:
                    operationActionIndex = OperationActionCount - 1;
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
            if (currentLevel == MenuLevel.MedicalSettingsList && settingsTypeahead.HasActiveSearch)
            {
                int next = settingsTypeahead.GetNextMatch(medicalSettingIndex);
                if (next >= 0) medicalSettingIndex = next;
            }
            else if (currentLevel == MenuLevel.MedicalSettingChange && settingChoiceTypeahead.HasActiveSearch)
            {
                int next = settingChoiceTypeahead.GetNextMatch(settingChoiceIndex);
                if (next >= 0) settingChoiceIndex = next;
            }
            else if (currentLevel == MenuLevel.OperationsList && operationsTypeahead.HasActiveSearch)
            {
                int next = operationsTypeahead.GetNextMatch(operationIndex);
                if (next >= 0) operationIndex = next;
            }
            else if (currentLevel == MenuLevel.AddRecipeList && recipeTypeahead.HasActiveSearch)
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
            if (currentLevel == MenuLevel.MedicalSettingsList && settingsTypeahead.HasActiveSearch)
            {
                int prev = settingsTypeahead.GetPreviousMatch(medicalSettingIndex);
                if (prev >= 0) medicalSettingIndex = prev;
            }
            else if (currentLevel == MenuLevel.MedicalSettingChange && settingChoiceTypeahead.HasActiveSearch)
            {
                int prev = settingChoiceTypeahead.GetPreviousMatch(settingChoiceIndex);
                if (prev >= 0) settingChoiceIndex = prev;
            }
            else if (currentLevel == MenuLevel.OperationsList && operationsTypeahead.HasActiveSearch)
            {
                int prev = operationsTypeahead.GetPreviousMatch(operationIndex);
                if (prev >= 0) operationIndex = prev;
            }
            else if (currentLevel == MenuLevel.AddRecipeList && recipeTypeahead.HasActiveSearch)
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
            if (currentLevel == MenuLevel.MedicalSettingsList)
            {
                var labels = Enumerable.Range(0, MedicalSettingCount).Select(i => GetMedicalSettingLabel(i)).ToList();
                if (settingsTypeahead.ProcessCharacterInput(character, labels, out int newIndex))
                {
                    if (newIndex >= 0) medicalSettingIndex = newIndex;
                    AnnounceWithSearch();
                }
                else
                    AnnounceNoMatches(settingsTypeahead);
                return true;
            }
            else if (currentLevel == MenuLevel.MedicalSettingChange)
            {
                List<string> labels;
                TypeaheadSearchHelper typeahead;
                if (currentSettingIndex == SettingFoodRestriction)
                {
                    labels = availableFoodRestrictions.Select(r => r.label).ToList();
                    typeahead = settingChoiceTypeahead;
                }
                else if (currentSettingIndex == SettingMedicalCare)
                {
                    labels = availableMedicalCare.Select(c => c.GetLabel()).ToList();
                    typeahead = settingChoiceTypeahead;
                }
                else
                    return false;

                if (typeahead.ProcessCharacterInput(character, labels, out int newIndex))
                {
                    if (newIndex >= 0) settingChoiceIndex = newIndex;
                    AnnounceWithSearch();
                }
                else
                    AnnounceNoMatches(typeahead);
                return true;
            }
            else if (currentLevel == MenuLevel.OperationsList && queuedOperations.Count > 0)
            {
                var labels = queuedOperations.Select(b => b.LabelCap.StripTags()).ToList();
                if (operationsTypeahead.ProcessCharacterInput(character, labels, out int newIndex))
                {
                    if (newIndex >= 0) operationIndex = newIndex;
                    AnnounceWithSearch();
                }
                else
                    AnnounceNoMatches(operationsTypeahead);
                return true;
            }
            else if (currentLevel == MenuLevel.AddRecipeList && availableRecipes.Count > 0)
            {
                var labels = availableRecipes.Select(r => r.LabelCap.ToString().StripTags()).ToList();
                if (recipeTypeahead.ProcessCharacterInput(character, labels, out int newIndex))
                {
                    if (newIndex >= 0) recipeIndex = newIndex;
                    AnnounceWithSearch();
                }
                else
                    AnnounceNoMatches(recipeTypeahead);
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
                    AnnounceNoMatches(bodyPartTypeahead);
                return true;
            }

            return false;
        }

        private static void AnnounceNoMatches(TypeaheadSearchHelper typeahead)
        {
            if (!string.IsNullOrEmpty(typeahead.LastFailedSearch))
                typeahead.SpeakNoMatches();
        }

        private static bool HandleTypeaheadBackspace()
        {
            if (currentLevel == MenuLevel.MedicalSettingsList && settingsTypeahead.HasActiveSearch)
            {
                var labels = Enumerable.Range(0, MedicalSettingCount).Select(i => GetMedicalSettingLabel(i)).ToList();
                if (settingsTypeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0) medicalSettingIndex = newIndex;
                    AnnounceWithSearch();
                }
                return true;
            }
            else if (currentLevel == MenuLevel.MedicalSettingChange && settingChoiceTypeahead.HasActiveSearch)
            {
                List<string> labels;
                if (currentSettingIndex == SettingFoodRestriction)
                    labels = availableFoodRestrictions.Select(r => r.label).ToList();
                else if (currentSettingIndex == SettingMedicalCare)
                    labels = availableMedicalCare.Select(c => c.GetLabel()).ToList();
                else
                    return false;

                if (settingChoiceTypeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0) settingChoiceIndex = newIndex;
                    AnnounceWithSearch();
                }
                return true;
            }
            else if (currentLevel == MenuLevel.OperationsList && operationsTypeahead.HasActiveSearch)
            {
                var labels = queuedOperations.Select(b => b.LabelCap.StripTags()).ToList();
                if (operationsTypeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0) operationIndex = newIndex;
                    AnnounceWithSearch();
                }
                return true;
            }
            else if (currentLevel == MenuLevel.AddRecipeList && recipeTypeahead.HasActiveSearch)
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
            settingsTypeahead.ClearSearch();
            settingChoiceTypeahead.ClearSearch();
            operationsTypeahead.ClearSearch();
            recipeTypeahead.ClearSearch();
            bodyPartTypeahead.ClearSearch();

            switch (currentLevel)
            {
                case MenuLevel.MedicalSettingsList:
                    currentSettingIndex = medicalSettingIndex;
                    if (currentSettingIndex == SettingFoodRestriction)
                    {
                        availableFoodRestrictions = HealthTabHelper.GetAvailableFoodRestrictions();
                        if (availableFoodRestrictions.Count == 0)
                        {
                            TolkHelper.Speak("NoneLower".Loc());
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            return;
                        }
                        currentLevel = MenuLevel.MedicalSettingChange;
                        settingChoiceIndex = 0;
                    }
                    else if (currentSettingIndex == SettingMedicalCare)
                    {
                        availableMedicalCare = HealthTabHelper.GetAvailableMedicalCare();
                        currentLevel = MenuLevel.MedicalSettingChange;
                        settingChoiceIndex = 0;
                    }
                    else if (currentSettingIndex == SettingSelfTend)
                    {
                        HealthTabHelper.ToggleSelfTend(currentPawn);
                        // Vanilla validation: revert toggle and warn if pawn can't self-tend
                        if (currentPawn.playerSettings?.selfTend == true)
                        {
                            if (currentPawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor))
                            {
                                // Pawn can never do Doctor work — revert like vanilla does
                                currentPawn.playerSettings.selfTend = false;
                                TolkHelper.Speak("MessageCannotSelfTendEver".Loc(
                                    currentPawn.LabelShort, currentPawn), SpeechPriority.High);
                            }
                            else if (currentPawn.workSettings != null
                                && !currentPawn.workSettings.WorkIsActive(WorkTypeDefOf.Doctor))
                            {
                                // Doctor work not assigned — warn but allow (vanilla behavior)
                                TolkHelper.Speak("MessageSelfTendUnsatisfied".Loc(
                                    currentPawn.LabelShort, currentPawn), SpeechPriority.High);
                            }
                        }
                        AnnounceCurrentSelection();
                        return;
                    }
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

                case MenuLevel.MedicalSettingChange:
                    if (currentSettingIndex == SettingFoodRestriction)
                    {
                        if (settingChoiceIndex >= 0 && settingChoiceIndex < availableFoodRestrictions.Count)
                        {
                            HealthTabHelper.SetFoodRestriction(currentPawn, availableFoodRestrictions[settingChoiceIndex]);
                            currentLevel = MenuLevel.MedicalSettingsList;
                            AnnounceCurrentSelection();
                        }
                    }
                    else if (currentSettingIndex == SettingMedicalCare)
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
                            TolkHelper.Speak("NoneLower".Loc());
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
                    if (operationActionIndex == ActionViewDetails)
                    {
                        if (operationIndex >= 0 && operationIndex < queuedOperations.Count)
                        {
                            var bill = queuedOperations[operationIndex];
                            var detailSb = new StringBuilder();
                            detailSb.Append(bill.LabelCap.StripTags());

                            var recipe = bill.recipe;

                            if (recipe.skillRequirements != null && recipe.skillRequirements.Count > 0)
                            {
                                string skills = recipe.skillRequirements
                                    .Select(sr => $"{sr.skill.LabelCap} {sr.minLevel}")
                                    .ToCommaList();
                                detailSb.Append($". {"Requires".Translate()}: {skills}");
                            }

                            if (recipe.ingredients != null && recipe.ingredients.Count > 0)
                                detailSb.Append($". {"Ingredients".Translate()}: {recipe.ingredients.Select(i => i.Summary).ToCommaList()}");

                            if (recipe.products != null && recipe.products.Count > 0)
                                detailSb.Append($". {"Products".Translate()}: {recipe.products.Select(p => $"{p.thingDef.LabelCap} x{p.count}").ToCommaList()}");

                            var billMedical = bill as Bill_Medical;
                            if (billMedical?.Part != null && recipe.addsHediff?.addedPartProps?.solid == true)
                            {
                                var replacedParts = new List<string>();
                                foreach (var childPart in billMedical.Part.GetPartAndAllChildParts())
                                {
                                    if (currentPawn.health.hediffSet.TryGetDirectlyAddedPartFor(childPart, out var existing))
                                        replacedParts.Add(existing.Label);
                                }
                                if (replacedParts.Count > 0)
                                    detailSb.Append($". {"Replaces".Translate()}: {replacedParts.ToCommaList().CapitalizeFirst()}");
                            }

                            TolkHelper.SpeakData(detailSb.ToString());
                            SoundDefOf.Click.PlayOneShotOnCamera();
                        }
                    }
                    else if (operationActionIndex == ActionRemoveOperation)
                    {
                        if (operationIndex >= 0 && operationIndex < queuedOperations.Count)
                        {
                            var bill = queuedOperations[operationIndex];
                            HealthTabHelper.RemoveOperation(currentPawn, bill);
                            SoundDefOf.Click.PlayOneShotOnCamera();
                            queuedOperations = HealthTabHelper.GetQueuedOperations(currentPawn);
                            currentLevel = MenuLevel.OperationsList;
                            operationIndex = 0;
                            AnnounceCurrentSelection();
                        }
                    }
                    else if (operationActionIndex == ActionGoBack)
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
                            AcceptanceReport report = selectedRecipe.Worker.AvailableReport(currentPawn);
                            if (report.Accepted && selectedRecipe.Worker.AvailableOnNow(currentPawn, null))
                            {
                                HealthTabHelper.AddOperation(currentPawn, selectedRecipe, null);
                                SoundDefOf.Click.PlayOneShotOnCamera();
                                queuedOperations = HealthTabHelper.GetQueuedOperations(currentPawn);
                                currentLevel = MenuLevel.OperationsList;
                                operationIndex = 0;
                                AnnounceCurrentSelection();
                            }
                            else
                            {
                                string reason = report.Reason.NullOrEmpty() ? "" : report.Reason;
                                TolkHelper.Speak("CannotUseReason".Loc(reason), SpeechPriority.High);
                                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            }
                        }
                        else if (partsForRecipe.Count == 1)
                        {
                            // Only one valid part, add operation directly
                            HealthTabHelper.AddOperation(currentPawn, selectedRecipe, partsForRecipe[0]);
                            SoundDefOf.Click.PlayOneShotOnCamera();
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
                            SoundDefOf.Click.PlayOneShotOnCamera();
                            queuedOperations = HealthTabHelper.GetQueuedOperations(currentPawn);
                            currentLevel = MenuLevel.OperationsList;
                            operationIndex = 0;
                            AnnounceCurrentSelection();
                        }
                        else
                        {
                            AcceptanceReport partReport = selectedRecipe.Worker.AvailableReport(currentPawn);
                            string partReason = partReport.Reason.NullOrEmpty() ? "" : partReport.Reason;
                            TolkHelper.Speak("CannotUseReason".Loc(partReason), SpeechPriority.High);
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

        private static void ReorderOperation(int offset)
        {
            if (operationIndex >= queuedOperations.Count)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            var bill = queuedOperations[operationIndex];
            int newIndex = operationIndex + offset;

            if (newIndex < 0)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Top);
                return;
            }
            if (newIndex >= queuedOperations.Count)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Bottom);
                return;
            }

            currentPawn.BillStack.Reorder(bill, offset);
            queuedOperations = HealthTabHelper.GetQueuedOperations(currentPawn);
            operationIndex = newIndex;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        private static void AnnounceCurrentSelection()
        {
            var sb = new StringBuilder();

            switch (currentLevel)
            {
                case MenuLevel.MedicalSettingsList:
                    string settingLabel = GetMedicalSettingLabel(medicalSettingIndex);
                    if (medicalSettingIndex == SettingFoodRestriction)
                    {
                        string currentFood = HealthTabHelper.GetCurrentFoodRestriction(currentPawn);
                        sb.Append($"{settingLabel}: {currentFood}");
                        sb.Append($". {"FoodRestrictionDescription".Translate()}");
                    }
                    else if (medicalSettingIndex == SettingMedicalCare)
                    {
                        string currentCare = HealthTabHelper.GetCurrentMedicalCare(currentPawn);
                        sb.Append($"{settingLabel}: {currentCare}");
                        sb.Append($". {"MedicineQualityDescription".Translate()}");
                    }
                    else if (medicalSettingIndex == SettingSelfTend)
                    {
                        bool enabled = HealthTabHelper.GetSelfTendEnabled(currentPawn);
                        sb.Append($"{settingLabel}: {(enabled ? "On".Translate().ToString() : "Off".Translate().ToString())}");
                        sb.Append($". {"AllowSelfTendTip".Translate(Faction.OfPlayer.def.pawnsPlural, TendUtility.SelfTendQualityFactor.ToStringPercent())}");
                    }

                    {
                        string pos = MenuHelper.FormatPosition(medicalSettingIndex, MedicalSettingCount);
                        if (!string.IsNullOrEmpty(pos)) sb.Append($", {pos}");
                    }
                    break;

                case MenuLevel.MedicalSettingChange:
                    if (currentSettingIndex == SettingFoodRestriction)
                    {
                        if (settingChoiceIndex >= 0 && settingChoiceIndex < availableFoodRestrictions.Count)
                        {
                            var restriction = availableFoodRestrictions[settingChoiceIndex];
                            sb.Append(restriction.label);
                            string foodPos = MenuHelper.FormatPosition(settingChoiceIndex, availableFoodRestrictions.Count);
                            if (!string.IsNullOrEmpty(foodPos)) sb.Append($", {foodPos}");
                        }
                    }
                    else if (currentSettingIndex == SettingMedicalCare)
                    {
                        if (settingChoiceIndex >= 0 && settingChoiceIndex < availableMedicalCare.Count)
                        {
                            var care = availableMedicalCare[settingChoiceIndex];
                            sb.Append(care.GetLabel());
                            string carePos = MenuHelper.FormatPosition(settingChoiceIndex, availableMedicalCare.Count);
                            if (!string.IsNullOrEmpty(carePos)) sb.Append($", {carePos}");
                        }
                    }
                    break;

                case MenuLevel.OperationsList:
                    {
                        if (operationIndex < queuedOperations.Count)
                        {
                            var bill = queuedOperations[operationIndex];
                            sb.Append(bill.LabelCap.StripTags());
                        }
                        else
                        {
                            sb.Append("AddBill".Translate().ToString());
                        }
                        string opsPos = MenuHelper.FormatPosition(operationIndex, queuedOperations.Count + 1);
                        if (!string.IsNullOrEmpty(opsPos)) sb.Append($", {opsPos}");
                    }
                    break;

                case MenuLevel.OperationActions:
                    {
                        sb.Append(GetOperationActionLabel(operationActionIndex));
                        string actPos = MenuHelper.FormatPosition(operationActionIndex, OperationActionCount);
                        if (!string.IsNullOrEmpty(actPos)) sb.Append($", {actPos}");
                    }
                    break;

                case MenuLevel.AddRecipeList:
                    if (recipeIndex >= 0 && recipeIndex < availableRecipes.Count)
                    {
                        var recipe = availableRecipes[recipeIndex];
                        sb.Append(recipe.LabelCap.ToString());

                        if (!string.IsNullOrEmpty(recipe.description))
                            sb.Append($". {recipe.description}");

                        if (recipe.skillRequirements != null && recipe.skillRequirements.Count > 0)
                        {
                            string skills = recipe.skillRequirements
                                .Select(sr => $"{sr.skill.LabelCap} {sr.minLevel}")
                                .ToCommaList();
                            sb.Append($". {"Requires".Translate()}: {skills}");
                        }

                        if (recipe.ingredients != null && recipe.ingredients.Count > 0)
                            sb.Append($". {"Ingredients".Translate()}: {recipe.ingredients.Select(i => i.Summary).ToCommaList()}");

                        if (currentPawn.MapHeld != null)
                        {
                            var missingIngredients = recipe.PotentiallyMissingIngredients(null, currentPawn.MapHeld).ToList();
                            if (missingIngredients.Count > 0)
                            {
                                string missing = string.Join(", ", missingIngredients.Select(
                                    x => "MissingMedicalBillIngredient".Translate(x.label).ToString()));
                                sb.Append($". {missing}");
                            }
                        }

                        string recPos = MenuHelper.FormatPosition(recipeIndex, availableRecipes.Count);
                        if (!string.IsNullOrEmpty(recPos)) sb.Append($", {recPos}");
                    }
                    break;

                case MenuLevel.SelectBodyPart:
                    if (partSelectionIndex >= 0 && partSelectionIndex < partsForRecipe.Count)
                    {
                        var part = partsForRecipe[partSelectionIndex];
                        sb.Append(selectedRecipe.Worker.GetLabelWhenUsedOn(currentPawn, part).CapitalizeFirst());
                        sb.Append($", {part.LabelCap}");

                        float health = currentPawn.health.hediffSet.GetPartHealth(part);
                        float maxHealth = part.def.GetMaxHealth(currentPawn);
                        var conditionLabel = HealthUtility.GetPartConditionLabel(currentPawn, part);
                        sb.Append($". {conditionLabel.First}, {health:F0} / {maxHealth:F0}");

                        // Show rejection reason or missing ingredients (matches vanilla's float menu)
                        if (!selectedRecipe.Worker.AvailableOnNow(currentPawn, part))
                        {
                            AcceptanceReport partReport = selectedRecipe.Worker.AvailableReport(currentPawn);
                            if (!partReport.Reason.NullOrEmpty())
                                sb.Append($". {partReport.Reason}");
                        }
                        else if (currentPawn.MapHeld != null)
                        {
                            var missingIngredients = selectedRecipe.PotentiallyMissingIngredients(null, currentPawn.MapHeld).ToList();
                            if (missingIngredients.Count > 0)
                            {
                                string missing = string.Join(", ", missingIngredients.Select(
                                    x => "MissingMedicalBillIngredient".Translate(x.label).ToString()));
                                sb.Append($". {missing}");
                            }
                        }

                        string partPos = MenuHelper.FormatPosition(partSelectionIndex, partsForRecipe.Count);
                        if (!string.IsNullOrEmpty(partPos)) sb.Append($", {partPos}");
                    }
                    break;
            }

            TolkHelper.SpeakData(sb.ToString());
        }

        private static void AnnounceWithSearch()
        {
            TypeaheadSearchHelper activeTypeahead = null;
            string itemLabel = null;

            if (currentLevel == MenuLevel.MedicalSettingsList && settingsTypeahead.HasActiveSearch)
            {
                activeTypeahead = settingsTypeahead;
                itemLabel = GetMedicalSettingLabel(medicalSettingIndex);
            }
            else if (currentLevel == MenuLevel.MedicalSettingChange && settingChoiceTypeahead.HasActiveSearch)
            {
                activeTypeahead = settingChoiceTypeahead;
                if (currentSettingIndex == SettingFoodRestriction && settingChoiceIndex >= 0 && settingChoiceIndex < availableFoodRestrictions.Count)
                    itemLabel = availableFoodRestrictions[settingChoiceIndex].label;
                else if (currentSettingIndex == SettingMedicalCare && settingChoiceIndex >= 0 && settingChoiceIndex < availableMedicalCare.Count)
                    itemLabel = availableMedicalCare[settingChoiceIndex].GetLabel();
            }
            else if (currentLevel == MenuLevel.OperationsList && operationsTypeahead.HasActiveSearch)
            {
                activeTypeahead = operationsTypeahead;
                if (operationIndex >= 0 && operationIndex < queuedOperations.Count)
                    itemLabel = queuedOperations[operationIndex].LabelCap.StripTags();
            }
            else if (currentLevel == MenuLevel.AddRecipeList && recipeTypeahead.HasActiveSearch)
            {
                activeTypeahead = recipeTypeahead;
                if (recipeIndex >= 0 && recipeIndex < availableRecipes.Count)
                    itemLabel = availableRecipes[recipeIndex].LabelCap.ToString().StripTags();
            }
            else if (currentLevel == MenuLevel.SelectBodyPart && bodyPartTypeahead.HasActiveSearch)
            {
                activeTypeahead = bodyPartTypeahead;
                if (partSelectionIndex >= 0 && partSelectionIndex < partsForRecipe.Count)
                    itemLabel = partsForRecipe[partSelectionIndex].Label;
            }

            if (activeTypeahead != null && itemLabel != null)
            {
                TolkHelper.SpeakData(activeTypeahead.BuildItemAnnouncement(itemLabel));
            }
            else
            {
                AnnounceCurrentSelection();
            }
        }
    }
}
