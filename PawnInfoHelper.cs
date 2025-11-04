using System;
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
        /// Gets detailed health information for the pawn.
        /// Includes injuries, diseases, bleeding, pain, and capacities.
        /// </summary>
        public static string GetHealthInfo(Pawn pawn)
        {
            if (pawn == null)
                return "No pawn selected";

            if (pawn.health == null)
                return $"{pawn.LabelShort}: No health tracker";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{pawn.LabelShort} Health:");

            // Overall health state
            sb.AppendLine($"State: {pawn.health.State}");

            // Hediffs (injuries, diseases, conditions)
            var hediffs = pawn.health.hediffSet.hediffs;
            if (hediffs != null && hediffs.Count > 0)
            {
                sb.AppendLine($"\nConditions ({hediffs.Count}):");
                foreach (var hediff in hediffs)
                {
                    if (hediff.Visible)
                    {
                        string bodyPart = hediff.Part != null ? $" on {hediff.Part.Label}" : "";
                        sb.Append($"  - {hediff.LabelCap}{bodyPart}");

                        // Get description
                        string description = hediff.Description;
                        if (!string.IsNullOrEmpty(description))
                        {
                            description = description.StripTags().Trim();
                            sb.Append($": {description}");
                        }

                        // Get capacity modifiers directly
                        var capMods = hediff.CapMods;
                        if (capMods != null && capMods.Any())
                        {
                            var effects = new List<string>();
                            foreach (var capMod in capMods)
                            {
                                if (capMod.capacity != null)
                                {
                                    string effect = capMod.capacity.LabelCap;

                                    // Add the modifier details
                                    if (capMod.offset != 0)
                                    {
                                        effect += $" {capMod.offset:+0.##;-0.##}";
                                    }
                                    if (capMod.postFactor != 1f)
                                    {
                                        effect += $" x{capMod.postFactor:0.##}";
                                    }
                                    if (capMod.SetMaxDefined)
                                    {
                                        float setMax = capMod.EvaluateSetMax(pawn);
                                        effect += $" (max {setMax:P0})";
                                    }

                                    effects.Add(effect);
                                }
                            }

                            if (effects.Any())
                            {
                                sb.Append(": " + string.Join(", ", effects));
                            }
                        }

                        sb.AppendLine();
                    }
                }
            }
            else
            {
                sb.AppendLine("\nNo injuries or conditions");
            }

            // Bleeding
            if (pawn.health.hediffSet.BleedRateTotal > 0.01f)
            {
                sb.AppendLine($"\nBLEEDING: {pawn.health.hediffSet.BleedRateTotal:F2} per day");
            }

            // Pain level
            float painTotal = pawn.health.hediffSet.PainTotal;
            if (painTotal > 0.01f)
            {
                sb.AppendLine($"Pain: {painTotal:P0}");
            }

            // Key capacities
            if (pawn.health.capacities != null)
            {
                sb.AppendLine("\nCapacities:");
                var keyCapacities = new[]
                {
                    PawnCapacityDefOf.Consciousness,
                    PawnCapacityDefOf.Moving,
                    PawnCapacityDefOf.Manipulation,
                    PawnCapacityDefOf.Sight,
                    PawnCapacityDefOf.Hearing,
                    PawnCapacityDefOf.Talking
                };

                foreach (var capacity in keyCapacities)
                {
                    if (capacity != null && pawn.health.capacities.CapableOf(capacity))
                    {
                        float level = pawn.health.capacities.GetLevel(capacity);
                        string status = level < 1f ? $" ({level:P0})" : " (100%)";
                        sb.AppendLine($"  - {capacity.LabelCap}: {status}");
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Gets needs information for the pawn.
        /// Lists all needs with their current percentages.
        /// </summary>
        public static string GetNeedsInfo(Pawn pawn)
        {
            if (pawn == null)
                return "No pawn selected";

            if (pawn.needs == null)
                return $"{pawn.LabelShort}: No needs tracker";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{pawn.LabelShort} Needs:");

            var needs = pawn.needs.AllNeeds;
            if (needs != null && needs.Count > 0)
            {
                foreach (var need in needs)
                {
                    if (need.def.showOnNeedList)
                    {
                        float percentage = need.CurLevelPercentage * 100f;
                        string arrow = "";
                        if (need.GUIChangeArrow == 1)
                            arrow = " ↑";
                        else if (need.GUIChangeArrow == -1)
                            arrow = " ↓";

                        sb.AppendLine($"  {need.LabelCap}: {percentage:F0}%{arrow}");
                    }
                }
            }
            else
            {
                sb.AppendLine("No needs to display");
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Gets gear information for the pawn.
        /// Includes equipment, apparel, and inventory items.
        /// </summary>
        public static string GetGearInfo(Pawn pawn)
        {
            if (pawn == null)
                return "No pawn selected";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{pawn.LabelShort} Gear:");

            // Equipment (weapons)
            if (pawn.equipment != null && pawn.equipment.Primary != null)
            {
                sb.AppendLine($"\nEquipment:");
                sb.AppendLine($"  - {pawn.equipment.Primary.LabelCap}");
            }

            // Apparel
            if (pawn.apparel != null && pawn.apparel.WornApparel != null && pawn.apparel.WornApparel.Count > 0)
            {
                sb.AppendLine($"\nApparel ({pawn.apparel.WornApparel.Count}):");
                foreach (var apparel in pawn.apparel.WornApparel)
                {
                    sb.AppendLine($"  - {apparel.LabelCap}");
                }
            }

            // Inventory
            if (pawn.inventory != null && pawn.inventory.innerContainer != null && pawn.inventory.innerContainer.Count > 0)
            {
                sb.AppendLine($"\nInventory ({pawn.inventory.innerContainer.Count} items):");
                var groupedItems = pawn.inventory.innerContainer.GroupBy(t => t.def);
                foreach (var group in groupedItems)
                {
                    int count = group.Sum(t => t.stackCount);
                    if (count > 1)
                    {
                        sb.AppendLine($"  - {group.Key.label} x{count}");
                    }
                    else
                    {
                        sb.AppendLine($"  - {group.First().LabelCap}");
                    }
                }
            }

            if (sb.Length == $"{pawn.LabelShort} Gear:\n".Length)
            {
                sb.AppendLine("No equipment, apparel, or inventory items");
            }

            return sb.ToString().TrimEnd();
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
                        sb.AppendLine($"  - {rel.def.LabelCap}: {rel.otherPawn.LabelShort}");
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
