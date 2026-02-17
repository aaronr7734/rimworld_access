using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Sound;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages a windowless bill configuration menu.
    /// Provides keyboard navigation through all bill settings.
    /// </summary>
    public static class BillConfigState
    {
        private static Bill_Production bill = null;
        private static IntVec3 billGiverPos;
        private static List<MenuItem> menuItems = null;
        private static int selectedIndex = 0;
        private static bool isActive = false;
        private static bool isEditing = false;

        // Numeric input mode fields
        private static string numericBuffer = "";
        private static bool isNumericInputMode = false;

        private enum MenuItemType
        {
            RecipeInfo,
            RepeatMode,
            RepeatCount,
            TargetCount,
            PauseWhenSatisfied,
            UnpauseAt,
            StoreMode,
            AllowedSkillRange,
            PawnRestriction,
            IngredientSearchRadius,
            IngredientFilter,
            SuspendToggle,
            DeleteBill
        }

        private class MenuItem
        {
            public MenuItemType type;
            public string label;
            public string searchLabel; // Label used for typeahead search (field name only, no values)
            public object data;
            public bool isEditable; // Can be edited with left/right or Enter

            public MenuItem(MenuItemType type, string label, string searchLabel = null, object data = null, bool editable = false)
            {
                this.type = type;
                this.label = label;
                this.searchLabel = searchLabel ?? label; // Default to full label if not specified
                this.data = data;
                this.isEditable = editable;
            }
        }

        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        public static bool IsActive => isActive;
        public static bool HasActiveSearch => typeahead.HasActiveSearch;
        public static bool HasNoMatches => typeahead.HasNoMatches;
        public static bool IsEditing => isEditing;
        public static bool IsNumericInputMode => isNumericInputMode;

        /// <summary>
        /// Opens the bill configuration menu.
        /// </summary>
        public static void Open(Bill_Production productionBill, IntVec3 position)
        {
            if (productionBill == null)
            {
                Log.Error("Cannot open bill config: bill is null");
                return;
            }

            bill = productionBill;
            billGiverPos = position;
            menuItems = new List<MenuItem>();
            selectedIndex = 0;
            isActive = true;
            isEditing = false;
            typeahead.ClearSearch();

            BuildMenuItems();
            AnnounceCurrentSelection();

            Log.Message($"Opened bill config for {bill.LabelCap}");
        }

        /// <summary>
        /// Closes the bill configuration menu.
        /// </summary>
        public static void Close()
        {
            bill = null;
            menuItems = null;
            selectedIndex = 0;
            isActive = false;
            isEditing = false;
            isNumericInputMode = false;
            numericBuffer = "";
            typeahead.ClearSearch();
        }

        private static void BuildMenuItems()
        {
            menuItems.Clear();

            // Recipe info (read-only) - searchLabel: "Recipe"
            menuItems.Add(new MenuItem(MenuItemType.RecipeInfo, GetRecipeInfoLabel(), "Recipe", null, false));

            // Suspend/Resume toggle - searchLabel matches the action
            string suspendLabel = bill.suspended ? "Resume bill" : "Pause bill";
            menuItems.Add(new MenuItem(MenuItemType.SuspendToggle, suspendLabel, suspendLabel, null, true));

            // Repeat mode - searchLabel: "Repeat mode" (not the value)
            menuItems.Add(new MenuItem(MenuItemType.RepeatMode, GetRepeatModeLabel(), "Repeat mode", null, true));

            // Repeat count (only if mode is RepeatCount) - searchLabel: "Repeat count"
            if (bill.repeatMode == BillRepeatModeDefOf.RepeatCount)
            {
                menuItems.Add(new MenuItem(MenuItemType.RepeatCount, GetRepeatCountLabel(), "Repeat count", null, true));
            }

            // Target count and unpause threshold (only if mode is TargetCount)
            if (bill.repeatMode == BillRepeatModeDefOf.TargetCount)
            {
                menuItems.Add(new MenuItem(MenuItemType.TargetCount, GetTargetCountLabel(), "Target count", null, true));

                // Pause when satisfied checkbox
                menuItems.Add(new MenuItem(MenuItemType.PauseWhenSatisfied, GetPauseWhenSatisfiedLabel(), null, true));

                // Only show unpause threshold if pauseWhenSatisfied is enabled
                if (bill.pauseWhenSatisfied)
                {
                    menuItems.Add(new MenuItem(MenuItemType.UnpauseAt, GetUnpauseAtLabel(), "Unpause at", null, true));
                }
            }

            // Store mode - searchLabel: "Store in"
            menuItems.Add(new MenuItem(MenuItemType.StoreMode, GetStoreModeLabel(), "Store in", null, true));

            // Pawn restriction - searchLabel: "Worker"
            menuItems.Add(new MenuItem(MenuItemType.PawnRestriction, GetPawnRestrictionLabel(), "Worker", null, true));

            // Allowed skill range - searchLabel: "Allowed skill range"
            menuItems.Add(new MenuItem(MenuItemType.AllowedSkillRange, GetSkillRangeLabel(), "Allowed skill range", null, true));

            // Ingredient search radius - searchLabel: "Ingredient radius"
            menuItems.Add(new MenuItem(MenuItemType.IngredientSearchRadius, GetIngredientRadiusLabel(), "Ingredient radius", null, true));

            // Ingredient filter - searchLabel matches full label
            menuItems.Add(new MenuItem(MenuItemType.IngredientFilter, "Configure ingredient filter...", "Ingredient filter", null, true));

            // Delete bill - searchLabel matches full label
            menuItems.Add(new MenuItem(MenuItemType.DeleteBill, "Delete this bill", "Delete bill", null, true));
        }

        #region Label Generators

        private static string GetRecipeInfoLabel()
        {
            string label = $"Recipe: {bill.recipe.LabelCap}";

            if (bill.recipe.workSkill != null)
            {
                label += $" (Skill: {bill.recipe.workSkill.LabelCap}";
                if (bill.recipe.workSkillLearnFactor > 0f)
                {
                    label += $", Learn: {bill.recipe.workSkillLearnFactor:F1}";
                }
                label += ")";
            }

            return label;
        }

        private static string GetRepeatModeLabel()
        {
            return $"Repeat mode: {bill.repeatMode.LabelCap}";
        }

        private static string GetRepeatCountLabel()
        {
            return $"Repeat count: {bill.repeatCount}";
        }

        private static string GetTargetCountLabel()
        {
            if (bill.targetCount >= 999999)
            {
                return "Target count: Infinite";
            }
            return $"Target count: {bill.targetCount}";
        }

        private static string GetPauseWhenSatisfiedLabel()
        {
            return $"Pause when satisfied: {(bill.pauseWhenSatisfied ? "Yes" : "No")}";
        }

        private static string GetUnpauseAtLabel()
        {
            return $"Unpause at: {bill.unpauseWhenYouHave}";
        }

        private static string GetStoreModeLabel()
        {
            string label = "Store in: ";

            if (bill.GetStoreMode() == BillStoreModeDefOf.BestStockpile)
            {
                label += "Best stockpile";
            }
            else if (bill.GetStoreMode() == BillStoreModeDefOf.DropOnFloor)
            {
                label += "Drop on floor";
            }
            else if (bill.GetStoreMode() == BillStoreModeDefOf.SpecificStockpile)
            {
                ISlotGroup slotGroup = bill.GetSlotGroup();
                if (slotGroup is Zone_Stockpile stockpile)
                {
                    label += stockpile.label;
                }
                else
                {
                    label += "(No stockpile)";
                }
            }

            return label;
        }

        private static string GetPawnRestrictionLabel()
        {
            if (bill.PawnRestriction == null)
            {
                return "Worker: Anyone";
            }
            else
            {
                return $"Worker: {bill.PawnRestriction.LabelShortCap}";
            }
        }

        private static string GetSkillRangeLabel()
        {
            IntRange range = bill.allowedSkillRange;
            return $"Allowed skill range: {range.min} - {range.max}";
        }

        private static string GetIngredientRadiusLabel()
        {
            if (bill.ingredientSearchRadius >= 999f)
            {
                return "Ingredient radius: Unlimited";
            }
            else
            {
                return $"Ingredient radius: {bill.ingredientSearchRadius:F0} tiles";
            }
        }

        /// <summary>
        /// Gets the label for a menu item type.
        /// Used by JumpToMin/JumpToMax to update labels after value changes.
        /// </summary>
        private static string GetLabelForItem(MenuItemType type)
        {
            switch (type)
            {
                case MenuItemType.RepeatCount:
                    return GetRepeatCountLabel();
                case MenuItemType.TargetCount:
                    return GetTargetCountLabel();
                case MenuItemType.UnpauseAt:
                    return GetUnpauseAtLabel();
                case MenuItemType.IngredientSearchRadius:
                    return GetIngredientRadiusLabel();
                default:
                    return "";
            }
        }

        #endregion

        public static void SelectNext()
        {
            if (menuItems == null || menuItems.Count == 0)
                return;

            if (isEditing)
            {
                TolkHelper.Speak("Finish editing first (press Enter or Escape)");
                return;
            }

            selectedIndex = MenuHelper.SelectNext(selectedIndex, menuItems.Count);
            AnnounceCurrentSelection();
        }

        public static void SelectPrevious()
        {
            if (menuItems == null || menuItems.Count == 0)
                return;

            if (isEditing)
            {
                TolkHelper.Speak("Finish editing first (press Enter or Escape)");
                return;
            }

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
        /// Gets a list of search labels for typeahead.
        /// These are the field names only, not values.
        /// </summary>
        private static List<string> GetSearchLabels()
        {
            List<string> labels = new List<string>();
            if (menuItems != null)
            {
                foreach (var item in menuItems)
                {
                    labels.Add(item.searchLabel ?? "");
                }
            }
            return labels;
        }

        /// <summary>
        /// Processes a typeahead character input.
        /// </summary>
        public static bool ProcessTypeaheadCharacter(char c)
        {
            if (menuItems == null || menuItems.Count == 0)
                return false;

            if (isEditing)
                return false;

            var labels = GetSearchLabels();
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
        public static bool ProcessBackspace()
        {
            if (!typeahead.HasActiveSearch)
                return false;

            var labels = GetSearchLabels();
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
        /// Gets the last failed search string for no-match announcements.
        /// </summary>
        public static string GetLastFailedSearch()
        {
            return typeahead.LastFailedSearch;
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

        public static void AdjustValue(int direction, int multiplier = 1)
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            if (!item.isEditable)
            {
                TolkHelper.Speak("This item cannot be adjusted", SpeechPriority.High);
                return;
            }

            switch (item.type)
            {
                case MenuItemType.RepeatMode:
                    CycleRepeatMode(direction);
                    break;

                case MenuItemType.RepeatCount:
                    AdjustRepeatCount(direction, multiplier);
                    break;

                case MenuItemType.TargetCount:
                    AdjustTargetCount(direction, multiplier);
                    break;

                case MenuItemType.UnpauseAt:
                    AdjustUnpauseAt(direction, multiplier);
                    break;

                case MenuItemType.AllowedSkillRange:
                    AdjustSkillRange(direction);
                    break;

                case MenuItemType.IngredientSearchRadius:
                    AdjustIngredientRadius(direction, multiplier);
                    break;

                default:
                    TolkHelper.Speak("Use Enter to open submenu");
                    break;
            }
        }

        public static void ExecuteSelected()
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            switch (item.type)
            {
                case MenuItemType.SuspendToggle:
                    bill.suspended = !bill.suspended;
                    BuildMenuItems();
                    TolkHelper.Speak(bill.suspended ? "Bill paused" : "Bill resumed");
                    AnnounceCurrentSelection();
                    break;

                case MenuItemType.PauseWhenSatisfied:
                    bill.pauseWhenSatisfied = !bill.pauseWhenSatisfied;
                    // Ensure unpause threshold is valid
                    if (bill.pauseWhenSatisfied && bill.unpauseWhenYouHave >= bill.targetCount)
                    {
                        bill.unpauseWhenYouHave = bill.targetCount - 1;
                    }
                    BuildMenuItems();
                    TolkHelper.Speak(bill.pauseWhenSatisfied ? "Pause when satisfied enabled" : "Pause when satisfied disabled");
                    AnnounceCurrentSelection();
                    break;

                case MenuItemType.StoreMode:
                    OpenStoreModeMenu();
                    break;

                case MenuItemType.PawnRestriction:
                    OpenPawnRestrictionMenu();
                    break;

                case MenuItemType.IngredientFilter:
                    OpenIngredientFilterMenu();
                    break;

                case MenuItemType.DeleteBill:
                    DeleteBill();
                    break;

                default:
                    TolkHelper.Speak("Use left/right arrows to adjust");
                    break;
            }
        }

        #region Value Adjustment Methods

        private static void CycleRepeatMode(int direction)
        {
            List<BillRepeatModeDef> modes = DefDatabase<BillRepeatModeDef>.AllDefsListForReading;
            int currentIndex = modes.IndexOf(bill.repeatMode);

            if (direction > 0)
            {
                currentIndex = (currentIndex + 1) % modes.Count;
            }
            else
            {
                currentIndex = (currentIndex - 1 + modes.Count) % modes.Count;
            }

            bill.repeatMode = modes[currentIndex];
            BuildMenuItems(); // Rebuild to show/hide related options
            AnnounceCurrentSelection();
        }

        private static void AdjustRepeatCount(int direction, int multiplier = 1)
        {
            int step = direction * multiplier;
            int oldValue = bill.repeatCount;
            bill.repeatCount = Mathf.Max(1, bill.repeatCount + step);

            // Check if we hit a boundary
            if (bill.repeatCount == oldValue)
            {
                TolkHelper.Speak(direction > 0 ? "Maximum" : "Minimum");
                return;
            }
            if (bill.repeatCount == 1 && direction < 0)
            {
                TolkHelper.Speak("1, minimum");
            }
            else
            {
                TolkHelper.Speak(bill.repeatCount.ToString());
            }

            menuItems[selectedIndex].label = GetRepeatCountLabel();
        }

        private static void AdjustTargetCount(int direction, int multiplier = 1)
        {
            int step = direction * multiplier;
            int oldValue = bill.targetCount;
            bill.targetCount = Mathf.Max(1, bill.targetCount + step);

            // Enforce unpauseAt constraint
            if (bill.pauseWhenSatisfied && bill.unpauseWhenYouHave >= bill.targetCount)
            {
                bill.unpauseWhenYouHave = bill.targetCount - 1;
            }

            // Check if we hit Infinite threshold
            if (bill.targetCount >= 999999)
            {
                bill.targetCount = 999999;  // Normalize to exactly 999999
                TolkHelper.Speak("Infinite");
                menuItems[selectedIndex].label = GetTargetCountLabel();
                return;
            }

            // Check if we hit a boundary
            if (bill.targetCount == oldValue)
            {
                TolkHelper.Speak(direction > 0 ? "Maximum" : "Minimum");
                return;
            }
            if (bill.targetCount == 1 && direction < 0)
            {
                TolkHelper.Speak("1, minimum");
            }
            else
            {
                TolkHelper.Speak(bill.targetCount.ToString());
            }

            menuItems[selectedIndex].label = GetTargetCountLabel();
        }

        private static void AdjustUnpauseAt(int direction, int multiplier = 1)
        {
            int step = direction * multiplier;
            int oldValue = bill.unpauseWhenYouHave;
            int maxValue = bill.targetCount - 1;
            bill.unpauseWhenYouHave = Mathf.Clamp(bill.unpauseWhenYouHave + step, 0, maxValue);

            // Check if we hit a boundary
            if (bill.unpauseWhenYouHave == oldValue)
            {
                TolkHelper.Speak(direction > 0 ? "Maximum" : "Minimum");
                return;
            }
            if (bill.unpauseWhenYouHave == 0 && direction < 0)
            {
                TolkHelper.Speak("0, minimum");
            }
            else if (bill.unpauseWhenYouHave == maxValue && direction > 0)
            {
                TolkHelper.Speak($"{bill.unpauseWhenYouHave}, maximum");
            }
            else
            {
                TolkHelper.Speak(bill.unpauseWhenYouHave.ToString());
            }

            menuItems[selectedIndex].label = GetUnpauseAtLabel();
        }

        /// <summary>
        /// Jumps to the minimum value for the current numeric field.
        /// </summary>
        public static void JumpToMin()
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            switch (item.type)
            {
                case MenuItemType.RepeatCount:
                    if (bill.repeatCount == 1)
                    {
                        TolkHelper.Speak("Already at minimum");
                        return;
                    }
                    bill.repeatCount = 1;
                    TolkHelper.Speak("1, minimum");
                    break;

                case MenuItemType.TargetCount:
                    if (bill.targetCount == 1)
                    {
                        TolkHelper.Speak("Already at minimum");
                        return;
                    }
                    bill.targetCount = 1;
                    if (bill.pauseWhenSatisfied && bill.unpauseWhenYouHave >= bill.targetCount)
                    {
                        bill.unpauseWhenYouHave = 0;
                    }
                    TolkHelper.Speak("1, minimum");
                    break;

                case MenuItemType.UnpauseAt:
                    if (bill.unpauseWhenYouHave == 0)
                    {
                        TolkHelper.Speak("Already at minimum");
                        return;
                    }
                    bill.unpauseWhenYouHave = 0;
                    TolkHelper.Speak("0, minimum");
                    break;

                case MenuItemType.IngredientSearchRadius:
                    if (bill.ingredientSearchRadius <= 3f)
                    {
                        TolkHelper.Speak("Already at minimum");
                        return;
                    }
                    bill.ingredientSearchRadius = 3f;
                    TolkHelper.Speak("3, minimum");
                    break;

                default:
                    TolkHelper.Speak("This field cannot be adjusted");
                    return;
            }

            menuItems[selectedIndex].label = GetLabelForItem(item.type);
        }

        /// <summary>
        /// Jumps to the maximum value for the current numeric field.
        /// </summary>
        public static void JumpToMax()
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            switch (item.type)
            {
                case MenuItemType.RepeatCount:
                    TolkHelper.Speak("No maximum limit. Use numeric input for large values.");
                    return;

                case MenuItemType.TargetCount:
                    if (bill.targetCount >= 999999)
                    {
                        TolkHelper.Speak("Already at maximum");
                        return;
                    }
                    bill.targetCount = 999999;
                    TolkHelper.Speak("Infinite, maximum");
                    break;

                case MenuItemType.UnpauseAt:
                    int maxValue = bill.targetCount - 1;
                    if (bill.unpauseWhenYouHave == maxValue)
                    {
                        TolkHelper.Speak("Already at maximum");
                        return;
                    }
                    bill.unpauseWhenYouHave = maxValue;
                    TolkHelper.Speak($"{maxValue}, maximum");
                    break;

                case MenuItemType.IngredientSearchRadius:
                    if (bill.ingredientSearchRadius >= 999f)
                    {
                        TolkHelper.Speak("Already at maximum");
                        return;
                    }
                    bill.ingredientSearchRadius = 999f;
                    TolkHelper.Speak("Unlimited, maximum");
                    break;

                default:
                    TolkHelper.Speak("This field cannot be adjusted");
                    return;
            }

            menuItems[selectedIndex].label = GetLabelForItem(item.type);
        }

        #endregion

        #region Numeric Input Methods

        /// <summary>
        /// Starts numeric input mode for typing a value directly.
        /// </summary>
        public static void StartNumericInput()
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            // Only allow numeric input for numeric fields - otherwise execute the action
            if (item.type != MenuItemType.RepeatCount &&
                item.type != MenuItemType.TargetCount &&
                item.type != MenuItemType.UnpauseAt &&
                item.type != MenuItemType.IngredientSearchRadius)
            {
                // Not a numeric field - execute the action instead
                ExecuteSelected();
                return;
            }

            numericBuffer = "";
            isNumericInputMode = true;
            TolkHelper.Speak("Type a number, then press Enter to confirm or Escape to cancel");
        }

        /// <summary>
        /// Handles a digit input during numeric input mode.
        /// </summary>
        public static void HandleNumericDigit(char digit)
        {
            if (!isNumericInputMode) return;

            numericBuffer += digit;
            TolkHelper.Speak(numericBuffer, SpeechPriority.Low);
        }

        /// <summary>
        /// Handles backspace during numeric input mode.
        /// </summary>
        public static void HandleNumericBackspace()
        {
            if (!isNumericInputMode || numericBuffer.Length == 0) return;

            numericBuffer = numericBuffer.Substring(0, numericBuffer.Length - 1);
            if (numericBuffer.Length > 0)
            {
                TolkHelper.Speak(numericBuffer, SpeechPriority.Low);
            }
            else
            {
                TolkHelper.Speak("Empty", SpeechPriority.Low);
            }
        }

        /// <summary>
        /// Confirms and applies the numeric input value.
        /// </summary>
        public static void ConfirmNumericInput()
        {
            if (!isNumericInputMode) return;

            if (int.TryParse(numericBuffer, out int value) && value > 0)
            {
                ApplyNumericValue(value);
            }
            else
            {
                TolkHelper.Speak("Invalid number");
            }

            isNumericInputMode = false;
            numericBuffer = "";
        }

        /// <summary>
        /// Cancels numeric input mode without applying changes.
        /// </summary>
        public static void CancelNumericInput()
        {
            isNumericInputMode = false;
            numericBuffer = "";
            TolkHelper.Speak("Cancelled");
        }

        private static void ApplyNumericValue(int value)
        {
            if (menuItems == null || selectedIndex >= menuItems.Count)
                return;

            MenuItem item = menuItems[selectedIndex];

            switch (item.type)
            {
                case MenuItemType.RepeatCount:
                    bill.repeatCount = Mathf.Max(1, value);
                    menuItems[selectedIndex].label = GetRepeatCountLabel();
                    TolkHelper.Speak(menuItems[selectedIndex].label);
                    break;

                case MenuItemType.TargetCount:
                    bill.targetCount = Mathf.Max(1, value);
                    // Ensure unpause constraint
                    if (bill.pauseWhenSatisfied && bill.unpauseWhenYouHave >= bill.targetCount)
                    {
                        bill.unpauseWhenYouHave = bill.targetCount - 1;
                    }
                    menuItems[selectedIndex].label = GetTargetCountLabel();
                    if (bill.targetCount >= 999999)
                    {
                        bill.targetCount = 999999;
                        TolkHelper.Speak("Infinite");
                    }
                    else
                    {
                        TolkHelper.Speak(bill.targetCount.ToString());
                    }
                    break;

                case MenuItemType.UnpauseAt:
                    // Clamp to valid range: 0 to targetCount - 1
                    bill.unpauseWhenYouHave = Mathf.Clamp(value, 0, bill.targetCount - 1);
                    menuItems[selectedIndex].label = GetUnpauseAtLabel();
                    TolkHelper.Speak(menuItems[selectedIndex].label);
                    break;

                case MenuItemType.IngredientSearchRadius:
                    // Valid range is 3-100, anything over 100 becomes unlimited (999)
                    if (value > 100)
                    {
                        bill.ingredientSearchRadius = 999f;
                        TolkHelper.Speak("Unlimited");
                    }
                    else
                    {
                        bill.ingredientSearchRadius = Mathf.Clamp(value, 3, 100);
                        TolkHelper.Speak(bill.ingredientSearchRadius.ToString("F0"));
                    }
                    menuItems[selectedIndex].label = GetIngredientRadiusLabel();
                    break;

                default:
                    TolkHelper.Speak("Cannot apply numeric value to this field");
                    break;
            }
        }

        #endregion

        private static void AdjustSkillRange(int direction)
        {
            // Cycle through presets: 0-3, 0-20, 6-20, 10-20
            IntRange current = bill.allowedSkillRange;

            if (direction > 0)
            {
                if (current.min == 0 && current.max == 3)
                {
                    bill.allowedSkillRange = new IntRange(0, 20);
                }
                else if (current.min == 0 && current.max == 20)
                {
                    bill.allowedSkillRange = new IntRange(6, 20);
                }
                else if (current.min == 6 && current.max == 20)
                {
                    bill.allowedSkillRange = new IntRange(10, 20);
                }
                else
                {
                    bill.allowedSkillRange = new IntRange(0, 3);
                }
            }
            else
            {
                if (current.min == 0 && current.max == 3)
                {
                    bill.allowedSkillRange = new IntRange(10, 20);
                }
                else if (current.min == 10 && current.max == 20)
                {
                    bill.allowedSkillRange = new IntRange(6, 20);
                }
                else if (current.min == 6 && current.max == 20)
                {
                    bill.allowedSkillRange = new IntRange(0, 20);
                }
                else
                {
                    bill.allowedSkillRange = new IntRange(0, 3);
                }
            }

            menuItems[selectedIndex].label = GetSkillRangeLabel();
            TolkHelper.Speak(menuItems[selectedIndex].label);
        }

        private static void AdjustIngredientRadius(int direction, int multiplier = 1)
        {
            float oldValue = bill.ingredientSearchRadius;

            // Handle unlimited state
            if (bill.ingredientSearchRadius >= 999f)
            {
                if (direction < 0)
                {
                    bill.ingredientSearchRadius = 100f;
                    TolkHelper.Speak("100");
                }
                else
                {
                    TolkHelper.Speak("Already at maximum");
                }
                menuItems[selectedIndex].label = GetIngredientRadiusLabel();
                return;
            }

            // Translate multipliers for ingredient radius (range is only 3-100)
            float step;
            if (multiplier >= 1000)
            {
                // Shift+Ctrl = jump to 100 (or unlimited if already at 100)
                if (direction > 0)
                {
                    if (bill.ingredientSearchRadius >= 100f)
                    {
                        bill.ingredientSearchRadius = 999f;
                        TolkHelper.Speak("Unlimited, maximum");
                    }
                    else
                    {
                        bill.ingredientSearchRadius = 100f;
                        TolkHelper.Speak("100");
                    }
                }
                else
                {
                    // Shift+Ctrl+Down = jump to 3
                    bill.ingredientSearchRadius = 3f;
                    TolkHelper.Speak("3, minimum");
                }
                menuItems[selectedIndex].label = GetIngredientRadiusLabel();
                return;
            }
            else if (multiplier >= 100)
            {
                // Ctrl = ±25 for ingredient radius
                step = direction * 25f;
            }
            else
            {
                // Normal or Shift
                step = direction * multiplier;
            }

            bill.ingredientSearchRadius = Mathf.Clamp(bill.ingredientSearchRadius + step, 3f, 100f);

            // Check if we should go to unlimited (at 100 and pressing up)
            if (bill.ingredientSearchRadius >= 100f && direction > 0 && oldValue >= 100f)
            {
                bill.ingredientSearchRadius = 999f;
                TolkHelper.Speak("Unlimited, maximum");
                menuItems[selectedIndex].label = GetIngredientRadiusLabel();
                return;
            }

            // Check if we hit a boundary
            if (bill.ingredientSearchRadius == oldValue)
            {
                TolkHelper.Speak(direction > 0 ? "Maximum" : "Minimum");
                return;
            }

            // Announce the new value
            if (bill.ingredientSearchRadius == 3f && direction < 0)
            {
                TolkHelper.Speak("3, minimum");
            }
            else if (bill.ingredientSearchRadius >= 100f)
            {
                TolkHelper.Speak("100");
            }
            else
            {
                TolkHelper.Speak($"{bill.ingredientSearchRadius:F0}");
            }

            menuItems[selectedIndex].label = GetIngredientRadiusLabel();
        }

        #region Submenu Methods

        private static void OpenStoreModeMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            // Drop on floor
            options.Add(new FloatMenuOption("Drop on floor", delegate
            {
                bill.SetStoreMode(BillStoreModeDefOf.DropOnFloor);
                BuildMenuItems();
                TolkHelper.Speak("Store mode: Drop on floor");
                AnnounceCurrentSelection();
            }));

            // Best stockpile
            options.Add(new FloatMenuOption("Best stockpile", delegate
            {
                bill.SetStoreMode(BillStoreModeDefOf.BestStockpile);
                BuildMenuItems();
                TolkHelper.Speak("Store mode: Best stockpile");
                AnnounceCurrentSelection();
            }));

            // Specific stockpiles
            List<SlotGroup> allGroupsListForReading = bill.billStack.billGiver.Map.haulDestinationManager.AllGroupsListForReading;
            for (int i = 0; i < allGroupsListForReading.Count; i++)
            {
                SlotGroup group = allGroupsListForReading[i];
                Zone_Stockpile stockpile = group.parent as Zone_Stockpile;

                if (stockpile != null)
                {
                    ISlotGroup localGroup = group; // Capture for lambda
                    options.Add(new FloatMenuOption($"Stockpile: {stockpile.label}", delegate
                    {
                        bill.SetStoreMode(BillStoreModeDefOf.SpecificStockpile, localGroup);
                        BuildMenuItems();
                        TolkHelper.Speak($"Store mode: {stockpile.label}");
                        AnnounceCurrentSelection();
                    }));
                }
            }

            WindowlessFloatMenuState.Open(options, false);
        }

        private static void OpenPawnRestrictionMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            // Anyone
            options.Add(new FloatMenuOption("Anyone", delegate
            {
                bill.SetPawnRestriction(null);
                menuItems[selectedIndex].label = GetPawnRestrictionLabel();
                TolkHelper.Speak("Worker: Anyone");
            }));

            // Get all colonists and sort by skill
            Map map = bill.billStack.billGiver.Map;
            List<Pawn> colonists = map.mapPawns.FreeColonists.ToList();

            if (bill.recipe.workSkill != null)
            {
                colonists = colonists.OrderByDescending(p => p.skills.GetSkill(bill.recipe.workSkill).Level).ToList();
            }

            foreach (Pawn pawn in colonists)
            {
                string label = pawn.LabelShortCap;

                if (bill.recipe.workSkill != null)
                {
                    int skillLevel = pawn.skills.GetSkill(bill.recipe.workSkill).Level;
                    label += $" (Skill: {skillLevel})";
                }

                Pawn localPawn = pawn; // Capture for lambda
                options.Add(new FloatMenuOption(label, delegate
                {
                    bill.SetPawnRestriction(localPawn);
                    menuItems[selectedIndex].label = GetPawnRestrictionLabel();
                    TolkHelper.Speak($"Worker: {localPawn.LabelShortCap}");
                }));
            }

            WindowlessFloatMenuState.Open(options, false);
        }

        private static void OpenIngredientFilterMenu()
        {
            ThingFilterMenuState.Open(bill.ingredientFilter, bill.recipe.fixedIngredientFilter, "Ingredient Filter");
        }

        private static void DeleteBill()
        {
            string billLabel = bill.LabelCap;
            bill.billStack.Delete(bill);
            TolkHelper.Speak($"Deleted bill: {billLabel}");
            Close();

            // Go back to bills menu
            if (bill.billStack.billGiver is IBillGiver billGiver)
            {
                BillsMenuState.Open(billGiver, billGiverPos);
            }
        }

        #endregion

        /// <summary>
        /// Opens the info card for the product of the current bill.
        /// </summary>
        public static void OpenInfoCard()
        {
            if (bill == null)
            {
                TolkHelper.Speak("No info card available");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            ThingDef productDef = bill.recipe?.ProducedThingDef;
            if (productDef != null)
            {
                InfoCardState.OpenInfoCardForDef(productDef);
            }
            else
            {
                TolkHelper.Speak("No info card available for this bill");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        private static void AnnounceCurrentSelection()
        {
            if (selectedIndex >= 0 && selectedIndex < menuItems.Count)
            {
                MenuItem item = menuItems[selectedIndex];
                string announcement = $"{item.label}. {MenuHelper.FormatPosition(selectedIndex, menuItems.Count)}";
                TolkHelper.Speak(announcement);
            }
        }
    }
}
