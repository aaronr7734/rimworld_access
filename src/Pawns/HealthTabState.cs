using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// State handler for the Health tab in the inspection menu.
    /// Manages hierarchical navigation through medical settings, capacities, operations, and body parts.
    /// </summary>
    public static class HealthTabState
    {
        private enum MenuLevel
        {
            SectionMenu,           // Level 1: Choose section (Settings/Capacities/Operations/BodyParts)
            MedicalSettingsList,   // Level 2a: List medical settings
            MedicalSettingChange,  // Level 3a: Change a medical setting
            CapacitiesList,        // Level 2b: List capacities
            CapacityDetail,        // Level 3b: View capacity details
            OperationsList,        // Level 2c: List operations
            OperationActions,      // Level 3c: Actions for operation
            AddOperationList,      // Level 3d: List available operations to add (DEPRECATED - use AddRecipeList)
            AddRecipeList,         // Level 3d: List available recipes to add
            SelectBodyPart,        // Level 4d: Select body part for recipe
            BodyPartsList,         // Level 2d: List body parts
            HediffsList,           // Level 3e: List hediffs on a part
            HediffDetail           // Level 4e: View hediff details
        }

        private static bool isActive = false;
        private static Pawn currentPawn = null;

        private static MenuLevel currentLevel = MenuLevel.SectionMenu;
        private static int sectionIndex = 0;
        private static readonly List<string> sections = new List<string> { "Medical Settings", "Capacities", "Operations", "Body Parts" };

        // Medical Settings
        private static int medicalSettingIndex = 0;
        private static readonly List<string> medicalSettings = new List<string> { "Food Restriction", "Medical Care", "Self-Tend" };
        private static string currentSettingName = "";
        private static List<FoodPolicy> availableFoodRestrictions = new List<FoodPolicy>();
        private static List<MedicalCareCategory> availableMedicalCare = new List<MedicalCareCategory>();
        private static int settingChoiceIndex = 0;

        // Capacities
        private static List<HealthTabHelper.CapacityInfo> capacities = new List<HealthTabHelper.CapacityInfo>();
        private static int capacityIndex = 0;

        // Operations
        private static List<Bill> queuedOperations = new List<Bill>();
        private static List<HealthTabHelper.OperationInfo> availableOperations = new List<HealthTabHelper.OperationInfo>();
        private static List<RecipeDef> availableRecipes = new List<RecipeDef>();
        private static RecipeDef selectedRecipe = null;
        private static List<BodyPartRecord> partsForRecipe = new List<BodyPartRecord>();
        private static int operationIndex = 0;
        private static int recipeIndex = 0;
        private static int partSelectionIndex = 0;
        private static readonly List<string> operationActions = new List<string> { "View Details", "Remove Operation", "Go Back" };
        private static int operationActionIndex = 0;

        // Body Parts
        private static List<HealthTabHelper.BodyPartInfo> bodyParts = new List<HealthTabHelper.BodyPartInfo>();
        private static int bodyPartIndex = 0;
        private static int hediffIndex = 0;

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the health tab for a pawn.
        /// </summary>
        public static void Open(Pawn pawn)
        {
            if (pawn == null)
                return;

            currentPawn = pawn;
            isActive = true;
            currentLevel = MenuLevel.SectionMenu;
            sectionIndex = 0;

            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

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

            // Build operations list
            queuedOperations.Clear();
            availableOperations.Clear();

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
            SoundDefOf.TabClose.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Handles keyboard input.
        /// </summary>
        public static bool HandleInput(Event evt)
        {
            if (!isActive || evt.type != EventType.KeyDown)
                return false;

            KeyCode key = evt.keyCode;

            // Handle Escape - go back or close
            if (key == KeyCode.Escape)
            {
                evt.Use();
                GoBack();
                return true;
            }

            // Handle arrow keys
            if (key == KeyCode.UpArrow)
            {
                evt.Use();
                SelectPrevious();
                return true;
            }

            if (key == KeyCode.DownArrow)
            {
                evt.Use();
                SelectNext();
                return true;
            }

            // Handle Enter - drill down or execute
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                evt.Use();
                DrillDown();
                return true;
            }

            return false;
        }

        private static void SelectNext()
        {
            switch (currentLevel)
            {
                case MenuLevel.SectionMenu:
                    sectionIndex = MenuHelper.SelectNext(sectionIndex, sections.Count);
                    break;

                case MenuLevel.MedicalSettingsList:
                    medicalSettingIndex = MenuHelper.SelectNext(medicalSettingIndex, medicalSettings.Count);
                    break;

                case MenuLevel.MedicalSettingChange:
                    if (currentSettingName == "Food Restriction")
                        settingChoiceIndex = MenuHelper.SelectNext(settingChoiceIndex, availableFoodRestrictions.Count);
                    else if (currentSettingName == "Medical Care")
                        settingChoiceIndex = MenuHelper.SelectNext(settingChoiceIndex, availableMedicalCare.Count);
                    break;

                case MenuLevel.CapacitiesList:
                    if (capacities.Count > 0)
                        capacityIndex = MenuHelper.SelectNext(capacityIndex, capacities.Count);
                    break;

                case MenuLevel.OperationsList:
                    int totalOps = queuedOperations.Count + 1; // +1 for "Add Operation"
                    operationIndex = MenuHelper.SelectNext(operationIndex, totalOps);
                    break;

                case MenuLevel.OperationActions:
                    operationActionIndex = MenuHelper.SelectNext(operationActionIndex, operationActions.Count);
                    break;

                case MenuLevel.AddOperationList:
                    if (availableOperations.Count > 0)
                        operationIndex = MenuHelper.SelectNext(operationIndex, availableOperations.Count);
                    break;

                case MenuLevel.AddRecipeList:
                    if (availableRecipes.Count > 0)
                        recipeIndex = MenuHelper.SelectNext(recipeIndex, availableRecipes.Count);
                    break;

                case MenuLevel.SelectBodyPart:
                    if (partsForRecipe.Count > 0)
                        partSelectionIndex = MenuHelper.SelectNext(partSelectionIndex, partsForRecipe.Count);
                    break;

                case MenuLevel.BodyPartsList:
                    if (bodyParts.Count > 0)
                        bodyPartIndex = MenuHelper.SelectNext(bodyPartIndex, bodyParts.Count);
                    break;

                case MenuLevel.HediffsList:
                    if (bodyPartIndex >= 0 && bodyPartIndex < bodyParts.Count)
                    {
                        int hediffCount = bodyParts[bodyPartIndex].Hediffs.Count;
                        if (hediffCount > 0)
                            hediffIndex = MenuHelper.SelectNext(hediffIndex, hediffCount);
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
                case MenuLevel.SectionMenu:
                    sectionIndex = MenuHelper.SelectPrevious(sectionIndex, sections.Count);
                    break;

                case MenuLevel.MedicalSettingsList:
                    medicalSettingIndex = MenuHelper.SelectPrevious(medicalSettingIndex, medicalSettings.Count);
                    break;

                case MenuLevel.MedicalSettingChange:
                    if (currentSettingName == "Food Restriction")
                        settingChoiceIndex = MenuHelper.SelectPrevious(settingChoiceIndex, availableFoodRestrictions.Count);
                    else if (currentSettingName == "Medical Care")
                        settingChoiceIndex = MenuHelper.SelectPrevious(settingChoiceIndex, availableMedicalCare.Count);
                    break;

                case MenuLevel.CapacitiesList:
                    if (capacities.Count > 0)
                        capacityIndex = MenuHelper.SelectPrevious(capacityIndex, capacities.Count);
                    break;

                case MenuLevel.OperationsList:
                    int totalOps = queuedOperations.Count + 1;
                    operationIndex = MenuHelper.SelectPrevious(operationIndex, totalOps);
                    break;

                case MenuLevel.OperationActions:
                    operationActionIndex = MenuHelper.SelectPrevious(operationActionIndex, operationActions.Count);
                    break;

                case MenuLevel.AddOperationList:
                    if (availableOperations.Count > 0)
                        operationIndex = MenuHelper.SelectPrevious(operationIndex, availableOperations.Count);
                    break;

                case MenuLevel.AddRecipeList:
                    if (availableRecipes.Count > 0)
                        recipeIndex = MenuHelper.SelectPrevious(recipeIndex, availableRecipes.Count);
                    break;

                case MenuLevel.SelectBodyPart:
                    if (partsForRecipe.Count > 0)
                        partSelectionIndex = MenuHelper.SelectPrevious(partSelectionIndex, partsForRecipe.Count);
                    break;

                case MenuLevel.BodyPartsList:
                    if (bodyParts.Count > 0)
                        bodyPartIndex = MenuHelper.SelectPrevious(bodyPartIndex, bodyParts.Count);
                    break;

                case MenuLevel.HediffsList:
                    if (bodyPartIndex >= 0 && bodyPartIndex < bodyParts.Count)
                    {
                        int hediffCount = bodyParts[bodyPartIndex].Hediffs.Count;
                        if (hediffCount > 0)
                            hediffIndex = MenuHelper.SelectPrevious(hediffIndex, hediffCount);
                    }
                    break;
            }

            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        private static void DrillDown()
        {
            switch (currentLevel)
            {
                case MenuLevel.SectionMenu:
                    string section = sections[sectionIndex];
                    if (section == "Medical Settings")
                    {
                        currentLevel = MenuLevel.MedicalSettingsList;
                        medicalSettingIndex = 0;
                    }
                    else if (section == "Capacities")
                    {
                        capacities = HealthTabHelper.GetCapacities(currentPawn);
                        if (capacities.Count == 0)
                        {
                            TolkHelper.Speak("No capacity information available");
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            return;
                        }
                        currentLevel = MenuLevel.CapacitiesList;
                        capacityIndex = 0;
                    }
                    else if (section == "Operations")
                    {
                        queuedOperations = HealthTabHelper.GetQueuedOperations(currentPawn);
                        currentLevel = MenuLevel.OperationsList;
                        operationIndex = 0;
                    }
                    else if (section == "Body Parts")
                    {
                        bodyParts = HealthTabHelper.GetBodyPartsWithHediffs(currentPawn);
                        if (bodyParts.Count == 0)
                        {
                            TolkHelper.Speak("No health conditions");
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            return;
                        }
                        currentLevel = MenuLevel.BodyPartsList;
                        bodyPartIndex = 0;
                    }
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

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

                case MenuLevel.CapacitiesList:
                    if (capacityIndex >= 0 && capacityIndex < capacities.Count)
                    {
                        currentLevel = MenuLevel.CapacityDetail;
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        AnnounceCurrentSelection();
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

                case MenuLevel.AddOperationList:
                    if (operationIndex >= 0 && operationIndex < availableOperations.Count)
                    {
                        var op = availableOperations[operationIndex];
                        if (op.IsAvailable)
                        {
                            HealthTabHelper.AddOperation(currentPawn, op.Recipe, op.BodyPart);
                            queuedOperations = HealthTabHelper.GetQueuedOperations(currentPawn);
                            currentLevel = MenuLevel.OperationsList;
                            operationIndex = 0;
                            AnnounceCurrentSelection();
                        }
                        else
                        {
                            TolkHelper.Speak($"Cannot add: {op.UnavailableReason}", SpeechPriority.High);
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        }
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

                case MenuLevel.BodyPartsList:
                    if (bodyPartIndex >= 0 && bodyPartIndex < bodyParts.Count)
                    {
                        var part = bodyParts[bodyPartIndex];
                        if (part.Hediffs.Count > 0)
                        {
                            currentLevel = MenuLevel.HediffsList;
                            hediffIndex = 0;
                            SoundDefOf.Click.PlayOneShotOnCamera();
                            AnnounceCurrentSelection();
                        }
                        else
                        {
                            TolkHelper.Speak("No conditions on this body part");
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        }
                    }
                    break;

                case MenuLevel.HediffsList:
                    if (bodyPartIndex >= 0 && bodyPartIndex < bodyParts.Count)
                    {
                        var part = bodyParts[bodyPartIndex];
                        if (hediffIndex >= 0 && hediffIndex < part.Hediffs.Count)
                        {
                            currentLevel = MenuLevel.HediffDetail;
                            SoundDefOf.Click.PlayOneShotOnCamera();
                            AnnounceCurrentSelection();
                        }
                    }
                    break;
            }
        }

        private static void GoBack()
        {
            switch (currentLevel)
            {
                case MenuLevel.SectionMenu:
                    Close();
                    TolkHelper.Speak("Closed Health tab");
                    break;

                case MenuLevel.MedicalSettingsList:
                case MenuLevel.CapacitiesList:
                case MenuLevel.OperationsList:
                case MenuLevel.BodyPartsList:
                    currentLevel = MenuLevel.SectionMenu;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

                case MenuLevel.MedicalSettingChange:
                    currentLevel = MenuLevel.MedicalSettingsList;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

                case MenuLevel.CapacityDetail:
                    currentLevel = MenuLevel.CapacitiesList;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

                case MenuLevel.OperationActions:
                case MenuLevel.AddOperationList:
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

                case MenuLevel.HediffsList:
                    currentLevel = MenuLevel.BodyPartsList;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

                case MenuLevel.HediffDetail:
                    currentLevel = MenuLevel.HediffsList;
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
                case MenuLevel.SectionMenu:
                    sb.AppendLine($"Health - {sections[sectionIndex]}");
                    sb.AppendLine($"Section {MenuHelper.FormatPosition(sectionIndex, sections.Count)}");
                    sb.AppendLine("Press Enter to open");
                    break;

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

                    sb.AppendLine($"Setting {MenuHelper.FormatPosition(medicalSettingIndex, medicalSettings.Count)}");
                    sb.AppendLine("Press Enter to change");
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
                    sb.AppendLine("Press Enter to confirm");
                    break;

                case MenuLevel.CapacitiesList:
                    if (capacityIndex >= 0 && capacityIndex < capacities.Count)
                    {
                        var capacity = capacities[capacityIndex];
                        sb.AppendLine($"{capacity.Label}: {capacity.LevelLabel}");
                        sb.AppendLine($"Capacity {MenuHelper.FormatPosition(capacityIndex, capacities.Count)}");
                        sb.AppendLine("Press Enter for details");
                    }
                    break;

                case MenuLevel.CapacityDetail:
                    if (capacityIndex >= 0 && capacityIndex < capacities.Count)
                    {
                        var capacity = capacities[capacityIndex];
                        sb.AppendLine(capacity.DetailedBreakdown);
                    }
                    break;

                case MenuLevel.OperationsList:
                    if (operationIndex < queuedOperations.Count)
                    {
                        var bill = queuedOperations[operationIndex];
                        sb.AppendLine($"Queued: {bill.LabelCap.StripTags()}");
                        sb.AppendLine($"Operation {MenuHelper.FormatPosition(operationIndex, queuedOperations.Count + 1)}");
                        sb.AppendLine("Press Enter for actions");
                    }
                    else
                    {
                        sb.AppendLine("Add Operation");
                        sb.AppendLine($"Operation {MenuHelper.FormatPosition(operationIndex, queuedOperations.Count + 1)}");
                        sb.AppendLine("Press Enter to add");
                    }
                    break;

                case MenuLevel.OperationActions:
                    sb.AppendLine($"{operationActions[operationActionIndex]}");
                    sb.AppendLine($"Action {MenuHelper.FormatPosition(operationActionIndex, operationActions.Count)}");
                    sb.AppendLine("Press Enter to execute");
                    break;

                case MenuLevel.AddOperationList:
                    if (operationIndex >= 0 && operationIndex < availableOperations.Count)
                    {
                        var op = availableOperations[operationIndex];
                        sb.AppendLine($"{op.Label}");
                        if (!string.IsNullOrEmpty(op.Requirements))
                        {
                            sb.AppendLine(op.Requirements);
                        }
                        if (!op.IsAvailable)
                        {
                            sb.AppendLine($"Unavailable: {op.UnavailableReason}");
                        }
                        sb.AppendLine($"Operation {MenuHelper.FormatPosition(operationIndex, availableOperations.Count)}");
                        sb.AppendLine("Press Enter to add");
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

                        sb.AppendLine($"Recipe {MenuHelper.FormatPosition(recipeIndex, availableRecipes.Count)}");
                        sb.AppendLine("Press Enter to select");
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

                        sb.AppendLine($"Part {MenuHelper.FormatPosition(partSelectionIndex, partsForRecipe.Count)}");
                        sb.AppendLine("Press Enter to add operation");
                    }
                    break;

                case MenuLevel.BodyPartsList:
                    if (bodyPartIndex >= 0 && bodyPartIndex < bodyParts.Count)
                    {
                        var part = bodyParts[bodyPartIndex];
                        sb.AppendLine($"{part.Label}");
                        if (part.MaxHealth > 0)
                        {
                            sb.AppendLine($"Health: {part.Health:F0} / {part.MaxHealth:F0} ({part.Efficiency:P0})");
                        }
                        sb.AppendLine($"Conditions: {part.Hediffs.Count}");
                        sb.AppendLine($"Part {MenuHelper.FormatPosition(bodyPartIndex, bodyParts.Count)}");
                        sb.AppendLine("Press Enter to view conditions");
                    }
                    break;

                case MenuLevel.HediffsList:
                    if (bodyPartIndex >= 0 && bodyPartIndex < bodyParts.Count)
                    {
                        var part = bodyParts[bodyPartIndex];
                        if (hediffIndex >= 0 && hediffIndex < part.Hediffs.Count)
                        {
                            var hediff = part.Hediffs[hediffIndex];
                            sb.AppendLine($"{hediff.Label}");
                            sb.AppendLine($"Condition {MenuHelper.FormatPosition(hediffIndex, part.Hediffs.Count)} on {part.Label}");
                            sb.AppendLine("Press Enter for details");
                        }
                    }
                    break;

                case MenuLevel.HediffDetail:
                    if (bodyPartIndex >= 0 && bodyPartIndex < bodyParts.Count)
                    {
                        var part = bodyParts[bodyPartIndex];
                        if (hediffIndex >= 0 && hediffIndex < part.Hediffs.Count)
                        {
                            var hediff = part.Hediffs[hediffIndex];
                            sb.AppendLine(hediff.DetailedInfo);
                        }
                    }
                    break;
            }

            TolkHelper.Speak(sb.ToString());
        }
    }
}
