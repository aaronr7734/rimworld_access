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
                    Vector3 clickPos = cursorPosition.ToVector3Shifted();
                    var targets = GenUI.TargetsAt(clickPos, targetingSource.targetParams, thingsOnly: false, targetingSource);
                    LocalTargetInfo target = targets.FirstOrFallback(LocalTargetInfo.Invalid);

                    // Validate the target is valid
                    if (!target.IsValid)
                    {
                        TolkHelper.Speak("No valid target at cursor position");
                        Event.current.Use();
                        return false;
                    }

                    // Validate the target can be attacked/used
                    if (!targetingSource.ValidateTarget(target, showMessages: true))
                    {
                        // Invalid target, ValidateTarget already showed a message
                        Event.current.Use();
                        return false;
                    }

                    // Valid target! Use the standard OrderForceTarget method
                    targetingSource.OrderForceTarget(target);

                    // Stop targeting mode
                    __instance.StopTargeting();

                    // Announce success
                    string targetLabel = target.HasThing ? target.Thing.LabelShort : target.Cell.ToString();
                    TolkHelper.Speak($"Targeting: {targetLabel}");

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
                    Vector3 clickPos = cursorPosition.ToVector3Shifted();
                    var targets = GenUI.TargetsAt(clickPos, targetParams, thingsOnly: false, null);
                    LocalTargetInfo target = targets.FirstOrFallback(LocalTargetInfo.Invalid);

                    // If no specific thing found, use the cell position itself
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
                    string targetLabel = target.HasThing ? target.Thing.LabelShort : target.Cell.ToString();
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
