using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages the state and navigation for the interactive assign menu.
    /// Tracks pawns and their assignments across 5 columns:
    /// Outfit, Food Restrictions, Drug Policies, Allowed Areas, and Reading Policies.
    /// </summary>
    public static class AssignMenuState
    {
        private static bool isActive = false;
        private static Pawn currentPawn = null;
        private static int currentPawnIndex = 0;
        private static List<Pawn> allPawns = new List<Pawn>();

        // Column navigation (0=Outfit, 1=Food, 2=Drugs, 3=Areas, 4=Reading)
        private static int currentColumnIndex = 0;
        private static int selectedOptionIndex = 0;

        // Column option lists
        private static List<ApparelPolicy> outfitPolicies = new List<ApparelPolicy>();
        private static List<FoodPolicy> foodPolicies = new List<FoodPolicy>();
        private static List<DrugPolicy> drugPolicies = new List<DrugPolicy>();
        private static List<AreaOption> areaOptions = new List<AreaOption>();
        private static List<ReadingPolicy> readingPolicies = new List<ReadingPolicy>();

        // Column names for announcements
        private static readonly string[] columnNames = new string[]
        {
            "Outfit",
            "Food Restrictions",
            "Drug Policies",
            "Allowed Areas",
            "Reading Policies"
        };

        public static bool IsActive => isActive;
        public static Pawn CurrentPawn => currentPawn;
        public static int CurrentPawnIndex => currentPawnIndex;
        public static int TotalPawns => allPawns.Count;
        public static int CurrentColumnIndex => currentColumnIndex;

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

            // Build list of all colonists
            allPawns.Clear();
            if (Find.CurrentMap != null)
            {
                allPawns = Find.CurrentMap.mapPawns.FreeColonists.ToList();
                currentPawnIndex = allPawns.IndexOf(pawn);
                if (currentPawnIndex < 0)
                    currentPawnIndex = 0;
            }

            LoadAllPolicies();
            UpdateClipboard();
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

            outfitPolicies.Clear();
            foodPolicies.Clear();
            drugPolicies.Clear();
            areaOptions.Clear();
            readingPolicies.Clear();

            ClipboardHelper.CopyToClipboard("Assign menu closed");
        }

        /// <summary>
        /// Switches to the next column (wraps around).
        /// </summary>
        public static void SelectNextColumn()
        {
            int totalColumns = GetTotalColumns();
            if (totalColumns == 0)
                return;

            currentColumnIndex = (currentColumnIndex + 1) % totalColumns;
            selectedOptionIndex = GetCurrentOptionIndex();
            UpdateClipboard();
        }

        /// <summary>
        /// Switches to the previous column (wraps around).
        /// </summary>
        public static void SelectPreviousColumn()
        {
            int totalColumns = GetTotalColumns();
            if (totalColumns == 0)
                return;

            currentColumnIndex--;
            if (currentColumnIndex < 0)
                currentColumnIndex = totalColumns - 1;

            selectedOptionIndex = GetCurrentOptionIndex();
            UpdateClipboard();
        }

        /// <summary>
        /// Moves selection to next option in current column (wraps around).
        /// </summary>
        public static void SelectNextOption()
        {
            int optionCount = GetCurrentColumnOptionCount();
            if (optionCount == 0)
            {
                ClipboardHelper.CopyToClipboard("No options available");
                return;
            }

            selectedOptionIndex = (selectedOptionIndex + 1) % optionCount;
            UpdateClipboard();
        }

        /// <summary>
        /// Moves selection to previous option in current column (wraps around).
        /// </summary>
        public static void SelectPreviousOption()
        {
            int optionCount = GetCurrentColumnOptionCount();
            if (optionCount == 0)
            {
                ClipboardHelper.CopyToClipboard("No options available");
                return;
            }

            selectedOptionIndex--;
            if (selectedOptionIndex < 0)
                selectedOptionIndex = optionCount - 1;

            UpdateClipboard();
        }

        /// <summary>
        /// Applies the currently selected option to the current pawn.
        /// </summary>
        public static void ApplySelection()
        {
            if (currentPawn == null)
                return;

            string result = "";

            switch (currentColumnIndex)
            {
                case 0: // Outfit
                    if (currentPawn.outfits != null && selectedOptionIndex >= 0 && selectedOptionIndex < outfitPolicies.Count)
                    {
                        var policy = outfitPolicies[selectedOptionIndex];
                        currentPawn.outfits.CurrentApparelPolicy = policy;
                        result = $"{currentPawn.LabelShort}: Outfit set to {policy.label}";
                    }
                    break;

                case 1: // Food Restrictions
                    if (currentPawn.foodRestriction != null && selectedOptionIndex >= 0 && selectedOptionIndex < foodPolicies.Count)
                    {
                        var policy = foodPolicies[selectedOptionIndex];
                        currentPawn.foodRestriction.CurrentFoodPolicy = policy;
                        result = $"{currentPawn.LabelShort}: Food restriction set to {policy.label}";
                    }
                    break;

                case 2: // Drug Policies
                    if (currentPawn.drugs != null && selectedOptionIndex >= 0 && selectedOptionIndex < drugPolicies.Count)
                    {
                        var policy = drugPolicies[selectedOptionIndex];
                        currentPawn.drugs.CurrentPolicy = policy;
                        result = $"{currentPawn.LabelShort}: Drug policy set to {policy.label}";
                    }
                    break;

                case 3: // Allowed Areas
                    if (currentPawn.playerSettings != null && selectedOptionIndex >= 0 && selectedOptionIndex < areaOptions.Count)
                    {
                        var areaOption = areaOptions[selectedOptionIndex];
                        currentPawn.playerSettings.AreaRestrictionInPawnCurrentMap = areaOption.Area;
                        result = $"{currentPawn.LabelShort}: Allowed area set to {areaOption.Label}";
                    }
                    break;

                case 4: // Reading Policies
                    if (ModsConfig.IdeologyActive && currentPawn.reading != null &&
                        selectedOptionIndex >= 0 && selectedOptionIndex < readingPolicies.Count)
                    {
                        var policy = readingPolicies[selectedOptionIndex];
                        currentPawn.reading.CurrentPolicy = policy;
                        result = $"{currentPawn.LabelShort}: Reading policy set to {policy.label}";
                    }
                    break;
            }

            if (!string.IsNullOrEmpty(result))
            {
                ClipboardHelper.CopyToClipboard(result);
            }
        }

        /// <summary>
        /// Opens the management dialog for the current column type.
        /// Allows creating and editing policies or areas.
        /// </summary>
        public static void OpenManagementDialog()
        {
            switch (currentColumnIndex)
            {
                case 0: // Outfit - Open windowless apparel policies manager
                    if (Current.Game?.outfitDatabase != null)
                    {
                        // Pass the current pawn's outfit policy to open that policy for editing
                        ApparelPolicy currentPolicy = currentPawn?.outfits?.CurrentApparelPolicy;

                        // Close the assign menu before opening the policy editor
                        Close();

                        WindowlessOutfitPolicyState.Open(currentPolicy);
                        ClipboardHelper.CopyToClipboard("Opened apparel policies manager");
                    }
                    break;

                case 1: // Food Restrictions - Open windowless food policies manager
                    if (Current.Game?.foodRestrictionDatabase != null)
                    {
                        // Pass the current pawn's food policy to open that policy for editing
                        FoodPolicy currentPolicy = currentPawn?.foodRestriction?.CurrentFoodPolicy;

                        // Close the assign menu before opening the policy editor
                        Close();

                        WindowlessFoodPolicyState.Open(currentPolicy);
                        ClipboardHelper.CopyToClipboard("Opened food policies manager");
                    }
                    break;

                case 2: // Drug Policies - Open drug policies dialog
                    if (Current.Game?.drugPolicyDatabase != null)
                    {
                        // Pass the current pawn's drug policy to open that policy for editing
                        DrugPolicy currentPolicy = currentPawn?.drugs?.CurrentPolicy;
                        Find.WindowStack.Add(new Dialog_ManageDrugPolicies(currentPolicy));
                        ClipboardHelper.CopyToClipboard("Opened drug policies manager");
                    }
                    break;

                case 3: // Allowed Areas - Open areas dialog
                    if (Find.CurrentMap?.areaManager != null)
                    {
                        Find.WindowStack.Add(new Dialog_ManageAreas(Find.CurrentMap));
                        ClipboardHelper.CopyToClipboard("Opened areas manager");
                    }
                    break;

                case 4: // Reading Policies - Open reading policies dialog (Ideology DLC)
                    if (ModsConfig.IdeologyActive && Current.Game?.readingPolicyDatabase != null)
                    {
                        // Pass the current pawn's reading policy to open that policy for editing
                        ReadingPolicy currentPolicy = currentPawn?.reading?.CurrentPolicy;
                        Find.WindowStack.Add(new Dialog_ManageReadingPolicies(currentPolicy));
                        ClipboardHelper.CopyToClipboard("Opened reading policies manager");
                    }
                    break;
            }
        }

        /// <summary>
        /// Switches to the next pawn in the list (wraps around).
        /// </summary>
        public static void SwitchToNextPawn()
        {
            if (allPawns.Count == 0)
                return;

            currentPawnIndex = (currentPawnIndex + 1) % allPawns.Count;
            currentPawn = allPawns[currentPawnIndex];
            selectedOptionIndex = GetCurrentOptionIndex();
            LoadAllPolicies();

            ClipboardHelper.CopyToClipboard($"Now editing: {currentPawn.LabelShort} ({currentPawnIndex + 1}/{allPawns.Count})");
        }

        /// <summary>
        /// Switches to the previous pawn in the list (wraps around).
        /// </summary>
        public static void SwitchToPreviousPawn()
        {
            if (allPawns.Count == 0)
                return;

            currentPawnIndex--;
            if (currentPawnIndex < 0)
                currentPawnIndex = allPawns.Count - 1;

            currentPawn = allPawns[currentPawnIndex];
            selectedOptionIndex = GetCurrentOptionIndex();
            LoadAllPolicies();

            ClipboardHelper.CopyToClipboard($"Now editing: {currentPawn.LabelShort} ({currentPawnIndex + 1}/{allPawns.Count})");
        }

        /// <summary>
        /// Gets the number of columns available (may be 4 or 5 depending on DLC).
        /// </summary>
        private static int GetTotalColumns()
        {
            // Reading column only available with Ideology DLC
            return ModsConfig.IdeologyActive ? 5 : 4;
        }

        /// <summary>
        /// Gets the number of options in the current column.
        /// </summary>
        private static int GetCurrentColumnOptionCount()
        {
            switch (currentColumnIndex)
            {
                case 0: return outfitPolicies.Count;
                case 1: return foodPolicies.Count;
                case 2: return drugPolicies.Count;
                case 3: return areaOptions.Count;
                case 4: return readingPolicies.Count;
                default: return 0;
            }
        }

        /// <summary>
        /// Gets the current option index for the current pawn and column.
        /// </summary>
        private static int GetCurrentOptionIndex()
        {
            if (currentPawn == null)
                return 0;

            switch (currentColumnIndex)
            {
                case 0: // Outfit
                    if (currentPawn.outfits != null && currentPawn.outfits.CurrentApparelPolicy != null)
                    {
                        return outfitPolicies.IndexOf(currentPawn.outfits.CurrentApparelPolicy);
                    }
                    break;

                case 1: // Food Restrictions
                    if (currentPawn.foodRestriction != null && currentPawn.foodRestriction.CurrentFoodPolicy != null)
                    {
                        return foodPolicies.IndexOf(currentPawn.foodRestriction.CurrentFoodPolicy);
                    }
                    break;

                case 2: // Drug Policies
                    if (currentPawn.drugs != null && currentPawn.drugs.CurrentPolicy != null)
                    {
                        return drugPolicies.IndexOf(currentPawn.drugs.CurrentPolicy);
                    }
                    break;

                case 3: // Allowed Areas
                    if (currentPawn.playerSettings != null)
                    {
                        var currentArea = currentPawn.playerSettings.AreaRestrictionInPawnCurrentMap;
                        int index = areaOptions.FindIndex(a => a.Area == currentArea);
                        return index >= 0 ? index : 0;
                    }
                    break;

                case 4: // Reading Policies
                    if (ModsConfig.IdeologyActive && currentPawn.reading != null && currentPawn.reading.CurrentPolicy != null)
                    {
                        return readingPolicies.IndexOf(currentPawn.reading.CurrentPolicy);
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
                ClipboardHelper.CopyToClipboard("No pawn selected");
                return;
            }

            string columnName = columnNames[currentColumnIndex];
            string optionName = GetCurrentOptionName();
            int optionCount = GetCurrentColumnOptionCount();

            string message = $"{currentPawn.LabelShort} - {columnName}: {optionName} ({selectedOptionIndex + 1}/{optionCount})";
            ClipboardHelper.CopyToClipboard(message);
        }

        /// <summary>
        /// Gets the name of the currently selected option.
        /// </summary>
        private static string GetCurrentOptionName()
        {
            if (selectedOptionIndex < 0)
                return "None";

            switch (currentColumnIndex)
            {
                case 0: // Outfit
                    if (selectedOptionIndex < outfitPolicies.Count)
                        return outfitPolicies[selectedOptionIndex].label;
                    break;

                case 1: // Food Restrictions
                    if (selectedOptionIndex < foodPolicies.Count)
                        return foodPolicies[selectedOptionIndex].label;
                    break;

                case 2: // Drug Policies
                    if (selectedOptionIndex < drugPolicies.Count)
                        return drugPolicies[selectedOptionIndex].label;
                    break;

                case 3: // Allowed Areas
                    if (selectedOptionIndex < areaOptions.Count)
                        return areaOptions[selectedOptionIndex].Label;
                    break;

                case 4: // Reading Policies
                    if (selectedOptionIndex < readingPolicies.Count)
                        return readingPolicies[selectedOptionIndex].label;
                    break;
            }

            return "Unknown";
        }

        /// <summary>
        /// Represents an area option (including "Unrestricted" as null area).
        /// </summary>
        public class AreaOption
        {
            public string Label { get; set; }
            public Area Area { get; set; }
        }
    }
}
