using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>Every downed, bleeding or injured pawn a sighted player could click on the current map.</summary>
    public sealed class GlobalHealthWindow : MapOverviewWindow
    {
        internal static void Open()
        {
            if (!Find.WindowStack.IsOpen<GlobalHealthWindow>())
            {
                Find.WindowStack.Add(new GlobalHealthWindow());
            }
        }

        internal static string OpenerHotkey()
        {
            return ActionRegistry.Catalog.TryGet("map.openGlobalHealth", out InputAction action) && action.Bindings.Count > 0
                ? action.Bindings[0].DisplayLabel
                : null;
        }

        protected override MapOverviewScope Scope
        {
            get { return GlobalHealthScope.Current; }
        }

        protected override string Title
        {
            get { return "RimWorldAccess.GlobalHealth.Title".Translate(); }
        }
    }

    /// <summary>
    /// Keyboard scope for <see cref="GlobalHealthWindow"/>. Four nodes that always exist, Downed
    /// and bleeding, Downed, Bleeding and Injured, each pawn in the first it matches and the most
    /// urgent first; each node's label summarizes it. Enter on a pawn closes the window and opens
    /// its health tab.
    /// </summary>
    public sealed class GlobalHealthScope : MapOverviewScope
    {
        private enum Node { DownedBleeding, Downed, Bleeding, Injured }

        private static readonly Node[] Nodes = { Node.DownedBleeding, Node.Downed, Node.Bleeding, Node.Injured };

        private readonly struct Entry
        {
            public readonly Pawn Pawn;
            public readonly MapPawnGroup Group;
            public readonly PawnHealthSummary Health;
            public readonly string Label;

            public Entry(Pawn pawn, MapPawnGroup group, PawnHealthSummary health)
            {
                Pawn = pawn;
                Group = group;
                Health = health;
                Label = pawn.LabelShortCap;
            }
        }

        internal static GlobalHealthScope Current { get; private set; }

        private readonly GlobalHealthWindow window;
        private readonly List<Entry>[] buckets = { new List<Entry>(), new List<Entry>(), new List<Entry>(), new List<Entry>() };
        private int listedCount;

        public GlobalHealthScope(GlobalHealthWindow window) : base(window)
        {
            this.window = window;
            Claim(SharedMenuGrammar.Info, e => PerformInfoCard());
        }

        public override string Name
        {
            get { return "global-health"; }
        }

        protected override string TreeRegionLabel
        {
            get { return "RimWorldAccess.GlobalHealth.RegionName".Translate(); }
        }

        protected override MapPawnGroupFilter ShownGroups
        {
            get
            {
                RimWorldAccessSettings s = RimWorldAccessMod_Settings.Settings;
                return s != null ? s.GlobalHealthGroups : MapPawnGroupFilter.All;
            }
            set
            {
                RimWorldAccessSettings s = RimWorldAccessMod_Settings.Settings;
                if (s != null)
                {
                    s.GlobalHealthGroups = value;
                }
            }
        }

        public override void OnPush()
        {
            base.OnPush();
            Current = this;
        }

        public override void OnPop()
        {
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }
            base.OnPop();
        }

        protected override string ComposeOpenAnnouncement()
        {
            return "RimWorldAccess.GlobalHealth.Title".Translate();
        }

        protected override string FilterChangedAnnouncement(string state)
        {
            return listedCount == 1
                ? (string)"RimWorldAccess.GlobalHealth.FilterChangedOne".Translate(state)
                : (string)"RimWorldAccess.GlobalHealth.FilterChanged".Translate(state, listedCount);
        }

        protected override bool IsActionRow(InspectionTreeItem item)
        {
            return item.Data is Pawn;
        }

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            if (item.Data is Pawn pawn)
            {
                OpenHealthTab(pawn);
                return;
            }
            if (item.IsExpandable)
            {
                PerformActivateExpandToggle(item);
                return;
            }
            AnnounceCurrentItem();
        }

        /// <summary>
        /// The window closes first: the inspect-tab bridge stands down under a foreign dialog. The
        /// tab's own tree then speaks, so success says nothing here.
        /// </summary>
        private void OpenHealthTab(Pawn pawn)
        {
            if (pawn.Destroyed || !pawn.Spawned || !CameraJumper.CanJump(pawn))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData("RimWorldAccess.GlobalHealth.JumpUnavailable".Translate());
                return;
            }
            window.Close();
            CameraJumper.TryJumpAndSelect(pawn);
            if (!WindowlessInspectionState.OpenTabLandingOn(typeof(ITab_Pawn_Health), null))
            {
                MapNavigationState.SpeakJumpedTo(pawn.LabelShort);
            }
        }

        private void PerformInfoCard()
        {
            if (CurrentTreeItem()?.Data is Pawn pawn)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(pawn));
                return;
            }
            InfoCardState.SpeakNoInfoCardAvailable();
        }

        // Tree.

        /// <summary>Wild animals that are not hostile are left out.</summary>
        private static bool TryListed(Pawn pawn, MapPawnGroupFilter shown, out MapPawnGroup group, out PawnHealthSummary health)
        {
            health = default(PawnHealthSummary);
            group = MapPawnGroup.Wild;
            if (pawn.Dead || !ThingSelectionUtility.SelectableByMapClick(pawn))
            {
                return false;
            }
            group = MapPawnGroups.Of(pawn);
            if ((group == MapPawnGroup.Wild && pawn.RaceProps.Animal) || !MapPawnGroups.Shown(group, shown))
            {
                return false;
            }
            health = PawnHealthSummary.Of(pawn);
            return !health.IsHealthy;
        }

        protected override int ContentSignature(MapPawnGroupFilter shown)
        {
            int hash = (int)shown;
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return hash;
            }
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (TryListed(pawn, shown, out MapPawnGroup group, out PawnHealthSummary health))
                {
                    unchecked
                    {
                        hash = hash * 31 + pawn.thingIDNumber;
                        hash = hash * 31 + (int)group;
                        hash = hash * 31 + health.SpokenHash();
                        hash = hash * 31 + pawn.LabelShort.GetHashCode();
                    }
                }
            }
            return hash;
        }

        protected override InspectionTreeItem BuildRoot(MapPawnGroupFilter shown)
        {
            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i].Clear();
            }
            Map map = Find.CurrentMap;
            if (map != null)
            {
                IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    if (TryListed(pawns[i], shown, out MapPawnGroup group, out PawnHealthSummary health))
                    {
                        buckets[(int)NodeOf(health)].Add(new Entry(pawns[i], group, health));
                    }
                }
            }

            var root = new InspectionTreeItem { Label = "Root", IndentLevel = -1, IsExpanded = true };
            listedCount = 0;
            foreach (Node node in Nodes)
            {
                List<Entry> entries = buckets[(int)node];
                bool bleeding = node == Node.DownedBleeding || node == Node.Bleeding;
                entries.Sort(bleeding ? (Comparison<Entry>)ByBleedOut : ByHealth);
                listedCount += entries.Count;
                string name = NodeName(node);
                var nodeItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Category,
                    Label = Summary(name, entries, bleeding),
                    ExpandedLabel = name,
                    IndentLevel = 0,
                    IsExpandable = entries.Count > 0,
                    Data = node,
                };
                InspectNodeFactory.Attach(root, nodeItem);
                foreach (Entry entry in entries)
                {
                    InspectNodeFactory.Attach(nodeItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = "RimWorldAccess.GlobalHealth.PawnRow".Translate(
                            MapPawnGroups.NameWithGroup(entry.Label, entry.Group), entry.Health.Tag()),
                        IndentLevel = 1,
                        Data = entry.Pawn,
                    });
                }
            }
            return root;
        }

        private static Node NodeOf(PawnHealthSummary health)
        {
            if (health.Downed)
            {
                return health.IsBleeding ? Node.DownedBleeding : Node.Downed;
            }
            return health.IsBleeding ? Node.Bleeding : Node.Injured;
        }

        private static string NodeName(Node node)
        {
            switch (node)
            {
                case Node.DownedBleeding:
                    return "RimWorldAccess.GlobalHealth.Node.DownedBleeding".Translate();
                case Node.Downed:
                    return "RimWorldAccess.GlobalHealth.Node.Downed".Translate();
                case Node.Bleeding:
                    return "RimWorldAccess.GlobalHealth.Node.Bleeding".Translate();
                default:
                    return "RimWorldAccess.GlobalHealth.Node.Injured".Translate();
            }
        }

        /// <summary>The count, then the soonest bleed-out or the lowest health: the first entry, as sorted.</summary>
        private static string Summary(string name, List<Entry> entries, bool bleeding)
        {
            if (entries.Count == 0)
            {
                return "RimWorldAccess.GlobalHealth.Summary.None".Translate(name);
            }
            PawnHealthSummary first = entries[0].Health;
            bool one = entries.Count == 1;
            if (!bleeding)
            {
                string health = first.HealthFraction.ToStringPercent();
                return one
                    ? "RimWorldAccess.GlobalHealth.Summary.HealthOne".Translate(name, health)
                    : "RimWorldAccess.GlobalHealth.Summary.LowestHealth".Translate(name, entries.Count, health);
            }
            if (first.Bleeding == BleedOutKind.Timed)
            {
                string period = first.TicksToBleedOut.ToStringTicksToPeriod();
                return one
                    ? "RimWorldAccess.GlobalHealth.Summary.BleedOutOne".Translate(name, period)
                    : "RimWorldAccess.GlobalHealth.Summary.BleedOut".Translate(name, entries.Count, period);
            }
            if (first.Bleeding == BleedOutKind.Deathless)
            {
                return one
                    ? "RimWorldAccess.GlobalHealth.Summary.DeathlessOne".Translate(name)
                    : "RimWorldAccess.GlobalHealth.Summary.Deathless".Translate(name, entries.Count);
            }
            return one
                ? "RimWorldAccess.GlobalHealth.Summary.NotSoonOne".Translate(name)
                : "RimWorldAccess.GlobalHealth.Summary.NotSoon".Translate(name, entries.Count);
        }

        private static int ByHealth(Entry a, Entry b)
        {
            int c = a.Health.HealthFraction.CompareTo(b.Health.HealthFraction);
            return c != 0 ? c : ByLabel(a, b);
        }

        /// <summary>Deathless bleeders carry no countdown, so they sort last.</summary>
        private static int ByBleedOut(Entry a, Entry b)
        {
            int c = a.Health.TicksToBleedOut.CompareTo(b.Health.TicksToBleedOut);
            return c != 0 ? c : ByLabel(a, b);
        }

        private static int ByLabel(Entry a, Entry b)
        {
            int c = string.Compare(a.Label, b.Label, StringComparison.CurrentCulture);
            return c != 0 ? c : a.Pawn.thingIDNumber.CompareTo(b.Pawn.thingIDNumber);
        }
    }
}
