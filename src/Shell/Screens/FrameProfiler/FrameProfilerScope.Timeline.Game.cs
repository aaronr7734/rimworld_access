using System.Collections;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The timeline region: the flamechart of the displayed frame as a zone tree. Each recorded
    /// thread is a top-level node (the main thread named for its frame, as the chart labels it), and
    /// each zone nests under the zone that contained it, siblings in the order they ran. The zone
    /// data comes from the same chunk walk the flamechart draws from. Navigation, expansion and
    /// search are the shared tree base's.
    /// </summary>
    internal sealed partial class FrameProfilerScope
    {
        private sealed class ZoneData
        {
            public int Ordinal;
            public int ThreadId;
            public int PathId = -1;
            public bool IsRoot;
            public bool IsMain;
            public object Snapshot;
            public object Section;
            public object Chunk;
            public int NodeIndex;
            public long StartNs;
            public long EndNs;
            public long FrameBeginNs;
            public bool Open;
            public bool IsPauseLane;
            public FrameProfilerAccess.Pause? Pause;
        }

        private TreePanel timelinePanel;
        private int timelineOrdinal = int.MinValue;

        protected override string TreeRegionLabel => "RimWorldAccess.FrameProfiler.Region.Timeline".Translate().ToString();

        protected override TreePanel PanelFor(int region)
        {
            return region >= 0 && region < regions.Count && regions[region] == RegionKind.Timeline ? timelinePanel : null;
        }

        private void RefreshTimeline()
        {
            object snapshot = FrameProfilerAccess.Snapshot(Session);
            int ordinal = snapshot == null ? -1 : FrameProfilerAccess.Ordinal(snapshot);
            if (ordinal == timelineOrdinal)
            {
                return;
            }
            timelineOrdinal = ordinal;
            bool inTimeline = CursorInTreeRegion && Model.CurrentRegion != null;
            SetTreeRootPreservingState(timelinePanel, BuildTimeline(snapshot), inTimeline ? Model.CurrentRegion.Index : timelinePanel.Tree.SelectedIndex);
            if (inTimeline)
            {
                Model.CurrentRegion.SetCount(timelinePanel.Tree.Count);
                Model.CurrentRegion.MoveTo(timelinePanel.Tree.SelectedIndex);
            }
        }

        private InspectionTreeItem BuildTimeline(object snapshot)
        {
            InspectionTreeItem root = new InspectionTreeItem { Children = new List<InspectionTreeItem>(), IndentLevel = -1 };
            if (snapshot == null)
            {
                return root;
            }
            int ordinal = FrameProfilerAccess.Ordinal(snapshot);
            long begin = FrameProfilerAccess.BeginNs(snapshot);
            AddPauseLane(root, snapshot, ordinal);
            int sections = FrameProfilerAccess.SectionCount(snapshot);
            for (int i = 0; i < sections; i++)
            {
                object section = FrameProfilerAccess.SectionAt(snapshot, i);
                int threadId = FrameProfilerAccess.SectionThreadId(section);
                bool main = i == 0;
                long startNs = main ? begin : begin + FrameProfilerAccess.SectionStartNs(section);
                long endNs = main ? FrameProfilerAccess.EndNs(snapshot) : begin + FrameProfilerAccess.SectionEndNs(section);
                IList chunks = FrameProfilerAccess.Chunks(snapshot, threadId, startNs, endNs);
                if (!main && chunks.Count == 0)
                {
                    continue;
                }
                ZoneData threadData = new ZoneData
                {
                    Ordinal = ordinal, ThreadId = threadId, IsRoot = true, IsMain = main, Snapshot = snapshot, Section = section,
                    StartNs = startNs, EndNs = endNs, FrameBeginNs = begin,
                };
                string threadName = main
                    ? "RimWorldAccess.FrameProfiler.Frame".Translate(FrameProfilerAccess.FrameIndex(snapshot)).ToString()
                    : FrameProfilerAccess.SectionName(section);
                InspectionTreeItem thread = NewZoneItem(root, threadName, threadData);
                AddZones(thread, chunks, threadData);
                root.Children.Add(thread);
            }
            return root;
        }

        /// <summary>The flamechart's top lane: every runtime pause in the frame, in the order it happened.</summary>
        private static void AddPauseLane(InspectionTreeItem root, object snapshot, int ordinal)
        {
            List<FrameProfilerAccess.Pause> pauses = FrameProfilerAccess.Pauses(snapshot);
            if (pauses.Count == 0)
            {
                return;
            }
            InspectionTreeItem lane = NewZoneItem(root, "RimWorldAccess.FrameProfiler.PauseLane".Translate().ToString(),
                new ZoneData { Ordinal = ordinal, IsRoot = true, IsPauseLane = true, Snapshot = snapshot });
            lane.IsExpandable = true;
            for (int i = 0; i < pauses.Count; i++)
            {
                lane.Children.Add(NewZoneItem(lane, pauses[i].Name, new ZoneData { Ordinal = ordinal, Snapshot = snapshot, Pause = pauses[i] }));
            }
            root.Children.Add(lane);
        }

        private static List<string> PauseStats(FrameProfilerAccess.Pause pause)
        {
            List<string> stats = new List<string>
            {
                "RimWorldAccess.FrameProfiler.ZoneStart".Translate(FrameProfilerAccess.FormatMs(pause.StartUs / 1000f)).ToString(),
                "RimWorldAccess.FrameProfiler.PauseStopped".Translate(FrameProfilerAccess.FormatMs(pause.StoppedUs / 1000f)).ToString(),
                "RimWorldAccess.FrameProfiler.PauseWaiting".Translate(FrameProfilerAccess.FormatMs(pause.WaitingUs / 1000f)).ToString(),
            };
            if (pause.CollectedBytes > 0 || pause.PromotedBytes > 0)
            {
                stats.Add("RimWorldAccess.FrameProfiler.PauseCollected".Translate(
                    FrameProfilerAccess.FormatBytes(System.Math.Max(0L, pause.CollectedBytes)), FrameProfilerAccess.FormatBytes(pause.PromotedBytes)).ToString());
            }
            if (pause.TriggeringThreadId == 0)
            {
                stats.Add("RimWorldAccess.FrameProfiler.PauseByRuntime".Translate().ToString());
            }
            else if (pause.TriggeringThreadId == System.Environment.CurrentManagedThreadId)
            {
                stats.Add("RimWorldAccess.FrameProfiler.PauseByMainThread".Translate().ToString());
            }
            else
            {
                stats.Add("RimWorldAccess.FrameProfiler.PauseByThread".Translate(pause.TriggeringThreadId).ToString());
            }
            return stats;
        }

        /// <summary>The flamechart's own chunk walk: nodes are pre-ordered, and a node's subtree ends at its <c>subtreeEnd</c>.</summary>
        private static void AddZones(InspectionTreeItem thread, IList chunks, ZoneData threadData)
        {
            List<int> ancestorEnds = new List<int>();
            List<InspectionTreeItem> ancestors = new List<InspectionTreeItem>();
            for (int c = 0; c < chunks.Count; c++)
            {
                object chunk = chunks[c];
                long baseNs = FrameProfilerAccess.ChunkBaseNs(chunk);
                int count = FrameProfilerAccess.ChunkCount(chunk);
                ancestorEnds.Clear();
                ancestors.Clear();
                int index = 0;
                while (index < count)
                {
                    while (ancestorEnds.Count > 0 && index >= ancestorEnds[ancestorEnds.Count - 1])
                    {
                        ancestorEnds.RemoveAt(ancestorEnds.Count - 1);
                        ancestors.RemoveAt(ancestors.Count - 1);
                    }
                    object node = FrameProfilerAccess.NodeAt(chunk, index);
                    bool open = FrameProfilerAccess.NodeIsOpen(node);
                    int subtreeEnd = FrameProfilerAccess.NodeSubtreeEnd(node);
                    bool closed = !open && subtreeEnd > index;
                    int end = closed ? subtreeEnd : count;
                    long startNs = baseNs + FrameProfilerAccess.NodeStartNs(node);
                    long endNs = closed ? baseNs + FrameProfilerAccess.NodeEndNs(node) : threadData.EndNs;
                    if (startNs >= threadData.EndNs || endNs <= threadData.StartNs)
                    {
                        index = end;
                        continue;
                    }
                    InspectionTreeItem parent = ancestors.Count > 0 ? ancestors[ancestors.Count - 1] : thread;
                    int pathId = FrameProfilerAccess.NodePathId(node);
                    ZoneData data = new ZoneData
                    {
                        Ordinal = threadData.Ordinal, ThreadId = threadData.ThreadId, PathId = pathId, Snapshot = threadData.Snapshot,
                        Chunk = chunk, NodeIndex = index, StartNs = startNs, EndNs = endNs, FrameBeginNs = threadData.FrameBeginNs, Open = open,
                    };
                    InspectionTreeItem item = NewZoneItem(parent, FrameProfilerAccess.PathName(pathId), data);
                    parent.Children.Add(item);
                    parent.IsExpandable = true;
                    ancestorEnds.Add(end);
                    ancestors.Add(item);
                    index++;
                }
            }
        }

        private static InspectionTreeItem NewZoneItem(InspectionTreeItem parent, string label, ZoneData data)
        {
            InspectionTreeItem item = new InspectionTreeItem
            {
                Children = new List<InspectionTreeItem>(),
                Parent = parent.IndentLevel < 0 ? null : parent,
                IndentLevel = parent.IndentLevel + 1,
                Data = data,
                Label = label,
            };
            return item;
        }

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            ElementDescription d = new ElementDescription { Label = item.Label, Role = ElementRole.TreeItem };
            ZoneData zone = item.Data as ZoneData;
            if (zone == null)
            {
                return d;
            }
            if (item.IsExpandable)
            {
                d.Expanded = item.IsExpanded;
            }

            List<string> stats = new List<string>();
            if (zone.Pause.HasValue)
            {
                stats = PauseStats(zone.Pause.Value);
            }
            else if (zone.IsPauseLane)
            {
                stats.Add("RimWorldAccess.FrameProfiler.PauseCount".Translate(item.Children.Count).ToString());
            }
            else if (zone.IsRoot)
            {
                float selfUs;
                long alloc;
                FrameProfilerAccess.SectionRootStats(zone.Snapshot, zone.Section, out selfUs, out alloc);
                stats.Add(zone.IsMain
                    ? "RimWorldAccess.FrameProfiler.ZoneTotal".Translate(FrameProfilerAccess.FormatMs(FrameProfilerAccess.TotalMs(zone.Snapshot))).ToString()
                    : "RimWorldAccess.FrameProfiler.ThreadBusy".Translate(FrameProfilerAccess.FormatMs(FrameProfilerAccess.SectionBusyNs(zone.Section) / 1000000f)).ToString());
                if (selfUs > 0f)
                {
                    stats.Add("RimWorldAccess.FrameProfiler.Unprofiled".Translate(FrameProfilerAccess.FormatMs(selfUs / 1000f)).ToString());
                }
                if (alloc > 0)
                {
                    stats.Add("RimWorldAccess.FrameProfiler.Alloc".Translate(FrameProfilerAccess.FormatBytes(alloc)).ToString());
                }
            }
            else
            {
                stats.Add("RimWorldAccess.FrameProfiler.ZoneStart".Translate(FrameProfilerAccess.FormatMs((zone.StartNs - zone.FrameBeginNs) / 1000000f)).ToString());
                stats.Add("RimWorldAccess.FrameProfiler.ZoneTotal".Translate(FrameProfilerAccess.FormatMs((zone.EndNs - zone.StartNs) / 1000000f)).ToString());
                stats.Add(zone.Open
                    ? "RimWorldAccess.FrameProfiler.ZoneRunning".Translate().ToString()
                    : "RimWorldAccess.FrameProfiler.ZoneSelf".Translate(FrameProfilerAccess.FormatMs(FrameProfilerAccess.ZoneSelfUs(zone.Chunk, zone.NodeIndex) / 1000f)).ToString());
                long bytes;
                int objects;
                if (FrameProfilerAccess.TryZoneAlloc(zone.Chunk, zone.NodeIndex, out bytes, out objects))
                {
                    stats.Add(AllocStat(bytes, objects));
                }
            }
            d.Extras = string.Join(". ", stats.ToArray());
            return d;
        }

        /// <summary>Enter on a zone does what clicking it does: selects it, which reveals its path in the call tree.</summary>
        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            ZoneData zone = item.Data as ZoneData;
            if (zone == null || zone.IsRoot || zone.Pause.HasValue)
            {
                AnnounceCurrentItem();
                return;
            }
            FrameProfilerAccess.SelectZone(window, zone.Ordinal, zone.ThreadId, zone.PathId);
            SoundDefOf.Click.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.FrameProfiler.RevealedInCallTree".Loc(item.Label));
        }

        private void SelectZoneRange()
        {
            InspectionTreeItem item = CurrentTreeItem();
            ZoneData zone = item?.Data as ZoneData;
            if (zone == null || zone.IsPauseLane || zone.Pause.HasValue || zone.EndNs <= zone.StartNs || FrameProfilerAccess.IsSpikes(Session))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            FrameProfilerAccess.SelectTimeRange(Session, zone.Ordinal, zone.ThreadId, zone.StartNs, zone.EndNs);
            SoundDefOf.Click.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.FrameProfiler.RangeSelected".Loc(item.Label));
        }

        private void HighlightZone(int index)
        {
            IReadOnlyList<InspectionTreeItem> visible = timelinePanel.Tree.Visible;
            ZoneData zone = index >= 0 && index < visible.Count ? visible[index].Data as ZoneData : null;
            if (zone != null && !zone.IsPauseLane && !zone.Pause.HasValue)
            {
                FrameProfilerAccess.HighlightZone(window, zone.Ordinal, zone.IsRoot ? -1 : zone.PathId);
            }
        }
    }
}
