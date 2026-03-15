using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public static class PawnFilterPresetSerializer
    {
        private static string filePath;

        public static string FilePath
        {
            get
            {
                if (filePath == null)
                {
                    string configFolder = Path.GetDirectoryName(GenFilePaths.ModsConfigFilePath);
                    filePath = Path.Combine(configFolder, "RandomPlus.xml");
                }
                return filePath;
            }
        }

        public static List<string> GetPresetNames()
        {
            var presets = LoadAllPresets();
            return presets.Select(p => p.name ?? "").ToList();
        }

        public static PawnFilter LoadPreset(int index)
        {
            var presets = LoadAllPresets();
            if (index < 0 || index >= presets.Count)
                return null;

            return presets[index].ToPawnFilter();
        }

        public static void SavePreset(PawnFilter filter, string name)
        {
            var presets = LoadAllPresets();
            presets.Add(PresetEntry.FromPawnFilter(filter, name));
            SaveAllPresets(presets);
        }

        public static void OverwritePreset(PawnFilter filter, string name, int index)
        {
            var presets = LoadAllPresets();
            if (index < 0 || index >= presets.Count)
                return;

            presets[index] = PresetEntry.FromPawnFilter(filter, name);
            SaveAllPresets(presets);
        }

        public static void DeletePreset(int index)
        {
            var presets = LoadAllPresets();
            if (index < 0 || index >= presets.Count)
                return;

            presets.RemoveAt(index);
            SaveAllPresets(presets);
        }

        private static List<PresetEntry> LoadAllPresets()
        {
            var presets = new List<PresetEntry>();

            if (!File.Exists(FilePath))
                return presets;

            try
            {
                Scribe.loader.InitLoading(FilePath);
                Scribe_Collections.Look(ref presets, "list", LookMode.Deep);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Failed to load RandomPlus.xml: {ex.Message}");
                presets = new List<PresetEntry>();
            }
            finally
            {
                Scribe.loader.FinalizeLoading();
                Scribe.mode = LoadSaveMode.Inactive;
            }

            if (presets == null)
                presets = new List<PresetEntry>();

            return presets;
        }

        private static void SaveAllPresets(List<PresetEntry> presets)
        {
            try
            {
                Scribe.saver.InitSaving(FilePath, "RandomPlus");
                Scribe_Collections.Look(ref presets, "list", LookMode.Deep);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Failed to save RandomPlus.xml: {ex.Message}");
            }
            finally
            {
                Scribe.saver.FinalizeSaving();
                Scribe.mode = LoadSaveMode.Inactive;
            }
        }

        private class SkillEntry : IExposable
        {
            public SkillDef skillDef;
            public Passion passion;
            public int min_value;

            public SkillEntry() { }

            public SkillEntry(SkillFilter sf)
            {
                skillDef = sf.Skill;
                passion = sf.MinPassion;
                min_value = sf.MinLevel;
            }

            public void ExposeData()
            {
                Scribe_Defs.Look(ref skillDef, "skillDef");
                Scribe_Values.Look(ref passion, "passion");
                Scribe_Values.Look(ref min_value, "min_value");
            }
        }

        private class TraitEntry : IExposable
        {
            public string traitDefName;
            public int traitDegree;
            public TraitFilterMode filterType;

            public TraitEntry() { }

            public TraitEntry(TraitFilter tf)
            {
                traitDefName = tf.Def.defName;
                traitDegree = tf.Degree;
                filterType = tf.Mode;
            }

            public void ExposeData()
            {
                switch (Scribe.mode)
                {
                    case LoadSaveMode.Saving:
                        Scribe_Values.Look(ref traitDefName, "traitDef", null, false);
                        Scribe_Values.Look(ref traitDegree, "traitDegree", 0, false);
                        break;
                    case LoadSaveMode.LoadingVars:
                        Scribe_Values.Look(ref traitDefName, "traitDef", null, false);
                        Scribe_Values.Look(ref traitDegree, "traitDegree", 0, false);
                        break;
                }
                Scribe_Values.Look(ref filterType, "filterType", TraitFilterMode.Required, false);
            }
        }

        private class PresetEntry : IExposable
        {
            public string name = "";
            private int version = 1;
            private List<SkillEntry> skills = new List<SkillEntry>();
            private List<TraitEntry> traits = new List<TraitEntry>();
            private int poolSize;
            private int passionRangeMin;
            private int passionRangeMax;
            private int skillRangeMin;
            private int skillRangeMax;
            private bool countOnlyHighestAttack;
            private bool countOnlyPassion;
            private int ageRangeMin;
            private int ageRangeMax;
            private int rerollLimit;
            private Gender gender;
            private HealthFilterMode healthCondition;
            private WorkFilterMode incapable;

            public PresetEntry() { }

            public void ExposeData()
            {
                Scribe_Values.Look(ref name, "name", "");
                Scribe_Values.Look(ref version, "version", 1);
                Scribe_Collections.Look(ref skills, "skills", LookMode.Deep);
                Scribe_Collections.Look(ref traits, "traits", LookMode.Deep);
                Scribe_Values.Look(ref poolSize, "poolSize", 0);
                Scribe_Values.Look(ref passionRangeMin, "passionRangeMin", 0);
                Scribe_Values.Look(ref passionRangeMax, "passionRangeMax",
                    DefDatabase<SkillDef>.AllDefsListForReading.Count);
                Scribe_Values.Look(ref skillRangeMin, "skillRangeMin", 0);
                Scribe_Values.Look(ref skillRangeMax, "skillRangeMax",
                    DefDatabase<SkillDef>.AllDefsListForReading.Count * 8);
                Scribe_Values.Look(ref countOnlyHighestAttack, "countOnlyHighestAttack", false);
                Scribe_Values.Look(ref countOnlyPassion, "countOnlyPassion", false);
                Scribe_Values.Look(ref ageRangeMin, "ageRangeMin", 0);
                Scribe_Values.Look(ref ageRangeMax, "ageRangeMax", 120);
                // rerollAlgorithm field removed — old presets may still contain it, Scribe silently ignores missing fields
                Scribe_Values.Look(ref rerollLimit, "rerollLimit", 1000);
                Scribe_Values.Look(ref gender, "gender", Gender.None);
                Scribe_Values.Look(ref healthCondition, "healthCondition", HealthFilterMode.AllowAll);
                Scribe_Values.Look(ref incapable, "incapable", WorkFilterMode.AllowAll);
            }

            public PawnFilter ToPawnFilter()
            {
                var filter = new PawnFilter();
                filter.InitializeSkills();

                // Apply saved skills
                if (skills != null)
                {
                    foreach (var entry in skills)
                    {
                        if (entry.skillDef == null) continue;
                        var sf = filter.Skills.FirstOrDefault(s => s.Skill == entry.skillDef);
                        if (sf != null)
                        {
                            sf.MinLevel = entry.min_value;
                            sf.MinPassion = entry.passion;
                        }
                    }
                }

                // Apply saved traits
                if (traits != null)
                {
                    foreach (var entry in traits)
                    {
                        if (string.IsNullOrEmpty(entry.traitDefName)) continue;
                        var traitDef = DefDatabase<TraitDef>.GetNamedSilentFail(entry.traitDefName);
                        if (traitDef != null)
                        {
                            filter.Traits.Add(new TraitFilter
                            {
                                Def = traitDef,
                                Degree = entry.traitDegree,
                                Mode = entry.filterType
                            });
                        }
                    }
                }

                filter.RequiredTraitsInPool = poolSize;
                filter.PassionMin = passionRangeMin;
                filter.PassionMax = passionRangeMax;
                filter.SkillPointsMin = skillRangeMin;
                filter.SkillPointsMax = skillRangeMax;
                filter.CountOnlyHighestAttack = countOnlyHighestAttack;
                filter.CountOnlyPassionSkills = countOnlyPassion;
                filter.AgeMin = ageRangeMin;
                filter.AgeMax = ageRangeMax;
                filter.RerollLimit = rerollLimit;
                filter.Gender = (gender == Gender.None) ? (Gender?)null : gender;
                filter.Health = healthCondition;
                filter.Work = incapable;

                return filter;
            }

            public static PresetEntry FromPawnFilter(PawnFilter filter, string presetName)
            {
                var entry = new PresetEntry();
                entry.name = presetName ?? "";

                // Convert skills
                entry.skills = new List<SkillEntry>();
                foreach (var sf in filter.Skills)
                {
                    entry.skills.Add(new SkillEntry(sf));
                }

                // Convert traits
                entry.traits = new List<TraitEntry>();
                foreach (var tf in filter.Traits)
                {
                    entry.traits.Add(new TraitEntry(tf));
                }

                entry.poolSize = filter.RequiredTraitsInPool;
                entry.passionRangeMin = filter.PassionMin;
                entry.passionRangeMax = filter.PassionMax;
                entry.skillRangeMin = filter.SkillPointsMin;
                entry.skillRangeMax = filter.SkillPointsMax;
                entry.countOnlyHighestAttack = filter.CountOnlyHighestAttack;
                entry.countOnlyPassion = filter.CountOnlyPassionSkills;
                entry.ageRangeMin = filter.AgeMin;
                entry.ageRangeMax = filter.AgeMax;
                entry.rerollLimit = filter.RerollLimit;
                entry.gender = filter.Gender ?? Gender.None;
                entry.healthCondition = filter.Health;
                entry.incapable = filter.Work;

                return entry;
            }
        }
    }
}
