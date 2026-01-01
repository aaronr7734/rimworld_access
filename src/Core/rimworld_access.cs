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

            // Initialize Tolk screen reader integration
            try
            {
                TolkHelper.Initialize();
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Failed to initialize Tolk screen reader integration: {ex.Message}");
                Log.Error("[RimWorld Access] The mod will not function without Tolk.dll");
                return;
            }

            // Apply Harmony patches
            var harmony = new Harmony(HarmonyId);

            Log.Message("[RimWorld Access] Applying Harmony patches...");
            harmony.PatchAll();

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

        private static void OnApplicationQuit()
        {
            Log.Message("[RimWorld Access] Shutting down...");
            TolkHelper.Shutdown();
        }
    }
}
