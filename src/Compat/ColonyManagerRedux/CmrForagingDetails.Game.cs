using System;
using System.Collections.Generic;
using Verse;
using static RimWorldAccess.Shell.CompatText;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Foraging tab's detail rows: the mod's two columns as two regions, mirroring
    /// <see cref="CmrHuntingDetails"/>'s shape.
    ///
    /// Options carries the left column: the whole threshold section (summary, count, the two
    /// threshold-scope toggles the trigger itself draws, synchronize, reachability, path-based
    /// distance -- in that order, the mod's own draw order for this tab, which puts reachability
    /// before path-based distance where Hunting's own tab draws them the other way round), the
    /// foraging-area strip with its invert toggle, and the "only fully matured plants" toggle. Plants
    /// carries the right column: the group shortcuts, the refresh and padlock icons, and one row per
    /// plant.
    ///
    /// Unlike Hunting, Foraging has no conditional rows in its options column: every section and
    /// toggle here is unconditional, so <see cref="Build"/> never needs a rebuild trigger the way a
    /// radio change would.
    /// </summary>
    internal sealed class CmrForagingDetails : ICmrJobDetailsProvider
    {
        public bool Handles(object tab)
        {
            return CmrCompat.Foraging.HandlesTab(tab);
        }

        public List<CmrDetailRegion> Build(object tab, object job)
        {
            var regions = new List<CmrDetailRegion>();
            if (job == null || !CmrCompat.Foraging.Ready)
            {
                return regions;
            }
            regions.Add(BuildOptions(job));
            regions.Add(BuildPlants(job));
            return regions;
        }

        // ------------------------------------------------------------------
        // Options: the left column.
        // ------------------------------------------------------------------

        private static CmrDetailRegion BuildOptions(object job)
        {
            var region = new CmrDetailRegion("RimWorldAccess.Cmr.OptionsRegion".Translate());
            AddThreshold(region, job);
            AddForagingArea(region, job);
            AddForceFullyMature(region, job);
            return region;
        }

        private static void AddThreshold(CmrDetailRegion region, object job)
        {
            // Reachability then path-based distance -- this tab's own draw order
            // (ManagerTab_Foraging.cs:288-296), the reverse of Hunting's.
            CmrTabRows.AddThresholdRows(region, job, "Foraging",
                "RimWorldAccess.Cmr.Threshold.TargetCountRow",
                () => ThresholdText(job, "ColonyManagerRedux.Foraging.TargetCount"),
                () => Flatten(ThresholdText(job, "ColonyManagerRedux.Foraging.TargetCountTooltip")),
                () => CmrCompat.Foraging.OpenThresholdDetails(job),
                () => CmrCompat.Foraging.TargetLabel(job),
                () => CmrCompat.Foraging.SyncFilterAndAllowed(job),
                value => CmrCompat.Foraging.SetSyncFilterAndAllowed(job, value),
                reachBeforePath: true);
        }

        /// <summary>
        /// The area strip and its invert toggle. The strip's cells share one heading and one value, so
        /// the row wears that heading as its label and carries no section title of its own, exactly as
        /// Hunting's hunting-grounds row does.
        /// </summary>
        private static void AddForagingArea(CmrDetailRegion region, object job)
        {
            region.Rows.Add(CmrTabRows.AreaRow(
                ModText("ColonyManagerRedux.Foraging.ForagingArea"),
                () => CmrCompat.Foraging.ForagingArea(job),
                area => CmrCompat.Foraging.SetForagingArea(job, area),
                () => CmrCompat.JobBase.MapOf(job)));
            region.Rows.Add(CmrTabRows.Toggle(null,
                "ColonyManagerRedux.InvertArea",
                () => CmrCompat.Foraging.InvertForagingArea(job),
                value => CmrCompat.Foraging.SetInvertForagingArea(job, value)));
        }

        /// <summary>The mod draws this one in a section of its own, with no heading of any kind.</summary>
        private static void AddForceFullyMature(CmrDetailRegion region, object job)
        {
            region.Rows.Add(CmrTabRows.Toggle(null,
                "ColonyManagerRedux.Foraging.ForceFullyMature",
                () => CmrCompat.Foraging.ForceFullyMature(job),
                value => CmrCompat.Foraging.SetForceFullyMature(job, value)));
        }

        // ------------------------------------------------------------------
        // Plants: the right column.
        // ------------------------------------------------------------------

        private static CmrDetailRegion BuildPlants(object job)
        {
            var region = new CmrDetailRegion(ModText("ColonyManagerRedux.Foraging.Plants"));
            List<ThingDef> all = CmrCompat.Foraging.AllPlants(job);

            CmrTabRows.AddShortcutRows(region, all, CmrCompat.Shortcuts.Foraging(),
                def => CmrCompat.Foraging.IsPlantAllowed(job, def),
                (def, allow) => CmrCompat.Foraging.SetPlantAllowed(job, def, allow));

            CmrTabRows.AddRefreshAndLock(region,
                "RimWorldAccess.Cmr.Foraging.RefreshPlants", "RimWorldAccess.Cmr.Foraging.PlantsRefreshed",
                () => CmrCompat.Foraging.RefreshAllPlants(job),
                "RimWorldAccess.Cmr.Foraging.LockPlantsToMap",
                () => CmrCompat.Foraging.PlantsLockedToMap(job),
                value => CmrCompat.Foraging.SetPlantsLockedToMap(job, value));

            foreach (ThingDef plant in all)
            {
                ThingDef def = plant;
                region.Rows.Add(new CmrDetailRow
                {
                    Label = def.LabelCap,
                    Role = ElementRole.Checkbox,
                    Check = () => CmrCompat.Foraging.IsPlantAllowed(job, def)
                        ? CheckState.Checked
                        : CheckState.Unchecked,
                    Activate = () => CmrCompat.Foraging.SetPlantAllowed(job, def,
                        !CmrCompat.Foraging.IsPlantAllowed(job, def)),
                    Tooltip = () => Flatten(CmrCompat.Foraging.PlantTooltip(def)),
                    InfoCardDef = def,
                });
            }
            return region;
        }

        // ------------------------------------------------------------------
        // Threshold count and area helpers.
        // ------------------------------------------------------------------

        /// <summary>
        /// The threshold label and its tooltip, composed from exactly the three values the mod's own
        /// section passes to the same two keys (ManagerTab_Foraging.cs:254-275): what is in storage
        /// now, what the outstanding designations will yield, and the trigger's own operator-and-target
        /// label.
        /// </summary>
        private static string ThresholdText(object job, string key)
        {
            return ModArgs(key,
                CmrCompat.Foraging.CurrentCount(job),
                CmrCompat.Foraging.DesignatedCount(job),
                CmrCompat.Foraging.TargetLabel(job));
        }

    }
}
