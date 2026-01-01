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
        /// Checks if a hediff is a missing part caused by surgical addition (bionic).
        /// These clutter the display since they're just side effects of having bionics.
        /// </summary>
        private static bool IsSurgicallyRemovedPart(Hediff hediff, Pawn pawn)
        {
            // Only filter Hediff_MissingPart
            if (!(hediff is Hediff_MissingPart))
                return false;

            // Filter if the parent part has a bionic/added part
            if (hediff.Part != null && pawn.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(hediff.Part))
                return true;

            return false;
        }

        /// <summary>
        /// Checks if a hediff is drug tolerance or dependency (not useful during combat).
        /// </summary>
        private static bool IsDrugToleranceOrDependency(Hediff hediff)
        {
            // Filter out addiction hediffs
            if (hediff is Hediff_Addiction)
                return true;

            // Filter out tolerance hediffs (usually have "tolerance" in the def name)
            if (hediff.def.defName.ToLower().Contains("tolerance"))
                return true;

            return false;
        }

        /// <summary>
        /// Gets health summary for the pawn.
        /// Shows critical info first (bleeding, blood loss, pain), then injured parts and capacities.
        /// </summary>
        public static string GetHealthInfo(Pawn pawn)
        {
            if (pawn == null)
                return "No pawn selected";

            if (pawn.health == null)
                return $"{pawn.LabelShort}: No health tracker";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{pawn.LabelShort}'s Health.");

            // Overall health state
            sb.AppendLine($"State: {pawn.health.State}.");

            // Critical info first - bleeding, blood loss, pain
            if (pawn.health.hediffSet.BleedRateTotal > 0.01f)
            {
                sb.AppendLine($"BLEEDING: {pawn.health.hediffSet.BleedRateTotal:F2} per day.");
            }

            var bloodLoss = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
            if (bloodLoss != null)
            {
                sb.AppendLine($"Blood Loss: {bloodLoss.Severity:P0}.");
            }

            float painTotal = pawn.health.hediffSet.PainTotal;
            if (painTotal > 0.01f)
            {
                sb.AppendLine($"Pain: {painTotal:P0}.");
            }

            // Key capacities first - consciousness, moving, manipulation (most important for combat)
            if (pawn.health.capacities != null)
            {
                var keyCapacities = new[]
                {
                    PawnCapacityDefOf.Consciousness,
                    PawnCapacityDefOf.Moving,
                    PawnCapacityDefOf.Manipulation
                };

                foreach (var cap in keyCapacities)
                {
                    if (cap != null && pawn.health.capacities.CapableOf(cap))
                    {
                        float level = pawn.health.capacities.GetLevel(cap);
                        string status = level < 1f ? $"{level:P0}" : "100%";
                        sb.AppendLine($"{cap.LabelCap}: {status}.");
                    }
                }
            }

            // Injured body parts - filter out surgically removed parts (from bionics)
            var hediffs = pawn.health.hediffSet.hediffs;
            if (hediffs != null && hediffs.Count > 0)
            {
                var visibleHediffs = hediffs
                    .Where(h => h.Visible)
                    .Where(h => !IsSurgicallyRemovedPart(h, pawn))
                    .Where(h => !IsDrugToleranceOrDependency(h))
                    .ToList();

                if (visibleHediffs.Count > 0)
                {
                    // Get injured body parts with their health
                    var injuredParts = visibleHediffs
                        .Where(h => h.Part != null)
                        .Select(h => h.Part)
                        .Distinct()
                        .Select(part => new {
                            Part = part,
                            Health = pawn.health.hediffSet.GetPartHealth(part),
                            MaxHealth = part.def.GetMaxHealth(pawn)
                        })
                        .OrderBy(p => p.Health / p.MaxHealth) // Most damaged first
                        .ToList();

                    // Get whole-body conditions (excluding drug stuff)
                    var wholeBodyConditions = visibleHediffs
                        .Where(h => h.Part == null)
                        .ToList();

                    if (injuredParts.Count > 0)
                    {
                        sb.AppendLine($"\nInjured Parts.");

                        foreach (var part in injuredParts)
                        {
                            float healthPercent = part.Health / part.MaxHealth * 100f;
                            sb.AppendLine($"  {part.Part.LabelCap}: {healthPercent:F0}%.");
                        }
                    }

                    if (wholeBodyConditions.Count > 0)
                    {
                        sb.AppendLine($"\nConditions: {wholeBodyConditions.Count}.");
                    }
                }
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
                }
            }
            else
            {
                sb.AppendLine("No needs to display.");
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
