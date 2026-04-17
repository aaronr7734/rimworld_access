using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    [StaticConstructorOnStartup]
    public static class RimWorldAccessMod
    {
        public static readonly string HarmonyId = "com.rimworldaccess.mainmenukeyboard";

        public static Harmony HarmonyInstance { get; private set; }

        // Path to the mod DLL on disk. Captured during Initialize so that hot-reload
        // (which byte-loads the assembly, leaving Assembly.Location empty) can still
        // find the DLL on subsequent reload passes.
        public static string DllPath { get; private set; }

        static RimWorldAccessMod()
        {
            // Skip initialization when the assembly was byte-loaded (hot-reload path).
            // In that case, Assembly.Location is empty and the reload caller will
            // invoke Initialize(dllPath) explicitly with the correct path.
            if (string.IsNullOrEmpty(Assembly.GetExecutingAssembly().Location))
            {
                Log.Message("[RimWorld Access] Assembly byte-loaded; awaiting explicit Initialize call.");
                return;
            }
            Initialize();
        }

        public static void Initialize(string dllPathOverride = null)
        {
            Log.Message("[RimWorld Access] Initializing accessibility features...");

            string dllPath = string.IsNullOrEmpty(dllPathOverride)
                ? Assembly.GetExecutingAssembly().Location
                : dllPathOverride;
            DllPath = dllPath;

            string modRoot = string.IsNullOrEmpty(dllPath)
                ? null
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(dllPath), ".."));

            try
            {
                TolkHelper.Initialize(modRoot);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Failed to initialize Prism screen reader integration: {ex.Message}");
                Log.Error("[RimWorld Access] The mod will not function without the Prism native library");
                return;
            }

            HarmonyInstance = new Harmony(HarmonyId);

            Log.Message("[RimWorld Access] Applying Harmony patches...");
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

            ApplyGravshipStartPatch(HarmonyInstance);

            var patchedMethods = HarmonyInstance.GetPatchedMethods();
            int patchCount = 0;
            foreach (var method in patchedMethods)
            {
                patchCount++;
                Log.Message($"[RimWorld Access] Patched: {method.DeclaringType?.Name}.{method.Name}");
            }
            Log.Message($"[RimWorld Access] Total patches applied: {patchCount}");

            Log.Message("[RimWorld Access] Main menu keyboard navigation enabled!");
            Log.Message("[RimWorld Access] Use Arrow keys to navigate, Enter to select.");

            Application.quitting += OnApplicationQuit;
        }

        public static void Teardown()
        {
            Log.Message("[RimWorld Access] Tearing down for reload...");

            Application.quitting -= OnApplicationQuit;

            if (HarmonyInstance != null)
            {
                HarmonyInstance.UnpatchAll(HarmonyId);
                HarmonyInstance = null;
            }

            TolkHelper.Shutdown();
        }

        private static void ApplyGravshipStartPatch(Harmony harmony)
        {
            var gravshipDialogType = AccessTools.TypeByName("RimWorld.Dialog_BeginGravshipLaunch");
            if (gravshipDialogType == null) return;

            var startMethod = AccessTools.Method(gravshipDialogType, "Start");
            if (startMethod == null) return;

            var prefixMethod = AccessTools.Method(typeof(RitualPatch), nameof(RitualPatch.GravshipStartPrefix));
            harmony.Patch(startMethod, prefix: new HarmonyMethod(prefixMethod));
            Log.Message("[RimWorld Access] Applied gravship launch Start() patch");
        }

        private static void OnApplicationQuit()
        {
            Log.Message("[RimWorld Access] Shutting down...");
            TolkHelper.Shutdown();
        }
    }
}
