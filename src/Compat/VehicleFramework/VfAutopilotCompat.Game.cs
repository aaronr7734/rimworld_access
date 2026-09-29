using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimWorldAccess
{
    /// <summary>
    /// Gate for the vehicle think-tree insert (Patches/CombatAutopilotVehicles.xml):
    /// a drafted player vehicle with seek-and-destroy on. VehiclePawn syncs the
    /// vanilla drafter to its ignition, so pawn.Drafted is truthful here. References
    /// no Vehicle Framework types; the XML only applies while that mod is loaded.
    /// </summary>
    public class ThinkNode_ConditionalVehicleAutopilot : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Faction == null
                || !pawn.Faction.IsPlayer || !pawn.Drafted)
            {
                return false;
            }
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings != null && !settings.EnableCombatAutopilot)
            {
                return false;
            }
            CombatAutopilotData data = CombatAutopilotComponent.TryGetData(pawn);
            return data != null && !data.Paused && data.SeekAndDestroy;
        }
    }

    /// <summary>
    /// VehiclePawn.GetGizmos never yields the drafter strip, so the policy picker and
    /// pause checkbox are appended here; the mod's own combat job givers get report
    /// overrides on player vehicles so un-ordered movement always says what it is.
    /// </summary>
    internal static class VfAutopilotCompat
    {
        public static void Register(Harmony harmony)
        {
            try
            {
                Type vehiclePawn = AccessTools.TypeByName("Vehicles.VehiclePawn");
                if (vehiclePawn == null)
                {
                    return;
                }
                MethodInfo getGizmos = AccessTools.Method(vehiclePawn, "GetGizmos");
                if (getGizmos == null)
                {
                    ModLogger.Error("VfAutopilotCompat: VehiclePawn.GetGizmos did not resolve; vehicle policy gizmos disabled.");
                    return;
                }
                harmony.Patch(getGizmos,
                    postfix: new HarmonyMethod(typeof(VfAutopilotCompat), nameof(GizmosPostfix)));

                advanceGiver = PatchJobReport(harmony, "Vehicles.JobGiver_GotoNearestHostile", nameof(AdvanceReportPostfix));
                supportGiver = PatchJobReport(harmony, "Vehicles.JobGiver_RangedSupport", nameof(SupportReportPostfix));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfAutopilotCompat registration failed: {ex.Message}");
            }
        }

        private static Type advanceGiver;
        private static Type supportGiver;

        /// <summary>An inherited TryGiveJob is patched on its declaring type; the postfixes check the instance's type.</summary>
        private static Type PatchJobReport(Harmony harmony, string giverTypeName, string postfixName)
        {
            Type giver = AccessTools.TypeByName(giverTypeName);
            MethodInfo tryGiveJob = giver == null ? null : AccessTools.Method(giver, "TryGiveJob");
            if (tryGiveJob == null || tryGiveJob.IsAbstract)
            {
                ModLogger.Error($"VfAutopilotCompat: {giverTypeName}.TryGiveJob did not resolve; its jobs keep the default report.");
                return null;
            }
            if (tryGiveJob.DeclaringType != giver)
            {
                tryGiveJob = AccessTools.DeclaredMethod(tryGiveJob.DeclaringType, "TryGiveJob");
            }
            harmony.Patch(tryGiveJob,
                postfix: new HarmonyMethod(typeof(VfAutopilotCompat), postfixName));
            return giver;
        }

        public static void GizmosPostfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings != null && !settings.EnableCombatAutopilot)
            {
                return;
            }
            if (__instance == null || !__instance.Spawned || __instance.Faction == null
                || !__instance.Faction.IsPlayer || !__instance.Drafted)
            {
                return;
            }
            __result = Append(__result, __instance);
        }

        private static IEnumerable<Gizmo> Append(IEnumerable<Gizmo> source, Pawn vehicle)
        {
            foreach (Gizmo gizmo in source)
            {
                yield return gizmo;
            }
            // Vehicles have no pawn-table home, so the policy picker stays a gizmo here.
            yield return CombatAutopilotGizmoPatch.PolicyPickerGizmo(vehicle);
            CombatAutopilotData data = CombatAutopilotComponent.TryGetData(vehicle);
            // The policy is the master switch; without one only the picker shows.
            if (data?.AssignedPolicy != null)
            {
                yield return CombatAutopilotGizmoPatch.SeekDestroyToggle(vehicle);
            }
            if (data != null && data.HasAnyAutomation)
            {
                yield return CombatAutopilotGizmoPatch.PauseToggleGizmo(vehicle);
            }
        }

        public static void AdvanceReportPostfix(ThinkNode_JobGiver __instance, Pawn pawn, ref Job __result)
        {
            if (advanceGiver != null && advanceGiver.IsInstanceOfType(__instance)
                && __result != null && pawn?.Faction != null && pawn.Faction.IsPlayer)
            {
                // A multi-cell vehicle whose footprint already covers the goal finishes the Goto on
                // its first tick and is handed it again, looping until VF strips its jobs.
                if (__result.targetA.IsValid && pawn.OccupiedRect().ExpandedBy(1).Contains(__result.targetA.Cell))
                {
                    __result = null;
                    return;
                }
                __result.reportStringOverride =
                    "RimWorldAccess.Autopilot.Report.VehicleAdvance".Translate();
            }
        }

        public static void SupportReportPostfix(ThinkNode_JobGiver __instance, Pawn pawn, ref Job __result)
        {
            if (supportGiver == null || !supportGiver.IsInstanceOfType(__instance)
                || __result == null || pawn?.Faction == null || !pawn.Faction.IsPlayer)
            {
                return;
            }
            __result.reportStringOverride = __result.def == JobDefOf.Goto
                ? (string)"RimWorldAccess.Autopilot.Report.VehicleFiringPosition".Translate()
                : (string)"RimWorldAccess.Autopilot.Report.VehicleHolding".Translate();
        }
    }
}
