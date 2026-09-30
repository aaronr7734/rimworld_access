using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// The shared Contents list behind every <see cref="ITab_ContentsBase"/> without a bespoke
    /// adapter (caskets, map portals, and mod storage tabs built on the same base). Mirrors
    /// vanilla's DoItemsLists: a header, one row per held thing (its info card on Alt+I), and each
    /// row's drop and drop-count buttons. Subclasses add what a mod's own tab draws on top.
    /// </summary>
    internal class ContainerContentsAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Container Contents";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        /// <summary>The tab's own button label: several of these tabs can sit on one thing.</summary>
        public override string DisplayName(InspectTabBase tab)
        {
            return tab.labelKey.Translate().CapitalizeFirst();
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (!(categoryItem.SourceTab is ITab_ContentsBase tab) || !(obj is Thing holder))
                return;

            var things = new List<Thing>();
            foreach (Thing thing in tab.container)
            {
                if (thing != null)
                    things.Add(thing);
            }

            InspectNodeFactory.DetailLine(categoryItem, HeaderLabel(tab, holder, things));
            AddTabControls(categoryItem, tab, holder, mode);

            if (things.Count == 0)
            {
                InspectNodeFactory.DetailLine(categoryItem, "NoneBrackets".Translate());
                return;
            }

            foreach (Thing thing in things)
            {
                InspectionTreeItem row = InspectNodeFactory.ItemRow(categoryItem, RowLabel(tab, thing), thing);
                Thing local = thing;
                // The row's info card button, reached with Alt+I.
                row.OnInfo = () => Find.WindowStack.Add(new Dialog_InfoCard(local));
                row.IsExpandable = HasRowControls(tab, thing, mode);
                if (row.IsExpandable)
                    row.OnActivate = () => BuildRowChildren(row, categoryItem, tab, holder, local, mode);
                if (mode == InspectionMode.Full && CanRemove(tab, thing))
                {
                    row.OnDelete = () => RequestDrop(categoryItem, tab, holder, local, local.stackCount, mode);
                }
            }
        }

        protected virtual string HeaderLabel(ITab_ContentsBase tab, Thing holder, List<Thing> things)
        {
            return tab.containedItemsKey.Translate();
        }

        /// <summary>Controls a mod's tab draws above its list; vanilla draws none.</summary>
        protected virtual void AddTabControls(InspectionTreeItem categoryItem, ITab_ContentsBase tab, Thing holder, InspectionMode mode)
        {
        }

        protected virtual string RowLabel(ITab_ContentsBase tab, Thing thing)
        {
            return thing.LabelCap.StripTags();
        }

        protected virtual bool CanRemove(ITab_ContentsBase tab, Thing thing)
        {
            return tab.canRemoveThings;
        }

        protected virtual string DropCountLabel()
        {
            return "RimWorldAccess.Inspection.Contents.DropCount".Translate();
        }

        /// <summary>Row controls a mod's tab draws beside vanilla's buttons.</summary>
        protected virtual void AddRowControls(InspectionTreeItem row, ITab_ContentsBase tab, Thing thing, InspectionMode mode)
        {
        }

        protected virtual bool HasRowControls(ITab_ContentsBase tab, Thing thing, InspectionMode mode)
        {
            return mode == InspectionMode.Full && CanRemove(tab, thing);
        }

        private void BuildRowChildren(InspectionTreeItem row, InspectionTreeItem categoryItem, ITab_ContentsBase tab,
            Thing holder, Thing thing, InspectionMode mode)
        {
            if (row.Children.Count > 0)
                return;

            AddRowControls(row, tab, thing, mode);

            if (mode != InspectionMode.Full || !CanRemove(tab, thing))
                return;

            InspectNodeFactory.ActionRow(row, "DropThing".Translate(), thing,
                () => RequestDrop(categoryItem, tab, holder, thing, thing.stackCount, mode));
            if (thing.stackCount != 1)
            {
                InspectNodeFactory.ActionRow(row, DropCountLabel(), thing, () => Find.WindowStack.Add(
                    new Dialog_Slider("RemoveSliderText".Translate(thing.def.label), 1, thing.stackCount,
                        count => Drop(categoryItem, tab, holder, thing, count, mode, announce: false))));
            }
        }

        /// <summary>Vanilla's DoThingRow drop button: confirms first unless the tab opts out.</summary>
        private void RequestDrop(InspectionTreeItem categoryItem, ITab_ContentsBase tab, Thing holder, Thing thing,
            int count, InspectionMode mode)
        {
            if (!tab.UseDiscardMessage)
            {
                Drop(categoryItem, tab, holder, thing, count, mode);
                return;
            }
            string text = thing is Pawn pawn ? pawn.LabelShortCap : thing.def.label;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("ConfirmRemoveItemDialog".Translate(text),
                () => Drop(categoryItem, tab, holder, thing, count, mode)));
        }

        /// <summary>The slider path stays silent: the slider dialog speaks its own confirmation.</summary>
        private void Drop(InspectionTreeItem categoryItem, ITab_ContentsBase tab, Thing holder, Thing thing,
            int count, InspectionMode mode, bool announce = true)
        {
            string label = GenLabel.ThingLabel(thing, count).CapitalizeFirst();
            if (!ContentsTabDrop.TryDrop(tab, holder, thing, count))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            SoundDefOf.Click.PlayOneShotOnCamera();
            InspectionTreeBuilder.RebuildAdapterCategory(categoryItem, holder, mode, this, tab);
            if (announce)
                TolkHelper.Speak("RimWorldAccess.Inspection.Contents.Dropped".Loc(label));
        }
    }
}
