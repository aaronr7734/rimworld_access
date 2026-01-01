using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper class for extracting inspection information for various object types.
    /// Provides category lists and detailed information for pawns, animals, buildings, items, and plants.
    /// </summary>
    public static class InspectionInfoHelper
    {
        /// <summary>
        /// Gets a one-line summary description of an object.
        /// </summary>
        public static string GetObjectSummary(object obj)
        {
            if (obj == null) return "Unknown object";

            if (obj is Pawn pawn)
            {
                string pawnType = pawn.RaceProps.Humanlike ? "Pawn" : "Animal";
                string status = "";

                if (pawn.Dead)
                    status = " (Dead)";
                else if (pawn.Downed)
                    status = " (Downed)";
                else if (pawn.Drafted)
                    status = " (Drafted)";

                return $"{pawnType}: {pawn.LabelCap.StripTags()}{status}";
            }

            if (obj is Building building)
            {
                return $"Building: {building.LabelCap.StripTags()}";
            }

            if (obj is Plant plant)
            {
                return $"Plant: {plant.LabelCap.StripTags()}";
            }

            if (obj is Thing thing)
            {
                return $"Item: {thing.LabelCap.StripTags()}";
            }

            if (obj is Zone zone)
            {
                return $"Zone: {zone.label}";
            }

            return obj.ToString();
        }

        /// <summary>
        /// Gets the list of available information categories for an object.
        /// </summary>
        public static List<string> GetAvailableCategories(object obj)
        {
            var categories = new List<string>();

            if (obj is Pawn pawn)
            {
                categories.Add("Overview");
                categories.Add("Health");

                if (pawn.RaceProps.Humanlike)
                {
                    categories.Add("Needs");
                    categories.Add("Mood");
                    categories.Add("Gear");
                    categories.Add("Skills");
                    categories.Add("Social");
                    categories.Add("Character");
                    categories.Add("Work Priorities");
                    categories.Add("Log");

                    // Add Job Queue category if there are queued jobs
                    if (pawn.jobs?.jobQueue?.Count > 0)
                    {
                        categories.Add("Job Queue");
                    }

                    // Add Prisoner category for prisoners and slaves
                    if (pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony)
                    {
                        categories.Add("Prisoner");
                    }
                }
                else // Animal
                {
                    categories.Add("Needs");
                    if (pawn.training != null)
                        categories.Add("Training");
                }
            }
            else if (obj is Building building)
            {
                categories.Add("Overview");

                // Check for bills (workbench)
                if (building is IBillGiver)
                    categories.Add("Bills");

                // Check for bed assignment
                if (building is Building_Bed)
                    categories.Add("Bed Assignment");

                // Check for temperature control (coolers, heaters, vents)
                var tempControl = building.TryGetComp<CompTempControl>();
                if (tempControl != null)
                    categories.Add("Temperature");

                // Check for storage
                if (building is IStoreSettingsParent || building is Building_Storage)
                    categories.Add("Storage");

                // Check for plant grower (hydroponics basin, growing zones, etc.)
                if (building is IPlantToGrowSettable)
                    categories.Add("Plant Selection");

                // Check for power
                var powerComp = building.TryGetComp<CompPowerTrader>();
                if (powerComp != null)
                    categories.Add("Power");

                // Dynamically discover and add component categories
                var discoveredComponents = BuildingComponentsHelper.GetDiscoverableComponents(building);
                foreach (var component in discoveredComponents)
                {
                    // Only add if not already covered by specialized menus above
                    if (!categories.Contains(component.CategoryName))
                    {
                        categories.Add(component.CategoryName);
                    }
                }

                // Add stats category for all buildings
                categories.Add("Stats");
            }
            else if (obj is Plant plant)
            {
                categories.Add("Overview");
                categories.Add("Growth Info");
                categories.Add("Stats");
            }
            else if (obj is Thing)
            {
                categories.Add("Overview");
                categories.Add("Quality & Stats");
            }

            return categories;
        }

        /// <summary>
        /// Gets detailed information for a specific category of an object.
        /// </summary>
        public static string GetCategoryInfo(object obj, string category)
        {
            if (obj == null) return "No information available.";

            try
            {
                if (obj is Pawn pawn)
                {
                    return GetPawnCategoryInfo(pawn, category);
                }
                else if (obj is Building building)
                {
                    return GetBuildingCategoryInfo(building, category);
                }
                else if (obj is Plant plant)
                {
                    return GetPlantCategoryInfo(plant, category);
                }
                else if (obj is Thing thing)
                {
                    return GetThingCategoryInfo(thing, category);
                }
            }
            catch (Exception ex)
            {
                return $"Error retrieving {category} information: {ex.Message}";
            }

            return "No information available for this category.";
        }

        /// <summary>
        /// Gets category information for a pawn (colonist or animal).
        /// </summary>
        private static string GetPawnCategoryInfo(Pawn pawn, string category)
        {
            switch (category)
            {
                case "Overview":
                    return GetPawnOverview(pawn);

                case "Health":
                    return PawnInfoHelper.GetHealthInfo(pawn);

                case "Needs":
                    return PawnInfoHelper.GetNeedsInfo(pawn);

                case "Mood":
                    if (pawn.needs?.mood != null)
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine($"Mood: {pawn.needs.mood.CurLevelPercentage:P0}");
                        sb.AppendLine();

                        // Get mood thoughts
                        List<Thought> thoughts = new List<Thought>();
                        pawn.needs.mood.thoughts.GetAllMoodThoughts(thoughts);

                        if (thoughts.Any())
                        {
                            sb.AppendLine("Recent Thoughts:");
                            foreach (var thought in thoughts.Take(10))
                            {
                                sb.AppendLine($"  {thought.LabelCap.StripTags()}: {thought.MoodOffset():+0.#;-0.#}");
                            }
                        }
                        else
                        {
                            sb.AppendLine("No significant thoughts.");
                        }

                        return sb.ToString();
                    }
                    return "No mood information available.";

                case "Gear":
                    return PawnInfoHelper.GetGearInfo(pawn);

                case "Skills":
                    return PawnInfoHelper.GetCharacterInfo(pawn); // Includes skills

                case "Social":
                    return PawnInfoHelper.GetSocialInfo(pawn);

                case "Character":
                    return GetPawnCharacterInfo(pawn);

                case "Training":
                    return PawnInfoHelper.GetTrainingInfo(pawn);

                case "Work Priorities":
                    return PawnInfoHelper.GetWorkInfo(pawn);

                case "Prisoner":
                    if (pawn.IsPrisonerOfColony)
                    {
                        return PrisonerTabHelper.GetPrisonerInfo(pawn);
                    }
                    else if (pawn.IsSlaveOfColony)
                    {
                        return PrisonerTabHelper.GetSlaveInfo(pawn);
                    }
                    return "Not a prisoner or slave.";

                default:
                    return "Category not found.";
            }
        }

        /// <summary>
        /// Gets overview information for a pawn.
        /// </summary>
        private static string GetPawnOverview(Pawn pawn)
        {
            var sb = new StringBuilder();
            sb.AppendLine(pawn.LabelCap.StripTags());
            sb.AppendLine();

            // Get the inspect string (current activity, status)
            // This already includes age, gender, faction, equipped items, and current activity
            string inspectString = pawn.GetInspectString();
            if (!string.IsNullOrEmpty(inspectString))
            {
                sb.AppendLine(inspectString);
            }

            // Add description for animals (humanlike pawns have backstories in Character category instead)
            if (!pawn.RaceProps.Humanlike && pawn.def != null && !string.IsNullOrEmpty(pawn.def.description))
            {
                sb.AppendLine();
                sb.AppendLine("Description:");
                string description = pawn.def.description.StripTags().Trim();
                // Clean up whitespace
                description = System.Text.RegularExpressions.Regex.Replace(description, @"\s+", " ");
                sb.AppendLine(description);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets character information (traits and backstory) for a pawn.
        /// </summary>
        private static string GetPawnCharacterInfo(Pawn pawn)
        {
            var sb = new StringBuilder();

            // Name information
            if (pawn.Name is NameTriple nameTriple)
            {
                sb.AppendLine($"Name: {nameTriple.ToStringFull}");
                sb.AppendLine();
            }
            else if (pawn.Name != null)
            {
                sb.AppendLine($"Name: {pawn.Name}");
                sb.AppendLine();
            }

            if (pawn.story != null)
            {
                // Traits
                if (pawn.story.traits?.allTraits != null && pawn.story.traits.allTraits.Any())
                {
                    sb.AppendLine("Traits:");
                    foreach (var trait in pawn.story.traits.allTraits)
                    {
                        // Get trait name
                        sb.Append($"  {trait.LabelCap.StripTags()}");

                        // Get trait description and effects using TipString
                        string tipString = trait.TipString(pawn);
                        if (!string.IsNullOrEmpty(tipString))
                        {
                            // Strip tags and extract description and effects
                            tipString = tipString.StripTags();

                            // Split into lines and format
                            var lines = tipString.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                            if (lines.Length > 0)
                            {
                                // First line is usually the description
                                sb.Append($": {lines[0].Trim()}");

                                // Add effects if present (skip empty lines and the description)
                                var effects = new List<string>();
                                for (int i = 1; i < lines.Length; i++)
                                {
                                    string line = lines[i].Trim();
                                    if (!string.IsNullOrEmpty(line))
                                    {
                                        effects.Add(line);
                                    }
                                }

                                if (effects.Any())
                                {
                                    sb.Append(": " + string.Join(", ", effects));
                                }
                            }
                        }

                        sb.AppendLine();
                    }
                    sb.AppendLine();
                }

                // Backstory
                if (pawn.story.Childhood != null)
                {
                    sb.Append($"Childhood: {pawn.story.Childhood.TitleCapFor(pawn.gender)}");

                    // Add description and effects
                    string childDesc = pawn.story.Childhood.FullDescriptionFor(pawn).ToString().StripTags();
                    if (!string.IsNullOrEmpty(childDesc))
                    {
                        // Clean up the description (remove extra whitespace)
                        childDesc = childDesc.Replace("\r", "").Replace("\n", " ").Trim();
                        // Remove redundant info
                        childDesc = System.Text.RegularExpressions.Regex.Replace(childDesc, @"\s+", " ");
                        sb.Append($": {childDesc}");
                    }
                    sb.AppendLine();
                }
                if (pawn.story.Adulthood != null)
                {
                    sb.Append($"Adulthood: {pawn.story.Adulthood.TitleCapFor(pawn.gender)}");

                    // Add description and effects
                    string adultDesc = pawn.story.Adulthood.FullDescriptionFor(pawn).ToString().StripTags();
                    if (!string.IsNullOrEmpty(adultDesc))
                    {
                        // Clean up the description (remove extra whitespace)
                        adultDesc = adultDesc.Replace("\r", "").Replace("\n", " ").Trim();
                        // Remove redundant info
                        adultDesc = System.Text.RegularExpressions.Regex.Replace(adultDesc, @"\s+", " ");
                        sb.Append($": {adultDesc}");
                    }
                    sb.AppendLine();
                }
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Gets category information for a building.
        /// </summary>
        private static string GetBuildingCategoryInfo(Building building, string category)
        {
            switch (category)
            {
                case "Overview":
                    return GetBuildingOverview(building);

                case "Bills":
                    return GetBuildingBillsInfo(building);

                case "Bed Assignment":
                    return GetBuildingBedAssignmentInfo(building);

                case "Temperature":
                    return GetBuildingTemperatureInfo(building);

                case "Storage":
                    return GetBuildingStorageInfo(building);

                case "Power":
                    return GetBuildingPowerInfo(building);

                case "Stats":
                    return GetBuildingStatsInfo(building);

                default:
                    return "Category not found.";
            }
        }

        /// <summary>
        /// Gets overview information for a building.
        /// </summary>
        private static string GetBuildingOverview(Building building)
        {
            var sb = new StringBuilder();
            sb.AppendLine(building.LabelCap.StripTags());
            sb.AppendLine();

            // Get the inspect string
            string inspectString = building.GetInspectString();
            if (!string.IsNullOrEmpty(inspectString))
            {
                sb.AppendLine(inspectString);
                sb.AppendLine();
            }

            // Health
            if (building.HitPoints < building.MaxHitPoints)
            {
                float healthPercent = (float)building.HitPoints / building.MaxHitPoints;
                sb.AppendLine($"Health: {healthPercent:P0} ({building.HitPoints} / {building.MaxHitPoints})");
            }

            // Add description for buildings
            if (building.def != null && !string.IsNullOrEmpty(building.def.description))
            {
                sb.AppendLine();
                sb.AppendLine("Description:");
                string description = building.def.description.StripTags().Trim();
                // Clean up whitespace
                description = System.Text.RegularExpressions.Regex.Replace(description, @"\s+", " ");
                sb.AppendLine(description);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets bills information for a workbench.
        /// </summary>
        private static string GetBuildingBillsInfo(Building building)
        {
            if (building is IBillGiver billGiver && billGiver.BillStack != null)
            {
                var sb = new StringBuilder();

                if (billGiver.BillStack.Count == 0)
                {
                    return "No bills queued.";
                }

                sb.AppendLine($"Bills ({billGiver.BillStack.Count}):");
                sb.AppendLine();

                int index = 1;
                foreach (var bill in billGiver.BillStack.Bills)
                {
                    sb.AppendLine($"{index}. {bill.LabelCap.StripTags()}");

                    if (bill is Bill_Production productionBill)
                    {
                        if (productionBill.repeatMode == BillRepeatModeDefOf.RepeatCount)
                            sb.AppendLine($"   Target: {productionBill.repeatCount}");
                        else if (productionBill.repeatMode == BillRepeatModeDefOf.TargetCount)
                            sb.AppendLine($"   Target: {productionBill.targetCount}");
                        else
                            sb.AppendLine($"   Mode: {productionBill.repeatMode.label}");
                    }

                    if (bill.suspended)
                        sb.AppendLine("   (Suspended)");

                    sb.AppendLine();
                    index++;
                }

                return sb.ToString();
            }

            return "This building does not have bills.";
        }

        /// <summary>
        /// Gets storage settings information for a storage building.
        /// </summary>
        private static string GetBuildingStorageInfo(Building building)
        {
            if (building is IStoreSettingsParent storeParent && storeParent.GetStoreSettings() != null)
            {
                var settings = storeParent.GetStoreSettings();
                var sb = new StringBuilder();

                sb.AppendLine($"Priority: {settings.Priority}");
                sb.AppendLine();

                // Get filter summary
                if (settings.filter != null)
                {
                    string summary = settings.filter.Summary;
                    if (!string.IsNullOrEmpty(summary))
                    {
                        sb.AppendLine("Allowed items:");
                        sb.AppendLine(summary);
                    }
                    else
                    {
                        sb.AppendLine("No items allowed.");
                    }
                }

                return sb.ToString();
            }

            return "This building does not have storage settings.";
        }

        /// <summary>
        /// Gets power information for a powered building.
        /// </summary>
        private static string GetBuildingPowerInfo(Building building)
        {
            var powerComp = building.TryGetComp<CompPowerTrader>();
            if (powerComp != null)
            {
                var sb = new StringBuilder();

                sb.AppendLine($"Power: {(powerComp.PowerOn ? "On" : "Off")}");
                sb.AppendLine($"Consumption: {powerComp.PowerOutput} W");

                if (!powerComp.PowerOn)
                {
                    sb.AppendLine();
                    sb.AppendLine("Power is currently off or unavailable.");
                }

                return sb.ToString();
            }

            return "This building does not use power.";
        }


        /// <summary>
        /// Gets all stats information for a building using RimWorld's native stat system.
        /// </summary>
        private static string GetBuildingStatsInfo(Building building)
        {
            var sb = new StringBuilder();

            // Get all stats using RimWorld's native stat system
            List<StatDrawEntry> stats = StatsHelper.GetAllStats(building);

            if (stats != null && stats.Count > 0)
            {
                // Format stats grouped by category
                string formattedStats = StatsHelper.FormatStatsForScreenReader(stats);
                sb.Append(formattedStats);
            }
            else
            {
                // Fallback: if no stats found, show basic info
                sb.AppendLine("No stats available for this building.");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets bed assignment information for a bed.
        /// </summary>
        private static string GetBuildingBedAssignmentInfo(Building building)
        {
            if (building is Building_Bed bed)
            {
                var sb = new StringBuilder();

                // Show if it's for colonists, prisoners, slaves, or medical
                if (bed.ForPrisoners)
                    sb.AppendLine("Type: Prison Bed");
                else if (bed.Medical)
                    sb.AppendLine("Type: Medical Bed");
                else
                    sb.AppendLine("Type: Colonist Bed");

                sb.AppendLine();

                // Show current assignments
                if (bed.OwnersForReading != null && bed.OwnersForReading.Any())
                {
                    sb.AppendLine("Assigned to:");
                    foreach (var owner in bed.OwnersForReading)
                    {
                        sb.AppendLine($"  {owner.LabelShort}");
                    }
                }
                else
                {
                    sb.AppendLine("Not assigned to anyone");
                }

                sb.AppendLine();
                sb.AppendLine("Press Enter to change assignments");

                return sb.ToString();
            }

            return "This building is not a bed.";
        }

        /// <summary>
        /// Gets temperature control information for a cooler/heater.
        /// </summary>
        private static string GetBuildingTemperatureInfo(Building building)
        {
            var tempControl = building.TryGetComp<CompTempControl>();
            if (tempControl != null)
            {
                var sb = new StringBuilder();

                sb.AppendLine($"Target Temperature: {tempControl.targetTemperature}°C");

                // Check if it's powered
                var powerComp = building.TryGetComp<CompPowerTrader>();
                if (powerComp != null)
                {
                    sb.AppendLine($"Power: {(powerComp.PowerOn ? "On" : "Off")}");
                }

                sb.AppendLine();
                sb.AppendLine("Press Enter to adjust temperature");

                return sb.ToString();
            }

            return "This building does not have temperature control.";
        }

        /// <summary>
        /// Gets category information for a plant.
        /// </summary>
        private static string GetPlantCategoryInfo(Plant plant, string category)
        {
            switch (category)
            {
                case "Overview":
                    return GetPlantOverview(plant);

                case "Growth Info":
                    return GetPlantGrowthInfo(plant);

                case "Stats":
                    return GetPlantStatsInfo(plant);

                default:
                    return "Category not found.";
            }
        }

        /// <summary>
        /// Gets overview information for a plant.
        /// </summary>
        private static string GetPlantOverview(Plant plant)
        {
            var sb = new StringBuilder();
            sb.AppendLine(plant.LabelCap.StripTags());
            sb.AppendLine();

            // Get the inspect string
            string inspectString = plant.GetInspectString();
            if (!string.IsNullOrEmpty(inspectString))
            {
                sb.AppendLine(inspectString);
            }

            // Add description for plants
            if (plant.def != null && !string.IsNullOrEmpty(plant.def.description))
            {
                sb.AppendLine();
                sb.AppendLine("Description:");
                string description = plant.def.description.StripTags().Trim();
                // Clean up whitespace
                description = System.Text.RegularExpressions.Regex.Replace(description, @"\s+", " ");
                sb.AppendLine(description);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets detailed growth information for a plant.
        /// </summary>
        private static string GetPlantGrowthInfo(Plant plant)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"Growth: {plant.Growth:P0}");
            sb.AppendLine($"Lifespan: {plant.Age} / {plant.def.plant.LifespanTicks.TicksToDays():F1} days");

            if (plant.Blighted)
                sb.AppendLine("Status: Blighted");
            else if (plant.Dying)
                sb.AppendLine("Status: Dying");

            if (plant.HarvestableNow)
                sb.AppendLine("Ready to harvest!");

            return sb.ToString();
        }



        /// <summary>
        /// Gets all stats information for a plant using RimWorld's native stat system.
        /// </summary>
        private static string GetPlantStatsInfo(Plant plant)
        {
            var sb = new StringBuilder();

            // Get all stats using RimWorld's native stat system
            List<StatDrawEntry> stats = StatsHelper.GetAllStats(plant);

            if (stats != null && stats.Count > 0)
            {
                // Format stats grouped by category
                string formattedStats = StatsHelper.FormatStatsForScreenReader(stats);
                sb.Append(formattedStats);
            }
            else
            {
                // Fallback: if no stats found, show basic info
                sb.AppendLine("No stats available for this plant.");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets category information for a generic thing (item).
        /// </summary>
        private static string GetThingCategoryInfo(Thing thing, string category)
        {
            switch (category)
            {
                case "Overview":
                    return GetThingOverview(thing);

                case "Quality & Stats":
                    return GetThingQualityInfo(thing);

                default:
                    return "Category not found.";
            }
        }

        /// <summary>
        /// Gets overview information for a thing.
        /// </summary>
        private static string GetThingOverview(Thing thing)
        {
            var sb = new StringBuilder();
            sb.AppendLine(thing.LabelCap.StripTags());
            sb.AppendLine();

            // Stack count
            if (thing.stackCount > 1)
                sb.AppendLine($"Stack: {thing.stackCount}");

            // Get the inspect string
            string inspectString = thing.GetInspectString();
            if (!string.IsNullOrEmpty(inspectString))
            {
                sb.AppendLine(inspectString);
            }

            // Add description for items
            if (thing.def != null && !string.IsNullOrEmpty(thing.def.description))
            {
                sb.AppendLine();
                sb.AppendLine("Description:");
                string description = thing.def.description.StripTags().Trim();
                // Clean up whitespace
                description = System.Text.RegularExpressions.Regex.Replace(description, @"\s+", " ");
                sb.AppendLine(description);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets quality and stats information for a thing.
        /// Uses RimWorld's native stat system to display all relevant stats.
        /// </summary>
        private static string GetThingQualityInfo(Thing thing)
        {
            var sb = new StringBuilder();

            // Quality (display at top if applicable)
            var qualityComp = thing.TryGetComp<CompQuality>();
            if (qualityComp != null)
            {
                sb.AppendLine($"Quality: {qualityComp.Quality}");
                sb.AppendLine();
            }

            // Material (display before stats if applicable)
            if (thing.Stuff != null)
            {
                sb.AppendLine($"Material: {thing.Stuff.LabelCap.ToString().StripTags()}");
                sb.AppendLine();
            }

            // Get all stats using RimWorld's native stat system
            List<StatDrawEntry> stats = StatsHelper.GetAllStats(thing);

            if (stats != null && stats.Count > 0)
            {
                // Format stats grouped by category
                string formattedStats = StatsHelper.FormatStatsForScreenReader(stats);
                sb.Append(formattedStats);
            }
            else
            {
                // Fallback: if no stats found, show basic info
                sb.AppendLine($"Market Value: {thing.MarketValue:F0} silver");
                sb.AppendLine($"Mass: {thing.GetStatValue(StatDefOf.Mass):F2} kg");
            }

            return sb.ToString();
        }
    }
}
