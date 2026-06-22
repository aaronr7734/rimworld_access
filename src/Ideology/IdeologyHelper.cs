using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared utility methods for building ideology data used by IdeologyTabState.
    /// Provides data extraction, announcement building, and tree construction.
    /// </summary>
    public static class IdeologyHelper
    {
        /// <summary>
        /// Returns all visible ideologies in the game's standard view order.
        /// </summary>
        public static List<Ideo> BuildIdeologyList()
        {
            return Find.IdeoManager.IdeosInViewOrder.ToList();
        }

        /// <summary>
        /// Builds the announcement string for an ideology in the list panel.
        /// Shows name and which factions hold it.
        /// </summary>
        public static string BuildIdeoListAnnouncement(Ideo ideo)
        {
            var sb = new StringBuilder();
            sb.Append(ideo.name);

            // Show which factions use this ideology
            var factionParts = new List<string>();
            if (Find.World != null)
            {
                foreach (Faction faction in Find.FactionManager.AllFactionsInViewOrder)
                {
                    if (faction.Hidden || faction.ideos == null)
                        continue;

                    if (faction.ideos.IsPrimary(ideo))
                        factionParts.Add("RimWorldAccess.Ideology.List.FactionPrimarySuffix".Translate(faction.Name));
                    else if (faction.ideos.IsMinor(ideo))
                        factionParts.Add("RimWorldAccess.Ideology.List.FactionMinorSuffix".Translate(faction.Name));
                }
            }

            if (factionParts.Count > 0)
                AppendSentence(sb, "IdeoligionOf".Translate() + " " + string.Join(", ", factionParts));

            return sb.ToString();
        }

        /// <summary>
        /// Builds the full tree structure for one ideology's details.
        /// Root is hidden (IndentLevel -1). Level 0 nodes are section headers.
        /// Each section aggregates its children's text into its own label.
        /// </summary>
        public static InspectionTreeItem BuildIdeologyTree(Ideo ideo)
        {
            var root = new InspectionTreeItem
            {
                Label = ideo.name,
                IndentLevel = -1,
                IsExpandable = true,
                IsExpanded = true
            };

            BuildOverviewSection(root, ideo);
            BuildStyleCategoriesSection(root, ideo);
            BuildFactionsSection(root, ideo);

            if (ideo.Fluid && ideo.development != null)
                BuildFluidSection(root, ideo);

            BuildMemesSection(root, ideo);
            BuildNarrativeSection(root, ideo);
            BuildDeitiesSection(root, ideo);

            // Precept categories matching vanilla's DoPrecepts order
            BuildPreceptCategorySection(root, ideo, "Precepts".Translate(),
                p => p.preceptClass == typeof(Precept));
            BuildPreceptCategorySection(root, ideo, "IdeoRoles".Translate(),
                p => typeof(Precept_Role).IsAssignableFrom(p.preceptClass));
            BuildPreceptCategorySection(root, ideo, "Rituals".Translate(),
                p => p.preceptClass == typeof(Precept_Ritual));
            BuildPreceptCategorySection(root, ideo, "IdeoBuildings".Translate(),
                p => p.preceptClass == typeof(Precept_Building) || p.preceptClass == typeof(Precept_RitualSeat));
            BuildPreceptCategorySection(root, ideo, "IdeoRelics".Translate(),
                p => p.preceptClass == typeof(Precept_Relic));
            BuildPreceptCategorySection(root, ideo, "IdeoWeapons".Translate(),
                p => p.preceptClass == typeof(Precept_Weapon));
            BuildPreceptCategorySection(root, ideo, "VeneratedAnimals".Translate(),
                p => p.preceptClass == typeof(Precept_Animal));

            if (ModsConfig.BiotechActive)
            {
                BuildPreceptCategorySection(root, ideo, "PreferredXenotypes".Translate(),
                    p => p.preceptClass == typeof(Precept_Xenotype));
            }

            BuildPreceptCategorySection(root, ideo, "IdeoApparel".Translate(),
                p => p.preceptClass == typeof(Precept_Apparel));

            BuildAppearanceSection(root, ideo);

            return root;
        }

        #region Section Builders

        private static void BuildOverviewSection(InspectionTreeItem root, Ideo ideo)
        {
            var children = new List<string>();

            children.Add("Adjective".Translate().CapitalizeFirst() + ": " + ideo.adjective.CapitalizeFirst());
            children.Add("IdeoMembers".Translate().CapitalizeFirst() + ": " + ideo.memberName.CapitalizeFirst());

            MemeDef structureMeme = ideo.StructureMeme;
            if (structureMeme != null)
                children.Add("StructureMeme".Translate().CapitalizeFirst() + ": " + structureMeme.LabelCap);

            if (ideo.culture != null)
                children.Add("Culture".Translate().CapitalizeFirst() + ": " + ideo.culture.LabelCap);

            if (!string.IsNullOrEmpty(ideo.leaderTitleMale))
            {
                string leaderEntry = "LeaderTitle".Translate().CapitalizeFirst() + ": " + ideo.leaderTitleMale.CapitalizeFirst();
                if (!string.IsNullOrEmpty(ideo.leaderTitleFemale) && ideo.leaderTitleFemale != ideo.leaderTitleMale)
                    leaderEntry += " (" + ideo.leaderTitleFemale.CapitalizeFirst() + ")";
                children.Add(leaderEntry);
            }

            children.Add("WorshipRoom".Translate().CapitalizeFirst() + ": " + ideo.WorshipRoomLabel.CapitalizeFirst());

            var sectionNode = CreateSectionNode(root, ideo.name, children);
            root.Children.Add(sectionNode);
        }

        private static void BuildStyleCategoriesSection(InspectionTreeItem root, Ideo ideo)
        {
            if (ideo.thingStyleCategories.NullOrEmpty())
                return;

            var selectedStyles = ideo.thingStyleCategories;

            // Build child data for each style category
            var catDataList = new List<(string name, List<string> childTexts, bool hasSound)>();
            foreach (var styleCatWithPriority in selectedStyles)
            {
                StyleCategoryDef cat = styleCatWithPriority.category;
                var childTexts = new List<string>();
                var overrides = new Dictionary<StyleCategoryDef, List<string>>();

                // New buildables
                var buildables = new List<string>();
                if (!cat.addDesignators.NullOrEmpty())
                    foreach (var bd in cat.addDesignators)
                        buildables.Add(bd.LabelCap.Resolve());
                if (!cat.addDesignatorGroups.NullOrEmpty())
                    foreach (var group in cat.addDesignatorGroups)
                        buildables.Add(group.LabelCap.Resolve());
                buildables.Sort();
                if (buildables.Count > 0)
                    childTexts.Add("IdeoMakesBuildingBuildable".Translate() + ": " + string.Join(", ", buildables));

                // Styled items/objects with override tracking
                var styledThings = new List<string>();
                foreach (var tds in cat.thingDefStyles)
                {
                    ThingDef td = tds.ThingDef;
                    if (!td.canGenerateDefaultDesignator && (cat.addDesignators.NullOrEmpty() || !cat.addDesignators.Contains(td)))
                        continue;

                    StyleCategoryDef overriddenBy = null;
                    foreach (var higherStyle in selectedStyles)
                    {
                        if (higherStyle.category == cat)
                            break;
                        if (higherStyle.category.thingDefStyles.Any(s => s.ThingDef == td))
                        {
                            overriddenBy = higherStyle.category;
                            break;
                        }
                    }

                    if (overriddenBy != null)
                    {
                        if (!overrides.ContainsKey(overriddenBy))
                            overrides[overriddenBy] = new List<string>();
                        overrides[overriddenBy].Add(td.LabelCap.Resolve());
                    }
                    else
                    {
                        styledThings.Add(td.LabelCap.Resolve());
                    }
                }
                styledThings.Sort();
                if (styledThings.Count > 0)
                    childTexts.Add("StyleCategoryDetails".Translate(cat.LabelCap).Resolve() + ": " + string.Join(", ", styledThings));

                bool hasActiveRitualSound = cat.soundOngoingRitual != null
                    && cat.soundOngoingRitual == ideo.SoundOngoingRitual;

                // Override entries
                foreach (var kvp in overrides.OrderBy(o => selectedStyles.ToList().FindIndex(s => s.category == o.Key)))
                {
                    kvp.Value.Sort();
                    childTexts.Add("OverriddenByStyle".Translate(kvp.Key.LabelCap).Resolve() + ": " + string.Join(", ", kvp.Value));
                }

                catDataList.Add((cat.LabelCap.Resolve(), childTexts, hasActiveRitualSound));
            }

            // Single style: flatten — children go directly under the section node
            if (catDataList.Count == 1)
            {
                var (name, childTexts, hasSound) = catDataList[0];
                var sectionLabel = new StringBuilder("Styles".Translate() + ": " + name);
                foreach (string childText in childTexts)
                    AppendSentence(sectionLabel, childText);

                var sectionNode = new InspectionTreeItem
                {
                    Label = sectionLabel.ToString(),
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = root,
                    Type = InspectionTreeItem.ItemType.Category
                };

                foreach (string childText in childTexts)
                    AddChildNode(sectionNode, childText);
                if (hasSound)
                    AddChildNode(sectionNode, "RitualAmbienceSound".Translate().Resolve() + "RimWorldAccess.Ideology.Styles.EnterToPreview".Translate(), ideo.SoundOngoingRitual);

                root.Children.Add(sectionNode);
                return;
            }

            // Multiple styles: nested structure
            var multiSectionNode = new InspectionTreeItem
            {
                IndentLevel = 0,
                IsExpandable = true,
                IsExpanded = false,
                Parent = root,
                Type = InspectionTreeItem.ItemType.Category
            };

            var catLabels = new List<string>();
            var ritualSoundStyleNames = new List<string>();

            for (int i = 0; i < catDataList.Count; i++)
            {
                var (name, childTexts, hasSound) = catDataList[i];
                var catHeader = new StringBuilder(name);
                foreach (string childText in childTexts)
                    AppendSentence(catHeader, childText);

                var catNode = new InspectionTreeItem
                {
                    Label = catHeader.ToString(),
                    IndentLevel = 1,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = multiSectionNode,
                    Type = InspectionTreeItem.ItemType.SubCategory
                };

                foreach (string childText in childTexts)
                    AddChildNode(catNode, childText);
                if (hasSound)
                {
                    AddChildNode(catNode, "RitualAmbienceSound".Translate().Resolve() + "RimWorldAccess.Ideology.Styles.EnterToPreview".Translate(), ideo.SoundOngoingRitual);
                    ritualSoundStyleNames.Add(name);
                }

                multiSectionNode.Children.Add(catNode);
                catLabels.Add(catHeader.ToString());
            }

            var sb = new StringBuilder("Styles".Translate());
            foreach (string catLabel in catLabels)
                AppendSentence(sb, catLabel);

            if (ritualSoundStyleNames.Count > 0)
            {
                string names = string.Join(", ", ritualSoundStyleNames);
                int count = ritualSoundStyleNames.Count;
                string hint = count == 1
                    ? "RimWorldAccess.Ideology.Styles.RitualSoundHintOne".Translate(names).ToString()
                    : "RimWorldAccess.Ideology.Styles.RitualSoundHintMany".Translate(names, count).ToString();
                AppendSentence(sb, hint);
            }

            multiSectionNode.Label = sb.ToString();
            root.Children.Add(multiSectionNode);
        }

        private static void BuildFactionsSection(InspectionTreeItem root, Ideo ideo)
        {
            if (Find.World == null)
                return;

            var factionEntries = new List<string>();
            foreach (Faction faction in Find.FactionManager.AllFactionsInViewOrder)
            {
                if (faction.Hidden || faction.ideos == null)
                    continue;

                if (faction.ideos.IsPrimary(ideo))
                    factionEntries.Add("RimWorldAccess.Ideology.FactionsSection.PrimaryEntry".Translate(faction.Name));
                else if (faction.ideos.IsMinor(ideo))
                    factionEntries.Add("RimWorldAccess.Ideology.FactionsSection.MinorEntry".Translate(faction.Name));
            }

            if (factionEntries.Count == 0)
                return;

            var sectionNode = CreateSectionNode(root, "IdeoligionOf".Translate().CapitalizeFirst(), factionEntries);
            root.Children.Add(sectionNode);
        }

        private static void BuildFluidSection(InspectionTreeItem root, Ideo ideo)
        {
            string points = ideo.development.Points + " / " + ideo.development.NextReformationDevelopmentPoints;
            bool canReform = ideo.development.CanReformNow;

            // The section title carries the live points (and, when reformable, the action hint) so
            // landing on the node leads with the actionable info. The explanatory tip and the list
            // of ways to earn points live in the children — expand (Right) to read them. Enter on
            // the node reforms directly when possible (no expand-then-arrow); the points are NOT
            // repeated as a child (that produced a "current points, current points" double).
            string title = "CurrentDevelopmentPoints".Translate().CapitalizeFirst() + ": " + points;
            if (canReform)
                title += ". " + (string)"RimWorldAccess.Ideology.PressEnterToReform".Translate();

            var children = new List<string>
            {
                "FluidIdeoTip".Translate().Resolve().StripTags(),
            };

            // Ways to earn development points.
            var earnMethods = new StringBuilder();
            earnMethods.Append("FluidIdeoTipGetPoints".Translate().Resolve().StripTags() + ": ");
            earnMethods.Append("FluidIdeoTipGetPoinsByConversion".Translate().Resolve().StripTags());

            var rituals = new List<Precept_Ritual>();
            IdeoDevelopmentUtility.GetAllRitualsThatGiveDevelopmentPoints(ideo, rituals);
            foreach (var ritual in rituals)
                earnMethods.Append(", " + ritual.LabelCap + " (" + "Ritual".Translate() + ")");

            var questEvents = new List<HistoryEventDef>();
            IdeoDevelopmentUtility.GetAllQuestSuccessEventsThatGiveDevelopmentPoints(ideo, questEvents);
            foreach (var questEvent in questEvents)
                earnMethods.Append(", " + questEvent.LabelCap + " (" + "QuestLower".Translate() + ")");

            children.Add(earnMethods.ToString());

            var sectionNode = CreateSectionNode(root, title, children);

            // When enough development points are earned, the node itself becomes the Reform action
            // (Enter activates it via IdeologyTreeNavigation.HandleActivate); Right still expands to
            // read the tip. Below the threshold it's a plain info node.
            if (canReform)
                sectionNode.Data = new IdeoReformState.ReformActionMarker { Ideo = ideo };

            root.Children.Add(sectionNode);
        }

        private static void BuildMemesSection(InspectionTreeItem root, Ideo ideo)
        {
            var nonStructureMemes = ideo.memes.Where(m => m.category != MemeCategory.Structure).ToList();
            if (nonStructureMemes.Count == 0)
                return;

            var sectionNode = new InspectionTreeItem
            {
                IndentLevel = 0,
                IsExpandable = true,
                IsExpanded = false,
                Parent = root,
                Type = InspectionTreeItem.ItemType.Category
            };

            var memeLabels = new List<string>();

            foreach (MemeDef meme in nonStructureMemes)
            {
                // Build header: name + impact
                var memeHeader = new StringBuilder(meme.LabelCap.Resolve());
                if (meme.impact > 0)
                    memeHeader.Append(", " + "IdeoImpact".Translate() + ": " + IdeoImpactUtility.MemeImpactLabel(meme.impact).CapitalizeFirst());

                // Build structured child lines
                var childLines = BuildMemeDetailLines(meme, ideo);

                if (childLines.Count > 0)
                {
                    // Build aggregated label: header + all children
                    var memeLabel = new StringBuilder(memeHeader.ToString());
                    foreach (string child in childLines)
                        AppendSentence(memeLabel, child);

                    var memeNode = new InspectionTreeItem
                    {
                        Label = memeLabel.ToString(),
                        IndentLevel = 1,
                        IsExpandable = true,
                        IsExpanded = false,
                        Parent = sectionNode,
                        Type = InspectionTreeItem.ItemType.SubCategory,
                        // Carry the MemeDef so Alt+I opens its info card (the walker climbs to here
                        // from the detail-line children too). MemeDef : Def.
                        Data = meme
                    };

                    foreach (string child in childLines)
                        AddChildNode(memeNode, child);

                    sectionNode.Children.Add(memeNode);
                    memeLabels.Add(memeLabel.ToString());
                }
                else
                {
                    AddChildNode(sectionNode, memeHeader.ToString(), meme);
                    memeLabels.Add(memeHeader.ToString());
                }
            }

            // Section label aggregates all memes' full content
            var sb = new StringBuilder("Memes".Translate());
            foreach (string memeLabel in memeLabels)
                AppendSentence(sb, memeLabel);
            sectionNode.Label = sb.ToString();

            root.Children.Add(sectionNode);
        }

        /// <summary>
        /// Builds individual detail lines for a meme, each becoming a child node.
        /// Matches the order of vanilla's IdeoUIUtility.GetMemeTip().
        /// </summary>
        private static List<string> BuildMemeDetailLines(MemeDef meme, Ideo ideo)
        {
            var lines = new List<string>();

            if (!string.IsNullOrEmpty(meme.description))
                lines.Add(meme.description);

            // Required precepts (from requireOne with single entries)
            if (!meme.requireOne.NullOrEmpty())
            {
                var required = new List<string>();
                foreach (var preceptList in meme.requireOne)
                {
                    if (preceptList.Count == 1)
                        required.Add(preceptList[0].issue.LabelCap + ": " + preceptList[0].LabelCap);
                }
                if (required.Count > 0)
                    lines.Add("RequiredPrecepts".Translate() + ": " + string.Join(", ", required));

                // "Requires one of" groups
                foreach (var preceptList in meme.requireOne)
                {
                    if (preceptList.Count > 1)
                    {
                        var options = preceptList.Select(p => p.issue.LabelCap + ": " + p.LabelCap);
                        lines.Add("RequiresOnePrecept".Translate() + ": " + string.Join(", ", options));
                    }
                }
            }

            // Chance-to-have precepts
            if (meme.selectOneOrNone != null && meme.selectOneOrNone.preceptThingPairs.Count > 0)
            {
                var items = meme.selectOneOrNone.preceptThingPairs
                    .Select(p => p.thing.LabelCap.Resolve() + ": " + p.precept.LabelCap.Resolve());
                lines.Add("ChanceToHavePrecept".Translate() + ": " + string.Join(", ", items));
            }

            // Required rituals
            if (!meme.requiredRituals.NullOrEmpty())
            {
                var items = meme.requiredRituals.Select(r =>
                    r.pattern.shortDescOverride?.CapitalizeFirst() ?? r.precept.LabelCap.Resolve());
                lines.Add("RequiredRituals".Translate() + ": " + string.Join(", ", items));
            }

            // Unlocked roles
            List<string> unlockedRoles = meme.UnlockedRoles(ideo);
            if (!unlockedRoles.NullOrEmpty())
                lines.Add("MemeUnlocksRoles".Translate() + ": " + string.Join(", ", unlockedRoles));

            // Unlocked rituals
            List<string> unlockedRituals = meme.UnlockedRituals();
            if (!unlockedRituals.NullOrEmpty())
                lines.Add("MemeUnlocksRituals".Translate() + ": " + string.Join(", ", unlockedRituals));

            // Style categories
            if (!meme.thingStyleCategories.NullOrEmpty())
            {
                var items = meme.thingStyleCategories.Select(sc => sc.category.LabelCap.Resolve());
                lines.Add("Styles".Translate() + ": " + string.Join(", ", items));
            }

            // Unlocked recipes
            var unlockedRecipes = DefDatabase<RecipeDef>.AllDefsListForReading
                .Where(r => r.memePrerequisitesAny != null && r.memePrerequisitesAny.Contains(meme))
                .Select(r => r.ProducedThingDef)
                .Where(td => td != null)
                .Distinct()
                .Select(td => td.LabelCap.Resolve())
                .OrderBy(s => s)
                .ToList();
            if (unlockedRecipes.Count > 0)
                lines.Add("UnlockedRecipes".Translate().CapitalizeFirst() + ": " + string.Join(", ", unlockedRecipes));

            // Buildable designators
            {
                var buildables = new List<string>();
                if (!meme.addDesignators.NullOrEmpty())
                    foreach (var bd in meme.addDesignators)
                        buildables.Add(bd.LabelCap.Resolve());
                if (!meme.addDesignatorGroups.NullOrEmpty())
                    foreach (var group in meme.addDesignatorGroups)
                        buildables.Add(group.LabelCap.Resolve());
                buildables.Sort();
                if (buildables.Count > 0)
                    lines.Add("IdeoMakesBuildingBuildable".Translate() + ": " + string.Join(", ", buildables));
            }

            // Starting research
            if (!meme.startingResearchProjects.NullOrEmpty())
            {
                var items = meme.startingResearchProjects.Select(r => r.LabelCap.Resolve()).OrderBy(s => s);
                lines.Add("IdeoStartWithResearch".Translate() + ": " + string.Join(", ", items));
            }

            // Agreeable traits
            if (!meme.agreeableTraits.NullOrEmpty())
            {
                var items = meme.agreeableTraits.Select(x =>
                    (!x.degree.HasValue ? x.def.degreeDatas.First().label : x.def.DataAtDegree(x.degree.Value).label).CapitalizeFirst());
                lines.Add("AgreeableTraits".Translate() + ": " + string.Join(", ", items));
            }

            // Disagreeable traits
            if (!meme.disagreeableTraits.NullOrEmpty())
            {
                var items = meme.disagreeableTraits.Select(x =>
                    (!x.degree.HasValue ? x.def.degreeDatas.First().label : x.def.DataAtDegree(x.degree.Value).label).CapitalizeFirst());
                lines.Add("DisagreeableTraits".Translate() + ": " + string.Join(", ", items));
            }

            // Conflicting precepts prevented
            var preventedPrecepts = DefDatabase<PreceptDef>.AllDefsListForReading
                .Where(p => p.conflictingMemes != null && p.conflictingMemes.Contains(meme))
                .Select(p => p.issue.LabelCap.Resolve() + ": " + p.LabelCap.Resolve())
                .ToList();
            if (preventedPrecepts.Count > 0)
                lines.Add("PreventsPrecepts".Translate() + ": " + string.Join(", ", preventedPrecepts));

            return lines;
        }

        private static void BuildNarrativeSection(InspectionTreeItem root, Ideo ideo)
        {
            if (string.IsNullOrEmpty(ideo.description))
                return;

            string narrativeLabel = "CoreNarrative".Translate().CapitalizeFirst();
            string[] lines = ideo.description.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length <= 1)
            {
                // Single line: simple expandable node
                var sectionNode = CreateSectionNode(root, narrativeLabel,
                    new List<string> { ideo.description.Trim() });
                root.Children.Add(sectionNode);
            }
            else
            {
                // Multi-line: expandable node with line children
                var sectionNode = new InspectionTreeItem
                {
                    Label = narrativeLabel + ". " + ideo.description.Replace("\r", "").Replace("\n", " ").Trim(),
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = root,
                    Type = InspectionTreeItem.ItemType.Category
                };

                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        AddChildNode(sectionNode, trimmed);
                }

                root.Children.Add(sectionNode);
            }
        }

        private static void BuildDeitiesSection(InspectionTreeItem root, Ideo ideo)
        {
            if (!(ideo.foundation is IdeoFoundation_Deity deityFoundation))
                return;

            var deities = deityFoundation.DeitiesListForReading;
            if (deities == null || deities.Count == 0)
                return;

            var sectionNode = new InspectionTreeItem
            {
                IndentLevel = 0,
                IsExpandable = true,
                IsExpanded = false,
                Parent = root,
                Type = InspectionTreeItem.ItemType.Category
            };

            var aggregatedParts = new List<string>();

            foreach (var deity in deities)
            {
                string deityInfo = BuildDeityAnnouncement(deity);
                aggregatedParts.Add(deityInfo);
                AddChildNode(sectionNode, deityInfo);
            }

            // Section label: "Deities. [deity1 info]. [deity2 info]..."
            var sb = new StringBuilder("Deities".Translate());
            foreach (string part in aggregatedParts)
                AppendSentence(sb, part);
            sectionNode.Label = sb.ToString();

            root.Children.Add(sectionNode);
        }

        private static string BuildDeityAnnouncement(IdeoFoundation_Deity.Deity deity)
        {
            var sb = new StringBuilder();
            sb.Append(deity.name);

            if (!string.IsNullOrEmpty(deity.type))
                AppendSentence(sb, deity.type);

            if (deity.gender != Gender.None)
                AppendSentence(sb, deity.gender.GetLabel().CapitalizeFirst());

            if (deity.relatedMeme != null)
                AppendSentence(sb, "RelatedToMeme".Translate() + ": " + deity.relatedMeme.LabelCap);

            return sb.ToString();
        }

        private static void BuildPreceptCategorySection(InspectionTreeItem root, Ideo ideo,
            string categoryLabel, Func<PreceptDef, bool> filter)
        {
            var matchingPrecepts = new List<Precept>();
            foreach (Precept precept in ideo.PreceptsListForReading)
            {
                if (precept.def.visible && filter(precept.def))
                    matchingPrecepts.Add(precept);
            }

            if (matchingPrecepts.Count == 0)
                return;

            // Single-precept optimization: flatten to avoid redundant nesting
            // e.g., "Weapons → Weapons: Noble and despised" becomes just
            // "Weapons: Noble and despised" directly at level 0
            if (matchingPrecepts.Count == 1)
            {
                var precept = matchingPrecepts[0];
                string tipLabel = precept.TipLabel.StripTags();
                string fullTip = precept.GetTip().StripTags();

                if (!precept.def.grantedAbilities.NullOrEmpty())
                    fullTip = EnhanceWithAbilityDescriptions(precept.def.grantedAbilities, precept.ideo, fullTip);

                Dictionary<string, AbilityDef> abilityDefMap = null;
                if (!precept.def.grantedAbilities.NullOrEmpty())
                    abilityDefMap = BuildAbilityDefMap(precept.def.grantedAbilities, precept.ideo);

                Def preceptDef = GetPreceptDef(precept);

                string[] rawTipLines = fullTip.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                var tipLines = ConsolidateTipLines(rawTipLines);

                if (tipLines.Count > 1)
                {
                    string flatTipLabel = tipLabel.Replace("\r", "").Replace("\n", " ").Trim();
                    string flatFullTip = fullTip.Replace("\r", "").Replace("\n", " ").Trim();
                    var combinedSb = new StringBuilder(flatTipLabel);
                    AppendSentence(combinedSb, flatFullTip);

                    var flatNode = new InspectionTreeItem
                    {
                        Label = combinedSb.ToString(),
                        IndentLevel = 0,
                        IsExpandable = true,
                        IsExpanded = false,
                        Parent = root,
                        Type = InspectionTreeItem.ItemType.Category,
                        Data = preceptDef
                    };

                    foreach (string line in tipLines)
                    {
                        string trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed))
                            continue;

                        object childData = GetChildData(trimmed, precept, abilityDefMap);
                        string label = trimmed.StripTags();

                        // Append apparel description for required apparel items
                        if (childData is ThingDef td && td.IsApparel && !string.IsNullOrEmpty(td.description))
                            label += ". " + td.description;

                        AddChildNode(flatNode, label, childData);
                    }

                    root.Children.Add(flatNode);
                    return;
                }
            }

            var sectionNode = new InspectionTreeItem
            {
                IndentLevel = 0,
                IsExpandable = true,
                IsExpanded = false,
                Parent = root,
                Type = InspectionTreeItem.ItemType.Category
            };

            var aggregatedParts = new List<string>();

            foreach (Precept precept in matchingPrecepts)
            {
                string tipLabel = precept.TipLabel.StripTags();
                string fullTip = precept.GetTip().StripTags();

                // Enhance role ability lines with descriptions
                if (!precept.def.grantedAbilities.NullOrEmpty())
                    fullTip = EnhanceWithAbilityDescriptions(precept.def.grantedAbilities, precept.ideo, fullTip);

                // Build ability Def mapping for attaching to child nodes
                Dictionary<string, AbilityDef> abilityDefMap = null;
                if (!precept.def.grantedAbilities.NullOrEmpty())
                    abilityDefMap = BuildAbilityDefMap(precept.def.grantedAbilities, precept.ideo);

                Def preceptDef = GetPreceptDef(precept);

                // Flatten before combining — TipLabel can be multi-line for rituals
                string flatTipLabel = tipLabel.Replace("\r", "").Replace("\n", " ").Trim();
                string flatFullTip = fullTip.Replace("\r", "").Replace("\n", " ").Trim();
                var combinedSb = new StringBuilder(flatTipLabel);
                if (!string.IsNullOrEmpty(flatFullTip))
                    AppendSentence(combinedSb, flatFullTip);
                string combinedText = combinedSb.ToString();

                aggregatedParts.Add(flatTipLabel);

                // Check if tooltip is multi-line for expandable children
                string[] rawTipLines = fullTip.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                var tipLines = ConsolidateTipLines(rawTipLines);
                if (tipLines.Count > 1)
                {
                    var preceptNode = new InspectionTreeItem
                    {
                        Label = combinedText,
                        IndentLevel = 1,
                        IsExpandable = true,
                        IsExpanded = false,
                        Parent = sectionNode,
                        Type = InspectionTreeItem.ItemType.SubCategory,
                        Data = preceptDef
                    };

                    foreach (string line in tipLines)
                    {
                        string trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed))
                            continue;

                        object childData = GetChildData(trimmed, precept, abilityDefMap);
                        string label = trimmed.StripTags();

                        // Append apparel description for required apparel items
                        if (childData is ThingDef td && td.IsApparel && !string.IsNullOrEmpty(td.description))
                            label += ". " + td.description;

                        AddChildNode(preceptNode, label, childData);
                    }

                    sectionNode.Children.Add(preceptNode);
                }
                else
                {
                    // Single-line tip: simple leaf
                    AddChildNode(sectionNode, combinedText, preceptDef);
                }
            }

            // Section label: "CategoryLabel. [precept1 tipLabel]. [precept2 tipLabel]..."
            var sb = new StringBuilder(categoryLabel);
            foreach (string part in aggregatedParts)
                AppendSentence(sb, part);
            sectionNode.Label = sb.ToString();

            root.Children.Add(sectionNode);
        }

        private static void BuildAppearanceSection(InspectionTreeItem root, Ideo ideo)
        {
            if (ideo.style == null)
                return;

            int hairCount = ideo.style.NumHairAndBeardStylesAvailable;
            int tattooCount = ideo.style.NumTattooStylesAvailable;

            string hairSummary = "HairAndBeards".Translate() + ": " + "NumAvailable".Translate(hairCount.ToString())
                + ". " + "HairAndBeardsDesc".Translate();
            string tattooSummary = "Tattoos".Translate() + ": " + "NumAvailable".Translate(tattooCount.ToString())
                + ". " + "TattoosDesc".Translate();

            // Section node — label is just summary counts (not all style names)
            var sectionNode = new InspectionTreeItem
            {
                Label = "Appearance".Translate().CapitalizeFirst() + ". " + hairSummary + ". " + tattooSummary,
                IndentLevel = 0,
                IsExpandable = true,
                IsExpanded = false,
                Parent = root,
                Type = InspectionTreeItem.ItemType.Category
            };

            // Hair and Beards (level 1, expandable)
            var hairNode = new InspectionTreeItem
            {
                Label = hairSummary,
                IndentLevel = 1,
                IsExpandable = true,
                IsExpanded = false,
                Parent = sectionNode,
                Type = InspectionTreeItem.ItemType.SubCategory
            };
            BuildStyleItemTypeNode(hairNode, ideo, "Hair".Translate(),
                DefDatabase<HairDef>.AllDefs.Cast<StyleItemDef>(), 2);
            BuildStyleItemTypeNode(hairNode, ideo, "Beard".Translate(),
                DefDatabase<BeardDef>.AllDefs.Cast<StyleItemDef>(), 2);
            sectionNode.Children.Add(hairNode);

            // Tattoos (level 1, expandable)
            var tattooNode = new InspectionTreeItem
            {
                Label = tattooSummary,
                IndentLevel = 1,
                IsExpandable = true,
                IsExpanded = false,
                Parent = sectionNode,
                Type = InspectionTreeItem.ItemType.SubCategory
            };
            BuildStyleItemTypeNode(tattooNode, ideo, "TattooFace".Translate(),
                DefDatabase<TattooDef>.AllDefs.Where(t => t.tattooType == TattooType.Face).Cast<StyleItemDef>(), 2);
            BuildStyleItemTypeNode(tattooNode, ideo, "TattooBody".Translate(),
                DefDatabase<TattooDef>.AllDefs.Where(t => t.tattooType == TattooType.Body).Cast<StyleItemDef>(), 2);
            sectionNode.Children.Add(tattooNode);

            root.Children.Add(sectionNode);
        }

        /// <summary>
        /// Builds a sub-node for a style item type (e.g., Hair, Beards, Face Tattoos)
        /// grouped by category, with individual items sorted by frequency (most used first).
        /// </summary>
        private static void BuildStyleItemTypeNode(InspectionTreeItem parent, Ideo ideo,
            string typeLabel, IEnumerable<StyleItemDef> allDefs, int baseIndent)
        {
            var typeNode = new InspectionTreeItem
            {
                Label = typeLabel,
                IndentLevel = baseIndent,
                IsExpandable = true,
                IsExpanded = false,
                Parent = parent,
                Type = InspectionTreeItem.ItemType.SubCategory
            };

            // Group by category, sort categories alphabetically
            var byCategory = allDefs
                .Where(d => d.StyleItemCategory != null)
                .GroupBy(d => d.StyleItemCategory)
                .OrderBy(g => g.Key.LabelCap.Resolve())
                .ToList();

            // Frequency sort order: Frequent(5) > Common(4) > Normal(3) > Uncommon(2) > Rare(1) > Never(0)
            foreach (var group in byCategory)
            {
                var catNode = new InspectionTreeItem
                {
                    Label = group.Key.LabelCap.Resolve(),
                    IndentLevel = baseIndent + 1,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = typeNode,
                    Type = InspectionTreeItem.ItemType.SubCategory
                };

                var sortedItems = group
                    .OrderByDescending(d => (int)ideo.style.GetFrequency(d))
                    .ThenBy(d => d.LabelCap.Resolve())
                    .ToList();

                foreach (var item in sortedItems)
                {
                    string itemLabel = BuildStyleItemLabel(ideo, item);
                    catNode.Children.Add(new InspectionTreeItem
                    {
                        Label = itemLabel,
                        IndentLevel = baseIndent + 2,
                        IsExpandable = false,
                        IsExpanded = false,
                        Parent = catNode,
                        Type = InspectionTreeItem.ItemType.DetailText
                    });
                }

                typeNode.Children.Add(catNode);
            }

            // Also handle items with no category
            var uncategorized = allDefs.Where(d => d.StyleItemCategory == null).ToList();
            if (uncategorized.Count > 0)
            {
                foreach (var item in uncategorized
                    .OrderByDescending(d => (int)ideo.style.GetFrequency(d))
                    .ThenBy(d => d.LabelCap.Resolve()))
                {
                    string itemLabel = BuildStyleItemLabel(ideo, item);
                    typeNode.Children.Add(new InspectionTreeItem
                    {
                        Label = itemLabel,
                        IndentLevel = baseIndent + 1,
                        IsExpandable = false,
                        IsExpanded = false,
                        Parent = typeNode,
                        Type = InspectionTreeItem.ItemType.DetailText
                    });
                }
            }

            parent.Children.Add(typeNode);
        }

        private static string BuildStyleItemLabel(Ideo ideo, StyleItemDef item)
        {
            var sb = new StringBuilder(item.LabelCap.Resolve());
            sb.Append(", " + ideo.style.GetFrequency(item).GetLabel());

            // Beards have no gender restriction; hair and tattoos do
            if (!(item is BeardDef))
            {
                StyleGender gender = ideo.style.GetGender(item);
                if (gender == StyleGender.Male || gender == StyleGender.MaleUsually)
                    sb.Append(", " + Gender.Male.GetLabel().CapitalizeFirst());
                else if (gender == StyleGender.Female || gender == StyleGender.FemaleUsually)
                    sb.Append(", " + Gender.Female.GetLabel().CapitalizeFirst());
            }

            // Trailing visual description so the player knows what the style looks like.
            string description = StyleDescriptionHelper.Describe(item);
            if (!string.IsNullOrEmpty(description))
                sb.Append(". " + description);

            return sb.ToString();
        }

        #endregion

        #region Tree Building Utilities

        /// <summary>
        /// Creates a section node at level 0 whose label aggregates all children's text.
        /// Children are added as non-expandable leaves at level 1.
        /// </summary>
        private static InspectionTreeItem CreateSectionNode(InspectionTreeItem root,
            string sectionName, List<string> childTexts)
        {
            var node = new InspectionTreeItem
            {
                IndentLevel = 0,
                IsExpandable = true,
                IsExpanded = false,
                Parent = root,
                Type = InspectionTreeItem.ItemType.Category,
                ExpandedLabel = sectionName
            };

            // Build aggregated label
            var sb = new StringBuilder(sectionName);
            foreach (string text in childTexts)
                AppendSentence(sb, text);
            node.Label = sb.ToString();

            // Add children
            foreach (string text in childTexts)
            {
                AddChildNode(node, text);
            }

            return node;
        }

        private static void AddChildNode(InspectionTreeItem parent, string label, object data = null)
        {
            parent.Children.Add(new InspectionTreeItem
            {
                Label = label,
                IndentLevel = parent.IndentLevel + 1,
                IsExpandable = false,
                IsExpanded = false,
                Parent = parent,
                Type = InspectionTreeItem.ItemType.DetailText,
                Data = data
            });
        }

        /// <summary>
        /// Extracts a Def from a precept for info card display, returning only
        /// Def types that produce useful info cards (ThingDef, XenotypeDef).
        /// Returns null for precept types whose info cards only repeat description text.
        /// </summary>
        private static Def GetPreceptDef(Precept precept)
        {
            if (precept is Precept_ThingDef ptd && ptd.ThingDef != null)
                return ptd.ThingDef;
            if (precept is Precept_Apparel pa && pa.apparelDef != null)
                return pa.apparelDef;
            if (precept is Precept_Xenotype px && px.xenotype != null)
                return px.xenotype;
            if (precept is Precept_Weapon)
                return null;
            return null;
        }

        /// <summary>
        /// Builds a mapping of tip-line ability names to AbilityDefs for a precept.
        /// Uses same label logic as EnhanceWithAbilityDescriptions.
        /// </summary>
        private static Dictionary<string, AbilityDef> BuildAbilityDefMap(List<AbilityDef> abilities, Ideo ideo)
        {
            var map = new Dictionary<string, AbilityDef>(StringComparer.OrdinalIgnoreCase);
            foreach (var abilityDef in abilities)
            {
                var ritualComp = abilityDef.comps?.FirstOrDefault(c => c is CompProperties_AbilityStartRitual)
                    as CompProperties_AbilityStartRitual;

                if (ritualComp != null && ideo != null)
                {
                    foreach (Precept p in ideo.PreceptsListForReading)
                    {
                        if (p is Precept_Ritual ritual && ritual.def == ritualComp.ritualDef)
                        {
                            map[ritual.LabelCap.StripTags()] = abilityDef;
                            break;
                        }
                    }
                }
                else
                {
                    map[abilityDef.LabelCap.Resolve().StripTags()] = abilityDef;
                }
            }
            return map;
        }

        /// <summary>
        /// Enhances ability name lines in a precept tip with their descriptions.
        /// Lines like "- Convert" become "- Convert. Attempt to convert a pawn..."
        /// For ritual abilities, the tip line shows the ritual precept label,
        /// so we map ritual labels to the ability description.
        /// </summary>
        /// <summary>
        /// Injects each granted ability's description inline with its bullet line in a role/ritual
        /// precept tip (the vanilla tip lists ability NAMES only). Shared with the IdeoBuilder typed-
        /// precept editor (<see cref="IdeoTypedPreceptState"/>) so the editor and the read-only viewer
        /// present abilities identically. Preserves every non-ability line unchanged.
        /// </summary>
        internal static string EnhanceWithAbilityDescriptions(List<AbilityDef> abilities, Ideo ideo, string tip)
        {
            var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var abilityDef in abilities)
            {
                if (string.IsNullOrEmpty(abilityDef.description))
                    continue;

                var ritualComp = abilityDef.comps?.FirstOrDefault(c => c is CompProperties_AbilityStartRitual)
                    as CompProperties_AbilityStartRitual;

                if (ritualComp != null && ideo != null)
                {
                    // Ritual ability: tip line shows ritual precept label
                    foreach (Precept p in ideo.PreceptsListForReading)
                    {
                        if (p is Precept_Ritual ritual && ritual.def == ritualComp.ritualDef)
                        {
                            string ritualLabel = ritual.LabelCap.StripTags();
                            descriptions[ritualLabel] = abilityDef.description;
                            break;
                        }
                    }
                }
                else
                {
                    // Normal ability: tip line shows ability label
                    string label = abilityDef.LabelCap.Resolve().StripTags();
                    descriptions[label] = abilityDef.description;
                }
            }

            if (descriptions.Count == 0)
                return tip;

            var lines = tip.Split('\n');
            var result = new StringBuilder();
            string abilitiesHeader = "RoleGrantedAbilitiesLabel".Translate().Resolve().StripTags();
            bool inAbilitiesSection = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) result.Append('\n');
                string line = lines[i];
                string trimmed = line.TrimStart();

                // Track section transitions — only enhance under "Abilities:" header
                string strippedLine = trimmed.StripTags().TrimEnd();
                if (strippedLine.EndsWith(":") && !strippedLine.StartsWith("- "))
                {
                    string sectionName = strippedLine.TrimEnd(':').Trim();
                    inAbilitiesSection = sectionName == abilitiesHeader;
                }

                if (inAbilitiesSection && trimmed.StartsWith("- "))
                {
                    string name = trimmed.Substring(2).Trim();
                    if (descriptions.TryGetValue(name, out string desc))
                    {
                        result.Append(line + ". " + desc.Replace("\r", "").Replace("\n", " ").Trim());
                        continue;
                    }
                }
                result.Append(line);
            }
            return result.ToString();
        }

        /// <summary>
        /// Consolidates split tip lines by merging section headers with their items.
        /// Single-item sections become "Header: Item". "Has role in rituals" always
        /// comma-separates. Non-bullet text after a header merges into the header.
        /// Multi-item sections are kept as header + individual bullet lines.
        /// </summary>
        private static List<string> ConsolidateTipLines(string[] lines)
        {
            var result = new List<string>();
            string ritualsHeader = "RoleRitualsLabel".Translate().Resolve().StripTags();

            int i = 0;
            while (i < lines.Length)
            {
                string current = lines[i].Trim();
                if (string.IsNullOrEmpty(current)) { i++; continue; }

                // Check if this line is a section header (ends with ":")
                if (current.StripTags().TrimEnd().EndsWith(":"))
                {
                    string headerName = current.StripTags().TrimEnd().TrimEnd(':').Trim();

                    var bulletItems = new List<string>();
                    string nonBulletMerge = null;
                    int j = i + 1;

                    while (j < lines.Length)
                    {
                        string next = lines[j].Trim();
                        if (string.IsNullOrEmpty(next)) { j++; continue; }

                        string nextTrimStart = next.TrimStart();
                        if (nextTrimStart.StartsWith("- "))
                        {
                            bulletItems.Add(nextTrimStart.Substring(2).Trim());
                            j++;
                        }
                        else if (bulletItems.Count > 0 && !next.StripTags().TrimEnd().EndsWith(":"))
                        {
                            // Continuation text after a bullet item (e.g., orphaned ability
                            // description) — merge with the previous bullet item
                            bulletItems[bulletItems.Count - 1] += ". " + next.Trim();
                            j++;
                        }
                        else if (bulletItems.Count == 0 && !next.StripTags().TrimEnd().EndsWith(":"))
                        {
                            nonBulletMerge = next;
                            j++;
                            break;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (nonBulletMerge != null)
                    {
                        result.Add(headerName + ": " + nonBulletMerge);
                    }
                    else if (bulletItems.Count == 0)
                    {
                        result.Add(current);
                    }
                    else if (headerName == ritualsHeader)
                    {
                        result.Add(headerName + ": " + string.Join(", ", bulletItems));
                    }
                    else if (bulletItems.Count == 1)
                    {
                        result.Add(headerName + ": " + bulletItems[0]);
                    }
                    else
                    {
                        result.Add(current);
                        foreach (string item in bulletItems)
                            result.Add("- " + item);
                    }

                    i = j;
                }
                else
                {
                    result.Add(current);
                    i++;
                }
            }

            return result;
        }

        /// <summary>
        /// Determines the appropriate Data object for a consolidated tip line.
        /// Handles ability lines (AbilityDef), weapon lines (List of ThingDef),
        /// and "Has role in rituals" lines (List of AbilityDef).
        /// </summary>
        private static object GetChildData(string line, Precept precept,
            Dictionary<string, AbilityDef> abilityDefMap)
        {
            // Weapon lines: "Noble: Wood, Club, ..." or "Despised: Persona monosword, ..."
            if (precept is Precept_Weapon pw)
            {
                string noblePrefix = "Noble".Translate().Resolve().StripTags() + ":";
                string despisedPrefix = "Despised".Translate().Resolve().StripTags() + ":";

                if (line.StartsWith(noblePrefix, StringComparison.OrdinalIgnoreCase) && pw.noble != null)
                    return GetWeaponThingDefs(pw.noble);
                if (line.StartsWith(despisedPrefix, StringComparison.OrdinalIgnoreCase) && pw.despised != null)
                    return GetWeaponThingDefs(pw.despised);
            }

            // "Has role in rituals: Name1, Name2, ..." line
            string ritualsHeader = "RoleRitualsLabel".Translate().Resolve().StripTags();
            if (line.StartsWith(ritualsHeader + ":") && abilityDefMap != null)
            {
                string namesPart = line.Substring(ritualsHeader.Length + 1).Trim();
                var names = namesPart.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                var defs = new List<Def>();
                foreach (string name in names)
                {
                    string trimmedName = name.Trim();
                    if (abilityDefMap.TryGetValue(trimmedName, out AbilityDef aDef))
                        defs.Add(aDef);
                }
                return defs.Count > 0 ? (object)defs : null;
            }

            // Ability lines: "- Name. Description"
            if (abilityDefMap != null && line.StartsWith("- "))
            {
                string abilityPart = line.Substring(2).Trim();
                int dotIndex = abilityPart.IndexOf(". ");
                string abilityName = dotIndex >= 0 ? abilityPart.Substring(0, dotIndex) : abilityPart;
                if (abilityDefMap.TryGetValue(abilityName, out AbilityDef aDef))
                    return aDef;
            }

            // Role required apparel lines in two formats:
            // Multi-item: "- Cape", "- Visage mask" (raw bullet items)
            // Single-item (consolidated): "Required apparel: Broadwrap"
            if (precept is Precept_Role role && !role.apparelRequirements.NullOrEmpty())
            {
                string apparelName = null;
                if (line.StartsWith("- "))
                {
                    apparelName = line.Substring(2).Trim();
                }
                else
                {
                    string apparelHeader = "RoleRequiredApparelLabel".Translate().Resolve().StripTags();
                    if (line.StartsWith(apparelHeader + ":", StringComparison.OrdinalIgnoreCase))
                        apparelName = line.Substring(apparelHeader.Length + 1).Trim();
                }

                if (apparelName != null)
                {
                    foreach (var req in role.apparelRequirements)
                    {
                        if (!req.requirement.groupLabel.NullOrEmpty()
                            && string.Equals(req.requirement.groupLabel, apparelName, StringComparison.OrdinalIgnoreCase))
                        {
                            ThingDef first = req.requirement.AllRequiredApparel().FirstOrDefault();
                            if (first != null) return first;
                        }
                        foreach (ThingDef apparel in req.requirement.AllRequiredApparel())
                        {
                            if (string.Equals(apparel.LabelCap.Resolve().StripTags(), apparelName, StringComparison.OrdinalIgnoreCase))
                                return apparel;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Looks up all weapon ThingDefs belonging to a WeaponClassDef.
        /// Matches the filter used by Precept_Weapon.GetTip() for consistency.
        /// </summary>
        private static List<Def> GetWeaponThingDefs(WeaponClassDef weaponClass)
        {
            return DefDatabase<ThingDef>.AllDefs
                .Where(x => x.IsWeapon && !x.weaponClasses.NullOrEmpty()
                    && x.weaponClasses.Contains(weaponClass)
                    && x.canGenerateDefaultDesignator)
                .Cast<Def>()
                .ToList();
        }

        /// <summary>
        /// Appends text as a new sentence, ensuring no double periods.
        /// </summary>
        public static void AppendSentence(StringBuilder sb, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // Trim leading periods/whitespace to prevent ". ." patterns
            text = text.TrimStart('.', ' ');
            if (string.IsNullOrEmpty(text))
                return;

            if (sb.Length > 0)
            {
                char lastChar = sb[sb.Length - 1];
                if (lastChar != '.' && lastChar != '!' && lastChar != '?')
                    sb.Append('.');
                sb.Append(' ');
            }

            sb.Append(text);
        }

        #endregion
    }
}
