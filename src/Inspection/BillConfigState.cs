using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
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

        // Text input mode fields (for bill rename)
        private static readonly TextInputController billRenameController = new TextInputController();

        private enum MenuItemType
        {
            RecipeInfo,
            RepeatMode,
            RepeatCount,
            TargetCount,
            CurrentlyHave,
            IncludeEquipped,
            IncludeTainted,
            IncludeSource,
            HpRange,
            QualityRange,
            LimitToAllowedStuff,
            PauseWhenSatisfied,
            UnpauseAt,
            StoreMode,
            SkillRangeMin,
            SkillRangeMax,
            PawnRestriction,
            IngredientSearchRadius,
            IngredientFilter,
            RenameBill,
            StyleSelection,
            SuspendToggle,
            UnpauseBill,
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
        public static bool IsTextInputMode => TextInputManager.Active == billRenameController;

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
            if (TextInputManager.Active == billRenameController) TextInputManager.Clear();
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
            if (TextInputManager.Active == billRenameController) TextInputManager.Clear();
            numericBuffer = "";
            typeahead.ClearSearch();
        }

        private static void BuildMenuItems()
        {
            menuItems.Clear();

            // 1. Recipe info (read-only)
            menuItems.Add(new MenuItem(MenuItemType.RecipeInfo, GetRecipeInfoLabel(),
                "RimWorldAccess.Inspection.BillConfig.SearchLabel.Recipe".Translate(), null, false));

            // 2. Suspend/Resume toggle
            string suspendLabel = bill.suspended ? "Suspended".Translate().ToString() : "NotSuspended".Translate().ToString();
            menuItems.Add(new MenuItem(MenuItemType.SuspendToggle, suspendLabel, suspendLabel, null, true));

            // 2b. Unpause button (only when auto-paused, matching vanilla's Unpause button)
            if (bill.paused)
            {
                menuItems.Add(new MenuItem(MenuItemType.UnpauseBill, "Unpause".Translate().ToString(),
                    "Unpause".Translate().ToString(), null, true));
            }

            // 3. Repeat mode
            menuItems.Add(new MenuItem(MenuItemType.RepeatMode, GetRepeatModeLabel(),
                "RimWorldAccess.Inspection.BillConfig.SearchLabel.RepeatMode".Translate(), null, true));

            // 4. Repeat count (only if mode is RepeatCount)
            if (bill.repeatMode == BillRepeatModeDefOf.RepeatCount)
            {
                menuItems.Add(new MenuItem(MenuItemType.RepeatCount, GetRepeatCountLabel(),
                    "RimWorldAccess.Inspection.BillConfig.SearchLabel.RepeatCount".Translate(), null, true));
            }

            // 5-14. Target count block (only if mode is TargetCount)
            if (bill.repeatMode == BillRepeatModeDefOf.TargetCount)
            {
                menuItems.Add(new MenuItem(MenuItemType.TargetCount, GetTargetCountLabel(),
                    "RimWorldAccess.Inspection.BillConfig.SearchLabel.TargetCount".Translate(), null, true));

                // Currently have (read-only live count)
                menuItems.Add(new MenuItem(MenuItemType.CurrentlyHave, GetCurrentlyHaveLabel(),
                    "RimWorldAccess.Inspection.BillConfig.SearchLabel.CurrentlyHave".Translate(), null, false));

                ThingDef producedThingDef = bill.recipe.ProducedThingDef;
                if (producedThingDef != null)
                {
                    // Include equipped (weapons/apparel only)
                    if (producedThingDef.IsWeapon || producedThingDef.IsApparel)
                    {
                        menuItems.Add(new MenuItem(MenuItemType.IncludeEquipped, GetIncludeEquippedLabel(),
                            "IncludeEquipped".Translate().ToString(), null, true));
                    }

                    // Include tainted (apparel with corpse-care only)
                    if (producedThingDef.IsApparel && producedThingDef.apparel.careIfWornByCorpse)
                    {
                        menuItems.Add(new MenuItem(MenuItemType.IncludeTainted, GetIncludeTaintedLabel(),
                            "IncludeTainted".Translate().ToString(), null, true));
                    }

                    // Include source (count from which stockpile)
                    menuItems.Add(new MenuItem(MenuItemType.IncludeSource, GetIncludeSourceLabel(),
                        "IncludeFromAll".Translate().ToString(), null, true));

                    // HP range (products with hit points only)
                    if (bill.recipe.products.Any(p => p.thingDef.useHitPoints))
                    {
                        menuItems.Add(new MenuItem(MenuItemType.HpRange, GetHpRangeLabel(),
                            "HitPointsBasic".Translate().CapitalizeFirst().ToString(), null, true));
                    }

                    // Quality range (products with CompQuality only)
                    if (producedThingDef.HasComp(typeof(CompQuality)))
                    {
                        menuItems.Add(new MenuItem(MenuItemType.QualityRange, GetQualityRangeLabel(),
                            "Quality".Translate().ToString(), null, true));
                    }

                    // Limit to allowed stuff (products made from stuff only)
                    if (producedThingDef.MadeFromStuff)
                    {
                        menuItems.Add(new MenuItem(MenuItemType.LimitToAllowedStuff, GetLimitToAllowedStuffLabel(),
                            "LimitToAllowedStuff".Translate().ToString(), null, true));
                    }
                }

                // Pause when satisfied checkbox
                menuItems.Add(new MenuItem(MenuItemType.PauseWhenSatisfied, GetPauseWhenSatisfiedLabel(),
                    "PauseWhenSatisfied".Translate().ToString(), null, true));

                // Only show unpause threshold if pauseWhenSatisfied is enabled
                if (bill.pauseWhenSatisfied)
                {
                    menuItems.Add(new MenuItem(MenuItemType.UnpauseAt, GetUnpauseAtLabel(), "UnpauseWhenYouHave".Translate().ToString(), null, true));
                }
            }

            // 15. Store mode
            menuItems.Add(new MenuItem(MenuItemType.StoreMode, GetStoreModeLabel(), bill.GetStoreMode().LabelCap.ToString(), null, true));

            // 16. Pawn restriction
            menuItems.Add(new MenuItem(MenuItemType.PawnRestriction, GetPawnRestrictionLabel(), "AnyWorker".Translate().ToString(), null, true));

            // 17-18. Skill range (two items: min and max, conditional)
            if (bill.PawnRestriction == null && bill.recipe.workSkill != null && !bill.MechsOnly)
            {
                string skillSearchLabel = "AllowedSkillRange".Translate(bill.recipe.workSkill.label).ToString();
                menuItems.Add(new MenuItem(MenuItemType.SkillRangeMin, GetSkillRangeMinLabel(), skillSearchLabel, null, true));
                menuItems.Add(new MenuItem(MenuItemType.SkillRangeMax, GetSkillRangeMaxLabel(), skillSearchLabel, null, true));
            }

            // 19. Ingredient search radius
            menuItems.Add(new MenuItem(MenuItemType.IngredientSearchRadius, GetIngredientRadiusLabel(), "IngredientSearchRadius".Translate().ToString(), null, true));

            // 20. Ingredient filter
            menuItems.Add(new MenuItem(MenuItemType.IngredientFilter,
                "Filter".Translate() + " " + "Ingredients".Translate().ToLower() + "...",
                "Ingredients".Translate().ToString(), null, true));

            // 21. Rename bill
            menuItems.Add(new MenuItem(MenuItemType.RenameBill, GetRenameBillLabel(), "Rename".Translate().ToString(), null, true));

            // 22. Ideology styling (conditional)
            if (ModsConfig.IdeologyActive && !Find.IdeoManager.classicMode && bill.recipe.ProducedThingDef != null)
            {
                ThingDef producedDef = bill.recipe.ProducedThingDef;
                if (producedDef.RelevantStyleCategories != null && producedDef.RelevantStyleCategories.Any())
                {
                    menuItems.Add(new MenuItem(MenuItemType.StyleSelection, GetStyleLabel(), "Stat_Thing_StyleLabel".Translate().ToString(), null, true));
                }
            }

            // 23. Delete bill
            menuItems.Add(new MenuItem(MenuItemType.DeleteBill, "DeleteBillTip".Translate().ToString(), "DeleteBillTip".Translate().ToString(), null, true));
        }

        #region Label Generators

        private static string GetRecipeInfoLabel()
        {
            string label = "RimWorldAccess.Inspection.BillConfig.Recipe.Title".Translate(bill.recipe.LabelCap);

            // Recipe description (description text already includes trailing punctuation
            // from the def; we wrap with ". " separator only).
            if (!bill.recipe.description.NullOrEmpty())
            {
                label += $". {bill.recipe.description}";
            }

            // Work amount (formatted as hours)
            float workAmount = bill.recipe.WorkAmountTotal(null);
            if (workAmount > 0f)
            {
                label += "RimWorldAccess.Inspection.BillConfig.Recipe.WorkAmountSuffix".Translate(
                    "WorkAmount".Translate(), workAmount.ToStringWorkAmount());
            }

            // Minimum skill requirements (or just skill name if no requirements)
            if (!bill.recipe.skillRequirements.NullOrEmpty())
            {
                var reqs = bill.recipe.skillRequirements
                    .Select(r => "RimWorldAccess.Inspection.BillConfig.Recipe.SkillRequirement".Translate(
                        r.skill.LabelCap, r.minLevel).ToString());
                label += "RimWorldAccess.Inspection.BillConfig.Recipe.MinSkillsSuffix".Translate(
                    "MinimumSkills".Translate(), string.Join(", ", reqs));
            }
            else if (bill.recipe.workSkill != null)
            {
                label += "RimWorldAccess.Inspection.BillConfig.Recipe.WorkSkillSuffix".Translate(
                    bill.recipe.workSkill.LabelCap);
            }

            // Biotech: wearable by developmental stages
            if (ModsConfig.BiotechActive && bill.recipe.products != null && bill.recipe.products.Count == 1)
            {
                ThingDef thingDef = bill.recipe.products[0].thingDef;
                if (thingDef.IsApparel)
                {
                    label += "RimWorldAccess.Inspection.BillConfig.Recipe.WearableBySuffix".Translate(
                        "WearableBy".Translate(),
                        thingDef.apparel.developmentalStageFilter.ToCommaList().CapitalizeFirst());
                }
            }

            // Mech bill info
            if (bill is Bill_Mech)
            {
                label += "RimWorldAccess.Inspection.BillConfig.Recipe.GestationCyclesSuffix".Translate(
                    "GestationCycles".Translate(), bill.recipe.gestationCycles);
                ThingDef mechDef = bill.recipe.ProducedThingDef;
                if (mechDef != null)
                {
                    label += "RimWorldAccess.Inspection.BillConfig.Recipe.BandwidthSuffix".Translate(
                        "Bandwidth".Translate(), mechDef.GetStatValueAbstract(StatDefOf.BandwidthCost));
                    if (!bill.recipe.mechResurrection)
                    {
                        float wastepacks = (float)(int)mechDef.GetStatValueAbstract(StatDefOf.WastepacksPerRecharge)
                            * mechDef.GetStatValueAbstract(StatDefOf.BandwidthCost);
                        label += "RimWorldAccess.Inspection.BillConfig.Recipe.WastepacksSuffix".Translate(
                            Find.ActiveLanguageWorker.Pluralize(ThingDefOf.Wastepack.LabelCap),
                            "ThingsProduced".Translate(), wastepacks);
                    }
                }
            }

            return label;
        }

        private static string GetRepeatModeLabel()
        {
            return "RimWorldAccess.Inspection.BillConfig.Label.RepeatMode".Translate(bill.repeatMode.LabelCap);
        }

        private static string GetRepeatCountLabel()
        {
            return "RimWorldAccess.Inspection.BillConfig.Label.LabelWithValue".Translate(
                "RepeatCount".Translate(), bill.repeatCount);
        }

        private static string GetTargetCountLabel()
        {
            if (bill.targetCount >= 999999)
            {
                return "RimWorldAccess.Inspection.BillConfig.Label.TargetCountInfinite".Translate("Infinite".Translate());
            }
            return "RimWorldAccess.Inspection.BillConfig.Label.TargetCount".Translate(bill.targetCount);
        }

        private static string GetCurrentlyHaveLabel()
        {
            string targetSide = (bill.targetCount < 999999)
                ? bill.targetCount.ToString()
                : "Infinite".Translate().ToLower().ToString();
            string label = "RimWorldAccess.Inspection.BillConfig.Label.CurrentlyHave".Translate(
                "CurrentlyHave".Translate(),
                bill.recipe.WorkerCounter.CountProducts(bill),
                targetSide);

            string productsDesc = bill.recipe.WorkerCounter.ProductsDescription(bill);
            if (!productsDesc.NullOrEmpty())
            {
                label += "RimWorldAccess.Inspection.BillConfig.Label.CountingProductsSuffix".Translate(
                    "CountingProducts".Translate(), productsDesc.CapitalizeFirst());
            }
            return label;
        }

        private static string GetIncludeEquippedLabel()
        {
            return LabelWithOnOff("IncludeEquipped".Translate(), bill.includeEquipped);
        }

        private static string GetIncludeTaintedLabel()
        {
            return LabelWithOnOff("IncludeTainted".Translate(), bill.includeTainted);
        }

        private static string GetIncludeSourceLabel()
        {
            ISlotGroup group = bill.GetIncludeSlotGroup();
            if (group == null)
                return "IncludeFromAll".Translate().ToString();
            return "IncludeSpecific".Translate(SlotGroup.GetGroupLabel(group)).ToString();
        }

        private static string GetHpRangeLabel()
        {
            return "RimWorldAccess.Inspection.BillConfig.Label.LabelWithRangePercent".Translate(
                "HitPointsBasic".Translate().CapitalizeFirst(),
                bill.hpRange.min.ToStringPercent(),
                bill.hpRange.max.ToStringPercent());
        }

        private static string GetQualityRangeLabel()
        {
            return "RimWorldAccess.Inspection.BillConfig.Label.LabelWithRangeQuality".Translate(
                "Quality".Translate(), bill.qualityRange.min.GetLabel(), bill.qualityRange.max.GetLabel());
        }

        private static string GetLimitToAllowedStuffLabel()
        {
            return LabelWithOnOff("LimitToAllowedStuff".Translate(), bill.limitToAllowedStuff);
        }

        private static string GetPauseWhenSatisfiedLabel()
        {
            return LabelWithOnOff("PauseWhenSatisfied".Translate(), bill.pauseWhenSatisfied);
        }

        private static string GetUnpauseAtLabel()
        {
            return "RimWorldAccess.Inspection.BillConfig.Label.LabelWithValue".Translate(
                "UnpauseWhenYouHave".Translate(), bill.unpauseWhenYouHave);
        }

        private static string GetStoreModeLabel()
        {
            string label = string.Format(
                bill.GetStoreMode().LabelCap.ToString(),
                (bill.GetSlotGroup() != null)
                    ? SlotGroup.GetGroupLabel(bill.GetSlotGroup())
                    : "");

            if (bill.GetSlotGroup() != null
                && !bill.recipe.WorkerCounter.CanPossiblyStore(bill, bill.GetSlotGroup()))
            {
                label += "RimWorldAccess.Inspection.BillConfig.Label.IncompatibleSuffix".Translate(
                    "IncompatibleLower".Translate());
            }

            return label;
        }

        private static string GetPawnRestrictionLabel()
        {
            string worker;
            if (bill.PawnRestriction != null)
                worker = bill.PawnRestriction.LabelShortCap;
            else if (ModsConfig.IdeologyActive && bill.SlavesOnly)
                worker = "AnySlave".Translate();
            else if (ModsConfig.BiotechActive && bill.recipe.mechanitorOnlyRecipe)
                worker = "AnyMechanitor".Translate();
            else if (ModsConfig.BiotechActive && bill.MechsOnly)
                worker = "AnyMech".Translate();
            else if (ModsConfig.BiotechActive && bill.NonMechsOnly)
                worker = "AnyNonMech".Translate();
            else
                worker = "AnyWorker".Translate();
            return "RimWorldAccess.Inspection.BillConfig.Label.WorkerWithLabel".Translate(worker);
        }

        private static string GetSkillRangeMinLabel()
        {
            return "RimWorldAccess.Inspection.BillConfig.Label.SkillRangeMin".Translate(
                "AllowedSkillRange".Translate(bill.recipe.workSkill.label),
                "RimWorldAccess.Stepper.Minimum".Translate(),
                bill.allowedSkillRange.min);
        }

        private static string GetSkillRangeMaxLabel()
        {
            return "RimWorldAccess.Inspection.BillConfig.Label.SkillRangeMax".Translate(
                "AllowedSkillRange".Translate(bill.recipe.workSkill.label),
                "RimWorldAccess.Stepper.Maximum".Translate(),
                bill.allowedSkillRange.max);
        }

        private static string GetIngredientRadiusLabel()
        {
            string value = bill.ingredientSearchRadius >= 999f
                ? "Unlimited".Translate().ToString()
                : bill.ingredientSearchRadius.ToString("F0");
            return "RimWorldAccess.Inspection.BillConfig.Label.LabelWithValue".Translate(
                "IngredientSearchRadius".Translate(), value);
        }

        private static string GetRenameBillLabel()
        {
            string custom = bill.RenamableLabel;
            string baseName = bill.BaseLabel;
            if (custom != baseName)
                return "RimWorldAccess.Inspection.BillConfig.Label.RenameWithCustom".Translate(
                    "Rename".Translate(), custom, baseName);
            return "Rename".Translate().ToString();
        }

        private static string GetStyleLabel()
        {
            string stylePrefix = "Stat_Thing_StyleLabel".Translate().ToString();
            if (bill.globalStyle)
            {
                return "RimWorldAccess.Inspection.BillConfig.Label.LabelWithValue".Translate(
                    stylePrefix, "UseGlobalStyle".Translate());
            }
            if (bill.style != null)
            {
                return "RimWorldAccess.Inspection.BillConfig.Label.LabelWithValue".Translate(
                    stylePrefix, bill.style.Category.LabelCap);
            }
            return stylePrefix;
        }

        /// <summary>
        /// Composes "{label}: {On/Off}" using vanilla "On" and "Off" keys for the
        /// boolean state and the shared LabelWithValue key for the colon glue.
        /// </summary>
        private static string LabelWithOnOff(string label, bool value)
        {
            return "RimWorldAccess.Inspection.BillConfig.Label.LabelWithValue".Translate(
                label, (value ? "On" : "Off").Translate());
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
                case MenuItemType.SkillRangeMin:
                    return GetSkillRangeMinLabel();
                case MenuItemType.SkillRangeMax:
                    return GetSkillRangeMaxLabel();
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
                TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.FinishEditingFirst".Loc());
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
                TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.FinishEditingFirst".Loc());
                return;
            }

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, menuItems.Count);
            AnnounceCurrentSelection();
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
        /// Handles typeahead character input from the layout-aware dispatcher.
        /// Wraps <see cref="ProcessTypeaheadCharacter"/> with the no-match announcement.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive) return;
            if (!ProcessTypeaheadCharacter(c))
            {
                typeahead.SpeakNoMatches();
            }
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
                announcement += typeahead.BuildSearchContextSuffix();
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
                TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.NotAdjustable".Loc(), SpeechPriority.High);
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

                case MenuItemType.SkillRangeMin:
                    AdjustSkillRangeMin(direction);
                    break;

                case MenuItemType.SkillRangeMax:
                    AdjustSkillRangeMax(direction);
                    break;

                case MenuItemType.IngredientSearchRadius:
                    AdjustIngredientRadius(direction, multiplier);
                    break;

                default:
                    TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.UseEnterToOpenSubmenu".Loc());
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
                    AnnounceCurrentSelection();
                    break;

                case MenuItemType.UnpauseBill:
                    bill.paused = false;
                    BuildMenuItems();
                    AnnounceCurrentSelection();
                    break;

                case MenuItemType.PauseWhenSatisfied:
                    bill.pauseWhenSatisfied = !bill.pauseWhenSatisfied;
                    if (bill.pauseWhenSatisfied && bill.unpauseWhenYouHave >= bill.targetCount)
                    {
                        bill.unpauseWhenYouHave = bill.targetCount - 1;
                    }
                    BuildMenuItems();
                    AnnounceCurrentSelection();
                    break;

                case MenuItemType.IncludeEquipped:
                    bill.includeEquipped = !bill.includeEquipped;
                    BuildMenuItems();
                    AnnounceCurrentSelection();
                    break;

                case MenuItemType.IncludeTainted:
                    bill.includeTainted = !bill.includeTainted;
                    BuildMenuItems();
                    AnnounceCurrentSelection();
                    break;

                case MenuItemType.LimitToAllowedStuff:
                    bill.limitToAllowedStuff = !bill.limitToAllowedStuff;
                    BuildMenuItems();
                    AnnounceCurrentSelection();
                    break;

                case MenuItemType.IncludeSource:
                    OpenIncludeSourceMenu();
                    break;

                case MenuItemType.HpRange:
                    RangeEditMenuState.OpenHitPointsRange(bill.hpRange);
                    break;

                case MenuItemType.QualityRange:
                    RangeEditMenuState.OpenQualityRange(bill.qualityRange);
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

                case MenuItemType.RenameBill:
                    StartTextInput();
                    break;

                case MenuItemType.StyleSelection:
                    OpenStyleMenu();
                    break;

                case MenuItemType.DeleteBill:
                    DeleteBill();
                    break;

                default:
                    TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.UseLeftRightToAdjust".Loc());
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
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }
            if (bill.repeatCount == 1 && direction < 0)
            {
                NumericStepperHelper.SpeakValueAtMinimum("1");
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
                TolkHelper.Speak("Infinite".Translate().ToString());
                menuItems[selectedIndex].label = GetTargetCountLabel();
                return;
            }

            // Check if we hit a boundary
            if (bill.targetCount == oldValue)
            {
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }
            if (bill.targetCount == 1 && direction < 0)
            {
                NumericStepperHelper.SpeakValueAtMinimum("1");
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
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }
            if (bill.unpauseWhenYouHave == 0 && direction < 0)
            {
                NumericStepperHelper.SpeakValueAtMinimum("0");
            }
            else if (bill.unpauseWhenYouHave == maxValue && direction > 0)
            {
                NumericStepperHelper.SpeakValueAtMaximum(bill.unpauseWhenYouHave.ToString());
            }
            else
            {
                TolkHelper.Speak(bill.unpauseWhenYouHave.ToString());
            }

            menuItems[selectedIndex].label = GetUnpauseAtLabel();
        }

        private static void AdjustSkillRangeMin(int direction)
        {
            int oldMin = bill.allowedSkillRange.min;
            int newMin = Mathf.Clamp(oldMin + direction, 0, bill.allowedSkillRange.max);
            if (newMin == oldMin)
            {
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }
            bill.allowedSkillRange = new IntRange(newMin, bill.allowedSkillRange.max);
            menuItems[selectedIndex].label = GetSkillRangeMinLabel();
            TolkHelper.Speak(newMin.ToString());
        }

        private static void AdjustSkillRangeMax(int direction)
        {
            int oldMax = bill.allowedSkillRange.max;
            int newMax = Mathf.Clamp(oldMax + direction, bill.allowedSkillRange.min, 20);
            if (newMax == oldMax)
            {
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }
            bill.allowedSkillRange = new IntRange(bill.allowedSkillRange.min, newMax);
            menuItems[selectedIndex].label = GetSkillRangeMaxLabel();
            TolkHelper.Speak(newMax.ToString());
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
                        MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Minimum);
                        return;
                    }
                    bill.repeatCount = 1;
                    NumericStepperHelper.SpeakValueAtMinimum("1");
                    break;

                case MenuItemType.TargetCount:
                    if (bill.targetCount == 1)
                    {
                        MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Minimum);
                        return;
                    }
                    bill.targetCount = 1;
                    if (bill.pauseWhenSatisfied && bill.unpauseWhenYouHave >= bill.targetCount)
                    {
                        bill.unpauseWhenYouHave = 0;
                    }
                    NumericStepperHelper.SpeakValueAtMinimum("1");
                    break;

                case MenuItemType.UnpauseAt:
                    if (bill.unpauseWhenYouHave == 0)
                    {
                        MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Minimum);
                        return;
                    }
                    bill.unpauseWhenYouHave = 0;
                    NumericStepperHelper.SpeakValueAtMinimum("0");
                    break;

                case MenuItemType.IngredientSearchRadius:
                    if (bill.ingredientSearchRadius <= 3f)
                    {
                        MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Minimum);
                        return;
                    }
                    bill.ingredientSearchRadius = 3f;
                    NumericStepperHelper.SpeakValueAtMinimum("3");
                    break;

                case MenuItemType.SkillRangeMin:
                    if (bill.allowedSkillRange.min == 0)
                    {
                        MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Minimum);
                        return;
                    }
                    bill.allowedSkillRange = new IntRange(0, bill.allowedSkillRange.max);
                    NumericStepperHelper.SpeakValueAtMinimum("0");
                    break;

                case MenuItemType.SkillRangeMax:
                    if (bill.allowedSkillRange.max == bill.allowedSkillRange.min)
                    {
                        MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Minimum);
                        return;
                    }
                    bill.allowedSkillRange = new IntRange(bill.allowedSkillRange.min, bill.allowedSkillRange.min);
                    NumericStepperHelper.SpeakValueAtMinimum(bill.allowedSkillRange.min.ToString());
                    break;

                default:
                    TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.FieldNotAdjustable".Loc());
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
                    TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.NoMaximumLimit".Loc());
                    return;

                case MenuItemType.TargetCount:
                    if (bill.targetCount >= 999999)
                    {
                        MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Maximum);
                        return;
                    }
                    bill.targetCount = 999999;
                    NumericStepperHelper.SpeakValueAtMaximum("Infinite".Translate());
                    break;

                case MenuItemType.UnpauseAt:
                    int maxValue = bill.targetCount - 1;
                    if (bill.unpauseWhenYouHave == maxValue)
                    {
                        MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Maximum);
                        return;
                    }
                    bill.unpauseWhenYouHave = maxValue;
                    NumericStepperHelper.SpeakValueAtMaximum(maxValue.ToString());
                    break;

                case MenuItemType.IngredientSearchRadius:
                    if (bill.ingredientSearchRadius >= 999f)
                    {
                        MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Maximum);
                        return;
                    }
                    bill.ingredientSearchRadius = 999f;
                    NumericStepperHelper.SpeakValueAtMaximum("Unlimited".Translate());
                    break;

                case MenuItemType.SkillRangeMin:
                    if (bill.allowedSkillRange.min == bill.allowedSkillRange.max)
                    {
                        MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Maximum);
                        return;
                    }
                    bill.allowedSkillRange = new IntRange(bill.allowedSkillRange.max, bill.allowedSkillRange.max);
                    NumericStepperHelper.SpeakValueAtMaximum(bill.allowedSkillRange.max.ToString());
                    break;

                case MenuItemType.SkillRangeMax:
                    if (bill.allowedSkillRange.max == 20)
                    {
                        MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Maximum);
                        return;
                    }
                    bill.allowedSkillRange = new IntRange(bill.allowedSkillRange.min, 20);
                    NumericStepperHelper.SpeakValueAtMaximum("20");
                    break;

                default:
                    TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.FieldNotAdjustable".Loc());
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
                item.type != MenuItemType.IngredientSearchRadius &&
                item.type != MenuItemType.SkillRangeMin &&
                item.type != MenuItemType.SkillRangeMax)
            {
                // Not a numeric field - execute the action instead
                ExecuteSelected();
                return;
            }

            numericBuffer = "";
            isNumericInputMode = true;
            TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Numeric.Prompt".Loc());
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
                TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Numeric.Empty".Loc(), SpeechPriority.Low);
            }
        }

        /// <summary>
        /// Confirms and applies the numeric input value.
        /// </summary>
        public static void ConfirmNumericInput()
        {
            if (!isNumericInputMode) return;

            if (int.TryParse(numericBuffer, out int value) && value >= 0)
            {
                ApplyNumericValue(value);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Numeric.InvalidNumber".Loc());
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
            TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Numeric.Cancelled".Loc());
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
                        TolkHelper.Speak("Infinite".Translate().ToString());
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
                        TolkHelper.Speak("Unlimited".Loc());
                    }
                    else
                    {
                        bill.ingredientSearchRadius = Mathf.Clamp(value, 3, 100);
                        TolkHelper.Speak(bill.ingredientSearchRadius.ToString("F0"));
                    }
                    menuItems[selectedIndex].label = GetIngredientRadiusLabel();
                    break;

                case MenuItemType.SkillRangeMin:
                    int newMin = Mathf.Clamp(value, 0, bill.allowedSkillRange.max);
                    bill.allowedSkillRange = new IntRange(newMin, bill.allowedSkillRange.max);
                    menuItems[selectedIndex].label = GetSkillRangeMinLabel();
                    TolkHelper.Speak(menuItems[selectedIndex].label);
                    break;

                case MenuItemType.SkillRangeMax:
                    int newMax = Mathf.Clamp(value, bill.allowedSkillRange.min, 20);
                    bill.allowedSkillRange = new IntRange(bill.allowedSkillRange.min, newMax);
                    menuItems[selectedIndex].label = GetSkillRangeMaxLabel();
                    TolkHelper.Speak(menuItems[selectedIndex].label);
                    break;

                default:
                    TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Numeric.NotApplicable".Loc());
                    break;
            }
        }

        #endregion

        #region Text Input Methods (Bill Rename)

        // Modal bill rename — controller registers with TextInputManager so the
        // priority -1.6 dispatch in UnifiedKeyboardPatch handles every key.
        private static readonly TextFieldSpec billRenameSpec = new TextFieldSpec(
            labelKey: "RimWorldAccess.TextInput.LabelDefault",
            maxLength: 28,
            minLength: 1);

        private static void StartTextInput()
        {
            billRenameController.Begin(bill.RenamableLabel, billRenameSpec, OnBillRenameConfirm, OnBillRenameCancel, replaceOnType: true);
        }

        private static void OnBillRenameConfirm(string newName)
        {
            bill.RenamableLabel = newName;
            BuildMenuItems();
            BillsMenuState.RefreshMenuItems();
            TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.RenamedTo".Loc(newName));
            AnnounceCurrentSelection();
        }

        private static void OnBillRenameCancel()
        {
            TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.RenameCancelled".Loc());
        }

        #endregion

        #region Range Edit Integration

        /// <summary>
        /// Applies range changes from RangeEditMenuState back to the bill.
        /// Called from BuildingInspectPatch when range editing completes.
        /// </summary>
        public static void ApplyRangeChanges(FloatRange hitPoints, QualityRange quality)
        {
            if (menuItems == null || selectedIndex >= menuItems.Count || bill == null)
                return;

            var item = menuItems[selectedIndex];
            if (item.type == MenuItemType.HpRange)
            {
                bill.hpRange = hitPoints;
                // Match vanilla rounding
                bill.hpRange = new FloatRange(
                    Mathf.Round(bill.hpRange.min * 100f) / 100f,
                    Mathf.Round(bill.hpRange.max * 100f) / 100f);
                TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.HitPointsApplied".Loc(
                    bill.hpRange.min.ToStringPercent(), bill.hpRange.max.ToStringPercent()));
            }
            else if (item.type == MenuItemType.QualityRange)
            {
                bill.qualityRange = quality;
                TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.QualityApplied".Loc(
                    bill.qualityRange.min.GetLabel(), bill.qualityRange.max.GetLabel()));
            }

            BuildMenuItems();
            AnnounceCurrentSelection();
        }

        #endregion

        private static void AdjustIngredientRadius(int direction, int multiplier = 1)
        {
            float oldValue = bill.ingredientSearchRadius;

            // Handle unlimited state
            if (bill.ingredientSearchRadius >= 999f)
            {
                if (direction < 0)
                {
                    bill.ingredientSearchRadius = 100f;
                    TolkHelper.SpeakData("100");
                }
                else
                {
                    MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Maximum);
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
                        NumericStepperHelper.SpeakValueAtMaximum("Unlimited".Translate());
                    }
                    else
                    {
                        bill.ingredientSearchRadius = 100f;
                        TolkHelper.SpeakData("100");
                    }
                }
                else
                {
                    // Shift+Ctrl+Down = jump to 3
                    bill.ingredientSearchRadius = 3f;
                    NumericStepperHelper.SpeakValueAtMinimum("3");
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
                NumericStepperHelper.SpeakValueAtMaximum("Unlimited".Translate());
                menuItems[selectedIndex].label = GetIngredientRadiusLabel();
                return;
            }

            // Check if we hit a boundary
            if (bill.ingredientSearchRadius == oldValue)
            {
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }

            // Announce the new value
            if (bill.ingredientSearchRadius == 3f && direction < 0)
            {
                NumericStepperHelper.SpeakValueAtMinimum("3");
            }
            else if (bill.ingredientSearchRadius >= 100f)
            {
                TolkHelper.Speak("100");
            }
            else
            {
                TolkHelper.SpeakData($"{bill.ingredientSearchRadius:F0}");
            }

            menuItems[selectedIndex].label = GetIngredientRadiusLabel();
        }

        #region Submenu Methods

        private static void OpenStoreModeMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (BillStoreModeDef storeDef in DefDatabase<BillStoreModeDef>.AllDefs
                .OrderBy(bsm => bsm.listOrder))
            {
                if (storeDef == BillStoreModeDefOf.SpecificStockpile)
                {
                    FillOutputDropdownOptions(options,
                        BillStoreModeDefOf.SpecificStockpile.LabelCap,
                        delegate(ISlotGroup slot)
                        {
                            bill.SetStoreMode(BillStoreModeDefOf.SpecificStockpile, slot);
                            BuildMenuItems();
                            AnnounceCurrentSelection();
                        });
                }
                else
                {
                    BillStoreModeDef smLocal = storeDef;
                    options.Add(new FloatMenuOption(smLocal.LabelCap, delegate
                    {
                        bill.SetStoreMode(smLocal);
                        BuildMenuItems();
                        AnnounceCurrentSelection();
                    }));
                }
            }

            WindowlessFloatMenuState.Open(options, false, announceSelection: false);
        }

        private static void OpenPawnRestrictionMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            Map map = bill.billStack.billGiver.Map;

            if (ModsConfig.BiotechActive && bill.recipe.mechanitorOnlyRecipe)
            {
                // Mechanitor-only recipe: show AnyMechanitor + only mechanitor pawns
                options.Add(new FloatMenuOption("AnyMechanitor".Translate().ToString(), delegate
                {
                    bill.SetAnyPawnRestriction();
                    BuildMenuItems();
                    AnnounceCurrentSelection();
                }));

                foreach (Pawn pawn in map.mapPawns.FreeColonists.Where(MechanitorUtility.IsMechanitor))
                {
                    Pawn localPawn = pawn;
                    string label = pawn.LabelShortCap;
                    options.Add(new FloatMenuOption(label, delegate
                    {
                        bill.SetPawnRestriction(localPawn);
                        BuildMenuItems();
                        AnnounceCurrentSelection();
                    }));
                }
            }
            else
            {
                // Standard: AnyWorker
                options.Add(new FloatMenuOption("AnyWorker".Translate().ToString(), delegate
                {
                    bill.SetAnyPawnRestriction();
                    BuildMenuItems();
                    AnnounceCurrentSelection();
                }));

                // Ideology: AnySlave
                if (ModsConfig.IdeologyActive)
                {
                    options.Add(new FloatMenuOption("AnySlave".Translate().ToString(), delegate
                    {
                        bill.SetAnySlaveRestriction();
                        BuildMenuItems();
                        AnnounceCurrentSelection();
                    }));
                }

                // Biotech: AnyMech / AnyNonMech
                if (ModsConfig.BiotechActive && MechWorkUtility.AnyWorkMechCouldDo(bill.recipe))
                {
                    options.Add(new FloatMenuOption("AnyMech".Translate().ToString(), delegate
                    {
                        bill.SetAnyMechRestriction();
                        BuildMenuItems();
                        AnnounceCurrentSelection();
                    }));
                    options.Add(new FloatMenuOption("AnyNonMech".Translate().ToString(), delegate
                    {
                        bill.SetAnyNonMechRestriction();
                        BuildMenuItems();
                        AnnounceCurrentSelection();
                    }));
                }

                // Individual pawns
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
                        label = "RimWorldAccess.Inspection.BillConfig.Label.PawnWithSkillSuffix".Translate(
                            label, skillLevel);
                    }

                    Pawn localPawn = pawn;
                    options.Add(new FloatMenuOption(label, delegate
                    {
                        bill.SetPawnRestriction(localPawn);
                        BuildMenuItems();
                        AnnounceCurrentSelection();
                    }));
                }
            }

            WindowlessFloatMenuState.Open(options, false, announceSelection: false);
        }

        private static void OpenIncludeSourceMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            // Include from all
            options.Add(new FloatMenuOption("IncludeFromAll".Translate().ToString(), delegate
            {
                bill.SetIncludeGroup(null);
                BuildMenuItems();
                AnnounceCurrentSelection();
            }));

            // Specific storage locations (grouped like vanilla)
            FillOutputDropdownOptions(options,
                "IncludeSpecific".Translate(),
                delegate(ISlotGroup slot)
                {
                    bill.SetIncludeGroup(slot);
                    BuildMenuItems();
                    AnnounceCurrentSelection();
                });

            WindowlessFloatMenuState.Open(options, false, announceSelection: false);
        }

        private static void OpenStyleMenu()
        {
            if (bill.recipe.ProducedThingDef == null)
                return;

            ThingDef producedDef = bill.recipe.ProducedThingDef;
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            // Use global style
            options.Add(new FloatMenuOption("UseGlobalStyle".Translate().ToString(), delegate
            {
                bill.globalStyle = true;
                bill.style = null;
                bill.graphicIndexOverride = null;
                BuildMenuItems();
                AnnounceCurrentSelection();
            }));

            // Basic (no style)
            options.Add(new FloatMenuOption("RimWorldAccess.Inspection.BillConfig.Style.Basic".Translate(), delegate
            {
                bill.globalStyle = false;
                bill.style = null;
                bill.graphicIndexOverride = null;
                BuildMenuItems();
                AnnounceCurrentSelection();
            }));

            // Per-style category options
            if (producedDef.RelevantStyleCategories != null)
            {
                foreach (StyleCategoryDef styleCat in producedDef.RelevantStyleCategories)
                {
                    ThingStyleDef styleDef = styleCat.GetStyleForThingDef(producedDef);
                    if (styleDef != null)
                    {
                        StyleCategoryDef localCat = styleCat;
                        ThingStyleDef localStyle = styleDef;
                        options.Add(new FloatMenuOption(localCat.LabelCap.ToString(), delegate
                        {
                            bill.globalStyle = false;
                            bill.style = localStyle;
                            bill.graphicIndexOverride = null;
                            BuildMenuItems();
                            AnnounceCurrentSelection();
                        }));
                    }
                }
            }

            WindowlessFloatMenuState.Open(options, false, announceSelection: false);
        }

        /// <summary>
        /// Fills dropdown options for storage locations, replicating vanilla's
        /// FillOutputDropdownOptions logic with StorageGroup deduplication
        /// and unnamed Building_Storage filtering.
        /// </summary>
        private static void FillOutputDropdownOptions(
            List<FloatMenuOption> options,
            string prefix,
            Action<ISlotGroup> onSelected)
        {
            List<SlotGroup> allGroups = bill.billStack.billGiver.Map
                .haulDestinationManager.AllGroupsListInPriorityOrder;

            var groupsByLabel = new Dictionary<string, List<ISlotGroup>>();

            for (int i = 0; i < allGroups.Count; i++)
            {
                SlotGroup slotGroup = allGroups[i];

                if (slotGroup.StorageGroup != null)
                {
                    StorageGroup storageGroup = slotGroup.StorageGroup;
                    if (!groupsByLabel.ContainsKey(storageGroup.GroupingLabel))
                        groupsByLabel.Add(storageGroup.GroupingLabel, new List<ISlotGroup>());
                    if (!groupsByLabel[storageGroup.GroupingLabel].Contains(storageGroup))
                        groupsByLabel[storageGroup.GroupingLabel].Add(storageGroup);
                }
                else if (!(slotGroup.parent is Building_Storage) || slotGroup.parent is IRenameable)
                {
                    if (!groupsByLabel.ContainsKey(slotGroup.GroupingLabel))
                        groupsByLabel.Add(slotGroup.GroupingLabel, new List<ISlotGroup>());
                    groupsByLabel[slotGroup.GroupingLabel].Add(slotGroup);
                }
            }

            // Flatten groups maintaining GroupingOrder, then separate compatible from incompatible
            var orderedGroups = groupsByLabel
                .OrderBy(kv => (kv.Value.Count > 0) ? kv.Value[0].GroupingOrder : 0)
                .SelectMany(kv => kv.Value)
                .ToList();

            var compatible = new List<ISlotGroup>();
            var incompatible = new List<ISlotGroup>();

            foreach (var group in orderedGroups)
            {
                if (bill.recipe.WorkerCounter.CanPossiblyStore(bill, group))
                    compatible.Add(group);
                else
                    incompatible.Add(group);
            }

            // Compatible locations first
            foreach (var group in compatible)
            {
                string label = string.Format(prefix, SlotGroup.GetGroupLabel(group));
                ISlotGroup localGroup = group;
                options.Add(new FloatMenuOption(label, delegate
                {
                    onSelected(localGroup);
                }));
            }

            // Incompatible locations after
            foreach (var group in incompatible)
            {
                string label = string.Format(prefix, SlotGroup.GetGroupLabel(group));
                options.Add(new FloatMenuOption(
                    label + "RimWorldAccess.Inspection.BillConfig.Label.IncompatibleSuffix".Translate(
                        "IncompatibleLower".Translate()),
                    null));
            }
        }

        private static void OpenIngredientFilterMenu()
        {
            ThingFilterMenuState.Open(bill.ingredientFilter, bill.recipe.fixedIngredientFilter,
                "RimWorldAccess.Inspection.BillConfig.IngredientFilterTitle".Translate());
        }

        private static void DeleteBill()
        {
            string billLabel = bill.LabelCap;
            bill.billStack.Delete(bill);
            TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.DeletedBill".Loc(billLabel));
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
            InfoCardState.TryOpenInfoCardForDef(bill?.recipe?.ProducedThingDef);
        }

        public static void Reannounce() => AnnounceCurrentSelection();

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
