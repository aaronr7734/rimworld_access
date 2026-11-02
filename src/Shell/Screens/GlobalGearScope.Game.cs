using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Everything a visible pawn on the current map wields or wears, one row per kind of item with
    /// its holders under it. Carried inventory is left to the inventory screens.
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
    /// </summary>
    public sealed class GlobalGearScope : MapOverviewScope
    {
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

        protected override string ComposeOpenAnnouncement()
        {
            return "RimWorldAccess.GlobalGear.Title".Translate();
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

        /// <summary>What the pawn wields and wears, in the gear tab's Equipment then Apparel order.</summary>
        private static IEnumerable<Thing> GearThings(Pawn pawn)
        {
            foreach (InteractiveGearHelper.GearItem item in InteractiveGearHelper.GetEquipmentItems(pawn))
            {
                if (item.Thing != null)
                {
                    yield return item.Thing;
                }
            }
            foreach (InteractiveGearHelper.GearItem item in InteractiveGearHelper.GetApparelItems(pawn))
            {
                if (item.Thing != null)
                {
                    yield return item.Thing;
                }
            }
        }

        /// <summary>Everything a row speaks: holders, their names and groups, condition.</summary>
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
                    hash = hash * 31 + thing.HitPoints;
                }
            }
            return hash;
        }

        private sealed class ItemGroup
        {
            public string Name;
            public int Rank;
            public int ListOrder;
            public readonly List<Holder> Holders = new List<Holder>();
        }

        /// <summary>
        /// One row per kind of item, grouped by def, stuff, quality and vanilla's label without hit
        /// points, so a tainted piece stays apart from a clean one. Holders sit under it.
        /// </summary>
        protected override InspectionTreeItem BuildRoot(MapPawnGroupFilter shown)
        {
            var root = new InspectionTreeItem
            {
                Label = "RimWorldAccess.GlobalGear.Title".Translate(),
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false,
            };
            var groups = new Dictionary<(ThingDef, ThingDef, QualityCategory?, string), ItemGroup>();
            var holderPawns = new HashSet<Pawn>();
            foreach (Pawn pawn in ListedPawns(shown))
            {
                MapPawnGroup group = MapPawnGroups.Of(pawn);
                foreach (Thing thing in GearThings(pawn))
                {
                    QualityCategory? quality = thing.TryGetQuality(out QualityCategory q) ? q : (QualityCategory?)null;
                    string name = thing.GetCustomLabelNoCount(includeHp: false).CapitalizeFirst(thing.def).StripTags();
                    var key = (thing.def, thing.Stuff, quality, name);
                    if (!groups.TryGetValue(key, out ItemGroup itemGroup))
                    {
                        itemGroup = new ItemGroup { Name = name, Rank = SortRank(thing), ListOrder = ApparelListOrder(thing) };
                        groups[key] = itemGroup;
                    }
                    itemGroup.Holders.Add(new Holder(pawn, thing, group));
                    holderPawns.Add(pawn);
                }
            }
            listedCount = holderPawns.Count;

            var ordered = new List<KeyValuePair<(ThingDef, ThingDef, QualityCategory?, string), ItemGroup>>(groups);
            ordered.Sort((a, b) =>
            {
                ItemGroup x = a.Value, y = b.Value;
                if (x.Rank != y.Rank)
                {
                    return x.Rank.CompareTo(y.Rank);
                }
                if (x.ListOrder != y.ListOrder)
                {
                    return y.ListOrder.CompareTo(x.ListOrder);
                }
                return string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            foreach (KeyValuePair<(ThingDef, ThingDef, QualityCategory?, string), ItemGroup> entry in ordered)
            {
                InspectNodeFactory.Attach(root, ItemNode(entry.Key, entry.Value));
            }
            return root;
        }

        private static InspectionTreeItem ItemNode(object key, ItemGroup itemGroup)
        {
            List<Holder> holders = itemGroup.Holders;
            holders.Sort((a, b) => a.Group != b.Group
                ? a.Group.CompareTo(b.Group)
                : string.Compare(a.Pawn.LabelShort, b.Pawn.LabelShort, StringComparison.CurrentCultureIgnoreCase));
            Thing first = holders[0].Thing;
            string name = holders.Count == 1 ? ItemLabelHelper.LabelWithCondition(first).StripTags() : itemGroup.Name;
            var names = new List<string>(holders.Count);
            foreach (Holder holder in holders)
            {
                names.Add(holder.Pawn.LabelShort);
            }
            var itemNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = (first is Apparel ? "RimWorldAccess.GlobalGear.WornBy" : "RimWorldAccess.GlobalGear.EquippedBy")
                    .Translate(name, names.ToCommaList()),
                Data = key,
                LinkedDef = first.def,
                IndentLevel = 0,
                IsExpandable = true,
            };
            foreach (Holder holder in holders)
            {
                InspectNodeFactory.Attach(itemNode, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = MapPawnGroups.NameWithGroup(holder.Pawn.LabelShort, holder.Group),
                    Detail = HolderDetail(holder.Thing),
                    Data = holder,
                    IndentLevel = 1,
                });
            }
            return itemNode;
        }

        /// <summary>The gear tab's section: weapons and belts under Equipment, the rest under Apparel.</summary>
        private static int SortRank(Thing thing)
        {
            return thing is Apparel apparel && !apparel.def.apparel.layers.Contains(ApparelLayerDefOf.Belt) ? 1 : 0;
        }

        /// <summary>ITab_Pawn_Gear's apparel order key, sorted descending.</summary>
        private static int ApparelListOrder(Thing thing)
        {
            List<BodyPartGroupDef> parts = thing.def.apparel?.bodyPartGroups;
            return parts != null && parts.Count > 0 ? parts[0].listOrder : 0;
        }

        private static string HolderDetail(Thing thing)
        {
            string condition = ItemLabelHelper.ConditionPercent(thing);
            return condition != null ? (string)"RimWorldAccess.GlobalGear.Condition".Translate(condition) : null;
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
            bool landed = WindowlessInspectionState.OpenTabLandingOn(typeof(ITab_Pawn_Gear), holder.Thing);
            if (!landed)
            {
                MapNavigationState.SpeakJumpedTo(pawn.LabelShort);
            }
            ArmReturn(landed, GlobalGearWindow.Open);
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
