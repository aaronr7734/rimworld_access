using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper class for character tab data extraction.
    /// Provides methods for backstories, traits, incapacities, and abilities.
    /// </summary>
    public static class CharacterTabHelper
    {
        /// <summary>
        /// Represents basic character information.
        /// </summary>
        public class BasicInfo
        {
            public string Name { get; set; }
            public int BiologicalAge { get; set; }
            public int ChronologicalAge { get; set; }
            public Gender Gender { get; set; }
            public string Race { get; set; }
            public string Faction { get; set; }
            public string Xenotype { get; set; }
            public string Ideology { get; set; }
            public string Role { get; set; }
        }

        /// <summary>
        /// Represents a backstory.
        /// </summary>
        public class BackstoryInfo
        {
            public BackstoryDef Def { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public string DetailedInfo { get; set; }
        }

        /// <summary>
        /// Represents a trait.
        /// </summary>
        public class TraitInfo
        {
            public Trait Trait { get; set; }
            public string Label { get; set; }
            public string DetailedInfo { get; set; }
        }

        /// <summary>
        /// Represents work incapacities.
        /// </summary>
        public class IncapacityInfo
        {
            public WorkTags Tag { get; set; }
            public string Label { get; set; }
            public string DetailedInfo { get; set; }
        }

        /// <summary>
        /// Represents an ability.
        /// </summary>
        public class AbilityInfo
        {
            public Ability Ability { get; set; }
            public string Label { get; set; }
            public string DetailedInfo { get; set; }
        }

        #region Basic Info

        /// <summary>
        /// Gets basic character information for a pawn.
        /// </summary>
        public static BasicInfo GetBasicInfo(Pawn pawn)
        {
            var info = new BasicInfo
            {
                Name = pawn.LabelShort.StripTags(),
                BiologicalAge = pawn.ageTracker?.AgeBiologicalYears ?? 0,
                ChronologicalAge = pawn.ageTracker?.AgeChronologicalYears ?? 0,
                Gender = pawn.gender,
                Race = pawn.def?.label ?? "Unknown",
                Faction = pawn.Faction?.Name ?? "None",
            };

            // Xenotype (if Biotech DLC)
            if (ModsConfig.BiotechActive && pawn.genes?.Xenotype != null)
            {
                info.Xenotype = pawn.genes.Xenotype.LabelCap;
            }

            // Ideology (if Ideology DLC)
            if (ModsConfig.IdeologyActive && pawn.Ideo != null)
            {
                info.Ideology = pawn.Ideo.name;

                // Role
                var role = pawn.Ideo.GetRole(pawn);
                if (role != null)
                {
                    info.Role = role.LabelCap;
                }
            }

            return info;
        }

        #endregion

        #region Backstories

        /// <summary>
        /// Gets backstory information for a pawn.
        /// </summary>
        public static List<BackstoryInfo> GetBackstories(Pawn pawn)
        {
            var backstories = new List<BackstoryInfo>();

            if (pawn?.story == null)
                return backstories;

            // Childhood backstory
            if (pawn.story.Childhood != null)
            {
                backstories.Add(new BackstoryInfo
                {
                    Def = pawn.story.Childhood,
                    Title = $"Childhood: {pawn.story.Childhood.TitleCapFor(pawn.gender)}",
                    Description = pawn.story.Childhood.FullDescriptionFor(pawn),
                    DetailedInfo = GetBackstoryDetailedInfo(pawn.story.Childhood, pawn)
                });
            }

            // Adulthood backstory
            if (pawn.story.Adulthood != null)
            {
                backstories.Add(new BackstoryInfo
                {
                    Def = pawn.story.Adulthood,
                    Title = $"Adulthood: {pawn.story.Adulthood.TitleCapFor(pawn.gender)}",
                    Description = pawn.story.Adulthood.FullDescriptionFor(pawn),
                    DetailedInfo = GetBackstoryDetailedInfo(pawn.story.Adulthood, pawn)
                });
            }

            // Custom title
            if (!string.IsNullOrEmpty(pawn.story.title))
            {
                backstories.Add(new BackstoryInfo
                {
                    Title = $"Title: {pawn.story.title}",
                    Description = "Custom title",
                    DetailedInfo = $"Custom title: {pawn.story.title}"
                });
            }

            return backstories;
        }

        private static string GetBackstoryDetailedInfo(BackstoryDef backstory, Pawn pawn)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{backstory.TitleCapFor(pawn.gender)}:");
            sb.AppendLine();

            // Skill modifiers (show mechanical effects first)
            if (backstory.skillGains != null && backstory.skillGains.Count > 0)
            {
                sb.AppendLine("Skill modifiers:");
                foreach (var skillGain in backstory.skillGains)
                {
                    string sign = skillGain.amount >= 0 ? "+" : "";
                    sb.AppendLine($"  {skillGain.skill.skillLabel.CapitalizeFirst()}: {sign}{skillGain.amount}");
                }
                sb.AppendLine();
            }

            // Work restrictions
            if (backstory.workDisables != WorkTags.None)
            {
                sb.AppendLine("Incapable of:");
                var tags = GetWorkTagLabels(backstory.workDisables);
                foreach (var tag in tags)
                {
                    sb.AppendLine($"  {tag}");
                }
                sb.AppendLine();
            }

            // Full description (show after mechanical effects)
            sb.AppendLine(backstory.FullDescriptionFor(pawn));

            return sb.ToString().TrimEnd();
        }

        #endregion

        #region Traits

        /// <summary>
        /// Gets all traits for a pawn.
        /// </summary>
        public static List<TraitInfo> GetTraits(Pawn pawn)
        {
            var traits = new List<TraitInfo>();

            if (pawn?.story?.traits == null)
                return traits;

            var allTraits = pawn.story.traits.allTraits;
            if (allTraits == null)
                return traits;

            foreach (var trait in allTraits)
            {
                traits.Add(new TraitInfo
                {
                    Trait = trait,
                    Label = trait.LabelCap.StripTags(),
                    DetailedInfo = GetTraitDetailedInfo(trait)
                });
            }

            return traits;
        }

        private static string GetTraitDetailedInfo(Trait trait)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{trait.LabelCap.StripTags()}:");
            sb.AppendLine();

            // Description
            string desc = trait.TipString(null);
            if (!string.IsNullOrEmpty(desc))
            {
                sb.AppendLine(desc);
            }

            return sb.ToString().TrimEnd();
        }

        #endregion

        #region Incapacities

        /// <summary>
        /// Gets all work incapacities for a pawn.
        /// </summary>
        public static List<IncapacityInfo> GetIncapacities(Pawn pawn)
        {
            var incapacities = new List<IncapacityInfo>();

            if (pawn == null)
                return incapacities;

            WorkTags combined = pawn.CombinedDisabledWorkTags;
            if (combined == WorkTags.None)
                return incapacities;

            // Get individual work tags
            var tags = GetIndividualWorkTags(combined);

            foreach (var tag in tags)
            {
                incapacities.Add(new IncapacityInfo
                {
                    Tag = tag,
                    Label = GetWorkTagLabel(tag),
                    DetailedInfo = GetIncapacityDetailedInfo(pawn, tag)
                });
            }

            return incapacities;
        }

        private static List<WorkTags> GetIndividualWorkTags(WorkTags combined)
        {
            var tags = new List<WorkTags>();

            foreach (WorkTags tag in Enum.GetValues(typeof(WorkTags)))
            {
                if (tag == WorkTags.None || tag == WorkTags.AllWork)
                    continue;

                if ((combined & tag) == tag)
                {
                    tags.Add(tag);
                }
            }

            return tags;
        }

        private static string GetWorkTagLabel(WorkTags tag)
        {
            return tag.ToString().Replace("ManualDumb", "Dumb Labor")
                                 .Replace("ManualSkilled", "Skilled Labor");
        }

        private static List<string> GetWorkTagLabels(WorkTags tags)
        {
            var individualTags = GetIndividualWorkTags(tags);
            return individualTags.Select(t => GetWorkTagLabel(t)).ToList();
        }

        private static string GetIncapacityDetailedInfo(Pawn pawn, WorkTags tag)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Incapable of: {GetWorkTagLabel(tag)}");
            sb.AppendLine();

            sb.AppendLine("Causes:");

            // Check backstory
            if (pawn.story?.Childhood != null && (pawn.story.Childhood.workDisables & tag) == tag)
            {
                sb.AppendLine($"  Backstory: {pawn.story.Childhood.TitleCapFor(pawn.gender)}");
            }
            if (pawn.story?.Adulthood != null && (pawn.story.Adulthood.workDisables & tag) == tag)
            {
                sb.AppendLine($"  Backstory: {pawn.story.Adulthood.TitleCapFor(pawn.gender)}");
            }

            // Check traits
            if (pawn.story?.traits != null)
            {
                foreach (var trait in pawn.story.traits.allTraits)
                {
                    if ((trait.def.disabledWorkTags & tag) == tag)
                    {
                        sb.AppendLine($"  Trait: {trait.LabelCap.StripTags()}");
                    }
                }
            }

            // Check hediffs
            if (pawn.health?.hediffSet != null)
            {
                foreach (var hediff in pawn.health.hediffSet.hediffs)
                {
                    // Check current stage for disabled work tags
                    var stage = hediff.CurStage;
                    if (stage != null && (stage.disabledWorkTags & tag) == tag)
                    {
                        sb.AppendLine($"  Condition: {hediff.LabelCap.StripTags()}");
                    }
                }
            }

            // Check genes (if Biotech)
            if (ModsConfig.BiotechActive && pawn.genes != null)
            {
                foreach (var gene in pawn.genes.GenesListForReading)
                {
                    if ((gene.def.disabledWorkTags & tag) == tag)
                    {
                        sb.AppendLine($"  Gene: {gene.LabelCap}");
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("Affected work types:");

            // Get work types affected by this tag
            var workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            foreach (var workType in workTypes)
            {
                if ((workType.workTags & tag) == tag)
                {
                    sb.AppendLine($"  {workType.labelShort}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        #endregion

        #region Abilities

        /// <summary>
        /// Gets all abilities for a pawn.
        /// </summary>
        public static List<AbilityInfo> GetAbilities(Pawn pawn)
        {
            var abilities = new List<AbilityInfo>();

            if (pawn?.abilities == null)
                return abilities;

            var allAbilities = pawn.abilities.AllAbilitiesForReading;
            if (allAbilities == null)
                return abilities;

            foreach (var ability in allAbilities)
            {
                abilities.Add(new AbilityInfo
                {
                    Ability = ability,
                    Label = ability.def.LabelCap.ToString().StripTags(),
                    DetailedInfo = GetAbilityDetailedInfo(ability)
                });
            }

            return abilities;
        }

        private static string GetAbilityDetailedInfo(Ability ability)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{ability.def.LabelCap.ToString().StripTags()}:");
            sb.AppendLine();

            // Cooldown (show mechanical effects first)
            if (ability.def.cooldownTicksRange.max > 0)
            {
                float cooldownDays = ability.def.cooldownTicksRange.max / 60000f;
                sb.AppendLine($"Cooldown: {cooldownDays:F1} days");
            }

            // Current cooldown
            if (ability.CooldownTicksRemaining > 0)
            {
                float remainingDays = ability.CooldownTicksRemaining / 60000f;
                sb.AppendLine($"Ready in: {remainingDays:F1} days");
            }
            else
            {
                sb.AppendLine("Status: Ready");
            }

            // Description (show after mechanical effects)
            if (!string.IsNullOrEmpty(ability.def.description))
            {
                sb.AppendLine();
                sb.AppendLine(ability.def.description);
            }

            return sb.ToString().TrimEnd();
        }

        #endregion
    }
}
