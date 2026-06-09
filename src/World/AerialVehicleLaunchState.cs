using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public static class AerialVehicleLaunchState
    {
        private const string ArrivalOptionTypeName = "Vehicles.World.ArrivalOption";

        private static bool isActive = false;
        private static object currentSource = null;
        private static object currentVehicle = null;
        private static PlanetTile originTile = PlanetTile.Invalid;
        private static bool isChoosingArrivalOption = false;
        private static List<object> arrivalOptions = new List<object>();
        private static int selectedOptionIndex = 0;
        private static GlobalTargetInfo selectedTarget = GlobalTargetInfo.Invalid;
        private static object activeWorldTargeter = null;
        private static object pendingSource = null;
        private static object pendingVehicle = null;
        private static PlanetTile pendingOriginTile = PlanetTile.Invalid;

        public static bool IsActive => isActive;
        public static bool IsChoosingArrivalOption => isChoosingArrivalOption;
        public static PlanetTile OriginTile => originTile;
        public static bool HasPendingVehicleLaunch => pendingSource != null;

        public static void OpenFromLauncher(object launcher)
        {
            if (launcher == null)
                return;

            object vehicle = GetProperty(launcher, "Vehicle");
            PlanetTile tile = GetPlanetTileProperty(launcher, "Tile");
            Open(launcher, vehicle, tile);
        }

        public static void OpenFromAerialVehicle(object aerialVehicle)
        {
            if (aerialVehicle == null)
                return;

            object vehicle = GetProperty(aerialVehicle, "Vehicle");
            PlanetTile tile = GetPlanetTileProperty(aerialVehicle, "Tile");
            Open(aerialVehicle, vehicle, tile);
        }

        public static void OpenFromVehicleCaravan(object vehicleCaravan)
        {
            if (vehicleCaravan == null)
                return;

            object vehicle = GetProperty(vehicleCaravan, "LeadVehicle")
                ?? GetProperty(vehicleCaravan, "Vehicle")
                ?? GetProperty(vehicleCaravan, "leadVehicle");
            PlanetTile tile = GetPlanetTileProperty(vehicleCaravan, "Tile");
            Open(vehicleCaravan, vehicle, tile);
        }

        public static void PrepareForVehicleCaravanLaunch(object vehicleCaravan)
        {
            if (vehicleCaravan == null)
                return;

            object vehicle = GetProperty(vehicleCaravan, "LeadVehicle")
                ?? GetProperty(vehicleCaravan, "Vehicle")
                ?? GetProperty(vehicleCaravan, "leadVehicle");
            PlanetTile tile = GetPlanetTileProperty(vehicleCaravan, "Tile");

            pendingSource = vehicleCaravan;
            pendingVehicle = vehicle;
            pendingOriginTile = tile;
        }

        public static void ClearPendingVehicleLaunch()
        {
            pendingSource = null;
            pendingVehicle = null;
            pendingOriginTile = PlanetTile.Invalid;
        }

        public static bool TryActivatePendingVehicleLaunch()
        {
            if (isActive || pendingSource == null)
                return false;

            object currentWorldTargeter = null;
            try
            {
                Type targetersType = AccessTools.TypeByName("Vehicles.Targeters");
                currentWorldTargeter = AccessTools.Property(targetersType, "CurrentWorldTargeter")?.GetValue(null, null)
                    ?? AccessTools.Field(targetersType, "CurrentWorldTargeter")?.GetValue(null);
            }
            catch { }

            if (currentWorldTargeter == null)
                return false;

            object source = pendingSource;
            object vehicle = pendingVehicle;
            PlanetTile tile = pendingOriginTile;
            pendingSource = null;
            pendingVehicle = null;
            pendingOriginTile = PlanetTile.Invalid;

            activeWorldTargeter = currentWorldTargeter;
            Open(source, vehicle, tile);
            return true;
        }

        private static void Open(object source, object vehicle, PlanetTile tile)
        {
            currentSource = source;
            currentVehicle = vehicle;
            originTile = tile;
            isActive = true;
            isChoosingArrivalOption = false;
            arrivalOptions.Clear();
            selectedOptionIndex = 0;
            selectedTarget = GlobalTargetInfo.Invalid;

            if (!WorldNavigationState.IsActive)
            {
                WorldNavigationState.Open();
            }
            else if (WorldNavigationState.IsInitialized)
            {
                WorldNavigationState.AnnounceTile();
            }

            string label = GetThingLabel(vehicle) ?? "aerial vehicle";
            string range = GetRangeSummary(vehicle);
            TolkHelper.Speak($"{label} flight targeting. Use world navigation or scanner to choose destination. Enter selects destination, Escape cancels.{range}");
        }

        public static void Close()
        {
            isActive = false;
            currentSource = null;
            currentVehicle = null;
            originTile = PlanetTile.Invalid;
            isChoosingArrivalOption = false;
            arrivalOptions.Clear();
            selectedOptionIndex = 0;
            selectedTarget = GlobalTargetInfo.Invalid;
            activeWorldTargeter = null;
            pendingSource = null;
            pendingVehicle = null;
            pendingOriginTile = PlanetTile.Invalid;
        }

        public static void SetActiveWorldTargeter(object targeter)
        {
            activeWorldTargeter = targeter;

            if (!isActive)
            {
                TryOpenFromPendingOrTargeter(targeter);
            }
        }

        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive)
                return false;

            if (isChoosingArrivalOption)
                return HandleArrivalOptionInput(key, shift, ctrl, alt);

            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !shift && !ctrl && !alt)
            {
                ConfirmCurrentDestination();
                return true;
            }

            if (key == KeyCode.Escape && !shift && !ctrl && !alt)
            {
                CancelTargeting();
                return true;
            }

            if (key == KeyCode.F && !shift && !ctrl && !alt)
            {
                AnnounceDestinationInfo();
                return true;
            }

            return false;
        }

        private static bool HandleArrivalOptionInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (key == KeyCode.UpArrow && !shift && !ctrl && !alt)
            {
                selectedOptionIndex = MenuHelper.SelectPrevious(selectedOptionIndex, arrivalOptions.Count);
                AnnounceArrivalOption();
                return true;
            }

            if (key == KeyCode.DownArrow && !shift && !ctrl && !alt)
            {
                selectedOptionIndex = MenuHelper.SelectNext(selectedOptionIndex, arrivalOptions.Count);
                AnnounceArrivalOption();
                return true;
            }

            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !shift && !ctrl && !alt)
            {
                ExecuteSelectedArrivalOption();
                return true;
            }

            if (key == KeyCode.Escape && !shift && !ctrl && !alt)
            {
                isChoosingArrivalOption = false;
                arrivalOptions.Clear();
                selectedOptionIndex = 0;
                TolkHelper.Speak("Arrival option cancelled. Choose another destination.");
                return true;
            }

            return false;
        }

        private static void ConfirmCurrentDestination()
        {
            PlanetTile tile = WorldNavigationState.CurrentSelectedTile;
            if (!tile.Valid)
            {
                TolkHelper.Speak("No valid destination selected", SpeechPriority.High);
                return;
            }

            GlobalTargetInfo target = BuildGlobalTargetInfo(tile);
            if (!TargetCanBeSelected(target))
                return;

            List<object> options = GetArrivalOptions(target);
            if (options.Count == 0)
            {
                TolkHelper.Speak("No arrival options for this destination", SpeechPriority.High);
                return;
            }

            selectedTarget = target;
            arrivalOptions = options;
            selectedOptionIndex = 0;

            if (arrivalOptions.Count == 1)
            {
                ExecuteSelectedArrivalOption();
                return;
            }

            isChoosingArrivalOption = true;
            TolkHelper.Speak($"{arrivalOptions.Count} arrival options.");
            AnnounceArrivalOption();
        }

        private static bool TargetCanBeSelected(GlobalTargetInfo target)
        {
            object result = InvokeCanTarget(target, out bool invoked);
            if (!invoked)
                return true;

            try
            {
                if (result == null)
                    return true;

                bool accepted = GetBoolProperty(result, "Accepted", true);
                if (accepted)
                    return true;

                string reason = GetProperty(result, "Tooltip")?.ToString();
                if (reason.NullOrEmpty())
                    reason = "Invalid destination";
                TolkHelper.Speak(reason.StripTags(), SpeechPriority.High);
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to validate aerial vehicle destination: {ex.Message}");
                return true;
            }
        }

        private static GlobalTargetInfo BuildGlobalTargetInfo(PlanetTile tile)
        {
            List<WorldObject> objects = Find.WorldObjects?.ObjectsAt(tile)?.ToList();
            if (objects != null && objects.Count > 0)
                return new GlobalTargetInfo(objects[0]);

            return new GlobalTargetInfo(tile);
        }

        private static List<object> GetArrivalOptions(GlobalTargetInfo target)
        {
            List<object> options = new List<object>();
            object result = InvokeOptionsAt(target, out bool invoked);
            if (!invoked)
                return options;

            try
            {
                IEnumerable enumerable = result as IEnumerable;
                if (enumerable == null)
                    return options;

                foreach (object option in enumerable)
                {
                    if (option != null && option.GetType().FullName == ArrivalOptionTypeName)
                        options.Add(option);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to get aerial vehicle arrival options: {ex.Message}");
            }

            return options;
        }

        private static object InvokeCanTarget(GlobalTargetInfo target, out bool invoked)
        {
            invoked = false;
            if (currentSource == null)
                return null;

            try
            {
                MethodInfo direct = AccessTools.Method(currentSource.GetType(), "CanTarget", new[] { typeof(GlobalTargetInfo) });
                if (direct != null)
                {
                    invoked = true;
                    return direct.Invoke(currentSource, new object[] { target });
                }

                Type arrivalOptionType = AccessTools.TypeByName(ArrivalOptionTypeName);
                Type targeterSourceGeneric = AccessTools.TypeByName("SmashTools.Targeting.ITargeterSource`2");
                if (arrivalOptionType == null || targeterSourceGeneric == null)
                    return null;

                Type iface = targeterSourceGeneric.MakeGenericType(typeof(GlobalTargetInfo), arrivalOptionType);
                MethodInfo interfaceMethod = iface.GetMethod("CanTarget", new[] { typeof(GlobalTargetInfo) });
                if (interfaceMethod == null || !iface.IsAssignableFrom(currentSource.GetType()))
                    return null;

                invoked = true;
                return interfaceMethod.Invoke(currentSource, new object[] { target });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to invoke aerial vehicle destination validation: {ex.Message}");
                return null;
            }
        }

        private static object InvokeOptionsAt(GlobalTargetInfo target, out bool invoked)
        {
            invoked = false;
            if (currentSource == null)
                return null;

            try
            {
                MethodInfo direct = AccessTools.Method(currentSource.GetType(), "OptionsAt", new[] { typeof(GlobalTargetInfo) });
                if (direct != null)
                {
                    invoked = true;
                    return direct.Invoke(currentSource, new object[] { target });
                }

                Type launcherInterface = AccessTools.TypeByName("Vehicles.ILauncher");
                MethodInfo interfaceMethod = launcherInterface?.GetMethod("OptionsAt", new[] { typeof(GlobalTargetInfo) });
                if (interfaceMethod == null || launcherInterface == null || !launcherInterface.IsAssignableFrom(currentSource.GetType()))
                    return null;

                invoked = true;
                return interfaceMethod.Invoke(currentSource, new object[] { target });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to invoke aerial vehicle arrival options: {ex.Message}");
                return null;
            }
        }

        private static void ExecuteSelectedArrivalOption()
        {
            if (selectedOptionIndex < 0 || selectedOptionIndex >= arrivalOptions.Count)
                return;

            object option = arrivalOptions[selectedOptionIndex];
            if (!IsArrivalOptionAccepted(option))
            {
                TolkHelper.Speak($"{GetArrivalOptionLabel(option)}, unavailable", SpeechPriority.High);
                return;
            }

            try
            {
                object targetData = BuildTargetData(selectedTarget);
                object continueWith = AccessTools.Field(option.GetType(), "continueWith")?.GetValue(option);
                if (continueWith is Delegate continuation)
                {
                    continuation.DynamicInvoke(targetData);
                }
                else
                {
                    object arrivalAction = AccessTools.Field(option.GetType(), "arrivalAction")?.GetValue(option);
                    MethodInfo launch = AccessTools.Method(currentSource.GetType(), "Launch");
                    if (launch == null || arrivalAction == null)
                    {
                        TolkHelper.Speak("Arrival option cannot be executed", SpeechPriority.High);
                        return;
                    }
                    launch.Invoke(currentSource, new[] { targetData, arrivalAction });
                }

                string label = GetArrivalOptionLabel(option);
                StopActiveWorldTargeter();
                Close();
                isChoosingArrivalOption = false;
                arrivalOptions.Clear();
                selectedOptionIndex = 0;
                TolkHelper.Speak($"{label} selected");
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to execute aerial vehicle arrival option: {ex}");
                TolkHelper.Speak("Could not execute arrival option", SpeechPriority.High);
            }
        }

        private static object BuildTargetData(GlobalTargetInfo target)
        {
            Type targetDataType = AccessTools.TypeByName("SmashTools.Targeting.TargetData`1")
                ?.MakeGenericType(typeof(GlobalTargetInfo));
            object targetData = Activator.CreateInstance(targetDataType);
            object targets = AccessTools.Field(targetDataType, "targets")?.GetValue(targetData)
                ?? AccessTools.Property(targetDataType, "targets")?.GetValue(targetData, null)
                ?? AccessTools.Property(targetDataType, "Targets")?.GetValue(targetData, null);

            if (targets is IList list)
                list.Add(target);

            return targetData;
        }

        private static bool IsArrivalOptionAccepted(object option)
        {
            object report = AccessTools.Property(option.GetType(), "AcceptanceReport")?.GetValue(option, null);
            return report == null || GetBoolProperty(report, "Accepted", true);
        }

        private static void AnnounceArrivalOption()
        {
            if (arrivalOptions.Count == 0)
                return;

            object option = arrivalOptions[selectedOptionIndex];
            string label = GetArrivalOptionLabel(option);
            string unavailable = IsArrivalOptionAccepted(option) ? "" : ", unavailable";
            TolkHelper.Speak($"{label}{unavailable}. {MenuHelper.FormatPosition(selectedOptionIndex, arrivalOptions.Count)}");
        }

        private static string GetArrivalOptionLabel(object option)
        {
            object label = AccessTools.Field(option.GetType(), "label")?.GetValue(option);
            return label?.ToString().StripTags() ?? "Arrival option";
        }

        private static void AnnounceDestinationInfo()
        {
            PlanetTile tile = WorldNavigationState.CurrentSelectedTile;
            if (!tile.Valid)
            {
                TolkHelper.Speak("No valid destination selected");
                return;
            }

            string info = GetDestinationInfo(tile);
            TolkHelper.Speak(info.NullOrEmpty() ? "Destination available" : info);
        }

        public static string GetDestinationInfo(PlanetTile tile)
        {
            if (!isActive || !tile.Valid)
                return null;

            GlobalTargetInfo target = BuildGlobalTargetInfo(tile);
            string fuel = GetFuelCostSummary(target);
            if (!fuel.NullOrEmpty())
                return fuel;

            return null;
        }

        public static bool ShouldAnnounceFuelCosts()
        {
            return isActive;
        }

        private static string GetFuelCostSummary(GlobalTargetInfo target)
        {
            object launcher = GetProperty(currentVehicle, "CompVehicleLauncher") ?? currentSource;
            MethodInfo fuelNeeded = AccessTools.Method(launcher?.GetType(), "FuelNeededToLaunchAtDist", new[] { typeof(PlanetTile) });
            if (fuelNeeded == null || !target.Tile.Valid)
                return null;

            try
            {
                float needed = Convert.ToSingle(fuelNeeded.Invoke(launcher, new object[] { target.Tile }));
                object fuelComp = GetProperty(currentVehicle, "CompFueledTravel");
                float available = ToFloat(GetProperty(fuelComp, "Fuel"), -1f);
                if (available >= 0f)
                    return needed > available
                        ? $"NOT ENOUGH FUEL, need {needed:F0}, have {available:F0}"
                        : $"{needed:F0} fuel needed, {available:F0} available";

                return $"{needed:F0} fuel needed";
            }
            catch
            {
                return null;
            }
        }

        private static string GetRangeSummary(object vehicle)
        {
            object launcher = GetProperty(vehicle, "CompVehicleLauncher") ?? currentSource;
            int maxDistance = ToInt(GetProperty(launcher, "MaxLaunchDistance"), 0);
            float fuel = ToFloat(GetProperty(GetProperty(vehicle, "CompFueledTravel"), "Fuel"), -1f);
            List<string> parts = new List<string>();
            if (maxDistance > 0 && maxDistance < int.MaxValue)
                parts.Add($"max range {maxDistance} tiles");
            if (fuel >= 0f)
                parts.Add($"{fuel:F0} fuel available");
            return parts.Count == 0 ? "" : $" {string.Join(", ", parts)}.";
        }

        private static void CancelTargeting()
        {
            CloseVehicleFrameworkWorldTargeters();
            Close();
            TolkHelper.Speak("Flight targeting cancelled");
            CameraJumper.TryHideWorld();
        }

        public static void CloseVehicleFrameworkWorldTargeters()
        {
            StopActiveWorldTargeter();

            try
            {
                Type targeters = AccessTools.TypeByName("Vehicles.Targeters");
                object current = AccessTools.Property(targeters, "CurrentWorldTargeter")?.GetValue(null, null)
                    ?? AccessTools.Field(targeters, "CurrentWorldTargeter")?.GetValue(null);
                AccessTools.Method(current?.GetType(), "StopTargeting")?.Invoke(current, null);
            }
            catch { }
        }

        private static void StopActiveWorldTargeter()
        {
            try
            {
                AccessTools.Method(activeWorldTargeter?.GetType(), "StopTargeting")?.Invoke(activeWorldTargeter, null);
            }
            catch { }

            activeWorldTargeter = null;
        }

        private static void TryOpenFromPendingOrTargeter(object targeter)
        {
            if (pendingSource != null)
            {
                object source = pendingSource;
                object vehicle = pendingVehicle;
                PlanetTile tile = pendingOriginTile;
                pendingSource = null;
                pendingVehicle = null;
                pendingOriginTile = PlanetTile.Invalid;
                activeWorldTargeter = targeter;
                Open(source, vehicle, tile);
                return;
            }

            object sourceFromTargeter = GetProperty(targeter, "source")
                ?? GetProperty(targeter, "Source")
                ?? GetProperty(targeter, "targeterSource");
            if (sourceFromTargeter == null)
                return;

            string typeName = sourceFromTargeter.GetType().FullName ?? string.Empty;
            if (typeName == "Vehicles.World.VehicleCaravan")
            {
                object vehicle = GetProperty(sourceFromTargeter, "LeadVehicle")
                    ?? GetProperty(sourceFromTargeter, "Vehicle");
                PlanetTile tile = GetPlanetTileProperty(sourceFromTargeter, "Tile");
                Open(sourceFromTargeter, vehicle, tile);
            }
            else if (typeName == "Vehicles.World.AerialVehicleInFlight")
            {
                OpenFromAerialVehicle(sourceFromTargeter);
            }
            else if (typeName == "Vehicles.CompVehicleLauncher")
            {
                OpenFromLauncher(sourceFromTargeter);
            }
        }

        private static object GetProperty(object instance, string name)
        {
            if (instance == null || name.NullOrEmpty())
                return null;

            PropertyInfo property = AccessTools.Property(instance.GetType(), name);
            if (property != null)
                return property.GetValue(instance, null);

            FieldInfo field = AccessTools.Field(instance.GetType(), name);
            return field?.GetValue(instance);
        }

        private static PlanetTile GetPlanetTileProperty(object instance, string name)
        {
            object value = GetProperty(instance, name);
            return value is PlanetTile tile ? tile : PlanetTile.Invalid;
        }

        private static bool GetBoolProperty(object instance, string name, bool defaultValue)
        {
            object value = GetProperty(instance, name);
            return value is bool boolValue ? boolValue : defaultValue;
        }

        private static int ToInt(object value, int defaultValue)
        {
            try { return value == null ? defaultValue : Convert.ToInt32(value); }
            catch { return defaultValue; }
        }

        private static float ToFloat(object value, float defaultValue)
        {
            try { return value == null ? defaultValue : Convert.ToSingle(value); }
            catch { return defaultValue; }
        }

        private static string GetThingLabel(object thing)
        {
            return thing is Thing t ? t.LabelShortCap.ToString().StripTags() : null;
        }
    }
}
