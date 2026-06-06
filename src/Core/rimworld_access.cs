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

            // Register all typeahead-consuming States so the priority -1.5 dispatch in
            // UnifiedKeyboardPatch can route layout-aware characters from non-Latin
            // keyboard layouts (fixes the OlegTheSnowman Cyrillic-typeahead bug).
            TypeaheadConsumerRegistry.RegisterAll();

            // Register all typeahead-consuming States so the priority -1.5 dispatch in
            // UnifiedKeyboardPatch can route layout-aware characters from non-Latin
            // keyboard layouts (fixes the OlegTheSnowman Cyrillic-typeahead bug).
            TypeaheadConsumerRegistry.RegisterAll();

            Log.Message("[RimWorld Access] Applying Harmony patches...");
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

            // DLC-safe: patch Start() on each Dialog_BeginLordJob subclass we don't already
            // patch declaratively. The shared prefix returns false while LordJobDialogState is
            // active, blocking accidental Start invocations behind the user's back.
            ApplyLordJobStartPatches(harmony);

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

        private static void OnApplicationQuit()
        {
            Log.Message("[RimWorld Access] Shutting down...");
            TolkHelper.Shutdown();
        }
    }
}
