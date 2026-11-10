using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    internal static partial class CmrCompat
    {
        /// <summary>
        /// The Overview tab's job list controls: search, group-by, collapse state, job-type filters,
        /// per-group actions and manual groups. Groups come from the tab's own
        /// <c>GetFilteredJobs</c>/<c>GetGroups</c>, so filtering, grouping and ordering are the mod's.
        ///
        /// MUTATION VEHICLES. Suspend, force update and update interval go through the job's own
        /// setters and <c>Untouch</c>, gated by the mod's own <c>AllSuspended</c> and
        /// <c>JobsEligibleForForceUpdate</c>; manual groups go through the tab's
        /// <c>SetManualGroup</c> and the mod's own <c>Dialog_ManualGroupName</c>; search writes the
        /// widget's filter text. Group mode, hidden job types and collapsed groups are private fields
        /// the tab's own button closures write directly, so those writes are doctrine C.
        /// </summary>
        internal static class OverviewList
        {
            private static readonly Type tabType;
            private static readonly Type jobType;
            private static readonly Type groupModeType;
            private static readonly object manualGroupMode;

            private static readonly MethodInfo getFilteredJobsMethod;
            private static readonly MethodInfo getGroupsMethod;
            private static readonly FieldInfo groupModeField;
            private static readonly FieldInfo hiddenJobTypesField;
            private static readonly FieldInfo collapsedGroupsField;
            private static readonly FieldInfo quickSearchWidgetField;
            private static readonly MethodInfo getManualGroupMethod;
            private static readonly MethodInfo setManualGroupMethod;
            private static readonly MethodInfo allSuspendedDefinition;
            private static readonly MethodInfo eligibleForForceUpdateDefinition;

            private static readonly MethodInfo jobDefGetter;
            private static readonly MethodInfo isSuspendedGetter;
            private static readonly MethodInfo shouldDoNowGetter;
            private static readonly MethodInfo untouchMethod;
            private static readonly MethodInfo updateIntervalSetter;
            private static readonly MethodInfo updateIntervalOptionsGetter;
            private static readonly MethodInfo intervalLabelGetter;
            private static readonly FieldInfo managerDefOrderField;
            private static readonly ConstructorInfo groupNameDialogCtor;

            private static readonly bool ready;

            static OverviewList()
            {
                var surface = new ReflectionSurface("CmrCompat.OverviewList");

                tabType = surface.Type("ColonyManagerRedux.Managers.ManagerTab_Overview");
                jobType = surface.Type("ColonyManagerRedux.ManagerJob");
                Type managerDefType = surface.Type("ColonyManagerRedux.ManagerDef");
                Type utilitiesType = surface.Type("ColonyManagerRedux.Utilities");
                Type intervalType = surface.Type("ColonyManagerRedux.UpdateInterval");
                Type dialogType = surface.Type("ColonyManagerRedux.Managers.Dialog_ManualGroupName");
                groupModeType = surface.Required("ManagerTab_Overview.OverviewGroupMode",
                    tabType != null ? AccessTools.Inner(tabType, "OverviewGroupMode") : null);
                manualGroupMode = surface.EnumValue(groupModeType, "Manual");

                getFilteredJobsMethod = surface.Method(tabType, "GetFilteredJobs", Type.EmptyTypes);
                getGroupsMethod = surface.Required("ManagerTab_Overview.GetGroups(List)", NonGeneric(tabType, "GetGroups"));
                groupModeField = surface.Field(tabType, "_groupMode");
                hiddenJobTypesField = surface.Field(tabType, "_hiddenJobTypes");
                collapsedGroupsField = surface.Field(tabType, "_collapsedGroups");
                quickSearchWidgetField = surface.Field(tabType, "_quickSearchWidget");
                if (jobType != null)
                {
                    getManualGroupMethod = surface.Method(tabType, "GetManualGroup", new[] { jobType });
                    setManualGroupMethod = surface.Method(tabType, "SetManualGroup", new[] { jobType, typeof(string) });
                }
                allSuspendedDefinition = surface.Required("ManagerTab_Overview.AllSuspended", Generic(tabType, "AllSuspended"));
                eligibleForForceUpdateDefinition = surface.Required("ManagerTab_Overview.JobsEligibleForForceUpdate",
                    Generic(tabType, "JobsEligibleForForceUpdate"));

                jobDefGetter = Getter(surface.Property(jobType, "Def"));
                isSuspendedGetter = Getter(surface.Property(jobType, "IsSuspended"));
                shouldDoNowGetter = Getter(surface.Property(jobType, "ShouldDoNow"));
                untouchMethod = surface.Method(jobType, "Untouch", Type.EmptyTypes);
                PropertyInfo intervalProperty = surface.Property(jobType, "UpdateInterval");
                updateIntervalSetter = surface.Required("ManagerJob.UpdateInterval setter",
                    intervalProperty != null ? intervalProperty.GetSetMethod(true) : null);
                updateIntervalOptionsGetter = Getter(surface.Property(utilitiesType, "UpdateIntervalOptions"));
                intervalLabelGetter = Getter(surface.Property(intervalType, "Label"));
                managerDefOrderField = surface.Field(managerDefType, "order");
                groupNameDialogCtor = surface.Constructor(dialogType,
                    new[] { typeof(string), typeof(IReadOnlyCollection<string>), typeof(string), typeof(Action<string>) });

                ready = surface.Ready;
            }

            public static bool Ready
            {
                get { return ready; }
            }

            public static bool HandlesTab(object tab)
            {
                return ready && tab != null && tabType.IsInstanceOfType(tab);
            }

            // ---- Groups ----

            /// <summary>The groups the tab draws right now: search and hidden job types applied, in the mod's own order.</summary>
            public static List<CmrOverviewGroup> Groups(object tab)
            {
                var result = new List<CmrOverviewGroup>();
                if (!HandlesTab(tab))
                {
                    return result;
                }
                try
                {
                    object filtered = getFilteredJobsMethod.Invoke(tab, null);
                    var groups = getGroupsMethod.Invoke(tab, new[] { filtered }) as IEnumerable;
                    if (groups == null)
                    {
                        return result;
                    }
                    foreach (object group in groups)
                    {
                        Type recordType = group.GetType();
                        object rawJobs = recordType.GetProperty("Jobs").GetValue(group, null);
                        result.Add(new CmrOverviewGroup(
                            recordType.GetProperty("Key").GetValue(group, null) as string,
                            recordType.GetProperty("Header").GetValue(group, null) as string,
                            ((IEnumerable)rawJobs).Cast<object>().ToList(), rawJobs));
                    }
                }
                catch (Exception ex)
                {
                    Fail("Groups", ex);
                }
                return result;
            }

            /// <summary>Whether the tab draws any job at all; with none it shows only its "no jobs" label.</summary>
            public static bool AnyMatching(object tab)
            {
                List<CmrOverviewGroup> groups = Groups(tab);
                for (int i = 0; i < groups.Count; i++)
                {
                    if (groups[i].Jobs.Count > 0)
                    {
                        return true;
                    }
                }
                return false;
            }

            public static bool IsCollapsed(object tab, string key)
            {
                HashSet<string> collapsed = Collapsed(tab);
                return collapsed != null && collapsed.Contains(key);
            }

            /// <summary>The group header's click body (ManagerTab_Overview.DrawGroupHeader).</summary>
            public static void SetCollapsed(object tab, string key, bool collapse)
            {
                HashSet<string> collapsed = Collapsed(tab);
                if (collapsed == null)
                {
                    return;
                }
                // MUTATION-C: the header's own closure edits this private set directly; no method wraps it.
                if (collapse)
                {
                    collapsed.Add(key);
                }
                else
                {
                    collapsed.Remove(key);
                }
            }

            private static HashSet<string> Collapsed(object tab)
            {
                return Field(collapsedGroupsField, tab, "Collapsed") as HashSet<string>;
            }

            // ---- Group mode ----

            public static List<object> GroupModes()
            {
                var modes = new List<object>();
                if (ready)
                {
                    foreach (object mode in Enum.GetValues(groupModeType))
                    {
                        modes.Add(mode);
                    }
                }
                return modes;
            }

            public static object GroupMode(object tab)
            {
                return Field(groupModeField, tab, "GroupMode");
            }

            public static bool IsGrouped(object tab)
            {
                object mode = GroupMode(tab);
                return mode != null && Convert.ToInt32(mode) != 0;
            }

            public static string GroupModeLabel(object mode)
            {
                return mode == null ? "" : CompatText.ModText("ColonyManagerRedux.Overview.GroupBy." + mode);
            }

            /// <summary>The group-by float menu option's own action.</summary>
            public static void SetGroupMode(object tab, object mode)
            {
                // MUTATION-C: the float menu option's closure assigns this private field; no setter exists.
                SetField(groupModeField, tab, mode, "SetGroupMode");
            }

            // ---- Job-type filter ----

            /// <summary>The job types with at least one job, in ManagerDef order (ManagerTab_Overview.DrawJobTypeFilterRow).</summary>
            public static List<Def> JobTypes(object tab)
            {
                var defs = new List<Def>();
                foreach (object job in TabJobs(tab))
                {
                    var def = Read(jobDefGetter, job, "JobTypes") as Def;
                    if (def != null && !defs.Contains(def))
                    {
                        defs.Add(def);
                    }
                }
                return defs.OrderBy(OrderOf).ToList();
            }

            public static bool IsJobTypeHidden(object tab, Def def)
            {
                object set = Field(hiddenJobTypesField, tab, "IsJobTypeHidden");
                return set != null && def != null && (bool)Invoke(set, "Contains", def);
            }

            /// <summary>A filter icon's click body: hide a shown type, show a hidden one.</summary>
            public static void SetJobTypeHidden(object tab, Def def, bool hidden)
            {
                object set = Field(hiddenJobTypesField, tab, "SetJobTypeHidden");
                if (set == null || def == null)
                {
                    return;
                }
                // MUTATION-C: the icon's closure edits this private set directly; no method wraps it.
                Invoke(set, hidden ? "Add" : "Remove", def);
            }

            /// <summary>The eye icon's click body: clear the set, then hide every type unless all were already hidden.</summary>
            public static void SetAllJobTypesHidden(object tab, bool hidden)
            {
                object set = Field(hiddenJobTypesField, tab, "SetAllJobTypesHidden");
                if (set == null)
                {
                    return;
                }
                // MUTATION-C: the eye icon's closure edits this private set directly; no method wraps it.
                Invoke(set, "Clear");
                if (hidden)
                {
                    foreach (Def def in JobTypes(tab))
                    {
                        Invoke(set, "Add", def);
                    }
                }
            }

            // ---- Search ----

            public static string SearchText(object tab)
            {
                var widget = Field(quickSearchWidgetField, tab, "SearchText") as QuickSearchWidget;
                return widget == null ? "" : widget.filter.Text;
            }

            public static void SetSearchText(object tab, string text)
            {
                var widget = Field(quickSearchWidgetField, tab, "SetSearchText") as QuickSearchWidget;
                if (widget != null)
                {
                    widget.filter.Text = text ?? ""; // MUTATION-C: mirrors QuickSearchWidget.OnGUI's text write; the widget's only text writer is its IMGUI field
                }
            }

            // ---- Group actions (ManagerTab_Overview.DrawGroupActions) ----

            public static bool AllSuspended(CmrOverviewGroup group)
            {
                return CallGeneric(allSuspendedDefinition, group, isSuspendedGetter, "AllSuspended") is bool all && all;
            }

            /// <summary>The group stamp's click body: resume every job when all are suspended, else suspend every job.</summary>
            public static void ToggleSuspendAll(CmrOverviewGroup group)
            {
                bool suspend = !AllSuspended(group);
                for (int i = 0; i < group.Jobs.Count; i++)
                {
                    SetJobSuspended(group.Jobs[i], suspend);
                }
            }

            /// <summary>The refresh icon's click body: untouch every job the mod's own eligibility rule allows; returns how many.</summary>
            public static int ForceUpdateAll(CmrOverviewGroup group)
            {
                var eligible = CallGeneric(eligibleForForceUpdateDefinition, group, isSuspendedGetter, "ForceUpdateAll",
                    shouldDoNowGetter) as IEnumerable;
                int count = 0;
                if (eligible == null)
                {
                    return count;
                }
                foreach (object job in eligible)
                {
                    Call(untouchMethod, job, null, "ForceUpdateAll");
                    count++;
                }
                return count;
            }

            public static List<object> UpdateIntervalOptions()
            {
                var options = Read(updateIntervalOptionsGetter, null, "UpdateIntervalOptions") as IEnumerable;
                return options == null ? new List<object>() : options.Cast<object>().ToList();
            }

            public static string UpdateIntervalLabel(object interval)
            {
                return Read(intervalLabelGetter, interval, "UpdateIntervalLabel") as string ?? "";
            }

            /// <summary>The stopwatch menu option's action: every job in the group takes the interval.</summary>
            public static void SetUpdateIntervalAll(CmrOverviewGroup group, object interval)
            {
                for (int i = 0; i < group.Jobs.Count; i++)
                {
                    Call(updateIntervalSetter, group.Jobs[i], new[] { interval }, "SetUpdateIntervalAll");
                }
            }

            // ---- Manual groups ----

            public static string ManualGroup(object job)
            {
                return Call(getManualGroupMethod, null, new[] { job }, "ManualGroup") as string;
            }

            public static void SetManualGroup(object job, string group)
            {
                Call(setManualGroupMethod, null, new[] { job, group }, "SetManualGroup");
            }

            /// <summary>A named manual group, which the mod gives a rename icon; the Ungrouped bucket's jobs carry no group name.</summary>
            public static bool IsManualGroup(object tab, CmrOverviewGroup group)
            {
                return Equals(GroupMode(tab), manualGroupMode)
                    && group.Jobs.Count > 0 && ManualGroup(group.Jobs[0]) != null;
            }

            /// <summary>Every manual group name in use, distinct ignoring case, optionally leaving one out (the rename dialog's own list).</summary>
            public static List<string> ExistingManualGroups(object tab, string except = null)
            {
                var names = new List<string>();
                foreach (object job in TabJobs(tab))
                {
                    string name = ManualGroup(job);
                    if (name != null && !string.Equals(name, except, StringComparison.OrdinalIgnoreCase)
                        && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        names.Add(name);
                    }
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
                return names;
            }

            /// <summary>Opens the mod's own name dialog (new group, or rename), which validates the name itself.</summary>
            public static void OpenGroupNameDialog(string title, List<string> existing, string currentName, Action<string> onNamed)
            {
                if (!ready)
                {
                    return;
                }
                try
                {
                    var dialog = (Window)groupNameDialogCtor.Invoke(new object[] { title, existing, currentName, onNamed });
                    Find.WindowStack.Add(dialog);
                }
                catch (Exception ex)
                {
                    Fail("OpenGroupNameDialog", ex);
                }
            }

            // ---- Plumbing ----

            private static int OrderOf(Def def)
            {
                try
                {
                    return managerDefOrderField.GetValue(def) is int order ? order : 0;
                }
                catch (Exception ex)
                {
                    Fail("OrderOf", ex);
                    return 0;
                }
            }

            private static MethodInfo NonGeneric(Type type, string name)
            {
                return type == null ? null : AccessTools.GetDeclaredMethods(type)
                    .FirstOrDefault(m => m.Name == name && !m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
            }

            private static MethodInfo Generic(Type type, string name)
            {
                return type == null ? null : AccessTools.GetDeclaredMethods(type)
                    .FirstOrDefault(m => m.Name == name && m.IsGenericMethodDefinition);
            }

            private static MethodInfo Getter(PropertyInfo property)
            {
                return property != null ? property.GetGetMethod(true) : null;
            }

            /// <summary>Calls one of the tab's generic job helpers closed over ManagerJob, with the job getters as its predicates.</summary>
            private static object CallGeneric(MethodInfo definition, CmrOverviewGroup group, MethodInfo firstPredicate,
                string member, MethodInfo secondPredicate = null)
            {
                if (!ready || group == null)
                {
                    return null;
                }
                try
                {
                    Type predicateType = typeof(Func<,>).MakeGenericType(jobType, typeof(bool));
                    var args = new List<object> { group.RawJobs, Delegate.CreateDelegate(predicateType, null, firstPredicate) };
                    if (secondPredicate != null)
                    {
                        args.Add(Delegate.CreateDelegate(predicateType, null, secondPredicate));
                    }
                    return definition.MakeGenericMethod(jobType).Invoke(null, args.ToArray());
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                    return null;
                }
            }

            private static object Field(FieldInfo field, object instance, string member)
            {
                if (!ready || instance == null)
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

            private static void SetField(FieldInfo field, object instance, object value, string member)
            {
                if (!ready || instance == null)
                {
                    return;
                }
                try
                {
                    // MUTATION-C: see each wrapper; these private fields have no setter to invoke instead.
                    field.SetValue(instance, value);
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                }
            }

            private static object Read(MethodInfo getter, object instance, string member)
            {
                return Call(getter, instance, null, member);
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

            private static object Invoke(object set, string method, params object[] args)
            {
                try
                {
                    return set.GetType().GetMethod(method).Invoke(set, args);
                }
                catch (Exception ex)
                {
                    Fail("HashSet." + method, ex);
                    return false;
                }
            }

            private static readonly HashSet<string> loggedFailures = new HashSet<string>();

            private static void Fail(string member, Exception ex)
            {
                if (loggedFailures.Add(member))
                {
                    ModLogger.Error("CmrCompat.OverviewList." + member + " failed: " + ex.Message);
                }
            }
        }
    }
}
