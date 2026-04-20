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
                return "No pawn";

            string task = pawn.GetJobReport();
            if (string.IsNullOrEmpty(task))
            {
                return "Idle";
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
                return "No pawn selected";

            if (pawn.health == null)
                return $"{pawn.LabelShort}: No health tracker";

            StringBuilder sb = new StringBuilder();
            sb.Append($"{pawn.LabelShort}. ");
            bool hasHealthDetails = false;

            // Pain (uses vanilla translation keys and qualitative labels)
            string painLabel = HealthTabHelper.GetPainLabel(pawn);
            if (painLabel != null)
            {
                sb.Append($"{painLabel}. ");
                hasHealthDetails = true;
            }

            // Bleeding with time-to-death (uses vanilla translation keys)
            string bleedingLabel = HealthTabHelper.GetBleedingLabel(pawn);
            if (bleedingLabel != null)
            {
                sb.Append($"{bleedingLabel}. ");
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
                    sb.Append($"{cap.Label}: {cap.LevelLabel}. ");
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
                    sb.Append($"{p.Part.LabelCap}: {p.Health:F0}/{p.MaxHealth:F0}. ");
                    hasHealthDetails = true;
                }
            }

            // Whole-body hediffs — show all visible ones
            var wholeBodyHediffs = HealthTabHelper.GetVisibleHediffs(pawn)
                .Where(h => h.Part == null)
                .ToList();

            foreach (var hediff in wholeBodyHediffs)
            {
                sb.Append($"{hediff.LabelCap}. ");
                hasHealthDetails = true;
            }

            if (!hasHealthDetails)
            {
                sb.Append($"{ "Healthy".Translate()}.");
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
                return "No pawn selected";

            if (pawn.needs == null)
                return $"{pawn.LabelShort}: No needs tracker";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{pawn.LabelShort}'s Needs.");

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
                    sb.AppendLine($"  {need.LabelCap}: {percentage:F0}%.");

                    // After the Learning need, list active learning desires (Biotech children only)
                    if (need.def == NeedDefOf.Learning && pawn.learning?.ActiveLearningDesires != null
                        && pawn.learning.ActiveLearningDesires.Count > 0)
                    {
                        var desireLabels = pawn.learning.ActiveLearningDesires
                            .Select(d => d.LabelCap.ToString())
                            .ToArray();
                        sb.AppendLine($"    Learning desires: {string.Join(", ", desireLabels)}.");
                    }
                }
            }
            else
            {
                sb.AppendLine("No needs to display.");
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
                return "No pawn selected";

            if (pawn.needs?.mood == null)
                return $"{pawn.LabelShort}: No mood tracker";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{pawn.LabelShort}'s Mood.");

            Need_Mood mood = pawn.needs.mood;

            // Current mood level and description
            float moodPercentage = mood.CurLevelPercentage * 100f;
            string moodDescription = mood.MoodString;
            sb.AppendLine($"Mood: {moodPercentage:F0}% ({moodDescription}).");

            // Get thoughts affecting mood
            List<Thought> thoughtGroups = new List<Thought>();
            PawnNeedsUIUtility.GetThoughtGroupsInDisplayOrder(mood, thoughtGroups);

            if (thoughtGroups.Count > 0)
            {
                sb.AppendLine($"\nThoughts affecting mood. {thoughtGroups.Count} total.");

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
                        thoughtLabel = $"{thoughtLabel} x{thoughtGroup.Count}";
                    }

                    string offsetText = moodOffset.ToString("+0;-0;0");
                    sb.AppendLine($"  {thoughtLabel}: {offsetText}.");

                    thoughtGroup.Clear();
                }
            }
            else
            {
                sb.AppendLine("\nNo thoughts affecting mood.");
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
                return "No pawn selected";

            StringBuilder sb = new StringBuilder();
            sb.Append($"{pawn.LabelShort}'s Gear. ");

            // Weapon
            if (pawn.equipment != null && pawn.equipment.Primary != null)
            {
                var weapon = pawn.equipment.Primary;
                sb.Append($"Wielding: {weapon.LabelNoParenthesisCap.StripTags()}");
                var qualityComp = weapon.TryGetComp<CompQuality>();
                if (qualityComp != null)
                {
                    sb.Append($" ({qualityComp.Quality})");
                }
                sb.Append(". ");
            }
            else
            {
                sb.Append("Wielding: Nothing. ");
            }

            // Apparel
            if (pawn.apparel != null && pawn.apparel.WornApparel != null && pawn.apparel.WornApparel.Count > 0)
            {
                sb.Append("Wearing: ");
                var apparelList = new List<string>();
                foreach (var apparel in pawn.apparel.WornApparel)
                {
                    string apparelEntry = apparel.LabelNoParenthesisCap.StripTags();
                    var qualityComp = apparel.TryGetComp<CompQuality>();
                    if (qualityComp != null)
                    {
                        apparelEntry += $" ({qualityComp.Quality})";
                    }
                    apparelList.Add(apparelEntry);
                }
                sb.Append(string.Join(", ", apparelList));
                sb.Append(".");
            }
            else
            {
                sb.Append("Wearing: Nothing.");
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
                return "No pawn selected";

            if (pawn.relations == null)
                return $"{pawn.LabelShort}: No relations tracker";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{pawn.LabelShort} Social:");

            // Direct relations (family, lovers, etc.)
            var directRelations = pawn.relations.DirectRelations;
            if (directRelations != null && directRelations.Count > 0)
            {
                sb.AppendLine($"\nRelationships ({directRelations.Count}):");
                foreach (var rel in directRelations)
                {
                    if (rel.otherPawn != null)
                    {
                        // Use GetGenderSpecificLabelCap to get the correct label based on the other pawn's gender
                        string relationLabel = rel.def.GetGenderSpecificLabelCap(rel.otherPawn);
                        sb.AppendLine($"  - {relationLabel}: {rel.otherPawn.LabelShort}");
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
                            opinions.Add($"  - {otherPawn.LabelShort}: {opinion:+0;-0}");
                        }
                    }
                }

                if (opinions.Count > 0)
                {
                    sb.AppendLine($"\nOpinions from others ({opinions.Count}):");
                    foreach (var opinion in opinions.Take(10)) // Limit to 10 to avoid spam
                    {
                        sb.AppendLine(opinion);
                    }
                    if (opinions.Count > 10)
                    {
                        sb.AppendLine($"  ... and {opinions.Count - 10} more");
                    }
                }
            }

            if (directRelations == null || directRelations.Count == 0)
            {
                sb.AppendLine("No direct relationships");
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Gets training information for the pawn (mainly for animals).
        /// </summary>
        public static string GetTrainingInfo(Pawn pawn)
        {
            if (pawn == null)
                return "No pawn selected";

            if (pawn.training == null)
                return $"{pawn.LabelShort}: Not trainable";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{pawn.LabelShort} Training:");

            var trainableDefs = DefDatabase<TrainableDef>.AllDefsListForReading;
            var trainedSkills = new List<string>();
            var untrainedSkills = new List<string>();

            foreach (var trainable in trainableDefs)
            {
                if (pawn.training.CanAssignToTrain(trainable).Accepted)
                {
                    if (pawn.training.HasLearned(trainable))
                    {
                        trainedSkills.Add($"  - {trainable.LabelCap}: Learned");
                    }
                    else
                    {
                        // Training in progress - show without step count since GetSteps is not available
                        untrainedSkills.Add($"  - {trainable.LabelCap}: In progress");
                    }
                }
            }

            if (trainedSkills.Count > 0)
            {
                sb.AppendLine($"\nTrained ({trainedSkills.Count}):");
                foreach (var skill in trainedSkills)
                {
                    sb.AppendLine(skill);
                }
            }

            if (untrainedSkills.Count > 0)
            {
                sb.AppendLine($"\nIn Progress ({untrainedSkills.Count}):");
                foreach (var skill in untrainedSkills)
                {
                    sb.AppendLine(skill);
                }
            }

            if (trainedSkills.Count == 0 && untrainedSkills.Count == 0)
            {
                sb.AppendLine("No trainable skills");
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
                return "No pawn selected";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{pawn.LabelShort} Character:");

            // Basic info
            if (pawn.ageTracker != null)
            {
                sb.AppendLine($"Age: {pawn.ageTracker.AgeBiologicalYears} years");
            }

            // Traits
            if (pawn.story != null && pawn.story.traits != null)
            {
                var traits = pawn.story.traits.allTraits;
                if (traits != null && traits.Count > 0)
                {
                    sb.AppendLine($"\nTraits ({traits.Count}):");
                    foreach (var trait in traits)
                    {
                        sb.AppendLine($"  - {trait.LabelCap}");
                    }
                }
            }

            // Backstory
            if (pawn.story != null)
            {
                if (pawn.story.Childhood != null)
                {
                    sb.AppendLine($"\nChildhood: {pawn.story.Childhood.TitleCapFor(pawn.gender)}");
                }
                if (pawn.story.Adulthood != null)
                {
                    sb.AppendLine($"Adulthood: {pawn.story.Adulthood.TitleCapFor(pawn.gender)}");
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
                    sb.AppendLine($"\nTop Skills:");
                    foreach (var skill in topSkills)
                    {
                        string passion = "";
                        if (skill.passion == Passion.Minor)
                            passion = " (•)";
                        else if (skill.passion == Passion.Major)
                            passion = " (••)";

                        sb.AppendLine($"  - {skill.def.LabelCap}: {skill.Level}{passion}");
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
                return "No pawn selected";

            if (pawn.skills == null || pawn.skills.skills == null)
                return $"{pawn.LabelShort}: No skills";

            var topSkills = pawn.skills.skills
                .Where(s => !s.TotallyDisabled && s.Level > 0)
                .OrderByDescending(s => s.Level)
                .Take(3)
                .ToList();

            if (!topSkills.Any())
                return $"{pawn.LabelShort}: No skills";

            StringBuilder sb = new StringBuilder();
            sb.Append($"{pawn.LabelShort}'s Top Skills. ");

            foreach (var skill in topSkills)
            {
                sb.Append($"{skill.def.LabelCap}: {skill.Level}.");

                if (skill.passion == Passion.Minor)
                    sb.Append(" (passion.)");
                else if (skill.passion == Passion.Major)
                    sb.Append(" (double passion.)");

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
                return "No pawn selected";

            if (pawn.workSettings == null)
                return $"{pawn.LabelShort}: No work settings";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{pawn.LabelShort} Work:");

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
                        string priorityText = priority > 0 ? $" (Priority {priority})" : " (Enabled)";
                        enabledWork.Add($"  - {workType.labelShort}{priorityText}");
                    }
                    else if (!pawn.WorkTypeIsDisabled(workType))
                    {
                        disabledWork.Add($"  - {workType.labelShort}");
                    }
                }
            }

            if (enabledWork.Count > 0)
            {
                sb.AppendLine($"\nEnabled ({enabledWork.Count}):");
                foreach (var work in enabledWork)
                {
                    sb.AppendLine(work);
                }
            }

            if (disabledWork.Count > 0 && disabledWork.Count <= 10)
            {
                sb.AppendLine($"\nDisabled ({disabledWork.Count}):");
                foreach (var work in disabledWork)
                {
                    sb.AppendLine(work);
                }
            }

            if (enabledWork.Count == 0)
            {
                sb.AppendLine("No work types enabled");
            }

            return sb.ToString().TrimEnd();
        }
    }
}
