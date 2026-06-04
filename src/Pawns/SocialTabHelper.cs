using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
            public List<string> DetailLines { get; set; }
            public int RelationshipLineIndex { get; set; }
            public bool CanChangePregnancyApproach { get; set; }
            public RimWorld.PregnancyApproach CurrentPregnancyApproach { get; set; }

            public RelationInfo()
            {
                Relations = new List<string>();
                DetailLines = new List<string>();
                RelationshipLineIndex = -1;
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

            string noneLabel = "RimWorldAccess.Pawns.Social.Ideology.None".Translate();
            var info = new IdeologyInfo
            {
                Ideo = pawn.Ideo,
                IdeoName = pawn.Ideo?.name ?? noneLabel,
                Certainty = pawn.ideo.Certainty
            };

            // Get role
            if (pawn.Ideo != null)
            {
                info.Role = pawn.Ideo.GetRole(pawn);
                info.RoleName = info.Role?.LabelCap ?? noneLabel;
            }

            return info;
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
                TolkHelper.Speak("RimWorldAccess.Pawns.Social.RoleAssigned".Loc(pawn.LabelShort, roleName));
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error assigning role: {ex}");
                TolkHelper.Speak("RimWorldAccess.Pawns.Social.ErrorAssignRole".Loc(), SpeechPriority.High);
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
                TolkHelper.Speak("RimWorldAccess.Pawns.Social.RoleUnassigned".Loc(pawn.LabelShort, roleName));
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error unassigning role: {ex}");
                TolkHelper.Speak("RimWorldAccess.Pawns.Social.ErrorUnassignRole".Loc(), SpeechPriority.High);
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
                var relationInfo = BuildRelationInfo(pawn, otherPawn, pawn.relations.OpinionOf(otherPawn));
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
                        relations.Add(BuildRelationInfo(pawn, otherPawn, opinion));
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

            // Add friendship/rivalry labels if no family relations.
            // "Friend" / "Rival" / "Acquaintance" reuse the vanilla keys of the same names.
            if (labels.Count == 0)
            {
                int opinion = pawn.relations.OpinionOf(otherPawn);
                if (opinion >= 20)
                    labels.Add("Friend".Translate());
                else if (opinion <= -20)
                    labels.Add("Rival".Translate());
                else
                    labels.Add("Acquaintance".Translate());
            }

            return labels;
        }

        private static RelationInfo BuildRelationInfo(Pawn pawn, Pawn otherPawn, int myOpinion)
        {
            var info = new RelationInfo
            {
                OtherPawn = otherPawn,
                OtherPawnName = otherPawn.LabelShort.StripTags(),
                Relations = GetRelationLabels(pawn, otherPawn),
                MyOpinion = myOpinion,
                TheirOpinion = otherPawn.relations?.OpinionOf(pawn) ?? 0
            };

            PopulateDetailLines(info, pawn, otherPawn);

            if (ModsConfig.BiotechActive && LovePartnerRelationUtility.LovePartnerRelationExists(pawn, otherPawn))
            {
                info.CanChangePregnancyApproach = true;
                info.CurrentPregnancyApproach = GetPregnancyApproach(pawn, otherPawn);
            }

            return info;
        }

        private static void PopulateDetailLines(RelationInfo info, Pawn pawn, Pawn otherPawn)
        {
            var lines = info.DetailLines;

            lines.Add("RimWorldAccess.Pawns.Social.Relation.Header"
                .Translate(otherPawn.LabelShort.StripTags()));

            if (info.Relations.Count > 0)
            {
                info.RelationshipLineIndex = lines.Count;
                lines.Add("RimWorldAccess.Pawns.Social.Relation.Relationships"
                    .Translate(string.Join(", ", info.Relations)));
            }

            lines.Add("RimWorldAccess.Pawns.Social.Relation.MyOpinion"
                .Translate(info.MyOpinion.ToString("+0;-0;0")));
            lines.Add("RimWorldAccess.Pawns.Social.Relation.TheirOpinion"
                .Translate(info.TheirOpinion.ToString("+0;-0;0")));

            lines.Add("RimWorldAccess.Pawns.Social.Relation.OpinionFactorsHeader".Translate());

            bool hasFactors = false;

            var directRelations = pawn.relations.DirectRelations;
            if (directRelations != null)
            {
                foreach (var rel in directRelations)
                {
                    if (rel.otherPawn == otherPawn && rel.def.opinionOffset != 0)
                    {
                        lines.Add("RimWorldAccess.Pawns.Social.Relation.OpinionFactor"
                            .Translate(rel.def.GetGenderSpecificLabelCap(otherPawn),
                                rel.def.opinionOffset.ToString("+0;-0;0")));
                        hasFactors = true;
                    }
                }
            }

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

                        int count = 1;
                        if (thought.def.IsMemory && socialThought is Thought_MemorySocial memorySocial)
                        {
                            count = thoughts.memories.NumMemoriesInGroup(memorySocial);
                        }

                        if (count > 1)
                        {
                            label += $" x{count}";
                        }

                        lines.Add("RimWorldAccess.Pawns.Social.Relation.OpinionFactor"
                            .Translate(label, opinionOffset.ToString("+0;-0;0")));
                        hasFactors = true;
                    }
                }
            }

            if (!hasFactors)
            {
                lines.Add("RimWorldAccess.Pawns.Social.Relation.NoFactors".Translate());
            }

            if (LovePartnerRelationUtility.LovePartnerRelationExists(pawn, otherPawn))
            {
                lines.Add("RimWorldAccess.Pawns.Social.Relation.LovePartners".Translate());
            }
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

                TolkHelper.Speak("RimWorldAccess.Pawns.Social.PregnancyApproachSet".Loc(approachLabel));
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error setting pregnancy approach: {ex}");
                TolkHelper.Speak("RimWorldAccess.Pawns.Social.ErrorSetPregnancyApproach".Loc(), SpeechPriority.High);
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
                TolkHelper.Speak("RimWorldAccess.Pawns.Social.ErrorInitiateRomance".Loc(), SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        #endregion
    }
}
