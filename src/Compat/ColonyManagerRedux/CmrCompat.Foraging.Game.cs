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
        /// Colony Manager Redux's Foraging job and tab, as far as
        /// <see cref="Shell.CmrForagingDetails"/> reads and writes them. Kept separate from
        /// <see cref="CmrCompat.Ready"/> so a rename touching only these rows cannot disturb the
        /// manager window's tab and job lists.
        ///
        /// <c>ManagerJob_Foraging</c> and <c>ManagerTab_Foraging</c> are internal to the mod and
        /// resolved by name once; callers pass them as <c>object</c>.
        ///
        /// The reachability and path-based-distance toggles are NOT bound here: both are
        /// <c>ref bool</c> properties over private fields on the mod's <c>ManagerJob</c> base, so
        /// callers reach them — and the job's map — through <see cref="JobBase"/>. The shared
        /// <c>Trigger_Threshold</c> members go through <see cref="Threshold"/>'s job-taking
        /// overloads.
        ///
        /// MUTATION VEHICLES — every write here is one of the mod's own widget bodies:
        /// <list type="bullet">
        /// <item>The property setter its toggle delegate calls (<c>PlantsLockedToMap</c>) and the
        /// methods it calls verbatim (<c>SetPlantAllowed</c>, <c>RefreshAllPlants</c>) are doctrine
        /// A/B; the padlock setter drops the cached plant list itself.</item>
        /// <item>The remaining toggles and the area strip are drawn by handing
        /// <c>Utilities.DrawToggle</c> or <c>AreaAllowedGUI.DoAllowedAreaSelectorsWithInvert</c> a
        /// <c>ref</c> parameter, so the widget's whole mutation IS a write to that storage — a
        /// plain public field with no setter method in every case. All go through
        /// <see cref="SetField"/>, whose single write site carries the MUTATION-C marker; each
        /// wrapper cites the draw call it reproduces.</item>
        /// <item><see cref="OpenThresholdDetails"/> writes the foraging-specific sync field, then
        /// hands off to <see cref="Threshold.OpenDetails"/>.</item>
        /// </list>
        /// </summary>
        internal static class Foraging
        {
            private static readonly Type tabType;

            private static readonly MethodInfo triggerGetter;

            private static readonly FieldInfo allowedPlantsField;
            private static readonly FieldInfo foragingAreaField;
            private static readonly FieldInfo invertForagingAreaField;
            private static readonly FieldInfo forceFullyMatureField;
            private static readonly FieldInfo syncFilterAndAllowedField;
            private static readonly FieldInfo syncField;

            private static readonly MethodInfo plantsLockedGetter;
            private static readonly MethodInfo plantsLockedSetter;
            private static readonly MethodInfo allPlantsGetter;
            private static readonly MethodInfo refreshAllPlantsMethod;
            private static readonly MethodInfo setPlantAllowedMethod;
            private static readonly MethodInfo plantTooltipMethod;

            private static readonly MethodInfo cachedDesignatedCountGetter;
            private static readonly MethodInfo cacheUpdateMethod;
            private static readonly MethodInfo cacheValueGetter;

            private static readonly MethodInfo targetLabelGetter;
            private static readonly MethodInfo currentCountMethod;

            private static readonly object filterToAllowedSync;

            private static readonly bool ready;

            static Foraging()
            {
                var surface = new ReflectionSurface("CmrCompat.Foraging");

                Type jobType = surface.Type("ColonyManagerRedux.Managers.ManagerJob_Foraging");
                tabType = surface.Type("ColonyManagerRedux.Managers.ManagerTab_Foraging");
                Type triggerType = surface.Type("ColonyManagerRedux.Trigger_Threshold");

                triggerGetter = Getter(surface.Property(jobType, "TriggerThreshold"));

                allowedPlantsField = surface.Field(jobType, "AllowedPlants");
                foragingAreaField = surface.Field(jobType, "ForagingArea");
                invertForagingAreaField = surface.Field(jobType, "InvertForagingArea");
                forceFullyMatureField = surface.Field(jobType, "ForceFullyMature");
                syncFilterAndAllowedField = surface.Field(jobType, "SyncFilterAndAllowed");
                syncField = surface.Field(jobType, "Sync");
                filterToAllowedSync = surface.EnumValue(syncField != null ? syncField.FieldType : null, "FilterToAllowed");

                PropertyInfo plantsLockedProperty = surface.Property(jobType, "PlantsLockedToMap");
                plantsLockedGetter = Getter(plantsLockedProperty);
                plantsLockedSetter = surface.Required("ManagerJob_Foraging.PlantsLockedToMap setter",
                    Setter(plantsLockedProperty));
                allPlantsGetter = Getter(surface.Property(jobType, "AllPlants"));
                refreshAllPlantsMethod = surface.Method(jobType, "RefreshAllPlants", new Type[0]);
                setPlantAllowedMethod = surface.Method(jobType, "SetPlantAllowed",
                    new[] { typeof(ThingDef), typeof(bool), typeof(bool) });
                plantTooltipMethod = surface.Method(tabType, "GetPlantTooltip", new[] { typeof(ThingDef) });

                cachedDesignatedCountGetter = Getter(surface.Property(jobType, "CachedCurrentDesignatedCount"));
                // The cache is a closed generic the mod never names publicly, so its accessor's
                // return type is the only handle that survives a version change.
                Type cacheType = cachedDesignatedCountGetter != null ? cachedDesignatedCountGetter.ReturnType : null;
                cacheUpdateMethod = surface.Method(cacheType, "DoUpdateIfNeeded", new[] { typeof(bool) });
                cacheValueGetter = Getter(surface.Property(cacheType, "Value"));

                targetLabelGetter = Getter(surface.Property(triggerType, "TargetLabel"));
                currentCountMethod = surface.Method(triggerType, "GetCurrentCount", new[] { typeof(bool) });

                ready = surface.Ready;
            }

            /// <summary>True when every member the Foraging detail rows read or write resolved.</summary>
            public static bool Ready
            {
                get { return ready; }
            }

            /// <summary>Whether this tab is the mod's Foraging tab, by the type resolved above.</summary>
            public static bool HandlesTab(object tab)
            {
                return ready && tab != null && tabType != null && tabType.IsInstanceOfType(tab);
            }

            // Threshold.

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

            /// <summary>The job's outstanding designated yield as the threshold section reads it: kick the multi-tick cache if due, then take its last finished value (:257-258).</summary>
            public static int DesignatedCount(object job)
            {
                if (!ready || job == null || cachedDesignatedCountGetter == null || cacheUpdateMethod == null)
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
            /// Opens the mod's threshold details window as clicking the threshold label does: write
            /// the foraging-specific sync field the click's delegate carries, then hand off to the
            /// shared threshold body.
            /// </summary>
            public static void OpenThresholdDetails(object job)
            {
                // MUTATION-C: the field write is the delegate ManagerTab_Foraging.cs:277 hands the
                // shared threshold click body (Trigger_Threshold.DrawTriggerConfig,
                // Trigger_Threshold.cs:501-505): for a foraging job, onOpenFilterDetails is
                // `job.Sync = Utilities.SyncDirection.FilterToAllowed`.
                Threshold.OpenDetails(job,
                    () => SetField(syncField, job, filterToAllowedSync, "OpenThresholdDetails"));
            }

            public static bool SyncFilterAndAllowed(object job)
            {
                return FieldFlag(syncFilterAndAllowedField, job, "SyncFilterAndAllowed");
            }

            /// <summary>Writes the public field the mod's own synchronize-threshold toggle passes by ref (ManagerTab_Foraging.cs:281-287).</summary>
            public static void SetSyncFilterAndAllowed(object job, bool value)
            {
                SetField(syncFilterAndAllowedField, job, value, "SetSyncFilterAndAllowed");
            }

            // Foraging area.

            public static Area ForagingArea(object job)
            {
                if (!ready || job == null || foragingAreaField == null)
                {
                    return null;
                }
                try
                {
                    return foragingAreaField.GetValue(job) as Area;
                }
                catch (Exception ex)
                {
                    Fail("ForagingArea", ex);
                    return null;
                }
            }

            /// <summary>Writes the field the area strip itself writes by ref (AreaAllowedGUI.DoAllowedAreaSelectorsWithInvert, via ManagerTab_Foraging.cs:143-151).</summary>
            public static void SetForagingArea(object job, Area area)
            {
                SetField(foragingAreaField, job, area, "SetForagingArea");
            }

            public static bool InvertForagingArea(object job)
            {
                return FieldFlag(invertForagingAreaField, job, "InvertForagingArea");
            }

            /// <summary>Writes the storage the same area strip's invert cell passes by ref (ManagerTab_Foraging.cs:143-151).</summary>
            public static void SetInvertForagingArea(object job, bool value)
            {
                SetField(invertForagingAreaField, job, value, "SetInvertForagingArea");
            }

            // Fully mature.

            public static bool ForceFullyMature(object job)
            {
                return FieldFlag(forceFullyMatureField, job, "ForceFullyMature");
            }

            /// <summary>Writes the storage the "only fully matured plants" toggle passes by ref (ManagerTab_Foraging.cs:153-164).</summary>
            public static void SetForceFullyMature(object job, bool value)
            {
                SetField(forceFullyMatureField, job, value, "SetForceFullyMature");
            }

            // Plants.

            /// <summary>Every plant the job offers, in the mod's own order.</summary>
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

            public static bool IsPlantAllowed(object job, ThingDef plant)
            {
                if (!ready || job == null || allowedPlantsField == null || plant == null)
                {
                    return false;
                }
                try
                {
                    var allowed = allowedPlantsField.GetValue(job) as ICollection<ThingDef>;
                    return allowed != null && allowed.Contains(plant);
                }
                catch (Exception ex)
                {
                    Fail("IsPlantAllowed", ex);
                    return false;
                }
            }

            /// <summary>The exact call every plant toggle and shortcut hands to the mod's own widgets (ManagerTab_Foraging.cs:175, 217, 232, 245).</summary>
            public static void SetPlantAllowed(object job, ThingDef plant, bool allow)
            {
                if (plant == null)
                {
                    return;
                }
                Call(setPlantAllowedMethod, job, new object[] { plant, allow, true }, "SetPlantAllowed");
            }

            /// <summary>The refresh icon's own call (ManagerTab_Foraging.cs:81-84).</summary>
            public static void RefreshAllPlants(object job)
            {
                Call(refreshAllPlantsMethod, job, null, "RefreshAllPlants");
            }

            public static bool PlantsLockedToMap(object job)
            {
                return Flag(plantsLockedGetter, job, "PlantsLockedToMap");
            }

            /// <summary>The padlock icon's own write (ManagerTab_Foraging.cs:92-103); the setter drops the cached plant list itself.</summary>
            public static void SetPlantsLockedToMap(object job, bool value)
            {
                SetProperty(plantsLockedSetter, job, value, "SetPlantsLockedToMap");
            }

            /// <summary>The mod's own hover text for a plant row: description and expected yield (ManagerTab_Foraging.cs:182-198).</summary>
            public static string PlantTooltip(ThingDef plant)
            {
                if (plant == null || plantTooltipMethod == null)
                {
                    return "";
                }
                return Call(plantTooltipMethod, null, new object[] { plant }, "PlantTooltip") as string ?? "";
            }

            // Plumbing, gated on this block's own Ready so a Foraging-only rename disturbs nothing
            // else.

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
            /// Shared write primitive for the plain public fields the mod's <c>ref</c>-parameter
            /// widgets write. Each wrapper above cites the draw call it reproduces.
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
                    // see this class's remarks and each wrapper's cited draw call. None of these fields
                    // has a gated setter to invoke instead.
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
