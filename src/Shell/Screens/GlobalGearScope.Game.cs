using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Every piece of gear held by a visible pawn on the current map, item first: the gear tab's
    /// Equipment, Apparel and Inventory sections, each item under them, and each holder under the
    /// item.
    /// </summary>
    public sealed class GlobalGearWindow : MapOverviewWindow
    {
        internal static void Open()
        {
            if (!Find.WindowStack.IsOpen<GlobalGearWindow>())
            {
                Find.WindowStack.Add(new GlobalGearWindow());
            }
        }

        internal static string OpenerHotkey()
        {
            return ActionRegistry.Catalog.TryGet("map.openGlobalGear", out InputAction action) && action.Bindings.Count > 0
                ? action.Bindings[0].DisplayLabel
                : null;
        }

        protected override MapOverviewScope Scope
        {
            get { return GlobalGearScope.Current; }
        }

        protected override string Title
        {
            get { return "RimWorldAccess.GlobalGear.Title".Translate(); }
        }

        protected override Thing RowIcon(InspectionTreeItem item)
        {
            return item.Data is GlobalGearScope.Holder holder ? holder.Thing : null;
        }
    }

    /// <summary>
    /// Keyboard scope for <see cref="GlobalGearWindow"/>. Read only: Enter on a holder, or on an
    /// item with a single holder, closes the window and opens that pawn's gear tab on the item.
    /// The three sections always exist, each labelled with what it holds.
    /// </summary>
    public sealed class GlobalGearScope : MapOverviewScope
    {
        internal enum Section
        {
            Equipment,
            Apparel,
            Inventory,
        }

        private static readonly Section[] Sections = { Section.Equipment, Section.Apparel, Section.Inventory };

        /// <summary>One holder row's datum; equal by the held thing, so a rebuild finds it again.</summary>
        internal sealed class Holder
        {
            public readonly Pawn Pawn;
            public readonly Thing Thing;
            public readonly MapPawnGroup Group;

            public Holder(Pawn pawn, Thing thing, MapPawnGroup group)
            {
                Pawn = pawn;
                Thing = thing;
                Group = group;
            }

            public override bool Equals(object obj)
            {
                return obj is Holder other && ReferenceEquals(other.Thing, Thing);
            }

            public override int GetHashCode()
            {
                return Thing.GetHashCode();
            }
        }

        internal static GlobalGearScope Current { get; private set; }

        private readonly GlobalGearWindow window;
        private readonly List<Pawn> pawnsBuffer = new List<Pawn>();
        private int listedCount;
        private bool announcedOpen;

        public GlobalGearScope(GlobalGearWindow window) : base(window)
        {
            this.window = window;
            Tree.TrackLastChild = true;
            Claim(SharedMenuGrammar.Info, e => PerformInfoCard());
        }

        public override string Name
        {
            get { return "global-gear"; }
        }

        protected override string TreeRegionLabel
        {
            get { return "RimWorldAccess.GlobalGear.Title".Translate(); }
        }

        protected override MapPawnGroupFilter ShownGroups
        {
            get
            {
                RimWorldAccessSettings s = RimWorldAccessMod_Settings.Settings;
                return s != null ? s.GlobalGearGroups : MapPawnGroupFilter.All;
            }
            set
            {
                RimWorldAccessSettings s = RimWorldAccessMod_Settings.Settings;
                if (s != null)
                {
                    s.GlobalGearGroups = value;
                }
            }
        }

        public override void OnPush()
        {
            Current = this;
            base.OnPush();
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
            if (!announcedOpen)
            {
                announcedOpen = true;
                SoundDefOf.TabOpen.PlayOneShotOnCamera();
            }
            AnnounceCurrentItem();
        }

        protected override string FilterChangedAnnouncement(string state)
        {
            return listedCount == 1
                ? (string)"RimWorldAccess.GlobalGear.FilterChangedOne".Translate(state)
                : (string)"RimWorldAccess.GlobalGear.FilterChanged".Translate(state, listedCount);
        }

        // Rows.

        /// <summary>
        /// Pawns whose gear tab a sighted player could open, in a shown group: spawned, selectable by
        /// a map click (neither fogged nor hidden), and not excluded by ITab_Pawn_Gear.IsVisible.
        /// </summary>
        private List<Pawn> ListedPawns(MapPawnGroupFilter shown)
        {
            pawnsBuffer.Clear();
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return pawnsBuffer;
            }
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!ThingSelectionUtility.SelectableByMapClick(pawn)
                    || pawn.ageTracker.CurLifeStage == LifeStageDefOf.HumanlikeBaby
                    || (ModsConfig.AnomalyActive && pawn.RaceProps.IsAnomalyEntity)
                    || (ModsConfig.OdysseyActive && pawn.RaceProps.IsDrone)
                    || !MapPawnGroups.Shown(MapPawnGroups.Of(pawn), shown))
                {
                    continue;
                }
                pawnsBuffer.Add(pawn);
            }
            return pawnsBuffer;
        }

        private static List<Thing> SectionThings(Pawn pawn, Section section)
        {
            List<InteractiveGearHelper.GearItem> items;
            switch (section)
            {
                case Section.Equipment:
                    items = InteractiveGearHelper.GetEquipmentItems(pawn);
                    break;
                case Section.Apparel:
                    items = InteractiveGearHelper.GetApparelItems(pawn);
                    break;
                default:
                    if (CarriesTradeStock(pawn))
                    {
                        return new List<Thing>();
                    }
                    items = InteractiveGearHelper.GetInventoryItems(pawn);
                    break;
            }
            var things = new List<Thing>(items.Count);
            foreach (InteractiveGearHelper.GearItem item in items)
            {
                if (item.Thing != null)
                {
                    things.Add(item.Thing);
                }
            }
            return things;
        }

        /// <summary>A visiting trader's or pack animal's inventory is the caravan's stock, not gear.</summary>
        private static bool CarriesTradeStock(Pawn pawn)
        {
            if (pawn.Faction == Faction.OfPlayer)
            {
                return false;
            }
            TraderCaravanRole role = pawn.GetTraderCaravanRole();
            return role == TraderCaravanRole.Trader || role == TraderCaravanRole.Carrier;
        }

        /// <summary>Everything a row speaks: holders, their names and groups, stacks, condition.</summary>
        protected override int ContentSignature(MapPawnGroupFilter shown)
        {
            int hash = (int)shown;
            List<Pawn> pawns = ListedPawns(shown);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                unchecked
                {
                    hash = hash * 31 + pawn.thingIDNumber;
                    hash = hash * 31 + (int)MapPawnGroups.Of(pawn);
                    hash = hash * 31 + pawn.LabelShort.GetHashCode();
                }
                hash = AddGear(hash, pawn.equipment?.AllEquipmentListForReading);
                hash = AddGear(hash, pawn.apparel?.WornApparel);
                hash = AddGear(hash, pawn.inventory?.innerContainer);
            }
            return hash;
        }

        private static int AddGear<T>(int hash, IList<T> things) where T : Thing
        {
            if (things == null)
            {
                return hash;
            }
            unchecked
            {
                for (int i = 0; i < things.Count; i++)
                {
                    T thing = things[i];
                    hash = hash * 31 + thing.thingIDNumber;
                    hash = hash * 31 + thing.stackCount;
                    hash = hash * 31 + thing.HitPoints;
                }
            }
            return hash;
        }

        protected override InspectionTreeItem BuildRoot(MapPawnGroupFilter shown)
        {
            var root = new InspectionTreeItem
            {
                Label = "RimWorldAccess.GlobalGear.Title".Translate(),
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false,
            };
            List<Pawn> pawns = ListedPawns(shown);
            var holderPawns = new HashSet<Pawn>();
            var allHolders = new HashSet<Pawn>();
            foreach (Section section in Sections)
            {
                holderPawns.Clear();
                AttachSection(root, section, pawns, holderPawns);
                allHolders.UnionWith(holderPawns);
            }
            listedCount = allHolders.Count;
            return root;
        }

        private static void AttachSection(InspectionTreeItem root, Section section, List<Pawn> pawns, HashSet<Pawn> holderPawns)
        {
            var groups = new Dictionary<(ThingDef, ThingDef, QualityCategory?, string), InspectionTreeItem>();
            var counts = new Dictionary<InspectionTreeItem, int>();
            var holders = new Dictionary<InspectionTreeItem, List<Holder>>();
            foreach (Pawn pawn in pawns)
            {
                MapPawnGroup group = MapPawnGroups.Of(pawn);
                foreach (Thing thing in SectionThings(pawn, section))
                {
                    QualityCategory? quality = thing.TryGetQuality(out QualityCategory q) ? q : (QualityCategory?)null;
                    string name = thing.GetCustomLabelNoCount(includeHp: false).CapitalizeFirst(thing.def).StripTags();
                    var key = (thing.def, thing.Stuff, quality, name);
                    if (!groups.TryGetValue(key, out InspectionTreeItem itemNode))
                    {
                        itemNode = new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.Item,
                            ExpandedLabel = name,
                            Data = key,
                            LinkedDef = thing.def,
                            IndentLevel = 1,
                            IsExpandable = true,
                        };
                        groups[key] = itemNode;
                        counts[itemNode] = 0;
                        holders[itemNode] = new List<Holder>();
                    }
                    counts[itemNode] += thing.stackCount;
                    holders[itemNode].Add(new Holder(pawn, thing, group));
                    holderPawns.Add(pawn);
                }
            }

            var itemNodes = new List<InspectionTreeItem>(groups.Values);
            itemNodes.Sort((a, b) => string.Compare(a.ExpandedLabel, b.ExpandedLabel, StringComparison.CurrentCultureIgnoreCase));
            int total = 0;
            string sectionName = SectionLabel(section);
            var sectionNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                ExpandedLabel = sectionName,
                Data = section,
                IndentLevel = 0,
                IsExpandable = itemNodes.Count > 0,
            };
            foreach (InspectionTreeItem itemNode in itemNodes)
            {
                total += counts[itemNode];
                itemNode.Label = "RimWorldAccess.GlobalGear.Item".Translate(itemNode.ExpandedLabel, counts[itemNode]);
                List<Holder> list = holders[itemNode];
                list.Sort((a, b) => a.Group != b.Group
                    ? a.Group.CompareTo(b.Group)
                    : string.Compare(a.Pawn.LabelShort, b.Pawn.LabelShort, StringComparison.CurrentCultureIgnoreCase));
                foreach (Holder holder in list)
                {
                    InspectNodeFactory.Attach(itemNode, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = MapPawnGroups.NameWithGroup(holder.Pawn.LabelShort, holder.Group),
                        Detail = HolderDetail(holder.Thing),
                        Data = holder,
                        IndentLevel = 2,
                    });
                }
                InspectNodeFactory.Attach(sectionNode, itemNode);
            }
            sectionNode.Label = SectionSummary(sectionName, total, holderPawns.Count);
            InspectNodeFactory.Attach(root, sectionNode);
        }

        /// <summary>The gear tab's own section headings.</summary>
        private static string SectionLabel(Section section)
        {
            switch (section)
            {
                case Section.Equipment:
                    return "Equipment".Translate();
                case Section.Apparel:
                    return "Apparel".Translate();
                default:
                    return "Inventory".Translate();
            }
        }

        private static string SectionSummary(string name, int items, int holders)
        {
            if (items == 0)
            {
                return "RimWorldAccess.GlobalGear.Summary.None".Translate(name);
            }
            if (items == 1)
            {
                return "RimWorldAccess.GlobalGear.Summary.OneItem".Translate(name);
            }
            return holders == 1
                ? "RimWorldAccess.GlobalGear.Summary.OneHolder".Translate(name, items)
                : "RimWorldAccess.GlobalGear.Summary.Items".Translate(name, items, holders);
        }

        /// <summary>
        /// The stack count, then the condition under GenLabel.LabelExtras' own rule for showing it.
        /// </summary>
        private static string HolderDetail(Thing thing)
        {
            var parts = new List<string>(2);
            if (thing.stackCount > 1)
            {
                parts.Add("RimWorldAccess.GlobalGear.StackCount".Translate(thing.stackCount));
            }
            if (thing.def.useHitPoints && thing.HitPoints < thing.MaxHitPoints && thing.def.stackLimit == 1)
            {
                parts.Add("RimWorldAccess.GlobalGear.Condition".Translate(((float)thing.HitPoints / thing.MaxHitPoints).ToStringPercent()));
            }
            return parts.Count > 0 ? string.Join(". ", parts) : null;
        }

        // Content.

        protected override bool IsActionRow(InspectionTreeItem item)
        {
            return item.Data is Holder;
        }

        protected override ElementRole ActionRowRole
        {
            get { return ElementRole.Button; }
        }

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            if (item.Data is Holder holder)
            {
                OpenGearTab(holder);
                return;
            }
            if (item.Children.Count == 1 && item.Children[0].Data is Holder only)
            {
                OpenGearTab(only);
                return;
            }
            if (item.IsExpandable)
            {
                PerformActivateExpandToggle(item);
                return;
            }
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// The log tab's jump (<see cref="CameraJumper.TryJumpAndSelect"/>), then vanilla's
        /// <see cref="InspectPaneUtility.OpenTab"/> as a death letter does. The window closes first:
        /// the tab bridge stands down under a foreign dialog.
        /// </summary>
        private void OpenGearTab(Holder holder)
        {
            Pawn pawn = holder.Pawn;
            if (pawn.Destroyed || !pawn.Spawned || !CameraJumper.CanJump(pawn))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData("RimWorldAccess.GlobalGear.JumpUnavailable".Translate());
                return;
            }
            window.Close();
            CameraJumper.TryJumpAndSelect(pawn);
            if (!WindowlessInspectionState.OpenTabLandingOn(typeof(ITab_Pawn_Gear), holder.Thing))
            {
                MapNavigationState.SpeakJumpedTo(pawn.LabelShort);
            }
        }

        /// <summary>Alt+I: the held thing's own card on a holder row, so condition and quality are its own.</summary>
        private void PerformInfoCard()
        {
            InspectionTreeItem item = CurrentTreeItem();
            if (item?.Data is Holder holder)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(holder.Thing));
                return;
            }
            if (item?.LinkedDef != null)
            {
                InfoCardState.OpenInfoCardForDef(item.LinkedDef);
                return;
            }
            InfoCardState.SpeakNoInfoCardAvailable();
        }
    }
}
