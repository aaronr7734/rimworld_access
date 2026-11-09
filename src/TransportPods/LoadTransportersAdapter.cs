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
    /// <see cref="ITransferLoadDialog"/> implementation for <see cref="Dialog_LoadTransporters"/>,
    /// used by transport pods and shuttles. Encapsulates all reflection into the game's private
    /// dialog members and the caravan-style stats summary.
    /// </summary>
    internal sealed class LoadTransportersAdapter : ITransferLoadDialog
    {
        private static readonly FieldInfo tabField = AccessTools.Field(typeof(Dialog_LoadTransporters), "tab");
        private static readonly FieldInfo transferablesField = AccessTools.Field(typeof(Dialog_LoadTransporters), "transferables");
        private static readonly FieldInfo transportersField = AccessTools.Field(typeof(Dialog_LoadTransporters), "transporters");
        private static readonly MethodInfo countChangedMethod = AccessTools.Method(typeof(Dialog_LoadTransporters), "CountToTransferChanged");
        private static readonly MethodInfo debugLoadInstantlyMethod = AccessTools.Method(typeof(Dialog_LoadTransporters), "DebugTryLoadInstantly");
        private static readonly MethodInfo setToLoadEverythingMethod = AccessTools.Method(typeof(Dialog_LoadTransporters), "SetToLoadEverything");
        private static readonly PropertyInfo loadingInProgressProp = AccessTools.Property(typeof(Dialog_LoadTransporters), "LoadingInProgressOrReadyToLaunch");

        /// <summary>Whether the reflection handles resolved successfully.</summary>
        public static bool ReflectionReady => tabField != null && transferablesField != null && transportersField != null;

        private readonly Dialog_LoadTransporters dialog;

        /// <summary>
        /// Language-independent stat-kind tag for each line produced by the last
        /// <see cref="BuildSummaryItems"/> call, kept in lockstep with the line list so
        /// <see cref="GetStatExplanation"/> can dispatch by index instead of matching the
        /// (localized) line text. A null entry means the line has no breakdown.
        /// </summary>
        private readonly List<string> summaryKinds = new List<string>();

        public LoadTransportersAdapter(Dialog_LoadTransporters dialog)
        {
            this.dialog = dialog;
        }

        public string OpenAnnouncement
        {
            get
            {
                var transporters = GetTransporters();
                int podCount = transporters?.Count ?? 0;
                float capacity = MassCapacity;
                string key = podCount == 1
                    ? "RimWorldAccess.TransportPods.Loading.OpenAnnouncementOne"
                    : "RimWorldAccess.TransportPods.Loading.OpenAnnouncementMany";
                return key.Translate(podCount, capacity.ToString("F0"));
            }
        }

        public string CancelAnnouncement => "RimWorldAccess.TransportPods.Loading.Cancelled".Translate();

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
                Log.Error($"RimWorld Access: Failed to get transferables: {ex.Message}");
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
                    Log.Error($"RimWorld Access: Failed to sync tab: {ex.Message}");
                }
            }
        }

        public float MassCapacity
        {
            get
            {
                var transporters = GetTransporters();
                if (transporters == null || transporters.Count == 0)
                    return 0f;

                float total = 0f;
                foreach (var transporter in transporters)
                {
                    if (transporter?.Props != null)
                    {
                        total += transporter.Props.massCapacity;
                    }
                }
                return total;
            }
        }

        public void NotifyTransferablesChanged()
        {
            if (countChangedMethod == null)
                return;

            try
            {
                countChangedMethod.Invoke(dialog, null);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to call CountToTransferChanged: {ex.Message}");
            }
        }

        public void TriggerAccept()
        {
            dialog.OnAcceptKeyPressed();
        }

        // ----- DEV-mode buttons (Dialog_LoadTransporters.DoBottomButtons :348-361) -----
        //
        // Only this vanilla dialog draws them, so the reflection lives here rather than on
        // ITransferLoadDialog; the scope gates on `adapter is LoadTransportersAdapter`. Both
        // operations invoke the dialog's own private methods (Vehicle A) — no raw mutation.

        /// <summary>Vanilla's own gate for the "DEV: Load instantly" button (decompiled :352): hidden once loading has already started.</summary>
        public bool LoadingInProgressOrReadyToLaunch =>
            loadingInProgressProp != null && (bool)loadingInProgressProp.GetValue(dialog);

        /// <summary>Invokes the dialog's own DebugTryLoadInstantly (Vehicle A); returns whether it loaded (vanilla then ticks and closes).</summary>
        public bool DebugTryLoadInstantly() =>
            debugLoadInstantlyMethod != null && (bool)debugLoadInstantlyMethod.Invoke(dialog, null);

        /// <summary>Invokes the dialog's own SetToLoadEverything (Vehicle A): maxes every transferable and recaches.</summary>
        public void SetToLoadEverything() => setToLoadEverythingMethod?.Invoke(dialog, null);

        public string AcceptLabel => null;

        public bool HasSummary => true;

        /// <summary>
        /// Builds the summary lines shown via the Tab key, mirroring exactly what
        /// CaravanUIUtility.DrawCaravanInfo shows for the dialog.
        /// For transport pods: Mass, CaravanMass, Speed, Food, Foraging, Visibility.
        /// For shuttles: Mass and Food only (CaravanMass, Speed, Foraging, Visibility are hidden).
        /// </summary>
        public void BuildSummaryItems(List<string> outItems, float massUsage)
        {
            summaryKinds.Clear();
            try
            {
                var transporters = GetTransporters();
                if (transporters == null || transporters.Count == 0)
                {
                    outItems.Add("RimWorldAccess.TransportPods.Loading.SummaryNoTransporters".Translate());
                    summaryKinds.Add(null);
                    return;
                }

                bool isShuttle = TransportPodHelper.IsShuttle(transporters[0]);

                float massCapacity = MassCapacity;
                bool isOverloaded = massUsage > massCapacity;

                // 1. Mass - always shown
                outItems.Add(CaravanStatFormatter.FormatMass(massUsage, massCapacity));
                summaryKinds.Add("Mass");

                // 2. CaravanMass ("Caravan mass" - the cargo's own carrying capacity once the pods
                // land and become a caravan) - only for non-shuttles. Mirrors
                // CaravanUIUtility.DrawCaravanInfo's own gate (decompiled :199): a second mass line,
                // shown whenever extraMassUsage is set and isCaravan, which Dialog_LoadTransporters
                // (decompiled :253) passes as exactly !transporters.IsShuttle() - the same condition
                // already used below for Speed/Foraging/Visibility.
                if (!isShuttle)
                {
                    var caravanMassCapacityProp = AccessTools.Property(typeof(Dialog_LoadTransporters), "CaravanMassCapacity");
                    if (caravanMassCapacityProp != null)
                    {
                        float caravanMassUsage = dialog.CaravanMassUsage;
                        float caravanMassCapacity = (float)caravanMassCapacityProp.GetValue(dialog);
                        string caravanMassLabel = "CaravanMass".Translate();
                        string usageStr = caravanMassUsage.ToString("F1");
                        string capacityStr = caravanMassCapacity.ToString("F1");
                        outItems.Add(caravanMassUsage > caravanMassCapacity
                            ? "RimWorldAccess.TransportPods.Loading.CaravanMassOverloaded".Translate(caravanMassLabel, usageStr, capacityStr)
                            : "RimWorldAccess.TransportPods.Loading.CaravanMass".Translate(caravanMassLabel, usageStr, capacityStr));
                        summaryKinds.Add("CaravanMass");
                    }
                }

                // 3. Speed - only for non-shuttles
                if (!isShuttle)
                {
                    var tilesInfo = AccessTools.Property(typeof(Dialog_LoadTransporters), "TilesPerDay");
                    if (tilesInfo != null)
                    {
                        float tilesPerDay = (float)tilesInfo.GetValue(dialog);
                        outItems.Add(CaravanStatFormatter.FormatSpeed(tilesPerDay, isOverloaded));
                        summaryKinds.Add("Speed");
                    }
                }

                // 4. Food - always shown
                var foodInfo = AccessTools.Property(typeof(Dialog_LoadTransporters), "DaysWorthOfFood");
                if (foodInfo != null)
                {
                    var foodObj = foodInfo.GetValue(dialog);
                    var food = (ValueTuple<float, float>)foodObj;
                    outItems.Add(CaravanStatFormatter.FormatFood(food.Item1, food.Item2));
                    summaryKinds.Add("Food");
                }

                // 5. Foraging - only for non-shuttles
                if (!isShuttle)
                {
                    var forageInfo = AccessTools.Property(typeof(Dialog_LoadTransporters), "ForagedFoodPerDay");
                    if (forageInfo != null)
                    {
                        var forageObj = forageInfo.GetValue(dialog);
                        var forage = (ValueTuple<ThingDef, float>)forageObj;
                        outItems.Add(CaravanStatFormatter.FormatForaging(forage.Item1, forage.Item2));
                        summaryKinds.Add("Foraging");
                    }
                }

                // 6. Visibility - only for non-shuttles
                if (!isShuttle)
                {
                    var visInfo = AccessTools.Property(typeof(Dialog_LoadTransporters), "Visibility");
                    if (visInfo != null)
                    {
                        float visibility = (float)visInfo.GetValue(dialog);
                        outItems.Add(CaravanStatFormatter.FormatVisibility(visibility));
                        summaryKinds.Add("Visibility");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to get pod stats: {ex.Message}");
                outItems.Add("RimWorldAccess.TransportPods.Loading.SummaryStatsUnavailable".Translate());
                summaryKinds.Add(null);
            }
        }

        /// <summary>
        /// Returns the (stat name, breakdown explanation) for the summary line at the given index.
        /// Dispatches on the language-independent kind tag recorded in <see cref="summaryKinds"/>
        /// while building the summary (NOT on the line's localized text), then accesses the matching
        /// property to force the game to recache its explanation string.
        /// </summary>
        public (string name, string explanation)? GetStatExplanation(int summaryIndex)
        {
            if (summaryIndex < 0 || summaryIndex >= summaryKinds.Count)
                return null;

            string kind = summaryKinds[summaryIndex];
            if (string.IsNullOrEmpty(kind))
                return null;

            try
            {
                string fieldName;
                string propertyName;
                string statName;

                switch (kind)
                {
                    // CaravanMass's breakdown belongs to CaravanMassCapacity (Dialog_LoadTransporters
                    // passes cachedCaravanMassCapacityExplanation as its OWN extraMassCapacityExplanation
                    // arg, decompiled :253). Plain "Mass" (the pod capacity line) has no breakdown at
                    // all in vanilla - Dialog_LoadTransporters passes "" as massCapacityExplanation for
                    // it (same call site) - so it now falls to the default case below, where it belongs.
                    case "CaravanMass":
                        fieldName = "cachedCaravanMassCapacityExplanation";
                        propertyName = "CaravanMassCapacity";
                        statName = "CaravanMass".Translate();
                        break;
                    case "Speed":
                        fieldName = "cachedTilesPerDayExplanation";
                        propertyName = "TilesPerDay";
                        statName = "RimWorldAccess.TransportPods.Loading.StatSpeed".Translate();
                        break;
                    case "Foraging":
                        fieldName = "cachedForagedFoodPerDayExplanation";
                        propertyName = "ForagedFoodPerDay";
                        statName = "RimWorldAccess.TransportPods.Loading.StatForaging".Translate();
                        break;
                    case "Visibility":
                        fieldName = "cachedVisibilityExplanation";
                        propertyName = "Visibility";
                        statName = "RimWorldAccess.TransportPods.Loading.StatVisibility".Translate();
                        break;
                    default:
                        // "Mass" (see above) and "Food" (DaysWorthOfFoodTooltip is already included
                        // in the line) have no breakdown explanation in the game.
                        return null;
                }

                // Access the property getter to trigger recalculation of the cached explanation.
                var prop = AccessTools.Property(typeof(Dialog_LoadTransporters), propertyName);
                prop?.GetValue(dialog);

                var field = AccessTools.Field(typeof(Dialog_LoadTransporters), fieldName);
                if (field == null)
                    return null;

                string explanation = field.GetValue(dialog) as string;
                if (string.IsNullOrEmpty(explanation))
                    return null;

                return (statName, explanation);
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to get stat explanation: {ex.Message}");
                return null;
            }
        }

        /// <summary>Same kind->short-name mapping GetStatExplanation uses, but returned for EVERY kind (including "Food", which has no breakdown) — table-model T3's stat table needs a name for every row.</summary>
        public string GetStatName(int summaryIndex)
        {
            if (summaryIndex < 0 || summaryIndex >= summaryKinds.Count)
                return "";
            switch (summaryKinds[summaryIndex])
            {
                case "Mass": return "RimWorldAccess.TransportPods.Loading.StatMassCapacity".Translate();
                case "CaravanMass": return "CaravanMass".Translate();
                case "Speed": return "RimWorldAccess.TransportPods.Loading.StatSpeed".Translate();
                case "Food": return "RimWorldAccess.Caravan.Inspect.StatFood".Translate();
                case "Foraging": return "RimWorldAccess.TransportPods.Loading.StatForaging".Translate();
                case "Visibility": return "RimWorldAccess.TransportPods.Loading.StatVisibility".Translate();
                default: return "";
            }
        }

        // Table-model T3 (the table-model doctrine): the column set is read
        // off the dialog's own live TransferableOneWayWidget instances, so it is
        // the game's decision by construction. The ColumnProfile presets are only
        // the fallback for a null/unreflectable widget.
        private static readonly FieldInfo pawnsTransferField = AccessTools.Field(typeof(Dialog_LoadTransporters), "pawnsTransfer");
        private static readonly FieldInfo itemsTransferField = AccessTools.Field(typeof(Dialog_LoadTransporters), "itemsTransfer");

        private PlanetTile? FallbackTile => Find.CurrentMap != null ? (PlanetTile?)Find.CurrentMap.Tile : null;

        public TransferableTableColumns.WidgetView PawnsView => TransferableTableColumns.ViewFor(
            pawnsTransferField?.GetValue(dialog) as TransferableOneWayWidget,
            TransferableTableColumns.ColumnProfile.PodPawns, FallbackTile);

        public TransferableTableColumns.WidgetView ItemsView => TransferableTableColumns.ViewFor(
            itemsTransferField?.GetValue(dialog) as TransferableOneWayWidget,
            TransferableTableColumns.ColumnProfile.PodItems, FallbackTile);

        public string CountColumnTooltip => "TransporterColonyThingCountTip".Translate();

        public bool HasPawnsTab => true;

        private List<CompTransporter> GetTransporters()
        {
            if (transportersField == null)
                return new List<CompTransporter>();

            try
            {
                var transporters = transportersField.GetValue(dialog) as List<CompTransporter>;
                return transporters ?? new List<CompTransporter>();
            }
            catch (Exception ex)
            {
                Log.Error($"RimWorld Access: Failed to get transporters: {ex.Message}");
                return new List<CompTransporter>();
            }
        }
    }
}
