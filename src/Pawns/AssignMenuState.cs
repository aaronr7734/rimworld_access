using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Column types for the assign menu.
    /// </summary>
    public enum ColumnType
    {
        Outfit,
        FoodRestrictions,
        DrugPolicies,
        AllowedAreas,
        ReadingPolicies,
        MedicineCarry,
        HostilityResponse
    }

    /// <summary>
    /// Manages the state and navigation for the interactive assign menu.
    /// Tracks pawns and their assignments across multiple columns.
    /// </summary>
    public static class AssignMenuState
    {
        private static bool isActive = false;
        private static Pawn currentPawn = null;
        private static int currentPawnIndex = 0;
        private static List<Pawn> allPawns = new List<Pawn>();

        // Column navigation - uses dynamic column list based on available features
        private static List<ColumnType> activeColumns = new List<ColumnType>();
        private static int currentColumnIndex = 0;
        private static int selectedOptionIndex = 0;

        // Typeahead search
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        // Column option lists
        private static List<ApparelPolicy> outfitPolicies = new List<ApparelPolicy>();
        private static List<FoodPolicy> foodPolicies = new List<FoodPolicy>();
        private static List<DrugPolicy> drugPolicies = new List<DrugPolicy>();
        private static List<AreaOption> areaOptions = new List<AreaOption>();
        private static List<ReadingPolicy> readingPolicies = new List<ReadingPolicy>();
        private static List<MedicineCarryOption> medicineCarryOptions = new List<MedicineCarryOption>();
        private static List<HostilityResponseMode> hostilityOptions = new List<HostilityResponseMode>();

        // Column names for announcements
        private static readonly Dictionary<ColumnType, string> columnNames = new Dictionary<ColumnType, string>
        {
            { ColumnType.Outfit, "Outfit" },
            { ColumnType.FoodRestrictions, "Food Restrictions" },
            { ColumnType.DrugPolicies, "Drug Policies" },
            { ColumnType.AllowedAreas, "Allowed Areas" },
            { ColumnType.ReadingPolicies, "Reading Policies" },
            { ColumnType.MedicineCarry, "Medicine Carry" },
            { ColumnType.HostilityResponse, "Hostility Response" }
        };

        public static bool IsActive => isActive;
        public static Pawn CurrentPawn => currentPawn;
        public static int CurrentPawnIndex => currentPawnIndex;
        public static int TotalPawns => allPawns.Count;
        public static int CurrentColumnIndex => currentColumnIndex;
        public static bool HasActiveSearch => typeahead.HasActiveSearch;
        public static bool HasNoMatches => typeahead.HasNoMatches;

        /// <summary>
        /// Opens the assign menu for the specified pawn.
        /// </summary>
        public static void Open(Pawn pawn)
        {
            if (pawn == null)
                return;

            isActive = true;
            currentPawn = pawn;
            currentColumnIndex = 0;
            selectedOptionIndex = 0;
            typeahead.ClearSearch();

            // Build list of all colonists
            allPawns.Clear();
            if (Find.CurrentMap != null)
            {
                allPawns = Find.CurrentMap.mapPawns.FreeColonists.ToList();
                currentPawnIndex = allPawns.IndexOf(pawn);
                if (currentPawnIndex < 0)
                    currentPawnIndex = 0;
            }

            RebuildActiveColumns();
            LoadAllPolicies();
            TolkHelper.Speak("Assign menu");
            UpdateClipboard();
        }

        /// <summary>
        /// Rebuilds the list of active columns based on available features for current pawn.
        /// </summary>
        private static void RebuildActiveColumns()
        {
            activeColumns.Clear();

            // Base columns always available
            activeColumns.Add(ColumnType.Outfit);
            activeColumns.Add(ColumnType.FoodRestrictions);
            activeColumns.Add(ColumnType.DrugPolicies);
            activeColumns.Add(ColumnType.AllowedAreas);

            // Reading policies only with Ideology DLC
            if (ModsConfig.IdeologyActive)
            {
                activeColumns.Add(ColumnType.ReadingPolicies);
            }

            // Medicine carry only if pawn has inventoryStock
            if (currentPawn?.inventoryStock != null)
            {
                activeColumns.Add(ColumnType.MedicineCarry);
            }

            // Hostility response only for humanlike pawns
            if (currentPawn?.playerSettings != null && currentPawn.RaceProps.Humanlike)
            {
                activeColumns.Add(ColumnType.HostilityResponse);
            }
        }

        /// <summary>
        /// Loads all policy databases and area lists.
        /// </summary>
        private static void LoadAllPolicies()
        {
            // Load outfits
            outfitPolicies.Clear();
            if (Current.Game?.outfitDatabase != null)
            {
                outfitPolicies = Current.Game.outfitDatabase.AllOutfits.ToList();
            }

            // Load food restrictions
            foodPolicies.Clear();
            if (Current.Game?.foodRestrictionDatabase != null)
            {
                foodPolicies = Current.Game.foodRestrictionDatabase.AllFoodRestrictions.ToList();
            }

            // Load drug policies
            drugPolicies.Clear();
            if (Current.Game?.drugPolicyDatabase != null)
            {
                drugPolicies = Current.Game.drugPolicyDatabase.AllPolicies.ToList();
            }

            // Load allowed areas
            areaOptions.Clear();
            if (Find.CurrentMap?.areaManager != null)
            {
                // Add "Unrestricted" as first option
                areaOptions.Add(new AreaOption { Label = "Unrestricted", Area = null });

                // Add all assignable areas
                var areas = Find.CurrentMap.areaManager.AllAreas
                    .Where(a => a.AssignableAsAllowed())
                    .OrderBy(a => a.Label);

                foreach (var area in areas)
                {
                    areaOptions.Add(new AreaOption { Label = area.Label, Area = area });
                }
            }

            // Load reading policies (if DLC is active)
            readingPolicies.Clear();
            if (ModsConfig.IdeologyActive && Current.Game?.readingPolicyDatabase != null)
            {
                readingPolicies = Current.Game.readingPolicyDatabase.AllReadingPolicies.ToList();
            }

            // Load medicine carry options (0 to max medicine count)
            medicineCarryOptions.Clear();
            if (currentPawn?.inventoryStock != null && InventoryStockGroupDefOf.Medicine != null)
            {
                var medicineGroup = InventoryStockGroupDefOf.Medicine;
                // Group by medicine type first, then by count
                foreach (var medicineDef in medicineGroup.thingDefs)
                {
                    for (int i = medicineGroup.min; i <= medicineGroup.max; i++)
                    {
                        medicineCarryOptions.Add(new MedicineCarryOption
                        {
                            Count = i,
                            MedicineDef = medicineDef,
                            Label = $"{medicineDef.label} x{i}"
                        });
                    }
                }
            }

            // Load hostility response options
            hostilityOptions.Clear();
            if (currentPawn?.playerSettings != null && currentPawn.RaceProps.Humanlike)
            {
                hostilityOptions.Add(HostilityResponseMode.Ignore);
                // Only add Attack if pawn doesn't have WorkTags.Violent disabled
                if (!currentPawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    hostilityOptions.Add(HostilityResponseMode.Attack);
                }
                hostilityOptions.Add(HostilityResponseMode.Flee);
            }
        }

        /// <summary>
        /// Closes the menu.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentPawn = null;
            currentPawnIndex = 0;
            allPawns.Clear();
            currentColumnIndex = 0;
            selectedOptionIndex = 0;
            typeahead.ClearSearch();

            activeColumns.Clear();
            outfitPolicies.Clear();
            foodPolicies.Clear();
            drugPolicies.Clear();
            areaOptions.Clear();
            readingPolicies.Clear();
            medicineCarryOptions.Clear();
            hostilityOptions.Clear();

            TolkHelper.Speak("Assign menu closed");
        }

        /// <summary>
        /// Switches to the next column (does not wrap).
        /// </summary>
        public static void SelectNextColumn()
        {
            int totalColumns = GetTotalColumns();
            if (totalColumns == 0)
                return;

            currentColumnIndex = MenuHelper.SelectNext(currentColumnIndex, totalColumns);
            selectedOptionIndex = GetCurrentOptionIndex();
            typeahead.ClearSearch();
            UpdateClipboard();
        }

        /// <summary>
        /// Switches to the previous column (does not wrap).
        /// </summary>
        public static void SelectPreviousColumn()
        {
            int totalColumns = GetTotalColumns();
            if (totalColumns == 0)
                return;

            currentColumnIndex = MenuHelper.SelectPrevious(currentColumnIndex, totalColumns);

            selectedOptionIndex = GetCurrentOptionIndex();
            typeahead.ClearSearch();
            UpdateClipboard();
        }

        /// <summary>
        /// Moves selection to next option in current column (does not wrap).
        /// </summary>
        public static void SelectNextOption()
        {
            int optionCount = GetCurrentColumnOptionCount();
            if (optionCount == 0)
            {
                TolkHelper.Speak("No options available");
                return;
            }

            selectedOptionIndex = MenuHelper.SelectNext(selectedOptionIndex, optionCount);
            UpdateClipboard();
        }

        /// <summary>
        /// Moves selection to previous option in current column (does not wrap).
        /// </summary>
        public static void SelectPreviousOption()
        {
            int optionCount = GetCurrentColumnOptionCount();
            if (optionCount == 0)
            {
                TolkHelper.Speak("No options available");
                return;
            }

            selectedOptionIndex = MenuHelper.SelectPrevious(selectedOptionIndex, optionCount);

            UpdateClipboard();
        }

        /// <summary>
        /// Applies the currently selected option to the current pawn.
        /// </summary>
        public static void ApplySelection()
        {
            if (currentPawn == null || currentColumnIndex < 0 || currentColumnIndex >= activeColumns.Count)
                return;

            string result = "";

            switch (activeColumns[currentColumnIndex])
            {
                case ColumnType.Outfit:
                    if (currentPawn.outfits != null && selectedOptionIndex >= 0 && selectedOptionIndex < outfitPolicies.Count)
                    {
                        var policy = outfitPolicies[selectedOptionIndex];
                        currentPawn.outfits.CurrentApparelPolicy = policy;
                        result = $"{currentPawn.LabelShort}: Outfit set to {policy.label}";
                    }
                    break;

                case ColumnType.FoodRestrictions:
                    if (currentPawn.foodRestriction != null && selectedOptionIndex >= 0 && selectedOptionIndex < foodPolicies.Count)
                    {
                        var policy = foodPolicies[selectedOptionIndex];
                        currentPawn.foodRestriction.CurrentFoodPolicy = policy;
                        result = $"{currentPawn.LabelShort}: Food restriction set to {policy.label}";
                    }
                    break;

                case ColumnType.DrugPolicies:
                    if (currentPawn.drugs != null && selectedOptionIndex >= 0 && selectedOptionIndex < drugPolicies.Count)
                    {
                        var policy = drugPolicies[selectedOptionIndex];
                        currentPawn.drugs.CurrentPolicy = policy;
                        result = $"{currentPawn.LabelShort}: Drug policy set to {policy.label}";
                    }
                    break;

                case ColumnType.AllowedAreas:
                    if (currentPawn.playerSettings != null && selectedOptionIndex >= 0 && selectedOptionIndex < areaOptions.Count)
                    {
                        var areaOption = areaOptions[selectedOptionIndex];
                        currentPawn.playerSettings.AreaRestrictionInPawnCurrentMap = areaOption.Area;
                        result = $"{currentPawn.LabelShort}: Allowed area set to {areaOption.Label}";
                    }
                    break;

                case ColumnType.ReadingPolicies:
                    if (ModsConfig.IdeologyActive && currentPawn.reading != null &&
                        selectedOptionIndex >= 0 && selectedOptionIndex < readingPolicies.Count)
                    {
                        var policy = readingPolicies[selectedOptionIndex];
                        currentPawn.reading.CurrentPolicy = policy;
                        result = $"{currentPawn.LabelShort}: Reading policy set to {policy.label}";
                    }
                    break;

                case ColumnType.MedicineCarry:
                    if (currentPawn.inventoryStock != null && InventoryStockGroupDefOf.Medicine != null &&
                        selectedOptionIndex >= 0 && selectedOptionIndex < medicineCarryOptions.Count)
                    {
                        var option = medicineCarryOptions[selectedOptionIndex];
                        var medicineGroup = InventoryStockGroupDefOf.Medicine;
                        currentPawn.inventoryStock.SetCountForGroup(medicineGroup, option.Count);
                        currentPawn.inventoryStock.SetThingForGroup(medicineGroup, option.MedicineDef);
                        result = $"{currentPawn.LabelShort}: Medicine carry set to {option.Label}";
                    }
                    break;

                case ColumnType.HostilityResponse:
                    if (currentPawn.playerSettings != null &&
                        selectedOptionIndex >= 0 && selectedOptionIndex < hostilityOptions.Count)
                    {
                        var response = hostilityOptions[selectedOptionIndex];
                        currentPawn.playerSettings.hostilityResponse = response;
                        string modeName = HostilityResponseModeUtility.GetLabel(response);
                        result = $"{currentPawn.LabelShort}: Hostility response set to {modeName}";
                    }
                    break;
            }

            if (!string.IsNullOrEmpty(result))
            {
                TolkHelper.Speak(result);
            }
        }

        /// <summary>
        /// Opens the management dialog for the current column type.
        /// Allows creating and editing policies or areas.
        /// </summary>
        public static void OpenManagementDialog()
        {
            if (currentColumnIndex < 0 || currentColumnIndex >= activeColumns.Count)
                return;

            switch (activeColumns[currentColumnIndex])
            {
                case ColumnType.Outfit: // Outfit - Open windowless apparel policies manager
                    if (Current.Game?.outfitDatabase != null)
                    {
                        // Pass the current pawn's outfit policy to open that policy for editing
                        ApparelPolicy currentPolicy = currentPawn?.outfits?.CurrentApparelPolicy;

                        // Close the assign menu before opening the policy editor
                        Close();

                        WindowlessOutfitPolicyState.Open(currentPolicy);
                        TolkHelper.Speak("Opened apparel policies manager");
                    }
                    break;

                case ColumnType.FoodRestrictions: // Food Restrictions - Open windowless food policies manager
                    if (Current.Game?.foodRestrictionDatabase != null)
                    {
                        // Pass the current pawn's food policy to open that policy for editing
                        FoodPolicy currentPolicy = currentPawn?.foodRestriction?.CurrentFoodPolicy;

                        // Close the assign menu before opening the policy editor
                        Close();

                        WindowlessFoodPolicyState.Open(currentPolicy);
                        TolkHelper.Speak("Opened food policies manager");
                    }
                    break;

                case ColumnType.DrugPolicies: // Drug Policies - Open windowless drug policies manager
                    if (Current.Game?.drugPolicyDatabase != null)
                    {
                        // Pass the current pawn's drug policy to open that policy for editing
                        DrugPolicy currentPolicy = currentPawn?.drugs?.CurrentPolicy;

                        // Close the assign menu before opening the policy editor
                        Close();

                        WindowlessDrugPolicyState.Open(currentPolicy);
                        TolkHelper.Speak("Opened drug policies manager");
                    }
                    break;

                case ColumnType.AllowedAreas: // Allowed Areas - Open windowless areas manager
                    if (Find.CurrentMap?.areaManager != null)
                    {
                        // Close the assign menu before opening the area manager
                        Close();

                        WindowlessAreaState.Open(Find.CurrentMap);
                        TolkHelper.Speak("Opened areas manager");
                    }
                    break;

                case ColumnType.ReadingPolicies: // Reading Policies - Open reading policies dialog (Ideology DLC)
                    if (ModsConfig.IdeologyActive && Current.Game?.readingPolicyDatabase != null)
                    {
                        // Pass the current pawn's reading policy to open that policy for editing
                        ReadingPolicy currentPolicy = currentPawn?.reading?.CurrentPolicy;
                        Find.WindowStack.Add(new Dialog_ManageReadingPolicies(currentPolicy));
                        TolkHelper.Speak("Opened reading policies manager");
                    }
                    break;
            }
        }

        /// <summary>
        /// Switches to the next pawn in the list (does not wrap).
        /// </summary>
        public static void SwitchToNextPawn()
        {
            if (allPawns.Count == 0)
                return;

            currentPawnIndex = MenuHelper.SelectNext(currentPawnIndex, allPawns.Count);
            currentPawn = allPawns[currentPawnIndex];
            RebuildActiveColumns();
            LoadAllPolicies();
            selectedOptionIndex = GetCurrentOptionIndex();
            typeahead.ClearSearch();

            TolkHelper.Speak($"Now editing: {currentPawn.LabelShort}. {MenuHelper.FormatPosition(currentPawnIndex, allPawns.Count)}");
        }

        /// <summary>
        /// Switches to the previous pawn in the list (does not wrap).
        /// </summary>
        public static void SwitchToPreviousPawn()
        {
            if (allPawns.Count == 0)
                return;

            currentPawnIndex = MenuHelper.SelectPrevious(currentPawnIndex, allPawns.Count);

            currentPawn = allPawns[currentPawnIndex];
            RebuildActiveColumns();
            LoadAllPolicies();
            selectedOptionIndex = GetCurrentOptionIndex();
            typeahead.ClearSearch();

            TolkHelper.Speak($"Now editing: {currentPawn.LabelShort}. {MenuHelper.FormatPosition(currentPawnIndex, allPawns.Count)}");
        }

        /// <summary>
        /// Gets the number of columns available (may vary depending on DLC and pawn type).
        /// </summary>
        private static int GetTotalColumns()
        {
            return activeColumns.Count;
        }

        /// <summary>
        /// Gets the number of options in the current column.
        /// </summary>
        private static int GetCurrentColumnOptionCount()
        {
            if (currentColumnIndex < 0 || currentColumnIndex >= activeColumns.Count)
                return 0;

            switch (activeColumns[currentColumnIndex])
            {
                case ColumnType.Outfit: return outfitPolicies.Count;
                case ColumnType.FoodRestrictions: return foodPolicies.Count;
                case ColumnType.DrugPolicies: return drugPolicies.Count;
                case ColumnType.AllowedAreas: return areaOptions.Count;
                case ColumnType.ReadingPolicies: return readingPolicies.Count;
                case ColumnType.MedicineCarry: return medicineCarryOptions.Count;
                case ColumnType.HostilityResponse: return hostilityOptions.Count;
                default: return 0;
            }
        }

        /// <summary>
        /// Gets the current option index for the current pawn and column.
        /// </summary>
        private static int GetCurrentOptionIndex()
        {
            if (currentPawn == null || currentColumnIndex < 0 || currentColumnIndex >= activeColumns.Count)
                return 0;

            switch (activeColumns[currentColumnIndex])
            {
                case ColumnType.Outfit:
                    if (currentPawn.outfits != null && currentPawn.outfits.CurrentApparelPolicy != null)
                    {
                        return outfitPolicies.IndexOf(currentPawn.outfits.CurrentApparelPolicy);
                    }
                    break;

                case ColumnType.FoodRestrictions:
                    if (currentPawn.foodRestriction != null && currentPawn.foodRestriction.CurrentFoodPolicy != null)
                    {
                        return foodPolicies.IndexOf(currentPawn.foodRestriction.CurrentFoodPolicy);
                    }
                    break;

                case ColumnType.DrugPolicies:
                    if (currentPawn.drugs != null && currentPawn.drugs.CurrentPolicy != null)
                    {
                        return drugPolicies.IndexOf(currentPawn.drugs.CurrentPolicy);
                    }
                    break;

                case ColumnType.AllowedAreas:
                    if (currentPawn.playerSettings != null)
                    {
                        var currentArea = currentPawn.playerSettings.AreaRestrictionInPawnCurrentMap;
                        int index = areaOptions.FindIndex(a => a.Area == currentArea);
                        return index >= 0 ? index : 0;
                    }
                    break;

                case ColumnType.ReadingPolicies:
                    if (ModsConfig.IdeologyActive && currentPawn.reading != null && currentPawn.reading.CurrentPolicy != null)
                    {
                        return readingPolicies.IndexOf(currentPawn.reading.CurrentPolicy);
                    }
                    break;

                case ColumnType.MedicineCarry:
                    if (currentPawn.inventoryStock != null && InventoryStockGroupDefOf.Medicine != null)
                    {
                        var medicineGroup = InventoryStockGroupDefOf.Medicine;
                        int currentCount = currentPawn.inventoryStock.GetDesiredCountForGroup(medicineGroup);
                        ThingDef currentMedicine = currentPawn.inventoryStock.GetDesiredThingForGroup(medicineGroup);

                        // Find the option that matches current count and medicine type
                        int index = medicineCarryOptions.FindIndex(o =>
                            o.Count == currentCount && o.MedicineDef == currentMedicine);
                        return index >= 0 ? index : 0;
                    }
                    break;

                case ColumnType.HostilityResponse:
                    if (currentPawn.playerSettings != null)
                    {
                        return hostilityOptions.IndexOf(currentPawn.playerSettings.hostilityResponse);
                    }
                    break;
            }

            return 0;
        }

        /// <summary>
        /// Gets the current selection as a formatted string for screen reader.
        /// </summary>
        private static void UpdateClipboard()
        {
            if (currentPawn == null)
            {
                TolkHelper.Speak("No pawn selected");
                return;
            }

            if (currentColumnIndex < 0 || currentColumnIndex >= activeColumns.Count)
            {
                TolkHelper.Speak("Invalid column");
                return;
            }

            ColumnType currentColumn = activeColumns[currentColumnIndex];
            string columnName = columnNames[currentColumn];
            string optionName = GetCurrentOptionName();
            int optionCount = GetCurrentColumnOptionCount();

            string message = $"{currentPawn.LabelShort} - {columnName}: {optionName}. {MenuHelper.FormatPosition(selectedOptionIndex, optionCount)}";
            TolkHelper.Speak(message);
        }

        /// <summary>
        /// Gets the name of the currently selected option.
        /// </summary>
        private static string GetCurrentOptionName()
        {
            if (selectedOptionIndex < 0 || currentColumnIndex < 0 || currentColumnIndex >= activeColumns.Count)
                return "None";

            switch (activeColumns[currentColumnIndex])
            {
                case ColumnType.Outfit:
                    if (selectedOptionIndex < outfitPolicies.Count)
                        return outfitPolicies[selectedOptionIndex].label;
                    break;

                case ColumnType.FoodRestrictions:
                    if (selectedOptionIndex < foodPolicies.Count)
                        return foodPolicies[selectedOptionIndex].label;
                    break;

                case ColumnType.DrugPolicies:
                    if (selectedOptionIndex < drugPolicies.Count)
                        return drugPolicies[selectedOptionIndex].label;
                    break;

                case ColumnType.AllowedAreas:
                    if (selectedOptionIndex < areaOptions.Count)
                        return areaOptions[selectedOptionIndex].Label;
                    break;

                case ColumnType.ReadingPolicies:
                    if (selectedOptionIndex < readingPolicies.Count)
                        return readingPolicies[selectedOptionIndex].label;
                    break;

                case ColumnType.MedicineCarry:
                    if (selectedOptionIndex < medicineCarryOptions.Count)
                        return medicineCarryOptions[selectedOptionIndex].Label;
                    break;

                case ColumnType.HostilityResponse:
                    if (selectedOptionIndex < hostilityOptions.Count)
                        return HostilityResponseModeUtility.GetLabel(hostilityOptions[selectedOptionIndex]);
                    break;
            }

            return "Unknown";
        }

        /// <summary>
        /// Processes a character for typeahead search in the current column.
        /// </summary>
        public static void ProcessTypeaheadCharacter(char c)
        {
            var labels = GetItemLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedOptionIndex = newIndex;
                    AnnounceWithSearch();
                }
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
            }
        }

        /// <summary>
        /// Handles backspace key for typeahead search.
        /// </summary>
        public static void ProcessBackspace()
        {
            if (!typeahead.HasActiveSearch)
                return;

            var labels = GetItemLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedOptionIndex = newIndex;
                }
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Clears the typeahead search and announces.
        /// </summary>
        public static void ClearTypeaheadSearch()
        {
            typeahead.ClearSearchAndAnnounce();
        }

        /// <summary>
        /// Jumps to the first option in the current column.
        /// </summary>
        public static void JumpToFirst()
        {
            int optionCount = GetCurrentColumnOptionCount();
            if (optionCount == 0)
                return;

            selectedOptionIndex = MenuHelper.JumpToFirst();
            typeahead.ClearSearch();
            UpdateClipboard();
        }

        /// <summary>
        /// Jumps to the last option in the current column.
        /// </summary>
        public static void JumpToLast()
        {
            int optionCount = GetCurrentColumnOptionCount();
            if (optionCount == 0)
                return;

            selectedOptionIndex = MenuHelper.JumpToLast(optionCount);
            typeahead.ClearSearch();
            UpdateClipboard();
        }

        /// <summary>
        /// Selects the next match in the typeahead search results.
        /// </summary>
        public static void SelectNextMatch()
        {
            if (!typeahead.HasActiveSearch || typeahead.HasNoMatches)
                return;

            int nextIndex = typeahead.GetNextMatch(selectedOptionIndex);
            if (nextIndex >= 0)
            {
                selectedOptionIndex = nextIndex;
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Selects the previous match in the typeahead search results.
        /// </summary>
        public static void SelectPreviousMatch()
        {
            if (!typeahead.HasActiveSearch || typeahead.HasNoMatches)
                return;

            int prevIndex = typeahead.GetPreviousMatch(selectedOptionIndex);
            if (prevIndex >= 0)
            {
                selectedOptionIndex = prevIndex;
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Gets the labels for all options in the current column.
        /// </summary>
        private static List<string> GetItemLabels()
        {
            var labels = new List<string>();

            if (currentColumnIndex < 0 || currentColumnIndex >= activeColumns.Count)
                return labels;

            switch (activeColumns[currentColumnIndex])
            {
                case ColumnType.Outfit:
                    foreach (var policy in outfitPolicies)
                        labels.Add(policy.label ?? "");
                    break;

                case ColumnType.FoodRestrictions:
                    foreach (var policy in foodPolicies)
                        labels.Add(policy.label ?? "");
                    break;

                case ColumnType.DrugPolicies:
                    foreach (var policy in drugPolicies)
                        labels.Add(policy.label ?? "");
                    break;

                case ColumnType.AllowedAreas:
                    foreach (var area in areaOptions)
                        labels.Add(area.Label ?? "");
                    break;

                case ColumnType.ReadingPolicies:
                    foreach (var policy in readingPolicies)
                        labels.Add(policy.label ?? "");
                    break;

                case ColumnType.MedicineCarry:
                    foreach (var option in medicineCarryOptions)
                        labels.Add(option.Label ?? "");
                    break;

                case ColumnType.HostilityResponse:
                    foreach (var response in hostilityOptions)
                        labels.Add(HostilityResponseModeUtility.GetLabel(response) ?? "");
                    break;
            }

            return labels;
        }

        /// <summary>
        /// Announces the current selection with search context if applicable.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (currentPawn == null)
            {
                TolkHelper.Speak("No pawn selected");
                return;
            }

            if (currentColumnIndex < 0 || currentColumnIndex >= activeColumns.Count)
            {
                TolkHelper.Speak("Invalid column");
                return;
            }

            ColumnType currentColumn = activeColumns[currentColumnIndex];
            string columnName = columnNames[currentColumn];
            string optionName = GetCurrentOptionName();
            int optionCount = GetCurrentColumnOptionCount();

            string message = $"{currentPawn.LabelShort} - {columnName}: {optionName}. {MenuHelper.FormatPosition(selectedOptionIndex, optionCount)}";

            if (typeahead.HasActiveSearch)
            {
                message += $", match {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} for '{typeahead.SearchBuffer}'";
            }

            TolkHelper.Speak(message);
        }

        /// <summary>
        /// Represents an area option (including "Unrestricted" as null area).
        /// </summary>
        public class AreaOption
        {
            public string Label { get; set; }
            public Area Area { get; set; }
        }

        /// <summary>
        /// Represents a medicine carry option (medicine type and count).
        /// </summary>
        public class MedicineCarryOption
        {
            public int Count { get; set; }
            public ThingDef MedicineDef { get; set; }
            public string Label { get; set; }
        }
    }
}
