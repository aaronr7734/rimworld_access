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
        /// Colony Manager Redux's Hunting job and tab, as far as the job's detail rows
        /// (<see cref="Shell.CmrHuntingDetails"/>) read and write it. <see cref="Ready"/> is
        /// separate from <see cref="CmrCompat.Ready"/> on purpose: the manager window's tab and job
        /// lists keep working when a mod update renames something only these rows touch, and the
        /// Hunting regions simply report no rows.
        ///
        /// <c>ManagerJob_Hunting</c> and <c>ManagerTab_Hunting</c> are internal to the mod, so
        /// callers pass the tab and job as <c>object</c> and read them through the typed wrappers.
        ///
        /// MUTATION VEHICLES. Every write is one of the mod's own widget bodies. Property setters
        /// its toggle delegates call and methods it calls verbatim are vehicles A/B and carry the
        /// mod's own bookkeeping (the target-resource setter re-reads the animal list, the padlock
        /// setter drops the cached one, the meat setters flip the sync direction and write the
        /// threshold filter). The remaining toggles are drawn by handing
        /// <c>Utilities.DrawToggle</c> a <c>ref bool</c>, so the toggle's whole mutation IS a write
        /// to that storage; all of those go through <see cref="SetField"/>, whose single write site
        /// carries the MUTATION-C marker, and each wrapper cites the draw call it reproduces.
        /// </summary>
        internal static class Hunting
        {
            private static readonly Type tabType;
            private static readonly Type targetResourceType;

            private static readonly MethodInfo targetResourceGetter;
            private static readonly MethodInfo targetResourceSetter;
            private static readonly MethodInfo triggerGetter;

            private static readonly FieldInfo huntingGroundsField;
            private static readonly FieldInfo invertHuntingGroundsField;
            private static readonly FieldInfo syncFilterAndAllowedField;
            private static readonly FieldInfo syncField;
            private static readonly FieldInfo unforbidCorpsesField;
            private static readonly FieldInfo unforbidAllCorpsesField;
            private static readonly FieldInfo unforbidHumanCorpsesField;

            private static readonly MethodInfo animalsLockedGetter;
            private static readonly MethodInfo animalsLockedSetter;
            private static readonly MethodInfo allAnimalsGetter;
            private static readonly MethodInfo allowedAnimalsGetter;
            private static readonly MethodInfo setAnimalAllowedMethod;
            private static readonly MethodInfo refreshAllAnimalsMethod;
            private static readonly MethodInfo animalTooltipMethod;

            private static readonly MethodInfo allowAllHumanLikeMeatGetter;
            private static readonly MethodInfo allowNoneHumanLikeMeatGetter;
            private static readonly MethodInfo allowHumanLikeMeatSetter;
            private static readonly MethodInfo allowInsectMeatSetter;
            private static readonly MethodInfo allowTwistedMeatSetter;

            private static readonly MethodInfo corpsesCacheMethod;
            private static readonly MethodInfo designationsCacheMethod;
            private static readonly MethodInfo cacheUpdateMethod;
            private static readonly MethodInfo cacheValueGetter;

            private static readonly MethodInfo targetLabelGetter;
            private static readonly MethodInfo currentCountMethod;
            private static readonly MethodInfo thresholdFilterGetter;

            private static readonly object meatTargetResource;
            private static readonly object filterToAllowedSync;

            private static readonly bool ready;

            static Hunting()
            {
                var surface = new ReflectionSurface("CmrCompat.Hunting");

                Type jobType = surface.Type("ColonyManagerRedux.Managers.ManagerJob_Hunting");
                tabType = surface.Type("ColonyManagerRedux.Managers.ManagerTab_Hunting");
                Type triggerType = surface.Type("ColonyManagerRedux.Trigger_Threshold");

                PropertyInfo targetResourceProperty = surface.Property(jobType, "TargetResource");
                targetResourceGetter = Getter(targetResourceProperty);
                targetResourceSetter = surface.Required("ManagerJob_Hunting.TargetResource setter",
                    Setter(targetResourceProperty));
                targetResourceType = targetResourceGetter != null ? targetResourceGetter.ReturnType : null;
                meatTargetResource = surface.EnumValue(targetResourceType, "Meat");

                triggerGetter = Getter(surface.Property(jobType, "TriggerThreshold"));

                huntingGroundsField = surface.Field(jobType, "HuntingGrounds");
                invertHuntingGroundsField = surface.Field(jobType, "InvertHuntingGrounds");
                syncFilterAndAllowedField = surface.Field(jobType, "SyncFilterAndAllowed");
                syncField = surface.Field(jobType, "Sync");
                filterToAllowedSync = surface.EnumValue(syncField != null ? syncField.FieldType : null, "FilterToAllowed");

                // Backing fields for three ref-bool toggles — the storage the mod's own DrawToggle
                // writes through. See the class remarks.
                unforbidCorpsesField = surface.Field(jobType, "_unforbidCorpses");
                unforbidAllCorpsesField = surface.Field(jobType, "_unforbidAllCorpses");
                unforbidHumanCorpsesField = surface.Field(jobType, "_unforbidHumanCorpses");

                PropertyInfo animalsLockedProperty = surface.Property(jobType, "AnimalsLockedToMap");
                animalsLockedGetter = Getter(animalsLockedProperty);
                animalsLockedSetter = surface.Required("ManagerJob_Hunting.AnimalsLockedToMap setter",
                    Setter(animalsLockedProperty));
                allAnimalsGetter = Getter(surface.Property(jobType, "AllAnimals"));
                allowedAnimalsGetter = Getter(surface.Property(jobType, "AllowedAnimals"));
                setAnimalAllowedMethod = surface.Method(jobType, "SetAnimalAllowed",
                    new[] { typeof(PawnKindDef), typeof(bool), typeof(bool) });
                refreshAllAnimalsMethod = surface.Method(jobType, "RefreshAllAnimals", new Type[0]);
                if (targetResourceType != null)
                {
                    animalTooltipMethod = surface.Method(tabType, "GetAnimalKindTooltip",
                        new[] { typeof(PawnKindDef), targetResourceType });
                }

                allowAllHumanLikeMeatGetter = Getter(surface.Property(jobType, "AllowAllHumanLikeMeat"));
                allowNoneHumanLikeMeatGetter = Getter(surface.Property(jobType, "AllowNoneHumanLikeMeat"));
                allowHumanLikeMeatSetter = surface.Required("ManagerJob_Hunting.AllowHumanLikeMeat setter",
                    Setter(surface.Property(jobType, "AllowHumanLikeMeat")));
                allowInsectMeatSetter = surface.Required("ManagerJob_Hunting.AllowInsectMeat setter",
                    Setter(surface.Property(jobType, "AllowInsectMeat")));
                allowTwistedMeatSetter = surface.Required("ManagerJob_Hunting.AllowTwistedMeat setter",
                    Setter(surface.Property(jobType, "AllowTwistedMeat")));

                corpsesCacheMethod = surface.Method(jobType, "GetYieldInCorpsesCache", new Type[0]);
                designationsCacheMethod = surface.Method(jobType, "GetYieldInDesignationsCache", new Type[0]);
                // The cache is a closed generic the mod never names publicly; its accessor's return
                // type is the only handle that survives a version change.
                Type cacheType = corpsesCacheMethod != null ? corpsesCacheMethod.ReturnType : null;
                cacheUpdateMethod = surface.Method(cacheType, "DoUpdateIfNeeded", new[] { typeof(bool) });
                cacheValueGetter = Getter(surface.Property(cacheType, "Value"));

                targetLabelGetter = Getter(surface.Property(triggerType, "TargetLabel"));
                currentCountMethod = surface.Method(triggerType, "GetCurrentCount", new[] { typeof(bool) });
                thresholdFilterGetter = Getter(surface.Property(triggerType, "ThresholdFilter"));

                ready = surface.Ready;
            }

            /// <summary>True when every member the Hunting detail rows read or write resolved.</summary>
            public static bool Ready
            {
                get { return ready; }
            }

            /// <summary>Whether this tab is the mod's Hunting tab, by the type resolved above.</summary>
            public static bool HandlesTab(object tab)
            {
                return ready && tab != null && tabType != null && tabType.IsInstanceOfType(tab);
            }

            /// <summary>The enum's own values, in declaration order — the order the mod's radio pair draws in.</summary>
            public static List<object> TargetResourceValues()
            {
                var values = new List<object>();
                if (!ready || targetResourceType == null)
                {
                    return values;
                }
                foreach (object value in Enum.GetValues(targetResourceType))
                {
                    values.Add(value);
                }
                return values;
            }

            /// <summary>The enum member's own name, for composing the mod's own label/tooltip keys.</summary>
            public static string TargetResourceName(object value)
            {
                return value == null ? "" : value.ToString();
            }

            public static object TargetResource(object job)
            {
                return Get(targetResourceGetter, job, "TargetResource");
            }

            public static bool TargetsMeat(object job)
            {
                object current = TargetResource(job);
                return current != null && meatTargetResource != null && current.Equals(meatTargetResource);
            }

            /// <summary>Writes the target resource through the radio pair's own delegate target; the setter re-reads the animal list itself.</summary>
            public static void SetTargetResource(object job, object value)
            {
                SetProperty(targetResourceSetter, job, value, "SetTargetResource");
            }

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

            /// <summary>Expected yield from butchering corpses: kick the multi-tick cache if it is due, then take the value it last finished.</summary>
            public static int YieldInCorpses(object job)
            {
                return CachedYield(corpsesCacheMethod, job, "YieldInCorpses");
            }

            /// <summary>The job's expected yield from its outstanding hunt designations.</summary>
            public static int YieldInDesignations(object job)
            {
                return CachedYield(designationsCacheMethod, job, "YieldInDesignations");
            }

            private static int CachedYield(MethodInfo accessor, object job, string member)
            {
                if (!ready || job == null || accessor == null || cacheUpdateMethod == null)
                {
                    return 0;
                }
                object cache = Call(accessor, job, null, member);
                if (cache == null)
                {
                    return 0;
                }
                Call(cacheUpdateMethod, cache, new object[] { false }, member);
                object value = Get(cacheValueGetter, cache, member);
                return value is int count ? count : 0;
            }

            /// <summary>The trigger's own operator-and-count label ("&lt; 500"), verbatim.</summary>
            public static string TargetLabel(object job)
            {
                return Get(targetLabelGetter, Trigger(job), "TargetLabel") as string ?? "";
            }

            /// <summary>Opens the mod's threshold details window exactly as clicking the threshold label does.</summary>
            public static void OpenThresholdDetails(object job)
            {
                // MUTATION-C: the field write is the delegate ManagerTab_Hunting.cs:491-494 hands the
                // shared threshold click body (Trigger_Threshold.DrawTriggerConfig,
                // Trigger_Threshold.cs:501-505): for a hunting job, onOpenFilterDetails is
                // `job.Sync = Utilities.SyncDirection.FilterToAllowed`.
                Threshold.OpenDetails(job,
                    () => SetField(syncField, job, filterToAllowedSync, "OpenThresholdDetails"));
            }

            public static bool SyncFilterAndAllowed(object job)
            {
                return FieldFlag(syncFilterAndAllowedField, job, "SyncFilterAndAllowed");
            }

            /// <summary>Writes the public field the mod's synchronize-threshold toggle passes by ref.</summary>
            public static void SetSyncFilterAndAllowed(object job, bool value)
            {
                SetField(syncFilterAndAllowedField, job, value, "SetSyncFilterAndAllowed");
            }

            // The meat shortcuts below are present only while the job targets meat.

            public static bool AllowsAllHumanLikeMeat(object job)
            {
                return Flag(allowAllHumanLikeMeatGetter, job, "AllowsAllHumanLikeMeat");
            }

            public static bool AllowsNoHumanLikeMeat(object job)
            {
                return Flag(allowNoneHumanLikeMeatGetter, job, "AllowsNoHumanLikeMeat");
            }

            /// <summary>The mod's own shortcut setter: flips the sync direction, then writes every humanlike meat def into the threshold filter.</summary>
            public static void SetHumanLikeMeat(object job, bool value)
            {
                SetProperty(allowHumanLikeMeatSetter, job, value, "SetHumanLikeMeat");
            }

            /// <summary>Whether insect meat currently counts toward the threshold — the state the mod's own toggle reads.</summary>
            public static bool AllowsInsectMeat(object job)
            {
                return FilterAllows(job, MegaspiderMeat);
            }

            public static void SetInsectMeat(object job, bool value)
            {
                SetProperty(allowInsectMeatSetter, job, value, "SetInsectMeat");
            }

            /// <summary>False when Anomaly is inactive: the def only ships with that expansion.</summary>
            public static bool TwistedMeatExists
            {
                get { return ready && TwistedMeat != null; }
            }

            public static bool AllowsTwistedMeat(object job)
            {
                return FilterAllows(job, TwistedMeat);
            }

            public static void SetTwistedMeat(object job, bool value)
            {
                SetProperty(allowTwistedMeatSetter, job, value, "SetTwistedMeat");
            }

            // Both of the mod's assemblies declare a ManagerThingDefOf in the SAME namespace, so
            // its DefOf holders cannot be resolved unambiguously by name; these are plain
            // vanilla/Anomaly defs, so the def database is the stable handle.
            private static ThingDef MegaspiderMeat
            {
                get { return DefDatabase<ThingDef>.GetNamedSilentFail("Meat_Megaspider"); }
            }

            private static ThingDef TwistedMeat
            {
                get { return DefDatabase<ThingDef>.GetNamedSilentFail("Meat_Twisted"); }
            }

            private static bool FilterAllows(object job, ThingDef def)
            {
                ThingFilter filter = ThresholdFilter(Trigger(job));
                return filter != null && def != null && filter.Allows(def);
            }

            public static bool UnforbidCorpses(object job)
            {
                return FieldFlag(unforbidCorpsesField, job, "UnforbidCorpses");
            }

            /// <summary>Writes the storage the unforbid-corpses toggle passes by ref.</summary>
            public static void SetUnforbidCorpses(object job, bool value)
            {
                SetField(unforbidCorpsesField, job, value, "SetUnforbidCorpses");
            }

            public static bool UnforbidAllCorpses(object job)
            {
                return FieldFlag(unforbidAllCorpsesField, job, "UnforbidAllCorpses");
            }

            /// <summary>Writes the storage the child unforbid-all-corpses toggle passes by ref.</summary>
            public static void SetUnforbidAllCorpses(object job, bool value)
            {
                SetField(unforbidAllCorpsesField, job, value, "SetUnforbidAllCorpses");
            }

            public static bool UnforbidHumanCorpses(object job)
            {
                return FieldFlag(unforbidHumanCorpsesField, job, "UnforbidHumanCorpses");
            }

            public static void SetUnforbidHumanCorpses(object job, bool value)
            {
                SetField(unforbidHumanCorpsesField, job, value, "SetUnforbidHumanCorpses");
            }

            public static Area HuntingGrounds(object job)
            {
                if (!ready || job == null || huntingGroundsField == null)
                {
                    return null;
                }
                try
                {
                    return huntingGroundsField.GetValue(job) as Area;
                }
                catch (Exception ex)
                {
                    Fail("HuntingGrounds", ex);
                    return null;
                }
            }

            /// <summary>Writes the field the area strip itself writes by ref (AreaAllowedGUI.DoAreaSelector).</summary>
            public static void SetHuntingGrounds(object job, Area area)
            {
                SetField(huntingGroundsField, job, area, "SetHuntingGrounds");
            }

            public static bool InvertHuntingGrounds(object job)
            {
                return FieldFlag(invertHuntingGroundsField, job, "InvertHuntingGrounds");
            }

            /// <summary>Writes the storage the invert-area toggle passes by ref.</summary>
            public static void SetInvertHuntingGrounds(object job, bool value)
            {
                SetField(invertHuntingGroundsField, job, value, "SetInvertHuntingGrounds");
            }

            /// <summary>Every animal kind the job offers, in the mod's own order.</summary>
            public static List<PawnKindDef> AllAnimals(object job)
            {
                var animals = new List<PawnKindDef>();
                var items = Get(allAnimalsGetter, job, "AllAnimals") as IEnumerable;
                if (items == null)
                {
                    return animals;
                }
                try
                {
                    foreach (object item in items)
                    {
                        if (item is PawnKindDef animal)
                        {
                            animals.Add(animal);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Fail("AllAnimals", ex);
                }
                return animals;
            }

            public static bool IsAnimalAllowed(object job, PawnKindDef animal)
            {
                var allowed = Get(allowedAnimalsGetter, job, "IsAnimalAllowed") as ICollection<PawnKindDef>;
                return allowed != null && animal != null && allowed.Contains(animal);
            }

            /// <summary>The exact call every animal toggle and shortcut hands to the mod's own widgets.</summary>
            public static void SetAnimalAllowed(object job, PawnKindDef animal, bool allow)
            {
                if (animal == null)
                {
                    return;
                }
                Call(setAnimalAllowedMethod, job, new object[] { animal, allow, true }, "SetAnimalAllowed");
            }

            /// <summary>The refresh icon's own call.</summary>
            public static void RefreshAllAnimals(object job)
            {
                Call(refreshAllAnimalsMethod, job, null, "RefreshAllAnimals");
            }

            public static bool AnimalsLockedToMap(object job)
            {
                return Flag(animalsLockedGetter, job, "AnimalsLockedToMap");
            }

            /// <summary>The padlock icon's own write; the setter drops the cached animal list itself.</summary>
            public static void SetAnimalsLockedToMap(object job, bool value)
            {
                SetProperty(animalsLockedSetter, job, value, "SetAnimalsLockedToMap");
            }

            /// <summary>The mod's own hover text for an animal row: description, yields for the job's target resource, aggressiveness.</summary>
            public static string AnimalTooltip(object job, PawnKindDef animal)
            {
                if (animal == null || animalTooltipMethod == null)
                {
                    return "";
                }
                object resource = TargetResource(job);
                if (resource == null)
                {
                    return "";
                }
                return Call(animalTooltipMethod, null, new[] { animal, resource }, "AnimalTooltip") as string ?? "";
            }

            // The plumbing below is gated on this block's own Ready, so a Hunting-only member
            // rename never disturbs the manager window's tab and job lists.

            private static object Trigger(object job)
            {
                return Get(triggerGetter, job, "TriggerThreshold");
            }

            private static ThingFilter ThresholdFilter(object trigger)
            {
                return Get(thresholdFilterGetter, trigger, "ThresholdFilter") as ThingFilter;
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
            /// Shared write primitive for the storage the mod's own <c>ref</c>-parameter widgets
            /// write. Each public wrapper above cites the draw call it reproduces.
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
