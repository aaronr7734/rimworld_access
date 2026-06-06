using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimWorldAccess
{
    public class PsychicRitualAdapter : LordJobDialogAdapterBase
    {
        protected static readonly FieldInfo AssignmentsField =
            AccessTools.Field(typeof(Dialog_BeginPsychicRitual), "assignments");
        protected static readonly FieldInfo PsychicRitualDefField =
            AccessTools.Field(typeof(Dialog_BeginPsychicRitual), "psychicRitualDef");
        protected static readonly FieldInfo MapField =
            AccessTools.Field(typeof(Dialog_BeginPsychicRitual), "map");

        protected readonly Dialog_BeginPsychicRitual psychicDialog;
        protected readonly PsychicRitualRoleAssignments assignments;
        protected readonly PsychicRitualDef psychicRitualDef;
        protected readonly Map map;

        public PsychicRitualAdapter(Dialog_BeginPsychicRitual dialog) : base(dialog)
        {
            psychicDialog = dialog;
            assignments = AssignmentsField?.GetValue(dialog) as PsychicRitualRoleAssignments;
            psychicRitualDef = PsychicRitualDefField?.GetValue(dialog) as PsychicRitualDef;
            map = MapField?.GetValue(dialog) as Map;
        }

        public override TargetInfo Target => assignments?.Target ?? TargetInfo.Invalid;

        public override string LocalizedDialogName =>
            psychicRitualDef?.LabelCap.Resolve() ?? (string)"RimWorldAccess.Rituals.Psychic.FallbackName".Translate();

        public override string ClosingAnnouncement => "RimWorldAccess.Rituals.Psychic.DialogClosed".Translate();

        public override string OutcomeDescriptionText
        {
            get
            {
                if (psychicRitualDef == null || assignments == null) return null;
                try
                {
                    GetQualityFactors(out var range);
                    string qualityNumber = System.Math.Abs(range.min - range.max) < 0.01f
                        ? range.min.ToStringPercent("F0")
                        : $"{range.min.ToStringPercent("F0")}-{range.max.ToStringPercent("F0")}";
                    string raw = psychicRitualDef.OutcomeDescription(range, qualityNumber, assignments).Resolve();
                    return string.IsNullOrEmpty(raw) ? null : SanitizeText(raw);
                }
                catch { return null; }
            }
        }

        protected override void AppendDialogSpecificWarnings(List<string> warnings)
        {
            if (assignments == null || psychicRitualDef == null) return;

            try
            {
                // Sleeping pawns assigned to roles that disallow Sleeping.
                var sleeping = SleepingAssignedPawns();
                if (sleeping.Count > 0)
                {
                    string names = sleeping.Select(p => p.LabelShortCap).ToCommaList(useAnd: true);
                    string key = sleeping.Count > 1 ? "PsychicRitualWakingPawnsWarning" : "PsychicRitualWakingPawnWarning";
                    warnings.Add(key.Translate(names).Resolve());
                }

                // Drafted pawns assigned to roles that disallow Drafted.
                var drafted = DraftedAssignedPawns();
                if (drafted.Count > 0)
                {
                    string names = drafted.Select(p => p.LabelShortCap).ToCommaList(useAnd: true);
                    string key = drafted.Count > 1 ? "PsychicRitualUndraftPawnsWarning" : "PsychicRitualUndraftPawnWarning";
                    warnings.Add(key.Translate(names).Resolve());
                }
            }
            catch { /* defensive */ }

            try
            {
                foreach (var w in psychicRitualDef.OutcomeWarnings(assignments))
                {
                    string s = w.Resolve();
                    if (!string.IsNullOrEmpty(s)) warnings.Add(s);
                }
            }
            catch { /* defensive */ }
        }

        private List<Pawn> SleepingAssignedPawns()
        {
            var result = new List<Pawn>();
            foreach (var kvp in assignments.RoleAssignments)
            {
                var roleDef = kvp.Key;
                if (roleDef.ConditionAllowed(PsychicRitualRoleDef.Condition.Sleeping)) continue;
                foreach (var p in kvp.Value)
                {
                    if (!p.Awake() && p.health.capacities.CanBeAwake) result.Add(p);
                }
            }
            return result;
        }

        private List<Pawn> DraftedAssignedPawns()
        {
            var result = new List<Pawn>();
            foreach (var kvp in assignments.RoleAssignments)
            {
                var roleDef = kvp.Key;
                if (roleDef.ConditionAllowed(PsychicRitualRoleDef.Condition.Drafted)) continue;
                foreach (var p in kvp.Value)
                {
                    if (p.Drafted) result.Add(p);
                }
            }
            return result;
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
                int maxCount = 0;
                int minCount = 0;
                foreach (var role in roleList)
                {
                    assignedCount += assignments.RoleAssignedCount(role);
                    maxCount += role.MaxCount;
                    minCount += role.MinCount;
                }

                bool isRequired = minCount > 0;
                bool isLocked = AllAssignedAreForced(roleList);

                string categoryLabel = firstRole.CategoryLabelCap.ToString();
                string label = !string.IsNullOrEmpty(categoryLabel) ? categoryLabel : firstRole.LabelCap.ToString();

                string extraInfo = BuildDisallowedConditionsLine(firstRole);

                views.Add(new LordJobRoleView
                {
                    Type = LordJobRoleView.Kind.Role,
                    Label = label,
                    AssignedCount = assignedCount,
                    MaxCount = maxCount,
                    MinCount = minCount,
                    IsRequired = isRequired,
                    IsLocked = isLocked,
                    ExtraInfoLine = extraInfo,
                    Tooltip = SanitizeText(firstRole.description),
                    AdapterTag = roleList,
                });
            }

            return views;
        }

        private bool AllAssignedAreForced(List<PsychicRitualRoleDef> roles)
        {
            var assigned = new List<Pawn>();
            foreach (var r in roles) assigned.AddRange(assignments.AssignedPawns(r));
            if (assigned.Count == 0) return false;
            return assigned.All(p => assignments.ForcedRole(p) != null);
        }

        private static string BuildDisallowedConditionsLine(PsychicRitualRoleDef role)
        {
            // Surface the Condition flags this role disallows so blind users hear the
            // assignability rules vanilla shows only via on-hover tooltips.
            var allowed = role.AllowedConditions;
            var disallowed = new List<string>();
            void Check(PsychicRitualRoleDef.Condition c, string conditionKey)
            {
                if ((allowed & c) == 0)
                    disallowed.Add(("RimWorldAccess.Rituals.Psychic.Condition." + conditionKey).Translate());
            }
            Check(PsychicRitualRoleDef.Condition.Sleeping, "Sleeping");
            Check(PsychicRitualRoleDef.Condition.Drafted, "Drafted");
            Check(PsychicRitualRoleDef.Condition.Bleeding, "Bleeding");
            Check(PsychicRitualRoleDef.Condition.Burning, "Burning");
            Check(PsychicRitualRoleDef.Condition.MentalState, "MentalState");
            Check(PsychicRitualRoleDef.Condition.Downed, "Downed");
            Check(PsychicRitualRoleDef.Condition.Prisoner, "Prisoner");
            Check(PsychicRitualRoleDef.Condition.Slave, "Slave");
            Check(PsychicRitualRoleDef.Condition.Baby, "Baby");
            Check(PsychicRitualRoleDef.Condition.Child, "Child");

            return disallowed.Count == 0
                ? null
                : (string)"RimWorldAccess.Rituals.Psychic.Disallows".Translate(disallowed.ToCommaList(useAnd: false));
        }

        public override IReadOnlyList<LordJobPawnView> BuildPawnList(LordJobRoleView role)
        {
            var views = new List<LordJobPawnView>();
            if (assignments == null || role == null || !(role.AdapterTag is List<PsychicRitualRoleDef> roleDefs) || roleDefs.Count == 0)
                return views;

            var allPawns = new HashSet<Pawn>();
            var assignedPawns = new HashSet<Pawn>();
            var forcedPawns = new HashSet<Pawn>();

            foreach (var rd in roleDefs)
            {
                foreach (var p in assignments.AssignedPawns(rd))
                {
                    allPawns.Add(p);
                    assignedPawns.Add(p);
                    if (assignments.ForcedRole(p) != null) forcedPawns.Add(p);
                }
            }

            // Candidate pool: all colonists/prisoners on the map who could potentially do any role.
            var candidatePool = map?.mapPawns?.FreeColonistsAndPrisonersSpawned;
            if (candidatePool != null)
            {
                foreach (var p in candidatePool)
                {
                    if (p == null || p.IsSubhuman) continue;
                    if (!allPawns.Add(p)) continue;
                }
            }

            var firstRole = roleDefs[0];
            foreach (var pawn in allPawns
                .OrderByDescending(p => assignedPawns.Contains(p))
                .ThenBy(p => p.LabelShort))
            {
                string disabledReason = assignments.PawnNotAssignableReason(pawn, firstRole);
                string suitability = FormatPsychicSuitability(pawn);

                string tooltip = null;
                try
                {
                    var extras = psychicRitualDef?.GetPawnTooltipExtras(pawn)?.Where(s => !string.IsNullOrEmpty(s));
                    if (extras != null && extras.Any())
                    {
                        tooltip = string.Join(". ", extras);
                    }
                }
                catch { /* tolerate */ }

                views.Add(new LordJobPawnView
                {
                    Pawn = pawn,
                    IsAssigned = assignedPawns.Contains(pawn),
                    IsForced = forcedPawns.Contains(pawn),
                    SuitabilityLine = suitability,
                    DisabledReason = disabledReason,
                    Tooltip = tooltip,
                });
            }

            return views;
        }

        private static string FormatPsychicSuitability(Pawn pawn)
        {
            try
            {
                float sens = pawn.GetStatValue(StatDefOf.PsychicSensitivity);
                return $"{StatDefOf.PsychicSensitivity.LabelCap}: {sens.ToStringPercent()}";
            }
            catch { return null; }
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

            if (!(role.AdapterTag is List<PsychicRitualRoleDef> roleDefs) || roleDefs.Count == 0)
                return AssignmentResult.Failed;

            try
            {
                if (pawn.IsAssigned)
                {
                    assignments.TryUnassignAnyRole(pawn.Pawn);
                    return AssignmentResult.Unassigned;
                }
                if (assignments.TryAssign(pawn.Pawn, roleDefs[0], out _))
                    return AssignmentResult.Assigned;
                return AssignmentResult.Failed;
            }
            catch (Exception ex)
            {
                Log.Warning($"[PsychicRitualAdapter] Toggle failed: {ex.Message}");
                return AssignmentResult.Failed;
            }
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
    }
}
