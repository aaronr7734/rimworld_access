using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using static RimWorldAccess.Shell.CompatText;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Forestry tab's detail rows: the mod's two columns as two regions, in the order
    /// <see cref="CmrHuntingDetails"/> established for a threshold-driven job with a def-list column.
    ///
    /// Options carries the left column: the job-type radio pair, then a set of sections that switch
    /// entirely with that choice -- the clear-area multi-select while Type is ClearArea, or the whole
    /// threshold section, the logging-area strip and the saplings toggle while Type is Logging. Trees
    /// carries the right column: the group shortcuts (four always, four more only while clearing an
    /// area), the refresh and padlock icons, and one row per tree.
    ///
    /// Conditional rows appear and vanish with their own conditions exactly as the sighted rows do --
    /// the whole options half with the job type, the extra clearing shortcuts with it too, Flammable
    /// with whether any tree is nonflammable, Ugly with whether any tree is ugly. Nothing is presented
    /// that the mod is not drawing.
    ///
    /// DEFERRED: the magnifier beside the threshold label, shared with Hunting's own threshold section
    /// (<c>Trigger_Threshold.DrawTriggerConfig</c> draws it for every threshold job alike); see
    /// <see cref="CmrHuntingDetails"/>'s own remarks for why it is out of scope for this slice.
    /// </summary>
    internal sealed class CmrForestryDetails : ICmrJobDetailsProvider
    {
        public bool Handles(object tab)
        {
            return CmrCompat.Forestry.HandlesTab(tab);
        }

        public List<CmrDetailRegion> Build(object tab, object job)
        {
            var regions = new List<CmrDetailRegion>();
            if (job == null || !CmrCompat.Forestry.Ready)
            {
                return regions;
            }
            regions.Add(BuildOptions(job));
            regions.Add(BuildTrees(job));
            return regions;
        }

        // ------------------------------------------------------------------
        // Options: the left column.
        // ------------------------------------------------------------------

        private static CmrDetailRegion BuildOptions(object job)
        {
            var region = new CmrDetailRegion("RimWorldAccess.Cmr.OptionsRegion".Translate());
            AddJobType(region, job);
            if (CmrCompat.Forestry.IsClearArea(job))
            {
                AddClearArea(region, job);
            }
            if (CmrCompat.Forestry.IsLogging(job))
            {
                AddThreshold(region, job);
                AddLoggingArea(region, job);
                AddAllowSaplings(region, job);
            }
            return region;
        }

        /// <summary>
        /// The mod's type pair, in the enum's own declaration order (ClearArea, then Logging -- the
        /// order <c>Enum.GetValues</c> hands its draw loop, ManagerTab_Forestry.cs:259-277).
        /// Activating the current choice does nothing, matching the widget's empty "off" delegate.
        /// </summary>
        private static void AddJobType(CmrDetailRegion region, object job)
        {
            string section = ModText("ColonyManagerRedux.Forestry.JobType");
            foreach (object value in CmrCompat.Forestry.JobTypeValues())
            {
                string keyBase = "ColonyManagerRedux.Forestry.JobType."
                    + CmrCompat.Forestry.JobTypeName(value);
                object choice = value;
                region.Rows.Add(new CmrDetailRow
                {
                    Label = ModText(keyBase),
                    Tooltip = ModTip(keyBase + ".Tip"),
                    Role = ElementRole.RadioButton,
                    SectionTitle = section,
                    Selected = () => IsCurrentType(job, choice),
                    Activate = delegate
                    {
                        if (!IsCurrentType(job, choice))
                        {
                            CmrCompat.Forestry.SetJobType(job, choice);
                        }
                    },
                });
            }
        }

        private static bool IsCurrentType(object job, object value)
        {
            object current = CmrCompat.Forestry.JobType(job);
            return current != null && current.Equals(value);
        }

        /// <summary>
        /// The clear-area multi-select strip: one toggle per assignable area, no unrestricted cell
        /// (AreaAllowedGUI.DoAllowedAreaSelectorsMC draws no "none" option, unlike the single-select
        /// strips). Pruned against deleted areas first, the way the tab's own Refresh does
        /// (ManagerTab_Forestry.cs Refresh, via UpdateClearAreas).
        /// </summary>
        private static void AddClearArea(CmrDetailRegion region, object job)
        {
            string section = ModText("ColonyManagerRedux.Forestry.JobType.ClearArea");
            CmrCompat.Forestry.UpdateClearAreas(job);
            List<Area> areas = AssignableAreas(job);
            for (int i = 0; i < areas.Count; i++)
            {
                Area area = areas[i];
                region.Rows.Add(new CmrDetailRow
                {
                    Label = AreaLabel(area),
                    Role = ElementRole.Checkbox,
                    SectionTitle = section,
                    Check = () => CmrCompat.Forestry.IsClearAreaAllowed(job, area)
                        ? CheckState.Checked
                        : CheckState.Unchecked,
                    Activate = () => CmrCompat.Forestry.SetClearAreaAllowed(job, area,
                        !CmrCompat.Forestry.IsClearAreaAllowed(job, area)),
                });
            }
        }

        private static void AddThreshold(CmrDetailRegion region, object job)
        {
            // Forestry's own draw order swaps the reach/path pair from Hunting's
            // (ManagerTab_Forestry.cs:319-331 draws reachability before path-based distance;
            // ManagerTab_Hunting.cs:506-514 draws them the other way around) -- the two toggles'
            // storage is the shared ManagerJob base fields, so the shared job-base facade serves
            // every tab alike.
            CmrTabRows.AddThresholdRows(region, job, "Forestry",
                "RimWorldAccess.Cmr.Threshold.TargetCountRow",
                () => ThresholdText(job, "ColonyManagerRedux.Forestry.TargetCount"),
                () => Flatten(ThresholdText(job, "ColonyManagerRedux.Forestry.TargetCountTooltip")),
                () => CmrCompat.Forestry.OpenThresholdDetails(job),
                () => CmrCompat.Forestry.TargetLabel(job),
                () => CmrCompat.Forestry.SyncFilterAndAllowed(job),
                value => CmrCompat.Forestry.SetSyncFilterAndAllowed(job, value),
                reachBeforePath: true);
        }

        /// <summary>
        /// The single-select logging-area strip and its invert toggle. The strip's cells share one
        /// heading and one value, so the row wears that heading as its label and carries no section
        /// title of its own -- the section crossing is already audible in the label.
        /// </summary>
        private static void AddLoggingArea(CmrDetailRegion region, object job)
        {
            region.Rows.Add(CmrTabRows.AreaRow(
                ModText("ColonyManagerRedux.Forestry.LoggingArea"),
                () => CmrCompat.Forestry.LoggingArea(job),
                area => CmrCompat.Forestry.SetLoggingArea(job, area),
                () => CmrCompat.JobBase.MapOf(job)));
            region.Rows.Add(CmrTabRows.Toggle(null,
                "ColonyManagerRedux.InvertArea",
                () => CmrCompat.Forestry.InvertLoggingArea(job),
                value => CmrCompat.Forestry.SetInvertLoggingArea(job, value)));
        }

        /// <summary>
        /// The mod draws this one in a section of its own, with no heading of any kind. The mod's own
        /// checkbox reads "AllowSaplings" but checks/unchecks the REVERSE of that field
        /// (ManagerTab_Forestry.cs DrawAllowSaplings: "AllowSaplings logic is the reverse from the
        /// label that is shown to the user"), so the facade's OnlyFullyMaturedTrees IS the checkbox's
        /// own displayed state and stays uninverted here -- inverting it would show a state the
        /// sighted checkbox never shows.
        /// </summary>
        private static void AddAllowSaplings(CmrDetailRegion region, object job)
        {
            region.Rows.Add(CmrTabRows.Toggle(null, "ColonyManagerRedux.Forestry.AllowSaplings",
                () => CmrCompat.Forestry.OnlyFullyMaturedTrees(job),
                value => CmrCompat.Forestry.SetOnlyFullyMaturedTrees(job, value)));
        }

        // ------------------------------------------------------------------
        // Trees: the right column.
        // ------------------------------------------------------------------

        private static CmrDetailRegion BuildTrees(object job)
        {
            var region = new CmrDetailRegion(ModText("ColonyManagerRedux.Forestry.Trees"));
            List<ThingDef> all = CmrCompat.Forestry.AllPlants(job);

            Func<ThingDef, bool> isAllowed = def => CmrCompat.Forestry.IsTreeAllowed(job, def);
            Action<ThingDef, bool> setAllowed = (def, allow) => CmrCompat.Forestry.SetTreeAllowed(job, def, allow);
            CmrTabRows.AddShortcutRows(region, all, CmrCompat.Shortcuts.Forestry(clearArea: false), isAllowed, setAllowed);
            if (CmrCompat.Forestry.IsClearArea(job))
            {
                CmrTabRows.AddShortcutRows(region, all, CmrCompat.Shortcuts.Forestry(clearArea: true), isAllowed,
                    setAllowed, includeAllRow: false);
            }

            CmrTabRows.AddRefreshAndLock(region,
                "RimWorldAccess.Cmr.Forestry.RefreshTrees", "RimWorldAccess.Cmr.Forestry.TreesRefreshed",
                () => CmrCompat.Forestry.RefreshAllTrees(job),
                "RimWorldAccess.Cmr.Forestry.LockTreesToMap",
                () => CmrCompat.Forestry.PlantsLockedToMap(job),
                value => CmrCompat.Forestry.SetPlantsLockedToMap(job, value));

            foreach (ThingDef tree in all)
            {
                ThingDef def = tree;
                region.Rows.Add(new CmrDetailRow
                {
                    Label = def.LabelCap,
                    Role = ElementRole.Checkbox,
                    Check = () => CmrCompat.Forestry.IsTreeAllowed(job, def)
                        ? CheckState.Checked
                        : CheckState.Unchecked,
                    Activate = () => CmrCompat.Forestry.SetTreeAllowed(job, def,
                        !CmrCompat.Forestry.IsTreeAllowed(job, def)),
                    Tooltip = () => Flatten(CmrCompat.Forestry.PlantTooltip(def)),
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
        /// section passes to the same two keys (ManagerTab_Forestry.cs:285-303): the current count,
        /// the outstanding designated count, and the trigger's own operator-and-target label.
        /// </summary>
        private static string ThresholdText(object job, string key)
        {
            return ModArgs(key,
                CmrCompat.Forestry.CurrentCount(job),
                CmrCompat.Forestry.DesignatedCount(job),
                CmrCompat.Forestry.TargetLabel(job));
        }

        /// <summary>Every assignable area on the job's map, with no unrestricted entry -- the multi-select strip's own cells (AreaAllowedGUI.DoAllowedAreaSelectorsMC).</summary>
        private static List<Area> AssignableAreas(object job)
        {
            var areas = new List<Area>();
            Map map = CmrCompat.JobBase.MapOf(job);
            if (map != null)
            {
                List<Area> all = map.areaManager.AllAreas;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].AssignableAsAllowed())
                    {
                        areas.Add(all[i]);
                    }
                }
            }
            return areas;
        }

        /// <summary>The strip's own cell label, from the game's own area labelling (AreaAllowedGUI.cs:255).</summary>
        private static string AreaLabel(Area area)
        {
            return AreaUtility.AreaAllowedLabel_Area(area).StripTags();
        }
    }
}
