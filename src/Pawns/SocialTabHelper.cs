using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            public PregnancyApproach CurrentPregnancyApproach { get; set; }

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
            public string CertaintyDetails { get; set; }
            public string RoleDetails { get; set; }
        }

        /// <summary>
        /// Represents a social interaction log entry.
        /// </summary>
        public class SocialInteractionInfo
        {
            public string InteractionType { get; set; }
            public string InteractionLabel { get; set; }
            public string Description { get; set; }
            public string Timestamp { get; set; }
            public int AgeTicks { get; set; }
            public bool IsFaded { get; set; }
        }

        /// <summary>
        /// Pregnancy approach options.
        /// </summary>
        public enum PregnancyApproach
        {
            None,
            TryForPregnancy,
            AvoidPregnancy,
            UseContraceptives
        }

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

            // Get detailed certainty info
            info.CertaintyDetails = GetCertaintyDetails(pawn);

            // Get detailed role info
            if (info.Role != null)
            {
                info.RoleDetails = GetRoleDetails(pawn, info.Role);
            }

            return info;
        }

        private static string GetCertaintyDetails(Pawn pawn)
        {
            if (pawn?.ideo == null)
                return "";

            var sb = new StringBuilder();
            sb.AppendLine($"Certainty: {pawn.ideo.Certainty:P0}");
            sb.AppendLine();

            // Get certainty change rate
            float certaintyChangePerDay = pawn.ideo.CertaintyChangePerDay;
            if (Math.Abs(certaintyChangePerDay) > 0.001f)
            {
                string direction = certaintyChangePerDay > 0 ? "increasing" : "decreasing";
                sb.AppendLine($"Certainty is {direction}");
                sb.AppendLine();
            }

            sb.AppendLine("Certainty is a measure of how strongly this colonist believes in their ideology.");
            sb.AppendLine("Higher certainty makes them more resistant to conversion attempts.");

            return sb.ToString().TrimEnd();
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

                // Check if can change pregnancy approach
                if (LovePartnerRelationUtility.LovePartnerRelationExists(pawn, otherPawn))
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

        private static PregnancyApproach GetPregnancyApproach(Pawn pawn, Pawn partner)
        {
            // This is a simplified version - actual implementation would check the game's pregnancy settings
            // For now, return None as placeholder
            return PregnancyApproach.None;
        }

        /// <summary>
        /// Sets pregnancy approach between two pawns.
        /// </summary>
        public static bool SetPregnancyApproach(Pawn pawn, Pawn partner, PregnancyApproach approach)
        {
            try
            {
                // Placeholder - actual implementation would set the game's pregnancy approach
                TolkHelper.Speak($"Pregnancy approach set to: {approach}");
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error setting pregnancy approach: {ex}");
                TolkHelper.Speak("Error setting pregnancy approach", SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        #endregion

        #region Social Interactions

        /// <summary>
        /// Gets recent social interactions for a pawn.
        /// Follows RimWorld's display logic: shows up to 12 interactions from the play log.
        /// </summary>
        public static List<SocialInteractionInfo> GetSocialInteractions(Pawn pawn)
        {
            var interactions = new List<SocialInteractionInfo>();

            if (pawn == null)
                return interactions;

            // Get interactions from play log
            var playLog = Find.PlayLog;
            if (playLog == null)
                return interactions;

            const int maxEntries = 12; // Match the game's default
            const int fadeAgeTicks = 7500; // Ticks after which entries appear faded (~2 hours)

            // Iterate through all play log entries
            foreach (var entry in playLog.AllEntries)
            {
                // Check if this entry concerns the pawn (is the pawn involved in this entry?)
                if (!entry.Concerns(pawn))
                    continue;

                // Get the description from this pawn's point of view
                string description = entry.ToGameStringFromPOV(pawn);

                // Get additional details
                string interactionType = "Event";
                string interactionLabel = "";

                // Check if this is a PlayLogEntry_Interaction to get the interaction type
                if (entry is PlayLogEntry_Interaction interactionEntry)
                {
                    // Use reflection to access the protected intDef field
                    var intDefField = typeof(PlayLogEntry_Interaction).GetField("intDef",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (intDefField != null)
                    {
                        var intDef = intDefField.GetValue(interactionEntry) as InteractionDef;
                        if (intDef != null)
                        {
                            interactionType = intDef.label;
                            interactionLabel = intDef.LabelCap;
                        }
                    }
                }

                // Get timestamp
                int ageTicks = entry.Age;
                string timestamp = ageTicks.ToStringTicksToPeriod();

                interactions.Add(new SocialInteractionInfo
                {
                    InteractionType = interactionType,
                    InteractionLabel = interactionLabel,
                    Description = !string.IsNullOrEmpty(description) ? description.StripTags() : "[No description]",
                    Timestamp = timestamp,
                    AgeTicks = ageTicks,
                    IsFaded = ageTicks > fadeAgeTicks
                });

                if (interactions.Count >= maxEntries)
                    break;
            }

            return interactions;
        }

        #endregion
    }
}
