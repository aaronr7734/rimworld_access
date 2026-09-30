using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Adaptive Storage Framework's Contents tab (and its Group subclass): the shared contents
    /// list plus what the mod's DoItemsLists and DoThingRow draw on top of vanilla's, namely the
    /// stack and mass header, the slot-limit slider, per-row mass and days until rot, and the
    /// allow checkbox.
    /// </summary>
    internal sealed class AdaptiveStorageContentsAdapter : ContainerContentsAdapter
    {
        private const float RotShownBelowTicks = GenDate.TicksPerYear * 10;

        private readonly MethodInfo slotLimit;
        private readonly MethodInfo displaySlider;
        private readonly MethodInfo canRemoveThing;
        private readonly MethodInfo stacks;
        private readonly MethodInfo storingAdaptiveStorage;
        private readonly PropertyInfo selectedStorages;
        private readonly PropertyInfo currentSlotLimit;
        private readonly PropertyInfo totalSlots;
        private readonly PropertyInfo allowItemForbiddingAccess;
        private readonly bool ready;

        public AdaptiveStorageContentsAdapter(Type tabType)
        {
            var surface = new ReflectionSurface("AdaptiveStorageContentsAdapter");
            surface.Supplied("AdaptiveStorage.ContentsITab", tabType);
            Type thingClass = surface.Type("AdaptiveStorage.ThingClass");
            Type strings = surface.Type("AdaptiveStorage.Strings");
            Type thingExtensions = surface.Type("AdaptiveStorage.Utility.ThingExtensions");
            slotLimit = surface.Method(tabType, "get_SlotLimit");
            displaySlider = surface.Method(tabType, "get_DisplaySlider");
            canRemoveThing = surface.Method(tabType, "CanRemoveThing", new[] { typeof(Thing) });
            stacks = surface.Method(strings, "Stacks", new[] { typeof(int), typeof(int) });
            storingAdaptiveStorage = surface.Method(thingExtensions, "StoringAdaptiveStorage", new[] { typeof(Thing) });
            selectedStorages = surface.Property(tabType, "SelectedStorages");
            currentSlotLimit = surface.Property(thingClass, "CurrentSlotLimit");
            totalSlots = surface.Property(thingClass, "TotalSlots");
            allowItemForbiddingAccess = surface.Property(thingClass, "AllowItemForbiddingAccess");
            ready = surface.Ready;
        }

        public override bool Ready => ready;

        public override string CategoryKey => "Adaptive Storage Contents";

        protected override string HeaderLabel(ITab_ContentsBase tab, Thing holder, List<Thing> things)
        {
            float mass = 0f;
            foreach (Thing thing in things)
                mass += thing.GetStatValue(StatDefOf.Mass) * thing.stackCount;
            string stackText = (string)stacks.Invoke(null, new object[] { things.Count, (int)slotLimit.Invoke(tab, null) });
            return $"{"ContainedItems".Translate()} ({stackText}, {mass.ToStringMass()})";
        }

        protected override void AddTabControls(InspectionTreeItem categoryItem, ITab_ContentsBase tab, Thing holder, InspectionMode mode)
        {
            if (!(bool)displaySlider.Invoke(tab, null))
                return;
            var storages = (List<object>)selectedStorages.GetValue(tab, null);
            object storage = storages[0];

            var row = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = "RimWorldAccess.Compat.AdaptiveStorage.SlotLimit".Translate(),
                Data = holder,
                IndentLevel = categoryItem.IndentLevel + 1,
                DescribeElement = () => DescribeSlotLimit(storage, mode),
            };
            if (mode == InspectionMode.Full)
            {
                row.OnAdjust = direction =>
                {
                    AdjustSlotLimit(categoryItem, tab, holder, storage, direction);
                    return true;
                };
            }
            InspectNodeFactory.Attach(categoryItem, row);
        }

        private ElementDescription DescribeSlotLimit(object storage, InspectionMode mode)
        {
            int total = (int)totalSlots.GetValue(storage, null);
            int current = Math.Min((int)currentSlotLimit.GetValue(storage, null), total);
            return new ElementDescription
            {
                Label = "RimWorldAccess.Compat.AdaptiveStorage.SlotLimit".Translate(),
                Role = ElementRole.Slider,
                Value = current.ToString(),
                AtMinimum = current == 0,
                AtMaximum = current == total,
                ReadOnly = mode != InspectionMode.Full,
            };
        }

        private void AdjustSlotLimit(InspectionTreeItem categoryItem, ITab_ContentsBase tab, Thing holder, object storage, int direction)
        {
            int total = (int)totalSlots.GetValue(storage, null);
            int current = Math.Min((int)currentSlotLimit.GetValue(storage, null), total);
            int next = Mathf.Clamp(current + direction, 0, total);
            if (next == current)
            {
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            // MUTATION-C: mirrors ContentsITab.TryDrawSlider, which writes the result straight into ThingClass.CurrentSlotLimit; the slider is IMGUI-only with no method to call.
            currentSlotLimit.SetValue(storage, next, null);
            // The header's stack count reads the new limit.
            categoryItem.Children[0].Label = HeaderLabel(tab, holder, new List<Thing>(NonNull(tab.container)));
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(new ElementDescription
            {
                Role = ElementRole.Slider,
                Value = next.ToString(),
                AtMinimum = next == 0,
                AtMaximum = next == total,
            }, TranslatedShellVocabulary.Instance));
        }

        private static IEnumerable<Thing> NonNull(IList<Thing> things)
        {
            foreach (Thing thing in things)
            {
                if (thing != null)
                    yield return thing;
            }
        }

        protected override string RowLabel(ITab_ContentsBase tab, Thing thing)
        {
            string label = base.RowLabel(tab, thing)
                + ", " + (thing.GetStatValue(StatDefOf.Mass) * thing.stackCount).ToStringMass();
            CompRottable rottable = thing.TryGetComp<CompRottable>();
            if (rottable != null)
            {
                int ticks = rottable.TicksUntilRotAtCurrentTemp;
                if (ticks < RotShownBelowTicks)
                {
                    label += ", " + "RimWorldAccess.Compat.AdaptiveStorage.DaysUntilRot".Translate(
                        (ticks / (float)GenDate.TicksPerDay).ToString("0.#"));
                }
            }
            return label;
        }

        protected override bool CanRemove(ITab_ContentsBase tab, Thing thing)
        {
            return (bool)canRemoveThing.Invoke(tab, new object[] { thing });
        }

        protected override string DropCountLabel()
        {
            return CompatText.ModText("ASF_DropSpecificCount");
        }

        /// <summary>The allow checkbox is drawn on every row, disabled where it cannot change.</summary>
        protected override bool HasRowControls(ITab_ContentsBase tab, Thing thing, InspectionMode mode)
        {
            return true;
        }

        protected override void AddRowControls(InspectionTreeItem row, ITab_ContentsBase tab, Thing thing, InspectionMode mode)
        {
            CompForbiddable forbiddable = (thing as ThingWithComps)?.GetComp<CompForbiddable>();
            object storing = storingAdaptiveStorage.Invoke(null, new object[] { thing });
            bool locked = forbiddable == null
                || (storing != null && !(bool)allowItemForbiddingAccess.GetValue(storing, null));

            var allow = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Action,
                Label = "CommandAllow".Translate(),
                Data = thing,
                IndentLevel = row.IndentLevel + 1,
                DescribeElement = () => DescribeAllow(forbiddable, locked || mode != InspectionMode.Full),
            };
            if (!locked && mode == InspectionMode.Full)
            {
                // MUTATION vehicle B: the self-gating CompForbiddable.Forbidden setter the mod's checkbox writes.
                allow.OnActivate = () =>
                {
                    forbiddable.Forbidden = !forbiddable.Forbidden;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                        DescribeAllow(forbiddable, false), TranslatedShellVocabulary.Instance));
                };
                allow.OpensOverlayMenu = true;
            }
            InspectNodeFactory.Attach(row, allow);
        }

        private static ElementDescription DescribeAllow(CompForbiddable forbiddable, bool readOnly)
        {
            bool allowed = forbiddable == null || !forbiddable.Forbidden;
            return new ElementDescription
            {
                Label = "CommandAllow".Translate(),
                Role = ElementRole.Checkbox,
                Check = allowed ? CheckState.Checked : CheckState.Unchecked,
                ReadOnly = readOnly,
                Extras = forbiddable == null ? null
                    : allowed ? "CommandNotForbiddenDesc".Translate() : "CommandForbiddenDesc".Translate(),
            };
        }
    }
}
