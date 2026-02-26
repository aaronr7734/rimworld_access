using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper class for working with RimWorld's architect system.
    /// Provides methods to retrieve categories, designators, and materials.
    /// </summary>
    public static class ArchitectHelper
    {
        /// <summary>
        /// Gets all visible designation categories for the current game state.
        /// </summary>
        public static List<DesignationCategoryDef> GetAllCategories()
        {
            List<DesignationCategoryDef> categories = new List<DesignationCategoryDef>();

            foreach (DesignationCategoryDef categoryDef in DefDatabase<DesignationCategoryDef>.AllDefsListForReading)
            {
                // Check if category is visible (research unlocked, etc.)
                if (categoryDef.Visible)
                {
                    categories.Add(categoryDef);
                }
            }

            // Sort by order
            categories.SortBy(c => c.order);

            return categories;
        }

        /// <summary>
        /// Gets all allowed designators for a specific category.
        /// </summary>
        public static List<Designator> GetDesignatorsForCategory(DesignationCategoryDef category)
        {
            if (category == null)
                return new List<Designator>();

            List<Designator> designators = new List<Designator>();

            try
            {
                // First check if we have AllResolvedDesignators (this includes ideology and all resolved designators)
                List<Designator> allDesignators = category.AllResolvedDesignators;

                if (allDesignators == null || allDesignators.Count == 0)
                {
                    Log.Warning($"No resolved designators found for category: {category.defName}");
                    return designators;
                }

                Log.Message($"Found {allDesignators.Count} designators in category: {category.defName}");

                // Get allowed designators (filters by game rules and research)
                foreach (Designator designator in category.ResolvedAllowedDesignators)
                {
                    // Skip dropdown designators - we'll handle their contents instead
                    if (designator is Designator_Dropdown dropdown)
                    {
                        // Add all elements from the dropdown
                        if (dropdown.Elements != null)
                        {
                            foreach (Designator element in dropdown.Elements)
                            {
                                // Check visibility (includes research requirements)
                                if (element.Visible)
                                {
                                    designators.Add(element);
                                }
                            }
                        }
                    }
                    else
                    {
                        // Check visibility (includes research requirements)
                        if (designator.Visible)
                        {
                            designators.Add(designator);
                        }
                    }
                }

                Log.Message($"After filtering: {designators.Count} designators available");


            }
            catch (System.Exception ex)
            {
                Log.Error($"Error getting designators for category {category.defName}: {ex}");
            }

            return designators;
        }

        /// <summary>
        /// Gets all valid stuff (materials) for a buildable that requires stuff.
        /// </summary>
        public static List<ThingDef> GetMaterialsForBuildable(BuildableDef buildable)
        {
            List<ThingDef> materials = new List<ThingDef>();

            if (buildable is ThingDef thingDef && thingDef.MadeFromStuff)
            {
                // Get all stuff that can be used to make this thing
                foreach (ThingDef stuffDef in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if (stuffDef.IsStuff && stuffDef.stuffProps.CanMake(thingDef))
                    {
                        materials.Add(stuffDef);
                    }
                }

                // Sort by commonality - most common materials first
                materials.SortBy(m => -m.BaseMarketValue);
            }

            return materials;
        }

        /// <summary>
        /// Creates a Designator_Build for a specific buildable and material.
        /// </summary>
        public static Designator_Build CreateBuildDesignator(BuildableDef buildable, ThingDef stuffDef)
        {
            Designator_Build designator = new Designator_Build(buildable);

            // Set the stuff if provided
            if (stuffDef != null && buildable is ThingDef thingDef && thingDef.MadeFromStuff)
            {
                designator.SetStuffDef(stuffDef);
            }

            return designator;
        }

        /// <summary>
        /// Gets the designator label with the "..." suffix stripped.
        /// RimWorld adds "..." to labels when no material is selected (e.g., "wall...").
        /// This suffix needs to be removed before pluralization to avoid "wall...s".
        /// </summary>
        /// <param name="designator">The designator to get the label from</param>
        /// <param name="fallback">Fallback value if designator is null or label is empty</param>
        /// <returns>The sanitized label without trailing "..."</returns>
        public static string GetSanitizedLabel(Designator designator, string fallback = "Unknown")
        {
            string label = designator?.Label ?? fallback;
            if (label.EndsWith("..."))
            {
                label = label.Substring(0, label.Length - 3);
            }
            return label;
        }

        /// <summary>
        /// Pluralizes a label while preserving parenthetical suffixes.
        /// "sandstone grand stele (61%)" -> "sandstone grand steles (61%)"
        /// </summary>
        public static string PluralizePreservingParentheses(string label, int count)
        {
            if (string.IsNullOrEmpty(label) || count <= 1)
                return label;

            int parenIndex = label.IndexOf('(');
            if (parenIndex <= 0)
                return Find.ActiveLanguageWorker.Pluralize(label, count);

            string baseNoun = label.Substring(0, parenIndex).TrimEnd();
            string suffix = label.Substring(parenIndex);
            string pluralNoun = Find.ActiveLanguageWorker.Pluralize(baseNoun, count);
            return $"{pluralNoun} {suffix}";
        }

        /// <summary>
        /// Gets the default or most commonly available material for a buildable.
        /// </summary>
        public static ThingDef GetDefaultMaterial(BuildableDef buildable)
        {
            if (buildable is ThingDef thingDef && thingDef.MadeFromStuff)
            {
                // Try to get the default stuff
                ThingDef defaultStuff = GenStuff.DefaultStuffFor(thingDef);
                if (defaultStuff != null)
                    return defaultStuff;

                // Fall back to the first available material
                List<ThingDef> materials = GetMaterialsForBuildable(buildable);
                if (materials.Count > 0)
                    return materials[0];
            }

            return null;
        }

        /// <summary>
        /// Checks if a buildable requires material selection.
        /// </summary>
        public static bool RequiresMaterialSelection(BuildableDef buildable)
        {
            if (buildable is ThingDef thingDef)
            {
                return thingDef.MadeFromStuff;
            }
            return false;
        }

        /// <summary>
        /// Formats a list of materials as FloatMenuOptions.
        /// </summary>
        public static List<FloatMenuOption> CreateMaterialOptions(BuildableDef buildable, Action<ThingDef> onSelected)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            List<ThingDef> materials = GetMaterialsForBuildable(buildable);

            foreach (ThingDef material in materials)
            {
                // Check if we have this material available
                int availableCount = 0;
                if (Find.CurrentMap != null)
                {
                    availableCount = Find.CurrentMap.resourceCounter.GetCount(material);
                }

                string label = material.LabelCap;
                if (availableCount > 0)
                {
                    label += $" ({availableCount} available)";
                }
                else
                {
                    label += " (none available)";
                }

                options.Add(new FloatMenuOption(label, () => onSelected(material)));
            }

            return options;
        }

        /// <summary>
        /// Formats a list of designators as FloatMenuOptions.
        /// </summary>
        public static List<FloatMenuOption> CreateDesignatorOptions(List<Designator> designators, Action<Designator> onSelected)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (Designator designator in designators)
            {
                string label = designator.LabelCap;

                // Add cost and skill information for build designators
                if (designator is Designator_Build buildDesignator)
                {
                    string extraInfo = GetBuildableExtraInfo(buildDesignator.PlacingDef);
                    if (!string.IsNullOrEmpty(extraInfo))
                    {
                        label += extraInfo;
                    }
                }
                else
                {
                    // For non-build designators (orders), add description if available
                    string description = GetDesignatorDescriptionText(designator);
                    if (!string.IsNullOrEmpty(description))
                    {
                        label += $" ({description})";
                    }
                }

                // Add action
                options.Add(new FloatMenuOption(label, () => onSelected(designator)));
            }

            return options;
        }

        /// <summary>
        /// Gets extra information (cost, skill requirement, and description) for a buildable.
        /// Format: ": {cost}, requires Construction {level} ({description})" matching tree view style.
        /// </summary>
        private static string GetBuildableExtraInfo(BuildableDef buildable)
        {
            if (buildable == null)
                return "";

            string costInfo = GetBriefCostInfo(buildable);
            string skillInfo = GetSkillRequirement(buildable);
            string description = GetDescription(buildable);

            // Build list of info parts (cost, skill)
            var infoParts = new List<string>();
            if (!string.IsNullOrEmpty(costInfo))
                infoParts.Add(costInfo);
            if (!string.IsNullOrEmpty(skillInfo))
                infoParts.Add(skillInfo);

            string combinedInfo = string.Join(", ", infoParts);

            // Build the formatted string: ": cost, skill (description)"
            if (!string.IsNullOrEmpty(combinedInfo) && !string.IsNullOrEmpty(description))
            {
                return $": {combinedInfo} ({description})";
            }
            else if (!string.IsNullOrEmpty(combinedInfo))
            {
                return $": {combinedInfo}";
            }
            else if (!string.IsNullOrEmpty(description))
            {
                return $" ({description})";
            }

            return "";
        }

        /// <summary>
        /// Gets the skill requirement for a buildable, if any.
        /// </summary>
        public static string GetSkillRequirement(BuildableDef buildable)
        {
            if (buildable is ThingDef thingDef && thingDef.constructionSkillPrerequisite > 0)
            {
                return $"requires Construction {thingDef.constructionSkillPrerequisite}";
            }
            return "";
        }

        /// <summary>
        /// Gets brief cost information for display (no "Cost:" prefix).
        /// </summary>
        public static string GetBriefCostInfo(BuildableDef buildable)
        {
            if (buildable == null)
                return "";

            List<string> costParts = new List<string>();

            // Get stuff cost first (most common)
            if (buildable is ThingDef thingDef && thingDef.MadeFromStuff)
            {
                int stuffCount = buildable.CostStuffCount;
                if (stuffCount > 0)
                {
                    costParts.Add($"{stuffCount} material");
                }
            }

            // Get fixed costs
            List<ThingDefCountClass> costs = buildable.CostList;
            if (costs != null)
            {
                foreach (ThingDefCountClass cost in costs)
                {
                    costParts.Add($"{cost.count} {cost.thingDef.label}");
                }
            }

            return string.Join(", ", costParts);
        }

        /// <summary>
        /// Cleans up description text by removing newlines and collapsing whitespace.
        /// </summary>
        private static string CleanupDescription(string description)
        {
            if (string.IsNullOrEmpty(description))
                return "";

            description = description.Replace("\n", " ").Replace("\r", " ");
            description = System.Text.RegularExpressions.Regex.Replace(description, @"\s+", " ").Trim();
            return description;
        }

        /// <summary>
        /// Gets the description for a buildable as a formatted string.
        /// </summary>
        public static string GetDescription(BuildableDef buildable)
        {
            if (buildable == null)
                return "";
            return CleanupDescription(buildable.description);
        }

        /// <summary>
        /// Gets the description text for a designator (for orders/commands).
        /// </summary>
        public static string GetDesignatorDescriptionText(Designator designator)
        {
            if (designator == null)
                return "";
            return CleanupDescription(designator.Desc);
        }

        /// <summary>
        /// Formats categories as FloatMenuOptions.
        /// </summary>
        public static List<FloatMenuOption> CreateCategoryOptions(List<DesignationCategoryDef> categories, Action<DesignationCategoryDef> onSelected)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (DesignationCategoryDef category in categories)
            {
                string label = category.LabelCap;
                options.Add(new FloatMenuOption(label, () => onSelected(category)));
            }

            return options;
        }
    }
}
