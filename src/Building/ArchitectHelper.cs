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

                // Add missing designators for Orders category
                if (category.defName == "Orders")
                {
                    // Add Uninstall designator if not already present
                    bool hasUninstall = designators.Any(d => d is Designator_Uninstall);
                    if (!hasUninstall)
                    {
                        designators.Add(new Designator_Uninstall());
                        Log.Message("Added Designator_Uninstall to Orders category");
                    }

                    // Add Open designator if not already present
                    bool hasOpen = designators.Any(d => d is Designator_Open);
                    if (!hasOpen)
                    {
                        designators.Add(new Designator_Open());
                        Log.Message("Added Designator_Open to Orders category");
                    }
                }
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
        /// Checks if a designator supports multi-cell designation (e.g., mining, plant cutting).
        /// </summary>
        public static bool SupportsMultiCellDesignation(Designator designator)
        {
            // Most cell-based designators support multiple cells
            if (designator is Designator_Cells)
                return true;

            // Build designators can be placed on multiple cells if not a single-tile building
            if (designator is Designator_Build buildDesignator)
            {
                // Buildings are typically placed one at a time
                // But we can allow multiple placements in sequence
                return false;
            }

            return false;
        }

        /// <summary>
        /// Gets a user-friendly description of what a designator does.
        /// </summary>
        public static string GetDesignatorDescription(Designator designator)
        {
            if (!string.IsNullOrEmpty(designator.Desc))
                return designator.Desc;

            // Provide default descriptions for common designator types
            if (designator is Designator_Mine)
                return "Mine rock and minerals";
            else if (designator is Designator_Build)
                return "Construct buildings and structures";
            else if (designator is Designator_PlantsHarvestWood)
                return "Chop down trees for wood";
            else if (designator is Designator_PlantsCut)
                return "Cut plants";
            else if (designator is Designator_Hunt)
                return "Hunt animals for meat";
            else if (designator is Designator_Tame)
                return "Tame wild animals";

            return designator.Label;
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
        /// Gets extra information (cost and description) for a buildable.
        /// Format: ": {cost} ({description})" matching tree view style.
        /// </summary>
        private static string GetBuildableExtraInfo(BuildableDef buildable)
        {
            if (buildable == null)
                return "";

            string costInfo = GetBriefCostInfo(buildable);
            string description = GetDescription(buildable);

            // Build the formatted string: ": cost (description)"
            if (!string.IsNullOrEmpty(costInfo) && !string.IsNullOrEmpty(description))
            {
                return $": {costInfo} ({description})";
            }
            else if (!string.IsNullOrEmpty(costInfo))
            {
                return $": {costInfo}";
            }
            else if (!string.IsNullOrEmpty(description))
            {
                return $" ({description})";
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
        /// Gets skill requirements for a buildable as a formatted string.
        /// </summary>
        private static string GetSkillRequirements(BuildableDef buildable)
        {
            if (buildable == null)
                return "";

            List<string> skillParts = new List<string>();

            // Check construction skill requirement
            if (buildable.constructionSkillPrerequisite > 0)
            {
                skillParts.Add($"Construction {buildable.constructionSkillPrerequisite}");
            }

            // Check artistic skill requirement
            if (buildable.artisticSkillPrerequisite > 0)
            {
                skillParts.Add($"Artistic {buildable.artisticSkillPrerequisite}");
            }

            if (skillParts.Count > 0)
            {
                return "Skills: " + string.Join(", ", skillParts);
            }

            return "";
        }

        /// <summary>
        /// Gets the description for a buildable as a formatted string.
        /// </summary>
        public static string GetDescription(BuildableDef buildable)
        {
            if (buildable == null)
                return "";

            string description = buildable.description;
            if (!string.IsNullOrEmpty(description))
            {
                // Clean up the description - remove newlines and excess whitespace
                description = description.Replace("\n", " ").Replace("\r", " ");
                description = System.Text.RegularExpressions.Regex.Replace(description, @"\s+", " ").Trim();

                return description;
            }

            return "";
        }

        /// <summary>
        /// Gets the description text for a designator (for orders/commands).
        /// </summary>
        private static string GetDesignatorDescriptionText(Designator designator)
        {
            if (designator == null)
                return "";

            string description = designator.Desc;
            if (!string.IsNullOrEmpty(description))
            {
                // Clean up the description - remove newlines and excess whitespace
                description = description.Replace("\n", " ").Replace("\r", " ");
                description = System.Text.RegularExpressions.Regex.Replace(description, @"\s+", " ").Trim();

                return description;
            }

            return "";
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
