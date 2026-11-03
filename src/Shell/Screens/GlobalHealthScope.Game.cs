using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

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
            return ActionChords.Display("map.openGlobalHealth");
        }

        protected override string TitleKey
        {
            get { return "RimWorldAccess.GlobalHealth.Title"; }
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

        private readonly struct Scanned
        {
            public readonly Pawn Pawn;
            public readonly MapPawnGroup Group;
            public readonly PawnHealthSummary Health;
            public readonly int DiseaseStart;
            public readonly int DiseaseCount;

            public Scanned(Pawn pawn, MapPawnGroup group, PawnHealthSummary health, int diseaseStart, int diseaseCount)
            {
                Pawn = pawn;
                Group = group;
                Health = health;
                DiseaseStart = diseaseStart;
                DiseaseCount = diseaseCount;
            }
        }

        private readonly List<Entry>[] buckets = { new List<Entry>(), new List<Entry>(), new List<Entry>(), new List<Entry>(), new List<Entry>() };
        private readonly List<Scanned> scanned = new List<Scanned>();
        private readonly List<(Hediff Hediff, int Order)> scannedDiseases = new List<(Hediff, int)>();

        public GlobalHealthScope(GlobalHealthWindow window) : base(window)
        {
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

        protected override string FilterChangedOneKey
        {
            get { return "RimWorldAccess.GlobalHealth.FilterChangedOne"; }
        }

        protected override string FilterChangedKey
        {
            get { return "RimWorldAccess.GlobalHealth.FilterChanged"; }
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

        private void OpenHealthTab(Pawn pawn, Hediff landing)
        {
            HandOffToTab(pawn, typeof(ITab_Pawn_Health), landing, "RimWorldAccess.GlobalHealth.JumpUnavailable", GlobalHealthWindow.Open);
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

        private void Scan(MapPawnGroupFilter shown)
        {
            scanned.Clear();
            scannedDiseases.Clear();
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!TryEligible(pawn, shown, out MapPawnGroup group))
                {
                    continue;
                }
                int start = scannedDiseases.Count;
                List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
                for (int h = 0; h < hediffs.Count; h++)
                {
                    if (IsDisease(hediffs[h]))
                    {
                        scannedDiseases.Add((hediffs[h], h));
                    }
                }
                PawnHealthSummary health = PawnHealthSummary.Of(pawn);
                int count = scannedDiseases.Count - start;
                if (!health.IsHealthy || count > 0)
                {
                    scanned.Add(new Scanned(pawn, group, health, start, count));
                }
            }
        }

        private int DiseaseHash(Scanned record)
        {
            int hash = 0;
            for (int i = record.DiseaseStart; i < record.DiseaseStart + record.DiseaseCount; i++)
            {
                Hediff hediff = scannedDiseases[i].Hediff;
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

        private List<Disease> DiseasesOf(Scanned record)
        {
            if (record.DiseaseCount == 0)
            {
                return null;
            }
            var diseases = new List<Disease>(record.DiseaseCount);
            for (int i = record.DiseaseStart; i < record.DiseaseStart + record.DiseaseCount; i++)
            {
                diseases.Add(new Disease(scannedDiseases[i].Hediff, scannedDiseases[i].Order));
            }
            diseases.Sort(ByUrgency);
            return diseases;
        }

        protected override int ContentSignature(MapPawnGroupFilter shown)
        {
            Scan(shown);
            int hash = (int)shown;
            for (int i = 0; i < scanned.Count; i++)
            {
                Scanned record = scanned[i];
                Pawn pawn = record.Pawn;
                unchecked
                {
                    hash = hash * 31 + pawn.thingIDNumber;
                    hash = hash * 31 + (int)record.Group;
                    hash = hash * 31 + (record.Health.IsHealthy ? 0 : record.Health.SpokenHash());
                    hash = hash * 31 + DiseaseHash(record);
                    hash = hash * 31 + (NeedsTend(pawn) ? 1 : 0) + (NeedsRescue(pawn) ? 2 : 0);
                    hash = hash * 31 + pawn.LabelShort.GetHashCode();
                }
            }
            return hash;
        }

        protected override InspectionTreeItem BuildRoot(MapPawnGroupFilter shown, out int listed)
        {
            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i].Clear();
            }
            listed = scanned.Count;
            foreach (Scanned record in scanned)
            {
                if (!record.Health.IsHealthy)
                {
                    buckets[(int)NodeOf(record.Health)].Add(new Entry(record.Pawn, record.Group, record.Health));
                }
                List<Disease> diseases = DiseasesOf(record);
                if (diseases != null)
                {
                    buckets[(int)Node.Sick].Add(new Entry(record.Pawn, record.Group, record.Health, diseases));
                }
            }

            List<Entry> sick = buckets[(int)Node.Sick];
            sick.Sort(ColonyFirst(BySickness));
            bool sickLeads = sick.Count > 0 && Worst(sick, BySickness).Diseases[0].Threat;
            var root = new InspectionTreeItem { Label = "Root", IndentLevel = -1, IsExpanded = true };
            if (sickLeads)
            {
                AttachSickNode(root, sick);
            }
            foreach (Node node in InjuryNodes)
            {
                List<Entry> entries = buckets[(int)node];
                bool bleeding = node == Node.DownedBleeding || node == Node.Bleeding;
                Comparison<Entry> urgency = bleeding ? (Comparison<Entry>)ByBleedOut : ByHealth;
                entries.Sort(ColonyFirst(urgency));
                string name = NodeName(node);
                var nodeItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Category,
                    Label = WithCare(Summary(name, entries, urgency, bleeding), entries, node == Node.DownedBleeding || node == Node.Downed),
                    ExpandedLabel = name,
                    IndentLevel = 0,
                    IsExpandable = entries.Count > 0,
                    Data = node,
                };
                InspectNodeFactory.Attach(root, nodeItem);
                foreach (Entry entry in entries)
                {
                    Pawn pawn = entry.Pawn;
                    InspectNodeFactory.Attach(nodeItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = "RimWorldAccess.GlobalHealth.PawnRow".Translate(
                            MapPawnGroups.NameWithGroup(entry.Label, entry.Group), entry.Health.Tag()),
                        IndentLevel = 1,
                        Data = pawn,
                        OnInfo = delegate { Find.WindowStack.Add(new Dialog_InfoCard(pawn)); },
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
            foreach (Entry entry in entries)
            {
                Pawn pawn = entry.Pawn;
                string pawnName = MapPawnGroups.NameWithGroup(entry.Label, entry.Group);
                var headers = new List<string>(entry.Diseases.Count);
                var pawnItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    ExpandedLabel = pawnName,
                    IndentLevel = 1,
                    IsExpandable = true,
                    Data = pawn,
                    OnInfo = delegate { Find.WindowStack.Add(new Dialog_InfoCard(pawn)); },
                };
                foreach (Disease disease in entry.Diseases)
                {
                    Hediff hediff = disease.Hediff;
                    List<string> lines = HealthTabHelper.GetHediffSummaryLines(hediff, pawn, out string immunity);
                    string header = disease.Threat
                        ? (string)"RimWorldAccess.GlobalHealth.DiseaseThreat".Translate(lines[0])
                        : lines[0];
                    headers.Add(immunity == null ? header : new AnnouncementBuilder().Add(header).Add(immunity, Separator.Comma).Build());
                    InspectNodeFactory.Attach(pawnItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = string.Join(". ", lines),
                        IndentLevel = 2,
                        Data = hediff,
                        OnInfo = delegate { Find.WindowStack.Add(new Dialog_InfoCard(hediff)); },
                    });
                }
                pawnItem.Label = "RimWorldAccess.GlobalHealth.SickPawnRow".Translate(pawnName, string.Join(". ", headers));
                InspectNodeFactory.Attach(nodeItem, pawnItem);
            }
            nodeItem.Label = WithSickCare(SickSummary(name, entries), entries);
        }

        private static Comparison<Entry> ColonyFirst(Comparison<Entry> urgency)
        {
            return (a, b) =>
            {
                int c = ColonyRank(a).CompareTo(ColonyRank(b));
                return c != 0 ? c : urgency(a, b);
            };
        }

        private static int ColonyRank(Entry entry)
        {
            return entry.Group <= MapPawnGroup.Slave ? 0 : (int)entry.Group;
        }

        /// <summary>The most urgent entry regardless of group, which a node's summary describes.</summary>
        private static Entry Worst(List<Entry> entries, Comparison<Entry> urgency)
        {
            Entry worst = entries[0];
            for (int i = 1; i < entries.Count; i++)
            {
                if (urgency(entries[i], worst) < 0)
                {
                    worst = entries[i];
                }
            }
            return worst;
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

        /// <summary>The count, then the soonest bleed-out or the lowest health: the most urgent entry.</summary>
        private static string Summary(string name, List<Entry> entries, Comparison<Entry> urgency, bool bleeding)
        {
            if (entries.Count == 0)
            {
                return "RimWorldAccess.GlobalHealth.Summary.None".Translate(name);
            }
            Entry worst = Worst(entries, urgency);
            PawnHealthSummary first = worst.Health;
            bool one = entries.Count == 1;
            if (!bleeding)
            {
                string health = first.HealthFraction.ToStringPercent();
                return one
                    ? "RimWorldAccess.GlobalHealth.Summary.HealthPawn".Translate(name, worst.Label, health)
                    : "RimWorldAccess.GlobalHealth.Summary.LowestHealth".Translate(name, entries.Count, health);
            }
            if (first.Bleeding == BleedOutKind.Timed)
            {
                string period = first.TicksToBleedOut.ToStringTicksToPeriod();
                return one
                    ? "RimWorldAccess.GlobalHealth.Summary.BleedOutPawn".Translate(name, worst.Label, period)
                    : "RimWorldAccess.GlobalHealth.Summary.BleedOut".Translate(name, entries.Count, period);
            }
            if (first.Bleeding == BleedOutKind.Deathless)
            {
                return one
                    ? "RimWorldAccess.GlobalHealth.Summary.DeathlessPawn".Translate(name, worst.Label)
                    : "RimWorldAccess.GlobalHealth.Summary.Deathless".Translate(name, entries.Count);
            }
            return one
                ? "RimWorldAccess.GlobalHealth.Summary.NotSoonPawn".Translate(name, worst.Label)
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
            var care = new List<string>();
            if (entries.Count == 1)
            {
                if (rescue > 0 || tend > 0)
                {
                    care.Add(rescue > 0 && tend > 0 ? "RimWorldAccess.GlobalHealth.Care.Pawn.RescueAndTend".Translate().ToString()
                        : rescue > 0 ? "RimWorldAccess.GlobalHealth.Care.Pawn.Rescue".Translate().ToString()
                        : "RimWorldAccess.GlobalHealth.Care.Pawn.Tend".Translate().ToString());
                }
                return AppendCare(summary, care, onePawn: true);
            }
            if (rescue > 0)
            {
                care.Add(CareCount(rescue, entries.Count, "RimWorldAccess.GlobalHealth.Care.AllNeedRescue",
                    "RimWorldAccess.GlobalHealth.Care.NeedRescueOne", "RimWorldAccess.GlobalHealth.Care.NeedRescue"));
            }
            if (tend > 0)
            {
                care.Add(CareCount(tend, entries.Count, "RimWorldAccess.GlobalHealth.Care.AllNeedTend",
                    "RimWorldAccess.GlobalHealth.Care.NeedTendOne", "RimWorldAccess.GlobalHealth.Care.NeedTend"));
            }
            return AppendCare(summary, care, onePawn: false);
        }

        private static string CareCount(int count, int total, string allKey, string oneKey, string countKey)
        {
            if (count == total)
            {
                return allKey.Translate().ToString();
            }
            return count == 1 ? oneKey.Translate().ToString() : countKey.Translate(count).ToString();
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
            return AppendCare(summary, care, entries.Count == 1);
        }

        /// <summary>A lone pawn's care reads as part of its summary; a group's starts a sentence of its own.</summary>
        private static string AppendCare(string summary, List<string> care, bool onePawn)
        {
            if (care.Count == 0)
            {
                return summary;
            }
            var builder = new AnnouncementBuilder().Add(summary);
            for (int i = 0; i < care.Count; i++)
            {
                if (i == 0 && !onePawn)
                {
                    builder.Add(care[i]);
                }
                else
                {
                    builder.Add(care[i], Separator.Comma);
                }
            }
            return builder.Build();
        }

        private static string SickSummary(string name, List<Entry> entries)
        {
            if (entries.Count == 0)
            {
                return "RimWorldAccess.GlobalHealth.Summary.None".Translate(name);
            }
            Entry first = Worst(entries, BySickness);
            Disease worst = first.Diseases[0];
            string header = HealthTabHelper.GetHediffHeader(worst.Hediff, first.Pawn);
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

        /// <summary>Life-threatening first, then the narrowest immunity race.</summary>
        private static int CompareDanger(Disease a, Disease b)
        {
            int c = b.Threat.CompareTo(a.Threat);
            return c != 0 ? c : a.Margin.CompareTo(b.Margin);
        }

        private static int ByUrgency(Disease a, Disease b)
        {
            int c = CompareDanger(a, b);
            return c != 0 ? c : a.Order.CompareTo(b.Order);
        }

        private static int BySickness(Entry a, Entry b)
        {
            int c = CompareDanger(a.Diseases[0], b.Diseases[0]);
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
