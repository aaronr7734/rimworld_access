using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// <see cref="ITransferLoadDialog"/> implementation for <see cref="Dialog_EnterPortal"/>,
    /// the dialog used to load pawns and items into a <see cref="MapPortal"/> (ancient complex
    /// entrances, Anomaly pit gates, insect lair entrances, and pocket-map exits).
    ///
    /// Map portals impose no mass limit and the dialog shows no stats panel, so this adapter
    /// reports an unlimited capacity, has no summary, and treats change notification as a no-op
    /// (the dialog's widgets recompute themselves each frame).
    /// </summary>
    internal sealed class EnterPortalAdapter : ITransferLoadDialog
    {
        private static readonly FieldInfo tabField = AccessTools.Field(typeof(Dialog_EnterPortal), "tab");
        private static readonly FieldInfo transferablesField = AccessTools.Field(typeof(Dialog_EnterPortal), "transferables");
        private static readonly FieldInfo portalField = AccessTools.Field(typeof(Dialog_EnterPortal), "portal");

        /// <summary>Whether the reflection handles resolved successfully.</summary>
        public static bool ReflectionReady => tabField != null && transferablesField != null;

        private readonly Dialog_EnterPortal dialog;

        public EnterPortalAdapter(Dialog_EnterPortal dialog)
        {
            this.dialog = dialog;
        }

        public string OpenAnnouncement
        {
            get
            {
                MapPortal portal = Portal;
                // portal.EnterString is localized, e.g. "Enter ancient complex" / "Exit ancient complex".
                string title = portal != null ? portal.EnterString : "Load portal";
                return $"{title}. Left/Right for tabs, Space or Enter to select, Alt+S to confirm.";
            }
        }

        public string CancelAnnouncement
        {
            get
            {
                MapPortal portal = Portal;
                string title = portal != null ? portal.EnterString : "Loading";
                return $"{title} cancelled";
            }
        }

        public List<TransferableOneWay> GetAllTransferables()
        {
            if (transferablesField == null)
                return new List<TransferableOneWay>();

            try
            {
                var transferables = transferablesField.GetValue(dialog) as List<TransferableOneWay>;
                return transferables ?? new List<TransferableOneWay>();
            }
            catch (Exception ex)
            {
                Log.Error($"RimWorld Access: Failed to get portal transferables: {ex.Message}");
                return new List<TransferableOneWay>();
            }
        }

        public int GameTab
        {
            get => tabField != null ? Convert.ToInt32(tabField.GetValue(dialog)) : 0;
            set
            {
                if (tabField == null)
                    return;
                try
                {
                    tabField.SetValue(dialog, value);
                }
                catch (Exception ex)
                {
                    Log.Error($"RimWorld Access: Failed to sync portal tab: {ex.Message}");
                }
            }
        }

        // Map portals have no mass limit: Dialog_EnterPortal passes () => float.MaxValue as capacity.
        public float MassCapacity => float.MaxValue;

        // Dialog_EnterPortal has no recache method; its TransferableOneWayWidget reads the live
        // CountToTransfer each frame, so adjusting the transferable is sufficient.
        public void NotifyTransferablesChanged()
        {
        }

        public void TriggerAccept()
        {
            dialog.OnAcceptKeyPressed();
        }

        // The vanilla portal dialog shows only the two tabs and the Reset/Cancel/Accept buttons -
        // no stats panel - so there is no summary to present.
        public bool HasSummary => false;

        public void BuildSummaryItems(List<string> outItems, float massUsage)
        {
        }

        public (string name, string explanation)? GetStatExplanation(string summaryItem)
        {
            return null;
        }

        private MapPortal Portal
        {
            get
            {
                if (portalField == null)
                    return null;
                try
                {
                    return portalField.GetValue(dialog) as MapPortal;
                }
                catch (Exception ex)
                {
                    Log.Error($"RimWorld Access: Failed to get portal: {ex.Message}");
                    return null;
                }
            }
        }
    }
}
