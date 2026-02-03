using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld;
using System;
using System.Linq;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch for Targeter.ProcessInputEvents to add keyboard support for target selection.
    /// Allows using Enter key at map cursor position to select targets instead of requiring mouse click.
    /// </summary>
    [HarmonyPatch(typeof(Targeter))]
    [HarmonyPatch("ProcessInputEvents")]
    public static class TargetingPatch
    {
        /// <summary>
        /// Prefix patch that intercepts Enter key during targeting mode and converts it to target selection.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        public static bool Prefix(Targeter __instance)
        {
            // Only process if targeting is active
            if (!__instance.IsTargeting)
                return true;

            // Only process keyboard events
            if (Event.current.type != EventType.KeyDown)
                return true;

            KeyCode key = Event.current.keyCode;

            // Check for Enter key
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                // Make sure map navigation is initialized
                if (!MapNavigationState.IsInitialized)
                    return true;

                // Get the current cursor position
                IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;

                // Validate cursor position
                if (!cursorPosition.IsValid || !cursorPosition.InBounds(Find.CurrentMap))
                {
                    TolkHelper.Speak("Invalid target position");
                    Event.current.Use();
                    return false;
                }

                // Check if this is verb-based targeting (Command_VerbTarget) or action-based (Command_Target)
                var targetingSourceField = AccessTools.Field(typeof(Targeter), "targetingSource");
                var targetingSource = targetingSourceField?.GetValue(__instance) as ITargetingSource;

                if (targetingSource != null)
                {
                    // VERB-BASED TARGETING (Command_VerbTarget - weapon attacks, abilities)
                    // Get the best target at the cursor position (prioritized: pawns > things > cell)
                    // IMPORTANT: Use thingsOnly: true because GenUI.TargetsAt has a bug where it falls back
                    // to UI.MouseCell() (actual mouse position) instead of the clickPos we pass in.
                    // We handle cell targeting explicitly below using our virtual cursor position.
                    Vector3 clickPos = cursorPosition.ToVector3Shifted();
                    var targets = GenUI.TargetsAt(clickPos, targetingSource.targetParams, thingsOnly: true, targetingSource);
                    LocalTargetInfo target = targets.FirstOrFallback(LocalTargetInfo.Invalid);

                    // If no specific thing found, use the cell itself (for mortars and other cell-targeting weapons)
                    // This ensures we use OUR cursor position, not the actual mouse position
                    if (!target.IsValid)
                    {
                        target = new LocalTargetInfo(cursorPosition);
                    }

                    // For ability targeting, provide more specific feedback before standard validation
                    if (AbilityTargetingState.IsActive)
                    {
                        // Check for psycast immunity first (clearer message than game's default)
                        string immunityMessage = AbilityTargetingState.GetImmunityMessage(target);
                        if (immunityMessage != null)
                        {
                            TolkHelper.Speak(immunityMessage, SpeechPriority.High);
                            Event.current.Use();
                            return false;
                        }

                        // Check if there's no valid target at cursor when ability requires one
                        // This provides clearer feedback than the game's "out of range" message
                        string targetError = AbilityTargetingState.ValidateTargetPresent(target, cursorPosition);
                        if (targetError != null)
                        {
                            TolkHelper.Speak(targetError, SpeechPriority.High);
                            Event.current.Use();
                            return false;
                        }

                        // Check range before game's validation for clearer accessible error message
                        string rangeError = AbilityTargetingState.ValidateRange(cursorPosition);
                        if (rangeError != null)
                        {
                            TolkHelper.Speak(rangeError, SpeechPriority.High);
                            Event.current.Use();
                            return false;
                        }

                        // Check line of sight before game's validation
                        string losError = AbilityTargetingState.ValidateLineOfSight(cursorPosition);
                        if (losError != null)
                        {
                            TolkHelper.Speak(losError, SpeechPriority.High);
                            Event.current.Use();
                            return false;
                        }
                    }

                    // Validate the target can be attacked/used
                    if (!targetingSource.ValidateTarget(target, showMessages: true))
                    {
                        // Invalid target - stay in targeting mode, let user try another position
                        // User must press Escape to exit targeting
                        Event.current.Use();
                        return false;
                    }

                    // For turrets, call OrderAttack on the building instead of the verb's OrderForceTarget
                    // (Verb.OrderForceTarget assumes a pawn caster and will throw NullReferenceException)
                    var verb = targetingSource as Verb;
                    if (verb?.caster is Building_TurretGun turret)
                    {
                        // Pre-check range to stay in targeting mode on failure
                        float distance = (target.Cell - turret.Position).LengthHorizontal;
                        float minRange = turret.AttackVerb.verbProps.EffectiveMinRange(target, turret);
                        float maxRange = turret.AttackVerb.EffectiveRange;

                        if (distance < minRange)
                        {
                            Messages.Message("MessageTargetBelowMinimumRange".Translate(), turret, MessageTypeDefOf.RejectInput, historical: false);
                            Event.current.Use();
                            return false;
                        }
                        if (distance > maxRange)
                        {
                            Messages.Message("MessageTargetBeyondMaximumRange".Translate(), turret, MessageTypeDefOf.RejectInput, historical: false);
                            Event.current.Use();
                            return false;
                        }

                        turret.OrderAttack(target);
                    }
                    else
                    {
                        // Standard pawn targeting
                        targetingSource.OrderForceTarget(target);
                    }

                    // Build success announcement BEFORE stopping targeting
                    // (StopTargeting closes AbilityTargetingState via our patch)
                    string successMessage;
                    if (AbilityTargetingState.IsActive)
                    {
                        successMessage = AbilityTargetingState.BuildSuccessAnnouncement(target, cursorPosition);
                    }
                    else
                    {
                        // Non-ability targeting (weapons, turrets)
                        if (target.HasThing)
                        {
                            successMessage = $"Targeting: {target.Thing.LabelShort}";
                        }
                        else
                        {
                            // Cell-only target (like mortar bombardment)
                            successMessage = "Targeting location";
                        }
                    }

                    // Check if this ability has a second phase (destination selection, like Skip)
                    if (targetingSource.DestinationSelector != null)
                    {
                        // Start second targeting phase for destination selection
                        __instance.BeginTargeting(targetingSource.DestinationSelector, targetingSource);

                        // Announce with destination range if available
                        string destInfo = "Now select destination";
                        if (targetingSource.DestinationSelector is CompAbilityEffect_WithDest destComp)
                        {
                            var props = destComp.Props;
                            if (props.range > 0)
                            {
                                destInfo = $"Now select destination within {props.range:F0} tiles";
                            }
                        }
                        TolkHelper.Speak($"{successMessage}. {destInfo}");
                    }
                    else
                    {
                        // No second phase - stop targeting mode
                        __instance.StopTargeting();
                        TolkHelper.Speak(successMessage);
                    }

                    // Consume the event
                    Event.current.Use();
                    return false;
                }
                else
                {
                    // ACTION-BASED TARGETING (Command_Target - copy, reinstall, etc.)
                    // Get the action callback and targeting parameters via reflection
                    var actionField = AccessTools.Field(typeof(Targeter), "action");
                    var action = actionField?.GetValue(__instance) as Action<LocalTargetInfo>;

                    var targetParamsField = AccessTools.Field(typeof(Targeter), "targetParams");
                    var targetParams = targetParamsField?.GetValue(__instance) as TargetingParameters;

                    if (action == null)
                    {
                        TolkHelper.Speak("No targeting action available");
                        Event.current.Use();
                        return false;
                    }

                    // Get the best target at the cursor position
                    // IMPORTANT: Use thingsOnly: true because GenUI.TargetsAt has a bug where it falls back
                    // to UI.MouseCell() (actual mouse position) instead of the clickPos we pass in.
                    // We handle cell targeting explicitly below using our virtual cursor position.
                    Vector3 clickPos = cursorPosition.ToVector3Shifted();
                    var targets = GenUI.TargetsAt(clickPos, targetParams, thingsOnly: true, null);
                    LocalTargetInfo target = targets.FirstOrFallback(LocalTargetInfo.Invalid);

                    // If no specific thing found, use the cell position itself
                    // This ensures we use OUR cursor position, not the actual mouse position
                    if (!target.IsValid)
                    {
                        target = new LocalTargetInfo(cursorPosition);
                    }

                    // Check if there's a validator
                    var validatorField = AccessTools.Field(typeof(Targeter), "targetValidator");
                    var validator = validatorField?.GetValue(__instance) as Func<LocalTargetInfo, bool>;

                    if (validator != null && !validator(target))
                    {
                        TolkHelper.Speak("Invalid target");
                        Event.current.Use();
                        return false;
                    }

                    // Execute the action callback
                    action(target);

                    // Stop targeting mode
                    __instance.StopTargeting();

                    // Announce success
                    string targetLabel = target.HasThing ? target.Thing.LabelShort : "location";
                    TolkHelper.Speak($"Target selected: {targetLabel}");

                    // Consume the event
                    Event.current.Use();
                    return false;
                }
            }

            // Let other keys pass through
            return true;
        }
    }
}
