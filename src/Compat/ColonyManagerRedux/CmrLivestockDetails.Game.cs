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
    /// The Livestock tab's detail rows. Not a threshold tab: no New-job button, no threshold window,
    /// an Available/Current sub-tab pair, and two live vanilla <c>PawnTable</c>s instead of a def list.
    /// Regions follow <c>ManagerTab_Livestock.DoMainContent</c>'s order: Available, Options, then the
    /// tame and wild tables; Available is the only region that exists with no job selected.
    /// Conditional rows appear and vanish with the sighted ones, and controls the mod greys out become
    /// disabled rows carrying its reason wording rather than disappearing.
    /// </summary>
    internal sealed class CmrLivestockDetails : ICmrJobDetailsProvider
    {
        private const int MinTrainedAnimalLimit = 0;
        private const int MaxTrainedAnimalLimit = 9999;
        private const float MinEndOfLifeThreshold = 0.1f;

        public bool Handles(object tab)
        {
            return CmrCompat.Livestock.HandlesTab(tab);
        }

        public List<CmrDetailRegion> Build(object tab, object job)
        {
            var regions = new List<CmrDetailRegion>();
            if (tab == null || !CmrCompat.Livestock.Ready)
            {
                return regions;
            }
            regions.Add(BuildAvailable(tab));
            if (job == null)
            {
                var empty = new CmrDetailRegion("RimWorldAccess.Cmr.OptionsRegion".Translate());
                empty.Rows.Add(new CmrDetailRow { Label = ModText("ColonyManagerRedux.Livestock.NoPawnKindSelected") });
                SettleToCurrent(empty, tab);
                regions.Add(empty);
                return regions;
            }
            CmrDetailRegion options = BuildOptions(tab, job);
            CmrDetailRegion tame = BuildAnimals(tab, job, wild: false);
            CmrDetailRegion wildAnimals = BuildAnimals(tab, job, wild: true);
            SettleToCurrent(options, tab);
            SettleToCurrent(tame, tab);
            SettleToCurrent(wildAnimals, tab);
            regions.Add(options);
            regions.Add(tame);
            regions.Add(wildAnimals);
            return regions;
        }

        /// <summary>Shows the Current sub-tab as the cursor crosses any job-side row. Overwrites OnSettle
        /// wholesale, so the Available region (whose rows carry their own) must stay outside.</summary>
        private static void SettleToCurrent(CmrDetailRegion region, object tab)
        {
            for (int i = 0; i < region.Rows.Count; i++)
            {
                region.Rows[i].OnSettle = () => CmrCompat.Livestock.ShowSubTab(tab, 1);
            }
        }

        /// <summary>
        /// One row per pawnkind the tab offers, in its own order: name, the mod's tame/wild count line,
        /// then the icon strip's tooltips in draw order as the verbose tail
        /// (ManagerTab_Livestock.cs:685-847). Enter runs the row's own click body.
        /// </summary>
        private static CmrDetailRegion BuildAvailable(object tab)
        {
            var region = new CmrDetailRegion(ModText("ColonyManagerRedux.Thresholds.Available"));
            // The manager window belongs to the current map; a pawnkind's counts come from its caches.
            Map map = Find.CurrentMap;
            List<PawnKindDef> kinds = CmrCompat.Livestock.AvailablePawnKinds(tab);
            for (int i = 0; i < kinds.Count; i++)
            {
                PawnKindDef kind = kinds[i];
                region.Rows.Add(new CmrDetailRow
                {
                    Label = AvailableLabel(kind, map),
                    Tooltip = () => AvailableTail(kind, map),
                    Role = ElementRole.Button,
                    InfoCardDef = kind.race,
                    OnSettle = () => CmrCompat.Livestock.MirrorAvailableSelection(tab, kind),
                    Activate = () => CmrCompat.Livestock.SelectAvailable(tab, kind),
                    Confirmation = "RimWorldAccess.Cmr.Livestock.JobOpened".Translate(kind.LabelCap)
                        .Resolve(),
                });
            }
            return region;
        }

        private static string AvailableLabel(PawnKindDef kind, Map map)
        {
            string counts = ModArgs("ColonyManagerRedux.Livestock.TameCount",
                    CmrCompat.Livestock.TameCount(kind, map))
                + ", "
                + ModArgs("ColonyManagerRedux.Livestock.WildCount",
                    CmrCompat.Livestock.WildCount(kind, map));
            return Flatten(CompatText.JoinSentences(new List<string> { kind.LabelCap, counts }));
        }

        /// <summary>Each icon's own tooltip, in the order the row draws the icons.</summary>
        private static string AvailableTail(PawnKindDef kind, Map map)
        {
            var parts = new List<string>();
            RaceProperties race = kind.RaceProps;
            // A race claiming meat without a meatDef gets no icon from the mod, so no line here.
            if (race.hasMeat && race.meatDef != null)
            {
                parts.Add(ModArgs("ColonyManagerRedux.Livestock.Yields", race.meatDef.LabelCap,
                    CmrCompat.Livestock.EstimatedMeatCount(kind)));
            }
            if (race.leatherDef != null)
            {
                parts.Add(ModArgs("ColonyManagerRedux.Livestock.Yields", race.leatherDef.LabelCap,
                    CmrCompat.Livestock.EstimatedLeatherCount(kind)));
            }
            var milkable = kind.race.GetCompProperties<CompProperties_Milkable>();
            if (milkable != null && milkable.milkDef != null)
            {
                parts.Add(ModArgs("ColonyManagerRedux.Livestock.YieldsInterval", milkable.milkDef.LabelCap,
                    milkable.milkAmount, milkable.milkIntervalDays));
            }
            var shearable = kind.race.GetCompProperties<CompProperties_Shearable>();
            if (shearable != null && shearable.woolDef != null)
            {
                parts.Add(ModArgs("ColonyManagerRedux.Livestock.YieldsInterval", shearable.woolDef.LabelCap,
                    shearable.woolAmount, shearable.shearIntervalDays));
            }
            if (race.trainability != null)
            {
                parts.Add("Trainability".Translate() + ": " + race.trainability.LabelCap);
            }
            if (race.nuzzleMtbHours > 0f)
            {
                parts.Add("NuzzleInterval".Translate() + ": "
                    + Mathf.RoundToInt(race.nuzzleMtbHours * 2500f).ToStringTicksToPeriod());
            }
            if (race.manhunterOnTameFailChance >= 0.1f)
            {
                parts.Add(CmrCompat.Livestock.Aggressiveness(race.manhunterOnTameFailChance));
            }
            string venerated = VeneratedTail(kind, map);
            if (venerated != null)
            {
                parts.Add(venerated);
            }
            return Flatten(CompatText.JoinSentences(parts));
        }

        /// <summary>The venerated-animal warning, only while some colonist venerates the kind, with the mod's all/some distinction.</summary>
        private static string VeneratedTail(PawnKindDef kind, Map map)
        {
            if (!ModsConfig.IdeologyActive || map == null)
            {
                return null;
            }
            bool anyVenerated = false;
            bool allVenerated = true;
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Ideo ideo = colonists[i].Ideo;
                bool venerated = ideo != null && ideo.IsVeneratedAnimal(kind.race);
                anyVenerated |= venerated;
                allVenerated &= venerated;
            }
            if (!anyVenerated)
            {
                return null;
            }
            return ModArgs("ColonyManagerRedux.Livestock.VeneratedAnimal.Tip",
                ModText(allVenerated ? "ColonyManagerRedux.Misc.All" : "ColonyManagerRedux.Misc.Some"));
        }

        private static CmrDetailRegion BuildOptions(object tab, object job)
        {
            var region = new CmrDetailRegion("RimWorldAccess.Cmr.OptionsRegion".Translate());
            AddTargetCounts(region, tab, job);
            AddTaming(region, job);
            AddCulling(region, tab, job);
            AddCullingExceptions(region, job);
            AddTraining(region, job);
            AddAreaRestrictions(region, job);
            AddFollow(region, job);
            return region;
        }

        /// <summary>The four count targets the mod draws as a grid of text fields; Enter types an exact number into the same array cell.</summary>
        private static void AddTargetCounts(CmrDetailRegion region, object tab, object job)
        {
            string section = ModText("ColonyManagerRedux.Livestock.TargetCountsHeader");
            int buckets = CmrCompat.Livestock.AgeSexCount;
            for (int i = 0; i < buckets; i++)
            {
                int index = i;
                region.Rows.Add(new CmrDetailRow
                {
                    Label = "RimWorldAccess.Cmr.Livestock.CountTarget"
                        .Translate(CmrCompat.Livestock.AgeSexLabel(index)).Resolve().CapitalizeFirst(),
                    Role = ElementRole.Stepper,
                    SectionTitle = section,
                    Value = () => CmrCompat.Livestock.CountTarget(job, index)
                        .ToString(CultureInfo.InvariantCulture),
                    CanAdjust = direction => direction > 0
                        || CmrCompat.Livestock.CountTarget(job, index) > 0,
                    Adjust = direction => StepCount(tab, job, index, direction),
                    Numeric = new CmrNumericSpec
                    {
                        // The field's only gate is int.TryParse, so the range is int's own.
                        Min = 0,
                        Max = int.MaxValue,
                        Current = () => CmrCompat.Livestock.CountTarget(job, index),
                        Apply = value => CmrCompat.Livestock.SetCountTarget(tab, job, index, value),
                    },
                });
            }
        }

        /// <summary>One is the text field's own unit; the mod ships no stepper of its own.</summary>
        private static void StepCount(object tab, object job, int index, int direction)
        {
            int stepped = CmrCompat.Livestock.CountTarget(job, index) + direction;
            CmrCompat.Livestock.SetCountTarget(tab, job, index, stepped < 0 ? 0 : stepped);
        }

        /// <summary>The tame-more toggle and, only while it is on, the four controls under it (ManagerTab_Livestock.cs:1334-1374).</summary>
        private static void AddTaming(CmrDetailRegion region, object job)
        {
            string section = ModText("ColonyManagerRedux.Livestock.TamingHeader");
            region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Livestock.TameMore",
                () => CmrCompat.Livestock.TryTameMore(job),
                value => CmrCompat.Livestock.SetTryTameMore(job, value)));

            if (!CmrCompat.Livestock.TryTameMore(job))
            {
                return;
            }
            region.Rows.Add(CmrTabRows.AreaRow(ModLabel("ColonyManagerRedux.Livestock.TameArea"),
                () => CmrCompat.Livestock.TameArea(job),
                area => CmrCompat.Livestock.SetTameArea(job, area),
                () => CmrCompat.JobBase.MapOf(job),
                sectionTitle: section));
            region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.InvertArea",
                () => CmrCompat.Livestock.InvertTameArea(job),
                value => CmrCompat.Livestock.SetInvertTameArea(job, value)));
            region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Livestock.TamePastTargets",
                () => CmrCompat.Livestock.TamePastTargets(job),
                value => CmrCompat.Livestock.SetTamePastTargets(job, value)));
            region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Threshold.CheckReachability",
                () => CmrCompat.JobBase.ShouldCheckReachable(job),
                value => CmrCompat.JobBase.SetShouldCheckReachable(job, value)));
            region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Threshold.PathBasedDistance",
                () => CmrCompat.JobBase.UsePathBasedDistance(job),
                value => CmrCompat.JobBase.SetUsePathBasedDistance(job, value)));
        }

        /// <summary>One radio row per culling strategy, in the enum's declaration order — the order the mod's grid draws them.</summary>
        private static void AddCulling(CmrDetailRegion region, object tab, object job)
        {
            string section = ModText("ColonyManagerRedux.Livestock.CullingHeader");
            List<object> strategies = CmrCompat.Livestock.CullingStrategyValues();
            for (int i = 0; i < strategies.Count; i++)
            {
                object strategy = strategies[i];
                string key = "ColonyManagerRedux.Livestock.CullingStrategy."
                    + CmrCompat.Livestock.CullingStrategyName(strategy);
                region.Rows.Add(new CmrDetailRow
                {
                    Label = ModText(key),
                    Tooltip = ModTip(key + ".Tip"),
                    Role = ElementRole.RadioButton,
                    SectionTitle = section,
                    Selected = () => Equals(CmrCompat.Livestock.CullingStrategy(job), strategy),
                    Activate = () => CmrCompat.Livestock.SetCullingStrategy(tab, job, strategy),
                });
            }
        }

        /// <summary>The exceptions the mod draws only while a culling strategy is set; with None the section is empty for a sighted player too.</summary>
        private static void AddCullingExceptions(CmrDetailRegion region, object job)
        {
            if (!CmrCompat.Livestock.CullExcess(job))
            {
                return;
            }
            string section = ModText("ColonyManagerRedux.Livestock.CullingExceptionsHeader");
            region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Livestock.CullTrained",
                () => CmrCompat.Livestock.CullTrained(job),
                value => CmrCompat.Livestock.SetCullTrained(job, value)));
            region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Livestock.CullPregnant",
                () => CmrCompat.Livestock.CullPregnant(job),
                value => CmrCompat.Livestock.SetCullPregnant(job, value)));
            region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Livestock.CullBonded",
                () => CmrCompat.Livestock.CullBonded(job),
                value => CmrCompat.Livestock.SetCullBonded(job, value)));
            region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Livestock.AvoidCullingNamed",
                () => CmrCompat.Livestock.AvoidCullingNamed(job),
                value => CmrCompat.Livestock.SetAvoidCullingNamed(job, value)));

            region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Livestock.AvoidCullingMilkable",
                () => CmrCompat.Livestock.AvoidCullingMilkable(job),
                value => CmrCompat.Livestock.SetAvoidCullingMilkable(job, value)));
            if (CmrCompat.Livestock.AvoidCullingMilkable(job))
            {
                region.Rows.Add(ThresholdRow(section,
                    "ColonyManagerRedux.Livestock.AvoidCullingMilkableThreshold",
                    () => CmrCompat.Livestock.AvoidCullingMilkableThreshold(job),
                    value => CmrCompat.Livestock.SetAvoidCullingMilkableThreshold(job, value)));
            }

            region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Livestock.AvoidCullingShearable",
                () => CmrCompat.Livestock.AvoidCullingShearable(job),
                value => CmrCompat.Livestock.SetAvoidCullingShearable(job, value)));
            if (CmrCompat.Livestock.AvoidCullingShearable(job))
            {
                region.Rows.Add(ThresholdRow(section,
                    "ColonyManagerRedux.Livestock.AvoidCullingShearableThreshold",
                    () => CmrCompat.Livestock.AvoidCullingShearableThreshold(job),
                    value => CmrCompat.Livestock.SetAvoidCullingShearableThreshold(job, value)));
            }
        }

        /// <summary>A percentage slider whose caption embeds the percentage, so the row carries no separate
        /// value: stepping rebuilds the label. No typed entry; Enter-to-type is for count fields.</summary>
        private static CmrDetailRow ThresholdRow(string section, string labelKey, Func<float> read,
            Action<float> write, float min = 0f)
        {
            return new CmrDetailRow
            {
                Label = ModArgs(labelKey, PercentText(read())),
                Role = ElementRole.Stepper,
                SectionTitle = section,
                CanAdjust = direction => direction < 0 ? read() > min : read() < 1f,
                Adjust = direction => write(SliderStep.Stepped(read(), direction, min, 1f, -1f)),
            };
        }

        /// <summary>One row per trainable the mod draws a cell for: a toggle when the kind accepts it, else a
        /// disabled row speaking the mod's reason. A job whose pawnkind went missing gets no trainable rows.</summary>
        private static void AddTraining(CmrDetailRegion region, object job)
        {
            string section = ModText("ColonyManagerRedux.Livestock.TrainingHeader");
            PawnKindDef kind = CmrCompat.Livestock.PawnKind(job);
            if (kind != null)
            {
                List<TrainableDef> defs = DefDatabase<TrainableDef>.AllDefsListForReading;
                for (int i = 0; i < defs.Count; i++)
                {
                    TrainableDef def = defs[i];
                    bool visible;
                    string reason;
                    bool accepted = CmrCompat.Livestock.CanBeTrained(kind, def, out visible, out reason);
                    if (!visible)
                    {
                        continue;
                    }
                    if (!accepted)
                    {
                        region.Rows.Add(DisabledRow(section, def.LabelCap, reason));
                        continue;
                    }
                    region.Rows.Add(new CmrDetailRow
                    {
                        Label = def.LabelCap,
                        Tooltip = () => Flatten(def.description),
                        Role = ElementRole.Checkbox,
                        SectionTitle = section,
                        Check = () => CmrCompat.Livestock.TrainingWanted(job, def)
                            ? CheckState.Checked
                            : CheckState.Unchecked,
                        Activate = () => CmrCompat.Livestock.SetTrainingWanted(job, def,
                            !CmrCompat.Livestock.TrainingWanted(job, def)),
                    });
                }
            }
            region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Livestock.UnassignTraining",
                () => CmrCompat.Livestock.UnassignTraining(job),
                value => CmrCompat.Livestock.SetUnassignTraining(job, value)));
            region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Livestock.TrainYoung",
                () => CmrCompat.Livestock.TrainYoung(job),
                value => CmrCompat.Livestock.SetTrainYoung(job, value)));
            AddTrainingLimit(region, section, job);
        }

        private static void AddTrainingLimit(CmrDetailRegion region, string section, object job)
        {
            region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Livestock.LimitTrainedAnimals",
                () => CmrCompat.Livestock.LimitTrainedAnimals(job),
                value => CmrCompat.Livestock.SetLimitTrainedAnimals(job, value)));
            if (!CmrCompat.Livestock.LimitTrainedAnimals(job))
            {
                return;
            }
            region.Rows.Add(new CmrDetailRow
            {
                Label = ModText("ColonyManagerRedux.Livestock.LimitTrainedAnimals"),
                Role = ElementRole.Stepper,
                SectionTitle = section,
                Value = () => CmrCompat.Livestock.TrainedAnimalLimit(job).ToString(CultureInfo.InvariantCulture),
                CanAdjust = direction => direction < 0
                    ? CmrCompat.Livestock.TrainedAnimalLimit(job) > MinTrainedAnimalLimit
                    : CmrCompat.Livestock.TrainedAnimalLimit(job) < MaxTrainedAnimalLimit,
                Adjust = direction => CmrCompat.Livestock.SetTrainedAnimalLimit(job, Mathf.Clamp(
                    CmrCompat.Livestock.TrainedAnimalLimit(job) + direction, MinTrainedAnimalLimit,
                    MaxTrainedAnimalLimit)),
                Numeric = new CmrNumericSpec
                {
                    Min = MinTrainedAnimalLimit,
                    Max = MaxTrainedAnimalLimit,
                    Current = () => CmrCompat.Livestock.TrainedAnimalLimit(job),
                    Apply = value => CmrCompat.Livestock.SetTrainedAnimalLimit(job, value),
                },
            });

            region.Rows.Add(new CmrDetailRow
            {
                Label = ModText("ColonyManagerRedux.Livestock.TrainingPriority"),
                Tooltip = ModTip("ColonyManagerRedux.Livestock.TrainingPriority.Tip"),
                Role = ElementRole.ComboBox,
                SectionTitle = section,
                Value = () => TrainingPriorityLabel(CmrCompat.Livestock.TrainingPriority(job)),
                Choices = () => TrainingPriorityChoices(job),
            });
            region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Livestock.PrioritizeNamed",
                () => CmrCompat.Livestock.PrioritizeNamed(job),
                value => CmrCompat.Livestock.SetPrioritizeNamed(job, value)));
            region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Livestock.PrioritizeBonded",
                () => CmrCompat.Livestock.PrioritizeBonded(job),
                value => CmrCompat.Livestock.SetPrioritizeBonded(job, value)));
            region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Livestock.IgnoreNearingEndOfLife",
                () => CmrCompat.Livestock.IgnoreNearingEndOfLife(job),
                value => CmrCompat.Livestock.SetIgnoreNearingEndOfLife(job, value)));
            if (CmrCompat.Livestock.IgnoreNearingEndOfLife(job))
            {
                region.Rows.Add(ThresholdRow(section, "ColonyManagerRedux.Livestock.EndOfLifeThreshold",
                    () => CmrCompat.Livestock.EndOfLifeThreshold(job),
                    value => CmrCompat.Livestock.SetEndOfLifeThreshold(job, value), MinEndOfLifeThreshold));
            }
        }

        private static string TrainingPriorityLabel(object priority)
        {
            return priority == null ? "" : ModText("ColonyManagerRedux.Livestock.TrainingPriority." + priority);
        }

        private static List<CmrPickerChoice> TrainingPriorityChoices(object job)
        {
            var choices = new List<CmrPickerChoice>();
            foreach (object priority in CmrCompat.Livestock.TrainingPriorityValues())
            {
                object choice = priority;
                choices.Add(new CmrPickerChoice(TrainingPriorityLabel(choice),
                    () => CmrCompat.Livestock.SetTrainingPriority(job, choice)));
            }
            return choices;
        }

        /// <summary>The restriction grid and the five send-to-area controls. A roaming animal ends the section after one greyed label, as the mod draws no send-to controls at all.</summary>
        private static void AddAreaRestrictions(CmrDetailRegion region, object job)
        {
            string section = ModText("ColonyManagerRedux.Livestock.AreaRestrictionsHeader");
            PawnKindDef kind = CmrCompat.Livestock.PawnKind(job);
            if (kind != null && kind.RaceProps.Roamer)
            {
                string plural = kind.GetLabelPlural().CapitalizeFirst();
                region.Rows.Add(new CmrDetailRow
                {
                    Label = ModArgs("ColonyManagerRedux.Livestock.DisabledBecauseRoamingAnimal", plural),
                    Tooltip = () => Flatten(ModArgs(
                        "ColonyManagerRedux.Livestock.DisabledBecauseRoamingAnimalTip", plural)),
                    Role = ElementRole.None,
                    SectionTitle = section,
                });
                return;
            }

            region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Livestock.RestrictToArea",
                () => CmrCompat.Livestock.RestrictToArea(job),
                value => CmrCompat.Livestock.SetRestrictToArea(job, value)));
            if (CmrCompat.Livestock.RestrictToArea(job))
            {
                int buckets = CmrCompat.Livestock.AgeSexCount;
                for (int i = 0; i < buckets; i++)
                {
                    int index = i;
                    region.Rows.Add(CmrTabRows.AreaRow(
                        "RimWorldAccess.Cmr.Livestock.RestrictAreaRow"
                            .Translate(CmrCompat.Livestock.AgeSexLabel(index)).Resolve().CapitalizeFirst(),
                        () => CmrCompat.Livestock.RestrictArea(job, index),
                        area => CmrCompat.Livestock.SetRestrictArea(job, index, area),
                        () => CmrCompat.JobBase.MapOf(job),
                        sectionTitle: section));
                }
            }

            if (CmrCompat.Livestock.CullExcess(job))
            {
                AddSendToArea(region, section, job, "ColonyManagerRedux.Livestock.SendToCullingArea",
                    "RimWorldAccess.Cmr.Livestock.CullingAreaRow",
                    () => CmrCompat.Livestock.SendToCullingArea(job),
                    value => CmrCompat.Livestock.SetSendToCullingArea(job, value),
                    () => CmrCompat.Livestock.CullingArea(job),
                    area => CmrCompat.Livestock.SetCullingArea(job, area));
            }
            else
            {
                region.Rows.Add(DisabledRow(section,
                    ModText("ColonyManagerRedux.Livestock.SendToCullingArea"),
                    ModText("ColonyManagerRedux.Livestock.DisabledBecauseCullExcessDisabled")));
            }

            if (kind != null && CmrCompat.Livestock.Milkable(kind))
            {
                AddSendToArea(region, section, job, "ColonyManagerRedux.Livestock.SendToMilkingArea",
                    "RimWorldAccess.Cmr.Livestock.MilkingAreaRow",
                    () => CmrCompat.Livestock.SendToMilkingArea(job),
                    value => CmrCompat.Livestock.SetSendToMilkingArea(job, value),
                    () => CmrCompat.Livestock.MilkArea(job),
                    area => CmrCompat.Livestock.SetMilkArea(job, area));
            }
            if (kind != null && CmrCompat.Livestock.Shearable(kind))
            {
                AddSendToArea(region, section, job, "ColonyManagerRedux.Livestock.SendToShearingArea",
                    "RimWorldAccess.Cmr.Livestock.ShearingAreaRow",
                    () => CmrCompat.Livestock.SendToShearingArea(job),
                    value => CmrCompat.Livestock.SetSendToShearingArea(job, value),
                    () => CmrCompat.Livestock.ShearArea(job),
                    area => CmrCompat.Livestock.SetShearArea(job, area));
            }

            if (CmrCompat.Livestock.TrainingAnyEnabled(job))
            {
                AddSendToArea(region, section, job, "ColonyManagerRedux.Livestock.SendToTrainingArea",
                    "RimWorldAccess.Cmr.Livestock.TrainingAreaRow",
                    () => CmrCompat.Livestock.SendToTrainingArea(job),
                    value => CmrCompat.Livestock.SetSendToTrainingArea(job, value),
                    () => CmrCompat.Livestock.TrainingArea(job),
                    area => CmrCompat.Livestock.SetTrainingArea(job, area));
                AddSendToArea(region, section, job, "ColonyManagerRedux.Livestock.SendToTrainedArea",
                    "RimWorldAccess.Cmr.Livestock.TrainedAreaRow",
                    () => CmrCompat.Livestock.SendToTrainedArea(job),
                    value => CmrCompat.Livestock.SetSendToTrainedArea(job, value),
                    () => CmrCompat.Livestock.TrainedArea(job),
                    area => CmrCompat.Livestock.SetTrainedArea(job, area));
            }
            else
            {
                string reason = ModText("ColonyManagerRedux.Livestock.DisabledBecauseNoTrainingSet");
                region.Rows.Add(DisabledRow(section,
                    ModText("ColonyManagerRedux.Livestock.SendToTrainingArea"), reason));
                region.Rows.Add(DisabledRow(section,
                    ModText("ColonyManagerRedux.Livestock.SendToTrainedArea"), reason));
            }
        }

        private static void AddSendToArea(CmrDetailRegion region, string section, object job,
            string toggleKey, string areaLabelKey, Func<bool> read, Action<bool> write,
            Func<Area> readArea, Action<Area> writeArea)
        {
            region.Rows.Add(CmrTabRows.Toggle(section, toggleKey, read, write));
            if (read())
            {
                region.Rows.Add(CmrTabRows.AreaRow(areaLabelKey.Translate(), readArea, writeArea,
                    () => CmrCompat.JobBase.MapOf(job), sectionTitle: section));
            }
        }

        /// <summary>The following section, gated throughout on whether the kind can learn obedience; where the
        /// mod greys a label with the report's reason, the row here is disabled and carries that reason.</summary>
        private static void AddFollow(CmrDetailRegion region, object job)
        {
            string section = ModText("ColonyManagerRedux.Livestock.FollowHeader");
            PawnKindDef kind = CmrCompat.Livestock.PawnKind(job);
            string reason = "";
            bool accepted = kind != null
                && CmrCompat.Livestock.CanBeTrained(kind, TrainableDefOf.Obedience, out _, out reason);

            string masterLabel = ModText("ColonyManagerRedux.Livestock.MasterDefault");
            if (accepted)
            {
                region.Rows.Add(new CmrDetailRow
                {
                    Label = masterLabel,
                    Tooltip = ModTip("ColonyManagerRedux.Livestock.MasterDefault.Tip"),
                    Role = ElementRole.ComboBox,
                    SectionTitle = section,
                    Value = () => Flatten(CmrCompat.Livestock.MasterLabel(job)),
                    Choices = () => MasterChoices(job, forTrainers: false),
                    CapturedTwin = CmrCompat.Livestock.MasterLabel(job),
                });
            }
            else
            {
                CmrDetailRow row = DisabledRow(section, masterLabel, reason);
                row.Value = () => Flatten(CmrCompat.Livestock.MasterLabel(job));
                region.Rows.Add(row);
            }

            string bondsLabel = ModText("ColonyManagerRedux.Livestock.RespectBonds");
            if (!accepted)
            {
                region.Rows.Add(DisabledRow(section, bondsLabel, reason));
            }
            else if (!CmrCompat.Livestock.MastersIsManualOrSpecific(job))
            {
                region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Livestock.RespectBonds",
                    () => CmrCompat.Livestock.RespectBonds(job),
                    value => CmrCompat.Livestock.SetRespectBonds(job, value)));
            }
            else
            {
                region.Rows.Add(DisabledRow(section, bondsLabel, ModText(
                    "ColonyManagerRedux.Livestock.RespectBonds.DisabledBecauseMastersNotSet")));
            }

            if (accepted)
            {
                region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Livestock.Follow",
                    () => CmrCompat.Livestock.FollowEnabled(job),
                    value => CmrCompat.Livestock.SetFollowEnabled(job, value)));
                if (CmrCompat.Livestock.FollowEnabled(job))
                {
                    region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Livestock.FollowDrafted",
                        () => CmrCompat.Livestock.FollowDrafted(job),
                        value => CmrCompat.Livestock.SetFollowDrafted(job, value)));
                    region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Livestock.FollowFieldwork",
                        () => CmrCompat.Livestock.FollowFieldwork(job),
                        value => CmrCompat.Livestock.SetFollowFieldwork(job, value)));
                }
                region.Rows.Add(CmrTabRows.Toggle(section, "ColonyManagerRedux.Livestock.FollowTraining",
                    () => CmrCompat.Livestock.FollowTraining(job),
                    value => CmrCompat.Livestock.SetFollowTraining(job, value)));
                if (CmrCompat.Livestock.FollowTraining(job))
                {
                    region.Rows.Add(new CmrDetailRow
                    {
                        Label = ModText("ColonyManagerRedux.Livestock.MasterTraining"),
                        Tooltip = ModTip("ColonyManagerRedux.Livestock.MasterTraining.Tip"),
                        Role = ElementRole.ComboBox,
                        SectionTitle = section,
                        Value = () => Flatten(CmrCompat.Livestock.TrainerLabel(job)),
                        Choices = () => MasterChoices(job, forTrainers: true),
                        CapturedTwin = CmrCompat.Livestock.TrainerLabel(job),
                    });
                }
                return;
            }
            region.Rows.Add(DisabledRow(section, ModText("ColonyManagerRedux.Livestock.Follow"), reason));
            region.Rows.Add(DisabledRow(section,
                ModText("ColonyManagerRedux.Livestock.FollowTraining"), reason));
        }

        /// <summary>The master or trainer float menu's options: every mode its mask admits, then every colonist its option query returns.</summary>
        private static List<CmrPickerChoice> MasterChoices(object job, bool forTrainers)
        {
            var choices = new List<CmrPickerChoice>();
            List<object> modes = CmrCompat.Livestock.MasterModeChoices(forTrainers);
            for (int i = 0; i < modes.Count; i++)
            {
                object mode = modes[i];
                string label = ModText("ColonyManagerRedux.Livestock.MasterMode."
                    + CmrCompat.Livestock.MasterModeName(mode));
                choices.Add(new CmrPickerChoice(label, forTrainers
                    ? (Action)(() => CmrCompat.Livestock.SetTrainers(job, mode))
                    : () => CmrCompat.Livestock.SetMasters(job, mode)));
            }
            List<Pawn> pawns = forTrainers
                ? CmrCompat.Livestock.TrainerOptions(job)
                : CmrCompat.Livestock.MasterOptions(job);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                choices.Add(new CmrPickerChoice(
                    ModArgs("ColonyManagerRedux.Livestock.Master", pawn.LabelShort, HandlingSkill(pawn)),
                    forTrainers
                        ? (Action)(() => CmrCompat.Livestock.SetTrainer(job, pawn))
                        : () => CmrCompat.Livestock.SetMaster(job, pawn)));
            }
            return choices;
        }

        private static float HandlingSkill(Pawn pawn)
        {
            return pawn.skills == null
                ? 0f
                : pawn.skills.AverageOfRelevantSkillsFor(WorkTypeDefOf.Handling);
        }

        /// <summary>
        /// One region per table, navigable and sortable like the vanilla pawn-table tabs; cells are
        /// read-only because every one is a status display.
        /// Columns must be read through the table's own <c>Columns</c> property rather than its def (via
        /// <see cref="CmrDetailPawnTable.Read"/>, whose read order is load-bearing): that read is what
        /// tells the mod's visibility patch which of the two tables it is answering for.
        /// </summary>
        private static CmrDetailRegion BuildAnimals(object tab, object job, bool wild)
        {
            string typeWord = ModText(wild
                ? "ColonyManagerRedux.Livestock.Wild"
                : "ColonyManagerRedux.Livestock.Tame");
            string plural = PluralLabel(job);
            var region = new CmrDetailRegion(
                ModArgs("ColonyManagerRedux.Livestock.AnimalsHeader", typeWord, plural).CapitalizeFirst());

            CmrDetailPawnTable table = CmrDetailPawnTable.Read(CmrCompat.Livestock.AnimalTable(tab, wild));
            if (table == null || table.Rows.Count == 0)
            {
                region.Rows.Add(new CmrDetailRow
                {
                    Label = Flatten(ModArgs("ColonyManagerRedux.Livestock.NoAnimals", typeWord, plural)),
                    Role = ElementRole.None,
                });
                return region;
            }
            region.Table = table;
            return region;
        }

        /// <summary>The plural name of the kind the job manages, or the mod's own placeholder for a kind it lost.</summary>
        private static string PluralLabel(object job)
        {
            PawnKindDef kind = CmrCompat.Livestock.PawnKind(job);
            return kind != null ? kind.GetLabelPlural() : CmrCompat.Livestock.ExpectedPawnKindName(job);
        }

        /// <summary>A row for a control the mod greys out: no Check and no Activate, so it never claims a state or accepts Enter.</summary>
        private static CmrDetailRow DisabledRow(string section, string label, string reason)
        {
            return new CmrDetailRow
            {
                Label = label,
                Tooltip = () => Flatten(reason),
                Role = ElementRole.None,
                SectionTitle = section,
            };
        }

        /// <summary>A mod label written as a caption ("Allow taming in:"); a row name carries no trailing colon.</summary>
        private static string ModLabel(string key)
        {
            return ModText(key).TrimEnd(' ', ':');
        }

        private static string PercentText(float value)
        {
            return value.ToString("0%", CultureInfo.InvariantCulture);
        }
    }
}
