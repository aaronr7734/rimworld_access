using System;
using System.Collections.Generic;
using System.Globalization;
using RimWorld;
using UnityEngine;
using Verse;
using static RimWorldAccess.Shell.CompatText;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Production tab's detail rows: the sub-tab pair as Available (the recipe browser, present
    /// with no job selected) plus Options and Ingredients (the mod's two columns, each a run of rows
    /// naming the mod's own section headings) once a job is selected.
    ///
    /// Regions, in the mod's own visual order (<c>ManagerTab_Production.DoMainContent</c>): Available
    /// (one row per offered recipe), Options (Recipe, Mode, Threshold, the conditional LinkedJobs
    /// section, WorkbenchScope, JobSettings, then the heading-less Status run), Ingredients (the
    /// sync toggle, the shortcut rows, then one row per ingredient with its supply-link row
    /// immediately after when one applies).
    ///
    /// No search row: the mod's own Available list carries a mouse-only <c>QuickSearchWidget</c>; the
    /// shell's typeahead over the full row list is the keyboard search, so it is not modeled here.
    ///
    /// Every Options/Ingredients row settles the visual pane to sub-tab 1 (Current) as the cursor
    /// crosses it, and every Available row settles it back to sub-tab 0.
    /// </summary>
    internal sealed class CmrProductionDetails : ICmrJobDetailsProvider
    {
        private const int MaxLinkedIngredientNamesShown = 3;

        public bool Handles(object tab)
        {
            return CmrCompat.Production.HandlesTab(tab);
        }

        public List<CmrDetailRegion> Build(object tab, object job)
        {
            var regions = new List<CmrDetailRegion>();
            if (tab == null || !CmrCompat.Production.Ready)
            {
                return regions;
            }
            regions.Add(BuildAvailable(tab));
            if (job == null)
            {
                return regions;
            }
            CmrDetailRegion options = BuildOptions(tab, job);
            CmrDetailRegion ingredients = BuildIngredients(tab, job);
            SettleToCurrent(options, tab);
            SettleToCurrent(ingredients, tab);
            regions.Add(options);
            regions.Add(ingredients);
            return regions;
        }

        /// <summary>Overwrites every row's OnSettle wholesale; a row needing its own settle action must be built in a region outside this pass (the Available rows are).</summary>
        private static void SettleToCurrent(CmrDetailRegion region, object tab)
        {
            for (int i = 0; i < region.Rows.Count; i++)
            {
                region.Rows[i].OnSettle = () => CmrCompat.Production.ShowSubTab(tab, 1);
            }
        }

        // ------------------------------------------------------------------
        // Available: the recipe browser, present with no job selected.
        // ------------------------------------------------------------------

        private static CmrDetailRegion BuildAvailable(object tab)
        {
            var region = new CmrDetailRegion(ModText("ColonyManagerRedux.Thresholds.Available"));
            CmrDetailRow groupToggle = CmrTabRows.Toggle(null, "ColonyManagerRedux.Production.GroupByWorkbench",
                () => CmrCompat.Production.GroupByWorkbench(tab),
                value => CmrCompat.Production.SetGroupByWorkbench(tab, value));
            groupToggle.OnSettle = () => CmrCompat.Production.ShowSubTab(tab, 0);
            region.Rows.Add(groupToggle);

            if (!CmrCompat.Production.GroupByWorkbench(tab))
            {
                foreach (RecipeDef recipe in CmrCompat.Production.AvailableRecipes(tab))
                {
                    region.Rows.Add(RecipeRow(tab, recipe));
                }
                return region;
            }
            foreach (KeyValuePair<ThingDef, List<RecipeDef>> group in CmrCompat.Production.WorkbenchGroups(tab))
            {
                ThingDef workbench = group.Key;
                Func<bool> expanded = () => !CmrCompat.Production.IsWorkbenchGroupCollapsed(tab, workbench);
                region.Rows.Add(new CmrDetailRow
                {
                    Label = workbench.LabelCap + " (" + group.Value.Count + ")",
                    Role = ElementRole.TreeItem,
                    Expanded = expanded,
                    OnSettle = () => CmrCompat.Production.ShowSubTab(tab, 0),
                    Activate = () => CmrCompat.Production.SetWorkbenchGroupCollapsed(tab, workbench, expanded()),
                    CanAdjust = direction => expanded() == (direction < 0),
                    Adjust = direction => CmrCompat.Production.SetWorkbenchGroupCollapsed(tab, workbench, direction < 0),
                    InfoCardDef = workbench,
                });
                if (expanded())
                {
                    foreach (RecipeDef recipe in group.Value)
                    {
                        region.Rows.Add(RecipeRow(tab, recipe));
                    }
                }
            }
            return region;
        }

        private static CmrDetailRow RecipeRow(object tab, RecipeDef recipe)
        {
            return new CmrDetailRow
            {
                Label = recipe.LabelCap,
                Value = () => Workstations(recipe),
                Tooltip = () => Flatten(recipe.description),
                Role = ElementRole.Button,
                OnSettle = () => CmrCompat.Production.ShowSubTab(tab, 0),
                Activate = () => CmrCompat.Production.SelectRecipe(tab, recipe),
                Confirmation = "RimWorldAccess.Cmr.Production.JobOpened".Translate(recipe.LabelCap),
            };
        }

        private static string Workstations(RecipeDef recipe)
        {
            var names = new List<string>();
            foreach (ThingDef user in recipe.AllRecipeUsers)
            {
                names.Add(user.LabelCap.ToString());
            }
            return string.Join(", ", names);
        }

        // ------------------------------------------------------------------
        // Options: the left column's sections.
        // ------------------------------------------------------------------

        private static CmrDetailRegion BuildOptions(object tab, object job)
        {
            var region = new CmrDetailRegion("RimWorldAccess.Cmr.OptionsRegion".Translate());
            AddRecipe(region, tab, job);
            AddMode(region, job);
            AddThreshold(region, job);
            AddLinkedJobs(region, tab, job);
            AddWorkbenchScope(region, job);
            AddJobSettings(region, job);
            AddReservedStock(region, tab, job);
            AddStatus(region, job);
            return region;
        }

        /// <summary>The recipe's own info, work amount and ingredient requirements, plus the recipe-swap button when the job's output has alternatives (tab 348-457).</summary>
        private static void AddRecipe(CmrDetailRegion region, object tab, object job)
        {
            string section = ModText("ColonyManagerRedux.Production.Recipe");
            RecipeDef recipe = CmrCompat.Production.RecipeOf(job);
            if (recipe == null)
            {
                return;
            }

            region.Rows.Add(new CmrDetailRow
            {
                Label = recipe.LabelCap,
                Tooltip = () => Flatten(recipe.description),
                Role = ElementRole.None,
                SectionTitle = section,
                InfoCardDef = CmrCompat.Production.InfoCardThingFor(recipe),
            });

            region.Rows.Add(new CmrDetailRow
            {
                Label = "WorkAmount".Translate() + ": " + recipe.WorkAmountTotal(null).ToStringWorkAmount(),
                Role = ElementRole.None,
                SectionTitle = section,
            });

            region.Rows.Add(new CmrDetailRow
            {
                Label = "BillRequires".Translate(),
                Value = () => RequirementsValue(recipe),
                Role = ElementRole.None,
                SectionTitle = section,
            });

            if (CmrCompat.Production.HasRecipeSwapCandidates(tab, job))
            {
                region.Rows.Add(new CmrDetailRow
                {
                    Label = ModText("ColonyManagerRedux.Production.OtherRecipesAvailable"),
                    Tooltip = ModTip("ColonyManagerRedux.Production.OtherRecipesAvailable.Tip"),
                    Role = ElementRole.Button,
                    SectionTitle = section,
                    Activate = () => CmrCompat.Production.OpenRecipeSwapMenu(tab, job),
                    OpensWindow = true,
                    CapturedTwin = ModText("ColonyManagerRedux.Production.OtherRecipesAvailable"),
                });
            }
        }

        /// <summary>One clause per ingredient slot with a non-empty filter summary, joined the way the mod's own text block lists them (tab 406-421).</summary>
        private static string RequirementsValue(RecipeDef recipe)
        {
            var clauses = new List<string>();
            foreach (IngredientCount ingredientCount in recipe.ingredients)
            {
                if (ingredientCount.filter.Summary.NullOrEmpty())
                {
                    continue;
                }
                clauses.Add(recipe.IngredientValueGetter.BillRequirementsDescription(recipe, ingredientCount));
            }
            return string.Join(". ", clauses);
        }

        /// <summary>One radio row per <c>ProductionMode</c> value, in the enum's own declaration order (tab 519-544).</summary>
        private static void AddMode(CmrDetailRegion region, object job)
        {
            string section = ModText("ColonyManagerRedux.Production.Mode");
            List<string> names = CmrCompat.Production.ModeNames();
            for (int i = 0; i < names.Count; i++)
            {
                int index = i;
                string key = "ColonyManagerRedux.Production.Mode." + names[i];
                region.Rows.Add(new CmrDetailRow
                {
                    Label = ModText(key),
                    Tooltip = ModTip(key + ".Tip"),
                    Role = ElementRole.RadioButton,
                    SectionTitle = section,
                    Selected = () => CmrCompat.Production.Mode(job) == index,
                    Activate = () => CmrCompat.Production.SetMode(job, index),
                });
            }
        }

        /// <summary>
        /// The threshold row set both modes share (tab 546-665): the status/cog row, the target row
        /// (a plain readout while <c>AutoTargetFromLinks</c> is computing it for MaintainStock,
        /// otherwise editable), the ConsumeSurplus-only AllowAnyThreshold toggle, and CountAllOnMap.
        /// </summary>
        private static void AddThreshold(CmrDetailRegion region, object job)
        {
            string section = ModText("ColonyManagerRedux.Threshold");
            bool maintainStock = CmrCompat.Production.IsMaintainStock(job);
            bool consumeSurplus = CmrCompat.Production.IsConsumeSurplus(job);

            var statusRow = new CmrDetailRow
            {
                Label = Flatten(CmrCompat.Production.ThresholdStatus(job)),
                // In MaintainStock the mod deliberately ships no threshold-details window
                // (tab 562-576) yet reuses the same tooltip whose leading paragraph says to
                // click for one; a row with no Activate must not speak that instruction, so
                // only the Target/Current tail (after the key's own blank line) remains.
                Tooltip = consumeSurplus
                    ? (Func<string>)(() => Flatten(CmrCompat.Production.ThresholdCountTooltip(job)))
                    : () => Flatten(CompatText.AfterFirstParagraph(
                        CmrCompat.Production.ThresholdCountTooltip(job))),
                Role = consumeSurplus ? ElementRole.Button : ElementRole.None,
                SectionTitle = section,
            };
            if (consumeSurplus)
            {
                statusRow.Activate = () => CmrCompat.Production.OpenThresholdDetails(job);
                statusRow.OpensWindow = true;
            }
            region.Rows.Add(statusRow);

            if (maintainStock && CmrCompat.Production.AutoTargetFromLinks(job))
            {
                region.Rows.Add(new CmrDetailRow
                {
                    Label = ModArgs("ColonyManagerRedux.Production.AutoTargetFromLinks.ComputedTarget",
                        CmrCompat.Threshold.JobTargetCount(job)),
                    Tooltip = ModTip("ColonyManagerRedux.Production.AutoTargetFromLinks.ComputedTarget.Tip"),
                    Role = ElementRole.None,
                    SectionTitle = section,
                });
            }
            else
            {
                region.Rows.Add(new CmrDetailRow
                {
                    Label = "RimWorldAccess.Cmr.Threshold.TargetCountRow".Translate(),
                    Role = ElementRole.Stepper,
                    SectionTitle = section,
                    Value = () => CmrCompat.Threshold.JobTargetCount(job).ToString(CultureInfo.InvariantCulture),
                    CanAdjust = direction => CanStepTargetCount(job, direction),
                    Adjust = direction => StepTargetCount(job, direction),
                    // No upper bound on typed entry: the mod's own field accepts any count and
                    // raises MaxUpperThreshold to match (tab 637-644), which SetTargetCount
                    // reproduces -- clamping to the CURRENT max would reject what the field takes.
                    Numeric = new CmrNumericSpec
                    {
                        Min = 0,
                        Max = int.MaxValue,
                        Current = () => CmrCompat.Threshold.JobTargetCount(job),
                        Apply = value => CmrCompat.Threshold.SetJobTargetCount(job, value),
                    },
                });
            }

            if (consumeSurplus)
            {
                region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Threshold.AllowAnyThreshold",
                    () => CmrCompat.Threshold.JobAllowAnyThreshold(job),
                    value => CmrCompat.Threshold.SetJobAllowAnyThreshold(job, value)));
            }

            region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Threshold.CountAllOnMap",
                () => CmrCompat.Threshold.JobCountAllOnMap(job),
                value => CmrCompat.Threshold.SetJobCountAllOnMap(job, value)));

            if (maintainStock && CmrCompat.Production.ShowsQualityRange(job))
            {
                region.Rows.Add(new CmrDetailRow
                {
                    Label = "Quality".Translate(),
                    Role = ElementRole.Slider,
                    SectionTitle = section,
                    Value = () => QualityRangeValue(CmrCompat.Production.CountedQualityRange(job)),
                    Tooltip = ModTip("ColonyManagerRedux.Production.QualityRange.Tip"),
                    Activate = () => RangeEditMenuState.OpenQualityRange(
                        () => CmrCompat.Production.CountedQualityRange(job),
                        value => CmrCompat.Production.SetCountedQualityRange(job, value)),
                    OpensWindow = true,
                });
            }
        }

        private static string QualityRangeValue(QualityRange range)
        {
            return "RimWorldAccess.Shell.FilterTree.RangeValue".Translate(range.min.GetLabel(), range.max.GetLabel());
        }

        private static bool CanStepTargetCount(object job, int direction)
        {
            int count = CmrCompat.Threshold.JobTargetCount(job);
            return direction < 0 ? count > 0 : count < CmrCompat.Threshold.JobMaxUpperThreshold(job);
        }

        /// <summary>One keyboard step along the same 0..MaxUpperThreshold range the mod's slider spans.</summary>
        private static void StepTargetCount(object job, int direction)
        {
            int max = CmrCompat.Threshold.JobMaxUpperThreshold(job);
            float stepped = SliderStep.Stepped(CmrCompat.Threshold.JobTargetCount(job), direction, 0f, max, -1f);
            CmrCompat.Threshold.SetJobTargetCount(job, Mathf.RoundToInt(stepped));
        }

        /// <summary>
        /// The whole section, only while the mod would draw it at all (tab 267-281): the consumer-side
        /// buffer row and one row per linked producer, then the producer-side toggles/aggregation
        /// choice and one row per linked consumer, in the mod's own draw order (tab 690-843).
        /// </summary>
        private static void AddLinkedJobs(CmrDetailRegion region, object tab, object job)
        {
            if (!CmrCompat.Production.HasLinkedSection(tab, job))
            {
                return;
            }
            string section = ModText("ColonyManagerRedux.Production.LinkedJobs");
            bool maintainStock = CmrCompat.Production.IsMaintainStock(job);

            List<object> producers = CmrCompat.Production.LinkedProducers(tab, job);
            if (maintainStock && producers.Count > 0)
            {
                region.Rows.Add(BufferCountRow(section, job));
            }
            for (int i = 0; i < producers.Count; i++)
            {
                object producer = producers[i];
                List<ThingDef> covered = CmrCompat.Production.ProducerCoveredIngredients(job, producer);
                region.Rows.Add(LinkedJobRow(section, tab, producer, covered,
                    "ColonyManagerRedux.Production.LinkedRow.Supplies"));
            }

            List<CmrLinkedConsumer> consumers = CmrCompat.Production.LinkedConsumerDemands(tab, job);

            // The mod returns before drawing the consumer-side controls when there are no consumer
            // demands and auto-targeting is off (tab 785-788, kept reachable so a job whose linked
            // consumers were all deleted can still switch the toggle back off).
            if (consumers.Count > 0 || CmrCompat.Production.AutoTargetFromLinks(job))
            {
                region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Production.AutoTargetFromLinks",
                    () => CmrCompat.Production.AutoTargetFromLinks(job),
                    value => CmrCompat.Production.SetAutoTargetFromLinks(job, value)));
                region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Production.AutoRestrictIngredientsFromLinks",
                    () => CmrCompat.Production.AutoRestrictIngredientsFromLinks(job),
                    value => CmrCompat.Production.SetAutoRestrictIngredientsFromLinks(job, value)));

                if (CmrCompat.Production.AutoTargetFromLinks(job))
                {
                    List<string> names = CmrCompat.Production.DemandAggregationNames();
                    for (int i = 0; i < names.Count; i++)
                    {
                        int index = i;
                        string key = "ColonyManagerRedux.Production.DemandAggregation." + names[i];
                        region.Rows.Add(new CmrDetailRow
                        {
                            Label = ModText(key),
                            Tooltip = ModTip(key + ".Tip"),
                            Role = ElementRole.RadioButton,
                            SectionTitle = section,
                            Selected = () => CmrCompat.Production.DemandAggregation(job) == index,
                            Activate = () => CmrCompat.Production.SetDemandAggregation(job, index),
                        });
                    }
                }
            }
            for (int i = 0; i < consumers.Count; i++)
            {
                CmrLinkedConsumer entry = consumers[i];
                region.Rows.Add(LinkedConsumerRow(section, tab, entry));
            }
        }

        private static CmrDetailRow BufferCountRow(string section, object job)
        {
            // The label clamps exactly as the mod's own caption does (tab 734-741); the raw field
            // is not clamped by its getter.
            int shownBuffer = Mathf.Clamp(CmrCompat.Production.LinkedDemandBufferCount(job),
                1, Math.Max(1, CmrCompat.Threshold.JobTargetCount(job)));
            return new CmrDetailRow
            {
                Label = ModArgs("ColonyManagerRedux.Production.LinkedDemandBufferCount",
                    shownBuffer),
                Tooltip = ModTip("ColonyManagerRedux.Production.LinkedDemandBufferCount.Tip"),
                Role = ElementRole.Stepper,
                SectionTitle = section,
                CanAdjust = direction => CanStepBuffer(job, direction),
                Adjust = direction => StepBuffer(job, direction),
                Numeric = new CmrNumericSpec
                {
                    Min = 1,
                    Max = Math.Max(1, CmrCompat.Threshold.JobTargetCount(job)),
                    Current = () => CmrCompat.Production.LinkedDemandBufferCount(job),
                    Apply = value => CmrCompat.Production.SetLinkedDemandBufferCount(job, value),
                },
            };
        }

        private static bool CanStepBuffer(object job, int direction)
        {
            int max = Math.Max(1, CmrCompat.Threshold.JobTargetCount(job));
            int current = CmrCompat.Production.LinkedDemandBufferCount(job);
            return direction < 0 ? current > 1 : current < max;
        }

        private static void StepBuffer(object job, int direction)
        {
            int max = Math.Max(1, CmrCompat.Threshold.JobTargetCount(job));
            int stepped = Mathf.Clamp(CmrCompat.Production.LinkedDemandBufferCount(job) + direction, 1, max);
            CmrCompat.Production.SetLinkedDemandBufferCount(job, stepped);
        }

        private static CmrDetailRow LinkedJobRow(string section, object tab, object other,
            List<ThingDef> covered, string summaryKey)
        {
            string tooltip;
            string summary = SummarizeCovered(covered, out tooltip);
            string flatTooltip = tooltip == null ? null : Flatten(tooltip);
            return new CmrDetailRow
            {
                Label = CmrCompat.Production.GetSubLabel(tab, other),
                // No covered ingredient is a real state (the consumer allows none of this
                // producer's outputs); a bare "Supplies:" with nothing after it says less than
                // silence, so the value drops instead.
                Value = summary == null ? (Func<string>)null : () => ModArgs(summaryKey, summary),
                Tooltip = flatTooltip == null ? null : (Func<string>)(() => flatTooltip),
                Role = ElementRole.Button,
                SectionTitle = section,
                Activate = () => CmrCompat.SelectJob(tab, other),
                Confirmation = "RimWorldAccess.Cmr.Production.LinkedJobSelected"
                    .Translate(CmrCompat.Production.GetSubLabel(tab, other)),
            };
        }

        private static CmrDetailRow LinkedConsumerRow(string section, object tab, CmrLinkedConsumer entry)
        {
            string tooltip;
            string summary = SummarizeCovered(entry.Covered, out tooltip);
            string flatTooltip = tooltip == null ? null : Flatten(tooltip);
            return new CmrDetailRow
            {
                Label = CmrCompat.Production.GetSubLabel(tab, entry.Consumer),
                // With nothing covered the demand still matters; only the empty "Supplies:"
                // clause drops.
                Value = summary != null
                    ? (Func<string>)(() => ModArgs("ColonyManagerRedux.Production.LinkedRow.SuppliesWithDemand",
                        summary, entry.Demand))
                    : () => "RimWorldAccess.Cmr.Production.LinkedRow.Demand"
                        .Translate(entry.Demand).ToString(),
                Tooltip = flatTooltip == null ? null : (Func<string>)(() => flatTooltip),
                Role = ElementRole.Button,
                SectionTitle = section,
                Activate = () => CmrCompat.SelectJob(tab, entry.Consumer),
                Confirmation = "RimWorldAccess.Cmr.Production.LinkedJobSelected"
                    .Translate(CmrCompat.Production.GetSubLabel(tab, entry.Consumer)),
            };
        }

        /// <summary>
        /// Up to three covered ingredient names comma-joined; beyond that the inline text collapses
        /// through "and N more" and the full list moves to <paramref name="tooltip"/> instead
        /// (mirrors <c>SummarizeCoveredIngredients</c>, tab 758-774).
        /// </summary>
        private static string SummarizeCovered(List<ThingDef> covered, out string tooltip)
        {
            if (covered.Count == 0)
            {
                tooltip = null;
                return null;
            }
            var names = new List<string>(covered.Count);
            for (int i = 0; i < covered.Count; i++)
            {
                names.Add(covered[i].LabelCap.ToString());
            }
            if (names.Count <= MaxLinkedIngredientNamesShown)
            {
                tooltip = null;
                return string.Join(", ", names);
            }
            var shown = new List<string>(MaxLinkedIngredientNamesShown);
            for (int i = 0; i < MaxLinkedIngredientNamesShown; i++)
            {
                shown.Add(names[i]);
            }
            tooltip = string.Join(", ", names);
            return ModArgs("ColonyManagerRedux.Production.LinkedRow.SuppliesMore",
                string.Join(", ", shown), names.Count - MaxLinkedIngredientNamesShown);
        }

        /// <summary>The assignment-mode radios, then the area strip (Area mode) or the work table checklist (Specific mode) (tab 1500-1533).</summary>
        private static void AddWorkbenchScope(CmrDetailRegion region, object job)
        {
            string section = ModText("ColonyManagerRedux.Production.WorkbenchScope");
            List<string> names = CmrCompat.Production.AssignmentModeNames();
            for (int i = 0; i < names.Count; i++)
            {
                int index = i;
                string key = "ColonyManagerRedux.Production.WorkbenchScope." + names[i];
                region.Rows.Add(new CmrDetailRow
                {
                    Label = ModText(key),
                    Tooltip = ModTip(key + ".Tip"),
                    Role = ElementRole.RadioButton,
                    SectionTitle = section,
                    Selected = () => CmrCompat.Production.AssignmentMode(job) == index,
                    Activate = () => CmrCompat.Production.SetAssignmentMode(job, index),
                });
            }

            // mode >= 0: an unresolvable mode and a missing enum name are both -1, and equal-but-
            // invalid must not grow the area rows.
            int mode = CmrCompat.Production.AssignmentMode(job);
            if (mode >= 0 && mode == names.IndexOf("Area"))
            {
                region.Rows.Add(WorkbenchAreaRow(section, job));

                region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.InvertArea",
                    () => CmrCompat.Production.InvertWorkbenchArea(job),
                    value => CmrCompat.Production.SetInvertWorkbenchArea(job, value)));
            }
            else if (mode >= 0 && mode == names.IndexOf("Specific"))
            {
                AddSpecificWorkbenches(region, section, job);
            }
        }

        private static CmrDetailRow WorkbenchAreaRow(string section, object job)
        {
            return CmrTabRows.AreaRow(
                "RimWorldAccess.Cmr.Production.WorkbenchAreaRow".Translate(),
                () => CmrCompat.Production.WorkbenchArea(job),
                area => CmrCompat.Production.SetWorkbenchArea(job, area),
                () => CmrCompat.JobBase.MapOf(job),
                sectionTitle: section);
        }

        private static void AddSpecificWorkbenches(CmrDetailRegion region, string section, object job)
        {
            List<Thing> tables = CmrCompat.Production.EligibleWorkTables(job);
            if (tables.Count == 0)
            {
                region.Rows.Add(new CmrDetailRow
                {
                    Label = ModText("ColonyManagerRedux.Production.WorkbenchScope.NoEligibleWorkbenches"),
                    Role = ElementRole.None,
                    SectionTitle = section,
                });
                return;
            }
            for (int i = 0; i < tables.Count; i++)
            {
                Thing table = tables[i];
                region.Rows.Add(new CmrDetailRow
                {
                    Label = table.LabelCap,
                    Role = ElementRole.Checkbox,
                    SectionTitle = section,
                    Check = () => CmrCompat.Production.SpecificContains(job, table)
                        ? CheckState.Checked
                        : CheckState.Unchecked,
                    Activate = () => CmrCompat.Production.SetSpecific(job, table,
                        !CmrCompat.Production.SpecificContains(job, table)),
                });
            }
        }


        /// <summary>Skill range (when the recipe has a work skill), ingredient radius, and the store-mode button (tab 1182-1256).</summary>
        private static void AddJobSettings(CmrDetailRegion region, object job)
        {
            string section = ModText("ColonyManagerRedux.Production.JobSettings");
            if (CmrCompat.Production.HasWorkSkill(job))
            {
                AddSkillRange(region, section, job);
            }
            AddIngredientRadius(region, section, job);

            region.Rows.Add(new CmrDetailRow
            {
                Label = CmrCompat.Production.StoreModeLabel(job),
                Role = ElementRole.Button,
                SectionTitle = section,
                Activate = () => CmrCompat.Production.OpenStoreModeMenu(job),
                OpensWindow = true,
                CapturedTwin = CmrCompat.Production.StoreModeLabel(job),
            });
        }

        private static void AddReservedStock(CmrDetailRegion region, object tab, object job)
        {
            string section = ModText("ColonyManagerRedux.Production.ReservedStock");
            foreach (ThingDef ingredient in CmrCompat.Production.ReservedIngredients(job))
            {
                ThingDef def = ingredient;
                Func<int> current = () => CmrCompat.Production.EffectiveReservedStock(job, def);
                Action<int> apply = value => CmrCompat.Production.SetReservedStockOverride(tab, job, def, Math.Max(0, value));
                region.Rows.Add(new CmrDetailRow
                {
                    Label = def.LabelCap,
                    Role = ElementRole.Stepper,
                    SectionTitle = section,
                    Value = () => current().ToString(CultureInfo.InvariantCulture),
                    Tooltip = ModTip("ColonyManagerRedux.Production.ReservedStock.JobOverride.Tip"),
                    CanAdjust = direction => direction > 0 || current() > 0,
                    Adjust = direction => apply(current() + direction),
                    Numeric = new CmrNumericSpec
                    {
                        Min = 0,
                        Max = int.MaxValue,
                        Current = current,
                        Apply = apply,
                    },
                    InfoCardDef = def,
                });
                if (CmrCompat.Production.HasReservedStockOverride(job, def))
                {
                    string key = CmrCompat.Production.HasDefaultReservedStock(job, def)
                        ? "ColonyManagerRedux.Production.ReservedStock.ResetToGlobal"
                        : "ColonyManagerRedux.Production.ReservedStock.Remove";
                    region.Rows.Add(new CmrDetailRow
                    {
                        Label = "RimWorldAccess.Cmr.Production.ReservedStockAction".Translate(ModText(key), def.LabelCap),
                        Role = ElementRole.Button,
                        SectionTitle = section,
                        Activate = () => CmrCompat.Production.SetReservedStockOverride(tab, job, def, null),
                        Confirmation = ModText(key),
                    });
                }
            }
            region.Rows.Add(new CmrDetailRow
            {
                Label = ModText("ColonyManagerRedux.Production.AddReservedStock"),
                Role = ElementRole.Button,
                SectionTitle = section,
                Activate = () => CmrCompat.Production.OpenAddReservedStockMenu(job),
                OpensWindow = true,
            });
        }

        /// <summary>The min/max pair, wording and grammar reused key-for-key from the bill config skill-range rows (BillConfigState.cs:700-714).</summary>
        private static void AddSkillRange(CmrDetailRegion region, string section, object job)
        {
            string skill = CmrCompat.Production.WorkSkillLabel(job);
            region.Rows.Add(new CmrDetailRow
            {
                Label = "RimWorldAccess.Inspection.BillConfig.Label.SkillRangeMin".Translate(
                    "AllowedSkillRange".Translate(skill),
                    "RimWorldAccess.Stepper.Minimum".Translate(),
                    CmrCompat.Production.SkillMin(job)),
                Role = ElementRole.Stepper,
                SectionTitle = section,
                CanAdjust = direction => CanStepSkillMin(job, direction),
                Adjust = direction => StepSkillMin(job, direction),
            });
            region.Rows.Add(new CmrDetailRow
            {
                Label = "RimWorldAccess.Inspection.BillConfig.Label.SkillRangeMax".Translate(
                    "AllowedSkillRange".Translate(skill),
                    "RimWorldAccess.Stepper.Maximum".Translate(),
                    CmrCompat.Production.SkillMax(job)),
                Role = ElementRole.Stepper,
                SectionTitle = section,
                CanAdjust = direction => CanStepSkillMax(job, direction),
                Adjust = direction => StepSkillMax(job, direction),
            });
        }

        private static bool CanStepSkillMin(object job, int direction)
        {
            int min = CmrCompat.Production.SkillMin(job);
            int max = CmrCompat.Production.SkillMax(job);
            return direction < 0 ? min > 0 : min < max;
        }

        private static void StepSkillMin(object job, int direction)
        {
            int max = CmrCompat.Production.SkillMax(job);
            int min = Mathf.Clamp(CmrCompat.Production.SkillMin(job) + direction, 0, max);
            CmrCompat.Production.SetSkillRange(job, min, max);
        }

        private static bool CanStepSkillMax(object job, int direction)
        {
            int min = CmrCompat.Production.SkillMin(job);
            int max = CmrCompat.Production.SkillMax(job);
            return direction < 0 ? max > min : max < CmrCompat.Production.MaxAllowedSkillLevel();
        }

        private static void StepSkillMax(object job, int direction)
        {
            int min = CmrCompat.Production.SkillMin(job);
            int max = Mathf.Clamp(CmrCompat.Production.SkillMax(job) + direction, min,
                CmrCompat.Production.MaxAllowedSkillLevel());
            CmrCompat.Production.SetSkillRange(job, min, max);
        }

        /// <summary>Same wording as the bill config radius row (BillConfigState.cs:716-723); the step behavior mirrors the slider's own 3..100-then-snap-to-unlimited span (tab 1218-1237).</summary>
        private static void AddIngredientRadius(CmrDetailRegion region, string section, object job)
        {
            region.Rows.Add(new CmrDetailRow
            {
                Label = "RimWorldAccess.Inspection.BillConfig.Label.LabelWithValue".Translate(
                    "IngredientSearchRadius".Translate(), RadiusValueText(job)),
                Role = ElementRole.Stepper,
                SectionTitle = section,
                CanAdjust = direction => CanStepRadius(job, direction),
                Adjust = direction => StepRadius(job, direction),
            });
        }

        private static string RadiusValueText(object job)
        {
            float radius = CmrCompat.Production.IngredientRadius(job);
            return radius >= 999f
                ? "Unlimited".Translate().ToString()
                : radius.ToString("F0", CultureInfo.InvariantCulture);
        }

        private static bool CanStepRadius(object job, int direction)
        {
            float radius = CmrCompat.Production.IngredientRadius(job);
            float displayed = Mathf.Min(radius, 100f);
            return direction < 0 ? displayed > 3f : radius < 999f;
        }

        private static void StepRadius(object job, int direction)
        {
            float radius = CmrCompat.Production.IngredientRadius(job);
            float displayed = Mathf.Min(radius, 100f);
            if (direction > 0)
            {
                CmrCompat.Production.SetIngredientRadius(job, displayed >= 100f ? 999f : displayed + 1f);
                return;
            }
            CmrCompat.Production.SetIngredientRadius(job, radius >= 999f ? 99f : Mathf.Max(3f, displayed - 1f));
        }

        /// <summary>The heading-less bill count line, then one row per managed bill on a work table (tab 1438-1498).</summary>
        private static void AddStatus(CmrDetailRegion region, object job)
        {
            List<Bill_Production> bills = CmrCompat.Production.ManagedBillsOf(job);
            region.Rows.Add(new CmrDetailRow
            {
                Label = ModArgs("ColonyManagerRedux.Production.ManagedBillCount", bills.Count),
                Role = ElementRole.None,
            });
            for (int i = 0; i < bills.Count; i++)
            {
                Building_WorkTable workTable = bills[i].billStack.billGiver as Building_WorkTable;
                if (workTable == null)
                {
                    continue;
                }
                region.Rows.Add(new CmrDetailRow
                {
                    Label = workTable.LabelCap,
                    Role = ElementRole.Button,
                    Activate = () => JumpToWorkTable(workTable),
                });
            }
        }

        // MUTATION-C vehicle A: the row's own click body (ManagerTab_Production.cs:1464-1471).
        private static void JumpToWorkTable(Building_WorkTable workTable)
        {
            CameraJumper.TryJumpAndSelect(workTable);
            InspectPaneUtility.OpenTab(typeof(ITab_Bills));
        }

        // ------------------------------------------------------------------
        // Ingredients: the right column.
        // ------------------------------------------------------------------

        private static CmrDetailRegion BuildIngredients(object tab, object job)
        {
            var region = new CmrDetailRegion(ModText("ColonyManagerRedux.Production.Ingredients"));
            RecipeDef recipe = CmrCompat.Production.RecipeOf(job);
            if (recipe == null)
            {
                return region;
            }

            if (CmrCompat.Production.IsConsumeSurplus(job))
            {
                region.Rows.Add(CmrTabRows.Toggle(null, "ColonyManagerRedux.SyncFilterAndAllowed",
                    () => CmrCompat.Production.SyncFilterAndAllowed(job),
                    value => CmrCompat.Production.SetSyncFilterAndAllowed(job, value),
                    tipKey: "ColonyManagerRedux.Production.SyncFilterAndAllowed.Tip"));
                region.Rows.Add(CmrTabRows.Toggle(null, "ColonyManagerRedux.Production.ConsumeSurplusPerIngredient",
                    () => CmrCompat.Production.ConsumeSurplusPerIngredient(job),
                    value => CmrCompat.Production.SetConsumeSurplusPerIngredient(job, value)));
            }

            List<ThingDef> allIngredients = CmrCompat.Production.OptionalIngredientOptions(recipe);
            Func<ThingDef, bool> isAllowed = def => CmrCompat.Production.IngredientAllowed(job, def);
            Action<ThingDef, bool> setAllowed = (def, allow) => CmrCompat.Production.SetIngredientAllowed(job, def, allow);

            CmrTabRows.AddShortcutRows(region, allIngredients, new List<CmrShortcutCategory<ThingDef>>(),
                isAllowed, setAllowed);
            foreach (KeyValuePair<ThingCategoryDef, List<ThingDef>> category in
                CmrCompat.Production.IngredientCategories(allIngredients))
            {
                CmrTabRows.AddShortcutRow(region, category.Value, allIngredients, category.Key.LabelCap,
                    ModArgs("ColonyManagerRedux.Production.IngredientCategory.Tip", category.Key.label), false,
                    isAllowed, setAllowed);
            }

            for (int i = 0; i < allIngredients.Count; i++)
            {
                ThingDef def = allIngredients[i];
                region.Rows.Add(new CmrDetailRow
                {
                    Label = def.LabelCap,
                    Role = ElementRole.Checkbox,
                    Check = () => CmrCompat.Production.IngredientAllowed(job, def)
                        ? CheckState.Checked
                        : CheckState.Unchecked,
                    Activate = () => CmrCompat.Production.SetIngredientAllowed(job, def,
                        !CmrCompat.Production.IngredientAllowed(job, def)),
                    InfoCardDef = def,
                });

                if (CmrCompat.Production.IngredientLinkState(tab, job, def) != 0)
                {
                    region.Rows.Add(SupplyLinkRow(tab, job, def));
                }
            }
            return region;
        }

        /// <summary>
        /// The link icon's own state (tab 1134-1177) as a followed row instead of a small icon.
        /// The mod's tip keys lead with the state line ("Linked to: ...", "Not linked...") and
        /// follow with explanation after a blank line, so the value speaks the state paragraph
        /// alone and the explanation stays in the tooltip.
        /// </summary>
        private static CmrDetailRow SupplyLinkRow(object tab, object job, ThingDef def)
        {
            return new CmrDetailRow
            {
                Label = "RimWorldAccess.Cmr.Production.SupplyLinkRow".Translate(def.LabelCap),
                Value = () => Flatten(CompatText.FirstParagraph(LinkStateTip(tab, job, def))),
                Tooltip = () => Flatten(CompatText.AfterFirstParagraph(LinkStateTip(tab, job, def))),
                Role = ElementRole.Button,
                Activate = () => CmrCompat.Production.OpenIngredientLinkMenu(tab, job, def),
                OpensWindow = true,
            };
        }

        private static string LinkStateTip(object tab, object job, ThingDef def)
        {
            return CmrCompat.Production.IngredientLinkState(tab, job, def) == 2
                ? ModArgs("ColonyManagerRedux.Production.IngredientLinked.Tip",
                    CmrCompat.Production.LinkedProducerSummary(tab, job, def))
                : ModText("ColonyManagerRedux.Production.IngredientNotLinked.Tip");
        }
    }
}
