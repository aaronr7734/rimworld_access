using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>Every downed, bleeding, injured or sick pawn a sighted player could click on the current map.</summary>
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

    /// <summary>Keyboard scope for <see cref="GlobalHealthWindow"/>. Five nodes that always exist: Downed and
    /// bleeding, Downed, Bleeding and Injured, each hurt pawn in the first it matches; and Sick, which also lists a
    /// pawn already under one of those, one child per disease, and leads while its worst disease is life-threatening.
    /// Most urgent first; each node's label summarizes it. Enter closes the window and opens the health tab there.</summary>
    public sealed class GlobalHealthScope : MapOverviewScope
    {
        private enum Node { DownedBleeding, Downed, Bleeding, Injured, Sick }

        private static readonly Node[] InjuryNodes = { Node.DownedBleeding, Node.Downed, Node.Bleeding, Node.Injured };

        private readonly struct Entry
        {
            public readonly Pawn Pawn;
            public readonly MapPawnGroup Group;
            public readonly PawnHealthSummary Health;
            public readonly string Label;

            public readonly List<Disease> Diseases;

            public Entry(Pawn pawn, MapPawnGroup group, PawnHealthSummary health, List<Disease> diseases = null)
            {
                Pawn = pawn;
                Group = group;
                Health = health;
                Label = pawn.LabelShortCap;
                Diseases = diseases;
            }
        }

        private sealed class Disease
        {
            public readonly Hediff Hediff;
            public readonly int Order;
            public readonly bool Threat;
            public readonly float Margin;

            public Disease(Hediff hediff, int order)
            {
                Hediff = hediff;
                Order = order;
                Threat = IsThreat(hediff);
                Margin = ImmunityMargin(hediff);
            }
        }

        internal static GlobalHealthScope Current { get; private set; }

        private readonly GlobalHealthWindow window;
        private readonly List<Entry>[] buckets = { new List<Entry>(), new List<Entry>(), new List<Entry>(), new List<Entry>(), new List<Entry>() };
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
            return item.Data is Pawn || item.Data is Hediff;
        }

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            if (item.Data is Pawn pawn)
            {
                OpenHealthTab(pawn, null);
                return;
            }
            if (item.Data is Hediff hediff)
            {
                OpenHealthTab(hediff.pawn, hediff);
                return;
            }
            if (item.IsExpandable)
            {
                PerformActivateExpandToggle(item);
                return;
            }
            AnnounceCurrentItem();
        }

        /// <summary>The window closes first: the inspect-tab bridge stands down under a foreign dialog. The
        /// tab's own tree then speaks, so success says nothing here.</summary>
        private void OpenHealthTab(Pawn pawn, Hediff landing)
        {
            if (pawn.Destroyed || !pawn.Spawned || !CameraJumper.CanJump(pawn))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData("RimWorldAccess.GlobalHealth.JumpUnavailable".Translate());
                return;
            }
            window.Close();
            CameraJumper.TryJumpAndSelect(pawn);
            bool landed = WindowlessInspectionState.OpenTabLandingOn(typeof(ITab_Pawn_Health), landing);
            if (!landed)
            {
                MapNavigationState.SpeakJumpedTo(pawn.LabelShort);
            }
            ArmReturn(landed, GlobalHealthWindow.Open);
        }

        private void PerformInfoCard()
        {
            object data = CurrentTreeItem()?.Data;
            if (data is Pawn pawn)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(pawn));
                return;
            }
            if (data is Hediff hediff)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(hediff));
                return;
            }
            InfoCardState.SpeakNoInfoCardAvailable();
        }


        /// <summary>Wild animals that are not hostile are left out.</summary>
        private static bool TryEligible(Pawn pawn, MapPawnGroupFilter shown, out MapPawnGroup group)
        {
            group = MapPawnGroup.Wild;
            if (pawn.Dead || !ThingSelectionUtility.SelectableByMapClick(pawn))
            {
                return false;
            }
            group = MapPawnGroups.Of(pawn);
            return !(group == MapPawnGroup.Wild && pawn.RaceProps.Animal) && MapPawnGroups.Shown(group, shown);
        }

        /// <summary>Vanilla's own sick verdict, per hediff: HediffSet.AnyHediffMakesSickThought.</summary>
        private static bool IsDisease(Hediff hediff)
        {
            return hediff.def.makesSickThought && hediff.Visible;
        }

        /// <summary>Alert_LifeThreateningHediff's test, which ignores a disease the pawn is already immune to.</summary>
        private static bool IsThreat(Hediff hediff)
        {
            return hediff.IsCurrentlyLifeThreatening && !hediff.FullyImmune();
        }

        /// <summary>Immunity minus severity, the two percentages the tooltip prints; only a lethal disease still being fought has one.</summary>
        private static float ImmunityMargin(Hediff hediff)
        {
            HediffComp_Immunizable immunizable = hediff.TryGetComp<HediffComp_Immunizable>();
            if (immunizable == null || immunizable.FullyImmune || !hediff.IsLethal)
            {
                return float.MaxValue;
            }
            return immunizable.NaturalImmunity - hediff.Severity / Mathf.Abs(hediff.def.lethalSeverity);
        }

        private static List<Disease> DiseasesOf(Pawn pawn)
        {
            List<Disease> diseases = null;
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (IsDisease(hediffs[i]))
                {
                    diseases = diseases ?? new List<Disease>();
                    diseases.Add(new Disease(hediffs[i], i));
                }
            }
            diseases?.Sort(ByUrgency);
            return diseases;
        }

        private static int DiseaseHash(Pawn pawn, out bool sick)
        {
            sick = false;
            int hash = 0;
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (!IsDisease(hediff))
                {
                    continue;
                }
                sick = true;
                unchecked
                {
                    hash = hash * 31 + hediff.loadID;
                    hash = hash * 31 + hediff.LabelCap.GetHashCode();
                    hash = hash * 31 + (hediff.SeverityLabel?.GetHashCode() ?? 0);
                    hash = hash * 31 + (hediff.TryGetComp<HediffComp_Immunizable>()?.CompTipStringExtra?.GetHashCode() ?? 0);
                    hash = hash * 31 + (hediff.TryGetComp<HediffComp_TendDuration>()?.CompTipStringExtra?.GetHashCode() ?? 0);
                    hash = hash * 31 + (hediff.IsCurrentlyLifeThreatening ? 1 : 0) + (IsThreat(hediff) ? 2 : 0);
                }
            }
            return hash;
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
                if (!TryEligible(pawn, shown, out MapPawnGroup group))
                {
                    continue;
                }
                PawnHealthSummary health = PawnHealthSummary.Of(pawn);
                int diseases = DiseaseHash(pawn, out bool sick);
                if (health.IsHealthy && !sick)
                {
                    continue;
                }
                unchecked
                {
                    hash = hash * 31 + pawn.thingIDNumber;
                    hash = hash * 31 + (int)group;
                    hash = hash * 31 + (health.IsHealthy ? 0 : health.SpokenHash());
                    hash = hash * 31 + diseases;
                    hash = hash * 31 + (NeedsTend(pawn) ? 1 : 0) + (NeedsRescue(pawn) ? 2 : 0);
                    hash = hash * 31 + pawn.LabelShort.GetHashCode();
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
            listedCount = 0;
            Map map = Find.CurrentMap;
            if (map != null)
            {
                IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (!TryEligible(pawn, shown, out MapPawnGroup group))
                    {
                        continue;
                    }
                    PawnHealthSummary health = PawnHealthSummary.Of(pawn);
                    List<Disease> diseases = DiseasesOf(pawn);
                    if (!health.IsHealthy)
                    {
                        buckets[(int)NodeOf(health)].Add(new Entry(pawn, group, health));
                    }
                    if (diseases != null)
                    {
                        buckets[(int)Node.Sick].Add(new Entry(pawn, group, health, diseases));
                    }
                    if (!health.IsHealthy || diseases != null)
                    {
                        listedCount++;
                    }
                }
            }

            List<Entry> sick = buckets[(int)Node.Sick];
            sick.Sort(BySickness);
            bool sickLeads = sick.Count > 0 && sick[0].Diseases[0].Threat;
            var root = new InspectionTreeItem { Label = "Root", IndentLevel = -1, IsExpanded = true };
            if (sickLeads)
            {
                AttachSickNode(root, sick);
            }
            foreach (Node node in InjuryNodes)
            {
                List<Entry> entries = buckets[(int)node];
                bool bleeding = node == Node.DownedBleeding || node == Node.Bleeding;
                entries.Sort(bleeding ? (Comparison<Entry>)ByBleedOut : ByHealth);
                string name = NodeName(node);
                var nodeItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Category,
                    Label = WithCare(Summary(name, entries, bleeding), entries, node == Node.DownedBleeding || node == Node.Downed),
                    ExpandedLabel = name,
                    IndentLevel = 0,
                    IsExpandable = entries.Count > 0,
                    Data = node,
                };
                InspectNodeFactory.Attach(root, nodeItem);
                foreach (Entry entry in ColonyFirst(entries))
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
            if (!sickLeads)
            {
                AttachSickNode(root, sick);
            }
            return root;
        }

        private static void AttachSickNode(InspectionTreeItem root, List<Entry> entries)
        {
            string name = NodeName(Node.Sick);
            var nodeItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                ExpandedLabel = name,
                IndentLevel = 0,
                IsExpandable = entries.Count > 0,
                Data = Node.Sick,
            };
            InspectNodeFactory.Attach(root, nodeItem);
            string worstHeader = null;
            foreach (Entry entry in ColonyFirst(entries))
            {
                string pawnName = MapPawnGroups.NameWithGroup(entry.Label, entry.Group);
                var headers = new List<string>(entry.Diseases.Count);
                var pawnItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    ExpandedLabel = pawnName,
                    IndentLevel = 1,
                    IsExpandable = true,
                    Data = entry.Pawn,
                };
                foreach (Disease disease in entry.Diseases)
                {
                    List<string> lines = HealthTabHelper.GetHediffSummaryLines(disease.Hediff, entry.Pawn);
                    if (entry.Pawn == entries[0].Pawn && worstHeader == null)
                    {
                        worstHeader = lines[0];
                    }
                    string header = disease.Threat
                        ? (string)"RimWorldAccess.GlobalHealth.DiseaseThreat".Translate(lines[0])
                        : lines[0];
                    string immunity = disease.Hediff.TryGetComp<HediffComp_Immunizable>()?.CompTipStringExtra;
                    headers.Add(immunity == null ? header : new AnnouncementBuilder().Add(header).Add(immunity, Separator.Comma).Build());
                    InspectNodeFactory.Attach(pawnItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = string.Join(". ", lines),
                        IndentLevel = 2,
                        Data = disease.Hediff,
                    });
                }
                pawnItem.Label = "RimWorldAccess.GlobalHealth.SickPawnRow".Translate(pawnName, string.Join(". ", headers));
                InspectNodeFactory.Attach(nodeItem, pawnItem);
            }
            nodeItem.Label = WithSickCare(SickSummary(name, entries, worstHeader), entries);
        }

        private static List<Entry> ColonyFirst(List<Entry> entries)
        {
            return entries.OrderBy(e => e.Group <= MapPawnGroup.Slave ? 0 : (int)e.Group).ToList();
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
                case Node.Sick:
                    return "RimWorldAccess.GlobalHealth.Node.Sick".Translate();
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
                    ? "RimWorldAccess.GlobalHealth.Summary.HealthPawn".Translate(name, entries[0].Label, health)
                    : "RimWorldAccess.GlobalHealth.Summary.LowestHealth".Translate(name, entries.Count, health);
            }
            if (first.Bleeding == BleedOutKind.Timed)
            {
                string period = first.TicksToBleedOut.ToStringTicksToPeriod();
                return one
                    ? "RimWorldAccess.GlobalHealth.Summary.BleedOutPawn".Translate(name, entries[0].Label, period)
                    : "RimWorldAccess.GlobalHealth.Summary.BleedOut".Translate(name, entries.Count, period);
            }
            if (first.Bleeding == BleedOutKind.Deathless)
            {
                return one
                    ? "RimWorldAccess.GlobalHealth.Summary.DeathlessPawn".Translate(name, entries[0].Label)
                    : "RimWorldAccess.GlobalHealth.Summary.Deathless".Translate(name, entries.Count);
            }
            return one
                ? "RimWorldAccess.GlobalHealth.Summary.NotSoonPawn".Translate(name, entries[0].Label)
                : "RimWorldAccess.GlobalHealth.Summary.NotSoon".Translate(name, entries.Count);
        }

        // Alert_ColonistNeedsRescuing only scans free colonists and player-controlled subhumans.
        private static bool NeedsRescue(Pawn pawn)
        {
            return (pawn.IsFreeColonist || (pawn.Faction == Faction.OfPlayer && pawn.IsColonySubhumanPlayerControlled))
                && Alert_ColonistNeedsRescuing.NeedsRescue(pawn);
        }

        private static bool NeedsTend(Pawn pawn)
        {
            return pawn.health.HasHediffsNeedingTendByPlayer();
        }

        private static string WithCare(string summary, List<Entry> entries, bool downed)
        {
            int rescue = downed ? entries.Count(e => NeedsRescue(e.Pawn)) : 0;
            int tend = entries.Count(e => NeedsTend(e.Pawn));
            if (entries.Count == 1)
            {
                string single = rescue > 0 && tend > 0 ? "RimWorldAccess.GlobalHealth.Care.Pawn.RescueAndTend".Translate().ToString()
                    : rescue > 0 ? "RimWorldAccess.GlobalHealth.Care.Pawn.Rescue".Translate().ToString()
                    : tend > 0 ? "RimWorldAccess.GlobalHealth.Care.Pawn.Tend".Translate().ToString()
                    : null;
                return single == null ? summary : new AnnouncementBuilder().Add(summary).Add(single, Separator.Comma).Build();
            }
            var care = new List<string>();
            if (rescue > 0)
            {
                care.Add(CareCount(rescue, entries.Count, true));
            }
            if (tend > 0)
            {
                care.Add(CareCount(tend, entries.Count, false));
            }
            return AppendCare(summary, care);
        }

        private static string CareCount(int count, int total, bool rescue)
        {
            if (count == total)
            {
                return rescue ? "RimWorldAccess.GlobalHealth.Care.AllNeedRescue".Translate().ToString()
                    : "RimWorldAccess.GlobalHealth.Care.AllNeedTend".Translate().ToString();
            }
            if (count == 1)
            {
                return rescue ? "RimWorldAccess.GlobalHealth.Care.NeedRescueOne".Translate().ToString()
                    : "RimWorldAccess.GlobalHealth.Care.NeedTendOne".Translate().ToString();
            }
            return rescue ? "RimWorldAccess.GlobalHealth.Care.NeedRescue".Translate(count).ToString()
                : "RimWorldAccess.GlobalHealth.Care.NeedTend".Translate(count).ToString();
        }

        private static string WithSickCare(string summary, List<Entry> entries)
        {
            var diseases = entries.SelectMany(e => e.Diseases).ToList();
            int untended = diseases.Count(d => d.Hediff.TendableNow() && !d.Hediff.IsTended());
            int threats = diseases.Count(d => d.Threat);
            var care = new List<string>();
            if (untended > 0)
            {
                care.Add(untended == 1
                    ? "RimWorldAccess.GlobalHealth.Care.UntendedOne".Translate().ToString()
                    : "RimWorldAccess.GlobalHealth.Care.Untended".Translate(untended).ToString());
            }
            if (threats > 1)
            {
                care.Add("RimWorldAccess.GlobalHealth.Care.Threats".Translate(threats).ToString());
            }
            if (entries.Count == 1 && care.Count > 0)
            {
                var builder = new AnnouncementBuilder().Add(summary);
                foreach (string part in care)
                {
                    builder.Add(part, Separator.Comma);
                }
                return builder.Build();
            }
            return AppendCare(summary, care);
        }

        private static string AppendCare(string summary, List<string> care)
        {
            if (care.Count == 0)
            {
                return summary;
            }
            var builder = new AnnouncementBuilder().Add(summary).Add(care[0]);
            for (int i = 1; i < care.Count; i++)
            {
                builder.Add(care[i], Separator.Comma);
            }
            return builder.Build();
        }

        private static string SickSummary(string name, List<Entry> entries, string header)
        {
            if (entries.Count == 0)
            {
                return "RimWorldAccess.GlobalHealth.Summary.None".Translate(name);
            }
            Entry first = entries[0];
            Disease worst = first.Diseases[0];
            bool one = entries.Count == 1;
            if (worst.Threat)
            {
                return one
                    ? "RimWorldAccess.GlobalHealth.Summary.SickThreatPawn".Translate(name, first.Label, header)
                    : "RimWorldAccess.GlobalHealth.Summary.SickThreat".Translate(name, entries.Count, first.Label, header);
            }
            return one
                ? "RimWorldAccess.GlobalHealth.Summary.SickPawn".Translate(name, first.Label, header)
                : "RimWorldAccess.GlobalHealth.Summary.Sick".Translate(name, entries.Count, first.Label, header);
        }

        /// <summary>Life-threatening first, then the narrowest immunity race, then the health tab's order.</summary>
        private static int ByUrgency(Disease a, Disease b)
        {
            int c = b.Threat.CompareTo(a.Threat);
            if (c == 0)
            {
                c = a.Margin.CompareTo(b.Margin);
            }
            return c != 0 ? c : a.Order.CompareTo(b.Order);
        }

        private static int BySickness(Entry a, Entry b)
        {
            Disease x = a.Diseases[0];
            Disease y = b.Diseases[0];
            int c = y.Threat.CompareTo(x.Threat);
            if (c == 0)
            {
                c = x.Margin.CompareTo(y.Margin);
            }
            return c != 0 ? c : ByHealth(a, b);
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
