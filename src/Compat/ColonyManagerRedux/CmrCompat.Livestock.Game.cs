using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    internal static partial class CmrCompat
    {
        /// <summary>
        /// Colony Manager Redux's Livestock job and tab, as far as the job's detail rows
        /// (<see cref="Shell.CmrLivestockDetails"/>) read and write them. Livestock is the one
        /// manager tab that is not a threshold job: its trigger is <c>Trigger_PawnKind</c> (four
        /// age/sex count targets in a plain <c>int[]</c>), its job list is a sub-tab pair whose
        /// Available half offers one row per unmanaged pawnkind, and its right column is two live
        /// vanilla <c>PawnTable</c>s.
        ///
        /// No mod type is named in source beyond the strings passed to
        /// <see cref="ReflectionSurface"/>; tab and job arrive as <c>object</c>. The
        /// <c>AgeAndSex</c>, <c>MasterMode</c> and <c>LivestockCullingStrategy</c> enums are
        /// unreachable from here, so their values travel boxed — age/sex by INDEX, because the four
        /// values are the indices the mod's own count and area arrays are addressed by.
        /// <c>ShouldCheckReachable</c>, <c>UsePathBasedDistance</c> and <see cref="JobBase.MapOf"/>
        /// resolve on the common <c>ManagerJob</c> base, so they stay on <see cref="JobBase"/>'s
        /// shared bindings rather than being rebound here.
        ///
        /// Mutation vehicles: doctrine A for the mod's own gated members — the
        /// <c>CullingStrategy</c> setter (it drops the job's label cache), <c>TrainingTracker</c>'s
        /// indexer setter (it IS <c>SetWantedRecursive</c>), <c>MakeNewJob</c>/<c>Selected</c>, and
        /// vanilla's <c>PawnTable.SetDirty</c>. Doctrine C for the rest, because the rest is what
        /// the mod's own widgets write with nothing wrapping it: a <c>ref bool</c> handed to
        /// <c>Utilities.DrawToggle</c>, a <c>ref Area?</c> handed to an area strip, a slider result
        /// assigned straight into a field, a float-menu option body's bare assignment.
        /// <see cref="SetField"/> is the single write site for those; <see cref="SetCountTarget"/>
        /// and <see cref="SetRestrictArea"/> write through an array's own indexer.
        /// </summary>
        internal static class Livestock
        {
            private static readonly Type jobType;
            private static readonly Type tabType;
            private static readonly Type cullingStrategyType;

            private static readonly MethodInfo triggerGetter;
            private static readonly FieldInfo pawnKindField;
            private static readonly FieldInfo countTargetsField;
            private static readonly MethodInfo expectedPawnKindNameGetter;

            private static readonly MethodInfo cullingStrategyGetter;
            private static readonly MethodInfo cullingStrategySetter;
            private static readonly MethodInfo cullExcessGetter;

            private static readonly FieldInfo cullTrainedField;
            private static readonly FieldInfo cullPregnantField;
            private static readonly FieldInfo cullBondedField;
            private static readonly FieldInfo avoidCullingNamedField;
            private static readonly FieldInfo avoidCullingMilkableField;
            private static readonly FieldInfo avoidCullingMilkableThresholdField;
            private static readonly FieldInfo avoidCullingShearableField;
            private static readonly FieldInfo avoidCullingShearableThresholdField;
            private static readonly FieldInfo tryTameMoreField;
            private static readonly FieldInfo tamePastTargetsField;
            private static readonly FieldInfo invertTameAreaField;
            private static readonly FieldInfo restrictToAreaField;
            private static readonly FieldInfo sendToCullingAreaField;
            private static readonly FieldInfo sendToMilkingAreaField;
            private static readonly FieldInfo sendToShearingAreaField;
            private static readonly FieldInfo sendToTrainingAreaField;
            private static readonly FieldInfo sendToTrainedAreaField;
            private static readonly FieldInfo setFollowField;
            private static readonly FieldInfo followDraftedField;
            private static readonly FieldInfo followFieldworkField;
            private static readonly FieldInfo followTrainingField;
            private static readonly FieldInfo respectBondsField;

            private static readonly FieldInfo tameAreaField;
            private static readonly FieldInfo cullingAreaField;
            private static readonly FieldInfo milkAreaField;
            private static readonly FieldInfo shearAreaField;
            private static readonly FieldInfo trainingAreaField;
            private static readonly FieldInfo trainedAreaField;
            private static readonly FieldInfo restrictAreaField;

            private static readonly FieldInfo masterField;
            private static readonly FieldInfo mastersField;
            private static readonly FieldInfo trainerField;
            private static readonly FieldInfo trainersField;

            private static readonly FieldInfo trainingField;
            private static readonly FieldInfo trainYoungField;
            private static readonly FieldInfo unassignTrainingField;
            private static readonly FieldInfo limitAnimalsField;
            private static readonly FieldInfo animalLimitField;
            private static readonly FieldInfo trainingPriorityField;
            private static readonly FieldInfo prioritizeNamedField;
            private static readonly FieldInfo prioritizeBondedField;
            private static readonly FieldInfo ignoreNearingEndOfLifeField;
            private static readonly FieldInfo endOfLifeThresholdField;
            private static readonly MethodInfo anyEnabledGetter;
            private static readonly MethodInfo trainingWantedGetter;
            private static readonly MethodInfo trainingWantedSetter;

            private static readonly MethodInfo canBeTrainedMethod;
            private static readonly MethodInfo masterLabelMethod;
            private static readonly MethodInfo trainerLabelMethod;

            private static readonly FieldInfo availablePawnKindsField;
            private static readonly FieldInfo selectedAvailableField;
            private static readonly FieldInfo currentTabField;
            private static readonly FieldInfo newCountsField;
            private static readonly FieldInfo tameTableField;
            private static readonly FieldInfo wildTableField;
            private static readonly MethodInfo tabListGetter;
            private static readonly MethodInfo tabRecordTabGetter;
            private static readonly MethodInfo makeNewJobMethod;

            private static readonly MethodInfo getTameMethod;
            private static readonly MethodInfo getWildMethod;
            private static readonly MethodInfo getMasterOptionsMethod;
            private static readonly MethodInfo getTrainersMethod;
            private static readonly MethodInfo milkableMethod;
            private static readonly MethodInfo shearableMethod;
            private static readonly MethodInfo estimatedMeatCountMethod;
            private static readonly MethodInfo estimatedLeatherCountMethod;
            private static readonly MethodInfo ageSexLabelMethod;
            private static readonly MethodInfo aggressivenessMethod;

            private static readonly object[] ageSexValues;
            private static readonly List<object> masterModes = new List<object>();
            private static readonly object masterModeManual;
            private static readonly object masterModeSpecific;
            private static readonly object masterModeAll;
            private static readonly object masterModeTrainers;

            private static readonly bool ready;

            static Livestock()
            {
                var surface = new ReflectionSurface("CmrCompat.Livestock");

                jobType = surface.Type("ColonyManagerRedux.Managers.ManagerJob_Livestock");
                tabType = surface.Type("ColonyManagerRedux.Managers.ManagerTab_Livestock");
                Type managerTabBaseType = surface.Type("ColonyManagerRedux.ManagerTab");
                Type triggerType = surface.Type("ColonyManagerRedux.Managers.Trigger_PawnKind");
                Type trackerType = surface.Type("ColonyManagerRedux.Managers.ManagerJob_Livestock+TrainingTracker");
                Type ageAndSexType = surface.Type("ColonyManagerRedux.Managers.AgeAndSex");
                Type masterModeType = surface.Type("ColonyManagerRedux.Managers.MasterMode");
                Type utilitiesType = surface.Type("ColonyManagerRedux.Managers.Utilities_Livestock");
                Type huntingUtilitiesType = surface.Type("ColonyManagerRedux.Managers.Utilities_Hunting");
                Type ageSexExtensionsType = surface.Type("ColonyManagerRedux.Managers.AgeAndSexExtensions");
                Type i18nType = surface.Type("ColonyManagerRedux.I18n");
                Type tabRecordType = surface.Type("ilyvion.Laboratory.UI.TabRecord");
                cullingStrategyType =
                    surface.Type("ColonyManagerRedux.Managers.ManagerJob_Livestock+LivestockCullingStrategy");

                triggerGetter = Getter(surface.Property(jobType, "TriggerPawnKind"));
                pawnKindField = surface.Field(triggerType, "pawnKind");
                countTargetsField = surface.Field(triggerType, "CountTargets");
                expectedPawnKindNameGetter = Getter(surface.Property(triggerType, "ExpectedPawnKindName"));

                PropertyInfo cullingStrategyProperty = surface.Property(jobType, "CullingStrategy");
                cullingStrategyGetter = Getter(cullingStrategyProperty);
                cullingStrategySetter = surface.Required("ManagerJob_Livestock.CullingStrategy setter",
                    Setter(cullingStrategyProperty));
                cullExcessGetter = Getter(surface.Property(jobType, "CullExcess"));

                cullTrainedField = surface.Field(jobType, "CullTrained");
                cullPregnantField = surface.Field(jobType, "CullPregnant");
                cullBondedField = surface.Field(jobType, "CullBonded");
                avoidCullingNamedField = surface.Field(jobType, "AvoidCullingNamed");
                avoidCullingMilkableField = surface.Field(jobType, "AvoidCullingMilkable");
                avoidCullingMilkableThresholdField = surface.Field(jobType, "AvoidCullingMilkableThreshold");
                avoidCullingShearableField = surface.Field(jobType, "AvoidCullingShearable");
                avoidCullingShearableThresholdField = surface.Field(jobType, "AvoidCullingShearableThreshold");
                tryTameMoreField = surface.Field(jobType, "TryTameMore");
                tamePastTargetsField = surface.Field(jobType, "TamePastTargets");
                invertTameAreaField = surface.Field(jobType, "InvertTameArea");
                restrictToAreaField = surface.Field(jobType, "RestrictToArea");
                sendToCullingAreaField = surface.Field(jobType, "SendToCullingArea");
                sendToMilkingAreaField = surface.Field(jobType, "SendToMilkingArea");
                sendToShearingAreaField = surface.Field(jobType, "SendToShearingArea");
                sendToTrainingAreaField = surface.Field(jobType, "SendToTrainingArea");
                sendToTrainedAreaField = surface.Field(jobType, "SendToTrainedArea");
                setFollowField = surface.Field(jobType, "SetFollow");
                followDraftedField = surface.Field(jobType, "FollowDrafted");
                followFieldworkField = surface.Field(jobType, "FollowFieldwork");
                followTrainingField = surface.Field(jobType, "FollowTraining");
                respectBondsField = surface.Field(jobType, "RespectBonds");

                tameAreaField = surface.Field(jobType, "TameArea");
                cullingAreaField = surface.Field(jobType, "CullingArea");
                milkAreaField = surface.Field(jobType, "MilkArea");
                shearAreaField = surface.Field(jobType, "ShearArea");
                trainingAreaField = surface.Field(jobType, "TrainingArea");
                trainedAreaField = surface.Field(jobType, "TrainedArea");
                restrictAreaField = surface.Field(jobType, "RestrictArea");

                masterField = surface.Field(jobType, "Master");
                mastersField = surface.Field(jobType, "Masters");
                trainerField = surface.Field(jobType, "Trainer");
                trainersField = surface.Field(jobType, "Trainers");

                trainingField = surface.Field(jobType, "Training");
                trainYoungField = surface.Field(trackerType, "TrainYoung");
                unassignTrainingField = surface.Field(trackerType, "UnassignTraining");
                limitAnimalsField = surface.Field(trackerType, "LimitAnimals");
                animalLimitField = surface.Field(trackerType, "AnimalLimit");
                trainingPriorityField = surface.Field(trackerType, "Priority");
                prioritizeNamedField = surface.Field(trackerType, "PrioritizeNamed");
                prioritizeBondedField = surface.Field(trackerType, "PrioritizeBonded");
                ignoreNearingEndOfLifeField = surface.Field(trackerType, "IgnoreNearingEndOfLife");
                endOfLifeThresholdField = surface.Field(trackerType, "EndOfLifeThreshold");
                anyEnabledGetter = Getter(surface.Property(trackerType, "AnyEnabled"));
                // The tracker's per-def flag is an indexer; its accessors are bound by their own
                // method names because AccessTools.Property cannot express the index parameter.
                trainingWantedGetter = surface.Method(trackerType, "get_Item", new[] { typeof(TrainableDef) });
                trainingWantedSetter = surface.Method(trackerType, "set_Item",
                    new[] { typeof(TrainableDef), typeof(bool) });

                canBeTrainedMethod = surface.Method(jobType, "CanBeTrained",
                    new[] { typeof(PawnKindDef), typeof(TrainableDef), typeof(bool).MakeByRefType() });
                if (jobType != null)
                {
                    masterLabelMethod = surface.Method(tabType, "GetMasterLabel", new[] { jobType });
                    trainerLabelMethod = surface.Method(tabType, "GetTrainerLabel", new[] { jobType });
                }

                availablePawnKindsField = surface.Field(tabType, "_availablePawnKinds");
                selectedAvailableField = surface.Field(tabType, "_selectedAvailable");
                currentTabField = surface.Field(tabType, "_currentTab");
                newCountsField = surface.Field(tabType, "_newCounts");
                tameTableField = surface.Field(tabType, "animalsTameTable");
                wildTableField = surface.Field(tabType, "animalsWildTable");
                tabListGetter = Getter(surface.Property(tabType, "TabList"));
                tabRecordTabGetter = Getter(surface.Property(tabRecordType, "Tab"));
                makeNewJobMethod = surface.Method(managerTabBaseType, "MakeNewJob", new[] { typeof(object[]) });

                // Each Utilities_Livestock member below has a same-named overload taking an AgeAndSex
                // or a Pawn, so all are bound by full parameter list. Their map parameter is a Map: the
                // tab passes its Manager and the mod's implicit Manager-to-Map conversion does the rest.
                getTameMethod = surface.Method(utilitiesType, "GetTame",
                    new[] { typeof(PawnKindDef), typeof(Map), typeof(bool) });
                getWildMethod = surface.Method(utilitiesType, "GetWild",
                    new[] { typeof(PawnKindDef), typeof(Map) });
                milkableMethod = surface.Method(utilitiesType, "Milkable", new[] { typeof(PawnKindDef) });
                shearableMethod = surface.Method(utilitiesType, "Shearable", new[] { typeof(PawnKindDef) });
                estimatedMeatCountMethod = surface.Method(huntingUtilitiesType, "EstimatedMeatCount",
                    new[] { typeof(PawnKindDef) });
                estimatedLeatherCountMethod = surface.Method(huntingUtilitiesType, "EstimatedLeatherCount",
                    new[] { typeof(PawnKindDef) });
                aggressivenessMethod = surface.Method(i18nType, "Aggressiveness", new[] { typeof(float) });
                if (masterModeType != null)
                {
                    getMasterOptionsMethod = surface.Method(utilitiesType, "GetMasterOptions",
                        new[] { typeof(PawnKindDef), typeof(Map), masterModeType });
                    getTrainersMethod = surface.Method(utilitiesType, "GetTrainers",
                        new[] { typeof(PawnKindDef), typeof(Map), masterModeType });
                }
                if (ageAndSexType != null)
                {
                    ageSexLabelMethod = surface.Method(ageSexExtensionsType, "GetLabel",
                        new[] { ageAndSexType, typeof(bool) });
                }

                ageSexValues = EnumValues(ageAndSexType);
                masterModes.AddRange(EnumValues(masterModeType));
                masterModeManual = surface.Required("MasterMode.Manual", EnumValue(masterModeType, "Manual"));
                masterModeSpecific = surface.Required("MasterMode.Specific", EnumValue(masterModeType, "Specific"));
                masterModeAll = surface.Required("MasterMode.All", EnumValue(masterModeType, "All"));
                masterModeTrainers = surface.Required("MasterMode.Trainers", EnumValue(masterModeType, "Trainers"));

                ready = surface.Ready;
            }

            /// <summary>True when every member the Livestock detail rows read or write resolved.</summary>
            public static bool Ready
            {
                get { return ready; }
            }

            /// <summary>Whether this tab is the mod's Livestock tab, by the type resolved above.</summary>
            public static bool HandlesTab(object tab)
            {
                return ready && tab != null && tabType != null && tabType.IsInstanceOfType(tab);
            }

            // The trigger: the managed pawnkind and its four count targets.

            /// <summary>The pawnkind the job manages, or null for a job whose kind the game could no longer find.</summary>
            public static PawnKindDef PawnKind(object job)
            {
                object trigger = Trigger(job);
                if (!ready || trigger == null || pawnKindField == null)
                {
                    return null;
                }
                try
                {
                    return pawnKindField.GetValue(trigger) as PawnKindDef;
                }
                catch (Exception ex)
                {
                    Fail("PawnKind", ex);
                    return null;
                }
            }

            /// <summary>The mod's stand-in name for a job whose pawnkind is missing -- what every header
            /// and label falls back to when <see cref="PawnKind"/> is null.</summary>
            public static string ExpectedPawnKindName(object job)
            {
                return Get(expectedPawnKindNameGetter, Trigger(job), "ExpectedPawnKindName") as string ?? "";
            }

            /// <summary>How many age/sex buckets the mod's own arrays carry (four: the count and area grids' indices).</summary>
            public static int AgeSexCount
            {
                get { return ageSexValues == null ? 0 : ageSexValues.Length; }
            }

            /// <summary>The mod's own whole-phrase name for one age/sex bucket ("adult female").</summary>
            public static string AgeSexLabel(int index)
            {
                object value = AgeSexValue(index);
                if (value == null || ageSexLabelMethod == null)
                {
                    return "";
                }
                return Call(ageSexLabelMethod, null, new[] { value, (object)false }, "AgeSexLabel") as string ?? "";
            }

            public static int CountTarget(object job, int index)
            {
                var targets = CountTargets(job);
                return targets != null && index >= 0 && index < targets.Length ? targets[index] : 0;
            }

            /// <summary>
            /// MUTATION-C: mirrors <c>ManagerTab_Livestock.DoCountField</c>'s own pair of writes
            /// (ManagerTab_Livestock.cs:310-324); its only gate is <c>int.TryParse</c> and no method
            /// wraps it. Both stores must be written: the int array the job reads, and the
            /// <c>_newCounts</c> text buffer the field draws from — array-only would leave stale text
            /// on screen that the field parses back over the new value on the next frame.
            /// </summary>
            public static void SetCountTarget(object tab, object job, int index, int value)
            {
                var targets = CountTargets(job);
                if (targets == null || index < 0 || index >= targets.Length)
                {
                    return;
                }
                try
                {
                    targets[index] = value;
                }
                catch (Exception ex)
                {
                    Fail("SetCountTarget", ex);
                    return;
                }
                RefreshCountText(tab, index, value);
            }

            private static void RefreshCountText(object tab, int index, int value)
            {
                if (!ready || tab == null || newCountsField == null)
                {
                    return;
                }
                try
                {
                    var texts = newCountsField.GetValue(tab) as string[];
                    if (texts != null && index >= 0 && index < texts.Length)
                    {
                        texts[index] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                catch (Exception ex)
                {
                    Fail("RefreshCountText", ex);
                }
            }

            private static int[] CountTargets(object job)
            {
                object trigger = Trigger(job);
                if (!ready || trigger == null || countTargetsField == null)
                {
                    return null;
                }
                try
                {
                    return countTargetsField.GetValue(trigger) as int[];
                }
                catch (Exception ex)
                {
                    Fail("CountTargets", ex);
                    return null;
                }
            }

            // The Available sub-tab: the tab's own unmanaged-pawnkind list.

            /// <summary>Every pawnkind the tab offers, in its own order (biome and colony kinds, label-sorted, already-managed ones removed).</summary>
            public static List<PawnKindDef> AvailablePawnKinds(object tab)
            {
                var kinds = new List<PawnKindDef>();
                if (!ready || tab == null || availablePawnKindsField == null)
                {
                    return kinds;
                }
                try
                {
                    var list = availablePawnKindsField.GetValue(tab) as IEnumerable;
                    if (list != null)
                    {
                        foreach (object item in list)
                        {
                            if (item is PawnKindDef kind)
                            {
                                kinds.Add(kind);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Fail("AvailablePawnKinds", ex);
                }
                return kinds;
            }

            /// <summary>The Available row the tab currently highlights, or null.</summary>
            private static PawnKindDef SelectedAvailable(object tab)
            {
                if (!ready || tab == null || selectedAvailableField == null)
                {
                    return null;
                }
                try
                {
                    return selectedAvailableField.GetValue(tab) as PawnKindDef;
                }
                catch (Exception ex)
                {
                    Fail("SelectedAvailable", ex);
                    return null;
                }
            }

            /// <summary>
            /// Puts the tab's own highlight and sub-tab where the keyboard cursor is, changing no game
            /// state at all.
            ///
            /// MUTATION-C: writes the two private stores the tab's own row and sub-tab clicks write --
            /// <c>_selectedAvailable</c> and <c>_currentTab</c> (ManagerTab_Livestock.cs:672-675, 882,
            /// 283, 298). Both are purely visual, with no setter to invoke.
            /// </summary>
            public static void MirrorAvailableSelection(object tab, PawnKindDef kind)
            {
                if (!ready || tab == null)
                {
                    return;
                }
                if (!ReferenceEquals(SelectedAvailable(tab), kind))
                {
                    SetField(selectedAvailableField, tab, kind, "MirrorAvailableSelection");
                }
                ShowSubTab(tab, 0);
            }

            /// <summary>The Available row's own click body (ManagerTab_Livestock.cs:880-884). The
            /// <c>Selected</c> setter's PostSelect flips the sub-tab to Current and seeds the count
            /// text buffer by itself.</summary>
            public static void SelectAvailable(object tab, PawnKindDef kind)
            {
                if (!ready || tab == null || kind == null || makeNewJobMethod == null)
                {
                    return;
                }
                SetField(selectedAvailableField, tab, kind, "SelectAvailable");
                object job = Call(makeNewJobMethod, tab,
                    new object[] { new object[] { kind } }, "SelectAvailable");
                // Unconditional, as in the mod's own click body: a failed MakeNewJob returns null,
                // which deselects — exactly what the mod would show.
                CmrCompat.SelectJob(tab, job);
            }

            /// <summary>Flips the visible sub-tab to <c>TabList[index]</c>'s own tab object, the write
            /// <c>TabDrawer.DrawTabs</c> performs on a sub-tab click. Silent: only ever follows the
            /// cursor.</summary>
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
                        // MUTATION-C: the private store the sub-tab strip writes; see MirrorAvailableSelection.
                        currentTabField.SetValue(tab, target);
                    }
                }
                catch (Exception ex)
                {
                    Fail("ShowSubTab", ex);
                }
            }

            // The two animal tables.

            /// <summary>The tab's tame or wild animal table, or null before the mod has drawn it once:
            /// both are created lazily inside the mod's own draw, so null means "not yet" and not an
            /// error.</summary>
            public static PawnTable AnimalTable(object tab, bool wild)
            {
                FieldInfo field = wild ? wildTableField : tameTableField;
                if (!ready || tab == null || field == null)
                {
                    return null;
                }
                try
                {
                    return field.GetValue(tab) as PawnTable;
                }
                catch (Exception ex)
                {
                    Fail("AnimalTable", ex);
                    return null;
                }
            }

            // Culling strategy. The enum is unreachable from here: values travel boxed.

            /// <summary>The strategy enum's own values in declaration order -- the order the mod's radio group draws in.</summary>
            public static List<object> CullingStrategyValues()
            {
                var values = new List<object>();
                object[] all = EnumValues(cullingStrategyType);
                if (ready && all != null)
                {
                    values.AddRange(all);
                }
                return values;
            }

            /// <summary>The enum member's own name, for composing the mod's own per-strategy label key.</summary>
            public static string CullingStrategyName(object value)
            {
                return value == null ? "" : value.ToString();
            }

            public static object CullingStrategy(object job)
            {
                return Get(cullingStrategyGetter, job, "CullingStrategy");
            }

            /// <summary>The culling radio's own click body (ManagerTab_Livestock.cs:930-934): the
            /// property setter drops the job's cached label, then the tame table is dirtied because its
            /// Cull column comes and goes with the strategy.</summary>
            public static void SetCullingStrategy(object tab, object job, object value)
            {
                SetProperty(cullingStrategySetter, job, value, "SetCullingStrategy");
                PawnTable table = AnimalTable(tab, wild: false);
                if (table != null)
                {
                    table.SetDirty();
                }
            }

            /// <summary>The mod's own gate on the culling-exception and send-to-culling controls.</summary>
            public static bool CullExcess(object job)
            {
                return Flag(cullExcessGetter, job, "CullExcess");
            }

            // Plain-field toggles. Every one is a Utilities.DrawToggle ref-bool.

            public static bool CullTrained(object job)
            {
                return FieldFlag(cullTrainedField, job, "CullTrained");
            }

            public static void SetCullTrained(object job, bool value)
            {
                SetField(cullTrainedField, job, value, "SetCullTrained");
            }

            public static bool CullPregnant(object job)
            {
                return FieldFlag(cullPregnantField, job, "CullPregnant");
            }

            public static void SetCullPregnant(object job, bool value)
            {
                SetField(cullPregnantField, job, value, "SetCullPregnant");
            }

            public static bool CullBonded(object job)
            {
                return FieldFlag(cullBondedField, job, "CullBonded");
            }

            public static void SetCullBonded(object job, bool value)
            {
                SetField(cullBondedField, job, value, "SetCullBonded");
            }

            public static bool AvoidCullingNamed(object job)
            {
                return FieldFlag(avoidCullingNamedField, job, "AvoidCullingNamed");
            }

            public static void SetAvoidCullingNamed(object job, bool value)
            {
                SetField(avoidCullingNamedField, job, value, "SetAvoidCullingNamed");
            }

            public static bool AvoidCullingMilkable(object job)
            {
                return FieldFlag(avoidCullingMilkableField, job, "AvoidCullingMilkable");
            }

            public static void SetAvoidCullingMilkable(object job, bool value)
            {
                SetField(avoidCullingMilkableField, job, value, "SetAvoidCullingMilkable");
            }

            public static bool AvoidCullingShearable(object job)
            {
                return FieldFlag(avoidCullingShearableField, job, "AvoidCullingShearable");
            }

            public static void SetAvoidCullingShearable(object job, bool value)
            {
                SetField(avoidCullingShearableField, job, value, "SetAvoidCullingShearable");
            }

            public static bool TryTameMore(object job)
            {
                return FieldFlag(tryTameMoreField, job, "TryTameMore");
            }

            public static void SetTryTameMore(object job, bool value)
            {
                SetField(tryTameMoreField, job, value, "SetTryTameMore");
            }

            public static bool TamePastTargets(object job)
            {
                return FieldFlag(tamePastTargetsField, job, "TamePastTargets");
            }

            public static void SetTamePastTargets(object job, bool value)
            {
                SetField(tamePastTargetsField, job, value, "SetTamePastTargets");
            }

            public static bool InvertTameArea(object job)
            {
                return FieldFlag(invertTameAreaField, job, "InvertTameArea");
            }

            public static void SetInvertTameArea(object job, bool value)
            {
                SetField(invertTameAreaField, job, value, "SetInvertTameArea");
            }

            public static bool RestrictToArea(object job)
            {
                return FieldFlag(restrictToAreaField, job, "RestrictToArea");
            }

            public static void SetRestrictToArea(object job, bool value)
            {
                SetField(restrictToAreaField, job, value, "SetRestrictToArea");
            }

            public static bool SendToCullingArea(object job)
            {
                return FieldFlag(sendToCullingAreaField, job, "SendToCullingArea");
            }

            public static void SetSendToCullingArea(object job, bool value)
            {
                SetField(sendToCullingAreaField, job, value, "SetSendToCullingArea");
            }

            public static bool SendToMilkingArea(object job)
            {
                return FieldFlag(sendToMilkingAreaField, job, "SendToMilkingArea");
            }

            public static void SetSendToMilkingArea(object job, bool value)
            {
                SetField(sendToMilkingAreaField, job, value, "SetSendToMilkingArea");
            }

            public static bool SendToShearingArea(object job)
            {
                return FieldFlag(sendToShearingAreaField, job, "SendToShearingArea");
            }

            public static void SetSendToShearingArea(object job, bool value)
            {
                SetField(sendToShearingAreaField, job, value, "SetSendToShearingArea");
            }

            public static bool SendToTrainingArea(object job)
            {
                return FieldFlag(sendToTrainingAreaField, job, "SendToTrainingArea");
            }

            public static void SetSendToTrainingArea(object job, bool value)
            {
                SetField(sendToTrainingAreaField, job, value, "SetSendToTrainingArea");
            }

            public static bool SendToTrainedArea(object job)
            {
                return FieldFlag(sendToTrainedAreaField, job, "SendToTrainedArea");
            }

            public static void SetSendToTrainedArea(object job, bool value)
            {
                SetField(sendToTrainedAreaField, job, value, "SetSendToTrainedArea");
            }

            public static bool FollowEnabled(object job)
            {
                return FieldFlag(setFollowField, job, "SetFollow");
            }

            public static void SetFollowEnabled(object job, bool value)
            {
                SetField(setFollowField, job, value, "SetFollowEnabled");
            }

            public static bool FollowDrafted(object job)
            {
                return FieldFlag(followDraftedField, job, "FollowDrafted");
            }

            public static void SetFollowDrafted(object job, bool value)
            {
                SetField(followDraftedField, job, value, "SetFollowDrafted");
            }

            public static bool FollowFieldwork(object job)
            {
                return FieldFlag(followFieldworkField, job, "FollowFieldwork");
            }

            public static void SetFollowFieldwork(object job, bool value)
            {
                SetField(followFieldworkField, job, value, "SetFollowFieldwork");
            }

            public static bool FollowTraining(object job)
            {
                return FieldFlag(followTrainingField, job, "FollowTraining");
            }

            public static void SetFollowTraining(object job, bool value)
            {
                SetField(followTrainingField, job, value, "SetFollowTraining");
            }

            public static bool RespectBonds(object job)
            {
                return FieldFlag(respectBondsField, job, "RespectBonds");
            }

            public static void SetRespectBonds(object job, bool value)
            {
                SetField(respectBondsField, job, value, "SetRespectBonds");
            }

            // Culling-exception thresholds. The mod writes each slider's result straight back into the
            // field, with no clamping beyond the slider's own range.

            public static float AvoidCullingMilkableThreshold(object job)
            {
                return FieldFloat(avoidCullingMilkableThresholdField, job, "AvoidCullingMilkableThreshold");
            }

            public static void SetAvoidCullingMilkableThreshold(object job, float value)
            {
                SetField(avoidCullingMilkableThresholdField, job, value, "SetAvoidCullingMilkableThreshold");
            }

            public static float AvoidCullingShearableThreshold(object job)
            {
                return FieldFloat(avoidCullingShearableThresholdField, job, "AvoidCullingShearableThreshold");
            }

            public static void SetAvoidCullingShearableThreshold(object job, float value)
            {
                SetField(avoidCullingShearableThresholdField, job, value, "SetAvoidCullingShearableThreshold");
            }

            // Areas. Each is written by an AreaAllowedGUI.DoAllowedAreaSelectors strip taking the field
            // by ref; the four restriction areas share one array.

            public static Area TameArea(object job)
            {
                return FieldArea(tameAreaField, job, "TameArea");
            }

            public static void SetTameArea(object job, Area area)
            {
                SetField(tameAreaField, job, area, "SetTameArea");
            }

            public static Area CullingArea(object job)
            {
                return FieldArea(cullingAreaField, job, "CullingArea");
            }

            public static void SetCullingArea(object job, Area area)
            {
                SetField(cullingAreaField, job, area, "SetCullingArea");
            }

            public static Area MilkArea(object job)
            {
                return FieldArea(milkAreaField, job, "MilkArea");
            }

            public static void SetMilkArea(object job, Area area)
            {
                SetField(milkAreaField, job, area, "SetMilkArea");
            }

            public static Area ShearArea(object job)
            {
                return FieldArea(shearAreaField, job, "ShearArea");
            }

            public static void SetShearArea(object job, Area area)
            {
                SetField(shearAreaField, job, area, "SetShearArea");
            }

            public static Area TrainingArea(object job)
            {
                return FieldArea(trainingAreaField, job, "TrainingArea");
            }

            public static void SetTrainingArea(object job, Area area)
            {
                SetField(trainingAreaField, job, area, "SetTrainingArea");
            }

            public static Area TrainedArea(object job)
            {
                return FieldArea(trainedAreaField, job, "TrainedArea");
            }

            public static void SetTrainedArea(object job, Area area)
            {
                SetField(trainedAreaField, job, area, "SetTrainedArea");
            }

            /// <summary>The restriction area for one age/sex bucket, from the array the mod indexes by the same value.</summary>
            public static Area RestrictArea(object job, int index)
            {
                Area[] areas = RestrictAreas(job);
                return areas != null && index >= 0 && index < areas.Length ? areas[index] : null;
            }

            /// <summary>
            /// MUTATION-C: writes one cell of <c>RestrictArea</c> through the array's own indexer,
            /// exactly as the restriction grid's four area strips do (ManagerTab_Livestock.cs:426-466).
            /// The array is a plain public field and the strips are inline in the draw, so there is no
            /// method to invoke instead.
            /// </summary>
            public static void SetRestrictArea(object job, int index, Area area)
            {
                Area[] areas = RestrictAreas(job);
                if (areas == null || index < 0 || index >= areas.Length)
                {
                    return;
                }
                try
                {
                    areas[index] = area;
                }
                catch (Exception ex)
                {
                    Fail("SetRestrictArea", ex);
                }
            }

            private static Area[] RestrictAreas(object job)
            {
                if (!ready || job == null || restrictAreaField == null)
                {
                    return null;
                }
                try
                {
                    return restrictAreaField.GetValue(job) as Area[];
                }
                catch (Exception ex)
                {
                    Fail("RestrictAreas", ex);
                    return null;
                }
            }

            // Masters and trainers. The mod's two float menus write these fields from their option bodies.

            /// <summary>The mod's own wording for the master button's current value, including its "unavailable" case.</summary>
            public static string MasterLabel(object job)
            {
                return Call(masterLabelMethod, null, new[] { job }, "MasterLabel") as string ?? "";
            }

            /// <summary>The mod's own wording for the trainer button's current value.</summary>
            public static string TrainerLabel(object job)
            {
                return Call(trainerLabelMethod, null, new[] { job }, "TrainerLabel") as string ?? "";
            }

            /// <summary>The modes the master menu lists — those the <c>All</c> mask covers, as the menu's
            /// own LINQ filter computes them. <paramref name="forTrainers"/> switches to the trainer
            /// menu's narrower <c>Trainers</c> mask.</summary>
            public static List<object> MasterModeChoices(bool forTrainers)
            {
                var choices = new List<object>();
                object mask = forTrainers ? masterModeTrainers : masterModeAll;
                if (!ready || mask == null)
                {
                    return choices;
                }
                int maskValue = EnumInt(mask);
                for (int i = 0; i < masterModes.Count; i++)
                {
                    int value = EnumInt(masterModes[i]);
                    if ((value & maskValue) == value)
                    {
                        choices.Add(masterModes[i]);
                    }
                }
                return choices;
            }

            /// <summary>The enum member's own name, for composing the mod's own per-mode label key.</summary>
            public static string MasterModeName(object mode)
            {
                return mode == null ? "" : mode.ToString();
            }

            /// <summary>Whether the master mode is one of the two the respect-bonds toggle is inert for,
            /// the mod's own condition for greying it out (ManagerTab_Livestock.cs:1180).</summary>
            public static bool MastersIsManualOrSpecific(object job)
            {
                object current = FieldValue(mastersField, job, "Masters");
                return current == null || Equals(current, masterModeManual) || Equals(current, masterModeSpecific);
            }

            public static void SetMasters(object job, object mode)
            {
                SetField(mastersField, job, mode, "SetMasters");
            }

            /// <summary>Both statements a master menu pawn option writes, in its own order: the pawn,
            /// then the <c>Specific</c> mode (ManagerTab_Livestock.cs:1156-1158).</summary>
            public static void SetMaster(object job, Pawn pawn)
            {
                SetField(masterField, job, pawn, "SetMaster");
                SetField(mastersField, job, masterModeSpecific, "SetMaster");
            }

            public static void SetTrainers(object job, object mode)
            {
                SetField(trainersField, job, mode, "SetTrainers");
            }

            /// <summary>Both statements a trainer menu pawn option writes (ManagerTab_Livestock.cs:1319-1320).</summary>
            public static void SetTrainer(object job, Pawn pawn)
            {
                SetField(trainerField, job, pawn, "SetTrainer");
                SetField(trainersField, job, masterModeSpecific, "SetTrainer");
            }

            /// <summary>The colonists the master menu offers, in the mod's own (shuffled, cached) order.</summary>
            public static List<Pawn> MasterOptions(object job)
            {
                return PawnOptions(getMasterOptionsMethod, job, masterModeAll, "MasterOptions");
            }

            /// <summary>The colonists the trainer menu offers -- the master options, narrowed by the mod's own handling-skill gate.</summary>
            public static List<Pawn> TrainerOptions(object job)
            {
                return PawnOptions(getTrainersMethod, job, masterModeTrainers, "TrainerOptions");
            }

            private static List<Pawn> PawnOptions(MethodInfo method, object job, object mode, string member)
            {
                var pawns = new List<Pawn>();
                PawnKindDef kind = PawnKind(job);
                Map map = JobBase.MapOf(job);
                if (!ready || method == null || kind == null || map == null || mode == null)
                {
                    return pawns;
                }
                var items = Call(method, null, new[] { kind, (object)map, mode }, member) as IEnumerable;
                if (items == null)
                {
                    return pawns;
                }
                try
                {
                    foreach (object item in items)
                    {
                        if (item is Pawn pawn)
                        {
                            pawns.Add(pawn);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                }
                return pawns;
            }

            // Training.

            /// <summary>Whether the job wants this trainable taught, from the tracker's own indexer.</summary>
            public static bool TrainingWanted(object job, TrainableDef def)
            {
                object tracker = Training(job);
                if (tracker == null || def == null || trainingWantedGetter == null)
                {
                    return false;
                }
                return Call(trainingWantedGetter, tracker, new object[] { def }, "TrainingWanted") is bool value
                    && value;
            }

            /// <summary>The tracker's own indexer setter, which the mod's training toggle assigns to
            /// verbatim. The setter IS <c>SetWantedRecursive</c>: it pulls in prerequisites, drops what
            /// depends on the trainable, and plays the mod's checkbox sound.</summary>
            public static void SetTrainingWanted(object job, TrainableDef def, bool wanted)
            {
                object tracker = Training(job);
                if (tracker == null || def == null || trainingWantedSetter == null)
                {
                    return;
                }
                Call(trainingWantedSetter, tracker, new object[] { def, wanted }, "SetTrainingWanted");
            }

            /// <summary>The mod's own gate on the two send-to-training-area controls.</summary>
            public static bool TrainingAnyEnabled(object job)
            {
                return Flag(anyEnabledGetter, Training(job), "TrainingAnyEnabled");
            }

            public static bool TrainYoung(object job)
            {
                return FieldFlag(trainYoungField, Training(job), "TrainYoung");
            }

            public static void SetTrainYoung(object job, bool value)
            {
                SetField(trainYoungField, Training(job), value, "SetTrainYoung");
            }

            public static bool UnassignTraining(object job)
            {
                return FieldFlag(unassignTrainingField, Training(job), "UnassignTraining");
            }

            public static void SetUnassignTraining(object job, bool value)
            {
                SetField(unassignTrainingField, Training(job), value, "SetUnassignTraining");
            }

            public static bool LimitTrainedAnimals(object job)
            {
                return FieldFlag(limitAnimalsField, Training(job), "LimitTrainedAnimals");
            }

            public static void SetLimitTrainedAnimals(object job, bool value)
            {
                SetField(limitAnimalsField, Training(job), value, "SetLimitTrainedAnimals");
            }

            public static int TrainedAnimalLimit(object job)
            {
                return FieldValue(animalLimitField, Training(job), "TrainedAnimalLimit") is int limit ? limit : 0;
            }

            public static void SetTrainedAnimalLimit(object job, int value)
            {
                SetField(animalLimitField, Training(job), value, "SetTrainedAnimalLimit");
            }

            public static List<object> TrainingPriorityValues()
            {
                var values = new List<object>();
                if (ready && trainingPriorityField != null)
                {
                    values.AddRange(EnumValues(trainingPriorityField.FieldType));
                }
                return values;
            }

            public static object TrainingPriority(object job)
            {
                return FieldValue(trainingPriorityField, Training(job), "TrainingPriority");
            }

            public static void SetTrainingPriority(object job, object priority)
            {
                SetField(trainingPriorityField, Training(job), priority, "SetTrainingPriority");
            }

            public static bool PrioritizeNamed(object job)
            {
                return FieldFlag(prioritizeNamedField, Training(job), "PrioritizeNamed");
            }

            public static void SetPrioritizeNamed(object job, bool value)
            {
                SetField(prioritizeNamedField, Training(job), value, "SetPrioritizeNamed");
            }

            public static bool PrioritizeBonded(object job)
            {
                return FieldFlag(prioritizeBondedField, Training(job), "PrioritizeBonded");
            }

            public static void SetPrioritizeBonded(object job, bool value)
            {
                SetField(prioritizeBondedField, Training(job), value, "SetPrioritizeBonded");
            }

            public static bool IgnoreNearingEndOfLife(object job)
            {
                return FieldFlag(ignoreNearingEndOfLifeField, Training(job), "IgnoreNearingEndOfLife");
            }

            public static void SetIgnoreNearingEndOfLife(object job, bool value)
            {
                SetField(ignoreNearingEndOfLifeField, Training(job), value, "SetIgnoreNearingEndOfLife");
            }

            public static float EndOfLifeThreshold(object job)
            {
                return FieldFloat(endOfLifeThresholdField, Training(job), "EndOfLifeThreshold");
            }

            public static void SetEndOfLifeThreshold(object job, float value)
            {
                SetField(endOfLifeThresholdField, Training(job), value, "SetEndOfLifeThreshold");
            }

            /// <summary>The mod's own answer for whether a pawnkind can be taught a trainable: accepted,
            /// its reason when not, and <paramref name="visible"/> for whether it draws the control at
            /// all. Every training row and the whole follow section gate on this.</summary>
            public static bool CanBeTrained(PawnKindDef kind, TrainableDef def, out bool visible,
                out string reason)
            {
                visible = false;
                reason = "";
                if (!ready || kind == null || def == null || canBeTrainedMethod == null)
                {
                    return false;
                }
                try
                {
                    var args = new object[] { kind, def, false };
                    object result = canBeTrainedMethod.Invoke(null, args);
                    visible = args[2] is bool shown && shown;
                    if (!(result is AcceptanceReport report))
                    {
                        return false;
                    }
                    reason = report.Reason ?? "";
                    return report.Accepted;
                }
                catch (Exception ex)
                {
                    Fail("CanBeTrained", ex);
                    return false;
                }
            }

            private static object Training(object job)
            {
                return FieldValue(trainingField, job, "Training");
            }

            // Pawnkind facts the Available list and the area sections read.

            /// <summary>How many of this kind the colony has, excluding guests -- the Available row's own tame count.</summary>
            public static int TameCount(PawnKindDef kind, Map map)
            {
                return CountOf(getTameMethod, new object[] { kind, map, false }, kind, map, "TameCount");
            }

            /// <summary>How many of this kind are on the map unclaimed -- the Available row's own wild count.</summary>
            public static int WildCount(PawnKindDef kind, Map map)
            {
                return CountOf(getWildMethod, new object[] { kind, map }, kind, map, "WildCount");
            }

            private static int CountOf(MethodInfo method, object[] args, PawnKindDef kind, Map map,
                string member)
            {
                if (!ready || method == null || kind == null || map == null)
                {
                    return 0;
                }
                var items = Call(method, null, args, member) as IEnumerable;
                if (items == null)
                {
                    return 0;
                }
                try
                {
                    if (items is ICollection collection)
                    {
                        return collection.Count;
                    }
                    int count = 0;
                    foreach (object item in items)
                    {
                        count++;
                    }
                    return count;
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                    return 0;
                }
            }

            /// <summary>The mod's own cached answer for whether this kind gives milk -- what gates the milking controls.</summary>
            public static bool Milkable(PawnKindDef kind)
            {
                return KindFlag(milkableMethod, kind, "Milkable");
            }

            /// <summary>The mod's own cached answer for whether this kind gives wool -- what gates the shearing controls.</summary>
            public static bool Shearable(PawnKindDef kind)
            {
                return KindFlag(shearableMethod, kind, "Shearable");
            }

            /// <summary>The meat one animal of this kind is expected to yield, as the mod's own icon tooltip states it.</summary>
            public static int EstimatedMeatCount(PawnKindDef kind)
            {
                return KindCount(estimatedMeatCountMethod, kind, "EstimatedMeatCount");
            }

            /// <summary>The leather one animal of this kind is expected to yield, as the mod's own icon tooltip states it.</summary>
            public static int EstimatedLeatherCount(PawnKindDef kind)
            {
                return KindCount(estimatedLeatherCountMethod, kind, "EstimatedLeatherCount");
            }

            /// <summary>The mod's own wording for a manhunter-on-tame-failure chance, from its own I18n helper.</summary>
            public static string Aggressiveness(float chance)
            {
                return Call(aggressivenessMethod, null, new object[] { chance }, "Aggressiveness") as string ?? "";
            }

            // Plumbing, all gated on this block's own Ready. Getter/Setter/Fail belong to the
            // enclosing CmrCompat.

            private static object Trigger(object job)
            {
                return Get(triggerGetter, job, "TriggerPawnKind");
            }

            private static object AgeSexValue(int index)
            {
                return ready && ageSexValues != null && index >= 0 && index < ageSexValues.Length
                    ? ageSexValues[index]
                    : null;
            }

            private static bool KindFlag(MethodInfo method, PawnKindDef kind, string member)
            {
                if (!ready || method == null || kind == null)
                {
                    return false;
                }
                return Call(method, null, new object[] { kind }, member) is bool value && value;
            }

            private static int KindCount(MethodInfo method, PawnKindDef kind, string member)
            {
                if (!ready || method == null || kind == null)
                {
                    return 0;
                }
                return Call(method, null, new object[] { kind }, member) is int value ? value : 0;
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

            private static object EnumValue(Type enumType, string name)
            {
                if (enumType == null || !enumType.IsEnum || !Enum.IsDefined(enumType, name))
                {
                    return null;
                }
                return Enum.Parse(enumType, name);
            }

            private static int EnumInt(object value)
            {
                try
                {
                    return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (Exception ex)
                {
                    Fail("EnumInt", ex);
                    return 0;
                }
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

            private static Area FieldArea(FieldInfo field, object instance, string member)
            {
                return FieldValue(field, instance, member) as Area;
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

            /// <summary>The single write site for the plain fields the mod's <c>ref</c>-parameter
            /// widgets and float-menu option bodies write.</summary>
            private static void SetField(FieldInfo field, object instance, object value, string member)
            {
                if (!ready || field == null || instance == null)
                {
                    return;
                }
                try
                {
                    // MUTATION-C: the write the mod's own widget performs through its ref parameter or
                    // option body; none of these fields has a gated setter to invoke instead.
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
