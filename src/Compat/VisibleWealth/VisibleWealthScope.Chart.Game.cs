using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    internal sealed partial class VisibleWealthScope
    {
        private readonly List<object> flatRows = new List<object>();
        private readonly List<object> slices = new List<object>();

        /// <summary>Each tree node's expansion as last agreed with the mod's own <c>Open</c> flag.</summary>
        private readonly Dictionary<object, bool> syncedOpen = new Dictionary<object, bool>();

        private ChartMode CurrentMode()
        {
            if (VwCompat.IsPieChart)
            {
                return ChartMode.Pie;
            }
            return VwCompat.IsNestedList ? ChartMode.NestedList : ChartMode.FlatList;
        }

        /// <summary>
        /// Rebuilds the chart regions from the mod's live state after anything that can change what
        /// the window draws: a toolbar option, the search text, an expand or collapse on a slice.
        /// </summary>
        private void ResyncChart()
        {
            mode = CurrentMode();
            RebuildTree();
            flatRows.Clear();
            if (mode == ChartMode.FlatList)
            {
                flatRows.AddRange(VwCompat.FlatRows(window));
            }
            RebuildSlices();
            RefreshModel();
            if (mode == ChartMode.NestedList && Tree.Count > 0)
            {
                Model.Region(NestedRegion)?.MoveTo(Tree.SelectedIndex);
            }
        }

        private void RebuildSlices()
        {
            slices.Clear();
            if (mode == ChartMode.Pie)
            {
                slices.AddRange(VwCompat.Slices(window));
            }
        }

        // ------------------------------------------------------------------
        // Nested list: the tree.
        // ------------------------------------------------------------------

        /// <summary>
        /// A fresh tree of every row the window lists, expanded where the nodes are open, keeping the
        /// cursor on its node or the nearest ancestor still listed. The mod's flags win over the old
        /// tree's expansion, so a collapse made in the window holds here too.
        /// </summary>
        private void RebuildTree()
        {
            if (mode != ChartMode.NestedList)
            {
                if (Tree.Root != null)
                {
                    ResetTree();
                }
                syncedOpen.Clear();
                return;
            }
            SyncOpenToMod();
            var lineage = new List<object>();
            for (object node = TreeRowNode(); node != null; node = VwCompat.Parent(node))
            {
                lineage.Add(node);
            }
            syncedOpen.Clear();
            bool searching = VwCompat.SearchActive();
            var root = new InspectionTreeItem { IsExpandable = true, IsExpanded = true, IndentLevel = -1 };
            foreach (object node in VwCompat.RootNodes(window))
            {
                AddTreeNode(root, node, searching);
            }
            SetTreeRoot(root);
            foreach (object node in lineage)
            {
                int index = IndexOfVisible(node);
                if (index >= 0)
                {
                    Tree.SetSelectedIndex(index);
                    break;
                }
            }
        }

        private void AddTreeNode(InspectionTreeItem parent, object node, bool searching)
        {
            if (!VwCompat.IsListed(node))
            {
                return;
            }
            var item = new InspectionTreeItem
            {
                Label = VwCompat.Text(node),
                Data = node,
                IndentLevel = parent.IndentLevel + 1,
            };
            InspectNodeFactory.Attach(parent, item);
            foreach (object child in VwCompat.DrawOrderChildren(node))
            {
                AddTreeNode(item, child, searching);
            }
            item.IsExpandable = item.Children.Count > 0;
            if (item.IsExpandable)
            {
                // A search draws every match's ancestors open whatever their flags say.
                item.IsExpanded = searching || VwCompat.IsOpen(node);
                syncedOpen[node] = item.IsExpanded;
            }
            if (VwCompat.HasInfoCard(node))
            {
                item.OnInfo = () => VwCompat.ShowInfoCard(node);
            }
        }

        private object TreeRowNode()
        {
            if (Tree.Root == null || Model.RegionCount <= NestedRegion)
            {
                return null;
            }
            int index = Model.Region(NestedRegion).Index;
            return index >= 0 && index < Tree.Count ? Tree.Visible[index].Data : null;
        }

        private int IndexOfVisible(object node)
        {
            IReadOnlyList<InspectionTreeItem> visible = Tree.Visible;
            for (int i = 0; i < visible.Count; i++)
            {
                if (ReferenceEquals(visible[i].Data, node))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Carries an expand or collapse made in the tree onto the node's own <c>Open</c> flag, the
        /// write the window's arrow button makes, so the window unfolds where the cursor goes. Stands
        /// down while the window's search is on: it draws everything open then and leaves the flags be.
        /// </summary>
        private void SyncOpenToMod()
        {
            if (mode != ChartMode.NestedList || Tree.Root == null || VwCompat.SearchActive())
            {
                return;
            }
            SyncOpenToMod(Tree.Root.Children);
        }

        private void SyncOpenToMod(List<InspectionTreeItem> items)
        {
            foreach (InspectionTreeItem item in items)
            {
                if (item.IsExpandable && syncedOpen.TryGetValue(item.Data, out bool was) && was != item.IsExpanded)
                {
                    VwCompat.SetOpen(item.Data, item.IsExpanded);
                    syncedOpen[item.Data] = item.IsExpanded;
                }
                SyncOpenToMod(item.Children);
            }
        }

        protected override bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            return item.IsExpandable;
        }

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            object node = item.Data;
            var d = new ElementDescription
            {
                Label = VwCompat.Text(node),
                Value = Money(node),
                Extras = ShownPercent(node),
            };
            if (item.IsExpandable)
            {
                d.Role = ElementRole.TreeItem;
                d.Expanded = item.IsExpanded;
            }
            else if (!VwCompat.HasInfoCard(node))
            {
                d.ReadOnly = true;
            }
            return d;
        }

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            if (item.IsExpandable)
            {
                PerformActivateExpandToggle(item);
                return;
            }
            ActivateLeaf(item.Data);
        }

        // ------------------------------------------------------------------
        // Flat list: a table of every leaf.
        // ------------------------------------------------------------------

        private const int ItemColumn = 0;
        private const int ValueColumn = 1;

        private static bool PercentShown => VwCompat.ShowsPercentOfTotal || VwCompat.ShowsPercentOfCategory;

        protected override int ContentColumnCount(int region)
        {
            if (region != FlatRegion || mode != ChartMode.FlatList)
            {
                return 0;
            }
            return PercentShown ? 3 : 2;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            switch (column)
            {
                case ItemColumn: return new TableColumnInfo("RimWorldAccess.Compat.VisibleWealth.ItemColumn".Translate());
                case ValueColumn: return new TableColumnInfo("RimWorldAccess.Compat.VisibleWealth.ValueColumn".Translate());
                default: return new TableColumnInfo("RimWorldAccess.Compat.VisibleWealth.PercentColumn".Translate());
            }
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            object node = NodeAt(flatRows, row);
            if (node == null)
            {
                return null;
            }
            switch (column)
            {
                case ItemColumn: return VwCompat.Text(node);
                case ValueColumn: return Money(node);
                default: return ShownPercent(node);
            }
        }

        /// <summary>The list is ordered by the toolbar's sort options, which the header reports.</summary>
        protected override bool TryGetLiveSort(int region, out int column, out bool descending)
        {
            column = VwCompat.SortsByName ? ItemColumn : ValueColumn;
            descending = !VwCompat.SortAscending;
            return region == FlatRegion && mode == ChartMode.FlatList;
        }

        private ElementDescription DescribeFlatRow(int row)
        {
            object node = NodeAt(flatRows, row);
            return new ElementDescription { Label = node != null ? VwCompat.Text(node) : null };
        }

        // ------------------------------------------------------------------
        // Pie chart: the slices.
        // ------------------------------------------------------------------

        private ElementDescription DescribeSlice(int index)
        {
            var d = new ElementDescription();
            object node = NodeAt(slices, index);
            if (node == null)
            {
                return d;
            }
            d.Label = VwCompat.Text(node);
            d.Value = Money(node);
            d.Level = VwCompat.Level(node) + 1;
            if (!VwCompat.IsLeaf(node))
            {
                d.Role = ElementRole.TreeItem;
                d.Expanded = false;
            }
            var extras = new List<string>
            {
                "RimWorldAccess.Compat.VisibleWealth.PercentOfTotal".Translate(VwCompat.PercentOfTotalText(node)),
            };
            object parent = VwCompat.Parent(node);
            if (parent != null)
            {
                extras.Add(VwCompat.ShowsPercentOfCategory
                    ? (string)"RimWorldAccess.Compat.VisibleWealth.PercentOfParent".Translate(VwCompat.PercentText(node, VwCompat.PercentOf), VwCompat.Text(parent))
                    : (string)"RimWorldAccess.Compat.VisibleWealth.InParent".Translate(VwCompat.Text(parent)));
            }
            if (VwCompat.SearchActive() && !VwCompat.MatchesSearch(node))
            {
                extras.Add("RimWorldAccess.Compat.VisibleWealth.NotMatching".Translate());
            }
            d.Extras = CompatText.JoinSentences(extras);
            return d;
        }

        /// <summary>A click on a slice: a leaf opens its info card, a branch opens into its parts.</summary>
        private void ActivateSlice(int index)
        {
            object node = NodeAt(slices, index);
            if (node == null || RefuseWhileSearching())
            {
                return;
            }
            if (VwCompat.IsLeaf(node))
            {
                ActivateLeaf(node);
                return;
            }
            ExpandSlice(node);
        }

        /// <summary>Right opens a branch as a click would; Left closes the slice's parent as its ring or arrow does.</summary>
        private void AdjustSlice(int index, int direction)
        {
            object node = NodeAt(slices, index);
            if (node == null || RefuseWhileSearching())
            {
                return;
            }
            if (direction > 0)
            {
                if (VwCompat.IsLeaf(node))
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return;
                }
                ExpandSlice(node);
                return;
            }
            object parent = VwCompat.Parent(node);
            if (parent == null || !CanCollapseFromChart(parent))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            VwCompat.SetOpen(parent, false);
            SoundDefOf.TabClose.PlayOneShotOnCamera();
            LandOnSlice(parent);
        }

        /// <summary>
        /// The nested chart draws every open ancestor as a clickable ring; the flat chart has a
        /// collapse arrow only on open nodes with no open child (ChartWorker_PieChart.LowestOpenNodes).
        /// </summary>
        private static bool CanCollapseFromChart(object parent)
        {
            if (VwCompat.IsNestedPie)
            {
                return true;
            }
            foreach (object child in VwCompat.Children(parent))
            {
                if (VwCompat.IsOpen(child))
                {
                    return false;
                }
            }
            return true;
        }

        private void ExpandSlice(object node)
        {
            VwCompat.SetOpen(node, true);
            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            LandOnSlice(node);
        }

        /// <summary>Puts the cursor on <paramref name="node"/>'s slice, or on the first slice it opened into.</summary>
        private void LandOnSlice(object node)
        {
            ResyncChart();
            for (int i = 0; i < slices.Count; i++)
            {
                for (object n = slices[i]; n != null; n = VwCompat.Parent(n))
                {
                    if (ReferenceEquals(n, node))
                    {
                        Model.Region(SliceRegion)?.MoveTo(i);
                        AnnounceCurrentItem();
                        return;
                    }
                }
            }
            AnnounceCurrentItem();
        }

        /// <summary>The chart ignores clicks while the search is on.</summary>
        private static bool RefuseWhileSearching()
        {
            if (!VwCompat.SearchActive())
            {
                return false;
            }
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.SpeakData("RimWorldAccess.Compat.VisibleWealth.ChartLockedWhileSearching".Translate());
            return true;
        }

        // ------------------------------------------------------------------
        // Shared row text.
        // ------------------------------------------------------------------

        /// <summary>The value the row prints, in the mod's whole-unit format.</summary>
        private static string Money(object node)
        {
            return "RimWorldAccess.Compat.VisibleWealth.Money".Translate(VwCompat.Value(node).ToString("F0"));
        }

        /// <summary>The percentage the row prints under the toolbar's setting, or null when it prints none.</summary>
        private static string ShownPercent(object node)
        {
            if (VwCompat.ShowsPercentOfTotal)
            {
                return "RimWorldAccess.Compat.VisibleWealth.PercentOfTotal".Translate(VwCompat.PercentOfTotalText(node));
            }
            if (!VwCompat.ShowsPercentOfCategory)
            {
                return null;
            }
            string percent = VwCompat.PercentText(node, VwCompat.PercentOf);
            object parent = VwCompat.Parent(node);
            return parent != null
                ? (string)"RimWorldAccess.Compat.VisibleWealth.PercentOfParent".Translate(percent, VwCompat.Text(parent))
                : (string)"RimWorldAccess.Compat.VisibleWealth.PercentOfTotal".Translate(percent);
        }
    }
}
