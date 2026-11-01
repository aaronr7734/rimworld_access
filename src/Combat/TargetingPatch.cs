using System.Collections.Generic;
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
    /// Keyboard target selection: Enter at the map cursor stands in for the mouse click
    /// Targeter.ProcessInputEvents would otherwise require.
    /// </summary>
    [HarmonyPatch(typeof(Targeter))]
    [HarmonyPatch("ProcessInputEvents")]
    public static class TargetingPatch
    {
        // Range constraints GizmoNavigationState knows for a Command_Target (animal attack).
        private static bool hasTargetingContext = false;
        private static IntVec3 contextCasterPos = IntVec3.Invalid;
        private static float contextRange = 0f;

        public static bool HasTargetingContext => hasTargetingContext;

        public static void SetTargetingContext(IntVec3 casterPos, float range)
        {
            hasTargetingContext = true;
            contextCasterPos = casterPos;
            contextRange = range;
        }

        public static void ClearTargetingContext()
        {
            hasTargetingContext = false;
            contextCasterPos = IntVec3.Invalid;
            contextRange = 0f;
        }

        /// <summary>
        /// Announces the cursor's distance against the active targeting context's range.
        /// </summary>
        public static void HandleRangeCheck()
        {
            if (!hasTargetingContext || !contextCasterPos.IsValid)
            {
                TolkHelper.Speak("RimWorldAccess.Combat.Target.NoRangeInfo".Loc(), SpeechPriority.Normal);
                return;
            }

            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
            if (!GuardHelper.RequireValidCursor(cursorPos)) return;

            float distance = (cursorPos - contextCasterPos).LengthHorizontal;
            string distanceStr = distance.ToString("F0");

            string announcement = distance <= contextRange
                ? "RimWorldAccess.Combat.Target.DistanceInRange".Translate(distanceStr).ToString()
                : "RimWorldAccess.Combat.Target.DistanceOutOfRange".Translate(distanceStr, contextRange.ToString("F0")).ToString();

            TolkHelper.SpeakData(announcement, SpeechPriority.Normal);
        }

        /// <summary>
        /// Converts an Enter press during targeting into a target selection at the cursor.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        public static bool Prefix(Targeter __instance)
        {
            if (!__instance.IsTargeting)
                return true;

            IntVec3 cursorPosition;
            switch (KeyboardTargeterConfirm.TryResolveCell(out cursorPosition))
            {
                case KeyboardTargeterConfirm.Outcome.NotOurs:
                    return true;
                case KeyboardTargeterConfirm.Outcome.InvalidPosition:
                    return false;
            }

            var targetingSourceField = AccessTools.Field(typeof(Targeter), "targetingSource");
            var targetingSource = targetingSourceField?.GetValue(__instance) as ITargetingSource;

            if (targetingSource != null)
            {
                // Verb-based targeting (Command_VerbTarget). thingsOnly is required: GenUI.TargetsAt
                // falls back to UI.MouseCell() rather than the clickPos passed in, so cell targeting
                // is handled below against the virtual cursor instead.
                Vector3 clickPos = cursorPosition.ToVector3Shifted();
                var targets = GenUI.TargetsAt(clickPos, targetingSource.targetParams, thingsOnly: true, targetingSource);
                LocalTargetInfo target = targets.FirstOrFallback(LocalTargetInfo.Invalid);

                if (!target.IsValid)
                {
                    target = new LocalTargetInfo(cursorPosition);
                }

                if (JumpTargetingState.IsActive)
                {
                    string jumpError = JumpTargetingState.ValidateAndGetError(cursorPosition);
                    if (jumpError != null)
                    {
                        TolkHelper.SpeakData(jumpError, SpeechPriority.High);
                        Event.current.Use();
                        return false;
                    }
                }

                // No predictive ability pre-checks: ValidateTarget below is trusted, and its
                // silent rejections are covered by the fallback branch. Guessing which
                // verbs/comps/mods speak is not sound.

                // Vanilla's Verb.OrderForceTarget only rejects below-min-range when the target
                // is adjacent, so a non-adjacent below-min target is accepted and then stalls
                // silently at the aim stance. Announce it and keep targeting open for a retry.
                if (GenericTargetingState.IsActive)
                {
                    string genericRangeError = GenericTargetingState.ValidateRangeError(cursorPosition);
                    if (genericRangeError != null)
                    {
                        TolkHelper.SpeakData(genericRangeError, SpeechPriority.High);
                        Event.current.Use();
                        return false;
                    }
                }

                // Cell-fallback rejection: block "no thing at cursor" only when the params
                // genuinely refuse locations. Verbs that accept cells (turret packs, mortars,
                // ranged mech abilities) must stay able to fire on empty tiles.
                if (!target.HasThing && !TargetingParametersDescriber.AcceptsLocations(targetingSource.targetParams))
                {
                    string typeDesc = TargetingParametersDescriber.Describe(targetingSource.targetParams);
                    string msg = string.IsNullOrEmpty(typeDesc)
                        ? (string)"RimWorldAccess.Abilities.Item.NoTargetAtCursor".Translate()
                        : (string)"RimWorldAccess.Combat.Target.NoValidAtCursorWithDesc".Translate(typeDesc);
                    TolkHelper.SpeakData(msg, SpeechPriority.High);
                    Event.current.Use();
                    return false;
                }

                // targetingParameters.validator is consulted by GenUI.TargetsAt only for Thing
                // targets, so a cell-only fallback target bypasses the permit's range check.
                if (targetingSource is RoyalTitlePermitWorker_Targeted permitWorker
                    && permitWorker.def.royalAid != null)
                {
                    float targetingRange = permitWorker.def.royalAid.targetingRange;
                    float weatherCap = Find.CurrentMap.weatherManager.CurWeatherMaxRangeCap;
                    float rangeClamped = Mathf.Min(targetingRange, weatherCap);

                    if (rangeClamped > 0f)
                    {
                        IntVec3 casterPos = permitWorker.CasterPawn.Position;
                        float distance = cursorPosition.DistanceTo(casterPos);

                        if (distance > rangeClamped)
                        {
                            TolkHelper.Speak(
                                "RimWorldAccess.Combat.Target.OutOfRange".Loc(distance.ToString("F0"), rangeClamped.ToString("F0")),
                                SpeechPriority.High);
                            Event.current.Use();
                            return false;
                        }
                    }
                }

                // Sampling the message-emission counter around ValidateTarget separates a
                // rejection the source already announced (NotificationAccessibilityPatch spoke
                // it; announcing again would double it) from a silent one needing a fallback.
                long messagesBefore = NotificationAccessibilityPatch.MessageEmissionCount;
                long messageAttemptsBefore = NotificationAccessibilityPatch.MessageAttemptCount;
                if (!targetingSource.ValidateTarget(target, showMessages: true))
                {
                    bool gameSpoke = NotificationAccessibilityPatch.MessageEmissionCount != messagesBefore;
                    if (!gameSpoke)
                    {
                        string fallback = null;

                        // A rejection message suppressed as a recent duplicate never reaches the
                        // display funnel but is still seen by AcceptsMessage; recovering it beats
                        // speaking a generic "Invalid target".
                        if (NotificationAccessibilityPatch.MessageAttemptCount != messageAttemptsBefore)
                        {
                            fallback = NotificationAccessibilityPatch.LastAttemptedMessageText;
                        }

                        if (string.IsNullOrEmpty(fallback))
                        {
                            if (AbilityTargetingState.IsActive)
                            {
                                fallback = AbilityTargetingState.DiagnoseRejection(target, cursorPosition);
                            }
                            else if (ItemTargetingState.IsActive)
                            {
                                string targetLabel = target.HasThing
                                    ? target.Thing.LabelShort
                                    : (string)"RimWorldAccess.Combat.Target.GenericTargetLabel".Translate();
                                fallback = "RimWorldAccess.Combat.Target.NotValidTarget".Translate(targetLabel).ToString();
                            }
                        }
                        TolkHelper.SpeakData(fallback ?? (string)"RimWorldAccess.Combat.Target.InvalidTarget".Translate(), SpeechPriority.High);
                    }
                    // Targeting stays open; Escape is the way out.
                    Event.current.Use();
                    return false;
                }

                // An order that speaks a message (hold fire, melee range) already reported its outcome.
                long orderMessagesBefore = NotificationAccessibilityPatch.MessageEmissionCount;
                long orderAttemptsBefore = NotificationAccessibilityPatch.MessageAttemptCount;
                // Turrets take OrderAttack: Verb.OrderForceTarget assumes a pawn caster and throws.
                var turret = (targetingSource as Verb)?.caster as Building_TurretGun;
                try
                {
                    if (turret != null)
                    {
                        // Pre-check range so a failure keeps targeting open.
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
                    else if (JecsAbilityCompat.TryCastViaAbility(
                        targetingSource, target, out bool jecsCast, out string jecsRefusal))
                    {
                        // PawnAbility.TryCastAbility is JecsTools' own gated cast vehicle;
                        // Verb.OrderForceTarget would bypass CanCastPowerCheck and the cooldown.
                        if (!jecsCast)
                        {
                            TolkHelper.SpeakData(
                                !string.IsNullOrEmpty(jecsRefusal)
                                    ? jecsRefusal
                                    : (string)"RimWorldAccess.Compat.JecsAbility.CannotCastNow".Translate(),
                                SpeechPriority.High);
                            Event.current.Use();
                            return false; // Keep targeting open for retry.
                        }
                    }
                    else
                    {
                        targetingSource.OrderForceTarget(target);
                    }
                }
                catch (System.Exception ex)
                {
                    ModLogger.Error($"Exception in OrderForceTarget: {ex.Message}");
                    TolkHelper.Speak(
                        "RimWorldAccess.Combat.Target.ErrorUsing".Loc(ex.Message),
                        SpeechPriority.High);
                    Event.current.Use();
                    return false;
                }

                // Multi-phase items (the sentience catalyst) start a second targeting phase from
                // inside OrderForceTarget; a changed targeting source means StopTargeting here
                // would kill that new phase.
                var newTargetingSource = targetingSourceField?.GetValue(__instance) as ITargetingSource;
                if (__instance.IsTargeting && newTargetingSource != null && newTargetingSource != targetingSource)
                {
                    // The BeginTargeting postfix already announced the new phase.
                    Event.current.Use();
                    return false;
                }

                // Build the announcement BEFORE StopTargeting, which closes AbilityTargetingState.
                string successMessage;
                if (NotificationAccessibilityPatch.MessageEmissionCount != orderMessagesBefore)
                {
                    successMessage = null;
                }
                else if (NotificationAccessibilityPatch.MessageAttemptCount != orderAttemptsBefore)
                {
                    successMessage = NotificationAccessibilityPatch.LastAttemptedMessageText;
                }
                else if (JumpTargetingState.IsActive)
                {
                    successMessage = JumpTargetingState.BuildSuccessAnnouncement(cursorPosition);
                }
                else if (AbilityTargetingState.IsActive)
                {
                    successMessage = AbilityTargetingState.BuildSuccessAnnouncement(target, cursorPosition);
                }
                else if (ItemTargetingState.IsActive)
                {
                    successMessage = ItemTargetingState.BuildSuccessAnnouncement(target);
                }
                else if (turret != null)
                {
                    successMessage = "RimWorldAccess.Combat.Target.TurretForcedTargetSet".Translate(
                        turret.LabelShortCap, KeyboardTargeterConfirm.TargetLabel(target));
                }
                else if (GenericTargetingState.IsActive)
                {
                    successMessage = GenericTargetingState.BuildSuccessAnnouncement(target);
                }
                else
                {
                    successMessage = "RimWorldAccess.Combat.Target.Targeting".Translate(KeyboardTargeterConfirm.TargetLabel(target));
                }

                // A destination selector means a second phase (Skip). The state must learn the
                // first target BEFORE BeginTargeting fires AbilityTargetingPatch's postfix, so
                // range is measured from that target.
                if (targetingSource.DestinationSelector != null)
                {
                    if (AbilityTargetingState.IsActive && targetingSource.DestinationSelector is CompAbilityEffect_WithDest destCompForContext)
                    {
                        AbilityTargetingState.EnterDestinationPhase(target.Cell, destCompForContext);
                    }

                    __instance.BeginTargeting(targetingSource.DestinationSelector, targetingSource);

                    string destInfo = "RimWorldAccess.Combat.Target.SelectDestination".Translate();
                    if (targetingSource.DestinationSelector is CompAbilityEffect_WithDest destComp)
                    {
                        var props = destComp.Props;
                        if (props.range > 0)
                        {
                            destInfo = "RimWorldAccess.Combat.Target.SelectDestinationInRange".Translate(props.range.ToString("F0"));
                        }
                    }
                    TolkHelper.SpeakData(successMessage == null ? destInfo : $"{successMessage}. {destInfo}");
                }
                else
                {
                    __instance.StopTargeting();
                    TolkHelper.SpeakData(successMessage);
                }

                Event.current.Use();
                return false;
            }
            else
            {
                // Action-based targeting (Command_Target: copy, reinstall).
                var actionField = AccessTools.Field(typeof(Targeter), "action");
                var action = actionField?.GetValue(__instance) as Action<LocalTargetInfo>;

                var targetParamsField = AccessTools.Field(typeof(Targeter), "targetParams");
                var targetParams = targetParamsField?.GetValue(__instance) as TargetingParameters;

                if (action == null)
                {
                    TolkHelper.Speak("RimWorldAccess.Combat.Target.NoActionAvailable".Loc());
                    Event.current.Use();
                    return false;
                }

                LocalTargetInfo target = KeyboardTargeterConfirm.ResolveTargetAt(cursorPosition, targetParams);

                var validatorField = AccessTools.Field(typeof(Targeter), "targetValidator");
                var validator = validatorField?.GetValue(__instance) as Func<LocalTargetInfo, bool>;

                if (validator != null && !validator(target))
                {
                    TolkHelper.Speak("RimWorldAccess.Combat.Target.InvalidTarget".Loc());
                    Event.current.Use();
                    return false;
                }

                // A sighted mouse click would get an Invalid LocalTargetInfo from vanilla's
                // GenUI.TargetsAtMouse pre-filtering; the cursor-based path falls back to a cell
                // target instead, so the params gate has to be applied here. The describer's
                // flags report what the params genuinely accept; a validator may narrow further,
                // but describing the params beats misleading silence.
                if (targetParams != null
                    && Find.CurrentMap != null
                    && !targetParams.CanTarget(target.ToTargetInfo(Find.CurrentMap)))
                {
                    string typeDesc = TargetingParametersDescriber.Describe(targetParams);
                    string msg = string.IsNullOrEmpty(typeDesc)
                        ? (string)"RimWorldAccess.Abilities.Item.NoTargetAtCursor".Translate()
                        : (string)"RimWorldAccess.Combat.Target.NoValidAtCursorWithDesc".Translate(typeDesc);
                    TolkHelper.SpeakData(msg, SpeechPriority.High);
                    Event.current.Use();
                    return false;
                }

                // The game's range check lives inside the action delegate, so checking first is
                // what keeps targeting open for a retry.
                if (hasTargetingContext && contextCasterPos.IsValid && contextRange > 0f)
                {
                    float distance = (cursorPosition - contextCasterPos).LengthHorizontal;
                    if (distance > contextRange)
                    {
                        TolkHelper.Speak(
                            "RimWorldAccess.Combat.Target.OutOfRange".Loc(distance.ToString("F0"), contextRange.ToString("F0")),
                            SpeechPriority.High);
                        Event.current.Use();
                        return false; // Stay in targeting mode for retry
                    }
                }

                // Per-pawn job snapshots are how the multi-select announcement below tells who
                // actually accepted the order.
                Dictionary<Pawn, Verse.AI.Job> jobsBeforeTarget = null;
                Dictionary<Pawn, int> queueBeforeTarget = null;
                bool isMultiSelect = MultiSelectState.IsMultiSelectActive;
                List<Pawn> multiPawns = null;
                if (isMultiSelect)
                {
                    multiPawns = Find.Selector.SelectedPawns.ToList();
                    jobsBeforeTarget = new Dictionary<Pawn, Verse.AI.Job>();
                    queueBeforeTarget = new Dictionary<Pawn, int>();
                    foreach (var p in multiPawns)
                    {
                        jobsBeforeTarget[p] = p.jobs?.curJob;
                        queueBeforeTarget[p] = p.jobs?.jobQueue?.Count ?? 0;
                    }
                }

                // Some callbacks (CompPlantable seed planting) re-open BeginTargeting on an
                // invalid cell to keep the player in placement mode; vanilla suppresses its own
                // StopTargeting because every BeginTargeting overload clears
                // needsStopTargetingCall. Snapshotting the action delegate detects that restart
                // so neither StopTargeting nor a bogus success announcement follows — the
                // callback already spoke the reason through Messages.Message.
                var actionBeforeCallback = action;
                int windowCountBeforeCallback = Find.WindowStack?.Count ?? 0;

                action(target);

                var actionAfterCallback = actionField?.GetValue(__instance) as Action<LocalTargetInfo>;
                if (__instance.IsTargeting
                    && actionAfterCallback != null
                    && !ReferenceEquals(actionAfterCallback, actionBeforeCallback))
                {
                    // The callback rejected this cell and restarted targeting; stay in
                    // placement mode so the cursor can be adjusted and retried.
                    Event.current.Use();
                    return false;
                }

                // A confirmation dialog opened by the callback now owns the interaction. Every
                // dialog is a real window, so the window-count diff alone detects it.
                int windowCountAfterCallback = Find.WindowStack?.Count ?? 0;
                bool confirmationDialogOpened =
                    windowCountAfterCallback > windowCountBeforeCallback;

                // Capture the planting context BEFORE StopTargeting closes PlantTargetingState,
                // so cancelling the dialog re-opens placement instead of kicking the user out.
                if (confirmationDialogOpened && PlantTargetingState.IsActive)
                    PlantTargetingState.NotifyConfirmationDialogOpened();

                __instance.StopTargeting();

                if (confirmationDialogOpened)
                {
                    // The dialog's own scope announces it. Marking the frame is what stops the
                    // Enter that opened the dialog from also confirming it: Window.OnAcceptKeyPressed
                    // is NOT blocked by Event.current.Use().
                    TargetConfirmDialogGuard.MarkDialogOpenedThisFrame();
                    Event.current.Use();
                    return false;
                }

                string targetLabel = KeyboardTargeterConfirm.TargetLabel(target);
                if (isMultiSelect && multiPawns != null && multiPawns.Count > 1)
                {
                    string everyone = ((string)"ConfirmAbandonHomeNegativeThoughts_Everyone".Translate()).TrimEnd(':', ' ');
                    var succeeded = multiPawns.Where(p =>
                        p.jobs?.curJob != jobsBeforeTarget[p] ||
                        (p.jobs?.jobQueue?.Count ?? 0) > queueBeforeTarget[p]).ToList();
                    var unchanged = multiPawns.Where(p =>
                        p.jobs?.curJob == jobsBeforeTarget[p] &&
                        (p.jobs?.jobQueue?.Count ?? 0) <= queueBeforeTarget[p]).ToList();

                    if (unchanged.Count == 0)
                    {
                        TolkHelper.Speak("RimWorldAccess.Combat.MultiSelect.EveryoneAttacks".Loc(everyone, targetLabel));
                    }
                    else if (succeeded.Count == 0)
                    {
                        TolkHelper.Speak("RimWorldAccess.Combat.MultiSelect.NoOneCouldAttack".Loc(targetLabel));
                    }
                    else if (unchanged.Count <= succeeded.Count)
                    {
                        string names = MenuHelper.FormatNameList(unchanged.Select(p => p.LabelShort).ToList());
                        TolkHelper.Speak("RimWorldAccess.Combat.MultiSelect.EveryoneExceptAttacks".Loc(everyone, names, targetLabel));
                    }
                    else
                    {
                        string names = MenuHelper.FormatNameList(succeeded.Select(p => p.LabelShort).ToList());
                        string onlyKey = succeeded.Count == 1
                            ? "RimWorldAccess.Combat.MultiSelect.OnlyOneAttacks"
                            : "RimWorldAccess.Combat.MultiSelect.OnlyManyAttack";
                        TolkHelper.Speak(onlyKey.Loc(names, targetLabel));
                    }
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Combat.Target.Selected".Loc(targetLabel));
                }

                Event.current.Use();
                return false;
            }
        }
    }
}
