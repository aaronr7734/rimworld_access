using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    [DefOf]
    public static class CombatAutopilotKeyDefOf
    {
        public static KeyBindingDef RWA_GizmoEngage;
        public static KeyBindingDef RWA_GizmoSeekDestroy;
        public static KeyBindingDef RWA_GizmoCombatPolicy;
        public static KeyBindingDef RWA_GizmoPause;
        public static KeyBindingDef RWA_GizmoHuntAnimals;
        public static KeyBindingDef RWA_GizmoAutocastAll;
        public static KeyBindingDef RWA_GizmoPatrol;

        static CombatAutopilotKeyDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(CombatAutopilotKeyDefOf));
        }
    }

    /// <summary>
    /// Adds the autopilot ORDERS to every drafted player pawn's gizmo row: an engage
    /// targeter, the seek-and-destroy toggle, the patrol picker, and the per-pawn feature
    /// checkboxes (pause, hunt animals, autocast-all) as plain vanilla commands with
    /// ordinary gizmo hotkeys. Doctrine (the combat policy) is assigned from the pawn
    /// tables, not from here. Group keys merge multi-selected pawns: every command
    /// applies to the whole selection itself, so group propagation is disarmed throughout
    /// (see <see cref="Command_SelectionToggle"/> for why the flag alone is not enough).
    /// </summary>
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(Pawn_DraftController), "GetGizmos")]
    public static class CombatAutopilotGizmoPatch
    {
        private const int GroupKeyBase = 84720610;

        private static Texture2D huntAnimalsIcon;
        private static Texture2D patrolIcon;

        private static Texture2D HuntAnimalsIcon =>
            huntAnimalsIcon ?? (huntAnimalsIcon = ContentFinder<Texture2D>.Get("UI/Designators/Hunt"));

        private static Texture2D PatrolIcon =>
            patrolIcon ?? (patrolIcon = ContentFinder<Texture2D>.Get("UI/Designators/AreaAllowedExpand"));

        public static void Postfix(ref IEnumerable<Gizmo> __result, Pawn_DraftController __instance)
        {
            Pawn pawn = __instance.pawn;
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings != null && !settings.EnableCombatAutopilot)
            {
                return;
            }
            if (pawn?.Faction == null || !pawn.Faction.IsPlayer || !pawn.Drafted)
            {
                return;
            }
            __result = Append(__result, pawn, settings);
        }

        private static IEnumerable<Gizmo> Append(IEnumerable<Gizmo> source, Pawn pawn,
            RimWorldAccessSettings settings)
        {
            foreach (Gizmo gizmo in source)
            {
                yield return gizmo;
            }
            bool violentCapable = CombatAutopilotOrders.ViolentCapable(pawn);
            bool hasAbilities = CombatAutopilotOrders.HasAbilities(pawn);
            CombatAutopilotData current = CombatAutopilotComponent.TryGetData(pawn);
            // The combat policy is the autopilot's master switch: without one the combat
            // gizmos hide entirely. Patrol (walking, not fighting) and the hunting system
            // (its own policy) stay.
            bool hasPolicy = current?.AssignedPolicy != null;
            if (hasPolicy && (violentCapable || hasAbilities))
            {
                yield return EngageCommand(pawn);
                yield return SeekDestroyToggle(pawn);
            }
            // Patrolling needs walking, not violence, so pacifists get the beat too.
            yield return PatrolPicker(pawn);
            // Unchecked by default (automation runs); hidden while nothing is configured.
            if (current != null && current.HasAnyAutomation)
            {
                yield return PauseToggleGizmo(pawn);
            }
            if (violentCapable)
            {
                yield return SelectionToggle(pawn, "HuntAnimals", HuntAnimalsIcon, GroupKeyBase + 6,
                    CombatAutopilotKeyDefOf.RWA_GizmoHuntAnimals,
                    data => data.HuntAnimals, (t, data, on) => data.HuntAnimals = on,
                    CombatAutopilotOrders.ViolentCapable);
            }
            if (hasPolicy && hasAbilities)
            {
                yield return SelectionToggle(pawn, "AutocastAll", TexCommand.DesirePower, GroupKeyBase + 4,
                    CombatAutopilotKeyDefOf.RWA_GizmoAutocastAll,
                    data => data.AutocastAbilities.Count > 0, SetAutocastAll,
                    t => CombatAutopilotOrders.HasAbilities(t)
                        && CombatAutopilotComponent.TryGetData(t)?.AssignedPolicy != null);
            }
        }

        /// <summary>The engage order: pick any attackable target; the whole selection queues on it.</summary>
        internal static Gizmo EngageCommand(Pawn pawn)
        {
            return new Command_EngageTarget(pawn)
            {
                defaultLabel = "RimWorldAccess.Autopilot.Engage.Label".Translate(),
                defaultDesc = EngageDesc(pawn),
                icon = TexCommand.Attack,
                hotKey = CombatAutopilotKeyDefOf.RWA_GizmoEngage,
                groupKey = GroupKeyBase + 2,
                alsoClickIfOtherInGroupClicked = false,
                targetingParams = TargetingParameters.ForAttackAny(),
                action = target => CombatAutopilotOrders.EngageSelection(pawn, target.Thing),
            };
        }

        private static string EngageDesc(Pawn pawn)
        {
            string desc = "RimWorldAccess.Autopilot.Engage.Desc".Translate(pawn.LabelShort);
            CombatAutopilotData data = CombatAutopilotComponent.TryGetData(pawn);
            if (data?.CurrentOrderedTarget() != null)
            {
                desc += "\n" + "RimWorldAccess.Autopilot.Engage.CurrentOrders".Translate(
                    data.OrderedTargets.Select(t => t.LabelShort).ToCommaList());
            }
            return desc;
        }

        internal static Gizmo SeekDestroyToggle(Pawn pawn)
        {
            return SelectionToggle(pawn, "SeekDestroy", TexCommand.SquadAttack, GroupKeyBase + 3,
                CombatAutopilotKeyDefOf.RWA_GizmoSeekDestroy,
                data => data.SeekAndDestroy, (t, data, on) => data.SeekAndDestroy = on,
                CombatAutopilotOrders.CanFightWithPolicy);
        }

        /// <summary>The patrol order: pick an area to walk a beat in; the whole selection follows.</summary>
        private static Command_Action PatrolPicker(Pawn pawn)
        {
            CombatAutopilotData data = CombatAutopilotComponent.TryGetData(pawn);
            string current = data?.PatrolArea != null
                ? data.PatrolArea.Label
                : (string)"RimWorldAccess.Autopilot.Policy.None".Translate();
            return new Command_Action
            {
                defaultLabel = "RimWorldAccess.Autopilot.Patrol.Label".Translate(current),
                defaultDesc = "RimWorldAccess.Autopilot.Patrol.Desc".Translate(pawn.LabelShort),
                icon = PatrolIcon,
                hotKey = CombatAutopilotKeyDefOf.RWA_GizmoPatrol,
                groupKey = GroupKeyBase + 5,
                alsoClickIfOtherInGroupClicked = false,
                action = () => Find.WindowStack.Add(new FloatMenu(BuildPatrolOptions(pawn))),
            };
        }

        private static List<FloatMenuOption> BuildPatrolOptions(Pawn clickedPawn)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("RimWorldAccess.Autopilot.Patrol.NoneOption".Translate(),
                    () => AssignPatrolToSelection(clickedPawn, null)),
            };
            Map map = clickedPawn.MapHeld;
            if (map != null)
            {
                foreach (Area area in map.areaManager.AllAreas)
                {
                    if (!(area is Area_Home) && !area.AssignableAsAllowed())
                    {
                        continue;
                    }
                    Area chosen = area;
                    options.Add(new FloatMenuOption(chosen.Label,
                        () => AssignPatrolToSelection(clickedPawn, chosen)));
                }
            }
            return options;
        }

        /// <summary>Patrolling needs walking, not violence, so pacifists qualify too.</summary>
        private static void AssignPatrolToSelection(Pawn clickedPawn, Area area)
        {
            int count = 0;
            foreach (Pawn target in CombatAutopilotOrders.SelectionTargets(clickedPawn, t => true))
            {
                CombatAutopilotData data = CombatAutopilotComponent.GetData(target);
                data.PatrolArea = area;
                data.PatrolDest = IntVec3.Invalid;
                if (area != null)
                {
                    data.Paused = false;
                    data.HoldPost = IntVec3.Invalid;
                }
                InterruptForRethink(target);
                count++;
            }
            if (count == 0)
            {
                return;
            }
            string who = count == 1
                ? clickedPawn.LabelShort
                : (string)"RimWorldAccess.Autopilot.EngagePawnCount".Translate(count);
            TolkHelper.Speak(area != null
                ? "RimWorldAccess.Autopilot.PatrolAssigned".Loc(who, area.Label)
                : "RimWorldAccess.Autopilot.PatrolCleared".Loc(who));
        }

        /// <summary>The policy picker survives only for hosts without a pawn-table home (vehicle compat).</summary>
        internal static Gizmo PolicyPickerGizmo(Pawn pawn)
        {
            return PolicyPicker(pawn);
        }

        internal static Gizmo PauseToggleGizmo(Pawn pawn)
        {
            return SelectionToggle(pawn, "Pause", TexCommand.PauseCaravan, GroupKeyBase + 1,
                CombatAutopilotKeyDefOf.RWA_GizmoPause,
                data => data.Paused, (t, data, on) => data.Paused = on,
                t => CombatAutopilotComponent.TryGetData(t)?.HasAnyAutomation == true);
        }

        /// <summary>
        /// The policy dropdown as a gizmo: current assignment in the label, choices in a float
        /// menu whose rows carry each policy's behavior summary as a tooltip. Choosing applies
        /// to every selected applicable pawn, so the group must not re-click siblings.
        /// </summary>
        private static Command_Action PolicyPicker(Pawn pawn)
        {
            CombatAutopilotData data = CombatAutopilotComponent.TryGetData(pawn);
            CombatPolicy assigned = data?.AssignedPolicy;
            string current = assigned != null
                ? assigned.label
                : (string)"RimWorldAccess.Autopilot.Policy.None".Translate();
            return new Command_Action
            {
                defaultLabel = "RimWorldAccess.Autopilot.PickerLabel".Translate(current),
                defaultDesc = "RimWorldAccess.Autopilot.PickerDesc".Translate(pawn.LabelShort),
                icon = TexCommand.FireAtWill,
                hotKey = CombatAutopilotKeyDefOf.RWA_GizmoCombatPolicy,
                groupKey = GroupKeyBase,
                alsoClickIfOtherInGroupClicked = false,
                action = () => Find.WindowStack.Add(new FloatMenu(BuildPolicyOptions(pawn))),
            };
        }

        private static List<FloatMenuOption> BuildPolicyOptions(Pawn clickedPawn)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("RimWorldAccess.Autopilot.Policy.None".Translate(),
                    () => AssignToSelection(clickedPawn, null))
                {
                    tooltip = (TipSignal)(string)"RimWorldAccess.Autopilot.Policy.None.Tip".Translate(),
                },
            };
            foreach (CombatPolicy policy in CombatAutopilotComponent.AllPolicies)
            {
                CombatPolicy chosen = policy;
                options.Add(new FloatMenuOption(chosen.label, () => AssignToSelection(clickedPawn, chosen))
                {
                    tooltip = (TipSignal)chosen.SummaryLine(),
                });
            }
            options.Add(new FloatMenuOption(string.Format("{0}...", "AssignTabEdit".Translate()),
                () => Find.WindowStack.Add(new Dialog_ManageCombatPolicies(
                    CombatAutopilotComponent.TryGetData(clickedPawn)?.AssignedPolicy))));
            return options;
        }

        /// <summary>Assigns to every selected applicable pawn — the multi-select contract. Doctrine never unpauses.</summary>
        private static void AssignToSelection(Pawn clickedPawn, CombatPolicy policy)
        {
            foreach (Pawn target in CombatAutopilotOrders.SelectionTargets(clickedPawn,
                PawnColumnWorker_CombatPolicy.AppliesTo))
            {
                CombatAutopilotComponent.GetData(target).AssignedPolicy = policy;
                InterruptForRethink(target);
            }
        }

        /// <summary>
        /// A checkbox aligning the WHOLE selection: the clicked pawn's state inverts and every
        /// other selected applicable pawn is SET to that state, so a mixed selection comes out
        /// uniform. Group propagation is disarmed; this action already covers the selection.
        /// </summary>
        private static Command_Toggle SelectionToggle(Pawn pawn, string keyPart, Texture2D icon, int groupKey,
            KeyBindingDef hotKey, Func<CombatAutopilotData, bool> isOn,
            Action<Pawn, CombatAutopilotData, bool> setTo, Func<Pawn, bool> appliesTo)
        {
            return new Command_SelectionToggle
            {
                defaultLabel = ("RimWorldAccess.Autopilot." + keyPart + ".Label").Translate(),
                defaultDesc = ("RimWorldAccess.Autopilot." + keyPart + ".Desc").Translate(pawn.LabelShort),
                icon = icon,
                hotKey = hotKey,
                groupKey = groupKey,
                alsoClickIfOtherInGroupClicked = false,
                isActive = () =>
                {
                    CombatAutopilotData data = CombatAutopilotComponent.TryGetData(pawn);
                    return data != null && isOn(data);
                },
                toggleAction = () =>
                {
                    bool target = !isOn(CombatAutopilotComponent.GetData(pawn));
                    foreach (Pawn t in CombatAutopilotOrders.SelectionTargets(pawn, appliesTo))
                    {
                        setTo(t, CombatAutopilotComponent.GetData(t), target);
                        InterruptForRethink(t);
                    }
                },
            };
        }

        /// <summary>
        /// Command_Toggle's InheritInteractionsFrom ignores alsoClickIfOtherInGroupClicked
        /// and re-clicks same-state group siblings; a toggle whose action already covers the
        /// whole selection would then fire once per selected pawn, flip-flopping the state
        /// ("started 10 jobs in one tick"). Refusing inheritance disarms propagation.
        /// </summary>
        private sealed class Command_SelectionToggle : Command_Toggle
        {
            public override bool InheritInteractionsFrom(Gizmo other)
            {
                return false;
            }
        }

        private static void SetAutocastAll(Pawn pawn, CombatAutopilotData data, bool on)
        {
            if (on)
            {
                CombatAutopilotAutocast.FillAll(pawn, data);
            }
            else
            {
                data.AutocastAbilities.Clear();
            }
        }

        /// <summary>
        /// Ends the current job so a changed setting takes effect now. A player-ordered job is
        /// left to finish unless <paramref name="replacesPlayerOrder"/>: the autopilot's think node
        /// only runs once the pawn needs a new job, so it takes over when that order ends.
        /// </summary>
        internal static void InterruptForRethink(Pawn pawn, bool replacesPlayerOrder = false)
        {
            if (pawn.Spawned && pawn.Drafted && pawn.jobs != null
                && (replacesPlayerOrder || pawn.CurJob == null || !pawn.CurJob.playerForced))
            {
                pawn.jobs.EndCurrentJob(Verse.AI.JobCondition.InterruptForced);
            }
        }
    }

    /// <summary>Autocast-list plumbing shared by the all-abilities toggle and the per-ability menu entries.</summary>
    public static class CombatAutopilotAutocast
    {
        public static void FillAll(Pawn pawn, CombatAutopilotData data)
        {
            data.AutocastAbilities.Clear();
            if (pawn.abilities == null)
            {
                return;
            }
            foreach (Ability ability in pawn.abilities.AllAbilitiesForReading)
            {
                if (MassMarkable(ability) && !data.AutocastAbilities.Contains(ability.def))
                {
                    data.AutocastAbilities.Add(ability.def);
                }
            }
        }

        /// <summary>
        /// Abilities the mark-all toggle selects: combat casts (vanilla's hostile or aiCanUse
        /// flags) plus self-buff hediff givers. Social and utility casts (throne speech, solar
        /// pinhole) stay manual-mark only.
        /// </summary>
        internal static bool MassMarkable(Ability ability)
        {
            AbilityDef def = ability.def;
            // Displacement casts (skip class) are stuns-by-teleport whose psyfocus/heat
            // spend is a judgment call: manual mark only.
            if (HasTeleportEffect(def))
            {
                return false;
            }
            if (def.hostile || def.aiCanUse)
            {
                return true;
            }
            return ability.verb != null && ability.verb.targetParams.canTargetSelf
                && GivesAnyHediff(def);
        }

        private static bool HasTeleportEffect(AbilityDef def)
        {
            List<AbilityCompProperties> comps = def.comps;
            if (comps == null)
            {
                return false;
            }
            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i] is CompProperties_AbilityTeleport)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Whether any effect comp grants a hediff — the shape of a buff cast.</summary>
        internal static bool GivesAnyHediff(AbilityDef def)
        {
            List<AbilityCompProperties> comps = def.comps;
            if (comps == null)
            {
                return false;
            }
            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i] is CompProperties_AbilityGiveHediff give && give.hediffDef != null)
                {
                    return true;
                }
            }
            return false;
        }

        public static void ToggleFor(Pawn pawn, AbilityDef def)
        {
            CombatAutopilotData data = CombatAutopilotComponent.GetData(pawn);
            if (data == null)
            {
                return;
            }
            if (!data.AutocastAbilities.Remove(def))
            {
                data.AutocastAbilities.Add(def);
            }
            CombatAutopilotGizmoPatch.InterruptForRethink(pawn);
        }

        /// <summary>
        /// True when the autocast surfaces (menu rows, gizmo overlays) apply to this pawn:
        /// drafted player pawn holding a combat policy — the autopilot's master switch.
        /// </summary>
        public static bool AppliesTo(Pawn pawn)
        {
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings != null && !settings.EnableCombatAutopilot)
            {
                return false;
            }
            return pawn?.Faction != null && pawn.Faction.IsPlayer && pawn.Drafted
                && CombatAutopilotComponent.TryGetData(pawn)?.AssignedPolicy != null;
        }
    }

    /// <summary>
    /// Appends an "Enable/Disable autocast" entry to every ability command's right-click
    /// menu for drafted player pawns. One patch serves both audiences: vanilla shows the
    /// float menu on right-click, and the gizmo menu already reads
    /// RightClickFloatMenuOptions for the keyboard. Declared on Gizmo — Command_Ability
    /// inherits the getter — and guarded by instance type.
    /// </summary>
    [HarmonyPatch(typeof(Gizmo), nameof(Gizmo.RightClickFloatMenuOptions), MethodType.Getter)]
    public static class CombatAutopilotAbilityMenuPatch
    {
        public static IEnumerable<FloatMenuOption> Postfix(IEnumerable<FloatMenuOption> values, Gizmo __instance)
        {
            foreach (FloatMenuOption option in values)
            {
                yield return option;
            }
            if (!(__instance is Command_Ability command) || command.Ability?.def == null)
            {
                yield break;
            }
            Pawn pawn = command.Pawn;
            if (!CombatAutopilotAutocast.AppliesTo(pawn))
            {
                yield break;
            }
            AbilityDef def = command.Ability.def;
            yield return new CheckboxFloatMenuOption(
                "RimWorldAccess.Autopilot.AutocastOption".Translate(),
                () => CombatAutopilotAutocast.ToggleFor(pawn, def),
                () => CombatAutopilotComponent.TryGetData(pawn)?.AutocastFor(def) ?? false);
        }
    }

    /// <summary>
    /// Draws a small check mark on ability gizmos whose autocast is on, so sighted players
    /// see the state the way the workshop mods showed it. Draw-only: toggling lives in the
    /// right-click menu above.
    /// </summary>
    [HarmonyPatch(typeof(Command), "GizmoOnGUIInt")]
    public static class CombatAutopilotAbilityOverlayPatch
    {
        public static void Postfix(Command __instance, Rect butRect, GizmoRenderParms parms)
        {
            if (!(__instance is Command_Ability command) || command.Ability?.def == null)
            {
                return;
            }
            Pawn pawn = command.Pawn;
            if (!CombatAutopilotAutocast.AppliesTo(pawn))
            {
                return;
            }
            CombatAutopilotData data = CombatAutopilotComponent.TryGetData(pawn);
            if (data == null || !data.AutocastFor(command.Ability.def))
            {
                return;
            }
            float size = parms.shrunk ? 12f : 24f;
            GUI.DrawTexture(new Rect(butRect.x + butRect.width - size, butRect.y, size, size),
                Widgets.CheckboxOnTex);
        }
    }

}
