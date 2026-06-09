using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    [StaticConstructorOnStartup]
    public static class RimWorldAccessMod
    {
        public static readonly string HarmonyId = "com.rimworldaccess.mainmenukeyboard";

        static RimWorldAccessMod()
        {
            Log.Message("[RimWorld Access] Initializing accessibility features...");

            // Initialize Prism screen reader integration
            try
            {
                TolkHelper.Initialize();
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Failed to initialize Prism screen reader integration: {ex.Message}");
                Log.Error("[RimWorld Access] The mod will not function without the Prism native library");
                return;
            }

            // Apply Harmony patches
            var harmony = new Harmony(HarmonyId);

            // Register all typeahead-consuming States so the priority -1.5 dispatch in
            // UnifiedKeyboardPatch can route layout-aware characters from non-Latin
            // keyboard layouts (fixes the OlegTheSnowman Cyrillic-typeahead bug).
            TypeaheadConsumerRegistry.RegisterAll();

            Log.Message("[RimWorld Access] Applying Harmony patches...");
            harmony.PatchAll();

            // DLC-safe: patch Start() on each Dialog_BeginLordJob subclass we don't already
            // patch declaratively. The shared prefix returns false while LordJobDialogState is
            // active, blocking accidental Start invocations behind the user's back.
            ApplyLordJobStartPatches(harmony);
            ApplyVehicleFrameworkPatches(harmony);

            // Log which patches were applied
            var patchedMethods = harmony.GetPatchedMethods();
            int patchCount = 0;
            foreach (var method in patchedMethods)
            {
                patchCount++;
                Log.Message($"[RimWorld Access] Patched: {method.DeclaringType?.Name}.{method.Name}");
            }
            Log.Message($"[RimWorld Access] Total patches applied: {patchCount}");

            Log.Message("[RimWorld Access] Main menu keyboard navigation enabled!");
            Log.Message("[RimWorld Access] Use Arrow keys to navigate, Enter to select.");

            // Register shutdown handler
            Application.quitting += OnApplicationQuit;
        }

        private static void ApplyLordJobStartPatches(Harmony harmony)
        {
            var prefix = AccessTools.Method(typeof(RitualPatch), nameof(RitualPatch.LordJobStartPrefix));
            if (prefix == null) return;

            // Dialog_BeginRitual.Start is patched declaratively in RitualPatch.
            // Patch the other two subclasses here so virtual dispatch hits each override.
            string[] additionalDialogTypes =
            {
                "RimWorld.Dialog_BeginPsychicRitual",
                "RimWorld.Dialog_BeginGravshipLaunch",
            };

            foreach (var typeName in additionalDialogTypes)
            {
                var t = AccessTools.TypeByName(typeName);
                if (t == null) continue;
                var startMethod = AccessTools.Method(t, "Start");
                if (startMethod == null) continue;
                harmony.Patch(startMethod, prefix: new HarmonyMethod(prefix));
                Log.Message($"[RimWorld Access] Applied {typeName}.Start() prefix");
            }
        }

        private static void ApplyVehicleFrameworkPatches(Harmony harmony)
        {
            var trySendType = AccessTools.TypeByName("Vehicles.World.CaravanFormation");
            var trySendMethod = AccessTools.Method(trySendType, "TrySendVehicleCaravan");
            var trySendPrefix = AccessTools.Method(
                typeof(VehicleFrameworkPatches),
                nameof(VehicleFrameworkPatches.TrySendVehicleCaravanPrefix));
            if (trySendMethod != null && trySendPrefix != null)
            {
                harmony.Patch(trySendMethod, prefix: new HarmonyMethod(trySendPrefix));
                Log.Message("[RimWorld Access] Applied Vehicle Framework caravan send prefix");
            }

            var formationInfoType = AccessTools.TypeByName("Vehicles.World.FormationInfo");
            var recacheMethod = AccessTools.Method(formationInfoType, "RecacheTransferables");
            var recachePostfix = AccessTools.Method(
                typeof(VehicleFrameworkPatches),
                nameof(VehicleFrameworkPatches.RecacheTransferablesPostfix));
            if (recacheMethod != null && recachePostfix != null)
            {
                harmony.Patch(recacheMethod, postfix: new HarmonyMethod(recachePostfix));
                Log.Message("[RimWorld Access] Applied Vehicle Framework recache postfix");
            }

            var loadCargoType = AccessTools.TypeByName("Vehicles.Dialog_LoadCargo");
            var loadCargoPostOpenMethod = AccessTools.Method(loadCargoType, "PostOpen");
            var loadCargoPostOpenPostfix = AccessTools.Method(
                typeof(VehicleFrameworkPatches),
                nameof(VehicleFrameworkPatches.LoadCargoPostOpenPostfix));
            if (loadCargoPostOpenMethod != null && loadCargoPostOpenPostfix != null)
            {
                harmony.Patch(loadCargoPostOpenMethod, postfix: new HarmonyMethod(loadCargoPostOpenPostfix));
                Log.Message("[RimWorld Access] Applied Vehicle Framework load cargo postfix");
            }

            var launcherType = AccessTools.TypeByName("Vehicles.CompVehicleLauncher");
            var startChoosingMethod = AccessTools.Method(launcherType, "StartChoosingDestination");
            var startChoosingPostfix = AccessTools.Method(
                typeof(VehicleFrameworkPatches),
                nameof(VehicleFrameworkPatches.AerialVehicleStartChoosingDestinationPostfix));
            if (startChoosingMethod != null && startChoosingPostfix != null)
            {
                harmony.Patch(startChoosingMethod, postfix: new HarmonyMethod(startChoosingPostfix));
                Log.Message("[RimWorld Access] Applied Vehicle Framework aerial launch postfix");
            }

            var aerialVehicleType = AccessTools.TypeByName("Vehicles.World.AerialVehicleInFlight");
            var aerialStartTargetingMethod = AccessTools.Method(aerialVehicleType, "StartTargeting");
            var aerialStartTargetingPostfix = AccessTools.Method(
                typeof(VehicleFrameworkPatches),
                nameof(VehicleFrameworkPatches.AerialVehicleInFlightStartTargetingPostfix));
            if (aerialStartTargetingMethod != null && aerialStartTargetingPostfix != null)
            {
                harmony.Patch(aerialStartTargetingMethod, postfix: new HarmonyMethod(aerialStartTargetingPostfix));
                Log.Message("[RimWorld Access] Applied Vehicle Framework in-flight targeting postfix");
            }

            var vehicleCaravanType = AccessTools.TypeByName("Vehicles.World.VehicleCaravan");
            var vehicleCaravanLaunchMethod = AccessTools.Method(vehicleCaravanType, "LaunchAerialVehicle");
            var vehicleCaravanLaunchPrefix = AccessTools.Method(
                typeof(VehicleFrameworkPatches),
                nameof(VehicleFrameworkPatches.VehicleCaravanLaunchAerialVehiclePrefix));
            if (vehicleCaravanLaunchMethod != null && vehicleCaravanLaunchPrefix != null)
            {
                harmony.Patch(vehicleCaravanLaunchMethod, prefix: new HarmonyMethod(vehicleCaravanLaunchPrefix));
                Log.Message("[RimWorld Access] Applied Vehicle Framework vehicle caravan launch prefix");
            }

            var landingTargeterType = AccessTools.TypeByName("Vehicles.LandingTargeter");
            var landingBeginTargetingMethod = AccessTools.Method(landingTargeterType, "BeginTargeting");
            var landingBeginTargetingPostfix = AccessTools.Method(
                typeof(VehicleFrameworkPatches),
                nameof(VehicleFrameworkPatches.AerialVehicleLandingBeginTargetingPostfix));
            if (landingBeginTargetingMethod != null && landingBeginTargetingPostfix != null)
            {
                harmony.Patch(landingBeginTargetingMethod, postfix: new HarmonyMethod(landingBeginTargetingPostfix));
                Log.Message("[RimWorld Access] Applied Vehicle Framework landing targeter postfix");
            }

            var orientationControllerType = AccessTools.TypeByName("Vehicles.VehicleOrientationController");
            var orientationStartMethod = AccessTools.Method(orientationControllerType, "OnStart");
            var orientationStopMethod = AccessTools.Method(orientationControllerType, "StopTargeting");
            var orientationStartPostfix = AccessTools.Method(
                typeof(VehicleFrameworkPatches),
                nameof(VehicleFrameworkPatches.VehicleOrientationControllerStartPostfix));
            var orientationStopPostfix = AccessTools.Method(
                typeof(VehicleFrameworkPatches),
                nameof(VehicleFrameworkPatches.VehicleOrientationControllerStopPostfix));
            if (orientationStartMethod != null && orientationStartPostfix != null)
            {
                harmony.Patch(orientationStartMethod, postfix: new HarmonyMethod(orientationStartPostfix));
                Log.Message("[RimWorld Access] Applied Vehicle Framework orientation controller start postfix");
            }
            if (orientationStopMethod != null && orientationStopPostfix != null)
            {
                harmony.Patch(orientationStopMethod, postfix: new HarmonyMethod(orientationStopPostfix));
                Log.Message("[RimWorld Access] Applied Vehicle Framework orientation controller stop postfix");
            }

            var arrivalOptionType = AccessTools.TypeByName("Vehicles.World.ArrivalOption");
            var smashWorldTargeterGeneric = AccessTools.TypeByName("SmashTools.Targeting.WorldTargeter`1");
            if (arrivalOptionType != null && smashWorldTargeterGeneric != null)
            {
                var smashWorldTargeterType = smashWorldTargeterGeneric.MakeGenericType(arrivalOptionType);
                var smashStartMethod = AccessTools.Method(smashWorldTargeterType, "Start");
                var smashStopMethod = AccessTools.Method(smashWorldTargeterType, "StopTargeting");
                var smashStartPostfix = AccessTools.Method(
                    typeof(VehicleFrameworkPatches),
                    nameof(VehicleFrameworkPatches.AerialVehicleWorldTargeterStartPostfix));
                var smashStopPostfix = AccessTools.Method(
                    typeof(VehicleFrameworkPatches),
                    nameof(VehicleFrameworkPatches.AerialVehicleWorldTargeterStopPostfix));
                if (smashStartMethod != null && smashStartPostfix != null)
                {
                    harmony.Patch(smashStartMethod, postfix: new HarmonyMethod(smashStartPostfix));
                    Log.Message("[RimWorld Access] Applied Vehicle Framework aerial world targeter start postfix");
                }
                if (smashStopMethod != null && smashStopPostfix != null)
                {
                    harmony.Patch(smashStopMethod, postfix: new HarmonyMethod(smashStopPostfix));
                    Log.Message("[RimWorld Access] Applied Vehicle Framework aerial world targeter stop postfix");
                }
            }
        }

        private static void OnApplicationQuit()
        {
            Log.Message("[RimWorld Access] Shutting down...");
            TolkHelper.Shutdown();
        }
    }
}
