using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public enum TraitFilterMode
    {
        Required,
        Optional,
        Excluded
    }

    public enum HealthFilterMode
    {
        AllowAll,
        OnlyStartCondition,
        NoPain,
        NoAddiction,
        AllowNone
    }

    public enum WorkFilterMode
    {
        AllowAll,
        NoDumbLabor,
        AllowNone
    }

    public enum RerollAlgorithm
    {
        Normal,
        Fast
    }

    public class SkillFilter
    {
        public SkillDef Skill { get; set; }
        public int MinLevel { get; set; }
        public Passion MinPassion { get; set; }

        public bool IsActive => MinLevel > 0 || MinPassion != Passion.None;
    }

    public class TraitFilter
    {
        public TraitDef Def { get; set; }
        public int Degree { get; set; }
        public TraitFilterMode Mode { get; set; }

        public string Label => new Trait(Def, Degree).LabelCap;
    }

    public class PawnFilter
    {
        public List<SkillFilter> Skills { get; set; } = new List<SkillFilter>();
        public List<TraitFilter> Traits { get; set; } = new List<TraitFilter>();
        public int AgeMin { get; set; } = 0;
        public int AgeMax { get; set; } = 120;
        public Gender? Gender { get; set; } = null;
        public HealthFilterMode Health { get; set; } = HealthFilterMode.AllowAll;
        public WorkFilterMode Work { get; set; } = WorkFilterMode.AllowAll;
        public int PassionMin { get; set; } = 0;
        public int PassionMax { get; set; } = 12;
        public int SkillPointsMin { get; set; } = 0;
        public int SkillPointsMax { get; set; } = 240;
        public int RerollLimit { get; set; } = 500;
        public int RequiredTraitsInPool { get; set; } = 0;
        public bool CountOnlyHighestAttack { get; set; } = false;
        public bool CountOnlyPassionSkills { get; set; } = false;
        public RerollAlgorithm Algorithm { get; set; } = RerollAlgorithm.Fast;

        public void InitializeSkills()
        {
            Skills.Clear();
            foreach (var skillDef in DefDatabase<SkillDef>.AllDefsListForReading
                .OrderByDescending(s => s.listOrder))
            {
                Skills.Add(new SkillFilter
                {
                    Skill = skillDef,
                    MinLevel = 0,
                    MinPassion = Passion.None
                });
            }
        }

        public void Reset()
        {
            foreach (var skill in Skills)
            {
                skill.MinLevel = 0;
                skill.MinPassion = Passion.None;
            }
            Traits.Clear();
            PassionMin = 0;
            PassionMax = 12;
            SkillPointsMin = 0;
            SkillPointsMax = 240;
            AgeMin = 0;
            AgeMax = 120;
            Gender = null;
            Health = HealthFilterMode.AllowAll;
            Work = WorkFilterMode.AllowAll;
            RerollLimit = 500;
            RequiredTraitsInPool = 0;
            CountOnlyHighestAttack = false;
            CountOnlyPassionSkills = false;
            Algorithm = RerollAlgorithm.Fast;
        }

        public bool HasActiveFilters()
        {
            if (Skills.Any(s => s.IsActive))
                return true;
            if (PassionMin > 0 || PassionMax < 12)
                return true;
            if (SkillPointsMin > 0 || SkillPointsMax < 240)
                return true;
            if (CountOnlyHighestAttack || CountOnlyPassionSkills)
                return true;
            if (Traits.Count > 0)
                return true;
            if (RequiredTraitsInPool > 0)
                return true;
            if (AgeMin > 0 || AgeMax < 120)
                return true;
            if (Gender.HasValue)
                return true;
            if (Health != HealthFilterMode.AllowAll)
                return true;
            if (Work != WorkFilterMode.AllowAll)
                return true;
            return false;
        }

        public int GetActiveFilterCount()
        {
            int count = 0;
            count += Skills.Count(s => s.IsActive);
            if (PassionMin > 0 || PassionMax < 12) count++;
            if (SkillPointsMin > 0 || SkillPointsMax < 240) count++;
            if (CountOnlyHighestAttack) count++;
            if (CountOnlyPassionSkills) count++;
            count += Traits.Count;
            if (RequiredTraitsInPool > 0) count++;
            if (AgeMin > 0 || AgeMax < 120) count++;
            if (Gender.HasValue) count++;
            if (Health != HealthFilterMode.AllowAll) count++;
            if (Work != WorkFilterMode.AllowAll) count++;
            return count;
        }

        public bool Evaluate(Pawn pawn)
        {
            if (pawn == null) return false;

            // Baby pawns skip skill/trait checks
            bool isBaby = ModsConfig.BiotechActive && pawn.DevelopmentalStage.Baby();

            if (!isBaby)
            {
                if (!CheckSkills(pawn)) return false;
                if (!CheckPassionRange(pawn)) return false;
                if (!CheckSkillPoints(pawn)) return false;
                if (!CheckTraits(pawn)) return false;
            }

            if (!CheckAge(pawn)) return false;
            if (!CheckGender(pawn)) return false;
            if (!CheckHealth(pawn)) return false;
            if (!CheckWork(pawn)) return false;

            return true;
        }

        private bool CheckSkills(Pawn pawn)
        {
            if (pawn.skills == null) return true;

            foreach (var filter in Skills)
            {
                if (!filter.IsActive) continue;

                var skill = pawn.skills.GetSkill(filter.Skill);
                if (skill == null || skill.TotallyDisabled) return false;

                if (filter.MinLevel > 0 && skill.Level < filter.MinLevel)
                    return false;

                if (filter.MinPassion != Passion.None && (int)skill.passion < (int)filter.MinPassion)
                    return false;
            }

            return true;
        }

        private bool CheckPassionRange(Pawn pawn)
        {
            if (PassionMin <= 0 && PassionMax >= 12) return true;
            if (pawn.skills == null) return true;

            int totalPassions = pawn.skills.skills
                .Count(s => !s.TotallyDisabled && s.passion != Passion.None);
            return totalPassions >= PassionMin && totalPassions <= PassionMax;
        }

        private bool CheckSkillPoints(Pawn pawn)
        {
            if (SkillPointsMin <= 0 && SkillPointsMax >= 240) return true;
            if (pawn.skills == null) return true;

            int totalPoints = 0;
            bool shootingCounted = false;

            foreach (var skill in pawn.skills.skills)
            {
                if (skill.TotallyDisabled) continue;

                if (CountOnlyHighestAttack &&
                    (skill.def == SkillDefOf.Shooting || skill.def == SkillDefOf.Melee))
                {
                    if (!shootingCounted)
                    {
                        var shooting = pawn.skills.GetSkill(SkillDefOf.Shooting);
                        var melee = pawn.skills.GetSkill(SkillDefOf.Melee);
                        int shootingLevel = (shooting != null && !shooting.TotallyDisabled) ? shooting.Level : 0;
                        int meleeLevel = (melee != null && !melee.TotallyDisabled) ? melee.Level : 0;
                        totalPoints += Math.Max(shootingLevel, meleeLevel);
                        shootingCounted = true;
                    }
                    continue;
                }

                if (CountOnlyPassionSkills && skill.passion == Passion.None)
                    continue;

                totalPoints += skill.Level;
            }

            return totalPoints >= SkillPointsMin && totalPoints <= SkillPointsMax;
        }

        private bool CheckTraits(Pawn pawn)
        {
            if (pawn.story?.traits == null) return Traits.Count == 0;

            foreach (var filter in Traits)
            {
                bool hasTrait = pawn.story.traits.HasTrait(filter.Def, filter.Degree);

                if (filter.Mode == TraitFilterMode.Required && !hasTrait)
                    return false;
                if (filter.Mode == TraitFilterMode.Excluded && hasTrait)
                    return false;
            }

            // Check optional trait pool
            if (RequiredTraitsInPool > 0)
            {
                var optionalTraits = Traits.Where(t => t.Mode == TraitFilterMode.Optional);
                int poolMatches = optionalTraits.Count(t => pawn.story.traits.HasTrait(t.Def, t.Degree));
                if (poolMatches < RequiredTraitsInPool)
                    return false;
            }

            return true;
        }

        private bool CheckAge(Pawn pawn)
        {
            if (AgeMin <= 0 && AgeMax >= 120) return true;

            float age = pawn.ageTracker.AgeBiologicalYearsFloat;
            return age >= AgeMin && age <= AgeMax;
        }

        private bool CheckGender(Pawn pawn)
        {
            if (!Gender.HasValue) return true;
            return pawn.gender == Gender.Value;
        }

        private static bool IsGeneAffected(Hediff hediff)
        {
            if (!ModsConfig.BiotechActive) return false;
            return hediff is Hediff_ChemicalDependency chemDep && chemDep.LinkedGene != null;
        }

        private bool CheckHealth(Pawn pawn)
        {
            if (Health == HealthFilterMode.AllowAll) return true;
            if (pawn.health?.hediffSet == null) return true;

            var hediffs = pawn.health.hediffSet.hediffs;

            switch (Health)
            {
                case HealthFilterMode.AllowNone:
                    foreach (var hediff in hediffs)
                    {
                        if (!IsGeneAffected(hediff))
                            return false;
                    }
                    return true;

                case HealthFilterMode.OnlyStartCondition:
                    foreach (var hediff in hediffs)
                    {
                        if (IsGeneAffected(hediff))
                            continue;
                        if (hediff.def != HediffDefOf.CryptosleepSickness &&
                            hediff.def != HediffDefOf.Malnutrition)
                            return false;
                    }
                    return true;

                case HealthFilterMode.NoPain:
                    foreach (var hediff in hediffs)
                    {
                        if (IsGeneAffected(hediff))
                            continue;
                        var stage = hediff.CurStage;
                        if (stage != null && stage.painOffset > 0f)
                            return false;
                    }
                    return true;

                case HealthFilterMode.NoAddiction:
                    foreach (var hediff in hediffs)
                    {
                        if (IsGeneAffected(hediff))
                            continue;
                        if (hediff is Hediff_Addiction)
                            return false;
                    }
                    return true;

                default:
                    return true;
            }
        }

        // Fast reroll helper methods - expose individual checks for step-by-step filtering
        public bool CheckAgeForFastReroll(Pawn pawn) => CheckAge(pawn) && CheckGender(pawn);
        public bool CheckSkillsAndTraitsForFastReroll(Pawn pawn) =>
            CheckSkills(pawn) && CheckPassionRange(pawn) && CheckSkillPoints(pawn) && CheckTraits(pawn);
        public bool CheckHealthForFastReroll(Pawn pawn) => CheckHealth(pawn);
        public bool CheckWorkForFastReroll(Pawn pawn) => CheckWork(pawn);

        private bool CheckWork(Pawn pawn)
        {
            switch (Work)
            {
                case WorkFilterMode.AllowAll:
                    return true;

                case WorkFilterMode.AllowNone:
                    return pawn.CombinedDisabledWorkTags == WorkTags.None;

                case WorkFilterMode.NoDumbLabor:
                    return (pawn.CombinedDisabledWorkTags & WorkTags.ManualDumb) == 0;

                default:
                    return true;
            }
        }

        public PawnFilter Clone()
        {
            var clone = new PawnFilter
            {
                AgeMin = AgeMin,
                AgeMax = AgeMax,
                PassionMin = PassionMin,
                PassionMax = PassionMax,
                SkillPointsMin = SkillPointsMin,
                SkillPointsMax = SkillPointsMax,
                Gender = Gender,
                Health = Health,
                Work = Work,
                RerollLimit = RerollLimit,
                RequiredTraitsInPool = RequiredTraitsInPool,
                CountOnlyHighestAttack = CountOnlyHighestAttack,
                CountOnlyPassionSkills = CountOnlyPassionSkills,
                Algorithm = Algorithm
            };

            foreach (var skill in Skills)
            {
                clone.Skills.Add(new SkillFilter
                {
                    Skill = skill.Skill,
                    MinLevel = skill.MinLevel,
                    MinPassion = skill.MinPassion
                });
            }

            foreach (var trait in Traits)
            {
                clone.Traits.Add(new TraitFilter
                {
                    Def = trait.Def,
                    Degree = trait.Degree,
                    Mode = trait.Mode
                });
            }

            return clone;
        }

        public void CopyFrom(PawnFilter source)
        {
            AgeMin = source.AgeMin;
            AgeMax = source.AgeMax;
            PassionMin = source.PassionMin;
            PassionMax = source.PassionMax;
            SkillPointsMin = source.SkillPointsMin;
            SkillPointsMax = source.SkillPointsMax;
            Gender = source.Gender;
            Health = source.Health;
            Work = source.Work;
            RerollLimit = source.RerollLimit;
            RequiredTraitsInPool = source.RequiredTraitsInPool;
            CountOnlyHighestAttack = source.CountOnlyHighestAttack;
            CountOnlyPassionSkills = source.CountOnlyPassionSkills;
            Algorithm = source.Algorithm;

            Skills.Clear();
            foreach (var skill in source.Skills)
            {
                Skills.Add(new SkillFilter
                {
                    Skill = skill.Skill,
                    MinLevel = skill.MinLevel,
                    MinPassion = skill.MinPassion
                });
            }

            Traits.Clear();
            foreach (var trait in source.Traits)
            {
                Traits.Add(new TraitFilter
                {
                    Def = trait.Def,
                    Degree = trait.Degree,
                    Mode = trait.Mode
                });
            }
        }
    }

    public static class PawnFilterData
    {
        private static PawnFilter activeFilter = new PawnFilter();

        public static int LastRerollAttempts { get; set; }
        public static bool LastRerollSucceeded { get; set; }

        public static PawnFilter ActiveFilter => activeFilter;

        public static bool HasActiveFilters()
        {
            return activeFilter.HasActiveFilters();
        }

        public static void Initialize()
        {
            activeFilter = new PawnFilter();
            activeFilter.InitializeSkills();
            LastRerollAttempts = 0;
            LastRerollSucceeded = true;
        }

        public static void Reset()
        {
            activeFilter.Reset();
        }
    }
}
