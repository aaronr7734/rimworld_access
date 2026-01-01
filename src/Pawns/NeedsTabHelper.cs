using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper class for needs tab data extraction.
    /// Provides methods for need information and thought details.
    /// </summary>
    public static class NeedsTabHelper
    {
        /// <summary>
        /// Represents a need with its current status.
        /// </summary>
        public class NeedInfo
        {
            public Need Need { get; set; }
            public string Label { get; set; }
            public float Percentage { get; set; }
            public string Arrow { get; set; }
            public string DetailedInfo { get; set; }
        }

        /// <summary>
        /// Represents a thought with its mood effect.
        /// </summary>
        public class ThoughtInfo
        {
            public Thought Thought { get; set; }
            public string Label { get; set; }
            public float MoodEffect { get; set; }
            public int StackCount { get; set; }
            public string DetailedInfo { get; set; }
        }

        #region Needs

        /// <summary>
        /// Gets all needs for a pawn.
        /// </summary>
        public static List<NeedInfo> GetNeeds(Pawn pawn)
        {
            var needs = new List<NeedInfo>();

            if (pawn?.needs == null)
                return needs;

            var allNeeds = pawn.needs.AllNeeds;
            if (allNeeds == null)
                return needs;

            foreach (var need in allNeeds)
            {
                if (!need.def.showOnNeedList)
                    continue;

                string arrow = "";
                if (need.GUIChangeArrow == 1)
                    arrow = " ↑";
                else if (need.GUIChangeArrow == -1)
                    arrow = " ↓";

                needs.Add(new NeedInfo
                {
                    Need = need,
                    Label = need.LabelCap.ToString().StripTags(),
                    Percentage = need.CurLevelPercentage * 100f,
                    Arrow = arrow,
                    DetailedInfo = GetNeedDetailedInfo(need)
                });
            }

            // Sort by priority (mood first if humanlike, then others)
            needs = needs.OrderByDescending(n => n.Need is Need_Mood ? 1 : 0)
                        .ThenBy(n => n.Need.def.listPriority)
                        .ToList();

            return needs;
        }

        private static string GetNeedDetailedInfo(Need need)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{need.LabelCap.ToString().StripTags()}:");
            sb.AppendLine();

            // Current level
            sb.AppendLine($"Current: {need.CurLevelPercentage:P0}");

            // Description
            if (!string.IsNullOrEmpty(need.def.description))
            {
                sb.AppendLine();
                sb.AppendLine(need.def.description);
            }

            // Change rate if available
            if (need is Need_Rest rest)
            {
                float restFallPerTick = rest.RestFallPerTick;
                sb.AppendLine();
                sb.AppendLine($"Fall rate: {restFallPerTick * 60000f:F2} per day");
            }
            else if (need is Need_Food food)
            {
                float foodFallPerTick = food.FoodFallPerTick;
                sb.AppendLine();
                sb.AppendLine($"Fall rate: {foodFallPerTick * 60000f:F2} per day");
            }

            // Threshold warnings
            if (need.CurLevelPercentage < 0.3f)
            {
                sb.AppendLine();
                sb.AppendLine("WARNING: Low level");
            }

            return sb.ToString().TrimEnd();
        }

        #endregion

        #region Mood & Thoughts

        /// <summary>
        /// Gets mood information for a pawn.
        /// </summary>
        public static string GetMoodInfo(Pawn pawn)
        {
            if (pawn?.needs?.mood == null)
                return "No mood information available";

            var sb = new StringBuilder();

            // Current mood level
            float moodLevel = pawn.needs.mood.CurLevelPercentage * 100f;
            sb.AppendLine($"Mood: {moodLevel:F0}%");

            // Mood description
            string moodDesc = pawn.needs.mood.CurInstantLevelPercentage < pawn.mindState.mentalBreaker.BreakThresholdMinor
                ? "Breaking"
                : moodLevel < 50f ? "Low"
                : moodLevel < 70f ? "Content"
                : "Happy";
            sb.AppendLine($"Status: {moodDesc}");

            // Break threshold
            float breakThreshold = pawn.mindState.mentalBreaker.BreakThresholdMinor * 100f;
            sb.AppendLine($"Break threshold: {breakThreshold:F0}%");

            if (moodLevel < breakThreshold + 10f)
            {
                sb.AppendLine();
                sb.AppendLine("WARNING: Close to breaking");
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Gets all thoughts for a pawn.
        /// </summary>
        public static List<ThoughtInfo> GetThoughts(Pawn pawn)
        {
            var thoughts = new List<ThoughtInfo>();

            if (pawn?.needs?.mood?.thoughts == null)
                return thoughts;

            // Get all mood thoughts
            List<Thought> allThoughts = new List<Thought>();
            pawn.needs.mood.thoughts.GetAllMoodThoughts(allThoughts);

            // Group by def for stacking
            var grouped = allThoughts.GroupBy(t => t.def);

            foreach (var group in grouped)
            {
                var firstThought = group.First();
                int count = group.Count();
                float totalEffect = group.Sum(t => t.MoodOffset());

                thoughts.Add(new ThoughtInfo
                {
                    Thought = firstThought,
                    Label = firstThought.LabelCap.ToString().StripTags(),
                    MoodEffect = totalEffect,
                    StackCount = count,
                    DetailedInfo = GetThoughtDetailedInfo(firstThought, count, totalEffect)
                });
            }

            // Sort by mood effect (most negative first, then most positive)
            thoughts = thoughts.OrderBy(t => t.MoodEffect).ToList();

            return thoughts;
        }

        private static string GetThoughtDetailedInfo(Thought thought, int stackCount, float totalEffect)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{thought.LabelCap.ToString().StripTags()}:");
            sb.AppendLine();

            // Mood effect
            string effectStr = totalEffect >= 0 ? $"+{totalEffect:F0}" : $"{totalEffect:F0}";
            sb.AppendLine($"Mood effect: {effectStr}");

            if (stackCount > 1)
            {
                sb.AppendLine($"Stack count: {stackCount}");
            }

            // Description
            string desc = thought.Description;
            if (!string.IsNullOrEmpty(desc))
            {
                sb.AppendLine();
                sb.AppendLine(desc);
            }

            // Duration for memories
            if (thought is Thought_Memory memory)
            {
                sb.AppendLine();
                if (memory.age >= 0)
                {
                    float daysLeft = (memory.def.DurationTicks - memory.age) / 60000f;
                    if (daysLeft > 0)
                    {
                        sb.AppendLine($"Expires in: {daysLeft:F1} days");
                    }
                }
            }

            // Stage information if multiple stages
            if (thought.CurStageIndex >= 0 && thought.def.stages != null && thought.def.stages.Count > 1)
            {
                sb.AppendLine();
                sb.AppendLine($"Stage: {thought.CurStageIndex + 1} of {thought.def.stages.Count}");
            }

            return sb.ToString().TrimEnd();
        }

        #endregion
    }
}
