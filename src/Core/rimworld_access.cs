using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    [StaticConstructorOnStartup]
    public static class RimWorldAccessMod
    {
        /// <summary>
        /// Matches About.xml's packageId deliberately. Harmony keeps no mapping from a patch
        /// owner back to a mod, so this string is the ONLY name other mods can show a player
        /// when they list who else patched a method (Rimworld Together's compatibility check
        /// does exactly that) — the previous value dated from when this mod was only a
        /// main-menu keyboard helper and named neither the mod nor its author. Logs and patch
        /// listings from before this change refer to the old id.
        /// </summary>
        public const string HarmonyId = "aaronr7734.rimworldaccess";

        public static Harmony HarmonyInstance { get; private set; }

        static RimWorldAccessMod()
        {
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

            HarmonyInstance = new Harmony(HarmonyId);

            // RETIRED: TypeaheadConsumerRegistry.RegisterAll()
            // (called twice here, verbatim duplicate) — the registry it populated is deleted;
            // see ImeFunnel.RouteImeCommittedChar and FocusStack.OfferChar for the successor.

            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

            // DLC-safe: patch Start() on each Dialog_BeginLordJob subclass we don't already
            // patch declaratively. The shared prefix returns false while LordJobDialogState is
            // active, blocking accidental Start invocations behind the user's back.
            ApplyLordJobStartPatches(HarmonyInstance);
            List<string> compat = CompatBootstrap.ActivateAll(HarmonyInstance);
            int patchCount = HarmonyInstance.GetPatchedMethods().Count();

            // Counted separately: this sweep patches only the methods that hold a certified site.
            TooltipGateInstaller.Install(HarmonyInstance);
            TooltipGateModScan.Install(HarmonyInstance);

            Log.Message("[RimWorld Access] Loaded. Screen reader: " + TolkHelper.DescribeBackend()
                + ". Harmony patches: " + patchCount
                + ". Compat modules: " + (compat.Count == 0 ? "none" : string.Join(", ", compat)) + ".");

            // Settings are loaded by the Mod constructor (runs before this), so we can now compare
            // the persisted last-seen version against the current one and flag whether to surface the
            // "What's New" message when the player reaches the main menu.
            WhatsNewState.CheckForUpdateAtStartup();

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
            }
        }

        private static void OnApplicationQuit()
        {
            TolkHelper.Shutdown();
        }
    }
}
