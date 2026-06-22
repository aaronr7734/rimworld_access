using System;
using System.Collections.Generic;
using System.Linq;
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

            var rawLines = inspectString.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var formatted = new List<string>();

            foreach (var line in rawLines)
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

                formatted.Add(trimmed);
            }

            return string.Join("\n", formatted);
        }

        /// <summary>
        /// Translates a RimWorld keyed string for display. Falls back to an English default
        /// when no translation is registered so the label never reads as a raw key.
        /// </summary>
        private static string TranslateSyntheticName(string translationKey, string englishFallback)
        {
            if (string.IsNullOrEmpty(translationKey))
                return englishFallback;

            try
            {
                string translated = translationKey.Translate().ToString();
                if (!string.IsNullOrEmpty(translated) && translated != translationKey)
                    return translated;
            }
            catch
            {
                // Ignore and fall through
            }

            return englishFallback;
        }

        /// <summary>
        /// Gets a one-line summary description of an object.
        /// </summary>
        public static string GetObjectSummary(object obj)
        {
            if (obj == null) return "RimWorldAccess.Inspection.Summary.Unknown".Translate();

            if (obj is Pawn pawn)
            {
                string label = pawn.LabelCap.StripTags();

                // Determine status suffix key
                string statusKey;
                if (pawn.Dead)
                    statusKey = "RimWorldAccess.Inspection.Summary.PawnStatusDead";
                else if (pawn.Downed)
                    statusKey = "RimWorldAccess.Inspection.Summary.PawnStatusDowned";
                else if (pawn.Drafted)
                    statusKey = "RimWorldAccess.Inspection.Summary.PawnStatusDrafted";
                else
                    statusKey = null;

                // Prefix humanlikes with their translated kind (Colonist / Prisoner / Raider /
                // Pirate / etc.) so the user knows the role at a glance. Animals' LabelCap
                // already encodes the kind, so prefixing would read as "Muffalo: Muffalo".
                if (pawn.RaceProps.Humanlike)
                {
                    string kindLabel = pawn.KindLabel.CapitalizeFirst();
                    string displayName = (!string.IsNullOrEmpty(kindLabel) && !label.Equals(kindLabel, StringComparison.OrdinalIgnoreCase))
                        ? (string)"RimWorldAccess.Inspection.Summary.KindWithLabel".Translate(kindLabel, label)
                        : label;

                    return statusKey != null
                        ? (string)"RimWorldAccess.Inspection.Summary.WithStatus".Translate(displayName, statusKey.Translate())
                        : displayName;
                }

                return statusKey != null
                    ? (string)"RimWorldAccess.Inspection.Summary.WithStatus".Translate(label, statusKey.Translate())
                    : label;
            }

            if (obj is Building building)
            {
                return building.LabelCap.StripTags();
            }

            if (obj is Plant plant)
            {
                return plant.LabelCap.StripTags();
            }

            if (obj is Thing thing)
            {
                return thing.LabelCap.StripTags();
            }

            if (obj is Zone zone)
            {
                // Include zone type for clarity
                string zoneType = GetZoneTypeName(zone);
                return "RimWorldAccess.Inspection.Summary.Zone".Translate(zone.label, zoneType);
            }

            if (obj is Plan plan)
            {
                return "RimWorldAccess.Inspection.Summary.Plan".Translate(
                    PlanColorHelper.ColorName(plan.Color), plan.RenamableLabel);
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
                Name = TranslateSyntheticName("HealthOverview", "Overview"),
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
                    if (pawn.needs?.mood != null && !categories.Any(c => c.OriginalCategoryName == "Mood"))
                    {
                        categories.Add(new TabCategoryInfo
                        {
                            Name = TranslateSyntheticName("Mood", "Mood"),
                            Tab = null,
                            Handler = TabHandlerType.RichNavigation,
                            IsKnown = true,
                            OriginalCategoryName = "Mood"
                        });
                    }

                    // Add Skills category for humanlike pawns (part of Character tab in game)
                    if (pawn.RaceProps.Humanlike && pawn.skills?.skills != null && !categories.Any(c => c.OriginalCategoryName == "Skills"))
                    {
                        categories.Add(new TabCategoryInfo
                        {
                            Name = TranslateSyntheticName("Skills", "Skills"),
                            Tab = null,
                            Handler = TabHandlerType.RichNavigation,
                            IsKnown = true,
                            OriginalCategoryName = "Skills"
                        });
                    }

                    // Add Appearance category for humanlike pawns (hair / beard / tattoos / favorite
                    // color). Vanilla only exposes these through the visual styling station and
                    // portrait, so there is otherwise no way to review what a pawn looks like.
                    if (pawn.RaceProps.Humanlike && pawn.story != null && !categories.Any(c => c.OriginalCategoryName == "Appearance"))
                    {
                        categories.Add(new TabCategoryInfo
                        {
                            Name = TranslateSyntheticName("Appearance", "Appearance"),
                            Tab = null,
                            Handler = TabHandlerType.RichNavigation,
                            IsKnown = true,
                            OriginalCategoryName = "Appearance"
                        });
                    }

                    // Add Work Priorities for humanlike pawns
                    if (pawn.RaceProps.Humanlike && !categories.Any(c => c.OriginalCategoryName == "Work Priorities"))
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
                    if (pawn.jobs?.jobQueue?.Count > 0 && !categories.Any(c => c.OriginalCategoryName == "Job Queue"))
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
                    if (tempControl != null && !categories.Any(c => c.OriginalCategoryName == "Temperature"))
                    {
                        categories.Add(new TabCategoryInfo
                        {
                            Name = TranslateSyntheticName("Temperature", "Temperature"),
                            Tab = null,
                            Handler = TabHandlerType.Action,
                            IsKnown = true,
                            OriginalCategoryName = "Temperature"
                        });
                    }

                    // Bed Assignment (not a tab)
                    if (building is Building_Bed && !categories.Any(c => c.OriginalCategoryName == "Bed Assignment"))
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
                        if (assignComp != null && !categories.Any(c => c.OriginalCategoryName == "Owner Assignment"))
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
                        && !categories.Any(c => c.OriginalCategoryName == "Meditation Focus"))
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
                    if (building is IPlantToGrowSettable && !categories.Any(c => c.OriginalCategoryName == "Plant Selection"))
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

                    // Dynamically discovered components
                    var discoveredComponents = BuildingComponentsHelper.GetDiscoverableComponents(building);
                    foreach (var component in discoveredComponents.Where(cmp => !categories.Any(c => c.OriginalCategoryName == cmp.CategoryName)))
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
                    if (FacilityLinkHelper.HasFacilityComps(building) && !categories.Any(c => c.OriginalCategoryName == "Linked Facilities"))
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

            // Plans only get the synthetic Overview (added for every object above). Their management
            // actions (rename, change color, visibility, expand, shrink, delete) live on the G gizmo
            // key instead — see Building/PlanActionHelper.BuildGizmos.

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
                else if (obj is Plan plan)
                {
                    return GetPlanCategoryInfo(plan, category);
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
            if (pawn.Name != null)
            {
                lines.Add("RimWorldAccess.Inspection.Pawn.NameLabel".Translate(pawn.Name.ToStringFull));
            }

            // Age and birthday — shared with the InfoCard Character tab so the two never drift
            lines.AddRange(InfoCardDataExtractor.GetAgeInfo(pawn));

            // Xenotype (Biotech)
            var xenotype = InfoCardDataExtractor.GetXenotypeInfo(pawn);
            if (xenotype.HasValue)
            {
                lines.Add("RimWorldAccess.Inspection.Pawn.LabeledList".Translate(
                    "Xenotype".Translate(), xenotype.Value.xenotypeName));
            }

            // Ideoligion role (Ideology)
            var roleInfo = InfoCardDataExtractor.GetIdeologyRoleInfo(pawn);
            if (roleInfo.HasValue)
            {
                lines.Add("RimWorldAccess.Inspection.Pawn.LabeledList".Translate(
                    "RimWorldAccess.Inspection.InfoCardTree.Section.IdeologyRole".Translate(),
                    $"{roleInfo.Value.roleName} ({roleInfo.Value.ideoName})"));
            }

            // Royal titles (Royalty)
            foreach (var (title, faction, _) in InfoCardDataExtractor.GetRoyalTitlesInfo(pawn))
            {
                lines.Add("RimWorldAccess.Inspection.Pawn.LabeledList".Translate(
                    "RimWorldAccess.Inspection.InfoCardTree.Section.RoyalTitles".Translate(),
                    $"{title} ({faction})"));
            }

            if (pawn.story != null)
            {
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

                // Backstory title (e.g. "Test subject") — vanilla shows this only when a custom title is set
                if (!string.IsNullOrEmpty(pawn.story.title))
                {
                    lines.Add("RimWorldAccess.Inspection.Pawn.LabeledList".Translate(
                        "BackstoryTitle".Translate(), pawn.story.title));
                }

                // Traits
                if (pawn.story.traits?.allTraits != null && pawn.story.traits.allTraits.Any())
                {
                    lines.Add("RimWorldAccess.Inspection.Pawn.TraitsHeader".Translate());
                    foreach (var trait in pawn.story.traits.allTraits)
                    {
                        lines.Add("  " + FormatTraitLine(trait, pawn));
                    }
                }
            }

            // Incapable of (disabled work tags) — reuse the InfoCard extractor for identical data
            var incapable = InfoCardDataExtractor.GetIncapableWorkTagsInfo(pawn);
            if (incapable.Count > 0)
            {
                string tags = string.Join(", ", incapable.Select(t => t.tagLabel));
                lines.Add("RimWorldAccess.Inspection.Pawn.LabeledList".Translate(
                    "IncapableOf".Translate(), tags));
            }

            // Abilities shown on the character card
            var abilities = InfoCardDataExtractor.GetAbilitiesInfo(pawn);
            if (abilities.Count > 0)
            {
                string list = string.Join(", ", abilities.Select(a => a.label));
                lines.Add("RimWorldAccess.Inspection.Pawn.LabeledList".Translate(
                    "Abilities".Translate(), list));
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

                case "Linked Facilities":
                    return FacilityLinkHelper.GetInspectionInfo(building)
                        ?? (string)"RimWorldAccess.Inspection.Building.NoFacilityInfo".Translate();

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
            var lines = new List<string>();
            lines.Add(building.LabelCap.StripTags());
            lines.Add("");

            // Get the inspect string and format with punctuation for screen reader clarity
            string inspectString = building.GetInspectString();
            if (!string.IsNullOrEmpty(inspectString))
            {
                lines.Add(FormatInspectStringWithPunctuation(inspectString));
                lines.Add("");
            }

            // Health — skipped for indestructible buildings (geysers etc.) which
            // report HitPoints of -1. Vanilla gates on def.useHitPoints.
            if (building.def != null && building.def.useHitPoints &&
                building.HitPoints < building.MaxHitPoints)
            {
                float healthPercent = (float)building.HitPoints / building.MaxHitPoints;
                lines.Add("RimWorldAccess.Inspection.Building.Health"
                    .Translate(healthPercent.ToStringPercent(), building.HitPoints, building.MaxHitPoints));
            }

            // Add description for buildings
            if (building.def != null && !string.IsNullOrEmpty(building.def.description))
            {
                lines.Add("");
                lines.Add("RimWorldAccess.Inspection.DescriptionHeader".Translate("Description".Translate()));
                string description = building.def.description.StripTags().Trim();
                // Clean up whitespace
                description = System.Text.RegularExpressions.Regex.Replace(description, @"\s+", " ");
                lines.Add(description);
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Gets bills information for a workbench.
        /// </summary>
        private static string GetBuildingBillsInfo(Building building)
        {
            if (building is IBillGiver billGiver && billGiver.BillStack != null)
            {
                if (billGiver.BillStack.Count == 0)
                {
                    return "RimWorldAccess.Inspection.Building.NoBills".Translate();
                }

                var lines = new List<string>();
                lines.Add("RimWorldAccess.Inspection.Building.BillsHeader".Translate(billGiver.BillStack.Count));
                lines.Add("");

                int index = 1;
                foreach (var bill in billGiver.BillStack.Bills)
                {
                    lines.Add("RimWorldAccess.Inspection.Building.BillEntry"
                        .Translate(index, bill.LabelCap.StripTags()));

                    if (bill is Bill_Production productionBill)
                    {
                        if (productionBill.repeatMode == BillRepeatModeDefOf.RepeatCount)
                            lines.Add("RimWorldAccess.Inspection.Building.BillTarget".Translate(productionBill.repeatCount));
                        else if (productionBill.repeatMode == BillRepeatModeDefOf.TargetCount)
                            lines.Add("RimWorldAccess.Inspection.Building.BillTarget".Translate(productionBill.targetCount));
                        else
                            lines.Add("RimWorldAccess.Inspection.Building.BillMode".Translate(productionBill.repeatMode.label));
                    }

                    if (bill.suspended)
                        lines.Add("RimWorldAccess.Inspection.Building.BillSuspended".Translate("Suspended".Translate()));

                    lines.Add("");
                    index++;
                }

                return string.Join("\n", lines);
            }

            return "RimWorldAccess.Inspection.Building.NoBillsCapability".Translate();
        }

        /// <summary>
        /// Gets storage settings information for a storage building.
        /// </summary>
        private static string GetBuildingStorageInfo(Building building)
        {
            if (building is IStoreSettingsParent storeParent && storeParent.GetStoreSettings() != null)
            {
                var settings = storeParent.GetStoreSettings();
                var lines = new List<string>();

                lines.Add("RimWorldAccess.Inspection.Building.PriorityLine"
                    .Translate("Priority".Translate(), settings.Priority.ToString()));
                lines.Add("");

                // Get filter summary
                if (settings.filter != null)
                {
                    string summary = settings.filter.Summary;
                    if (!string.IsNullOrEmpty(summary))
                    {
                        lines.Add("RimWorldAccess.Inspection.Building.AllowedItemsHeader".Translate());
                        lines.Add(summary);
                    }
                    else
                    {
                        lines.Add("RimWorldAccess.Inspection.Building.NoItemsAllowed".Translate());
                    }
                }

                return string.Join("\n", lines);
            }

            return "RimWorldAccess.Inspection.Building.NoStorageSettings".Translate();
        }

        /// <summary>
        /// Gets bed assignment information for a bed.
        /// </summary>
        private static string GetBuildingBedAssignmentInfo(Building building)
        {
            if (building is Building_Bed bed)
            {
                var lines = new List<string>();

                // Show if it's for colonists, prisoners, slaves, or medical
                if (bed.ForPrisoners)
                    lines.Add("RimWorldAccess.Inspection.Building.PrisonBed".Translate());
                else if (bed.Medical)
                    lines.Add("RimWorldAccess.Inspection.Building.MedicalBed".Translate());
                else
                    lines.Add("RimWorldAccess.Inspection.Building.ColonistBed".Translate());

                lines.Add("");

                // Show current assignments
                if (bed.OwnersForReading != null && bed.OwnersForReading.Any())
                {
                    lines.Add("RimWorldAccess.Inspection.Building.AssignedToHeader".Translate());
                    foreach (var owner in bed.OwnersForReading)
                    {
                        lines.Add("RimWorldAccess.Inspection.Building.OwnerEntry".Translate(owner.LabelShort));
                    }
                }
                else
                {
                    lines.Add("RimWorldAccess.Inspection.Building.NotAssigned".Translate());
                }

                lines.Add("");
                lines.Add("RimWorldAccess.Inspection.Building.PressEnterAssign".Translate());

                return string.Join("\n", lines);
            }

            return "RimWorldAccess.Inspection.Building.NotABed".Translate();
        }

        /// <summary>
        /// Gets generic owner assignment information for non-bed buildings.
        /// </summary>
        private static string GetBuildingOwnerAssignmentInfo(Building building)
        {
            var comp = (building as ThingWithComps)?.TryGetComp<CompAssignableToPawn>();
            if (comp == null)
                return "RimWorldAccess.Inspection.Building.NoOwnerAssignment".Translate();

            var lines = new List<string>();

            if (comp.AssignedPawnsForReading.Count > 0)
            {
                lines.Add("RimWorldAccess.Inspection.Building.AssignedToHeader".Translate());
                foreach (var pawn in comp.AssignedPawnsForReading)
                {
                    lines.Add("RimWorldAccess.Inspection.Building.OwnerEntry".Translate(pawn.LabelShort));
                }
            }
            else
            {
                lines.Add("RimWorldAccess.Inspection.Building.NotAssigned".Translate());
            }

            lines.Add("");
            lines.Add("RimWorldAccess.Inspection.Building.PressEnterAssign".Translate());

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Gets meditation focus information for a meditation spot (fallback text).
        /// </summary>
        private static string GetMeditationFocusInfo(Building building)
        {
            if (!ModsConfig.RoyaltyActive || !building.Spawned)
                return "RimWorldAccess.Inspection.Building.NoMeditationInfo".Translate();

            return "RimWorldAccess.Inspection.Building.NoMeditationFocus"
                .Translate(MeditationUtility.FocusObjectSearchRadius.ToString("F0"));
        }

        /// <summary>
        /// Gets temperature control information for a cooler/heater.
        /// </summary>
        private static string GetBuildingTemperatureInfo(Building building)
        {
            var tempControl = building.TryGetComp<CompTempControl>();
            if (tempControl != null)
            {
                var lines = new List<string>();
                lines.Add("RimWorldAccess.Inspection.Building.TargetTemperature"
                    .Translate(MenuHelper.FormatTemperature(tempControl.targetTemperature, "F0")));

                // Check if it's powered
                var powerComp = building.TryGetComp<CompPowerTrader>();
                if (powerComp != null)
                {
                    lines.Add("RimWorldAccess.Inspection.Building.PowerStatus"
                        .Translate("Power".Translate(), powerComp.PowerOn ? "On".Translate() : "Off".Translate()));
                }

                lines.Add("");
                lines.Add("RimWorldAccess.Inspection.Building.PressEnterTemperature".Translate());

                return string.Join("\n", lines);
            }

            return "RimWorldAccess.Inspection.Building.NoTempControl".Translate();
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
                    return "RimWorldAccess.Inspection.Category.NotFound".Translate();
            }
        }

        /// <summary>
        /// Gets overview information for a plant.
        /// </summary>
        private static string GetPlantOverview(Plant plant)
        {
            var lines = new List<string>();
            lines.Add(plant.LabelCap.StripTags());
            lines.Add("");

            // Get the inspect string and format with punctuation for screen reader clarity
            string inspectString = plant.GetInspectString();
            if (!string.IsNullOrEmpty(inspectString))
            {
                lines.Add(FormatInspectStringWithPunctuation(inspectString));
            }

            // Add description for plants
            if (plant.def != null && !string.IsNullOrEmpty(plant.def.description))
            {
                lines.Add("");
                lines.Add("RimWorldAccess.Inspection.DescriptionHeader".Translate("Description".Translate()));
                string description = plant.def.description.StripTags().Trim();
                // Clean up whitespace
                description = System.Text.RegularExpressions.Regex.Replace(description, @"\s+", " ");
                lines.Add(description);
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Gets detailed growth information for a plant.
        /// </summary>
        private static string GetPlantGrowthInfo(Plant plant)
        {
            var lines = new List<string>();
            lines.Add("RimWorldAccess.Inspection.Plant.Growth".Translate(plant.Growth.ToStringPercent()));
            lines.Add("RimWorldAccess.Inspection.Plant.Lifespan"
                .Translate(plant.Age, plant.def.plant.LifespanTicks.TicksToDays().ToString("F1")));

            if (plant.Blighted)
                lines.Add("RimWorldAccess.Inspection.Plant.StatusBlighted".Translate());
            else if (plant.Dying)
                lines.Add("RimWorldAccess.Inspection.Plant.StatusDying".Translate());

            if (plant.HarvestableNow)
                lines.Add("RimWorldAccess.Inspection.Plant.ReadyHarvest".Translate());

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Gets a user-friendly zone type name.
        /// </summary>
        private static string GetZoneTypeName(Zone zone)
        {
            if (zone is Zone_Stockpile)
                return "RimWorldAccess.Inspection.Zone.TypeStockpile".Translate();
            if (zone is Zone_Growing)
                return "RimWorldAccess.Inspection.Zone.TypeGrowing".Translate();
            // Could add Zone_Fishing for Odyssey DLC if needed
            return "RimWorldAccess.Inspection.Zone.TypeGeneric".Translate();
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
                    return "RimWorldAccess.Inspection.Zone.NoPlantInfo".Translate();

                default:
                    return "RimWorldAccess.Inspection.Category.NotFound".Translate();
            }
        }

        /// <summary>
        /// Gets category information for a plan. Only the Overview is read-only text; the other plan
        /// categories are actions dispatched through ExecuteCategoryAction.
        /// </summary>
        private static string GetPlanCategoryInfo(Plan plan, string category)
        {
            switch (category)
            {
                case "Overview":
                    return GetPlanOverview(plan);
                default:
                    return "RimWorldAccess.Inspection.Category.NotFound".Translate();
            }
        }

        /// <summary>
        /// Gets overview information for a plan: its name, color, and RimWorld's own localized size
        /// inspect string ("Size: N", "Total planning area: M").
        /// </summary>
        private static string GetPlanOverview(Plan plan)
        {
            var lines = new List<string>();

            lines.Add("RimWorldAccess.Inspection.Summary.Plan".Translate(
                PlanColorHelper.ColorName(plan.Color), plan.RenamableLabel));

            if (plan.Hidden)
                lines.Add("RimWorldAccess.Building.Plan.Hidden".Translate());

            string inspectString = plan.GetInspectString();
            if (!string.IsNullOrWhiteSpace(inspectString))
            {
                lines.Add(FormatInspectStringWithPunctuation(inspectString));
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Gets overview information for a zone using RimWorld's localized GetInspectString.
        /// </summary>
        private static string GetZoneOverview(Zone zone)
        {
            var lines = new List<string>();

            // Zone name and type
            lines.Add(zone.label);

            // Get the inspect string from RimWorld (already localized)
            // Format with punctuation for screen reader clarity
            string inspectString = zone.GetInspectString();
            if (!string.IsNullOrWhiteSpace(inspectString))
            {
                lines.Add(FormatInspectStringWithPunctuation(inspectString));
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Gets plant information for a growing zone.
        /// </summary>
        private static string GetGrowingZonePlantInfo(Zone_Growing zone)
        {
            var lines = new List<string>();

            // Current plant type
            var plantDef = zone.GetPlantDefToGrow();
            if (plantDef != null)
            {
                lines.Add("RimWorldAccess.Inspection.Zone.PlantLabel".Translate(plantDef.LabelCap));

                // Growth time
                if (plantDef.plant != null)
                {
                    float growDays = plantDef.plant.growDays;
                    lines.Add("RimWorldAccess.Inspection.Zone.GrowthTime".Translate(growDays.ToString("F1")));

                    // Harvest yield if applicable
                    if (plantDef.plant.harvestedThingDef != null)
                    {
                        lines.Add("RimWorldAccess.Inspection.Zone.HarvestLabel"
                            .Translate(plantDef.plant.harvestedThingDef.LabelCap));
                    }
                }
            }
            else
            {
                lines.Add("RimWorldAccess.Inspection.Zone.NoPlantSelected".Translate());
            }

            // Sow and cut toggles
            lines.Add("RimWorldAccess.Inspection.Zone.AllowSow"
                .Translate(zone.allowSow ? "Yes".Translate() : "No".Translate()));
            lines.Add("RimWorldAccess.Inspection.Zone.AllowCut"
                .Translate(zone.allowCut ? "Yes".Translate() : "No".Translate()));

            return string.Join("\n", lines);
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
                    return "RimWorldAccess.Inspection.Category.NotFound".Translate();
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

            var lines = new List<string>();
            lines.Add(thing.LabelCap.StripTags());
            lines.Add("");

            // Stack count
            if (thing.stackCount > 1)
                lines.Add("RimWorldAccess.Inspection.Thing.Stack".Translate(thing.stackCount));

            // Get the inspect string and format with punctuation for screen reader clarity
            string inspectString = thing.GetInspectString();
            if (!string.IsNullOrEmpty(inspectString))
            {
                lines.Add(FormatInspectStringWithPunctuation(inspectString));
            }

            // Add description for items
            if (thing.def != null && !string.IsNullOrEmpty(thing.def.description))
            {
                lines.Add("");
                lines.Add("RimWorldAccess.Inspection.DescriptionHeader".Translate("Description".Translate()));
                string description = thing.def.description.StripTags().Trim();
                // Clean up whitespace
                description = System.Text.RegularExpressions.Regex.Replace(description, @"\s+", " ");
                lines.Add(description);
            }

            return string.Join("\n", lines);
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
