using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Visible Wealth's breakdown window (<c>Dialog_WealthBreakdown</c>), read from the mod's own
    /// <c>WealthNode</c> tree. Regions mirror the window top to bottom: the total and the search
    /// box, the chart, then the toolbar. The chart is one of three regions, only the one the window
    /// draws being reachable: the nested list as a tree whose expansion is the nodes' own
    /// <c>Open</c> flags (so the window unfolds with the cursor), the flat list as a table, and the
    /// pie chart as its slices in drawing order, clockwise from the top. The toolbar rows are the
    /// window's own <c>ChartOption</c> objects, driven through their own delegates.
    /// Enter on a row with an info button opens the mod's info card, as Alt+I does anywhere.
    /// </summary>
    internal sealed partial class VisibleWealthScope : TreeRegionScope
    {
        private const int OverviewRegion = 0;
        private const int NestedRegion = 1;
        private const int FlatRegion = 2;
        private const int SliceRegion = 3;
        private const int OptionsRegion = 4;

        private const int TotalRow = 0;
        private const int SearchRow = 1;

        private enum ChartMode { NestedList, FlatList, Pie }

        private readonly Window window;
        private readonly TextFieldEditSession searchSession = new TextFieldEditSession();
        private readonly ForeignQuickSearchRow searchRow;
        private readonly List<object> options = new List<object>();
        private readonly Dictionary<object, Rect> rowLocalRects = new Dictionary<object, Rect>();
        private readonly Dictionary<object, Rect> rowScreenRects = new Dictionary<object, Rect>();
        private readonly List<Rect> optionRects = new List<Rect>();

        private ChartMode mode;
        private Rect totalRect;
        private Rect chartRect;
        private Rect viewport;
        private object scrolledNode;
        private bool landed;

        public VisibleWealthScope(Window window)
        {
            this.window = window;
            searchRow = new ForeignQuickSearchRow(searchSession, VwCompat.Search,
                () => "RimWorldAccess.Compat.VisibleWealth.Search".Translate(), ResyncChart);
            Claim(SharedMenuGrammar.Info, e => PerformInfoCard());
            RegisterPopTeardown(searchSession.CancelIfActive);
        }

        public override string Name => "visibleWealth";

        protected internal override Window OwnedWindow => window;

        protected override bool EnableTypeahead => true;

        protected override int TreeRegionIndex => NestedRegion;

        protected override string TreeRegionLabel => "RimWorldAccess.Compat.VisibleWealth.NestedRegion".Translate();

        protected override string ComposeOpenAnnouncement()
        {
            return CompatText.ModText("VisibleWealth_WealthBreakdown") + ". " + TotalLabel();
        }

        /// <summary>The cursor opens on the chart, below the total the open announcement already speaks.</summary>
        public override void OnPush()
        {
            ResyncChart();
            if (!landed)
            {
                landed = true;
                Model.MoveToRegion(mode == ChartMode.Pie ? SliceRegion : mode == ChartMode.FlatList ? FlatRegion : NestedRegion);
                if (mode == ChartMode.FlatList && flatRows.Count > 0)
                {
                    Model.CurrentRegion?.MoveTo(1);
                }
            }
            base.OnPush();
        }

        public override void OnFocus()
        {
            ResyncChart();
            base.OnFocus();
        }

        // ------------------------------------------------------------------
        // Regions.
        // ------------------------------------------------------------------

        protected override int ContentRegionCount => 5;

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case OverviewRegion: return "RimWorldAccess.Compat.VisibleWealth.OverviewRegion".Translate();
                case FlatRegion: return "RimWorldAccess.Compat.VisibleWealth.FlatRegion".Translate();
                case SliceRegion: return "RimWorldAccess.Compat.VisibleWealth.SliceRegion".Translate();
                case OptionsRegion: return "RimWorldAccess.Compat.VisibleWealth.OptionsRegion".Translate();
                default: return TreeRegionLabel;
            }
        }

        /// <summary>Only the chart the window draws is reachable; the other two are not on screen.</summary>
        protected override bool ContentRegionAlwaysNavigable(int region)
        {
            switch (region)
            {
                case NestedRegion: return mode == ChartMode.NestedList;
                case FlatRegion: return mode == ChartMode.FlatList;
                case SliceRegion: return mode == ChartMode.Pie;
                default: return true;
            }
        }

        protected override int ContentItemCount(int region)
        {
            switch (region)
            {
                case OverviewRegion: return 2;
                case FlatRegion: return mode == ChartMode.FlatList ? flatRows.Count : 0;
                case SliceRegion: return mode == ChartMode.Pie ? slices.Count : 0;
                case OptionsRegion: return options.Count;
                default: return base.ContentItemCount(region);
            }
        }

        protected override void RefreshContent()
        {
            base.RefreshContent();
            SyncOpenToMod();
            options.Clear();
            options.AddRange(VwCompat.ToolbarOptions());
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            switch (region)
            {
                case OverviewRegion: return DescribeOverviewRow(index);
                case FlatRegion: return DescribeFlatRow(index);
                case SliceRegion: return DescribeSlice(index);
                case OptionsRegion: return DescribeOption(index);
                default: return base.DescribeContentItem(region, index);
            }
        }

        protected override void ActivateContentItem(int region, int index)
        {
            switch (region)
            {
                case OverviewRegion:
                    if (index == SearchRow)
                    {
                        BeginSearchEdit();
                    }
                    break;
                case FlatRegion:
                    ActivateLeaf(NodeAt(flatRows, index));
                    break;
                case SliceRegion:
                    ActivateSlice(index);
                    break;
                case OptionsRegion:
                    ActivateOption(index);
                    break;
                default:
                    base.ActivateContentItem(region, index);
                    break;
            }
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            if (region == SliceRegion)
            {
                return index >= 0 && index < slices.Count;
            }
            return region == NestedRegion && base.CanAdjustContentItem(region, index);
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (region == SliceRegion)
            {
                AdjustSlice(index, direction);
                return;
            }
            base.AdjustContentItem(region, index, direction);
        }

        protected override void OnCursorSettled(int region, int index)
        {
            base.OnCursorSettled(region, index);
            SyncOpenToMod();
        }

        // ------------------------------------------------------------------
        // Overview: the total and the search box.
        // ------------------------------------------------------------------

        private string TotalLabel()
        {
            return CompatText.ModArgs("VisibleWealth_TotalWealth", VwCompat.TotalWealth(window).ToString("F0"));
        }

        private ElementDescription DescribeOverviewRow(int index)
        {
            var d = new ElementDescription();
            if (index == TotalRow)
            {
                d.Label = TotalLabel();
                d.ReadOnly = true;
                return d;
            }
            searchRow.Describe(d);
            return d;
        }

        private void BeginSearchEdit()
        {
            searchRow.BeginEdit(() =>
            {
                RefreshModel();
                AnnounceCurrentItem();
            });
        }

        // ------------------------------------------------------------------
        // Toolbar.
        // ------------------------------------------------------------------

        private ElementDescription DescribeOption(int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= options.Count)
            {
                return d;
            }
            object option = options[index];
            string name = VwCompat.OptionName(option);
            switch (VwCompat.KindOf(option))
            {
                case VwCompat.OptionKind.Toggle:
                    d.Label = name;
                    d.Role = ElementRole.Checkbox;
                    d.Check = VwCompat.ToggleValue(option) ? CheckState.Checked : CheckState.Unchecked;
                    break;
                case VwCompat.OptionKind.Button:
                    d.Label = name;
                    d.Role = ElementRole.Button;
                    d.Disabled = !VwCompat.ButtonEnabled(option);
                    break;
                default:
                    // The chart type and percentage pickers carry no name of their own; their icon is the caption.
                    if (name.NullOrEmpty())
                    {
                        name = (VwCompat.IsChartTypeOption(option)
                            ? "RimWorldAccess.Compat.VisibleWealth.ChartTypeOption"
                            : "RimWorldAccess.Compat.VisibleWealth.PercentOption").Translate();
                    }
                    d.Label = name;
                    d.Role = ElementRole.ComboBox;
                    d.Value = VwCompat.ChoiceLabel(option, VwCompat.ChoiceValue(option));
                    break;
            }
            return d;
        }

        private void ActivateOption(int index)
        {
            if (index < 0 || index >= options.Count)
            {
                return;
            }
            object option = options[index];
            switch (VwCompat.KindOf(option))
            {
                case VwCompat.OptionKind.Toggle:
                    bool value = !VwCompat.ToggleValue(option);
                    VwCompat.SetToggle(option, value);
                    (value ? SoundDefOf.Tick_High : SoundDefOf.Tick_Low).PlayOneShotOnCamera();
                    ResyncChart();
                    AnnounceCurrentItem();
                    break;
                case VwCompat.OptionKind.Button:
                    if (!VwCompat.ButtonEnabled(option))
                    {
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        AnnounceCurrentItem();
                        return;
                    }
                    VwCompat.PressButton(option);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    ResyncChart();
                    AnnounceCurrentItem();
                    break;
                default:
                    OpenChoiceMenu(option);
                    break;
            }
        }

        /// <summary>
        /// The picker <c>ChartOption&lt;T&gt;.DoOption</c> opens on a click, built from the option's own
        /// choice, label, icon and setter delegates; DoOption itself only acts on a live IMGUI click.
        /// </summary>
        private void OpenChoiceMenu(object option)
        {
            var menu = new List<FloatMenuOption>();
            foreach (object choice in VwCompat.Choices(option))
            {
                object picked = choice;
                menu.Add(new FloatMenuOption(VwCompat.ChoiceLabel(option, picked), delegate
                {
                    VwCompat.Choose(option, picked);
                    ResyncChart();
                }, VwCompat.ChoiceIcon(option, picked), Color.white));
            }
            if (menu.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(menu));
            }
        }

        // ------------------------------------------------------------------
        // Info cards.
        // ------------------------------------------------------------------

        private object CursorNode()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return null;
            }
            switch (Model.RegionIndex)
            {
                case NestedRegion: return CurrentTreeItem()?.Data;
                case FlatRegion: return NodeAt(flatRows, region.Index - 1);
                case SliceRegion: return NodeAt(slices, region.Index);
                default: return null;
            }
        }

        private static object NodeAt(List<object> nodes, int index)
        {
            return index >= 0 && index < nodes.Count ? nodes[index] : null;
        }

        private void PerformInfoCard()
        {
            object node = CursorNode();
            if (node != null && VwCompat.HasInfoCard(node))
            {
                VwCompat.ShowInfoCard(node);
            }
            else
            {
                InfoCardState.SpeakNoInfoCardAvailable();
            }
        }

        /// <summary>A leaf row's only control is its info button.</summary>
        private void ActivateLeaf(object node)
        {
            if (node != null && VwCompat.HasInfoCard(node))
            {
                VwCompat.ShowInfoCard(node);
            }
        }

        // ------------------------------------------------------------------
        // Draw records: the focus ring and scroll-to-cursor.
        // ------------------------------------------------------------------

        internal void BeginPass(Rect inRect)
        {
            SyncOpenToMod();
            ScrollToCursor();
            rowLocalRects.Clear();
            rowScreenRects.Clear();
            chartRect = default(Rect);
            RecordLayout(inRect);
            searchSession.MirrorLive();
            QuickSearchCapture.BeginPass(Model.RegionIndex == OverviewRegion && Model.CurrentRegion?.Index == SearchRow);
        }

        internal void EndPass()
        {
            QuickSearchCapture.EndPass();
        }

        /// <summary>
        /// The total line and the toolbar slots, at the offsets Dialog_WealthBreakdown.DoWindowContents
        /// lays them out: the total under a 45-point title, the chart type icon at the strip's left
        /// edge, then one 24-point icon per option after a 48-point gutter.
        /// </summary>
        private void RecordLayout(Rect inRect)
        {
            totalRect = GuiSpace.VisibleScreenRect(new Rect(inRect.x, inRect.y + 45f, inRect.width - Window.QuickSearchSize.x, 30f));
            optionRects.Clear();
            float y = inRect.yMax - Window.CloseButSize.y - 10f - 24f;
            for (int i = 0; i < options.Count; i++)
            {
                float x = inRect.x + (i == 0 ? 0f : 48f + 24f * (i - 1));
                optionRects.Add(GuiSpace.VisibleScreenRect(new Rect(x, y, 24f, 24f)));
            }
        }

        internal void RecordRow(object node, Rect local)
        {
            rowLocalRects[node] = local;
            rowScreenRects[node] = GuiSpace.VisibleScreenRect(local);
            viewport = GuiSpace.CurrentClip().VisibleRect;
        }

        internal void RecordChart(Rect screen)
        {
            chartRect = screen;
        }

        protected internal override Rect FocusedContentRect()
        {
            ListModel region = Model.CurrentRegion;
            switch (Model.RegionIndex)
            {
                case OverviewRegion:
                    return region != null && region.Index == TotalRow ? totalRect : default(Rect);
                case SliceRegion:
                    return chartRect;
                case OptionsRegion:
                    return region != null && region.Index >= 0 && region.Index < optionRects.Count
                        ? optionRects[region.Index]
                        : default(Rect);
            }
            object node = CursorNode();
            return node != null && rowScreenRects.TryGetValue(node, out Rect rect) ? rect : default(Rect);
        }

        /// <summary>Brings the cursor's row, as the last pass drew it, into the list's view once each time the cursor changes row.</summary>
        private void ScrollToCursor()
        {
            object node = CursorNode();
            if (node == null || ReferenceEquals(node, scrolledNode) || viewport.height <= 0f
                || !rowLocalRects.TryGetValue(node, out Rect row))
            {
                return;
            }
            scrolledNode = node;
            ref Vector2 scroll = ref VwCompat.ScrollPosition();
            if (row.yMin < scroll.y)
            {
                scroll.y = row.yMin;
            }
            else if (row.yMax > scroll.y + viewport.height)
            {
                scroll.y = row.yMax - viewport.height;
            }
        }
    }
}
