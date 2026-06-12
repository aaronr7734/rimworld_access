using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RimWorld;
using Verse;
using Verse.Grammar;

namespace RimWorldAccess
{
    public enum FilterItemType
    {
        SectionHeader,
        Skill,
        PassionMin,
        PassionMax,
        SkillPointsMin,
        SkillPointsMax,
        CountOnlyHighestAttack,
        CountOnlyPassionSkills,
        TraitEntry,
        RequiredTraitsInPool,
        AddRequiredTrait,
        AddExcludedTrait,
        AddOptionalTrait,
        AgeMin,
        AgeMax,
        Gender,
        Health,
        Work,
        RerollLimit,
        SavePreset,
        LoadPreset,
        ClearAll
    }

    public class FilterMenuItem
    {
        public string Label { get; set; }
        public FilterItemType ItemType { get; set; }
        public SkillFilter SkillFilter { get; set; }
        public TraitFilter TraitFilter { get; set; }
        public bool IsSectionHeader => ItemType == FilterItemType.SectionHeader;
    }

    public static class PawnFilterHelper
    {
        private static readonly int[] RerollLimitSteps = { 100, 250, 500, 1000, 2500, 5000, 10000, 50000 };

        public static List<FilterMenuItem> BuildMenuItems(PawnFilter filter)
        {
            var items = new List<FilterMenuItem>();

            // Skills section
            items.Add(new FilterMenuItem
            {
                Label = "Skills".Translate(),
                ItemType = FilterItemType.SectionHeader
            });

            foreach (var skill in filter.Skills)
            {
                items.Add(new FilterMenuItem
                {
                    Label = FormatSkillLabel(skill),
                    ItemType = FilterItemType.Skill,
                    SkillFilter = skill
                });
            }

            // Passion and skill point aggregate filters (still in Skills section)
            items.Add(new FilterMenuItem
            {
                Label = FormatPassionMinLabel(filter),
                ItemType = FilterItemType.PassionMin
            });

            items.Add(new FilterMenuItem
            {
                Label = FormatPassionMaxLabel(filter),
                ItemType = FilterItemType.PassionMax
            });

            items.Add(new FilterMenuItem
            {
                Label = FormatSkillPointsMinLabel(filter),
                ItemType = FilterItemType.SkillPointsMin
            });

            items.Add(new FilterMenuItem
            {
                Label = FormatSkillPointsMaxLabel(filter),
                ItemType = FilterItemType.SkillPointsMax
            });

            items.Add(new FilterMenuItem
            {
                Label = FormatCountOnlyHighestAttackLabel(filter),
                ItemType = FilterItemType.CountOnlyHighestAttack
            });

            items.Add(new FilterMenuItem
            {
                Label = FormatCountOnlyPassionSkillsLabel(filter),
                ItemType = FilterItemType.CountOnlyPassionSkills
            });

            // Traits section
            items.Add(new FilterMenuItem
            {
                Label = "Traits".Translate(),
                ItemType = FilterItemType.SectionHeader
            });

            foreach (var trait in filter.Traits)
            {
                string modeLabel = GetTraitModeLabel(trait.Mode);
                string chanceText = GetTraitRollChanceText(trait.Def);
                items.Add(new FilterMenuItem
                {
                    Label = "RimWorldAccess.PawnFilter.TraitEntryFormat".Translate(modeLabel, trait.Label, chanceText),
                    ItemType = FilterItemType.TraitEntry,
                    TraitFilter = trait
                });
            }

            // Show "Required traits from pool" when optional traits exist
            if (filter.Traits.Any(t => t.Mode == TraitFilterMode.Optional))
            {
                items.Add(new FilterMenuItem
                {
                    Label = FormatRequiredTraitsInPoolLabel(filter),
                    ItemType = FilterItemType.RequiredTraitsInPool
                });
            }

            items.Add(new FilterMenuItem
            {
                Label = "RimWorldAccess.PawnFilter.AddRequiredTrait".Translate(),
                ItemType = FilterItemType.AddRequiredTrait
            });

            items.Add(new FilterMenuItem
            {
                Label = "RimWorldAccess.PawnFilter.AddExcludedTrait".Translate(),
                ItemType = FilterItemType.AddExcludedTrait
            });

            items.Add(new FilterMenuItem
            {
                Label = "RimWorldAccess.PawnFilter.AddOptionalTrait".Translate(),
                ItemType = FilterItemType.AddOptionalTrait
            });

            // Demographics section
            items.Add(new FilterMenuItem
            {
                Label = "RimWorldAccess.PawnFilter.SectionDemographics".Translate(),
                ItemType = FilterItemType.SectionHeader
            });

            items.Add(new FilterMenuItem
            {
                Label = FormatAgeMinLabel(filter),
                ItemType = FilterItemType.AgeMin
            });

            items.Add(new FilterMenuItem
            {
                Label = FormatAgeMaxLabel(filter),
                ItemType = FilterItemType.AgeMax
            });

            items.Add(new FilterMenuItem
            {
                Label = FormatGenderLabel(filter),
                ItemType = FilterItemType.Gender
            });

            // Conditions section
            items.Add(new FilterMenuItem
            {
                Label = "RimWorldAccess.PawnFilter.SectionConditions".Translate(),
                ItemType = FilterItemType.SectionHeader
            });

            items.Add(new FilterMenuItem
            {
                Label = FormatHealthLabel(filter),
                ItemType = FilterItemType.Health
            });

            items.Add(new FilterMenuItem
            {
                Label = FormatWorkLabel(filter),
                ItemType = FilterItemType.Work
            });

            // Settings section
            items.Add(new FilterMenuItem
            {
                Label = "RimWorldAccess.PawnFilter.SectionSettings".Translate(),
                ItemType = FilterItemType.SectionHeader
            });

            items.Add(new FilterMenuItem
            {
                Label = FormatRerollLimitLabel(filter),
                ItemType = FilterItemType.RerollLimit
            });

            // Actions section
            items.Add(new FilterMenuItem
            {
                Label = "RimWorldAccess.PawnFilter.SectionActions".Translate(),
                ItemType = FilterItemType.SectionHeader
            });

            items.Add(new FilterMenuItem
            {
                Label = "RimWorldAccess.PawnFilter.SavePreset".Translate(),
                ItemType = FilterItemType.SavePreset
            });

            items.Add(new FilterMenuItem
            {
                Label = "RimWorldAccess.PawnFilter.LoadPreset".Translate(),
                ItemType = FilterItemType.LoadPreset
            });

            items.Add(new FilterMenuItem
            {
                Label = "ClearAll".Translate(),
                ItemType = FilterItemType.ClearAll
            });

            return items;
        }

        public static string GetTraitModeLabel(TraitFilterMode mode)
        {
            switch (mode)
            {
                case TraitFilterMode.Required: return "Required".Translate();
                case TraitFilterMode.Excluded: return "RimWorldAccess.PawnFilter.TraitExcluded".Translate();
                case TraitFilterMode.Optional: return "Optional".Translate();
                default: return mode.ToString();
            }
        }

        public static string GetTraitRollChanceText(TraitDef traitDef)
        {
            var allTraits = DefDatabase<TraitDef>.AllDefsListForReading;
            float totalMale = 0f;
            float totalFemale = 0f;
            foreach (var td in allTraits)
            {
                totalMale += td.GetGenderSpecificCommonality(Verse.Gender.Male);
                totalFemale += td.GetGenderSpecificCommonality(Verse.Gender.Female);
            }

            if (totalMale <= 0f || totalFemale <= 0f) return "";

            float malePct = traitDef.GetGenderSpecificCommonality(Verse.Gender.Male) / totalMale * 100f;
            float femalePct = traitDef.GetGenderSpecificCommonality(Verse.Gender.Female) / totalFemale * 100f;

            if (Math.Abs(malePct - femalePct) < 0.05f)
                return $"({malePct:F1}%)";
            return $"({"Male".Translate()}: {malePct:F1}%, {"Female".Translate()}: {femalePct:F1}%)";
        }

        public static string FormatSkillLabel(SkillFilter skill)
        {
            string skillName = skill.Skill.skillLabel.CapitalizeFirst();

            if (skill.MinLevel <= 0 && skill.MinPassion == Passion.None)
                return (string)"RimWorldAccess.PawnFilter.SkillLabelAny".Translate(skillName);

            var parts = new List<string>();
            if (skill.MinLevel > 0)
                parts.Add($"{"RimWorldAccess.PawnFilter.Minimum".Translate()} {skill.MinLevel}");

            if (skill.MinPassion != Passion.None)
            {
                string passionLabel = skill.MinPassion == Passion.Minor
                    ? "PassionMinor".Translate()
                    : "PassionMajor".Translate();
                parts.Add(passionLabel);
            }

            return (string)"RimWorldAccess.PawnFilter.SkillLabelWithRequirements".Translate(skillName, string.Join(", ", parts));
        }

        public static string FormatPassionMinLabel(PawnFilter filter)
        {
            string value = filter.PassionMin <= 0
                ? (string)"RimWorldAccess.PawnFilter.Any".Translate()
                : filter.PassionMin.ToString();
            return (string)"RimWorldAccess.PawnFilter.TotalPassionsWithValue".Translate("RimWorldAccess.PawnFilter.Minimum".Translate(), value);
        }

        public static string FormatPassionMaxLabel(PawnFilter filter)
        {
            string value = filter.PassionMax >= 12
                ? (string)"RimWorldAccess.PawnFilter.Any".Translate()
                : filter.PassionMax.ToString();
            return (string)"RimWorldAccess.PawnFilter.TotalPassionsWithValue".Translate("RimWorldAccess.PawnFilter.Maximum".Translate(), value);
        }

        public static string FormatSkillPointsMinLabel(PawnFilter filter)
        {
            string value = filter.SkillPointsMin <= 0
                ? (string)"RimWorldAccess.PawnFilter.Any".Translate()
                : filter.SkillPointsMin.ToString();
            return (string)"RimWorldAccess.PawnFilter.TotalSkillPointsWithValue".Translate("RimWorldAccess.PawnFilter.Minimum".Translate(), value);
        }

        public static string FormatSkillPointsMaxLabel(PawnFilter filter)
        {
            string value = filter.SkillPointsMax >= 240
                ? (string)"RimWorldAccess.PawnFilter.Any".Translate()
                : filter.SkillPointsMax.ToString();
            return (string)"RimWorldAccess.PawnFilter.TotalSkillPointsWithValue".Translate("RimWorldAccess.PawnFilter.Maximum".Translate(), value);
        }

        public static string FormatCountOnlyHighestAttackLabel(PawnFilter filter)
        {
            string state = filter.CountOnlyHighestAttack ? "On".Translate() : "Off".Translate();
            return (string)"RimWorldAccess.PawnFilter.CountOnlyHighestAttack".Translate(state);
        }

        public static string FormatCountOnlyPassionSkillsLabel(PawnFilter filter)
        {
            string state = filter.CountOnlyPassionSkills ? "On".Translate() : "Off".Translate();
            return (string)"RimWorldAccess.PawnFilter.CountOnlyPassionSkills".Translate(state);
        }

        public static string FormatRequiredTraitsInPoolLabel(PawnFilter filter)
        {
            return (string)"RimWorldAccess.PawnFilter.RequiredTraitsInPool".Translate(filter.RequiredTraitsInPool);
        }

        public static string FormatAgeMinLabel(PawnFilter filter)
        {
            string value = filter.AgeMin <= 0
                ? (string)"RimWorldAccess.PawnFilter.Any".Translate()
                : filter.AgeMin.ToString();
            return (string)"RimWorldAccess.PawnFilter.AgeWithValue".Translate("RimWorldAccess.PawnFilter.Minimum".Translate(), value);
        }

        public static string FormatAgeMaxLabel(PawnFilter filter)
        {
            string value = filter.AgeMax >= 120
                ? (string)"RimWorldAccess.PawnFilter.Any".Translate()
                : filter.AgeMax.ToString();
            return (string)"RimWorldAccess.PawnFilter.AgeWithValue".Translate("RimWorldAccess.PawnFilter.Maximum".Translate(), value);
        }

        public static string FormatGenderLabel(PawnFilter filter)
        {
            string value;
            if (!filter.Gender.HasValue)
                value = (string)"RimWorldAccess.PawnFilter.Any".Translate();
            else if (filter.Gender.Value == Verse.Gender.Male)
                value = "Male".Translate();
            else
                value = "Female".Translate();

            return "Gender".Translate() + ": " + value;
        }

        public static string FormatHealthLabel(PawnFilter filter)
        {
            string value;
            switch (filter.Health)
            {
                case HealthFilterMode.AllowAll: value = "AllowAll".Translate(); break;
                case HealthFilterMode.OnlyStartCondition: value = (string)"RimWorldAccess.PawnFilter.HealthOnlyStartConditions".Translate(); break;
                case HealthFilterMode.NoPain: value = (string)"RimWorldAccess.PawnFilter.HealthNoPain".Translate(); break;
                case HealthFilterMode.NoAddiction: value = (string)"RimWorldAccess.PawnFilter.HealthNoAddictions".Translate(); break;
                case HealthFilterMode.AllowNone: value = (string)"RimWorldAccess.PawnFilter.HealthNoConditions".Translate(); break;
                default: value = "AllowAll".Translate(); break;
            }
            return "Health".Translate() + ": " + value;
        }

        public static string FormatWorkLabel(PawnFilter filter)
        {
            string value;
            switch (filter.Work)
            {
                case WorkFilterMode.AllowAll: value = "AllowAll".Translate(); break;
                case WorkFilterMode.NoDumbLabor: value = (string)"RimWorldAccess.PawnFilter.WorkNoDumbLabor".Translate(); break;
                case WorkFilterMode.AllowNone: value = "RimWorldAccess.PawnFilter.WorkAllowNone".Translate(); break;
                default: value = "AllowAll".Translate(); break;
            }
            return "IncapableOf".Translate() + ": " + value;
        }

        public static string FormatRerollLimitLabel(PawnFilter filter)
        {
            return (string)"RimWorldAccess.PawnFilter.RerollLimit".Translate(filter.RerollLimit);
        }

        public static List<FloatMenuOption> BuildTraitPickerOptions(
            PawnFilter filter, TraitFilterMode mode, Action onTraitAdded)
        {
            var options = new List<FloatMenuOption>();
            var existingTraits = new HashSet<string>();

            foreach (var existing in filter.Traits)
                existingTraits.Add(existing.Def.defName + "_" + existing.Degree);

            // Collect required trait defs for conflict checking
            var requiredTraitDefs = filter.Traits
                .Where(t => t.Mode == TraitFilterMode.Required)
                .Select(t => t.Def)
                .ToList();

            var allTraits = DefDatabase<TraitDef>.AllDefsListForReading;

            foreach (var traitDef in allTraits.OrderBy(t => t.defName))
            {
                if (traitDef.degreeDatas == null) continue;

                // Check conflicts with existing required traits (both explicit conflicts
                // and same-TraitDef different-degree conflicts — a pawn can only have one degree)
                bool conflictsWithRequired = requiredTraitDefs.Any(req =>
                    req.ConflictsWith(traitDef) || req == traitDef);
                if (conflictsWithRequired && mode != TraitFilterMode.Excluded)
                    continue;

                foreach (var degree in traitDef.degreeDatas)
                {
                    string key = traitDef.defName + "_" + degree.degree;
                    if (existingTraits.Contains(key))
                        continue;

                    var trait = new Trait(traitDef, degree.degree);
                    string label = trait.LabelCap + " " + GetTraitRollChanceText(traitDef);

                    string desc = degree.description;
                    if (!string.IsNullOrEmpty(desc))
                    {
                        // Convert {PAWN_*} to [PAWN_*] so GrammarResolver handles both formats
                        desc = Regex.Replace(desc, @"\{(PAWN_\w+)\}", "[$1]");

                        // Resolve using game's grammar system with generic female colonist
                        var request = default(GrammarRequest);
                        request.Includes.Add(RulePackDefOf.DynamicWrapper);
                        request.Rules.Add(new Rule_String("RULE", desc));
                        request.Rules.AddRange(GrammarUtility.RulesForPawn(
                            "PAWN", null, null, PawnKindDefOf.Colonist, Verse.Gender.Female,
                            null, 25, 25, "", false, false, false, null, false, "",
                            null, false));
                        desc = GrammarResolver.Resolve("r_root", request);
                    }

                    var option = new FloatMenuOption(label, () =>
                    {
                        filter.Traits.Add(new TraitFilter
                        {
                            Def = traitDef,
                            Degree = degree.degree,
                            Mode = mode
                        });
                        onTraitAdded?.Invoke();
                    });

                    if (!string.IsNullOrEmpty(desc))
                        option.tooltip = new TipSignal(desc);

                    options.Add(option);
                }
            }

            return options;
        }

        public static void AdjustRerollLimit(PawnFilter filter, int direction)
        {
            int currentIndex = -1;
            for (int i = 0; i < RerollLimitSteps.Length; i++)
            {
                if (RerollLimitSteps[i] == filter.RerollLimit)
                {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                // Not on a standard step — snap to nearest
                currentIndex = 2; // default to 500
            }

            int newIndex = currentIndex + direction;
            if (newIndex < 0) newIndex = 0;
            if (newIndex >= RerollLimitSteps.Length) newIndex = RerollLimitSteps.Length - 1;

            filter.RerollLimit = RerollLimitSteps[newIndex];
        }

        public static void CycleGender(PawnFilter filter, int direction)
        {
            // Cycle: null (Any) → Male → Female → null
            if (!filter.Gender.HasValue)
                filter.Gender = direction > 0 ? Verse.Gender.Male : Verse.Gender.Female;
            else if (filter.Gender.Value == Verse.Gender.Male)
                filter.Gender = direction > 0 ? Verse.Gender.Female : (Gender?)null;
            else // Female
                filter.Gender = direction > 0 ? (Gender?)null : Verse.Gender.Male;
        }

        public static void CycleHealth(PawnFilter filter, int direction)
        {
            var values = (HealthFilterMode[])Enum.GetValues(typeof(HealthFilterMode));
            int idx = Array.IndexOf(values, filter.Health);
            idx = (idx + direction + values.Length) % values.Length;
            filter.Health = values[idx];
        }

        public static void CycleWork(PawnFilter filter, int direction)
        {
            var values = (WorkFilterMode[])Enum.GetValues(typeof(WorkFilterMode));
            int idx = Array.IndexOf(values, filter.Work);
            idx = (idx + direction + values.Length) % values.Length;
            filter.Work = values[idx];
        }

        public static void CyclePassion(SkillFilter skill)
        {
            switch (skill.MinPassion)
            {
                case Passion.None:
                    skill.MinPassion = Passion.Minor;
                    break;
                case Passion.Minor:
                    skill.MinPassion = Passion.Major;
                    break;
                case Passion.Major:
                    skill.MinPassion = Passion.None;
                    break;
            }
        }

        public static void AdjustSkillLevel(SkillFilter skill, int direction)
        {
            skill.MinLevel += direction;
            if (skill.MinLevel < 0) skill.MinLevel = 0;
            if (skill.MinLevel > 20) skill.MinLevel = 20;
        }

        public static void AdjustPassion(PawnFilter filter, bool isMin, int direction)
        {
            if (isMin)
            {
                filter.PassionMin += direction;
                if (filter.PassionMin < 0) filter.PassionMin = 0;
                if (filter.PassionMin > 12) filter.PassionMin = 12;
                if (filter.PassionMin > filter.PassionMax) filter.PassionMax = filter.PassionMin;
            }
            else
            {
                filter.PassionMax += direction;
                if (filter.PassionMax < 0) filter.PassionMax = 0;
                if (filter.PassionMax > 12) filter.PassionMax = 12;
                if (filter.PassionMax < filter.PassionMin) filter.PassionMin = filter.PassionMax;
            }
        }

        public static void AdjustSkillPoints(PawnFilter filter, bool isMin, int direction)
        {
            if (isMin)
            {
                filter.SkillPointsMin += direction;
                if (filter.SkillPointsMin < 0) filter.SkillPointsMin = 0;
                if (filter.SkillPointsMin > 240) filter.SkillPointsMin = 240;
                if (filter.SkillPointsMin > filter.SkillPointsMax) filter.SkillPointsMax = filter.SkillPointsMin;
            }
            else
            {
                filter.SkillPointsMax += direction;
                if (filter.SkillPointsMax < 0) filter.SkillPointsMax = 0;
                if (filter.SkillPointsMax > 240) filter.SkillPointsMax = 240;
                if (filter.SkillPointsMax < filter.SkillPointsMin) filter.SkillPointsMin = filter.SkillPointsMax;
            }
        }

        public static void AdjustAge(PawnFilter filter, bool isMin, int direction)
        {
            if (isMin)
            {
                filter.AgeMin += direction;
                if (filter.AgeMin < 0) filter.AgeMin = 0;
                if (filter.AgeMin > 120) filter.AgeMin = 120;
                if (filter.AgeMin > filter.AgeMax) filter.AgeMax = filter.AgeMin;
            }
            else
            {
                filter.AgeMax += direction;
                if (filter.AgeMax < 0) filter.AgeMax = 0;
                if (filter.AgeMax > 120) filter.AgeMax = 120;
                if (filter.AgeMax < filter.AgeMin) filter.AgeMin = filter.AgeMax;
            }
        }

        public static void AdjustRequiredTraitsInPool(PawnFilter filter, int direction)
        {
            int optionalCount = filter.Traits.Count(t => t.Mode == TraitFilterMode.Optional);
            int maxPool = Math.Min(3, optionalCount);

            filter.RequiredTraitsInPool += direction;
            if (filter.RequiredTraitsInPool < 0) filter.RequiredTraitsInPool = 0;
            if (filter.RequiredTraitsInPool > maxPool) filter.RequiredTraitsInPool = maxPool;
        }
    }
}
