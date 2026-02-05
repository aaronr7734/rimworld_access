using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches to detect ability targeting start and end.
    /// </summary>
    [HarmonyPatch]
    public static class AbilityTargetingPatch
    {
        /// <summary>
        /// Patch Targeter.BeginTargeting to detect when ability targeting starts.
        /// This overload is called when abilities (including psycasts) begin targeting.
        /// </summary>
        [HarmonyPatch(typeof(Targeter), "BeginTargeting", new Type[] {
            typeof(ITargetingSource),
            typeof(ITargetingSource),
            typeof(bool),
            typeof(Func<LocalTargetInfo, ITargetingSource>),
            typeof(Action),
            typeof(bool)
        })]
        [HarmonyPostfix]
        public static void BeginTargeting_ITargetingSource_Postfix(ITargetingSource source)
        {
            // Check if the targeting source is an ability verb
            if (source is Verb_CastAbility verbAbility && verbAbility.ability != null)
            {
                // Don't re-open if already active (can happen with destination selection)
                if (AbilityTargetingState.IsActive)
                    return;

                AbilityTargetingState.Open(verbAbility.ability);
            }
        }

        /// <summary>
        /// Patch Targeter.StopTargeting to close ability targeting state.
        /// </summary>
        [HarmonyPatch(typeof(Targeter), "StopTargeting")]
        [HarmonyPostfix]
        public static void StopTargeting_Postfix()
        {
            if (AbilityTargetingState.IsActive)
            {
                AbilityTargetingState.Close();
            }

            // Also clear Command_Target targeting context (e.g., animal attack range info)
            TargetingPatch.ClearTargetingContext();
        }

        /// <summary>
        /// Patch WorldTargeter.StopTargeting to close world ability targeting state.
        /// </summary>
        [HarmonyPatch(typeof(WorldTargeter), "StopTargeting")]
        [HarmonyPostfix]
        public static void WorldTargeter_StopTargeting_Postfix()
        {
            if (WorldAbilityTargetingState.IsActive)
            {
                WorldAbilityTargetingState.Close();
            }
        }

        /// <summary>
        /// Patch Command_Ability.ProcessInput to detect when world targeting starts.
        /// This is called when abilities that target world cells (like Farskip) are activated.
        /// </summary>
        [HarmonyPatch(typeof(Command_Ability), "ProcessInput")]
        [HarmonyPostfix]
        public static void Command_Ability_ProcessInput_Postfix(Command_Ability __instance)
        {
            // Check if this ability targets world cells
            if (__instance.Ability?.def?.targetWorldCell == true)
            {
                // Don't re-open if already active
                if (WorldAbilityTargetingState.IsActive)
                    return;

                WorldAbilityTargetingState.Open(__instance.Ability);
            }
        }
    }
}
