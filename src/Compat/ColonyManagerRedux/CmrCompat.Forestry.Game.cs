using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    internal static partial class CmrCompat
    {
        /// <summary>
        /// Colony Manager Redux's Forestry job and tab, as far as the job's detail rows
        /// (<see cref="Shell.CmrForestryDetails"/>) read and write it. Bound separately from
        /// <see cref="CmrCompat.Ready"/> and <see cref="Hunting"/> so a rename of something only
        /// the Forestry rows touch cannot disturb the tab and job lists or the Hunting rows.
        ///
        /// The job and tab types are internal to the mod and resolved by name once; callers pass
        /// them as <c>object</c> and read them through the typed wrappers below. Members that live
        /// on the shared <c>ManagerJob</c> base or on <c>Trigger_Threshold</c> are reached through
        /// <see cref="JobBase"/> and <see cref="Threshold"/> instead of being bound here.
        ///
        /// Every write is one of the mod's own widget bodies: property setters and methods the
        /// mod's widgets call, or — for the toggles it draws by handing a public field by
        /// reference — that field itself, through <see cref="SetField"/>. The one exception is
        /// <see cref="SetClearAreaAllowed"/>, marked at the method.
        /// </summary>
        internal static class Forestry
        {
            private static readonly Type tabType;
            private static readonly Type jobTypeEnum;

            private static readonly MethodInfo typeGetter;
            private static readonly MethodInfo typeSetter;
            private static readonly MethodInfo triggerGetter;

            private static readonly FieldInfo allowedTreesField;
            private static readonly FieldInfo allowSaplingsField;
            private static readonly FieldInfo clearAreasField;
            private static readonly FieldInfo loggingAreaField;
            private static readonly FieldInfo invertLoggingAreaField;
            private static readonly FieldInfo syncField;
            private static readonly FieldInfo syncFilterAndAllowedField;

            private static readonly MethodInfo allPlantsGetter;
            private static readonly MethodInfo plantsLockedGetter;
            private static readonly MethodInfo plantsLockedSetter;
            private static readonly MethodInfo setTreeAllowedMethod;
            private static readonly MethodInfo refreshAllTreesMethod;
            private static readonly MethodInfo updateClearAreasMethod;
            private static readonly MethodInfo plantTooltipMethod;

            private static readonly MethodInfo cachedDesignatedCountGetter;
            private static readonly MethodInfo cacheUpdateMethod;
            private static readonly MethodInfo cacheValueGetter;

            private static readonly MethodInfo targetLabelGetter;
            private static readonly MethodInfo currentCountMethod;

            private static readonly object clearAreaValue;
            private static readonly object loggingValue;
            private static readonly object filterToAllowedSync;

            private static readonly bool ready;

            static Forestry()
            {
                var surface = new ReflectionSurface("CmrCompat.Forestry");

                Type jobType = surface.Type("ColonyManagerRedux.Managers.ManagerJob_Forestry");
                tabType = surface.Type("ColonyManagerRedux.Managers.ManagerTab_Forestry");
                Type foragingTabType = surface.Type("ColonyManagerRedux.Managers.ManagerTab_Foraging");
                Type triggerType = surface.Type("ColonyManagerRedux.Trigger_Threshold");
                jobTypeEnum = surface.Supplied("ManagerJob_Forestry.ForestryJobType",
                    jobType != null ? jobType.GetNestedType("ForestryJobType") : null);

                PropertyInfo typeProperty = surface.Property(jobType, "Type");
                typeGetter = Getter(typeProperty);
                typeSetter = surface.Required("ManagerJob_Forestry.Type setter", Setter(typeProperty));
                clearAreaValue = surface.EnumValue(jobTypeEnum, "ClearArea");
                loggingValue = surface.EnumValue(jobTypeEnum, "Logging");

                triggerGetter = Getter(surface.Property(jobType, "TriggerThreshold"));

                allowedTreesField = surface.Field(jobType, "AllowedTrees");
                allowSaplingsField = surface.Field(jobType, "AllowSaplings");
                clearAreasField = surface.Field(jobType, "ClearAreas");
                loggingAreaField = surface.Field(jobType, "LoggingArea");
                invertLoggingAreaField = surface.Field(jobType, "InvertLoggingArea");
                syncField = surface.Field(jobType, "Sync");
                filterToAllowedSync = surface.EnumValue(syncField != null ? syncField.FieldType : null, "FilterToAllowed");
                syncFilterAndAllowedField = surface.Field(jobType, "SyncFilterAndAllowed");

                allPlantsGetter = Getter(surface.Property(jobType, "AllPlants"));
                PropertyInfo plantsLockedProperty = surface.Property(jobType, "PlantsLockedToMap");
                plantsLockedGetter = Getter(plantsLockedProperty);
                plantsLockedSetter = surface.Required("ManagerJob_Forestry.PlantsLockedToMap setter",
                    Setter(plantsLockedProperty));
                setTreeAllowedMethod = surface.Method(jobType, "SetTreeAllowed",
                    new[] { typeof(ThingDef), typeof(bool), typeof(bool) });
                refreshAllTreesMethod = surface.Method(jobType, "RefreshAllTrees", new Type[0]);
                updateClearAreasMethod = surface.Method(jobType, "UpdateClearAreas", new Type[0]);
                plantTooltipMethod = surface.Method(foragingTabType, "GetPlantTooltip",
                    new[] { typeof(ThingDef) });

                cachedDesignatedCountGetter =
                    Getter(surface.Property(jobType, "CachedCurrentDesignatedCount"));
                // The cache is a closed generic the mod never names publicly; its own accessor's
                // return type is the only handle on it that survives a version change.
                Type cacheType = cachedDesignatedCountGetter != null
                    ? cachedDesignatedCountGetter.ReturnType
                    : null;
                cacheUpdateMethod = surface.Method(cacheType, "DoUpdateIfNeeded", new[] { typeof(bool) });
                cacheValueGetter = Getter(surface.Property(cacheType, "Value"));

                targetLabelGetter = Getter(surface.Property(triggerType, "TargetLabel"));
                currentCountMethod = surface.Method(triggerType, "GetCurrentCount", new[] { typeof(bool) });

                ready = surface.Ready;
            }

            /// <summary>True when every member the Forestry detail rows read or write resolved.</summary>
            public static bool Ready
            {
                get { return ready; }
            }

            /// <summary>Whether this tab is the mod's Forestry tab, by the type resolved above.</summary>
            public static bool HandlesTab(object tab)
            {
                return ready && tab != null && tabType != null && tabType.IsInstanceOfType(tab);
            }

            // Job type.

            /// <summary>The enum's own values, in its own declaration order (ClearArea, then Logging) -- the order the mod's radio row draws in.</summary>
            public static List<object> JobTypeValues()
            {
                var values = new List<object>();
                if (!ready || jobTypeEnum == null)
                {
                    return values;
                }
                foreach (object value in Enum.GetValues(jobTypeEnum))
                {
                    values.Add(value);
                }
                return values;
            }

            /// <summary>The enum member's own name, for composing the mod's own label/tooltip keys.</summary>
            public static string JobTypeName(object value)
            {
                return value == null ? "" : value.ToString();
            }

            public static object JobType(object job)
            {
                return Get(typeGetter, job, "JobType");
            }

            public static bool IsClearArea(object job)
            {
                object current = JobType(job);
                return current != null && clearAreaValue != null && current.Equals(clearAreaValue);
            }

            public static bool IsLogging(object job)
            {
                object current = JobType(job);
                return current != null && loggingValue != null && current.Equals(loggingValue);
            }

            /// <summary>Writes the job type through the type toggles' own delegate target (ManagerTab_Forestry.cs:267-277); the setter re-derives the tree list and the threshold parent filter itself.</summary>
            public static void SetJobType(object job, object value)
            {
                SetProperty(typeSetter, job, value, "SetJobType");
            }

            // Threshold (Logging only).

            public static int CurrentCount(object job)
            {
                object trigger = Trigger(job);
                if (trigger == null || currentCountMethod == null)
                {
                    return 0;
                }
                object value = Call(currentCountMethod, trigger, new object[] { true }, "CurrentCount");
                return value is int count ? count : 0;
            }

            /// <summary>
            /// The job's outstanding designated count as the threshold section reads it: kick the
            /// multi-tick cache if due, then take the value it last finished
            /// (ManagerTab_Forestry.cs:286-287).
            /// </summary>
            public static int DesignatedCount(object job)
            {
                if (!ready || job == null || cachedDesignatedCountGetter == null
                    || cacheUpdateMethod == null)
                {
                    return 0;
                }
                object cache = Get(cachedDesignatedCountGetter, job, "DesignatedCount");
                if (cache == null)
                {
                    return 0;
                }
                Call(cacheUpdateMethod, cache, new object[] { false }, "DesignatedCount");
                object value = Get(cacheValueGetter, cache, "DesignatedCount");
                return value is int count ? count : 0;
            }

            /// <summary>The trigger's own operator-and-count label ("&lt; 500"), verbatim.</summary>
            public static string TargetLabel(object job)
            {
                return Get(targetLabelGetter, Trigger(job), "TargetLabel") as string ?? "";
            }

            /// <summary>
            /// Opens the mod's threshold details window as clicking the threshold label does:
            /// write the forestry sync field its delegate carries, then hand off to the shared body.
            /// </summary>
            public static void OpenThresholdDetails(object job)
            {
                // MUTATION-C: the field write is the delegate ManagerTab_Forestry.cs:305-308 hands the
                // shared threshold click body (Trigger_Threshold.DrawTriggerConfig,
                // Trigger_Threshold.cs:501-505): for a forestry job, onOpenFilterDetails is
                // `job.Sync = Utilities.SyncDirection.FilterToAllowed`.
                Threshold.OpenDetails(job,
                    () => SetField(syncField, job, filterToAllowedSync, "OpenThresholdDetails"));
            }

            public static bool SyncFilterAndAllowed(object job)
            {
                return FieldFlag(syncFilterAndAllowedField, job, "SyncFilterAndAllowed");
            }

            /// <summary>Writes the public field the mod's own synchronize-threshold toggle passes by ref (ManagerTab_Forestry.cs:312-318).</summary>
            public static void SetSyncFilterAndAllowed(object job, bool value)
            {
                SetField(syncFilterAndAllowedField, job, value, "SetSyncFilterAndAllowed");
            }

            // Logging area and saplings (Logging only).

            public static Area LoggingArea(object job)
            {
                if (!ready || job == null || loggingAreaField == null)
                {
                    return null;
                }
                try
                {
                    return loggingAreaField.GetValue(job) as Area;
                }
                catch (Exception ex)
                {
                    Fail("LoggingArea", ex);
                    return null;
                }
            }

            /// <summary>Writes the field the area strip itself writes by ref (AreaAllowedGUI.DoAreaSelector, AreaAllowedGUI.cs:271, via ManagerTab_Forestry.cs:227-235).</summary>
            public static void SetLoggingArea(object job, Area area)
            {
                SetField(loggingAreaField, job, area, "SetLoggingArea");
            }

            public static bool InvertLoggingArea(object job)
            {
                return FieldFlag(invertLoggingAreaField, job, "InvertLoggingArea");
            }

            /// <summary>Writes the storage the invert-area toggle passes by ref (ManagerTab_Forestry.cs:227-235).</summary>
            public static void SetInvertLoggingArea(object job, bool value)
            {
                SetField(invertLoggingAreaField, job, value, "SetInvertLoggingArea");
            }

            /// <summary>
            /// Whether the job cuts only fully matured trees: the state the toggle reads, already
            /// inverted, since the field means the opposite of what the row says
            /// (ManagerTab_Forestry.cs:215-224).
            /// </summary>
            public static bool OnlyFullyMaturedTrees(object job)
            {
                return !FieldFlag(allowSaplingsField, job, "AllowSaplings");
            }

            /// <summary>Writes the storage the toggle passes by ref, un-inverting back to the field's own sense.</summary>
            public static void SetOnlyFullyMaturedTrees(object job, bool value)
            {
                SetField(allowSaplingsField, job, !value, "SetOnlyFullyMaturedTrees");
            }

            // Clear areas (ClearArea only).

            /// <summary>The job's own pass at pruning deleted areas from its clear-area set (ManagerTab_Forestry.cs Refresh, via UpdateClearAreas).</summary>
            public static void UpdateClearAreas(object job)
            {
                Call(updateClearAreasMethod, job, null, "UpdateClearAreas");
            }

            public static bool IsClearAreaAllowed(object job, Area area)
            {
                HashSet<Area> areas = ClearAreasSet(job);
                return areas != null && area != null && areas.Contains(area);
            }

            /// <summary>The exact write AreaAllowedGUI.DoAllowedAreaSelectorsMC performs per cell it draws (AreaAllowedGUI.cs:196-211); the drag-select widget offers no per-area click primitive to invoke instead.</summary>
            public static void SetClearAreaAllowed(object job, Area area, bool allow)
            {
                HashSet<Area> areas = ClearAreasSet(job);
                if (areas == null || area == null)
                {
                    return;
                }
                try
                {
                    // MUTATION-C: see the method summary; this is the widget's own per-cell body.
                    if (allow)
                    {
                        areas.Add(area);
                    }
                    else
                    {
                        areas.Remove(area);
                    }
                }
                catch (Exception ex)
                {
                    Fail("SetClearAreaAllowed", ex);
                }
            }

            private static HashSet<Area> ClearAreasSet(object job)
            {
                if (!ready || job == null || clearAreasField == null)
                {
                    return null;
                }
                try
                {
                    return clearAreasField.GetValue(job) as HashSet<Area>;
                }
                catch (Exception ex)
                {
                    Fail("ClearAreas", ex);
                    return null;
                }
            }

            // Trees.

            /// <summary>Every plant the job offers for its current job type, in the mod's own order.</summary>
            public static List<ThingDef> AllPlants(object job)
            {
                var plants = new List<ThingDef>();
                var items = Get(allPlantsGetter, job, "AllPlants") as IEnumerable;
                if (items == null)
                {
                    return plants;
                }
                try
                {
                    foreach (object item in items)
                    {
                        if (item is ThingDef plant)
                        {
                            plants.Add(plant);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Fail("AllPlants", ex);
                }
                return plants;
            }

            public static bool IsTreeAllowed(object job, ThingDef tree)
            {
                if (!ready || job == null || tree == null || allowedTreesField == null)
                {
                    return false;
                }
                try
                {
                    var allowed = allowedTreesField.GetValue(job) as ICollection<ThingDef>;
                    return allowed != null && allowed.Contains(tree);
                }
                catch (Exception ex)
                {
                    Fail("IsTreeAllowed", ex);
                    return false;
                }
            }

            /// <summary>The exact call every tree toggle and shortcut hands to the mod's own widgets (ManagerTab_Forestry.cs:345, 366, 379, 392, 405, 425, 440, 460, 479).</summary>
            public static void SetTreeAllowed(object job, ThingDef tree, bool allow)
            {
                if (tree == null)
                {
                    return;
                }
                Call(setTreeAllowedMethod, job, new object[] { tree, allow, true }, "SetTreeAllowed");
            }

            /// <summary>The refresh icon's own call (ManagerTab_Forestry.cs:124-127).</summary>
            public static void RefreshAllTrees(object job)
            {
                Call(refreshAllTreesMethod, job, null, "RefreshAllTrees");
            }

            public static bool PlantsLockedToMap(object job)
            {
                return Flag(plantsLockedGetter, job, "PlantsLockedToMap");
            }

            /// <summary>The padlock icon's own write (ManagerTab_Forestry.cs:135-146); the setter drops the cached plant list itself.</summary>
            public static void SetPlantsLockedToMap(object job, bool value)
            {
                SetProperty(plantsLockedSetter, job, value, "SetPlantsLockedToMap");
            }

            /// <summary>The mod's own hover text for a tree row, forwarded from ManagerTab_Foraging.GetPlantTooltip (ManagerTab_Forestry.cs:32).</summary>
            public static string PlantTooltip(ThingDef tree)
            {
                if (tree == null || plantTooltipMethod == null)
                {
                    return "";
                }
                return Call(plantTooltipMethod, null, new object[] { tree }, "PlantTooltip") as string ?? "";
            }

            // Plumbing. Gated on this block's own Ready, so a Forestry-only member rename never
            // disturbs the manager window's tab and job lists.

            private static object Trigger(object job)
            {
                return Get(triggerGetter, job, "TriggerThreshold");
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

            private static bool FieldFlag(FieldInfo field, object instance, string member)
            {
                if (!ready || field == null || instance == null)
                {
                    return false;
                }
                try
                {
                    return field.GetValue(instance) is bool value && value;
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                    return false;
                }
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
            /// Shared write primitive for the plain public field the mod's <c>ref</c>-parameter
            /// widgets write. Each wrapper above carries the citation for the draw call it
            /// reproduces; this helper adds no path of its own.
            /// </summary>
            private static void SetField(FieldInfo field, object instance, object value, string member)
            {
                if (!ready || field == null || instance == null)
                {
                    return;
                }
                try
                {
                    // MUTATION-C: the write the mod's own widget performs through its ref parameter --
                    // see each wrapper's cited draw call.
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
