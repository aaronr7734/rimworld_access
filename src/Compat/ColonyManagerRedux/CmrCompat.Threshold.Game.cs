using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    internal static partial class CmrCompat
    {
        /// <summary>
        /// Colony Manager Redux's threshold trigger and the details window it opens, as far as
        /// <see cref="Shell.CmrThresholdWindowScope"/> reads and writes them. It has its own
        /// readiness gate because every threshold job opens this one window, so a rename here
        /// must disturb neither the manager window's lists nor the Hunting detail rows.
        /// <see cref="Hunting"/>'s threshold members resolve on the Hunting job type and take a
        /// job; every member here resolves on the trigger base and takes the trigger the window
        /// handed over, which is what makes this surface serve every manager's threshold job.
        /// The window keeps that trigger in a private readonly field, the one non-property
        /// binding here. Every write rides the storage the window's own widget writes: the
        /// <c>Op</c> setter behind each float-menu option (offered only for an operator
        /// <see cref="SupportsOp"/> accepts), the clamping <c>TargetCount</c> setter plus the
        /// maximum raise the count field performs, and the public <c>Stockpile</c> setter that
        /// assigns the field the stockpile strip mutates by reference.
        /// </summary>
        internal static class Threshold
        {
            /// <summary>One supported comparison operator: the enum value, and the mod's own label key for it.</summary>
            private sealed class OpEntry
            {
                public readonly object Value;
                public readonly string LabelKey;

                public OpEntry(object value, string labelKey)
                {
                    Value = value;
                    LabelKey = labelKey;
                }
            }

            private static readonly Type windowType;

            private static readonly FieldInfo windowTriggerField;

            private static readonly MethodInfo thresholdFilterGetter;
            private static readonly MethodInfo parentFilterGetter;
            private static readonly MethodInfo opGetter;
            private static readonly MethodInfo opSetter;
            private static readonly MethodInfo supportsOpMethod;
            private static readonly MethodInfo targetCountGetter;
            private static readonly MethodInfo targetCountSetter;
            private static readonly MethodInfo maxUpperThresholdGetter;
            private static readonly MethodInfo maxUpperThresholdSetter;
            private static readonly MethodInfo stockpileGetter;
            private static readonly MethodInfo stockpileSetter;
            private static readonly MethodInfo triggerJobGetter;
            private static readonly MethodInfo jobManagerGetter;

            // The Job* members below; bound separately because CmrThresholdWindowScope never
            // touches them.
            private static readonly MethodInfo allowAnyThresholdGetter;
            private static readonly MethodInfo allowAnyThresholdSetter;
            private static readonly MethodInfo allowAnyThresholdChangedGetter;
            private static readonly MethodInfo countAllOnMapGetter;
            private static readonly MethodInfo countAllOnMapSetter;
            private static readonly MethodInfo detailsWindowGetter;

            /// <summary>
            /// The four operators in the window's own float-menu order, which is not the enum's
            /// declaration order, each with the label key the window gives it. Left/Right on the
            /// operator row walks this same order.
            /// </summary>
            private static readonly List<OpEntry> ops = new List<OpEntry>();

            private static readonly bool ready;

            static Threshold()
            {
                var surface = new ReflectionSurface("CmrCompat.Threshold");

                windowType = surface.Type("ColonyManagerRedux.WindowTriggerThresholdDetails");
                Type triggerType = surface.Type("ColonyManagerRedux.Trigger_Threshold");
                Type baseJobType = surface.Type("ColonyManagerRedux.ManagerJob");
                Type baseTriggerType = surface.Type("ColonyManagerRedux.Trigger");
                Type opsType = surface.Supplied("Trigger_Threshold.Ops",
                    triggerType != null ? triggerType.GetNestedType("Ops") : null);

                windowTriggerField = surface.Field(windowType, "_trigger");

                thresholdFilterGetter = Getter(surface.Property(triggerType, "ThresholdFilter"));
                parentFilterGetter = Getter(surface.Property(triggerType, "ParentFilter"));
                PropertyInfo opProperty = surface.Property(triggerType, "Op");
                opGetter = Getter(opProperty);
                opSetter = surface.Required("Trigger_Threshold.Op setter", Setter(opProperty));
                if (opsType != null)
                {
                    supportsOpMethod = surface.Method(triggerType, "SupportsOp", new[] { opsType });
                    AddOp(surface, opsType, "LowerThan", "ColonyManagerRedux.Threshold.LowerThan");
                    AddOp(surface, opsType, "Equals", "ColonyManagerRedux.Threshold.EqualTo");
                    AddOp(surface, opsType, "NotEquals", "ColonyManagerRedux.Threshold.NotEqualTo");
                    AddOp(surface, opsType, "HigherThan", "ColonyManagerRedux.Threshold.GreaterThan");
                }

                PropertyInfo targetCountProperty = surface.Property(triggerType, "TargetCount");
                targetCountGetter = Getter(targetCountProperty);
                targetCountSetter = surface.Required("Trigger_Threshold.TargetCount setter",
                    Setter(targetCountProperty));
                PropertyInfo maxUpperProperty = surface.Property(triggerType, "MaxUpperThreshold");
                maxUpperThresholdGetter = Getter(maxUpperProperty);
                maxUpperThresholdSetter = surface.Required("Trigger_Threshold.MaxUpperThreshold setter",
                    Setter(maxUpperProperty));
                PropertyInfo stockpileProperty = surface.Property(triggerType, "Stockpile");
                stockpileGetter = Getter(stockpileProperty);
                stockpileSetter = surface.Required("Trigger_Threshold.Stockpile setter",
                    Setter(stockpileProperty));

                triggerJobGetter = Getter(surface.Property(baseTriggerType, "Job"));
                jobManagerGetter = Getter(surface.Property(baseJobType, "Manager"));

                PropertyInfo allowAnyProperty = surface.Property(triggerType, "AllowAnyThreshold");
                allowAnyThresholdGetter = Getter(allowAnyProperty);
                allowAnyThresholdSetter = surface.Required("Trigger_Threshold.AllowAnyThreshold setter",
                    Setter(allowAnyProperty));
                allowAnyThresholdChangedGetter =
                    Getter(surface.Property(triggerType, "AllowAnyThresholdChanged"));
                PropertyInfo countAllProperty = surface.Property(triggerType, "CountAllOnMap");
                countAllOnMapGetter = Getter(countAllProperty);
                countAllOnMapSetter = surface.Required("Trigger_Threshold.CountAllOnMap setter",
                    Setter(countAllProperty));
                detailsWindowGetter = Getter(surface.Property(triggerType, "DetailsWindow"));

                ready = surface.Ready;
            }

            private static void AddOp(ReflectionSurface surface, Type opsType, string name, string labelKey)
            {
                object value = surface.EnumValue(opsType, name);
                if (value != null)
                {
                    ops.Add(new OpEntry(value, labelKey));
                }
            }

            /// <summary>True when every member the threshold window's scope reads or writes resolved.</summary>
            public static bool Ready
            {
                get { return ready; }
            }

            /// <summary>The details window's type, for the <c>ScopeForWindow</c> registration.</summary>
            public static Type WindowType
            {
                get { return windowType; }
            }

            /// <summary>The trigger the window was constructed for, or null -- the scope's whole handle on the job it is configuring.</summary>
            public static object TriggerOf(Window window)
            {
                if (!ready || window == null || windowTriggerField == null)
                {
                    return null;
                }
                try
                {
                    return windowTriggerField.GetValue(window);
                }
                catch (Exception ex)
                {
                    Fail("TriggerOf", ex);
                    return null;
                }
            }

            // The filter pair the window hands vanilla's own ThingFilterUI.

            public static ThingFilter ThresholdFilter(object trigger)
            {
                return Get(thresholdFilterGetter, trigger, "ThresholdFilter") as ThingFilter;
            }

            public static ThingFilter ParentFilter(object trigger)
            {
                return Get(parentFilterGetter, trigger, "ParentFilter") as ThingFilter;
            }

            /// <summary>The operators the owning job accepts, in the window's own float-menu order.</summary>
            public static List<object> SupportedOps(object trigger)
            {
                var supported = new List<object>(ops.Count);
                for (int i = 0; i < ops.Count; i++)
                {
                    if (SupportsOp(trigger, ops[i].Value))
                    {
                        supported.Add(ops[i].Value);
                    }
                }
                return supported;
            }

            /// <summary>The job's own answer for whether it accepts an operator -- the gate the window asks before drawing that option.</summary>
            public static bool SupportsOp(object trigger, object op)
            {
                if (op == null || supportsOpMethod == null)
                {
                    return false;
                }
                return Call(supportsOpMethod, trigger, new[] { op }, "SupportsOp") is bool value && value;
            }

            public static object Op(object trigger)
            {
                return Get(opGetter, trigger, "Op");
            }

            /// <summary>Writes the operator through the float-menu option's own delegate target.</summary>
            public static void SetOp(object trigger, object op)
            {
                if (op == null || !SupportsOp(trigger, op))
                {
                    return;
                }
                SetProperty(opSetter, trigger, op, "SetOp");
            }

            /// <summary>The mod's label key for an operator; its <c>.Tip</c> twin is the window's tooltip for the operator and the count field alike.</summary>
            public static string OpLabelKey(object op)
            {
                for (int i = 0; i < ops.Count; i++)
                {
                    if (ops[i].Value.Equals(op))
                    {
                        return ops[i].LabelKey;
                    }
                }
                return "";
            }

            public static int TargetCount(object trigger)
            {
                return Get(targetCountGetter, trigger, "TargetCount") is int count ? count : 0;
            }

            /// <summary>The upper bound the count field and the job's own slider share.</summary>
            public static int MaxUpperThreshold(object trigger)
            {
                return Get(maxUpperThresholdGetter, trigger, "MaxUpperThreshold") is int max ? max : 0;
            }

            /// <summary>
            /// Writes the count as the window's own field does: the clamping setter takes the
            /// value, then the maximum rises to match anything above it, so a number the slider
            /// could not reach widens the slider instead of being refused.
            /// </summary>
            public static void SetTargetCount(object trigger, int value)
            {
                SetProperty(targetCountSetter, trigger, value, "SetTargetCount");
                int written = TargetCount(trigger);
                if (written > MaxUpperThreshold(trigger))
                {
                    SetProperty(maxUpperThresholdSetter, trigger, written, "SetTargetCount");
                }
            }

            /// <summary>The stockpile the threshold counts in, or null for the strip's own "any stockpile" cell.</summary>
            public static Zone_Stockpile Stockpile(object trigger)
            {
                return Get(stockpileGetter, trigger, "Stockpile") as Zone_Stockpile;
            }

            /// <summary>Writes the storage the stockpile strip writes by reference.</summary>
            public static void SetStockpile(object trigger, Zone_Stockpile zone)
            {
                SetProperty(stockpileSetter, trigger, zone, "SetStockpile");
            }

            /// <summary>
            /// Every stockpile the strip draws a cell for, in the map's own zone order.
            /// </summary>
            public static List<Zone_Stockpile> Stockpiles(object trigger)
            {
                var zones = new List<Zone_Stockpile>();
                Map map = MapOf(trigger);
                if (map == null || map.zoneManager == null)
                {
                    return zones;
                }
                List<Zone> all = map.zoneManager.AllZones;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] is Zone_Stockpile stockpile)
                    {
                        zones.Add(stockpile);
                    }
                }
                return zones;
            }

            /// <summary>The map whose zones the strip lists: the trigger's job, then its manager.</summary>
            private static Map MapOf(object trigger)
            {
                object job = Get(triggerJobGetter, trigger, "Job");
                var manager = Get(jobManagerGetter, job, "Manager") as MapComponent;
                return manager == null ? null : manager.map;
            }

            // Shared Trigger_Threshold operations taking the job directly and resolving its
            // trigger via CmrCompat.JobTrigger. The Job prefix is forced: the trigger-taking
            // twins above take a bare `object` too, and C# cannot overload on parameter name.
            // Each wrapper delegates to the trigger-taking body, so the logic lives in one place.

            public static int JobTargetCount(object job)
            {
                return TargetCount(CmrCompat.JobTrigger(job));
            }

            /// <summary>
            /// <see cref="SetTargetCount(object, int)"/> reached from a job rather than a
            /// trigger already in hand.
            /// </summary>
            public static void SetJobTargetCount(object job, int value)
            {
                SetTargetCount(CmrCompat.JobTrigger(job), value);
            }

            public static int JobMaxUpperThreshold(object job)
            {
                return MaxUpperThreshold(CmrCompat.JobTrigger(job));
            }

            public static bool JobAllowAnyThreshold(object job)
            {
                return Flag(allowAnyThresholdGetter, CmrCompat.JobTrigger(job), "AllowAnyThreshold");
            }

            /// <summary>
            /// MUTATION-C: reproduces the checkbox's own post-click block statement for statement
            /// (Trigger_Threshold.DrawTriggerConfig, Trigger_Threshold.cs:588-601) -- the same shared
            /// widget body every trigger-threshold job's threshold section draws (see
            /// Hunting.SetAllowAnyThreshold's original remarks for the fuller citation). The public
            /// setter covers only the field write plus the parent-filter reset when switching ON; the
            /// widget also raises AllowAnyThresholdChanged, and, when switching OFF, narrows the
            /// threshold filter back to the parent filter. Trigger_Threshold.cs:588 wraps that whole
            /// block in a check that the toggle's value actually changed, so a write that leaves the
            /// value unchanged is reproduced as a no-op the same way the sighted checkbox is.
            /// </summary>
            public static void SetJobAllowAnyThreshold(object job, bool value)
            {
                object trigger = CmrCompat.JobTrigger(job);
                if (trigger == null)
                {
                    return;
                }
                if (Flag(allowAnyThresholdGetter, trigger, "AllowAnyThreshold") == value)
                {
                    return;
                }
                SetProperty(allowAnyThresholdSetter, trigger, value, "SetJobAllowAnyThreshold");
                var changed = Get(allowAnyThresholdChangedGetter, trigger, "AllowAnyThresholdChanged") as Action;
                if (changed != null)
                {
                    changed();
                }
                if (!value)
                {
                    ThingFilter threshold = ThresholdFilter(trigger);
                    ThingFilter parent = ParentFilter(trigger);
                    if (threshold != null && parent != null)
                    {
                        threshold.SetDisallowAll(parent.AllowedThingDefs);
                    }
                }
            }

            public static bool JobCountAllOnMap(object job)
            {
                return Flag(countAllOnMapGetter, CmrCompat.JobTrigger(job), "CountAllOnMap");
            }

            /// <summary>Writes the storage the trigger's "count all on map" toggle writes by ref.</summary>
            public static void SetJobCountAllOnMap(object job, bool value)
            {
                SetProperty(countAllOnMapSetter, CmrCompat.JobTrigger(job), value, "SetJobCountAllOnMap");
            }

            /// <summary>
            /// Opens the mod's threshold details window for a job: the tab's own sync write, if
            /// it has one, then the threshold label's click body.
            /// </summary>
            public static void OpenDetails(object job, Action applySync)
            {
                object trigger = CmrCompat.JobTrigger(job);
                if (trigger == null || detailsWindowGetter == null)
                {
                    return;
                }
                // MUTATION-C: mirrors the two statements the threshold label's own invisible button
                // runs (Trigger_Threshold.DrawTriggerConfig, Trigger_Threshold.cs:501-505):
                // onOpenFilterDetails?.Invoke(), then Find.WindowStack.Add(DetailsWindow).
                if (applySync != null)
                {
                    applySync();
                }
                if (Get(detailsWindowGetter, trigger, "DetailsWindow") is Window window)
                {
                    // The window closes itself on any Return KeyDown, and one added mid-dispatch
                    // draws later in this same OnGUI pass with the activating Enter still live, so
                    // it would open and self-close in one frame. Adding after the frame ends keeps
                    // that Enter out of its event stream.
                    LongEventHandler.ExecuteWhenFinished(() => Find.WindowStack.Add(window));
                }
            }

            // Plumbing, gated on this block's own Ready. Getter/Setter/Fail come from the
            // enclosing surface.

            private static bool Flag(MethodInfo getter, object instance, string member)
            {
                return Get(getter, instance, member) is bool value && value;
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
        }
    }
}
