using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Global Log: every social and combat entry in the world, filterable, over
    /// <see cref="GlobalLog"/>. A self-drawn window paired with <see cref="GlobalLogScope"/>, the
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
            float y = inRect.y + titleHeight + 8f;
            float buttonRowY = inRect.yMax - buttonHeight;
            float bodyHeight = buttonRowY - 10f - y;

            DrawFilters(new Rect(inRect.x, y, FilterColumnWidth, bodyHeight), scope);
            DrawList(new Rect(inRect.x + FilterColumnWidth + 10f, y, inRect.width - FilterColumnWidth - 10f, bodyHeight), scope);
            DrawButtons(new Rect(inRect.x, buttonRowY, inRect.width, buttonHeight), scope);
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
    /// Keyboard scope for <see cref="GlobalLogWindow"/>. Regions: Log (the filtered entries, each
    /// read as the event, then how long ago), Filters (the log tab's own Show social, Show combat
    /// and Show all, plus whose entries and the order), and Buttons (a jump to each pawn in the
    /// focused entry, then Close).
    ///
    /// Typeahead walks matches in log order rather than by match quality, so Down after a search
    /// always moves forward through the log; Enter settles on the match, since a log row has no
    /// action of its own, leaving the cursor there to read the surrounding entries.
    ///
    /// Rows rebuild when a filter changes, and at most every <see cref="LiveRebuildSeconds"/> for
    /// new entries, so a large fight costs a few rebuilds a second rather than one per line. New
    /// entries also wait while a search is live, since its matches are row positions. The cursor
    /// follows its entry across every rebuild.
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

        private static readonly Filter[] Filters =
        {
            new Filter("ShowSocial", s => s.GlobalLogShowSocial, (s, v) => s.GlobalLogShowSocial = v),
            new Filter("ShowCombat", s => s.GlobalLogShowCombat, (s, v) => s.GlobalLogShowCombat = v),
            // The log tab's label alone does not say it adds the minor combat lines.
            new Filter("ShowAll", s => s.GlobalLogShowAll, (s, v) => s.GlobalLogShowAll = v, "RimWorldAccess.GlobalLog.Filter.ShowAllTip"),
            OwnerFilter("RimWorldAccess.GlobalLog.Filter.Colonists", GlobalLogOwners.Colonists),
            OwnerFilter("RimWorldAccess.GlobalLog.Filter.ColonyAnimals", GlobalLogOwners.ColonyAnimals),
            OwnerFilter("RimWorldAccess.GlobalLog.Filter.Others", GlobalLogOwners.Others),
            new Filter("RimWorldAccess.GlobalLog.Filter.NewestFirst", s => s.GlobalLogNewestFirst, (s, v) => s.GlobalLogNewestFirst = v),
        };

        internal static int FilterCount => Filters.Length;

        internal static GlobalLogScope Current { get; private set; }

        private readonly GlobalLogWindow window;
        private readonly List<GlobalLogRecord> rows = new List<GlobalLogRecord>();
        private readonly List<ScreenAction> actionsBuffer = new List<ScreenAction>();
        private int builtLogVersion = -1;
        private float lastRebuildTime = -1f;
        private bool announcedOpen;

        internal readonly Rect[] FilterRects = new Rect[Filters.Length];
        internal Rect FocusedRowRect;

        public GlobalLogScope(GlobalLogWindow window)
        {
            this.window = window;
        }

        private static Filter OwnerFilter(string labelKey, GlobalLogOwners flag)
        {
            return new Filter(labelKey,
                s => (s.GlobalLogOwnerFilter & flag) != 0,
                (s, v) => s.GlobalLogOwnerFilter = v ? s.GlobalLogOwnerFilter | flag : s.GlobalLogOwnerFilter & ~flag);
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

        public override void OnPush()
        {
            base.OnPush();
            Current = this;
            Rebuild();
            GlobalLog log = GlobalLog.Instance;
            int remembered = log != null ? rows.FindIndex(r => r.LogId == log.LastFocusedLogId) : -1;
            if (remembered >= 0)
            {
                Model.Region(LogRegion)?.MoveTo(remembered);
            }
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
            TolkHelper.SpeakData(rows.Count == 1
                ? (string)"RimWorldAccess.GlobalLog.OpenedOne".Translate()
                : (string)"RimWorldAccess.GlobalLog.Opened".Translate(rows.Count));
            AnnounceCurrentItem();
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
            IReadOnlyList<GlobalLogRecord> records = log.Records;
            for (int i = 0; i < records.Count; i++)
            {
                GlobalLogRecord record = records[i];
                bool kindShown = record.Kind == GlobalLogKind.Social
                    ? s.GlobalLogShowSocial
                    : s.GlobalLogShowCombat && (s.GlobalLogShowAll || record.ShowInCompactView);
                if (kindShown && !record.Broken && (record.Owners & s.GlobalLogOwnerFilter) != 0)
                {
                    rows.Add(record);
                }
            }
            if (s.GlobalLogNewestFirst)
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
                return Filters.Length;
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
                if (index < 0 || index >= Filters.Length)
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
            AnnounceCurrentItem();
        }

        protected override void OnCursorSettled(int region, int index)
        {
            base.OnCursorSettled(region, index);
            GlobalLog log = GlobalLog.Instance;
            if (log != null && region == LogRegion && index >= 0 && index < rows.Count)
            {
                log.LastFocusedLogId = rows[index].LogId;
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
                return region.Index < Filters.Length ? FilterRects[region.Index] : default(Rect);
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
            return Filters[index].LabelKey.Translate();
        }

        internal static string FilterTooltip(int index)
        {
            string key = Filters[index].TipKey;
            return key != null ? (string)key.Translate() : null;
        }

        internal static bool FilterValue(int index)
        {
            RimWorldAccessSettings s = RimWorldAccessMod_Settings.Settings;
            return s != null && Filters[index].Get(s);
        }

        private void ToggleFilter(int index)
        {
            RimWorldAccessSettings s = RimWorldAccessMod_Settings.Settings;
            if (s == null || index < 0 || index >= Filters.Length)
            {
                return;
            }
            bool next = !Filters[index].Get(s);
            Filters[index].Set(s, next);
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

        /// <summary>
        /// The log tab's own row click (<see cref="CameraJumper.TryJumpAndSelect"/>), gated by
        /// <see cref="CameraJumper.CanJump"/>. The window closes so the player lands on the pawn;
        /// reopening returns to this entry.
        /// </summary>
        private void JumpTo(Pawn pawn)
        {
            if (!CameraJumper.CanJump(pawn))
            {
                AnnounceCurrentItem();
                return;
            }
            window.Close();
            CameraJumper.TryJumpAndSelect(pawn);
            MapNavigationState.SpeakJumpedTo(pawn.LabelShort);
        }
    }
}
