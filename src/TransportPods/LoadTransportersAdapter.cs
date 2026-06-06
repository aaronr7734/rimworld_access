using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
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

        /// <summary>Whether the reflection handles resolved successfully.</summary>
        public static bool ReflectionReady => tabField != null && transferablesField != null && transportersField != null;

        private readonly Dialog_LoadTransporters dialog;

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
                string podType = podCount == 1 ? "pod" : "pods";
                return $"Load transport {podType}. {podCount} {podType}, {capacity:F0} kg capacity. Left/Right for tabs, Enter to adjust.";
            }
        }

        public string CancelAnnouncement => "Transport pod loading cancelled";

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

        public bool HasSummary => true;

        /// <summary>
        /// Builds the summary lines shown via the Tab key, mirroring exactly what
        /// CaravanUIUtility.DrawCaravanInfo shows for the dialog.
        /// For transport pods: Mass, Speed, Food, Foraging, Visibility.
        /// For shuttles: Mass and Food only (Speed, Foraging, Visibility are hidden).
        /// </summary>
        public void BuildSummaryItems(List<string> outItems, float massUsage)
        {
            try
            {
                var transporters = GetTransporters();
                if (transporters == null || transporters.Count == 0)
                {
                    outItems.Add("No transporters");
                    return;
                }

                bool isShuttle = TransportPodHelper.IsShuttle(transporters[0]);

                float massCapacity = MassCapacity;
                bool isOverloaded = massUsage > massCapacity;

                // 1. Mass - always shown
                outItems.Add(CaravanStatFormatter.FormatMass(massUsage, massCapacity));

                // 2. Speed - only for non-shuttles
                if (!isShuttle)
                {
                    var tilesInfo = AccessTools.Property(typeof(Dialog_LoadTransporters), "TilesPerDay");
                    if (tilesInfo != null)
                    {
                        float tilesPerDay = (float)tilesInfo.GetValue(dialog);
                        outItems.Add(CaravanStatFormatter.FormatSpeed(tilesPerDay, isOverloaded));
                    }
                }

                // 3. Food - always shown
                var foodInfo = AccessTools.Property(typeof(Dialog_LoadTransporters), "DaysWorthOfFood");
                if (foodInfo != null)
                {
                    var foodObj = foodInfo.GetValue(dialog);
                    var food = (ValueTuple<float, float>)foodObj;
                    outItems.Add(CaravanStatFormatter.FormatFood(food.Item1, food.Item2));
                }

                // 4. Foraging - only for non-shuttles
                if (!isShuttle)
                {
                    var forageInfo = AccessTools.Property(typeof(Dialog_LoadTransporters), "ForagedFoodPerDay");
                    if (forageInfo != null)
                    {
                        var forageObj = forageInfo.GetValue(dialog);
                        var forage = (ValueTuple<ThingDef, float>)forageObj;
                        outItems.Add(CaravanStatFormatter.FormatForaging(forage.Item1, forage.Item2));
                    }
                }

                // 5. Visibility - only for non-shuttles
                if (!isShuttle)
                {
                    var visInfo = AccessTools.Property(typeof(Dialog_LoadTransporters), "Visibility");
                    if (visInfo != null)
                    {
                        float visibility = (float)visInfo.GetValue(dialog);
                        outItems.Add(CaravanStatFormatter.FormatVisibility(visibility));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to get pod stats: {ex.Message}");
                outItems.Add("Stats unavailable");
            }
        }

        /// <summary>
        /// Returns the (stat name, breakdown explanation) for a summary line. Detects the stat
        /// from the line's text prefix since the order varies (shuttles vs pods), then accesses
        /// the matching property to force the game to recache its explanation string.
        /// </summary>
        public (string name, string explanation)? GetStatExplanation(string summaryItem)
        {
            if (string.IsNullOrEmpty(summaryItem))
                return null;

            try
            {
                string fieldName;
                string propertyName;
                string statName;

                if (summaryItem.StartsWith("Mass:"))
                {
                    fieldName = "cachedCaravanMassCapacityExplanation";
                    propertyName = "CaravanMassCapacity";
                    statName = "Mass Capacity";
                }
                else if (summaryItem.StartsWith("Speed:"))
                {
                    fieldName = "cachedTilesPerDayExplanation";
                    propertyName = "TilesPerDay";
                    statName = "Speed";
                }
                else if (summaryItem.StartsWith("Food:"))
                {
                    // Food has no breakdown explanation in the game; its tooltip
                    // (DaysWorthOfFoodTooltip) is already included in the line.
                    return null;
                }
                else if (summaryItem.StartsWith("Foraging:"))
                {
                    fieldName = "cachedForagedFoodPerDayExplanation";
                    propertyName = "ForagedFoodPerDay";
                    statName = "Foraging";
                }
                else if (summaryItem.StartsWith("Visibility:"))
                {
                    fieldName = "cachedVisibilityExplanation";
                    propertyName = "Visibility";
                    statName = "Visibility";
                }
                else
                {
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
