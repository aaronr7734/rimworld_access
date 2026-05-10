using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public static class VehicleCargoLoadingState
    {
        private const string LoadCargoDialogTypeName = "Vehicles.Dialog_LoadCargo";
        private const string VehicleReservationManagerTypeName = "Vehicles.VehicleReservationManager";
        private const string LoadVehicleReservationType = "LoadVehicle";

        private static bool isActive = false;
        private static Window currentDialog = null;
        private static int selectedIndex = 0;
        private static bool acceptAttempted = false;

        private static FieldInfo vehicleField = null;
        private static FieldInfo transferablesField = null;
        private static FieldInfo cargoToLoadField = null;
        private static FieldInfo massUsageDirtyField = null;
        private static PropertyInfo massUsageProperty = null;
        private static PropertyInfo massCapacityProperty = null;
        private static MethodInfo countToTransferChangedMethod = null;
        private static MethodInfo calculateAndRecacheTransferablesMethod = null;

        public static bool IsActive => isActive;
        public static bool AcceptAttempted => acceptAttempted;

        public static bool IsLoadCargoDialog(Window window)
        {
            return window != null && window.GetType().FullName == LoadCargoDialogTypeName;
        }

        public static void Open(Window dialog)
        {
            if (!IsLoadCargoDialog(dialog))
                return;

            if (!InitializeReflection(dialog.GetType()))
            {
                TolkHelper.Speak("Vehicle cargo loading accessibility unavailable due to Vehicle Framework update.", SpeechPriority.High);
                return;
            }

            isActive = true;
            currentDialog = dialog;
            selectedIndex = 0;
            acceptAttempted = false;

            object vehicle = GetVehicle();
            string vehicleLabel = vehicle is Thing thing ? thing.LabelShortCap : "vehicle";
            TolkHelper.Speak($"Load cargo for {vehicleLabel}. Up and down choose item, Enter adjusts quantity, Shift Enter loads maximum, Delete clears item, Alt S accepts, Alt R resets, Tab summary.");
            AnnounceCurrentItem();
        }

        public static void Close()
        {
            isActive = false;
            currentDialog = null;
            selectedIndex = 0;
            acceptAttempted = false;
        }

        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive || currentDialog == null)
                return false;

            if (key == KeyCode.UpArrow && !shift && !ctrl && !alt)
            {
                SelectPrevious();
                return true;
            }

            if (key == KeyCode.DownArrow && !shift && !ctrl && !alt)
            {
                SelectNext();
                return true;
            }

            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Space) && !shift && !ctrl && !alt)
            {
                OpenQuantityMenu();
                return true;
            }

            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && shift && !ctrl && !alt)
            {
                TransferableQuantityHelper.SetToMax(GetCurrentTransferableForQuantity, CountToTransferChanged);
                AnnounceCurrentItem();
                return true;
            }

            if (key == KeyCode.Delete && !shift && !ctrl && !alt)
            {
                TransferableQuantityHelper.SetToZero(GetCurrentTransferableForQuantity, CountToTransferChanged);
                AnnounceCurrentItem();
                return true;
            }

            if (key == KeyCode.Tab && !ctrl && !alt)
            {
                AnnounceSummary();
                return true;
            }

            if (key == KeyCode.S && alt && !shift && !ctrl)
            {
                Accept();
                return true;
            }

            if (key == KeyCode.R && alt && !shift && !ctrl)
            {
                Reset();
                return true;
            }

            if (TransferableQuantityHelper.HandleQuantityInput(key, shift, ctrl, alt, GetCurrentTransferableForQuantity, CountToTransferChanged))
            {
                AnnounceCurrentItem();
                return true;
            }

            return false;
        }

        private static bool InitializeReflection(Type dialogType)
        {
            if (vehicleField == null)
                vehicleField = AccessTools.Field(dialogType, "vehicle");
            if (transferablesField == null)
                transferablesField = AccessTools.Field(dialogType, "transferables");
            if (massUsageDirtyField == null)
                massUsageDirtyField = AccessTools.Field(dialogType, "massUsageDirty");
            if (massUsageProperty == null)
                massUsageProperty = AccessTools.Property(dialogType, "MassUsage");
            if (massCapacityProperty == null)
                massCapacityProperty = AccessTools.Property(dialogType, "MassCapacity");
            if (countToTransferChangedMethod == null)
                countToTransferChangedMethod = AccessTools.Method(dialogType, "CountToTransferChanged");
            if (calculateAndRecacheTransferablesMethod == null)
                calculateAndRecacheTransferablesMethod = AccessTools.Method(dialogType, "CalculateAndRecacheTransferables");

            Type vehicleType = AccessTools.TypeByName("Vehicles.VehiclePawn");
            if (vehicleType != null)
            {
                if (cargoToLoadField == null)
                    cargoToLoadField = AccessTools.Field(vehicleType, "cargoToLoad");
            }

            return vehicleField != null
                && transferablesField != null
                && massUsageProperty != null
                && massCapacityProperty != null
                && countToTransferChangedMethod != null
                && calculateAndRecacheTransferablesMethod != null
                && cargoToLoadField != null;
        }

        private static object GetVehicle()
        {
            return vehicleField?.GetValue(currentDialog);
        }

        private static List<TransferableOneWay> GetTransferables()
        {
            return transferablesField?.GetValue(currentDialog) as List<TransferableOneWay> ?? new List<TransferableOneWay>();
        }

        private static TransferableOneWay GetCurrentTransferableForQuantity()
        {
            List<TransferableOneWay> transferables = GetTransferables();
            if (transferables.Count == 0)
                return null;

            if (selectedIndex < 0 || selectedIndex >= transferables.Count)
                selectedIndex = 0;

            return transferables[selectedIndex];
        }

        private static void SelectPrevious()
        {
            List<TransferableOneWay> transferables = GetTransferables();
            if (transferables.Count == 0)
            {
                TolkHelper.Speak("No reachable cargo");
                return;
            }

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, transferables.Count);
            AnnounceCurrentItem();
        }

        private static void SelectNext()
        {
            List<TransferableOneWay> transferables = GetTransferables();
            if (transferables.Count == 0)
            {
                TolkHelper.Speak("No reachable cargo");
                return;
            }

            selectedIndex = MenuHelper.SelectNext(selectedIndex, transferables.Count);
            AnnounceCurrentItem();
        }

        private static void OpenQuantityMenu()
        {
            TransferableOneWay transferable = GetCurrentTransferableForQuantity();
            if (transferable == null)
            {
                TolkHelper.Speak("No cargo item selected");
                return;
            }

            QuantityMenuState.Open(transferable, (newQuantity) =>
            {
                transferable.AdjustTo(newQuantity);
                CountToTransferChanged();
                AnnounceCurrentItem();
            });
        }

        private static void Reset()
        {
            try
            {
                calculateAndRecacheTransferablesMethod.Invoke(currentDialog, null);
                selectedIndex = 0;
                TolkHelper.Speak("Cargo loading reset");
                AnnounceCurrentItem();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to reset vehicle cargo loading: {ex.Message}");
                TolkHelper.Speak("Could not reset cargo loading", SpeechPriority.High);
            }
        }

        private static void Accept()
        {
            try
            {
                object vehicle = GetVehicle();
                Thing vehicleThing = vehicle as Thing;
                if (vehicle == null || vehicleThing?.Map == null)
                {
                    TolkHelper.Speak("No vehicle available", SpeechPriority.High);
                    return;
                }

                List<TransferableOneWay> cargoToTransfer = GetTransferables()
                    .Where(t => t.CountToTransfer > 0)
                    .ToList();

                cargoToLoadField.SetValue(vehicle, cargoToTransfer);
                RegisterVehicleLister(vehicle, vehicleThing.Map);

                acceptAttempted = true;
                TolkHelper.Speak(BuildAcceptedAnnouncement(cargoToTransfer));
                currentDialog.Close(true);
            }
            catch (Exception ex)
            {
                acceptAttempted = false;
                ModLogger.Error($"Failed to accept vehicle cargo loading: {ex}");
                TolkHelper.Speak("Could not accept cargo loading", SpeechPriority.High);
            }
        }

        private static void RegisterVehicleLister(object vehicle, Map map)
        {
            MapComponent manager = map.components?.FirstOrDefault(c => c.GetType().FullName == VehicleReservationManagerTypeName);
            if (manager == null)
                return;

            MethodInfo registerLister = AccessTools.Method(manager.GetType(), "RegisterLister");
            registerLister?.Invoke(manager, new object[] { vehicle, LoadVehicleReservationType });
        }

        private static string BuildAcceptedAnnouncement(List<TransferableOneWay> cargoToTransfer)
        {
            int selectedStacks = cargoToTransfer.Count;
            int selectedCount = cargoToTransfer.Sum(t => t.CountToTransfer);
            float mass = GetMassUsage();
            float capacity = GetMassCapacity();

            if (selectedCount <= 0)
                return "Cargo loading accepted with no cargo selected";

            return $"Cargo loading accepted. {selectedCount} items in {selectedStacks} stacks, {mass:F1} of {capacity:F1} kg";
        }

        private static void CountToTransferChanged()
        {
            try
            {
                countToTransferChangedMethod.Invoke(currentDialog, null);
                massUsageDirtyField?.SetValue(currentDialog, true);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to update vehicle cargo transfer count: {ex.Message}");
            }
        }

        private static void AnnounceCurrentItem()
        {
            List<TransferableOneWay> transferables = GetTransferables();
            if (transferables.Count == 0)
            {
                TolkHelper.Speak("No reachable cargo");
                return;
            }

            if (selectedIndex < 0 || selectedIndex >= transferables.Count)
                selectedIndex = 0;

            TransferableOneWay transferable = transferables[selectedIndex];
            TolkHelper.Speak(BuildTransferableAnnouncement(transferable, selectedIndex, transferables.Count));
        }

        private static string BuildTransferableAnnouncement(TransferableOneWay transferable, int index, int total)
        {
            string label = GetTransferableLabel(transferable);
            int selected = transferable.CountToTransfer;
            int max = transferable.MaxCount;
            float massEach = GetMassEach(transferable);
            float massTotal = selected * massEach;
            string massPart = selected > 0 ? $", {massTotal:F1} kg selected" : "";
            return $"{label}, {selected} of {max}{massPart}. {MenuHelper.FormatPosition(index, total)}";
        }

        private static void AnnounceSummary()
        {
            List<TransferableOneWay> transferables = GetTransferables();
            int selectedStacks = transferables.Count(t => t.CountToTransfer > 0);
            int selectedCount = transferables.Sum(t => t.CountToTransfer);
            float mass = GetMassUsage();
            float capacity = GetMassCapacity();
            string overweight = mass > capacity ? ", overweight" : "";
            TolkHelper.Speak($"Vehicle cargo summary. {selectedCount} items in {selectedStacks} stacks selected. Mass {mass:F1} of {capacity:F1} kg{overweight}. {transferables.Count} reachable cargo stacks.");
        }

        private static string GetTransferableLabel(TransferableOneWay transferable)
        {
            if (transferable == null)
                return "";

            return transferable.LabelCap.StripTags();
        }

        private static float GetMassEach(TransferableOneWay transferable)
        {
            if (transferable?.AnyThing == null)
                return transferable?.ThingDef?.BaseMass ?? 0f;

            try
            {
                return transferable.AnyThing.GetStatValue(StatDefOf.Mass);
            }
            catch
            {
                return transferable.ThingDef?.BaseMass ?? 0f;
            }
        }

        private static float GetMassUsage()
        {
            if (massUsageProperty == null || currentDialog == null)
                return 0f;

            return Convert.ToSingle(massUsageProperty.GetValue(currentDialog, null));
        }

        private static float GetMassCapacity()
        {
            if (massCapacityProperty == null || currentDialog == null)
                return 0f;

            return Convert.ToSingle(massCapacityProperty.GetValue(currentDialog, null));
        }
    }
}
