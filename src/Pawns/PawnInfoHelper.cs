using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper class for extracting detailed pawn information for accessibility features.
    /// Provides methods to get health, needs, gear, social, training, character, and work info.
    /// </summary>
    public static class PawnInfoHelper
    {
        /// <summary>
        /// Gets the current task/job the pawn is performing.
        /// </summary>
        public static string GetCurrentTask(Pawn pawn)
        {
            if (pawn == null)
                return "RimWorldAccess.Pawns.Info.NoPawn".Translate();

            string task = pawn.GetJobReport();
            if (string.IsNullOrEmpty(task))
            {
                return "RimWorldAccess.Pawns.Info.Idle".Translate();
            }
            return task;
        }

        /// <summary>
        /// Gets a combat-focused health summary for the pawn.
        /// Shows critical triage info: pain, bleeding/time-to-death, impaired
        /// sight/manipulation/moving capacities, body parts with bad conditions,
        /// and whole-body hediffs.
        /// Skips bionics/implants unless the part is actually injured, and skips
        /// chronic conditions that aren't combat-relevant.
        /// </summary>
        public static string GetHealthInfo(Pawn pawn)
        {
            if (pawn == null)
                return "RimWorldAccess.Pawns.Info.NoPawnSelected".Translate();

            if (pawn.health == null)
                return "RimWorldAccess.Pawns.Health.NoTracker".Translate(pawn.LabelShort);

            StringBuilder sb = new StringBuilder();
            sb.Append("RimWorldAccess.Pawns.Health.NameHeader".Translate(pawn.LabelShort));
            bool hasHealthDetails = false;

            // Pain (uses vanilla translation keys and qualitative labels)
            string painLabel = HealthTabHelper.GetPainLabel(pawn);
            if (painLabel != null)
            {
                sb.Append("RimWorldAccess.Pawns.Health.SentenceSuffix".Translate(painLabel));
                hasHealthDetails = true;
            }

            // Bleeding with time-to-death (uses vanilla translation keys)
            string bleedingLabel = HealthTabHelper.GetBleedingLabel(pawn);
            if (bleedingLabel != null)
            {
                sb.Append("RimWorldAccess.Pawns.Health.SentenceSuffix".Translate(bleedingLabel));
                hasHealthDetails = true;
            }

            // Capacities — only show sight, manipulation, and moving, and only when not at 100%
            if (pawn.health.capacities != null && !pawn.Dead)
            {
                var impaired = HealthTabHelper.GetCapacities(pawn)
                    .Where(c => c.Def == PawnCapacityDefOf.Sight
                             || c.Def == PawnCapacityDefOf.Manipulation
                             || c.Def == PawnCapacityDefOf.Moving)
                    .Where(c => c.Level < 0.999f || c.Level > 1.001f)
                    .ToList();

                foreach (var cap in impaired)
                {
                    sb.Append("RimWorldAccess.Pawns.Health.Capacity".Translate(cap.Label, cap.LevelLabel));
                    hasHealthDetails = true;
                }
            }

            // Body parts — only show parts that have at least one bad hediff, with HP
            var badPartHediffs = HealthTabHelper.GetVisibleHediffs(pawn)
                .Where(h => h.Part != null && h.def.isBad)
                .Select(h => h.Part)
                .Distinct()
                .ToList();

            if (badPartHediffs.Count > 0)
            {
                var damagedParts = badPartHediffs
                    .Select(part => new {
                        Part = part,
                        Health = pawn.health.hediffSet.GetPartHealth(part),
                        MaxHealth = part.def.GetMaxHealth(pawn)
                    })
                    .Where(p => p.Health < p.MaxHealth * 0.999f)
                    .OrderBy(p => p.Health / p.MaxHealth)
                    .ToList();

                foreach (var p in damagedParts)
                {
                    sb.Append("RimWorldAccess.Pawns.Health.PartHealth".Translate(
                        p.Part.LabelCap, p.Health.ToString("F0"), p.MaxHealth.ToString("F0")));
                    hasHealthDetails = true;
                }
            }

            // Whole-body hediffs — show all visible ones
            var wholeBodyHediffs = HealthTabHelper.GetVisibleHediffs(pawn)
                .Where(h => h.Part == null)
                .ToList();

            foreach (var hediff in wholeBodyHediffs)
            {
                sb.Append("RimWorldAccess.Pawns.Health.SentenceSuffix".Translate(hediff.LabelCap));
                hasHealthDetails = true;
            }

            if (!hasHealthDetails)
            {
                sb.Append("RimWorldAccess.Pawns.Health.HealthySuffix".Translate("Healthy".Translate()));
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Gets needs information for the pawn.
        /// Lists all needs with their current percentages, sorted from lowest to highest (most urgent first).
        /// </summary>
        public static string GetNeedsInfo(Pawn pawn)
        {
            if (pawn == null)
                return "RimWorldAccess.Pawns.Info.NoPawnSelected".Translate();

            if (pawn.needs == null)
                return "RimWorldAccess.Pawns.Needs.NoTracker".Translate(pawn.LabelShort);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("RimWorldAccess.Pawns.Needs.Header".Translate(pawn.LabelShort));

            var needs = pawn.needs.AllNeeds;
            if (needs != null && needs.Count > 0)
            {
                // Filter to visible needs and sort by percentage (lowest first = most urgent)
                var sortedNeeds = needs
                    .Where(n => n.def.showOnNeedList)
                    .OrderBy(n => n.CurLevelPercentage)
                    .ToList();

                foreach (var need in sortedNeeds)
                {
                    float percentage = need.CurLevelPercentage * 100f;
                    sb.AppendLine("RimWorldAccess.Pawns.Needs.NeedLine".Translate(need.LabelCap, percentage.ToString("F0")));

                    // After the Learning need, list active learning desires (Biotech children only)
                    if (need.def == NeedDefOf.Learning && pawn.learning?.ActiveLearningDesires != null
                        && pawn.learning.ActiveLearningDesires.Count > 0)
                    {
                        var desireLabels = pawn.learning.ActiveLearningDesires
                            .Select(d => d.LabelCap.ToString())
                            .ToArray();
                        sb.AppendLine("RimWorldAccess.Pawns.Needs.LearningDesires".Translate(string.Join(", ", desireLabels)));
                    }
                }
            }
            else
            {
                sb.AppendLine("RimWorldAccess.Pawns.Needs.Empty".Translate());
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Gets mood information for the pawn.
        /// Shows mood level, mood description, and all thoughts affecting mood.
        /// </summary>
        public static string GetMoodInfo(Pawn pawn)
        {
            if (pawn == null)
                return "RimWorldAccess.Pawns.Info.NoPawnSelected".Translate();

            if (pawn.needs?.mood == null)
                return "RimWorldAccess.Pawns.Mood.NoTracker".Translate(pawn.LabelShort);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("RimWorldAccess.Pawns.Mood.Header".Translate(pawn.LabelShort));

            Need_Mood mood = pawn.needs.mood;

            // Current mood level and description
            float moodPercentage = mood.CurLevelPercentage * 100f;
            string moodDescription = mood.MoodString;
            sb.AppendLine("RimWorldAccess.Pawns.Mood.MoodLine".Translate(moodPercentage.ToString("F0"), moodDescription));

            // Get thoughts affecting mood
            List<Thought> thoughtGroups = new List<Thought>();
            PawnNeedsUIUtility.GetThoughtGroupsInDisplayOrder(mood, thoughtGroups);

            if (thoughtGroups.Count > 0)
            {
                sb.AppendLine("RimWorldAccess.Pawns.Mood.ThoughtsHeader".Translate(thoughtGroups.Count));

                List<Thought> thoughtGroup = new List<Thought>();
                foreach (Thought group in thoughtGroups)
                {
                    mood.thoughts.GetMoodThoughts(group, thoughtGroup);

                    if (thoughtGroup.Count == 0)
                        continue;

                    Thought leadingThought = PawnNeedsUIUtility.GetLeadingThoughtInGroup(thoughtGroup);

                    if (leadingThought == null || !leadingThought.VisibleInNeedsTab)
                        continue;

                    float moodOffset = mood.thoughts.MoodOffsetOfGroup(group);

                    string thoughtLabel = leadingThought.LabelCap;
                    if (thoughtGroup.Count > 1)
                    {
                        thoughtLabel = "RimWorldAccess.Pawns.Mood.ThoughtMultiplier".Translate(thoughtLabel, thoughtGroup.Count);
                    }

                    string offsetText = moodOffset.ToString("+0;-0;0");
                    sb.AppendLine("RimWorldAccess.Pawns.Mood.ThoughtLine".Translate(thoughtLabel, offsetText));

                    thoughtGroup.Clear();
                }
            }
            else
            {
                sb.AppendLine("RimWorldAccess.Pawns.Mood.NoThoughts".Translate());
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Gets gear information for the pawn in sentence format.
        /// Shows weapon being wielded and apparel being worn, with quality but no durability.
        /// </summary>
        public static string GetGearInfo(Pawn pawn)
        {
            if (pawn == null)
                return "RimWorldAccess.Pawns.Info.NoPawnSelected".Translate();

            StringBuilder sb = new StringBuilder();
            sb.Append("RimWorldAccess.Pawns.GearInfo.Header".Translate(pawn.LabelShort));

            // Weapon
            if (pawn.equipment != null && pawn.equipment.Primary != null)
            {
                var weapon = pawn.equipment.Primary;
                sb.Append("RimWorldAccess.Pawns.GearInfo.WieldingItem".Translate(weapon.LabelNoParenthesisCap.StripTags()));
                var qualityComp = weapon.TryGetComp<CompQuality>();
                if (qualityComp != null)
                {
                    sb.Append("RimWorldAccess.Pawns.GearInfo.QualityParen".Translate(qualityComp.Quality.GetLabel()));
                }
                sb.Append("RimWorldAccess.Pawns.GearInfo.WieldingTerminator".Translate());
            }
            else
            {
                sb.Append("RimWorldAccess.Pawns.GearInfo.WieldingNothing".Translate());
            }

            // Apparel
            if (pawn.apparel != null && pawn.apparel.WornApparel != null && pawn.apparel.WornApparel.Count > 0)
            {
                var apparelList = new List<string>();
                foreach (var apparel in pawn.apparel.WornApparel)
                {
                    string apparelEntry = apparel.LabelNoParenthesisCap.StripTags();
                    var qualityComp = apparel.TryGetComp<CompQuality>();
                    if (qualityComp != null)
                    {
                        apparelEntry += "RimWorldAccess.Pawns.GearInfo.QualityParen".Translate(qualityComp.Quality.GetLabel());
                    }
                    apparelList.Add(apparelEntry);
                }
                sb.Append("RimWorldAccess.Pawns.GearInfo.Wearing".Translate(string.Join(", ", apparelList)));
            }
            else
            {
                sb.Append("RimWorldAccess.Pawns.GearInfo.WearingNothing".Translate());
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets social information for the pawn.
        /// Lists relationships and opinions.
        /// </summary>
        public static string GetSocialInfo(Pawn pawn)
        {
            if (pawn == null)
                return "RimWorldAccess.Pawns.Info.NoPawnSelected".Translate();

            if (pawn.relations == null)
                return "RimWorldAccess.Pawns.SocialInfo.NoTracker".Translate(pawn.LabelShort);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("RimWorldAccess.Pawns.SocialInfo.Header".Translate(pawn.LabelShort));

            // Direct relations (family, lovers, etc.)
            var directRelations = pawn.relations.DirectRelations;
            if (directRelations != null && directRelations.Count > 0)
            {
                sb.AppendLine("RimWorldAccess.Pawns.SocialInfo.RelationshipsHeader".Translate(directRelations.Count));
                foreach (var rel in directRelations)
                {
                    if (rel.otherPawn != null)
                    {
                        // Use GetGenderSpecificLabelCap to get the correct label based on the other pawn's gender
                        string relationLabel = rel.def.GetGenderSpecificLabelCap(rel.otherPawn);
                        sb.AppendLine("RimWorldAccess.Pawns.SocialInfo.RelationshipLine".Translate(relationLabel, rel.otherPawn.LabelShort));
                    }
                }
            }

            // Opinions (checking pawns that have opinions about this pawn)
            var allPawns = pawn.Map?.mapPawns.AllPawnsSpawned;
            if (allPawns != null)
            {
                var opinions = new List<string>();
                foreach (var otherPawn in allPawns)
                {
                    if (otherPawn != pawn && otherPawn.relations != null)
                    {
                        int opinion = otherPawn.relations.OpinionOf(pawn);
                        if (opinion != 0)
                        {
                            opinions.Add("RimWorldAccess.Pawns.SocialInfo.OpinionLine".Translate(otherPawn.LabelShort, opinion.ToString("+0;-0")));
                        }
                    }
                }

                if (opinions.Count > 0)
                {
                    sb.AppendLine("RimWorldAccess.Pawns.SocialInfo.OpinionsHeader".Translate(opinions.Count));
                    foreach (var opinion in opinions.Take(10)) // Limit to 10 to avoid spam
                    {
                        sb.AppendLine(opinion);
                    }
                    if (opinions.Count > 10)
                    {
                        sb.AppendLine("RimWorldAccess.Pawns.SocialInfo.OpinionsMore".Translate(opinions.Count - 10));
                    }
                }
            }

            if (directRelations == null || directRelations.Count == 0)
            {
                sb.AppendLine("RimWorldAccess.Pawns.SocialInfo.NoRelationships".Translate());
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Gets training information for the pawn (mainly for animals).
        /// </summary>
        public static string GetTrainingInfo(Pawn pawn)
        {
            if (pawn == null)
                return "RimWorldAccess.Pawns.Info.NoPawnSelected".Translate();

            if (pawn.training == null)
                return "RimWorldAccess.Pawns.TrainingInfo.NotTrainable".Translate(pawn.LabelShort);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("RimWorldAccess.Pawns.TrainingInfo.Header".Translate(pawn.LabelShort));

            var trainableDefs = DefDatabase<TrainableDef>.AllDefsListForReading;
            var trainedSkills = new List<string>();
            var untrainedSkills = new List<string>();

            foreach (var trainable in trainableDefs)
            {
                if (pawn.training.CanAssignToTrain(trainable).Accepted)
                {
                    if (pawn.training.HasLearned(trainable))
                    {
                        trainedSkills.Add("RimWorldAccess.Pawns.TrainingInfo.LineLearned".Translate(trainable.LabelCap));
                    }
                    else
                    {
                        // Training in progress - show without step count since GetSteps is not available
                        untrainedSkills.Add("RimWorldAccess.Pawns.TrainingInfo.LineInProgress".Translate(trainable.LabelCap));
                    }
                }
            }

            if (trainedSkills.Count > 0)
            {
                sb.AppendLine("RimWorldAccess.Pawns.TrainingInfo.TrainedHeader".Translate(trainedSkills.Count));
                foreach (var skill in trainedSkills)
                {
                    sb.AppendLine(skill);
                }
            }

            if (untrainedSkills.Count > 0)
            {
                sb.AppendLine("RimWorldAccess.Pawns.TrainingInfo.InProgressHeader".Translate(untrainedSkills.Count));
                foreach (var skill in untrainedSkills)
                {
                    sb.AppendLine(skill);
                }
            }

            if (trainedSkills.Count == 0 && untrainedSkills.Count == 0)
            {
                sb.AppendLine("RimWorldAccess.Pawns.TrainingInfo.NoSkills".Translate());
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Gets character information for the pawn.
        /// Includes traits, backstory, and skills.
        /// </summary>
        public static string GetCharacterInfo(Pawn pawn)
        {
            if (pawn == null)
                return "RimWorldAccess.Pawns.Info.NoPawnSelected".Translate();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("RimWorldAccess.Pawns.Character.Header".Translate(pawn.LabelShort));

            // Basic info
            if (pawn.ageTracker != null)
            {
                sb.AppendLine("RimWorldAccess.Pawns.Character.Age".Translate(pawn.ageTracker.AgeBiologicalYears));
            }

            // Traits
            if (pawn.story != null && pawn.story.traits != null)
            {
                var traits = pawn.story.traits.allTraits;
                if (traits != null && traits.Count > 0)
                {
                    sb.AppendLine("RimWorldAccess.Pawns.Character.TraitsHeader".Translate(traits.Count));
                    foreach (var trait in traits)
                    {
                        sb.AppendLine("RimWorldAccess.Pawns.Character.TraitLine".Translate(trait.LabelCap));
                    }
                }
            }

            // Backstory
            if (pawn.story != null)
            {
                if (pawn.story.Childhood != null)
                {
                    sb.AppendLine("RimWorldAccess.Pawns.Character.Childhood".Translate(pawn.story.Childhood.TitleCapFor(pawn.gender)));
                }
                if (pawn.story.Adulthood != null)
                {
                    sb.AppendLine("RimWorldAccess.Pawns.Character.Adulthood".Translate(pawn.story.Adulthood.TitleCapFor(pawn.gender)));
                }
            }

            // Skills (top skills only)
            if (pawn.skills != null && pawn.skills.skills != null)
            {
                var topSkills = pawn.skills.skills
                    .Where(s => !s.TotallyDisabled && s.Level > 0)
                    .OrderByDescending(s => s.Level)
                    .Take(5);

                if (topSkills.Any())
                {
                    sb.AppendLine("RimWorldAccess.Pawns.Character.TopSkillsHeader".Translate());
                    foreach (var skill in topSkills)
                    {
                        string passion = "";
                        if (skill.passion == Passion.Minor)
                            passion = "RimWorldAccess.Pawns.Character.PassionMinor".Translate();
                        else if (skill.passion == Passion.Major)
                            passion = "RimWorldAccess.Pawns.Character.PassionMajor".Translate();

                        sb.AppendLine("RimWorldAccess.Pawns.Character.SkillLine".Translate(skill.def.LabelCap, skill.Level, passion));
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Gets a concise summary of the pawn's top 3 skills with passion levels.
        /// </summary>
        public static string GetTopSkillsInfo(Pawn pawn)
        {
            if (pawn == null)
                return "RimWorldAccess.Pawns.Info.NoPawnSelected".Translate();

            if (pawn.skills == null || pawn.skills.skills == null)
                return "RimWorldAccess.Pawns.TopSkills.NoSkills".Translate(pawn.LabelShort);

            var topSkills = pawn.skills.skills
                .Where(s => !s.TotallyDisabled && s.Level > 0)
                .OrderByDescending(s => s.Level)
                .Take(3)
                .ToList();

            if (!topSkills.Any())
                return "RimWorldAccess.Pawns.TopSkills.NoSkills".Translate(pawn.LabelShort);

            StringBuilder sb = new StringBuilder();
            sb.Append("RimWorldAccess.Pawns.TopSkills.Header".Translate(pawn.LabelShort));

            foreach (var skill in topSkills)
            {
                sb.Append("RimWorldAccess.Pawns.TopSkills.SkillEntry".Translate(skill.def.LabelCap, skill.Level));

                if (skill.passion == Passion.Minor)
                    sb.Append("RimWorldAccess.Pawns.TopSkills.PassionMinor".Translate());
                else if (skill.passion == Passion.Major)
                    sb.Append("RimWorldAccess.Pawns.TopSkills.PassionMajor".Translate());

                sb.Append(" ");
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Gets work priorities information for the pawn.
        /// </summary>
        public static string GetWorkInfo(Pawn pawn)
        {
            if (pawn == null)
                return "RimWorldAccess.Pawns.Info.NoPawnSelected".Translate();

            if (pawn.workSettings == null)
                return "RimWorldAccess.Pawns.WorkInfo.NoSettings".Translate(pawn.LabelShort);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("RimWorldAccess.Pawns.WorkInfo.Header".Translate(pawn.LabelShort));

            var workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            var enabledWork = new List<string>();
            var disabledWork = new List<string>();

            foreach (var workType in workTypes)
            {
                if (workType.visible)
                {
                    if (pawn.workSettings.WorkIsActive(workType))
                    {
                        int priority = pawn.workSettings.GetPriority(workType);
                        string entry = priority > 0
                            ? "RimWorldAccess.Pawns.WorkInfo.LinePriority".Translate(workType.labelShort, priority).ToString()
                            : "RimWorldAccess.Pawns.WorkInfo.LineEnabled".Translate(workType.labelShort).ToString();
                        enabledWork.Add(entry);
                    }
                    else if (!pawn.WorkTypeIsDisabled(workType))
                    {
                        disabledWork.Add("RimWorldAccess.Pawns.WorkInfo.LineDisabled".Translate(workType.labelShort));
                    }
                }
            }

            if (enabledWork.Count > 0)
            {
                sb.AppendLine("RimWorldAccess.Pawns.WorkInfo.EnabledHeader".Translate(enabledWork.Count));
                foreach (var work in enabledWork)
                {
                    sb.AppendLine(work);
                }
            }

            if (disabledWork.Count > 0 && disabledWork.Count <= 10)
            {
                sb.AppendLine("RimWorldAccess.Pawns.WorkInfo.DisabledHeader".Translate(disabledWork.Count));
                foreach (var work in disabledWork)
                {
                    sb.AppendLine(work);
                }
            }

            if (enabledWork.Count == 0)
            {
                sb.AppendLine("RimWorldAccess.Pawns.WorkInfo.NoWork".Translate());
            }

            return sb.ToString().TrimEnd();
        }
    }
}
