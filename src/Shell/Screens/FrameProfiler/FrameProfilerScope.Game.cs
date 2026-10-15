using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// RimWorld 1.7's frame profiler (<c>Verse.ProfilerUI.Window_FrameProfiler</c>), a dev tool for
    /// finding what makes a frame slow: a rolling history of recorded frames, a per-thread timeline
    /// of the selected frame, and a call tree (or allocation / VRAM table) aggregating it.
    ///
    /// Regions mirror the window top to bottom: the toolbar controls, the summary and status lines,
    /// the recorded frames (the histogram), the timeline (the flamechart), the table's controls,
    /// then the table. The thread filter popup and the settings drawer join as trailing regions
    /// while open. Every control row is captured from the window's own draw
    /// (<see cref="FrameProfilerCapture"/>) and pressed by forcing that primitive's click, so the
    /// window's handler runs; frame and row navigation call the same session and list-view methods
    /// the window's own keys call. The window's key handler is suppressed while this scope is
    /// attached, so the dispatcher is the only consumer and every key reaches the recorder.
    ///
    /// The viewer's cursor is the window's own: the selected table row, the selected frame band,
    /// the highlighted timeline zone, or a ring on the captured control. Moving through frames pins
    /// and pauses exactly as the window's Page Up/Down do. The flamechart's zoom, pan and lane
    /// collapse only rearrange the drawing, so the timeline region presents the zone tree instead.
    ///
    /// Data the window rebuilds in its WindowUpdate (rows after an expand, the summary after a frame
    /// step) changes a frame later, so those announcements wait for the next repaint.
    ///
    /// Keys keep the window's own meanings: Space pauses capture (except on a checkbox row), Page
    /// Up/Down step frames, Ctrl+C copies the selected row and Ctrl+Shift+C the view, F3 jumps between
    /// search matches. The window's Home (jump to the newest frame) would break the list grammar, so
    /// it moves to Ctrl+Page Down; Shift+Up/Down extend the frame selection the way a drag does, and
    /// Shift+Enter on a timeline zone selects its span the way a drag across it does.
    /// </summary>
    internal sealed partial class FrameProfilerScope : ScreenScope
    {
        private enum RegionKind { Controls, Summary, Frames, Timeline, TableControls, Table, Threads, Settings }

        private enum Pending { None, Current, Summary, Region, CheckState }

        private sealed class Row
        {
            public ProfilerControl Control;
            public bool IsControl;
            public string Caption;
            public int SortColumn = -1;
            public bool IsPath;
        }

        internal static FrameProfilerScope Active;

        private static readonly Regex Whitespace = new Regex(@"\s+");

        private readonly Window window;
        private readonly List<RegionKind> regions = new List<RegionKind>();
        private readonly Dictionary<RegionKind, List<Row>> rowLists = new Dictionary<RegionKind, List<Row>>();
        private readonly TextFieldEditSession editSession = new TextFieldEditSession();

        private Pending pending;
        private string editText = "";
        private RegionKind? jumpTo;
        private int pendingFrame;
        private int pushedTableIndex = -1;
        private bool threadsWereOpen;
        private bool settingsWereOpen;

        public FrameProfilerScope(Window window)
        {
            this.window = window;
            foreach (RegionKind kind in Enum.GetValues(typeof(RegionKind)))
            {
                rowLists[kind] = new List<Row>();
            }

            Claim("frameProfiler.pause", e => TogglePause(), when: () => !editSession.Editing && !OnToggleRow());
            Claim("frameProfiler.olderFrame", e => StepFrame(1), when: NotEditing);
            Claim("frameProfiler.newerFrame", e => StepFrame(-1), when: NotEditing);
            Claim("frameProfiler.newestFrame", e => JumpToNewest(), when: NotEditing);
            Claim("frameProfiler.extendOlder", e => ExtendFrames(-1), when: () => InRegion(RegionKind.Frames));
            Claim("frameProfiler.extendNewer", e => ExtendFrames(1), when: () => InRegion(RegionKind.Frames));
            Claim("frameProfiler.clearSelection", e => ClearFrameSelection(), when: () => InRegion(RegionKind.Frames));
            Claim("frameProfiler.copyRow", e => CopyRow(), when: () => InRegion(RegionKind.Table));
            Claim("frameProfiler.copyView", e => CopyView(), when: NotEditing);
            Claim("frameProfiler.nextMatch", e => NextMatch(1), when: NotEditing);
            Claim("frameProfiler.previousMatch", e => NextMatch(-1), when: NotEditing);
            Claim("frameProfiler.contextMenu", e => OpenContextMenu(), when: () => InRegion(RegionKind.Table));
            Claim("frameProfiler.selectTimeRange", e => SelectZoneRange(), when: () => InRegion(RegionKind.Timeline) && !TypeaheadHasActiveSearch);
            Claim(SharedMenuGrammar.Cancel, e => CloseOverlay(), when: () => OverlayOpen && !TypeaheadHasActiveSearch);

            RegisterPopTeardown(editSession.CancelIfActive);
        }

        public override string Name => "frame-profiler";

        protected override bool EnableTypeahead => true;

        protected override bool CaptureWindowButtons => false;

        protected override bool IncludeActionsRegion => false;

        /// <summary>An open thread popup or settings drawer takes Escape first; otherwise vanilla closes the window.</summary>
        public override bool OwnsCancel => OverlayOpen || base.OwnsCancel;

        private object Win => window;

        private object Session => FrameProfilerAccess.Session(window);

        private bool OverlayOpen => FrameProfilerAccess.SettingsOpen(window) || FrameProfilerAccess.ThreadPopupOpen(window);

        private bool NotEditing() => !editSession.Editing;

        private bool InRegion(RegionKind kind) => !editSession.Editing && CurrentKind() == kind;

        private RegionKind CurrentKind()
        {
            int region = Model.RegionIndex;
            return region >= 0 && region < regions.Count ? regions[region] : RegionKind.Controls;
        }

        public override void OnPush()
        {
            base.OnPush();
            Active = this;
            FrameProfilerCapture.ScopedWindow = window;
            FrameProfilerCapture.Reset();
            UI.UnfocusCurrentControl();
        }

        public override void OnPop()
        {
            if (ReferenceEquals(Active, this))
            {
                Active = null;
            }
            if (ReferenceEquals(FrameProfilerCapture.ScopedWindow, window))
            {
                FrameProfilerCapture.ScopedWindow = null;
            }
            base.OnPop();
        }

        protected override string ComposeOpenAnnouncement()
        {
            return "RimWorldAccess.FrameProfiler.Title".Translate().ToString();
        }

        // ---- Model ----

        protected override int ContentRegionCount => regions.Count;

        protected override string ContentRegionName(int region)
        {
            switch (regions[region])
            {
                case RegionKind.Controls: return "RimWorldAccess.FrameProfiler.Region.Controls".Translate().ToString();
                case RegionKind.Summary: return "RimWorldAccess.FrameProfiler.Region.Summary".Translate().ToString();
                case RegionKind.Frames: return "RimWorldAccess.FrameProfiler.Region.Frames".Translate().ToString();
                case RegionKind.Timeline: return "RimWorldAccess.FrameProfiler.Region.Timeline".Translate().ToString();
                case RegionKind.TableControls: return "RimWorldAccess.FrameProfiler.Region.TableControls".Translate().ToString();
                case RegionKind.Table: return "RimWorldAccess.FrameProfiler.Region.Table".Translate().ToString();
                case RegionKind.Threads: return "RimWorldAccess.FrameProfiler.Region.Threads".Translate().ToString();
                default: return "RimWorldAccess.FrameProfiler.Region.Settings".Translate().ToString();
            }
        }

        protected override void RefreshContent()
        {
            bool threadsOpen = FrameProfilerAccess.ThreadPopupOpen(window);
            bool settingsOpen = FrameProfilerAccess.SettingsOpen(window);
            regions.Clear();
            // Until the window's first repaint nothing is captured; an empty model holds the entry announcement.
            if (FrameProfilerCapture.Published.Count == 0)
            {
                return;
            }
            regions.Add(RegionKind.Controls);
            regions.Add(RegionKind.Summary);
            regions.Add(RegionKind.Frames);
            regions.Add(RegionKind.Timeline);
            regions.Add(RegionKind.TableControls);
            regions.Add(RegionKind.Table);
            if (threadsOpen)
            {
                regions.Add(RegionKind.Threads);
            }
            if (settingsOpen)
            {
                regions.Add(RegionKind.Settings);
            }

            RegionKind cursorKind = CurrentKind();
            Row cursorRow = ControlRowAtCursor(cursorKind);
            int cursorOrdinal = cursorRow == null ? -1 : SameKindOrdinal(rowLists[cursorKind], cursorRow);
            BuildControlRows();
            RestoreControlCursor(cursorKind, cursorRow, cursorOrdinal);
            RefreshTimeline();
            SyncTableCursorFromWindow();

            // A panel that just opened takes the cursor once the model holds its region.
            if (threadsOpen && !threadsWereOpen)
            {
                jumpTo = RegionKind.Threads;
            }
            if (settingsOpen && !settingsWereOpen)
            {
                jumpTo = RegionKind.Settings;
            }
            threadsWereOpen = threadsOpen;
            settingsWereOpen = settingsOpen;
        }

        protected override int ContentItemCount(int region)
        {
            switch (regions[region])
            {
                case RegionKind.Frames:
                    return FrameProfilerAccess.FrameCount(Session);
                case RegionKind.Timeline:
                    return timeline.Count;
                case RegionKind.Table:
                    return TableRowCount() + rowLists[RegionKind.Table].Count;
                case RegionKind.Threads:
                    return 1 + FrameProfilerAccess.ThreadItems(window).Count;
                default:
                    return rowLists[regions[region]].Count;
            }
        }

        protected override bool ContentRegionSearchable(int region)
        {
            RegionKind kind = regions[region];
            return kind != RegionKind.Frames && kind != RegionKind.Summary;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            switch (regions[region])
            {
                case RegionKind.Frames:
                    return DescribeFrame(index);
                case RegionKind.Timeline:
                    return DescribeZone(index);
                case RegionKind.Table:
                    return index < TableRowCount() ? DescribeTableRow(index) : DescribeRow(rowLists[RegionKind.Table][index - TableRowCount()]);
                case RegionKind.Threads:
                    return DescribeThread(index);
                default:
                    List<Row> rows = rowLists[regions[region]];
                    return index >= 0 && index < rows.Count ? DescribeRow(rows[index]) : new ElementDescription();
            }
        }

        protected override void ActivateContentItem(int region, int index)
        {
            switch (regions[region])
            {
                case RegionKind.Frames:
                    PinFrame(index, announce: true);
                    return;
                case RegionKind.Timeline:
                    ActivateZone(index);
                    return;
                case RegionKind.Table:
                    if (index < TableRowCount())
                    {
                        object view = ActiveView();
                        FrameProfilerAccess.Select(view, index);
                        FrameProfilerAccess.ActivateSelected(view, false);
                        Defer(Pending.Current);
                        return;
                    }
                    ActivateRow(rowLists[RegionKind.Table][index - TableRowCount()]);
                    return;
                case RegionKind.Threads:
                    ToggleThread(index);
                    return;
                default:
                    List<Row> rows = rowLists[regions[region]];
                    if (index >= 0 && index < rows.Count)
                    {
                        ActivateRow(rows[index]);
                    }
                    return;
            }
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            RegionKind kind = regions[region];
            return kind == RegionKind.Timeline || (kind == RegionKind.Table && index < TableRowCount());
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (regions[region] == RegionKind.Timeline)
            {
                AdjustZone(index, direction);
                return;
            }
            object view = ActiveView();
            FrameProfilerAccess.Select(view, index);
            if (direction > 0)
            {
                FrameProfilerAccess.ActivateSelected(view, true);
            }
            else
            {
                FrameProfilerAccess.CollapseSelected(FrameProfilerAccess.ActiveSection(window));
            }
            Defer(Pending.Current);
        }

        protected override void OnCursorSettled(int region, int index)
        {
            if (region < 0 || region >= regions.Count)
            {
                return;
            }
            switch (regions[region])
            {
                case RegionKind.Table:
                    if (index < TableRowCount() && index != FrameProfilerAccess.SelectedIndex(ActiveView()))
                    {
                        FrameProfilerAccess.Select(ActiveView(), index);
                        pushedTableIndex = index;
                    }
                    break;
                case RegionKind.Frames:
                    PinFrame(index, announce: false);
                    break;
                case RegionKind.Timeline:
                    HighlightZone(index);
                    break;
            }
            SetThreadPopupForRegion(regions[region]);
        }

        protected override void OnRegionChanged(MoveResult result)
        {
            SyncFramesCursor(force: true);
            if (CurrentKind() == RegionKind.Table)
            {
                int selected = FrameProfilerAccess.SelectedIndex(ActiveView());
                if (selected >= 0 && Model.CurrentRegion != null && selected < Model.CurrentRegion.Count)
                {
                    Model.CurrentRegion.MoveTo(selected);
                }
                pushedTableIndex = selected;
            }
            base.OnRegionChanged(result);
            SetThreadPopupForRegion(CurrentKind());
        }

        protected internal override Rect FocusedContentRect()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.Index < 0)
            {
                return default(Rect);
            }
            RegionKind kind = CurrentKind();
            if (kind == RegionKind.Threads)
            {
                return FrameProfilerAccess.ThreadRowScreenRect(window, region.Index);
            }
            List<Row> rows = rowLists[kind];
            int index = kind == RegionKind.Table ? region.Index - TableRowCount() : region.Index;
            if (kind == RegionKind.Frames || kind == RegionKind.Timeline || index < 0 || index >= rows.Count)
            {
                return default(Rect);
            }
            Row row = rows[index];
            return row.IsControl ? row.Control.ScreenRect : default(Rect);
        }

        // ---- Deferred announcements ----

        private void Defer(Pending what)
        {
            pending = what;
            pendingFrame = Time.frameCount;
        }

        /// <summary>Runs at the end of each of the window's repaint passes.</summary>
        internal void OnRepaint()
        {
            if ((pending == Pending.None && !jumpTo.HasValue) || Time.frameCount <= pendingFrame || editSession.Editing)
            {
                return;
            }
            // A control that opened a dialog (Assemblies..., a context menu) leaves that dialog the only voice.
            if (!ReferenceEquals(FocusStack.Top, this))
            {
                pending = Pending.None;
                return;
            }
            Pending what = pending;
            pending = Pending.None;
            RefreshModel();
            if (jumpTo.HasValue && regions.Contains(jumpTo.Value))
            {
                Model.MoveToRegion(regions.IndexOf(jumpTo.Value));
                what = Pending.Region;
            }
            jumpTo = null;
            SyncFramesCursor(force: false);
            switch (what)
            {
                case Pending.Summary:
                    SpeakSummary();
                    break;
                case Pending.Region:
                    AnnounceRegion();
                    break;
                case Pending.CheckState:
                    ElementDescription d = DescribeContentItem(Model.RegionIndex, Model.CurrentRegion != null ? Model.CurrentRegion.Index : -1);
                    TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(new ElementDescription { Check = d.Check }, TranslatedShellVocabulary.Instance));
                    break;
                default:
                    AnnounceCurrentItem();
                    break;
            }
        }

        // ---- Control rows ----

        private void BuildControlRows()
        {
            foreach (KeyValuePair<RegionKind, List<Row>> entry in rowLists)
            {
                entry.Value.Clear();
            }
            IReadOnlyList<ProfilerControl> controls = FrameProfilerCapture.Published;
            List<ProfilerControl> top = new List<ProfilerControl>();
            List<ProfilerControl> strip = new List<ProfilerControl>();
            List<ProfilerControl> pane = new List<ProfilerControl>();
            List<ProfilerControl> summary = new List<ProfilerControl>();
            List<ProfilerControl> settings = new List<ProfilerControl>();
            for (int i = 0; i < controls.Count; i++)
            {
                ProfilerControl c = controls[i];
                switch (c.Zone)
                {
                    case ProfilerZone.Title:
                    case ProfilerZone.Toolbar:
                    case ProfilerZone.HistogramHeader:
                        top.Add(c);
                        break;
                    case ProfilerZone.TabStrip:
                        strip.Add(c);
                        break;
                    case ProfilerZone.Pane:
                        pane.Add(c);
                        break;
                    case ProfilerZone.Summary:
                    case ProfilerZone.Status:
                        summary.Add(c);
                        break;
                    case ProfilerZone.Settings:
                    case ProfilerZone.Targets:
                        settings.Add(c);
                        break;
                }
            }
            // Right-aligned runs draw right to left; present every strip in reading order.
            top.Sort(CompareByZoneThenPosition);
            strip.Sort(CompareByZoneThenPosition);
            AppendRows(rowLists[RegionKind.Controls], top);
            AppendRows(rowLists[RegionKind.Summary], summary);
            AppendRows(rowLists[RegionKind.TableControls], strip);
            AppendSortRows(rowLists[RegionKind.TableControls]);
            AppendPathRow(rowLists[RegionKind.TableControls]);
            AppendRows(rowLists[RegionKind.Table], pane);
            AppendRows(rowLists[RegionKind.Settings], settings);
        }

        private Row ControlRowAtCursor(RegionKind kind)
        {
            ListModel region = Model.CurrentRegion;
            if (kind == RegionKind.Frames || kind == RegionKind.Timeline || kind == RegionKind.Table || kind == RegionKind.Threads
                || region == null || region.Index < 0 || region.Index >= rowLists[kind].Count)
            {
                return null;
            }
            return rowLists[kind][region.Index];
        }

        private static bool SameKind(Row a, Row b)
        {
            return a.IsControl == b.IsControl && a.IsPath == b.IsPath && (a.SortColumn >= 0) == (b.SortColumn >= 0)
                && (!a.IsControl || (a.Control.Kind == b.Control.Kind && a.Control.Zone == b.Control.Zone));
        }

        private static int SameKindOrdinal(List<Row> rows, Row row)
        {
            int ordinal = 0;
            for (int i = 0; i < rows.Count && !ReferenceEquals(rows[i], row); i++)
            {
                if (SameKind(rows[i], row))
                {
                    ordinal++;
                }
            }
            return ordinal;
        }

        /// <summary>
        /// Control rows come and go as the window redraws (a search status appearing beside the
        /// search box, a button relabelling itself), so the cursor follows the control it was on:
        /// the same control by label, else the same-ordinal control of that kind.
        /// </summary>
        private void RestoreControlCursor(RegionKind kind, Row previous, int ordinal)
        {
            ListModel region = Model.CurrentRegion;
            if (previous == null || region == null || CurrentKind() != kind)
            {
                return;
            }
            List<Row> rows = rowLists[kind];
            int byOrdinal = -1;
            int seen = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                if (!SameKind(row, previous))
                {
                    continue;
                }
                bool sameIdentity = row.SortColumn == previous.SortColumn && row.IsPath == previous.IsPath
                    && (!row.IsControl || (row.Control.Label == previous.Control.Label
                        && row.Control.SegmentGroup == previous.Control.SegmentGroup && row.Control.SegmentIndex == previous.Control.SegmentIndex));
                if (sameIdentity)
                {
                    region.SetCount(rows.Count);
                    region.MoveTo(i);
                    return;
                }
                if (seen++ == ordinal)
                {
                    byOrdinal = i;
                }
            }
            if (byOrdinal >= 0)
            {
                region.SetCount(rows.Count);
                region.MoveTo(byOrdinal);
            }
        }

        private static int CompareByZoneThenPosition(ProfilerControl a, ProfilerControl b)
        {
            int zone = a.Zone.CompareTo(b.Zone);
            if (zone != 0)
            {
                return zone;
            }
            float ay = Mathf.Round(a.ScreenRect.center.y / 4f);
            float by = Mathf.Round(b.ScreenRect.center.y / 4f);
            return ay != by ? ay.CompareTo(by) : a.ScreenRect.x.CompareTo(b.ScreenRect.x);
        }

        /// <summary>
        /// Consecutive text runs on one line are one visual line (the summary's head, total and
        /// tail; the toolbar's median/p99/budget), so they join into a single read-only row. A
        /// placeholder drawn inside a text field becomes that field's caption.
        /// </summary>
        private static void AppendRows(List<Row> into, List<ProfilerControl> controls)
        {
            for (int i = 0; i < controls.Count; i++)
            {
                ProfilerControl c = controls[i];
                if (c.Kind == ProfilerControlKind.Label)
                {
                    Row field = FindFieldContaining(into, c.ScreenRect);
                    if (field != null)
                    {
                        field.Caption = c.Label;
                        continue;
                    }
                    Row last = into.Count > 0 ? into[into.Count - 1] : null;
                    if (last != null && last.IsControl && last.Control.Kind == ProfilerControlKind.Label
                        && last.Control.Zone == c.Zone && Mathf.Abs(last.Control.ScreenRect.center.y - c.ScreenRect.center.y) < 4f)
                    {
                        last.Control.Label = last.Control.Label + " " + c.Label;
                        last.Control.ScreenRect = Rect.MinMaxRect(last.Control.ScreenRect.xMin, Mathf.Min(last.Control.ScreenRect.yMin, c.ScreenRect.yMin),
                            Mathf.Max(last.Control.ScreenRect.xMax, c.ScreenRect.xMax), Mathf.Max(last.Control.ScreenRect.yMax, c.ScreenRect.yMax));
                        continue;
                    }
                }
                into.Add(new Row { Control = c, IsControl = true });
            }
            for (int i = 0; i < into.Count; i++)
            {
                if (into[i].IsControl && into[i].Control.Kind == ProfilerControlKind.Label)
                {
                    into[i].Control.Label = Whitespace.Replace(into[i].Control.Label, " ").Trim();
                }
            }
        }

        private static Row FindFieldContaining(List<Row> rows, Rect rect)
        {
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                ProfilerControl c = rows[i].Control;
                if (rows[i].IsControl && (c.Kind == ProfilerControlKind.SearchField || c.Kind == ProfilerControlKind.TextArea)
                    && c.ScreenRect.Contains(rect.center))
                {
                    return rows[i];
                }
            }
            return null;
        }

        /// <summary>The call tree's sortable column headers (vanilla's header click calls each column's sort delegate).</summary>
        private void AppendSortRows(List<Row> into)
        {
            if (!FrameProfilerAccess.ActiveIsCallTree(window) || FrameProfilerAccess.FlamegraphMode(window))
            {
                return;
            }
            object view = ActiveView();
            int columns = FrameProfilerAccess.ColumnCount(view);
            for (int i = 0; i < columns; i++)
            {
                if (FrameProfilerAccess.ColumnSort(view, i) != null)
                {
                    into.Add(new Row { SortColumn = i });
                }
            }
            if (FrameProfilerAccess.LabelSort(view) != null)
            {
                into.Add(new Row { SortColumn = columns });
            }
        }

        private void AppendPathRow(List<Row> into)
        {
            into.Add(new Row { IsPath = true });
        }

        private ElementDescription DescribeRow(Row row)
        {
            ElementDescription d = new ElementDescription();
            if (row.SortColumn >= 0)
            {
                return DescribeSortRow(row.SortColumn);
            }
            if (row.IsPath)
            {
                List<string> crumbs = FrameProfilerAccess.Crumbs(ActiveView());
                d.Label = "RimWorldAccess.FrameProfiler.Path".Translate(string.Join(", ", crumbs.ToArray())).ToString();
                d.ReadOnly = true;
                return d;
            }
            ProfilerControl c = row.Control;
            d.Label = SpokenLabel(row.Caption ?? c.Label);
            switch (c.Kind)
            {
                case ProfilerControlKind.Chip:
                case ProfilerControlKind.Ghost:
                    d.Role = ElementRole.Button;
                    d.Selected = c.Active ? true : (bool?)null;
                    d.Disabled = !c.Enabled;
                    break;
                case ProfilerControlKind.Segment:
                    d.Role = ElementRole.RadioButton;
                    d.Selected = c.Active;
                    break;
                case ProfilerControlKind.Checkbox:
                    d.Role = ElementRole.Checkbox;
                    d.Check = c.Active ? CheckState.Checked : CheckState.Unchecked;
                    d.Disabled = !c.Enabled;
                    break;
                case ProfilerControlKind.Field:
                    d.Role = ElementRole.TextField;
                    d.Value = string.IsNullOrEmpty(c.Unit) ? c.Value : "RimWorldAccess.FrameProfiler.FieldValue".Translate(c.Value, c.Unit).ToString();
                    break;
                case ProfilerControlKind.SearchField:
                case ProfilerControlKind.TextArea:
                    d.Role = ElementRole.TextField;
                    if (d.Label.Length == 0)
                    {
                        d.Label = (c.Kind == ProfilerControlKind.TextArea ? "RimWorldAccess.FrameProfiler.Filters" : "RimWorldAccess.FrameProfiler.Search").Translate().ToString();
                    }
                    d.Value = c.Value;
                    d.ValueBlank = string.IsNullOrEmpty(c.Value);
                    break;
                case ProfilerControlKind.ThreadButton:
                    d.Role = ElementRole.ComboBox;
                    d.Value = c.Value;
                    break;
                case ProfilerControlKind.Heading:
                    d.ReadOnly = true;
                    break;
                default:
                    d.ReadOnly = true;
                    break;
            }
            return d;
        }

        /// <summary>
        /// Several of the window's buttons are a bare glyph with no words. Each maps to the action
        /// its click runs; any other label is spoken as drawn, minus leading arrow glyphs.
        /// </summary>
        private static string SpokenLabel(string label)
        {
            switch (label)
            {
                case "◀": return "RimWorldAccess.FrameProfiler.Control.OlderFrame".Translate().ToString();
                case "▶": return "RimWorldAccess.FrameProfiler.Control.NewerFrame".Translate().ToString();
                case "▶▶": return "RimWorldAccess.FrameProfiler.Control.NewestFrame".Translate().ToString();
                case "×": return "RimWorldAccess.FrameProfiler.Control.Close".Translate().ToString();
                case "+": return "RimWorldAccess.FrameProfiler.Control.ExpandAll".Translate().ToString();
                case "−": return "RimWorldAccess.FrameProfiler.Control.CollapseAll".Translate().ToString();
                default: return (label ?? "").TrimStart('↑', '↓', ' ');
            }
        }

        private ElementDescription DescribeSortRow(int column)
        {
            object view = ActiveView();
            int columns = FrameProfilerAccess.ColumnCount(view);
            bool isLabel = column >= columns;
            string header = isLabel ? FrameProfilerAccess.LabelHeader(view) : FrameProfilerAccess.ColumnHeader(view, column);
            string sortName = isLabel ? "Label" : FrameProfilerAccess.ColumnKind(view, column);
            bool active = FrameProfilerAccess.ActiveSortColumn(Session) == sortName;
            if (active)
            {
                // The active header carries a trailing " ↑"/" ↓"; the direction is spoken as a state instead.
                header = header.TrimEnd('↑', '↓', ' ');
            }
            ElementDescription d = new ElementDescription
            {
                Label = "RimWorldAccess.FrameProfiler.SortBy".Translate(header).ToString(),
                Role = ElementRole.Button,
            };
            if (active)
            {
                d.Value = (FrameProfilerAccess.SortAscending(Session) ? "RimWorldAccess.FrameProfiler.SortedAscending" : "RimWorldAccess.FrameProfiler.SortedDescending").Translate().ToString();
            }
            return d;
        }

        private void ActivateRow(Row row)
        {
            if (row.SortColumn >= 0)
            {
                object view = ActiveView();
                Delegate sort = row.SortColumn >= FrameProfilerAccess.ColumnCount(view)
                    ? FrameProfilerAccess.LabelSort(view)
                    : FrameProfilerAccess.ColumnSort(view, row.SortColumn);
                sort?.DynamicInvoke();
                SoundDefOf.Click.PlayOneShotOnCamera();
                Defer(Pending.Current);
                return;
            }
            if (!row.IsControl)
            {
                AnnounceCurrentItem();
                return;
            }
            ProfilerControl c = row.Control;
            switch (c.Kind)
            {
                case ProfilerControlKind.Chip:
                case ProfilerControlKind.Ghost:
                case ProfilerControlKind.Segment:
                case ProfilerControlKind.Checkbox:
                    if (!c.Enabled || (c.Kind == ProfilerControlKind.Segment && c.Active))
                    {
                        AnnounceCurrentItem();
                        return;
                    }
                    FrameProfilerCapture.ArmPress(c);
                    Defer(c.Kind == ProfilerControlKind.Checkbox ? Pending.CheckState : Pending.Current);
                    return;
                case ProfilerControlKind.ThreadButton:
                    FrameProfilerAccess.SetThreadPopupOpen(window, true);
                    SoundDefOf.FloatMenu_Open.PlayOneShotOnCamera();
                    Defer(Pending.Current);
                    return;
                case ProfilerControlKind.Field:
                case ProfilerControlKind.SearchField:
                case ProfilerControlKind.TextArea:
                    BeginEdit(c, row.Caption);
                    return;
                default:
                    AnnounceCurrentItem();
                    return;
            }
        }

        private void BeginEdit(ProfilerControl control, string caption)
        {
            bool multiLine = control.Kind == ProfilerControlKind.TextArea;
            TextFieldSpec spec = new TextFieldSpec(labelKey: null, minLength: 0, multiLine: multiLine);
            ElementDescription d = DescribeRow(new Row { Control = control, IsControl = true, Caption = caption });
            editText = control.Value ?? "";
            editSession.EnterEdit(
                control.Value,
                spec,
                d.Label,
                text => editText = text,
                OnEditExit,
                announcePrompt: true,
                onConfirm: () => FrameProfilerCapture.ArmText(control, editText));
        }

        private void OnEditExit()
        {
            Defer(Pending.Current);
        }

        // ---- Frames (the histogram) ----

        /// <summary>Region index 0 is the oldest recorded frame, matching the histogram's left-to-right order.</summary>
        private int OffsetForIndex(int index) => FrameProfilerAccess.FrameCount(Session) - 1 - index;

        private ElementDescription DescribeFrame(int index)
        {
            object session = Session;
            int offset = OffsetForIndex(index);
            object snapshot = FrameProfilerAccess.SnapshotAt(session, offset);
            ElementDescription d = new ElementDescription();
            if (snapshot == null)
            {
                return d;
            }
            float totalMs = FrameProfilerAccess.TotalMs(snapshot);
            d.Label = "RimWorldAccess.FrameProfiler.Frame".Translate(FrameProfilerAccess.FrameIndex(snapshot)).ToString();
            d.Value = FrameProfilerAccess.FormatMs(totalMs);
            d.Selected = FrameSelected(session, offset) ? true : (bool?)null;

            List<string> stats = new List<string>();
            if (totalMs > FrameProfilerAccess.BudgetMs(session))
            {
                stats.Add("RimWorldAccess.FrameProfiler.OverBudget".Translate().ToString());
            }
            if (FrameProfilerAccess.IsSpike(snapshot))
            {
                stats.Add("RimWorldAccess.FrameProfiler.Spike".Translate().ToString());
            }
            int gen0 = FrameProfilerAccess.Gen0(snapshot), gen1 = FrameProfilerAccess.Gen1(snapshot), gen2 = FrameProfilerAccess.Gen2(snapshot);
            if (gen0 + gen1 + gen2 > 0)
            {
                stats.Add("RimWorldAccess.FrameProfiler.GarbageCollections".Translate(gen0, gen1, gen2).ToString());
                long promoted = FrameProfilerAccess.PromotedBytes(snapshot);
                if (promoted > 0)
                {
                    stats.Add("RimWorldAccess.FrameProfiler.Promoted".Translate(FrameProfilerAccess.FormatBytes(promoted)).ToString());
                }
            }
            int pauses = FrameProfilerAccess.PauseCount(snapshot);
            if (pauses > 0)
            {
                stats.Add((pauses == 1 ? "RimWorldAccess.FrameProfiler.OnePause" : "RimWorldAccess.FrameProfiler.Pauses")
                    .Translate(pauses, FrameProfilerAccess.FormatMs(FrameProfilerAccess.PauseMs(snapshot))).ToString());
            }
            long alloc = FrameProfilerAccess.AllocBytes(snapshot);
            if (alloc > 0)
            {
                int objects = FrameProfilerAccess.AllocObjects(snapshot);
                stats.Add(objects > 0
                    ? "RimWorldAccess.FrameProfiler.AllocObjects".Translate(FrameProfilerAccess.FormatBytes(alloc), objects.ToString("n0")).ToString()
                    : "RimWorldAccess.FrameProfiler.Alloc".Translate(FrameProfilerAccess.FormatBytes(alloc)).ToString());
            }
            float pausedBefore = FrameProfilerAccess.PauseBefore(window, offset);
            if (pausedBefore > 0f)
            {
                stats.Add("RimWorldAccess.FrameProfiler.PausedBefore".Translate(FrameProfilerAccess.FormatMs(pausedBefore)).ToString());
            }
            d.Extras = string.Join(". ", stats.ToArray());
            return d;
        }

        /// <summary>While following the newest frame the band spans the whole buffer, which is no selection at all.</summary>
        private static bool FrameSelected(object session, int offset)
        {
            int newest, oldest;
            return !FrameProfilerAccess.FollowingNewest(session)
                && FrameProfilerAccess.TryGetSelectionBand(session, out newest, out oldest) && offset >= newest && offset <= oldest;
        }

        /// <summary>The frame the window displays: the pinned one, or the newest while live.</summary>
        private int DisplayedFrameIndex()
        {
            object session = Session;
            int count = FrameProfilerAccess.FrameCount(session);
            int newest, oldest;
            if (FrameProfilerAccess.FollowingNewest(session) || !FrameProfilerAccess.TryGetSelectionBand(session, out newest, out oldest))
            {
                return count - 1;
            }
            return count - 1 - newest;
        }

        /// <summary>Page Up/Down and the toolbar's frame buttons move the pin; the cursor follows unless it already sits inside the selection band.</summary>
        private void SyncFramesCursor(bool force)
        {
            ListModel region = Model.CurrentRegion;
            if (CurrentKind() != RegionKind.Frames || region == null || region.Count == 0)
            {
                return;
            }
            if (!force && FrameSelected(Session, OffsetForIndex(region.Index)))
            {
                return;
            }
            region.MoveTo(Mathf.Clamp(DisplayedFrameIndex(), 0, region.Count - 1));
        }

        /// <summary>Selects one frame the way a histogram click or Page Up/Down does: it pins the frame and pauses capture.</summary>
        private void PinFrame(int index, bool announce)
        {
            object session = Session;
            int offset = OffsetForIndex(index);
            int newest, oldest;
            bool banded = FrameProfilerAccess.TryGetSelectionBand(session, out newest, out oldest);
            bool live = FrameProfilerAccess.FollowingNewest(session);
            if (offset < 0 || (!live && banded && newest == offset && oldest == offset) || (live && offset == 0 && !announce))
            {
                if (announce)
                {
                    AnnounceCurrentItem();
                }
                return;
            }
            FrameProfilerAccess.BeginSelection(session, offset);
            if (announce)
            {
                Defer(Pending.Current);
            }
        }

        private void ExtendFrames(int direction)
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.Count == 0)
            {
                return;
            }
            int target = Mathf.Clamp(region.Index + direction, 0, region.Count - 1);
            region.MoveTo(target);
            FrameProfilerAccess.ExtendSelection(Session, OffsetForIndex(target));
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            Defer(Pending.Current);
        }

        private void ClearFrameSelection()
        {
            FrameProfilerAccess.ClearSelection(Session);
            TolkHelper.Speak("RimWorldAccess.FrameProfiler.SelectionCleared".Loc());
        }

        private void StepFrame(int direction)
        {
            FrameProfilerAccess.Step(Session, direction);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            Defer(InRegion(RegionKind.Frames) ? Pending.Current : Pending.Summary);
        }

        private void JumpToNewest()
        {
            FrameProfilerAccess.JumpToNewest(Session);
            TolkHelper.Speak("RimWorldAccess.FrameProfiler.FollowingNewest".Loc());
        }

        private void TogglePause()
        {
            object session = Session;
            bool paused = !FrameProfilerAccess.IsPaused(session);
            FrameProfilerAccess.SetPaused(session, paused);
            TolkHelper.Speak((paused ? "RimWorldAccess.FrameProfiler.Paused" : "RimWorldAccess.FrameProfiler.Recording").Loc());
        }

        private void SpeakSummary()
        {
            List<Row> rows = rowLists[RegionKind.Summary];
            if (rows.Count > 0 && rows[0].IsControl)
            {
                TolkHelper.SpeakData(rows[0].Control.Label);
            }
        }

        // ---- Table ----

        private object ActiveView() => FrameProfilerAccess.View(FrameProfilerAccess.ActiveSection(window));

        private int TableRowCount()
        {
            object section = FrameProfilerAccess.ActiveSection(window);
            return FrameProfilerAccess.HasData(Session) || FrameProfilerAccess.ActiveIsVram(window)
                ? FrameProfilerAccess.Rows(FrameProfilerAccess.View(section)).Count
                : 0;
        }

        /// <summary>The window moves its own selection (an expand, a reveal from the timeline, a search jump); the cursor follows it.</summary>
        private void SyncTableCursorFromWindow()
        {
            int selected = FrameProfilerAccess.SelectedIndex(ActiveView());
            int tableRegion = regions.IndexOf(RegionKind.Table);
            if (selected < 0 || selected == pushedTableIndex || Model.RegionIndex != tableRegion)
            {
                pushedTableIndex = selected;
                return;
            }
            pushedTableIndex = selected;
            ListModel region = Model.CurrentRegion;
            if (region != null && selected < region.Count)
            {
                region.MoveTo(selected);
            }
        }

        private ElementDescription DescribeTableRow(int index)
        {
            object view = ActiveView();
            IList rows = FrameProfilerAccess.Rows(view);
            ElementDescription d = new ElementDescription();
            if (index < 0 || index >= rows.Count)
            {
                return d;
            }
            object row = rows[index];
            d.Label = FrameProfilerAccess.RowLabel(row);
            d.Role = ElementRole.TreeItem;
            d.Level = FrameProfilerAccess.RowDepth(row) + 1;
            if (FrameProfilerAccess.RowHasChildren(row))
            {
                d.Expanded = FrameProfilerAccess.RowExpanded(row);
            }
            List<string> stats = new List<string>();
            int columns = FrameProfilerAccess.ColumnCount(view);
            for (int i = 0; i < columns; i++)
            {
                string kind = FrameProfilerAccess.ColumnKind(view, i);
                string cell = FrameProfilerAccess.RowCell(row, kind);
                if (string.IsNullOrEmpty(cell))
                {
                    continue;
                }
                string header = FrameProfilerAccess.ColumnHeader(view, i).TrimEnd('↑', '↓', ' ');
                stats.Add(kind == "Percent"
                    ? "RimWorldAccess.FrameProfiler.PercentCell".Translate(cell).ToString()
                    : "RimWorldAccess.FrameProfiler.Cell".Translate(header, cell).ToString());
            }
            string detail = FrameProfilerAccess.RowDetail(row);
            if (!string.IsNullOrEmpty(detail))
            {
                stats.Add(detail);
            }
            if (FrameProfilerAccess.RowDimmed(row))
            {
                stats.Add("RimWorldAccess.FrameProfiler.NoSearchMatch".Translate().ToString());
            }
            d.Extras = string.Join(". ", stats.ToArray());
            return d;
        }

        private void CopyRow()
        {
            if (FrameProfilerAccess.CopySelectedRow(ActiveView()))
            {
                TolkHelper.Speak("RimWorldAccess.FrameProfiler.Copied".Loc());
            }
        }

        private void CopyView()
        {
            FrameProfilerAccess.CopyView(window);
            TolkHelper.Speak("RimWorldAccess.FrameProfiler.ViewCopied".Loc());
        }

        private void NextMatch(int direction)
        {
            if (!FrameProfilerAccess.SearchActive(Session))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            FrameProfilerAccess.SelectNextMatch(ActiveView(), direction);
            int table = regions.IndexOf(RegionKind.Table);
            if (Model.RegionIndex != table)
            {
                Model.MoveToRegion(table);
            }
            pushedTableIndex = -2;
            RefreshModel();
            AnnounceCurrentItem();
        }

        private void OpenContextMenu()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.Index < 0 || region.Index >= TableRowCount())
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            FrameProfilerAccess.Select(ActiveView(), region.Index);
            // The pointer is not on the row, so a plain FloatMenu would vanish; the guard routes it windowless.
            object section = FrameProfilerAccess.ActiveSection(window);
            ScopeDelegateGuard.Run(() => FrameProfilerAccess.OpenContextMenu(section));
        }

        // ---- Threads popup ----

        private bool OnToggleRow()
        {
            RegionKind kind = CurrentKind();
            if (kind == RegionKind.Threads)
            {
                return true;
            }
            ListModel region = Model.CurrentRegion;
            List<Row> rows = rowLists[kind];
            int index = region == null ? -1 : (kind == RegionKind.Table ? region.Index - TableRowCount() : region.Index);
            return index >= 0 && index < rows.Count && rows[index].IsControl && rows[index].Control.Kind == ProfilerControlKind.Checkbox;
        }

        private ElementDescription DescribeThread(int index)
        {
            IList items = FrameProfilerAccess.ThreadItems(window);
            ElementDescription d = new ElementDescription { Role = ElementRole.Checkbox };
            if (index == 0)
            {
                int selected = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    if (FrameProfilerAccess.ThreadSelected(FrameProfilerAccess.ThreadItemId(items[i])))
                    {
                        selected++;
                    }
                }
                d.Label = "RimWorldAccess.FrameProfiler.AllThreads".Translate().ToString();
                d.Check = FrameProfilerAccess.AllThreadsSelected() ? CheckState.Checked : (selected > 0 ? CheckState.PartiallyChecked : CheckState.Unchecked);
                d.Extras = "RimWorldAccess.FrameProfiler.ThreadsSelected".Translate(selected, items.Count).ToString();
                return d;
            }
            if (index - 1 >= items.Count)
            {
                return d;
            }
            object item = items[index - 1];
            d.Label = FrameProfilerAccess.ThreadItemLabel(item);
            d.Check = FrameProfilerAccess.ThreadSelected(FrameProfilerAccess.ThreadItemId(item)) ? CheckState.Checked : CheckState.Unchecked;
            d.Extras = Whitespace.Replace(FrameProfilerAccess.ThreadItemBusy(item) + " " + FrameProfilerAccess.ThreadItemUtil(item), " ").Trim();
            return d;
        }

        private void ToggleThread(int index)
        {
            if (index == 0)
            {
                FrameProfilerAccess.ToggleSelectAllThreads(window);
            }
            else
            {
                IList items = FrameProfilerAccess.ThreadItems(window);
                if (index - 1 >= items.Count)
                {
                    return;
                }
                int threadId = FrameProfilerAccess.ThreadItemId(items[index - 1]);
                bool value = !FrameProfilerAccess.ThreadSelected(threadId);
                // MUTATION-C: mirrors ProfilerThreadFilter.HandleInput's row click; that toggle is inline in a mouse-only handler.
                FrameProfilerAccess.SetThreadSelected(threadId, value);
                (value ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
                FrameProfilerAccess.Invalidate(Session);
            }
            RefreshModel();
            ElementDescription d = DescribeThread(index);
            d.Label = null;
            d.Extras = null;
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        /// <summary>The popup is visible exactly while its region holds the cursor, so the viewer sees the row being read.</summary>
        private void SetThreadPopupForRegion(RegionKind kind)
        {
            if (kind != RegionKind.Threads && FrameProfilerAccess.ThreadPopupOpen(window))
            {
                FrameProfilerAccess.SetThreadPopupOpen(window, false);
            }
        }

        private void CloseOverlay()
        {
            if (FrameProfilerAccess.ThreadPopupOpen(window))
            {
                FrameProfilerAccess.SetThreadPopupOpen(window, false);
                SoundDefOf.FloatMenu_Cancel.PlayOneShotOnCamera();
                Model.MoveToRegion(regions.IndexOf(RegionKind.TableControls));
                Defer(Pending.Region);
                return;
            }
            if (FrameProfilerAccess.SettingsOpen(window))
            {
                FrameProfilerAccess.ToggleSettings(window);
                SoundDefOf.Click.PlayOneShotOnCamera();
                Model.MoveToRegion(regions.IndexOf(RegionKind.Controls));
                Defer(Pending.Region);
            }
        }
    }
}
