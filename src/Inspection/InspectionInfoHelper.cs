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
        /// Processes an inspect string to ensure each line ends with proper punctuation.
        /// RimWorld's GetInspectString() returns newline-separated stats that may lack punctuation.
        /// When newlines become spaces (for screen reader output), stats run together without this fix.
        /// </summary>
        private static string FormatInspectStringWithPunctuation(string inspectString)
        {
            if (string.IsNullOrEmpty(inspectString))
                return inspectString;

            var lines = inspectString.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new StringBuilder();

            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                // Check if line already ends with sentence-ending punctuation
                char lastChar = trimmed[trimmed.Length - 1];
                if (lastChar != '.' && lastChar != '!' && lastChar != '?' && lastChar != ':')
                {
                    trimmed += ".";
                }

                if (result.Length > 0)
                    result.AppendLine();
                result.Append(trimmed);
            }

            return result.ToString();
        }

        /// <summary>
        /// Gets a one-line summary description of an object.
        /// </summary>
        public static string GetObjectSummary(object obj)
        {
            if (obj == null) return "RimWorldAccess.Inspection.Summary.Unknown".Translate();

            if (obj is Pawn pawn)
            {
                bool isHumanlike = pawn.RaceProps.Humanlike;
                string label = pawn.LabelCap.StripTags();
                string statusKey;
                if (pawn.Dead)
                    statusKey = isHumanlike ? "RimWorldAccess.Inspection.Summary.PawnDead" : "RimWorldAccess.Inspection.Summary.AnimalDead";
                else if (pawn.Downed)
                    statusKey = isHumanlike ? "RimWorldAccess.Inspection.Summary.PawnDowned" : "RimWorldAccess.Inspection.Summary.AnimalDowned";
                else if (pawn.Drafted)
                    statusKey = isHumanlike ? "RimWorldAccess.Inspection.Summary.PawnDrafted" : "RimWorldAccess.Inspection.Summary.AnimalDrafted";
                else
                    statusKey = isHumanlike ? "RimWorldAccess.Inspection.Summary.Pawn" : "RimWorldAccess.Inspection.Summary.Animal";
                return statusKey.Translate(label);
            }

            if (obj is Building building)
            {
                return "RimWorldAccess.Inspection.Summary.Building".Translate(building.LabelCap.StripTags());
            }

            if (obj is Plant plant)
            {
                return "RimWorldAccess.Inspection.Summary.Plant".Translate(plant.LabelCap.StripTags());
            }

            if (obj is Thing thing)
            {
                return "RimWorldAccess.Inspection.Summary.Item".Translate(thing.LabelCap.StripTags());
            }

            if (obj is Zone zone)
            {
                // Include zone type for clarity
                string zoneType = GetZoneTypeName(zone);
                return "RimWorldAccess.Inspection.Summary.Zone".Translate(zone.label, zoneType);
            }

            return obj.ToString();
        }

        /// <summary>
        /// Gets dynamic categories for an object by discovering tabs from RimWorld's inspect system.
        /// This is the new dynamic approach that reads tabs from the game.
        /// </summary>
        public static List<TabCategoryInfo> GetDynamicCategories(object obj)
        {
            var categories = new List<TabCategoryInfo>();

            // Always add Overview first (synthetic category, not a real tab)
            categories.Add(new TabCategoryInfo
            {
                Name = "Overview",
                Tab = null,
                Handler = TabHandlerType.RichNavigation,
                IsKnown = true,
                OriginalCategoryName = "Overview"
            });

            // For Things (pawns, buildings, items), get tabs dynamically
            if (obj is Thing thing)
            {
                var tabCategories = TabRegistry.GetTabCategories(thing);
                categories.AddRange(tabCategories);

                // Add synthetic categories that aren't tabs but provide useful info
                if (obj is Pawn pawn)
                {
                    // Add Mood category (not a separate tab in RimWorld, but we show it)
                    if (pawn.needs?.mood != null && !categories.Any(c => c.Name == "Mood"))
                    {
                        categories.Add(new TabCategoryInfo
                        {
                            Name = "Mood",
                            Tab = null,
                            Handler = TabHandlerType.RichNavigation,
                            IsKnown = true,
                            OriginalCategoryName = "Mood"
                        });
                    }

                    // Add Skills category for humanlike pawns (part of Character tab in game)
                    if (pawn.RaceProps.Humanlike && pawn.skills?.skills != null && !categories.Any(c => c.Name == "Skills"))
                    {
                        categories.Add(new TabCategoryInfo
                        {
                            Name = "Skills",
                            Tab = null,
                            Handler = TabHandlerType.RichNavigation,
                            IsKnown = true,
                            OriginalCategoryName = "Skills"
                        });
                    }

                    // Add Work Priorities for humanlike pawns
                    if (pawn.RaceProps.Humanlike && !categories.Any(c => c.Name == "Work Priorities"))
                    {
                        categories.Add(new TabCategoryInfo
                        {
                            Name = "Work Priorities",
                            Tab = null,
                            Handler = TabHandlerType.BasicInspectString,
                            IsKnown = true,
                            OriginalCategoryName = "Work Priorities"
                        });
                    }

                    // Add Job Queue if there are queued jobs
                    if (pawn.jobs?.jobQueue?.Count > 0 && !categories.Any(c => c.Name == "Job Queue"))
                    {
                        categories.Add(new TabCategoryInfo
                        {
                            Name = "Job Queue",
                            Tab = null,
                            Handler = TabHandlerType.RichNavigation,
                            IsKnown = true,
                            OriginalCategoryName = "Job Queue"
                        });
                    }
                }

                // Add building-specific synthetic categories
                if (obj is Building building)
                {
                    // Temperature control (not a tab, but a component)
                    var tempControl = building.TryGetComp<CompTempControl>();
                    if (tempControl != null && !categories.Any(c => c.Name == "Temperature"))
                    {
                        categories.Add(new TabCategoryInfo
                        {
                            Name = "Temperature",
                            Tab = null,
                            Handler = TabHandlerType.Action,
                            IsKnown = true,
                            OriginalCategoryName = "Temperature"
                        });
                    }

                    // Bed Assignment (not a tab)
                    if (building is Building_Bed && !categories.Any(c => c.Name == "Bed Assignment"))
                    {
                        categories.Add(new TabCategoryInfo
                        {
                            Name = "Bed Assignment",
                            Tab = null,
                            Handler = TabHandlerType.Action,
                            IsKnown = true,
                            OriginalCategoryName = "Bed Assignment"
                        });
                    }

                    // Owner Assignment (non-bed buildings with CompAssignableToPawn)
                    if (!(building is Building_Bed))
                    {
                        var assignComp = building.TryGetComp<CompAssignableToPawn>();
                        if (assignComp != null && !categories.Any(c => c.Name == "Owner Assignment"))
                        {
                            categories.Add(new TabCategoryInfo
                            {
                                Name = "Owner Assignment",
                                Tab = null,
                                Handler = TabHandlerType.Action,
                                IsKnown = true,
                                OriginalCategoryName = "Owner Assignment"
                            });
                        }
                    }

                    // Meditation Focus (meditation spots with Royalty DLC)
                    if (building.def == ThingDefOf.MeditationSpot
                        && ModsConfig.RoyaltyActive
                        && !categories.Any(c => c.Name == "Meditation Focus"))
                    {
                        categories.Add(new TabCategoryInfo
                        {
                            Name = "Meditation Focus",
                            Tab = null,
                            Handler = TabHandlerType.RichNavigation,
                            IsKnown = true,
                            OriginalCategoryName = "Meditation Focus"
                        });
                    }

                    // Plant Selection for plant growers
                    if (building is IPlantToGrowSettable && !categories.Any(c => c.Name == "Plant Selection"))
                    {
                        categories.Add(new TabCategoryInfo
                        {
                            Name = "Plant Selection",
                            Tab = null,
                            Handler = TabHandlerType.Action,
                            IsKnown = true,
                            OriginalCategoryName = "Plant Selection"
                        });
                    }

                    // Power info
                    var powerComp = building.TryGetComp<CompPowerTrader>();
                    if (powerComp != null && !categories.Any(c => c.Name == "Power"))
                    {
                        categories.Add(new TabCategoryInfo
                        {
                            Name = "Power",
                            Tab = null,
                            Handler = TabHandlerType.BasicInspectString,
                            IsKnown = true,
                            OriginalCategoryName = "Power"
                        });
                    }

                    // Dynamically discovered components
                    var discoveredComponents = BuildingComponentsHelper.GetDiscoverableComponents(building);
                    foreach (var component in discoveredComponents.Where(cmp => !categories.Any(c => c.Name == cmp.CategoryName)))
                    {
                        categories.Add(new TabCategoryInfo
                        {
                            Name = component.CategoryName,
                            Tab = null,
                            Handler = component.IsReadOnly ? TabHandlerType.BasicInspectString : TabHandlerType.Action,
                            IsKnown = true,
                            OriginalCategoryName = component.CategoryName
                        });
                    }

                    // Facility linking (CompFacility / CompAffectedByFacilities)
                    if (FacilityLinkHelper.HasFacilityComps(building) && !categories.Any(c => c.Name == "Linked Facilities"))
                    {
                        categories.Add(new TabCategoryInfo
                        {
                            Name = "Linked Facilities",
                            Tab = null,
                            Handler = TabHandlerType.RichNavigation,
                            IsKnown = true,
                            OriginalCategoryName = "Linked Facilities"
                        });
                    }

                    // Pen marker rename
                    if (building.TryGetComp<CompAnimalPenMarker>() != null && !categories.Any(c => c.OriginalCategoryName == "Rename"))
                    {
                        categories.Add(new TabCategoryInfo
                        {
                            Name = "Rename".Translate().ToString(),
                            Tab = null,
                            Handler = TabHandlerType.Action,
                            IsKnown = true,
                            OriginalCategoryName = "Rename"
                        });
                    }

                }

                // Add growth info for plants
                if (obj is Plant)
                {
                    if (!categories.Any(c => c.Name == "Growth Info"))
                    {
                        categories.Add(new TabCategoryInfo
                        {
                            Name = "Growth Info",
                            Tab = null,
                            Handler = TabHandlerType.RichNavigation,
                            IsKnown = true,
                            OriginalCategoryName = "Growth Info"
                        });
                    }
                }

            }

            // Zone-specific categories
            if (obj is Zone zone)
            {
                // Add tabs dynamically discovered from zone's GetInspectTabs()
                // This includes ITab_Storage for Zone_Stockpile
                var zoneTabCategories = TabRegistry.GetZoneTabCategories(zone);
                categories.AddRange(zoneTabCategories);

                // Rename is a gizmo action, not a tab - add as synthetic category
                if (!categories.Any(c => c.OriginalCategoryName == "Rename"))
                {
                    categories.Add(new TabCategoryInfo
                    {
                        Name = "Rename".Translate().ToString(),
                        Tab = null,
                        Handler = TabHandlerType.Action,
                        IsKnown = true,
                        OriginalCategoryName = "Rename"
                    });
                }

                // Plant Info is a synthetic category for growing zones (not a real tab)
                if (zone is Zone_Growing && !categories.Any(c => c.OriginalCategoryName == "Plant Info"))
                {
                    categories.Add(new TabCategoryInfo
                    {
                        Name = "Plant Info",
                        Tab = null,
                        Handler = TabHandlerType.RichNavigation,
                        IsKnown = true,
                        OriginalCategoryName = "Plant Info"
                    });
                }
            }

            return categories;
        }

        /// <summary>
        /// Gets the list of available information categories for an object.
        /// This is the legacy method that returns simple string categories.
        /// Preserved for backward compatibility with existing code.
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

                // Owner Assignment for non-bed CompAssignableToPawn buildings
                if (!(building is Building_Bed) && building.TryGetComp<CompAssignableToPawn>() != null)
                    categories.Add("Owner Assignment");

                // Meditation Focus for meditation spots
                if (building.def == ThingDefOf.MeditationSpot && ModsConfig.RoyaltyActive)
                    categories.Add("Meditation Focus");

                // Check for temperature control (coolers, heaters, vents)
                var tempControl = building.TryGetComp<CompTempControl>();
                if (tempControl != null)
                    categories.Add("Temperature");

                // Check for storage
                if (building is IStoreSettingsParent || building is Building_Storage)
                    categories.Add("Storage");

                // Check for turrets with changeable projectiles (mortars)
                if (building is Building_TurretGun turretGun && turretGun.gun?.TryGetComp<CompChangeableProjectile>() != null)
                    categories.Add("Shells");

                // Check for plant grower (hydroponics basin, growing zones, etc.)
                if (building is IPlantToGrowSettable)
                    categories.Add("Plant Selection");

                // Check for power
                var powerComp = building.TryGetComp<CompPowerTrader>();
                if (powerComp != null)
                    categories.Add("Power");

                // Dynamically discover and add component categories
                var discoveredComponents = BuildingComponentsHelper.GetDiscoverableComponents(building);
                foreach (var component in discoveredComponents.Where(cmp => !categories.Contains(cmp.CategoryName)))
                {
                    categories.Add(component.CategoryName);
                }

            }
            else if (obj is Plant plant)
            {
                categories.Add("Overview");
                categories.Add("Growth Info");
            }
            else if (obj is Zone zone)
            {
                categories.Add("Overview");
                categories.Add("Rename".Translate().ToString());

                // Zone_Stockpile implements IStoreSettingsParent, so add Storage category
                if (zone is IStoreSettingsParent)
                    categories.Add("Storage");

                // Zone_Growing has plant settings
                if (zone is Zone_Growing)
                    categories.Add("Plant Info");
            }
            else if (obj is Thing)
            {
                categories.Add("Overview");
            }

            return categories;
        }

        /// <summary>
        /// Gets detailed information for a specific category of an object.
        /// </summary>
        public static string GetCategoryInfo(object obj, string category)
        {
            if (obj == null) return "RimWorldAccess.Inspection.Category.NoInfo".Translate();

            try
            {
                // Extract inner pawn from corpse, but NOT for Overview category
                // Overview should show corpse decay info from Corpse.GetInspectString()
                if (obj is Corpse corpse && category != "Overview")
                {
                    obj = corpse.InnerPawn;
                }

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
                else if (obj is Zone zone)
                {
                    return GetZoneCategoryInfo(zone, category);
                }
                else if (obj is Thing thing)
                {
                    return GetThingCategoryInfo(thing, category);
                }
            }
            catch (Exception ex)
            {
                return "RimWorldAccess.Inspection.Category.Error".Translate(category, ex.Message);
            }

            return "RimWorldAccess.Inspection.Category.NoCategoryInfo".Translate();
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
                    return GetPawnMoodInfo(pawn);

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
                    return "RimWorldAccess.Inspection.Pawn.NotPrisonerOrSlave".Translate();

                default:
                    // Try to get info from dynamic tab using GetInspectString as fallback
                    return GetDynamicTabInfo(pawn, category);
            }
        }

        /// <summary>
        /// Gets fallback information for a dynamic tab using GetInspectString().
        /// </summary>
        private static string GetDynamicTabInfo(Thing thing, string category)
        {
            if (thing == null)
                return "RimWorldAccess.Inspection.Category.NoInfo".Translate();

            // Try to find the matching tab
            var tabs = thing.GetInspectTabs();
            if (tabs != null)
            {
                foreach (var tab in tabs)
                {
                    if (tab == null || !tab.IsVisible)
                        continue;

                    string tabLabel = TabRegistry.GetCategoryNameForTab(tab);
                    if (tabLabel == category)
                    {
                        return TabRegistry.GetFallbackInfo(thing, tab);
                    }
                }
            }

            // If no matching tab found, use general inspect string
            string inspectString = thing.GetInspectString();
            if (!string.IsNullOrEmpty(inspectString))
                return inspectString;

            return "RimWorldAccess.Inspection.Category.NoFallback".Translate(category);
        }

        /// <summary>
        /// Gets mood information for a pawn (extracted from GetPawnCategoryInfo).
        /// </summary>
        private static string GetPawnMoodInfo(Pawn pawn)
        {
            if (pawn.needs?.mood == null)
                return "RimWorldAccess.Inspection.Pawn.NoMood".Translate();

            var lines = new List<string>();
            lines.Add("RimWorldAccess.Inspection.Pawn.MoodHeader"
                .Translate(pawn.needs.mood.CurLevelPercentage.ToStringPercent()));
            lines.Add("");

            List<Thought> thoughts = new List<Thought>();
            pawn.needs.mood.thoughts.GetAllMoodThoughts(thoughts);

            if (thoughts.Any())
            {
                lines.Add("RimWorldAccess.Inspection.Pawn.RecentThoughtsHeader".Translate());
                foreach (var thought in thoughts.Take(10))
                {
                    lines.Add("  " + (string)"RimWorldAccess.Inspection.Pawn.ThoughtEntry"
                        .Translate(thought.LabelCap.StripTags(),
                            thought.MoodOffset().ToString("+0.#;-0.#")));
                }
            }
            else
            {
                lines.Add("RimWorldAccess.Inspection.Pawn.NoThoughts".Translate());
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Gets overview information for a pawn.
        /// </summary>
        private static string GetPawnOverview(Pawn pawn)
        {
            var lines = new List<string>();
            lines.Add(pawn.LabelCap.StripTags());
            lines.Add("");

            // Get the inspect string (current activity, status)
            // This already includes age, gender, faction, equipped items, and current activity
            // Format with punctuation for screen reader clarity
            string inspectString = pawn.GetInspectString();
            if (!string.IsNullOrEmpty(inspectString))
            {
                lines.Add(FormatInspectStringWithPunctuation(inspectString));
            }

            // Add description for animals (humanlike pawns have backstories in Character category instead)
            if (!pawn.RaceProps.Humanlike && pawn.def != null && !string.IsNullOrEmpty(pawn.def.description))
            {
                lines.Add("");
                lines.Add("RimWorldAccess.Inspection.DescriptionHeader".Translate("Description".Translate()));
                string description = pawn.def.description.StripTags().Trim();
                // Clean up whitespace
                description = System.Text.RegularExpressions.Regex.Replace(description, @"\s+", " ");
                lines.Add(description);
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Gets character information (traits and backstory) for a pawn.
        /// </summary>
        private static string GetPawnCharacterInfo(Pawn pawn)
        {
            var lines = new List<string>();

            // Name information
            if (pawn.Name is NameTriple nameTriple)
            {
                lines.Add("RimWorldAccess.Inspection.Pawn.NameLabel".Translate(nameTriple.ToStringFull));
                lines.Add("");
            }
            else if (pawn.Name != null)
            {
                lines.Add("RimWorldAccess.Inspection.Pawn.NameLabel".Translate(pawn.Name.ToStringFull));
                lines.Add("");
            }

            if (pawn.story != null)
            {
                // Traits
                if (pawn.story.traits?.allTraits != null && pawn.story.traits.allTraits.Any())
                {
                    lines.Add("RimWorldAccess.Inspection.Pawn.TraitsHeader".Translate());
                    foreach (var trait in pawn.story.traits.allTraits)
                    {
                        lines.Add("  " + FormatTraitLine(trait, pawn));
                    }
                    lines.Add("");
                }

                // Backstory
                if (pawn.story.Childhood != null)
                {
                    string title = pawn.story.Childhood.TitleCapFor(pawn.gender);
                    string desc = CleanBackstoryDescription(pawn.story.Childhood.FullDescriptionFor(pawn).ToString());
                    lines.Add(string.IsNullOrEmpty(desc)
                        ? (string)"RimWorldAccess.Inspection.Pawn.Childhood".Translate(title)
                        : (string)"RimWorldAccess.Inspection.Pawn.ChildhoodWithDesc".Translate(title, desc));
                }
                if (pawn.story.Adulthood != null)
                {
                    string title = pawn.story.Adulthood.TitleCapFor(pawn.gender);
                    string desc = CleanBackstoryDescription(pawn.story.Adulthood.FullDescriptionFor(pawn).ToString());
                    lines.Add(string.IsNullOrEmpty(desc)
                        ? (string)"RimWorldAccess.Inspection.Pawn.Adulthood".Translate(title)
                        : (string)"RimWorldAccess.Inspection.Pawn.AdulthoodWithDesc".Translate(title, desc));
                }
            }

            return string.Join("\n", lines).Trim();
        }

        private static string FormatTraitLine(Trait trait, Pawn pawn)
        {
            string label = trait.LabelCap.StripTags();
            string tipString = trait.TipString(pawn);
            if (string.IsNullOrEmpty(tipString))
                return "RimWorldAccess.Inspection.Pawn.TraitOnly".Translate(label);

            tipString = tipString.StripTags();
            var tipLines = tipString.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (tipLines.Length == 0)
                return "RimWorldAccess.Inspection.Pawn.TraitOnly".Translate(label);

            string description = tipLines[0].Trim();

            var effects = new List<string>();
            for (int i = 1; i < tipLines.Length; i++)
            {
                string line = tipLines[i].Trim();
                if (!string.IsNullOrEmpty(line))
                    effects.Add(line);
            }

            if (effects.Count == 0)
                return "RimWorldAccess.Inspection.Pawn.TraitWithDesc".Translate(label, description);

            return "RimWorldAccess.Inspection.Pawn.TraitWithDescAndEffects"
                .Translate(label, description, string.Join(", ", effects));
        }

        private static string CleanBackstoryDescription(string raw)
        {
            string desc = raw.StripTags();
            if (string.IsNullOrEmpty(desc))
                return string.Empty;
            desc = desc.Replace("\r", "").Replace("\n", " ").Trim();
            return System.Text.RegularExpressions.Regex.Replace(desc, @"\s+", " ");
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

                case "Owner Assignment":
                    return GetBuildingOwnerAssignmentInfo(building);

                case "Meditation Focus":
                    return GetMeditationFocusInfo(building);

                case "Temperature":
                    return GetBuildingTemperatureInfo(building);

                case "Storage":
                    return GetBuildingStorageInfo(building);

                case "Power":
                    return GetBuildingPowerInfo(building);

                case "Linked Facilities":
                    return FacilityLinkHelper.GetInspectionInfo(building) ?? "No facility linking information.";

                default:
                    // Try to get info from dynamic tab using GetInspectString as fallback
                    return GetDynamicTabInfo(building, category);
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

            // Get the inspect string and format with punctuation for screen reader clarity
            string inspectString = building.GetInspectString();
            if (!string.IsNullOrEmpty(inspectString))
            {
                sb.AppendLine(FormatInspectStringWithPunctuation(inspectString));
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
        /// Gets generic owner assignment information for non-bed buildings.
        /// </summary>
        private static string GetBuildingOwnerAssignmentInfo(Building building)
        {
            var comp = (building as ThingWithComps)?.TryGetComp<CompAssignableToPawn>();
            if (comp == null)
                return "This building does not support owner assignment.";

            var sb = new StringBuilder();

            if (comp.AssignedPawnsForReading.Count > 0)
            {
                sb.AppendLine("Assigned to:");
                foreach (var pawn in comp.AssignedPawnsForReading)
                {
                    sb.AppendLine($"  {pawn.LabelShort}");
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

        /// <summary>
        /// Gets meditation focus information for a meditation spot (fallback text).
        /// </summary>
        private static string GetMeditationFocusInfo(Building building)
        {
            if (!ModsConfig.RoyaltyActive || !building.Spawned)
                return "No meditation focus information available.";

            return $"No meditation focus objects nearby. Place focus objects within {MeditationUtility.FocusObjectSearchRadius:F0} cells.";
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

                sb.AppendLine($"Target Temperature: {MenuHelper.FormatTemperature(tempControl.targetTemperature, "F0")}");

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

            // Get the inspect string and format with punctuation for screen reader clarity
            string inspectString = plant.GetInspectString();
            if (!string.IsNullOrEmpty(inspectString))
            {
                sb.AppendLine(FormatInspectStringWithPunctuation(inspectString));
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
        /// Gets a user-friendly zone type name.
        /// </summary>
        private static string GetZoneTypeName(Zone zone)
        {
            if (zone is Zone_Stockpile)
                return "Stockpile";
            if (zone is Zone_Growing)
                return "Growing Zone";
            // Could add Zone_Fishing for Odyssey DLC if needed
            return "Zone";
        }

        /// <summary>
        /// Gets category information for a zone.
        /// </summary>
        private static string GetZoneCategoryInfo(Zone zone, string category)
        {
            switch (category)
            {
                case "Overview":
                    return GetZoneOverview(zone);

                case "Plant Info":
                    if (zone is Zone_Growing growing)
                        return GetGrowingZonePlantInfo(growing);
                    return "No plant information available.";

                default:
                    return "Category not found.";
            }
        }

        /// <summary>
        /// Gets overview information for a zone using RimWorld's localized GetInspectString.
        /// </summary>
        private static string GetZoneOverview(Zone zone)
        {
            var sb = new StringBuilder();

            // Zone name and type
            sb.AppendLine(zone.label);

            // Get the inspect string from RimWorld (already localized)
            // Format with punctuation for screen reader clarity
            string inspectString = zone.GetInspectString();
            if (!string.IsNullOrWhiteSpace(inspectString))
            {
                sb.AppendLine(FormatInspectStringWithPunctuation(inspectString));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets plant information for a growing zone.
        /// </summary>
        private static string GetGrowingZonePlantInfo(Zone_Growing zone)
        {
            var sb = new StringBuilder();

            // Current plant type
            var plantDef = zone.GetPlantDefToGrow();
            if (plantDef != null)
            {
                sb.AppendLine($"Plant: {plantDef.LabelCap}");

                // Growth time
                if (plantDef.plant != null)
                {
                    float growDays = plantDef.plant.growDays;
                    sb.AppendLine($"Growth time: {growDays:F1} days");

                    // Harvest yield if applicable
                    if (plantDef.plant.harvestedThingDef != null)
                    {
                        sb.AppendLine($"Harvest: {plantDef.plant.harvestedThingDef.LabelCap}");
                    }
                }
            }
            else
            {
                sb.AppendLine("No plant selected");
            }

            // Sow and cut toggles
            sb.AppendLine($"Allow sow: {(zone.allowSow ? "Yes" : "No")}");
            sb.AppendLine($"Allow cut: {(zone.allowCut ? "Yes" : "No")}");

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

                default:
                    return "Category not found.";
            }
        }

        /// <summary>
        /// Gets overview information for a thing.
        /// </summary>
        private static string GetThingOverview(Thing thing)
        {
            // GeneSetHolderBase items need shade-aware gene labels in overview
            if (thing is GeneSetHolderBase geneHolder && geneHolder.GeneSet != null && ModsConfig.BiotechActive)
            {
                return GetGeneSetHolderOverview(geneHolder);
            }

            var sb = new StringBuilder();
            sb.AppendLine(thing.LabelCap.StripTags());
            sb.AppendLine();

            // Stack count
            if (thing.stackCount > 1)
                sb.AppendLine($"Stack: {thing.stackCount}");

            // Get the inspect string and format with punctuation for screen reader clarity
            string inspectString = thing.GetInspectString();
            if (!string.IsNullOrEmpty(inspectString))
            {
                sb.AppendLine(FormatInspectStringWithPunctuation(inspectString));
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
        /// Gets overview information for a GeneSetHolderBase item with shade-aware gene labels.
        /// Replaces the raw gene labels from GetInspectString() with descriptive shade names
        /// for skin color genes.
        /// </summary>
        private static string GetGeneSetHolderOverview(GeneSetHolderBase holder)
        {
            var lines = new List<string>();
            lines.Add(holder.LabelCap.StripTags());
            lines.Add("");

            // Get the full inspect string
            string inspectString = holder.GetInspectString();

            // Split at the "Genes:" header to separate non-gene info from gene list
            string genesHeader = "Genes".Translate().CapitalizeFirst() + ":";
            int headerIndex = inspectString?.IndexOf(genesHeader) ?? -1;

            if (headerIndex >= 0)
            {
                // Format the non-gene portion (component info, etc.)
                string preGenes = inspectString.Substring(0, headerIndex).Trim();
                if (!string.IsNullOrEmpty(preGenes))
                {
                    lines.Add(FormatInspectStringWithPunctuation(preGenes));
                }

                // Build our own gene section with shade-aware labels
                var genes = holder.GeneSet.GenesListForReading;
                if (genes != null && genes.Count > 0)
                {
                    lines.Add(genesHeader);
                    int cap = Math.Min(5, genes.Count);
                    for (int i = 0; i < cap; i++)
                    {
                        string geneLabel = GeneTreeBuilder.GetGeneDisplayLabel(genes[i]);
                        lines.Add(holder.GeneSet.IsOverridden(genes[i])
                            ? (string)"RimWorldAccess.Inspection.GeneHolder.GeneEntryOverridden"
                                .Translate(geneLabel, "Overridden".Translate())
                            : (string)"RimWorldAccess.Inspection.GeneHolder.GeneEntry".Translate(geneLabel));
                    }
                    if (genes.Count > cap)
                    {
                        lines.Add("RimWorldAccess.Inspection.GeneHolder.Etc".Translate("Etc".Translate()));
                    }
                }
            }
            else
            {
                // No gene section found - just format the whole string
                if (!string.IsNullOrEmpty(inspectString))
                {
                    lines.Add(FormatInspectStringWithPunctuation(inspectString));
                }
            }

            // Add description
            if (holder.def != null && !string.IsNullOrEmpty(holder.def.description))
            {
                lines.Add("");
                lines.Add("RimWorldAccess.Inspection.DescriptionHeader".Translate("Description".Translate()));
                string description = holder.def.description.StripTags().Trim();
                description = System.Text.RegularExpressions.Regex.Replace(description, @"\s+", " ");
                lines.Add(description);
            }

            return string.Join("\n", lines);
        }
    }
}
