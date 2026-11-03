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
            return ActionChords.Display("map.openGlobalGear");
        }

        protected override string TitleKey
        {
            get { return "RimWorldAccess.GlobalGear.Title"; }
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

        private readonly List<(Pawn Pawn, MapPawnGroup Group)> pawnsBuffer = new List<(Pawn, MapPawnGroup)>();

        public GlobalGearScope(GlobalGearWindow window) : base(window)
        {
            Tree.TrackLastChild = true;
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

        protected override string FilterChangedOneKey
        {
            get { return "RimWorldAccess.GlobalGear.FilterChangedOne"; }
        }

        protected override string FilterChangedKey
        {
            get { return "RimWorldAccess.GlobalGear.FilterChanged"; }
        }

        protected override Thing RowIcon(InspectionTreeItem item)
        {
            return item.Data is Holder holder ? holder.Thing : null;
        }

        // Rows.

        /// <summary>
        /// Pawns whose gear tab a sighted player could open, in a shown group: spawned, selectable by
        /// a map click (neither fogged nor hidden), and not excluded by ITab_Pawn_Gear.IsVisible.
        /// </summary>
        private List<(Pawn Pawn, MapPawnGroup Group)> ListedPawns(MapPawnGroupFilter shown)
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
                    || (ModsConfig.OdysseyActive && pawn.RaceProps.IsDrone))
                {
                    continue;
                }
                MapPawnGroup group = MapPawnGroups.Of(pawn);
                if (MapPawnGroups.Shown(group, shown))
                {
                    pawnsBuffer.Add((pawn, group));
                }
            }
            return pawnsBuffer;
        }

        /// <summary>What the pawn wields and wears, in the gear tab's Equipment then Apparel order.</summary>
        private static IEnumerable<InteractiveGearHelper.GearItem> GearItems(Pawn pawn)
        {
            foreach (InteractiveGearHelper.GearItem item in InteractiveGearHelper.GetEquipmentItems(pawn))
            {
                if (item.Thing != null)
                {
                    yield return item;
                }
            }
            foreach (InteractiveGearHelper.GearItem item in InteractiveGearHelper.GetApparelItems(pawn))
            {
                if (item.Thing != null)
                {
                    yield return item;
                }
            }
        }

        /// <summary>Everything a row speaks: holders, their names and groups, condition.</summary>
        protected override int ContentSignature(MapPawnGroupFilter shown)
        {
            int hash = (int)shown;
            foreach ((Pawn pawn, MapPawnGroup group) in ListedPawns(shown))
            {
                unchecked
                {
                    hash = hash * 31 + pawn.thingIDNumber;
                    hash = hash * 31 + (int)group;
                    hash = hash * 31 + pawn.LabelShort.GetHashCode();
                    foreach (InteractiveGearHelper.GearItem item in GearItems(pawn))
                    {
                        hash = hash * 31 + item.Thing.thingIDNumber;
                        hash = hash * 31 + (ItemLabelHelper.ConditionPercent(item.Thing)?.GetHashCode() ?? 0);
                    }
                }
            }
            return hash;
        }

        private sealed class ItemGroup
        {
            public object Key;
            public string Name;
            public int Rank;
            public int ListOrder;
            public readonly List<Holder> Holders = new List<Holder>();
        }

        /// <summary>
        /// One row per kind of item, grouped by def, stuff, quality and vanilla's label without hit
        /// points, so a tainted piece stays apart from a clean one. Holders sit under it.
        /// </summary>
        protected override InspectionTreeItem BuildRoot(MapPawnGroupFilter shown, out int listed)
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
            foreach ((Pawn pawn, MapPawnGroup group) in ListedPawns(shown))
            {
                foreach (InteractiveGearHelper.GearItem item in GearItems(pawn))
                {
                    Thing thing = item.Thing;
                    QualityCategory? quality = thing.TryGetQuality(out QualityCategory q) ? q : (QualityCategory?)null;
                    string name = thing.GetCustomLabelNoCount(includeHp: false).CapitalizeFirst(thing.def).StripTags();
                    var key = (thing.def, thing.Stuff, quality, name);
                    if (!groups.TryGetValue(key, out ItemGroup itemGroup))
                    {
                        itemGroup = new ItemGroup
                        {
                            Key = key,
                            Name = name,
                            Rank = item.Category == "Apparel" ? 1 : 0,
                            ListOrder = InteractiveGearHelper.ApparelListOrder(thing),
                        };
                        groups[key] = itemGroup;
                    }
                    itemGroup.Holders.Add(new Holder(pawn, thing, group));
                    holderPawns.Add(pawn);
                }
            }
            listed = holderPawns.Count;

            var ordered = new List<ItemGroup>(groups.Values);
            ordered.Sort((x, y) =>
            {
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
            foreach (ItemGroup itemGroup in ordered)
            {
                InspectNodeFactory.Attach(root, ItemNode(itemGroup));
            }
            return root;
        }

        private static InspectionTreeItem ItemNode(ItemGroup itemGroup)
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
                Data = itemGroup.Key,
                LinkedDef = first.def,
                IndentLevel = 0,
                IsExpandable = true,
            };
            foreach (Holder holder in holders)
            {
                Thing held = holder.Thing;
                InspectNodeFactory.Attach(itemNode, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = MapPawnGroups.NameWithGroup(holder.Pawn.LabelShort, holder.Group),
                    Detail = HolderDetail(held),
                    Data = holder,
                    OnInfo = delegate { Find.WindowStack.Add(new Dialog_InfoCard(held)); },
                    IndentLevel = 1,
                });
            }
            return itemNode;
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

        private void OpenGearTab(Holder holder)
        {
            HandOffToTab(holder.Pawn, typeof(ITab_Pawn_Gear), holder.Thing, "RimWorldAccess.GlobalGear.JumpUnavailable", GlobalGearWindow.Open);
        }
    }
}
