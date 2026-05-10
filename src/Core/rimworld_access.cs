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
        }

        private static void OnApplicationQuit()
        {
            Log.Message("[RimWorld Access] Shutting down...");
            TolkHelper.Shutdown();
        }
    }
}
