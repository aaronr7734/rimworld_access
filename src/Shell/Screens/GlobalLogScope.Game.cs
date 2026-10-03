using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The colony's social and combat logs, one tab each, every entry in the world, filterable,
    /// over <see cref="GlobalLog"/>. A self-drawn window paired with <see cref="GlobalLogScope"/>, the
    /// same shape as <see cref="DialogueLogWindow"/>: it draws the scope's own rows and filter
    /// state, and every mouse action runs the scope's method, so the two cannot drift.
    ///
    /// Only rows inside the scroll viewport are measured and drawn; the rest keep a one-line
    /// estimate until they scroll into view. Measured heights are kept per record, so a rebuild
    /// from new entries neither re-measures nor moves the view.
    /// </summary>
    public sealed class GlobalLogWindow : Window
    {
        private const float FilterColumnWidth = 320f;
        private const float TimeColumnWidth = 110f;
        private const float RowPadding = 4f;

        private readonly List<TabRecord> tabs = new List<TabRecord>();
        private readonly Dictionary<GlobalLogRecord, float> rowHeights = new Dictionary<GlobalLogRecord, float>();
        private Vector2 scrollPosition;
        private float heightsWidth = -1f;
        private GlobalLogRecord scrolledTo;

        public GlobalLogWindow()
        {
            doCloseX = true;
            absorbInputAroundWindow = true;
            draggable = true;
            resizeable = true;
        }

        internal static void Open()
        {
            if (!Find.WindowStack.IsOpen<GlobalLogWindow>())
            {
                Find.WindowStack.Add(new GlobalLogWindow());
            }
        }

        internal static string OpenerHotkey()
        {
            return ActionRegistry.Catalog.TryGet("map.openGlobalLog", out InputAction action) && action.Bindings.Count > 0
                ? action.Bindings[0].DisplayLabel
                : null;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(1040f, 640f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            GlobalLogScope scope = GlobalLogScope.Current;
            scope?.SyncRows();

            Text.Font = GameFont.Medium;
            float titleHeight = Text.LineHeight;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, titleHeight),
                (string)"RimWorldAccess.GlobalLog.Title".Translate());
            Text.Font = GameFont.Small;

            const float buttonHeight = 35f;
            float y = inRect.y + titleHeight + 8f + TabDrawer.TabHeight;
            float buttonRowY = inRect.yMax - buttonHeight;
            float bodyHeight = buttonRowY - 10f - y;

            Rect body = new Rect(inRect.x, y, inRect.width, bodyHeight);
            DrawTabs(body, scope);
            body = body.ContractedBy(10f);
            DrawFilters(new Rect(body.x, body.y, FilterColumnWidth, body.height), scope);
            DrawList(new Rect(body.x + FilterColumnWidth + 10f, body.y, body.width - FilterColumnWidth - 10f, body.height), scope);
            DrawButtons(new Rect(inRect.x, buttonRowY, inRect.width, buttonHeight), scope);
        }

        private void DrawTabs(Rect baseRect, GlobalLogScope scope)
        {
            if (scope == null)
            {
                return;
            }
            tabs.Clear();
            GlobalLogKind view = GlobalLogScope.View;
            foreach (GlobalLogKind log in GlobalLogScope.Logs)
            {
                GlobalLogKind target = log;
                tabs.Add(new TabRecord(GlobalLogScope.LogLabel(log), delegate { scope.ClickLog(target); }, log == view));
            }
            Widgets.DrawMenuSection(baseRect);
            TabDrawer.DrawTabs(baseRect, tabs);
        }

        private static void DrawFilters(Rect rect, GlobalLogScope scope)
        {
            if (scope == null)
            {
                return;
            }
            float y = rect.y;
            for (int i = 0; i < GlobalLogScope.FilterCount; i++)
            {
                Rect row = new Rect(rect.x, y, rect.width, 28f);
                scope.FilterRects[i] = GuiSpace.VisibleScreenRect(row);
                bool value = GlobalLogScope.FilterValue(i);
                bool before = value;
                string tip = GlobalLogScope.FilterTooltip(i);
                if (tip != null)
                {
                    TooltipHandler.TipRegion(row, tip);
                }
                Widgets.CheckboxLabeled(row, GlobalLogScope.FilterLabel(i), ref value);
                if (value != before)
                {
                    scope.ClickFilter(i);
                }
                y += 30f;
            }
        }

        private void DrawList(Rect outer, GlobalLogScope scope)
        {
            IReadOnlyList<GlobalLogRecord> rows = scope != null ? scope.Rows : null;
            if (scope != null)
            {
                scope.FocusedRowRect = default(Rect);
            }
            if (rows == null || rows.Count == 0)
            {
                Widgets.Label(outer, (string)"RimWorldAccess.GlobalLog.Empty".Translate());
                return;
            }

            float innerWidth = outer.width - 16f;
            float textWidth = innerWidth - TimeColumnWidth;
            if (heightsWidth != textWidth)
            {
                rowHeights.Clear();
                heightsWidth = textWidth;
                scrolledTo = null;
            }

            float estimate = Text.LineHeight + RowPadding;
            float totalHeight = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                totalHeight += KnownHeight(rows[i], estimate);
            }

            int highlighted = scope.CurrentRowIndex;
            GlobalLogRecord focused = highlighted >= 0 ? rows[highlighted] : null;
            if (focused != null && focused != scrolledTo)
            {
                ScrollToRow(highlighted, outer.height, estimate, textWidth, rows);
            }

            bool repaint = Event.current.type == EventType.Repaint;
            Rect viewRect = new Rect(0f, 0f, innerWidth, totalHeight);
            Widgets.BeginScrollView(outer, ref scrollPosition, viewRect);
            float curY = 0f;
            float visibleTop = scrollPosition.y;
            float visibleBottom = scrollPosition.y + outer.height;
            int clickedIndex = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                GlobalLogRecord record = rows[i];
                float h = KnownHeight(record, estimate);
                if (curY + h < visibleTop)
                {
                    curY += h;
                    continue;
                }
                if (curY > visibleBottom)
                {
                    break;
                }
                h = MeasureRow(record, textWidth);
                Rect rowRect = new Rect(0f, curY, innerWidth, h);
                if (i == highlighted)
                {
                    Widgets.DrawHighlight(rowRect);
                    scope.FocusedRowRect = GuiSpace.VisibleScreenRect(rowRect);
                    // Settles only once the row is wholly in view with its neighbours measured;
                    // until then each pass re-scrolls with the better heights.
                    if (curY >= visibleTop && curY + h <= visibleBottom)
                    {
                        scrolledTo = record;
                    }
                }
                if (repaint)
                {
                    Widgets.Label(new Rect(0f, curY, textWidth, h), record.Text);
                    Text.Anchor = TextAnchor.UpperRight;
                    Widgets.Label(new Rect(textWidth, curY, TimeColumnWidth, h), GlobalLogScope.TimeAgo(record));
                    Text.Anchor = TextAnchor.UpperLeft;
                }
                if (Widgets.ButtonInvisible(rowRect))
                {
                    clickedIndex = i;
                }
                curY += h;
            }
            Widgets.EndScrollView();

            if (clickedIndex >= 0)
            {
                scope.SelectRow(clickedIndex);
            }
        }

        private float KnownHeight(GlobalLogRecord record, float estimate)
        {
            return rowHeights.TryGetValue(record, out float h) ? h : estimate;
        }

        private float MeasureRow(GlobalLogRecord record, float textWidth)
        {
            if (!rowHeights.TryGetValue(record, out float h))
            {
                h = Text.CalcHeight(record.Text, textWidth) + RowPadding;
                rowHeights[record] = h;
            }
            return h;
        }

        /// <summary>Keeps the cursor's row inside the viewport for a sighted viewer; the rows above it keep their estimates.</summary>
        private void ScrollToRow(int index, float viewHeight, float estimate, float textWidth, IReadOnlyList<GlobalLogRecord> rows)
        {
            float top = 0f;
            for (int i = 0; i < index; i++)
            {
                top += KnownHeight(rows[i], estimate);
            }
            float h = MeasureRow(rows[index], textWidth);
            if (top < scrollPosition.y)
            {
                scrollPosition.y = top;
            }
            else if (top + h > scrollPosition.y + viewHeight)
            {
                scrollPosition.y = top + h - viewHeight;
            }
        }

        private void DrawButtons(Rect rect, GlobalLogScope scope)
        {
            if (scope == null)
            {
                return;
            }
            IReadOnlyList<ScreenAction> actions = scope.CurrentActions();
            const float spacing = 10f;
            float width = Mathf.Min(200f, (rect.width - spacing * (actions.Count - 1)) / actions.Count);
            float x = rect.x;
            int focusedIndex = scope.CurrentActionIndex;
            for (int i = 0; i < actions.Count; i++)
            {
                ScreenAction action = actions[i];
                Rect r = new Rect(x, rect.y, width, rect.height);
                bool wasEnabled = GUI.enabled;
                GUI.enabled = !action.Disabled;
                if (Widgets.ButtonText(r, action.Label))
                {
                    action.Activate();
                }
                GUI.enabled = wasEnabled;
                if (i == focusedIndex)
                {
                    FocusRing.Draw(r);
                }
                x += width + spacing;
            }
        }
    }

    /// <summary>
    /// Keyboard scope for <see cref="GlobalLogWindow"/>: the Social and Combat logs, switched by
    /// Left/Right, each with its own filters and remembered entry. Regions: Log, Filters, and
    /// Buttons (a jump to each pawn in the focused entry, then Close). Enter on an entry jumps to
    /// its pawn, or offers a menu when it names several.
    ///
    /// Typeahead walks matches in log order. Rows rebuild on a filter or log change, and at most
    /// every <see cref="LiveRebuildSeconds"/> for new entries, never under a live search, since
    /// its matches are row positions. The cursor follows its entry across every rebuild.
    /// </summary>
    public sealed class GlobalLogScope : ScreenScope
    {
        private const int LogRegion = 0;
        private const int FiltersRegion = 1;
        private const float LiveRebuildSeconds = 0.25f;

        private readonly struct Filter
        {
            public readonly string LabelKey;
            public readonly string TipKey;
            public readonly Func<RimWorldAccessSettings, bool> Get;
            public readonly Action<RimWorldAccessSettings, bool> Set;

            public Filter(string labelKey, Func<RimWorldAccessSettings, bool> get, Action<RimWorldAccessSettings, bool> set, string tipKey = null)
            {
                LabelKey = labelKey;
                TipKey = tipKey;
                Get = get;
                Set = set;
            }
        }

        private static readonly Filter[] SocialFilters =
        {
            OwnerFilter("RimWorldAccess.GlobalLog.Filter.Colonists", GlobalLogKind.Social, GlobalLogOwners.Colonists),
            OwnerFilter("RimWorldAccess.GlobalLog.Filter.ColonyAnimals", GlobalLogKind.Social, GlobalLogOwners.ColonyAnimals),
            OwnerFilter("RimWorldAccess.GlobalLog.Filter.Others", GlobalLogKind.Social, GlobalLogOwners.Others),
            new Filter("RimWorldAccess.GlobalLog.Filter.NewestFirst", s => s.GlobalLogSocialNewestFirst, (s, v) => s.GlobalLogSocialNewestFirst = v),
        };

        private static readonly Filter[] CombatFilters =
        {
            OwnerFilter("RimWorldAccess.GlobalLog.Filter.Colonists", GlobalLogKind.Combat, GlobalLogOwners.Colonists),
            OwnerFilter("RimWorldAccess.GlobalLog.Filter.ColonyAnimals", GlobalLogKind.Combat, GlobalLogOwners.ColonyAnimals),
            OwnerFilter("RimWorldAccess.GlobalLog.Filter.Others", GlobalLogKind.Combat, GlobalLogOwners.Others),
            // The log tab's label alone does not say it adds the minor combat lines.
            new Filter("ShowAll", s => s.GlobalLogShowAll, (s, v) => s.GlobalLogShowAll = v, "RimWorldAccess.GlobalLog.Filter.ShowAllTip"),
            new Filter("RimWorldAccess.GlobalLog.Filter.NewestFirst", s => s.GlobalLogCombatNewestFirst, (s, v) => s.GlobalLogCombatNewestFirst = v),
        };

        internal static readonly GlobalLogKind[] Logs = { GlobalLogKind.Social, GlobalLogKind.Combat };

        internal static GlobalLogScope Current { get; private set; }

        private readonly GlobalLogWindow window;
        private readonly List<GlobalLogRecord> rows = new List<GlobalLogRecord>();
        private readonly List<ScreenAction> actionsBuffer = new List<ScreenAction>();
        private int builtLogVersion = -1;
        private float lastRebuildTime = -1f;
        private bool announcedOpen;

        internal readonly Rect[] FilterRects = new Rect[Math.Max(SocialFilters.Length, CombatFilters.Length)];
        internal Rect FocusedRowRect;

        public GlobalLogScope(GlobalLogWindow window)
        {
            this.window = window;
            // Reached because the base's Left/Right claims stand down on a flat row.
            Claim(SharedMenuGrammar.NextHorizontal, delegate { SwitchLog(1); }, when: InLogOrFilters);
            Claim(SharedMenuGrammar.PreviousHorizontal, delegate { SwitchLog(-1); }, when: InLogOrFilters);
        }

        private static Filter OwnerFilter(string labelKey, GlobalLogKind log, GlobalLogOwners flag)
        {
            return new Filter(labelKey,
                s => (OwnersFor(s, log) & flag) != 0,
                (s, v) => SetOwnersFor(s, log, v ? OwnersFor(s, log) | flag : OwnersFor(s, log) & ~flag));
        }

        private static GlobalLogOwners OwnersFor(RimWorldAccessSettings s, GlobalLogKind log)
        {
            return log == GlobalLogKind.Combat ? s.GlobalLogCombatOwners : s.GlobalLogSocialOwners;
        }

        private static void SetOwnersFor(RimWorldAccessSettings s, GlobalLogKind log, GlobalLogOwners owners)
        {
            if (log == GlobalLogKind.Combat)
            {
                s.GlobalLogCombatOwners = owners;
            }
            else
            {
                s.GlobalLogSocialOwners = owners;
            }
        }

        public override string Name
        {
            get { return "global-log"; }
        }

        protected internal override Window OwnedWindow
        {
            get { return window; }
        }

        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        protected override bool TypeaheadDocumentOrder
        {
            get { return true; }
        }

        internal IReadOnlyList<GlobalLogRecord> Rows
        {
            get { return rows; }
        }

        internal static GlobalLogKind View
        {
            get
            {
                RimWorldAccessSettings s = RimWorldAccessMod_Settings.Settings;
                return s != null ? s.GlobalLogView : GlobalLogKind.Social;
            }
        }

        private static Filter[] ViewFilters
        {
            get { return View == GlobalLogKind.Combat ? CombatFilters : SocialFilters; }
        }

        internal static int FilterCount => ViewFilters.Length;

        public override void OnPush()
        {
            base.OnPush();
            Current = this;
            Rebuild();
            LandOnRemembered();
        }

        public override void OnPop()
        {
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }
            base.OnPop();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                AnnounceCurrentItem();
                return;
            }
            announcedOpen = true;
            TolkHelper.SpeakData(LogSummary());
            AnnounceCurrentItem();
        }

        // Logs.

        internal static string LogLabel(GlobalLogKind log)
        {
            return log == GlobalLogKind.Combat
                ? (string)"RimWorldAccess.GlobalLog.Tab.Combat".Translate()
                : (string)"RimWorldAccess.GlobalLog.Tab.Social".Translate();
        }

        private string LogSummary()
        {
            return rows.Count == 1
                ? (string)"RimWorldAccess.GlobalLog.SummaryOne".Translate(LogLabel(View))
                : (string)"RimWorldAccess.GlobalLog.Summary".Translate(LogLabel(View), rows.Count);
        }

        private bool InLogOrFilters()
        {
            RefreshModel();
            return Model.RegionIndex == LogRegion || Model.RegionIndex == FiltersRegion;
        }

        /// <summary>Left/Right: the neighbouring log, wrapping.</summary>
        private void SwitchLog(int direction)
        {
            int at = Array.IndexOf(Logs, View);
            int next = ((at + direction) % Logs.Length + Logs.Length) % Logs.Length;
            ScreenPolicy.TabSwitchSound?.PlayOneShotOnCamera();
            ShowLog(Logs[next]);
        }

        // The tab strip has already played its sound.
        internal void ClickLog(GlobalLogKind log)
        {
            if (log != View)
            {
                ShowLog(log);
            }
        }

        private void ShowLog(GlobalLogKind log)
        {
            RimWorldAccessSettings s = RimWorldAccessMod_Settings.Settings;
            if (s == null)
            {
                return;
            }
            TypeaheadReset();
            s.GlobalLogView = log;
            LoadedModManager.GetMod<RimWorldAccessMod_Settings>()?.WriteSettings();
            RebuildRows();
            RefreshModel();
            LandOnRemembered();
            TolkHelper.SpeakData(LogSummary());
            AnnounceCurrentItem();
        }

        private void LandOnRemembered()
        {
            GlobalLog log = GlobalLog.Instance;
            int id = log == null ? -1 : View == GlobalLogKind.Combat ? log.LastFocusedCombatLogId : log.LastFocusedSocialLogId;
            int remembered = id >= 0 ? rows.FindIndex(r => r.LogId == id) : -1;
            Model.Region(LogRegion)?.MoveTo(remembered >= 0 ? remembered : 0);
        }

        // Rows.

        /// <summary>Picks up new log entries, throttled, and never under a live search.</summary>
        internal void SyncRows()
        {
            if (builtLogVersion == GlobalLog.Version || TypeaheadHasActiveSearch
                || Time.realtimeSinceStartup - lastRebuildTime < LiveRebuildSeconds)
            {
                return;
            }
            Rebuild();
        }

        /// <summary>Rebuilds the filtered rows now, keeping the cursor on its entry.</summary>
        private void Rebuild()
        {
            GlobalLogRecord focused = CurrentRow();
            RebuildRows();
            RefreshModel();
            int index = focused != null ? rows.IndexOf(focused) : -1;
            if (index >= 0)
            {
                Model.Region(LogRegion)?.MoveTo(index);
            }
        }

        private void RebuildRows()
        {
            builtLogVersion = GlobalLog.Version;
            lastRebuildTime = Time.realtimeSinceStartup;
            rows.Clear();
            RimWorldAccessSettings s = RimWorldAccessMod_Settings.Settings;
            GlobalLog log = GlobalLog.Instance;
            if (s == null || log == null)
            {
                return;
            }
            GlobalLogKind view = s.GlobalLogView;
            bool combat = view == GlobalLogKind.Combat;
            GlobalLogOwners owners = OwnersFor(s, view);
            IReadOnlyList<GlobalLogRecord> records = log.Records;
            for (int i = 0; i < records.Count; i++)
            {
                GlobalLogRecord record = records[i];
                if (record.Kind == view && (!combat || s.GlobalLogShowAll || record.ShowInCompactView)
                    && !record.Broken && (record.Owners & owners) != 0)
                {
                    rows.Add(record);
                }
            }
            if (combat ? s.GlobalLogCombatNewestFirst : s.GlobalLogSocialNewestFirst)
            {
                rows.Reverse();
            }
        }

        /// <summary>Usually a no-op: <see cref="GlobalLog"/> renders ahead in the background.</summary>
        protected override void OnTypeaheadWillSearch()
        {
            SyncRows();
            for (int i = 0; i < rows.Count; i++)
            {
                _ = rows[i].Text;
            }
        }

        private GlobalLogRecord CurrentRow()
        {
            int index = CurrentRowIndex;
            return index >= 0 && index < rows.Count ? rows[index] : null;
        }

        /// <summary>The Log region's retained cursor, so Buttons keep acting on the last focused entry.</summary>
        internal int CurrentRowIndex
        {
            get
            {
                if (Model.RegionCount <= LogRegion)
                {
                    return -1;
                }
                ListModel region = Model.Region(LogRegion);
                return region != null && !region.IsEmpty && region.Index < rows.Count ? region.Index : -1;
            }
        }

        /// <summary>The declared-action index under the cursor, or -1; Buttons always follow the two content regions.</summary>
        internal int CurrentActionIndex
        {
            get
            {
                if (Model.RegionIndex != ContentRegionCount)
                {
                    return -1;
                }
                ListModel region = Model.CurrentRegion;
                return region != null && !region.IsEmpty ? region.Index : -1;
            }
        }

        internal static string TimeAgo(GlobalLogRecord record)
        {
            int age = Mathf.Max(0, Find.TickManager.TicksAbs - record.TicksAbs);
            return "TimeAgo".Translate(age.ToStringTicksToPeriod());
        }

        // Content.

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        protected override string ContentRegionName(int region)
        {
            return region == LogRegion
                ? (string)"RimWorldAccess.GlobalLog.LogRegion".Translate()
                : (string)"RimWorldAccess.GlobalLog.FiltersRegion".Translate();
        }

        /// <summary>An empty log keeps one placeholder row so it says why it is empty.</summary>
        protected override int ContentItemCount(int region)
        {
            if (region == FiltersRegion)
            {
                return FilterCount;
            }
            return rows.Count == 0 ? 1 : rows.Count;
        }

        protected override void RefreshContent()
        {
            if (builtLogVersion < 0)
            {
                RebuildRows();
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (region == FiltersRegion)
            {
                if (index < 0 || index >= FilterCount)
                {
                    return new ElementDescription();
                }
                return new ElementDescription
                {
                    Label = FilterLabel(index),
                    Role = ElementRole.Checkbox,
                    Check = FilterValue(index) ? CheckState.Checked : CheckState.Unchecked,
                    Extras = FilterTooltip(index),
                };
            }
            var d = new ElementDescription { ReadOnly = true };
            if (rows.Count == 0)
            {
                d.Label = (string)"RimWorldAccess.GlobalLog.Empty".Translate();
                return d;
            }
            if (index < 0 || index >= rows.Count)
            {
                return d;
            }
            GlobalLogRecord record = rows[index];
            d.ReadOnly = record.Pawns.Count == 0;
            d.Label = "RimWorldAccess.GlobalLog.Row".Translate(record.Text.TrimEnd('.', ' '), TimeAgo(record));
            d.PositionIndex = index + 1;
            d.PositionCount = rows.Count;
            return d;
        }

        /// <summary>Matches the event text only, never the time.</summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            if (region == LogRegion && row >= 0 && row < rows.Count)
            {
                return rows[row].Text;
            }
            return base.ContentRowSearchText(region, row);
        }

        /// <summary>Every log row is read-only text; answering directly spares describing thousands of rows per keystroke.</summary>
        protected override TypeaheadCandidateKind ContentRowSearchKind(int region, int row)
        {
            return region == LogRegion ? TypeaheadCandidateKind.Text : base.ContentRowSearchKind(region, row);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == FiltersRegion)
            {
                ToggleFilter(index);
                return;
            }
            GlobalLogRecord record = index >= 0 && index < rows.Count ? rows[index] : null;
            if (record == null || record.Pawns.Count == 0)
            {
                AnnounceCurrentItem();
                return;
            }
            if (record.Pawns.Count == 1)
            {
                JumpTo(record.Pawns[0]);
                return;
            }
            OpenJumpMenu(record);
        }

        protected override void OnCursorSettled(int region, int index)
        {
            base.OnCursorSettled(region, index);
            GlobalLog log = GlobalLog.Instance;
            if (log == null || region != LogRegion || index < 0 || index >= rows.Count)
            {
                return;
            }
            if (View == GlobalLogKind.Combat)
            {
                log.LastFocusedCombatLogId = rows[index].LogId;
            }
            else
            {
                log.LastFocusedSocialLogId = rows[index].LogId;
            }
        }

        protected internal override Rect FocusedContentRect()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty || region.Index < 0)
            {
                return default(Rect);
            }
            if (Model.RegionIndex == FiltersRegion)
            {
                return region.Index < FilterCount ? FilterRects[region.Index] : default(Rect);
            }
            return Model.RegionIndex == LogRegion ? FocusedRowRect : default(Rect);
        }

        /// <summary>A mouse click on a row: moves the keyboard cursor there and reads it, as if arrowed to.</summary>
        internal void SelectRow(int index)
        {
            if (index >= 0 && index < rows.Count)
            {
                LandCursorAt(LogRegion, index);
            }
        }

        /// <summary>A mouse click on a filter: moves the cursor there and toggles it, exactly as Enter would.</summary>
        internal void ClickFilter(int index)
        {
            LandCursorAt(FiltersRegion, index, announce: false);
            ToggleFilter(index);
        }

        // Filters.

        internal static string FilterLabel(int index)
        {
            return ViewFilters[index].LabelKey.Translate();
        }

        internal static string FilterTooltip(int index)
        {
            string key = ViewFilters[index].TipKey;
            return key != null ? (string)key.Translate() : null;
        }

        internal static bool FilterValue(int index)
        {
            RimWorldAccessSettings s = RimWorldAccessMod_Settings.Settings;
            return s != null && ViewFilters[index].Get(s);
        }

        private void ToggleFilter(int index)
        {
            RimWorldAccessSettings s = RimWorldAccessMod_Settings.Settings;
            Filter[] filters = ViewFilters;
            if (s == null || index < 0 || index >= filters.Length)
            {
                return;
            }
            bool next = !filters[index].Get(s);
            filters[index].Set(s, next);
            LoadedModManager.GetMod<RimWorldAccessMod_Settings>()?.WriteSettings();
            Rebuild();
            string state = AnnouncementComposer.ComposeStateChange(
                new ElementDescription { Check = next ? CheckState.Checked : CheckState.Unchecked },
                TranslatedShellVocabulary.Instance);
            TolkHelper.SpeakData(rows.Count == 1
                ? (string)"RimWorldAccess.GlobalLog.FilterChangedOne".Translate(state)
                : (string)"RimWorldAccess.GlobalLog.FilterChanged".Translate(state, rows.Count));
        }

        // Buttons.

        internal IReadOnlyList<ScreenAction> CurrentActions()
        {
            return DeclaredActions;
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actionsBuffer.Clear();
                GlobalLogRecord record = CurrentRow();
                if (record != null)
                {
                    foreach (Pawn pawn in record.Pawns)
                    {
                        Pawn target = pawn;
                        bool canJump = CameraJumper.CanJump(target);
                        actionsBuffer.Add(new ScreenAction(
                            "RimWorldAccess.GlobalLog.JumpTo".Translate(target.LabelShort),
                            delegate { JumpTo(target); },
                            disabled: !canJump,
                            disabledReason: canJump ? null : (string)"RimWorldAccess.GlobalLog.JumpUnavailable".Translate()));
                    }
                }
                actionsBuffer.Add(new ScreenAction(
                    (string)"CloseButton".Translate(), delegate { window.Close(); }, SharedMenuGrammar.Cancel));
                return actionsBuffer;
            }
        }

        private void OpenJumpMenu(GlobalLogRecord record)
        {
            var options = new List<FloatMenuOption>();
            foreach (Pawn pawn in record.Pawns)
            {
                Pawn target = pawn;
                options.Add(CameraJumper.CanJump(target)
                    ? new FloatMenuOption("RimWorldAccess.GlobalLog.JumpTo".Translate(target.LabelShort), delegate { JumpTo(target); })
                    : new FloatMenuOption("RimWorldAccess.GlobalLog.JumpToUnavailable".Translate(target.LabelShort), null));
            }
            KeyboardFloatMenu.Open(options, givesColonistOrders: false);
        }

        /// <summary>
        /// The log tab's own row click (<see cref="CameraJumper.TryJumpAndSelect"/>), gated by
        /// <see cref="CameraJumper.CanJump"/>. The window closes so the player lands on the pawn;
        /// reopening returns to this entry.
        /// </summary>
        private void JumpTo(Pawn pawn)
        {
            if (!CameraJumper.CanJump(pawn))
            {
                TolkHelper.SpeakData("RimWorldAccess.GlobalLog.JumpUnavailable".Translate());
                return;
            }
            window.Close();
            CameraJumper.TryJumpAndSelect(pawn);
            MapNavigationState.SpeakJumpedTo(pawn.LabelShort);
        }
    }
}
