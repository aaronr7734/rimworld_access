using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages a windowless bills menu for crafting stations.
    /// Provides keyboard navigation to add, configure, and delete bills.
    /// </summary>
    public static class BillsMenuState
    {
        private static IBillGiver billGiver = null;
        private static IntVec3 billGiverPos;
        private static List<MenuItem> menuItems = null;
        private static int selectedIndex = 0;
        private static bool isActive = false;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        private enum MenuItemType
        {
            AddBill,
            ExistingBill,
            PasteBill
        }

        private class MenuItem
        {
            public MenuItemType type;
            public string label;
            public object data; // Bill for ExistingBill, RecipeDef for AddBill
            public bool isEnabled;

            public MenuItem(MenuItemType type, string label, object data, bool enabled = true)
            {
                this.type = type;
                this.label = label;
                this.data = data;
                this.isEnabled = enabled;
            }
        }

        public static bool IsActive => isActive;
        public static bool HasActiveSearch => typeahead.HasActiveSearch;
        public static bool HasNoMatches => typeahead.HasNoMatches;
        public static int SelectedIndex => selectedIndex;

        /// <summary>
        /// Opens the bills menu for the given bill giver.
        /// </summary>
        public static void Open(IBillGiver giver, IntVec3 position)
        {
            if (giver == null)
            {
                Log.Error("Cannot open bills menu: giver is null");
                return;
            }

            billGiver = giver;
            billGiverPos = position;
            menuItems = new List<MenuItem>();
            selectedIndex = 0;
            isActive = true;
            typeahead.ClearSearch();

            BuildMenuItems();
            AnnounceCurrentSelection();

            Log.Message($"Opened bills menu with {menuItems.Count} items");
        }

        /// <summary>
        /// Closes the bills menu.
        /// </summary>
        public static void Close()
        {
            billGiver = null;
            menuItems = null;
            selectedIndex = 0;
            isActive = false;
            typeahead.ClearSearch();
        }

        /// <summary>
        /// Builds the menu item list.
        /// </summary>
        private static void BuildMenuItems()
        {
            menuItems.Clear();

            // Add "Add new bill" option
            menuItems.Add(new MenuItem(MenuItemType.AddBill, "Add new bill...", null));

            // Add paste bill option if clipboard has a bill
            if (BillUtility.Clipboard != null)
            {
                Building_WorkTable workTable = billGiver as Building_WorkTable;
                bool canPaste = false;
                string pasteLabel = "Paste bill";

                if (workTable != null)
                {
                    if (!workTable.def.AllRecipes.Contains(BillUtility.Clipboard.recipe) ||
                        !BillUtility.Clipboard.recipe.AvailableNow ||
                        !BillUtility.Clipboard.recipe.AvailableOnNow(workTable))
                    {
                        pasteLabel = $"Paste bill (not available here): {BillUtility.Clipboard.LabelCap}";
                        canPaste = false;
                    }
                    else if (billGiver.BillStack.Count >= 15)
                    {
                        pasteLabel = $"Paste bill (limit reached): {BillUtility.Clipboard.LabelCap}";
                        canPaste = false;
                    }
                    else
                    {
                        pasteLabel = $"Paste bill: {BillUtility.Clipboard.LabelCap}";
                        canPaste = true;
                    }
                }

                menuItems.Add(new MenuItem(MenuItemType.PasteBill, pasteLabel, BillUtility.Clipboard, canPaste));
            }

            // Add existing bills
            if (billGiver.BillStack != null)
            {
                for (int i = 0; i < billGiver.BillStack.Count; i++)
                {
                    Bill bill = billGiver.BillStack[i];
                    string billLabel = $"{i + 1}. {bill.LabelCap}";

                    // Add cost information
                    string costInfo = GetBillCostInfo(bill);
                    if (!string.IsNullOrEmpty(costInfo))
                    {
                        billLabel += $" - {costInfo}";
                    }

                    // Add description
                    string description = GetBillDescription(bill);
                    if (!string.IsNullOrEmpty(description))
                    {
                        billLabel += $" - {description}";
                    }

                    if (bill.suspended)
                    {
                        billLabel += " (paused)";
                    }

                    menuItems.Add(new MenuItem(MenuItemType.ExistingBill, billLabel, bill));
                }
            }

            // If no bills, add a note
            if (billGiver.BillStack == null || billGiver.BillStack.Count == 0)
            {
                menuItems.Add(new MenuItem(MenuItemType.ExistingBill, "(No bills)", null, false));
            }
        }

        public static void SelectNext()
        {
            if (menuItems == null || menuItems.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, menuItems.Count);
            AnnounceCurrentSelection();
        }

        public static void SelectPrevious()
        {
            if (menuItems == null || menuItems.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, menuItems.Count);
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Sets the selected index directly (used for typeahead navigation).
        /// </summary>
        public static void SetSelectedIndex(int index)
        {
            if (menuItems == null || menuItems.Count == 0)
                return;

            if (index >= 0 && index < menuItems.Count)
            {
                selectedIndex = index;
            }
        }

        /// <summary>
        /// Jumps to the first item in the list.
        /// </summary>
        public static void JumpToFirst()
        {
            if (menuItems == null || menuItems.Count == 0)
                return;

            selectedIndex = MenuHelper.JumpToFirst();
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Jumps to the last item in the list.
        /// </summary>
        public static void JumpToLast()
        {
            if (menuItems == null || menuItems.Count == 0)
                return;

            selectedIndex = MenuHelper.JumpToLast(menuItems.Count);
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Processes a typeahead character input.
        /// </summary>
        /// <param name="c">The character typed</param>
        /// <returns>True if a match was found</returns>
        public static bool ProcessTypeaheadCharacter(char c)
        {
            if (menuItems == null || menuItems.Count == 0)
                return false;

            var labels = GetItemLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Processes backspace for typeahead search.
        /// </summary>
        /// <returns>True if backspace was handled</returns>
        public static bool ProcessBackspace()
        {
            if (!typeahead.HasActiveSearch)
                return false;

            var labels = GetItemLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                }
                AnnounceWithSearch();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Clears the typeahead search and announces the action.
        /// </summary>
        /// <returns>True if there was an active search to clear</returns>
        public static bool ClearTypeaheadSearch()
        {
            return typeahead.ClearSearchAndAnnounce();
        }

        /// <summary>
        /// Gets the next match index when navigating with active search.
        /// </summary>
        public static int SelectNextMatch()
        {
            return typeahead.GetNextMatch(selectedIndex);
        }

        /// <summary>
        /// Gets the previous match index when navigating with active search.
        /// </summary>
        public static int SelectPreviousMatch()
        {
            return typeahead.GetPreviousMatch(selectedIndex);
        }

        /// <summary>
        /// Gets the current search buffer for announcements.
        /// </summary>
        public static string GetSearchBuffer()
        {
            return typeahead.SearchBuffer;
        }

        /// <summary>
        /// Gets the last failed search string for no-match announcements.
        /// </summary>
        public static string GetLastFailedSearch()
        {
            return typeahead.LastFailedSearch;
        }

        /// <summary>
        /// Gets a list of labels for all menu items for typeahead search.
        /// </summary>
        private static List<string> GetItemLabels()
        {
            List<string> labels = new List<string>();
            if (menuItems != null)
            {
                foreach (var item in menuItems)
                {
                    labels.Add(item.label ?? "");
                }
            }
            return labels;
        }

        /// <summary>
        /// Announces the current selection with search context if applicable.
        /// </summary>
        public static void AnnounceWithSearch()
        {
            if (menuItems == null || menuItems.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];
            string announcement = item.label;

            if (!item.isEnabled)
            {
                announcement += " (unavailable)";
            }

            if (typeahead.HasActiveSearch)
            {
                announcement += $", match {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} for '{typeahead.SearchBuffer}'";
            }
            else
            {
                announcement += $". {MenuHelper.FormatPosition(selectedIndex, menuItems.Count)}";
            }

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Executes the currently selected menu item.
        /// </summary>
        public static void ExecuteSelected()
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            if (!item.isEnabled)
            {
                TolkHelper.Speak("Option not available", SpeechPriority.High);
                return;
            }

            switch (item.type)
            {
                case MenuItemType.AddBill:
                    OpenAddBillMenu();
                    break;

                case MenuItemType.PasteBill:
                    PasteBill();
                    break;

                case MenuItemType.ExistingBill:
                    OpenBillConfig(item.data as Bill);
                    break;
            }
        }

        /// <summary>
        /// Deletes the currently selected bill.
        /// </summary>
        public static void DeleteSelected()
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            if (item.type == MenuItemType.ExistingBill && item.data is Bill bill)
            {
                billGiver.BillStack.Delete(bill);
                TolkHelper.Speak($"Deleted: {bill.LabelCap}");

                // Clear search and rebuild menu
                typeahead.ClearSearch();
                BuildMenuItems();

                // Adjust selection
                if (selectedIndex >= menuItems.Count)
                {
                    selectedIndex = menuItems.Count - 1;
                }

                AnnounceCurrentSelection();
            }
            else
            {
                TolkHelper.Speak("Cannot delete this item", SpeechPriority.High);
            }
        }

        /// <summary>
        /// Copies the currently selected bill to clipboard.
        /// </summary>
        public static void CopySelected()
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            if (item.type == MenuItemType.ExistingBill && item.data is Bill bill)
            {
                BillUtility.Clipboard = bill;
                TolkHelper.Speak($"Copied to clipboard: {bill.LabelCap}");

                // Clear search and rebuild to show paste option
                typeahead.ClearSearch();
                BuildMenuItems();
                AnnounceCurrentSelection();
            }
            else
            {
                TolkHelper.Speak("Cannot copy this item", SpeechPriority.High);
            }
        }

        /// <summary>
        /// Opens the add bill submenu (list of available recipes).
        /// </summary>
        private static void OpenAddBillMenu()
        {
            Building_WorkTable workTable = billGiver as Building_WorkTable;
            if (workTable == null)
            {
                TolkHelper.Speak("Cannot add bills to this object", SpeechPriority.High);
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();

            // Build list of available recipes
            foreach (RecipeDef recipe in workTable.def.AllRecipes)
            {
                if (recipe.AvailableNow && recipe.AvailableOnNow(workTable))
                {
                    // Add main recipe option
                    AddRecipeOption(recipe, workTable, options, null);

                    // Add precept-specific variants if applicable
                    if (recipe.ProducedThingDef != null)
                    {
                        foreach (Ideo ideo in Faction.OfPlayer.ideos.AllIdeos)
                        {
                            foreach (Precept_Building precept in ideo.cachedPossibleBuildings)
                            {
                                if (precept.ThingDef == recipe.ProducedThingDef)
                                {
                                    AddRecipeOption(recipe, workTable, options, precept);
                                }
                            }
                        }
                    }
                }
            }

            if (options.Count == 0)
            {
                TolkHelper.Speak("No recipes available");
                return;
            }

            // Open windowless float menu
            WindowlessFloatMenuState.Open(options, false);
        }

        private static void AddRecipeOption(RecipeDef recipe, Building_WorkTable workTable, List<FloatMenuOption> options, Precept_ThingStyle precept)
        {
            string label = (precept != null) ? "RecipeMake".Translate(precept.LabelCap).CapitalizeFirst() : recipe.LabelCap;

            // Add cost information
            string costInfo = GetRecipeCostInfo(recipe);
            if (!string.IsNullOrEmpty(costInfo))
            {
                label += $" - {costInfo}";
            }

            // Add description
            string description = GetRecipeDescription(recipe);
            if (!string.IsNullOrEmpty(description))
            {
                label += $" - {description}";
            }

            FloatMenuOption option = new FloatMenuOption(label, delegate
            {
                // Check requirements
                if (ModsConfig.BiotechActive && recipe.mechanitorOnlyRecipe &&
                    !workTable.Map.mapPawns.FreeColonists.Any(MechanitorUtility.IsMechanitor))
                {
                    TolkHelper.Speak($"Recipe requires mechanitor: {recipe.LabelCap}");
                    return;
                }

                if (!workTable.Map.mapPawns.FreeColonists.Any((Pawn col) => recipe.PawnSatisfiesSkillRequirements(col)))
                {
                    TolkHelper.Speak($"No pawns have required skills for: {recipe.LabelCap}");
                    return;
                }

                // Create the bill
                Bill bill = recipe.MakeNewBill(precept);
                billGiver.BillStack.AddBill(bill);

                TolkHelper.Speak($"Added bill: {bill.LabelCap}");

                // Clear search and rebuild menu
                typeahead.ClearSearch();
                BuildMenuItems();

                // Select the newly added bill
                for (int i = 0; i < menuItems.Count; i++)
                {
                    if (menuItems[i].type == MenuItemType.ExistingBill && menuItems[i].data == bill)
                    {
                        selectedIndex = i;
                        break;
                    }
                }

                AnnounceCurrentSelection();

                // Demonstrate knowledge
                if (recipe.conceptLearned != null)
                {
                    PlayerKnowledgeDatabase.KnowledgeDemonstrated(recipe.conceptLearned, KnowledgeAmount.Total);
                }
            });

            options.Add(option);
        }

        private static void PasteBill()
        {
            if (BillUtility.Clipboard == null)
            {
                TolkHelper.Speak("Clipboard is empty");
                return;
            }

            Bill bill = BillUtility.Clipboard.Clone();
            bill.InitializeAfterClone();
            billGiver.BillStack.AddBill(bill);

            TolkHelper.Speak($"Pasted bill: {bill.LabelCap}");

            // Clear search, rebuild menu and select the new bill
            typeahead.ClearSearch();
            BuildMenuItems();
            for (int i = 0; i < menuItems.Count; i++)
            {
                if (menuItems[i].type == MenuItemType.ExistingBill && menuItems[i].data == bill)
                {
                    selectedIndex = i;
                    break;
                }
            }
            AnnounceCurrentSelection();
        }

        private static void OpenBillConfig(Bill bill)
        {
            if (bill == null)
            {
                TolkHelper.Speak("No bill selected");
                return;
            }

            if (bill is Bill_Production productionBill)
            {
                BillConfigState.Open(productionBill, billGiverPos);
            }
            else
            {
                TolkHelper.Speak($"Bill type {bill.GetType().Name} not yet supported");
            }
        }

        private static void AnnounceCurrentSelection()
        {
            if (selectedIndex >= 0 && selectedIndex < menuItems.Count)
            {
                MenuItem item = menuItems[selectedIndex];
                string announcement = item.label;

                if (!item.isEnabled)
                {
                    announcement += " (unavailable)";
                }

                announcement += $". {MenuHelper.FormatPosition(selectedIndex, menuItems.Count)}";

                TolkHelper.Speak(announcement);
            }
        }

        /// <summary>
        /// Gets cost information for a recipe (ingredients required).
        /// </summary>
        private static string GetRecipeCostInfo(RecipeDef recipe)
        {
            if (recipe == null)
                return "";

            List<string> costs = new List<string>();

            // Get ingredient costs
            if (recipe.ingredients != null && recipe.ingredients.Count > 0)
            {
                foreach (IngredientCount ingredient in recipe.ingredients)
                {
                    string ingredientName = ingredient.filter.Summary;
                    float amount = ingredient.GetBaseCount();
                    costs.Add($"{amount} {ingredientName}");
                }
            }

            // Get fixed ingredient costs
            if (recipe.fixedIngredientFilter != null && recipe.fixedIngredientFilter.AllowedThingDefs.Any())
            {
                // Only show if not already covered by ingredients list
                if (recipe.ingredients == null || recipe.ingredients.Count == 0)
                {
                    costs.Add(recipe.fixedIngredientFilter.Summary);
                }
            }

            if (costs.Count == 0)
                return "";

            return string.Join(", ", costs);
        }

        /// <summary>
        /// Gets description for a recipe (what it produces).
        /// </summary>
        private static string GetRecipeDescription(RecipeDef recipe)
        {
            if (recipe == null)
                return "";

            List<string> descriptions = new List<string>();

            // Add what it produces
            if (recipe.products != null && recipe.products.Count > 0)
            {
                foreach (ThingDefCountClass product in recipe.products)
                {
                    string productDesc = $"Makes {product.count} {product.thingDef.LabelCap}";
                    descriptions.Add(productDesc);
                }
            }
            else if (recipe.ProducedThingDef != null)
            {
                // Check recipe's ProducedThingDef if no products list
                descriptions.Add($"Makes {recipe.ProducedThingDef.LabelCap}");
            }

            // Add work amount if available
            if (recipe.workAmount > 0)
            {
                descriptions.Add($"Work: {recipe.workAmount}");
            }

            // Add skill requirement if available
            if (recipe.workSkill != null)
            {
                string skillInfo = recipe.workSkill.LabelCap.ToString();
                if (recipe.workSkillLearnFactor > 0)
                {
                    skillInfo += $" (Learn factor: {recipe.workSkillLearnFactor:F1})";
                }
                descriptions.Add(skillInfo);
            }

            if (descriptions.Count == 0)
                return "";

            return string.Join(", ", descriptions);
        }

        /// <summary>
        /// Gets cost information for a bill (ingredients required).
        /// </summary>
        private static string GetBillCostInfo(Bill bill)
        {
            if (bill?.recipe == null)
                return "";

            List<string> costs = new List<string>();

            // Get ingredient costs
            if (bill.recipe.ingredients != null && bill.recipe.ingredients.Count > 0)
            {
                foreach (IngredientCount ingredient in bill.recipe.ingredients)
                {
                    string ingredientName = ingredient.filter.Summary;
                    float amount = ingredient.GetBaseCount();
                    costs.Add($"{amount} {ingredientName}");
                }
            }

            // Get fixed ingredient costs
            if (bill.recipe.fixedIngredientFilter != null && bill.recipe.fixedIngredientFilter.AllowedThingDefs.Any())
            {
                // Only show if not already covered by ingredients list
                if (bill.recipe.ingredients == null || bill.recipe.ingredients.Count == 0)
                {
                    costs.Add(bill.recipe.fixedIngredientFilter.Summary);
                }
            }

            if (costs.Count == 0)
                return "";

            return string.Join(", ", costs);
        }

        /// <summary>
        /// Gets description for a bill (what it produces).
        /// </summary>
        private static string GetBillDescription(Bill bill)
        {
            if (bill?.recipe == null)
                return "";

            List<string> descriptions = new List<string>();

            // Add what it produces
            if (bill.recipe.products != null && bill.recipe.products.Count > 0)
            {
                foreach (ThingDefCountClass product in bill.recipe.products)
                {
                    string productDesc = $"Makes {product.count} {product.thingDef.LabelCap}";
                    descriptions.Add(productDesc);
                }
            }
            else if (bill.recipe.ProducedThingDef != null)
            {
                // Check recipe's ProducedThingDef if no products list
                descriptions.Add($"Makes {bill.recipe.ProducedThingDef.LabelCap}");
            }

            // Add work amount if available
            if (bill.recipe.workAmount > 0)
            {
                descriptions.Add($"Work: {bill.recipe.workAmount}");
            }

            // Add skill requirement if available
            if (bill.recipe.workSkill != null)
            {
                string skillInfo = bill.recipe.workSkill.LabelCap.ToString();
                if (bill.recipe.workSkillLearnFactor > 0)
                {
                    skillInfo += $" (Learn factor: {bill.recipe.workSkillLearnFactor:F1})";
                }
                descriptions.Add(skillInfo);
            }

            if (descriptions.Count == 0)
                return "";

            return string.Join(", ", descriptions);
        }
    }
}
