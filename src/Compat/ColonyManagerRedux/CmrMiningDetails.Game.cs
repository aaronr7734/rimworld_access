using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using static RimWorldAccess.Shell.CompatText;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Mining tab's detail rows: the mod's own columns as regions, each section of the Options
    /// column as a run of rows in the mod's own draw order (<c>ManagerTab_Mining.DoMainContent</c>).
    ///
    /// Options carries the left column: the threshold section, the task priority order, the mining
    /// toggles, the chunk-hauling toggles, the deconstruct-buildings pair, the mining-area strip, and
    /// the roof/room safety toggles. Minerals carries the middle column: the group shortcuts, the
    /// refresh and padlock icons, and one row per mineral. Buildings carries the right column -- only
    /// while <see cref="CmrCompat.Mining.DeconstructBuildings"/> is true, exactly as the mod's own
    /// third column appears and vanishes with that same toggle.
    ///
    /// Conditional rows and regions appear and vanish with their own conditions exactly as the sighted
    /// ones do -- TakeOwnershipOfMiningJobs and CheckRoofSupportAdvanced turn into disabled rows rather
    /// than disappearing, matching the mod drawing a greyed label in their place rather than nothing;
    /// the Buildings region itself disappears entirely, matching the mod drawing no third column.
    /// </summary>
    internal sealed class CmrMiningDetails : ICmrJobDetailsProvider
    {
        public bool Handles(object tab)
        {
            return CmrCompat.Mining.HandlesTab(tab);
        }

        public List<CmrDetailRegion> Build(object tab, object job)
        {
            var regions = new List<CmrDetailRegion>();
            if (job == null || !CmrCompat.Mining.Ready)
            {
                return regions;
            }
            regions.Add(BuildOptions(job));
            regions.Add(BuildMinerals(job));
            if (CmrCompat.Mining.DeconstructBuildings(job))
            {
                regions.Add(BuildBuildings(job));
            }
            return regions;
        }

        // ------------------------------------------------------------------
        // Options: the left column.
        // ------------------------------------------------------------------

        private static CmrDetailRegion BuildOptions(object job)
        {
            var region = new CmrDetailRegion("RimWorldAccess.Cmr.OptionsRegion".Translate());
            AddThreshold(region, job);
            AddTaskPriorityOrder(region, job);
            AddMining(region, job);
            AddChunks(region, job);
            AddDeconstructBuildings(region, job);
            AddMiningArea(region, job);
            AddHealthAndSafety(region, job);
            return region;
        }

        /// <summary>
        /// The threshold section, with Mining's own four-argument summary (current count, chunk
        /// yield, designated yield, target label) and a fifth tooltip-only argument naming what the
        /// job's counted minerals will drop (ManagerTab_Mining.cs:487-518).
        /// </summary>
        private static void AddThreshold(CmrDetailRegion region, object job)
        {
            CmrTabRows.AddThresholdRows(region, job, "Mining",
                "RimWorldAccess.Cmr.Threshold.TargetCountRow",
                () => ThresholdText(job, "ColonyManagerRedux.Mining.TargetCount"),
                () => Flatten(ThresholdTipText(job)),
                () => CmrCompat.Mining.OpenThresholdDetails(job),
                () => CmrCompat.Mining.TargetLabel(job),
                () => CmrCompat.Mining.SyncFilterAndAllowed(job),
                value => CmrCompat.Mining.SetSyncFilterAndAllowed(job, value),
                reachBeforePath: true);
        }

        /// <summary>
        /// One row per task in the job's own priority order, numbered the way the mod's own labels
        /// are (ManagerTab_Mining.cs:273-278). Ctrl+Up/Down swap this row with its neighbor, the
        /// same chord the job list reorders with.
        /// </summary>
        private static void AddTaskPriorityOrder(CmrDetailRegion region, object job)
        {
            string section = ModText("ColonyManagerRedux.Mining.TaskPriorityOrder");
            List<object> tasks = CmrCompat.Mining.TaskPriorityOrder(job);
            for (int i = 0; i < tasks.Count; i++)
            {
                object task = tasks[i];
                int position = i;
                region.Rows.Add(new CmrDetailRow
                {
                    Label = (position + 1) + ". "
                        + ModText("ColonyManagerRedux.Mining.TaskPriorityOrder." + CmrCompat.Mining.TaskName(task)),
                    Role = ElementRole.MenuItem,
                    SectionTitle = section,
                    CanReorder = direction => CanSwapTask(job, task, direction),
                    Reorder = direction => SwapTask(job, task, direction),
                });
            }
        }

        /// <summary>
        /// AllowMining, then the ownership toggle that only exists as a real control while AllowMining
        /// is true, then ControlDeepDrills (ManagerTab_Mining.cs:295-337).
        /// </summary>
        private static void AddMining(CmrDetailRegion region, object job)
        {
            string section = ModText("ColonyManagerRedux.Mining.Mining");
            region.Rows.Add(CmrTabRows.Toggle(section,
                "ColonyManagerRedux.Mining.AllowMining",
                () => CmrCompat.Mining.AllowMining(job),
                value => CmrCompat.Mining.SetAllowMining(job, value)));

            if (CmrCompat.Mining.AllowMining(job))
            {
                region.Rows.Add(CmrTabRows.Toggle(section,
                    "ColonyManagerRedux.Mining.TakeOwnershipOfMiningJobs",
                    () => CmrCompat.Mining.TakeOwnershipOfMiningJobs(job),
                    value => CmrCompat.Mining.SetTakeOwnershipOfMiningJobs(job, value)));
            }
            else
            {
                region.Rows.Add(DisabledRow(section,
                    "ColonyManagerRedux.Mining.TakeOwnershipOfMiningJobs",
                    "ColonyManagerRedux.Mining.TakeOwnershipOfMiningJobs.Disabled.Tip"));
            }

            region.Rows.Add(CmrTabRows.Toggle(section,
                "ColonyManagerRedux.Mining.ControlDeepDrills",
                () => CmrCompat.Mining.ControlDeepDrills(job),
                value => CmrCompat.Mining.SetControlDeepDrills(job, value)));
        }

        private static void AddChunks(CmrDetailRegion region, object job)
        {
            string section = ModText("ColonyManagerRedux.Mining.Chunks");
            region.Rows.Add(CmrTabRows.Toggle(section,
                "ColonyManagerRedux.Mining.CountChunkProducts",
                () => CmrCompat.Mining.CountChunkProducts(job),
                value => CmrCompat.Mining.SetCountChunkProducts(job, value)));
            bool counted = CmrCompat.Mining.CountChunkProducts(job);
            region.Rows.Add(HaulChunksRow(section, counted, "ColonyManagerRedux.Mining.HaulMapChunks",
                () => CmrCompat.Mining.HaulMapChunks(job),
                value => CmrCompat.Mining.SetHaulMapChunks(job, value)));
            region.Rows.Add(HaulChunksRow(section, counted, "ColonyManagerRedux.Mining.HaulMinedChunks",
                () => CmrCompat.Mining.HaulMinedChunks(job),
                value => CmrCompat.Mining.SetHaulMinedChunks(job, value)));
        }

        private static CmrDetailRow HaulChunksRow(string section, bool counted, string labelKey, Func<bool> get,
            Action<bool> set)
        {
            if (counted)
            {
                return CmrTabRows.Toggle(section, labelKey, get, set);
            }
            return new CmrDetailRow
            {
                Label = ModText(labelKey),
                Tooltip = ModTip("ColonyManagerRedux.Mining.HaulChunks.Disabled.Tip"),
                Disabled = true,
                SectionTitle = section,
            };
        }

        /// <summary>
        /// The mod draws this pair in a section of its own, with no heading of any kind
        /// (ManagerTab_Mining.cs:632-638). The ancient-danger child row's tail grows the mod's own
        /// warning sentence when the map has no ancient danger rect for it to act on
        /// (ManagerTab_Mining.cs:373, 386-402).
        /// </summary>
        private static void AddDeconstructBuildings(CmrDetailRegion region, object job)
        {
            region.Rows.Add(CmrTabRows.Toggle(null,
                "ColonyManagerRedux.Mining.DeconstructBuildings",
                () => CmrCompat.Mining.DeconstructBuildings(job),
                value => CmrCompat.Mining.SetDeconstructBuildings(job, value)));

            if (!CmrCompat.Mining.DeconstructBuildings(job))
            {
                return;
            }
            region.Rows.Add(new CmrDetailRow
            {
                Label = ModText("ColonyManagerRedux.Mining.DeconstructAncientDangerWhenFogged"),
                Tooltip = () => AncientDangerTail(job),
                Role = ElementRole.Checkbox,
                Check = () => CmrCompat.Mining.DeconstructAncientDangerWhenFogged(job)
                    ? CheckState.Checked
                    : CheckState.Unchecked,
                Activate = () => CmrCompat.Mining.SetDeconstructAncientDangerWhenFogged(job,
                    !CmrCompat.Mining.DeconstructAncientDangerWhenFogged(job)),
            });
        }

        private static string AncientDangerTail(object job)
        {
            string tip = ModText("ColonyManagerRedux.Mining.DeconstructAncientDangerWhenFogged.Tip");
            if (CmrCompat.Mining.AncientDangerRectCount(job) > 0)
            {
                return Flatten(tip);
            }
            var parts = new List<string> { tip, ModText("ColonyManagerRedux.Mining.CannotFindAncientDanger") };
            return Flatten(CompatText.JoinSentences(parts));
        }

        /// <summary>The area strip and its invert toggle (ManagerTab_Mining.cs:408-420).</summary>
        private static void AddMiningArea(CmrDetailRegion region, object job)
        {
            string label = ModText("ColonyManagerRedux.Mining.MiningArea");
            region.Rows.Add(CmrTabRows.AreaRow(
                label,
                () => CmrCompat.Mining.MiningArea(job),
                area => CmrCompat.Mining.SetMiningArea(job, area),
                () => CmrCompat.JobBase.MapOf(job)));
            region.Rows.Add(CmrTabRows.Toggle(null,
                "ColonyManagerRedux.InvertArea",
                () => CmrCompat.Mining.InvertMiningArea(job),
                value => CmrCompat.Mining.SetInvertMiningArea(job, value)));
        }

        /// <summary>
        /// MineThickRoofs, CheckRoofSupport, the advanced check that only exists as a real control
        /// while CheckRoofSupport is true, then CheckRoomDivision (ManagerTab_Mining.cs:422-473).
        /// </summary>
        private static void AddHealthAndSafety(CmrDetailRegion region, object job)
        {
            string section = ModText("ColonyManagerRedux.Mining.HealthAndSafety");
            region.Rows.Add(CmrTabRows.Toggle(section,
                "ColonyManagerRedux.Mining.MineThickRoofs",
                () => CmrCompat.Mining.MineThickRoofs(job),
                value => CmrCompat.Mining.SetMineThickRoofs(job, value)));
            region.Rows.Add(CmrTabRows.Toggle(section,
                "ColonyManagerRedux.Mining.CheckRoofSupport",
                () => CmrCompat.Mining.CheckRoofSupport(job),
                value => CmrCompat.Mining.SetCheckRoofSupport(job, value)));

            if (CmrCompat.Mining.CheckRoofSupport(job))
            {
                region.Rows.Add(CmrTabRows.Toggle(section,
                    "ColonyManagerRedux.Mining.CheckRoofSupportAdvanced",
                    () => CmrCompat.Mining.CheckRoofSupportAdvanced(job),
                    value => CmrCompat.Mining.SetCheckRoofSupportAdvanced(job, value)));
            }
            else
            {
                region.Rows.Add(DisabledRow(section,
                    "ColonyManagerRedux.Mining.CheckRoofSupportAdvanced",
                    "ColonyManagerRedux.Mining.CheckRoofSupportAdvanced.Disabled.Tip"));
            }

            region.Rows.Add(CmrTabRows.Toggle(section,
                "ColonyManagerRedux.Mining.CheckRoomDivision",
                () => CmrCompat.Mining.CheckRoomDivision(job),
                value => CmrCompat.Mining.SetCheckRoomDivision(job, value)));
        }

        // ------------------------------------------------------------------
        // Minerals: the middle column.
        // ------------------------------------------------------------------

        private static CmrDetailRegion BuildMinerals(object job)
        {
            var region = new CmrDetailRegion(ModText("ColonyManagerRedux.Mining.AllowedMinerals"));
            List<ThingDef> all = CmrCompat.Mining.AllMinerals(job);

            CmrTabRows.AddShortcutRows(region, all, CmrCompat.Shortcuts.Minerals(),
                def => CmrCompat.Mining.IsMineralAllowed(job, def),
                (def, allow) => CmrCompat.Mining.SetAllowMineral(job, def, allow));

            CmrTabRows.AddRefreshAndLock(region,
                "RimWorldAccess.Cmr.Mining.RefreshMinerals", "RimWorldAccess.Cmr.Mining.MineralsRefreshed",
                () => CmrCompat.Mining.RefreshAllBuildingsAndMinerals(job),
                "RimWorldAccess.Cmr.Mining.LockMineralsToMap",
                () => CmrCompat.Mining.MineralsLockedToMap(job),
                value => CmrCompat.Mining.SetMineralsLockedToMap(job, value));

            foreach (ThingDef mineral in all)
            {
                ThingDef def = mineral;
                region.Rows.Add(new CmrDetailRow
                {
                    Label = def.LabelCap,
                    Role = ElementRole.Checkbox,
                    Check = () => CmrCompat.Mining.IsMineralAllowed(job, def)
                        ? CheckState.Checked
                        : CheckState.Unchecked,
                    Activate = () => CmrCompat.Mining.SetAllowMineral(job, def,
                        !CmrCompat.Mining.IsMineralAllowed(job, def)),
                    Tooltip = () => Flatten(CmrCompat.Mining.MineralTooltip(def)),
                    InfoCardDef = def,
                });
            }
            return region;
        }

        // ------------------------------------------------------------------
        // Buildings: the right column, only while DeconstructBuildings is on.
        // ------------------------------------------------------------------

        /// <summary>This column draws no refresh icon of its own (ManagerTab_Mining.cs:703-741): the Minerals column's refresh already covers both lists.</summary>
        private static CmrDetailRegion BuildBuildings(object job)
        {
            var region = new CmrDetailRegion(ModText("ColonyManagerRedux.Mining.AllowedBuildings"));
            List<ThingDef> all = CmrCompat.Mining.AllDeconstructibleBuildings(job);

            Func<ThingDef, bool> isAllowed = def => CmrCompat.Mining.IsBuildingAllowed(job, def);
            Action<ThingDef, bool> setAllowed = (def, allow) => CmrCompat.Mining.SetBuildingAllowed(job, def, allow);
            CmrTabRows.AddShortcutRows(region, all, new List<CmrShortcutCategory<ThingDef>>(), isAllowed, setAllowed);
            foreach (KeyValuePair<DesignationCategoryDef, List<ThingDef>> category in CmrCompat.Shortcuts.BuildingCategories(all))
            {
                CmrTabRows.AddShortcutRow(region, category.Value, all, category.Key.LabelCap,
                    ModArgs("ColonyManagerRedux.Mining.BuildingCategory.Tip", category.Key.label), false,
                    isAllowed, setAllowed);
            }

            region.Rows.Add(CmrTabRows.LockRow("RimWorldAccess.Cmr.Mining.LockBuildingsToMap",
                () => CmrCompat.Mining.BuildingsLockedToMap(job),
                value => CmrCompat.Mining.SetBuildingsLockedToMap(job, value)));

            foreach (ThingDef building in all)
            {
                ThingDef def = building;
                region.Rows.Add(new CmrDetailRow
                {
                    Label = def.LabelCap,
                    Role = ElementRole.Checkbox,
                    Check = () => CmrCompat.Mining.IsBuildingAllowed(job, def)
                        ? CheckState.Checked
                        : CheckState.Unchecked,
                    Activate = () => CmrCompat.Mining.SetBuildingAllowed(job, def,
                        !CmrCompat.Mining.IsBuildingAllowed(job, def)),
                    Tooltip = () => Flatten(CmrCompat.Mining.BuildingTooltip(def)),
                    InfoCardDef = def,
                });
            }
            return region;
        }

        // ------------------------------------------------------------------
        // Threshold, task-order and area helpers.
        // ------------------------------------------------------------------

        private static string ThresholdText(object job, string key)
        {
            return ModArgs(key,
                CmrCompat.Mining.CurrentCount(job),
                CmrCompat.Mining.ChunkCount(job),
                CmrCompat.Mining.DesignatedCount(job),
                CmrCompat.Mining.TargetLabel(job));
        }

        /// <summary>The tooltip's own fifth argument: the mod's own wording for what the job's counted minerals will drop (ManagerTab_Mining.cs:497-503).</summary>
        private static string ThresholdTipText(object job)
        {
            string kind = ModText("ColonyManagerRedux.Mining.TargetCount.Tip." + CmrCompat.Mining.ChunkProductKindName(job));
            return ModArgs("ColonyManagerRedux.Mining.TargetCount.Tip",
                CmrCompat.Mining.CurrentCount(job),
                CmrCompat.Mining.ChunkCount(job),
                CmrCompat.Mining.DesignatedCount(job),
                CmrCompat.Mining.TargetLabel(job),
                kind);
        }

        /// <summary>
        /// Re-derives the task's current index from the live list rather than trusting a captured
        /// one: a swap rebuilds every row, so the row that asked for it may no longer sit at the
        /// index it was built at.
        /// </summary>
        private static bool CanSwapTask(object job, object task, int direction)
        {
            List<object> tasks = CmrCompat.Mining.TaskPriorityOrder(job);
            int index = tasks.IndexOf(task);
            if (index < 0)
            {
                return false;
            }
            return direction < 0 ? index > 0 : index < tasks.Count - 1;
        }

        private static void SwapTask(object job, object task, int direction)
        {
            List<object> tasks = CmrCompat.Mining.TaskPriorityOrder(job);
            int index = tasks.IndexOf(task);
            if (index < 0)
            {
                return;
            }
            int other = index + direction;
            if (other < 0 || other >= tasks.Count)
            {
                return;
            }
            CmrCompat.Mining.SwapTasks(job, index, other);
        }

        // ------------------------------------------------------------------
        // Row and text plumbing.
        // ------------------------------------------------------------------

        /// <summary>
        /// A row for a control the mod currently draws as a greyed, inert label rather than a real
        /// toggle (TakeOwnershipOfMiningJobs while mining is off, the advanced roof check while basic
        /// roof checking is off): no <see cref="CmrDetailRow.Check"/> and no
        /// <see cref="CmrDetailRow.Activate"/>, so it never claims a checked/unchecked state or
        /// accepts Enter, and its tooltip is the mod's own <c>.Disabled.Tip</c> wording, which already
        /// opens with why it is unavailable.
        /// </summary>
        private static CmrDetailRow DisabledRow(string section, string labelKey, string disabledTipKey)
        {
            return new CmrDetailRow
            {
                Label = ModText(labelKey),
                Tooltip = ModTip(disabledTipKey),
                Role = ElementRole.None,
                SectionTitle = section,
            };
        }
    }
}
