using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimWorldAccess.Shell;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// <see cref="ITransferLoadDialog"/> implementation for Vehicle Framework's
    /// <c>Vehicles.World.Dialog_StashVehicle</c> ("Dock to caravan": move items out of a
    /// VehicleCaravan before stashing the vehicle away). Structurally a third sibling of
    /// Dialog_LoadCargo -- a single items table, no Pawns tab, no invocable accept method (the
    /// accept branch is inline IMGUI in DoBottomButtons) -- so it rides the same
    /// <see cref="TransportPodLoadingScope"/> machinery. The per-stat cached-explanation fields
    /// and the caravan-pather "is it moving" check are OPTIONAL: their absence drops the
    /// breakdown/arrival line rather than killing the dialog.
    ///
    /// The dialog's on-screen title ("Items to Take", VF's own VF_DockToCaravan key) is baked
    /// into StashOpen's wording rather than pulled dynamically, because Workshop-mod Keyed XML is
    /// invisible to check_l10n_keys.py's fake-key ratchet.
    /// </summary>
    internal sealed class VfStashDialogAdapter : ITransferLoadDialog
    {
        private static readonly Type dialogType;
        private static readonly Type vehicleCaravanType;
        private static readonly Type pathFollowerType;

        private static readonly FieldInfo caravanField;
        private static readonly FieldInfo transferablesField;
        private static readonly FieldInfo itemsTransferField;
        private static readonly MethodInfo transferPawnsMethod;
        private static readonly MethodInfo countToTransferChangedMethod;

        private static readonly MethodInfo sourceMassUsageGetter;
        private static readonly MethodInfo sourceMassCapacityGetter;
        private static readonly MethodInfo sourceTilesPerDayGetter;
        private static readonly MethodInfo sourceDaysWorthOfFoodGetter;
        private static readonly MethodInfo sourceForagedFoodPerDayGetter;
        private static readonly MethodInfo sourceVisibilityGetter;
        private static readonly MethodInfo destMassCapacityGetter;
        private static readonly MethodInfo destMassUsageGetter;
        private static readonly MethodInfo ticksToArriveGetter;

        // Optional: a missing cache field only drops that one breakdown line.
        private static readonly FieldInfo cachedSourceMassCapacityExplanationField;
        private static readonly FieldInfo cachedSourceTilesPerDayExplanationField;
        private static readonly FieldInfo cachedSourceForagedFoodPerDayExplanationField;
        private static readonly FieldInfo cachedSourceVisibilityExplanationField;

        // Optional: a missing pather surface only drops the arrival line.
        private static readonly FieldInfo vehiclePatherField;
        private static readonly MethodInfo patherMovingGetter;

        private static readonly bool ready;

        /// <summary>Vehicle Framework's stash dialog type, or null when VF isn't loaded.</summary>
        public static Type DialogType => dialogType;

        /// <summary>Whether every REQUIRED reflected member resolved. False with the type present means a VF update renamed something.</summary>
        public static bool ReflectionReady => ready;

        static VfStashDialogAdapter()
        {
            var surface = new ReflectionSurface("VfStashDialogAdapter");

            dialogType = surface.Type("Vehicles.World.Dialog_StashVehicle");

            caravanField = surface.Field(dialogType, "caravan");
            transferablesField = surface.Field(dialogType, "transferables");
            itemsTransferField = surface.Field(dialogType, "itemsTransfer");
            transferPawnsMethod = surface.Method(dialogType, "TransferPawns");
            countToTransferChangedMethod = surface.Method(dialogType, "CountToTransferChanged");

            sourceMassUsageGetter = surface.Property(dialogType, "SourceMassUsage")?.GetGetMethod(true);
            sourceMassCapacityGetter = surface.Property(dialogType, "SourceMassCapacity")?.GetGetMethod(true);
            sourceTilesPerDayGetter = surface.Property(dialogType, "SourceTilesPerDay")?.GetGetMethod(true);
            sourceDaysWorthOfFoodGetter = surface.Property(dialogType, "SourceDaysWorthOfFood")?.GetGetMethod(true);
            sourceForagedFoodPerDayGetter = surface.Property(dialogType, "SourceForagedFoodPerDay")?.GetGetMethod(true);
            sourceVisibilityGetter = surface.Property(dialogType, "SourceVisibility")?.GetGetMethod(true);
            destMassCapacityGetter = surface.Property(dialogType, "DestMassCapacity")?.GetGetMethod(true);
            destMassUsageGetter = surface.Property(dialogType, "DestMassUsage")?.GetGetMethod(true);
            ticksToArriveGetter = surface.Property(dialogType, "TicksToArrive")?.GetGetMethod(true);

            // Optional from here down: raw lookups, kept out of the surface's ready gate.
            cachedSourceMassCapacityExplanationField = AccessTools.Field(dialogType, "cachedSourceMassCapacityExplanation");
            cachedSourceTilesPerDayExplanationField = AccessTools.Field(dialogType, "cachedSourceTilesPerDayExplanation");
            cachedSourceForagedFoodPerDayExplanationField = AccessTools.Field(dialogType, "cachedSourceForagedFoodPerDayExplanation");
            cachedSourceVisibilityExplanationField = AccessTools.Field(dialogType, "cachedSourceVisibilityExplanation");

            vehicleCaravanType = AccessTools.TypeByName("Vehicles.World.VehicleCaravan");
            pathFollowerType = AccessTools.TypeByName("Vehicles.World.VehicleCaravan_PathFollower");
            vehiclePatherField = vehicleCaravanType != null ? AccessTools.Field(vehicleCaravanType, "vehiclePather") : null;
            patherMovingGetter = pathFollowerType != null ? AccessTools.PropertyGetter(pathFollowerType, "Moving") : null;

            ready = surface.Ready;
        }

        private readonly Window window;

        public VfStashDialogAdapter(Window window)
        {
            this.window = window;
        }

        public string OpenAnnouncement => "RimWorldAccess.Compat.Vf.StashOpen".Translate();

        public string CancelAnnouncement => "RimWorldAccess.Compat.Vf.StashCancelled".Translate();

        public List<TransferableOneWay> GetAllTransferables()
        {
            if (transferablesField == null)
                return new List<TransferableOneWay>();

            try
            {
                var transferables = transferablesField.GetValue(window) as List<TransferableOneWay>;
                return transferables ?? new List<TransferableOneWay>();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfStashDialogAdapter.GetAllTransferables failed: {ex.Message}");
                return new List<TransferableOneWay>();
            }
        }

        // Dialog_StashVehicle draws a single items table with no tab bar (a TabRecord is drawn but
        // it is the sole "Items" tab), so there is nothing to read or mirror into.
        public int GameTab
        {
            get => 1;
            set { }
        }

        /// <summary>
        /// The widget's own availableMassGetter is DestMassCapacity - DestMassUsage
        /// (CreateCaravanItemsWidget's call site), so MassCapacity is the total side of that
        /// pair -- DestMassCapacity itself.
        /// </summary>
        public float MassCapacity
        {
            get
            {
                if (destMassCapacityGetter == null)
                    return 0f;
                try
                {
                    return (float)destMassCapacityGetter.Invoke(window, null);
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"VfStashDialogAdapter.MassCapacity failed: {ex.Message}");
                    return 0f;
                }
            }
        }

        public void NotifyTransferablesChanged()
        {
            if (countToTransferChangedMethod == null)
                return;

            try
            {
                countToTransferChangedMethod.Invoke(window, null);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfStashDialogAdapter.NotifyTransferablesChanged failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Vehicle B: invokes the dialog's own gated TransferPawns() and honors its answer,
        /// mirroring DoBottomButtons' accept branch (same sound, same Close(false)) -- no
        /// MUTATION-C marker needed, the mutation lives inside the invoked vanilla method
        /// (StashedVehicle.Create). VF gives no player-facing message on either branch, so this
        /// speaks the outcome.
        /// </summary>
        public void TriggerAccept()
        {
            if (!ready)
                return;

            try
            {
                bool accepted = (bool)transferPawnsMethod.Invoke(window, null);
                if (accepted)
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                    window.Close(false);
                    TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vf.StashAccepted".Translate());
                }
                else
                {
                    TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vf.StashFailed".Translate());
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfStashDialogAdapter.TriggerAccept failed: {ex.Message}");
            }
        }

        public string AcceptLabel => null;

        public bool HasSummary => true;

        /// <summary>
        /// DoWindowContents passes the SOURCE stats to CaravanUIUtility.DrawCaravanInfo with a null
        /// second CaravanInfo, so only source stats show (destination stats exist on the dialog but
        /// are never drawn). The caller's massUsage parameter is ignored -- the dialog's own
        /// SourceMassUsage property is the source of truth, unlike the two vanilla dialogs.
        /// </summary>
        public void BuildSummaryItems(List<string> outItems, float massUsage)
        {
            summaryKinds.Clear();
            if (!ready)
                return;

            try
            {
                float sourceMassUsage = (float)sourceMassUsageGetter.Invoke(window, null);
                float sourceMassCapacity = (float)sourceMassCapacityGetter.Invoke(window, null);
                outItems.Add(CaravanStatFormatter.FormatMass(sourceMassUsage, sourceMassCapacity));
                summaryKinds.Add("Mass");

                float sourceTilesPerDay = (float)sourceTilesPerDayGetter.Invoke(window, null);
                outItems.Add(CaravanStatFormatter.FormatSpeed(sourceTilesPerDay, sourceMassUsage > sourceMassCapacity));
                summaryKinds.Add("Speed");

                var food = ((float days, float tillRot))sourceDaysWorthOfFoodGetter.Invoke(window, null);
                outItems.Add(CaravanStatFormatter.FormatFood(food.days, food.tillRot));
                summaryKinds.Add("Food");

                var forage = ((ThingDef food, float perDay))sourceForagedFoodPerDayGetter.Invoke(window, null);
                outItems.Add(CaravanStatFormatter.FormatForaging(forage.food, forage.perDay));
                summaryKinds.Add("Foraging");

                float visibility = (float)sourceVisibilityGetter.Invoke(window, null);
                outItems.Add(CaravanStatFormatter.FormatVisibility(visibility));
                summaryKinds.Add("Visibility");

                if (vehiclePatherField != null && patherMovingGetter != null && ticksToArriveGetter != null)
                {
                    object caravan = caravanField.GetValue(window);
                    object pather = caravan != null ? vehiclePatherField.GetValue(caravan) : null;
                    bool moving = pather != null && (bool)patherMovingGetter.Invoke(pather, null);
                    if (moving)
                    {
                        int ticks = (int)ticksToArriveGetter.Invoke(window, null);
                        if (ticks > 0)
                        {
                            outItems.Add("RimWorldAccess.Compat.Vf.StashArrival".Translate((ticks / 60000f).ToString("F1")));
                            summaryKinds.Add("Arrival");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfStashDialogAdapter.BuildSummaryItems failed: {ex.Message}");
                outItems.Add("RimWorldAccess.TransportPods.Loading.SummaryStatsUnavailable".Translate());
                summaryKinds.Add(null);
            }
        }

        /// <summary>Language-independent stat-kind tag per line, so <see cref="GetStatExplanation"/> dispatches by index rather than matching localized text.</summary>
        private readonly List<string> summaryKinds = new List<string>();

        public (string name, string explanation)? GetStatExplanation(int summaryIndex)
        {
            if (summaryIndex < 0 || summaryIndex >= summaryKinds.Count)
                return null;

            string kind = summaryKinds[summaryIndex];
            if (string.IsNullOrEmpty(kind))
                return null;

            try
            {
                MethodInfo getter;
                FieldInfo explanationField;
                switch (kind)
                {
                    case "Mass":
                        getter = sourceMassCapacityGetter;
                        explanationField = cachedSourceMassCapacityExplanationField;
                        break;
                    case "Speed":
                        getter = sourceTilesPerDayGetter;
                        explanationField = cachedSourceTilesPerDayExplanationField;
                        break;
                    case "Foraging":
                        getter = sourceForagedFoodPerDayGetter;
                        explanationField = cachedSourceForagedFoodPerDayExplanationField;
                        break;
                    case "Visibility":
                        getter = sourceVisibilityGetter;
                        explanationField = cachedSourceVisibilityExplanationField;
                        break;
                    default:
                        // "Food" and "Arrival" have no cached breakdown text on this dialog.
                        return null;
                }
                if (getter == null || explanationField == null)
                    return null;

                // Touch the getter first -- it recomputes the cached explanation when dirty.
                getter.Invoke(window, null);
                string explanation = explanationField.GetValue(window) as string;
                if (string.IsNullOrEmpty(explanation))
                    return null;
                return (GetStatName(summaryIndex), explanation);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfStashDialogAdapter.GetStatExplanation failed: {ex.Message}");
                return null;
            }
        }

        public string GetStatName(int summaryIndex)
        {
            if (summaryIndex < 0 || summaryIndex >= summaryKinds.Count)
                return "";
            switch (summaryKinds[summaryIndex])
            {
                case "Mass": return "RimWorldAccess.Caravan.Inspect.StatMass".Translate();
                case "Speed": return "RimWorldAccess.Caravan.Inspect.StatSpeed".Translate();
                case "Food": return "RimWorldAccess.Caravan.Inspect.StatFood".Translate();
                case "Foraging": return "RimWorldAccess.Caravan.Inspect.StatForaging".Translate();
                case "Visibility": return "RimWorldAccess.Caravan.Inspect.StatVisibility".Translate();
                case "Arrival": return "RimWorldAccess.Compat.Vf.StatArrival".Translate();
                default: return "";
            }
        }

        private PlanetTile? FallbackTile => Find.CurrentMap != null ? (PlanetTile?)Find.CurrentMap.Tile : null;

        // Never reached -- HasPawnsTab is false -- but kept total.
        public TransferableTableColumns.WidgetView PawnsView => TransferableTableColumns.ViewFor(
            null, TransferableTableColumns.ColumnProfile.PodItems, FallbackTile);

        public TransferableTableColumns.WidgetView ItemsView => TransferableTableColumns.ViewFor(
            itemsTransferField?.GetValue(window) as TransferableOneWayWidget,
            TransferableTableColumns.ColumnProfile.PodItems, FallbackTile);

        // A genuine vanilla Core key (Dialogs_Various.xml), the same one CreateCaravanItemsWidget
        // passes as this dialog's thingCountTip -- safe to call directly, unlike VF's own keys.
        public string CountColumnTooltip => "SplitCaravanThingCountTip".Translate();

        public bool HasPawnsTab => false;
    }
}
