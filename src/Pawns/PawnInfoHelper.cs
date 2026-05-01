using System.Collections.Generic;
using System.Linq;
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
        /// Combat-focused triage: pain, bleeding/time-to-death, impaired
        /// sight/manipulation/moving, damaged parts, whole-body hediffs.
        /// Skips bionics/implants unless the part is actually injured, and
        /// skips chronic non-combat conditions.
        /// </summary>
        public static string GetHealthInfo(Pawn pawn)
        {
            if (pawn == null)
                return "RimWorldAccess.Pawns.Info.NoPawnSelected".Translate();

            if (pawn.health == null)
                return "RimWorldAccess.Pawns.Health.NoTracker".Translate(pawn.LabelShort);

            var ab = new AnnouncementBuilder()
                .Add("RimWorldAccess.Pawns.Health.NameHeader".Translate(pawn.LabelShort));

            bool hasHealthDetails = false;

            string painLabel = HealthTabHelper.GetPainLabel(pawn);
            if (painLabel != null)
            {
                ab.Add(painLabel);
                hasHealthDetails = true;
            }

            string bleedingLabel = HealthTabHelper.GetBleedingLabel(pawn);
            if (bleedingLabel != null)
            {
                ab.Add(bleedingLabel);
                hasHealthDetails = true;
            }

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
                    ab.Add("RimWorldAccess.Pawns.Health.Capacity".Translate(cap.Label, cap.LevelLabel));
                    hasHealthDetails = true;
                }
            }

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
                    ab.Add("RimWorldAccess.Pawns.Health.PartHealth".Translate(
                        p.Part.LabelCap, p.Health.ToString("F0"), p.MaxHealth.ToString("F0")));
                    hasHealthDetails = true;
                }
            }

            var wholeBodyHediffs = HealthTabHelper.GetVisibleHediffs(pawn)
                .Where(h => h.Part == null)
                .ToList();

            foreach (var hediff in wholeBodyHediffs)
            {
                ab.Add(hediff.LabelCap);
                hasHealthDetails = true;
            }

            if (!hasHealthDetails)
            {
                ab.Add("Healthy".Translate());
            }

            return ab.Build();
        }

        public static string GetNeedsInfo(Pawn pawn)
        {
            if (pawn == null)
                return "RimWorldAccess.Pawns.Info.NoPawnSelected".Translate();

            if (pawn.needs == null)
                return "RimWorldAccess.Pawns.Needs.NoTracker".Translate(pawn.LabelShort);

            var ab = new AnnouncementBuilder()
                .Add("RimWorldAccess.Pawns.Needs.Header".Translate(pawn.LabelShort));

            var needs = pawn.needs.AllNeeds;
            if (needs != null && needs.Count > 0)
            {
                var sortedNeeds = needs
                    .Where(n => n.def.showOnNeedList)
                    .OrderBy(n => n.CurLevelPercentage)
                    .ToList();

                foreach (var need in sortedNeeds)
                {
                    float percentage = need.CurLevelPercentage * 100f;
                    ab.Add("RimWorldAccess.Pawns.Needs.NeedLine".Translate(need.LabelCap, percentage.ToString("F0")));

                    if (need.def == NeedDefOf.Learning && pawn.learning?.ActiveLearningDesires != null
                        && pawn.learning.ActiveLearningDesires.Count > 0)
                    {
                        var desireLabels = pawn.learning.ActiveLearningDesires
                            .Select(d => d.LabelCap.ToString())
                            .ToArray();
                        ab.Add("RimWorldAccess.Pawns.Needs.LearningDesires".Translate(string.Join(", ", desireLabels)));
                    }
                }
            }
            else
            {
                ab.Add("RimWorldAccess.Pawns.Needs.Empty".Translate());
            }

            return ab.Build();
        }

        public static string GetMoodInfo(Pawn pawn)
        {
            if (pawn == null)
                return "RimWorldAccess.Pawns.Info.NoPawnSelected".Translate();

            if (pawn.needs?.mood == null)
                return "RimWorldAccess.Pawns.Mood.NoTracker".Translate(pawn.LabelShort);

            var ab = new AnnouncementBuilder()
                .Add("RimWorldAccess.Pawns.Mood.Header".Translate(pawn.LabelShort));

            Need_Mood mood = pawn.needs.mood;

            float moodPercentage = mood.CurLevelPercentage * 100f;
            string moodDescription = mood.MoodString;
            ab.Add("RimWorldAccess.Pawns.Mood.MoodLine".Translate(moodPercentage.ToString("F0"), moodDescription));

            List<Thought> thoughtGroups = new List<Thought>();
            PawnNeedsUIUtility.GetThoughtGroupsInDisplayOrder(mood, thoughtGroups);

            if (thoughtGroups.Count > 0)
            {
                ab.Add("RimWorldAccess.Pawns.Mood.ThoughtsHeader".Translate(thoughtGroups.Count));

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
                    ab.Add("RimWorldAccess.Pawns.Mood.ThoughtLine".Translate(thoughtLabel, offsetText));

                    thoughtGroup.Clear();
                }
            }
            else
            {
                ab.Add("RimWorldAccess.Pawns.Mood.NoThoughts".Translate());
            }

            return ab.Build();
        }

        public static string GetGearInfo(Pawn pawn)
        {
            if (pawn == null)
                return "RimWorldAccess.Pawns.Info.NoPawnSelected".Translate();

            var ab = new AnnouncementBuilder()
                .Add("RimWorldAccess.Pawns.GearInfo.Header".Translate(pawn.LabelShort));

            if (pawn.equipment != null && pawn.equipment.Primary != null)
            {
                var weapon = pawn.equipment.Primary;
                string weaponLabel = weapon.LabelNoParenthesisCap.StripTags();
                var qualityComp = weapon.TryGetComp<CompQuality>();
                if (qualityComp != null)
                {
                    weaponLabel = "RimWorldAccess.Pawns.GearInfo.QualifiedItem".Translate(weaponLabel, qualityComp.Quality.GetLabel());
                }
                ab.Add("RimWorldAccess.Pawns.GearInfo.Wielding".Translate(weaponLabel));
            }
            else
            {
                ab.Add("RimWorldAccess.Pawns.GearInfo.WieldingNothing".Translate());
            }

            if (pawn.apparel != null && pawn.apparel.WornApparel != null && pawn.apparel.WornApparel.Count > 0)
            {
                var apparelList = new List<string>();
                foreach (var apparel in pawn.apparel.WornApparel)
                {
                    string apparelEntry = apparel.LabelNoParenthesisCap.StripTags();
                    var qualityComp = apparel.TryGetComp<CompQuality>();
                    if (qualityComp != null)
                    {
                        apparelEntry = "RimWorldAccess.Pawns.GearInfo.QualifiedItem".Translate(apparelEntry, qualityComp.Quality.GetLabel());
                    }
                    apparelList.Add(apparelEntry);
                }
                ab.Add("RimWorldAccess.Pawns.GearInfo.Wearing".Translate(string.Join(", ", apparelList)));
            }
            else
            {
                ab.Add("RimWorldAccess.Pawns.GearInfo.WearingNothing".Translate());
            }

            return ab.Build();
        }

        public static string GetSocialInfo(Pawn pawn)
        {
            if (pawn == null)
                return "RimWorldAccess.Pawns.Info.NoPawnSelected".Translate();

            if (pawn.relations == null)
                return "RimWorldAccess.Pawns.SocialInfo.NoTracker".Translate(pawn.LabelShort);

            var ab = new AnnouncementBuilder()
                .Add("RimWorldAccess.Pawns.SocialInfo.Header".Translate(pawn.LabelShort));

            var directRelations = pawn.relations.DirectRelations;
            if (directRelations != null && directRelations.Count > 0)
            {
                ab.Add("RimWorldAccess.Pawns.SocialInfo.RelationshipsHeader".Translate(directRelations.Count));
                foreach (var rel in directRelations)
                {
                    if (rel.otherPawn != null)
                    {
                        string relationLabel = rel.def.GetGenderSpecificLabelCap(rel.otherPawn);
                        ab.Add("RimWorldAccess.Pawns.SocialInfo.RelationshipLine".Translate(relationLabel, rel.otherPawn.LabelShort));
                    }
                }
            }

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
                    ab.Add("RimWorldAccess.Pawns.SocialInfo.OpinionsHeader".Translate(opinions.Count));
                    foreach (var opinion in opinions.Take(10))
                    {
                        ab.Add(opinion);
                    }
                    if (opinions.Count > 10)
                    {
                        ab.Add("RimWorldAccess.Pawns.SocialInfo.OpinionsMore".Translate(opinions.Count - 10));
                    }
                }
            }

            if (directRelations == null || directRelations.Count == 0)
            {
                ab.Add("RimWorldAccess.Pawns.SocialInfo.NoRelationships".Translate());
            }

            return ab.Build();
        }

        public static string GetTrainingInfo(Pawn pawn)
        {
            if (pawn == null)
                return "RimWorldAccess.Pawns.Info.NoPawnSelected".Translate();

            if (pawn.training == null)
                return "RimWorldAccess.Pawns.TrainingInfo.NotTrainable".Translate(pawn.LabelShort);

            var ab = new AnnouncementBuilder()
                .Add("RimWorldAccess.Pawns.TrainingInfo.Header".Translate(pawn.LabelShort));

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
                        untrainedSkills.Add("RimWorldAccess.Pawns.TrainingInfo.LineInProgress".Translate(trainable.LabelCap));
                    }
                }
            }

            if (trainedSkills.Count > 0)
            {
                ab.Add("RimWorldAccess.Pawns.TrainingInfo.TrainedHeader".Translate(trainedSkills.Count));
                foreach (var skill in trainedSkills)
                {
                    ab.Add(skill);
                }
            }

            if (untrainedSkills.Count > 0)
            {
                ab.Add("RimWorldAccess.Pawns.TrainingInfo.InProgressHeader".Translate(untrainedSkills.Count));
                foreach (var skill in untrainedSkills)
                {
                    ab.Add(skill);
                }
            }

            if (trainedSkills.Count == 0 && untrainedSkills.Count == 0)
            {
                ab.Add("RimWorldAccess.Pawns.TrainingInfo.NoSkills".Translate());
            }

            return ab.Build();
        }

        public static string GetCharacterInfo(Pawn pawn)
        {
            if (pawn == null)
                return "RimWorldAccess.Pawns.Info.NoPawnSelected".Translate();

            var ab = new AnnouncementBuilder()
                .Add("RimWorldAccess.Pawns.Character.Header".Translate(pawn.LabelShort));

            if (pawn.ageTracker != null)
            {
                ab.Add("RimWorldAccess.Pawns.Character.Age".Translate(pawn.ageTracker.AgeBiologicalYears));
            }

            if (pawn.story != null && pawn.story.traits != null)
            {
                var traits = pawn.story.traits.allTraits;
                if (traits != null && traits.Count > 0)
                {
                    ab.Add("RimWorldAccess.Pawns.Character.TraitsHeader".Translate(traits.Count));
                    foreach (var trait in traits)
                    {
                        ab.Add(trait.LabelCap);
                    }
                }
            }

            if (pawn.story != null)
            {
                if (pawn.story.Childhood != null)
                {
                    ab.Add("RimWorldAccess.Pawns.Character.Childhood".Translate(pawn.story.Childhood.TitleCapFor(pawn.gender)));
                }
                if (pawn.story.Adulthood != null)
                {
                    ab.Add("RimWorldAccess.Pawns.Character.Adulthood".Translate(pawn.story.Adulthood.TitleCapFor(pawn.gender)));
                }
            }

            if (pawn.skills != null && pawn.skills.skills != null)
            {
                var topSkills = pawn.skills.skills
                    .Where(s => !s.TotallyDisabled && s.Level > 0)
                    .OrderByDescending(s => s.Level)
                    .Take(5);

                if (topSkills.Any())
                {
                    ab.Add("RimWorldAccess.Pawns.Character.TopSkillsHeader".Translate());
                    foreach (var skill in topSkills)
                    {
                        string passion = "";
                        if (skill.passion == Passion.Minor)
                            passion = "RimWorldAccess.Pawns.Character.PassionMinor".Translate();
                        else if (skill.passion == Passion.Major)
                            passion = "RimWorldAccess.Pawns.Character.PassionMajor".Translate();

                        ab.Add("RimWorldAccess.Pawns.Character.SkillLine".Translate(skill.def.LabelCap, skill.Level, passion));
                    }
                }
            }

            return ab.Build();
        }

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

            var ab = new AnnouncementBuilder()
                .Add("RimWorldAccess.Pawns.TopSkills.Header".Translate(pawn.LabelShort));

            foreach (var skill in topSkills)
            {
                string entryKey;
                if (skill.passion == Passion.Minor)
                    entryKey = "RimWorldAccess.Pawns.TopSkills.SkillEntryMinorPassion";
                else if (skill.passion == Passion.Major)
                    entryKey = "RimWorldAccess.Pawns.TopSkills.SkillEntryMajorPassion";
                else
                    entryKey = "RimWorldAccess.Pawns.TopSkills.SkillEntry";

                ab.Add(entryKey.Translate(skill.def.LabelCap, skill.Level));
            }

            return ab.Build();
        }

        public static string GetWorkInfo(Pawn pawn)
        {
            if (pawn == null)
                return "RimWorldAccess.Pawns.Info.NoPawnSelected".Translate();

            if (pawn.workSettings == null)
                return "RimWorldAccess.Pawns.WorkInfo.NoSettings".Translate(pawn.LabelShort);

            var ab = new AnnouncementBuilder()
                .Add("RimWorldAccess.Pawns.WorkInfo.Header".Translate(pawn.LabelShort));

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
                        disabledWork.Add(workType.labelShort);
                    }
                }
            }

            if (enabledWork.Count > 0)
            {
                ab.Add("RimWorldAccess.Pawns.WorkInfo.EnabledHeader".Translate(enabledWork.Count));
                foreach (var work in enabledWork)
                {
                    ab.Add(work);
                }
            }

            if (disabledWork.Count > 0 && disabledWork.Count <= 10)
            {
                ab.Add("RimWorldAccess.Pawns.WorkInfo.DisabledHeader".Translate(disabledWork.Count));
                foreach (var work in disabledWork)
                {
                    ab.Add(work);
                }
            }

            if (enabledWork.Count == 0)
            {
                ab.Add("RimWorldAccess.Pawns.WorkInfo.NoWork".Translate());
            }

            return ab.Build();
        }
    }
}
