using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper class for social tab data extraction and interactions.
    /// Provides methods for relations, opinions, ideology, and social interactions.
    /// </summary>
    public static class SocialTabHelper
    {
        /// <summary>
        /// Represents a relation with another pawn.
        /// </summary>
        public class RelationInfo
        {
            public Pawn OtherPawn { get; set; }
            public string OtherPawnName { get; set; }
            public List<string> Relations { get; set; }
            public int MyOpinion { get; set; }
            public int TheirOpinion { get; set; }
            public string Situation { get; set; }
            public string DetailedInfo { get; set; }
            public bool CanChangePregnancyApproach { get; set; }
            public RimWorld.PregnancyApproach CurrentPregnancyApproach { get; set; }

            public RelationInfo()
            {
                Relations = new List<string>();
            }
        }

        /// <summary>
        /// Represents ideology and role information.
        /// </summary>
        public class IdeologyInfo
        {
            public Ideo Ideo { get; set; }
            public string IdeoName { get; set; }
            public float Certainty { get; set; }
            public Precept_Role Role { get; set; }
            public string RoleName { get; set; }
            public string RoleDetails { get; set; }
        }

        // Uses RimWorld.PregnancyApproach enum (Normal, AvoidPregnancy, TryForBaby)

        #region Ideology & Role

        /// <summary>
        /// Gets ideology and role information for a pawn.
        /// </summary>
        public static IdeologyInfo GetIdeologyInfo(Pawn pawn)
        {
            if (pawn?.ideo == null || !ModsConfig.IdeologyActive)
                return null;

            var info = new IdeologyInfo
            {
                Ideo = pawn.Ideo,
                IdeoName = pawn.Ideo?.name ?? "None",
                Certainty = pawn.ideo.Certainty
            };

            // Get role
            if (pawn.Ideo != null)
            {
                info.Role = pawn.Ideo.GetRole(pawn);
                info.RoleName = info.Role?.LabelCap ?? "None";
            }

            // Get detailed role info
            if (info.Role != null)
            {
                info.RoleDetails = GetRoleDetails(pawn, info.Role);
            }

            return info;
        }

        private static string GetRoleDetails(Pawn pawn, Precept_Role role)
        {
            var sb = new StringBuilder();
            // Use LabelForPawn for pawn-specific role label
            sb.AppendLine($"Role: {role.LabelForPawn(pawn)}");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(role.def.description))
            {
                sb.AppendLine(role.def.description);
                sb.AppendLine();
            }

            // Role requirements
            if (role.def.roleRequirements != null && role.def.roleRequirements.Count > 0)
            {
                sb.AppendLine("Requirements:");
                foreach (var req in role.def.roleRequirements)
                {
                    // RoleRequirement has GetLabelCap method that takes the role
                    sb.AppendLine($"  {req.GetLabelCap(role)}");
                }
                sb.AppendLine();
            }

            // Role effects
            if (role.def.roleEffects != null && role.def.roleEffects.Count > 0)
            {
                sb.AppendLine("Effects:");
                foreach (var effect in role.def.roleEffects)
                {
                    // RoleEffect.Label requires pawn and role parameters
                    sb.AppendLine($"  {effect.Label(pawn, role)}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Gets all active roles from the pawn's ideology.
        /// </summary>
        public static List<Precept_Role> GetAvailableRoles(Pawn pawn)
        {
            var roles = new List<Precept_Role>();
            if (pawn?.Ideo == null || !ModsConfig.IdeologyActive)
                return roles;

            foreach (var role in pawn.Ideo.RolesListForReading)
            {
                if (role.Active)
                    roles.Add(role);
            }

            return roles;
        }

        /// <summary>
        /// Assigns a pawn to an ideology role.
        /// </summary>
        public static bool AssignRole(Precept_Role role, Pawn pawn)
        {
            try
            {
                if (role == null || pawn == null)
                    return false;

                role.Assign(pawn, addThoughts: true);
                string roleName = role.LabelForPawn(pawn);
                TolkHelper.Speak("RimWorldAccess.Pawns.Social.RoleAssigned".Translate(pawn.LabelShort, roleName));
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error assigning role: {ex}");
                TolkHelper.Speak("RimWorldAccess.Pawns.Social.ErrorAssignRole".Translate(), SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        /// <summary>
        /// Unassigns a pawn from an ideology role.
        /// </summary>
        public static bool UnassignRole(Precept_Role role, Pawn pawn)
        {
            try
            {
                if (role == null || pawn == null)
                    return false;

                string roleName = role.LabelForPawn(pawn);
                role.Unassign(pawn, generateThoughts: true);
                TolkHelper.Speak("RimWorldAccess.Pawns.Social.RoleUnassigned".Translate(pawn.LabelShort, roleName));
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error unassigning role: {ex}");
                TolkHelper.Speak("RimWorldAccess.Pawns.Social.ErrorUnassignRole".Translate(), SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        /// <summary>
        /// Checks if a pawn is eligible for a specific role (meets all requirements).
        /// </summary>
        public static bool IsEligibleForRole(Precept_Role role, Pawn pawn)
        {
            if (role == null || pawn == null)
                return false;

            return role.RequirementsMet(pawn);
        }

        #endregion

        #region Relations

        /// <summary>
        /// Gets all relations for a pawn.
        /// </summary>
        public static List<RelationInfo> GetRelations(Pawn pawn)
        {
            var relations = new List<RelationInfo>();

            if (pawn?.relations == null)
                return relations;

            // Get all pawns this pawn has relations with
            var relatedPawns = pawn.relations.RelatedPawns;

            foreach (var otherPawn in relatedPawns)
            {
                var relationInfo = new RelationInfo
                {
                    OtherPawn = otherPawn,
                    OtherPawnName = otherPawn.LabelShort.StripTags(),
                    Relations = GetRelationLabels(pawn, otherPawn),
                    MyOpinion = pawn.relations.OpinionOf(otherPawn),
                    TheirOpinion = otherPawn.relations?.OpinionOf(pawn) ?? 0,
                    Situation = GetPawnSituation(otherPawn),
                    DetailedInfo = GetRelationDetailedInfo(pawn, otherPawn)
                };

                // Check if can change pregnancy approach (Biotech DLC required)
                if (ModsConfig.BiotechActive && LovePartnerRelationUtility.LovePartnerRelationExists(pawn, otherPawn))
                {
                    relationInfo.CanChangePregnancyApproach = true;
                    relationInfo.CurrentPregnancyApproach = GetPregnancyApproach(pawn, otherPawn);
                }

                relations.Add(relationInfo);
            }

            // Also include pawns with non-zero opinions even if no direct relations
            if (pawn.Map != null)
            {
                var allPawns = pawn.Map.mapPawns.AllPawnsSpawned;
                foreach (var otherPawn in allPawns)
                {
                    if (otherPawn == pawn || relatedPawns.Contains(otherPawn))
                        continue;

                    int opinion = pawn.relations.OpinionOf(otherPawn);
                    if (opinion != 0)
                    {
                        var relationInfo = new RelationInfo
                        {
                            OtherPawn = otherPawn,
                            OtherPawnName = otherPawn.LabelShort.StripTags(),
                            Relations = GetRelationLabels(pawn, otherPawn),
                            MyOpinion = opinion,
                            TheirOpinion = otherPawn.relations?.OpinionOf(pawn) ?? 0,
                            Situation = GetPawnSituation(otherPawn),
                            DetailedInfo = GetRelationDetailedInfo(pawn, otherPawn)
                        };

                        relations.Add(relationInfo);
                    }
                }
            }

            // Sort by opinion (most positive first)
            relations = relations.OrderByDescending(r => r.MyOpinion).ToList();

            return relations;
        }

        private static List<string> GetRelationLabels(Pawn pawn, Pawn otherPawn)
        {
            var labels = new List<string>();

            var directRelations = pawn.relations.DirectRelations;
            if (directRelations != null)
            {
                foreach (var rel in directRelations)
                {
                    if (rel.otherPawn == otherPawn)
                    {
                        // Use GetGenderSpecificLabelCap to get the correct label based on the other pawn's gender
                        labels.Add(rel.def.GetGenderSpecificLabelCap(otherPawn));
                    }
                }
            }

            // Add friendship/rivalry labels if no family relations
            if (labels.Count == 0)
            {
                int opinion = pawn.relations.OpinionOf(otherPawn);
                if (opinion >= 20)
                    labels.Add("Friend");
                else if (opinion <= -20)
                    labels.Add("Rival");
                else
                    labels.Add("Acquaintance");
            }

            return labels;
        }

        private static string GetPawnSituation(Pawn pawn)
        {
            if (pawn.Dead)
                return "Dead";
            if (pawn.Destroyed && !pawn.Dead)
                return "Missing";
            if (pawn.IsPrisonerOfColony)
                return "Prisoner";
            if (pawn.IsSlaveOfColony)
                return "Slave";
            if (pawn.Faction == null)
                return "No faction";

            string factionLabel = pawn.Faction.Name;
            if (pawn.Faction.HostileTo(Faction.OfPlayer))
                return $"Hostile, {factionLabel}";
            if (pawn.Faction.IsPlayer)
                return $"Colonist, {factionLabel}";
            return $"Neutral, {factionLabel}";
        }

        private static string GetRelationDetailedInfo(Pawn pawn, Pawn otherPawn)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Relation with {otherPawn.LabelShort.StripTags()}:");
            sb.AppendLine();

            // Relations
            var relations = GetRelationLabels(pawn, otherPawn);
            if (relations.Count > 0)
            {
                sb.AppendLine($"Relationship: {string.Join(", ", relations)}");
            }

            // Opinions
            int myOpinion = pawn.relations.OpinionOf(otherPawn);
            int theirOpinion = otherPawn.relations?.OpinionOf(pawn) ?? 0;

            sb.AppendLine($"My opinion: {myOpinion:+0;-0;0}");
            sb.AppendLine($"Their opinion: {theirOpinion:+0;-0;0}");
            sb.AppendLine();

            // Opinion breakdown
            sb.AppendLine("Opinion factors:");
            bool hasFactors = false;

            // Add relation opinion modifiers
            var directRelations = pawn.relations.DirectRelations;
            if (directRelations != null)
            {
                foreach (var rel in directRelations)
                {
                    if (rel.otherPawn == otherPawn && rel.def.opinionOffset != 0)
                    {
                        sb.AppendLine($"  {rel.def.GetGenderSpecificLabelCap(otherPawn)}: {rel.def.opinionOffset:+0;-0;0}");
                        hasFactors = true;
                    }
                }
            }

            // Add social thought opinion modifiers
            if (pawn.RaceProps.Humanlike && pawn.needs?.mood?.thoughts != null)
            {
                var thoughts = pawn.needs.mood.thoughts;
                var socialThoughts = new List<ISocialThought>();
                thoughts.GetDistinctSocialThoughtGroups(otherPawn, socialThoughts);

                foreach (var socialThought in socialThoughts)
                {
                    int opinionOffset = thoughts.OpinionOffsetOfGroup(socialThought, otherPawn);
                    if (opinionOffset != 0)
                    {
                        Thought thought = (Thought)socialThought;
                        string label = thought.LabelCapSocial.StripTags();

                        // Check if there are multiple instances of this thought
                        int count = 1;
                        if (thought.def.IsMemory && socialThought is Thought_MemorySocial memorySocial)
                        {
                            count = thoughts.memories.NumMemoriesInGroup(memorySocial);
                        }

                        if (count > 1)
                        {
                            label += $" x{count}";
                        }

                        sb.AppendLine($"  {label}: {opinionOffset:+0;-0;0}");
                        hasFactors = true;
                    }
                }
            }

            if (!hasFactors)
            {
                sb.AppendLine("  (None)");
            }

            // Romance info if applicable
            if (LovePartnerRelationUtility.LovePartnerRelationExists(pawn, otherPawn))
            {
                sb.AppendLine();
                sb.AppendLine("Love partners");
            }

            return sb.ToString().TrimEnd();
        }

        private static RimWorld.PregnancyApproach GetPregnancyApproach(Pawn pawn, Pawn partner)
        {
            if (pawn?.relations == null || partner == null)
                return RimWorld.PregnancyApproach.Normal;

            return pawn.relations.GetPregnancyApproachForPartner(partner);
        }

        /// <summary>
        /// Sets pregnancy approach between two pawns.
        /// Uses the game's Pawn_RelationsTracker API which sets it on both pawns.
        /// </summary>
        public static bool SetPregnancyApproach(Pawn pawn, Pawn partner, RimWorld.PregnancyApproach approach)
        {
            try
            {
                if (pawn?.relations == null || partner == null)
                    return false;

                pawn.relations.SetPregnancyApproach(partner, approach);

                string approachLabel = approach.GetLabel().CapitalizeFirst();

                TolkHelper.Speak("RimWorldAccess.Pawns.Social.PregnancyApproachSet".Translate(approachLabel));
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error setting pregnancy approach: {ex}");
                TolkHelper.Speak("RimWorldAccess.Pawns.Social.ErrorSetPregnancyApproach".Translate(), SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        #endregion

        #region Romance

        /// <summary>
        /// Represents a potential romance target with eligibility and chance info.
        /// </summary>
        public class RomanceTargetInfo
        {
            public Pawn Target { get; set; }
            public string TargetName { get; set; }
            public bool IsViable { get; set; }
            public float Chance { get; set; }
            public string Reason { get; set; }
        }

        /// <summary>
        /// Checks if the Try Romance button should be visible for this pawn.
        /// Mirrors SocialCardUtility.CanDrawTryRomance.
        /// </summary>
        public static bool CanTryRomance(Pawn pawn)
        {
            return ModsConfig.BiotechActive
                && pawn.ageTracker.AgeBiologicalYearsFloat >= 16f
                && pawn.Spawned
                && pawn.IsFreeColonist;
        }

        /// <summary>
        /// Checks if the pawn is on romance cooldown and returns the translated cooldown message.
        /// </summary>
        public static bool IsRomanceOnCooldown(Pawn pawn, out string cooldownText)
        {
            if (pawn.relations.IsTryRomanceOnCooldown)
            {
                int numTicks = pawn.relations.romanceEnableTick - Find.TickManager.TicksGame;
                cooldownText = "CantRomanceInitiateMessageCooldown".Translate(pawn, numTicks.ToStringTicksToPeriod());
                return true;
            }
            cooldownText = null;
            return false;
        }

        /// <summary>
        /// Checks if the pawn is eligible to initiate romance.
        /// Wraps RelationsUtility.RomanceEligible.
        /// </summary>
        public static AcceptanceReport GetRomanceInitiatorEligibility(Pawn pawn)
        {
            return RelationsUtility.RomanceEligible(pawn, initiator: true, forOpinionExplanation: false);
        }

        /// <summary>
        /// Gets all romance targets for a pawn, sorted like vanilla:
        /// viable targets descending by chance, then non-viable alphabetically.
        /// Mirrors SocialCardUtility.RomanceOptions.
        /// </summary>
        public static List<RomanceTargetInfo> GetRomanceTargets(Pawn romancer)
        {
            var viable = new List<(float chance, RomanceTargetInfo info)>();
            var nonViable = new List<RomanceTargetInfo>();

            foreach (Pawn target in romancer.Map.mapPawns.FreeColonistsSpawned)
            {
                if (target == romancer)
                    continue;

                // Skip if not attracted (matches vanilla filter in RelationsUtility.RomanceOption)
                if (!RelationsUtility.AttractedToGender(romancer, target.gender))
                    continue;

                var eligibility = RelationsUtility.RomanceEligiblePair(romancer, target, forOpinionExplanation: false);

                if (eligibility.Accepted)
                {
                    float chance = InteractionWorker_RomanceAttempt.SuccessChance(romancer, target, 1f);

                    viable.Add((chance, new RomanceTargetInfo
                    {
                        Target = target,
                        TargetName = target.LabelShort.StripTags(),
                        IsViable = true,
                        Chance = chance
                    }));
                }
                else if (!eligibility.Reason.NullOrEmpty())
                {
                    nonViable.Add(new RomanceTargetInfo
                    {
                        Target = target,
                        TargetName = target.LabelShort.StripTags(),
                        IsViable = false,
                        Reason = eligibility.Reason
                    });
                }
            }

            var result = new List<RomanceTargetInfo>();
            result.AddRange(viable.OrderByDescending(v => v.chance).Select(v => v.info));
            result.AddRange(nonViable.OrderBy(nv => nv.TargetName));
            return result;
        }

        /// <summary>
        /// Builds a descriptive romance factor breakdown for StatBreakdownState.
        /// Includes overall chance header and reformats the game's "x" notation
        /// into clearer multiplier format for screen reader users.
        /// </summary>
        public static string BuildRomanceBreakdown(Pawn romancer, Pawn target)
        {
            // Get the game's factor breakdown and reformat for accessibility
            string factors = InteractionWorker_RomanceAttempt.RomanceFactors(romancer, target).StripTags();
            // Replace "x" multiplier notation (e.g. ": x22%") with plain percentage
            // The "x" is visual shorthand that reads poorly with screen readers
            factors = System.Text.RegularExpressions.Regex.Replace(factors, @": x(\d)", ": $1");
            // Strip leading " - " so each factor line is a flat root item in StatBreakdownState
            // Without this, the " - " prefix causes indent level 1, creating a collapsed parent node
            factors = System.Text.RegularExpressions.Regex.Replace(factors, @"(?m)^ - ", "");

            return factors;
        }

        private static MethodInfo giveRomanceJobWithWarningMethod;

        /// <summary>
        /// Initiates a romance attempt, showing warning dialog if the pawn has existing relationships.
        /// Uses reflection to call RelationsUtility.GiveRomanceJobWithWarning (private).
        /// Returns false if the pawn already has a romance job queued.
        /// </summary>
        public static bool InitiateRomance(Pawn romancer, Pawn target)
        {
            try
            {
                // Guard against duplicate romance jobs - vanilla doesn't need this because
                // the float menu closes after selection, but our tree keeps the action available
                if (romancer.CurJob?.def == JobDefOf.TryRomance ||
                    romancer.jobs.jobQueue.Any(j => j.job.def == JobDefOf.TryRomance))
                {
                    return false;
                }

                if (giveRomanceJobWithWarningMethod == null)
                {
                    giveRomanceJobWithWarningMethod = AccessTools.Method(
                        typeof(RelationsUtility), "GiveRomanceJobWithWarning");
                }

                giveRomanceJobWithWarningMethod.Invoke(null, new object[] { romancer, target });
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error initiating romance: {ex}");
                TolkHelper.Speak("RimWorldAccess.Pawns.Social.ErrorInitiateRomance".Translate(), SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        #endregion
    }
}
