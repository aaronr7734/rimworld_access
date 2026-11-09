using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard support for Colony Manager Redux's manager window
    /// (<c>ColonyManagerRedux.MainTabWindow_Manager</c>), registered through
    /// <see cref="ScopeForWindow.Register"/>. Content regions, plus the automatic Buttons region:
    /// the manager tab strip (labelled by each icon's own hover tooltip; icons switched off in mod
    /// settings are not drawn and not presented, while a tab the game blocks stays a navigable,
    /// disabled row carrying its reason); the selected tab's job list as the mod draws it (Overview's
    /// with its group headers; none on Import/Export and Power; on Logs Enter toggles the log
    /// filter); then one region per column of the selected job's detail pane, from whichever
    /// <see cref="ICmrJobDetailsProvider"/> handles the tab. Selection follows the cursor: landing on
    /// a job row writes the tab's <c>Selected</c> exactly as a mouse click does, silently.
    /// <c>MainTabWindow</c> overrides neither <c>OnAcceptKeyPressed</c> nor <c>OnCancelKeyPressed</c>,
    /// so <see cref="ScreenScope"/>'s defaults are correct.
    /// </summary>
    internal sealed class CmrManagerScope : ScreenScope
    {
        private const int TabsRegion = 0;
        private const int JobsRegion = 1;

        /// <summary>The first region index a job's detail pane occupies; everything from here on is provider-supplied.</summary>
        private const int FirstDetailRegion = 2;

        private readonly Window window;
        private readonly List<object> tabs = new List<object>();
        private readonly List<object> jobs = new List<object>();

        private readonly List<CmrOverviewGroup> entryGroups = new List<CmrOverviewGroup>();
        private readonly List<CmrDetailRegion> detailRegions = new List<CmrDetailRegion>();
        private readonly HashSet<string> capturedTwins = new HashSet<string>();
        private object currentTab;
        private object jobTracker;

        // Voices the Manage! commit: the button flips this job's IsManaged but says nothing itself.
        private object manageWatchJob;
        private bool manageWatchWasManaged;

        private readonly CmrRowEditor editor = new CmrRowEditor();

        /// <summary>
        /// The section heading of the most recently landed-on detail row, spoken once when the cursor
        /// crosses into a new group so a flat row list keeps the grouping a sighted player reads off
        /// the headings. No rebuild-reset is needed: the row list is rebuilt from live job state every
        /// pass and the comparison is by heading TEXT. Headingless rows count as landings, so leaving
        /// a group and returning re-announces it.
        /// </summary>
        private readonly SectionPrefixTracker sectionPrefix = new SectionPrefixTracker(trackSilentRows: true, speakFirstLanding: true);

        public CmrManagerScope(Window window)
        {
            this.window = window;
            // The unified Alt+I drill-in, claimed only while the focused row names a carded def, so
            // the chord falls through everywhere else on this screen.
            Claim(SharedMenuGrammar.Info, e => OpenRowInfoCard(), when: CurrentRowHasInfoCard);

            Claim("cmrManager.cyclePriorityDown", e => AdjustCurrentCell(-1), when: () => CurrentCellAdjustable());
            Claim("cmrManager.cyclePriorityUp", e => AdjustCurrentCell(1), when: () => CurrentCellAdjustable());
            Claim("cmrManager.paintDown", e => PaintDetailRow(1), when: () => CurrentRowPaintable());
            Claim("cmrManager.paintUp", e => PaintDetailRow(-1), when: () => CurrentRowPaintable());
            Claim("cmrManager.shortcutRestrict", e => ShortcutClick(CmrShortcutClick.Restrict), when: CurrentRowIsShortcut);
            Claim("cmrManager.shortcutAdd", e => ShortcutClick(CmrShortcutClick.Add), when: CurrentRowIsShortcut);
            Claim("cmrManager.shortcutRemove", e => ShortcutClick(CmrShortcutClick.Remove), when: CurrentRowIsShortcut);

            RegisterPopTeardown(editor.CancelIfActive);
        }

        public override string Name
        {
            get { return "cmr-manager"; }
        }

        protected internal override Window OwnedWindow
        {
            get { return window; }
        }

        /// <summary>Tabs and jobs are both named lists worth searching.</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>
        /// The job tabs' Manage/Delete buttons are real window actions worth capturing; on
        /// Import/Export and Power every text button is content the provider already models with its
        /// target attached, so capturing there would duplicate rows stripped of their context.
        /// </summary>
        protected override bool CaptureWindowButtons
        {
            get
            {
                object tab = CmrCompat.CurrentTab;
                return !CmrCompat.ImportExport.HandlesTab(tab) && !CmrCompat.Power.HandlesTab(tab);
            }
        }

        protected override int ContentRegionCount
        {
            get { return FirstDetailRegion + detailRegions.Count; }
        }

        protected override string ContentRegionName(int region)
        {
            if (region == TabsRegion)
            {
                return "RimWorldAccess.Cmr.TabsRegion".Translate();
            }
            if (region == JobsRegion)
            {
                return "RimWorldAccess.Cmr.JobsRegion".Translate();
            }
            CmrDetailRegion detail = DetailRegion(region);
            return detail == null ? "" : detail.Name;
        }

        private CmrDetailRegion DetailRegion(int region)
        {
            int index = region - FirstDetailRegion;
            return index >= 0 && index < detailRegions.Count ? detailRegions[index] : null;
        }

        private CmrDetailRow DetailRow(int region, int index)
        {
            CmrDetailRegion detail = DetailRegion(region);
            if (detail == null || index < 0 || index >= detail.Rows.Count)
            {
                return null;
            }
            return detail.Rows[index];
        }

        /// <summary>The embedded pawn table a detail region carries, or null for a flat row region.</summary>
        private CmrDetailPawnTable DetailTable(int region)
        {
            CmrDetailRegion detail = DetailRegion(region);
            return detail == null ? null : detail.Table;
        }

        /// <summary>
        /// Re-reads the whole screen from live mod state every pass, so a tab switch made anywhere else
        /// lands here silently with no cached-tab comparison to keep in step. A map without a manager
        /// component, or a mod surface that declined, reports zero rows rather than throwing.
        /// </summary>
        protected override void RefreshContent()
        {
            tabs.Clear();
            jobs.Clear();
            entryGroups.Clear();
            detailRegions.Clear();
            currentTab = null;
            jobTracker = null;

            object manager = CmrCompat.ManagerFor(Find.CurrentMap);
            if (manager == null)
            {
                return;
            }
            jobTracker = CmrCompat.JobTracker(manager);
            foreach (object tab in CmrCompat.Tabs(manager))
            {
                if (CmrCompat.TabShow(tab))
                {
                    tabs.Add(tab);
                }
            }

            currentTab = CmrCompat.CurrentTab;
            if (currentTab == null)
            {
                return;
            }
            // Import/Export and Power replace the tab layout wholesale and draw no job list; their
            // providers model everything those tabs do draw.
            if (CmrCompat.OverviewList.HandlesTab(currentTab))
            {
                AddOverviewEntries();
            }
            else if (!CmrCompat.ImportExport.HandlesTab(currentTab)
                && !CmrCompat.Power.HandlesTab(currentTab))
            {
                jobs.AddRange(CmrCompat.TabJobs(currentTab));
            }

            // The detail pane belongs to the job the tab has selected. The selected job may be null:
            // providers with job-independent regions still answer, the rest return nothing.
            object selectedJob = CmrCompat.TabSelectedJob(currentTab);
            detailRegions.AddRange(CmrJobDetails.Build(currentTab, selectedJob));
            WatchManageCommit(selectedJob);

            capturedTwins.Clear();
            for (int r = 0; r < detailRegions.Count; r++)
            {
                List<CmrDetailRow> rows = detailRegions[r].Rows;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (!string.IsNullOrEmpty(rows[i].CapturedTwin))
                    {
                        capturedTwins.Add(rows[i].CapturedTwin);
                    }
                }
            }
        }

        /// <summary>Overview: header rows carry a null job; collapse is the mod's own persisted state.</summary>
        private void AddOverviewEntries()
        {
            foreach (CmrOverviewGroup group in CmrCompat.OverviewList.Groups(currentTab))
            {
                if (group.Header != null)
                {
                    jobs.Add(null);
                    entryGroups.Add(group);
                    if (CmrCompat.OverviewList.IsCollapsed(currentTab, group.Key))
                    {
                        continue;
                    }
                }
                foreach (object job in group.Jobs)
                {
                    jobs.Add(job);
                    entryGroups.Add(group);
                }
            }
        }

        private CmrOverviewGroup HeaderAt(int index)
        {
            return index >= 0 && index < jobs.Count && jobs[index] == null && index < entryGroups.Count
                ? entryGroups[index]
                : null;
        }

        private ElementDescription DescribeGroupHeader(CmrOverviewGroup group)
        {
            return new ElementDescription
            {
                Label = group.Header + " (" + group.Jobs.Count + ")",
                Role = ElementRole.TreeItem,
                Expanded = !CmrCompat.OverviewList.IsCollapsed(currentTab, group.Key),
            };
        }

        private void OpenGroupActions(CmrOverviewGroup group)
        {
            bool opened = CmrRowEditor.OpenPicker(CmrOverviewDetails.GroupActionChoices(currentTab, group), null,
                group.Header, delegate
                {
                    RefreshModel();
                    AnnounceCurrentItem();
                });
            if (!opened)
            {
                AnnounceCurrentItem();
            }
        }

        private bool CanAdjustOverviewEntry(int index, int direction)
        {
            if (!CmrCompat.OverviewList.HandlesTab(currentTab) || index < 0 || index >= entryGroups.Count)
            {
                return false;
            }
            CmrOverviewGroup header = HeaderAt(index);
            if (header != null)
            {
                return CmrCompat.OverviewList.IsCollapsed(currentTab, header.Key) == (direction > 0);
            }
            return direction < 0 && entryGroups[index] != null && entryGroups[index].Header != null;
        }

        private void AdjustOverviewEntry(int index, int direction)
        {
            CmrOverviewGroup header = HeaderAt(index);
            if (header != null)
            {
                CmrCompat.OverviewList.SetCollapsed(currentTab, header.Key, direction < 0);
                RefreshModel();
                TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                    DescribeGroupHeader(header), TranslatedShellVocabulary.Instance));
                return;
            }
            int headerIndex = -1;
            for (int i = index - 1; i >= 0; i--)
            {
                if (jobs[i] == null)
                {
                    headerIndex = i;
                    break;
                }
            }
            ListModel region = Model.CurrentRegion;
            if (region != null && headerIndex >= 0)
            {
                region.MoveTo(headerIndex);
                NotifyCursorSettled();
                AnnounceCurrentItem();
            }
        }

        /// <summary>
        /// Watches IsManaged turn on for the same job across a refresh rather than reading the
        /// button's translated label. Identity-keyed: Delete re-selects a fresh unmanaged job whose
        /// off state must not read as a flip, and landing on an already-managed job stays silent.
        /// </summary>
        private void WatchManageCommit(object selectedJob)
        {
            bool managed = selectedJob != null && CmrCompat.JobIsManaged(selectedJob);
            if (selectedJob != null
                && ReferenceEquals(selectedJob, manageWatchJob)
                && !manageWatchWasManaged
                && managed)
            {
                TolkHelper.SpeakData("RimWorldAccess.Cmr.NowManaged".Translate().ToString());
            }
            manageWatchJob = selectedJob;
            manageWatchWasManaged = managed;
        }

        /// <summary>
        /// Drops a captured window button a detail row fully models
        /// (<see cref="CmrDetailRow.CapturedTwin"/>): the row carries the control's target, picker and
        /// echo, while the bare captured label would present it again with its context stripped.
        /// </summary>
        protected override bool KeepCapturedButton(string rawLabel)
        {
            return !capturedTwins.Contains(rawLabel);
        }

        protected override int ContentItemCount(int region)
        {
            if (region == TabsRegion)
            {
                return tabs.Count;
            }
            if (region == JobsRegion)
            {
                return jobs.Count;
            }
            CmrDetailRegion detail = DetailRegion(region);
            if (detail == null)
            {
                return 0;
            }
            return detail.Table != null ? detail.Table.Rows.Count : detail.Rows.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (region == TabsRegion)
            {
                return DescribeTabRow(index);
            }
            if (region == JobsRegion)
            {
                return DescribeJobRow(index);
            }
            CmrDetailPawnTable table = DetailTable(region);
            if (table != null)
            {
                return new ElementDescription { Label = table.RowLabel(index) };
            }
            return DescribeDetailRow(region, index);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == TabsRegion)
            {
                ActivateTabRow(index);
                return;
            }
            if (region == JobsRegion)
            {
                // On the Logs tab a job row IS the log filter and draws no stamp, so Enter toggles the
                // filter there and the suspend stamp everywhere else.
                CmrOverviewGroup header = HeaderAt(index);
                if (header != null)
                {
                    OpenGroupActions(header);
                }
                else if (CmrCompat.Logs.HandlesTab(currentTab))
                {
                    ToggleLogFilter(index);
                }
                else
                {
                    ToggleJobSuspended(index);
                }
                return;
            }
            if (DetailTable(region) != null)
            {
                // A table row with no cell action of its own: settle audibly.
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentItem();
                return;
            }
            ActivateDetailRow(region, index);
        }

        /// <summary>Mirrors the job row's own click (<c>Selected = job</c>), so the selection the mod's detail pane reads follows the keyboard cursor. Silent and idempotent.</summary>
        protected override void OnCursorSettled(int region, int index)
        {
            CmrDetailRow detail = DetailRow(region, index);
            if (detail != null)
            {
                if (detail.OnSettle != null)
                {
                    detail.OnSettle();
                }
                return;
            }
            if (region != JobsRegion || index < 0 || index >= jobs.Count || currentTab == null)
            {
                return;
            }
            object job = jobs[index];
            if (job != null && !ReferenceEquals(CmrCompat.TabSelectedJob(currentTab), job))
            {
                CmrCompat.SelectJob(currentTab, job);
            }
        }

        // Manager tabs.

        private ElementDescription DescribeTabRow(int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= tabs.Count)
            {
                return d;
            }
            object tab = tabs[index];
            d.Label = CmrCompat.TabLabel(tab);
            d.Role = ElementRole.Tab;
            d.Selected = ReferenceEquals(tab, currentTab);
            if (!CmrCompat.TabEnabled(tab))
            {
                d.Disabled = true;
                d.Extras = FlattenTooltip(CmrCompat.TabDisabledReason(tab));
            }
            return d;
        }

        private void ActivateTabRow(int index)
        {
            if (index < 0 || index >= tabs.Count)
            {
                return;
            }
            object tab = tabs[index];
            if (!CmrCompat.TabEnabled(tab))
            {
                SpeakTabRefusal(tab);
                return;
            }
            CmrCompat.GoTo(tab);
            RefreshModel();
            TolkHelper.SpeakData("RimWorldAccess.Cmr.TabSwitched".Translate(CmrCompat.TabLabel(tab)).ToString());
        }

        private static void SpeakTabRefusal(object tab)
        {
            string refusal = "RimWorldAccess.Shell.GenericWindow.Disabled"
                .Loc(CmrCompat.TabLabel(tab)).ToString();
            string reason = FlattenTooltip(CmrCompat.TabDisabledReason(tab));
            TolkHelper.SpeakData(string.IsNullOrEmpty(reason) ? refusal : refusal + " " + reason);
        }

        // Jobs.

        private ElementDescription DescribeJobRow(int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= jobs.Count)
            {
                return d;
            }
            CmrOverviewGroup header = HeaderAt(index);
            if (header != null)
            {
                return DescribeGroupHeader(header);
            }
            object job = jobs[index];
            d.Label = CmrCompat.JobLabel(job);
            d.Role = ElementRole.MenuItem;
            // The sub-label in the OWNING tab's own words, exactly what the sighted row composes;
            // the base virtual is TargetsLabel, kept as the decline fallback.
            object jobTab = CmrCompat.JobTab(job);
            string subLabel = CmrCompat.TabGetSubLabel(jobTab ?? currentTab, job);
            d.Value = string.IsNullOrEmpty(subLabel) ? CmrCompat.JobTargetsLabel(job) : subLabel;
            d.Extras = ComposeJobStatus(job);
            return d;
        }

        /// <summary>
        /// The job's state, the tooltip the sighted stamp carries for it, the progress bar's status
        /// text, then the clock's last-update reading and check interval — the mod's own row order.
        /// </summary>
        private static string ComposeJobStatus(object job)
        {
            var parts = new List<string>();
            if (CmrCompat.JobIsSuspended(job))
            {
                bool byError = CmrCompat.JobCausedException(job);
                parts.Add((byError
                    ? "RimWorldAccess.Cmr.SuspendedByError"
                    : "RimWorldAccess.Cmr.Suspended").Translate());
                parts.Add(FlattenTooltip(byError
                    ? CmrCompat.JobSuspendedByErrorTooltip(job)
                    : CmrCompat.JobSuspendedTooltip(job)));
            }
            else if (CmrCompat.JobIsCompleted(job))
            {
                parts.Add("RimWorldAccess.Cmr.Completed".Translate());
                parts.Add(FlattenTooltip(CmrCompat.JobCompletedTooltip(job)));
            }
            else if (!CmrCompat.JobIsManaged(job))
            {
                parts.Add("RimWorldAccess.Cmr.NotManaged".Translate());
            }
            else
            {
                parts.Add("RimWorldAccess.Cmr.Active".Translate());
            }

            parts.Add(FlattenTooltip(CmrCompat.JobProgressTooltip(job)));
            parts.Add(CmrCompat.JobHasBeenUpdated(job)
                ? "RimWorldAccess.Cmr.UpdatedAgo".Translate(
                    CmrCompat.JobTicksSinceLastUpdate(job).ToStringTicksToPeriod()).ToString()
                : "RimWorldAccess.Cmr.NeverUpdated".Translate().ToString());
            parts.Add("RimWorldAccess.Cmr.ChecksInterval"
                .Translate(CmrCompat.JobUpdateIntervalLabel(job)).ToString());
            return JoinSentences(parts);
        }

        /// <summary>
        /// The Logs tab's job-row click body: selecting filters the log list to that job, re-clicking
        /// deselects to show all. Landing already selected the row, so Enter reads as "show all logs
        /// again" until the cursor moves back onto a job row.
        /// </summary>
        private void ToggleLogFilter(int index)
        {
            if (index < 0 || index >= jobs.Count || currentTab == null)
            {
                return;
            }
            object job = jobs[index];
            bool clearing = ReferenceEquals(CmrCompat.TabSelectedJob(currentTab), job);
            CmrCompat.SelectJob(currentTab, clearing ? null : job);
            RefreshModel();
            TolkHelper.SpeakData(clearing
                ? "RimWorldAccess.Cmr.Logs.FilterCleared".Translate().ToString()
                : "RimWorldAccess.Cmr.Logs.FilterApplied".Translate(CmrCompat.JobLabel(job)).ToString());
        }

        private void ToggleJobSuspended(int index)
        {
            if (index < 0 || index >= jobs.Count)
            {
                return;
            }
            object job = jobs[index];
            bool suspended = !CmrCompat.JobIsSuspended(job);
            CmrCompat.SetJobSuspended(job, suspended);
            RefreshModel();
            TolkHelper.SpeakData((suspended
                ? "RimWorldAccess.Cmr.JobSuspended"
                : "RimWorldAccess.Cmr.JobUnsuspended").Translate(CmrCompat.JobLabel(job)).ToString());
        }

        // The selected job's detail rows (provider-supplied; see CmrDetailRow).

        private ElementDescription DescribeDetailRow(int region, int index)
        {
            return DescribeDetailRow(DetailRow(region, index));
        }

        private ElementDescription DescribeDetailRow(CmrDetailRow row)
        {
            var d = new ElementDescription();
            if (row == null)
            {
                return d;
            }
            d.Label = row.Label;
            d.Role = row.Role;
            d.Disabled = row.Disabled;
            if (row.Check != null)
            {
                d.Check = row.Check();
            }
            if (row.Selected != null)
            {
                d.Selected = row.Selected();
            }
            if (row.Value != null)
            {
                d.Value = row.Value();
            }
            if (row.Expanded != null)
            {
                d.Expanded = row.Expanded();
            }
            if (row.CanAdjust != null)
            {
                d.AtMinimum = !row.CanAdjust(-1);
                d.AtMaximum = !row.CanAdjust(1);
            }
            // A count or name row's Enter opens an edit session, so it is not inert for the proceed seam.
            d.EntersEditOnAccept = row.Numeric != null || row.Text != null;
            if (row.Tooltip != null)
            {
                d.Extras = row.Tooltip();
            }
            return d;
        }

        /// <summary>
        /// Enter on a detail row: type an exact count, open a picker, or perform the control's own
        /// action and echo what changed. Every branch re-reads the row after a fresh refresh, since a
        /// control can add or remove the rows below it exactly as it does for a sighted player.
        /// </summary>
        private void ActivateDetailRow(int region, int index)
        {
            CmrDetailRow row = DetailRow(region, index);
            if (row == null)
            {
                return;
            }
            if (row.Numeric != null)
            {
                BeginCountEdit(region, index, row);
                return;
            }
            if (row.Text != null)
            {
                BeginTextEdit(region, index, row);
                return;
            }
            if (row.Choices != null)
            {
                OpenRowPicker(region, index, row);
                return;
            }
            if (row.Activate == null)
            {
                AnnounceCurrentItem();
                return;
            }
            bool opensWindow = row.OpensWindow;
            row.Activate();
            RefreshModel();
            if (!opensWindow)
            {
                AnnounceDetailChange(row);
            }
        }

        /// <summary>
        /// The state-change echo: the row's identity was spoken on focus, so only its new state
        /// re-announces; a row carrying an explicit confirmation speaks that instead. Composed from
        /// the activated row OBJECT, not its index — the action can remove rows above itself, leaving
        /// the old index on a different row after the refresh.
        /// </summary>
        private void AnnounceDetailChange(CmrDetailRow row)
        {
            if (row != null && !string.IsNullOrEmpty(row.Confirmation))
            {
                TolkHelper.SpeakData(row.Confirmation);
                return;
            }
            if (row != null && (row.Check != null || row.Selected != null || row.Value != null
                || row.Expanded != null))
            {
                TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                    DescribeDetailRow(row), TranslatedShellVocabulary.Instance));
                return;
            }
            AnnounceCurrentItem();
        }

        private bool CurrentRowIsShortcut()
        {
            RefreshModel();
            CmrDetailRow row = CurrentDetailRow();
            return row != null && row.ShortcutClick != null;
        }

        private void ShortcutClick(CmrShortcutClick mode)
        {
            CmrDetailRow row = CurrentDetailRow();
            if (row == null || row.ShortcutClick == null)
            {
                return;
            }
            row.ShortcutClick(mode);
            RefreshModel();
            AnnounceDetailChange(row);
        }

        // -------------------------------------------------------------------
        // Paint (Shift+Down/Up): the keyboard mirror of CheckboxMulti(paintable: true)'s drag-paint.
        // -------------------------------------------------------------------

        private bool CurrentRowPaintable()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null)
            {
                return false;
            }
            CmrDetailRow row = DetailRow(Model.RegionIndex, region.Index);
            return row != null && row.SetChecked != null && row.Check != null;
        }

        /// <summary>
        /// Shift+Down/Up: carry the focused checkbox's state onto the adjacent row. Stops (re-announces
        /// in place) when the neighbour is not paintable, so the strip's bounds are audible; never
        /// wraps, like the drag. The cursor moved, so the echo is the FULL row, not a state-only echo.
        /// </summary>
        private void PaintDetailRow(int direction)
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null)
            {
                return;
            }
            int index = region.Index;
            CmrDetailRow source = DetailRow(Model.RegionIndex, index);
            if (source == null || source.SetChecked == null || source.Check == null)
            {
                return;
            }
            int targetIndex = index + direction;
            CmrDetailRow target = DetailRow(Model.RegionIndex, targetIndex);
            if (target == null || target.SetChecked == null)
            {
                AnnounceCurrentItem();
                return;
            }
            bool brush = source.Check() == CheckState.Checked;
            region.MoveTo(targetIndex);
            NotifyCursorSettled();
            target.SetChecked(brush);
            RefreshModel();
            AnnounceCurrentItem();
        }

        // -------------------------------------------------------------------
        // Embedded pawn tables: a detail region carrying a CmrDetailPawnTable answers the base's table
        // contract, so the mod's animal and worker tables read as real tables — header row, column
        // cursor, per-cell values and tips, and the table's own SortBy.
        // -------------------------------------------------------------------

        protected override int ContentColumnCount(int region)
        {
            CmrDetailPawnTable table = DetailTable(region);
            return table == null ? 0 : table.Columns.Count;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            CmrDetailPawnTable table = DetailTable(region);
            if (table == null || column < 0 || column >= table.Columns.Count)
            {
                return new TableColumnInfo("", null, sortable: false);
            }
            PawnColumnDef def = table.Columns[column];
            string label = CmrPawnTableFusion.ColumnHeader(def);
            // Suppress a tip the label already IS (an icon-headed column's headerTip promoted to it).
            string tip = OverriddenHeaderTip(def, table.Table)
                ?? (def.headerTip.NullOrEmpty() || def.headerTip == label ? null : def.headerTip);
            return new TableColumnInfo(label, tip, def.sortable);
        }

        private static readonly Dictionary<Type, MethodInfo> headerTipOverrides = new Dictionary<Type, MethodInfo>();

        /// <summary>A mod worker's own GetHeaderTip; vanilla's base only adds a click-to-sort instruction.</summary>
        private static string OverriddenHeaderTip(PawnColumnDef def, PawnTable pawnTable)
        {
            PawnColumnWorker worker = def.Worker;
            if (worker == null)
            {
                return null;
            }
            MethodInfo method;
            Type workerType = worker.GetType();
            if (!headerTipOverrides.TryGetValue(workerType, out method))
            {
                method = AccessTools.Method(workerType, "GetHeaderTip", new[] { typeof(PawnTable) });
                if (method != null && method.DeclaringType == typeof(PawnColumnWorker))
                {
                    method = null;
                }
                headerTipOverrides[workerType] = method;
            }
            if (method == null)
            {
                return null;
            }
            try
            {
                string tip = method.Invoke(worker, new object[] { pawnTable }) as string;
                return string.IsNullOrEmpty(tip) ? null : FlattenTooltip(tip);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("CmrManagerScope.OverriddenHeaderTip", ex);
                return null;
            }
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            CmrDetailPawnTable table = DetailTable(region);
            if (table == null || row < 0 || row >= table.Rows.Count
                || column < 0 || column >= table.Columns.Count)
            {
                return "";
            }
            return CmrPawnTableFusion.CellText(table.Columns[column], table.Rows[row]);
        }

        protected override string ContentCellTip(int region, int row, int column)
        {
            CmrDetailPawnTable table = DetailTable(region);
            if (table == null || row < 0 || row >= table.Rows.Count
                || column < 0 || column >= table.Columns.Count)
            {
                return null;
            }
            string tip = CmrPawnTableFusion.CellTip(table.Columns[column], table.Rows[row]);
            return string.IsNullOrEmpty(tip) ? null : tip;
        }

        protected override bool TryGetPawnCell(int region, int row, int column, out PawnColumnDef def, out Pawn pawn)
        {
            CmrDetailPawnTable table = DetailTable(region);
            bool inRange = table != null && row >= 0 && row < table.Rows.Count
                && column >= 0 && column < table.Columns.Count;
            def = inRange ? table.Columns[column] : null;
            pawn = inRange ? table.Rows[row] : null;
            return inRange;
        }

        protected override bool ActivateContentCell(int region, int row, int column)
        {
            CmrDetailPawnTable table = DetailTable(region);
            if (table == null || row < 0 || row >= table.Rows.Count
                || column < 0 || column >= table.Columns.Count)
            {
                return false;
            }
            switch (CmrPawnTableFusion.ActivateCell(table.Columns[column], table.Rows[row], table.Table))
            {
                case PawnColumnActivation.StateChanged:
                    // Toggle doctrine: the cursor didn't move — speak only the new state.
                    AnnounceCurrentCellStateChange();
                    return true;
                case PawnColumnActivation.OpenedUI:
                    // The float menu / dialog announces itself.
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Sorting runs through the mod's own table (<c>PawnTable.SortBy</c>), so the comparer is the
        /// column worker's language-independent Compare. The tracked pawn's new position keeps the
        /// cursor on it after the next refresh.
        /// </summary>
        protected override int ApplyContentSort(int region, int column, SortCycleResult cycle, int currentRow)
        {
            CmrDetailPawnTable table = DetailTable(region);
            if (table == null || table.Table == null || column < 0 || column >= table.Columns.Count)
            {
                return -1;
            }
            Pawn tracked = currentRow >= 0 && currentRow < table.Rows.Count ? table.Rows[currentRow] : null;
            if (cycle == SortCycleResult.Cleared)
            {
                table.Table.SortBy(null, descending: false);
            }
            else
            {
                table.Table.SortBy(table.Columns[column], cycle == SortCycleResult.SortedDescending);
            }
            List<Pawn> sorted = CmrPawnTableFusion.Rows(table.Table);
            if (tracked == null)
            {
                return 0;
            }
            int index = sorted.IndexOf(tracked);
            return index >= 0 ? index : 0;
        }

        /// <summary>The bracket chords claim only on a data cell whose column handler has a value axis (the Overview priority column); everywhere else they fall through.</summary>
        private bool CurrentCellAdjustable()
        {
            RefreshModel();
            CmrDetailPawnTable table = DetailTable(Model.RegionIndex);
            TableModel model = Model.CurrentTable;
            if (table == null || model == null)
            {
                return false;
            }
            int row = model.Rows.Index - 1;
            int column = model.ColumnIndex;
            if (row < 0 || row >= table.Rows.Count || column < 0 || column >= table.Columns.Count)
            {
                return false;
            }
            return CmrPawnTableFusion.CanAdjustCell(table.Columns[column]);
        }

        private void AdjustCurrentCell(int direction)
        {
            RefreshModel();
            CmrDetailPawnTable table = DetailTable(Model.RegionIndex);
            TableModel model = Model.CurrentTable;
            if (table == null || model == null)
            {
                return;
            }
            int row = model.Rows.Index - 1;
            int column = model.ColumnIndex;
            if (row < 0 || row >= table.Rows.Count || column < 0 || column >= table.Columns.Count)
            {
                return;
            }
            if (CmrPawnTableFusion.AdjustCell(table.Columns[column], table.Rows[row], direction, table.Table)
                == PawnColumnActivation.StateChanged)
            {
                AnnounceCurrentCellStateChange();
            }
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            if (region == JobsRegion)
            {
                return CanAdjustOverviewEntry(index, -1) || CanAdjustOverviewEntry(index, 1);
            }
            CmrDetailRow row = DetailRow(region, index);
            return row != null && row.Adjust != null
                && (row.CanAdjust == null || row.CanAdjust(-1) || row.CanAdjust(1));
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (region == JobsRegion)
            {
                if (CanAdjustOverviewEntry(index, direction))
                {
                    AdjustOverviewEntry(index, direction);
                }
                else
                {
                    AnnounceCurrentItem();
                }
                return;
            }
            CmrDetailRow row = DetailRow(region, index);
            if (row == null || row.Adjust == null)
            {
                return;
            }
            if (row.CanAdjust != null && !row.CanAdjust(direction))
            {
                // Already at that bound: re-announce in place so the boundary word is heard.
                AnnounceCurrentItem();
                return;
            }
            row.Adjust(direction);
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            RefreshModel();
            AnnounceDetailChange(DetailRow(region, index));
        }

        /// <summary>
        /// The row is re-resolved from its region and index at apply time rather than captured: the row
        /// list is rebuilt from live job state on every refresh, so the object handed here is stale by
        /// the time Enter confirms.
        /// </summary>
        private void BeginCountEdit(int region, int index, CmrDetailRow row)
        {
            editor.BeginCountEdit(row.Label, () =>
            {
                CmrDetailRow live = DetailRow(region, index);
                return live == null ? null : live.Numeric;
            }, RefreshModel, delegate
            {
                RefreshModel();
                AnnounceDetailChange(DetailRow(region, index));
            });
        }

        private void BeginTextEdit(int region, int index, CmrDetailRow row)
        {
            editor.BeginTextEdit(row.Label, () =>
            {
                CmrDetailRow live = DetailRow(region, index);
                return live == null ? null : live.Text;
            }, RefreshModel, delegate
            {
                RefreshModel();
                AnnounceDetailChange(DetailRow(region, index));
            });
        }

        private void OpenRowPicker(int region, int index, CmrDetailRow row)
        {
            bool opened = CmrRowEditor.OpenPicker(row.Choices(),
                row.Value == null ? null : row.Value(), row.Label, delegate
                {
                    RefreshModel();
                    AnnounceDetailChange(DetailRow(region, index));
                });
            if (!opened)
            {
                AnnounceCurrentItem();
            }
        }

        // Alt+I: the unified drill-in, for rows that name a carded def.

        private bool CurrentRowHasInfoCard()
        {
            CmrDetailRow row = CurrentDetailRow();
            return row != null && row.InfoCardDef != null;
        }

        private void OpenRowInfoCard()
        {
            CmrDetailRow row = CurrentDetailRow();
            InfoCardState.TryOpenInfoCardForDef(row == null ? null : row.InfoCardDef);
        }

        private CmrDetailRow CurrentDetailRow()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return null;
            }
            return DetailRow(Model.RegionIndex, region.Index);
        }

        // Section headings and typeahead.

        /// <summary>
        /// Folds the mod's own heading for a group of controls into the landing announcement of the
        /// first row reached inside that group, so flattening each column into a row list loses none.
        /// </summary>
        protected override string AnnouncePrefix(int region, int index)
        {
            string boundary = base.AnnouncePrefix(region, index);
            CmrDetailRow row = DetailRow(region, index);
            string section = sectionPrefix.Cross(row == null ? null : row.SectionTitle);
            if (section == null)
            {
                return boundary;
            }
            return string.IsNullOrEmpty(boundary) ? section : boundary + ". " + section;
        }

        /// <summary>
        /// Typing matches a row's name only: the default composes the whole description, which for a
        /// detail row would build the mod's tooltip for every row on every keystroke.
        /// </summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            CmrDetailRow detail = DetailRow(region, row);
            return detail != null ? detail.Label : base.ContentRowSearchText(region, row);
        }

        // Reorder (Ctrl+Up/Down): the row's own priority arrows.

        /// <summary>
        /// The same boundary answer the arrow buttons gate on: the mod passes <c>top</c> as the up
        /// arrow's <c>atBoundary</c> and <c>bottom</c> as the down arrow's, and
        /// <c>Utilities.DrawReorderButton</c> draws nothing when set, so a move is available exactly
        /// when the corresponding bound is not reached.
        /// </summary>
        protected override bool CanReorderContentItem(int region, int index, int direction)
        {
            CmrDetailRow detail = DetailRow(region, index);
            if (detail != null)
            {
                return detail.Reorder != null
                    && (detail.CanReorder == null || detail.CanReorder(direction));
            }
            if (region != JobsRegion || index < 0 || index >= jobs.Count || currentTab == null)
            {
                return false;
            }
            // Livestock's, Logs' and Overview's job rows draw no order arrows at all, so the reorder chord refuses
            // on those tabs.
            if (CmrCompat.Livestock.HandlesTab(currentTab) || CmrCompat.Logs.HandlesTab(currentTab)
                || CmrCompat.Overview.HandlesTab(currentTab))
            {
                return false;
            }
            bool atTop, atBottom;
            if (!CmrCompat.TryGetJobOrderBounds(currentTab, jobs[index], jobTracker, out atTop, out atBottom))
            {
                return false;
            }
            return direction < 0 ? !atTop : !atBottom;
        }

        protected override int ReorderContentItem(int region, int index, int direction)
        {
            CmrDetailRow detail = DetailRow(region, index);
            if (detail != null)
            {
                if (detail.Reorder == null
                    || (detail.CanReorder != null && !detail.CanReorder(direction)))
                {
                    return -1;
                }
                detail.Reorder(direction);
                RefreshContent();
                return index + direction;
            }
            if (region != JobsRegion || index < 0 || index >= jobs.Count || currentTab == null
                || CmrCompat.Livestock.HandlesTab(currentTab)
                || CmrCompat.Logs.HandlesTab(currentTab)
                || CmrCompat.Overview.HandlesTab(currentTab))
            {
                return -1;
            }
            object job = jobs[index];
            if (direction < 0)
            {
                CmrCompat.IncreasePriority(currentTab, jobTracker, job);
            }
            else
            {
                CmrCompat.DecreasePriority(currentTab, jobTracker, job);
            }
            RefreshContent();
            return jobs.IndexOf(job);
        }

        // Lifecycle and text shaping.

        public override void OnPush()
        {
            base.OnPush();
            sectionPrefix.Reset();
        }

        private static string JoinSentences(List<string> parts)
        {
            return CompatText.JoinSentences(parts);
        }

        /// <summary>The mod's tooltips are multi-line; announcements separate with periods, never newlines.</summary>
        private static string FlattenTooltip(string text)
        {
            return CompatText.Flatten(text);
        }
    }
}
