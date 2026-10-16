using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimWorldAccess
{
    /// <summary>
    /// Gate for the autopilot job giver: feature on, pawn drafted, and something to automate.
    /// The combat policy is the master switch for everything combat-shaped (search and
    /// destroy, engage orders, autocast, safety); the hunting system, patrol orders and a
    /// pending walk back to post run without one. Referenced by class name from
    /// Patches/CombatAutopilot.xml.
    /// </summary>
    public class ThinkNode_ConditionalCombatAutopilot : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            if (pawn == null || !pawn.Drafted)
            {
                return false;
            }
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings != null && !settings.EnableCombatAutopilot)
            {
                return false;
            }
            CombatAutopilotData data = CombatAutopilotComponent.TryGetData(pawn);
            if (data == null || data.Paused)
            {
                return false;
            }
            return data.AssignedPolicy != null
                || JobGiver_CombatAutopilot.HuntsAnimals(data)
                || data.PatrolArea != null
                || data.HoldPost.IsValid;
        }
    }

    [DefOf]
    public static class CombatAutopilotJobDefOf
    {
        public static JobDef RWA_GotoCover;
        public static JobDef RWA_GotoEngage;
        public static JobDef RWA_Retreat;
        public static JobDef RWA_ReturnToPost;
        public static JobDef RWA_Patrol;
        public static DesignationDef RWA_AttackTarget;

        static CombatAutopilotJobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(CombatAutopilotJobDefOf));
        }
    }

    /// <summary>
    /// The autopilot brain, consulted from the think tree just before the vanilla drafted
    /// wait (JobGiver_Orders). Priority ladder: safety (doctrine), the engage queue
    /// (explicit orders, served even while holding), attack marks (standing intent, for
    /// seek-and-destroy pawns), then stance — holding pawns autocast and run safety from
    /// the spot while vanilla Wait_Combat fires at whatever wanders into range;
    /// seek-and-destroy pawns acquire and engage targets, seeking cover per the doctrine,
    /// walking toward the nearest reachable hostile when nothing is attackable yet; idle
    /// pawns with a patrol area walk their beat. Hunt
    /// animals on: wild prey joins behind the hostile machinery, engaged through the
    /// player's own attack-order jobs. Deliberately
    /// skips the base class's hostility-response gate: the player opted this pawn in.
    /// Movement jobs use our Goto defs whose report strings say what the move is for, so
    /// the existing activity readers (colonist cycling, map cursor, inspect pane) speak
    /// the tactic; attack, cast and wait jobs already report meaningfully in vanilla.
    /// </summary>
    public class JobGiver_CombatAutopilot : JobGiver_AIFightEnemy
    {
        private const float MeleeHoldOffDistSq = 9f;
        private const int RepollTicks = 100;
        private const int ApproachExpiryTicks = 60;
        private const float MinCoverBlockChance = 0.24f;
        private const float FullAIScanRange = 900f;
        // Vanilla JobGiver_AIFightEnemy targetAcquireRadius default; free-roam prey only.
        private const float HuntAnimalsAcquireRadius = 56f;
        private const float FallBackHealthPct = 0.35f;
        // Retreat at 3/4 of the pawn's own pain shock threshold, before the game downs it.
        private const float PainShockFleeFraction = 0.75f;
        // Roughly double vanilla's Moving collapse floor (0.16); painless leg loss still retreats.
        private const float MovingFleeLevel = 0.35f;
        private const float RetreatThreatRadius = 32f;
        // Vanilla JobGiver_FleePotentialExplosion.FleeDist.
        private const float ExplosionFleeDist = 9f;
        // Thrown explosives can land one tile off their target; stand clear of that too.
        private const float BlastMargin = 1f;
        // Kiting: the safe distance scales with the foe's speed and the pawn's aim time —
        // a fresh shot must complete before the foe crosses into melee range.
        private const float KiteMinSafeDist = 6f;
        private const float KiteMaxSafeDist = 16f;
        private const float KiteMeleeBuffer = 3f;
        private const float KiteHysteresis = 2f;
        // Melee foe inside this radius = fast re-polls; also the reaction latency the
        // safe distance must absorb.
        private const float KiteAwarenessDist = 24f;
        private const int KiteVigilanceTicks = 30;
        private const float KiteAbortAimDist = 3f;
        private const float InterceptMeleeDist = 10f;
        private const float SquadRadius = 25f;
        // One empty tile between squadmates (stray shots pass, blasts catch one pawn).
        private const int AllySpacingDistSq = 2;
        private const int CoverMoveMaxDistSq = 144;
        private const float SidestepSearchRadius = 15f;
        // A leg shorter than this reads as jitter, not a beat.
        private const float MinPatrolLegDist = 10f;
        private const int PatrolCandidates = 12;
        private const float StandWatchChance = 0.35f;
        private static readonly IntRange StandWatchTicks = new IntRange(120, 600);

        private static readonly Func<StunHandler, DamageDef, bool> canBeStunnedByDamage =
            AccessTools.MethodDelegate<Func<StunHandler, DamageDef, bool>>(
                AccessTools.Method(typeof(StunHandler), "CanBeStunnedByDamage"));

        // Squad-spread pass state, set around FindAttackTarget's first pick and read by
        // ExtraTargetValidator inside vanilla's finder. Job assignment is single-threaded.
        private bool spreadPass;
        private bool spreadRejected;
        private int spreadCap;
        private Thing spreadOwnTarget;
        private static readonly Dictionary<Thing, int> squadTargetClaims = new Dictionary<Thing, int>();

        private struct BlastCircle
        {
            public IntVec3 Center;
            public float Radius;
        }

        private static readonly List<BlastCircle> tmpBlastCircles = new List<BlastCircle>();

        /// <summary>Vanilla's drafted-mech order gate: colony mechs act only inside overseer command range.</summary>
        private static bool InCommandRange(Pawn pawn, LocalTargetInfo target)
        {
            if (!ModsConfig.BiotechActive || !pawn.IsColonyMech)
            {
                return true;
            }
            return MechanitorUtility.InMechanitorCommandRange(pawn, target);
        }

        /// <summary>Any player pawn (the caster included unless ignored) inside the radius around the cell.</summary>
        private static bool AlliedPawnNear(Map map, IntVec3 center, float radius, Pawn ignore = null)
        {
            List<Pawn> colony = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < colony.Count; i++)
            {
                if (colony[i] != ignore && colony[i].Position.InHorDistOf(center, radius))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The AoE cast guard's no-splash rule: any pawn the caster has no quarrel with —
        /// colonists, allies, neutral visitors, prisoners — inside the radius. Factionless
        /// wildlife never vetoes a cast; manhunter-ing a squirrel is cheaper than holding fire.
        /// </summary>
        private static bool FriendlySplashNear(Pawn caster, IntVec3 center, float radius, bool ignoreCaster)
        {
            IReadOnlyList<Pawn> spawned = caster.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn p = spawned[i];
                if (ignoreCaster && p == caster)
                {
                    continue;
                }
                if ((p.Faction == null && p.HostFaction == null) || p.HostileTo(caster))
                {
                    continue;
                }
                if (p.Position.InHorDistOf(center, radius))
                {
                    return true;
                }
            }
            return false;
        }

        internal static bool HuntsAnimals(CombatAutopilotData data)
        {
            return data.HuntAnimals;
        }

        /// <summary>A pawn the automation must not act on: downed, dead, or psychologically invisible.</summary>
        private static bool DownedOrHidden(Thing thing)
        {
            return thing is Pawn pawn && (pawn.DeadOrDowned || pawn.IsPsychologicallyInvisible());
        }

        /// <summary>Autocast can act: marks exist, no policy suppresses them, and the pawn has abilities.</summary>
        private static bool Autocasting(Pawn pawn, CombatAutopilotData data)
        {
            return data.AutocastActive && data.AutocastAbilities.Count > 0 && pawn.abilities != null;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            Job job = TryGiveJobInner(pawn);
            CombatAutopilotDebug.Record(pawn, job);
            return job;
        }

        private Job TryGiveJobInner(Pawn pawn)
        {
            if (pawn == null || !pawn.Drafted)
            {
                return null;
            }
            CombatAutopilotData data = CombatAutopilotComponent.TryGetData(pawn);
            if (data == null || data.Paused)
            {
                return null;
            }
            CombatPolicy policy = data.AssignedPolicy;
            // Wounded discipline: fall back from enemies pressing the attack, then keep
            // fighting carefully from cover at range — never charge, roam or patrol.
            bool wounded = policy != null && policy.FallBackInjured && BadlyHurt(pawn);
            Job safety = TrySafetyJob(pawn, data, policy, wounded);
            if (safety != null)
            {
                RecordHoldPost(pawn, data);
                return safety;
            }
            if (policy != null && policy.KeepDistance)
            {
                Job kite = TryKiteJob(pawn);
                if (kite != null)
                {
                    RecordHoldPost(pawn, data);
                    return kite;
                }
            }
            // An explicit engage order outranks every stance; an unreachable order stays
            // queued and is re-tried each poll while the pawn falls back to its stance.
            // Combat orders require a policy — the policy is the autopilot's master switch.
            Thing ordered = policy != null ? data.CurrentOrderedTarget() : null;
            if (ordered != null)
            {
                Job intercept = TryFightThreatEnRoute(pawn, data, ordered, wounded);
                if (intercept != null)
                {
                    RecordHoldPost(pawn, data);
                    return intercept;
                }
                Job engage = TryEngagePinnedTarget(pawn, data, ordered,
                    "RimWorldAccess.Autopilot.Report.EngagingOrdered".Translate(ordered.LabelShort),
                    ignoreChargeGate: true, wounded);
                if (engage != null)
                {
                    RecordHoldPost(pawn, data);
                    return engage;
                }
            }
            bool seekHostiles = data.SeekAndDestroy && policy != null;
            bool huntsAnimals = HuntsAnimals(data);
            if (seekHostiles)
            {
                data.HoldPost = IntVec3.Invalid;
                // Attack marks are standing player intent: they outrank ordinary hostiles
                // for hunters, while holding pawns keep their post.
                Thing marked = FindMarkedTarget(pawn, policy);
                if (marked != null)
                {
                    Job engage = TryEngagePinnedTarget(pawn, data, marked,
                        "RimWorldAccess.Autopilot.Report.EngagingMarked".Translate(marked.LabelShort),
                        ignoreChargeGate: false, wounded);
                    if (engage != null)
                    {
                        return engage;
                    }
                }
            }
            if (!seekHostiles && !huntsAnimals)
            {
                Job patrol = wounded ? null : TryPatrolJob(pawn, data);
                if (patrol != null)
                {
                    return patrol;
                }
                if (data.HoldPost.IsValid)
                {
                    Job fight = TryStandAndFight(pawn, data);
                    if (fight != null)
                    {
                        return fight;
                    }
                }
                Job returnJob = TryReturnToPost(pawn, data);
                if (returnJob != null)
                {
                    return returnJob;
                }
                Job hold = TryHoldModeJob(pawn, data);
                if (hold != null)
                {
                    return hold;
                }
                // Pending-but-unservable automation (an unreachable order, an idled patrol)
                // must keep polling: null hands vanilla's never-expiring drafted wait.
                return ordered != null || data.PatrolArea != null ? RepollWait(pawn) : null;
            }
            UpdateEnemyTarget(pawn);
            Thing enemy = pawn.mindState.enemyTarget;
            if (DownedOrHidden(enemy))
            {
                enemy = null;
            }
            if (enemy != null && !seekHostiles)
            {
                // A hittable threat trumps prey: the expiring wait's vanilla auto-attack
                // engages it; chasing hostiles stays opt-in via the policy's Hunt checkbox.
                Verb attackVerb = pawn.TryGetAttackVerb(enemy);
                if (attackVerb != null && attackVerb.CanHitTarget(enemy))
                {
                    return RepollWait(pawn);
                }
                enemy = null;
            }
            if (enemy != null)
            {
                Job abilityJob = TryAutocastJob(pawn, data, enemy);
                if (abilityJob != null)
                {
                    return abilityJob;
                }
                return TryWeaponJob(pawn, data, enemy, pinTarget: false, wounded: wounded);
            }
            // Any active map threat pauses hunting, designated animals included.
            if (huntsAnimals && !wounded && !GenHostility.AnyHostileActiveThreatToPlayer(pawn.Map))
            {
                Thing animal = FindHuntableAnimal(pawn);
                if (animal != null)
                {
                    return TryWeaponJob(pawn, data, animal, pinTarget: true);
                }
            }
            if (seekHostiles && !wounded)
            {
                Job approach = TryApproachNearestHostile(pawn);
                if (approach != null)
                {
                    return approach;
                }
            }
            // Nothing to fight: walk the beat if one is assigned.
            Job idlePatrol = wounded ? null : TryPatrolJob(pawn, data);
            if (idlePatrol != null)
            {
                return idlePatrol;
            }
            // Idle hunters and seekers need an expiring wait: jobs only re-check on
            // expiry or damage, so a null here would sleep through the next raid.
            return RepollWait(pawn, wounded ? WoundedReport() : null);
        }

        /// <summary>A holding pawn about to be displaced remembers where it belongs; roaming stances never do.</summary>
        private static void RecordHoldPost(Pawn pawn, CombatAutopilotData data)
        {
            if (!data.SeekAndDestroy && !HuntsAnimals(data) && data.PatrolArea == null
                && !data.HoldPost.IsValid)
            {
                data.HoldPost = pawn.Position;
            }
        }

        /// <summary>
        /// Walks a displaced holding pawn back to its post once nothing demands
        /// otherwise. A post that became unreachable or invalid (a trap built on it
        /// included) is dropped; arrival clears it; drafted move orders and
        /// undrafting clear it elsewhere.
        /// </summary>
        private static Job TryReturnToPost(Pawn pawn, CombatAutopilotData data)
        {
            if (!data.HoldPost.IsValid)
            {
                return null;
            }
            IntVec3 post = data.HoldPost;
            if (pawn.Position == post)
            {
                data.HoldPost = IntVec3.Invalid;
                return null;
            }
            if (!post.InBounds(pawn.Map) || !post.Standable(pawn.Map) || !InCommandRange(pawn, post)
                || !TrapSafe(pawn, post) || !pawn.CanReach(post, PathEndMode.OnCell, Danger.Deadly))
            {
                data.HoldPost = IntVec3.Invalid;
                return null;
            }
            Job job = JobMaker.MakeJob(CombatAutopilotJobDefOf.RWA_ReturnToPost, post);
            job.reportStringOverride =
                "RimWorldAccess.Autopilot.Report.ReturningToPost".Translate(post.x, post.z);
            job.expiryInterval = RepollTicks;
            job.checkOverrideOnExpire = true;
            job.collideWithPawns = true;
            return job;
        }

        /// <summary>
        /// Walks the assigned beat when nothing is fighting the pawn: a hittable enemy
        /// stands it fast (a Goto job never auto-attacks, so a patroller must stop to
        /// shoot), legs persist across re-polls, and arrival sometimes stands watch
        /// before the next leg.
        /// </summary>
        private Job TryPatrolJob(Pawn pawn, CombatAutopilotData data)
        {
            Area area = ValidPatrolArea(pawn, data);
            if (area == null)
            {
                return null;
            }
            Job fight = TryStandAndFight(pawn, data);
            if (fight != null)
            {
                return fight;
            }
            IntVec3 dest = data.PatrolDest;
            if (dest.IsValid && dest == pawn.Position)
            {
                data.PatrolDest = IntVec3.Invalid;
                if (Rand.Chance(StandWatchChance))
                {
                    return JobMaker.MakeJob(JobDefOf.Wait_Combat, StandWatchTicks.RandomInRange,
                        checkOverrideOnExpiry: true);
                }
                dest = IntVec3.Invalid;
            }
            if (!dest.IsValid || !area[dest]
                || !pawn.CanReach(dest, PathEndMode.OnCell, Danger.Deadly))
            {
                dest = FindPatrolDest(pawn, area);
                if (!dest.IsValid)
                {
                    return null;
                }
                data.PatrolDest = dest;
            }
            Job job = JobMaker.MakeJob(CombatAutopilotJobDefOf.RWA_Patrol, dest);
            job.reportStringOverride = "RimWorldAccess.Autopilot.Report.Patrolling".Translate(area.Label);
            job.locomotionUrgency = LocomotionUrgency.Walk;
            job.expiryInterval = VigilantExpiry(pawn, RepollTicks * 3);
            job.checkOverrideOnExpire = true;
            job.collideWithPawns = true;
            return job;
        }

        /// <summary>
        /// Stops a traveling pawn to fight a hittable enemy: a Goto job never
        /// auto-attacks, so patrol legs and post returns must stand fast first.
        /// </summary>
        private Job TryStandAndFight(Pawn pawn, CombatAutopilotData data)
        {
            UpdateEnemyTarget(pawn);
            Thing enemy = pawn.mindState.enemyTarget;
            if (enemy == null || DownedOrHidden(enemy))
            {
                return null;
            }
            Verb verb = pawn.TryGetAttackVerb(enemy);
            if (verb == null || !verb.CanHitTarget(enemy))
            {
                return null;
            }
            Job cast = TryAutocastJob(pawn, data, enemy);
            return cast ?? FiringWait(pawn);
        }

        /// <summary>An invalid area (deleted, emptied, another map) idles the order but keeps the assignment.</summary>
        private static Area ValidPatrolArea(Pawn pawn, CombatAutopilotData data)
        {
            Area area = data.PatrolArea;
            if (area == null || area.Map != pawn.Map || area.TrueCount == 0
                || !pawn.Map.areaManager.AllAreas.Contains(area))
            {
                return null;
            }
            return area;
        }

        /// <summary>
        /// One reservoir-sampling pass over the area's cells, then the first valid
        /// candidate — long legs first, so the walk reads as a beat.
        /// </summary>
        private static IntVec3 FindPatrolDest(Pawn pawn, Area area)
        {
            var candidates = new List<IntVec3>(PatrolCandidates);
            int seen = 0;
            foreach (IntVec3 cell in area.ActiveCells)
            {
                seen++;
                if (candidates.Count < PatrolCandidates)
                {
                    candidates.Add(cell);
                }
                else if (Rand.Range(0, seen) < PatrolCandidates)
                {
                    candidates[Rand.Range(0, PatrolCandidates)] = cell;
                }
            }
            candidates.SortByDescending(c => c.DistanceToSquared(pawn.Position));
            IntVec3 fallback = IntVec3.Invalid;
            foreach (IntVec3 cell in candidates)
            {
                if (cell == pawn.Position || cell.Fogged(pawn.Map) || !cell.Standable(pawn.Map)
                    || !TrapSafe(pawn, cell) || !InCommandRange(pawn, cell)
                    || !pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                {
                    continue;
                }
                if (cell.DistanceToSquared(pawn.Position) >= MinPatrolLegDist * MinPatrolLegDist)
                {
                    return cell;
                }
                if (!fallback.IsValid)
                {
                    fallback = cell;
                }
            }
            return fallback;
        }

        /// <summary>Vanilla's own known-trap predicate; modded trap types follow automatically.</summary>
        private static bool TrapSafe(Pawn pawn, IntVec3 cell)
        {
            return !PawnUtility.KnownDangerAt(cell, pawn.Map, pawn);
        }

        /// <summary>
        /// The direct flee finder checks terrain but never known traps; retry for a
        /// trap-free cell, then accept the last candidate — a 0.5% spring beats
        /// standing in a blast.
        /// </summary>
        private static bool TryFindTrapSafeFleeDest(IntVec3 root, float dist, Pawn pawn, out IntVec3 dest)
        {
            for (int i = 0; i < 3; i++)
            {
                if (RCellFinder.TryFindDirectFleeDestination(root, dist, pawn, out dest)
                    && TrapSafe(pawn, dest))
                {
                    return true;
                }
            }
            return RCellFinder.TryFindDirectFleeDestination(root, dist, pawn, out dest);
        }

        /// <summary>
        /// Holding automation: the pawn stays where the player put it. Autocast fires from
        /// the spot; a doctrine with safety behaviors gets the same shadow wait so blast
        /// avoidance and fall-back keep re-polling while the pawn holds.
        /// </summary>
        private Job TryHoldModeJob(Pawn pawn, CombatAutopilotData data)
        {
            CombatPolicy policy = data.AssignedPolicy;
            bool autocasting = Autocasting(pawn, data);
            bool safetyPolling = policy != null
                && (policy.FallBackInjured || policy.AvoidBlasts || policy.KeepDistance);
            if (!autocasting && !safetyPolling)
            {
                return null;
            }
            if (autocasting)
            {
                UpdateEnemyTarget(pawn);
                Thing enemy = pawn.mindState.enemyTarget;
                if (enemy != null)
                {
                    Job cast = TryAutocastJob(pawn, data, enemy);
                    if (cast != null)
                    {
                        return cast;
                    }
                }
            }
            // The vanilla drafted wait never expires, so nothing would ever re-poll the
            // autocast list. Shadow it with an expiring wait that behaves identically.
            return JobMaker.MakeJob(JobDefOf.Wait_Combat, VigilantExpiry(pawn, RepollTicks),
                checkOverrideOnExpiry: true);
        }

        /// <summary>
        /// Serves an explicit target — the engage queue or an attack mark — through the
        /// normal weapon machinery with the target pinned. Null when the pawn cannot act on
        /// it yet (out of command range, or neither hittable nor reachable): the caller
        /// keeps the order and falls back to stance behavior until the next poll.
        /// </summary>
        private Job TryEngagePinnedTarget(Pawn pawn, CombatAutopilotData data, Thing target,
            string report, bool ignoreChargeGate, bool wounded = false)
        {
            if (!InCommandRange(pawn, target))
            {
                return null;
            }
            Job cast = TryAutocastJob(pawn, data, target);
            if (cast != null)
            {
                return cast;
            }
            // An ability-only pawn serves the order by casting alone.
            Verb verb = pawn.TryGetAttackVerb(target);
            if (verb == null)
            {
                return null;
            }
            CombatPolicy policy = data.AssignedPolicy;
            bool canBash = policy != null && policy.BashDoors;
            if (!verb.CanHitTarget(target)
                && !pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly, canBashDoors: canBash))
            {
                return null;
            }
            return TryWeaponJob(pawn, data, target, pinTarget: true, pinnedReport: report,
                ignoreChargeGate: ignoreChargeGate, wounded: wounded);
        }

        /// <summary>
        /// The problem at hand outranks a pinned order: a melee hostile on this pawn, any
        /// enemy actually attacking it (ranged included), or any enemy it can already hit
        /// becomes the target until dead, downed or broken off — then the order resumes.
        /// Without this an engage march walks through a firing line without shooting back.
        /// </summary>
        private Job TryFightThreatEnRoute(Pawn pawn, CombatAutopilotData data, Thing ordered, bool wounded)
        {
            Pawn interceptor = pawn.mindState.MeleeThreatStillThreat ? pawn.mindState.meleeThreat : null;
            if (interceptor == null)
            {
                interceptor = ClosestMeleeThreat(pawn, InterceptMeleeDist,
                    hostile => IsTargetingPawn(hostile, pawn));
            }
            if (interceptor != null && interceptor != ordered)
            {
                return TryEngagePinnedTarget(pawn, data, interceptor,
                    "RimWorldAccess.Autopilot.Report.FightingOff".Translate(interceptor.LabelShort),
                    ignoreChargeGate: false, wounded);
            }
            UpdateEnemyTarget(pawn);
            Thing enemy = pawn.mindState.enemyTarget;
            if (enemy == null || enemy == ordered || DownedOrHidden(enemy))
            {
                return null;
            }
            Verb verb = pawn.TryGetAttackVerb(enemy);
            bool hittable = verb != null && verb.CanHitTarget(enemy);
            bool attackingUs = enemy is Pawn hostilePawn && IsTargetingPawn(hostilePawn, pawn);
            if (!hittable && !attackingUs)
            {
                return null;
            }
            return TryEngagePinnedTarget(pawn, data, enemy,
                "RimWorldAccess.Autopilot.Report.FightingOff".Translate(enemy.LabelShort),
                ignoreChargeGate: false, wounded);
        }

        private static bool IsTargetingPawn(Pawn hostile, Pawn pawn)
        {
            return hostile.mindState?.enemyTarget == pawn
                || (hostile.CurJob != null && hostile.CurJob.AnyTargetIs(pawn));
        }

        /// <summary>Nearest live attack-marked thing this pawn can act on; marks are player intent, hostility not required.</summary>
        private static Thing FindMarkedTarget(Pawn pawn, CombatPolicy policy)
        {
            Map map = pawn.Map;
            bool canBash = policy != null && policy.BashDoors;
            float bestDistSq = float.MaxValue;
            Thing best = null;
            foreach (Designation designation in map.designationManager
                .SpawnedDesignationsOfDef(CombatAutopilotJobDefOf.RWA_AttackTarget))
            {
                Thing thing = designation.target.Thing;
                if (thing == null || !thing.Spawned || thing.Position.Fogged(map) || DownedOrHidden(thing))
                {
                    continue;
                }
                float distSq = thing.Position.DistanceToSquared(pawn.Position);
                if (distSq >= bestDistSq || !InCommandRange(pawn, thing))
                {
                    continue;
                }
                Verb verb = pawn.TryGetAttackVerb(thing);
                if (verb == null)
                {
                    continue;
                }
                if (!verb.CanHitTarget(thing)
                    && !pawn.CanReach(thing, PathEndMode.Touch, Danger.Deadly, canBashDoors: canBash))
                {
                    continue;
                }
                bestDistSq = distSq;
                best = thing;
            }
            return best;
        }

        private Job TryWeaponJob(Pawn pawn, CombatAutopilotData data, Thing enemy, bool pinTarget,
            string pinnedReport = null, bool ignoreChargeGate = false, bool wounded = false)
        {
            // Never hand weapon violence to a violence-incapable pawn (vanilla guards its
            // own auto-attack the same way); casting alone serves such pawns.
            if (!CombatAutopilotOrders.ViolentCapable(pawn))
            {
                return RepollWait(pawn);
            }
            CombatPolicy policy = data.AssignedPolicy;
            string waitReport = wounded ? WoundedReport() : null;
            // Fighting with no policy keeps the pre-policy defaults: charge, no cover.
            bool meleeCharge = (ignoreChargeGate || policy == null || policy.MeleeCharge) && !wounded;
            bool takeCover = (wounded || (policy != null && policy.TakeCover)) && CoverUseful(enemy);
            bool bashDoors = policy != null && policy.BashDoors;
            bool autocasting = Autocasting(pawn, data);
            Verb verb = pawn.TryGetAttackVerb(enemy);
            if (verb == null)
            {
                return null;
            }
            if (verb.verbProps.IsMeleeAttack)
            {
                if (!meleeCharge
                    && (pawn.Position - enemy.Position).LengthHorizontalSquared > MeleeHoldOffDistSq)
                {
                    return RepollWait(pawn, waitReport);
                }
                // The short expiry lets marked abilities interject; the enemies-nearby
                // requirement must drop with it (a pinned target need not count as an enemy).
                Job melee = MeleeAttackJob(pawn, enemy);
                if (autocasting)
                {
                    melee.expiryInterval = RepollTicks;
                }
                if (pinTarget || autocasting)
                {
                    melee.expireRequiresEnemiesNearby = false;
                }
                melee.canBashDoors = bashDoors;
                string meleeReport = pinnedReport ?? SpreadReport(data, enemy, pinTarget);
                if (meleeReport != null)
                {
                    melee.reportStringOverride = meleeReport;
                }
                return melee;
            }
            // Never lob explosives at a target with a colony pawn inside the weapon's
            // own friendly-fire radius; vanilla only score-penalizes such shots.
            if (verb.verbProps.ai_AvoidFriendlyFireRadius > 0f
                && AlliedPawnNear(pawn.Map, enemy.Position, verb.verbProps.ai_AvoidFriendlyFireRadius))
            {
                return RepollWait(pawn, waitReport);
            }
            bool inCover = CoverUtility.CalculateOverallBlockChance(pawn, enemy.Position, pawn.Map)
                >= (takeCover ? MinCoverBlockChance : 0f);
            bool canStay = pawn.Position.Standable(pawn.Map)
                && pawn.Map.pawnDestinationReservationManager.CanReserve(pawn.Position, pawn, pawn.Drafted);
            bool canHit = verb.CanHitTarget(enemy);
            bool inRange = (pawn.Position - enemy.Position).LengthHorizontalSquared
                < verb.verbProps.range * verb.verbProps.range;
            if (inCover && canStay && canHit && inRange)
            {
                data.CastDest = IntVec3.Invalid;
                return pinTarget ? PinnedShootJob(pawn, enemy, pinnedReport) : FiringWait(pawn);
            }
            // A committed destination is kept while it still works: re-running the finder
            // every poll zigzagged pawns between fresh picks instead of arriving anywhere.
            if (StickyDestValid(pawn, data, enemy, verb))
            {
                return GotoJob(pawn, data.CastDest, seekingCover: canHit && inRange, enemy,
                    SpreadReport(data, enemy, pinTarget));
            }
            if (!TryFindCastPosition(pawn, data, enemy, verb, preferCurrent: true, wounded, out IntVec3 dest))
            {
                return RepollWait(pawn, waitReport);
            }
            if (dest == pawn.Position)
            {
                // The finder settled for the current spot; when cover was wanted and this
                // spot has none, ask again without the current-position preference — but
                // only move for real cover a short walk away, else stand and shoot.
                if (takeCover && !inCover
                    && TryFindCastPosition(pawn, data, enemy, verb, preferCurrent: false, wounded, out IntVec3 coverDest)
                    && coverDest != pawn.Position
                    && CoverWorthMoving(pawn, coverDest, enemy))
                {
                    data.CastDest = coverDest;
                    data.CastDestEnemy = enemy;
                    return GotoJob(pawn, coverDest, seekingCover: true, enemy);
                }
                // An unhittable pinned target gets the wait: AttackStatic would end instantly.
                if (pinTarget)
                {
                    return canHit ? PinnedShootJob(pawn, enemy, pinnedReport) : RepollWait(pawn, waitReport);
                }
                return canHit ? FiringWait(pawn) : RepollWait(pawn, waitReport);
            }
            data.CastDest = dest;
            data.CastDestEnemy = enemy;
            return GotoJob(pawn, dest, seekingCover: canHit && inRange, enemy,
                SpreadReport(data, enemy, pinTarget));
        }

        /// <summary>"Spreading fire" engage report, only while the squad plan genuinely redirected this pick.</summary>
        private static string SpreadReport(CombatAutopilotData data, Thing enemy, bool pinTarget)
        {
            if (pinTarget || data.SpreadFireTarget != enemy)
            {
                return null;
            }
            return "RimWorldAccess.Autopilot.Report.SpreadingFire".Translate(enemy.LabelShort);
        }

        /// <summary>The committed firing position still serves this enemy and can be walked to.</summary>
        private static bool StickyDestValid(Pawn pawn, CombatAutopilotData data, Thing enemy, Verb verb)
        {
            IntVec3 dest = data.CastDest;
            if (!dest.IsValid || data.CastDestEnemy != enemy || dest == pawn.Position)
            {
                return false;
            }
            Map map = pawn.Map;
            return dest.InBounds(map) && dest.Standable(map) && TrapSafe(pawn, dest)
                && InCommandRange(pawn, dest) && verb.CanHitTargetFrom(dest, enemy)
                && pawn.CanReach(dest, PathEndMode.OnCell, Danger.Deadly);
        }

        /// <summary>
        /// Cover only blocks projectiles: seek it against a shooter or turret, never
        /// against a melee-only foe — kiting handles those.
        /// </summary>
        private static bool CoverUseful(Thing enemy)
        {
            Verb verb = (enemy as Pawn)?.CurrentEffectiveVerb
                ?? (enemy as Building_Turret)?.AttackVerb;
            return verb != null && !verb.verbProps.IsMeleeAttack;
        }

        private static bool CoverWorthMoving(Pawn pawn, IntVec3 dest, Thing enemy)
        {
            return dest.DistanceToSquared(pawn.Position) <= CoverMoveMaxDistSq
                && CoverUtility.CalculateOverallBlockChance(dest, enemy.Position, pawn.Map)
                    >= MinCoverBlockChance;
        }

        /// <summary>Hunt with no acquirable target: close on the nearest reachable hostile.</summary>
        private Job TryApproachNearestHostile(Pawn pawn)
        {
            float bestDistSq = float.MaxValue;
            Thing best = null;
            foreach (IAttackTarget candidate in pawn.Map.attackTargetsCache.GetPotentialTargetsFor(pawn))
            {
                if (candidate.ThreatDisabled(pawn) || !AttackTargetFinder.IsAutoTargetable(candidate))
                {
                    continue;
                }
                Thing thing = candidate.Thing;
                if (!ExtraTargetValidator(pawn, thing))
                {
                    continue;
                }
                float distSq = thing.Position.DistanceToSquared(pawn.Position);
                if (distSq < bestDistSq && pawn.CanReach(thing, PathEndMode.Touch, Danger.Deadly))
                {
                    bestDistSq = distSq;
                    best = thing;
                }
            }
            if (best == null)
            {
                return null;
            }
            Job job = JobMaker.MakeJob(CombatAutopilotJobDefOf.RWA_GotoEngage, best);
            job.reportStringOverride = "RimWorldAccess.Autopilot.Report.MovingToEngage".Translate(best.LabelShort);
            job.checkOverrideOnExpire = true;
            job.collideWithPawns = true;
            job.expiryInterval = ApproachExpiryTicks;
            return job;
        }

        /// <summary>
        /// Nearest wild animal the pawn's hunting policy allows, within the acquire radius,
        /// past the attack order's willingness gates. The policy outranks hunt designations
        /// entirely — a designation neither qualifies nor prioritizes an animal here, so
        /// designated safe game stays available to the colony's undrafted work hunters.
        /// </summary>
        private static Thing FindHuntableAnimal(Pawn pawn)
        {
            HuntingPolicy huntPolicy = CombatAutopilotComponent.EffectiveHuntPolicy(pawn);
            bool veneratedSkip = huntPolicy.SpareVenerated;
            Map map = pawn.Map;
            float maxDistSq = HuntAnimalsAcquireRadius * HuntAnimalsAcquireRadius;
            float bestDistSq = float.MaxValue;
            Pawn best = null;
            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn animal = spawned[i];
                if (animal == pawn || animal.Faction != null || !WildManUtility.AnimalOrWildMan(animal))
                {
                    continue;
                }
                if (animal.DeadOrDowned || animal.IsPsychologicallyInvisible()
                    || animal.IsPrisonerInPrisonCell() || animal.Position.Fogged(map))
                {
                    continue;
                }
                float distSq = animal.Position.DistanceToSquared(pawn.Position);
                if (distSq >= bestDistSq || distSq > maxDistSq)
                {
                    continue;
                }
                if (!huntPolicy.Allows(animal))
                {
                    continue;
                }
                if (map.designationManager.DesignationOn(animal, DesignationDefOf.Tame) != null)
                {
                    continue;
                }
                if (veneratedSkip && pawn.Ideo != null && pawn.Ideo.IsVeneratedAnimal(animal))
                {
                    continue;
                }
                if (!WillingToHunt(pawn, animal) || !InCommandRange(pawn, animal)
                    || !pawn.CanReach(animal, PathEndMode.Touch, Danger.Deadly))
                {
                    continue;
                }
                bestDistSq = distSq;
                best = animal;
            }
            return best;
        }

        /// <summary>The attack order's own refusals (FloatMenuUtility); Verb.ValidateTarget re-checks at fire time.</summary>
        private static bool WillingToHunt(Pawn hunter, Pawn animal)
        {
            if (hunter.Ideo != null && hunter.Ideo.IsVeneratedAnimal(animal)
                && !new HistoryEvent(HistoryEventDefOf.HuntedVeneratedAnimal, hunter.Named(HistoryEventArgsNames.Doer)).DoerWillingToDo())
            {
                return false;
            }
            if (HistoryEventUtility.IsKillingInnocentAnimal(hunter, animal)
                && !new HistoryEvent(HistoryEventDefOf.KilledInnocentAnimal, hunter.Named(HistoryEventArgsNames.Doer)).DoerWillingToDo())
            {
                return false;
            }
            return true;
        }

        /// <summary>The player's own ranged attack order, which pins the target; the drafted wait's auto-attack never fires at non-hostiles.</summary>
        private static Job PinnedShootJob(Pawn pawn, Thing target, string report)
        {
            Job job = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
            job.endIfCantShootTargetFromCurPos = true;
            job.expiryInterval = VigilantExpiry(pawn, ExpiryInterval_ShooterSucceeded.RandomInRange / 3);
            job.checkOverrideOnExpire = true;
            if (report != null)
            {
                job.reportStringOverride = report;
            }
            return job;
        }

        private static Job TryAutocastJob(Pawn pawn, CombatAutopilotData data, Thing enemy)
        {
            if (!Autocasting(pawn, data))
            {
                return null;
            }
            Job selfBuff = TrySelfBuffJob(pawn, data);
            if (selfBuff != null)
            {
                return selfBuff;
            }
            return TryOffensiveCastJob(pawn, data, enemy);
        }

        private static Job TrySelfBuffJob(Pawn pawn, CombatAutopilotData data)
        {
            if (!Autocasting(pawn, data))
            {
                return null;
            }
            List<Ability> all = pawn.abilities.AllAbilitiesForReading;
            for (int i = 0; i < all.Count; i++)
            {
                Ability ability = all[i];
                // Self casts must be actual buffs: non-hostile and hediff-granting, or a
                // marked skip/pulse self-casts in place every poll.
                if (!data.AutocastFor(ability.def) || ability.def.hostile
                    || !ability.verb.targetParams.canTargetSelf
                    || !CombatAutopilotAutocast.GivesAnyHediff(ability.def))
                {
                    continue;
                }
                // A violent self-centered area cast (fire burst class) must not roast allies.
                if (ability.def.HasAreaOfEffect && ability.verb.verbProps.violent
                    && FriendlySplashNear(pawn, pawn.Position, ability.def.EffectRadius, ignoreCaster: true))
                {
                    continue;
                }
                if (AutocastCanTargetNow(ability, pawn))
                {
                    return WithCastReport(ability.GetJob(pawn, pawn), ability, null);
                }
            }
            return null;
        }

        private static Job TryOffensiveCastJob(Pawn pawn, CombatAutopilotData data, Thing enemy)
        {
            if (!pawn.Position.Standable(pawn.Map)
                || !pawn.Map.pawnDestinationReservationManager.CanReserve(pawn.Position, pawn, pawn.Drafted))
            {
                return null;
            }
            List<Ability> all = pawn.abilities.AllAbilitiesForReading;
            for (int i = 0; i < all.Count; i++)
            {
                Ability ability = all[i];
                // def.hostile is vanilla's "aggressive act" flag: it keeps social and utility
                // casts (throne speech, focus, solar pinhole) off enemies.
                if (!data.AutocastFor(ability.def) || !ability.def.hostile)
                {
                    continue;
                }
                // An area cast lands on friendlies near the enemy; vanilla's picker never
                // checks. The caster counts too: a melee-range pulse would catch itself.
                if (ability.def.HasAreaOfEffect
                    && FriendlySplashNear(pawn, enemy.Position, ability.def.EffectRadius, ignoreCaster: false))
                {
                    continue;
                }
                if (AutocastCanTargetNow(ability, enemy) && ability.verb.CanHitTarget(enemy))
                {
                    return WithCastReport(ability.GetJob(enemy, enemy), ability, enemy);
                }
            }
            for (int i = 0; i < all.Count; i++)
            {
                Ability ability = all[i];
                if (!data.AutocastFor(ability.def) || !ability.def.hostile || !ability.CanCast)
                {
                    continue;
                }
                LocalTargetInfo aoeTarget = ability.AIGetAOETarget();
                if (aoeTarget.IsValid
                    && !FriendlySplashNear(pawn, aoeTarget.Cell, ability.def.EffectRadius, ignoreCaster: false))
                {
                    return WithCastReport(ability.GetJob(aoeTarget, aoeTarget), ability, null);
                }
            }
            return null;
        }

        /// <summary>
        /// The autocast gate: cooldown/charges/psyfocus/neural heat (CanCast), the
        /// ability's own targeting and validity rules, each effect comp's AI targeting
        /// judgment, and a redundancy guard. The def.aiCanUse FLAG is deliberately not
        /// required — the player's mark replaces it. (Drafted Auto-Combat flips that flag
        /// around vanilla's AICanTargetNow wrapper for the same effect; consulting the
        /// comps directly avoids mutating the def.)
        /// </summary>
        private static bool AutocastCanTargetNow(Ability ability, Thing target)
        {
            if (target == null || !ability.CanCast)
            {
                return false;
            }
            if (!ability.verb.targetParams.CanTarget(target))
            {
                return false;
            }
            if (!ability.CanApplyOn((LocalTargetInfo)target))
            {
                return false;
            }
            List<CompAbilityEffect> comps = ability.EffectComps;
            if (comps != null)
            {
                for (int i = 0; i < comps.Count; i++)
                {
                    if (!comps[i].AICanTargetNow((LocalTargetInfo)target))
                    {
                        return false;
                    }
                }
            }
            return !WouldOnlyRefreshHediffs(ability, target);
        }

        /// <summary>
        /// True when every hediff the ability would give is already on its recipient —
        /// re-casting would only refresh durations, burning psyfocus every re-poll.
        /// </summary>
        private static bool WouldOnlyRefreshHediffs(Ability ability, Thing target)
        {
            bool givesAnyHediff = false;
            List<AbilityCompProperties> comps = ability.def.comps;
            if (comps == null)
            {
                return false;
            }
            for (int i = 0; i < comps.Count; i++)
            {
                if (!(comps[i] is CompProperties_AbilityGiveHediff give) || give.hediffDef == null)
                {
                    continue;
                }
                givesAnyHediff = true;
                if (!give.onlyApplyToSelf && give.applyToTarget && target is Pawn targetPawn
                    && !targetPawn.health.hediffSet.HasHediff(give.hediffDef))
                {
                    return false;
                }
                if ((give.applyToSelf || give.onlyApplyToSelf)
                    && !ability.pawn.health.hediffSet.HasHediff(give.hediffDef))
                {
                    return false;
                }
            }
            return givesAnyHediff;
        }

        protected override Thing FindAttackTarget(Pawn pawn)
        {
            CombatAutopilotData data = CombatAutopilotComponent.TryGetData(pawn);
            CombatPolicy policy = data?.AssignedPolicy;
            if (policy != null && policy.FightAsSquad && TryBuildSquadPlan(pawn))
            {
                // First pick skips targets already claimed by enough squadmates; a fully
                // claimed field falls through to the normal pick (concentration beats idling).
                spreadPass = true;
                spreadRejected = false;
                spreadOwnTarget = pawn.mindState.enemyTarget;
                Thing spread;
                try
                {
                    spread = FindAttackTargetCore(pawn, data, policy);
                }
                finally
                {
                    spreadPass = false;
                    spreadOwnTarget = null;
                }
                if (spread != null)
                {
                    // "Spreading fire" is spoken only when the cap genuinely changed the
                    // pick; the plain pick re-runs solely when crowding pruned candidates.
                    Thing plain = spreadRejected ? FindAttackTargetCore(pawn, data, policy) : spread;
                    data.SpreadFireTarget = plain != spread ? spread : null;
                    return spread;
                }
            }
            Thing normal = FindAttackTargetCore(pawn, data, policy);
            if (data != null)
            {
                data.SpreadFireTarget = null;
            }
            return normal;
        }

        private Thing FindAttackTargetCore(Pawn pawn, CombatAutopilotData data, CombatPolicy policy)
        {
            if (data != null && data.SeekAndDestroy && policy != null && policy.PursueAnywhere)
            {
                return (Thing)AttackTargetFinder.BestAttackTarget(pawn,
                    TargetScanFlags.NeedReachableIfCantHitFromMyPos | TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable,
                    thing => IsFullAITarget(thing) && ExtraTargetValidator(pawn, thing),
                    0f, FullAIScanRange, default(IntVec3), float.MaxValue,
                    canBashDoors: policy.BashDoors, canTakeTargetsCloserThanEffectiveMinRange: true, canBashFences: false);
            }
            return base.FindAttackTarget(pawn);
        }

        /// <summary>Counts squadmates' committed targets; cap = even split across live threats. False = fighting alone.</summary>
        private bool TryBuildSquadPlan(Pawn pawn)
        {
            squadTargetClaims.Clear();
            int squadSize = 1;
            List<Pawn> colony = pawn.Map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < colony.Count; i++)
            {
                Pawn mate = colony[i];
                if (mate == pawn || !mate.Drafted
                    || !mate.Position.InHorDistOf(pawn.Position, SquadRadius))
                {
                    continue;
                }
                CombatAutopilotData mateData = CombatAutopilotComponent.TryGetData(mate);
                CombatPolicy matePolicy = mateData?.AssignedPolicy;
                if (mateData == null || mateData.Paused || matePolicy == null || !matePolicy.FightAsSquad)
                {
                    continue;
                }
                squadSize++;
                Thing mateTarget = mate.mindState?.enemyTarget;
                CountClaim(mateTarget);
                Thing mateOrdered = mateData.CurrentOrderedTarget();
                if (mateOrdered != mateTarget)
                {
                    CountClaim(mateOrdered);
                }
            }
            if (squadSize < 2)
            {
                return false;
            }
            int threats = 0;
            foreach (IAttackTarget candidate in pawn.Map.attackTargetsCache.TargetsHostileToColony)
            {
                if (!candidate.ThreatDisabled(pawn) && AttackTargetFinder.IsAutoTargetable(candidate)
                    && IsFullAITarget(candidate.Thing))
                {
                    threats++;
                }
            }
            if (threats == 0)
            {
                return false;
            }
            spreadCap = Math.Max(1, (int)Math.Ceiling(squadSize / (double)threats));
            return true;
        }

        private static void CountClaim(Thing target)
        {
            if (target == null || target.Destroyed)
            {
                return;
            }
            squadTargetClaims.TryGetValue(target, out int claims);
            squadTargetClaims[target] = claims + 1;
        }

        /// <summary>The pursue-anywhere sweep targets pawns only, and only where the player could see them.</summary>
        private static bool IsFullAITarget(Thing thing)
        {
            if (!thing.Spawned || thing.MapHeld == null || thing.Fogged())
            {
                return false;
            }
            return thing is Pawn pawn && !pawn.Downed && !pawn.IsPsychologicallyInvisible();
        }

        /// <summary>Hunting never aggros passive wildlife or dormant threats (Search and Destroy's rule).</summary>
        protected override bool ExtraTargetValidator(Pawn pawn, Thing target)
        {
            // The pawn's own current target is grandfathered so spreading never causes
            // mid-fight target flip-flop; it steers new acquisitions only.
            if (spreadPass && target != spreadOwnTarget
                && squadTargetClaims.TryGetValue(target, out int claims) && claims >= spreadCap)
            {
                spreadRejected = true;
                return false;
            }
            if (!InCommandRange(pawn, target))
            {
                return false;
            }
            // Factionless wildlife only: idle mechs/war animals are combatants, manhunters never passive.
            if (target is Pawn targetPawn && targetPawn.Faction == null
                && WildManUtility.NonHumanlikeOrWildMan(targetPawn)
                && !targetPawn.InAggroMentalState && !PawnUtility.IsAttacking(targetPawn))
            {
                return false;
            }
            // Boom-weapon shooters skip targets that would catch a colony pawn in the
            // blast, so selection flows to a clean enemy instead of holding fire.
            Verb attackVerb = pawn.TryGetAttackVerb(target);
            if (attackVerb != null && attackVerb.verbProps.ai_AvoidFriendlyFireRadius > 0f
                && AlliedPawnNear(pawn.Map, target.Position, attackVerb.verbProps.ai_AvoidFriendlyFireRadius))
            {
                return false;
            }
            return base.ExtraTargetValidator(pawn, target);
        }

        protected override bool ShouldLoseTarget(Pawn pawn)
        {
            CombatAutopilotData data = CombatAutopilotComponent.TryGetData(pawn);
            CombatPolicy policy = data?.AssignedPolicy;
            if (data == null || !data.SeekAndDestroy)
            {
                // Hold mode re-acquires every poll; a sticky target buys nothing there.
                return true;
            }
            Thing enemy = pawn.mindState.enemyTarget;
            if (enemy == null || enemy.Destroyed)
            {
                return true;
            }
            if (enemy is Pawn enemyPawn && enemyPawn.DeadOrDowned)
            {
                return true;
            }
            if (enemy is IAttackTarget attackTarget && attackTarget.ThreatDisabled(pawn))
            {
                return true;
            }
            if (Find.TickManager.TicksGame - pawn.mindState.lastEngageTargetTick > TicksSinceEngageToLoseTarget)
            {
                return true;
            }
            if (!pawn.CanReach(enemy, PathEndMode.Touch, Danger.Deadly,
                canBashDoors: policy != null && policy.BashDoors))
            {
                return true;
            }
            if (policy != null && policy.PursueAnywhere)
            {
                return false;
            }
            return (float)(pawn.Position - enemy.Position).LengthHorizontalSquared
                > targetKeepRadius * targetKeepRadius;
        }

        protected override bool TryFindShootingPosition(Pawn pawn, out IntVec3 dest, Verb verbToUse = null)
        {
            Thing enemy = pawn.mindState.enemyTarget;
            Verb verb = verbToUse ?? pawn.TryGetAttackVerb(enemy);
            CombatAutopilotData data = CombatAutopilotComponent.TryGetData(pawn);
            if (enemy == null || verb == null || data == null)
            {
                dest = pawn.Position;
                return false;
            }
            return TryFindCastPosition(pawn, data, enemy, verb, preferCurrent: true,
                wounded: false, out dest);
        }

        /// <summary>Squad pawns try a spaced position first (one tile from allies), settling for crowded only when none exists.</summary>
        private static bool TryFindCastPosition(Pawn pawn, CombatAutopilotData data, Thing enemy,
            Verb verb, bool preferCurrent, bool wounded, out IntVec3 dest)
        {
            CombatPolicy policy = data.AssignedPolicy;
            bool wantCover = (wounded || (policy != null && policy.TakeCover)) && CoverUseful(enemy);
            if (policy != null && policy.FightAsSquad
                && TryFindCastPositionInner(pawn, enemy, verb, preferCurrent, wantCover, spaced: true, out dest))
            {
                return true;
            }
            return TryFindCastPositionInner(pawn, enemy, verb, preferCurrent, wantCover, spaced: false, out dest);
        }

        private static bool TryFindCastPositionInner(Pawn pawn, Thing enemy, Verb verb,
            bool preferCurrent, bool wantCover, bool spaced, out IntVec3 dest)
        {
            return CastPositionFinder.TryFindCastPosition(new CastPositionRequest
            {
                caster = pawn,
                target = enemy,
                verb = verb,
                maxRangeFromTarget = verb.EffectiveRange,
                wantCoverFromTarget = wantCover,
                preferredCastPosition = preferCurrent ? pawn.Position : (IntVec3?)null,
                validator = c => InCommandRange(pawn, c) && (!spaced || !CrowdsAlly(pawn, c)),
            }, out dest);
        }

        private static bool CrowdsAlly(Pawn pawn, IntVec3 cell)
        {
            List<Pawn> colony = pawn.Map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < colony.Count; i++)
            {
                Pawn ally = colony[i];
                if (ally != pawn && ally.Position.DistanceToSquared(cell) <= AllySpacingDistSq)
                {
                    return true;
                }
            }
            return false;
        }

        private static Job RepollWait(Pawn pawn, string report = null)
        {
            pawn.pather?.StopDead();
            Job job = JobMaker.MakeJob(JobDefOf.Wait_Combat, VigilantExpiry(pawn, RepollTicks),
                checkOverrideOnExpiry: true);
            if (report != null)
            {
                job.reportStringOverride = report;
            }
            return job;
        }

        /// <summary>The wait a pawn holds while auto-attack fires from the spot; the report distinguishes it from an idle wait.</summary>
        private static Job FiringWait(Pawn pawn)
        {
            pawn.pather?.StopDead();
            Job job = JobMaker.MakeJob(JobDefOf.Wait_Combat,
                VigilantExpiry(pawn, ExpiryInterval_ShooterSucceeded.RandomInRange / 3),
                checkOverrideOnExpiry: true);
            job.reportStringOverride = "RimWorldAccess.Autopilot.Report.HoldingFiring".Translate();
            return job;
        }

        private static string WoundedReport()
        {
            return "RimWorldAccess.Autopilot.Report.WoundedHolding".Translate();
        }

        /// <summary>
        /// The def report strings are the save-safe fallback; the override enriches them
        /// with the destination or the enemy's name (a vanilla Job facility, scribed).
        /// </summary>
        private static Job GotoJob(Pawn pawn, IntVec3 dest, bool seekingCover, Thing enemy, string engageReport = null)
        {
            Job job = JobMaker.MakeJob(
                seekingCover ? CombatAutopilotJobDefOf.RWA_GotoCover : CombatAutopilotJobDefOf.RWA_GotoEngage,
                dest);
            if (!seekingCover)
            {
                job.reportStringOverride = engageReport
                    ?? (string)"RimWorldAccess.Autopilot.Report.MovingToEngage".Translate(enemy.LabelShort);
            }
            else
            {
                Thing cover = BestCoverGiver(dest, enemy);
                job.reportStringOverride = cover != null
                    ? (string)"RimWorldAccess.Autopilot.Report.MovingToCoverBehind".Translate(cover.LabelShort, dest.x, dest.z)
                    : (string)"RimWorldAccess.Autopilot.Report.MovingToCover".Translate(dest.x, dest.z);
            }
            job.expiryInterval = VigilantExpiry(pawn, ExpiryInterval_ShooterSucceeded.RandomInRange / 3);
            job.checkOverrideOnExpire = true;
            return job;
        }

        /// <summary>The strongest cover thing protecting a shooter at this spot from the enemy.</summary>
        private static Thing BestCoverGiver(IntVec3 shooterLoc, Thing enemy)
        {
            Map map = enemy.MapHeld;
            if (map == null)
            {
                return null;
            }
            List<CoverInfo> covers = CoverUtility.CalculateCoverGiverSet(enemy, shooterLoc, map);
            Thing best = null;
            float bestChance = 0f;
            for (int i = 0; i < covers.Count; i++)
            {
                if (covers[i].Thing != null && covers[i].BlockChance > bestChance)
                {
                    bestChance = covers[i].BlockChance;
                    best = covers[i].Thing;
                }
            }
            return best;
        }

        /// <summary>Blast avoidance and injured fall-back outrank every combat decision.</summary>
        private Job TrySafetyJob(Pawn pawn, CombatAutopilotData data, CombatPolicy policy, bool wounded)
        {
            if (policy == null || PawnUtility.PlayerForcedJobNowOrSoon(pawn))
            {
                return null;
            }
            if (policy.AvoidBlasts)
            {
                Job flee = TryFleeExploderJob(pawn) ?? TrySidestepBlastJob(pawn);
                if (flee != null)
                {
                    return flee;
                }
            }
            if (wounded)
            {
                Job retreat = TryRetreatJob(pawn);
                if (retreat != null)
                {
                    return retreat;
                }
                // Clear of pressing enemies: self-buffs now, then the ladder continues
                // under wounded discipline.
                return TrySelfBuffJob(pawn, data);
            }
            return null;
        }

        /// <summary>
        /// Nearing collapse by the game's own downing rules (pain shock, moving floor);
        /// summary health is only the coarse backstop for spread-out wounds.
        /// </summary>
        private static bool BadlyHurt(Pawn pawn)
        {
            if (pawn.health.hediffSet.PainTotal
                >= pawn.GetStatValue(StatDefOf.PainShockThreshold) * PainShockFleeFraction)
            {
                return true;
            }
            if (pawn.health.capacities.GetLevel(PawnCapacityDefOf.Moving) < MovingFleeLevel)
            {
                return true;
            }
            return pawn.health.summaryHealth.SummaryHealthPercent < FallBackHealthPct;
        }

        /// <summary>Vanilla's exploder flee (JobGiver_FleePotentialExplosion), which drafted pawns never get.</summary>
        private static Job TryFleeExploderJob(Pawn pawn)
        {
            Thing exploder = pawn.mindState.knownExploder;
            if (exploder == null)
            {
                return null;
            }
            if (!exploder.Spawned)
            {
                pawn.mindState.knownExploder = null;
                return null;
            }
            if ((float)(pawn.Position - exploder.Position).LengthHorizontalSquared
                > ExplosionFleeDist * ExplosionFleeDist)
            {
                return null;
            }
            if (!TryFindTrapSafeFleeDest(exploder.Position, ExplosionFleeDist, pawn, out IntVec3 dest)
                || !InCommandRange(pawn, dest))
            {
                return null;
            }
            return RetreatJob(dest, "RimWorldAccess.Autopilot.Report.FleeingBlast".Translate(exploder.LabelShort));
        }

        /// <summary>
        /// Steps out of the blast circle a hostile is aiming up — to the NEAREST cell clear
        /// of every aimed circle: a flee sprint chained one dodge into the next warmup. A
        /// circle aimed at this pawn itself would follow the runner; cover, kiting and
        /// fall-back handle that case.
        /// </summary>
        private static Job TrySidestepBlastJob(Pawn pawn)
        {
            tmpBlastCircles.Clear();
            Pawn danger = null;
            BlastCircle dangerCircle = default(BlastCircle);
            foreach (IAttackTarget candidate in pawn.Map.attackTargetsCache.GetPotentialTargetsFor(pawn))
            {
                if (candidate.ThreatDisabled(pawn) || !(candidate.Thing is Pawn hostile))
                {
                    continue;
                }
                Stance_Warmup warmup = hostile.stances?.curStance as Stance_Warmup;
                if (warmup == null || warmup.verb == null
                    || !warmup.focusTarg.IsValid || warmup.focusTarg.Thing == pawn)
                {
                    continue;
                }
                float radius = BlastRadius(pawn, warmup.verb);
                if (radius <= 0f)
                {
                    continue;
                }
                var circle = new BlastCircle { Center = warmup.focusTarg.Cell, Radius = radius + BlastMargin };
                tmpBlastCircles.Add(circle);
                if (danger == null && pawn.Position.InHorDistOf(circle.Center, circle.Radius))
                {
                    danger = hostile;
                    dangerCircle = circle;
                }
            }
            if (danger == null)
            {
                return null;
            }
            string report = "RimWorldAccess.Autopilot.Report.FleeingBlast".Translate(danger.LabelShort);
            IntVec3 dest = FindSidestepDest(pawn);
            if (dest.IsValid)
            {
                return RetreatJob(dest, report);
            }
            // Boxed in: fall back to the flee finder out of the triggering circle.
            if (TryFindTrapSafeFleeDest(dangerCircle.Center, dangerCircle.Radius + 2f, pawn, out dest)
                && InCommandRange(pawn, dest))
            {
                return RetreatJob(dest, report);
            }
            return null;
        }

        /// <summary>Nearest standable cell clear of every collected blast circle; radial order = shortest step first.</summary>
        private static IntVec3 FindSidestepDest(Pawn pawn)
        {
            Map map = pawn.Map;
            int cells = GenRadial.NumCellsInRadius(SidestepSearchRadius);
            for (int i = 1; i < cells; i++)
            {
                IntVec3 cell = pawn.Position + GenRadial.RadialPattern[i];
                if (!cell.InBounds(map) || InAnyBlastCircle(cell) || !cell.Standable(map)
                    || !TrapSafe(pawn, cell) || !InCommandRange(pawn, cell)
                    || !pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                {
                    continue;
                }
                return cell;
            }
            return IntVec3.Invalid;
        }

        private static bool InAnyBlastCircle(IntVec3 cell)
        {
            for (int i = 0; i < tmpBlastCircles.Count; i++)
            {
                if (cell.InHorDistOf(tmpBlastCircles[i].Center, tmpBlastCircles[i].Radius))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The circle the game draws for this verb: an explosive projectile's blast
        /// radius, or an AOE ability's effect radius. Damage that neither harms this
        /// pawn's health nor stuns it (smoke always; EMP for flesh) is no danger, per
        /// vanilla's own stun predicate, so mechs flee EMP and humans ignore it.
        /// </summary>
        private static float BlastRadius(Pawn pawn, Verb verb)
        {
            if (verb is Verb_CastAbility cast)
            {
                AbilityDef abilityDef = cast.ability?.def;
                return abilityDef != null && abilityDef.HasAreaOfEffect && verb.verbProps.violent
                    ? abilityDef.EffectRadius
                    : 0f;
            }
            ThingDef proj = (verb as Verb_LaunchProjectile)?.Projectile ?? verb.verbProps.defaultProjectile;
            if (proj?.projectile == null || proj.projectile.explosionRadius <= 0f)
            {
                return 0f;
            }
            DamageDef damage = proj.projectile.damageDef;
            bool dangerous = damage == null || damage.harmsHealth
                || (pawn.stances?.stunner != null && canBeStunnedByDamage(pawn.stances.stunner, damage));
            return dangerous ? proj.projectile.explosionRadius : 0f;
        }

        /// <summary>
        /// The colonist flee response's own destination pick (CellFinderLoose.GetFleeDest),
        /// from PRESSING enemies only — aiming at this pawn, chasing it, or melee-armed
        /// nearby. A distant firefight aimed elsewhere never drives a wounded pawn off.
        /// </summary>
        private static Job TryRetreatJob(Pawn pawn)
        {
            List<Thing> threats = null;
            foreach (IAttackTarget candidate in pawn.Map.attackTargetsCache.GetPotentialTargetsFor(pawn))
            {
                if (candidate.ThreatDisabled(pawn))
                {
                    continue;
                }
                Thing thing = candidate.Thing;
                if (thing.Position.InHorDistOf(pawn.Position, RetreatThreatRadius)
                    && PressingThreat(candidate, pawn))
                {
                    if (threats == null)
                    {
                        threats = new List<Thing>();
                    }
                    threats.Add(thing);
                }
            }
            if (threats == null)
            {
                return null;
            }
            IntVec3 dest = CellFinderLoose.GetFleeDest(pawn, threats);
            // GetFleeDest checks terrain but never known traps; re-roll a trapped pick.
            if (dest != pawn.Position && !TrapSafe(pawn, dest)
                && !TryFindTrapSafeFleeDest(NearestThreatPosition(pawn, threats), RetreatThreatRadius, pawn, out dest))
            {
                return null;
            }
            if (dest == pawn.Position || !InCommandRange(pawn, dest))
            {
                return null;
            }
            return RetreatJob(dest, "RimWorldAccess.Autopilot.Report.FallingBack".Translate(dest.x, dest.z));
        }

        private static bool PressingThreat(IAttackTarget candidate, Pawn pawn)
        {
            if (candidate.TargetCurrentlyAimingAt.Thing == pawn)
            {
                return true;
            }
            return candidate.Thing is Pawn hostile
                && (IsTargetingPawn(hostile, pawn)
                    || ((hostile.CurrentEffectiveVerb?.verbProps.IsMeleeAttack ?? false)
                        && CanWalkTo(hostile, pawn)));
        }

        private static bool CanWalkTo(Pawn hostile, Pawn pawn)
        {
            return hostile.CanReach(pawn, PathEndMode.Touch, Danger.Deadly);
        }

        private static IntVec3 NearestThreatPosition(Pawn pawn, List<Thing> threats)
        {
            IntVec3 best = threats[0].Position;
            float bestDistSq = float.MaxValue;
            for (int i = 0; i < threats.Count; i++)
            {
                float distSq = threats[i].Position.DistanceToSquared(pawn.Position);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = threats[i].Position;
                }
            }
            return best;
        }

        /// <summary>
        /// Melee foes only: backing away from a shooter would just donate free shots.
        /// Triggers at the speed-scaled safe distance and flees past it, so the pawn
        /// finishes a full aim before the foe can close back into melee range.
        /// </summary>
        private static Job TryKiteJob(Pawn pawn)
        {
            Pawn threat = ClosestKiteThreat(pawn, KiteMaxSafeDist);
            if (threat == null)
            {
                return null;
            }
            Verb verb = pawn.TryGetAttackVerb(threat);
            if (verb == null || verb.verbProps.IsMeleeAttack
                || verb.EffectiveRange < KiteMinSafeDist + KiteHysteresis)
            {
                return null;
            }
            float safe = KiteSafeDist(pawn, threat, verb);
            float distSq = (pawn.Position - threat.Position).LengthHorizontalSquared;
            if (distSq >= safe * safe)
            {
                return null;
            }
            // Mid-aim the shot lands before the foe does; abandoning it re-pays the whole
            // warmup while fleeing. Break off only when the foe is nearly in melee range.
            if (pawn.stances?.curStance is Stance_Warmup
                && distSq > KiteAbortAimDist * KiteAbortAimDist)
            {
                return null;
            }
            if (!TryFindTrapSafeFleeDest(threat.Position, safe + KiteHysteresis, pawn, out IntVec3 dest)
                || !InCommandRange(pawn, dest))
            {
                return null;
            }
            // A player move order's stance cancel: job overrides never cancel stances, so
            // without this a post-shot cooldown pins the pawn while the foe closes.
            pawn.stances?.CancelBusyStanceHard();
            return RetreatJob(dest, "RimWorldAccess.Autopilot.Report.BackingAway".Translate(threat.LabelShort));
        }

        /// <summary>
        /// Distance at which a fresh aim completes before the foe reaches melee range: foe
        /// speed times aim time plus reaction latency, plus a melee buffer. Clamped so the
        /// pawn can still shoot from where it stops.
        /// </summary>
        private static float KiteSafeDist(Pawn pawn, Pawn threat, Verb verb)
        {
            float aimSeconds = verb.verbProps.warmupTime
                * pawn.GetStatValue(StatDefOf.AimingDelayFactor);
            float reactSeconds = KiteVigilanceTicks / 60f;
            float dist = threat.GetStatValue(StatDefOf.MoveSpeed) * (aimSeconds + reactSeconds)
                + KiteMeleeBuffer;
            float max = Math.Min(KiteMaxSafeDist, verb.EffectiveRange - KiteHysteresis);
            return Math.Max(KiteMinSafeDist, Math.Min(dist, max));
        }

        /// <summary>Nearest melee foe that could turn on this pawn: targeting it or uncommitted, and able to walk to it now (no bashing: a broken door flips this next poll).</summary>
        private static Pawn ClosestKiteThreat(Pawn pawn, float maxDist)
        {
            return ClosestMeleeThreat(pawn, maxDist,
                hostile => (IsTargetingPawn(hostile, pawn) || hostile.mindState?.enemyTarget == null)
                    && CanWalkTo(hostile, pawn));
        }

        /// <summary>
        /// Short re-poll while a kiting pawn has a melee foe inside awareness range: the
        /// vanilla shooter interval would let a fast animal cross the whole safe distance
        /// between checks.
        /// </summary>
        private static int VigilantExpiry(Pawn pawn, int normalTicks)
        {
            if (normalTicks <= KiteVigilanceTicks)
            {
                return normalTicks;
            }
            CombatPolicy policy = CombatAutopilotComponent.TryGetData(pawn)?.AssignedPolicy;
            if (policy == null || !policy.KeepDistance
                || ClosestKiteThreat(pawn, KiteAwarenessDist) == null)
            {
                return normalTicks;
            }
            return KiteVigilanceTicks;
        }

        /// <summary>The nearest live melee-armed hostile within the radius, optionally filtered further.</summary>
        private static Pawn ClosestMeleeThreat(Pawn pawn, float maxDist, Func<Pawn, bool> extra = null)
        {
            float bestDistSq = maxDist * maxDist;
            Pawn best = null;
            foreach (IAttackTarget candidate in pawn.Map.attackTargetsCache.GetPotentialTargetsFor(pawn))
            {
                if (candidate.ThreatDisabled(pawn))
                {
                    continue;
                }
                Pawn hostile = candidate.Thing as Pawn;
                if (hostile == null || DownedOrHidden(hostile))
                {
                    continue;
                }
                Verb hostileVerb = hostile.CurrentEffectiveVerb;
                if (hostileVerb == null || !hostileVerb.verbProps.IsMeleeAttack)
                {
                    continue;
                }
                float distSq = hostile.Position.DistanceToSquared(pawn.Position);
                if (distSq < bestDistSq && (extra == null || extra(hostile)))
                {
                    bestDistSq = distSq;
                    best = hostile;
                }
            }
            return best;
        }

        /// <summary>The vanilla cast JobDefs have no reportString ("Doing something."); name the ability.</summary>
        private static Job WithCastReport(Job job, Ability ability, Thing target)
        {
            job.reportStringOverride = target == null
                ? (string)"RimWorldAccess.Autopilot.Report.Casting".Translate(ability.def.label)
                : (string)"RimWorldAccess.Autopilot.Report.CastingOn".Translate(ability.def.label, target.LabelShort);
            return job;
        }

        /// <summary>Sprinted retreat move; the short expiry re-checks the danger mid-run.</summary>
        private static Job RetreatJob(IntVec3 dest, string report)
        {
            Job job = JobMaker.MakeJob(CombatAutopilotJobDefOf.RWA_Retreat, dest);
            job.reportStringOverride = report;
            job.locomotionUrgency = LocomotionUrgency.Sprint;
            job.expiryInterval = RepollTicks;
            job.checkOverrideOnExpire = true;
            job.collideWithPawns = true;
            return job;
        }
    }

    /// <summary>
    /// Vanilla skips drafted pawns when flagging a dangerous exploder (drafted means the
    /// player's problem). Pawns whose policy opts into blast avoidance get the flag and
    /// the immediate job re-check; vanilla's relevance rules (intelligence, faction,
    /// damage type, room and line of sight) have all passed before this runs.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_MindState), "Notify_DangerousExploderAboutToExplode")]
    public static class DraftedExploderNotifyPatch
    {
        public static void Postfix(Pawn_MindState __instance, Thing exploder)
        {
            Pawn pawn = __instance.pawn;
            if (pawn == null || !pawn.Spawned || !pawn.Drafted)
            {
                return;
            }
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings != null && !settings.EnableCombatAutopilot)
            {
                return;
            }
            CombatAutopilotData data = CombatAutopilotComponent.TryGetData(pawn);
            CombatPolicy policy = data?.AssignedPolicy;
            if (policy == null || !policy.AvoidBlasts || data.Paused)
            {
                return;
            }
            __instance.knownExploder = exploder;
            pawn.jobs?.CheckForJobOverride();
        }
    }
}
