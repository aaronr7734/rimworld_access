using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// <see cref="ITransferLoadDialog"/> over the outpost framework's two item dialogs,
    /// <c>Outposts.Dialog_GiveItems</c> (caravan inventory into the outpost) and
    /// <c>Outposts.Dialog_TakeItems</c> (outpost stock into the caravan). Both are one untitled
    /// items table with no tab bar, no mass readout and an inline-IMGUI accept branch, so they
    /// present on <see cref="TransportPodLoadingScope"/> as an items-only screen.
    /// </summary>
    internal sealed class VefOutpostItemsAdapter : ITransferLoadDialog
    {
        private static readonly Type giveDialogType;
        private static readonly Type takeDialogType;
        private static readonly FieldInfo giveCaravanField;
        private static readonly FieldInfo giveOutpostField;
        private static readonly FieldInfo giveTransferablesField;
        private static readonly FieldInfo giveWidgetField;
        private static readonly FieldInfo takeCaravanField;
        private static readonly FieldInfo takeOutpostField;
        private static readonly FieldInfo takeTransferablesField;
        private static readonly FieldInfo takeWidgetField;
        private static readonly MethodInfo addItemMethod;
        private static readonly MethodInfo takeItemMethod;
        private static readonly bool ready;

        public static Type GiveDialogType => giveDialogType;
        public static Type TakeDialogType => takeDialogType;
        public static bool Ready => ready;

        static VefOutpostItemsAdapter()
        {
            var surface = new ReflectionSurface("VefOutpostItemsAdapter");
            Type outpostType = surface.Type("Outposts.Outpost");
            giveDialogType = surface.Type("Outposts.Dialog_GiveItems");
            takeDialogType = surface.Type("Outposts.Dialog_TakeItems");
            giveCaravanField = surface.Field(giveDialogType, "caravan");
            giveOutpostField = surface.Field(giveDialogType, "outpost");
            giveTransferablesField = surface.Field(giveDialogType, "transferables");
            giveWidgetField = surface.Field(giveDialogType, "itemsTransfer");
            takeCaravanField = surface.Field(takeDialogType, "caravan");
            takeOutpostField = surface.Field(takeDialogType, "outpost");
            takeTransferablesField = surface.Field(takeDialogType, "transferables");
            takeWidgetField = surface.Field(takeDialogType, "itemsTransfer");
            addItemMethod = surface.Method(outpostType, "AddItem", new[] { typeof(Thing) });
            takeItemMethod = surface.Method(outpostType, "TakeItem", new[] { typeof(Thing) });
            ready = surface.Ready;
        }

        /// <summary>Applied from the module so an absent framework costs no Window.PostClose postfix.</summary>
        public static void PatchClose(Harmony harmony)
        {
            if (!ready)
                return;
            harmony.Patch(AccessTools.Method(typeof(Window), "PostClose"),
                postfix: new HarmonyMethod(typeof(VefOutpostItemsAdapter), nameof(PostClosePostfix)));
        }

        /// <summary>The vanilla transfer dialogs' close pattern: reset the accept latch, and speak a cancel the accept path did not already answer.</summary>
        private static void PostClosePostfix(Window __instance)
        {
            if (!giveDialogType.IsInstanceOfType(__instance) && !takeDialogType.IsInstanceOfType(__instance))
                return;
            bool wasAccepted = TransportPodLoadingState.AcceptAttempted;
            TransportPodLoadingState.Close();
            if (!wasAccepted)
                TolkHelper.Speak("RimWorldAccess.Compat.Vef.OutpostItemsCancelled".Loc(), SpeechPriority.Normal);
        }

        private readonly Window window;
        private readonly bool give;

        public VefOutpostItemsAdapter(Window window)
        {
            this.window = window;
            give = giveDialogType.IsInstanceOfType(window);
        }

        private Caravan Caravan => (give ? giveCaravanField : takeCaravanField).GetValue(window) as Caravan;

        private WorldObject Outpost => (give ? giveOutpostField : takeOutpostField).GetValue(window) as WorldObject;

        public string OpenAnnouncement => give
            ? "RimWorldAccess.Compat.Vef.OutpostGiveOpen".Translate(Outpost?.LabelCap ?? "", Caravan?.LabelCap ?? "")
            : "RimWorldAccess.Compat.Vef.OutpostTakeOpen".Translate(Outpost?.LabelCap ?? "", Caravan?.LabelCap ?? "");

        public string CancelAnnouncement => "RimWorldAccess.Compat.Vef.OutpostItemsCancelled".Translate();

        public List<TransferableOneWay> GetAllTransferables()
        {
            return (give ? giveTransferablesField : takeTransferablesField).GetValue(window) as List<TransferableOneWay>
                ?? new List<TransferableOneWay>();
        }

        // A single items table: the scope's Items region is tab 1.
        public int GameTab
        {
            get => 1;
            set { }
        }

        // Neither dialog draws or enforces a mass limit.
        public float MassCapacity => float.MaxValue;

        // The widget recomputes from the transferables every frame; the dialogs cache nothing.
        public void NotifyTransferablesChanged()
        {
        }

        /// <summary>
        /// MUTATION-C: mirrors Outposts.Dialog_GiveItems/Dialog_TakeItems.DoBottomButtons accept branch;
        /// the body is inline IMGUI with no invocable method. Moves each counted stack between the
        /// caravan and the outpost through the outpost's own AddItem/TakeItem, then closes as the
        /// button does.
        /// </summary>
        public void TriggerAccept()
        {
            Caravan caravan = Caravan;
            WorldObject outpost = Outpost;
            if (caravan == null || outpost == null)
                return;

            foreach (TransferableOneWay transferable in GetAllTransferables())
            {
                while (transferable.HasAnyThing && transferable.CountToTransfer > 0)
                {
                    Thing thing = transferable.things.Pop();
                    if (thing.stackCount <= transferable.CountToTransfer)
                    {
                        transferable.AdjustBy(-thing.stackCount);
                        thing.holdingOwner?.Remove(thing);
                        if (give)
                            addItemMethod.Invoke(outpost, new object[] { thing });
                        else
                            caravan.AddPawnOrItem((Thing)takeItemMethod.Invoke(outpost, new object[] { thing }), true);
                    }
                    else
                    {
                        Thing split = thing.SplitOff(transferable.CountToTransfer);
                        if (give)
                            addItemMethod.Invoke(outpost, new object[] { split });
                        else
                            caravan.AddPawnOrItem(split, true);
                        transferable.AdjustTo(0);
                        transferable.things.Add(thing);
                    }
                }
            }
            window.Close(true);
            TolkHelper.SpeakData(give
                ? (string)"RimWorldAccess.Compat.Vef.OutpostGiveDone".Translate(outpost.LabelCap)
                : (string)"RimWorldAccess.Compat.Vef.OutpostTakeDone".Translate(outpost.LabelCap));
        }

        public string AcceptLabel => CompatText.ModText(give ? "Outposts.Give" : "Outposts.Take");

        public bool HasSummary => false;

        public void BuildSummaryItems(List<string> outItems, float massUsage)
        {
        }

        public (string name, string explanation)? GetStatExplanation(int summaryIndex) => null;

        public string GetStatName(int summaryIndex) => "";

        private PlanetTile? Tile => Caravan?.Tile;

        // Never reached: HasPawnsTab is false.
        public TransferableTableColumns.WidgetView PawnsView => TransferableTableColumns.ViewFor(
            null, TransferableTableColumns.ColumnProfile.CaravanItems, Tile);

        public TransferableTableColumns.WidgetView ItemsView => TransferableTableColumns.ViewFor(
            (give ? giveWidgetField : takeWidgetField).GetValue(window) as TransferableOneWayWidget,
            TransferableTableColumns.ColumnProfile.CaravanItems, Tile);

        // Both dialogs pass this key as the widget's sourceCountDesc.
        public string CountColumnTooltip => "FormCaravanColonyThingCountTip".Translate();

        public bool HasPawnsTab => false;
    }
}
