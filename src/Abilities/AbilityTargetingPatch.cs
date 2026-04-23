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
            // Check for Verb_Jump FIRST (Jump Pack / Locust Armor).
            // Verb_Jump extends Verb directly, not Verb_CastAbility/IAbilityVerb,
            // so it must be detected separately from ability verbs.
            if (source is Verb_Jump verbJump)
            {
                if (!JumpTargetingState.IsActive)
                    JumpTargetingState.Open(verbJump);
                return;
            }

            // Check if the targeting source is an ability verb.
            // Use IAbilityVerb interface to catch all ability verb types:
            // - Verb_CastAbility (standard psycasts/abilities)
            // - Verb_CastAbilityJump, Verb_CastAbilityTouch (subclasses)
            // - Verb_AbilityShoot (extends Verb_Shoot, not Verb_CastAbility)
            // - Any modded ability verbs implementing IAbilityVerb
            Ability ability = null;
            if (source is Verb_CastAbility verbAbility)
                ability = verbAbility.ability;
            else if (source is IAbilityVerb abilityVerb)
                ability = abilityVerb.Ability;

            if (ability != null)
            {
                // Don't re-open if already active (can happen with destination selection)
                if (AbilityTargetingState.IsActive)
                    return;

                AbilityTargetingState.Open(ability);
                return;
            }

            // Note: Destination phase for dual-target abilities (e.g., Skip) is handled by
            // TargetingPatch.Prefix, which calls AbilityTargetingState.EnterDestinationPhase()
            // with both the target position and destination range before BeginTargeting is called.

            // Handle non-ability targeting sources. Three buckets:
            //   - RoyalTitlePermitWorker_Targeted → permit-specific announcement, no state.
            //   - CompTargetable / CompUsable → ItemTargetingState (Thing-only validation).
            //   - Anything else (verbs like Verb_LaunchProjectile from turret packs and
            //     mech ranged abilities, modded ITargetingSource) → GenericTargetingState.
            //
            // Previously every non-ability source fell into ItemTargetingState, which
            // forced the cell-fallback rejection in TargetingPatch and broke any verb that
            // legitimately targets a map cell (turret packs, Diabolus Hellsphere, mortars
            // mounted as apparel, etc.).
            if (!AbilityTargetingState.IsActive)
            {
                if (source is RoyalTitlePermitWorker_Targeted permitWorker)
                {
                    string permitLabel = permitWorker.def?.LabelCap ?? "permit";
                    TolkHelper.Speak(
                        $"Select a target for {permitLabel}. Navigate with arrow keys, Enter to confirm.",
                        SpeechPriority.Normal);
                }
                else if (source is CompTargetable || source is CompUsable)
                {
                    // Multi-phase items (e.g., sentience catalyst) transition from CompUsable
                    // (Phase 1: select colonist) to CompTargetable (Phase 2: select animal) via
                    // OrderForceTarget → SelectedUseOption → BeginTargeting.
                    if (ItemTargetingState.IsActive)
                        ItemTargetingState.Close();
                    ItemTargetingState.Open(source);
                }
                else
                {
                    // Generic fallback. Catches verbs (turret packs, mech abilities), modded
                    // sources, anything we don't have specialized handling for.
                    if (GenericTargetingState.IsActive)
                        GenericTargetingState.Close();
                    GenericTargetingState.Open(source);
                }
            }
        }

        /// <summary>
        /// Patch the callback-based BeginTargeting overload (TargetingParameters + Action + Pawn + ...).
        /// Used by float-menu actions like "Force [pawn] to wear [apparel]" that open a targeter
        /// without an ITargetingSource. We capture the option's localized label from
        /// PendingTargetingContext (set by WindowlessFloatMenuState / UnifiedKeyboardPatch
        /// before invoking option.Chosen).
        /// </summary>
        [HarmonyPatch(typeof(Targeter), "BeginTargeting", new Type[] {
            typeof(TargetingParameters),
            typeof(Action<LocalTargetInfo>),
            typeof(Pawn),
            typeof(Action),
            typeof(UnityEngine.Texture2D),
            typeof(bool)
        })]
        [HarmonyPostfix]
        public static void BeginTargeting_Callback_Postfix(TargetingParameters targetParams)
        {
            OpenCallbackTargetingState(targetParams);
        }

        /// <summary>
        /// Patch the callback-based BeginTargeting overload with onGuiAction.
        /// </summary>
        [HarmonyPatch(typeof(Targeter), "BeginTargeting", new Type[] {
            typeof(TargetingParameters),
            typeof(Action<LocalTargetInfo>),
            typeof(Action<LocalTargetInfo>)
        })]
        [HarmonyPostfix]
        public static void BeginTargeting_CallbackOnGui_Postfix(TargetingParameters targetParams)
        {
            OpenCallbackTargetingState(targetParams);
        }

        /// <summary>
        /// Patch the full-featured callback-based BeginTargeting overload (with highlight + validator).
        /// </summary>
        [HarmonyPatch(typeof(Targeter), "BeginTargeting", new Type[] {
            typeof(TargetingParameters),
            typeof(Action<LocalTargetInfo>),
            typeof(Action<LocalTargetInfo>),
            typeof(Func<LocalTargetInfo, bool>),
            typeof(Pawn),
            typeof(Action),
            typeof(UnityEngine.Texture2D),
            typeof(bool),
            typeof(Action<LocalTargetInfo>),
            typeof(Action<LocalTargetInfo>)
        })]
        [HarmonyPostfix]
        public static void BeginTargeting_CallbackFull_Postfix(TargetingParameters targetParams)
        {
            OpenCallbackTargetingState(targetParams);
        }

        /// <summary>
        /// Shared logic for the callback-based BeginTargeting postfixes. Closes any prior
        /// ItemTargetingState (e.g., a phase-1 catalyst announcement) before opening a new one
        /// so multi-phase flows announce each phase distinctly.
        /// </summary>
        private static void OpenCallbackTargetingState(TargetingParameters targetParams)
        {
            string label = PendingTargetingContext.ConsumeLabel();

            if (ItemTargetingState.IsActive)
                ItemTargetingState.Close();

            ItemTargetingState.Open(label, targetParams);
        }

        /// <summary>
        /// Patch the 5th BeginTargeting overload — used by ability-aware item callbacks
        /// (TargetingParameters + ITargetingSource ability + action callback). Vanilla calls
        /// this from a few less-common paths; without the patch, the targeting session opens
        /// silently with no announcement.
        /// </summary>
        [HarmonyPatch(typeof(Targeter), "BeginTargeting", new Type[] {
            typeof(TargetingParameters),
            typeof(ITargetingSource),
            typeof(Action<LocalTargetInfo>),
            typeof(Action),
            typeof(UnityEngine.Texture2D)
        })]
        [HarmonyPostfix]
        public static void BeginTargeting_AbilityCallback_Postfix(
            TargetingParameters targetParams, ITargetingSource ability)
        {
            // If the ability is an ITargetingSource we recognize, route through the standard
            // detection path; otherwise treat as a labeled callback.
            if (ability != null)
            {
                BeginTargeting_ITargetingSource_Postfix(ability);
            }
            else
            {
                OpenCallbackTargetingState(targetParams);
            }
        }

        /// <summary>
        /// Patch Targeter.StopTargeting to close all targeting states.
        /// </summary>
        [HarmonyPatch(typeof(Targeter), "StopTargeting")]
        [HarmonyPostfix]
        public static void StopTargeting_Postfix()
        {
            if (JumpTargetingState.IsActive)
                JumpTargetingState.Close();
            if (AbilityTargetingState.IsActive)
                AbilityTargetingState.Close();
            if (ItemTargetingState.IsActive)
                ItemTargetingState.Close();
            if (GenericTargetingState.IsActive)
                GenericTargetingState.Close();
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
