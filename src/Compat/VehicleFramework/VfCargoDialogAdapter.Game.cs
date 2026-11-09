using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// <see cref="ITransferLoadDialog"/> implementation for Vehicle Framework's
    /// <c>Vehicles.Dialog_LoadCargo</c> (opened from a vehicle's "Load Cargo" gizmo).
    ///
    /// Unlike the two vanilla dialogs, Dialog_LoadCargo has no Pawns tab at all -- it is a single
    /// items table with no tab bar (<see cref="HasPawnsTab"/> is false) -- and no invocable
    /// accept method: its accept branch is inline IMGUI in BottomButtons, so
    /// <see cref="TriggerAccept"/> is MUTATION-C (see its own remarks).
    /// </summary>
    internal sealed class VfCargoDialogAdapter : ITransferLoadDialog
    {
        private static readonly Type dialogType;
        private static readonly Type vehiclePawnType;
        private static readonly Type reservationManagerType;

        private static readonly FieldInfo vehicleField;
        private static readonly FieldInfo transferablesField;
        private static readonly FieldInfo itemsTransferField;
        private static readonly PropertyInfo massUsageProperty;
        private static readonly PropertyInfo massCapacityProperty;
        private static readonly MethodInfo countToTransferChangedMethod;
        private static readonly MethodInfo registerListerMethod;

        private static readonly bool ready;

        /// <summary>Vehicle Framework's cargo dialog type, or null when VF isn't loaded.</summary>
        public static Type DialogType => dialogType;

        /// <summary>Whether every reflected member resolved. False with the type present means a VF update renamed something.</summary>
        public static bool ReflectionReady => ready;

        static VfCargoDialogAdapter()
        {
            var surface = new ReflectionSurface("VfCargoDialogAdapter");

            dialogType = surface.Type("Vehicles.Dialog_LoadCargo");
            vehiclePawnType = surface.Supplied("Vehicles.VehiclePawn", VfVehiclePawn.VehiclePawnType);
            reservationManagerType = surface.Type("Vehicles.VehicleReservationManager");

            vehicleField = surface.Field(dialogType, "vehicle");
            transferablesField = surface.Field(dialogType, "transferables");
            itemsTransferField = surface.Field(dialogType, "itemsTransfer");
            massUsageProperty = surface.Property(dialogType, "MassUsage");
            massCapacityProperty = surface.Property(dialogType, "MassCapacity");
            countToTransferChangedMethod = surface.Method(dialogType, "CountToTransferChanged");
            registerListerMethod = vehiclePawnType != null
                ? surface.Method(reservationManagerType, "RegisterLister", new[] { vehiclePawnType, typeof(string) })
                : null;

            ready = surface.Ready && VfVehiclePawn.Ready;
        }

        // All member access below goes through the base Verse.Window reference -- Dialog_LoadCargo
        // itself is never referenced as a compiled type, only as the runtime object reflection
        // targets.
        private readonly Window window;

        public VfCargoDialogAdapter(Window window)
        {
            this.window = window;
        }

        public string OpenAnnouncement
        {
            get
            {
                Pawn vehicle = GetVehicle();
                string vehicleName = vehicle?.LabelShortCap ?? "";
                return "RimWorldAccess.Compat.Vf.LoadCargoOpen".Translate(vehicleName, MassCapacity.ToString("F0"));
            }
        }

        public string CancelAnnouncement => "RimWorldAccess.Compat.Vf.LoadCargoCancelled".Translate();

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
                ModLogger.Error($"VfCargoDialogAdapter.GetAllTransferables failed: {ex.Message}");
                return new List<TransferableOneWay>();
            }
        }

        // Dialog_LoadCargo has no tab field at all -- it draws a single items table -- so there is
        // nothing to read or mirror into.
        public int GameTab
        {
            get => 1;
            set { }
        }

        public float MassCapacity
        {
            get
            {
                if (massCapacityProperty == null)
                    return 0f;
                try
                {
                    return (float)massCapacityProperty.GetValue(window);
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"VfCargoDialogAdapter.MassCapacity failed: {ex.Message}");
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
                ModLogger.Error($"VfCargoDialogAdapter.NotifyTransferablesChanged failed: {ex.Message}");
            }
        }

        /// <summary>
        /// MUTATION-C: mirrors Dialog_LoadCargo.BottomButtons accept branch; body is inline IMGUI,
        /// no invocable vehicle. Filters transferables to the ones with CountToTransfer &gt; 0,
        /// assigns them to VehiclePawn.cargoToLoad, registers the vehicle as a cargo-loading
        /// reservation lister exactly as the button does, then closes the dialog exactly like the
        /// button's own Close(true) call. The vehicle then handles the actual pickup over
        /// subsequent ticks; the game gives no message of its own here, so we speak one so the
        /// player hears that Accept took effect.
        /// </summary>
        public void TriggerAccept()
        {
            if (!ready)
                return;

            try
            {
                Pawn vehicle = GetVehicle();
                if (vehicle == null)
                    return;

                List<TransferableOneWay> allTransferables = transferablesField.GetValue(window) as List<TransferableOneWay>;
                List<TransferableOneWay> cargoToLoad = (allTransferables ?? new List<TransferableOneWay>())
                    .Where(t => t.CountToTransfer > 0)
                    .ToList();
                // MUTATION-C: mirrors Dialog_LoadCargo.BottomButtons accept branch; body is
                // inline IMGUI, no invocable vehicle.
                VfVehiclePawn.CargoToLoadField.SetValue(vehicle, cargoToLoad);

                MapComponent reservationManager = vehicle.Map?.GetComponent(reservationManagerType);
                if (reservationManager == null)
                {
                    ModLogger.Error("VfCargoDialogAdapter.TriggerAccept: vehicle's map has no VehicleReservationManager component.");
                    return;
                }
                // Vehicles.ReservationType.LoadVehicle is a public const string ("LoadVehicle"),
                // not an enum member -- reproduced literally rather than resolved via reflection.
                registerListerMethod.Invoke(reservationManager, new object[] { vehicle, "LoadVehicle" });

                window.Close(true);
                TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vf.LoadCargoAccepted".Translate(vehicle.LabelShortCap));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfCargoDialogAdapter.TriggerAccept failed: {ex.Message}");
            }
        }

        public string AcceptLabel => null;

        public bool HasSummary => true;

        /// <summary>The cargo dialog's own DrawCargoNumbers readout shows only mass, so the summary is a single line.</summary>
        public void BuildSummaryItems(List<string> outItems, float massUsage)
        {
            outItems.Add(CaravanStatFormatter.FormatMass(massUsage, MassCapacity));
        }

        // This dialog caches no per-stat breakdown text, so there is never a breakdown to show.
        public (string name, string explanation)? GetStatExplanation(int summaryIndex) => null;

        public string GetStatName(int summaryIndex)
        {
            return summaryIndex == 0 ? (string)"RimWorldAccess.TransportPods.Loading.StatMassCapacity".Translate() : "";
        }

        private PlanetTile? FallbackTile => Find.CurrentMap != null ? (PlanetTile?)Find.CurrentMap.Tile : null;

        // Never reached -- HasPawnsTab is false -- but kept total: a null widget falls back to the
        // Items profile rather than throwing if some caller ever asks anyway.
        public TransferableTableColumns.WidgetView PawnsView => TransferableTableColumns.ViewFor(
            null, TransferableTableColumns.ColumnProfile.PodItems, FallbackTile);

        public TransferableTableColumns.WidgetView ItemsView => TransferableTableColumns.ViewFor(
            itemsTransferField?.GetValue(window) as TransferableOneWayWidget,
            TransferableTableColumns.ColumnProfile.PodItems, FallbackTile);

        // Dialog_LoadCargo passes sourceCountDesc as a literal null when it builds its
        // TransferableOneWayWidget, and the widget stores exactly what it is given -- there is no
        // generic fallback key. The count column genuinely has no tooltip for this dialog.
        public string CountColumnTooltip => null;

        public bool HasPawnsTab => false;

        private Pawn GetVehicle()
        {
            if (vehicleField == null)
                return null;
            try
            {
                return vehicleField.GetValue(window) as Pawn;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfCargoDialogAdapter.GetVehicle failed: {ex.Message}");
                return null;
            }
        }
    }
}
