using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// One linked consumer of a Production job's output: the consumer job (opaque, since
    /// <c>ManagerJob_Production</c> is internal to the mod), the producer ingredients it allows, and
    /// the demand it contributes. Unpacked field by field from the mod's own ValueTuple, whose first
    /// element type is internal and so cannot be named in this assembly.
    /// </summary>
    internal sealed class CmrLinkedConsumer
    {
        public object Consumer;
        public List<ThingDef> Covered;
        public int Demand;
    }

    internal static partial class CmrCompat
    {
        /// <summary>
        /// Colony Manager Redux's Production job and tab, as far as the job's detail rows
        /// (<see cref="Shell.CmrProductionDetails"/>) read and write them. Kept in its own block, like
        /// <see cref="Hunting"/> and <see cref="Livestock"/>, so a rename inside the Production tab
        /// cannot disturb the manager window's lists or another tab's detail rows.
        ///
        /// Production is the only job type whose Available list offers a recipe rather than a
        /// resource, and the only one managing live vanilla <c>Bill_Production</c>s rather than
        /// designations. Its job and tab types are internal to the mod, so callers pass both as
        /// <c>object</c>; every enum it exposes is internal by containment, so each travels as an
        /// index into its declaration-order names list rather than a boxed value.
        ///
        /// The shared <c>Trigger_Threshold</c> members are not bound here — Production's threshold
        /// callers reach them through <see cref="Threshold"/>'s job-taking overloads.
        ///
        /// MUTATION VEHICLES.
        /// <list type="bullet">
        /// <item>The mod's own gated members, invoked directly: the <c>Recipe</c> and <c>Mode</c>
        /// setters (both reseed derived state themselves), <c>LinkedDemandBufferCount</c>'s setter (it
        /// Untouches linked producers), <c>SetIngredientAllowed</c> (keeps the ConsumeSurplus sync
        /// direction correct), and <c>ManagerTab.MakeNewJob</c>/<c>Selected</c> for the Available
        /// row's click.</item>
        /// <item>Hand-copied writes for the rest: the tab draws every remaining control by handing a
        /// widget a <c>ref</c> to its storage or assigning a field in a float-menu option body, so the
        /// control's whole mutation IS that write. <see cref="SetField"/> is the single write site for
        /// the field-backed ones, and each public wrapper carries the citation for the draw call it
        /// reproduces.</item>
        /// <item><see cref="OpenThresholdDetails"/>, <see cref="OpenRecipeSwapMenu"/>,
        /// <see cref="OpenStoreModeMenu"/> and <see cref="OpenIngredientLinkMenu"/> each reproduce an
        /// inline button-click body statement for statement, because the button is inline IMGUI with
        /// no gated "open" method to call instead.</item>
        /// </list>
        /// </summary>
        internal static class Production
        {
            private static readonly Type tabType;

            private static readonly MethodInfo recipeGetter;
            private static readonly MethodInfo recipeSetter;
            private static readonly MethodInfo modeGetter;
            private static readonly MethodInfo modeSetter;
            private static readonly MethodInfo triggerThresholdGetter;

            private static readonly FieldInfo autoTargetFromLinksField;
            private static readonly FieldInfo autoRestrictIngredientsFromLinksField;
            private static readonly FieldInfo demandAggregationField;
            private static readonly FieldInfo syncFilterAndAllowedField;
            private static readonly FieldInfo consumeSurplusPerIngredientField;
            private static readonly FieldInfo maxAllowedSkillLevelField;
            private static readonly FieldInfo groupByWorkbenchField;
            private static readonly FieldInfo collapsedWorkbenchGroupsField;
            private static readonly MethodInfo workbenchGroupsGetter;
            private static readonly FieldInfo reservedStockOverridesField;
            private static readonly FieldInfo reservedStockInputBuffersField;
            private static readonly MethodInfo effectiveReservedStockMethod;
            private static readonly MethodInfo managerSettingsGetter;
            private static readonly FieldInfo globalReservedStockField;
            private static readonly MethodInfo relevantReservedIngredientsMethod;
            private static readonly MethodInfo addReservedStockOptionsMethod;
            private static readonly MethodInfo countedQualityRangeGetter;
            private static readonly MethodInfo countedQualityRangeSetter;
            private static readonly MethodInfo anyHasQualityMethod;
            private static readonly FieldInfo assignmentModeField;
            private static readonly FieldInfo workbenchAreaField;
            private static readonly FieldInfo invertWorkbenchAreaField;
            private static readonly FieldInfo allowedSkillRangeField;
            private static readonly FieldInfo ingredientSearchRadiusField;
            private static readonly FieldInfo linkedProducersField;
            private static readonly FieldInfo allowedIngredientsField;
            private static readonly FieldInfo specificWorkbenchesField;
            private static readonly FieldInfo storeModeField;
            private static readonly FieldInfo storeGroupField;

            private static readonly MethodInfo linkedDemandBufferCountGetter;
            private static readonly MethodInfo linkedDemandBufferCountSetter;
            private static readonly MethodInfo allEligibleWorkTablesGetter;
            private static readonly MethodInfo managedBillsGetter;
            private static readonly MethodInfo setIngredientAllowedMethod;
            private static readonly MethodInfo optionalRecipeIngredientOptionsMethod;
            private static readonly MethodInfo groupIngredientsByCategoryMethod;
            private static readonly MethodInfo resolvedOutputDefsMethod;
            private static readonly MethodInfo representativeThingDefMethod;

            private static readonly MethodInfo targetLabelGetter;
            private static readonly MethodInfo statusTooltipGetter;
            private static readonly MethodInfo currentCountMethod;
            private static readonly MethodInfo detailsWindowGetter;

            private static readonly FieldInfo availableRecipesField;
            private static readonly FieldInfo currentTabField;
            private static readonly MethodInfo tabListGetter;
            private static readonly MethodInfo tabRecordTabGetter;
            private static readonly MethodInfo makeNewJobMethod;
            private static readonly MethodInfo getSubLabelMethod;
            private static readonly MethodInfo computeRecipeSwapCandidatesMethod;
            private static readonly MethodInfo computeLinkedConsumerDemandsMethod;
            private static readonly MethodInfo computeIngredientSourceCandidatesMethod;
            private static readonly MethodInfo buildRecipeSwapOptionsMethod;
            private static readonly MethodInfo buildStoreModeOptionsMethod;
            private static readonly MethodInfo buildIngredientLinkOptionsMethod;

            private static readonly object[] modeValues;
            private static readonly List<string> modeNames = new List<string>();
            private static readonly object[] assignmentModeValues;
            private static readonly List<string> assignmentModeNames = new List<string>();
            private static readonly object[] demandAggregationValues;
            private static readonly List<string> demandAggregationNames = new List<string>();
            private static readonly object productionModeMaintainStock;
            private static readonly object productionModeConsumeSurplus;

            private static readonly bool ready;

            static Production()
            {
                var surface = new ReflectionSurface("CmrCompat.Production");

                Type jobType = surface.Type("ColonyManagerRedux.Managers.ManagerJob_Production");
                tabType = surface.Type("ColonyManagerRedux.Managers.ManagerTab_Production");
                Type managerTabBaseType = surface.Type("ColonyManagerRedux.ManagerTab");
                Type managerJobBaseType = surface.Type("ColonyManagerRedux.ManagerJob");
                Type triggerType = surface.Type("ColonyManagerRedux.Trigger_Threshold");
                Type tabRecordType = surface.Type("ilyvion.Laboratory.UI.TabRecord");
                Type resolversType = surface.Type("ColonyManagerRedux.RecipeProductResolvers");
                Type productionModeType =
                    surface.Type("ColonyManagerRedux.Managers.ManagerJob_Production+ProductionMode");
                Type assignmentModeType =
                    surface.Type("ColonyManagerRedux.Managers.ManagerJob_Production+WorkbenchAssignmentMode");
                Type demandAggregationType =
                    surface.Type("ColonyManagerRedux.Managers.ManagerJob_Production+LinkedDemandAggregation");

                PropertyInfo recipeProperty = surface.Property(jobType, "Recipe");
                recipeGetter = Getter(recipeProperty);
                recipeSetter = surface.Required("ManagerJob_Production.Recipe setter", Setter(recipeProperty));

                PropertyInfo modeProperty = surface.Property(jobType, "Mode");
                modeGetter = Getter(modeProperty);
                modeSetter = surface.Required("ManagerJob_Production.Mode setter", Setter(modeProperty));

                triggerThresholdGetter = Getter(surface.Property(jobType, "TriggerThreshold"));

                autoTargetFromLinksField = surface.Field(jobType, "AutoTargetFromLinks");
                autoRestrictIngredientsFromLinksField =
                    surface.Field(jobType, "AutoRestrictIngredientsFromLinks");
                demandAggregationField = surface.Field(jobType, "DemandAggregation");
                syncFilterAndAllowedField = surface.Field(jobType, "SyncFilterAndAllowed");
                consumeSurplusPerIngredientField = surface.Field(jobType, "ConsumeSurplusPerIngredient");
                maxAllowedSkillLevelField = surface.Field(jobType, "MaxAllowedSkillLevel");
                PropertyInfo qualityRangeProperty = surface.Property(jobType, "CountedQualityRange");
                countedQualityRangeGetter = Getter(qualityRangeProperty);
                countedQualityRangeSetter = surface.Required("ManagerJob_Production.CountedQualityRange setter",
                    Setter(qualityRangeProperty));
                anyHasQualityMethod = surface.Method(jobType, "AnyHasQuality", new[] { typeof(IEnumerable<ThingDef>) });
                assignmentModeField = surface.Field(jobType, "AssignmentMode");
                workbenchAreaField = surface.Field(jobType, "WorkbenchArea");
                invertWorkbenchAreaField = surface.Field(jobType, "InvertWorkbenchArea");
                allowedSkillRangeField = surface.Field(jobType, "AllowedSkillRange");
                ingredientSearchRadiusField = surface.Field(jobType, "IngredientSearchRadius");
                linkedProducersField = surface.Field(jobType, "LinkedProducers");
                allowedIngredientsField = surface.Field(jobType, "AllowedIngredients");
                specificWorkbenchesField = surface.Field(jobType, "SpecificWorkbenches");
                storeModeField = surface.Field(jobType, "StoreMode");
                storeGroupField = surface.Field(jobType, "StoreGroup");

                PropertyInfo bufferProperty = surface.Property(jobType, "LinkedDemandBufferCount");
                linkedDemandBufferCountGetter = Getter(bufferProperty);
                linkedDemandBufferCountSetter = surface.Required(
                    "ManagerJob_Production.LinkedDemandBufferCount setter", Setter(bufferProperty));

                allEligibleWorkTablesGetter = Getter(surface.Property(jobType, "AllEligibleWorkTables"));
                managedBillsGetter = Getter(surface.Property(jobType, "ManagedBills"));
                setIngredientAllowedMethod = surface.Method(jobType, "SetIngredientAllowed",
                    new[] { typeof(ThingDef), typeof(bool), typeof(bool) });
                optionalRecipeIngredientOptionsMethod =
                    surface.Method(jobType, "OptionalRecipeIngredientOptions", new[] { typeof(RecipeDef) });
                groupIngredientsByCategoryMethod = surface.Method(jobType, "GroupIngredientsByCategory",
                    new[] { typeof(IEnumerable<ThingDef>) });
                resolvedOutputDefsMethod =
                    surface.Method(jobType, "ResolvedOutputDefs", new[] { typeof(RecipeDef) });
                representativeThingDefMethod =
                    surface.Method(resolversType, "RepresentativeThingDef", new[] { typeof(RecipeDef) });

                targetLabelGetter = Getter(surface.Property(triggerType, "TargetLabel"));
                statusTooltipGetter = Getter(surface.Property(triggerType, "StatusTooltip"));
                currentCountMethod = surface.Method(triggerType, "GetCurrentCount", new[] { typeof(bool) });
                detailsWindowGetter = Getter(surface.Property(triggerType, "DetailsWindow"));

                availableRecipesField = surface.Field(tabType, "_availableRecipes");
                groupByWorkbenchField = surface.Field(tabType, "_groupByWorkbench");
                collapsedWorkbenchGroupsField = surface.Field(tabType, "_collapsedWorkbenchGroups");
                workbenchGroupsGetter = Getter(surface.Property(tabType, "WorkbenchGroups"));
                reservedStockOverridesField = surface.Field(jobType, "ReservedStockOverrides");
                reservedStockInputBuffersField = surface.Field(tabType, "_reservedStockInputBuffers");
                effectiveReservedStockMethod = surface.Method(jobType, "GetEffectiveReservedStock", new[] { typeof(ThingDef) });
                managerSettingsGetter = Getter(surface.Property(jobType, "ManagerSettings"));
                globalReservedStockField = surface.Field(
                    surface.Type("ColonyManagerRedux.Managers.ManagerSettings_Production"), "ReservedStock");
                if (jobType != null)
                {
                    relevantReservedIngredientsMethod =
                        surface.Method(tabType, "ComputeRelevantReservedIngredients", new[] { jobType });
                    addReservedStockOptionsMethod =
                        surface.Method(tabType, "BuildAddJobReservedStockOptions", new[] { jobType });
                }
                currentTabField = surface.Field(tabType, "_currentTab");
                tabListGetter = Getter(surface.Property(tabType, "TabList"));
                tabRecordTabGetter = Getter(surface.Property(tabRecordType, "Tab"));
                makeNewJobMethod = surface.Method(managerTabBaseType, "MakeNewJob", new[] { typeof(object[]) });
                if (managerJobBaseType != null)
                {
                    getSubLabelMethod = surface.Method(tabType, "GetSubLabel", new[] { managerJobBaseType });
                }
                if (jobType != null)
                {
                    computeRecipeSwapCandidatesMethod =
                        surface.Method(tabType, "ComputeRecipeSwapCandidates", new[] { jobType });
                    computeLinkedConsumerDemandsMethod =
                        surface.Method(tabType, "ComputeLinkedConsumerDemands", new[] { jobType });
                    buildStoreModeOptionsMethod = surface.Method(tabType, "BuildStoreModeOptions", new[] { jobType });
                    buildRecipeSwapOptionsMethod = surface.Method(tabType, "BuildRecipeSwapOptions",
                        new[] { jobType, typeof(List<RecipeDef>) });
                    buildIngredientLinkOptionsMethod = surface.Method(tabType, "BuildIngredientLinkOptions",
                        new[] { jobType, typeof(ThingDef) });
                }
                computeIngredientSourceCandidatesMethod =
                    surface.Method(tabType, "ComputeIngredientSourceCandidates", new[] { typeof(ThingDef) });

                modeValues = EnumValues(productionModeType);
                modeNames.AddRange(NamesOf(modeValues));
                assignmentModeValues = EnumValues(assignmentModeType);
                assignmentModeNames.AddRange(NamesOf(assignmentModeValues));
                demandAggregationValues = EnumValues(demandAggregationType);
                demandAggregationNames.AddRange(NamesOf(demandAggregationValues));
                productionModeMaintainStock = surface.EnumValue(productionModeType, "MaintainStock");
                productionModeConsumeSurplus = surface.EnumValue(productionModeType, "ConsumeSurplus");

                ready = surface.Ready;
            }

            /// <summary>True when every member the Production detail rows read or write resolved.</summary>
            public static bool Ready
            {
                get { return ready; }
            }

            /// <summary>Whether this tab is the mod's Production tab, by the type resolved above.</summary>
            public static bool HandlesTab(object tab)
            {
                return ready && tab != null && tabType != null && tabType.IsInstanceOfType(tab);
            }

            // ------------------------------------------------------------------
            // Available sub-tab and recipe selection.
            // ------------------------------------------------------------------

            public static bool GroupByWorkbench(object tab)
            {
                return FieldFlag(groupByWorkbenchField, tab, "GroupByWorkbench");
            }

            public static void SetGroupByWorkbench(object tab, bool value)
            {
                SetField(groupByWorkbenchField, tab, value, "SetGroupByWorkbench");
            }

            public static List<KeyValuePair<ThingDef, List<RecipeDef>>> WorkbenchGroups(object tab)
            {
                var result = new List<KeyValuePair<ThingDef, List<RecipeDef>>>();
                var groups = Get(workbenchGroupsGetter, tab, "WorkbenchGroups") as IEnumerable;
                if (groups == null)
                {
                    return result;
                }
                try
                {
                    foreach (object group in groups)
                    {
                        Type recordType = group.GetType();
                        result.Add(new KeyValuePair<ThingDef, List<RecipeDef>>(
                            recordType.GetProperty("Workbench").GetValue(group, null) as ThingDef,
                            recordType.GetProperty("Recipes").GetValue(group, null) as List<RecipeDef>));
                    }
                }
                catch (Exception ex)
                {
                    Fail("WorkbenchGroups", ex);
                }
                return result;
            }

            public static bool IsWorkbenchGroupCollapsed(object tab, ThingDef workbench)
            {
                var collapsed = FieldValue(collapsedWorkbenchGroupsField, tab, "IsWorkbenchGroupCollapsed") as HashSet<ThingDef>;
                return collapsed != null && collapsed.Contains(workbench);
            }

            public static void SetWorkbenchGroupCollapsed(object tab, ThingDef workbench, bool collapse)
            {
                var collapsed = FieldValue(collapsedWorkbenchGroupsField, tab, "SetWorkbenchGroupCollapsed") as HashSet<ThingDef>;
                if (collapsed == null)
                {
                    return;
                }
                // MUTATION-C: the header's closure edits this private set directly; no method wraps it.
                if (collapse)
                {
                    collapsed.Add(workbench);
                }
                else
                {
                    collapsed.Remove(workbench);
                }
            }

            public static List<ThingDef> ReservedIngredients(object job)
            {
                return Call(relevantReservedIngredientsMethod, null, new[] { job }, "ReservedIngredients") as List<ThingDef>
                    ?? new List<ThingDef>();
            }

            public static int EffectiveReservedStock(object job, ThingDef def)
            {
                return Call(effectiveReservedStockMethod, job, new object[] { def }, "EffectiveReservedStock") is int count
                    ? count
                    : 0;
            }

            public static bool HasReservedStockOverride(object job, ThingDef def)
            {
                var overrides = FieldValue(reservedStockOverridesField, job, "HasReservedStockOverride") as Dictionary<ThingDef, int>;
                return overrides != null && overrides.ContainsKey(def);
            }

            public static bool HasDefaultReservedStock(object job, ThingDef def)
            {
                object settings = Get(managerSettingsGetter, job, "HasDefaultReservedStock");
                var defaults = FieldValue(globalReservedStockField, settings, "HasDefaultReservedStock") as Dictionary<ThingDef, int>;
                return defaults != null && defaults.ContainsKey(def);
            }

            /// <summary>The tab re-parses its text buffer every frame, so the buffer entry goes too, as the delete icon does.</summary>
            public static void SetReservedStockOverride(object tab, object job, ThingDef def, int? count)
            {
                var overrides = FieldValue(reservedStockOverridesField, job, "SetReservedStockOverride") as Dictionary<ThingDef, int>;
                var buffers = FieldValue(reservedStockInputBuffersField, tab, "SetReservedStockOverride") as Dictionary<ThingDef, string>;
                if (overrides == null)
                {
                    return;
                }
                // MUTATION-C: the field's and delete icon's own dictionary writes; no method wraps them.
                if (count.HasValue)
                {
                    overrides[def] = count.Value;
                }
                else
                {
                    overrides.Remove(def);
                }
                if (buffers != null)
                {
                    buffers.Remove(def);
                }
            }

            public static void OpenAddReservedStockMenu(object job)
            {
                var options = Call(addReservedStockOptionsMethod, null, new[] { job }, "OpenAddReservedStockMenu")
                    as List<FloatMenuOption>;
                if (options != null && options.Count > 0)
                {
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            }

            /// <summary>Every recipe the Available list offers, already label-sorted by the tab's own Refresh.</summary>
            public static List<RecipeDef> AvailableRecipes(object tab)
            {
                var recipes = new List<RecipeDef>();
                if (!ready || tab == null || availableRecipesField == null)
                {
                    return recipes;
                }
                var items = FieldValue(availableRecipesField, tab, "AvailableRecipes") as IEnumerable<RecipeDef>;
                if (items == null)
                {
                    return recipes;
                }
                try
                {
                    recipes.AddRange(items);
                }
                catch (Exception ex)
                {
                    Fail("AvailableRecipes", ex);
                }
                return recipes;
            }

            /// <summary>
            /// The Available row's own click body: make a new job, set its recipe (the setter reseeds
            /// everything recipe-derived), then select it. Null on failure.
            /// </summary>
            public static object SelectRecipe(object tab, RecipeDef recipe)
            {
                if (!ready || tab == null || recipe == null || makeNewJobMethod == null
                    || recipeSetter == null)
                {
                    return null;
                }
                object job = Call(makeNewJobMethod, tab, new object[] { new object[0] }, "SelectRecipe");
                if (job == null)
                {
                    return null;
                }
                SetProperty(recipeSetter, job, recipe, "SelectRecipe");
                CmrCompat.SelectJob(tab, job);
                return job;
            }

            /// <summary>
            /// Puts the visible sub-tab where the keyboard cursor is: the write <c>TabDrawer.DrawTabs</c>
            /// performs on a sub-tab click. MUTATION-C: no setter exists, and the write is purely visual.
            /// </summary>
            public static void ShowSubTab(object tab, int index)
            {
                if (!ready || tab == null || currentTabField == null || tabListGetter == null
                    || tabRecordTabGetter == null)
                {
                    return;
                }
                var records = Get(tabListGetter, tab, "ShowSubTab") as IList;
                if (records == null || index < 0 || index >= records.Count)
                {
                    return;
                }
                object target = Get(tabRecordTabGetter, records[index], "ShowSubTab");
                if (target == null)
                {
                    return;
                }
                try
                {
                    if (!ReferenceEquals(currentTabField.GetValue(tab), target))
                    {
                        // MUTATION-C: the private store the sub-tab strip writes; see Livestock.ShowSubTab.
                        currentTabField.SetValue(tab, target);
                    }
                }
                catch (Exception ex)
                {
                    Fail("ShowSubTab", ex);
                }
            }

            public static RecipeDef RecipeOf(object job)
            {
                return Get(recipeGetter, job, "Recipe") as RecipeDef;
            }

            /// <summary>
            /// The icon-card def the recipe row hands Alt+I, mirroring the mod's own icon pick: the
            /// recipe's <c>UIIconThing</c> when set; else, only when <c>UIIcon</c> is also unset, the
            /// resolver's representative def; else null, the icon being a bare texture.
            /// </summary>
            public static ThingDef InfoCardThingFor(RecipeDef recipe)
            {
                if (recipe == null)
                {
                    return null;
                }
                if (recipe.UIIconThing != null)
                {
                    return recipe.UIIconThing;
                }
                if (recipe.UIIcon != null)
                {
                    return null;
                }
                return Call(representativeThingDefMethod, null, new object[] { recipe },
                    "InfoCardThingFor") as ThingDef;
            }

            // ------------------------------------------------------------------
            // Mode.
            // ------------------------------------------------------------------

            public static int Mode(object job)
            {
                return IndexOf(modeValues, Get(modeGetter, job, "Mode"));
            }

            /// <summary>The mode enum's own member names, in declaration order (index 0 = MaintainStock, 1 = ConsumeSurplus).</summary>
            public static List<string> ModeNames()
            {
                return new List<string>(modeNames);
            }

            /// <summary>The mode radio cell's own click body; the property setter restricts the trigger's ops and reconfigures its filter itself.</summary>
            public static void SetMode(object job, int index)
            {
                object value = ValueAt(modeValues, index);
                if (value == null)
                {
                    return;
                }
                SetProperty(modeSetter, job, value, "SetMode");
            }

            /// <summary>The authoritative mode tests, against the resolved enum values; callers never compare raw indices. Both false when the mode is unreadable, so a broken read grows no mode-gated control.</summary>
            public static bool IsMaintainStock(object job)
            {
                object current = Get(modeGetter, job, "Mode");
                return current != null && productionModeMaintainStock != null
                    && current.Equals(productionModeMaintainStock);
            }

            public static bool IsConsumeSurplus(object job)
            {
                object current = Get(modeGetter, job, "Mode");
                return current != null && productionModeConsumeSurplus != null
                    && current.Equals(productionModeConsumeSurplus);
            }

            // ------------------------------------------------------------------
            // Threshold.
            // ------------------------------------------------------------------

            /// <summary>
            /// Opens the mod's own threshold details window, exactly as clicking the ConsumeSurplus
            /// threshold label does. Unlike <see cref="Hunting.OpenThresholdDetails"/>, this tab
            /// passes no onOpenFilterDetails delegate, so there is no extra field write to reproduce.
            /// </summary>
            public static void OpenThresholdDetails(object job)
            {
                object trigger = Trigger(job);
                if (trigger == null || detailsWindowGetter == null)
                {
                    return;
                }
                if (Get(detailsWindowGetter, trigger, "DetailsWindow") is Window window)
                {
                    // Adding after the frame ends keeps the activating Enter out of the new window's
                    // own event stream.
                    LongEventHandler.ExecuteWhenFinished(() => Find.WindowStack.Add(window));
                }
            }

            public static string ThresholdStatus(object job)
            {
                return Get(statusTooltipGetter, Trigger(job), "StatusTooltip") as string ?? "";
            }

            /// <summary>The mod's own hover text for the threshold status row, composed here so the provider needs no trigger handle.</summary>
            public static string ThresholdCountTooltip(object job)
            {
                object trigger = Trigger(job);
                if (trigger == null)
                {
                    return "";
                }
                int current = CurrentCount(trigger);
                string targetLabel = Get(targetLabelGetter, trigger, "TargetLabel") as string ?? "";
                return TranslateMod("ColonyManagerRedux.Thresholds.ThresholdCountTooltip", current, targetLabel);
            }

            private static int CurrentCount(object trigger)
            {
                if (!ready || trigger == null || currentCountMethod == null)
                {
                    return 0;
                }
                object value = Call(currentCountMethod, trigger, new object[] { true }, "GetCurrentCount");
                return value is int count ? count : 0;
            }

            private static object Trigger(object job)
            {
                return Get(triggerThresholdGetter, job, "TriggerThreshold");
            }

            // ------------------------------------------------------------------
            // Linked jobs.
            // ------------------------------------------------------------------

            /// <summary>Every producer job this job links to, sorted by the tab's own sub-label; takes the tab because <see cref="GetSubLabel"/> is an instance method.</summary>
            public static List<object> LinkedProducers(object tab, object job)
            {
                var producers = new List<object>();
                if (!ready || job == null || linkedProducersField == null)
                {
                    return producers;
                }
                try
                {
                    var set = linkedProducersField.GetValue(job) as IEnumerable;
                    if (set != null)
                    {
                        foreach (object item in set)
                        {
                            if (item != null)
                            {
                                producers.Add(item);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Fail("LinkedProducers", ex);
                }
                if (tab != null)
                {
                    producers.Sort((a, b) => string.Compare(GetSubLabel(tab, a), GetSubLabel(tab, b),
                        StringComparison.OrdinalIgnoreCase));
                }
                return producers;
            }

            public static bool AutoTargetFromLinks(object job)
            {
                return FieldFlag(autoTargetFromLinksField, job, "AutoTargetFromLinks");
            }

            /// <summary>Writes the field the auto-target toggle passes by ref.</summary>
            public static void SetAutoTargetFromLinks(object job, bool value)
            {
                SetField(autoTargetFromLinksField, job, value, "SetAutoTargetFromLinks");
            }

            public static bool AutoRestrictIngredientsFromLinks(object job)
            {
                return FieldFlag(autoRestrictIngredientsFromLinksField, job, "AutoRestrictIngredientsFromLinks");
            }

            /// <summary>Writes the field the auto-restrict toggle passes by ref.</summary>
            public static void SetAutoRestrictIngredientsFromLinks(object job, bool value)
            {
                SetField(autoRestrictIngredientsFromLinksField, job, value, "SetAutoRestrictIngredientsFromLinks");
            }

            public static int DemandAggregation(object job)
            {
                return IndexOf(demandAggregationValues, FieldValue(demandAggregationField, job, "DemandAggregation"));
            }

            /// <summary>The aggregation enum's own member names, in declaration order (0 = Sum, 1 = MaxOfConsumers).</summary>
            public static List<string> DemandAggregationNames()
            {
                return new List<string>(demandAggregationNames);
            }

            /// <summary>MUTATION-C: writes the field the aggregation radio cell assigns directly (tab 810-827); a plain public field, no setter of its own.</summary>
            public static void SetDemandAggregation(object job, int index)
            {
                object value = ValueAt(demandAggregationValues, index);
                if (value == null)
                {
                    return;
                }
                SetField(demandAggregationField, job, value, "SetDemandAggregation");
            }

            public static int LinkedDemandBufferCount(object job)
            {
                object value = Get(linkedDemandBufferCountGetter, job, "LinkedDemandBufferCount");
                return value is int count ? count : 0;
            }

            /// <summary>The buffer slider's own property setter -- it Untouches every currently-linked producer itself.</summary>
            public static void SetLinkedDemandBufferCount(object job, int value)
            {
                SetProperty(linkedDemandBufferCountSetter, job, value, "SetLinkedDemandBufferCount");
            }

            /// <summary>The tab's own sub-label for a job: recipe target plus mode and count/threshold.</summary>
            public static string GetSubLabel(object tab, object job)
            {
                if (!ready || tab == null || job == null || getSubLabelMethod == null)
                {
                    return "";
                }
                return Call(getSubLabelMethod, tab, new object[] { job }, "GetSubLabel") as string ?? "";
            }

            /// <summary>The consumer's allowed ingredients, filtered to what the producer's recipe resolves to.</summary>
            public static List<ThingDef> ProducerCoveredIngredients(object job, object producer)
            {
                var covered = new List<ThingDef>();
                RecipeDef producerRecipe = RecipeOf(producer);
                if (producerRecipe == null)
                {
                    return covered;
                }
                List<ThingDef> outputs = ResolvedOutputDefsOf(producerRecipe);
                HashSet<ThingDef> allowed = AllowedIngredientsOf(job);
                if (allowed == null)
                {
                    return covered;
                }
                foreach (ThingDef def in allowed)
                {
                    if (outputs.Contains(def))
                    {
                        covered.Add(def);
                    }
                }
                return covered;
            }

            /// <summary>Every job currently linking to this producer, paired with what it covers and the demand it contributes (invokes the tab's own ComputeLinkedConsumerDemands).</summary>
            public static List<CmrLinkedConsumer> LinkedConsumerDemands(object tab, object job)
            {
                var result = new List<CmrLinkedConsumer>();
                if (!ready || tab == null || job == null || computeLinkedConsumerDemandsMethod == null)
                {
                    return result;
                }
                var items = Call(computeLinkedConsumerDemandsMethod, tab, new object[] { job },
                    "LinkedConsumerDemands") as IEnumerable;
                if (items == null)
                {
                    return result;
                }
                FieldInfo item1 = null;
                FieldInfo item2 = null;
                FieldInfo item3 = null;
                try
                {
                    foreach (object item in items)
                    {
                        if (item == null)
                        {
                            continue;
                        }
                        // The tuple's Consumer element type is internal to the mod, so the
                        // ValueTuple instantiation cannot be named here: unpack by field name,
                        // resolved once, bailing wholesale on a shape change rather than per entry.
                        if (item1 == null)
                        {
                            Type tupleType = item.GetType();
                            item1 = tupleType.GetField("Item1");
                            item2 = tupleType.GetField("Item2");
                            item3 = tupleType.GetField("Item3");
                            if (item1 == null || item2 == null || item3 == null)
                            {
                                Fail("LinkedConsumerDemands",
                                    new MissingFieldException(tupleType.FullName, "Item1..Item3"));
                                return result;
                            }
                        }
                        object consumer = item1.GetValue(item);
                        object coveredRaw = item2.GetValue(item);
                        object demandRaw = item3.GetValue(item);
                        var entry = new CmrLinkedConsumer();
                        entry.Consumer = consumer;
                        entry.Covered = new List<ThingDef>();
                        var coveredItems = coveredRaw as IEnumerable<ThingDef>;
                        if (coveredItems != null)
                        {
                            entry.Covered.AddRange(coveredItems);
                        }
                        entry.Demand = demandRaw is int demand ? demand : 0;
                        result.Add(entry);
                    }
                }
                catch (Exception ex)
                {
                    Fail("LinkedConsumerDemands", ex);
                }
                return result;
            }

            /// <summary>Whether the Linked Jobs section has anything to show at all.</summary>
            public static bool HasLinkedSection(object tab, object job)
            {
                if (!ready || job == null)
                {
                    return false;
                }
                if (LinkedProducers(tab, job).Count > 0)
                {
                    return true;
                }
                if (AutoTargetFromLinks(job))
                {
                    return true;
                }
                return LinkedConsumerDemands(tab, job).Count > 0;
            }

            // ------------------------------------------------------------------
            // Recipe swap.
            // ------------------------------------------------------------------

            public static bool HasRecipeSwapCandidates(object tab, object job)
            {
                if (!ready || tab == null || job == null || !IsMaintainStock(job))
                {
                    return false;
                }
                return ComputeRecipeSwapCandidates(tab, job).Count > 0;
            }

            private static List<RecipeDef> ComputeRecipeSwapCandidates(object tab, object job)
            {
                var result = new List<RecipeDef>();
                if (!ready || tab == null || job == null || computeRecipeSwapCandidatesMethod == null)
                {
                    return result;
                }
                var items = Call(computeRecipeSwapCandidatesMethod, tab, new object[] { job },
                    "ComputeRecipeSwapCandidates") as IEnumerable<RecipeDef>;
                if (items == null)
                {
                    return result;
                }
                try
                {
                    result.AddRange(items);
                }
                catch (Exception ex)
                {
                    Fail("ComputeRecipeSwapCandidates", ex);
                }
                return result;
            }

            /// <summary>MUTATION-C: reproduces the recipe-swap button's own inline click body (mirrors tab 438-447); no gated "open" method exists to invoke instead.</summary>
            public static void OpenRecipeSwapMenu(object tab, object job)
            {
                if (!ready || tab == null || job == null || buildRecipeSwapOptionsMethod == null)
                {
                    return;
                }
                List<RecipeDef> candidates = ComputeRecipeSwapCandidates(tab, job);
                if (candidates.Count == 0)
                {
                    return;
                }
                var options = Call(buildRecipeSwapOptionsMethod, null, new object[] { job, candidates },
                    "OpenRecipeSwapMenu") as List<FloatMenuOption>;
                if (options == null || options.Count == 0)
                {
                    return;
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }

            // ------------------------------------------------------------------
            // Store mode.
            // ------------------------------------------------------------------

            /// <summary>The store-mode button's own label text.</summary>
            public static string StoreModeLabel(object job)
            {
                var mode = FieldValue(storeModeField, job, "StoreMode") as BillStoreModeDef;
                if (mode == null)
                {
                    return "";
                }
                var group = FieldValue(storeGroupField, job, "StoreGroup") as ISlotGroup;
                string groupLabel = group != null ? SlotGroup.GetGroupLabel(group) : "";
                try
                {
                    return string.Format(CultureInfo.InvariantCulture, mode.LabelCap, groupLabel);
                }
                catch (Exception ex)
                {
                    Fail("StoreModeLabel", ex);
                    return mode.LabelCap;
                }
            }

            /// <summary>MUTATION-C: reproduces the store-mode button's own inline click body (mirrors tab 1249-1252); no gated "open" method exists to invoke instead.</summary>
            public static void OpenStoreModeMenu(object job)
            {
                if (!ready || job == null || buildStoreModeOptionsMethod == null)
                {
                    return;
                }
                var options = Call(buildStoreModeOptionsMethod, null, new object[] { job },
                    "OpenStoreModeMenu") as List<FloatMenuOption>;
                if (options == null || options.Count == 0)
                {
                    return;
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }

            // ------------------------------------------------------------------
            // Ingredient links (per-def producer coverage).
            // ------------------------------------------------------------------

            private static List<object> CoveringProducers(object tab, object job, ThingDef def)
            {
                var covering = new List<object>();
                if (def == null)
                {
                    return covering;
                }
                List<object> linked = LinkedProducers(tab, job);
                for (int i = 0; i < linked.Count; i++)
                {
                    RecipeDef producerRecipe = RecipeOf(linked[i]);
                    if (producerRecipe != null && ResolvedOutputDefsOf(producerRecipe).Contains(def))
                    {
                        covering.Add(linked[i]);
                    }
                }
                return covering;
            }

            /// <summary>0 = no producing recipe exists for this def; 1 = linkable but not linked; 2 = a linked producer already covers it. Mirrors DrawIngredientLinkIcon's own gate.</summary>
            public static int IngredientLinkState(object tab, object job, ThingDef def)
            {
                if (!ready || tab == null || job == null || def == null || !CmrCompat.JobIsManaged(job))
                {
                    return 0;
                }
                if (CoveringProducers(tab, job, def).Count > 0)
                {
                    return 2;
                }
                return ComputeIngredientSourceCandidates(tab, def).Count > 0 ? 1 : 0;
            }

            /// <summary>The covering producers' own sub-labels, comma-joined.</summary>
            public static string LinkedProducerSummary(object tab, object job, ThingDef def)
            {
                List<object> covering = CoveringProducers(tab, job, def);
                var labels = new List<string>();
                for (int i = 0; i < covering.Count; i++)
                {
                    string label = GetSubLabel(tab, covering[i]);
                    if (!string.IsNullOrEmpty(label))
                    {
                        labels.Add(label);
                    }
                }
                return string.Join(", ", labels.ToArray());
            }

            private static List<RecipeDef> ComputeIngredientSourceCandidates(object tab, ThingDef def)
            {
                var result = new List<RecipeDef>();
                if (!ready || tab == null || def == null || computeIngredientSourceCandidatesMethod == null)
                {
                    return result;
                }
                var items = Call(computeIngredientSourceCandidatesMethod, tab, new object[] { def },
                    "ComputeIngredientSourceCandidates") as IEnumerable<RecipeDef>;
                if (items == null)
                {
                    return result;
                }
                try
                {
                    result.AddRange(items);
                }
                catch (Exception ex)
                {
                    Fail("ComputeIngredientSourceCandidates", ex);
                }
                return result;
            }

            /// <summary>MUTATION-C: reproduces the ingredient-link icon's own inline click body (mirrors tab 1173-1176); no gated "open" method exists to invoke instead.</summary>
            public static void OpenIngredientLinkMenu(object tab, object job, ThingDef def)
            {
                if (!ready || tab == null || job == null || def == null || buildIngredientLinkOptionsMethod == null)
                {
                    return;
                }
                var options = Call(buildIngredientLinkOptionsMethod, tab, new object[] { job, def },
                    "OpenIngredientLinkMenu") as List<FloatMenuOption>;
                if (options == null || options.Count == 0)
                {
                    return;
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }

            // ------------------------------------------------------------------
            // Ingredients.
            // ------------------------------------------------------------------

            public static bool SyncFilterAndAllowed(object job)
            {
                return FieldFlag(syncFilterAndAllowedField, job, "SyncFilterAndAllowed");
            }

            /// <summary>Writes the field the ConsumeSurplus-only sync toggle passes by ref.</summary>
            public static void SetSyncFilterAndAllowed(object job, bool value)
            {
                SetField(syncFilterAndAllowedField, job, value, "SetSyncFilterAndAllowed");
            }

            public static bool ConsumeSurplusPerIngredient(object job)
            {
                return FieldFlag(consumeSurplusPerIngredientField, job, "ConsumeSurplusPerIngredient");
            }

            /// <summary>Writes the field the ConsumeSurplus-only "Check each ingredient" toggle passes by ref.</summary>
            public static void SetConsumeSurplusPerIngredient(object job, bool value)
            {
                SetField(consumeSurplusPerIngredientField, job, value, "SetConsumeSurplusPerIngredient");
            }

            /// <summary>Whether the tab draws the quality range: some def the threshold counts has a quality (ManagerTab_Production.DrawThresholdReadOnly).</summary>
            public static bool ShowsQualityRange(object job)
            {
                ThingFilter filter = Threshold.ThresholdFilter(JobTrigger(job));
                if (!ready || filter == null)
                {
                    return false;
                }
                return Call(anyHasQualityMethod, null, new object[] { filter.AllowedThingDefs },
                    "ShowsQualityRange") is bool any && any;
            }

            public static QualityRange CountedQualityRange(object job)
            {
                return Get(countedQualityRangeGetter, job, "CountedQualityRange") is QualityRange range
                    ? range
                    : QualityRange.All;
            }

            public static void SetCountedQualityRange(object job, QualityRange value)
            {
                SetProperty(countedQualityRangeSetter, job, value, "SetCountedQualityRange");
            }

            /// <summary>The ingredients a player can choose for the recipe, the list the tab's shortcuts and checkboxes draw (ManagerTab_Production.DrawIngredientList).</summary>
            public static List<ThingDef> OptionalIngredientOptions(RecipeDef recipe)
            {
                var result = new List<ThingDef>();
                if (!ready || recipe == null || optionalRecipeIngredientOptionsMethod == null)
                {
                    return result;
                }
                var items = Call(optionalRecipeIngredientOptionsMethod, null, new object[] { recipe },
                    "OptionalIngredientOptions") as IEnumerable<ThingDef>;
                if (items == null)
                {
                    return result;
                }
                try
                {
                    result.AddRange(items);
                }
                catch (Exception ex)
                {
                    Fail("OptionalIngredientOptions", ex);
                }
                return result;
            }

            public static bool IngredientAllowed(object job, ThingDef def)
            {
                HashSet<ThingDef> allowed = AllowedIngredientsOf(job);
                return allowed != null && def != null && allowed.Contains(def);
            }

            /// <summary>The exact call every ingredient checkbox and shortcut hands to the mod's own method.</summary>
            public static void SetIngredientAllowed(object job, ThingDef def, bool allow)
            {
                if (job == null || def == null || setIngredientAllowedMethod == null)
                {
                    return;
                }
                Call(setIngredientAllowedMethod, job, new object[] { def, allow, true }, "SetIngredientAllowed");
            }

            private static HashSet<ThingDef> AllowedIngredientsOf(object job)
            {
                if (!ready || job == null || allowedIngredientsField == null)
                {
                    return null;
                }
                try
                {
                    return allowedIngredientsField.GetValue(job) as HashSet<ThingDef>;
                }
                catch (Exception ex)
                {
                    Fail("AllowedIngredientsOf", ex);
                    return null;
                }
            }

            /// <summary>The ingredient shortcut rows' groups, the mod's own grouping ordered as the tab orders them (ManagerTab_Production.DrawIngredientShortcuts).</summary>
            public static List<KeyValuePair<ThingCategoryDef, List<ThingDef>>> IngredientCategories(
                List<ThingDef> ingredients)
            {
                var result = new List<KeyValuePair<ThingCategoryDef, List<ThingDef>>>();
                if (!ready || ingredients == null)
                {
                    return result;
                }
                var groups = Call(groupIngredientsByCategoryMethod, null, new object[] { ingredients },
                    "IngredientCategories") as IEnumerable<IGrouping<ThingCategoryDef, ThingDef>>;
                if (groups == null)
                {
                    return result;
                }
                foreach (IGrouping<ThingCategoryDef, ThingDef> group in groups
                    .OrderBy(g => g.Key.LabelCap.Resolve(), StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(new KeyValuePair<ThingCategoryDef, List<ThingDef>>(group.Key, group.ToList()));
                }
                return result;
            }

            private static List<ThingDef> ResolvedOutputDefsOf(RecipeDef recipe)
            {
                var result = new List<ThingDef>();
                if (!ready || recipe == null || resolvedOutputDefsMethod == null)
                {
                    return result;
                }
                var items = Call(resolvedOutputDefsMethod, null, new object[] { recipe },
                    "ResolvedOutputDefs") as IEnumerable<ThingDef>;
                if (items == null)
                {
                    return result;
                }
                try
                {
                    result.AddRange(items);
                }
                catch (Exception ex)
                {
                    Fail("ResolvedOutputDefs", ex);
                }
                return result;
            }

            // ------------------------------------------------------------------
            // Workbench scope.
            // ------------------------------------------------------------------

            public static int AssignmentMode(object job)
            {
                return IndexOf(assignmentModeValues, FieldValue(assignmentModeField, job, "AssignmentMode"));
            }

            /// <summary>The scope enum's own member names, in declaration order (0 = All, 1 = Area, 2 = Specific).</summary>
            public static List<string> AssignmentModeNames()
            {
                return new List<string>(assignmentModeNames);
            }

            /// <summary>MUTATION-C: writes the field the scope radio cell assigns directly (tab 1550-1557); a plain public field, no setter of its own.</summary>
            public static void SetAssignmentMode(object job, int index)
            {
                object value = ValueAt(assignmentModeValues, index);
                if (value == null)
                {
                    return;
                }
                SetField(assignmentModeField, job, value, "SetAssignmentMode");
            }

            public static Area WorkbenchArea(object job)
            {
                return FieldValue(workbenchAreaField, job, "WorkbenchArea") as Area;
            }

            /// <summary>Writes the field the workbench-area strip itself writes by ref.</summary>
            public static void SetWorkbenchArea(object job, Area area)
            {
                SetField(workbenchAreaField, job, area, "SetWorkbenchArea");
            }

            public static bool InvertWorkbenchArea(object job)
            {
                return FieldFlag(invertWorkbenchAreaField, job, "InvertWorkbenchArea");
            }

            /// <summary>Writes the field the invert-area toggle passes by ref.</summary>
            public static void SetInvertWorkbenchArea(object job, bool value)
            {
                SetField(invertWorkbenchAreaField, job, value, "SetInvertWorkbenchArea");
            }

            public static List<Thing> EligibleWorkTables(object job)
            {
                var tables = new List<Thing>();
                if (!ready || job == null || allEligibleWorkTablesGetter == null)
                {
                    return tables;
                }
                var items = Get(allEligibleWorkTablesGetter, job, "EligibleWorkTables")
                    as IEnumerable<Building_WorkTable>;
                if (items == null)
                {
                    return tables;
                }
                try
                {
                    foreach (Building_WorkTable table in items)
                    {
                        tables.Add(table);
                    }
                }
                catch (Exception ex)
                {
                    Fail("EligibleWorkTables", ex);
                }
                return tables;
            }

            public static bool SpecificContains(object job, Thing thing)
            {
                HashSet<Building_WorkTable> set = SpecificWorkbenchesOf(job);
                Building_WorkTable table = thing as Building_WorkTable;
                return set != null && table != null && set.Contains(table);
            }

            /// <summary>MUTATION-C: the exact Add/Remove the specific-workbench checkbox passes as its own toggle delegates (tab 1595-1596); a plain public field, no gated method of its own.</summary>
            public static void SetSpecific(object job, Thing thing, bool allow)
            {
                HashSet<Building_WorkTable> set = SpecificWorkbenchesOf(job);
                Building_WorkTable table = thing as Building_WorkTable;
                if (set == null || table == null)
                {
                    return;
                }
                try
                {
                    if (allow)
                    {
                        set.Add(table);
                    }
                    else
                    {
                        set.Remove(table);
                    }
                }
                catch (Exception ex)
                {
                    Fail("SetSpecific", ex);
                }
            }

            private static HashSet<Building_WorkTable> SpecificWorkbenchesOf(object job)
            {
                if (!ready || job == null || specificWorkbenchesField == null)
                {
                    return null;
                }
                try
                {
                    return specificWorkbenchesField.GetValue(job) as HashSet<Building_WorkTable>;
                }
                catch (Exception ex)
                {
                    Fail("SpecificWorkbenchesOf", ex);
                    return null;
                }
            }

            // ------------------------------------------------------------------
            // Job settings.
            // ------------------------------------------------------------------

            /// <summary>Gates the skill rows, mirroring the tab's own check.</summary>
            public static bool HasWorkSkill(object job)
            {
                RecipeDef recipe = RecipeOf(job);
                return recipe != null && recipe.workSkill != null;
            }

            public static string WorkSkillLabel(object job)
            {
                RecipeDef recipe = RecipeOf(job);
                return recipe != null && recipe.workSkill != null ? recipe.workSkill.LabelCap.ToString() : "";
            }

            public static int SkillMin(object job)
            {
                return AllowedSkillRangeOf(job).min;
            }

            /// <summary>The skill range slider's live upper bound, which integrations such as EndlessGrowth raise.</summary>
            public static int MaxAllowedSkillLevel()
            {
                const int VanillaMax = 20;
                if (!ready || maxAllowedSkillLevelField == null)
                {
                    return VanillaMax;
                }
                try
                {
                    var max = maxAllowedSkillLevelField.GetValue(null) as Func<int>;
                    return max != null ? max() : VanillaMax;
                }
                catch (Exception ex)
                {
                    Fail("MaxAllowedSkillLevel", ex);
                    return VanillaMax;
                }
            }

            public static int SkillMax(object job)
            {
                return AllowedSkillRangeOf(job).max;
            }

            /// <summary>MUTATION-C: writes the field the skill-range grid's own ref-write targets (tab 1209); callers pass already-clamped values.</summary>
            public static void SetSkillRange(object job, int min, int max)
            {
                SetField(allowedSkillRangeField, job, new IntRange(min, max), "SetSkillRange");
            }

            private static IntRange AllowedSkillRangeOf(object job)
            {
                if (!ready || job == null || allowedSkillRangeField == null)
                {
                    return default(IntRange);
                }
                try
                {
                    return (IntRange)allowedSkillRangeField.GetValue(job);
                }
                catch (Exception ex)
                {
                    Fail("AllowedSkillRangeOf", ex);
                    return default(IntRange);
                }
            }

            public static float IngredientRadius(object job)
            {
                return FieldFloat(ingredientSearchRadiusField, job, "IngredientRadius");
            }

            /// <summary>Writes the field the radius slider's result is assigned to.</summary>
            public static void SetIngredientRadius(object job, float value)
            {
                SetField(ingredientSearchRadiusField, job, value, "SetIngredientRadius");
            }

            // ------------------------------------------------------------------
            // Status.
            // ------------------------------------------------------------------

            public static List<Bill_Production> ManagedBillsOf(object job)
            {
                var bills = new List<Bill_Production>();
                if (!ready || job == null || managedBillsGetter == null)
                {
                    return bills;
                }
                var items = Get(managedBillsGetter, job, "ManagedBillsOf") as IEnumerable<Bill_Production>;
                if (items == null)
                {
                    return bills;
                }
                try
                {
                    bills.AddRange(items);
                }
                catch (Exception ex)
                {
                    Fail("ManagedBillsOf", ex);
                }
                return bills;
            }

            // ------------------------------------------------------------------
            // Plumbing, gated on this block's own Ready. Getter/Setter/Fail are the enclosing
            // surface's helpers.
            // ------------------------------------------------------------------

            private static string TranslateMod(string key, params NamedArgument[] args)
            {
                return TranslatorFormattedStringExtensions.Translate(key, args).Resolve();
            }

            private static object[] EnumValues(Type enumType)
            {
                if (enumType == null || !enumType.IsEnum)
                {
                    return new object[0];
                }
                Array values = Enum.GetValues(enumType);
                var boxed = new object[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    boxed[i] = values.GetValue(i);
                }
                return boxed;
            }

            private static List<string> NamesOf(object[] values)
            {
                var names = new List<string>();
                if (values == null)
                {
                    return names;
                }
                for (int i = 0; i < values.Length; i++)
                {
                    names.Add(values[i] == null ? "" : values[i].ToString());
                }
                return names;
            }

            private static int IndexOf(object[] values, object value)
            {
                if (values == null || value == null)
                {
                    return -1;
                }
                for (int i = 0; i < values.Length; i++)
                {
                    if (Equals(values[i], value))
                    {
                        return i;
                    }
                }
                return -1;
            }

            private static object ValueAt(object[] values, int index)
            {
                return values != null && index >= 0 && index < values.Length ? values[index] : null;
            }

            private static object Get(MethodInfo getter, object instance, string member)
            {
                if (!ready || getter == null || (instance == null && !getter.IsStatic))
                {
                    return null;
                }
                try
                {
                    return getter.Invoke(instance, null);
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                    return null;
                }
            }

            private static bool Flag(MethodInfo getter, object instance, string member)
            {
                return Get(getter, instance, member) is bool value && value;
            }

            private static object FieldValue(FieldInfo field, object instance, string member)
            {
                if (!ready || field == null || instance == null)
                {
                    return null;
                }
                try
                {
                    return field.GetValue(instance);
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                    return null;
                }
            }

            private static bool FieldFlag(FieldInfo field, object instance, string member)
            {
                return FieldValue(field, instance, member) is bool value && value;
            }

            private static float FieldFloat(FieldInfo field, object instance, string member)
            {
                return FieldValue(field, instance, member) is float value ? value : 0f;
            }

            private static object Call(MethodInfo method, object instance, object[] args, string member)
            {
                if (!ready || method == null || (instance == null && !method.IsStatic))
                {
                    return null;
                }
                try
                {
                    return method.Invoke(instance, args);
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                    return null;
                }
            }

            private static void SetProperty(MethodInfo setter, object instance, object value, string member)
            {
                if (!ready || setter == null || instance == null)
                {
                    return;
                }
                try
                {
                    setter.Invoke(instance, new[] { value });
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                }
            }

            /// <summary>
            /// Shared write primitive for the plain fields the mod's own <c>ref</c>-parameter widgets
            /// and option bodies write. Each public wrapper above cites the draw call it reproduces;
            /// this helper adds no path of its own.
            /// </summary>
            private static void SetField(FieldInfo field, object instance, object value, string member)
            {
                if (!ready || field == null || instance == null)
                {
                    return;
                }
                try
                {
                    // MUTATION-C: the write the mod's own widget performs through its ref parameter or
                    // its own toggle-cell/option body -- see this class's remarks and each wrapper's
                    // cited draw call. None of these fields has a gated setter to invoke instead.
                    field.SetValue(instance, value);
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                }
            }
        }
    }
}
