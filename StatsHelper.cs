using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper class for fetching and formatting stats for Things using RimWorld's native stat system.
    /// This ensures all stats displayed to sighted players are also accessible to screen reader users.
    /// </summary>
    public static class StatsHelper
    {
        /// <summary>
        /// Checks if a StatDef is food-related and should be excluded from non-food items.
        /// </summary>
        private static bool IsFoodRelatedStat(StatDef statDef)
        {
            // Check for common food-related stats
            return statDef == StatDefOf.FoodPoisonChance ||
                   statDef == StatDefOf.FoodPoisonChanceFixedHuman ||
                   statDef == StatDefOf.Nutrition;
        }

        /// <summary>
        /// Gets a concise description for a stat entry, suitable for screen reader output.
        /// Extracts the first sentence or paragraph from the stat's explanation text.
        /// </summary>
        private static string GetStatDescription(StatDrawEntry stat)
        {
            try
            {
                // Get the full explanation text from the stat entry
                string explanation = stat.GetExplanationText(StatRequest.ForEmpty());

                if (string.IsNullOrEmpty(explanation))
                    return null;

                // Strip XML tags
                explanation = explanation.StripTags();

                // Remove excessive whitespace and newlines
                explanation = System.Text.RegularExpressions.Regex.Replace(explanation, @"\s+", " ").Trim();

                // Extract the first sentence (description usually comes first)
                // Look for the first period followed by space or end of string
                int firstPeriod = explanation.IndexOf(". ");
                if (firstPeriod > 0 && firstPeriod < 200) // Reasonable length for a description
                {
                    explanation = explanation.Substring(0, firstPeriod);
                }
                else if (explanation.Length > 200)
                {
                    // If too long and no period, truncate at reasonable length
                    explanation = explanation.Substring(0, 197) + "...";
                }

                return explanation;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Error getting description for stat {stat.LabelCap}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets all displayable stats for a Thing using RimWorld's native stat system.
        /// This mimics the logic in StatsReportUtility.StatsToDraw().
        /// </summary>
        public static List<StatDrawEntry> GetAllStats(Thing thing)
        {
            if (thing == null)
                return new List<StatDrawEntry>();

            var stats = new List<StatDrawEntry>();

            try
            {
                // Create stat request for this thing
                StatRequest statRequest = StatRequest.For(thing);

                // Iterate through all stat definitions in the game
                foreach (StatDef statDef in DefDatabase<StatDef>.AllDefs
                    .Where(st => st.Worker.ShouldShowFor(statRequest)))
                {
                    // Skip abstract stats (matches RimWorld's native filtering)
                    if (!statDef.showNonAbstract)
                        continue;

                    // Exclude food-related stats for non-food items
                    if (!thing.def.IsIngestible && IsFoodRelatedStat(statDef))
                        continue;

                    // Check if stat is disabled for this thing
                    if (!statDef.Worker.IsDisabledFor(thing))
                    {
                        float statValue = thing.GetStatValue(statDef);

                        // Only show if value differs from default OR stat forces display
                        if (statDef.showOnDefaultValue || Math.Abs(statValue - statDef.defaultBaseValue) > 0.0001f)
                        {
                            stats.Add(new StatDrawEntry(
                                statDef.category,
                                statDef,
                                statValue,
                                statRequest,
                                ToStringNumberSense.Absolute
                            ));
                        }
                    }
                    else
                    {
                        // Stat is disabled (e.g., Medical stat on pawn with no medical skill)
                        // We can choose to skip these or show as disabled
                        // For now, we'll skip them to reduce clutter
                    }
                }

                // Add hit points if applicable
                if (thing.def.useHitPoints)
                {
                    stats.Add(new StatDrawEntry(
                        StatCategoryDefOf.BasicsImportant,
                        "HitPointsBasic".Translate().CapitalizeFirst(),
                        thing.HitPoints + " / " + thing.MaxHitPoints,
                        "Stat_HitPoints_Desc".Translate(),
                        99998
                    ));
                }

                // Add special/custom stats from the ThingDef (includes weapon verb stats like damage, range, etc.)
                // This is CRITICAL for weapons - damage stats come from ThingDef.SpecialDisplayStats()
                IEnumerable<StatDrawEntry> defSpecialStats = thing.def.SpecialDisplayStats(statRequest);
                if (defSpecialStats != null)
                {
                    stats.AddRange(defSpecialStats);
                }

                // Add special/custom stats defined by the thing instance
                // These include instance-specific stats and component stats
                IEnumerable<StatDrawEntry> specialStats = thing.SpecialDisplayStats();
                if (specialStats != null)
                {
                    stats.AddRange(specialStats);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error getting stats for {thing.LabelCap}: {ex.Message}");
            }

            return stats;
        }

        /// <summary>
        /// Formats a list of stat entries into a readable string for screen readers.
        /// Groups stats by category and sorts by priority.
        /// </summary>
        public static string FormatStatsForScreenReader(List<StatDrawEntry> stats, string objectLabel = null)
        {
            if (stats == null || stats.Count == 0)
                return "No stats available.";

            var sb = new StringBuilder();

            // Optional header
            if (!string.IsNullOrEmpty(objectLabel))
            {
                sb.AppendLine(objectLabel);
                sb.AppendLine();
            }

            // Filter out entries that shouldn't be displayed
            var displayableStats = stats.ToList();

            if (displayableStats == null || displayableStats.Count == 0)
                return "No stats to display.";

            // Sort by category order, then by priority within category, then by label
            var sortedStats = displayableStats
                .OrderBy(e => e.category.displayOrder)
                .ThenByDescending(e => e.DisplayPriorityWithinCategory)
                .ThenBy(e => e.LabelCap)
                .ToList();

            // Group by category and format
            string currentCategory = null;
            foreach (var stat in sortedStats)
            {
                // Add category header if this is a new category
                string categoryLabel = stat.category?.LabelCap.ToString() ?? "Other";
                if (categoryLabel != currentCategory)
                {
                    currentCategory = categoryLabel;
                    if (sb.Length > 0)
                        sb.AppendLine(); // Add spacing between categories
                    sb.AppendLine($"--- {currentCategory} ---");
                }

                // Format the stat line with description
                string label = stat.LabelCap.ToString().StripTags();
                string value = stat.ValueString.StripTags();

                // Replace dollar sign with "silver" for lore-friendly currency display
                if (value.Contains("$"))
                {
                    value = value.Replace("$", "").Trim() + " silver";
                }

                // Get description/explanation for the stat
                string description = GetStatDescription(stat);

                // Format: "Stat Name: Value - Description"
                if (!string.IsNullOrEmpty(description))
                {
                    sb.AppendLine($"{label}: {value} - {description}");
                }
                else
                {
                    sb.AppendLine($"{label}: {value}");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets formatted stats specifically for quality items (weapons, apparel, etc.).
        /// Includes quality at the top if applicable.
        /// </summary>
        public static string GetFormattedStatsWithQuality(Thing thing)
        {
            if (thing == null)
                return "No object selected.";

            var sb = new StringBuilder();

            // Check for quality component
            var qualityComp = thing.TryGetComp<CompQuality>();
            if (qualityComp != null)
            {
                sb.AppendLine($"Quality: {qualityComp.Quality}");
                sb.AppendLine();
            }

            // Get all stats
            List<StatDrawEntry> stats = GetAllStats(thing);

            // Format and append
            string formattedStats = FormatStatsForScreenReader(stats);
            sb.Append(formattedStats);

            return sb.ToString();
        }

        /// <summary>
        /// Gets a concise summary of the most important stats for quick reference.
        /// This is useful for combat items where users need quick info.
        /// </summary>
        public static string GetQuickStatsSummary(Thing thing)
        {
            if (thing == null)
                return "No object selected.";

            var sb = new StringBuilder();
            sb.AppendLine(thing.LabelCap.StripTags());

            try
            {
                // For weapons, show damage and cooldown
                if (thing.def.IsWeapon)
                {
                    if (thing.def.IsRangedWeapon)
                    {
                        float damage = thing.GetStatValue(StatDefOf.RangedWeapon_DamageMultiplier);
                        float cooldown = thing.GetStatValue(StatDefOf.RangedWeapon_Cooldown);
                        sb.AppendLine($"Damage: {damage:F1}x");
                        sb.AppendLine($"Cooldown: {cooldown:F2}s");
                    }
                    else if (thing.def.IsMeleeWeapon)
                    {
                        // Melee weapons have different damage stats
                        float marketValue = thing.GetStatValue(StatDefOf.MarketValue);
                        sb.AppendLine($"Market Value: {marketValue:F0}");
                    }
                }
                // For apparel, show armor
                else if (thing.def.IsApparel)
                {
                    float armorSharp = thing.GetStatValue(StatDefOf.ArmorRating_Sharp);
                    float armorBlunt = thing.GetStatValue(StatDefOf.ArmorRating_Blunt);
                    sb.AppendLine($"Armor (Sharp): {armorSharp:P0}");
                    sb.AppendLine($"Armor (Blunt): {armorBlunt:P0}");
                }
                // For everything else, show market value and mass
                else
                {
                    float marketValue = thing.GetStatValue(StatDefOf.MarketValue);
                    float mass = thing.GetStatValue(StatDefOf.Mass);
                    sb.AppendLine($"Value: {marketValue:F0} silver");
                    sb.AppendLine($"Mass: {mass:F2} kg");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Error: {ex.Message}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets stats grouped by category for hierarchical navigation.
        /// Returns a dictionary where keys are category labels and values are lists of stat entries.
        /// </summary>
        public static Dictionary<string, List<StatDrawEntry>> GetStatsGroupedByCategory(Thing thing)
        {
            var grouped = new Dictionary<string, List<StatDrawEntry>>();

            if (thing == null)
                return grouped;

            List<StatDrawEntry> allStats = GetAllStats(thing);

            foreach (var stat in allStats)
            {
                string categoryLabel = stat.category?.LabelCap.ToString() ?? "Other";

                if (!grouped.ContainsKey(categoryLabel))
                {
                    grouped[categoryLabel] = new List<StatDrawEntry>();
                }

                grouped[categoryLabel].Add(stat);
            }

            // Sort each category's stats by priority
            foreach (var category in grouped.Keys.ToList())
            {
                grouped[category] = grouped[category]
                    .OrderByDescending(s => s.DisplayPriorityWithinCategory)
                    .ThenBy(s => s.LabelCap)
                    .ToList();
            }

            return grouped;
        }

        /// <summary>
        /// Checks if a Thing has any displayable stats.
        /// </summary>
        public static bool HasStats(Thing thing)
        {
            if (thing == null)
                return false;

            List<StatDrawEntry> stats = GetAllStats(thing);
            return stats != null && stats.Any();
        }
    }
}
