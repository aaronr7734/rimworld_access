using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The engage gizmo: a vanilla targeter pick that orders the whole selection onto the
    /// chosen target. The right-click menu clears the queue, which the keyboard gizmo menu
    /// reads through RightClickFloatMenuOptions like any other command.
    /// </summary>
    public class Command_EngageTarget : Command_Target
    {
        private readonly Pawn pawn;

        public Command_EngageTarget(Pawn pawn)
        {
            this.pawn = pawn;
        }

        public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions
        {
            get
            {
                foreach (FloatMenuOption option in base.RightClickFloatMenuOptions)
                {
                    yield return option;
                }
                if (CombatAutopilotOrders.AnySelectedHasOrders(pawn))
                {
                    yield return new FloatMenuOption(
                        "RimWorldAccess.Autopilot.ClearOrders".Translate(),
                        () => CombatAutopilotOrders.ClearOrdersForSelection(pawn));
                }
            }
        }
    }

    /// <summary>
    /// Engage-order plumbing shared by the gizmo targeter and the right-click float menu:
    /// the order goes to every selected drafted player pawn that can fight (the clicked
    /// pawn always included), a held shift queues instead of replacing — vanilla's order
    /// queue modifier, live through both the mouse click and the shell's Shift+Enter.
    /// </summary>
    public static class CombatAutopilotOrders
    {
        public static bool QueueModifierHeld()
        {
            if (Event.current != null && Event.current.shift)
            {
                return true;
            }
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        /// <summary>Violence is not work-disabled; vehicles and animals pass via the null story.</summary>
        public static bool ViolentCapable(Pawn pawn)
        {
            return pawn.story == null || !pawn.WorkTagIsDisabled(WorkTags.Violent);
        }

        public static bool HasAbilities(Pawn pawn)
        {
            return pawn.abilities != null && pawn.abilities.AllAbilitiesForReading.Count > 0;
        }

        /// <summary>Can fight at all: weapon violence allowed, or abilities to cast.</summary>
        public static bool CanFight(Pawn pawn)
        {
            return ViolentCapable(pawn) || HasAbilities(pawn);
        }

        /// <summary>Engage orders are served by the autopilot, whose master switch is the combat policy.</summary>
        public static bool CanFightWithPolicy(Pawn pawn)
        {
            return CanFight(pawn) && CombatAutopilotComponent.TryGetData(pawn)?.AssignedPolicy != null;
        }

        /// <summary>True when the pawn can carry out an engage order at all.</summary>
        public static bool CanTakeEngageOrder(Pawn pawn)
        {
            return pawn?.Faction != null && pawn.Faction.IsPlayer && pawn.Drafted
                && CanFightWithPolicy(pawn);
        }

        /// <summary>
        /// Vanilla's drafted-attack menu gate, minus downed pawns: the order ends at
        /// downing, and executions remain a vanilla attack order.
        /// </summary>
        public static bool ValidEngageTarget(Thing thing)
        {
            if (thing == null || !thing.Spawned || thing.Destroyed)
            {
                return false;
            }
            if (thing.def.noRightClickDraftAttack && thing.HostileTo(Faction.OfPlayer))
            {
                return false;
            }
            if (thing is Pawn pawn)
            {
                return !pawn.DeadOrDowned && (pawn.HostileTo(Faction.OfPlayer) || pawn.NonHumanlikeOrWildMan());
            }
            if (thing.def.IsNonDeconstructibleAttackableBuilding
                || (thing.def.building != null && thing.def.building.quickTargetable))
            {
                return true;
            }
            return thing.def.destroyable && thing.HostileTo(Faction.OfPlayer);
        }

        public static void EngageSelection(Pawn clickedPawn, Thing target, bool speak = true)
        {
            if (target == null)
            {
                return;
            }
            bool queue = QueueModifierHeld();
            int count = 0;
            Pawn firstOrdered = null;
            foreach (Pawn pawn in SelectionTargets(clickedPawn, CanFightWithPolicy))
            {
                CombatAutopilotData data = CombatAutopilotComponent.GetData(pawn);
                data.GiveEngageOrder(target, queue);
                data.Paused = false;
                count++;
                if (firstOrdered == null)
                {
                    firstOrdered = pawn;
                }
                if (!queue)
                {
                    CombatAutopilotGizmoPatch.InterruptForRethink(pawn, replacesPlayerOrder: true);
                }
            }
            if (count == 0)
            {
                return;
            }
            if (target.MapHeld != null)
            {
                FleckMaker.Static(target.DrawPos, target.MapHeld, FleckDefOf.FeedbackShoot);
            }
            if (speak)
            {
                string who = count == 1
                    ? firstOrdered.LabelShort
                    : (string)"RimWorldAccess.Autopilot.EngagePawnCount".Translate(count);
                TolkHelper.Speak(queue
                    ? "RimWorldAccess.Autopilot.EngageQueued".Loc(who, target.LabelShort)
                    : "RimWorldAccess.Autopilot.EngageOrdered".Loc(who, target.LabelShort));
            }
        }

        public static bool AnySelectedHasOrders(Pawn clickedPawn)
        {
            foreach (Pawn pawn in SelectionTargets(clickedPawn, CanFight))
            {
                CombatAutopilotData data = CombatAutopilotComponent.TryGetData(pawn);
                if (data != null && data.OrderedTargets.Count > 0)
                {
                    return true;
                }
            }
            return false;
        }

        public static void ClearOrdersForSelection(Pawn clickedPawn)
        {
            foreach (Pawn pawn in SelectionTargets(clickedPawn, CanFight))
            {
                CombatAutopilotData data = CombatAutopilotComponent.TryGetData(pawn);
                if (data != null && data.OrderedTargets.Count > 0)
                {
                    data.OrderedTargets.Clear();
                    CombatAutopilotGizmoPatch.InterruptForRethink(pawn);
                }
            }
            TolkHelper.Speak("RimWorldAccess.Autopilot.OrdersCleared".Loc());
        }

        /// <summary>The clicked pawn plus every other selected drafted player pawn that passes the filter.</summary>
        public static IEnumerable<Pawn> SelectionTargets(Pawn clicked, Func<Pawn, bool> appliesTo)
        {
            if (Applies(clicked, appliesTo))
            {
                yield return clicked;
            }
            if (Find.Selector == null)
            {
                yield break;
            }
            foreach (Pawn selected in Find.Selector.SelectedPawns)
            {
                if (selected != clicked && Applies(selected, appliesTo))
                {
                    yield return selected;
                }
            }
        }

        private static bool Applies(Pawn pawn, Func<Pawn, bool> appliesTo)
        {
            return pawn?.Faction != null && pawn.Faction.IsPlayer && pawn.Drafted && appliesTo(pawn);
        }
    }

    /// <summary>
    /// "Engage X" in the drafted right-click menu (auto-discovered provider, so it appears
    /// in the mouse menu and the keyboard orders menu alike). Multi-select aware: one
    /// option orders every valid selected pawn; shift at choose time queues, which the
    /// float menu scope's Shift+Enter passes through.
    /// </summary>
    public class FloatMenuOptionProvider_EngageTarget : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;

        protected override bool Undrafted => false;

        protected override bool Multiselect => true;

        protected override bool MechanoidCanDo => true;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            return settings == null || settings.EnableCombatAutopilot;
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            if (!CombatAutopilotOrders.ValidEngageTarget(clickedThing))
            {
                yield break;
            }
            Pawn first = null;
            foreach (Pawn pawn in context.ValidSelectedPawns)
            {
                if (CombatAutopilotOrders.CanTakeEngageOrder(pawn))
                {
                    first = pawn;
                    break;
                }
            }
            if (first == null)
            {
                yield break;
            }
            Pawn clickedFor = first;
            yield return new FloatMenuOption(
                "RimWorldAccess.Autopilot.EngageOption".Translate(clickedThing.Label),
                () => CombatAutopilotOrders.EngageSelection(clickedFor, clickedThing, speak: false),
                MenuOptionPriority.AttackEnemy)
            {
                tooltip = (TipSignal)(string)"RimWorldAccess.Autopilot.EngageOption.Tip".Translate(),
            };
        }
    }

    /// <summary>Undrafting releases control: the engage queue, the hold post and (by setting)
    /// search and destroy and hunt animals are all standing orders to a DRAFTED pawn.</summary>
    [HarmonyPatch(typeof(Pawn_DraftController), nameof(Pawn_DraftController.Drafted), MethodType.Setter)]
    public static class CombatAutopilotUndraftPatch
    {
        public static void Postfix(Pawn_DraftController __instance, bool value)
        {
            if (value)
            {
                return;
            }
            CombatAutopilotData data = CombatAutopilotComponent.TryGetData(__instance.pawn);
            if (data == null)
            {
                return;
            }
            data.OrderedTargets.Clear();
            data.HoldPost = IntVec3.Invalid;
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings == null || settings.UndraftClearsStandingOrders)
            {
                data.SeekAndDestroy = false;
                data.HuntAnimals = false;
            }
        }
    }

    /// <summary>A drafted move order repositions the pawn: the old hold post no longer applies.</summary>
    [HarmonyPatch(typeof(Verse.AI.Pawn_JobTracker), nameof(Verse.AI.Pawn_JobTracker.TryTakeOrderedJob))]
    public static class CombatAutopilotRepositionPatch
    {
        public static void Postfix(Pawn ___pawn, Verse.AI.Job job, bool __result)
        {
            if (!__result || job == null || job.def != JobDefOf.Goto)
            {
                return;
            }
            CombatAutopilotData data = CombatAutopilotComponent.TryGetData(___pawn);
            if (data != null)
            {
                data.HoldPost = IntVec3.Invalid;
            }
        }
    }
}
