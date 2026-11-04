using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// The bill configuration menu's FIELD half: which of one bill's settings are currently shown,
    /// what each says, and every write back into the bill.
    /// Navigation, typeahead, announcements and exact numeric entry live on
    /// <see cref="Shell.BillConfigScope"/>, which owns the cursor and asks this class about the row
    /// under it by index into <see cref="visibleFields"/>. Nothing here tracks a selection, a search
    /// or an announcement.
    /// </summary>
    public static partial class BillConfigState
    {
        private static Bill_Production bill = null;
        private static IntVec3 billGiverPos;
        private static bool isActive = false;

        /// <summary>The real dialog on screen for this session, so both close directions can find it; null between sessions.</summary>
        private static Window openedDialog;

        /// <summary>
        /// Vanilla's own protected virtual dialog factory, the vehicle the Details button rides, so
        /// a bill subclass with its own dialog is honored instead of hard-coding Dialog_BillConfig.
        /// </summary>
        private static readonly MethodInfo GetBillDialogMethod =
            AccessTools.Method(typeof(Bill_Production), "GetBillDialog");

        private static readonly AccessTools.FieldRef<Dialog_BillConfig, Bill_Production> DialogBillRef =
            AccessTools.FieldRefAccess<Dialog_BillConfig, Bill_Production>("bill");

        /// <summary>
        /// The descriptors whose <see cref="FieldDescriptor.IsVisible"/> currently holds, in table
        /// order: the row list <see cref="Shell.BillConfigScope"/> presents. Rebuilt by
        /// <see cref="RefreshRows"/> on every model refresh; labels are composed lazily per row, so a
        /// refresh never builds them all.
        /// </summary>
        private static readonly List<FieldDescriptor> visibleFields = new List<FieldDescriptor>();

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
            DeleteBill,
            Extra
        }

        /// <summary>
        /// One descriptor carries every behavior a field can have — visibility, label, search text,
        /// arrow step, Enter action, min/max jumps, typed value — rather than the same field
        /// reappearing in five parallel switches. A field lacking a behavior leaves that delegate
        /// null, and the dispatchers below refuse.
        /// </summary>
        private class FieldDescriptor
        {
            public readonly MenuItemType Type;
            public readonly Func<bool> IsVisible;
            public readonly Func<string> GetLabel;
            public readonly Func<string> GetSearchLabel;
            public readonly bool IsEditable;
            public readonly Action<int, int> Adjust;   // (direction, multiplier)
            public readonly Action Execute;             // Enter
            public readonly Action JumpMin;              // Shift+Home
            public readonly Action JumpMax;              // Shift+End
            public readonly Func<string> GetNumericText; // the digits exact entry starts from
            public readonly Action<int> ApplyNumeric;    // the typed value's commit

            /// <summary>Whether Enter opens exact numeric entry rather than running an action: the fields that can both state a value as digits and take one back.</summary>
            public bool TakesTypedNumber
            {
                get { return GetNumericText != null && ApplyNumeric != null; }
            }

            /// <summary>
            /// The control role a sighted player sees, harvested from Dialog_BillConfig's own
            /// widgets: float-menu openers are ComboBox, CheckboxLabeled rows Checkbox, IntEntry/
            /// IntRange rows Stepper, plain Label rows roleless, and anything whose Enter runs an
            /// action or opens an editor a Button.
            /// </summary>
            public readonly Shell.ElementRole Role;

            /// <summary>Live checkbox state for a Checkbox field; null for every other role.</summary>
            public readonly Func<Shell.CheckState?> GetCheck;

            public Func<string> GetDescription;
            public string RingKey;
            public Func<string> RingTripwire;

            public FieldDescriptor(MenuItemType type, Func<bool> isVisible, Func<string> getLabel,
                Func<string> getSearchLabel = null, bool isEditable = false,
                Action<int, int> adjust = null, Action execute = null,
                Action jumpMin = null, Action jumpMax = null,
                Func<string> numericText = null, Action<int> applyNumeric = null,
                Shell.ElementRole role = Shell.ElementRole.None, Func<Shell.CheckState?> getCheck = null)
            {
                Role = role;
                GetCheck = getCheck;
                Type = type;
                IsVisible = isVisible;
                GetLabel = getLabel;
                GetSearchLabel = getSearchLabel ?? getLabel;
                IsEditable = isEditable;
                Adjust = adjust;
                Execute = execute;
                GetNumericText = numericText;
                JumpMin = jumpMin;
                JumpMax = jumpMax;
                ApplyNumeric = applyNumeric;
            }
        }

        // Built once: every descriptor delegate reads the current static `bill` at call time.
        private static List<FieldDescriptor> fieldDescriptorOrder;

        private static void EnsureFieldDescriptors()
        {
            if (fieldDescriptorOrder != null) return;
            fieldDescriptorOrder = BuildFieldDescriptors();
        }

        public static bool IsActive => isActive;
        public static Bill_Production ConfiguredBill => bill;

        /// <summary>
        /// Opens the menu together with the real dialog a sighted player would see. The scope resets
        /// its cursor and speaks the entry announcement itself once the mirror pushes it.
        /// </summary>
        public static void Open(Bill_Production productionBill, IntVec3 position)
        {
            if (productionBill == null)
            {
                Log.Error("Cannot open bill config: bill is null");
                return;
            }

            // GetBillDialog computes the dialog's own billGiverPos; the position parameter stays
            // this state's own.
            Window dialog = GetBillDialogMethod?.Invoke(productionBill, null) as Window;
            if (dialog == null)
            {
            }

            StartSession(productionBill, position, dialog);

        }

        /// <summary>
        /// The mouse path: vanilla's Details button opens the dialog on its own, so the shell adopts
        /// it and drives the same rows. Reached through the ScopeForWindow registration, which also
        /// keeps the generic window reader off a dialog this state fronts.
        /// </summary>
        internal static void AdoptOpenDialog(Window window)
        {
            if (isActive || !(window is Dialog_BillConfig dialog))
            {
                return;
            }

            Bill_Production dialogBill = DialogBillRef(dialog);
            if (dialogBill?.billStack?.billGiver is Thing billGiver)
            {
                StartSession(dialogBill, billGiver.Position, window);
            }
        }

        /// <summary>
        /// Everything the two entry points must leave identical. <see cref="isActive"/> holds before
        /// the window reaches the stack, which is what makes <see cref="AdoptOpenDialog"/> stand down
        /// for a dialog opened from here.
        /// </summary>
        private static void StartSession(Bill_Production productionBill, IntVec3 position, Window dialog)
        {
            bill = productionBill;
            billGiverPos = position;
            isActive = true;
            openedDialog = dialog;
            if (TextInputManager.Active == billRenameController) TextInputManager.Clear();

            WindowStack stack = Find.WindowStack;
            if (dialog != null && stack != null && !stack.IsOpen(dialog))
            {
                stack.Add(dialog);
            }

            Shell.BillConfigScopeMirror.OpenFresh();
        }

        /// <summary>Closes the menu, and the dialog with it.</summary>
        public static void Close()
        {
            Window dialog = openedDialog;
            openedDialog = null;
            bill = null;
            visibleFields.Clear();
            isActive = false;
            if (TextInputManager.Active == billRenameController) TextInputManager.Clear();

            // Removed last, session already torn down, so the PostClose patch's re-entry through
            // NotifyDialogClosed finds nothing left to close.
            if (dialog != null)
            {
                Find.WindowStack?.TryRemove(dialog, true);
            }
        }

        /// <summary>
        /// The other close direction: the dialog leaving by a route this state did not drive ends the
        /// session too. Reference equality keeps a second, independently opened bill dialog from
        /// tearing this session down.
        /// </summary>
        internal static void NotifyDialogClosed(Window window)
        {
            if (!isActive || !ReferenceEquals(window, openedDialog))
            {
                return;
            }
            Close();
        }

        /// <summary>
        /// Re-evaluates which fields are visible. Called from the scope's content refresh, so a
        /// change that reveals or hides a row reshapes the list with no caller having to rebuild it.
        /// </summary>
        internal static void RefreshRows()
        {
            visibleFields.Clear();
            if (bill == null)
            {
                return;
            }
            EnsureFieldDescriptors();
            foreach (FieldDescriptor descriptor in fieldDescriptorOrder)
            {
                // The repeat-mode section ends just before the store mode button.
                if (descriptor.Type == MenuItemType.StoreMode)
                {
                    AddExtraRows();
                }
                if (descriptor.IsVisible())
                {
                    visibleFields.Add(descriptor);
                }
            }
        }

        private static void AddExtraRows()
        {
            foreach (Func<Bill_Production, IEnumerable<BillConfigExtraRow>> source in BillConfigExtraRow.Sources)
            {
                IEnumerable<BillConfigExtraRow> rows;
                try
                {
                    rows = source(bill);
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("BillConfigState.AddExtraRows", ex);
                    continue;
                }
                if (rows == null)
                {
                    continue;
                }
                foreach (BillConfigExtraRow row in rows)
                {
                    FieldDescriptor descriptor = row.Standard.HasValue ? StandardDescriptor(row.Standard.Value) : ExtraDescriptor(row);
                    if (descriptor != null)
                    {
                        visibleFields.Add(descriptor);
                    }
                }
            }
        }

        private static FieldDescriptor StandardDescriptor(BillConfigExtraRow.StandardRow row)
        {
            MenuItemType type;
            switch (row)
            {
                case BillConfigExtraRow.StandardRow.IncludeEquipped: type = MenuItemType.IncludeEquipped; break;
                case BillConfigExtraRow.StandardRow.IncludeTainted: type = MenuItemType.IncludeTainted; break;
                case BillConfigExtraRow.StandardRow.HpRange: type = MenuItemType.HpRange; break;
                case BillConfigExtraRow.StandardRow.QualityRange: type = MenuItemType.QualityRange; break;
                default: type = MenuItemType.LimitToAllowedStuff; break;
            }
            return fieldDescriptorOrder.Find(d => d.Type == type);
        }

        private static FieldDescriptor ExtraDescriptor(BillConfigExtraRow row)
        {
            Func<string> label = row.Label ?? (() => "");
            return new FieldDescriptor(MenuItemType.Extra,
                isVisible: () => true,
                getLabel: label,
                getSearchLabel: row.SearchLabel,
                isEditable: row.Editable,
                adjust: row.Adjust,
                execute: row.Execute,
                jumpMin: row.JumpMin,
                jumpMax: row.JumpMax,
                numericText: row.NumericText,
                applyNumeric: row.ApplyNumeric,
                role: row.Role)
            {
                GetDescription = row.Description,
                RingKey = row.RingKey,
                RingTripwire = row.RingTripwire,
            };
        }

        /// <summary>
        /// Builds the ordered field-descriptor table once, in vanilla's own Dialog_BillConfig row
        /// order, so display and typeahead order follow the dialog a sighted player reads. Each
        /// IsVisible predicate reproduces the exact condition, block nesting included, that vanilla
        /// guards that row's draw with.
        /// </summary>
        private static List<FieldDescriptor> BuildFieldDescriptors()
        {
            return new List<FieldDescriptor>
            {
                new FieldDescriptor(MenuItemType.RecipeInfo,
                    isVisible: () => true,
                    getLabel: GetRecipeInfoLabel,
                    getSearchLabel: () => "RimWorldAccess.Inspection.BillConfig.SearchLabel.Recipe".Translate().ToString()),

                // Vanilla is a ButtonText whose caption IS the state: a button, not a checkbox.
                new FieldDescriptor(MenuItemType.SuspendToggle,
                    isVisible: () => true,
                    getLabel: () => bill.suspended ? "Suspended".Translate().ToString() : "NotSuspended".Translate().ToString(),
                    isEditable: true,
                    execute: ExecuteSuspendToggle,
                    role: Shell.ElementRole.Button),

                new FieldDescriptor(MenuItemType.UnpauseBill,
                    isVisible: () => bill.paused,
                    getLabel: () => "Unpause".Translate().ToString(),
                    isEditable: true,
                    execute: ExecuteUnpauseBill,
                    role: Shell.ElementRole.Button),

                new FieldDescriptor(MenuItemType.RepeatMode,
                    isVisible: () => true,
                    getLabel: GetRepeatModeLabel,
                    getSearchLabel: () => "RimWorldAccess.Inspection.BillConfig.SearchLabel.RepeatMode".Translate().ToString(),
                    isEditable: true,
                    execute: OpenRepeatModeMenu,
                    role: Shell.ElementRole.ComboBox),

                new FieldDescriptor(MenuItemType.RepeatCount,
                    isVisible: () => bill.repeatMode == BillRepeatModeDefOf.RepeatCount,
                    getLabel: GetRepeatCountLabel,
                    getSearchLabel: () => "RimWorldAccess.Inspection.BillConfig.SearchLabel.RepeatCount".Translate().ToString(),
                    isEditable: true,
                    adjust: AdjustRepeatCount,
                    jumpMin: JumpRepeatCountToMin,
                    jumpMax: JumpRepeatCountToMax,
                    numericText: () => bill.repeatCount.ToString(),
                    applyNumeric: ApplyRepeatCountValue,
                    role: Shell.ElementRole.Stepper),

                new FieldDescriptor(MenuItemType.TargetCount,
                    isVisible: () => bill.repeatMode == BillRepeatModeDefOf.TargetCount,
                    getLabel: GetTargetCountLabel,
                    getSearchLabel: () => "RimWorldAccess.Inspection.BillConfig.SearchLabel.TargetCount".Translate().ToString(),
                    isEditable: true,
                    adjust: AdjustTargetCount,
                    jumpMin: JumpTargetCountToMin,
                    jumpMax: JumpTargetCountToMax,
                    numericText: () => bill.targetCount.ToString(),
                    applyNumeric: ApplyTargetCountValue,
                    role: Shell.ElementRole.Stepper),

                new FieldDescriptor(MenuItemType.CurrentlyHave,
                    isVisible: () => bill.repeatMode == BillRepeatModeDefOf.TargetCount,
                    getLabel: GetCurrentlyHaveLabel,
                    getSearchLabel: () => "RimWorldAccess.Inspection.BillConfig.SearchLabel.CurrentlyHave".Translate().ToString()),

                new FieldDescriptor(MenuItemType.IncludeEquipped,
                    isVisible: () => bill.repeatMode == BillRepeatModeDefOf.TargetCount
                        && bill.recipe.ProducedThingDef != null
                        && (bill.recipe.ProducedThingDef.IsWeapon || bill.recipe.ProducedThingDef.IsApparel),
                    getLabel: GetIncludeEquippedLabel,
                    getSearchLabel: () => "IncludeEquipped".Translate().ToString(),
                    isEditable: true,
                    execute: ExecuteIncludeEquippedToggle,
                    role: Shell.ElementRole.Checkbox,
                    getCheck: () => CheckStateOf(bill.includeEquipped)),

                new FieldDescriptor(MenuItemType.IncludeTainted,
                    isVisible: () => bill.repeatMode == BillRepeatModeDefOf.TargetCount
                        && bill.recipe.ProducedThingDef != null
                        && bill.recipe.ProducedThingDef.IsApparel
                        && bill.recipe.ProducedThingDef.apparel.careIfWornByCorpse,
                    getLabel: GetIncludeTaintedLabel,
                    getSearchLabel: () => "IncludeTainted".Translate().ToString(),
                    isEditable: true,
                    execute: ExecuteIncludeTaintedToggle,
                    role: Shell.ElementRole.Checkbox,
                    getCheck: () => CheckStateOf(bill.includeTainted)),

                new FieldDescriptor(MenuItemType.IncludeSource,
                    isVisible: () => bill.repeatMode == BillRepeatModeDefOf.TargetCount
                        && bill.recipe.ProducedThingDef != null,
                    getLabel: GetIncludeSourceLabel,
                    getSearchLabel: () => "IncludeFromAll".Translate().ToString(),
                    isEditable: true,
                    execute: OpenIncludeSourceMenu,
                    role: Shell.ElementRole.ComboBox),

                new FieldDescriptor(MenuItemType.HpRange,
                    isVisible: () => bill.repeatMode == BillRepeatModeDefOf.TargetCount
                        && bill.recipe.ProducedThingDef != null
                        && bill.recipe.products.Any(p => p.thingDef.useHitPoints),
                    getLabel: GetHpRangeLabel,
                    getSearchLabel: () => "HitPointsBasic".Translate().CapitalizeFirst().ToString(),
                    isEditable: true,
                    execute: () => RangeEditMenuState.OpenHitPointsRange(
                        () => bill.hpRange,
                        v => bill.hpRange = new FloatRange(Mathf.Round(v.min * 100f) / 100f,
                                                           Mathf.Round(v.max * 100f) / 100f),
                        onClosed: RangeEditClosed),
                    // Vanilla draws a two-handle FloatRange, but this row OPENS the shared range
                    // editor rather than adjusting by arrow, so Button is the honest role; the live
                    // range rides the label.
                    role: Shell.ElementRole.Button),

                new FieldDescriptor(MenuItemType.QualityRange,
                    isVisible: () => bill.repeatMode == BillRepeatModeDefOf.TargetCount
                        && bill.recipe.ProducedThingDef != null
                        && bill.recipe.ProducedThingDef.HasComp(typeof(CompQuality)),
                    getLabel: GetQualityRangeLabel,
                    getSearchLabel: () => "Quality".Translate().ToString(),
                    isEditable: true,
                    execute: () => RangeEditMenuState.OpenQualityRange(
                        () => bill.qualityRange,
                        v => bill.qualityRange = v,
                        onClosed: RangeEditClosed),
                    // Same reasoning as HpRange above.
                    role: Shell.ElementRole.Button),

                new FieldDescriptor(MenuItemType.LimitToAllowedStuff,
                    isVisible: () => bill.repeatMode == BillRepeatModeDefOf.TargetCount
                        && bill.recipe.ProducedThingDef != null
                        && bill.recipe.ProducedThingDef.MadeFromStuff,
                    getLabel: GetLimitToAllowedStuffLabel,
                    getSearchLabel: () => "LimitToAllowedStuff".Translate().ToString(),
                    isEditable: true,
                    execute: ExecuteLimitToAllowedStuffToggle,
                    role: Shell.ElementRole.Checkbox,
                    getCheck: () => CheckStateOf(bill.limitToAllowedStuff)),

                new FieldDescriptor(MenuItemType.PauseWhenSatisfied,
                    isVisible: () => bill.repeatMode == BillRepeatModeDefOf.TargetCount,
                    getLabel: GetPauseWhenSatisfiedLabel,
                    getSearchLabel: () => "PauseWhenSatisfied".Translate().ToString(),
                    isEditable: true,
                    execute: ExecutePauseWhenSatisfiedToggle,
                    role: Shell.ElementRole.Checkbox,
                    getCheck: () => CheckStateOf(bill.pauseWhenSatisfied)),

                new FieldDescriptor(MenuItemType.UnpauseAt,
                    isVisible: () => bill.repeatMode == BillRepeatModeDefOf.TargetCount && bill.pauseWhenSatisfied,
                    getLabel: GetUnpauseAtLabel,
                    getSearchLabel: () => "UnpauseWhenYouHave".Translate().ToString(),
                    isEditable: true,
                    adjust: AdjustUnpauseAt,
                    jumpMin: JumpUnpauseAtToMin,
                    jumpMax: JumpUnpauseAtToMax,
                    numericText: () => bill.unpauseWhenYouHave.ToString(),
                    applyNumeric: ApplyUnpauseAtValue,
                    role: Shell.ElementRole.Stepper),

                new FieldDescriptor(MenuItemType.StoreMode,
                    isVisible: () => true,
                    getLabel: GetStoreModeLabel,
                    getSearchLabel: () => bill.GetStoreMode().LabelCap.ToString(),
                    isEditable: true,
                    execute: OpenStoreModeMenu,
                    role: Shell.ElementRole.ComboBox),

                new FieldDescriptor(MenuItemType.PawnRestriction,
                    isVisible: () => true,
                    getLabel: GetPawnRestrictionLabel,
                    getSearchLabel: () => "AnyWorker".Translate().ToString(),
                    isEditable: true,
                    execute: OpenPawnRestrictionMenu,
                    role: Shell.ElementRole.ComboBox),

                new FieldDescriptor(MenuItemType.SkillRangeMin,
                    isVisible: () => bill.PawnRestriction == null && bill.recipe.workSkill != null && !bill.MechsOnly,
                    getLabel: GetSkillRangeMinLabel,
                    getSearchLabel: () => "AllowedSkillRange".Translate(bill.recipe.workSkill.label).ToString(),
                    isEditable: true,
                    adjust: (direction, multiplier) => AdjustSkillRangeMin(direction),
                    jumpMin: JumpSkillRangeMinToMin,
                    jumpMax: JumpSkillRangeMinToMax,
                    numericText: () => bill.allowedSkillRange.min.ToString(),
                    applyNumeric: ApplySkillRangeMinValue,
                    // Vanilla's IntRange slider, split into two bounds; each really is arrow-stepped
                    // here, so Stepper is the honest role.
                    role: Shell.ElementRole.Stepper),

                new FieldDescriptor(MenuItemType.SkillRangeMax,
                    isVisible: () => bill.PawnRestriction == null && bill.recipe.workSkill != null && !bill.MechsOnly,
                    getLabel: GetSkillRangeMaxLabel,
                    getSearchLabel: () => "AllowedSkillRange".Translate(bill.recipe.workSkill.label).ToString(),
                    isEditable: true,
                    adjust: (direction, multiplier) => AdjustSkillRangeMax(direction),
                    jumpMin: JumpSkillRangeMaxToMin,
                    jumpMax: JumpSkillRangeMaxToMax,
                    numericText: () => bill.allowedSkillRange.max.ToString(),
                    applyNumeric: ApplySkillRangeMaxValue,
                    role: Shell.ElementRole.Stepper),

                new FieldDescriptor(MenuItemType.IngredientSearchRadius,
                    isVisible: () => true,
                    getLabel: GetIngredientRadiusLabel,
                    getSearchLabel: () => "IngredientSearchRadius".Translate().ToString(),
                    isEditable: true,
                    adjust: AdjustIngredientRadius,
                    jumpMin: JumpIngredientRadiusToMin,
                    jumpMax: JumpIngredientRadiusToMax,
                    // "Unlimited" is a display word, never a seed: the field's own 999 is what
                    // vanilla's text box shows and what re-typing it means.
                    numericText: () => bill.ingredientSearchRadius.ToString("F0"),
                    applyNumeric: ApplyIngredientRadiusValue,
                    role: Shell.ElementRole.Stepper),

                new FieldDescriptor(MenuItemType.IngredientFilter,
                    isVisible: () => true,
                    getLabel: () => "Filter".Translate() + " " + "Ingredients".Translate().ToLower() + "...",
                    getSearchLabel: () => "Ingredients".Translate().ToString(),
                    isEditable: true,
                    execute: OpenIngredientFilterMenu,
                    role: Shell.ElementRole.Button),

                new FieldDescriptor(MenuItemType.RenameBill,
                    isVisible: () => true,
                    getLabel: GetRenameBillLabel,
                    getSearchLabel: () => "Rename".Translate().ToString(),
                    isEditable: true,
                    execute: StartTextInput,
                    // Enter starts the text session.
                    role: Shell.ElementRole.Button),

                new FieldDescriptor(MenuItemType.StyleSelection,
                    isVisible: () => ModsConfig.IdeologyActive && !Find.IdeoManager.classicMode
                        && bill.recipe.ProducedThingDef != null
                        && bill.recipe.ProducedThingDef.RelevantStyleCategories != null
                        && bill.recipe.ProducedThingDef.RelevantStyleCategories.Any(),
                    getLabel: GetStyleLabel,
                    getSearchLabel: () => "Stat_Thing_StyleLabel".Translate().ToString(),
                    isEditable: true,
                    execute: OpenStyleMenu,
                    role: Shell.ElementRole.ComboBox),

                new FieldDescriptor(MenuItemType.DeleteBill,
                    isVisible: () => true,
                    getLabel: () => "DeleteBillTip".Translate().ToString(),
                    isEditable: true,
                    execute: DeleteBill,
                    role: Shell.ElementRole.Button),
            };
        }

        #region Label Generators

        private static string GetRecipeInfoLabel()
        {
            string label = "RimWorldAccess.Inspection.BillConfig.Recipe.Title".Translate(bill.recipe.LabelCap);

            // The def's description already ends in punctuation, so only a ". " separator is added.
            if (!bill.recipe.description.NullOrEmpty())
            {
                label += $". {bill.recipe.description}";
            }

            float workAmount = bill.recipe.WorkAmountTotal(null);
            if (workAmount > 0f)
            {
                label += "RimWorldAccess.Inspection.BillConfig.Recipe.WorkAmountSuffix".Translate(
                    "WorkAmount".Translate(), workAmount.ToStringWorkAmount());
            }

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
            return CurrentlyHaveLabel(targetSide);
        }

        internal static string CurrentlyHaveLabel(string targetSide)
        {
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
            return "IncludeEquipped".Translate().ToString();
        }

        private static string GetIncludeTaintedLabel()
        {
            return "IncludeTainted".Translate().ToString();
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
            return "LimitToAllowedStuff".Translate().ToString();
        }

        private static string GetPauseWhenSatisfiedLabel()
        {
            return "PauseWhenSatisfied".Translate().ToString();
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

            // Vanilla's button has no caption, but a combo row needs its field named.
            return "RimWorldAccess.Inspection.BillConfig.Label.StoreModeWithLabel".Translate(label);
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

        /// <summary>The Check datum for one of vanilla's four CheckboxLabeled rows; the state rides the Check channel rather than being composed into the label.</summary>
        private static Shell.CheckState CheckStateOf(bool value)
        {
            return value ? Shell.CheckState.Checked : Shell.CheckState.Unchecked;
        }

        #endregion

    }
}
