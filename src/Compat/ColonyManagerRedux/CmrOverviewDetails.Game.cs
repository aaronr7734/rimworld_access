using System.Collections.Generic;
using RimWorld;
using Verse;
using static RimWorldAccess.Shell.CompatText;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Overview tab's detail rows, in the mod's own visual order
    /// (<c>ManagerTab_Overview.DoTabContents</c>): the controls above the job list (search, group
    /// by, collapse all, job type filters), the selected job's tab jump and manual group, then the
    /// worker table. The grouped job list itself is the scope's Jobs region; a group header's
    /// action icons are <see cref="GroupActionChoices"/>.
    /// </summary>
    internal sealed class CmrOverviewDetails : ICmrJobDetailsProvider
    {
        public bool Handles(object tab)
        {
            return CmrCompat.Overview.HandlesTab(tab);
        }

        public List<CmrDetailRegion> Build(object tab, object job)
        {
            var regions = new List<CmrDetailRegion>();
            if (tab == null || !CmrCompat.Overview.Ready)
            {
                return regions;
            }
            if (CmrCompat.OverviewList.Ready)
            {
                regions.Add(BuildListOptions(tab));
            }
            if (job != null)
            {
                CmrDetailRegion selectedJob = BuildSelectedJob(tab, job);
                if (selectedJob != null)
                {
                    regions.Add(selectedJob);
                }
            }
            CmrDetailRegion workers = BuildWorkers(tab);
            if (workers != null)
            {
                regions.Add(workers);
            }
            return regions;
        }

        // ------------------------------------------------------------------
        // Selected job: the tab-jump the sighted row's own icon performs.
        // ------------------------------------------------------------------

        private static CmrDetailRegion BuildSelectedJob(object tab, object job)
        {
            object target = CmrCompat.JobTab(job);
            if (target == null)
            {
                return null;
            }
            var region = new CmrDetailRegion(
                "RimWorldAccess.Cmr.Overview.SelectedJobRegion".Translate());

            string label = ModArgs("ColonyManagerRedux.Common.GoToJob",
                CmrCompat.JobLabel(job).UncapitalizeFirst());
            if (CmrCompat.TabEnabled(target))
            {
                region.Rows.Add(new CmrDetailRow
                {
                    Label = label,
                    Role = ElementRole.Button,
                    Activate = () => CmrCompat.GoTo(target, job),
                    Confirmation = "RimWorldAccess.Cmr.TabSwitched".Translate(CmrCompat.TabLabel(target)),
                });
            }
            else
            {
                region.Rows.Add(new CmrDetailRow
                {
                    Label = label,
                    Tooltip = () => CmrCompat.TabLabel(target)
                        + Flatten(ModArgs("ColonyManagerRedux.Common.TabDisabledBecause",
                            CmrCompat.TabDisabledReason(target))),
                    Role = ElementRole.None,
                });
            }
            if (CmrCompat.OverviewList.Ready)
            {
                region.Rows.Add(ManualGroupRow(tab, job));
            }
            return region;
        }

        private static CmrDetailRow ManualGroupRow(object tab, object job)
        {
            return new CmrDetailRow
            {
                Label = ModText("ColonyManagerRedux.Overview.GroupBy.Manual"),
                Role = ElementRole.ComboBox,
                // The tooltip's first paragraph states the group; the second is a mouse instruction.
                Value = () =>
                {
                    string group = CmrCompat.OverviewList.ManualGroup(job);
                    return Flatten(CompatText.FirstParagraph(group != null
                        ? ModArgs("ColonyManagerRedux.Overview.ManualGroup.AssignedTooltip", group)
                        : ModText("ColonyManagerRedux.Overview.ManualGroup.UnassignedTooltip")));
                },
                Choices = () =>
                {
                    List<string> existing = CmrCompat.OverviewList.ExistingManualGroups(tab);
                    var choices = new List<CmrPickerChoice>();
                    foreach (string name in existing)
                    {
                        string group = name;
                        choices.Add(new CmrPickerChoice(group, () => CmrCompat.OverviewList.SetManualGroup(job, group)));
                    }
                    choices.Add(new CmrPickerChoice(ModText("ColonyManagerRedux.Overview.ManualGroup.NewGroup"),
                        () => CmrCompat.OverviewList.OpenGroupNameDialog(
                            ModText("ColonyManagerRedux.Overview.ManualGroup.CreateGroup"), existing, null,
                            name => CmrCompat.OverviewList.SetManualGroup(job, name))));
                    if (CmrCompat.OverviewList.ManualGroup(job) != null)
                    {
                        choices.Add(new CmrPickerChoice(ModText("ColonyManagerRedux.Overview.ManualGroup.RemoveFromGroup"),
                            () => CmrCompat.OverviewList.SetManualGroup(job, null)));
                    }
                    return choices;
                },
            };
        }

        private static CmrDetailRegion BuildListOptions(object tab)
        {
            var region = new CmrDetailRegion("RimWorldAccess.Cmr.Overview.ListOptionsRegion".Translate());
            region.Rows.Add(new CmrDetailRow
            {
                Label = "RimWorldAccess.Cmr.Overview.Search".Translate(),
                Role = ElementRole.TextField,
                Value = () => CmrCompat.OverviewList.SearchText(tab),
                Text = new CmrTextSpec
                {
                    Current = () => CmrCompat.OverviewList.SearchText(tab),
                    Apply = text => CmrCompat.OverviewList.SetSearchText(tab, text),
                },
            });
            region.Rows.Add(new CmrDetailRow
            {
                Label = ModArgs("ColonyManagerRedux.Overview.GroupBy",
                    CmrCompat.OverviewList.GroupModeLabel(CmrCompat.OverviewList.GroupMode(tab))),
                Role = ElementRole.ComboBox,
                Choices = () =>
                {
                    var choices = new List<CmrPickerChoice>();
                    foreach (object mode in CmrCompat.OverviewList.GroupModes())
                    {
                        object chosen = mode;
                        choices.Add(new CmrPickerChoice(CmrCompat.OverviewList.GroupModeLabel(chosen),
                            () => CmrCompat.OverviewList.SetGroupMode(tab, chosen)));
                    }
                    return choices;
                },
            });

            List<CmrOverviewGroup> groups = CmrCompat.OverviewList.Groups(tab);
            AddCollapseAllRow(region, tab, groups);

            List<Def> types = CmrCompat.OverviewList.JobTypes(tab);
            if (types.Count == 0)
            {
                region.Rows.Add(new CmrDetailRow { Label = ModText("ColonyManagerRedux.Overview.NoJobs") });
                return region;
            }
            bool allHidden = types.TrueForAll(def => CmrCompat.OverviewList.IsJobTypeHidden(tab, def));
            region.Rows.Add(new CmrDetailRow
            {
                Label = ModText(allHidden
                    ? "ColonyManagerRedux.Overview.JobTypeFilter.ShowAll"
                    : "ColonyManagerRedux.Overview.JobTypeFilter.HideAll"),
                Role = ElementRole.Button,
                Activate = () => CmrCompat.OverviewList.SetAllJobTypesHidden(tab, !allHidden),
                Confirmation = ModText(allHidden
                    ? "ColonyManagerRedux.Overview.JobTypeFilter.ShowAll"
                    : "ColonyManagerRedux.Overview.JobTypeFilter.HideAll"),
            });
            foreach (Def type in types)
            {
                Def def = type;
                region.Rows.Add(new CmrDetailRow
                {
                    Label = def.LabelCap,
                    Role = ElementRole.Checkbox,
                    Check = () => CmrCompat.OverviewList.IsJobTypeHidden(tab, def) ? CheckState.Unchecked : CheckState.Checked,
                    Tooltip = () => ModArgs(CmrCompat.OverviewList.IsJobTypeHidden(tab, def)
                        ? "ColonyManagerRedux.Overview.JobTypeFilter.Show"
                        : "ColonyManagerRedux.Overview.JobTypeFilter.Hide", def.label.CapitalizeFirst()),
                    Activate = () => CmrCompat.OverviewList.SetJobTypeHidden(tab, def,
                        !CmrCompat.OverviewList.IsJobTypeHidden(tab, def)),
                });
            }
            if (!CmrCompat.OverviewList.AnyMatching(tab))
            {
                region.Rows.Add(new CmrDetailRow { Label = ModText("ColonyManagerRedux.Overview.NoMatchingJobs") });
            }
            return region;
        }

        private static void AddCollapseAllRow(CmrDetailRegion region, object tab, List<CmrOverviewGroup> groups)
        {
            if (!CmrCompat.OverviewList.IsGrouped(tab))
            {
                return;
            }
            var keys = new List<string>();
            foreach (CmrOverviewGroup group in groups)
            {
                if (group.Header != null)
                {
                    keys.Add(group.Key);
                }
            }
            if (keys.Count == 0)
            {
                return;
            }
            bool allCollapsed = keys.TrueForAll(key => CmrCompat.OverviewList.IsCollapsed(tab, key));
            string label = ModText(allCollapsed
                ? "ColonyManagerRedux.Overview.CollapseAllGroups.ExpandAll"
                : "ColonyManagerRedux.Overview.CollapseAllGroups.CollapseAll");
            region.Rows.Add(new CmrDetailRow
            {
                Label = label,
                Role = ElementRole.Button,
                Activate = delegate
                {
                    foreach (string key in keys)
                    {
                        CmrCompat.OverviewList.SetCollapsed(tab, key, !allCollapsed);
                    }
                },
                Confirmation = label,
            });
        }

        /// <summary>A group header's action icons as one menu (ManagerTab_Overview.DrawGroupActions).</summary>
        internal static List<CmrPickerChoice> GroupActionChoices(object tab, CmrOverviewGroup group)
        {
            var choices = new List<CmrPickerChoice>
            {
                new CmrPickerChoice(ModText(CmrCompat.OverviewList.AllSuspended(group)
                    ? "ColonyManagerRedux.Overview.GroupActions.ResumeAll"
                    : "ColonyManagerRedux.Overview.GroupActions.SuspendAll"),
                    () => CmrCompat.OverviewList.ToggleSuspendAll(group)),
                new CmrPickerChoice(ModText("ColonyManagerRedux.Overview.GroupActions.ForceUpdateAll"),
                    () => CmrCompat.OverviewList.ForceUpdateAll(group)),
            };
            foreach (object interval in CmrCompat.OverviewList.UpdateIntervalOptions())
            {
                object chosen = interval;
                choices.Add(new CmrPickerChoice(ModArgs("ColonyManagerRedux.Overview.GroupActions.SetUpdateIntervalAllTo",
                    CmrCompat.OverviewList.UpdateIntervalLabel(chosen).UncapitalizeFirst()),
                    () => CmrCompat.OverviewList.SetUpdateIntervalAll(group, chosen)));
            }
            if (CmrCompat.OverviewList.IsManualGroup(tab, group))
            {
                choices.Add(new CmrPickerChoice(ModText("ColonyManagerRedux.Overview.ManualGroup.RenameGroupTooltip"),
                    () => CmrCompat.OverviewList.OpenGroupNameDialog(
                        ModText("ColonyManagerRedux.Overview.ManualGroup.RenameGroup"),
                        CmrCompat.OverviewList.ExistingManualGroups(tab, group.Header), group.Header,
                        name =>
                        {
                            foreach (object job in group.Jobs)
                            {
                                CmrCompat.OverviewList.SetManualGroup(job, name);
                            }
                        })));
            }
            return choices;
        }

        // ------------------------------------------------------------------
        // Workers: the mod's pawn table, presented as a real table.
        // ------------------------------------------------------------------

        /// <summary>
        /// The worker table as a table region: the label column names each row and the remaining
        /// cells (activity, priority) are navigable columns, so Enter on the priority cell cycles
        /// it through the column's own handler exactly as a sighted click does. An empty table
        /// stays an empty flat region, which the shell's empty-region rule hides -- the same
        /// no-rows behavior this region always had.
        /// </summary>
        private static CmrDetailRegion BuildWorkers(object tab)
        {
            CmrDetailPawnTable table = CmrDetailPawnTable.Read(CmrCompat.Overview.Table(tab));
            if (table == null)
            {
                return null;
            }
            WorkTypeDef workType = CmrCompat.Overview.CurrentWorkType(tab);
            string name = workType != null
                ? workType.gerundLabel.CapitalizeFirst()
                : "RimWorldAccess.Cmr.Overview.WorkersRegion".Translate().ToString();
            var region = new CmrDetailRegion(name);
            if (table.Rows.Count > 0)
            {
                region.Table = table;
            }
            return region;
        }
    }
}
