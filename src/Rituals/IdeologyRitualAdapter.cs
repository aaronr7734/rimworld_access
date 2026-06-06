using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public class IdeologyRitualAdapter : LordJobDialogAdapterBase
    {
        protected static readonly FieldInfo AssignmentsField =
            AccessTools.Field(typeof(Dialog_BeginRitual), "assignments");
        protected static readonly FieldInfo RitualField =
            AccessTools.Field(typeof(Dialog_BeginRitual), "ritual");
        protected static readonly FieldInfo TargetField =
            AccessTools.Field(typeof(Dialog_BeginRitual), "target");
        protected static readonly FieldInfo OutcomeField =
            AccessTools.Field(typeof(Dialog_BeginRitual), "outcome");
        protected static readonly PropertyInfo SleepingWarningProp =
            AccessTools.Property(typeof(Dialog_BeginRitual), "SleepingWarning");

        protected readonly Dialog_BeginRitual ideologyDialog;
        protected readonly RitualRoleAssignments assignments;
        protected readonly Precept_Ritual ritual;
        protected readonly TargetInfo target;
        protected readonly RitualOutcomeEffectDef outcome;

        public IdeologyRitualAdapter(Dialog_BeginRitual dialog) : base(dialog)
        {
            ideologyDialog = dialog;
            assignments = AssignmentsField?.GetValue(dialog) as RitualRoleAssignments;
            ritual = RitualField?.GetValue(dialog) as Precept_Ritual;
            target = TargetField != null ? (TargetInfo)TargetField.GetValue(dialog) : TargetInfo.Invalid;
            outcome = OutcomeField?.GetValue(dialog) as RitualOutcomeEffectDef;
        }

        public override TargetInfo Target => target;

        public override string LocalizedDialogName
        {
            get
            {
                if (ritual?.behavior?.def?.label != null)
                    return ritual.behavior.def.label.CapitalizeFirst();
                return ritual?.Label?.CapitalizeFirst()
                    ?? (string)"RimWorldAccess.Rituals.Ritual.DialogNameFallback".Translate();
            }
        }

        public override string ClosingAnnouncement => "RimWorldAccess.Rituals.Ritual.DialogClosed".Translate();

        protected override void AppendDialogSpecificWarnings(List<string> warnings)
        {
            try
            {
                string sleeping = SleepingWarningProp?.GetValue(ideologyDialog)?.ToString();
                if (!string.IsNullOrEmpty(sleeping)) warnings.Add(sleeping);
            }
            catch { /* ignore */ }

            try
            {
                if (assignments != null && assignments.Participants.Any(p => p.Drafted))
                {
                    warnings.Add("RimWorldAccess.Rituals.Ritual.ParticipantsDrafted".Translate());
                }
            }
            catch { /* ignore */ }
        }

        public override IReadOnlyList<LordJobRoleView> BuildRoleList()
        {
            var views = new List<LordJobRoleView>();
            if (assignments == null) return views;

            foreach (var roleGroup in assignments.RoleGroups())
            {
                var roleList = roleGroup.ToList();
                var firstRole = roleList[0];

                int assignedCount = 0;
                foreach (var role in roleList)
                    assignedCount += assignments.AssignedPawns(role).Count();

                int maxCount = 0;
                bool hasUnlimited = false;
                int minCount = 0;
                foreach (var role in roleList)
                {
                    if (role.maxCount <= 0) { hasUnlimited = true; }
                    else { maxCount += role.maxCount; }
                    minCount += role.MinCount;
                }
                if (hasUnlimited) maxCount = -1;

                bool isRequired = roleList.Any(r => r.required);
                bool isLocked = AllAssignedAreForcedInstance(roleList);

                string categoryLabel = firstRole.CategoryLabelCap.ToString();
                string label = !string.IsNullOrEmpty(categoryLabel) ? categoryLabel : firstRole.LabelCap.ToString();

                string extraInfo = null;
                try
                {
                    var assignedPawns = assignments.AssignedPawns(firstRole);
                    extraInfo = firstRole.ExtraInfoForDialog(assignedPawns);
                }
                catch { /* role-specific; tolerate failure */ }

                views.Add(new LordJobRoleView
                {
                    Type = LordJobRoleView.Kind.Role,
                    Label = label,
                    AssignedCount = assignedCount,
                    MaxCount = maxCount,
                    MinCount = minCount,
                    IsRequired = isRequired,
                    IsLocked = isLocked,
                    ExtraInfoLine = string.IsNullOrEmpty(extraInfo) ? null : extraInfo,
                    AdapterTag = roleList,
                });
            }

            if (assignments.SpectatorsAllowed)
            {
                int spectatorCount = assignments.SpectatorsForReading.Count;
                string spectatorLabel = ritual?.behavior?.def?.spectatorsLabel;
                if (string.IsNullOrEmpty(spectatorLabel))
                    spectatorLabel = "Spectators";

                views.Add(new LordJobRoleView
                {
                    Type = LordJobRoleView.Kind.Spectators,
                    Label = spectatorLabel,
                    AssignedCount = spectatorCount,
                    MaxCount = -1,
                    MinCount = 0,
                    IsRequired = false,
                    IsLocked = false,
                });
            }

            return views;
        }

        public override IReadOnlyList<LordJobPawnView> BuildPawnList(LordJobRoleView role)
        {
            var views = new List<LordJobPawnView>();
            if (assignments == null || role == null) return views;

            var allPawns = new HashSet<Pawn>();
            var assignedPawns = new HashSet<Pawn>();
            var forcedPawns = new HashSet<Pawn>();

            if (role.Type == LordJobRoleView.Kind.Role && role.AdapterTag is List<RitualRole> roles && roles.Count > 0)
            {
                foreach (var ritualRole in roles)
                {
                    foreach (var pawn in assignments.AssignedPawns(ritualRole))
                    {
                        allPawns.Add(pawn);
                        assignedPawns.Add(pawn);
                        if (assignments.Forced(pawn)) forcedPawns.Add(pawn);
                    }
                }

                var firstRole = roles[0];
                foreach (var pawn in assignments.CandidatesForRole(firstRole, target, includeAssigned: false))
                {
                    allPawns.Add(pawn);
                }
            }
            else if (role.Type == LordJobRoleView.Kind.Spectators)
            {
                foreach (var pawn in assignments.SpectatorsForReading)
                {
                    allPawns.Add(pawn);
                    assignedPawns.Add(pawn);
                }
                foreach (var pawn in assignments.AllCandidatePawns)
                {
                    if (allPawns.Contains(pawn)) continue;
                    if (assignments.RoleForPawn(pawn) != null) continue;
                    if (assignments.CanEverSpectate(pawn)) allPawns.Add(pawn);
                }
            }

            var allRolesForSuitability = assignments.AllRolesForReading;

            foreach (var pawn in allPawns
                .OrderByDescending(p => assignedPawns.Contains(p))
                .ThenBy(p => p.LabelShort))
            {
                string disabledReason = null;
                if (role.Type == LordJobRoleView.Kind.Role && role.AdapterTag is List<RitualRole> rs && rs.Count > 0)
                {
                    disabledReason = assignments.PawnNotAssignableReason(pawn, rs[0]);
                }
                else if (role.Type == LordJobRoleView.Kind.Spectators)
                {
                    disabledReason = assignments.PawnNotAssignableReason(pawn, null);
                }

                string suitability = role.Type == LordJobRoleView.Kind.Role
                    ? FormatPawnSuitability(pawn, allRolesForSuitability)
                    : null;

                views.Add(new LordJobPawnView
                {
                    Pawn = pawn,
                    IsAssigned = assignedPawns.Contains(pawn),
                    IsForced = forcedPawns.Contains(pawn),
                    SuitabilityLine = suitability,
                    DisabledReason = disabledReason,
                });
            }

            return views;
        }

        public override AssignmentResult ToggleAssignment(LordJobRoleView role, LordJobPawnView pawn, out string failureReason)
        {
            failureReason = null;
            if (assignments == null || role == null || pawn?.Pawn == null) return AssignmentResult.Failed;

            if (pawn.IsForced) return AssignmentResult.BlockedForced;
            if (pawn.DisabledReason != null && !pawn.IsAssigned)
            {
                failureReason = pawn.DisabledReason;
                return AssignmentResult.BlockedDisabled;
            }

            try
            {
                if (pawn.IsAssigned)
                {
                    if (role.Type == LordJobRoleView.Kind.Role)
                    {
                        assignments.TryUnassignAnyRole(pawn.Pawn);
                    }
                    else if (role.Type == LordJobRoleView.Kind.Spectators)
                    {
                        assignments.RemoveParticipant(pawn.Pawn);
                    }
                    return AssignmentResult.Unassigned;
                }
                else
                {
                    if (role.Type == LordJobRoleView.Kind.Role && role.AdapterTag is List<RitualRole> rs && rs.Count > 0)
                    {
                        if (assignments.TryAssign(pawn.Pawn, rs[0], out _))
                            return AssignmentResult.Assigned;
                        return AssignmentResult.Failed;
                    }
                    if (role.Type == LordJobRoleView.Kind.Spectators)
                    {
                        if (assignments.TryAssignSpectate(pawn.Pawn))
                            return AssignmentResult.Assigned;
                        return AssignmentResult.Failed;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[IdeologyRitualAdapter] Toggle failed: {ex.Message}");
                return AssignmentResult.Failed;
            }
            return AssignmentResult.Failed;
        }

        public override IReadOnlyList<LordJobQualityRow> BuildExtraQualityRows()
        {
            var rows = new List<LordJobQualityRow>();
            if (ritual?.ideo == null || ritual.ideo.Fluid != true) return rows;

            try
            {
                var outcomeEffect = ritual.outcomeEffect;
                if (outcomeEffect?.def?.outcomeChances == null || outcomeEffect.def.outcomeChances.Count == 0)
                    return rows;

                var devPointsCurve = IdeoDevelopmentUtility.GetDevelopmentPointsOverOutcomeIndexCurveForRitual(ritual.ideo, ritual);
                if (devPointsCurve == null) return rows;

                rows.Add(new LordJobQualityRow
                {
                    Label = "Development Points",
                    Change = "(Fluid ideology)",
                    IsPresent = true,
                    IsPositive = true,
                    Tooltip = "Points awarded to your fluid ideology based on ritual outcome.",
                });

                var outcomeChances = outcomeEffect.def.outcomeChances;
                for (int i = 0; i < outcomeChances.Count; i++)
                {
                    var oc = outcomeChances[i];
                    float points = devPointsCurve.Evaluate(i);
                    rows.Add(new LordJobQualityRow
                    {
                        Label = $"  {oc.label}",
                        Change = points.ToStringWithSign(),
                        IsPresent = true,
                        IsPositive = points >= 0,
                    });
                }
            }
            catch { /* dev-points are optional info */ }
            return rows;
        }

        public override void Notify_AssignmentsChanged()
        {
            if (assignments == null) return;
            try
            {
                var notifyMethod = AccessTools.Method(typeof(RitualRoleAssignments), "Notify_AssignmentsChanged");
                notifyMethod?.Invoke(assignments, null);
            }
            catch { /* swallow */ }

            // Mirror Dialog_BeginRitual.PostOpen lines 529-532: per-comp notification so quality
            // factors recompute when assignments change. The base RitualRoleAssignments call alone
            // misses outcome-comp-derived factors.
            try
            {
                if (outcome != null && ritual?.outcomeEffect != null)
                {
                    foreach (var comp in outcome.comps)
                    {
                        comp.Notify_AssignmentsChanged(assignments, ritual.outcomeEffect.DataForComp(comp));
                    }
                }
            }
            catch { /* tolerate */ }
        }

        public override bool TryStart(out IReadOnlyList<string> blockingReasons)
        {
            var reasons = new List<string>();
            try
            {
                var enumerable = BlockingIssuesMethod?.Invoke(dialog, null) as System.Collections.IEnumerable;
                if (enumerable != null)
                {
                    foreach (var item in enumerable)
                    {
                        string s = item?.ToString();
                        if (!string.IsNullOrEmpty(s)) reasons.Add(s);
                    }
                }
            }
            catch { /* fall through */ }
            blockingReasons = reasons;
            return reasons.Count == 0;
        }

        private bool AllAssignedAreForcedInstance(List<RitualRole> roles)
        {
            var assigned = new List<Pawn>();
            foreach (var r in roles) assigned.AddRange(assignments.AssignedPawns(r));
            if (assigned.Count == 0) return false;
            return assigned.All(p => assignments.Forced(p));
        }

        protected static string FormatPawnSuitability(Pawn pawn, List<RitualRole> allRoles)
        {
            var stats = new List<string>();
            var processedStats = new HashSet<StatDef>();
            var processedSkills = new HashSet<SkillDef>();

            foreach (var role in allRoles)
            {
                if (role is RitualRoleColonist colonistRole)
                {
                    if (colonistRole.usedStat != null && processedStats.Add(colonistRole.usedStat))
                    {
                        if (colonistRole.usedStat.Worker.IsDisabledFor(pawn))
                        {
                            stats.Add($"{colonistRole.usedStat.LabelCap}: Disabled");
                        }
                        else
                        {
                            string value = colonistRole.usedStat.Worker.ValueToStringFor(pawn);
                            stats.Add($"{colonistRole.usedStat.LabelCap}: {value}");
                        }
                    }

                    if (colonistRole.usedSkill != null && processedSkills.Add(colonistRole.usedSkill) && pawn.skills != null)
                    {
                        var skill = pawn.skills.GetSkill(colonistRole.usedSkill);
                        if (skill != null)
                        {
                            stats.Add(skill.TotallyDisabled
                                ? $"{colonistRole.usedSkill.LabelCap}: Disabled"
                                : $"{colonistRole.usedSkill.LabelCap}: {skill.Level}");
                        }
                    }
                }
            }

            return stats.Count > 0 ? string.Join(". ", stats) : null;
        }
    }
}
