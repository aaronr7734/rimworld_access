using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    public static partial class BillConfigState
    {
        #region Submenu Methods

        /// <summary>
        /// The repeat-mode combo's picker: the options vanilla's own builder makes, captured
        /// before its FloatMenu reaches the stack, so modes other mods patch in are offered too.
        /// </summary>
        private static void OpenRepeatModeMenu()
        {
            Bill_Production menuBill = bill;
            List<FloatMenuOption> captured = FloatMenuHarvest.Capture(
                () => BillRepeatModeUtility.MakeConfigFloatMenu(menuBill));
            if (captured == null || captured.Count == 0)
            {
                return;
            }

            var options = new List<FloatMenuOption>(captured.Count);
            foreach (FloatMenuOption option in captured)
            {
                Action vanillaAction = option.action;
                string label = option.Label;
                options.Add(new FloatMenuOption(label, vanillaAction == null ? (Action)null : delegate
                {
                    BillRepeatModeDef before = bill.repeatMode;
                    vanillaAction();
                    // A refused mode (RecipeCannotHaveTargetCount) already spoke its message.
                    if (bill.repeatMode != before || before.LabelCap.ToString() == label)
                    {
                        RowsChanged();
                    }
                }));
            }
            WindowlessFloatMenuState.Open(options, false, announceSelection: false);
        }

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
                            RowsChanged();
                        });
                }
                else
                {
                    BillStoreModeDef smLocal = storeDef;
                    options.Add(new FloatMenuOption(smLocal.LabelCap, delegate
                    {
                        bill.SetStoreMode(smLocal);
                        RowsChanged();
                    }));
                }
            }

            WindowlessFloatMenuState.Open(options, false, announceSelection: false);
        }

        private static void OpenPawnRestrictionMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            if (ModsConfig.BiotechActive && bill.recipe.mechanitorOnlyRecipe)
            {
                // Mechanitor-only recipe: AnyMechanitor plus mechanitor pawns.
                options.Add(new FloatMenuOption("AnyMechanitor".Translate().ToString(), delegate
                {
                    bill.SetAnyPawnRestriction();
                    RowsChanged();
                }));

                // Per-pawn rows come from vanilla's own builder, across all maps and with its
                // disabled/unassigned annotations, matching Dialog_BillConfig's mechanitor call.
                AddVanillaPawnRestrictionOptions(options,
                    BillDialogUtility.GetPawnRestrictionOptionsForBill(bill, MechanitorUtility.IsMechanitor));
            }
            else
            {
                options.Add(new FloatMenuOption("AnyWorker".Translate().ToString(), delegate
                {
                    bill.SetAnyPawnRestriction();
                    RowsChanged();
                }));

                if (ModsConfig.IdeologyActive)
                {
                    options.Add(new FloatMenuOption("AnySlave".Translate().ToString(), delegate
                    {
                        bill.SetAnySlaveRestriction();
                        RowsChanged();
                    }));
                }

                if (ModsConfig.BiotechActive && MechWorkUtility.AnyWorkMechCouldDo(bill.recipe))
                {
                    options.Add(new FloatMenuOption("AnyMech".Translate().ToString(), delegate
                    {
                        bill.SetAnyMechRestriction();
                        RowsChanged();
                    }));
                    options.Add(new FloatMenuOption("AnyNonMech".Translate().ToString(), delegate
                    {
                        bill.SetAnyNonMechRestriction();
                        RowsChanged();
                    }));
                }

                // Per-pawn rows come from vanilla's own builder, across all maps and with its
                // disabled/unassigned annotations, matching Dialog_BillConfig's standard call.
                AddVanillaPawnRestrictionOptions(options, BillDialogUtility.GetPawnRestrictionOptionsForBill(bill));
            }

            WindowlessFloatMenuState.Open(options, false, announceSelection: false);
        }

        /// <summary>
        /// Wraps each element vanilla's
        /// <see cref="BillDialogUtility.GetPawnRestrictionOptionsForBill"/> returns: a null action
        /// means a work-disabled pawn, kept as a disabled-but-navigable row; otherwise the vanilla
        /// action runs (vehicle A) before the menu refreshes.
        /// </summary>
        private static void AddVanillaPawnRestrictionOptions(
            List<FloatMenuOption> options,
            IEnumerable<Widgets.DropdownMenuElement<Pawn>> elements)
        {
            foreach (Widgets.DropdownMenuElement<Pawn> element in elements)
            {
                FloatMenuOption vanillaOption = element.option;

                if (vanillaOption.action == null)
                {
                    options.Add(new FloatMenuOption(vanillaOption.Label, null));
                    continue;
                }

                Action vanillaAction = vanillaOption.action;
                options.Add(new FloatMenuOption(vanillaOption.Label, delegate
                {
                    vanillaAction();
                    RowsChanged();
                }));
            }
        }

        private static void OpenIncludeSourceMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            options.Add(new FloatMenuOption("IncludeFromAll".Translate().ToString(), delegate
            {
                bill.SetIncludeGroup(null);
                RowsChanged();
            }));

            // Specific storage locations, grouped as vanilla groups them.
            FillOutputDropdownOptions(options,
                "IncludeSpecific".Translate(),
                delegate(ISlotGroup slot)
                {
                    bill.SetIncludeGroup(slot);
                    RowsChanged();
                });

            WindowlessFloatMenuState.Open(options, false, announceSelection: false);
        }

        private static void OpenStyleMenu()
        {
            if (bill.recipe.ProducedThingDef == null)
                return;

            ThingDef producedDef = bill.recipe.ProducedThingDef;
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            options.Add(new FloatMenuOption("UseGlobalStyle".Translate().ToString(), delegate
            {
                bill.globalStyle = true;
                bill.style = null;
                bill.graphicIndexOverride = null;
                RowsChanged();
            }));

            options.Add(new FloatMenuOption("RimWorldAccess.Inspection.BillConfig.Style.Basic".Translate(), delegate
            {
                bill.globalStyle = false;
                bill.style = null;
                bill.graphicIndexOverride = null;
                RowsChanged();
            }));

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
                            RowsChanged();
                        }));
                    }
                }
            }

            WindowlessFloatMenuState.Open(options, false, announceSelection: false);
        }

        /// <summary>
        /// Fills the storage-location dropdown. The bucketing loop's StorageGroup deduplication and
        /// unnamed Building_Storage filtering mirror vanilla directly; the collapse and inline
        /// passes and the per-group formatting are MUTATION-C: mirrors
        /// RimWorld.Dialog_BillConfig.FillOutputDropdownOptions/ShouldCollapseGroup/FillSlotGroupOptions;
        /// private dialog methods, no callable vehicle.
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

            // First pass: a bucket that should collapse becomes one entry in GroupingOrder, labeled
            // with the bucket's own grouping label rather than the per-group prefix format, and
            // opening it shows that bucket's groups in a nested menu.
            foreach (var kvp in groupsByLabel.OrderBy(kv => (kv.Value.Count > 0) ? kv.Value[0].GroupingOrder : 0))
            {
                if (!ShouldCollapseGroup(groupsByLabel, kvp.Key))
                    continue;

                string bucketLabel = kvp.Key;
                List<ISlotGroup> bucketGroups = kvp.Value;
                options.Add(new FloatMenuOption(bucketLabel, delegate
                {
                    List<FloatMenuOption> nestedOptions = new List<FloatMenuOption>();
                    FillSlotGroupOptions(bucketGroups, nestedOptions, prefix, onSelected);
                    WindowlessFloatMenuState.Open(nestedOptions, false, announceSelection: false);
                }));
            }

            // Second pass: non-collapsed buckets are inlined in place, in the same GroupingOrder.
            foreach (var kvp in groupsByLabel.OrderBy(kv => (kv.Value.Count > 0) ? kv.Value[0].GroupingOrder : 0))
            {
                if (!ShouldCollapseGroup(groupsByLabel, kvp.Key))
                    FillSlotGroupOptions(kvp.Value, options, prefix, onSelected);
            }
        }

        /// <summary>
        /// MUTATION-C: mirrors RimWorld.Dialog_BillConfig.ShouldCollapseGroup; private dialog
        /// method, no callable vehicle. A bucket collapses into a nested menu when it holds more
        /// than two groups AND at least one other bucket is non-empty.
        /// </summary>
        private static bool ShouldCollapseGroup(Dictionary<string, List<ISlotGroup>> groupsByLabel, string key)
        {
            bool hasOtherNonEmptyBucket = false;
            foreach (var kvp in groupsByLabel)
            {
                if (kvp.Key != key && kvp.Value.Count > 0)
                {
                    hasOtherNonEmptyBucket = true;
                    break;
                }
            }
            return groupsByLabel[key].Count > 2 && hasOtherNonEmptyBucket;
        }

        /// <summary>
        /// MUTATION-C: mirrors RimWorld.Dialog_BillConfig.FillSlotGroupOptions; private dialog
        /// method, no callable vehicle. Emits each group in place (not sorted to the end):
        /// compatible groups as actionable options, incompatible ones as disabled rows using
        /// vanilla's own "{0} ({1})"/IncompatibleLower label format.
        /// </summary>
        private static void FillSlotGroupOptions(
            List<ISlotGroup> groups,
            List<FloatMenuOption> options,
            string prefix,
            Action<ISlotGroup> onSelected)
        {
            foreach (ISlotGroup group in groups)
            {
                string label = string.Format(prefix, SlotGroup.GetGroupLabel(group));

                if (!bill.recipe.WorkerCounter.CanPossiblyStore(bill, group))
                {
                    options.Add(new FloatMenuOption(
                        label + "RimWorldAccess.Inspection.BillConfig.Label.IncompatibleSuffix".Translate(
                            "IncompatibleLower".Translate()),
                        null));
                    continue;
                }

                ISlotGroup localGroup = group;
                options.Add(new FloatMenuOption(label, delegate
                {
                    onSelected(localGroup);
                }));
            }
        }

        private static void OpenIngredientFilterMenu()
        {
            ThingFilterMenuState.Open(bill.ingredientFilter, bill.recipe.fixedIngredientFilter,
                "RimWorldAccess.Inspection.BillConfig.IngredientFilterTitle".Translate(),
                forceHiddenFilters: IngredientHiddenSpecialFilters.ConcatIfNotNull(bill.recipe.forceHiddenSpecialFilters));
        }

        private static List<SpecialThingFilterDef> cachedIngredientHiddenSpecialFilters;

        /// <summary>
        /// Vanilla-verbatim from Dialog_BillConfig.HiddenSpecialThingFilters: the Ideology diet
        /// filters every recipe's ingredient filter hides, independent of the per-recipe
        /// forceHiddenSpecialFilters this is concatenated with.
        /// </summary>
        private static IEnumerable<SpecialThingFilterDef> IngredientHiddenSpecialFilters
        {
            get
            {
                if (cachedIngredientHiddenSpecialFilters != null)
                    return cachedIngredientHiddenSpecialFilters;
                cachedIngredientHiddenSpecialFilters = new List<SpecialThingFilterDef>();
                if (ModsConfig.IdeologyActive)
                {
                    cachedIngredientHiddenSpecialFilters.Add(SpecialThingFilterDefOf.AllowCarnivore);
                    cachedIngredientHiddenSpecialFilters.Add(SpecialThingFilterDefOf.AllowVegetarian);
                    cachedIngredientHiddenSpecialFilters.Add(SpecialThingFilterDefOf.AllowCannibal);
                    cachedIngredientHiddenSpecialFilters.Add(SpecialThingFilterDefOf.AllowInsectMeat);
                }
                return cachedIngredientHiddenSpecialFilters;
            }
        }

        private static void DeleteBill()
        {
            string billLabel = bill.LabelCap;
            bill.billStack.Delete(bill);
            TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.DeletedBill".Loc(billLabel));
            Close();

            if (bill.billStack.billGiver is IBillGiver billGiver)
            {
                BillsMenuState.Open(billGiver, billGiverPos);
            }
        }

        #endregion

        /// <summary>Opens the info card for the current bill's product.</summary>
        public static void OpenInfoCard()
        {
            InfoCardState.TryOpenInfoCardForDef(bill?.recipe?.ProducedThingDef);
        }

        /// <summary>Re-announces the focused row; consulted by <see cref="InspectionReturnHelper"/>.</summary>
        public static void Reannounce()
        {
            if (isActive) Shell.BillConfigScopeMirror.ReannounceCurrent();
        }

        /// <summary>
        /// Re-evaluates the row list after a mutation and re-announces the row the cursor now sits
        /// on: the change is what was asked for, so the row's new state is the announcement.
        /// </summary>
        private static void RowsChanged()
        {
            Shell.BillConfigScopeMirror.ReannounceCurrent();
        }

        #region Focus Ring

        /// <summary>
        /// Which row of the live <see cref="Dialog_BillConfig"/> the focused menu row corresponds
        /// to, for <see cref="Shell.BillConfigScope"/>'s pass. Rows vanilla draws outside a listing
        /// ring nothing: the recipe block, the ingredient filter and its search radius, rename and
        /// delete, unpause (vanilla has no such row) and styling (vanilla's style button belongs to
        /// the classic ideoligion mode our own style row hides itself for).
        /// </summary>
        internal static Shell.ListingRingFocus ListingFocusFor(int index)
        {
            FieldDescriptor field = isActive && bill != null ? RowAt(index) : null;
            if (field == null)
            {
                return Shell.ListingRingFocus.None;
            }

            switch (field.Type)
            {
                case MenuItemType.SuspendToggle:
                    return bill.suspended
                        ? Ring(Shell.BillConfigRowKeys.Suspended, (string)"Suspended".Translate())
                        : Ring(Shell.BillConfigRowKeys.NotSuspended, (string)"NotSuspended".Translate());

                case MenuItemType.RepeatMode:
                    return Ring(Shell.BillConfigRowKeys.RepeatMode, (string)bill.repeatMode.LabelCap);

                case MenuItemType.RepeatCount:
                    return Ring(Shell.BillConfigRowKeys.RepeatCount,
                        (string)"RepeatCount".Translate(bill.repeatCount));

                // Many-to-one: vanilla folds the owned count and the target count into one label
                // (Dialog_BillConfig.cs:139-148).
                case MenuItemType.TargetCount:
                case MenuItemType.CurrentlyHave:
                    return Ring(Shell.BillConfigRowKeys.CurrentlyHave, VanillaCurrentlyHaveLabel());

                case MenuItemType.IncludeEquipped:
                    return Ring(Shell.BillConfigRowKeys.IncludeEquipped, (string)"IncludeEquipped".Translate());

                case MenuItemType.IncludeTainted:
                    return Ring(Shell.BillConfigRowKeys.IncludeTainted, (string)"IncludeTainted".Translate());

                case MenuItemType.IncludeSource:
                    return Ring(Shell.BillConfigRowKeys.IncludeSource, VanillaIncludeSourceLabel());

                // Raw FloatRange/QualityRange/Dropdown bands carry no listing caption, so the "#"
                // token's sentinel tripwire is what a null DrawnLabel accepts.
                case MenuItemType.HpRange:
                    return Ring(Shell.BillConfigRowKeys.HpRange, Shell.BillConfigRowKeys.RawBand);

                case MenuItemType.QualityRange:
                    return Ring(Shell.BillConfigRowKeys.QualityRange, Shell.BillConfigRowKeys.RawBand);

                case MenuItemType.PawnRestriction:
                    return Ring(Shell.BillConfigRowKeys.PawnRestriction, Shell.BillConfigRowKeys.RawBand);

                case MenuItemType.LimitToAllowedStuff:
                    return Ring(Shell.BillConfigRowKeys.LimitToAllowedStuff,
                        (string)"LimitToAllowedStuff".Translate());

                case MenuItemType.PauseWhenSatisfied:
                    return Ring(Shell.BillConfigRowKeys.PauseWhenSatisfied,
                        (string)"PauseWhenSatisfied".Translate());

                case MenuItemType.UnpauseAt:
                    return Ring(Shell.BillConfigRowKeys.UnpauseAt, VanillaUnpauseAtLabel());

                case MenuItemType.StoreMode:
                    return Ring(Shell.BillConfigRowKeys.StoreMode, VanillaStoreModeLabel());

                // Many-to-one again: vanilla captions the whole IntRange once.
                case MenuItemType.SkillRangeMin:
                case MenuItemType.SkillRangeMax:
                    return Ring(Shell.BillConfigRowKeys.SkillRange, VanillaSkillRangeLabel());

                case MenuItemType.Extra:
                    return field.RingKey == null
                        ? Shell.ListingRingFocus.None
                        : Ring(field.RingKey, field.RingTripwire != null ? field.RingTripwire() : null);

                default:
                    return Shell.ListingRingFocus.None;
            }
        }

        private static Shell.ListingRingFocus Ring(string rowKey, string tripwire)
        {
            return new Shell.ListingRingFocus { RowKey = rowKey, LabelTripwire = tripwire };
        }

        /// <summary>Mirrors the caption vanilla builds at Dialog_BillConfig.cs:139-148.</summary>
        private static string VanillaCurrentlyHaveLabel()
        {
            string text = (string)("CurrentlyHave".Translate() + ": ");
            text += bill.recipe.WorkerCounter.CountProducts(bill);
            text += " / ";
            text += (bill.targetCount < 999999)
                ? bill.targetCount.ToString()
                : "Infinite".Translate().ToLower().ToString();
            string products = bill.recipe.WorkerCounter.ProductsDescription(bill);
            if (!products.NullOrEmpty())
            {
                text += "\n" + "CountingProducts".Translate() + ": " + products.CapitalizeFirst();
            }
            return text;
        }

        /// <summary>Mirrors the caption vanilla builds at Dialog_BillConfig.cs:163.</summary>
        private static string VanillaIncludeSourceLabel()
        {
            ISlotGroup group = bill.GetIncludeSlotGroup();
            return (string)((group == null)
                ? "IncludeFromAll".Translate()
                : "IncludeSpecific".Translate(SlotGroup.GetGroupLabel(group)));
        }

        /// <summary>Mirrors the caption vanilla builds at Dialog_BillConfig.cs:207.</summary>
        private static string VanillaUnpauseAtLabel()
        {
            return (string)("UnpauseWhenYouHave".Translate() + ": " + bill.unpauseWhenYouHave.ToString("F0"));
        }

        /// <summary>Mirrors the caption vanilla builds at Dialog_BillConfig.cs:217-223.</summary>
        private static string VanillaStoreModeLabel()
        {
            string label = string.Format(bill.GetStoreMode().LabelCap,
                (bill.GetSlotGroup() != null) ? SlotGroup.GetGroupLabel(bill.GetSlotGroup()) : "");
            if (bill.GetSlotGroup() != null
                && !bill.recipe.WorkerCounter.CanPossiblyStore(bill, bill.GetSlotGroup()))
            {
                label += string.Format(" ({0})", "IncompatibleLower".Translate());
            }
            return label;
        }

        /// <summary>Mirrors the caption vanilla builds at Dialog_BillConfig.cs:251.</summary>
        private static string VanillaSkillRangeLabel()
        {
            return (string)("AllowedSkillRange".Translate(bill.recipe.workSkill.label) + ":");
        }

        #endregion

        /// <summary>
        /// The changed-state-only announcement after an arrow step: the new state, never the row
        /// identity, through AnnouncementComposer.ComposeStateChange so these steppers say what
        /// every other adjusted control says. Boundary wording stays with
        /// <see cref="NumericStepperHelper"/>, the mod-wide stepper vocabulary.
        /// </summary>
        internal static void SpeakValueChange(string value)
        {
            TolkHelper.SpeakData(Shell.AnnouncementComposer.ComposeStateChange(
                new Shell.ElementDescription { Value = value }, Shell.TranslatedShellVocabulary.Instance));
        }
    }
}
